namespace CCZModStudio.Models;

public sealed class PatchPreviewRow
{
    public int Index { get; init; }
    public int SourceLine { get; init; }
    public string Comment { get; init; } = string.Empty;
    public string AddressKind { get; init; } = string.Empty;
    public string AddressHex { get; init; } = string.Empty;
    public string FileOffsetHex { get; init; } = string.Empty;
    public long FileOffset { get; init; }
    public int Length { get; init; }
    public string OldBytesHex { get; init; } = string.Empty;
    public string NewBytesHex { get; init; } = string.Empty;
    public bool Changed { get; init; }
    public bool CanApply { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class PatchPreviewResult
{
    public required PatchDocument Document { get; init; }
    public required string TargetFilePath { get; init; }
    public IReadOnlyList<PatchPreviewRow> Rows { get; init; } = Array.Empty<PatchPreviewRow>();
    public int ChangedBytes { get; init; }

    public bool CanApply => Rows.Count > 0 && Rows.All(r => r.CanApply);
    public int TotalBytes => Rows.Where(r => r.CanApply).Sum(r => r.Length);
    public int WarningCount => Rows.Count(r => !r.CanApply);
}

public sealed class PatchApplyResult
{
    public required string TargetFilePath { get; init; }
    public required string BackupPath { get; init; }
    public required string ReportPath { get; init; }
    public string ReportJsonPath { get; init; } = string.Empty;
    public int EntriesApplied { get; init; }
    public int BytesWritten { get; init; }
    public int ChangedBytes { get; init; }
}
