using FsBank.Scanner.Imaging;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Ocr;

// A tooltip may be motionless while the game's stat rows are still stacked on
// top of each other. Confirm a numeric stat (or a charm's metadata and ability).
internal sealed class TooltipCaptureQuality : IDisposable
{
    private NativeTextReader? reader;
    public bool HasReadableStats(Pixels pixels,double scale=1)
    {
        var bands=TooltipSegmenter.Find(pixels,excludeHeaderDecoration:true);
        bool checkedCharm = false;
        foreach(var band in bands)
        {
            // Stats start below the title/metadata and are short, left-aligned
            // white/cyan lines. Ignore prose and the footer before invoking OCR.
            if(band.Box.Top<85*scale || band.Box.Bottom>pixels.Height-45*scale || band.Box.Left>pixels.Width*.15
                || band.Box.Width>pixels.Width*.85 || band.Box.Height>24*scale || band.Color=="mixed")continue;
            var result=(reader ??= new NativeTextReader()).ReadVariant(pixels,band.Box,3,2,7,60);
            if(result.Confidence>=60 && Regex.IsMatch(result.Text,@"^\+\d+\s+[A-Za-z][A-Za-z ]*$"))return true;
            // Ordinary equipment succeeds on its first stat. After a failed
            // candidate, check for a charm before OCRing the rest of its prose.
            if(!checkedCharm)
            {
                checkedCharm=true;
                if(HasCharmAbility(pixels,bands,scale))return true;
            }
        }
        return !checkedCharm && HasCharmAbility(pixels,bands,scale);
    }
    private bool HasCharmAbility(Pixels pixels,IReadOnlyList<TextBand> bands,double scale)
    {
        // Charms intentionally have no stat rows or item level. Require their
        // narrow slot label and pink ability heading, without reading the title,
        // rarity, power potential, descriptions or repeated OCR variants.
        bool charm = false;
        foreach (var band in bands)
        {
            if (band.Box.Bottom > pixels.Height - 35 * scale) continue;
            if(band.Box.Height>24*scale)continue;
            if(!charm && (band.Box.Top<40*scale || band.Box.Top>110*scale
                || band.Box.Left>pixels.Width*.15 || band.Box.Width>pixels.Width*.35))continue;
            if(charm && !HasAbilityColor(pixels,band.Box))continue;
            var result = (reader ??= new NativeTextReader()).ReadVariant(pixels, band.Box, 3, 2, 7, 60);
            if (!charm && Regex.IsMatch(result.Text, @"\bCharm\s*$")) charm = true;
            else if (charm && Regex.IsMatch(result.Text, @"\bAbility:\s*\S")) return true;
        }
        return false;
    }
    private static bool HasAbilityColor(Pixels pixels,Rectangle box)
    {
        for(int y=box.Top;y<box.Bottom;y++)for(int x=box.Left;x<box.Right;x++)
        {
            int i=pixels.Offset(x,y),b=pixels.Data[i],g=pixels.Data[i+1],r=pixels.Data[i+2];
            if(r>120 && r-g>60 && b-g>30)return true;
        }
        return false;
    }
    public void Dispose() => reader?.Dispose();
}
