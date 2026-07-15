using CCZModStudio;
using CCZModStudio.Models;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    private static void RunImageAssignmentImportPreviewDialogSmoke()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);

                using var temp = new TemporarySmokeDirectory("ImageAssignmentImportPreviewDialog");
                var rOutput = CreatePreviewSmokePngBytes(48, 1280, Color.MediumPurple, Color.Black);
                var sOutput = CreatePreviewSmokePngBytes(48, 528, Color.DeepSkyBlue, Color.Black);
                var faceOutput = CreatePreviewSmokePngBytes(120, 120, Color.Orange, Color.DarkRed);
                var model = new ImageAssignmentImportPreviewDialogModel
                {
                    Title = "Image Assignment Import Preview Dialog Smoke",
                    SummaryText = "Smoke preview summary",
                    CanWrite = true,
                    Items = new[]
                    {
                        new ImageAssignmentImportPreviewItem
                        {
                            Kind = "R",
                            DisplayName = "R smoke",
                            ResourceId = 1,
                            ActionName = "front",
                            TargetFileName = "missing-r.e5",
                            TargetImageNumber = 1,
                            SourcePath = Path.Combine(temp.Path, "r.bmp"),
                            SourceWidth = 48,
                            SourceHeight = 1280,
                            OutputWidth = 48,
                            OutputHeight = 1280,
                            OutputBytes = rOutput,
                            FrameWidth = 48,
                            FrameHeight = 64,
                            Detail = "R smoke"
                        },
                        new ImageAssignmentImportPreviewItem
                        {
                            Kind = "S",
                            DisplayName = "S smoke",
                            ResourceId = 2,
                            StageName = "stage 1",
                            ActionName = "mov",
                            TargetFileName = "missing-s.e5",
                            TargetImageNumber = 2,
                            SourcePath = Path.Combine(temp.Path, "s.bmp"),
                            SourceWidth = 48,
                            SourceHeight = 528,
                            OutputWidth = 48,
                            OutputHeight = 528,
                            OutputBytes = sOutput,
                            FrameWidth = 48,
                            FrameHeight = 48,
                            Detail = "S smoke"
                        },
                        new ImageAssignmentImportPreviewItem
                        {
                            Kind = "Face",
                            DisplayName = "Face smoke",
                            ResourceId = 3,
                            ActionName = "face",
                            TargetFileName = "missing-face.e5",
                            TargetImageNumber = 3,
                            SourcePath = Path.Combine(temp.Path, "face.png"),
                            SourceWidth = 120,
                            SourceHeight = 120,
                            OutputWidth = 120,
                            OutputHeight = 120,
                            OutputBytes = faceOutput,
                            FrameWidth = 120,
                            FrameHeight = 120,
                            Detail = "Face smoke"
                        }
                    },
                    SkippedItems = Array.Empty<BatchImageImportSkippedItem>(),
                    Warnings = Array.Empty<string>()
                };

                using var dialog = new ImageAssignmentImportPreviewDialog(null!, model);
                dialog.Show();
                dialog.PerformLayout();
                DrainImageAssignmentImportPreviewDialogEvents();

                if (dialog.ClientSize.Width <= 0 || dialog.ClientSize.Height <= 0)
                {
                    throw new InvalidOperationException($"Preview dialog has invalid client size: {dialog.ClientSize}.");
                }

                if (Math.Abs(dialog.Font.SizeInPoints - ImportExportDialogLayout.FontSize) > 0.01F ||
                    dialog.AutoScaleMode != AutoScaleMode.Dpi ||
                    !dialog.AutoScroll)
                {
                    throw new InvalidOperationException(
                        $"Preview dialog import/export layout mismatch: font={dialog.Font.SizeInPoints} autoscale={dialog.AutoScaleMode} autoscroll={dialog.AutoScroll}.");
                }

                var currentBox = GetPreviewDialogPictureBox(dialog, "_currentBox");
                var outputBox = GetPreviewDialogPictureBox(dialog, "_outputBox");
                AssertPreviewBoxImageFillsClient(currentBox, "_currentBox");
                AssertPreviewBoxImageFillsClient(outputBox, "_outputBox");

                dialog.ClientSize = new Size(980, 660);
                dialog.PerformLayout();
                dialog.ClientSize = new Size(1260, 780);
                dialog.PerformLayout();
                DrainImageAssignmentImportPreviewDialogEvents();
                AssertPreviewBoxImageFillsClient(currentBox, "_currentBox resized");
                AssertPreviewBoxImageFillsClient(outputBox, "_outputBox resized");

                dialog.Close();
                Console.WriteLine("IMAGE_ASSIGNMENT_IMPORT_PREVIEW_DIALOG_SMOKE_OK");
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
            throw new InvalidOperationException("Image assignment import preview dialog smoke failed.", failure);
        }
    }

    private static byte[] CreatePreviewSmokePngBytes(int width, int height, Color primary, Color secondary)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var primaryBrush = new SolidBrush(primary);
        using var secondaryBrush = new SolidBrush(secondary);
        graphics.Clear(Color.Transparent);
        graphics.FillRectangle(primaryBrush, 0, 0, width, Math.Max(1, height / 2));
        graphics.FillRectangle(secondaryBrush, 0, Math.Max(1, height / 2), width, Math.Max(1, height - height / 2));
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        return memory.ToArray();
    }

    private static PictureBox GetPreviewDialogPictureBox(ImageAssignmentImportPreviewDialog dialog, string fieldName)
    {
        var field = typeof(ImageAssignmentImportPreviewDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing ImageAssignmentImportPreviewDialog.{fieldName}.");
        return (PictureBox)field.GetValue(dialog)!;
    }

    private static void AssertPreviewBoxImageFillsClient(PictureBox box, string name)
    {
        var image = box.Image ?? throw new InvalidOperationException($"{name} has no image.");
        if (image.Width != box.ClientSize.Width || image.Height != box.ClientSize.Height)
        {
            throw new InvalidOperationException($"{name} image does not fill client canvas: image={image.Size}, client={box.ClientSize}.");
        }
    }

    private static void DrainImageAssignmentImportPreviewDialogEvents()
    {
        for (var i = 0; i < 8; i++)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }
}
