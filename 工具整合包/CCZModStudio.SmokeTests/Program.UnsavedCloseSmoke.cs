using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Data;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    private static void RunUnsavedCloseSmoke()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var cleanForm = new MainForm();
                cleanForm.Show();
                Application.DoEvents();
                var cleanArgs = InvokeMainFormClosingForUnsavedCloseSmoke(cleanForm);
                if (cleanArgs.Cancel)
                {
                    throw new InvalidOperationException("Clean main form close should not be canceled.");
                }

                using var coverageForm = new MainForm();
                coverageForm.Show();
                Application.DoEvents();
                AssertCmAndItemUnsavedCoverage(coverageForm);

                using var dirtyForm = new MainForm();
                dirtyForm.Show();
                Application.DoEvents();
                MakeMapWorkbenchDraftDirtyForUnsavedCloseSmoke(dirtyForm);
                var dirtyArgs = InvokeMainFormClosingForUnsavedCloseSmoke(dirtyForm);
                if (!dirtyArgs.Cancel)
                {
                    throw new InvalidOperationException("Dirty main form close should be canceled before unsaved confirmation.");
                }

                if (!GetPrivateFieldForUnsavedCloseSmoke<bool>(dirtyForm, "_unsavedClosePromptRunning"))
                {
                    throw new InvalidOperationException("Dirty close did not start the unsaved confirmation flow.");
                }

                SetPrivateFieldForUnsavedCloseSmoke(dirtyForm, "_unsavedClosePromptRunning", false);
                SetPrivateFieldForUnsavedCloseSmoke(dirtyForm, "_unsavedCloseConfirmed", true);
                var confirmedArgs = InvokeMainFormClosingForUnsavedCloseSmoke(dirtyForm);
                if (confirmedArgs.Cancel)
                {
                    throw new InvalidOperationException("Confirmed main form close should not be canceled.");
                }

                Console.WriteLine("UNSAVED_CLOSE_SMOKE_OK");
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
            throw new InvalidOperationException("Unsaved close smoke failed.", failure);
        }
    }

    private static FormClosingEventArgs InvokeMainFormClosingForUnsavedCloseSmoke(MainForm form)
    {
        var method = typeof(MainForm).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing MainForm.OnFormClosing.");
        var args = new FormClosingEventArgs(CloseReason.UserClosing, false);
        try
        {
            method.Invoke(form, [args]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }

        return args;
    }

    private static void AssertCmAndItemUnsavedCoverage(MainForm form)
    {
        var gameRoot = Environment.GetEnvironmentVariable("CCZMODSTUDIO_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            throw new InvalidOperationException("Unsaved coverage smoke requires CCZMODSTUDIO_GAME_ROOT.");
        }

        var project = new ProjectDetector().CreateProjectFromGameRoot(gameRoot);
        var tables = new HexTableParser().Load(project.HexTableXmlPath);
        SetPrivateFieldForUnsavedCloseSmoke(form, "_project", project);
        SetPrivateFieldForUnsavedCloseSmoke(form, "_tables", tables);

        InvokePrivateForUnsavedCloseSmoke(form, "ReloadCmSettingsPage", false);
        var cmSession = GetPrivateFieldForUnsavedCloseSmoke<object>(form, "_cmSettingsPageSession");
        var globalGrid = (DataGridView)(cmSession.GetType().GetProperty("GlobalNumericGrid")?.GetValue(cmSession)
            ?? throw new InvalidOperationException("CM settings session does not expose its numeric grid."));
        var globalTable = globalGrid.DataSource as DataTable
            ?? throw new InvalidOperationException("CM global numeric table was not loaded.");
        var globalRow = globalTable.Rows.Cast<DataRow>().FirstOrDefault()
            ?? throw new InvalidOperationException("CM global numeric table has no editable rows.");
        var currentValue = int.Parse(Convert.ToString(globalRow["当前值"]) ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var minValue = Convert.ToInt32(globalRow["MinValue"], System.Globalization.CultureInfo.InvariantCulture);
        var maxValue = Convert.ToInt32(globalRow["MaxValue"], System.Globalization.CultureInfo.InvariantCulture);
        globalRow["新值"] = currentValue < maxValue ? currentValue + 1 : Math.Max(minValue, currentValue - 1);
        AssertUnsavedItemExists(form, "全局设定");

        InvokePrivateForUnsavedCloseSmoke(form, "LoadItemEquipmentTypeSettings");
        var equipmentTable = GetPrivateFieldForUnsavedCloseSmoke<DataTable>(form, "_currentItemEquipmentTypeData");
        var equipmentRow = equipmentTable.Rows.Cast<DataRow>()
            .FirstOrDefault(row => Convert.ToBoolean(row["AllowDisplay"], System.Globalization.CultureInfo.InvariantCulture))
            ?? throw new InvalidOperationException("Equipment type table has no row with a display toggle.");
        equipmentRow["Visible"] = !Convert.ToBoolean(equipmentRow["Visible"], System.Globalization.CultureInfo.InvariantCulture);
        AssertUnsavedItemExists(form, "装备类型设置");
    }

    private static void AssertUnsavedItemExists(MainForm form, string expectedText)
    {
        var result = InvokePrivateForUnsavedCloseSmoke(form, "CollectUnsavedItems") as System.Collections.IEnumerable
            ?? throw new InvalidOperationException("CollectUnsavedItems did not return an item collection.");
        var displayTexts = result.Cast<object>()
            .Select(item => Convert.ToString(item.GetType().GetProperty("DisplayText")?.GetValue(item)) ?? string.Empty)
            .ToList();
        if (!displayTexts.Any(text => text.Contains(expectedText, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Unsaved item list does not include {expectedText}. Items={string.Join(" | ", displayTexts)}");
        }
    }

    private static object? InvokePrivateForUnsavedCloseSmoke(MainForm form, string methodName, params object?[] arguments)
    {
        var method = typeof(MainForm).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing MainForm.{methodName}.");
        try
        {
            return method.Invoke(form, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    private static void MakeMapWorkbenchDraftDirtyForUnsavedCloseSmoke(MainForm form)
    {
        var draft = new MapWorkbenchDraft
        {
            DraftId = "unsaved-close-smoke",
            GridWidth = 1,
            GridHeight = 1,
            TerrainCells = [(byte)0],
            OriginalTerrainCells = [(byte)0]
        };
        SetPrivateFieldForUnsavedCloseSmoke(form, "_currentMapWorkbenchDraft", draft);

        var changeType = typeof(MainForm).GetNestedType("MapWorkbenchCellChange", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing MainForm.MapWorkbenchCellChange.");
        var change = Activator.CreateInstance(changeType, [0, null, null, null, null])
            ?? throw new InvalidOperationException("Could not create MapWorkbenchCellChange.");
        var listType = typeof(List<>).MakeGenericType(changeType);
        var changes = (System.Collections.IList)(Activator.CreateInstance(listType)
            ?? throw new InvalidOperationException("Could not create MapWorkbenchCellChange list."));
        changes.Add(change);

        var undoStack = GetPrivateFieldForUnsavedCloseSmoke<object>(form, "_mapMakerMapUndoStack");
        var push = undoStack.GetType().GetMethod("Push")
            ?? throw new InvalidOperationException("Missing Stack<MapWorkbenchCellChange>.Push.");
        push.Invoke(undoStack, [changes]);
    }

    private static T GetPrivateFieldForUnsavedCloseSmoke<T>(MainForm form, string fieldName)
    {
        var field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing MainForm.{fieldName}.");
        return (T)field.GetValue(form)!;
    }

    private static void SetPrivateFieldForUnsavedCloseSmoke<T>(MainForm form, string fieldName, T value)
    {
        var field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing MainForm.{fieldName}.");
        field.SetValue(form, value);
    }
}
