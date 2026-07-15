using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EnginePatchProfileService
{
    private readonly CczEngineProfileService _engineProfile = new();

    public EnginePatchProfile Build(CczProject project)
    {
        var detected = _engineProfile.Detect(project);
        var blocked = new List<BlockedCodeCaveRange>
        {
            new()
            {
                CaveId = "legacy-large-cave-E",
                StartVirtualAddress = 0x00601A9A,
                EndVirtualAddress = 0x00602500,
                Status = "blocked-unmapped",
                Reason = "当前 6.5 未加密基底不可映射，禁止作为默认代码洞。"
            }
        };

        var hookPoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var publicFunctions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var runtimeAddresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var specialSkillHookSpecs = new Dictionary<string, SpecialSkillHookSpec>(StringComparer.OrdinalIgnoreCase);
        var reserved = new List<AllocatedCodeCaveRange>();
        if (detected.VersionHint == "6.5")
        {
            hookPoints["effect_stub_dispatch"] = "0x00421175";
            hookPoints["physical_after_damage_mp_restore"] = "0x00418335";
            hookPoints["strategy_after_damage_random_status"] = "0x004259AF";
            hookPoints["strategy_after_damage_money"] = "0x004259B4";
            hookPoints["zombie_mp_defense"] = "0x00406006";
            hookPoints["strategy_floor_cap"] = "0x0043C2D5";
            hookPoints["ignore_strategy_reduction_a"] = "0x0043C242";
            hookPoints["ignore_strategy_reduction_b"] = "0x0043C266";
            hookPoints["guard_final_a"] = "0x004105C8";
            hookPoints["guard_final_b"] = "0x0043ACA2";
            hookPoints["guard_final_c"] = "0x0043DB3E";
            hookPoints["guard_final_d"] = "0x00410F23";
            publicFunctions["core_effect_engine"] = "0x004101D9";
            publicFunctions["clear_unit_flag"] = "0x00406690";
            publicFunctions["effect_stub_dispatch"] = "0x00421175";
            publicFunctions["battlefield_id_to_unit"] = $"0x{EngineRuntimeSemanticRegistry.BattlefieldIdToTacticalUnitAddress:X8}";
            publicFunctions["data_id_to_runtime_character"] = $"0x{EngineRuntimeSemanticRegistry.DataIdToRuntimeCharacterAddress:X8}";
            publicFunctions["tactical_unit_to_runtime_character"] = $"0x{EngineRuntimeSemanticRegistry.TacticalUnitToRuntimeCharacterAddress:X8}";
            publicFunctions["strategy_id_to_record"] = $"0x{EngineRuntimeSemanticRegistry.StrategyIdToRecordAddress:X8}";
            runtimeAddresses["unit_array_base"] = $"0x{EngineRuntimeSemanticRegistry.TacticalUnitArrayAddress:X8}";
            runtimeAddresses["battle_context_base"] = $"0x{EngineRuntimeSemanticRegistry.PhysicalAttackContextAddress:X8}";
            runtimeAddresses["strategy_context_base"] = $"0x{EngineRuntimeSemanticRegistry.StrategyContextAddress:X8}";
            runtimeAddresses["item_context_base"] = $"0x{EngineRuntimeSemanticRegistry.ItemContextAddress:X8}";
            runtimeAddresses["second_action_state"] = "0x00508B00";
            reserved.AddRange(BuildKnown65ReservedRanges());
            foreach (var spec in BuildKnown65SpecialSkillHookSpecs())
            {
                specialSkillHookSpecs[spec.HookPoint] = spec;
                if (!string.IsNullOrWhiteSpace(spec.TemplateId))
                {
                    specialSkillHookSpecs[spec.TemplateId] = spec;
                }
            }
        }

        return new EnginePatchProfile
        {
            EngineVersion = detected.VersionHint,
            ExeSha256 = detected.ExeSha256,
            IsKnown = detected.IsKnown,
            HookPoints = hookPoints,
            PublicFunctions = publicFunctions,
            RuntimeAddresses = runtimeAddresses,
            SpecialSkillHookSpecs = specialSkillHookSpecs,
            BlockedRanges = blocked,
            ReservedRanges = reserved,
            Warnings = detected.Warnings.ToList()
        };
    }

    private static IEnumerable<AllocatedCodeCaveRange> BuildKnown65ReservedRanges()
    {
        yield return Reserved("known-65-cave-D-pre-mp-restore", 0x004528FC, 0x004529A6, "Knowledge base sample-used range: 回MP攻击 171B variant; 136B variant overlaps.");
        yield return Reserved("known-65-random-status", 0x0041A627, 0x0041A6DB, "Knowledge base sample-used range: 噬心毒咒/策略随机状态 v3.");
        yield return Reserved("known-65-strategy-money", 0x0041A5A7, 0x0041A626, "Knowledge base sample-used range: 策略偷钱.");
        yield return Reserved("known-65-guard-final", 0x0041AA98, 0x0041AB9E, "Knowledge base sample-used range: 护卫最终版; overlaps zombie range.");
        yield return Reserved("known-65-zombie-mp-defense", 0x0041AB00, 0x0041AB2F, "Knowledge base sample-used range: 殭屍大法; overlaps guard-final range.");
        yield return Reserved("known-65-strategy-floor-bridge", 0x0043C528, 0x0043C548, "Knowledge base sample-used bridge range: 策略保底/策略限伤.");
        yield return Reserved("known-65-ignore-strategy-reduction", 0x0043D0D6, 0x0043D101, "Knowledge base sample-used range: 无视策略减伤.");
        yield return Reserved("known-65-strategy-floor-cap", 0x0043D3AE, 0x0043D44E, "Knowledge base sample-used range: 策略保底/策略限伤.");
    }

    private static AllocatedCodeCaveRange Reserved(string caveId, uint start, uint end, string reason)
        => new()
        {
            CaveId = caveId,
            StartVirtualAddress = start,
            EndVirtualAddress = end,
            Length = checked((int)(end - start + 1)),
            Reason = reason
        };

    private static IEnumerable<SpecialSkillHookSpec> BuildKnown65SpecialSkillHookSpecs()
    {
        yield return new SpecialSkillHookSpec
        {
            TemplateId = "strategy-damage-adjust-after-move",
            HookPoint = "strategy_after_damage_adjust_move",
            HookAddress = 0x0043C2B0,
            OverwriteLength = 5,
            Mode = "damage-adjust",
            SafetyLevel = "known-safe-template",
            AllowAutoPreview = true,
            UnitPointerSource = "dword [ebp-04]",
            DamageSlot = "strategy-damage-current",
            ConflictGroup = "strategy-damage-formula",
            RequiredCodeCaveBytes = 128,
            DynamicValidationPlan =
            {
                "Break at 0x0043C2B0 before the generated hook is installed and capture damage/register context.",
                "After preview, break at the allocated code cave entry and at 0x004101D9.",
                "Verify no other strategy-damage formula patch has already claimed this exact hook."
            },
            Notes =
            {
                "Single-entry strategy damage formula hook suitable for controlled increase/decrease templates.",
                "The generated v1 body is a scaffold unless FunctionAssemblySource is supplied after review."
            }
        };

        yield return new SpecialSkillHookSpec
        {
            TemplateId = "strategy-damage-adjust-around-allies",
            HookPoint = "strategy_after_damage_adjust_allies",
            HookAddress = 0x0043C2B5,
            OverwriteLength = 5,
            Mode = "damage-adjust",
            SafetyLevel = "known-safe-template",
            AllowAutoPreview = true,
            UnitPointerSource = "dword [ebp-04]",
            DamageSlot = "strategy-damage-current",
            ConflictGroup = "strategy-damage-formula",
            RequiredCodeCaveBytes = 128,
            DynamicValidationPlan =
            {
                "Break at 0x0043C2B5 before the generated hook is installed and capture damage/register context.",
                "After preview, break at the allocated code cave entry and at 0x004101D9.",
                "Verify ordering with strategy floor/cap and ignore-reduction patches."
            },
            Notes =
            {
                "Single-entry strategy damage formula hook suitable for controlled increase/decrease templates.",
                "This is near related strategy formula hooks; install order must be validated dynamically."
            }
        };

        yield return new SpecialSkillHookSpec
        {
            TemplateId = "physical-after-damage-mp-restore",
            HookPoint = "physical_after_damage_mp_restore",
            HookAddress = 0x00418335,
            OverwriteLength = 5,
            Mode = "post-damage",
            SafetyLevel = "sandbox-chain-template",
            AllowAutoPreview = true,
            UnitPointerSource = "path(dword [ebp+08])->dword [+0C]",
            DamageSlot = "current-mp",
            ConflictGroup = "physical-after-damage",
            RequiredCodeCaveBytes = 192,
            DynamicValidationPlan =
            {
                "Break at 0x00418335 and capture physical damage context.",
                "Verify original instruction length before replacing the known sample variant."
            },
            Notes =
            {
                "Known 136B/171B sample variants overlap; v1 should not auto-preview this unless a reviewed body is supplied."
            }
        };
    }
}
