namespace CCZModStudio.Models;

public static class ExecutableProfileTrustStatus
{
    public const string ExactCanonical = "ExactCanonical";
    public const string AutoDerivedDataOnly = "AutoDerivedDataOnly";
    public const string LocalVerified = "LocalVerified";
    public const string TrackedDescendant = "TrackedDescendant";
    public const string ReadOnlyUnknownDifference = "ReadOnlyUnknownDifference";
    public const string UnsupportedVersion = "UnsupportedVersion";
    public const string MissingExecutable = "MissingExecutable";
}

public sealed class ExecutableRegisteredField
{
    public string FieldId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public long FileOffset { get; set; }
    public uint VirtualAddress { get; set; }
    public int Width { get; set; }
    public string Encoding { get; set; } = string.Empty;
    public string CanonicalValueHex { get; set; } = string.Empty;
    public ulong? AllowedBitMask { get; set; }
    public long? Minimum { get; set; }
    public long? Maximum { get; set; }
    public List<string> AllowedValuesHex { get; set; } = [];
    public bool AffectsAbi { get; set; }
    public bool AffectsHook { get; set; }
    public List<string> AffectedContractFamilies { get; set; } = [];
}

public sealed class EngineEffectProfileDefinition
{
    public string ProfileId { get; set; } = string.Empty;
    public int ProfileVersion { get; set; }
    public string EngineVersion { get; set; } = string.Empty;
    public long FileLength { get; set; }
    public uint ImageBase { get; set; }
    public string CanonicalSha256 { get; set; } = string.Empty;
    public string NormalizedIdentitySha256 { get; set; } = string.Empty;
    public List<string> PeSectionLayout { get; set; } = [];
    public List<string> KnownFullSha256 { get; set; } = [];
    public List<ExecutableRegisteredField> RegisteredFields { get; set; } = [];
    public List<string> ForbiddenRangeKinds { get; set; } = [];
    public int MaximumChangedBytes { get; set; } = 32;
    public int MaximumChangedRanges { get; set; } = 16;
}

public sealed class ExecutableProfileDifference
{
    public long FileOffset { get; set; }
    public uint? VirtualAddress { get; set; }
    public int Width { get; set; }
    public string OldBytesHex { get; set; } = string.Empty;
    public string NewBytesHex { get; set; } = string.Empty;
    public string FieldId { get; set; } = string.Empty;
    public string MeaningZh { get; set; } = string.Empty;
    public bool IsRegisteredField { get; set; }
    public bool ValueAllowed { get; set; }
}

