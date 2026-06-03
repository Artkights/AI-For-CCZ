namespace CCZModStudio.Models;

public sealed class ScenarioCommandProbeRow
{
    public int Index { get; init; }
    public int WordIndex { get; init; }
    public string OffsetHex { get; init; } = string.Empty;
    public string CommandIdHex { get; init; } = string.Empty;
    public int CommandId { get; init; }
    public string CommandName { get; init; } = string.Empty;
    public string ContextWordsHex { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
}
