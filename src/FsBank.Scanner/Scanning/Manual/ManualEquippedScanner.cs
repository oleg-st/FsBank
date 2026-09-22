using FsBank.Scanner.Capture;
using FsBank.Scanner.Game;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Input;
using FsBank.Scanner.Ocr;
using FsBank.Scanner.Scanning;

using System.Diagnostics;
using System.Text.Json;
using static FsBank.Scanner.Scanning.ScanConstants;
using static FsBank.Scanner.Imaging.DetectionConstants;

namespace FsBank.Scanner.Scanning.Manual;

internal sealed class ManualEquippedScanner(Action<string> log)
{
    public void Run(string root,CancellationToken token,Action<Rectangle,ScanLayout,(int,int)[],(int,int)[]> update,Action<string>? showStatus=null)
    {
        nint game=Native.FindGame();
        if(game==0)throw new InvalidOperationException("Game window not found.");
        void Wait() { if(token.WaitHandle.WaitOne(15))token.ThrowIfCancellationRequested(); }
        log("Manual: switch to the game, open the character panel and move off items. Hold Left Alt yourself when hovering. Esc stops.");
        while(Native.GetForegroundWindow()!=game){token.ThrowIfCancellationRequested();Wait();}
        var preparation=Stopwatch.StartNew();
        var client=Native.ClientBounds(game);
        void Check()
        {
            token.ThrowIfCancellationRequested();
            if(Native.GetForegroundWindow()!=game || Native.ClientBounds(game)!=client)
                throw new OperationCanceledException("Game lost focus or moved.");
            if((Native.GetAsyncKeyState((int)Keys.Escape)&Native.KeyDownMask)!=0)
                throw new OperationCanceledException();
        }
        using var capture=new DesktopCapture(client);
        var vision=new Vision();
        var panelSearch=new ManualPanelSearch(vision);
        double setupMs=preparation.Elapsed.TotalMilliseconds,layoutMs=0,clearMs=0;
        Pixels Read()
        {
            var watch=Stopwatch.StartNew();long notBefore=Stopwatch.GetTimestamp();
            while(watch.ElapsedMilliseconds<CaptureConstants.NewFrameTimeoutMs)
            {
                Check();var frame=capture.Next(client,notBefore:notBefore);
                if(frame is not null)return frame;
            }
            throw new InvalidOperationException("No new game frames. Use borderless windowed mode.");
        }
        ScanLayout? layout=null;Pixels baseline;
        int clearFrames=0;
        while(true)
        {
            Wait();baseline=Read();
            long stage=Stopwatch.GetTimestamp();
            if(layout is null || !vision.PanelStillOpen(baseline,layout))layout=panelSearch.Find(baseline);
            layoutMs+=Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
            if(layout is null){clearFrames=0;continue;}
            stage=Stopwatch.GetTimestamp();
            // The tooltip follows the cursor. Wait off all character slots and
            // check its placement corridor instead of scanning the entire desktop.
            if(!Native.GetCursorPos(out var cursor)){clearFrames=0;continue;}
            cursor.Offset(-client.Left,-client.Top);
            var inventory=new InventoryLayout(layout.Bounds,layout.Anchor,layout.Scale);
            bool overItem=layout.Slots().Concat(inventory.Slots()).Any(s=>s.Bounds.Contains(cursor));
            bool clear=!overItem && !vision.FooterPresentNear(baseline,layout.Scale,cursor);
            clearMs+=Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
            clearFrames=clear ? clearFrames+1 : 0;
            if(clearFrames>=2)break;
        }
        string session=Path.Combine(root,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-equipped-manual");
        Directory.CreateDirectory(session);
        // DXGI pixels are borrowed. Detach before writing on the background worker.
        var baselineCopy=baseline.Crop(baseline.Bounds);
        var baselineSave=Task.Run(()=>baselineCopy.Save(Path.Combine(session,"baseline.png")));
        var pending=layout.Slots().Where(s=>Vision.Occupied(baseline,s.Bounds)).Select(s=>(s.Row,s.Col)).ToHashSet();
        var done=new HashSet<(int,int)>();
        var captured=new HashSet<(int,int)>();
        var pipeline=new OcrPipeline(log);
        pipeline.Register(session);
        var search=new ManualTooltipSearch(vision,layout.Scale);
        var timings=new List<object>();
        string status="running";string? error=null;var elapsed=Stopwatch.StartNew();
        double readyMs=0;
        string? lastReason=null;
        var waits=new Dictionary<string,int>();
        var diagnostics=new HashSet<string>();
        var reasonTime=Stopwatch.StartNew();
        void Waiting(string reason,string message,Pixels? frame=null)
        {
            waits[reason]=waits.GetValueOrDefault(reason)+1;
            if(lastReason!=reason){log("Manual: "+message);showStatus?.Invoke(message);lastReason=reason;reasonTime.Restart();}
            if(frame is not null && reasonTime.ElapsedMilliseconds>=2000 && diagnostics.Add(reason))
                frame.Save(Path.Combine(session,"waiting-"+reason+".png"));
        }
        void Save() => File.WriteAllText(Path.Combine(session,"session.json"),JsonSerializer.Serialize(new
        {
            Mode="equipped_manual",LocationType="equipped",Status=status,Error=error,GameClient=client,CaptureRegion=client,Layout=layout,
            TooltipMode="alt_details",DetailsKey="LeftAlt",InputMode="user_only",ElapsedMs=elapsed.ElapsedMilliseconds,LastWaitReason=lastReason,WaitCounts=waits,
            Timings=timings,Searches=new {search.FullSearches,search.LocalSearches,search.TrackSearches},
            Preparation=new {ReadyMs=readyMs,SetupMs=setupMs,LayoutMs=layoutMs,TooltipClearMs=clearMs,panelSearch.TilesSearched},
            Cells=layout.Slots().Select(s=>new {Row=s.Row+1,Column=s.Col+1,
                Status=done.Contains((s.Row,s.Col)) ? "captured" : captured.Contains((s.Row,s.Col)) ? "ocr_failed" : pending.Contains((s.Row,s.Col)) ? "pending" : "empty",
                File=captured.Contains((s.Row,s.Col)) ? $"r{s.Row+1:00}_c{s.Col+1:00}.png" : null})
        },new JsonSerializerOptions{WriteIndented=true}));
        try
        {
            Save();update(client,layout,pending.ToArray(),done.ToArray());
            readyMs=preparation.Elapsed.TotalMilliseconds;
            log($"Manual ready in {readyMs:F0} ms after game focus: setup {setupMs:F0}, panel search {layoutMs:F0}, clear check {clearMs:F0} ms; baseline PNG saves in background.");
            Pixels? candidate=null;Rectangle? candidateBounds=null;int stable=0;
            (int,int)? activeSlot=null;
            double captureTotalMs=0,searchTotalMs=0;int probes=0;
            bool waitingForPanel=false;
            var gate=new ManualTooltipGate();var hover=Stopwatch.StartNew();var altHold=Stopwatch.StartNew();bool altWasDown=false;
            log($"Manual ready: {pending.Count} occupied slots. Move directly between items while holding Left Alt. Green means saved; move on immediately. OCR runs in the background.");
            while(pending.Count>0)
            {
                Wait();Check();
                if(!Native.GetCursorPos(out var cursor))continue;
                cursor.Offset(-client.Left,-client.Top);
                var slot=layout.Slots().Where(s=>s.Bounds.Contains(cursor)).Select(s=>((int,int)?)(s.Row,s.Col)).FirstOrDefault();
                if(slot!=activeSlot)
                {
                    activeSlot=slot;candidate=null;candidateBounds=null;stable=0;hover.Restart();
                    captureTotalMs=0;searchTotalMs=0;probes=0;
                }
                long captureStart=Stopwatch.GetTimestamp();
                var frame=Read();
                captureTotalMs+=Stopwatch.GetElapsedTime(captureStart).TotalMilliseconds;
                long searchStart=Stopwatch.GetTimestamp();
                var detection=search.Find(frame,cursor);
                double searchMs=Stopwatch.GetElapsedTime(searchStart).TotalMilliseconds;
                searchTotalMs+=searchMs;probes++;
                if(!detection.Confirmed){candidate=null;stable=0;continue;}
                var tooltip=detection.Tooltip;
                bool checkPanel=waitingForPanel || (tooltip is null && slot is null && !search.PreviousFooterVisible(frame)
                    && !vision.FooterPresentNear(frame,layout.Scale,cursor));
                if(checkPanel && !ManualPanelPresence.IsVisible(vision,frame,baselineCopy,layout))
                {
                    waitingForPanel=true;candidate=null;candidateBounds=null;stable=0;
                    Waiting("panel_unconfirmed","Panel temporarily unconfirmed. Waiting for it to be visible in the same position...",frame);
                    continue;
                }
                if(waitingForPanel)
                {
                    waitingForPanel=false;candidate=null;candidateBounds=null;stable=0;hover.Restart();
                    Waiting("panel_restored","Panel visible again. Continue hovering red slots.");
                }
                bool altDown=(Native.GetAsyncKeyState((int)Keys.LMenu)&Native.KeyDownMask)!=0;
                if(!altDown || !altWasDown)altHold.Restart();
                altWasDown=altDown;
                gate.Observe(slot,cursor,tooltip?.Bounds,new Size(frame.Width,frame.Height),layout.Scale);
                string? waitReason=null;string waitMessage="";
                if(slot is null){waitReason="hover_item";waitMessage="Hover a red slot while holding Left Alt.";}
                else if(!pending.Contains(slot.Value)){waitReason="already_done";waitMessage="Saved! Hover another red slot. OCR runs in background.";}
                else if(!altDown){waitReason="hold_alt";waitMessage="Hold LEFT ALT yourself to capture the detailed tooltip.";}
                else if(tooltip is null){waitReason="no_tooltip";waitMessage="Waiting for a detectable tooltip. Keep hovering.";}
                else if(!gate.MatchesCursor || !vision.TooltipNearCursor(frame,layout.Scale,cursor,tooltip))
                {waitReason="cursor_alignment";waitMessage="Waiting for the tooltip to follow the cursor.";}
                if(waitReason is not null)
                {
                    Waiting(waitReason,waitMessage,slot is not null && waitReason!="settling" ? frame : null);
                    candidate=null;stable=0;continue;
                }
                var key=slot!.Value;
                if(tooltip is null)continue;
                var crop=frame.Crop(tooltip.Bounds);
                stable=candidateBounds==tooltip.Bounds && candidate is not null && candidate.Difference(crop,crop.Bounds,StabilityDifferenceStep)<MaximumStableDifference ? stable+1 : 0;
                candidate=crop;candidateBounds=tooltip.Bounds;
                if(stable+1<StableFrameCount){Waiting("unstable","Waiting for the tooltip to stop changing.",frame);continue;}
                // Accumulate stable frames during the short input-settle interval.
                if(altHold.ElapsedMilliseconds<150 || hover.ElapsedMilliseconds<100)
                {Waiting("settling","Hold still briefly...");continue;}
                if(!TooltipSegmenter.HasCompleteTitle(crop,layout.Scale)){Waiting("header","Full title not detected. Hover a little lower in this slot.",frame);continue;}
                Check();
                if(!Native.GetCursorPos(out var after) || !layout.Cell(key.Item1,key.Item2).Contains(new Point(after.X-client.Left,after.Y-client.Top))
                    || Math.Abs(after.X-client.Left-cursor.X)>4 || Math.Abs(after.Y-client.Top-cursor.Y)>4
                    || (Native.GetAsyncKeyState((int)Keys.LMenu)&Native.KeyDownMask)==0){candidate=null;stable=0;continue;}
                string file=Path.Combine(session,$"r{key.Item1+1:00}_c{key.Item2+1:00}.png");
                long saveStart=Stopwatch.GetTimestamp();
                crop.Save(file);
                captured.Add(key);
                pipeline.Enqueue(file);
                double saveMs=Stopwatch.GetElapsedTime(saveStart).TotalMilliseconds;
                timings.Add(new {Row=key.Item1+1,Column=key.Item2+1,HoverMs=hover.Elapsed.TotalMilliseconds,
                    CaptureMs=captureTotalMs,SearchMs=searchTotalMs,Probes=probes,LastSearchMs=searchMs,SaveAndQueueMs=saveMs});
                Waiting("saved","Saved! Move to the next item. OCR runs in background.");
                pending.Remove(key);done.Add(key);Save();
                update(client,layout,pending.ToArray(),done.ToArray());
                log($"Manual: {done.Count} saved; {pending.Count} remaining; hover {hover.ElapsedMilliseconds} ms, last search {searchMs:F0} ms, save/queue {saveMs:F0} ms.");
                candidate=null;stable=0;
            }
            status="completed";
        }
        catch(OperationCanceledException e){status="cancelled";error=e.Message;throw;}
        catch(Exception e){status="failed";error=e.Message;throw;}
        finally
        {
            Save();
            try { pipeline.Complete(); }
            catch(Exception e)
            {
                if(error is null){status="ocr_failed";error=e.Message;throw;}
                log("OCR error: "+e.Message);
            }
            finally
            {
                try { baselineSave.GetAwaiter().GetResult(); }
                catch(Exception e){status="save_failed";error=e.Message;throw;}
                finally { Save();log("Manual capture saved: "+session); }
            }
        }
    }
}

