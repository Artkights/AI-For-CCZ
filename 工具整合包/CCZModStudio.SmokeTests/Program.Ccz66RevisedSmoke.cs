using CCZModStudio.Core;
using CCZModStudio.Formats;
using System.Drawing;
using System.Drawing.Imaging;

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
        var tables = parser.Load(project.HexTableXmlPath);
        var actualPrefixes = tables
            .Where(table => HexTableNameResolver.Is6XTable(table))
            .Select(table => table.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (actualPrefixes.Contains("6.6", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Smoke expectation changed: current HexTable already contains 6.6 tables.");
        }

        var table = HexTableNameResolver.ResolveForProject(project, tables, "6.6-0 人物");
        var validation = new HexTableReader().Validate(project, table);
        if (!validation.Warnings.Any(warning => warning.Contains("CrossVersionFallback", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("6.6 HexTable fallback warning was not emitted.");
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
            $"hexFallback=CrossVersionFallback actualPrefixes={string.Join(",", actualPrefixes)}");
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
        if (itemPreview.AvailableIconCount <= 0 ||
            strategyPreview.AvailableIconCount <= 0 ||
            !itemPreview.RenderMode.Equals("DLL RT_BITMAP", StringComparison.Ordinal) ||
            !strategyPreview.RenderMode.Equals("DLL RT_BITMAP", StringComparison.Ordinal) ||
            itemPreview.ResourceVariants == null ||
            itemPreview.ResourceVariants.Count == 0 ||
            strategyPreview.ResourceVariants == null ||
            strategyPreview.ResourceVariants.Count == 0)
        {
            throw new InvalidOperationException("6.5 icon preview no longer reads DLL icon resources.");
        }

        using (itemPreview.Bitmap)
        using (itemPreview.NativeBitmap)
        using (itemPreview.SmallBitmap)
        using (itemPreview.LargeBitmap)
        using (strategyPreview.Bitmap)
        using (strategyPreview.NativeBitmap)
        using (strategyPreview.SmallBitmap)
        using (strategyPreview.LargeBitmap)
        {
        }

        var toolExe = typeof(CCZModStudio.MainForm).Assembly.Location;
        toolExe = Path.ChangeExtension(toolExe, ".exe");
        if (File.Exists(toolExe))
        {
            var fallbackProject = new CCZModStudio.Models.CczProject
            {
                WorkspaceRoot = Path.GetDirectoryName(toolExe)!,
                GameRoot = Path.GetDirectoryName(toolExe)!,
                HexTableXmlPath = project.HexTableXmlPath
            };
            var fallbackPreview = new ItemIconPreviewService().BuildPreview(fallbackProject, 0, Path.GetFileName(toolExe), "普通程序图标");
            if (fallbackPreview.AvailableIconCount <= 0 ||
                !fallbackPreview.RenderMode.Equals("Windows ICO", StringComparison.Ordinal) ||
                fallbackPreview.Bitmap == null)
            {
                throw new InvalidOperationException("Non-game DLL/EXE icon preview did not keep the Windows ICO fallback path.");
            }

            using (fallbackPreview.Bitmap)
            using (fallbackPreview.NativeBitmap)
            {
            }
        }

        Console.WriteLine($"CCZ66_REGRESSION_SMOKE OK engine={engine.VersionHint} itemDll={itemIcon.EntryCount} mgcDll={mgcIcon.EntryCount}");
    }
}
