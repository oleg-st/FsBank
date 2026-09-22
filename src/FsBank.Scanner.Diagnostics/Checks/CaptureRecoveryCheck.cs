using FsBank.Scanner.Export;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Items;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FsBank.Scanner.Diagnostics.Checks;

// Real frames from the September 22 rescan: titles visible in the full frame
// were clipped by the detector, and all three hover attempts were rejected.
internal static class CaptureRecoveryCheck
{
    public static void Run(string batch, string output)
    {
        Directory.CreateDirectory(output);
        var vision = new Vision();
        using var reader = new NativeTextReader();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(batch,"full.json")));
        var rows = new List<JsonElement>();
        int renamed = 0;
        foreach (var row in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var lines = JsonSerializer.Deserialize<RecognizedLine[]>(row.GetProperty("lines").GetRawText())!;
            var parsed = TooltipParser.Parse(lines);
            var node = JsonNode.Parse(row.GetRawText())!;
            node["item"] = JsonSerializer.SerializeToNode(parsed);
            var updated = JsonSerializer.SerializeToElement(node);
            var before = CompactExport.Project(row,null);
            var after = CompactExport.Project(updated,null);
            if (!JsonNode.DeepEquals(before["name"],after["name"]))
            {
                string file = row.GetProperty("file").GetString()!;
                string expected = file switch
                {
                    "tab-02/r07_c05.png" => "BANDS OF THE FIRST DRYAD",
                    "tab-07/r03_c06.png" => "BETRAYER'S BLOOD-QUARTZ RING",
                    _ => throw new Exception("Unexpected name change: " + file)
                };
                if (parsed.Name != expected) throw new Exception("Incorrect title repair: " + file);
                renamed++;
            }
            before.Remove("name"); after.Remove("name");
            if (!JsonNode.DeepEquals(before,after)) throw new Exception("Unexpected change outside item name");
            rows.Add(updated);
        }
        if (rows.Count != 478 || renamed != 2) throw new Exception("Unexpected source fixtures");
        int recoveredCount = 0;
        foreach (var (tab, cell, name, stats) in new[]
        {
            (1,"r02_c05","TATTERED OILCLOTH",6),
            (6,"r05_c02","REDEEMER'S THORN-CRESTED MASK",6),
            (6,"r08_c02","HARDENED KELP WRAPPINGS",5)
        })
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            string relative = $"tab-{tab:00}/{cell}-header-attempt-{attempt}.png";
            var frame = Pixels.Load(Path.Combine(batch,relative));
            var tooltip = vision.FindTooltip(frame,1) ?? throw new Exception("No tooltip: " + relative);
            var crop = frame.Crop(tooltip.Bounds);
            if (!TooltipSegmenter.HasCompleteTitle(crop) || !TooltipSegmenter.HasReadableLayout(crop))
                throw new Exception("Incomplete recovered tooltip: " + relative);
            string folder = Path.Combine(output,$"attempt-{attempt}",$"tab-{tab:00}");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder,cell+".png");
            crop.Save(file);
            var result = ItemRecognition.RecognizeFile(file,reader,_=>{},CancellationToken.None);
            var node = JsonSerializer.SerializeToNode(result.Data)!;
            string? recognizedName = node["item"]!["Name"]?.GetValue<string>();
            int recognizedStats = node["item"]!["Stats"]!.AsArray().Count;
            if (recognizedName != name || recognizedStats != stats)
                throw new Exception($"Recovery mismatch: {relative}: {recognizedName}, {recognizedStats} stats");
            recoveredCount++;
            if (attempt == 3)
            {
                node["file"] = $"tab-{tab:00}/{cell}.png";
                node["tab"] = tab;
                node["location_type"] = "bank";
                rows.Add(JsonSerializer.SerializeToElement(node));
            }
            Console.WriteLine($"PASS {relative}: {name}, {stats} stats");
        }
        if (recoveredCount != 9 || rows.Count != 481) throw new Exception("Missing recovered fixtures");
        // Keep source snapshots intact; this is a derived export using recovered
        // screenshots and parser output, with no copied or guessed item values.
        CompactExport.Write(output,rows);
        ScanExportCheck.CheckItemBrowser(output);
        Console.WriteLine("PASS: 9 full-frame recoveries; 2 title repairs; original 478 item fields preserved; derived export contains 481 items.");
    }
}
