using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ScenarioScriptSearchService
{
    public IReadOnlyList<ScenarioSearchResultRow> Search(
        string keyword,
        ScenarioStructureProbeResult? structure,
        IReadOnlyList<ScenarioTextEntry> texts)
    {
        keyword = keyword.Trim();
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<ScenarioSearchResultRow>();

        var rows = new List<ScenarioSearchResultRow>();
        if (structure != null)
        {
            foreach (var command in structure.Rows.Where(x => x.NodeType == "Command候选" && Matches(x, keyword)))
            {
                rows.Add(new ScenarioSearchResultRow
                {
                    Index = rows.Count + 1,
                    Kind = "命令",
                    Location = $"Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex} / {command.OffsetHex}",
                    Name = $"{command.CommandIdHex} {command.CommandName}",
                    Preview = FirstNonEmpty(command.ParameterPreview, command.RawContextWordsHex, command.CommandTemplateHint, command.ReferenceHint),
                    Annotation = command.Annotation,
                    ActionHint = "双击或选中后自动定位到左侧树和命令列表；可继续复制候选或补充核对记录。",
                    CommandRow = command
                });
            }
        }

        foreach (var text in texts.Where(x => Matches(x, keyword)))
        {
            rows.Add(new ScenarioSearchResultRow
            {
                Index = rows.Count + 1,
                Kind = "文本",
                Location = $"文本 #{text.Index} / {text.Kind} / {text.OffsetHex}",
                Name = text.Kind,
                Preview = FirstNonEmpty(text.Preview, text.Text),
                Annotation = text.Annotation,
                ActionHint = "双击或选中后定位到文本线索；可在右侧编辑并按原容量短写回。",
                TextEntry = text
            });
        }

        return rows;
    }

    private static bool Matches(ScenarioStructureRow row, string keyword) =>
        Contains(row.CommandName, keyword) ||
        Contains(row.CommandIdHex, keyword) ||
        Contains(row.ParameterPreview, keyword) ||
        Contains(row.RawContextWordsHex, keyword) ||
        Contains(row.LegacyParameterLayout, keyword) ||
        Contains(row.CommandTemplateHint, keyword) ||
        Contains(row.ReferenceHint, keyword) ||
        Contains(row.Annotation, keyword) ||
        Contains(row.OffsetHex, keyword) ||
        Contains(row.SceneIndex.ToString(), keyword) ||
        Contains(row.SectionIndex.ToString(), keyword);

    private static bool Matches(ScenarioTextEntry text, string keyword) =>
        Contains(text.Text, keyword) ||
        Contains(text.Preview, keyword) ||
        Contains(text.Kind, keyword) ||
        Contains(text.Annotation, keyword) ||
        Contains(text.OffsetHex, keyword);

    private static bool Contains(string? value, string keyword) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "（无）";
}
