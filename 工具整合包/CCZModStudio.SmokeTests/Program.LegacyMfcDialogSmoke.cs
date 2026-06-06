using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;
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

    private static void RunLegacyScenarioCommandDisplaySmoke(LegacyMfcDialogDataSources dataSources)
    {
        var formatter = new LegacyScenarioCommandDisplayFormatter(dataSources);

        var variableTest = BuildDisplayCommand(0x05, "变量测试", [], string.Empty);
        variableTest.Parameters.Add(new LegacyScenarioCommandParameter { Index = 0, Kind = LegacyScenarioParameterKind.VariableArray, Values = { 20 } });
        variableTest.Parameters.Add(new LegacyScenarioCommandParameter { Index = 1, Kind = LegacyScenarioParameterKind.VariableArray });
        AssertTrue(formatter.FormatCommand(variableTest).Contains("Var20;无", StringComparison.Ordinal), "command 0x05 display uses old variable-array summary");

        var positionTest = BuildDisplayCommand(0x25, "武将进入指定地点测试", [LegacyMfcDialogDataSources.Per2ListToCode(1025), 13, 12], string.Empty);
        var positionText = formatter.FormatCommand(positionTest);
        AssertTrue(positionText.Contains("13,12", StringComparison.Ordinal), "command 0x25 display includes coordinates");
        AssertTrue(!positionText.Contains("P0=", StringComparison.Ordinal), "command 0x25 display hides raw parameter tokens");

        var personCondition = BuildDisplayCommand(0x36, "武将状态测试", [146, 7, 0, 2], string.Empty);
        var conditionText = formatter.FormatCommand(personCondition);
        AssertTrue(conditionText.Contains("HPCur", StringComparison.Ordinal), "command 0x36 display maps condition name");
        AssertTrue(conditionText.Contains("=", StringComparison.Ordinal), "command 0x36 display maps compare operator");

        var rSceneDocument = new LegacyScenarioDocument { FilePath = "R_00.eex" };
        var rScene = new LegacyScenarioScene { SceneIndex = 0 };
        var rSection = new LegacyScenarioSection { SceneIndex = 0, SectionIndex = 0 };
        var rSceneAppearance = BuildDisplayCommand(0x30, "武将出现", [LegacyMfcDialogDataSources.Per2ListToCode(12), 13, 12, 0, 0], string.Empty);
        var rSceneMove = BuildDisplayCommand(0x32, "武将移动", [0, LegacyMfcDialogDataSources.Per2ListToCode(12), 0, 40, 15, 0], string.Empty);
        var rSceneMoveText = formatter.FormatCommand(rSceneMove);
        AssertTrue(rSceneMoveText.Contains("12:", StringComparison.Ordinal), "command 0x32 display resolves data-role person slot");
        AssertTrue(!rSceneMoveText.Contains("data角色", StringComparison.Ordinal), "command 0x32 data-role display matches command 0x30 person label style");
        AssertTrue(!rSceneMoveText.Contains("战场编号 0", StringComparison.Ordinal), "command 0x32 display does not misread mode 0 as battle number");
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

        var dialog70 = OpenLegacyMfcDialog("Dialog_70", 70, Enumerable.Repeat(0, 12 * 20).ToArray(), string.Empty, dataSources);
        AssertEqual(18, dialog70.Session.ListBox("IDC_LIST1").ItemHeight, "Dialog_70 old owner-draw list height");
        AssertTrue(dialog70.Session.GetListIndex("IDC_LIST1") >= 0, "Dialog_70 selects the first row on init");

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
                LegacyMfcControlKind.ComboBox => new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    IntegralHeight = false,
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

    private sealed record LegacyMfcDialogFixture(
        LegacyScenarioItemData Item,
        LegacyMfcDialogSpec Spec,
        LegacyMfcDialogSession Session,
        IReadOnlyDictionary<string, Control> Controls);
}
