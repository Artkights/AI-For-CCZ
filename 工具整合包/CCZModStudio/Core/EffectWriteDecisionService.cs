using System.Security.Cryptography;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectWriteDecisionService
{
    public EffectWriteDecision Decide(
        CczProject project,
        string modificationKind,
        bool hasExactLocation = true,
        bool hasStaticContract = false,
        bool hasDynamicV2Evidence = false,
        int affectedConsumers = 0)
        => Decide(project, new EffectWriteRequest
        {
            ModificationKind = modificationKind,
            HasExactLocation = hasExactLocation,
            HasStaticContract = hasStaticContract,
            HasDynamicV3Evidence = hasDynamicV2Evidence,
            AffectedConsumers = affectedConsumers
        });

    public EffectWriteDecision Decide(CczProject project, EffectWriteRequest request)
    {
        var audit = new ExecutableProfileAuditService().Audit(project);
        return Decide(project, request, audit);
    }

    internal EffectWriteDecision Decide(
        CczProject project,
        EffectWriteRequest request,
        ExecutableProfileAuditResult audit)
    {
        var writable = new EffectWritableProfileService().EvaluateFromAudit(project, audit);
        return Decide(project, request, audit, writable);
    }

    internal EffectWriteDecision Decide(
        CczProject project,
        EffectWriteRequest request,
        ExecutableProfileAuditResult audit,
        EffectWritableProfileStatus writable)
    {
        var sandbox = request.RunMode == EffectWriteRunMode.SandboxValidation && EffectSandboxService.IsSandbox(project);
        var localProfile = new LocalEffectProfileService().FindVerified(project, audit.CurrentSha256);
        var trustedRepair = request.ModificationKind == EffectModificationKind.Maintenance && request.HasTrustedRepairLineage;
        var trusted = writable.CanWrite || localProfile != null || trustedRepair;
        var result = new EffectWriteDecision
        {
            TrustStatus = writable.ProfileAudit.TrustStatus,
            ProfileTrustTier = localProfile != null
                ? EffectProfileTrustTier.LocalVerified
                : writable.CanWrite || trustedRepair
                    ? EffectProfileTrustTier.BuiltInVerified
                    : sandbox ? EffectProfileTrustTier.SandboxCandidate : EffectProfileTrustTier.ReadOnlyUnknown,
            SandboxOnly = sandbox,
            AffectedConsumers = request.AffectedConsumers
        };

        if (!trusted && !sandbox)
        {
            result.BlockerCodes.AddRange(audit.BlockerCodes);
            result.ReasonsZh.AddRange(audit.ReasonsZh);
            result.NextAction = "验证并支持此 EXE";
            return Finish(request, result);
        }
        if (!request.HasExactLocation)
        {
            result.BlockerCodes.Add("LOCATION_NOT_UNIQUE");
            result.ReasonsZh.Add("没有唯一且带容量和旧字节锁的物理位置。");
            result.NextAction = "重新扫描物理位置";
            return Finish(request, result);
        }

        if (sandbox)
        {
            if (request.ModificationKind == EffectModificationKind.BehaviorLogic && !request.HasStaticContract)
                Block(result, "STATIC_CONTRACT_REQUIRED", "沙箱行为探针仍需要结构化静态契约。", "结构化槽位和代码身份");
            else Grant(result, EffectWriteCapability.SandboxValidation, apply: true);
            result.SandboxOnly = true;
            result.NextAction = result.CanApply ? "仅写入自动测试副本" : "补齐静态契约";
            return Finish(request, result);
        }

        switch (request.ModificationKind)
        {
            case EffectModificationKind.NameOrDescription:
            case EffectModificationKind.Binding:
                Grant(result, EffectWriteCapability.RegisteredData, apply: true);
                break;
            case EffectModificationKind.DirectEffectId:
            case EffectModificationKind.ManagedParameter:
                Grant(result, EffectWriteCapability.DirectParameter, apply: true);
                break;
            case EffectModificationKind.SignedImmediateAdapter:
                if (!request.HasStaticContract)
                    Block(result, "ADAPTER_CONTRACT_REQUIRED", "宽参数适配器尚未证明完整指令窗口和无中间入口。", "适配器静态契约");
                else Grant(result, EffectWriteCapability.Adapter, apply: true);
                break;
            case EffectModificationKind.WrapperParameter:
                if (!request.HasStaticContract)
                    Block(result, "WRAPPER_CONTRACT_REQUIRED", "包装入口或四参数映射不唯一。", "静态包装契约");
                else Grant(result, EffectWriteCapability.Adapter, apply: true);
                break;
            case EffectModificationKind.BehaviorLogic:
                if (!request.HasStaticContract)
                    Block(result, "STATIC_CONTRACT_REQUIRED", "行为逻辑缺少结构化静态契约。", "结构化槽位和代码身份");
                else if (!request.HasDynamicV3Evidence)
                {
                    result.CanEdit = true;
                    result.CanPreview = true;
                    result.CanApply = false;
                    result.Capability = EffectWriteCapability.StaticPreviewOnly;
                    result.BlockerCodes.Add("DYNAMIC_V3_EVIDENCE_REQUIRED");
                    result.ReasonsZh.Add("静态位置已确定，但缺少同代码身份的 V3 动态验证证据，只允许诊断预览。");
                    result.RequiredEvidence.Add("同档案、同代码身份、同契约的 GameDebug V3 原始证据");
                    result.NextAction = "验证缺失权限";
                }
                else Grant(result, EffectWriteCapability.DynamicBehavior, apply: true);
                break;
            case EffectModificationKind.Maintenance:
                Grant(result, EffectWriteCapability.RegisteredData, apply: true);
                break;
            default:
                Block(result, "DIAGNOSTIC_ONLY", "样本相似、历史记录或来源不唯一，只能诊断。", "唯一静态来源");
                break;
        }

        if (string.IsNullOrWhiteSpace(result.NextAction))
            result.NextAction = result.CanApply ? "预览并写入" : result.CanPreview ? "查看诊断预览" : "补齐所需证据";
        return Finish(request, result);
    }

    private static EffectWriteDecision Finish(EffectWriteRequest request, EffectWriteDecision result)
    {
        var token = string.Join("|", request.ModificationKind, request.SemanticFieldId, request.TargetFile,
            request.SourceLocationId, request.RequiredCapability, request.ContractId, request.ContractHash,
            request.ContractCodeIdentityHash, request.HasExactLocation, request.HasStaticContract,
            request.HasDynamicV3Evidence, request.HasTrustedRepairLineage, request.RunMode, request.RequestedValue, request.AffectedConsumers,
            result.TrustStatus, result.ProfileTrustTier, result.CanEdit, result.CanPreview, result.CanApply,
            result.Capability, result.SandboxOnly, string.Join(";", result.BlockerCodes.Order()),
            string.Join(";", result.RequiredEvidence.Order()));
        result.DecisionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return result;
    }

    private static void Grant(EffectWriteDecision result, string capability, bool apply)
    {
        result.CanEdit = true;
        result.CanPreview = true;
        result.CanApply = apply;
        result.Capability = capability;
        result.ReasonsZh.Add("当前修改满足该字段级别的统一读写规则。");
    }

    private static void Block(EffectWriteDecision result, string code, string reason, string evidence)
    {
        result.BlockerCodes.Add(code);
        result.ReasonsZh.Add(reason);
        result.RequiredEvidence.Add(evidence);
    }
}
