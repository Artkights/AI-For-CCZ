using System.Data;
using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class GlobalSettingsService
{
    private const long GameTitleOffset = 0x8D3C4;
    private const int GameTitleCapacityBytes = 32;
    private const string GameTitleSource = "本地 6.5 Ekd5.exe 样本：GBK 文本“【三国志曹操传 加强版】”位于 0x8D3C4，32 字节定长区。";

    private static readonly IReadOnlyList<GlobalNumericSetting> NumericSettingCatalog =
    [
        Pending("AbilityDisplay", "能力显示（单数/双数）", "单数", "旧形象指定器全局设定截图；当前缺少 6.5 可靠偏移。"),
        Pending("AbilityThreshold", "能力条件", "135 / 144 / 144 / 144", "旧形象指定器全局设定截图；当前缺少 6.5 可靠偏移。"),
        Pending("PromotionLevel", "转职等级（一转/二转）", "20 / 40", "旧形象指定器全局设定截图；当前缺少 6.5 可靠偏移。"),
        Pending("LevelAndExp", "等级上限 / 升级经验", "60 / 73", "旧形象指定器全局设定截图；当前缺少 6.5 可靠偏移。"),
        Pending("EquipmentExp", "普装/特装升级经验", "150 / 200", "旧形象指定器全局设定截图；当前缺少 6.5 可靠偏移。"),
        Pending("EquipmentLevelLimit", "普装/特装等级上限", "5 / 9", "旧形象指定器全局设定截图；当前缺少 6.5 可靠偏移。"),
        Pending("Merit", "新加/敌武将功勋", "25 / 25", "旧形象指定器全局设定截图；当前缺少 6.5 可靠偏移。"),
        Pending("EquipmentLevelRaise", "普装/特装提升等级", "4 / 6", "旧形象指定器全局设定截图；当前缺少 6.5 可靠偏移。"),
        Pending("MiddleEquipmentLevel", "中级装备出现等级", "20", "旧形象指定器全局设定截图；当前缺少 6.5 可靠偏移。")
    ];

    private readonly HexTableReader _reader = new();
    private readonly HexTableWriter _writer = new();
    private readonly WriteOperationReportService _reportService = new();
    private readonly CczEngineProfileService _engineProfileService = new();

    public GlobalSettingsDocument Load(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var hints = _engineProfileService.Detect(project).TableHints;
        var jobSeriesTable = FindTable(project, tables, hints.JobSeriesTable);
        var detailedJobTable = FindTable(project, tables, hints.DetailedJobTable);
        var jobSeriesRead = _reader.Read(project, jobSeriesTable, tables);
        var detailedJobRead = _reader.Read(project, detailedJobTable, tables);
        if (!jobSeriesRead.Validation.IsUsable || !detailedJobRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("全局设定读取兵种名称表失败，请先检查 HexTable.xml 与 Ekd5.exe。");
        }

        var title = ReadGameTitle(project);
        var evidence = BuildEvidence(project, jobSeriesTable, detailedJobTable, title).ToList();
        return new GlobalSettingsDocument
        {
            NumericSettings = NumericSettingCatalog,
            JobSeriesNames = jobSeriesRead.Data,
            DetailedJobNames = detailedJobRead.Data,
            GameTitle = title,
            Evidence = evidence
        };
    }

    public GlobalSettingsSaveResult Save(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        GlobalSettingsDocument document,
        bool saveJobSeries,
        bool saveDetailedJobs,
        bool saveGameTitle)
    {
        var backups = new List<string>();
        var reports = new List<string>();
        var changedBytes = 0;
        var parts = new List<string>();

        if (saveJobSeries)
        {
            var table = FindTable(project, tables, _engineProfileService.Detect(project).TableHints.JobSeriesTable);
            var save = _writer.Save(project, table, document.JobSeriesNames);
            changedBytes += save.ChangedBytes;
            backups.Add(save.BackupPath);
            if (!string.IsNullOrWhiteSpace(save.ReportJsonPath)) reports.Add(save.ReportJsonPath);
            parts.Add($"兵种名 {save.ChangedBytes} 字节");
        }

        if (saveDetailedJobs)
        {
            var table = FindTable(project, tables, _engineProfileService.Detect(project).TableHints.DetailedJobTable);
            var save = _writer.Save(project, table, document.DetailedJobNames);
            changedBytes += save.ChangedBytes;
            backups.Add(save.BackupPath);
            if (!string.IsNullOrWhiteSpace(save.ReportJsonPath)) reports.Add(save.ReportJsonPath);
            parts.Add($"职业名 {save.ChangedBytes} 字节");
        }

        if (saveGameTitle)
        {
            var save = SaveGameTitle(project, document.GameTitle.Title);
            changedBytes += save.ChangedBytes;
            backups.Add(save.BackupPath);
            if (!string.IsNullOrWhiteSpace(save.ReportJsonPath)) reports.Add(save.ReportJsonPath);
            parts.Add($"游戏标题 {save.ChangedBytes} 字节");
        }

        if (saveJobSeries || saveDetailedJobs || saveGameTitle)
        {
            VerifyAfterSave(project, tables, document, saveJobSeries, saveDetailedJobs, saveGameTitle);
        }

        return new GlobalSettingsSaveResult
        {
            ChangedBytes = changedBytes,
            Summary = parts.Count == 0 ? "没有选择可保存的全局设定。" : string.Join("；", parts),
            BackupPaths = backups,
            ReportJsonPaths = reports
        };
    }

    private void VerifyAfterSave(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        GlobalSettingsDocument document,
        bool verifyJobSeries,
        bool verifyDetailedJobs,
        bool verifyGameTitle)
    {
        if (verifyJobSeries)
        {
            var table = FindTable(project, tables, _engineProfileService.Detect(project).TableHints.JobSeriesTable);
            var reread = _reader.Read(project, table, tables).Data;
            AssertNameTableMatches(document.JobSeriesNames, reread, "兵种名");
        }

        if (verifyDetailedJobs)
        {
            var table = FindTable(project, tables, _engineProfileService.Detect(project).TableHints.DetailedJobTable);
            var reread = _reader.Read(project, table, tables).Data;
            AssertNameTableMatches(document.DetailedJobNames, reread, "职业名");
        }

        if (verifyGameTitle)
        {
            var reread = ReadGameTitle(project);
            if (!string.Equals(reread.Title, document.GameTitle.Title, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"游戏标题复读失败：expected={document.GameTitle.Title} actual={reread.Title}");
            }
        }

    }

    private static void AssertNameTableMatches(DataTable expected, DataTable actual, string label)
    {
        foreach (DataRow row in expected.Rows)
        {
            var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            var expectedName = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            var actualRow = actual.Rows.Cast<DataRow>().FirstOrDefault(x => Convert.ToInt32(x["ID"], CultureInfo.InvariantCulture) == id)
                ?? throw new InvalidOperationException($"{label}复读失败：找不到 ID={id}");
            var actualName = Convert.ToString(actualRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.Equals(expectedName, actualName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label}复读失败：ID={id} expected={expectedName} actual={actualName}");
            }
        }
    }

    private GlobalTitleSetting ReadGameTitle(CczProject project)
    {
        var filePath = project.ResolveGameFile("Ekd5.exe");
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("全局设定找不到 Ekd5.exe。", filePath);
        }

        var data = File.ReadAllBytes(filePath);
        if (data.Length < GameTitleOffset + GameTitleCapacityBytes)
        {
            throw new InvalidOperationException($"Ekd5.exe 长度不足，无法读取游戏标题 {HexDisplayFormatter.FormatOffset(GameTitleOffset)}。");
        }

        var bytes = new byte[GameTitleCapacityBytes];
        Buffer.BlockCopy(data, checked((int)GameTitleOffset), bytes, 0, bytes.Length);
        return new GlobalTitleSetting
        {
            Title = EncodingService.DecodeFixedString(bytes),
            CapacityBytes = GameTitleCapacityBytes,
            FileName = "Ekd5.exe",
            Offset = GameTitleOffset,
            Source = GameTitleSource,
            Status = "已验证：本地 6.5 样本文本定位"
        };
    }

    private TableSaveResult SaveGameTitle(CczProject project, string title)
    {
        ProjectVersionGuardService.EnsureCoreFileCompatibleForWrite(project, "Ekd5.exe");
        _ = EncodingService.EncodeFixedString(title, GameTitleCapacityBytes);

        var filePath = project.ResolveGameFile("Ekd5.exe");
        var backupPath = CreateBeforeSaveBackup(project, filePath);
        var original = File.ReadAllBytes(filePath);
        var output = (byte[])original.Clone();
        var encoded = EncodingService.EncodeFixedString(title, GameTitleCapacityBytes);
        var offset = checked((int)GameTitleOffset);
        var oldBytes = new byte[GameTitleCapacityBytes];
        Buffer.BlockCopy(original, offset, oldBytes, 0, oldBytes.Length);
        Buffer.BlockCopy(encoded, 0, output, offset, encoded.Length);

        var changedBytes = 0;
        for (var i = 0; i < output.Length; i++)
        {
            if (output[i] != original[i]) changedBytes++;
        }

        File.WriteAllBytes(filePath, output);
        var report = new WriteOperationReport
        {
            OperationKind = "全局设定保存",
            SourceAction = "全局设定游戏标题保存前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, filePath),
            TargetPath = filePath,
            BackupPath = backupPath,
            BeforeSha256 = WriteOperationReportService.ComputeSha256(original),
            AfterSha256 = WriteOperationReportService.ComputeSha256(output),
            ChangedBytes = changedBytes,
            Summary = $"保存全局设定“游戏标题”，目标 Ekd5.exe@{HexDisplayFormatter.FormatOffset(GameTitleOffset)}，字节改动 {changedBytes:N0}。",
            SafetyNotes = project.IsTestCopy
                ? "该报告由测试副本写入流程自动生成。"
                : "保存前已备份 Ekd5.exe；如需回退，请使用备份文件手动恢复。",
            Changes =
            {
                new WriteOperationChange
                {
                    Category = "全局设定",
                    TableName = "游戏标题",
                    ColumnName = "游戏标题",
                    OffsetHex = HexDisplayFormatter.FormatOffset(GameTitleOffset),
                    ByteLength = GameTitleCapacityBytes,
                    OldValue = EncodingService.DecodeFixedString(oldBytes),
                    NewValue = title,
                    Annotation = "修改旧形象指定器全局设定中的游戏标题文本。"
                }
            },
            Metadata =
            {
                ["Field"] = "GameTitle",
                ["Offset"] = HexDisplayFormatter.FormatOffset(GameTitleOffset),
                ["CapacityBytes"] = GameTitleCapacityBytes.ToString(CultureInfo.InvariantCulture),
                ["Evidence"] = GameTitleSource
            }
        };
        var reportPath = _reportService.WriteJsonReport(report, backupPath);

        return new TableSaveResult
        {
            Table = new HexTableDefinition
            {
                Id = -1,
                Enabled = true,
                TableName = "全局设定-游戏标题",
                FileName = "Ekd5.exe",
                DataPos = GameTitleOffset,
                RowCount = 1,
                RowSize = GameTitleCapacityBytes,
                Columns = ["游戏标题"],
                ByteSizes = [GameTitleCapacityBytes],
                IndexTable = string.Empty,
                BeginId = 0,
                OnMem = false,
                ReadOnly = false,
                Version = "6.5",
                Fields = []
            },
            FilePath = filePath,
            RowsWritten = 1,
            ChangedBytes = changedBytes,
            BackupPath = backupPath,
            ReportJsonPath = reportPath
        };
    }

    private static IEnumerable<GlobalSettingEvidence> BuildEvidence(
        CczProject project,
        HexTableDefinition jobSeriesTable,
        HexTableDefinition detailedJobTable,
        GlobalTitleSetting title)
    {
        yield return new GlobalSettingEvidence
        {
            Area = "名称设置",
            Item = "兵种名修改",
            Target = jobSeriesTable.FileName,
            OffsetText = HexDisplayFormatter.FormatOffset(jobSeriesTable.DataPos),
            LengthText = $"{jobSeriesTable.RowCount} x {jobSeriesTable.RowSize}B",
            Status = "已验证：HexTable.xml + 现有兵种系烟测链路",
            Source = jobSeriesTable.TableName,
            Note = "等价于旧窗口右侧“兵种名修改”。"
        };
        yield return new GlobalSettingEvidence
        {
            Area = "名称设置",
            Item = "职业名修改",
            Target = detailedJobTable.FileName,
            OffsetText = HexDisplayFormatter.FormatOffset(detailedJobTable.DataPos),
            LengthText = $"{detailedJobTable.RowCount} x {detailedJobTable.RowSize}B",
            Status = "已验证：HexTable.xml + 现有详细兵种烟测链路",
            Source = detailedJobTable.TableName,
            Note = "等价于旧窗口右侧“职业名修改”。"
        };
        yield return new GlobalSettingEvidence
        {
            Area = "标题设置",
            Item = "游戏标题修改",
            Target = title.FileName,
            OffsetText = HexDisplayFormatter.FormatOffset(title.Offset),
            LengthText = $"{title.CapacityBytes}B",
            Status = title.Status,
            Source = title.Source,
            Note = "定长 GBK 文本，保存前校验字节容量。"
        };

        foreach (var setting in NumericSettingCatalog)
        {
            yield return new GlobalSettingEvidence
            {
                Area = "全局参数",
                Item = setting.DisplayName,
                Target = "待定位",
                OffsetText = "待验证",
                LengthText = "待验证",
                Status = setting.Status,
                Source = setting.Source,
                Note = "旧形象指定器无源码；当前未找到可作为 6.5 写回依据的偏移，界面只记录待验证项。"
            };
        }

        if (!string.IsNullOrWhiteSpace(project.ImageAssignerSystemIniPath))
        {
            yield return new GlobalSettingEvidence
            {
                Area = "旧工具配置",
                Item = "B形象指定器 System.ini",
                Target = project.ImageAssignerSystemIniPath,
                OffsetText = "-",
                LengthText = "-",
                Status = File.Exists(project.ImageAssignerSystemIniPath) ? "已找到" : "未找到",
                Source = "ProjectDetector",
                Note = "用于校验 R/S、策略数、道具分段等旧工具配置；不包含截图左侧全局参数偏移。"
            };
        }
    }

    private static GlobalNumericSetting Pending(string key, string displayName, string defaultText, string source) => new()
    {
        Key = key,
        DisplayName = displayName,
        SuggestedDefaultText = defaultText,
        Source = source,
        Status = "待验证",
        Detail = "需要继续逆向旧形象指定器或用实机/调试器定位 6.5 写回地址后才开放保存。"
    };

    private static HexTableDefinition FindTable(CczProject project, IReadOnlyList<HexTableDefinition> tables, string name)
        => HexTableNameResolver.ResolveForProject(project, tables, name);

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{Path.GetFileName(filePath)}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{stamp}_{suffix++}_{Path.GetFileName(filePath)}");
        }

        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }

}
