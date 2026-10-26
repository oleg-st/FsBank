using FsBank.Scanner.Export;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Items;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class Patch05Check
{
    public static void Compact(string screenshots, string source, string output)
    {
        Directory.CreateDirectory(output);
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "full.json")));
        using var reader = new NativeTextReader();
        using var quality = new TooltipCaptureQuality();
        var vision = new Vision();
        var results = new List<ItemRecognition.Result>();
        int compactLines = 0, detailedLines = 0;
        foreach (var (file, name) in new[] {("weapon", "ELHERYN'S CACHE"), ("relic", "ORB OF GATHERING STORMS"), ("necklace", "SKITTERING COLLAR"), ("head", "HUNTER'S HOOD")})
        {
            var frame = Pixels.Load(Path.Combine(screenshots, file + ".png"));
            var tip = vision.FindTooltip(frame, 1);
            Require(tip is not null, "Compact tooltip not found: " + file);
            var crop = frame.Crop(tip!.Bounds);
            string captured = Path.Combine(output, file + ".png");
            crop.Save(captured);
            var result = ItemRecognition.RecognizeFile(captured, reader, Console.WriteLine, CancellationToken.None);
            results.Add(result);
            Console.WriteLine(result.Issue ?? "No review issues");
            var actual = CompactExport.Project(JsonSerializer.SerializeToElement(result.Data), null);
            Console.WriteLine(actual.ToJsonString());
            var original = saved.RootElement.GetProperty("items").EnumerateArray().First(r => r.GetProperty("item").GetProperty("Name").GetString() == name);
            var originalItem = TooltipParser.Parse(original.GetProperty("lines").Deserialize<RecognizedLine[]>()!);
            var expected = CompactExport.Project(JsonSerializer.SerializeToElement(new { item = originalItem }), null);
            // Compact mode intentionally hides per-stat enchant details, but
            // must retain each modifier's identity/rank and every base stat.
            foreach (var mod in expected["mods"]!.AsArray()) mod!.AsObject().Remove("stats");
            Require(result.Issue is null && JsonNode.DeepEquals(expected, actual), "Compact export differs: " + file);
            Require(TooltipSegmenter.HasCompleteTitle(crop) && TooltipSegmenter.HasReadableLayout(crop) && quality.HasReadableStats(crop), "Compact capture rejected: " + file);
            compactLines += result.Lines; detailedLines += original.GetProperty("lines").GetArrayLength();
        }
        ItemRecognition.WriteReport(output, results, 0, Console.WriteLine);
        File.WriteAllText(Path.Combine(output, "checks.txt"), $"Passed: all four compact screenshots preserve item fields, base stats and modifier names/ranks. OCR lines: {compactLines} compact vs {detailedLines} detailed. Enchant bonus details remain available only in detailed captures.");
    }

    public static void Run(string source, string output)
    {
        Directory.CreateDirectory(output);
        CheckParser();
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "full.json")));
        var items = new JsonArray();
        var reviews = new JsonArray();
        int oldReview = 0, newReview = 0;
        foreach (var row in saved.RootElement.GetProperty("items").EnumerateArray())
        {
            var lines = row.GetProperty("lines").Deserialize<RecognizedLine[]>()!;
            var parsed = TooltipParser.Parse(lines);
            if (row.GetProperty("status").GetString() != "recognized") oldReview++;
            if (parsed.ReviewWarnings.Count > 0)
            {
                newReview++;
                Console.WriteLine(row.GetProperty("file").GetString() + ": " + string.Join("; ", parsed.ReviewWarnings));
                reviews.Add(new JsonObject { ["file"] = row.GetProperty("file").GetString(), ["warnings"] = JsonSerializer.SerializeToNode(parsed.ReviewWarnings) });
            }
            Require(parsed.Modifiers.All(m => m.Kind != "unresolved"), "Unresolved 0.5 modifier");
            Require(parsed.Stats.All(s => s.Origin == "base"), "Unexpected stat origin in 0.5 capture");
            var updated = JsonNode.Parse(row.GetRawText())!;
            updated["item"] = JsonSerializer.SerializeToNode(parsed);
            items.Add(CompactExport.Project(JsonSerializer.SerializeToElement(updated), null));
        }
        Console.WriteLine($"Saved OCR: {items.Count} items; review {oldReview} -> {newReview}");
        using var quality = new TooltipCaptureQuality();
        using var reader = new NativeTextReader();
        var vision = new Vision();
        string charms = Path.Combine(output, "tab-07");
        Directory.CreateDirectory(charms);
        var results = new List<ItemRecognition.Result>();
        foreach (string file in Directory.GetFiles(Path.Combine(source, "tab-07"), "*-tooltip-failed.png").Order())
        {
            var frame = Pixels.Load(file);
            var tip = vision.FindTooltip(frame, 1);
            Require(tip is not null, "Charm tooltip not found: " + file);
            var crop = frame.Crop(tip!.Bounds);
            string recovered = Path.Combine(charms, Path.GetFileName(file).Replace("-tooltip-failed", ""));
            crop.Save(recovered);
            Console.WriteLine($"Charm {Path.GetFileName(file)}: {tip.Bounds}, title={TooltipSegmenter.HasCompleteTitle(crop)}, layout={TooltipSegmenter.HasReadableLayout(crop)}, content={quality.HasReadableStats(crop)}");
            var result = ItemRecognition.RecognizeFile(recovered, reader, Console.WriteLine, CancellationToken.None);
            results.Add(result);
            var row = JsonSerializer.SerializeToElement(result.Data);
            var compact = CompactExport.Project(row, 7);
            Console.WriteLine(compact.ToJsonString());
            Require(TooltipSegmenter.HasCompleteTitle(crop) && TooltipSegmenter.HasReadableLayout(crop) && quality.HasReadableStats(crop), "Charm capture rejected: " + file);
            Require(result.Issue is null, "Charm review: " + result.Issue);
            Require(compact["item_slot"]!.GetValue<string>() == "Charm" && compact["item_level"] is null && compact["stats"]!.AsArray().Count == 0, "Charm fields");
            items.Add(compact);
        }
        ItemRecognition.WriteReport(charms, results, 0, Console.WriteLine);
        CompactExport.WriteItems(output, items);
        ScanExportCheck.CheckItemBrowser(output);
        File.WriteAllText(Path.Combine(output, "review.json"), reviews.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        // Preserve the original OCR's uncertain title instead of hiding it to
        // produce a clean report. Patch-related warnings must all be resolved.
        Require(newReview == 1 && reviews[0]!["file"]!.GetValue<string>() == "tab-06/r04_c01.png"
            && reviews[0]!["warnings"]!.AsArray().Single()!.GetValue<string>() == "Low confidence in item name in line 0", "Unexpected remaining reviews");
        Require(results.Count == 12 && items.Count == 146, "Missing items from patch fixture");
        File.WriteAllText(Path.Combine(output, "checks.txt"), $"Passed: {items.Count} items, including {results.Count} recovered charms; saved review count {oldReview} -> {newReview} (original low-confidence title retained). All five modifier types, enchant bonuses, slot-free ability, relic set, malformed ranks and statless charm checks passed.");
    }

    private static void CheckParser()
    {
        RecognizedLine L(int i, string text) => new(i, new Rectangle(0, i * 20, 300, 16), "white", text, 99);
        RecognizedLine[] lines = [L(0,"EXAMPLE RELIC"), L(1,"Relic 315"), L(2,"Regal Tempered 8/8"),
            L(3,"Power Potential 500"), L(4,"+27 Haste"), L(5,"Attunement Tree: (1/1)"), L(6,"L The Dragon"),
            L(7,"ITEM MODIFIER (Slot: 1)"), L(8,"Glyph: The Mystic +1"),
            L(9,"ITEM MODIFIER (Slot: 2)"), L(10,"Enchant: Banshee's Cunning +5"), L(11,"+6 Critical Strike"), L(12,"+6 Spirit"),
            L(13,"ITEM MODIFIER (Slot: 3)"), L(14,"Embellishment Sapphire +100"),
            L(15,"Ability: Undying Heart"), L(16,"Set: (2/2) Example"), L(17,"Left Alt - Show Details")];
        var item = TooltipParser.Parse(lines);
        Require(item.ReviewWarnings.Count == 0, "New modifiers caused review warnings");
        Require(item.Modifiers.Select(m => m.Kind).SequenceEqual(["glyph","enchant","embellishment","ability","set"]), "Modifier kinds");
        Require(item.Modifiers.Select(m => m.DisplayedSlot).SequenceEqual(new int?[] {1,2,3,null,null}), "Ability/set consumed a slot");
        Require(item.Stats.Single().Value == 27 && item.ImbuedTraits.Single().Name == "The Dragon", "Base stats/attunement");
        var compact = CompactExport.Project(JsonSerializer.SerializeToElement(new { file="r01_c01.png", item }), 1);
        var enchant = compact["mods"]![1]!;
        Require(enchant["enchant"]!.GetValue<string>() == "Banshee's Cunning" && enchant["value"]!.GetValue<int>() == 5
            && enchant["stats"]!.AsArray().Select(s => s!["type"]!.GetValue<string>()).SequenceEqual(["Critical Strike","Spirit"])
            && enchant["stats"]!.AsArray().All(s => s!["value"]!.GetValue<int>() == 6), "Enchant export lost name, rank or bonuses");
        foreach (string heading in new[] {"Aspect: Martial Initiative", "Sigil: Hidden Power"})
        {
            var parsed = TooltipParser.Parse(lines.Select(l => l.Index == 8 ? l with { Text = heading } : l).ToArray());
            Require(parsed.ReviewWarnings.Count == 0 && parsed.Modifiers[0].Kind == heading.Split(':')[0].ToLowerInvariant()
                && parsed.Modifiers[0].Value is null, "Unranked modifier");
        }
        foreach (string heading in new[] {"Glyph: The Mystic +?", "Enchant: Banshee's Cunning +?", "Embellishment Sapphire +?"})
            Require(TooltipParser.Parse(lines.Select(l => l.Index == 8 ? l with { Text = heading } : l).ToArray()).ReviewWarnings.Contains("Unresolved modifier block"), "Malformed rank silently accepted");
        Require(TooltipParser.Parse(lines.Select(l => l.Index == 11 ? l with { Text = "+? Critical Strike" } : l).ToArray()).ReviewWarnings.Contains("Enchant stat not recognized"), "Malformed enchant bonus silently accepted");
        var charm = TooltipParser.Parse([L(0,"PORTAL"), L(1,"Charm"), L(2,"Common Non Temperable"), L(3,"Power Potential 0"), L(4,"Ability: Conjure Portal"), L(5,"Left Alt - Show Details")]);
        Require(charm.ReviewWarnings.Count == 0 && charm.Slot == "Charm" && charm.ItemLevel is null && charm.Stats.Count == 0, "Statless charm");
        var equipment = TooltipParser.Parse([L(0,"EXAMPLE RING"), L(1,"Ring 315"), L(2,"Common Non Temperable"), L(3,"Power Potential 0"), L(4,"Ability: Example"), L(5,"Left Alt - Show Details")]);
        Require(equipment.ReviewWarnings.Contains("No stats recognized"), "Charm exception leaked to equipment");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
