namespace CCZModStudio.Models;

public sealed class RsPixelMaterialValidationRequest
{
    public string MaterialRoot { get; init; } = string.Empty;
    public string RMaterialFolder { get; init; } = string.Empty;
    public string SMaterialFolder { get; init; } = string.Empty;
    public int? RImageId { get; init; }
    public int? SImageId { get; init; }
    public int? JobId { get; init; }
    public int FactionSlot { get; init; } = 1;
}

public sealed class RsPixelMaterialValidationResult
{
    public RsPixelMaterialValidationRequest Request { get; init; } = new();
    public string MaterialRoot { get; init; } = string.Empty;
    public string RMaterialFolder { get; init; } = string.Empty;
    public string SMaterialFolder { get; init; } = string.Empty;
    public IReadOnlyList<RsPixelMaterialFileCheck> Files { get; init; } = Array.Empty<RsPixelMaterialFileCheck>();
    public RImageReplacePreviewResult? RPreview { get; init; }
    public SImageReplacePreviewResult? SPreview { get; init; }
    public IReadOnlyList<RsPixelMaterialPreviewOperation> PreviewOperations { get; init; } = Array.Empty<RsPixelMaterialPreviewOperation>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public bool FormatPassed { get; init; }
    public bool PreviewPassed { get; init; }
    public bool RequiresTestCopyWrite { get; init; }
    public bool ReadyForTestCopyWrite { get; init; }
    public int TotalOperationCount => PreviewOperations.Count;
    public string SafetyNote { get; init; } = string.Empty;
}

public sealed class RsPixelMaterialFileCheck
{
    public string Group { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string ExpectedFileName { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public int ExpectedWidth { get; init; }
    public int ExpectedHeight { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool DimensionPassed { get; init; }
    public int PixelCount { get; init; }
    public int TransparentPixelCount { get; init; }
    public int StrictMagentaPixelCount { get; init; }
    public int NearMagentaPixelCount { get; init; }
    public double StrictMagentaPercent { get; init; }
    public double NearMagentaPercent { get; init; }
    public int InteriorStrictMagentaPixelCount { get; init; }
    public int InteriorNearMagentaPixelCount { get; init; }
    public bool MagentaKeyLikely { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed class RsPixelMaterialPreviewOperation
{
    public string Group { get; init; } = string.Empty;
    public string TargetFileName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public int ImageNumber { get; init; }
    public string OldKind { get; init; } = string.Empty;
    public string NewKind { get; init; } = string.Empty;
    public int OldSizeBytes { get; init; }
    public int NewSizeBytes { get; init; }
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
}
