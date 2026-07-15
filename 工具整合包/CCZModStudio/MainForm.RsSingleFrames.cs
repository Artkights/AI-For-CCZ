using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private void OpenRsSingleFrameCatalog(
        CczProject project,
        ImageAssignmentResourceKind kind,
        int imageId,
        int? jobId,
        int factionSlot,
        int stageSlot,
        string displayName,
        bool editable,
        string sharedUsageWarning = "")
    {
        var service = new RsSingleFrameCatalogService();
        RsFrameCatalog Factory(int selectedStage, int selectedFaction) => kind == ImageAssignmentResourceKind.R
            ? service.BuildR(project, imageId, displayName)
            : service.BuildS(project, imageId, jobId, selectedFaction, selectedStage, displayName);
        RsSingleFrameViewerDialog.TryShowOwned(
            this,
            () => Factory(stageSlot, factionSlot),
            Factory,
            editable && ReferenceEquals(project, _project)
                ? descriptor => EditAndWriteSingleRsFrame(descriptor, sharedUsageWarning)
                : null,
            "读取 R/S 单帧失败",
            "Open R/S single-frame viewer");
    }

    private void OpenSelectedRSceneSingleFrames()
    {
        if (_project == null || _rSceneActorListBox.SelectedItem is not RSceneActorPaletteItem item)
        {
            MessageBox.Show(this, "请先在 R 场景角色列表选择人物。", "查看全部R帧", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var service = new RsSingleFrameCatalogService();
        RsFrameCatalog Factory(int _, int __) => service.BuildR(_project, item.RImageId, item.Name);
        RsSingleFrameViewerDialog.TryShowOwned(
            this,
            () => Factory(1, 1),
            Factory,
            descriptor => EditAndWriteSingleRsFrame(descriptor, $"R={item.RImageId} 可能被多个人物或场景角色共享引用。"),
            "读取 R 场景单帧失败",
            "Open R-scene single-frame viewer");
    }

    private void OpenSelectedBattlefieldSingleFrames()
    {
        if (_project == null || _battlefieldUnitListBox.SelectedItem is not BattlefieldUnitPaletteItem item)
        {
            MessageBox.Show(this, "请先在战场角色列表选择人物。", "查看全部S帧", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var faction = GetBattlefieldFactionSlot(GetSelectedBattlefieldFaction());
        var stage = NormalizeBattlefieldLevelMode(_battlefieldLevelModeCombo.SelectedItem?.ToString() ?? "初级") switch
        {
            "中级" => 2,
            "高级" => 3,
            _ => 1
        };
        var service = new RsSingleFrameCatalogService();
        RsFrameCatalog Factory(int stageSlot, int factionSlot) => service.BuildS(
            _project, item.SImageId, item.JobId, factionSlot, stageSlot, item.Name);
        RsSingleFrameViewerDialog.TryShowOwned(
            this,
            () => Factory(stage, faction),
            Factory,
            descriptor => EditAndWriteSingleRsFrame(descriptor, $"S={item.SImageId} 的 Unit 图可能被多个人物、兵种或转级共享引用。"),
            "读取战场单帧失败",
            "Open battlefield single-frame viewer");
    }

    private void OpenSelectedJobSSingleFrames()
    {
        if (_project == null || _currentJobEditorData == null || _jobEditorGrid.CurrentRow == null)
        {
            MessageBox.Show(this, "请先读取兵种并选择一个详细兵种。", "查看单帧S", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!TryGetJobEditorRowIdentity(_jobEditorGrid.CurrentRow, out var jobId, out var jobName))
        {
            MessageBox.Show(this, "当前详细兵种行无法解析 ID。", "查看单帧S", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var catalogService = new RsSingleFrameCatalogService();
        RsFrameCatalog Factory(int _, int slot) => catalogService.BuildS(_project, 0, jobId, slot, 1, jobName);
        RsSingleFrameViewerDialog.TryShowOwned(
            this,
            () => Factory(1, CharacterImageResourceService.DefaultSPreviewFactionSlot),
            Factory,
            descriptor => EditAndWriteSingleRsFrame(descriptor, "S=0 的兵种 Unit 图可能被同职业、同阵营的多个人物共享引用。"),
            "读取兵种单帧S失败",
            "Open job S single-frame viewer");
    }

    private void OpenSelectedImageResourceSingleFrames()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "查看单帧", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var entry = GetSelectedImageResourceEntry();
        if (entry == null)
        {
            MessageBox.Show(this, "请先选择一个图片资源条目。", "查看单帧", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (new RsFrameLayoutResolver().TryResolve(entry.FileName) == null)
        {
            MessageBox.Show(this, "当前条目不是 Pmapobj.e5 或 Unit_mov/atk/spc.e5，无法按 R/S 物理帧规格切分。", "查看单帧", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var catalogService = new RsSingleFrameCatalogService();
        RsFrameCatalog Factory(int _, int __) => catalogService.BuildResourceEntry(
            _project, entry.Path, entry.ImageNumber, $"{entry.FileName} #{entry.ImageNumber} {entry.Usage}");
        RsSingleFrameViewerDialog.TryShowOwned(
            this,
            () => Factory(1, 1),
            Factory,
            descriptor => EditAndWriteSingleRsFrame(descriptor, "图片资源条目可能被多个人物、兵种或转级共享引用。"),
            "读取资源单帧失败",
            "Open image-resource single-frame viewer");
    }

    private void OpenSelectedImageAssignmentSingleFrames(ImageAssignmentResourceKind kind)
    {
        if (_project == null || _currentImageAssignments == null)
        {
            MessageBox.Show(this, "请先读取人物形象设定。", "查看 R/S 单帧", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var row = GetSelectedImageAssignmentRow();
        if (row == null)
        {
            MessageBox.Show(this, "请先在人物形象设定页面选择一行。", "查看 R/S 单帧", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryGetImageResourceId(row, kind, out var imageId) || imageId < 0)
        {
            MessageBox.Show(this, $"当前人物没有有效的 {GetImageAssignmentResourceKindText(kind)} 编号。", "查看 R/S 单帧", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var jobId = kind == ImageAssignmentResourceKind.S ? TryGetImageAssignmentJobId(row) : null;
        if (kind == ImageAssignmentResourceKind.S && imageId == 0 && !jobId.HasValue)
        {
            MessageBox.Show(this, "S=0 必须依赖当前人物职业解析 Unit 图号，但当前行没有有效职业。", "查看单帧S", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var displayName = TryGetRoleDisplayName(row);
        var initialFaction = GetImageAssignmentSPreviewFactionSlot();
        var stages = kind == ImageAssignmentResourceKind.S
            ? CharacterImageResourceService.GetAvailableSImageStageSlots(_project, imageId)
            : Array.Empty<int>();
        var initialStage = stages.FirstOrDefault();
        if (initialStage <= 0) initialStage = 1;
        var catalogService = new RsSingleFrameCatalogService();

        RsFrameCatalog Factory(int stage, int faction)
            => kind == ImageAssignmentResourceKind.R
                ? catalogService.BuildR(_project, imageId, displayName)
                : catalogService.BuildS(_project, imageId, jobId, faction, stage, displayName);

        var rId = TryGetImageResourceId(row, ImageAssignmentResourceKind.R, out var currentR) ? currentR : -1;
        var sId = TryGetImageResourceId(row, ImageAssignmentResourceKind.S, out var currentS) ? currentS : -1;
        var sharedWarning = BuildCharacterPixelGroupSharedUsageWarning(row, rId, sId, jobId);
        RsSingleFrameViewerDialog.TryShowOwned(
            this,
            () => Factory(initialStage, initialFaction),
            Factory,
            descriptor => EditAndWriteSingleRsFrame(descriptor, sharedWarning),
            "读取 R/S 单帧失败",
            "Open character R/S single-frame viewer");
    }

    private bool EditAndWriteSingleRsFrame(RsFrameDescriptor descriptor, string sharedUsageWarning)
    {
        if (_project == null) return false;
        var service = new RsSingleFrameEditWriteService();
        try
        {
            if (!string.IsNullOrWhiteSpace(sharedUsageWarning) &&
                MessageBox.Show(this,
                    sharedUsageWarning + "\r\n\r\n当前将只编辑所选物理帧。是否打开像素编辑器？",
                    "共享 R/S 资源提示",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning) != DialogResult.OK)
                return false;
            using var document = service.CreateEditDocument(_project, descriptor);
            using var editor = new PixelImageEditorDialog(document, singleFrameMode: true);
            if (editor.ShowDialog(this) != DialogResult.OK) return false;

            if (BitmapsEqual(document.OriginalBitmap, document.Bitmap))
            {
                MessageBox.Show(this, "当前帧没有变化；未创建备份，也没有修改 E5。", "单帧编辑", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            Cursor = Cursors.WaitCursor;
            using var preview = service.Preview(_project, descriptor, document.Bitmap);
            Cursor = Cursors.Default;
            if (preview.ChangedPixels <= 0)
            {
                MessageBox.Show(this, "当前帧没有变化；未创建备份，也没有修改 E5。", "单帧编辑", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            var previewText = preview.BuildText() +
                              $"\r\nE5 预计变化字节：{preview.E5Preview.ChangedBytesEstimate:N0}" +
                              $"\r\n档案大小：{preview.E5Preview.OldFileSizeBytes:N0} → {preview.E5Preview.NewFileSizeBytes:N0}（{preview.E5Preview.FileSizeDeltaBytes:+#,0;-#,0;0}）" +
                              $"\r\n存储格式：{preview.E5Preview.Operations.FirstOrDefault()?.OldKind ?? "未知"} -> {preview.E5Preview.Operations.FirstOrDefault()?.NewKind ?? "未知"}" +
                              $"\r\n条目偏移：0x{preview.E5Preview.Operations.FirstOrDefault()?.OldDataOffset ?? 0:X} → 0x{preview.E5Preview.Operations.FirstOrDefault()?.NewDataOffset ?? 0:X}" +
                              $"\r\n条目大小：{preview.E5Preview.Operations.FirstOrDefault()?.OldSizeBytes ?? 0:N0} → {preview.E5Preview.Operations.FirstOrDefault()?.NewSizeBytes ?? 0:N0}" +
                              (string.IsNullOrWhiteSpace(sharedUsageWarning) ? string.Empty : "\r\n\r\n" + sharedUsageWarning) +
                              "\r\n\r\n确认后将先自动备份目标 E5，再只替换当前图号条目并复读校验。是否继续？";
            if (MessageBox.Show(this,
                    previewText,
                    "确认 R/S 单帧写回",
                    MessageBoxButtons.YesNo,
                    _project.IsTestCopy ? MessageBoxIcon.Question : MessageBoxIcon.Warning) != DialogResult.Yes)
                return false;

            Cursor = Cursors.WaitCursor;
            var result = service.Write(_project, preview);
            ClearRsPreviewCachesAfterSingleFrameWrite();
            Cursor = Cursors.Default;
            MessageBox.Show(this,
                $"R/S 单帧写回完成。\r\n资源：{Path.GetFileName(descriptor.TargetPath)} #{descriptor.ImageNumber}\r\n" +
                $"物理帧：{descriptor.Group} / {descriptor.PhysicalFrameIndex}\r\n" +
                $"备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}",
                "单帧写回完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            SetStatus($"单帧写回完成：{Path.GetFileName(descriptor.TargetPath)} #{descriptor.ImageNumber} 帧{descriptor.PhysicalFrameIndex}");
            return true;
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            System.Diagnostics.Debug.WriteLine("R/S 单帧编辑写回失败：" + ex);
            MessageBox.Show(this, ex.Message, "R/S 单帧编辑失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void ClearRsPreviewCachesAfterSingleFrameWrite()
    {
        if (_project != null)
        {
            var paths = new[]
            {
                CharacterImageResourceService.ResolveGameFile(_project, "Pmapobj.e5"),
                CharacterImageResourceService.ResolveGameFile(_project, "Unit_mov.e5"),
                CharacterImageResourceService.ResolveGameFile(_project, "Unit_atk.e5"),
                CharacterImageResourceService.ResolveGameFile(_project, "Unit_spc.e5")
            };
            E5ImageReadSessionPool.Shared.Invalidate(paths);
            ImagePreviewCache.Shared.Invalidate(paths);
            _imageAssignmentPreviewService.InvalidateResources(paths);
            ProjectResourceInvalidationBus.Publish(paths, ProjectResourceKind.Image);
        }
        ClearImageAssignmentCaches();
        _imageResourceCatalogService.ClearCache();
        _itemIconPreviewService.ClearCache();
        ClearRSceneImageCache();
        ClearBattlefieldUnitFrameCache();
        ShowSelectedImageAssignmentDetail();
        _imageResourcePreviewBox.Invalidate();
        _rSceneCanvasBox.Invalidate();
        _battlefieldMapPreviewBox.Invalidate();
        _battlefieldUnitPreviewBox.Invalidate();
    }

    private static bool BitmapsEqual(Bitmap left, Bitmap right)
    {
        if (left.Size != right.Size) return false;
        for (var y = 0; y < left.Height; y++)
        for (var x = 0; x < left.Width; x++)
        {
            if (left.GetPixel(x, y).ToArgb() != right.GetPixel(x, y).ToArgb()) return false;
        }
        return true;
    }
}
