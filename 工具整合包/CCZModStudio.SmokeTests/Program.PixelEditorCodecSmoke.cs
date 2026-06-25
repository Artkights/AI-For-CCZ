using CCZModStudio.Core;
using CCZModStudio.Models;
using CCZModStudio;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

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
            AssertItemIconSlotMappingAndPreview(testProject, iconPath, 0);
            var iconTarget = new EditableImageTarget
            {
                Kind = EditableImageTargetKind.DllBitmapIcon,
                DisplayName = "Smoke Item Icon",
                TargetPath = iconPath,
                IconIndex = 0,
                OperationKind = "PixelEditorCodecSmoke DLL"
            };
            using var iconDocument = codec.Load(testProject, iconTarget);
            PaintAsymmetricIconSmokePattern(iconDocument.Bitmap);
            using var expectedLarge = new Bitmap(iconDocument.Bitmap);
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

            if (!iconResult.DllResult.ReadbackVerified || iconResult.DllResult.ReadbackItems.Count == 0)
            {
                throw new InvalidOperationException("DLL icon pixel editor writeback did not pass readback verification: " + string.Join(" | ", iconResult.DllResult.ReadbackWarnings));
            }

            var dllCodec = new DllIconBitmapCodec();
            var resources = dllCodec.ReadBitmapResources(iconPath);
            var pair = dllCodec.ResolveIconResources(resources, iconTarget.IconIndex);
            var largeResource = dllCodec.SelectLargeVariant(pair)
                                ?? throw new InvalidOperationException("DLL icon smoke did not find a large RT_BITMAP resource.");
            var smallResource = dllCodec.SelectSmallVariant(pair)
                                ?? throw new InvalidOperationException("DLL icon smoke did not find a small RT_BITMAP resource.");
            using var actualLarge = dllCodec.DecodeDib(largeResource.DibBytes)
                                    ?? throw new InvalidOperationException("DLL icon smoke could not decode reread large RT_BITMAP.");
            using var actualSmall = dllCodec.DecodeDib(smallResource.DibBytes)
                                    ?? throw new InvalidOperationException("DLL icon smoke could not decode reread small RT_BITMAP.");
            using var expectedSmall = DllIconBitmapCodec.ScaleToFit(expectedLarge, smallResource.Width, smallResource.Height);
            if (!DllIconBitmapCodec.ArePixelEqual(actualLarge, expectedLarge))
            {
                throw new InvalidOperationException("DLL icon pixel editor reread large bitmap did not match the edited canvas.");
            }

            if (!DllIconBitmapCodec.ArePixelEqual(actualSmall, expectedSmall))
            {
                throw new InvalidOperationException("DLL icon pixel editor reread small bitmap did not match the shared scaler output.");
            }

            var itemPreview = new ItemIconPreviewService().BuildPreview(testProject, iconTarget.IconIndex, "Itemicon.dll", "物品图标", 96);
            if (!itemPreview.RenderMode.Equals("DLL RT_BITMAP", StringComparison.Ordinal) ||
                itemPreview.NativeBitmap == null ||
                itemPreview.LargeBitmap == null ||
                itemPreview.SmallBitmap == null ||
                itemPreview.LargeVariant == null ||
                itemPreview.SmallVariant == null ||
                itemPreview.ResourceVariants == null ||
                itemPreview.ResourceVariants.Count == 0)
            {
                throw new InvalidOperationException("Item icon preview did not use structured DLL RT_BITMAP reread data after pixel editor write.");
            }

            using (itemPreview.Bitmap)
            using (itemPreview.NativeBitmap)
            using (itemPreview.LargeBitmap)
            using (itemPreview.SmallBitmap)
            {
                if (!DllIconBitmapCodec.ArePixelEqual(itemPreview.LargeBitmap, expectedLarge))
                {
                    throw new InvalidOperationException("Item icon preview large bitmap does not match the edited RT_BITMAP slot.");
                }
            }

            AssertDllIconMagentaKeyWriteback(testProject, iconPath, smokeRoot);
        }

        Console.WriteLine($"PIXEL_EDITOR_CODEC_SMOKE OK root={smokeRoot}");
    }

    private static void AssertDllIconMagentaKeyWriteback(CczProject project, string iconPath, string smokeRoot)
    {
        var sourcePath = Path.Combine(smokeRoot, "magenta_key_item_icon.png");
        using (var source = CreateMagentaKeyIconSource(32, 32))
        {
            source.Save(sourcePath, System.Drawing.Imaging.ImageFormat.Png);
        }

        var service = new IconResourceReplaceService();
        var result = service.ReplaceBitmapIcon(project, iconPath, 2, sourcePath);
        if (!result.ReadbackVerified || result.ReadbackItems.Count == 0)
        {
            throw new InvalidOperationException("DLL icon magenta-key writeback did not pass readback verification: " + string.Join(" | ", result.ReadbackWarnings));
        }

        if (result.ReadbackItems.Any(item => item.SourceBitCount == 8 && (item.WrittenBitCount != 32 || item.ActualBitCount != 32)))
        {
            throw new InvalidOperationException("DLL icon magenta-key writeback did not convert legacy 8bpp RT_BITMAP variants to 32bpp.");
        }

        if (result.ReadbackItems.All(item => item.MagentaKeyPixelCount == 0))
        {
            throw new InvalidOperationException("DLL icon magenta-key writeback did not report converted magenta-key pixels.");
        }

        var dllCodec = new DllIconBitmapCodec();
        var resources = dllCodec.ReadBitmapResources(iconPath);
        var slot = dllCodec.ResolveGameIconSlot(iconPath, resources, 2, "Itemicon.dll");
        var large = slot.LargeSelectedVariant ?? dllCodec.SelectLargeVariant(slot.Variants)
                    ?? throw new InvalidOperationException("DLL icon magenta-key smoke did not find a large resource.");
        var small = slot.SmallSelectedVariant ?? dllCodec.SelectSmallVariant(slot.Variants)
                    ?? throw new InvalidOperationException("DLL icon magenta-key smoke did not find a small resource.");
        using var actualLarge = dllCodec.DecodeDib(large.DibBytes)
                                ?? throw new InvalidOperationException("DLL icon magenta-key smoke could not decode large RT_BITMAP.");
        using var actualSmall = dllCodec.DecodeDib(small.DibBytes)
                                ?? throw new InvalidOperationException("DLL icon magenta-key smoke could not decode small RT_BITMAP.");
        AssertTransparentPixel(actualLarge, 1, 0, "DLL icon magenta-key large background");
        AssertNoVisibleMagenta(actualLarge, "DLL icon magenta-key large");
        AssertNoVisibleMagenta(actualSmall, "DLL icon magenta-key small");

        using var raw = Image.FromFile(sourcePath);
        using var normalized = DllIconBitmapCodec.NormalizeIconSource(raw, useCornerBackgroundKey: true);
        using var expectedSmall = DllIconBitmapCodec.ScaleToFit(normalized.Bitmap, small.Width, small.Height);
        AssertNoVisibleMagenta(expectedSmall, "DLL icon expected small");

        AssertDllIconLargeNearMagentaSourceWriteback(project, iconPath, smokeRoot);
    }

    private static void AssertDllIconLargeNearMagentaSourceWriteback(CczProject project, string iconPath, string smokeRoot)
    {
        var sourcePath = Path.Combine(smokeRoot, "large_near_magenta_item_icon.png");
        var userSourcePath = @"D:\Downloads\AI绘境_T20260625204544073B568D_1.png";
        var usingUserSource = File.Exists(userSourcePath);
        if (usingUserSource)
        {
            File.Copy(userSourcePath, sourcePath, overwrite: true);
        }
        else
        {
            using var generated = CreateLargeNearMagentaIconSource(2048, 2048);
            generated.Save(sourcePath, System.Drawing.Imaging.ImageFormat.Png);
        }

        var codec = new DllIconBitmapCodec();
        using (var source = Image.FromFile(sourcePath))
        using (var prepared = codec.PrepareIconSource(
                   source,
                   new Size(32, 32),
                   new Size(16, 16),
                   new IconSourcePrepareOptions(UseCornerBackgroundKey: true)))
        {
            if (prepared.MagentaKeyPixelCount <= 0 && prepared.CornerBackgroundPixelCount <= 0)
            {
                throw new InvalidOperationException("Large near-magenta source did not convert any background pixels to transparency.");
            }

            if (!usingUserSource &&
                (prepared.SourceOpaqueBounds.Width >= prepared.SourceWidth - 4 ||
                prepared.SourceOpaqueBounds.Height >= prepared.SourceHeight - 4)
            )
            {
                throw new InvalidOperationException($"Large near-magenta source was not cropped to visible bounds: {prepared.SourceOpaqueBounds} of {prepared.SourceWidth}x{prepared.SourceHeight}.");
            }

            AssertHasTransparentPixels(prepared.LargeBitmap, "prepared large near-magenta icon");
            AssertNoVisibleMagenta(prepared.LargeBitmap, "prepared large near-magenta icon");
            AssertNotMostlyWhite(prepared.LargeBitmap, "prepared large near-magenta icon");
            AssertNoVisibleMagenta(prepared.SmallBitmap, "prepared small near-magenta icon");
        }

        var service = new IconResourceReplaceService();
        var result = service.ReplaceBitmapIcon(project, iconPath, 4, sourcePath);
        if (!result.ReadbackVerified || result.ReadbackItems.Count == 0)
        {
            throw new InvalidOperationException("Large near-magenta DLL icon writeback did not pass readback verification: " + string.Join(" | ", result.ReadbackWarnings));
        }

        if (result.ReadbackItems.Any(item => item.WrittenBitCount != 32 || item.ActualBitCount != 32))
        {
            throw new InvalidOperationException("Large near-magenta DLL icon writeback did not write every readable variant as 32bpp.");
        }

        if (result.ReadbackItems.Any(item => item.ActualVisibleMagentaPixelCount > 0))
        {
            throw new InvalidOperationException("Large near-magenta DLL icon writeback left visible magenta pixels.");
        }

        if (result.ReadbackItems.Any(item => item.ActualWhiteishPixelCount > item.ActualWidth * item.ActualHeight / 2))
        {
            throw new InvalidOperationException("Large near-magenta DLL icon writeback produced a suspicious white-background variant.");
        }

        var resources = codec.ReadBitmapResources(iconPath);
        var slot = codec.ResolveGameIconSlot(iconPath, resources, 4, "Itemicon.dll");
        var large = slot.LargeSelectedVariant ?? codec.SelectLargeVariant(slot.Variants)
                    ?? throw new InvalidOperationException("Large near-magenta smoke did not find a large resource.");
        var small = slot.SmallSelectedVariant ?? codec.SelectSmallVariant(slot.Variants)
                    ?? throw new InvalidOperationException("Large near-magenta smoke did not find a small resource.");
        using var actualLarge = codec.DecodeDib(large.DibBytes)
                                ?? throw new InvalidOperationException("Large near-magenta smoke could not decode large RT_BITMAP.");
        using var actualSmall = codec.DecodeDib(small.DibBytes)
                                ?? throw new InvalidOperationException("Large near-magenta smoke could not decode small RT_BITMAP.");
        AssertHasTransparentPixels(actualLarge, "large near-magenta readback large");
        AssertNoVisibleMagenta(actualLarge, "large near-magenta readback large");
        AssertNotMostlyWhite(actualLarge, "large near-magenta readback large");
        AssertHasTransparentPixels(actualSmall, "large near-magenta readback small");
        AssertNoVisibleMagenta(actualSmall, "large near-magenta readback small");
    }

    private static Bitmap CreateMagentaKeyIconSource(int width, int height)
    {
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.Clear(Color.Magenta);
        for (var i = 0; i < Math.Min(width, height); i++)
        {
            bitmap.SetPixel(i, i, Color.Black);
        }

        bitmap.SetPixel(0, 0, Color.Red);
        bitmap.SetPixel(width - 1, 0, Color.Lime);
        bitmap.SetPixel(0, height - 1, Color.Blue);
        bitmap.SetPixel(width - 1, height - 1, Color.Yellow);
        bitmap.SetPixel(width / 2, height / 2 - 1, Color.Cyan);
        bitmap.SetPixel(width / 2 + 1, height / 2, Color.Cyan);
        return bitmap;
    }

    private static Bitmap CreateLargeNearMagentaIconSource(int width, int height)
    {
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.Clear(Color.FromArgb(255, 238, 12, 244));
        graphics.SmoothingMode = SmoothingMode.None;
        using var dark = new SolidBrush(Color.FromArgb(255, 25, 18, 18));
        using var gold = new SolidBrush(Color.FromArgb(255, 230, 174, 48));
        using var shadow = new SolidBrush(Color.FromArgb(255, 70, 38, 85));
        var cx = width / 2;
        graphics.FillPolygon(shadow, new[]
        {
            new Point(cx - 54, 220),
            new Point(cx + 54, 220),
            new Point(cx + 34, 1700),
            new Point(cx - 34, 1700)
        });
        graphics.FillPolygon(gold, new[]
        {
            new Point(cx - 32, 180),
            new Point(cx + 32, 180),
            new Point(cx + 18, 1680),
            new Point(cx - 18, 1680)
        });
        graphics.FillRectangle(dark, cx - 250, 1580, 500, 70);
        graphics.FillRectangle(gold, cx - 80, 1650, 160, 220);
        graphics.FillEllipse(dark, cx - 95, 1760, 190, 190);
        return bitmap;
    }

    private static void AssertTransparentPixel(Bitmap bitmap, int x, int y, string label)
    {
        var pixel = bitmap.GetPixel(x, y);
        if (pixel.A != 0)
        {
            throw new InvalidOperationException($"{label} expected transparent pixel at {x},{y}, got A={pixel.A} RGB={pixel.R},{pixel.G},{pixel.B}.");
        }
    }

    private static void AssertNoVisibleMagenta(Bitmap bitmap, string label)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A != 0 && DllIconBitmapCodec.IsMagentaKey(pixel))
                {
                    throw new InvalidOperationException($"{label} contains visible magenta-key pixel at {x},{y}.");
                }
            }
        }
    }

    private static void AssertHasTransparentPixels(Bitmap bitmap, string label)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0) return;
            }
        }

        throw new InvalidOperationException($"{label} does not contain any transparent pixels.");
    }

    private static void AssertNotMostlyWhite(Bitmap bitmap, string label)
    {
        var whiteish = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A != 0 && pixel.R >= 220 && pixel.G >= 220 && pixel.B >= 220)
                {
                    whiteish++;
                }
            }
        }

        if (whiteish > bitmap.Width * bitmap.Height / 2)
        {
            throw new InvalidOperationException($"{label} is suspiciously white: {whiteish}/{bitmap.Width * bitmap.Height}.");
        }
    }

    private static void AssertItemIconSlotMappingAndPreview(CczProject project, string iconPath, int iconIndex)
    {
        var dllCodec = new DllIconBitmapCodec();
        var resources = dllCodec.ReadBitmapResources(iconPath);
        if (resources.Count == 0)
        {
            throw new InvalidOperationException("Itemicon.dll smoke did not parse any RT_BITMAP resources.");
        }

        var minId = resources.Min(x => x.Id);
        var slot = dllCodec.ResolveGameIconSlot(iconPath, resources, iconIndex, "Itemicon.dll");
        if (minId == 100 && (slot.SmallResourceId != 100 || slot.LargeResourceId != 101))
        {
            throw new InvalidOperationException($"Itemicon.dll field 0 mapping changed: small={slot.SmallResourceId}, large={slot.LargeResourceId}, expected 100/101.");
        }

        var hasLanguageVariants = resources.Any(x => x.Id == slot.SmallResourceId && x.LanguageId == 0) &&
                                  resources.Any(x => x.Id == slot.SmallResourceId && x.LanguageId == 1041) &&
                                  resources.Any(x => x.Id == slot.LargeResourceId && x.LanguageId == 0) &&
                                  resources.Any(x => x.Id == slot.LargeResourceId && x.LanguageId == 1041);
        if (hasLanguageVariants && (!slot.SmallSelectedByWin32Probe || !slot.LargeSelectedByWin32Probe))
        {
            throw new InvalidOperationException("Itemicon.dll language variant smoke expected Win32 FindResource to select the active small/large variants.");
        }

        var preview = new ItemIconPreviewService().BuildPreview(project, iconIndex, "Itemicon.dll", "物品图标", 96);
        try
        {
            if (!preview.RenderMode.Equals("DLL RT_BITMAP", StringComparison.Ordinal) ||
                preview.SmallBitmap == null ||
                preview.LargeBitmap == null ||
                preview.SmallVariant == null ||
                preview.LargeVariant == null)
            {
                throw new InvalidOperationException("Item icon slot smoke preview did not return structured DLL RT_BITMAP small/large data.");
            }

            AssertWin32ResourceMatchesPreview(dllCodec, iconPath, resources, slot.SmallResourceId, preview.SmallVariant, preview.SmallBitmap, "small");
            AssertWin32ResourceMatchesPreview(dllCodec, iconPath, resources, slot.LargeResourceId, preview.LargeVariant, preview.LargeBitmap, "large");
        }
        finally
        {
            preview.Bitmap?.Dispose();
            preview.NativeBitmap?.Dispose();
            preview.SmallBitmap?.Dispose();
            preview.LargeBitmap?.Dispose();
        }
    }

    private static void AssertWin32ResourceMatchesPreview(
        DllIconBitmapCodec dllCodec,
        string iconPath,
        IReadOnlyList<DllIconBitmapResource> resources,
        int resourceId,
        IconResourceVariantInfo previewVariant,
        Bitmap previewBitmap,
        string role)
    {
        if (!dllCodec.TryReadWin32SelectedBitmapResource(iconPath, resourceId, out var win32Dib, out var warning))
        {
            throw new InvalidOperationException($"Win32 FindResource did not read Itemicon.dll {role} RT_BITMAP ID={resourceId}: {warning}");
        }

        var matched = resources.FirstOrDefault(resource => resource.Id == resourceId && resource.DibBytes.SequenceEqual(win32Dib))
                      ?? throw new InvalidOperationException($"Win32 FindResource selected {role} RT_BITMAP ID={resourceId}, but it did not match parsed variants.");
        if (previewVariant.LanguageId != matched.LanguageId ||
            previewVariant.BitCount != matched.BitCount ||
            previewVariant.Width != matched.Width ||
            previewVariant.Height != matched.Height)
        {
            throw new InvalidOperationException($"Item icon preview {role} variant does not match Win32 FindResource selection. preview={previewVariant.DisplayLabel}, win32=Lang {matched.LanguageId} {matched.Width}x{matched.Height} {matched.BitCount}bpp");
        }

        using var win32Bitmap = dllCodec.DecodeDib(win32Dib)
                                ?? throw new InvalidOperationException($"Win32-selected {role} RT_BITMAP ID={resourceId} could not be decoded.");
        if (!DllIconBitmapCodec.ArePixelEqual(previewBitmap, win32Bitmap))
        {
            throw new InvalidOperationException($"Item icon preview {role} bitmap does not match Win32-selected RT_BITMAP ID={resourceId}.");
        }
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

    private static void PaintAsymmetricIconSmokePattern(Bitmap bitmap)
    {
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.Clear(Color.Transparent);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 230, 30, 50));
        bitmap.SetPixel(bitmap.Width - 1, 0, Color.FromArgb(255, 30, 210, 70));
        bitmap.SetPixel(0, bitmap.Height - 1, Color.FromArgb(255, 40, 90, 230));
        bitmap.SetPixel(bitmap.Width - 1, bitmap.Height - 1, Color.FromArgb(255, 240, 210, 40));
        for (var i = 0; i < Math.Min(bitmap.Width, bitmap.Height); i++)
        {
            bitmap.SetPixel(i, i, Color.FromArgb(255, 180, 60, 220));
        }
    }

    private static void RunPixelEditorShortcutSmoke()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var bitmap = new Bitmap(8, 8);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                }

                using var document = new EditableImageDocument
                {
                    Target = new EditableImageTarget
                    {
                        Kind = EditableImageTargetKind.E5Standard,
                        DisplayName = "Shortcut Smoke",
                        TargetPath = "shortcut-smoke.png",
                        OperationKind = "shortcut smoke"
                    },
                    Bitmap = bitmap,
                    OriginalBitmap = new Bitmap(bitmap),
                    LoadDetail = "Shortcut Smoke；画布 8x8"
                };

                var writeCount = 0;
                using var dialog = new PixelImageEditorDialog(document, _ =>
                {
                    writeCount++;
                    return true;
                });

                AssertShortcut(dialog, Keys.P, "铅笔");
                AssertShortcut(dialog, Keys.E, "橡皮");
                AssertShortcut(dialog, Keys.I, "取色");
                AssertShortcut(dialog, Keys.F, "填充");
                AssertShortcut(dialog, Keys.L, "直线");
                AssertShortcut(dialog, Keys.R, "矩形");

                var initialGrid = dialog.IsGridVisibleForSmoke;
                if (!dialog.TryHandleShortcutForSmoke(Keys.Control | Keys.H) || dialog.IsGridVisibleForSmoke == initialGrid)
                {
                    throw new InvalidOperationException("Ctrl+H did not toggle the pixel editor grid.");
                }

                if (!dialog.TryHandleShortcutForSmoke(Keys.G) || dialog.IsGridVisibleForSmoke != initialGrid)
                {
                    throw new InvalidOperationException("G did not toggle the pixel editor grid.");
                }

                var initialZoom = dialog.ZoomForSmoke;
                if (!dialog.TryHandleShortcutForSmoke(Keys.Oemplus) || dialog.ZoomForSmoke != initialZoom + 1)
                {
                    throw new InvalidOperationException("+ did not zoom in by one step.");
                }

                if (!dialog.TryHandleShortcutForSmoke(Keys.OemMinus) || dialog.ZoomForSmoke != initialZoom)
                {
                    throw new InvalidOperationException("- did not zoom out by one step.");
                }

                dialog.TryHandleShortcutForSmoke(Keys.Oemplus);
                if (!dialog.TryHandleShortcutForSmoke(Keys.D0) || dialog.ZoomForSmoke != 12)
                {
                    throw new InvalidOperationException("0 did not reset zoom to the default value.");
                }

                SavePixelEditorUndoForSmoke(dialog);
                bitmap.SetPixel(0, 0, Color.Red);
                if (!dialog.TryHandleShortcutForSmoke(Keys.Control | Keys.Z) || bitmap.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb())
                {
                    throw new InvalidOperationException("Ctrl+Z did not restore the previous pixel state.");
                }

                if (!dialog.TryHandleShortcutForSmoke(Keys.Control | Keys.Y) || bitmap.GetPixel(0, 0).ToArgb() != Color.Red.ToArgb())
                {
                    throw new InvalidOperationException("Ctrl+Y did not redo the pixel state.");
                }

                if (!dialog.TryHandleShortcutForSmoke(Keys.Control | Keys.Shift | Keys.Z) || bitmap.GetPixel(0, 0).ToArgb() != Color.Red.ToArgb())
                {
                    throw new InvalidOperationException("Ctrl+Shift+Z was not accepted as redo.");
                }

                if (!dialog.TryHandleShortcutForSmoke(Keys.Control | Keys.S) || writeCount != 1 || !dialog.IsCurrentRevisionWritten)
                {
                    throw new InvalidOperationException("Ctrl+S did not route through the writeback delegate.");
                }

                using var disabledDialog = new PixelImageEditorDialog(document);
                if (disabledDialog.TryHandleShortcutForSmoke(Keys.Control | Keys.S))
                {
                    throw new InvalidOperationException("Ctrl+S should be disabled when no writeback delegate is available.");
                }
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

    private static void AssertShortcut(PixelImageEditorDialog dialog, Keys key, string expectedTool)
    {
        if (!dialog.TryHandleShortcutForSmoke(key) ||
            !dialog.CurrentToolDisplayNameForSmoke.Equals(expectedTool, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Pixel editor shortcut {key} did not select tool {expectedTool}.");
        }
    }

    private static void SavePixelEditorUndoForSmoke(PixelImageEditorDialog dialog)
    {
        var method = typeof(PixelImageEditorDialog).GetMethod("SaveUndo", BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException("PixelImageEditorDialog.SaveUndo was not found.");
        method.Invoke(dialog, null);
    }
}
