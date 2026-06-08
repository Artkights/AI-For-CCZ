namespace CCZModStudio.Models;

/// <summary>
/// SV 命令参数模板目录中的可视化行。
/// 这些条目只用于中文解释、筛选、核对记录和核对，不代表完整命令长度已确认。
/// </summary>
public sealed class ScenarioCommandTemplateCatalogItem
{
    public int Id { get; init; }
    public string IdHex { get; init; } = string.Empty;
    public string DictionaryName { get; init; } = string.Empty;
    public string TemplateName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int SlotCount { get; init; }
    public string SlotSummary { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public string Risk { get; init; } = string.Empty;
    public string SlotDetails { get; init; } = string.Empty;
    public string CreatorTip { get; init; } = string.Empty;
    public string SafetyNote { get; init; } = string.Empty;
}
