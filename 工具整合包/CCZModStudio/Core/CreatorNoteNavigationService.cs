using System.Globalization;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 把创作者备注的 Scope/TargetKey 解析为可定位目标。该服务只解析字符串，不读取或写入游戏文件。
/// </summary>
public sealed class CreatorNoteNavigationService
{
    public CreatorNoteNavigationTarget Parse(CreatorNote note)
    {
        var scope = note.Scope?.Trim() ?? string.Empty;
        var target = note.TargetKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(target))
        {
            return Unknown(scope, target);
        }

        if (scope.Contains("工作台行动", StringComparison.Ordinal))
        {
            var match = Regex.Match(
                target,
                @"^WorkflowAction#Area=(?<area>.+)$",
                RegexOptions.CultureInvariant);
            if (match.Success)
            {
                var area = match.Groups["area"].Value.Replace("＃", "#", StringComparison.Ordinal);
                return new CreatorNoteNavigationTarget
                {
                    IsRecognized = true,
                    Kind = "工作台行动",
                    Scope = scope,
                    TargetKey = target,
                    Name = area,
                    DisplayText = $"工作台行动：{area}"
                };
            }
        }

        if (IsScenarioCommandScope(scope))
        {
            var match = Regex.Match(
                target,
                @"^(?<file>[^#]+)#Scene=(?<scene>\d+)#Section=(?<section>\d+)#Command=(?<command>\d+)#Offset=(?<offset>0x[0-9A-Fa-f]+)$",
                RegexOptions.CultureInvariant);
            if (match.Success)
            {
                var kind = scope.Contains("R/S命令", StringComparison.Ordinal) ? "R/S命令" : "SV命令";
                return new CreatorNoteNavigationTarget
                {
                    IsRecognized = true,
                    Kind = kind,
                    Scope = scope,
                    TargetKey = target,
                    FileName = match.Groups["file"].Value,
                    SceneIndex = ParseInt(match.Groups["scene"].Value),
                    SectionIndex = ParseInt(match.Groups["section"].Value),
                    CommandIndex = ParseInt(match.Groups["command"].Value),
                    OffsetHex = NormalizeHex(match.Groups["offset"].Value),
                    DisplayText = $"{kind}：{match.Groups["file"].Value} / Scene {match.Groups["scene"].Value} / Command {match.Groups["command"].Value}"
                };
            }
        }

        if (IsScenarioTextScope(scope))
        {
            var match = Regex.Match(
                target,
                @"^(?<file>[^#]+)#TextIndex=(?<index>\d+)#Offset=(?<offset>0x[0-9A-Fa-f]+)$",
                RegexOptions.CultureInvariant);
            if (match.Success)
            {
                var kind = scope.Contains("R/S文本", StringComparison.Ordinal) ? "R/S文本" : "SV文本";
                return new CreatorNoteNavigationTarget
                {
                    IsRecognized = true,
                    Kind = kind,
                    Scope = scope,
                    TargetKey = target,
                    FileName = match.Groups["file"].Value,
                    TextIndex = ParseInt(match.Groups["index"].Value),
                    OffsetHex = NormalizeHex(match.Groups["offset"].Value),
                    DisplayText = $"{kind}：{match.Groups["file"].Value} / #{match.Groups["index"].Value}"
                };
            }
        }

        if (scope.Contains("Hexzmap", StringComparison.Ordinal))
        {
            var match = Regex.Match(
                target,
                @"^(?<map>M\d{3})#Offset=(?<offset>0x[0-9A-Fa-f]+)$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return new CreatorNoteNavigationTarget
                {
                    IsRecognized = true,
                    Kind = "Hexzmap地形块",
                    Scope = scope,
                    TargetKey = target,
                    MapId = match.Groups["map"].Value.ToUpperInvariant(),
                    OffsetHex = NormalizeHex(match.Groups["offset"].Value),
                    DisplayText = $"Hexzmap：{match.Groups["map"].Value.ToUpperInvariant()} / {NormalizeHex(match.Groups["offset"].Value)}"
                };
            }
        }

