namespace CCZModStudio.Models;

public sealed class ImageResourceFileInfo
{
    public string Key { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Aliases { get; init; } = string.Empty;
    public string Usage { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public long SizeBytes { get; init; }
    public int EntryCount { get; init; }
    public bool SupportsE5Index { get; init; }
    public bool SupportsPreview { get; init; }
    public bool CanReplace { get; init; }
    public string ResourceFormat { get; init; } = string.Empty;
    public string KindSummary { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string SafetyNote { get; init; } = string.Empty;
}

public sealed class ImageResourceEntryInfo
{
    public string ResourceKey { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string ResourceName { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public int ImageNumber { get; init; }
    public int IndexOffset { get; init; }
    public int DataOffset { get; init; }
    public int StoredLength { get; init; }
    public int DecodedLength { get; init; }
    public bool IsCompressed { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Usage { get; init; } = string.Empty;
    public bool CanReplace { get; init; }
}
