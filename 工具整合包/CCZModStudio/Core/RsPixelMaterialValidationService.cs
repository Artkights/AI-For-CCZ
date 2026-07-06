using System.Drawing;
using System.Drawing.Imaging;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RsPixelMaterialValidationService
{
    private const int RWidth = 48;
    private const int RHeight = 1280;
    private const int MovWidth = 48;
    private const int MovHeight = 528;
    private const int AtkWidth = 64;
    private const int AtkHeight = 768;
    private const int SpcWidth = 48;
    private const int SpcHeight = 240;

    private readonly RImageReplaceService _rReplace = new();
    private readonly SImageReplaceService _sReplace = new();

    public RsPixelMaterialValidationResult Validate(CczProject project, RsPixelMaterialValidationRequest request)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var materialRoot = NormalizeOptionalDirectory(request.MaterialRoot);
        var rFolder = ResolveMaterialFolder(materialRoot, request.RMaterialFolder, "r_actor", HasRMaterialFiles);
        var sFolder = ResolveMaterialFolder(materialRoot, request.SMaterialFolder, "s_unit", HasSMaterialFiles);

        var checks = new List<RsPixelMaterialFileCheck>();
        checks.AddRange(CheckGroup("R", rFolder, new[]
        {
            RequiredFile("front", "front.bmp", RWidth, RHeight, "x.bmp", "1.bmp"),
            RequiredFile("back", "back.bmp", RWidth, RHeight, "y.bmp", "2.bmp")
        }));
        checks.AddRange(CheckGroup("S", sFolder, new[]
        {
            RequiredFile("move", "mov.bmp", MovWidth, MovHeight),
            RequiredFile("attack", "atk.bmp", AtkWidth, AtkHeight),
            RequiredFile("special", "spc.bmp", SpcWidth, SpcHeight)
        }));

        foreach (var check in checks)
        {
            warnings.AddRange(check.Warnings);
            errors.AddRange(check.Errors);
        }

        if (string.IsNullOrWhiteSpace(rFolder))
        {
            errors.Add("R material folder was not resolved. Provide material_root containing materials/r_actor, or pass r_material_folder.");
        }

        if (string.IsNullOrWhiteSpace(sFolder))
        {
            errors.Add("S material folder was not resolved. Provide material_root containing materials/s_unit, or pass s_material_folder.");
        }

        RImageReplacePreviewResult? rPreview = null;
        SImageReplacePreviewResult? sPreview = null;
        var previewOperations = new List<RsPixelMaterialPreviewOperation>();

        if (request.RImageId.HasValue && !string.IsNullOrWhiteSpace(rFolder) && Directory.Exists(rFolder))
        {
            try
            {
                rPreview = _rReplace.Preview(project, new RImageReplaceRequest
                {
                    RImageId = request.RImageId.Value,
                    MaterialFolder = rFolder
                });
                warnings.AddRange(rPreview.Warnings.Select(warning => "R preview: " + warning));
                previewOperations.AddRange(BuildPreviewOperations(rPreview));
            }
            catch (Exception ex)
            {
                errors.Add("R preview failed: " + ex.Message);
            }
        }

        if (request.SImageId.HasValue && !string.IsNullOrWhiteSpace(sFolder) && Directory.Exists(sFolder))
        {
            try
            {
                sPreview = _sReplace.Preview(project, new SImageReplaceRequest
                {
                    SImageId = request.SImageId.Value,
                    MaterialFolder = sFolder,
                    JobId = request.JobId,
                    FactionSlot = request.FactionSlot
                });
                warnings.AddRange(sPreview.Warnings.Select(warning => "S preview: " + warning));
                previewOperations.AddRange(BuildPreviewOperations(sPreview));
            }
            catch (Exception ex)
            {
                errors.Add("S preview failed: " + ex.Message);
            }
        }

        var formatPassed = checks.All(check => check.Exists && check.DimensionPassed);
        var previewRequested = request.RImageId.HasValue || request.SImageId.HasValue;
        var previewPassed = !previewRequested || (
            (!request.RImageId.HasValue || rPreview != null) &&
            (!request.SImageId.HasValue || sPreview != null));
        var requiresTestCopyWrite = previewOperations.Any(IsRawToPng) || previewOperations.Count > 0;
        var distinctWarnings = warnings.Distinct(StringComparer.Ordinal).ToArray();
        var distinctErrors = errors.Distinct(StringComparer.Ordinal).ToArray();

        return new RsPixelMaterialValidationResult
        {
            Request = request,
            MaterialRoot = materialRoot,
            RMaterialFolder = rFolder,
            SMaterialFolder = sFolder,
            Files = checks,
            RPreview = rPreview,
            SPreview = sPreview,
            PreviewOperations = previewOperations,
            Warnings = distinctWarnings,
            Errors = distinctErrors,
            FormatPassed = formatPassed,
            PreviewPassed = previewPassed,
            RequiresTestCopyWrite = requiresTestCopyWrite,
            ReadyForTestCopyWrite = formatPassed && previewPassed && distinctErrors.Length == 0,
            SafetyNote = "Read-only validation and preview only. R/S true-color replacement stores PNG entries; RAW -> PNG compatibility must be checked on a test copy before touching the formal base."
        };
    }

    private static RequiredMaterialFile RequiredFile(string role, string fileName, int width, int height, params string[] aliases)
        => new(role, fileName, width, height, aliases);

    private static IReadOnlyList<RsPixelMaterialFileCheck> CheckGroup(
        string group,
        string folder,
        IReadOnlyList<RequiredMaterialFile> requiredFiles)
    {
        return requiredFiles
            .Select(required => CheckFile(group, folder, required))
            .ToArray();
    }

    private static RsPixelMaterialFileCheck CheckFile(string group, string folder, RequiredMaterialFile required)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var path = string.IsNullOrWhiteSpace(folder)
            ? required.FileName
            : Path.Combine(folder, required.FileName);

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            errors.Add($"{group}/{required.FileName}: material folder does not exist.");
            return BuildMissingCheck(group, required, path, errors);
        }

        var candidateNames = required.CandidateNames;
        var matchedPath = candidateNames
            .Select(candidate => Directory
                .EnumerateFiles(folder)
                .FirstOrDefault(file => Path.GetFileName(file).Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        if (string.IsNullOrWhiteSpace(matchedPath))
        {
            errors.Add($"{group}/{required.FileName}: required file is missing. Accepted names: {string.Join(", ", candidateNames)}.");
            return BuildMissingCheck(group, required, path, errors);
        }

        try
        {
            using var bitmap = LoadBitmap(matchedPath);
            var dimensionPassed = bitmap.Width == required.Width && bitmap.Height == required.Height;
            if (!dimensionPassed)
            {
                errors.Add($"{group}/{required.FileName}: size {bitmap.Width}x{bitmap.Height} does not match required {required.Width}x{required.Height}.");
            }

            var stats = ScanPixels(bitmap);
            if (!stats.MagentaKeyLikely)
            {
                warnings.Add($"{group}/{required.FileName}: no strict or near magenta key pixels were detected; background transparency may fail.");
            }

            if (stats.InteriorNearMagentaPixelCount > stats.InteriorStrictMagentaPixelCount)
            {
                warnings.Add($"{group}/{required.FileName}: near-magenta pixels were detected away from the border; character pixels may be cut out if magenta is used inside the actor.");
            }

            return new RsPixelMaterialFileCheck
            {
                Group = group,
                Role = required.Role,
                ExpectedFileName = required.FileName,
                Path = matchedPath,
                Exists = true,
                ExpectedWidth = required.Width,
                ExpectedHeight = required.Height,
                Width = bitmap.Width,
                Height = bitmap.Height,
                DimensionPassed = dimensionPassed,
                PixelCount = stats.PixelCount,
                TransparentPixelCount = stats.TransparentPixelCount,
                StrictMagentaPixelCount = stats.StrictMagentaPixelCount,
                NearMagentaPixelCount = stats.NearMagentaPixelCount,
                StrictMagentaPercent = stats.StrictMagentaPercent,
                NearMagentaPercent = stats.NearMagentaPercent,
                InteriorStrictMagentaPixelCount = stats.InteriorStrictMagentaPixelCount,
                InteriorNearMagentaPixelCount = stats.InteriorNearMagentaPixelCount,
                MagentaKeyLikely = stats.MagentaKeyLikely,
                Warnings = warnings,
                Errors = errors
            };
        }
        catch (Exception ex)
        {
            errors.Add($"{group}/{required.FileName}: failed to read image: {ex.Message}");
            return BuildMissingCheck(group, required, matchedPath, errors);
        }
    }

    private static RsPixelMaterialFileCheck BuildMissingCheck(
        string group,
        RequiredMaterialFile required,
        string path,
        IReadOnlyList<string> errors)
        => new()
        {
            Group = group,
            Role = required.Role,
            ExpectedFileName = required.FileName,
            Path = path,
            Exists = false,
            ExpectedWidth = required.Width,
            ExpectedHeight = required.Height,
            DimensionPassed = false,
            Errors = errors
        };

    private static IReadOnlyList<RsPixelMaterialPreviewOperation> BuildPreviewOperations(RImageReplacePreviewResult preview)
    {
        var sourceByNumber = preview.Files.ToDictionary(file => file.ImageNumber, file => file.SourcePath);
        return preview.BatchPreview.Operations.Select(operation => new RsPixelMaterialPreviewOperation
        {
            Group = "R",
            TargetFileName = Path.GetFileName(preview.BatchPreview.TargetPath),
            TargetPath = preview.BatchPreview.TargetPath,
            SourcePath = sourceByNumber.TryGetValue(operation.ImageNumber, out var sourcePath) ? sourcePath : operation.SourcePath,
            ImageNumber = operation.ImageNumber,
            OldKind = operation.OldKind,
            NewKind = operation.NewKind,
            OldSizeBytes = operation.OldSizeBytes,
            NewSizeBytes = operation.NewSizeBytes,
            FormatWarnings = operation.FormatWarnings
        }).ToArray();
    }

    private static IReadOnlyList<RsPixelMaterialPreviewOperation> BuildPreviewOperations(SImageReplacePreviewResult preview)
    {
        var operations = new List<RsPixelMaterialPreviewOperation>();
        foreach (var file in preview.Files)
        {
            operations.AddRange(file.BatchPreview.Operations.Select(operation => new RsPixelMaterialPreviewOperation
            {
                Group = "S",
                TargetFileName = file.TargetFileName,
                TargetPath = file.TargetPath,
                SourcePath = file.SourcePath,
                ImageNumber = operation.ImageNumber,
                OldKind = operation.OldKind,
                NewKind = operation.NewKind,
                OldSizeBytes = operation.OldSizeBytes,
                NewSizeBytes = operation.NewSizeBytes,
                FormatWarnings = operation.FormatWarnings
            }));
        }

        return operations;
    }

    private static PixelStats ScanPixels(Bitmap bitmap)
    {
        var strictMagenta = 0;
        var nearMagenta = 0;
        var transparent = 0;
        var interiorStrictMagenta = 0;
        var interiorNearMagenta = 0;
        var pixelCount = checked(bitmap.Width * bitmap.Height);
        var interiorMarginX = Math.Max(2, bitmap.Width / 8);
        var interiorMarginY = Math.Max(2, ResolveFrameHeight(bitmap.Width) / 8);

        for (var y = 0; y < bitmap.Height; y++)
        {
            var yInFrame = y % ResolveFrameHeight(bitmap.Width);
            var interiorY = yInFrame >= interiorMarginY && yInFrame < ResolveFrameHeight(bitmap.Width) - interiorMarginY;
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A == 0)
                {
                    transparent++;
                }

                var isStrict = IsStrictMagentaKey(color);
                var isNear = IsNearMagentaKey(color);
                if (isStrict)
                {
                    strictMagenta++;
                }

                if (isNear)
                {
                    nearMagenta++;
                }

                var interiorX = x >= interiorMarginX && x < bitmap.Width - interiorMarginX;
                if (interiorX && interiorY)
                {
                    if (isStrict)
                    {
                        interiorStrictMagenta++;
                    }

                    if (isNear)
                    {
                        interiorNearMagenta++;
                    }
                }
            }
        }

        return new PixelStats(
            pixelCount,
            transparent,
            strictMagenta,
            nearMagenta,
            Percent(strictMagenta, pixelCount),
            Percent(nearMagenta, pixelCount),
            interiorStrictMagenta,
            interiorNearMagenta,
            transparent > 0 || strictMagenta > 0 || nearMagenta > 0);
    }

    private static int ResolveFrameHeight(int width)
        => width == AtkWidth ? 64 : 48;

    private static double Percent(int count, int total)
        => total <= 0 ? 0 : Math.Round(count * 100.0 / total, 4);

    private static Bitmap LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        using var raw = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        var bitmap = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawImage(raw, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    private static bool IsStrictMagentaKey(Color color)
        => color.A != 0 && color.R >= 247 && color.G <= 8 && color.B >= 248;

    private static bool IsNearMagentaKey(Color color)
        => color.A != 0 && color.R >= 220 && color.G <= 80 && color.B >= 220;

    private static bool IsRawToPng(RsPixelMaterialPreviewOperation operation)
        => operation.OldKind.Equals("RAW", StringComparison.OrdinalIgnoreCase) &&
           operation.NewKind.Equals("PNG", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeOptionalDirectory(string path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);

    private static string ResolveMaterialFolder(
        string materialRoot,
        string explicitFolder,
        string subfolderName,
        Func<string, bool> folderHasExpectedFiles)
    {
        if (!string.IsNullOrWhiteSpace(explicitFolder))
        {
            return Path.GetFullPath(explicitFolder);
        }

        if (string.IsNullOrWhiteSpace(materialRoot))
        {
            return string.Empty;
        }

        var candidates = new[]
        {
            Path.Combine(materialRoot, "materials", subfolderName),
            Path.Combine(materialRoot, subfolderName),
            materialRoot
        };

        return candidates.FirstOrDefault(path => Directory.Exists(path) && folderHasExpectedFiles(path)) ??
               candidates.FirstOrDefault(Directory.Exists) ??
               string.Empty;
    }

    private static bool HasRMaterialFiles(string folder)
        => FileExists(folder, "front.bmp") || FileExists(folder, "back.bmp") ||
           FileExists(folder, "x.bmp") || FileExists(folder, "y.bmp") ||
           FileExists(folder, "1.bmp") || FileExists(folder, "2.bmp");

    private static bool HasSMaterialFiles(string folder)
        => FileExists(folder, "mov.bmp") || FileExists(folder, "atk.bmp") || FileExists(folder, "spc.bmp");

    private static bool FileExists(string folder, string fileName)
        => Directory.EnumerateFiles(folder)
            .Any(file => Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase));

    private sealed record RequiredMaterialFile(string Role, string FileName, int Width, int Height, IReadOnlyList<string> Aliases)
    {
        public IReadOnlyList<string> CandidateNames { get; } = new[] { FileName }.Concat(Aliases).ToArray();
    }

    private sealed record PixelStats(
        int PixelCount,
        int TransparentPixelCount,
        int StrictMagentaPixelCount,
        int NearMagentaPixelCount,
        double StrictMagentaPercent,
        double NearMagentaPercent,
        int InteriorStrictMagentaPixelCount,
        int InteriorNearMagentaPixelCount,
        bool MagentaKeyLikely);
}
