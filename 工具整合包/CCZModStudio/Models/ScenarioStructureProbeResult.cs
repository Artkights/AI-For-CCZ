namespace CCZModStudio.Models;

public sealed class ScenarioStructureProbeResult
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public int CommandCandidateCount { get; init; }
    public int SceneCount { get; init; }
    public int SectionCount { get; init; }
    public bool UsedLegacyParser { get; init; }
    public required string Summary { get; init; }
    public required string XmlText { get; init; }
    public IReadOnlyList<ScenarioStructureRow> Rows { get; init; } = Array.Empty<ScenarioStructureRow>();
}

public sealed class ScenarioStructureRow
{
    public int Index { get; init; }
    public int Level { get; init; }
    public string NodeType { get; init; } = string.Empty;
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int CommandIndex { get; init; }
    public string OffsetHex { get; init; } = string.Empty;
    public int CommandId { get; init; }
    public string CommandIdHex { get; init; } = string.Empty;
    public string CommandName { get; init; } = string.Empty;
    public string ParameterPreview { get; init; } = string.Empty;
    public string RawContextWordsHex { get; init; } = string.Empty;
    public string LegacyParameterLayout { get; init; } = string.Empty;
    public bool StartsBodyBlock { get; init; }
    public bool OpensSubEventBlock { get; init; }
    public bool EndsSubEventBlock { get; init; }
    public bool HasCommandTemplate { get; init; }
    public string CommandTemplateHint { get; init; } = string.Empty;
    public string ReferenceHint { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
}
