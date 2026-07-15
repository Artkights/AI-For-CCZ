using CCZModStudio.Core;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Globalization;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private const string SearchHitNodePrefix = "● ";
    private static readonly Color SearchHitBackColor = Color.FromArgb(255, 246, 174);
    private static readonly Color SearchHitCurrentBackColor = Color.FromArgb(255, 183, 77);
    private readonly Dictionary<DataGridView, string> _searchableGridKeywords = new();
    private readonly HashSet<DataGridView> _searchableGridHandlers = new();
    private readonly Dictionary<TreeView, string> _searchableTreeKeywords = new();
    private readonly HashSet<TreeView> _searchableTreeHandlers = new();

    private TextBox GetLegacyScriptSearchBox(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _scriptSearchBox,
            LegacyScriptEditorScope.Battlefield => _battlefieldScriptSearchBox,
            LegacyScriptEditorScope.RScene => _rSceneScriptSearchBox,
            _ => _scriptSearchBox
        };

    private TextBox GetLegacyScriptReplaceBox(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _scriptReplaceBox,
            LegacyScriptEditorScope.Battlefield => _battlefieldScriptReplaceBox,
            LegacyScriptEditorScope.RScene => _rSceneScriptReplaceBox,
            _ => _scriptReplaceBox
        };

    private Button GetLegacyScriptReplaceButton(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _scriptReplaceButton,
            LegacyScriptEditorScope.Battlefield => _battlefieldScriptReplaceButton,
            LegacyScriptEditorScope.RScene => _rSceneScriptReplaceButton,
            _ => _scriptReplaceButton
        };

    private DataGridView? GetLegacyScriptSearchResultGrid(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _scriptSearchResultGrid,
            LegacyScriptEditorScope.Battlefield => _battlefieldScriptSearchResultGrid,
            LegacyScriptEditorScope.RScene => _rSceneScriptSearchResultGrid,
            _ => null
        };

    private IReadOnlyList<ScenarioSearchResultRow> GetLegacyScriptSearchResults(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _currentScriptSearchResults,
            LegacyScriptEditorScope.Battlefield => _currentBattlefieldScriptSearchResults,
            LegacyScriptEditorScope.RScene => _currentRSceneScriptSearchResults,
            _ => Array.Empty<ScenarioSearchResultRow>()
        };

    private void SetLegacyScriptSearchResults(LegacyScriptEditorScope scope, IReadOnlyList<ScenarioSearchResultRow> rows)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _currentScriptSearchResults = rows;
                break;
            case LegacyScriptEditorScope.Battlefield:
                _currentBattlefieldScriptSearchResults = rows;
                break;
            case LegacyScriptEditorScope.RScene:
                _currentRSceneScriptSearchResults = rows;
                break;
        }
    }

    private void SetLegacyScriptSearchKeyword(LegacyScriptEditorScope scope, string keyword)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _currentScriptSearchKeyword = keyword;
                break;
            case LegacyScriptEditorScope.Battlefield:
                _currentBattlefieldScriptSearchKeyword = keyword;
                break;
            case LegacyScriptEditorScope.RScene:
                _currentRSceneScriptSearchKeyword = keyword;
                break;
        }
    }

    private void SetLegacyScriptSearchResultIndex(LegacyScriptEditorScope scope, int value)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _currentScriptSearchResultIndex = value;
                break;
            case LegacyScriptEditorScope.Battlefield:
                _currentBattlefieldScriptSearchResultIndex = value;
                break;
            case LegacyScriptEditorScope.RScene:
                _currentRSceneScriptSearchResultIndex = value;
                break;
        }
    }

    private HashSet<string> GetLegacyScriptSearchCommandKeys(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _currentScriptSearchCommandKeys,
            LegacyScriptEditorScope.Battlefield => _currentBattlefieldScriptSearchCommandKeys,
            LegacyScriptEditorScope.RScene => _currentRSceneScriptSearchCommandKeys,
            _ => _currentScriptSearchCommandKeys
        };

    private void ApplyLegacyScriptSearch(LegacyScriptEditorScope scope)
    {
        var structure = GetLegacyScriptStructure(scope);
        if (structure == null)
        {
            return;
        }

        var searchBox = GetLegacyScriptSearchBox(scope);
        var keyword = searchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            ClearLegacyScriptSearch(scope);
            return;
        }

        var rows = _scenarioScriptSearchService.Search(
            keyword,
            structure,
            GetLegacyScriptTextEntries(scope),
            text => GetScriptCommandsForText(text, scope),
            row => GetLegacyCommandForSearchRow(scope, row));

        SetLegacyScriptSearchKeyword(scope, keyword);
        SetLegacyScriptSearchResults(scope, rows);
        SetLegacyScriptSearchResultIndex(scope, rows.Count > 0 ? 0 : -1);
        RefreshLegacyScriptSearchCommandKeys(scope, rows);
        BindLegacyScriptSearchResultRows(scope, rows);
        RefreshLegacyScriptTreeSearchMarkers(scope);
        UpdateLegacyScriptReplaceButton(scope);

        var commandRows = rows.Count(row => row.CommandRow != null);
        var textRows = rows.Count(row => row.TextEntry != null);
        var totalMatches = rows.Sum(row => row.MatchCount);
        var replaceableMatches = rows.Sum(row => row.ReplaceableMatchCount);
        var preview = rows.Count == 0
            ? "未命中。"
            : string.Join("\r\n", rows.Take(20).Select(result =>
                $"#{result.Index} {result.Kind} 命中{result.MatchCount}处 {result.Location} {result.Name} {result.Preview}"));
        var prefix =
            $"{GetLegacyScriptScopeStatusPrefix(scope)}搜索：关键字“{keyword}”，结果 {rows.Count} 行，命中 {totalMatches} 处，可替换 {replaceableMatches} 处；命令 {commandRows} 行，文本 {textRows} 行。\r\n前 20 条：\r\n{preview}";

        var firstResult = rows.FirstOrDefault();
        if (firstResult != null)
        {
            ShowLegacyScriptSearchResult(scope, firstResult, prefix);
        }
        else
        {
            SetLegacyScriptDetailText(scope, prefix);
        }

        SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}搜索：{rows.Count} 行 / {totalMatches} 处");
    }

    private void RestoreLegacyScriptSearch(LegacyScriptEditorScope scope, string keyword, int resultIndex)
    {
        SetLegacyScriptSearchKeyword(scope, string.Empty);
        SetLegacyScriptSearchResults(scope, Array.Empty<ScenarioSearchResultRow>());
        SetLegacyScriptSearchResultIndex(scope, -1);
        GetLegacyScriptSearchCommandKeys(scope).Clear();

        var searchBox = GetLegacyScriptSearchBox(scope);
        searchBox.Text = keyword;
        if (string.IsNullOrWhiteSpace(keyword) || GetLegacyScriptStructure(scope) == null)
        {
            BindLegacyScriptSearchResultRows(scope, Array.Empty<ScenarioSearchResultRow>());
            RefreshLegacyScriptTreeSearchMarkers(scope);
            UpdateLegacyScriptReplaceButton(scope);
            return;
        }

        var rows = _scenarioScriptSearchService.Search(
            keyword,
            GetLegacyScriptStructure(scope),
            GetLegacyScriptTextEntries(scope),
            text => GetScriptCommandsForText(text, scope),
            row => GetLegacyCommandForSearchRow(scope, row));
        var clampedIndex = rows.Count == 0
            ? -1
            : Math.Clamp(resultIndex, 0, rows.Count - 1);

        SetLegacyScriptSearchKeyword(scope, keyword.Trim());
        SetLegacyScriptSearchResults(scope, rows);
        SetLegacyScriptSearchResultIndex(scope, clampedIndex);
        RefreshLegacyScriptSearchCommandKeys(scope, rows);
        BindLegacyScriptSearchResultRows(scope, rows);
        SelectLegacyScriptSearchResultGridRow(scope, clampedIndex);
        RefreshLegacyScriptTreeSearchMarkers(scope);
        UpdateLegacyScriptReplaceButton(scope);
    }

    private void ClearLegacyScriptSearch(LegacyScriptEditorScope scope)
    {
        GetLegacyScriptSearchBox(scope).Clear();
        SetLegacyScriptSearchKeyword(scope, string.Empty);
        SetLegacyScriptSearchResults(scope, Array.Empty<ScenarioSearchResultRow>());
        SetLegacyScriptSearchResultIndex(scope, -1);
        GetLegacyScriptSearchCommandKeys(scope).Clear();
        BindLegacyScriptSearchResultRows(scope, Array.Empty<ScenarioSearchResultRow>());
        RefreshLegacyScriptTreeSearchMarkers(scope);
        UpdateLegacyScriptReplaceButton(scope);
        SetLegacyScriptOverviewDetail(scope);
        SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}搜索已清除");
    }

    private void BindLegacyScriptSearchResultRows(LegacyScriptEditorScope scope, IReadOnlyList<ScenarioSearchResultRow> rows)
    {
        if (scope == LegacyScriptEditorScope.Script)
        {
            BindScriptSearchResultRows(rows);
            return;
        }

        var grid = GetLegacyScriptSearchResultGrid(scope);
        if (grid == null)
        {
            return;
        }

        grid.DataSource = new BindingList<ScenarioSearchResultRow>(rows.ToList());
        ConfigureScriptSearchResultGrid(grid);
    }

    private void SelectLegacyScriptSearchResultGridRow(LegacyScriptEditorScope scope, int resultIndex)
    {
        var grid = GetLegacyScriptSearchResultGrid(scope);
        if (grid == null || resultIndex < 0 || resultIndex >= grid.Rows.Count)
        {
            return;
        }

        var row = grid.Rows[resultIndex];
        row.Selected = true;
        grid.CurrentCell = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(cell => cell.Visible);
        TryScrollGridRowIntoView(grid, resultIndex);
    }

    private void ConfigureLegacyScriptSearchResultGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.BorderStyle = BorderStyle.FixedSingle;
        RegisterSearchableGrid(grid);
    }

    private void ConfigureScriptSearchResultGrid(DataGridView grid)
    {
        var keyword = ReferenceEquals(grid, _scriptSearchResultGrid)
            ? _currentScriptSearchKeyword
            : ReferenceEquals(grid, _battlefieldScriptSearchResultGrid)
                ? _currentBattlefieldScriptSearchKeyword
                : ReferenceEquals(grid, _rSceneScriptSearchResultGrid)
                    ? _currentRSceneScriptSearchKeyword
                    : string.Empty;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _searchableGridKeywords.Remove(grid);
        }
        else
        {
            _searchableGridKeywords[grid] = keyword;
        }

        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.HeaderText = column.DataPropertyName switch
            {
                nameof(ScenarioSearchResultRow.Index) => "序号",
                nameof(ScenarioSearchResultRow.Kind) => "类型",
                nameof(ScenarioSearchResultRow.Location) => "位置",
                nameof(ScenarioSearchResultRow.Name) => "名称",
                nameof(ScenarioSearchResultRow.Preview) => "预览",
                nameof(ScenarioSearchResultRow.MatchCount) => "命中",
                nameof(ScenarioSearchResultRow.ReplaceableMatchCount) => "可替换",
                nameof(ScenarioSearchResultRow.Annotation) => "中文注释",
                nameof(ScenarioSearchResultRow.ActionHint) => "操作提示",
                _ => column.HeaderText
            };
            if (column.DataPropertyName is nameof(ScenarioSearchResultRow.CommandRow)
                or nameof(ScenarioSearchResultRow.TextEntry)
                or nameof(ScenarioSearchResultRow.Matches)
                or nameof(ScenarioSearchResultRow.RelatedCommandRows))
            {
                column.Visible = false;
            }
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (column.DataPropertyName is nameof(ScenarioSearchResultRow.Location)
                or nameof(ScenarioSearchResultRow.Preview)
                or nameof(ScenarioSearchResultRow.Annotation)
                or nameof(ScenarioSearchResultRow.ActionHint))
            {
                column.Width = 220;
            }
        }
    }

    private void RefreshLegacyScriptSearchCommandKeys(LegacyScriptEditorScope scope, IReadOnlyList<ScenarioSearchResultRow> rows)
    {
        var keys = GetLegacyScriptSearchCommandKeys(scope);
        keys.Clear();
        foreach (var row in rows)
        {
            if (row.CommandRow != null)
            {
                keys.Add(BuildLegacyCommandKey(row.CommandRow));
            }

            foreach (var related in row.RelatedCommandRows)
            {
                if (related.NodeType == "Command候选")
                {
                    keys.Add(BuildLegacyCommandKey(related));
                }
            }
        }
    }

    private void RefreshLegacyScriptTreeSearchMarkers(LegacyScriptEditorScope scope)
    {
        var tree = GetLegacyScriptTree(scope);
        RegisterSearchableTree(tree);
        var keys = GetLegacyScriptSearchCommandKeys(scope);
        var keyword = GetLegacyScriptSearchBox(scope).Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _searchableTreeKeywords.Remove(tree);
        }
        else
        {
            _searchableTreeKeywords[tree] = keyword;
        }
        tree.BeginUpdate();
        try
        {
            foreach (TreeNode root in tree.Nodes)
            {
                foreach (var node in EnumerateScriptTreeNodes(root))
                {
                    var baseText = RemoveSearchHitNodePrefix(node.Text);
                    if (TryGetScriptTreeRow(node, out var row) &&
                        row.NodeType == "Command候选" &&
                        keys.Contains(BuildLegacyCommandKey(row)))
                    {
                        node.Text = baseText.StartsWith(SearchHitNodePrefix, StringComparison.Ordinal)
                            ? baseText
                            : SearchHitNodePrefix + baseText;
                        node.BackColor = SearchHitBackColor;
                        node.NodeFont = new Font(tree.Font, FontStyle.Bold);
                    }
                    else
                    {
                        node.Text = baseText;
                        node.BackColor = Color.Empty;
                        node.NodeFont = null;
                    }
                }
            }
        }
        finally
        {
            tree.EndUpdate();
        }
    }

    private static string RemoveSearchHitNodePrefix(string text)
        => text.StartsWith(SearchHitNodePrefix, StringComparison.Ordinal)
            ? text[SearchHitNodePrefix.Length..]
            : text;

    private ScenarioSearchResultRow? GetSelectedLegacyScriptSearchResultRow(LegacyScriptEditorScope scope)
    {
        var grid = GetLegacyScriptSearchResultGrid(scope);
        if (grid == null)
        {
            return GetLegacyScriptSearchResults(scope).FirstOrDefault();
        }

        if (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].DataBoundItem is ScenarioSearchResultRow selected)
        {
            return selected;
        }

        return grid.CurrentRow?.DataBoundItem as ScenarioSearchResultRow;
    }

    private void ShowSelectedLegacyScriptSearchResult(LegacyScriptEditorScope scope)
    {
        var result = GetSelectedLegacyScriptSearchResultRow(scope);
        if (result == null) return;
        ShowLegacyScriptSearchResult(scope, result);
    }

    private void ShowLegacyScriptSearchResult(LegacyScriptEditorScope scope, ScenarioSearchResultRow result, string? prefix = null)
    {
        SetLegacyScriptSearchResultIndex(scope, Math.Max(0, result.Index - 1));
        if (result.CommandRow != null)
        {
            SelectLegacyScriptTreeNode(scope, result.CommandRow, suppressEvents: true);
            ShowSelectedLegacyScriptTreeNode(scope);
            SetLegacyScriptDetailText(scope,
                (string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix + "\r\n\r\n") +
                $"搜索结果：#{result.Index} {result.Kind}，命中 {result.MatchCount} 处\r\n{result.Location}\r\n\r\n" +
                BuildLegacyScriptSearchMatchDetail(result));
            return;
        }

        if (result.TextEntry != null)
        {
            var related = result.RelatedCommandRows.FirstOrDefault();
            if (related != null)
            {
                SelectLegacyScriptTreeNode(scope, related, suppressEvents: true);
            }
            else
            {
                SelectLegacyScriptTextTreeNode(scope, result.TextEntry, suppressEvents: true);
            }
            ShowSelectedLegacyScriptTreeNode(scope);
            SetLegacyScriptDetailText(scope,
                (string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix + "\r\n\r\n") +
                $"搜索结果：#{result.Index} {result.Kind}，命中 {result.MatchCount} 处，可替换 {result.ReplaceableMatchCount} 处\r\n{result.Location}\r\n\r\n" +
                BuildLegacyScriptSearchMatchDetail(result));
        }
    }

    private string BuildLegacyScriptSearchMatchDetail(ScenarioSearchResultRow result)
    {
        var lines = result.Matches
            .Take(20)
            .Select(match => $"- {match.FieldName} @{match.Start}: {match.Text}（{FormatSearchMatchReplaceStatus(match)}）")
            .ToList();
        if (result.Matches.Count > lines.Count)
        {
            lines.Add($"... 其余 {result.Matches.Count - lines.Count} 处省略");
        }

        var related = result.RelatedCommandRows.Count == 0
            ? string.Empty
            : "\r\n\r\n相关指令：\r\n" + string.Join("\r\n", result.RelatedCommandRows.Take(12).Select(row =>
                $"- Scene {row.SceneIndex} / Section {row.SectionIndex} / Command {row.CommandIndex} {row.CommandIdHex} {row.CommandName} {row.OffsetHex}"));

        return $"{result.Name}\r\n预览：{result.Preview}\r\n\r\n命中：\r\n{string.Join("\r\n", lines)}{related}";
    }

    private static string FormatSearchMatchReplaceStatus(ScenarioSearchMatch match)
    {
        if (!match.IsReplaceable || match.ReplaceTarget == ScenarioSearchReplaceTarget.None)
        {
            var reason = FormatSearchProtectionKind(match.ProtectionKind);
            return string.IsNullOrWhiteSpace(match.ProtectionDetail)
                ? $"受保护：{reason}"
                : $"受保护：{reason}；{match.ProtectionDetail}";
        }

        return match.ReplaceTarget switch
        {
            ScenarioSearchReplaceTarget.TextEntryText => "可替换：文本正文",
            ScenarioSearchReplaceTarget.CommandTextParameter => "可替换：文本参数",
            ScenarioSearchReplaceTarget.CommandScalarParameter => "可替换：数值参数",
            ScenarioSearchReplaceTarget.GridStringCell => "可替换：表格文本",
            _ => "可替换"
        };
    }

    private static string FormatSearchMatchReplaceStatusLegacy(ScenarioSearchMatch match)
    {
        if (!match.IsReplaceable || match.ReplaceTarget == ScenarioSearchReplaceTarget.None)
        {
            return "受保护";
        }

        return match.ReplaceTarget switch
        {
            ScenarioSearchReplaceTarget.TextEntryText => "可替换：文本正文",
            ScenarioSearchReplaceTarget.CommandTextParameter => "可替换：文本参数",
            ScenarioSearchReplaceTarget.CommandScalarParameter => "可替换：数值参数",
            ScenarioSearchReplaceTarget.GridStringCell => "可替换：表格文本",
            _ => "可替换"
        };
    }

    private bool SelectLegacyScriptTreeNode(LegacyScriptEditorScope scope, ScenarioStructureRow row, bool suppressEvents = false, bool ensureVisible = true)
    {
        return scope switch
        {
            LegacyScriptEditorScope.Script => SelectScriptTreeNode(row, suppressEvents, ensureVisible),
            LegacyScriptEditorScope.Battlefield => SelectBattlefieldScriptTreeNode(row, ensureVisible),
            LegacyScriptEditorScope.RScene => SelectRSceneScriptTreeNode(row, ensureVisible),
            _ => false
        };
    }

    private bool SelectLegacyScriptTextTreeNode(LegacyScriptEditorScope scope, ScenarioTextEntry entry, bool suppressEvents = false)
    {
        if (scope == LegacyScriptEditorScope.Script)
        {
            return SelectScriptTextTreeNode(entry, suppressEvents);
        }

        var tree = GetLegacyScriptTree(scope);
        foreach (TreeNode root in tree.Nodes)
        {
            var found = FindScriptTextTreeNode(root, entry);
            if (found == null) continue;
            var previous = IsLegacyScriptEditorBinding(scope);
            if (suppressEvents)
            {
                SetLegacyScriptEditorBinding(scope, true);
            }
            try
            {
                tree.SelectedNode = found;
                found.EnsureVisible();
            }
            finally
            {
                SetLegacyScriptEditorBinding(scope, previous);
            }
            return true;
        }

        return false;
    }

    private bool IsLegacyScriptEditorBinding(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _bindingScriptDocument,
            LegacyScriptEditorScope.Battlefield => _bindingBattlefieldScriptEditor,
            LegacyScriptEditorScope.RScene => _bindingRSceneScriptTree,
            _ => false
        };

    private IReadOnlyList<ScenarioStructureRow> GetScriptCommandsForText(ScenarioTextEntry text, LegacyScriptEditorScope scope)
    {
        var structure = GetLegacyScriptStructure(scope);
        if (structure == null)
        {
            return Array.Empty<ScenarioStructureRow>();
        }

        var rows = structure.Rows
            .Where(row => row.NodeType == "Command候选")
            .ToList();
        return GetScriptCommandsForText(text, rows);
    }

    private IReadOnlyList<ScenarioStructureRow> GetScriptCommandsForText(ScenarioTextEntry text, IReadOnlyList<ScenarioStructureRow> rows)
    {
        return rows
            .Select(row => new
            {
                Row = row,
                Score = GetScriptTextRelationScore(row, text),
                Distance = GetNearestScriptCommandDistance(new[] { row }, text)
            })
            .Where(candidate => candidate.Score >= 40)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Row.CommandIndex)
            .Select(candidate => candidate.Row)
            .Take(12)
            .ToList();
    }

    private LegacyScenarioCommandNode? GetLegacyCommandForSearchRow(LegacyScriptEditorScope scope, ScenarioStructureRow row)
    {
        if (row.NodeType != "Command候选")
        {
            return null;
        }

        var key = BuildLegacyCommandKey(row);
        return scope switch
        {
            LegacyScriptEditorScope.Script => _legacyScriptCommandByKey.TryGetValue(key, out var scriptCommand) ? scriptCommand : null,
            LegacyScriptEditorScope.Battlefield => _battlefieldScriptCommandByKey.TryGetValue(key, out var battlefieldCommand) ? battlefieldCommand : null,
            LegacyScriptEditorScope.RScene => _rSceneScriptCommandByKey.TryGetValue(key, out var rSceneCommand) ? rSceneCommand : null,
            _ => null
        };
    }

    private void ReplaceLegacyScriptSearchMatches(LegacyScriptEditorScope scope)
    {
        var keyword = GetLegacyScriptSearchBox(scope).Text.Trim();
        var replacement = GetLegacyScriptReplaceBox(scope).Text;
        if (string.IsNullOrWhiteSpace(keyword) || string.IsNullOrEmpty(replacement))
        {
            return;
        }

        var results = GetLegacyScriptSearchResults(scope);
        if (results.Count == 0)
        {
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)} 替换：当前搜索没有命中。");
            return;
        }

        var replaceableResults = results
            .Where(row => row.ReplaceableMatchCount > 0)
            .ToList();
        if (replaceableResults.Count == 0)
        {
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)} 替换：没有可替换命中；{FormatProtectedSearchSummary(results)}。");
            return;
        }

        var document = GetCurrentLegacyScriptDocument(scope);
        LegacyScenarioHistorySnapshot? beforeEdit = null;
        LegacyScriptViewportSnapshot? viewport = null;
        if (document != null)
        {
            beforeEdit = CaptureLegacyScenarioHistorySnapshot(scope, document);
            viewport = CaptureLegacyScriptViewport(scope);
        }

        var replaced = 0;
        var skipped = 0;
        var changedObjects = 0;
        var changedEntries = new List<ScenarioTextEntry>();
        var skipReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var changedParameters = new HashSet<LegacyScenarioCommandParameter>();
        var parameterMatches = replaceableResults
            .SelectMany(row => row.Matches)
            .Where(match => match.ReplaceTarget is ScenarioSearchReplaceTarget.CommandTextParameter or ScenarioSearchReplaceTarget.CommandScalarParameter &&
                            match.CommandParameter != null)
            .ToList();
        var parametersWithMatches = parameterMatches
            .Select(match => match.CommandParameter!)
            .ToHashSet();

        foreach (var group in replaceableResults
                     .Where(row => row.TextEntry != null)
                     .GroupBy(row => row.TextEntry!))
        {
            var entry = group.Key;
            var matches = group.SelectMany(row => row.Matches)
                .Where(match => match.ReplaceTarget == ScenarioSearchReplaceTarget.TextEntryText)
                .ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            if (TryGetLegacyTextParameter(scope, entry.Offset, out var boundParameter))
            {
                if (parametersWithMatches.Contains(boundParameter))
                {
                    continue;
                }

                var newText = ReplaceTextByMatches(boundParameter.Text, matches, replacement, out var count);
                if (count == 0)
                {
                    AddSkip(skipReasons, "文本参数无可应用命中");
                    skipped += Math.Max(1, matches.Count);
                    continue;
                }

                boundParameter.Text = newText;
                boundParameter.ByteLength = EncodingService.GetGbkByteCount(newText) + 1;
                replaced += count;
                changedObjects++;
                changedParameters.Add(boundParameter);
                parametersWithMatches.Add(boundParameter);
                changedEntries.Add(entry);
                continue;
            }

            var replacedText = ReplaceTextByMatches(entry.Text, matches, replacement, out var textCount);
            if (textCount == 0)
            {
                AddSkip(skipReasons, "文本正文无可应用命中");
                skipped += Math.Max(1, matches.Count);
                continue;
            }

            var writableText = entry.BuildWritableText(replacedText);
            var byteCount = EncodingService.GetGbkByteCount(writableText);
            if (entry.ByteLength > 0 && byteCount > entry.ByteLength)
            {
                AddSkip(skipReasons, "固定容量文本超出 GBK 容量");
                skipped += textCount;
                continue;
            }

            entry.Text = replacedText;
            entry.Preview = replacedText.Length > 60 ? replacedText[..60] : replacedText;
            entry.CharLength = replacedText.Length;
            replaced += textCount;
            changedObjects++;
            changedEntries.Add(entry);
        }

        foreach (var group in parameterMatches.GroupBy(match => match.CommandParameter!))
        {
            var parameter = group.Key;
            switch (parameter.Kind)
            {
                case LegacyScenarioParameterKind.Text:
                    {
                        var newText = ReplaceTextByMatches(parameter.Text, group, replacement, out var count);
                        if (count == 0)
                        {
                            AddSkip(skipReasons, "文本参数无可应用命中");
                            skipped += Math.Max(1, group.Count());
                            continue;
                        }

                        parameter.Text = newText;
                        parameter.ByteLength = EncodingService.GetGbkByteCount(newText) + 1;
                        replaced += count;
                        changedObjects++;
                        changedParameters.Add(parameter);
                        break;
                    }
                case LegacyScenarioParameterKind.Word16:
                case LegacyScenarioParameterKind.Dword32:
                    {
                        var oldValue = parameter.IntValue.ToString(CultureInfo.InvariantCulture);
                        var newValueText = ReplaceTextByMatches(oldValue, group, replacement, out var count);
                        if (count == 0)
                        {
                            AddSkip(skipReasons, "数值参数无可应用命中");
                            skipped += Math.Max(1, group.Count());
                            continue;
                        }

                        if (!TryParseReplacementParameterValue(parameter, newValueText, out var newValue, out var reason))
                        {
                            AddSkip(skipReasons, reason);
                            skipped += count;
                            continue;
                        }

                        parameter.IntValue = newValue;
                        replaced += count;
                        changedObjects++;
                        changedParameters.Add(parameter);
                        break;
                    }
            }
        }

        foreach (var parameter in changedParameters.Where(parameter => parameter.Kind == LegacyScenarioParameterKind.Text))
        {
            if (GetLegacyScriptTextEntryByOffset(scope, parameter.FileOffset) is { } entry &&
                changedEntries.All(candidate => candidate.Offset != entry.Offset))
            {
                entry.Text = parameter.Text;
                entry.Preview = entry.Text.Length > 60 ? entry.Text[..60] : entry.Text;
                entry.CharLength = entry.Text.Length;
                changedEntries.Add(entry);
            }
        }

        if (replaced == 0)
        {
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)} 替换：未修改，跳过 {skipped} 处；{FormatSearchReplaceSkipReasons(skipReasons)}；{FormatProtectedSearchSummary(results)}。");
            return;
        }

        LegacyScenarioCommandNode? preferredSelection = null;
        if (changedParameters.Count > 0)
        {
            preferredSelection = FindLegacyCommandForParameter(scope, changedParameters.First());
        }

        if (document != null && beforeEdit != null)
        {
            PushLegacyScenarioUndoSnapshot(scope, beforeEdit);
            RefreshLegacyScriptView(scope, preferredSelection, viewport);
        }
        else
        {
            EnableLegacyTextSaveForScope(scope);
            RefreshLegacyTextEntriesAfterReplace(scope, changedEntries);
        }

        ApplyLegacyScriptSearch(scope);
        var refreshedResults = GetLegacyScriptSearchResults(scope);
        var skipText = skipped == 0 ? string.Empty : $"，跳过 {skipped} 处；{FormatSearchReplaceSkipReasons(skipReasons)}";
        var remainingProtected = FormatRemainingProtectedSearchSummary(refreshedResults);
        SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)} 替换：已替换 {replaced} 处 / 修改 {changedObjects} 个对象{skipText}{remainingProtected}；尚未写盘。");
    }

    private void ReplaceLegacyScriptSearchMatchesLegacy(LegacyScriptEditorScope scope)
    {
        var keyword = GetLegacyScriptSearchBox(scope).Text.Trim();
        var replacement = GetLegacyScriptReplaceBox(scope).Text;
        if (string.IsNullOrWhiteSpace(keyword) || string.IsNullOrEmpty(replacement))
        {
            return;
        }

        var results = GetLegacyScriptSearchResults(scope);
        var replaceableResults = results
            .Where(row => row.ReplaceableMatchCount > 0)
            .ToList();
        if (replaceableResults.Count == 0)
        {
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}替换：没有可替换命中");
            return;
        }

        var document = GetCurrentLegacyScriptDocument(scope);
        LegacyScenarioHistorySnapshot? beforeEdit = null;
        if (document != null)
        {
            beforeEdit = CaptureLegacyScenarioHistorySnapshot(scope, document);
        }

        var replaced = 0;
        var skipped = 0;
        var changedObjects = 0;
        var changedEntries = new List<ScenarioTextEntry>();
        var skipReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var changedParameters = new HashSet<LegacyScenarioCommandParameter>();
        var legacyTextEntryParameterOffsets = new HashSet<int>();

        foreach (var group in replaceableResults
                     .Where(row => row.TextEntry != null)
                     .GroupBy(row => row.TextEntry!))
        {
            var entry = group.Key;
            if (TryGetLegacyTextParameter(scope, entry.Offset, out _))
            {
                legacyTextEntryParameterOffsets.Add(entry.Offset);
                continue;
            }

            var oldText = entry.Text;
            var matches = group.SelectMany(row => row.Matches)
                .Where(match => match.ReplaceTarget == ScenarioSearchReplaceTarget.TextEntryText)
                .ToList();
            var newText = ReplaceTextByMatches(oldText, matches, replacement, out var count);
            if (count == 0)
            {
                AddSkip(skipReasons, "文本正文无可应用命中");
                skipped += Math.Max(1, matches.Count);
                continue;
            }

            var writableText = entry.BuildWritableText(newText);
            var byteCount = EncodingService.GetGbkByteCount(writableText);
            if (entry.ByteLength > 0 && byteCount > entry.ByteLength && !TryGetLegacyTextParameter(scope, entry.Offset, out _))
            {
                AddSkip(skipReasons, "固定容量文本超出 GBK 容量");
                skipped += count;
                continue;
            }

            entry.Text = newText;
            entry.Preview = newText.Length > 60 ? newText[..60] : newText;
            entry.CharLength = newText.Length;
            if (TryGetLegacyTextParameter(scope, entry.Offset, out var parameter))
            {
                parameter.Text = newText;
                parameter.ByteLength = EncodingService.GetGbkByteCount(newText) + 1;
            }

            replaced += count;
            changedObjects++;
            changedEntries.Add(entry);
        }

        foreach (var group in replaceableResults
                     .SelectMany(row => row.Matches)
                     .Where(match => match.ReplaceTarget is ScenarioSearchReplaceTarget.CommandTextParameter or ScenarioSearchReplaceTarget.CommandScalarParameter &&
                                     match.CommandParameter != null)
                     .GroupBy(match => match.CommandParameter!))
        {
            var parameter = group.Key;
            switch (parameter.Kind)
            {
                case LegacyScenarioParameterKind.Text:
                    {
                        var oldText = parameter.Text;
                        var newText = ReplaceTextByMatches(oldText, group, replacement, out var count);
                        if (count == 0)
                        {
                            AddSkip(skipReasons, "文本参数无可应用命中");
                            skipped += Math.Max(1, group.Count());
                            continue;
                        }

                        parameter.Text = newText;
                        parameter.ByteLength = EncodingService.GetGbkByteCount(newText) + 1;
                        replaced += count;
                        changedObjects++;
                        changedParameters.Add(parameter);
                        break;
                    }
                case LegacyScenarioParameterKind.Word16:
                case LegacyScenarioParameterKind.Dword32:
                    {
                        var oldValue = parameter.IntValue.ToString(CultureInfo.InvariantCulture);
                        var newValueText = ReplaceTextByMatches(oldValue, group, replacement, out var count);
                        if (count == 0)
                        {
                            AddSkip(skipReasons, "数值参数无可应用命中");
                            skipped += Math.Max(1, group.Count());
                            continue;
                        }

                        if (!TryParseReplacementParameterValue(parameter, newValueText, out var newValue, out var reason))
                        {
                            AddSkip(skipReasons, reason);
                            skipped += count;
                            continue;
                        }

                        parameter.IntValue = newValue;
                        replaced += count;
                        changedObjects++;
                        changedParameters.Add(parameter);
                        break;
                    }
            }
        }

        foreach (var offset in legacyTextEntryParameterOffsets)
        {
            if (changedEntries.Any(entry => entry.Offset == offset))
            {
                continue;
            }

            if (GetLegacyScriptTextEntryByOffset(scope, offset) is { } entry)
            {
                entry.Text = TryGetLegacyTextParameter(scope, offset, out var parameter) ? parameter.Text : entry.Text;
                entry.Preview = entry.Text.Length > 60 ? entry.Text[..60] : entry.Text;
                entry.CharLength = entry.Text.Length;
                changedEntries.Add(entry);
            }
        }

        if (replaced == 0)
        {
            SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}替换：未修改，跳过 {skipped} 处；{FormatSearchReplaceSkipReasons(skipReasons)}");
            return;
        }

        if (document != null && beforeEdit != null)
        {
            PushLegacyScenarioUndoSnapshot(scope, beforeEdit);
            MarkLegacyScriptStructureDirty(scope);
        }
        else
        {
            EnableLegacyTextSaveForScope(scope);
        }

        RefreshLegacyTextEntriesAfterReplace(scope, changedEntries);
        ApplyLegacyScriptSearch(scope);
        var skipText = skipped == 0 ? string.Empty : $"，跳过 {skipped} 处；{FormatSearchReplaceSkipReasons(skipReasons)}";
        SetStatus($"{GetLegacyScriptScopeStatusPrefix(scope)}替换：替换 {replaced} 处 / 修改 {changedObjects} 个对象{skipText}，尚未写盘");
    }

    private static string ReplaceTextByMatches(
        string text,
        IEnumerable<ScenarioSearchMatch> matches,
        string replacement,
        out int count)
    {
        var spans = matches
            .Where(match => match.IsReplaceable &&
                            match.Start >= 0 &&
                            match.Length > 0 &&
                            match.Start + match.Length <= text.Length)
            .OrderByDescending(match => match.Start)
            .ToList();

        count = spans.Count;
        if (spans.Count == 0)
        {
            return text;
        }

        var result = text;
        foreach (var span in spans)
        {
            result = result.Remove(span.Start, span.Length).Insert(span.Start, replacement);
        }

        return result;
    }

    private static bool TryParseReplacementParameterValue(
        LegacyScenarioCommandParameter parameter,
        string valueText,
        out int value,
        out string reason)
    {
        value = 0;
        if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            reason = "数值解析失败";
            return false;
        }

        if (parameter.Kind == LegacyScenarioParameterKind.Word16 && (value < short.MinValue || value > ushort.MaxValue))
        {
            reason = "16 位数值超出范围";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static void AddSkip(Dictionary<string, int> reasons, string reason)
    {
        if (!reasons.TryAdd(reason, 1))
        {
            reasons[reason]++;
        }
    }

    private static string FormatSearchReplaceSkipReasons(IReadOnlyDictionary<string, int> reasons)
        => reasons.Count == 0
            ? "无跳过原因"
            : string.Join("；", reasons.Select(pair => $"{pair.Key} {pair.Value}"));

    private static string FormatProtectedSearchSummary(IReadOnlyList<ScenarioSearchResultRow> results)
    {
        var protectedMatches = results
            .SelectMany(row => row.Matches)
            .Where(match => !match.IsReplaceable || match.ReplaceTarget == ScenarioSearchReplaceTarget.None)
            .ToList();
        if (protectedMatches.Count == 0)
        {
            return "没有受保护命中";
        }

        return "受保护命中：" + string.Join("，", protectedMatches
            .GroupBy(match => FormatSearchProtectionKind(match.ProtectionKind))
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key} {group.Count()} 处"));
    }

    private static string FormatRemainingProtectedSearchSummary(IReadOnlyList<ScenarioSearchResultRow> results)
    {
        var protectedText = FormatProtectedSearchSummary(results);
        return protectedText == "没有受保护命中"
            ? string.Empty
            : "；剩余旧词" + protectedText;
    }

    private static string FormatSearchProtectionKind(ScenarioSearchProtectionKind kind)
        => kind switch
        {
            ScenarioSearchProtectionKind.CommandIdentity => "命令号/身份字段",
            ScenarioSearchProtectionKind.StructureField => "结构字段",
            ScenarioSearchProtectionKind.DerivedDisplayName => "派生显示名",
            ScenarioSearchProtectionKind.UnboundCommandParameter => "未绑定真实参数",
            _ => "受保护字段"
        };

    private LegacyScenarioCommandNode? FindLegacyCommandForParameter(
        LegacyScriptEditorScope scope,
        LegacyScenarioCommandParameter parameter)
    {
        var document = GetCurrentLegacyScriptDocument(scope);
        if (document == null)
        {
            return null;
        }

        return document
            .EnumerateCommands()
            .FirstOrDefault(command => command.Parameters.Any(candidate => ReferenceEquals(candidate, parameter)));
    }

    private bool TryGetLegacyTextParameter(LegacyScriptEditorScope scope, int offset, out LegacyScenarioCommandParameter parameter)
    {
        parameter = null!;
        var found = scope switch
        {
            LegacyScriptEditorScope.Script => _legacyScriptTextByOffset.TryGetValue(offset, out var pair) ? pair.Parameter : null,
            LegacyScriptEditorScope.Battlefield => _battlefieldScriptTextByOffset.TryGetValue(offset, out var pair) ? pair.Parameter : null,
            _ => null
        };
        if (found != null)
        {
            parameter = found;
            return true;
        }

        if (scope == LegacyScriptEditorScope.RScene && _currentRSceneLegacyScriptDocument != null)
        {
            foreach (var command in _currentRSceneLegacyScriptDocument.EnumerateCommands())
            {
                var textParameter = command.TextParameters.FirstOrDefault(candidate => candidate.FileOffset == offset);
                if (textParameter != null)
                {
                    parameter = textParameter;
                    return true;
                }
            }
        }

        return false;
    }

    private ScenarioTextEntry? GetLegacyScriptTextEntryByOffset(LegacyScriptEditorScope scope, int offset)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _legacyScriptTextEntryByOffset.TryGetValue(offset, out var scriptEntry) ? scriptEntry : null,
            LegacyScriptEditorScope.Battlefield => _battlefieldScriptTextEntryByOffset.TryGetValue(offset, out var battlefieldEntry) ? battlefieldEntry : null,
            LegacyScriptEditorScope.RScene => _currentRSceneScriptTextEntries.FirstOrDefault(entry => entry.Offset == offset),
            _ => null
        };

    private static string ReplaceAllLiteral(string text, string keyword, string replacement, out int count)
    {
        count = 0;
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword))
        {
            return text;
        }

        var indexes = new List<int>();
        var start = 0;
        while (start < text.Length)
        {
            var index = text.IndexOf(keyword, start, StringComparison.CurrentCultureIgnoreCase);
            if (index < 0) break;
            indexes.Add(index);
            start = index + Math.Max(1, keyword.Length);
        }

        if (indexes.Count == 0)
        {
            return text;
        }

        var result = text;
        for (var i = indexes.Count - 1; i >= 0; i--)
        {
            result = result.Remove(indexes[i], keyword.Length).Insert(indexes[i], replacement);
        }

        count = indexes.Count;
        return result;
    }

    private void EnableLegacyTextSaveForScope(LegacyScriptEditorScope scope)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _saveScriptTextButton.Enabled = true;
                break;
            case LegacyScriptEditorScope.Battlefield:
                _saveBattlefieldScriptTextButton.Enabled = true;
                break;
        }
    }

    private void RefreshLegacyTextEntriesAfterReplace(LegacyScriptEditorScope scope, IReadOnlyList<ScenarioTextEntry> entries)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                RefreshScriptTextRows(entries);
                UpdateScriptTextCapacityLabel();
                break;
            case LegacyScriptEditorScope.Battlefield:
                UpdateBattlefieldScriptTextCapacityLabel();
                break;
            case LegacyScriptEditorScope.RScene:
                break;
        }
    }

    private void UpdateLegacyScriptReplaceButton(LegacyScriptEditorScope scope)
    {
        var button = GetLegacyScriptReplaceButton(scope);
        var hasKeyword = !string.IsNullOrWhiteSpace(GetLegacyScriptSearchBox(scope).Text);
        var hasReplacement = !string.IsNullOrEmpty(GetLegacyScriptReplaceBox(scope).Text);
        var hasResults = GetLegacyScriptSearchResults(scope).Count > 0;
        button.Enabled = hasKeyword && hasReplacement && hasResults;
    }

    private Control CreateSearchableGridPanel(DataGridView grid, string placeholder)
    {
        RegisterSearchableGrid(grid);
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = CreateToolbarRow();
        var searchBox = new TextBox();
        ConfigureToolbarInput(searchBox, 180, 120);
        searchBox.PlaceholderText = placeholder;
        var searchButton = new Button { Text = "搜索" };
        ConfigureToolbarButton(searchButton, 72);
        var previousButton = new Button { Text = "上一条" };
        ConfigureToolbarButton(previousButton, 72);
        var nextButton = new Button { Text = "下一条" };
        ConfigureToolbarButton(nextButton, 72);
        var clearButton = new Button { Text = "清除" };
        ConfigureToolbarButton(clearButton, 72);
        var replaceBox = new TextBox();
        ConfigureToolbarInput(replaceBox, 150, 110);
        replaceBox.PlaceholderText = "替换为";
        var replaceButton = new Button { Text = "替换" };
        ConfigureToolbarButton(replaceButton, 72);
        replaceButton.Enabled = false;
        var allowReplace = !grid.ReadOnly;
        toolbar.Controls.AddRange(new Control[]
        {
            CreateToolbarLabel("搜索：", 0),
            searchBox,
            searchButton,
            previousButton,
            nextButton,
            clearButton
        });
        if (allowReplace)
        {
            toolbar.Controls.AddRange(new Control[]
            {
                CreateToolbarLabel("替换：", 0),
                replaceBox,
                replaceButton
            });
        }

        void UpdateReplace()
        {
            replaceButton.Enabled = allowReplace &&
                                    CanReplaceSearchableGridMatches(grid, searchBox.Text.Trim(), replaceBox.Text);
        }

        void Apply()
        {
            ApplySearchableGridSearch(grid, searchBox.Text.Trim());
            UpdateReplace();
        }

        searchButton.Click += (_, _) => Apply();
        previousButton.Click += (_, _) =>
        {
            MoveSearchableGridMatch(grid, searchBox.Text.Trim(), forward: false);
            UpdateReplace();
        };
        nextButton.Click += (_, _) =>
        {
            MoveSearchableGridMatch(grid, searchBox.Text.Trim(), forward: true);
            UpdateReplace();
        };
        clearButton.Click += (_, _) =>
        {
            searchBox.Clear();
            ApplySearchableGridSearch(grid, string.Empty);
            UpdateReplace();
        };
        replaceBox.TextChanged += (_, _) => UpdateReplace();
        if (allowReplace)
        {
            replaceButton.Click += (_, _) =>
            {
                ReplaceSearchableGridMatches(grid, searchBox.Text.Trim(), replaceBox.Text);
                UpdateReplace();
            };
        }
        searchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            Apply();
            e.SuppressKeyPress = true;
        };

        panel.Controls.Add(toolbar, 0, 0);
        panel.Controls.Add(grid, 0, 1);
        return panel;
    }

    private Control CreateSearchableTreePanel(TreeView tree, string placeholder)
    {
        RegisterSearchableTree(tree);
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = CreateToolbarRow();
        var searchBox = new TextBox();
        ConfigureToolbarInput(searchBox, 180, 120);
        searchBox.PlaceholderText = placeholder;
        var searchButton = new Button { Text = "搜索" };
        ConfigureToolbarButton(searchButton, 72);
        var clearButton = new Button { Text = "清除" };
        ConfigureToolbarButton(clearButton, 72);
        toolbar.Controls.AddRange(new Control[]
        {
            CreateToolbarLabel("搜索：", 0),
            searchBox,
            searchButton,
            clearButton
        });

        void Apply()
        {
            ApplySearchableTreeSearch(tree, searchBox.Text.Trim());
        }

        searchButton.Click += (_, _) => Apply();
        clearButton.Click += (_, _) =>
        {
            searchBox.Clear();
            ApplySearchableTreeSearch(tree, string.Empty);
        };
        searchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            Apply();
            e.SuppressKeyPress = true;
        };

        panel.Controls.Add(toolbar, 0, 0);
        panel.Controls.Add(tree, 0, 1);
        return panel;
    }

    private void RegisterSearchableTree(TreeView tree)
    {
        if (!_searchableTreeHandlers.Add(tree))
        {
            return;
        }

        tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
        tree.DrawNode += HandleSearchableTreeDrawNode;
    }

    private void RegisterSearchableGrid(DataGridView grid)
    {
        if (!_searchableGridHandlers.Add(grid))
        {
            return;
        }

        grid.CellPainting += HandleSearchableGridCellPainting;
        grid.DataBindingComplete += (_, _) =>
        {
            if (_searchableGridKeywords.TryGetValue(grid, out var keyword) && !string.IsNullOrWhiteSpace(keyword))
            {
                grid.Invalidate();
            }
        };
    }

    private void ApplySearchableGridSearch(DataGridView grid, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _searchableGridKeywords.Remove(grid);
            grid.Invalidate();
            SetStatus("表格搜索已清除");
            return;
        }

        _searchableGridKeywords[grid] = keyword;
        var first = FindFirstGridSearchMatch(grid, keyword);
        if (first.HasValue)
        {
            SelectSearchableGridMatch(grid, first.Value);
            SetStatus($"表格搜索命中：{keyword}");
        }
        else
        {
            SetStatus($"表格搜索未命中：{keyword}");
        }

        grid.Invalidate();
    }

    private static (int RowIndex, int ColumnIndex)? FindFirstGridSearchMatch(DataGridView grid, string keyword)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!row.Visible) continue;
            foreach (DataGridViewCell cell in row.Cells)
            {
                if (!cell.Visible) continue;
                var text = Convert.ToString(cell.FormattedValue, CultureInfo.CurrentCulture);
                if (!string.IsNullOrEmpty(text) && text.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
                {
                    return (row.Index, cell.ColumnIndex);
                }
            }
        }

        return null;
    }

    private bool CanReplaceSearchableGridMatches(DataGridView grid, string keyword, string replacement)
        => _searchableGridKeywords.TryGetValue(grid, out var appliedKeyword) &&
           string.Equals(appliedKeyword, keyword, StringComparison.CurrentCultureIgnoreCase) &&
           !string.IsNullOrWhiteSpace(keyword) &&
           !string.IsNullOrEmpty(replacement) &&
           FindReplaceableGridCells(grid, keyword).Any();

    private void ReplaceSearchableGridMatches(DataGridView grid, string keyword, string replacement)
    {
        if (string.IsNullOrWhiteSpace(keyword) || string.IsNullOrEmpty(replacement))
        {
            return;
        }

        var replaced = 0;
        var changedCells = 0;
        foreach (var cell in FindReplaceableGridCells(grid, keyword).ToList())
        {
            if (cell.Value is not string oldText)
            {
                continue;
            }

            var newText = ReplaceAllLiteral(oldText, keyword, replacement, out var count);
            if (count == 0 || string.Equals(newText, oldText, StringComparison.Ordinal))
            {
                continue;
            }

            cell.Value = newText;
            replaced += count;
            changedCells++;
        }

        grid.Invalidate();
        SetStatus(changedCells == 0
            ? $"表格替换：没有可替换的字符串单元格，关键字 {keyword}"
            : $"表格替换：替换 {replaced} 处 / {changedCells} 个单元格，尚未写盘");
    }

    private static IEnumerable<DataGridViewCell> FindReplaceableGridCells(DataGridView grid, string keyword)
    {
        if (grid.ReadOnly || string.IsNullOrWhiteSpace(keyword))
        {
            yield break;
        }

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!row.Visible || row.ReadOnly) continue;
            foreach (DataGridViewCell cell in row.Cells)
            {
                if (!IsReplaceableGridCell(cell)) continue;
                if (cell.Value is string text &&
                    text.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
                {
                    yield return cell;
                }
            }
        }
    }

    private static bool IsReplaceableGridCell(DataGridViewCell cell)
    {
        var column = cell.OwningColumn;
        if (!cell.Visible || cell.ReadOnly || column == null || column.ReadOnly || !column.Visible)
        {
            return false;
        }

        var key = string.IsNullOrWhiteSpace(column.DataPropertyName) ? column.Name : column.DataPropertyName;
        return !IsGenericReadOnlyIdentityColumn(key);
    }

    private static bool IsGenericReadOnlyIdentityColumn(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return false;
        }

        var normalized = columnName.Trim();
        return normalized.Equals("ID", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("序号", StringComparison.Ordinal) ||
               normalized.Equals("编号", StringComparison.Ordinal) ||
               normalized.Equals("命令号", StringComparison.Ordinal) ||
               normalized.Equals("CommandId", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("CommandID", StringComparison.OrdinalIgnoreCase);
    }

    private void MoveSearchableGridMatch(DataGridView grid, string keyword, bool forward)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return;
        }

        _searchableGridKeywords[grid] = keyword;
        var matches = FindGridSearchMatches(grid, keyword).ToList();
        if (matches.Count == 0)
        {
            SetStatus($"表格搜索未命中：{keyword}");
            grid.Invalidate();
            return;
        }

        var currentRow = grid.CurrentCell?.RowIndex ?? -1;
        var currentColumn = grid.CurrentCell?.ColumnIndex ?? -1;
        var current = matches.FindIndex(match => match.RowIndex == currentRow && match.ColumnIndex == currentColumn);
        var next = current < 0
            ? (forward ? 0 : matches.Count - 1)
            : (forward ? (current + 1) % matches.Count : (current - 1 + matches.Count) % matches.Count);
        SelectSearchableGridMatch(grid, matches[next]);
        SetStatus($"表格搜索：{next + 1}/{matches.Count} {keyword}");
        grid.Invalidate();
    }

    private static IEnumerable<(int RowIndex, int ColumnIndex)> FindGridSearchMatches(DataGridView grid, string keyword)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!row.Visible) continue;
            foreach (DataGridViewCell cell in row.Cells)
            {
                if (!cell.Visible) continue;
                var text = Convert.ToString(cell.FormattedValue, CultureInfo.CurrentCulture);
                if (!string.IsNullOrEmpty(text) && text.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
                {
                    yield return (row.Index, cell.ColumnIndex);
                }
            }
        }
    }

    private static void SelectSearchableGridMatch(DataGridView grid, (int RowIndex, int ColumnIndex) match)
    {
        grid.CurrentCell = grid.Rows[match.RowIndex].Cells[match.ColumnIndex];
        grid.ClearSelection();
        grid.Rows[match.RowIndex].Selected = true;
        TryScrollGridRowIntoView(grid, match.RowIndex);
    }

    private void HandleSearchableGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView grid ||
            e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            !_searchableGridKeywords.TryGetValue(grid, out var keyword) ||
            string.IsNullOrWhiteSpace(keyword))
        {
            return;
        }

        var text = Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture);
        if (string.IsNullOrEmpty(text) || !text.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        e.Handled = true;
        var paintParts = e.PaintParts & ~DataGridViewPaintParts.ContentForeground;
        e.Paint(e.CellBounds, paintParts);

        var graphics = e.Graphics;
        if (graphics == null)
        {
            return;
        }

        var bounds = e.CellBounds;
        bounds.Inflate(-4, -2);
        using var clip = new Region(bounds);
        using var oldClip = graphics.Clip?.Clone();
        graphics.Clip = clip;

        var cellStyle = e.CellStyle ?? grid.DefaultCellStyle;
        var font = cellStyle.Font ?? grid.Font;
        var foreColor = (e.State & DataGridViewElementStates.Selected) != 0
            ? cellStyle.SelectionForeColor
            : cellStyle.ForeColor;
        if (foreColor.IsEmpty)
        {
            foreColor = SystemColors.ControlText;
        }

        var x = bounds.Left;
        var y = bounds.Top + Math.Max(0, (bounds.Height - font.Height) / 2);
        var start = 0;
        while (start < text.Length && x < bounds.Right)
        {
            var index = text.IndexOf(keyword, start, StringComparison.CurrentCultureIgnoreCase);
            if (index < 0)
            {
                DrawSearchTextSegment(graphics, text[start..], font, foreColor, ref x, y);
                break;
            }

            if (index > start)
            {
                DrawSearchTextSegment(graphics, text[start..index], font, foreColor, ref x, y);
            }

            var matchText = text.Substring(index, keyword.Length);
            var matchSize = TextRenderer.MeasureText(graphics, matchText, font, Size.Empty, TextFormatFlags.NoPadding);
            var highlight = new Rectangle(x, y - 1, Math.Max(4, matchSize.Width), font.Height + 2);
            using (var brush = new SolidBrush(grid.CurrentCell?.RowIndex == e.RowIndex && grid.CurrentCell.ColumnIndex == e.ColumnIndex
                       ? SearchHitCurrentBackColor
                       : SearchHitBackColor))
            {
                graphics.FillRectangle(brush, highlight);
            }
            DrawSearchTextSegment(graphics, matchText, font, Color.Black, ref x, y);
            start = index + keyword.Length;
        }

        if (oldClip != null)
        {
            graphics.Clip = oldClip;
        }
    }

    private static void DrawSearchTextSegment(Graphics graphics, string text, Font font, Color color, ref int x, int y)
    {
        if (string.IsNullOrEmpty(text)) return;
        var size = TextRenderer.MeasureText(graphics, text, font, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, text, font, new Point(x, y), color, TextFormatFlags.NoPadding);
        x += size.Width;
    }

    private void ApplySearchableTreeSearch(TreeView tree, string keyword)
    {
        RegisterSearchableTree(tree);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _searchableTreeKeywords.Remove(tree);
        }
        else
        {
            _searchableTreeKeywords[tree] = keyword;
        }
        TreeNode? first = null;
        tree.BeginUpdate();
        try
        {
            foreach (TreeNode root in tree.Nodes)
            {
                foreach (var node in EnumerateScriptTreeNodes(root))
                {
                    var hit = !string.IsNullOrWhiteSpace(keyword) &&
                              node.Text.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
                    node.BackColor = hit ? SearchHitBackColor : Color.Empty;
                    node.NodeFont = hit ? new Font(tree.Font, FontStyle.Bold) : null;
                    first ??= hit ? node : null;
                }
            }
        }
        finally
        {
            tree.EndUpdate();
        }

        if (string.IsNullOrWhiteSpace(keyword))
        {
            SetStatus("树搜索已清除");
            return;
        }

        if (first != null)
        {
            tree.SelectedNode = first;
            first.EnsureVisible();
            SetStatus($"树搜索命中：{keyword}");
        }
        else
        {
            SetStatus($"树搜索未命中：{keyword}");
        }
    }

    private void HandleSearchableTreeDrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        if (sender is not TreeView tree || e.Node == null)
        {
            return;
        }

        var graphics = e.Graphics;
        var node = e.Node;
        var text = node.Text;
        var bounds = e.Bounds;
        var selected = (e.State & TreeNodeStates.Selected) != 0;
        var hit = _searchableTreeKeywords.TryGetValue(tree, out var keyword) &&
                  !string.IsNullOrWhiteSpace(keyword) &&
                  text.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            e.DrawDefault = true;
            return;
        }

        var backgroundColor = selected
            ? SystemColors.Highlight
            : !node.BackColor.IsEmpty
                ? node.BackColor
                : tree.BackColor;
        using (var background = new SolidBrush(backgroundColor))
        {
            graphics.FillRectangle(background, bounds);
        }

        var font = node.NodeFont ?? tree.Font;
        var textColor = selected
            ? SystemColors.HighlightText
            : !node.ForeColor.IsEmpty
                ? node.ForeColor
                : tree.ForeColor;
        var x = bounds.Left;
        var y = bounds.Top + Math.Max(0, (bounds.Height - font.Height) / 2);

        if (!hit)
        {
            TextRenderer.DrawText(graphics, text, font, new Point(x, y), textColor, TextFormatFlags.NoPadding);
            return;
        }

        DrawSearchHighlightedText(graphics, text, keyword!, font, textColor, ref x, y, selected);
    }

    private static void DrawSearchHighlightedText(
        Graphics graphics,
        string text,
        string keyword,
        Font font,
        Color textColor,
        ref int x,
        int y,
        bool selected)
    {
        var start = 0;
        while (start < text.Length)
        {
            var index = text.IndexOf(keyword, start, StringComparison.CurrentCultureIgnoreCase);
            if (index < 0)
            {
                DrawSearchTextSegment(graphics, text[start..], font, textColor, ref x, y);
                break;
            }

            if (index > start)
            {
                DrawSearchTextSegment(graphics, text[start..index], font, textColor, ref x, y);
            }

            var matchText = text.Substring(index, keyword.Length);
            var matchSize = TextRenderer.MeasureText(graphics, matchText, font, Size.Empty, TextFormatFlags.NoPadding);
            var highlight = new Rectangle(x, y - 1, Math.Max(4, matchSize.Width), font.Height + 2);
            using (var brush = new SolidBrush(selected ? SearchHitCurrentBackColor : SearchHitBackColor))
            {
                graphics.FillRectangle(brush, highlight);
            }
            DrawSearchTextSegment(graphics, matchText, font, Color.Black, ref x, y);
            start = index + keyword.Length;
        }
    }
}
