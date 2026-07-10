using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CmSettingsService
{
    private const string TargetFileName = "Ekd5.exe";
    private const string TerrainTableId = "terrain-strategy-availability";
    private const int TerrainFlagFire = 0x01;
    private const int TerrainFlagWater = 0x02;
    private const int TerrainFlagWind = 0x04;
    private const int TerrainFlagEarth = 0x08;

    private static readonly IReadOnlyList<CmSettingDefinition> SettingDefinitions =
    [
        D("kill-ability-five-dim-demand", "growth", "成长", "杀敌加能力，五维上升1%需求", CmSettingValueKind.DecimalByte, "Star6.5", "Star6.6"),
        D("kill-ability-hp-mp-demand", "growth", "成长", "杀敌加能力HP/MP上升1%需求", CmSettingValueKind.DecimalByte, "Star6.5"),
        D("kill-ability-hp-demand", "growth", "成长", "杀敌加能力HP上升1%需求", CmSettingValueKind.DecimalByte, "Star6.6"),
        D("kill-ability-mp-demand", "growth", "成长", "杀敌加能力MP上升1%需求", CmSettingValueKind.DecimalByte, "Star6.6"),
        D("treasure-mutation-level", "equipment-exp", "装备经验", "宝物质变等级", CmSettingValueKind.DecimalByte, "Star6.5", "Star6.6"),
        D("treasure-leap-level", "equipment-exp", "装备经验", "宝物飞跃等级", CmSettingValueKind.DecimalByte, "Star6.5", "Star6.6"),
        D("weapon-strategy-exp", "equipment-exp", "装备经验", "攻击武器类施放策略获得经验", CmSettingValueKind.ToggleByte, "Star6.5", "Star6.6"),
        D("spirit-weapon-physical-exp", "equipment-exp", "装备经验", "精神类武器物理攻击获得经验", CmSettingValueKind.ToggleByte, "Star6.5", "Star6.6"),
        D("physical-hit-weapon-exp", "equipment-exp", "装备经验", "物理命中武器获得exp", CmSettingValueKind.DecimalByte, "Star6.5", "Star6.6"),
        D("physical-block-weapon-exp", "equipment-exp", "装备经验", "物理格挡武器获得exp", CmSettingValueKind.DecimalByte, "Star6.5", "Star6.6"),
        D("hit-armor-exp", "equipment-exp", "装备经验", "命中防具获得exp", CmSettingValueKind.DecimalByte, "Star6.5", "Star6.6"),
        D("block-armor-exp", "equipment-exp", "装备经验", "格挡防具获得exp", CmSettingValueKind.DecimalByte, "Star6.5", "Star6.6"),
        D("strategy-hit-weapon-exp", "equipment-exp", "装备经验", "策略命中武器获得exp", CmSettingValueKind.DecimalByte, "Star6.5", "Star6.6"),
        D("strategy-block-weapon-exp", "equipment-exp", "装备经验", "策略格挡武器获得exp", CmSettingValueKind.DecimalByte, "Star6.5", "Star6.6"),
        D("side-attack-multiplier", "battle-formula", "战斗公式", "侧面攻击倍数", CmSettingValueKind.DecimalByte, "Star6.5"),
        D("back-attack-multiplier", "battle-formula", "战斗公式", "背后攻击倍数", CmSettingValueKind.DecimalByte, "Star6.5"),
        D("side-back-attack-base", "battle-formula", "战斗公式", "侧面、背后攻击基数", CmSettingValueKind.DecimalByte, "Star6.5"),
        D("physical-damage-base", "battle-formula", "战斗公式", "物理伤害基数", CmSettingValueKind.DecimalByte, "Star6.5", "Star6.6"),
        D("floating-damage", "battle-formula", "战斗公式", "浮动伤害", CmSettingValueKind.DecimalByte, "Star6.5"),
        D("guided-attack-count", "battle-formula", "战斗公式", "引导攻击次数", CmSettingValueKind.DecimalByte, "Star6.5", "Star6.6"),
        D("furious-attack-count", "battle-formula", "战斗公式", "奋战攻击次数", CmSettingValueKind.DecimalByte, "Star6.5", "Star6.6"),
        D("abnormal-ability-attack", "abnormal-state", "异常状态", "异常能力幅度：攻击", CmSettingValueKind.HexByteBare, "Star6.5", "Star6.6"),
        D("abnormal-ability-defense", "abnormal-state", "异常状态", "异常能力幅度：防御", CmSettingValueKind.HexByteBare, "Star6.5", "Star6.6"),
        D("abnormal-ability-spirit", "abnormal-state", "异常状态", "异常能力幅度：精神", CmSettingValueKind.HexByteBare, "Star6.5", "Star6.6"),
        D("abnormal-ability-agility", "abnormal-state", "异常状态", "异常能力幅度：敏捷", CmSettingValueKind.HexByteBare, "Star6.5", "Star6.6"),
        D("abnormal-ability-morale", "abnormal-state", "异常状态", "异常能力幅度：士气", CmSettingValueKind.HexByteBare, "Star6.5", "Star6.6"),
        D("abnormal-turn-poison", "abnormal-state", "异常状态", "异常持续回合：中毒", CmSettingValueKind.ShiftedTwoBitDecimal, "Star6.5", "Star6.6"),
        D("abnormal-turn-paralysis", "abnormal-state", "异常状态", "异常持续回合：麻痹", CmSettingValueKind.ShiftedTwoBitDecimal, "Star6.5", "Star6.6"),
        D("abnormal-turn-confusion", "abnormal-state", "异常状态", "异常持续回合：混乱", CmSettingValueKind.ShiftedTwoBitDecimal, "Star6.5", "Star6.6"),
        D("abnormal-turn-seal", "abnormal-state", "异常状态", "异常持续回合：禁咒", CmSettingValueKind.ShiftedTwoBitDecimal, "Star6.5", "Star6.6")
    ];

    private static readonly IReadOnlyList<CmTerrainDefinition> TerrainDefinitions =
    [
        new(0x00, "平原"),
        new(0x01, "草地"),
        new(0x02, "树林"),
        new(0x03, "荒地"),
        new(0x04, "山地"),
        new(0x05, "岩山"),
        new(0x06, "山崖"),
        new(0x07, "雪原"),
        new(0x08, "桥梁"),
        new(0x09, "浅滩"),
        new(0x0A, "沼泽"),
        new(0x0B, "池塘"),
        new(0x0C, "小河"),
        new(0x0D, "大河"),
        new(0x0E, "栅栏"),
        new(0x0F, "城墙"),
        new(0x10, "城内"),
        new(0x11, "城门"),
        new(0x12, "城池"),
        new(0x13, "关隘"),
        new(0x14, "鹿砦"),
        new(0x15, "村庄"),
        new(0x16, "兵营"),
        new(0x17, "民居"),
        new(0x18, "宝物库"),
        new(0x19, "水池"),
        new(0x1A, "火"),
        new(0x1B, "船"),
        new(0x1C, "祭坛"),
        new(0x1D, "地下")
    ];

    private readonly CmfManualSeedService _seedService = new();
    private readonly WriteOperationReportService _reportService = new();

    public CmSettingsDocument Load(CczProject project)
    {
        var context = BuildContext(project);
        var bytes = File.ReadAllBytes(context.TargetPath);
        var groups = new List<CmSettingGroup>();

        foreach (var group in context.Definitions.GroupBy(definition => new { definition.GroupKey, definition.GroupName }))
        {
            var items = group.Select(definition =>
            {
                var seed = context.FieldSeeds[definition.Key];
                EnsureRange(definition.DisplayName, bytes.Length, seed.Offset, seed.ByteLength);
                var raw = ReadBytes(bytes, seed.Offset, seed.ByteLength);
                return BuildSettingItem(definition.Key, definition.DisplayName, definition.ValueKind, raw, seed);
            }).ToArray();

            groups.Add(new CmSettingGroup
            {
                GroupKey = group.Key.GroupKey,
                DisplayName = group.Key.GroupName,
                Items = items
            });
        }

        var terrainRows = BuildTerrainRows(context, bytes).ToArray();
        return new CmSettingsDocument
        {
            TargetFile = TargetFileName,
            Groups = groups,
            TerrainStrategyRows = terrainRows
        };
    }

    public IReadOnlyList<CmSettingsPreviewChange> Preview(CczProject project, CmSettingsUpdate update)
    {
        var context = BuildContext(project);
        var bytes = File.ReadAllBytes(context.TargetPath);
        return BuildChanges(context, bytes, update).ToArray();
    }

    public CmSettingsSaveResult Save(CczProject project, CmSettingsUpdate update)
    {
        ProjectVersionGuardService.EnsureCoreFileCompatibleForWrite(project, TargetFileName);

        var context = BuildContext(project);
        var original = File.ReadAllBytes(context.TargetPath);
        var changes = BuildChanges(context, original, update).ToArray();
        if (changes.Length == 0)
        {
            return new CmSettingsSaveResult
            {
                Summary = "没有检测到需要保存的CM设定。"
            };
        }

        var backupPath = CreateBeforeSaveBackup(project, context.TargetPath);
        var output = (byte[])original.Clone();
        foreach (var change in changes)
        {
            var offset = checked((int)ParseHex(change.UeOffsetHex));
            var newBytes = ParseByteString(change.NewBytesHex);
            Buffer.BlockCopy(newBytes, 0, output, offset, newBytes.Length);
        }

        File.WriteAllBytes(context.TargetPath, output);
        VerifyWrites(context.TargetPath, changes);

        var changedBytes = CountChangedBytes(original, output);
        var report = new WriteOperationReport
        {
            OperationKind = "CM设定保存",
            SourceAction = "CM设定保存前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, context.TargetPath),
            TargetPath = context.TargetPath,
            BackupPath = backupPath,
            BeforeSha256 = WriteOperationReportService.ComputeSha256(original),
            AfterSha256 = WriteOperationReportService.ComputeSha256(output),
            ChangedBytes = changedBytes,
            Summary = $"保存CM设定：字段 {changes.Length} 项，字节变化 {changedBytes:N0}。",
            SafetyNotes = "CM设定写入使用白名单地址、固定长度覆盖、保存前备份和复读校验。",
            Changes = changes.Select(change => new WriteOperationChange
            {
                Category = "CM设定",
                TableName = change.GroupName,
                ColumnName = change.DisplayName,
                OffsetHex = change.UeOffsetHex,
                ByteLength = change.ByteLength,
                OldValue = change.OldValue,
                NewValue = change.NewValue,
                Annotation = $"写入 {TargetFileName}@{change.UeOffsetHex}：{change.OldBytesHex}->{change.NewBytesHex}"
            }).ToList(),
            Metadata =
            {
                ["Field"] = "CmSettings",
                ["TargetFileName"] = TargetFileName,
                ["ChangedFieldCount"] = changes.Length.ToString(CultureInfo.InvariantCulture),
                ["WritePolicy"] = "Whitelist only, fixed-length overwrite, reread validation"
            }
        };
        var reportPath = _reportService.WriteJsonReport(report, backupPath);

        return new CmSettingsSaveResult
        {
            ChangedFieldCount = changes.Length,
            ChangedBytes = changedBytes,
            BackupPath = backupPath,
            ReportJsonPath = reportPath,
            Summary = $"已保存 {changes.Length} 项，变更 {changedBytes} 字节，备份已生成。",
            Changes = changes
        };
    }

    public IReadOnlyList<object> GetDefinitions()
        => SettingDefinitions
            .Select(definition => new
            {
                definition.Key,
                definition.GroupKey,
                definition.GroupName,
                definition.DisplayName,
                ValueKind = definition.ValueKind.ToString()
            })
            .Cast<object>()
            .Concat(TerrainDefinitions.Select(terrain => new
            {
                Key = $"terrain-strategy-availability:{terrain.TerrainId:X2}",
                GroupKey = "terrain-strategy",
                GroupName = "地形策略",
                DisplayName = terrain.Name,
                ValueKind = "TerrainStrategyFlags"
            }).Cast<object>())
            .ToArray();

    private static CmSettingItem BuildSettingItem(
        string key,
        string displayName,
        CmSettingValueKind valueKind,
        byte[] raw,
        CmSeedFieldRuntime? seed)
    {
        var value = raw.Length == 0 ? (byte)0 : raw[0];
        var text = FormatValue(valueKind, raw, seed);
        var canEdit = GetCanEdit(valueKind, raw, seed, out var validationMessage);
        return new CmSettingItem
        {
            Key = key,
            DisplayName = displayName,
            ValueKind = valueKind,
            CurrentValue = value,
            CurrentValueText = text,
            CurrentTextValue = text,
            CurrentBoolValue = seed != null && value == seed.CheckedByte,
            CanEdit = canEdit,
            ValidationMessage = validationMessage
        };
    }

    private IEnumerable<CmTerrainStrategyRow> BuildTerrainRows(CmSettingsContext context, byte[] bytes)
    {
        if (context.TerrainSeeds.Count == 0)
        {
            yield break;
        }

        foreach (var terrain in TerrainDefinitions)
        {
            if (!context.TerrainSeeds.TryGetValue(terrain.TerrainId, out var seed))
            {
                throw new InvalidOperationException($"CM设定缺少地形策略 seed：terrainId=0x{terrain.TerrainId:X2}。");
            }

            EnsureRange(terrain.Name, bytes.Length, seed.Offset, seed.ByteLength);
            var value = bytes[checked((int)seed.Offset)] & 0x0F;
            yield return new CmTerrainStrategyRow
            {
                TerrainId = terrain.TerrainId,
                TerrainIdHex = "0x" + terrain.TerrainId.ToString("X2", CultureInfo.InvariantCulture),
                TerrainName = terrain.Name,
                Fire = (value & TerrainFlagFire) != 0,
                Water = (value & TerrainFlagWater) != 0,
                Wind = (value & TerrainFlagWind) != 0,
                Earth = (value & TerrainFlagEarth) != 0,
                CurrentValue = value
            };
        }
    }

    private IEnumerable<CmSettingsPreviewChange> BuildChanges(CmSettingsContext context, byte[] bytes, CmSettingsUpdate? update)
    {
        if (update == null) yield break;

        var definitionLookup = context.Definitions.ToDictionary(definition => definition.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, rawValue) in update.Values)
        {
            if (definitionLookup.TryGetValue(key, out var definition))
            {
                var seed = context.FieldSeeds[definition.Key];
                EnsureRange(definition.DisplayName, bytes.Length, seed.Offset, seed.ByteLength);
                var oldBytes = ReadBytes(bytes, seed.Offset, seed.ByteLength);
                EnsureCanWriteEncodedValue(definition.DisplayName, definition.ValueKind, oldBytes, seed);
                var newBytes = ParseValueBytes(definition, seed, rawValue);
                if (oldBytes.SequenceEqual(newBytes)) continue;
                yield return BuildChange(definition.Key, definition.DisplayName, definition.GroupName, oldBytes, newBytes, definition.ValueKind, seed.Offset, seed);
                continue;
            }

            throw new InvalidOperationException("未知CM设定字段：" + key);
        }

        foreach (var (terrainId, terrainUpdate) in update.TerrainStrategy)
        {
            var terrain = TerrainDefinitions.FirstOrDefault(item => item.TerrainId == terrainId);
            if (terrain == null)
            {
                throw new InvalidOperationException($"未知CM地形编号：0x{terrainId:X2}。");
            }

            if (context.TerrainSeeds.Count == 0)
            {
                throw new InvalidOperationException("当前 CM seed 未提供地形策略地址，不能保存地形策略。");
            }

            if (!context.TerrainSeeds.TryGetValue(terrainId, out var seed))
            {
                throw new InvalidOperationException($"CM设定缺少地形策略 seed：terrainId=0x{terrainId:X2}。");
            }

            EnsureRange(terrain.Name, bytes.Length, seed.Offset, seed.ByteLength);
            var oldValue = bytes[checked((int)seed.Offset)] & 0x0F;
            var newValue = oldValue;
            newValue = ApplyTerrainFlag(newValue, TerrainFlagFire, terrainUpdate.Fire);
            newValue = ApplyTerrainFlag(newValue, TerrainFlagWater, terrainUpdate.Water);
            newValue = ApplyTerrainFlag(newValue, TerrainFlagWind, terrainUpdate.Wind);
            newValue = ApplyTerrainFlag(newValue, TerrainFlagEarth, terrainUpdate.Earth);
            newValue &= 0x0F;
            if (oldValue == newValue) continue;
            yield return BuildChange(
                "terrain-strategy-availability:" + terrainId.ToString("X2", CultureInfo.InvariantCulture),
                terrain.Name,
                "地形策略",
                [checked((byte)oldValue)],
                [checked((byte)newValue)],
                CmSettingValueKind.HexByte,
                seed.Offset,
                null);
        }
    }

    private CmSettingsPreviewChange BuildChange(
        string key,
        string displayName,
        string groupName,
        byte[] oldBytes,
        byte[] newBytes,
        CmSettingValueKind valueKind,
        long offset,
        CmSeedFieldRuntime? seed)
    {
        if (oldBytes.Length != newBytes.Length)
        {
            throw new InvalidOperationException($"CM设定字段 {displayName} 写入长度不一致。");
        }

        return new CmSettingsPreviewChange
        {
            Key = key,
            DisplayName = displayName,
            GroupName = groupName,
            OldValue = FormatValue(valueKind, oldBytes, seed),
            NewValue = FormatValue(valueKind, newBytes, seed),
            OldBytesHex = Convert.ToHexString(oldBytes),
            NewBytesHex = Convert.ToHexString(newBytes),
            TargetFile = TargetFileName,
            UeOffsetHex = FormatOffset(offset),
            ByteLength = newBytes.Length
        };
    }

    private CmSettingsContext BuildContext(CczProject project)
    {
        var versionScope = ResolveSeedVersionScope(project);
        var seed = _seedService.LoadSeedDocuments(project)
            .FirstOrDefault(document =>
                document.VersionScope.Equals(versionScope, StringComparison.OrdinalIgnoreCase) &&
                document.TargetFile.Equals(TargetFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"未找到 {versionScope} CM 手工 seed，无法加载CM设定。");

        var validation = _seedService.ValidateSeeds(project);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("CM手工 seed 校验失败：" + string.Join("; ", validation.Issues.Select(issue => issue.Code + "=" + issue.Message)));
        }

        if (!seed.AddressKind.Equals("UeFileOffset", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CM设定只支持 UeFileOffset 地址。");
        }

        var definitions = SettingDefinitions
            .Where(definition => definition.VersionScopes.Contains(versionScope, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var targetPath = project.ResolveGameFile(TargetFileName);
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException("CM设定找不到 Ekd5.exe。", targetPath);
        }

        var fieldSeeds = new Dictionary<string, CmSeedFieldRuntime>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            var field = seed.Fields.FirstOrDefault(item => item.FieldId.Equals(definition.Key, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("CM手工 seed 缺少字段：" + definition.Key);
            var offset = ParseHex(field.UeOffsetHex);
            var runtime = new CmSeedFieldRuntime(
                field.FieldId,
                offset,
                field.ByteLength,
                ParseOptionalHexByte(field.CheckedBytesHex, 0xEB),
                ParseOptionalHexByte(field.UncheckedBytesHex, 0x74),
                field.Shift,
                ParseOptionalHexByteOrNull(field.MaskHex));
            if (runtime.ByteLength != 1)
            {
                throw new InvalidOperationException($"CM设定字段 {definition.DisplayName} 当前只支持 1 byte。");
            }

            fieldSeeds[definition.Key] = runtime;
        }

        var terrainTable = seed.Tables.FirstOrDefault(table => table.TableId.Equals(TerrainTableId, StringComparison.OrdinalIgnoreCase));
        var terrainSeeds = terrainTable == null
            ? new Dictionary<int, CmSeedFieldRuntime>()
            : terrainTable.Entries.ToDictionary(
                entry => entry.EntryId,
                entry => new CmSeedFieldRuntime(
                    TerrainTableId + ":" + entry.EntryId.ToString("X2", CultureInfo.InvariantCulture),
                    ParseHex(entry.UeOffsetHex),
                    terrainTable.EntryByteLength,
                    0,
                    0));

        return new CmSettingsContext(targetPath, definitions, fieldSeeds, terrainSeeds);
    }

    private static string ResolveSeedVersionScope(CczProject project)
    {
        var profile = new CczEngineProfileService().Detect(project);
        return profile.VersionHint.Equals("6.6", StringComparison.OrdinalIgnoreCase)
            ? "Star6.6"
            : "Star6.5";
    }

    private static byte[] ParseValueBytes(CmSettingDefinition definition, CmSeedFieldRuntime seed, string rawValue)
    {
        if (definition.ValueKind == CmSettingValueKind.ToggleByte)
        {
            if (bool.TryParse(rawValue, out var boolValue))
            {
                return [boolValue ? seed.CheckedByte : seed.UncheckedByte];
            }

            var normalized = rawValue.Trim();
            if (normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("启用", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("勾选", StringComparison.OrdinalIgnoreCase))
            {
                return [seed.CheckedByte];
            }

            if (normalized.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("取消", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("关闭", StringComparison.OrdinalIgnoreCase))
            {
                return [seed.UncheckedByte];
            }

            throw new InvalidOperationException("CM设定勾选字段值必须是 true/false、1/0、启用/取消：" + definition.DisplayName);
        }

        if (definition.ValueKind == CmSettingValueKind.FixedGbkText)
        {
            return ParseFixedGbkText(rawValue, seed.ByteLength, definition.DisplayName);
        }

        if (definition.ValueKind == CmSettingValueKind.ShiftedTwoBitDecimal)
        {
            var parsedTurn = ParseDecimal(rawValue);
            if (parsedTurn < 0 || parsedTurn > 3)
            {
                throw new InvalidOperationException("Abnormal turn value must be 0..3: " + definition.DisplayName);
            }

            var shift = seed.Shift ?? throw new InvalidOperationException("CM shifted field is missing shift metadata: " + definition.DisplayName);
            var mask = seed.Mask ?? throw new InvalidOperationException("CM shifted field is missing mask metadata: " + definition.DisplayName);
            var encoded = checked((byte)(((int)parsedTurn & 0x03) << shift));
            if ((encoded & ~mask) != 0)
            {
                throw new InvalidOperationException("CM shifted field mask metadata is inconsistent: " + definition.DisplayName);
            }

            return [encoded];
        }

        var parsed = definition.ValueKind is CmSettingValueKind.HexByte or CmSettingValueKind.HexByteBare
            ? ParseHex(rawValue)
            : ParseDecimal(rawValue);
        if (parsed < 0 || parsed > 255)
        {
            throw new InvalidOperationException("CM设定数值必须在 0..255：" + definition.DisplayName);
        }

        return [checked((byte)parsed)];
    }

    private static byte[] ParseFixedGbkText(string rawValue, int byteLength, string displayName)
    {
        try
        {
            return EncodingService.EncodeFixedString(rawValue ?? string.Empty, byteLength);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException($"{displayName} 最多 {byteLength} 字节 GBK。");
        }
    }

    private static string FormatValue(CmSettingValueKind valueKind, byte[] raw, CmSeedFieldRuntime? seed)
    {
        var value = raw.Length == 0 ? (byte)0 : raw[0];
        return valueKind switch
        {
            CmSettingValueKind.HexByte => "0x" + (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture),
            CmSettingValueKind.HexByteBare => (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture),
            CmSettingValueKind.ShiftedTwoBitDecimal when seed != null => DecodeShiftedTwoBit(value, seed).ToString(CultureInfo.InvariantCulture),
            CmSettingValueKind.ToggleByte when seed != null => value == seed.CheckedByte ? "true" : "false",
            CmSettingValueKind.FixedGbkText => EncodingService.DecodeFixedString(raw),
            _ => (value & 0xFF).ToString(CultureInfo.InvariantCulture)
        };
    }

    private static bool GetCanEdit(CmSettingValueKind valueKind, byte[] raw, CmSeedFieldRuntime? seed, out string validationMessage)
    {
        validationMessage = string.Empty;
        if (valueKind != CmSettingValueKind.ShiftedTwoBitDecimal || seed == null || raw.Length == 0)
        {
            return true;
        }

        var value = raw[0];
        if (TryValidateShiftedTwoBitRaw(value, seed, out validationMessage))
        {
            return true;
        }

        validationMessage = string.IsNullOrWhiteSpace(validationMessage)
            ? "Abnormal turn encoding needs address review."
            : validationMessage;
        return false;
    }

    private static void EnsureCanWriteEncodedValue(string displayName, CmSettingValueKind valueKind, byte[] raw, CmSeedFieldRuntime seed)
    {
        if (valueKind != CmSettingValueKind.ShiftedTwoBitDecimal || raw.Length == 0) return;
        if (TryValidateShiftedTwoBitRaw(raw[0], seed, out _)) return;
        throw new InvalidOperationException("Abnormal turn encoded byte contains bits outside the configured mask; review address before saving: " + displayName);
    }

    private static bool TryValidateShiftedTwoBitRaw(byte value, CmSeedFieldRuntime seed, out string validationMessage)
    {
        validationMessage = string.Empty;
        if (seed.Shift == null || seed.Shift.Value < 0 || seed.Shift.Value > 6 || seed.Mask == null)
        {
            validationMessage = "Missing shifted two-bit metadata.";
            return false;
        }

        var mask = seed.Mask.Value;
        if ((value & ~mask) == 0)
        {
            return true;
        }

        validationMessage = "Abnormal turn encoded byte has bits outside mask.";
        return false;
    }

    private static int DecodeShiftedTwoBit(byte value, CmSeedFieldRuntime seed)
    {
        var shift = seed.Shift ?? 0;
        var mask = seed.Mask ?? 0x03;
        return (value & mask) >> shift;
    }

    private static int ApplyTerrainFlag(int value, int flag, bool? enabled)
    {
        if (enabled == null) return value;
        return enabled.Value ? value | flag : value & ~flag;
    }

    private static long ParseDecimal(string value)
    {
        if (!long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException("无法解析十进制CM设定值：" + value);
        }

        return parsed;
    }

    private static long ParseHex(string value)
    {
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        if (value.Length == 0 || !value.All(Uri.IsHexDigit) ||
            !long.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException("无法解析十六进制CM设定值：" + value);
        }

        return parsed;
    }

    private static byte ParseOptionalHexByte(string value, byte fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var parsed = ParseHex(value);
        if (parsed < 0 || parsed > 255)
        {
            throw new InvalidOperationException("CM设定字节值超出 0x00..0xFF：" + value);
        }

        return checked((byte)parsed);
    }

    private static byte? ParseOptionalHexByteOrNull(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parsed = ParseHex(value);
        if (parsed < 0 || parsed > 255)
        {
            throw new InvalidOperationException("CM byte value is outside 0x00..0xFF: " + value);
        }

        return checked((byte)parsed);
    }

    private static byte[] ParseByteString(string value)
    {
        if (value.Length % 2 != 0)
        {
            throw new InvalidOperationException("无效字节串：" + value);
        }

        return Convert.FromHexString(value);
    }

    private static string FormatOffset(long offset)
        => "0x" + offset.ToString("X", CultureInfo.InvariantCulture);

    private static byte[] ReadBytes(byte[] bytes, long offset, int byteLength)
    {
        var start = checked((int)offset);
        var result = new byte[byteLength];
        Buffer.BlockCopy(bytes, start, result, 0, byteLength);
        return result;
    }

    private static void EnsureRange(string displayName, int fileLength, long offset, int byteLength)
    {
        if (offset < 0 || byteLength <= 0 || offset + byteLength > fileLength)
        {
            throw new InvalidOperationException($"CM设定字段越界：{displayName} @ {FormatOffset(offset)}，长度 {byteLength}，文件长度 {fileLength}。");
        }
    }

    private static void VerifyWrites(string targetPath, IReadOnlyList<CmSettingsPreviewChange> changes)
    {
        var bytes = File.ReadAllBytes(targetPath);
        foreach (var change in changes)
        {
            var offset = checked((int)ParseHex(change.UeOffsetHex));
            var expected = ParseByteString(change.NewBytesHex);
            EnsureRange(change.DisplayName, bytes.Length, offset, expected.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                if (bytes[offset + i] != expected[i])
                {
                    throw new InvalidOperationException($"CM设定复读校验失败：{change.DisplayName} @ {change.UeOffsetHex}。");
                }
            }
        }
    }

    private static int CountChangedBytes(byte[] before, byte[] after)
    {
        var count = 0;
        var length = Math.Min(before.Length, after.Length);
        for (var i = 0; i < length; i++)
        {
            if (before[i] != after[i]) count++;
        }

        return count + Math.Abs(before.Length - after.Length);
    }

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

    private static CmSettingDefinition D(
        string key,
        string groupKey,
        string groupName,
        string displayName,
        CmSettingValueKind valueKind,
        params string[] versionScopes)
        => new(
            key,
            groupKey,
            groupName,
            displayName,
            NormalizeDefinitionValueKind(key, valueKind),
            versionScopes.Length == 0 ? ["Star6.5"] : versionScopes);

    private static CmSettingValueKind NormalizeDefinitionValueKind(string key, CmSettingValueKind valueKind)
    {
        if (key.StartsWith("abnormal-ability-", StringComparison.OrdinalIgnoreCase))
        {
            return CmSettingValueKind.HexByteBare;
        }

        if (key.StartsWith("abnormal-turn-", StringComparison.OrdinalIgnoreCase))
        {
            return CmSettingValueKind.ShiftedTwoBitDecimal;
        }

        return valueKind;
    }

    private sealed record CmSettingDefinition(
        string Key,
        string GroupKey,
        string GroupName,
        string DisplayName,
        CmSettingValueKind ValueKind,
        IReadOnlyList<string> VersionScopes);

    private sealed record CmTerrainDefinition(int TerrainId, string Name);

    private sealed record CmSeedFieldRuntime(
        string Key,
        long Offset,
        int ByteLength,
        byte CheckedByte,
        byte UncheckedByte,
        int? Shift = null,
        byte? Mask = null);

    private sealed record CmSettingsContext(
        string TargetPath,
        IReadOnlyList<CmSettingDefinition> Definitions,
        IReadOnlyDictionary<string, CmSeedFieldRuntime> FieldSeeds,
        IReadOnlyDictionary<int, CmSeedFieldRuntime> TerrainSeeds);
}
