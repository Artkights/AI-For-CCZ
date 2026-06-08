using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;

namespace CCZModStudio.Core;

public sealed class ScriptVariableUsageService
{
    private static readonly string[] VariableKind =
    [
        "指针变量(*p)",
        "指针变量(p)",
        "整型变量"
    ];

    private static readonly string[] VariableKind2 =
    [
        "常数",
        "指针变量(*p)",
        "指针变量(p)",
        "指针变量(&p)",
        "整型变量(a)",
        "整型变量(&a)"
    ];

    private static readonly string[] Operate2 =
    [
        "+=",
        "-=",
        "=",
        "*=",
        "/=",
        "%=",
        "M="
    ];

    private static readonly string[] Compare2 =
    [
        "==",
        ">=",
        "<"
    ];

    public ScriptVariableUsageSnapshot BuildCurrentScenarioSnapshot(LegacyScenarioDocument document)
    {
        var occurrences = ExtractOccurrences(document).ToList();
        return BuildSnapshot(occurrences, document.FileName);
    }

    public ScriptVariableProjectScanResult ScanProject(
        CczProject project,
        SceneStringDocument dictionary,
        IProgress<ScriptVariableProjectScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var scenarios = new ScenarioFileReader()
            .ReadAllIndex(project)
            .Where(x => ScenarioFileReader.IsRsScriptFile(x.FileName))
            .OrderBy(x => ScenarioFileReader.IsBattlefieldScriptFile(x.FileName) ? 1 : 0)
            .ThenBy(x => int.TryParse(x.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : int.MaxValue)
            .ThenBy(x => x.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var reader = new LegacyScenarioReader();
        var allOccurrences = new List<ScriptVariableOccurrence>();
        var failures = new List<string>();

        for (var i = 0; i < scenarios.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scenario = scenarios[i];
            progress?.Report(new ScriptVariableProjectScanProgress
            {
                Completed = i,
                Total = scenarios.Count,
                CurrentScenario = scenario.FileName
            });

            try
            {
                var document = reader.Read(scenario.Path, dictionary);
                allOccurrences.AddRange(ExtractOccurrences(document));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                failures.Add($"{scenario.FileName}: {ex.Message}");
            }
        }

        progress?.Report(new ScriptVariableProjectScanProgress
        {
            Completed = scenarios.Count,
            Total = scenarios.Count,
            CurrentScenario = string.Empty
        });

        var snapshot = BuildSnapshot(allOccurrences, "当前项目");
        return new ScriptVariableProjectScanResult
        {
            Summaries = snapshot.Summaries,
            Occurrences = snapshot.Occurrences,
            SourceLabel = snapshot.SourceLabel,
            BuiltAt = snapshot.BuiltAt,
            ScannedScenarioCount = scenarios.Count - failures.Count,
            FailedScenarios = failures
        };
    }

    public IReadOnlyList<ScriptVariableOccurrence> ExtractOccurrences(LegacyScenarioDocument document)
    {
        var rows = new List<ScriptVariableOccurrence>();
        foreach (var command in document.EnumerateCommands())
        {
            AddCommandOccurrences(document, command, rows);
        }

        return rows;
    }

    public static ScriptVariableUsageSnapshot BuildSnapshot(
        IReadOnlyList<ScriptVariableOccurrence> occurrences,
        string sourceLabel)
    {
        var summaries = occurrences
            .GroupBy(x => x.SummaryKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new ScriptVariableSummary
                {
                    VariableType = first.VariableType,
                    VariableAddress = first.VariableAddress,
                    Scope = first.Scope,
                    OccurrenceCount = group.Count(),
                    ScenarioCount = group.Select(x => x.ScenarioFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    AccessKinds = string.Join(" / ", group.Select(x => x.AccessKind).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
                    CommandIds = string.Join(" / ", group.Select(x => x.CommandIdHex).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
                    FirstScenario = group.Select(x => x.ScenarioFileName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                    HasEditableOccurrence = group.Any(x => x.CanEdit)
                };
            })
            .OrderBy(x => GetVariableTypeSortKey(x.VariableType))
            .ThenBy(x => x.Scope, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.VariableAddress)
            .ThenBy(x => x.VariableType, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new ScriptVariableUsageSnapshot
        {
            Summaries = summaries,
            Occurrences = occurrences
                .OrderBy(x => x.ScenarioFileName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.SceneIndex)
                .ThenBy(x => x.SectionIndex)
                .ThenBy(x => x.CommandOrdinal)
                .ThenBy(x => x.ParameterIndex)
                .ToList(),
            SourceLabel = sourceLabel,
            BuiltAt = DateTime.Now
        };
    }

    private static void AddCommandOccurrences(
        LegacyScenarioDocument document,
        LegacyScenarioCommandNode command,
        List<ScriptVariableOccurrence> rows)
    {
        switch (command.CommandId)
        {
            case 0x05:
                AddBooleanTestOccurrences(document, command, rows);
                break;
            case 0x0B:
                AddBooleanAssignOccurrence(document, command, rows);
                break;
            case 0x77:
                AddVariableOperationOccurrences(document, command, rows);
                break;
            case 0x78:
                AddIntegerVariableAssignOccurrence(document, command, rows);
                break;
            case 0x79:
                AddVariableTestOccurrences(document, command, rows);
                break;
        }
    }

    private static void AddBooleanTestOccurrences(
        LegacyScenarioDocument document,
        LegacyScenarioCommandNode command,
        List<ScriptVariableOccurrence> rows)
    {
        var trueParameter = GetParameter(command, 0);
        var falseParameter = GetParameter(command, 1);
        if (trueParameter != null && trueParameter.Kind == LegacyScenarioParameterKind.VariableArray)
        {
            for (var i = 0; i < trueParameter.Values.Count; i++)
            {
                rows.Add(CreateOccurrence(
                    document,
                    command,
                    trueParameter,
                    "布尔变量",
                    trueParameter.Values[i],
                    "true变量",
                    "测试(true)",
                    $"数组第 {i + 1} 项",
                    canEdit: true));
            }
        }

        if (falseParameter != null && falseParameter.Kind == LegacyScenarioParameterKind.VariableArray)
        {
            for (var i = 0; i < falseParameter.Values.Count; i++)
            {
                rows.Add(CreateOccurrence(
                    document,
                    command,
                    falseParameter,
                    "布尔变量",
                    falseParameter.Values[i],
                    "false变量",
                    "测试(false)",
                    $"数组第 {i + 1} 项",
                    canEdit: true));
            }
        }
    }

    private static void AddBooleanAssignOccurrence(
        LegacyScenarioDocument document,
        LegacyScenarioCommandNode command,
        List<ScriptVariableOccurrence> rows)
    {
        var variableParameter = GetParameter(command, 0);
        if (variableParameter == null) return;
        var value = GetParameterValue(command, 1);
        rows.Add(CreateOccurrence(
            document,
            command,
            variableParameter,
            "布尔变量",
            variableParameter.IntValue,
            "变量",
            "赋值",
            value.HasValue ? $"赋值={(value.Value != 0 ? "true" : "false")}" : string.Empty,
            canEdit: true));
    }

    private static void AddVariableOperationOccurrences(
        LegacyScenarioDocument document,
        LegacyScenarioCommandNode command,
        List<ScriptVariableOccurrence> rows)
    {
        var leftKind = GetParameterValue(command, 0);
        var leftAddress = GetParameter(command, 1);
        var op = GetParameterValue(command, 2);
        var rightKind = GetParameterValue(command, 3);
        var rightAddress = GetParameter(command, 4);
        var opText = Safe(Operate2, op);

        if (leftKind.HasValue && IsVariableKind(leftKind.Value) && leftAddress != null)
        {
            rows.Add(CreateOccurrence(
                document,
                command,
                leftAddress,
                Safe(VariableKind, leftKind),
                leftAddress.IntValue,
                "左变量",
                "运算写入",
                string.IsNullOrWhiteSpace(opText) ? string.Empty : $"操作={opText}",
                canEdit: true));
        }

        if (rightKind.HasValue && IsVariableKind2(rightKind.Value) && rightAddress != null)
        {
            rows.Add(CreateOccurrence(
                document,
                command,
                rightAddress,
                Safe(VariableKind2, rightKind),
                rightAddress.IntValue,
                "右变量",
                "运算读取",
                string.IsNullOrWhiteSpace(opText) ? string.Empty : $"被用于 {opText}",
                canEdit: true));
        }
    }

    private static void AddIntegerVariableAssignOccurrence(
        LegacyScenarioDocument document,
        LegacyScenarioCommandNode command,
        List<ScriptVariableOccurrence> rows)
    {
        var variableParameter = GetParameter(command, 0);
        if (variableParameter == null) return;
        var operation = GetParameterValue(command, 1);
        var person = GetParameterValue(command, 2);
        var condition = GetParameterValue(command, 3);
        var related = new List<string>();
        if (operation.HasValue) related.Add("操作=" + operation.Value.ToString(CultureInfo.InvariantCulture));
        if (person.HasValue) related.Add("武将=" + person.Value.ToString(CultureInfo.InvariantCulture));
        if (condition.HasValue) related.Add("属性=" + condition.Value.ToString(CultureInfo.InvariantCulture));

        rows.Add(CreateOccurrence(
            document,
            command,
            variableParameter,
            "整型变量",
            variableParameter.IntValue,
            "整型变量",
            "赋值",
            string.Join(" / ", related),
            canEdit: true));
    }

    private static void AddVariableTestOccurrences(
        LegacyScenarioDocument document,
        LegacyScenarioCommandNode command,
        List<ScriptVariableOccurrence> rows)
    {
        var leftKind = GetParameterValue(command, 0);
        var leftAddress = GetParameter(command, 1);
        var compare = GetParameterValue(command, 2);
        var rightKind = GetParameterValue(command, 3);
        var rightAddress = GetParameter(command, 4);
        var compareText = Safe(Compare2, compare);

        if (leftKind.HasValue && IsVariableKind2(leftKind.Value) && leftAddress != null)
        {
            rows.Add(CreateOccurrence(
                document,
                command,
                leftAddress,
                Safe(VariableKind2, leftKind),
                leftAddress.IntValue,
                "左变量",
                "测试读取",
                string.IsNullOrWhiteSpace(compareText) ? string.Empty : $"比较={compareText}",
                canEdit: true));
        }

        if (rightKind.HasValue && IsVariableKind2(rightKind.Value) && rightAddress != null)
        {
            rows.Add(CreateOccurrence(
                document,
                command,
                rightAddress,
                Safe(VariableKind2, rightKind),
                rightAddress.IntValue,
                "右变量",
                "测试读取",
                string.IsNullOrWhiteSpace(compareText) ? string.Empty : $"比较={compareText}",
                canEdit: true));
        }
    }

    private static ScriptVariableOccurrence CreateOccurrence(
        LegacyScenarioDocument document,
        LegacyScenarioCommandNode command,
        LegacyScenarioCommandParameter parameter,
        string variableType,
        int variableAddress,
        string slot,
        string accessKind,
        string relatedValue,
        bool canEdit)
        => new()
        {
            ScenarioFileName = document.FileName,
            ScenarioPath = document.FilePath,
            SceneIndex = command.SceneIndex,
            SectionIndex = command.SectionIndex,
            CommandIndex = command.CommandIndex,
            CommandOrdinal = command.CommandOrdinal,
            CommandId = command.CommandId,
            CommandName = command.CommandName,
            VariableType = variableType,
            VariableAddress = variableAddress,
            Scope = GetVariableScope(document.FileName),
            ParameterIndex = parameter.Index,
            ParameterSlot = slot,
            AccessKind = accessKind,
            RelatedValue = relatedValue,
            CommandOffset = command.FileOffset,
            ParameterOffset = parameter.FileOffset,
            CanEdit = canEdit && CanEditVariableCommand(command.CommandId),
            EditHint = canEdit && CanEditVariableCommand(command.CommandId)
                ? "可用旧版 Dialog 修改该命令参数。"
                : "该命令暂无可用旧版 Dialog，只能定位查看。"
        };

    private static bool CanEditVariableCommand(int commandId)
        => commandId is 0x05 or 0x0B or 0x77 or 0x78 or 0x79;

    private static string GetVariableScope(string fileName)
    {
        if (ScenarioFileReader.IsBattlefieldScriptFile(fileName)) return "S战场";
        return ScenarioFileReader.IsRsScriptFile(fileName) ? "R剧本" : "未知";
    }

    private static LegacyScenarioCommandParameter? GetParameter(LegacyScenarioCommandNode command, int index)
        => index >= 0 && index < command.Parameters.Count ? command.Parameters[index] : null;

    private static int? GetParameterValue(LegacyScenarioCommandNode command, int index)
        => GetParameter(command, index)?.IntValue;

    private static bool IsVariableKind2(int kind)
        => kind is >= 1 and <= 5;

    private static bool IsVariableKind(int kind)
        => kind is >= 0 and <= 2;

    private static string Safe(IReadOnlyList<string> values, int? index)
        => index.HasValue && index.Value >= 0 && index.Value < values.Count
            ? values[index.Value]
            : index?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static int GetVariableTypeSortKey(string variableType)
    {
        if (variableType.Contains("布尔", StringComparison.Ordinal)) return 0;
        if (variableType.Contains("整型", StringComparison.Ordinal)) return 1;
        if (variableType.Contains("指针", StringComparison.Ordinal)) return 2;
        return 9;
    }
}
