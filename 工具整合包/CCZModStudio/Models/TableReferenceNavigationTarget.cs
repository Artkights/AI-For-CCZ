namespace CCZModStudio.Models;

/// <summary>
/// 数据表单元格跨表引用导航目标。
/// 用于把“人物职业 -> 详细兵种”“商店槽位 -> 物品表”“装备特效号 -> 装备特效名称列”等关系变成可视化跳转。
/// </summary>
public sealed class TableReferenceNavigationTarget
{
    public bool IsRecognized { get; init; }
    public bool IsOptionalEmpty { get; init; }
    public bool TargetRowExists { get; init; }
    public string SourceTableName { get; init; } = string.Empty;
    public string SourceRowId { get; init; } = string.Empty;
    public string SourceFieldName { get; init; } = string.Empty;
    public string SourceValue { get; init; } = string.Empty;
    public string ReferenceKind { get; init; } = string.Empty;
    public string TargetTableName { get; init; } = string.Empty;
    public string TargetRowId { get; init; } = string.Empty;
    public string TargetFieldName { get; init; } = string.Empty;
    public string TargetName { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string SafetyNote { get; init; } = string.Empty;

    public bool CanNavigate =>
        IsRecognized &&
        !IsOptionalEmpty &&
        TargetRowExists &&
        !string.IsNullOrWhiteSpace(TargetTableName) &&
        !string.IsNullOrWhiteSpace(TargetRowId) &&
        !string.IsNullOrWhiteSpace(TargetFieldName);
}
