namespace FsBank.Scanner.Game;

// Geometry uses reference pixels; runtime positions follow the detected anchor and UI scale.
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
