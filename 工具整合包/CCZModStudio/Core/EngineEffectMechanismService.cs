using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EngineEffectMechanismService
{
    private const uint CoreAddress = 0x004101D9;
    private const uint WrapperAddress = 0x0042518F;
    private const uint WrapperPrecheck = 0x0040B922;
    private const uint WrapperCoreCall = 0x004251AB;
    private const uint WrapperFallbackCall = 0x004251B2;
    private const uint FallbackAddress = 0x0041301E;

    public EngineEffectMechanismProfile Build(CczProject project, string targetFile = "Ekd5.exe")
    {
        var inventory = new EffectInventoryService().Scan(project, targetFile);
        var engine = new EnginePatchProfileService().Build(project);
        var writable = new EffectWritableProfileService().Evaluate(project);
        var chinese = new EffectChineseDisplayService();
        var executable = ExecutableAnalysisSnapshotCache.Shared.GetBase(project.ResolveGameFile(targetFile));
        var instructionScan = executable.InstructionScan;
        var profile = new EngineEffectMechanismProfile
        {
            EngineVersion = engine.EngineVersion,
            ExeSha256 = inventory.ExeSha256,
            EvidenceExeSha256 = inventory.ExeSha256,
            IsKnownWritableProfile = engine.IsKnown && engine.EngineVersion == "6.5" && writable.CanWrite,
            WritableProfileId = writable.ProfileId,
            EvidenceScopeZh = writable.IsOriginalBaseline
                ? "当前 6.5 未加密基底的静态复读证据。"
                : writable.IsTrackedDescendant
                    ? "由当前 6.5 未加密基底和本工具事务记录共同证明。"
                    : "只读分析；不能跨 EXE 身份用于写入。"
        };

        profile.Functions.AddRange(
        [
            Function(profile, CoreAddress, "core_effect_engine", "双渠道特技判定核心",
                "接收单位指针和四个栈参数，检查个人/兵种与宝物渠道。",
                "ECX 为单位指针；依次压入个人号、宝物号、叠加方式、效果值方式；被调函数清理 16 字节参数。",
                "EAX 返回是否拥有或特效值，EDX 返回内部判定标志。"),
            Function(profile, FallbackAddress, "effect_channel_check", "特技渠道判定",
                "检查指定渠道是否提供目标特技。", "由核心判定或包装回退路径调用。", "返回渠道判定结果。"),
            Function(profile, 0x00413009, "effect_value_read", "特效值读取",
                "读取已经命中渠道的配置值。", "由渠道判定流程调用。", "EAX 返回配置值。"),
            Function(profile, WrapperAddress, "effect_wrapper", "特技判定包装层",
                "先执行预检查，再进入核心判定或渠道回退路径。", "接收与核心判定相同的四个栈参数，保留调用者的 ECX。", "透传判定结果。")
        ]);

        var wrapper = BuildKnownWrapperContract(profile, instructionScan);
        profile.WrapperContracts.Add(wrapper);
        profile.ComplexFamilyContracts.AddRange(BuildComplexFamilyContracts(profile, inventory));

        foreach (var effect in inventory.Effects)
        {
            var evidence = ResolveConsumerUsage(inventory, effect, instructionScan);
            var explanation = BuildExplanation(effect, evidence, chinese);
            var wrapperContract = effect.WrapperEntries.Count == 1
                ? profile.WrapperContracts.FirstOrDefault(item => item.EntryAddress == effect.WrapperEntries[0])
                : null;
            var directWritable = effect.CoreCalls.Count > 0 && effect.WrapperEntries.Count == 0;
            var wrapperWritable = effect.CoreCalls.Count > 0 && wrapperContract?.IsVerifiedForWrite == true;
            profile.Consumers.Add(new EffectConsumerRecord
            {
                InstanceId = effect.InstanceId,
                DisplayNameZh = effect.Name,
                TriggerPhaseZh = chinese.TriggerPhase(effect.TriggerPhase),
                ReturnUsage = evidence.Usage,
                ReturnUsageZh = ExplainReturnUsage(evidence.Usage),
                MeaningZh = explanation.SummaryZh,
                Explanation = explanation,
                ConsumerChainZh = evidence.ChainZh,
                CallSites = evidence.CallSites.Count > 0 ? evidence.CallSites : effect.CoreCalls.ToList(),
                IsWritable = profile.IsKnownWritableProfile && (directWritable || wrapperWritable),
                EditabilityReasonZh = !profile.IsKnownWritableProfile
                    ? writable.ReasonZh
                    : directWritable
                        ? "已恢复直接核心调用点，可进入带旧字节锁的预览。"
                        : wrapperWritable
                            ? "包装入口、参数映射和回退路径已在当前 SHA 上复读，可保留包装语义进行预览。"
                            : "调用链或参数来源尚未形成当前 SHA 对应的可写契约。"
            });
        }

        AddIdentityContracts(profile, writable);
        if (!profile.IsKnownWritableProfile) profile.WarningsZh.Add("当前 EXE 保持只读：" + writable.ReasonZh);
        if (inventory.ExeSha256.Equals(EffectWritableProfileStatus.LegacyDynamicEvidenceSha256, StringComparison.OrdinalIgnoreCase))
            profile.WarningsZh.Add("旧动态命中证据属于另一份 EXE，仅作为跨版本旁证。 ");
        profile.SummaryZh = $"已整理 {profile.Functions.Count} 个核心函数、{profile.WrapperContracts.Count} 个包装契约、" +
                            $"{profile.ComplexFamilyContracts.Count} 个复杂家族和 {profile.Consumers.Count} 个特效消费者；" +
                            (profile.IsKnownWritableProfile ? "当前基底允许安全预览。" : "当前基底只允许读取。 ");
        return profile;
    }

    private static WrapperContract BuildKnownWrapperContract(EngineEffectMechanismProfile profile, X86ScanResult scan)
    {
        var calls = scan.Instructions.Where(item => item.IsDirectCall &&
            item.Address >= WrapperAddress && item.Address < WrapperAddress + 0x40).ToDictionary(item => item.Address);
        var signatureMatches = calls.TryGetValue(0x0042519F, out var precheck) && precheck.BranchTarget == WrapperPrecheck &&
                               calls.TryGetValue(WrapperCoreCall, out var core) && core.BranchTarget == CoreAddress &&
                               calls.TryGetValue(WrapperFallbackCall, out var fallback) && fallback.BranchTarget == FallbackAddress;
        return new WrapperContract
        {
            ContractId = "ccz65-wrapper-0042518f-v1",
            EntryAddress = WrapperAddress,
            DisplayNameZh = "特技判定包装层",
            EvidenceExeSha256 = profile.ExeSha256,
            IsVerifiedForWrite = profile.IsKnownWritableProfile && signatureMatches,
            CoreCallAddress = WrapperCoreCall,
            FallbackCallAddress = WrapperFallbackCall,
            CallingConventionZh = "ECX 保持单位指针，四个 32 位参数位于调用栈；返回时清理 16 字节。",
            ParameterMappingZh = "调用者的个人号、宝物号、叠加方式、效果值方式原样转发给预检查和后续判定。",
            ReturnValueZh = "预检查通过时返回核心判定结果，否则返回渠道回退结果。",
            RegisterConstraintZh = "适配器只改写四个参数栈槽，不改变进入包装层时的 ECX。",
            FlagsConstraintZh = "包装层自行产生分支标志；适配器不得把外部 EFLAGS 当作输入。",
            ReasonZh = signatureMatches
                ? profile.IsKnownWritableProfile ? "当前 SHA 上的预检查、核心和回退三条调用边均已复读。" : "结构匹配，但 EXE 身份不允许写入。"
                : "包装层的预检查、核心或回退调用边与契约不一致。",
            CallerAddresses = scan.Instructions.Where(item => item.IsDirectCall && item.BranchTarget == WrapperAddress)
                .Select(item => item.Address).Distinct().OrderBy(value => value).ToList()
        };
    }

    private static IEnumerable<ComplexEffectFamilyContract> BuildComplexFamilyContracts(
        EngineEffectMechanismProfile profile,
        EffectInventoryReport inventory)
    {
        var definitions = new[]
        {
            new { Id = "ccz65-family-guard-final", Name = "护卫", Names = new[] { "护卫" }, Signatures = new[] { "guard", "护卫" }, Hooks = new uint[] { 0x004105C8, 0x0043ACA2, 0x0043DB3E, 0x00410F23 }, Conflicts = new[] { "guard-and-zombie-cave" } },
            new { Id = "ccz65-family-large-area-attack", Name = "大杀四方", Names = new[] { "大杀四方" }, Signatures = new[] { "large-area", "大杀四方" }, Hooks = Array.Empty<uint>(), Conflicts = new[] { "physical-attack-dispatch" } },
            new { Id = "ccz65-family-strategy-money", Name = "策略偷钱", Names = new[] { "策略偷钱" }, Signatures = new[] { "strategy-money", "策略偷钱" }, Hooks = new uint[] { 0x004259B4 }, Conflicts = new[] { "strategy-after-damage" } },
            new { Id = "ccz65-family-heart-poison", Name = "噬心毒咒", Names = new[] { "噬心毒咒", "策略随机状态" }, Signatures = new[] { "random-status", "噬心毒咒" }, Hooks = new uint[] { 0x004259AF }, Conflicts = new[] { "strategy-after-damage" } }
        };
        foreach (var definition in definitions)
        {
            var matches = inventory.Effects.Where(effect =>
                    definition.Names.Any(name => effect.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) ||
                    effect.Implementations.Any(implementation => definition.Signatures.Any(signature =>
                        implementation.SignatureId.Contains(signature, StringComparison.OrdinalIgnoreCase))))
                .ToList();
            var observedHooks = matches.SelectMany(item => item.EntryHooks).Distinct().OrderBy(value => value).ToList();
            var requiredHooksPresent = definition.Hooks.Length > 0 && definition.Hooks.All(observedHooks.Contains);
            var directCallsPresent = matches.Count > 0 && matches.All(item => item.CoreCalls.Count > 0 && item.WrapperEntries.Count == 0);
            var verified = profile.IsKnownWritableProfile && requiredHooksPresent && directCallsPresent;
            yield return new ComplexEffectFamilyContract
            {
                ContractId = definition.Id,
                DisplayNameZh = definition.Name,
                EvidenceExeSha256 = profile.ExeSha256,
                IsVerifiedForWrite = verified,
                NamePatterns = definition.Names.ToList(),
                SignatureIds = matches.SelectMany(item => item.Implementations.Select(implementation => implementation.SignatureId))
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                HookAddresses = definition.Hooks.ToList(),
                SharedHelperAddresses = matches.SelectMany(item => item.CodeEntries).Distinct().OrderBy(value => value).ToList(),
                ConflictGroups = definition.Conflicts.ToList(),
                DynamicValidationScenariosZh = ["在实际触发阶段命中全部入口，并确认原特效与复合编号各只执行一次。"],
                AdapterPolicy = "RedirectVerifiedDecisionCalls",
                ReasonZh = verified
                    ? "当前 SHA 上已同时确认补丁家族、全部必要入口和可恢复判定调用。"
                    : matches.Count == 0 ? "当前 EXE 未安装该补丁家族。"
                    : !requiredHooksPresent ? "已发现家族候选，但必要 Hook 集合不完整。"
                    : !directCallsPresent ? "家族成员没有唯一可恢复的直接判定调用。"
                    : "EXE 身份不允许写入。"
            };
        }
    }

    private static ConsumerEvidence ResolveConsumerUsage(EffectInventoryReport inventory, LogicalEffectInstance effect, X86ScanResult scan)
    {
        var guardStarts = inventory.Discovery.Candidates
            .SelectMany(candidate => candidate.CheckGroups)
            .Where(group => group.GuardCallAddress.HasValue && effect.CoreCalls.Contains(group.GuardCallAddress.Value))
            .Select(group => group.GuardStartAddress).Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToList();
        var entryCandidates = new HashSet<uint>(guardStarts.Concat(effect.WrapperEntries));
        var callers = scan.Instructions.Where(item => item.IsDirectCall && item.BranchTarget.HasValue && entryCandidates.Contains(item.BranchTarget.Value)).ToList();
        var chain = new List<string>();
        foreach (var caller in callers)
        {
            var section = scan.InstructionsBySection.Values.FirstOrDefault(items => items.Any(item => item.Address == caller.Address));
            if (section == null) continue;
            var index = section.FindIndex(item => item.Address == caller.Address);
            if (index < 0) continue;
            var alias = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "eax" };
            foreach (var next in section.Skip(index + 1).Take(18))
            {
                chain.Add($"{next.Address:X8} {next.Mnemonic}");
                if (next.Mnemonic.Equals("mov", StringComparison.OrdinalIgnoreCase) && next.Operands.Count >= 2 &&
                    next.Operands[0].Kind.Equals("Register", StringComparison.OrdinalIgnoreCase) &&
                    next.Operands[1].Kind.Equals("Register", StringComparison.OrdinalIgnoreCase) && alias.Contains(next.Operands[1].Register))
                    alias.Add(next.Operands[0].Register);
                var readsResult = next.RegistersRead.Any(alias.Contains) || next.Operands.Any(operand => alias.Contains(operand.Register));
                if (!readsResult) continue;
                var usage = ClassifyConsumer(next, section, index);
                if (usage != "Unknown") return new ConsumerEvidence(usage, callers.Select(item => item.Address).Distinct().ToList(), chain);
            }
        }
        return new ConsumerEvidence("Unknown", callers.Select(item => item.Address).Distinct().ToList(), chain);
    }

    private static string ClassifyConsumer(X86InstructionInfo instruction, IReadOnlyList<X86InstructionInfo> section, int callIndex)
    {
        var mnemonic = instruction.Mnemonic.ToLowerInvariant();
        if (mnemonic is "test" or "cmp")
        {
            var following = section.SkipWhile(item => item.Address <= instruction.Address).Take(4).ToList();
            if (following.Any(item => item.IsConditionalBranch)) return "BooleanSwitch";
        }
        if (mnemonic is "imul" or "mul") return "Multiplier";
        if (mnemonic is "idiv" or "div") return "Percentage";
        if (mnemonic is "add" or "sub") return "AdditiveValue";
        if (mnemonic is "and" or "or" or "xor" && instruction.Operands.Any(item => item.Kind == "Memory")) return "StateWrite";
        if (mnemonic == "mov" && instruction.Operands.FirstOrDefault()?.Kind == "Memory") return "StoredValue";
        if (mnemonic == "mov") return "ForwardedValue";
        return "Unknown";
    }

    private static EffectSemanticExplanation BuildExplanation(
        LogicalEffectInstance effect,
        ConsumerEvidence evidence,
        EffectChineseDisplayService chinese)
    {
        var trigger = chinese.TriggerPhase(effect.TriggerPhase);
        var channels = new List<string>();
        if (effect.PersonalChannel != null) channels.Add($"人物/兵种特技 {effect.PersonalChannel.DisplayName}");
        if (effect.ItemChannel?.IsEnabled == true) channels.Add($"宝物特效 {effect.ItemChannel.DisplayName}");
        var returnUsage = ExplainReturnUsage(evidence.Usage);
        var explanation = new EffectSemanticExplanation
        {
            TriggerZh = string.IsNullOrWhiteSpace(trigger) ? "触发时机尚未完整解析" : trigger,
            SubjectZh = "沿用调用点提供的当前单位。",
            ChannelDecisionZh = channels.Count == 0 ? "判定渠道尚未完整解析。" : "检查" + string.Join("与", channels) + "。",
            ReturnValueUsageZh = returnUsage,
            StateChangeZh = evidence.Usage switch
            {
                "AdditiveValue" => "返回值参与数值增减。",
                "Multiplier" => "返回值参与倍率计算。",
                "Percentage" => "返回值参与百分比换算。",
                "StateWrite" => "返回值控制状态字段写入。",
                "StoredValue" => "返回值被保存供后续流程使用。",
                _ => "后续状态变化尚未完整解析。"
            },
            EvidenceZh = effect.MatchedEvidence.Concat(evidence.ChainZh.Take(8)).Distinct().ToList()
        };
        if (evidence.Usage == "Unknown") explanation.MissingEvidenceZh.Add("尚未恢复返回值在消费者中的完整定义到使用链。");
        explanation.SummaryZh = $"{explanation.TriggerZh}，{explanation.ChannelDecisionZh}{explanation.ReturnValueUsageZh} {explanation.StateChangeZh}";
        return explanation;
    }

    private static string ExplainReturnUsage(string usage) => usage switch
    {
        "BooleanSwitch" => "消费者按返回值是否为零决定是否执行效果。",
        "AdditiveValue" => "消费者把返回值加入或减去当前数值。",
        "Multiplier" => "消费者把返回值用于倍率计算。",
        "Percentage" => "消费者把返回值用于百分比换算。",
        "StateWrite" => "消费者依据返回值修改状态字段。",
        "StoredValue" => "消费者把返回值保存到内存供后续使用。",
        "ForwardedValue" => "消费者把返回值转交给后续寄存器或调用。",
        _ => "返回值用途尚未完整解析。"
    };

    private static void AddIdentityContracts(EngineEffectMechanismProfile profile, EffectWritableProfileStatus writable)
    {
        profile.IdentityContracts.Add(new CompositeIdentityContract
        {
            ContractId = "ccz65-personal-job-fallback", Channel = CompositeEffectChannel.PersonalJob,
            DisplayNameZh = "人物/兵种复合编号兼容判定", IsVerified = profile.IsKnownWritableProfile,
            DisabledOtherChannelId = -1, UnitPointerSourceZh = "沿用成员调用点进入判定函数时的 ECX 单位指针。",
            EvidenceZh = "先执行原成员判定；原结果为零时，保留原宝物号并替换个人号再次判定。",
            BlockingReasonZh = profile.IsKnownWritableProfile ? string.Empty : writable.ReasonZh
        });
        profile.IdentityContracts.Add(new CompositeIdentityContract
        {
            ContractId = "ccz65-item-fallback", Channel = CompositeEffectChannel.Item,
            DisplayNameZh = "宝物复合编号兼容判定", IsVerified = false,
            DisabledOtherChannelId = -1, UnitPointerSourceZh = "沿用成员调用点进入判定函数时的 ECX 单位指针。",
            EvidenceZh = "先执行原成员判定；原结果为零时，保留原个人号并替换宝物号，不把个人号 FF 当作空值。",
            BlockingReasonZh = "宝物渠道尚无当前 SHA 的独立动态 ABI 证据，暂处于研究状态。"
        });
    }

    private static EngineEffectFunctionRecord Function(
        EngineEffectMechanismProfile profile,
        uint address, string name, string displayName, string role, string callingConvention, string returnValue)
        => new()
        {
            Address = address, Name = name, DisplayNameZh = displayName, RoleZh = role,
            CallingConventionZh = callingConvention, ReturnValueZh = returnValue,
            EvidenceExeSha256 = profile.ExeSha256, EvidenceScopeZh = profile.EvidenceScopeZh,
            SourceFiles = ["Ekd5.exe", "本地知识库/01-核心引擎/核心引擎.md", "本地知识库/01-核心引擎/桩函数.md"],
            EvidenceZh = ["当前 EXE 静态地址复读", "本地知识库核心引擎与桩函数记录"]
        };

    private sealed record ConsumerEvidence(string Usage, List<uint> CallSites, List<string> ChainZh);
}
