using FsBank.Scanner.Export;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class WideTooltipCheck
{
    public static void Run(string source,string output)
    {
        Directory.CreateDirectory(output);
        var image=Regex.Match(File.ReadAllText(Path.Combine(source,"scan-issues.html")),@"data:image/png;base64,([A-Za-z0-9+/=]+)");
        Require(image.Success,"No saved issue image");
        string folder=Path.Combine(output,"inventory");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"session.json"),"{\"LocationType\":\"inventory\"}");
        string file=Path.Combine(folder,"r01_c01.png");File.WriteAllBytes(file,Convert.FromBase64String(image.Groups[1].Value));
        var expected=JsonNode.Parse("""
            {"name":"SHARD OF ANTIMATTER","item_level":315,"temper":"0/4","rarity":"Uncommon",
             "stats":[{"type":"Intellect","value":6,"origin":"base"},{"type":"Expertise","value":7,"origin":"base"},
                      {"type":"Spirit","value":7,"origin":"base"},{"type":"Haste","value":8,"origin":"dynamic"}],
             "mods":[],"item_slot":"Relic","location":{"type":"inventory","tab":null,"row":1,"column":1}}
            """)!;
        using var reader=new NativeTextReader();var vision=new Vision();using var quality=new TooltipCaptureQuality();
        JsonObject Verify(string path)
        {
            var result=ItemRecognition.RecognizeFile(path,reader,_=>{},CancellationToken.None);
            ItemRecognition.WriteReport(Path.GetDirectoryName(path)!,[result],0,_=>{});
            Require(result.Issue is null,path+": "+result.Issue);
            var data=JsonSerializer.SerializeToElement(result.Data);var actual=CompactExport.Project(data,null,"inventory");
            Require(JsonNode.DeepEquals(expected,actual),"Visible item fields differ: "+path);
            Require(data.GetProperty("item").GetProperty("PowerPotential").GetInt32()==345,"Power potential lost");
            return actual;
        }
        var actual=Verify(file);var original=Pixels.Load(file);
        Require(quality.HasReadableStats(original),"Original readable stats rejected");
        var detected=vision.FindTooltip(original,1);
        Require(detected is not null && detected.Bounds.Left>=28,"Bank strip remained in capture");
        Require(vision.TrackTooltip(original,1,detected!)==detected,"Capture tracking moved the bounds");
        var normalized=original.Crop(detected!.Bounds);
        int variants=0;
        foreach(int left in new[]{0,28,56})foreach(int right in new[]{0,28,56})
        {
            var padded=new Pixels(normalized.Width+left+right,normalized.Height);
            for(int y=0;y<padded.Height;y++)
            {
                // Repeat the real external bank strip, including its seams.
                for(int x=0;x<padded.Width;x++)Array.Copy(original.Data,original.Offset(x%28,y),padded.Data,padded.Offset(x,y),Pixels.BytesPerPixel);
                Array.Copy(normalized.Data,y*normalized.Width*Pixels.BytesPerPixel,padded.Data,padded.Offset(left,y),normalized.Width*Pixels.BytesPerPixel);
            }
            string variantFolder=Path.Combine(output,$"padding-{left}-{right}");Directory.CreateDirectory(variantFolder);
            string variantFile=Path.Combine(variantFolder,Path.GetFileName(file));padded.Save(variantFile);
            Verify(variantFile);
            var tip=vision.FindTooltip(padded,1);
            Require(tip is not null && tip.Bounds.Left>=left && tip.Bounds.Right<=left+normalized.Width,"Exterior grid selected as panel edge");
            string capturedFolder=Path.Combine(variantFolder,"captured");Directory.CreateDirectory(capturedFolder);
            string capturedFile=Path.Combine(capturedFolder,Path.GetFileName(file));padded.Crop(tip!.Bounds).Save(capturedFile);
            Verify(capturedFile);variants++;
        }
        var items=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")))!.AsArray();
        int index=Enumerable.Range(0,items.Count).Single(n=>items[n]!["location"]!["type"]!.GetValue<string>()=="inventory"
            && items[n]!["location"]!["row"]!.GetValue<int>()==1 && items[n]!["location"]!["column"]!.GetValue<int>()==1);
        items[index]=actual;CompactExport.WriteItems(output,items);ScanExportCheck.CheckItemBrowser(output);
        File.WriteAllText(Path.Combine(output,"checks.txt"),$"Passed: original screenshot and {variants} independent exterior-bank padding combinations recognized with complete name, rarity, temper, slot, level, four stats and power potential; capture removes bank strip and preserves all fields. Derived export contains {items.Count} items.");
        Console.WriteLine($"PASS original wide capture, {variants} padded legacy images and {variants} corrected captures");
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
