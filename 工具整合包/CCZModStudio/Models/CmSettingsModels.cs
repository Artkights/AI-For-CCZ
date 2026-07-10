namespace CCZModStudio.Models;

public enum CmSettingValueKind
{
    DecimalByte,
    HexByte,
    HexByteBare,
    ToggleByte,
    ShiftedTwoBitDecimal,
    FixedGbkText
}

public sealed class CmSettingsDocument
{
    public string TargetFile { get; init; } = "Ekd5.exe";
    public IReadOnlyList<CmSettingGroup> Groups { get; init; } = Array.Empty<CmSettingGroup>();
    public IReadOnlyList<CmTerrainStrategyRow> TerrainStrategyRows { get; init; } = Array.Empty<CmTerrainStrategyRow>();
}

public sealed class CmSettingGroup
{
    public string GroupKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<CmSettingItem> Items { get; init; } = Array.Empty<CmSettingItem>();
}

public sealed class CmSettingItem
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public CmSettingValueKind ValueKind { get; init; }
    public int CurrentValue { get; init; }
    public string CurrentValueText { get; init; } = string.Empty;
    public string CurrentTextValue { get; init; } = string.Empty;
    public bool CurrentBoolValue { get; init; }
    public bool CanEdit { get; init; } = true;
    public string ValidationMessage { get; init; } = string.Empty;
}

public sealed class CmTerrainStrategyRow
{
    public int TerrainId { get; init; }
    public string TerrainIdHex { get; init; } = string.Empty;
    public string TerrainName { get; init; } = string.Empty;
    public bool Fire { get; init; }
    public bool Water { get; init; }
    public bool Wind { get; init; }
    public bool Earth { get; init; }
    public int CurrentValue { get; init; }
}

public sealed class CmSettingsUpdate
{
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, CmTerrainStrategyUpdate> TerrainStrategy { get; init; } = new();
}

public sealed class CmTerrainStrategyUpdate
{
    public bool? Fire { get; init; }
    public bool? Water { get; init; }
    public bool? Wind { get; init; }
    public bool? Earth { get; init; }
}

public sealed class CmSettingsPreviewChange
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public string OldValue { get; init; } = string.Empty;
    public string NewValue { get; init; } = string.Empty;
    public string OldBytesHex { get; init; } = string.Empty;
    public string NewBytesHex { get; init; } = string.Empty;
    public string TargetFile { get; init; } = "Ekd5.exe";
    public string UeOffsetHex { get; init; } = string.Empty;
    public int ByteLength { get; init; }
}

public sealed class CmSettingsSaveResult
{
    public int ChangedFieldCount { get; init; }
    public int ChangedBytes { get; init; }
    public string BackupPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<CmSettingsPreviewChange> Changes { get; init; } = Array.Empty<CmSettingsPreviewChange>();
}
