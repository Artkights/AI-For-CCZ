using CCZModStudio.Core;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace CCZModStudio;

internal sealed class BattlefieldDeploymentBlockEditDialog : Form
{
    private readonly BattlefieldDeploymentRecordDefinition _definition;
    private readonly BindingList<BattlefieldDeploymentBlockEditRow> _rows;
    private readonly DataGridView _grid = new();
    private readonly TextBox _detailBox = new();

    private IReadOnlyList<int> _committedValues = Array.Empty<int>();

    public BattlefieldDeploymentBlockEditDialog(
        string commandTitle,
        LegacyScenarioCommandNode command,
        LegacyMfcDialogDataSources dataSources,
        int precedingSameCommandCount,
        int? preferredParameterIndex)
    {
        _definition = BattlefieldDeploymentRecordDefinition.FromCommandId(command.CommandId)
                      ?? throw new ArgumentException("Only 0x46/0x47 deployment block commands are supported.", nameof(command));
        _rows = new BindingList<BattlefieldDeploymentBlockEditRow>(
            BuildRows(command, dataSources, _definition, precedingSameCommandCount).ToList());

        Text = $"{_definition.Title} - {commandTitle}";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowIcon = false;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(920, 540);

        BuildLayout(command, preferredParameterIndex);
    }

    public IReadOnlyList<int> CommittedValues => _committedValues;

    public int ChangedSlotCount { get; private set; }

    private static IEnumerable<BattlefieldDeploymentBlockEditRow> BuildRows(
        LegacyScenarioCommandNode command,
        LegacyMfcDialogDataSources dataSources,
        BattlefieldDeploymentRecordDefinition definition,
        int precedingSameCommandCount)
    {
        for (var recordIndex = 0; recordIndex < definition.RecordCount; recordIndex++)
        {
            var values = new int[definition.GroupSize];
            var baseIndex = recordIndex * definition.GroupSize;
            for (var slot = 0; slot < definition.GroupSize; slot++)
            {
                var parameterIndex = baseIndex + slot;
                values[slot] = parameterIndex < command.Parameters.Count
                    ? command.Parameters[parameterIndex].IntValue
                    : 0;
            }

            yield return new BattlefieldDeploymentBlockEditRow(
                definition,
                dataSources,
                recordIndex,
                definition.BattlefieldNumberBase + recordIndex + precedingSameCommandCount * definition.RecordCount,
                values);
        }
    }

