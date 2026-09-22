using FsBank.Scanner.Capture;
using FsBank.Scanner.Game;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Input;
using FsBank.Scanner.Ocr;
using FsBank.Scanner.Runtime;
using FsBank.Scanner.Scanning.Batch;

using static FsBank.Scanner.Game.BankGeometry;
using static FsBank.Scanner.Scanning.ScanConstants;
using static FsBank.Scanner.Imaging.DetectionConstants;

using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace FsBank.Scanner.Scanning;

internal sealed class ItemScanner(Action<string> log, OcrPipeline? ocr = null)
{
    // One capture device per batch; disposed even when a tab fails or is cancelled.
    private sealed class CaptureContext : IDisposable
    {
        private DesktopCapture? capture;
        private Rectangle client;
        public Vision Vision { get; } = new();
        public Point? ExpectedCursor { get; set; }
        public DesktopCapture Get(Rectangle bounds)
        {
            if(capture is not null && client!=bounds)
                throw new InvalidOperationException("The game window moved between tabs. Capture stopped.");
            if(capture is null){capture=new DesktopCapture(bounds);client=bounds;}
            return capture;
        }
        public void Dispose() => capture?.Dispose();
    }
    private sealed record CellResult(int Row,int Column,string Status,string? File,Rectangle? Tooltip,double Milliseconds,CellTiming? Timing=null);
    private sealed class CellTiming
    {
        public double CaptureMs {get;set;}
        public double SearchMs {get;set;}
        public double ParkMs {get;set;}
        public double QueueWaitMs {get;set;}
        public double TotalMs {get;set;}
        public int SearchCalls {get;set;}
        public int TrackCalls {get;set;}
        public int FullSearchCalls {get;set;}
        public long Frames {get;set;}
    }
    public void RunAll(string root,int hoverMs,CancellationToken token)
    {
        nint game=Native.FindGame();
        Native.GetCursorPos(out var batchCursor);
        using var context=new CaptureContext();
        BatchScan.Run(root,(tab,folder)=>
        {
            // Only the first tab may activate the game. Do not steal focus back between tabs.
            if(tab>0 && Native.GetForegroundWindow()!=game)throw new OperationCanceledException("The game lost focus between tabs.");
            if(tab>0 && Native.GetCursorPos(out var current) && (Math.Abs(current.X-batchCursor.X)>CursorTolerancePx || Math.Abs(current.Y-batchCursor.Y)>CursorTolerancePx))
                throw new OperationCanceledException("The mouse moved between tabs.");
            RunCore(root,hoverMs,token,tab,folder,context);
        },log,token);
    }
    public void RunEverything(string root,int hoverMs,CancellationToken token)
    {
        nint game=Native.FindGame();
        Native.GetCursorPos(out var originalCursor);
        using var context=new CaptureContext();
        bool first=true;
        try
        {
            BatchScan.Run(root,(index,folder)=>
            {
                token.ThrowIfCancellationRequested();
                if(!first)
                {
                    if(Native.GetForegroundWindow()!=game ||
                        (context.ExpectedCursor is Point expected && Native.GetCursorPos(out var current) &&
                        (Math.Abs(current.X-expected.X)>CursorTolerancePx || Math.Abs(current.Y-expected.Y)>CursorTolerancePx)))
                        throw new OperationCanceledException("Focus or mouse changed between scan locations.");
                    // Let the last park and Alt release render before detecting the next panel.
                    if(token.WaitHandle.WaitOne(InitialParkMs))token.ThrowIfCancellationRequested();
                }
                var target=index==-2 ? ScanTarget.Equipped : index==-1 ? ScanTarget.Inventory : ScanTarget.Bank;
                RunCore(root,hoverMs,token,target==ScanTarget.Bank ? index : null,folder,context,target,restoreCursor:false);
                first=false;
            },log,token,everything:true);
        }
        finally
        {
            // Restore only once, and never override intervening user input.
            if(context.ExpectedCursor is Point expected && Native.GetForegroundWindow()==game &&
                Native.GetCursorPos(out var current) && current==expected)
                Native.SetCursorPos(originalCursor.X,originalCursor.Y);
        }
    }

