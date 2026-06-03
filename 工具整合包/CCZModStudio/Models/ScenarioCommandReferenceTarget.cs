namespace CCZModStudio.Models;

/// <summary>
/// SV 命令参数中的“可跳转引用候选”。
/// 当前仍是只读候选：用于把命令参数里疑似人物、物品、策略、文本、地图/坐标的值跳到相关页面核对。
/// </summary>
public sealed class ScenarioCommandReferenceTarget
{
    public string Kind { get; init; } = string.Empty;
    public string DisplayText { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string SafetyNote { get; init; } = string.Empty;
    public string ScenarioFileName { get; init; } = string.Empty;
    public int CommandIndex { get; init; }
    public string CommandOffsetHex { get; init; } = string.Empty;
    public int? RawValue { get; init; }
    public string TableName { get; init; } = string.Empty;
    public string RowId { get; init; } = string.Empty;
    public string FieldName { get; init; } = string.Empty;
    public string MapId { get; init; } = string.Empty;
    public string TextOffsetHex { get; init; } = string.Empty;
    public int? TextIndex { get; init; }
    public int? CoordinateX { get; init; }
    public int? CoordinateY { get; init; }

    public bool CanJumpDataTable =>
        !string.IsNullOrWhiteSpace(TableName) &&
        !string.IsNullOrWhiteSpace(RowId) &&
        !string.IsNullOrWhiteSpace(FieldName);

    public bool CanJumpScenarioMap =>
        !string.IsNullOrWhiteSpace(MapId) ||
        (!string.IsNullOrWhiteSpace(ScenarioFileName) && CoordinateX.HasValue && CoordinateY.HasValue);

    public bool CanJumpScenarioText =>
        !string.IsNullOrWhiteSpace(ScenarioFileName) &&
        (TextIndex.HasValue || !string.IsNullOrWhiteSpace(TextOffsetHex));

    public bool CanNavigate => CanJumpDataTable || CanJumpScenarioMap || CanJumpScenarioText;

    public override string ToString() => string.IsNullOrWhiteSpace(DisplayText) ? Kind : DisplayText;
}