    private void BuildLayout(LegacyScenarioCommandNode command, int? preferredParameterIndex)
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
            Text = $"{command.CommandIdHex} {command.CommandName}：{_definition.RecordCount} 条记录，每条 {_definition.GroupSize} 个 16 位槽。表格按旧版 Dialog_70 字段展开，未限制创作者修改；保存前仍由完整 S 剧本写回流程备份并复读校验。"
        }, 0, 0);

        ConfigureGrid();
        _grid.DataSource = _rows;
        root.Controls.Add(_grid, 0, 1);

        _detailBox.Dock = DockStyle.Fill;
        _detailBox.Multiline = true;
        _detailBox.ReadOnly = true;
        _detailBox.ScrollBars = ScrollBars.Vertical;
        _detailBox.WordWrap = true;
        _detailBox.BorderStyle = BorderStyle.FixedSingle;
        _detailBox.BackColor = Color.FromArgb(250, 250, 250);
        root.Controls.Add(_detailBox, 0, 2);

        var okButton = new Button
        {
            Text = "确定",
            AutoSize = true
        };
        okButton.Click += (_, _) => CommitAndClose();

        var cancelButton = new Button
        {
            Text = "取消",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);
        root.Controls.Add(buttonPanel, 0, 3);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Shown += (_, _) =>
        {
            SelectInitialRow(preferredParameterIndex);
            ShowCurrentRowDetail();
        };
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        _grid.StandardTab = true;

        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.RecordNumber), "序", 46));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.DisplayOrdinal), "旧编号", 62));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.Person), "武将码", 74, readOnly: false));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.PersonName), "武将", 150));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.Reinforcement), "援军(0/1)", 78, readOnly: false, visible: _definition.HasReinforcement));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.Hidden), "隐藏(0/1)", 78, readOnly: false));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.X), "X", 58, readOnly: false));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.Y), "Y", 58, readOnly: false));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.Direction), "朝向", 64, readOnly: false));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.DirectionName), "朝向名", 82));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.Level), "等级", 64, readOnly: false));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.LevelName), "等级名", 82));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.JobLevel), "兵种级", 70, readOnly: false));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.JobLevelName), "兵种级名", 82));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.Ai), "AI", 58, readOnly: false));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.AiName), "AI名", 108));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.TargetPerson), "目标武将", 82, readOnly: false));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.TargetPersonName), "目标名", 130));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.TargetX), "目标X", 70, readOnly: false));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.TargetY), "目标Y", 70, readOnly: false));
        _grid.Columns.Add(TextColumn(nameof(BattlefieldDeploymentBlockEditRow.RawValues), "原始槽", 220));

        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        _grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                _grid.InvalidateRow(e.RowIndex);
            }

            ShowCurrentRowDetail();
        };
        _grid.CellValidating += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var column = _grid.Columns[e.ColumnIndex];
            if (column.ReadOnly) return;
            if (_grid.Rows[e.RowIndex].DataBoundItem is not BattlefieldDeploymentBlockEditRow row) return;
            var text = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
            if (row.TryValidateProperty(column.DataPropertyName, text, out var error)) return;

            e.Cancel = true;
            MessageBox.Show(this, error, "参数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        _grid.SelectionChanged += (_, _) => ShowCurrentRowDetail();
    }

    private static DataGridViewTextBoxColumn TextColumn(string propertyName, string headerText, int width, bool readOnly = true, bool visible = true)
        => new()
        {
            Name = propertyName,
            DataPropertyName = propertyName,
            HeaderText = headerText,
            Width = width,
            ReadOnly = readOnly,
            Visible = visible,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                WrapMode = DataGridViewTriState.False
            }
        };

    private void SelectInitialRow(int? preferredParameterIndex)
    {
        if (_grid.Rows.Count == 0) return;

        var rowIndex = 0;
        if (preferredParameterIndex.HasValue && preferredParameterIndex.Value >= 0)
        {
            rowIndex = Math.Clamp(preferredParameterIndex.Value / _definition.GroupSize, 0, _grid.Rows.Count - 1);
        }

        _grid.ClearSelection();
        _grid.Rows[rowIndex].Selected = true;
        _grid.CurrentCell = _grid.Rows[rowIndex].Cells[nameof(BattlefieldDeploymentBlockEditRow.Person)];
    }

    private void ShowCurrentRowDetail()
    {
        if (_grid.CurrentRow?.DataBoundItem is not BattlefieldDeploymentBlockEditRow row)
        {
            _detailBox.Clear();
            return;
        }

        _detailBox.Text = row.BuildDetailText();
    }

    private void CommitAndClose()
    {
        Validate();
        _grid.EndEdit();

        var values = new List<int>(_definition.RecordCount * _definition.GroupSize);
        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            var row = _rows[rowIndex];
            if (!row.TryBuildValues(out var rowValues, out var error, out var propertyName))
            {
                MessageBox.Show(this, error, "参数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SelectCell(rowIndex, propertyName);
                return;
            }

            values.AddRange(rowValues);
        }

        ChangedSlotCount = _rows.Sum(row => row.ChangedSlotCount());
        _committedValues = values;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SelectCell(int rowIndex, string propertyName)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
        if (!_grid.Columns.Contains(propertyName)) propertyName = nameof(BattlefieldDeploymentBlockEditRow.Person);
        var column = _grid.Columns[propertyName];
        if (!column.Visible) propertyName = nameof(BattlefieldDeploymentBlockEditRow.Person);

        _grid.ClearSelection();
        _grid.Rows[rowIndex].Selected = true;
        _grid.CurrentCell = _grid.Rows[rowIndex].Cells[propertyName];
        _grid.BeginEdit(true);
    }

    internal static bool TryParseWord16(string text, out int value, out string error)
    {
        value = 0;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            error = "参数值不能为空。";
            return false;
        }

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed[2..] : trimmed[1..];
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var word) || word > ushort.MaxValue)
            {
                error = "16 位十六进制值应为 0x0000 到 0xFFFF。";
                return false;
            }

            value = word > 60000 ? unchecked((ushort)word) - 65536 : (int)word;
            error = string.Empty;
            return true;
        }

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
            value < short.MinValue ||
            value > ushort.MaxValue)
        {
            error = "16 位参数值可填写 -32768 到 65535；也支持 0x0000 到 0xFFFF。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

internal sealed class BattlefieldDeploymentBlockEditRow
{
    private readonly BattlefieldDeploymentRecordDefinition _definition;
    private readonly LegacyMfcDialogDataSources _dataSources;
    private readonly int[] _originalValues;

    public BattlefieldDeploymentBlockEditRow(
        BattlefieldDeploymentRecordDefinition definition,
        LegacyMfcDialogDataSources dataSources,
        int recordIndex,
        int displayOrdinal,
        IReadOnlyList<int> values)
    {
        _definition = definition;
        _dataSources = dataSources;
        RecordIndex = recordIndex;
        RecordNumber = recordIndex + 1;
        DisplayOrdinal = displayOrdinal;
        _originalValues = values.ToArray();

        Person = FormatValue(values, definition.PersonIndex);
        Reinforcement = definition.HasReinforcement ? FormatValue(values, definition.ReinforcementIndex) : string.Empty;
        Hidden = FormatValue(values, definition.HiddenIndex);
        X = FormatValue(values, definition.XIndex);
        Y = FormatValue(values, definition.YIndex);
        Direction = FormatValue(values, definition.DirectionIndex);
        Level = FormatValue(values, definition.LevelIndex);
        JobLevel = FormatValue(values, definition.JobLevelIndex);
        Ai = FormatValue(values, definition.AiIndex);
        TargetPerson = FormatValue(values, definition.TargetPersonIndex);
        TargetX = FormatValue(values, definition.TargetXIndex);
        TargetY = FormatValue(values, definition.TargetYIndex);
    }

    public int RecordIndex { get; }
    public int RecordNumber { get; }
    public int DisplayOrdinal { get; }
    public string Person { get; set; }
    public bool IsBlank => TryBuildRawValues(out var values) &&
                           BattlefieldDeploymentRecordFormatter.IsBlankRecord(_definition, values);
    public string PersonName => IsBlank ? BattlefieldDeploymentRecordFormatter.EmptySlotText : FormatPerson(Person);
    public string DisplayPersonName => PersonName;
    public string Reinforcement { get; set; }
    public string Hidden { get; set; }
    public string X { get; set; }
    public string Y { get; set; }
    public string Direction { get; set; }
    public string DirectionName => FormatListValue(Direction, _dataSources.Direction, "默认方向");
    public string Level { get; set; }
    public string LevelName => FormatListValue(Level, _dataSources.LevelOffsetItems().ToList(), "+0级");
    public string JobLevel { get; set; }
    public string JobLevelName => FormatListValue(JobLevel, BattlefieldDeploymentRecordDefinition.JobLevelItems, "高级");
    public string Ai { get; set; }
    public string AiName => FormatListValue(Ai, _dataSources.Policy, "自定义AI");
    public string TargetPerson { get; set; }
    public string TargetPersonName => IsBlank ? BattlefieldDeploymentRecordFormatter.EmptySlotText : FormatPerson(TargetPerson);
    public string TargetX { get; set; }
    public string TargetY { get; set; }
    public string DisplaySummary => IsBlank
        ? BattlefieldDeploymentRecordFormatter.EmptySlotText
        : $"{PersonName} ({X},{Y}) {AiName}";
    public string RawValues => TryBuildValues(out var values, out _, out _)
        ? string.Join(", ", values.Select(value => value.ToString(CultureInfo.InvariantCulture)))
        : "含待校验值";

    public bool TryBuildValues(out int[] values, out string error, out string propertyName)
    {
        var parsedValues = new int[_definition.GroupSize];
        var parsedError = string.Empty;
        var parsedPropertyName = nameof(Person);

        bool Read(string text, int slot, string name, string property, Func<string, ParsedField> parser)
        {
            if (slot < 0) return true;
            var parsed = parser(text);
            if (parsed.Success)
            {
                parsedValues[slot] = parsed.Value;
                return true;
            }

            parsedPropertyName = property;
            parsedError = $"第 {RecordNumber} 条记录的“{name}”无效：{parsed.Error}";
            return false;
        }

        var success = Read(Person, _definition.PersonIndex, "武将码", nameof(Person), ParsePersonCode) &&
                      Read(Reinforcement, _definition.ReinforcementIndex, "援军", nameof(Reinforcement), ParseCheckValue) &&
                      Read(Hidden, _definition.HiddenIndex, "隐藏", nameof(Hidden), ParseCheckValue) &&
                      Read(X, _definition.XIndex, "X", nameof(X), ParseCoordinate) &&
                      Read(Y, _definition.YIndex, "Y", nameof(Y), ParseCoordinate) &&
                      Read(Direction, _definition.DirectionIndex, "朝向", nameof(Direction), ParseDirection) &&
                      Read(Level, _definition.LevelIndex, "等级", nameof(Level), ParseLevel) &&
                      Read(JobLevel, _definition.JobLevelIndex, "兵种级", nameof(JobLevel), ParseJobLevel) &&
                      Read(Ai, _definition.AiIndex, "AI", nameof(Ai), ParseAi) &&
                      Read(TargetPerson, _definition.TargetPersonIndex, "目标武将", nameof(TargetPerson), ParsePersonCode) &&
                      Read(TargetX, _definition.TargetXIndex, "目标X", nameof(TargetX), ParseCoordinate) &&
                      Read(TargetY, _definition.TargetYIndex, "目标Y", nameof(TargetY), ParseCoordinate);
        values = parsedValues;
        error = parsedError;
        propertyName = parsedPropertyName;
        return success;
    }

    public bool TryValidateProperty(string propertyName, string text, out string error)
    {
        var parsed = propertyName switch
        {
            nameof(Person) or nameof(TargetPerson) => ParsePersonCode(text),
            nameof(Reinforcement) or nameof(Hidden) => ParseCheckValue(text),
            nameof(Direction) => ParseDirection(text),
            nameof(Level) => ParseLevel(text),
            nameof(JobLevel) => ParseJobLevel(text),
            nameof(Ai) => ParseAi(text),
            nameof(X) or nameof(Y) or nameof(TargetX) or nameof(TargetY) => ParseCoordinate(text),
            _ => ParseWord16Field(text)
        };

        error = parsed.Error;
        return parsed.Success;
    }

    public int ChangedSlotCount()
    {
        if (!TryBuildValues(out var values, out _, out _)) return 0;
        var count = 0;
        for (var i = 0; i < Math.Min(values.Length, _originalValues.Length); i++)
        {
            if (values[i] != _originalValues[i]) count++;
        }

        return count;
    }

    public string BuildDetailText()
    {
        TryBuildValues(out var values, out _, out _);
        var builder = new StringBuilder();
        builder.AppendLine($"{_definition.Title} 第 {RecordNumber} 条 / 旧编号 [{DisplayOrdinal}]");
        builder.AppendLine($"武将：{Person} {PersonName}");
        if (IsBlank)
        {
            builder.AppendLine("当前为空位：无");
        }
        if (_definition.HasReinforcement)
        {
            builder.AppendLine($"援军：{Reinforcement}");
        }
        builder.AppendLine($"隐藏：{Hidden}");
        builder.AppendLine($"坐标：({X},{Y})");
        builder.AppendLine($"朝向：{Direction} {DirectionName}");
        builder.AppendLine($"等级：{Level} {LevelName}");
        builder.AppendLine($"兵种级别：{JobLevel} {JobLevelName}");
        builder.AppendLine($"AI：{Ai} {AiName}");
        builder.AppendLine($"AI附加目标显示规则：AI=3/5 使用目标武将；AI=4/6 使用目标坐标；其它 AI 在旧版 Dialog_70 中隐藏附加目标控件但仍保留原槽。");
        builder.AppendLine($"AI目标武将：{TargetPerson} {TargetPersonName}");
        builder.AppendLine($"AI目标坐标：({TargetX},{TargetY})");
        builder.AppendLine();
        builder.AppendLine("槽位对应：");
        for (var i = 0; i < _definition.GroupSize; i++)
        {
            var current = i < values.Length ? values[i] : _originalValues[i];
            var marker = current == _originalValues[i] ? string.Empty : $"  原值={_originalValues[i].ToString(CultureInfo.InvariantCulture)}";
            builder.AppendLine($"  槽 {RecordIndex * _definition.GroupSize + i} / 记录内 {i}：{_definition.SlotName(i)} = {current.ToString(CultureInfo.InvariantCulture)}{marker}");
        }

        return builder.ToString().TrimEnd();
    }

    private ParsedField ParsePersonCode(string text)
    {
        var parsed = ParseWord16Field(text);
        if (!parsed.Success) return parsed;

        if (IsBlankPersonSentinelAllowed(parsed.Value))
        {
            return parsed;
        }

        var listIndex = LegacyMfcDialogDataSources.Per2CodeToList(parsed.Value);
        if (listIndex < 0 || listIndex >= _dataSources.Person2.Count)
        {
            return ParsedField.Fail($"旧版 Person2 选择范围外：{parsed.Value}。可用人物/变量/特殊码需能被旧版 Per2Code2List 映射。");
        }

        return parsed;
    }

    private bool IsBlankPersonSentinelAllowed(int value)
    {
        if (value is not (0 or -1))
        {
            return false;
        }

        if (!TryBuildRawValues(out var values))
        {
            return false;
        }

        if (_definition.PersonIndex >= 0 && _definition.PersonIndex < values.Length)
        {
            values[_definition.PersonIndex] = value;
        }

        return BattlefieldDeploymentRecordFormatter.IsBlankRecord(_definition, values);
    }

    private static ParsedField ParseCheckValue(string text)
    {
        var parsed = ParseWord16Field(text);
        if (!parsed.Success) return parsed;
        return parsed.Value is 0 or 1
            ? parsed
            : ParsedField.Fail("旧版复选框字段只能为 0 或 1。");
    }

    private static ParsedField ParseCoordinate(string text)
        => ParseWord16Field(text);

    private static ParsedField ParseDirection(string text)
    {
        var parsed = ParseWord16Field(text);
        if (!parsed.Success) return parsed;
        if (parsed.Value is >= 0 and <= 3) return parsed;
        if (parsed.Value is -1 or 4) return parsed.Value == -1 ? parsed : ParsedField.Ok(-1);
        return ParsedField.Fail("朝向需为 0-3；旧版“默认方向”保存为 -1，也可输入 4 自动转为 -1。");
    }

    private ParsedField ParseLevel(string text)
    {
        var parsed = ParseWord16Field(text);
        if (!parsed.Success) return parsed;
        var max = Math.Max(0, _dataSources.LevelOffsetItems().Count() - 1);
        return parsed.Value >= 0 && parsed.Value <= max
            ? parsed
            : ParsedField.Fail($"等级下拉索引需为 0 到 {max}。");
    }

    private static ParsedField ParseJobLevel(string text)
    {
        var parsed = ParseWord16Field(text);
        if (!parsed.Success) return parsed;
        return parsed.Value is >= 0 and <= 2
            ? parsed
            : ParsedField.Fail("兵种级别需为 0-2，对应初级/中级/高级。");
    }

    private ParsedField ParseAi(string text)
    {
        var parsed = ParseWord16Field(text);
        if (!parsed.Success) return parsed;
        return parsed.Value >= 0 && parsed.Value < _dataSources.Policy.Count
            ? parsed
            : ParsedField.Fail($"AI 下拉索引需为 0 到 {_dataSources.Policy.Count - 1}。");
    }

    private static ParsedField ParseWord16Field(string text)
    {
        return BattlefieldDeploymentBlockEditDialog.TryParseWord16(text, out var value, out var error)
            ? ParsedField.Ok(value)
            : ParsedField.Fail(error);
    }

    private string FormatPerson(string text)
    {
        if (!BattlefieldDeploymentBlockEditDialog.TryParseWord16(text, out var value, out _))
        {
            return "待校验";
        }

        return FormatPersonCode(value);
    }

    private string FormatPersonCode(int code)
    {
        if (ScriptVariableValueResolver.TryDecodePerson2VariableReference(code, out var variableAddress))
        {
            return "V" + variableAddress.ToString(CultureInfo.InvariantCulture);
        }

        var index = LegacyMfcDialogDataSources.Per2CodeToList(code);
        var value = index >= 0 && index < _dataSources.Person2.Count
            ? _dataSources.Person2[index].Trim()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return code.ToString(CultureInfo.InvariantCulture);
        }

        var prefix = code.ToString(CultureInfo.InvariantCulture);
        if (value.Equals(prefix + ":", StringComparison.Ordinal))
        {
            return prefix;
        }

        return value.StartsWith(prefix + ":", StringComparison.Ordinal)
            ? prefix + "(" + value[(prefix.Length + 1)..] + ")"
            : value;
    }

    private static string FormatListValue(string text, IReadOnlyList<string> values, string fallback)
    {
        if (!BattlefieldDeploymentBlockEditDialog.TryParseWord16(text, out var value, out _))
        {
            return "待校验";
        }

        if (value >= 0 && value < values.Count)
        {
            return values[value];
        }

        return value is -1 or 4 ? fallback : "自定义";
    }

    private static string FormatValue(IReadOnlyList<int> values, int index)
        => index >= 0 && index < values.Count
            ? values[index].ToString(CultureInfo.InvariantCulture)
            : string.Empty;

    private bool TryBuildRawValues(out int[] values)
    {
        var result = new int[_definition.GroupSize];
        var parseFailure = false;

        void Read(string text, int slot)
        {
            if (slot < 0) return;
            if (BattlefieldDeploymentBlockEditDialog.TryParseWord16(text, out var value, out _))
            {
                result[slot] = value;
                return;
            }

            parseFailure = true;
        }

        Read(Person, _definition.PersonIndex);
        Read(Reinforcement, _definition.ReinforcementIndex);
        Read(Hidden, _definition.HiddenIndex);
        Read(X, _definition.XIndex);
        Read(Y, _definition.YIndex);
        Read(Direction, _definition.DirectionIndex);
        Read(Level, _definition.LevelIndex);
        Read(JobLevel, _definition.JobLevelIndex);
        Read(Ai, _definition.AiIndex);
        Read(TargetPerson, _definition.TargetPersonIndex);
        Read(TargetX, _definition.TargetXIndex);
        Read(TargetY, _definition.TargetYIndex);

        values = result;
        return !parseFailure;
    }

    private readonly record struct ParsedField(bool Success, int Value, string Error)
    {
        public static ParsedField Ok(int value) => new(true, value, string.Empty);
        public static ParsedField Fail(string error) => new(false, 0, error);
    }
}
