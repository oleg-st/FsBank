using FsBank.Scanner.Ocr;

using System.Text.RegularExpressions;

namespace FsBank.Scanner.Items;

internal static class TooltipParser
{
    public static ParsedTooltip Parse(IReadOnlyList<RecognizedLine> lines)
    {
        var result = new ParsedTooltip();
        bool stats = false, footer = false;
        int nameLineHeight = 0;
        int nameBottom = 0;
        bool imbuedTraits = false;
        int remainingTraits = 0;
        ModifierBlock? block = null;
        HashSet<int> informationalLines = [];
        HashSet<int> nameLines = [];
        List<int> unrecognizedNameLines = [];
        // The top ornament can be segmented before the first title line. Use
        // the actual title's height so uppercase border noise never joins Name.
        int headerTitleHeight=lines.TakeWhile(l=>!Regex.IsMatch(l.Text,@"\b(Back|Head|Hands|Shoulders|Chest|Legs|Feet|Waist|Wrists?|Necklace|Ring|Relic|Weapon|Off-Hand)\b.*?\b\d+\s*$")
                && !Regex.IsMatch(l.Text,@"^\s*(Common|Uncommon|Rare|Epic|Heroic|Regal|Legendary|Power Potential)\b"))
            .Where(l=>Regex.IsMatch(l.Text,@"^[A-Z][A-Z '\-’]+$")).Select(l=>l.Box.Height).DefaultIfEmpty(0).Max();
        foreach (var line in lines)
        {
            string text = line.Text.Replace('’', '\'').Replace('‘', '\'');
            if (text.Contains("Left Alt") || text.Contains("Left Shift")) { footer = true; informationalLines.Add(line.Index); continue; }
            if (footer) { informationalLines.Add(line.Index); continue; }
            // Heading detection tolerates spacing only; raw text always survives.
            if (Regex.IsMatch(text, @"ITEM\s*MODIFIER", RegexOptions.IgnoreCase))
            {
                imbuedTraits = false;
                block = new(line.Index); result.Modifiers.Add(block); block.Lines.Add(text);
                var slotNumber = Regex.Match(text, @"Slot:\s*(\d+)");
                if (slotNumber.Success && int.TryParse(slotNumber.Groups[1].Value, out int slot)) block.DisplayedSlot = slot;
                continue;
            }
            if (text.Contains("Gem Socket")) { result.GemSocketLabels++; block = null; imbuedTraits = false; continue; }
            var traits = Regex.Match(text, @"\bImbued Traits\s*\((\d+)\s*/\s*(\d+)\)\s*$");
            if (stats && traits.Success && int.TryParse(traits.Groups[1].Value,out int traitCount))
            {
                imbuedTraits = traitCount > 0; remainingTraits = traitCount; block = null;
                result.MetadataLines.Add(text); informationalLines.Add(line.Index);
                continue;
            }
            var special = Regex.Match(text, @"\b(Set|Ability):\s*(.+)$");
            if (special.Success)
            {
                imbuedTraits = false;
                block = new(line.Index) { Kind = special.Groups[1].Value.ToLowerInvariant(), Name = special.Groups[2].Value };
                result.Modifiers.Add(block);
            }
            if (imbuedTraits)
            {
                // Imbued Traits are intentionally omitted from the compact export.
                // Recognize them separately, retaining source line references for diagnostics.
                // Limit this to the displayed count so unrelated unknown text still warns.
                informationalLines.Add(line.Index);
                string traitName = Regex.Replace(text, @"^\s*(?:[ULI|]{1,2}\s+|[└├│─┗┣┃━]+\s*)", "").Trim();
                if (Regex.IsMatch(traitName, @"^[A-Za-z][A-Za-z '\-]+$"))
                {
                    result.ImbuedTraits.Add(new(traitName,line.Index));
                    informationalLines.Add(line.Index);
                }
                else result.UnparsedLines.Add(line.Index);
                if (--remainingTraits == 0) imbuedTraits = false;
                continue;
            }
            if (block is not null)
            {
                // A resolved modifier's remaining prose is retained in diagnostics,
                // but is not used by CompactExport. Its heading/value still matters.
                if(block.Kind != "unresolved" && !special.Success)informationalLines.Add(line.Index);
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
                    line.Color == "cyan" ? "dynamic" : line.Color == "white" ? "base" : "unresolved", line.Index));
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
                else if (result.Slot is null && headerTitleHeight>0 && line.Box.Height<headerTitleHeight*.75)
                { result.MetadataLines.Add(text); informationalLines.Add(line.Index); }
                else if (nameLineHeight > 0 && result.Slot is null && line.Box.Height < nameLineHeight * .75
                    && line.Box.Top >= nameBottom && line.Box.Top-nameBottom < nameLineHeight)
                {
                    // Thin ornament directly below the title, before the slot line.
                    // It may OCR as lowercase noise (e.g. "eee"), not only capitals.
                    result.MetadataLines.Add(text); informationalLines.Add(line.Index);
                }
                else if (Regex.IsMatch(text, @"^[A-Z][A-Z '\-’]+$") && !text.Contains("ITEM"))
                {
                    // The ornamental separator can OCR as uppercase letters,
                    // but is shorter than the title font. Keep it as evidence.
                    if (nameLineHeight > 0 && line.Box.Height < nameLineHeight * .75)
                    { result.MetadataLines.Add(text); informationalLines.Add(line.Index); continue; }
                    nameLineHeight = Math.Max(nameLineHeight, line.Box.Height);
                    nameBottom = line.Box.Bottom;
                    nameLines.Add(line.Index);
                    // OCR can split one possessive apostrophe into curly + straight.
                    text = Regex.Replace(text, @"(?<=[A-Z])'{2,}(?=S\b)", "'");
                    // A title wrapped after a hyphen continues the same compound word.
                    result.Name = result.Name is null ? text : result.Name + (result.Name.EndsWith('-') ? "" : " ") + text;
                }
                else
                {
                    result.MetadataLines.Add(text);
                    // Unknown title text can silently truncate an otherwise valid Name.
                    if (result.Slot is null) unrecognizedNameLines.Add(line.Index);
                }
            }
            else if (text is "The item's potential power; derived from its" or "Item Level, Rarity, and the Modifiers it has.")
            { result.ExplanationLines.Add(line.Index); informationalLines.Add(line.Index); }
            else result.UnparsedLines.Add(line.Index);
        }
        if (result.Name is null) result.Warn("Name not recognized");
        if (!footer) result.Warn("Footer not recognized; capture may be incomplete", needsReview:false);
        if (result.Stats.Count == 0) result.Warn("No stats recognized");
        if (result.Slot is null) result.Warn("Item slot not recognized");
        if (result.ItemLevel is null) result.Warn("Item level not recognized");
        if (result.Rarity is null) result.Warn("Rarity not recognized");
        if (result.PowerPotential is null) result.Warn("Power potential not recognized", needsReview:false);
        if (result.Temperable is null) result.Warn("Tempering not recognized; inspect source line");
        if (result.Stats.Any(s => s.Origin == "unresolved")) result.Warn("Stat origin not recognized");
        if (result.Modifiers.Any(m => m.Kind == "unresolved")) result.Warn("Unresolved modifier block");
        if (unrecognizedNameLines.Count > 0) result.Warn("Unrecognized item name text in lines " + string.Join(", ", unrecognizedNameLines));
        if (result.UnparsedLines.Count != 0) result.Warn("Additional text retained in UnparsedLines; inspect source lines",
            result.UnparsedLines.Any(index=>!informationalLines.Contains(index)));
        var uncertain=lines.Where(l => l.Confidence < 60 || l.Text.Length == 0).ToArray();
        // Structured fields are assessed by the values we extracted, not OCR's
        // confidence in their source words. Free-form item names have no such check.
        if (uncertain.Length>0) result.Warn("Low confidence or empty text in a detected line", needsReview:false);
        foreach (var line in lines.Where(l => nameLines.Contains(l.Index) && (l.VerifiedConfidence ?? l.Confidence) < 60))
            result.Warn($"Low confidence in item name in line {line.Index}");
        foreach (var line in lines.Where(l => Regex.IsMatch(l.Text, @"\([^()\[\]]*\]|\[[^()\[\]]*\)")))
            result.Warn($"Mismatched brackets in line {line.Index}: visual review required",nameLines.Contains(line.Index));
        foreach(var line in lines.Where(l=>Regex.IsMatch(l.Text,@"\([A-Z]{3,}\)")))
            result.Warn($"Parenthesized uppercase token in line {line.Index}: verify bracket shape",nameLines.Contains(line.Index));
        if (result.Name is not null && (result.Name.Contains("''") || Regex.IsMatch(result.Name, "'S[A-Z]{2,}"))) result.Warn("Suspicious name spacing/punctuation");
        return result;
    }
}
