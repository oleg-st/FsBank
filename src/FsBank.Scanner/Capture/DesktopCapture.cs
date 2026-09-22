using FsBank.Scanner.Imaging;

using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace FsBank.Scanner.Capture;

// All D3D calls belong to the scanner worker. No per-frame device/texture allocation.
internal sealed class DesktopCapture : IDisposable
{
    private ID3D11Device? device;
    private ID3D11DeviceContext? context;
    private IDXGIOutputDuplication? duplication;
    private ID3D11Texture2D? staging;
    private Pixels? pixels;
    private readonly Rectangle monitor;
    public long Frames { get; private set; }
    public DesktopCapture(Rectangle game)
    {
        using var factory = CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        using (adapter)
        for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
        using (output)
        {
            var d = output.Description;
            var r = d.DesktopCoordinates;
            var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (!bounds.Contains(game)) continue;
            if (d.Rotation != ModeRotation.Identity && d.Rotation != ModeRotation.Unspecified)
                throw new NotSupportedException("The prototype requires a monitor without display rotation.");
            monitor = bounds;
            try
            {
                D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                    new[] { FeatureLevel.Level_11_0 }, out device, out context).CheckError();
                using var output1 = output.QueryInterface<IDXGIOutput1>();
                duplication = output1.DuplicateOutput(device);
                return;
            }
            catch { Dispose(); throw; }
        }
        throw new InvalidOperationException("The game window must be entirely on one monitor.");
    }
    // Returned Pixels is borrowed until the next call. Only newly acquired frames are returned.
    public Pixels? Next(Rectangle region, uint waitMs = CaptureConstants.FrameWaitMs, long notBefore=0)
    {
        if (!monitor.Contains(region)) throw new InvalidOperationException("The capture region extends beyond the monitor.");
        var result = duplication!.AcquireNextFrame(waitMs, out var info, out var resource);
        if (result.Code == CaptureConstants.DxgiWaitTimeout) return null; // DXGI_ERROR_WAIT_TIMEOUT
        result.CheckError(); // access lost/device removed aborts this session, never returns stale pixels
        try
        {
            using (resource)
            {
                if (info.LastPresentTime == 0 || info.LastPresentTime<notBefore) return null;
                // QPC timestamp rejects pointer-only updates and queued frames
                // rendered before the latest hover/park input.
                using var source = resource.QueryInterface<ID3D11Texture2D>();
                if (pixels is null || pixels.Width != region.Width || pixels.Height != region.Height)
                {
                    staging?.Dispose();
                    var description = source.Description;
                    description.Width = (uint)region.Width;
                    description.Height = (uint)region.Height;
                    description.Usage = ResourceUsage.Staging;
                    description.BindFlags = BindFlags.None;
                    description.CPUAccessFlags = CpuAccessFlags.Read;
                    description.MiscFlags = ResourceOptionFlags.None;
                    staging = device!.CreateTexture2D(description);
                    pixels = new Pixels(region.Width, region.Height);
                }
                var box = new Box(region.Left-monitor.Left, region.Top-monitor.Top, 0,
                    region.Right-monitor.Left, region.Bottom-monitor.Top, 1);
                context!.CopySubresourceRegion(staging!, 0, 0, 0, 0, source, 0, box);
                var mapped = context.Map(staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    for (int y = 0; y < pixels.Height; y++)
                        Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch, pixels.Data, y * pixels.Width * Pixels.BytesPerPixel, pixels.Width * Pixels.BytesPerPixel);
                }
                finally { context.Unmap(staging!, 0); }
                Frames++;
                return pixels;
            }
        }
        finally { duplication.ReleaseFrame(); }
    }
    public void Dispose() { staging?.Dispose(); duplication?.Dispose(); context?.Dispose(); device?.Dispose(); }
}
