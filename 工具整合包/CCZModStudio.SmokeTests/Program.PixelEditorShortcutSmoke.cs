using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    private static void RunPixelEditorShortcutSmoke()
    {
        RunPixelColorReplacementSmoke();
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

    private static void RunPixelColorReplacementSmoke()
    {
        using var first = new Bitmap(3, 1);
        using var second = new Bitmap(3, 1);
        first.SetPixel(0, 0, Color.Red);
        first.SetPixel(1, 0, Color.Blue);
        first.SetPixel(2, 0, Color.Transparent);
        second.SetPixel(0, 0, Color.Blue);
        second.SetPixel(1, 0, Color.Red);
        second.SetPixel(2, 0, Color.FromArgb(128, 255, 0, 0));
        var documents = new[]
        {
            ("first", "first", first),
            ("second", "second", second)
        };
        var service = new PixelColorReplacementService();
        var result = service.Apply(documents,
        [
            new PixelColorReplacementRule(Color.Red, Color.Blue),
            new PixelColorReplacementRule(Color.Blue, Color.Green)
        ]);
        if (first.GetPixel(0, 0).ToArgb() != Color.Blue.ToArgb() ||
            first.GetPixel(1, 0).ToArgb() != Color.Green.ToArgb() ||
            second.GetPixel(0, 0).ToArgb() != Color.Green.ToArgb() ||
            second.GetPixel(1, 0).ToArgb() != Color.Blue.ToArgb())
        {
            throw new InvalidOperationException("Parallel color replacement cascaded instead of using the original pixels.");
        }
        if (result.Preview.RuleMatchCounts[0] != 2 || result.Preview.RuleMatchCounts[1] != 2 || result.ChangedDocumentKeys.Count != 2)
        {
            throw new InvalidOperationException("Parallel color replacement match counts are incorrect.");
        }
        if (second.GetPixel(2, 0).ToArgb() != Color.FromArgb(128, 255, 0, 0).ToArgb())
        {
            throw new InvalidOperationException("Exact ARGB replacement should not match a semi-transparent color.");
        }

        var duplicateRejected = false;
        try
        {
            service.Preview(documents,
            [
                new PixelColorReplacementRule(Color.Blue, Color.Red),
                new PixelColorReplacementRule(Color.Blue, Color.Green)
            ]);
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }
        if (!duplicateRejected) throw new InvalidOperationException("Duplicate replacement sources should be rejected.");
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

        RunSingleFrameShiftShortcutSmoke();

        RunPixelColorReplaceDialogLayoutAndPickerSmoke();
        RunPixelEditorColorReplacementPickerSmoke();
    }

    private static void RunSingleFrameShiftShortcutSmoke()
    {
        using var document = new EditableImageDocument
        {
            Target = new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5Standard,
                DisplayName = "Single-frame shift shortcut smoke"
            },
            Bitmap = BuildCoordinateBitmap(),
            OriginalBitmap = BuildCoordinateBitmap(),
            LoadDetail = "single-frame shift smoke"
        };
        using var dialog = new PixelImageEditorDialog(document, singleFrameMode: true);
        var before = document.Bitmap.GetPixel(1, 1).ToArgb();
        InvokeShortcut(dialog, Keys.Alt | Keys.Left);
        if (document.Bitmap.GetPixel(0, 1).ToArgb() != before || document.Bitmap.GetPixel(2, 1).A != 0)
            throw new InvalidOperationException("Alt+Left should shift content left and add a transparent right column.");
        InvokeShortcut(dialog, Keys.Control | Keys.Z);
        if (document.Bitmap.GetPixel(1, 1).ToArgb() != before)
            throw new InvalidOperationException("Single-frame shift should be independently undoable.");
    }

    private static Bitmap BuildCoordinateBitmap()
    {
        var bitmap = new Bitmap(3, 3);
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
            bitmap.SetPixel(x, y, Color.FromArgb(255, 20 + x * 50, 30 + y * 50, 40 + x + y));
        return bitmap;
    }

    private static void RunPixelColorReplaceDialogLayoutAndPickerSmoke()
    {
        using var bitmap = new Bitmap(4, 4);
        bitmap.SetPixel(0, 0, Color.Red);
        bitmap.SetPixel(1, 0, Color.Blue);
        bitmap.SetPixel(2, 0, Color.FromArgb(128, 10, 20, 30));
        bitmap.SetPixel(3, 0, Color.Transparent);
        using var document = new EditableImageDocument
        {
            Target = new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5Standard,
                DisplayName = "Color replacement dialog smoke"
            },
            Bitmap = bitmap,
            OriginalBitmap = new Bitmap(4, 4),
            Palette = Array.Empty<Color>(),
            LoadDetail = "color replacement smoke"
        };
        var page = new PixelEditResourcePage
        {
            Key = "color-dialog-smoke",
            Label = "Color dialog smoke",
            Document = document
        };

        using var dialog = new PixelColorReplaceDialog(new[] { page }, "long scope text smoke with many colors and 12pt font");
        dialog.Show();
        Application.DoEvents();

        if (dialog.RuleCountForSmoke != 5)
        {
            throw new InvalidOperationException($"Color replacement dialog should expose five rule rows, actual={dialog.RuleCountForSmoke}.");
        }
        if (dialog.RuleRowHeightsForSmoke.Any(height => height < 38))
        {
            throw new InvalidOperationException("Color replacement combo rows should be tall enough for 12pt/high-DPI text.");
        }
        if (dialog.ComboItemHeightsForSmoke.Any(height => height <= dialog.Font.Height))
        {
            throw new InvalidOperationException("Color replacement combo dropdown items should be taller than the font.");
        }
        if (dialog.HasHorizontalScrollForSmoke)
        {
            throw new InvalidOperationException("Color replacement dialog should not use a window-level horizontal scrollbar.");
        }
        if (string.IsNullOrWhiteSpace(dialog.PreviewTextForSmoke))
        {
            throw new InvalidOperationException("Color replacement dialog preview should have visible content.");
        }

        PixelColorPickRequestedEventArgs? request = null;
        dialog.PickRequested += (_, args) => request = args;
        dialog.RequestPickForSmoke(1, PixelColorReplaceDialog.PickFieldKind.Source);
        if (request == null || request.RuleIndex != 1 || request.FieldKind != PixelColorReplaceDialog.PickFieldKind.Source)
        {
            throw new InvalidOperationException("Color replacement dialog did not raise the expected source pick request.");
        }
        if (dialog.Visible)
        {
            throw new InvalidOperationException("Color replacement dialog should hide while the main canvas is picking a color.");
        }

        var picked = Color.FromArgb(255, 1, 2, 3);
        dialog.ApplyPickedColor(1, PixelColorReplaceDialog.PickFieldKind.Source, picked);
        if (!dialog.Visible ||
            dialog.GetRuleColorArgbForSmoke(1, PixelColorReplaceDialog.PickFieldKind.Source) != picked.ToArgb())
        {
            throw new InvalidOperationException("Picked source color was not dynamically added and selected.");
        }

        dialog.RequestPickForSmoke(1, PixelColorReplaceDialog.PickFieldKind.Target);
        dialog.ApplyPickedColor(1, PixelColorReplaceDialog.PickFieldKind.Target, Color.FromArgb(0, 200, 100, 50));
        if (dialog.GetRuleColorArgbForSmoke(1, PixelColorReplaceDialog.PickFieldKind.Target) != 0)
        {
            throw new InvalidOperationException("Picked fully transparent target color should normalize to transparent.");
        }
    }

    private static void RunPixelEditorColorReplacementPickerSmoke()
    {
        using var document = new EditableImageDocument
        {
            Target = new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5Standard,
                DisplayName = "Editor color picker smoke"
            },
            Bitmap = new Bitmap(3, 3),
            OriginalBitmap = new Bitmap(3, 3),
            Palette = Array.Empty<Color>(),
            LoadDetail = "editor color picker smoke"
        };
        document.Bitmap.SetPixel(1, 1, Color.FromArgb(255, 12, 34, 56));
        using var dialog = new PixelImageEditorDialog(document);
        dialog.Show();
        Application.DoEvents();

        dialog.OpenColorReplacementForSmoke();
        Application.DoEvents();
        var replacementDialog = dialog.ColorReplaceDialogForSmoke
            ?? throw new InvalidOperationException("Pixel editor should open a modeless color replacement dialog.");
        if (dialog.InteractionStateForSmoke != "ConfiguringColorReplacement")
        {
            throw new InvalidOperationException("Pixel editor should enter color replacement configuration state.");
        }

        replacementDialog.RequestPickForSmoke(0, PixelColorReplaceDialog.PickFieldKind.Source);
        Application.DoEvents();
        if (dialog.InteractionStateForSmoke != "PickingColorReplacement")
        {
            throw new InvalidOperationException("Pixel editor should enter replacement picking state after a pick request.");
        }
        var before = document.Bitmap.GetPixel(1, 1).ToArgb();
        dialog.PickColorReplacementPixelForSmoke(new Point(1, 1), MouseButtons.Left);
        if (document.Bitmap.GetPixel(1, 1).ToArgb() != before)
        {
            throw new InvalidOperationException("Picking a replacement color should not modify the bitmap.");
        }
        if (dialog.InteractionStateForSmoke != "ConfiguringColorReplacement" ||
            replacementDialog.GetRuleColorArgbForSmoke(0, PixelColorReplaceDialog.PickFieldKind.Source) != before)
        {
            throw new InvalidOperationException("Picked canvas color was not returned to the original replacement rule.");
        }

        replacementDialog.RequestPickForSmoke(0, PixelColorReplaceDialog.PickFieldKind.Target);
        dialog.PickColorReplacementPixelForSmoke(new Point(1, 1), MouseButtons.Right);
        if (dialog.InteractionStateForSmoke != "ConfiguringColorReplacement" || !replacementDialog.Visible)
        {
            throw new InvalidOperationException("Right-click should cancel replacement picking and show the dialog again.");
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
