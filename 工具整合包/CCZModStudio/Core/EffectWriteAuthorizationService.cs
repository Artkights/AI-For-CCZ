using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectWriteAuthorizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public IReadOnlyList<EffectWriteDecision> AuthorizePreview(
        CczProject project,
        EffectPackage package,
        TimeSpan? lifetime = null)
    {
        NormalizeKnownClaims(package);
        var audit = new ExecutableProfileAuditService().Audit(project);
        var writable = new EffectWritableProfileService().EvaluateFromAudit(project, audit);
        var trustedRepair = new TrustedEffectRepairService().Evaluate(project, package).IsTrusted;
        var exeLength = new FileInfo(project.ResolveGameFile("Ekd5.exe")).Length;
        var decisions = new List<EffectWriteDecision>();
        var claims = new List<EffectWriteAuthorizationClaim>();
        var runMode = package.Metadata.GetValueOrDefault("EffectWriteRunMode", EffectWriteRunMode.Formal);
        for (var index = 0; index < package.PatchSegments.Count; index++)
        {
            var segment = package.PatchSegments[index];
            var request = BuildRequest(project, package, segment, runMode, audit.CurrentSha256, index, trustedRepair);
            var decision = new EffectWriteDecisionService().Decide(project, request, audit, writable);
            decisions.Add(decision);
            claims.Add(new EffectWriteAuthorizationClaim
            {
                ClaimId = $"segment-{index:D4}",
                ModificationKind = request.ModificationKind,
                SemanticFieldId = request.SemanticFieldId,
                TargetFile = request.TargetFile,
                SourceLocationId = request.SourceLocationId,
                RequiredCapability = request.RequiredCapability,
                ContractId = request.ContractId,
                ContractHash = request.ContractHash,
                ContractCodeIdentityHash = request.ContractCodeIdentityHash,
                DecisionHash = decision.DecisionHash,
                CanApply = decision.CanApply,
                SandboxOnly = decision.SandboxOnly,
                EvidenceBundleIds = ReadEvidenceBundleIds(project, audit.CurrentSha256, request)
            });
        }

        var identity = new ProjectPatchIdentityService().BuildKnown(
            project, "Ekd5.exe", exeLength, audit.CurrentSha256, audit.ProfileId);
        var authorization = new EffectWriteAuthorization
        {
            ProjectId = identity.ProjectId,
            FullExeSha256 = audit.CurrentSha256,
            ProfileId = writable.ProfileId,
            ProfileTrustTier = decisions.Select(item => item.ProfileTrustTier).Distinct().SingleOrDefault() ?? EffectProfileTrustTier.ReadOnlyUnknown,
            NormalizedProfileIdentity = audit.NormalizedProfileIdentity,
            RunMode = runMode,
            SandboxOnly = decisions.Any(item => item.SandboxOnly),
            IssuedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(15)),
            Claims = claims
        };
        authorization.DecisionHash = ComputeAuthorizationHash(authorization);
        package.WriteAuthorization = authorization;
        return decisions;
    }

    internal void ValidateApply(
        CczProject project,
        EffectPackage package,
        ExecutableProfileAuditResult audit,
        EffectWritableProfileStatus writable,
        ProjectPatchIdentity verifiedIdentity,
        byte[] currentExeBytes)
    {
        var authorization = package.WriteAuthorization
                            ?? throw new InvalidOperationException("补丁包缺少结构化写入授权，请重新预览。");
        if (authorization.ExpiresAtUtc <= DateTime.UtcNow || authorization.IssuedAtUtc > DateTime.UtcNow.AddMinutes(1))
            throw new InvalidOperationException("结构化写入授权已经过期或时间无效，请重新预览。");
        if (!authorization.FullExeSha256.Equals(audit.CurrentSha256, StringComparison.OrdinalIgnoreCase) ||
            !authorization.NormalizedProfileIdentity.Equals(audit.NormalizedProfileIdentity, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("EXE 档案身份已在预览后变化，请重新预览。");
        if (!authorization.ProjectId.Equals(verifiedIdentity.ProjectId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("写入授权不属于当前项目。");
        if (!authorization.DecisionHash.Equals(ComputeAuthorizationHash(authorization), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("写入授权摘要已被修改。");
        if (authorization.SandboxOnly && !EffectSandboxService.IsSandbox(project))
            throw new InvalidOperationException("沙箱授权不能用于正式项目。");
        if (authorization.Claims.Count != package.PatchSegments.Count)
            throw new InvalidOperationException("写入授权与补丁段数量不一致。");

        var trustedRepair = new TrustedEffectRepairService().Evaluate(project, package, currentExeBytes).IsTrusted;
        for (var index = 0; index < package.PatchSegments.Count; index++)
        {
            var request = BuildRequest(project, package, package.PatchSegments[index], authorization.RunMode, audit.CurrentSha256, index, trustedRepair);
            var decision = new EffectWriteDecisionService().Decide(project, request, audit, writable);
            var claim = authorization.Claims[index];
            if (!claim.ClaimId.Equals($"segment-{index:D4}", StringComparison.Ordinal) ||
                !claim.ModificationKind.Equals(request.ModificationKind, StringComparison.Ordinal) ||
                !claim.SemanticFieldId.Equals(request.SemanticFieldId, StringComparison.Ordinal) ||
                !claim.TargetFile.Equals(request.TargetFile, StringComparison.OrdinalIgnoreCase) ||
                !claim.SourceLocationId.Equals(request.SourceLocationId, StringComparison.OrdinalIgnoreCase) ||
                !claim.ContractId.Equals(request.ContractId, StringComparison.OrdinalIgnoreCase) ||
                !claim.ContractHash.Equals(request.ContractHash, StringComparison.OrdinalIgnoreCase) ||
                !claim.ContractCodeIdentityHash.Equals(request.ContractCodeIdentityHash, StringComparison.OrdinalIgnoreCase) ||
                !claim.DecisionHash.Equals(decision.DecisionHash, StringComparison.OrdinalIgnoreCase) ||
                !decision.CanApply)
                throw new InvalidOperationException($"补丁段 {index + 1} 的写入授权已失效：" + string.Join("；", decision.ReasonsZh));
        }
    }

    public EffectWriteMatrix ReadMatrix(CczProject project)
    {
        var snapshot = EffectAnalysisCoordinator.Shared.Scan(project);
        var audit = snapshot.ProfileAudit;
        var requests = new List<EffectWriteRequest>
        {
            Request("native-name", EffectModificationKind.NameOrDescription, true),
            Request("native-description", EffectModificationKind.NameOrDescription, true),
            Request("native-bindings", EffectModificationKind.Binding, true),
            Request("direct-effect-id", EffectModificationKind.DirectEffectId, true),
            Request("signed-immediate-adapter", EffectModificationKind.SignedImmediateAdapter, true, hasStatic: true),
            Request("wrapper-parameter", EffectModificationKind.WrapperParameter, true, hasStatic: snapshot.MechanismProfile.WrapperContracts.Any(item => item.IsVerifiedForWrite))
        };
        foreach (var contract in snapshot.HookContracts)
        {
            requests.Add(new EffectWriteRequest
            {
                ModificationKind = EffectModificationKind.BehaviorLogic,
                SemanticFieldId = contract.ContractFamilyId,
                HasExactLocation = !string.IsNullOrWhiteSpace(contract.ExpectedOldBytesHex),
                HasStaticContract = contract.Slots.Any(item => item.IsStaticallyResolved) && !string.IsNullOrWhiteSpace(contract.ContractCodeIdentityHash),
                HasDynamicV3Evidence = HasTrustedV3(project, audit.CurrentSha256, contract.ContractId, contract.ContractHash, contract.ContractCodeIdentityHash),
                ContractId = contract.ContractId,
                ContractHash = contract.ContractHash,
                ContractCodeIdentityHash = contract.ContractCodeIdentityHash
            });
        }
        var writable = new EffectWritableProfileService().EvaluateFromAudit(project, audit);
        var decisions = requests.Select(request => new EffectWriteDecisionService().Decide(project, request, audit, writable)).ToList();
        return new EffectWriteMatrix
        {
            AnalysisFingerprint = snapshot.AnalysisFingerprint,
            ProfileTrustTier = decisions.Select(item => item.ProfileTrustTier).FirstOrDefault() ?? EffectProfileTrustTier.ReadOnlyUnknown,
            BuildIdentity = EffectCapabilityVersion.BuildIdentity,
            Decisions = decisions
        };
    }

    private static EffectWriteRequest BuildRequest(
        CczProject project,
        EffectPackage package,
        EffectPatchSegment segment,
        string runMode,
        string exeSha256,
        int index,
        bool trustedRepair = false)
    {
        var contractId = segment.ContractId;
        var contractHash = segment.ContractHash;
        var codeIdentity = segment.ContractCodeIdentityHash;
        if (string.IsNullOrWhiteSpace(contractId)) contractId = package.Metadata.GetValueOrDefault("HookExecutionContractId", string.Empty);
        if (string.IsNullOrWhiteSpace(contractHash)) contractHash = package.Metadata.GetValueOrDefault("HookExecutionContractHash", string.Empty);
        if (string.IsNullOrWhiteSpace(codeIdentity)) codeIdentity = package.Metadata.GetValueOrDefault("ContractCodeIdentityHash", string.Empty);
        if ((!string.IsNullOrWhiteSpace(contractId) && (string.IsNullOrWhiteSpace(contractHash) || string.IsNullOrWhiteSpace(codeIdentity))))
        {
            var contract = new HookExecutionContractService().BuildContracts(project)
                .FirstOrDefault(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase));
            contractHash = contractHash.Length > 0 ? contractHash : contract?.ContractHash ?? string.Empty;
            codeIdentity = codeIdentity.Length > 0 ? codeIdentity : contract?.ContractCodeIdentityHash ?? string.Empty;
        }
        var request = new EffectWriteRequest
        {
            RequestId = $"{package.PackageId}:{index}",
            ModificationKind = segment.ModificationKind,
            SemanticFieldId = segment.SemanticFieldId,
            TargetFile = string.IsNullOrWhiteSpace(segment.TargetFile) ? "Ekd5.exe" : segment.TargetFile,
            SourceLocationId = segment.SourceLocationId,
            RequiredCapability = segment.RequiredCapability,
            ContractId = contractId,
            ContractHash = contractHash,
            ContractCodeIdentityHash = codeIdentity,
            HasExactLocation = !string.IsNullOrWhiteSpace(segment.ExpectedOldBytesHex) &&
                               (!string.IsNullOrWhiteSpace(segment.AddressKind) || !string.IsNullOrWhiteSpace(segment.SourceLocationId)),
            HasStaticContract = segment.ModificationKind is not (EffectModificationKind.BehaviorLogic or EffectModificationKind.SignedImmediateAdapter or EffectModificationKind.WrapperParameter) ||
                                (!string.IsNullOrWhiteSpace(contractHash) && !string.IsNullOrWhiteSpace(codeIdentity)) ||
                                !string.IsNullOrWhiteSpace(segment.AssemblySourceHash),
            HasTrustedRepairLineage = trustedRepair,
            RunMode = runMode
        };
        request.HasDynamicV3Evidence = request.ModificationKind != EffectModificationKind.BehaviorLogic ||
                                       HasTrustedV3(project, exeSha256, contractId, contractHash, codeIdentity);
        return request;
    }

    private static void NormalizeKnownClaims(EffectPackage package)
    {
        var logicalKind = package.Metadata.GetValueOrDefault("LogicalPatchKind", string.Empty);
        for (var index = 0; index < package.PatchSegments.Count; index++)
        {
            var segment = package.PatchSegments[index];
            if (!string.IsNullOrWhiteSpace(segment.ModificationKind) && !string.IsNullOrWhiteSpace(segment.SemanticFieldId)) continue;
            var inferred = InferKnownClaim(logicalKind, segment);
            if (inferred == null)
                throw new InvalidOperationException($"补丁段 {index + 1} 缺少结构化语义声明，且 LogicalPatchKind“{logicalKind}”不能安全迁移。");
            segment.ModificationKind = inferred.Value.Kind;
            segment.SemanticFieldId = inferred.Value.Field;
            segment.RequiredCapability = inferred.Value.Capability;
            segment.SourceLocationId = string.IsNullOrWhiteSpace(segment.SourceLocationId)
                ? $"{segment.TargetFile}:{segment.AddressKind}:{segment.Address:X8}"
                : segment.SourceLocationId;
        }
    }

    private static (string Kind, string Field, string Capability)? InferKnownClaim(string logicalKind, EffectPatchSegment segment)
    {
        var text = string.Join("|", segment.HookPoint, segment.Comment);
        if (logicalKind.Contains("remove", StringComparison.OrdinalIgnoreCase) ||
            logicalKind.Contains("repair", StringComparison.OrdinalIgnoreCase) ||
            logicalKind.Contains("disable", StringComparison.OrdinalIgnoreCase) ||
            logicalKind.Contains("enable", StringComparison.OrdinalIgnoreCase))
            return (EffectModificationKind.Maintenance, "maintenance:" + segment.HookPoint, EffectWriteCapability.RegisteredData);
        if (logicalKind.Equals("native-effect-update", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Contains("名称", StringComparison.Ordinal) || text.Contains("说明", StringComparison.Ordinal))
                return (EffectModificationKind.NameOrDescription, "native-text:" + segment.HookPoint, EffectWriteCapability.RegisteredData);
            if (!segment.TargetFile.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase))
                return (EffectModificationKind.Binding, "native-binding:" + segment.HookPoint, EffectWriteCapability.RegisteredData);
            return (EffectModificationKind.DirectEffectId, "native-parameter:" + segment.HookPoint, EffectWriteCapability.DirectParameter);
        }
        if (logicalKind.Contains("modular-semantic", StringComparison.OrdinalIgnoreCase) ||
            packageLooksBehavior(logicalKind, segment))
            return (EffectModificationKind.BehaviorLogic, "behavior:" + segment.HookPoint, EffectWriteCapability.DynamicBehavior);
        if (logicalKind.Contains("adapter", StringComparison.OrdinalIgnoreCase))
            return (EffectModificationKind.SignedImmediateAdapter, "adapter:" + segment.HookPoint, EffectWriteCapability.Adapter);
        if (logicalKind.Contains("composite", StringComparison.OrdinalIgnoreCase))
            return (EffectModificationKind.WrapperParameter, "composite:" + segment.HookPoint, EffectWriteCapability.Adapter);
        if (logicalKind.Contains("parameter", StringComparison.OrdinalIgnoreCase) || logicalKind.Contains("effect-id", StringComparison.OrdinalIgnoreCase))
            return (EffectModificationKind.DirectEffectId, "parameter:" + segment.HookPoint, EffectWriteCapability.DirectParameter);
        return null;

        static bool packageLooksBehavior(string kind, EffectPatchSegment value)
            => kind.Contains("dispatcher", StringComparison.OrdinalIgnoreCase) ||
               value.ContractId.EndsWith("-v2", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasTrustedV3(CczProject project, string exeSha256, string contractId, string contractHash, string codeIdentity)
        => !string.IsNullOrWhiteSpace(contractId) && EffectEvidenceBundleService.ReadTrustedV3(project, exeSha256, contractId)
            .Any(item => item.ContractHash.Equals(contractHash, StringComparison.OrdinalIgnoreCase) &&
                         item.ContractCodeIdentityHash.Equals(codeIdentity, StringComparison.OrdinalIgnoreCase) &&
                         item.ProbeRestored);

    private static List<string> ReadEvidenceBundleIds(CczProject project, string exeSha256, EffectWriteRequest request)
        => request.ModificationKind == EffectModificationKind.BehaviorLogic
            ? EffectEvidenceBundleService.ReadTrustedV3(project, exeSha256, request.ContractId)
                .Where(item => item.ContractHash.Equals(request.ContractHash, StringComparison.OrdinalIgnoreCase) &&
                               item.ContractCodeIdentityHash.Equals(request.ContractCodeIdentityHash, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.BundleId).ToList()
            : [];

    private static string ComputeAuthorizationHash(EffectWriteAuthorization authorization)
    {
        var copy = JsonSerializer.Deserialize<EffectWriteAuthorization>(JsonSerializer.Serialize(authorization, JsonOptions), JsonOptions)!;
        copy.DecisionHash = string.Empty;
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(copy, JsonOptions)));
    }

    private static EffectWriteRequest Request(string field, string kind, bool exact, bool hasStatic = false)
        => new() { SemanticFieldId = field, ModificationKind = kind, HasExactLocation = exact, HasStaticContract = hasStatic };
}
