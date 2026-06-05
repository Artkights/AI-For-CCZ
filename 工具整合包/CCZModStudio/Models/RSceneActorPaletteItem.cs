namespace CCZModStudio.Models;

public sealed class RSceneActorPaletteItem
{
    public int Index { get; init; }
    public int PersonId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int? JobId { get; init; }
    public string JobName { get; init; } = string.Empty;
    public int RImageId { get; init; }
    public int SImageId { get; init; }
    public string DisplayText => $"{PersonId} {Name}";
    public string DetailText => $"职业={JobId?.ToString() ?? "?"} {JobName}  R={RImageId}  S={SImageId}";
}
