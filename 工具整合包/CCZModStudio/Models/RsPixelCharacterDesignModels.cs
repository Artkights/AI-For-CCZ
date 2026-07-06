namespace CCZModStudio.Models;

public sealed class RsPixelCharacterDesignRequest
{
    public string PackageId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string UnitType { get; init; } = string.Empty;
    public string DesignImagePath { get; init; } = string.Empty;
    public string? FormatActionImagePath { get; init; }
    public string? FormatReferenceFolder { get; init; }
    public string? FormatReferenceGameRoot { get; init; }
    public int? FormatReferenceSImageId { get; init; }
    public int? FormatReferenceRowId { get; init; }
    public string? FormatReferenceDisplayName { get; init; }
    public string CharacterBrief { get; init; } = string.Empty;
    public string WeaponBrief { get; init; } = string.Empty;
    public string ForbiddenReadings { get; init; } = string.Empty;
    public bool GenerateNow { get; init; }
    public bool DryRun { get; init; } = true;
    public int? RImageId { get; init; }
    public int? SImageId { get; init; }
    public int? JobId { get; init; }
    public int FactionSlot { get; init; } = 1;
}

public sealed class RsPixelCharacterDesignResult
{
    public required string PackageId { get; init; }
    public required string PackageRoot { get; init; }
    public required string GenerationStatus { get; init; }
    public required string DesignImagePath { get; init; }
    public required string FormatActionImagePath { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> Reports { get; init; }
    public required AiImagePromptPlan SUnitPlan { get; init; }
    public required AiImagePromptPlan RActorPlan { get; init; }
    public AiImageDrawResult? SUnitDraw { get; init; }
    public AiImageDrawResult? RActorDraw { get; init; }
    public required string SafetyNote { get; init; }
}
