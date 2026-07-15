using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
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
                RunJobSImageBatchDialogLayoutCases();
                RunImportExportDialogTypographyCases();
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

        if (Math.Abs(dialog.Font.SizeInPoints - ImportExportDialogLayout.FontSize) > 0.01F)
        {
            throw new InvalidOperationException($"S image export dialog font mismatch: {dialog.Font.SizeInPoints}pt.");
        }

        if (dialog.ClientSize.Width < 1000 || dialog.ClientSize.Height < 500)
        {
            throw new InvalidOperationException($"S image export dialog was not enlarged: {dialog.ClientSize}.");
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

    private static void RunJobSImageBatchDialogLayoutCases()
    {
        using var exportDialog = new JobSImageBatchDialog(JobSImageBatchDialogMode.Export, new[] { 0, 1 });
        exportDialog.Show();
        exportDialog.PerformLayout();
        DrainSImageExportDialogLayoutEvents();
        AssertImportExportDialogFont(exportDialog, "job S export");
        foreach (var name in new[]
                 {
                     "JobSBatchFolderBox",
                     "JobSBatchBrowseButton",
                     "JobSBatchFaction1CheckBox",
                     "JobSBatchFaction2CheckBox",
                     "JobSBatchFaction3CheckBox",
                     "JobSBatchOverwriteCheckBox",
                     "JobSBatchStatusBox",
                     "JobSBatchOkButton",
                     "JobSBatchCancelButton"
                 })
        {
            AssertSImageExportDialogControlVisible(exportDialog, name);
        }

        if (!exportDialog.FactionSlots.SequenceEqual(new[] { 1 }))
        {
            throw new InvalidOperationException("Job S export should default to Faction1 only.");
        }

        exportDialog.Close();

        using var temp = new TemporarySmokeDirectory("JobSBatchDialog");
        Directory.CreateDirectory(Path.Combine(temp.Path, "Job0", "Faction1"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "Job1", "Faction3"));
        using var importDialog = new JobSImageBatchDialog(JobSImageBatchDialogMode.Import, new[] { 0, 1 });
        var folderBox = FindSImageExportDialogControl<TextBox>(importDialog, "JobSBatchFolderBox");
        folderBox.Text = temp.Path;
        importDialog.Show();
        importDialog.PerformLayout();
        DrainSImageExportDialogLayoutEvents();
        AssertImportExportDialogFont(importDialog, "job S import");
        if (!importDialog.FactionSlots.SequenceEqual(new[] { 1, 3 }))
        {
            throw new InvalidOperationException(
                "Job S import should auto-select detected canonical factions: " + string.Join(",", importDialog.FactionSlots));
        }

        importDialog.Close();
    }

    private static void RunImportExportDialogTypographyCases()
    {
        using var sReplace = new SImageReplaceDialog(0, 0, 1, "Smoke");
        AssertImportExportDialogFont(sReplace, "person S replace");

        using var stageSelection = new SImageStageSelectionDialog("Smoke", new[] { 1, 2, 3 }, "Smoke");
        AssertImportExportDialogFont(stageSelection, "S stage selection");

        using var jobReplace = new JobSImageReplaceDialog(0, "Smoke");
        AssertImportExportDialogFont(jobReplace, "job S replace");

        using var scenarioImport = new ScenarioTextImportDialog(
            "Smoke",
            null!,
            _ => new ScenarioTextImportParseResult(
                Array.Empty<LegacyScenarioCommandNode>(),
                Array.Empty<ScenarioTextImportPreviewRow>(),
                Array.Empty<ScenarioTextImportError>()),
            string.Empty);
        AssertImportExportDialogFont(scenarioImport, "scenario text import");
    }

    private static void AssertImportExportDialogFont(Form dialog, string name)
    {
        if (Math.Abs(dialog.Font.SizeInPoints - ImportExportDialogLayout.FontSize) > 0.01F)
        {
            throw new InvalidOperationException($"{name} dialog font should be 12pt, actual={dialog.Font.SizeInPoints}pt.");
        }

        if (dialog.AutoScaleMode != AutoScaleMode.Dpi || !dialog.AutoScroll)
        {
            throw new InvalidOperationException($"{name} dialog should use DPI autoscaling and scrolling fallback.");
        }
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
