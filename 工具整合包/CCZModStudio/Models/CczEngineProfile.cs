using CCZModStudio.Core;

namespace CCZModStudio.Models;

public sealed class CczEngineProfile
{
    public string EngineKey { get; set; } = "unknown";
    public string DisplayName { get; set; } = "未知引擎";
    public string VersionHint { get; set; } = "unknown";
    public string TableVersionPrefix { get; set; } = "6.5";
    public bool IsKnown { get; set; }
    public bool AllowCrossVersionTableFallback { get; set; } = true;
    public string DetectionSource { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public long? ExeSize { get; set; }
    public string ExeSha256 { get; set; } = string.Empty;
    public string VersionResourceText { get; set; } = string.Empty;
    public int? VersionResourceLowWord { get; set; }
    public string PathHint { get; set; } = string.Empty;
    public long? DataSize { get; set; }
    public long? ImsgSize { get; set; }
    public long? StarSize { get; set; }
    public long? ItemSize { get; set; }
    public CczEngineTableHints TableHints { get; set; } = new();
    public Qinger66TableStatusSummary? TableStatusSummary { get; set; }
    public Qinger66Diagnostics? Qinger66Diagnostics { get; set; }
    public CczLegacyRuntimeMemoryLayout? LegacyRuntimeLayout { get; set; }
    public List<CczEngineDetectionEvidence> DetectionEvidence { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class CczEngineDetectionEvidence
{
    public string Kind { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string VersionHint { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class CczEngineTableHints
{
    public string PersonTable { get; set; } = "6.5-0 人物";
    public string BiographyTable { get; set; } = "6.5-0-1 人物列传";
    public string CriticalQuoteTable { get; set; } = "6.5-0-2 暴击台词";
    public string RetreatQuoteTable { get; set; } = "6.5-0-3 撤退台词";
    public string ItemLowTable { get; set; } = "6.5-1 物品（0-103）";
    public string ItemHighTable { get; set; } = "6.5-2 物品（104-255）";
    public string JobTable { get; set; } = "6.5-3 兵种系";
    public string JobSeriesTable { get; set; } = "6.5-3 兵种系";
    public string DetailedJobTable { get; set; } = "6.5-4 详细兵种";
    public string ItemEffectNameLowTable { get; set; } = "6.5-1-2 装备特效名称（1A-57）";
    public string ItemEffectNameHighTable { get; set; } = "6.5-1-3 装备特效名称（58-7F）";
    public string JobEffectNameTable { get; set; } = "6.5-7 兵种特效";
    public string JobEffectDescriptionTable { get; set; } = "6.5-7-1 兵种特效说明";
    public string JobEffectAssignmentTable { get; set; } = "6.5-7-2 兵种特效分配";
    public string PersonalEffectTable { get; set; } = "6.5-7-3 人物专属、套装专属";
    public string CampaignNameTable { get; set; } = "6.5-8 战役名称";
    public string ShopDataTable { get; set; } = "6.5-8-1 商店数据";
}

public sealed class CczLegacyRuntimeMemoryLayout
{
    public string Source { get; set; } = "old-wrench-source";
    public uint CharacterPointerAddress { get; set; } = 0x4CEA00;
    public int CharacterRecordSize { get; set; } = 0x48;
    public int CharacterNameOffset { get; set; } = 0x08;
    public int CharacterNameLength { get; set; } = 0x08;
    public int CharacterMaxHpOffset { get; set; }
    public int CharacterMaxHpByteWidth { get; set; }
    public int CharacterMaxMpOffset { get; set; }
    public int CharacterMaxMpByteWidth { get; set; }
    public uint RImageTableAddress { get; set; } = 0x50F800;
    public uint SImageTableAddress { get; set; } = 0x501000;
    public uint FaceTableAddress { get; set; } = 0x50F000;
    public uint JobNameTableAddress { get; set; } = 0x5000D0;
    public int JobNameRecordSize { get; set; } = 0x09;
    public int JobNameLength { get; set; } = 0x09;
    public uint ItemNameTableAddress { get; set; } = 0x4A1140;
    public int ItemNameRecordSize { get; set; } = EngineRuntimeSemanticRegistry.ItemRecordStride;
    public int ItemNameLength { get; set; } = EngineRuntimeSemanticRegistry.ItemNameStorageWidth;
    public int ItemNameTextCapacity { get; set; } = EngineRuntimeSemanticRegistry.ItemNameTextCapacity;
    public int ItemTypeOffset { get; set; } = 0x11;
    public int ItemEffectIdOffset { get; set; } = 0x12;
    public int ItemPriceOffset { get; set; } = 0x13;
    public int ItemIconOffset { get; set; } = 0x14;
    public int ItemInitialValueOffset { get; set; } = 0x15;
    public int ItemEffectValueOffset { get; set; } = 0x16;
    public int ItemGrowthOffset { get; set; } = 0x17;
    public int ItemGalleryOffset { get; set; } = 0x18;
    public uint DetailedJobTableAddress { get; set; }
    public int DetailedJobRecordSize { get; set; }
    public int DetailedJobCount { get; set; }
    public uint JobFamilyTerrainTableAddress { get; set; }
    public int JobFamilyTerrainRecordSize { get; set; }
    public int JobFamilyTerrainCount { get; set; }
    public uint ConsumableCountArrayAddress { get; set; }
    public int ConsumableMinimumItemId { get; set; }
    public int ConsumableMaximumItemId { get; set; }
    public uint ConsumableNameTableAddress { get; set; }
    public int ConsumableCount { get; set; }
    public string ConsumableEncoding { get; set; } = "gbk";
    public uint TalentNameTableAddress { get; set; }
    public int TalentCount { get; set; }
    public int TalentRecordSize { get; set; } = 16;
    public int TalentNameLength { get; set; } = 15;
    public uint KillNameTableAddress { get; set; }
    public int KillCount { get; set; }
    public int KillRecordSize { get; set; } = 16;
    public int KillNameLength { get; set; } = 11;
    public uint WarArrayAddress { get; set; }
    public int WarRecordSize { get; set; }
    public int AllyCapacity { get; set; }
    public int FriendlyCapacity { get; set; }
    public int EnemyCapacity { get; set; }
    public int ItemCapacity { get; set; }
    public int UnitDataIdOffset { get; set; } = 0x04;
    public int UnitDataIdByteWidth { get; set; } = 1;
    public int UnitDataIdContainerByteWidth { get; set; } = 4;
    public int UnitBattleSpriteIdOffset { get; set; } = 0x04;
    public int UnitBattleSpriteIdByteWidth { get; set; } = 1;
    public int UnitPackedDisplayStateOffset { get; set; } = 0x04;
    public int UnitPackedDisplayStateByteWidth { get; set; } = 4;

    [Obsolete("Use UnitBattleSpriteIdOffset or UnitPackedDisplayStateOffset.")]
    public int UnitDisplayIdOffset { get; set; } = 0x04;

    [Obsolete("The compatibility display id is the one-byte battle sprite id.")]
    public int UnitDisplayIdByteWidth { get; set; } = 1;
    public int UnitSideOffset { get; set; } = 0x05;
    public int UnitXOffset { get; set; } = 0x06;
    public int UnitYOffset { get; set; } = 0x07;
    public int UnitActionOffset { get; set; } = 0x0D;
    public int UnitCurrentHpOffset { get; set; } = 0x10;
    public int UnitCurrentHpByteWidth { get; set; } = 2;
    public int UnitCurrentMpOffset { get; set; } = 0x14;
    public int UnitCurrentMpByteWidth { get; set; } = 2;
    public int UnitAttributesOffset { get; set; } = 0x18;
    public int UnitAttributesLength { get; set; } = 6;
    public uint ReviveFunctionAddress { get; set; }
    public uint BishaTableAddress { get; set; }
    public uint TalentTableAddress { get; set; }
    public uint ExclusiveSetTableAddress { get; set; }
    public uint MeritTableAddress { get; set; }
    public uint SpecialSkillCatalogOffset { get; set; }
    public int SpecialSkillRecordSize { get; set; } = 16;
    public int SpecialSkillNameLength { get; set; } = 14;
    public bool IsRuntimeProbeOnly { get; set; } = true;
    public string Variant { get; set; } = string.Empty;
    public string Applicability { get; set; } = "旧扳手运行时地址，仅作定位线索，不作为当前 6.5 文件写入规则。";
}