public sealed class ExecutableProfileAuditResult
{
    public string ProfileId { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public string CanonicalSha256 { get; set; } = string.Empty;
    public string CurrentSha256 { get; set; } = string.Empty;
    public string NormalizedProfileIdentity { get; set; } = string.Empty;
    public string TrustStatus { get; set; } = ExecutableProfileTrustStatus.UnsupportedVersion;
    public bool CanWriteRegisteredData { get; set; }
    public bool CanReuseCodeContracts { get; set; }
    public int ChangedByteCount { get; set; }
    public int ChangedRangeCount { get; set; }
    public List<ExecutableProfileDifference> RegisteredDifferences { get; set; } = [];
    public List<ExecutableProfileDifference> UnknownDifferences { get; set; } = [];
    public List<string> BlockerCodes { get; set; } = [];
    public List<string> ReasonsZh { get; set; } = [];
    public string AuditSummaryHash { get; set; } = string.Empty;
    public string SummaryZh { get; set; } = string.Empty;
}

public static class EffectWriteCapability
{
    public const string ReadOnlyDiagnostic = "ReadOnlyDiagnostic";
    public const string RegisteredData = "RegisteredData";
    public const string DirectParameter = "DirectParameter";
    public const string Adapter = "Adapter";
    public const string StaticPreviewOnly = "StaticPreviewOnly";
    public const string DynamicBehavior = "DynamicBehavior";
    public const string SandboxValidation = "SandboxValidation";
}

public static class EffectModificationKind
{
    public const string NameOrDescription = "NameOrDescription";
    public const string Binding = "Binding";
    public const string DirectEffectId = "DirectEffectId";
    public const string SignedImmediateAdapter = "SignedImmediateAdapter";
    public const string WrapperParameter = "WrapperParameter";
    public const string ManagedParameter = "ManagedParameter";
    public const string BehaviorLogic = "BehaviorLogic";
    public const string Maintenance = "Maintenance";
    public const string Diagnostic = "Diagnostic";
}

public static class EffectProfileTrustTier
{
    public const string BuiltInVerified = "BuiltInVerified";
    public const string LocalVerified = "LocalVerified";
    public const string SandboxCandidate = "SandboxCandidate";
    public const string ReadOnlyUnknown = "ReadOnlyUnknown";
}

public static class EffectWriteRunMode
{
    public const string Formal = "Formal";
    public const string SandboxValidation = "SandboxValidation";
}

public sealed class EffectWriteRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string ModificationKind { get; set; } = EffectModificationKind.Diagnostic;
    public string SemanticFieldId { get; set; } = string.Empty;
    public string TargetFile { get; set; } = string.Empty;
    public string SourceLocationId { get; set; } = string.Empty;
    public string RequiredCapability { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string ContractHash { get; set; } = string.Empty;
    public string ContractCodeIdentityHash { get; set; } = string.Empty;
    public bool HasExactLocation { get; set; }
    public bool HasStaticContract { get; set; }
    public bool HasDynamicV3Evidence { get; set; }
    public bool HasTrustedRepairLineage { get; set; }
    public string RunMode { get; set; } = EffectWriteRunMode.Formal;
    public int? RequestedValue { get; set; }
    public int AffectedConsumers { get; set; }
}

public sealed class EffectWriteAuthorizationClaim
{
    public string ClaimId { get; set; } = string.Empty;
    public string ModificationKind { get; set; } = string.Empty;
    public string SemanticFieldId { get; set; } = string.Empty;
    public string TargetFile { get; set; } = string.Empty;
    public string SourceLocationId { get; set; } = string.Empty;
    public string RequiredCapability { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string ContractHash { get; set; } = string.Empty;
    public string ContractCodeIdentityHash { get; set; } = string.Empty;
    public string DecisionHash { get; set; } = string.Empty;
    public bool CanApply { get; set; }
    public bool SandboxOnly { get; set; }
    public List<string> EvidenceBundleIds { get; set; } = [];
}

public sealed class EffectWriteAuthorization
{
    public string SchemaVersion { get; set; } = "effect-write-authorization-v2";
    public string AuthorizationId { get; set; } = Guid.NewGuid().ToString("N");
    public string Nonce { get; set; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    public string ProjectId { get; set; } = string.Empty;
    public string FullExeSha256 { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileTrustTier { get; set; } = EffectProfileTrustTier.ReadOnlyUnknown;
    public string NormalizedProfileIdentity { get; set; } = string.Empty;
    public string RunMode { get; set; } = EffectWriteRunMode.Formal;
    public bool SandboxOnly { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string DecisionHash { get; set; } = string.Empty;
    public List<EffectWriteAuthorizationClaim> Claims { get; set; } = [];
}

public sealed class EffectWriteMatrix
{
    public string AnalysisFingerprint { get; set; } = string.Empty;
    public string ProfileTrustTier { get; set; } = EffectProfileTrustTier.ReadOnlyUnknown;
    public string BuildIdentity { get; set; } = string.Empty;
    public List<EffectWriteDecision> Decisions { get; set; } = [];
    public int EditableCount => Decisions.Count(item => item.CanEdit);
    public int ApplicableCount => Decisions.Count(item => item.CanApply);
    public int MissingStaticContractCount => Decisions.Count(item => item.BlockerCodes.Contains("STATIC_CONTRACT_REQUIRED"));
    public int MissingDynamicEvidenceCount => Decisions.Count(item => item.BlockerCodes.Contains("DYNAMIC_V3_EVIDENCE_REQUIRED"));
}

public sealed class EffectSandboxDescriptor
{
    public string SchemaVersion { get; set; } = "effect-sandbox-v1";
    public string SandboxId { get; set; } = string.Empty;
    public string OriginalGameRoot { get; set; } = string.Empty;
    public string SandboxRoot { get; set; } = string.Empty;
    public string OriginalExeSha256 { get; set; } = string.Empty;
    public string SandboxExeSha256 { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string SignatureAlgorithm { get; set; } = "HMAC-SHA256-DPAPI-CurrentUser";
    public string Signature { get; set; } = string.Empty;
}

public sealed class LocalEffectProfileRecord
{
    public string SchemaVersion { get; set; } = "effect-local-profile-v1";
    public string ProfileId { get; set; } = string.Empty;
    public int ProfileVersion { get; set; } = 1;
    public string EngineVersion { get; set; } = "6.5";
    public string TrustTier { get; set; } = EffectProfileTrustTier.LocalVerified;
    public string FullExeSha256 { get; set; } = string.Empty;
    public long FileLength { get; set; }
    public uint ImageBase { get; set; }
    public string NormalizedProfileIdentity { get; set; } = string.Empty;
    public string PeLayoutIdentityHash { get; set; } = string.Empty;
    public string TableLayoutIdentityHash { get; set; } = string.Empty;
    public List<ExecutableProfileDifference> ExplainedDifferences { get; set; } = [];
    public Dictionary<string, string> ContractCodeIdentities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> EvidenceBundleIds { get; set; } = [];
    public List<string> ForbiddenRangeKinds { get; set; } = [];
    public string SourceSandboxId { get; set; } = string.Empty;
    public string ToolBuildIdentity { get; set; } = string.Empty;
    public DateTime VerifiedAtUtc { get; set; }
    public string SignatureAlgorithm { get; set; } = "HMAC-SHA256-DPAPI-CurrentUser";
    public string Signature { get; set; } = string.Empty;
}

public sealed class EffectProfileOnboardingPlan
{
    public string PlanId { get; set; } = string.Empty;
    public string OriginalGameRoot { get; set; } = string.Empty;
    public string SandboxRoot { get; set; } = string.Empty;
    public string SandboxExeSha256 { get; set; } = string.Empty;
    public ExecutableProfileAuditResult ProfileAudit { get; set; } = new();
    public string ProfileTrustTier { get; set; } = EffectProfileTrustTier.ReadOnlyUnknown;
    public bool CanRunSandboxValidation { get; set; }
    public bool CanPromote { get; set; }
    public List<string> RequiredContractIds { get; set; } = [];
    public List<string> BlockerCodes { get; set; } = [];
    public List<string> StepsZh { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class EffectProfileOnboardingResult
{
    public bool Completed { get; set; }
    public bool Promoted { get; set; }
    public string ProfilePath { get; set; } = string.Empty;
    public LocalEffectProfileRecord? Profile { get; set; }
    public List<string> WarningsZh { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class EffectWriteDecision
{
    public bool CanEdit { get; set; }
    public bool CanPreview { get; set; }
    public bool CanApply { get; set; }
    public string Capability { get; set; } = EffectWriteCapability.ReadOnlyDiagnostic;
    public string TrustStatus { get; set; } = ExecutableProfileTrustStatus.UnsupportedVersion;
    public string ProfileTrustTier { get; set; } = EffectProfileTrustTier.ReadOnlyUnknown;
    public bool SandboxOnly { get; set; }
    public string DecisionHash { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
    public List<string> BlockerCodes { get; set; } = [];
    public List<string> ReasonsZh { get; set; } = [];
    public List<string> RequiredEvidence { get; set; } = [];
    public int AffectedConsumers { get; set; }
}
