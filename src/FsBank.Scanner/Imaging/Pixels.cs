using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FsBank.Scanner.Imaging;

// BGRA, top-down, tightly packed. Capture reuses this buffer; Crop owns its copy.
public sealed class Pixels(int width, int height)
{
    public const int BytesPerPixel = 4;
    public const int ColorChannels = 3;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Data { get; } = new byte[checked(width * height * BytesPerPixel)];
    public Rectangle Bounds => new(0, 0, Width, Height);
    public int Offset(int x, int y) => (y * Width + x) * BytesPerPixel;
    public int Bright(int x, int y)
    {
        int i = Offset(x, y);
        return Math.Max(Data[i], Math.Max(Data[i + 1], Data[i + 2]));
    }
    public Pixels Crop(Rectangle rectangle)
    {
        if (!Bounds.Contains(rectangle) || rectangle.IsEmpty) throw new ArgumentOutOfRangeException(nameof(rectangle));
        var copy = new Pixels(rectangle.Width, rectangle.Height);
        for (int y = 0; y < rectangle.Height; y++)
            Buffer.BlockCopy(Data, Offset(rectangle.X, rectangle.Y + y), copy.Data, y * copy.Width * BytesPerPixel, copy.Width * BytesPerPixel);
        return copy;
    }
    public Bitmap Bitmap()
    {
        // DXGI alpha is unspecified. RGB output ignores that byte without an
        // extra full-frame alpha pass on every acquired frame.
        var image = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
        var bits = image.LockBits(Bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < Height; y++) Marshal.Copy(Data, y * Width * BytesPerPixel, bits.Scan0 + y * bits.Stride, Width * BytesPerPixel);
        }
        finally { image.UnlockBits(bits); }
        return image;
    }
    public void Save(string path) { using var image = Bitmap(); image.Save(path, ImageFormat.Png); }
    public static Pixels Load(string path)
    {
        using var source = new Bitmap(path);
        using var image = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(image)) g.DrawImageUnscaled(source, 0, 0);
        var result = new Pixels(image.Width, image.Height);
        var bits = image.LockBits(result.Bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < result.Height; y++) Marshal.Copy(bits.Scan0 + y * bits.Stride, result.Data, y * result.Width * BytesPerPixel, result.Width * BytesPerPixel); }
        finally { image.UnlockBits(bits); }
        return result;
    }
    public double Difference(Pixels other, Rectangle area, int step = DetectionConstants.DefaultDifferenceStep)
    {
        if (Width != other.Width || Height != other.Height) return byte.MaxValue;
        long sum = 0, count = 0;
        for (int y = area.Top; y < area.Bottom; y += step)
            for (int x = area.Left; x < area.Right; x += step)
            {
                int i = Offset(x, y);
                for (int c = 0; c < ColorChannels; c++) sum += Math.Abs(Data[i+c] - other.Data[i+c]);
                count += ColorChannels;
            }
        return count == 0 ? byte.MaxValue : (double)sum / count;
    }
}
