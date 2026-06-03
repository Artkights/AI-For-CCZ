namespace CCZModStudio.Models;

public sealed class WriteOperationReport
{
    public string SchemaVersion { get; set; } = "1.0";
    public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string OperationKind { get; set; } = string.Empty;
    public string SourceAction { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
    public string TargetRelativePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string TextReportPath { get; set; } = string.Empty;
    public string JsonReportPath { get; set; } = string.Empty;
    public string BeforeSha256 { get; set; } = string.Empty;
    public string AfterSha256 { get; set; } = string.Empty;
    public int ChangedBytes { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string SafetyNotes { get; set; } = string.Empty;
    public string FormatCheckSummary { get; set; } = string.Empty;
    public string RiskSummary { get; set; } = string.Empty;
    public List<WriteOperationChange> Changes { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WriteOperationChange
{
    public string Category { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public int? RowIndex { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string OffsetHex { get; set; } = string.Empty;
    public int? ByteLength { get; set; }
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string Annotation { get; set; } = string.Empty;
}
