namespace CCZModStudio.Models;

public sealed class ScenarioCommandClipboardItem
{
    public required string ScenarioFileName { get; init; }
    public int SourceSceneIndex { get; init; }
    public int SourceSectionIndex { get; init; }
    public int SourceCommandIndex { get; init; }
    public required string SourceOffsetHex { get; init; }
    public required string CommandIdHex { get; init; }
    public required string CommandName { get; init; }
    public required string ParameterPreview { get; init; }
    public required string CommandTemplateHint { get; init; }
    public required string ReferenceHint { get; init; }
    public required string Annotation { get; init; }
    public IReadOnlyList<ScenarioCommandParameterRow> Parameters { get; init; } = Array.Empty<ScenarioCommandParameterRow>();
}
