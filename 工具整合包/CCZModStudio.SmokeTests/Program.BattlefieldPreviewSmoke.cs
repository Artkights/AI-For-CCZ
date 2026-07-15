using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunBattlefieldPreviewSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Environment.SetEnvironmentVariable("CCZMODSTUDIO_DISABLE_LAZY_UI", "1");
                RunBattlefieldMapResolutionSmoke();
                RunBattlefieldDeploymentRecordStateSmoke();
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var form = new MainForm();
                var scenarios = new ScenarioFileReader().ReadAllIndex(project);
                var scenario = scenarios.FirstOrDefault(x => ScenarioFileReader.IsBattlefieldScriptFile(x.FileName))
                    ?? throw new InvalidOperationException("No battlefield S_XX.eex scenario was found.");
                var mapResources = new MapResourceIndexer().Index(project);
                var hexzmap = new HexzmapProbeReader().Read(project);
                var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
                var dictionary = File.Exists(dictionaryPath) ? new SceneStringParser().Parse(dictionaryPath) : null;
                var document = new BattlefieldEditorService().Load(project, scenario, dictionary, tables);
                var titleWarning = TryAssertBattlefieldTitleMatchesCampaignName(project, tables, document);
                AssertBattlefieldConditionExpansionValidation(document, dictionary != null);

                SetPrivateField(form, "_project", project);
                SetPrivateField(form, "_currentMapResources", mapResources);
                SetPrivateField(form, "_currentHexzmapProbe", hexzmap);
                SetPrivateField(form, "_currentBattlefieldDocument", document);

                PerformanceMetrics.Reset();
                InvokePrivate(form, "RenderBattlefieldMapPreview", document, null);

                var previewBox = GetPrivateField<PictureBox>(form, "_battlefieldMapPreviewBox");
                var hintLabel = GetPrivateField<Label>(form, "_battlefieldMapHintLabel");
                if (previewBox.Image == null || previewBox.Image.Width <= 0 || previewBox.Image.Height <= 0)
                {
                    throw new InvalidOperationException("Battlefield map preview did not render an image. Hint=" + hintLabel.Text);
                }

                var colorPixels = CountColorPixels(previewBox.Image);
                if (colorPixels <= 0)
                {
                    throw new InvalidOperationException("Battlefield map preview rendered a blank image.");
                }

                AssertTrue(!string.IsNullOrWhiteSpace(document.MapReference.MapId), "loaded battlefield document carries a resolved map reference");
                AssertTrue(hintLabel.Parent != null, "battlefield map source label is attached to the visible toolbar layout");
                AssertTrue(hintLabel.Text.Contains(document.MapReference.MapId, StringComparison.OrdinalIgnoreCase), "battlefield preview hint shows the resolved map id");
                var staticImage = GetPrivateField<Bitmap>(form, "_battlefieldMapStaticPreviewImage");
                var terrainCells = GetPrivateField<byte[]>(form, "_battlefieldMapTerrainCells");
                previewBox.Size = staticImage.Size;
                for (var index = 0; index < 50; index++)
                {
                    var gridX = index % 20;
                    var gridY = (index / 20) % 20;
                    var location = new Point(gridX * previewBox.Width / 20 + 1, gridY * previewBox.Height / 20 + 1);
                    InvokePrivate(
                        form,
                        "HandleBattlefieldMapMouseMove",
                        new MouseEventArgs(MouseButtons.None, 0, location.X, location.Y, 0));
                }
                InvokePrivate(form, "RenderBattlefieldMapPreview", document, null);

                var placedUnits = GetPrivateField<List<BattlefieldPlacedUnit>>(form, "_battlefieldPlacedUnits");
                var dragged = new BattlefieldPlacedUnit
                {
                    TargetKey = "PreviewSmoke#Dragged",
                    PersonId = 12,
                    Name = "拖动烟测",
                    GridX = 1,
                    GridY = 1,
                    Faction = "友军"
                };
                var occupied = new BattlefieldPlacedUnit
                {
                    TargetKey = "PreviewSmoke#Occupied",
                    PersonId = 13,
                    Name = "占用烟测",
                    GridX = 4,
                    GridY = 1,
                    Faction = "敌军"
                };
                placedUnits.Add(dragged);
                placedUnits.Add(occupied);
                static Point GridPoint(PictureBox box, int gridX, int gridY)
                    => new(gridX * box.Width / 20 + box.Width / 40, gridY * box.Height / 20 + box.Height / 40);

                var dragStart = GridPoint(previewBox, 1, 1);
                var dragTarget = GridPoint(previewBox, 2, 1);
                InvokePrivate(form, "BeginBattlefieldPlacedUnitInteraction", new MouseEventArgs(MouseButtons.Right, 1, dragStart.X, dragStart.Y, 0));
                InvokePrivate(form, "HandleBattlefieldMapMouseMove", new MouseEventArgs(MouseButtons.Right, 0, dragTarget.X, dragTarget.Y, 0));
                InvokePrivate(form, "EndBattlefieldPlacedUnitInteraction", dragTarget);
                AssertEqual(2, dragged.GridX, "placed-unit drag updates the in-memory grid coordinate");
                AssertEqual(1, dragged.GridY, "placed-unit drag preserves the expected row");

                dragStart = GridPoint(previewBox, 2, 1);
                var occupiedTarget = GridPoint(previewBox, 4, 1);
                InvokePrivate(form, "BeginBattlefieldPlacedUnitInteraction", new MouseEventArgs(MouseButtons.Right, 1, dragStart.X, dragStart.Y, 0));
                InvokePrivate(form, "HandleBattlefieldMapMouseMove", new MouseEventArgs(MouseButtons.Right, 0, occupiedTarget.X, occupiedTarget.Y, 0));
                InvokePrivate(form, "EndBattlefieldPlacedUnitInteraction", occupiedTarget);
                AssertEqual(2, dragged.GridX, "occupied drag target restores the original grid coordinate");

                var hiddenCheck = GetPrivateField<CheckBox>(form, "_battlefieldHiddenCheckBox");
                hiddenCheck.Checked = true;
                AssertTrue(dragged.Hidden, "hidden property change updates the in-memory placed unit immediately");
                var mainTabs = GetPrivateField<TabControl>(form, "_mainTabs");
                mainTabs.SelectedTab = mainTabs.TabPages.Cast<TabPage>().First(page => page.Contains(previewBox));
                var oldAnimationPhase = GetPrivateField<int>(form, "_battlefieldUnitAnimationPhase");
                InvokePrivate(form, "AdvanceBattlefieldUnitAnimation");
                AssertTrue(oldAnimationPhase != GetPrivateField<int>(form, "_battlefieldUnitAnimationPhase"), "battlefield unit animation advances without rebuilding the static map");

                AssertTrue(ReferenceEquals(staticImage, GetPrivateField<Bitmap>(form, "_battlefieldMapStaticPreviewImage")), "dynamic invalidation preserves the static battlefield bitmap");
                AssertTrue(ReferenceEquals(terrainCells, GetPrivateField<byte[]>(form, "_battlefieldMapTerrainCells")), "drag, property edit, and animation preserve the cached terrain array");
                AssertTrue(ReferenceEquals(staticImage, previewBox.Image), "PictureBox owns the cached static battlefield bitmap");
                var performance = PerformanceMetrics.GetSnapshot();
                AssertEqual(1L, performance.Counters.GetValueOrDefault("Battlefield.StaticMap.Build.Completed"), "static battlefield map builds once");
                AssertTrue(performance.Counters.GetValueOrDefault("Battlefield.DynamicOverlay.Invalidate") >= 50, "50 hover moves invalidate only the dynamic overlay");
                AssertEqual(52L, performance.Counters.GetValueOrDefault("Battlefield.MouseMove.Completed"), "each hover and drag event is handled once");
                if (terrainCells.Length > 0)
                {
                    AssertEqual(1L, performance.Counters.GetValueOrDefault("Battlefield.Hexzmap.Decode.Completed"), "Hexzmap terrain decodes once for repeated dynamic refreshes");
                }

                AssertExplicitBattlefieldMapOverridesScenario(form, document, mapResources, hexzmap, previewBox, hintLabel);
                Console.WriteLine($"BATTLEFIELD_PREVIEW_SMOKE_OK scenario={scenario.FileName} title=\"{document.CampaignTitle}\" image={previewBox.Image.Width}x{previewBox.Image.Height} colorPixels={colorPixels} titleWarning=\"{titleWarning}\" hint={hintLabel.Text}");
                AssertMissingExplicitBattlefieldMapDoesNotFallback(form, document, mapResources, hexzmap, previewBox, hintLabel);
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
            throw new InvalidOperationException("Battlefield preview smoke failed.", failure);
        }
    }

    private static void RunBattlefieldMapResolutionSmoke()
    {
        var scenario = new ScenarioFileInfo
        {
            FileName = "S_12.eex",
            Id = "12",
            Path = "S_12.eex"
        };

        var fallback = BattlefieldMapResolutionService.Resolve(scenario, null);
        AssertEqual("M012", fallback.MapId, "battlefield map falls back to the S scenario number");
        AssertEqual(BattlefieldMapReferenceSource.ScenarioNumberFallback, fallback.SourceKind, "fallback map source kind");

        var explicitMap = BattlefieldMapResolutionService.Resolve(
            scenario,
            BuildBattlefieldMapResolutionDocument(BuildBattlefieldBackgroundCommand(3, 1, commandIndex: 18)));
        AssertEqual("M001", explicitMap.MapId, "0x27 battlefield category reads flattened slot 4");
        AssertEqual(BattlefieldMapReferenceSource.BackgroundCommand27, explicitMap.SourceKind, "explicit map source kind");
        AssertEqual(18, explicitMap.CommandIndex, "explicit map keeps its command location");

        foreach (var category in new[] { 0, 1, 2 })
        {
            var ignored = BattlefieldMapResolutionService.Resolve(
                scenario,
                BuildBattlefieldMapResolutionDocument(BuildBattlefieldBackgroundCommand(category, 1, commandIndex: category + 1)));
            AssertEqual("M012", ignored.MapId, $"0x27 background category {category} is not a battlefield map");
            AssertEqual(BattlefieldMapReferenceSource.ScenarioNumberFallback, ignored.SourceKind, $"category {category} uses scenario fallback");
        }

        foreach (var invalidMapNumber in new[] { -1, 1000 })
        {
            var ignored = BattlefieldMapResolutionService.Resolve(
                scenario,
                BuildBattlefieldMapResolutionDocument(BuildBattlefieldBackgroundCommand(3, invalidMapNumber, commandIndex: 1)));
            AssertEqual("M012", ignored.MapId, $"invalid 0x27 map number {invalidMapNumber} uses scenario fallback");
        }

        var truncatedCommand = new LegacyScenarioCommandNode
        {
            SceneIndex = 1,
            SectionIndex = 1,
            CommandIndex = 1,
            CommandOrdinal = 1,
            CommandId = 0x27,
            CommandName = "背景显示"
        };
        truncatedCommand.Parameters.Add(new LegacyScenarioCommandParameter
        {
            Index = 0,
            Kind = LegacyScenarioParameterKind.Word16,
            IntValue = 3
        });
        var truncated = BattlefieldMapResolutionService.Resolve(
            scenario,
            BuildBattlefieldMapResolutionDocument(truncatedCommand));
        AssertEqual("M012", truncated.MapId, "truncated 0x27 command uses scenario fallback");

        var firstValidAfterInvalid = BattlefieldMapResolutionService.Resolve(
            scenario,
            BuildBattlefieldMapResolutionDocument(
                BuildBattlefieldBackgroundCommand(3, -1, commandIndex: 1),
                BuildBattlefieldBackgroundCommand(3, 7, commandIndex: 2),
                BuildBattlefieldBackgroundCommand(3, 8, commandIndex: 3)));
        AssertEqual("M007", firstValidAfterInvalid.MapId, "first valid 0x27 battlefield map wins");
        AssertEqual(2, firstValidAfterInvalid.CommandIndex, "invalid 0x27 does not block a later valid map");

        var editableCommand = BuildBattlefieldBackgroundCommand(3, 1, commandIndex: 4);
        var editableLegacyDocument = BuildBattlefieldMapResolutionDocument(editableCommand);
        var current = new BattlefieldEditorDocument { Scenario = scenario };
        var rebuilt = BattlefieldEditorService.RebuildFromLegacyDocument(current, editableLegacyDocument);
        AssertEqual("M001", rebuilt.MapReference.MapId, "battlefield document rebuild resolves the current 0x27 map");
        editableCommand.Parameters[4].IntValue = 2;
        rebuilt = BattlefieldEditorService.RebuildFromLegacyDocument(rebuilt, editableLegacyDocument);
        AssertEqual("M002", rebuilt.MapReference.MapId, "battlefield document rebuild refreshes an edited 0x27 map");

        var unresolved = BattlefieldMapResolutionService.Resolve(
            new ScenarioFileInfo { FileName = "battle.eex", Path = "battle.eex" },
            null);
        AssertEqual(BattlefieldMapReferenceSource.Unresolved, unresolved.SourceKind, "scenario without a number remains unresolved");
        AssertEqual(string.Empty, unresolved.MapId, "unresolved battlefield map id is empty");
    }

    private static LegacyScenarioDocument BuildBattlefieldMapResolutionDocument(params LegacyScenarioCommandNode[] commands)
    {
        var document = new LegacyScenarioDocument { FilePath = "S_12.eex" };
        var scene = new LegacyScenarioScene { SceneIndex = 1 };
        var section = new LegacyScenarioSection { SceneIndex = 1, SectionIndex = 1 };
        section.Commands.AddRange(commands);
        scene.Sections.Add(section);
        document.Scenes.Add(scene);
        return document;
    }

    private static LegacyScenarioCommandNode BuildBattlefieldBackgroundCommand(int category, int mapNumber, int commandIndex)
    {
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = 1,
            SectionIndex = 1,
            CommandIndex = commandIndex,
            CommandOrdinal = commandIndex,
            CommandId = 0x27,
            CommandName = "背景显示",
            FileOffset = 0x200 + commandIndex * 0x10
        };
        for (var index = 0; index <= category + 1; index++)
        {
            command.Parameters.Add(new LegacyScenarioCommandParameter
            {
                Index = index,
                Kind = LegacyScenarioParameterKind.Word16,
                IntValue = index == 0 ? category : index == category + 1 ? mapNumber : 0
            });
        }

        return command;
    }

    private static void AssertMissingExplicitBattlefieldMapDoesNotFallback(
        MainForm form,
        BattlefieldEditorDocument loadedDocument,
        IReadOnlyList<MapResourceItem> mapResources,
        HexzmapProbeResult hexzmap,
        PictureBox previewBox,
        Label hintLabel)
    {
        var usedMapNumbers = mapResources
            .Select(item => item.MapId)
            .Concat(hexzmap.Blocks.Select(block => block.MapId))
            .Where(mapId => mapId.Length > 1 && mapId[0] == 'M')
            .Select(mapId => int.TryParse(mapId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1)
            .Where(value => value >= 0)
            .ToHashSet();
        var missingMapNumber = Enumerable.Range(0, 1000).First(number => !usedMapNumbers.Contains(number));
        var explicitReference = BattlefieldMapResolutionService.Resolve(
            loadedDocument.Scenario,
            BuildBattlefieldMapResolutionDocument(BuildBattlefieldBackgroundCommand(3, missingMapNumber, commandIndex: 9)));
        var explicitDocument = new BattlefieldEditorDocument
        {
            Scenario = loadedDocument.Scenario,
            MapReference = explicitReference
        };

        SetPrivateField(form, "_currentBattlefieldDocument", explicitDocument);
        InvokePrivate(form, "RenderBattlefieldMapPreview", explicitDocument, null);

        AssertTrue(previewBox.Image == null, "missing explicit 0x27 map does not render the S-number fallback map");
        AssertTrue(hintLabel.Text.Contains(explicitReference.MapId, StringComparison.OrdinalIgnoreCase), "missing explicit map hint keeps the requested map id");
        AssertTrue(hintLabel.Text.Contains("0x27", StringComparison.OrdinalIgnoreCase), "missing explicit map hint keeps the command source");
    }

    private static void AssertExplicitBattlefieldMapOverridesScenario(
        MainForm form,
        BattlefieldEditorDocument loadedDocument,
        IReadOnlyList<MapResourceItem> mapResources,
        HexzmapProbeResult hexzmap,
        PictureBox previewBox,
        Label hintLabel)
    {
        var targetMap = mapResources.FirstOrDefault(map =>
            !map.MapId.Equals(loadedDocument.MapReference.MapId, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(map.Path) &&
            map.Width > 0 &&
            map.Height > 0 &&
            hexzmap.Blocks.Any(block => block.MapId.Equals(map.MapId, StringComparison.OrdinalIgnoreCase)));
        AssertTrue(targetMap != null, "battlefield preview fixture has a second map with Hexzmap terrain");

        var mapNumber = int.Parse(targetMap!.MapId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var explicitReference = BattlefieldMapResolutionService.Resolve(
            loadedDocument.Scenario,
            BuildBattlefieldMapResolutionDocument(BuildBattlefieldBackgroundCommand(3, mapNumber, commandIndex: 8)));
        var explicitDocument = new BattlefieldEditorDocument
        {
            Scenario = loadedDocument.Scenario,
            MapReference = explicitReference
        };

        SetPrivateField(form, "_currentBattlefieldDocument", explicitDocument);
        InvokePrivate(form, "RenderBattlefieldMapPreview", explicitDocument, null);

        AssertTrue(previewBox.Image != null, "explicit 0x27 map renders instead of the S-number map");
        AssertEqual(targetMap.Width, previewBox.Image!.Width, "explicit 0x27 map preview width");
        AssertEqual(targetMap.Height, previewBox.Image.Height, "explicit 0x27 map preview height");
        AssertTrue(hintLabel.Text.Contains(targetMap.MapId, StringComparison.OrdinalIgnoreCase), "explicit map preview hint shows the overridden map id");
        AssertTrue(hintLabel.Text.Contains("0x27", StringComparison.OrdinalIgnoreCase), "explicit map preview hint shows the command source");
        AssertTrue(InvokePrivateResult<bool>(form, "HasBattlefieldMapResource", explicitDocument), "map resource lookup uses the explicit 0x27 map id");

        var block = hexzmap.Blocks.First(item => item.MapId.Equals(targetMap.MapId, StringComparison.OrdinalIgnoreCase));
        var cells = new HexzmapProbeReader().GetBlockCells(hexzmap, block);
        var hoverArguments = new object?[] { 0, 0, null };
        var hoverResolved = InvokePrivateResult<bool>(form, "TryGetBattlefieldHoverTerrain", hoverArguments);
        AssertTrue(hoverResolved, "terrain hover lookup uses the explicit 0x27 map block");
        AssertTrue(
            (hoverArguments[2]?.ToString() ?? string.Empty).Contains(HexDisplayFormatter.FormatByte(cells[0]), StringComparison.OrdinalIgnoreCase),
            "terrain hover value comes from the explicit 0x27 map block");
    }

    private static void RunBattlefieldDeploymentRecordStateSmoke()
    {
        var friendPersonCode = BattlefieldEditorService.EncodePerson2ScriptCode(12);
        var friendCommand = BuildDeploymentStateSmokeCommand(
            0x46,
            "友军出场设定",
            sceneIndex: 1,
            values: BuildDeploymentStateSmokeValues(
                BattlefieldDeploymentRecordDefinition.Friend,
                recordIndex: 0,
                recordValues: [friendPersonCode, 1, 8, 10, 1, 3, 2, 4, BattlefieldEditorService.EncodePerson2ScriptCode(13), 14, 16]));

        var friendRecords = BattlefieldEditorService.BuildDeploymentRecordStates(new[] { friendCommand });
        var friend = friendRecords.Single(record => record.CommandId == 0x46 && record.RecordIndex == 0);
        AssertEqual(friendPersonCode, friend.PersonRawCode, "46 keeps Person2 raw code");
        AssertEqual(12, friend.PersonId, "46 decodes Person2 person id");
        AssertTrue(friend.Hidden, "46 hidden flag comes from slot 1");
        AssertEqual("右", friend.Direction, "46 direction reads slot 4");
        AssertEqual(3, friend.LevelOffset, "46 level offset reads slot 5");
        AssertEqual("高级", friend.JobLevel, "46 job level reads slot 6");
        AssertEqual("到点", friend.AiMode, "46 AI reads slot 7");
        AssertTrue(friend.IsInitialDeployment, "46 Scene1 is initial deployment");

        var zeroFriendCommand = BuildDeploymentStateSmokeCommand(
            0x46,
            "友军出场设定",
            sceneIndex: 1,
            values: BuildDeploymentStateSmokeValues(
                BattlefieldDeploymentRecordDefinition.Friend,
                recordIndex: 0,
                recordValues: Enumerable.Repeat(0, BattlefieldDeploymentRecordDefinition.Friend.GroupSize).ToArray()));
        var zeroFriendRecords = BattlefieldEditorService.BuildDeploymentRecordStates(new[] { zeroFriendCommand });
        var zeroFriend = zeroFriendRecords.Single(record => record.CommandId == 0x46 && record.RecordIndex == 0);
        AssertEqual(0, zeroFriend.PersonRawCode, "46 all-zero record keeps raw Person2 code 0");
        AssertEqual(0, zeroFriend.PersonId, "46 all-zero record decodes as person id 0");
        AssertTrue(!zeroFriend.IsBlank, "46 all-zero record is not blank");
        var zeroFriendSlot = BattlefieldEditorService.BuildDeploymentSlotInfos(zeroFriendCommand)
            .Single(slot => slot.CommandId == 0x46 && slot.RecordIndex == 0);
        AssertTrue(!zeroFriendSlot.IsBlank, "46 all-zero slot info is not blank");
        AssertEqual(0, zeroFriendSlot.PersonId, "46 all-zero slot info keeps person id 0 candidate");

        var scene2Command = BuildDeploymentStateSmokeCommand(
            0x46,
            "友军剧情出场设定",
            sceneIndex: 2,
            values: BuildDeploymentStateSmokeValues(
                BattlefieldDeploymentRecordDefinition.Friend,
                recordIndex: 0,
                recordValues: [friendPersonCode, 0, 2, 3, 2, 0, 0, 0, 0, 0, 0]));
        AssertTrue(!BattlefieldEditorService.BuildDeploymentRecordStates(new[] { scene2Command })[0].IsInitialDeployment, "Scene2 deployment is not initial preview");

        foreach (var (reinforcement, hidden) in new[] { (true, false), (false, true), (true, true) })
        {
            var enemyCommand = BuildDeploymentStateSmokeCommand(
                0x47,
                "敌军出场设定",
                sceneIndex: 1,
                values: BuildDeploymentStateSmokeValues(
                    BattlefieldDeploymentRecordDefinition.Enemy,
                    recordIndex: 0,
                    recordValues:
                    [
                        BattlefieldEditorService.EncodePerson2ScriptCode(21),
                        reinforcement ? 1 : 0,
                        hidden ? 1 : 0,
                        4,
                        5,
                        3,
                        2,
                        1,
                        5,
                        BattlefieldEditorService.EncodePerson2ScriptCode(22),
                        6,
                        7
                    ]));
            var enemy = BattlefieldEditorService.BuildDeploymentRecordStates(new[] { enemyCommand })[0];
            AssertEqual(reinforcement, enemy.Reinforcement, $"47 reinforcement={reinforcement} reads slot 1");
            AssertEqual(hidden, enemy.Hidden, $"47 hidden={hidden} reads slot 2");
            AssertEqual("左", enemy.Direction, "47 direction reads slot 5");
            AssertEqual("跟随", enemy.AiMode, "47 AI reads slot 8");
        }

        var blankEnemyValues = Enumerable.Repeat(0, BattlefieldDeploymentRecordDefinition.Enemy.GroupSize).ToArray();
        blankEnemyValues[BattlefieldDeploymentRecordDefinition.Enemy.PersonIndex] = BattlefieldDeploymentRecordFormatter.EmptyPerson2Code;
        var blankEnemyCommand = BuildDeploymentStateSmokeCommand(
            0x47,
            "敌军出场设定",
            sceneIndex: 1,
            values: BuildDeploymentStateSmokeValues(
                BattlefieldDeploymentRecordDefinition.Enemy,
                recordIndex: 0,
                recordValues: blankEnemyValues));
        var blankEnemy = BattlefieldEditorService.BuildDeploymentRecordStates(new[] { blankEnemyCommand })[0];
        AssertEqual(BattlefieldDeploymentRecordFormatter.EmptyPerson2Code, blankEnemy.PersonRawCode, "47 blank record stores -1 person sentinel");
        AssertTrue(blankEnemy.IsBlank, "47 person -1 record is blank");
        AssertTrue(BattlefieldEditorService.BuildDeploymentSlotInfos(blankEnemyCommand).Single(slot => slot.RecordIndex == 0).IsBlank, "47 person -1 slot info is blank");

        var allyCommand = BuildDeploymentStateSmokeCommand(
            0x4B,
            "我军出场设定",
            sceneIndex: 1,
            values: [3, 9, 11, 0, 1]);
        var ally = BattlefieldEditorService.BuildDeploymentRecordStates(new[] { allyCommand })[0];
        AssertTrue(ally.IsAllySlot, "4B is ally slot");
        AssertEqual(3, ally.PersonRawCode, "4B keeps deployment order in person raw slot");
        AssertEqual(-1, ally.PersonId, "4B deployment order is not decoded as Data person");
        AssertEqual(9, ally.GridX, "4B X reads slot 1");
        AssertEqual(11, ally.GridY, "4B Y reads slot 2");
        AssertEqual("上", ally.Direction, "4B direction reads slot 3");
        AssertTrue(ally.Hidden, "4B hidden reads slot 4");
    }

    private static BattlefieldCommandCandidate BuildDeploymentStateSmokeCommand(
        int commandId,
        string commandName,
        int sceneIndex,
        IReadOnlyList<int> values)
        => new()
        {
            Index = 1,
            SceneIndex = sceneIndex,
            SectionIndex = 1,
            CommandIndex = 1,
            OffsetHex = "000100",
            CommandIdHex = HexDisplayFormatter.Format(commandId, 2),
            CommandName = commandName,
            RawContextWordsHex = string.Join(" ", new[] { commandId }.Concat(values).Select(value => HexDisplayFormatter.FormatWord(unchecked((ushort)value))))
        };

    private static IReadOnlyList<int> BuildDeploymentStateSmokeValues(
        BattlefieldDeploymentRecordDefinition definition,
        int recordIndex,
        IReadOnlyList<int> recordValues)
    {
        var values = Enumerable.Repeat(0, definition.GroupSize * definition.RecordCount).ToArray();
        for (var i = 0; i < recordValues.Count && i < definition.GroupSize; i++)
        {
            values[recordIndex * definition.GroupSize + i] = recordValues[i];
        }

        return values;
    }

    private static int CountColorPixels(Image image)
    {
        using var bitmap = new Bitmap(image);
        var count = 0;
        var stepX = Math.Max(1, bitmap.Width / 80);
        var stepY = Math.Max(1, bitmap.Height / 80);
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A > 0 && (color.R > 8 || color.G > 8 || color.B > 8))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static string TryAssertBattlefieldTitleMatchesCampaignName(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        BattlefieldEditorDocument document)
    {
        if (!document.CanWriteCampaignTitle)
        {
            return "Battlefield title did not resolve to the campaign-name table; preview continues with script title.";
        }

        var profile = new CczEngineProfileService().Detect(project);
        var table = HexTableNameResolver.ResolveForProject(project, tables, profile.TableHints.CampaignNameTable);
        var read = new HexTableReader().Read(project, table, tables);
        var row = read.Data.Rows
            .Cast<System.Data.DataRow>()
            .FirstOrDefault(x => Convert.ToInt32(x["ID"], CultureInfo.InvariantCulture) == document.CampaignId);
        if (row == null)
        {
            return "Campaign-name table row was not found for battlefield scenario; preview continues with script title.";
        }

        var expected = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        if (!string.Equals(expected, document.CampaignTitle, StringComparison.Ordinal))
        {
            return $"Battlefield title source mismatch: expected={expected}, actual={document.CampaignTitle}; preview continues with script title.";
        }

        return string.Empty;
    }

    private static void AssertBattlefieldConditionExpansionValidation(BattlefieldEditorDocument document, bool hasDictionary)
    {
        if (!hasDictionary || document.ConditionEntry == null) return;
        var expanded = document.ConditionEntry.Text + " 扩容校验文本";
        if (EncodingService.GetGbkByteCount(expanded) <= document.ConditionEntry.ByteLength)
        {
            expanded += "继续增加到超过原始容量";
        }

        var error = BattlefieldEditorService.ValidateStructuredScenarioText(
            document.ConditionEntry,
            expanded,
            "胜败条件",
            allowExpansion: true);
        if (error != null)
        {
            throw new InvalidOperationException("Battlefield condition expansion validation should allow full-structure text growth: " + error);
        }
    }

    private static void SetPrivateField<T>(MainForm form, string fieldName, T value)
    {
        var field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Field not found: " + fieldName);
        field.SetValue(form, value);
    }

    private static T GetPrivateField<T>(MainForm form, string fieldName)
    {
        var field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Field not found: " + fieldName);
        return (T)field.GetValue(form)!;
    }

    private static void InvokePrivate(MainForm form, string methodName, params object?[] args)
    {
        var method = typeof(MainForm).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Method not found: " + methodName);
        method.Invoke(form, args);
    }

    private static T InvokePrivateResult<T>(MainForm form, string methodName, params object?[] args)
    {
        var method = typeof(MainForm).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Method not found: " + methodName);
        return (T)method.Invoke(form, args)!;
    }
}
