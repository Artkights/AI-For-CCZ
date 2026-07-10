using CCZModStudio.Models;
using CCZModStudio.Core;

namespace CCZModStudio;

internal sealed class LegacyMfcDialogHostControl : UserControl
{
    private const float ScaleX = 1.9f;
    private const float ScaleY = 2.1f;
    private const int RowTopTolerance = 8;
    private const int ControlSpacing = 6;
    private const int SurfacePadding = 12;
    private const int LabelHorizontalPadding = 12;
    private const int ComboBoxHorizontalPadding = 38;
    private const int CheckBoxHorizontalPadding = 30;
    private const int ButtonHorizontalPadding = 28;
    private const int MinimumTextBoxWidth = 64;
    private const int MinimumComboBoxWidth = 92;
    private const int MaximumComboBoxWidth = 260;

    private readonly Panel _surface = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true
    };

    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);

    private LegacyScenarioItemData? _target;
    private LegacyScenarioItemData? _working;
    private LegacyMfcDialogSpec? _spec;
    private LegacyMfcDialogDataSources? _dataSources;
    private int _commandCount;
    private int _precedingSameCommandCount;
    private LegacyTextWrapOptions? _textWrapOptions;
    private Action<LegacyTextWrapResult>? _textWrapResultChanged;
    private bool _applyingTextWrap;

    public LegacyMfcDialogHostControl()
    {
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Controls.Add(_surface);
    }

    public bool HasDialog => _target != null && _working != null && _spec != null && _dataSources != null;

    public Size PreferredDialogClientSize
    {
        get
        {
            if (_spec == null) return new Size(260, 110);
            var baseSize = new Size(
                Math.Max(260, ToPixelsX(_spec.DialogUnits.Width) + 24),
                Math.Max(110, ToPixelsY(_spec.DialogUnits.Height) + 22));
            var contentSize = _surface.AutoScrollMinSize;
            if (contentSize.Width <= 0 || contentSize.Height <= 0)
            {
                return baseSize;
            }

            var workingArea = SystemInformation.WorkingArea;
            var maxWidth = Math.Max(baseSize.Width, workingArea.Width - 120);
            var maxHeight = Math.Max(baseSize.Height, workingArea.Height - 120);
            return new Size(
                Math.Min(Math.Max(baseSize.Width, contentSize.Width + 6), maxWidth),
                Math.Min(Math.Max(baseSize.Height, contentSize.Height + 6), maxHeight));
        }
    }

    public void LoadDialog(
        LegacyScenarioItemData target,
        LegacyMfcDialogSpec spec,
        LegacyMfcDialogDataSources dataSources,
        int commandCount,
        int precedingSameCommandCount,
        bool includeDialogButtons)
    {
        _target = target;
        _working = target.CloneSnapshot();
        _spec = spec;
        _dataSources = dataSources;
        _commandCount = Math.Max(0, commandCount);
        _precedingSameCommandCount = Math.Max(0, precedingSameCommandCount);

        BuildLayout(includeDialogButtons);
        InitializeWorkingData();
        AttachTextWrappingHandlers();
        NormalizeControlRowsForReadableText();
    }

    public void ConfigureTextWrapping(LegacyTextWrapOptions? options, Action<LegacyTextWrapResult>? resultChanged = null)
    {
        _textWrapOptions = options;
        _textWrapResultChanged = resultChanged;
        AttachTextWrappingHandlers();
        ApplyTextWrappingToAllTextBoxes(notify: true);
    }

    public void ClearDialog(string message = "请选择左侧指令。")
    {
        _target = null;
        _working = null;
        _spec = null;
        _dataSources = null;
        _commandCount = 0;
        _precedingSameCommandCount = 0;
        _textWrapResultChanged?.Invoke(new LegacyTextWrapResult(string.Empty, Array.Empty<LegacyTextWrapDiagnostic>()));
        _textWrapOptions = null;
        _textWrapResultChanged = null;
        _controls.Clear();
        _surface.Controls.Clear();
        _surface.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = message,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText
        });
    }

    public void ResetWorkingData()
    {
        if (_target == null || _spec == null || _dataSources == null)
        {
            return;
        }

        _working = _target.CloneSnapshot();
        BuildLayout(includeDialogButtons: false);
        InitializeWorkingData();
        AttachTextWrappingHandlers();
        NormalizeControlRowsForReadableText();
    }

    public string? CommitToTarget()
    {
        if (_target == null || _working == null || _spec == null || _dataSources == null)
        {
            return "没有可提交的旧版 Dialog。";
        }

        var error = _spec.Commit?.Invoke(CreateSession());
        if (!string.IsNullOrWhiteSpace(error))
        {
            return error;
        }

        ApplyTextWrappingToWorkingData();

        _target.IntData.Clear();
        _target.IntData.AddRange(_working.IntData);
        _target.LongCharData = _working.LongCharData;
        return null;
    }

    private void AttachTextWrappingHandlers()
    {
        if (_textWrapOptions == null || _controls.Count == 0)
        {
            return;
        }

        foreach (var textBox in GetTextWrapTargetTextBoxes())
        {
            textBox.TextChanged -= HandleWrappedTextBoxTextChanged;
            textBox.TextChanged += HandleWrappedTextBoxTextChanged;
            textBox.Leave -= HandleWrappedTextBoxLeave;
            textBox.Leave += HandleWrappedTextBoxLeave;
        }
    }

    private void HandleWrappedTextBoxTextChanged(object? sender, EventArgs e)
    {
        if (_applyingTextWrap || sender is not TextBox textBox)
        {
            return;
        }

        ApplyTextWrapping(textBox, notify: true);
    }

    private void HandleWrappedTextBoxLeave(object? sender, EventArgs e)
    {
        if (sender is TextBox textBox)
        {
            ApplyTextWrapping(textBox, notify: true);
        }
    }

    private void ApplyTextWrappingToAllTextBoxes(bool notify)
    {
        if (_textWrapOptions == null || _controls.Count == 0)
        {
            return;
        }

        var lastResult = new LegacyTextWrapResult(string.Empty, Array.Empty<LegacyTextWrapDiagnostic>());
        foreach (var textBox in GetTextWrapTargetTextBoxes())
        {
            lastResult = ApplyTextWrapping(textBox, notify: false);
        }

        if (notify)
        {
            _textWrapResultChanged?.Invoke(lastResult);
        }
    }

    private LegacyTextWrapResult ApplyTextWrapping(TextBox textBox, bool notify)
    {
        if (_textWrapOptions == null || _textWrapOptions.Disabled)
        {
            return new LegacyTextWrapResult(textBox.Text, Array.Empty<LegacyTextWrapDiagnostic>());
        }

        var result = LegacyTextWrapService.Wrap(textBox.Text, _textWrapOptions);
        var displayText = result.Text.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        if (!string.Equals(textBox.Text, displayText, StringComparison.Ordinal))
        {
            var selectionStart = Math.Min(textBox.SelectionStart, displayText.Length);
            _applyingTextWrap = true;
            try
            {
                textBox.Text = displayText;
                textBox.SelectionStart = selectionStart;
                textBox.SelectionLength = 0;
            }
            finally
            {
                _applyingTextWrap = false;
            }
        }

        if (notify)
        {
            _textWrapResultChanged?.Invoke(result);
        }

        return result;
    }

    private void ApplyTextWrappingToWorkingData()
    {
        if (_textWrapOptions == null || _textWrapOptions.Disabled || _working == null)
        {
            return;
        }

        var result = LegacyTextWrapService.Wrap(_working.LongCharData ?? string.Empty, _textWrapOptions);
        _working.LongCharData = result.Text;
        _textWrapResultChanged?.Invoke(result);
    }

    private IEnumerable<TextBox> GetTextWrapTargetTextBoxes()
    {
        if (_spec == null)
        {
            yield break;
        }

        var targetIds = GetTextWrapTargetIds(_spec.DialogName).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in _controls)
        {
            if (pair.Value is not TextBox textBox)
            {
                continue;
            }

            if (targetIds.Count == 0)
            {
                if (textBox.Multiline)
                {
                    yield return textBox;
                }
                continue;
            }

            if (targetIds.Contains(pair.Key) || targetIds.Contains(pair.Key.Split('#', 2)[0]))
            {
                yield return textBox;
            }
        }
    }

    private static IReadOnlyList<string> GetTextWrapTargetIds(string dialogName)
        => dialogName switch
        {
            "Dialog_2" or "Dialog_18" or "Dialog_21" or "Dialog_44" or "Dialog_96" or "Dialog_99" => ["IDC_EDIT1"],
            "Dialog_103" or "Dialog_114" => ["IDC_EDIT2"],
            _ => Array.Empty<string>()
        };

    private void BuildLayout(bool includeDialogButtons)
    {
        _controls.Clear();
        _surface.Controls.Clear();
        if (_spec == null) return;

        foreach (var controlSpec in _spec.Controls)
        {
            if (!includeDialogButtons &&
                (controlSpec.Id.Equals("IDOK", StringComparison.Ordinal) ||
                 controlSpec.Id.Equals("IDCANCEL", StringComparison.Ordinal)))
            {
                continue;
            }

            var control = CreateControl(controlSpec);
            control.Name = controlSpec.Id;
            control.Bounds = ToPixels(controlSpec.DialogUnits);
            _surface.Controls.Add(control);
            RegisterControl(controlSpec.Id, control);
        }
    }

    private void NormalizeControlRowsForReadableText()
    {
        if (_surface.Controls.Count == 0)
        {
            _surface.AutoScrollMinSize = Size.Empty;
            return;
        }

        _surface.SuspendLayout();
        try
        {
            foreach (Control control in _surface.Controls)
            {
                EnsureReadableControlSize(control);
            }

            var maxRight = 0;
            var maxBottom = 0;
            foreach (var row in BuildControlRows(_surface.Controls.Cast<Control>()))
            {
                Control? previous = null;
                foreach (var control in row.Where(control => control.Visible).OrderBy(control => control.Left))
                {
                    if (previous != null)
                    {
                        var minimumLeft = previous.Right + ControlSpacing;
                        if (control.Left < minimumLeft)
                        {
                            control.Left = minimumLeft;
                        }
                    }

                    previous = control;
                    maxRight = Math.Max(maxRight, control.Right);
                    maxBottom = Math.Max(maxBottom, control.Bottom);
                }
            }

            _surface.AutoScrollMinSize = new Size(maxRight + SurfacePadding, maxBottom + SurfacePadding);
        }
        finally
        {
            _surface.ResumeLayout();
        }
    }

    private static List<List<Control>> BuildControlRows(IEnumerable<Control> controls)
    {
        var rows = new List<List<Control>>();
        foreach (var control in controls.OrderBy(control => control.Top).ThenBy(control => control.Left))
        {
            var row = rows.FirstOrDefault(existing => Math.Abs(RowTop(existing) - control.Top) <= RowTopTolerance);
            if (row == null)
            {
                rows.Add([control]);
                continue;
            }

            row.Add(control);
        }

        return rows;
    }

    private static int RowTop(IReadOnlyList<Control> row)
        => row.Count == 0 ? 0 : (int)Math.Round(row.Average(control => control.Top));

    private static void EnsureReadableControlSize(Control control)
    {
        switch (control)
        {
            case Label label:
                if (!string.IsNullOrEmpty(label.Text))
                {
                    label.Width = Math.Max(label.Width, MeasureTextWidth(label, label.Text) + LabelHorizontalPadding);
                    label.Height = Math.Max(label.Height, MeasureTextHeight(label, label.Text) + 2);
                }
                break;
            case ComboBox comboBox:
                comboBox.Width = Math.Max(comboBox.Width, GetPreferredComboBoxWidth(comboBox));
                break;
            case CheckBox checkBox:
                checkBox.Width = Math.Max(checkBox.Width, MeasureTextWidth(checkBox, checkBox.Text) + CheckBoxHorizontalPadding);
                checkBox.Height = Math.Max(checkBox.Height, MeasureTextHeight(checkBox, checkBox.Text) + 4);
                break;
            case Button button:
                button.Width = Math.Max(button.Width, MeasureTextWidth(button, button.Text) + ButtonHorizontalPadding);
                button.Height = Math.Max(button.Height, MeasureTextHeight(button, button.Text) + 8);
                break;
            case TextBox { Multiline: false } textBox:
                textBox.Width = Math.Max(textBox.Width, MinimumTextBoxWidth);
                textBox.Height = Math.Max(textBox.Height, textBox.Font.Height + 8);
                break;
        }
    }

    private static int GetPreferredComboBoxWidth(ComboBox comboBox)
    {
        var measured = string.IsNullOrEmpty(comboBox.Text) ? 0 : MeasureTextWidth(comboBox, comboBox.Text);
        foreach (var item in comboBox.Items)
        {
            measured = Math.Max(measured, MeasureTextWidth(comboBox, Convert.ToString(item) ?? string.Empty));
        }

        if (measured == 0)
        {
            return MinimumComboBoxWidth;
        }

        return Math.Clamp(measured + ComboBoxHorizontalPadding, MinimumComboBoxWidth, MaximumComboBoxWidth);
    }

    private static int MeasureTextWidth(Control control, string text)
        => TextRenderer.MeasureText(
            text,
            control.Font,
            new Size(10_000, 10_000),
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;

    private static int MeasureTextHeight(Control control, string text)
        => TextRenderer.MeasureText(
            text,
            control.Font,
            new Size(10_000, 10_000),
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Height;

    private Control CreateControl(LegacyMfcControlSpec controlSpec)
    {
        switch (controlSpec.Kind)
        {
            case LegacyMfcControlKind.Label:
                return new Label
                {
                    Text = controlSpec.Text,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true,
                    UseMnemonic = false
                };
            case LegacyMfcControlKind.TextBox:
                return new TextBox
                {
                    Multiline = controlSpec.Multiline || controlSpec.DialogUnits.Height > 16,
                    ScrollBars = controlSpec.Scrollable ? ScrollBars.Vertical : ScrollBars.None,
                    AcceptsReturn = controlSpec.Multiline,
                    WordWrap = controlSpec.Multiline
                };
            case LegacyMfcControlKind.ComboBox:
                return new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    IntegralHeight = false,
                    Sorted = controlSpec.Sorted
                };
            case LegacyMfcControlKind.CheckBox:
                return new CheckBox
                {
                    Text = controlSpec.Text,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft
                };
            case LegacyMfcControlKind.ListBox:
                var listBox = new ListBox
                {
                    IntegralHeight = false
                };
                if (controlSpec.ItemHeight > 0)
                {
                    listBox.ItemHeight = controlSpec.ItemHeight;
                }
                return listBox;
            case LegacyMfcControlKind.Button:
                return new Button
                {
                    Text = controlSpec.Text,
                    UseVisualStyleBackColor = true
                };
            default:
                return new Label { Text = controlSpec.Text };
        }
    }

    private void RegisterControl(string id, Control control)
    {
        if (!_controls.ContainsKey(id))
        {
            _controls[id] = control;
            return;
        }

        var suffix = 2;
        while (_controls.ContainsKey(id + "#" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            suffix++;
        }
        _controls[id + "#" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture)] = control;
    }

    private void InitializeWorkingData()
    {
        if (_spec == null) return;
        _spec.Initialize?.Invoke(CreateSession());
    }

    private LegacyMfcDialogSession CreateSession()
    {
        if (_working == null || _dataSources == null)
        {
            throw new InvalidOperationException("Legacy MFC dialog host is not initialized.");
        }

        return new LegacyMfcDialogSession(
            new LegacyScenarioItemDataAccessor(_working),
            _dataSources,
            _controls,
            _commandCount,
            _precedingSameCommandCount);
    }

    private static Rectangle ToPixels(Rectangle dialogUnits)
        => new(
            ToPixelsX(dialogUnits.X),
            ToPixelsY(dialogUnits.Y),
            Math.Max(1, ToPixelsX(dialogUnits.Width)),
            Math.Max(1, ToPixelsY(dialogUnits.Height)));

    private static int ToPixelsX(int value)
        => (int)Math.Round(value * ScaleX);

    private static int ToPixelsY(int value)
        => (int)Math.Round(value * ScaleY);
}
