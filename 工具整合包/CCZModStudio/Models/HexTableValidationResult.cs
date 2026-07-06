namespace CCZModStudio.Models;

public sealed class HexTableValidationResult
{
    public required HexTableDefinition Table { get; init; }
    public required string FilePath { get; init; }
    public bool FileExists { get; init; }
    public long FileLength { get; init; }
    public bool ColumnsMatchBytes { get; init; }
    public bool FitsInFile { get; init; }
    public int PaddingBytes { get; init; }
    public string TableStatus { get; init; } = "ExactOrCompatible";
    public string WriteRisk { get; init; } = string.Empty;
    public bool IsNative66 { get; init; }
    public bool IsCrossVersionFallback { get; init; }
    public bool IsReadOnlyEvidenceOnly { get; init; }
    public string SemanticValidationStatus { get; init; } = string.Empty;
    public string HiddenTailPolicy { get; init; } = string.Empty;
    public string EffectResolutionSource { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool IsUsable => FileExists && ColumnsMatchBytes && FitsInFile;
    public bool CanWrite => IsUsable && !IsReadOnlyEvidenceOnly && !IsCrossVersionFallback;
}
