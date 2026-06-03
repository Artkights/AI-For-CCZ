namespace CCZModStudio.Core;

public static class ItemTypeCatalogService
{
    private static readonly IReadOnlyDictionary<int, ItemTypeCatalogEntry> Entries = new Dictionary<int, ItemTypeCatalogEntry>
    {
        [0] = new("普通剑系", "短剑、大剑、钢剑"),
        [1] = new("宝物剑/刀剑系", "雌雄双剑、青釭剑、倚天剑、古锭刀、太阿"),
        [2] = new("普通枪系", "短枪、长枪、钢枪"),
        [3] = new("宝物长兵器/枪戟系", "青龙偃月刀、蛇矛、方天画戟、龙胆枪"),
        [4] = new("普通弓系", "短弓、大弓、铁弓"),
        [5] = new("宝物弓系", "吕布之弓、李广之弓、养由基之弓"),
        [6] = new("普通刀系", "环首刀、眉尖刀、凤嘴刀"),
        [7] = new("宝物刀/骨朵系", "三尖刀、铁蒺藜骨朵、霸海"),
        [8] = new("普通弩系", "擘张弩、蹶张弩、腰引弩"),
        [9] = new("宝物弩/炮候选", "金火罐炮、黄肩弩"),
        [10] = new("普通锤系", "木锤、铜锤、铁锤"),
        [11] = new("宝物锤/戟候选", "流星锤、双铁戟"),
        [12] = new("普通斧系", "镔铁斧、精钢斧、开山斧"),
        [13] = new("宝物斧系", "大斧"),
        [14] = new("普通扇系", "青竹扇、红绫扇、鹤羽扇"),
        [15] = new("宝物扇系", "芭蕉扇、五火神焰扇、白羽扇"),
        [16] = new("普通法剑/文士剑系", "桃木剑、松纹剑、文士剑"),
        [17] = new("宝物法剑系", "七星剑、圣者宝剑"),
        [18] = new("将剑系", "铜制将剑、铁制将剑、银制将剑"),
        [20] = new("普通铠甲系", "硬皮甲、鱼鳞铠、明光铠"),
        [21] = new("宝物铠甲/战衣系", "镜铠、黄金铠、龙鳞铠、青龙战衣"),
        [22] = new("普通衣服系", "麻布衣、兽皮袄、金缕衣"),
        [23] = new("宝物道袍/战袍系", "飞龙道袍、凤凰羽衣、白虎斗袍、锦帆战袍"),
        [24] = new("普通文官袍系", "文官袍、太极袍、楚锦袍"),
        [25] = new("宝物道服/衣袍系", "鹤氅、漆黑道服、天仙洞衣"),
        [26] = new("恢复/通用道具候选", "青囊书、豆、麦、米"),
        [27] = new("酒水/太平要术候选", "太平要术、山泉水、杜康酒"),
        [28] = new("状态恢复候选", "太平清领道、正气丸"),
        [29] = new("解毒候选", "解毒散"),
        [30] = new("兵书/恢复草候选", "孙子兵法、活血草"),
        [31] = new("醒脑候选", "醒脑丸"),
        [32] = new("兵书/高级丹候选", "六韬、碧灵丹"),
        [33] = new("兵书候选", "三略"),
        [37] = new("镜/头盔类辅助", "护心镜、皮盔、铜盔、铁盔"),
        [38] = new("头巾类辅助", "方巾、纶巾、诸葛巾"),
        [39] = new("兵书/兵符候选", "孟德新书、兵符"),
        [40] = new("坐骑/车轮/扩展占位", "风车轮、爪黄飞电、绝影、F3热兵器"),
        [41] = new("赤兔马专用候选", "赤兔马"),
        [42] = new("的卢专用候选", "的卢"),
        [47] = new("手套类辅助", "布手套、皮手套"),
        [50] = new("暗器/没羽箭候选", "没羽箭、B1道具"),
        [51] = new("投掷/箭矢候选", "手戟、狼牙箭"),
        [58] = new("四神宝玉/铜雀", "青龙宝玉、朱雀宝玉、白虎宝玉、玄武宝玉、铜雀"),
        [59] = new("遁甲天书候选", "遁甲天书"),
        [60] = new("强化书/果/证书", "武器强化书、防具强化书、经验果、功勋果、星级证"),
        [61] = new("盾/装备突破候选", "皮盾、铜盾、装备突破书"),
        [62] = new("盾/工程/热兵器扩展", "白银盾、神工铲、F9热兵器"),
        [63] = new("盾/机关盒候选", "风神盾、天机盒"),
        [64] = new("地雷类", "土雷、石雷、铁雷、风火雷、彻地雷、轰天雷"),
        [65] = new("爆炸物类", "手榴弹、手雷、爆破筒、炸药包、核弹"),
        [66] = new("探雷器", "探雷器"),
        [67] = new("热兵器扩展 E2", "E2热兵器"),
        [68] = new("热兵器扩展 E3", "E3热兵器"),
        [69] = new("马铠/热兵器扩展 E4", "铁制马凯、E4热兵器"),
        [70] = new("粮车/热兵器扩展 E5", "粮车、E5热兵器"),
        [71] = new("热兵器扩展 E6", "E6热兵器"),
        [72] = new("热兵器扩展 E7", "E7热兵器"),
        [73] = new("热兵器扩展 E8", "E8热兵器"),
        [74] = new("热兵器扩展 E9", "E9热兵器"),
        [75] = new("热兵器扩展 EA", "EA热兵器"),
        [76] = new("热兵器扩展 EB", "EB热兵器"),
        [77] = new("热兵器扩展 EC", "EC热兵器"),
        [78] = new("热兵器扩展 ED", "ED热兵器"),
        [79] = new("青缸剑/热兵器扩展 EE", "青缸剑、EE热兵器"),
        [80] = new("铁鞭/热兵器扩展 EF", "铁鞭、EF热兵器"),
        [81] = new("热兵器扩展 F0", "F0热兵器"),
        [82] = new("热兵器扩展 F1", "F1热兵器"),
        [83] = new("热兵器扩展 F2", "F2热兵器"),
        [97] = new("玉玺", "玉玺"),
        [110] = new("春秋左传", "春秋左传"),
        [118] = new("锁链", "锁链")
    };

    public static string BuildDescription(int typeId, string majorCategory, int catalog)
    {
        var catalogText = catalog == 1 ? "图鉴=宝物" : "图鉴=普通/未登记";
        if (!Entries.TryGetValue(typeId, out var entry))
        {
            return $"{majorCategory} 类型码={typeId}；{catalogText}；含义待 B形象指定器/CczRSX/实机确认";
        }

        return $"{entry.Name}；类型码={typeId}；{catalogText}；样例：{entry.SampleNames}";
    }

    public static string BuildShortName(int typeId, string majorCategory)
    {
        if (Entries.TryGetValue(typeId, out var entry))
        {
            return entry.Name;
        }

        return string.IsNullOrWhiteSpace(majorCategory)
            ? $"类型{typeId}"
            : $"{majorCategory}类型{typeId}";
    }

    public static bool TryGetEntry(int typeId, out ItemTypeCatalogEntry entry) => Entries.TryGetValue(typeId, out entry!);

    public static IReadOnlyDictionary<int, ItemTypeCatalogEntry> GetEntries() => Entries;
}

public sealed record ItemTypeCatalogEntry(string Name, string SampleNames);
