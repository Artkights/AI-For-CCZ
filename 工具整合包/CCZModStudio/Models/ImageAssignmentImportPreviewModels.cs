namespace CCZModStudio.Models;

public sealed class ImageAssignmentImportPreviewDialogModel
{
    public string Title { get; init; } = string.Empty;
    public string SummaryText { get; init; } = string.Empty;
    public bool CanWrite { get; init; }
    public IReadOnlyList<ImageAssignmentImportPreviewItem> Items { get; init; } = Array.Empty<ImageAssignmentImportPreviewItem>();
    public IReadOnlyList<BatchImageImportSkippedItem> SkippedItems { get; init; } = Array.Empty<BatchImageImportSkippedItem>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class ImageAssignmentImportPreviewItem
{
    public string Kind { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int ResourceId { get; init; }
    public string StageName { get; init; } = string.Empty;
    public string ActionName { get; init; } = string.Empty;
    public string TargetFileName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public int TargetImageNumber { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public int? SourceWidth { get; init; }
    public int? SourceHeight { get; init; }
    public int OutputWidth { get; init; }
    public int OutputHeight { get; init; }
    public byte[]? OutputBytes { get; init; }
    public int FrameWidth { get; init; }
    public int FrameHeight { get; init; }
    public string Detail { get; init; } = string.Empty;
}
