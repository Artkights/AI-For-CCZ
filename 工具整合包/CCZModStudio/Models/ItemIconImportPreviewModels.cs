using System.Drawing;

namespace CCZModStudio.Models;

public sealed class ItemIconImportPreviewResult
{
    public string TargetPath { get; init; } = string.Empty;
    public string TargetRelativePath { get; init; } = string.Empty;
    public int IconIndex { get; init; }
    public IReadOnlyList<int> ResourceIds { get; init; } = Array.Empty<int>();
    public string SourcePath { get; init; } = string.Empty;
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public int SmallWidth { get; init; }
    public int SmallHeight { get; init; }
    public int LargeWidth { get; init; }
    public int LargeHeight { get; init; }
    public int? SmallResourceId { get; init; }
    public int? LargeResourceId { get; init; }
    public Bitmap? OriginalSmallBitmap { get; init; }
    public Bitmap? OriginalLargeBitmap { get; init; }
    public Bitmap? ReplacementSmallBitmap { get; init; }
    public Bitmap? ReplacementLargeBitmap { get; init; }
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
    public string RiskSummary { get; init; } = string.Empty;
}
