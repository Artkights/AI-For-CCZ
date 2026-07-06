using System.Data;
using System.Globalization;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

internal partial class Program
{
    private const int QingerExpectedExeSize = 1_413_120;

    static void RunQinger66ReadSmoke()
    {
        var project = LoadQinger66Project();
        var engine = new CczEngineProfileService().Detect(project);
        AssertQinger66Engine(engine);

        var rawTables = new HexTableParser().Load(project.HexTableXmlPath);
        var tables = new Ccz66HexTableAugmentationService().AugmentForProject(project, rawTables);
        var tableReader = new HexTableReader();
        var diagnostics = new Qinger66DiagnosticsService().Build(project, engine, tables, tableReader);
        var itemAudit = new Qinger66ItemAuditService().Build(project, engine, tables);
        if (!diagnostics.Applies)
        {
            throw new InvalidOperationException("Qinger 6.6 diagnostics did not apply to the target project.");
        }

        AssertQinger66ItemAudit(project, itemAudit.Summary, itemAudit.Rows);
        AssertQinger66ItemEffectNames(project, tables);

        foreach (var resource in diagnostics.RequiredResources)
        {
            if (!resource.Exists)
            {
                throw new FileNotFoundException("Qinger 6.6 required resource is missing.", resource.Path);
            }
        }

        var tableReads = new List<object>();
        foreach (var tableDiagnostic in diagnostics.Tables)
        {
            if (!tableDiagnostic.IsUsable)
            {
                throw new InvalidOperationException($"Qinger 6.6 table is not usable: {tableDiagnostic.RequestedName}, status={tableDiagnostic.TableStatus}");
            }

            var table = HexTableNameResolver.ResolveForProject(project, tables, tableDiagnostic.RequestedName);
            var read = tableReader.Read(project, table, tables);
            tableReads.Add(new
            {
                tableDiagnostic.RequestedName,
                tableDiagnostic.ResolvedName,
                read.Validation.TableStatus,
                read.Validation.WriteRisk,
                Rows = read.Data.Rows.Count,
                Columns = read.Data.Columns.Count
            });
        }

        var catalogService = new ImageResourceCatalogService();
        var catalog = catalogService.BuildCatalog(project);
        foreach (var requiredFile in new[] { "Item.e5", "Mtem.e5", "DT.e5", "Fb.e5", "U_select.e5", "Pmap.e5" })
        {
            var item = catalog.FirstOrDefault(resource => resource.FileName.Equals(requiredFile, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Qinger 6.6 image catalog did not expose " + requiredFile);
            if (!item.Exists || item.EntryCount <= 0 || !item.SupportsPreview)
            {
                throw new InvalidOperationException($"Qinger 6.6 image resource is not previewable: {requiredFile}, exists={item.Exists}, entries={item.EntryCount}");
            }
        }

        if (catalog.Any(item => item.FileName.Equals("Itemicon.dll", StringComparison.OrdinalIgnoreCase) ||
                                item.FileName.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Qinger 6.6 image catalog should not expose obsolete Itemicon.dll/Mgcicon.dll as active resources.");
        }

        AssertQinger66IconMapping(catalogService, catalog);
        AssertQinger66FieldIconPreview(project);
        var scenarioProbe = ProbeQinger66Scenarios(project, maxFiles: 10);

        var reportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports", "Qinger66ReadAudit");
        Directory.CreateDirectory(reportRoot);
        var reportPath = Path.Combine(reportRoot, "qinger-66-read-audit-" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".json");
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(new
            {
                Project = project.GameRoot,
                Engine = engine,
                Diagnostics = diagnostics,
                ItemAuditSummary = itemAudit.Summary,
                ItemAuditRows = itemAudit.Rows.Take(255),
                TableReads = tableReads,
                ImageResources = catalog
                    .Where(item => new[] { "Item.e5", "Mtem.e5", "DT.e5", "Fb.e5", "U_select.e5", "Pmap.e5" }
                        .Contains(item.FileName, StringComparer.OrdinalIgnoreCase))
                    .Select(item => new
                    {
                        item.FileName,
                        item.RelativePath,
                        item.Path,
                        item.SizeBytes,
                        item.EntryCount,
                        item.Status,
                        item.Usage,
                        item.SafetyNote
                    }),
                ScenarioProbe = scenarioProbe
            }, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"QINGER_66_READ_SMOKE_OK version={engine.VersionHint} lowWord={engine.VersionResourceLowWord} tables={diagnostics.TableStatusSummary.Total} native={diagnostics.TableStatusSummary.Native66} resources={diagnostics.RequiredResources.Count} scenarios={scenarioProbe.Readable}/{scenarioProbe.Total} report={reportPath}");
    }

    static void RunQinger66WriteSmoke()
    {
        var sourceProject = LoadQinger66Project();
        var sourceEngine = new CczEngineProfileService().Detect(sourceProject);
        AssertQinger66Engine(sourceEngine);

        var smokeRoot = CreateQinger66MinimalTestCopy(sourceProject);
        var project = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        if (!project.IsTestCopy)
        {
            throw new InvalidOperationException("Qinger 6.6 write smoke must run only against a test copy.");
        }

        var rawTables = new HexTableParser().Load(project.HexTableXmlPath);
        var tables = new Ccz66HexTableAugmentationService().AugmentForProject(project, rawTables);
        var tableWriterResult = RunQinger66NativeTableWriteRoundTrip(project, tables);
        var itemLayoutWriteResult = RunQinger66ItemLayoutWriteRoundTrip(project, tables);
        var itemNameWriteResult = RunQinger66ItemNameWriteRoundTrip(project, tables);
        var effectNameSlotWriteResult = RunQinger66EffectNameSlotWriteRoundTrip(project);
        var e5WriteResult = RunQinger66E5WriteRoundTrip(project);
        var fallbackStatus = AssertQinger66CrossVersionFallback(rawTables, project);
        var readOnlyStatus = AssertQinger66ReadOnlyEvidenceOnlyBlocked(project, tables);
        var obsoleteDllStatus = AssertQinger66ObsoleteDllBlocked(project);

        var reportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports", "Qinger66ReadAudit");
        Directory.CreateDirectory(reportRoot);
        var reportPath = Path.Combine(reportRoot, "qinger-66-write-smoke-" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".json");
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(new
            {
                SourceProject = sourceProject.GameRoot,
                TestCopy = project.GameRoot,
                NativeTableWrite = tableWriterResult,
                ItemLayoutWrite = itemLayoutWriteResult,
                ItemNameWrite = itemNameWriteResult,
                EffectNameSlotWrite = effectNameSlotWriteResult,
                E5Write = e5WriteResult,
                CrossVersionFallback = fallbackStatus,
                ReadOnlyEvidenceOnly = readOnlyStatus,
                ObsoleteDll = obsoleteDllStatus
            }, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"QINGER_66_WRITE_SMOKE_OK testCopy={project.GameRoot} tableChanged={tableWriterResult.ChangedBytes} effectNameSlotChanged={effectNameSlotWriteResult.ChangedBytes} e5Changed={e5WriteResult.ChangedBytesEstimate} report={reportPath}");
    }

    private static CczProject LoadQinger66Project()
    {
        var root = Environment.GetEnvironmentVariable("QINGER66_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Environment.CurrentDirectory, "基底", "清儿吕布传");
        }

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("Qinger 6.6 project was not found: " + root);
        }

        return new ProjectDetector().CreateProjectFromGameRoot(root);
    }

    private static void AssertQinger66Engine(CczEngineProfile engine)
    {
        if (!engine.VersionHint.Equals("6.6", StringComparison.OrdinalIgnoreCase) ||
            !engine.TableVersionPrefix.Equals("6.6", StringComparison.OrdinalIgnoreCase) ||
            engine.VersionResourceLowWord != 6)
        {
            throw new InvalidOperationException(
                $"Qinger engine detection failed: version={engine.VersionHint}, tablePrefix={engine.TableVersionPrefix}, lowWord={engine.VersionResourceLowWord}, source={engine.DetectionSource}");
        }

        if (engine.ExeSize != QingerExpectedExeSize)
        {
            throw new InvalidOperationException($"Qinger Ekd5.exe size changed unexpectedly: {engine.ExeSize}.");
        }

        if (!engine.DetectionSource.Contains("old-wrench FileVersionLS low word", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Qinger 6.6 must be selected from old-wrench low-word evidence, not EXE size fallback.");
        }
    }

    private static void AssertQinger66IconMapping(ImageResourceCatalogService catalogService, IReadOnlyList<ImageResourceFileInfo> catalog)
    {
        var item = catalog.Single(resource => resource.FileName.Equals("Item.e5", StringComparison.OrdinalIgnoreCase));
        var itemEntries = catalogService.ReadEntries(item).ToDictionary(entry => entry.ImageNumber);
        foreach (var fieldValue in new[] { 0, 1, 2, 21, 145, 156, 248, 255 })
        {
            var (small, large) = Ccz66RevisedLayout.ResolveItemIconImageNumbers(fieldValue);
            if (fieldValue == 0 && (small != 1 || large != 2))
            {
                throw new InvalidOperationException("Qinger 6.6 item icon field 0 must map to Item.e5 #1/#2.");
            }

            if (fieldValue == 1 && (small != 3 || large != 4))
            {
                throw new InvalidOperationException("Qinger 6.6 item icon field 1 must map to Item.e5 #3/#4.");
            }

            if (!itemEntries.ContainsKey(small) || !itemEntries.ContainsKey(large))
            {
                throw new InvalidOperationException($"Qinger 6.6 item icon mapping is out of range: field={fieldValue}, small=#{small}, large=#{large}.");
            }

            if (!itemEntries[small].Usage.Contains($"field value {fieldValue}", StringComparison.OrdinalIgnoreCase) ||
                !itemEntries[large].Usage.Contains($"field value {fieldValue}", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Qinger 6.6 item icon usage annotation mismatch for field {fieldValue}.");
            }
        }

        var strategy = catalog.Single(resource => resource.FileName.Equals("Mtem.e5", StringComparison.OrdinalIgnoreCase));
        var strategyEntries = catalogService.ReadEntries(strategy).ToDictionary(entry => entry.ImageNumber);
        foreach (var fieldValue in new[] { 0, 1, 2 })
        {
            var imageNumber = Ccz66RevisedLayout.ResolveStrategyIconImageNumber(fieldValue);
            if (!strategyEntries.TryGetValue(imageNumber, out var entry) ||
                !entry.Usage.Contains($"field value {fieldValue}", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Qinger 6.6 strategy icon mapping mismatch: field={fieldValue}, image=#{imageNumber}.");
            }
        }
    }

    private static void AssertQinger66ItemAudit(
        CczProject project,
        Qinger66ItemAuditSummary summary,
        IReadOnlyList<Qinger66ItemAuditRow> rows)
    {
        if (!summary.Applies)
        {
            throw new InvalidOperationException("Qinger 6.6 item audit did not apply.");
        }

        if (summary.ItemRowCount != 255)
        {
            throw new InvalidOperationException($"Qinger 6.6 item audit expected 255 item rows, got {summary.ItemRowCount}.");
        }

        if (summary.NameControlCharacterRowCount != 0 ||
            rows.Any(row => row.DisplayName.Any(ch => char.IsControl(ch))))
        {
            throw new InvalidOperationException("Qinger 6.6 item names still contain visible control characters.");
        }

        if (summary.MinIconField != 0 || summary.MaxIconField != 255)
        {
            throw new InvalidOperationException($"Qinger 6.6 item icon field range mismatch: {summary.MinIconField}..{summary.MaxIconField}.");
        }

        if (summary.ItemE5EntryCount != 535 || !summary.ItemIconRangeCovered || summary.MaxRequiredImageNumber != 512)
        {
            throw new InvalidOperationException($"Qinger 6.6 Item.e5 coverage mismatch: entries={summary.ItemE5EntryCount}, required={summary.MaxRequiredImageNumber}, covered={summary.ItemIconRangeCovered}.");
        }

        AssertQinger66ItemAuditRow(rows, 0, "短剑", 1, 255, 255);
        AssertQinger66ItemAuditRow(rows, 1, "大剑", 2, 255, 255);
        AssertQinger66ItemAuditRow(rows, 2, "钢剑", 3, 255, 255);
        AssertQinger66ItemAuditRow(rows, 36, "方天画戟", 248, 0x65, 0x65);
        AssertQinger66ItemAuditRow(rows, 51, "芭蕉扇", 232, 0x3A, 0x3A);
        AssertQinger66ItemAuditRow(rows, 148, "墨麒麟", 231, 0x02, 0x6D);
        AssertQinger66ItemAuditRow(rows, 159, "筋斗云", 255, 0x02, 0x28);

        AssertQinger66AuxEffect(rows, 109, 0x3D, "盾反");
        AssertQinger66AuxEffect(rows, 111, 0x3F, "辅助全防御");
        AssertQinger66AuxEffect(rows, 112, 0x53, "策略绝对命中");
        AssertQinger66AuxEffect(rows, 140, 0x5C, "神魔附体");
        AssertQinger66AuxEffect(rows, 160, 0x27, "辅助获得Exp");

        if (!summary.EffectResolutionSourceCounts.ContainsKey(Ccz66ItemEffectNameService.SourceName) &&
            !summary.EffectResolutionSourceCounts.ContainsKey(Ccz66ItemEffectNameService.SlotFallbackSourceName) &&
            !summary.EffectResolutionSourceCounts.ContainsKey("ProjectCatalogOverride"))
        {
            throw new InvalidOperationException("Qinger 6.6 item audit did not resolve any equipment effect names from verified sources.");
        }
    }

    private static void AssertQinger66ItemAuditRow(
        IReadOnlyList<Qinger66ItemAuditRow> rows,
        int itemId,
        string expectedName,
        int expectedIcon,
        int expectedRawEffect,
        int expectedEffectiveEffect)
    {
        var row = rows.FirstOrDefault(item => item.ItemId == itemId)
            ?? throw new InvalidOperationException($"Qinger 6.6 item audit missing ID={itemId}.");
        if (!row.DisplayName.Equals(expectedName, StringComparison.Ordinal) ||
            row.IconField != expectedIcon ||
            row.RawEffectId != expectedRawEffect ||
            row.EffectiveEffectId != expectedEffectiveEffect)
        {
            throw new InvalidOperationException(
                $"Qinger 6.6 item audit row mismatch ID={itemId}: name={row.DisplayName}, icon={row.IconField}, raw={row.RawEffectId:X2}, effective={row.EffectiveEffectId:X2}.");
        }
    }

    private static void AssertQinger66AuxEffect(IReadOnlyList<Qinger66ItemAuditRow> rows, int itemId, int expectedEffectId, string expectedName)
    {
        var row = rows.FirstOrDefault(item => item.ItemId == itemId)
            ?? throw new InvalidOperationException($"Qinger 6.6 accessory row missing ID={itemId}.");
        if (row.RawEffectId is not (2 or 3) ||
            row.EffectiveEffectId != expectedEffectId ||
            !row.Warnings.Any(warning => warning.Contains("row+0x12", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Qinger 6.6 accessory effect mapping failed ID={itemId}: raw={row.RawEffectId:X2}, effective={row.EffectiveEffectId:X2}.");
        }

        var resolver = new ItemEffectResolutionService();
        var project = LoadQinger66Project();
        var tables = new Ccz66HexTableAugmentationService().LoadForProject(project, new HexTableParser());
        var resolved = resolver.Resolve(project, tables, row.RawEffectId == 3 ? "道具/消耗品" : "辅助装备", row.EffectiveEffectId ?? 0, row.RawEffectId);
        if (!resolved.DisplayName.Contains(expectedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Qinger 6.6 accessory effect name mismatch ID={itemId}: {resolved.DisplayName}");
        }
    }

    private static void AssertQinger66ItemEffectNames(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var service = new Ccz66ItemEffectNameService();
        var slots = service.ReadSlots(project).ToDictionary(slot => slot.SlotIndex);
        foreach (var (slot, expected) in new[]
                 {
                     (0, "每回合恢复HP"),
                     (28, "辅助策略命中"),
                     (56, "二次行动"),
                     (72, "强化反击")
                 })
        {
            if (!slots.TryGetValue(slot, out var actual) ||
                !string.Equals(actual.Name, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Qinger 6.6 effect name slot sentinel mismatch: slot={slot}, expected={expected}, actual={actual?.Name ?? "<missing>"}.");
            }
        }

        var catalog = service.BuildCatalogEntries(project);
        foreach (var duplicateId in new[] { 0x1A, 0x1B, 0x1E, 0x1F, 0x29, 0x31, 0x35, 0x41, 0x4D, 0x52, 0x53, 0x57, 0x59, 0x65 })
        {
            var count = catalog.Count(entry => entry.EffectId == duplicateId);
            if (count < 2)
            {
                throw new InvalidOperationException($"Qinger 6.6 effect catalog lost duplicate ID {HexDisplayFormatter.Format(duplicateId, 2)}; count={count}.");
            }
        }

        var resolver = new ItemEffectResolutionService();
        var resolved1A = resolver.Resolve(project, tables, "武器", 0, 0x1A);
        if (!resolved1A.Source.Equals(Ccz66ItemEffectNameService.SourceName, StringComparison.Ordinal) ||
            !resolved1A.DisplayName.Contains("每回合恢复HP", StringComparison.Ordinal) ||
            !resolved1A.DisplayName.Contains("每回合恢复状态", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Qinger 6.6 effect 1A resolved incorrectly: source={resolved1A.Source}, name={resolved1A.DisplayName}.");
        }

        var resolved90 = resolver.Resolve(project, tables, "武器", 0, 0x90);
        if (!resolved90.DisplayName.Contains("强化反击", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Qinger 6.6 effect 90 must resolve to 强化反击.");
        }

        var resolved52 = resolver.Resolve(project, tables, "武器", 0, 0x52);
        if (!resolved52.DisplayName.Contains("健康光环", StringComparison.Ordinal) ||
            !resolved52.DisplayName.Contains("生命光环", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Qinger 6.6 effect 52 must use image-assigner display names: {resolved52.DisplayName}.");
        }

        foreach (var (effectId, expectedName) in new[]
                 {
                     (0x2B, "策略无视地形"),
                     (0x2D, "策略模仿"),
                     (0x2E, "节约MP"),
                     (0x38, "二次行动"),
                     (0x39, "唯我独尊"),
                     (0x3E, "强化地形"),
                     (0x49, "辅助攻击力"),
                     (0x50, "学会策略 四神"),
                     (0x5D, "攻击追加"),
                     (0x5F, "策略反击"),
                     (0x67, "回复周围SP"),
                     (0x6A, "以柔克刚"),
                     (0x6E, "斩杀攻击")
                 })
        {
            var resolved = resolver.Resolve(project, tables, "武器", 0, effectId);
            if (!resolved.Source.Equals(Ccz66ItemEffectNameService.SlotFallbackSourceName, StringComparison.Ordinal) ||
                !resolved.DisplayName.Contains(expectedName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Qinger 6.6 slot fallback failed for {HexDisplayFormatter.Format(effectId, 2)}: source={resolved.Source}, name={resolved.DisplayName}.");
            }
        }

        foreach (var table in tables.Where(ItemEffectNameReader.IsItemEffectNameTable))
        {
            var validation = new HexTableReader().Validate(project, table);
            if (table.Version.Equals("6.6", StringComparison.OrdinalIgnoreCase) &&
                !validation.SemanticValidationStatus.Equals("Obsolete65NameBlockIn66ReadOnly", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Qinger 6.6 old effect-name HexTable was not marked obsolete evidence: {table.TableName}, semantic={validation.SemanticValidationStatus}.");
            }
        }
    }

    private static void AssertQinger66FieldIconPreview(CczProject project)
    {
        var previewer = new ItemIconPreviewService();
        foreach (var fieldValue in new[] { 0, 1, 21, 145, 156, 248, 255 })
        {
            var preview = previewer.BuildPreview(project, fieldValue);
            if (preview.Bitmap == null ||
                !preview.SourcePath.EndsWith(Path.Combine("E5", "Item.e5"), StringComparison.OrdinalIgnoreCase) ||
                preview.LargeVariant == null)
            {
                throw new InvalidOperationException($"Qinger 6.6 item icon preview failed for field {fieldValue}: source={preview.SourcePath}, message={preview.Message}");
            }
        }
    }

    private static QingerScenarioProbeResult ProbeQinger66Scenarios(CczProject project, int maxFiles)
    {
        var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
        var sceneDoc = File.Exists(sceneStringPath)
            ? new SceneStringParser().Parse(sceneStringPath)
            : new SceneStringDocument { SourcePath = sceneStringPath };
        var scenarios = new ScenarioFileReader()
            .ReadAllIndex(project)
            .Where(scenario => ScenarioFileReader.IsRsScriptFile(scenario.FileName))
            .Take(maxFiles)
            .ToList();
        if (scenarios.Count == 0)
        {
            throw new InvalidOperationException("Qinger 6.6 scenario smoke found no R/S eex files.");
        }

        var reader = new LegacyScenarioReader();
        var files = new List<object>();
        var readable = 0;
        long commandCount = 0;
        foreach (var scenario in scenarios)
        {
            try
            {
                var document = reader.Read(scenario.Path, sceneDoc);
                var analysis = AnalyzeLegacyScenarioDepth(document);
                readable++;
                commandCount += analysis.CommandCount;
                files.Add(new
                {
                    scenario.FileName,
                    scenario.Kind,
                    scenario.Length,
                    Readable = true,
                    analysis.CommandCount,
                    analysis.MaxDepth
                });
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
            {
                files.Add(new
                {
                    scenario.FileName,
                    scenario.Kind,
                    scenario.Length,
                    Readable = false,
                    Error = ex.Message
                });
            }
        }

        if (readable == 0)
        {
            throw new InvalidOperationException("Qinger 6.6 scenario smoke could not parse any sampled R/S script.");
        }

        return new QingerScenarioProbeResult(scenarios.Count, readable, commandCount, files);
    }

    private static string CreateQinger66MinimalTestCopy(CczProject sourceProject)
    {
        var smokeRoot = Path.Combine(
            sourceProject.WorkspaceRoot,
            "CCZModStudio_TestCopies",
            "Qinger66WriteSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);

        foreach (var file in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5" })
        {
            var source = sourceProject.ResolveGameFile(file);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, file), overwrite: false);
            }
        }

        var e5Root = Path.Combine(smokeRoot, "E5");
        Directory.CreateDirectory(e5Root);
        foreach (var relative in Ccz66RevisedLayout.RequiredE5Resources)
        {
            var source = Ccz66RevisedLayout.ResolveResourcePath(sourceProject, relative);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Qinger 6.6 write smoke requires resource " + relative, source);
            }

            File.Copy(source, Path.Combine(e5Root, Path.GetFileName(source)), overwrite: false);
        }

        var rsSource = sourceProject.ResolveGameFile("RS");
        if (Directory.Exists(rsSource))
        {
            var rsTarget = Path.Combine(smokeRoot, "RS");
            Directory.CreateDirectory(rsTarget);
            foreach (var scenario in Directory.GetFiles(rsSource, "*.eex", SearchOption.TopDirectoryOnly)
                         .Where(path => ScenarioFileReader.IsRsScriptFile(Path.GetFileName(path)))
                         .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                         .Take(4))
            {
                File.Copy(scenario, Path.Combine(rsTarget, Path.GetFileName(scenario)), overwrite: false);
            }
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={sourceProject.GameRoot}\r\nPurpose=Qinger 6.6 write smoke\r\n");
        return smokeRoot;
    }

    private static TableSaveResult RunQinger66NativeTableWriteRoundTrip(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var table = tables
            .Where(table => table.Version.Equals("6.6", StringComparison.OrdinalIgnoreCase))
            .Select(table => new { Table = table, Validation = new HexTableReader().Validate(project, table) })
            .Where(item => item.Validation.IsNative66 && item.Validation.CanWrite)
            .OrderBy(item => item.Table.Id)
            .Select(item => item.Table)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Qinger 6.6 write smoke found no Native66 writable table.");

        var reader = new HexTableReader();
        var read = reader.Read(project, table, tables);
        var data = read.Data;
        var writableColumn = FindFirstWritableIntegerColumn(table, data)
            ?? throw new InvalidOperationException("Qinger 6.6 native table has no reversible UInt8/UInt16/UInt32 field for write smoke: " + table.TableName);
        var row = data.Rows[0];
        var original = Convert.ToInt64(row[writableColumn.Column], CultureInfo.InvariantCulture);
        var next = writableColumn.MaxValue == 0
            ? 0
            : original < writableColumn.MaxValue ? original + 1 : original - 1;
        if (next == original)
        {
            throw new InvalidOperationException("Qinger 6.6 native table write smoke could not produce a changed value.");
        }

        row[writableColumn.Column] = next.ToString(CultureInfo.InvariantCulture);
        var changed = new HexTableWriter().SaveToTestCopy(project, table, data);
        if (changed.ChangedBytes <= 0 ||
            !changed.TableStatus.Equals(Ccz66RevisedLayout.Native66TableStatus, StringComparison.OrdinalIgnoreCase) ||
            !changed.WriteMode.Equals("Direct", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Qinger 6.6 native table write did not report a direct byte change: changed={changed.ChangedBytes}, status={changed.TableStatus}, mode={changed.WriteMode}");
        }

        var verify = reader.Read(project, table, tables);
        var actual = Convert.ToInt64(verify.Data.Rows[0][writableColumn.Column], CultureInfo.InvariantCulture);
        if (actual != next)
        {
            throw new InvalidOperationException($"Qinger 6.6 native table reread mismatch: expected={next}, actual={actual}");
        }

        verify.Data.Rows[0][writableColumn.Column] = original.ToString(CultureInfo.InvariantCulture);
        var restored = new HexTableWriter().SaveToTestCopy(project, table, verify.Data);
        if (restored.ChangedBytes <= 0)
        {
            throw new InvalidOperationException("Qinger 6.6 native table restore did not change bytes.");
        }

        return changed;
    }

    private static object RunQinger66ItemNameWriteRoundTrip(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var table = HexTableNameResolver.ResolveItemTables(project, tables)
            .FirstOrDefault(table => table.Version.Equals("6.6", StringComparison.OrdinalIgnoreCase) &&
                                     Ccz66ItemNameEncodingService.IsItemBaseTable(table))
            ?? throw new InvalidOperationException("Qinger 6.6 item name write smoke found no 6.6 item table.");
        var nameField = table.Fields.FirstOrDefault(field => Ccz66ItemNameEncodingService.IsNameColumn(field.ColumnName) && field.Size == Ccz66ItemLayoutService.NameSize)
            ?? throw new InvalidOperationException("Qinger 6.6 item table has no 15-byte name field.");

        var reader = new HexTableReader();
        var read = reader.Read(project, table, tables);
        var row = read.Data.Rows.Cast<DataRow>()
            .FirstOrDefault(row =>
            {
                var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                var raw = ReadRawFieldBytes(project, table, id, nameField);
                var capacity = GetVisibleNameCapacity(raw);
                return capacity >= EncodingService.GetGbkByteCount("测试");
            })
            ?? throw new InvalidOperationException("Qinger 6.6 item name write smoke found no row with visible capacity.");
        var rowId = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
        var originalName = Convert.ToString(row[nameField.ColumnName], CultureInfo.InvariantCulture) ?? string.Empty;
        var originalRowBytes = ReadRawRowBytes(project, table, rowId);
        var originalBytes = ReadRawFieldBytes(project, table, rowId, nameField);
        var testName = BuildSameCapacityTestName(originalBytes);

        row[nameField.ColumnName] = testName;
        var changed = new HexTableWriter().SaveToTestCopy(project, table, read.Data);
        var changedRowBytes = ReadRawRowBytes(project, table, rowId);
        if (changedRowBytes[Ccz66ItemLayoutService.IconOffset] != originalRowBytes[Ccz66ItemLayoutService.IconOffset] ||
            changedRowBytes[Ccz66ItemLayoutService.Reserved10Offset] != originalRowBytes[Ccz66ItemLayoutService.Reserved10Offset] ||
            changedRowBytes[Ccz66ItemLayoutService.TypeOffset] != originalRowBytes[Ccz66ItemLayoutService.TypeOffset] ||
            changedRowBytes[Ccz66ItemLayoutService.RawEffectMarkerOffset] != originalRowBytes[Ccz66ItemLayoutService.RawEffectMarkerOffset])
        {
            throw new InvalidOperationException("Qinger 6.6 item name write touched icon/reserved/effect bytes outside row+0x00..0x0E.");
        }

        var reread = reader.Read(project, table, tables);
        var verifyRow = reread.Data.Rows.Cast<DataRow>().First(item => Convert.ToInt32(item["ID"], CultureInfo.InvariantCulture) == rowId);
        var rereadName = Convert.ToString(verifyRow[nameField.ColumnName], CultureInfo.InvariantCulture) ?? string.Empty;
        if (!rereadName.Equals(testName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Qinger 6.6 item name reread mismatch: expected={testName}, actual={rereadName}.");
        }

        verifyRow[nameField.ColumnName] = originalName;
        var restored = new HexTableWriter().SaveToTestCopy(project, table, reread.Data);
        var restoredRowBytes = ReadRawRowBytes(project, table, rowId);
        if (!restoredRowBytes.SequenceEqual(originalRowBytes))
        {
            throw new InvalidOperationException("Qinger 6.6 item name restore did not reproduce original bytes.");
        }

        return new
        {
            table.TableName,
            RowId = rowId,
            OriginalName = originalName,
            TestName = testName,
            changed.ChangedBytes,
            RestoreChangedBytes = restored.ChangedBytes,
            NameFieldBytes = Ccz66ItemLayoutService.NameSize,
            PreservedOffsets = "row+0x0F..0x18"
        };
    }

    private static object RunQinger66ItemLayoutWriteRoundTrip(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var itemTables = HexTableNameResolver.ResolveItemTables(project, tables)
            .Where(table => table.Version.Equals("6.6", StringComparison.OrdinalIgnoreCase) &&
                            Ccz66ItemNameEncodingService.IsItemBaseTable(table))
            .ToArray();
        var lowTable = itemTables.FirstOrDefault(table => table.BeginId == 0)
            ?? throw new InvalidOperationException("Qinger 6.6 item layout smoke missing low item table.");
        var highTable = itemTables.FirstOrDefault(table => table.BeginId == 104)
            ?? throw new InvalidOperationException("Qinger 6.6 item layout smoke missing high item table.");

        var reader = new HexTableReader();
        var lowRead = reader.Read(project, lowTable, tables);
        var id1 = lowRead.Data.Rows.Cast<DataRow>().First(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == 1);
        var id1OriginalRow = ReadRawRowBytes(project, lowTable, 1);
        var id1OriginalIcon = Convert.ToInt32(id1["图标"], CultureInfo.InvariantCulture);
        id1["图标"] = id1OriginalIcon == 2 ? 3 : 2;
        var iconChanged = new HexTableWriter().SaveToTestCopy(project, lowTable, lowRead.Data);
        var id1ChangedRow = ReadRawRowBytes(project, lowTable, 1);
        if (id1ChangedRow[Ccz66ItemLayoutService.IconOffset] == id1OriginalRow[Ccz66ItemLayoutService.IconOffset] ||
            id1ChangedRow[Ccz66ItemLayoutService.LegacyIconOrReserved14Offset] != id1OriginalRow[Ccz66ItemLayoutService.LegacyIconOrReserved14Offset])
        {
            throw new InvalidOperationException("Qinger 6.6 icon write did not target row+0x0F or touched old row+0x14.");
        }

        var rereadLow = reader.Read(project, lowTable, tables);
        var verifyId1 = rereadLow.Data.Rows.Cast<DataRow>().First(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == 1);
        verifyId1["图标"] = id1OriginalIcon;
        new HexTableWriter().SaveToTestCopy(project, lowTable, rereadLow.Data);
        if (!ReadRawRowBytes(project, lowTable, 1).SequenceEqual(id1OriginalRow))
        {
            throw new InvalidOperationException("Qinger 6.6 icon write restore failed.");
        }

        var highRead = reader.Read(project, highTable, tables);
        var id109 = highRead.Data.Rows.Cast<DataRow>().First(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == 109);
        var id109OriginalRow = ReadRawRowBytes(project, highTable, 109);
        var originalEffect = Convert.ToInt32(id109["装备特效号"], CultureInfo.InvariantCulture);
        var testEffect = originalEffect == 0x3D ? 0x3F : 0x3D;
        id109["装备特效号"] = testEffect;
        var effectChanged = new HexTableWriter().SaveToTestCopy(project, highTable, highRead.Data);
        var id109ChangedRow = ReadRawRowBytes(project, highTable, 109);
        if (id109ChangedRow[Ccz66ItemLayoutService.TypeOffset] != testEffect ||
            id109ChangedRow[Ccz66ItemLayoutService.RawEffectMarkerOffset] != 2)
        {
            throw new InvalidOperationException(
                $"Qinger 6.6 accessory effect write failed: row+0x11={id109ChangedRow[Ccz66ItemLayoutService.TypeOffset]:X2}, row+0x12={id109ChangedRow[Ccz66ItemLayoutService.RawEffectMarkerOffset]:X2}.");
        }

        var rereadHigh = reader.Read(project, highTable, tables);
        var verifyId109 = rereadHigh.Data.Rows.Cast<DataRow>().First(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == 109);
        if (Convert.ToInt32(verifyId109["装备特效号"], CultureInfo.InvariantCulture) != testEffect ||
            Convert.ToInt32(verifyId109["类型"], CultureInfo.InvariantCulture) != testEffect)
        {
            throw new InvalidOperationException("Qinger 6.6 accessory effect reread did not expose row+0x11 as visible effect/type.");
        }

        verifyId109["装备特效号"] = originalEffect;
        new HexTableWriter().SaveToTestCopy(project, highTable, rereadHigh.Data);
        if (!ReadRawRowBytes(project, highTable, 109).SequenceEqual(id109OriginalRow))
        {
            throw new InvalidOperationException("Qinger 6.6 accessory effect write restore failed.");
        }

        return new
        {
            IconWrite = new
            {
                RowId = 1,
                Offset = "row+0x0F",
                Old = id1OriginalIcon,
                New = Convert.ToInt32(id1["图标"], CultureInfo.InvariantCulture),
                iconChanged.ChangedBytes
            },
            AccessoryEffectWrite = new
            {
                RowId = 109,
                Offset = "row+0x11",
                MarkerOffset = "row+0x12",
                Marker = 2,
                Old = originalEffect,
                New = testEffect,
                effectChanged.ChangedBytes
            }
        };
    }

    private static Ccz66ItemEffectNameWriteResult RunQinger66EffectNameSlotWriteRoundTrip(CczProject project)
    {
        var service = new Ccz66ItemEffectNameService();
        var slotIndex = 72;
        var slotsBefore = service.ReadSlots(project).ToDictionary(slot => slot.SlotIndex);
        var original = slotsBefore[slotIndex].Name;
        var testName = original.Equals("强化反击", StringComparison.Ordinal) ? "强化返击" : "强化反击";
        var changed = service.WriteSlot(project, slotIndex, testName);
        var rereadChanged = service.ReadSlots(project).ToDictionary(slot => slot.SlotIndex)[slotIndex].Name;
        if (!string.Equals(rereadChanged, testName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Qinger 6.6 effect name slot write reread mismatch: expected={testName}, actual={rereadChanged}.");
        }

        var restored = service.WriteSlot(project, slotIndex, original);
        var rereadRestored = service.ReadSlots(project).ToDictionary(slot => slot.SlotIndex)[slotIndex].Name;
        if (!string.Equals(rereadRestored, original, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Qinger 6.6 effect name slot restore mismatch: expected={original}, actual={rereadRestored}.");
        }

        try
        {
            service.WriteSlot(project, slotIndex, "这个名字肯定超过十六字节");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("exceeds slot capacity", StringComparison.OrdinalIgnoreCase))
        {
            return changed;
        }

        throw new InvalidOperationException("Qinger 6.6 effect name slot writer did not reject an overlong GBK name.");
    }

    private static QingerWritableColumn? FindFirstWritableIntegerColumn(HexTableDefinition table, DataTable data)
    {
        for (var columnIndex = 1; columnIndex < data.Columns.Count; columnIndex++)
        {
            var column = data.Columns[columnIndex];
            if (column.ExtendedProperties["FieldDefinition"] is not HexFieldDefinition field ||
                !field.ConsumesBytes ||
                field.Kind is not (HexFieldKind.UInt8 or HexFieldKind.UInt16 or HexFieldKind.UInt32))
            {
                continue;
            }

            if (!long.TryParse(Convert.ToString(data.Rows[0][column], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            var max = field.Kind switch
            {
                HexFieldKind.UInt8 => (long)byte.MaxValue,
                HexFieldKind.UInt16 => (long)ushort.MaxValue,
                HexFieldKind.UInt32 => (long)uint.MaxValue,
                _ => 0L
            };
            return new QingerWritableColumn(column.ColumnName, max);
        }

        return null;
    }

    private static E5ImageReplaceResult RunQinger66E5WriteRoundTrip(CczProject project)
    {
        var itemPath = Ccz66RevisedLayout.ResolveResourcePath(project, "E5\\Item.e5");
        var service = new E5ImageReplaceService();
        var beforeBytes = File.ReadAllBytes(itemPath);
        var sourceImageNumber = 4;
        var targetImageNumber = 2;
        var result = service.ReplaceFromEntry(project, itemPath, targetImageNumber, itemPath, sourceImageNumber);
        if (result.ChangedBytesEstimate <= 0 || string.IsNullOrWhiteSpace(result.BackupPath) || string.IsNullOrWhiteSpace(result.ReportJsonPath))
        {
            throw new InvalidOperationException("Qinger 6.6 E5 write smoke did not produce backup/report/change evidence.");
        }

        var afterSource = service.ReadEntryBytes(itemPath, sourceImageNumber);
        var afterTarget = service.ReadEntryBytes(itemPath, targetImageNumber);
        if (!afterTarget.SequenceEqual(afterSource))
        {
            throw new InvalidOperationException("Qinger 6.6 E5 write smoke reread mismatch after replacement.");
        }

        File.WriteAllBytes(itemPath, beforeBytes);
        var restoredTarget = service.ReadEntryBytes(itemPath, targetImageNumber);
        var originalTarget = new E5ImageReplaceService().ReadEntryBytes(result.BackupPath, targetImageNumber);
        if (!restoredTarget.SequenceEqual(originalTarget))
        {
            throw new InvalidOperationException("Qinger 6.6 E5 write smoke restore mismatch.");
        }

        return result;
    }

    private static object AssertQinger66CrossVersionFallback(IReadOnlyList<HexTableDefinition> rawTables, CczProject project)
    {
        var fallback = rawTables.FirstOrDefault(table => table.Version.Equals("6.5", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Qinger 6.6 fallback validation requires a raw 6.5 table.");
        var validation = new HexTableReader().Validate(project, fallback);
        if (!validation.IsCrossVersionFallback ||
            validation.CanWrite ||
            !validation.TableStatus.Equals(Ccz66RevisedLayout.CrossVersionFallbackTableStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Qinger 6.6 raw 6.5 table did not validate as blocked fallback: status={validation.TableStatus}, canWrite={validation.CanWrite}");
        }

        return new
        {
            fallback.TableName,
            validation.TableStatus,
            validation.WriteRisk,
            validation.CanWrite
        };
    }

    private static object AssertQinger66ReadOnlyEvidenceOnlyBlocked(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var table = tables.FirstOrDefault(item => item.Version.Equals("6.6", StringComparison.OrdinalIgnoreCase) && item.IsEvidenceReadOnlyTable);
        if (table == null)
        {
            return new
            {
                Status = "NoReadOnlyEvidenceOnlyTableInCurrentSample",
                Note = "All generated 6.6 compatibility tables fit the local file bounds in this sample; refusal is still enforced by HexTableWriter when such a table is present."
            };
        }

        var read = new HexTableReader().Read(project, table, tables);
        try
        {
            new HexTableWriter().SaveToTestCopy(project, table, read.Data);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ReadOnlyEvidenceOnly", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                Status = "Blocked",
                table.TableName,
                read.Validation.TableStatus,
                Error = ex.Message
            };
        }

        throw new InvalidOperationException("Qinger 6.6 ReadOnlyEvidenceOnly table write was not blocked.");
    }

    private static object AssertQinger66ObsoleteDllBlocked(CczProject project)
    {
        foreach (var obsolete in new[] { "Itemicon.dll", "Mgcicon.dll" })
        {
            if (!ItemIconMappingService.IsObsolete66DllIconResource(project, obsolete))
            {
                throw new InvalidOperationException("Qinger 6.6 obsolete DLL icon resource was not classified as obsolete: " + obsolete);
            }

            var message = ItemIconMappingService.BuildObsolete66DllIconMessage(project, obsolete, 0);
            if (!message.Contains("ObsoleteRuntimeResource", StringComparison.OrdinalIgnoreCase) ||
                !message.Contains("E5/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Qinger 6.6 obsolete DLL icon message is incomplete: " + message);
            }
        }

        return new { Status = "Blocked", Resources = new[] { "Itemicon.dll", "Mgcicon.dll" } };
    }

    private static byte[] ReadRawFieldBytes(CczProject project, HexTableDefinition table, int rowId, HexFieldDefinition field)
    {
        var fieldOffset = 0;
        foreach (var current in table.Fields)
        {
            if (ReferenceEquals(current, field)) break;
            if (current.ConsumesBytes) fieldOffset += current.Size;
        }

        var rowIndex = rowId - table.BeginId;
        var filePath = project.ResolveGameFile(table.FileName);
        var bytes = File.ReadAllBytes(filePath);
        var offset = checked((int)(table.DataPos + rowIndex * (long)table.RowSize + fieldOffset));
        var result = new byte[field.Size];
        Buffer.BlockCopy(bytes, offset, result, 0, result.Length);
        return result;
    }

    private static byte[] ReadRawRowBytes(CczProject project, HexTableDefinition table, int rowId)
    {
        var rowIndex = rowId - table.BeginId;
        var filePath = project.ResolveGameFile(table.FileName);
        var bytes = File.ReadAllBytes(filePath);
        var offset = checked((int)(table.DataPos + rowIndex * (long)table.RowSize));
        var result = new byte[table.RowSize];
        Buffer.BlockCopy(bytes, offset, result, 0, result.Length);
        return result;
    }

    private static byte[] GetTailAfterFirstNul(byte[] bytes)
    {
        var nul = Array.IndexOf(bytes, (byte)0x00);
        if (nul < 0 || nul + 1 >= bytes.Length) return Array.Empty<byte>();
        var tail = new byte[bytes.Length - nul - 1];
        Buffer.BlockCopy(bytes, nul + 1, tail, 0, tail.Length);
        return tail;
    }

    private static string BuildSameCapacityTestName(byte[] nameBytes)
    {
        var capacity = GetVisibleNameCapacity(nameBytes);
        return capacity >= 4 ? "测试" : "A";
    }

    private static int GetVisibleNameCapacity(byte[] nameBytes)
    {
        var nul = Array.IndexOf(nameBytes, (byte)0x00);
        return nul > 0 ? nul : nameBytes.Length;
    }

    private sealed record QingerScenarioProbeResult(int Total, int Readable, long CommandCount, IReadOnlyList<object> Files);

    private sealed record QingerWritableColumn(string Column, long MaxValue);
}
