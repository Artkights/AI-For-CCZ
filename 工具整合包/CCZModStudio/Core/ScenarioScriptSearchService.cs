using System.Globalization;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ScenarioScriptSearchService
{
    private static readonly Regex DerivedDisplayRegex = new(@"(?<![\w-])(?<id>-?\d+)\((?<name>[^()\r\n]+)\)", RegexOptions.Compiled);

    public IReadOnlyList<ScenarioSearchResultRow> Search(
        string keyword,
        ScenarioStructureProbeResult? structure,
        IReadOnlyList<ScenarioTextEntry> texts)
        => Search(keyword, structure, texts, null);

    public IReadOnlyList<ScenarioSearchResultRow> Search(
        string keyword,
        ScenarioStructureProbeResult? structure,
        IReadOnlyList<ScenarioTextEntry> texts,
        Func<ScenarioTextEntry, IReadOnlyList<ScenarioStructureRow>>? relatedCommandResolver)
        => Search(keyword, structure, texts, relatedCommandResolver, null);

    public IReadOnlyList<ScenarioSearchResultRow> Search(
        string keyword,
        ScenarioStructureProbeResult? structure,
        IReadOnlyList<ScenarioTextEntry> texts,
        Func<ScenarioTextEntry, IReadOnlyList<ScenarioStructureRow>>? relatedCommandResolver,
        Func<ScenarioStructureRow, LegacyScenarioCommandNode?>? commandResolver)
    {
        keyword = keyword.Trim();
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<ScenarioSearchResultRow>();

        var rows = new List<ScenarioSearchResultRow>();
        if (structure != null)
        {
            foreach (var command in structure.Rows.Where(IsCommandSearchRow))
            {
                var matches = BuildCommandMatches(command, keyword, commandResolver?.Invoke(command));
                if (matches.Count == 0) continue;

                rows.Add(new ScenarioSearchResultRow
                {
                    Index = rows.Count + 1,
                    Kind = "Command",
                    Location = $"Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex} / {command.OffsetHex}",
                    Name = $"{command.CommandIdHex} {command.CommandName}",
                    Preview = BuildMatchPreview(matches, FirstNonEmpty(command.ParameterPreview, command.RawContextWordsHex, command.CommandTemplateHint, command.ReferenceHint)),
                    MatchCount = matches.Count,
                    ReplaceableMatchCount = matches.Count(IsReplaceableMatch),
                    Annotation = command.Annotation,
                    ActionHint = "Double-click to locate. Derived display names are highlighted only and are not replaced.",
                    Matches = matches,
                    RelatedCommandRows = new[] { command },
                    CommandRow = command
                });
            }
        }

        foreach (var text in texts)
        {
            var matches = BuildTextMatches(text, keyword);
            if (matches.Count == 0) continue;
            var relatedRows = relatedCommandResolver?.Invoke(text) ?? Array.Empty<ScenarioStructureRow>();

            rows.Add(new ScenarioSearchResultRow
            {
                Index = rows.Count + 1,
                Kind = "Text",
                Location = $"Text #{text.Index} / {text.Kind} / {text.OffsetHex}",
                Name = text.Kind,
                Preview = BuildMatchPreview(matches, FirstNonEmpty(text.Preview, text.Text)),
                MatchCount = matches.Count,
                ReplaceableMatchCount = matches.Count(IsReplaceableMatch),
                Annotation = text.Annotation,
                ActionHint = "Double-click to locate the text entry. Legacy text parameters are saved by full script save.",
                Matches = matches,
                RelatedCommandRows = relatedRows,
                TextEntry = text
            });
        }

        return rows;
    }

    private static bool IsCommandSearchRow(ScenarioStructureRow row)
        => row.Level == 2 &&
           (!string.IsNullOrWhiteSpace(row.CommandIdHex) || !string.IsNullOrWhiteSpace(row.CommandName));

    private static IReadOnlyList<ScenarioSearchMatch> BuildCommandMatches(
        ScenarioStructureRow row,
        string keyword,
        LegacyScenarioCommandNode? command)
    {
        var matches = new List<ScenarioSearchMatch>();
        AddMatches(matches, "Command name", row.CommandName, keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.CommandIdentity);
        AddMatches(matches, "Command id", row.CommandIdHex, keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.CommandIdentity);
        AddMatches(
            matches,
            "Parameter preview",
            row.ParameterPreview,
            keyword,
            isReplaceable: false,
            protectionKind: InferParameterPreviewProtection(row.ParameterPreview, keyword),
            protectionDetail: BuildParameterPreviewProtectionDetail(row.ParameterPreview, keyword));
        AddMatches(matches, "Raw context", row.RawContextWordsHex, keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.StructureField);
        AddMatches(matches, "Legacy layout", row.LegacyParameterLayout, keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.StructureField);
        AddMatches(matches, "Template", row.CommandTemplateHint, keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.StructureField);
        AddMatches(matches, "Reference hint", row.ReferenceHint, keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.StructureField);
        AddMatches(matches, "Annotation", row.Annotation, keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.StructureField);
        AddMatches(matches, "Offset", row.OffsetHex, keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.StructureField);
        AddMatches(matches, "Scene", row.SceneIndex.ToString(CultureInfo.InvariantCulture), keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.CommandIdentity);
        AddMatches(matches, "Section", row.SectionIndex.ToString(CultureInfo.InvariantCulture), keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.CommandIdentity);
        if (command != null)
        {
            AddCommandParameterMatches(matches, command, keyword);
        }
        else if (!string.IsNullOrWhiteSpace(row.ParameterPreview))
        {
            MarkUnboundParameterPreviewMatches(matches, row.ParameterPreview);
        }
        return matches;
    }

    private static IReadOnlyList<ScenarioSearchMatch> BuildTextMatches(ScenarioTextEntry text, string keyword)
    {
        var matches = new List<ScenarioSearchMatch>();
        AddMatches(matches, ScenarioSearchMatch.TextFieldName, text.Text, keyword, text.IsWritable, ScenarioSearchReplaceTarget.TextEntryText);
        AddMatches(matches, ScenarioSearchMatch.PreviewFieldName, text.Preview, keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.StructureField);
        AddMatches(matches, "Type", text.Kind, keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.StructureField);
        AddMatches(matches, "Annotation", text.Annotation, keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.StructureField);
        AddMatches(matches, "Offset", text.OffsetHex, keyword, isReplaceable: false, protectionKind: ScenarioSearchProtectionKind.StructureField);
        return matches;
    }

    private static void AddCommandParameterMatches(
        List<ScenarioSearchMatch> matches,
        LegacyScenarioCommandNode command,
        string keyword)
    {
        foreach (var parameter in command.Parameters)
        {
            switch (parameter.Kind)
            {
                case LegacyScenarioParameterKind.Text:
                    AddMatches(
                        matches,
                        $"Text parameter P{parameter.Index + 1}",
                        parameter.Text,
                        keyword,
                        isReplaceable: true,
                        ScenarioSearchReplaceTarget.CommandTextParameter,
                        parameter);
                    break;
                case LegacyScenarioParameterKind.Word16:
                case LegacyScenarioParameterKind.Dword32:
                    AddMatches(
                        matches,
                        $"Scalar parameter P{parameter.Index + 1}",
                        parameter.IntValue.ToString(CultureInfo.InvariantCulture),
                        keyword,
                        isReplaceable: true,
                        ScenarioSearchReplaceTarget.CommandScalarParameter,
                        parameter);
                    break;
            }
        }
    }

    private static void AddMatches(
        List<ScenarioSearchMatch> matches,
        string fieldName,
        string? fieldText,
        string keyword,
        bool isReplaceable,
        ScenarioSearchReplaceTarget replaceTarget = ScenarioSearchReplaceTarget.None,
        LegacyScenarioCommandParameter? commandParameter = null,
        ScenarioSearchProtectionKind protectionKind = ScenarioSearchProtectionKind.None,
        string protectionDetail = "")
    {
        if (string.IsNullOrEmpty(fieldText) || string.IsNullOrEmpty(keyword))
        {
            return;
        }

        var start = 0;
        while (start < fieldText.Length)
        {
            var index = fieldText.IndexOf(keyword, start, StringComparison.CurrentCultureIgnoreCase);
            if (index < 0)
            {
                break;
            }

            var actualProtectionKind = isReplaceable
                ? ScenarioSearchProtectionKind.None
                : NormalizeProtectionKind(fieldText, index, keyword.Length, protectionKind);
            matches.Add(new ScenarioSearchMatch
            {
                FieldName = fieldName,
                FieldText = fieldText,
                Start = index,
                Length = keyword.Length,
                Text = fieldText.Substring(index, keyword.Length),
                IsReplaceable = isReplaceable,
                ReplaceTarget = isReplaceable ? replaceTarget : ScenarioSearchReplaceTarget.None,
                ProtectionKind = actualProtectionKind,
                ProtectionDetail = isReplaceable
                    ? string.Empty
                    : BuildProtectionDetail(fieldText, index, keyword.Length, actualProtectionKind, protectionDetail),
                CommandParameter = commandParameter
            });
            start = index + Math.Max(1, keyword.Length);
        }
    }

    private static ScenarioSearchProtectionKind InferParameterPreviewProtection(string? text, string keyword)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword))
        {
            return ScenarioSearchProtectionKind.StructureField;
        }

        return ContainsDerivedDisplayNameHit(text, keyword)
            ? ScenarioSearchProtectionKind.DerivedDisplayName
            : ScenarioSearchProtectionKind.StructureField;
    }

    private static string BuildParameterPreviewProtectionDetail(string? text, string keyword)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword))
        {
            return string.Empty;
        }

        foreach (Match match in DerivedDisplayRegex.Matches(text))
        {
            var name = match.Groups["name"];
            if (name.Value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
            {
                var id = match.Groups["id"].Value;
                return $"Derived display name from actor/reference id {id}; edit the name table to rename it, or replace/edit numeric value {id} to change the reference.";
            }
        }

        return string.Empty;
    }

    private static bool ContainsDerivedDisplayNameHit(string text, string keyword)
    {
        foreach (Match match in DerivedDisplayRegex.Matches(text))
        {
            if (match.Groups["name"].Value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ScenarioSearchProtectionKind NormalizeProtectionKind(
        string fieldText,
        int start,
        int length,
        ScenarioSearchProtectionKind protectionKind)
    {
        if (protectionKind != ScenarioSearchProtectionKind.StructureField)
        {
            return protectionKind;
        }

        return IsInsideDerivedDisplayName(fieldText, start, length)
            ? ScenarioSearchProtectionKind.DerivedDisplayName
            : protectionKind;
    }

    private static bool IsInsideDerivedDisplayName(string text, int start, int length)
    {
        foreach (Match match in DerivedDisplayRegex.Matches(text))
        {
            var name = match.Groups["name"];
            if (start >= name.Index && start + length <= name.Index + name.Length)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildProtectionDetail(
        string fieldText,
        int start,
        int length,
        ScenarioSearchProtectionKind protectionKind,
        string fallback)
    {
        if (protectionKind != ScenarioSearchProtectionKind.DerivedDisplayName)
        {
            return fallback;
        }

        foreach (Match match in DerivedDisplayRegex.Matches(fieldText))
        {
            var name = match.Groups["name"];
            if (start >= name.Index && start + length <= name.Index + name.Length)
            {
                var id = match.Groups["id"].Value;
                return $"Derived display name from actor/reference id {id}; edit the name table to rename it, or replace/edit numeric value {id} to change the reference.";
            }
        }

        return string.IsNullOrWhiteSpace(fallback)
            ? "Derived display name; it is not stored as command text and is not replaced."
            : fallback;
    }

    private static void MarkUnboundParameterPreviewMatches(List<ScenarioSearchMatch> matches, string parameterPreview)
    {
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (!string.Equals(match.FieldText, parameterPreview, StringComparison.Ordinal))
            {
                continue;
            }

            if (match.IsReplaceable || match.ProtectionKind == ScenarioSearchProtectionKind.DerivedDisplayName)
            {
                continue;
            }

            matches[i] = new ScenarioSearchMatch
            {
                FieldName = match.FieldName,
                FieldText = match.FieldText,
                Start = match.Start,
                Length = match.Length,
                Text = match.Text,
                IsReplaceable = false,
                ReplaceTarget = ScenarioSearchReplaceTarget.None,
                ProtectionKind = ScenarioSearchProtectionKind.UnboundCommandParameter,
                ProtectionDetail = "Not bound to a real command parameter; highlighted only.",
                CommandParameter = match.CommandParameter
            };
        }
    }

    private static string BuildMatchPreview(IReadOnlyList<ScenarioSearchMatch> matches, string fallback)
    {
        var first = matches.FirstOrDefault();
        if (first == null)
        {
            return fallback;
        }

        var text = first.FieldText;
        if (text.Length <= 80)
        {
            return $"{first.FieldName}: {text}";
        }

        var start = Math.Max(0, first.Start - 24);
        var end = Math.Min(text.Length, first.Start + first.Length + 36);
        var prefix = start > 0 ? "..." : string.Empty;
        var suffix = end < text.Length ? "..." : string.Empty;
        return $"{first.FieldName}: {prefix}{text[start..end]}{suffix}";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "(none)";

    private static bool IsReplaceableMatch(ScenarioSearchMatch match)
        => match.IsReplaceable && match.ReplaceTarget != ScenarioSearchReplaceTarget.None;
}
