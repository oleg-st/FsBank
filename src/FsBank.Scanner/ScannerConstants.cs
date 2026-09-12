namespace FsBank.Scanner;

// Geometry is measured in pixels on the supplied 2560x1440 reference captures.
// Runtime positions are obtained from the detected title anchor and UI scale.
internal static class GameConstants
{
    public const string ProcessName = "fellowship-Win64-Shipping";
    public const string ItemsFolder = "items";
    public const int ReferenceWidth = 2560;
    public const int ReferenceHeight = 1440;
}

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

internal static class TooltipGeometry
{
    public const string FooterAsset = "tooltip-footer.png";
    // Only match the invariant "Left Shift - Compare Items" line. Keep the
    // original full-footer coordinate system for panel geometry and tracking.
    public const int FooterMatchTopInsetPx = 16;
    public const int TypicalWidthPx = 276;
    public const int CursorGapPx = 27;
    public const int FooterLeftInsetPx = 62;
    public const int ScreenEdgeMarginPx = 8;
    public const int NearSearchPaddingPx = 24;
    public const int FooterTrackingPaddingPx = 3;
    public const int FooterToBodyProbePx = 12;
    public const int MinimumBodyProbeY = 20;
    public const int EdgeDistanceMinPx = 120;
    public const int EdgeDistanceMaxPx = 165;
    public const int EdgeSampleHeightPx = 48;
    public const int EdgeNeighborDistancePx = 2;
    public const int EdgeScanInsetPx = 3;
    public const int VerticalGapPx = 10;
    public const int MinimumVerticalGapPx = 8;
    public const int HeaderSearchExtraPx = 15;
    public const int HeaderHorizontalInsetPx = 8;
    public const int HeaderMinRunPx = 30;
    public const int RecoveryHeaderMinRunPx = 20;
    public const int HeaderMinHeightPx = 15;
    public const int ColoredEdgeRadiusPx = 1;
    public const int MinimumColoredSidePixels = 18;
    public const int BottomPaddingPx = 6;
    public const int CropLeftPaddingPx = 3;
    public const int CropRightPaddingPx = 4;
    public const int CropTopPaddingPx = 9;
    public const int MinimumHeightPx = 120;
}

internal static class DetectionConstants
{
    public const int PatternSampleSeed = 41;
    public const int PatternSampleInsetPx = 1;
    public const int PatternBrightThreshold = 90;
    public const int PatternDarkThreshold = 50;
    public const int PatternBrightSamples = 64;
    public const int PatternDarkSamples = 32;
    public const int PatternEarlySamples = 12;
    public const int PatternEarlyMeanError = 50;
    public const double PatternMaxError = 23;
    public const double FooterMaxError = 26;
    public const double PatternImmediateMatchError = 1.2;
    public const int MinimumTabBorders = 5;
    public const int TabBorderBrightness = 45;
    public const int OccupancyInsetPx = 2;
    public const int OccupancySampleStep = 2;
    public const int OccupancyBrightness = 65;
    public const int OccupancyContrast = 35;
    public const int OccupancyMinimumPixels = 8;
    public const double OccupancyMinimumFraction = .035;
    public const int MinimumEdgePixels = 20;
    public const int EdgeBrightness = 50;
    public const int EdgeContrast = 23;
    public const int HeaderBrightness = 90;
    public const int RecoveryHeaderBrightness = 60;
    public const double HeaderMinimumSaturation = .60;
    public const int HeaderContrast = 45;
    public const double HeaderColoredFraction = .20;
    public const int ColoredEdgeBrightness = 75;
    public const int ColoredEdgeContrast = 50;
    public const int TabSampleStep = 2;
    public const int ActiveTabRedThreshold = 100;
    public const double ActiveTabRedGreenRatio = 1.15;
    public const double ActiveTabGreenBlueRatio = 1.25;
    public const int ActiveTabMinimumPixels = 8;
    public const int DefaultDifferenceStep = 3;
    public const int StabilityDifferenceStep = 2;
    public const int TabDifferenceStep = 2;
    public const double MaximumTabDifference = 9;
    public const double MaximumStableDifference = 1.2;
    public const int StableFrameCount = 2;
}

internal static class ScanConstants
{
    public const int ActivationTimeoutMs = 1800;
    public const int ActivationPollMs = 30;
    public const int ActivationSettleMs = 180;
    public const int DetailsKeyTimeoutMs = 250;
    public const int DetailsKeyPollMs = 10;
    public const int CursorTolerancePx = 5;
    public const int SaveQueueCapacity = 8;
    public const int InitialParkMs = 140;
    public const int TooltipClearTimeoutMs = 1200;
    public const int InitialHoverDelayMs = 10;
    public const int TooltipTimeoutMs = 1600;
    public const int FirstFullSearchMs = 400;
    public const int FullSearchIntervalMs = 350;
    public const int FailedTooltipParkMs = 120;
    public const int TabHoverSettleMs = 150;
    public const int TabClickHoldMs = 100;
    public const int TabReleaseSettleMs = 200;
    public const int TabRetryAfterMs = 1000;
    public const int TabClickAttempts = 3;
    public const int TabSwitchTimeoutMs = 4000;
    public const int TabSettleMs = 350;
    public const int TabStabilityPollMs = 70;
    public const int TabStableFrames = 3;
}

internal static class CaptureConstants
{
    public const uint FrameWaitMs = 30;
    public const int NewFrameTimeoutMs = 2000;
    public const int DxgiWaitTimeout = unchecked((int)0x887A0027);
}

internal static class UiConstants
{
    public static readonly Size WindowSize = new(780, 480);
    public static readonly Size MinimumWindowSize = new(650, 380);
    public const int ControlsHeight = 190;
    public const int SmallGap = 3;
    public const int SectionGap = 12;
    public const int FolderLabelTopGap = 10;
    public const int FolderInputWidth = 420;
}

internal static class VerificationConstants
{
    public const int BenchmarkRepeats = 3;
    public const int CellOutlineWidthPx = 2;
    public const int TooltipOutlineWidthPx = 3;
    public const string CleanBankFixtureNamePart = "11-48-22";
    // Diverse real-session fixtures: edges of grid and short/long tooltips.
    private static readonly (int Row,int Column)[] performanceSampleCells = [(1,1),(1,8),(2,2),(4,3),(7,6),(8,8)];
    public static ReadOnlySpan<(int Row,int Column)> PerformanceSampleCells => performanceSampleCells;
}
