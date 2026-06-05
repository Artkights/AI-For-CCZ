namespace CCZModStudio.Models;

public sealed class RScenePlacedActor
{
    public string TargetKey { get; set; } = string.Empty;
    public int PersonId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public int RImageId { get; set; }
    public int SImageId { get; set; }
    public string Facing { get; set; } = "下";
    public int FrameIndex { get; set; }
    public int GridX { get; set; }
    public int GridY { get; set; }
    public int PixelX { get; set; }
    public int PixelY { get; set; }
    public string Source { get; set; } = "拖放";
    public string Memo { get; set; } = string.Empty;
}
