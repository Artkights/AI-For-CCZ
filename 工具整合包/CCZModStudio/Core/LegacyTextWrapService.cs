using System.Text;

namespace CCZModStudio.Core;

public enum LegacyTextWrapMode
{
    PlainText,
    DialogueSegments,
    MapText
}

public sealed record LegacyTextWrapOptions(
    int CommandId,
    int LineLimit,
    int MaxLines,
    LegacyTextWrapMode Mode)
{
    public bool Disabled => LineLimit <= 0;
}

public sealed record LegacyTextWrapDiagnostic(
    string Message,
    int SegmentIndex,
    int LineCount,
    int MaxLines);

public sealed record LegacyTextWrapResult(
    string Text,
    IReadOnlyList<LegacyTextWrapDiagnostic> Diagnostics)
{
    public bool HasWarnings => Diagnostics.Count > 0;
}

public static class LegacyTextWrapService
{
    public const int DialogueDefaultLineLimit = 39;
    public const int MapTextDefaultLineLimit = 72;
    public const int DefaultMaxLines = 3;

    public static LegacyTextWrapOptions CreateOptions(int commandId, int lineLimit)
        => new(commandId, Math.Max(0, lineLimit), DefaultMaxLines, GetMode(commandId));

    public static int GetDefaultLineLimit(int commandId)
        => commandId == 0x2C ? MapTextDefaultLineLimit : DialogueDefaultLineLimit;

    public static LegacyTextWrapMode GetMode(int commandId)
        => commandId switch
        {
            0x14 or 0x15 or 0x16 or 0x69 or 0x7A => LegacyTextWrapMode.DialogueSegments,
            0x2C => LegacyTextWrapMode.MapText,
            _ => LegacyTextWrapMode.PlainText
        };

    public static bool IsTextCommand(int commandId, bool hasTextParameter)
        => hasTextParameter || IsKnownTextCommand(commandId);

    public static bool IsKnownTextCommand(int commandId)
        => commandId is 0x00 or 0x01 or 0x02 or
            0x14 or 0x15 or 0x16 or 0x17 or 0x18 or 0x19 or 0x1A or
            0x2C or 0x69 or 0x7A or 96 or 99 or 103 or 114 or 123;

    public static string BuildSettingsKey(int commandId)
        => commandId >= 0 && commandId <= 0xFF
            ? $"TextWrap.{commandId:X2}"
            : $"TextWrap.{commandId}";

    public static LegacyTextWrapResult Wrap(string text, LegacyTextWrapOptions options)
    {
        text ??= string.Empty;
        if (options.Disabled)
        {
            return new LegacyTextWrapResult(NormalizeNewLines(text), Array.Empty<LegacyTextWrapDiagnostic>());
        }

        var normalized = NormalizeNewLines(text);
        return options.Mode == LegacyTextWrapMode.DialogueSegments
            ? WrapDialogueSegments(normalized, options)
            : WrapPlainText(normalized, options, segmentIndex: 0);
    }

    public static string FormatDiagnostics(IReadOnlyList<LegacyTextWrapDiagnostic> diagnostics)
        => diagnostics.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message));

    private static LegacyTextWrapResult WrapDialogueSegments(string text, LegacyTextWrapOptions options)
    {
        var lines = text.Split('\n');
        var output = new List<string>();
        var diagnostics = new List<LegacyTextWrapDiagnostic>();
        var bodyLines = new List<string>();
        var segmentIndex = 0;
        var hasSegment = false;

        void FlushBody()
        {
            if (bodyLines.Count == 0)
            {
                return;
            }

            var wrapped = WrapPlainLines(bodyLines, options.LineLimit);
            output.AddRange(wrapped);
            if (wrapped.Count > options.MaxLines)
            {
                diagnostics.Add(BuildLineLimitDiagnostic(options, segmentIndex, wrapped.Count));
            }

            bodyLines.Clear();
        }

        foreach (var line in lines)
        {
            if (IsDialogueSpeakerLine(line))
            {
                FlushBody();
                segmentIndex++;
                hasSegment = true;
                output.Add(line);
                continue;
            }

            bodyLines.Add(line);
        }

        FlushBody();
        if (!hasSegment && output.Count == 0 && text.Length == 0)
        {
            output.Add(string.Empty);
        }

        return new LegacyTextWrapResult(string.Join("\n", output), diagnostics);
    }

    private static LegacyTextWrapResult WrapPlainText(string text, LegacyTextWrapOptions options, int segmentIndex)
    {
        var wrapped = WrapPlainLines(text.Split('\n'), options.LineLimit);
        var diagnostics = new List<LegacyTextWrapDiagnostic>();
        if (wrapped.Count > options.MaxLines)
        {
            diagnostics.Add(BuildLineLimitDiagnostic(options, segmentIndex, wrapped.Count));
        }

        return new LegacyTextWrapResult(string.Join("\n", wrapped), diagnostics);
    }

    private static IReadOnlyList<string> WrapPlainLines(IEnumerable<string> lines, int lineLimit)
    {
        var output = new List<string>();
        foreach (var line in lines)
        {
            output.AddRange(WrapLine(line, lineLimit));
        }

        return output.Count == 0 ? [string.Empty] : output;
    }

    private static IReadOnlyList<string> WrapLine(string line, int lineLimit)
    {
        if (line.Length == 0)
        {
            return [string.Empty];
        }

        var output = new List<string>();
        var builder = new StringBuilder();
        var width = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var runeWidth = GetDisplayWidth(rune);
            if (builder.Length > 0 && width + runeWidth > lineLimit)
            {
                output.Add(builder.ToString());
                builder.Clear();
                width = 0;
            }

            builder.Append(rune.ToString());
            width += runeWidth;
        }

        output.Add(builder.ToString());
        return output;
    }

    private static int GetDisplayWidth(Rune rune)
    {
        var value = rune.Value;
        return value <= 0x7F ? 1 : 2;
    }

    private static bool IsDialogueSpeakerLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 0 && trimmed[0] is '&' or '＆';
    }

    private static LegacyTextWrapDiagnostic BuildLineLimitDiagnostic(
        LegacyTextWrapOptions options,
        int segmentIndex,
        int lineCount)
    {
        var segmentText = options.Mode == LegacyTextWrapMode.DialogueSegments
            ? $"第 {Math.Max(1, segmentIndex)} 段对白"
            : "当前文字";
        return new LegacyTextWrapDiagnostic(
            $"{segmentText}自动换行后为 {lineCount} 行，超过实机建议的 {options.MaxLines} 行；请拆成多段对白或多条指令。",
            Math.Max(0, segmentIndex),
            lineCount,
            options.MaxLines);
    }

    private static string NormalizeNewLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
