namespace CCZModStudio.Models;

public enum ExclusiveSetDesignKind
{
    Personal = 0,
    Set = 1
}

public sealed class ExclusiveSetScenarioEntry
{
    public string EntryId { get; init; } = string.Empty;
    public ExclusiveSetDesignKind Kind { get; init; }
    public string KindText => Kind == ExclusiveSetDesignKind.Set ? "套装" : "专属";
    public string RelativePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int CommandIndex { get; init; }
    public int CommandOrdinal { get; init; }
    public int FileOffset { get; init; }
    public string OffsetHex => "0x" + FileOffset.ToString("X6", System.Globalization.CultureInfo.InvariantCulture);
    public string SourceText { get; init; } = string.Empty;
    public string SourceTextHash { get; init; } = string.Empty;
    public string Remarks { get; init; } = string.Empty;
    public int Position { get; init; }
    public int EffectId { get; init; }
    public int PersonId { get; init; } = 255;
    public int WeaponId { get; init; } = 255;
    public int ArmorId { get; init; } = 255;
    public int AccessoryId { get; init; } = 255;
    public int EffectValue { get; init; }
    public string EffectDisplay { get; init; } = string.Empty;
    public string EquipmentDisplay { get; init; } = string.Empty;
    public string PersonDisplay { get; init; } = string.Empty;
    public string SourceDisplay { get; init; } = string.Empty;
}

public sealed class ExclusiveSetScenarioMalformedEntry
{
    public string RelativePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int CommandIndex { get; init; }
    public int CommandOrdinal { get; init; }
    public int FileOffset { get; init; }
    public string OffsetHex => "0x" + FileOffset.ToString("X6", System.Globalization.CultureInfo.InvariantCulture);
    public string SourceText { get; init; } = string.Empty;
    public string SourceTextHash { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string SourceDisplay { get; init; } = string.Empty;
}

public sealed class ExclusiveSetScenarioReadResult
{
    public IReadOnlyList<ExclusiveSetScenarioEntry> Entries { get; init; } = Array.Empty<ExclusiveSetScenarioEntry>();
    public IReadOnlyList<ExclusiveSetScenarioMalformedEntry> MalformedEntries { get; init; } = Array.Empty<ExclusiveSetScenarioMalformedEntry>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public int ScannedFileCount { get; init; }
    public int TotalCommandCount { get; init; }
}

public sealed class ExclusiveSetScenarioUpdate
{
    public string EntryId { get; init; } = string.Empty;
    public ExclusiveSetDesignKind Kind { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int CommandIndex { get; init; }
    public int CommandOrdinal { get; init; }
    public int FileOffset { get; init; }
    public string OriginalTextHash { get; init; } = string.Empty;
    public int Position { get; init; }
    public int EffectId { get; init; }
    public int PersonId { get; init; } = 255;
    public int WeaponId { get; init; } = 255;
    public int ArmorId { get; init; } = 255;
    public int AccessoryId { get; init; } = 255;
    public int EffectValue { get; init; }
    public string Remarks { get; init; } = string.Empty;
}

public sealed class ExclusiveSetScenarioSaveResult
{
    public IReadOnlyList<LegacyScenarioWriteResult> Writes { get; init; } = Array.Empty<LegacyScenarioWriteResult>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public int ChangedEntryCount { get; init; }
}
