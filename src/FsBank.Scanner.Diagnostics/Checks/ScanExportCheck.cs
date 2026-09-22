using FsBank.Scanner.Export;
using FsBank.Scanner.Items;
using FsBank.Scanner.Ocr;
using FsBank.Scanner.Scanning.Batch;

using System.Text.Json;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class ScanExportCheck
{
    public static void Run(string root)
    {
        CheckStatOrigins();
        foreach (bool debug in new[] { false, true })
        foreach (bool partial in new[] { false, true })
        {
            string output = Path.Combine(root, $"batch-{debug}-{partial}");
            using var export = new ScanExport(output, debug);
            try
            {
                BatchScan.Run(export.WorkingRoot, (index, session) =>
                {
                    Directory.CreateDirectory(session);
                    File.WriteAllText(Path.Combine(session, "session.json"), JsonSerializer.Serialize(new
                    { Status="completed", ActiveTab=index+1, Cells=Array.Empty<object>() }));
                    File.WriteAllText(Path.Combine(session, "full.json"), "{\"items\":[]}");
                    File.WriteAllText(Path.Combine(session, "report.html"), "test");
                    if (partial) throw new OperationCanceledException();
                }, _ => {}, CancellationToken.None, everything:true);
            }
            catch (OperationCanceledException) when (partial) { }
            export.Complete(_ => {});
            string result = Directory.GetDirectories(output).Single();
            int count = Directory.GetDirectories(result).Length;
            if (count != (debug ? partial ? 1 : 9 : 0)) throw new Exception("Unexpected batch folders");
            if (!debug && Directory.GetFiles(result).Length != 2) throw new Exception("Unexpected compact batch files");
        }
        foreach (bool debug in new[] { false, true })
        foreach (string location in new[] { "equipped", "inventory", "bank", "unknown" })
        {
            string output = Path.Combine(root, debug + "-" + location);
            using var export = new ScanExport(output, debug);
            string session = Path.Combine(export.WorkingRoot, "capture");
            Directory.CreateDirectory(session);
            File.WriteAllText(Path.Combine(session, "session.json"), JsonSerializer.Serialize(new
            { LocationType = location == "unknown" ? "bank" : location, ActiveTab = location == "bank" ? 3 : 0, Status = "cancelled", Cells = Array.Empty<object>() }));
            File.WriteAllText(Path.Combine(session, "full.json"), "{\"items\":[{\"file\":\"r01_c01.png\",\"item\":{}}]}");
            File.WriteAllBytes(Path.Combine(session, "r01_c01.png"), [1, 2, 3]);
            File.WriteAllText(Path.Combine(session, "report.html"), "test");
            export.Complete(_ => {});
            string result = Path.Combine(output, "capture");
            string expected = location == "bank" ? "tab-03" : location == "unknown" ? "tab" : location;
            var directories = Directory.GetDirectories(result).Select(Path.GetFileName).ToArray();
            if (debug ? !directories.SequenceEqual(new[] { expected }) : directories.Length != 0)
                throw new Exception("Unexpected export directories: " + output);
            var files = Directory.GetFiles(result).Select(Path.GetFileName).Order().ToArray();
            string[] expectedFiles = debug ? ["batch.json", "full.json", "items.html", "items.json", "report.html"] : ["items.html", "items.json"];
            if (!files.SequenceEqual(expectedFiles)) throw new Exception("Unexpected export files");
            string html = File.ReadAllText(Path.Combine(result, "items.html"));
            if (!debug && (!html.Contains("data:image/png;base64,AQID") || html.Contains("href='report.html'")))
                throw new Exception("Compact HTML is not self-contained");
            using var items = JsonDocument.Parse(File.ReadAllText(Path.Combine(result, "items.json")));
            if (items.RootElement[0].GetProperty("location").GetProperty("type").GetString() != (location == "unknown" ? "bank" : location))
                throw new Exception("Incorrect location");
        }
    }

    private static void CheckStatOrigins()
    {
        RecognizedLine Line(int index, string color, string text) =>
            new(index, new Rectangle(0, index * 12, 100, 10), color, text, 99);
        var parsed = TooltipParser.Parse([
            Line(0, "white", "Power Potential 100"),
            Line(1, "white", "+3 Intellect"),
            Line(2, "cyan", "+2 Intellect"),
            Line(3, "cyan", "+2 Intellect"),
            Line(4, "unknown", "+1 Haste"),
            Line(5, "white", "Left Alt - Show Details")
        ]);
        if (!parsed.Stats.Select(s => s.Origin).SequenceEqual(["base", "dynamic", "dynamic", "unresolved"]))
            throw new Exception("Incorrect stat origins from tooltip colors");
        var row = JsonSerializer.SerializeToElement(new { file = "r01_c01.png", item = parsed });
        var stats = CompactExport.Project(row, 1)["stats"]!.AsArray();
        if (!stats.Select(s => s!["origin"]!.GetValue<string>()).SequenceEqual(["base", "dynamic", "dynamic", "unresolved"]))
            throw new Exception("Compact export lost stat origins or repeated stats");
        using var legacy = JsonDocument.Parse("""
            {"item":{"Stats":[{"Name":"Intellect","Value":2,"Origin":"fixed_roll"},{"Name":"Haste","Value":1}]}}
            """);
        var legacyStats = CompactExport.Project(legacy.RootElement, 1)["stats"]!.AsArray();
        if (legacyStats[0]!["origin"]!.GetValue<string>() != "dynamic"
            || legacyStats[1]!["origin"]!.GetValue<string>() != "unresolved")
            throw new Exception("Incorrect legacy stat origin conversion");
    }
}
