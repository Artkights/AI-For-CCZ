namespace CCZModStudio.Core;

/// <summary>
/// Version-locked runtime semantics shared by authoring, analysis, and GameDebug.
/// Addresses in this registry are valid only for the registered 6.5 executable profile.
/// </summary>
public static class EngineRuntimeSemanticRegistry
{
    public const string EngineVersion = "6.5";
    public const string Profile64ReferenceId = "6.4-reference-readonly";
    public const string Profile65CanonicalId = "6.5-canonical";

    public const uint TacticalUnitArrayAddress = 0x004A7B20;
    public const int TacticalUnitStride = 0x30;
    public const int TacticalUnitDataIdOffset = 0x00;
    public const int TacticalUnitDataIdWidth = 2;
    public const int TacticalUnitDataIdContainerWidth = 4;
    public const int TacticalUnitBattleSpriteIdOffset = 0x04;
    public const int TacticalUnitBattleSpriteIdWidth = 1;
    public const int TacticalUnitPackedDisplayStateOffset = 0x04;
    public const int TacticalUnitPackedDisplayStateWidth = 4;

    [Obsolete("Use TacticalUnitBattleSpriteIdOffset or TacticalUnitPackedDisplayStateOffset.")]
    public const int TacticalUnitDisplayIdOffset = 0x04;

    [Obsolete("The compatibility display id is the one-byte battle sprite id.")]
    public const int TacticalUnitDisplayIdWidth = TacticalUnitBattleSpriteIdWidth;
    public const int TacticalUnitSideOffset = 0x05;
    public const int TacticalUnitXOffset = 0x06;
    public const int TacticalUnitYOffset = 0x07;
    public const int TacticalUnitActionOffset = 0x0D;
    public const int TacticalUnitCurrentHpOffset = 0x10;
    public const int TacticalUnitCurrentMpOffset = 0x14;
    public const int TacticalUnitCurrentValueWidth = 4;
    public const int TacticalUnitAttributesOffset = 0x18;
    public const int TacticalUnitAttributesLength = 6;
    public const int EmptyTacticalUnitDataId = 0xFFFF;

    public const uint RuntimeCharacterPointerSlotAddress = 0x004CEA00;
    public const int RuntimeCharacterStride = 0x48;
    public const int RuntimeCharacterMaximumMpOffset = 0x1F;
    public const int RuntimeCharacterMaximumMpWidth = 2;

    public const uint StrategyRecordBaseAddress = 0x004A3E77;
    public const int StrategyRecordStride = 0x61;

    public const uint ItemRecordBaseAddress = 0x004A1140;
    public const int ItemRecordStride = 0x19;
    public const int ItemRecordMinimumId = 0;
    public const int ItemRecordMaximumId = 254;
    public const int ItemNameStorageWidth = 0x11;
    public const int ItemNameTextCapacity = 0x10;

    public const uint DetailedJobRecordBaseAddress = 0x004A2A27;
    public const int DetailedJobRecordStride = 0x23;
    public const int DetailedJobRecordMinimumId = 0;
    public const int DetailedJobRecordMaximumId = 79;

    public const uint JobFamilyTerrainRecordBaseAddress = 0x004A3517;
    public const int JobFamilyTerrainRecordStride = 0x3C;
    public const int JobFamilyTerrainRecordMinimumId = 0;
    public const int JobFamilyTerrainRecordMaximumId = 39;

    public const uint ConsumableCountArrayAddress = 0x00510C80;
    public const int ConsumableItemMinimumId = 150;
    public const int ConsumableItemMaximumId = 254;
    public const int ConsumableCountWidth = 1;

    public const uint PhysicalAttackContextAddress = 0x004927F0;
    public const uint StrategyContextAddress = 0x00497AF8;
    public const uint ItemContextAddress = 0x00497750;

    public const uint DataIdToRuntimeCharacterAddress = 0x004061E4;
    public const uint BattlefieldIdToTacticalUnitAddress = 0x004061F9;
    public const uint TacticalUnitToRuntimeCharacterAddress = 0x0040658F;
    public const uint DetailedJobIdToRecordAddress = 0x0041B782;
    public const uint JobFamilyIdToTerrainRecordAddress = 0x0041B7D4;
    public const uint ConsumableItemIdToCountAddress = 0x00414F65;
    public const uint ItemIdToRecordAddress = 0x004837C2;
    public const uint StrategyIdToRecordAddress = 0x00484002;

    public const uint PhysicalRecoveryHookAddress = 0x00418335;
    public const uint LegacyPhysicalRecoveryBodyAddress = 0x004528FC;
    public const uint LegacyPhysicalRecoveryBodyEndAddress = 0x004529A6;

