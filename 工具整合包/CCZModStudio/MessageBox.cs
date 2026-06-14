using FormsMessageBox = System.Windows.Forms.MessageBox;

namespace CCZModStudio;

internal static class MessageBox
{
    public static DialogResult Show(string text)
    {
        return DialogResult.OK;
    }

    public static DialogResult Show(string text, string caption)
    {
        return DialogResult.OK;
    }

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons)
    {
        return GetSuppressedResult(buttons);
    }

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        return ShouldShow(icon)
            ? FormsMessageBox.Show(text, caption, buttons, icon)
            : GetSuppressedResult(buttons, icon);
    }

    public static DialogResult Show(
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton)
    {
        return ShouldShow(icon)
            ? FormsMessageBox.Show(text, caption, buttons, icon, defaultButton)
            : GetSuppressedResult(buttons, icon);
    }

    public static DialogResult Show(
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton,
        MessageBoxOptions options)
    {
        return ShouldShow(icon)
            ? FormsMessageBox.Show(text, caption, buttons, icon, defaultButton, options)
            : GetSuppressedResult(buttons, icon);
    }

    public static DialogResult Show(IWin32Window owner, string text)
    {
        return DialogResult.OK;
    }

    public static DialogResult Show(IWin32Window owner, string text, string caption)
    {
        return DialogResult.OK;
    }

    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons)
    {
        return GetSuppressedResult(buttons);
    }

    public static DialogResult Show(
        IWin32Window owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon)
    {
        return ShouldShow(icon)
            ? FormsMessageBox.Show(owner, text, caption, buttons, icon)
            : GetSuppressedResult(buttons, icon);
    }

    public static DialogResult Show(
        IWin32Window owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton)
    {
        return ShouldShow(icon)
            ? FormsMessageBox.Show(owner, text, caption, buttons, icon, defaultButton)
            : GetSuppressedResult(buttons, icon);
    }

    public static DialogResult Show(
        IWin32Window owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton,
        MessageBoxOptions options)
    {
        return ShouldShow(icon)
            ? FormsMessageBox.Show(owner, text, caption, buttons, icon, defaultButton, options)
            : GetSuppressedResult(buttons, icon);
    }

    private static bool ShouldShow(MessageBoxIcon icon)
    {
        return icon == MessageBoxIcon.Error;
    }

    private static DialogResult GetSuppressedResult(MessageBoxButtons buttons)
    {
        return buttons switch
        {
            MessageBoxButtons.AbortRetryIgnore => DialogResult.Ignore,
            MessageBoxButtons.OK => DialogResult.OK,
            MessageBoxButtons.OKCancel => DialogResult.OK,
            MessageBoxButtons.RetryCancel => DialogResult.Retry,
            MessageBoxButtons.YesNo => DialogResult.Yes,
            MessageBoxButtons.YesNoCancel => DialogResult.Yes,
            _ => DialogResult.OK
        };
    }

    private static DialogResult GetSuppressedResult(MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        if (buttons == MessageBoxButtons.YesNo && icon == MessageBoxIcon.Information)
        {
            return DialogResult.No;
        }

        return GetSuppressedResult(buttons);
    }
}
