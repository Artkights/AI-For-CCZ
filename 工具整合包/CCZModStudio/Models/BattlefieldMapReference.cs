using System.Globalization;

namespace CCZModStudio.Models;

public enum BattlefieldMapReferenceSource
{
    Unresolved,
    BackgroundCommand27,
    ScenarioNumberFallback
}

public sealed class BattlefieldMapReference
{
    public static BattlefieldMapReference Unresolved { get; } = new();

    public int? MapNumber { get; init; }
    public string MapId { get; init; } = string.Empty;
    public BattlefieldMapReferenceSource SourceKind { get; init; }
    public string ScenarioFileName { get; init; } = string.Empty;
    public int? SceneIndex { get; init; }
    public int? SectionIndex { get; init; }
    public int? CommandIndex { get; init; }
    public string OffsetHex { get; init; } = string.Empty;

    public string DisplayText
    {
        get
        {
            if (SourceKind == BattlefieldMapReferenceSource.BackgroundCommand27)
            {
                var location = SceneIndex.HasValue && SectionIndex.HasValue && CommandIndex.HasValue
                    ? $"S{SceneIndex.Value.ToString(CultureInfo.InvariantCulture)}/Section{SectionIndex.Value.ToString(CultureInfo.InvariantCulture)}/C{CommandIndex.Value.ToString(CultureInfo.InvariantCulture)}"
                    : "位置未知";
                return $"地图 {MapId} · 来源 0x27 {location}";
            }

            if (SourceKind == BattlefieldMapReferenceSource.ScenarioNumberFallback)
            {
                var scenarioName = Path.GetFileNameWithoutExtension(ScenarioFileName);
                return string.IsNullOrWhiteSpace(scenarioName)
                    ? $"地图 {MapId} · 来源战场编号回退"
                    : $"地图 {MapId} · 来源 {scenarioName} 编号回退";
            }

            return "地图未解析";
        }
    }
}
