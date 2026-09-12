using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FsBank.Scanner;

internal sealed record RecognizedLine(int Index, Rectangle Box, string Color, string Text, int Confidence)
{
    public List<OcrSymbol> Symbols { get; init; } = [];
    public string? RawText { get; init; }
    public string? RecognitionText { get; init; }
    public List<TextCorrection> Corrections { get; init; } = [];
}
internal sealed record RecognizedStat(string Name, int Value, string Origin, int SourceLine);
internal sealed class ModifierBlock(int sourceLine)
{
    public int SourceLine { get; } = sourceLine;
    public string Kind { get; set; } = "unresolved";
    public string? Name { get; set; }
    public int? Value { get; set; }
    public int? DisplayedSlot { get; set; }
    public List<string> Lines { get; } = [];
}
internal sealed class ParsedTooltip
{
    public string? Name { get; set; }
    public string? Material { get; set; }
    public string? Slot { get; set; }
    public int? ItemLevel { get; set; }
    public string? Rarity { get; set; }
    public int? TemperedCurrent { get; set; }
    public int? TemperedMaximum { get; set; }
    public bool? Temperable { get; set; }
    public int? PowerPotential { get; set; }
    public bool UniqueEquipped { get; set; }
    public List<RecognizedStat> Stats { get; } = [];
    public List<ModifierBlock> Modifiers { get; } = [];
    public List<string> MetadataLines { get; } = [];
    public List<int> UnparsedLines { get; } = [];
    public List<int> ExplanationLines { get; } = [];
    public List<string> Warnings { get; } = [];
    public int GemSocketLabels { get; set; }
}
internal static class TooltipParser
{
    public static ParsedTooltip Parse(IReadOnlyList<RecognizedLine> lines)
    {
        var result = new ParsedTooltip();
        bool stats = false, footer = false;
        ModifierBlock? block = null;
        foreach (var line in lines)
        {
            string text = line.Text.Replace('’', '\'').Replace('‘', '\'');
            if (text.Contains("Left Alt") || text.Contains("Left Shift")) { footer = true; continue; }
            if (footer) continue;
            // Heading detection tolerates spacing only; raw text always survives.
            if (Regex.IsMatch(text, @"ITEM\s*MODIFIER", RegexOptions.IgnoreCase))
            {
                block = new(line.Index); result.Modifiers.Add(block); block.Lines.Add(text);
                var slotNumber = Regex.Match(text, @"Slot:\s*(\d+)");
                if (slotNumber.Success && int.TryParse(slotNumber.Groups[1].Value, out int slot)) block.DisplayedSlot = slot;
                continue;
            }
            if (text.Contains("Gem Socket")) { result.GemSocketLabels++; block = null; continue; }
            var special = Regex.Match(text, @"\b(Set|Ability):\s*(.+)$");
            if (special.Success)
            {
                block = new(line.Index) { Kind = special.Groups[1].Value.ToLowerInvariant(), Name = special.Groups[2].Value };
                result.Modifiers.Add(block);
            }
            if (block is not null)
            {
                if (block.Kind == "unresolved")
                {
                    var mod = Regex.Match(text, @"\b(Blessing|Trait|Imbued Essence):\s*(.+?)\s*\+(\d+)\s*$");
                    var modStat = Regex.Match(text, @"^\+(\d+)\s+([A-Za-z][A-Za-z ]*)$");
                    if (mod.Success && int.TryParse(mod.Groups[3].Value, out int amount))
                    { block.Kind = mod.Groups[1].Value == "Imbued Essence" ? "gem_power" : mod.Groups[1].Value.ToLowerInvariant(); block.Name = mod.Groups[2].Value; block.Value = amount; }
                    else if (modStat.Success && int.TryParse(modStat.Groups[1].Value, out int bonus))
                    { block.Kind = "stat"; block.Name = modStat.Groups[2].Value; block.Value = bonus; }
                }
                block.Lines.Add(text); continue;
            }
            var power = Regex.Match(text, @"^Power Potential\s+(\d{1,3}(?:,\d{3})+|\d+)$");
            if (power.Success && int.TryParse(power.Groups[1].Value.Replace(",", ""), out int potential))
            { stats = true; result.PowerPotential = potential; continue; }
            var stat = Regex.Match(text, @"^\+(\d+)\s+([A-Za-z][A-Za-z ]*)$");
            if (stats && stat.Success && int.TryParse(stat.Groups[1].Value, out int value))
            {
                result.Stats.Add(new(stat.Groups[2].Value.Trim(), value,
                    line.Color == "cyan" ? "fixed_roll" : line.Color == "white" ? "base" : "unresolved", line.Index));
                continue;
            }
            if (!stats)
            {
                var rarity = Regex.Match(text, @"^\s*(Common|Uncommon|Rare|Epic|Heroic|Regal|Legendary)\b");
                var tempering = Regex.Match(text, @"\b(\d+)\s*/\s*(\d+)\s*\.?$");
                var equipment = Regex.Match(text, @"\b(Back|Head|Hands|Shoulders|Chest|Legs|Feet|Waist|Wrists?|Necklace|Ring|Relic|Weapon|Off-Hand)\b.*?\b(\d+)\s*$");
                if (rarity.Success)
                {
                    result.Rarity = rarity.Groups[1].Value;
                    if(text.Contains("Non Temperable"))result.Temperable=false;
                    else if(tempering.Success && int.TryParse(tempering.Groups[1].Value,out int current)
                        && int.TryParse(tempering.Groups[2].Value,out int maximum) && current<=maximum)
                    {result.Temperable=true;result.TemperedCurrent=current;result.TemperedMaximum=maximum;}
                }
                else if (equipment.Success && int.TryParse(equipment.Groups[2].Value, out int level))
                { result.Slot = equipment.Groups[1].Value; result.ItemLevel = level; }
                else if (text is "Cloth" or "Leather" or "Mail" or "Plate") result.Material = text;
                else if (text == "Unique Equipped" || text.StartsWith("Unique Equipped:")) result.UniqueEquipped = true;
                else if (Regex.IsMatch(text, @"^[A-Z][A-Z '\-’]+$") && !text.Contains("ITEM"))
                    result.Name = result.Name is null ? text : result.Name + " " + text;
                else result.MetadataLines.Add(text);
            }
            else if (text is "The item's potential power; derived from its" or "Item Level, Rarity, and the Modifiers it has.")
                result.ExplanationLines.Add(line.Index);
            else result.UnparsedLines.Add(line.Index);
        }
        if (result.Name is null) result.Warnings.Add("Name not recognized");
        if (!footer) result.Warnings.Add("Footer not recognized; capture may be incomplete");
        if (result.Stats.Count == 0) result.Warnings.Add("No stats recognized");
        if (result.Slot is null || result.ItemLevel is null || result.Rarity is null || result.PowerPotential is null) result.Warnings.Add("Incomplete item metadata");
        if (result.Rarity is not null && result.Temperable is null) result.Warnings.Add("Tempering not recognized; inspect source line");
        if (result.Modifiers.Any(m => m.Kind == "unresolved")) result.Warnings.Add("Unresolved modifier block");
        if (result.UnparsedLines.Count != 0) result.Warnings.Add("Additional text retained in UnparsedLines; inspect source lines");
        if (lines.Any(l => l.Confidence < 60 || l.Text.Length == 0)) result.Warnings.Add("Low confidence or empty text in a detected line");
        foreach (var line in lines.Where(l => Regex.IsMatch(l.Text, @"\([^()\[\]]*\]|\[[^()\[\]]*\)")))
            result.Warnings.Add($"Mismatched brackets in line {line.Index}: visual review required");
        foreach(var line in lines.Where(l=>Regex.IsMatch(l.Text,@"\([A-Z]{3,}\)")))
            result.Warnings.Add($"Parenthesized uppercase token in line {line.Index}: verify bracket shape");
        if (result.Name is not null && (result.Name.Contains("''") || Regex.IsMatch(result.Name, "'S[A-Z]{2,}"))) result.Warnings.Add("Suspicious name spacing/punctuation");
        return result;
    }
}
internal static class ItemRecognition
{
    public static Task<string> Run(string folder, Action<string> log, CancellationToken token) =>
        Task.Run(() => RunCore(folder, log, token), token);

