using System.Diagnostics;
using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CczEngineProfileService
{
    private const long Version65ExeSize = 1_196_032;
    private const long Version66ExeSize = 1_130_496;

    public CczEngineProfile Detect(CczProject project)
    {
        var exePath = project.ResolveGameFile("Ekd5.exe");
        var exeSize = TryGetLength(exePath);
        var versionText = TryReadVersion(exePath);
        var pathHint = Extract6xVersionHint(project.GameRoot) ?? Extract6xVersionHint(project.WorkspaceRoot);
        var profile = BuildProfile(exeSize, versionText, pathHint);

        profile.ExePath = File.Exists(exePath) ? exePath : string.Empty;
        profile.ExeSize = exeSize;
        profile.DataSize = TryGetLength(project.ResolveGameFile("Data.e5"));
        profile.ImsgSize = TryGetLength(project.ResolveGameFile("Imsg.e5"));
        profile.StarSize = TryGetLength(project.ResolveGameFile("Star.e5"));
        profile.ItemSize = TryGetLength(project.ResolveGameFile("Item.e5"));

        if (!profile.IsKnown)
        {
            profile.Warnings.Add("未能可靠识别 6.x 引擎版本；表读取将使用语义匹配和内置 6.5 兜底，写入前必须复核目标表定义。");
        }
        else if (profile.VersionHint == "6.4" && profile.TableVersionPrefix == "6.5")
        {
            profile.Warnings.Add("旧 6.4 运行时可识别，但当前工具包没有独立 6.4 离线 HexTable；Data/物品/特效表读取会使用 6.5 表语义兜底，写入前必须另行确认。");
        }

        if (profile.VersionHint == "6.5" && profile.ExeSize is not null and not Version65ExeSize)
        {
            profile.Warnings.Add($"路径或版本提示为 6.5，但 Ekd5.exe 大小为 {profile.ExeSize.Value.ToString(CultureInfo.InvariantCulture)}，与参考大小 {Version65ExeSize} 不一致。");
        }

        if (profile.VersionHint == "6.6" && profile.ExeSize is not null and not Version66ExeSize)
        {
            profile.Warnings.Add($"路径或版本提示为 6.6，但 Ekd5.exe 大小为 {profile.ExeSize.Value.ToString(CultureInfo.InvariantCulture)}，与参考大小 {Version66ExeSize} 不一致。");
        }

        if (LegacyHmMapReader.HasLegacyHmLayout(project))
        {
            profile.Warnings.Add("检测到旧式 Hm 战场地图布局：项目无 Map 目录，存在 HmNN.e5、Hexzmap.e5、Spalet.e5。战场底图将以只读 LegacyHmRaw 方式索引和预览，暂不开放重封包写入。");
        }

        return profile;
    }

    private static CczEngineProfile BuildProfile(long? exeSize, string? versionText, string? pathHint)
    {
        var versionHint = NormalizeVersionHint(versionText) ?? NormalizeVersionHint(pathHint);
        var source = !string.IsNullOrWhiteSpace(versionText)
            ? "Ekd5.exe version resource"
            : !string.IsNullOrWhiteSpace(pathHint)
                ? "path hint"
                : string.Empty;

        if (exeSize == Version66ExeSize)
        {
            versionHint = "6.6";
            source = "Ekd5.exe size";
        }
        else if (exeSize == Version65ExeSize)
        {
            versionHint = "6.5";
            source = "Ekd5.exe size";
        }

        if (versionHint is "6.3" or "6.4" or "6.5" or "6.6")
        {
            return CreateKnown(versionHint, source);
        }

        var profile = CreateKnown("6.5", string.IsNullOrWhiteSpace(source) ? "fallback" : source);
        profile.EngineKey = "unknown";
        profile.DisplayName = "未知 6.x 引擎（6.5 表兜底）";
        profile.VersionHint = versionHint ?? "unknown";
        profile.IsKnown = false;
        profile.DetectionSource = string.IsNullOrWhiteSpace(source) ? "fallback" : source;
        profile.LegacyRuntimeLayout = null;
        return profile;
    }

    private static CczEngineProfile CreateKnown(string version, string source)
    {
        var tableVersionPrefix = ResolveTableVersionPrefix(version);
        var profile = new CczEngineProfile
        {
            EngineKey = version.Replace(".", string.Empty, StringComparison.Ordinal),
            DisplayName = $"曹操传加强版 {version}",
            VersionHint = version,
            TableVersionPrefix = tableVersionPrefix,
            IsKnown = true,
            DetectionSource = string.IsNullOrWhiteSpace(source) ? "known default" : source,
            TableHints = BuildTableHints(version)
        };

        profile.LegacyRuntimeLayout = version switch
        {
            "6.3" => BuildLegacyRuntimeLayout(version, merit: 0x508400, talent: 0x5089B0, exclusive: 0x50E400, war: 0x4B2C50, warLen: 0x24, revive: 0x4092E0, bisha: 0x508800, itemCapacity: 154),
            "6.4" => BuildLegacyRuntimeLayout(version, merit: 0x508000, talent: 0x508998, exclusive: 0x50E400, war: 0x4A7B20, warLen: 0x30, revive: 0x4092C7, bisha: 0x511800, itemCapacity: 255),
            "6.5" => new CczLegacyRuntimeMemoryLayout
            {
                Source = "current-project-debug-and-old-wrench-comparison",
                WarArrayAddress = 0x4A7B20,
                WarRecordSize = 0x30,
                AllyCapacity = 20,
                FriendlyCapacity = 40,
                EnemyCapacity = 190,
                ItemCapacity = 255,
                Applicability = "当前 6.5 已验证战场数组/HP 几何接近旧 6.4；其它运行时地址仍必须动态验证。"
            },
            _ => null
        };

        return profile;
    }

    private static string ResolveTableVersionPrefix(string version)
        => version switch
        {
            "6.3" => "6.3",
            "6.6" => "6.6",
            _ => "6.5"
        };

    private static CczEngineTableHints BuildTableHints(string version)
    {
        var prefix = version switch
        {
            "6.3" => "6.3",
            "6.6" => "6.6",
            _ => "6.5"
        };

        return new CczEngineTableHints
        {
            PersonTable = $"{prefix}-0 人物",
            ItemLowTable = $"{prefix}-1 物品（0-103）",
            ItemHighTable = $"{prefix}-2 物品（104-255）",
            JobTable = $"{prefix}-3 兵种系",
            JobSeriesTable = $"{prefix}-3 兵种系",
            DetailedJobTable = $"{prefix}-4 详细兵种",
            ItemEffectNameLowTable = $"{prefix}-1-2 装备特效名称（1A-57）",
            ItemEffectNameHighTable = $"{prefix}-1-3 装备特效名称（58-7F）",
            JobEffectNameTable = $"{prefix}-7 兵种特效",
            JobEffectDescriptionTable = $"{prefix}-7-1 兵种特效说明",
            JobEffectAssignmentTable = $"{prefix}-7-2 兵种特效分配",
            PersonalEffectTable = $"{prefix}-7-3 人物专属、套装专属",
            CampaignNameTable = $"{prefix}-8 战役名称",
            ShopDataTable = $"{prefix}-8-1 商店数据"
        };
    }

    private static CczLegacyRuntimeMemoryLayout BuildLegacyRuntimeLayout(
        string version,
        uint merit,
        uint talent,
        uint exclusive,
        uint war,
        int warLen,
        uint revive,
        uint bisha,
        int itemCapacity)
        => new()
        {
            Source = $"old-wrench-source-{version}",
            WarArrayAddress = war,
            WarRecordSize = warLen,
            AllyCapacity = version == "6.4" ? 20 : 16,
            FriendlyCapacity = version == "6.4" ? 40 : 19,
            EnemyCapacity = version == "6.4" ? 190 : 80,
            ItemCapacity = itemCapacity,
            ReviveFunctionAddress = revive,
            BishaTableAddress = bisha,
            TalentTableAddress = talent,
            ExclusiveSetTableAddress = exclusive,
            MeritTableAddress = merit
        };

    private static long? TryGetLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadVersion(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            return FirstNonEmpty(info.FileVersion, info.ProductVersion);
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeVersionHint(string? value)
    {
        var extracted = Extract6xVersionHint(value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(extracted)) return null;

        var suffix = extracted[2..];
        var digits = new string(suffix.TakeWhile(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits)) return null;
        return "6." + digits;
    }

    private static string? Extract6xVersionHint(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        for (var i = 0; i < value.Length - 2; i++)
        {
            if (value[i] != '6' || value[i + 1] != '.' || !char.IsLetterOrDigit(value[i + 2])) continue;
            var end = i + 3;
            while (end < value.Length && char.IsLetterOrDigit(value[end])) end++;
            return value[i..end];
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
