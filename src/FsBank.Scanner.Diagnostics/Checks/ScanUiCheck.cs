using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;
using FsBank.Scanner.Game;
using FsBank.Scanner.Items;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using FsBank.Scanner.Scanning;
using FsBank.Scanner.Scanning.Batch;
using FsBank.Scanner.UI;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class ScanUiCheck
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        Progress(output);RecognitionWarnings();HeaderSegmentation();Selection(output);CancelledStart(output);OcrFailures(output);Render(output);
        File.WriteAllText(Path.Combine(output,"checks.txt"),"Passed: diagnostic-only prose warnings versus actionable item warnings, single-surface overlay updates without child handles, asynchronous per-area progress, duplicate callbacks, cross-tab identity, waiting/cancellation, selected-area and current-tab exports, timestamp-only folder names, cancelled Auto/Manual start without input, real OCR queue error routing/drain, manual restrictions and restored Auto selection, diagnostic checkbox defaults, Result tab creation/reuse, preserved tab selection during updates, layout fit at default/minimum size with expanded journal. UI previews rendered without game input.");
        Console.WriteLine("Scan UI checks passed: "+output);
    }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    private static void HeaderSegmentation()
    {
        var pixels=new Pixels(280,150);
        void Ink(int x,int y)
        {
            int i=pixels.Offset(x,y);pixels.Data[i]=pixels.Data[i+1]=pixels.Data[i+2]=200;
        }
        // Disconnected decorative strokes: together they span the header.
        for(int piece=0;piece<3;piece++)for(int x=0;x<70;x++)Ink(18+piece*80+x,10+x/7);
        // Separate, full-height glyphs spanning the same width must survive.
        for(int letter=0;letter<12;letter++)for(int x=0;x<10;x++)for(int y=30;y<44;y++)Ink(20+letter*20+x,y);
        // A short horizontal fragment alone is insufficient evidence of a border.
        for(int x=0;x<35;x++)for(int y=60;y<67;y++)Ink(30+x,y);
        var raw=TooltipSegmenter.Find(pixels);
        var filtered=TooltipSegmenter.Find(pixels,excludeHeaderDecoration:true);
        Require(raw.Count==3 && filtered.Count==2 && filtered.All(b=>b.Box.Top>=29)
            && filtered[0].Box==raw[1].Box && filtered[1].Box==raw[2].Box,
            "Header decoration filtering removed real glyphs or missed disconnected border strokes.");
    }
    private static void RecognitionWarnings()
    {
        RecognizedLine L(int index,string text,int confidence=99)=>new(index,new(1,index*20,300,16),"white",text,confidence);
        RecognizedLine[] lines=[L(0,"EXAMPLE ROBE"),L(1,"Chest 315"),L(2,"Rare Non Temperable"),
            L(3,"Power Potential 420"),L(4,"+5 Intellect"),L(5,"The item's potential power; derived from its",40),
            L(6,"ITEM MODIFIER (Slot: 1)"),L(7,"Blessing: The Wayfarer +1"),
            L(8,"(CORE]ability used. Abilities with no",40),L(9,"Each (CORE) ability grants haste."),
            L(10,"Left Alt - Show Details",40)];
        var item=TooltipParser.Parse(lines);
        Require(item.Warnings.Count==3 && item.ReviewWarnings.Count==0 && item.Modifiers.Single().Name=="The Wayfarer",
            "Diagnostic-only description/footer warnings became scan issues or lost their evidence.");
        foreach(int index in new[]{0,1,2,4,7})
        {
            var uncertain=lines.Select(line=>line.Index==index ? line with { Confidence=40 } : line).ToArray();
            Require(TooltipParser.Parse(uncertain).ReviewWarnings.Any(w=>w.StartsWith("Low confidence")),
                $"Low confidence in an exported field was hidden: {index}.");
        }
        var unresolved=TooltipParser.Parse(lines.Select(line=>line.Index==7 ? line with { Text="Unknown modifier" } : line).ToArray());
        Require(unresolved.ReviewWarnings.Contains("Unresolved modifier block") && unresolved.ReviewWarnings.Any(w=>w.StartsWith("Mismatched brackets")),
            "An unresolved modifier was incorrectly treated as harmless prose.");
        var missing=TooltipParser.Parse(lines.Where(line=>line.Index!=1 && line.Index!=10).ToArray());
        Require(missing.ReviewWarnings.Contains("Incomplete item metadata") && missing.ReviewWarnings.Any(w=>w.StartsWith("Footer not recognized")),
            "Missing item fields or capture footer no longer require review.");
        var unparsed=TooltipParser.Parse(lines.Select(line=>line.Index==5 ? line with { Text="+?? Haste" } : line).ToArray());
        Require(unparsed.ReviewWarnings.Any(w=>w.StartsWith("Additional text")),"An unreadable stat was hidden.");

        var traits=TooltipParser.Parse(lines[..5].Concat(new[]{L(5,"<} Imbued Traits (2/2)"),L(6,"UL The Mountain"),
            L(7,"L The Dragon"),L(8,"ITEM MODIFIER (Slot: 1)"),L(9,"Blessing: The Wayfarer +1"),L(10,"Left Alt - Show Details")}).ToArray());
        Require(traits.ReviewWarnings.Count==0 && traits.UnparsedLines.Count==0
            && traits.ImbuedTraits.SequenceEqual(new[]{new RecognizedImbuedTrait("The Mountain",6),new RecognizedImbuedTrait("The Dragon",7)})
            && traits.MetadataLines.Contains("<} Imbued Traits (2/2)") && traits.Modifiers.Single().Kind=="blessing",
            "Imbued traits were not structured separately or swallowed the next modifier.");
        var compactTraits=FsBank.Scanner.Export.CompactExport.Project(JsonSerializer.SerializeToElement(new { file="r01_c01.png",item=traits }),null,"equipped");
        Require(compactTraits["mods"]!.AsArray().Count==1 && !compactTraits.ContainsKey("ImbuedTraits")
            && !compactTraits.ToJsonString().Contains("The Mountain"),"Imbued traits leaked into compact export.");
        var uncertainTrait=TooltipParser.Parse(lines[..5].Concat(new[]{L(5,"Imbued Traits (1/1)"),L(6,"L The Mountain",40),L(7,"Left Alt - Show Details")}).ToArray());
        Require(uncertainTrait.ReviewWarnings.Count==0 && uncertainTrait.Warnings.Count>0,"Unexported imbued trait names raised issues or lost diagnostics.");
        var unknownAfterTraits=TooltipParser.Parse(lines[..5].Concat(new[]{L(5,"Imbued Traits (1/1)"),L(6,"L The Mountain"),L(7,"Unknown stat"),L(8,"Left Alt - Show Details")}).ToArray());
        Require(unknownAfterTraits.ReviewWarnings.Any(w=>w.StartsWith("Additional text")),"Imbued trait filtering hid unrelated text after the list.");
        var ornament=TooltipParser.Parse(new[]{L(0,"EXAMPLE ROBE"),new RecognizedLine(11,new(30,18,160,9),"mixed","eee",28)}.Concat(lines[1..]).ToArray());
        Require(ornament.ReviewWarnings.Count==0 && ornament.MetadataLines.Contains("eee"),"Title ornament produced a false issue.");
        var unknown=TooltipParser.Parse(new[]{L(0,"EXAMPLE ROBE"),L(11,"eee",28)}.Concat(lines[1..]).ToArray());
        Require(unknown.ReviewWarnings.Any(w=>w.StartsWith("Low confidence")),"Normal-sized unknown text was mistaken for a title ornament.");
        var topOrnament=TooltipParser.Parse(new[]{new RecognizedLine(11,new(14,9,250,9),"mixed","SSS SS SS",47)}.Concat(lines).ToArray());
        Require(topOrnament.Name=="EXAMPLE ROBE" && topOrnament.ReviewWarnings.Count==0,"Top ornament leaked into the title or scan issues.");
        var verified=TooltipParser.Parse(lines.Select(line=>line.Index==0 ? line with { Confidence=55,VerifiedConfidence=94 } : line).ToArray());
        Require(verified.ReviewWarnings.Count==0 && verified.Warnings.Any(w=>w.StartsWith("Low confidence")),"Confirmed OCR text lost raw diagnostics or still requires review.");
        var weakVerification=TooltipParser.Parse(lines.Select(line=>line.Index==0 ? line with { Confidence=55,VerifiedConfidence=58 } : line).ToArray());
        Require(weakVerification.ReviewWarnings.Any(w=>w.StartsWith("Low confidence")),"Weak OCR confirmation suppressed a real uncertainty.");
    }
    private static void Progress(string output)
    {
        var options=new ScanOptions(ScanMode.Auto,true,true,true,true);
        var tracker=new ScanProgressTracker(options,_=> { });
        string session=Path.Combine(output,"equipped"),bank1=Path.Combine(output,"bank1"),bank2=Path.Combine(output,"bank2");
        tracker.Scanning(ScanTarget.Equipped);tracker.RegisterSession(session,ScanTarget.Equipped,null);
        string file=Path.Combine(session,"r01_c01.png");
        tracker.Queued(file);tracker.CaptureComplete(ScanTarget.Equipped);
        Require(tracker.Snapshot.Areas[0].Phase==AreaPhase.Recognizing,"Capture must wait for OCR before becoming complete.");
        tracker.Waiting(ScanTarget.Bank,"Open your stash.");
        var result=new ItemRecognition.Result("r01_c01.png",new { },"",1);
        tracker.Recognized(file,result,null);tracker.Recognized(file,result,null);tracker.Queued(file);
        Require(tracker.Snapshot.Items==1 && tracker.Snapshot.Pending==0,"Duplicate callbacks inflated counts.");
        Require(tracker.Snapshot.Areas[0].Phase==AreaPhase.Completed && tracker.Snapshot.Phase==ScanPhase.Waiting,"Late OCR changed current waiting state.");
        tracker.Scanning(ScanTarget.Bank);tracker.RegisterSession(bank1,ScanTarget.Bank,1);
        string first=Path.Combine(bank1,"r02_c03.png"),second=Path.Combine(bank2,"r02_c03.png");
        tracker.Queued(first);tracker.RegisterSession(bank2,ScanTarget.Bank,2);tracker.Queued(second);
        tracker.Recognized(first,result with { Issue="Unreadable stat <value>" },null);
        Require(tracker.Snapshot.Areas[2].Tab==2 && tracker.Snapshot.Issues[0].Tab==1,"Late OCR confused bank tab identity.");
        tracker.Waiting(ScanTarget.Bank,"Open your stash.");
        Require(tracker.Snapshot.Pending==1 && tracker.Snapshot.Items==1,"Waiting reset counters.");
        tracker.Recognized(second,null,new IOException("Missing capture"));
        tracker.Finish(ScanPhase.Cancelled,"Stopped.");
        Require(tracker.Snapshot.Areas[0].Phase==AreaPhase.Completed && tracker.Snapshot.Areas[1].Phase==AreaPhase.Queued
            && tracker.Snapshot.Areas[2].Phase==AreaPhase.Cancelled && tracker.Snapshot.Pending==0,"Cancellation lost completed or pending area states.");
        Require(tracker.Snapshot.Issues.Length==2 && tracker.Snapshot.Issues[0].Row==2 && tracker.Snapshot.Issues[0].Column==3,"Issue coordinates were lost.");
        ScanController.WriteSummary(output,tracker.Snapshot);
        Require(!File.ReadAllText(Path.Combine(output,"scan-issues.html")).Contains("<value>"),"Issue text was not escaped.");

        var concurrent=new ScanProgressTracker(new(ScanMode.Auto,true,false,false,false),_=> { });
        concurrent.RegisterSession(session,ScanTarget.Equipped,null);
        var files=Enumerable.Range(1,100).Select(n=>Path.Combine(session,$"r{n:00}_c01.png")).ToArray();
        foreach(string path in files)concurrent.Queued(path);
        concurrent.CaptureComplete(ScanTarget.Equipped);
        Parallel.ForEach(files,path=> { concurrent.Recognized(path,result,null);concurrent.Recognized(path,result,null); });
        Require(concurrent.Snapshot.Items==100 && concurrent.Snapshot.Pending==0 && concurrent.Snapshot.Areas[0].Phase==AreaPhase.Completed,"Concurrent workers lost progress.");
    }
    private static void Selection(string output)
    {
        var options=new ScanOptions(ScanMode.Auto,true,false,true,false);
        Require(BatchScan.CreatePlan(options).Select(step=>step.Folder).SequenceEqual(new[] { "equipped","tab" }),"Selected areas or current-tab plan is wrong.");
        Require(BatchScan.CreatePlan(options with { Mode=ScanMode.Manual }).Select(step=>step.Folder).SequenceEqual(new[] { "equipped" }),"Manual must only scan equipped.");
        string batch=BatchScan.RunSelection(Path.Combine(output,"selection"),options,(step,folder)=>
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"session.json"),JsonSerializer.Serialize(new { Status="completed",ActiveTab=step.LocationType=="bank" ? (int?)4 : null,Cells=Array.Empty<object>() }));
        },_=> { },CancellationToken.None);
        Require(DateTime.TryParseExact(Path.GetFileName(batch),"yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),"Scan folder must contain only the timestamp.");
        BatchRecognition.Run(batch,folder=>
        {
            File.WriteAllText(Path.Combine(folder,"full.json"),"{\"items\":[{\"file\":\"r01_c01.png\",\"item\":{\"Name\":\"Test\"}}]}");
            return Path.Combine(folder,"report.html");
        },_=> { },CancellationToken.None);
        using var items=JsonDocument.Parse(File.ReadAllText(Path.Combine(batch,"items.json")));
        Require(items.RootElement.GetArrayLength()==2 && items.RootElement[0].GetProperty("location").GetProperty("tab").ValueKind==JsonValueKind.Null
            && items.RootElement[1].GetProperty("location").GetProperty("tab").GetInt32()==4,"Current bank tab identity was lost in the combined export.");
    }
    private static void Render(string output)
    {
        using var form=new MainForm();
        _=form.Handle;form.PerformLayout();
        T Find<T>(string name) where T:Control => (T)form.Controls.Find(name,true).Single();
        var pages=Find<TabControl>("mainTabs");
        Require(Find<CheckBox>("targetEquipped").Checked && Find<CheckBox>("targetBank").Checked,"Default areas not selected.");
        Require(form.Controls.Find("debugCaptures",true).Length==1 && !Find<CheckBox>("debugCaptures").Checked,"Capture diagnostics must be available but off by default.");
        Save(form,Path.Combine(output,"main-auto.png"));
        Require(pages.TabPages.Count==1 && pages.SelectedTab?.Name=="scanPage","Only Scan should be shown before the first run.");
        RequireFits(Find<TableLayoutPanel>("scanContent"));
        Find<CheckBox>("targetEquipped").Checked=false;
        Find<RadioButton>("modeManual").Checked=true;
        Require(Find<CheckBox>("targetEquipped").Checked && !Find<CheckBox>("targetEquipped").Enabled
            && !Find<CheckBox>("targetInventory").Enabled && !Find<CheckBox>("targetBank").Checked,"Manual choices not restricted.");
        Save(form,Path.Combine(output,"main-manual.png"));
        Find<RadioButton>("modeAuto").Checked=true;
        Require(!Find<CheckBox>("targetEquipped").Checked && Find<CheckBox>("targetInventory").Checked && Find<CheckBox>("targetBank").Checked,"Manual overwrote the saved Auto choices.");
        Find<CheckBox>("targetInventory").Checked=false;Find<CheckBox>("targetBank").Checked=false;
        Require(!Find<Button>("scanStart").Enabled,"Empty selection can start a scan.");
        Find<CheckBox>("targetEquipped").Checked=Find<CheckBox>("targetInventory").Checked=Find<CheckBox>("targetBank").Checked=true;
        var snapshot=new ScanSnapshot(10,ScanMode.Auto,ScanPhase.Waiting,
            [new(ScanTarget.Equipped,AreaPhase.Completed,3,0,0,null,false),new(ScanTarget.Inventory,AreaPhase.Completed,0,0,0,null,false),new(ScanTarget.Bank,AreaPhase.Waiting,40,0,1,2,true)],
            ScanTarget.Bank,"Bank not detected. Open your stash to continue.",[new(ScanTarget.Bank,1,2,3,"Could not read title")],null);
        var initialSize=form.Size;
        form.DisplayProgress(snapshot);
        Require(pages.TabPages.Count==2 && pages.SelectedTab?.Name=="resultPage" && form.Size==initialSize,"A scan must select Result without enlarging the window.");
        Find<Button>("scanStop").Visible=true;Find<Button>("scanStop").Enabled=true;
        Save(form,Path.Combine(output,"main-progress.png"));RequireFits(Find<TableLayoutPanel>("resultContent"));
        pages.SelectedTab=pages.TabPages[0];form.DisplayProgress(snapshot with { Revision=11 });
        Require(pages.SelectedTab?.Name=="scanPage","Progress updates stole the selected tab.");
        form.ShowResult();form.DisplayProgress(snapshot with { Revision=12 });
        Require(pages.TabPages.Count==2 && pages.SelectedTab?.Name=="resultPage","Repeated scans must reuse the Result tab.");
        // Results and an expanded journal must fit even at the minimum window size.
        var journal=Find<TextBox>("scanLog");journal.Visible=true;journal.Text=string.Join(Environment.NewLine,Enumerable.Range(1,25).Select(n=>$"Diagnostic line {n}"));
        form.Size=form.MinimumSize;Save(form,Path.Combine(output,"result-details-minimum.png"));
        RequireFits(Find<TableLayoutPanel>("resultContent"));RequireFits(journal);
        Require(journal.Height>=50,$"Technical journal has no usable space at minimum size: {journal.Bounds}; result content: {Find<TableLayoutPanel>("resultContent").Bounds}; layout: {journal.Parent!.ClientSize}.");
        pages.SelectedTab=pages.TabPages[0];Save(form,Path.Combine(output,"scan-minimum.png"));RequireFits(Find<TableLayoutPanel>("scanContent"));
        form.Size=initialSize;form.ShowResult();journal.Visible=false;Find<Button>("scanStop").Visible=false;
        form.DisplayProgress(snapshot with { Phase=ScanPhase.Cancelled,Message="Stopped. Captured items are kept.",Areas=snapshot.Areas.Select(a=>a.Area==ScanTarget.Bank ? a with { Phase=AreaPhase.Cancelled } : a).ToArray() });
        Save(form,Path.Combine(output,"result-stopped.png"));RequireFits(Find<TableLayoutPanel>("resultContent"));
        Find<CheckBox>("debugCaptures").Checked=true;
        using(var fresh=new MainForm())Require(!((CheckBox)fresh.Controls.Find("debugCaptures",true).Single()).Checked,"Diagnostic option persisted to another instance.");
        foreach(var mode in new[]{ScanMode.Auto,ScanMode.Manual})
        {
            using var overlay=new ScanOverlay(error=>throw error) { ClientSize=new(820,560) };
            var overlaySnapshot=mode==ScanMode.Auto ? snapshot with { Phase=ScanPhase.Scanning,Message="",Areas=snapshot.Areas.Select(a=>a.Area==ScanTarget.Bank ? a with { Phase=AreaPhase.Scanning } : a).ToArray() }
                : snapshot with { Mode=ScanMode.Manual,Phase=ScanPhase.Scanning,Areas=[new(ScanTarget.Equipped,AreaPhase.Scanning,3,1,0,null,false)],Message="Hold Left Alt yourself and hover a blue slot.",Issues=[] };
            overlay.UpdateProgress(overlaySnapshot);
            Require(overlay.Controls.Count==0,"Overlay gained independently painted child windows.");
            Save(overlay,Path.Combine(output,$"overlay-{mode.ToString().ToLowerInvariant()}.png"));
            nint handle=overlay.Handle;
            for(int count=0;count<100;count++)overlay.UpdateProgress(overlaySnapshot with { Revision=20+count,
                Areas=overlaySnapshot.Areas.Select(a=>a with { Items=count,Pending=100-count }).ToArray() });
            Require(overlay.Handle==handle && !overlay.Visible,"Progress recreated the overlay window or activated it without a game.");
            overlay.UpdateProgress(overlaySnapshot with { Revision=120,Phase=ScanPhase.Completed,
                Message="Scan complete. Some items need review.",
                Areas=overlaySnapshot.Areas.Select(a=>a with { Phase=a.Issues>0 ? AreaPhase.NeedsReview : AreaPhase.Completed,Pending=0 }).ToArray() });
            Save(overlay,Path.Combine(output,$"overlay-{mode.ToString().ToLowerInvariant()}-completed.png"));
        }
    }
    private static void RequireFits(Control control)
    {
        var parent=control.Parent!;
        Require(control.Bottom<=parent.ClientSize.Height-parent.Padding.Bottom && control.Right<=parent.ClientSize.Width-parent.Padding.Right,
            $"{control.Name} overflows its container: {control.Bounds} / {parent.ClientSize}.");
    }
    private static void CancelledStart(string output)
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        foreach(var mode in new[]{ScanMode.Auto,ScanMode.Manual})
        {
            string destination=Path.Combine(output,"cancelled-"+mode);
            var outcome=new ScanController(_=> { }).Run(new(mode,true,true,true,true),destination,false,cancellation.Token,_=> { },_=>throw new InvalidOperationException("Cancelled run reached capture."));
            Require(outcome.Progress.Phase==ScanPhase.Cancelled && outcome.Progress.GameBounds is null && outcome.Progress.Pending==0,"Cancelled start entered game capture or did not finish.");
            Require(outcome.ExportFolders.Length==0 && !Directory.Exists(destination) && outcome.Progress.Message.Contains("No results folder"),"Empty cancelled run created a folder or lost its in-memory outcome.");
        }
    }
    private static void OcrFailures(string output)
    {
        string session=Path.Combine(output,"ocr-failures");Directory.CreateDirectory(session);
        var tracker=new ScanProgressTracker(new(ScanMode.Auto,false,false,true,false),_=> { });
        tracker.Scanning(ScanTarget.Bank);tracker.RegisterSession(session,ScanTarget.Bank,6);
        var pipeline=new OcrPipeline(_=> { },2,tracker.Queued,tracker.Recognized);
        pipeline.Register(session);
        string corrupt=Path.Combine(session,"r01_c01.png"),blank=Path.Combine(session,"r01_c02.png");
        File.WriteAllText(corrupt,"Invalid PNG fixture");
        using(var bitmap=new Bitmap(240,120))
        {
            using(var graphics=Graphics.FromImage(bitmap))graphics.Clear(Color.Black);
            bitmap.Save(blank,ImageFormat.Png);
        }
        pipeline.Enqueue(corrupt);pipeline.Enqueue(blank);tracker.CaptureComplete(ScanTarget.Bank);
        bool failed=false;
        try { pipeline.Complete(); }catch(InvalidOperationException){failed=true;}
        Require(failed && tracker.Snapshot.Pending==0 && tracker.Snapshot.Items==0 && tracker.Snapshot.Issues.Length==2
            && tracker.Snapshot.Issues.All(issue=>issue.Tab==6) && tracker.Snapshot.Areas[0].Phase==AreaPhase.NeedsReview,"OCR failures were not drained into per-slot issues.");
        using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(session,"full.json")));
        Require(report.RootElement.GetProperty("items").GetArrayLength()==1,"Worker did not continue after an unreadable capture.");
    }
    private static void Save(Form form,string path)
    {
        static void CreateHandles(Control control)
        {
            _=control.Handle;
            foreach(Control child in control.Controls)CreateHandles(child);
            control.PerformLayout();
        }
        CreateHandles(form);
        form.PerformLayout();
        using var bitmap=new Bitmap(form.Width,form.Height);
        form.DrawToBitmap(bitmap,new(Point.Empty,form.Size));bitmap.Save(path,ImageFormat.Png);
    }
}
