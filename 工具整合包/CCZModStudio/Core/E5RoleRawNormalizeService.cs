using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class E5RoleRawNormalizeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly E5RawImageCodec _codec = new();
    private readonly E5ImageReplaceService _replace = new();

    public E5RoleRawNormalizePreviewResult Preview(CczProject project)
    {
        var files = new List<E5RoleRawNormalizeFilePreview>();
        var warnings = new List<string>();
        foreach (var filePlan in BuildFilePlans(project))
        {
            if (!File.Exists(filePlan.TargetPath))
            {
                warnings.Add($"跳过 {filePlan.FileName}: 文件不存在。");
                continue;
            }

            var entries = BuildEntryPreviews(project, filePlan);
            var requests = BuildRequests(entries);
            E5ImageBatchReplacePreviewResult? batchPreview = null;
            if (requests.Count > 0)
            {
                batchPreview = _replace.PreviewBatchReplacement(project, filePlan.TargetPath, requests);
            }

            files.Add(new E5RoleRawNormalizeFilePreview
            {
                TargetFileName = filePlan.FileName,
                TargetPath = filePlan.TargetPath,
                Entries = entries,
                BatchPreview = batchPreview
            });
        }

        return new E5RoleRawNormalizePreviewResult
        {
            Files = files,
            Warnings = warnings
        };
    }

    public E5RoleRawNormalizeResult Normalize(CczProject project)
    {
        var preview = Preview(project);
        var files = new List<E5RoleRawNormalizeFileResult>();
        foreach (var filePreview in preview.Files)
        {
            var requests = BuildRequests(filePreview.Entries);
            E5ImageBatchReplaceResult? result = null;
            if (requests.Count > 0)
            {
                result = _replace.ReplaceBatch(project, filePreview.TargetPath, requests);
            }

            files.Add(new E5RoleRawNormalizeFileResult
            {
                TargetFileName = filePreview.TargetFileName,
                TargetPath = filePreview.TargetPath,
                Entries = filePreview.Entries,
                WriteResult = result
            });
        }

        var payload = new E5RoleRawNormalizeResult
        {
            Files = files,
            Warnings = preview.Warnings
        };

        return new E5RoleRawNormalizeResult
        {
            Files = payload.Files,
            Warnings = payload.Warnings,
            AggregateReportPath = WriteAggregateReport(project, payload)
        };
    }

    private IReadOnlyList<E5RoleRawNormalizeEntryPreview> BuildEntryPreviews(CczProject project, E5RoleRawNormalizeFilePlan filePlan)
    {
        var result = new List<E5RoleRawNormalizeEntryPreview>();
        var entries = _replace.ReadIndex(filePlan.TargetPath);
        foreach (var entry in entries)
        {
            if (entry.Kind.Equals("RAW", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(BuildSkip(filePlan, entry, "已经是 RAW。"));
                continue;
            }

            if (entry.IsCompressed || entry.Kind.Equals("LS12", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(BuildSkip(filePlan, entry, "压缩条目暂不自动改写。"));
                continue;
            }

            if (!entry.Kind.Equals("BMP", StringComparison.OrdinalIgnoreCase) &&
                !entry.Kind.Equals("JPG", StringComparison.OrdinalIgnoreCase) &&
                !entry.Kind.Equals("PNG", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(BuildSkip(filePlan, entry, $"不是可解码标准图片格式：{entry.Kind}。"));
                continue;
            }

            var bytes = _replace.ReadEntryBytes(filePlan.TargetPath, entry.ImageNumber);
            if (!_codec.TryDecodeStandardImage(bytes, out var width, out var height))
            {
                result.Add(BuildSkip(filePlan, entry, "标准图片解码失败。"));
                continue;
            }

            if (width != filePlan.Spec.Width || height % filePlan.Spec.FrameHeight != 0)
            {
                result.Add(BuildSkip(filePlan, entry, $"尺寸 {width}x{height} 不符合 {filePlan.Spec.FileName} RAW 宽度/帧高规则。"));
                continue;
            }

            try
            {
                var encode = _codec.EncodeEntryBytes(
                    project,
                    bytes,
                    $"{filePlan.FileName}#{entry.ImageNumber}",
                    filePlan.Spec,
                    strictHeight: false);
                result.Add(new E5RoleRawNormalizeEntryPreview
                {
                    TargetFileName = filePlan.FileName,
                    TargetPath = filePlan.TargetPath,
                    ImageNumber = entry.ImageNumber,
                    OldKind = entry.Kind,
                    OldSizeBytes = entry.StoredLength,
                    Status = "convert",
                    Reason = "标准图片可按角色 RAW 规格转换。",
                    Encode = encode
                });
            }
            catch (Exception ex)
            {
                result.Add(BuildSkip(filePlan, entry, ex.Message));
            }
        }

        return result;
    }

    private static E5RoleRawNormalizeEntryPreview BuildSkip(E5RoleRawNormalizeFilePlan filePlan, E5ImageEntryInfo entry, string reason)
        => new()
        {
            TargetFileName = filePlan.FileName,
            TargetPath = filePlan.TargetPath,
            ImageNumber = entry.ImageNumber,
            OldKind = entry.Kind,
            OldSizeBytes = entry.StoredLength,
            Status = "skip",
            Reason = reason
        };

    private static IReadOnlyList<E5ImageBatchReplaceRequest> BuildRequests(IReadOnlyList<E5RoleRawNormalizeEntryPreview> entries)
        => entries
            .Where(entry => entry.Status.Equals("convert", StringComparison.OrdinalIgnoreCase) && entry.Encode != null)
            .Select(entry => new E5ImageBatchReplaceRequest
            {
                ImageNumber = entry.ImageNumber,
                SourceBytes = entry.Encode!.RawBytes,
                SourceBytesAreRaw = true,
                SourceLabel = $"{entry.TargetFileName}#{entry.ImageNumber} -> RAW",
                OperationKind = "角色图片统一RAW"
            })
            .ToArray();

    private static IReadOnlyList<E5RoleRawNormalizeFilePlan> BuildFilePlans(CczProject project)
        =>
        [
            new("Pmapobj.e5", CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5"), E5RawImageCodec.PmapobjSpec),
            new("Unit_mov.e5", CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5"), E5RawImageCodec.UnitMovSpec),
            new("Unit_atk.e5", CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5"), E5RawImageCodec.UnitAtkSpec),
            new("Unit_spc.e5", CharacterImageResourceService.ResolveGameFile(project, "Unit_spc.e5"), E5RawImageCodec.UnitSpcSpec)
        ];

    private static string WriteAggregateReport(CczProject project, E5RoleRawNormalizeResult result)
    {
        var backupRoot = ProjectBackupPathService.GetBackupRoot(project);
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_E5RoleRawNormalizeReport.json");
        var payload = new
        {
            OperationKind = "角色图片统一RAW",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ProjectRoot = project.GameRoot,
            result.ConvertCount,
            result.SkipCount,
            result.Warnings,
            Files = result.Files.Select(file => new
            {
                file.TargetFileName,
                file.TargetPath,
                file.ConvertCount,
                file.SkipCount,
                Write = file.WriteResult == null
                    ? null
                    : new
                    {
                        file.WriteResult.TargetRelativePath,
                        file.WriteResult.OperationCount,
                        file.WriteResult.BackupPath,
                        file.WriteResult.ReportJsonPath
                    },
                Entries = file.Entries.Select(entry => new
                {
                    entry.ImageNumber,
                    entry.OldKind,
                    entry.OldSizeBytes,
                    entry.Status,
                    entry.Reason,
                    Encode = entry.Encode == null
                        ? null
                        : new
                        {
                            entry.Encode.SourceWidth,
                            entry.Encode.SourceHeight,
                            entry.Encode.RawLength,
                            entry.Encode.TransparentPixels,
                            entry.Encode.ExactPalettePixels,
                            entry.Encode.NearestPalettePixels,
                            entry.Encode.Warnings
                        }
                })
            })
        };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, JsonOptions));
        return reportPath;
    }

    private sealed record E5RoleRawNormalizeFilePlan(
        string FileName,
        string TargetPath,
        E5RawImageSpec Spec);
}
