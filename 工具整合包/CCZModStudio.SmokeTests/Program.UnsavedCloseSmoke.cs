using CCZModStudio;
using CCZModStudio.Models;
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
