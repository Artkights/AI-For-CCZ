using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class BmpImageExportService
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly DllIconBitmapCodec _dllIconCodec = new();
    private readonly E5ImageReplaceService _e5 = new();
    private readonly E5ImageRenderService _e5Render = new();
    private readonly E5RawImageCodec _raw = new();
    private readonly CharacterImageResourceService _characterResources = new();

    public BmpExportResult Export(CczProject project, BmpExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OutputRoot))
        {
            throw new InvalidOperationException("请选择 BMP 导出目录。");
        }

        if (request.Targets.Count == 0)
        {
            throw new InvalidOperationException("没有可导出的目标。");
        }

        Directory.CreateDirectory(request.OutputRoot);
        var files = new List<BmpExportedFile>();
        var skipped = new List<BmpExportSkippedItem>();
        var warnings = new List<string>();
        var usedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in request.Targets)
        {
            try
            {
                switch (request.Kind)
                {
                    case BmpExportKind.ItemIcon:
                        ExportItemIcon(project, request, target, files, skipped, warnings, usedDirectories);
                        break;
                    case BmpExportKind.StrategyIcon:
                        ExportStrategyIcon(project, request, target, files, skipped, warnings, usedDirectories);
                        break;
                    case BmpExportKind.RImage:
                        ExportRImage(project, request, target, files, skipped, usedDirectories);
                        break;
                    case BmpExportKind.SImage:
                        ExportSImage(project, request, target, files, skipped, warnings, usedDirectories);
                        break;
                    case BmpExportKind.Face:
                        ExportFace(project, request, target, files, skipped, warnings, usedDirectories);
                        break;
                    default:
                        skipped.Add(Skip(request.Kind, target, "未知导出类型。"));
                        break;
                }
            }
            catch (Exception ex)
            {
                skipped.Add(Skip(request.Kind, target, ex.Message));
            }
        }

        var result = new BmpExportResult
        {
            Request = request,
            Files = files.ToArray(),
            SkippedItems = skipped.ToArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };

        return new BmpExportResult
        {
            Request = result.Request,
            Files = result.Files,
            SkippedItems = result.SkippedItems,
            Warnings = result.Warnings,
            ReportJsonPath = WriteReport(project, result)
        };
    }

    public static Bitmap CompositeTransparentToMagenta(Bitmap source)
    {
        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                bitmap.SetPixel(x, y, pixel.A == 0 ? Color.Fuchsia : Color.FromArgb(255, pixel.R, pixel.G, pixel.B));
            }
        }

        return bitmap;
    }

    public static void SaveBmpWithMagentaBackground(Bitmap source, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = CompositeTransparentToMagenta(source);
        bitmap.Save(path, ImageFormat.Bmp);
    }

    private void ExportItemIcon(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped,
        List<string> warnings,
        HashSet<string> usedDirectories)
    {
        var resourceFile = Ccz66RevisedLayout.ResolveItemIconResourceFile(project);
        var sourcePath = Ccz66RevisedLayout.ResolveResourcePath(project, resourceFile);
        var targetDirectory = BuildTargetDirectory(request.OutputRoot, request.Kind, target, usedDirectories);
        if (Ccz66RevisedLayout.IsE5IconResource(resourceFile))
        {
            var (small, large) = Ccz66RevisedLayout.ResolveItemIconImageNumbers(target.FieldValue);
            ExportE5Standard(project, request, target, sourcePath, small, targetDirectory, "small", "small.bmp", null, files, skipped);
            ExportE5Standard(project, request, target, sourcePath, large, targetDirectory, "large", "large.bmp", null, files, skipped);
            return;
        }

        ExportDllIconPair(request, target, sourcePath, targetDirectory, forcePairNames: true, files, skipped, warnings);
    }

    private void ExportStrategyIcon(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped,
        List<string> warnings,
        HashSet<string> usedDirectories)
    {
        var resourceFile = Ccz66RevisedLayout.ResolveStrategyIconResourceFile(project);
        var sourcePath = Ccz66RevisedLayout.ResolveResourcePath(project, resourceFile);
        var targetDirectory = BuildTargetDirectory(request.OutputRoot, request.Kind, target, usedDirectories);
        if (Ccz66RevisedLayout.IsE5IconResource(resourceFile))
        {
            var imageNumber = Ccz66RevisedLayout.ResolveStrategyIconImageNumber(target.FieldValue);
            ExportE5Standard(project, request, target, sourcePath, imageNumber, targetDirectory, "icon", "icon.bmp", null, files, skipped);
            return;
        }

        ExportDllIconPair(request, target, sourcePath, targetDirectory, forcePairNames: false, files, skipped, warnings);
    }

    private void ExportDllIconPair(
        BmpExportRequest request,
        BmpExportTarget target,
        string sourcePath,
        string targetDirectory,
        bool forcePairNames,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped,
        List<string> warnings)
    {
        if (!File.Exists(sourcePath))
        {
            skipped.Add(Skip(request.Kind, target, "找不到 DLL 图标资源。", sourcePath));
            return;
        }

        var resources = _dllIconCodec.ReadBitmapResources(sourcePath);
        DllIconGameSlot slot;
        try
        {
            slot = _dllIconCodec.ResolveGameIconSlot(sourcePath, resources, target.FieldValue, Path.GetFileName(sourcePath));
        }
        catch (Exception ex)
        {
            skipped.Add(Skip(request.Kind, target, ex.Message, sourcePath));
            return;
        }

        warnings.AddRange(slot.Warnings.Select(warning => $"{request.Kind} 字段 {target.FieldValue}：{warning}"));
        var small = slot.SmallSelectedVariant;
        var large = slot.LargeSelectedVariant;
        if (small == null && large == null)
        {
            skipped.Add(Skip(request.Kind, target, "未找到可导出的 RT_BITMAP 图标变体。", sourcePath));
            return;
        }

        if (!forcePairNames && small != null && large != null && small.Id == large.Id && small.LanguageId == large.LanguageId)
        {
            ExportDllBitmap(request, target, sourcePath, small, targetDirectory, "icon", "icon.bmp", files, skipped);
            return;
        }

        if (small != null)
        {
            ExportDllBitmap(request, target, sourcePath, small, targetDirectory, "small", "small.bmp", files, skipped);
        }

        if (large != null)
        {
            ExportDllBitmap(request, target, sourcePath, large, targetDirectory, "large", "large.bmp", files, skipped);
        }

        if (small != null && large != null && small.Id == large.Id && small.LanguageId == large.LanguageId)
        {
            warnings.Add($"{request.Kind} 字段 {target.FieldValue} 只有一个 RT_BITMAP 变体，已同时导出为 small.bmp / large.bmp。");
        }
    }

    private void ExportDllBitmap(
        BmpExportRequest request,
        BmpExportTarget target,
        string sourcePath,
        DllIconBitmapResource resource,
        string targetDirectory,
        string role,
        string fileName,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped)
    {
        using var bitmap = _dllIconCodec.DecodeDib(resource.DibBytes);
        if (bitmap == null)
        {
            skipped.Add(Skip(request.Kind, target, $"RT_BITMAP ID={resource.Id} 无法解码。", sourcePath));
            return;
        }

        var outputPath = Path.Combine(targetDirectory, fileName);
        if (!TrySaveBitmap(request, target, bitmap, outputPath, sourcePath, skipped)) return;

        files.Add(new BmpExportedFile
        {
            Kind = request.Kind,
            FieldValue = target.FieldValue,
            RowId = target.RowId,
            DisplayName = target.DisplayName,
            Role = role,
            SourcePath = sourcePath,
            ResourceId = resource.Id,
            LanguageId = resource.LanguageId,
            Width = bitmap.Width,
            Height = bitmap.Height,
            OutputPath = outputPath,
            Detail = $"RT_BITMAP ID={resource.Id} Lang={resource.LanguageId}"
        });
    }

    private void ExportRImage(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped,
        HashSet<string> usedDirectories)
    {
        var sourcePath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        var targetDirectory = BuildTargetDirectory(request.OutputRoot, request.Kind, target, usedDirectories);
        var front = checked(target.FieldValue * 2 + 1);
        var back = checked(target.FieldValue * 2 + 2);
        ExportE5Standard(project, request, target, sourcePath, front, targetDirectory, "front", "front.bmp", E5RawImageCodec.PmapobjSpec, files, skipped);
        ExportE5Standard(project, request, target, sourcePath, back, targetDirectory, "back", "back.bmp", E5RawImageCodec.PmapobjSpec, files, skipped);
    }

    private void ExportSImage(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped,
        List<string> warnings,
        HashSet<string> usedDirectories)
    {
        var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(target.FieldValue, target.JobId, request.FactionSlot);
        if (mapping.ImageNumbers.Count == 0)
        {
            skipped.Add(Skip(request.Kind, target, mapping.Detail));
            return;
        }

        var imageNumber = mapping.ImageNumbers[0];
        if (mapping.ImageNumbers.Count > 1)
        {
            warnings.Add($"S{target.FieldValue} 映射到 Unit 图 {string.Join("/", mapping.ImageNumbers.Select(x => "#" + x.ToString(CultureInfo.InvariantCulture)))}，本次按当前预览阵营导出 #{imageNumber}。");
        }

        var targetDirectory = BuildTargetDirectory(request.OutputRoot, request.Kind, target, usedDirectories);
        ExportE5Standard(project, request, target, CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5"), imageNumber, targetDirectory, "mov", "mov.bmp", E5RawImageCodec.UnitMovSpec, files, skipped, mapping.Detail);
        ExportE5Standard(project, request, target, CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5"), imageNumber, targetDirectory, "atk", "atk.bmp", E5RawImageCodec.UnitAtkSpec, files, skipped, mapping.Detail);
        ExportE5Standard(project, request, target, CharacterImageResourceService.ResolveGameFile(project, "Unit_spc.e5"), imageNumber, targetDirectory, "spc", "spc.bmp", E5RawImageCodec.UnitSpcSpec, files, skipped, mapping.Detail);
    }

    private void ExportFace(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped,
        List<string> warnings,
        HashSet<string> usedDirectories)
    {
        var sourcePath = CharacterImageResourceService.ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", "Face.e5");
        var mapping = _characterResources.MapFaceId(target.FieldValue);
        var targetDirectory = BuildTargetDirectory(request.OutputRoot, request.Kind, target, usedDirectories);
        if (target.FieldValue <= 0)
        {
            warnings.Add("头像编号 0 是 Face.e5 #1-#8 的遗留多表情槽，已按多文件导出。");
            for (var i = 0; i < mapping.FaceImageNumbers.Count; i++)
            {
                var imageNumber = mapping.FaceImageNumbers[i];
                ExportE5Standard(project, request, target, sourcePath, imageNumber, targetDirectory, $"face_{i + 1:D2}", $"face_{i + 1:D2}.bmp", null, files, skipped, mapping.Explanation);
            }

            return;
        }

        ExportE5Standard(project, request, target, sourcePath, mapping.FaceImageNumbers[0], targetDirectory, "face", "face.bmp", null, files, skipped, mapping.Explanation);
    }

    private void ExportE5Standard(
        CczProject project,
        BmpExportRequest request,
        BmpExportTarget target,
        string sourcePath,
        int imageNumber,
        string targetDirectory,
        string role,
        string fileName,
        E5RawImageSpec? rawSpec,
        List<BmpExportedFile> files,
        List<BmpExportSkippedItem> skipped,
        string detail = "")
    {
        if (!File.Exists(sourcePath))
        {
            skipped.Add(Skip(request.Kind, target, "找不到 E5 资源文件。", sourcePath));
            return;
        }

        var entries = _e5.ReadIndex(sourcePath);
        if (imageNumber <= 0 || imageNumber > entries.Count)
        {
            skipped.Add(Skip(request.Kind, target, $"E5 图号 #{imageNumber} 越界，可用范围 1-{entries.Count}。", sourcePath));
            return;
        }

        Bitmap? bitmap = null;
        try
        {
            var bytes = _e5.ReadEntryBytes(sourcePath, imageNumber);
            bitmap = _e5Render.TryDecodeStandardImage(bytes);
            bitmap ??= rawSpec == null
                ? null
                : _raw.DecodeRawBytes(project, bytes, $"{Path.GetFileName(sourcePath)} #{imageNumber}", rawSpec);
            if (bitmap == null)
            {
                skipped.Add(Skip(request.Kind, target, $"E5 图号 #{imageNumber} 无法按标准图片解码。", sourcePath));
                return;
            }

            var outputPath = Path.Combine(targetDirectory, fileName);
            if (!TrySaveBitmap(request, target, bitmap, outputPath, sourcePath, skipped)) return;

            files.Add(new BmpExportedFile
            {
                Kind = request.Kind,
                FieldValue = target.FieldValue,
                RowId = target.RowId,
                DisplayName = target.DisplayName,
                Role = role,
                SourcePath = sourcePath,
                ImageNumber = imageNumber,
                Width = bitmap.Width,
                Height = bitmap.Height,
                OutputPath = outputPath,
                Detail = string.IsNullOrWhiteSpace(detail) ? $"E5 #{imageNumber}" : $"{detail}; E5 #{imageNumber}"
            });
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private bool TrySaveBitmap(
        BmpExportRequest request,
        BmpExportTarget target,
        Bitmap bitmap,
        string outputPath,
        string sourcePath,
        List<BmpExportSkippedItem> skipped)
    {
        if (File.Exists(outputPath) && !request.OverwriteExisting)
        {
            skipped.Add(Skip(request.Kind, target, "目标 BMP 已存在，已跳过。", sourcePath));
            return false;
        }

        SaveBmpWithMagentaBackground(bitmap, outputPath);
        return true;
    }

    private static string BuildTargetDirectory(
        string outputRoot,
        BmpExportKind kind,
        BmpExportTarget target,
        HashSet<string> usedDirectories)
    {
        var baseName = BuildBaseDirectoryName(kind, target);
        var candidate = Path.Combine(outputRoot, baseName);
        if (usedDirectories.Add(candidate)) return candidate;

        for (var index = 2; ; index++)
        {
            candidate = Path.Combine(outputRoot, $"{baseName}_{index}");
            if (usedDirectories.Add(candidate)) return candidate;
        }
    }

    private static string BuildBaseDirectoryName(BmpExportKind kind, BmpExportTarget target)
    {
        var prefix = kind switch
        {
            BmpExportKind.ItemIcon => $"item_icon_{target.FieldValue}",
            BmpExportKind.StrategyIcon => $"strategy_icon_{target.FieldValue}",
            BmpExportKind.RImage => $"R{target.FieldValue}",
            BmpExportKind.SImage => $"S{target.FieldValue}",
            BmpExportKind.Face => $"face_{target.FieldValue}",
            _ => $"bmp_{target.FieldValue}"
        };
        var name = SanitizePathPart(target.DisplayName);
        return string.IsNullOrWhiteSpace(name) ? prefix : $"{prefix}_{name}";
    }

    private static string SanitizePathPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Trim()
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();
        var text = WhitespaceRegex.Replace(new string(chars), "_").Trim('_');
        return text.Length <= 64 ? text : text[..64].TrimEnd('_');
    }

    private string WriteReport(CczProject project, BmpExportResult result)
    {
        var reportPath = Path.Combine(
            result.Request.OutputRoot,
            $"CCZModStudio_BmpExportReport_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        var payload = new
        {
            Project = new
            {
                project.Name,
                project.GameRoot,
                project.WorkspaceRoot,
                project.IsTestCopy
            },
            result.Request,
            result.Files,
            result.SkippedItems,
            result.Warnings
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, JsonOptions));
        return reportPath;
    }

    private static BmpExportSkippedItem Skip(BmpExportKind kind, BmpExportTarget target, string reason, string sourcePath = "")
        => new()
        {
            Kind = kind,
            FieldValue = target.FieldValue,
            RowId = target.RowId,
            DisplayName = target.DisplayName,
            Reason = reason,
            SourcePath = sourcePath
        };
}
