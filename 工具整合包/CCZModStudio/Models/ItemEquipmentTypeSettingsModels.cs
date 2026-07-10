namespace CCZModStudio.Models;

public enum ItemEquipmentPermissionState
{
    Unchecked = 0,
    Checked = 1,
    Indeterminate = 2
}

public sealed class ItemEquipmentTypeSettingsDocument
{
    public string TargetFile { get; init; } = "Ekd5.exe";
    public IReadOnlyList<ItemEquipmentTypeRow> Rows { get; init; } = Array.Empty<ItemEquipmentTypeRow>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public sealed class ItemEquipmentTypeRow
{
    public int RowIndex { get; init; }
    public int NameEntryId { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool HasDisplayToggle { get; init; }
    public bool IsVisible { get; init; }
    public int? DisplayIndex { get; init; }
    public int? NormalDisplayValue { get; init; }
    public string EquipmentSummary { get; init; } = string.Empty;
    public IReadOnlyList<ItemEquipmentTypeJobPermission> JobPermissions { get; init; } = Array.Empty<ItemEquipmentTypeJobPermission>();
}

public sealed class ItemEquipmentTypeJobPermission
{
    public int RowIndex { get; init; }
    public int JobId { get; init; }
    public int SeriesId { get; init; }
    public string SeriesName { get; init; } = string.Empty;
    public string JobName { get; init; } = string.Empty;
    public ItemEquipmentPermissionState State { get; init; }
}

public sealed class ItemEquipmentTypeSettingsUpdate
{
    public Dictionary<int, string> Names { get; init; } = new();
    public Dictionary<int, bool> Visibility { get; init; } = new();
    public Dictionary<int, Dictionary<int, bool>> JobPermissions { get; init; } = new();
}

public sealed class ItemEquipmentTypePreviewChange
{
    public string Area { get; init; } = string.Empty;
    public int RowIndex { get; init; }
    public int? JobId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string OldValue { get; init; } = string.Empty;
    public string NewValue { get; init; } = string.Empty;
    public string OldBytesHex { get; init; } = string.Empty;
    public string NewBytesHex { get; init; } = string.Empty;
    public string TargetFile { get; init; } = string.Empty;
    public string OffsetHex { get; init; } = string.Empty;
    public int ByteLength { get; init; }
}

public sealed class ItemEquipmentTypeSettingsSaveResult
{
    public int ChangedFieldCount { get; init; }
    public int ChangedBytes { get; init; }
    public string ExeBackupPath { get; init; } = string.Empty;
    public string ExeReportJsonPath { get; init; } = string.Empty;
    public IReadOnlyList<TableSaveResult> TableSaves { get; init; } = Array.Empty<TableSaveResult>();
    public IReadOnlyList<ItemEquipmentTypePreviewChange> Changes { get; init; } = Array.Empty<ItemEquipmentTypePreviewChange>();
    public string Summary { get; init; } = string.Empty;
}
