using System.Globalization;

namespace CCZModStudio;

internal enum LegacyMfcControlKind
{
    Label,
    TextBox,
    ComboBox,
    CheckBox,
    ListBox,
    Button
}

internal sealed class LegacyMfcDialogSpec
{
    public required string DialogName { get; init; }
    public required string ResourceId { get; init; }
    public Size DialogUnits { get; init; }
    public List<LegacyMfcControlSpec> Controls { get; } = [];
    public Action<LegacyMfcDialogSession>? Initialize { get; init; }
    public Func<LegacyMfcDialogSession, string?>? Commit { get; init; }
}

internal sealed class LegacyMfcControlSpec
{
    public required LegacyMfcControlKind Kind { get; init; }
    public required string Id { get; init; }
    public string Text { get; init; } = string.Empty;
    public Rectangle DialogUnits { get; init; }
    public bool Multiline { get; init; }
    public bool Scrollable { get; init; }
    public bool Sorted { get; init; }
    public int ItemHeight { get; init; }
}

internal sealed class LegacyMfcDialogSession
{
    private readonly Dictionary<string, Control> _controls;

    public LegacyMfcDialogSession(
        LegacyScenarioItemDataAccessor data,
        LegacyMfcDialogDataSources dataSources,
        Dictionary<string, Control> controls,
        int commandCount,
        int precedingSameCommandCount)
    {
        Data = data;
        DataSources = dataSources;
        _controls = controls;
        CommandCount = Math.Max(0, commandCount);
        PrecedingSameCommandCount = Math.Max(0, precedingSameCommandCount);
    }

    public LegacyScenarioItemDataAccessor Data { get; }
    public LegacyMfcDialogDataSources DataSources { get; }
    public int CommandCount { get; }
    public int PrecedingSameCommandCount { get; }

    public TextBox TextBox(string id) => (TextBox)_controls[id];
    public ComboBox ComboBox(string id) => (ComboBox)_controls[id];
    public CheckBox CheckBox(string id) => (CheckBox)_controls[id];
    public ListBox ListBox(string id) => (ListBox)_controls[id];

    public bool TryGetControl<T>(string id, out T control) where T : Control
    {
        if (_controls.TryGetValue(id, out var value) && value is T typed)
        {
            control = typed;
            return true;
        }

        control = null!;
        return false;
    }

    public void SetText(string id, string text)
    {
        if (_controls.TryGetValue(id, out var control))
        {
            control.Text = text;
        }
    }

    public string GetText(string id)
        => _controls.TryGetValue(id, out var control) ? control.Text : string.Empty;

    public void SetCheck(string id, bool value)
    {
        if (_controls.TryGetValue(id, out var control) && control is CheckBox checkBox)
        {
            checkBox.Checked = value;
        }
    }

    public bool GetCheck(string id)
        => _controls.TryGetValue(id, out var control) && control is CheckBox checkBox && checkBox.Checked;

    public void SetVisible(string id, bool value)
    {
        if (_controls.TryGetValue(id, out var control))
        {
            control.Visible = value;
        }
    }

    public void SetVisible(params (string Id, bool Value)[] bindings)
    {
        foreach (var (id, value) in bindings)
        {
            SetVisible(id, value);
        }
    }

    public void SetEnabled(string id, bool value)
    {
        if (_controls.TryGetValue(id, out var control))
        {
            control.Enabled = value;
        }
    }

    public void SetEnabled(params (string Id, bool Value)[] bindings)
    {
        foreach (var (id, value) in bindings)
        {
            SetEnabled(id, value);
        }
    }

    public void SetComboItems(string id, IEnumerable<string> items, int selectedIndex = 0)
    {
        if (!_controls.TryGetValue(id, out var control) || control is not ComboBox comboBox)
        {
            return;
        }

        comboBox.BeginUpdate();
        comboBox.Items.Clear();
        foreach (var item in items)
        {
            comboBox.Items.Add(item);
        }
        comboBox.EndUpdate();
        SetComboIndex(id, selectedIndex);
    }

    public void AddComboItems(string id, IEnumerable<string> items)
    {
        if (!_controls.TryGetValue(id, out var control) || control is not ComboBox comboBox)
        {
            return;
        }

        comboBox.BeginUpdate();
        foreach (var item in items)
        {
            comboBox.Items.Add(item);
        }
        comboBox.EndUpdate();
    }

    public bool ComboContains(string id, string text)
        => _controls.TryGetValue(id, out var control) &&
           control is ComboBox comboBox &&
           comboBox.Items.Cast<object>().Any(item => string.Equals(Convert.ToString(item), text, StringComparison.Ordinal));

