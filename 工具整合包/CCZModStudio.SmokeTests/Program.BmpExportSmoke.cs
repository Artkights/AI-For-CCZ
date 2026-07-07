using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Globalization;

internal partial class Program
{
    static void RunBmpExportSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "BmpExportSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);

        VerifyMagentaComposite(smokeRoot);

        var service = new BmpImageExportService();
        var raw = new E5RawImageCodec();

        RunCharacterImageLayoutSmoke(project);
        RunJobSExportSmoke(project, service, raw, smokeRoot);
        RunRExportSmoke(project, service, raw, smokeRoot);
        RunSExportSmoke(project, service, raw, smokeRoot);
        RunFaceExportSmoke(project, service, smokeRoot);
        RunItemIconExportSmoke(project, service, smokeRoot);
        RunStrategyIconExportSmoke(project, service, smokeRoot);

        var jsonFiles = Directory.EnumerateFiles(smokeRoot, "*.json", SearchOption.AllDirectories).ToArray();
        if (jsonFiles.Length != 0)
        {
            throw new InvalidOperationException("BMP export smoke should not create JSON files: " + string.Join(", ", jsonFiles));
        }

        Console.WriteLine($"BMP_EXPORT_SMOKE OK root={smokeRoot}");
    }

    private static void RunCharacterImageLayoutSmoke(CczProject project)
    {
        var layout = new CharacterImageLayoutService().Resolve(project);
        var e5 = new E5ImageReplaceService();
        var pmapPath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        if (File.Exists(pmapPath))
        {
            var rEntries = e5.ReadIndex(pmapPath).Count;
            var expectedRMax = rEntries >= 2 ? rEntries / 2 - 1 : 0;
            if (layout.RMaxId != expectedRMax)
            {
                throw new InvalidOperationException($"R layout max mismatch: actual={layout.RMaxId}, expected={expectedRMax}, evidence={layout.Evidence}");
            }
        }

        var unitPaths = new[] { "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5" }
            .Select(fileName => CharacterImageResourceService.ResolveGameFile(project, fileName))
            .ToArray();
        if (unitPaths.Any(path => !File.Exists(path)))
        {
            Console.WriteLine("BMP_EXPORT_SMOKE skip layout S max: Unit_*.e5 missing.");
            return;
        }

        var unitEntryCount = unitPaths.Select(path => e5.ReadIndex(path).Count).Min();
        var expectedSMax = Math.Max(0,
            CharacterImageLayoutService.DefaultThreeStageSpecialCount +
            unitEntryCount -
            CharacterImageLayoutService.DefaultOneStageSpecialStart);
        if (layout.SMaxId != expectedSMax)
        {
            throw new InvalidOperationException($"S layout max mismatch: actual={layout.SMaxId}, expected={expectedSMax}, evidence={layout.Evidence}");
        }

        if (layout.SMaxId > CharacterImageLayoutService.DefaultThreeStageSpecialCount)
        {
            var maxMapping = CharacterImageResourceService.ResolveSUnitImageMapping(project, layout.SMaxId);
            if (maxMapping.ImageNumbers.Count != 1 || maxMapping.ImageNumbers[0] != unitEntryCount)
            {
                throw new InvalidOperationException(
                    $"S max mapping mismatch: S{layout.SMaxId} -> {string.Join("/", maxMapping.ImageNumbers)}, expected Unit #{unitEntryCount}.");
            }

            var overflowMapping = CharacterImageResourceService.ResolveSUnitImageMapping(project, layout.SMaxId + 1);
            if (overflowMapping.ImageNumbers.Count == 1 && overflowMapping.ImageNumbers[0] <= unitEntryCount)
            {
                throw new InvalidOperationException(
                    $"S overflow mapping should exceed Unit entries: S{layout.SMaxId + 1} -> {overflowMapping.ImageNumbers[0]}, unit entries={unitEntryCount}.");
            }
        }

        Console.WriteLine($"BMP_EXPORT_LAYOUT_SMOKE OK profile={layout.ProfileName} RMax={layout.RMaxId} SMax={layout.SMaxId}");
    }

    private static void RunJobSExportSmoke(CczProject project, BmpImageExportService service, E5RawImageCodec raw, string smokeRoot)
    {
        if (!HasFiles(project, "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5"))
        {
            Console.WriteLine("BMP_EXPORT_SMOKE skip job S: Unit_*.e5 missing.");
            return;
        }

        var output = Path.Combine(smokeRoot, "single_job_s");
        var result = service.Export(project, new BmpExportRequest
        {
            Kind = BmpExportKind.JobSImage,
            OutputRoot = output,
            SingleMode = true,
            Targets = new[]
            {
                new BmpExportTarget { RowId = 0, DisplayName = "Job0", FieldValue = 0, JobId = 0 }
            }
        });

        AssertExported(result, 3, "single job S");
        AssertBmpDimensions(Path.Combine(output, "mov.bmp"), 48, 528);
        AssertBmpDimensions(Path.Combine(output, "atk.bmp"), 64, 768);
        AssertBmpDimensions(Path.Combine(output, "spc.bmp"), 48, 240);
        raw.EncodeFile(project, Path.Combine(output, "mov.bmp"), E5RawImageCodec.UnitMovSpec, strictHeight: true);
        raw.EncodeFile(project, Path.Combine(output, "atk.bmp"), E5RawImageCodec.UnitAtkSpec, strictHeight: true);
        raw.EncodeFile(project, Path.Combine(output, "spc.bmp"), E5RawImageCodec.UnitSpcSpec, strictHeight: true);

        var batchOutput = Path.Combine(smokeRoot, "batch_job_s");
        var batchResult = service.Export(project, new BmpExportRequest
        {
            Kind = BmpExportKind.JobSImage,
            OutputRoot = batchOutput,
            SingleMode = false,
            Targets = new[]
            {
                new BmpExportTarget { RowId = 0, DisplayName = "Job0", FieldValue = 0, JobId = 0 },
                new BmpExportTarget { RowId = 1, DisplayName = "Job1", FieldValue = 0, JobId = 1 }
            }
        });
        AssertExported(batchResult, 6, "batch job S");
        AssertBmpDimensions(Path.Combine(batchOutput, "Job0", "mov.bmp"), 48, 528);
        AssertBmpDimensions(Path.Combine(batchOutput, "Job1", "spc.bmp"), 48, 240);
    }

    private static void RunRExportSmoke(CczProject project, BmpImageExportService service, E5RawImageCodec raw, string smokeRoot)
    {
        if (!File.Exists(CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5")))
        {
            Console.WriteLine("BMP_EXPORT_SMOKE skip R: Pmapobj.e5 missing.");
            return;
        }

        var output = Path.Combine(smokeRoot, "batch_r");
        var result = service.Export(project, new BmpExportRequest
        {
            Kind = BmpExportKind.RImage,
            OutputRoot = output,
            SingleMode = false,
            Targets = new[]
            {
                new BmpExportTarget { RowId = 0, DisplayName = "RSmokeA", FieldValue = 0 },
                new BmpExportTarget { RowId = 1, DisplayName = "RSmokeB", FieldValue = 1 }
            }
        });

        AssertExported(result, 4, "batch R");
        if (!Directory.Exists(Path.Combine(output, "R0")) || !Directory.Exists(Path.Combine(output, "R1")))
        {
            throw new InvalidOperationException("Batch R export should create importable R0/R1 folders without display-name suffixes.");
        }

        var front = Directory.EnumerateFiles(output, "front.bmp", SearchOption.AllDirectories).First();
        var back = Directory.EnumerateFiles(output, "back.bmp", SearchOption.AllDirectories).First();
        AssertBmpDimensions(front, 48, 1280);
        AssertBmpDimensions(back, 48, 1280);
        raw.EncodeFile(project, front, new E5RawImageSpec(E5RawImageCodec.PmapobjSpec.FileName, 48, 64, 1280), strictHeight: true);
    }

    private static void RunSExportSmoke(CczProject project, BmpImageExportService service, E5RawImageCodec raw, string smokeRoot)
    {
        if (!HasFiles(project, "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5"))
        {
            Console.WriteLine("BMP_EXPORT_SMOKE skip S: Unit_*.e5 missing.");
            return;
        }

        var output = Path.Combine(smokeRoot, "batch_s");
        var result = service.Export(project, new BmpExportRequest
        {
            Kind = BmpExportKind.SImage,
            OutputRoot = output,
            SingleMode = false,
            Targets = new[]
            {
                new BmpExportTarget { RowId = 1, DisplayName = "SSmokeA", FieldValue = 1 },
                new BmpExportTarget { RowId = 250, DisplayName = "SSmokeB", FieldValue = 250 }
            }
        });

        AssertExported(result, 6, "batch S");
        if (!Directory.Exists(Path.Combine(output, "S1")) || !Directory.Exists(Path.Combine(output, "S250")))
        {
            throw new InvalidOperationException("Batch S export should create importable S1/S250 folders without display-name suffixes.");
        }

        var mov = Directory.EnumerateFiles(output, "mov.bmp", SearchOption.AllDirectories).First();
        AssertBmpDimensions(mov, 48, 528);
        raw.EncodeFile(project, mov, E5RawImageCodec.UnitMovSpec, strictHeight: true);

        var allStagesOutput = Path.Combine(smokeRoot, "batch_s_all_stages");
        var allStages = service.Export(project, new BmpExportRequest
        {
            Kind = BmpExportKind.SImage,
            OutputRoot = allStagesOutput,
            SingleMode = false,
            SImageStageSlots = new[] { 1, 2, 3 },
            Targets = new[]
            {
                new BmpExportTarget { RowId = 1, DisplayName = "SSmokeA", FieldValue = 1 },
                new BmpExportTarget { RowId = 250, DisplayName = "SSmokeB", FieldValue = 250 }
            }
        });
        AssertExported(allStages, 12, "batch S all stages");
        AssertBmpDimensions(Path.Combine(allStagesOutput, "S1", "turn1", "mov.bmp"), 48, 528);
        AssertBmpDimensions(Path.Combine(allStagesOutput, "S1", "turn2", "atk.bmp"), 64, 768);
        AssertBmpDimensions(Path.Combine(allStagesOutput, "S1", "turn3", "spc.bmp"), 48, 240);
        AssertBmpDimensions(Path.Combine(allStagesOutput, "S250", "mov.bmp"), 48, 528);
        if (allStages.Files.Where(file => file.FieldValue == 1).Select(file => file.ImageNumber).Distinct().OrderBy(x => x).SequenceEqual(new int?[] { 241, 242, 243 }) == false)
        {
            throw new InvalidOperationException("S=1 all-stage export should use Unit #241/#242/#243.");
        }

        if (allStages.Files.Where(file => file.FieldValue == 250).Select(file => file.ImageNumber).Distinct().SingleOrDefault() != 554)
        {
            throw new InvalidOperationException("S=250 all-stage export should only use Unit #554.");
        }

        if (allStages.Warnings.All(warning => !warning.Contains("S250", StringComparison.Ordinal) || !warning.Contains("忽略", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("S=250 all-stage export should warn that second/third turns were ignored.");
        }

        RunKnown65PlMaxSExportSmoke(project, service, smokeRoot);
    }

    private static void RunKnown65PlMaxSExportSmoke(CczProject project, BmpImageExportService service, string smokeRoot)
    {
        var layout = new CharacterImageLayoutService().Resolve(project);
        if (!layout.ProfileName.Contains("6.5pl 神话三国志 2026 新春版", StringComparison.Ordinal))
        {
            return;
        }

        var output = Path.Combine(smokeRoot, "known_65pl_max_s");
        var result = service.Export(project, new BmpExportRequest
        {
            Kind = BmpExportKind.SImage,
            OutputRoot = output,
            SingleMode = true,
            Targets = new[]
            {
                new BmpExportTarget { RowId = layout.SMaxId, DisplayName = "SMax", FieldValue = layout.SMaxId }
            }
        });

        AssertExported(result, 3, $"known 6.5pl S{layout.SMaxId}");
        if (result.Files.Any(file => file.ImageNumber != layout.UnitEntryCount || file.VisiblePixels <= 0))
        {
            throw new InvalidOperationException(
                $"Known 6.5pl max S export should use visible Unit #{layout.UnitEntryCount}: " +
                string.Join(", ", result.Files.Select(file => $"{file.Role}=#{file.ImageNumber} visible={file.VisiblePixels}")));
        }

        AssertBmpDimensions(Path.Combine(output, "mov.bmp"), 48, 528);
        AssertBmpDimensions(Path.Combine(output, "atk.bmp"), 64, 768);
        AssertBmpDimensions(Path.Combine(output, "spc.bmp"), 48, 240);
        Console.WriteLine($"BMP_EXPORT_65PL_MAX_S_SMOKE OK S{layout.SMaxId}=Unit#{layout.UnitEntryCount}");
    }

    private static void RunFaceExportSmoke(CczProject project, BmpImageExportService service, string smokeRoot)
    {
        var facePath = CharacterImageResourceService.ResolveFaceFile(project);
        if (facePath == null || !File.Exists(facePath))
        {
            Console.WriteLine("BMP_EXPORT_SMOKE skip face: Face.e5 missing.");
            return;
        }

        var output = Path.Combine(smokeRoot, "single_face0");
        var result = service.Export(project, new BmpExportRequest
        {
            Kind = BmpExportKind.Face,
            OutputRoot = output,
            SingleMode = true,
            Targets = new[]
            {
                new BmpExportTarget { RowId = 0, DisplayName = "Face0", FieldValue = 0 }
            }
        });

        AssertExported(result, 1, "face 0");
        AssertBmpDimensions(Path.Combine(output, "face_0.bmp"), 120, 120);
    }

    private static void RunItemIconExportSmoke(CczProject project, BmpImageExportService service, string smokeRoot)
    {
        var resource = Ccz66RevisedLayout.ResolveResourcePath(project, Ccz66RevisedLayout.ResolveItemIconResourceFile(project));
        if (!File.Exists(resource))
        {
            Console.WriteLine("BMP_EXPORT_SMOKE skip item icon: resource missing.");
            return;
        }

        var output = Path.Combine(smokeRoot, "single_item_icon");
        var result = service.Export(project, new BmpExportRequest
        {
            Kind = BmpExportKind.ItemIcon,
            OutputRoot = output,
            SingleMode = true,
            Targets = new[]
            {
                new BmpExportTarget { RowId = 0, DisplayName = "Item0", FieldValue = 0 }
            }
        });

        var iconPath = Path.Combine(output, "item_icon_0.bmp");
        if (Ccz66RevisedLayout.Is66(project))
        {
            AssertExported(result, 1, "item icon");
            if (!File.Exists(iconPath) ||
                File.Exists(Path.Combine(output, "small.bmp")) ||
                File.Exists(Path.Combine(output, "large.bmp")))
            {
                throw new InvalidOperationException("6.6 item icon export should create item_icon_0.bmp only for the default importable layout.");
            }

            AssertBmpDimensions(iconPath, 32, 32);
            AssertBmpContainsRgb(iconPath, 247, 0, 255);
        }
        else
        {
            AssertExported(result, 3, "item icon");
            var smallPath = Path.Combine(output, "item_icon_0_small.bmp");
            var largePath = Path.Combine(output, "item_icon_0_large.bmp");
            if (!File.Exists(smallPath) || !File.Exists(largePath) || !File.Exists(iconPath))
            {
                throw new InvalidOperationException("6.5 item icon export should create small/large storage BMP files plus a large compatibility alias.");
            }

            AssertBmpDimensions(smallPath, 16, 16);
            AssertBmpDimensions(largePath, 32, 32);
            AssertBmpDimensions(iconPath, 32, 32);
            var codec = new DllBitmapIconCodecService();
            var resources = codec.ParseBitmapResources(resource);
            var pair = codec.ResolveBitmapResourcePair(resources, 0);
            var small = codec.SelectDisplayVariant(pair.SmallVariants) ?? throw new InvalidOperationException("Missing exported small source resource.");
            var large = codec.SelectDisplayVariant(pair.LargeVariants) ?? throw new InvalidOperationException("Missing exported large source resource.");
            AssertBmpBitDepth(smallPath, small.BitCount);
            AssertBmpBitDepth(largePath, large.BitCount);
        }
    }

    private static void RunStrategyIconExportSmoke(CczProject project, BmpImageExportService service, string smokeRoot)
    {
        var resource = Ccz66RevisedLayout.ResolveResourcePath(project, Ccz66RevisedLayout.ResolveStrategyIconResourceFile(project));
        if (!File.Exists(resource))
        {
            Console.WriteLine("BMP_EXPORT_SMOKE skip strategy icon: resource missing.");
            return;
        }

        var output = Path.Combine(smokeRoot, "single_strategy_icon");
        var result = service.Export(project, new BmpExportRequest
        {
            Kind = BmpExportKind.StrategyIcon,
            OutputRoot = output,
            SingleMode = true,
            Targets = new[]
            {
                new BmpExportTarget { RowId = 0, DisplayName = "Strategy0", FieldValue = 0 }
            }
        });

        AssertExported(result, 1, "strategy icon");
        if (!File.Exists(Path.Combine(output, "strategy_icon_0.bmp")) ||
            File.Exists(Path.Combine(output, "icon.bmp")))
        {
            throw new InvalidOperationException("Strategy icon export should create strategy_icon_0.bmp only for the default importable layout.");
        }
    }

    private static void VerifyMagentaComposite(string smokeRoot)
    {
        using var source = new Bitmap(2, 1);
        source.SetPixel(0, 0, Color.Transparent);
        source.SetPixel(1, 0, Color.FromArgb(255, 12, 34, 56));
        var path = Path.Combine(smokeRoot, "magenta_check.bmp");
        BmpImageExportService.SaveBmpWithMagentaBackground(source, path);
        using var read = new Bitmap(path);
        var transparent = read.GetPixel(0, 0);
        var opaque = read.GetPixel(1, 0);
        if (transparent.R != 255 || transparent.G != 0 || transparent.B != 255 ||
            opaque.R != 12 || opaque.G != 34 || opaque.B != 56)
        {
            throw new InvalidOperationException("Transparent-to-magenta BMP composition failed.");
        }
    }

    private static bool HasFiles(CczProject project, params string[] fileNames)
        => fileNames.All(fileName => File.Exists(CharacterImageResourceService.ResolveGameFile(project, fileName)));

    private static void AssertExported(BmpExportResult result, int expectedFiles, string label)
    {
        if (result.Files.Count != expectedFiles || result.SkippedItems.Count != 0)
        {
            throw new InvalidOperationException(
                $"{label} export mismatch: files={result.Files.Count}, skipped={result.SkippedItems.Count}, expected={expectedFiles}.");
        }
    }

    private static void AssertBmpDimensions(string path, int width, int height)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Expected BMP was not created.", path);
        }

        using var image = Image.FromFile(path);
        if (image.Width != width || image.Height != height)
        {
            throw new InvalidOperationException($"Unexpected BMP dimensions for {path}: {image.Width}x{image.Height}, expected {width}x{height}.");
        }
    }

    private static void AssertBmpContainsRgb(string path, int r, int g, int b)
    {
        using var bitmap = new Bitmap(path);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R == r && pixel.G == g && pixel.B == b)
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException($"BMP {path} does not contain RGB({r},{g},{b}).");
    }

    private static void AssertBmpBitDepth(string path, int expectedBitDepth)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 30 || bytes[0] != 'B' || bytes[1] != 'M')
        {
            throw new InvalidOperationException($"Not a BMP file: {path}");
        }

        var actual = BitConverter.ToUInt16(bytes, 28);
        if (actual != expectedBitDepth)
        {
            throw new InvalidOperationException($"Unexpected BMP bit depth for {path}: {actual}, expected {expectedBitDepth}.");
        }
    }
}
