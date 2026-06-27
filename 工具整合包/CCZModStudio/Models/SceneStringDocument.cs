namespace CCZModStudio.Models;

public sealed class SceneStringDocument
{
    public required string SourcePath { get; init; }
    public string EncodingName { get; init; } = string.Empty;
    public string DecodeConfidence { get; init; } = string.Empty;
    public IReadOnlyList<string> DecodeWarnings { get; init; } = Array.Empty<string>();
    public int SourceLineCount { get; init; }
    public string DecodeDiagnostic
    {
        get
        {
            var warning = DecodeWarnings.Count == 0 ? string.Empty : "；" + string.Join("；", DecodeWarnings);
            var encoding = string.IsNullOrWhiteSpace(EncodingName) ? "未知编码" : EncodingName;
            var confidence = string.IsNullOrWhiteSpace(DecodeConfidence) ? "未知" : DecodeConfidence;
            return $"{encoding}，置信度 {confidence}，行数 {SourceLineCount}，命令 {Commands.Count} 条{warning}";
        }
    }
    public IReadOnlyList<SceneCommandDefinition> Commands { get; init; } = Array.Empty<SceneCommandDefinition>();
    public IReadOnlyList<SceneStringGroup> Groups { get; init; } = Array.Empty<SceneStringGroup>();
}

public sealed class SceneCommandDefinition
{
    public int Id { get; init; }
    public string IdHex => CCZModStudio.Core.HexDisplayFormatter.Format(Id);
    public string Name { get; init; } = string.Empty;
}

public sealed class SceneStringGroup
{
    public int Index { get; init; }
    public string ItemsText { get; init; } = string.Empty;
    public int ItemCount { get; init; }
}
