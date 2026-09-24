using System.Text.Json;
using System.Text.Json.Nodes;
using FsBank.Scanner.Game;
using FsBank.Scanner.Ocr;
using FsBank.Scanner.Scanning;

namespace FsBank.Scanner.Diagnostics.Checks;

// Regression against the saved equipped scan of 2026-09-23 23:27.
internal static class ReviewWarningsCheck
{
    public static void FullScan(string source,string output)
    {
        Directory.CreateDirectory(output);
        File.Copy(Path.Combine(source,"batch.json"),Path.Combine(output,"batch.json"),true);
        var tracker=new ScanProgressTracker(new(ScanMode.Auto,true,true,true,true),_=> { });
        var pipeline=new OcrPipeline(_=> { },2,tracker.Queued,tracker.Recognized);
        int count=0;
        foreach(string original in Directory.GetDirectories(source).Order(StringComparer.Ordinal))
        {
            string folder=Path.GetFileName(original);
            if(folder is not ("equipped" or "inventory") && !folder.StartsWith("tab-"))continue;
            string session=Path.Combine(output,folder);Directory.CreateDirectory(session);
            File.Copy(Path.Combine(original,"session.json"),Path.Combine(session,"session.json"),true);
            var target=folder=="equipped" ? ScanTarget.Equipped : folder=="inventory" ? ScanTarget.Inventory : ScanTarget.Bank;
            tracker.Scanning(target);tracker.RegisterSession(session,target,target==ScanTarget.Bank ? int.Parse(folder[4..]) : null);
            pipeline.Register(session);
            foreach(string file in Directory.GetFiles(original,"r??_c??.png"))
            {
                string copy=Path.Combine(session,Path.GetFileName(file));File.Copy(file,copy,true);pipeline.Enqueue(copy);count++;
            }
        }
        foreach(var target in new[]{ScanTarget.Equipped,ScanTarget.Inventory,ScanTarget.Bank})tracker.CaptureComplete(target);
        Console.WriteLine($"Recognizing {count} saved captures...");
        pipeline.Complete();tracker.Finish(ScanPhase.Completed,"Full scan regression complete.");
        ScanController.WriteSummary(output,tracker.Snapshot);
        Require(count>0 && tracker.Snapshot.Items==count && tracker.Snapshot.Issues.Length==0 && tracker.Snapshot.Pending==0,
            "Full scan issues: "+string.Join("; ",tracker.Snapshot.Issues.Select(i=>$"{i.Area}/{i.Tab}/{i.Row}:{i.Column} {i.Reason}")));
        var expected=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")))!.AsArray();
        int corrected=0;
        foreach(var item in expected)
        {
            string? name=item!["name"]?.GetValue<string>();
            if(name=="SSS HUNGERING BLOODTHORN WRISTGUARDS") { item["name"]="HUNGERING BLOODTHORN WRISTGUARDS";corrected++; }
            if(name=="SSS SS SS ANCIENT POULTRY FETISH") { item["name"]="ANCIENT POULTRY FETISH";corrected++; }
            if(name=="EZ FALLEN ORDER'S THORNED TREADS") { item["name"]="FALLEN ORDER'S THORNED TREADS";corrected++; }
        }
        var actual=JsonNode.Parse(File.ReadAllText(Path.Combine(output,"items.json")));
        Require(JsonNode.DeepEquals(expected,actual),"Export differs beyond the visually verified title corrections.");
        File.WriteAllText(Path.Combine(output,"checks.txt"),$"Passed: real OCR of all {count} captures, zero review issues, {corrected} decorative title prefixes removed, all other exported fields unchanged; imbued traits remain excluded.");
        Console.WriteLine("Full scan review regression passed: "+output);
    }
    public static void Run(string source,string output)
    {
        Directory.CreateDirectory(output);
        string session=Path.Combine(output,"equipped");
        Directory.CreateDirectory(session);
        File.Copy(Path.Combine(source,"session.json"),Path.Combine(session,"session.json"),true);
        var tracker=new ScanProgressTracker(new(ScanMode.Auto,true,false,false,false),_=> { });
        tracker.Scanning(ScanTarget.Equipped);tracker.RegisterSession(session,ScanTarget.Equipped,null);
        var pipeline=new OcrPipeline(_=> { },2,tracker.Queued,tracker.Recognized);
        pipeline.Register(session);
        foreach(string file in Directory.GetFiles(source,"r??_c??.png"))
        {
            string copy=Path.Combine(session,Path.GetFileName(file));File.Copy(file,copy,true);pipeline.Enqueue(copy);
        }
        tracker.CaptureComplete(ScanTarget.Equipped);pipeline.Complete();
        tracker.Finish(ScanPhase.Completed,"Saved capture regression complete.");
        ScanController.WriteSummary(session,tracker.Snapshot);
        Require(tracker.Snapshot.Items==14 && tracker.Snapshot.Issues.Length==0 && tracker.Snapshot.Pending==0,
            "Expected 14 recognized items without issues: "+string.Join("; ",tracker.Snapshot.Issues.Select(i=>$"{i.Row}:{i.Column} {i.Reason}")));
        using var full=JsonDocument.Parse(File.ReadAllText(Path.Combine(session,"full.json")));
        var items=full.RootElement.GetProperty("items").EnumerateArray().ToDictionary(i=>i.GetProperty("file").GetString()!);
        var originalExport=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")));
        var updatedExport=JsonNode.Parse(File.ReadAllText(Path.Combine(session,"items.json")));
        Require(JsonNode.DeepEquals(originalExport,updatedExport),"Compact export changed; imbued traits must remain excluded.");
        void Traits(string file,string[] expected)
        {
            var item=items[file];
            var traits=item.GetProperty("item").GetProperty("ImbuedTraits").EnumerateArray().ToArray();
            Require(traits.Select(t=>t.GetProperty("Name").GetString()).SequenceEqual(expected,StringComparer.OrdinalIgnoreCase),
                "Imbued traits were not recognized: "+file);
            foreach(var trait in traits)
                Require(item.GetProperty("lines").EnumerateArray().Any(line=>line.GetProperty("Index").GetInt32()==trait.GetProperty("SourceLine").GetInt32()
                    && line.GetProperty("Text").GetString()!.Contains(trait.GetProperty("Name").GetString()!)),"Imbued trait lost its source line.");
            Require(item.GetProperty("item").GetProperty("UnparsedLines").GetArrayLength()==0,"Recognized imbued traits still marked unparsed.");
        }
        Traits("r01_c02.png",["The Mountain","The Dragon"]);
        Traits("r07_c02.png",["Seized Opportunity","King of the Hill","Willful Momentum","Latent Resurgence","Martial Initiative"]);
        var ring=items["r03_c02.png"].GetProperty("item");
        Require(ring.GetProperty("Name").GetString()=="LOOP OF UNYIELDING BLOOM" && ring.GetProperty("ItemLevel").GetInt32()==360
            && ring.GetProperty("Stats").GetArrayLength()==5,"Title ornament changed ring data.");
        File.WriteAllText(Path.Combine(output,"checks.txt"),"Passed: real OCR for 14 equipped captures, 7 imbued traits recognized with source lines in full.json, zero actionable issues, compact export unchanged (imbued traits excluded), ring title ornament ignored by issue count, diagnostic evidence retained.");
        Console.WriteLine("Review warning regression passed: "+output);
    }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
}
