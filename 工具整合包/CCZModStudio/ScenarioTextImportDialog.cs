using System.ComponentModel;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class ScenarioTextImportDialog : Form
{
    private readonly ScenarioTextImportService _service;
    private readonly Func<string, ScenarioTextImportParseResult> _parse;
    private readonly string _templateText;
    private readonly TextBox _inputBox = new();
    private readonly DataGridView _previewGrid = new();
    private readonly TextBox _errorBox = new();
    private readonly Button _parseButton = new();
    private readonly Button _templateButton = new();
    private readonly Button _okButton = new();
    private readonly Button _cancelButton = new();

    private ScenarioTextImportParseResult _currentResult = new(
        Array.Empty<LegacyScenarioCommandNode>(),
        Array.Empty<ScenarioTextImportPreviewRow>(),
        Array.Empty<ScenarioTextImportError>());

    public ScenarioTextImportDialog(
        string targetText,
        ScenarioTextImportService service,
        Func<string, ScenarioTextImportParseResult> parse,
        string templateText)
    {
        _service = service;
        _parse = parse;
        _templateText = templateText;

        Text = "文本导入";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowIcon = false;
        ImportExportDialogLayout.Apply(this, new Size(1180, 760), new Size(900, 560));

        BuildLayout(targetText);
        SeedExample();
        ParseInput();
    }

    public IReadOnlyList<LegacyScenarioCommandNode> ImportedCommands => _currentResult.Commands;

    private void BuildLayout(string targetText)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 8),
            Text = $"导入位置：{targetText}。确认后会把解析出的指令插入到当前位置下方；仍需点击完整保存剧本才会写入文件。"
        }, 0, 0);

        var importPreviewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 2
        };
        importPreviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        importPreviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        importPreviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(importPreviewLayout, 0, 1);

        ConfigureInputBox();
        importPreviewLayout.Controls.Add(CreateTitledPanel("导入文本", _inputBox), 0, 0);

        ConfigurePreviewGrid();
        importPreviewLayout.Controls.Add(CreateTitledPanel("解析预览", _previewGrid), 1, 0);

        _errorBox.Dock = DockStyle.Fill;
        _errorBox.Multiline = true;
        _errorBox.ReadOnly = true;
        _errorBox.ScrollBars = ScrollBars.Vertical;
        _errorBox.WordWrap = true;
        _errorBox.BorderStyle = BorderStyle.FixedSingle;
        _errorBox.BackColor = Color.FromArgb(250, 250, 250);
        root.Controls.Add(CreateTitledPanel("错误和提示", _errorBox), 0, 2);

        _parseButton.Text = "解析预览";
        _parseButton.AutoSize = true;
        _parseButton.Click += (_, _) => ParseInput();

        _templateButton.Text = "复制AI说明模板";
        _templateButton.AutoSize = true;
        _templateButton.Click += (_, _) => CopyTemplate();

        _okButton.Text = "导入";
        _okButton.AutoSize = true;
        _okButton.Enabled = false;
        _okButton.Click += (_, _) =>
        {
            ParseInput();
            if (!_currentResult.Success)
            {
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        _cancelButton.Text = "取消";
        _cancelButton.AutoSize = true;
        _cancelButton.DialogResult = DialogResult.Cancel;

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_okButton);
        buttonPanel.Controls.Add(_parseButton);
        buttonPanel.Controls.Add(_templateButton);
        root.Controls.Add(buttonPanel, 0, 3);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

    }

    private void ConfigureInputBox()
    {
        _inputBox.Dock = DockStyle.Fill;
        _inputBox.Multiline = true;
        _inputBox.AcceptsReturn = true;
        _inputBox.AcceptsTab = true;
        _inputBox.ScrollBars = ScrollBars.Both;
        _inputBox.WordWrap = false;
        _inputBox.BorderStyle = BorderStyle.FixedSingle;
        _inputBox.Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);
        _inputBox.TextChanged += (_, _) =>
        {
            _okButton.Enabled = false;
            _errorBox.Text = "文本已变更，请点击“解析预览”。";
        };
    }

    private void ConfigurePreviewGrid()
    {
        _previewGrid.Dock = DockStyle.Fill;
        _previewGrid.AllowUserToAddRows = false;
        _previewGrid.AllowUserToDeleteRows = false;
        _previewGrid.AllowUserToResizeRows = false;
        _previewGrid.AutoGenerateColumns = false;
        _previewGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _previewGrid.BorderStyle = BorderStyle.FixedSingle;
        _previewGrid.MultiSelect = false;
        _previewGrid.ReadOnly = true;
        _previewGrid.RowHeadersVisible = false;
        _previewGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        _previewGrid.Columns.Add(TextColumn(nameof(ScenarioTextImportPreviewRow.Index), "序", 48));
        _previewGrid.Columns.Add(TextColumn(nameof(ScenarioTextImportPreviewRow.Line), "行", 54));
        _previewGrid.Columns.Add(TextColumn(nameof(ScenarioTextImportPreviewRow.SourceCommand), "文本命令", 96));
        _previewGrid.Columns.Add(TextColumn(nameof(ScenarioTextImportPreviewRow.CommandId), "ID", 70));
        _previewGrid.Columns.Add(TextColumn(nameof(ScenarioTextImportPreviewRow.CommandName), "剧本指令", 130));
        _previewGrid.Columns.Add(TextColumn(nameof(ScenarioTextImportPreviewRow.Parameters), "参数预览", 520, DataGridViewAutoSizeColumnMode.Fill));
    }

    private void ParseInput()
    {
        _currentResult = _parse(_inputBox.Text);
        _previewGrid.DataSource = new BindingList<ScenarioTextImportPreviewRow>(_currentResult.PreviewRows.ToList());
        _errorBox.Text = BuildErrorText(_currentResult);
        _okButton.Enabled = _currentResult.Success;
    }

    private static string BuildErrorText(ScenarioTextImportParseResult result)
    {
        if (result.Errors.Count > 0)
        {
            return string.Join(Environment.NewLine, result.Errors.Select(error => error.DisplayText));
        }

        return result.Commands.Count > 0
            ? $"解析成功：将导入 {result.Commands.Count} 条指令。"
            : "没有可导入的指令。";
    }

    private void CopyTemplate()
    {
        try
        {
            Clipboard.SetText(_templateText);
            _errorBox.Text = "已复制 AI 说明模板到剪贴板。";
        }
        catch (Exception ex)
        {
            _errorBox.Text = "复制模板失败：" + ex.Message;
        }
    }

    private void SeedExample()
    {
        _inputBox.Text = """
@dialog speaker=0
对白正文

@narration
旁白正文
""";
    }

    private static Panel CreateTitledPanel(string title, Control content)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft
        };
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content);
        panel.Controls.Add(label);
        content.BringToFront();
        return panel;
    }

    private static DataGridViewTextBoxColumn TextColumn(
        string property,
        string header,
        int width,
        DataGridViewAutoSizeColumnMode autoSizeMode = DataGridViewAutoSizeColumnMode.None)
        => new()
        {
            DataPropertyName = property,
            HeaderText = header,
            Width = width,
            AutoSizeMode = autoSizeMode,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
}
