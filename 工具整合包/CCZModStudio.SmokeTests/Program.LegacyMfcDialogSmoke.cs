using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal partial class Program
{
    private static void RunLegacyMfcDialogSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var oldDispatchMap = LoadOldMfcDialogDispatchMap(project.WorkspaceRoot);
        AssertEqual(101, oldDispatchMap.Count, "old OnEditModify command mapping count");
        AssertEqual(71, oldDispatchMap.Values.Distinct(StringComparer.Ordinal).Count(), "old OnEditModify dialog count");

        var resourceSpecs = LegacyMfcDialogResourceLoader.LoadFromWorkspace();
        AssertEqual(71, resourceSpecs.Count, "legacy .rc dialog resource count");

        var templates = LegacyMfcDialogResourceLoader.LoadDialog114TemplatesFromWorkspace();
        AssertEqual(34, templates.Count, "Dialog_114 template count");
        AssertTrue(templates.ContainsKey(0) && templates.ContainsKey(33), "Dialog_114 templates must cover 0..33");
        AssertEqual(71, CountExplicitLegacyMfcDialogBehaviors(project.WorkspaceRoot), "legacy C# dialog behavior switch count");
        AssertDeploymentDefaultParameterSentinels();

        foreach (var dialogName in oldDispatchMap.Values.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            AssertTrue(resourceSpecs.ContainsKey(dialogName), $"{dialogName} resource exists");
            AssertTrue(LegacyMfcDialogCatalog.TryGet(dialogName, out _), $"{dialogName} catalog behavior exists");
        }

        foreach (var pair in oldDispatchMap.OrderBy(x => x.Key))
        {
            var actual = LegacyCommandEditDispatcher.GetDialogName(pair.Key);
            AssertEqual(pair.Value, actual, $"command {pair.Key} dispatcher mapping");
        }

        var sortedCombos = resourceSpecs
            .SelectMany(spec => spec.Value.Controls.Select(control => (Dialog: spec.Key, Control: control)))
            .Where(x => x.Control.Kind == LegacyMfcControlKind.ComboBox && x.Control.Sorted)
            .Select(x => $"{x.Dialog}.{x.Control.Id}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        AssertEqual("Dialog_33.IDC_COMBO1", string.Join(",", sortedCombos), "CBS_SORT combo controls");

        var dataSources = LegacyMfcDialogDataSources.Create(project, tables);
        RunLegacyPersonComboLookupSmoke(dataSources);
        AssertEqual("0:普通", dataSources.GestureLabel(0), "Dialog_52 gesture preview label 0");
        AssertEqual("1:下跪", dataSources.GestureLabel(1), "Dialog_52 gesture preview label 1");
        AssertEqual("19:变量", dataSources.GestureLabel(19), "Dialog_52 gesture preview label 19");
        foreach (var group in oldDispatchMap.GroupBy(x => x.Value, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var commandId = group.Min(x => x.Key);
            var fixture = OpenLegacyMfcDialog(group.Key, commandId, Enumerable.Repeat(0, 1300).ToArray(), "全量初始化", dataSources);
            CommitLegacyMfcDialog(fixture);
        }

        RunLegacyMfcDialogBehaviorSmoke(dataSources);
        RunLegacyScenarioCommandDisplaySmoke(dataSources);

        Console.WriteLine($"LEGACY_MFC_DIALOG_SMOKE_OK oldCommands={oldDispatchMap.Count} dialogs={resourceSpecs.Count} templates={templates.Count} sortedCombos={string.Join("/", sortedCombos)}");
    }

    private static void RunLegacyPersonComboLookupSmoke(LegacyMfcDialogDataSources dataSources)
    {
        var dialog46 = OpenLegacyMfcDialog(
            "Dialog_70",
            70,
            Enumerable.Repeat(0, 11 * 20).ToArray(),
            string.Empty,
            dataSources);
        var person = (LegacyPersonComboBox)dialog46.Session.ComboBox("IDC_COMBO1");
        var target = (LegacyPersonComboBox)dialog46.Session.ComboBox("IDC_COMBO13");
        AssertEqual(LegacyPersonComboKind.Person2, person.PersonKind, "Dialog_70 primary person numeric lookup kind");
        AssertEqual(LegacyPersonComboKind.Person2, target.PersonKind, "Dialog_70 target person numeric lookup kind");
        AssertEqual(1, person.ItemPopulationCount, "Dialog_70 primary person list initially populated once");
        AssertEqual(1, target.ItemPopulationCount, "Dialog_70 target person list initially populated once");

        var now = DateTime.UtcNow;
        AssertTrue(person.ProcessLookupDigit('1', now), "person numeric lookup accepts first digit");
        AssertTrue(person.ProcessLookupDigit('2', now.AddMilliseconds(100)), "person numeric lookup accepts second digit");
        AssertTrue(person.ProcessLookupDigit('3', now.AddMilliseconds(200)), "person numeric lookup accepts third digit");
        AssertEqual(123, person.SelectedIndex, "person numeric lookup 123 selects role 123");
        AssertEqual("123", person.LookupBuffer, "person numeric lookup keeps multi-digit buffer");

        AssertTrue(person.ProcessLookupBackspace(now.AddMilliseconds(300)), "person numeric lookup backspace is handled");
        AssertEqual(12, person.SelectedIndex, "person numeric lookup backspace returns to role 12");
        AssertEqual("12", person.LookupBuffer, "person numeric lookup backspace updates buffer");
        person.ResetLookup();
        var mainKey1 = new KeyEventArgs(Keys.D1);
        var mainKey2 = new KeyEventArgs(Keys.D2);
        var mainKey3 = new KeyEventArgs(Keys.D3);
        InvokePrivate(person, "OnKeyDown", mainKey1);
        InvokePrivate(person, "OnKeyDown", mainKey2);
        InvokePrivate(person, "OnKeyDown", mainKey3);
        AssertTrue(
            mainKey1.Handled && mainKey1.SuppressKeyPress &&
            mainKey2.Handled && mainKey2.SuppressKeyPress &&
            mainKey3.Handled && mainKey3.SuppressKeyPress,
            "main keyboard digits suppress native single-character ComboBox lookup");
        AssertEqual(123, person.SelectedIndex, "main keyboard lookup 123 selects role 123");
        AssertEqual("123", person.LookupBuffer, "main keyboard lookup keeps multi-digit buffer");
        person.ResetLookup();
        AssertTrue(person.ProcessLookupKey(Keys.NumPad1, now), "person numeric lookup accepts numpad digit 1");
        AssertTrue(person.ProcessLookupKey(Keys.NumPad2, now.AddMilliseconds(100)), "person numeric lookup accepts numpad digit 2");
        AssertTrue(person.ProcessLookupKey(Keys.NumPad3, now.AddMilliseconds(200)), "person numeric lookup accepts numpad digit 3");
        AssertEqual(123, person.SelectedIndex, "person numpad lookup 123 selects role 123");
        person.ProcessLookupDigit('4', now.AddSeconds(2));
        AssertEqual(4, person.SelectedIndex, "person numeric lookup timeout starts a fresh number");
        person.ProcessLookupDigit('2', now.AddSeconds(2.1));
        InvokePrivate(person, "OnKeyDown", new KeyEventArgs(Keys.Escape));
        AssertEqual(string.Empty, person.LookupBuffer, "Escape clears person numeric lookup buffer");
        person.ProcessLookupDigit('3', now.AddSeconds(2.2));
        InvokePrivate(person, "OnKeyDown", new KeyEventArgs(Keys.Down));
        AssertEqual(string.Empty, person.LookupBuffer, "direction key clears person numeric lookup buffer");
        person.ProcessLookupDigit('4', now.AddSeconds(2.3));
        InvokePrivate(person, "OnDropDown", EventArgs.Empty);
        AssertEqual(string.Empty, person.LookupBuffer, "opening dropdown clears person numeric lookup buffer");
        person.ProcessLookupDigit('5', now.AddSeconds(2.4));
        InvokePrivate(person, "OnLeave", EventArgs.Empty);
        AssertEqual(string.Empty, person.LookupBuffer, "focus loss clears person numeric lookup buffer");
        person.ResetLookup();
        var previous = person.SelectedIndex;
        person.ProcessLookupDigit('9', now);
        person.ProcessLookupDigit('9', now.AddMilliseconds(50));
        person.ProcessLookupDigit('9', now.AddMilliseconds(100));
        person.ProcessLookupDigit('9', now.AddMilliseconds(150));
        person.ProcessLookupDigit('9', now.AddMilliseconds(200));
        AssertTrue(person.SelectedIndex >= 0, "invalid person lookup never creates an unselected script value");
        AssertTrue(person.SelectedIndex != previous || person.LookupBuffer == "9", "invalid accumulated lookup restarts from the latest digit");

        dialog46.Session.ListBox("IDC_LIST1").SelectedIndex = 1;
        AssertEqual(1, person.ItemPopulationCount, "Dialog_70 row switch reuses primary Person2 items");
        AssertEqual(1, target.ItemPopulationCount, "Dialog_70 row switch reuses target Person2 items");

        var dialog6 = OpenLegacyMfcDialog(
            "Dialog_6",
            0x4A,
            Enumerable.Repeat(0, 11).ToArray(),
            string.Empty,
            dataSources);
        var person1 = (LegacyPersonComboBox)dialog6.Session.ComboBox("IDC_COMBO1");
        AssertEqual(LegacyPersonComboKind.Person1, person1.PersonKind, "Dialog_6 Person1 numeric lookup kind");
        person1.ProcessLookupDigit('1', now);
        person1.ProcessLookupDigit('2', now.AddMilliseconds(100));
        person1.ProcessLookupDigit('3', now.AddMilliseconds(200));
        AssertEqual(123, person1.SelectedIndex, "Person1 numeric lookup selects the displayed role number");
        CommitLegacyMfcDialog(dialog6);
        AssertEqual(LegacyMfcDialogDataSources.Per1ListToCode(123), dialog6.Item.IntData[1], "Person1 numeric lookup preserves Per1 encoding");

        var dialog78 = OpenLegacyMfcDialog(
            "Dialog_78",
            78,
            [1, 5, 2, 3, 4, 5, 2, 3, 0, 7, 8],
            string.Empty,
            dataSources);
        var directIndexPerson = (LegacyPersonComboBox)dialog78.Session.ComboBox("IDC_COMBO9");
        directIndexPerson.ProcessLookupDigit('3', now);
        directIndexPerson.ProcessLookupDigit('2', now.AddMilliseconds(100));
        directIndexPerson.ProcessLookupDigit('1', now.AddMilliseconds(200));
        CommitLegacyMfcDialog(dialog78);
        AssertEqual(321, dialog78.Item.IntData[8], "direct-index target person keeps list-index commit semantics");
    }

    private static void RunLegacyScenarioCommandDisplaySmoke(LegacyMfcDialogDataSources dataSources)
    {
        var formatter = new LegacyScenarioCommandDisplayFormatter(dataSources);

        var variableTest = BuildDisplayCommand(0x05, "变量测试", [], string.Empty);
        variableTest.Parameters.Add(new LegacyScenarioCommandParameter { Index = 0, Kind = LegacyScenarioParameterKind.VariableArray, Values = { 20 } });
        variableTest.Parameters.Add(new LegacyScenarioCommandParameter { Index = 1, Kind = LegacyScenarioParameterKind.VariableArray });
        AssertTrue(formatter.FormatCommand(variableTest).Contains("Var20;无", StringComparison.Ordinal), "command 05 display uses old variable-array summary");

        var positionTest = BuildDisplayCommand(0x25, "武将进入指定地点测试", [LegacyMfcDialogDataSources.Per2ListToCode(1025), 13, 12], string.Empty);
        var positionText = formatter.FormatCommand(positionTest);
        AssertTrue(positionText.Contains("13,12", StringComparison.Ordinal), "command 25 display includes coordinates");
        AssertTrue(!positionText.Contains("P0=", StringComparison.Ordinal), "command 25 display hides raw parameter tokens");

        var personCondition = BuildDisplayCommand(0x36, "武将状态测试", [146, 7, 0, 2], string.Empty);
        var conditionText = formatter.FormatCommand(personCondition);
        AssertTrue(conditionText.Contains("HPCur", StringComparison.Ordinal), "command 36 display maps condition name");
        AssertTrue(conditionText.Contains("=", StringComparison.Ordinal), "command 36 display maps compare operator");

        AssertEqual(70, dataSources.WeaponCount, "legacy equipment preview weapon count follows System.ini DefID");
        AssertEqual(39, dataSources.ArmorCount, "legacy equipment preview armor count follows System.ini AssID");
        AssertEqual(147, dataSources.AssistCount, "legacy equipment preview assist count follows System.ini AssID");
        var consumableClassification = dataSources.ItemClassifications.Values.FirstOrDefault(item => item.Kind == ItemKind.Consumable);
        if (consumableClassification != null)
        {
            AssertTrue(
                dataSources.Item[consumableClassification.ItemId].Contains("道具/消耗品-不可装备", StringComparison.Ordinal),
                "legacy item list labels consumables as not equipable");
        }

        var equipmentSet = BuildDisplayCommand(0x48, "战场装备设定", [0, 0, 0, 0, 0, 15], string.Empty);
        var equipmentSetText = formatter.FormatCommand(equipmentSet);
        AssertTrue(equipmentSetText.Contains("122:绝影", StringComparison.Ordinal), "command 48 assist code 15 maps to global item ID122");
        AssertTrue(!equipmentSetText.Contains("132:太平清领道", StringComparison.Ordinal), "command 48 assist preview must not use stale ItemDefenseSum=49 offset");

        var prefixedEquipmentSet = BuildDisplayCommand(0x48, "48:敌方装备设定", [321, 0, 0, 0, 0, 15], string.Empty);
        var prefixedEquipmentSetText = formatter.FormatCommand(prefixedEquipmentSet);
        AssertTrue(prefixedEquipmentSetText.StartsWith("48:敌方装备设定 ", StringComparison.Ordinal), "command display strips duplicate numeric prefix");
        AssertTrue(!prefixedEquipmentSetText.Contains("48:48:", StringComparison.Ordinal), "command display does not duplicate command id prefix");
        AssertTrue(prefixedEquipmentSetText.Contains("321(", StringComparison.Ordinal), "command display includes person name next to Per2 id");

        var rSceneDocument = new LegacyScenarioDocument { FilePath = "R_00.eex" };
        var rScene = new LegacyScenarioScene { SceneIndex = 0 };
        var rSection = new LegacyScenarioSection { SceneIndex = 0, SectionIndex = 0 };
        var rSceneAppearance = BuildDisplayCommand(0x30, "武将出现", [LegacyMfcDialogDataSources.Per2ListToCode(12), 13, 12, 0, 0], string.Empty);
        var rSceneMove = BuildDisplayCommand(0x32, "武将移动", [0, LegacyMfcDialogDataSources.Per2ListToCode(12), 0, 40, 15, 0], string.Empty);
        var rSceneMoveText = formatter.FormatCommand(rSceneMove);
        AssertTrue(rSceneMoveText.Contains("12", StringComparison.Ordinal), "command 32 display keeps data-role person number");
        AssertTrue(!rSceneMoveText.Contains("data角色", StringComparison.Ordinal), "command 32 data-role display matches command 30 person label style");
        AssertTrue(!rSceneMoveText.Contains("战场编号 0", StringComparison.Ordinal), "command 32 display does not misread mode 0 as battle number");
        rSection.Commands.Add(rSceneAppearance);
        rScene.Sections.Add(rSection);
        rSceneDocument.Scenes.Add(rScene);
        var rSceneCandidates = new RSceneDraftService().BuildCommandCandidates(
            rSceneDocument,
            command => formatter.FormatCommand(command, includeIdentity: false),
            command => formatter.FormatValuesPreview(command, maxVisibleValues: 8));
        AssertEqual(1, rSceneCandidates.Count, "R scene visual command candidate count");
        AssertTrue(rSceneCandidates[0].CommandName.Contains("13,12", StringComparison.Ordinal), "R scene candidate command name uses legacy display coordinates");
        AssertTrue(rSceneCandidates[0].ParameterPreview.Contains("13,12", StringComparison.Ordinal), "R scene candidate parameter preview uses legacy display coordinates");
        AssertTrue(!rSceneCandidates[0].ParameterPreview.Contains("P0=", StringComparison.Ordinal), "R scene candidate parameter preview hides raw parameter tokens");

        var variablePersonCode = ScriptVariableValueResolver.EncodePerson2VariableReference(44);
        var variableAppearance = BuildDisplayCommand(0x30, "R scene variable appearance", [variablePersonCode, 7, 8, 0, 0], string.Empty);
        AssertTrue(formatter.FormatCommand(variableAppearance).Contains("V44", StringComparison.Ordinal), "command 30 variable person displays as V-number");
        var variableDocument = new LegacyScenarioDocument { FilePath = "R_01.eex" };
        var variableScene = new LegacyScenarioScene { SceneIndex = 0 };
        var variableSection = new LegacyScenarioSection { SceneIndex = 0, SectionIndex = 0 };
        var variableBackground = BuildDisplayCommand(0x27, "R scene background", [0, 1], string.Empty);
        var variableAssign = BuildDisplayCommand(0x77, "integer variable set", [2, 44, 2, 0, 12], string.Empty);
        variableBackground.CommandIndex = 1;
        variableAssign.CommandIndex = 2;
        variableAppearance.CommandIndex = 3;
        variableSection.Commands.Add(variableBackground);
        variableSection.Commands.Add(variableAssign);
        variableSection.Commands.Add(variableAppearance);
        variableScene.Sections.Add(variableSection);
        variableDocument.Scenes.Add(variableScene);
        var valueResolver = new ScriptVariableValueResolver();
        var variableSnapshot = new RSceneDraftService().BuildStateSnapshot(
            variableSection,
            currentCommandIndex: variableAppearance.CommandIndex,
            (command, _) => valueResolver.BuildSnapshotToCommand(variableDocument, command));
        AssertEqual(1, variableSnapshot.Actors.Count, "R scene variable actor count");
        AssertEqual(12, variableSnapshot.Actors[0].PersonId, "R scene variable actor resolves to data person");
        AssertEqual(44, variableSnapshot.Actors[0].PersonVariableAddress ?? -1, "R scene actor keeps variable address");

        RunBattlefieldDeploymentDisplaySmoke(formatter, dataSources);
    }

    private static void AssertDeploymentDefaultParameterSentinels()
    {
        using var form = new MainForm();
        var scriptDefaults = InvokePrivateResult<IReadOnlyList<LegacyScenarioCommandParameter>>(
            form,
            "CreateDefaultLegacyScriptParameters",
            0x46);
        AssertDefaultDeploymentParameters(scriptDefaults, BattlefieldDeploymentRecordDefinition.Friend, "script command 46 defaults");

        scriptDefaults = InvokePrivateResult<IReadOnlyList<LegacyScenarioCommandParameter>>(
            form,
            "CreateDefaultLegacyScriptParameters",
            0x47);
        AssertDefaultDeploymentParameters(scriptDefaults, BattlefieldDeploymentRecordDefinition.Enemy, "script command 47 defaults");

        scriptDefaults = InvokePrivateResult<IReadOnlyList<LegacyScenarioCommandParameter>>(
            form,
            "CreateDefaultLegacyScriptParameters",
            0x4B);
        AssertTrue(scriptDefaults.All(parameter => parameter.IntValue == 0), "script command 4B defaults keep order slot at 0");

        scriptDefaults = InvokePrivateResult<IReadOnlyList<LegacyScenarioCommandParameter>>(
            form,
            "CreateDefaultLegacyScriptParameters",
            0x4A);
        AssertDefaultForceAllyDeploymentParameters(scriptDefaults, "script command 4A defaults");

        var packageDefaults = InvokePrivateStaticResult<List<LegacyScenarioCommandParameter>>(
            typeof(ModPackageService),
            "CreateDefaultScenarioParameters",
            0x46);
        AssertDefaultDeploymentParameters(packageDefaults, BattlefieldDeploymentRecordDefinition.Friend, "package command 46 defaults");

        packageDefaults = InvokePrivateStaticResult<List<LegacyScenarioCommandParameter>>(
            typeof(ModPackageService),
            "CreateDefaultScenarioParameters",
            0x47);
        AssertDefaultDeploymentParameters(packageDefaults, BattlefieldDeploymentRecordDefinition.Enemy, "package command 47 defaults");

        packageDefaults = InvokePrivateStaticResult<List<LegacyScenarioCommandParameter>>(
            typeof(ModPackageService),
            "CreateDefaultScenarioParameters",
            0x4B);
        AssertTrue(packageDefaults.All(parameter => parameter.IntValue == 0), "package command 4B defaults keep order slot at 0");

        packageDefaults = InvokePrivateStaticResult<List<LegacyScenarioCommandParameter>>(
            typeof(ModPackageService),
            "CreateDefaultScenarioParameters",
            0x4A);
        AssertDefaultForceAllyDeploymentParameters(packageDefaults, "package command 4A defaults");
    }

    private static void AssertDefaultDeploymentParameters(
        IReadOnlyList<LegacyScenarioCommandParameter> parameters,
        BattlefieldDeploymentRecordDefinition definition,
        string label)
    {
        AssertEqual(definition.GroupSize * definition.RecordCount, parameters.Count, label + " count");
        for (var record = 0; record < definition.RecordCount; record++)
        {
            var start = record * definition.GroupSize;
            for (var slot = 0; slot < definition.GroupSize; slot++)
            {
                var expected = slot == definition.PersonIndex ? BattlefieldDeploymentRecordFormatter.EmptyPerson2Code : 0;
                AssertEqual(expected, parameters[start + slot].IntValue, $"{label} record={record} slot={slot}");
            }
        }
    }

    private static void AssertDefaultForceAllyDeploymentParameters(
        IReadOnlyList<LegacyScenarioCommandParameter> parameters,
        string label)
    {
        AssertEqual(11, parameters.Count, label + " count");
        for (var index = 0; index < parameters.Count; index++)
        {
            var expected = index == 0 ? 0 : LegacyMfcDialogDataSources.EmptyPerson1Code;
            AssertEqual(expected, parameters[index].IntValue, $"{label} index={index}");
        }
    }

    private static void RunBattlefieldDeploymentDisplaySmoke(
        LegacyScenarioCommandDisplayFormatter formatter,
        LegacyMfcDialogDataSources dataSources)
    {
        var emptyFriend = BuildDisplayCommand(0x46, "友军出场设定", Enumerable.Repeat(0, 11 * 20).ToArray(), string.Empty);
        var emptyFriendText = formatter.FormatCommand(emptyFriend);
        var emptyFriendPreview = formatter.FormatValuesPreview(emptyFriend, 8);
        AssertDeploymentCommandTitleIsShort(formatter, emptyFriend, "empty command 46 deployment title");
        AssertTrue(emptyFriendPreview.Contains("Valid 20/20", StringComparison.Ordinal), "all-zero command 46 deployment preview counts person 0 rows");
        AssertTrue(!emptyFriendPreview.Contains(BattlefieldDeploymentRecordFormatter.EmptySlotText, StringComparison.Ordinal), "all-zero command 46 deployment preview is not none");

        var sentinelFriendValues = Enumerable.Repeat(0, 11 * 20).ToArray();
        for (var record = 0; record < 20; record++)
        {
            var start = record * 11;
            sentinelFriendValues[start] = -1;
            sentinelFriendValues[start + 4] = -1;
            sentinelFriendValues[start + 8] = -1;
        }
        var sentinelFriend = BuildDisplayCommand(0x46, "友军出场设定", sentinelFriendValues, string.Empty);
        var sentinelFriendText = formatter.FormatCommand(sentinelFriend);
        AssertDeploymentCommandTitleIsShort(formatter, sentinelFriend, "old sentinel command 46 deployment title");
        AssertTrue(formatter.FormatValuesPreview(sentinelFriend, 8).Contains("全空：无", StringComparison.Ordinal), "old sentinel command 46 deployment preview displays as none");
        AssertTrue(!sentinelFriendText.Contains("0号", StringComparison.Ordinal), "old sentinel command 46 deployment does not display slot 0 as person 0");

        var emptyEnemy = BuildDisplayCommand(0x47, "敌军出场设定", Enumerable.Repeat(0, 12 * 80).ToArray(), string.Empty);
        var emptyEnemyText = formatter.FormatCommand(emptyEnemy);
        var emptyEnemyPreview = formatter.FormatValuesPreview(emptyEnemy, 8);
        AssertDeploymentCommandTitleIsShort(formatter, emptyEnemy, "empty command 47 deployment title");
        AssertTrue(emptyEnemyPreview.Contains("Valid 80/80", StringComparison.Ordinal), "all-zero command 47 deployment preview counts person 0 rows");
        AssertTrue(!emptyEnemyPreview.Contains(BattlefieldDeploymentRecordFormatter.EmptySlotText, StringComparison.Ordinal), "all-zero command 47 deployment preview is not none");

        var sentinelEnemyValues = Enumerable.Repeat(0, 12 * 80).ToArray();
        for (var record = 0; record < 80; record++)
        {
            var start = record * 12;
            sentinelEnemyValues[start] = -1;
            sentinelEnemyValues[start + 5] = -1;
            sentinelEnemyValues[start + 9] = -1;
        }
        var sentinelEnemy = BuildDisplayCommand(0x47, "敌军出场设定", sentinelEnemyValues, string.Empty);
        var sentinelEnemyText = formatter.FormatCommand(sentinelEnemy);
        AssertDeploymentCommandTitleIsShort(formatter, sentinelEnemy, "old sentinel command 47 deployment title");
        AssertTrue(formatter.FormatValuesPreview(sentinelEnemy, 8).Contains("全空：无", StringComparison.Ordinal), "old sentinel command 47 deployment preview displays as none");
        AssertTrue(!sentinelEnemyText.Contains("0号", StringComparison.Ordinal), "old sentinel command 47 deployment does not display slot 0 as person 0");

        var mixedFriendValues = BuildBlankDeploymentValues(BattlefieldDeploymentRecordDefinition.Friend);
        mixedFriendValues[0] = LegacyMfcDialogDataSources.Per2ListToCode(12);
        mixedFriendValues[2] = 8;
        mixedFriendValues[3] = 10;
        mixedFriendValues[7] = 1;
        var mixedFriend = BuildDisplayCommand(0x46, "友军出场设定", mixedFriendValues, string.Empty);
        var mixedFriendText = formatter.FormatCommand(mixedFriend);
        AssertDeploymentCommandTitleIsShort(formatter, mixedFriend, "mixed command 46 deployment title");
        var mixedFriendPreview = formatter.FormatValuesPreview(mixedFriend, 8);
        AssertTrue(mixedFriendPreview.Contains("Valid 1/20", StringComparison.Ordinal), "mixed command 46 deployment preview counts non-empty records");
        AssertTrue(!mixedFriendText.Contains("(8,10)", StringComparison.Ordinal), "mixed command 46 deployment title hides coordinate layout");

        var variableFriendValues = BuildBlankDeploymentValues(BattlefieldDeploymentRecordDefinition.Friend);
        variableFriendValues[0] = ScriptVariableValueResolver.EncodePerson2VariableReference(44);
        var variableFriend = BuildDisplayCommand(0x46, "友军出场设定", variableFriendValues, string.Empty);
        var variableFriendText = formatter.FormatCommand(variableFriend);
        AssertDeploymentCommandTitleIsShort(formatter, variableFriend, "command 46 variable person deployment title");
        AssertTrue(formatter.FormatValuesPreview(variableFriend, 8).Contains("Valid 1/20", StringComparison.Ordinal), "command 46 variable person deployment preview is not blank");
        AssertTrue(!variableFriendText.Contains("V44", StringComparison.Ordinal), "command 46 variable person deployment title hides variable reference");

        var mixedEnemyValues = BuildBlankDeploymentValues(BattlefieldDeploymentRecordDefinition.Enemy);
        mixedEnemyValues[0] = LegacyMfcDialogDataSources.Per2ListToCode(13);
        mixedEnemyValues[1] = 1;
        mixedEnemyValues[2] = 0;
        mixedEnemyValues[3] = 9;
        mixedEnemyValues[4] = 11;
        mixedEnemyValues[8] = 4;
        var mixedEnemy = BuildDisplayCommand(0x47, "敌军出场设定", mixedEnemyValues, string.Empty);
        var mixedEnemyText = formatter.FormatCommand(mixedEnemy);
        AssertDeploymentCommandTitleIsShort(formatter, mixedEnemy, "mixed command 47 deployment title");
        var mixedEnemyPreview = formatter.FormatValuesPreview(mixedEnemy, 8);
        AssertTrue(mixedEnemyPreview.Contains("Valid 1/80", StringComparison.Ordinal), "mixed command 47 deployment preview counts non-empty records");
        AssertTrue(!mixedEnemyText.Contains("(9,11)", StringComparison.Ordinal), "mixed command 47 deployment title hides enemy coordinate layout");

        var emptyFriendRow = new BattlefieldDeploymentBlockEditRow(
            BattlefieldDeploymentRecordDefinition.Friend,
            dataSources,
            recordIndex: 0,
            displayOrdinal: 20,
            Enumerable.Repeat(0, 11).ToArray());
        AssertTrue(!emptyFriendRow.IsBlank, "all-zero command 46 edit row is person 0, not blank");
        AssertTrue(!string.Equals(BattlefieldDeploymentRecordFormatter.EmptySlotText, emptyFriendRow.PersonName, StringComparison.Ordinal), "all-zero command 46 edit row person display is not none");
        AssertTrue(emptyFriendRow.TryBuildValues(out _, out _, out _), "empty command 46 edit row can commit original zero values");

        var emptyEnemyRow = new BattlefieldDeploymentBlockEditRow(
            BattlefieldDeploymentRecordDefinition.Enemy,
            dataSources,
            recordIndex: 0,
            displayOrdinal: 60,
            Enumerable.Repeat(0, 12).ToArray());
        AssertTrue(!emptyEnemyRow.IsBlank, "all-zero command 47 edit row is person 0, not blank");
        AssertTrue(!string.Equals(BattlefieldDeploymentRecordFormatter.EmptySlotText, emptyEnemyRow.PersonName, StringComparison.Ordinal), "all-zero command 47 edit row person display is not none");

        var blankFriendValues = Enumerable.Repeat(0, 11).ToArray();
        blankFriendValues[0] = BattlefieldDeploymentRecordFormatter.EmptyPerson2Code;
        var blankFriendRow = new BattlefieldDeploymentBlockEditRow(
            BattlefieldDeploymentRecordDefinition.Friend,
            dataSources,
            recordIndex: 0,
            displayOrdinal: 20,
            blankFriendValues);
        AssertTrue(blankFriendRow.IsBlank, "person -1 command 46 edit row is blank");
        AssertEqual(BattlefieldDeploymentRecordFormatter.EmptySlotText, blankFriendRow.PersonName, "person -1 command 46 edit row person display is none");
        AssertTrue(blankFriendRow.BuildDetailText().Contains(BattlefieldDeploymentRecordFormatter.EmptySlotText, StringComparison.Ordinal), "person -1 command 46 edit row detail labels blank slot");

        var blankEnemyValues = Enumerable.Repeat(0, 12).ToArray();
        blankEnemyValues[0] = BattlefieldDeploymentRecordFormatter.EmptyPerson2Code;
        var blankEnemyRow = new BattlefieldDeploymentBlockEditRow(
            BattlefieldDeploymentRecordDefinition.Enemy,
            dataSources,
            recordIndex: 0,
            displayOrdinal: 60,
            blankEnemyValues);
        AssertTrue(blankEnemyRow.IsBlank, "person -1 command 47 edit row is blank");
        AssertEqual(BattlefieldDeploymentRecordFormatter.EmptySlotText, blankEnemyRow.PersonName, "person -1 command 47 edit row person display is none");

        var validFriendValues = Enumerable.Repeat(0, 11).ToArray();
        validFriendValues[0] = LegacyMfcDialogDataSources.Per2ListToCode(12);
        validFriendValues[2] = 8;
        validFriendValues[3] = 10;
        var validFriendRow = new BattlefieldDeploymentBlockEditRow(
            BattlefieldDeploymentRecordDefinition.Friend,
            dataSources,
            recordIndex: 0,
            displayOrdinal: 20,
            validFriendValues);
        AssertTrue(!validFriendRow.IsBlank, "non-empty command 46 edit row is not blank");
        AssertTrue(!string.Equals("无", validFriendRow.PersonName, StringComparison.Ordinal), "non-empty command 46 edit row keeps person display");

        RunBattlefieldDeploymentTreePreviewParitySmoke(formatter);
    }

    private static void RunBattlefieldDeploymentTreePreviewParitySmoke(LegacyScenarioCommandDisplayFormatter formatter)
    {
        using var form = new MainForm();
        AssertBattlefieldDeploymentTreePreviewParity(form, formatter, 0x46, "友军出场设定", groupSize: 11, recordCount: 20, previewFaction: "友军");
        AssertBattlefieldDeploymentTreePreviewParity(form, formatter, 0x47, "敌军出场设定", groupSize: 12, recordCount: 80, previewFaction: "敌军");
    }

    private static int[] BuildBlankDeploymentValues(BattlefieldDeploymentRecordDefinition definition)
    {
        var values = Enumerable.Repeat(0, definition.GroupSize * definition.RecordCount).ToArray();
        for (var record = 0; record < definition.RecordCount; record++)
        {
            values[record * definition.GroupSize + definition.PersonIndex] = BattlefieldDeploymentRecordFormatter.EmptyPerson2Code;
        }

        return values;
    }

    private static void AssertBattlefieldDeploymentTreePreviewParity(
        MainForm form,
        LegacyScenarioCommandDisplayFormatter formatter,
        int commandId,
        string commandName,
        int groupSize,
        int recordCount,
        string previewFaction)
    {
        var command = new LegacyScenarioCommandNode
        {
            CommandId = commandId,
            CommandName = commandName,
            SceneIndex = 1,
            SectionIndex = 2,
            CommandIndex = commandId,
            FileOffset = 0x1200 + commandId,
            CommandOrdinal = commandId
        };
        for (var i = 0; i < groupSize * recordCount; i++)
        {
            command.Parameters.Add(new LegacyScenarioCommandParameter
            {
                Index = i,
                Kind = LegacyScenarioParameterKind.Word16,
                IntValue = 0
            });
        }
        var row = new ScenarioStructureRow
        {
            NodeType = "Command候选",
            SceneIndex = command.SceneIndex,
            SectionIndex = command.SectionIndex,
            CommandIndex = command.CommandIndex,
            OffsetHex = HexDisplayFormatter.FormatOffset(command.FileOffset),
            CommandId = command.CommandId,
            CommandIdHex = command.CommandIdHex,
            CommandName = command.CommandName
        };
        var itemData = new LegacyScenarioItemData
        {
            Id = command.CommandId,
            Ord = command.CommandOrdinal,
            Command = command,
            UiRow = row
        };
        var node = new TreeNode("stale battlefield text") { Tag = itemData };
        var preview = new BattlefieldPlacedUnit
        {
            TargetKey = $"Scene=1;Section=2;Command={command.CommandIndex};Offset={row.OffsetHex};Id={row.CommandIdHex};Record=0",
            PersonId = 12,
            Name = "preview",
            GridX = 8,
            GridY = 10,
            Faction = previewFaction,
            AiMode = "主动"
        };
        var previewMap = GetPrivateField<Dictionary<string, BattlefieldPlacedUnit>>(form, "_battlefieldScriptPreviewPlacementsByTargetKey");
        previewMap.Clear();
        previewMap[preview.TargetKey] = preview;

        InvokePrivate(form, "ApplyBattlefieldScriptPreviewToNode", node, row);

        var scriptSummary = formatter.FormatCommand(command);
        AssertEqual(scriptSummary, node.Text, "battlefield tree command text matches script editor summary");
        AssertTrue(!node.Text.Contains("地图预览", StringComparison.Ordinal), "battlefield tree command text does not include map preview suffix");
        AssertTrue(!node.Text.Contains("@8,10", StringComparison.Ordinal), "battlefield tree command text does not include coordinate suffix");
        AssertTrue(node.ToolTipText.Contains("地图预览", StringComparison.Ordinal), "battlefield map preview remains available in tooltip");
    }

    private static void AssertDeploymentCommandTitleIsShort(
        LegacyScenarioCommandDisplayFormatter formatter,
        LegacyScenarioCommandNode command,
        string label)
    {
        var text = formatter.FormatCommand(command);
        AssertTrue(text.StartsWith(command.CommandIdHex + ":", StringComparison.Ordinal), label + " starts with command id");
        AssertTrue(text.Contains(command.CommandName, StringComparison.Ordinal), label + " includes command name");
        AssertTrue(!text.Contains("全空", StringComparison.Ordinal), label + " hides blank summary");
        AssertTrue(!text.Contains("有效", StringComparison.Ordinal), label + " hides record count");
        AssertTrue(!text.Contains("Valid", StringComparison.Ordinal), label + " hides ASCII record count");
        AssertTrue(!text.Contains("第", StringComparison.Ordinal), label + " hides record ordinal");
        AssertTrue(!text.Contains("AI=", StringComparison.Ordinal), label + " hides AI detail");
    }

    private static LegacyScenarioCommandNode BuildDisplayCommand(int commandId, string name, IReadOnlyList<int> values, string text)
    {
        var command = new LegacyScenarioCommandNode
        {
            CommandId = commandId,
            CommandName = name
        };
        for (var i = 0; i < values.Count; i++)
        {
            command.Parameters.Add(new LegacyScenarioCommandParameter
            {
                Index = i,
                Kind = LegacyScenarioParameterKind.Word16,
                IntValue = values[i]
            });
        }

        if (!string.IsNullOrEmpty(text))
        {
            command.Parameters.Add(new LegacyScenarioCommandParameter
            {
                Index = values.Count,
                Kind = LegacyScenarioParameterKind.Text,
                Text = text
            });
        }

        return command;
    }

    private static void RunLegacyMfcDialogBehaviorSmoke(LegacyMfcDialogDataSources dataSources)
    {
        var dialog2 = OpenLegacyMfcDialog("Dialog_2", 2, [], "第一行\n第二行", dataSources);
        AssertEqual("第一行" + Environment.NewLine + "第二行", dialog2.Session.GetText("IDC_EDIT1"), "Dialog_2 displays LF text as multiline edit text");
        dialog2.Session.SetText("IDC_EDIT1", "甲\r\n乙");
        CommitLegacyMfcDialog(dialog2);
        AssertEqual("甲\n乙", dialog2.Item.LongCharData, "Dialog_2 strips CR and preserves LF on commit");

        var dialog6 = OpenLegacyMfcDialog("Dialog_6", 6, [0, 1], string.Empty, dataSources);
        AssertEqual(18, dialog6.Session.ListBox("IDC_LIST1").ItemHeight, "Dialog_6 old owner-draw list height");

        var forceAllyZero = OpenLegacyMfcDialog("Dialog_6", 0x4A, Enumerable.Repeat(0, 11).ToArray(), string.Empty, dataSources);
        AssertEqual(0, forceAllyZero.Session.GetComboIndex("IDC_COMBO1"), "Dialog_6 command 4A all-zero first person is person 0");
        AssertTrue(!string.Equals(
            "无",
            forceAllyZero.Session.ComboBox("IDC_COMBO1").SelectedItem?.ToString(),
            StringComparison.Ordinal), "Dialog_6 command 4A all-zero first person is not none");
        CommitLegacyMfcDialog(forceAllyZero);
        AssertTrue(forceAllyZero.Item.IntData.Take(11).All(value => value == 0), "Dialog_6 command 4A commit preserves all-zero person 0 rows");

        var forceAllyBlankData = Enumerable.Repeat(LegacyMfcDialogDataSources.EmptyPerson1Code, 11).ToArray();
        forceAllyBlankData[0] = 0;
        var forceAllyBlank = OpenLegacyMfcDialog("Dialog_6", 0x4A, forceAllyBlankData, string.Empty, dataSources);
        AssertEqual(5120, forceAllyBlank.Session.GetComboIndex("IDC_COMBO1"), "Dialog_6 command 4A person -1 selects none");
        AssertEqual("无", forceAllyBlank.Session.ComboBox("IDC_COMBO1").SelectedItem?.ToString() ?? string.Empty, "Dialog_6 command 4A person -1 display is none");
        CommitLegacyMfcDialog(forceAllyBlank);
        AssertSequenceEqual(forceAllyBlankData, forceAllyBlank.Item.IntData.Take(11).ToArray(), "Dialog_6 command 4A commit preserves -1 person slots");

        var dialog70 = OpenLegacyMfcDialog("Dialog_70", 70, Enumerable.Repeat(0, 11 * 20).ToArray(), string.Empty, dataSources);
        AssertEqual(18, dialog70.Session.ListBox("IDC_LIST1").ItemHeight, "Dialog_70 old owner-draw list height");
        AssertTrue(dialog70.Session.GetListIndex("IDC_LIST1") >= 0, "Dialog_70 selects the first row on init");
        var dialog70FirstRow = dialog70.Session.ListBox("IDC_LIST1").Items[0]?.ToString() ?? string.Empty;
        AssertTrue(!dialog70FirstRow.Contains(BattlefieldDeploymentRecordFormatter.EmptySlotText, StringComparison.Ordinal), "Dialog_70 command 46 all-zero row displays person 0, not none");
        CommitLegacyMfcDialog(dialog70);
        AssertEqual(11 * 20, dialog70.Item.IntData.Count, "Dialog_70 command 46 commit keeps exact 220-slot length");
        AssertTrue(dialog70.Item.IntData.Take(11 * 20).All(value => value == 0), "Dialog_70 command 46 commit preserves all-zero person 0 rows");

        var sentinelDialog70Data = Enumerable.Repeat(0, 11 * 20).ToArray();
        for (var record = 0; record < 20; record++)
        {
            var start = record * 11;
            sentinelDialog70Data[start] = -1;
            sentinelDialog70Data[start + 4] = -1;
            sentinelDialog70Data[start + 8] = -1;
        }
        var sentinelDialog70 = OpenLegacyMfcDialog("Dialog_70", 70, sentinelDialog70Data, string.Empty, dataSources);
        AssertTrue(sentinelDialog70.Session.ListBox("IDC_LIST1").Items[0]?.ToString()?.Contains("无", StringComparison.Ordinal) == true, "Dialog_70 command 46 old sentinel blank row displays none");
        sentinelDialog70.Session.ListBox("IDC_LIST1").SelectedIndex = 1;
        CommitLegacyMfcDialog(sentinelDialog70);
        AssertEqual(11 * 20, sentinelDialog70.Item.IntData.Count, "Dialog_70 command 46 sentinel commit keeps exact 220-slot length");
        AssertSequenceEqual(sentinelDialog70Data, sentinelDialog70.Item.IntData, "Dialog_70 command 46 commit preserves old sentinel blank rows");

        var bloatedDialog70 = OpenLegacyMfcDialog("Dialog_70", 70, Enumerable.Repeat(0, 12 * 20).ToArray(), string.Empty, dataSources);
        CommitLegacyMfcDialog(bloatedDialog70);
        AssertEqual(11 * 20, bloatedDialog70.Item.IntData.Count, "Dialog_70 command 46 commit trims stale 240-slot data");

        var dialog71 = OpenLegacyMfcDialog("Dialog_70", 71, Enumerable.Repeat(0, 12 * 80).ToArray(), string.Empty, dataSources);
        var dialog71FirstRow = dialog71.Session.ListBox("IDC_LIST1").Items[0]?.ToString() ?? string.Empty;
        AssertTrue(!dialog71FirstRow.Contains(BattlefieldDeploymentRecordFormatter.EmptySlotText, StringComparison.Ordinal), "Dialog_70 command 47 all-zero row displays person 0, not none");
        CommitLegacyMfcDialog(dialog71);
        AssertEqual(12 * 80, dialog71.Item.IntData.Count, "Dialog_70 command 47 commit keeps exact 960-slot length");
        AssertTrue(dialog71.Item.IntData.Take(12 * 80).All(value => value == 0), "Dialog_70 command 47 commit preserves all-zero person 0 rows");

        var sentinelDialog71Data = Enumerable.Repeat(0, 12 * 80).ToArray();
        for (var record = 0; record < 80; record++)
        {
            var start = record * 12;
            sentinelDialog71Data[start] = -1;
            sentinelDialog71Data[start + 5] = -1;
            sentinelDialog71Data[start + 9] = -1;
        }
        var sentinelDialog71 = OpenLegacyMfcDialog("Dialog_70", 71, sentinelDialog71Data, string.Empty, dataSources);
        AssertTrue(sentinelDialog71.Session.ListBox("IDC_LIST1").Items[0]?.ToString()?.Contains("无", StringComparison.Ordinal) == true, "Dialog_70 command 47 old sentinel blank row displays none");
        sentinelDialog71.Session.ListBox("IDC_LIST1").SelectedIndex = 1;
        CommitLegacyMfcDialog(sentinelDialog71);
        AssertEqual(12 * 80, sentinelDialog71.Item.IntData.Count, "Dialog_70 command 47 sentinel commit keeps exact 960-slot length");
        AssertSequenceEqual(sentinelDialog71Data, sentinelDialog71.Item.IntData, "Dialog_70 command 47 commit preserves old sentinel blank rows");

        var dialog78 = OpenLegacyMfcDialog("Dialog_78", 78, [1, 5, 2, 3, 4, 5, 2, 3, 123, 7, 8], string.Empty, dataSources);
        AssertEqual(123, dialog78.Session.GetComboIndex("IDC_COMBO9"), "Dialog_78 target person keeps old list-index storage");
        dialog78.Session.SetComboIndex("IDC_COMBO9", 321);
        CommitLegacyMfcDialog(dialog78);
        AssertEqual(321, dialog78.Item.IntData[8], "Dialog_78 commits IDC_COMBO9 with GetCurSel, not Per2List2Code");

        var dialog89 = OpenLegacyMfcDialog("Dialog_89", 89, [1, -1, -7, 2, -8, 3, -9, 1], string.Empty, dataSources);
        AssertEqual("0", dialog89.Session.GetText("IDC_EDIT2"), "Dialog_89 mutates negative weapon level to 0 on init");
        AssertEqual("0", dialog89.Session.GetText("IDC_EDIT4"), "Dialog_89 mutates negative armor level to 0 on init");
        AssertEqual("0", dialog89.Session.GetText("IDC_EDIT6"), "Dialog_89 mutates negative assist level to 0 on init");
        var dialog89SentinelIndex = dataSources.ExtendedItems ? 511 : 255;
        var dialog89NormalIndex = dataSources.ExtendedItems ? 256 : 254;
        dialog89.Session.SetComboIndex("IDC_COMBO1", 255);
        dialog89.Session.SetComboIndex("IDC_COMBO3", dialog89NormalIndex);
        dialog89.Session.SetComboIndex("IDC_COMBO5", dialog89SentinelIndex);
        CommitLegacyMfcDialog(dialog89);
        AssertEqual(-1, dialog89.Item.IntData[1], "Dialog_89 item 1 sentinel maps list index 255 to -1");
        AssertEqual(dialog89NormalIndex, dialog89.Item.IntData[3], "Dialog_89 item 2 keeps non-sentinel item index");
        AssertEqual(-1, dialog89.Item.IntData[5], "Dialog_89 item 3 sentinel maps the current item range terminator to -1");

        var dialog114 = OpenLegacyMfcDialog("Dialog_114", 114, [1], "old", dataSources);
        AssertTrue(dialog114.Session.ListBox("IDC_LIST1").Items.Count > 0, "Dialog_114 initializes template category list");
        dialog114.Session.SetComboIndex("IDC_COMBO1", 0);
        dialog114.Session.TryGetControl<Button>("IDC_BUTTON1", out var button);
        AssertTrue(button != null, "Dialog_114 template button exists");
        button!.PerformClick();
        AssertTrue(dialog114.Session.GetText("IDC_EDIT2").Length > 0, "Dialog_114 template button writes template text");
    }

    private static LegacyMfcDialogFixture OpenLegacyMfcDialog(
        string dialogName,
        int commandId,
        IReadOnlyList<int> intData,
        string text,
        LegacyMfcDialogDataSources dataSources)
    {
        if (!LegacyMfcDialogCatalog.TryGet(dialogName, out var spec))
        {
            throw new InvalidOperationException($"{dialogName} not found in legacy MFC dialog catalog.");
        }

        var item = new LegacyScenarioItemData
        {
            Id = commandId,
            Ord = 0,
            LongCharData = text
        };
        item.IntData.AddRange(intData);
        var controls = BuildLegacyMfcDialogControls(spec);
        var session = new LegacyMfcDialogSession(new LegacyScenarioItemDataAccessor(item), dataSources, controls, 200, 0);
        spec.Initialize?.Invoke(session);
        return new LegacyMfcDialogFixture(item, spec, session, controls);
    }

    private static Dictionary<string, Control> BuildLegacyMfcDialogControls(LegacyMfcDialogSpec spec)
    {
        var controls = new Dictionary<string, Control>(StringComparer.Ordinal);
        foreach (var controlSpec in spec.Controls)
        {
            var control = controlSpec.Kind switch
            {
                LegacyMfcControlKind.Label => new Label { Text = controlSpec.Text },
                LegacyMfcControlKind.TextBox => new TextBox
                {
                    Multiline = controlSpec.Multiline || controlSpec.DialogUnits.Height > 16,
                    ScrollBars = controlSpec.Scrollable ? ScrollBars.Vertical : ScrollBars.None,
                    AcceptsReturn = controlSpec.Multiline,
                    WordWrap = controlSpec.Multiline
                },
                LegacyMfcControlKind.ComboBox => new LegacyPersonComboBox
                {
                    Sorted = controlSpec.Sorted
                },
                LegacyMfcControlKind.CheckBox => new CheckBox { Text = controlSpec.Text },
                LegacyMfcControlKind.ListBox => new ListBox
                {
                    IntegralHeight = false,
                    ItemHeight = controlSpec.ItemHeight > 0 ? controlSpec.ItemHeight : new ListBox().ItemHeight
                },
                LegacyMfcControlKind.Button => new Button { Text = controlSpec.Text },
                _ => new Control()
            };
            RegisterLegacyMfcSmokeControl(controls, controlSpec.Id, control);
        }

        return controls;
    }

    private static void RegisterLegacyMfcSmokeControl(IDictionary<string, Control> controls, string id, Control control)
    {
        if (!controls.ContainsKey(id))
        {
            controls[id] = control;
            return;
        }

        var suffix = 2;
        while (controls.ContainsKey(id + "#" + suffix.ToString(CultureInfo.InvariantCulture)))
        {
            suffix++;
        }
        controls[id + "#" + suffix.ToString(CultureInfo.InvariantCulture)] = control;
    }

    private static void CommitLegacyMfcDialog(LegacyMfcDialogFixture fixture)
    {
        var error = fixture.Spec.Commit?.Invoke(fixture.Session);
        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException($"{fixture.Spec.DialogName} commit failed: {error}");
        }
    }

    private static IReadOnlyDictionary<int, string> LoadOldMfcDialogDispatchMap(string workspaceRoot)
    {
        var path = Path.Combine(workspaceRoot, "老版游戏制作工具", "a新剧本编辑器v0.23", "ccz-SceneEditor-main", "cczEditor2", "cczEditor2View.cpp");
        if (!File.Exists(path))
        {
            path = Path.Combine(workspaceRoot, "工具整合包", "CCZModStudio", "Assets", "LegacyResources", "a新剧本编辑器v0.23", "cczEditor2", "cczEditor2View.cpp");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Cannot locate old cczEditor2View.cpp for legacy MFC dialog smoke.", path);
        }

        var source = File.ReadAllText(path);
        var function = Regex.Match(
            source,
            @"void\s+CcczEditor2View::OnEditModify\(\)\s*\{(?<body>.*?)^\}\s*\r?\n\r?\n\r?\nvoid\s+CcczEditor2View::OnEditAdd",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!function.Success)
        {
            throw new InvalidOperationException("Cannot parse old CcczEditor2View::OnEditModify().");
        }

        var result = new Dictionary<int, string>();
        foreach (Match block in Regex.Matches(
                     function.Groups["body"].Value,
                     @"if\s*\((?<cond>.*?)\)\s*\{\s*(?<dialog>Dialog_\d+)\s+d;\s*d\.DoModal\(\);\s*\}",
                     RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            var dialogName = block.Groups["dialog"].Value;
            var condition = block.Groups["cond"].Value;
            foreach (Match idMatch in Regex.Matches(condition, @"id\s*==\s*(\d+)", RegexOptions.CultureInvariant))
            {
                result[int.Parse(idMatch.Groups[1].Value, CultureInfo.InvariantCulture)] = dialogName;
            }

            foreach (Match rangeMatch in Regex.Matches(condition, @"id\s*>=\s*(\d+)\s*&&\s*id\s*<=\s*(\d+)", RegexOptions.CultureInvariant))
            {
                var start = int.Parse(rangeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                var end = int.Parse(rangeMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                for (var id = start; id <= end; id++)
                {
                    result[id] = dialogName;
                }
            }
        }

        return result;
    }

    private static int CountExplicitLegacyMfcDialogBehaviors(string workspaceRoot)
    {
        var path = Path.Combine(workspaceRoot, "工具整合包", "CCZModStudio", "LegacyMfcDialogCatalog.cs");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Cannot locate LegacyMfcDialogCatalog.cs for explicit behavior count.", path);
        }

        var source = File.ReadAllText(path);
        return Regex.Matches(
                source,
                @"""Dialog_\d+""\s*=>\s*Behavior",
                RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static void AssertEqual<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{description}: expected={expected}, actual={actual}");
        }
    }

    private static void AssertTrue(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
    }

    private static void AssertSequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string description)
    {
        var expectedList = expected.ToList();
        var actualList = actual.ToList();
        if (expectedList.Count != actualList.Count || !expectedList.SequenceEqual(actualList))
        {
            throw new InvalidOperationException($"{description}: expected=[{string.Join(",", expectedList.Take(16))}], actual=[{string.Join(",", actualList.Take(16))}]");
        }
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(instance.GetType().FullName, fieldName);
        return (T)field.GetValue(instance)!;
    }

    private static void InvokePrivate(object instance, string methodName, params object?[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
        method.Invoke(instance, args);
    }

    private static T InvokePrivateResult<T>(object instance, string methodName, params object?[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
        return (T)method.Invoke(instance, args)!;
    }

    private static T InvokePrivateStaticResult<T>(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(type.FullName, methodName);
        return (T)method.Invoke(null, args)!;
    }

    private sealed record LegacyMfcDialogFixture(
        LegacyScenarioItemData Item,
        LegacyMfcDialogSpec Spec,
        LegacyMfcDialogSession Session,
        IReadOnlyDictionary<string, Control> Controls);
}
