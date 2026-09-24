using FsBank.Scanner.Imaging;

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Ocr;

internal sealed record OcrSymbol(string Text, RectangleF Box);

// A replaceable character reader. Segmentation and item interpretation stay in C#.
internal sealed class NativeTextReader : IDisposable
{
    private const string Library = "tesseract55.dll";
    private IntPtr handle;
    internal List<OcrSymbol> Symbols { get; } = [];
    internal string PrimaryText { get; private set; } = "";
    internal int? VerifiedConfidence { get; private set; }
    static NativeTextReader()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeTextReader).Assembly, (name, assembly, search) =>
            name == Library ? NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Ocr", name)) : IntPtr.Zero);
    }
    public NativeTextReader()
    {
        handle = TessBaseAPICreate();
        try
        {
            foreach (string variable in new[] { "load_system_dawg", "load_freq_dawg", "load_punc_dawg", "load_number_dawg", "load_unambig_dawg", "load_bigram_dawg" })
                if (TessBaseAPISetVariable(handle, variable, "0") == 0) throw new InvalidOperationException("Unknown OCR setting: " + variable);
            if (TessBaseAPIInit3(handle, Path.Combine(AppContext.BaseDirectory, "Assets", "Ocr"), "eng") != 0)
                throw new InvalidOperationException("Cannot initialize bundled OCR model.");
            TessBaseAPISetPageSegMode(handle, 7);
        }
        catch { Dispose(); throw; }
    }
    public unsafe (string Text, int Confidence) Read(Pixels pixels, Rectangle box, bool title = false, bool details = true)
    {
        var primary=ReadVariant(pixels,box,title ? 2 : 3,title || details ? 2 : 0,7,title || details ? 60 : 0);
        PrimaryText=primary.Text;
        VerifiedConfidence=null;
        if(primary.Text.Length==0 && !title && !details)
        {
            // Small, dim material labels (such as Mail) can disappear in the
            // unpadded metadata read. Recover only on two matching confident reads.
            var emptySymbols=Symbols.ToArray();
            var emptyCandidates=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(int scale in new[]{2,3,4})
            {
                var candidate=ReadVariant(pixels,box,scale,2,7,60);
                if(candidate.Confidence<80 || candidate.Text.Length==0)continue;
                if(emptyCandidates.TryGetValue(candidate.Text,out int confidence))
                    return (candidate.Text,Math.Min(confidence,candidate.Confidence));
                emptyCandidates.Add(candidate.Text,candidate.Confidence);
            }
            Symbols.Clear();Symbols.AddRange(emptySymbols);
        }
        if(primary.Confidence<60 && primary.Text.Length>0)
        {
            var primarySymbols=Symbols.ToArray();
            var confirmations=new List<int>();
            foreach(var (scale,black) in new[]{(2,0),(3,0),(2,60),(3,60),(4,0),(4,60)})
            {
                var check=ReadVariant(pixels,box,scale,2,7,black);
                if(check.Confidence>=60 && ConfidenceText(check.Text)==ConfidenceText(primary.Text))
                    confirmations.Add(check.Confidence);
                if(confirmations.Count==2){VerifiedConfidence=confirmations.Min();break;}
            }
            Symbols.Clear();Symbols.AddRange(primarySymbols);
        }
        if(!NeedsReview(primary.Text))return primary;
        var originalSymbols=Symbols.ToArray();
        bool metadata=Regex.IsMatch(primary.Text,@"^(Common|Uncommon|Rare|Epic|Heroic|Regal|Legendary)\b");
        var candidates=new Dictionary<string,int>(StringComparer.Ordinal);
        // Metadata spans two distant columns. Background noise and scaling can
        // spoil one read; require two exact, high-confidence reads of the full
        // line, including its numbers, before replacing the primary result.
        var variants=metadata ? new[]{(3,60),(4,60),(2,60)} : new[]{(3,0),(2,60)};
        foreach(var (scale,black) in variants)
        {
            var candidate=ReadVariant(pixels,box,scale,2,7,black);
            if(candidate.Confidence<80 || candidate.Text.Length==0 || candidate.Text==primary.Text
                || NeedsReview(candidate.Text))continue;
            if(candidates.TryGetValue(candidate.Text,out int confidence))
            {
                VerifiedConfidence=null;
                return (candidate.Text,Math.Min(confidence,candidate.Confidence));
            }
            candidates.Add(candidate.Text,candidate.Confidence);
        }
        Symbols.Clear();Symbols.AddRange(originalSymbols);
        return primary;
    }
    // Ignore only the icon before a parsed Set/Ability label. The full heading
    // and its value must agree; uncertain names/numbers cannot be voted away.
    private static string ConfidenceText(string text)
    {
        var heading=Regex.Match(text,@"\b(?:Set|Ability):\s*.+$");
        return heading.Success ? heading.Value : text;
    }
    private static bool NeedsReview(string text)
    {
        if(Regex.IsMatch(text,@"\([^()\[\]]*\]|\[[^()\[\]]*\)|\([A-Z]{3,}\)|[A-Za-z]!"))return true;
        return Regex.IsMatch(text,@"^(Common|Uncommon|Rare|Epic|Heroic|Regal|Legendary)\b")
            && !Regex.IsMatch(text,@"^(Common|Uncommon|Rare|Epic|Heroic|Regal|Legendary)\s+(?:Tempered\s+\d+\s*/\s*\d+|Non Temperable)$");
    }

    internal unsafe (string Text, int Confidence) ReadVariant(Pixels pixels, Rectangle box, int scale, int padding, int mode, int black)
    {
        const int border = 12;
        box = Rectangle.Intersect(pixels.Bounds, Rectangle.FromLTRB(box.Left, box.Top-padding, box.Right, box.Bottom+padding));
        TessBaseAPISetPageSegMode(handle, mode);
        int width = box.Width * scale + border * 2, height = box.Height * scale + border * 2;
        byte[] data = new byte[width * height];
        Array.Fill(data, (byte)255);
        using var source = pixels.Crop(box).Bitmap();
        using var enlarged = new Bitmap(box.Width * scale, box.Height * scale);
        using (var graphics = Graphics.FromImage(enlarged))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, enlarged.Width, enlarged.Height));
        }
        for (int y = 0; y < box.Height * scale; y++)
            for (int x = 0; x < box.Width * scale; x++)
            {
                var color = enlarged.GetPixel(x, y);
                int bright = Math.Max(color.R, Math.Max(color.G, color.B));
                data[(y + border) * width + x + border] = (byte)(255 - Math.Clamp((bright-black)*255/(255-black), 0, 255));
            }
        fixed (byte* pointer = data)
        {
            TessBaseAPISetImage(handle, (IntPtr)pointer, width, height, 1, width);
            IntPtr text = TessBaseAPIGetUTF8Text(handle);
            if (text == IntPtr.Zero) throw new InvalidOperationException("OCR returned no text buffer.");
            try
            {
                Symbols.Clear();
                IntPtr iterator = TessBaseAPIGetIterator(handle);
                if (iterator != IntPtr.Zero)
                {
                    try
                    {
                        var page = TessResultIteratorGetPageIterator(iterator);
                        do
                        {
                            IntPtr symbol = TessResultIteratorGetUTF8Text(iterator, 4);
                            try
                            {
                                string value = Marshal.PtrToStringUTF8(symbol) ?? "";
                                if (value.Length > 0 && TessPageIteratorBoundingBox(page, 4, out int l, out int t, out int r, out int b) != 0)
                                    Symbols.Add(new(value, RectangleF.FromLTRB(box.Left+(l-border)/(float)scale, box.Top+(t-border)/(float)scale, box.Left+(r-border)/(float)scale, box.Top+(b-border)/(float)scale)));
                            }
                            finally { if(symbol != IntPtr.Zero) TessDeleteText(symbol); }
                        } while(TessResultIteratorNext(iterator, 4) != 0);
                    }
                    finally { TessResultIteratorDelete(iterator); }
                }
                return ((Marshal.PtrToStringUTF8(text) ?? "").Trim(), TessBaseAPIMeanTextConf(handle));
            }
            finally { TessDeleteText(text); TessBaseAPIClear(handle); }
        }
    }
    public void Dispose() { if (handle != IntPtr.Zero) { TessBaseAPIDelete(handle); handle = IntPtr.Zero; } }
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr TessBaseAPIGetIterator(IntPtr api);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr TessResultIteratorGetPageIterator(IntPtr iterator);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int TessResultIteratorNext(IntPtr iterator, int level);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr TessResultIteratorGetUTF8Text(IntPtr iterator, int level);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int TessPageIteratorBoundingBox(IntPtr iterator, int level, out int l, out int t, out int r, out int b);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void TessResultIteratorDelete(IntPtr iterator);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr TessBaseAPICreate();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void TessBaseAPIDelete(IntPtr api);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int TessBaseAPISetVariable(IntPtr api, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int TessBaseAPIInit3(IntPtr api, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string language);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void TessBaseAPISetPageSegMode(IntPtr api, int mode);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void TessBaseAPISetImage(IntPtr api, IntPtr bytes, int width, int height, int bytesPerPixel, int stride);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr TessBaseAPIGetUTF8Text(IntPtr api);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int TessBaseAPIMeanTextConf(IntPtr api);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void TessDeleteText(IntPtr text);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void TessBaseAPIClear(IntPtr api);
}
