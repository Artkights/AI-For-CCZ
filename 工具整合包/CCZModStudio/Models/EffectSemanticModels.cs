namespace CCZModStudio.Models;

public static class EffectSemanticEvidenceLevel
{
    public const string VerifiedDynamic = "VerifiedDynamic";
    public const string VerifiedStatic = "VerifiedStatic";
    public const string KnownSample = "KnownSample";
    public const string ExternalCorroboration = "ExternalCorroboration";
    public const string Hypothesis = "Hypothesis";
}

public static class EffectSemanticKind
{
    public const string SwitchEffect = "SwitchEffect";
    public const string ValueEffect = "ValueEffect";
    public const string DamageModifier = "DamageModifier";
    public const string Recovery = "Recovery";
    public const string StatusInflict = "StatusInflict";
    public const string ActionControl = "ActionControl";
    public const string StrategyModifier = "StrategyModifier";
    public const string EngineExtension = "EngineExtension";
    public const string UnknownCandidate = "UnknownCandidate";
}

public sealed class FunctionSemanticCatalog
{
    public string TargetFilePath { get; set; } = string.Empty;
    public string TargetFileName { get; set; } = "Ekd5.exe";
    public string ExeSha256 { get; set; } = string.Empty;
    public uint ImageBase { get; set; }
    public string ImageBaseHex => $"0x{ImageBase:X8}";
    public string Summary { get; set; } = string.Empty;
    public List<FunctionSemanticRecord> Functions { get; set; } = [];
    public List<EffectMeaningRecord> Effects { get; set; } = [];
    public AgentSpecialEffectKnowledge AgentKnowledge { get; set; } = new();
    public List<string> SourceDocuments { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> ReportPaths { get; set; } = [];
}

public sealed class FunctionSemanticRecord
{
    public uint Address { get; set; }
    public string AddressHex => Address == 0 ? string.Empty : $"0x{Address:X8}";
    public int FileOffset { get; set; } = -1;
    public string FileOffsetHex => FileOffset < 0 ? string.Empty : $"0x{FileOffset:X}";
    public string Name { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int ConfidenceScore { get; set; }
    public string EvidenceLevel { get; set; } = EffectSemanticEvidenceLevel.Hypothesis;
    public string SemanticKind { get; set; } = EffectSemanticKind.UnknownCandidate;
    public List<string> MatchedEvidence { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public List<string> Reads { get; set; } = [];
    public List<string> Writes { get; set; } = [];
    public List<uint> Calls { get; set; } = [];
    public List<uint> CalledBy { get; set; } = [];
    public List<int> RelatedEffectIds { get; set; } = [];
    public string SourceSummary { get; set; } = string.Empty;
}

public sealed class EffectMeaningRecord
{
    public int EffectId { get; set; }
    public string EffectIdHex => $"0x{EffectId:X2}";
    public string Channel { get; set; } = string.Empty;
    public List<string> NameCandidates { get; set; } = [];
    public string ObservedMeaning { get; set; } = string.Empty;
    public string SemanticKind { get; set; } = EffectSemanticKind.UnknownCandidate;
    public string ValueFlagMeaning { get; set; } = string.Empty;
    public string StackingMeaning { get; set; } = string.Empty;
    public string TriggerPhase { get; set; } = string.Empty;
    public string ImplementationFunction { get; set; } = string.Empty;
    public string EvidenceLevel { get; set; } = EffectSemanticEvidenceLevel.KnownSample;
    public int ConfidenceScore { get; set; }
    public string RecommendedInjectionTemplate { get; set; } = string.Empty;
    public List<string> MatchedEvidence { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public string SourceSummary { get; set; } = string.Empty;
}

public sealed class AgentSpecialEffectKnowledge
{
    public string UsagePolicy { get; set; } = "local-agent knowledge only; generated code must use MCP preview/apply";
    public int ContextBudgetCharacters { get; set; } = 12000;
    public List<FunctionSemanticRecord> CallableFunctions { get; set; } = [];
    public List<EffectMeaningRecord> EffectTemplates { get; set; } = [];
    public List<string> Guardrails { get; set; } = [];
    public List<HookContract> HookContracts { get; set; } = [];
    public List<string> DraftSafetyFields { get; set; } = [];
    public string AgentContext { get; set; } = string.Empty;
}
