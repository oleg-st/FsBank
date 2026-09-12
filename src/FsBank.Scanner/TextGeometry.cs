using System.Text;
using System.Text.RegularExpressions;

namespace FsBank.Scanner;

internal sealed record TextCorrection(string Kind, int Offset, string Before, string After, int BlankColumns);

internal static class TextGeometry
{
    // Restrict repairs to letter/number boundaries with a measured empty gap.
    // English ordinal suffixes stay attached; no known item names are used.
    // Overlapping OCR symbol boxes are common, so inspect bounded gaps in pixels.
    public static (string Text, List<TextCorrection> Corrections) RestoreSpaces(
        string raw, IReadOnlyList<OcrSymbol> symbols, Pixels pixels, Rectangle line)
    {
        List<TextCorrection> corrections = [];
        if (symbols.Count < 2 || symbols.Any(s => s.Text.Length != 1)) return (raw, corrections);
        var characters = raw.Select((value, index) => (value, index)).Where(c => !char.IsWhiteSpace(c.value)).ToArray();
        if (characters.Length != symbols.Count || characters.Where((c, i) => c.value != symbols[i].Text[0]).Any()) return (raw, corrections);
        for (int i = 1; i < symbols.Count; i++)
        {
            var previous = characters[i-1]; var current = characters[i];
            bool boundary = char.IsLetter(previous.value) && char.IsDigit(current.value)
                || char.IsDigit(previous.value) && char.IsLetter(current.value);
            if (!boundary || current.index != previous.index+1) continue;
            // Ordinal suffixes are part of the number even when glyph spacing is wide.
            if(char.IsDigit(previous.value) && Regex.IsMatch(raw[current.index..], @"^(?:st|nd|rd|th)\b", RegexOptions.IgnoreCase))continue;
            if(symbols[i-1].Box.Width>line.Height*2 || symbols[i].Box.Width>line.Height*2)continue;
            int left = Math.Max(0, (int)Math.Floor(symbols[i-1].Box.Left));
            int right = Math.Min(pixels.Width, (int)Math.Ceiling(symbols[i].Box.Right));
            int run = 0, longest = 0;bool seenInk=false;
            for (int x = left; x < right; x++)
            {
                bool ink = false;
                for (int y = Math.Max(0,line.Top); y < Math.Min(pixels.Height,line.Bottom); y++)
                    if (pixels.Bright(x,y) >= 95) { ink = true; break; }
                // Only gaps bounded by ink count. Overlapping symbol boxes can put
                // the nominal centre of a glyph inside the following space.
                if(ink){if(seenInk)longest=Math.Max(longest,run);seenInk=true;run=0;}
                else if(seenInk)run++;
            }
            if (longest >= 3) corrections.Add(new("visible_letter_number_gap",current.index,""," ",longest));
        }
        var text = new StringBuilder(raw);
        foreach (var correction in corrections.AsEnumerable().Reverse()) text.Insert(correction.Offset, correction.After);
        return (text.ToString(), corrections);
    }
}
