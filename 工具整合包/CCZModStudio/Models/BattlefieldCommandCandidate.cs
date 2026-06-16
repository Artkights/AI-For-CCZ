namespace CCZModStudio.Models;

public sealed class BattlefieldCommandCandidate
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
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int CommandIndex { get; init; }
    public string OffsetHex { get => _offsetHex; init => _offsetHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string CommandIdHex { get => _commandIdHex; init => _commandIdHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string CommandName { get; init; } = string.Empty;
    public string RoleHint { get; init; } = string.Empty;
    public string ParameterPreview { get => _parameterPreview; init => _parameterPreview = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string RawContextWordsHex { get => _rawContextWordsHex; init => _rawContextWordsHex = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string LegacyParameterLayout { get => _legacyParameterLayout; init => _legacyParameterLayout = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string CommandTemplateHint { get => _commandTemplateHint; init => _commandTemplateHint = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string ReferenceHint { get => _referenceHint; init => _referenceHint = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
    public string Annotation { get => _annotation; init => _annotation = CCZModStudio.Core.HexDisplayFormatter.NormalizeText(value); }
}
