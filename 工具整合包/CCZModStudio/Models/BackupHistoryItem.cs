namespace CCZModStudio.Models;

public sealed class BackupHistoryItem
{
    public DateTime CreatedAt { get; init; }
    public string CreatedAtText => CreatedAt == DateTime.MinValue ? "未知" : CreatedAt.ToString("yyyy-MM-dd HH:mm:ss.fff");
    public string Kind { get; init; } = string.Empty;
    public string TargetRelativePath { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string BackupFileName { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
    public long BackupSizeBytes { get; init; }
    public string ReportPath { get; init; } = string.Empty;
    public string SourceAction { get; init; } = string.Empty;
    public bool Restorable { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
}
