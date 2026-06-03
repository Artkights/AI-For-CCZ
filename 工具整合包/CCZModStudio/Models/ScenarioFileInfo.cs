namespace CCZModStudio.Models;

public sealed class ScenarioFileInfo
{
    public string FileName { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public long Length { get; init; }
    public string Kind { get; init; } = string.Empty;
    public int WordCount { get; init; }
    public int NonZeroWordCount { get; init; }
    public int DistinctWordCount { get; init; }
    public long UsedBytes { get; init; }
    public double UsedPercent { get; init; }
    public string LastNonZeroOffsetHex { get; init; } = string.Empty;
    public string FirstWordsHex { get; init; } = string.Empty;
    public string TopWordsHex { get; init; } = string.Empty;
    public int RecognizedCommandCount { get; init; }
    public string FirstCommandNames { get; init; } = string.Empty;
    public int TextHintCount { get; init; }
    public string FirstTextOffsetHex { get; init; } = string.Empty;
    public string TitleHint { get; init; } = string.Empty;
    public string TextHints { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
    public string UsageAnnotation { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
