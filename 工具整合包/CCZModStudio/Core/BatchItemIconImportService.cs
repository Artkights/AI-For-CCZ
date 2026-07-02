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
    private readonly ItemIconRasterNormalizeService _itemIconNormalizer = new();
    private readonly DllBitmapIconCodecService _dllIconCodec = new();

    public BatchItemIconImportPreviewResult Preview(CczProject project, BatchItemIconImportRequest request)
    {
        if (request.SourceFiles.Count == 0 && string.IsNullOrWhiteSpace(request.SourceRoot))
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
            var requests = BuildNormalizedE5Requests(preview.Items);
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

        var dllResult = ReplaceDllItems(project, preview);
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
        IReadOnlyList<MatchedItemIconSource> usable,
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
        var resourceIdsByIcon = preview?.Items.ToDictionaryFirstByKey(item => item.IconIndex, item => item.ResourceIds) ??
                                new Dictionary<int, IReadOnlyList<int>>();
        var dllItemsByIcon = preview?.Items.ToDictionaryFirstByKey(item => item.IconIndex, item => item) ??
                             new Dictionary<int, IconResourceBatchReplacePreviewItem>();
        var resources = _dllIconCodec.ParseBitmapResources(targetPath);
        var importDiagnosticsByIcon = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var item in usable)
        {
            var largeSource = string.IsNullOrWhiteSpace(item.LargeSourcePath) ? item.SourcePath : item.LargeSourcePath;
            var smallSource = string.IsNullOrWhiteSpace(item.SmallSourcePath) ? null : item.SmallSourcePath;
            var pair = _dllIconCodec.ResolveBitmapResourcePair(resources, item.Target.IconIndex);
            var classification = _dllIconCodec.ClassifyItemIconBmpImport(largeSource, pair, resources, smallSource);
            importDiagnosticsByIcon[item.Target.IconIndex] = new[] { $"import-mode={classification.Mode}" }
                .Concat(classification.Diagnostics)
                .ToArray();
        }

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
                SmallSourcePath = item.SmallSourcePath,
                LargeSourcePath = item.LargeSourcePath,
                ResourceIds = resourceIdsByIcon.TryGetValue(item.Target.IconIndex, out var resourceIds) ? resourceIds : Array.Empty<int>(),
                SourceWidth = dllItemsByIcon.TryGetValue(item.Target.IconIndex, out var dllItem) ? dllItem.SourceWidth : null,
                SourceHeight = dllItemsByIcon.TryGetValue(item.Target.IconIndex, out var dllItemForHeight) ? dllItemForHeight.SourceHeight : null,
                NormalizeSummary = dllItemsByIcon.TryGetValue(item.Target.IconIndex, out var detailItem)
                    ? string.Join("; ", detailItem.ResourceDetails
                        .Concat(detailItem.FormatWarnings)
                        .Concat(importDiagnosticsByIcon.TryGetValue(item.Target.IconIndex, out var diagnostics) ? diagnostics : Array.Empty<string>()))
                    : string.Empty
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
        IReadOnlyList<MatchedItemIconSource> usable,
        IReadOnlyList<BatchImageImportSkippedItem> skipped)
    {
        var e5Requests = new List<E5ImageBatchReplaceRequest>();
        var previewItems = new List<BatchItemIconImportItemPreview>();
        foreach (var item in usable)
        {
            var (small, large) = Ccz66RevisedLayout.ResolveItemIconImageNumbers(item.Target.IconIndex);
            var normalized = _itemIconNormalizer.NormalizePairFromFile(item.SourcePath);
            e5Requests.AddRange(BuildNormalizedE5Requests(item.SourcePath, small, large, normalized));
            previewItems.Add(new BatchItemIconImportItemPreview
            {
                RowId = item.Target.RowId,
                DisplayName = item.Target.DisplayName,
                IconIndex = item.Target.IconIndex,
                SourcePath = item.SourcePath,
                SourceWidth = normalized.SourceWidth,
                SourceHeight = normalized.SourceHeight,
                SmallWidth = normalized.Small.Width,
                SmallHeight = normalized.Small.Height,
                LargeWidth = normalized.Large.Width,
                LargeHeight = normalized.Large.Height,
                NormalizeSummary = normalized.Summary,
                TargetImageNumbers = new[] { small, large }
            });
        }

        var preview = e5Requests.Count == 0 ? null : _e5Replace.PreviewBatchReplacement(project, targetPath, e5Requests);
        return new BatchItemIconImportPreviewResult
        {
            Request = request,
            TargetPath = Path.GetFullPath(targetPath),
            TargetRelativePath = preview?.TargetRelativePath ?? WriteOperationReportService.ToProjectRelativePath(project, targetPath),
            ResourceKind = "E5",
            Items = previewItems,
            SkippedItems = skipped.ToArray(),
            Warnings = preview?.FormatWarnings ?? Array.Empty<string>(),
            E5Preview = preview
        };
    }

    private IReadOnlyList<E5ImageBatchReplaceRequest> BuildNormalizedE5Requests(IReadOnlyList<BatchItemIconImportItemPreview> items)
    {
        var requests = new List<E5ImageBatchReplaceRequest>();
        foreach (var item in items)
        {
            var (small, large) = item.TargetImageNumbers.Count >= 2
                ? (item.TargetImageNumbers[0], item.TargetImageNumbers[1])
                : Ccz66RevisedLayout.ResolveItemIconImageNumbers(item.IconIndex);
            var normalized = _itemIconNormalizer.NormalizePairFromFile(item.SourcePath);
            requests.AddRange(BuildNormalizedE5Requests(item.SourcePath, small, large, normalized));
        }

        return requests;
    }

    private static IReadOnlyList<E5ImageBatchReplaceRequest> BuildNormalizedE5Requests(
        string sourcePath,
        int smallImageNumber,
        int largeImageNumber,
        ItemIconRasterPair normalized)
        =>
        [
            new E5ImageBatchReplaceRequest
            {
                ImageNumber = smallImageNumber,
                SourceBytes = normalized.Small.BmpBytes,
                SourceLabel = $"{sourcePath} (normalized small 16x16)",
                OperationKind = "batch item icon import small normalized"
            },
            new E5ImageBatchReplaceRequest
            {
                ImageNumber = largeImageNumber,
                SourceBytes = normalized.Large.BmpBytes,
                SourceLabel = $"{sourcePath} (normalized large 32x32)",
                OperationKind = "batch item icon import large normalized"
            }
        ];

    private IReadOnlyList<MatchedItemIconSource> MatchTargets(
        BatchItemIconImportRequest request,
        List<BatchImageImportSkippedItem> skipped)
    {
        var sourceCandidates = BatchImageSourceResolver.Resolve(
            BatchImageSourceKind.ItemIcon,
            request.SourceFiles,
            request.SourceRoot).ToList();
        var pairSources = BatchImageSourceResolver.ResolveItemIconPairs(request.SourceFiles, request.SourceRoot);
        var candidatePaths = new HashSet<string>(sourceCandidates.Select(candidate => candidate.SourcePath), StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairSources.Values.OrderBy(pair => pair.IconIndex))
        {
            if (string.IsNullOrWhiteSpace(pair.LargeSourcePath) ||
                !candidatePaths.Add(pair.LargeSourcePath))
            {
                continue;
            }

            sourceCandidates.Add(new BatchImageSourceCandidate(
                pair.LargeSourcePath,
                pair.IconIndex,
                Path.GetFileName(pair.LargeSourcePath)));
        }

        foreach (var sourceFile in sourceCandidates.Select(candidate => candidate.SourcePath).Where(path => !File.Exists(path)))
        {
            skipped.Add(Skip(Path.GetFileName(sourceFile), sourceFile, BatchImageImportSkipReasons.MissingFile));
        }

        sourceCandidates = sourceCandidates.Where(candidate => File.Exists(candidate.SourcePath)).ToList();
        var targetRows = request.TargetRows.ToArray();
        var strictRowOrder = request.MatchMode.Equals("selected-row-order", StringComparison.OrdinalIgnoreCase);
        var explicitFileSelection = request.SourceFiles.Count > 0 && string.IsNullOrWhiteSpace(request.SourceRoot);
        if (strictRowOrder && targetRows.Length > 0 && sourceCandidates.Count == targetRows.Length)
        {
            return targetRows.Zip(sourceCandidates, (row, source) => CreateMatched(row, source.SourcePath, pairSources)).ToArray();
        }

        if (strictRowOrder && targetRows.Length > 0 && sourceCandidates.Count != targetRows.Length)
        {
            skipped.Add(Skip("selected rows", string.Empty, BatchImageImportSkipReasons.CountMismatch, $"rows={targetRows.Length}, files={sourceCandidates.Count}"));
            return Array.Empty<MatchedItemIconSource>();
        }

        if (explicitFileSelection && targetRows.Length > 0 && sourceCandidates.Count == targetRows.Length)
        {
            var ordered = targetRows.Zip(sourceCandidates, (row, source) => CreateMatched(row, source.SourcePath, pairSources)).ToArray();
            foreach (var (row, source) in targetRows.Zip(sourceCandidates, (row, source) => (row, source)))
            {
                var iconIndex = source.FieldValue ?? ExtractLastNumber(Path.GetFileNameWithoutExtension(source.SourcePath));
                if (iconIndex.HasValue && iconIndex.Value != row.IconIndex)
                {
                    skipped.Add(Skip(
                        row.IconIndex.ToString(CultureInfo.InvariantCulture),
                        source.SourcePath,
                        BatchImageImportSkipReasons.Unused,
                        $"filename icon #{iconIndex.Value} ignored; selected row icon #{row.IconIndex} used"));
                }
            }

            return ordered;
        }

        var targetByIcon = targetRows
            .GroupBy(row => row.IconIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var matched = new List<MatchedItemIconSource>();
        if (targetByIcon.Count > 0)
        {
            var unresolved = new List<BatchImageSourceCandidate>();
            foreach (var source in sourceCandidates)
            {
                var iconIndex = source.FieldValue ?? ExtractLastNumber(Path.GetFileNameWithoutExtension(source.SourcePath));
                if (ShouldSkipSmallPairFile(source.SourcePath, iconIndex, pairSources))
                {
                    continue;
                }

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

                matched.Add(CreateMatched(target, source.SourcePath, pairSources));
            }

            if (matched.Count > 0)
            {
                foreach (var source in unresolved)
                {
                    skipped.Add(Skip(Path.GetFileName(source.SourcePath), source.SourcePath, BatchImageImportSkipReasons.InvalidName));
                }

                return matched;
            }

            skipped.Add(Skip("selected rows", string.Empty, BatchImageImportSkipReasons.CountMismatch, $"rows={targetRows.Length}, files={sourceCandidates.Count}"));
            return Array.Empty<MatchedItemIconSource>();
        }

        foreach (var source in sourceCandidates)
        {
            var iconIndex = source.FieldValue ?? ExtractLastNumber(Path.GetFileNameWithoutExtension(source.SourcePath));
            if (ShouldSkipSmallPairFile(source.SourcePath, iconIndex, pairSources))
            {
                continue;
            }

            if (!iconIndex.HasValue)
            {
                skipped.Add(Skip(Path.GetFileName(source.SourcePath), source.SourcePath, BatchImageImportSkipReasons.InvalidName));
                continue;
            }

            var target = new BatchItemIconTargetRow(iconIndex.Value, $"icon #{iconIndex.Value}", iconIndex.Value);
            matched.Add(CreateMatched(target, source.SourcePath, pairSources));
        }

        return matched;
    }

    private static void AddDuplicateTargetSkips(
        IReadOnlyList<MatchedItemIconSource> matched,
        List<BatchImageImportSkippedItem> skipped)
    {
        foreach (var duplicate in matched.GroupBy(item => item.Target.IconIndex).Where(group => group.Count() > 1))
        {
            skipped.Add(Skip(duplicate.Key.ToString(CultureInfo.InvariantCulture), string.Join("; ", duplicate.Select(item => item.SourcePath)), BatchImageImportSkipReasons.DuplicateTarget));
        }
    }

    private static MatchedItemIconSource CreateMatched(
        BatchItemIconTargetRow target,
        string sourcePath,
        IReadOnlyDictionary<int, BatchItemIconSourcePair> pairSources)
    {
        if (pairSources.TryGetValue(target.IconIndex, out var pair))
        {
            return new MatchedItemIconSource(target, pair.LargeSourcePath, pair.SmallSourcePath, pair.LargeSourcePath);
        }

        return new MatchedItemIconSource(target, sourcePath, string.Empty, sourcePath);
    }

    private static bool ShouldSkipSmallPairFile(
        string sourcePath,
        int? iconIndex,
        IReadOnlyDictionary<int, BatchItemIconSourcePair> pairSources)
        => iconIndex.HasValue &&
           pairSources.TryGetValue(iconIndex.Value, out var pair) &&
           !string.IsNullOrWhiteSpace(pair.SmallSourcePath) &&
           sourcePath.Equals(pair.SmallSourcePath, StringComparison.OrdinalIgnoreCase);

    private IconResourceBatchReplaceResult ReplaceDllItems(CczProject project, BatchItemIconImportPreviewResult preview)
    {
        var resources = _dllIconCodec.ParseBitmapResources(preview.TargetPath);
        var requests = new List<IconResourcePreparedDibReplaceRequest>();
        foreach (var item in preview.Items)
        {
            var largeSource = string.IsNullOrWhiteSpace(item.LargeSourcePath) ? item.SourcePath : item.LargeSourcePath;
            var smallSource = string.IsNullOrWhiteSpace(item.SmallSourcePath) ? null : item.SmallSourcePath;
            var pair = _dllIconCodec.ResolveBitmapResourcePair(resources, item.IconIndex);
            var classification = _dllIconCodec.ClassifyItemIconBmpImport(largeSource, pair, resources, smallSource);

            if (classification.PreserveStorage && classification.StoragePair != null)
            {
                var storageUpdates = _dllIconCodec.BuildCanonical8BppUpdates(pair, classification.StoragePair).ToArray();
                var canonical = storageUpdates.Select(update => (update.ResourceId, update.LanguageId)).ToHashSet();
                requests.Add(new IconResourcePreparedDibReplaceRequest
                {
                    IconIndex = item.IconIndex,
                    SourcePath = item.SourcePath,
                    SourceLabel = item.SourcePath,
                    SourceWidth = classification.StoragePair.SourceWidth,
                    SourceHeight = classification.StoragePair.SourceHeight,
                    OperationKind = "batch item icon import storage-preserved",
                    ResourceFormatSummary = "DLL RT_BITMAP 8bpp indexed storage (storage-preserved)",
                    Diagnostics = classification.Diagnostics
                        .Concat(pair.AllVariants.Count > 2 ? new[] { "duplicate-variant-cleanup: non-canonical language/format variants for this icon will be removed." } : Array.Empty<string>())
                        .ToArray(),
                    Updates = storageUpdates
                        .Select(update => new IconResourcePreparedDibUpdate
                        {
                            ResourceId = update.ResourceId,
                            LanguageId = update.LanguageId,
                            DibBytes = update.DibBytes
                        })
                        .ToArray(),
                    Deletes = pair.AllVariants
                        .Where(resource => !canonical.Contains((resource.Id, resource.LanguageId)))
                        .Select(resource => new IconResourcePreparedDibDelete
                        {
                            ResourceId = resource.Id,
                            LanguageId = resource.LanguageId
                        })
                        .ToArray()
                });
                continue;
            }

            var palette = _dllIconCodec.ResolveStoragePalette(resources, pair);
            using var rasterPair = _dllIconCodec.BuildPairFromSources(largeSource, smallSource);
            using var quantized = palette.Count > 0 ? _dllIconCodec.QuantizePair(rasterPair, palette) : null;
            var effective = quantized ?? rasterPair;
            var updates = _dllIconCodec.BuildUpdates(pair, effective).ToArray();
            requests.Add(new IconResourcePreparedDibReplaceRequest
            {
                IconIndex = item.IconIndex,
                SourcePath = item.SourcePath,
                SourceLabel = item.SourcePath,
                SourceWidth = rasterPair.SourceWidth,
                SourceHeight = rasterPair.SourceHeight,
                OperationKind = "batch item icon import visual-normalized",
                ResourceFormatSummary = $"DLL RT_BITMAP {classification.Mode}; {effective.Summary}",
                Diagnostics = classification.Diagnostics,
                Updates = updates
                    .Select(update => new IconResourcePreparedDibUpdate
                    {
                        ResourceId = update.ResourceId,
                        LanguageId = update.LanguageId,
                        DibBytes = update.DibBytes
                    })
                    .ToArray()
            });
        }

        return _iconReplace.ReplaceBitmapIconsFromPreparedDibs(project, preview.TargetPath, requests);
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

    private sealed record MatchedItemIconSource(
        BatchItemIconTargetRow Target,
        string SourcePath,
        string SmallSourcePath,
        string LargeSourcePath);

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
                item.SourceWidth,
                item.SourceHeight,
                item.SmallWidth,
                item.SmallHeight,
                item.LargeWidth,
                item.LargeHeight,
                item.NormalizeSummary,
                item.TargetImageNumbers,
                item.ResourceIds
            })
        };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, JsonOptions));
        return reportPath;
    }
}
