using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Globalization;

internal partial class Program
{
    static void RunPixelEditorCodecSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "PixelEditorCodecSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);

        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5", "Pmapobj.e5", "Itemicon.dll" })
        {
            var source = fileName.EndsWith(".e5", StringComparison.OrdinalIgnoreCase)
                ? CharacterImageResourceService.ResolveGameFile(project, fileName)
                : Path.Combine(project.GameRoot, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, fileName), overwrite: false);
            }
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=pixel editor codec smoke\r\n");
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var codec = new EditableImageCodecService();
        var e5 = new E5ImageReplaceService();
        AssertDllDibDecodeRowDirection();
        RunPixelEditorShortcutSmoke();

        var rawPath = CharacterImageResourceService.ResolveGameFile(testProject, "Pmapobj.e5");
        if (!File.Exists(rawPath))
        {
            throw new FileNotFoundException("Pixel editor codec smoke requires Pmapobj.e5.", rawPath);
        }

        var rawTarget = new EditableImageTarget
        {
            Kind = EditableImageTargetKind.E5RawStrip,
            DisplayName = "Smoke R RAW",
            TargetPath = rawPath,
            ImageNumber = 3,
            FrameWidth = 48,
            FrameHeight = 64,
            OperationKind = "PixelEditorCodecSmoke RAW"
        };
        using (var document = codec.Load(testProject, rawTarget))
        {
            if (document.Bitmap.Width != 48 || document.Bitmap.Height % 64 != 0)
            {
                throw new InvalidOperationException($"RAW load dimensions are unexpected: {document.Bitmap.Width}x{document.Bitmap.Height}");
            }

            document.Bitmap.SetPixel(0, 0, Color.Transparent);
            var preview = codec.PreviewWrite(testProject, rawTarget, document.Bitmap);
            if (preview.E5Preview == null || preview.E5Preview.OperationCount != 1)
            {
                throw new InvalidOperationException("RAW pixel editor preview did not use E5 batch writeback.");
            }

            var result = codec.Write(testProject, rawTarget, document.Bitmap);
            if (result.E5Result == null || !File.Exists(result.BackupPath) || !File.Exists(result.ReportPath))
            {
                throw new InvalidOperationException("RAW pixel editor writeback result is missing backup or report.");
            }
        }

        var rawBytes = e5.ReadEntryBytes(rawPath, rawTarget.ImageNumber);
        if (rawBytes.Length < 1 || rawBytes[0] != 0)
        {
            throw new InvalidOperationException("RAW pixel editor reread pixel did not match the expected value.");
        }

        var iconPath = Path.Combine(testProject.GameRoot, "Itemicon.dll");
        if (File.Exists(iconPath))
        {
            var iconTarget = new EditableImageTarget
            {
                Kind = EditableImageTargetKind.DllBitmapIcon,
                DisplayName = "Smoke Item Icon",
                TargetPath = iconPath,
                IconIndex = 0,
                OperationKind = "PixelEditorCodecSmoke DLL"
            };
            using var iconDocument = codec.Load(testProject, iconTarget);
            iconDocument.Bitmap.SetPixel(0, 0, Color.Transparent);
            iconDocument.Bitmap.SetPixel(10, 10, Color.FromArgb(255, 12, 34, 56));
            var iconPreview = codec.PreviewWrite(testProject, iconTarget, iconDocument.Bitmap);
            if (iconPreview.DllPreview == null || iconPreview.DllPreview.Items.Count != 1)
            {
                throw new InvalidOperationException("DLL icon pixel editor preview did not use DLL batch writeback.");
            }

            var iconResult = codec.Write(testProject, iconTarget, iconDocument.Bitmap);
            if (iconResult.DllResult == null || !File.Exists(iconResult.BackupPath) || !File.Exists(iconResult.ReportPath))
            {
                throw new InvalidOperationException("DLL icon pixel editor writeback result is missing backup or report.");
            }

            AssertDllIconPixelEditorWriteback(iconPath, iconTarget.IconIndex);
        }

        Console.WriteLine($"PIXEL_EDITOR_CODEC_SMOKE OK root={smokeRoot}");
    }

    static void AssertDllDibDecodeRowDirection()
    {
        var top = Color.FromArgb(255, 230, 70, 80);
        var bottom = Color.FromArgb(255, 40, 190, 80);

        using var standard8Bpp = EditableImageCodecService.DecodeDibForSmoke(BuildSmoke8BppStandardBottomUpDib(top, bottom))
            ?? throw new InvalidOperationException("8bpp 标准 bottom-up DIB 编辑器解码未生成。");
        AssertBitmapPreviewTopBottom(standard8Bpp, top, bottom, "8bpp 标准 bottom-up DIB 编辑器解码");

        using var ccz32Bpp = EditableImageCodecService.DecodeDibForSmoke(BuildSmoke32BppCczTopFirstDib(top, bottom))
            ?? throw new InvalidOperationException("32bpp CCZ top-first DIB 编辑器解码未生成。");
        AssertBitmapPreviewTopBottom(ccz32Bpp, top, bottom, "32bpp CCZ top-first DIB 编辑器解码");
    }

    static void AssertDllIconPixelEditorWriteback(string dllPath, int iconIndex)
    {
        var codec = new DllBitmapIconCodecService();
        var resources = codec.ParseBitmapResources(dllPath);
        var pair = codec.ResolveBitmapResourcePair(resources, iconIndex);
        if (pair.AllVariants.Count == 0)
        {
            throw new InvalidOperationException("DLL icon pixel editor writeback did not find reread variants.");
        }

        foreach (var resource in pair.AllVariants)
        {
            using var decoded = DllBitmapIconCodecService.DecodeDib(resource.DibBytes)
                                ?? throw new InvalidOperationException($"DLL icon pixel editor reread failed for ID={resource.Id}.");
            var corner = decoded.Bitmap.GetPixel(0, 0);
            if (corner.A != 0)
            {
                throw new InvalidOperationException($"DLL icon pixel editor transparent corner was not preserved for ID={resource.Id}: {corner}.");
            }

            for (var y = 0; y < decoded.Bitmap.Height; y++)
            {
                for (var x = 0; x < decoded.Bitmap.Width; x++)
                {
                    var pixel = decoded.Bitmap.GetPixel(x, y);
                    if (pixel.A != 0 && DllBitmapIconCodecService.IsMagentaKey(pixel))
                    {
                        throw new InvalidOperationException($"DLL icon pixel editor left visible magenta in ID={resource.Id} at {x},{y}: {pixel}.");
                    }
                }
            }
        }
    }
}
