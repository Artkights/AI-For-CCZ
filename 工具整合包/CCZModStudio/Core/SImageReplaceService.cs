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
    private readonly SImageMaterialLayoutResolver _layoutResolver = new();

    public SImageReplacePreviewResult Preview(CczProject project, SImageReplaceRequest request)
    {
        var resolvedMapping = ResolveMapping(project, request);
        var stageTargets = CharacterImageResourceService.ResolveSImageStageTargets(
            project,
            resolvedMapping,
            request.StageSlots,
            defaultAllStages: true);
        if (stageTargets.Count == 0)
        {
            throw new InvalidOperationException($"Current S image does not contain selected stages: {string.Join(", ", request.StageSlots)}.");
        }

        var mapping = ToSnapshot(resolvedMapping, stageTargets);
        var filePlans = BuildFilePlans(project, request, stageTargets);
        var warnings = new List<string>();
        if (CharacterImageResourceService.GetAvailableSImageStageSlots(project, request.SImageId).Count > 1 &&
            request.StageSlots.Count == 0 &&
            stageTargets.Count > 1)
        {
            warnings.Add("No stage was selected, so all available S image stages will be written.");
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

    private static SUnitImageMapping ResolveMapping(CczProject project, SImageReplaceRequest request)
    {
        if (request.SImageId == 0 && (!request.JobId.HasValue || request.JobId.Value < 0))
        {
            throw new InvalidOperationException("S=0 must be imported from a selected character with a valid job id.");
        }

        var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(
            project,
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
            throw new InvalidOperationException("Missing S image material folder.");
        }

        var folder = Path.GetFullPath(request.MaterialFolder);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"S image material folder not found: {folder}");
        }

        var layout = _layoutResolver.Resolve(folder, stageTargets);
        var missing = layout.StageFiles
            .Where(stage => !stage.IsComplete)
            .Select(stage => $"{stage.StageName}: {string.Join(", ", stage.MissingFiles)}")
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException("S image material is missing required files: " + string.Join("; ", missing));
        }

        var plans = new List<SImageFilePlan>();
        foreach (var stageFiles in layout.StageFiles)
        {
            var stageTarget = stageTargets.Single(target => target.StageSlot == stageFiles.StageSlot);
            AddFilePlan(plans, project, stageTarget, "mov", "Unit_mov.e5", stageFiles.MovPath, E5RawImageCodec.UnitMovSpec);
            AddFilePlan(plans, project, stageTarget, "atk", "Unit_atk.e5", stageFiles.AtkPath, E5RawImageCodec.UnitAtkSpec);
            AddFilePlan(plans, project, stageTarget, "spc", "Unit_spc.e5", stageFiles.SpcPath, E5RawImageCodec.UnitSpcSpec);
        }

        if (plans.Count == 0)
        {
            throw new InvalidOperationException("No importable S image materials were found.");
        }

        foreach (var plan in plans)
        {
            if (!File.Exists(plan.TargetPath))
            {
                throw new FileNotFoundException($"Target E5 file not found: {plan.TargetFileName}", plan.TargetPath);
            }
        }

        return plans;
    }

    private static void AddFilePlan(
        List<SImageFilePlan> plans,
        CczProject project,
        SImageStageTarget stageTarget,
        string actionName,
        string targetFileName,
        string sourcePath,
        E5RawImageSpec spec)
    {
        plans.Add(new SImageFilePlan(
            actionName,
            stageTarget.StageSlot,
            stageTarget.DisplayName,
            stageTarget.ImageNumber,
            targetFileName,
            CharacterImageResourceService.ResolveGameFile(project, targetFileName),
            sourcePath,
            spec));
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
                OperationKind = $"S image import {stageName} {actionName}"
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
            OperationKind = "S image true-color replace",
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
}
