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
    public long? DataSize { get; set; }
    public long? ImsgSize { get; set; }
    public long? StarSize { get; set; }
    public long? ItemSize { get; set; }
    public CczEngineTableHints TableHints { get; set; } = new();
    public CczLegacyRuntimeMemoryLayout? LegacyRuntimeLayout { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class CczEngineTableHints
{
    public string PersonTable { get; set; } = "6.5-0 人物";
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
    public uint WarArrayAddress { get; set; }
    public int WarRecordSize { get; set; }
    public int AllyCapacity { get; set; }
    public int FriendlyCapacity { get; set; }
    public int EnemyCapacity { get; set; }
    public int ItemCapacity { get; set; }
    public uint ReviveFunctionAddress { get; set; }
    public uint BishaTableAddress { get; set; }
    public uint TalentTableAddress { get; set; }
    public uint ExclusiveSetTableAddress { get; set; }
    public uint MeritTableAddress { get; set; }
    public string Applicability { get; set; } = "旧扳手运行时地址，仅作定位线索，不作为当前 6.5 文件写入规则。";
}
