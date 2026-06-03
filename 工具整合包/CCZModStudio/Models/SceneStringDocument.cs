namespace CCZModStudio.Models;

public sealed class SceneStringDocument
{
    public required string SourcePath { get; init; }
    public IReadOnlyList<SceneCommandDefinition> Commands { get; init; } = Array.Empty<SceneCommandDefinition>();
    public IReadOnlyList<SceneStringGroup> Groups { get; init; } = Array.Empty<SceneStringGroup>();
}

public sealed class SceneCommandDefinition
{
    public int Id { get; init; }
    public string IdHex => "0x" + Id.ToString("X");
    public string Name { get; init; } = string.Empty;
}

public sealed class SceneStringGroup
{
    public int Index { get; init; }
    public string ItemsText { get; init; } = string.Empty;
    public int ItemCount { get; init; }
}
