namespace CCZModStudio.Models;

public sealed class ScenarioSearchResultRow
{
    public int Index { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Preview { get; init; } = string.Empty;
    public int MatchCount { get; init; }
    public int ReplaceableMatchCount { get; init; }
    public string Annotation { get; init; } = string.Empty;
    public string ActionHint { get; init; } = string.Empty;
    public IReadOnlyList<ScenarioSearchMatch> Matches { get; init; } = Array.Empty<ScenarioSearchMatch>();
    public IReadOnlyList<ScenarioStructureRow> RelatedCommandRows { get; init; } = Array.Empty<ScenarioStructureRow>();
    public ScenarioStructureRow? CommandRow { get; init; }
    public ScenarioTextEntry? TextEntry { get; init; }
}

public sealed class ScenarioSearchMatch
{
    public const string TextFieldName = "文本";
    public const string PreviewFieldName = "预览";

    public string FieldName { get; init; } = string.Empty;
    public string FieldText { get; init; } = string.Empty;
    public int Start { get; init; }
    public int Length { get; init; }
    public string Text { get; init; } = string.Empty;
    public bool IsReplaceable { get; init; }
    public ScenarioSearchReplaceTarget ReplaceTarget { get; init; } = ScenarioSearchReplaceTarget.None;
    public ScenarioSearchProtectionKind ProtectionKind { get; init; } = ScenarioSearchProtectionKind.None;
    public string ProtectionDetail { get; init; } = string.Empty;
    public LegacyScenarioCommandParameter? CommandParameter { get; init; }
}

public enum ScenarioSearchReplaceTarget
{
    None,
    TextEntryText,
    CommandTextParameter,
    CommandScalarParameter,
    GridStringCell
}

public enum ScenarioSearchProtectionKind
{
    None,
    CommandIdentity,
    StructureField,
    DerivedDisplayName,
    UnboundCommandParameter
}
