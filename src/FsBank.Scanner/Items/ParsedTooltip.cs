namespace FsBank.Scanner.Items;

internal sealed record RecognizedStat(string Name, int Value, string Origin, int SourceLine);
internal sealed record RecognizedImbuedTrait(string Name, int SourceLine);
internal sealed class ModifierBlock(int sourceLine)
{
    public int SourceLine { get; } = sourceLine;
    public string Kind { get; set; } = "unresolved";
    public string? Name { get; set; }
    public int? Value { get; set; }
    public int? DisplayedSlot { get; set; }
    public List<string> Lines { get; } = [];
}
internal sealed class ParsedTooltip
{
    public string? Name { get; set; }
    public string? Material { get; set; }
    public string? Slot { get; set; }
    public int? ItemLevel { get; set; }
    public string? Rarity { get; set; }
    public int? TemperedCurrent { get; set; }
    public int? TemperedMaximum { get; set; }
    public bool? Temperable { get; set; }
    public int? PowerPotential { get; set; }
    public bool UniqueEquipped { get; set; }
    public List<RecognizedStat> Stats { get; } = [];
    public List<ModifierBlock> Modifiers { get; } = [];
    // Recognized for diagnostics/future use; deliberately separate from exported modifiers.
    public List<RecognizedImbuedTrait> ImbuedTraits { get; } = [];
    public List<string> MetadataLines { get; } = [];
    public List<int> UnparsedLines { get; } = [];
    public List<int> ExplanationLines { get; } = [];
    public List<string> Warnings { get; } = [];
    // Full diagnostics retain OCR imperfections in prose. Only warnings that can
    // affect item values should interrupt the scan progress with "Needs review".
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> ReviewWarnings { get; } = [];
    internal void Warn(string message,bool needsReview=true)
    {
        Warnings.Add(message);
        if(needsReview)ReviewWarnings.Add(message);
    }
    public int GemSocketLabels { get; set; }
}
