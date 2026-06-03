using System.Globalization;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class ResourceDiagnosticService
{
    public IReadOnlyList<ResourceDiagnosticItem> Analyze(IEnumerable<ResourceIndexItem> resources)
    {
        var items = resources.ToList();
        var diagnostics = new List<ResourceDiagnosticItem>();

        AddCategoryOverview(items, diagnostics);
        AddDuplicateIdDiagnostics(items, diagnostics);
        AddMissingNumberDiagnostics(items, diagnostics);
        AddNamingDiagnostics(items, diagnostics);
        AddFormatDiagnostics(items, diagnostics);
        AddDimensionDiagnostics(items, diagnostics);

        return diagnostics
            .OrderByDescending(x => SeverityRank(x.Severity))
            .ThenBy(x => x.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Rule, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => NaturalIdSortKey(x.Id))
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void AddCategoryOverview(IReadOnlyList<ResourceIndexItem> items, List<ResourceDiagnosticItem> diagnostics)
    {
        foreach (var group in items.GroupBy(x => x.Category).OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var numbered = GetNumbered(group).ToList();
            var range = numbered.Count == 0
                ? "无数字编号"
                : $"{numbered.Min(x => x.Number)}-{numbered.Max(x => x.Number)}，数字项 {numbered.Select(x => x.Number).Distinct().Count()}";
            var formats = string.Join("，", group
                .GroupBy(x => string.IsNullOrWhiteSpace(x.FormatHint) ? "未知格式" : x.FormatHint)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
                .Take(5)
                .Select(x => $"{x.Key}:{x.Count()}"));

            diagnostics.Add(new ResourceDiagnosticItem
            {
                Severity = "Info",
                Category = group.Key,
                Rule = "分类概览",
                Id = string.Empty,
                Name = group.Key,
                Status = $"{group.Count()} 个文件",
                Detail = $"编号范围：{range}；格式摘要：{formats}",
                Suggestion = "用于快速确认当前 MOD 资源规模；后续缺号、重复和命名异常会在同一报告中列出。",
                Path = string.Empty
            });
        }
    }

    private static void AddDuplicateIdDiagnostics(IReadOnlyList<ResourceIndexItem> items, List<ResourceDiagnosticItem> diagnostics)
    {
        foreach (var duplicate in items
                     .Where(x => IsSequenceCategory(x.Category) && TryParseId(x.Id, out _))
                     .GroupBy(x => new { x.Category, x.Id })
                     .Where(x => x.Count() > 1)
                     .OrderBy(x => x.Key.Category, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(x => NaturalIdSortKey(x.Key.Id)))
        {
            diagnostics.Add(new ResourceDiagnosticItem
            {
                Severity = "Warn",
                Category = duplicate.Key.Category,
                Rule = "重复编号",
                Id = duplicate.Key.Id,
                Name = string.Join("；", duplicate.Select(x => x.Name).Take(8)),
                Status = $"{duplicate.Count()} 个文件共用编号",
                Detail = "同一分类内编号重复：" + string.Join(" | ", duplicate.Select(x => x.Path).Take(8)),
                Suggestion = "曹操传资源通常按编号引用。若不是刻意保留多份备选文件，请统一命名或删除多余副本，避免工具和策划表误判。",
                Path = duplicate.First().Path
            });
        }
    }

    private static void AddMissingNumberDiagnostics(IReadOnlyList<ResourceIndexItem> items, List<ResourceDiagnosticItem> diagnostics)
    {
        foreach (var group in items
                     .Where(x => IsSequenceCategory(x.Category))
                     .GroupBy(x => x.Category)
                     .OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var numbered = GetNumbered(group).ToList();
            if (numbered.Count < 2) continue;

            var numbers = numbered.Select(x => x.Number).Distinct().Order().ToList();
            var min = numbers[0];
            var max = numbers[^1];
            var expected = max - min + 1;
            var missingCount = expected - numbers.Count;
            if (missingCount <= 0) continue;

            var numberSet = numbers.ToHashSet();
            var examples = Enumerable.Range(min, expected)
                .Where(x => !numberSet.Contains(x))
                .Take(40)
                .Select(x => FormatLikeExisting(numbered, x))
                .ToList();

            diagnostics.Add(new ResourceDiagnosticItem
            {
                Severity = IsCoreSequentialCategory(group.Key) ? "Warn" : "Info",
                Category = group.Key,
                Rule = "连续编号缺口",
                Id = $"{min}-{max}",
                Name = group.Key,
                Status = $"缺 {missingCount} 个编号",
                Detail = $"现有 {numbers.Count} 个不同编号，理论连续区间 {min}-{max} 共 {expected} 个；缺号示例：{string.Join("，", examples)}{(missingCount > examples.Count ? " ..." : string.Empty)}",
                Suggestion = "缺号不一定是错误，但 R/S 剧本、地图等常被制作流程或引擎按编号引用；E5S 仅按旧兼容/存档信息处理；发布前建议确认引用表或剧本规划里没有指向缺失编号。",
                Path = string.Empty
            });
        }
    }

    private static void AddNamingDiagnostics(IReadOnlyList<ResourceIndexItem> items, List<ResourceDiagnosticItem> diagnostics)
    {
        foreach (var item in items.Where(x => IsSequenceCategory(x.Category)))
        {
            if (RequiresNumericId(item.Category) && string.IsNullOrWhiteSpace(item.Id))
            {
                diagnostics.Add(new ResourceDiagnosticItem
                {
                    Severity = item.Category == "E5S存档信息" && item.Name.Equals("SV.E5S", StringComparison.OrdinalIgnoreCase) ? "Info" : "Warn",
                    Category = item.Category,
                    Rule = "缺少数字编号",
                    Id = string.Empty,
                    Name = item.Name,
                    Status = "未解析到数字编号",
                    Detail = "文件名中没有可用于表格引用的数字编号。",
                    Suggestion = item.Category == "E5S存档信息" && item.Name.Equals("SV.E5S", StringComparison.OrdinalIgnoreCase)
                        ? "E5S 是存档信息/旧兼容对象，不是当前 R/S eex 剧本主格式；如需保留请放在存档信息或旧兼容语境中核对。"
                        : "若该文件需要被游戏按编号引用，建议采用当前分类常见命名格式。",
                    Path = item.Path
                });
            }

            var expectedPattern = GetExpectedPattern(item.Category);
            if (expectedPattern.Length > 0 && !Regex.IsMatch(item.Name, expectedPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                diagnostics.Add(new ResourceDiagnosticItem
                {
                    Severity = IsAllowedSpecialName(item) ? "Info" : "Warn",
                    Category = item.Category,
                    Rule = "命名不规则",
                    Id = item.Id,
                    Name = item.Name,
                    Status = "不符合推荐命名",
                    Detail = $"推荐格式：{GetFriendlyPattern(item.Category)}；当前文件名：{item.Name}",
                    Suggestion = "不规则命名可能仍可被游戏读取，但会降低索引、筛选、自动校验和批量替换的可靠性。",
                    Path = item.Path
                });
            }
        }

        foreach (var group in items.Where(x => IsSequenceCategory(x.Category) && TryParseId(x.Id, out _)).GroupBy(x => x.Category))
        {
            var widths = group.Select(x => x.Id.Length).Distinct().Order().ToList();
            if (widths.Count <= 1) continue;

            diagnostics.Add(new ResourceDiagnosticItem
            {
                Severity = "Info",
                Category = group.Key,
                Rule = "编号位数不一致",
                Id = string.Empty,
                Name = group.Key,
                Status = string.Join("/", widths),
                Detail = "同一分类中数字编号位数不完全一致，可能影响按文件名排序和批量生成脚本。",
                Suggestion = $"建议统一采用该分类主流格式：{GetFriendlyPattern(group.Key)}。",
                Path = string.Empty
            });
        }
    }

    private static void AddFormatDiagnostics(IReadOnlyList<ResourceIndexItem> items, List<ResourceDiagnosticItem> diagnostics)
    {
        foreach (var item in items)
        {
            if (item.SizeBytes == 0)
            {
                diagnostics.Add(new ResourceDiagnosticItem
                {
                    Severity = "Error",
                    Category = item.Category,
                    Rule = "空文件",
                    Id = item.Id,
                    Name = item.Name,
                    Status = "0 字节",
                    Detail = "资源文件长度为 0，游戏通常无法正常读取。",
                    Suggestion = "请从备份恢复、重新导出或删除该占位文件。",
                    Path = item.Path
                });
            }

            var expectedFormat = ExpectedFormatFor(item);
            if (expectedFormat.Length > 0 && !item.FormatHint.Contains(expectedFormat, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new ResourceDiagnosticItem
                {
                    Severity = item.Category is "R剧本EEX" or "S剧本EEX" or "地图图片" ? "Warn" : "Info",
                    Category = item.Category,
                    Rule = "格式线索异常",
                    Id = item.Id,
                    Name = item.Name,
                    Status = string.IsNullOrWhiteSpace(item.FormatHint) ? "未识别格式" : item.FormatHint,
                    Detail = $"该分类预期格式线索包含“{expectedFormat}”，但魔数检测结果为“{(string.IsNullOrWhiteSpace(item.FormatHint) ? "未知" : item.FormatHint)}”，Magic={item.Magic}。",
                    Suggestion = "如果文件来自旧工具或手动替换，请用专用工具重新打开验证，避免扩展名正确但内容不是目标格式。",
                    Path = item.Path
                });
            }

            if ((item.Category == "R剧本EEX" || item.Category == "S剧本EEX") && item.SizeBytes < 16)
            {
                diagnostics.Add(new ResourceDiagnosticItem
                {
                    Severity = "Warn",
                    Category = item.Category,
                    Rule = "EEX尺寸过小",
                    Id = item.Id,
                    Name = item.Name,
                    Status = $"{item.SizeBytes} 字节",
                    Detail = "R/S 剧本 EEX 过小，连基础头部都可能不完整。",
                    Suggestion = "建议在 EEX 探针页查看 Magic、版本和头部字段，必要时从原版或素材库重新导入。",
                    Path = item.Path
                });
            }
        }
    }

    private static void AddDimensionDiagnostics(IReadOnlyList<ResourceIndexItem> items, List<ResourceDiagnosticItem> diagnostics)
    {
        foreach (var map in items.Where(x => x.Category == "地图图片"))
        {
            if (map.Width <= 0 || map.Height <= 0)
            {
                diagnostics.Add(new ResourceDiagnosticItem
                {
                    Severity = "Warn",
                    Category = map.Category,
                    Rule = "图片尺寸不可读",
                    Id = map.Id,
                    Name = map.Name,
                    Status = $"{map.Width}x{map.Height}",
                    Detail = "系统图片解码器未能读取地图尺寸，可能是损坏图片或扩展名/内容不匹配。",
                    Suggestion = "请在地图浏览页或外部图片查看器中打开验证；发布前建议重新保存为标准 JPG。",
                    Path = map.Path
                });
            }
        }
    }

    private static IEnumerable<(ResourceIndexItem Item, int Number)> GetNumbered(IEnumerable<ResourceIndexItem> items)
    {
        foreach (var item in items)
        {
            if (TryParseId(item.Id, out var number))
            {
                yield return (item, number);
            }
        }
    }

    private static bool TryParseId(string id, out int number) =>
        int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);

    private static bool IsSequenceCategory(string category) =>
        category is "R剧本EEX" or "S剧本EEX" or "地图图片" or "E5S存档信息" or "WAV音效" or "MP3音轨";

    private static bool RequiresNumericId(string category) =>
        category is "R剧本EEX" or "S剧本EEX" or "地图图片" or "E5S存档信息";

    private static bool IsCoreSequentialCategory(string category) =>
        category is "R剧本EEX" or "S剧本EEX" or "地图图片" or "E5S存档信息";

    private static bool IsAllowedSpecialName(ResourceIndexItem item) =>
        item.Category == "E5S存档信息" && item.Name.Equals("SV.E5S", StringComparison.OrdinalIgnoreCase);

    private static string GetExpectedPattern(string category) => category switch
    {
        "R剧本EEX" => @"^R_\d{2,}\.eex$",
        "S剧本EEX" => @"^S_\d{2,}\.eex$",
        "地图图片" => @"^M\d{3,}\.jpe?g$",
        "E5S存档信息" => @"^(SV\d{3,}|SV)\.E5S$",
        _ => string.Empty
    };

    private static string GetFriendlyPattern(string category) => category switch
    {
        "R剧本EEX" => "R_00.eex / R_001.eex",
        "S剧本EEX" => "S_00.eex / S_001.eex",
        "地图图片" => "M000.JPG",
        "E5S存档信息" => "SV001.E5S / SV.E5S（存档信息/旧兼容，不是 R/S eex 剧本）",
        "WAV音效" => "数字或语义化 .wav，建议与表格编号保持一致",
        "MP3音轨" => "数字或语义化 .mp3，建议与表格编号保持一致",
        _ => "当前分类常见编号格式"
    };

    private static string ExpectedFormatFor(ResourceIndexItem item) => item.Category switch
    {
        "R剧本EEX" or "S剧本EEX" => "EEX",
        "地图图片" => "JPEG",
        "E5S存档信息" => "E5S",
        "WAV音效" => "WAV",
        "MP3音轨" => "MP3",
        _ => string.Empty
    };

    private static string FormatLikeExisting(List<(ResourceIndexItem Item, int Number)> numbered, int number)
    {
        var width = numbered
            .Select(x => x.Item.Id.Length)
            .GroupBy(x => x)
            .OrderByDescending(x => x.Count())
            .ThenByDescending(x => x.Key)
            .First().Key;
        return number.ToString("D" + width, CultureInfo.InvariantCulture);
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "Error" => 3,
        "Warn" => 2,
        "Info" => 1,
        _ => 0
    };

    private static int NaturalIdSortKey(string id) =>
        TryParseId(id, out var number) ? number : int.MaxValue;
}
