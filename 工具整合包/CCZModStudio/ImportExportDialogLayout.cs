namespace CCZModStudio;

internal static class ImportExportDialogLayout
{
    internal const float FontSize = 12F;
    private const float LegacyScale = FontSize / 9F;
    private const int WorkingAreaMargin = 24;

    public static void Apply(Form dialog, Size legacyPreferredClientSize, Size legacyMinimumClientSize)
    {
        dialog.AutoScaleMode = AutoScaleMode.Dpi;
        dialog.AutoScroll = true;
        dialog.Font = new Font("Microsoft YaHei UI", FontSize, FontStyle.Regular, GraphicsUnit.Point);

        var preferred = Scale(legacyPreferredClientSize);
        var minimum = Scale(legacyMinimumClientSize);
        dialog.ClientSize = preferred;
        var nonClientWidth = Math.Max(0, dialog.Width - dialog.ClientSize.Width);
        var nonClientHeight = Math.Max(0, dialog.Height - dialog.ClientSize.Height);
        dialog.MinimumSize = new Size(minimum.Width + nonClientWidth, minimum.Height + nonClientHeight);

        dialog.Load += (_, _) => ConstrainToWorkingArea(dialog);
        dialog.DpiChanged += (_, _) => dialog.BeginInvoke(() => ConstrainToWorkingArea(dialog));
    }

    private static Size Scale(Size size)
        => new(
            Math.Max(1, (int)Math.Ceiling(size.Width * LegacyScale)),
            Math.Max(1, (int)Math.Ceiling(size.Height * LegacyScale)));

    private static void ConstrainToWorkingArea(Form dialog)
    {
        if (dialog.IsDisposed) return;
        var screen = dialog.Owner != null
            ? Screen.FromControl(dialog.Owner)
            : Screen.FromPoint(Cursor.Position);
        var maxSize = new Size(
            Math.Max(320, screen.WorkingArea.Width - WorkingAreaMargin * 2),
            Math.Max(240, screen.WorkingArea.Height - WorkingAreaMargin * 2));
        var minimum = new Size(
            Math.Min(dialog.MinimumSize.Width, maxSize.Width),
            Math.Min(dialog.MinimumSize.Height, maxSize.Height));
        var size = new Size(
            Math.Clamp(dialog.Width, minimum.Width, maxSize.Width),
            Math.Clamp(dialog.Height, minimum.Height, maxSize.Height));

        dialog.MinimumSize = minimum;
        dialog.Size = size;
    }
}
