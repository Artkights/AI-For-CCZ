using System.Text;
using System.Text.RegularExpressions;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed partial class LsResourceReader
{
    public IReadOnlyList<LsResourceInfo> ReadAll(CczProject project)
    {
        var result = new List<LsResourceInfo>();
        AddDirectory(project.GameRoot, "根目录E5", result);
        AddDirectory(project.ResolveGameFile("E5"), "E5资源", result);
        return result
            .Where(x => x.MagicValid)
            .OrderBy(x => x.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public LsResourceInfo Read(string path, string category)
    {
        var bytes = File.ReadAllBytes(path);
        var info = new FileInfo(path);
        var magic = bytes.Length >= 4 ? Encoding.ASCII.GetString(bytes, 0, 4) : string.Empty;
        var magicValid = magic is "Ls12" or "Ls11" or "Ls10";
        var payloadOffset = magicValid && bytes.Length >= 16 ? 16 : 0;
        var payload = bytes.AsSpan(payloadOffset);
        var topBytes = payload.ToArray()
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(8)
            .Select(g => $"{HexDisplayFormatter.FormatByte(g.Key)}:{g.Count()}");
        var firstPayload = payload[..Math.Min(32, payload.Length)].ToArray();
        var textHits = BinaryTextScanner.ScanGbkNullTerminatedStringHits(bytes, minByteLength: 5, maxItems: 6, requireCjk: true);

        return new LsResourceInfo
        {
            Category = category,
            FileName = info.Name,
            Id = ExtractNumber(info.Name),
            Length = info.Length,
            MagicValid = magicValid,
            Magic = magic,
            HeaderText = bytes.Length >= 16 ? Encoding.ASCII.GetString(bytes, 0, 16).TrimEnd() : string.Empty,
            PayloadOffset = payloadOffset,
            PayloadLength = Math.Max(0, bytes.Length - payloadOffset),
            UniqueByteCount = payload.ToArray().Distinct().Count(),
            ZeroPercent = payload.Length == 0 ? 0 : payload.ToArray().Count(x => x == 0) * 100.0 / payload.Length,
            TopBytesHex = string.Join(" / ", topBytes),
            FirstPayloadBytesHex = BitConverter.ToString(firstPayload).Replace("-", " "),
            RoleHint = GuessRole(info.Name, category),
            TextHintCount = textHits.Count,
            TextHints = string.Join(" / ", textHits.Select(x => $"{x.OffsetHex}:{TrimForGrid(x.Text, 32)}")),
            Annotation = BuildAnnotation(GuessRole(info.Name, category), magicValid, payload.Length, payload.ToArray().Distinct().Count(), payload.Length == 0 ? 0 : payload.ToArray().Count(x => x == 0) * 100.0 / payload.Length, textHits.Count),
            RoleReason = GuessRoleReason(info.Name, category),
            Path = path
        };
    }



    private static string BuildAnnotation(string roleHint, bool magicValid, int payloadLength, int uniqueByteCount, double zeroPercent, int textHintCount)
    {
        if (!magicValid)
        {
            return "\u4e0d\u662f Ls12/Ls11/Ls10 \u5934\u7684\u8d44\u6e90\uff1b\u5f53\u524d\u63a2\u9488\u4f1a\u8fc7\u6ee4\u6389\u975e Ls \u5c01\u88c5\u6587\u4ef6\u3002";
        }

        var density = zeroPercent > 70
            ? "00 \u5b57\u8282\u5360\u6bd4\u8f83\u9ad8\uff0c\u53ef\u80fd\u5305\u542b\u7a00\u758f\u8868\u3001\u56fe\u5f62\u7a7a\u767d\u533a\u6216\u538b\u7f29\u540e\u586b\u5145\u3002"
            : zeroPercent < 5
                ? "00 \u5b57\u8282\u5360\u6bd4\u8f83\u4f4e\uff0c\u53ef\u80fd\u662f\u8f83\u5bc6\u96c6\u7684\u56fe\u50cf/\u538b\u7f29\u8f7d\u8377\u3002"
                : "00 \u5b57\u8282\u5360\u6bd4\u4e2d\u7b49\uff0c\u9700\u8981\u7ed3\u5408\u89d2\u8272\u5019\u9009\u548c\u6587\u4ef6\u540d\u5224\u65ad\u3002";
        var text = textHintCount > 0
            ? $"\u53d1\u73b0 {textHintCount} \u6761\u6587\u672c\u7ebf\u7d22\uff0c\u53ef\u8f85\u52a9\u5224\u65ad\u8d44\u6e90\u7528\u9014\u3002"
            : "\u672a\u53d1\u73b0\u660e\u663e GBK \u6587\u672c\u7ebf\u7d22\u3002";
        return $"\u89d2\u8272\u5019\u9009\uff1a{roleHint}\u3002\u8f7d\u8377 {payloadLength:N0} \u5b57\u8282\uff0c\u4e0d\u540c\u5b57\u8282 {uniqueByteCount}\u3002{density}{text}\u5f53\u524d\u4ec5\u505a\u53ea\u8bfb\u5c01\u88c5\u7814\u7a76\uff0c\u4e0d\u6267\u884c\u89e3\u538b\u6216\u91cd\u5c01\u5305\u3002";
    }

    private static string GuessRoleReason(string fileName, string category)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (name.Equals("Data", StringComparison.OrdinalIgnoreCase)) return "\u4f9d\u636e\u6587\u4ef6\u540d Data\uff1a\u6838\u5fc3\u6570\u503c\u8868\u901a\u5e38\u5c01\u88c5\u4eba\u7269\u3001\u7269\u54c1\u3001\u7b56\u7565\u7b49\u57fa\u7840\u6570\u636e\u3002";
        if (name.Equals("Imsg", StringComparison.OrdinalIgnoreCase)) return "\u4f9d\u636e\u6587\u4ef6\u540d Imsg\uff1a\u901a\u5e38\u627f\u8f7d\u4ecb\u7ecd\u3001\u6d88\u606f\u7b49\u56fa\u5b9a\u957f\u5ea6\u6587\u672c\u3002";
        if (name.Equals("Star", StringComparison.OrdinalIgnoreCase)) return "\u4f9d\u636e\u6587\u4ef6\u540d Star\uff1a\u5e38\u89c1\u4e8e\u6269\u5c55\u5b9d\u7269\u6216\u661f\u7ea7/\u6269\u5c55\u8868\u3002";
        if (name.Equals("Face", StringComparison.OrdinalIgnoreCase)) return "\u4f9d\u636e\u6587\u4ef6\u540d Face\uff1a\u901a\u5e38\u4e0e\u5934\u50cf\u8d44\u6e90\u76f8\u5173\u3002";
        if (name.StartsWith("Unit_", StringComparison.OrdinalIgnoreCase)) return "\u4f9d\u636e Unit_ \u524d\u7f00\uff1a\u901a\u5e38\u4e3a\u5355\u4f4d\u52a8\u4f5c/\u79fb\u52a8/\u653b\u51fb\u5e27\u8d44\u6e90\u3002";
        if (name.Contains("palet", StringComparison.OrdinalIgnoreCase)) return "\u4f9d\u636e palet/palette \u547d\u540d\uff1a\u901a\u5e38\u4e0e\u8c03\u8272\u677f\u76f8\u5173\u3002";
        if (name.Contains("map", StringComparison.OrdinalIgnoreCase)) return "\u4f9d\u636e map \u547d\u540d\uff1a\u901a\u5e38\u4e0e\u5730\u56fe\u3001\u5730\u5f62\u3001\u7f29\u7565\u56fe\u6216\u5730\u56fe\u5bf9\u8c61\u76f8\u5173\u3002";
        if (name.Contains("eff", StringComparison.OrdinalIgnoreCase) || name.Contains("hit", StringComparison.OrdinalIgnoreCase)) return "\u4f9d\u636e eff/hit \u547d\u540d\uff1a\u901a\u5e38\u4e0e\u6218\u6597\u7279\u6548\u6216\u547d\u4e2d\u8303\u56f4\u76f8\u5173\u3002";
        if (name.Equals("Mark", StringComparison.OrdinalIgnoreCase)) return "\u4f9d\u636e\u6587\u4ef6\u540d Mark\uff1a\u901a\u5e38\u4e0e\u6807\u8bb0\u3001\u5c0f\u56fe\u6807\u6216\u72b6\u6001\u56fe\u6807\u76f8\u5173\u3002";
        return $"\u4f9d\u636e\u6240\u5728\u5206\u7c7b {category} \u548c\u6587\u4ef6\u540d\u505a\u5f31\u63a8\u65ad\uff1b\u9700\u7ed3\u5408\u8f7d\u8377\u7edf\u8ba1\u4e0e\u6e38\u620f\u5185\u5f15\u7528\u7ee7\u7eed\u9a8c\u8bc1\u3002";
    }

    private static void AddDirectory(string dir, string category, List<LsResourceInfo> result)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var path in Directory.GetFiles(dir, "*.e5").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            result.Add(new LsResourceReader().Read(path, category));
        }
    }

    private static string GuessRole(string fileName, string category)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (name.Equals("Data", StringComparison.OrdinalIgnoreCase)) return "核心数值表封装";
        if (name.Equals("Imsg", StringComparison.OrdinalIgnoreCase)) return "文本消息表封装";
        if (name.Equals("Star", StringComparison.OrdinalIgnoreCase)) return "扩展宝物表封装";
        if (name.Equals("Font", StringComparison.OrdinalIgnoreCase)) return "字体/字形资源候选";
        if (name.Equals("Pmapobj", StringComparison.OrdinalIgnoreCase)) return "地图对象资源候选";
        if (name.StartsWith("Unit_", StringComparison.OrdinalIgnoreCase)) return "单位动作资源候选";
        if (name.Equals("Hexzmap", StringComparison.OrdinalIgnoreCase)) return "战场地形/地图索引候选";
        if (name.Equals("Mmap", StringComparison.OrdinalIgnoreCase)) return "大地图/战场地图资源候选";
        if (name.Equals("Pmap", StringComparison.OrdinalIgnoreCase)) return "地图缩略/图片资源候选";
        if (name.Equals("Spalet", StringComparison.OrdinalIgnoreCase)) return "战场地图调色板候选";
        if (name.Equals("Pmpalet", StringComparison.OrdinalIgnoreCase)) return "缩略图调色板候选";
        if (name.StartsWith("Mcall", StringComparison.OrdinalIgnoreCase)) return "召唤/战场动画资源候选";
        if (name.Equals("Face", StringComparison.OrdinalIgnoreCase)) return "头像资源候选";
        if (name.Equals("Mark", StringComparison.OrdinalIgnoreCase)) return "标记/小图标资源候选";
        if (name.Equals("Meff", StringComparison.OrdinalIgnoreCase) || name.Equals("Effarea", StringComparison.OrdinalIgnoreCase) || name.Equals("Hitarea", StringComparison.OrdinalIgnoreCase)) return "战斗特效资源候选";
        return category;
    }

    private static string TrimForGrid(string text, int maxChars)
    {
        text = text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        return text.Length > maxChars ? text[..maxChars] + "…" : text;
    }

    private static string ExtractNumber(string fileName)
    {
        var match = NumberRegex().Match(fileName);
        return match.Success ? match.Value : string.Empty;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();
}
