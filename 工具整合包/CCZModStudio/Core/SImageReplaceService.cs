using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class SImageReplaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly E5RawImageCodec _codec = new();
    private readonly E5ImageReplaceService _replace = new();

    public SImageReplacePreviewResult Preview(CczProject project, SImageReplaceRequest request)
    {
        var resolvedMapping = ResolveMapping(request);
        var mapping = ToSnapshot(resolvedMapping);
        var filePlans = BuildFilePlans(project, request);
        var warnings = new List<string>();
        if (request.SImageId is >= 1 and <= 32)
        {
            warnings.Add("S=1..32 会把同一套 mov/atk/spc 素材写入该 S 对应的三个 Unit 图号。");
        }

        var files = new List<SImageReplaceFilePreview>();
        foreach (var plan in filePlans)
        {
            var encode = _codec.EncodeFile(project, plan.SourcePath, plan.Spec, strictHeight: true);
            warnings.AddRange(encode.Warnings.Select(warning => $"{plan.TargetFileName}: {warning}"));
            var requests = BuildRequests(mapping.ImageNumbers, encode, plan.ActionName);
            var preview = _replace.PreviewBatchReplacement(project, plan.TargetPath, requests);
            files.Add(new SImageReplaceFilePreview
            {
                ActionName = plan.ActionName,
                TargetFileName = plan.TargetFileName,
                TargetPath = plan.TargetPath,
                SourcePath = plan.SourcePath,
                Encode = encode,
                BatchPreview = preview
            });
        }

        return new SImageReplacePreviewResult
        {
            Request = request,
            Mapping = mapping,
            Files = files,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    public SImageReplaceResult Replace(CczProject project, SImageReplaceRequest request)
    {
        var preview = Preview(project, request);
        var files = new List<SImageReplaceFileResult>();
        foreach (var file in preview.Files)
        {
            var requests = BuildRequests(preview.Mapping.ImageNumbers, file.Encode, file.ActionName);
            var result = _replace.ReplaceBatch(project, file.TargetPath, requests);
            files.Add(new SImageReplaceFileResult
            {
                ActionName = file.ActionName,
                TargetFileName = file.TargetFileName,
                TargetPath = file.TargetPath,
                SourcePath = file.SourcePath,
                Encode = file.Encode,
                WriteResult = result
            });
        }

        var resultPayload = new SImageReplaceResult
        {
            Request = request,
            Mapping = preview.Mapping,
            Files = files,
            Warnings = preview.Warnings
        };

        return new SImageReplaceResult
        {
            Request = resultPayload.Request,
            Mapping = resultPayload.Mapping,
            Files = resultPayload.Files,
            Warnings = resultPayload.Warnings,
            AggregateReportPath = WriteAggregateReport(project, resultPayload)
        };
    }

    private static SUnitImageMapping ResolveMapping(SImageReplaceRequest request)
    {
        if (request.SImageId == 0 && (!request.JobId.HasValue || request.JobId.Value < 0))
        {
            throw new InvalidOperationException("S=0 必须从已选角色执行，并提供有效职业编号。");
        }

        var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(
            request.SImageId,
            request.JobId,
            request.FactionSlot);
        if (mapping.ImageNumbers.Count == 0)
        {
            throw new InvalidOperationException(mapping.Detail);
        }

        return mapping;
    }

    private static SImageMappingSnapshot ToSnapshot(SUnitImageMapping mapping)
        => new()
        {
            SImageId = mapping.SImageId,
            JobId = mapping.JobId,
            FactionSlot = mapping.FactionSlot,
            ImageNumbers = mapping.ImageNumbers.ToArray(),
            ShortText = mapping.ShortText,
            Detail = mapping.Detail
        };

    private IReadOnlyList<SImageFilePlan> BuildFilePlans(CczProject project, SImageReplaceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MaterialFolder))
        {
            throw new InvalidOperationException("缺少 S 形象素材目录。");
        }

        var folder = Path.GetFullPath(request.MaterialFolder);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"找不到 S 形象素材目录：{folder}");
        }

        var plans = new[]
        {
            new SImageFilePlan("移动", "Unit_mov.e5", CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5"), ResolveMaterialFile(folder, "mov.bmp"), E5RawImageCodec.UnitMovSpec),
            new SImageFilePlan("攻击", "Unit_atk.e5", CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5"), ResolveMaterialFile(folder, "atk.bmp"), E5RawImageCodec.UnitAtkSpec),
            new SImageFilePlan("特技", "Unit_spc.e5", CharacterImageResourceService.ResolveGameFile(project, "Unit_spc.e5"), ResolveMaterialFile(folder, "spc.bmp"), E5RawImageCodec.UnitSpcSpec)
        }
            .Where(plan => !string.IsNullOrWhiteSpace(plan.SourcePath))
            .ToArray();

        if (plans.Length == 0)
        {
            throw new InvalidOperationException("没有找到可导入的 S 形象素材。");
        }

        foreach (var plan in plans)
        {
            if (!File.Exists(plan.TargetPath))
            {
                throw new FileNotFoundException($"找不到目标 E5：{plan.TargetFileName}", plan.TargetPath);
            }
        }

        return plans;
    }

    private static string ResolveMaterialFile(string folder, string fileName)
    {
        var path = Directory
            .EnumerateFiles(folder)
            .FirstOrDefault(file => Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return string.Empty;
    }

    private static IReadOnlyList<E5ImageBatchReplaceRequest> BuildRequests(IReadOnlyList<int> imageNumbers, E5RawEncodeResult encode, string actionName)
        => imageNumbers.Select(imageNumber => new E5ImageBatchReplaceRequest
        {
            ImageNumber = imageNumber,
            SourceBytes = encode.RawBytes,
            SourceBytesAreRaw = true,
            SourceLabel = $"{encode.SourcePath} -> RAW",
            OperationKind = $"一键替换S形象-{actionName}"
        }).ToArray();

    private static string WriteAggregateReport(CczProject project, SImageReplaceResult result)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_SImageRawReplaceReport.json");
        var payload = new
        {
            OperationKind = "S形象一键RAW替换",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ProjectRoot = project.GameRoot,
            result.Request,
            Mapping = new
            {
                result.Mapping.SImageId,
                result.Mapping.JobId,
                result.Mapping.FactionSlot,
                result.Mapping.ImageNumbers,
                result.Mapping.Detail
            },
            result.TotalOperationCount,
            result.Warnings,
            Files = result.Files.Select(file => new
            {
                file.ActionName,
                file.TargetFileName,
                file.TargetPath,
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
                },
                Write = new
                {
                    file.WriteResult.TargetRelativePath,
                    file.WriteResult.OperationCount,
                    file.WriteResult.BackupPath,
                    file.WriteResult.ReportJsonPath,
                    Operations = file.WriteResult.Operations.Select(operation => new
                    {
                        operation.ImageNumber,
                        operation.OldKind,
                        operation.NewKind,
                        operation.OldSizeBytes,
                        operation.NewSizeBytes
                    })
                }
            })
        };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, JsonOptions));
        return reportPath;
    }

    private sealed record SImageFilePlan(
        string ActionName,
        string TargetFileName,
        string TargetPath,
        string SourcePath,
        E5RawImageSpec Spec);
}
