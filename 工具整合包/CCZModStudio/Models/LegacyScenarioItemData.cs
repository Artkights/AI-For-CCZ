namespace CCZModStudio.Models;

public sealed class LegacyScenarioItemData
{
    public required int Id { get; init; }
    public int Ord { get; set; }
    public List<int> IntData { get; } = [];
    public string LongCharData { get; set; } = string.Empty;
    public LegacyScenarioItemData? Child { get; set; }
    public LegacyScenarioItemData? Sibling { get; set; }
    public LegacyScenarioCommandNode? Command { get; init; }
    public LegacyScenarioScene? Scene { get; init; }
    public LegacyScenarioSection? Section { get; init; }
    public object? UiRow { get; set; }

    public LegacyScenarioItemData CloneSnapshot()
    {
        var clone = new LegacyScenarioItemData
        {
            Id = Id,
            Ord = Ord,
            LongCharData = LongCharData,
            Command = Command,
            Scene = Scene,
            Section = Section,
            UiRow = UiRow
        };
        clone.IntData.AddRange(IntData);
        return clone;
    }
}
