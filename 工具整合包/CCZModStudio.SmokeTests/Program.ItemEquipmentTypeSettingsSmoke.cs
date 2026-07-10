using CCZModStudio.Core;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunItemEquipmentTypeSettingsSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        using var temp = new TemporarySmokeDirectory("ItemEquipmentTypeSettings");
        var gameRoot = Path.Combine(temp.Path, "Game");
        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(Path.Combine(gameRoot, "_CCZModStudio_TestCopy.txt"), "Item equipment type smoke.");

        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5" })
        {
            var source = project.ResolveGameFile(fileName);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("装备类型烟测缺少核心文件。", source);
            }

            File.Copy(source, Path.Combine(gameRoot, fileName), overwrite: false);
        }

        var exePath = Path.Combine(gameRoot, "Ekd5.exe");
        var exeBytes = File.ReadAllBytes(exePath);
        var oldNameBytes = EncodingService.EncodeFixedString("旧", 4);
        Buffer.BlockCopy(oldNameBytes, 0, exeBytes, 0x8AC70, oldNameBytes.Length);
        exeBytes[0x8AC74] = 0x7E;
        exeBytes[0x81827] = 0x00;
        File.WriteAllBytes(exePath, exeBytes);

        var testProject = new CczProject
        {
            WorkspaceRoot = temp.Path,
            GameRoot = gameRoot,
            HexTableXmlPath = project.HexTableXmlPath
        };

        var service = new ItemEquipmentTypeSettingsService();
        var document = service.Load(testProject, tables);
        if (document.Rows.Count != 15)
        {
            throw new InvalidOperationException("装备类型应读取 15 行。");
        }

        var first = document.Rows.Single(row => row.RowIndex == 0);
        if (!first.HasDisplayToggle ||
            first.JobPermissions.Count == 0 ||
            first.JobPermissions.All(permission => permission.State is not (ItemEquipmentPermissionState.Checked or ItemEquipmentPermissionState.Unchecked or ItemEquipmentPermissionState.Indeterminate)))
        {
            throw new InvalidOperationException("装备类型首行没有读取显示开关或可装备部队。");
        }
        AssertItemEquipmentTypeJobSeriesMapping(first.JobPermissions);

        var firstJob = first.JobPermissions.First();
        var targetAllowed = firstJob.State != ItemEquipmentPermissionState.Checked;
        var update = new ItemEquipmentTypeSettingsUpdate
        {
            Names = { [0] = "测" },
            Visibility = { [0] = false },
            JobPermissions =
            {
                [0] = new Dictionary<int, bool>
                {
                    [firstJob.JobId] = targetAllowed
                }
            }
        };

        var preview = service.Preview(testProject, tables, update);
        if (preview.All(change => change.Area != "名称") ||
            preview.All(change => change.Area != "显示") ||
            preview.All(change => change.Area != "可装备部队"))
        {
            throw new InvalidOperationException("装备类型预览缺少名称、显示或可装备部队改动。");
        }

        var save = service.Save(testProject, tables, update);
        var written = File.ReadAllBytes(exePath);
        if (EncodingService.DecodeFixedString(written.AsSpan(0x8AC70, 4)) != "测" ||
            written[0x8AC74] != 0x7E ||
            written[0x81827] != 0xFF ||
            string.IsNullOrWhiteSpace(save.ExeBackupPath) ||
            string.IsNullOrWhiteSpace(save.ExeReportJsonPath) ||
            !File.Exists(save.ExeBackupPath) ||
            !File.Exists(save.ExeReportJsonPath) ||
            save.TableSaves.Count == 0 ||
            save.TableSaves.Any(item => string.IsNullOrWhiteSpace(item.BackupPath) || !File.Exists(item.BackupPath)))
        {
            throw new InvalidOperationException("装备类型保存没有正确写入名称、隐藏值、备份或报告。");
        }

        var reread = service.Load(testProject, tables);
        var rereadFirst = reread.Rows.Single(row => row.RowIndex == 0);
        var rereadJob = rereadFirst.JobPermissions.Single(permission => permission.JobId == firstJob.JobId);
        if (rereadFirst.IsVisible ||
            rereadJob.State != (targetAllowed ? ItemEquipmentPermissionState.Checked : ItemEquipmentPermissionState.Unchecked))
        {
            throw new InvalidOperationException("装备类型复读没有对齐显示状态或配对许可槽。");
        }

        var profile = new ProjectEquipmentTypeProfileService().Build(
            testProject,
            tables,
            Enumerable.Range(0, ProjectEquipmentTypeProfileService.JobPermissionSlotCount)
                .Select(index => index % 2 == 0 ? "普通剑" : "特殊剑")
                .ToArray());
        if (profile.GetTypeOrFallback(0).ShortDisplayName != "普通测" ||
            profile.GetTypeOrFallback(1).ShortDisplayName != "特殊测")
        {
            throw new InvalidOperationException("装备类型名称没有同步为 data 设定类型码显示来源并保留普通/特殊前缀。");
        }

        var restoreVisible = service.Save(testProject, tables, new ItemEquipmentTypeSettingsUpdate
        {
            Visibility = { [0] = true }
        });
        written = File.ReadAllBytes(exePath);
        if (written[0x81827] != 0x00 || restoreVisible.ChangedFieldCount == 0)
        {
            throw new InvalidOperationException("装备类型显示重新勾选没有写回正常值 00。");
        }

        try
        {
            service.Preview(testProject, tables, new ItemEquipmentTypeSettingsUpdate
            {
                Names = { [0] = "超长名称" }
            });
            throw new InvalidOperationException("装备类型名称超过 4 字节 GBK 时应被拒绝。");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("最多 4 字节 GBK", StringComparison.Ordinal))
        {
        }

        Console.WriteLine($"ITEM_EQUIPMENT_TYPE_SETTINGS_SMOKE_OK rows={document.Rows.Count} changed={save.ChangedFieldCount} bytes={save.ChangedBytes}");
    }

    private static void AssertItemEquipmentTypeJobSeriesMapping(IReadOnlyList<ItemEquipmentTypeJobPermission> permissions)
    {
        var byJobId = permissions.ToDictionary(permission => permission.JobId);
        foreach (var (jobId, expectedSeriesId) in new[]
                 {
                     (0, 0),
                     (1, 0),
                     (2, 0),
                     (3, 1),
                     (59, 19),
                     (60, 20),
                     (79, 39)
                 })
        {
            if (!byJobId.TryGetValue(jobId, out var permission))
            {
                throw new InvalidOperationException($"装备类型可装备部队缺少兵种 ID={jobId}。");
            }

            if (permission.SeriesId != expectedSeriesId)
            {
                throw new InvalidOperationException($"装备类型可装备部队兵种系映射错误：兵种 ID={jobId} 应属于兵种系 {expectedSeriesId}，实际 {permission.SeriesId}。");
            }

            if (string.IsNullOrWhiteSpace(permission.SeriesName))
            {
                throw new InvalidOperationException($"装备类型可装备部队兵种系名称为空：兵种 ID={jobId}，兵种系 {expectedSeriesId}。");
            }
        }
    }
}
