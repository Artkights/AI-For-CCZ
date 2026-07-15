using CCZModStudio.Core;

namespace CCZModStudio.Models;

public static class CompositeEffectChannel
{
    public const string PersonalJob = "PersonalJob";
    public const string Item = "Item";
}

public static class NativeEffectEditField
{
    public const string Name = "Name";
    public const string Description = "Description";
    public const string PersonalEffectId = "PersonalEffectId";
    public const string ItemEffectId = "ItemEffectId";
    public const string EffectValueMode = "EffectValueMode";
    public const string StackingMode = "StackingMode";
}

public sealed class EngineEffectMechanismProfile
{
    public string EngineVersion { get; set; } = string.Empty;
    public string ExeSha256 { get; set; } = string.Empty;
    public bool IsKnownWritableProfile { get; set; }
    public string WritableProfileId { get; set; } = string.Empty;
    public string EvidenceExeSha256 { get; set; } = string.Empty;
    public string EvidenceScopeZh { get; set; } = string.Empty;
    public List<EngineEffectFunctionRecord> Functions { get; set; } = [];
    public List<EffectConsumerRecord> Consumers { get; set; } = [];
    public List<CompositeIdentityContract> IdentityContracts { get; set; } = [];
    public List<WrapperContract> WrapperContracts { get; set; } = [];
    public List<ComplexEffectFamilyContract> ComplexFamilyContracts { get; set; } = [];
    public List<string> WarningsZh { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class EngineEffectFunctionRecord
{
    public uint Address { get; set; }
    public string AddressHex => $"0x{Address:X8}";
    public string Name { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public string RoleZh { get; set; } = string.Empty;
    public string CallingConventionZh { get; set; } = string.Empty;
    public string ReturnValueZh { get; set; } = string.Empty;
    public string EvidenceExeSha256 { get; set; } = string.Empty;
    public string EvidenceLevel { get; set; } = "VerifiedStatic";
    public string EvidenceScopeZh { get; set; } = string.Empty;
    public List<string> SourceFiles { get; set; } = [];
    public List<string> EvidenceZh { get; set; } = [];
}

public sealed class EffectConsumerRecord
{
    public string InstanceId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public string TriggerPhaseZh { get; set; } = string.Empty;
    public string ReturnUsage { get; set; } = string.Empty;
    public string ReturnUsageZh { get; set; } = string.Empty;
    public string MeaningZh { get; set; } = string.Empty;
    public EffectSemanticExplanation Explanation { get; set; } = new();
    public List<string> ConsumerChainZh { get; set; } = [];
    public List<uint> CallSites { get; set; } = [];
    public bool IsWritable { get; set; }
    public string EditabilityReasonZh { get; set; } = string.Empty;
}

public sealed class EffectSemanticExplanation
{
    public string TriggerZh { get; set; } = string.Empty;
    public string SubjectZh { get; set; } = string.Empty;
    public string ChannelDecisionZh { get; set; } = string.Empty;
    public string ReturnValueUsageZh { get; set; } = string.Empty;
    public string StateChangeZh { get; set; } = string.Empty;
    public string SummaryZh { get; set; } = string.Empty;
    public List<string> EvidenceZh { get; set; } = [];
    public List<string> MissingEvidenceZh { get; set; } = [];
}

public static class EffectMemberCompatibilityKind
{
    public const string DirectCoreCall = "DirectCoreCall";
    public const string VerifiedWrapper = "VerifiedWrapper";
    public const string VerifiedComplexFamily = "VerifiedComplexFamily";
    public const string Unsupported = "Unsupported";
}

public static class CompositeInstallationStatus
{
    public const string Complete = "Complete";
    public const string Disabled = "Disabled";
    public const string Repairable = "Repairable";
    public const string Incomplete = "Incomplete";
    public const string ExternallyModified = "ExternallyModified";
    public const string Removed = "Removed";
}

public sealed class WrapperContract
{
    public string ContractId { get; set; } = string.Empty;
    public uint EntryAddress { get; set; }
    public string DisplayNameZh { get; set; } = string.Empty;
    public string EvidenceExeSha256 { get; set; } = string.Empty;
    public bool IsVerifiedForWrite { get; set; }
    public uint? CoreCallAddress { get; set; }
    public uint? FallbackCallAddress { get; set; }
    public string CallingConventionZh { get; set; } = string.Empty;
    public string ParameterMappingZh { get; set; } = string.Empty;
    public string ReturnValueZh { get; set; } = string.Empty;
    public string RegisterConstraintZh { get; set; } = string.Empty;
    public string FlagsConstraintZh { get; set; } = string.Empty;
    public string ReasonZh { get; set; } = string.Empty;
    public List<uint> CallerAddresses { get; set; } = [];
}

public sealed class ComplexEffectFamilyContract
{
    public string ContractId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public string EvidenceExeSha256 { get; set; } = string.Empty;
    public bool IsVerifiedForWrite { get; set; }
    public List<string> NamePatterns { get; set; } = [];
    public List<string> SignatureIds { get; set; } = [];
    public List<uint> HookAddresses { get; set; } = [];
    public List<uint> SharedHelperAddresses { get; set; } = [];
    public List<string> ConflictGroups { get; set; } = [];
    public List<string> DynamicValidationScenariosZh { get; set; } = [];
    public string AdapterPolicy { get; set; } = string.Empty;
    public string ReasonZh { get; set; } = string.Empty;
}

public sealed class EffectWritableProfileStatus
{
    public const string Current65BaselineSha256 = EngineEffectProfileRegistry.Canonical65Sha256;
    public const string LegacyDynamicEvidenceSha256 = EngineEffectProfileRegistry.Known65VariantSha256;
    public bool CanWrite { get; set; }
    public bool IsOriginalBaseline { get; set; }
    public bool IsTrackedDescendant { get; set; }
    public string CurrentExeSha256 { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ReasonZh { get; set; } = string.Empty;
    public List<string> EvidenceManifestPaths { get; set; } = [];
    public ExecutableProfileAuditResult ProfileAudit { get; set; } = new();
    public string TrustStatus => ProfileAudit.TrustStatus;
}

public sealed class CompositeIdentityContract
{
    public string ContractId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public int DisabledOtherChannelId { get; set; }
    public string UnitPointerSourceZh { get; set; } = string.Empty;
    public string EvidenceZh { get; set; } = string.Empty;
    public string BlockingReasonZh { get; set; } = string.Empty;
}

public sealed class NativeEffectDefinition
{
    public string Channel { get; set; } = string.Empty;
    public int EffectId { get; set; }
    public string DisplayId => EffectId.ToString("X2");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string CatalogName { get; set; } = string.Empty;
    public bool HasNativeNameSlot { get; set; }
    public bool IsWritable { get; set; }
    public string EditabilityReasonZh { get; set; } = string.Empty;
    public List<EffectBindingReference> Bindings { get; set; } = [];
    public List<NativeEffectStubDefinition> Stubs { get; set; } = [];
    public List<NativeEffectFieldCapability> FieldCapabilities { get; set; } = [];
    public ExtendedBindingCapability ExtendedBindingCapability { get; set; } = new();
    public string AnalysisFingerprint { get; set; } = string.Empty;
    public string CacheState { get; set; } = string.Empty;
    public List<string> CompletedStages { get; set; } = [];
    public Dictionary<string, double> Performance { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ExecutableProfileAuditResult ProfileAudit { get; set; } = new();
}

public sealed class NativeEffectFieldCapability
{
    public string FieldId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public string CurrentValueZh { get; set; } = string.Empty;
    public string WriteCapability { get; set; } = EffectIdWriteCapability.DiagnosticOnly;
    public string WriteCapabilityZh { get; set; } = string.Empty;
    public bool CanEdit { get; set; }
    public string ReasonZh { get; set; } = string.Empty;
    public int AffectedConsumerCount { get; set; }
    public List<string> LocationIds { get; set; } = [];
    public EffectWriteDecision WriteDecision { get; set; } = new();
}

public sealed class NativeEffectStubDefinition
{
    public string InstanceId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public int? PersonalEffectId { get; set; }
    public int? ItemEffectId { get; set; }
    public int? EffectValueMode { get; set; }
    public int? StackingMode { get; set; }
    public List<uint> CallSites { get; set; } = [];
    public bool IsWritable { get; set; }
    public string EditabilityReasonZh { get; set; } = string.Empty;
}

public sealed class NativeEffectEditDraft
{
    public string RunMode { get; set; } = EffectWriteRunMode.Formal;
    public string Channel { get; set; } = string.Empty;
    public int EffectId { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? InstanceId { get; set; }
    public int? PersonalEffectId { get; set; }
    public int? ItemEffectId { get; set; }
    public int? EffectValueMode { get; set; }
    public int? StackingMode { get; set; }
    public int? EffectValue { get; set; }
    public bool ReplaceAllBindings { get; set; }
    public List<EffectPackageBinding> Bindings { get; set; } = [];
}

public sealed class NativeEffectEditPreview
{
    public bool CanApply { get; set; }
    public string SummaryZh { get; set; } = string.Empty;
    public List<string> WarningsZh { get; set; } = [];
    public EffectPackage Package { get; set; } = new();
    public EffectPatchPreviewResult PatchPreview { get; set; } = new();
    public List<NativeEffectFieldCapability> FieldResults { get; set; } = [];
    public ExtendedBindingPreview? ExtendedBindingPreview { get; set; }
}

public static class ExtendedBindingSourceKind
{
    public const string Character = "Character";
    public const string Job = "Job";
}

public sealed class ExtendedPersonalJobBindingEntry
{
    public int EffectId { get; set; }
    public string SourceKind { get; set; } = ExtendedBindingSourceKind.Character;
    public int SourceId { get; set; }
    public int EffectValue { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class ExtendedPersonalJobBindingManifest
{
    public string SchemaVersion { get; set; } = "1.0";
    public string ManifestId { get; set; } = string.Empty;
    public ProjectPatchIdentity? ProjectIdentity { get; set; }
    public string EvidenceExeSha256 { get; set; } = string.Empty;
    public string RuntimeContractVersion { get; set; } = "personal-job-binding-query-v1";
    public List<ExtendedPersonalJobBindingEntry> Entries { get; set; } = [];
    public string StatusZh { get; set; } = "等待动态验证";
}

public sealed class ExtendedBindingCapability
{
    public bool HasRequiredDynamicEvidence { get; set; }
    public bool QueryCompilerAvailable { get; set; }
    public bool CanInstallRuntimeQuery => HasRequiredDynamicEvidence && QueryCompilerAvailable;
    public string EvidenceExeSha256 { get; set; } = string.Empty;
    public string ContractId { get; set; } = "personal-job-binding-query-v1";
    public string StatusZh { get; set; } = string.Empty;
    public string ReasonZh { get; set; } = string.Empty;
}

public sealed class ExtendedBindingPreview
{
    public bool RequiresExtension { get; set; }
    public bool CanApply { get; set; }
    public EffectPackageBinding? NativeBinding { get; set; }
    public List<ExtendedPersonalJobBindingEntry> ExtendedEntries { get; set; } = [];
    public ExtendedBindingCapability Capability { get; set; } = new();
    public ExtendedBindingProbePlan ProbePlan { get; set; } = new();
    public List<string> WarningsZh { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class ExtendedBindingProbePlan
{
    public string PlanId { get; set; } = string.Empty;
    public string ContractId { get; set; } = "personal-job-binding-query-v1";
    public string ExeSha256 { get; set; } = string.Empty;
    public List<uint> ReadWatchAddresses { get; set; } = [];
    public List<uint> BreakpointAddresses { get; set; } = [];
    public List<string> RequiredCaptureFieldsZh { get; set; } = [];
    public List<ExtendedBindingProbeScenario> Scenarios { get; set; } = [];
    public List<EffectProbeBatch> Batches { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class ExtendedBindingProbeScenario
{
    public string ScenarioId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public string RequiredObservationZh { get; set; } = string.Empty;
}

public sealed class ExtendedBindingEvidence
{
    public string EvidenceId { get; set; } = string.Empty;
    public string ContractId { get; set; } = "personal-job-binding-query-v1";
    public string ExeSha256 { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public string SourceTool { get; set; } = string.Empty;
    public List<string> CompletedScenarioIds { get; set; } = [];
    public Dictionary<string, string> Observations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CallStacksZh { get; set; } = [];
    public string NotesZh { get; set; } = string.Empty;
}

public sealed class ExtendedBindingEvidenceImportResult
{
    public bool Accepted { get; set; }
    public string SavedPath { get; set; } = string.Empty;
    public ExtendedBindingCapability Capability { get; set; } = new();
    public List<string> WarningsZh { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class EffectTransactionalApplyResult
{
    public bool Applied { get; set; }
    public string SummaryZh { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public List<string> BackupPaths { get; set; } = [];
    public List<EffectManifestBackup> Backups { get; set; } = [];
    public int ChangedBytes { get; set; }
}

public sealed class CompositeEffectDraft
{
    public string SchemaVersion { get; set; } = "1.0";
    public string CompositeId { get; set; } = string.Empty;
    public string Channel { get; set; } = CompositeEffectChannel.PersonalJob;
    public int EffectId { get; set; } = -1;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AllocationMode { get; set; } = "Free";
    public string ReplacedDefinitionName { get; set; } = string.Empty;
    public List<EffectPackageBinding> Bindings { get; set; } = [];
    public List<CompositeEffectMember> Members { get; set; } = [];
}

public sealed class CompositeEffectMember
{
    public string InstanceId { get; set; } = string.Empty;
    public int? EffectValue { get; set; }
    public int? EffectValueMode { get; set; }
    public int? StackingMode { get; set; }
}

public sealed class CompositeEffectCompatibility
{
    public string InstanceId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public bool IsCompatible { get; set; }
    public string CompatibilityKind { get; set; } = EffectMemberCompatibilityKind.Unsupported;
    public string ContractId { get; set; } = string.Empty;
    public string ReasonZh { get; set; } = string.Empty;
    public string TriggerPhaseZh { get; set; } = string.Empty;
    public int? OriginalPersonalEffectId { get; set; }
    public int? OriginalItemEffectId { get; set; }
    public int EffectValueMode { get; set; }
    public int StackingMode { get; set; }
    public List<uint> RedirectedCallSites { get; set; } = [];
    public uint OriginalCallTarget { get; set; }
}

public sealed class CompositeEffectParameterBlock
{
    public string Magic { get; set; } = "CCZE";
    public int Version { get; set; } = 2;
    public uint Address { get; set; }
    public string AddressHex => Address == 0 ? string.Empty : $"0x{Address:X8}";
    public List<CompositeEffectParameterRecord> Records { get; set; } = [];
}

public sealed class CompositeEffectParameterRecord
{
    public string InstanceId { get; set; } = string.Empty;
    public int EffectValue { get; set; }
    public int EffectValueMode { get; set; }
    public int StackingMode { get; set; }
    public uint ValueAddress { get; set; }
    public string MeaningKind { get; set; } = string.Empty;
    public string UnitZh { get; set; } = string.Empty;
    public string CompatibilityKind { get; set; } = string.Empty;
    public string IntegrityHash { get; set; } = string.Empty;
}

public sealed class CompositeEffectPreview
{
    public bool CanApply { get; set; }
    public string SummaryZh { get; set; } = string.Empty;
    public List<string> WarningsZh { get; set; } = [];
    public CompositeEffectDraft Draft { get; set; } = new();
    public List<CompositeEffectCompatibility> Members { get; set; } = [];
    public CompositeEffectParameterBlock ParameterBlock { get; set; } = new();
    public EffectPackage Package { get; set; } = new();
    public EffectPatchPreviewResult PatchPreview { get; set; } = new();
    public CompositeEffectPreflightReport Preflight { get; set; } = new();
    public CompositePreviewReceipt? Receipt { get; set; }
}

public sealed class CompositeEffectPreflightReport
{
    public bool CanApply { get; set; }
    public List<CompositeEffectPreflightStage> Stages { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class CompositeEffectPreflightStage
{
    public string StageId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string ResultZh { get; set; } = string.Empty;
}

public sealed class CompositePreviewReceipt
{
    public string ReceiptId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string ExeSha256 { get; set; } = string.Empty;
    public string PackageHash { get; set; } = string.Empty;
    public string EffectIdSnapshot { get; set; } = string.Empty;
    public string AllocatedRange { get; set; } = string.Empty;
}

public sealed class CompositeEffectManifest
{
    public string SchemaVersion { get; set; } = "2.0";
    public string ManifestId { get; set; } = string.Empty;
    public ProjectPatchIdentity? ProjectIdentity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string ExeSha256Before { get; set; } = string.Empty;
    public string ExeSha256After { get; set; } = string.Empty;
    public string EffectManifestPath { get; set; } = string.Empty;
    public CompositeEffectDraft Draft { get; set; } = new();
    public List<CompositeEffectCompatibility> Members { get; set; } = [];
    public CompositeEffectParameterBlock ParameterBlock { get; set; } = new();
    public EffectPackage Package { get; set; } = new();
    public List<string> BackupPaths { get; set; } = [];
    public string StatusZh { get; set; } = "已安装";
    public string InstallationStatus { get; set; } = "Complete";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string SignatureAlgorithm { get; set; } = "HMAC-SHA256-DPAPI-CurrentUser";
    public string Signature { get; set; } = string.Empty;
}

public sealed class CompositeEffectMaintenanceDraft
{
    public string ManifestId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool ReplaceBindings { get; set; }
    public List<EffectPackageBinding> Bindings { get; set; } = [];
    public List<CompositeEffectMember> Members { get; set; } = [];
}

public sealed class CompositeEffectMaintenancePreview
{
    public bool CanApply { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string SummaryZh { get; set; } = string.Empty;
    public List<string> WarningsZh { get; set; } = [];
    public EffectPackage Package { get; set; } = new();
    public EffectPatchPreviewResult PatchPreview { get; set; } = new();
}

public sealed class CompositeEffectApplyResult
{
    public bool Applied { get; set; }
    public string SummaryZh { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public List<string> BackupPaths { get; set; } = [];
    public EffectPackageApplyResult PatchResult { get; set; } = new();
}

public sealed class FreeEffectIdReport
{
    public string Channel { get; set; } = string.Empty;
    public List<int> FreeIds { get; set; } = [];
    public List<int> ReclaimableIds { get; set; } = [];
    public Dictionary<int, string> ReclaimableNames { get; set; } = [];
    public Dictionary<int, List<string>> OccupiedReasonsZh { get; set; } = [];
    public List<int> OccupiedIds { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}
