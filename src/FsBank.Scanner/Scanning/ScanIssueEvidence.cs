using FsBank.Scanner.Items;
using FsBank.Scanner.Ocr;

namespace FsBank.Scanner.Scanning;

internal sealed record ScanIssueLine(int Index, Rectangle Box, string Text, string? RawText, string[] Reasons);
internal sealed record ScanIssueEvidence(string ImageBase64, int Width, int Height, ScanIssueLine[] Lines)
{
    internal static ScanIssueEvidence Create(string file, int width, int height, IReadOnlyList<RecognizedLine> lines, ParsedTooltip item) =>
        new(Convert.ToBase64String(File.ReadAllBytes(file)), width, height,
            lines.Where(l => item.ReviewLines.ContainsKey(l.Index))
                .Select(l => new ScanIssueLine(l.Index, l.Box, l.Text, l.RawText, item.ReviewLines[l.Index].Distinct().ToArray())).ToArray());
}
