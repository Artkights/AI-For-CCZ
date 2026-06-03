namespace CCZModStudio.Models;

public sealed class ScenarioCommandParameterRow
{
    public int Index { get; init; }
    public string SlotName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string RawHex { get; init; } = string.Empty;
    public int DecimalValue { get; init; }
    public string DecodedValue { get; init; } = string.Empty;
    public string Meaning { get; init; } = string.Empty;
    public string Risk { get; init; } = string.Empty;
    public bool FromTemplate { get; init; }
    public string Annotation { get; init; } = string.Empty;
}
