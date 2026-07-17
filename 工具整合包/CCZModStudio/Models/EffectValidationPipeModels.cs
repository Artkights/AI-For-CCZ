using System.Text.Json;

namespace CCZModStudio.Models;

public static class EffectValidationPipeProtocol
{
    public const string Version = "effect-validation-pipe-v3";
    public const int MaximumMessageCharacters = 4 * 1024 * 1024;
}

public sealed class EffectValidationPipeRequest
{
    public string ProtocolVersion { get; set; } = EffectValidationPipeProtocol.Version;
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionToken { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }
}

public sealed class EffectValidationPipeResponse
{
    public string ProtocolVersion { get; set; } = EffectValidationPipeProtocol.Version;
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorZh { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }
}

public sealed class EffectValidationStartRequest
{
    public string ContractId { get; set; } = string.Empty;
    public string ContractHash { get; set; } = string.Empty;
    public string ContractCodeIdentityHash { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string NormalizedProfileIdentity { get; set; } = string.Empty;
    public int ContractVersion { get; set; } = 2;
    public EffectValidationRecipe ValidationRecipe { get; set; } = new();
    public string BaseExeSha256 { get; set; } = string.Empty;
    public string SandboxPatchSha256 { get; set; } = string.Empty;
    public string ProbePackageHash { get; set; } = string.Empty;
    public uint ContinuationAddress { get; set; }
    public string EvidenceDisposition { get; set; } = EffectEvidenceDispositions.StoreAndPromote;
    public int EffectId { get; set; } = 1;
    public string SandboxRoot { get; set; } = string.Empty;
    public bool LaunchDebugger { get; set; } = true;
}

public sealed class EffectValidationSessionRequest
{
    public string SessionPath { get; set; } = string.Empty;
    public int BatchIndex { get; set; }
    public string ScenarioId { get; set; } = string.Empty;
}
