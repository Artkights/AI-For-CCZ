using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class PixelColorReplaceDialog : Form
{
    internal enum PickFieldKind
    {
        Source,
        Target
    }

    private readonly IReadOnlyList<PixelEditResourcePage> _pages;
    private readonly PixelColorReplacementService _service = new();
    private readonly List<RuleRow> _rows = new();
    private readonly TextBox _previewBox = new();
    private readonly Button _okButton = new();
    private readonly ToolTip _toolTip = new();
    private readonly IReadOnlyList<ColorChoice> _sourceChoices;
    private readonly IReadOnlyList<ColorChoice> _targetChoices;
    private readonly bool _paletteRestricted;
    private bool _applying;

    public PixelColorReplaceDialog(IReadOnlyList<PixelEditResourcePage> pages, string scopeDescription)
    {
        _pages = pages;
        Text = "整组换色 - " + scopeDescription;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        ImportExportDialogLayout.Apply(this, new Size(1040, 760), new Size(900, 650));
        AutoScroll = false;

        var counts = _service.CountColors(pages.Select(page => page.Document.Bitmap));
        _sourceChoices = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Select(pair => new ColorChoice(Color.FromArgb(pair.Key), pair.Value, BuildColorText(Color.FromArgb(pair.Key), pair.Value)))
            .ToArray();

        _paletteRestricted = pages.Any(page =>
            page.Document.Target.Kind is EditableImageTargetKind.E5RawStrip or EditableImageTargetKind.DllBitmapIcon &&
            page.Document.Palette.Count > 0);
        var targetColors = new List<Color> { Color.Transparent };
        foreach (var color in pages.SelectMany(page => page.Document.Palette)) AddDistinct(targetColors, color);
        foreach (var choice in _sourceChoices) AddDistinct(targetColors, choice.Color);
        _targetChoices = targetColors
            .Take(512)
            .Select(color => new ColorChoice(color, 0, BuildColorText(color, null)))
            .ToArray();

        Controls.Add(BuildRoot(scopeDescription));
        UpdatePreview();
    }

    public event EventHandler<PixelColorPickRequestedEventArgs>? PickRequested;
    public event EventHandler? ApplyRequested;

    public IReadOnlyList<PixelColorReplacementRule> Rules { get; private set; } = Array.Empty<PixelColorReplacementRule>();
    public PixelColorReplacementPreview? Preview { get; private set; }
    public bool IsApplying => _applying;

    internal IReadOnlyList<int> RuleRowHeightsForSmoke => _rows.Select(row => row.Source.Height).ToArray();
    internal IReadOnlyList<int> ComboItemHeightsForSmoke => _rows.Select(row => row.Source.ItemHeight).ToArray();
    internal bool HasHorizontalScrollForSmoke => AutoScroll && HorizontalScroll.Visible;
    internal string PreviewTextForSmoke => _previewBox.Text;
    internal int RuleCountForSmoke => _rows.Count;

    private Control BuildRoot(string scopeDescription)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 5,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(56)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(360)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(34)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = $"范围：{scopeDescription}。所有映射基于换色前像素并行执行，最多五组。",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Padding = new Padding(0, 0, 0, 8),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var rules = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            ColumnCount = 7,
            RowCount = 6,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Margin = new Padding(0, 0, 0, 10)
        };
        rules.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(70)));
        rules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        rules.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(72)));
        rules.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(64)));
        rules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        rules.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(72)));
        rules.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(120)));
        rules.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(40)));
        for (var index = 0; index < 5; index++)
        {
            rules.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(56)));
        }

        AddHeader(rules, "编号/启用", 0);
        AddHeader(rules, "源颜色（完整组）", 1);
        AddHeader(rules, "取色", 2);
        AddHeader(rules, "替换为", 3);
        AddHeader(rules, "目标颜色", 4);
        AddHeader(rules, "取色", 5);
        AddHeader(rules, "自定义", 6);

        for (var index = 0; index < 5; index++)
        {
            var enabled = new CheckBox
            {
                Text = (index + 1).ToString(),
                Checked = index == 0,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                CheckAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(8, 8, 4, 8)
            };
            var source = BuildColorCombo(_sourceChoices);
            var target = BuildColorCombo(_targetChoices);
            var sourcePick = BuildPickButton();
            var targetPick = BuildPickButton();
            var custom = new Button
            {
                Text = "自定义",
                AutoSize = false,
                Dock = DockStyle.Fill,
                MinimumSize = new Size(ScaleLogical(112), ScaleLogical(40)),
                Enabled = !_paletteRestricted,
                Margin = new Padding(6, 8, 6, 8)
            };

            var row = new RuleRow(index, enabled, source, sourcePick, target, targetPick, custom);
            _rows.Add(row);
            enabled.CheckedChanged += (_, _) => UpdateRowState(row);
            source.SelectedIndexChanged += (_, _) => UpdatePreview();
            target.SelectedIndexChanged += (_, _) => UpdatePreview();
            sourcePick.Click += (_, _) => RequestPick(row.Index, PickFieldKind.Source);
            targetPick.Click += (_, _) => RequestPick(row.Index, PickFieldKind.Target);
            custom.Click += (_, _) => ChooseCustomTarget(row);

            rules.Controls.Add(enabled, 0, index + 1);
            rules.Controls.Add(source, 1, index + 1);
            rules.Controls.Add(sourcePick, 2, index + 1);
            rules.Controls.Add(new Label
            {
                Text = "→",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0),
                Font = new Font(Font, FontStyle.Bold)
            }, 3, index + 1);
            rules.Controls.Add(target, 4, index + 1);
            rules.Controls.Add(targetPick, 5, index + 1);
            rules.Controls.Add(custom, 6, index + 1);
            UpdateRowState(row);
        }
        root.Controls.Add(rules, 0, 1);

        root.Controls.Add(new Label
        {
            Text = "命中预览",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, Math.Max(9f, Font.Size - 1f), FontStyle.Bold)
        }, 0, 2);

        _previewBox.Dock = DockStyle.Fill;
        _previewBox.Multiline = true;
        _previewBox.ReadOnly = true;
        _previewBox.ScrollBars = ScrollBars.Vertical;
        _previewBox.WordWrap = true;
        _previewBox.Font = new Font(Font.FontFamily, 10.5f);
        root.Controls.Add(_previewBox, 0, 3);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        _okButton.Text = "应用换色";
        _okButton.AutoSize = false;
        _okButton.MinimumSize = new Size(ScaleLogical(112), ScaleLogical(40));
        _okButton.Size = _okButton.MinimumSize;
        _okButton.Margin = new Padding(8, 0, 0, 0);
        _okButton.Click += (_, _) => ApplyAndClose();
        var cancel = new Button
        {
            Text = "取消",
            AutoSize = false,
            MinimumSize = new Size(ScaleLogical(96), ScaleLogical(40)),
            Size = new Size(ScaleLogical(96), ScaleLogical(40)),
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(8, 0, 0, 0)
        };
        bottom.Controls.Add(_okButton);
        bottom.Controls.Add(cancel);
        root.Controls.Add(bottom, 0, 4);
        CancelButton = cancel;
        return root;
    }

    private ComboBox BuildColorCombo(IReadOnlyList<ColorChoice> choices)
    {
        var itemHeight = Math.Max(ScaleLogical(32), Font.Height + ScaleLogical(10));
        var combo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = itemHeight,
            IntegralHeight = false,
            DropDownHeight = Math.Max(ScaleLogical(320), itemHeight * 8),
            Margin = new Padding(6, 8, 6, 8),
            Font = Font,
            MinimumSize = new Size(1, ScaleLogical(40)),
            Height = ScaleLogical(40)
        };
        combo.Items.AddRange(choices.Cast<object>().ToArray());
        combo.DrawItem += DrawColorChoice;
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (combo.SelectedItem is ColorChoice choice) _toolTip.SetToolTip(combo, choice.Text);
        };
        return combo;
    }

    private Button BuildPickButton()
    {
        var button = new Button
        {
            Text = "取色",
            AutoSize = false,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(ScaleLogical(64), ScaleLogical(40)),
            Margin = new Padding(4, 8, 4, 8)
        };
        _toolTip.SetToolTip(button, "从主像素画布单击取色");
        return button;
    }

    private static void DrawColorChoice(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (sender is not ComboBox combo || e.Index < 0 || combo.Items[e.Index] is not ColorChoice choice) return;

        var swatchHeight = Math.Min(24, Math.Max(16, e.Bounds.Height - 8));
        var swatch = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top + (e.Bounds.Height - swatchHeight) / 2, 36, swatchHeight);
        using var checkerLight = new SolidBrush(Color.White);
        using var checkerDark = new SolidBrush(Color.LightGray);
        e.Graphics.FillRectangle(checkerLight, swatch);
        e.Graphics.FillRectangle(checkerDark, swatch.Left, swatch.Top, swatch.Width / 2, swatch.Height / 2);
        e.Graphics.FillRectangle(checkerDark, swatch.Left + swatch.Width / 2, swatch.Top + swatch.Height / 2, swatch.Width / 2, swatch.Height / 2);
        using var brush = new SolidBrush(choice.Color);
        e.Graphics.FillRectangle(brush, swatch);
        e.Graphics.DrawRectangle(Pens.DimGray, swatch);

        var textRect = new Rectangle(e.Bounds.Left + 50, e.Bounds.Top, Math.Max(1, e.Bounds.Width - 54), e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            choice.Text,
            e.Font ?? Control.DefaultFont,
            textRect,
            e.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }

    private void UpdateRowState(RuleRow row)
    {
        row.Source.Enabled = row.Enabled.Checked;
        row.SourcePick.Enabled = row.Enabled.Checked;
        row.Target.Enabled = row.Enabled.Checked;
        row.TargetPick.Enabled = row.Enabled.Checked;
        row.Custom.Enabled = row.Enabled.Checked && !_paletteRestricted;
        UpdatePreview();
    }

    private void ChooseCustomTarget(RuleRow row)
    {
        using var dialog = new ColorDialog { AllowFullOpen = true, FullOpen = true, Color = Color.White };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetComboColor(row.Target, SnapTargetColor(dialog.Color));
    }

    public void ApplyPickedColor(int ruleIndex, PickFieldKind fieldKind, Color color)
    {
        if (ruleIndex < 0 || ruleIndex >= _rows.Count) return;

        var row = _rows[ruleIndex];
        var combo = fieldKind == PickFieldKind.Source ? row.Source : row.Target;
        var finalColor = fieldKind == PickFieldKind.Target ? SnapTargetColor(color) : PixelColorReplacementService.NormalizeColor(color);
        SetComboColor(combo, finalColor);
        row.Enabled.Checked = true;
        UpdateRowState(row);
        Show();
        Activate();
    }

    public void CancelPick()
    {
        Show();
        Activate();
    }

    private void UpdatePreview()
    {
        var rules = TryBuildRules(out var error);
        if (rules == null)
        {
            Preview = null;
            _previewBox.Text = error;
            _okButton.Enabled = false;
            return;
        }

        try
        {
            Preview = _service.Preview(ToServiceDocuments(), rules);
            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"并行规则：{rules.Count}；总命中像素：{Preview.TotalMatches:N0}");
            for (var i = 0; i < rules.Count; i++)
            {
                builder.AppendLine($"{i + 1}. {BuildColorText(rules[i].Source, null)} -> {BuildColorText(rules[i].Target, null)}：{Preview.RuleMatchCounts[i]:N0}");
            }
            builder.AppendLine();
            foreach (var document in Preview.Documents)
            {
                builder.AppendLine($"{document.DisplayName}：{document.TotalMatches:N0}（{string.Join(" / ", document.RuleMatchCounts.Select(count => count.ToString("N0")))}）");
            }
            _previewBox.Text = builder.ToString();
            _okButton.Enabled = Preview.TotalMatches > 0;
        }
        catch (Exception ex)
        {
            Preview = null;
            _previewBox.Text = ex.Message;
            _okButton.Enabled = false;
        }
    }

    private void ApplyAndClose()
    {
        var rules = TryBuildRules(out var error);
        if (rules == null)
        {
            MessageBox.Show(this, error, "换色", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Rules = rules;
        _applying = true;
        if (ApplyRequested != null)
        {
            ApplyRequested.Invoke(this, EventArgs.Empty);
        }
        else
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private IReadOnlyList<PixelColorReplacementRule>? TryBuildRules(out string error)
    {
        error = string.Empty;
        var result = new List<PixelColorReplacementRule>();
        foreach (var row in _rows.Where(row => row.Enabled.Checked))
        {
            if (row.Source.SelectedItem is not ColorChoice source || row.Target.SelectedItem is not ColorChoice target)
            {
                error = "每个已启用的换色行都必须选择源颜色和目标颜色。";
                return null;
            }
            result.Add(new PixelColorReplacementRule(source.Color, SnapTargetColor(target.Color)));
        }

        if (result.Count == 0)
        {
            error = "请至少启用一组换色规则。";
            return null;
        }

        try
        {
            _service.Preview(ToServiceDocuments(), result);
            return result;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private Color SnapTargetColor(Color color)
    {
        if (!_paletteRestricted || color.A == 0) return PixelColorReplacementService.NormalizeColor(color);
        var palette = _pages.SelectMany(page => page.Document.Palette).DistinctBy(item => item.ToArgb()).ToArray();
        return palette.Length == 0 ? color : DllBitmapIconCodecService.MapColorToPalette(color, palette);
    }

    private IReadOnlyList<(string Key, string DisplayName, Bitmap Bitmap)> ToServiceDocuments()
        => _pages.Select(page => (page.Key, page.Label, page.Document.Bitmap)).ToArray();

    private static string BuildColorText(Color color, int? count)
    {
        var normalized = PixelColorReplacementService.NormalizeColor(color);
        var name = normalized.A == 0 ? "透明" : $"#{normalized.A:X2}{normalized.R:X2}{normalized.G:X2}{normalized.B:X2}";
        return count.HasValue ? $"{name}  ({count.Value:N0} 像素)" : name;
    }

    private static void AddDistinct(List<Color> colors, Color color)
    {
        color = PixelColorReplacementService.NormalizeColor(color);
        if (colors.All(existing => PixelColorReplacementService.NormalizeArgb(existing) != PixelColorReplacementService.NormalizeArgb(color)))
        {
            colors.Add(color);
        }
    }

    private void SetComboColor(ComboBox combo, Color color)
    {
        color = PixelColorReplacementService.NormalizeColor(color);
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ColorChoice choice &&
                PixelColorReplacementService.NormalizeArgb(choice.Color) == PixelColorReplacementService.NormalizeArgb(color))
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        var newChoice = new ColorChoice(color, 0, BuildColorText(color, null));
        combo.Items.Insert(0, newChoice);
        combo.SelectedIndex = 0;
        _toolTip.SetToolTip(combo, newChoice.Text);
    }

    private void RequestPick(int ruleIndex, PickFieldKind fieldKind)
    {
        if (PickRequested == null) return;
        Hide();
        PickRequested.Invoke(this, new PixelColorPickRequestedEventArgs(ruleIndex, fieldKind));
    }

    internal void RequestPickForSmoke(int ruleIndex, PickFieldKind fieldKind)
        => RequestPick(ruleIndex, fieldKind);

    internal int? GetRuleColorArgbForSmoke(int ruleIndex, PickFieldKind fieldKind)
    {
        if (ruleIndex < 0 || ruleIndex >= _rows.Count) return null;
        var combo = fieldKind == PickFieldKind.Source ? _rows[ruleIndex].Source : _rows[ruleIndex].Target;
        return combo.SelectedItem is ColorChoice choice ? PixelColorReplacementService.NormalizeArgb(choice.Color) : null;
    }

    private void AddHeader(TableLayoutPanel table, string text, int column)
    {
        table.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font, FontStyle.Bold),
            AutoEllipsis = true,
            Margin = new Padding(3)
        }, column, 0);
    }

    private int ScaleLogical(int value)
        => (int)Math.Round(value * DeviceDpi / 96.0);

    private sealed record ColorChoice(Color Color, int Count, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record RuleRow(int Index, CheckBox Enabled, ComboBox Source, Button SourcePick, ComboBox Target, Button TargetPick, Button Custom);
}

internal sealed class PixelColorPickRequestedEventArgs(int ruleIndex, PixelColorReplaceDialog.PickFieldKind fieldKind) : EventArgs
{
    public int RuleIndex { get; } = ruleIndex;
    public PixelColorReplaceDialog.PickFieldKind FieldKind { get; } = fieldKind;
}