    public int GetComboIndex(string id)
        => _controls.TryGetValue(id, out var control) && control is ComboBox comboBox
            ? comboBox.SelectedIndex
            : 0;

    public void SetComboIndex(string id, int selectedIndex)
    {
        if (!_controls.TryGetValue(id, out var control) || control is not ComboBox comboBox)
        {
            return;
        }

        if (comboBox.Items.Count == 0 || selectedIndex < 0)
        {
            comboBox.SelectedIndex = -1;
            return;
        }

        comboBox.SelectedIndex = Math.Clamp(selectedIndex, 0, comboBox.Items.Count - 1);
    }

    public void SetListItems(string id, IEnumerable<string> items, int selectedIndex = 0)
    {
        if (!_controls.TryGetValue(id, out var control) || control is not ListBox listBox)
        {
            return;
        }

        listBox.BeginUpdate();
        listBox.Items.Clear();
        foreach (var item in items)
        {
            listBox.Items.Add(item);
        }
        listBox.EndUpdate();
        if (listBox.Items.Count > 0)
        {
            listBox.SelectedIndex = Math.Clamp(selectedIndex, 0, listBox.Items.Count - 1);
        }
    }

    public void SetListItem(string id, int index, string text)
    {
        if (!_controls.TryGetValue(id, out var control) || control is not ListBox listBox)
        {
            return;
        }

        if (index < 0 || index >= listBox.Items.Count)
        {
            return;
        }

        listBox.Items[index] = text;
    }

    public int GetListIndex(string id)
        => _controls.TryGetValue(id, out var control) && control is ListBox listBox
            ? listBox.SelectedIndex
            : 0;

    public void BindComboSelectionChanged(string id, EventHandler handler)
    {
        if (_controls.TryGetValue(id, out var control) && control is ComboBox comboBox)
        {
            comboBox.SelectedIndexChanged += handler;
        }
    }

    public void BindListSelectionChanged(string id, EventHandler handler)
    {
        if (_controls.TryGetValue(id, out var control) && control is ListBox listBox)
        {
            listBox.SelectedIndexChanged += handler;
        }
    }

    public void BindButtonClick(string id, EventHandler handler)
    {
        if (_controls.TryGetValue(id, out var control) && control is Button button)
        {
            button.Click += handler;
        }
    }

    public static bool TryParseInteger(string text, out int value)
    {
        value = ParseLegacyInteger(text);
        return true;
    }

    public static int ParseLegacyInteger(string? text)
    {
        var valueText = text ?? string.Empty;
        if (valueText.Length == 0) return 0;

        if (valueText[0] is 'x' or 'X')
        {
            unchecked
            {
                var sum = 0u;
                for (var i = 1; i < valueText.Length; i++)
                {
                    sum *= 16;
                    var ch = valueText[i];
                    if (ch is >= '0' and <= '9') sum += (uint)(ch - '0');
                    else if (ch is >= 'a' and <= 'f') sum += (uint)(ch - 'a' + 10);
                    else if (ch is >= 'A' and <= 'F') sum += (uint)(ch - 'A' + 10);
                }

                return (int)sum;
            }
        }

        unchecked
        {
            var sign = false;
            var start = 0;
            if (valueText[0] == '-')
            {
                sign = true;
                start = 1;
            }

            var sum = 0;
            for (var i = start; i < valueText.Length; i++)
            {
                sum *= 10;
                sum += valueText[i] - '0';
            }

            return sign ? -sum : sum;
        }
    }

    public bool TryReadEditInt(string id, out int value, out string error)
    {
        if (!TryParseInteger(GetText(id), out value))
        {
            error = $"控件 {id} 的值不是有效整数。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

internal sealed class LegacyScenarioItemDataAccessor
{
    private readonly Models.LegacyScenarioItemData _itemData;

    public LegacyScenarioItemDataAccessor(Models.LegacyScenarioItemData itemData)
    {
        _itemData = itemData;
    }

    public int Id => _itemData.Id;
    public int Ord => _itemData.Ord;

    public string Text
    {
        get => _itemData.LongCharData ?? string.Empty;
        set => _itemData.LongCharData = value ?? string.Empty;
    }

    public int GetInt(int index, int fallback = 0)
        => index >= 0 && index < _itemData.IntData.Count ? _itemData.IntData[index] : fallback;

    public void SetInt(int index, int value)
    {
        EnsureIntSize(index + 1);
        _itemData.IntData[index] = value;
    }

    public IReadOnlyList<int> IntData => _itemData.IntData;

    public void EnsureIntSize(int size)
    {
        while (_itemData.IntData.Count < size)
        {
            _itemData.IntData.Add(0);
        }
    }
}
