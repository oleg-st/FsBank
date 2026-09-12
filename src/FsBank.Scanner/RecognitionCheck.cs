using System.Text.Json;

namespace FsBank.Scanner;

// Regression checks against visible facts in the supplied Alt capture, not OCR agreement.
internal static class RecognitionCheck
{
    public static void Run(string session)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(CompactExport.FullReportPath(session)));
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToDictionary(x => x.GetProperty("file").GetString()!);
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var first = items["r01_c01.png"].GetProperty("item");
        Require(first.GetProperty("Name").GetString() == "PATCHWORK RAT FUR CAPE", "First title");
        Require(first.GetProperty("ItemLevel").GetInt32() == 315, "First item level");
        Require(first.GetProperty("PowerPotential").GetInt32() == 420, "First potential");
        var stats = first.GetProperty("Stats").EnumerateArray().ToArray();
        Require(stats.Length == 5, "First item has five stat lines");
        Require(stats.Any(s => s.GetProperty("Name").GetString() == "Intellect" && s.GetProperty("Value").GetInt32() == 3 && s.GetProperty("Origin").GetString() == "base"), "Base intellect");
        Require(stats.Any(s => s.GetProperty("Name").GetString() == "Intellect" && s.GetProperty("Value").GetInt32() == 2 && s.GetProperty("Origin").GetString() == "fixed_roll"), "Fixed intellect");
        var mods = first.GetProperty("Modifiers").EnumerateArray().ToArray();
        Require(mods.Length == 2 && mods[0].GetProperty("Kind").GetString() == "stat" && mods[0].GetProperty("Value").GetInt32() == 7, "Haste modifier is not base");
        var cloak = items["r04_c05.png"].GetProperty("item");
        Require(cloak.GetProperty("Stats").EnumerateArray().Count(s => s.GetProperty("Name").GetString() == "Intellect" && s.GetProperty("Value").GetInt32() == 2) == 2, "Repeated fixed rolls preserved");
        Require(cloak.GetProperty("Modifiers").EnumerateArray().Count(s => s.GetProperty("Kind").GetString() == "gem_power" && s.GetProperty("Value").GetInt32() == 100) == 2, "Repeated gems preserved");
        // Synthetic parser regression: a set bonus must not become an item stat.
        RecognizedLine L(int index, string text) => new(index, new Rectangle(1, 1, 100, 10), "white", text, 99);
        var parsed = TooltipParser.Parse(new[] { L(0, "EXAMPLE"), L(1, "Power Potential 100"), L(2, "+5 Intellect"), L(3, "Set: (2/2) Example"), L(4, "+54 Intellect"), L(5, "Left Alt - Show Details") });
        Require(parsed.Stats.Count == 1 && parsed.Modifiers.Single().Lines.Contains("+54 Intellect"), "Set description leaked into stats");
        var punctuation = TooltipParser.Parse(new[] { L(0, "CHALICE OF AL‘ZERAC’S"), L(1, "ESSENCE"), L(2, "Power Potential 345"), L(3, "(CORE]ability") });
        Require(punctuation.Name == "CHALICE OF AL'ZERAC'S ESSENCE", "Apostrophe typography loses title");
        Require(punctuation.Warnings.Any(w => w.StartsWith("Mismatched brackets")), "Mixed brackets not flagged");
        var info = TooltipParser.Parse(new[] { L(0, "EXAMPLE"), L(1, "Power Potential 100"), L(2, "The item's potential power; derived from its"), L(3, "Item Level, Rarity, and the Modifiers it has."), L(4,"An unknown detail") });
        Require(info.ExplanationLines.Count == 2 && info.UnparsedLines.SequenceEqual(new[]{4}), "Information filtering hides unknown text");
        var gapImage = new Pixels(20, 10);
        for (int y = 1; y < 9; y++) for(int x = 1; x < 14; x++)
            if (x < 5 || x >= 9) { int offset=gapImage.Offset(x,y); gapImage.Data[offset]=gapImage.Data[offset+1]=gapImage.Data[offset+2]=255; }
        OcrSymbol[] glyphs = [new("a",new RectangleF(1,1,4,8)),new("1",new RectangleF(9,1,5,8))];
        Require(TextGeometry.RestoreSpaces("a1",glyphs,gapImage,gapImage.Bounds).Text == "a 1", "Visible gap not restored");
        Require(TextGeometry.RestoreSpaces("a 1",glyphs,gapImage,gapImage.Bounds).Corrections.Count == 0, "Existing space duplicated");
        Require(TextGeometry.RestoreSpaces("b1",glyphs,gapImage,gapImage.Bounds).Corrections.Count == 0, "Mismatched symbols must abstain");
        for(int x=5;x<9;x++)gapImage.Data[gapImage.Offset(x,5)]=255;
        Require(TextGeometry.RestoreSpaces("a1",glyphs,gapImage,gapImage.Bounds).Corrections.Count == 0, "Gap invented without pixel evidence");
        foreach (var row in items.Values)
        {
            using var bitmap = new Bitmap(Path.Combine(session, row.GetProperty("file").GetString()!));
            int bottom = 0;
            foreach (var line in row.GetProperty("lines").EnumerateArray())
            {
                var box = line.GetProperty("Box");
                int top = box.GetProperty("Y").GetInt32();
                Require(top >= bottom && box.GetProperty("Right").GetInt32() <= bitmap.Width && box.GetProperty("Bottom").GetInt32() <= bitmap.Height, "Overlapping or out-of-image lines");
                bottom = box.GetProperty("Bottom").GetInt32();
            }
        }
        File.WriteAllText(Path.Combine(session, "checks.txt"), "Passed: selected visible fields, duplicate rolls/gems, modifier boundaries, all line bounds, apostrophe normalization, bracket warning, explanation filtering, and evidence-based spacing/abstention. This is not a full transcription accuracy audit.");
    }
}
