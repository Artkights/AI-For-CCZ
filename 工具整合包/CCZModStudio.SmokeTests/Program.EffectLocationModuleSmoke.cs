using CCZModStudio.Core;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunEffectLocationSmoke(CczProject project)
    {
        var source = ResolveEffectInjectionDiscoverySmokeSourceProject(project);
        var index = new EffectIdLocationIndexService().Scan(source);
        if (index.Locations.Count == 0 || index.Locations.All(item => item.Kind != EffectIdLocationKind.NativeTableField) ||
            index.Locations.All(item => item.Kind is not EffectIdLocationKind.StubImmediate and not EffectIdLocationKind.InjectedPatchParameter and not EffectIdLocationKind.WrapperForwardedArgument) ||
            index.ReportPaths.Count != 2 || index.ReportPaths.Any(path => !File.Exists(path)))
        {
            throw new InvalidOperationException("特效号位置索引没有覆盖指令、原生表或报告导出。");
        }
        if (index.Locations.Any(item => item.Channel == EffectChannelKind.Item && item.EffectId == 0 && item.EffectNameZh.Contains("回MP攻击", StringComparison.Ordinal)))
            throw new InvalidOperationException("宝物渠道 00 被错误借用为具体特效名称。");
        if (index.Locations.Where(item => item.WriteCapability is EffectIdWriteCapability.DirectWritable or EffectIdWriteCapability.TransactionWritable)
            .Any(item => string.IsNullOrWhiteSpace(item.ExpectedOldBytesHex) || !item.FileOffset.HasValue))
            throw new InvalidOperationException("可写位置缺少文件偏移或旧字节锁。");
        if (index.Locations.Where(item => item.TargetFile.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) &&
                                          item.WriteCapability is EffectIdWriteCapability.DirectWritable or EffectIdWriteCapability.TransactionWritable or EffectIdWriteCapability.AdapterRequired)
            .Any(item => item.FileOffset is < 0x400))
            throw new InvalidOperationException("可写机器码位置错误落入 PE 文件头。");

        const uint baseAddress = 0x00510000;
        var bytes = new byte[] { 0xB8, 0xB0, 0, 0, 0, 0x50, 0xE8, 0, 0, 0, 0 };
        var instructions = new X86InstructionScanner().DecodeBlock(bytes, baseAddress);
        var argument = X86InstructionScanner.BackwardSliceStackArguments(instructions, 2, maximumArguments: 1).Single();
        if (argument.Value != 0xB0 || argument.OperandAddress != baseAddress + 1 || argument.DefinitionInstructionAddress != baseAddress ||
            argument.SourceKind != X86ArgumentSourceKind.RegisterDefinitionImmediate || !argument.IsDirectlyPatchable)
            throw new InvalidOperationException("push reg 的精确常量来源没有指向 mov 立即数字节。");

        var imm8 = index.Locations.FirstOrDefault(item => item.IsSigned && item.ByteLength == 1 && item.WriteCapability == EffectIdWriteCapability.AdapterRequired);
        if (imm8 != null)
        {
            var preview = new EffectIdLocationIndexService().PreviewUpdate(source, new EffectIdUpdateRequest { LocationId = imm8.LocationId, NewValue = 0xB0 });
            if (!preview.CanApply && preview.WarningsZh.Any(item => item.Contains("不能原地改写", StringComparison.Ordinal) || item.Contains("功能未实现", StringComparison.Ordinal)))
                throw new InvalidOperationException("push imm8 高位编号仍停留在旧的未实现阻断路径。");
            if (preview.CanApply && preview.Package.Metadata.GetValueOrDefault("LogicalPatchKind") != "effect-parameter-adapter-v2")
                throw new InvalidOperationException("push imm8 高位编号没有生成 V2 宽参数适配器。");
            var lowPreview = new EffectIdLocationIndexService().PreviewUpdate(source, new EffectIdUpdateRequest { LocationId = imm8.LocationId, NewValue = 0x7F });
            if (!lowPreview.CanApply || lowPreview.Package.PatchSegments.Count != 1)
                throw new InvalidOperationException("push imm8 的 00-7F 范围没有继续使用原位锁定修改。");
        }
        Console.WriteLine($"EFFECT_LOCATION_SMOKE_OK locations={index.Locations.Count} direct={index.CountsByWriteCapability.GetValueOrDefault(EffectIdWriteCapability.DirectWritable)} transaction={index.CountsByWriteCapability.GetValueOrDefault(EffectIdWriteCapability.TransactionWritable)} sha={index.ExeSha256[..8]}");
    }

    static void RunEffectModuleSmoke(CczProject project)
    {
        var source = ResolveEffectInjectionDiscoverySmokeSourceProject(project);
        var catalog = new EffectModuleCatalogService().Build(source);
        if (catalog.Modules.Select(item => item.Kind).Distinct().Count() < 8 || catalog.Recipes.Count < 4 || catalog.InstanceTags.Count == 0 ||
            catalog.Modules.All(item => item.ModuleId != "condition.personal-or-item" || !item.IsAvailableForAuthoring) ||
            catalog.Recipes.All(item => item.RecipeId != "recipe.compose-existing-effects" || !item.IsAvailable))
            throw new InvalidOperationException("类型化模块目录、配方或扫描标签不完整。");

        var executionContracts = new HookExecutionContractService().BuildContracts(source);
        var strategyContract = executionContracts.Single(item => item.ContractId == "strategy-damage-formula-v2");
        var recoveryContract = executionContracts.Single(item => item.ContractId == "physical-after-damage-recovery-v2");
        if (strategyContract.AllowSemanticPreview || recoveryContract.AllowSemanticPreview ||
            strategyContract.VerificationStatus != HookContractVerificationStatus.StaticCandidate ||
            strategyContract.Slots.All(item => item.SlotId != "strategy-current-damage" || item.Access != ContextSlotAccess.ReadWrite))
            throw new InvalidOperationException("未验证的策略/恢复执行契约被错误开放，或策略伤害槽缺失。");
        var probe = new HookExecutionContractService().CreateProbePlan(source, strategyContract.ContractId);
        if (probe.BreakpointAddresses.Count < 3 || probe.RequiredCapturesZh.Count < 4 || probe.ExeSha256 != strategyContract.ExeSha256)
            throw new InvalidOperationException("执行契约动态探针计划不完整。");

        var semantic = new SemanticEffectCompiler().Compile(source, new SemanticEffectProgram
        {
            ProgramId = "semantic-smoke", HookContractId = strategyContract.ContractId,
            Channel = CompositeEffectChannel.PersonalJob, PersonalEffectId = 0xB0, ItemEffectId = 0,
            SubjectSlotId = "strategy-effect-subject", TargetSlotId = "strategy-current-damage",
            Action = SemanticEffectAction.AddDamagePercent, ValueSource = SemanticEffectValueSource.Constant, Value = 25
        });
        if (!semantic.CanCompile || semantic.CanPreview || !semantic.AssemblySource.Contains("idiv ecx", StringComparison.OrdinalIgnoreCase) ||
            !semantic.AssemblySource.Contains("mov ecx, dword [ebp-0x10]", StringComparison.OrdinalIgnoreCase) ||
            !semantic.AssemblySource.Contains("dword [ebp-0x04]", StringComparison.OrdinalIgnoreCase) ||
            semantic.AssemblySource.Contains("[ebp-0xF0]", StringComparison.OrdinalIgnoreCase) ||
            semantic.AssemblySource.Contains("[ebp-0xFC]", StringComparison.OrdinalIgnoreCase) ||
            semantic.WarningsZh.All(item => !item.Contains("动态验证", StringComparison.Ordinal)))
            throw new InvalidOperationException("受约束语义编译器没有生成百分比动作，或绕过了动态证据门禁。");

        var dispatcherDraft = new EffectTriggerDispatcherDraft
        {
            DispatcherId = "dispatcher-smoke", HookContractId = strategyContract.ContractId, Capacity = 16,
            Entries =
            [
                new EffectDispatcherEntry { EntryId = "damage-a", PersonalEffectId = 0xB0, ItemEffectId = 0, EffectValueMode = 1, StackingMode = 1, Action = SemanticEffectAction.AddDamageFixed, ValueSource = SemanticEffectValueSource.Constant, Value = 20, ExecutionOrder = 10 },
                new EffectDispatcherEntry { EntryId = "damage-b", PersonalEffectId = 0xB1, ItemEffectId = 0, EffectValueMode = 1, StackingMode = 1, Action = SemanticEffectAction.SubtractDamagePercent, ValueSource = SemanticEffectValueSource.Constant, Value = 15, ExecutionOrder = 20 }
            ]
        };
        var dispatcherService = new EffectTriggerDispatcherService();
        var registry = dispatcherService.BuildRegistry(dispatcherDraft);
        var reread = dispatcherService.ReadRegistry(registry, dispatcherDraft.DispatcherId, dispatcherDraft.HookContractId);
        var compiledDispatcher = dispatcherService.Compile(source, dispatcherDraft);
        if (registry.Length != 16 + 16 * EffectTriggerDispatcherService.EntrySize || reread.Entries.Count != 2 ||
            reread.Entries[1].Action != SemanticEffectAction.SubtractDamagePercent || !compiledDispatcher.CanCompile || compiledDispatcher.CanPreview)
            throw new InvalidOperationException("共享调度注册表往返或动态门禁不正确。");

        var inventory = new EffectInventoryService().Scan(source);
        var members = inventory.Effects.Where(item => item.CoreCalls.Count > 0 && item.WrapperEntries.Count == 0).Take(2).ToList();
        var free = new CompositeEffectService().FindFreeIds(source, CompositeEffectChannel.PersonalJob).FreeIds.Concat(
            new CompositeEffectService().FindFreeIds(source, CompositeEffectChannel.PersonalJob).ReclaimableIds).FirstOrDefault(-1);
        if (members.Count < 2 || free < 0) throw new InvalidOperationException("模块化烟测缺少兼容成员或空闲编号。");
        var blueprint = new ModularCompositeEffectBlueprint
        {
            BlueprintId = "module-smoke", RecipeId = "recipe.compose-existing-effects", Channel = CompositeEffectChannel.PersonalJob,
            EffectId = free, Name = "模块烟测", Description = "验证模块蓝图到复合预览的转换。",
            ConditionModuleIds = ["condition.personal-or-item"], ActionModuleId = "action.compose-existing",
            ValueModuleId = "value.fixed", SafetyModuleId = "safety.direct-core",
            Members = members.Select(item => new CompositeEffectMember { InstanceId = item.InstanceId, EffectValue = 3 }).ToList(),
            Bindings = [new EffectPackageBinding { Kind = "job_assignment", PersonId = 0, EffectValue = 3 }]
        };
        var preview = new ModularEffectAuthoringService().Preview(source, blueprint);
        if (!preview.CanApply || preview.Package.Metadata.GetValueOrDefault("LogicalPatchKind") != "modular-composite-effect" ||
            !preview.Package.Metadata.ContainsKey("BlueprintJson") || preview.CompositePreview.ParameterBlock.Records.Count != 2)
            throw new InvalidOperationException("模块化蓝图没有生成可追溯的复合特效预览包：" + preview.SummaryZh);

        blueprint.ActionModuleId = "action.apply-status";
        var rejected = new ModularEffectAuthoringService().Validate(source, blueprint);
        if (rejected.IsValid || rejected.WarningsZh.Count == 0)
            throw new InvalidOperationException("不兼容的效果模块没有被配方矩阵阻断。");
        Console.WriteLine($"EFFECT_MODULE_SMOKE_OK modules={catalog.Modules.Count} recipes={catalog.Recipes.Count} tags={catalog.InstanceTags.Count} segments={preview.Package.PatchSegments.Count} contracts={executionContracts.Count} dispatcher={reread.Entries.Count}");
    }
}
