using System.Globalization;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class BatchStrategyIconImportService
{
    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);

    private readonly IconResourceReplaceService _iconReplace = new();
    private readonly E5ImageReplaceService _e5Replace = new();

    public BatchStrategyIconImportPreviewResult Preview(CczProject project, BatchStrategyIconImportRequest request)
    {
        if (request.SourceFiles.Count == 0 && string.IsNullOrWhiteSpace(request.SourceRoot))
        {
            throw new InvalidOperationException("No strategy icon source files were selected.");
        }

        var targetResource = Ccz66RevisedLayout.ResolveStrategyIconResourceFile(project);
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

        return isE5
            ? PreviewE5(project, request, targetPath, usable, skipped)
            : PreviewDll(project, request, targetPath, usable, skipped);
    }

    public BatchStrategyIconImportResult Replace(CczProject project, BatchStrategyIconImportRequest request)
    {
        var preview = Preview(project, request);
        if (!preview.CanWrite)
        {
            throw new InvalidOperationException("Batch strategy icon import has blocking skipped items.");
        }

        if (preview.Items.Count == 0)
        {
            throw new InvalidOperationException("Batch strategy icon import has no writable items.");
        }

        if (preview.ResourceKind.Equals("E5", StringComparison.OrdinalIgnoreCase))
        {
            var requests = preview.Items.Select(item => new E5ImageBatchReplaceRequest
            {
                ImageNumber = item.IconIndex + 1,
                SourcePath = item.SourcePath,
                SourceLabel = item.SourcePath,
                OperationKind = "batch strategy icon import"
            }).ToArray();
            var result = _e5Replace.ReplaceBatch(project, preview.TargetPath, requests);
            return new BatchStrategyIconImportResult
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
        }

        var dllRequests = preview.Items.Select(item => new IconResourceBatchReplaceRequest
        {
            IconIndex = item.IconIndex,
            SourcePath = item.SourcePath,
            SourceLabel = item.SourcePath,
            OperationKind = "batch strategy icon import"
        }).ToArray();
        var dllResult = _iconReplace.ReplaceBitmapIcons(project, preview.TargetPath, dllRequests);
        return new BatchStrategyIconImportResult
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
    }

    private BatchStrategyIconImportPreviewResult PreviewDll(
        CczProject project,
        BatchStrategyIconImportRequest request,
        string targetPath,
        IReadOnlyList<(BatchStrategyIconTargetRow Target, string SourcePath)> usable,
        IReadOnlyList<BatchImageImportSkippedItem> skipped)
    {
        var dllRequests = usable.Select(item => new IconResourceBatchReplaceRequest
        {
            IconIndex = item.Target.IconIndex,
            SourcePath = item.SourcePath,
            SourceLabel = item.SourcePath,
            OperationKind = "batch strategy icon import"
        }).ToArray();
        var preview = dllRequests.Length == 0 ? null : _iconReplace.PreviewReplaceBitmapIcons(project, targetPath, dllRequests);
        var resourceIdsByIcon = preview?.Items.ToDictionary(item => item.IconIndex, item => item.ResourceIds) ??
                                new Dictionary<int, IReadOnlyList<int>>();
        return new BatchStrategyIconImportPreviewResult
        {
            Request = request,
            TargetPath = Path.GetFullPath(targetPath),
            TargetRelativePath = preview?.TargetRelativePath ?? WriteOperationReportService.ToProjectRelativePath(project, targetPath),
            ResourceKind = "DLL",
            Items = usable.Select(item => new BatchStrategyIconImportItemPreview
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

    private BatchStrategyIconImportPreviewResult PreviewE5(
        CczProject project,
        BatchStrategyIconImportRequest request,
        string targetPath,
        IReadOnlyList<(BatchStrategyIconTargetRow Target, string SourcePath)> usable,
        IReadOnlyList<BatchImageImportSkippedItem> skipped)
    {
        var e5Requests = usable.Select(item => new E5ImageBatchReplaceRequest
        {
            ImageNumber = item.Target.IconIndex + 1,
            SourcePath = item.SourcePath,
            SourceLabel = item.SourcePath,
            OperationKind = "batch strategy icon import"
        }).ToArray();
        var preview = e5Requests.Length == 0 ? null : _e5Replace.PreviewBatchReplacement(project, targetPath, e5Requests);
        return new BatchStrategyIconImportPreviewResult
        {
            Request = request,
            TargetPath = Path.GetFullPath(targetPath),
            TargetRelativePath = preview?.TargetRelativePath ?? WriteOperationReportService.ToProjectRelativePath(project, targetPath),
            ResourceKind = "E5",
            Items = usable.Select(item => new BatchStrategyIconImportItemPreview
            {
                RowId = item.Target.RowId,
                DisplayName = item.Target.DisplayName,
                IconIndex = item.Target.IconIndex,
                SourcePath = item.SourcePath,
                TargetImageNumbers = new[] { item.Target.IconIndex + 1 }
            }).ToArray(),
            SkippedItems = skipped.ToArray(),
            Warnings = preview?.FormatWarnings ?? Array.Empty<string>(),
            E5Preview = preview
        };
    }

    private static IReadOnlyList<(BatchStrategyIconTargetRow Target, string SourcePath)> MatchTargets(
        BatchStrategyIconImportRequest request,
        List<BatchImageImportSkippedItem> skipped)
    {
        var sourceCandidates = BatchImageSourceResolver.Resolve(
            BatchImageSourceKind.StrategyIcon,
            request.SourceFiles,
            request.SourceRoot);
        foreach (var sourceFile in sourceCandidates.Select(candidate => candidate.SourcePath).Where(path => !File.Exists(path)))
        {
            skipped.Add(Skip(Path.GetFileName(sourceFile), sourceFile, BatchImageImportSkipReasons.MissingFile));
        }

        sourceCandidates = sourceCandidates.Where(candidate => File.Exists(candidate.SourcePath)).ToArray();
        var targetRows = request.TargetRows.ToArray();
        var strictRowOrder = request.MatchMode.Equals("selected-row-order", StringComparison.OrdinalIgnoreCase);
        if (strictRowOrder && targetRows.Length > 0 && sourceCandidates.Count == targetRows.Length)
        {
            return targetRows.Zip(sourceCandidates, (row, source) => (row, source.SourcePath)).ToArray();
        }

        if (strictRowOrder && targetRows.Length > 0 && sourceCandidates.Count != targetRows.Length)
        {
            skipped.Add(Skip("selected rows", string.Empty, BatchImageImportSkipReasons.CountMismatch, $"rows={targetRows.Length}, files={sourceCandidates.Count}"));
            return Array.Empty<(BatchStrategyIconTargetRow Target, string SourcePath)>();
        }

        var targetByIcon = targetRows
            .GroupBy(row => row.IconIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var matched = new List<(BatchStrategyIconTargetRow Target, string SourcePath)>();
        if (targetByIcon.Count > 0)
        {
            var unresolved = new List<BatchImageSourceCandidate>();
            foreach (var source in sourceCandidates)
            {
                var iconIndex = source.FieldValue ?? ExtractLastNumber(Path.GetFileNameWithoutExtension(source.SourcePath));
                if (!iconIndex.HasValue)
                {
                    unresolved.Add(source);
                    continue;
                }

                if (!targetByIcon.TryGetValue(iconIndex.Value, out var target))
                {
                    skipped.Add(Skip(iconIndex.Value.ToString(CultureInfo.InvariantCulture), source.SourcePath, BatchImageImportSkipReasons.UnmatchedFile));
                    continue;
                }

                matched.Add((target, source.SourcePath));
            }

            if (matched.Count > 0)
            {
                foreach (var source in unresolved)
                {
                    skipped.Add(Skip(Path.GetFileName(source.SourcePath), source.SourcePath, BatchImageImportSkipReasons.InvalidName));
                }

                return matched;
            }

            if (sourceCandidates.Count == targetRows.Length)
            {
                return targetRows.Zip(sourceCandidates, (row, source) => (row, source.SourcePath)).ToArray();
            }

            skipped.Add(Skip("selected rows", string.Empty, BatchImageImportSkipReasons.CountMismatch, $"rows={targetRows.Length}, files={sourceCandidates.Count}"));
            return Array.Empty<(BatchStrategyIconTargetRow Target, string SourcePath)>();
        }

        foreach (var source in sourceCandidates)
        {
            var iconIndex = source.FieldValue ?? ExtractLastNumber(Path.GetFileNameWithoutExtension(source.SourcePath));
            if (!iconIndex.HasValue)
            {
                skipped.Add(Skip(Path.GetFileName(source.SourcePath), source.SourcePath, BatchImageImportSkipReasons.InvalidName));
                continue;
            }

            var target = new BatchStrategyIconTargetRow(iconIndex.Value, $"strategy icon #{iconIndex.Value}", iconIndex.Value);
            matched.Add((target, source.SourcePath));
        }

        return matched;
    }

    private static void AddDuplicateTargetSkips(
        IReadOnlyList<(BatchStrategyIconTargetRow Target, string SourcePath)> matched,
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
}
