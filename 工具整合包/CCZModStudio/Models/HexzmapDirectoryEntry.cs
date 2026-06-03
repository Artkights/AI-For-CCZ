namespace CCZModStudio.Models;

public sealed class HexzmapDirectoryEntry
{
    public int Index { get; init; }
    public int EntryOffset { get; init; }
    public int SegmentLength { get; init; }
    public int FileOffset { get; init; }
    public int NextSegmentLength { get; init; }
    public string MapId { get; init; } = string.Empty;
    public string CandidateMapIdA { get; init; } = string.Empty;
    public string CandidateMapIdB { get; init; } = string.Empty;
    public string CandidateMapIdC { get; init; } = string.Empty;
    public bool IsValidSegment { get; init; }
    public string Annotation { get; init; } = string.Empty;
}
