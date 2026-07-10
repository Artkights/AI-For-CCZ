namespace CCZModStudio.Models;

public sealed class CmfDesignerSnapshot
{
    public string SchemaVersion { get; init; } = "1.0";
    public string SourcePath { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public long SourceLength { get; init; }
    public DateTime ExtractedAtUtc { get; init; } = DateTime.UtcNow;
    public string CheatMakerExePath { get; init; } = string.Empty;
    public string CheatMakerVersion { get; init; } = string.Empty;
    public string ExtractionMode { get; init; } = "StaticOnly";
    public string ReportDirectory { get; init; } = string.Empty;
    public IReadOnlyList<CmfDesignerPage> Pages { get; init; } = Array.Empty<CmfDesignerPage>();
    public IReadOnlyList<CmfDesignerModule> Modules { get; init; } = Array.Empty<CmfDesignerModule>();
    public IReadOnlyList<CmfDesignerControl> Controls { get; init; } = Array.Empty<CmfDesignerControl>();
    public IReadOnlyList<CmfDesignerBinding> Bindings { get; init; } = Array.Empty<CmfDesignerBinding>();
    public CmfDesignerRawNode? RawUiTree { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class CmfDesignerPage
{
    public string PageId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
    public CmfUiRect Bounds { get; init; } = CmfUiRect.Empty;
}

public sealed class CmfDesignerModule
{
    public string ModuleId { get; init; } = string.Empty;
    public string PageId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public CmfUiRect Bounds { get; init; } = CmfUiRect.Empty;
    public IReadOnlyList<CmfModuleNote> Notes { get; init; } = Array.Empty<CmfModuleNote>();
    public IReadOnlyList<string> ControlIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BindingIds { get; init; } = Array.Empty<string>();
}

public sealed class CmfDesignerControl
{
    public string ControlId { get; init; } = string.Empty;
    public string PageId { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
    public string ControlType { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public CmfUiRect Bounds { get; init; } = CmfUiRect.Empty;
    public string Font { get; init; } = string.Empty;
    public string ForeColor { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
}

public sealed class CmfDesignerBinding
{
    public string BindingId { get; init; } = string.Empty;
    public string PageId { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
    public string ControlId { get; init; } = string.Empty;
    public string ControlName { get; init; } = string.Empty;
    public string ControlType { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string TargetFile { get; init; } = string.Empty;
    public string AddressKind { get; init; } = "UeFileOffset";
    public string UeOffsetHex { get; init; } = string.Empty;
    public long? UeOffset { get; init; }
    public string OdVirtualAddressHex { get; init; } = string.Empty;
    public long? OdVirtualAddress { get; init; }
    public int ByteLength { get; init; }
    public string DataType { get; init; } = string.Empty;
    public string FunctionType { get; init; } = string.Empty;
    public string DefaultValueRaw { get; init; } = string.Empty;
    public string DefaultValueParsed { get; init; } = string.Empty;
    public string DataListRaw { get; init; } = string.Empty;
    public string Script { get; init; } = string.Empty;
    public string ValidationStatus { get; init; } = "ExtractedFromDesigner";
    public IReadOnlyDictionary<string, string> SourceProperties { get; init; } = new Dictionary<string, string>();
}

public sealed class CmfDesignerFieldListItem
{
    public string SourceCmfRelativePath { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public string ExtractionMode { get; init; } = string.Empty;
    public string TrustLevel { get; init; } = string.Empty;
    public string PageName { get; init; } = string.Empty;
    public string ModuleTitle { get; init; } = string.Empty;
    public string BindingId { get; init; } = string.Empty;
    public string ControlName { get; init; } = string.Empty;
    public string ControlType { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string TargetFile { get; init; } = string.Empty;
    public string AddressKind { get; init; } = string.Empty;
    public string UeOffsetHex { get; init; } = string.Empty;
    public long? UeOffset { get; init; }
    public int ByteLength { get; init; }
    public string DataType { get; init; } = string.Empty;
    public string FunctionType { get; init; } = string.Empty;
    public string DefaultValueRaw { get; init; } = string.Empty;
    public string ValidationStatus { get; init; } = string.Empty;
    public string DataListPreview { get; init; } = string.Empty;
}

public sealed class CmfModuleNote
{
    public string Text { get; init; } = string.Empty;
    public CmfUiRect Bounds { get; init; } = CmfUiRect.Empty;
    public string Color { get; init; } = string.Empty;
    public string SourceControlId { get; init; } = string.Empty;
}

public sealed class CmfDesignerRawNode
{
    public string Handle { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public CmfUiRect Bounds { get; init; } = CmfUiRect.Empty;
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CmfDesignerRawNode> Children { get; init; } = Array.Empty<CmfDesignerRawNode>();
}

public sealed class CmfDesignerExtractionOptions
{
    public string Mode { get; init; } = "StaticOnly";
    public int TimeoutMs { get; init; } = 15000;
    public bool KeepProcessOpen { get; init; }
    public bool UseTempCopy { get; init; } = true;
    public string FixtureSnapshotPath { get; init; } = string.Empty;
    public string CheatMakerExePath { get; init; } = string.Empty;
}

public sealed class CmfDesignerExtractionResult
{
    public required CmfDesignerSnapshot Snapshot { get; init; }
    public string ReportDirectory { get; init; } = string.Empty;
    public string SnapshotJsonPath { get; init; } = string.Empty;
    public string FieldsCsvPath { get; init; } = string.Empty;
    public string ModulesMarkdownPath { get; init; } = string.Empty;
    public string AddressesMarkdownPath { get; init; } = string.Empty;
    public string RawUiTreeJsonPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class CmfDesignerSnapshotDiffReport
{
    public string ReportKind { get; init; } = "CmfDesignerSnapshotDiff";
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public string LeftRelativePath { get; init; } = string.Empty;
    public string RightRelativePath { get; init; } = string.Empty;
    public string LeftSha256 { get; init; } = string.Empty;
    public string RightSha256 { get; init; } = string.Empty;
    public string ReportDirectory { get; set; } = string.Empty;
    public string JsonReportPath { get; set; } = string.Empty;
    public string MarkdownReportPath { get; set; } = string.Empty;
    public IReadOnlyList<CmfDesignerSnapshotDiffItem> PageDiffs { get; init; } = Array.Empty<CmfDesignerSnapshotDiffItem>();
    public IReadOnlyList<CmfDesignerSnapshotDiffItem> ModuleDiffs { get; init; } = Array.Empty<CmfDesignerSnapshotDiffItem>();
    public IReadOnlyList<CmfDesignerSnapshotDiffItem> BindingDiffs { get; init; } = Array.Empty<CmfDesignerSnapshotDiffItem>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class CmfDesignerSnapshotDiffItem
{
    public string DiffKind { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string LeftValue { get; init; } = string.Empty;
    public string RightValue { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class CmfDesignerWriteVerificationOptions
{
    public string SnapshotPath { get; init; } = string.Empty;
    public IReadOnlyList<string> BindingIds { get; init; } = Array.Empty<string>();
    public int MaxFields { get; init; } = 500;
    public bool IncludeNeedsManualReview { get; init; }
}

public sealed class CmfDesignerWriteVerificationReport
{
    public string ReportKind { get; init; } = "CmfDesignerWriteVerification";
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public string SourceCmfRelativePath { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public string SourceGameRoot { get; init; } = string.Empty;
    public string TestCopyRoot { get; init; } = string.Empty;
    public string ReportDirectory { get; set; } = string.Empty;
    public string JsonReportPath { get; set; } = string.Empty;
    public int TotalFields { get; init; }
    public int WriteVerifiedCount { get; init; }
    public IReadOnlyList<CmfDesignerFieldWriteVerification> Fields { get; init; } = Array.Empty<CmfDesignerFieldWriteVerification>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class CmfDesignerFieldWriteVerification
{
    public string BindingId { get; init; } = string.Empty;
    public string PageName { get; init; } = string.Empty;
    public string ModuleTitle { get; init; } = string.Empty;
    public string ControlName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string TargetFile { get; init; } = string.Empty;
    public string AddressKind { get; init; } = string.Empty;
    public string UeOffsetHex { get; init; } = string.Empty;
    public string FileOffsetHex { get; init; } = string.Empty;
    public long? FileOffset { get; init; }
    public int ByteLength { get; init; }
    public string DataType { get; init; } = string.Empty;
    public string OriginalBytesHex { get; init; } = string.Empty;
    public string NewBytesHex { get; init; } = string.Empty;
    public string CandidateSource { get; init; } = string.Empty;
    public string FinalStatus { get; init; } = string.Empty;
    public bool CanPromoteToWrite { get; init; }
    public string PatchReportJsonPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Stages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public readonly record struct CmfUiRect(int Left, int Top, int Width, int Height)
{
    public static CmfUiRect Empty { get; } = new(0, 0, 0, 0);
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool IsEmpty => Width == 0 && Height == 0;

    public bool Contains(CmfUiRect other)
        => !IsEmpty &&
           !other.IsEmpty &&
           other.Left >= Left &&
           other.Top >= Top &&
           other.Right <= Right &&
           other.Bottom <= Bottom;
}
