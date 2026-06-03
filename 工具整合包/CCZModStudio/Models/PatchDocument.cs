namespace CCZModStudio.Models;

public enum PatchAddressKind
{
    Unknown,
    OdVirtualAddress,
    FileOffset
}

public sealed class PatchDocument
{
    public required string SourcePath { get; init; }
    public required string Version { get; init; }
    public PatchAddressKind AddressKind { get; init; }
    public IReadOnlyList<PatchEntry> Entries { get; init; } = Array.Empty<PatchEntry>();
    public IReadOnlyList<string> Comments { get; init; } = Array.Empty<string>();
}

public sealed class PatchEntry
{
    public int Index { get; init; }
    public uint Address { get; init; }
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
    public int SourceLine { get; init; }
    public string? Comment { get; init; }

    public string AddressHex => Address.ToString("X");
    public string BytesHex => BitConverter.ToString(Bytes).Replace("-", " ");
}