    public static IReadOnlyDictionary<uint, RuntimeFunctionSemantic> Functions { get; } =
        new Dictionary<uint, RuntimeFunctionSemantic>
        {
            [DataIdToRuntimeCharacterAddress] = new(
                DataIdToRuntimeCharacterAddress, "data_id_to_runtime_character", "DataId", "RuntimeCharacter*"),
            [BattlefieldIdToTacticalUnitAddress] = new(
                BattlefieldIdToTacticalUnitAddress, "battlefield_id_to_tactical_unit", "BattlefieldUnitId", "TacticalUnit*"),
            [TacticalUnitToRuntimeCharacterAddress] = new(
                TacticalUnitToRuntimeCharacterAddress, "tactical_unit_to_runtime_character", "TacticalUnit*", "RuntimeCharacter*"),
            [DetailedJobIdToRecordAddress] = new(
                DetailedJobIdToRecordAddress, "detailed_job_id_to_record", "DetailedJobId", "DetailedJobRecord*"),
            [JobFamilyIdToTerrainRecordAddress] = new(
                JobFamilyIdToTerrainRecordAddress, "job_family_id_to_terrain_record", "JobFamilyId", "JobFamilyTerrainRecord*"),
            [ConsumableItemIdToCountAddress] = new(
                ConsumableItemIdToCountAddress, "consumable_item_id_to_count", "ItemId", "ByteCount"),
            [ItemIdToRecordAddress] = new(
                ItemIdToRecordAddress, "item_id_to_record", "ItemId", "ItemRecord*"),
            [StrategyIdToRecordAddress] = new(
                StrategyIdToRecordAddress, "strategy_id_to_record", "StrategyId", "StrategyRecord*")
        };

    public static IReadOnlyDictionary<string, RuntimeTableSemantic> Tables { get; } =
        new Dictionary<string, RuntimeTableSemantic>(StringComparer.OrdinalIgnoreCase)
        {
            ["items"] = new(
                "items", "道具运行时表", ItemRecordBaseAddress, ItemRecordStride,
                ItemRecordMinimumId, ItemRecordMaximumId, RuntimeSemanticEvidenceLevels.StaticVerified,
                [
                    Field("name", "名称存储区", 0x00, ItemNameStorageWidth),
                    Field("type", "类型", 0x11, 1),
                    Field("effect-id", "装备特效号", 0x12, 1),
                    Field("price-div-100", "价格（/100）", 0x13, 1),
                    Field("icon", "图标", 0x14, 1),
                    Field("initial-value", "初始能力", 0x15, 1),
                    Field("effect-value", "装备特效值", 0x16, 1),
                    Field("growth", "成长值", 0x17, 1),
                    Field("gallery", "宝物图鉴", 0x18, 1)
                ]),
            ["detailed-jobs"] = new(
                "detailed-jobs", "详细兵种运行时表", DetailedJobRecordBaseAddress, DetailedJobRecordStride,
                DetailedJobRecordMinimumId, DetailedJobRecordMaximumId, RuntimeSemanticEvidenceLevels.StaticVerified,
                [
                    Field("movement", "移动力", 0x00, 1),
                    Field("attack-range", "攻击范围", 0x01, 1),
                    Field("attack", "攻击成长", 0x02, 1),
                    Field("defense", "防御成长", 0x03, 1),
                    Field("spirit", "精神成长", 0x04, 1),
                    Field("agility", "爆发成长", 0x05, 1),
                    Field("morale", "士气成长", 0x06, 1),
                    Field("hp", "HP 成长", 0x07, 1),
                    Field("mp", "MP 成长", 0x08, 1),
                    Field("equipment-eligibility", "可装备类别矩阵", 0x09, 0x1A)
                ]),
            ["job-family-terrain"] = new(
                "job-family-terrain", "兵种系地形运行时表", JobFamilyTerrainRecordBaseAddress, JobFamilyTerrainRecordStride,
                JobFamilyTerrainRecordMinimumId, JobFamilyTerrainRecordMaximumId, RuntimeSemanticEvidenceLevels.StaticVerified,
                [
                    Field("terrain-adaptation", "地形适应", 0x00, 0x1E),
                    Field("movement-cost", "地形移动消耗", 0x1E, 0x1E)
                ]),
            ["consumable-counts"] = new(
                "consumable-counts", "消耗品数量", ConsumableCountArrayAddress, 1,
                ConsumableItemMinimumId, ConsumableItemMaximumId, RuntimeSemanticEvidenceLevels.StaticVerified,
                [Field("count", "当前数量", 0x00, ConsumableCountWidth)], RecordIndexBase: ConsumableItemMinimumId)
        };

    public static bool TryResolveTableAddress(uint address, out RuntimeTableAddressResolution resolution)
    {
        foreach (var table in Tables.Values)
        {
            if (table.TryResolve(address, out resolution)) return true;
        }

        resolution = new RuntimeTableAddressResolution();
        return false;
    }

