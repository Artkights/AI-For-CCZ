using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectModuleCatalogService
{
    public EffectModuleCatalog Build(CczProject project)
    {
        var inventory = new EffectInventoryService().Scan(project);
        var executionContracts = new HookExecutionContractService().BuildContracts(project);
        var contracts = new HookContractService().BuildContracts(project);
        var result = new EffectModuleCatalog { ExeSha256 = inventory.ExeSha256, EngineVersion = inventory.EngineVersion };
        AddCoreModules(result, contracts, executionContracts);
        AddRecipes(result);
        result.InstanceTags = inventory.Effects.Select(instance => Tag(instance, result)).ToList();
        result.SummaryZh = $"模块目录包含 {result.Modules.Count} 个类型化模块、{result.Recipes.Count} 个验证配方；当前可用于制作的模块 {result.Modules.Count(item => item.IsAvailableForAuthoring)} 个。";
        return result;
    }

    public EffectModuleDefinition Read(CczProject project, string moduleId)
        => Build(project).Modules.FirstOrDefault(item => item.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException($"没有找到特效模块“{moduleId}”。");

    private static void AddCoreModules(EffectModuleCatalog catalog, IReadOnlyList<HookContract> contracts, IReadOnlyList<HookExecutionContract> executionContracts)
    {
        var strategyExecution = executionContracts.FirstOrDefault(item => item.ContractFamilyId == "strategy-damage-formula");
        var physicalExecution = executionContracts.FirstOrDefault(item => item.ContractFamilyId == "physical-after-damage");
        var strategy = contracts.FirstOrDefault(item => item.ConflictGroup == strategyExecution?.ConflictGroup);
        var physical = contracts.FirstOrDefault(item => item.ConflictGroup == physicalExecution?.ConflictGroup);
        catalog.Modules.AddRange([
            Module("trigger.strategy-damage-calc", EffectModuleKind.Trigger, "策略伤害结算", "策略伤害数值已经形成、写回目标前触发。", strategy?.AllowPreview == true, strategy, "strategy-damage-context", "effect-subject"),
            Module("trigger.physical-after-damage", EffectModuleKind.Trigger, "物理伤害后", "物理伤害应用后触发。", physical?.AllowPreview == true, physical, "physical-damage-context", "effect-subject", physical?.SafetyNote ?? "当前契约仍需人工动态复核。"),
            Module("trigger.strategy-after-damage", EffectModuleKind.Trigger, "策略伤害后", "策略伤害应用后触发。", false, null, "strategy-post-damage", "effect-subject", "当前基底尚无自动预览 Hook 契约。"),
            Module("trigger.action-end", EffectModuleKind.Trigger, "行动结束", "单位完成一次行动后触发。", false, null, "action-context", "effect-subject", "尚未建立当前 SHA 的行动结束 Hook 契约。"),
            Module("trigger.turn-start", EffectModuleKind.Trigger, "回合开始", "单位或阵营回合开始时触发。", false, null, "turn-context", "effect-subject", "尚未建立当前 SHA 的回合 Hook 契约。"),
            Module("subject.effect-owner", EffectModuleKind.Subject, "特效拥有者", "传给核心特效判定函数的单位。",
                strategy?.AllowPreview == true || physical?.AllowPreview == true,
                physical?.AllowPreview == true ? physical : strategy, "effect-subject", "unit-pointer"),
            Module("subject.attacker", EffectModuleKind.Subject, "攻击者", "本次攻击或策略的发动单位。", false, null, "combat-context", "unit-pointer", "现有通用契约没有证明所有 Hook 的攻击者指针来源。"),
            Module("subject.defender", EffectModuleKind.Subject, "受击者", "本次伤害的承受单位。", false, null, "combat-context", "unit-pointer", "现有通用契约没有证明所有 Hook 的受击者指针来源。"),
            Module("target.single", EffectModuleKind.Target, "当前单体", "只作用于当前契约提供的单个单位。", strategy?.AllowPreview == true, strategy, "unit-pointer", "single-target"),
            Module("target.adjacent-enemies", EffectModuleKind.Target, "相邻敌军", "遍历相邻格内的敌方单位。", false, null, "map-and-unit-context", "target-list", "尚未验证统一的相邻单位遍历契约。"),
            Plain("condition.personal-or-item", EffectModuleKind.Condition, "个人或宝物特效判定", "通过 004101D9 按叠加方式检查个人/兵种和宝物渠道。", true),
            Plain("condition.hp-threshold", EffectModuleKind.Condition, "生命阈值", "根据单位当前生命比例决定是否生效。", false, "单位字段和比较时机尚未形成通用契约。"),
            Plain("condition.probability", EffectModuleKind.Condition, "概率判定", "按百分比概率决定是否执行效果。", false, "随机数函数、边界和调用副作用尚未形成通用契约。"),
            Value("value.switch", "开关", EffectParameterMeaningKind.Switch, 0, 1, "开关", true),
            Value("value.fixed", "固定值", EffectParameterMeaningKind.FixedValue, 0, 65535, "点", true),
            Value("value.current-damage-percentage", "当前伤害百分比", EffectParameterMeaningKind.Percentage, 0, 100, "%", strategy?.AllowPreview == true, strategy?.SafetyNote ?? "策略伤害槽尚未动态验证。"),
            Value("value.max-hp-percentage", "最大生命百分比", EffectParameterMeaningKind.Percentage, 0, 100, "%", false, "最大生命字段和恢复顺序尚未形成可写契约。"),
            Value("value.max-mp-percentage", "最大策略值百分比", EffectParameterMeaningKind.Percentage, 0, 100, "%", false, "最大策略值字段和恢复顺序尚未形成可写契约。"),
            Value("value.percentage", "百分比（兼容旧蓝图）", EffectParameterMeaningKind.Percentage, 0, 100, "%", strategy?.AllowPreview == true, strategy?.SafetyNote ?? "策略伤害槽尚未动态验证。"),
            Value("value.probability", "概率", EffectParameterMeaningKind.Probability, 0, 100, "%", false, "概率执行器尚未验证。"),
            Plain("action.compose-existing", EffectModuleKind.Action, "叠加现有特效", "保留每个成员原判定，未命中时让复合编号进入成员适配器。", true),
            Plain("action.modify-strategy-damage", EffectModuleKind.Action, "调整策略伤害", "按固定值或百分比调整策略伤害。", strategy?.AllowPreview == true, strategy?.SafetyNote ?? string.Empty),
            Plain("action.restore-mp", EffectModuleKind.Action, "恢复策略值", "在物理伤害段完成后恢复当前攻击者策略值，并按动态最大值封顶。", physical?.AllowPreview == true, physical?.SafetyNote ?? "物理恢复契约只在验证副本开放。"),
            Plain("action.apply-status", EffectModuleKind.Action, "附加异常状态", "向目标添加一个已验证的异常状态。", false, "状态写入和概率边界尚未形成通用契约。"),
            Plain("binding.personal-job", EffectModuleKind.Binding, "武将或兵种绑定", "通过人物、兵种、人物专属或套装表配置来源。", true),
            Plain("binding.item", EffectModuleKind.Binding, "物品绑定", "通过物品表配置宝物特效号和特效值。", true),
            Plain("safety.direct-core", EffectModuleKind.Safety, "直接核心判定", "成员直接调用 004101D9，四参数和调用点已恢复。", true),
            Plain("safety.verified-wrapper", EffectModuleKind.Safety, "已验证包装链", "成员通过当前 SHA 已验证的包装函数进入核心判定。", true),
            Plain("safety.read-only", EffectModuleKind.Safety, "只读证据", "可以识别和归纳，但不能自动生成写入包。", false, "缺少当前 SHA 的可写契约。")
        ]);
    }

    private static void AddRecipes(EffectModuleCatalog result)
    {
        result.Recipes.AddRange([
            new EffectModuleRecipe
            {
                RecipeId = "recipe.compose-existing-effects", DisplayNameZh = "复合现有特效",
                RequiredModuleIds = ["condition.personal-or-item", "action.compose-existing"],
                AllowedActionModuleIds = ["action.compose-existing"], AllowedValueModuleIds = ["value.switch", "value.fixed", "value.percentage"],
                MinimumMembers = 2, IsAvailable = true,
                ReasonZh = "复用现有复合适配器，只接纳直接核心调用、已验证包装链或已验证家族成员。"
            },
            new EffectModuleRecipe
            {
                RecipeId = "recipe.strategy-damage-adjust", DisplayNameZh = "策略伤害调整",
                RequiredModuleIds = ["trigger.strategy-damage-calc", "subject.effect-owner", "target.single", "condition.personal-or-item", "action.modify-strategy-damage"],
                AllowedActionModuleIds = ["action.modify-strategy-damage"], AllowedValueModuleIds = ["value.fixed", "value.current-damage-percentage", "value.percentage"],
                MinimumMembers = 1, IsAvailable = result.Modules.All(item => !new[] { "trigger.strategy-damage-calc", "action.modify-strategy-damage" }.Contains(item.ModuleId) || item.IsAvailableForAuthoring),
                ReasonZh = "使用当前基底已开放预览的策略伤害 Hook 契约。"
            },
            new EffectModuleRecipe
            {
                RecipeId = "recipe.physical-after-damage-mp", DisplayNameZh = "物理伤害后恢复策略值",
                RequiredModuleIds = ["trigger.physical-after-damage", "subject.effect-owner", "condition.personal-or-item", "action.restore-mp", "binding.personal-job"],
                AllowedActionModuleIds = ["action.restore-mp"], AllowedValueModuleIds = ["value.fixed"], MinimumMembers = 0,
                IsAvailable = result.Modules.All(item => !new[] { "trigger.physical-after-damage", "action.restore-mp" }.Contains(item.ModuleId) || item.IsAvailableForAuthoring),
                ReasonZh = "仅在自动验证副本中使用链式 continuation 契约；正式项目仍需 V3 证据。"
            },
            new EffectModuleRecipe { RecipeId = "recipe.post-damage-status", DisplayNameZh = "伤害后附加状态", RequiredModuleIds = ["action.apply-status"], AllowedActionModuleIds = ["action.apply-status"], AllowedValueModuleIds = ["value.probability"], IsAvailable = false, ReasonZh = "状态写入、目标指针和概率执行器尚未形成完整契约。" }
        ]);
    }

    private static EffectInstanceModuleTags Tag(LogicalEffectInstance instance, EffectModuleCatalog catalog)
    {
        var ids = new List<string> { "condition.personal-or-item" };
        var phase = instance.TriggerPhase;
        if (phase.Contains("策略", StringComparison.Ordinal) && phase.Contains("伤害", StringComparison.Ordinal)) ids.Add("trigger.strategy-after-damage");
        else if (phase.Contains("物理", StringComparison.Ordinal) && phase.Contains("伤害", StringComparison.Ordinal)) ids.Add("trigger.physical-after-damage");
        else if (phase.Contains("行动", StringComparison.Ordinal)) ids.Add("trigger.action-end");
        else if (phase.Contains("回合", StringComparison.Ordinal)) ids.Add("trigger.turn-start");
        var text = instance.Name + " " + instance.NaturalLanguageDescription;
        if (text.Contains("恢复", StringComparison.Ordinal) && text.Contains("MP", StringComparison.OrdinalIgnoreCase)) ids.Add("action.restore-mp");
        if (text.Contains("状态", StringComparison.Ordinal) || text.Contains("中毒", StringComparison.Ordinal)) ids.Add("action.apply-status");
        if (text.Contains("伤害", StringComparison.Ordinal) || text.Contains("增伤", StringComparison.Ordinal) || text.Contains("减伤", StringComparison.Ordinal)) ids.Add("action.modify-strategy-damage");
        foreach (var parameter in instance.Parameters)
        {
            ids.Add(parameter.MeaningKind switch
            {
                EffectParameterMeaningKind.Percentage => "value.percentage",
                EffectParameterMeaningKind.FixedValue => "value.fixed",
                EffectParameterMeaningKind.Probability => "value.probability",
                _ => "value.switch"
            });
        }
        ids = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var tags = ids.Select(id => catalog.Modules.FirstOrDefault(item => item.ModuleId == id)?.DisplayNameZh).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToList();
        var evidence = ids.Select(id => new EffectModuleTagEvidence
        {
            ModuleId = id,
            SourceKind = "DirectoryHeuristic",
            SourceId = instance.InstanceId,
            EvidenceExeSha256 = instance.EvidenceExeSha256,
            GrantsAuthoringCapability = false,
            ReasonZh = "目录标签来自名称、说明、阶段或参数归纳，仅用于浏览，不授予制作能力。"
        }).ToList();
        return new EffectInstanceModuleTags
        {
            InstanceId = instance.InstanceId, DisplayNameZh = instance.Name, ModuleIds = ids, TagsZh = tags,
            Evidence = evidence, CanGrantAuthoringCapability = false,
            SummaryZh = tags.Count == 0 ? "尚未完整解析" : string.Join(" + ", tags)
        };
    }

    private static EffectModuleDefinition Module(string id, string kind, string name, string meaning, bool available, HookContract? contract, string input, string output, string reason = "")
        => new() { ModuleId = id, Kind = kind, KindZh = KindZh(kind), DisplayNameZh = name, MeaningZh = meaning, IsVerified = contract != null, IsAvailableForAuthoring = available, ReasonZh = available ? string.Empty : string.IsNullOrWhiteSpace(reason) ? "缺少当前基底可写契约。" : reason, RequiredHookContractId = contract?.ContractId ?? string.Empty, RequiredContext = input, OutputContext = output, ConflictGroup = contract?.ConflictGroup ?? string.Empty, EvidenceExeSha256 = contract?.ExeSha256 ?? string.Empty, DynamicValidationScenariosZh = contract?.DynamicValidationPlan.ToList() ?? [] };
    private static EffectModuleDefinition Plain(string id, string kind, string name, string meaning, bool available, string reason = "") => new() { ModuleId = id, Kind = kind, KindZh = KindZh(kind), DisplayNameZh = name, MeaningZh = meaning, IsVerified = available, IsAvailableForAuthoring = available, ReasonZh = available ? string.Empty : reason };
    private static EffectModuleDefinition Value(string id, string name, string valueKind, int min, int max, string unit, bool available, string reason = "") { var result = Plain(id, EffectModuleKind.Value, name, $"使用{unit}表达成员参数。", available, reason); result.ValueKind = valueKind; result.Minimum = min; result.Maximum = max; result.UnitZh = unit; return result; }
    private static string KindZh(string kind) => kind switch { EffectModuleKind.Trigger => "触发时机", EffectModuleKind.Subject => "判定对象", EffectModuleKind.Target => "作用目标", EffectModuleKind.Condition => "生效条件", EffectModuleKind.Action => "实际效果", EffectModuleKind.Value => "数值方式", EffectModuleKind.Binding => "配置来源", EffectModuleKind.Safety => "安全契约", _ => "模块" };
}
