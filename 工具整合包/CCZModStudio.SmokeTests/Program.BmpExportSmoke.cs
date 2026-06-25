using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

internal partial class Program
{
    static void RunBmpExportSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "BmpExportSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);

        foreach (var resource in new[]
                 {
                     "Ekd5.exe",
                     "Data.e5",
                     "Imsg.e5",
                     "Star.e5",
                     "Hexzmap.e5",
                     "Pmapobj.e5",
                     "Unit_mov.e5",
                     "Unit_atk.e5",
                     "Unit_spc.e5",
                     "Face.e5",
                     "Itemicon.dll",
                     "Mgcicon.dll",
                     "E5\\Item.e5",
                     "E5\\Mtem.e5",
                     "E5\\Face.e5"
                 })
        {
            CopyBmpSmokeResource(project, smokeRoot, resource);
        }

        CopyBmpSmokePalette(project, smokeRoot);
        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=BMP export smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var exportRoot = Path.Combine(smokeRoot, "_BmpExports");
        Directory.CreateDirectory(exportRoot);

        var service = new BmpImageExportService();
        RunBmpExportTransparentSmoke(exportRoot);
        RunBmpExportItemIconSmoke(testProject, exportRoot, service);
        RunBmpExportStrategyIconSmoke(testProject, exportRoot, service);
        RunBmpExportRImageSmoke(testProject, exportRoot, service);
        RunBmpExportSImageSmoke(testProject, exportRoot, service);
        RunBmpExportFaceSmoke(testProject, exportRoot, service);

