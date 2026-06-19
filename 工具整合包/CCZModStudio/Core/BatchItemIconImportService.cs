using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class BatchItemIconImportService
{
    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly IconResourceReplaceService _iconReplace = new();
    private readonly E5ImageReplaceService _e5Replace = new();

    public BatchItemIconImportPreviewResult Preview(CczProject project, BatchItemIconImportRequest request)
    {
        if (request.SourceFiles.Count == 0)
        {
            throw new InvalidOperationException("No item icon source files were selected.");
        }

        var targetResource = Ccz66RevisedLayout.ResolveItemIconResourceFile(project);
        var targetPath = Ccz66RevisedLayout.ResolveResourcePath(project, targetResource);
        var isE5 = Ccz66RevisedLayout.IsE5IconResource(targetResource);
        var skipped = new List<BatchImageImportSkippedItem>();
        var matched = MatchTargets(request, skipped);
        AddDuplicateTargetSkips(matched, skipped);
        var usable = matched
            .GroupBy(item => item.Target.IconIndex)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(item => item.Target.IconIndex)
            .ToArray();

        if (isE5)
        {
            return PreviewE5(project, request, targetPath, usable, skipped);
        }

        return PreviewDll(project, request, targetPath, usable, skipped);
    }

    public BatchItemIconImportResult Replace(CczProject project, BatchItemIconImportRequest request)
    {
        var preview = Preview(project, request);
        if (!preview.CanWrite)
        {
            throw new InvalidOperationException("Batch item icon import has blocking skipped items.");
        }

        if (preview.Items.Count == 0)
        {
            throw new InvalidOperationException("Batch item icon import has no writable items.");
        }

        if (preview.ResourceKind.Equals("E5", StringComparison.OrdinalIgnoreCase))
        {
            var requests = preview.Items.SelectMany(item => item.TargetImageNumbers.Select(imageNumber => new E5ImageBatchReplaceRequest
            {
                ImageNumber = imageNumber,
                SourcePath = item.SourcePath,
                SourceLabel = item.SourcePath,
                OperationKind = "batch item icon import"
            })).ToArray();
            var result = _e5Replace.ReplaceBatch(project, preview.TargetPath, requests);
            var payload = new BatchItemIconImportResult
            {
                Request = preview.Request,
                TargetPath = preview.TargetPath,
                TargetRelativePath = preview.TargetRelativePath,
                ResourceKind = preview.ResourceKind,
                Items = preview.Items,
                SkippedItems = preview.SkippedItems,
                Warnings = preview.Warnings,
                E5Preview = preview.E5Preview,
                E5Result = result
            };

            return new BatchItemIconImportResult
            {
                Request = payload.Request,
                TargetPath = payload.TargetPath,
                TargetRelativePath = payload.TargetRelativePath,
                ResourceKind = payload.ResourceKind,
                Items = payload.Items,
                SkippedItems = payload.SkippedItems,
                Warnings = payload.Warnings,
                E5Preview = payload.E5Preview,
                E5Result = payload.E5Result,
                AggregateReportPath = WriteAggregateReport(project, payload)
            };
        }

        var dllRequests = preview.Items.Select(item => new IconResourceBatchReplaceRequest
        {
            IconIndex = item.IconIndex,
            SourcePath = item.SourcePath,
            SourceLabel = item.SourcePath,
            OperationKind = "batch item icon import"
        }).ToArray();
        var dllResult = _iconReplace.ReplaceBitmapIcons(project, preview.TargetPath, dllRequests);
        var dllPayload = new BatchItemIconImportResult
        {
            Request = preview.Request,
            TargetPath = preview.TargetPath,
            TargetRelativePath = preview.TargetRelativePath,
            ResourceKind = preview.ResourceKind,
            Items = preview.Items,
            SkippedItems = preview.SkippedItems,
            Warnings = preview.Warnings,
            DllPreview = preview.DllPreview,
            DllResult = dllResult
        };

        return new BatchItemIconImportResult
        {
            Request = dllPayload.Request,
            TargetPath = dllPayload.TargetPath,
            TargetRelativePath = dllPayload.TargetRelativePath,
            ResourceKind = dllPayload.ResourceKind,
            Items = dllPayload.Items,
            SkippedItems = dllPayload.SkippedItems,
            Warnings = dllPayload.Warnings,
            DllPreview = dllPayload.DllPreview,
            DllResult = dllPayload.DllResult,
            AggregateReportPath = WriteAggregateReport(project, dllPayload)
        };
    }

    private BatchItemIconImportPreviewResult PreviewDll(
        CczProject project,
        BatchItemIconImportRequest request,
        string targetPath,
        IReadOnlyList<(BatchItemIconTargetRow Target, string SourcePath)> usable,
        IReadOnlyList<BatchImageImportSkippedItem> skipped)
    {
        var dllRequests = usable.Select(item => new IconResourceBatchReplaceRequest
        {
            IconIndex = item.Target.IconIndex,
            SourcePath = item.SourcePath,
            SourceLabel = item.SourcePath,
            OperationKind = "batch item icon import"
        }).ToArray();
        var preview = dllRequests.Length == 0 ? null : _iconReplace.PreviewReplaceBitmapIcons(project, targetPath, dllRequests);
        var resourceIdsByIcon = preview?.Items.ToDictionary(item => item.IconIndex, item => item.ResourceIds) ??
                                new Dictionary<int, IReadOnlyList<int>>();
        return new BatchItemIconImportPreviewResult
        {
            Request = request,
            TargetPath = Path.GetFullPath(targetPath),
            TargetRelativePath = preview?.TargetRelativePath ?? WriteOperationReportService.ToProjectRelativePath(project, targetPath),
            ResourceKind = "DLL",
            Items = usable.Select(item => new BatchItemIconImportItemPreview
            {
                RowId = item.Target.RowId,
                DisplayName = item.Target.DisplayName,
                IconIndex = item.Target.IconIndex,
                SourcePath = item.SourcePath,
                ResourceIds = resourceIdsByIcon.TryGetValue(item.Target.IconIndex, out var resourceIds) ? resourceIds : Array.Empty<int>()
            }).ToArray(),
            SkippedItems = skipped.ToArray(),
            Warnings = preview?.FormatWarnings ?? Array.Empty<string>(),
            DllPreview = preview
        };
    }

    private BatchItemIconImportPreviewResult PreviewE5(
        CczProject project,
        BatchItemIconImportRequest request,
        string targetPath,
        IReadOnlyList<(BatchItemIconTargetRow Target, string SourcePath)> usable,
        IReadOnlyList<BatchImageImportSkippedItem> skipped)
    {
        var e5Requests = usable.SelectMany(item =>
        {
            var (small, large) = Ccz66RevisedLayout.ResolveItemIconImageNumbers(item.Target.IconIndex);
            return new[]
            {
                new E5ImageBatchReplaceRequest
                {
                    ImageNumber = small,
                    SourcePath = item.SourcePath,
                    SourceLabel = item.SourcePath,
                    OperationKind = "batch item icon import small"
                },
                new E5ImageBatchReplaceRequest
                {
                    ImageNumber = large,
                    SourcePath = item.SourcePath,
                    SourceLabel = item.SourcePath,
                    OperationKind = "batch item icon import large"
                }
            };
        }).ToArray();
        var preview = e5Requests.Length == 0 ? null : _e5Replace.PreviewBatchReplacement(project, targetPath, e5Requests);
        return new BatchItemIconImportPreviewResult
        {
            Request = request,
            TargetPath = Path.GetFullPath(targetPath),
            TargetRelativePath = preview?.TargetRelativePath ?? WriteOperationReportService.ToProjectRelativePath(project, targetPath),
            ResourceKind = "E5",
            Items = usable.Select(item =>
            {
                var (small, large) = Ccz66RevisedLayout.ResolveItemIconImageNumbers(item.Target.IconIndex);
                return new BatchItemIconImportItemPreview
                {
                    RowId = item.Target.RowId,
                    DisplayName = item.Target.DisplayName,
                    IconIndex = item.Target.IconIndex,
                    SourcePath = item.SourcePath,
                    TargetImageNumbers = new[] { small, large }
                };
            }).ToArray(),
            SkippedItems = skipped.ToArray(),
            Warnings = preview?.FormatWarnings ?? Array.Empty<string>(),
            E5Preview = preview
        };
    }

    private static IReadOnlyList<(BatchItemIconTargetRow Target, string SourcePath)> MatchTargets(
        BatchItemIconImportRequest request,
        List<BatchImageImportSkippedItem> skipped)
    {
        var sourceFiles = request.SourceFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        foreach (var sourceFile in sourceFiles.Where(path => !File.Exists(path)))
        {
            skipped.Add(Skip(Path.GetFileName(sourceFile), sourceFile, BatchImageImportSkipReasons.MissingFile));
        }

        sourceFiles = sourceFiles.Where(File.Exists).ToArray();
        var targetRows = request.TargetRows.ToArray();
        var strictRowOrder = request.MatchMode.Equals("selected-row-order", StringComparison.OrdinalIgnoreCase);
        if (targetRows.Length > 0 && sourceFiles.Length == targetRows.Length)
        {
            return targetRows.Zip(sourceFiles, (row, sourcePath) => (row, sourcePath)).ToArray();
        }

        if (strictRowOrder && targetRows.Length > 0 && sourceFiles.Length != targetRows.Length)
        {
            skipped.Add(Skip("selected rows", string.Empty, BatchImageImportSkipReasons.CountMismatch, $"rows={targetRows.Length}, files={sourceFiles.Length}"));
            return Array.Empty<(BatchItemIconTargetRow Target, string SourcePath)>();
        }

        var targetByIcon = targetRows
            .GroupBy(row => row.IconIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var matched = new List<(BatchItemIconTargetRow Target, string SourcePath)>();
        foreach (var sourceFile in sourceFiles)
        {
            var iconIndex = ExtractLastNumber(Path.GetFileNameWithoutExtension(sourceFile));
            if (!iconIndex.HasValue)
            {
                skipped.Add(Skip(Path.GetFileName(sourceFile), sourceFile, BatchImageImportSkipReasons.InvalidName));
                continue;
            }

            BatchItemIconTargetRow target = default!;
            if (targetByIcon.Count > 0 && !targetByIcon.TryGetValue(iconIndex.Value, out target!))
            {
                skipped.Add(Skip(iconIndex.Value.ToString(CultureInfo.InvariantCulture), sourceFile, BatchImageImportSkipReasons.UnmatchedFile));
                continue;
            }

            if (targetByIcon.Count == 0)
            {
                target = new BatchItemIconTargetRow(iconIndex.Value, $"icon #{iconIndex.Value}", iconIndex.Value);
            }

            matched.Add((target, sourceFile));
        }

        return matched;
    }

    private static void AddDuplicateTargetSkips(
        IReadOnlyList<(BatchItemIconTargetRow Target, string SourcePath)> matched,
        List<BatchImageImportSkippedItem> skipped)
    {
        foreach (var duplicate in matched.GroupBy(item => item.Target.IconIndex).Where(group => group.Count() > 1))
        {
            skipped.Add(Skip(duplicate.Key.ToString(CultureInfo.InvariantCulture), string.Join("; ", duplicate.Select(item => item.SourcePath)), BatchImageImportSkipReasons.DuplicateTarget));
        }
    }

    private static int? ExtractLastNumber(string text)
    {
        var matches = NumberRegex.Matches(text);
        if (matches.Count == 0) return null;
        return int.TryParse(matches[^1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static BatchImageImportSkippedItem Skip(string key, string sourcePath, string reason, string detail = "")
        => new()
        {
            Key = key,
            SourcePath = sourcePath,
            Reason = string.IsNullOrWhiteSpace(detail) ? reason : $"{reason}: {detail}"
        };

    private static string WriteAggregateReport(CczProject project, BatchItemIconImportResult result)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_BatchItemIconImportReport.json");
        var payload = new
        {
            OperationKind = "Batch item icon import",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ProjectRoot = project.GameRoot,
            result.TargetRelativePath,
            result.ResourceKind,
            result.TotalOperationCount,
            result.Warnings,
            result.SkippedItems,
            DllTarget = result.DllResult == null ? null : new
            {
                result.DllResult.OperationKind,
                Count = result.DllResult.Items.Count,
                result.DllResult.BackupPath,
                result.DllResult.ReportPath,
                result.DllResult.ReportJsonPath
            },
            E5Target = result.E5Result == null ? null : new
            {
                result.E5Result.TargetRelativePath,
                result.E5Result.OperationCount,
                result.E5Result.BackupPath,
                result.E5Result.ReportPath,
                result.E5Result.ReportJsonPath
            },
            Items = result.Items.Select(item => new
            {
                item.RowId,
                item.DisplayName,
                item.IconIndex,
                item.SourcePath,
                item.TargetImageNumbers,
                item.ResourceIds
            })
        };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, JsonOptions));
        return reportPath;
    }
}
