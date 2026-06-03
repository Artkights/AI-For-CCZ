namespace CCZModStudio.Models;

/// <summary>
/// 制作向导中的“优先行动”可视化条目。它只描述下一步建议和跳转目标，不直接修改游戏文件。
/// </summary>
public sealed class WorkflowActionItem
{
    public int PriorityNo { get; init; }
    public string Level { get; init; } = string.Empty;
    public string TargetArea { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string ExpectedResult { get; init; } = string.Empty;
    public string SafetyNote { get; init; } = string.Empty;
    public string NoteHint { get; init; } = string.Empty;
}
