using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class BmpImageExportService
{
    private static readonly Color MagentaKey = Color.FromArgb(255, 255, 0, 255);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly E5RawImageSpec RStripSpec = new(
        E5RawImageCodec.PmapobjSpec.FileName,
        E5RawImageCodec.PmapobjSpec.Width,
        E5RawImageCodec.PmapobjSpec.FrameHeight,
        E5RawImageCodec.PmapobjSpec.FrameHeight * 20);

    private readonly E5ImageReplaceService _e5 = new();
    private readonly E5ImageRenderService _e5Render = new();
    private readonly E5RawImageCodec _raw = new();
    private readonly ItemIconPreviewService _iconPreview = new();
    private readonly CharacterImageResourceService _character = new();
    private readonly DllBitmapIconCodecService _dllIconCodec = new();

    public BmpExportResult Export(CczProject project, BmpExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OutputRoot))
        {
            throw new InvalidOperationException("Missing BMP export output folder.");
        }

        if (request.Targets.Count == 0)
        {
            throw new InvalidOperationException("No BMP export targets were selected.");
        }

        var outputRoot = Path.GetFullPath(request.OutputRoot);
        Directory.CreateDirectory(outputRoot);

        var files = new List<BmpExportedFile>();
        var skipped = new List<BmpExportSkippedItem>();
        var warnings = new List<string>();
        var usedBatchFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in request.Targets)
        {
            var folder = request.SingleMode || UsesFlatBatchLayout(request.Kind)
                ? outputRoot
                : ResolveBatchTargetFolder(outputRoot, request.Kind, target, usedBatchFolders);

            try
            {
                Directory.CreateDirectory(folder);
                switch (request.Kind)
                {
                    case BmpExportKind.JobSImage:
                    case BmpExportKind.SImage:
                        ExportSImage(project, request, target, folder, files, skipped, warnings);
                        break;
                    case BmpExportKind.RImage:
                        ExportRImage(project, request, target, folder, files, skipped);
                        break;
                    case BmpExportKind.Face:
                        ExportFace(project, request, target, folder, files, skipped, warnings);
                        break;
                    case BmpExportKind.ItemIcon:
                        ExportItemIcon(project, request, target, folder, files, skipped);
                        break;
                    case BmpExportKind.StrategyIcon:
                        ExportStrategyIcon(project, request, target, folder, files, skipped);
                        break;
                    default:
                        skipped.Add(new BmpExportSkippedItem
                        {
                            Kind = request.Kind,
                            RowId = target.RowId,
                            FieldValue = target.FieldValue,
                            DisplayName = target.DisplayName,
                            Reason = "Unsupported export kind."
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                skipped.Add(new BmpExportSkippedItem
                {
                    Kind = request.Kind,
                    RowId = target.RowId,
                    FieldValue = target.FieldValue,
                    DisplayName = target.DisplayName,
                    OutputPath = folder,
                    Reason = ex.Message
                });
            }
        }

        return new BmpExportResult
        {
            Request = request,
            Files = files.ToArray(),
            SkippedItems = skipped.ToArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    public static Bitmap CompositeTransparentToMagenta(Bitmap source)
        => CompositeTransparentToMagenta(source, MagentaKey);

    public static Bitmap CompositeTransparentToMagenta(Bitmap source, Color magentaKey)
    {
        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                bitmap.SetPixel(x, y, color.A == 0 ? magentaKey : Color.FromArgb(255, color.R, color.G, color.B));
            }
        }

        return bitmap;
    }

    public static void SaveBmpWithMagentaBackground(Bitmap source, string path)
        => SaveBmpWithMagentaBackground(source, path, MagentaKey);

    public static void SaveBmpWithMagentaBackground(Bitmap source, string path, Color magentaKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var bitmap = CompositeTransparentToMagenta(source, magentaKey);
        bitmap.Save(path, ImageFormat.Bmp);
    }

    private void ExportSImage(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        string folder,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped,
        List<string> warnings)
    {
        var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(
            target.FieldValue,
            target.JobId,
            request.FactionSlot);
        if (mapping.ImageNumbers.Count == 0)
        {
            throw new InvalidOperationException(mapping.Detail);
        }

        var imageNumber = mapping.ImageNumbers[0];
        if (mapping.ImageNumbers.Count > 1)
        {
            warnings.Add(
                $"S{target.FieldValue} maps to Unit #{string.Join("/#", mapping.ImageNumbers)}; exported #{imageNumber}.");
        }

        ExportRawEntry(project, request, target, folder, "mov", "mov.bmp", "Unit_mov.e5",
            CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5"), imageNumber, E5RawImageCodec.UnitMovSpec, files, skipped);
        ExportRawEntry(project, request, target, folder, "atk", "atk.bmp", "Unit_atk.e5",
            CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5"), imageNumber, E5RawImageCodec.UnitAtkSpec, files, skipped);
        ExportRawEntry(project, request, target, folder, "spc", "spc.bmp", "Unit_spc.e5",
            CharacterImageResourceService.ResolveGameFile(project, "Unit_spc.e5"), imageNumber, E5RawImageCodec.UnitSpcSpec, files, skipped);
    }

    private void ExportRImage(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        string folder,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped)
    {
        if (target.FieldValue < 0)
        {
            throw new InvalidOperationException($"R image id cannot be negative: {target.FieldValue}");
        }

        var sourcePath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        ExportRawEntry(project, request, target, folder, "front", "front.bmp", "Pmapobj.e5",
            sourcePath, checked(target.FieldValue * 2 + 1), RStripSpec, files, skipped);
        ExportRawEntry(project, request, target, folder, "back", "back.bmp", "Pmapobj.e5",
            sourcePath, checked(target.FieldValue * 2 + 2), RStripSpec, files, skipped);
    }

    private void ExportFace(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        string folder,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped,
        List<string> warnings)
    {
        var sourcePath = CharacterImageResourceService.ResolveFaceFile(project) ??
                         Path.Combine(project.GameRoot, "E5", "Face.e5");
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Face.e5 was not found.", sourcePath);
        }

        var mapping = _character.MapFaceId(target.FieldValue);
        var numbers = mapping.FaceImageNumbers.Take(1).ToArray();
        if (target.FieldValue == 0)
        {
            warnings.Add("Face id 0 is a legacy multi-expression slot; exported Face.e5 #1 for the current face import pipeline.");
        }

        for (var i = 0; i < numbers.Length; i++)
        {
            var imageNumber = numbers[i];
            var fileName = $"face_{target.FieldValue.ToString(CultureInfo.InvariantCulture)}.bmp";
            var bytes = _e5.ReadEntryBytes(sourcePath, imageNumber);
            using var bitmap = DecodeStandardImage(bytes, $"Face.e5 #{imageNumber}");
            SaveBitmap(project, request, target, folder, "face", fileName, sourcePath, imageNumber, null, bitmap, files, skipped);
        }
    }

    private void ExportItemIcon(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        string folder,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped)
    {
        if (!Ccz66RevisedLayout.Is66(project))
        {
            ExportDllItemIconPair(project, request, target, folder, files, skipped);
            return;
        }

        var result = _iconPreview.BuildPreview(project, target.FieldValue);
        if (result.SmallBitmap == null && result.LargeBitmap == null)
        {
            throw new InvalidOperationException(result.Message);
        }

        var source = result.LargeBitmap ?? result.NativeBitmap ?? result.SmallBitmap;
        var variant = result.LargeVariant ?? result.SmallVariant;
        if (source == null)
        {
            throw new InvalidOperationException(result.Message);
        }

        var fileName = $"item_icon_{target.FieldValue.ToString(CultureInfo.InvariantCulture)}.bmp";
        SaveBitmap(project, request, target, folder, "icon", fileName, result.SourcePath,
            variant?.ResourceId, variant?.ResourceId, source, files, skipped);
    }

    private void ExportDllItemIconPair(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        string folder,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped)
    {
        var sourcePath = Ccz66RevisedLayout.ResolveResourcePath(project, Ccz66RevisedLayout.ResolveItemIconResourceFile(project));
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Itemicon.dll was not found.", sourcePath);
        }

        var resources = _dllIconCodec.ParseBitmapResources(sourcePath);
        var pair = _dllIconCodec.ResolveBitmapResourcePair(resources, target.FieldValue);
        var small = _dllIconCodec.SelectDisplayVariant(pair.SmallVariants);
        var large = _dllIconCodec.SelectDisplayVariant(pair.LargeVariants);
        if (small == null && large == null)
        {
            throw new InvalidOperationException($"Item icon #{target.FieldValue} has no small/large RT_BITMAP resources.");
        }

        if (small != null)
        {
            SaveStorageBmp(project, request, target, folder, "small", $"item_icon_{target.FieldValue.ToString(CultureInfo.InvariantCulture)}_small.bmp", sourcePath, small, files, skipped);
        }

        if (large != null)
        {
            SaveStorageBmp(project, request, target, folder, "large", $"item_icon_{target.FieldValue.ToString(CultureInfo.InvariantCulture)}_large.bmp", sourcePath, large, files, skipped);
            SaveStorageBmp(project, request, target, folder, "icon", $"item_icon_{target.FieldValue.ToString(CultureInfo.InvariantCulture)}.bmp", sourcePath, large, files, skipped);
        }
    }

    private void SaveStorageBmp(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        string folder,
        string role,
        string fileName,
        string sourcePath,
        DllBitmapResourceRecord resource,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped)
    {
        var outputPath = Path.Combine(folder, fileName);
        if (File.Exists(outputPath) && !request.OverwriteExisting)
        {
            skipped.Add(new BmpExportSkippedItem
            {
                Kind = request.Kind,
                RowId = target.RowId,
                FieldValue = target.FieldValue,
                DisplayName = target.DisplayName,
                OutputPath = outputPath,
                Reason = "Target BMP already exists."
            });
            return;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllBytes(outputPath, _dllIconCodec.BuildStorageBmpBytes(resource));
        files.Add(new BmpExportedFile
        {
            Kind = request.Kind,
            RowId = target.RowId,
            FieldValue = target.FieldValue,
            DisplayName = target.DisplayName,
            Role = role,
            SourcePath = sourcePath,
            SourceRelativePath = WriteOperationReportService.ToProjectRelativePath(project, sourcePath),
            ResourceId = resource.Id,
            Width = resource.Width,
            Height = resource.Height,
            OutputPath = outputPath
        });
    }

    private void ExportStrategyIcon(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        string folder,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped)
    {
        var resourceFile = Ccz66RevisedLayout.ResolveStrategyIconResourceFile(project);
        var result = _iconPreview.BuildPreview(project, target.FieldValue, resourceFile, "strategy icon");
        var source = result.LargeBitmap ?? result.NativeBitmap ?? result.SmallBitmap;
        var variant = result.LargeVariant ?? result.SmallVariant;
        if (source == null)
        {
            throw new InvalidOperationException(result.Message);
        }

        var fileName = $"strategy_icon_{target.FieldValue.ToString(CultureInfo.InvariantCulture)}.bmp";
        SaveBitmap(project, request, target, folder, "icon", fileName, result.SourcePath,
            variant?.ResourceId, variant?.ResourceId, source, files, skipped);
    }

    private void ExportRawEntry(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        string folder,
        string role,
        string fileName,
        string sourceFileName,
        string sourcePath,
        int imageNumber,
        E5RawImageSpec spec,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"{sourceFileName} was not found.", sourcePath);
        }

        var rawBytes = _e5.ReadEntryBytes(sourcePath, imageNumber);
        using var bitmap = _raw.DecodeRawBytes(project, rawBytes, $"{sourceFileName} #{imageNumber}", spec);
        SaveBitmap(project, request, target, folder, role, fileName, sourcePath, imageNumber, null, bitmap, files, skipped);
    }

    private Bitmap DecodeStandardImage(byte[] bytes, string sourceLabel)
    {
        var bitmap = _e5Render.TryDecodeStandardImage(bytes);
        return bitmap ?? throw new InvalidOperationException($"{sourceLabel} is not a decodable BMP/JPG/PNG image.");
    }

    private void SaveBitmap(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        string folder,
        string role,
        string fileName,
        string sourcePath,
        int? imageNumber,
        int? resourceId,
        Bitmap bitmap,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped)
    {
        var outputPath = Path.Combine(folder, fileName);
        if (File.Exists(outputPath) && !request.OverwriteExisting)
        {
            skipped.Add(new BmpExportSkippedItem
            {
                Kind = request.Kind,
                RowId = target.RowId,
                FieldValue = target.FieldValue,
                DisplayName = target.DisplayName,
                OutputPath = outputPath,
                Reason = "Target BMP already exists."
            });
            return;
        }

        var magentaKey = request.Kind == BmpExportKind.ItemIcon
            ? Ccz66RevisedLayout.Is66(project)
                ? ItemIconRasterNormalizeService.GameMagentaKey
                : DllBitmapIconCodecService.DllTransparentKey
            : MagentaKey;
        SaveBmpWithMagentaBackground(bitmap, outputPath, magentaKey);
        files.Add(new BmpExportedFile
        {
            Kind = request.Kind,
            RowId = target.RowId,
            FieldValue = target.FieldValue,
            DisplayName = target.DisplayName,
            Role = role,
            SourcePath = sourcePath,
            SourceRelativePath = WriteOperationReportService.ToProjectRelativePath(project, sourcePath),
            ImageNumber = imageNumber,
            ResourceId = resourceId,
            Width = bitmap.Width,
            Height = bitmap.Height,
            OutputPath = outputPath
        });
    }

    private static bool UsesFlatBatchLayout(BmpExportKind kind)
        => kind is BmpExportKind.Face or BmpExportKind.ItemIcon or BmpExportKind.StrategyIcon;

    private static string ResolveBatchTargetFolder(
        string outputRoot,
        BmpExportKind kind,
        BmpExportTarget target,
        HashSet<string> usedBatchFolders)
    {
        var baseName = BuildBatchFolderName(kind, target);
        var candidate = Path.Combine(outputRoot, baseName);
        usedBatchFolders.Add(candidate);

        return candidate;
    }

    private static string BuildBatchFolderName(BmpExportKind kind, BmpExportTarget target)
    {
        var prefix = kind switch
        {
            BmpExportKind.JobSImage => "Job",
            BmpExportKind.SImage => "S",
            BmpExportKind.RImage => "R",
            BmpExportKind.Face => "face_",
            BmpExportKind.ItemIcon => "item_icon_",
            BmpExportKind.StrategyIcon => "strategy_icon_",
            _ => "bmp_"
        };
        var idValue = kind == BmpExportKind.JobSImage
            ? target.JobId ?? target.RowId
            : target.FieldValue;
        var id = idValue.ToString(CultureInfo.InvariantCulture);
        return prefix.EndsWith("_", StringComparison.Ordinal) ? prefix + id : prefix + id;
    }

    private static string SanitizeFileNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var normalized = WhitespaceRegex.Replace(builder.ToString(), "_").Trim('_');
        return normalized.Length <= 64 ? normalized : normalized[..64].Trim('_');
    }
}

