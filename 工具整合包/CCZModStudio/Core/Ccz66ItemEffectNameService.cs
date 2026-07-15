using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class Ccz66ItemEffectNameService
{
    public const long NamePoolOffset = 0x9E800;
    public const int SlotSize = 16;
    public const int ReadSlotCount = 220;
    public const string SourceName = "ImageAssigner66NamePool";
    public const string SlotFallbackSourceName = "Verified66ExeSlotFallback";

    private static readonly IReadOnlyList<Ccz66ItemEffectBinding> Bindings =
    [
        Bind(0x1A, "每回合恢复HP", 0),
        Bind(0x1A, "每回合恢复状态", 2),
        Bind(0x1B, "辅助策略命中", 28),
        Bind(0x1B, "每回合恢复MP", 1),
        Bind(0x1C, "猛击", 114),
        Bind(0x1D, "斩杀攻击", 110),
        Bind(0x1E, "获得功勋", 69),
        Bind(0x1E, "每回合获得Exp", 3),
        Bind(0x1E, "每回合获得护具Exp", 5),
        Bind(0x1E, "每回合获得武器Exp", 4),
        Bind(0x1F, "破甲攻击", 22),
        Bind(0x1F, "无反击攻击", 38),
        Bind(0x20, "辅助攻击力", 73),
        Bind(0x20, "破坏攻击", 6),
        Bind(0x21, "辅助防御力", 74),
        Bind(0x22, "辅助精神力", 75),
        Bind(0x23, "辅助爆发力", 76),
        Bind(0x24, "辅助士气", 77),
        Bind(0x25, "辅助HP", 143),
        Bind(0x26, "辅助MP", 146),
        Bind(0x26, "节约MP", 46),
        Bind(0x27, "辅助获得Exp", null),
        Bind(0x28, "辅助移动力", 177),
        Bind(0x29, "突击移动", 55),
        Bind(0x29, "先手攻击", 26),
        Bind(0x2A, "恶路移动", 63),
        Bind(0x2C, "辅助地形状态", 62),
        Bind(0x30, "反击后反击", 37),
        Bind(0x31, "无视暴防", null),
        Bind(0x31, "致命一击攻击", 51),
        Bind(0x32, "远距攻击1", 155),
        Bind(0x33, "穿透攻击", 149),
        Bind(0x35, "辅助攻击命中", 31),
        Bind(0x35, "加强攻击/突破攻击", 98),
        Bind(0x36, "引导攻击/奋战攻击", 24),
        Bind(0x37, "辅助妨碍策略", 39),
        Bind(0x3A, "可以禁咒", 14),
        Bind(0x3B, "策略模仿", 45),
        Bind(0x3D, "盾反", 115),
        Bind(0x3D, "辅助攻击防御", 32),
        Bind(0x3F, "辅助全防御", 33),
        Bind(0x40, "防御远距攻击", 47),
        Bind(0x41, "防御二次攻击", 48),
        Bind(0x41, "防御致命攻击", 112),
        Bind(0x41, "远距攻击3", 157),
        Bind(0x42, "主动连击/双枪攻击", 52),
        Bind(0x43, "辅助策略防御", 29),
        Bind(0x44, "MP防御", 50),
        Bind(0x45, "减轻远距损伤", 34),
        Bind(0x45, "再次移动", 90),
        Bind(0x46, "自动使用", 108),
        Bind(0x47, "铁索横江", 96),
        Bind(0x48, "末日审判", 111),
        Bind(0x4A, "随机异常状态", 17),
        Bind(0x4D, "冲锋攻击", 25),
        Bind(0x4D, "疾风攻击", 117),
        Bind(0x4D, "减轻策略伤害", 41),
        Bind(0x4E, "绝对克制", 118),
        Bind(0x4F, "策略免疫", 99),
        Bind(0x51, "先制攻击", 82),
        Bind(0x52, "健康光环", 100),
        Bind(0x52, "生命光环", 101),
        Bind(0x53, "策略绝对命中", 27),
        Bind(0x53, "策略无视地形", 43),
        Bind(0x53, "策略无视天气", 42),
        Bind(0x54, "大杀四方", 64),
        Bind(0x55, "策略反伤", 66),
        Bind(0x56, "策略吸血", 67),
        Bind(0x57, "辅助穿透类型", 149),
        Bind(0x57, "洪荒之力", 68),
        Bind(0x58, "戮力同心", 113),
        Bind(0x59, "反弹伤害", 19),
        Bind(0x59, "反弹异常攻击", 18),
        Bind(0x5A, "二次行动", 56),
        Bind(0x5B, "仇恨攻击", 44),
        Bind(0x5C, "神魔附体", 61),
        Bind(0x5E, "随机破坏能力", 12),
        Bind(0x60, "减轻物理伤害", 35),
        Bind(0x61, "唯我独尊", 57),
        Bind(0x62, "策略连击", 58),
        Bind(0x63, "同仇敌忾", 59),
        Bind(0x64, "众志成城", 60),
        Bind(0x65, "攻击绝对命中", 30),
        Bind(0x65, "吸血攻击", 20),
        Bind(0x66, "自动提升", 137),
        Bind(0x69, "一夫当关", 71),
        Bind(0x6B, "远距攻击2", 156),
        Bind(0x6C, "学会策略", 78),
        Bind(0x6D, "鬼神之勇", 81),
        Bind(0x6F, "众矢之的", 83),
        Bind(0x70, "批亢捣虚", 84),
        Bind(0x71, "万夫莫敌", 85),
        Bind(0x72, "限制伤害", 86),
        Bind(0x73, "转移伤害", 87),
        Bind(0x74, "特效模仿", 88),
        Bind(0x75, "特效屏蔽", 89),
        Bind(0x76, "远距攻击4", 158),
        Bind(0x77, "策略追击", 93),
        Bind(0x78, "策略先手", 94),
        Bind(0x79, "策略反击", 95),
        Bind(0x7A, "能力忽视", 134),
        Bind(0x7B, "能力辅助", 131),
        Bind(0x7C, "能力转换", 128),
        Bind(0x7D, "HP辅助", 143),
        Bind(0x7E, "MP辅助", 146),
        Bind(0x7F, "Buff光环", 140),
        Bind(0x90, "强化反击", 72)
    ];

    private static readonly IReadOnlyList<ItemEffectCatalogEntry> DisabledEntries =
    [
        Disabled("穿越移动", 54),
        Disabled("辅助水战", 169),
        Disabled("辅助四类策略", 78),
        Disabled("混乱攻击", 15),
        Disabled("集气加倍", 36),
        Disabled("金钱防御", 107),
        Disabled("禁咒攻击", 14),
        Disabled("麻痹攻击", 13),
        Disabled("魔力光环", null),
        Disabled("破坏爆发", 9),
        Disabled("破坏防御", 7),
        Disabled("破坏精神", 8),
        Disabled("破坏士气", 10),
        Disabled("破坏移动", 11),
        Disabled("深入敌后", 70),
        Disabled("吸收金钱", 21),
        Disabled("中毒攻击", 16),
        Disabled("状态强化", 116)
    ];

    public bool IsSupported(CczProject project)
        => Ccz66RevisedLayout.Is66(project) && File.Exists(GetExePath(project));

    public IReadOnlyList<Ccz66ItemEffectNameSlot> ReadSlots(CczProject project, int slotCount = ReadSlotCount)
    {
        var path = GetExePath(project);
        if (!File.Exists(path)) return Array.Empty<Ccz66ItemEffectNameSlot>();
        var bytes = File.ReadAllBytes(path);
        var result = new List<Ccz66ItemEffectNameSlot>();
        for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            var offset = checked(NamePoolOffset + slotIndex * (long)SlotSize);
            if (offset < 0 || offset + SlotSize > bytes.Length) break;
            result.Add(new Ccz66ItemEffectNameSlot
            {
                SlotIndex = slotIndex,
                Offset = offset,
                Name = DecodeSlot(bytes, checked((int)offset)),
                IsWritableNameSlot = true
            });
        }

        return result;
    }

    public IReadOnlyList<Ccz66ItemEffectBinding> GetBindings()
        => Bindings;

    public IReadOnlyList<ItemEffectCatalogEntry> BuildCatalogEntries(CczProject project, bool includeDisabled = false)
    {
        var slots = ReadSlots(project).ToDictionary(slot => slot.SlotIndex);
        var entries = Bindings
            .Select(binding => new ItemEffectCatalogEntry
            {
                EffectId = binding.EffectId,
                Name = ResolveBindingName(binding, slots),
                Description = BuildBindingDescription(binding, slots)
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .ToList();
        if (includeDisabled)
        {
            entries.AddRange(DisabledEntries);
        }

        return entries;
    }

    public IReadOnlyDictionary<int, string> BuildDisplayLookup(CczProject project)
    {
        var catalog = BuildCatalogEntries(project);
        return catalog
            .GroupBy(entry => entry.EffectId)
            .ToDictionary(
                group => group.Key,
                group => string.Join(" / ", group
                    .Select(entry => entry.Name.Trim())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.CurrentCulture)),
                EqualityComparer<int>.Default);
    }

    public bool TryResolve(CczProject project, int effectId, out string displayName, out IReadOnlyList<string> warnings)
        => TryResolve(project, effectId, out displayName, out _, out _, out warnings);

    public bool TryResolve(
        CczProject project,
        int effectId,
        out string displayName,
        out string source,
        out string confidence,
        out IReadOnlyList<string> warnings)
    {
        displayName = string.Empty;
        source = SourceName;
        confidence = "Verified66ImageAssignerMapping";
        warnings = Array.Empty<string>();
        if (!IsSupported(project)) return false;

        var localWarnings = new List<string>();
        if (!ValidateSentinels(project, out var sentinelWarnings))
        {
            localWarnings.AddRange(sentinelWarnings);
        }

        var matches = BuildCatalogEntries(project)
            .Where(entry => entry.EffectId == effectId)
            .Select(entry => entry.Name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCulture)
            .ToArray();
        if (matches.Length > 0)
        {
            displayName = string.Join(" / ", matches);
            warnings = localWarnings;
            return true;
        }

        var slot = ReadSlots(project, Math.Max(ReadSlotCount, effectId + 1))
            .FirstOrDefault(item => item.SlotIndex == effectId);
        if (slot == null || string.IsNullOrWhiteSpace(slot.Name))
        {
            return false;
        }

        displayName = slot.Name;
        source = SlotFallbackSourceName;
        confidence = "NameSlotCandidateNoImageAssignerBinding";
        localWarnings.Add($"6.6 effect id {HexDisplayFormatter.Format(effectId, 2)} was not present in the image-assigner binding list; using Ekd5.exe name slot {effectId} at {HexDisplayFormatter.FormatOffset(slot.Offset)} as a readable candidate.");
        warnings = localWarnings;
        return true;
    }

    public Ccz66ItemEffectNameWriteResult WriteSlot(CczProject project, int slotIndex, string newName)
    {
        var safeNewName = newName ?? string.Empty;
        if (!Ccz66RevisedLayout.Is66(project))
        {
            throw new InvalidOperationException("6.6 item effect name slots are only available for 6.6 projects.");
        }

        if (slotIndex < 0 || slotIndex >= ReadSlotCount)
        {
            throw new InvalidOperationException($"6.6 item effect name slot out of range: {slotIndex}.");
        }

        var encoded = EncodingService.Gbk.GetBytes(safeNewName);
        if (encoded.Length >= SlotSize)
        {
            throw new InvalidOperationException($"6.6 item effect name exceeds slot capacity: GBK bytes={encoded.Length}, max={SlotSize - 1}.");
        }

        var path = GetExePath(project);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Ekd5.exe was not found.", path);
        }

        var offset = checked(NamePoolOffset + slotIndex * (long)SlotSize);
        var original = File.ReadAllBytes(path);
        if (offset + SlotSize > original.Length)
        {
            throw new InvalidOperationException($"6.6 item effect name slot exceeds Ekd5.exe length: slot={slotIndex}, offset=0x{offset:X}.");
        }

        var output = (byte[])original.Clone();
        var intOffset = checked((int)offset);
        var oldName = DecodeSlot(original, intOffset);
        Array.Clear(output, intOffset, SlotSize);
        Buffer.BlockCopy(encoded, 0, output, intOffset, encoded.Length);
        output[intOffset + encoded.Length] = 0x00;

        var backupPath = CreateBackup(project, path);
        File.WriteAllBytes(path, output);
        var reread = File.ReadAllBytes(path);
        var rereadName = DecodeSlot(reread, intOffset);
        if (!string.Equals(rereadName, safeNewName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"6.6 item effect name reread mismatch: expected={safeNewName}, actual={rereadName}.");
        }

        var changedBytes = CountChangedBytes(original, output);
        var beforeSha = WriteOperationReportService.ComputeSha256(original);
        var afterSha = WriteOperationReportService.ComputeSha256(output);
        var report = new WriteOperationReport
        {
            OperationKind = "6.6宝物特效名槽写入",
            SourceAction = "专用16字节GBK名称槽写入",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, path),
            TargetPath = path,
            BackupPath = backupPath,
            BeforeSha256 = beforeSha,
            AfterSha256 = afterSha,
            ChangedBytes = changedBytes,
            Summary = $"写入 6.6 宝物特效名槽 slot={slotIndex}, offset={HexDisplayFormatter.FormatOffset(offset)}, old={oldName}, new={safeNewName}, changedBytes={changedBytes}.",
            SafetyNotes = "只写 Ekd5.exe 0x9E800 起的 16 字节 GBK 名称槽，不改特效号绑定规则；写前已备份，写后已复读槽文本。",
            Changes =
            [
                new WriteOperationChange
                {
                    Category = "ItemEffectName66",
                    TableName = "6.6 宝物特效名槽",
                    RowIndex = slotIndex,
                    ColumnName = "特效名",
                    OffsetHex = HexDisplayFormatter.FormatOffset(offset),
                    ByteLength = SlotSize,
                    OldValue = oldName,
                    NewValue = safeNewName,
                    Annotation = "Ekd5.exe 0x9E800 + slotIndex * 16"
                }
            ],
            Metadata =
            {
                ["SlotIndex"] = slotIndex.ToString(CultureInfo.InvariantCulture),
                ["Offset"] = HexDisplayFormatter.FormatOffset(offset),
                ["SlotSize"] = SlotSize.ToString(CultureInfo.InvariantCulture),
                ["Source"] = SourceName,
                ["WriteMode"] = "DirectNameSlot66"
            }
        };
        var reportPath = new WriteOperationReportService().WriteJsonReport(report, backupPath);
        return new Ccz66ItemEffectNameWriteResult
        {
            FilePath = path,
            BackupPath = backupPath,
            ReportJsonPath = reportPath,
            SlotIndex = slotIndex,
            Offset = offset,
            OldName = oldName,
            NewName = safeNewName,
            ChangedBytes = changedBytes,
            BeforeSha256 = beforeSha,
            AfterSha256 = afterSha
        };
    }

    public bool ValidateSentinels(CczProject project, out IReadOnlyList<string> warnings)
    {
        var result = new List<string>();
        var slots = ReadSlots(project).ToDictionary(slot => slot.SlotIndex);
        foreach (var (slot, expected) in new[]
                 {
                     (0, "每回合恢复HP"),
                     (28, "辅助策略命中"),
                     (56, "二次行动"),
                     (72, "强化反击")
                 })
        {
            if (!slots.TryGetValue(slot, out var actual) ||
                !string.Equals(actual.Name, expected, StringComparison.Ordinal))
            {
                result.Add($"6.6 effect name sentinel mismatch: slot={slot}, expected={expected}, actual={actual?.Name ?? "<missing>"}.");
            }
        }

        warnings = result;
        return result.Count == 0;
    }

    private static string ResolveBindingName(Ccz66ItemEffectBinding binding, IReadOnlyDictionary<int, Ccz66ItemEffectNameSlot> slots)
    {
        return binding.CanonicalName;
    }

    private static string BuildBindingDescription(Ccz66ItemEffectBinding binding, IReadOnlyDictionary<int, Ccz66ItemEffectNameSlot> slots)
    {
        if (binding.SlotIndex.HasValue &&
            slots.TryGetValue(binding.SlotIndex.Value, out var slot))
        {
            return $"{SourceName}; slot={binding.SlotIndex.Value}; offset={HexDisplayFormatter.FormatOffset(slot.Offset)}; currentSlotName={slot.Name}.";
        }

        return $"{SourceName}; binding from 6.6 image assigner list; no unique writable EXE name slot was mapped.";
    }

    private static string DecodeSlot(byte[] bytes, int offset)
    {
        var length = 0;
        while (length < SlotSize && offset + length < bytes.Length && bytes[offset + length] != 0x00)
        {
            length++;
        }

        if (length == 0) return string.Empty;
        var text = EncodingService.Gbk.GetString(bytes, offset, length);
        return new string(text.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
    }

    private static string GetExePath(CczProject project)
        => project.ResolveGameFile("Ekd5.exe");

    private static Ccz66ItemEffectBinding Bind(int effectId, string name, int? slotIndex)
        => new()
        {
            EffectId = effectId,
            CanonicalName = name,
            SlotIndex = slotIndex
        };

    private static ItemEffectCatalogEntry Disabled(string name, int? slotIndex)
        => new()
        {
            EffectId = -1,
            Name = name,
            Description = slotIndex.HasValue
                ? $"禁用；{SourceName}; slot={slotIndex.Value}."
                : $"禁用；{SourceName}; no unique writable EXE name slot was mapped."
        };

    private static string CreateBackup(CczProject project, string path)
    {
        var backupRoot = ProjectBackupPathService.EnsureBackupRootWritable(project);
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{Path.GetFileName(path)}.bak");
        File.Copy(path, backupPath, overwrite: false);
        return backupPath;
    }

    private static int CountChangedBytes(byte[] oldBytes, byte[] newBytes)
    {
        var count = Math.Abs(oldBytes.Length - newBytes.Length);
        var shared = Math.Min(oldBytes.Length, newBytes.Length);
        for (var i = 0; i < shared; i++)
        {
            if (oldBytes[i] != newBytes[i]) count++;
        }

        return count;
    }
}
