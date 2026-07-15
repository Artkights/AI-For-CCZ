using CCZModStudio.Core;

namespace CCZModStudio;

internal sealed class PixelFactionSelectionDialog : Form
{
    private readonly List<CheckBox> _checks = new();
    private readonly bool _allowMultiple;
    private bool _updating;

    public PixelFactionSelectionDialog(
        string title,
        bool allowMultiple,
        IReadOnlyCollection<int> selectedSlots,
        Func<int, string>? detailBuilder = null)
    {
        _allowMultiple = allowMultiple;
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ImportExportDialogLayout.Apply(this, new Size(480, 280), new Size(440, 250));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = allowMultiple ? "选择换色覆盖的阵营（至少一个）：" : "选择要编辑的兵种 S 阵营：",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8)
        }, 0, 0);
        for (var slot = 1; slot <= 3; slot++)
        {
            var captured = slot;
            var text = $"Faction{slot} {CharacterImageResourceService.BuildSPreviewFactionText(slot)}";
            if (detailBuilder != null) text += "  " + detailBuilder(slot);
            var check = new CheckBox { Text = text, Checked = selectedSlots.Contains(slot), AutoSize = true, Padding = new Padding(8, 5, 0, 5) };
            check.CheckedChanged += (_, _) => EnforceSingleSelection(check);
            _checks.Add(check);
            root.Controls.Add(check, 0, slot);
        }

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "确定", AutoSize = true };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Confirm();
        bottom.Controls.Add(ok);
        bottom.Controls.Add(cancel);
        root.Controls.Add(bottom, 0, 4);
        Controls.Add(root);
        CancelButton = cancel;
    }

    public IReadOnlyList<int> SelectedSlots => _checks
        .Select((check, index) => (check, slot: index + 1))
        .Where(item => item.check.Checked)
        .Select(item => item.slot)
        .ToArray();

    private void EnforceSingleSelection(CheckBox selected)
    {
        if (_allowMultiple || _updating || !selected.Checked) return;
        _updating = true;
        foreach (var check in _checks.Where(check => !ReferenceEquals(check, selected))) check.Checked = false;
        _updating = false;
    }

    private void Confirm()
    {
        if (SelectedSlots.Count == 0)
        {
            MessageBox.Show(this, "请至少选择一个阵营。", "换色", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
