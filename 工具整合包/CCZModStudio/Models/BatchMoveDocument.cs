namespace CCZModStudio.Models;

public sealed class BatchMoveDocument
{
    public required string SourcePath { get; init; }
    public IReadOnlyList<BatchMoveEntry> Entries { get; init; } = Array.Empty<BatchMoveEntry>();
}

public sealed class BatchMoveEntry
{
    public int Index { get; init; }
    public int SourceLine { get; init; }
    public string Comment { get; init; } = string.Empty;
    public long SourceOffset { get; init; }
    public long TargetOffset { get; init; }
    public int Length { get; init; }
    public string SourceOffsetHex => "0x" + SourceOffset.ToString("X");
    public string TargetOffsetHex => "0x" + TargetOffset.ToString("X");
}
