namespace CCZModStudio.Models;

public sealed class EexCrossFileComparisonResult
{
    public required string TargetFileName { get; init; }
    public required string TargetCategory { get; init; }
    public required string TargetId { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<EexCrossFileComparisonRow> Rows { get; init; } = Array.Empty<EexCrossFileComparisonRow>();
}

public sealed class EexCrossFileComparisonRow
{
    public string PeerKind { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public long FileLength { get; init; }
    public bool MagicValid { get; init; }
    public string RoleHint { get; init; } = string.Empty;
    public int SectionCount { get; init; }
    public long TotalLength { get; init; }
    public int MinLength { get; init; }
    public int MaxLength { get; init; }
    public double AverageLength { get; init; }
    public double AverageZeroPercent { get; init; }
    public double AverageSmallWordPercent { get; init; }
    public int TextHintCount { get; init; }
    public string FirstOffsets { get; init; } = string.Empty;
    public string DifferenceHint { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
