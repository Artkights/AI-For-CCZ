namespace CCZModStudio.Models;

public sealed class BattlefieldUnitCandidate
{
    private string _sourceCommand = string.Empty;
    private string _offsetHex = string.Empty;
    private string _annotation = string.Empty;

    public int Index { get; init; }
    public int? BattlefieldNumber { get; init; }
    public string SourceCommandDisplay { get; init; } = string.Empty;
    public string PersonDisplay { get; init; } = string.Empty;
    public string CoordinateDisplay { get; init; } = string.Empty;
    public string FactionDisplay { get; init; } = string.Empty;
    public string AiDisplay { get; init; } = string.Empty;
    public string LevelJobDisplay { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string SourceCommand { get => _sourceCommand; init => _sourceCommand = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string SceneSection { get; init; } = string.Empty;
    public string OffsetHex { get => _offsetHex; init => _offsetHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string PersonHint { get; init; } = string.Empty;
    public string CoordinateHint { get; init; } = string.Empty;
    public string FactionHint { get; init; } = string.Empty;
    public string AiHint { get; init; } = string.Empty;
    public string LevelOrStateHint { get; init; } = string.Empty;
    public string Annotation { get => _annotation; init => _annotation = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string TargetKey { get; init; } = string.Empty;
    public string ReviewStatus { get; set; } = string.Empty;
    public string ReviewNote { get; set; } = string.Empty;
}
