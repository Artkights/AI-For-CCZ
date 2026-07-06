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

    private readonly E5TrueColorImageCodec _codec = new();
    private readonly E5ImageReplaceService _replace = new();

    public SImageReplacePreviewResult Preview(CczProject project, SImageReplaceRequest request)
    {
        var resolvedMapping = ResolveMapping(request);
        var stageTargets = CharacterImageResourceService.ResolveSImageStageTargets(
            resolvedMapping,
            request.StageSlots,
            defaultAllStages: true);
        if (stageTargets.Count == 0)
        {
            throw new InvalidOperationException($"当前 S 形象不包含所选转数：{string.Join(", ", request.StageSlots)}。");
        }

        var mapping = ToSnapshot(resolvedMapping, stageTargets);
        var filePlans = BuildFilePlans(project, request, stageTargets);
        var warnings = new List<string>();
        if (request.SImageId is >= 1 and <= 32 && request.StageSlots.Count == 0 && stageTargets.Count > 1)
        {
            warnings.Add("未指定转数，按旧行为写入该 S 对应的全部三转 Unit 图号。");
        }

        var files = new List<SImageReplaceFilePreview>();
        foreach (var plan in filePlans)
        {
            var encode = _codec.EncodeFile(project, plan.SourcePath, plan.Spec, strictHeight: true);
            warnings.AddRange(encode.Warnings.Select(warning => $"{plan.TargetFileName}: {warning}"));
            var requests = BuildRequests(plan.ImageNumber, encode, plan.ActionName, plan.StageName);
            var preview = _replace.PreviewBatchReplacement(project, plan.TargetPath, requests);
            files.Add(new SImageReplaceFilePreview
            {
                ActionName = plan.ActionName,
                StageSlot = plan.StageSlot,
                StageName = plan.StageName,
                ImageNumber = plan.ImageNumber,
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
            var requests = BuildRequests(file.ImageNumber, file.Encode, file.ActionName, file.StageName);
            var result = _replace.ReplaceBatch(project, file.TargetPath, requests);
            files.Add(new SImageReplaceFileResult
            {
                ActionName = file.ActionName,
                StageSlot = file.StageSlot,
                StageName = file.StageName,
                ImageNumber = file.ImageNumber,
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

    private static SImageMappingSnapshot ToSnapshot(
        SUnitImageMapping mapping,
        IReadOnlyList<SImageStageTarget> stageTargets)
        => new()
        {
            SImageId = mapping.SImageId,
            JobId = mapping.JobId,
            FactionSlot = mapping.FactionSlot,
            ImageNumbers = stageTargets.Select(target => target.ImageNumber).ToArray(),
            StageTargets = stageTargets.ToArray(),
            ShortText = mapping.ShortText,
            Detail = mapping.Detail
        };

    private IReadOnlyList<SImageFilePlan> BuildFilePlans(
        CczProject project,
        SImageReplaceRequest request,
        IReadOnlyList<SImageStageTarget> stageTargets)
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

        var actions = new[]
        {
            new SImageActionPlan("移动", "Unit_mov.e5", CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5"), "mov.bmp", E5RawImageCodec.UnitMovSpec),
            new SImageActionPlan("攻击", "Unit_atk.e5", CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5"), "atk.bmp", E5RawImageCodec.UnitAtkSpec),
            new SImageActionPlan("特技", "Unit_spc.e5", CharacterImageResourceService.ResolveGameFile(project, "Unit_spc.e5"), "spc.bmp", E5RawImageCodec.UnitSpcSpec)
        };
        var plans = new List<SImageFilePlan>();
        foreach (var stageTarget in stageTargets)
        {
            foreach (var action in actions)
            {
                var sourcePath = ResolveMaterialFile(folder, stageTarget.StageSlot, action.SourceFileName);
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    continue;
                }

                plans.Add(new SImageFilePlan(
                    action.ActionName,
                    stageTarget.StageSlot,
                    stageTarget.DisplayName,
                    stageTarget.ImageNumber,
                    action.TargetFileName,
                    action.TargetPath,
                    sourcePath,
                    action.Spec));
            }
        }

        if (plans.Count == 0)
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

    private static string ResolveMaterialFile(string folder, int stageSlot, string fileName)
    {
        var stageFolder = Path.Combine(folder, $"turn{stageSlot.ToString(CultureInfo.InvariantCulture)}");
        var staged = Directory.Exists(stageFolder)
            ? ResolveMaterialFile(stageFolder, fileName)
            : string.Empty;
        return !string.IsNullOrWhiteSpace(staged)
            ? staged
            : ResolveMaterialFile(folder, fileName);
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

    private static IReadOnlyList<E5ImageBatchReplaceRequest> BuildRequests(
        int imageNumber,
        E5TrueColorEncodeResult encode,
        string actionName,
        string stageName)
        => new[]
        {
            new E5ImageBatchReplaceRequest
            {
                ImageNumber = imageNumber,
                SourceBytes = encode.ImageBytes,
                SourceBytesAreRaw = false,
                SourceLabel = $"{encode.SourcePath} -> {encode.StorageFormat}",
                OperationKind = $"一键替换S形象-{stageName}-{actionName}"
            }
        };

    private static string WriteAggregateReport(CczProject project, SImageReplaceResult result)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_SImageTrueColorReplaceReport.json");
        var payload = new
        {
            OperationKind = "S形象一键真彩替换",
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
                file.StageSlot,
                file.StageName,
                file.ImageNumber,
                file.TargetFileName,
                file.TargetPath,
                file.SourcePath,
                Encode = new
                {
                    file.Encode.SourceWidth,
                    file.Encode.SourceHeight,
                    file.Encode.NormalizedWidth,
                    file.Encode.NormalizedHeight,
                    file.Encode.StorageFormat,
                    file.Encode.ColorDepth,
                    file.Encode.ImageLength,
                    file.Encode.TransparentPixels,
                    file.Encode.MagentaKeyPixels,
                    file.Encode.Quantization,
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
        int StageSlot,
        string StageName,
        int ImageNumber,
        string TargetFileName,
        string TargetPath,
        string SourcePath,
        E5RawImageSpec Spec);

    private sealed record SImageActionPlan(
        string ActionName,
        string TargetFileName,
        string TargetPath,
        string SourceFileName,
        E5RawImageSpec Spec);
}
