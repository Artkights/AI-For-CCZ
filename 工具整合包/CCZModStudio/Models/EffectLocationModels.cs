namespace CCZModStudio.Models;

public static class EffectIdLocationKind
{
    public const string StubImmediate = "StubImmediate";
    public const string RegisterDefinitionImmediate = "RegisterDefinitionImmediate";
    public const string StackSlotDefinition = "StackSlotDefinition";
    public const string MemoryBackedSource = "MemoryBackedSource";
    public const string WrapperForwardedArgument = "WrapperForwardedArgument";
    public const string NativeTableField = "NativeTableField";
    public const string InjectedPatchParameter = "InjectedPatchParameter";
    public const string CompositeParameterBlock = "CompositeParameterBlock";
    public const string ManifestParameter = "ManifestParameter";
    public const string CatalogDefinition = "CatalogDefinition";
    public const string KnownSampleLocation = "KnownSampleLocation";
    public const string UnresolvedSource = "UnresolvedSource";
}

public static class EffectIdWriteCapability
{
    public const string DirectWritable = "DirectWritable";
    public const string AdapterRequired = "AdapterRequired";
    public const string TransactionWritable = "TransactionWritable";
    public const string ReadOnlyVerified = "ReadOnlyVerified";
    public const string DiagnosticOnly = "DiagnosticOnly";
}

public sealed class EffectIdLocationIndex
{
    public string AnalysisFingerprint { get; set; } = string.Empty;
    public string CacheState { get; set; } = string.Empty;
    public List<string> CompletedStages { get; set; } = [];
    public ExecutableProfileAuditResult ProfileAudit { get; set; } = new();
    public string TargetFilePath { get; set; } = string.Empty;
    public string ExeSha256 { get; set; } = string.Empty;
    public uint ImageBase { get; set; }
    public string WritableProfileId { get; set; } = string.Empty;
    public bool CurrentProfileCanWrite { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public List<EffectIdLocationRecord> Locations { get; set; } = [];
    public Dictionary<string, int> CountsByKind { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> CountsByWriteCapability { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ReportPaths { get; set; } = [];
    public List<string> WarningsZh { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class EffectIdLocationRecord
{
    public string LocationId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string KindZh { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string ChannelZh { get; set; } = string.Empty;
    public int? EffectId { get; set; }
    public string EffectIdHex => EffectId.HasValue ? EffectId.Value.ToString("X2") : string.Empty;
    public string EffectNameZh { get; set; } = string.Empty;
    public string ParameterRole { get; set; } = string.Empty;
    public string ParameterRoleZh { get; set; } = string.Empty;
    public string TargetFile { get; set; } = string.Empty;
    public uint? VirtualAddress { get; set; }
    public uint? Rva { get; set; }
    public long? FileOffset { get; set; }
    public uint? InstructionAddress { get; set; }
    public int? OperandOffset { get; set; }
    public int ByteLength { get; set; }
    public bool IsSigned { get; set; }
    public string Encoding { get; set; } = string.Empty;
    public string CurrentBytesHex { get; set; } = string.Empty;
    public int? CurrentValue { get; set; }
    public string OwnerInstanceId { get; set; } = string.Empty;
    public string OwnerNameZh { get; set; } = string.Empty;
    public List<uint> WrapperChain { get; set; } = [];
    public string EvidenceExeSha256 { get; set; } = string.Empty;
    public string EvidenceSource { get; set; } = string.Empty;
    public string EvidenceLevel { get; set; } = string.Empty;
    public string WriteCapability { get; set; } = EffectIdWriteCapability.DiagnosticOnly;
    public string WriteCapabilityZh { get; set; } = string.Empty;
    public string BlockingReasonZh { get; set; } = string.Empty;
    public string ExpectedOldBytesHex { get; set; } = string.Empty;
    public List<string> DefinitionChain { get; set; } = [];
    public int? InPlaceMinimum { get; set; }
    public int? InPlaceMaximum { get; set; }
    public int? ExtendedMinimum { get; set; }
    public int? ExtendedMaximum { get; set; }
    public string AdapterStrategy { get; set; } = string.Empty;
    public string AdapterManifestId { get; set; } = string.Empty;
}

public sealed class EffectIdUpdateRequest
{
    public string LocationId { get; set; } = string.Empty;
    public int NewValue { get; set; }
}

public sealed class EffectIdUpdatePreview
{
    public bool CanApply { get; set; }
    public string SummaryZh { get; set; } = string.Empty;
    public List<string> WarningsZh { get; set; } = [];
    public EffectIdLocationRecord? Location { get; set; }
    public EffectPackage Package { get; set; } = new();
    public EffectPatchPreviewResult PatchPreview { get; set; } = new();
}
