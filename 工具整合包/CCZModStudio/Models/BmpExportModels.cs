namespace CCZModStudio.Models;

public enum BmpExportKind
{
    ItemIcon,
    StrategyIcon,
    RImage,
    SImage,
    Face
}

public sealed class BmpExportRequest
{
    public BmpExportKind Kind { get; init; }
    public string OutputRoot { get; init; } = string.Empty;
    public bool OverwriteExisting { get; init; }
    public int FactionSlot { get; init; } = 1;
    public IReadOnlyList<BmpExportTarget> Targets { get; init; } = Array.Empty<BmpExportTarget>();
}

public sealed class BmpExportTarget
{
    public int RowId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public int FieldValue { get; init; }
    public int? JobId { get; init; }
}

public sealed class BmpExportResult
{
    public BmpExportRequest Request { get; init; } = new();
    public IReadOnlyList<BmpExportedFile> Files { get; init; } = Array.Empty<BmpExportedFile>();
    public IReadOnlyList<BmpExportSkippedItem> SkippedItems { get; init; } = Array.Empty<BmpExportSkippedItem>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string ReportJsonPath { get; init; } = string.Empty;
}

public sealed class BmpExportedFile
{
    public BmpExportKind Kind { get; init; }
    public int FieldValue { get; init; }
    public int RowId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public int? ImageNumber { get; init; }
    public int? ResourceId { get; init; }
    public int? LanguageId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class BmpExportSkippedItem
{
    public BmpExportKind Kind { get; init; }
    public int FieldValue { get; init; }
    public int RowId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
}
