namespace CCZModStudio.Models;

public sealed class EffectConfirmationLedger
{
    public string AnalysisFingerprint { get; set; } = string.Empty;
    public string FullExeSha256 { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileTrustStatus { get; set; } = string.Empty;
    public string BuildIdentity { get; set; } = string.Empty;
    public string CapabilitySchema { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public List<EffectConfirmationEntry> Effects { get; set; } = [];
    public Dictionary<string, double> Performance { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class EffectConfirmationEntry
{
    public string InstanceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string PatchCategory { get; set; } = string.Empty;
    public int? PersonalEffectId { get; set; }
    public int? ItemEffectId { get; set; }
    public string TriggerPhase { get; set; } = string.Empty;
    public string PhysicalEvidenceZh { get; set; } = string.Empty;
    public string StructuralEvidenceZh { get; set; } = string.Empty;
    public string SemanticEvidenceZh { get; set; } = string.Empty;
    public string DynamicEvidenceZh { get; set; } = string.Empty;
    public string ConclusionZh { get; set; } = string.Empty;
    public string NextActionZh { get; set; } = string.Empty;
    public int AffectedConsumers { get; set; }
    public List<string> BlockerCodes { get; set; } = [];
    public List<string> EvidenceReferences { get; set; } = [];
    public List<EffectConfirmationField> Fields { get; set; } = [];
}

public sealed class EffectConfirmationField
{
    public string FieldId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public string CurrentValueZh { get; set; } = string.Empty;
    public int PhysicalLocationCount { get; set; }
    public bool HasExactLocation { get; set; }
    public string ContractId { get; set; } = string.Empty;
    public EffectWriteDecision Decision { get; set; } = new();
}

public sealed class EffectConfirmationExportResult
{
    public string JsonPath { get; set; } = string.Empty;
    public string MarkdownPath { get; set; } = string.Empty;
    public int EffectCount { get; set; }
    public int FieldCount { get; set; }
    public string SummaryZh { get; set; } = string.Empty;
}
