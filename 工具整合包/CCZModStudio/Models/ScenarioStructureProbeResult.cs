namespace CCZModStudio.Models;

public sealed class ScenarioStructureProbeResult
{
    private string _summary = string.Empty;
    private string _xmlText = string.Empty;

    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public int CommandCandidateCount { get; init; }
    public int SceneCount { get; init; }
    public int SectionCount { get; init; }
    public bool UsedLegacyParser { get; init; }
    public required string Summary { get => _summary; init => _summary = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public required string XmlText { get => _xmlText; init => _xmlText = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public IReadOnlyList<ScenarioStructureRow> Rows { get; init; } = Array.Empty<ScenarioStructureRow>();
}

public sealed class ScenarioStructureRow
{
    private string _offsetHex = string.Empty;
    private string _commandIdHex = string.Empty;
    private string _parameterPreview = string.Empty;
    private string _rawContextWordsHex = string.Empty;
    private string _legacyParameterLayout = string.Empty;
    private string _commandTemplateHint = string.Empty;
    private string _referenceHint = string.Empty;
    private string _annotation = string.Empty;

    public int Index { get; init; }
    public int Level { get; init; }
    public string NodeType { get; init; } = string.Empty;
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int CommandIndex { get; init; }
    public string OffsetHex { get => _offsetHex; init => _offsetHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public int CommandId { get; init; }
    public string CommandIdHex { get => _commandIdHex; init => _commandIdHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string CommandName { get; init; } = string.Empty;
    public string ParameterPreview { get => _parameterPreview; init => _parameterPreview = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string RawContextWordsHex { get => _rawContextWordsHex; init => _rawContextWordsHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string LegacyParameterLayout { get => _legacyParameterLayout; init => _legacyParameterLayout = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public bool StartsBodyBlock { get; init; }
    public bool OpensSubEventBlock { get; init; }
    public bool EndsSubEventBlock { get; init; }
    public bool HasCommandTemplate { get; init; }
    public string CommandTemplateHint { get => _commandTemplateHint; init => _commandTemplateHint = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string ReferenceHint { get => _referenceHint; init => _referenceHint = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string Confidence { get; init; } = string.Empty;
    public string Annotation { get => _annotation; init => _annotation = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
}
