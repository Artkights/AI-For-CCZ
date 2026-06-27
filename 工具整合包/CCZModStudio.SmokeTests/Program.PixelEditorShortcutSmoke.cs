using CCZModStudio;
using CCZModStudio.Models;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    private static void RunPixelEditorShortcutSmoke()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunPixelEditorShortcutSmokeOnSta();
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
            throw new InvalidOperationException("Pixel editor shortcut smoke failed.", failure);
        }
    }

    private static void RunPixelEditorShortcutSmokeOnSta()
    {
        using var document = new EditableImageDocument
        {
            Target = new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5Standard,
                DisplayName = "Shortcut smoke"
            },
            Bitmap = new Bitmap(4, 4),
            OriginalBitmap = new Bitmap(4, 4),
            Palette = new[] { Color.Transparent, Color.Black, Color.White },
            LoadDetail = "shortcut smoke"
        };
        using var dialog = new PixelImageEditorDialog(document);

        InvokeShortcut(dialog, Keys.G);
        if (ReadCheckBox(dialog, "_gridCheckBox").Checked)
        {
            throw new InvalidOperationException("G should toggle the pixel editor grid off.");
        }

        InvokeShortcut(dialog, Keys.Control | Keys.H);
        if (!ReadCheckBox(dialog, "_gridCheckBox").Checked)
        {
            throw new InvalidOperationException("Ctrl+H should toggle the pixel editor grid on.");
        }

        InvokeShortcut(dialog, Keys.E);
        AssertTool(dialog, "Eraser");
        InvokeShortcut(dialog, Keys.F);
        AssertTool(dialog, "Fill");
        InvokeShortcut(dialog, Keys.L);
        AssertTool(dialog, "Line");
        InvokeShortcut(dialog, Keys.R);
        AssertTool(dialog, "Rectangle");
        InvokeShortcut(dialog, Keys.P);
        AssertTool(dialog, "Pencil");

        var zoom = ReadTrackBar(dialog, "_zoomBar");
        var initialZoom = zoom.Value;
        InvokeShortcut(dialog, Keys.Oemplus);
        if (zoom.Value != Math.Min(zoom.Maximum, initialZoom + 1))
        {
            throw new InvalidOperationException("+ should increase pixel editor zoom.");
        }

        InvokeShortcut(dialog, Keys.OemMinus);
        if (zoom.Value != initialZoom)
        {
            throw new InvalidOperationException("- should decrease pixel editor zoom.");
        }

        InvokeShortcut(dialog, Keys.D0);
        if (zoom.Value != 12)
        {
            throw new InvalidOperationException("0 should reset pixel editor zoom to 12.");
        }

        InvokePrivate(dialog, "SaveUndo");
        document.Bitmap.SetPixel(0, 0, Color.Red);
        InvokeShortcut(dialog, Keys.Control | Keys.Z);
        if (document.Bitmap.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb())
        {
            throw new InvalidOperationException("Ctrl+Z should undo the bitmap edit.");
        }

        InvokeShortcut(dialog, Keys.Control | Keys.Y);
        if (document.Bitmap.GetPixel(0, 0).ToArgb() != Color.Red.ToArgb())
        {
            throw new InvalidOperationException("Ctrl+Y should redo the bitmap edit.");
        }

        InvokeShortcut(dialog, Keys.Control | Keys.S);
        if (dialog.DialogResult != DialogResult.OK)
        {
            throw new InvalidOperationException("Ctrl+S should close the pixel editor with OK.");
        }
    }

    private static void InvokeShortcut(PixelImageEditorDialog dialog, Keys keys)
    {
        var method = typeof(PixelImageEditorDialog).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(PixelImageEditorDialog), "ProcessCmdKey");
        var message = new Message();
        var args = new object[] { message, keys };
        var handled = method.Invoke(dialog, args) as bool?;
        if (handled != true)
        {
            throw new InvalidOperationException($"Shortcut was not handled: {keys}");
        }
    }

    private static void InvokePrivate(PixelImageEditorDialog dialog, string methodName)
    {
        var method = typeof(PixelImageEditorDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(PixelImageEditorDialog), methodName);
        method.Invoke(dialog, null);
    }

    private static CheckBox ReadCheckBox(PixelImageEditorDialog dialog, string fieldName)
        => (CheckBox)(typeof(PixelImageEditorDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(dialog) ?? throw new MissingFieldException(nameof(PixelImageEditorDialog), fieldName));

    private static TrackBar ReadTrackBar(PixelImageEditorDialog dialog, string fieldName)
        => (TrackBar)(typeof(PixelImageEditorDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(dialog) ?? throw new MissingFieldException(nameof(PixelImageEditorDialog), fieldName));

    private static void AssertTool(PixelImageEditorDialog dialog, string expected)
    {
        var value = typeof(PixelImageEditorDialog).GetField("_tool", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(dialog)?.ToString();
        if (!string.Equals(value, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected pixel editor tool {expected}, actual {value}.");
        }
    }
}
