namespace FsBank.Scanner.Imaging;

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
