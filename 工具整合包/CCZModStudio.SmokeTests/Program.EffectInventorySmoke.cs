using CCZModStudio.Core;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunEffectInventorySmoke(CczProject project)
    {
        var sourceProject = ResolveEffectInjectionDiscoverySmokeSourceProject(project);
        var inventory = new EffectInventoryService().Scan(sourceProject);
        if (inventory.NativeEffects.Count == 0 ||
            inventory.Diagnostics.Count == 0 ||
            inventory.NativeEffects.Any(item => item.SourceKind != EffectInstanceSourceKind.Native) ||
            inventory.InjectedEffects.Any(item => item.SourceKind != EffectInstanceSourceKind.Injected))
        {
            throw new InvalidOperationException("Effect inventory did not separate injected/native/diagnostic records.");
        }
        if (inventory.InjectedEffects.Count != inventory.ConfirmedInjectedEffects.Count ||
            inventory.InjectedEffects.Any(item => !item.HasInjectedImplementation) ||
            inventory.InjectedEffects.Any(item => item.Name.Contains("大杀四方", StringComparison.Ordinal)) ||
            !inventory.IncompleteOrHistoricalEffects.Any(item => item.Name.Contains("大杀四方", StringComparison.Ordinal) && !item.HasInjectedImplementation))
        {
            throw new InvalidOperationException("Known-sample similarity was incorrectly promoted to an installed effect.");
        }

        if (inventory.NativeEffects.Select(item => item.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != inventory.NativeEffects.Count ||
            inventory.InjectedEffects.Select(item => item.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != inventory.InjectedEffects.Count)
        {
            throw new InvalidOperationException("Effect inventory returned duplicate logical instance ids.");
        }

        if (inventory.Effects.Count == 0 ||
            inventory.Effects.Select(item => item.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != inventory.Effects.Count ||
            inventory.Effects.Select(item => item.Name).Distinct(StringComparer.CurrentCultureIgnoreCase).Count() != inventory.Effects.Count ||
            inventory.Effects.Any(item => item.Name.StartsWith("Wrapper", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("包装特技判定桩", StringComparison.Ordinal)) ||
            inventory.InjectedEffects.Any(item => item.WrapperEntries.Count > 0 && item.EntryHooks.Count == 0))
        {
            throw new InvalidOperationException("Unified effect inventory contains duplicate ids or promoted wrapper diagnostics.");
        }

        foreach (var effect in inventory.Effects)
        {
            if (effect.Parameters.GroupBy(parameter => parameter.Role, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Key is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment or InjectedEffectParameterRole.EffectValue && group.Count() > 1))
            {
                throw new InvalidOperationException("Logical personal/item/value parameters were not consolidated.");
            }

            foreach (var parameter in effect.Parameters)
            {
                if (parameter.IsConsistent && parameter.ObservedValues.Count > 1 ||
                    parameter.PhysicalPatchPoints.Select(point => point.Address).Distinct().Count() != parameter.PhysicalPatchPoints.Count)
                {
                    throw new InvalidOperationException("Logical parameter consistency or physical patch-point deduplication failed.");
                }
            }
        }

        var itemZero = inventory.ItemOptions.Single(item => item.EffectId == 0);
        var personalFf = inventory.PersonalJobOptions.Single(item => item.EffectId == 255);
        if (itemZero.IsEnabled || !itemZero.DisplayName.Contains("未启用", StringComparison.Ordinal) ||
            !personalFf.IsEnabled || !personalFf.DisplayName.Contains("FF", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Effect channel sentinel rules for item 00 / personal FF are incorrect.");
        }

        if (inventory.Effects.Any(item =>
                item.Name.StartsWith("回MP攻击", StringComparison.OrdinalIgnoreCase) &&
                item.PersonalChannel?.EffectId != 0xAD &&
                !item.Implementations.Any(implementation => implementation.Name.Contains("回MP攻击", StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("Item sentinel 00 incorrectly named a non-AD effect as MP recovery attack.");
        }

        var native = new NativeEffectConfigurationService().Read(sourceProject, CompositeEffectChannel.PersonalJob, 0xAA);
        if (native.FieldCapabilities.Count < 7 ||
            native.FieldCapabilities.All(item => item.FieldId != "name" || !item.CanEdit) ||
            !native.FieldCapabilities.Any(item => item.FieldId.StartsWith("personal:", StringComparison.OrdinalIgnoreCase) && item.CanEdit))
            throw new InvalidOperationException("Native effect fields were not exposed with per-field capabilities: " +
                                                string.Join(" | ", native.FieldCapabilities.Select(item =>
                                                    $"{item.FieldId}:edit={item.CanEdit}:locations={item.LocationIds.Count}:cap={item.WriteCapability}:blockers={string.Join(',', item.WriteDecision.BlockerCodes)}")));

        if (inventory.NativeEffects.Concat(inventory.InjectedEffects).Any(item =>
                string.IsNullOrWhiteSpace(item.NaturalLanguageDescription) ||
                string.IsNullOrWhiteSpace(item.TriggerPhase)))
        {
            throw new InvalidOperationException("Effect inventory did not produce natural-language meaning and trigger phases.");
        }

        var address = new ExeAddressSemanticService().Explain(sourceProject, 0x004101D9);
        if (address.FunctionName != "core_effect_engine" ||
            address.FileOffset < 0 ||
            string.IsNullOrWhiteSpace(address.InstructionText) ||
            address.RegistersRead.Count == 0 && address.RegistersWritten.Count == 0 ||
            !address.ChineseExplanation.Contains("x86", StringComparison.OrdinalIgnoreCase) &&
            !address.ChineseExplanation.Contains("复制", StringComparison.Ordinal) &&
            !address.ChineseExplanation.Contains("压入", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Address semantic explanation for 004101D9 is incomplete.");
        }

        var context = new ExeAddressSemanticService().BuildGenerationContext(sourceProject, null, null, null, null, 4000);
        if (context.Length > 4050 ||
            !context.Contains("004101D9", StringComparison.OrdinalIgnoreCase) ||
            !context.Contains("Hook 契约", StringComparison.Ordinal) ||
            !context.Contains("preview", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Effect generation context exceeded budget or omitted required guardrails.");
        }

        var contracts = new HookContractService().BuildContracts(sourceProject);
        if (contracts.Count == 0 || contracts.Any(contract => contract.HookAddress == 0 || string.IsNullOrWhiteSpace(contract.ExeSha256)))
        {
            throw new InvalidOperationException("Known 6.5 Hook contracts were not generated.");
        }

        Console.WriteLine($"EFFECT_INVENTORY_SMOKE_OK logical={inventory.Effects.Count} injected={inventory.InjectedEffects.Count} nativeCompatibility={inventory.NativeEffects.Count} diagnostics={inventory.Diagnostics.Count} contracts={contracts.Count} sha={inventory.ExeSha256[..8]}");
    }
}
