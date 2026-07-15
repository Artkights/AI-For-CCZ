using CCZModStudio.Core;

namespace CCZModStudio.Models;

public static class OriginalInstructionPolicies
{
    public const string AutoRelocate = "AutoRelocate";
    public const string HookReplacesOriginal = "HookReplacesOriginal";
    public const string PaddingOnly = "PaddingOnly";
    public const string ChainExistingJumpTarget = "ChainExistingJumpTarget";
}

public static class OriginalInstructionPlacements
{
    public const string BeforeBody = "BeforeBody";
    public const string AfterBody = "AfterBody";
}

public sealed class HookContract
{
    public string ContractId { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public string ExeSha256 { get; set; } = string.Empty;
    public string TriggerPhase { get; set; } = string.Empty;
    public uint HookAddress { get; set; }
    public string HookAddressHex => HookAddress == 0 ? string.Empty : $"0x{HookAddress:X8}";
    public int MinimumOverwriteLength { get; set; } = 5;
    public string ExpectedOldBytesHex { get; set; } = string.Empty;
    public string UnitPointerSource { get; set; } = string.Empty;
    public string OriginalInstructionPolicy { get; set; } = OriginalInstructionPolicies.AutoRelocate;
    public string OriginalInstructionPlacement { get; set; } = OriginalInstructionPlacements.AfterBody;
    public bool PreserveFlags { get; set; } = true;
    public int ExpectedStackDelta { get; set; }
    public string ConflictGroup { get; set; } = string.Empty;
    public uint ExistingJumpTarget { get; set; }
    public List<string> AllowedTemplateIds { get; set; } = [];
    public List<string> RequiredSymbols { get; set; } = [];
    public List<string> DynamicValidationPlan { get; set; } = [];
    public bool AllowPreview { get; set; }
    public string SafetyNote { get; set; } = string.Empty;
}

public sealed class HookSafetyAnalysisResult
{
    public bool IsSafe { get; set; }
    public string Summary { get; set; } = string.Empty;
    public HookContract? Contract { get; set; }
    public uint HookAddress { get; set; }
    public int RequiredOverwriteLength { get; set; }
    public string CurrentBytesHex { get; set; } = string.Empty;
    public uint ReturnAddress { get; set; }
    public List<X86InstructionInfo> OriginalInstructions { get; set; } = [];
    public byte[] RelocatedOriginalBytes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
