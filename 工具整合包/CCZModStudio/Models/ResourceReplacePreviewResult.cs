namespace CCZModStudio.Models;

/// <summary>
/// 测试副本资源整文件替换前的只读预览结果。
/// 该对象不执行写入，只用于让创作者在确认前看到大小、哈希、格式检查和风险提示。
/// </summary>
public sealed class ResourceReplacePreviewResult
{
    public required string TargetPath { get; init; }
    public required string TargetRelativePath { get; init; }
    public required string ReplacementPath { get; init; }
    public required string Extension { get; init; }
    public long OldSizeBytes { get; init; }
    public long NewSizeBytes { get; init; }
    public long SizeDeltaBytes => NewSizeBytes - OldSizeBytes;
    public int ChangedBytesEstimate { get; init; }
    public double ChangedPercent
    {
        get
        {
            var baseline = Math.Max(OldSizeBytes, NewSizeBytes);
            return baseline <= 0 ? 0 : ChangedBytesEstimate * 100.0 / baseline;
        }
    }

    public required string OldSha256 { get; init; }
    public required string NewSha256 { get; init; }
    public bool IsContentIdentical => string.Equals(OldSha256, NewSha256, StringComparison.OrdinalIgnoreCase);
    public required string FormatCheckSummary { get; init; }
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
    public required string RiskSummary { get; init; }
}
