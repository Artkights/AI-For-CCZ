namespace CCZModStudio.Models;

public sealed class BattlefieldUnitReview
{
    public string TargetKey { get; set; } = string.Empty;
    public string ScenarioFileName { get; set; } = string.Empty;
    public string SourceCommand { get; set; } = string.Empty;
    public string SceneSection { get; set; } = string.Empty;
    public string OffsetHex { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = string.Empty;
    public string CreatorMemo { get; set; } = string.Empty;
    public string UpdatedAtText { get; set; } = string.Empty;
    public string SafetyNote { get; set; } = "项目侧战场核对备注：不写入游戏文件，不参与发布封包。";
}
