using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class LegacyMfcDialogHostControl : UserControl
{
    private const float ScaleX = 1.9f;
    private const float ScaleY = 2.1f;

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
            return new Size(
                Math.Max(260, ToPixelsX(_spec.DialogUnits.Width) + 24),
                Math.Max(110, ToPixelsY(_spec.DialogUnits.Height) + 22));
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
    }

    public void ClearDialog(string message = "请选择左侧指令。")
    {
        _target = null;
        _working = null;
        _spec = null;
        _dataSources = null;
        _commandCount = 0;
        _precedingSameCommandCount = 0;
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

        _target.IntData.Clear();
        _target.IntData.AddRange(_working.IntData);
        _target.LongCharData = _working.LongCharData;
        return null;
    }

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
