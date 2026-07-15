namespace CCZModStudio.Models;

public sealed class ExeSectionInfo
{
    public string Name { get; init; } = string.Empty;
    public uint VirtualAddress { get; init; }
    public uint VirtualSize { get; init; }
    public uint RawPointer { get; init; }
    public uint RawSize { get; init; }
    public uint Characteristics { get; init; }
    public bool IsExecutable { get; init; }

    public string VirtualAddressHex => $"0x{VirtualAddress:X8}";
    public string RawPointerHex => $"0x{RawPointer:X}";
    public string CharacteristicsHex => $"0x{Characteristics:X8}";
}

public sealed class ExeCodeCaveCandidate
{
    public string CaveId { get; init; } = string.Empty;
    public string SectionName { get; init; } = string.Empty;
    public string FillKind { get; init; } = string.Empty;
    public uint StartVirtualAddress { get; init; }
    public uint EndVirtualAddress { get; init; }
    public long FileOffset { get; init; }
    public int Length { get; init; }
    public string RiskLevel { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string FillBytesSummary { get; init; } = string.Empty;
    public Dictionary<string, int> FillByteCounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsRecommended { get; init; }

    public string StartVirtualAddressHex => $"0x{StartVirtualAddress:X8}";
    public string EndVirtualAddressHex => $"0x{EndVirtualAddress:X8}";
    public string FileOffsetHex => $"0x{FileOffset:X}";
}

public sealed class BlockedCodeCaveRange
{
    public string CaveId { get; init; } = string.Empty;
    public uint StartVirtualAddress { get; init; }
    public uint EndVirtualAddress { get; init; }
    public string Status { get; init; } = "blocked";
    public string Reason { get; init; } = string.Empty;

    public string StartVirtualAddressHex => $"0x{StartVirtualAddress:X8}";
    public string EndVirtualAddressHex => $"0x{EndVirtualAddress:X8}";
}

public sealed class ExeCodeCaveScanResult
{
    public string TargetFilePath { get; init; } = string.Empty;
    public string TargetFileName { get; init; } = string.Empty;
    public string ExeSha256 { get; init; } = string.Empty;
    public long ExeSize { get; init; }
    public uint ImageBase { get; init; }
    public string EngineVersionHint { get; init; } = string.Empty;
    public bool IsKnownEngine { get; init; }
    public int MinimumLength { get; init; }
    public bool IncludeZeroFill { get; init; }
    public bool IncludeMixedFill { get; init; }
    public List<ExeSectionInfo> Sections { get; init; } = [];
    public List<ExeCodeCaveCandidate> Candidates { get; init; } = [];
    public List<BlockedCodeCaveRange> BlockedRanges { get; init; } = [];
    public List<string> Warnings { get; init; } = [];

    public string ImageBaseHex => $"0x{ImageBase:X8}";
}

public sealed class CodeCaveAllocationRequest
{
    public int RequiredBytes { get; set; }
    public int ReserveBytes { get; set; } = 8;
    public string AllocatorPolicy { get; set; } = "smallest-fit";
    public bool AllowZeroFillCave { get; set; }
    public bool AllowMixedFillCave { get; set; }
    public bool AllowRelayAllocation { get; set; }
    public List<AllocatedCodeCaveRange> ExistingAllocations { get; set; } = [];
}

public sealed class AllocatedCodeCaveRange
{
    public string CaveId { get; init; } = string.Empty;
    public uint StartVirtualAddress { get; init; }
    public uint EndVirtualAddress { get; init; }
    public int Length { get; init; }
    public string Reason { get; init; } = string.Empty;

    public string StartVirtualAddressHex => $"0x{StartVirtualAddress:X8}";
    public string EndVirtualAddressHex => $"0x{EndVirtualAddress:X8}";
}

public sealed class FreeCodeCaveRange
{
    public string CaveId { get; init; } = string.Empty;
    public string SourceCaveId { get; init; } = string.Empty;
    public string SectionName { get; init; } = string.Empty;
    public string FillKind { get; init; } = string.Empty;
    public uint StartVirtualAddress { get; init; }
    public uint EndVirtualAddress { get; init; }
    public long FileOffset { get; init; }
    public int Length { get; init; }
    public string RiskLevel { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;

    public string StartVirtualAddressHex => $"0x{StartVirtualAddress:X8}";
    public string EndVirtualAddressHex => $"0x{EndVirtualAddress:X8}";
    public string FileOffsetHex => $"0x{FileOffset:X}";
}

public sealed class CodeCaveAllocationResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public ExeCodeCaveCandidate? Candidate { get; init; }
    public FreeCodeCaveRange? FreeRange { get; init; }
    public AllocatedCodeCaveRange? Allocation { get; init; }
    public List<ExeCodeCaveCandidate> ConsideredCandidates { get; init; } = [];
    public List<FreeCodeCaveRange> ConsideredFreeRanges { get; init; } = [];
}

public sealed class EnginePatchProfile
{
    public string EngineVersion { get; init; } = string.Empty;
    public string ExeSha256 { get; init; } = string.Empty;
    public bool IsKnown { get; init; }
    public Dictionary<string, string> HookPoints { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> PublicFunctions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RuntimeAddresses { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SpecialSkillHookSpec> SpecialSkillHookSpecs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<BlockedCodeCaveRange> BlockedRanges { get; init; } = [];
    public List<AllocatedCodeCaveRange> ReservedRanges { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed class AssemblyPatchDraft
{
    public string Prompt { get; set; } = string.Empty;
    public string TargetFile { get; set; } = "Ekd5.exe";
    public string EngineVersion { get; set; } = string.Empty;
    public int EffectId { get; set; }
    public string HookPoint { get; set; } = string.Empty;
    public uint HookAddress { get; set; }
    public string HookAddressHex { get; set; } = string.Empty;
    public int OverwriteLength { get; set; } = 5;
    public string ExpectedOldBytesHex { get; set; } = string.Empty;
    public uint ReturnAddress { get; set; }
    public string ReturnAddressHex { get; set; } = string.Empty;
    public string AssemblySource { get; set; } = string.Empty;
    public string HookContractId { get; set; } = string.Empty;
    public string OriginalInstructionPolicy { get; set; } = string.Empty;
    public string OriginalInstructionPlacement { get; set; } = OriginalInstructionPlacements.AfterBody;
    public bool PreserveFlags { get; set; }
    public int ExpectedStackDelta { get; set; }
    public List<string> RequiredSymbols { get; set; } = [];
    public int RequiredCodeCaveBytes { get; set; }
    public string RegisterStrategy { get; set; } = "Use eax/ecx/edx only; preserve ebx/esi/edi/ebp.";
    public List<string> Dependencies { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<string> DynamicValidationPlan { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AssemblyPatchPreviewResult
{
    public bool CanApply { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
    public AssemblyPatchDraft Draft { get; set; } = new();
    public EffectPackage Package { get; set; } = new();
    public CodeCaveAllocationResult Allocation { get; set; } = new();
    public byte[] CodeCaveBytes { get; set; } = [];
    public byte[] HookBytes { get; set; } = [];
    public string DisassemblyPreview { get; set; } = string.Empty;
    public EffectPatchPreviewResult PatchPreview { get; set; } = new();
    public HookSafetyAnalysisResult HookSafety { get; set; } = new();
}

public sealed class AssemblyPatchApplyResult
{
    public bool Applied { get; set; }
    public string Summary { get; set; } = string.Empty;
    public AssemblyPatchPreviewResult Preview { get; set; } = new();
    public EffectPackageApplyResult PatchApplyResult { get; set; } = new();
}
