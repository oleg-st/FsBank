using FsBank.Scanner.Export;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class BackgroundHeaderCheck
{
    public static void Regression(string source,string output)
    {
        Directory.CreateDirectory(output);
        using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"scan-summary.json")));
        var issues=summary.RootElement.GetProperty("Issues").EnumerateArray().ToArray();
        var images=Regex.Matches(File.ReadAllText(Path.Combine(source,"scan-issues.html")),@"data:image/png;base64,([A-Za-z0-9+/=]+)");
        string[] names=["TITANSLAYER'S DRAPE","PATCHWORK RAT FUR CAPE","SOUL-STICHED CAP","CONJURER'S SILKEN CLOAK",
            "CONJURER'S SILKEN CLOAK","HARDENED LEATHER GRIPS","TITANSLAYER'S DRAPE","BUTCHER'S APRON","EVERWARM GLOVES",
            "CONJURER'S SILKEN CLOAK","CORAL-LACED GRIPS","ANCIENT POULTRY FETISH","ANCIENT POULTRY FETISH",
            "MISMATCHED BOOTS","BARNACLED BOOTS","SNOWMELT BOOTS","SALVAGED BOOTS","WHITE FUR BOOTS",
            "LEAFWOVEN HOOD","LEAFWOVEN HOOD","LEAFWOVEN HOOD","MINER'S HEAVY LEGGUARDS","MINER'S HEAVY LEGGUARDS",
            "MINER'S HEAVY LEGGUARDS","FUNERARY JACKET","GROVE STALKER'S TUNIC"];
        if(issues.Length!=names.Length || images.Count!=names.Length)throw new InvalidOperationException("Expected 26 issue images");
        var items=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")))!.AsArray();
        using var reader=new NativeTextReader();
        var reports=new Dictionary<string,List<ItemRecognition.Result>>();
        for(int i=0;i<issues.Length;i++)
        {
            var issue=issues[i];
            string kind=issue.GetProperty("Area").GetString()!.ToLowerInvariant();
            int? tab=kind=="bank" ? issue.GetProperty("Tab").GetInt32() : null;
            int row=issue.GetProperty("Row").GetInt32(),column=issue.GetProperty("Column").GetInt32();
            string folder=Path.Combine(output,kind=="bank" ? $"tab-{tab:00}" : kind);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"session.json"),JsonSerializer.Serialize(new {LocationType=kind,ActiveTab=tab}));
            string file=Path.Combine(folder,$"r{row:00}_c{column:00}.png");
            File.WriteAllBytes(file,Convert.FromBase64String(images[i].Groups[1].Value));
            var result=ItemRecognition.RecognizeFile(file,reader,_=>{},CancellationToken.None);
            if(result.Issue is not null)throw new InvalidOperationException(file+": "+result.Issue);
            var actual=CompactExport.Project(JsonSerializer.SerializeToElement(result.Data),tab,kind);
            if(actual["name"]!.GetValue<string>()!=names[i])throw new InvalidOperationException("Title differs from visible image: "+file);
            int index=Enumerable.Range(0,items.Count).Single(n=>items[n]!["location"]!["type"]!.GetValue<string>()==kind
                && items[n]!["location"]!["tab"]?.GetValue<int>()==tab && items[n]!["location"]!["row"]!.GetValue<int>()==row
                && items[n]!["location"]!["column"]!.GetValue<int>()==column);
            var expected=items[index]!.DeepClone();expected["name"]=names[i];
            if(!JsonNode.DeepEquals(expected,actual))throw new InvalidOperationException("Fields changed outside restored title: "+file);
            items[index]=actual;
            if(!reports.TryGetValue(folder,out var results))reports[folder]=results=[];
            results.Add(result);
            Console.WriteLine($"PASS {kind}/{tab}/{row}:{column}: {names[i]}");
        }
        foreach(var (folder,results) in reports)ItemRecognition.WriteReport(folder,results,0,_=>{});
        CompactExport.WriteItems(output,items);ScanExportCheck.CheckItemBrowser(output);
        File.WriteAllText(Path.Combine(output,"checks.txt"),$"Passed: all 26 missing names restored from their original issue images, zero review issues on those images; other fields and all {items.Count-26} unaffected exports unchanged. Derived export contains {items.Count} items.");
        Console.WriteLine("PASS 26 title regressions; derived full export: "+output);
    }
    public static void Run(string source,string output)
    {
        Directory.CreateDirectory(output);
        // Release scans retain the exact issue image in HTML after raw captures
        // are cleaned up. Extract that evidence without altering the saved scan.
        var image=Regex.Match(File.ReadAllText(Path.Combine(source,"scan-issues.html")),@"data:image/png;base64,([A-Za-z0-9+/=]+)");
        if(!image.Success)throw new InvalidOperationException("No saved issue image");
        string file=Path.Combine(output,"r06_c01.png");
        File.WriteAllBytes(file,Convert.FromBase64String(image.Groups[1].Value));
        File.WriteAllText(Path.Combine(output,"session.json"),"{\"LocationType\":\"bank\",\"ActiveTab\":7}");
        using var reader=new NativeTextReader();
        var result=ItemRecognition.RecognizeFile(file,reader,_=>{},CancellationToken.None);
        var data=JsonSerializer.SerializeToElement(result.Data);
        if(result.Issue is not null)throw new InvalidOperationException(result.Issue);
        var first=data.GetProperty("lines")[0];
        if(first.GetProperty("Text").GetString()!="TRIBAL HAUBERK" || first.GetProperty("Box").GetProperty("Top").GetInt32()<40)
            throw new InvalidOperationException("World background remained in the title bands");
        var original=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")))!.AsArray();
        var expected=original.Single(item=>item!["location"]!["tab"]?.GetValue<int>()==7
            && item["location"]!["row"]!.GetValue<int>()==6 && item["location"]!["column"]!.GetValue<int>()==1);
        var actual=CompactExport.Project(data,7);
        if(!JsonNode.DeepEquals(expected,actual))throw new InvalidOperationException("Export changed while removing background noise");
        ItemRecognition.WriteReport(output,[result],0,_=>{});
        ScanExportCheck.CheckItemBrowser(output);
        File.WriteAllText(Path.Combine(output,"checks.txt"),"Passed: original issue image recovered from HTML; world background excluded from OCR; TRIBAL HAUBERK, all six stats and three modifiers unchanged; zero review issues.");
        Console.WriteLine("PASS background header: exported fields unchanged, zero review issues.");
    }
}
