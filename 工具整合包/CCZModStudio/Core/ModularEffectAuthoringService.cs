using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ModularEffectAuthoringService
{
    public EffectBlueprintValidationResult Validate(CczProject project, ModularCompositeEffectBlueprint blueprint)
    {
        var result = new EffectBlueprintValidationResult();
        var catalog = new EffectModuleCatalogService().Build(project);
        var recipe = catalog.Recipes.FirstOrDefault(item => item.RecipeId.Equals(blueprint.RecipeId, StringComparison.OrdinalIgnoreCase));
        if (recipe == null) result.WarningsZh.Add("未找到指定的模块配方。");
        else if (!recipe.IsAvailable) result.WarningsZh.Add(recipe.ReasonZh);
        if (string.IsNullOrWhiteSpace(blueprint.Name)) result.WarningsZh.Add("必须填写中文特效名称。");
        if (blueprint.Channel is not CompositeEffectChannel.PersonalJob and not CompositeEffectChannel.Item) result.WarningsZh.Add("渠道必须是人物/兵种或宝物渠道。");
        if (blueprint.EffectId < 0) result.WarningsZh.Add("必须选择一个空闲的新特效编号。");
        if (blueprint.Bindings.Count == 0) result.WarningsZh.Add("必须配置至少一项真实绑定，确保新编号在实机中有触发来源。");

        var selectedIds = SelectedModuleIds(blueprint).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in selectedIds)
        {
            var module = catalog.Modules.FirstOrDefault(item => item.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (module == null) result.WarningsZh.Add($"模块“{id}”不存在。");
            else
            {
                result.ResolvedModules.Add(module);
                if (!module.IsAvailableForAuthoring) result.WarningsZh.Add($"{module.DisplayNameZh}：{module.ReasonZh}");
            }
        }
        if (recipe != null)
        {
            foreach (var required in recipe.RequiredModuleIds.Where(required => !selectedIds.Contains(required))) result.WarningsZh.Add($"缺少配方必需模块：{catalog.Modules.FirstOrDefault(item => item.ModuleId == required)?.DisplayNameZh ?? required}。");
            if (recipe.AllowedActionModuleIds.Count > 0 && !recipe.AllowedActionModuleIds.Contains(blueprint.ActionModuleId, StringComparer.OrdinalIgnoreCase)) result.WarningsZh.Add("所选实际效果不属于该配方允许范围。");
            if (recipe.AllowedValueModuleIds.Count > 0 && !recipe.AllowedValueModuleIds.Contains(blueprint.ValueModuleId, StringComparer.OrdinalIgnoreCase)) result.WarningsZh.Add("所选数值方式不属于该配方允许范围。");
            if (blueprint.Members.Count < recipe.MinimumMembers) result.WarningsZh.Add($"该配方至少需要 {recipe.MinimumMembers} 个现有特效成员。");
        }
        var valueModule = result.ResolvedModules.FirstOrDefault(item => item.ModuleId == blueprint.ValueModuleId);
        if (valueModule != null && blueprint.Value.HasValue && (blueprint.Value < valueModule.Minimum || blueprint.Value > valueModule.Maximum)) result.WarningsZh.Add($"数值必须在 {valueModule.Minimum}-{valueModule.Maximum}{valueModule.UnitZh} 之间。");
        var conflictGroups = result.ResolvedModules.Select(item => item.ConflictGroup).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (conflictGroups.Count > 1) result.WarningsZh.Add("所选模块属于不同 Hook 冲突组，不能编译为同一入口补丁。");

        if (recipe?.RecipeId == "recipe.compose-existing-effects" && result.WarningsZh.Count == 0)
        {
            var draft = new CompositeEffectService().Draft(project, blueprint.Channel, blueprint.EffectId, blueprint.Name, blueprint.Description,
                blueprint.Members.Select(member => member.InstanceId));
            draft.SchemaVersion = "2.0";
            draft.CompositeId = string.IsNullOrWhiteSpace(blueprint.BlueprintId) ? draft.CompositeId : blueprint.BlueprintId;
            draft.Bindings = blueprint.Bindings.ToList();
            draft.Members = blueprint.Members.Select(member => new CompositeEffectMember
            {
                InstanceId = member.InstanceId,
                EffectValue = member.EffectValue ?? blueprint.Value,
                EffectValueMode = member.EffectValueMode ?? (blueprint.ValueModuleId == "value.switch" ? 1 : 0),
                StackingMode = member.StackingMode ?? 1
            }).ToList();
            result.CompositeDraft = draft;
        }
        else if (recipe?.RecipeId == "recipe.strategy-damage-adjust" && result.WarningsZh.Count == 0)
        {
            result.SemanticProgram = BuildStrategyProgram(blueprint);
            result.CompiledSemanticBody = new SemanticEffectCompiler().Compile(project, result.SemanticProgram);
            result.WarningsZh.AddRange(result.CompiledSemanticBody.WarningsZh);
        }
        else if (recipe?.RecipeId == "recipe.physical-after-damage-mp" && result.WarningsZh.Count == 0)
        {
            result.SemanticProgram = BuildPhysicalRecoveryProgram(blueprint);
            result.CompiledSemanticBody = new SemanticEffectCompiler().Compile(project, result.SemanticProgram);
            result.WarningsZh.AddRange(result.CompiledSemanticBody.WarningsZh);
        }
        else if (recipe?.RecipeId != "recipe.compose-existing-effects" && result.WarningsZh.Count == 0)
        {
            result.WarningsZh.Add("该配方尚未形成当前 SHA 可写执行契约，只保留研究模块。 ");
        }
        result.IsValid = result.WarningsZh.Count == 0 && (result.CompositeDraft != null || result.CompiledSemanticBody?.CanPreview == true);
        result.SummaryZh = result.IsValid ? "模块兼容性检查通过，可进入复合特效预览。" : "模块兼容性检查未通过：" + string.Join("；", result.WarningsZh.Take(8));
        return result;
    }

    public ModularEffectPreview Preview(CczProject project, ModularCompositeEffectBlueprint blueprint)
    {
        var result = new ModularEffectPreview { Blueprint = blueprint, Validation = Validate(project, blueprint) };
        if (!result.Validation.IsValid) { result.WarningsZh.AddRange(result.Validation.WarningsZh); result.SummaryZh = result.Validation.SummaryZh; return result; }
        if (result.Validation.SemanticProgram != null && result.Validation.CompiledSemanticBody != null)
        {
            result.SemanticPreview = new ModularEffectLifecycleService().PreviewCreate(
                project, blueprint, result.Validation.SemanticProgram, result.Validation.CompiledSemanticBody);
            result.CanApply = result.SemanticPreview.CanApply;
            result.Package = result.SemanticPreview.Package;
            if (result.CanApply)
            {
                result.Package.Metadata["RecipeId"] = blueprint.RecipeId;
                new LockedEffectWriteReceiptService().Issue(project, result.Package, "modular-semantic-effect");
            }
            result.WarningsZh.AddRange(result.SemanticPreview.Warnings);
            result.SummaryZh = result.CanApply ? "模块化语义特效预览通过，可使用锁定包显式注入。" : result.SemanticPreview.Summary;
            return result;
        }
        if (result.Validation.CompositeDraft == null) { result.SummaryZh = "模块化预览缺少可编译草案。"; return result; }
        var finalMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LogicalPatchKind"] = "modular-composite-effect",
            ["BlueprintJson"] = System.Text.Json.JsonSerializer.Serialize(blueprint),
            ["RecipeId"] = blueprint.RecipeId
        };
        result.CompositePreview = new CompositeEffectService().Preview(project, result.Validation.CompositeDraft, finalMetadata);
        result.CanApply = result.CompositePreview.CanApply;
        result.Package = result.CompositePreview.Package;
        result.WarningsZh.AddRange(result.CompositePreview.WarningsZh);
        result.SummaryZh = result.CanApply ? "模块化复合特效预览通过，可使用锁定包显式注入。" : result.CompositePreview.SummaryZh;
        return result;
    }

    public CompositeEffectApplyResult Apply(CczProject project, EffectPackage package)
    {
        if (!package.Metadata.TryGetValue("LogicalPatchKind", out var kind) ||
            kind is not "modular-composite-effect" and not "modular-semantic-effect-v2" || !package.Metadata.ContainsKey("BlueprintJson"))
            throw new InvalidOperationException("只接受 preview_modular_effect 返回的锁定模块化复合特效包。");
        if (kind == "modular-semantic-effect-v2")
            return new ModularEffectLifecycleService().ApplyCreate(project, package);
        return new CompositeEffectService().Apply(project, package);
    }

    private static SemanticEffectProgram BuildStrategyProgram(ModularCompositeEffectBlueprint blueprint)
    {
        var percentage = blueprint.ValueModuleId is "value.current-damage-percentage" or "value.percentage";
        var subtract = blueprint.Description.Contains("减少", StringComparison.Ordinal) || blueprint.Description.Contains("减伤", StringComparison.Ordinal);
        return new SemanticEffectProgram
        {
            ProgramId = string.IsNullOrWhiteSpace(blueprint.BlueprintId) ? "semantic-" + Guid.NewGuid().ToString("N") : blueprint.BlueprintId,
            HookContractId = "strategy-damage-formula-v2", Channel = blueprint.Channel,
            PersonalEffectId = blueprint.EffectId, ItemEffectId = 0, EffectValueMode = 1, StackingMode = 1,
            SubjectSlotId = "strategy-effect-subject", TargetSlotId = "strategy-current-damage",
            Action = percentage
                ? subtract ? SemanticEffectAction.SubtractDamagePercent : SemanticEffectAction.AddDamagePercent
                : subtract ? SemanticEffectAction.SubtractDamageFixed : SemanticEffectAction.AddDamageFixed,
            ValueSource = SemanticEffectValueSource.Constant, Value = blueprint.Value ?? 0,
            BoundaryPolicy = subtract ? "MinimumOne" : "CheckedInt32"
        };
    }

    private static SemanticEffectProgram BuildPhysicalRecoveryProgram(ModularCompositeEffectBlueprint blueprint)
        => new()
        {
            ProgramId = string.IsNullOrWhiteSpace(blueprint.BlueprintId) ? "physical-recovery-" + Guid.NewGuid().ToString("N") : blueprint.BlueprintId,
            HookContractId = "physical-after-damage-recovery-v2", Channel = CompositeEffectChannel.PersonalJob,
            PersonalEffectId = blueprint.EffectId, ItemEffectId = 0, EffectValueMode = 0, StackingMode = 1,
            SubjectSlotId = "physical-effect-subject", TargetSlotId = "current-mp",
            Action = SemanticEffectAction.RestoreMpFixed, ValueSource = SemanticEffectValueSource.Constant,
            Value = blueprint.Value ?? 5, BoundaryPolicy = "DynamicMaximumMp"
        };

    private static AssemblyPatchPreviewResult PreviewSemantic(CczProject project, ModularCompositeEffectBlueprint blueprint, SemanticEffectProgram program, CompiledSemanticBody body)
    {
        var legacy = new HookContractService().BuildContracts(project).FirstOrDefault(item => item.ConflictGroup == body.Contract.ConflictGroup && item.AllowPreview);
        if (legacy == null) return new AssemblyPatchPreviewResult { Summary = "执行契约尚未动态验证，不能生成入口补丁。", Warnings = body.Contract.MissingEvidenceZh.ToList() };
        var draft = new AssemblyPatchDraft
        {
            Prompt = body.MeaningZh, TargetFile = "Ekd5.exe", EngineVersion = "6.5", EffectId = blueprint.EffectId,
            HookPoint = body.Contract.ContractFamilyId, HookAddress = body.Contract.HookAddress,
            OverwriteLength = 5, ExpectedOldBytesHex = EffectPatchByteService.ReadVirtualBytes(project, body.Contract.HookAddress, 5),
            HookContractId = legacy.ContractId, OriginalInstructionPolicy = legacy.OriginalInstructionPolicy,
            OriginalInstructionPlacement = OriginalInstructionPlacements.BeforeBody, PreserveFlags = true, ExpectedStackDelta = 0,
            RequiredSymbols = body.RequiredSymbols.ToList(), RequiredCodeCaveBytes = 160,
            AssemblySource = body.AssemblySource, RegisterStrategy = "语义编译器生成；pushad/popad 与 pushfd/popfd 成对保护。"
        };
        draft.Metadata["SemanticCompiler"] = "v2";
        draft.Metadata["SemanticProgramJson"] = System.Text.Json.JsonSerializer.Serialize(program);
        if (EffectSandboxService.IsSandbox(project)) draft.Metadata["EffectWriteRunMode"] = EffectWriteRunMode.SandboxValidation;
        return new AssemblyPatchCompiler().Preview(project, draft);
    }

    private static IEnumerable<string> SelectedModuleIds(ModularCompositeEffectBlueprint blueprint)
    {
        if (!string.IsNullOrWhiteSpace(blueprint.TriggerModuleId)) yield return blueprint.TriggerModuleId;
        if (!string.IsNullOrWhiteSpace(blueprint.SubjectModuleId)) yield return blueprint.SubjectModuleId;
        if (!string.IsNullOrWhiteSpace(blueprint.TargetModuleId)) yield return blueprint.TargetModuleId;
        foreach (var item in blueprint.ConditionModuleIds) if (!string.IsNullOrWhiteSpace(item)) yield return item;
        if (!string.IsNullOrWhiteSpace(blueprint.ActionModuleId)) yield return blueprint.ActionModuleId;
        if (!string.IsNullOrWhiteSpace(blueprint.ValueModuleId)) yield return blueprint.ValueModuleId;
        foreach (var item in blueprint.BindingModuleIds) if (!string.IsNullOrWhiteSpace(item)) yield return item;
        if (!string.IsNullOrWhiteSpace(blueprint.SafetyModuleId)) yield return blueprint.SafetyModuleId;
    }
}
