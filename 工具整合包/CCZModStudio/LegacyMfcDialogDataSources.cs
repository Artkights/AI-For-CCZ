using System.Data;
using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class LegacyMfcDialogDataSources
{
    public const int EmptyPerson1Code = -1;

    private const int DefaultWeaponCount = 70;
    private const int DefaultArmorCount = 39;
    private const int DefaultAssistCount = 41;
    private const int DefaultLevelMax = 50;
    private const int DefaultRsMax = 100;
    private const int DefaultSoundNormalCount = 100;
    private const int DefaultSoundMapCount = 100;
    private const int DefaultSoundEffectCount = 55;
    private const int DefaultCdTrackCount = 999;
    private const int Version64SpecialSkillOffset = 0xD0D60;
    private const int Version65SpecialSkillOffset = 0x9E800;
    private const int VersionOtherSpecialSkillOffset = 0xD0FA0;

    private readonly HexTableReader _tableReader = new();
    private readonly CczEngineProfileService _engineProfileService = new();
    private string? _configuredExePath;
    private bool _levelConfigured;

    private LegacyMfcDialogDataSources()
    {
        BuildDefaultArrays();
    }

    public int WeaponCount { get; private set; } = DefaultWeaponCount;
    public int ArmorCount { get; private set; } = DefaultArmorCount;
    public int AssistCount { get; private set; } = DefaultAssistCount;
    public int LevelMax { get; private set; } = DefaultLevelMax;
    public int RsMax { get; private set; } = DefaultRsMax;
    public int SoundNormalCount { get; private set; } = DefaultSoundNormalCount;
    public int SoundMapCount { get; private set; } = DefaultSoundMapCount;
    public int SoundEffectCount { get; private set; } = DefaultSoundEffectCount;
    public int CdTrackCount { get; private set; } = DefaultCdTrackCount;
    public int MagicCount { get; private set; } = 100;
    public bool ExtendedItems { get; private set; }
    public bool HasSpecialSkillCatalog { get; private set; }

    public List<string> CommandNames { get; } = [];
    public List<string> FaceCondition { get; } = [];
    public List<string> Person1 { get; } = [];
    public List<string> Person2 { get; } = [];
    public List<string> Item { get; } = [];
    public IReadOnlyDictionary<int, ItemClassification> ItemClassifications { get; private set; } = new Dictionary<int, ItemClassification>();
    public List<string> Job { get; } = [];
    public List<string> Movie { get; } = [];
    public List<string> Object { get; } = [];
    public List<string> MagicEffect { get; } = [];
    public List<string> SpecialSkill { get; } = [];

    public IReadOnlyList<string> Direction { get; } = ["朝北", "朝东", "朝南", "朝西", "默认方向"];
    public IReadOnlyList<string> Gesture { get; } =
    [
        "普通", "下跪", "脸红", "举手", "哭", "伸手", "作揖", "盘坐脸红", "盘坐举手", "盘坐哭",
        "倒下", "单膝跪地", "被缚", "挥剑扬起", "挥剑劈下", "活埋", "起身", "单手举起", "未知", "变量", "无"
    ];
    public IReadOnlyList<string> WarGesture { get; } =
    [
        "静止", "举起武器", "防御", "受攻击", "虚弱", "攻击预备", "攻击", "二次攻击", "慢速转圈", "喘气", "晕倒", "快速转圈", "中速转圈", "无"
    ];
    public IReadOnlyList<string> Camp { get; } = ["我军", "友军", "敌军", "援军", "我军及友军", "敌军及援军", "所有部队"];
    public IReadOnlyList<string> PersonCondition { get; private set; } =
    [
        "攻击", "防御", "精神", "爆发", "士气", "HP", "MP", "HPCur", "MPCur", "Lv", "武力", "统率", "智力", "敏捷", "运气", "头像"
    ];
    public IReadOnlyList<string> WarPersonCondition { get; private set; } = ["攻击", "防御", "精神", "爆发", "士气", "移动力", "无"];
    public IReadOnlyList<string> Compare { get; private set; } = [">=", "<", "="];
    public IReadOnlyList<string> Compare2 { get; private set; } = ["==", ">=", "<"];
    public IReadOnlyList<string> Operate { get; private set; } = ["=", "+", "-"];
    public IReadOnlyList<string> Operate2 { get; private set; } = ["+=", "-=", "=", "*=", "/=", "%=", "M="];
    public IReadOnlyList<string> Change { get; } = ["下降", "正常", "上升", "无"];
    public IReadOnlyList<string> Debuff { get; private set; } = ["麻痹", "封咒", "混乱", "中毒", "5号", "6号"];
    public IReadOnlyList<string> JoinCondition { get; private set; } = ["data加入", "内存加入", "离开"];
    public IReadOnlyList<string> Weather { get; } = ["普通", "晴好", "阴雨", "小雪", "大雪"];
    public IReadOnlyList<string> Weather2 { get; } = ["晴", "晴/晴/阴/晴/阴", "晴/晴/雨/阴/雪", "阴/晴/雨/阴/雪", "雨/阴/豪雨/雪/雪", "豪雨/雨/豪雨/雪/雪"];
    public IReadOnlyList<string> Policy { get; private set; } = ["被动出击", "主动出击", "坚守原地", "攻击武将", "到指定点", "跟随武将", "逃到指定点"];
    public IReadOnlyList<string> Terrain { get; } =
    [
        "平原", "草原", "树林", "荒地", "山地", "岩山", "山崖", "雪原", "桥梁", "浅滩", "沼泽", "池塘", "小河", "大河", "栅栏",
        "城墙", "城内", "城门", "城池", "关隘", "鹿柴", "村落", "兵营", "居民", "宝库", "水池", "火焰", "船", "祭坛", "地下"
    ];
    public IReadOnlyList<string> SoloGesture { get; } = ["无", "后转", "前移", "小步前移", "小步后退", "举起武器", "防御", "受攻击", "攻击预备", "攻击", "二次攻击", "晕倒", "喘气", "撤退", "跳舞1", "跳舞2"];
    public IReadOnlyList<string> SoloAttack1 { get; } = ["命中", "格挡", "格挡后退", "后退", "闪躲绕前"];
    public IReadOnlyList<string> SoloAttack2 { get; } = ["原地攻击", "移动攻击", "互相冲锋"];
    public IReadOnlyList<string> VariableKind { get; private set; } = ["指针变量(*p)", "指针变量(p)", "整型变量"];
    public IReadOnlyList<string> VariableKind2 { get; private set; } = ["常数", "指针变量(*p)", "指针变量(p)", "指针变量(&p)", "整型变量(a)", "整型变量(&a)"];
    public IReadOnlyList<string> AllCondition { get; private set; } =
    [
        "R形象", "头像", "攻击", "防御", "精神", "爆发", "士气", "HP", "MP", "武力", "统率", "智力", "敏捷", "运气", "出战场数", "撤退场数",
        "我军标识", "兵种", "人物等级", "人物经验值", "武器", "武器等级", "武器经验值", "防具", "防具等级", "防具经验值", "辅助",
        "战场特殊形象", "战场编号", "战场横坐标", "战场纵坐标", "战场行动标识", "战场人物朝向", "HpCur", "MpCur", "战场人物攻击状态",
        "战场人物防御状态", "战场人物精神状态", "战场人物爆发状态", "战场人物士气状态", "战场人物移动状态", "战场人物健康状态"
    ];
    public IReadOnlyList<string> SpecialCommand { get; private set; } =
    [
        "0:武将改名", "1:对指定人物释放法术", "2:对指定地点释放法术", "3:对指定范围施放法术", "4:习得策略", "5:习得特技", "6:习得必杀",
        "7:商店商家变更", "8:战场地图扩展", "9:结局角色展示", "10:装备专属和套装", "11:限定AI行动范围(逐个)", "12:S插图", "13:剧本调用函数",
        "14:战场明暗变化", "15:更换动图", "16:清理离队武将信息", "17:限制区域AI行动范围（范围）", "18:全队成员提升到平均等级",
        "19:清除一个特殊指针变量", "20:待开发", "21:部队行动标识", "22:结局设置", "23:指定武将列传", "24:指定武将点击语音", "25:R数字",
        "26:产生一个随机数", "27:清空指定武将的功勋", "28:R插图", "29:pl版指令", "30:R文字", "31:只使用战场的一个矩形范围 ",
        "32:控制动态图", "33:设置一个矩形范围的黑幕"
    ];

    public static LegacyMfcDialogDataSources Create(CczProject? project, IReadOnlyList<HexTableDefinition>? tables)
    {
        var result = new LegacyMfcDialogDataSources();
        result.LoadBundledLegacySettings();
        if (project != null)
        {
            result.LoadProjectSettings(project);
        }

        if (project != null)
        {
            result.LoadLegacyGameResources(project);
            result.ApplyProjectEquipmentBoundary(project);
        }

        if (project != null && tables is { Count: > 0 })
        {
            result.LoadTableNames(project, tables);
        }

        return result;
    }

    public string GestureLabel(int id)
        => id >= 0 && id < Gesture.Count
            ? $"{id.ToString(CultureInfo.InvariantCulture)}:{Gesture[id]}"
            : id.ToString(CultureInfo.InvariantCulture);

    public string CommandName(int id, string fallback)
        => id >= 0 && id < CommandNames.Count && !string.IsNullOrWhiteSpace(CommandNames[id])
            ? CommandNames[id]
            : fallback;

    public IEnumerable<string> RangeNumbers(int start, int count)
        => Enumerable.Range(start, Math.Max(0, count)).Select(value => value.ToString(CultureInfo.InvariantCulture));

    public IEnumerable<string> LevelItems(bool includeDefault)
    {
        if (includeDefault)
        {
            yield return "默认";
        }

        for (var i = includeDefault ? 1 : 0; i <= LevelMax; i++)
        {
            yield return i.ToString(CultureInfo.InvariantCulture);
        }
    }

    public IEnumerable<string> EquipmentLevelItems()
    {
        yield return "默认";
        for (var i = 1; i <= 16; i++)
        {
            yield return "Lv" + i.ToString(CultureInfo.InvariantCulture);
        }
    }

    public IEnumerable<string> LevelOffsetItems()
    {
        for (var i = 0; i <= LevelMax; i++)
        {
            yield return $"+{i}级";
        }

        for (var i = 1; i <= LevelMax; i++)
        {
            yield return $"-{i}级";
        }
    }

    public int LevelOffsetCodeToList(int value)
        => value >= 0 && value <= LevelMax * 2 ? value : 0;

    public IEnumerable<string> ScenarioFiles()
    {
        for (var i = 0; i < RsMax; i++)
        {
            for (var j = 0; j < 2; j++)
            {
                if (i == 0 && j == 0) continue;
                yield return $"{(j == 0 ? "R_" : "S_")}{i:00}.eex";
            }
        }
    }

    public IEnumerable<string> SoundItems()
    {
        for (var i = 0; i < SoundNormalCount; i++) yield return $"SE{i:00}.WAV";
        for (var i = 0; i < SoundMapCount; i++) yield return $"SE_M_{i:00}.WAV";
        for (var i = 0; i < SoundEffectCount; i++) yield return $"SE_E_{i:00}.WAV";
    }

    public int SoundCodeToList(int code)
        => code < 100
            ? code
            : code < 200
                ? SoundNormalCount + code - 100
                : SoundNormalCount + SoundMapCount + code - 200;

    public int SoundListToCode(int index)
        => index < SoundNormalCount
            ? index
            : index < SoundNormalCount + SoundMapCount
                ? index - SoundNormalCount + 100
                : index - SoundNormalCount - SoundMapCount + 200;

    public IEnumerable<string> CdTracks()
    {
        for (var i = 0; i < CdTrackCount; i++)
        {
            yield return $"Track{i + 2}";
        }

        yield return "无";
    }

    public IEnumerable<string> MagicItems()
    {
        for (var i = 0; i < MagicCount; i++) yield return $"MEff-{i + 1}";
        for (var i = 0; i < 10; i++) yield return $"MCall-{i}.e5";
    }

    public IEnumerable<string> Dialog114ListItems(int category)
        => category switch
        {
            0 => Person2.Take(1024),
            1 => Item.Take(255),
            2 => MagicEffect.Take(144),
            3 => SpecialSkill.Take(255),
            _ => Enumerable.Empty<string>()
        };

    public static int Per1CodeToList(int value)
    {
        if (value >= 0) return value;
        return value == EmptyPerson1Code ? 5120 : 1022 - value;
    }

    public static int Per1ListToCode(int value)
    {
        if (value < 1024) return value;
        return value == 5120 ? EmptyPerson1Code : 1022 - value;
    }

    public static int Per2CodeToList(int value)
    {
        if (value >= 0) return value;
        return value == -1 ? 5374 : 1276 - value;
    }

    public static int Per2ListToCode(int value)
    {
        if (value < 1278) return value;
        return value == 5374 ? -1 : 1276 - value;
    }

    public int FaceCodeToList(int code)
        => code switch
        {
            0 => 0,
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            16 => 5,
            32 => 6,
            128 => 7,
            255 => 8,
            _ => Math.Clamp(code, 0, Math.Max(0, FaceCondition.Count - 1))
        };

    public int FaceListToCode(int index)
    {
        var values = new[] { 0, 1, 2, 3, 4, 16, 32, 128, 255 };
        return values[Math.Clamp(index, 0, values.Length - 1)];
    }

    private void BuildDefaultArrays()
    {
        CommandNames.AddRange(Enumerable.Range(0, 124).Select(i => HexDisplayFormatter.Format(i, 2)));

        for (var i = 0; i < 1024; i++)
        {
            Person1.Add($"{i}:");
            Person2.Add($"{i}:");
        }

        Person2.AddRange(["任何部队", "我军或友军", "敌军", "我军当前人物"]);
        for (var i = 0; i < 250; i++) Person2.Add($"w{i} ");
        for (var i = 0; i < 4096; i++)
        {
            Person1.Add($"v{i} ");
            Person2.Add($"v{i} ");
        }
        Person1.Add("无");
        Person2.Add("无");

        for (var i = 0; i < 512; i++) Item.Add($"{i}:");
        Item[255] = "无";
        Item[511] = "无";
        for (var i = 0; i < 80; i++) Job.Add($"{i}:");
        for (var i = 0; i < 256; i++)
        {
            MagicEffect.Add($"{i}:");
            SpecialSkill.Add($"{i}:");
        }

        Movie.AddRange(["LOGO.AVI", "OPEN.AVI", "END.AVI", "PRESS.AVI"]);
        for (var i = 0; i < 124; i++) Movie.Add($"movie{i + 1}");

        Object.AddRange(["火", "船", "起火船", "未知"]);
        for (var i = 4; i < 128; i++) Object.Add($"Gate-{i * 2 - 7}");

        FaceCondition.AddRange(["曹操-普通", "曹操-惊讶", "曹操-愤怒", "曹操-欣喜", "夏侯惇-蒙目", "孔明-邪恶", "曹丕-称帝", "夏侯惇-独目", "孔明-正常"]);
    }

    private void LoadBundledLegacySettings()
    {
        var legacyRoot = PortableInstallPaths.LegacyResource("a新剧本编辑器v0.23");
        LoadStringConfig(Path.Combine(legacyRoot, "CczString.ini"));
        LoadEditorConfig(Path.Combine(legacyRoot, "CczSceneEditor2.ini"));
    }

    private void LoadProjectSettings(CczProject project)
    {
        var candidates = new[]
        {
            project.SceneDictionaryPath,
            !string.IsNullOrWhiteSpace(project.SceneEditorDirectory) ? Path.Combine(project.SceneEditorDirectory, "CczString.ini") : string.Empty,
            Path.Combine(project.GameRoot, "CczString.ini"),
            Path.Combine(project.GameRoot, "a新剧本编辑器v0.23", "CczString.ini"),
            PortableInstallPaths.LegacyResource("a新剧本编辑器v0.23", "CczString.ini")
        };
        foreach (var path in candidates)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            LoadStringConfig(path);
        }

        var configCandidates = new[]
        {
            Path.Combine(project.GameRoot, "CczSceneEditor2.ini"),
            !string.IsNullOrWhiteSpace(project.SceneEditorDirectory) ? Path.Combine(project.SceneEditorDirectory, "CczSceneEditor2.ini") : string.Empty,
            Path.Combine(project.GameRoot, "a新剧本编辑器v0.23", "CczSceneEditor2.ini"),
            PortableInstallPaths.LegacyResource("a新剧本编辑器v0.23", "CczSceneEditor2.ini")
        };
        foreach (var path in configCandidates)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            LoadEditorConfig(path);
        }
    }

    private void LoadStringConfig(string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            var lines = LegacyTextDecoder.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Select(line => line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList())
                .ToList();
            if (lines.Count < 15) return;

            ReplaceList(CommandNames, lines[0]);
            ReplaceList(FaceCondition, lines[1]);
            PersonCondition = lines[2];
            WarPersonCondition = lines[3];
            Compare = lines[4];
            Compare2 = lines[5];
            Operate = lines[6];
            Operate2 = lines[7];
            JoinCondition = lines[8];
            Policy = lines[9];
            VariableKind = lines[10];
            VariableKind2 = lines[11];
            AllCondition = lines[12];
            SpecialCommand = lines[13];
            Debuff = lines[14].Take(6).ToList();
        }
        catch
        {
            // Keep built-in defaults when legacy user config is malformed.
        }
    }

    private void LoadEditorConfig(string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            foreach (var line in File.ReadLines(path, EncodingService.Gbk))
            {
                var trimmed = line.Trim();
                var parts = trimmed.Split('=', 2);
                if (parts.Length != 2) continue;
                if (parts[0].Equals("ExePath", StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(parts[1]))
                    {
                        _configuredExePath = parts[1];
                    }
                    continue;
                }

                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) continue;
                switch (parts[0])
                {
                    case "ItemWeaponSum":
                        WeaponCount = value;
                        break;
                    case "ItemDefenseSum":
                        ArmorCount = value;
                        break;
                    case "ItemAssistSum":
                        AssistCount = value;
                        break;
                    case "CharLvMax":
                        LevelMax = value;
                        _levelConfigured = true;
                        break;
                    case "RSMax":
                        RsMax = value;
                        break;
                }
            }
        }
        catch
        {
            // Keep defaults.
        }
    }

    private void LoadLegacyGameResources(CczProject project)
    {
        foreach (var root in ResolveLegacyResourceRoots(project).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var hasFixedResources =
                ResolveLegacyFile(root, "Ekd5.exe") != null ||
                ResolveLegacyFile(root, "Data.e5") != null ||
                ResolveLegacyFile(root, "Star.e5") != null ||
                ResolveLegacyFile(root, "Item.e5") != null;

            LoadLegacyExe(root);
            LoadLegacyDataE5(root);
            LoadLegacyStarE5(root);
            LoadLegacyItemE5(root);
            if (hasFixedResources) break;
        }
    }

    private IEnumerable<string> ResolveLegacyResourceRoots(CczProject project)
    {
        yield return project.GameRoot;

        if (!string.IsNullOrWhiteSpace(_configuredExePath))
        {
            var configuredRoot = Path.GetDirectoryName(_configuredExePath);
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                yield return configuredRoot;
            }
        }

        if (!string.IsNullOrWhiteSpace(project.SceneEditorDirectory))
        {
            var parent = Directory.GetParent(project.SceneEditorDirectory);
            if (parent != null) yield return parent.FullName;
        }
    }

    private static string? ResolveLegacyFile(string root, string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(root, fileName),
            Path.Combine(root, "E5", fileName),
            Path.Combine(root, "e5", fileName)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private void LoadLegacyExe(string root)
    {
        var path = ResolveLegacyFile(root, "Ekd5.exe");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            using var stream = File.OpenRead(path);
            if (!_levelConfigured && stream.Length > 0x68F1)
            {
                stream.Position = 0x68F1;
                var level = stream.ReadByte();
                if (level > 0) LevelMax = level;
            }

            LoadSpecialSkillNames(path);
        }
        catch
        {
            // Keep defaults when the executable cannot be inspected.
        }
    }

    private void LoadLegacyDataE5(string root)
    {
        var path = ResolveLegacyFile(root, "Data.e5");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[16];
            for (var i = 0; i < 1024; i++)
            {
                ReadFixedStringAt(stream, 0x18C + i * 0x20, buffer.AsSpan(0, 12), name =>
                {
                    if (string.IsNullOrWhiteSpace(name)) return;
                    Person1[i] = $"{i}:{name}";
                    Person2[i] = $"{i}:{name}";
                });
            }

            for (var i = 0; i < 104; i++)
            {
                ReadFixedStringAt(stream, 0x818C + i * 0x19, buffer, name =>
                {
                    if (!string.IsNullOrWhiteSpace(name)) Item[i] = $"{i}:{name}";
                });
            }

            for (var i = 0; i < 144; i++)
            {
                ReadFixedStringAt(stream, 0xB204 + i * 0x61, buffer.AsSpan(0, 10), name =>
                {
                    if (!string.IsNullOrWhiteSpace(name)) MagicEffect[i] = $"{i}:{name}";
                });
            }
        }
        catch
        {
            // Keep defaults when Data.e5 is unavailable or not in the old layout.
        }
    }

    private void LoadLegacyStarE5(string root)
    {
        var path = ResolveLegacyFile(root, "Star.e5") ?? ResolveLegacyFile(root, "star.e5");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[16];
            for (var i = 0; i < 151; i++)
            {
                var itemIndex = i + 104;
                ReadFixedStringAt(stream, i * 0x19, buffer, name =>
                {
                    if (!string.IsNullOrWhiteSpace(name) && itemIndex < Item.Count) Item[itemIndex] = $"{itemIndex}:{name}";
                });
            }
        }
        catch
        {
            // Keep defaults.
        }
    }

    private void LoadLegacyItemE5(string root)
    {
        var path = ResolveLegacyFile(root, "Item.e5") ?? ResolveLegacyFile(root, "item.e5");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[16];
            for (var i = 0; i < 255; i++)
            {
                var itemIndex = i + 256;
                ReadFixedStringAt(stream, i * 0x19, buffer, name =>
                {
                    if (!string.IsNullOrWhiteSpace(name) && itemIndex < Item.Count) Item[itemIndex] = $"{itemIndex}:{name}";
                });
            }

            ExtendedItems = true;
        }
        catch
        {
            // Keep non-extended item list when item.e5 cannot be read.
        }
    }

    private void ApplyProjectEquipmentBoundary(CczProject project)
    {
        var boundary = ItemCategoryBoundaryService.Resolve(project);
        WeaponCount = boundary.WeaponCount;
        ArmorCount = boundary.DefenseCount;
        AssistCount = boundary.AccessoryCount;
    }

    private static void ReadFixedStringAt(Stream stream, long offset, Span<byte> buffer, Action<string> apply)
    {
        if (stream.Length < offset + buffer.Length) return;
        buffer.Clear();
        stream.Position = offset;
        var read = stream.Read(buffer);
        if (read <= 0) return;
        apply(EncodingService.DecodeFixedString(buffer[..read]));
    }

    private void LoadTableNames(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var profile = _engineProfileService.Detect(project);
        LoadSingleTableNames(project, tables, profile.TableHints.PersonTable, Person1, 0, 1024);
        LoadSingleTableNames(project, tables, profile.TableHints.PersonTable, Person2, 0, 1024);
        LoadItemTableNames(project, tables);
        ApplyItemClassificationLabels(project, tables);
        LoadSingleTableNames(project, tables, profile.TableHints.DetailedJobTable, Job, 0, 80);
    }

    private void LoadSpecialSkillNames(string exePath)
    {
        if (!File.Exists(exePath)) return;

        try
        {
            var info = new FileInfo(exePath);
            var version = CczEngineProfileService.InferVersionFromExeSize(info.Length);
            var offset = version switch
            {
                "6.5" => Version65SpecialSkillOffset,
                "6.6" => VersionOtherSpecialSkillOffset,
                _ => Version64SpecialSkillOffset
            };
            if (info.Length < offset + 256 * 16) return;

            using var stream = File.OpenRead(exePath);
            var buffer = new byte[14];
            for (var i = 0; i < 256; i++)
            {
                stream.Position = offset + i * 16;
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                var name = EncodingService.DecodeFixedString(buffer.AsSpan(0, read));
                if (!string.IsNullOrWhiteSpace(name))
                {
                    SpecialSkill[i] = $"{i}:{name}";
                }
            }

            HasSpecialSkillCatalog = true;
        }
        catch
        {
            // Keep the base 0:..255: list and hide the optional category when it cannot be read.
        }
    }

    private void LoadSingleTableNames(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName, List<string> target, int offset, int maxCount)
    {
        if (!HexTableNameResolver.TryResolveForProject(project, tables, tableName, out var table)) return;

        try
        {
            var read = _tableReader.Read(project, table, tables);
            var nameColumn = FindNameColumn(read.Data);
            if (string.IsNullOrEmpty(nameColumn)) return;
            foreach (DataRow row in read.Data.Rows)
            {
                var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                if (id < offset || id >= offset + maxCount) continue;
                var index = id - offset;
                if (index < 0 || index >= target.Count) continue;
                var name = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture)?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    target[index] = $"{id}:{name}";
                }
            }
        }
        catch
        {
            // Missing tables should not block command editing.
        }
    }

    private void LoadItemTableNames(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        try
        {
            foreach (var table in HexTableNameResolver.ResolveItemTables(project, tables))
            {
                var read = _tableReader.Read(project, table, tables);
                var nameColumn = FindNameColumn(read.Data);
                if (string.IsNullOrEmpty(nameColumn)) continue;
                foreach (DataRow row in read.Data.Rows)
                {
                    var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                    if (id < 0 || id >= Item.Count) continue;
                    var name = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture)?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        Item[id] = $"{id}:{name}";
                    }
                }
            }
        }
        catch
        {
            // Keep defaults.
        }
    }

    private void ApplyItemClassificationLabels(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        try
        {
            ItemClassifications = new ItemClassificationService().BuildLookup(project, tables);
            foreach (var classification in ItemClassifications.Values)
            {
                if (classification.ItemId < 0 || classification.ItemId >= Item.Count) continue;
                Item[classification.ItemId] = AppendItemClassificationLabel(Item[classification.ItemId], classification);
            }
        }
        catch
        {
            ItemClassifications = new Dictionary<int, ItemClassification>();
        }
    }

    private static string AppendItemClassificationLabel(string text, ItemClassification classification)
    {
        if (classification.Kind == ItemKind.Consumable)
        {
            return text.Contains("不可装备", StringComparison.Ordinal)
                ? text
                : text + " [道具/消耗品-不可装备]";
        }

        if (classification.Kind == ItemKind.AccessoryEquipment)
        {
            return text.Contains("辅助装备", StringComparison.Ordinal)
                ? text
                : text + " [辅助装备]";
        }

        if (classification.Kind == ItemKind.Reserved)
        {
            return text.Contains("预留", StringComparison.Ordinal)
                ? text
                : text + " [预留/空位]";
        }

        return text;
    }

    private static string FindNameColumn(DataTable data)
    {
        if (data.Columns.Contains("名称")) return "名称";
        if (data.Columns.Contains("名字")) return "名字";

        foreach (DataColumn column in data.Columns)
        {
            if (column.ColumnName.Contains("名称", StringComparison.Ordinal) ||
                column.ColumnName.Contains("名字", StringComparison.Ordinal) ||
                column.ColumnName.Contains("姓名", StringComparison.Ordinal))
            {
                return column.ColumnName;
            }
        }

        return data.Columns.Count > 1 ? data.Columns[1].ColumnName : string.Empty;
    }

    private static void ReplaceList(List<string> target, IReadOnlyList<string> values)
    {
        target.Clear();
        target.AddRange(values);
    }
}
