using CCZModStudio.Models;

namespace CCZModStudio;

internal static class LegacyCommandEditDispatcher
{
    private static readonly IReadOnlyDictionary<int, string> DialogByCommandId = BuildDialogMap();

    public static bool CanEdit(int commandId)
        => DialogByCommandId.ContainsKey(commandId);

    public static string GetDialogName(int commandId)
        => DialogByCommandId.TryGetValue(commandId, out var name) ? name : string.Empty;

    public static bool Edit(
        IWin32Window owner,
        LegacyScenarioItemData itemData,
        string commandTitle,
        int commandCount,
        int precedingSameCommandCount,
        LegacyMfcDialogDataSources dataSources)
    {
        if (!DialogByCommandId.TryGetValue(itemData.Id, out var dialogName))
        {
            MessageBox.Show(
                owner,
                "旧版源码的 OnEditModify() 没有为该命令提供修改窗口。",
                "该命令暂不可修改",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        if (LegacyMfcDialogCatalog.TryGet(dialogName, out var spec))
        {
            using var mfcDialog = new LegacyMfcItemDataEditDialog(itemData, spec, dataSources, commandTitle, commandCount, precedingSameCommandCount);
            return mfcDialog.ShowDialog(owner) == DialogResult.OK;
        }

        using var dialog = new LegacyItemDataEditDialog(itemData, commandTitle, dialogName, commandCount);
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }

    private static IReadOnlyDictionary<int, string> BuildDialogMap()
    {
        var map = new Dictionary<int, string>();
        Add(map, "Dialog_2", 0, 1, 2, 20, 22, 23, 24, 25, 26, 105, 122);
        Add(map, "Dialog_4", 4, 8, 98, 116);
        Add(map, "Dialog_5", 5);
        Add(map, "Dialog_6", 6, 74);
        Add(map, "Dialog_9", 9, 19, 40, 110, 113, 118);
        Add(map, "Dialog_11", 11);
        Add(map, "Dialog_15", 15);
        Add(map, "Dialog_17", 17);
        Add(map, "Dialog_18", 18);
        Add(map, "Dialog_21", 21);
        Add(map, "Dialog_27", 27);
        Add(map, "Dialog_31", 31);
        Add(map, "Dialog_32", 32);
        Add(map, "Dialog_33", 33);
        Add(map, "Dialog_34", 34);
        Add(map, "Dialog_35", 35);
        Add(map, "Dialog_36", 36);
        Add(map, "Dialog_37", 37, 41, 42);
        Add(map, "Dialog_38", 38);
        Add(map, "Dialog_39", 39);
        Add(map, "Dialog_43", 43, 45, 92);
        Add(map, "Dialog_44", 44);
        Add(map, "Dialog_46", 46, 94, 108);
        Add(map, "Dialog_48", 48);
        Add(map, "Dialog_49", 49);
        Add(map, "Dialog_50", 50, 85);
        Add(map, "Dialog_51", 51);
        Add(map, "Dialog_52", 52);
        Add(map, "Dialog_53", 53, 57, 117);
        Add(map, "Dialog_54", 54);
        Add(map, "Dialog_55", 55);
        Add(map, "Dialog_56", 56);
        Add(map, "Dialog_58", 58);
        Add(map, "Dialog_59", 59);
        Add(map, "Dialog_60", 60);
        Add(map, "Dialog_61", 61);
        Add(map, "Dialog_62", 62, 72);
        Add(map, "Dialog_63", 63);
        Add(map, "Dialog_64", 64);
        Add(map, "Dialog_65", 65);
        Add(map, "Dialog_69", 69);
        Add(map, "Dialog_70", 70, 71);
        Add(map, "Dialog_75", 75);
        Add(map, "Dialog_76", 76);
        Add(map, "Dialog_77", 77);
        Add(map, "Dialog_78", 78);
        Add(map, "Dialog_79", 79);
        Add(map, "Dialog_80", 80);
        Add(map, "Dialog_82", 82);
        Add(map, "Dialog_83", 83);
        Add(map, "Dialog_86", 86, 87);
        Add(map, "Dialog_88", 88);
        Add(map, "Dialog_89", 89);
        Add(map, "Dialog_91", 91);
        Add(map, "Dialog_93", 93);
        Add(map, "Dialog_96", 96);
        Add(map, "Dialog_99", 99);
        Add(map, "Dialog_100", 100);
        Add(map, "Dialog_101", 101);
        Add(map, "Dialog_102", 102);
        Add(map, "Dialog_103", 103);
        Add(map, "Dialog_104", 104);
        Add(map, "Dialog_107", 107);
        Add(map, "Dialog_109", 109);
        Add(map, "Dialog_111", 111);
        Add(map, "Dialog_112", 112);
        Add(map, "Dialog_114", 114, 123);
        Add(map, "Dialog_115", 115);
        Add(map, "Dialog_119", 119);
        Add(map, "Dialog_120", 120);
        Add(map, "Dialog_121", 121);
        return map;
    }

    private static void Add(IDictionary<int, string> map, string dialogName, params int[] commandIds)
    {
        foreach (var commandId in commandIds)
        {
            map[commandId] = dialogName;
        }
    }
}