    private static string RunCore(string folder, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string session = CaptureSessions.Resolve(folder);
        if(File.Exists(Path.Combine(session,"batch.json")))
            return BatchRecognition.Run(session,tab=>RunSingle(tab,log,token),log,token);
        return RunSingle(session,log,token);
    }
    private static string RunSingle(string session, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string output = session;
        Directory.CreateDirectory(output);
        var timer = Stopwatch.StartNew();
        using var reader = new NativeTextReader();
        var results = Directory.EnumerateFiles(session, "r??_c??.png").Order(StringComparer.Ordinal)
            .Select(file => RecognizeFile(file, reader, log, token)).ToArray();
        return WriteReport(session, results, timer.Elapsed.TotalSeconds, log);
    }
    internal sealed record Result(string File, object Data, string Card, int Lines);
    internal static Result RecognizeFile(string file, NativeTextReader reader, Action<string> log, CancellationToken token)
    {
        string output = Path.GetDirectoryName(file)!;
        Directory.CreateDirectory(output);
        var cards = new StringBuilder();
        token.ThrowIfCancellationRequested();
        var pixels = Pixels.Load(file);
        var bands = TooltipSegmenter.Find(pixels);
        List<RecognizedLine> lines = [];
        bool title = true;
        bool details = false;
        int previousBottom = 0;
        foreach (var band in bands)
        {
            token.ThrowIfCancellationRequested();
            if (band.Index > 0 && band.Box.Top - previousBottom > 12) title = false;
            var read = reader.Read(pixels, band.Box, title, details);
            if (read.Text.StartsWith("Power Potential")) details = true;
            previousBottom = band.Box.Bottom;
            var repaired = TextGeometry.RestoreSpaces(read.Text, reader.Symbols, pixels, band.Box);
            if(reader.PrimaryText!=read.Text)
                repaired.Corrections.Insert(0,new("ocr_variant_agreement",0,reader.PrimaryText,read.Text,0));
            lines.Add(new(band.Index, band.Box, band.Color, repaired.Text, read.Confidence)
                { Symbols = [..reader.Symbols], RawText = reader.PrimaryText, RecognitionText=read.Text, Corrections = repaired.Corrections });
        }
        var item = TooltipParser.Parse(lines);
        bool captureHeaderComplete=TooltipSegmenter.HasCompleteTitle(pixels);
        if(!captureHeaderComplete)item.Warnings.Add("Incomplete capture header; rescan required");
        string name = Path.GetFileNameWithoutExtension(file);
        using (var annotated = pixels.Bitmap())
        {
            using (var g = Graphics.FromImage(annotated))
            using (var pen = new Pen(Color.Lime, 1))
                foreach (var band in bands) g.DrawRectangle(pen, band.Box);
            annotated.Save(Path.Combine(output, name + "-lines.png"));
        }
        var data = new { file = Path.GetFileName(file), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))),
            status = captureHeaderComplete ? "needs_visual_review" : "rescan_required", captureHeaderComplete, lines, item };
        cards.Append($"<article><h2>{name}</h2><div class='pair'><img src='{name}-lines.png' alt='Detected lines'><img src='data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(file))}' alt='Source'></div><pre>{WebUtility.HtmlEncode(JsonSerializer.Serialize(item, JsonOptions))}</pre><table><tr><th>Line</th><th>Color</th><th>Confidence</th><th>Raw text</th></tr>");
        foreach (var line in lines) cards.Append($"<tr><td>{line.Index}</td><td>{line.Color}</td><td>{line.Confidence}</td><td>{WebUtility.HtmlEncode(line.Text)}"
            + (line.Corrections.Count > 0 ? $"<details><summary>OCR corrections: {line.Corrections.Count}</summary>Raw OCR: {WebUtility.HtmlEncode(line.RawText)}<br>{WebUtility.HtmlEncode(JsonSerializer.Serialize(line.Corrections))}</details>" : "") + "</td></tr>");
        cards.Append("</table></article>");

        log($"{name}: {lines.Count} lines, {item.Stats.Count} stats, {item.Modifiers.Count} modifier blocks");
        return new(file, data, cards.ToString(), lines.Count);

    }
    internal static string WriteReport(string session, IEnumerable<Result> results, double seconds, Action<string> log)
    {
        string output = session;
        Directory.CreateDirectory(output);
        var ordered = results.OrderBy(r => r.File, StringComparer.Ordinal).ToArray();
        var items = ordered.Select(r => r.Data).ToList();
        var cards = new StringBuilder(string.Concat(ordered.Select(r => r.Card)));
        int lineCount = ordered.Sum(r => r.Lines);
        var summary = new { items = items.Count, lines = lineCount, seconds = seconds,
            engine = "Tesseract native / title 2x, detail 3x / contrast + padding / measured gaps / DAWG dictionaries disabled", status = "needs_visual_review",
            scope = "Alt tooltip prototype. Modifier descriptions retained verbatim; not schema v2. No font atlas yet." };
        File.WriteAllText(Path.Combine(output, "full.json"), JsonSerializer.Serialize(new { summary, items }, JsonOptions));
        CompactExport.Write(output, items.Select(item => JsonSerializer.SerializeToElement(item)));
        string report = Path.Combine(output, "report.html");
        File.WriteAllText(report, "<!doctype html><meta charset='utf-8'><title>FsBank — C# OCR</title><style>body{background:#141c25;color:#eee;font:15px system-ui;margin:24px}article{border-top:1px solid #789;padding:20px 0}img{max-width:45%;object-fit:contain;align-self:start}.pair{display:flex;gap:20px}td,th{text-align:left;padding:4px 12px;border-bottom:1px solid #456}pre{white-space:pre-wrap}</style><h1>Alt tooltip recognition — C#</h1><p>All results require visual review. Green rectangles show detected lines. Confidence is not a correctness guarantee.</p><pre>" + WebUtility.HtmlEncode(JsonSerializer.Serialize(summary, JsonOptions)) + "</pre>" + cards);
        log($"Done: {items.Count} items in {seconds:F1} s.");
        return report;
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
