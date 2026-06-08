namespace CCZModStudio.Models;

public sealed class BattlefieldPlacedUnit
{
    public string TargetKey { get; set; } = string.Empty;
    public int PersonId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public int SImageId { get; set; }
    public int RImageId { get; set; }
    public string Faction { get; set; } = "我军";
    public int LevelOffset { get; set; }
    public string LevelMode { get; set; } = "初级";
    public string AiMode { get; set; } = "被动";
    public bool Hidden { get; set; }
    public string Direction { get; set; } = "下";
    public int GridX { get; set; }
    public int GridY { get; set; }
    public string Source { get; set; } = "拖放";
    public string PlacementNote { get; set; } = string.Empty;
}
