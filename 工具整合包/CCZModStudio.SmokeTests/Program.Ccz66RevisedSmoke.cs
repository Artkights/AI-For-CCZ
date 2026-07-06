using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

internal partial class Program
{
    static void Run66RevisedSmoke(CCZModStudio.Models.CczProject defaultProject)
    {
        var gameRoot = Path.Combine(defaultProject.WorkspaceRoot, "基底", "新改曹操傳6.6修正版");
        if (!Directory.Exists(gameRoot))
        {
            throw new DirectoryNotFoundException("6.6 revised baseline project was not found: " + gameRoot);
        }

        var project = new ProjectDetector().CreateProjectFromGameRoot(gameRoot);
        var engine = new CczEngineProfileService().Detect(project);
        if (!Ccz66RevisedLayout.Is66(engine) || engine.ExeSize != Ccz66RevisedLayout.Ekd5ExeSize)
        {
            throw new InvalidOperationException($"6.6 engine detection failed: version={engine.VersionHint}, exeSize={engine.ExeSize}");
        }

        var statuses = project.GetFileStatuses().ToList();
        foreach (var required in Ccz66RevisedLayout.RequiredE5Resources)
        {
            var status = statuses.SingleOrDefault(item => item.Name.Equals(required, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Missing 6.6 required resource status: " + required);
            if (!status.Exists || !status.CountsAsMissing)
            {
                throw new InvalidOperationException($"Invalid 6.6 required resource status: {status.Name}, exists={status.Exists}, counts={status.CountsAsMissing}");
            }
        }

        foreach (var obsolete in new[] { "Itemicon.dll", "Mgcicon.dll", "ts.e5" })
        {
            var status = statuses.SingleOrDefault(item => item.Name.Equals(obsolete, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Missing 6.6 obsolete resource status: " + obsolete);
            if (status.CountsAsMissing || !status.Kind.Equals("6.6-obsolete", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"6.6 obsolete resource is still counted as missing: {obsolete}");
            }
        }

        foreach (var legacy in new[] { "Smlmap.e5", "Spalet.e5" })
        {
            var status = statuses.SingleOrDefault(item => item.Name.Equals(legacy, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Missing 6.6 legacy optional resource status: " + legacy);
            if (status.CountsAsMissing || !status.Kind.Equals("legacy-optional", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"6.6 legacy optional resource is still counted as missing: {legacy}");
            }
        }

        var mapatr = statuses.SingleOrDefault(item => item.Name.Equals("Mapatr.dll", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Missing 6.6 Mapatr.dll legacy dependency status.");
        if (mapatr.CountsAsMissing || !mapatr.Kind.Equals("legacy-terrain-editor-dependency", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("6.6 Mapatr.dll should be reported only as a legacy terrain editor dependency.");
        }

        var catalogService = new ImageResourceCatalogService();
        var catalog = catalogService.BuildCatalog(project);
        AssertCatalogCount(catalog, "Item.e5", 512);
        AssertCatalogCount(catalog, "Mtem.e5", 176);
        AssertCatalogCount(catalog, "DT.e5", 60);
        AssertCatalogCount(catalog, "Fb.e5", 673);
        AssertCatalogCount(catalog, "U_select.e5", 32);
        AssertCatalogCount(catalog, "Pmap.e5", 477);
        AssertCatalogCount(catalog, "Cmdicon.dll", 29);

        if (catalog.Any(item => item.FileName.Equals("Itemicon.dll", StringComparison.OrdinalIgnoreCase) ||
                                item.FileName.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("6.6 catalog should not expose obsolete Itemicon.dll/Mgcicon.dll resources.");
        }

        var itemE5 = catalog.Single(item => item.FileName.Equals("Item.e5", StringComparison.OrdinalIgnoreCase));
        var itemEntries = catalogService.ReadEntries(itemE5).ToDictionary(entry => entry.ImageNumber);
        AssertUsageContains(itemEntries, 1, "blank small");
        AssertUsageContains(itemEntries, 2, "blank large");
        AssertUsageContains(itemEntries, 3, "field value 1");
        AssertUsageContains(itemEntries, 4, "large preview");

        var iconPreview = new ItemIconPreviewService();
        Assert66ItemIconPreview(iconPreview.BuildPreview(project, 0), 0, 1, 2);
        Assert66ItemIconPreview(iconPreview.BuildPreview(project, 1), 1, 3, 4);
        Assert66ItemIconPreview(iconPreview.BuildPreview(project, 2), 2, 5, 6);

        var aiPlan = new AiImageAssetService().BuildPromptPlan(
            project,
            "dll_icon",
            "smoke item icon",
            null,
            1,
            null,
            null,
            null,
            null,
            1,
            null,
            null,
            null);
        if (!aiPlan.TargetRelativePath.Equals("E5/Item.e5", StringComparison.OrdinalIgnoreCase) ||
            !aiPlan.TargetImageNumbers.SequenceEqual(new[] { 3, 4 }) ||
            !aiPlan.MappingSummary.Contains("small #3 / large #4", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "6.6 AI dll_icon item mapping failed: target=" + aiPlan.TargetRelativePath +
                ", numbers=" + string.Join("/", aiPlan.TargetImageNumbers) +
                ", mapping=" + aiPlan.MappingSummary);
        }
        Assert66AiItemIconPreparedFiles(project, aiPlan);
        Assert66ItemIconRasterNormalize();
        var itemIconSmokeProject = Create66ItemIconWriteSmokeProject(defaultProject, gameRoot);
        Run66ItemE5BatchImportSmoke(itemIconSmokeProject);
        Run66ItemIconPixelEditorSmoke(itemIconSmokeProject);
        Run66ItemIconBmpExportSmoke(itemIconSmokeProject);

        var uSelect = catalog.Single(item => item.FileName.Equals("U_select.e5", StringComparison.OrdinalIgnoreCase));
        var entries = catalogService.ReadEntries(uSelect).ToDictionary(entry => entry.ImageNumber);
        foreach (var imageNumber in new[] { 22, 23, 25, 26, 27, 28, 29, 30, 31, 32 })
        {
            if (!entries.TryGetValue(imageNumber, out var entry) ||
                !entry.Usage.Contains("6.6 U_select", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"6.6 U_select slot #{imageNumber} usage was not annotated.");
            }
        }

        var parser = new HexTableParser();
        var rawTables = parser.Load(project.HexTableXmlPath);
        var tables = new Ccz66HexTableAugmentationService().AugmentForProject(project, rawTables);
        var actualPrefixes = tables
            .Where(table => HexTableNameResolver.Is6XTable(table))
            .Select(table => table.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!actualPrefixes.Contains("6.6", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("6.6 HexTable augmentation did not expose native 6.6 tables.");
        }

        var table = HexTableNameResolver.ResolveForProject(project, tables, "6.6-0 人物");
        var validation = new HexTableReader().Validate(project, table);
        if (!validation.TableStatus.Equals(Ccz66RevisedLayout.Native66TableStatus, StringComparison.OrdinalIgnoreCase) ||
            !validation.CanWrite ||
            !table.IsGeneratedCompatibilityTable ||
            !table.SourceTableName.StartsWith("6.5-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"6.6 native generated table status mismatch: status={validation.TableStatus}, source={table.SourceTableName}.");
        }

        var fallbackTable = rawTables.FirstOrDefault(item => item.TableName.StartsWith("6.5-5 ", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("6.5 strategy table was not found for fallback validation.");
        var fallbackValidation = new HexTableReader().Validate(project, fallbackTable);
        if (!fallbackValidation.TableStatus.Equals(Ccz66RevisedLayout.CrossVersionFallbackTableStatus, StringComparison.OrdinalIgnoreCase) ||
            fallbackValidation.CanWrite ||
            !fallbackValidation.Warnings.Any(warning => warning.Contains("CrossVersionFallback", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("6.6 HexTable cross-version fallback warning was not emitted for a non-augmented table.");
        }

        var templates = new ScenarioCommandParameterTemplateService();
        var templateItems = templates.BuildCatalogItems(dictionary: null);
        foreach (var idHex in new[] { "72-2", "72-3", "72-5", "72-12", "72-14", "72-28", "72-30", "72-31", "72-32", "72-34", "72-35", "72-36" })
        {
            if (templateItems.All(item => !item.IdHex.Equals(idHex, StringComparison.OrdinalIgnoreCase) &&
                                          !item.TemplateName.StartsWith(idHex + " ", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"6.6 scenario template {idHex} was not registered.");
            }
        }

        Console.WriteLine(
            "CCZ66_REVISED_SMOKE OK " +
            $"gameRoot={project.GameRoot} " +
            $"exeSize={engine.ExeSize} " +
            "resources=Item.e5:512,Mtem.e5:176,DT.e5:60,Fb.e5:673,U_select.e5:32,Pmap.e5:477,Cmdicon.dll:29 " +
            $"hexNative={validation.TableStatus} hexFallback={fallbackValidation.TableStatus} actualPrefixes={string.Join(",", actualPrefixes)}");
    }

    private static CczProject Create66ItemIconWriteSmokeProject(CczProject defaultProject, string baselineRoot)
    {
        var smokeRoot = Path.Combine(
            defaultProject.WorkspaceRoot,
            "CCZModStudio_TestCopies",
            "Ccz66ItemIconSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);

        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" })
        {
            var source = Path.Combine(baselineRoot, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, fileName), overwrite: false);
            }
        }

        var e5Root = Path.Combine(smokeRoot, "E5");
        Directory.CreateDirectory(e5Root);
        File.Copy(Path.Combine(baselineRoot, "E5", "Item.e5"), Path.Combine(e5Root, "Item.e5"), overwrite: false);
        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={baselineRoot}\r\nPurpose=6.6 Item.e5 item icon smoke\r\n");
        return new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
    }

    private static void Assert66ItemIconRasterNormalize()
    {
        var normalizer = new ItemIconRasterNormalizeService();
        using (var source = new Bitmap(2, 1, PixelFormat.Format32bppArgb))
        {
            source.SetPixel(0, 0, ItemIconRasterNormalizeService.GameMagentaKey);
            source.SetPixel(1, 0, Color.FromArgb(255, 255, 0, 255));
            var pair = normalizer.NormalizePair(source, "magenta-key-smoke");
            var info = ItemIconRasterNormalizeService.ReadBmpInfo(pair.Large.BmpBytes);
            if (info.Width != 32 || info.Height != 32 ||
                info.TopLeftR != 247 || info.TopLeftG != 0 || info.TopLeftB != 255)
            {
                throw new InvalidOperationException("Item icon magenta-key normalization did not write #F700FF BMP output.");
            }
        }

        using (var source = new Bitmap(56, 64, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(source))
        using (var brush = new SolidBrush(Color.FromArgb(255, 40, 120, 220)))
        {
            graphics.Clear(Color.Transparent);
            graphics.FillRectangle(brush, 14, 8, 28, 48);
            var pair = normalizer.NormalizePair(source, "non-square-smoke");
            using var large = pair.Large.CreateTransparentBitmap();
            var bounds = FindVisibleBoundsForSmoke(large);
            if (large.Width != 32 || large.Height != 32 || bounds.Width >= bounds.Height)
            {
                throw new InvalidOperationException($"Item icon non-square normalization appears stretched: bounds={bounds}.");
            }
        }

        using (var source = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(source))
        using (var brush = new SolidBrush(Color.FromArgb(255, 220, 80, 40)))
        {
            graphics.Clear(Color.Transparent);
            graphics.FillRectangle(brush, 48, 4, 8, 8);
            var pair = normalizer.NormalizePair(source, "asymmetric-border-smoke");
            using var large = pair.Large.CreateTransparentBitmap();
            var bounds = FindVisibleBoundsForSmoke(large);
            var centerX = bounds.Left + (bounds.Width - 1) / 2.0;
            var centerY = bounds.Top + (bounds.Height - 1) / 2.0;
            if (Math.Abs(centerX - 15.5) > 1.5 || Math.Abs(centerY - 15.5) > 1.5)
            {
                throw new InvalidOperationException($"Item icon asymmetric transparent border was not recentered: bounds={bounds}.");
            }
        }

        using (var source = new Bitmap(40, 40, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(source))
        using (var background = new SolidBrush(Color.FromArgb(255, 20, 80, 180)))
        using (var foreground = new SolidBrush(Color.FromArgb(255, 220, 60, 40)))
        {
            graphics.FillRectangle(background, 0, 0, 40, 40);
            graphics.FillRectangle(foreground, 12, 12, 16, 16);
            var pair = normalizer.NormalizePair(source, "colored-background-smoke");
            using var large = pair.Large.CreateTransparentBitmap();
            var pixel = large.GetPixel(0, 0);
            if (pixel.A == 0 || pixel.R != 20 || pixel.G != 80 || pixel.B != 180)
            {
                throw new InvalidOperationException("Item icon normalization incorrectly removed an ordinary colored background.");
            }
        }
    }

    private static void Run66ItemE5BatchImportSmoke(CczProject project)
    {
        var materialRoot = Path.Combine(project.GameRoot, "_ItemIconImportMaterials");
        Directory.CreateDirectory(materialRoot);
        var icon0 = Path.Combine(materialRoot, "item_icon_0.png");
        var icon1 = Path.Combine(materialRoot, "item_icon_1.png");
        var icon2 = Path.Combine(materialRoot, "item_icon_2.png");
        CreateTransparentIconPng(icon0, 64, 64, new Rectangle(10, 18, 44, 28), Color.FromArgb(255, 220, 60, 40));
        CreateTransparentIconPng(icon1, 56, 64, new Rectangle(8, 12, 36, 40), Color.FromArgb(255, 40, 140, 220));
        CreateTransparentIconPng(icon2, 32, 32, new Rectangle(6, 5, 20, 22), Color.FromArgb(255, 80, 190, 90));

        var service = new BatchItemIconImportService();
        var request = new BatchItemIconImportRequest
        {
            SourceFiles = new[] { icon0, icon1, icon2 },
            TargetRows = new[]
            {
                new BatchItemIconTargetRow(0, "Item Icon 0", 0),
                new BatchItemIconTargetRow(1, "Item Icon 1", 1),
                new BatchItemIconTargetRow(2, "Item Icon 2", 2)
            },
            MatchMode = "auto",
            WriteMode = "test_copy"
        };

        var preview = service.Preview(project, request);
        if (!preview.CanWrite ||
            !preview.ResourceKind.Equals("E5", StringComparison.OrdinalIgnoreCase) ||
            preview.TotalOperationCount != 6 ||
            preview.Items.Count != 3 ||
            preview.Items.Any(item => item.SmallWidth != 16 || item.SmallHeight != 16 || item.LargeWidth != 32 || item.LargeHeight != 32) ||
            preview.Items.Any(item => item.TargetImageNumbers.Count != 2))
        {
            throw new InvalidOperationException("6.6 Item.e5 batch item icon preview did not normalize to small/large pairs.");
        }

        var result = service.Replace(project, request);
        if (result.E5Result == null ||
            result.E5Result.OperationCount != 6 ||
            !File.Exists(result.E5Result.BackupPath) ||
            !File.Exists(result.AggregateReportPath))
        {
            throw new InvalidOperationException("6.6 Item.e5 batch item icon write did not create the expected E5 batch result.");
        }

        var itemPath = Ccz66RevisedLayout.ResolveResourcePath(project, "E5\\Item.e5");
        var e5 = new E5ImageReplaceService();
        for (var field = 0; field <= 2; field++)
        {
            var (small, large) = Ccz66RevisedLayout.ResolveItemIconImageNumbers(field);
            AssertItemIconBmpEntry(e5, itemPath, small, 16, $"field {field} small");
            AssertItemIconBmpEntry(e5, itemPath, large, 32, $"field {field} large");
        }
    }

    private static void Run66ItemIconPixelEditorSmoke(CczProject project)
    {
        var itemPath = Ccz66RevisedLayout.ResolveResourcePath(project, "E5\\Item.e5");
        var codec = new EditableImageCodecService();
        var e5 = new E5ImageReplaceService();
        var target = new EditableImageTarget
        {
            Kind = EditableImageTargetKind.E5Standard,
            DisplayName = "Smoke Item.e5 field 1",
            TargetPath = itemPath,
            ImageNumber = 4,
            IsItemIconPair = true,
            SmallImageNumber = 3,
            LargeImageNumber = 4,
            OperationKind = "6.6 item icon pixel editor smoke"
        };

        using (var document = codec.Load(project, target))
        {
            if (document.Bitmap.Width != 32 || document.Bitmap.Height != 32)
            {
                throw new InvalidOperationException($"6.6 Item.e5 pixel editor should open a 32x32 large canvas, actual={document.Bitmap.Width}x{document.Bitmap.Height}.");
            }

            using (var graphics = Graphics.FromImage(document.Bitmap))
            using (var brush = new SolidBrush(Color.FromArgb(255, 12, 34, 56)))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.Clear(Color.Transparent);
                graphics.FillRectangle(brush, 10, 8, 12, 16);
            }

            var preview = codec.PreviewWrite(project, target, document.Bitmap);
            if (preview.E5Preview == null || preview.E5Preview.OperationCount != 2 ||
                !preview.E5Preview.Operations.Select(operation => operation.ImageNumber).SequenceEqual(new[] { 3, 4 }))
            {
                throw new InvalidOperationException("6.6 Item.e5 pixel editor preview should write small #3 and large #4 together.");
            }

            var result = codec.Write(project, target, document.Bitmap);
            if (result.E5Result == null || result.E5Result.OperationCount != 2 ||
                !File.Exists(result.BackupPath) || !File.Exists(result.ReportPath))
            {
                throw new InvalidOperationException("6.6 Item.e5 pixel editor writeback result is missing backup or report.");
            }
        }

        AssertItemIconBmpEntry(e5, itemPath, 3, 16, "pixel editor small");
        AssertItemIconBmpEntry(e5, itemPath, 4, 32, "pixel editor large");
        using var smallBitmap = ItemIconRasterNormalizeService.DecodeGameIconBmp(e5.ReadEntryBytes(itemPath, 3));
        using var largeBitmap = ItemIconRasterNormalizeService.DecodeGameIconBmp(e5.ReadEntryBytes(itemPath, 4));
        var smallBounds = FindVisibleBoundsForSmoke(smallBitmap);
        var largeBounds = FindVisibleBoundsForSmoke(largeBitmap);
        var smallCenterX = smallBounds.Left + (smallBounds.Width - 1) / 2.0;
        var smallCenterY = smallBounds.Top + (smallBounds.Height - 1) / 2.0;
        var largeCenterX = largeBounds.Left + (largeBounds.Width - 1) / 2.0;
        var largeCenterY = largeBounds.Top + (largeBounds.Height - 1) / 2.0;
        if (Math.Abs(smallCenterX - largeCenterX / 2.0) > 1.0 ||
            Math.Abs(smallCenterY - largeCenterY / 2.0) > 1.0)
        {
            throw new InvalidOperationException($"6.6 Item.e5 pixel editor small/large anchors diverged: small={smallBounds}, large={largeBounds}.");
        }
    }

    private static void Run66ItemIconBmpExportSmoke(CczProject project)
    {
        var output = Path.Combine(project.GameRoot, "_ItemIconBmpExport");
        var result = new BmpImageExportService().Export(project, new BmpExportRequest
        {
            Kind = BmpExportKind.ItemIcon,
            OutputRoot = output,
            SingleMode = true,
            OverwriteExisting = true,
            Targets = new[]
            {
                new BmpExportTarget { RowId = 0, DisplayName = "Item0", FieldValue = 0 }
            }
        });

        if (result.Files.Count != 1 || result.SkippedItems.Count != 0)
        {
            throw new InvalidOperationException($"6.6 item icon BMP export mismatch: files={result.Files.Count}, skipped={result.SkippedItems.Count}.");
        }

        var path = Path.Combine(output, "item_icon_0.bmp");
        using var bitmap = new Bitmap(path);
        var topLeft = bitmap.GetPixel(0, 0);
        if (bitmap.Width != 32 || bitmap.Height != 32 ||
            topLeft.R != 247 || topLeft.G != 0 || topLeft.B != 255)
        {
            throw new InvalidOperationException($"6.6 item_icon_0.bmp export should be 32x32 with #F700FF transparent key, actual={bitmap.Width}x{bitmap.Height} topLeft={topLeft}.");
        }
    }

    private static void CreateTransparentIconPng(string path, int width, int height, Rectangle visibleBounds, Color color)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(color);
        graphics.Clear(Color.Transparent);
        graphics.FillEllipse(brush, visibleBounds);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void AssertItemIconBmpEntry(E5ImageReplaceService e5, string itemPath, int imageNumber, int expectedSize, string label)
    {
        var bytes = e5.ReadEntryBytes(itemPath, imageNumber);
        if (bytes.Length < 2 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
        {
            throw new InvalidOperationException($"{label}: Item.e5 #{imageNumber} was not written as BMP.");
        }

        var info = ItemIconRasterNormalizeService.ReadBmpInfo(bytes);
        if (info.Width != expectedSize || info.Height != expectedSize || info.BitCount != 24 ||
            info.TopLeftR != 247 || info.TopLeftG != 0 || info.TopLeftB != 255)
        {
            throw new InvalidOperationException(
                $"{label}: Item.e5 #{imageNumber} expected {expectedSize}x{expectedSize} 24bpp BMP with #F700FF key, " +
                $"actual={info.Width}x{info.Height} {info.BitCount}bpp topLeft=({info.TopLeftR},{info.TopLeftG},{info.TopLeftB}).");
        }
    }

    private static Rectangle FindVisibleBoundsForSmoke(Bitmap bitmap)
    {
        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0) continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < 0)
        {
            throw new InvalidOperationException("Smoke bitmap has no visible pixels.");
        }

        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static void Assert66AiItemIconPreparedFiles(CCZModStudio.Models.CczProject project, CCZModStudio.Models.AiImagePromptPlan plan)
    {
        var sourcePath = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports", "Smoke", "ccz66_item_icon_source.png");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        using (var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(255, 180, 40, 20));
            graphics.FillEllipse(brush, 8, 8, 48, 48);
            bitmap.Save(sourcePath, ImageFormat.Png);
        }

        var prepared = new AiImageAssetService().PrepareExistingImage(project, plan, sourcePath);
        if (prepared.PreparedFiles.Count != 2)
        {
            throw new InvalidOperationException("6.6 AI Item.e5 prepare should create small and large outputs.");
        }

        var small = prepared.PreparedFiles.Single(file => file.Role.Equals("small", StringComparison.OrdinalIgnoreCase));
        var large = prepared.PreparedFiles.Single(file => file.Role.Equals("large", StringComparison.OrdinalIgnoreCase));
        if (!small.TargetImageNumbers.SequenceEqual(new[] { 3 }) ||
            !large.TargetImageNumbers.SequenceEqual(new[] { 4 }) ||
            small.OutputWidth != 16 ||
            small.OutputHeight != 16 ||
            large.OutputWidth != 32 ||
            large.OutputHeight != 32)
        {
            throw new InvalidOperationException(
                $"6.6 AI Item.e5 prepared outputs are invalid: small={string.Join("/", small.TargetImageNumbers)} {small.OutputWidth}x{small.OutputHeight}, " +
                $"large={string.Join("/", large.TargetImageNumbers)} {large.OutputWidth}x{large.OutputHeight}");
        }
    }

    private static void AssertUsageContains(IReadOnlyDictionary<int, CCZModStudio.Models.ImageResourceEntryInfo> entries, int imageNumber, string expected)
    {
        if (!entries.TryGetValue(imageNumber, out var entry) ||
            !entry.Usage.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"6.6 Item.e5 entry #{imageNumber} usage does not contain '{expected}': {entry?.Usage}");
        }
    }

    private static void Assert66ItemIconPreview(CCZModStudio.Core.ItemIconPreviewResult preview, int fieldValue, int small, int large)
    {
        if (preview.Bitmap == null ||
            !preview.SourcePath.EndsWith("Item.e5", StringComparison.OrdinalIgnoreCase) ||
            !preview.Message.Contains($"field={fieldValue}", StringComparison.OrdinalIgnoreCase) ||
            !preview.Message.Contains($"small=#{small}", StringComparison.OrdinalIgnoreCase) ||
            !preview.Message.Contains($"large=#{large}", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"6.6 Item.e5 preview mapping failed for field {fieldValue}: {preview.Message}");
        }
    }

    private static void AssertCatalogCount(IReadOnlyList<CCZModStudio.Models.ImageResourceFileInfo> catalog, string fileName, int expectedCount)
    {
        var item = catalog.SingleOrDefault(item => item.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Image resource was not found in catalog: " + fileName);
        if (!item.Exists || item.EntryCount != expectedCount || !item.SupportsPreview)
        {
            throw new InvalidOperationException($"Unexpected catalog count for {fileName}: exists={item.Exists}, entries={item.EntryCount}, preview={item.SupportsPreview}, expected={expectedCount}");
        }
    }

    static void Run66RegressionSmoke(CCZModStudio.Models.CczProject defaultProject)
    {
        var gameRoot = Path.Combine(defaultProject.WorkspaceRoot, "基底", "加强版6.5未加密版");
        var project = new ProjectDetector().CreateProjectFromGameRoot(gameRoot);
        var engine = new CczEngineProfileService().Detect(project);
        if (engine.VersionHint != "6.5")
        {
            throw new InvalidOperationException("6.5 regression project was not detected as 6.5.");
        }

        var catalogService = new ImageResourceCatalogService();
        var catalog = catalogService.BuildCatalog(project);
        var itemIcon = catalog.SingleOrDefault(item => item.FileName.Equals("Itemicon.dll", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("6.5 catalog no longer exposes Itemicon.dll.");
        var mgcIcon = catalog.SingleOrDefault(item => item.FileName.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("6.5 catalog no longer exposes Mgcicon.dll.");

        if (!itemIcon.Exists || itemIcon.EntryCount <= 0 || !itemIcon.ResourceFormat.Contains("DLL", StringComparison.OrdinalIgnoreCase) ||
            !mgcIcon.Exists || mgcIcon.EntryCount <= 0 || !mgcIcon.ResourceFormat.Contains("DLL", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"6.5 icon DLL regression failed: item={itemIcon.EntryCount}/{itemIcon.ResourceFormat}, mgc={mgcIcon.EntryCount}/{mgcIcon.ResourceFormat}");
        }

        if (catalog.Any(item => item.FileName.Equals("Item.e5", StringComparison.OrdinalIgnoreCase) ||
                                item.FileName.Equals("Mtem.e5", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("6.5 catalog should not promote 6.6 Item.e5/Mtem.e5 known resources.");
        }

        var itemPreview = new ItemIconPreviewService().BuildPreview(project, 0);
        var strategyPreview = new ItemIconPreviewService().BuildPreview(project, 0, "Mgcicon.dll", "策略图标");
        if (itemPreview.AvailableIconCount <= 0 || strategyPreview.AvailableIconCount <= 0)
        {
            throw new InvalidOperationException("6.5 icon preview no longer reads DLL icon resources.");
        }

        Console.WriteLine($"CCZ66_REGRESSION_SMOKE OK engine={engine.VersionHint} itemDll={itemIcon.EntryCount} mgcDll={mgcIcon.EntryCount}");
    }
}
