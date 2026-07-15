namespace CCZModStudio.Models;

public sealed class SpecialSkillHookSpec
{
    public string TemplateId { get; init; } = string.Empty;
    public string HookPoint { get; init; } = string.Empty;
    public uint HookAddress { get; init; }
    public int OverwriteLength { get; init; } = 5;
    public string Mode { get; init; } = "damage-adjust";
    public string SafetyLevel { get; init; } = "manual-review-template";
    public bool AllowAutoPreview { get; init; }
    public string UnitPointerSource { get; init; } = "dword [ebp-04]";
    public string DamageSlot { get; init; } = string.Empty;
    public string ReturnStrategy { get; init; } = "hook+overwriteLength";
    public string ConflictGroup { get; init; } = string.Empty;
    public int RequiredCodeCaveBytes { get; init; } = 96;
    public List<string> DynamicValidationPlan { get; init; } = [];
    public List<string> Notes { get; init; } = [];

    public string HookAddressHex => $"0x{HookAddress:X8}";
}

public sealed class InlineSpecialSkillPatchDraft
{
    public string Prompt { get; set; } = string.Empty;
    public string TargetFile { get; set; } = "Ekd5.exe";
    public string EngineVersion { get; set; } = string.Empty;
    public int EffectId { get; set; }
    public string Mode { get; set; } = "damage-adjust";
    public string TemplateId { get; set; } = string.Empty;
    public string HookPoint { get; set; } = string.Empty;
    public uint HookAddress { get; set; }
    public string HookAddressHex { get; set; } = string.Empty;
    public int OverwriteLength { get; set; } = 5;
    public string ExpectedOldBytesHex { get; set; } = string.Empty;
    public uint ReturnAddress { get; set; }
    public string ReturnAddressHex { get; set; } = string.Empty;
    public int PersonalEffectId { get; set; }
    public int ItemEffectId { get; set; }
    public int EffectValueFlag { get; set; }
    public int StackFlag { get; set; } = 1;
    public string ParameterEncodingPolicy { get; set; } = "auto-wide";
    public string UnitPointerSource { get; set; } = "dword [ebp-04]";
    public string FunctionAssemblySource { get; set; } = "nop";
    public string HookContractId { get; set; } = string.Empty;
    public string OriginalInstructionPolicy { get; set; } = string.Empty;
    public string OriginalInstructionPlacement { get; set; } = OriginalInstructionPlacements.AfterBody;
    public bool PreserveFlags { get; set; } = true;
    public int ExpectedStackDelta { get; set; }
    public List<string> RequiredSymbols { get; set; } = [];
    public int RequiredCodeCaveBytes { get; set; } = 96;
    public bool AllowPreview { get; set; }
    public InlineSpecialSkillPatch LogicalPatch { get; set; } = new();
    public List<string> Warnings { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<string> DynamicValidationPlan { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class InlineSpecialSkillPatch
{
    public string LogicalPatchKind { get; set; } = "inline-special-skill";
    public SpecialSkillHookJumpModule HookJump { get; set; } = new();
    public SpecialSkillStubAndBodyModule StubAndBody { get; set; } = new();
    public SpecialSkillParameterPatchPoint PersonalEffectPatchPoint { get; set; } = new();
    public SpecialSkillParameterPatchPoint ItemEffectPatchPoint { get; set; } = new();
}

public sealed class SpecialSkillHookJumpModule
{
    public string HookPoint { get; set; } = string.Empty;
    public uint HookAddress { get; set; }
    public int OverwriteLength { get; set; }
    public string ExpectedOldBytesHex { get; set; } = string.Empty;
    public uint ReturnAddress { get; set; }
    public string CodeCaveId { get; set; } = string.Empty;
    public uint CodeCaveAddress { get; set; }

    public string HookAddressHex => HookAddress == 0 ? string.Empty : $"0x{HookAddress:X8}";
    public string ReturnAddressHex => ReturnAddress == 0 ? string.Empty : $"0x{ReturnAddress:X8}";
    public string CodeCaveAddressHex => CodeCaveAddress == 0 ? string.Empty : $"0x{CodeCaveAddress:X8}";
}

public sealed class SpecialSkillStubAndBodyModule
{
    public string UnitPointerSource { get; set; } = string.Empty;
    public int EffectValueFlag { get; set; }
    public int StackFlag { get; set; }
    public int ItemEffectId { get; set; }
    public int PersonalEffectId { get; set; }
    public string CoreEffectEngineAddressHex { get; set; } = "0x004101D9";
    public string FunctionAssemblySource { get; set; } = string.Empty;
    public string RegisterStrategy { get; set; } = "pushad/popad wrapper; generated body must not rely on preserved scratch registers.";
}

public sealed class SpecialSkillParameterPatchPoint
{
    public string Kind { get; set; } = string.Empty;
    public int EffectId { get; set; }
    public string Encoding { get; set; } = "push-imm32";
    public uint InstructionAddress { get; set; }
    public uint ValueAddress { get; set; }
    public int ValueByteLength { get; set; } = 4;
    public string Note { get; set; } = string.Empty;

    public string InstructionAddressHex => InstructionAddress == 0 ? string.Empty : $"0x{InstructionAddress:X8}";
    public string ValueAddressHex => ValueAddress == 0 ? string.Empty : $"0x{ValueAddress:X8}";
}

public sealed class SpecialSkillPatchPreviewResult
{
    public bool CanApply { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
    public InlineSpecialSkillPatchDraft Draft { get; set; } = new();
    public InlineSpecialSkillPatch LogicalPatch { get; set; } = new();
    public EffectPackage Package { get; set; } = new();
    public AssemblyPatchPreviewResult AssemblyPreview { get; set; } = new();
    public EffectPatchPreviewResult PatchPreview { get; set; } = new();
}

public sealed class SpecialSkillPatchApplyResult
{
    public bool Applied { get; set; }
    public string Summary { get; set; } = string.Empty;
    public EffectPackageApplyResult PatchApplyResult { get; set; } = new();
}

public sealed class SpecialSkillParamRebindPreviewResult
{
    public bool CanApply { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
    public EffectPackage Package { get; set; } = new();
    public EffectPatchPreviewResult PatchPreview { get; set; } = new();
    public List<SpecialSkillParameterPatchPoint> ParameterPatchPoints { get; set; } = [];
}
