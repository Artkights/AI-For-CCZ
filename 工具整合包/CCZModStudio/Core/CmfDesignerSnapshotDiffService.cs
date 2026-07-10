using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CmfDesignerSnapshotDiffService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public CmfDesignerSnapshotDiffReport Compare(CczProject project, CmfDesignerSnapshot left, CmfDesignerSnapshot right)
    {
        var pageDiffs = CompareSets(
            left.Pages,
            right.Pages,
            page => NormalizeKey(page.Name),
            page => page.Name,
            (leftPage, rightPage) => leftPage.WindowTitle == rightPage.WindowTitle && leftPage.Bounds.Equals(rightPage.Bounds)
                ? string.Empty
                : $"WindowTitle/Bounds changed: {leftPage.WindowTitle} {leftPage.Bounds} -> {rightPage.WindowTitle} {rightPage.Bounds}",
            "Page").ToArray();

        var moduleDiffs = CompareModules(left, right).ToArray();

        var bindingDiffs = CompareBindings(left, right).ToArray();

        var report = new CmfDesignerSnapshotDiffReport
        {
            LeftRelativePath = left.RelativePath,
            RightRelativePath = right.RelativePath,
            LeftSha256 = left.SourceSha256,
            RightSha256 = right.SourceSha256,
            PageDiffs = pageDiffs,
            ModuleDiffs = moduleDiffs,
            BindingDiffs = bindingDiffs,
            Warnings = left.Warnings.Select(warning => "Left: " + warning)
                .Concat(right.Warnings.Select(warning => "Right: " + warning))
                .ToArray()
        };

        return WriteReport(project, report);
    }

    private static CmfDesignerSnapshotDiffReport WriteReport(CczProject project, CmfDesignerSnapshotDiffReport report)
    {
        var root = Path.Combine(
            project.WorkspaceRoot,
            "CCZModStudio_Reports",
            "CmfDesignerDiff",
            DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(root);
        report.ReportDirectory = root;
        report.JsonReportPath = Path.Combine(root, "designer-diff.json");
        report.MarkdownReportPath = Path.Combine(root, "designer-diff.md");
        File.WriteAllText(report.JsonReportPath, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(report.MarkdownReportPath, BuildMarkdown(report), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return report;
    }

    private static string BuildMarkdown(CmfDesignerSnapshotDiffReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# CheatMaker Designer Snapshot Diff");
        builder.AppendLine();
        builder.AppendLine($"- Left: `{report.LeftRelativePath}` `{report.LeftSha256}`");
        builder.AppendLine($"- Right: `{report.RightRelativePath}` `{report.RightSha256}`");
        builder.AppendLine();
        AppendDiffTable(builder, "Pages", report.PageDiffs);
        AppendDiffTable(builder, "Modules", report.ModuleDiffs);
        AppendDiffTable(builder, "Bindings", report.BindingDiffs);
        return builder.ToString();
    }

    private static void AppendDiffTable(StringBuilder builder, string title, IReadOnlyList<CmfDesignerSnapshotDiffItem> items)
    {
        builder.AppendLine("## " + title);
        builder.AppendLine();
        if (items.Count == 0)
        {
            builder.AppendLine("No differences.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Kind | Key | Left | Right | Detail |");
        builder.AppendLine("|---|---|---|---|---|");
        foreach (var item in items)
        {
            builder.AppendLine($"| {Cell(item.DiffKind)} | {Cell(item.Key)} | {Cell(item.LeftValue)} | {Cell(item.RightValue)} | {Cell(item.Detail)} |");
        }

        builder.AppendLine();
    }

    private static IEnumerable<CmfDesignerSnapshotDiffItem> CompareSets<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, string> keySelector,
        Func<T, string> valueSelector,
        Func<T, T, string> changeSelector,
        string label)
    {
        var leftMap = left.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rightMap = right.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var key in leftMap.Keys.Except(rightMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            yield return new CmfDesignerSnapshotDiffItem
            {
                DiffKind = "Removed" + label,
                Key = key,
                LeftValue = valueSelector(leftMap[key]),
                Detail = "Present only in left snapshot."
            };
        }

        foreach (var key in rightMap.Keys.Except(leftMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            yield return new CmfDesignerSnapshotDiffItem
            {
                DiffKind = "Added" + label,
                Key = key,
                RightValue = valueSelector(rightMap[key]),
                Detail = "Present only in right snapshot."
            };
        }

        foreach (var key in leftMap.Keys.Intersect(rightMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            var detail = changeSelector(leftMap[key], rightMap[key]);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                yield return new CmfDesignerSnapshotDiffItem
                {
                    DiffKind = "Changed" + label,
                    Key = key,
                    LeftValue = valueSelector(leftMap[key]),
                    RightValue = valueSelector(rightMap[key]),
                    Detail = detail
                };
            }
        }
    }

    private static IEnumerable<CmfDesignerSnapshotDiffItem> CompareModules(CmfDesignerSnapshot left, CmfDesignerSnapshot right)
    {
        var leftMap = left.Modules
            .GroupBy(module => BuildModuleKey(left, module), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rightMap = right.Modules
            .GroupBy(module => BuildModuleKey(right, module), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var key in leftMap.Keys.Except(rightMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            yield return new CmfDesignerSnapshotDiffItem
            {
                DiffKind = "RemovedModule",
                Key = key,
                LeftValue = BuildModuleValue(left, leftMap[key]),
                Detail = "Present only in left snapshot."
            };
        }

        foreach (var key in rightMap.Keys.Except(leftMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            yield return new CmfDesignerSnapshotDiffItem
            {
                DiffKind = "AddedModule",
                Key = key,
                RightValue = BuildModuleValue(right, rightMap[key]),
                Detail = "Present only in right snapshot."
            };
        }

        foreach (var key in leftMap.Keys.Intersect(rightMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            var detail = BuildModuleChangeDetail(leftMap[key], rightMap[key]);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                yield return new CmfDesignerSnapshotDiffItem
                {
                    DiffKind = "ChangedModule",
                    Key = key,
                    LeftValue = BuildModuleValue(left, leftMap[key]),
                    RightValue = BuildModuleValue(right, rightMap[key]),
                    Detail = detail
                };
            }
        }
    }

    private static string BuildModuleKey(CmfDesignerSnapshot snapshot, CmfDesignerModule module)
        => NormalizeKey(FindPageName(snapshot, module.PageId) + "|" + module.Title);

    private static string BuildModuleValue(CmfDesignerSnapshot snapshot, CmfDesignerModule module)
        => FindPageName(snapshot, module.PageId) + " / " + module.Title;

    private static string BuildModuleChangeDetail(CmfDesignerModule left, CmfDesignerModule right)
    {
        var leftNotes = string.Join("\n", left.Notes.Select(note => note.Text));
        var rightNotes = string.Join("\n", right.Notes.Select(note => note.Text));
        return leftNotes == rightNotes && left.Bounds.Equals(right.Bounds)
            ? string.Empty
            : $"Notes/Bounds changed: {leftNotes} {left.Bounds} -> {rightNotes} {right.Bounds}";
    }

    private static IEnumerable<CmfDesignerSnapshotDiffItem> CompareBindings(CmfDesignerSnapshot left, CmfDesignerSnapshot right)
    {
        var leftRemaining = left.Bindings.ToList();
        var rightRemaining = right.Bindings.ToList();
        var matched = new List<(string Key, CmfDesignerBinding Left, CmfDesignerBinding Right)>();

        MatchBindingsByKey(left, right, leftRemaining, rightRemaining, matched, BuildPageModuleControlBindingKey);
        MatchBindingsByKey(left, right, leftRemaining, rightRemaining, matched, BuildPageControlBindingKey);
        MatchBindingsByKey(left, right, leftRemaining, rightRemaining, matched, BuildDisplayAddressBindingKey);
        MatchBindingsByKey(left, right, leftRemaining, rightRemaining, matched, BuildUniqueControlBindingKey);

        var leftMap = BuildRemainingBindingMap(left, leftRemaining);
        var rightMap = BuildRemainingBindingMap(right, rightRemaining);

        foreach (var key in leftMap.Keys.Except(rightMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            yield return new CmfDesignerSnapshotDiffItem
            {
                DiffKind = "RemovedBinding",
                Key = key,
                LeftValue = BuildBindingValue(left, leftMap[key]),
                Detail = "Present only in left snapshot."
            };
        }

        foreach (var key in rightMap.Keys.Except(leftMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            yield return new CmfDesignerSnapshotDiffItem
            {
                DiffKind = "AddedBinding",
                Key = key,
                RightValue = BuildBindingValue(right, rightMap[key]),
                Detail = "Present only in right snapshot."
            };
        }

        foreach (var pair in matched.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var detail = BuildBindingChangeDetail(pair.Left, pair.Right);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                yield return new CmfDesignerSnapshotDiffItem
                {
                    DiffKind = "ChangedBinding",
                    Key = pair.Key,
                    LeftValue = BuildBindingValue(left, pair.Left),
                    RightValue = BuildBindingValue(right, pair.Right),
                    Detail = detail
                };
            }
        }
    }

    private static void MatchBindingsByKey(
        CmfDesignerSnapshot left,
        CmfDesignerSnapshot right,
        List<CmfDesignerBinding> leftRemaining,
        List<CmfDesignerBinding> rightRemaining,
        List<(string Key, CmfDesignerBinding Left, CmfDesignerBinding Right)> matched,
        Func<CmfDesignerSnapshot, CmfDesignerBinding, string> keySelector)
    {
        var leftMap = BuildUniqueBindingMap(left, leftRemaining, keySelector);
        var rightMap = BuildUniqueBindingMap(right, rightRemaining, keySelector);
        foreach (var key in leftMap.Keys.Intersect(rightMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            var leftBinding = leftMap[key];
            var rightBinding = rightMap[key];
            matched.Add((key, leftBinding, rightBinding));
            leftRemaining.Remove(leftBinding);
            rightRemaining.Remove(rightBinding);
        }
    }

    private static Dictionary<string, CmfDesignerBinding> BuildUniqueBindingMap(
        CmfDesignerSnapshot snapshot,
        IReadOnlyList<CmfDesignerBinding> bindings,
        Func<CmfDesignerSnapshot, CmfDesignerBinding, string> keySelector)
        => bindings
            .Select(binding => new { Key = keySelector(snapshot, binding), Binding = binding })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Binding, StringComparer.OrdinalIgnoreCase);

    private static string BuildPageModuleControlBindingKey(CmfDesignerSnapshot snapshot, CmfDesignerBinding binding)
    {
        var pageName = FindPageName(snapshot, binding.PageId);
        var moduleTitle = FindModuleTitle(snapshot, binding.ModuleId);
        var controlName = binding.ControlName?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(pageName) &&
            !string.IsNullOrWhiteSpace(moduleTitle) &&
            !string.IsNullOrWhiteSpace(controlName))
        {
            return NormalizeKey(pageName + "|" + moduleTitle + "|" + controlName);
        }

        return string.Empty;
    }

    private static string BuildPageControlBindingKey(CmfDesignerSnapshot snapshot, CmfDesignerBinding binding)
    {
        var pageName = FindPageName(snapshot, binding.PageId);
        var controlName = binding.ControlName?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(pageName) && !string.IsNullOrWhiteSpace(controlName))
        {
            return NormalizeKey(pageName + "|" + controlName);
        }

        return string.Empty;
    }

    private static string BuildDisplayAddressBindingKey(CmfDesignerSnapshot snapshot, CmfDesignerBinding binding)
    {
        var displayName = string.IsNullOrWhiteSpace(binding.DisplayName)
            ? binding.BindingId
            : binding.DisplayName;
        return string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(binding.UeOffsetHex)
            ? string.Empty
            : NormalizeKey(displayName + "|" + binding.UeOffsetHex);
    }

    private static string BuildUniqueControlBindingKey(CmfDesignerSnapshot snapshot, CmfDesignerBinding binding)
        => string.IsNullOrWhiteSpace(binding.ControlName)
            ? string.Empty
            : NormalizeKey("control|" + binding.ControlName);

    private static string BuildFallbackBindingKey(CmfDesignerSnapshot snapshot, CmfDesignerBinding binding)
    {
        var strictKey = BuildPageModuleControlBindingKey(snapshot, binding);
        if (!string.IsNullOrWhiteSpace(strictKey)) return strictKey;

        var pageControlKey = BuildPageControlBindingKey(snapshot, binding);
        if (!string.IsNullOrWhiteSpace(pageControlKey)) return pageControlKey;

        var displayAddressKey = BuildDisplayAddressBindingKey(snapshot, binding);
        if (!string.IsNullOrWhiteSpace(displayAddressKey)) return displayAddressKey;

        return NormalizeKey(binding.BindingId);
    }

    private static Dictionary<string, CmfDesignerBinding> BuildRemainingBindingMap(CmfDesignerSnapshot snapshot, IReadOnlyList<CmfDesignerBinding> bindings)
    {
        var result = new Dictionary<string, CmfDesignerBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in bindings.GroupBy(binding => BuildFallbackBindingKey(snapshot, binding), StringComparer.OrdinalIgnoreCase))
        {
            var items = group.ToArray();
            if (items.Length == 1)
            {
                result[group.Key] = items[0];
                continue;
            }

            for (var index = 0; index < items.Length; index++)
            {
                result[group.Key + "#" + (index + 1).ToString("D3")] = items[index];
            }
        }

        return result;
    }

    private static string BuildBindingValue(CmfDesignerSnapshot snapshot, CmfDesignerBinding binding)
        => string.Join(" / ", new[]
            {
                FindPageName(snapshot, binding.PageId),
                FindModuleTitle(snapshot, binding.ModuleId),
                binding.ControlName,
                binding.DisplayName,
                binding.UeOffsetHex
            }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string BuildBindingChangeDetail(CmfDesignerBinding left, CmfDesignerBinding right)
    {
        var changes = new List<string>();
        AddIfChanged(changes, "UE", left.UeOffsetHex, right.UeOffsetHex);
        AddIfChanged(changes, "Length", left.ByteLength.ToString(), right.ByteLength.ToString());
        AddIfChanged(changes, "DataType", left.DataType, right.DataType);
        AddIfChanged(changes, "FunctionType", left.FunctionType, right.FunctionType);
        AddIfChanged(changes, "Default", left.DefaultValueRaw, right.DefaultValueRaw);
        AddIfChanged(changes, "DataList", left.DataListRaw, right.DataListRaw);
        return string.Join("; ", changes);
    }

    private static void AddIfChanged(List<string> changes, string label, string left, string right)
    {
        if (!string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal))
        {
            changes.Add($"{label}: {left} -> {right}");
        }
    }

    private static string FindPageName(CmfDesignerSnapshot snapshot, string pageId)
        => snapshot.Pages.FirstOrDefault(page => page.PageId.Equals(pageId, StringComparison.OrdinalIgnoreCase))?.Name ?? pageId;

    private static string FindModuleTitle(CmfDesignerSnapshot snapshot, string moduleId)
        => snapshot.Modules.FirstOrDefault(module => module.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))?.Title ?? moduleId;

    private static string NormalizeKey(string value)
        => string.Join(" ", (value ?? string.Empty).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string Cell(string value)
        => (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
}
