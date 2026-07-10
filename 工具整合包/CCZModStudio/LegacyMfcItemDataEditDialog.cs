using CCZModStudio.Models;
using CCZModStudio.Core;

namespace CCZModStudio;

internal sealed class LegacyMfcItemDataEditDialog : Form
{
    private readonly LegacyMfcDialogHostControl _host = new();

    public LegacyMfcItemDataEditDialog(
        LegacyScenarioItemData target,
        LegacyMfcDialogSpec spec,
        LegacyMfcDialogDataSources dataSources,
        string commandTitle,
        int commandCount,
        int precedingSameCommandCount,
        LegacyTextWrapOptions? textWrapOptions = null)
    {
        Text = $"{spec.DialogName} - {commandTitle}";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.None;

        _host.LoadDialog(target, spec, dataSources, commandCount, precedingSameCommandCount, includeDialogButtons: true);
        _host.ConfigureTextWrapping(textWrapOptions);
        Controls.Add(_host);
        var clientSize = _host.PreferredDialogClientSize;
        ClientSize = clientSize;
        MinimumSize = new Size(Math.Min(360, clientSize.Width + 16), Math.Min(160, clientSize.Height + 39));
        WireDialogButtons(_host);
    }

    private void WireDialogButtons(Control root)
    {
        foreach (var button in EnumerateControls(root).OfType<Button>())
        {
            if (button.Text.Equals("确定", StringComparison.OrdinalIgnoreCase) ||
                button.Name.Equals("IDOK", StringComparison.OrdinalIgnoreCase))
            {
                button.Click += (_, _) => CommitAndClose();
                AcceptButton = button;
            }
            else if (button.Text.Equals("取消", StringComparison.OrdinalIgnoreCase) ||
                     button.Name.Equals("IDCANCEL", StringComparison.OrdinalIgnoreCase))
            {
                button.DialogResult = DialogResult.Cancel;
                CancelButton = button;
            }
        }
    }

    private void CommitAndClose()
    {
        var error = _host.CommitToTarget();
        if (!string.IsNullOrWhiteSpace(error))
        {
            MessageBox.Show(this, error, "参数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in EnumerateControls(child))
            {
                yield return nested;
            }
        }
    }
}
