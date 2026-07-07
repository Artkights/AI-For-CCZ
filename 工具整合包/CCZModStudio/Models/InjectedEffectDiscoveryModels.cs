namespace CCZModStudio.Models;

public static class InjectedEffectPatternKind
{
    public const string KnownPatch = "KnownPatch";
    public const string InlineCoreStub = "InlineCoreStub";
    public const string FourModuleDamageModifier = "FourModuleDamageModifier";
    public const string FourModuleLikeCandidate = "FourModuleLikeCandidate";
    public const string RawJumpCandidate = "RawJumpCandidate";
}

public static class InjectedEffectPatchCategory
{
    public const string SimpleFourModuleSpecialEffect = "SimpleFourModuleSpecialEffect";
    public const string MultiCheckSpecialEffect = "MultiCheckSpecialEffect";
    public const string ComplexMultiHookPatch = "ComplexMultiHookPatch";
    public const string FunctionExtensionPatch = "FunctionExtensionPatch";
    public const string KnownPatchSignatureOnly = "KnownPatchSignatureOnly";
    public const string InlineCoreStub = "InlineCoreStub";
    public const string UnknownCandidate = "UnknownCandidate";
}

public static class InjectedEffectParameterRole
{
    public const string Equipment = "Equipment";
    public const string Personal = "Personal";
    public const string EffectValue = "EffectValue";
    public const string Range = "Range";
    public const string BooleanOption = "BooleanOption";
    public const string MessageText = "MessageText";
    public const string Unknown = "Unknown";
    public const string UnknownCombined = "UnknownCombined";
}

public sealed class InjectedEffectDiscoveryReport
{
    public string TargetFilePath { get; set; } = string.Empty;
    public string TargetFileName { get; set; } = "Ekd5.exe";
    public string ExeSha256 { get; set; } = string.Empty;
    public long ExeSize { get; set; }
    public uint ImageBase { get; set; }
    public string ImageBaseHex => $"0x{ImageBase:X8}";
    public string EngineVersionHint { get; set; } = string.Empty;
    public bool IsKnownEngine { get; set; }
    public List<InjectedEffectCandidate> Candidates { get; set; } = [];
    public List<InjectedEffectHookCandidate> HookCandidates { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
}

public sealed class InjectedEffectCandidate
{
    public string AddressHex { get; set; } = string.Empty;
    public uint Address { get; set; }
    public string Type { get; set; } = string.Empty;
    public string PatternKind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? PersonalEffectId { get; set; }
    public int? EquipmentEffectId { get; set; }
    public int? EffectValueFlag { get; set; }
    public int? StackingFlag { get; set; }
    public string HookPoint { get; set; } = string.Empty;
    public string CodeCave { get; set; } = string.Empty;
    public uint? JumpOutAddress { get; set; }
    public uint? CodeCaveEntryAddress { get; set; }
    public uint? GuardStartAddress { get; set; }
    public uint? FeatureStartAddress { get; set; }
    public uint? ReturnAddress { get; set; }
    public uint? PersonalIdPatchAddress { get; set; }
    public uint? EquipmentIdPatchAddress { get; set; }
    public string ModuleSummary { get; set; } = string.Empty;
    public string UserReadableDiagnosis { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string PatchCategory { get; set; } = string.Empty;
    public string StructureDiagnosis { get; set; } = string.Empty;
    public List<InjectedEffectParameterSlot> ParameterSlots { get; set; } = [];
    public List<InjectedEffectCheckGroup> CheckGroups { get; set; } = [];
    public List<InjectedEffectModuleInfo> Modules { get; set; } = [];
}

public sealed class InjectedEffectParameterSlot
{
    public string Role { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public uint? Address { get; set; }
    public string AddressHex => Address.HasValue ? $"0x{Address.Value:X8}" : string.Empty;
    public int? Value { get; set; }
    public string ValueText => Value.HasValue ? $"0x{Value.Value:X2} / {Value.Value}" : string.Empty;
    public int ByteLength { get; set; }
    public string SourceComment { get; set; } = string.Empty;
    public string Editability { get; set; } = string.Empty;
    public string SafeRangeDescription { get; set; } = string.Empty;
}

public sealed class InjectedEffectCheckGroup
{
    public string GroupName { get; set; } = string.Empty;
    public uint? GuardStartAddress { get; set; }
    public uint? GuardCallAddress { get; set; }
    public uint? GuardFunctionAddress { get; set; }
    public InjectedEffectParameterSlot? EquipmentSlot { get; set; }
    public InjectedEffectParameterSlot? PersonalSlot { get; set; }
    public uint? FailureBranchAddress { get; set; }
    public uint? FeatureStartAddress { get; set; }
    public uint? ReturnAddress { get; set; }
    public string Diagnosis { get; set; } = string.Empty;
}

public sealed class InjectedEffectModuleInfo
{
    public string ModuleName { get; set; } = string.Empty;
    public uint? Address { get; set; }
    public string AddressHex => Address.HasValue ? $"0x{Address.Value:X8}" : string.Empty;
    public string Role { get; set; } = string.Empty;
    public string CurrentContent { get; set; } = string.Empty;
    public string Editable { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class InjectedEffectHookCandidate
{
    public string AddressHex { get; set; } = string.Empty;
    public uint Address { get; set; }
    public string TargetHex { get; set; } = string.Empty;
    public uint Target { get; set; }
    public string SectionName { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
}
