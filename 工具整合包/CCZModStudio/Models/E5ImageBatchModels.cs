namespace CCZModStudio.Models;

public enum E5ImageWritePlacementPolicy
{
    AllowAppend,
    RequireInPlace,
    CompactRewrite,
    RequireStableOffset,

    // Explicit names used by pixel editing. The aliases preserve compatibility
    // with import and archive-repair callers that use the original names.
    RequireExactInPlace = RequireInPlace,
    ExplicitCompactRewrite = CompactRewrite
}

public sealed class E5ImageBatchReplaceRequest
{
    public int ImageNumber { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public byte[]? SourceBytes { get; init; }
    public bool SourceBytesAreRaw { get; init; }
    public string SourceLabel { get; init; } = string.Empty;
    public string OperationKind { get; init; } = "替换";
    public string ExpectedTargetKind { get; init; } = string.Empty;
    public string ExpectedTargetSha256 { get; init; } = string.Empty;
    public string ExpectedArchiveSha256 { get; init; } = string.Empty;
    public string ExpectedIndexSha256 { get; init; } = string.Empty;
    public bool AllowFormatConversion { get; init; }
    public E5ImageWritePlacementPolicy PlacementPolicy { get; init; } = E5ImageWritePlacementPolicy.AllowAppend;
    public CharacterImageTargetDescriptor? CharacterTarget { get; init; }

    public string DisplaySource => string.IsNullOrWhiteSpace(SourceLabel) ? SourcePath : SourceLabel;
}

public sealed class E5ArchiveTopologyEntry
{
    public int ImageNumber { get; init; }
    public int IndexOffset { get; init; }
    public int DataOffset { get; init; }
    public int StoredLength { get; init; }
    public int DecodedLength { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string StoredSha256 { get; init; } = string.Empty;
}

public sealed class E5ArchiveTopologySnapshot
{
    public string FileSha256 { get; init; } = string.Empty;
    public string IndexSha256 { get; init; } = string.Empty;
    public string HeaderSha256 { get; init; } = string.Empty;
    public long FileLength { get; init; }
    public int EntryCount { get; init; }
    public long ActivePayloadBytes { get; init; }
    public long GapBytes { get; init; }
    public long TailBytes { get; init; }
    public int SharedPayloadGroupCount { get; init; }
    public int OverlapPairCount { get; init; }
    public IReadOnlyList<E5ArchiveTopologyEntry> Entries { get; init; } = Array.Empty<E5ArchiveTopologyEntry>();
}

public sealed class E5ImageBatchOperationPreviewResult
{
    public int ImageNumber { get; init; }
    public int IndexOffset { get; init; }
    public int OldDataOffset { get; init; }
    public int NewDataOffset { get; init; }
    public int OldSizeBytes { get; init; }
    public int NewSizeBytes { get; init; }
    public string OldKind { get; init; } = string.Empty;
    public string NewKind { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string OperationKind { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public int? SourceWidth { get; init; }
    public int? SourceHeight { get; init; }
    public string Placement { get; init; } = string.Empty;
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
    public CharacterImageTargetDescriptor? CharacterTarget { get; init; }
}

public class E5ImageBatchReplacePreviewResult
{
    public string TargetPath { get; init; } = string.Empty;
    public string TargetRelativePath { get; init; } = string.Empty;
    public int OperationCount { get; init; }
    public long OldFileSizeBytes { get; init; }
    public long NewFileSizeBytes { get; init; }
    public long FileSizeDeltaBytes => NewFileSizeBytes - OldFileSizeBytes;
    public int ChangedBytesEstimate { get; init; }
    public int IndexEntriesChanged { get; init; }
    public int UntouchedEntriesVerified { get; init; }
    public string OldFileSha256 { get; init; } = string.Empty;
    public string NewFileSha256 { get; init; } = string.Empty;
    public IReadOnlyList<E5ImageBatchOperationPreviewResult> Operations { get; init; } = Array.Empty<E5ImageBatchOperationPreviewResult>();
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
    public string RiskSummary { get; init; } = string.Empty;
}

public sealed class E5ImageBatchReplaceResult : E5ImageBatchReplacePreviewResult
{
    public string BackupPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
}
