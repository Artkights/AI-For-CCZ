namespace CCZModStudio.Models;

public static class EffectModuleKind
{
    public const string Trigger = "TriggerModule";
    public const string Subject = "SubjectModule";
    public const string Target = "TargetModule";
    public const string Condition = "ConditionModule";
    public const string Action = "ActionModule";
    public const string Value = "ValueModule";
    public const string Binding = "BindingModule";
    public const string Safety = "SafetyModule";
}

public sealed class EffectModuleCatalog
{
    public string ExeSha256 { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public List<EffectModuleDefinition> Modules { get; set; } = [];
    public List<EffectModuleRecipe> Recipes { get; set; } = [];
    public List<EffectInstanceModuleTags> InstanceTags { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class EffectModuleDefinition
{
    public string ModuleId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string KindZh { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public string MeaningZh { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public bool IsAvailableForAuthoring { get; set; }
    public string ReasonZh { get; set; } = string.Empty;
    public string RequiredHookContractId { get; set; } = string.Empty;
    public string RequiredWrapperContractId { get; set; } = string.Empty;
    public string RequiredFamilyContractId { get; set; } = string.Empty;
    public string RequiredContext { get; set; } = string.Empty;
    public string OutputContext { get; set; } = string.Empty;
    public string ValueKind { get; set; } = string.Empty;
    public int? Minimum { get; set; }
    public int? Maximum { get; set; }
    public string UnitZh { get; set; } = string.Empty;
    public string ConflictGroup { get; set; } = string.Empty;
    public string EvidenceExeSha256 { get; set; } = string.Empty;
    public List<string> DynamicValidationScenariosZh { get; set; } = [];
}

public sealed class EffectModuleRecipe
{
    public string RecipeId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public List<string> RequiredModuleIds { get; set; } = [];
    public List<string> AllowedActionModuleIds { get; set; } = [];
    public List<string> AllowedValueModuleIds { get; set; } = [];
    public int MinimumMembers { get; set; } = 1;
    public bool IsAvailable { get; set; }
    public string ReasonZh { get; set; } = string.Empty;
}

public sealed class EffectInstanceModuleTags
{
    public string InstanceId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public List<string> ModuleIds { get; set; } = [];
    public List<string> TagsZh { get; set; } = [];
    public List<EffectModuleTagEvidence> Evidence { get; set; } = [];
    public bool CanGrantAuthoringCapability { get; set; }
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class ModularCompositeEffectBlueprint
{
    public string SchemaVersion { get; set; } = "1.0";
    public string BlueprintId { get; set; } = string.Empty;
    public string RecipeId { get; set; } = string.Empty;
    public string Channel { get; set; } = CompositeEffectChannel.PersonalJob;
    public int EffectId { get; set; } = -1;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TriggerModuleId { get; set; } = string.Empty;
    public string SubjectModuleId { get; set; } = string.Empty;
    public string TargetModuleId { get; set; } = string.Empty;
    public List<string> ConditionModuleIds { get; set; } = [];
    public string ActionModuleId { get; set; } = string.Empty;
    public string ValueModuleId { get; set; } = string.Empty;
    public int? Value { get; set; }
    public List<string> BindingModuleIds { get; set; } = [];
    public string SafetyModuleId { get; set; } = string.Empty;
    public List<CompositeEffectMember> Members { get; set; } = [];
    public List<EffectPackageBinding> Bindings { get; set; } = [];
}

public sealed class EffectBlueprintValidationResult
{
    public bool IsValid { get; set; }
    public string SummaryZh { get; set; } = string.Empty;
    public List<string> WarningsZh { get; set; } = [];
    public List<EffectModuleDefinition> ResolvedModules { get; set; } = [];
    public CompositeEffectDraft? CompositeDraft { get; set; }
    public SemanticEffectProgram? SemanticProgram { get; set; }
    public CompiledSemanticBody? CompiledSemanticBody { get; set; }
}

public sealed class ModularEffectPreview
{
    public bool CanApply { get; set; }
    public string SummaryZh { get; set; } = string.Empty;
    public List<string> WarningsZh { get; set; } = [];
    public ModularCompositeEffectBlueprint Blueprint { get; set; } = new();
    public EffectBlueprintValidationResult Validation { get; set; } = new();
    public CompositeEffectPreview CompositePreview { get; set; } = new();
    public AssemblyPatchPreviewResult SemanticPreview { get; set; } = new();
    public EffectPackage Package { get; set; } = new();
}
