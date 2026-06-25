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
    private readonly DllIconBitmapCodec _dllIconCodec = new();

    public IconResourceReplacePreviewResult PreviewReplaceBitmapIcon(CczProject project, string targetPath, int iconIndex, string sourcePath)
    {
        var target = Path.GetFullPath(targetPath);
        var source = Path.GetFullPath(sourcePath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("目标 DLL 文件不存在。", target);
        if (!File.Exists(source)) throw new FileNotFoundException("来源图片文件不存在。", source);

        var resources = _dllIconCodec.ReadBitmapResources(target);
        var slot = _dllIconCodec.ResolveGameIconSlot(target, resources, iconIndex, Path.GetFileName(target));
        var pair = slot.WritableVariants;
        var sourceInfo = ReadSourceImageInfo(source);
        var oldBytes = File.ReadAllBytes(target);
        var sourceBytes = File.ReadAllBytes(source);
        var warnings = BuildReplaceWarnings(pair, sourceInfo).Concat(slot.Warnings).Distinct(StringComparer.Ordinal).ToArray();
        return new IconResourceReplacePreviewResult
        {
            TargetPath = target,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, target),
            IconIndex = iconIndex,
            ResourceIds = pair.Select(x => x.Id).Distinct().ToArray(),
            ResourceVariants = ToVariantInfo(pair),
            SourcePath = source,
            OperationKind = "替换RT_BITMAP图标",
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
        var resources = _dllIconCodec.ReadBitmapResources(preview.TargetPath);
        var slot = _dllIconCodec.ResolveGameIconSlot(preview.TargetPath, resources, iconIndex, Path.GetFileName(preview.TargetPath));
        var updates = _dllIconCodec.BuildUpdatesFromImage(sourcePath, slot.WritableVariants);
        return ApplyBitmapUpdates(project, preview, updates);
    }

    public IconResourceBatchReplacePreviewResult PreviewReplaceBitmapIcons(
        CczProject project,
        string targetPath,
        IReadOnlyList<IconResourceBatchReplaceRequest> requests)
    {
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("没有可导入的 DLL 图标。");
        }

        var target = Path.GetFullPath(targetPath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("目标 DLL 文件不存在。", target);

        var duplicateIcon = requests
            .GroupBy(request => request.IconIndex)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateIcon != null)
        {
            throw new InvalidOperationException($"批量导入包含重复图标编号：{duplicateIcon.Key}。");
        }

        var resources = _dllIconCodec.ReadBitmapResources(target);
        var oldBytes = File.ReadAllBytes(target);
        var items = new List<IconResourceBatchReplacePreviewItem>();
        var warnings = new List<string>();
        foreach (var request in requests.OrderBy(request => request.IconIndex))
        {
            var source = Path.GetFullPath(request.SourcePath);
            if (!File.Exists(source)) throw new FileNotFoundException("来源图片文件不存在。", source);

            var slot = _dllIconCodec.ResolveGameIconSlot(target, resources, request.IconIndex, Path.GetFileName(target));
            var pair = slot.WritableVariants;
            var sourceInfo = ReadSourceImageInfo(source);
            var sourceBytes = File.ReadAllBytes(source);
            var itemWarnings = BuildReplaceWarnings(pair, sourceInfo).Concat(slot.Warnings).Distinct(StringComparer.Ordinal).ToArray();
            warnings.AddRange(itemWarnings.Select(warning => $"图标#{request.IconIndex}：{warning}"));
            items.Add(new IconResourceBatchReplacePreviewItem
            {
                IconIndex = request.IconIndex,
                ResourceIds = pair.Select(x => x.Id).Distinct().ToArray(),
                ResourceVariants = ToVariantInfo(pair),
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
            OperationKind = requests.Select(request => request.OperationKind).FirstOrDefault(kind => !string.IsNullOrWhiteSpace(kind)) ?? "批量替换RT_BITMAP图标",
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
        var resources = _dllIconCodec.ReadBitmapResources(preview.TargetPath);
        var updates = new List<DllIconBitmapUpdate>();
        foreach (var request in requests.OrderBy(request => request.IconIndex))
        {
            var slot = _dllIconCodec.ResolveGameIconSlot(preview.TargetPath, resources, request.IconIndex, Path.GetFileName(preview.TargetPath));
            updates.AddRange(_dllIconCodec.BuildUpdatesFromImage(request.SourcePath, slot.WritableVariants));
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
            throw new InvalidOperationException("没有可导入的 DLL 图标。");
        }

        var target = Path.GetFullPath(targetPath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("目标 DLL 文件不存在。", target);

        var duplicateIcon = requests
            .GroupBy(request => request.IconIndex)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateIcon != null)
        {
            throw new InvalidOperationException($"批量导入包含重复图标编号：{duplicateIcon.Key}。");
        }

        var resources = _dllIconCodec.ReadBitmapResources(target);
        var oldBytes = File.ReadAllBytes(target);
        var items = new List<IconResourceBatchReplacePreviewItem>();
        var warnings = new List<string>();
        var updates = new List<DllIconBitmapUpdate>();
        foreach (var request in requests.OrderBy(request => request.IconIndex))
        {
            var slot = _dllIconCodec.ResolveGameIconSlot(target, resources, request.IconIndex, Path.GetFileName(target));
            var pair = slot.WritableVariants;
            var sourceInfo = new SourceImageInfo(request.Bitmap.Width, request.Bitmap.Height);
            var itemWarnings = BuildReplaceWarnings(pair, sourceInfo).Concat(slot.Warnings).Distinct(StringComparer.Ordinal).ToArray();
            warnings.AddRange(itemWarnings.Select(warning => $"图标#{request.IconIndex}：{warning}"));
            var sourceLabel = string.IsNullOrWhiteSpace(request.SourceLabel) ? "<像素编辑>" : request.SourceLabel;
            var sourceBytes = EncodeBitmapToPngBytes(request.Bitmap);
            items.Add(new IconResourceBatchReplacePreviewItem
            {
                IconIndex = request.IconIndex,
                ResourceIds = pair.Select(x => x.Id).Distinct().ToArray(),
                ResourceVariants = ToVariantInfo(pair),
                SourcePath = sourceLabel,
                SourceLabel = sourceLabel,
                SourceSizeBytes = sourceBytes.LongLength,
                SourceSha256 = WriteOperationReportService.ComputeSha256(sourceBytes),
                SourceWidth = sourceInfo.Width,
                SourceHeight = sourceInfo.Height,
                FormatWarnings = itemWarnings
            });

            updates.AddRange(_dllIconCodec.BuildUpdatesFromImage(request.Bitmap, pair));
        }

        var preview = new IconResourceBatchReplacePreviewResult
        {
            TargetPath = target,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, target),
            Requests = requests.Select(request => new IconResourceBatchReplaceRequest
            {
                IconIndex = request.IconIndex,
                SourcePath = string.IsNullOrWhiteSpace(request.SourceLabel) ? "<像素编辑>" : request.SourceLabel,
                SourceLabel = request.SourceLabel,
                OperationKind = request.OperationKind
            }).ToArray(),
            Items = items,
            OperationKind = requests.Select(request => request.OperationKind).FirstOrDefault(kind => !string.IsNullOrWhiteSpace(kind)) ?? "批量替换RT_BITMAP图标",
            OldFileSizeBytes = oldBytes.LongLength,
            OldFileSha256 = WriteOperationReportService.ComputeSha256(oldBytes),
            ResourceFormat = "DLL RT_BITMAP",
            FormatWarnings = warnings,
            RiskSummary = BuildBatchReplaceRiskSummary(items, warnings)
        };

        return ApplyBatchBitmapUpdates(project, preview, updates);
    }

    public IconResourceReplacePreviewResult PreviewClearBitmapIcon(CczProject project, string targetPath, int iconIndex)
    {
        var target = Path.GetFullPath(targetPath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("目标 DLL 文件不存在。", target);

        var resources = _dllIconCodec.ReadBitmapResources(target);
        var slot = _dllIconCodec.ResolveGameIconSlot(target, resources, iconIndex, Path.GetFileName(target));
        var pair = slot.WritableVariants;
        var oldBytes = File.ReadAllBytes(target);
        return new IconResourceReplacePreviewResult
        {
            TargetPath = target,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, target),
            IconIndex = iconIndex,
            ResourceIds = pair.Select(x => x.Id).Distinct().ToArray(),
            ResourceVariants = ToVariantInfo(pair),
            SourcePath = "<透明占位>",
            OperationKind = "清空RT_BITMAP图标",
            OldFileSizeBytes = oldBytes.LongLength,
            SourceSizeBytes = 0,
            OldFileSha256 = WriteOperationReportService.ComputeSha256(oldBytes),
            SourceSha256 = string.Empty,
            ResourceFormat = "DLL RT_BITMAP",
            FormatWarnings = slot.Warnings,
            RiskSummary = "清空不会删除资源ID，而是按原尺寸写入透明位图，避免破坏字段编号到资源ID的对应关系。"
        };
    }

    public IconResourceReplaceResult ClearBitmapIcon(CczProject project, string targetPath, int iconIndex)
    {
        var preview = PreviewClearBitmapIcon(project, targetPath, iconIndex);
        var resources = _dllIconCodec.ReadBitmapResources(preview.TargetPath);
        var slot = _dllIconCodec.ResolveGameIconSlot(preview.TargetPath, resources, iconIndex, Path.GetFileName(preview.TargetPath));
        var updates = slot.WritableVariants.Select(_dllIconCodec.BuildTransparentUpdate).ToArray();
        return ApplyBitmapUpdates(project, preview, updates);
    }

    private IconResourceReplaceResult ApplyBitmapUpdates(
        CczProject project,
        IconResourceReplacePreviewResult preview,
        IReadOnlyList<DllIconBitmapUpdate> updates)
    {
        if (updates.Count == 0)
        {
            throw new InvalidOperationException("没有可写入的 DLL 图标资源。");
        }

        var backupPath = CreateBeforeSaveBackup(project, preview.TargetPath);
        var beforeBytes = File.ReadAllBytes(preview.TargetPath);
        var updateHandle = BeginUpdateResource(preview.TargetPath, false);
        if (updateHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("BeginUpdateResource 失败，Win32Error=" + Marshal.GetLastWin32Error());
        }

        var committed = false;
        try
        {
            foreach (var update in updates)
            {
                if (!UpdateResource(updateHandle, (IntPtr)RtBitmap, (IntPtr)update.ResourceId, update.LanguageId, update.DibBytes, update.DibBytes.Length))
                {
                    throw new InvalidOperationException($"UpdateResource RT_BITMAP ID={update.ResourceId} 失败，Win32Error={Marshal.GetLastWin32Error()}");
                }
            }

            if (!EndUpdateResource(updateHandle, false))
            {
                throw new InvalidOperationException("EndUpdateResource 失败，Win32Error=" + Marshal.GetLastWin32Error());
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
        var updateWarnings = updates.SelectMany(update => update.Warnings).Distinct(StringComparer.Ordinal).ToArray();
        var readback = VerifyReadback(preview.TargetPath, updates);
        var reportPath = WriteTextReport(project, preview, updates, backupPath, afterBytes.LongLength, changedBytes, newHash);
        var reportJsonPath = WriteStructuredReport(project, preview, backupPath, reportPath, afterBytes.LongLength, changedBytes, newHash);

        return new IconResourceReplaceResult
        {
            TargetPath = preview.TargetPath,
            TargetRelativePath = preview.TargetRelativePath,
            IconIndex = preview.IconIndex,
            ResourceIds = preview.ResourceIds,
            ResourceVariants = preview.ResourceVariants,
            SourcePath = preview.SourcePath,
            OperationKind = preview.OperationKind,
            OldFileSizeBytes = preview.OldFileSizeBytes,
            SourceSizeBytes = preview.SourceSizeBytes,
            OldFileSha256 = preview.OldFileSha256,
            SourceSha256 = preview.SourceSha256,
            SourceWidth = preview.SourceWidth,
            SourceHeight = preview.SourceHeight,
            ResourceFormat = preview.ResourceFormat,
            FormatWarnings = preview.FormatWarnings.Concat(updateWarnings).Concat(readback.Warnings).Distinct(StringComparer.Ordinal).ToArray(),
            RiskSummary = preview.RiskSummary,
            BackupPath = backupPath,
            ReportPath = reportPath,
            ReportJsonPath = reportJsonPath,
            NewFileSizeBytes = afterBytes.LongLength,
            ChangedBytesEstimate = changedBytes,
            NewFileSha256 = newHash,
            ReadbackVerified = readback.Verified,
            ReadbackWarnings = readback.Warnings,
            ReadbackItems = readback.Items
        };
    }

    private IconResourceBatchReplaceResult ApplyBatchBitmapUpdates(
        CczProject project,
        IconResourceBatchReplacePreviewResult preview,
        IReadOnlyList<DllIconBitmapUpdate> updates)
    {
        if (updates.Count == 0)
        {
            throw new InvalidOperationException("没有可写入的 DLL 图标资源。");
        }

        var backupPath = CreateBeforeSaveBackup(project, preview.TargetPath);
        var beforeBytes = File.ReadAllBytes(preview.TargetPath);
        var updateHandle = BeginUpdateResource(preview.TargetPath, false);
        if (updateHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("BeginUpdateResource 失败，Win32Error=" + Marshal.GetLastWin32Error());
        }

        var committed = false;
        try
        {
            foreach (var update in updates)
            {
                if (!UpdateResource(updateHandle, (IntPtr)RtBitmap, (IntPtr)update.ResourceId, update.LanguageId, update.DibBytes, update.DibBytes.Length))
                {
                    throw new InvalidOperationException($"UpdateResource RT_BITMAP ID={update.ResourceId} 失败，Win32Error={Marshal.GetLastWin32Error()}");
                }
            }

            if (!EndUpdateResource(updateHandle, false))
            {
                throw new InvalidOperationException("EndUpdateResource 失败，Win32Error=" + Marshal.GetLastWin32Error());
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
        var updateWarnings = updates.SelectMany(update => update.Warnings).Distinct(StringComparer.Ordinal).ToArray();
        var readback = VerifyReadback(preview.TargetPath, updates);
        var reportPath = WriteBatchTextReport(project, preview, updates, backupPath, afterBytes.LongLength, changedBytes, newHash);
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
            FormatWarnings = preview.FormatWarnings.Concat(updateWarnings).Concat(readback.Warnings).Distinct(StringComparer.Ordinal).ToArray(),
            RiskSummary = preview.RiskSummary,
            BackupPath = backupPath,
            ReportPath = reportPath,
            ReportJsonPath = reportJsonPath,
            NewFileSizeBytes = afterBytes.LongLength,
            ChangedBytesEstimate = changedBytes,
            NewFileSha256 = newHash,
            ReadbackVerified = readback.Verified,
            ReadbackWarnings = readback.Warnings,
            ReadbackItems = readback.Items
        };
    }

    private ReadbackVerification VerifyReadback(string targetPath, IReadOnlyList<DllIconBitmapUpdate> updates)
    {
        var resources = _dllIconCodec.ReadBitmapResources(targetPath);
        var items = new List<IconResourceReadbackInfo>();
        var warnings = new List<string>();

        foreach (var update in updates)
        {
            var resource = resources.FirstOrDefault(x => x.Id == update.ResourceId && x.LanguageId == update.LanguageId)
                           ?? resources.FirstOrDefault(x => x.Id == update.ResourceId);
            if (resource == null)
            {
                warnings.Add($"写回后未能重读 RT_BITMAP ID={update.ResourceId} Lang={update.LanguageId}。");
                items.Add(new IconResourceReadbackInfo
                {
                    ResourceId = update.ResourceId,
                    LanguageId = update.LanguageId,
                    ExpectedWidth = update.Width,
                    ExpectedHeight = update.Height,
                    ExpectedPixelHash = update.ExpectedPixelHash,
                    SourceBitCount = update.SourceBitCount,
                    WrittenBitCount = update.WrittenBitCount,
                    TransparencyMode = update.TransparencyMode,
                    VariantWritePolicy = update.VariantWritePolicy,
                    PreparedLargeHash = update.PreparedLargeHash,
                    SourceOpaqueBounds = update.SourceOpaqueBounds,
                    Matches = false
                });
                continue;
            }

            using var bitmap = _dllIconCodec.DecodeDib(resource.DibBytes);
            if (bitmap == null)
            {
                warnings.Add($"写回后 RT_BITMAP ID={update.ResourceId} Lang={update.LanguageId} 无法解码。");
                items.Add(new IconResourceReadbackInfo
                {
                    ResourceId = update.ResourceId,
                    LanguageId = update.LanguageId,
                    ExpectedWidth = update.Width,
                    ExpectedHeight = update.Height,
                    ActualWidth = resource.Width,
                    ActualHeight = resource.Height,
                    ExpectedPixelHash = update.ExpectedPixelHash,
                    SourceBitCount = update.SourceBitCount,
                    WrittenBitCount = update.WrittenBitCount,
                    ActualBitCount = resource.BitCount,
                    TransparencyMode = update.TransparencyMode,
                    VariantWritePolicy = update.VariantWritePolicy,
                    PreparedLargeHash = update.PreparedLargeHash,
                    SourceOpaqueBounds = update.SourceOpaqueBounds,
                    Matches = false
                });
                continue;
            }

            var actualHash = DllIconBitmapCodec.ComputePixelHash(bitmap);
            var matches = bitmap.Width == update.Width &&
                          bitmap.Height == update.Height &&
                          actualHash.Equals(update.ExpectedPixelHash, StringComparison.OrdinalIgnoreCase) &&
                          resource.BitCount == update.WrittenBitCount;
            if (!matches)
            {
                warnings.Add($"写回后重读不一致：RT_BITMAP ID={update.ResourceId} Lang={update.LanguageId}，期望 {update.Width}x{update.Height}/{update.ExpectedPixelHash}，实际 {bitmap.Width}x{bitmap.Height}/{actualHash}。");
            }

            var diagnostics = AnalyzeReadbackBitmap(bitmap);
            if (update.MagentaKeyPixelCount > 0 && diagnostics.TransparentPixelCount == 0)
            {
                warnings.Add($"Readback lost transparency: RT_BITMAP ID={update.ResourceId} Lang={update.LanguageId}, source magenta key pixels={update.MagentaKeyPixelCount}, actual transparent pixels=0.");
                matches = false;
            }

            if (diagnostics.VisibleMagentaPixelCount > 0)
            {
                warnings.Add($"Readback has visible magenta pixels: RT_BITMAP ID={update.ResourceId} Lang={update.LanguageId}, count={diagnostics.VisibleMagentaPixelCount}.");
                matches = false;
            }

            if (update.MagentaKeyPixelCount > 0 &&
                diagnostics.WhiteishPixelCount > bitmap.Width * bitmap.Height / 2)
            {
                warnings.Add($"Readback may be white-background corrupted: RT_BITMAP ID={update.ResourceId} Lang={update.LanguageId}, whiteish={diagnostics.WhiteishPixelCount}/{bitmap.Width * bitmap.Height}.");
                matches = false;
            }

            items.Add(new IconResourceReadbackInfo
            {
                ResourceId = update.ResourceId,
                LanguageId = update.LanguageId,
                ExpectedWidth = update.Width,
                ExpectedHeight = update.Height,
                ActualWidth = bitmap.Width,
                ActualHeight = bitmap.Height,
                ExpectedPixelHash = update.ExpectedPixelHash,
                ActualPixelHash = actualHash,
                SourceBitCount = update.SourceBitCount,
                WrittenBitCount = update.WrittenBitCount,
                ActualBitCount = resource.BitCount,
                TransparencyMode = update.TransparencyMode,
                TransparentPixelCount = update.TransparentPixelCount,
                MagentaKeyPixelCount = update.MagentaKeyPixelCount,
                CornerBackgroundPixelCount = update.CornerBackgroundPixelCount,
                VariantWritePolicy = update.VariantWritePolicy,
                PreparedLargeHash = update.PreparedLargeHash,
                SourceOpaqueBounds = update.SourceOpaqueBounds,
                ActualTransparentPixelCount = diagnostics.TransparentPixelCount,
                ActualVisibleMagentaPixelCount = diagnostics.VisibleMagentaPixelCount,
                ActualWhiteishPixelCount = diagnostics.WhiteishPixelCount,
                ActualOpaquePixelCount = diagnostics.OpaquePixelCount,
                Matches = matches
            });
        }

        var verified = items.Count == updates.Count && items.All(item => item.Matches) && warnings.Count == 0;
        return new ReadbackVerification(verified, warnings, items);
    }

    private static ReadbackBitmapDiagnostics AnalyzeReadbackBitmap(Bitmap bitmap)
    {
        var transparent = 0;
        var visibleMagenta = 0;
        var whiteish = 0;
        var opaque = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    transparent++;
                    continue;
                }

                opaque++;
                if (DllIconBitmapCodec.IsMagentaKey(pixel)) visibleMagenta++;
                if (pixel.R >= 220 && pixel.G >= 220 && pixel.B >= 220) whiteish++;
            }
        }

        return new ReadbackBitmapDiagnostics(transparent, visibleMagenta, whiteish, opaque);
    }

    private sealed record ReadbackBitmapDiagnostics(
        int TransparentPixelCount,
        int VisibleMagentaPixelCount,
        int WhiteishPixelCount,
        int OpaquePixelCount);

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
                SourceLabel = string.IsNullOrWhiteSpace(request.SourceLabel) ? "<像素编辑>" : request.SourceLabel,
                OperationKind = string.IsNullOrWhiteSpace(request.OperationKind) ? "像素编辑" : request.OperationKind
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
            throw new InvalidOperationException("来源文件不是可解码图片，DLL 图标替换仅支持 BMP/JPG/PNG 等 GDI+ 可读取图片。", ex);
        }
    }

    private static byte[] EncodeBitmapToPngBytes(Bitmap bitmap)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        return memory.ToArray();
    }

    private static IReadOnlyList<string> BuildReplaceWarnings(IReadOnlyList<DllIconBitmapResource> pair, SourceImageInfo source)
    {
        var warnings = new List<string>();
        if (pair.Count == 0)
        {
            warnings.Add("没有解析到目标 RT_BITMAP 资源。");
            return warnings;
        }

        var maxWidth = pair.Max(x => x.Width);
        var maxHeight = pair.Max(x => x.Height);
        if (source.Width != maxWidth || source.Height != maxHeight)
        {
            warnings.Add($"来源图片尺寸 {source.Width}x{source.Height} 会缩放/居中到 DLL 资源尺寸 {string.Join("/", pair.Select(x => $"{x.Width}x{x.Height}"))}。");
        }

        if (pair.Count == 1)
        {
            warnings.Add("只找到一个 RT_BITMAP 资源；若该 DLL 原本按 16x16/32x32 成对保存，另一个尺寸可能缺失。");
        }

        if (pair.Any(resource => resource.BitCount == 8))
        {
            warnings.Add("8bpp RT_BITMAP variants will be written back as 32bpp top-first images for game compatibility.");
        }

        var unsupportedBitCounts = pair
            .Where(resource => resource.BitCount is not (8 or 32))
            .Select(resource => resource.BitCount)
            .Distinct()
            .OrderBy(bitCount => bitCount)
            .ToArray();
        if (unsupportedBitCounts.Length > 0)
        {
            warnings.Add($"RT_BITMAP variants with original bit depth {string.Join("/", unsupportedBitCounts)}bpp will be written as 32bpp.");
        }

        warnings.Add("#FF00FF and near-magenta source pixels will be treated as transparent before scaling.");

        return warnings;
    }

    private static string BuildReplaceRiskSummary(IReadOnlyList<DllIconBitmapResource> pair, IReadOnlyList<string> warnings)
    {
        var ids = pair.Count == 0 ? "无" : string.Join(", ", pair.Select(x => x.Id.ToString(CultureInfo.InvariantCulture)));
        var baseText = $"按字段编号定位 RT_BITMAP 资源 ID={ids} 并替换其 DIB 数据；写入前自动备份 DLL。";
        return warnings.Count == 0 ? baseText : baseText + "提示：" + string.Join("；", warnings);
    }

    private static string BuildBatchReplaceRiskSummary(IReadOnlyList<IconResourceBatchReplacePreviewItem> items, IReadOnlyList<string> warnings)
    {
        var icons = items.Count == 0
            ? "无"
            : string.Join(", ", items.Select(item => $"#{item.IconIndex}=ID{string.Join("/", item.ResourceIds)}"));
        var baseText = $"批量按字段编号定位 RT_BITMAP 资源并替换 DIB 数据：{icons}；写入前只备份一次 DLL。";
        return warnings.Count == 0 ? baseText : baseText + "提示：" + string.Join("；", warnings);
    }

    private static IReadOnlyList<IconResourceVariantInfo> ToVariantInfo(IReadOnlyList<DllIconBitmapResource> resources)
        => resources
            .Select(resource => new IconResourceVariantInfo
            {
                ResourceId = resource.Id,
                LanguageId = resource.LanguageId,
                Width = resource.Width,
                Height = resource.Height,
                BitCount = resource.BitCount,
                SizeBytes = resource.SizeBytes
            })
            .ToArray();

    private static void EnsureTargetInsideProject(CczProject project, string targetPath)
    {
        var gameRoot = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!targetPath.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标 DLL 文件不在当前项目目录内，禁止写入：" + targetPath);
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

    private static string FormatUpdateReportLine(DllIconBitmapUpdate update)
        => $"RT_BITMAP ID={update.ResourceId}; Lang={update.LanguageId}; Size={update.Width}x{update.Height}; BitCount={update.SourceBitCount}->{update.WrittenBitCount}; Policy={update.VariantWritePolicy}; OpaqueBounds={update.SourceOpaqueBounds}; PreparedLargeHash={update.PreparedLargeHash}; Transparency={update.TransparencyMode}; TransparentPixels={update.TransparentPixelCount}; MagentaKeyPixels={update.MagentaKeyPixelCount}; CornerBackgroundPixels={update.CornerBackgroundPixelCount}; Warnings={(update.Warnings.Count == 0 ? "None" : string.Join(" | ", update.Warnings))}";

    private static string WriteTextReport(
        CczProject project,
        IconResourceReplacePreviewResult preview,
        IReadOnlyList<DllIconBitmapUpdate> updates,
        string backupPath,
        long newSize,
        int changedBytes,
        string newHash)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_DllIconReplaceReport.txt");
        var lines = new List<string>
        {
            "CCZModStudio DLL Icon Replace Report",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "GameRoot=" + project.GameRoot,
            "Target=" + preview.TargetPath,
            "TargetRelative=" + preview.TargetRelativePath,
            "OperationKind=" + preview.OperationKind,
            "IconIndex=" + preview.IconIndex.ToString(CultureInfo.InvariantCulture),
            "ResourceIds=" + string.Join(",", preview.ResourceIds),
            "Source=" + preview.SourcePath,
            "Backup=" + backupPath,
            "OldSize=" + preview.OldFileSizeBytes.ToString(CultureInfo.InvariantCulture),
            "NewSize=" + newSize.ToString(CultureInfo.InvariantCulture),
            "ChangedBytesEstimate=" + changedBytes.ToString(CultureInfo.InvariantCulture),
            "OldSHA256=" + preview.OldFileSha256,
            "NewSHA256=" + newHash,
            "SourceSHA256=" + preview.SourceSha256,
            "Warnings=" + (preview.FormatWarnings.Count == 0 ? "无" : string.Join(" | ", preview.FormatWarnings)),
            "RiskSummary=" + preview.RiskSummary,
            string.Empty,
            "说明：当前 DLL 图标写回针对曹操传样本中的 RT_BITMAP 成对资源；不重排资源 ID。"
        };
        lines.Insert(lines.Count - 1, string.Empty);
        lines.Insert(lines.Count - 1, "Updates:");
        lines.InsertRange(lines.Count - 1, updates.Select(FormatUpdateReportLine));
        File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        return reportPath;
    }

    private static string WriteBatchTextReport(
        CczProject project,
        IconResourceBatchReplacePreviewResult preview,
        IReadOnlyList<DllIconBitmapUpdate> updates,
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
            "Warnings=" + (preview.FormatWarnings.Count == 0 ? "无" : string.Join(" | ", preview.FormatWarnings)),
            "RiskSummary=" + preview.RiskSummary,
            string.Empty,
            "Items:"
        };
        lines.AddRange(preview.Items.Select(item =>
            $"IconIndex={item.IconIndex}; ResourceIds={string.Join(",", item.ResourceIds)}; Source={item.SourcePath}; SourceSize={item.SourceWidth}x{item.SourceHeight}; SourceSHA256={item.SourceSha256}"));
        lines.Add(string.Empty);
        lines.Add("Updates:");
        lines.AddRange(updates.Select(FormatUpdateReportLine));
        lines.Add(string.Empty);
        lines.Add("说明：当前 DLL 图标批量写回针对曹操传样本中的 RT_BITMAP 成对资源；不重排资源 ID。");
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
            OperationKind = "DLL图标RT_BITMAP替换",
            SourceAction = "DLL图标资源写入前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = preview.TargetRelativePath,
            TargetPath = preview.TargetPath,
            BackupPath = backupPath,
            TextReportPath = textReportPath,
            BeforeSha256 = preview.OldFileSha256,
            AfterSha256 = newHash,
            ChangedBytes = changedBytes,
            Summary = $"{preview.OperationKind}：{preview.TargetRelativePath} 编号 #{preview.IconIndex}，资源ID {string.Join(",", preview.ResourceIds)}。",
            SafetyNotes = "当前只写 DLL PE 资源目录中的 RT_BITMAP DIB 数据；不重排编号，不修改游戏数据表字段。",
            FormatCheckSummary = preview.ResourceFormat,
            RiskSummary = preview.RiskSummary,
            Changes =
            [
                new WriteOperationChange
                {
                    Category = "DLL图标",
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
                ["OperationKind"] = preview.OperationKind,
                ["SourcePath"] = preview.SourcePath,
                ["SourceSha256"] = preview.SourceSha256,
                ["FormatWarnings"] = preview.FormatWarnings.Count == 0 ? "无" : string.Join("；", preview.FormatWarnings)
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
            OperationKind = "DLL图标RT_BITMAP批量替换",
            SourceAction = "DLL图标资源批量写入前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = preview.TargetRelativePath,
            TargetPath = preview.TargetPath,
            BackupPath = backupPath,
            TextReportPath = textReportPath,
            BeforeSha256 = preview.OldFileSha256,
            AfterSha256 = newHash,
            ChangedBytes = changedBytes,
            Summary = $"{preview.OperationKind}：{preview.TargetRelativePath}，共 {preview.Items.Count} 个图标。",
            SafetyNotes = "当前只写 DLL PE 资源目录中的 RT_BITMAP DIB 数据；不重排编号，不修改游戏数据表字段。",
            FormatCheckSummary = preview.ResourceFormat,
            RiskSummary = preview.RiskSummary,
            Changes = preview.Items.Select(item => new WriteOperationChange
            {
                Category = "DLL图标",
                TableName = preview.TargetRelativePath,
                RowIndex = item.IconIndex,
                ColumnName = "RT_BITMAP",
                OffsetHex = string.Join(",", item.ResourceIds.Select(id => "ID=" + id.ToString(CultureInfo.InvariantCulture))),
                ByteLength = newSize <= int.MaxValue ? (int)newSize : null,
                OldValue = $"size={preview.OldFileSizeBytes}; sha256={preview.OldFileSha256}",
                NewValue = $"size={newSize}; sha256={newHash}; source={item.SourcePath}; sourceSha256={item.SourceSha256}",
                Annotation = item.FormatWarnings.Count == 0 ? "按批量导入请求替换。 " : string.Join("；", item.FormatWarnings)
            }).ToList(),
            Metadata =
            {
                ["OperationKind"] = preview.OperationKind,
                ["OperationCount"] = preview.Items.Count.ToString(CultureInfo.InvariantCulture),
                ["IconIndexes"] = string.Join(",", preview.Items.Select(item => item.IconIndex.ToString(CultureInfo.InvariantCulture))),
                ["FormatWarnings"] = preview.FormatWarnings.Count == 0 ? "无" : string.Join("；", preview.FormatWarnings)
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    [DllImport("kernel32.dll", EntryPoint = "BeginUpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResource(string pFileName, [MarshalAs(UnmanagedType.Bool)] bool bDeleteExistingResources);

    [DllImport("kernel32.dll", EntryPoint = "UpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, int cbData);

    [DllImport("kernel32.dll", EntryPoint = "EndUpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndUpdateResource(IntPtr hUpdate, [MarshalAs(UnmanagedType.Bool)] bool fDiscard);

    private sealed record SourceImageInfo(int Width, int Height);
    private sealed record ReadbackVerification(bool Verified, IReadOnlyList<string> Warnings, IReadOnlyList<IconResourceReadbackInfo> Items);
}
