using CCZModStudio.Core;

internal partial class Program
{
    static void RunLegacyTextWrapSmoke()
    {
        var dialogueOptions = LegacyTextWrapService.CreateOptions(0x14, 39);
        AssertWrapped("39 个数字不换行", new string('1', 39), dialogueOptions, new string('1', 39), warnings: false);
        AssertWrapped("40 个数字换行", new string('1', 40), dialogueOptions, new string('1', 39) + "\n1", warnings: false);

        AssertWrapped("19 个汉字不换行", new string('曹', 19), dialogueOptions, new string('曹', 19), warnings: false);
        AssertWrapped("20 个汉字换行", new string('曹', 20), dialogueOptions, new string('曹', 19) + "\n曹", warnings: false);
        AssertWrapped("混合宽度换行", new string('曹', 18) + "123", dialogueOptions, new string('曹', 18) + "123", warnings: false);

        var legalSegments = "&小刘\n1\n2\n3\n&小刘\n1\n2\n3";
        AssertWrapped("& 分段各自三行合法", legalSegments, dialogueOptions, legalSegments, warnings: false);
        AssertWrapped("连续角色名不插入空白正文", "&小刘\n&小张\n1", dialogueOptions, "&小刘\n&小张\n1", warnings: false);
        AssertWrapped("0x16 同样按 & 分段", "&小刘\n1\n2\n3\n&小刘\n1\n2\n3", LegacyTextWrapService.CreateOptions(0x16, 39), legalSegments, warnings: false);

        var longSegment = "&小刘\n" + new string('曹', 80);
        var longResult = LegacyTextWrapService.Wrap(longSegment, dialogueOptions);
        if (!longResult.HasWarnings || !longResult.Text.StartsWith("&小刘\n", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("长对白段应提示超过三行，但不得删除角色名或正文。");
        }

        var mapOptions = LegacyTextWrapService.CreateOptions(0x2C, 72);
        AssertWrapped("2C 72 个 ASCII 不换行", new string('7', 72), mapOptions, new string('7', 72), warnings: false);
        AssertWrapped("2C 73 个 ASCII 换行", new string('7', 73), mapOptions, new string('7', 72) + "\n7", warnings: false);

        var disabledOptions = LegacyTextWrapService.CreateOptions(0x14, 0);
        var disabledText = new string('曹', 30);
        AssertWrapped("0 不限制", disabledText, disabledOptions, disabledText, warnings: false);
        if (!LegacyTextWrapService.IsKnownTextCommand(96) ||
            !LegacyTextWrapService.IsKnownTextCommand(99) ||
            !LegacyTextWrapService.IsKnownTextCommand(103) ||
            !LegacyTextWrapService.IsKnownTextCommand(114) ||
            !LegacyTextWrapService.IsKnownTextCommand(123) ||
            LegacyTextWrapService.IsKnownTextCommand(0x96))
        {
            throw new InvalidOperationException("旧版 Dialog_96/99/103/114/123 命令 ID 应按十进制识别。");
        }

        var manualText = "手动\n换行";
        AssertWrapped("手动换行保留", manualText, dialogueOptions, manualText, warnings: false);

        Console.WriteLine("LEGACY_TEXT_WRAP_SMOKE_OK");
    }

    private static void AssertWrapped(
        string name,
        string input,
        LegacyTextWrapOptions options,
        string expected,
        bool warnings)
    {
        var result = LegacyTextWrapService.Wrap(input, options);
        if (!string.Equals(result.Text, expected, StringComparison.Ordinal) || result.HasWarnings != warnings)
        {
            throw new InvalidOperationException(
                $"{name} failed: expected=[{expected.Replace("\n", "\\n", StringComparison.Ordinal)}] warnings={warnings}; actual=[{result.Text.Replace("\n", "\\n", StringComparison.Ordinal)}] warnings={result.HasWarnings}");
        }
    }
}
