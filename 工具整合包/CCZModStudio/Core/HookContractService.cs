using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class HookContractService
{
    public IReadOnlyList<HookContract> BuildContracts(CczProject project)
    {
        var profile = new EnginePatchProfileService().Build(project);
        var targetPath = project.ResolveGameFile("Ekd5.exe");
        if (!File.Exists(targetPath) || profile.EngineVersion != "6.5") return [];

        var executionContracts = new HookExecutionContractService().BuildContracts(project);
        return profile.SpecialSkillHookSpecs.Values
            .DistinctBy(spec => spec.TemplateId)
            .Where(spec => spec.HookAddress != 0)
            .Select(spec => BuildContract(project, profile, spec, executionContracts))
            .ToList();
    }

    public HookContract? Find(CczProject project, string? contractId, uint hookAddress)
    {
        var contracts = BuildContracts(project);
        if (!string.IsNullOrWhiteSpace(contractId))
        {
            return contracts.FirstOrDefault(contract => contract.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase));
        }

        return contracts.FirstOrDefault(contract => contract.HookAddress == hookAddress);
    }

    private static HookContract BuildContract(CczProject project, EnginePatchProfile profile, SpecialSkillHookSpec spec, IReadOnlyList<HookExecutionContract> executionContracts)
    {
        var oldBytes = ReadBytes(project, spec.HookAddress, spec.OverwriteLength);
        var paddingOnly = oldBytes.Length > 0 && oldBytes.All(value => value is 0x90 or 0xCC);
        var execution = executionContracts.FirstOrDefault(item => item.HookAddress == spec.HookAddress ||
            item.ContractFamilyId.Equals(spec.ConflictGroup, StringComparison.OrdinalIgnoreCase));
        return new HookContract
        {
            ContractId = string.IsNullOrWhiteSpace(spec.TemplateId) ? spec.HookPoint : spec.TemplateId,
            EngineVersion = profile.EngineVersion,
            ExeSha256 = profile.ExeSha256,
            TriggerPhase = TranslatePhase(spec.Mode, spec.HookPoint),
            HookAddress = spec.HookAddress,
            MinimumOverwriteLength = Math.Max(5, spec.OverwriteLength),
            ExpectedOldBytesHex = ToHex(oldBytes),
            UnitPointerSource = spec.UnitPointerSource,
            OriginalInstructionPolicy = execution?.ContinuationPolicy == HookContinuationPolicies.ChainExistingJumpTarget
                ? OriginalInstructionPolicies.ChainExistingJumpTarget
                : paddingOnly ? OriginalInstructionPolicies.PaddingOnly : OriginalInstructionPolicies.AutoRelocate,
            OriginalInstructionPlacement = OriginalInstructionPlacements.BeforeBody,
            PreserveFlags = true,
            ExpectedStackDelta = 0,
            ConflictGroup = spec.ConflictGroup,
            ExistingJumpTarget = execution?.ContinuationAddress ?? 0,
            AllowedTemplateIds = [spec.TemplateId],
            RequiredSymbols = ["core_effect_engine"],
            DynamicValidationPlan = spec.DynamicValidationPlan.ToList(),
            AllowPreview = spec.AllowAutoPreview && execution != null &&
                           (execution.AllowSemanticPreview ||
                            (EffectSandboxService.IsSandbox(project) && execution.VerificationStatus == HookContractVerificationStatus.StaticCandidate &&
                             execution.Slots.Any(item => item.IsStaticallyResolved))),
            SafetyNote = execution == null
                ? string.Join("；", spec.Notes)
                : execution.VerificationStatusZh + "；" + string.Join("；", execution.MissingEvidenceZh)
        };
    }

    private static byte[] ReadBytes(CczProject project, uint address, int length)
    {
        var executable = ExecutableAnalysisSnapshotCache.Shared.GetBase(project.ResolveGameFile("Ekd5.exe"));
        return executable.ReadVirtualRange(address, length).ToArray();
    }

    private static string TranslatePhase(string mode, string hookPoint)
    {
        var text = (mode + " " + hookPoint).ToLowerInvariant();
        if (text.Contains("strategy") && text.Contains("damage")) return "策略伤害计算阶段";
        if (text.Contains("physical") && text.Contains("damage")) return "物理伤害结算阶段";
        if (text.Contains("post-damage")) return "伤害结算后";
        return "战斗流程 Hook";
    }

    private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace('-', ' ');
}
