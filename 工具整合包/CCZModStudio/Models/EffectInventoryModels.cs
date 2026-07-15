using System.Text.Json.Serialization;

namespace CCZModStudio.Models;

public static class EffectInstanceSourceKind
{
    public const string Injected = "Injected";
    public const string Native = "Native";
    public const string Combined = "Combined";
}

public static class EffectParameterMeaningKind
{
    public const string Switch = "Switch";
    public const string Percentage = "Percentage";
    public const string FixedValue = "FixedValue";
    public const string Multiplier = "Multiplier";
    public const string Count = "Count";
    public const string Probability = "Probability";
    public const string Range = "Range";
    public const string Identifier = "Identifier";
    public const string Segmented = "Segmented";
    public const string Unknown = "Unknown";
}

public sealed class EffectInventoryReport
{
    public string AnalysisFingerprint { get; set; } = string.Empty;
    public string CacheState { get; set; } = string.Empty;
    public List<string> CompletedStages { get; set; } = [];
    public Dictionary<string, double> Performance { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ExecutableProfileAuditResult ProfileAudit { get; set; } = new();
    public string TargetFilePath { get; set; } = string.Empty;
    public string ExeSha256 { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public List<LogicalEffectInstance> Effects { get; set; } = [];
    public List<LogicalEffectInstance> InjectedEffects { get; set; } = [];
    public List<LogicalEffectInstance> ConfirmedInjectedEffects { get; set; } = [];
    public List<LogicalEffectInstance> NativeEffects { get; set; } = [];
    public List<LogicalEffectInstance> IncompleteOrHistoricalEffects { get; set; } = [];
    public int ManagedInjectedCount { get; set; }
    public int LegacyPresentCount { get; set; }
    public List<InjectedEffectDiagnostic> Diagnostics { get; set; } = [];
    public InjectedEffectDiscoveryReport Discovery { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    [JsonIgnore]
    public List<EffectChannelReference> PersonalJobOptions { get; set; } = [];
    [JsonIgnore]
    public List<EffectChannelReference> ItemOptions { get; set; } = [];
}

public sealed class InstalledEffectEvidenceDecision
{
    [Obsolete("Use IsPresent and IsToolManaged to distinguish byte presence from ownership.")]
    public bool IsInstalled { get => IsPresent; set => IsPresent = value; }
    public bool IsPresent { get; set; }
    public bool IsToolManaged { get; set; }
    public string OwnershipStatus { get; set; } = InstalledEffectOwnershipStatus.DiagnosticOnly;
    public string Status { get; set; } = "DiagnosticOnly";
    public string StatusZh { get; set; } = "仅供诊断";
    public string EvidenceExeSha256 { get; set; } = string.Empty;
    public bool HasCurrentHookBytes { get; set; }
    public bool HasReachableCodeBody { get; set; }
    public bool HasNormalizedBodySignature { get; set; }
    public bool HasCoreCall { get; set; }
    public bool HasValidReturnPath { get; set; }
    public bool HasCurrentProjectManifest { get; set; }
    public bool HasRequiredComplexFamilyEvidence { get; set; }
    public List<string> SatisfiedEvidenceZh { get; set; } = [];
    public List<string> MissingEvidenceZh { get; set; } = [];
}

public static class InstalledEffectOwnershipStatus
{
    public const string Managed = "Managed";
    public const string LegacyExact = "LegacyExact";
    public const string LegacyVariant = "LegacyVariant";
    public const string SampleSimilar = "SampleSimilar";
    public const string DiagnosticOnly = "DiagnosticOnly";
}

public sealed class ProjectPatchIdentity
{
    public string ProjectId { get; set; } = string.Empty;
    public string GameRoot { get; set; } = string.Empty;
    public string TargetFileName { get; set; } = "Ekd5.exe";
    public long TargetFileSize { get; set; }
    public string BaselineSha256 { get; set; } = string.Empty;
    public string CurrentSha256 { get; set; } = string.Empty;
    public string EngineProfileId { get; set; } = string.Empty;
}

public sealed class LogicalEffectInstance
{
    public string InstanceId { get; set; } = string.Empty;
    public string SourceKind { get; set; } = EffectInstanceSourceKind.Native;
    public bool HasInjectedImplementation { get; set; }
    public bool HasNativeImplementation { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TriggerPhase { get; set; } = string.Empty;
    public string NaturalLanguageDescription { get; set; } = string.Empty;
    public string CurrentParameterSummary { get; set; } = string.Empty;
    public string PatchCategory { get; set; } = string.Empty;
    public bool IsEditable { get; set; }
    public EffectWriteDecision? WriteDecision { get; set; }
    public string EditabilityReason { get; set; } = string.Empty;
    public int DetectionScore { get; set; }
    public string EvidenceLevel { get; set; } = string.Empty;
    public List<uint> EntryHooks { get; set; } = [];
    public List<uint> CodeEntries { get; set; } = [];
    public List<uint> CoreCalls { get; set; } = [];
    public List<uint> ConsumerFunctionAddresses { get; set; } = [];
    public List<uint> WrapperEntries { get; set; } = [];
    public List<string> WrapperContractIds { get; set; } = [];
    public string ComplexFamilyContractId { get; set; } = string.Empty;
    public string InstallationStatus { get; set; } = string.Empty;
    public string InstallationStatusZh { get; set; } = string.Empty;
    public string WritableContractId { get; set; } = string.Empty;
    public string EvidenceExeSha256 { get; set; } = string.Empty;
    public List<string> ConsumerChainZh { get; set; } = [];
    public List<LogicalEffectParameter> Parameters { get; set; } = [];
    public EffectChannelReference? PersonalChannel { get; set; }
    public EffectChannelReference? ItemChannel { get; set; }
    public List<EffectImplementationReference> Implementations { get; set; } = [];
    public List<string> MatchedEvidence { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public List<string> CandidateKeys { get; set; } = [];
}

public sealed class LogicalEffectParameter
{
    public string SlotId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string MeaningKind { get; set; } = EffectParameterMeaningKind.Unknown;
    public string Unit { get; set; } = string.Empty;
    public int? Value { get; set; }
    public uint? Address { get; set; }
    public int ByteLength { get; set; }
    public int? Minimum { get; set; }
    public int? Maximum { get; set; }
    public bool IsEditable { get; set; }
    public EffectWriteDecision? WriteDecision { get; set; }
    public string NaturalLanguageMeaning { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public bool IsConsistent { get; set; } = true;
    public List<int> ObservedValues { get; set; } = [];
    public List<EffectPhysicalPatchPoint> PhysicalPatchPoints { get; set; } = [];
}

public static class EffectChannelKind
{
    public const string PersonalJob = "PersonalJob";
    public const string Item = "Item";
}

public sealed class EffectChannelReference
{
    public string Channel { get; set; } = string.Empty;
    public int EffectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string NameSource { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsConfigured { get; set; }
    public List<EffectBindingReference> Bindings { get; set; } = [];
    public List<string> Conflicts { get; set; } = [];
}

public sealed class EffectBindingReference
{
    public string Kind { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int? EffectValue { get; set; }
    public EffectPackageBinding? PackageBinding { get; set; }
}

public sealed class EffectImplementationReference
{
    public string SourceKind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SignatureId { get; set; } = string.Empty;
    public List<uint> EntryHooks { get; set; } = [];
    public List<uint> CodeEntries { get; set; } = [];
    public List<uint> WrapperEntries { get; set; } = [];
}

public sealed class EffectPhysicalPatchPoint
{
    public uint Address { get; set; }
    public int ByteLength { get; set; }
    public int Value { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string ExpectedOldBytesHex { get; set; } = string.Empty;
}

public sealed class EffectSemanticRuleManifest
{
    public string RuleId { get; set; } = string.Empty;
    public List<string> NamePatterns { get; set; } = [];
    public string TriggerPhase { get; set; } = string.Empty;
    public string DescriptionTemplate { get; set; } = string.Empty;
    public string ValueMeaningKind { get; set; } = EffectParameterMeaningKind.Unknown;
    public string ValueUnit { get; set; } = string.Empty;
    public string ValueRule { get; set; } = string.Empty;
    public string Complexity { get; set; } = string.Empty;
    public List<string> Reads { get; set; } = [];
    public List<string> Writes { get; set; } = [];
    public List<string> SafelyEditableRoles { get; set; } = [];
}

public sealed class EffectParameterUpdateRequest
{
    public string InstanceId { get; set; } = string.Empty;
    public List<EffectParameterValueUpdate> Updates { get; set; } = [];
}

public sealed class EffectParameterValueUpdate
{
    public string SlotId { get; set; } = string.Empty;
    public int Value { get; set; }
}

public sealed class EffectParameterUpdatePreview
{
    public bool CanApply { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
    public EffectPackage Package { get; set; } = new();
    public EffectPatchPreviewResult PatchPreview { get; set; } = new();
}

public sealed class AddressSemanticRecord
{
    public string TargetFilePath { get; set; } = string.Empty;
    public string ExeSha256 { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public uint Address { get; set; }
    public string AddressHex => $"0x{Address:X8}";
    public uint Rva { get; set; }
    public string RvaHex => $"0x{Rva:X8}";
    public long FileOffset { get; set; }
    public string FileOffsetHex => $"0x{FileOffset:X}";
    public string SectionName { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
    public string TriggerPhase { get; set; } = string.Empty;
    public string InstructionText { get; set; } = string.Empty;
    public string ChineseExplanation { get; set; } = string.Empty;
    public string FlowControl { get; set; } = string.Empty;
    public uint? BranchTarget { get; set; }
    public List<string> RegistersRead { get; set; } = [];
    public List<string> RegistersWritten { get; set; } = [];
    public List<string> MemoryReads { get; set; } = [];
    public List<string> MemoryWrites { get; set; } = [];
    public List<string> CrossReferences { get; set; } = [];
    public List<string> Evidence { get; set; } = [];
}
