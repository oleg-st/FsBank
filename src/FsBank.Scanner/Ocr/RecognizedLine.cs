namespace FsBank.Scanner.Ocr;

internal sealed record RecognizedLine(int Index, Rectangle Box, string Color, string Text, int Confidence)
{
    public int? VerifiedConfidence { get; init; }
    public List<OcrSymbol> Symbols { get; init; } = [];
    public string? RawText { get; init; }
    public string? RecognitionText { get; init; }
    public List<TextCorrection> Corrections { get; init; } = [];
}