        if (scope.Contains("关卡地图联动", StringComparison.Ordinal) || target.Contains("->", StringComparison.Ordinal))
        {
            var match = Regex.Match(
                target,
                @"^(?<file>[^#>]+)->(?<map>M\d{3})$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return new CreatorNoteNavigationTarget
                {
                    IsRecognized = true,
                    Kind = "关卡地图联动",
                    Scope = scope,
                    TargetKey = target,
                    FileName = match.Groups["file"].Value,
                    MapId = match.Groups["map"].Value.ToUpperInvariant(),
                    DisplayText = $"关卡地图联动：{match.Groups["file"].Value} -> {match.Groups["map"].Value.ToUpperInvariant()}"
                };
            }
        }

        if (scope.Contains("数据表", StringComparison.Ordinal))
        {
            var match = Regex.Match(
                target,
                @"^(?<table>.+)#ID=(?<row>[^#]+)#字段=(?<field>.+)$",
                RegexOptions.CultureInvariant);
            if (match.Success)
            {
                return new CreatorNoteNavigationTarget
                {
                    IsRecognized = true,
                    Kind = "数据表单元格",
                    Scope = scope,
                    TargetKey = target,
                    TableName = match.Groups["table"].Value,
                    RowId = match.Groups["row"].Value,
                    FieldName = match.Groups["field"].Value,
                    DisplayText = $"数据表：{match.Groups["table"].Value} / ID={match.Groups["row"].Value} / {match.Groups["field"].Value}"
                };
            }
        }

        if (scope.Contains("资源诊断", StringComparison.Ordinal))
        {
            var match = Regex.Match(
                target,
                @"^资源诊断#分类=(?<category>.*?)#规则=(?<rule>.*?)#编号=(?<id>.*?)#对象=(?<name>.*)$",
                RegexOptions.CultureInvariant);
            if (match.Success)
            {
                return new CreatorNoteNavigationTarget
                {
                    IsRecognized = true,
                    Kind = "资源诊断",
                    Scope = scope,
                    TargetKey = target,
                    Category = match.Groups["category"].Value,
                    Rule = match.Groups["rule"].Value,
                    Id = match.Groups["id"].Value,
                    Name = match.Groups["name"].Value,
                    DisplayText = $"资源诊断：{match.Groups["category"].Value} / {match.Groups["rule"].Value} / {match.Groups["name"].Value}"
                };
            }
        }

        if (scope.Contains("EEX跨文件对比", StringComparison.Ordinal))
        {
            var match = Regex.Match(
                target,
                @"^EexCross#Base=(?<base>[^#]+)#PeerKind=(?<peer>[^#]*)#Category=(?<category>[^#]+)#File=(?<file>[^#]+)#Role=(?<role>.*)$",
                RegexOptions.CultureInvariant);
            if (match.Success)
            {
                return new CreatorNoteNavigationTarget
                {
                    IsRecognized = true,
                    Kind = "EEX跨文件对比",
                    Scope = scope,
                    TargetKey = target,
                    BaseFileName = match.Groups["base"].Value,
                    PeerKind = match.Groups["peer"].Value,
                    Category = match.Groups["category"].Value,
                    FileName = match.Groups["file"].Value,
                    Name = match.Groups["file"].Value,
                    RoleHint = match.Groups["role"].Value,
                    DisplayText = $"EEX跨文件对比：基准 {match.Groups["base"].Value} / {match.Groups["peer"].Value} / {match.Groups["category"].Value}/{match.Groups["file"].Value} / {match.Groups["role"].Value}"
                };
            }
        }

