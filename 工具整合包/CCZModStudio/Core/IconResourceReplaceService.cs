using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class IconResourceReplaceService
{
    private const int RtBitmap = 2;
    private readonly WriteOperationReportService _reportService = new();
    private readonly DllBitmapIconCodecService _dllCodec = new();

    public IconResourceReplacePreviewResult PreviewReplaceBitmapIcon(CczProject project, string targetPath, int iconIndex, string sourcePath)
    {
        var target = Path.GetFullPath(targetPath);
        var source = Path.GetFullPath(sourcePath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("Target DLL file was not found.", target);
        if (!File.Exists(source)) throw new FileNotFoundException("Source image file was not found.", source);

        var resources = _dllCodec.ParseBitmapResources(target);
        var pair = _dllCodec.ResolveBitmapResourcePair(resources, iconIndex);
        var sourceInfo = ReadSourceImageInfo(source);
        var oldBytes = File.ReadAllBytes(target);
        var sourceBytes = File.ReadAllBytes(source);
        var warnings = BuildReplaceWarnings(pair, sourceInfo);
        return new IconResourceReplacePreviewResult
        {
            TargetPath = target,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, target),
            IconIndex = iconIndex,
            ResourceIds = pair.ResourceIds,
            ResourceDetails = pair.VariantSummaries,
            SourcePath = source,
            OperationKind = "replace RT_BITMAP icon",
            OldFileSizeBytes = oldBytes.LongLength,
            SourceSizeBytes = sourceBytes.LongLength,
            OldFileSha256 = WriteOperationReportService.ComputeSha256(oldBytes),
            SourceSha256 = WriteOperationReportService.ComputeSha256(sourceBytes),
            SourceWidth = sourceInfo.Width,
            SourceHeight = sourceInfo.Height,
            ResourceFormat = "DLL RT_BITMAP",
            FormatWarnings = warnings,
            RiskSummary = BuildReplaceRiskSummary(pair, warnings)
        };
    }

    public IconResourceReplaceResult ReplaceBitmapIcon(CczProject project, string targetPath, int iconIndex, string sourcePath)
    {
        var preview = PreviewReplaceBitmapIcon(project, targetPath, iconIndex, sourcePath);
        var resources = _dllCodec.ParseBitmapResources(preview.TargetPath);
        var pair = _dllCodec.ResolveBitmapResourcePair(resources, iconIndex);
        var updates = BuildBitmapUpdatesFromImage(sourcePath, pair);
        return ApplyBitmapUpdates(project, preview, updates);
    }

    public IconResourceBatchReplacePreviewResult PreviewReplaceBitmapIcons(
        CczProject project,
        string targetPath,
        IReadOnlyList<IconResourceBatchReplaceRequest> requests)
    {
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("No DLL icons were provided.");
        }

        var target = Path.GetFullPath(targetPath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("Target DLL file was not found.", target);

        var duplicateIcon = requests
            .GroupBy(request => request.IconIndex)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateIcon != null)
        {
            throw new InvalidOperationException($"Batch import contains duplicate icon index {duplicateIcon.Key}.");
        }

        var resources = _dllCodec.ParseBitmapResources(target);
        var oldBytes = File.ReadAllBytes(target);
        var items = new List<IconResourceBatchReplacePreviewItem>();
        var warnings = new List<string>();
        foreach (var request in requests.OrderBy(request => request.IconIndex))
        {
            var source = Path.GetFullPath(request.SourcePath);
            if (!File.Exists(source)) throw new FileNotFoundException("Source image file was not found.", source);

            var pair = _dllCodec.ResolveBitmapResourcePair(resources, request.IconIndex);
            var sourceInfo = ReadSourceImageInfo(source);
            var sourceBytes = File.ReadAllBytes(source);
            var itemWarnings = BuildReplaceWarnings(pair, sourceInfo);
            warnings.AddRange(itemWarnings.Select(warning => $"icon #{request.IconIndex}: {warning}"));
            items.Add(new IconResourceBatchReplacePreviewItem
            {
                IconIndex = request.IconIndex,
                ResourceIds = pair.ResourceIds,
                ResourceDetails = pair.VariantSummaries,
                SourcePath = source,
                SourceLabel = string.IsNullOrWhiteSpace(request.SourceLabel) ? source : request.SourceLabel,
                SourceSizeBytes = sourceBytes.LongLength,
                SourceSha256 = WriteOperationReportService.ComputeSha256(sourceBytes),
                SourceWidth = sourceInfo.Width,
                SourceHeight = sourceInfo.Height,
                FormatWarnings = itemWarnings
            });
        }

        return new IconResourceBatchReplacePreviewResult
        {
            TargetPath = target,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, target),
            Requests = requests.ToArray(),
            Items = items,
            OperationKind = requests.Select(request => request.OperationKind).FirstOrDefault(kind => !string.IsNullOrWhiteSpace(kind)) ?? "batch replace RT_BITMAP icons",
            OldFileSizeBytes = oldBytes.LongLength,
            OldFileSha256 = WriteOperationReportService.ComputeSha256(oldBytes),
            ResourceFormat = "DLL RT_BITMAP",
            FormatWarnings = warnings,
            RiskSummary = BuildBatchReplaceRiskSummary(items, warnings)
        };
    }

    public IconResourceBatchReplaceResult ReplaceBitmapIcons(
        CczProject project,
        string targetPath,
        IReadOnlyList<IconResourceBatchReplaceRequest> requests)
    {
        var preview = PreviewReplaceBitmapIcons(project, targetPath, requests);
        var resources = _dllCodec.ParseBitmapResources(preview.TargetPath);
        var updates = new List<BitmapResourceUpdate>();
        foreach (var request in requests.OrderBy(request => request.IconIndex))
        {
            var pair = _dllCodec.ResolveBitmapResourcePair(resources, request.IconIndex);
            updates.AddRange(BuildBitmapUpdatesFromImage(request.SourcePath, pair));
        }

        return ApplyBatchBitmapUpdates(project, preview, updates);
    }

    public IconResourceBatchReplacePreviewResult PreviewReplaceBitmapIconsFromBitmaps(
        CczProject project,
        string targetPath,
        IReadOnlyList<IconResourceBitmapReplaceRequest> requests)
    {
        var fileRequests = MaterializeBitmapRequests(requests);
        try
        {
            return PreviewReplaceBitmapIcons(project, targetPath, fileRequests);
        }
        finally
        {
            DeleteTempRequestFiles(fileRequests);
        }
    }

    public IconResourceBatchReplaceResult ReplaceBitmapIconsFromBitmaps(
        CczProject project,
        string targetPath,
        IReadOnlyList<IconResourceBitmapReplaceRequest> requests)
    {
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("No DLL icons were provided.");
        }

        var target = Path.GetFullPath(targetPath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("Target DLL file was not found.", target);

        var duplicateIcon = requests
            .GroupBy(request => request.IconIndex)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateIcon != null)
        {
            throw new InvalidOperationException($"Batch import contains duplicate icon index {duplicateIcon.Key}.");
        }

        var resources = _dllCodec.ParseBitmapResources(target);
        var oldBytes = File.ReadAllBytes(target);
        var items = new List<IconResourceBatchReplacePreviewItem>();
        var warnings = new List<string>();
        var updates = new List<BitmapResourceUpdate>();
        foreach (var request in requests.OrderBy(request => request.IconIndex))
        {
            var pair = _dllCodec.ResolveBitmapResourcePair(resources, request.IconIndex);
            var sourceInfo = new SourceImageInfo(request.Bitmap.Width, request.Bitmap.Height);
            var itemWarnings = BuildReplaceWarnings(pair, sourceInfo);
            warnings.AddRange(itemWarnings.Select(warning => $"icon #{request.IconIndex}: {warning}"));
            var sourceLabel = string.IsNullOrWhiteSpace(request.SourceLabel) ? "<pixel editor>" : request.SourceLabel;
            var sourceBytes = EncodeBitmapToPngBytes(request.Bitmap);
            items.Add(new IconResourceBatchReplacePreviewItem
            {
                IconIndex = request.IconIndex,
                ResourceIds = pair.ResourceIds,
                ResourceDetails = pair.VariantSummaries,
                SourcePath = sourceLabel,
                SourceLabel = sourceLabel,
                SourceSizeBytes = sourceBytes.LongLength,
                SourceSha256 = WriteOperationReportService.ComputeSha256(sourceBytes),
                SourceWidth = sourceInfo.Width,
                SourceHeight = sourceInfo.Height,
                FormatWarnings = itemWarnings
            });

            using var rasterPair = request.SmallBitmap == null
                ? _dllCodec.NormalizePair(request.Bitmap, request.SourceLabel)
                : _dllCodec.BuildPairFromBitmaps(request.Bitmap, request.SmallBitmap, request.SourceLabel);
            updates.AddRange(_dllCodec.BuildUpdates(pair, rasterPair)
                .Select(update => new BitmapResourceUpdate(update.ResourceId, update.LanguageId, update.DibBytes)));
        }

        var preview = new IconResourceBatchReplacePreviewResult
        {
            TargetPath = target,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, target),
            Requests = requests.Select(request => new IconResourceBatchReplaceRequest
            {
                IconIndex = request.IconIndex,
                SourcePath = string.IsNullOrWhiteSpace(request.SourceLabel) ? "<pixel editor>" : request.SourceLabel,
                SourceLabel = request.SourceLabel,
                OperationKind = request.OperationKind
            }).ToArray(),
            Items = items,
            OperationKind = requests.Select(request => request.OperationKind).FirstOrDefault(kind => !string.IsNullOrWhiteSpace(kind)) ?? "batch replace RT_BITMAP icons",
            OldFileSizeBytes = oldBytes.LongLength,
            OldFileSha256 = WriteOperationReportService.ComputeSha256(oldBytes),
            ResourceFormat = "DLL RT_BITMAP",
            FormatWarnings = warnings,
            RiskSummary = BuildBatchReplaceRiskSummary(items, warnings)
        };

        return ApplyBatchBitmapUpdates(project, preview, updates);
    }

    public IconResourceBatchReplaceResult ReplaceItemBitmapIconsFromStorage(
        CczProject project,
        string targetPath,
        IReadOnlyList<IconResourceStorageReplaceRequest> requests)
    {
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("No storage item icons were provided.");
        }

        var target = Path.GetFullPath(targetPath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("Target DLL file was not found.", target);

        var duplicateIcon = requests
            .GroupBy(request => request.IconIndex)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateIcon != null)
        {
            throw new InvalidOperationException($"Batch item icon import contains duplicate icon index {duplicateIcon.Key}.");
        }

        var resources = _dllCodec.ParseBitmapResources(target);
        var oldBytes = File.ReadAllBytes(target);
        var items = new List<IconResourceBatchReplacePreviewItem>();
        var warnings = new List<string>();
        var updates = new List<BitmapResourceUpdate>();
        var deletes = new List<BitmapResourceDelete>();
        foreach (var request in requests.OrderBy(request => request.IconIndex))
        {
            var pair = _dllCodec.ResolveBitmapResourcePair(resources, request.IconIndex);
            var itemWarnings = BuildReplaceWarnings(pair, new SourceImageInfo(request.SourceWidth, request.SourceHeight)).ToList();
            if (!string.IsNullOrWhiteSpace(request.StorageSummary))
            {
                itemWarnings.Add(request.StorageSummary);
            }

            warnings.AddRange(itemWarnings.Select(warning => $"icon #{request.IconIndex}: {warning}"));

            items.Add(new IconResourceBatchReplacePreviewItem
            {
                IconIndex = request.IconIndex,
                ResourceIds = pair.ResourceIds.Count == 0 ? new[] { pair.SmallId, pair.LargeId } : pair.ResourceIds,
                ResourceDetails = pair.VariantSummaries,
                SourcePath = request.SourcePath,
                SourceLabel = string.IsNullOrWhiteSpace(request.SourceLabel) ? request.SourcePath : request.SourceLabel,
                SourceSizeBytes = File.Exists(request.SourcePath) ? new FileInfo(request.SourcePath).Length : 0,
                SourceSha256 = File.Exists(request.SourcePath) ? WriteOperationReportService.ComputeSha256(File.ReadAllBytes(request.SourcePath)) : string.Empty,
                SourceWidth = request.SourceWidth,
                SourceHeight = request.SourceHeight,
                FormatWarnings = itemWarnings
            });

            var storagePair = new DllIconStoragePair(
                request.SourceLabel,
                request.SourceWidth,
                request.SourceHeight,
                new DllIconStorageImage("small", DllBitmapIconCodecService.SmallIconSize, DllBitmapIconCodecService.SmallIconSize, request.SmallDibBytes, Array.Empty<Color>(), DllBitmapIconCodecService.SmallIconSize, DllBitmapIconCodecService.SmallIconSize),
                new DllIconStorageImage("large", DllBitmapIconCodecService.LargeIconSize, DllBitmapIconCodecService.LargeIconSize, request.LargeDibBytes, Array.Empty<Color>(), DllBitmapIconCodecService.LargeIconSize, DllBitmapIconCodecService.LargeIconSize),
                request.StorageSummary);
            var canonicalUpdates = _dllCodec.BuildCanonical8BppUpdates(pair, storagePair).ToArray();
            updates.AddRange(canonicalUpdates.Select(update => new BitmapResourceUpdate(update.ResourceId, update.LanguageId, update.DibBytes)));

            var canonical = canonicalUpdates
                .Select(update => (update.ResourceId, update.LanguageId))
                .ToHashSet();
            deletes.AddRange(pair.AllVariants
                .Where(resource => !canonical.Contains((resource.Id, resource.LanguageId)))
                .Select(resource => new BitmapResourceDelete(resource.Id, resource.LanguageId)));
        }

        var preview = new IconResourceBatchReplacePreviewResult
        {
            TargetPath = target,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, target),
            Requests = requests.Select(request => new IconResourceBatchReplaceRequest
            {
                IconIndex = request.IconIndex,
                SourcePath = request.SourcePath,
                SourceLabel = request.SourceLabel,
                OperationKind = request.OperationKind
            }).ToArray(),
            Items = items,
            OperationKind = requests.Select(request => request.OperationKind).FirstOrDefault(kind => !string.IsNullOrWhiteSpace(kind)) ?? "batch item icon import",
            OldFileSizeBytes = oldBytes.LongLength,
            OldFileSha256 = WriteOperationReportService.ComputeSha256(oldBytes),
            ResourceFormat = "DLL RT_BITMAP 8bpp indexed storage",
            FormatWarnings = warnings,
            RiskSummary = BuildBatchReplaceRiskSummary(items, warnings)
        };

        return ApplyBatchBitmapUpdates(project, preview, updates, deletes);
    }

    public IconResourceBatchReplaceResult ReplaceBitmapIconsFromPreparedDibs(
        CczProject project,
        string targetPath,
        IReadOnlyList<IconResourcePreparedDibReplaceRequest> requests)
    {
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("No prepared DLL icon updates were provided.");
        }

        var target = Path.GetFullPath(targetPath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("Target DLL file was not found.", target);

        var duplicateIcon = requests
            .GroupBy(request => request.IconIndex)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateIcon != null)
        {
            throw new InvalidOperationException($"Prepared DLL icon import contains duplicate icon index {duplicateIcon.Key}.");
        }

        var resources = _dllCodec.ParseBitmapResources(target);
        var oldBytes = File.ReadAllBytes(target);
        var items = new List<IconResourceBatchReplacePreviewItem>();
        var warnings = new List<string>();
        var updates = new List<BitmapResourceUpdate>();
        var deletes = new List<BitmapResourceDelete>();
        foreach (var request in requests.OrderBy(request => request.IconIndex))
        {
            var pair = _dllCodec.ResolveBitmapResourcePair(resources, request.IconIndex);
            var itemWarnings = BuildReplaceWarnings(pair, new SourceImageInfo(request.SourceWidth, request.SourceHeight)).ToList();
            itemWarnings.AddRange(request.Diagnostics.Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic)));
            if (!string.IsNullOrWhiteSpace(request.ResourceFormatSummary))
            {
                itemWarnings.Add(request.ResourceFormatSummary);
            }

            warnings.AddRange(itemWarnings.Select(warning => $"icon #{request.IconIndex}: {warning}"));

            items.Add(new IconResourceBatchReplacePreviewItem
            {
                IconIndex = request.IconIndex,
                ResourceIds = pair.ResourceIds.Count == 0 ? new[] { pair.SmallId, pair.LargeId } : pair.ResourceIds,
                ResourceDetails = pair.VariantSummaries,
                SourcePath = request.SourcePath,
                SourceLabel = string.IsNullOrWhiteSpace(request.SourceLabel) ? request.SourcePath : request.SourceLabel,
                SourceSizeBytes = File.Exists(request.SourcePath) ? new FileInfo(request.SourcePath).Length : 0,
                SourceSha256 = File.Exists(request.SourcePath) ? WriteOperationReportService.ComputeSha256(File.ReadAllBytes(request.SourcePath)) : string.Empty,
                SourceWidth = request.SourceWidth,
                SourceHeight = request.SourceHeight,
                FormatWarnings = itemWarnings
            });

            updates.AddRange(request.Updates
                .Select(update => new BitmapResourceUpdate(update.ResourceId, update.LanguageId, update.DibBytes)));
            deletes.AddRange(request.Deletes
                .Select(delete => new BitmapResourceDelete(delete.ResourceId, delete.LanguageId)));
        }

        var preview = new IconResourceBatchReplacePreviewResult
        {
            TargetPath = target,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, target),
            Requests = requests.Select(request => new IconResourceBatchReplaceRequest
            {
                IconIndex = request.IconIndex,
                SourcePath = request.SourcePath,
                SourceLabel = request.SourceLabel,
                OperationKind = request.OperationKind
            }).ToArray(),
            Items = items,
            OperationKind = requests.Select(request => request.OperationKind).FirstOrDefault(kind => !string.IsNullOrWhiteSpace(kind)) ?? "prepared DLL icon import",
            OldFileSizeBytes = oldBytes.LongLength,
            OldFileSha256 = WriteOperationReportService.ComputeSha256(oldBytes),
            ResourceFormat = string.Join("; ", requests.Select(request => request.ResourceFormatSummary).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)),
            FormatWarnings = warnings,
            RiskSummary = BuildBatchReplaceRiskSummary(items, warnings)
        };

        return ApplyBatchBitmapUpdates(project, preview, updates, deletes);
    }

    public IconResourceReplacePreviewResult PreviewClearBitmapIcon(CczProject project, string targetPath, int iconIndex)
    {
        var target = Path.GetFullPath(targetPath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("Target DLL file was not found.", target);

        var resources = _dllCodec.ParseBitmapResources(target);
        var pair = _dllCodec.ResolveBitmapResourcePair(resources, iconIndex);
        var oldBytes = File.ReadAllBytes(target);
        return new IconResourceReplacePreviewResult
        {
            TargetPath = target,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, target),
            IconIndex = iconIndex,
            ResourceIds = pair.ResourceIds,
            ResourceDetails = pair.VariantSummaries,
            SourcePath = "<transparent>",
            OperationKind = "clear RT_BITMAP icon",
            OldFileSizeBytes = oldBytes.LongLength,
            SourceSizeBytes = 0,
            OldFileSha256 = WriteOperationReportService.ComputeSha256(oldBytes),
            SourceSha256 = string.Empty,
            ResourceFormat = "DLL RT_BITMAP",
            FormatWarnings = Array.Empty<string>(),
            RiskSummary = "Writes transparent DIB data to the existing RT_BITMAP IDs without remapping icon indexes."
        };
    }

    public IconResourceReplaceResult ClearBitmapIcon(CczProject project, string targetPath, int iconIndex)
    {
        var preview = PreviewClearBitmapIcon(project, targetPath, iconIndex);
        var resources = _dllCodec.ParseBitmapResources(preview.TargetPath);
        var pair = _dllCodec.ResolveBitmapResourcePair(resources, iconIndex);
        var updates = _dllCodec.BuildTransparentUpdates(pair)
            .Select(update => new BitmapResourceUpdate(update.ResourceId, update.LanguageId, update.DibBytes))
            .ToArray();
        return ApplyBitmapUpdates(project, preview, updates);
    }

    private IconResourceReplaceResult ApplyBitmapUpdates(
        CczProject project,
        IconResourceReplacePreviewResult preview,
        IReadOnlyList<BitmapResourceUpdate> updates)
    {
        if (updates.Count == 0)
        {
            throw new InvalidOperationException("No writable DLL icon resources were found.");
        }

        var backupPath = CreateBeforeSaveBackup(project, preview.TargetPath);
        var beforeBytes = File.ReadAllBytes(preview.TargetPath);
        var updateHandle = BeginUpdateResource(preview.TargetPath, false);
        if (updateHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("BeginUpdateResource failed, Win32Error=" + Marshal.GetLastWin32Error());
        }

        var committed = false;
        try
        {
            foreach (var update in updates)
            {
                if (!UpdateResource(updateHandle, (IntPtr)RtBitmap, (IntPtr)update.ResourceId, update.LanguageId, update.DibBytes, update.DibBytes.Length))
                {
                    throw new InvalidOperationException($"UpdateResource RT_BITMAP ID={update.ResourceId} LANG={update.LanguageId} failed, Win32Error={Marshal.GetLastWin32Error()}");
                }
            }

            if (!EndUpdateResource(updateHandle, false))
            {
                throw new InvalidOperationException("EndUpdateResource failed, Win32Error=" + Marshal.GetLastWin32Error());
            }

            committed = true;
        }
        finally
        {
            if (!committed)
            {
                EndUpdateResource(updateHandle, true);
            }
        }

        var afterBytes = File.ReadAllBytes(preview.TargetPath);
        var changedBytes = EstimateChangedBytes(beforeBytes, afterBytes);
        var newHash = WriteOperationReportService.ComputeSha256(afterBytes);
        var reportPath = WriteTextReport(project, preview, backupPath, afterBytes.LongLength, changedBytes, newHash);
        var reportJsonPath = WriteStructuredReport(project, preview, backupPath, reportPath, afterBytes.LongLength, changedBytes, newHash);

        return new IconResourceReplaceResult
        {
            TargetPath = preview.TargetPath,
            TargetRelativePath = preview.TargetRelativePath,
            IconIndex = preview.IconIndex,
            ResourceIds = preview.ResourceIds,
            ResourceDetails = preview.ResourceDetails,
            SourcePath = preview.SourcePath,
            OperationKind = preview.OperationKind,
            OldFileSizeBytes = preview.OldFileSizeBytes,
            SourceSizeBytes = preview.SourceSizeBytes,
            OldFileSha256 = preview.OldFileSha256,
            SourceSha256 = preview.SourceSha256,
            SourceWidth = preview.SourceWidth,
            SourceHeight = preview.SourceHeight,
            ResourceFormat = preview.ResourceFormat,
            FormatWarnings = preview.FormatWarnings,
            RiskSummary = preview.RiskSummary,
            BackupPath = backupPath,
            ReportPath = reportPath,
            ReportJsonPath = reportJsonPath,
            NewFileSizeBytes = afterBytes.LongLength,
            ChangedBytesEstimate = changedBytes,
            NewFileSha256 = newHash
        };
    }

    private IconResourceBatchReplaceResult ApplyBatchBitmapUpdates(
        CczProject project,
        IconResourceBatchReplacePreviewResult preview,
        IReadOnlyList<BitmapResourceUpdate> updates)
        => ApplyBatchBitmapUpdates(project, preview, updates, Array.Empty<BitmapResourceDelete>());

    private IconResourceBatchReplaceResult ApplyBatchBitmapUpdates(
        CczProject project,
        IconResourceBatchReplacePreviewResult preview,
        IReadOnlyList<BitmapResourceUpdate> updates,
        IReadOnlyList<BitmapResourceDelete> deletes)
    {
        if (updates.Count == 0)
        {
            throw new InvalidOperationException("No writable DLL icon resources were found.");
        }

        var backupPath = CreateBeforeSaveBackup(project, preview.TargetPath);
        var beforeBytes = File.ReadAllBytes(preview.TargetPath);
        var updateHandle = BeginUpdateResource(preview.TargetPath, false);
        if (updateHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("BeginUpdateResource failed, Win32Error=" + Marshal.GetLastWin32Error());
        }

        var committed = false;
        try
        {
            foreach (var update in updates)
            {
                if (!UpdateResource(updateHandle, (IntPtr)RtBitmap, (IntPtr)update.ResourceId, update.LanguageId, update.DibBytes, update.DibBytes.Length))
                {
                    throw new InvalidOperationException($"UpdateResource RT_BITMAP ID={update.ResourceId} LANG={update.LanguageId} failed, Win32Error={Marshal.GetLastWin32Error()}");
                }
            }

            foreach (var delete in deletes.Distinct())
            {
                if (!UpdateResource(updateHandle, (IntPtr)RtBitmap, (IntPtr)delete.ResourceId, delete.LanguageId, IntPtr.Zero, 0))
                {
                    throw new InvalidOperationException($"DeleteResource RT_BITMAP ID={delete.ResourceId} LANG={delete.LanguageId} failed, Win32Error={Marshal.GetLastWin32Error()}");
                }
            }

            if (!EndUpdateResource(updateHandle, false))
            {
                throw new InvalidOperationException("EndUpdateResource failed, Win32Error=" + Marshal.GetLastWin32Error());
            }

            committed = true;
        }
        finally
        {
            if (!committed)
            {
                EndUpdateResource(updateHandle, true);
            }
        }

        var afterBytes = File.ReadAllBytes(preview.TargetPath);
        var changedBytes = EstimateChangedBytes(beforeBytes, afterBytes);
        var newHash = WriteOperationReportService.ComputeSha256(afterBytes);
        var reportPath = WriteBatchTextReport(project, preview, backupPath, afterBytes.LongLength, changedBytes, newHash);
        var reportJsonPath = WriteBatchStructuredReport(project, preview, backupPath, reportPath, afterBytes.LongLength, changedBytes, newHash);

        return new IconResourceBatchReplaceResult
        {
            TargetPath = preview.TargetPath,
            TargetRelativePath = preview.TargetRelativePath,
            Requests = preview.Requests,
            Items = preview.Items,
            OperationKind = preview.OperationKind,
            OldFileSizeBytes = preview.OldFileSizeBytes,
            OldFileSha256 = preview.OldFileSha256,
            ResourceFormat = preview.ResourceFormat,
            FormatWarnings = preview.FormatWarnings,
            RiskSummary = preview.RiskSummary,
            BackupPath = backupPath,
            ReportPath = reportPath,
            ReportJsonPath = reportJsonPath,
            NewFileSizeBytes = afterBytes.LongLength,
            ChangedBytesEstimate = changedBytes,
            NewFileSha256 = newHash
        };
    }

    private IReadOnlyList<BitmapResourceUpdate> BuildBitmapUpdatesFromImage(string sourcePath, DllBitmapResourcePair targets)
    {
        using var rasterPair = _dllCodec.NormalizePairFromFile(sourcePath);
        return _dllCodec.BuildUpdates(targets, rasterPair)
            .Select(update => new BitmapResourceUpdate(update.ResourceId, update.LanguageId, update.DibBytes))
            .ToArray();
    }

    private static IReadOnlyList<IconResourceBatchReplaceRequest> MaterializeBitmapRequests(IReadOnlyList<IconResourceBitmapReplaceRequest> requests)
    {
        var result = new List<IconResourceBatchReplaceRequest>();
        foreach (var request in requests)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"CCZModStudio_pixel_{Guid.NewGuid():N}.png");
            request.Bitmap.Save(tempPath, ImageFormat.Png);
            result.Add(new IconResourceBatchReplaceRequest
            {
                IconIndex = request.IconIndex,
                SourcePath = tempPath,
                SourceLabel = string.IsNullOrWhiteSpace(request.SourceLabel) ? "<pixel editor>" : request.SourceLabel,
                OperationKind = string.IsNullOrWhiteSpace(request.OperationKind) ? "pixel editor" : request.OperationKind
            });
        }

        return result;
    }

    private static void DeleteTempRequestFiles(IReadOnlyList<IconResourceBatchReplaceRequest> requests)
    {
        foreach (var request in requests)
        {
            if (!Path.GetFileName(request.SourcePath).StartsWith("CCZModStudio_pixel_", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (File.Exists(request.SourcePath)) File.Delete(request.SourcePath);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static SourceImageInfo ReadSourceImageInfo(string sourcePath)
    {
        try
        {
            using var image = Image.FromFile(sourcePath);
            return new SourceImageInfo(image.Width, image.Height);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException)
        {
            throw new InvalidOperationException("Source file is not a decodable image. Supported input formats include BMP, JPG, and PNG.", ex);
        }
    }

    private static byte[] EncodeBitmapToPngBytes(Bitmap bitmap)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        return memory.ToArray();
    }

    private static IReadOnlyList<string> BuildReplaceWarnings(DllBitmapResourcePair pair, SourceImageInfo source)
    {
        var warnings = new List<string>();
        if (pair.AllVariants.Count == 0)
        {
            warnings.Add("No target RT_BITMAP resource was parsed.");
            return warnings;
        }

        var maxWidth = pair.AllVariants.Max(x => x.Width);
        var maxHeight = pair.AllVariants.Max(x => x.Height);
        if (source.Width != maxWidth || source.Height != maxHeight)
        {
            warnings.Add($"Source size {source.Width}x{source.Height} will be normalized for DLL sizes {string.Join("/", pair.AllVariants.Select(x => $"{x.Width}x{x.Height}").Distinct())}.");
        }

        if (pair.SmallVariants.Count == 0 || pair.LargeVariants.Count == 0)
        {
            warnings.Add("The expected 16x16/32x32 RT_BITMAP pair is incomplete.");
        }

        foreach (var group in pair.AllVariants.GroupBy(x => x.Id))
        {
            var languageCount = group.Select(x => x.LanguageId).Distinct().Count();
            var formatCount = group.Select(x => $"{x.Width}x{x.Height}/{x.BitCount}").Distinct(StringComparer.Ordinal).Count();
            if (languageCount > 1 || formatCount > 1)
            {
                warnings.Add($"RT_BITMAP ID={group.Key} has multiple language/format variants: {string.Join(", ", group.Select(DllBitmapIconCodecService.BuildVariantSummary))}.");
            }
        }

        return warnings;
    }

    private static string BuildReplaceRiskSummary(DllBitmapResourcePair pair, IReadOnlyList<string> warnings)
    {
        var ids = pair.ResourceIds.Count == 0 ? "none" : string.Join(", ", pair.ResourceIds.Select(x => x.ToString(CultureInfo.InvariantCulture)));
        var baseText = $"Writes DIB bytes to RT_BITMAP ID={ids}; the DLL is backed up before writing.";
        return warnings.Count == 0 ? baseText : baseText + " Warnings: " + string.Join("; ", warnings);
    }

    private static string BuildBatchReplaceRiskSummary(IReadOnlyList<IconResourceBatchReplacePreviewItem> items, IReadOnlyList<string> warnings)
    {
        var icons = items.Count == 0
            ? "none"
            : string.Join(", ", items.Select(item => $"#{item.IconIndex}=ID{string.Join("/", item.ResourceIds)}"));
        var baseText = $"Batch writes DIB bytes to RT_BITMAP resources: {icons}; the DLL is backed up once before writing.";
        return warnings.Count == 0 ? baseText : baseText + " Warnings: " + string.Join("; ", warnings);
    }

    private static void EnsureTargetInsideProject(CczProject project, string targetPath)
    {
        var gameRoot = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!targetPath.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Target DLL is outside the current project directory: " + targetPath);
        }
    }

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var safeRelative = MakeSafeRelativeName(project, filePath);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{safeRelative}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{stamp}_{suffix++}_{safeRelative}");
        }

        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }

    private static string MakeSafeRelativeName(CczProject project, string filePath)
    {
        var relative = Path.GetRelativePath(project.GameRoot, filePath);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            relative = relative.Replace(invalid, '_');
        }

        return relative.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
    }

    private static int EstimateChangedBytes(byte[] oldBytes, byte[] newBytes)
    {
        long count = Math.Abs((long)oldBytes.Length - newBytes.Length);
        var common = Math.Min(oldBytes.Length, newBytes.Length);
        for (var i = 0; i < common; i++)
        {
            if (oldBytes[i] != newBytes[i]) count++;
        }

        return count > int.MaxValue ? int.MaxValue : (int)count;
    }

    private static string WriteTextReport(
        CczProject project,
        IconResourceReplacePreviewResult preview,
        string backupPath,
        long newSize,
        int changedBytes,
        string newHash)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_DllIconReplaceReport.txt");
        var lines = new[]
        {
            "CCZModStudio DLL Icon Replace Report",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "GameRoot=" + project.GameRoot,
            "Target=" + preview.TargetPath,
            "TargetRelative=" + preview.TargetRelativePath,
            "OperationKind=" + preview.OperationKind,
            "IconIndex=" + preview.IconIndex.ToString(CultureInfo.InvariantCulture),
            "ResourceIds=" + string.Join(",", preview.ResourceIds),
            "ResourceDetails=" + string.Join(" | ", preview.ResourceDetails),
            "Source=" + preview.SourcePath,
            "Backup=" + backupPath,
            "OldSize=" + preview.OldFileSizeBytes.ToString(CultureInfo.InvariantCulture),
            "NewSize=" + newSize.ToString(CultureInfo.InvariantCulture),
            "ChangedBytesEstimate=" + changedBytes.ToString(CultureInfo.InvariantCulture),
            "OldSHA256=" + preview.OldFileSha256,
            "NewSHA256=" + newHash,
            "SourceSHA256=" + preview.SourceSha256,
            "Warnings=" + (preview.FormatWarnings.Count == 0 ? "none" : string.Join(" | ", preview.FormatWarnings)),
            "RiskSummary=" + preview.RiskSummary,
            string.Empty,
            "Note: only RT_BITMAP DIB bytes are written; table fields and resource IDs are not remapped."
        };
        File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        return reportPath;
    }

    private static string WriteBatchTextReport(
        CczProject project,
        IconResourceBatchReplacePreviewResult preview,
        string backupPath,
        long newSize,
        int changedBytes,
        string newHash)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_DllIconBatchReplaceReport.txt");
        var lines = new List<string>
        {
            "CCZModStudio DLL Icon Batch Replace Report",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "GameRoot=" + project.GameRoot,
            "Target=" + preview.TargetPath,
            "TargetRelative=" + preview.TargetRelativePath,
            "OperationKind=" + preview.OperationKind,
            "OperationCount=" + preview.Items.Count.ToString(CultureInfo.InvariantCulture),
            "Backup=" + backupPath,
            "OldSize=" + preview.OldFileSizeBytes.ToString(CultureInfo.InvariantCulture),
            "NewSize=" + newSize.ToString(CultureInfo.InvariantCulture),
            "ChangedBytesEstimate=" + changedBytes.ToString(CultureInfo.InvariantCulture),
            "OldSHA256=" + preview.OldFileSha256,
            "NewSHA256=" + newHash,
            "Warnings=" + (preview.FormatWarnings.Count == 0 ? "none" : string.Join(" | ", preview.FormatWarnings)),
            "RiskSummary=" + preview.RiskSummary,
            string.Empty,
            "Items:"
        };
        lines.AddRange(preview.Items.Select(item =>
            $"IconIndex={item.IconIndex}; ResourceIds={string.Join(",", item.ResourceIds)}; ResourceDetails={string.Join(" | ", item.ResourceDetails)}; Source={item.SourcePath}; SourceSize={item.SourceWidth}x{item.SourceHeight}; SourceSHA256={item.SourceSha256}"));
        lines.Add(string.Empty);
        lines.Add("Note: only RT_BITMAP DIB bytes are written; table fields and resource IDs are not remapped.");
        File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        return reportPath;
    }

    private string WriteStructuredReport(
        CczProject project,
        IconResourceReplacePreviewResult preview,
        string backupPath,
        string textReportPath,
        long newSize,
        int changedBytes,
        string newHash)
    {
        var report = new WriteOperationReport
        {
            OperationKind = "DLL RT_BITMAP icon replace",
            SourceAction = "auto backup before DLL icon resource write",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = preview.TargetRelativePath,
            TargetPath = preview.TargetPath,
            BackupPath = backupPath,
            TextReportPath = textReportPath,
            BeforeSha256 = preview.OldFileSha256,
            AfterSha256 = newHash,
            ChangedBytes = changedBytes,
            Summary = $"{preview.OperationKind}: {preview.TargetRelativePath}, icon #{preview.IconIndex}, resources {string.Join(",", preview.ResourceIds)}.",
            SafetyNotes = "Only RT_BITMAP DIB bytes are written. Icon IDs and game table fields are not remapped.",
            FormatCheckSummary = preview.ResourceFormat,
            RiskSummary = preview.RiskSummary,
            Changes =
            [
                new WriteOperationChange
                {
                    Category = "DLL icon",
                    TableName = preview.TargetRelativePath,
                    RowIndex = preview.IconIndex,
                    ColumnName = "RT_BITMAP",
                    OffsetHex = string.Join(",", preview.ResourceIds.Select(id => "ID=" + id.ToString(CultureInfo.InvariantCulture))),
                    ByteLength = newSize <= int.MaxValue ? (int)newSize : null,
                    OldValue = $"size={preview.OldFileSizeBytes}; sha256={preview.OldFileSha256}",
                    NewValue = $"size={newSize}; sha256={newHash}; source={preview.SourcePath}",
                    Annotation = preview.RiskSummary
                }
            ],
            Metadata =
            {
                ["IconIndex"] = preview.IconIndex.ToString(CultureInfo.InvariantCulture),
                ["ResourceIds"] = string.Join(",", preview.ResourceIds),
                ["ResourceDetails"] = string.Join(" | ", preview.ResourceDetails),
                ["OperationKind"] = preview.OperationKind,
                ["LegacyOperationKind"] = "DLL图标RT_BITMAP替换",
                ["SourcePath"] = preview.SourcePath,
                ["SourceSha256"] = preview.SourceSha256,
                ["FormatWarnings"] = preview.FormatWarnings.Count == 0 ? "none" : string.Join("; ", preview.FormatWarnings)
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    private string WriteBatchStructuredReport(
        CczProject project,
        IconResourceBatchReplacePreviewResult preview,
        string backupPath,
        string textReportPath,
        long newSize,
        int changedBytes,
        string newHash)
    {
        var report = new WriteOperationReport
        {
            OperationKind = "DLL RT_BITMAP icon batch replace",
            SourceAction = "auto backup before batch DLL icon resource write",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = preview.TargetRelativePath,
            TargetPath = preview.TargetPath,
            BackupPath = backupPath,
            TextReportPath = textReportPath,
            BeforeSha256 = preview.OldFileSha256,
            AfterSha256 = newHash,
            ChangedBytes = changedBytes,
            Summary = $"{preview.OperationKind}: {preview.TargetRelativePath}, {preview.Items.Count} icons.",
            SafetyNotes = "Only RT_BITMAP DIB bytes are written. Icon IDs and game table fields are not remapped.",
            FormatCheckSummary = preview.ResourceFormat,
            RiskSummary = preview.RiskSummary,
            Changes = preview.Items.Select(item => new WriteOperationChange
            {
                Category = "DLL icon",
                TableName = preview.TargetRelativePath,
                RowIndex = item.IconIndex,
                ColumnName = "RT_BITMAP",
                OffsetHex = string.Join(",", item.ResourceIds.Select(id => "ID=" + id.ToString(CultureInfo.InvariantCulture))),
                ByteLength = newSize <= int.MaxValue ? (int)newSize : null,
                OldValue = $"size={preview.OldFileSizeBytes}; sha256={preview.OldFileSha256}",
                NewValue = $"size={newSize}; sha256={newHash}; source={item.SourcePath}; sourceSha256={item.SourceSha256}",
                Annotation = item.FormatWarnings.Count == 0 ? "Replaced by batch request." : string.Join("; ", item.FormatWarnings)
            }).ToList(),
            Metadata =
            {
                ["OperationKind"] = preview.OperationKind,
                ["LegacyOperationKind"] = "DLL图标RT_BITMAP批量替换",
                ["OperationCount"] = preview.Items.Count.ToString(CultureInfo.InvariantCulture),
                ["IconIndexes"] = string.Join(",", preview.Items.Select(item => item.IconIndex.ToString(CultureInfo.InvariantCulture))),
                ["FormatWarnings"] = preview.FormatWarnings.Count == 0 ? "none" : string.Join("; ", preview.FormatWarnings)
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    [DllImport("kernel32.dll", EntryPoint = "BeginUpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResource(string pFileName, [MarshalAs(UnmanagedType.Bool)] bool bDeleteExistingResources);

    [DllImport("kernel32.dll", EntryPoint = "UpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, int cbData);

    [DllImport("kernel32.dll", EntryPoint = "UpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, IntPtr lpData, int cbData);

    [DllImport("kernel32.dll", EntryPoint = "EndUpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndUpdateResource(IntPtr hUpdate, [MarshalAs(UnmanagedType.Bool)] bool fDiscard);

    private sealed record BitmapResourceUpdate(int ResourceId, ushort LanguageId, byte[] DibBytes);
    private sealed record BitmapResourceDelete(int ResourceId, ushort LanguageId);
    private sealed record SourceImageInfo(int Width, int Height);
}
