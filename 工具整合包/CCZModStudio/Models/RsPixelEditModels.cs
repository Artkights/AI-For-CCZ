using System.Text.Json.Serialization;

namespace CCZModStudio.Models;

public sealed class RsPixelEditWorkspaceRequest
{
    public string PackageId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string UnitType { get; init; } = string.Empty;
    public string DesignImagePath { get; init; } = string.Empty;
    public string FormatReferenceRoot { get; init; } = string.Empty;
    public bool OverwriteExisting { get; init; }
}

public sealed class RsPixelEditPlanRequest
{
    public string PackageRoot { get; init; } = string.Empty;
    public string UnitType { get; init; } = string.Empty;
    public string CharacterBrief { get; init; } = string.Empty;
    public string WeaponBrief { get; init; } = string.Empty;
}

public sealed class RsPixelFrameEditBatchRequest
{
    public string PackageRoot { get; init; } = string.Empty;
    public IReadOnlyList<RsPixelFrameEditOperation> Operations { get; init; } = Array.Empty<RsPixelFrameEditOperation>();
}

public sealed class RsPixelFrameEditOperation
{
    public string Operation { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public IReadOnlyList<int> Frames { get; init; } = Array.Empty<int>();
    [JsonPropertyName("source_frame")]
    public int? SourceFrame { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int X2 { get; init; }
    public int Y2 { get; init; }
    public string Color { get; init; } = string.Empty;
    public string SecondaryColor { get; init; } = string.Empty;
    [JsonPropertyName("runs")]
    public IReadOnlyList<RsPixelRun> Runs { get; init; } = Array.Empty<RsPixelRun>();
    [JsonPropertyName("points")]
    public IReadOnlyList<RsPixelPoint> Points { get; init; } = Array.Empty<RsPixelPoint>();
    [JsonPropertyName("mask")]
    public IReadOnlyList<string> Mask { get; init; } = Array.Empty<string>();
    [JsonPropertyName("palette")]
    public string Palette { get; init; } = string.Empty;
    [JsonPropertyName("semantic_layer")]
    public string SemanticLayer { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}

public sealed class RsPixelRun
{
    [JsonPropertyName("frame")]
    public int Frame { get; init; } = -1;
    [JsonPropertyName("x")]
    public int X { get; init; }
    [JsonPropertyName("y")]
    public int Y { get; init; }
    [JsonPropertyName("length")]
    public int Length { get; init; } = 1;
    [JsonPropertyName("color")]
    public string Color { get; init; } = string.Empty;
}

public sealed class RsPixelPoint
{
    [JsonPropertyName("frame")]
    public int Frame { get; init; } = -1;
    [JsonPropertyName("x")]
    public int X { get; init; }
    [JsonPropertyName("y")]
    public int Y { get; init; }
    [JsonPropertyName("color")]
    public string Color { get; init; } = string.Empty;
}

public sealed class RsPixelContactSheetRequest
{
    public string PackageRoot { get; init; } = string.Empty;
    public int Scale { get; init; } = 4;
    public bool Annotate { get; init; } = true;
}

public sealed class RsPixelEditValidationRequest
{
    public string PackageRoot { get; init; } = string.Empty;
    public int? RImageId { get; init; }
    public int? SImageId { get; init; }
    public int? JobId { get; init; }
    public int FactionSlot { get; init; } = 1;
}

public sealed class RsPixelEditWorkspaceResult
{
    public string PackageId { get; init; } = string.Empty;
    public string PackageRoot { get; init; } = string.Empty;
    public string WorkspaceRoot { get; init; } = string.Empty;
    public string MaterialsRoot { get; init; } = string.Empty;
    public IReadOnlyList<RsPixelEditMaterialFile> MaterialFiles { get; init; } = Array.Empty<RsPixelEditMaterialFile>();
    public IReadOnlyList<string> Reports { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string SafetyNote { get; init; } = string.Empty;
}

public sealed class RsPixelEditPlanResult
{
    public string PackageRoot { get; init; } = string.Empty;
    public string PlanPath { get; init; } = string.Empty;
    public IReadOnlyList<RsPixelFrameEditOperation> RecommendedOperations { get; init; } = Array.Empty<RsPixelFrameEditOperation>();
    public IReadOnlyList<string> ReviewGates { get; init; } = Array.Empty<string>();
    public string SafetyNote { get; init; } = string.Empty;
}

public sealed class RsPixelFrameEditBatchResult
{
    public string PackageRoot { get; init; } = string.Empty;
    public int OperationCount { get; init; }
    public int ChangedPixelCount { get; init; }
    public IReadOnlyList<RsPixelFrameEditLogEntry> EditLogEntries { get; init; } = Array.Empty<RsPixelFrameEditLogEntry>();
    public IReadOnlyList<string> WrittenFiles { get; init; } = Array.Empty<string>();
    public string EditLogPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class RsPixelContactSheetResult
{
    public string PackageRoot { get; init; } = string.Empty;
    public int Scale { get; init; }
    public IReadOnlyList<string> ContactSheets { get; init; } = Array.Empty<string>();
    public string ReportPath { get; init; } = string.Empty;
}

public sealed class RsPixelEditValidationResult
{
    public string PackageRoot { get; init; } = string.Empty;
    public RsPixelMaterialValidationResult MaterialValidation { get; init; } = new();
    public IReadOnlyList<RsPixelFrameQualityCheck> FrameChecks { get; init; } = Array.Empty<RsPixelFrameQualityCheck>();
    public IReadOnlyList<string> SingleSpearRiskFrames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FaceRiskFrames { get; init; } = Array.Empty<string>();
    public bool LocalPixelEditPassed { get; init; }
    public string ReportPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class RsPixelEditMaterialFile
{
    public string Role { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
    public string MaterialPath { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public int FrameWidth { get; init; }
    public int FrameHeight { get; init; }
    public int FrameCount { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}

public sealed class RsPixelFrameEditLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public IReadOnlyList<int> Frames { get; init; } = Array.Empty<int>();
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int ChangedPixelCount { get; init; }
    public string Note { get; init; } = string.Empty;
}

public sealed class RsPixelFrameQualityCheck
{
    public string Target { get; init; } = string.Empty;
    public int FrameIndex { get; init; }
    public bool NonEmpty { get; init; }
    public int NonMagentaPixelCount { get; init; }
    public int StrictMagentaPixelCount { get; init; }
    public int NearMagentaNonStrictPixelCount { get; init; }
    public string BoundingBox { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
}
