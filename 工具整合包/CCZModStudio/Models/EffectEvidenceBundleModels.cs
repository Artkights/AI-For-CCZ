using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace CCZModStudio.Models;

public sealed class EffectValidationRecipe
{
    public string RecipeId { get; set; } = string.Empty;
    public int RecipeVersion { get; set; } = 1;
    public List<uint> BreakpointAddresses { get; set; } = [];
    public List<EffectValidationScenarioDefinition> Scenarios { get; set; } = [];
    public List<string> RequiredObservationKeys { get; set; } = [];
    public List<string> RequiredRelationshipSlots { get; set; } = [];
    public List<string> WritableBoundaryKeys { get; set; } = [];
    public List<EffectValidationCapturePointDefinition> CapturePoints { get; set; } = [];
    public List<EffectValidationRelationshipRule> RelationshipRules { get; set; } = [];
}

public sealed class EffectValidationScenarioDefinition
{
    public string ScenarioId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public string InstructionZh { get; set; } = string.Empty;
    public string ExpectedTransition { get; set; } = string.Empty;
    public string ScenarioRole { get; set; } = EffectValidationScenarioRoles.Normal;
    public Dictionary<string, int> RequiredMinimumHits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> AllowedMaximumHits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class EffectValidationScenarioRoles
{
    public const string Normal = "Normal";
    public const string Critical = "Critical";
    public const string Counter = "Counter";
    public const string Combo = "Combo";
    public const string MinimumBoundary = "MinimumBoundary";
    public const string MaximumBoundary = "MaximumBoundary";
    public const string Negative = "Negative";
}

public sealed class EffectValidationCapturePointDefinition
{
    public string CapturePointId { get; set; } = string.Empty;
    public uint Address { get; set; }
    public string ExtractorId { get; set; } = string.Empty;
    public bool Optional { get; set; }
}

public sealed class EffectValidationRelationshipRule
{
    public string RuleId { get; set; } = string.Empty;
    public string LeftObservationKey { get; set; } = string.Empty;
    public string RightObservationKey { get; set; } = string.Empty;
    public string RelationshipKind { get; set; } = "PointerEquality";
    public bool Required { get; set; } = true;
}

public static class EffectEvidenceProtocol
{
    public const string SchemaVersion = "effect-evidence-bundle-v1";
    public const string EffectCapabilitySchemaVersion = "effect-authoring-5.0";
    public const string BuildChannel = "ccz65-trusted-dispatcher-v5";
}

public static class EffectEvidenceProtocolV2
{
    public const string SchemaVersion = "effect-evidence-bundle-v2";
    public const string EffectCapabilitySchemaVersion = "effect-authoring-6.0";
    public const string BuildChannel = "ccz65-contract-v2-profile-audit-v6";
}

public static class EffectEvidenceProtocolV3
{
    public const string SchemaVersion = "effect-evidence-bundle-v3";
    public const string EffectCapabilitySchemaVersion = "effect-authoring-7.0";
    public const string BuildChannel = "ccz65-open-authoring-v7";
}

public sealed class EffectEvidenceBundleV3
{
    public string SchemaVersion { get; set; } = EffectEvidenceProtocolV3.SchemaVersion;
    public string EffectCapabilitySchemaVersion { get; set; } = EffectEvidenceProtocolV3.EffectCapabilitySchemaVersion;
    public string BuildChannel { get; set; } = EffectEvidenceProtocolV3.BuildChannel;
    public string BundleId { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public int ContractVersion { get; set; } = 2;
    public string ContractHash { get; set; } = string.Empty;
    public string ContractCodeIdentityHash { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string NormalizedProfileIdentity { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string OriginalGameRoot { get; set; } = string.Empty;
    public string SandboxRoot { get; set; } = string.Empty;
    public string SessionRoot { get; set; } = string.Empty;
    public string OriginalExeSha256 { get; set; } = string.Empty;
    public string BaseExeSha256 { get; set; } = string.Empty;
    public string SandboxPatchSha256 { get; set; } = string.Empty;
    public string PatchPackageHash { get; set; } = string.Empty;
    public string ProbePlanHash { get; set; } = string.Empty;
    public uint ContinuationAddress { get; set; }
    public string EvidenceDisposition { get; set; } = EffectEvidenceDispositions.StoreAndPromote;
    public string LoadedModulePath { get; set; } = string.Empty;
    public long LoadedModuleSize { get; set; }
    public string LoadedModuleSha256 { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ProcessPath { get; set; } = string.Empty;
    public string DebuggerVersion { get; set; } = string.Empty;
    public string ToolBuildId { get; set; } = string.Empty;
    public string ValidationRecipeId { get; set; } = string.Empty;
    public string ValidationRecipeHash { get; set; } = string.Empty;
    public string ProbePackageHash { get; set; } = string.Empty;
    public string ProbeExpectedOldBytesHash { get; set; } = string.Empty;
    public bool ProbeRestored { get; set; }
    public string ProbeRestoreEvidencePath { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<string> CompletedScenarioIds { get; set; } = [];
    public List<EffectEvidenceRawFile> RawFiles { get; set; } = [];
    public List<EffectEvidenceDerivedObservation> DerivedObservations { get; set; } = [];
    public List<EffectRelationshipAssertion> RelationshipAssertions { get; set; } = [];
    public string SignatureAlgorithm { get; set; } = "HMAC-SHA256-DPAPI-CurrentUser";
    public string Signature { get; set; } = string.Empty;
}

public sealed class EffectRelationshipAssertion
{
    public string SlotId { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string LeftSlotId { get; set; } = string.Empty;
    public string RightSlotId { get; set; } = string.Empty;
    public string RelationshipKind { get; set; } = string.Empty;
    public int? BattlefieldUnitId { get; set; }
    public string Camp { get; set; } = string.Empty;
    public int? HpObserved { get; set; }
    public bool CallChainVerified { get; set; }
    public string PointerHex { get; set; } = string.Empty;
    public int MatchingSamples { get; set; }
    public int NegativeSamples { get; set; }
    public bool Verified { get; set; }
    public List<string> EvidencePaths { get; set; } = [];
}

public static class EffectEvidenceDispositions
{
    public const string StoreAndPromote = "StoreAndPromote";
    public const string ValidationOnly = "ValidationOnly";
}

public sealed class EffectValidationSession
{
    public string SchemaVersion { get; set; } = "effect-validation-session-v1";
    public string SessionId { get; set; } = string.Empty;
    public string OriginalGameRoot { get; set; } = string.Empty;
    public string SandboxRoot { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string ContractHash { get; set; } = string.Empty;
    public string ContractCodeIdentityHash { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string NormalizedProfileIdentity { get; set; } = string.Empty;
    public string OriginalExeSha256 { get; set; } = string.Empty;
    public string SandboxExeSha256 { get; set; } = string.Empty;
    public string ValidationRecipeId { get; set; } = string.Empty;
    public string ValidationRecipeHash { get; set; } = string.Empty;
    public string ProbePackageHash { get; set; } = string.Empty;
    public string ProbeExpectedOldBytesHash { get; set; } = string.Empty;
    public string SessionRoot { get; set; } = string.Empty;
    public string SessionPath { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool SandboxOnly { get; set; } = true;
    public bool ProbeInstalled { get; set; }
    public bool ProbeRestored { get; set; }
    public string Status { get; set; } = "Prepared";
    public string NextAction { get; set; } = string.Empty;
    public List<EffectProbeScenarioState> Scenarios { get; set; } = [];
}

public sealed class EffectEvidenceBundleV2
{
    public string SchemaVersion { get; set; } = EffectEvidenceProtocolV2.SchemaVersion;
    public string EffectCapabilitySchemaVersion { get; set; } = EffectEvidenceProtocolV2.EffectCapabilitySchemaVersion;
    public string BuildChannel { get; set; } = EffectEvidenceProtocolV2.BuildChannel;
    public string BundleId { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public int ContractVersion { get; set; } = 2;
    public string ContractHash { get; set; } = string.Empty;
    public string ContractCodeIdentityHash { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string NormalizedProfileIdentity { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GameRoot { get; set; } = string.Empty;
    public string SessionRoot { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ProcessPath { get; set; } = string.Empty;
    public string LoadedModulePath { get; set; } = string.Empty;
    public long LoadedModuleSize { get; set; }
    public string LoadedModuleSha256 { get; set; } = string.Empty;
    public string DebuggerVersion { get; set; } = string.Empty;
    public string ToolBuildId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<string> CompletedScenarioIds { get; set; } = [];
    public List<EffectEvidenceRawFile> RawFiles { get; set; } = [];
    public List<EffectEvidenceDerivedObservation> DerivedObservations { get; set; } = [];
    public string SignatureAlgorithm { get; set; } = "HMAC-SHA256-DPAPI-CurrentUser";
    public string Signature { get; set; } = string.Empty;
}

public sealed class EffectEvidenceBundleV1
{
    public string SchemaVersion { get; set; } = EffectEvidenceProtocol.SchemaVersion;
    public string EffectCapabilitySchemaVersion { get; set; } = EffectEvidenceProtocol.EffectCapabilitySchemaVersion;
    public string BuildChannel { get; set; } = EffectEvidenceProtocol.BuildChannel;
    public string BundleId { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string ContractHash { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GameRoot { get; set; } = string.Empty;
    public string SessionRoot { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ProcessPath { get; set; } = string.Empty;
    public string LoadedModulePath { get; set; } = string.Empty;
    public long LoadedModuleSize { get; set; }
    public string LoadedModuleSha256 { get; set; } = string.Empty;
    public string DebuggerVersion { get; set; } = string.Empty;
    public string ToolBuildId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<string> CompletedScenarioIds { get; set; } = [];
    public List<EffectEvidenceRawFile> RawFiles { get; set; } = [];
    public List<EffectEvidenceDerivedObservation> DerivedObservations { get; set; } = [];
    public string SignatureAlgorithm { get; set; } = "HMAC-SHA256-DPAPI-CurrentUser";
    public string Signature { get; set; } = string.Empty;
}

public sealed class EffectEvidenceRawFile
{
    public string ScenarioId { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class EffectEvidenceDerivedObservation
{
    public string ScenarioId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string SourceRelativePath { get; set; } = string.Empty;
    public string JsonPath { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    /// <summary>本次证据样本中观察到的范围，不代表引擎的合法钳制边界。</summary>
    public int? Minimum { get; set; }
    public int? Maximum { get; set; }
    /// <summary>从边界场景和最终写回复算得到的合法下限；普通采样不得填写。</summary>
    public int? VerifiedMinimum { get; set; }
    /// <summary>从边界场景和最终写回复算得到的合法上限；普通采样不得填写。</summary>
    public int? VerifiedMaximum { get; set; }
    public string BoundaryEvidenceZh { get; set; } = string.Empty;
}

public sealed class EffectEvidenceBundleImportResult
{
    public bool Accepted { get; set; }
    public bool SignatureVerified { get; set; }
    public bool RawIntegrityVerified { get; set; }
    public bool ContractPromoted { get; set; }
    public string SavedPath { get; set; } = string.Empty;
    public string SummaryZh { get; set; } = string.Empty;
    public List<string> WarningsZh { get; set; } = [];
}

public sealed class EffectProbeSession
{
    public string SchemaVersion { get; set; } = "effect-probe-session-v1";
    public string SessionId { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string ContractHash { get; set; } = string.Empty;
    public int ContractVersion { get; set; } = 2;
    public string ContractCodeIdentityHash { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string NormalizedProfileIdentity { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GameRoot { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string ExeSha256 { get; set; } = string.Empty;
    public int EffectId { get; set; } = -1;
    public EffectValidationRecipe ValidationRecipe { get; set; } = new();
    public string BaseExeSha256 { get; set; } = string.Empty;
    public string SandboxPatchSha256 { get; set; } = string.Empty;
    public string ProbePackageHash { get; set; } = string.Empty;
    public uint ContinuationAddress { get; set; }
    public string EvidenceDisposition { get; set; } = EffectEvidenceDispositions.StoreAndPromote;
    public string SessionRoot { get; set; } = string.Empty;
    public string PlanPath { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<EffectProbeScenarioState> Scenarios { get; set; } = [];
}

public sealed class EffectProbeScenarioState
{
    public string ScenarioId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public string InstructionZh { get; set; } = string.Empty;
    public int BatchIndex { get; set; }
    public bool Captured { get; set; }
    public string CapturePath { get; set; } = string.Empty;
    public List<EffectProbeCaptureState> Captures { get; set; } = [];
}

public sealed class EffectProbeCaptureState
{
    public string CapturePointId { get; set; } = string.Empty;
    public uint Address { get; set; }
    public int HitIndex { get; set; }
    public string CapturePath { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class EffectProbeBatch
{
    public int BatchIndex { get; set; }
    public string ScenarioId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public List<string> X32dbgCommands { get; set; } = [];
}

public sealed class EffectModuleTagEvidence
{
    public string ModuleId { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string ContractHash { get; set; } = string.Empty;
    public string EvidenceExeSha256 { get; set; } = string.Empty;
    public bool GrantsAuthoringCapability { get; set; }
    public string ReasonZh { get; set; } = string.Empty;
}

public static class EffectEvidenceBundleCrypto
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("CCZModStudio.EffectEvidence.v1"));

    internal static void Sign(EffectEvidenceBundleV1 bundle)
    {
        bundle.Signature = string.Empty;
        using var hmac = new HMACSHA256(GetOrCreateUserKey());
        bundle.Signature = Convert.ToHexString(hmac.ComputeHash(CanonicalBytes(bundle)));
    }

    public static bool Verify(EffectEvidenceBundleV1 bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.Signature)) return false;
        var supplied = bundle.Signature;
        bundle.Signature = string.Empty;
        try
        {
            using var hmac = new HMACSHA256(GetOrCreateUserKey());
            var expected = Convert.ToHexString(hmac.ComputeHash(CanonicalBytes(bundle)));
            var left = Encoding.ASCII.GetBytes(expected);
            var right = Encoding.ASCII.GetBytes(supplied);
            return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
        }
        finally
        {
            bundle.Signature = supplied;
        }
    }

    public static void Sign(EffectEvidenceBundleV2 bundle)
    {
        bundle.Signature = string.Empty;
        using var hmac = new HMACSHA256(GetOrCreateUserKey());
        bundle.Signature = Convert.ToHexString(hmac.ComputeHash(CanonicalBytes(bundle)));
    }

    public static bool Verify(EffectEvidenceBundleV2 bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.Signature)) return false;
        var supplied = bundle.Signature;
        bundle.Signature = string.Empty;
        try
        {
            using var hmac = new HMACSHA256(GetOrCreateUserKey());
            var expected = Convert.ToHexString(hmac.ComputeHash(CanonicalBytes(bundle)));
            var left = Encoding.ASCII.GetBytes(expected);
            var right = Encoding.ASCII.GetBytes(supplied);
            return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
        }
        finally { bundle.Signature = supplied; }
    }

    public static void Sign(EffectEvidenceBundleV3 bundle)
    {
        bundle.Signature = string.Empty;
        using var hmac = new HMACSHA256(GetOrCreateUserKey());
        bundle.Signature = Convert.ToHexString(hmac.ComputeHash(CanonicalBytes(bundle)));
    }

    public static bool Verify(EffectEvidenceBundleV3 bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.Signature)) return false;
        var supplied = bundle.Signature;
        bundle.Signature = string.Empty;
        try
        {
            using var hmac = new HMACSHA256(GetOrCreateUserKey());
            var expected = Convert.ToHexString(hmac.ComputeHash(CanonicalBytes(bundle)));
            var left = Encoding.ASCII.GetBytes(expected);
            var right = Encoding.ASCII.GetBytes(supplied);
            return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
        }
        finally { bundle.Signature = supplied; }
    }

    public static bool VerifyReadOnly(EffectEvidenceBundleV3 bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.Signature) || !TryReadUserKey(out var key)) return false;
        var supplied = bundle.Signature;
        bundle.Signature = string.Empty;
        try
        {
            using var hmac = new HMACSHA256(key);
            var expected = Convert.ToHexString(hmac.ComputeHash(CanonicalBytes(bundle)));
            var left = Encoding.ASCII.GetBytes(expected);
            var right = Encoding.ASCII.GetBytes(supplied);
            return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
        }
        finally { bundle.Signature = supplied; }
    }

    public static string Serialize(EffectEvidenceBundleV1 bundle)
        => JsonSerializer.Serialize(bundle, JsonOptions);

    public static string Serialize(EffectEvidenceBundleV2 bundle)
        => JsonSerializer.Serialize(bundle, JsonOptions);

    public static string Serialize(EffectEvidenceBundleV3 bundle)
        => JsonSerializer.Serialize(bundle, JsonOptions);

    public static string ComputeFileSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static string ComputeValidationRecipeHash(EffectValidationRecipe recipe)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(recipe, JsonOptions))));

    private static byte[] CanonicalBytes(EffectEvidenceBundleV1 bundle)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(bundle, JsonOptions));

    private static byte[] CanonicalBytes(EffectEvidenceBundleV2 bundle)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(bundle, JsonOptions));

    private static byte[] CanonicalBytes(EffectEvidenceBundleV3 bundle)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(bundle, JsonOptions));

    private static byte[] GetOrCreateUserKey()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCZModStudio", "Security");
        var path = Path.Combine(root, "effect-evidence-hmac.dpapi");
        Directory.CreateDirectory(root);
        if (File.Exists(path))
            return ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        var key = RandomNumberGenerator.GetBytes(32);
        var protectedKey = ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, protectedKey);
        try { File.Move(temp, path); }
        catch (IOException) { File.Delete(temp); }
        return File.Exists(path)
            ? ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser)
            : key;
    }

    private static bool TryReadUserKey(out byte[] key)
    {
        key = [];
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CCZModStudio", "Security", "effect-evidence-hmac.dpapi");
            if (!File.Exists(path)) return false;
            key = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return key.Length > 0;
        }
        catch
        {
            key = [];
            return false;
        }
    }
}
