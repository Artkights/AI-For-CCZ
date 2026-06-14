using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RImageReplaceService
{
    private const int RStripWidth = 48;
    private const int RFrameHeight = 64;
    private const int RFrameCount = 20;
    private const int RStripHeight = RFrameHeight * RFrameCount;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private static readonly E5RawImageSpec RStripSpec = new(
        E5RawImageCodec.PmapobjSpec.FileName,
        E5RawImageCodec.PmapobjSpec.Width,
        E5RawImageCodec.PmapobjSpec.FrameHeight,
        RStripHeight);

    private readonly E5RawImageCodec _codec = new();
    private readonly E5ImageReplaceService _replace = new();

    public RImageReplacePreviewResult Preview(CczProject project, RImageReplaceRequest request)
    {
        var mapping = ResolveMapping(project, request);
        var filePlans = BuildFilePlans(project, request, mapping);
        var warnings = new List<string>();
        var files = new List<RImageReplaceFilePreview>();
        var requests = new List<E5ImageBatchReplaceRequest>();

        foreach (var plan in filePlans)
        {
            var encode = _codec.EncodeFile(project, plan.SourcePath, RStripSpec, strictHeight: true);
            warnings.AddRange(encode.Warnings.Select(warning => $"{plan.Role}: {warning}"));
            files.Add(new RImageReplaceFilePreview
            {
                Role = plan.Role,
                TargetFileName = plan.TargetFileName,
                TargetPath = plan.TargetPath,
                SourcePath = plan.SourcePath,
                ImageNumber = plan.ImageNumber,
                Encode = encode
            });
            requests.Add(new E5ImageBatchReplaceRequest
            {
                ImageNumber = plan.ImageNumber,
                SourceBytes = encode.RawBytes,
                SourceBytesAreRaw = true,
                SourceLabel = $"{encode.SourcePath} -> RAW",
                OperationKind = $"一键替换R形象-{plan.Role}"
            });
        }

        var preview = _replace.PreviewBatchReplacement(project, filePlans[0].TargetPath, requests);
        return new RImageReplacePreviewResult
        {
            Request = request,
            Mapping = mapping,
            Files = files,
            BatchPreview = preview,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    public RImageReplaceResult Replace(CczProject project, RImageReplaceRequest request)
    {
        var preview = Preview(project, request);
        var requests = preview.Files.Select(file => new E5ImageBatchReplaceRequest
        {
            ImageNumber = file.ImageNumber,
            SourceBytes = file.Encode.RawBytes,
            SourceBytesAreRaw = true,
            SourceLabel = $"{file.Encode.SourcePath} -> RAW",
            OperationKind = $"一键替换R形象-{file.Role}"
        }).ToArray();
        var result = _replace.ReplaceBatch(project, preview.Files[0].TargetPath, requests);
        var payload = new RImageReplaceResult
        {
            Request = request,
            Mapping = preview.Mapping,
            Files = preview.Files.Select(file => new RImageReplaceFileResult
            {
                Role = file.Role,
                TargetFileName = file.TargetFileName,
                TargetPath = file.TargetPath,
                SourcePath = file.SourcePath,
                ImageNumber = file.ImageNumber,
                Encode = file.Encode
            }).ToArray(),
            WriteResult = result,
            Warnings = preview.Warnings
        };

        return new RImageReplaceResult
        {
            Request = payload.Request,
            Mapping = payload.Mapping,
            Files = payload.Files,
            WriteResult = payload.WriteResult,
            Warnings = payload.Warnings,
            AggregateReportPath = WriteAggregateReport(project, payload)
        };
    }

    private RImageMappingSnapshot ResolveMapping(CczProject project, RImageReplaceRequest request)
    {
        if (request.RImageId < 0)
        {
            throw new InvalidOperationException($"R形象编号不能小于 0：{request.RImageId}");
        }

        var front = checked(request.RImageId * 2 + 1);
        var back = checked(request.RImageId * 2 + 2);
        var targetPath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException("找不到目标 E5：Pmapobj.e5", targetPath);
        }

        var entries = _replace.ReadIndex(targetPath);
        if (front <= 0 || back > entries.Count)
        {
            throw new InvalidOperationException($"R={request.RImageId} 映射到 Pmapobj.e5 #{front}/#{back}，但索引表只有 {entries.Count} 条。");
        }

        return new RImageMappingSnapshot
        {
            RImageId = request.RImageId,
            FrontImageNumber = front,
            BackImageNumber = back,
            ImageNumbers = new[] { front, back },
            Detail = $"R={request.RImageId} -> Pmapobj.e5 #{front}/#{back}"
        };
    }

    private static IReadOnlyList<RImageFilePlan> BuildFilePlans(CczProject project, RImageReplaceRequest request, RImageMappingSnapshot mapping)
    {
        if (string.IsNullOrWhiteSpace(request.MaterialFolder))
        {
            throw new InvalidOperationException("缺少 R 形象素材目录。");
        }

        var folder = Path.GetFullPath(request.MaterialFolder);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"找不到 R 形象素材目录：{folder}");
        }

        var targetPath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        var plans = new[]
        {
            new RImageFilePlan("正面", "Pmapobj.e5", targetPath, ResolveMaterialFile(folder, ["front.bmp", "x.bmp", "1.bmp"], "front.bmp / x.bmp / 1.bmp"), mapping.FrontImageNumber),
            new RImageFilePlan("反面", "Pmapobj.e5", targetPath, ResolveMaterialFile(folder, ["back.bmp", "y.bmp", "2.bmp"], "back.bmp / y.bmp / 2.bmp"), mapping.BackImageNumber)
        };

        foreach (var plan in plans)
        {
            if (!File.Exists(plan.SourcePath))
            {
                throw new FileNotFoundException($"素材目录缺少 {Path.GetFileName(plan.SourcePath)}。", plan.SourcePath);
            }
        }

        return plans;
    }

    private static string ResolveMaterialFile(string folder, IReadOnlyList<string> candidateNames, string displayNames)
    {
        var files = Directory.EnumerateFiles(folder).ToArray();
        foreach (var candidateName in candidateNames)
        {
            var path = files.FirstOrDefault(file => Path.GetFileName(file).Equals(candidateName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException($"素材目录缺少 {displayNames}。", Path.Combine(folder, candidateNames[0]));
    }

    private static string WriteAggregateReport(CczProject project, RImageReplaceResult result)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_RImageRawReplaceReport.json");
        var payload = new
        {
            OperationKind = "R形象一键RAW替换",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ProjectRoot = project.GameRoot,
            result.Request,
            Mapping = new
            {
                result.Mapping.RImageId,
                result.Mapping.FrontImageNumber,
                result.Mapping.BackImageNumber,
                result.Mapping.ImageNumbers,
                result.Mapping.Detail
            },
            result.TotalOperationCount,
            result.Warnings,
            Target = new
            {
                result.WriteResult.TargetRelativePath,
                result.WriteResult.OperationCount,
                result.WriteResult.BackupPath,
                result.WriteResult.ReportPath,
                result.WriteResult.ReportJsonPath
            },
            Files = result.Files.Select(file => new
            {
                file.Role,
                file.TargetFileName,
                file.TargetPath,
                file.ImageNumber,
                file.SourcePath,
                Encode = new
                {
                    file.Encode.SourceWidth,
                    file.Encode.SourceHeight,
                    file.Encode.RawLength,
                    file.Encode.TransparentPixels,
                    file.Encode.ExactPalettePixels,
                    file.Encode.NearestPalettePixels,
                    file.Encode.PalettePath,
                    file.Encode.Warnings
                }
            }),
            Operations = result.WriteResult.Operations.Select(operation => new
            {
                operation.ImageNumber,
                operation.OldKind,
                operation.NewKind,
                operation.OldSizeBytes,
                operation.NewSizeBytes
            })
        };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, JsonOptions));
        return reportPath;
    }

    private sealed record RImageFilePlan(
        string Role,
        string TargetFileName,
        string TargetPath,
        string SourcePath,
        int ImageNumber);
}
