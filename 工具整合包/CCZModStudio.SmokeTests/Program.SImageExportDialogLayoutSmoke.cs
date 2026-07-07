using CCZModStudio;
using CCZModStudio.Core;
using System.Windows.Forms;

internal partial class Program
{
    private static void RunSImageExportDialogLayoutSmoke()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                RunSImageExportDialogLayoutCase(single: true);
                RunSImageExportDialogLayoutCase(single: false);
                Console.WriteLine("S_IMAGE_EXPORT_DIALOG_LAYOUT_SMOKE_OK");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw new InvalidOperationException("S image export dialog layout smoke failed.", failure);
        }
    }

    private static void RunSImageExportDialogLayoutCase(bool single)
    {
        var targets = single
            ? new[]
            {
                new BmpExportTarget { RowId = 1, DisplayName = "曹操", FieldValue = 1, JobId = 0 }
            }
            : new[]
            {
                new BmpExportTarget { RowId = 1, DisplayName = "曹操", FieldValue = 1, JobId = 0 },
                new BmpExportTarget { RowId = 2, DisplayName = "夏侯惇", FieldValue = 250, JobId = 1 }
            };

        using var dialog = new SImageExportDialog(null, targets, CharacterImageResourceService.DefaultSPreviewFactionSlot);
        dialog.Show();
        dialog.PerformLayout();
        DrainSImageExportDialogLayoutEvents();

        if (dialog.ClientSize != new Size(800, 380))
        {
            throw new InvalidOperationException($"S image export dialog client size mismatch: {dialog.ClientSize}.");
        }

        if (dialog.MinimumSize != new Size(760, 420))
        {
            throw new InvalidOperationException($"S image export dialog minimum size mismatch: {dialog.MinimumSize}.");
        }

        AssertSImageExportDialogControlVisible(dialog, "SImageExportOutputFolderBox");
        AssertSImageExportDialogControlVisible(dialog, "SImageExportBrowseButton");
        AssertSImageExportDialogControlVisible(dialog, "SImageExportStage1CheckBox");
        AssertSImageExportDialogControlVisible(dialog, "SImageExportStage2CheckBox");
        AssertSImageExportDialogControlVisible(dialog, "SImageExportStage3CheckBox");
        AssertSImageExportDialogControlVisible(dialog, "SImageExportSelectAllStagesButton");
        AssertSImageExportDialogControlVisible(dialog, "SImageExportOverwriteExistingCheckBox");
        AssertSImageExportDialogControlVisible(dialog, "SImageExportStatusLabel");
        AssertSImageExportDialogControlVisible(dialog, "SImageExportOkButton");
        AssertSImageExportDialogControlVisible(dialog, "SImageExportCancelButton");

        if (!dialog.StageSlots.SequenceEqual(new[] { 1 }))
        {
            throw new InvalidOperationException($"Default S image export stages should be first turn only: {string.Join(",", dialog.StageSlots)}.");
        }

        var selectAll = FindSImageExportDialogControl<Button>(dialog, "SImageExportSelectAllStagesButton");
        selectAll.PerformClick();
        DrainSImageExportDialogLayoutEvents();

        if (!dialog.StageSlots.SequenceEqual(new[] { 1, 2, 3 }))
        {
            throw new InvalidOperationException($"Select-all S image export stages should be 1/2/3: {string.Join(",", dialog.StageSlots)}.");
        }

        dialog.Close();
    }

    private static void AssertSImageExportDialogControlVisible(Form dialog, string name)
    {
        var control = FindSImageExportDialogControl<Control>(dialog, name);
        var bounds = GetControlBoundsRelativeToForm(control, dialog);
        var client = dialog.ClientRectangle;
        if (bounds.Left < client.Left ||
            bounds.Top < client.Top ||
            bounds.Right > client.Right ||
            bounds.Bottom > client.Bottom)
        {
            throw new InvalidOperationException($"{name} is outside dialog client bounds. control={bounds}, client={client}");
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException($"{name} has invalid bounds: {bounds}.");
        }
    }

    private static T FindSImageExportDialogControl<T>(Control root, string name)
        where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child.Name == name)
            {
                return child as T ?? throw new InvalidOperationException($"{name} is not a {typeof(T).Name}.");
            }

            var nested = TryFindSImageExportDialogControl<T>(child, name);
            if (nested != null) return nested;
        }

        throw new InvalidOperationException($"Missing S image export dialog control: {name}.");
    }

    private static T? TryFindSImageExportDialogControl<T>(Control root, string name)
        where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child.Name == name)
            {
                return child as T ?? throw new InvalidOperationException($"{name} is not a {typeof(T).Name}.");
            }

            var nested = TryFindSImageExportDialogControl<T>(child, name);
            if (nested != null) return nested;
        }

        return null;
    }

    private static Rectangle GetControlBoundsRelativeToForm(Control control, Form form)
    {
        var screen = control.Parent?.RectangleToScreen(control.Bounds) ??
            throw new InvalidOperationException($"{control.Name} has no parent.");
        var formClientOrigin = form.PointToScreen(Point.Empty);
        return new Rectangle(
            screen.Left - formClientOrigin.X,
            screen.Top - formClientOrigin.Y,
            screen.Width,
            screen.Height);
    }

    private static void DrainSImageExportDialogLayoutEvents()
    {
        for (var i = 0; i < 4; i++)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }
}
