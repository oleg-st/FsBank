using FsBank.Scanner.Export;
using FsBank.Scanner.Game;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class SingleLineFooterCheck
{
    public static void Run(string source,string output)
    {
        Directory.CreateDirectory(output);
        void Require(bool ok,string message) { if(!ok)throw new InvalidOperationException(message); }
        string saved=Path.Combine(source,"inventory","r01_c04-tooltip-failed.png");
        File.Copy(saved,Path.Combine(output,"source-frame.png"),true);
        var frame=Pixels.Load(saved);
        var baseline=Pixels.Load(Path.Combine(source,"inventory","baseline.png"));
        var vision=new Vision();
        var layout=vision.FindLayout(baseline,ScanTarget.Inventory)!;
        var cell=layout.Cell(0,3);var cursor=new Point(cell.X+cell.Width/2,cell.Y+cell.Height/2);
        var tooltip=vision.FindTooltipNear(frame,1,cursor) ?? throw new InvalidOperationException("Single-line tooltip not detected");
        Require(vision.FindTooltip(frame,1)==tooltip,"Full and near detection differ");
        Require(vision.TrackTooltip(frame,1,tooltip)==tooltip,"Single-line tracking failed");
        Require(vision.FooterPresent(frame,1,tooltip.Footer) && vision.FooterPresentNear(frame,1,cursor),"Footer presence missed");
        Require(!vision.FooterPresent(baseline,1,tooltip.Footer) && !vision.FooterPresentNear(baseline,1,cursor),"Dismissed footer still present");
        Require(vision.FindTooltipNear(baseline,1,cursor) is null,"Empty baseline falsely detected");
        var crop=frame.Crop(tooltip.Bounds);
        Require(TooltipSegmenter.HasCompleteTitle(crop) && TooltipSegmenter.HasReadableLayout(crop),"Recovered crop incomplete");
        using var quality=new TooltipCaptureQuality();
        Require(quality.HasReadableStats(crop),"Recovered crop failed stat gate");
        string folder=Path.Combine(output,"inventory");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"session.json"),"{\"LocationType\":\"inventory\"}");
        string file=Path.Combine(folder,"r01_c04.png");crop.Save(file);
        using var reader=new NativeTextReader();
        var result=ItemRecognition.RecognizeFile(file,reader,_=>{},CancellationToken.None);
        Require(result.Issue is null,result.Issue ?? "");
        var item=CompactExport.Project(JsonSerializer.SerializeToElement(result.Data),null,"inventory");
        Require(item["name"]!.GetValue<string>()=="SOUL-CURSED SIGNET" && item["rarity"]!.GetValue<string>()=="Epic"
            && item["item_level"]!.GetValue<int>()==105 && item["temper"]!.GetValue<string>()=="0/2","Wrong ring metadata");
        Require(item["stats"]!.AsArray().Select(s=>(s!["type"]!.GetValue<string>(),s["value"]!.GetValue<int>(),s["origin"]!.GetValue<string>()))
            .SequenceEqual(new[]{("Stamina",5,"base"),("Critical Strike",4,"base"),("Haste",4,"base"),("Critical Strike",5,"dynamic")}),"Wrong visible stats");
        Require(item["mods"]!.AsArray().Count==2 && item["mods"]![0]!["stat"]!["type"]!.GetValue<string>()=="Haste"
            && item["mods"]![0]!["stat"]!["value"]!.GetValue<int>()==5 && item["mods"]![1]!["gem_power"]!.GetValue<string>()=="Emerald"
            && item["mods"]![1]!["value"]!.GetValue<int>()==100,"Wrong visible modifiers");
        ItemRecognition.WriteReport(folder,[result],0,_=>{});
        var items=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")))!.AsArray();
        Require(items.Count==19,"Unexpected original item count");items.Add(item);
        CompactExport.WriteItems(output,items);ScanExportCheck.CheckItemBrowser(output);
        File.WriteAllText(Path.Combine(output,"checks.txt"),"Passed: single-line footer full/near detection, tracking, presence and dismissal; complete capture and stat gate; visible name, level, rarity, temper, four stats and two modifiers; zero review warnings. Derived export preserves the original 19 items and adds the recovered ring.");
        Console.WriteLine("PASS single-line footer: ring recovered, derived export contains 20 items.");
    }
}
