namespace CCZModStudio.Models;

public enum CmfTrustLevel
{
    StaticSegmentOnly,
    ExtractedFromCheatMakerExport,
    ExtractedFromUiAutomation,
    ManualConfirmed
}

public enum CmfAddressSemantic
{
    Unknown,
    ExeImageAddress,
    FileOffset,
    RuntimeStaticMemory,
    DynamicPointer,
    ScriptVariable,
    ResourceFile
}

public sealed class CmfToolProject
{
    public string SourcePath { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Length { get; init; }
    public string FormatSignature { get; init; } = string.Empty;
    public string FormatVersion { get; init; } = string.Empty;
    public bool IsCheatMakerCmf { get; init; }
    public bool AuthoritativeToolSource { get; init; } = true;
    public string ExtractionMode { get; init; } = "StaticSegmentAnalysis";
    public string ConversionPolicy { get; init; } = "CMF is treated as a high-trust old modifier project. Writable rules still require extracted field metadata, version matching, address classification, and reread validation.";
    public IReadOnlyList<CmfSegmentAnalysis> Segments { get; init; } = Array.Empty<CmfSegmentAnalysis>();
    public IReadOnlyList<CmfPageDefinition> Pages { get; init; } = Array.Empty<CmfPageDefinition>();
    public IReadOnlyList<CmfControlDefinition> Controls { get; init; } = Array.Empty<CmfControlDefinition>();
    public IReadOnlyList<CmfDataBinding> DataBindings { get; init; } = Array.Empty<CmfDataBinding>();
    public IReadOnlyList<CmfAddressEntry> AddressEntries { get; init; } = Array.Empty<CmfAddressEntry>();
    public IReadOnlyList<CmfExportFieldRecord> ExportFields { get; init; } = Array.Empty<CmfExportFieldRecord>();
    public IReadOnlyList<CmfFeatureCandidate> FeatureCandidates { get; init; } = Array.Empty<CmfFeatureCandidate>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class CmfSegmentAnalysis
{
    public int Index { get; init; }
    public long ByteOffset { get; init; }
    public int ByteLength { get; init; }
    public int CharLength { get; init; }
    public double PrintableAsciiRatio { get; init; }
    public double CjkRatio { get; init; }
    public double ByteEntropy { get; init; }
    public IReadOnlyList<string> KeywordHits { get; init; } = Array.Empty<string>();
    public string SuspectedKind { get; init; } = string.Empty;
    public string Preview { get; init; } = string.Empty;
}

public sealed class CmfPageDefinition
{
    public string PageId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int? SegmentIndex { get; init; }
    public CmfTrustLevel TrustLevel { get; init; } = CmfTrustLevel.StaticSegmentOnly;
    public string SourceNote { get; init; } = string.Empty;
}

public sealed class CmfControlDefinition
{
    public string ControlId { get; init; } = string.Empty;
    public string PageId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ControlKind { get; init; } = string.Empty;
    public CmfTrustLevel TrustLevel { get; init; } = CmfTrustLevel.StaticSegmentOnly;
    public string SourceNote { get; init; } = string.Empty;
}

public sealed class CmfDataBinding
{
    public string BindingId { get; init; } = string.Empty;
    public string PageId { get; init; } = string.Empty;
    public string ControlId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TargetFile { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public int ByteLength { get; init; }
    public CmfAddressSemantic AddressSemantic { get; init; } = CmfAddressSemantic.Unknown;
    public long? Address { get; init; }
    public string AddressText { get; init; } = string.Empty;
    public CmfTrustLevel TrustLevel { get; init; } = CmfTrustLevel.StaticSegmentOnly;
    public string ConversionStatus { get; init; } = "ReadOnlyCandidate";
    public string SourceNote { get; init; } = string.Empty;
}

public sealed class CmfAddressEntry
{
    public string EntryId { get; init; } = string.Empty;
    public string SourceText { get; init; } = string.Empty;
    public string TargetFile { get; init; } = string.Empty;
    public CmfAddressSemantic Semantic { get; init; } = CmfAddressSemantic.Unknown;
    public long? Address { get; init; }
    public string AddressText { get; init; } = string.Empty;
    public int? ByteLength { get; init; }
    public string ValidationStatus { get; init; } = "Unclassified";
}

public sealed class CmfExportFieldRecord
{
    public string FieldId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string PageName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string TargetFile { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public int ByteLength { get; init; }
    public CmfAddressSemantic AddressSemantic { get; init; } = CmfAddressSemantic.Unknown;
    public long? Address { get; init; }
    public string AddressText { get; init; } = string.Empty;
    public string VersionScope { get; init; } = string.Empty;
    public string SourceLine { get; init; } = string.Empty;
}

public sealed class CmfFeatureCandidate
{
    public string FeatureId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string VersionScope { get; init; } = string.Empty;
    public string SourceCmfRelativePath { get; init; } = string.Empty;
    public string SourcePageId { get; init; } = string.Empty;
    public string TargetSubsystem { get; init; } = string.Empty;
    public CmfTrustLevel TrustLevel { get; init; } = CmfTrustLevel.StaticSegmentOnly;
    public string ConversionStatus { get; init; } = "ReadOnlyCandidate";
    public string WritePolicy { get; init; } = "Requires extracted address/type metadata plus reread validation before write.";
    public IReadOnlyList<string> EvidenceNotes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RelatedBindings { get; init; } = Array.Empty<string>();
}

public sealed class CmfPromotionDraft
{
    public string FeatureId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TargetSubsystem { get; init; } = string.Empty;
    public string RuleKind { get; init; } = string.Empty;
    public bool CanWriteNow { get; init; }
    public string RequiredValidation { get; init; } = string.Empty;
    public IReadOnlyList<string> BlockingReasons { get; init; } = Array.Empty<string>();
    public CmfFeatureCandidate? SourceFeature { get; init; }
}
