namespace CCZModStudio.Models;

public sealed class HexTableDefinition
{
    public int Id { get; init; }
    public bool Enabled { get; init; }
    public required string TableName { get; init; }
    public required string FileName { get; init; }
    public long DataPos { get; init; }
    public int RowCount { get; init; }
    public int RowSize { get; init; }
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> ByteSizes { get; init; } = Array.Empty<int>();
    public required string IndexTable { get; init; }
    public int BeginId { get; init; }
    public bool OnMem { get; init; }
    public bool ReadOnly { get; init; }
    public required string Version { get; init; }
    public IReadOnlyList<HexFieldDefinition> Fields { get; init; } = Array.Empty<HexFieldDefinition>();
    public string EvidenceStatus { get; init; } = string.Empty;
    public string SourceTableName { get; init; } = string.Empty;
    public bool IsGeneratedCompatibilityTable { get; init; }
    public bool IsEvidenceReadOnlyTable { get; init; }

    public int PositiveBytesSum => ByteSizes.Where(x => x > 0).Sum();
    public long EndOffsetExclusive => DataPos + ((long)RowCount * RowSize);

    public override string ToString() => $"{Id}. {TableName}";
}
