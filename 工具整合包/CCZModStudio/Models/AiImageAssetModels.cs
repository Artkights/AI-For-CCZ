namespace CCZModStudio.Models;

public sealed class AiImageAssetPreset
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public required string TargetKind { get; init; }
    public required string DefaultTargetRelativePath { get; init; }
    public required int DefaultWidth { get; init; }
    public required int DefaultHeight { get; init; }
    public required string OutputFormat { get; init; }
    public required string GenerationSize { get; init; }
    public required string Quality { get; init; }
    public required bool Foreground { get; init; }
    public required string NumberingRule { get; init; }
    public required string PostProcessRule { get; init; }
    public required string PreviewTool { get; init; }
    public required string SafetyNote { get; init; }
}

public sealed class AiImagePromptPlan
{
    public required AiImageAssetPreset Preset { get; init; }
    public required string Description { get; init; }
    public required string Prompt { get; init; }
    public required string NegativePrompt { get; init; }
    public required string TargetRelativePath { get; init; }
    public required IReadOnlyList<int> TargetImageNumbers { get; init; }
    public required int TargetWidth { get; init; }
    public required int TargetHeight { get; init; }
    public required string OutputFormat { get; init; }
    public required string GenerationSize { get; init; }
    public required string Quality { get; init; }
    public required string MappingSummary { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class AiImagePrepareResult
{
    public required AiImagePromptPlan Plan { get; init; }
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    public required string ManifestPath { get; init; }
    public required int SourceWidth { get; init; }
    public required int SourceHeight { get; init; }
    public required int OutputWidth { get; init; }
    public required int OutputHeight { get; init; }
    public required string OutputFormat { get; init; }
    public required string SourceSha256 { get; init; }
    public required string OutputSha256 { get; init; }
    public required string PostProcessSummary { get; init; }
    public required object? ReplacementPreview { get; init; }
    public required IReadOnlyList<AiImagePreparedFile> PreparedFiles { get; init; }
}

public sealed class AiImagePreparedFile
{
    public required string Role { get; init; }
    public required string TargetRelativePath { get; init; }
    public required IReadOnlyList<int> TargetImageNumbers { get; init; }
    public required string OutputPath { get; init; }
    public required int OutputWidth { get; init; }
    public required int OutputHeight { get; init; }
    public required string OutputSha256 { get; init; }
    public required object? ReplacementPreview { get; init; }
}

public sealed class AiImageDrawResult
{
    public required bool DryRun { get; init; }
    public required AiImagePromptPlan Plan { get; init; }
    public required string Provider { get; init; }
    public required string ApiMode { get; init; }
    public required string BaseUrl { get; init; }
    public required string TextModel { get; init; }
    public required string ImageModel { get; init; }
    public string? RawResponsePath { get; init; }
    public string? GeneratedSourcePath { get; init; }
    public AiImagePrepareResult? Prepared { get; init; }
    public required IReadOnlyList<string> Logs { get; init; }
}
