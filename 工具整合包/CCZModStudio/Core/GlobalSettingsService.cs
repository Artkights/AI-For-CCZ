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

    private static readonly IReadOnlyList<GlobalNumericSettingDefinition> NumericSettingDefinitions =
    [
        PendingDefinition("AbilityDisplay", "能力显示（单数/双数）", "单数", GlobalNumericValueKind.BooleanRadio, "旧形象指定器 Form8 全局设置页标签已静态确认；当前缺少官方输出 diff 或 6.5 可靠偏移。"),
        PendingDefinition("AbilityThreshold", "能力条件", "135 / 144 / 144 / 144", GlobalNumericValueKind.Byte, "旧形象指定器 Form8 全局设置页标签已静态确认；当前缺少官方输出 diff 或 6.5 可靠偏移。"),
        ParentDefinition("PromotionLevel", "转职等级（一转/二转）", "20 / 40", GlobalNumericValueKind.Byte, "组合显示项，不接受写入；请使用 PromotionLevelFirst 或 PromotionLevelSecond。"),
        VerifiedDefinition(
            "PromotionLevelFirst",
            "转职等级（一转）",
            "20",
            GlobalNumericValueKind.Byte,
            1,
            99,
            "Ekd5.exe",
            0x7E67,
            0x408A67,
            [
                Target("Ekd5.exe", 0x7E67, 0x408A67, "一转等级常量 #1"),
                Target("Ekd5.exe", 0xB7BD, 0x40C3BD, "一转等级常量 #2"),
                Target("Ekd5.exe", 0x1C7E3, 0x41D3E3, "一转等级常量 #3"),
                Target("Ekd5.exe", 0x41D03, 0x442903, "一转等级常量 #4"),
                Target("Ekd5.exe", 0x41D39, 0x442939, "一转等级常量 #5"),
                Target("Ekd5.exe", 0x680B8, 0x468CB8, "一转等级常量 #6"),
                Target("Ekd5.exe", 0xB7AE, 0x40C3AE, "二转派生常量 #1，一转值 * 2", valueMultiplier: 2),
                Target("Ekd5.exe", 0x41D21, 0x442921, "二转派生常量 #2，一转值 * 2", valueMultiplier: 2)
            ],
            "官方形象指定器低风险 leaf 单字段 diff：noop 基线后仅改“一转等级 20->21”，剔除旧工具升级经验保存噪声后，Ekd5.exe 命中 8 处；二转由旧工具强制为一转两倍。证据目录 CCZModStudio_Reports/DebugEvidence/global-numeric-discovery/20260706_223300_608。",
            "OfficialDiff:PromotionLevelFirst20To21;NoopBaselineSubtracted;DerivedSecondLevelIsDouble"),
        PendingDefinition("PromotionLevelSecond", "转职等级（二转）", "40", GlobalNumericValueKind.Byte, "旧工具强制二转等级为一转等级的两倍，不允许独立编辑；请修改 PromotionLevelFirst。"),
        VerifiedDefinition(
            "LevelLimit",
            "等级上限",
            "60",
            GlobalNumericValueKind.Byte,
            1,
            99,
            "Ekd5.exe",
            0x68F1,
            0x4074F1,
            [
                Target("Ekd5.exe", 0x68F1, 0x4074F1, "等级上限常量 #1"),
                Target("Ekd5.exe", 0x7CD3, 0x4088D3, "等级上限常量 #2"),
                Target("Ekd5.exe", 0xB7D6, 0x40C3D6, "等级上限常量 #3，官方 diff 显示写入值为 UI 值 + 1", valueDelta: 1),
                Target("Ekd5.exe", 0x116C8, 0x4122C8, "等级上限常量 #4"),
                Target("Ekd5.exe", 0x117CE, 0x4123CE, "等级上限常量 #5"),
                Target("Ekd5.exe", 0x1B98E, 0x41C58E, "等级上限常量 #6")
            ],
            "官方形象指定器 Form8 单字段 diff：noop 基线后仅改“等级上限 60->61”，Ekd5.exe 额外变化 6 处。证据目录 CCZModStudio_Reports/DebugEvidence/global-numeric-discovery/20260706_211950_213。",
            "OfficialDiff:LevelLimit60To61;NoopBaselineSubtracted"),
        VerifiedDefinition(
            "UpgradeExperience",
            "升级经验",
            "73",
            GlobalNumericValueKind.Byte,
            1,
            255,
            "Ekd5.exe",
            0x7CD6,
            0x4088D6,
            [
                Target("Ekd5.exe", 0x7CD6, 0x4088D6, "升级经验常量 #1"),
                Target("Ekd5.exe", 0x4F45A, 0x45005A, "升级经验常量 #2"),
                Target("Ekd5.exe", 0x4FF33, 0x450B33, "升级经验常量 #3"),
                Target("Ekd5.exe", 0x4FF48, 0x450B48, "升级经验常量 #4"),
                Target("Ekd5.exe", 0x5001F, 0x450C1F, "升级经验常量 #5"),
                Target("Ekd5.exe", 0x5BAA3, 0x45C6A3, "升级经验常量 #6"),
                Target("Ekd5.exe", 0x78958, 0x479558, "升级经验常量 #7")
            ],
            "官方形象指定器 Form8 单字段 diff：noop 基线后仅改“升级经验 73->74”，Ekd5.exe 额外变化 7 处。证据目录 CCZModStudio_Reports/DebugEvidence/global-numeric-discovery/20260706_211950_213。",
            "OfficialDiff:UpgradeExperience73To74;NoopBaselineSubtracted"),
        PendingDefinition("EquipmentExp", "普装/特装升级经验", "150 / 200", GlobalNumericValueKind.UInt16LE, "旧形象指定器 Form8 全局设置页标签已静态确认；当前缺少官方输出 diff 或 6.5 可靠偏移。"),
        ParentDefinition("EquipmentLevelLimit", "普装/特装等级上限", "5 / 9", GlobalNumericValueKind.Byte, "组合显示项，不接受写入；请使用 EquipmentLevelLimitNormal 或 EquipmentLevelLimitSpecial。"),
        VerifiedDefinition(
            "EquipmentLevelLimitNormal",
            "普装等级上限",
            "5",
            GlobalNumericValueKind.Byte,
            1,
            99,
            "Ekd5.exe",
            0x71D9,
            0x407DD9,
            [
                Target("Ekd5.exe", 0x71D9, 0x407DD9, "普装等级上限常量 #1"),
                Target("Ekd5.exe", 0x7409, 0x408009, "普装等级上限常量 #2"),
                Target("Ekd5.exe", 0x744C, 0x40804C, "普装等级上限常量 #3"),
                Target("Ekd5.exe", 0x74E6, 0x4080E6, "普装等级上限常量 #4"),
                Target("Ekd5.exe", 0x772B, 0x40832B, "普装等级上限常量 #5"),
                Target("Ekd5.exe", 0x1F5D2, 0x4201D2, "普装等级上限常量 #6"),
                Target("Ekd5.exe", 0x71A9, 0x407DA9, "普装等级上限 * 普装提升等级派生常量 #1", multiplyBySettingKey: "EquipmentLevelRaiseNormal"),
                Target("Ekd5.exe", 0x73DE, 0x407FDE, "普装等级上限 * 普装提升等级派生常量 #2", multiplyBySettingKey: "EquipmentLevelRaiseNormal")
            ],
            "官方形象指定器低风险 leaf 单字段 diff：noop 基线后仅改“普装等级上限 5->6”，剔除旧工具升级经验保存噪声后，Ekd5.exe 命中 8 处；其中 2 处为普装等级上限 * 普装提升等级派生常量。证据目录 CCZModStudio_Reports/DebugEvidence/global-numeric-discovery/20260706_223300_608。",
            "OfficialDiff:EquipmentLevelLimitNormal5To6;NoopBaselineSubtracted;DerivedNormalLimitTimesRaise"),
        VerifiedDefinition(
            "EquipmentLevelLimitSpecial",
            "特装等级上限",
            "9",
            GlobalNumericValueKind.Byte,
            1,
            99,
            "Ekd5.exe",
            0x71AC,
            0x407DAC,
            [
                Target("Ekd5.exe", 0x71AC, 0x407DAC, "特装等级上限常量 #1"),
                Target("Ekd5.exe", 0x7727, 0x408327, "特装等级上限常量 #2"),
                Target("Ekd5.exe", 0x1F5E1, 0x4201E1, "特装等级上限常量 #3"),
                Target("Ekd5.exe", 0x1F5CE, 0x4201CE, "特装等级上限 + 1 派生常量", valueDelta: 1)
            ],
            "官方形象指定器低风险 leaf 单字段 diff：noop 基线后仅改“特装等级上限 9->10”，剔除旧工具升级经验保存噪声后，Ekd5.exe 命中 4 处；其中 1 处为 UI 值 + 1。证据目录 CCZModStudio_Reports/DebugEvidence/global-numeric-discovery/20260706_223300_608。",
            "OfficialDiff:EquipmentLevelLimitSpecial9To10;NoopBaselineSubtracted"),
        PendingDefinition("Merit", "新加/敌武将功勋", "25 / 25", GlobalNumericValueKind.UInt16LE, "旧形象指定器 Form8 全局设置页标签已静态确认；当前功勋字段另有动态调试缺口，不能开放离线写入。"),
        ParentDefinition("EquipmentLevelRaise", "普装/特装提升等级", "4 / 6", GlobalNumericValueKind.Byte, "组合显示项，不接受写入；请使用 EquipmentLevelRaiseNormal 或 EquipmentLevelRaiseSpecial。"),
        VerifiedDefinition(
            "EquipmentLevelRaiseNormal",
            "普装提升等级",
            "4",
            GlobalNumericValueKind.Byte,
            1,
            99,
            "Ekd5.exe",
            0x73A3,
            0x407FA3,
            [
                Target("Ekd5.exe", 0x73A3, 0x407FA3, "普装提升等级常量 #1"),
                Target("Ekd5.exe", 0x71A9, 0x407DA9, "普装等级上限 * 普装提升等级派生常量 #1", multiplyBySettingKey: "EquipmentLevelLimitNormal"),
                Target("Ekd5.exe", 0x73DE, 0x407FDE, "普装等级上限 * 普装提升等级派生常量 #2", multiplyBySettingKey: "EquipmentLevelLimitNormal")
            ],
            "官方形象指定器低风险 leaf 单字段 diff：noop 基线后仅改“普装提升等级 4->5”，剔除旧工具升级经验保存噪声后，Ekd5.exe 命中 3 处；其中 2 处为普装等级上限 * 普装提升等级派生常量。证据目录 CCZModStudio_Reports/DebugEvidence/global-numeric-discovery/20260706_223300_608。",
            "OfficialDiff:EquipmentLevelRaiseNormal4To5;NoopBaselineSubtracted;DerivedNormalLimitTimesRaise"),
        VerifiedDefinition(
            "EquipmentLevelRaiseSpecial",
            "特装提升等级",
            "6",
            GlobalNumericValueKind.Byte,
            1,
            99,
            "Ekd5.exe",
            0x7392,
            0x407F92,
            [
                Target("Ekd5.exe", 0x7392, 0x407F92, "特装提升等级常量 #1")
            ],
            "官方形象指定器低风险 leaf 单字段 diff：noop 基线后仅改“特装提升等级 6->7”，剔除旧工具升级经验保存噪声后，Ekd5.exe 命中 1 处。证据目录 CCZModStudio_Reports/DebugEvidence/global-numeric-discovery/20260706_223300_608。",
            "OfficialDiff:EquipmentLevelRaiseSpecial6To7;NoopBaselineSubtracted"),
        PendingDefinition("MiddleEquipmentLevel", "中级装备出现等级", "20", GlobalNumericValueKind.Byte, "旧工具界面固定为 20，不能编辑；本轮人工实验无字段级变更，保持锁定。")
    ];

    private static readonly IReadOnlyDictionary<string, string> NumericParentKeyHints =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PromotionLevel"] = "PromotionLevelFirst, PromotionLevelSecond",
            ["EquipmentLevelLimit"] = "EquipmentLevelLimitNormal, EquipmentLevelLimitSpecial",
            ["EquipmentLevelRaise"] = "EquipmentLevelRaiseNormal, EquipmentLevelRaiseSpecial"
        };

    private readonly HexTableReader _reader = new();
    private readonly HexTableWriter _writer = new();
    private readonly WriteOperationReportService _reportService = new();
    private readonly CczEngineProfileService _engineProfileService = new();
    private readonly CmfDerivedCapabilityService _cmfDerivedCapabilityService = new();

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
        var cmfCandidates = _cmfDerivedCapabilityService.ListGlobalSettingCandidates(project);
        var evidence = BuildEvidence(project, jobSeriesTable, detailedJobTable, title, cmfCandidates).ToList();
        return new GlobalSettingsDocument
        {
            NumericSettings = NumericSettingDefinitions.Select(definition => ToNumericSetting(project, definition)).ToArray(),
            NumericDefinitions = NumericSettingDefinitions,
            JobSeriesNames = jobSeriesRead.Data,
            DetailedJobNames = detailedJobRead.Data,
            GameTitle = title,
            Evidence = evidence,
            CmfCandidates = cmfCandidates
        };
    }

    public IReadOnlyList<GlobalNumericSettingDefinition> GetNumericDefinitions()
        => NumericSettingDefinitions;

    public IReadOnlyList<object> PreviewNumericSettings(GlobalSettingsDocument document, IReadOnlyDictionary<string, int> updates)
        => PreviewNumericSettings(null, document, updates);

    public IReadOnlyList<object> PreviewNumericSettings(CczProject? project, GlobalSettingsDocument document, IReadOnlyDictionary<string, int> updates)
    {
        if (updates.Count == 0) return Array.Empty<object>();
        var settings = document.NumericSettings.ToDictionary(setting => setting.Key, StringComparer.OrdinalIgnoreCase);
        var changes = new List<object>();
        foreach (var (key, value) in updates)
        {
            if (!settings.TryGetValue(key, out var setting))
            {
                throw new InvalidOperationException($"未知全局数字参数：{key}。");
            }

            if (NumericParentKeyHints.TryGetValue(setting.Key, out var leafKeys))
            {
                throw new InvalidOperationException($"全局数字参数“{setting.DisplayName}”是组合显示项，不接受写入；请使用 leaf key：{leafKeys}。");
            }

            if (!setting.CanEdit)
            {
                throw new InvalidOperationException($"全局数字参数“{setting.DisplayName}”仍未完成官方 diff/偏移闭环，不能预览或写入。");
            }

            ValidateNumericSettingMetadata(setting);
            ValidateNumericValue(setting, value);
            var bytePreview = project == null ? null : BuildNumericBytePreview(project, setting, value);
            changes.Add(new
            {
                Field = "numeric_settings",
                setting.Key,
                setting.DisplayName,
                OldValue = setting.CurrentValueText,
                NewValue = value,
                setting.TargetFileName,
                Offset = HexDisplayFormatter.FormatOffset(setting.Offset),
                RuntimeAddress = setting.RuntimeAddress == 0 ? string.Empty : "0x" + setting.RuntimeAddress.ToString("X", CultureInfo.InvariantCulture),
                setting.ByteLength,
                WriteTargetCount = GetNumericWriteTargets(setting).Count,
                ValueKind = setting.ValueKind.ToString(),
                setting.OracleCoverage,
                OldBytesHex = bytePreview?.OldBytesHex ?? string.Empty,
                NewBytesHex = bytePreview?.NewBytesHex ?? string.Empty,
                Targets = project == null
                    ? Array.Empty<object>()
                    : BuildNumericTargetPreview(project, setting, value, document.NumericSettings, updates)
            });
        }

        return changes;
    }

    public GlobalSettingsSaveResult SaveNumericSettings(CczProject project, GlobalSettingsDocument document, IReadOnlyDictionary<string, int> updates)
    {
        if (updates.Count == 0)
        {
            return new GlobalSettingsSaveResult { Summary = "没有选择可保存的全局数字参数。" };
        }

        _ = PreviewNumericSettings(project, document, updates);
        var settings = document.NumericSettings.ToDictionary(setting => setting.Key, StringComparer.OrdinalIgnoreCase);
        var requests = updates.Select(pair => new NumericWriteRequest(settings[pair.Key], pair.Value)).ToArray();
        var backups = new List<string>();
        var reports = new List<string>();
        var changedBytes = 0;
        var parts = new List<string>();

        foreach (var group in requests.GroupBy(request => request.Setting.TargetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
            {
                throw new InvalidOperationException("已验证全局数字参数缺少目标文件名，拒绝写入。");
            }

            if (group.Key.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase))
            {
                ProjectVersionGuardService.EnsureCoreFileCompatibleForWrite(project, "Ekd5.exe");
            }

            var filePath = project.ResolveGameFile(group.Key);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("全局数字参数目标文件不存在。", filePath);
            }

            var backupPath = CreateBeforeSaveBackup(project, filePath);
            var original = File.ReadAllBytes(filePath);
            var output = (byte[])original.Clone();
            var reportChanges = new List<WriteOperationChange>();
            var changedOffsets = new List<string>();

            foreach (var request in group)
            {
                foreach (var target in GetNumericWriteTargets(request.Setting))
                {
                    var encodedValue = ResolveNumericTargetValue(target, request.Value, document.NumericSettings, updates);
                    var encoded = EncodeNumericValue(request.Setting, encodedValue, target.ByteLength);
                    EnsureNumericWriteRange(request.Setting, output.Length, target.FileOffset, encoded.Length);
                    var offset = checked((int)target.FileOffset);
                    var oldBytes = new byte[encoded.Length];
                    Buffer.BlockCopy(original, offset, oldBytes, 0, oldBytes.Length);
                    Buffer.BlockCopy(encoded, 0, output, offset, encoded.Length);
                    changedOffsets.Add(HexDisplayFormatter.FormatOffset(target.FileOffset));
                    reportChanges.Add(new WriteOperationChange
                    {
                        Category = "全局数字参数",
                        TableName = "全局参数",
                        ColumnName = request.Setting.DisplayName,
                        OffsetHex = HexDisplayFormatter.FormatOffset(target.FileOffset),
                        ByteLength = encoded.Length,
                        OldValue = DecodeNumericValue(request.Setting.ValueKind, oldBytes).ToString(CultureInfo.InvariantCulture),
                        NewValue = encodedValue.ToString(CultureInfo.InvariantCulture),
                        Annotation = $"写入 {target.TargetFileName}@{HexDisplayFormatter.FormatOffset(target.FileOffset)}；{target.Purpose}；UI值={request.Value}；证据状态：{request.Setting.Status}；来源：{request.Setting.Source}"
                    });
                }
            }

            var fileChangedBytes = CountChangedBytes(original, output);
            File.WriteAllBytes(filePath, output);
            VerifyNumericWrite(filePath, group, document.NumericSettings, updates);

            var report = new WriteOperationReport
            {
                OperationKind = "全局设定数字项保存",
                SourceAction = "全局设定数字项保存前自动备份",
                ProjectRoot = project.GameRoot,
                TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, filePath),
                TargetPath = filePath,
                BackupPath = backupPath,
                BeforeSha256 = WriteOperationReportService.ComputeSha256(original),
                AfterSha256 = WriteOperationReportService.ComputeSha256(output),
                ChangedBytes = fileChangedBytes,
                Summary = $"保存全局数字参数到 {group.Key}，字段 {group.Count()} 个，字节变化 {fileChangedBytes:N0}。",
                SafetyNotes = project.IsTestCopy
                    ? "该报告由测试副本写入流程自动生成。"
                    : "保存前已备份目标文件；数字项只有在 CanEdit=true 且具备字段级证据时才允许写入。",
                Changes = reportChanges,
                Metadata =
                {
                    ["Field"] = "GlobalNumericSettings",
                    ["TargetFileName"] = group.Key,
                    ["ChangedOffsets"] = string.Join(",", changedOffsets),
                    ["WritePolicy"] = "Requires CanEdit=true, offset, width, reread validation"
                }
            };
            var reportPath = _reportService.WriteJsonReport(report, backupPath);

            backups.Add(backupPath);
            reports.Add(reportPath);
            changedBytes += fileChangedBytes;
            parts.Add($"{group.Key} {fileChangedBytes} 字节");
        }

        return new GlobalSettingsSaveResult
        {
            ChangedBytes = changedBytes,
            Summary = parts.Count == 0 ? "没有选择可保存的全局数字参数。" : "保存全局数字参数：" + string.Join("；", parts),
            BackupPaths = backups,
            ReportJsonPaths = reports
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
        GlobalTitleSetting title,
        IReadOnlyList<CmfFeatureCandidate> cmfCandidates)
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

        foreach (var setting in NumericSettingDefinitions)
        {
            yield return new GlobalSettingEvidence
            {
                Area = "全局参数",
                Item = setting.DisplayName,
                Target = string.IsNullOrWhiteSpace(setting.TargetFileName) ? "待定位" : setting.TargetFileName,
                OffsetText = setting.FileOffset == 0 ? "待验证" : HexDisplayFormatter.FormatOffset(setting.FileOffset),
                LengthText = setting.ByteLength == 0 ? "待验证" : $"{setting.ByteLength}B",
                Status = setting.EvidenceStatus,
                Source = setting.EvidenceSource,
                Note = "旧形象指定器 Form8 可证明该功能存在，但 System.ini 不含数字项偏移；本轮未取得官方保存 diff，默认只读。"
            };
        }

        yield return new GlobalSettingEvidence
        {
            Area = "全局参数逆向",
            Item = "B形象指定器 Form8 全局设置页",
            Target = "形象指定器65.exe",
            OffsetText = "UI 资源段",
            LengthText = "9 个数字项标签",
            Status = "已确认功能存在；未确认写回地址",
            Source = "官方工具二进制静态扫描",
            Note = "已静态命中能力显示、能力条件、转职等级、等级上限、升级经验、装备经验、装备等级、功勋、装备提升和中级装备出现等级等标签。"
        };

        yield return new GlobalSettingEvidence
        {
            Area = "全局参数逆向",
            Item = "CMF 示例地址 0048D3C4/0048D3C5",
            Target = "Ekd5.exe",
            OffsetText = "VA 0048D3C4 -> 文件偏移 0x8BDC4",
            LengthText = "当前 6.5 样本复读",
            Status = "已排除为等级上限/升级经验写回地址",
            Source = "--global-numeric-evidence-smoke",
            Note = "该地址在当前 6.5 基底映射到 GBK 文本片段，首字节 D0-B6-AF，不是 60/73；不能据此放开等级上限或升级经验。"
        };

        foreach (var candidate in cmfCandidates.Take(24))
        {
            yield return new GlobalSettingEvidence
            {
                Area = "CMF-derived global candidate",
                Item = candidate.Name,
                Target = candidate.TargetSubsystem,
                OffsetText = "Needs CMF export/UI field extraction",
                LengthText = "Needs validation",
                Status = candidate.ConversionStatus,
                Source = candidate.SourceCmfRelativePath,
                Note = candidate.WritePolicy
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

    private static GlobalNumericSettingDefinition PendingDefinition(
        string key,
        string displayName,
        string defaultText,
        GlobalNumericValueKind valueKind,
        string source) => new()
    {
        Key = key,
        DisplayName = displayName,
        DefaultValueText = defaultText,
        ValueKind = valueKind,
        EvidenceSource = source,
        EvidenceStatus = "待验证",
        Detail = "已确认旧形象指定器 UI 功能存在；仍需官方工具输出 diff、地址分类、测试副本写回、复读和运行时验证后才开放保存。",
        OracleCoverage = "NeedsUiOrDiffExtraction"
    };

    private static GlobalNumericSettingDefinition ParentDefinition(
        string key,
        string displayName,
        string defaultText,
        GlobalNumericValueKind valueKind,
        string source) => new()
    {
        Key = key,
        DisplayName = displayName,
        DefaultValueText = defaultText,
        ValueKind = valueKind,
        EvidenceSource = source,
        EvidenceStatus = "组合项",
        Detail = "该行只用于展示旧工具组合字段；MCP/API 写入必须使用单个 leaf key，避免把多个子字段混写到同一请求里。",
        OracleCoverage = "ParentKeyUseLeaf"
    };

    private static GlobalNumericSettingDefinition VerifiedDefinition(
        string key,
        string displayName,
        string defaultText,
        GlobalNumericValueKind valueKind,
        int minValue,
        int maxValue,
        string targetFileName,
        long fileOffset,
        long runtimeAddress,
        IReadOnlyList<GlobalNumericWriteTarget> writeTargets,
        string source,
        string oracleCoverage) => new()
    {
        Key = key,
        DisplayName = displayName,
        DefaultValueText = defaultText,
        TargetFileName = targetFileName,
        FileOffset = fileOffset,
        RuntimeAddress = runtimeAddress,
        ByteLength = DefaultByteLength(valueKind),
        WriteTargets = writeTargets,
        ValueKind = valueKind,
        MinValue = minValue,
        MaxValue = maxValue,
        CanEdit = true,
        EvidenceSource = source,
        EvidenceStatus = "已验证",
        Detail = "已通过官方工具单字段 diff、noop 基线扣除、地址/宽度归类、CCZ 测试副本写回和复读验证；写回时同步更新所有官方 diff 命中的代码常量。",
        OracleCoverage = oracleCoverage,
        CurrentValueText = defaultText
    };

    private static GlobalNumericWriteTarget Target(
        string fileName,
        long fileOffset,
        long runtimeAddress,
        string purpose,
        int valueDelta = 0,
        int valueMultiplier = 1,
        string multiplyBySettingKey = "") => new()
    {
        TargetFileName = fileName,
        FileOffset = fileOffset,
        RuntimeAddress = runtimeAddress,
        ByteLength = 1,
        ValueMultiplier = valueMultiplier,
        ValueDelta = valueDelta,
        MultiplyBySettingKey = multiplyBySettingKey,
        Purpose = purpose
    };

    private static GlobalNumericSetting ToNumericSetting(CczProject project, GlobalNumericSettingDefinition definition)
    {
        var currentValueText = definition.CanEdit
            ? ReadNumericSettingValue(project, definition)
            : definition.CurrentValueText;

        return new GlobalNumericSetting
    {
        Key = definition.Key,
        DisplayName = definition.DisplayName,
        CurrentValueText = currentValueText,
        SuggestedDefaultText = definition.DefaultValueText,
        Source = definition.EvidenceSource,
        Status = definition.EvidenceStatus,
        Detail = definition.Detail,
        CanEdit = definition.CanEdit,
        MinValue = definition.MinValue,
        MaxValue = definition.MaxValue,
        TargetFileName = definition.TargetFileName,
        Offset = definition.FileOffset,
        RuntimeAddress = definition.RuntimeAddress,
        ByteLength = definition.ByteLength,
        WriteTargets = definition.WriteTargets,
        ValueKind = definition.ValueKind,
        OracleCoverage = definition.OracleCoverage
    };
    }

    private static void ValidateNumericValue(GlobalNumericSetting setting, int value)
    {
        if (setting.MinValue != setting.MaxValue &&
            (value < setting.MinValue || value > setting.MaxValue))
        {
            throw new InvalidOperationException($"全局数字参数“{setting.DisplayName}”超出范围：{value}，允许 {setting.MinValue}..{setting.MaxValue}。");
        }

        var kindRangeOk = setting.ValueKind switch
        {
            GlobalNumericValueKind.BooleanRadio => value is 0 or 1,
            GlobalNumericValueKind.Byte => value is >= byte.MinValue and <= byte.MaxValue,
            GlobalNumericValueKind.UInt16LE => value is >= ushort.MinValue and <= ushort.MaxValue,
            GlobalNumericValueKind.UInt32LE => value >= 0,
            _ => false
        };
        if (!kindRangeOk)
        {
            throw new InvalidOperationException($"全局数字参数“{setting.DisplayName}”不符合 {setting.ValueKind} 编码范围：{value}。");
        }
    }

    private static void ValidateNumericSettingMetadata(GlobalNumericSetting setting)
    {
        if (string.IsNullOrWhiteSpace(setting.TargetFileName) ||
            setting.ByteLength <= 0 ||
            setting.Offset < 0)
        {
            throw new InvalidOperationException($"全局数字参数“{setting.DisplayName}”缺少已验证写入元数据，拒绝预览或写入。");
        }

        foreach (var target in GetNumericWriteTargets(setting))
        {
            if (!target.TargetFileName.Equals(setting.TargetFileName, StringComparison.OrdinalIgnoreCase) ||
                target.ByteLength <= 0 ||
                target.FileOffset < 0)
            {
                throw new InvalidOperationException($"全局数字参数“{setting.DisplayName}”写入目标元数据不完整或跨文件，拒绝预览或写入。");
            }
        }
    }

    private static string ReadNumericSettingValue(CczProject project, GlobalNumericSettingDefinition definition)
    {
        var filePath = project.ResolveGameFile(definition.TargetFileName);
        if (!File.Exists(filePath))
        {
            return definition.CurrentValueText;
        }

        var bytes = File.ReadAllBytes(filePath);
        if (definition.FileOffset < 0 || definition.FileOffset + definition.ByteLength > bytes.Length)
        {
            return definition.CurrentValueText;
        }

        var raw = bytes.AsSpan(checked((int)definition.FileOffset), definition.ByteLength).ToArray();
        return DecodeNumericValue(definition.ValueKind, raw).ToString(CultureInfo.InvariantCulture);
    }

    private static NumericBytePreview BuildNumericBytePreview(CczProject project, GlobalNumericSetting setting, int value)
    {
        var filePath = project.ResolveGameFile(setting.TargetFileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("全局数字参数目标文件不存在。", filePath);
        }

        var bytes = File.ReadAllBytes(filePath);
        var encoded = EncodeNumericValue(setting, value, setting.ByteLength);
        EnsureNumericWriteRange(setting, bytes.Length, setting.Offset, encoded.Length);
        var oldBytes = new byte[encoded.Length];
        Buffer.BlockCopy(bytes, checked((int)setting.Offset), oldBytes, 0, oldBytes.Length);
        return new NumericBytePreview(Convert.ToHexString(oldBytes), Convert.ToHexString(encoded));
    }

    private static IReadOnlyList<object> BuildNumericTargetPreview(
        CczProject project,
        GlobalNumericSetting setting,
        int value,
        IReadOnlyList<GlobalNumericSetting> allSettings,
        IReadOnlyDictionary<string, int> updates)
    {
        var fileBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<object>();
        foreach (var target in GetNumericWriteTargets(setting))
        {
            var filePath = project.ResolveGameFile(target.TargetFileName);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("全局数字参数目标文件不存在。", filePath);
            }

            if (!fileBytes.TryGetValue(target.TargetFileName, out var bytes))
            {
                bytes = File.ReadAllBytes(filePath);
                fileBytes[target.TargetFileName] = bytes;
            }

            var encodedValue = ResolveNumericTargetValue(target, value, allSettings, updates);
            var encoded = EncodeNumericValue(setting, encodedValue, target.ByteLength);
            EnsureNumericWriteRange(setting, bytes.Length, target.FileOffset, encoded.Length);
            var oldBytes = new byte[encoded.Length];
            Buffer.BlockCopy(bytes, checked((int)target.FileOffset), oldBytes, 0, oldBytes.Length);
            targets.Add(new
            {
                target.TargetFileName,
                Offset = HexDisplayFormatter.FormatOffset(target.FileOffset),
                RuntimeAddress = target.RuntimeAddress == 0 ? string.Empty : "0x" + target.RuntimeAddress.ToString("X", CultureInfo.InvariantCulture),
                target.ByteLength,
                EncodedValue = encodedValue,
                target.ValueMultiplier,
                target.ValueDelta,
                target.MultiplyBySettingKey,
                target.Purpose,
                OldBytesHex = Convert.ToHexString(oldBytes),
                NewBytesHex = Convert.ToHexString(encoded)
            });
        }

        return targets;
    }

    private static IReadOnlyList<GlobalNumericWriteTarget> GetNumericWriteTargets(GlobalNumericSetting setting)
    {
        if (setting.WriteTargets.Count > 0)
        {
            return setting.WriteTargets;
        }

        return
        [
            new GlobalNumericWriteTarget
            {
                TargetFileName = setting.TargetFileName,
                FileOffset = setting.Offset,
                RuntimeAddress = setting.RuntimeAddress,
                ByteLength = setting.ByteLength,
                Purpose = "单偏移写入"
            }
        ];
    }

    private static int ResolveNumericTargetValue(
        GlobalNumericWriteTarget target,
        int baseValue,
        IEnumerable<GlobalNumericSetting> allSettings,
        IReadOnlyDictionary<string, int> updates)
    {
        var multiplier = target.ValueMultiplier == 0 ? 1 : target.ValueMultiplier;
        if (!string.IsNullOrWhiteSpace(target.MultiplyBySettingKey))
        {
            var factor = ResolveNumericSettingValue(target.MultiplyBySettingKey, allSettings, updates);
            multiplier = checked(multiplier * factor);
        }

        return checked(baseValue * multiplier + target.ValueDelta);
    }

    private static int ResolveNumericSettingValue(
        string key,
        IEnumerable<GlobalNumericSetting> allSettings,
        IReadOnlyDictionary<string, int> updates)
    {
        if (updates.TryGetValue(key, out var updatedValue))
        {
            return updatedValue;
        }

        var setting = allSettings.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (setting == null)
        {
            throw new InvalidOperationException($"全局数字参数派生目标缺少依赖字段：{key}。");
        }

        if (!int.TryParse(setting.CurrentValueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"全局数字参数派生目标依赖字段不可解析：{key}={setting.CurrentValueText}。");
        }

        return value;
    }

    private static byte[] EncodeNumericValue(GlobalNumericSetting setting, int value, int byteLength)
    {
        byteLength = byteLength > 0 ? byteLength : DefaultByteLength(setting.ValueKind);
        return setting.ValueKind switch
        {
            GlobalNumericValueKind.Byte or GlobalNumericValueKind.BooleanRadio => byteLength == 1
                ? [checked((byte)value)]
                : throw new InvalidOperationException($"全局数字参数“{setting.DisplayName}”字节宽度不匹配：{byteLength}。"),
            GlobalNumericValueKind.UInt16LE => byteLength == 2
                ? BitConverter.GetBytes(checked((ushort)value))
                : throw new InvalidOperationException($"全局数字参数“{setting.DisplayName}”字节宽度不匹配：{byteLength}。"),
            GlobalNumericValueKind.UInt32LE => byteLength == 4
                ? BitConverter.GetBytes(checked((uint)value))
                : throw new InvalidOperationException($"全局数字参数“{setting.DisplayName}”字节宽度不匹配：{byteLength}。"),
            _ => throw new InvalidOperationException("未知全局数字参数编码：" + setting.ValueKind)
        };
    }

    private static int DecodeNumericValue(GlobalNumericValueKind valueKind, byte[] bytes)
        => valueKind switch
        {
            GlobalNumericValueKind.Byte or GlobalNumericValueKind.BooleanRadio => bytes.Length >= 1 ? bytes[0] : 0,
            GlobalNumericValueKind.UInt16LE => bytes.Length >= 2 ? BitConverter.ToUInt16(bytes, 0) : 0,
            GlobalNumericValueKind.UInt32LE => bytes.Length >= 4 ? checked((int)BitConverter.ToUInt32(bytes, 0)) : 0,
            _ => 0
        };

    private static int DefaultByteLength(GlobalNumericValueKind valueKind)
        => valueKind switch
        {
            GlobalNumericValueKind.Byte or GlobalNumericValueKind.BooleanRadio => 1,
            GlobalNumericValueKind.UInt16LE => 2,
            GlobalNumericValueKind.UInt32LE => 4,
            _ => 0
        };

    private static void EnsureNumericWriteRange(GlobalNumericSetting setting, int fileLength, int encodedLength)
    {
        if (setting.Offset < 0 || setting.Offset + encodedLength > fileLength)
        {
            throw new InvalidOperationException($"全局数字参数“{setting.DisplayName}”偏移超出文件范围：{HexDisplayFormatter.FormatOffset(setting.Offset)} + {encodedLength}B，文件长度 {fileLength}。");
        }
    }

    private static void EnsureNumericWriteRange(GlobalNumericSetting setting, int fileLength, long offset, int encodedLength)
    {
        if (offset < 0 || offset + encodedLength > fileLength)
        {
            throw new InvalidOperationException($"全局数字参数“{setting.DisplayName}”偏移超出文件范围：{HexDisplayFormatter.FormatOffset(offset)} + {encodedLength}B，文件长度 {fileLength}。");
        }
    }

    private static int CountChangedBytes(byte[] before, byte[] after)
    {
        var max = Math.Max(before.Length, after.Length);
        var count = 0;
        for (var i = 0; i < max; i++)
        {
            var a = i < before.Length ? before[i] : -1;
            var b = i < after.Length ? after[i] : -1;
            if (a != b) count++;
        }

        return count;
    }

    private static void VerifyNumericWrite(
        string filePath,
        IEnumerable<NumericWriteRequest> requests,
        IReadOnlyList<GlobalNumericSetting> allSettings,
        IReadOnlyDictionary<string, int> updates)
    {
        var bytes = File.ReadAllBytes(filePath);
        foreach (var request in requests)
        {
            foreach (var target in GetNumericWriteTargets(request.Setting))
            {
                var expectedValue = ResolveNumericTargetValue(target, request.Value, allSettings, updates);
                var expected = EncodeNumericValue(request.Setting, expectedValue, target.ByteLength);
                EnsureNumericWriteRange(request.Setting, bytes.Length, target.FileOffset, expected.Length);
                for (var i = 0; i < expected.Length; i++)
                {
                    var actual = bytes[checked((int)target.FileOffset) + i];
                    if (actual != expected[i])
                    {
                        throw new InvalidOperationException($"全局数字参数复读失败：{request.Setting.DisplayName} @{HexDisplayFormatter.FormatOffset(target.FileOffset)} expected={Convert.ToHexString(expected)}。");
                    }
                }
            }
        }
    }

    private sealed record NumericWriteRequest(GlobalNumericSetting Setting, int Value);

    private sealed record NumericBytePreview(string OldBytesHex, string NewBytesHex);

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
