namespace CCZModStudio.Models;

public sealed class ProjectDiffItem
{
    public string Status { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public long? SourceSize { get; init; }
    public long? TestSize { get; init; }
    public string SourceSha256 { get; init; } = string.Empty;
    public string TestSha256 { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string TestPath { get; init; } = string.Empty;
}
