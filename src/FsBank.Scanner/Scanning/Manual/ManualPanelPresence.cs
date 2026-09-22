using FsBank.Scanner.Game;
using FsBank.Scanner.Imaging;

namespace FsBank.Scanner.Scanning.Manual;

internal static class ManualPanelPresence
{
    // POWER/STATS sit beside the items and can be covered by a moving tooltip.
    // The independently captured character header provides a second witness.
    internal static Rectangle Header(ScanLayout layout) => new(
        layout.Bounds.Left+(int)Math.Round(16*layout.Scale),layout.Bounds.Top+(int)Math.Round(10*layout.Scale),
        layout.Bounds.Width-(int)Math.Round(32*layout.Scale),(int)Math.Round(90*layout.Scale));
    internal static bool HeaderMatches(Pixels frame,Pixels baseline,ScanLayout layout)
    {
        var header=Header(layout);
        return frame.Bounds.Contains(header) && baseline.Bounds.Contains(header)
            && frame.Difference(baseline,header,3)<3.0;
    }
    internal static bool IsVisible(Vision vision,Pixels frame,Pixels baseline,ScanLayout layout) =>
        HeaderMatches(frame,baseline,layout) || vision.PanelStillOpen(frame,layout);
}
