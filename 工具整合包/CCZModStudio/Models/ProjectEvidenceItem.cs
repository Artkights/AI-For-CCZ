namespace CCZModStudio.Models;

/// <summary>
/// 制作向导中的“最近报告/发布证据”条目。它只指向工具生成的报告、导出物或预览图，不直接修改游戏文件。
/// </summary>
public sealed class ProjectEvidenceItem
{
    public string Category { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string SourceRoot { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public DateTime LastWriteTime { get; init; }
    public string LastWriteTimeText { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string SizeText { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
    public string SuggestedUse { get; init; } = string.Empty;
    public string SafetyNote { get; init; } = string.Empty;
}
