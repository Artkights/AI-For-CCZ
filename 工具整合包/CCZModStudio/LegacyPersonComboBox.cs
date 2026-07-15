using System.Globalization;

namespace CCZModStudio;

internal enum LegacyPersonComboKind
{
    None,
    Person1,
    Person2
}

/// <summary>
/// Keeps the legacy editor's numeric-prefix lookup while retaining a strict
/// DropDownList value. Typing 1, 2, 3 selects the item whose label starts
/// with "123:"; arbitrary text can never become a script value.
/// </summary>
internal sealed class LegacyPersonComboBox : ComboBox
{
    internal static readonly TimeSpan LookupTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly Dictionary<string, int> _indexByNumber = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? _boundItems;
    private LegacyPersonComboKind _personKind;
    private string _lookupBuffer = string.Empty;
    private DateTime _lastLookupUtc;
    private int _itemPopulationCount;
    private readonly System.Windows.Forms.Timer _lookupResetTimer;

    public LegacyPersonComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        IntegralHeight = false;
        _lookupResetTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)LookupTimeout.TotalMilliseconds
        };
        _lookupResetTimer.Tick += (_, _) => ResetLookup();
    }

    internal LegacyPersonComboKind PersonKind => _personKind;
    internal string LookupBuffer => _lookupBuffer;
    internal int ItemPopulationCount => _itemPopulationCount;

    internal void BindPersonItems(
        IReadOnlyList<string> items,
        LegacyPersonComboKind personKind,
        int selectedIndex)
    {
        var alreadyBound = _personKind == personKind &&
                           ReferenceEquals(_boundItems, items) &&
                           Items.Count == items.Count;
        if (!alreadyBound)
        {
            BeginUpdate();
            try
            {
                Items.Clear();
                Items.AddRange(items.Cast<object>().ToArray());
            }
            finally
            {
                EndUpdate();
            }

            _boundItems = items;
            _personKind = personKind;
            _itemPopulationCount++;
            BuildNumberIndex();
        }

        ResetLookup();
        SetSelectedIndex(selectedIndex);
    }

    internal bool ProcessLookupDigit(char digit, DateTime utcNow)
    {
        if (_personKind == LegacyPersonComboKind.None || digit is < '0' or > '9')
        {
            return false;
        }

        if (_lookupBuffer.Length == 0 || utcNow - _lastLookupUtc > LookupTimeout)
        {
            _lookupBuffer = digit.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            _lookupBuffer += digit;
        }
        _lastLookupUtc = utcNow;
        RestartLookupResetTimer();

        if (TrySelectLookupBuffer())
        {
            return true;
        }

        // Match the original CMyComboBox behavior: an invalid accumulated
        // prefix starts a fresh lookup from the most recently typed digit.
        _lookupBuffer = digit.ToString(CultureInfo.InvariantCulture);
        return TrySelectLookupBuffer();
    }

    internal bool ProcessLookupBackspace(DateTime utcNow)
    {
        if (_personKind == LegacyPersonComboKind.None || _lookupBuffer.Length == 0)
        {
            return false;
        }

        if (utcNow - _lastLookupUtc > LookupTimeout)
        {
            ResetLookup();
            return true;
        }

        _lookupBuffer = _lookupBuffer[..^1];
        _lastLookupUtc = utcNow;
        if (_lookupBuffer.Length > 0)
        {
            TrySelectLookupBuffer();
            RestartLookupResetTimer();
        }
        else
        {
            _lookupResetTimer.Stop();
        }
        return true;
    }

    internal bool ProcessLookupKey(Keys key, DateTime utcNow)
    {
        int digitValue;
        if (key >= Keys.D0 && key <= Keys.D9)
        {
            digitValue = key - Keys.D0;
        }
        else if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
        {
            digitValue = key - Keys.NumPad0;
        }
        else
        {
            return false;
        }

        var digit = (char)('0' + digitValue);
        ProcessLookupDigit(digit, utcNow);
        return _personKind != LegacyPersonComboKind.None;
    }

    internal void ResetLookup()
    {
        _lookupResetTimer.Stop();
        _lookupBuffer = string.Empty;
        _lastLookupUtc = default;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (_personKind != LegacyPersonComboKind.None && e.KeyChar is >= '0' and <= '9')
        {
            ProcessLookupDigit(e.KeyChar, DateTime.UtcNow);
            e.Handled = true;
            return;
        }

        base.OnKeyPress(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Control && !e.Alt && !e.Shift && ProcessLookupKey(e.KeyCode, DateTime.UtcNow))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Back && ProcessLookupBackspace(DateTime.UtcNow))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode is Keys.Escape or Keys.Up or Keys.Down)
        {
            ResetLookup();
        }

        base.OnKeyDown(e);
    }

    protected override void OnDropDown(EventArgs e)
    {
        ResetLookup();
        base.OnDropDown(e);
    }

    protected override void OnLeave(EventArgs e)
    {
        ResetLookup();
        base.OnLeave(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lookupResetTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildNumberIndex()
    {
        _indexByNumber.Clear();
        for (var index = 0; index < Items.Count; index++)
        {
            var text = Convert.ToString(Items[index], CultureInfo.InvariantCulture) ?? string.Empty;
            var colon = text.IndexOf(':');
            if (colon <= 0) continue;
            var prefix = text[..colon];
            if (!prefix.All(char.IsDigit)) continue;
            _indexByNumber.TryAdd(prefix, index);
        }
    }

    private bool TrySelectLookupBuffer()
    {
        if (!_indexByNumber.TryGetValue(_lookupBuffer, out var index))
        {
            return false;
        }

        if (SelectedIndex == index)
        {
            OnSelectedIndexChanged(EventArgs.Empty);
        }
        else
        {
            SetSelectedIndex(index);
        }
        return true;
    }

    private void RestartLookupResetTimer()
    {
        if (IsDisposed) return;
        _lookupResetTimer.Stop();
        _lookupResetTimer.Start();
    }

    private void SetSelectedIndex(int selectedIndex)
    {
        if (Items.Count == 0 || selectedIndex < 0)
        {
            SelectedIndex = -1;
            return;
        }

        SelectedIndex = Math.Clamp(selectedIndex, 0, Items.Count - 1);
    }
}
