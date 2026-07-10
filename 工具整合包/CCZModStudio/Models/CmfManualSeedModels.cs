namespace CCZModStudio.Models;

public sealed class CmfManualSeedDocument
{
    public string SchemaVersion { get; init; } = "1.0";
    public string SourceCmfRelativePath { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public string VersionScope { get; init; } = string.Empty;
    public string TargetFile { get; init; } = string.Empty;
    public string TargetSha256 { get; init; } = string.Empty;
    public string AddressKind { get; init; } = "UeFileOffset";
    public string TrustLevel { get; init; } = "ManualConfirmed";
    public string EvidenceDate { get; init; } = string.Empty;
    public string EvidenceSource { get; init; } = string.Empty;
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CmfManualSeedField> Fields { get; init; } = Array.Empty<CmfManualSeedField>();
    public IReadOnlyList<CmfManualSeedTable> Tables { get; init; } = Array.Empty<CmfManualSeedTable>();
}

public sealed class CmfManualSeedField
{
    public string FieldId { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string UiControl { get; init; } = string.Empty;
    public string UeOffsetHex { get; init; } = string.Empty;
    public int ByteLength { get; init; }
    public string DataType { get; init; } = string.Empty;
    public string ValueKind { get; init; } = string.Empty;
    public string DisplayFormat { get; init; } = string.Empty;
    public int? Shift { get; init; }
    public string MaskHex { get; init; } = string.Empty;
    public string FunctionType { get; init; } = string.Empty;
    public string DefaultValueRaw { get; init; } = string.Empty;
    public string CheckedBytesHex { get; init; } = string.Empty;
    public string UncheckedBytesHex { get; init; } = string.Empty;
    public bool LengthIsManualDefault { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed class CmfManualSeedTable
{
    public string TableId { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string BaseUeOffsetHex { get; init; } = string.Empty;
    public int EntryByteLength { get; init; }
    public int TextByteLength { get; init; }
    public int SlotStride { get; init; }
    public string ValueKind { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string FunctionType { get; init; } = string.Empty;
    public int ExpectedEntryCount { get; init; }
    public IReadOnlyList<CmfManualSeedBitFlag> BitFlags { get; init; } = Array.Empty<CmfManualSeedBitFlag>();
    public IReadOnlyList<CmfManualSeedTableEntry> Entries { get; init; } = Array.Empty<CmfManualSeedTableEntry>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed class CmfManualSeedTableEntry
{
    public int EntryId { get; init; }
    public string EntryIdHex { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string UeOffsetHex { get; init; } = string.Empty;
    public string DefaultValueRaw { get; init; } = string.Empty;
}

public sealed class CmfManualSeedBitFlag
{
    public string Hex { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

public sealed class CmfManualSeedValidationReport
{
    public string ReportKind { get; init; } = "CmfManualSeedValidation";
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public int SeedCount { get; init; }
    public int FieldCount { get; init; }
    public int TableCount { get; init; }
    public int ExpandedTableEntryCount { get; init; }
    public bool IsValid => Issues.All(issue => !issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));
    public IReadOnlyList<CmfManualSeedValidationIssue> Issues { get; init; } = Array.Empty<CmfManualSeedValidationIssue>();
}

public sealed class CmfManualSeedValidationIssue
{
    public string Severity { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
