using System.Globalization;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 将 EEX 区段探针结果整理成创作者可读的中文节点详情。
/// 该服务只解释只读探针证据，不解压、不还原帧图、不重封包。
/// </summary>
public sealed class EexEntryTreeDetailService
{
    public string BuildTreeSummary(IReadOnlyList<EexEntryProbeRow> rows)
    {
        if (rows.Count == 0)
        {
            return "EEX 区段树：尚未生成区段探针。请选择 R/S/Map .eex 后点击“解析选中EEX区段”。";
        }

        var fileName = rows.First().FileName;
        var category = rows.First().Category;
        var sections = rows.Where(row => row.NodeType == "区段候选").ToList();
        var headerRows = rows.Count(row => row.NodeType != "区段候选");
        var textSections = sections.Count(row => row.TextHintCount > 0);
        var roleSummary = string.Join("，", sections
            .GroupBy(row => row.RoleHint)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => $"{group.Key}:{group.Count()}"));

        return
            $"EEX 区段树：{category}/{fileName}\r\n" +
            $"头字段：{headerRows}    区段候选：{sections.Count}    含文本线索区段：{textSections}\r\n" +
            $"角色分布：{roleSummary}\r\n" +
            "说明：该树按“文件头/头字段/区段候选角色”组织，只读辅助判断动作参数、文本线索、图像或压缩载荷候选；不会解压或写回 EEX。";
    }

    public string BuildDetail(EexEntryProbeRow row)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"EEX区段节点详情：{row.Category}/{row.FileName}");
        builder.AppendLine($"节点类型：{row.NodeType}    序号：{row.Index}    偏移：{ValueOrDash(row.OffsetHex)}    长度：{row.Length:N0} 字节");
        builder.AppendLine($"角色候选：{ValueOrDash(row.RoleHint)}    值/范围：{ValueOrDash(row.ValueHex)}");
        if (row.UniqueByteCount > 0 || row.ZeroPercent > 0 || row.SmallWordPercent > 0)
        {
            builder.AppendLine($"字节统计：不同字节 {row.UniqueByteCount}；00 占比 {row.ZeroPercent:F1}%；小整数16位词占比 {row.SmallWordPercent:F1}%");
        }
        if (!string.IsNullOrWhiteSpace(row.TextHints))
        {
            builder.AppendLine("文本线索：" + row.TextHints);
        }
        if (!string.IsNullOrWhiteSpace(row.FirstBytesHex))
        {
            builder.AppendLine("开头字节：" + Trim(row.FirstBytesHex, 180));
        }
        if (!string.IsNullOrWhiteSpace(row.Annotation))
        {
            builder.AppendLine("中文注释：" + row.Annotation);
        }

        builder.AppendLine();
        builder.AppendLine("创作解释：" + BuildCreatorExplanation(row));
        builder.AppendLine("建议操作：" + BuildSuggestedAction(row));
        builder.AppendLine("安全边界：当前为只读探针，不能据此直接重封包写入；若要替换 EEX，只能走测试副本整文件替换、自动备份和实机验证流程。");
        return builder.ToString();
    }

    public IReadOnlyList<EexTreeGroup> BuildGroups(IReadOnlyList<EexEntryProbeRow> rows)
    {
        var result = new List<EexTreeGroup>();
        var headerRows = rows.Where(row => row.NodeType != "区段候选").ToList();
        if (headerRows.Count > 0)
        {
            result.Add(new EexTreeGroup("文件头/头字段", "EEX 魔数、版本、条目数和疑似区段边界字段。", headerRows));
        }

        result.AddRange(rows
            .Where(row => row.NodeType == "区段候选")
            .GroupBy(row => row.RoleHint)
            .OrderBy(group => RoleSortKey(group.Key))
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new EexTreeGroup(group.Key, BuildGroupExplanation(group.Key), group.OrderBy(row => ParseHexOffset(row.OffsetHex)).ToList())));
        return result;
    }

    private static string BuildCreatorExplanation(EexEntryProbeRow row)
    {
        if (row.NodeType == "文件头")
        {
            return "文件头用于判断是否是真正的 EEX 资源包，并为后续区段推断提供版本、条目数和边界字段。魔数异常时不应继续做结构判断。";
        }

        if (row.NodeType == "头字段")
        {
            return row.RoleHint.Contains("偏移", StringComparison.Ordinal)
                ? "该头字段落在文件范围内，可能指向一个区段边界；多个边界共同决定后续区段切分。"
                : "该头字段暂不能解释为稳定区段偏移，可能是计数、标志或未知参数。";
        }

        if (row.RoleHint.Contains("文本", StringComparison.Ordinal))
        {
            return "该区段含有可见文本线索，可能保存动作名、说明、内部标签或旧工具留下的字符串。它有助于判断 R/S 动作含义。";
        }
        if (row.RoleHint.Contains("动作参数", StringComparison.Ordinal) || row.RoleHint.Contains("帧表", StringComparison.Ordinal))
        {
            return "该区段小整数 16 位词比例较高，可能是动作参数、帧索引、坐标、延时或帧表类数据。适合与动作帧数量、热力图和同类 R/S 文件对比。";
        }
        if (row.RoleHint.Contains("图像", StringComparison.Ordinal) || row.RoleHint.Contains("压缩", StringComparison.Ordinal))
        {
            return "该区段字节种类较多且长度较大，可能是图像或压缩载荷。当前尚未确认编码方式，只能作为后续解包研究入口。";
        }
        if (row.RoleHint.Contains("透明", StringComparison.Ordinal) || row.RoleHint.Contains("稀疏", StringComparison.Ordinal))
        {
            return "该区段 00 占比较高，可能与透明像素、空白帧、稀疏掩码或填充数据有关。请配合热力图观察。";
        }

        return "该区段尚无稳定角色，只能根据长度、字节分布、文本线索和相邻区段位置继续推断。";
    }

    private static string BuildSuggestedAction(EexEntryProbeRow row)
    {
        if (row.NodeType != "区段候选")
        {
            return "先查看文件头字段是否合理，再选择区段候选生成字节热力图。";
        }

        if (row.TextHintCount > 0)
        {
            return "优先记录文本线索并与人物 R/S 编号、动作名、旧工具显示结果交叉验证。";
        }
        if (row.RoleHint.Contains("帧表", StringComparison.Ordinal) || row.RoleHint.Contains("动作参数", StringComparison.Ordinal))
        {
            return "建议导出 CSV，对比多个 R_XX/S_XX 的同类区段长度和小整数分布，寻找帧数、方向、动作序列规律。";
        }
        if (row.RoleHint.Contains("图像", StringComparison.Ordinal) || row.RoleHint.Contains("压缩", StringComparison.Ordinal))
        {
            return "建议生成热力图，观察是否存在块状、条纹或高熵区域；不要直接修改内部字节。";
        }
        return "建议先生成热力图并保留备份；只有在格式确认后再考虑更深入的解包研究。";
    }

    private static string BuildGroupExplanation(string roleHint)
    {
        if (roleHint.Contains("文本", StringComparison.Ordinal)) return "含 GBK/ASCII 文本线索的区段，常用于动作名、说明或内部标签定位。";
        if (roleHint.Contains("帧表", StringComparison.Ordinal) || roleHint.Contains("动作参数", StringComparison.Ordinal)) return "小整数密集区段，可能是动作参数、帧表、坐标或延时。";
        if (roleHint.Contains("图像", StringComparison.Ordinal) || roleHint.Contains("压缩", StringComparison.Ordinal)) return "高字节种类/较大载荷区段，可能是图像或压缩数据。";
        if (roleHint.Contains("透明", StringComparison.Ordinal) || roleHint.Contains("稀疏", StringComparison.Ordinal)) return "00 占比很高的区段，可能是透明像素、掩码或填充。";
        return "尚未稳定识别的二进制区段，需要结合热力图与同类文件对比。";
    }

    private static int RoleSortKey(string roleHint)
    {
        if (roleHint.Contains("文本", StringComparison.Ordinal)) return 0;
        if (roleHint.Contains("帧表", StringComparison.Ordinal) || roleHint.Contains("动作参数", StringComparison.Ordinal)) return 1;
        if (roleHint.Contains("图像", StringComparison.Ordinal) || roleHint.Contains("压缩", StringComparison.Ordinal)) return 2;
        if (roleHint.Contains("透明", StringComparison.Ordinal) || roleHint.Contains("稀疏", StringComparison.Ordinal)) return 3;
        return 9;
    }

    private static int ParseHexOffset(string offsetHex)
    {
        if (string.IsNullOrWhiteSpace(offsetHex)) return int.MaxValue;
        var text = offsetHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? offsetHex[2..] : offsetHex;
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;
    }

    private static string ValueOrDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string Trim(string value, int maxChars)
        => value.Length <= maxChars ? value : value[..maxChars] + "…";
}

public sealed record EexTreeGroup(string Name, string Explanation, IReadOnlyList<EexEntryProbeRow> Rows);
