namespace CCZModStudio.Models;

public sealed class HexzmapProbeResult
{
    public string Path { get; init; } = string.Empty;
    public string Magic { get; init; } = string.Empty;
    public bool MagicValid { get; init; }
    public int PayloadOffset { get; init; }
    public int PayloadLength { get; init; }
    public int CandidateWidth { get; init; }
    public int CandidateHeight { get; init; }
    public int CandidateBlockSize => CandidateWidth * CandidateHeight;
    public int TrailingBytes { get; init; }
    public byte[] Payload { get; init; } = Array.Empty<byte>();
    public int DirectoryTableOffset { get; init; }
    public IReadOnlyList<HexzmapDirectoryEntry> DirectoryEntries { get; init; } = Array.Empty<HexzmapDirectoryEntry>();
    public IReadOnlyList<HexzmapBlockInfo> Blocks { get; init; } = Array.Empty<HexzmapBlockInfo>();
    public IReadOnlyList<HexzmapBlockBinding> Bindings { get; init; } = Array.Empty<HexzmapBlockBinding>();
}
