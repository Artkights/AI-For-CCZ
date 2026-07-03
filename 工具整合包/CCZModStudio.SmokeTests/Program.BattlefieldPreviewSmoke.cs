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

                Console.WriteLine($"BATTLEFIELD_PREVIEW_SMOKE_OK scenario={scenario.FileName} title=\"{document.CampaignTitle}\" image={previewBox.Image.Width}x{previewBox.Image.Height} colorPixels={colorPixels} titleWarning=\"{titleWarning}\" hint={hintLabel.Text}");
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
}