    public static TacticalUnitSemanticSnapshot DecodeTacticalUnitRecord(ReadOnlySpan<byte> record)
    {
        if (record.Length < TacticalUnitStride)
            throw new ArgumentException($"战术单位记录至少需要 {TacticalUnitStride} 字节。", nameof(record));
        return new TacticalUnitSemanticSnapshot(
            BitConverter.ToUInt16(record.Slice(TacticalUnitDataIdOffset, TacticalUnitDataIdWidth)),
            BitConverter.ToUInt32(record.Slice(TacticalUnitDataIdOffset, TacticalUnitDataIdContainerWidth)),
            record[TacticalUnitBattleSpriteIdOffset],
            BitConverter.ToUInt32(record.Slice(TacticalUnitPackedDisplayStateOffset, TacticalUnitPackedDisplayStateWidth)),
            record[TacticalUnitSideOffset],
            record[TacticalUnitXOffset],
            record[TacticalUnitYOffset],
            record[TacticalUnitActionOffset],
            BitConverter.ToUInt32(record.Slice(TacticalUnitCurrentHpOffset, TacticalUnitCurrentValueWidth)),
            BitConverter.ToUInt32(record.Slice(TacticalUnitCurrentMpOffset, TacticalUnitCurrentValueWidth)));
    }

    private static RuntimeFieldSemantic Field(string id, string nameZh, int offset, int width)
        => new(id, nameZh, offset, width, RuntimeSemanticEvidenceLevels.StaticVerified, false);
}

public sealed record TacticalUnitSemanticSnapshot(
    ushort DataId,
    uint DataIdContainer,
    byte BattleSpriteId,
    uint PackedDisplayState,
    byte Side,
    byte X,
    byte Y,
    byte Action,
    uint CurrentHp,
    uint CurrentMp)
{
    public bool IsEmpty => DataId == EngineRuntimeSemanticRegistry.EmptyTacticalUnitDataId;
}

public sealed record RuntimeFunctionSemantic(uint Address, string Name, string InputType, string OutputType);

public static class RuntimeSemanticEvidenceLevels
{
    public const string ExternalReference = "ExternalReference";
    public const string StaticVerified = "StaticVerified";
    public const string DynamicVerified = "DynamicVerified";
}

public sealed record RuntimeFieldSemantic(
    string FieldId,
    string DisplayNameZh,
    int Offset,
    int Width,
    string EvidenceLevel,
    bool Writable);

public sealed record RuntimeTableSemantic(
    string TableId,
    string DisplayNameZh,
    uint BaseAddress,
    int RecordStride,
    int MinimumId,
    int MaximumId,
    string EvidenceLevel,
    IReadOnlyList<RuntimeFieldSemantic> Fields,
    int RecordIndexBase = 0)
{
    public uint AddressOf(int recordId, string? fieldId = null)
    {
        if (recordId < MinimumId || recordId > MaximumId)
            throw new ArgumentOutOfRangeException(nameof(recordId), $"{TableId} id must be {MinimumId}..{MaximumId}.");
        var field = string.IsNullOrWhiteSpace(fieldId)
            ? null
            : Fields.FirstOrDefault(item => item.FieldId.Equals(fieldId, StringComparison.OrdinalIgnoreCase))
              ?? throw new ArgumentException($"Unknown {TableId} field: {fieldId}", nameof(fieldId));
        return checked(BaseAddress + (uint)((recordId - RecordIndexBase) * RecordStride + (field?.Offset ?? 0)));
    }

    public bool TryResolve(uint address, out RuntimeTableAddressResolution resolution)
    {
        var length = checked((uint)((MaximumId - RecordIndexBase + 1) * RecordStride));
        if (address < BaseAddress || address >= BaseAddress + length)
        {
            resolution = new RuntimeTableAddressResolution();
            return false;
        }

        var relative = checked((int)(address - BaseAddress));
        var recordId = relative / RecordStride + RecordIndexBase;
        if (recordId < MinimumId || recordId > MaximumId)
        {
            resolution = new RuntimeTableAddressResolution();
            return false;
        }

        var offset = relative % RecordStride;
        var field = Fields.FirstOrDefault(item => offset >= item.Offset && offset < item.Offset + item.Width);
        resolution = new RuntimeTableAddressResolution
        {
            TableId = TableId,
            DisplayNameZh = DisplayNameZh,
            RecordId = recordId,
            RecordOffset = offset,
            FieldId = field?.FieldId ?? string.Empty,
            FieldNameZh = field?.DisplayNameZh ?? "保留/未知字段",
            FieldOffset = field?.Offset,
            FieldWidth = field?.Width,
            EvidenceLevel = EvidenceLevel
        };
        return true;
    }
}

public sealed class RuntimeTableAddressResolution
{
    public string TableId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public int RecordId { get; set; }
    public int RecordOffset { get; set; }
    public string FieldId { get; set; } = string.Empty;
    public string FieldNameZh { get; set; } = string.Empty;
    public int? FieldOffset { get; set; }
    public int? FieldWidth { get; set; }
    public string EvidenceLevel { get; set; } = string.Empty;
}
