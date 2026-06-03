namespace CCZModStudio.Models;

/// <summary>
/// 制作向导顶部的创作者工作台卡片。只汇总当前项目状态和工具产物，不直接修改游戏文件。
/// </summary>
public sealed class WorkflowDashboardItem
{
    public string Area { get; init; } = string.Empty;
    public string Level { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Suggestion { get; init; } = string.Empty;
    public string RelatedPage { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
}
