namespace CCZModStudio.Models;

public sealed class E5IndexRawEntry
{
    public int ImageNumber { get; init; }
    public int IndexOffset { get; init; }
    public uint StoredLength { get; init; }
    public uint DecodedLength { get; init; }
    public uint DataOffset { get; init; }
    public bool IsValid { get; init; }
    public string FailureReason { get; init; } = string.Empty;
}

public sealed class E5IndexProbeResult
{
    public string Path { get; init; } = string.Empty;
    public long FileLength { get; init; }
    public int ExpectedEntryCount { get; init; }
    public int DirectoryTrailerLength { get; init; }
    public int ParsedEntryCount => Entries.Count;
    public bool IsComplete { get; init; }
    public int? FirstInvalidImageNumber { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public string DirectorySha256 { get; init; } = string.Empty;
    public int SharedPayloadGroupCount { get; init; }
    public int OverlapPairCount { get; init; }
    public IReadOnlyList<E5ImageEntryInfo> Entries { get; init; } = Array.Empty<E5ImageEntryInfo>();
    public IReadOnlyList<E5IndexRawEntry> RawEntries { get; init; } = Array.Empty<E5IndexRawEntry>();

    public string Diagnostic => IsComplete
        ? $"E5 index is complete: {ParsedEntryCount} entries."
        : $"E5 index is damaged at image #{FirstInvalidImageNumber?.ToString() ?? "?"}: {FailureReason} " +
          $"(parsed {ParsedEntryCount}/{ExpectedEntryCount}).";
}

public sealed class E5ArchiveIntegrityException : InvalidOperationException
{
    public E5ArchiveIntegrityException(E5IndexProbeResult probe)
        : base(probe.Diagnostic)
    {
        Probe = probe;
    }

    public E5IndexProbeResult Probe { get; }
}
