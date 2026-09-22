using FsBank.Scanner.Imaging;

namespace FsBank.Scanner.Ocr;

internal sealed record TextBand(int Index, Rectangle Box, string Color);

internal static class TooltipSegmenter
{
    // Inspect only the header. A cropped title leaves body text as the first band.
    // Title colours differ from the neutral material/equipment text below it.
    internal static bool HasCompleteTitle(Pixels pixels, double scale=1)
    {
        var header=pixels.Crop(new Rectangle(0,0,pixels.Width,Math.Min(pixels.Height,(int)Math.Ceiling(85*scale))));
        var bands=Find(header);
        foreach(var band in bands)
        {
            if(band.Box.Height<11*scale || band.Box.Width<40*scale)continue;
            return band.Box.Top>=9*scale && band.Box.Top<=28*scale && band.Color!="white";
        }
        return false;
    }
    // Current capture profile: discard the ornamental border, not the text margin.
    public static List<TextBand> Find(Pixels pixels)
    {
        List<TextBand> result = [];
        int left = 14, right = pixels.Width - 14;
        if (right <= left || pixels.Height < 20) return result;
        bool Ink(int x, int y) => pixels.Bright(x, y) >= 95;
        bool Row(int y)
        {
            int count = 0, longest = 0, run = 0;
            for (int x = left; x < right; x++)
            {
                if (Ink(x, y)) { count++; longest = Math.Max(longest, ++run); } else run = 0;
            }
            return count >= 3 && longest < (right - left) * .7;
        }
        int start = -1, last = -1;
        for (int y = 10; y <= pixels.Height - 8; y++)
        {
            bool ink = y < pixels.Height - 8 && Row(y);
            if (ink) { if (start < 0) start = y; last = y; }
            if (start < 0 || (ink || y - last <= 2) && y < pixels.Height - 8) continue;
            if (last - start >= 4)
            {
                int min = right, max = left, cyan = 0, white = 0, other = 0;
                for (int yy = start; yy <= last; yy++) for (int x = left; x < right; x++)
                {
                    if (!Ink(x, yy)) continue;
                    min = Math.Min(min, x); max = Math.Max(max, x);
                    int i = pixels.Offset(x, yy), b = pixels.Data[i], g = pixels.Data[i+1], r = pixels.Data[i+2];
                    if (Math.Min(b,g)-r > 35 && Math.Min(b,g)>100) cyan++;
                    else if (Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<45) white++;
                    else other++;
                }
                if (max - min >= 3)
                {
                    int total = cyan + white + other;
                    string color = cyan > total * .65 ? "cyan" : white > total * .65 ? "white" : "mixed";
                    result.Add(new(result.Count, Rectangle.FromLTRB(Math.Max(left,min-1), Math.Max(0,start-1), Math.Min(right,max+2), Math.Min(pixels.Height,last+2)), color));
                }
            }
            start = last = -1;
        }
        return result;
    }
}
