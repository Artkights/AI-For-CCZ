using System.Data;

namespace CCZModStudio.Models;

public sealed class GlobalSettingsDocument
{
    public required IReadOnlyList<GlobalNumericSetting> NumericSettings { get; init; }
    public required IReadOnlyList<GlobalNumericSettingDefinition> NumericDefinitions { get; init; }
    public required DataTable JobSeriesNames { get; init; }
    public required DataTable DetailedJobNames { get; init; }
    public required GlobalTitleSetting GameTitle { get; init; }
    public required IReadOnlyList<GlobalSettingEvidence> Evidence { get; init; }
    public IReadOnlyList<CmfFeatureCandidate> CmfCandidates { get; init; } = Array.Empty<CmfFeatureCandidate>();
}

public enum GlobalNumericValueKind
{
    Byte,
    UInt16LE,
    UInt32LE,
    BooleanRadio
}

public sealed class GlobalNumericSettingDefinition
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public string DefaultValueText { get; init; } = string.Empty;
    public string TargetFileName { get; init; } = string.Empty;
    public long FileOffset { get; init; }
    public long RuntimeAddress { get; init; }
    public int ByteLength { get; init; }
    public IReadOnlyList<GlobalNumericWriteTarget> WriteTargets { get; init; } = Array.Empty<GlobalNumericWriteTarget>();
    public GlobalNumericValueKind ValueKind { get; init; }
    public int MinValue { get; init; }
    public int MaxValue { get; init; }
    public bool CanEdit { get; init; }
    public string EvidenceStatus { get; init; } = "待验证";
    public string EvidenceSource { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string OracleCoverage { get; init; } = "NeedsUiOrDiffExtraction";
    public string CurrentValueText { get; init; } = "待验证";
}

public sealed class GlobalNumericSetting
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public string CurrentValueText { get; set; } = "待验证";
    public string SuggestedDefaultText { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Status { get; init; } = "待验证";
    public string Detail { get; init; } = string.Empty;
    public bool CanEdit { get; init; }
    public int MinValue { get; init; }
    public int MaxValue { get; init; }
    public string TargetFileName { get; init; } = string.Empty;
    public long Offset { get; init; }
    public long RuntimeAddress { get; init; }
    public int ByteLength { get; init; }
    public IReadOnlyList<GlobalNumericWriteTarget> WriteTargets { get; init; } = Array.Empty<GlobalNumericWriteTarget>();
    public GlobalNumericValueKind ValueKind { get; init; }
    public string OracleCoverage { get; init; } = "NeedsUiOrDiffExtraction";
}

