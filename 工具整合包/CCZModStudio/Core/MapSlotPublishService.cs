using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MapSlotPublishService
{
    public const string Verified65ExecutableSha256 = "F9A357585F4D77C273A8BADD3E4EF4062983CC81D8040C0CB74768FAB7C04F5F";
    private readonly MapSlotCatalogService _catalogService = new();
    private readonly HexzmapProbeReader _hexzmapProbeReader = new();
    private readonly HexzmapLayoutWriter _layoutWriter = new();
    private readonly MapCanvasComposeService _composeService = new();
    private readonly WriteOperationReportService _reportService = new();
    private readonly MapDraftService _draftService = new();
    private readonly Action<MapSlotPublishFaultPoint>? _faultInjector;

    public MapSlotPublishService(Action<MapSlotPublishFaultPoint>? faultInjector = null)
    {
        _faultInjector = faultInjector;
    }

    public IReadOnlyList<MapSlotCatalogEntry> ListSlots(CczProject project) => _catalogService.List(project);

    public MapSlotPublishPlan Preview(
        CczProject project,
        MapWorkbenchDraft draft,
        IReadOnlyList<MaterialAsset> materials,
        MapSlotPublishRequest request,
        Bitmap? confirmedRender = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(request);
        ValidateDraft(draft);
        if (!string.IsNullOrWhiteSpace(request.DraftId) &&
            !request.DraftId.Equals(draft.DraftId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("发布请求中的 DraftId 与当前草稿不一致。");
        }

        var probe = _hexzmapProbeReader.Read(project);
        var hexzmapPath = probe.Path;
        var originalHexzmap = File.ReadAllBytes(hexzmapPath);
        var originalHexzmapHash = WriteOperationReportService.ComputeSha256(originalHexzmap);
        ValidateExpectedHash(request.ExpectedHexzmapHash, originalHexzmapHash, "Hexzmap.e5");

        string mapId;
        string targetMapPath;
        MapSlotCatalogEntry? targetSlot = null;
        var targetExists = false;
        var oldGridWidth = 0;
        var oldGridHeight = 0;
        HexzmapLayoutBuildResult layoutResult;

        if (request.Mode == MapSlotPublishMode.OverwriteExisting)
        {
            targetSlot = _catalogService.GetRequiredOverwriteTarget(project, request.TargetMapId);
            mapId = targetSlot.MapId;
            targetMapPath = targetSlot.MapResource!.Path;
            targetExists = true;
            oldGridWidth = targetSlot.GridWidth;
            oldGridHeight = targetSlot.GridHeight;
            var changesSize = oldGridWidth != draft.GridWidth || oldGridHeight != draft.GridHeight;
            if (changesSize && !request.AllowResizeExisting)
            {
                throw new InvalidOperationException(
                    $"草稿尺寸 {draft.GridWidth}x{draft.GridHeight} 与目标 {mapId} 的 {oldGridWidth}x{oldGridHeight} 不一致；必须明确允许同步调整目标尺寸。");
            }
            if (changesSize) EnsureVerifiedStructuralWriteEngine(project);

            layoutResult = _layoutWriter.ReplaceSegment(
                originalHexzmap,
                probe.DirectoryEntries,
                targetSlot.MapNumber,
                draft.GridWidth,
                draft.GridHeight,
                draft.TerrainCells);
        }
        else
        {
            EnsureVerifiedStructuralWriteEngine(project);
            var mapNumber = probe.DirectoryEntries.Count;
            if (mapNumber > HexzmapLayoutWriter.MaximumPublishedMapNumber)
            {
                throw new InvalidOperationException($"地图编号已达到首版上限 M{HexzmapLayoutWriter.MaximumPublishedMapNumber:000}。");
            }

            mapId = $"M{mapNumber:000}";
            var mapRoot = project.ResolveGameFile("Map");
            Directory.CreateDirectory(mapRoot);
            var conflicts = Directory.EnumerateFileSystemEntries(mapRoot, mapId + ".*", SearchOption.TopDirectoryOnly).ToList();
            if (conflicts.Count > 0)
            {
                throw new InvalidOperationException($"末尾追加目标 {mapId} 已存在冲突文件：{string.Join("；", conflicts.Select(Path.GetFileName))}");
            }

            targetMapPath = Path.Combine(mapRoot, mapId + ".JPG");
            layoutResult = _layoutWriter.AppendSegment(
                originalHexzmap,
                probe.DirectoryEntries,
                draft.GridWidth,
                draft.GridHeight,
                draft.TerrainCells);
        }

        var originalTargetHash = targetExists ? WriteOperationReportService.ComputeSha256(targetMapPath) : string.Empty;
        ValidateExpectedHash(request.ExpectedTargetHash, originalTargetHash, mapId + ".JPG");
        using var bitmap = confirmedRender == null
            ? _composeService.ComposeFinal(draft, materials)
            : new Bitmap(confirmedRender);
        var expectedPixelWidth = checked(draft.GridWidth * MapResourceItem.MapTilePixelSize);
        var expectedPixelHeight = checked(draft.GridHeight * MapResourceItem.MapTilePixelSize);
        if (bitmap.Width != expectedPixelWidth || bitmap.Height != expectedPixelHeight)
        {
            throw new InvalidOperationException(
                $"最终底图尺寸 {bitmap.Width}x{bitmap.Height} 与草稿网格要求 {expectedPixelWidth}x{expectedPixelHeight} 不一致。");
        }

        var jpegBytes = EncodeJpeg(bitmap, 92L);
        var newTargetHash = WriteOperationReportService.ComputeSha256(jpegBytes);
        var newHexzmapHash = WriteOperationReportService.ComputeSha256(layoutResult.Bytes);
        if (targetExists && originalTargetHash.Equals(newTargetHash, StringComparison.OrdinalIgnoreCase) &&
            originalHexzmapHash.Equals(newHexzmapHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标底图和地形块已经与草稿完全一致，无需发布。");
        }

        var resizePreview = BuildResizePreview(draft, oldGridWidth, oldGridHeight);
        if (resizePreview.IsDestructive && !request.ConfirmDestructiveCrop &&
            request.Mode == MapSlotPublishMode.OverwriteExisting &&
            (draft.GridWidth < oldGridWidth || draft.GridHeight < oldGridHeight))
        {
            throw new InvalidOperationException("目标地图会缩小；必须在预览中明确确认裁剪后才能发布。");
        }

        return new MapSlotPublishPlan
        {
            Mode = request.Mode,
            TargetMapId = mapId,
            TargetMapPath = targetMapPath,
            HexzmapPath = hexzmapPath,
            TargetMapExists = targetExists,
            ResizesExisting = targetExists && (oldGridWidth != draft.GridWidth || oldGridHeight != draft.GridHeight),
            OldGridWidth = oldGridWidth,
            OldGridHeight = oldGridHeight,
            NewGridWidth = draft.GridWidth,
            NewGridHeight = draft.GridHeight,
            OldDirectoryEntryCount = layoutResult.OldEntryCount,
            NewDirectoryEntryCount = layoutResult.NewEntryCount,
            OldTerrainSegmentLength = layoutResult.OldSegmentLength,
            NewTerrainSegmentLength = layoutResult.NewSegmentLength,
            DirectoryGrowthBytes = layoutResult.DirectoryGrowthBytes,
            NewTerrainSegmentOffset = layoutResult.NewSegmentOffset,
            ExpectedTargetHash = originalTargetHash,
            ExpectedHexzmapHash = originalHexzmapHash,
            NewTargetHash = newTargetHash,
            NewHexzmapHash = newHexzmapHash,
            JpegBytes = jpegBytes,
            HexzmapBytes = layoutResult.Bytes,
            ResizePreview = resizePreview,
            Summary = request.Mode == MapSlotPublishMode.AppendNew
                ? $"末尾追加 {mapId}，尺寸 {draft.GridWidth}x{draft.GridHeight}，Hexzmap 目录 {layoutResult.OldEntryCount} -> {layoutResult.NewEntryCount}。"
                : $"覆盖 {mapId}，尺寸 {oldGridWidth}x{oldGridHeight} -> {draft.GridWidth}x{draft.GridHeight}。"
        };
    }

    public MapSlotPublishResult Apply(CczProject project, MapWorkbenchDraft draft, MapSlotPublishPlan plan)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.JpegBytes.Length == 0 || plan.HexzmapBytes.Length == 0)
        {
            throw new InvalidOperationException("发布计划不包含完整的 JPG 或 Hexzmap 输出。");
        }

        var currentHexHash = WriteOperationReportService.ComputeSha256(plan.HexzmapPath);
        ValidateExpectedHash(plan.ExpectedHexzmapHash, currentHexHash, "Hexzmap.e5");
        if (plan.TargetMapExists)
        {
            if (!File.Exists(plan.TargetMapPath)) throw new FileNotFoundException("目标地图在发布前消失。", plan.TargetMapPath);
            ValidateExpectedHash(plan.ExpectedTargetHash, WriteOperationReportService.ComputeSha256(plan.TargetMapPath), plan.TargetMapId + ".JPG");
        }
        else if (File.Exists(plan.TargetMapPath))
        {
            throw new InvalidOperationException("末尾追加目标在预览后已被其他操作创建：" + plan.TargetMapPath);
        }

        var originalMap = plan.TargetMapExists ? File.ReadAllBytes(plan.TargetMapPath) : Array.Empty<byte>();
        var originalHexzmap = File.ReadAllBytes(plan.HexzmapPath);
        var draftPath = _draftService.GetDraftPath(project, draft.DraftId);
        var draftExisted = File.Exists(draftPath);
        var originalDraftBytes = draftExisted ? File.ReadAllBytes(draftPath) : Array.Empty<byte>();
        var originalBoundMapId = draft.BoundMapId;
        var originalBinding = CloneBinding(draft.HexzmapBinding);
        var mapBackup = plan.TargetMapExists ? CreateBackup(project, plan.TargetMapPath) : string.Empty;
        var hexBackup = CreateBackup(project, plan.HexzmapPath);
        var mapTemp = BuildTempPath(plan.TargetMapPath);
        var hexTemp = BuildTempPath(plan.HexzmapPath);
        var mapCommitted = false;
        var hexCommitted = false;
        var draftCommitted = false;

        try
        {
            StageAndVerifyMap(mapTemp, plan.JpegBytes, plan.NewGridWidth, plan.NewGridHeight, plan.NewTargetHash);
            StageAndVerifyBytes(hexTemp, plan.HexzmapBytes, plan.NewHexzmapHash, "Hexzmap");
            File.Move(mapTemp, plan.TargetMapPath, overwrite: plan.TargetMapExists);
            mapCommitted = true;
            _faultInjector?.Invoke(MapSlotPublishFaultPoint.AfterMapCommit);
            File.Move(hexTemp, plan.HexzmapPath, overwrite: true);
            hexCommitted = true;
            _faultInjector?.Invoke(MapSlotPublishFaultPoint.AfterHexzmapCommit);

            _faultInjector?.Invoke(MapSlotPublishFaultPoint.BeforeRereadVerification);
            VerifyCommitted(project, plan);
            draft.BoundMapId = plan.TargetMapId;
            draft.HexzmapBinding = new HexzmapBlockBinding
            {
                MapId = plan.TargetMapId,
                DirectoryEntryIndex = int.Parse(plan.TargetMapId[1..], CultureInfo.InvariantCulture),
                Width = plan.NewGridWidth,
                Height = plan.NewGridHeight,
                Source = HexzmapBindingSource.ExactMapNumber,
                Confidence = 1f,
                Evidence = "地图发布事务写后复读确认编号、尺寸和目录项一致。"
            };
            draftCommitted = true;
            _draftService.SaveDraft(project, draft);
            var reportPath = WriteReport(project, plan, mapBackup, hexBackup);

            return new MapSlotPublishResult
            {
                Mode = plan.Mode,
                MapId = plan.TargetMapId,
                MapPath = plan.TargetMapPath,
                HexzmapPath = plan.HexzmapPath,
                MapBackupPath = mapBackup,
                HexzmapBackupPath = hexBackup,
                ReportJsonPath = reportPath,
                DraftPath = draftPath,
                GridWidth = plan.NewGridWidth,
                GridHeight = plan.NewGridHeight,
                DirectoryEntryCount = plan.NewDirectoryEntryCount,
                MapSha256 = plan.NewTargetHash,
                HexzmapSha256 = plan.NewHexzmapHash
            };
        }
        catch (Exception publishError)
        {
            var rollbackErrors = new List<Exception>();
            if (mapCommitted)
            {
                TryRollbackMap(plan.TargetMapPath, plan.TargetMapExists, originalMap, rollbackErrors);
            }
            if (hexCommitted)
            {
                TryRestoreBytes(plan.HexzmapPath, originalHexzmap, rollbackErrors);
            }
            if (draftCommitted)
            {
                TryRollbackMap(draftPath, draftExisted, originalDraftBytes, rollbackErrors);
            }
            draft.BoundMapId = originalBoundMapId;
            draft.HexzmapBinding = originalBinding;

            if (rollbackErrors.Count > 0)
            {
                throw new AggregateException("地图发布失败，且回滚未完全成功。", new[] { publishError }.Concat(rollbackErrors));
            }

            throw new InvalidOperationException("地图发布失败；底图与 Hexzmap 已恢复到发布前状态。", publishError);
        }
        finally
        {
            TryDelete(mapTemp);
            TryDelete(hexTemp);
        }
    }

    public static MapResizePreview BuildResizePreview(MapWorkbenchDraft draft, int comparisonWidth, int comparisonHeight)
    {
        if (comparisonWidth <= 0 || comparisonHeight <= 0)
        {
            return new MapResizePreview
            {
                NewWidth = draft.GridWidth,
                NewHeight = draft.GridHeight,
                AddedCells = draft.CellCount
            };
        }

        var overlap = Math.Min(comparisonWidth, draft.GridWidth) * Math.Min(comparisonHeight, draft.GridHeight);
        return new MapResizePreview
        {
            OldWidth = comparisonWidth,
            OldHeight = comparisonHeight,
            NewWidth = draft.GridWidth,
            NewHeight = draft.GridHeight,
            AddedCells = Math.Max(0, draft.CellCount - overlap),
            RemovedCells = Math.Max(0, comparisonWidth * comparisonHeight - overlap)
        };
    }

    private void VerifyCommitted(CczProject project, MapSlotPublishPlan plan)
    {
        if (!WriteOperationReportService.ComputeSha256(plan.TargetMapPath).Equals(plan.NewTargetHash, StringComparison.OrdinalIgnoreCase) ||
            !WriteOperationReportService.ComputeSha256(plan.HexzmapPath).Equals(plan.NewHexzmapHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("发布后的文件哈希与预览计划不一致。");
        }

        using (var image = Image.FromFile(plan.TargetMapPath))
        {
            if (image.Width != plan.NewGridWidth * MapResourceItem.MapTilePixelSize ||
                image.Height != plan.NewGridHeight * MapResourceItem.MapTilePixelSize)
            {
                throw new InvalidOperationException("发布后的 JPG 尺寸复读失败。");
            }
        }

        var slots = _catalogService.List(project);
        var target = slots.FirstOrDefault(item => item.MapId.Equals(plan.TargetMapId, StringComparison.OrdinalIgnoreCase));
        if (target?.State != MapSlotState.Complete || target.GridWidth != plan.NewGridWidth || target.GridHeight != plan.NewGridHeight)
        {
            throw new InvalidOperationException("发布后的地图槽没有复读为完整且尺寸一致的状态。");
        }

        var probe = _hexzmapProbeReader.Read(project);
        if (probe.DirectoryEntries.Count != plan.NewDirectoryEntryCount)
        {
            throw new InvalidOperationException("发布后的 Hexzmap 目录数量复读失败。");
        }
    }

    private string WriteReport(CczProject project, MapSlotPublishPlan plan, string mapBackup, string hexBackup)
    {
        var report = new WriteOperationReport
        {
            OperationKind = "地图槽组合发布",
            SourceAction = plan.Mode == MapSlotPublishMode.AppendNew ? "地图编辑器末尾追加地图" : "地图编辑器覆盖已有地图",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, plan.TargetMapPath),
            TargetPath = plan.TargetMapPath,
            BackupPath = string.IsNullOrWhiteSpace(mapBackup) ? hexBackup : mapBackup,
            BeforeSha256 = plan.ExpectedTargetHash,
            AfterSha256 = plan.NewTargetHash,
            ChangedBytes = Math.Max(plan.JpegBytes.Length, plan.HexzmapBytes.Length),
            Summary = plan.Summary,
            SafetyNotes = "JPG 与 Hexzmap 通过同一预览计划提交；任一步骤失败会恢复已有文件并删除本次新增底图。",
            FormatCheckSummary = $"JPG={plan.NewGridWidth * 48}x{plan.NewGridHeight * 48}；Hexzmap目录={plan.OldDirectoryEntryCount}->{plan.NewDirectoryEntryCount}",
            RiskSummary = plan.ResizesExisting ? "目标地图尺寸发生变化；请复核所有引用该地图的战场坐标。" : string.Empty,
            Changes =
            [
                new WriteOperationChange
                {
                    Category = "Map地图底图",
                    TableName = plan.TargetMapId,
                    ColumnName = Path.GetFileName(plan.TargetMapPath),
                    OffsetHex = "整文件",
                    ByteLength = plan.JpegBytes.Length,
                    OldValue = plan.TargetMapExists ? plan.ExpectedTargetHash : "不存在",
                    NewValue = plan.NewTargetHash,
                    Annotation = plan.Mode == MapSlotPublishMode.AppendNew ? "新建末尾地图底图。" : "覆盖已有地图底图。"
                },
                new WriteOperationChange
                {
                    Category = "Hexzmap地形目录与数据段",
                    TableName = "Hexzmap.e5",
                    ColumnName = plan.TargetMapId,
                    OffsetHex = "0x110目录/目标数据段",
                    ByteLength = plan.HexzmapBytes.Length,
                    OldValue = plan.ExpectedHexzmapHash,
                    NewValue = plan.NewHexzmapHash,
                    Annotation = $"目录项 {plan.OldDirectoryEntryCount}->{plan.NewDirectoryEntryCount}；尺寸 {plan.OldGridWidth}x{plan.OldGridHeight}->{plan.NewGridWidth}x{plan.NewGridHeight}。"
                }
            ],
            Metadata =
            {
                ["Mode"] = plan.Mode.ToString(),
                ["MapId"] = plan.TargetMapId,
                ["MapBackupPath"] = mapBackup,
                ["HexzmapBackupPath"] = hexBackup,
                ["OldDirectoryEntryCount"] = plan.OldDirectoryEntryCount.ToString(CultureInfo.InvariantCulture),
                ["NewDirectoryEntryCount"] = plan.NewDirectoryEntryCount.ToString(CultureInfo.InvariantCulture),
                ["OldGrid"] = $"{plan.OldGridWidth}x{plan.OldGridHeight}",
                ["NewGrid"] = $"{plan.NewGridWidth}x{plan.NewGridHeight}"
            }
        };
        return _reportService.WriteJsonReport(report, string.IsNullOrWhiteSpace(mapBackup) ? hexBackup : mapBackup);
    }

    private static void ValidateDraft(MapWorkbenchDraft draft)
    {
        if (draft.GridWidth is < 1 or > HexzmapLayoutWriter.MaximumPublishedGridSize ||
            draft.GridHeight is < 1 or > HexzmapLayoutWriter.MaximumPublishedGridSize)
        {
            throw new InvalidOperationException($"首版项目发布尺寸必须在 1x1 到 {HexzmapLayoutWriter.MaximumPublishedGridSize}x{HexzmapLayoutWriter.MaximumPublishedGridSize} 格之间。");
        }
        if (draft.TerrainCells.Length != draft.CellCount)
        {
            throw new InvalidOperationException($"草稿地形长度 {draft.TerrainCells.Length} 与网格 {draft.GridWidth}x{draft.GridHeight} 不一致。");
        }
    }

    private static void EnsureVerifiedStructuralWriteEngine(CczProject project)
    {
        var executablePath = project.ResolveGameFile("Ekd5.exe");
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException("找不到 Ekd5.exe；未知引擎只允许同尺寸覆盖地图。");
        }

        var hash = WriteOperationReportService.ComputeSha256(executablePath);
        if (!hash.Equals(Verified65ExecutableSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"当前引擎尚未通过地图目录扩容验证（Ekd5.exe SHA256={hash}）；只允许同尺寸覆盖地图。");
        }
    }

    private static void ValidateExpectedHash(string expected, string actual, string targetName)
    {
        if (!string.IsNullOrWhiteSpace(expected) && !expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{targetName} 在预览后已经变化，请重新预览再发布。");
        }
    }

    private static byte[] EncodeJpeg(Image image, long quality)
    {
        using var stream = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(item => item.FormatID == ImageFormat.Jpeg.Guid);
        if (codec == null)
        {
            image.Save(stream, ImageFormat.Jpeg);
            return stream.ToArray();
        }
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        image.Save(stream, codec, parameters);
        return stream.ToArray();
    }

    private static void StageAndVerifyMap(string path, byte[] bytes, int width, int height, string expectedHash)
    {
        File.WriteAllBytes(path, bytes);
        StageAndVerifyBytes(path, bytes, expectedHash, "JPG");
        using var image = Image.FromFile(path);
        if (image.Width != width * MapResourceItem.MapTilePixelSize || image.Height != height * MapResourceItem.MapTilePixelSize)
        {
            throw new InvalidOperationException("临时 JPG 尺寸校验失败。");
        }
    }

    private static void StageAndVerifyBytes(string path, byte[] bytes, string expectedHash, string name)
    {
        File.WriteAllBytes(path, bytes);
        var reread = File.ReadAllBytes(path);
        if (!reread.AsSpan().SequenceEqual(bytes) ||
            !WriteOperationReportService.ComputeSha256(reread).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"临时 {name} 文件复读校验失败。");
        }
    }

    private static string CreateBackup(CczProject project, string path)
    {
        var root = ProjectBackupPathService.EnsureBackupRootWritable(project);
        Directory.CreateDirectory(root);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backup = Path.Combine(root, $"{stamp}_{Path.GetFileName(path)}");
        var suffix = 1;
        while (File.Exists(backup)) backup = Path.Combine(root, $"{stamp}_{suffix++}_{Path.GetFileName(path)}");
        File.Copy(path, backup, overwrite: false);
        return backup;
    }

    private static string BuildTempPath(string targetPath)
        => Path.Combine(Path.GetDirectoryName(targetPath)!, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.publish.tmp");

    private static void TryRollbackMap(string path, bool existed, byte[] original, List<Exception> errors)
    {
        try
        {
            if (existed) File.WriteAllBytes(path, original);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private static void TryRestoreBytes(string path, byte[] original, List<Exception> errors)
    {
        try
        {
            File.WriteAllBytes(path, original);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static HexzmapBlockBinding? CloneBinding(HexzmapBlockBinding? source)
        => source == null
            ? null
            : new HexzmapBlockBinding
            {
                MapId = source.MapId,
                DirectoryEntryIndex = source.DirectoryEntryIndex,
                Width = source.Width,
                Height = source.Height,
                Source = source.Source,
                Confidence = source.Confidence,
                UserConfirmed = source.UserConfirmed,
                Evidence = source.Evidence
            };
}
