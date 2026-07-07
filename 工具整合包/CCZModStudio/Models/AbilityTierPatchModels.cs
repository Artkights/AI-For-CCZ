namespace CCZModStudio.Models;

public sealed class AbilityTierScanReport
{
    public string ProjectRoot { get; init; } = string.Empty;
    public string GameRoot { get; init; } = string.Empty;
    public string ExePath { get; init; } = string.Empty;
    public string ExeSha256 { get; init; } = string.Empty;
    public long ImageBase { get; init; }
    public string ImageBaseHex => "0x" + ImageBase.ToString("X8");
    public string Status { get; set; } = string.Empty;
    public bool CanPatchMergeProfiles { get; set; }
    public int EngineTierCapacity { get; set; }
    public int EffectiveTierCount { get; set; }
    public string PatchModeRecommendation { get; set; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
    public List<AbilityTierThresholdRule> ThresholdRules { get; init; } = [];
    public List<AbilityTierReturnRule> ReturnRules { get; init; } = [];
    public List<AbilityTierPatchPoint> CallSites { get; init; } = [];
    public AbilityTierPatchPoint? ClampSite { get; set; }
    public AbilityTierDisplayPointer? DisplayPointer { get; set; }
    public List<string> DisplayLabels { get; init; } = [];
    public List<AbilityTierCaveCandidate> CaveCandidates { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed class AbilityTierProfile
{
    public string ProfileName { get; init; } = string.Empty;
    public int TierCount { get; init; }
    public string DisplayMode { get; init; } = "Letter";
    public List<string> Labels { get; init; } = [];
    public string PatchMode { get; init; } = "MergeOriginalBranches";
}

public sealed class AbilityTierPatchPreview
{
    public string ProjectRoot { get; init; } = string.Empty;
    public string GameRoot { get; init; } = string.Empty;
    public string ExePath { get; init; } = string.Empty;
    public string ExeSha256 { get; init; } = string.Empty;
    public AbilityTierProfile RequestedProfile { get; init; } = new();
    public bool CanWrite { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
    public List<AbilityTierByteChange> Changes { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed class AbilityTierPatchWriteResult
{
    public string ProjectRoot { get; init; } = string.Empty;
    public string GameRoot { get; init; } = string.Empty;
    public string ExePath { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public string BeforeSha256 { get; init; } = string.Empty;
    public string AfterSha256 { get; init; } = string.Empty;
    public int ChangedBytes { get; init; }
    public AbilityTierProfile RequestedProfile { get; init; } = new();
    public AbilityTierScanReport ReadBack { get; init; } = new();
    public List<AbilityTierByteChange> Changes { get; init; } = [];
}

public sealed class AbilityTierThresholdRule
{
    public string Label { get; init; } = string.Empty;
    public int Tier { get; init; }
    public long CompareInstructionVa { get; init; }
    public long ThresholdVa { get; init; }
    public long FileOffset { get; init; }
    public byte Threshold { get; init; }
    public string ThresholdHex => Threshold.ToString("X2");
}

public sealed class AbilityTierReturnRule
{
    public string Label { get; init; } = string.Empty;
    public int Tier { get; init; }
    public long MovInstructionVa { get; init; }
    public long ReturnValueVa { get; init; }
    public long FileOffset { get; init; }
    public byte ReturnValue { get; init; }
    public string ReturnValueHex => ReturnValue.ToString("X2");
}

public sealed class AbilityTierPatchPoint
{
    public string Purpose { get; init; } = string.Empty;
    public long Va { get; init; }
    public long FileOffset { get; init; }
    public int ByteLength { get; init; }
    public string BytesHex { get; init; } = string.Empty;
    public long? TargetVa { get; init; }
}

public sealed class AbilityTierDisplayPointer
{
    public long InstructionVa { get; init; }
    public long FileOffset { get; init; }
    public long TableVa { get; init; }
    public long TableFileOffset { get; init; }
    public string InstructionBytesHex { get; init; } = string.Empty;
    public List<AbilityTierDisplayLabelPointer> LabelPointers { get; init; } = [];
}

public sealed class AbilityTierDisplayLabelPointer
{
    public int Tier { get; init; }
    public long PointerVa { get; init; }
    public long PointerFileOffset { get; init; }
    public long LabelVa { get; init; }
    public long LabelFileOffset { get; init; }
    public string Label { get; init; } = string.Empty;
}

public sealed class AbilityTierCaveCandidate
{
    public string Purpose { get; init; } = string.Empty;
    public long Va { get; init; }
    public long FileOffset { get; init; }
    public int Length { get; init; }
    public bool IsAllNop { get; init; }
}

public sealed class AbilityTierByteChange
{
    public string Category { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public long Va { get; init; }
    public long FileOffset { get; init; }
    public string OldBytesHex { get; init; } = string.Empty;
    public string NewBytesHex { get; init; } = string.Empty;
    public int ByteLength { get; init; }
}
