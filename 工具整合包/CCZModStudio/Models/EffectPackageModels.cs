using System.Text.Json.Serialization;

namespace CCZModStudio.Models;

public sealed class EffectPackage
{
    public string SchemaVersion { get; set; } = "1.0";
    public string PackageId { get; set; } = string.Empty;
    public string Domain { get; set; } = "item";
    public int EffectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? EffectValue { get; set; }
    public List<EffectPackageBinding> Bindings { get; set; } = [];
    public List<EffectPatchSegment> PatchSegments { get; set; } = [];
    public List<string> SourceLinks { get; set; } = [];
    public string SourcePrompt { get; set; } = string.Empty;
    public string BackupNote { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EffectPackageBinding
{
    public string Kind { get; set; } = string.Empty;
    public int? RowId { get; set; }
    public int? ItemId { get; set; }
    public int? PersonId { get; set; }
    public int? PersonId2 { get; set; }
    public int? PersonId3 { get; set; }
    public int? JobId { get; set; }
    public int? ItemId2 { get; set; }
    public int? ItemId3 { get; set; }
    public int? ItemId4 { get; set; }
    public int? EffectValue { get; set; }
    public Dictionary<string, int> Values { get; set; } = new(StringComparer.Ordinal);
    public string Note { get; set; } = string.Empty;
}

public sealed class EffectPatchSegment
{
    public string TargetFile { get; set; } = "Ekd5.exe";
    public string AddressKind { get; set; } = "OdVirtualAddress";
    public uint Address { get; set; }
    public string AddressHex { get; set; } = string.Empty;
    public string BytesHex { get; set; } = string.Empty;
    public string ExpectedOldBytesHex { get; set; } = string.Empty;
    public string CodeCaveId { get; set; } = string.Empty;
    public string HookPoint { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}

public sealed class EffectCatalogEntry
{
    public string Domain { get; set; } = string.Empty;
    public int EffectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? EffectValue { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, object?> Details { get; set; } = new(StringComparer.Ordinal);
}

public sealed class EffectPackagePreviewResult
{
    public string Mode { get; set; } = "import";
    public string Domain { get; set; } = string.Empty;
    public int EffectId { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public bool CanApply { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
    public List<EffectPackageChangePreview> Changes { get; set; } = [];
}

public sealed class EffectPackageChangePreview
{
    public string Category { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public int? RowId { get; set; }
    public string Field { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public bool Changed { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class EffectPackageApplyResult
{
    public string Mode { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public int EffectId { get; set; }
    public string ManifestId { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public List<string> BackupPaths { get; set; } = [];
    public List<string> ReportPaths { get; set; } = [];
    public int ChangedBytes { get; set; }
    public int ChangeCount { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public sealed class EffectManifest
{
    public string SchemaVersion { get; set; } = "1.0";
    public string ManifestId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string ProjectRoot { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public int EffectId { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string SourcePrompt { get; set; } = string.Empty;
    public string BackupNote { get; set; } = string.Empty;
    public EffectPackage Package { get; set; } = new();
    public List<EffectManifestChange> Changes { get; set; } = [];
    public List<EffectManifestBackup> Backups { get; set; } = [];
    public List<string> BackupPaths { get; set; } = [];
    public List<string> ReportPaths { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EffectManifestBackup
{
    public string TargetPath { get; set; } = string.Empty;
    public string TargetRelativePath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool TargetExisted { get; set; } = true;
}

public sealed class EffectManifestChange
{
    public string Category { get; set; } = string.Empty;
    public string TargetRelativePath { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public int? RowId { get; set; }
    public string Field { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string OffsetHex { get; set; } = string.Empty;
    public int? ByteLength { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class EffectTemplate
{
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = "patch";
    public string Capability { get; set; } = string.Empty;
    public string SafetyLevel { get; set; } = "draft_only";
    public List<string> RequiredParameters { get; set; } = [];
    public string Description { get; set; } = string.Empty;
}

public sealed class EffectPatchPreviewResult
{
    public bool CanApply { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
    public List<EffectPackageChangePreview> Segments { get; set; } = [];
}

public sealed class EffectPackageToolRequest
{
    [JsonPropertyName("package")]
    public EffectPackage Package { get; set; } = new();
}
