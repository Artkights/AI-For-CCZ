using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class LegacyMfcItemDataEditDialog : Form
{
    private const float ScaleX = 1.9f;
    private const float ScaleY = 2.1f;

    private readonly LegacyScenarioItemData _target;
    private readonly LegacyScenarioItemData _working;
    private readonly LegacyMfcDialogSpec _spec;
    private readonly LegacyMfcDialogDataSources _dataSources;
    private readonly int _commandCount;
    private readonly int _precedingSameCommandCount;
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);

    public LegacyMfcItemDataEditDialog(
        LegacyScenarioItemData target,
        LegacyMfcDialogSpec spec,
        LegacyMfcDialogDataSources dataSources,
        string commandTitle,
        int commandCount,
        int precedingSameCommandCount)
    {
        _target = target;
        _working = target.CloneSnapshot();
        _spec = spec;
        _dataSources = dataSources;
        _commandCount = Math.Max(0, commandCount);
        _precedingSameCommandCount = Math.Max(0, precedingSameCommandCount);

        Text = $"{spec.DialogName} - {commandTitle}";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.None;

        BuildLayout();
        InitializeWorkingData();
    }

    private void BuildLayout()
    {
        var clientWidth = Math.Max(260, ToPixelsX(_spec.DialogUnits.Width) + 24);
        var clientHeight = Math.Max(110, ToPixelsY(_spec.DialogUnits.Height) + 22);
        ClientSize = new Size(clientWidth, clientHeight);
        MinimumSize = new Size(Math.Min(360, clientWidth + 16), Math.Min(160, clientHeight + 39));

        var surface = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };
        Controls.Add(surface);

        foreach (var controlSpec in _spec.Controls)
        {
            var control = CreateControl(controlSpec);
            control.Bounds = ToPixels(controlSpec.DialogUnits);
            surface.Controls.Add(control);
            RegisterControl(controlSpec.Id, control);
        }
    }

    private Control CreateControl(LegacyMfcControlSpec controlSpec)
    {
        switch (controlSpec.Kind)
        {
            case LegacyMfcControlKind.Label:
                return new Label
                {
                    Text = controlSpec.Text,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true
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
                var button = new Button
                {
                    Text = controlSpec.Text,
                    UseVisualStyleBackColor = true
                };
                if (controlSpec.Id.Equals("IDOK", StringComparison.Ordinal))
                {
                    button.Click += (_, _) => CommitAndClose();
                    AcceptButton = button;
                }
                else if (controlSpec.Id.Equals("IDCANCEL", StringComparison.Ordinal))
                {
                    button.DialogResult = DialogResult.Cancel;
                    CancelButton = button;
                }
                return button;
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
        var session = CreateSession();
        _spec.Initialize?.Invoke(session);
    }

    private void CommitAndClose()
    {
        var session = CreateSession();
        var error = _spec.Commit?.Invoke(session);
        if (!string.IsNullOrWhiteSpace(error))
        {
            MessageBox.Show(this, error, "参数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ApplyWorkingData();
        DialogResult = DialogResult.OK;
        Close();
    }

    private LegacyMfcDialogSession CreateSession()
        => new(new LegacyScenarioItemDataAccessor(_working), _dataSources, _controls, _commandCount, _precedingSameCommandCount);

    private void ApplyWorkingData()
    {
        _target.IntData.Clear();
        _target.IntData.AddRange(_working.IntData);
        _target.LongCharData = _working.LongCharData;
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
