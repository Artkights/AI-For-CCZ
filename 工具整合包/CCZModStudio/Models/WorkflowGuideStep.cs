namespace CCZModStudio.Models;

/// <summary>
/// 制作向导中的单步说明。只描述项目侧流程，不直接读写游戏文件。
/// </summary>
public sealed class WorkflowGuideStep
{
    public int StepNo { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public string RelatedPage { get; init; } = string.Empty;
    public string WhyItMatters { get; init; } = string.Empty;
    public string SafetyNote { get; init; } = string.Empty;
}
