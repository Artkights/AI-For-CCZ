namespace CCZModStudio.Models;

/// <summary>
/// 创作者项目侧备注。备注保存在 CCZModStudio_Notes 中，不写入游戏文件。
/// </summary>
public sealed class CreatorNote
{
    public string Id { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Scope { get; set; } = "全局项目";
    public string TargetKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string SourceHint { get; set; } = string.Empty;
    public string CreatedAtText { get; set; } = string.Empty;
    public string UpdatedAtText { get; set; } = string.Empty;
    public string SafetyNote { get; set; } = "项目侧备注：不写入游戏文件，不参与发布封包。";
}