        if (scope.Contains("EEX区段", StringComparison.Ordinal))
        {
            var match = Regex.Match(
                target,
                @"^EexEntry#File=(?<file>[^#]+)#Category=(?<category>[^#]+)#Index=(?<index>\d+)#Offset=(?<offset>[^#]+)$",
                RegexOptions.CultureInvariant);
            if (match.Success)
            {
                return new CreatorNoteNavigationTarget
                {
                    IsRecognized = true,
                    Kind = "EEX区段",
                    Scope = scope,
                    TargetKey = target,
                    FileName = match.Groups["file"].Value,
                    Name = match.Groups["file"].Value,
                    Category = match.Groups["category"].Value,
                    SectionIndex = ParseInt(match.Groups["index"].Value),
                    OffsetHex = NormalizeHex(match.Groups["offset"].Value),
                    DisplayText = $"EEX区段：{match.Groups["category"].Value}/{match.Groups["file"].Value} / #{match.Groups["index"].Value} / {NormalizeHex(match.Groups["offset"].Value)}"
                };
            }
        }

        if (scope.Contains("游戏资源", StringComparison.Ordinal) ||
            scope.Contains("EEX资源", StringComparison.Ordinal) ||
            scope.Contains("Ls/E5资源", StringComparison.Ordinal))
        {
            var slash = target.IndexOf('/');
            if (slash > 0 && slash < target.Length - 1)
            {
                var kind = scope.Contains("EEX资源", StringComparison.Ordinal)
                    ? "EEX资源"
                    : scope.Contains("Ls/E5资源", StringComparison.Ordinal)
                        ? "Ls/E5资源"
                        : "游戏资源";
                return new CreatorNoteNavigationTarget
                {
                    IsRecognized = true,
                    Kind = kind,
                    Scope = scope,
                    TargetKey = target,
                    Category = target[..slash],
                    Name = target[(slash + 1)..],
                    DisplayText = $"{kind}：{target[..slash]} / {target[(slash + 1)..]}"
                };
            }
        }

        if (scope.Contains("备份", StringComparison.Ordinal) || scope.Contains("差异", StringComparison.Ordinal))
        {
            if (target.StartsWith("Diff#", StringComparison.OrdinalIgnoreCase))
            {
                return new CreatorNoteNavigationTarget
                {
                    IsRecognized = true,
                    Kind = "差异",
                    Scope = scope,
                    TargetKey = target,
                    RelativePath = target["Diff#".Length..],
                    DisplayText = "测试副本差异：" + target["Diff#".Length..]
                };
            }

            if (target.StartsWith("Backup#", StringComparison.OrdinalIgnoreCase))
            {
                var parts = target.Split('#', 3);
                return new CreatorNoteNavigationTarget
                {
                    IsRecognized = true,
                    Kind = "备份",
                    Scope = scope,
                    TargetKey = target,
                    RelativePath = parts.Length >= 3 ? parts[2] : string.Empty,
                    DisplayText = "备份历史：" + (parts.Length >= 3 ? parts[2] : target)
                };
            }
        }

        return Unknown(scope, target);
    }

    private static CreatorNoteNavigationTarget Unknown(string scope, string target) => new()
    {
        IsRecognized = false,
        Kind = "未知",
        Scope = scope,
        TargetKey = target,
        DisplayText = string.IsNullOrWhiteSpace(target)
            ? "备注没有目标键，无法自动定位。"
            : $"暂不支持自动定位：{scope} / {target}"
    };

    private static int? ParseInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static bool IsScenarioCommandScope(string scope)
        => scope.Contains("R/S命令", StringComparison.Ordinal) ||
           scope.Contains("SV命令", StringComparison.Ordinal);

    private static bool IsScenarioTextScope(string scope)
        => scope.Contains("R/S文本", StringComparison.Ordinal) ||
           scope.Contains("SV文本", StringComparison.Ordinal);

    private static string NormalizeHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
            ? "0x" + parsed.ToString("X6", CultureInfo.InvariantCulture)
            : "0x" + value.ToUpperInvariant();
    }
}
