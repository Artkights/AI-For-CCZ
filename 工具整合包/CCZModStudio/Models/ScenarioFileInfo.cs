namespace CCZModStudio.Models;

public sealed class ScenarioFileInfo
{
    private string _lastNonZeroOffsetHex = string.Empty;
    private string _firstTextOffsetHex = string.Empty;
    private string _textHints = string.Empty;
    private string _annotation = string.Empty;

    public string FileName { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public long Length { get; init; }
    public string Kind { get; init; } = string.Empty;
    public int WordCount { get; init; }
    public int NonZeroWordCount { get; init; }
    public int DistinctWordCount { get; init; }
    public long UsedBytes { get; init; }
    public double UsedPercent { get; init; }
    public string LastNonZeroOffsetHex { get => _lastNonZeroOffsetHex; init => _lastNonZeroOffsetHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string FirstWordsHex { get; init; } = string.Empty;
    public string TopWordsHex { get; init; } = string.Empty;
    public int RecognizedCommandCount { get; init; }
    public string FirstCommandNames { get; init; } = string.Empty;
    public int TextHintCount { get; init; }
    public string FirstTextOffsetHex { get => _firstTextOffsetHex; init => _firstTextOffsetHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string TitleHint { get; init; } = string.Empty;
    public string TextHints { get => _textHints; init => _textHints = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string Annotation { get => _annotation; init => _annotation = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string UsageAnnotation { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
