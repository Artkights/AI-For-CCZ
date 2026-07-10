using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class LegacyItemDataEditDialog : Form
{
    private readonly LegacyScenarioItemData _target;
    private readonly LegacyScenarioItemData _working;
    private readonly string _dialogName;
    private readonly int _commandCount;
    private readonly LegacyTextWrapOptions? _textWrapOptions;
    private readonly BindingList<IntDataRow> _rows = [];
    private readonly DataGridView _grid = new();
    private readonly TextBox _textBox = new();
    private readonly Label _textWrapStatusLabel = new();
    private readonly TextBox _trueArrayBox = new();
    private readonly TextBox _falseArrayBox = new();
    private readonly NumericUpDown _jumpTargetInput = new();

    public LegacyItemDataEditDialog(
        LegacyScenarioItemData target,
        string commandTitle,
        string dialogName,
        int commandCount,
        LegacyTextWrapOptions? textWrapOptions = null)
    {
        _target = target;
        _working = target.CloneSnapshot();
        _dialogName = dialogName;
        _commandCount = Math.Max(0, commandCount);
        _textWrapOptions = textWrapOptions;

        Text = $"{dialogName} - {commandTitle}";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowIcon = false;
        MinimumSize = new Size(620, 420);
        Size = new Size(860, 620);

        BuildLayout(commandTitle);
        LoadWorkingData();
    }

    private void BuildLayout(string commandTitle)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 8),
            Text = $"{commandTitle}\r\n旧版修改链路：ItemData.IntData / ItemData.LongCharData -> code_instruct 整体重编码"
        }, 0, 0);

        var pages = new TabControl { Dock = DockStyle.Fill };
        root.Controls.Add(pages, 0, 1);

        var intPage = new TabPage("int_data");
        ConfigureGrid();
        intPage.Controls.Add(_grid);
        pages.TabPages.Add(intPage);

        var textPage = new TabPage("long_char_data");
        var textLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        textLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        textLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _textBox.Dock = DockStyle.Fill;
        _textBox.Multiline = true;
        _textBox.AcceptsReturn = true;
        _textBox.ScrollBars = ScrollBars.Vertical;
        _textBox.WordWrap = true;
        _textBox.TextChanged += (_, _) => ApplyTextWrappingToTextBox();
        _textBox.Leave += (_, _) => ApplyTextWrappingToTextBox();
        _textWrapStatusLabel.AutoSize = true;
        _textWrapStatusLabel.Padding = new Padding(0, 4, 0, 0);
        textLayout.Controls.Add(_textBox, 0, 0);
        textLayout.Controls.Add(_textWrapStatusLabel, 0, 1);
        textPage.Controls.Add(textLayout);
        pages.TabPages.Add(textPage);

        var varPage = new TabPage("Dialog_5 变量数组");
        var varLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        varLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        varLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        varLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        varLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _trueArrayBox.Dock = DockStyle.Fill;
        _trueArrayBox.Multiline = true;
        _trueArrayBox.ScrollBars = ScrollBars.Vertical;
        _falseArrayBox.Dock = DockStyle.Fill;
        _falseArrayBox.Multiline = true;
        _falseArrayBox.ScrollBars = ScrollBars.Vertical;
        varLayout.Controls.Add(new Label { Text = "true 数组：逗号分隔，写入 int_data[0..24]，空白后填 -1", AutoSize = true }, 0, 0);
        varLayout.Controls.Add(_trueArrayBox, 0, 1);
        varLayout.Controls.Add(new Label { Text = "false 数组：逗号分隔，写入 int_data[25..49]，空白后填 -1", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, 0, 2);
        varLayout.Controls.Add(_falseArrayBox, 0, 3);
        varPage.Controls.Add(varLayout);
        pages.TabPages.Add(varPage);

        var jumpPage = new TabPage("Dialog_76 跳转");
        var jumpLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        _jumpTargetInput.Minimum = 0;
        _jumpTargetInput.Maximum = Math.Max(0, _commandCount - 1);
        _jumpTargetInput.Width = 180;
        jumpLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "无条件跳转目标 ord。保存时按旧版规则换算成相对位移。"
        });
        jumpLayout.Controls.Add(_jumpTargetInput);
        jumpPage.Controls.Add(jumpLayout);
        pages.TabPages.Add(jumpPage);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0),
            Text = BuildModeNote()
        }, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        var ok = new Button { Text = "确定", AutoSize = true };
        ok.Click += (_, _) =>
        {
            if (!CommitWorkingData(out var error))
            {
                MessageBox.Show(this, error, "参数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ApplyWorkingData();
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        root.Controls.Add(buttons, 0, 3);

        AcceptButton = ok;
        CancelButton = cancel;

        if (_dialogName == "Dialog_5")
        {
            pages.SelectedTab = varPage;
        }
        else if (_target.Id == 0x76)
        {
            pages.SelectedTab = jumpPage;
        }
        else if (HasText())
        {
            pages.SelectedTab = textPage;
        }
    }

    private string BuildModeNote()
        => _dialogName switch
        {
            "Dialog_5" => "当前按旧版 Dialog_5 语义编辑两个变量数组。",
            "Dialog_76" => "当前按旧版跳转语义编辑目标 ord，不直接编辑最终相对位移。",
            _ when HasText() => "当前命令含 long_char_data，文本页按旧版换行规则处理。",
            _ => "当前命令使用 int_data 槽位编辑；后续可逐个命令补齐旧版下拉/勾选控件。"
        };

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(IntDataRow.Index),
            DataPropertyName = nameof(IntDataRow.Index),
            HeaderText = "槽",
            ReadOnly = true,
            FillWeight = 20
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(IntDataRow.Value),
            DataPropertyName = nameof(IntDataRow.Value),
            HeaderText = "值",
            ReadOnly = false,
            FillWeight = 45
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(IntDataRow.Note),
            DataPropertyName = nameof(IntDataRow.Note),
            HeaderText = "旧版含义",
            ReadOnly = true,
            FillWeight = 90
        });

        _grid.DataSource = _rows;
    }

    private void LoadWorkingData()
    {
        _rows.Clear();
        for (var i = 0; i < _working.IntData.Count; i++)
        {
            _rows.Add(new IntDataRow
            {
                Index = i,
                Value = _working.IntData[i].ToString(CultureInfo.InvariantCulture),
                Note = BuildIntDataNote(i)
            });
        }

        _textBox.Text = (_working.LongCharData ?? string.Empty).Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        _trueArrayBox.Text = FormatLegacyArray(0);
        _falseArrayBox.Text = FormatLegacyArray(25);
        var jumpTarget = _working.IntData.Count > 0 ? _working.IntData[0] : 0;
        _jumpTargetInput.Value = Math.Clamp(jumpTarget, (int)_jumpTargetInput.Minimum, (int)_jumpTargetInput.Maximum);
    }

    private bool CommitWorkingData(out string error)
    {
        error = string.Empty;
        _grid.EndEdit();

        for (var i = 0; i < _rows.Count; i++)
        {
            if (!TryParseInteger(_rows[i].Value, out var value))
            {
                error = $"int_data[{i}] 不是有效整数。";
                return false;
            }
            _working.IntData[i] = value;
        }

        _working.LongCharData = _textBox.Text.Replace("\r", string.Empty, StringComparison.Ordinal);
        if (_textWrapOptions is { Disabled: false })
        {
            var wrapResult = LegacyTextWrapService.Wrap(_working.LongCharData, _textWrapOptions);
            _working.LongCharData = wrapResult.Text;
            UpdateTextWrapStatus(wrapResult);
        }

        if (_dialogName == "Dialog_5")
        {
            if (!TryParseLegacyArray(_trueArrayBox.Text, out var trueValues, out error)) return false;
            if (!TryParseLegacyArray(_falseArrayBox.Text, out var falseValues, out error)) return false;
            WriteLegacyArray(0, trueValues);
            WriteLegacyArray(25, falseValues);
        }

        if (_target.Id == 0x76 && _working.IntData.Count > 0)
        {
            _working.IntData[0] = (int)_jumpTargetInput.Value;
        }

        return true;
    }

    private void ApplyWorkingData()
    {
        _target.IntData.Clear();
        _target.IntData.AddRange(_working.IntData);
        _target.LongCharData = _working.LongCharData;
    }

    private bool _applyingTextWrap;

    private void ApplyTextWrappingToTextBox()
    {
        if (_applyingTextWrap || _textWrapOptions == null || _textWrapOptions.Disabled)
        {
            return;
        }

        var result = LegacyTextWrapService.Wrap(_textBox.Text, _textWrapOptions);
        var displayText = result.Text.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        if (!string.Equals(_textBox.Text, displayText, StringComparison.Ordinal))
        {
            var selectionStart = Math.Min(_textBox.SelectionStart, displayText.Length);
            _applyingTextWrap = true;
            try
            {
                _textBox.Text = displayText;
                _textBox.SelectionStart = selectionStart;
                _textBox.SelectionLength = 0;
            }
            finally
            {
                _applyingTextWrap = false;
            }
        }

        UpdateTextWrapStatus(result);
    }

    private void UpdateTextWrapStatus(LegacyTextWrapResult result)
    {
        if (_textWrapOptions == null)
        {
            _textWrapStatusLabel.Text = string.Empty;
            return;
        }

        _textWrapStatusLabel.ForeColor = result.HasWarnings ? Color.DarkRed : Color.DarkGreen;
        _textWrapStatusLabel.Text = result.HasWarnings
            ? LegacyTextWrapService.FormatDiagnostics(result.Diagnostics)
            : $"每行上限：{_textWrapOptions.LineLimit}；最多 {_textWrapOptions.MaxLines} 行。";
    }

    private bool HasText()
        => !string.IsNullOrEmpty(_target.LongCharData)
           || _target.Command?.Parameters.Any(parameter => parameter.Kind == LegacyScenarioParameterKind.Text) == true;

    private string FormatLegacyArray(int start)
    {
        if (_working.IntData.Count <= start) return string.Empty;
        var values = new List<string>();
        for (var i = start; i < Math.Min(_working.IntData.Count, start + 25); i++)
        {
            if (_working.IntData[i] == -1) break;
            values.Add(_working.IntData[i].ToString(CultureInfo.InvariantCulture));
        }
        return string.Join(",", values);
    }

    private void WriteLegacyArray(int start, IReadOnlyList<int> values)
    {
        EnsureIntDataSize(start + 25);
        var count = Math.Min(25, values.Count);
        for (var i = 0; i < count; i++)
        {
            _working.IntData[start + i] = values[i];
        }
        for (var i = count; i < 25; i++)
        {
            _working.IntData[start + i] = -1;
        }
    }

    private void EnsureIntDataSize(int size)
    {
        while (_working.IntData.Count < size)
        {
            _working.IntData.Add(0);
        }
    }

    private static bool TryParseLegacyArray(string text, out List<int> values, out string error)
    {
        values = [];
        error = string.Empty;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return true;

        foreach (var token in Regex.Split(trimmed, @"[\s,;，；]+").Where(token => token.Length > 0))
        {
            if (!TryParseInteger(token, out var value))
            {
                error = $"数组元素“{token}”不是有效整数。";
                return false;
            }
            values.Add(value);
        }

        if (values.Count > 25)
        {
            error = "旧版 Dialog_5 每组最多 25 个变量。";
            return false;
        }

        return true;
    }

    private static bool TryParseInteger(string text, out int value)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
            {
                value = unchecked((int)parsed);
                return true;
            }

            value = 0;
            return false;
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private string BuildIntDataNote(int index)
        => _target.Id switch
        {
            0x05 when index < 25 => "true 变量数组",
            0x05 when index < 50 => "false 变量数组",
            0x76 when index == 0 => "跳转目标 ord",
            0x76 when index == 1 => "保留槽位/旧版 Dialog 参数",
            0x77 when index == 0 => "左侧变量类型",
            0x77 when index == 1 => "左侧变量编号",
            0x77 when index == 2 => "运算方式",
            0x78 when index == 0 => "整型变量编号",
            0x79 when index == 2 => "比较方式",
            _ => "int_data[" + index.ToString(CultureInfo.InvariantCulture) + "]"
        };

    private sealed class IntDataRow
    {
        public int Index { get; init; }
        public string Value { get; set; } = string.Empty;
        public string Note { get; init; } = string.Empty;
    }
}
