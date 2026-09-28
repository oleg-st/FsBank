using FsBank.Scanner.Imaging;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Ocr;

// A tooltip may be motionless while the game's stat rows are still stacked on
// top of each other. Confirm a readable numeric stat before accepting that frame.
internal sealed class TooltipCaptureQuality : IDisposable
{
    private NativeTextReader? reader;
    public bool HasReadableStats(Pixels pixels,double scale=1)
    {
        var bands=TooltipSegmenter.Find(pixels,excludeHeaderDecoration:true);
        foreach(var band in bands)
        {
            // Stats start below the title/metadata and are short, left-aligned
            // white/cyan lines. Ignore prose and the footer before invoking OCR.
            if(band.Box.Top<85*scale || band.Box.Bottom>pixels.Height-45*scale || band.Box.Left>pixels.Width*.15
                || band.Box.Width>pixels.Width*.85 || band.Box.Height>24*scale || band.Color=="mixed")continue;
            var result=(reader ??= new NativeTextReader()).ReadVariant(pixels,band.Box,3,2,7,60);
            if(result.Confidence>=60 && Regex.IsMatch(result.Text,@"^\+\d+\s+[A-Za-z][A-Za-z ]*$"))return true;
        }
        return false;
    }
    public void Dispose() => reader?.Dispose();
}
