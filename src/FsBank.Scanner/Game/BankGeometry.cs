namespace FsBank.Scanner.Game;

// Geometry uses reference pixels; runtime positions follow the detected anchor and UI scale.
internal static class BankGeometry
{
    public const int Rows = 8;
    public const int Columns = 8;
    public const int CellCount = Rows * Columns;
    public const int CellSizePx = 58;
    public const int CellPitchPx = 64;
    public const int TabCount = 7;
    public const int TabPitchPx = 60;
    public const int TabButtonWidthPx = 54;
    public static readonly Point ReferenceTitleAnchor = new(1788, 310);
    public static readonly Rectangle ReferenceBounds = new(1510, 295, 648, 824);
    public static readonly Point ReferenceGridOrigin = new(1580, 404);
    public static readonly Rectangle ReferenceTabs = new(1623, 360, 421, 31);
    public static readonly Point TabBorderProbe = new(4, 7);
    public const int ParkLeftInsetPx = 32;
    public const int ParkBottomInsetPx = 40;
    public const int CaptureLeftMarginPx = 350;
    public const string TitleAsset = "stash-title.png";
    public static ReadOnlySpan<double> RelativeScales => [1, .875, 1.125, .75, 1.25];
    public const double NativeScale = 1;
    public const int TitleTrackingPaddingPx = 2;
    public static int GridOffsetX => ReferenceGridOrigin.X - ReferenceTitleAnchor.X;
    public static int GridOffsetY => ReferenceGridOrigin.Y - ReferenceTitleAnchor.Y;
}
