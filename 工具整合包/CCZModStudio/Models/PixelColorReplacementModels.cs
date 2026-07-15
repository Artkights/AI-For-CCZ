using System.Drawing;

namespace CCZModStudio.Models;

public sealed record PixelColorReplacementRule(Color Source, Color Target);

public sealed record PixelColorDocumentMatch(
    string DocumentKey,
    string DisplayName,
    IReadOnlyList<int> RuleMatchCounts)
{
    public int TotalMatches => RuleMatchCounts.Sum();
}

public sealed class PixelColorReplacementPreview
{
    public IReadOnlyList<PixelColorReplacementRule> Rules { get; init; } = Array.Empty<PixelColorReplacementRule>();
    public IReadOnlyList<PixelColorDocumentMatch> Documents { get; init; } = Array.Empty<PixelColorDocumentMatch>();
    public IReadOnlyList<int> RuleMatchCounts { get; init; } = Array.Empty<int>();
    public int TotalMatches => Documents.Sum(document => document.TotalMatches);
}

public sealed class PixelColorReplacementResult
{
    public PixelColorReplacementPreview Preview { get; init; } = new();
    public IReadOnlyList<string> ChangedDocumentKeys { get; init; } = Array.Empty<string>();
}

public sealed class EditableImagePreparedE5Write
{
    public EditableImageTarget Target { get; init; } = new();
    public string TargetPath { get; init; } = string.Empty;
    public IReadOnlyList<E5ImageBatchReplaceRequest> Requests { get; init; } = Array.Empty<E5ImageBatchReplaceRequest>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class PixelEditResourceGroupWritePreview
{
    public string ScopeDescription { get; init; } = string.Empty;
    public IReadOnlyList<E5ImageBatchReplacePreviewResult> Files { get; init; } = Array.Empty<E5ImageBatchReplacePreviewResult>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string SharedUsageWarning { get; init; } = string.Empty;
    public PixelColorReplacementPreview? ColorReplacementPreview { get; init; }
    public EditableImageWritePreview? SinglePreview { get; init; }
    public int EntryCount => SinglePreview != null ? 1 : Files.Sum(file => file.Operations.Count);
}

public sealed class PixelEditResourceGroupWriteResult
{
    public string ScopeDescription { get; init; } = string.Empty;
    public IReadOnlyList<E5ImageBatchReplaceResult> Files { get; init; } = Array.Empty<E5ImageBatchReplaceResult>();
    public string AggregateReportPath { get; init; } = string.Empty;
    public EditableImageWriteResult? SingleResult { get; init; }
    public int EntryCount => SingleResult != null ? 1 : Files.Sum(file => file.Operations.Count);
}
