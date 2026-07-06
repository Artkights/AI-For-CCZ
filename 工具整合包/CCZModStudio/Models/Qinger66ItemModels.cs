namespace CCZModStudio.Models;

public sealed class Qinger66ItemAuditSummary
{
    public bool Applies { get; init; }
    public int ItemRowCount { get; init; }
    public int HiddenTailRowCount { get; init; }
    public int NameControlCharacterRowCount { get; init; }
    public int MinIconField { get; init; }
    public int MaxIconField { get; init; }
    public int ItemE5EntryCount { get; init; }
    public int MaxRequiredImageNumber { get; init; }
    public bool ItemIconRangeCovered { get; init; }
    public IReadOnlyDictionary<string, int> TableStatusCounts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> EffectResolutionSourceCounts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> EffectConfidenceCounts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class Qinger66ItemAuditRow
{
    public int ItemId { get; init; }
    public string TableName { get; init; } = string.Empty;
    public int RowIndex { get; init; }
    public string RawBytesHex { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string HiddenTailBytesHex { get; init; } = string.Empty;
    public int TypeId { get; init; }
    public int RawEffectId { get; init; }
    public int? EffectiveEffectId { get; init; }
    public int IconField { get; init; }
    public int SmallImageNumber { get; init; }
    public int LargeImageNumber { get; init; }
    public string TableStatus { get; init; } = string.Empty;
    public string WriteRisk { get; init; } = string.Empty;
    public string EffectSource { get; init; } = string.Empty;
    public string EffectConfidence { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class ItemIconFieldMapping
{
    public string Kind { get; init; } = string.Empty;
    public int FieldValue { get; init; }
    public string ResourceRelativePath { get; init; } = string.Empty;
    public string ResourcePath { get; init; } = string.Empty;
    public int EntryCount { get; init; }
    public int? SmallImageNumber { get; init; }
    public int LargeImageNumber { get; init; }
    public bool Is66E5Resource { get; init; }
    public bool InRange { get; init; }
    public string MappingRule { get; init; } = string.Empty;
    public string PreviewPolicy { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class ItemEffectResolutionResult
{
    public int RawEffectId { get; init; }
    public int? EffectiveEffectId { get; init; }
    public int? CategoryMarker { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public bool Is66Candidate { get; init; }
    public bool IsCategoryMarker { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class Ccz66ItemEffectNameSlot
{
    public int SlotIndex { get; init; }
    public long Offset { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsWritableNameSlot { get; init; }
}

public sealed class Ccz66ItemEffectBinding
{
    public int EffectId { get; init; }
    public string CanonicalName { get; init; } = string.Empty;
    public int? SlotIndex { get; init; }
    public string Name
    {
        get => CanonicalName;
        init => CanonicalName = value;
    }
}

public sealed class Ccz66ItemEffectNameWriteResult
{
    public string FilePath { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public int SlotIndex { get; init; }
    public long Offset { get; init; }
    public string OldName { get; init; } = string.Empty;
    public string NewName { get; init; } = string.Empty;
    public int ChangedBytes { get; init; }
    public string BeforeSha256 { get; init; } = string.Empty;
    public string AfterSha256 { get; init; } = string.Empty;
}
