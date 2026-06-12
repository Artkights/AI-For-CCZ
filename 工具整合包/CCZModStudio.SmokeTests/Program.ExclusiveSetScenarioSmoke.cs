using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;

internal partial class Program
{
    static void RunExclusiveSetScenarioSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        Console.WriteLine("EXCLUSIVE_SET_SMOKE=START");
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(dictionaryPath))
        {
            throw new InvalidOperationException("未找到 CczString.ini：" + dictionaryPath);
        }

        var dictionary = new SceneStringParser().Parse(dictionaryPath);
        var effectNames = new JobEffectNameReader().ReadNames(project, FindSmokeTable(tables, "6.5-7 兵种特效"));
        var personNames = ExclusiveSetScenarioService.BuildIdNameLookup(project, tables, "6.5-0 人物");
        var itemNames = ExclusiveSetScenarioService.BuildItemNameLookup(project, tables);

        if (!ExclusiveSetScenarioService.TryParseSample(
                "0\n0,144,0,31,2\n备注",
                effectNames,
                personNames,
                itemNames,
                out var personalSample,
                out var personalError) ||
            personalSample == null ||
            personalSample.Kind != ExclusiveSetDesignKind.Personal ||
            personalSample.Position != 0 ||
            personalSample.EffectId != 144 ||
            personalSample.PersonId != 0 ||
            personalSample.WeaponId != 31 ||
            personalSample.EffectValue != 2 ||
            personalSample.Remarks != "备注")
        {
            throw new InvalidOperationException("专属样本解析失败：" + personalError);
        }

        if (!ExclusiveSetScenarioService.TryParseSample(
                "1\n1,26,1,57,255,1\n备注",
                effectNames,
                personNames,
                itemNames,
                out var setSample,
                out var setError) ||
            setSample == null ||
            setSample.Kind != ExclusiveSetDesignKind.Set ||
            setSample.Position != 1 ||
            setSample.EffectId != 26 ||
            setSample.WeaponId != 1 ||
            setSample.ArmorId != 57 ||
            setSample.AccessoryId != 255 ||
            setSample.EffectValue != 1 ||
            setSample.Remarks != "备注")
        {
            throw new InvalidOperationException("套装样本解析失败：" + setError);
        }

        var result = new ExclusiveSetScenarioService().Read(project, dictionary, effectNames, personNames, itemNames);
        Console.WriteLine($"EXCLUSIVE_SET_FILES={result.ScannedFileCount}");
        Console.WriteLine($"EXCLUSIVE_SET_COMMANDS={result.TotalCommandCount}");
        Console.WriteLine($"EXCLUSIVE_SET_ENTRIES={result.Entries.Count}");
        Console.WriteLine($"EXCLUSIVE_SET_MALFORMED={result.MalformedEntries.Count}");
        Console.WriteLine($"EXCLUSIVE_SET_WARNINGS={result.Warnings.Count}");

        if (result.ScannedFileCount < 100)
        {
            throw new InvalidOperationException($"扫描剧本数量过少：{result.ScannedFileCount}");
        }

        if (result.Entries.Count == 0)
        {
            throw new InvalidOperationException("未识别任何 72/10 个人专属/套装命令。");
        }

        foreach (var expectedFile in new[] { "R_00.eex", "S_34.eex", "S_47.eex" })
        {
            if (!result.Entries.Any(entry => entry.FileName.Equals(expectedFile, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("未识别预期样本文件：" + expectedFile);
            }
        }

        var visible = result.Entries.FirstOrDefault(entry =>
            entry.EffectDisplay.Contains(':', StringComparison.Ordinal) &&
            entry.EquipmentDisplay.Contains("武器:", StringComparison.Ordinal) &&
            entry.SourceDisplay is "R_00" or "S_34" or "S_47");
        if (visible == null)
        {
            throw new InvalidOperationException("展示字段没有生成预期的特效/装备/出处文本。");
        }

        Console.WriteLine(
            "EXCLUSIVE_SET_FIRST=" +
            $"{visible.KindText}/{visible.EffectDisplay}/{visible.EquipmentDisplay}/{visible.SourceDisplay}".Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal));
        Console.WriteLine("EXCLUSIVE_SET_SMOKE=OK");
    }

    private static HexTableDefinition FindSmokeTable(IReadOnlyList<HexTableDefinition> tables, string tableName)
        => tables.FirstOrDefault(table => table.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException("未找到数据表：" + tableName);
}
