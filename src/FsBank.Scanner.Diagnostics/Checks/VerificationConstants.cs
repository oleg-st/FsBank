namespace FsBank.Scanner.Diagnostics.Checks;

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
