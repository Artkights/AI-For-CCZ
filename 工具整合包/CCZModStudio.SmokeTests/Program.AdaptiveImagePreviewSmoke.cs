using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    private static void RunAdaptiveImagePreviewSmoke()
    {
        AssertContainRectangle(new Size(400, 100), new Rectangle(0, 0, 100, 300));
        AssertContainRectangle(new Size(100, 400), new Rectangle(0, 0, 300, 100));
        AssertContainRectangle(new Size(100, 100), new Rectangle(7, 11, 401, 205));
        AssertContainRectangle(new Size(1, 1000), new Rectangle(0, 0, 800, 120));
        AssertContainRectangle(new Size(1000, 1), new Rectangle(0, 0, 120, 800));
        AssertContainRectangle(new Size(640, 480), new Rectangle(0, 0, 1, 1));
        if (AspectRatioDisplay.CalculateContainRectangle(Size.Empty, new Rectangle(0, 0, 100, 100)) != Rectangle.Empty ||
            AspectRatioDisplay.CalculateContainRectangle(new Size(100, 100), Rectangle.Empty) != Rectangle.Empty)
        {
            throw new InvalidOperationException("Contain rectangle must be empty for invalid source or bounds.");
        }

        RunOnStaThread(() =>
        {
            AssertAnimationDialogResize();
            AssertBeautifyDialogResize();
        });

        Console.WriteLine("ADAPTIVE_IMAGE_PREVIEW_SMOKE_OK");
    }

    private static void AssertContainRectangle(Size source, Rectangle bounds)
    {
        var target = AspectRatioDisplay.CalculateContainRectangle(source, bounds);
        if (target.Width <= 0 || target.Height <= 0 || !bounds.Contains(target))
        {
            throw new InvalidOperationException($"Invalid contain rectangle: source={source}, bounds={bounds}, target={target}.");
        }

        var scale = Math.Min(bounds.Width / (double)source.Width, bounds.Height / (double)source.Height);
        var expectedWidth = source.Width * scale;
        var expectedHeight = source.Height * scale;
        if (Math.Abs(target.Width - expectedWidth) > 1 || Math.Abs(target.Height - expectedHeight) > 1)
        {
            throw new InvalidOperationException($"Contain rectangle changed aspect ratio: source={source}, target={target}.");
        }

        var centeredX = Math.Abs((target.Left - bounds.Left) - (bounds.Right - target.Right)) <= 1;
        var centeredY = Math.Abs((target.Top - bounds.Top) - (bounds.Bottom - target.Bottom)) <= 1;
        var fillsAxis = Math.Abs(target.Width - bounds.Width) <= 1 || Math.Abs(target.Height - bounds.Height) <= 1;
        if (!centeredX || !centeredY || !fillsAxis)
        {
            throw new InvalidOperationException($"Contain rectangle is not centered and maximized: bounds={bounds}, target={target}.");
        }
    }

    private static void AssertAnimationDialogResize()
    {
        var frames = new Bitmap?[]
        {
            BuildSolidBitmap(32, 48, Color.Red),
            BuildSolidBitmap(32, 48, Color.Blue)
        };
        var preview = new CharacterImageAnimationPreview(
            ImageAssignmentResourceKind.R,
            1,
            1,
            "R smoke",
            1,
            1,
            64,
            80,
            new[] { new CharacterImageAnimationCell(0, 0, "front", frames) },
            Array.Empty<string>());

        using var dialog = new CharacterImageAnimationPreviewDialog(
            new ImageAssignmentPreviewService(),
            preview,
            CharacterImageAnimationPreviewDialog.MinIntervalMs);
        dialog.Show();
        DrainAdaptivePreviewEvents(5, 20);

        var box = GetAdaptivePreviewField<PictureBox>(dialog, "_pictureBox");
        var timer = GetAdaptivePreviewField<System.Windows.Forms.Timer>(dialog, "_timer");
        var optionCombo = GetAdaptivePreviewField<ComboBox>(dialog, "_optionCombo");
        var originalFrameSize = box.Image?.Size ?? throw new InvalidOperationException("Animation preview did not render a frame.");
        if (optionCombo.Parent != null)
        {
            throw new InvalidOperationException("R animation preview unexpectedly displayed an option selector.");
        }

        dialog.ClientSize = new Size(760, 320);
        dialog.PerformLayout();
        box.Invalidate();
        DrainAdaptivePreviewEvents(5, 20);
        var wideSize = box.ClientSize;
        dialog.ClientSize = new Size(340, 720);
        dialog.PerformLayout();
        box.Invalidate();
        var frameBeforeWait = GetAdaptivePreviewField<int>(dialog, "_frameIndex");
        var frameChanged = false;
        for (var index = 0; index < 12 && !frameChanged; index++)
        {
            DrainAdaptivePreviewEvents(1, 15);
            frameChanged = GetAdaptivePreviewField<int>(dialog, "_frameIndex") != frameBeforeWait;
        }

        if (box.Image?.Size != originalFrameSize || wideSize == box.ClientSize)
        {
            throw new InvalidOperationException($"Animation source frame was resized with the window: source={box.Image?.Size}, original={originalFrameSize}.");
        }
        if (!timer.Enabled || !frameChanged)
        {
            throw new InvalidOperationException("Animation did not continue after the preview window was resized.");
        }

        AssertContainRectangle(originalFrameSize, box.ClientRectangle);
        dialog.Close();
    }

    private static void AssertBeautifyDialogResize()
    {
        using var source = BuildSolidBitmap(320, 120, Color.SeaGreen);
        using var dialog = new CustomBeautifyFilterDialog(
            BeautifyCustomFilterSettings.CreateDefault(),
            null,
            source,
            1);
        dialog.Show();
        DrainAdaptivePreviewEvents(12, 20);

        var box = GetAdaptivePreviewField<PictureBox>(dialog, "_previewBox");
        var image = box.Image ?? throw new InvalidOperationException("Beautify preview did not render.");
        var sourceSize = image.Size;
        dialog.ClientSize = new Size(1180, 560);
        dialog.PerformLayout();
        box.Invalidate();
        DrainAdaptivePreviewEvents(3, 20);
        dialog.ClientSize = new Size(780, 820);
        dialog.PerformLayout();
        box.Invalidate();
        DrainAdaptivePreviewEvents(3, 20);

        if (!ReferenceEquals(image, box.Image) || box.Image?.Size != sourceSize)
        {
            throw new InvalidOperationException("Beautify preview was rebuilt or resized only because the window size changed.");
        }

        AssertContainRectangle(sourceSize, box.ClientRectangle);
        dialog.Close();
    }

    private static Bitmap BuildSolidBitmap(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private static T GetAdaptivePreviewField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing field {target.GetType().Name}.{fieldName}.");
        return (T)field.GetValue(target)!;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                action();
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
            throw new InvalidOperationException("Adaptive image preview UI smoke failed.", failure);
        }
    }

    private static void DrainAdaptivePreviewEvents(int iterations, int delayMs)
    {
        for (var index = 0; index < iterations; index++)
        {
            Application.DoEvents();
            Thread.Sleep(delayMs);
        }
    }
}
