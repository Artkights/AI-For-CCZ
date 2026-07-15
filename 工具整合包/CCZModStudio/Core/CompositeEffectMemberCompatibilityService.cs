using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CompositeEffectMemberCompatibilityService
{
    public CompositeEffectCompatibility Evaluate(CczProject project, EffectInventoryReport inventory, CompositeEffectMember member)
        => Evaluate(project, inventory, new EngineEffectMechanismService().Build(project), member);

    public IReadOnlyDictionary<string, CompositeEffectCompatibility> EvaluateBatch(
        CczProject project,
        EffectInventoryReport inventory,
        EngineEffectMechanismProfile mechanism)
        => inventory.Effects.ToDictionary(
            item => item.InstanceId,
            item => Evaluate(project, inventory, mechanism, new CompositeEffectMember { InstanceId = item.InstanceId }),
            StringComparer.OrdinalIgnoreCase);

    private static CompositeEffectCompatibility Evaluate(
        CczProject project,
        EffectInventoryReport inventory,
        EngineEffectMechanismProfile mechanism,
        CompositeEffectMember member)
    {
        var instance = inventory.Effects.FirstOrDefault(item => item.InstanceId.Equals(member.InstanceId, StringComparison.OrdinalIgnoreCase));
        if (instance == null) return Unsupported(member.InstanceId, member.InstanceId, "当前扫描中不存在该成员。");
        var result = new CompositeEffectCompatibility
        {
            InstanceId = instance.InstanceId,
            DisplayNameZh = instance.Name,
            TriggerPhaseZh = instance.TriggerPhase,
            OriginalPersonalEffectId = instance.PersonalChannel?.EffectId,
            OriginalItemEffectId = instance.ItemChannel?.EffectId,
            EffectValueMode = member.EffectValueMode ?? instance.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.EffectValue)?.Value ?? 1,
            StackingMode = member.StackingMode ?? instance.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.BooleanOption)?.Value ?? 1,
            RedirectedCallSites = instance.CoreCalls.Distinct().ToList(),
            OriginalCallTarget = EffectPatchByteService.CoreEffectEngineAddress
        };
        var wrapper = instance.WrapperEntries.Count == 1
            ? mechanism.WrapperContracts.FirstOrDefault(item => item.EntryAddress == instance.WrapperEntries[0])
            : null;
        var family = mechanism.ComplexFamilyContracts.FirstOrDefault(item => item.IsVerifiedForWrite &&
            (item.NamePatterns.Any(pattern => instance.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)) ||
             item.SignatureIds.Any(signature => instance.Implementations.Any(implementation => implementation.SignatureId.Equals(signature, StringComparison.OrdinalIgnoreCase)))));

        if (instance.CoreCalls.Count == 0) return Fail(result, "没有恢复出可重定向的判定调用点。");
        if (instance.PatchCategory == InjectedEffectPatchCategory.FunctionExtensionPatch) return Fail(result, "引擎扩展不是普通特效判定，不能作为复合成员。");
        if (instance.PatchCategory == InjectedEffectPatchCategory.ComplexMultiHookPatch && family == null) return Fail(result, "复杂多入口成员尚未形成当前 SHA 对应的家族契约。");
        if (instance.WrapperEntries.Count > 1) return Fail(result, "成员存在多个包装入口，参数映射不唯一。");
        if (instance.WrapperEntries.Count == 1 && wrapper?.IsVerifiedForWrite != true) return Fail(result, wrapper?.ReasonZh ?? "包装函数没有可写契约。");
        if (result.EffectValueMode is < 0 or > 1 || result.StackingMode is < 0 or > 2) return Fail(result, "效果值方式或叠加方式超出已验证范围。");

        result.CompatibilityKind = family != null ? EffectMemberCompatibilityKind.VerifiedComplexFamily
            : wrapper != null ? EffectMemberCompatibilityKind.VerifiedWrapper
            : EffectMemberCompatibilityKind.DirectCoreCall;
        result.ContractId = family?.ContractId ?? wrapper?.ContractId ?? "ccz65-direct-core-call";
        result.OriginalCallTarget = wrapper?.EntryAddress ?? EffectPatchByteService.CoreEffectEngineAddress;
        if (result.RedirectedCallSites.Any(call => !EffectPatchByteService.IsDirectCallTo(project, call, result.OriginalCallTarget)))
            return Fail(result, "成员调用点当前目标与可写契约不一致。");
        result.IsCompatible = true;
        result.ReasonZh = result.CompatibilityKind switch
        {
            EffectMemberCompatibilityKind.VerifiedWrapper => "包装链和四参数映射已验证。",
            EffectMemberCompatibilityKind.VerifiedComplexFamily => "复杂补丁家族和必要入口已验证。",
            _ => "直接核心调用和四参数已恢复。"
        };
        return result;
    }

    public IReadOnlyList<CompositeEffectCompatibility> Search(CczProject project, string? keyword = null)
    {
        var inventory = new EffectInventoryService().Scan(project);
        var mechanism = new EngineEffectMechanismService().Build(project);
        return inventory.Effects
            .Where(item => string.IsNullOrWhiteSpace(keyword) || item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) || item.NaturalLanguageDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Select(item => Evaluate(project, inventory, mechanism, new CompositeEffectMember { InstanceId = item.InstanceId }))
            .ToList();
    }

    private static CompositeEffectCompatibility Unsupported(string id, string name, string reason)
        => new() { InstanceId = id, DisplayNameZh = name, ReasonZh = reason };
    private static CompositeEffectCompatibility Fail(CompositeEffectCompatibility value, string reason)
    {
        value.IsCompatible = false;
        value.CompatibilityKind = EffectMemberCompatibilityKind.Unsupported;
        value.ReasonZh = reason;
        return value;
    }
}
