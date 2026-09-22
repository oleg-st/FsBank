using FsBank.Scanner.Export;
using FsBank.Scanner.Items;
using FsBank.Scanner.Ocr;
using FsBank.Scanner.Scanning.Batch;

using System.Text.Json;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class ScanExportCheck
{
    public static void Run(string root)
    {
        CheckStatOrigins();
        CheckWrappedTitles();
        CheckBrowserEscaping(root);
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
            CheckItemBrowser(result);
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
            CheckItemBrowser(result);
            using var items = JsonDocument.Parse(File.ReadAllText(Path.Combine(result, "items.json")));
            if (items.RootElement[0].GetProperty("location").GetProperty("type").GetString() != (location == "unknown" ? "bank" : location))
                throw new Exception("Incorrect location");
        }
    }

    internal static void CheckItemBrowser(string folder)
    {
        string html = File.ReadAllText(Path.Combine(folder, "items.html"));
        var match = Regex.Match(html, """<script id="embedded-data" type="application/json">(.*?)</script>""", RegexOptions.Singleline);
        if (!match.Success || Regex.IsMatch(html, """<(?:script|link|img)\b[^>]*(?:src|href)\s*=""", RegexOptions.IgnoreCase))
            throw new Exception("Item browser must embed its data, scripts and styles");
        using var embedded = JsonDocument.Parse(match.Groups[1].Value);
        using var items = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "items.json")));
        if (!JsonElement.DeepEquals(embedded.RootElement, items.RootElement))
            throw new Exception("Item browser data differs from items.json");
        if (!html.Contains("id=\"search\"") || !html.Contains("id=\"results\"") || !html.Contains("function render()"))
            throw new Exception("Item browser interface is missing");
    }

    private static void CheckBrowserEscaping(string root)
    {
        string folder = Path.Combine(root, "browser-escaping");
        Directory.CreateDirectory(folder);
        var row = JsonSerializer.SerializeToElement(new
        {
            file = "r01_c01.png",
            item = new
            {
                Name = "</script><script>alert('test')</script> & \"quoted\" Élan __ITEM_DATA__",
                Slot = "Head", Rarity = "Uncommon",
                Stats = new[] { new { Name = "Strength", Value = 10, Origin = "base" } },
                Modifiers = new[] { new { Kind = "ability", Name = "<Ability> & test" } }
            }
        });
        CompactExport.Write(folder, [row]);
        CheckItemBrowser(folder);
        string html = File.ReadAllText(Path.Combine(folder, "items.html"));
        if (html.Contains("</script><script>alert") || html.Contains("<Ability>"))
            throw new Exception("Item text can escape the embedded JSON script");
    }

    private static void CheckWrappedTitles()
    {
        RecognizedLine Line(int index, string text) =>
            new(index, new Rectangle(0, index * 16, 200, 14), "mixed", text, 99);
        foreach (var (first, second, expected) in new[]
        {
            ("BETRAYER'S BLOOD-", "QUARTZ RING", "BETRAYER'S BLOOD-QUARTZ RING"),
            ("REDEEMER'S THORN-", "CRESTED MASK", "REDEEMER'S THORN-CRESTED MASK"),
            ("WITCHSLAYER'S BONE-", "CARVED TORC", "WITCHSLAYER'S BONE-CARVED TORC"),
            ("GRIMOIRE OF", "RESURRECTION", "GRIMOIRE OF RESURRECTION"),
            ("VAULTBINDER'S LEGGINGS", "OF THE FLEET", "VAULTBINDER'S LEGGINGS OF THE FLEET"),
            ("SOUL-CURSED", "SIGNET", "SOUL-CURSED SIGNET"),
            ("BETRAYER’'S BLOOD-", "QUARTZ RING", "BETRAYER'S BLOOD-QUARTZ RING")
        })
        {
            var parsed = TooltipParser.Parse([Line(0, first), Line(1, second)]);
            if (parsed.Name != expected) throw new Exception($"Incorrect wrapped title: {parsed.Name}");
        }
        var ornament = new RecognizedLine(1, new Rectangle(64,36,157,7), "mixed", "SS", 0);
        var decorated = TooltipParser.Parse([Line(0,"BANDS OF THE FIRST DRYAD"),ornament]);
        if (decorated.Name != "BANDS OF THE FIRST DRYAD" || !decorated.MetadataLines.Contains("SS"))
            throw new Exception("Title ornament was included in the name or its evidence was lost");
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
