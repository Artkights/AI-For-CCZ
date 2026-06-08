using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

internal partial class Program
{
    static void RunRsSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var rTable = tables.Single(t => t.TableName == "6.5-0-4 R形象");
        var sTable = tables.Single(t => t.TableName == "6.5-0-5 S形象");
        Console.WriteLine($"RS_TABLE R={rTable.FileName}:0x{rTable.DataPos:X} S={sTable.FileName}:0x{sTable.DataPos:X}");
        if (!rTable.FileName.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ||
            !sTable.FileName.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ||
            rTable.DataPos != 0xE1000 ||
            sTable.DataPos != 0xD2800)
        {
            throw new InvalidOperationException("人物 R/S 表偏移与 B形象指定器 6.5 System.ini 不一致。");
        }
    
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var scenarioIndex = new ScenarioFileReader().ReadAllIndex(project);
        stopwatch.Stop();
        var e5sCount = scenarioIndex.Count(x => x.FileName.EndsWith(".E5S", StringComparison.OrdinalIgnoreCase));
        var rCount = scenarioIndex.Count(x => x.FileName.StartsWith("R_", StringComparison.OrdinalIgnoreCase));
        var sCount = scenarioIndex.Count(x => x.FileName.StartsWith("S_", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"RS_SCENARIO_INDEX count={scenarioIndex.Count} R={rCount} S={sCount} E5S={e5sCount} elapsedMs={stopwatch.ElapsedMilliseconds}");
        if (scenarioIndex.Count == 0 || rCount == 0 || sCount == 0 || e5sCount != 0 ||
            scenarioIndex.Any(x => !ScenarioFileReader.IsRsScriptFile(x.FileName)))
        {
            throw new InvalidOperationException("R/S eex 剧本索引结果不符合预期，不能混入 E5S。");
        }
    
        var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (File.Exists(sceneStringPath))
        {
            var sceneDoc = new SceneStringParser().Parse(sceneStringPath);
            var firstScenario = scenarioIndex.First();
            var commandRows = new ScenarioCommandProbeReader().Probe(firstScenario.Path, sceneDoc, maxRows: 40);
            var textRows = new ScenarioTextReader().Read(firstScenario.Path, maxItems: 40);
            Console.WriteLine($"RS_SCENARIO_DETAIL file={firstScenario.FileName} commands={commandRows.Count} texts={textRows.Count} kind={firstScenario.Kind}");
            var scriptStructure = new ScenarioStructureProbeReader().Build(firstScenario.Path, sceneDoc, maxCommandRows: 600, project: project, tables: tables);
            var scriptTexts = new ScenarioTextReader().Read(firstScenario.Path).ToList();
            var legacyDocument = new LegacyScenarioReader().Read(firstScenario.Path, sceneDoc);
            if (legacyDocument.SceneCount == 0 ||
                legacyDocument.SectionCount == 0 ||
                legacyDocument.CommandCount == 0 ||
                !legacyDocument.EnumerateCommands().Any(command => command.StartsBodyBlock) ||
                legacyDocument.EnumerateCommands().Any(command => command.CommandId == 0x76 && command.OriginalJumpDisplacement == null))
            {
                throw new InvalidOperationException("Legacy R/S eex scenario tree read result is incomplete.");
            }
            Console.WriteLine($"LEGACY_SCENARIO_READ file={firstScenario.FileName} scenes={legacyDocument.SceneCount} sections={legacyDocument.SectionCount} commands={legacyDocument.CommandCount} jumps={legacyDocument.EnumerateCommands().Count(command => command.CommandId == 0x76)} texts={legacyDocument.EnumerateCommands().SelectMany(command => command.TextParameters).Count()}");
            var scriptTreeSummary = BuildScriptEditorTreeSummary(scriptStructure, scriptTexts);
            if (scriptTreeSummary.SectionTextGroupCount == 0 || scriptTreeSummary.AttachedTextNodeCount == 0)
            {
                throw new InvalidOperationException($"Script editor tree did not attach text clues at Section level: sectionGroups={scriptTreeSummary.SectionTextGroupCount}, attached={scriptTreeSummary.AttachedTextNodeCount}");
            }
            if (scriptTreeSummary.AttachedTextNodeCount > scriptTexts.Count)
            {
                throw new InvalidOperationException($"Script editor tree attached more text nodes than source texts: attached={scriptTreeSummary.AttachedTextNodeCount}, source={scriptTexts.Count}");
            }
            Console.WriteLine($"SCRIPT_TREE sectionTextGroups={scriptTreeSummary.SectionTextGroupCount} sceneFallbackGroups={scriptTreeSummary.SceneFallbackGroupCount} unassignedGroups={scriptTreeSummary.UnassignedGroupCount} attachedTexts={scriptTreeSummary.AttachedTextNodeCount}");
    
            var battlefieldScenarios = scenarioIndex
                .Where(scenario => ScenarioFileReader.IsBattlefieldScriptFile(scenario.FileName))
                .Take(2)
                .ToList();
            if (battlefieldScenarios.Count == 0)
            {
                throw new InvalidOperationException("R/S eex index did not include any S_XX battlefield scripts.");
            }
    
            var battlefieldService = new BattlefieldEditorService();
            var battlefieldDocs = battlefieldScenarios
                .Select(scenario => battlefieldService.Load(project, scenario, sceneDoc, tables, Array.Empty<ScenarioMapLinkInfo>()))
                .ToList();
            foreach (var battlefieldDoc in battlefieldDocs)
            {
                if (battlefieldDoc.CommandCandidates.Count == 0 || battlefieldDoc.UnitCandidates.Count == 0)
                {
                    throw new InvalidOperationException($"Battlefield editor did not produce command/unit candidates for {battlefieldDoc.Scenario.FileName}.");
                }
            }
    
            var battlefieldResources = new GameResourceIndexer().Index(project);
            var battlefieldTerrainLookup = HexzmapProbeReader.BuildTerrainNameLookup(new MaterialLibraryIndexer().Index(project));
            var battlefieldHexzmap = File.Exists(project.ResolveGameFile("Hexzmap.e5"))
                ? new HexzmapProbeReader().Read(project, battlefieldTerrainLookup)
                : null;
            var battlefieldMapLinks = new ScenarioMapLinkService().BuildLinks(battlefieldScenarios, battlefieldResources, battlefieldHexzmap);
            var battlefieldDocsWithMaps = battlefieldScenarios
                .Select(scenario => battlefieldService.Load(project, scenario, sceneDoc, tables, battlefieldMapLinks))
                .ToList();
    
            if (battlefieldDocs.Count > 1 &&
                string.Equals(BuildBattlefieldCommandSignature(battlefieldDocs[0]), BuildBattlefieldCommandSignature(battlefieldDocs[1]), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Battlefield command candidates did not change between {battlefieldDocs[0].Scenario.FileName} and {battlefieldDocs[1].Scenario.FileName}.");
            }
    
            if (battlefieldDocs.Count > 1 &&
                string.Equals(BuildBattlefieldUnitSignature(battlefieldDocs[0]), BuildBattlefieldUnitSignature(battlefieldDocs[1]), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Battlefield unit candidates did not change between {battlefieldDocs[0].Scenario.FileName} and {battlefieldDocs[1].Scenario.FileName}.");
            }
    
            if (battlefieldDocsWithMaps.Count > 1 &&
                string.Equals(BuildBattlefieldMapSignature(battlefieldDocsWithMaps[0]), BuildBattlefieldMapSignature(battlefieldDocsWithMaps[1]), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Battlefield map link preview did not change between {battlefieldDocsWithMaps[0].Scenario.FileName} and {battlefieldDocsWithMaps[1].Scenario.FileName}.");
            }
    
            var battlefieldCoordinateCandidate = battlefieldDocs[0].UnitCandidates.FirstOrDefault(candidate => BattlefieldEditorService.TryExtractFirstCoordinate(candidate, out _, out _))
                                                ?? battlefieldDocs[0].UnitCandidates.First();
            var battlefieldDeploymentCategories = battlefieldDocs
                .SelectMany(document => document.UnitCandidates)
                .Select(candidate => candidate.Category)
                .Where(category => category is "我军出场" or "友军出场" or "敌军出场")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(category => category, StringComparer.Ordinal)
                .ToList();
            if (battlefieldDeploymentCategories.Count == 0)
            {
                throw new InvalidOperationException("Battlefield editor did not split any 0x46/0x47/0x4B deployment records.");
            }
    
            var allyDeploymentSlotScenario = battlefieldScenarios.Count > 1 ? battlefieldScenarios[1] : battlefieldScenarios[0];
            var allyDeploymentSlots = new BattlefieldAllyDeploymentSlotService().Load(
                allyDeploymentSlotScenario,
                sceneDoc,
                Array.Empty<BattlefieldUnitPaletteItem>());
            if (allyDeploymentSlots.Count == 0)
            {
                throw new InvalidOperationException($"Battlefield ally deployment slot overlay did not parse any 4B/4057 slots for {allyDeploymentSlotScenario.FileName}.");
            }
    
            var firstAllyDeploymentSlot = allyDeploymentSlots.OrderBy(slot => slot.Order).First();
            if (firstAllyDeploymentSlot.Order < 0 ||
                firstAllyDeploymentSlot.GridX < 0 ||
                firstAllyDeploymentSlot.GridY < 0)
            {
                throw new InvalidOperationException($"Battlefield ally deployment slot overlay parsed an invalid first slot for {allyDeploymentSlotScenario.FileName}: order={firstAllyDeploymentSlot.Order}, coord=({firstAllyDeploymentSlot.GridX},{firstAllyDeploymentSlot.GridY}).");
            }
    
            var battlefieldLegacyDocument = new LegacyScenarioReader().Read(battlefieldDocs[0].Scenario.Path, sceneDoc);
            var battlefieldLinkedCommand = FindLegacyBattlefieldCommand(battlefieldLegacyDocument, battlefieldCoordinateCandidate);
            if (battlefieldLinkedCommand == null)
            {
                throw new InvalidOperationException($"Battlefield candidate cannot be located in legacy script tree: {battlefieldDocs[0].Scenario.FileName} {battlefieldCoordinateCandidate.TargetKey}");
            }
    
            Console.WriteLine($"BATTLEFIELD_LEGACY_CANDIDATES first={battlefieldDocs[0].Scenario.FileName} commands={battlefieldDocs[0].CommandCandidates.Count} units={battlefieldDocs[0].UnitCandidates.Count} deployment={string.Join("/", battlefieldDeploymentCategories)} located={battlefieldLinkedCommand.CommandName}@0x{battlefieldLinkedCommand.FileOffset:X6} compare={(battlefieldDocs.Count > 1 ? battlefieldDocs[1].Scenario.FileName : "none")} map={BuildBattlefieldMapSignature(battlefieldDocsWithMaps[0])} allySlots={allyDeploymentSlotScenario.FileName}:{allyDeploymentSlots.Count} first=#{firstAllyDeploymentSlot.DisplayOrder}@({firstAllyDeploymentSlot.GridX},{firstAllyDeploymentSlot.GridY}) forced={allyDeploymentSlots.Count(slot => slot.IsForced)}");
        }
        else
        {
            Console.WriteLine("RS_SCENARIO_DETAIL skipped: CczString.ini not found");
        }
    
        var imageAssignments = new ImageAssignmentService().Load(project, tables);
        if (imageAssignments.Rows.Count == 0)
        {
            throw new InvalidOperationException("人物 R/S 形象联动表为空。");
        }
    
        var row0R = Convert.ToInt32(imageAssignments.Rows[0]["R形象编号"], CultureInfo.InvariantCulture);
        var row0S = Convert.ToInt32(imageAssignments.Rows[0]["S形象编号"], CultureInfo.InvariantCulture);
        var row0Job = imageAssignments.Columns.Contains("职业")
            ? Convert.ToInt32(imageAssignments.Rows[0]["职业"], CultureInfo.InvariantCulture)
            : 1;
        var row0Face = imageAssignments.Columns.Contains("头像编号")
            ? Convert.ToInt32(imageAssignments.Rows[0]["头像编号"], CultureInfo.InvariantCulture)
            : 0;
        var row0Name = Convert.ToString(imageAssignments.Rows[0]["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var previewService = new ImageAssignmentPreviewService();
        var previewInfo = previewService.BuildResourceInfo(project, "S", row0S, row0Name, row0Face, row0Job, 1);
        AssertSMapping(0, 1, 1, 4);
        AssertSMapping(0, 1, 2, 5);
        AssertSMapping(0, 1, 3, 6);
        AssertSMapping(1, null, 1, 241, 242, 243);
        AssertSMapping(32, null, 1, 334, 335, 336);
        AssertSMapping(33, null, 1, 337);
        AssertSMapping(250, null, 1, 554);
        AssertSMapping(252, null, 1, 556);
        AssertSMapping(253, null, 1, 557);
        var e5ImageReplaceService = new E5ImageReplaceService();
        var unitMovPath = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5");
        var unitMovEntries = e5ImageReplaceService.ReadIndex(unitMovPath);
        if (unitMovEntries.Count < 556)
        {
            throw new InvalidOperationException($"Unit_mov.e5 0x110 图片索引表条目不足，预期至少 556，实际 {unitMovEntries.Count}。");
        }
        var e5ReplacePreview = e5ImageReplaceService.PreviewReplacementFromEntry(project, unitMovPath, 554, unitMovPath);
        if (e5ReplacePreview.ImageNumber != 554 ||
            e5ReplacePreview.OldSizeBytes <= 0 ||
            e5ReplacePreview.NewSizeBytes <= 0 ||
            e5ReplacePreview.IndexOffset <= 0)
        {
            throw new InvalidOperationException("E5 图片条目替换预览验证失败。");
        }
        Console.WriteLine($"E5_IMAGE_REPLACE_PREVIEW file={Path.GetFileName(unitMovPath)} entries={unitMovEntries.Count} image=554 kind={e5ReplacePreview.OldKind}->{e5ReplacePreview.NewKind} placement={e5ReplacePreview.Placement}");
        var indexedSPreviewRow = imageAssignments.Rows.Cast<DataRow>()
            .FirstOrDefault(row => Convert.ToInt32(row["S形象编号"], CultureInfo.InvariantCulture) == 250);
        var indexedSId = indexedSPreviewRow == null
            ? 250
            : Convert.ToInt32(indexedSPreviewRow["S形象编号"], CultureInfo.InvariantCulture);
        using (var rResourcePreview = previewService.TryRenderCharacterResourceImage(project, "R", row0R))
        using (var normalSResourcePreview = previewService.TryRenderCharacterResourceImage(project, "S", row0S, row0Job, 1))
        using (var indexedSResourcePreview = previewService.TryRenderCharacterResourceImage(project, "S", indexedSId, null, 1))
        using (var defaultSAllyPreview = previewService.TryRenderCharacterResourceImage(project, "S", 0, 1, 1))
        using (var defaultSFriendPreview = previewService.TryRenderCharacterResourceImage(project, "S", 0, 1, 2))
        using (var defaultSEnemyPreview = previewService.TryRenderCharacterResourceImage(project, "S", 0, 1, 3))
        using (var outOfRangeSPreview = previewService.TryRenderCharacterResourceImage(project, "S", 253, null, 1))
        {
            if (rResourcePreview == null)
            {
                throw new InvalidOperationException("R 形象应能从 Pmapobj.e5 的 0x110 索引表生成预览。");
            }
    
            if (row0S > 0 && normalSResourcePreview == null)
            {
                throw new InvalidOperationException($"S={row0S} 形象应能从 Unit_*.e5 的 0x110 索引表生成预览。");
            }
    
            if (indexedSResourcePreview == null)
            {
                throw new InvalidOperationException($"S={indexedSId} 形象应能从 Unit_*.e5 的 0x110 索引表生成预览。");
            }
    
            if (defaultSAllyPreview == null || defaultSFriendPreview == null || defaultSEnemyPreview == null)
            {
                throw new InvalidOperationException("S=0 默认兵种形象应能按职业=1 和我/友/敌阵营分别生成预览。");
            }
    
            if (outOfRangeSPreview != null)
            {
                throw new InvalidOperationException("S=253 按紧凑映射会指向 Unit图557，当前 Unit 索引表应严格越界而不是回退旧直读。");
            }
        }
        using (var battlefieldStageLow = previewService.TryRenderBattlefieldMoveIdleFrame(project, 1, null, 1, "下", 0, "初级", out var battlefieldStageLowDetail))
        using (var battlefieldStageMid = previewService.TryRenderBattlefieldMoveIdleFrame(project, 1, null, 1, "下", 0, "中级", out var battlefieldStageMidDetail))
        using (var battlefieldStageHigh = previewService.TryRenderBattlefieldMoveIdleFrame(project, 1, null, 1, "下", 0, "高级", out var battlefieldStageHighDetail))
        using (var battlefieldDownIdle = previewService.TryRenderBattlefieldMoveIdleFrame(project, indexedSId, null, 1, "下", 0, out var battlefieldDownDetail))
        using (var battlefieldLeftIdle = previewService.TryRenderBattlefieldMoveIdleFrame(project, indexedSId, null, 1, "左", 1, out var battlefieldLeftDetail))
        using (var battlefieldRightIdle = previewService.TryRenderBattlefieldMoveIdleFrame(project, indexedSId, null, 1, "右", 1, out var battlefieldRightDetail))
        {
            if (battlefieldStageLow == null || battlefieldStageMid == null || battlefieldStageHigh == null)
            {
                throw new InvalidOperationException($"三转战场待机帧生成失败：low={battlefieldStageLowDetail} mid={battlefieldStageMidDetail} high={battlefieldStageHighDetail}");
            }
    
            if (!battlefieldStageLowDetail.Contains("#241", StringComparison.Ordinal) ||
                !battlefieldStageMidDetail.Contains("#242", StringComparison.Ordinal) ||
                !battlefieldStageHighDetail.Contains("#243", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"三转 S 形象没有按初/中/高级选择 Unit 图：low={battlefieldStageLowDetail} mid={battlefieldStageMidDetail} high={battlefieldStageHighDetail}");
            }
    
            if (battlefieldDownIdle == null || battlefieldLeftIdle == null || battlefieldRightIdle == null)
            {
                throw new InvalidOperationException($"战场 Unit_mov.e5 待机帧生成失败：down={battlefieldDownDetail} left={battlefieldLeftDetail} right={battlefieldRightDetail}");
            }
    
            if (battlefieldDownIdle.Size != new Size(48, 48) ||
                battlefieldLeftIdle.Size != new Size(48, 48) ||
                battlefieldRightIdle.Size != new Size(48, 48))
            {
                throw new InvalidOperationException($"战场 Unit_mov.e5 待机帧尺寸错误：down={battlefieldDownIdle.Size} left={battlefieldLeftIdle.Size} right={battlefieldRightIdle.Size}");
            }
    
            var transparentPixels = CountTransparentPixels(battlefieldDownIdle);
            if (transparentPixels < 64)
            {
                throw new InvalidOperationException($"战场 Unit_mov.e5 待机帧透明背景不足：transparent={transparentPixels}");
            }
    
            if (!AreHorizontalMirrors(battlefieldLeftIdle, battlefieldRightIdle))
            {
                throw new InvalidOperationException("战场 Unit_mov.e5 右向待机帧没有按左向帧水平翻转。");
            }
    
            Console.WriteLine($"BATTLEFIELD_IDLE_PREVIEW S={indexedSId} down={battlefieldDownIdle.Width}x{battlefieldDownIdle.Height} transparent={transparentPixels} detail={battlefieldDownDetail} stage={battlefieldStageLowDetail}|{battlefieldStageMidDetail}|{battlefieldStageHighDetail}");
        }
        var legacyCompressedGameRoot = Path.Combine(project.WorkspaceRoot, "基底", "三国之召唤猛将6.4（60关版）基底");
        if (File.Exists(Path.Combine(legacyCompressedGameRoot, "Unit_mov.e5")))
        {
            var legacyCompressedProject = new ProjectDetector().CreateProjectFromGameRoot(legacyCompressedGameRoot);
            var legacyCompressedEntries = e5ImageReplaceService.ReadIndex(CharacterImageResourceService.ResolveGameFile(legacyCompressedProject, "Unit_mov.e5"));
            if (legacyCompressedEntries.Count < 72 || !legacyCompressedEntries[63].IsCompressed)
            {
                throw new InvalidOperationException($"Legacy compressed Unit_mov.e5 index should include compressed entry #64; entries={legacyCompressedEntries.Count} compressed64={legacyCompressedEntries.ElementAtOrDefault(63)?.IsCompressed}");
            }
    
            using var legacyCompressedPreview = previewService.TryRenderCharacterResourceImage(legacyCompressedProject, "S", 0, 21, 1);
            if (legacyCompressedPreview == null)
            {
                throw new InvalidOperationException("Legacy compressed Unit_mov.e5 entry #64 should render after LS12 decode.");
            }
    
            var legacyCompressedColorPixels = CountColorfulPixels(legacyCompressedPreview);
            if (legacyCompressedColorPixels < 48)
            {
                throw new InvalidOperationException($"Legacy compressed Unit preview is still blank or grayscale. colorPixels={legacyCompressedColorPixels}");
            }
    
            Console.WriteLine($"RS_LEGACY_COMPRESSED_PREVIEW game={Path.GetFileName(legacyCompressedGameRoot)} colorPixels={legacyCompressedColorPixels}");
        }
        var previewPng = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", $"Smoke_RS_R00_FacePreview_{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(previewPng)!);
        using (var preview = previewService.RenderResourcePreview(project, "R", row0R, row0Name, row0Face))
        {
            preview.Save(previewPng, System.Drawing.Imaging.ImageFormat.Png);
        }
        var indexedPreviewPng = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", $"Smoke_RS_S{indexedSId}_UnitPreview_{Guid.NewGuid():N}.png");
        var indexedPreviewColorPixels = 0;
        using (var indexedPreview = previewService.TryRenderCharacterResourceImage(project, "S", indexedSId, null, 1))
        {
            if (indexedPreview == null)
            {
                throw new InvalidOperationException($"S={indexedSId} Unit 索引预览二次生成失败。");
            }
    
            indexedPreviewColorPixels = CountColorfulPixels(indexedPreview);
            if (indexedPreviewColorPixels < 48)
            {
                throw new InvalidOperationException($"S={indexedSId} Unit 索引预览仍接近灰度，可能没有套用 tsb 调色板。colorPixels={indexedPreviewColorPixels}");
            }
    
            indexedPreview.Save(indexedPreviewPng, System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine($"RS_IMAGE_ASSIGN rows={imageAssignments.Rows.Count} row0={row0Name} face={row0Face} job={row0Job} R={row0R} S={row0S}");
        var hexzmapProbe = new HexzmapProbeReader().Read(project);
        if (hexzmapProbe.DirectoryEntries.Count == 0)
        {
            throw new InvalidOperationException("Hexzmap 目录候选探针没有读取到任何目录项。");
        }
        var hexzmapDirectoryHit = hexzmapProbe.DirectoryEntries.FirstOrDefault(entry =>
            entry.CandidateMapIdA.Contains("M000", StringComparison.OrdinalIgnoreCase) ||
            entry.CandidateMapIdB.Contains("M000", StringComparison.OrdinalIgnoreCase) ||
            entry.CandidateMapIdC.Contains("M000", StringComparison.OrdinalIgnoreCase));
        if (hexzmapDirectoryHit == null)
        {
            throw new InvalidOperationException("Hexzmap 目录候选探针没有发现与真实地图格数相匹配的候选项。");
        }
        Console.WriteLine($"HEXZMAP_DIRECTORY entries={hexzmapProbe.DirectoryEntries.Count} firstHitOff=0x{hexzmapDirectoryHit.EntryOffset:X} segment={hexzmapDirectoryHit.SegmentLength} fileOff=0x{hexzmapDirectoryHit.FileOffset:X} next={hexzmapDirectoryHit.NextSegmentLength}");
        if (!previewInfo.Contains("FileHead=D2800", StringComparison.Ordinal) ||
            !previewInfo.Contains("RFileHead=E1000", StringComparison.Ordinal) ||
            !previewInfo.Contains("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ||
            !previewInfo.Contains("Face.e5", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(previewPng) ||
            new FileInfo(previewPng).Length == 0 ||
            !File.Exists(indexedPreviewPng) ||
            new FileInfo(indexedPreviewPng).Length == 0)
        {
            throw new InvalidOperationException("人物形象预览未读取到 B形象指定器 6.5 的 FileHead/RFileHead 配置或 Face.e5 头像来源。");
        }
        var indexedMapping = CharacterImageResourceService.ResolveSUnitImageMapping(indexedSId);
        Console.WriteLine($"RS_IMAGE_PREVIEW png={Path.GetFileName(previewPng)} indexed={Path.GetFileName(indexedPreviewPng)} face={row0Face} S={indexedSId} mapped={string.Join("/", indexedMapping.ImageNumbers)} colorPixels={indexedPreviewColorPixels}");
    
        var itemTypeCatalogChecks = new[]
        {
            (TypeId: 8, Expected: "普通弩系", MajorCategory: "武器", Catalog: 0),
            (TypeId: 10, Expected: "普通锤系", MajorCategory: "武器", Catalog: 0),
            (TypeId: 12, Expected: "普通斧系", MajorCategory: "武器", Catalog: 0),
            (TypeId: 58, Expected: "四神宝玉/铜雀", MajorCategory: "辅助/道具", Catalog: 1)
        };
        foreach (var check in itemTypeCatalogChecks)
        {
            var description = ItemTypeCatalogService.BuildDescription(check.TypeId, check.MajorCategory, check.Catalog);
            if (!description.Contains(check.Expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"宝物类型目录解释不符合预期：type={check.TypeId}, expected={check.Expected}, actual={description}");
            }
        }
        Console.WriteLine("ITEM_TYPE_CATALOG type8=普通弩系 type10=普通锤系 type12=普通斧系 type58=四神宝玉/铜雀");
    
        var itemTable = tables.Single(t => t.TableName == "6.5-1 物品（0-103）");
        var itemRead = new HexTableReader().Read(project, itemTable, tables);
        if (!itemRead.Validation.IsUsable || itemRead.Data.Rows.Count == 0)
        {
            throw new InvalidOperationException("物品图标预览烟测无法读取 6.5-1 物品（0-103）。");
        }
    
        var itemIconIndex = Convert.ToInt32(itemRead.Data.Rows[0]["图标"], CultureInfo.InvariantCulture);
        var itemName = Convert.ToString(itemRead.Data.Rows[0]["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var itemIconPreviewService = new ItemIconPreviewService();
        var itemIconPreview = itemIconPreviewService.BuildPreview(project, itemIconIndex);
        var itemIconPng = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", $"Smoke_ItemIcon_{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(itemIconPng)!);
        try
        {
            itemIconPreview.Bitmap?.Save(itemIconPng, System.Drawing.Imaging.ImageFormat.Png);
        }
        finally
        {
            itemIconPreview.Bitmap?.Dispose();
        }
    
        if (!File.Exists(itemIconPreview.SourcePath) ||
            itemIconPreview.AvailableIconCount <= itemIconIndex ||
            !File.Exists(itemIconPng) ||
            new FileInfo(itemIconPng).Length == 0 ||
            !itemIconPreview.Message.Contains("Itemicon.dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("物品图标预览未能从 Itemicon.dll 提取有效候选图标。");
        }
    
        Console.WriteLine($"ITEM_ICON_PREVIEW item={itemName} icon={itemIconIndex} count={itemIconPreview.AvailableIconCount} png={Path.GetFileName(itemIconPng)}");
    
        var accessoryTable = tables.Single(t => t.TableName == "6.5-2 物品（104-255）");
        var accessoryRead = new HexTableReader().Read(project, accessoryTable, tables);
        var accessoryRow = accessoryRead.Data.Rows.Cast<DataRow>()
            .FirstOrDefault(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) >= 109 &&
                                   Convert.ToInt32(row["装备特效号"], CultureInfo.InvariantCulture) == 2)
            ?? throw new InvalidOperationException("未找到可用于辅助装备字段校正烟测的辅助装备行。");
        var accessoryTypeId = Convert.ToInt32(accessoryRow["类型"], CultureInfo.InvariantCulture);
        var accessoryEffectId = Convert.ToInt32(accessoryRow["装备特效号"], CultureInfo.InvariantCulture);
        var effectiveEffectId = ItemEffectInterpretationService.ResolveEffectiveEffectId("辅助/道具", accessoryTypeId, accessoryEffectId);
        var effectiveEffectIdText = ItemEffectInterpretationService.BuildEffectiveEffectIdText("辅助/道具", accessoryTypeId, accessoryEffectId);
        var effectiveEffectDescription = ItemEffectInterpretationService.BuildEffectiveEffectDescription("辅助/道具", accessoryTypeId, accessoryEffectId, effectiveEffectId, _ => string.Empty);
        if (effectiveEffectId != accessoryTypeId ||
            !effectiveEffectIdText.Contains($"类型={accessoryTypeId}", StringComparison.Ordinal) ||
            !effectiveEffectDescription.Contains("类别标记", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"辅助装备效果字段校正不符合预期：type={accessoryTypeId}, effect={accessoryEffectId}, effective={effectiveEffectId}, text={effectiveEffectIdText}, desc={effectiveEffectDescription}");
        }
        Console.WriteLine($"ITEM_ACCESSORY_EFFECT_MODEL id={Convert.ToInt32(accessoryRow["ID"], CultureInfo.InvariantCulture)} type={accessoryTypeId} rawEffect={accessoryEffectId} effective={effectiveEffectId}");
    
        Console.WriteLine("RS_SMOKE OK");
    }
    
    static int ExtractShopSlotNumber(string columnName)
    {
        var text = columnName;
        if (text.StartsWith("\u88c5\u5907", StringComparison.Ordinal)) text = text[2..];
        if (text.StartsWith("\u9053\u5177", StringComparison.Ordinal)) text = text[2..];
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot) ? slot : -1;
    }

    static void RunRSceneDialogPreviewSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(sceneStringPath))
        {
            throw new InvalidOperationException("R 场景对白预览烟测需要 CczString.ini。");
        }

        var sceneDoc = new SceneStringParser().Parse(sceneStringPath);
        var scenarioIndex = new ScenarioFileReader().ReadAllIndex(project)
            .Where(scenario => scenario.FileName.StartsWith("R_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(scenario => scenario.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (scenarioIndex.Count == 0)
        {
            throw new InvalidOperationException("R 场景对白预览烟测没有找到 R_*.eex。");
        }

        LegacyScenarioCommandNode? dialogueCommand = null;
        ScenarioFileInfo? dialogueScenario = null;
        LegacyScenarioCommandNode? talkCommand = null;
        ScenarioFileInfo? talkScenario = null;
        foreach (var scenario in scenarioIndex)
        {
            var document = new LegacyScenarioReader().Read(scenario.Path, sceneDoc);
            talkCommand ??= document.EnumerateCommands()
                .FirstOrDefault(command => command.CommandId is 0x14 or 0x15 or 0x7A &&
                                           command.TextParameters.Any(parameter => !string.IsNullOrWhiteSpace(parameter.Text)));
            if (talkCommand != null && talkScenario == null)
            {
                talkScenario = scenario;
            }

            dialogueCommand = document.EnumerateCommands()
                .FirstOrDefault(command => RSceneDialoguePreviewService.IsPreviewCommand(command.CommandId) &&
                                           command.CommandId != 0x2C &&
                                           command.TextParameters.Any(parameter => !string.IsNullOrWhiteSpace(parameter.Text)));
            if (dialogueCommand != null)
            {
                dialogueScenario = scenario;
                break;
            }
        }

        if (dialogueCommand == null || dialogueScenario == null)
        {
            throw new InvalidOperationException("R 场景对白预览烟测没有找到可渲染的对白/信息命令。");
        }

        var beforeInfo = new FileInfo(dialogueScenario.Path);
        var beforeLength = beforeInfo.Length;
        var beforeWriteTime = beforeInfo.LastWriteTimeUtc;
        var people = LoadRSceneDialoguePreviewPeople(project, tables);
        var service = new RSceneDialoguePreviewService();
        var model = service.BuildPreviewModel(dialogueCommand, people)
                    ?? throw new InvalidOperationException("R 场景对白预览模型构建失败。");
        using var canvas = new Bitmap(640, 400);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(Color.FromArgb(36, 56, 72));
            using var marker = new SolidBrush(Color.FromArgb(80, 110, 130));
            graphics.FillRectangle(marker, 0, 0, 640, 280);
        }

        var result = service.RenderPreviewOnImage(canvas, project, dialogueCommand, people);
        if (!result.Applied)
        {
            throw new InvalidOperationException("R 场景对白预览未应用：" + result.Message);
        }

        var boxPixels = CountNonBackgroundPixels(canvas, new Rectangle(12, 292, 616, 92), Color.FromArgb(36, 56, 72));
        var textPixels = CountBrightPixels(canvas, new Rectangle(108, 306, 492, 70));
        if (boxPixels < 1000 || textPixels < 20)
        {
            throw new InvalidOperationException($"R 场景对白预览像素断言失败：box={boxPixels}, text={textPixels}, model={model.Detail}");
        }

        if (model.HasFace)
        {
            var facePixels = CountNonBackgroundPixels(canvas, new Rectangle(508, 286, 120, 112), Color.FromArgb(36, 56, 72));
            if (facePixels < 256)
            {
                throw new InvalidOperationException($"R 场景对白头像区域像素不足：face={facePixels}, speaker={model.SpeakerId}");
            }
        }

        var afterInfo = new FileInfo(dialogueScenario.Path);
        if (afterInfo.Length != beforeLength || afterInfo.LastWriteTimeUtc != beforeWriteTime)
        {
            throw new InvalidOperationException($"R 场景对白预览不应修改 eex：{dialogueScenario.FileName} length {beforeLength}->{afterInfo.Length}, mtime {beforeWriteTime:o}->{afterInfo.LastWriteTimeUtc:o}");
        }

        var missingFacePixels = 0;
        if (talkCommand != null)
        {
            var missingFaceCommand = BuildRSceneDialoguePreviewMissingFaceCommand(talkCommand, people);
            var missingFaceProject = new CczProject
            {
                WorkspaceRoot = project.WorkspaceRoot,
                GameRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_SmokeMissingFaceRoot_" + Guid.NewGuid().ToString("N")),
                HexTableXmlPath = project.HexTableXmlPath,
                SceneDictionaryPath = project.SceneDictionaryPath,
                SceneEditorDirectory = project.SceneEditorDirectory,
                ImageAssignerDirectory = project.ImageAssignerDirectory,
                ImageAssignerSystemIniPath = project.ImageAssignerSystemIniPath,
                MaterialLibraryRoot = project.MaterialLibraryRoot,
                PatchConfigRoot = project.PatchConfigRoot,
                PathDiagnostics = project.PathDiagnostics
            };
            using var missingFaceCanvas = new Bitmap(640, 400);
            using (var graphics = Graphics.FromImage(missingFaceCanvas))
            {
                graphics.Clear(Color.FromArgb(36, 56, 72));
            }

            var missingFaceResult = service.RenderPreviewOnImage(missingFaceCanvas, missingFaceProject, missingFaceCommand, people);
            if (!missingFaceResult.Applied)
            {
                throw new InvalidOperationException("R 场景对白预览头像缺失用例未应用：" + missingFaceResult.Message);
            }

            missingFacePixels = CountBrightPixels(missingFaceCanvas, new Rectangle(508, 286, 120, 112));
            var missingFaceTextPixels = CountBrightPixels(missingFaceCanvas, new Rectangle(108, 306, 492, 70));
            if (missingFacePixels < 20 || missingFaceTextPixels < 20)
            {
                throw new InvalidOperationException($"R 场景对白预览头像缺失占位断言失败：face={missingFacePixels}, text={missingFaceTextPixels}, command={missingFaceCommand.CommandIdHex}");
            }
        }

        var output = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", $"Smoke_RSceneDialoguePreview_{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        canvas.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        if (!File.Exists(output) || new FileInfo(output).Length == 0)
        {
            throw new InvalidOperationException("R 场景对白预览 PNG 未写出。");
        }

        Console.WriteLine($"RSCENE_DIALOG_PREVIEW_OK file={dialogueScenario.FileName} command={dialogueCommand.CommandIdHex}@0x{dialogueCommand.FileOffset:X6} png={Path.GetFileName(output)} boxPixels={boxPixels} textPixels={textPixels} missingFacePixels={missingFacePixels} readonly=ok detail={result.Message}");
    }

    static LegacyScenarioCommandNode BuildRSceneDialoguePreviewMissingFaceCommand(LegacyScenarioCommandNode source, IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people)
    {
        var personId = people.Keys.OrderBy(id => id).FirstOrDefault();
        var text = source.TextParameters.FirstOrDefault(parameter => !string.IsNullOrWhiteSpace(parameter.Text))?.Text ?? "头像缺失占位测试";
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = source.SceneIndex,
            SectionIndex = source.SectionIndex,
            CommandIndex = source.CommandIndex,
            CommandOrdinal = source.CommandOrdinal,
            CommandId = source.CommandId is 0x14 or 0x15 or 0x7A ? source.CommandId : 0x14,
            CommandName = source.CommandId is 0x14 or 0x15 or 0x7A ? source.CommandName : "对话",
            FileOffset = source.FileOffset,
            ConsumedBytes = source.ConsumedBytes
        };
        command.Parameters.Add(new LegacyScenarioCommandParameter
        {
            Index = 0,
            Kind = LegacyScenarioParameterKind.Word16,
            IntValue = personId,
            ByteLength = 2
        });
        command.Parameters.Add(new LegacyScenarioCommandParameter
        {
            Index = 1,
            Kind = LegacyScenarioParameterKind.Text,
            Text = text,
            ByteLength = text.Length
        });
        return command;
    }

    static void RunRSceneFramePreviewSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var imageAssignments = new ImageAssignmentService().Load(project, tables);
        var rImageId = imageAssignments.Rows.Cast<DataRow>()
            .Select(row => Convert.ToInt32(row["R形象编号"], CultureInfo.InvariantCulture))
            .FirstOrDefault(id => id > 0);
        if (rImageId <= 0)
        {
            rImageId = 1;
        }

        var previewService = new ImageAssignmentPreviewService();
        using var down = previewService.TryRenderRSceneFrameByIndex(project, rImageId, 0, "下", out var downDetail)
                         ?? throw new InvalidOperationException($"R 场景下方向帧未能渲染：{downDetail}");
        using var up = previewService.TryRenderRSceneFrameByIndex(project, rImageId, 0, "上", out var upDetail)
                       ?? throw new InvalidOperationException($"R 场景上方向帧未能渲染：{upDetail}");
        using var left = previewService.TryRenderRSceneFrameByIndex(project, rImageId, 0, "左", out var leftDetail)
                         ?? throw new InvalidOperationException($"R 场景左方向帧未能渲染：{leftDetail}");
        using var right = previewService.TryRenderRSceneFrameByIndex(project, rImageId, 0, "右", out var rightDetail)
                          ?? throw new InvalidOperationException($"R 场景右方向帧未能渲染：{rightDetail}");
        using var action = FindDistinctRSceneActionFrame(previewService, project, rImageId, down, out var actionFrameIndex, out var actionDetail)
                           ?? throw new InvalidOperationException($"R 场景没有找到相对普通帧可见且不同的动作帧：R={rImageId}");
        var frontImageNumber = checked(rImageId * 2 + 1);
        var backImageNumber = checked(rImageId * 2 + 2);
        if (!leftDetail.Contains($"#{backImageNumber}", StringComparison.Ordinal) ||
            !rightDetail.Contains($"#{frontImageNumber}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"R 场景左右方向图号映射错误：left={leftDetail} right={rightDetail} expectedLeft=#{backImageNumber} expectedRight=#{frontImageNumber}");
        }
        AssertRSceneActionCommandUsesSecondParameter();
        AssertRSceneGestureMapsToStripFrame(previewService, project, rImageId);
        AssertRSceneMovementStripFramesRender(previewService, project, rImageId);

        AssertRSceneFrameVisible(down, "下");
        AssertRSceneFrameVisible(up, "上");
        AssertRSceneFrameVisible(left, "左");
        AssertRSceneFrameVisible(right, "右");
        AssertRSceneFrameVisible(action, $"动作{actionFrameIndex}");

        var upDownDiff = CountDifferentPixels(down, up);
        var leftRightDiff = CountDifferentPixels(left, right);
        var downLeftDiff = CountDifferentPixels(down, left);
        var upRightDiff = CountDifferentPixels(up, right);
        var downActionDiff = CountDifferentPixels(down, action);
        if (upDownDiff < 64)
        {
            throw new InvalidOperationException($"R 场景上下方向仍过于相似：diff={upDownDiff} down={downDetail} up={upDetail}");
        }

        if (leftRightDiff < 64 || downLeftDiff < 64 || upRightDiff < 64)
        {
            throw new InvalidOperationException($"R 场景左右方向仍过于相似：leftRight={leftRightDiff}, downLeft={downLeftDiff}, upRight={upRightDiff} left={leftDetail} right={rightDetail}");
        }

        if (downActionDiff < 64)
        {
            throw new InvalidOperationException($"R 场景动作帧没有明显变化：diff={downActionDiff} down={downDetail} action={actionDetail}");
        }

        var output = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", $"Smoke_RSceneFrameDirections_{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var sheet = new Bitmap(48 * 5, 64))
        using (var graphics = Graphics.FromImage(sheet))
        {
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(down, 0, 0);
            graphics.DrawImage(up, 48, 0);
            graphics.DrawImage(left, 96, 0);
            graphics.DrawImage(right, 144, 0);
            graphics.DrawImage(action, 192, 0);
            sheet.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        }

        Console.WriteLine($"RSCENE_FRAME_PREVIEW_OK R={rImageId} png={Path.GetFileName(output)} actionFrame={actionFrameIndex} upDownDiff={upDownDiff} leftRightDiff={leftRightDiff} downLeftDiff={downLeftDiff} upRightDiff={upRightDiff} actionDiff={downActionDiff} down={downDetail} up={upDetail} left={leftDetail} right={rightDetail} action={actionDetail}");
    }

    static void AssertRSceneActionCommandUsesSecondParameter()
    {
        const int personId = 157;
        var section = new LegacyScenarioSection
        {
            SceneIndex = 2,
            SectionIndex = 1
        };
        section.Commands.Add(CreateNumericCommand(2, 1, 10, 0x30, 0x100, personId, 0, 0, 2, 0));
        section.Commands.Add(CreateNumericCommand(2, 1, 11, 0x34, 0x110, personId, 6));

        var snapshot = new RSceneDraftService().BuildStateSnapshot(section, currentCommandIndex: 11);
        var actor = snapshot.Actors.SingleOrDefault(x => x.PersonId == personId)
                    ?? throw new InvalidOperationException("R 场景 0x34 动作帧烟测没有推演出目标人物。");
        if (actor.FrameIndex != 6)
        {
            throw new InvalidOperationException($"R 场景 0x34 动作枚举应读取第二参数 6，实际={actor.FrameIndex}");
        }
    }

    static void AssertRSceneGestureMapsToStripFrame(ImageAssignmentPreviewService previewService, CczProject project, int rImageId)
    {
        using var gesture = previewService.TryRenderRSceneFrameByIndex(project, rImageId, 6, "下", out var detail)
                            ?? throw new InvalidOperationException($"R 场景作揖动作枚举 6 未能渲染：{detail}");
        if (!detail.Contains("动作帧=6", StringComparison.Ordinal) ||
            !detail.Contains("条带帧=8", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"R 场景动作枚举到 Pmapobj 条带帧映射错误：{detail}，作揖应为动作枚举 6 -> 条带帧 8。");
        }
    }

    static void AssertRSceneMovementStripFramesRender(ImageAssignmentPreviewService previewService, CczProject project, int rImageId)
    {
        using var movement1 = previewService.TryRenderRScenePhysicalStripFrame(project, rImageId, 1, "下", out var detail1)
                              ?? throw new InvalidOperationException($"R 场景移动条带帧 1 未能渲染：{detail1}");
        using var movement2 = previewService.TryRenderRScenePhysicalStripFrame(project, rImageId, 2, "下", out var detail2)
                              ?? throw new InvalidOperationException($"R 场景移动条带帧 2 未能渲染：{detail2}");

        if (!detail1.Contains("条带帧=1", StringComparison.Ordinal) ||
            !detail2.Contains("条带帧=2", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"R 场景移动帧必须按 Pmapobj 原始条带帧读取：frame1={detail1} frame2={detail2}");
        }

        AssertRSceneFrameVisible(movement1, "移动条带帧1");
        AssertRSceneFrameVisible(movement2, "移动条带帧2");
    }

    static LegacyScenarioCommandNode CreateNumericCommand(int sceneIndex, int sectionIndex, int commandIndex, int commandId, int fileOffset, params int[] values)
    {
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            CommandIndex = commandIndex,
            CommandId = commandId,
            CommandName = "smoke",
            FileOffset = fileOffset
        };
        for (var i = 0; i < values.Length; i++)
        {
            command.Parameters.Add(new LegacyScenarioCommandParameter
            {
                Index = i,
                Kind = LegacyScenarioParameterKind.Word16,
                IntValue = values[i],
                ByteLength = 2
            });
        }

        return command;
    }

    static IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> LoadRSceneDialoguePreviewPeople(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var personTable = tables.Single(t => t.TableName == "6.5-0 人物");
        var read = new HexTableReader().Read(project, personTable, tables);
        if (!read.Validation.IsUsable)
        {
            return new Dictionary<int, RSceneDialoguePreviewPerson>();
        }

        var result = new Dictionary<int, RSceneDialoguePreviewPerson>();
        foreach (DataRow row in read.Data.Rows)
        {
            var id = read.Data.Columns.Contains("ID")
                ? Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)
                : result.Count;
            var name = read.Data.Columns.Contains("名称")
                ? Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty;
            var faceId = read.Data.Columns.Contains("头像")
                ? Convert.ToInt32(row["头像"], CultureInfo.InvariantCulture)
                : (int?)null;
            result[id] = new RSceneDialoguePreviewPerson(string.IsNullOrWhiteSpace(name) ? $"人物{id}" : name, faceId);
        }

        return result;
    }

    static int CountNonBackgroundPixels(Bitmap bitmap, Rectangle rect, Color background)
    {
        var count = 0;
        for (var y = Math.Max(0, rect.Top); y < Math.Min(bitmap.Height, rect.Bottom); y++)
        {
            for (var x = Math.Max(0, rect.Left); x < Math.Min(bitmap.Width, rect.Right); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (Math.Abs(pixel.R - background.R) > 8 ||
                    Math.Abs(pixel.G - background.G) > 8 ||
                    Math.Abs(pixel.B - background.B) > 8)
                {
                    count++;
                }
            }
        }

        return count;
    }

    static int CountBrightPixels(Bitmap bitmap, Rectangle rect)
    {
        var count = 0;
        for (var y = Math.Max(0, rect.Top); y < Math.Min(bitmap.Height, rect.Bottom); y++)
        {
            for (var x = Math.Max(0, rect.Left); x < Math.Min(bitmap.Width, rect.Right); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R > 180 && pixel.G > 170 && pixel.B > 120)
                {
                    count++;
                }
            }
        }

        return count;
    }

    static void AssertRSceneFrameVisible(Bitmap bitmap, string label)
    {
        var visible = CountVisiblePixels(bitmap);
        if (visible < 64)
        {
            throw new InvalidOperationException($"R 场景 {label} 帧可见像素过少：{visible}");
        }
    }

    static Bitmap? FindDistinctRSceneActionFrame(ImageAssignmentPreviewService previewService, CczProject project, int rImageId, Bitmap baseline, out int frameIndex, out string detail)
    {
        frameIndex = -1;
        detail = string.Empty;
        for (var candidate = 1; candidate < 20; candidate++)
        {
            var frame = previewService.TryRenderRSceneFrameByIndex(project, rImageId, candidate, "下", out var candidateDetail);
            if (frame == null)
            {
                continue;
            }

            var visible = CountVisiblePixels(frame);
            var diff = CountDifferentPixels(baseline, frame);
            if (visible >= 64 && diff >= 64)
            {
                frameIndex = candidate;
                detail = candidateDetail;
                return frame;
            }

            frame.Dispose();
        }

        return null;
    }

    static int CountVisiblePixels(Bitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A > 0) count++;
            }
        }

        return count;
    }

    static int CountDifferentPixels(Bitmap left, Bitmap right)
    {
        var count = 0;
        var width = Math.Min(left.Width, right.Width);
        var height = Math.Min(left.Height, right.Height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var a = left.GetPixel(x, y);
                var b = right.GetPixel(x, y);
                if (Math.Abs(a.A - b.A) > 8 ||
                    Math.Abs(a.R - b.R) > 16 ||
                    Math.Abs(a.G - b.G) > 16 ||
                    Math.Abs(a.B - b.B) > 16)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
