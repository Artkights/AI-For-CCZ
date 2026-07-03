namespace CCZModStudio.Models;

public sealed class BattlefieldUnitReview
{
    public string TargetKey { get; set; } = string.Empty;
    public string ScenarioFileName { get; set; } = string.Empty;
    public string SourceCommand { get; set; } = string.Empty;
    public string SceneSection { get; set; } = string.Empty;
    public string OffsetHex { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = string.Empty;
    public string ReviewNote { get; set; } = string.Empty;
    public string UpdatedAtText { get; set; } = string.Empty;
    public bool IsPlacement { get; set; }
    public int PersonId { get; set; } = -1;
    public string UnitName { get; set; } = string.Empty;
    public int? JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public int SImageId { get; set; }
    public int RImageId { get; set; }
    public string Faction { get; set; } = string.Empty;
    public int LevelOffset { get; set; }
    public string LevelMode { get; set; } = string.Empty;
    public string AiMode { get; set; } = string.Empty;
    public bool Hidden { get; set; }
    public bool Reinforcement { get; set; }
    public string Direction { get; set; } = string.Empty;
    public int GridX { get; set; } = -1;
    public int GridY { get; set; } = -1;
    public string SafetyNote { get; set; } = "项目侧战场核对/布阵草稿：保存到 CCZModStudio_Notes，不写入游戏文件。";
}
