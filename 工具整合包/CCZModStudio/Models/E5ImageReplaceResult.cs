namespace CCZModStudio.Models;

public sealed class E5ImageReplaceResult : E5ImageReplacePreviewResult
{
    public required string BackupPath { get; init; }
    public required string ReportPath { get; init; }
    public required string ReportJsonPath { get; init; }
}