public enum BmpExportKind
{
    JobSImage,
    RImage,
    SImage,
    Face,
    ItemIcon,
    StrategyIcon
}

public sealed class BmpExportRequest
{
    public BmpExportKind Kind { get; init; }
    public string OutputRoot { get; init; } = string.Empty;
    public bool SingleMode { get; init; }
    public bool OverwriteExisting { get; init; }
    public int FactionSlot { get; init; } = CharacterImageResourceService.DefaultSPreviewFactionSlot;
    public IReadOnlyList<BmpExportTarget> Targets { get; init; } = Array.Empty<BmpExportTarget>();
}

public sealed class BmpExportTarget
{
    public int RowId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public int FieldValue { get; init; }
    public int? JobId { get; init; }
}

public sealed class BmpExportResult
{
    public BmpExportRequest Request { get; init; } = new();
    public IReadOnlyList<BmpExportedFile> Files { get; init; } = Array.Empty<BmpExportedFile>();
    public IReadOnlyList<BmpExportSkippedItem> SkippedItems { get; init; } = Array.Empty<BmpExportSkippedItem>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class BmpExportedFile
{
    public BmpExportKind Kind { get; init; }
    public int RowId { get; init; }
    public int FieldValue { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string SourceRelativePath { get; init; } = string.Empty;
    public int? ImageNumber { get; init; }
    public int? ResourceId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string OutputPath { get; init; } = string.Empty;
}

public sealed class BmpExportSkippedItem
{
    public BmpExportKind Kind { get; init; }
    public int RowId { get; init; }
    public int FieldValue { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
