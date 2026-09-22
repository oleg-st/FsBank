using FsBank.Scanner.Ocr;

using System.Text.RegularExpressions;

namespace FsBank.Scanner.Items;

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