public sealed class GlobalNumericWriteTarget
{
    public required string TargetFileName { get; init; }
    public long FileOffset { get; init; }
    public long RuntimeAddress { get; init; }
    public int ByteLength { get; init; }
    public int ValueMultiplier { get; init; } = 1;
    public int ValueDelta { get; init; }
    public string MultiplyBySettingKey { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
}

public sealed class GlobalNumericDiscoveryReport
{
    public string ReportKind { get; init; } = "GlobalNumericDiscovery";
    public string Status { get; init; } = "NeedsManualOfficialDiff";
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public string ProjectRoot { get; init; } = string.Empty;
    public string SourceGameRoot { get; init; } = string.Empty;
    public string EvidenceRoot { get; init; } = string.Empty;
    public string BeforeRoot { get; init; } = string.Empty;
    public string OfficialCaseRoot { get; init; } = string.Empty;
    public string OfficialToolRoot { get; init; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
    public IReadOnlyList<GlobalNumericDiscoveryField> Fields { get; init; } = Array.Empty<GlobalNumericDiscoveryField>();
    public IReadOnlyList<GlobalNumericDiscoveryFileDiff> FileDiffs { get; init; } = Array.Empty<GlobalNumericDiscoveryFileDiff>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ManualSteps { get; init; } = Array.Empty<string>();
    public string SafetyNote { get; init; } = "Discovery creates test copies and a report only; it does not modify the source project.";
}

public sealed class GlobalNumericDiscoveryField
{
    public required string Key { get; init; }
    public int UiControlIndex { get; init; }
    public required string DisplayName { get; init; }
    public string DefaultValueText { get; init; } = string.Empty;
    public string OldValueText { get; init; } = string.Empty;
    public string NewValueText { get; init; } = string.Empty;
    public string EvidenceStatus { get; init; } = string.Empty;
    public string EvidenceSource { get; init; } = string.Empty;
    public string TargetFileName { get; init; } = string.Empty;
    public long FileOffset { get; init; }
    public long RuntimeAddress { get; init; }
    public int ByteLength { get; init; }
    public IReadOnlyList<GlobalNumericWriteTarget> WriteTargets { get; init; } = Array.Empty<GlobalNumericWriteTarget>();
    public string ValueKind { get; init; } = string.Empty;
    public bool UniqueDiff { get; init; }
    public IReadOnlyList<GlobalNumericDiscoveryChange> Changes { get; init; } = Array.Empty<GlobalNumericDiscoveryChange>();
}

public sealed class GlobalNumericDiscoveryFileDiff
{
    public required string RelativePath { get; init; }
    public bool BeforeExists { get; init; }
    public bool AfterExists { get; init; }
    public long BeforeLength { get; init; }
    public long AfterLength { get; init; }
    public string BeforeSha256 { get; init; } = string.Empty;
    public string AfterSha256 { get; init; } = string.Empty;
    public int ChangedByteCount { get; init; }
    public IReadOnlyList<GlobalNumericDiscoveryChange> Changes { get; init; } = Array.Empty<GlobalNumericDiscoveryChange>();
}

public sealed class GlobalNumericDiscoveryChange
{
    public required string RelativePath { get; init; }
    public long FileOffset { get; init; }
    public long RuntimeAddress { get; init; }
    public int ByteLength { get; init; }
    public string OldBytesHex { get; init; } = string.Empty;
    public string NewBytesHex { get; init; } = string.Empty;
}

public sealed class GlobalNumericLowRiskExperimentReport
{
    public string ReportKind { get; init; } = "GlobalNumericLowRiskExperiment";
    public string Status { get; init; } = "NeedsManualOfficialDiff";
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public string ProjectRoot { get; init; } = string.Empty;
    public string SourceGameRoot { get; init; } = string.Empty;
    public string EvidenceRoot { get; init; } = string.Empty;
    public string BeforeRoot { get; init; } = string.Empty;
    public string NoopCaseRoot { get; init; } = string.Empty;
    public string NoopOfficialToolRoot { get; init; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
    public IReadOnlyList<GlobalNumericLowRiskCase> Cases { get; init; } = Array.Empty<GlobalNumericLowRiskCase>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ManualSteps { get; init; } = Array.Empty<string>();
    public string SafetyNote { get; init; } = "Creates test copies and official-tool copies only; it does not modify the source project or promote fields.";
}

public sealed class GlobalNumericLowRiskCase
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public int OldValue { get; init; }
    public int NewValue { get; init; }
    public string CaseDirectoryName { get; init; } = string.Empty;
    public string OfficialToolDirectoryName { get; init; } = string.Empty;
    public string CaseRoot { get; init; } = string.Empty;
    public string OfficialToolRoot { get; init; } = string.Empty;
    public string Instruction { get; init; } = string.Empty;
}

public sealed class GlobalNumericLowRiskCompareReport
{
    public string ReportKind { get; init; } = "GlobalNumericLowRiskCaseDiff";
    public string Status { get; init; } = "NeedsManualOfficialDiff";
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public string ProjectRoot { get; init; } = string.Empty;
    public string SourceGameRoot { get; init; } = string.Empty;
    public string EvidenceRoot { get; init; } = string.Empty;
    public string NoopCaseRoot { get; init; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
    public IReadOnlyList<GlobalNumericLowRiskCaseDiff> Cases { get; init; } = Array.Empty<GlobalNumericLowRiskCaseDiff>();
    public IReadOnlyList<string> SharedOffsetKeys { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string SafetyNote { get; init; } = "Compare report is evidence only; candidate offsets are not promoted until code metadata and write round-trip are added.";
}

public sealed class GlobalNumericLowRiskCaseDiff
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public int OldValue { get; init; }
    public int NewValue { get; init; }
    public string CaseRoot { get; init; } = string.Empty;
    public bool CaseExists { get; init; }
    public bool HasChanges { get; init; }
    public bool Ekd5Only { get; init; }
    public bool ByteIncrementShape { get; init; }
    public bool HasSharedOffsets { get; init; }
    public bool MinimalPromotableCandidate { get; init; }
    public string Conclusion { get; init; } = string.Empty;
    public IReadOnlyList<GlobalNumericLowRiskTargetCandidate> CandidateWriteTargets { get; init; } = Array.Empty<GlobalNumericLowRiskTargetCandidate>();
    public IReadOnlyList<GlobalNumericDiscoveryFileDiff> FileDiffs { get; init; } = Array.Empty<GlobalNumericDiscoveryFileDiff>();
}

public sealed class GlobalNumericLowRiskTargetCandidate
{
    public required string TargetFileName { get; init; }
    public long FileOffset { get; init; }
    public long RuntimeAddress { get; init; }
    public int ByteLength { get; init; }
    public string OldBytesHex { get; init; } = string.Empty;
    public string NewBytesHex { get; init; } = string.Empty;
    public int ValueDelta { get; init; }
    public bool ExpectedDeltaShape { get; init; }
    public string Purpose { get; init; } = string.Empty;
}

public sealed class GlobalNumericQueryReport
{
    public string ReportKind { get; init; } = "GlobalNumericQuery";
    public string Status { get; init; } = "StaticCandidatesOnly";
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public string ProjectRoot { get; init; } = string.Empty;
    public string SourceGameRoot { get; init; } = string.Empty;
    public string EvidenceRoot { get; init; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
    public IReadOnlyList<GlobalNumericQueryField> Fields { get; init; } = Array.Empty<GlobalNumericQueryField>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string SafetyNote { get; init; } = "Static candidate queries are evidence only; pending fields remain locked until official single-field diff and reread validation are complete.";
}

public sealed class GlobalNumericQueryField
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public string DefaultValueText { get; init; } = string.Empty;
    public string EvidenceStatus { get; init; } = string.Empty;
    public string EvidenceSource { get; init; } = string.Empty;
    public bool CanEdit { get; init; }
    public string TargetFileName { get; init; } = string.Empty;
    public long FileOffset { get; init; }
    public long RuntimeAddress { get; init; }
    public int ByteLength { get; init; }
    public IReadOnlyList<GlobalNumericWriteTarget> WriteTargets { get; init; } = Array.Empty<GlobalNumericWriteTarget>();
    public string ValueKind { get; init; } = string.Empty;
    public string OracleCoverage { get; init; } = string.Empty;
    public IReadOnlyList<int> ParsedDefaultValues { get; init; } = Array.Empty<int>();
    public IReadOnlyList<GlobalNumericQueryPattern> Patterns { get; init; } = Array.Empty<GlobalNumericQueryPattern>();
    public IReadOnlyList<GlobalNumericQueryCandidate> Candidates { get; init; } = Array.Empty<GlobalNumericQueryCandidate>();
    public int TotalCandidateCount { get; init; }
    public string QueryConclusion { get; init; } = string.Empty;
}

public sealed class GlobalNumericQueryPattern
{
    public required string PatternKind { get; init; }
    public string ValueKind { get; init; } = string.Empty;
    public IReadOnlyList<int> Values { get; init; } = Array.Empty<int>();
    public string BytesHex { get; init; } = string.Empty;
    public int ByteLength { get; init; }
    public int TotalHitCount { get; init; }
    public bool SearchSkipped { get; init; }
    public string Note { get; init; } = string.Empty;
}

public sealed class GlobalNumericQueryCandidate
{
    public required string RelativePath { get; init; }
    public long FileOffset { get; init; }
    public long RuntimeAddress { get; init; }
    public int ByteLength { get; init; }
    public string BytesHex { get; init; } = string.Empty;
    public string PatternKind { get; init; } = string.Empty;
    public string ContextBeforeHex { get; init; } = string.Empty;
    public string ContextAfterHex { get; init; } = string.Empty;
    public bool IsVerifiedWriteTarget { get; init; }
    public string VerifiedPurpose { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}

public sealed class GlobalTitleSetting
{
    public string Title { get; set; } = string.Empty;
    public int CapacityBytes { get; init; }
    public required string FileName { get; init; }
    public long Offset { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public sealed class GlobalSettingsSaveResult
{
    public int ChangedBytes { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> BackupPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ReportJsonPaths { get; init; } = Array.Empty<string>();
}

public sealed class GlobalSettingEvidence
{
    public required string Area { get; init; }
    public required string Item { get; init; }
    public string Target { get; init; } = string.Empty;
    public string OffsetText { get; init; } = string.Empty;
    public string LengthText { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}
