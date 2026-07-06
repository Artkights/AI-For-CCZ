using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

internal partial class Program
{
    static void RunPortraitFrameApplySmoke(CczProject project)
    {
        var faceSource = CharacterImageResourceService.ResolveGameFile(project, "Face.e5");
        if (!File.Exists(faceSource))
        {
            throw new FileNotFoundException("Portrait frame smoke requires Face.e5.", faceSource);
        }

        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "PortraitFrameApplySmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);
        File.Copy(faceSource, Path.Combine(smokeRoot, "Face.e5"), overwrite: false);
        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=portrait frame apply smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var framePath = Path.Combine(smokeRoot, "frame.png");
        CreateSmokePortraitFrame(framePath);

        var service = new PortraitFrameApplyService();
        var e5 = new E5ImageReplaceService();
        var facePath = CharacterImageResourceService.ResolveFaceFile(testProject) ?? Path.Combine(testProject.GameRoot, "Face.e5");

        var originalFace9 = DecodeStandardImage(e5.ReadEntryBytes(facePath, 9));
        var originalCenter = originalFace9.GetPixel(60, 60);
        originalFace9.Dispose();

        var preview = service.Preview(testProject, new PortraitFrameApplyRequest
        {
            FramePath = framePath,
            TargetRows = new[] { new PortraitFrameTargetRow(1, "SmokeFace", 1) },
            WriteMode = "test_copy"
        });
        if (!preview.CanWrite ||
            preview.Items.Count != 1 ||
            preview.TotalOperationCount != 1 ||
            preview.Items[0].OutputWidth != 120 ||
            preview.Items[0].OutputHeight != 120 ||
            !preview.Items[0].TargetImageNumbers.SequenceEqual(new[] { 9 }))
        {
            throw new InvalidOperationException("Portrait frame preview did not match face #1 -> Face.e5 #9.");
        }

        var result = service.Replace(testProject, preview.Request);
        if (result.E5Result == null ||
            result.TotalOperationCount != 1 ||
            !File.Exists(result.E5Result.BackupPath) ||
            !File.Exists(result.AggregateReportPath))
        {
            throw new InvalidOperationException("Portrait frame write did not create expected backup/report.");
        }

        using (var framed = DecodeStandardImage(e5.ReadEntryBytes(facePath, 9)))
        {
            if (framed.Width != 120 || framed.Height != 120)
            {
                throw new InvalidOperationException("Framed face should remain 120x120.");
            }

            AssertApproxColor(framed.GetPixel(2, 2), Color.FromArgb(255, 240, 20, 40), "frame edge");
            AssertApproxColor(framed.GetPixel(60, 60), originalCenter, "transparent center");
        }

        var zeroPreview = service.Preview(testProject, new PortraitFrameApplyRequest
        {
            FramePath = framePath,
            TargetRows = new[] { new PortraitFrameTargetRow(0, "DefaultFace", 0) },
            WriteMode = "test_copy"
        });
        if (!zeroPreview.CanWrite ||
            zeroPreview.TotalOperationCount != 8 ||
            !zeroPreview.Items.Single().TargetImageNumbers.SequenceEqual(Enumerable.Range(1, 8)))
        {
            throw new InvalidOperationException("Portrait frame preview should map face #0 to Face.e5 #1-#8.");
        }

        var duplicatePreview = service.Preview(testProject, new PortraitFrameApplyRequest
        {
            FramePath = framePath,
            TargetRows = new[]
            {
                new PortraitFrameTargetRow(10, "A", 2),
                new PortraitFrameTargetRow(11, "B", 2)
            },
            WriteMode = "test_copy"
        });
        if (!duplicatePreview.CanWrite ||
            duplicatePreview.TotalOperationCount != 1 ||
            duplicatePreview.Warnings.All(warning => !warning.Contains("共用", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Portrait frame preview should de-duplicate shared face ids.");
        }

        var invalidFrame = Path.Combine(smokeRoot, "not-image.txt");
        File.WriteAllText(invalidFrame, "not an image");
        var invalidPreview = service.Preview(testProject, new PortraitFrameApplyRequest
        {
            FramePath = invalidFrame,
            TargetRows = new[] { new PortraitFrameTargetRow(3, "BadFrame", 3) },
            WriteMode = "test_copy"
        });
        if (invalidPreview.CanWrite ||
            invalidPreview.SkippedItems.All(item => !item.Reason.StartsWith(BatchImageImportSkipReasons.InvalidFormat, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Portrait frame preview should block invalid frame images.");
        }

        Console.WriteLine($"PORTRAIT_FRAME_APPLY_SMOKE OK root={smokeRoot}");
    }

    private static void CreateSmokePortraitFrame(string path)
    {
        using var bitmap = new Bitmap(120, 120, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(255, 240, 20, 40));
            graphics.FillRectangle(brush, 0, 0, 120, 8);
            graphics.FillRectangle(brush, 0, 112, 120, 8);
            graphics.FillRectangle(brush, 0, 0, 8, 120);
            graphics.FillRectangle(brush, 112, 0, 8, 120);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static Bitmap DecodeStandardImage(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: true);
        return new Bitmap(image);
    }

    private static void AssertApproxColor(Color actual, Color expected, string label)
    {
        static int Delta(byte a, byte b) => Math.Abs(a - b);
        if (Delta(actual.A, expected.A) > 8 ||
            Delta(actual.R, expected.R) > 8 ||
            Delta(actual.G, expected.G) > 8 ||
            Delta(actual.B, expected.B) > 8)
        {
            throw new InvalidOperationException($"{label} color mismatch. expected={expected} actual={actual}");
        }
    }
}