    public void Run(string root,int hoverMs,CancellationToken token,ScanTarget target=ScanTarget.Bank)
    {
        using var context=new CaptureContext();
        RunCore(root,hoverMs,token,null,null,context,target);
    }
    private void RunCore(string root,int hoverMs,CancellationToken token,int? targetTab,string? sessionFolder,CaptureContext context,ScanTarget target=ScanTarget.Bank,bool restoreCursor=true)
    {
        var preparation=Stopwatch.StartNew();
        if(target!=ScanTarget.Bank && targetTab is not null)throw new ArgumentException("Only bank scans have tabs.");
        if(targetTab is <0 or >=TabCount)throw new ArgumentOutOfRangeException(nameof(targetTab));
        nint hwnd=Native.FindGame();
        if(hwnd==0)throw new InvalidOperationException("No visible fellowship-Win64-Shipping.exe window found.");
        bool needsActivation=Native.GetForegroundWindow()!=hwnd;
        if(needsActivation)Native.Activate(hwnd);
        var activate=Stopwatch.StartNew();
        while(Native.GetForegroundWindow()!=hwnd && activate.ElapsedMilliseconds<ActivationTimeoutMs){token.ThrowIfCancellationRequested();Thread.Sleep(ActivationPollMs);}
        if(Native.GetForegroundWindow()!=hwnd)throw new InvalidOperationException("Windows could not activate the game. Make sure the game window is accessible, then click a scan button to try again.");
        if(needsActivation)Thread.Sleep(ActivationSettleMs);
        var client=Native.ClientBounds(hwnd);
        var stage=Stopwatch.StartNew();
        var capture=context.Get(client);
        double captureSetupMs=stage.Elapsed.TotalMilliseconds;
        long initialFrames=capture.Frames;
        var vision=context.Vision;
        stage.Restart();
        // A reused duplication may hold a frame from the previous tab.
        var initial=Next(capture,client,token,Stopwatch.GetTimestamp());
        double initialFrameMs=stage.Elapsed.TotalMilliseconds;
        stage.Restart();
        var fullLayout=vision.FindLayout(initial,target);
        while(fullLayout is null && !restoreCursor && stage.ElapsedMilliseconds<TooltipClearTimeoutMs)
        {
            token.ThrowIfCancellationRequested();
            if(Native.GetForegroundWindow()!=hwnd || Native.ClientBounds(hwnd)!=client ||
                (context.ExpectedCursor is Point expected && Native.GetCursorPos(out var current) &&
                (Math.Abs(current.X-expected.X)>CursorTolerancePx || Math.Abs(current.Y-expected.Y)>CursorTolerancePx)))
                throw new OperationCanceledException("Focus or mouse changed while waiting for the next panel.");
            if(token.WaitHandle.WaitOne(ActivationPollMs))token.ThrowIfCancellationRequested();
            initial=Next(capture,client,token,Stopwatch.GetTimestamp());
            fullLayout=vision.FindLayout(initial,target);
        }
        double panelSearchMs=stage.Elapsed.TotalMilliseconds;
        if(fullLayout is null)
        {
            if(sessionFolder is not null)
            {
                Directory.CreateDirectory(sessionFolder);
                initial.Save(Path.Combine(sessionFolder,"panel-not-detected.png"));
            }
            throw new InvalidOperationException(target==ScanTarget.Bank ? "Stash not detected. Open the stash (STASH), then try again." : "Character panel not detected. Open the character window with all slots visible, then try again.");
        }
        log($"{fullLayout.LocationType} detected, scale {fullLayout.Scale:F2}. Checking {fullLayout.SlotCount} slots ({fullLayout.RowCount}×{fullLayout.ColumnCount}).");
        int offset=target==ScanTarget.Bank ? Math.Max(0,fullLayout.Bounds.Left-(int)(CaptureLeftMarginPx*fullLayout.Scale)) : 0;
        var region=new Rectangle(client.Left+offset,client.Top,client.Width-offset,client.Height);
        var layout=fullLayout.Translate(-offset,0);
        int? CurrentTab(Pixels frame) => layout is BankLayout bank ? Vision.ActiveTab(frame,bank) : null;
        Native.GetCursorPos(out var originalCursor);
        bool altPressed=false, altReleased=false;
        Point? expectedCursor=null;
        long lastInput=0;
        void Check()
        {
            if(altPressed && (Native.GetAsyncKeyState((int)Keys.LMenu)&Native.KeyDownMask)==0)
                throw new InvalidOperationException("Left Alt was released. Capture stopped to avoid mixing normal and detailed tooltips.");
            token.ThrowIfCancellationRequested();
            if(Native.GetForegroundWindow()!=hwnd || Native.ClientBounds(hwnd)!=client)
                throw new InvalidOperationException("The game window lost focus or moved. Capture stopped.");
            if((Native.GetAsyncKeyState((int)Keys.Escape)&Native.KeyDownMask)!=0 || (Native.GetAsyncKeyState((int)Keys.LButton)&Native.KeyDownMask)!=0 || (Native.GetAsyncKeyState((int)Keys.RButton)&Native.KeyDownMask)!=0)
                throw new OperationCanceledException("Stopped due to user input.");
            if(expectedCursor is Point p && Native.GetCursorPos(out var current) && (Math.Abs(p.X-current.X)>CursorTolerancePx || Math.Abs(p.Y-current.Y)>CursorTolerancePx))
                throw new OperationCanceledException("The user moved the mouse.");
        }
        void Move(Point local)
        {
            Check(); var target=new Point(local.X+region.Left,local.Y+region.Top);
            if(!Native.SetCursorPos(target.X,target.Y))throw new InvalidOperationException("Failed to move the mouse.");
            expectedCursor=target;context.ExpectedCursor=target;lastInput=Stopwatch.GetTimestamp();
        }
        var session=sessionFolder ?? Path.Combine(root,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+(target==ScanTarget.Bank ? "" : "-"+layout.LocationType));
        Directory.CreateDirectory(session);
        ocr?.Register(session);
        var results=new List<CellResult>();
        var queue=Channel.CreateBounded<(Pixels Pixels,string Path)>(new BoundedChannelOptions(SaveQueueCapacity){SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait});
        var saver=Task.Run(async()=>
        {
            try {await foreach(var item in queue.Reader.ReadAllAsync()) { item.Pixels.Save(item.Path); ocr?.Enqueue(item.Path); }}
            catch(Exception e){queue.Writer.TryComplete(e);throw;}
        });
        var total=Stopwatch.StartNew(); string status="completed";string? error=null;
        object? panelValidationFailure=null;
        int? activeTab=null;
        double selectionMs=0,readyMs=0;
        try
        {
            Move(layout.Park);Thread.Sleep(InitialParkMs);Check();
            var clearWatch=Stopwatch.StartNew(); Pixels baseline;
            do
            {
                Check();baseline=Next(capture,region,token,lastInput);
                if(vision.FindTooltip(baseline,layout.Scale) is null)break;
                if(clearWatch.ElapsedMilliseconds>TooltipClearTimeoutMs)throw new InvalidOperationException("Failed to dismiss the initial tooltip.");
            }while(true);
            baseline=baseline.Crop(baseline.Bounds);
            if(!vision.PanelStillOpen(baseline,layout))throw new InvalidOperationException("The scanned panel was closed.");
            activeTab=CurrentTab(baseline);
            if(activeTab<0)throw new InvalidOperationException("Failed to identify the active stash tab.");
            if(targetTab is int requested && layout is BankLayout bank)
            {
                Check();
                if((Native.GetAsyncKeyState((int)Keys.Menu)&Native.KeyDownMask)!=0)
                    throw new InvalidOperationException("Release Alt before switching tabs.");
                int clickAttempts=0;
                long tabReleasedAt=Stopwatch.GetTimestamp();
                var tabPreparation=Stopwatch.StartNew();
                void WaitOnTab(int milliseconds)
                {
                    if(token.WaitHandle.WaitOne(milliseconds))token.ThrowIfCancellationRequested();
                    Check();
                }
                void ClickTab()
                {
                    Move(bank.TabCenter(requested));
                    // Let the game process hover before pressing, and release before parking.
                    WaitOnTab(TabHoverSettleMs);
                    if(!Native.SetLeftMouse(true))throw new InvalidOperationException("Failed to press the mouse button to select the tab.");
                    try { if(token.WaitHandle.WaitOne(TabClickHoldMs))token.ThrowIfCancellationRequested(); }
                    finally
                    {
                        if(!Native.SetLeftMouse(false))throw new InvalidOperationException("Failed to release the mouse button. Press and release it manually.");
                    }
                    lastInput=Stopwatch.GetTimestamp();
                    tabReleasedAt=lastInput;
                    WaitOnTab(TabReleaseSettleMs);
                    Move(layout.Park);
                    clickAttempts++;
                    log($"Selecting tab {requested+1}: click {clickAttempts}/{TabClickAttempts}.");
                }
                if(activeTab!=requested)ClickTab();
                var transition=Stopwatch.StartNew();
                var sinceClick=Stopwatch.StartNew();
                Pixels? previousGrid=null;int stableFrames=0;
                while(true)
                {
                    Check();
                    if(token.WaitHandle.WaitOne(TabStabilityPollMs))token.ThrowIfCancellationRequested();
                    var frame=Next(capture,region,token,lastInput);
                    bool bankOpen=vision.BankStillOpen(frame,bank);
                    int observedTab=Vision.ActiveTab(frame,bank);
                    bool valid=bankOpen && observedTab==requested;
                    var grid=frame.Crop(layout.Grid);
                    stableFrames=valid && previousGrid is not null && previousGrid.Difference(grid,grid.Bounds,StabilityDifferenceStep)<MaximumStableDifference ? stableFrames+1 : 0;
                    previousGrid=valid ? grid : null;
                    // Full-frame tooltip search is expensive. Run it on the stable candidate,
                    // not on every intermediate frame; all checks still use the same image.
                    bool? tooltipPresent=null;
                    if(valid && Stopwatch.GetElapsedTime(tabReleasedAt).TotalMilliseconds>=TabSettleMs && stableFrames+1>=TabStableFrames)
                    {
                        tooltipPresent=vision.FindTooltip(frame,layout.Scale) is not null;
                        if(tooltipPresent==false){baseline=frame.Crop(frame.Bounds);activeTab=requested;break;}
                        previousGrid=null;stableFrames=0;
                    }
                    if(transition.ElapsedMilliseconds>=TabSwitchTimeoutMs)
                    {
                        frame.Save(Path.Combine(session,"tab-switch-failed.png"));
                        File.WriteAllText(Path.Combine(session,"tab-switch.json"),JsonSerializer.Serialize(new
                        {
                            RequestedTab=requested+1,ObservedTab=observedTab+1,BankOpen=bankOpen,
                            TooltipPresent=tooltipPresent,StableFrames=stableFrames+1,ClickAttempts=clickAttempts,
                            ClickPoint=bank.TabCenter(requested),CaptureRegion=region,
                            ElapsedMs=transition.ElapsedMilliseconds,TabHoverSettleMs,TabClickHoldMs,TabReleaseSettleMs
                        },new JsonSerializerOptions{WriteIndented=true}));
                        throw new InvalidOperationException($"Failed to confirm tab {requested+1}: detected {observedTab+1} (0 = not detected), STASH={bankOpen}, tooltip={tooltipPresent}, clicks={clickAttempts}. Diagnostics: tab-switch.json and tab-switch-failed.png. Capture stopped.");
                    }
                    // Retry only a recognized, open bank still showing another tab.
                    if(bankOpen && observedTab>=0 && observedTab!=requested
                        && clickAttempts<TabClickAttempts && sinceClick.ElapsedMilliseconds>=TabRetryAfterMs)
                    {
                        if(vision.FindTooltip(frame,layout.Scale) is not null)continue;
                        frame.Save(Path.Combine(session,$"tab-switch-before-retry-{clickAttempts+1}.png"));
                        ClickTab();sinceClick.Restart();previousGrid=null;stableFrames=0;
                    }
                }
                log($"Confirmed tab {activeTab+1}/{TabCount}; grid stable; selection and verification took {tabPreparation.ElapsedMilliseconds} ms, clicks {clickAttempts}.");
                selectionMs=tabPreparation.Elapsed.TotalMilliseconds;
            }
            baseline.Save(Path.Combine(session,"baseline.png"));
            var cells=layout.Slots().Select(slot=>(slot.Row,slot.Col,Occupied:Vision.Occupied(baseline,slot.Bounds))).ToArray();
            log($"Initially occupied: {cells.Count(c=>c.Occupied)}/{layout.SlotCount} cells.");
            log($"Build {BuildInfo.Configuration}; initial hover delay {hoverMs} ms; the following lines show frame capture/wait and tooltip search separately.");
            Check();
            if((Native.GetAsyncKeyState((int)Keys.Menu)&Native.KeyDownMask)!=0)
                throw new InvalidOperationException("Release Alt before starting capture.");
            if(!Native.SetLeftAlt(true))
                throw new InvalidOperationException("Failed to press Left Alt via SendInput. Check the privilege levels of the app and game.");
            altPressed=true;lastInput=Stopwatch.GetTimestamp();
            var altWait=Stopwatch.StartNew();
            while((Native.GetAsyncKeyState((int)Keys.LMenu)&Native.KeyDownMask)==0)
            {
                token.ThrowIfCancellationRequested();
                if(altWait.ElapsedMilliseconds>=DetailsKeyTimeoutMs)
                    throw new InvalidOperationException("Windows did not confirm that Left Alt was pressed.");
                Thread.Sleep(DetailsKeyPollMs);
            }
            log("Left Alt is held: capturing detailed tooltips.");
            readyMs=preparation.Elapsed.TotalMilliseconds;
            log($"Panel ready in {readyMs:F0} ms: capture setup {captureSetupMs:F0}, first frame {initialFrameMs:F0}, panel search {panelSearchMs:F0}, selection {selectionMs:F0}, other preparation {readyMs-captureSetupMs-initialFrameMs-panelSearchMs-selectionMs:F0} ms.");
            Tooltip? previous=null;
            foreach(var cell in cells)
            {
                Check();
                if(!cell.Occupied){results.Add(new(cell.Row+1,cell.Col+1,"empty",null,null,0));continue;}
                var cycle=Stopwatch.StartNew();var timing=new CellTiming();long startFrames=capture.Frames;
                Pixels Read(long notBefore=0)
                {
                    long started=Stopwatch.GetTimestamp();
                    var result=Next(capture,region,token,Math.Max(lastInput,notBefore));
                    timing.CaptureMs+=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    return result;
                }
                Move(layout.Park);
                var clear=Stopwatch.StartNew();
                Pixels before;
                bool bankOpen,tooltipPresent;
                int? observedTab;int validationFrames=0;
                double tabDifference;
                // A tooltip can relocate beside the parked cursor. Check that narrow
                // placement corridor as well as its old footer, avoiding a full-frame scan.
                // Keep the cursor parked while transient UI frames settle.
                while(true)
                {
                    Check();before=Read();validationFrames++;
                    tooltipPresent=previous is not null && vision.FooterPresent(before,layout.Scale,previous.Footer);
                    if(!tooltipPresent)
                        tooltipPresent=vision.FooterPresentNear(before,layout.Scale,layout.Park);
                    bankOpen=vision.PanelStillOpen(before,layout);
                    observedTab=CurrentTab(before);
                    tabDifference=before.Difference(baseline,layout.VerificationArea,TabDifferenceStep);
                    if(!tooltipPresent && bankOpen && observedTab==activeTab && tabDifference<=MaximumTabDifference)break;
                    if(clear.ElapsedMilliseconds>=TooltipClearTimeoutMs)break;
                }
                timing.ParkMs=clear.Elapsed.TotalMilliseconds;
                Check();
                if(tooltipPresent || !bankOpen || observedTab!=activeTab || tabDifference>MaximumTabDifference)
                {
                    var reasons=new List<string>();
                    if(tooltipPresent)reasons.Add("tooltip did not disappear after waiting");
                    if(!bankOpen)reasons.Add("Panel not detected");
                    if(observedTab!=activeTab)reasons.Add(observedTab<0 ? "active tab not detected" : "a different tab was detected");
                    if(tabDifference>MaximumTabDifference)reasons.Add("panel verification area changed");
                    string message=$"Failed to confirm panel readiness. Before cell {cell.Row+1}:{cell.Col+1}: {string.Join("; ",reasons)}. panel={bankOpen}; location={layout.LocationType}; tab={observedTab+1} (expected {activeTab+1}; blank = not applicable); difference={tabDifference:F3}, threshold={MaximumTabDifference:F3}; frames={validationFrames}, wait={timing.ParkMs:F0} ms.";
                    panelValidationFailure=new
                    {
                        Row=cell.Row+1,Column=cell.Col+1,Reasons=reasons,PanelDetected=bankOpen,
                        ExpectedTab=activeTab+1,ObservedTab=observedTab+1,TabNumbering="1-based; 0 means unrecognized",
                        TabDifference=tabDifference,MaximumTabDifference,TabDifferenceStep,
                        VerificationArea=layout.VerificationArea,PreviousTooltip=previous,Park=layout.Park,
                        TooltipPresent=tooltipPresent,ValidationFrames=validationFrames,WaitMs=timing.ParkMs,
                        MillisecondsSinceCursorMove=Stopwatch.GetElapsedTime(lastInput).TotalMilliseconds,
                        Frame="panel-check-failed.png",Baseline="baseline.png"
                    };
                    log(message);
                    // Preserve the exact rejected frame: a later capture can hide a transient failure.
                    try
                    {
                        before.Save(Path.Combine(session,"panel-check-failed.png"));
                        before.Crop(layout.VerificationArea).Save(Path.Combine(session,"panel-check-tabs.png"));
                        baseline.Crop(layout.VerificationArea).Save(Path.Combine(session,"panel-check-tabs-baseline.png"));
                        File.WriteAllText(Path.Combine(session,"panel-check.json"),JsonSerializer.Serialize(panelValidationFailure,new JsonSerializerOptions{WriteIndented=true}));
                        log("Diagnostics saved: panel-check.json, panel-check-failed.png, and two verification area images.");
                    }
                    catch(Exception diagnosticError)
                    {
                        log($"Failed to save all diagnostics: {diagnosticError.Message}");
                    }
                    throw new InvalidOperationException(message);
                }
                var rect=layout.Cell(cell.Row,cell.Col);var hover=new Point(rect.X+rect.Width/2,rect.Y+rect.Height/2);Move(hover);
                var watch=Stopwatch.StartNew();
                Pixels? candidate=null;Pixels? lastTooltipFrame=null;Tooltip? located=null;int stable=0;bool saved=false;int headerRetries=0;bool incompleteHeader=false;
                // Delay the first probe to avoid polling before the tooltip can appear.
                // A queued frame rendered AFTER the hover is valid even if it predates
                // the end of this sleep; requiring a later timestamp wastes another frame.
                // Read still rejects pre-input frames and stability requires two frames.
                if(token.WaitHandle.WaitOne(hoverMs))token.ThrowIfCancellationRequested();
                long nextFullSearch=FirstFullSearchMs;
                while(watch.ElapsedMilliseconds<TooltipTimeoutMs)
                {
                    Check();var frame=Read();lastTooltipFrame=frame;
                    long searchStart=Stopwatch.GetTimestamp();Tooltip? found=null;
                    if(located is not null)
                    {
                        timing.TrackCalls++;found=vision.TrackTooltip(frame,layout.Scale,located);
                    }
                    if(found is null)
                    {
                        timing.SearchCalls++;found=vision.FindTooltipNear(frame,layout.Scale,hover);
                    }
                    if(found is null && watch.ElapsedMilliseconds>=nextFullSearch)
                    {
                        // Exceptional placement/UI changes retain a broad fallback,
                        // throttled so an absent tooltip never triggers it each frame.
                        timing.FullSearchCalls++;found=vision.FindTooltip(frame,layout.Scale);
                        nextFullSearch=watch.ElapsedMilliseconds+FullSearchIntervalMs;
                    }
                    timing.SearchMs+=Stopwatch.GetElapsedTime(searchStart).TotalMilliseconds;
                    if(found is null){stable=0;located=null;candidate=null;continue;}
                    var crop=frame.Crop(found.Bounds);
                    stable=located?.Bounds==found.Bounds && candidate is not null && candidate.Difference(crop,crop.Bounds,StabilityDifferenceStep)<MaximumStableDifference ? stable+1 : 0;
                    candidate=crop;located=found;
                    if(stable+1<StableFrameCount)continue;
                    Check();
                    if(!TooltipSegmenter.HasCompleteTitle(crop,layout.Scale) || !TooltipSegmenter.HasReadableLayout(crop))
                    {
                        incompleteHeader=true;
                        frame.Save(Path.Combine(session,$"r{cell.Row+1:00}_c{cell.Col+1:00}-header-attempt-{headerRetries+1}.png"));
                        if(headerRetries>=2)break;
                        headerRetries++;
                        log($"{cell.Row+1}:{cell.Col+1}: header cropped or text layout incomplete; retry {headerRetries}/2.");
                        Move(layout.Park);
                        if(token.WaitHandle.WaitOne(InitialParkMs))token.ThrowIfCancellationRequested();
                        var clearRetry=Stopwatch.StartNew();
                        while(true)
                        {
                            Check();var retryFrame=Read();
                            if(!vision.PanelStillOpen(retryFrame,layout) || CurrentTab(retryFrame)!=activeTab)
                                throw new InvalidOperationException("The scanned panel changed during recapture.");
                            if(!vision.FooterPresent(retryFrame,layout.Scale,found.Footer) && !vision.FooterPresentNear(retryFrame,layout.Scale,layout.Park))break;
                            if(clearRetry.ElapsedMilliseconds>=TooltipClearTimeoutMs)throw new InvalidOperationException("Failed to dismiss the tooltip for recapture.");
                        }
                        hover=new Point(rect.X+rect.Width/2,headerRetries==1 ? rect.Top+rect.Height/4 : rect.Bottom-rect.Height/4);
                        Move(hover);previous=null;located=null;candidate=null;stable=0;watch.Restart();nextFullSearch=FirstFullSearchMs;
                        if(token.WaitHandle.WaitOne(hoverMs))token.ThrowIfCancellationRequested();
                        continue;
                    }
                    string filename=$"r{cell.Row+1:00}_c{cell.Col+1:00}.png";
                    long queueStart=Stopwatch.GetTimestamp();
                    queue.Writer.WriteAsync((crop,Path.Combine(session,filename)),token).AsTask().GetAwaiter().GetResult();
                    timing.QueueWaitMs=Stopwatch.GetElapsedTime(queueStart).TotalMilliseconds;
                    timing.TotalMs=cycle.Elapsed.TotalMilliseconds;timing.Frames=capture.Frames-startFrames;
                    results.Add(new(cell.Row+1,cell.Col+1,"captured",filename,found.Bounds,watch.Elapsed.TotalMilliseconds,timing));
                    previous=found;saved=true;
                    log($"{cell.Row+1}:{cell.Col+1} → {filename} ({watch.ElapsedMilliseconds} ms; total {timing.TotalMs:F0}; capture {timing.CaptureMs:F0}; search {timing.SearchMs:F0}; parking/verification {timing.ParkMs:F0}; full searches {timing.FullSearchCalls})");break;
                }
                if(!saved)
                {
                    // Preserve the last hovered frame before parking overwrites
                    // the capture buffer, including failures without a footer match.
                    string diagnostic=$"r{cell.Row+1:00}_c{cell.Col+1:00}-tooltip-failed.png";
                    lastTooltipFrame?.Save(Path.Combine(session,diagnostic));
                    timing.TotalMs=cycle.Elapsed.TotalMilliseconds;timing.Frames=capture.Frames-startFrames;
                    results.Add(new(cell.Row+1,cell.Col+1,incompleteHeader ? "incomplete_tooltip" : "no_stable_tooltip",null,located?.Bounds,watch.Elapsed.TotalMilliseconds,timing));
                    log($"{cell.Row+1}:{cell.Col+1}: {(incompleteHeader ? "complete tooltip not confirmed" : "stable tooltip not found")} (total {timing.TotalMs:F0} ms; capture {timing.CaptureMs:F0}; search {timing.SearchMs:F0}; full searches {timing.FullSearchCalls}); diagnostic: {diagnostic}.");
                    // Clear any late tooltip, including one that did not reach stability.
                    Move(layout.Park);Thread.Sleep(FailedTooltipParkMs);var f=Read();
                    previous=vision.FindTooltip(f,layout.Scale);
                }
            }
            Check();Move(layout.Park);
        }
        catch(OperationCanceledException e){status="cancelled";error=e.Message;throw;}
        catch(Exception e){status="failed";error=e.Message;throw;}
        finally
        {
            // Release input before waiting for disk IO, writing metadata or restoring
            // the cursor. This also runs on cancellation and loss of game focus.
            if(altPressed)
            {
                altReleased=Native.SetLeftAlt(false);
                if(!altReleased)
                {
                    status="input_release_failed";error="SendInput failed to release Left Alt. Press and release Alt manually.";
                    log(error);
                }
            }
            queue.Writer.TryComplete();
            try {saver.GetAwaiter().GetResult();}
            catch(Exception e) {status="save_failed";error=e.Message;throw;}
            finally
            {
                File.WriteAllText(Path.Combine(session,"session.json"),JsonSerializer.Serialize(new
                {
                    LocationType=layout.LocationType,Status=status,Error=error,Build=BuildInfo.Configuration,GameClient=client,CaptureRegion=region,Layout=layout,Bank=layout as BankLayout,ElapsedMs=total.ElapsedMilliseconds,
                    CaptureFrames=capture.Frames-initialFrames,Cells=results,PanelValidationFailure=panelValidationFailure,Note="Tooltip rectangles are relative to CaptureRegion; rows and columns are 1-based; clicks only on bank tab buttons.",
                    Preparation=new {ReadyMs=readyMs,CaptureSetupMs=captureSetupMs,InitialFrameMs=initialFrameMs,PanelSearchMs=panelSearchMs,SelectionMs=selectionMs},
                    TooltipMode="alt_details",DetailsKey="LeftAlt",AltPressed=altPressed,AltReleased=altReleased,InitialHoverDelayMs=hoverMs
                    ,ActiveTab=activeTab+1,RequestedTab=targetTab+1
                },new JsonSerializerOptions{WriteIndented=true}));
                // Do not fight a user who moved the cursor or changed applications.
                if(restoreCursor && expectedCursor is Point p && Native.GetForegroundWindow()==hwnd && Native.GetCursorPos(out var current) && current==p)
                    Native.SetCursorPos(originalCursor.X,originalCursor.Y);
                log($"Saved {results.Count(x=>x.File is not null && File.Exists(Path.Combine(session,x.File)))}; folder: {session}");
            }
        }
    }
    private static Pixels Next(DesktopCapture capture,Rectangle area,CancellationToken token,long notBefore=0)
    {
        var wait=Stopwatch.StartNew();
        while(wait.ElapsedMilliseconds<CaptureConstants.NewFrameTimeoutMs)
        {
            token.ThrowIfCancellationRequested();var frame=capture.Next(area,notBefore:notBefore);
            if(frame is not null)return frame;
        }
        throw new InvalidOperationException("DXGI is not receiving new frames. Make sure the game is visible and running in borderless windowed mode.");
    }
}
