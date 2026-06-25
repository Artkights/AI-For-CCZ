using System.Drawing;

namespace CCZModStudio.Models;

public class IconResourceReplacePreviewResult
{
    public string TargetPath { get; init; } = string.Empty;
    public string TargetRelativePath { get; init; } = string.Empty;
    public int IconIndex { get; init; }
    public IReadOnlyList<int> ResourceIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<IconResourceVariantInfo> ResourceVariants { get; init; } = Array.Empty<IconResourceVariantInfo>();
    public string SourcePath { get; init; } = string.Empty;
    public string OperationKind { get; init; } = string.Empty;
    public long OldFileSizeBytes { get; init; }
    public long SourceSizeBytes { get; init; }
    public string OldFileSha256 { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public int? SourceWidth { get; init; }
    public int? SourceHeight { get; init; }
    public string ResourceFormat { get; init; } = string.Empty;
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
    public string RiskSummary { get; init; } = string.Empty;
}

public sealed class IconResourceReplaceResult : IconResourceReplacePreviewResult
{
    public string BackupPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public long NewFileSizeBytes { get; init; }
    public int ChangedBytesEstimate { get; init; }
    public string NewFileSha256 { get; init; } = string.Empty;
    public bool ReadbackVerified { get; init; }
    public IReadOnlyList<string> ReadbackWarnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IconResourceReadbackInfo> ReadbackItems { get; init; } = Array.Empty<IconResourceReadbackInfo>();
}

public sealed class IconResourceBatchReplaceRequest
{
    public int IconIndex { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public string OperationKind { get; init; } = string.Empty;
}

public sealed class IconResourceBitmapReplaceRequest
{
    public int IconIndex { get; init; }
    public Bitmap Bitmap { get; init; } = new(1, 1);
    public string SourceLabel { get; init; } = string.Empty;
    public string OperationKind { get; init; } = "像素编辑";
}

public sealed class IconResourceBatchReplacePreviewItem
{
    public int IconIndex { get; init; }
    public IReadOnlyList<int> ResourceIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<IconResourceVariantInfo> ResourceVariants { get; init; } = Array.Empty<IconResourceVariantInfo>();
    public string SourcePath { get; init; } = string.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public long SourceSizeBytes { get; init; }
    public string SourceSha256 { get; init; } = string.Empty;
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
}

public sealed class IconResourceVariantInfo
{
    public int ResourceId { get; init; }
    public ushort LanguageId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int BitCount { get; init; }
    public int SizeBytes { get; init; }

    public string DisplayLabel =>
        $"ID={ResourceId} Lang={LanguageId} {Width}x{Height} {BitCount}bpp";
}

public class IconResourceBatchReplacePreviewResult
{
    public string TargetPath { get; init; } = string.Empty;
    public string TargetRelativePath { get; init; } = string.Empty;
    public IReadOnlyList<IconResourceBatchReplaceRequest> Requests { get; init; } = Array.Empty<IconResourceBatchReplaceRequest>();
    public IReadOnlyList<IconResourceBatchReplacePreviewItem> Items { get; init; } = Array.Empty<IconResourceBatchReplacePreviewItem>();
    public string OperationKind { get; init; } = string.Empty;
    public long OldFileSizeBytes { get; init; }
    public string OldFileSha256 { get; init; } = string.Empty;
    public string ResourceFormat { get; init; } = string.Empty;
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
    public string RiskSummary { get; init; } = string.Empty;
}

public sealed class IconResourceBatchReplaceResult : IconResourceBatchReplacePreviewResult
{
    public string BackupPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public long NewFileSizeBytes { get; init; }
    public int ChangedBytesEstimate { get; init; }
    public string NewFileSha256 { get; init; } = string.Empty;
    public bool ReadbackVerified { get; init; }
    public IReadOnlyList<string> ReadbackWarnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IconResourceReadbackInfo> ReadbackItems { get; init; } = Array.Empty<IconResourceReadbackInfo>();
}

public sealed class IconResourceReadbackInfo
{
    public int ResourceId { get; init; }
    public ushort LanguageId { get; init; }
    public int ExpectedWidth { get; init; }
    public int ExpectedHeight { get; init; }
    public int ActualWidth { get; init; }
    public int ActualHeight { get; init; }
    public string ExpectedPixelHash { get; init; } = string.Empty;
    public string ActualPixelHash { get; init; } = string.Empty;
    public int SourceBitCount { get; init; }
    public int WrittenBitCount { get; init; }
    public int ActualBitCount { get; init; }
    public string TransparencyMode { get; init; } = string.Empty;
    public string VariantWritePolicy { get; init; } = string.Empty;
    public string PreparedLargeHash { get; init; } = string.Empty;
    public Rectangle SourceOpaqueBounds { get; init; }
    public int TransparentPixelCount { get; init; }
    public int MagentaKeyPixelCount { get; init; }
    public int CornerBackgroundPixelCount { get; init; }
    public int ActualTransparentPixelCount { get; init; }
    public int ActualVisibleMagentaPixelCount { get; init; }
    public int ActualWhiteishPixelCount { get; init; }
    public int ActualOpaquePixelCount { get; init; }
    public bool Matches { get; init; }

    public string DisplayLabel =>
        $"ID={ResourceId} Lang={LanguageId} expected={ExpectedWidth}x{ExpectedHeight} actual={ActualWidth}x{ActualHeight} {SourceBitCount}->{WrittenBitCount}bpp actual={ActualBitCount}bpp transparent={TransparentPixelCount}/{ActualTransparentPixelCount} magenta={MagentaKeyPixelCount}/{ActualVisibleMagentaPixelCount} white={ActualWhiteishPixelCount} policy={VariantWritePolicy} match={Matches}";
}
