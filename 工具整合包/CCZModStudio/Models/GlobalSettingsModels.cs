using System.Data;

namespace CCZModStudio.Models;

public sealed class GlobalSettingsDocument
{
    public required IReadOnlyList<GlobalNumericSetting> NumericSettings { get; init; }
    public required DataTable JobSeriesNames { get; init; }
    public required DataTable DetailedJobNames { get; init; }
    public required GlobalTitleSetting GameTitle { get; init; }
    public required IReadOnlyList<GlobalSettingEvidence> Evidence { get; init; }
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