        Console.WriteLine($"BMP_EXPORT_SMOKE OK root={smokeRoot}");
    }

    private static void RunBmpExportTransparentSmoke(string exportRoot)
    {
        var outputPath = Path.Combine(exportRoot, "_transparent", "transparent.bmp");
        using (var bitmap = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
        {
            bitmap.SetPixel(0, 0, Color.Transparent);
            bitmap.SetPixel(1, 0, Color.FromArgb(255, 12, 34, 56));
            BmpImageExportService.SaveBmpWithMagentaBackground(bitmap, outputPath);
        }

        using var reloaded = new Bitmap(outputPath);
        var transparentPixel = reloaded.GetPixel(0, 0);
        if (transparentPixel.R != 255 || transparentPixel.G != 0 || transparentPixel.B != 255)
        {
            throw new InvalidOperationException("BMP 导出未把透明像素合成为 #FF00FF。");
        }

        var solidPixel = reloaded.GetPixel(1, 0);
        if (solidPixel.R != 12 || solidPixel.G != 34 || solidPixel.B != 56)
        {
            throw new InvalidOperationException("BMP 导出改变了非透明像素 RGB。");
        }
    }

    private static void RunBmpExportItemIconSmoke(CczProject project, string exportRoot, BmpImageExportService service)
    {
        var resourceFile = Ccz66RevisedLayout.ResolveItemIconResourceFile(project);
        var sourcePath = Ccz66RevisedLayout.ResolveResourcePath(project, resourceFile);
        if (!File.Exists(sourcePath))
        {
            Console.WriteLine($"BMP_EXPORT_SMOKE_SKIP item icon missing={sourcePath}");
            return;
        }

        var request = new BmpExportRequest
        {
            Kind = BmpExportKind.ItemIcon,
            OutputRoot = Path.Combine(exportRoot, "item_icon"),
            Targets = new[] { new BmpExportTarget { RowId = 0, FieldValue = 0 } }
        };
        var result = ExportBmpOrThrow(project, service, request, "宝物图标");
        AssertExportRoles(result, "宝物图标", "small", "large");
        AssertSkipExisting(project, service, request, result, "宝物图标");

        var preview = new ItemIconPreviewService().BuildPreview(project, 0);
        try
        {
            if (DllIconBitmapCodec.IsGameIconResourceFile(Path.GetFileName(sourcePath)) &&
                !preview.RenderMode.Equals("DLL RT_BITMAP", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("宝物图标导出 smoke 期望预览服务使用 DLL RT_BITMAP，而不是 Windows ICO。");
            }

            AssertExportMatchesPreviewBitmap(result, "small", preview.SmallBitmap, "宝物图标 small");
            AssertExportMatchesPreviewBitmap(result, "large", preview.LargeBitmap, "宝物图标 large");
        }
        finally
        {
            preview.Bitmap?.Dispose();
            preview.NativeBitmap?.Dispose();
            preview.SmallBitmap?.Dispose();
            preview.LargeBitmap?.Dispose();
        }

        if (DllIconBitmapCodec.IsGameIconResourceFile(Path.GetFileName(sourcePath)))
        {
            AssertBmpExportReimportRestoresIconTransparency(project, exportRoot, service, sourcePath);
        }
    }

    private static void AssertBmpExportReimportRestoresIconTransparency(
        CczProject project,
        string exportRoot,
        BmpImageExportService exportService,
        string iconDllPath)
    {
        var replaceService = new IconResourceReplaceService();
        var sourcePng = Path.Combine(exportRoot, "_reimport", "magenta_key_source.png");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePng)!);
        using (var source = CreateMagentaKeyIconSource(32, 32))
        {
            source.Save(sourcePng, ImageFormat.Png);
        }

        var writeResult = replaceService.ReplaceBitmapIcon(project, iconDllPath, 4, sourcePng);
        if (!writeResult.ReadbackVerified)
        {
            throw new InvalidOperationException("BMP export reimport setup failed to write transparent DLL icon.");
        }

        var request = new BmpExportRequest
        {
            Kind = BmpExportKind.ItemIcon,
            OutputRoot = Path.Combine(exportRoot, "item_icon_reimport"),
            Targets = new[] { new BmpExportTarget { RowId = 4, FieldValue = 4 } }
        };
        var export = ExportBmpOrThrow(project, exportService, request, "item icon reimport");
        var largeBmp = export.Files.FirstOrDefault(file => file.Role.Equals("large", StringComparison.OrdinalIgnoreCase))?.OutputPath
                       ?? throw new InvalidOperationException("BMP export reimport smoke did not export large.bmp.");
        using (var exported = new Bitmap(largeBmp))
        {
            var pixel = exported.GetPixel(1, 0);
            if (pixel.R != 255 || pixel.G != 0 || pixel.B != 255)
            {
                throw new InvalidOperationException("BMP export reimport smoke expected exported transparent background to be #FF00FF.");
            }
        }

        var reimport = replaceService.ReplaceBitmapIcon(project, iconDllPath, 4, largeBmp);
        if (!reimport.ReadbackVerified || reimport.ReadbackItems.All(item => item.MagentaKeyPixelCount == 0))
        {
            throw new InvalidOperationException("BMP export reimport smoke did not convert exported #FF00FF pixels back to transparent.");
        }

        var preview = new ItemIconPreviewService().BuildPreview(project, 4);
        try
        {
            if (preview.LargeBitmap == null)
            {
                throw new InvalidOperationException("BMP export reimport smoke did not produce a large preview bitmap.");
            }

            AssertTransparentPixel(preview.LargeBitmap, 1, 0, "BMP export reimport large background");
            AssertNoVisibleMagenta(preview.LargeBitmap, "BMP export reimport large");
        }
        finally
        {
            preview.Bitmap?.Dispose();
            preview.NativeBitmap?.Dispose();
            preview.SmallBitmap?.Dispose();
            preview.LargeBitmap?.Dispose();
        }
    }

    private static void RunBmpExportStrategyIconSmoke(CczProject project, string exportRoot, BmpImageExportService service)
    {
        var resourceFile = Ccz66RevisedLayout.ResolveStrategyIconResourceFile(project);
        var sourcePath = Ccz66RevisedLayout.ResolveResourcePath(project, resourceFile);
        if (!File.Exists(sourcePath))
        {
            Console.WriteLine($"BMP_EXPORT_SMOKE_SKIP strategy icon missing={sourcePath}");
            return;
        }

        var request = new BmpExportRequest
        {
            Kind = BmpExportKind.StrategyIcon,
            OutputRoot = Path.Combine(exportRoot, "strategy_icon"),
            Targets = new[] { new BmpExportTarget { RowId = 0, FieldValue = 0 } }
        };
        var result = ExportBmpOrThrow(project, service, request, "兵种策略图标");
        if (Ccz66RevisedLayout.IsE5IconResource(resourceFile))
        {
            AssertExportRoles(result, "兵种策略图标", "icon");
        }
        else if (!result.Files.Any(file => file.Role is "icon" or "small" or "large"))
        {
            throw new InvalidOperationException("兵种策略图标导出未生成 icon/small/large BMP。");
        }

        var preview = new ItemIconPreviewService().BuildPreview(project, 0, resourceFile, "策略图标", 96);
        try
        {
            if (DllIconBitmapCodec.IsGameIconResourceFile(Path.GetFileName(sourcePath)) &&
                !preview.RenderMode.Equals("DLL RT_BITMAP", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("兵种策略图标导出 smoke 期望预览服务使用 DLL RT_BITMAP，而不是 Windows ICO。");
            }

            AssertExportMatchesPreviewBitmap(result, "small", preview.SmallBitmap, "兵种策略图标 small");
            AssertExportMatchesPreviewBitmap(result, "large", preview.LargeBitmap, "兵种策略图标 large");
            AssertExportMatchesPreviewBitmap(result, "icon", preview.NativeBitmap, "兵种策略图标 icon");
        }
        finally
        {
            preview.Bitmap?.Dispose();
            preview.NativeBitmap?.Dispose();
            preview.SmallBitmap?.Dispose();
            preview.LargeBitmap?.Dispose();
        }
    }

    private static void RunBmpExportRImageSmoke(CczProject project, string exportRoot, BmpImageExportService service)
    {
        var sourcePath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        if (!File.Exists(sourcePath))
        {
            Console.WriteLine($"BMP_EXPORT_SMOKE_SKIP R image missing={sourcePath}");
            return;
        }

        var request = new BmpExportRequest
        {
            Kind = BmpExportKind.RImage,
            OutputRoot = Path.Combine(exportRoot, "r_image"),
            Targets = new[] { new BmpExportTarget { RowId = 0, FieldValue = 0 } }
        };
        var result = ExportBmpOrThrow(project, service, request, "R 形象");
        AssertExportRoles(result, "R 形象", "front", "back");
        AssertExportSize(result, "front", 48, 1280, "R 正面条带");
        AssertExportSize(result, "back", 48, 1280, "R 反面条带");
        AssertRawReimportable(project, result, "front", E5RawImageCodec.PmapobjSpec, "R 正面条带");
        AssertRawReimportable(project, result, "back", E5RawImageCodec.PmapobjSpec, "R 反面条带");
    }

    private static void RunBmpExportSImageSmoke(CczProject project, string exportRoot, BmpImageExportService service)
    {
        var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(1, null, CharacterImageResourceService.DefaultSPreviewFactionSlot);
        if (mapping.ImageNumbers.Count == 0)
        {
            Console.WriteLine("BMP_EXPORT_SMOKE_SKIP S image mapping empty");
            return;
        }

        var imageNumber = mapping.ImageNumbers[0];
        foreach (var fileName in new[] { "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5" })
        {
            var sourcePath = CharacterImageResourceService.ResolveGameFile(project, fileName);
            if (!File.Exists(sourcePath))
            {
                Console.WriteLine($"BMP_EXPORT_SMOKE_SKIP S image missing={sourcePath}");
                return;
            }

            var entries = new E5ImageReplaceService().ReadIndex(sourcePath);
            if (entries.Count < imageNumber)
            {
                Console.WriteLine($"BMP_EXPORT_SMOKE_SKIP S image {fileName} entries={entries.Count} need={imageNumber}");
                return;
            }
        }

        var request = new BmpExportRequest
        {
            Kind = BmpExportKind.SImage,
            OutputRoot = Path.Combine(exportRoot, "s_image"),
            FactionSlot = CharacterImageResourceService.DefaultSPreviewFactionSlot,
            Targets = new[] { new BmpExportTarget { RowId = 0, FieldValue = 1 } }
        };
        var result = ExportBmpOrThrow(project, service, request, "S 形象");
        AssertExportRoles(result, "S 形象", "mov", "atk", "spc");
        AssertExportSize(result, "mov", 48, 528, "S 移动条带");
        AssertExportSize(result, "atk", 64, 768, "S 攻击条带");
        AssertExportSize(result, "spc", 48, 240, "S 特技条带");
        AssertRawReimportable(project, result, "mov", E5RawImageCodec.UnitMovSpec, "S 移动条带");
        AssertRawReimportable(project, result, "atk", E5RawImageCodec.UnitAtkSpec, "S 攻击条带");
        AssertRawReimportable(project, result, "spc", E5RawImageCodec.UnitSpcSpec, "S 特技条带");
    }

    private static void RunBmpExportFaceSmoke(CczProject project, string exportRoot, BmpImageExportService service)
    {
        var sourcePath = CharacterImageResourceService.ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", "Face.e5");
        if (!File.Exists(sourcePath))
        {
            Console.WriteLine($"BMP_EXPORT_SMOKE_SKIP face missing={sourcePath}");
            return;
        }

        var entries = new E5ImageReplaceService().ReadIndex(sourcePath);
        if (entries.Count < 9)
        {
            Console.WriteLine($"BMP_EXPORT_SMOKE_SKIP face entries={entries.Count} need=9");
            return;
        }

        var normal = ExportBmpOrThrow(project, service, new BmpExportRequest
        {
            Kind = BmpExportKind.Face,
            OutputRoot = Path.Combine(exportRoot, "face_normal"),
            Targets = new[] { new BmpExportTarget { RowId = 0, FieldValue = 1 } }
        }, "头像");
        AssertExportRoles(normal, "头像", "face");
        AssertExportSize(normal, "face", 120, 120, "头像");

        var multi = ExportBmpOrThrow(project, service, new BmpExportRequest
        {
            Kind = BmpExportKind.Face,
            OutputRoot = Path.Combine(exportRoot, "face_zero"),
            Targets = new[] { new BmpExportTarget { RowId = 0, FieldValue = 0 } }
        }, "头像 0");
        AssertExportRoles(multi, "头像 0", "face_01", "face_02", "face_03", "face_04", "face_05", "face_06", "face_07", "face_08");
        if (multi.Files.Count(file => file.FieldValue == 0 && file.Role.StartsWith("face_", StringComparison.Ordinal)) != 8)
        {
            throw new InvalidOperationException("头像 0 导出未生成 8 张多表情 BMP。");
        }
    }

    private static BmpExportResult ExportBmpOrThrow(CczProject project, BmpImageExportService service, BmpExportRequest request, string label)
    {
        var result = service.Export(project, request);
        if (!File.Exists(result.ReportJsonPath))
        {
            throw new InvalidOperationException($"{label} BMP 导出未生成 JSON 报告。");
        }

        if (result.Files.Count == 0)
        {
            throw new InvalidOperationException($"{label} BMP 导出未生成文件。Skipped={string.Join(" | ", result.SkippedItems.Select(item => item.Reason))}");
        }

        foreach (var file in result.Files)
        {
            if (!File.Exists(file.OutputPath))
            {
                throw new InvalidOperationException($"{label} BMP 文件不存在：{file.OutputPath}");
            }

            if (!Path.GetExtension(file.OutputPath).Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{label} 导出文件不是 BMP：{file.OutputPath}");
            }

            using var bitmap = new Bitmap(file.OutputPath);
            if (bitmap.Width != file.Width || bitmap.Height != file.Height)
            {
                throw new InvalidOperationException($"{label} BMP 尺寸记录不一致：role={file.Role} record={file.Width}x{file.Height} file={bitmap.Width}x{bitmap.Height}");
            }
        }

        return result;
    }

    private static void AssertSkipExisting(CczProject project, BmpImageExportService service, BmpExportRequest request, BmpExportResult firstResult, string label)
    {
        var secondResult = service.Export(project, request);
        if (secondResult.Files.Count != 0 ||
            secondResult.SkippedItems.Count < firstResult.Files.Count ||
            secondResult.SkippedItems.All(item => !item.Reason.Contains("已存在", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"{label} BMP 导出未按默认规则跳过已存在文件。");
        }
    }

    private static void AssertExportRoles(BmpExportResult result, string label, params string[] roles)
    {
        foreach (var role in roles)
        {
            if (result.Files.All(file => !file.Role.Equals(role, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"{label} BMP 导出缺少 {role}。");
            }
        }
    }

    private static void AssertExportSize(BmpExportResult result, string role, int width, int height, string label)
    {
        var file = result.Files.FirstOrDefault(file => file.Role.Equals(role, StringComparison.Ordinal))
                   ?? throw new InvalidOperationException($"{label} BMP 导出缺少 {role}。");
        if (file.Width != width || file.Height != height)
        {
            throw new InvalidOperationException($"{label} BMP 尺寸错误：expected={width}x{height}, actual={file.Width}x{file.Height}");
        }
    }

    private static void AssertRawReimportable(CczProject project, BmpExportResult result, string role, E5RawImageSpec spec, string label)
    {
        if (!File.Exists(PortableInstallPaths.PaletteTsbPath) && !File.Exists(Path.Combine(project.GameRoot, "tsb")))
        {
            Console.WriteLine($"BMP_EXPORT_SMOKE_SKIP raw reimport no palette label={label}");
            return;
        }

        var file = result.Files.FirstOrDefault(file => file.Role.Equals(role, StringComparison.Ordinal))
                   ?? throw new InvalidOperationException($"{label} BMP 导出缺少 {role}。");
        var encoded = new E5RawImageCodec().EncodeFile(project, file.OutputPath, spec, strictHeight: true);
        if (encoded.RawBytes.Length != file.Width * file.Height)
        {
            throw new InvalidOperationException($"{label} BMP 无法按 RAW 条带回导：raw={encoded.RawBytes.Length}, expected={file.Width * file.Height}");
        }
    }

    private static void AssertExportMatchesPreviewBitmap(BmpExportResult result, string role, Bitmap? expected, string label)
    {
        if (expected == null) return;
        var file = result.Files.FirstOrDefault(file => file.Role.Equals(role, StringComparison.Ordinal));
        if (file == null) return;

        using var exported = new Bitmap(file.OutputPath);
        using var compositedExpected = BmpImageExportService.CompositeTransparentToMagenta(expected);
        if (!DllIconBitmapCodec.ArePixelEqual(exported, compositedExpected))
        {
            throw new InvalidOperationException($"{label} 导出 BMP 与共享预览解码结果不一致。");
        }
    }

    private static void CopyBmpSmokeResource(CczProject project, string smokeRoot, string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var source = Ccz66RevisedLayout.ResolveResourcePath(project, normalized);
        if (!File.Exists(source))
        {
            source = CharacterImageResourceService.ResolveGameFile(project, Path.GetFileName(normalized));
        }

        if (!File.Exists(source)) return;

        var target = Path.Combine(smokeRoot, normalized);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target))
        {
            File.Copy(source, target, overwrite: false);
        }
    }

    private static void CopyBmpSmokePalette(CczProject project, string smokeRoot)
    {
        var candidates = new[]
        {
            Path.Combine(project.GameRoot, "tsb"),
            PortableInstallPaths.PaletteTsbPath
        };
        var source = candidates.FirstOrDefault(File.Exists);
        if (source == null) return;

        var target = Path.Combine(smokeRoot, "tsb");
        File.Copy(source, target, overwrite: false);
    }
}
