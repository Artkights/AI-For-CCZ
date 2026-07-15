using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal static class LegacyMfcDialogCatalog
{
    private static readonly IReadOnlyDictionary<string, LegacyMfcDialogSpec> ResourceSpecs = LegacyMfcDialogResourceLoader.LoadFromWorkspace();
    private static readonly IReadOnlyDictionary<int, string> Dialog114Templates = LegacyMfcDialogResourceLoader.LoadDialog114TemplatesFromWorkspace();

    public static bool TryGet(string dialogName, out LegacyMfcDialogSpec spec)
    {
        if (!ResourceSpecs.TryGetValue(dialogName, out var resourceSpec))
        {
            spec = null!;
            return false;
        }

        spec = BuildBehavior(resourceSpec);
        return true;
    }

    private static LegacyMfcDialogSpec BuildBehavior(LegacyMfcDialogSpec resource)
    {
        var behavior = BuildBehaviorCore(resource.DialogName);
        var spec = new LegacyMfcDialogSpec
        {
            DialogName = resource.DialogName,
            ResourceId = resource.ResourceId,
            DialogUnits = resource.DialogUnits,
            Initialize = session =>
            {
                resource.Initialize?.Invoke(session);
                behavior.Initialize?.Invoke(session);
            },
            Commit = behavior.Commit
        };
        spec.Controls.AddRange(resource.Controls);
        return spec;
    }

    private static LegacyMfcDialogSpec BuildBehaviorCore(string dialogName)
        => dialogName switch
        {
            "Dialog_2" => Behavior(TextOnly, s => CommitText(s, "IDC_EDIT1")),
            "Dialog_4" => Behavior(InitDialog4, s => CommitChecks(s, ("IDC_CHECK2", 0))),
            "Dialog_5" => Behavior(InitDialog5, CommitDialog5),
            "Dialog_6" => Behavior(InitDialog6, CommitDialog6),
            "Dialog_9" => Behavior(s => InitEditInts(s, ("IDC_EDIT1", 0)), s => CommitEditInts(s, ("IDC_EDIT1", 0))),
            "Dialog_11" => Behavior(
                s => { InitEditInts(s, ("IDC_EDIT1", 0)); InitCombo(s, "IDC_COMBO1", ["false", "true"], s.Data.GetInt(1)); },
                s => CommitAll(s, CommitEditInts(s, ("IDC_EDIT1", 0)), CommitCombos(s, ("IDC_COMBO1", 1)))),
            "Dialog_15" => Behavior(s => InitCombo(s, "IDC_COMBO1", ["结局1", "结局2", "结局3"], s.Data.GetInt(0)), s => CommitCombos(s, ("IDC_COMBO1", 0))),
            "Dialog_17" => Behavior(s => InitCombo(s, "IDC_COMBO1", s.DataSources.ScenarioFiles(), s.Data.GetInt(0)), s => CommitCombos(s, ("IDC_COMBO1", 0))),
            "Dialog_18" => Behavior(
                s => { InitText(s, "IDC_EDIT1"); InitPer2Combo(s, "IDC_COMBO1", 0); },
                s => CommitAll(s, CommitText(s, "IDC_EDIT1"), CommitPer2Combos(s, ("IDC_COMBO1", 0)))),
            "Dialog_21" => Behavior(
                s => { InitText(s, "IDC_EDIT1"); InitPer2Combo(s, "IDC_COMBO1", 0); InitPer2Combo(s, "IDC_COMBO2", 1); },
                s => CommitAll(s, CommitText(s, "IDC_EDIT1"), CommitPer2Combos(s, ("IDC_COMBO1", 0), ("IDC_COMBO2", 1)))),
            "Dialog_27" => Behavior(
                s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitCombo(s, "IDC_COMBO2", ["false", "true"], s.Data.GetInt(1)); },
                s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitCombos(s, ("IDC_COMBO2", 1)))),
            "Dialog_31" => Behavior(s => InitEditInts(s, ("IDC_EDIT1", 0), ("IDC_EDIT2", 1)), s => CommitEditInts(s, ("IDC_EDIT1", 0), ("IDC_EDIT2", 1))),
            "Dialog_32" => Behavior(s => InitCombo(s, "IDC_COMBO1", s.DataSources.FaceCondition, s.DataSources.FaceCodeToList(s.Data.GetInt(0))), CommitDialog32),
            "Dialog_33" => Behavior(
                s => { InitEditInts(s, ("IDC_EDIT1", 0), ("IDC_EDIT2", 1)); InitExistingCombo(s, "IDC_COMBO1", s.Data.GetInt(2)); InitChecks(s, ("IDC_CHECK1", 3), ("IDC_CHECK3", 4)); },
                s => CommitAll(s, CommitEditInts(s, ("IDC_EDIT1", 0), ("IDC_EDIT2", 1)), CommitCombos(s, ("IDC_COMBO1", 2)), CommitChecks(s, ("IDC_CHECK1", 3), ("IDC_CHECK3", 4)))),
            "Dialog_34" => Behavior(s => InitCombo(s, "IDC_COMBO1", s.DataSources.Movie, s.Data.GetInt(0)), s => CommitCombos(s, ("IDC_COMBO1", 0))),
            "Dialog_35" => Behavior(
                s => { InitSoundCombo(s); InitEditInts(s, ("IDC_EDIT1", 1)); },
                s => CommitAll(s, CommitSoundCombo(s), CommitEditInts(s, ("IDC_EDIT1", 1)))),
            "Dialog_36" => Behavior(s => InitCdCombo(s), CommitCdCombo),
            "Dialog_37" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitEditInts(s, ("IDC_EDIT1", 1), ("IDC_EDIT2", 2)); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitEditInts(s, ("IDC_EDIT1", 1), ("IDC_EDIT2", 2)))),
            "Dialog_38" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitEditInts(s, ("IDC_EDIT1", 1), ("IDC_EDIT2", 2), ("IDC_EDIT3", 3), ("IDC_EDIT4", 4)); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitEditInts(s, ("IDC_EDIT1", 1), ("IDC_EDIT2", 2), ("IDC_EDIT3", 3), ("IDC_EDIT4", 4)))),
            "Dialog_39" => Behavior(InitDialog39, CommitDialog39),
            "Dialog_43" => Behavior(s => InitPer2Combo(s, "IDC_COMBO1", 0), s => CommitPer2Combos(s, ("IDC_COMBO1", 0))),
            "Dialog_44" => Behavior(s => { InitText(s, "IDC_EDIT1"); InitChecks(s, ("IDC_CHECK1", 0), ("IDC_CHECK2", 1), ("IDC_CHECK3", 2)); }, s => CommitAll(s, CommitText(s, "IDC_EDIT1"), CommitChecks(s, ("IDC_CHECK1", 0), ("IDC_CHECK2", 1), ("IDC_CHECK3", 2)))),
            "Dialog_46" => Behavior(InitDialog46, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0), ("IDC_COMBO2", 1)), CommitChecks(s, ("IDC_CHECK1", 2)))),
            "Dialog_48" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitEditInts(s, ("IDC_EDIT1", 1), ("IDC_EDIT2", 2)); InitSentinelCombo(s, "IDC_COMBO3", s.DataSources.Direction, 3, 4); InitSentinelCombo(s, "IDC_COMBO4", s.DataSources.Gesture, 4, 20); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitEditInts(s, ("IDC_EDIT1", 1), ("IDC_EDIT2", 2)), CommitSentinelCombos(s, ("IDC_COMBO3", 3, 4, -1), ("IDC_COMBO4", 4, 20, -1)))),
            "Dialog_49" => Behavior(InitDialog49, CommitDialog49),
            "Dialog_50" => Behavior(InitDialog50, CommitDialog50),
            "Dialog_51" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitSentinelCombo(s, "IDC_COMBO2", s.DataSources.Gesture, 1, 20); InitSentinelCombo(s, "IDC_COMBO3", s.DataSources.Direction, 2, 4); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitSentinelCombos(s, ("IDC_COMBO2", 1, 20, -1), ("IDC_COMBO3", 2, 4, -1)))),
            "Dialog_52" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitSentinelCombo(s, "IDC_COMBO2", s.DataSources.Gesture, 1, 20); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitSentinelCombos(s, ("IDC_COMBO2", 1, 20, -1)))),
            "Dialog_53" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitEditInts(s, ("IDC_EDIT1", 1)); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitEditInts(s, ("IDC_EDIT1", 1)))),
            "Dialog_54" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitCombo(s, "IDC_COMBO2", s.DataSources.PersonCondition, s.Data.GetInt(1)); InitEditInts(s, ("IDC_EDIT1", 2)); InitCombo(s, "IDC_COMBO3", s.DataSources.Compare, s.Data.GetInt(3)); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitCombos(s, ("IDC_COMBO2", 1), ("IDC_COMBO3", 3)), CommitEditInts(s, ("IDC_EDIT1", 2)))),
            "Dialog_55" => Behavior(s => { InitCombo(s, "IDC_COMBO1", ["钱", "剧本编号", "红蓝条"], s.Data.GetInt(0)); InitEditInts(s, ("IDC_EDIT1", 1)); InitCombo(s, "IDC_COMBO2", s.DataSources.Compare, s.Data.GetInt(2)); }, s => CommitAll(s, CommitCombos(s, ("IDC_COMBO1", 0), ("IDC_COMBO2", 2)), CommitEditInts(s, ("IDC_EDIT1", 1)))),
            "Dialog_56" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitCombo(s, "IDC_COMBO2", s.DataSources.PersonCondition, s.Data.GetInt(1)); InitCombo(s, "IDC_COMBO3", s.DataSources.Operate, s.Data.GetInt(2)); InitEditInts(s, ("IDC_EDIT1", 3)); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitCombos(s, ("IDC_COMBO2", 1), ("IDC_COMBO3", 2)), CommitEditInts(s, ("IDC_EDIT1", 3)))),
            "Dialog_58" => Behavior(s => { InitCombo(s, "IDC_COMBO1", ["钱", "剧本编号", "红蓝条"], s.Data.GetInt(0)); InitCombo(s, "IDC_COMBO2", s.DataSources.Operate, s.Data.GetInt(1)); InitEditInts(s, ("IDC_EDIT1", 2)); }, s => CommitAll(s, CommitCombos(s, ("IDC_COMBO1", 0), ("IDC_COMBO2", 1)), CommitEditInts(s, ("IDC_EDIT1", 2)))),
            "Dialog_59" => Behavior(InitDialog59, CommitDialog59),
            "Dialog_60" => Behavior(InitDialog60, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitJoinCondition(s, "IDC_COMBO2", 1), CommitCombos(s, ("IDC_COMBO3", 2)))),
            "Dialog_61" => Behavior(InitDialog61, CommitDialog61),
            "Dialog_62" => Behavior(InitDialog62, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitCombos(s, ("IDC_COMBO2", 1), ("IDC_COMBO3", 2), ("IDC_COMBO4", 3), ("IDC_COMBO5", 4), ("IDC_COMBO6", 5)))),
            "Dialog_63" => Behavior(s => { InitEditInts(s, ("IDC_EDIT1", 0)); InitCombo(s, "IDC_COMBO1", s.DataSources.Compare, s.Data.GetInt(1)); }, s => CommitAll(s, CommitEditInts(s, ("IDC_EDIT1", 0)), CommitCombos(s, ("IDC_COMBO1", 1)))),
            "Dialog_64" => Behavior(s => InitExistingCombo(s, "IDC_COMBO1", s.Data.GetInt(0)), s => CommitCombos(s, ("IDC_COMBO1", 0))),
            "Dialog_65" => Behavior(InitDialog65, CommitDialog65),
            "Dialog_69" => Behavior(InitDialog69, CommitDialog69),
            "Dialog_70" => Behavior(InitDialog70, CommitDialog70),
            "Dialog_75" => Behavior(s => { InitEditInts(s, ("IDC_EDIT1", 0), ("IDC_EDIT2", 1), ("IDC_EDIT3", 2)); InitSentinelCombo(s, "IDC_COMBO1", s.DataSources.Direction, 3, 4); InitChecks(s, ("IDC_CHECK1", 4)); }, s => CommitAll(s, CommitEditInts(s, ("IDC_EDIT1", 0), ("IDC_EDIT2", 1), ("IDC_EDIT3", 2)), CommitSentinelCombos(s, ("IDC_COMBO1", 3, 4, -1)), CommitChecks(s, ("IDC_CHECK1", 4)))),
            "Dialog_76" => Behavior(InitDialog76, CommitDialog76),
            "Dialog_77" => Behavior(InitDialog77, CommitDialog77),
            "Dialog_78" => Behavior(InitDialog78, CommitDialog78),
            "Dialog_79" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitPer2Combo(s, "IDC_COMBO2", 1); InitSentinelCombo(s, "IDC_COMBO3", s.DataSources.Direction, 2, 4); InitChecks(s, ("IDC_CHECK1", 3), ("IDC_CHECK2", 4), ("IDC_CHECK3", 5)); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0), ("IDC_COMBO2", 1)), CommitSentinelCombos(s, ("IDC_COMBO3", 2, 4, -1)), CommitChecks(s, ("IDC_CHECK1", 3), ("IDC_CHECK2", 4), ("IDC_CHECK3", 5)))),
            "Dialog_80" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitSentinelCombo(s, "IDC_COMBO2", s.DataSources.WarGesture, 1, 13); InitChecks(s, ("IDC_CHECK1", 2), ("IDC_CHECK2", 3)); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitSentinelCombos(s, ("IDC_COMBO2", 1, 13, -1)), CommitChecks(s, ("IDC_CHECK1", 2), ("IDC_CHECK2", 3)))),
            "Dialog_82" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitCombo(s, "IDC_COMBO2", s.DataSources.Job, s.Data.GetInt(1)); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitCombos(s, ("IDC_COMBO2", 1)))),
            "Dialog_83" => Behavior(InitDialog83, CommitDialog83),
            "Dialog_86" => Behavior(InitDialog86, s => CommitCombos(s, ("IDC_COMBO1", 0))),
            "Dialog_88" => Behavior(InitDialog88, s => CommitAll(s, CommitCombos(s, ("IDC_COMBO1", 0), ("IDC_COMBO3", 1), ("IDC_COMBO4", 2)), CommitEditInts(s, ("IDC_EDIT1", 3), ("IDC_EDIT2", 4)), CommitChecks(s, ("IDC_CHECK1", 5), ("IDC_CHECK2", 6)))),
            "Dialog_89" => Behavior(InitDialog89, CommitDialog89),
            "Dialog_91" => Behavior(s => { InitEditInts(s, ("IDC_EDIT1", 0), ("IDC_EDIT2", 1), ("IDC_EDIT3", 2), ("IDC_EDIT4", 3)); InitChecks(s, ("IDC_CHECK3", 4)); }, s => CommitAll(s, CommitEditInts(s, ("IDC_EDIT1", 0), ("IDC_EDIT2", 1), ("IDC_EDIT3", 2), ("IDC_EDIT4", 3)), CommitChecks(s, ("IDC_CHECK3", 4)))),
            "Dialog_93" => Behavior(s => { InitCombo(s, "IDC_COMBO1", s.DataSources.Operate, s.Data.GetInt(0)); InitEditInts(s, ("IDC_EDIT1", 1)); }, s => CommitAll(s, CommitCombos(s, ("IDC_COMBO1", 0)), CommitEditInts(s, ("IDC_EDIT1", 1)))),
            "Dialog_96" => Behavior(s => { InitChecks(s, ("IDC_CHECK1", 0)); InitText(s, "IDC_EDIT1"); InitCombo(s, "IDC_COMBO1", s.DataSources.SoloGesture, s.Data.GetInt(1)); }, s => CommitAll(s, CommitChecks(s, ("IDC_CHECK1", 0)), CommitText(s, "IDC_EDIT1"), CommitCombos(s, ("IDC_COMBO1", 1)))),
            "Dialog_99" => Behavior(s => { InitChecks(s, ("IDC_CHECK1", 0), ("IDC_CHECK2", 1)); InitText(s, "IDC_EDIT1"); }, s => CommitAll(s, CommitChecks(s, ("IDC_CHECK1", 0), ("IDC_CHECK2", 1)), CommitText(s, "IDC_EDIT1"))),
            "Dialog_100" => Behavior(s => { InitChecks(s, ("IDC_CHECK1", 0)); InitCombo(s, "IDC_COMBO1", s.DataSources.SoloGesture, s.Data.GetInt(1)); }, s => CommitAll(s, CommitChecks(s, ("IDC_CHECK1", 0)), CommitCombos(s, ("IDC_COMBO1", 1)))),
            "Dialog_101" => Behavior(s => { InitChecks(s, ("IDC_CHECK1", 0), ("IDC_CHECK2", 2)); InitCombo(s, "IDC_COMBO1", s.DataSources.SoloAttack1, s.Data.GetInt(1)); }, s => CommitAll(s, CommitChecks(s, ("IDC_CHECK1", 0), ("IDC_CHECK2", 2)), CommitCombos(s, ("IDC_COMBO1", 1)))),
            "Dialog_102" => Behavior(s => { InitChecks(s, ("IDC_CHECK1", 0), ("IDC_CHECK2", 2)); InitCombo(s, "IDC_COMBO1", s.DataSources.SoloAttack2, s.Data.GetInt(1)); }, s => CommitAll(s, CommitChecks(s, ("IDC_CHECK1", 0), ("IDC_CHECK2", 2)), CommitCombos(s, ("IDC_COMBO1", 1)))),
            "Dialog_103" => Behavior(s => { InitEditInts(s, ("IDC_EDIT1", 0)); InitText(s, "IDC_EDIT2"); }, s => CommitAll(s, CommitEditInts(s, ("IDC_EDIT1", 0)), CommitText(s, "IDC_EDIT2"))),
            "Dialog_104" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitPer2Combo(s, "IDC_COMBO2", 1); InitEditInts(s, ("IDC_EDIT1", 2)); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0), ("IDC_COMBO2", 1)), CommitEditInts(s, ("IDC_EDIT1", 2)))),
            "Dialog_107" => Behavior(InitDialog107, CommitDialog107),
            "Dialog_109" => Behavior(InitDialog109, CommitDialog109),
            "Dialog_111" => Behavior(s => InitCombo(s, "IDC_COMBO1", s.DataSources.Item.Take(256), s.Data.GetInt(0) < 255 ? s.Data.GetInt(0) : 255), CommitDialog111),
            "Dialog_112" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitPer2Combo(s, "IDC_COMBO2", 1); InitEditInts(s, ("IDC_EDIT1", 2)); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0), ("IDC_COMBO2", 1)), CommitEditInts(s, ("IDC_EDIT1", 2)))),
            "Dialog_114" => Behavior(InitDialog114, CommitDialog114),
            "Dialog_115" => Behavior(s => { InitPer2Combo(s, "IDC_COMBO1", 0); InitEditInts(s, ("IDC_EDIT1", 1)); InitCombo(s, "IDC_COMBO2", s.DataSources.Compare, s.Data.GetInt(2)); InitChecks(s, ("IDC_CHECK1", 3), ("IDC_CHECK3", 4), ("IDC_CHECK5", 5), ("IDC_CHECK6", 6), ("IDC_CHECK7", 7)); }, s => CommitAll(s, CommitPer2Combos(s, ("IDC_COMBO1", 0)), CommitEditInts(s, ("IDC_EDIT1", 1)), CommitCombos(s, ("IDC_COMBO2", 2)), CommitChecks(s, ("IDC_CHECK1", 3), ("IDC_CHECK3", 4), ("IDC_CHECK5", 5), ("IDC_CHECK6", 6), ("IDC_CHECK7", 7)))),
            "Dialog_119" => Behavior(s => { InitCombo(s, "IDC_COMBO1", s.DataSources.VariableKind, s.Data.GetInt(0)); InitEditInts(s, ("IDC_EDIT1", 1), ("IDC_EDIT3", 4)); InitCombo(s, "IDC_COMBO2", s.DataSources.Operate2, s.Data.GetInt(2)); InitCombo(s, "IDC_COMBO3", s.DataSources.VariableKind2, s.Data.GetInt(3)); }, s => CommitAll(s, CommitCombos(s, ("IDC_COMBO1", 0), ("IDC_COMBO2", 2), ("IDC_COMBO3", 3)), CommitEditInts(s, ("IDC_EDIT1", 1), ("IDC_EDIT3", 4)))),
            "Dialog_120" => Behavior(s => { InitEditInts(s, ("IDC_EDIT1", 0)); InitExistingCombo(s, "IDC_COMBO1", s.Data.GetInt(1)); InitPer2Combo(s, "IDC_COMBO2", 2); InitCombo(s, "IDC_COMBO3", s.DataSources.AllCondition, s.Data.GetInt(3)); }, s => CommitAll(s, CommitEditInts(s, ("IDC_EDIT1", 0)), CommitCombos(s, ("IDC_COMBO1", 1), ("IDC_COMBO3", 3)), CommitPer2Combos(s, ("IDC_COMBO2", 2)))),
            "Dialog_121" => Behavior(s => { InitCombo(s, "IDC_COMBO1", s.DataSources.VariableKind2, s.Data.GetInt(0)); InitEditInts(s, ("IDC_EDIT1", 1), ("IDC_EDIT3", 4)); InitCombo(s, "IDC_COMBO2", s.DataSources.Compare2, s.Data.GetInt(2)); InitCombo(s, "IDC_COMBO3", s.DataSources.VariableKind2, s.Data.GetInt(3)); }, s => CommitAll(s, CommitCombos(s, ("IDC_COMBO1", 0), ("IDC_COMBO2", 2), ("IDC_COMBO3", 3)), CommitEditInts(s, ("IDC_EDIT1", 1), ("IDC_EDIT3", 4)))),
            _ => Behavior(InitFallback, CommitFallback)
        };

    private static LegacyMfcDialogSpec Behavior(Action<LegacyMfcDialogSession> init)
        => new() { DialogName = string.Empty, ResourceId = string.Empty, Initialize = init, Commit = _ => null };

    private static LegacyMfcDialogSpec Behavior(Action<LegacyMfcDialogSession> init, Func<LegacyMfcDialogSession, string?> commit)
        => new() { DialogName = string.Empty, ResourceId = string.Empty, Initialize = init, Commit = commit };

    private static void InitFallback(LegacyMfcDialogSession session)
    {
        for (var i = 1; i <= 16; i++)
        {
            var id = "IDC_EDIT" + i.ToString(CultureInfo.InvariantCulture);
            if (session.TryGetControl<TextBox>(id, out _))
            {
                session.SetText(id, session.Data.GetInt(i - 1).ToString(CultureInfo.InvariantCulture));
            }
        }

        InitTextIfPresent(session, "IDC_EDIT1");
    }

    private static string? CommitFallback(LegacyMfcDialogSession session)
    {
        for (var i = 1; i <= 16; i++)
        {
            var id = "IDC_EDIT" + i.ToString(CultureInfo.InvariantCulture);
            if (!session.TryGetControl<TextBox>(id, out _)) continue;
            if (!session.TryReadEditInt(id, out var value, out var error)) return error;
            session.Data.SetInt(i - 1, value);
        }
        return null;
    }

    private static void TextOnly(LegacyMfcDialogSession session)
        => InitText(session, "IDC_EDIT1");

    private static void InitTextIfPresent(LegacyMfcDialogSession session, string id)
    {
        if (session.TryGetControl<TextBox>(id, out _))
        {
            InitText(session, id);
        }
    }

    private static void InitText(LegacyMfcDialogSession session, string id)
        => session.SetText(id, session.Data.Text.Replace("\n", Environment.NewLine, StringComparison.Ordinal));

    private static string? CommitText(LegacyMfcDialogSession session, string id)
    {
        session.Data.Text = session.GetText(id).Replace("\r", string.Empty, StringComparison.Ordinal);
        return null;
    }

    private static void InitEditInts(LegacyMfcDialogSession session, params (string ControlId, int Index)[] bindings)
    {
        foreach (var (controlId, index) in bindings)
        {
            session.SetText(controlId, session.Data.GetInt(index).ToString(CultureInfo.InvariantCulture));
        }
    }

    private static string? CommitEditInts(LegacyMfcDialogSession session, params (string ControlId, int Index)[] bindings)
    {
        foreach (var (controlId, index) in bindings)
        {
            if (!session.TryReadEditInt(controlId, out var value, out var error)) return error;
            session.Data.SetInt(index, value);
        }
        return null;
    }

    private static void InitChecks(LegacyMfcDialogSession session, params (string ControlId, int Index)[] bindings)
    {
        foreach (var (controlId, index) in bindings)
        {
            session.SetCheck(controlId, session.Data.GetInt(index) != 0);
        }
    }

    private static string? CommitChecks(LegacyMfcDialogSession session, params (string ControlId, int Index)[] bindings)
    {
        foreach (var (controlId, index) in bindings)
        {
            session.Data.SetInt(index, session.GetCheck(controlId) ? 1 : 0);
        }
        return null;
    }

    private static void InitCombo(LegacyMfcDialogSession session, string controlId, IEnumerable<string> items, int selectedIndex)
        => session.SetComboItems(controlId, items, selectedIndex);

    private static void InitExistingCombo(LegacyMfcDialogSession session, string controlId, int selectedIndex)
        => session.SetComboIndex(controlId, selectedIndex);

    private static string? CommitCombos(LegacyMfcDialogSession session, params (string ControlId, int Index)[] bindings)
    {
        foreach (var (controlId, index) in bindings)
        {
            session.Data.SetInt(index, session.GetComboIndex(controlId));
        }
        return null;
    }

    private static void InitPer2Combo(LegacyMfcDialogSession session, string controlId, int index)
        => session.SetPersonComboItems(
            controlId,
            session.DataSources.Person2,
            LegacyPersonComboKind.Person2,
            LegacyMfcDialogDataSources.Per2CodeToList(session.Data.GetInt(index)));

    private static string? CommitPer2Combos(LegacyMfcDialogSession session, params (string ControlId, int Index)[] bindings)
    {
        foreach (var (controlId, index) in bindings)
        {
            session.Data.SetInt(index, LegacyMfcDialogDataSources.Per2ListToCode(session.GetComboIndex(controlId)));
        }
        return null;
    }

    private static void InitPer1Combo(LegacyMfcDialogSession session, string controlId, int index)
        => session.SetPersonComboItems(
            controlId,
            session.DataSources.Person1,
            LegacyPersonComboKind.Person1,
            LegacyMfcDialogDataSources.Per1CodeToList(session.Data.GetInt(index)));

    private static void InitSentinelCombo(LegacyMfcDialogSession session, string controlId, IEnumerable<string> items, int index, int fallbackIndex)
    {
        var value = session.Data.GetInt(index);
        InitCombo(session, controlId, items, value >= 0 ? value : fallbackIndex);
    }

    private static string? CommitSentinelCombos(LegacyMfcDialogSession session, params (string ControlId, int Index, int SentinelIndex, int SentinelValue)[] bindings)
    {
        foreach (var (controlId, index, sentinelIndex, sentinelValue) in bindings)
        {
            var selected = session.GetComboIndex(controlId);
            session.Data.SetInt(index, selected < sentinelIndex ? selected : sentinelValue);
        }
        return null;
    }

    private static string? CommitAll(LegacyMfcDialogSession session, params string?[] errors)
        => errors.FirstOrDefault(error => !string.IsNullOrEmpty(error));

    private static void InitDialog4(LegacyMfcDialogSession session)
    {
        if (session.Data.Id == 98)
        {
            session.SetText("IDC_CHECK2", "敌方武将");
        }
        InitChecks(session, ("IDC_CHECK2", 0));
    }

    private static void InitDialog5(LegacyMfcDialogSession session)
    {
        session.SetText("IDC_EDIT1", FormatVariableArray(session.Data, 0));
        session.SetText("IDC_EDIT2", FormatVariableArray(session.Data, 25));
    }

    private static string? CommitDialog5(LegacyMfcDialogSession session)
    {
        if (!TryParseVariableArray(session.GetText("IDC_EDIT1"), out var trueValues, out var error)) return error;
        if (!TryParseVariableArray(session.GetText("IDC_EDIT2"), out var falseValues, out error)) return error;
        WriteVariableArray(session.Data, 0, trueValues);
        WriteVariableArray(session.Data, 25, falseValues);
        return null;
    }

    private static string FormatVariableArray(LegacyScenarioItemDataAccessor data, int start)
    {
        var values = new List<string>();
        for (var i = start; i < start + 25; i++)
        {
            var value = data.GetInt(i, -1);
            if (value == -1) break;
            values.Add(value.ToString(CultureInfo.InvariantCulture));
        }
        return values.Count == 0 ? string.Empty : string.Join(",", values) + ",";
    }

    private static bool TryParseVariableArray(string text, out List<int> values, out string error)
    {
        values = [];
        error = string.Empty;
        var source = (text ?? string.Empty) + ",";
        var number = 0;
        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];
            if ((ch < '0' || ch > '9') && ch != ',')
            {
                break;
            }
            if (ch != ',')
            {
                number *= 10;
                number += ch - '0';
                continue;
            }

            if (i > 0 && source[i - 1] == ',') break;
            if (i == 0) break;
            values.Add(number);
            number = 0;
            if (values.Count == 25) break;
        }
        return true;
    }

    private static void WriteVariableArray(LegacyScenarioItemDataAccessor data, int start, IReadOnlyList<int> values)
    {
        data.EnsureIntSize(start + 25);
        var count = Math.Min(25, values.Count);
        for (var i = 0; i < count; i++) data.SetInt(start + i, values[i]);
        for (var i = count; i < 25; i++) data.SetInt(start + i, -1);
    }

    private static void InitDialog6(LegacyMfcDialogSession session)
    {
        var off = session.Data.Id == 6 ? 0 : 1;
        var selectedLine = 0;
        var per = Enumerable.Range(0, 10).Select(i => session.Data.GetInt(i + 2 - off, -1)).ToArray();
        session.SetVisible("IDC_CHECK1", session.Data.Id == 6);
        InitChecks(session, ("IDC_CHECK1", 0));
        InitEditInts(session, ("IDC_EDIT1", 1 - off));
        session.SetListItems("IDC_LIST1", Enumerable.Repeat("强制出场", 5).Concat(Enumerable.Repeat("强制不出场", 5)));
        session.ListBox("IDC_LIST1").ItemHeight = 18;
        InitPer1Combo(session, "IDC_COMBO1", 2 - off);
        var list = session.ListBox("IDC_LIST1");
        list.SelectedIndexChanged += (_, _) =>
        {
            if (selectedLine >= 0 && selectedLine < per.Length)
            {
                per[selectedLine] = LegacyMfcDialogDataSources.Per1ListToCode(session.GetComboIndex("IDC_COMBO1"));
            }
            selectedLine = Math.Max(0, list.SelectedIndex);
            session.SetPersonComboItems("IDC_COMBO1", session.DataSources.Person1, LegacyPersonComboKind.Person1, LegacyMfcDialogDataSources.Per1CodeToList(per[selectedLine]));
        };
        session.BindComboSelectionChanged("IDC_COMBO1", (_, _) =>
        {
            if (selectedLine >= 0 && selectedLine < per.Length)
            {
                per[selectedLine] = LegacyMfcDialogDataSources.Per1ListToCode(session.GetComboIndex("IDC_COMBO1"));
            }
        });
        session.ListBox("IDC_LIST1").Tag = per;
    }

    private static string? CommitDialog6(LegacyMfcDialogSession session)
    {
        var off = session.Data.Id == 6 ? 0 : 1;
        var error = CommitChecks(session, ("IDC_CHECK1", 0)) ?? CommitEditInts(session, ("IDC_EDIT1", 1 - off));
        if (error != null) return error;
        var selected = session.GetListIndex("IDC_LIST1");
        if (session.ListBox("IDC_LIST1").Tag is int[] per)
        {
            if (selected >= 0 && selected < per.Length)
            {
                per[selected] = LegacyMfcDialogDataSources.Per1ListToCode(session.GetComboIndex("IDC_COMBO1"));
            }
            for (var i = 0; i < per.Length; i++)
            {
                session.Data.SetInt(i + 2 - off, per[i]);
            }
        }
        return null;
    }

    private static string? CommitDialog32(LegacyMfcDialogSession session)
    {
        session.Data.SetInt(0, session.DataSources.FaceListToCode(session.GetComboIndex("IDC_COMBO1")));
        return null;
    }

    private static void InitSoundCombo(LegacyMfcDialogSession session)
        => InitCombo(session, "IDC_COMBO1", session.DataSources.SoundItems(), session.DataSources.SoundCodeToList(session.Data.GetInt(0)));

    private static string? CommitSoundCombo(LegacyMfcDialogSession session)
    {
        session.Data.SetInt(0, session.DataSources.SoundListToCode(session.GetComboIndex("IDC_COMBO1")));
        return null;
    }

    private static void InitCdCombo(LegacyMfcDialogSession session)
        => InitCombo(session, "IDC_COMBO1", session.DataSources.CdTracks(), session.Data.GetInt(0) < 255 ? session.Data.GetInt(0) : session.DataSources.CdTrackCount);

    private static string? CommitCdCombo(LegacyMfcDialogSession session)
    {
        var selected = session.GetComboIndex("IDC_COMBO1");
        session.Data.SetInt(0, selected == session.DataSources.CdTrackCount ? 255 : selected);
        return null;
    }

    private static void InitDialog39(LegacyMfcDialogSession session)
    {
        InitExistingCombo(session, "IDC_COMBO1", session.Data.GetInt(0));
        var values = Enumerable.Range(0, 4).Select(i => session.Data.GetInt(i + 1)).ToArray();
        values[0] += 1;
        values[2] += 41;
        session.ComboBox("IDC_COMBO1").Tag = values;
        void Sync()
        {
            var index = session.GetComboIndex("IDC_COMBO1");
            session.SetText("IDC_EDIT1", values[Math.Clamp(index, 0, values.Length - 1)].ToString(CultureInfo.InvariantCulture));
        }
        session.BindComboSelectionChanged("IDC_COMBO1", (_, _) => Sync());
        Sync();
    }

    private static string? CommitDialog39(LegacyMfcDialogSession session)
    {
        if (!session.TryReadEditInt("IDC_EDIT1", out var value, out var error)) return error;
        var selected = session.GetComboIndex("IDC_COMBO1");
        session.Data.SetInt(0, selected);
        var values = session.ComboBox("IDC_COMBO1").Tag as int[] ??
                     Enumerable.Range(0, 4).Select(i => session.Data.GetInt(i + 1)).ToArray();
        for (var i = 0; i < values.Length; i++)
        {
            session.Data.SetInt(i + 1, values[i]);
        }
        session.Data.SetInt(selected + 1, value);
        session.Data.SetInt(1, session.Data.GetInt(1) - 1);
        session.Data.SetInt(3, session.Data.GetInt(3) - 41);
        return null;
    }

    private static void InitDialog46(LegacyMfcDialogSession session)
    {
        session.SetVisible("IDC_CHECK1", session.Data.Id == 46);
        InitPer2Combo(session, "IDC_COMBO1", 0);
        InitPer2Combo(session, "IDC_COMBO2", 1);
        InitChecks(session, ("IDC_CHECK1", 2));
    }

    private static void InitDialog49(LegacyMfcDialogSession session)
    {
        InitExistingCombo(session, "IDC_COMBO1", session.Data.GetInt(0));
        InitPer2Combo(session, "IDC_COMBO3", 1);
        InitCombo(session, "IDC_COMBO4", session.DataSources.Camp, session.Data.GetInt(6));
        InitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 3), ("IDC_EDIT3", 4), ("IDC_EDIT4", 5));
        session.BindComboSelectionChanged("IDC_COMBO1", (_, _) => SyncDialog49Type(session));
        SyncDialog49Type(session);
    }

    private static string? CommitDialog49(LegacyMfcDialogSession session)
        => CommitAll(session,
            CommitCombos(session, ("IDC_COMBO1", 0), ("IDC_COMBO4", 6)),
            CommitPer2Combos(session, ("IDC_COMBO3", 1)),
            CommitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 3), ("IDC_EDIT3", 4), ("IDC_EDIT4", 5)));

    private static void SyncDialog49Type(LegacyMfcDialogSession session)
    {
        var p = session.GetComboIndex("IDC_COMBO1");
        session.SetEnabled(
            ("IDC_COMBO3", p == 0),
            ("IDC_COMBO4", p != 0),
            ("IDC_EDIT1", p != 0),
            ("IDC_EDIT2", p != 0),
            ("IDC_EDIT3", p != 0),
            ("IDC_EDIT4", p != 0));
    }

    private static void InitDialog50(LegacyMfcDialogSession session)
    {
        InitExistingCombo(session, "IDC_COMBO1", session.Data.GetInt(0) == 1 ? 1 : 0);
        InitPer2Combo(session, "IDC_COMBO3", 1);
        InitSentinelCombo(session, "IDC_COMBO4", session.DataSources.Direction, 5, 4);
        InitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 3), ("IDC_EDIT3", 4));
        session.BindComboSelectionChanged("IDC_COMBO1", (_, _) => SyncDialog50Type(session));
        SyncDialog50Type(session);
    }

    private static string? CommitDialog50(LegacyMfcDialogSession session)
        => CommitAll(session,
            CommitCombos(session, ("IDC_COMBO1", 0)),
            CommitPer2Combos(session, ("IDC_COMBO3", 1)),
            CommitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 3), ("IDC_EDIT3", 4)),
            CommitSentinelCombos(session, ("IDC_COMBO4", 5, 4, -1)));

    private static void SyncDialog50Type(LegacyMfcDialogSession session)
    {
        var p = session.GetComboIndex("IDC_COMBO1");
        session.SetEnabled(("IDC_COMBO3", p == 0), ("IDC_EDIT1", p != 0));
    }

    private static void InitDialog59(LegacyMfcDialogSession session)
    {
        InitPer2Combo(session, "IDC_COMBO1", 0);
        InitJoinCondition(session, "IDC_COMBO2", 1);
        InitCombo(session, "IDC_COMBO3", session.DataSources.LevelOffsetItems(), session.DataSources.LevelOffsetCodeToList(session.Data.GetInt(2)));
    }

    private static string? CommitDialog59(LegacyMfcDialogSession session)
        => CommitAll(session, CommitPer2Combos(session, ("IDC_COMBO1", 0)), CommitJoinCondition(session, "IDC_COMBO2", 1), CommitCombos(session, ("IDC_COMBO3", 2)));

    private static void InitDialog60(LegacyMfcDialogSession session)
    {
        InitPer2Combo(session, "IDC_COMBO1", 0);
        InitJoinCondition(session, "IDC_COMBO2", 1);
        InitExistingCombo(session, "IDC_COMBO3", session.Data.GetInt(2));
    }

    private static void InitJoinCondition(LegacyMfcDialogSession session, string controlId, int index)
        => InitCombo(session, controlId, session.DataSources.JoinCondition, session.Data.GetInt(index) == 255 ? 2 : session.Data.GetInt(index));

    private static string? CommitJoinCondition(LegacyMfcDialogSession session, string controlId, int index)
    {
        var selected = session.GetComboIndex(controlId);
        session.Data.SetInt(index, selected == 2 ? 255 : selected);
        return null;
    }

    private static void InitDialog61(LegacyMfcDialogSession session)
    {
        InitCombo(session, "IDC_COMBO1", session.DataSources.Item.Take(256 * (session.DataSources.ExtendedItems ? 2 : 1)), session.Data.GetInt(0) >= 0 ? session.Data.GetInt(0) : 255);
        if (session.Data.GetInt(1) < 0) session.Data.SetInt(1, 0);
        InitEditInts(session, ("IDC_EDIT1", 1));
        InitChecks(session, ("IDC_CHECK1", 2));
        InitPer2Combo(session, "IDC_COMBO3", 3);
    }

    private static string? CommitDialog61(LegacyMfcDialogSession session)
    {
        var item = session.GetComboIndex("IDC_COMBO1");
        session.Data.SetInt(0, item % 256 != 255 ? item : -1);
        return CommitAll(session, CommitEditInts(session, ("IDC_EDIT1", 1)), CommitChecks(session, ("IDC_CHECK1", 2)), CommitPer2Combos(session, ("IDC_COMBO3", 3)));
    }

    private static void InitDialog62(LegacyMfcDialogSession session)
    {
        InitPer2Combo(session, "IDC_COMBO1", 0);
        InitCombo(session, "IDC_COMBO2", ["默认装备", "卸去装备", .. session.DataSources.Item.Take(session.DataSources.WeaponCount)], Math.Max(0, session.Data.GetInt(1)));
        InitCombo(session, "IDC_COMBO4", ["默认装备", "卸去装备", .. session.DataSources.Item.Skip(session.DataSources.WeaponCount).Take(session.DataSources.ArmorCount)], Math.Max(0, session.Data.GetInt(3)));
        InitCombo(session, "IDC_COMBO6", ["默认装备", "卸去装备", .. session.DataSources.Item.Skip(session.DataSources.WeaponCount + session.DataSources.ArmorCount).Take(session.DataSources.AssistCount)], Math.Max(0, session.Data.GetInt(5)));
        InitCombo(session, "IDC_COMBO3", session.DataSources.EquipmentLevelItems(), Math.Max(0, session.Data.GetInt(2)));
        InitCombo(session, "IDC_COMBO5", session.DataSources.EquipmentLevelItems(), Math.Max(0, session.Data.GetInt(4)));
    }

    private static void InitDialog65(LegacyMfcDialogSession session)
    {
        InitCombo(session, "IDC_COMBO1", session.DataSources.Camp, session.Data.GetInt(0));
        InitEditInts(session, ("IDC_EDIT1", 1), ("IDC_EDIT2", 4), ("IDC_EDIT3", 5), ("IDC_EDIT4", 6), ("IDC_EDIT5", 7));
        InitCombo(session, "IDC_COMBO3", session.DataSources.Compare, session.Data.GetInt(2));
        InitExistingCombo(session, "IDC_COMBO4", session.Data.GetInt(3));
        session.BindComboSelectionChanged("IDC_COMBO4", (_, _) => SyncDialog65Type(session));
        SyncDialog65Type(session);
    }

    private static string? CommitDialog65(LegacyMfcDialogSession session)
        => CommitAll(session,
            CommitCombos(session, ("IDC_COMBO1", 0), ("IDC_COMBO3", 2), ("IDC_COMBO4", 3)),
            CommitEditInts(session, ("IDC_EDIT1", 1), ("IDC_EDIT2", 4), ("IDC_EDIT3", 5), ("IDC_EDIT4", 6), ("IDC_EDIT5", 7)));

    private static void SyncDialog65Type(LegacyMfcDialogSession session)
    {
        var enabled = session.GetComboIndex("IDC_COMBO4") != 0;
        session.SetEnabled(
            ("IDC_EDIT2", enabled),
            ("IDC_EDIT3", enabled),
            ("IDC_EDIT4", enabled),
            ("IDC_EDIT5", enabled));
    }

    private static void InitDialog69(LegacyMfcDialogSession session)
    {
        InitChecks(session, ("IDC_CHECK1", 0), ("IDC_CHECK3", 1));
        InitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 4), ("IDC_EDIT6", 6));
        InitCombo(session, "IDC_COMBO1", session.DataSources.LevelOffsetItems(), session.DataSources.LevelOffsetCodeToList(session.Data.GetInt(3)));
        InitPer2Combo(session, "IDC_COMBO5", 5);
        InitPer2Combo(session, "IDC_COMBO7", 7);
        InitCombo(session, "IDC_COMBO8", session.DataSources.Weather, session.Data.GetInt(8));
        InitCombo(session, "IDC_COMBO9", session.DataSources.Weather2, session.Data.GetInt(9));
    }

    private static string? CommitDialog69(LegacyMfcDialogSession session)
        => CommitAll(session,
            CommitChecks(session, ("IDC_CHECK1", 0), ("IDC_CHECK3", 1)),
            CommitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 4), ("IDC_EDIT6", 6)),
            CommitCombos(session, ("IDC_COMBO1", 3), ("IDC_COMBO8", 8), ("IDC_COMBO9", 9)),
            CommitPer2Combos(session, ("IDC_COMBO5", 5), ("IDC_COMBO7", 7)));

    private static void InitDialog70(LegacyMfcDialogSession session)
    {
        var id = session.Data.Id - 70;
        var count = 20 + id * 60;
        var stride = 11 + id;
        var precedingSameCommandCount = session.PrecedingSameCommandCount;
        var dat = Enumerable.Range(0, stride * count).Select(i => session.Data.GetInt(i)).ToArray();
        var originalDat = dat.ToArray();
        var currentLine = 0;
        var loadingRow = false;
        session.SetListItems("IDC_LIST1", Enumerable.Range(0, count).Select(i => Dialog70ListText(session, id, stride, i, dat, precedingSameCommandCount)));
        session.ListBox("IDC_LIST1").ItemHeight = 18;
        session.ListBox("IDC_LIST1").Tag = dat;
        session.ListBox("IDC_LIST1").AccessibleName = string.Join(",", originalDat.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        session.ListBox("IDC_LIST1").AccessibleDescription = "0";
        session.SetVisible("IDC_CHECK1", id != 0);
        session.SetPersonComboItems("IDC_COMBO13", session.DataSources.Person2, LegacyPersonComboKind.Person2, 0);
        LoadDialog70Row(session, id, stride, 0, dat, ref loadingRow);
        session.BindComboSelectionChanged("IDC_COMBO1", (_, _) =>
        {
            if (loadingRow) return;
            currentLine = ClampDialog70Line(currentLine, count);
            dat[currentLine * stride] = LegacyMfcDialogDataSources.Per2ListToCode(session.GetComboIndex("IDC_COMBO1"));
            session.SetListItem("IDC_LIST1", currentLine, Dialog70ListText(session, id, stride, currentLine, dat, precedingSameCommandCount));
        });
        session.BindComboSelectionChanged("IDC_COMBO12", (_, _) => SyncDialog70Policy(session, id, stride, ClampDialog70Line(currentLine, count), dat, updatePolicy: !loadingRow));
        session.BindListSelectionChanged("IDC_LIST1", (_, _) =>
        {
            if (loadingRow) return;
            var nextLine = ClampDialog70Line(session.GetListIndex("IDC_LIST1"), count);
            SaveDialog70Row(session, id, stride, currentLine, dat, originalDat);
            currentLine = nextLine;
            session.ListBox("IDC_LIST1").AccessibleDescription = currentLine.ToString(CultureInfo.InvariantCulture);
            LoadDialog70Row(session, id, stride, currentLine, dat, ref loadingRow);
        });
    }

    private static string? CommitDialog70(LegacyMfcDialogSession session)
    {
        var id = session.Data.Id - 70;
        var listLine = int.TryParse(session.ListBox("IDC_LIST1").AccessibleDescription, NumberStyles.Integer, CultureInfo.InvariantCulture, out var storedLine)
            ? storedLine
            : session.GetListIndex("IDC_LIST1");
        var stride = 11 + id;
        var count = 20 + id * 60;
        listLine = ClampDialog70Line(listLine, count);
        if (session.ListBox("IDC_LIST1").Tag is int[] dat)
        {
            var originalDat = ParseDialog70OriginalData(session, dat.Length);
            var error = SaveDialog70Row(session, id, stride, listLine, dat, originalDat);
            if (error != null) return error;
            var writeCount = stride * count;
            for (var i = 0; i < writeCount; i++)
            {
                session.Data.SetInt(i, dat[i]);
            }
            session.Data.TrimIntSize(writeCount);
            return null;
        }

        return SaveDialog70Row(session, id, stride, listLine, null);
    }

    private static string Dialog70ListText(LegacyMfcDialogSession session, int id, int stride, int line, IReadOnlyList<int> dat, int precedingSameCommandCount)
    {
        var count = 20 + id * 60;
        var ordinal = (20 + 40 * id) + line + precedingSameCommandCount * count;
        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(70 + id);
        if (definition != null)
        {
            var values = dat.Skip(line * stride).Take(stride).ToArray();
            if (BattlefieldDeploymentRecordFormatter.IsBlankRecord(definition, values))
            {
                return $"[{ordinal}]{BattlefieldDeploymentRecordFormatter.EmptySlotText}";
            }
        }

        return $"[{ordinal}]{Safe(session.DataSources.Person2, LegacyMfcDialogDataSources.Per2CodeToList(dat[line * stride]))}";
    }

    private static void LoadDialog70Row(LegacyMfcDialogSession session, int id, int stride, int listLine, int[] dat, ref bool loadingRow)
    {
        loadingRow = true;
        try
        {
            listLine = ClampDialog70Line(listLine, 20 + id * 60);
            var baseIndex = listLine * stride;
            session.SetPersonComboItems("IDC_COMBO1", session.DataSources.Person2, LegacyPersonComboKind.Person2, LegacyMfcDialogDataSources.Per2CodeToList(dat[baseIndex]));
            session.SetCheck("IDC_CHECK1", dat[baseIndex + 1] == 1);
            session.SetCheck("IDC_CHECK4", dat[baseIndex + 1 + id] == 1);
            session.SetText("IDC_EDIT1", dat[baseIndex + 2 + id].ToString(CultureInfo.InvariantCulture));
            session.SetText("IDC_EDIT2", dat[baseIndex + 3 + id].ToString(CultureInfo.InvariantCulture));
            InitCombo(session, "IDC_COMBO5", session.DataSources.Direction, dat[baseIndex + 4 + id] >= 0 ? dat[baseIndex + 4 + id] : 4);
            InitCombo(session, "IDC_COMBO10", session.DataSources.LevelOffsetItems(), session.DataSources.LevelOffsetCodeToList(dat[baseIndex + 5 + id]));
            InitExistingCombo(session, "IDC_COMBO11", dat[baseIndex + 6 + id] >= 0 ? dat[baseIndex + 6 + id] : 2);
            InitCombo(session, "IDC_COMBO12", session.DataSources.Policy, dat[baseIndex + 7 + id] >= 0 ? dat[baseIndex + 7 + id] : 1);
            session.SetPersonComboItems("IDC_COMBO13", session.DataSources.Person2, LegacyPersonComboKind.Person2, LegacyMfcDialogDataSources.Per2CodeToList(dat[baseIndex + 8 + id]));
            session.SetText("IDC_EDIT7", dat[baseIndex + 9 + id].ToString(CultureInfo.InvariantCulture));
            session.SetText("IDC_EDIT8", dat[baseIndex + 10 + id].ToString(CultureInfo.InvariantCulture));
            SyncDialog70Policy(session, id, stride, listLine, dat, updatePolicy: false);
        }
        finally
        {
            loadingRow = false;
        }
    }

    private static void SyncDialog70Policy(LegacyMfcDialogSession session, int id, int stride, int listLine, int[] dat, bool updatePolicy)
    {
        listLine = ClampDialog70Line(listLine, 20 + id * 60);
        var baseIndex = listLine * stride;
        if (updatePolicy)
        {
            dat[baseIndex + 7 + id] = session.GetComboIndex("IDC_COMBO12");
        }
        var policy = dat[baseIndex + 7 + id];
        var showPerson = policy is 3 or 5;
        var showPoint = policy is 4 or 6;
        session.SetVisible(
            ("IDC_STATIC1", showPerson),
            ("IDC_COMBO13", showPerson),
            ("IDC_STATIC3", showPoint),
            ("IDC_EDIT7", showPoint),
            ("IDC_EDIT8", showPoint));
        if (showPerson)
        {
            session.SetComboIndex("IDC_COMBO13", LegacyMfcDialogDataSources.Per2CodeToList(dat[baseIndex + 8 + id]));
        }
        if (showPoint)
        {
            session.SetText("IDC_EDIT7", dat[baseIndex + 9 + id].ToString(CultureInfo.InvariantCulture));
            session.SetText("IDC_EDIT8", dat[baseIndex + 10 + id].ToString(CultureInfo.InvariantCulture));
        }
    }

    private static string? SaveDialog70Row(LegacyMfcDialogSession session, int id, int stride, int listLine, int[]? dat, IReadOnlyList<int>? originalDat = null)
    {
        listLine = ClampDialog70Line(listLine, 20 + id * 60);
        var baseIndex = listLine * stride;
        if (dat != null &&
            originalDat != null &&
            IsDialog70OriginalBlankRowUnchanged(session, id, stride, baseIndex, originalDat))
        {
            for (var offset = 0; offset < stride; offset++)
            {
                dat[baseIndex + offset] = originalDat[baseIndex + offset];
            }
            return null;
        }

        void Set(int index, int value)
        {
            if (dat != null)
            {
                dat[index] = value;
            }
            else
            {
                session.Data.SetInt(index, value);
            }
        }

        Set(baseIndex, LegacyMfcDialogDataSources.Per2ListToCode(session.GetComboIndex("IDC_COMBO1")));
        Set(baseIndex + 1, session.GetCheck("IDC_CHECK1") ? 1 : 0);
        Set(baseIndex + 1 + id, session.GetCheck("IDC_CHECK4") ? 1 : 0);
        if (!session.TryReadEditInt("IDC_EDIT1", out var x, out var error)) return error;
        if (!session.TryReadEditInt("IDC_EDIT2", out var y, out error)) return error;
        if (!session.TryReadEditInt("IDC_EDIT7", out var tx, out error)) return error;
        if (!session.TryReadEditInt("IDC_EDIT8", out var ty, out error)) return error;
        Set(baseIndex + 2 + id, x);
        Set(baseIndex + 3 + id, y);
        var dir = session.GetComboIndex("IDC_COMBO5");
        Set(baseIndex + 4 + id, dir < 4 ? dir : -1);
        Set(baseIndex + 5 + id, session.GetComboIndex("IDC_COMBO10"));
        Set(baseIndex + 6 + id, session.GetComboIndex("IDC_COMBO11"));
        Set(baseIndex + 7 + id, session.GetComboIndex("IDC_COMBO12"));
        Set(baseIndex + 8 + id, LegacyMfcDialogDataSources.Per2ListToCode(session.GetComboIndex("IDC_COMBO13")));
        Set(baseIndex + 9 + id, tx);
        Set(baseIndex + 10 + id, ty);
        return null;
    }

    private static int[] ParseDialog70OriginalData(LegacyMfcDialogSession session, int expectedCount)
    {
        var text = session.ListBox("IDC_LIST1").AccessibleName;
        if (string.IsNullOrWhiteSpace(text))
        {
            return Enumerable.Range(0, expectedCount).Select(session.Data.GetInt).ToArray();
        }

        var values = text.Split(',', StringSplitOptions.None);
        if (values.Length != expectedCount)
        {
            return Enumerable.Range(0, expectedCount).Select(session.Data.GetInt).ToArray();
        }

        var result = new int[expectedCount];
        for (var i = 0; i < values.Length; i++)
        {
            if (!int.TryParse(values[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i]))
            {
                return Enumerable.Range(0, expectedCount).Select(session.Data.GetInt).ToArray();
            }
        }

        return result;
    }

    private static bool IsDialog70OriginalBlankRowUnchanged(
        LegacyMfcDialogSession session,
        int id,
        int stride,
        int baseIndex,
        IReadOnlyList<int> originalDat)
    {
        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(70 + id);
        if (definition == null || baseIndex < 0 || baseIndex + stride > originalDat.Count)
        {
            return false;
        }

        var originalValues = originalDat.Skip(baseIndex).Take(stride).ToArray();
        if (!BattlefieldDeploymentRecordFormatter.IsBlankRecord(definition, originalValues))
        {
            return false;
        }

        if (!session.TryReadEditInt("IDC_EDIT1", out var x, out _)) return false;
        if (!session.TryReadEditInt("IDC_EDIT2", out var y, out _)) return false;
        if (!session.TryReadEditInt("IDC_EDIT7", out var targetX, out _)) return false;
        if (!session.TryReadEditInt("IDC_EDIT8", out var targetY, out _)) return false;

        return session.GetComboIndex("IDC_COMBO1") == LegacyMfcDialogDataSources.Per2CodeToList(originalValues[0]) &&
               session.GetCheck("IDC_CHECK1") == (originalValues[1] == 1) &&
               session.GetCheck("IDC_CHECK4") == (originalValues[1 + id] == 1) &&
               x == originalValues[2 + id] &&
               y == originalValues[3 + id] &&
               session.GetComboIndex("IDC_COMBO5") == (originalValues[4 + id] >= 0 ? originalValues[4 + id] : 4) &&
               session.GetComboIndex("IDC_COMBO10") == session.DataSources.LevelOffsetCodeToList(originalValues[5 + id]) &&
               session.GetComboIndex("IDC_COMBO11") == (originalValues[6 + id] >= 0 ? originalValues[6 + id] : 2) &&
               session.GetComboIndex("IDC_COMBO12") == (originalValues[7 + id] >= 0 ? originalValues[7 + id] : 1) &&
               session.GetComboIndex("IDC_COMBO13") == LegacyMfcDialogDataSources.Per2CodeToList(originalValues[8 + id]) &&
               targetX == originalValues[9 + id] &&
               targetY == originalValues[10 + id];
    }

    private static int ClampDialog70Line(int listLine, int count)
        => Math.Clamp(listLine, 0, Math.Max(0, count - 1));

    private static void InitDialog76(LegacyMfcDialogSession session)
    {
        InitExistingCombo(session, "IDC_COMBO1", session.Data.GetInt(0));
        InitPer2Combo(session, "IDC_COMBO3", 1);
        InitEditInts(session, ("IDC_EDIT1", 2));
        session.BindComboSelectionChanged("IDC_COMBO1", (_, _) => SyncDialog76Type(session));
        SyncDialog76Type(session);
    }

    private static string? CommitDialog76(LegacyMfcDialogSession session)
        => CommitAll(session,
            CommitCombos(session, ("IDC_COMBO1", 0)),
            CommitPer2Combos(session, ("IDC_COMBO3", 1)),
            CommitEditInts(session, ("IDC_EDIT1", 2)));

    private static void SyncDialog76Type(LegacyMfcDialogSession session)
    {
        var p = session.GetComboIndex("IDC_COMBO1");
        session.SetEnabled(("IDC_COMBO3", p == 0), ("IDC_EDIT1", p != 0));
    }

    private static void InitDialog77(LegacyMfcDialogSession session)
    {
        InitExistingCombo(session, "IDC_COMBO1", session.Data.GetInt(0));
        InitPer2Combo(session, "IDC_COMBO3", 1);
        InitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 3), ("IDC_EDIT3", 4), ("IDC_EDIT4", 5), ("IDC_EDIT5", 6), ("IDC_EDIT8", 11), ("IDC_EDIT9", 12));
        InitCombo(session, "IDC_COMBO4", session.DataSources.Camp, session.Data.GetInt(7));
        InitSentinelCombo(session, "IDC_COMBO10", session.DataSources.WarPersonCondition, 8, 6);
        InitSentinelCombo(session, "IDC_COMBO14", session.DataSources.Change, 9, 3);
        InitExistingCombo(session, "IDC_COMBO15", session.Data.GetInt(10) >= 128 ? 1 : 0);
        var debuffValue = session.Data.GetInt(10, -1);
        if (debuffValue < 0) debuffValue = 0;
        var checks = new[] { "IDC_CHECK3", "IDC_CHECK8", "IDC_CHECK9", "IDC_CHECK10", "IDC_CHECK11", "IDC_CHECK12" };
        for (var i = 0; i < checks.Length; i++)
        {
            session.SetText(checks[i], Safe(session.DataSources.Debuff, i));
            session.SetCheck(checks[i], (debuffValue & (1 << (i + 1))) != 0);
        }
        session.BindComboSelectionChanged("IDC_COMBO1", (_, _) => SyncDialog77Type(session));
        SyncDialog77Type(session);
    }

    private static string? CommitDialog77(LegacyMfcDialogSession session)
    {
        var error = CommitAll(session,
            CommitCombos(session, ("IDC_COMBO1", 0), ("IDC_COMBO4", 7)),
            CommitPer2Combos(session, ("IDC_COMBO3", 1)),
            CommitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 3), ("IDC_EDIT3", 4), ("IDC_EDIT4", 5), ("IDC_EDIT5", 6), ("IDC_EDIT8", 11), ("IDC_EDIT9", 12)),
            CommitSentinelCombos(session, ("IDC_COMBO10", 8, 6, -1), ("IDC_COMBO14", 9, 3, -1)));
        if (error != null) return error;
        var value = 0;
        var checks = new[] { "IDC_CHECK3", "IDC_CHECK8", "IDC_CHECK9", "IDC_CHECK10", "IDC_CHECK11", "IDC_CHECK12" };
        for (var i = 0; i < checks.Length; i++)
        {
            if (session.GetCheck(checks[i])) value += 1 << (i + 1);
        }
        value += session.GetComboIndex("IDC_COMBO15") * 128;
        session.Data.SetInt(10, value is 0 or 128 ? -1 : value);
        return null;
    }

    private static void SyncDialog77Type(LegacyMfcDialogSession session)
    {
        var p = session.GetComboIndex("IDC_COMBO1");
        session.SetEnabled(
            ("IDC_COMBO3", p == 0),
            ("IDC_EDIT1", p == 1),
            ("IDC_EDIT2", p == 2),
            ("IDC_EDIT3", p == 2),
            ("IDC_EDIT4", p == 2),
            ("IDC_EDIT5", p == 2),
            ("IDC_COMBO4", p == 2));
    }

    private static void InitDialog78(LegacyMfcDialogSession session)
    {
        InitExistingCombo(session, "IDC_COMBO1", session.Data.GetInt(0) < 255 ? session.Data.GetInt(0) : 0);
        InitPer2Combo(session, "IDC_COMBO3", 1);
        InitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 3), ("IDC_EDIT3", 4), ("IDC_EDIT4", 5), ("IDC_EDIT5", 9), ("IDC_EDIT6", 10));
        InitCombo(session, "IDC_COMBO4", session.DataSources.Camp, session.Data.GetInt(6));
        InitCombo(session, "IDC_COMBO8", session.DataSources.Policy, session.Data.GetInt(7));
        session.SetPersonComboItems("IDC_COMBO9", session.DataSources.Person2, LegacyPersonComboKind.Person2, session.Data.GetInt(8));
        session.BindComboSelectionChanged("IDC_COMBO1", (_, _) => SyncDialog78Type(session));
        session.BindComboSelectionChanged("IDC_COMBO8", (_, _) => SyncDialog78Policy(session));
        SyncDialog78Type(session);
        SyncDialog78Policy(session);
    }

    private static string? CommitDialog78(LegacyMfcDialogSession session)
        => CommitAll(session,
            CommitCombos(session, ("IDC_COMBO1", 0), ("IDC_COMBO4", 6), ("IDC_COMBO8", 7)),
            CommitPer2Combos(session, ("IDC_COMBO3", 1)),
            CommitCombos(session, ("IDC_COMBO9", 8)),
            CommitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 3), ("IDC_EDIT3", 4), ("IDC_EDIT4", 5), ("IDC_EDIT5", 9), ("IDC_EDIT6", 10)));

    private static void SyncDialog78Type(LegacyMfcDialogSession session)
    {
        var p = session.GetComboIndex("IDC_COMBO1");
        session.SetEnabled(
            ("IDC_COMBO3", p == 0),
            ("IDC_EDIT1", p == 1),
            ("IDC_EDIT2", p == 1),
            ("IDC_EDIT3", p == 1),
            ("IDC_EDIT4", p == 1),
            ("IDC_COMBO4", p == 1));
    }

    private static void SyncDialog78Policy(LegacyMfcDialogSession session)
    {
        var p = session.GetComboIndex("IDC_COMBO8");
        var showPerson = p is 3 or 5;
        var showPoint = p is 4 or 6;
        session.SetVisible(
            ("IDC_STATIC1", showPerson),
            ("IDC_COMBO9", showPerson),
            ("IDC_STATIC3", showPoint),
            ("IDC_STATIC4", showPoint),
            ("IDC_EDIT5", showPoint),
            ("IDC_EDIT6", showPoint));
    }

    private static void InitDialog83(LegacyMfcDialogSession session)
    {
        InitExistingCombo(session, "IDC_COMBO1", session.Data.GetInt(0));
        InitPer2Combo(session, "IDC_COMBO3", 1);
        InitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 3), ("IDC_EDIT3", 4), ("IDC_EDIT4", 5));
        InitCombo(session, "IDC_COMBO4", session.DataSources.Camp, session.Data.GetInt(6));
        InitChecks(session, ("IDC_CHECK3", 7));
        session.BindComboSelectionChanged("IDC_COMBO1", (_, _) => SyncDialog83Type(session));
        SyncDialog83Type(session);
    }

    private static string? CommitDialog83(LegacyMfcDialogSession session)
        => CommitAll(session,
            CommitCombos(session, ("IDC_COMBO1", 0), ("IDC_COMBO4", 6)),
            CommitPer2Combos(session, ("IDC_COMBO3", 1)),
            CommitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 3), ("IDC_EDIT3", 4), ("IDC_EDIT4", 5)),
            CommitChecks(session, ("IDC_CHECK3", 7)));

    private static void SyncDialog83Type(LegacyMfcDialogSession session)
    {
        var p = session.GetComboIndex("IDC_COMBO1");
        session.SetEnabled(
            ("IDC_COMBO3", p == 0),
            ("IDC_EDIT1", p == 1),
            ("IDC_EDIT2", p == 1),
            ("IDC_EDIT3", p == 1),
            ("IDC_EDIT4", p == 1),
            ("IDC_COMBO4", p == 1));
    }

    private static void InitDialog86(LegacyMfcDialogSession session)
        => InitCombo(session, "IDC_COMBO1", session.Data.Id == 86 ? session.DataSources.Weather : session.DataSources.Weather2, session.Data.GetInt(0));

    private static void InitDialog88(LegacyMfcDialogSession session)
    {
        InitCombo(session, "IDC_COMBO1", session.DataSources.Object, session.Data.GetInt(0));
        InitExistingCombo(session, "IDC_COMBO3", session.Data.GetInt(1));
        InitCombo(session, "IDC_COMBO4", session.DataSources.Terrain, session.Data.GetInt(2));
        InitEditInts(session, ("IDC_EDIT1", 3), ("IDC_EDIT2", 4));
        InitChecks(session, ("IDC_CHECK1", 5), ("IDC_CHECK2", 6));
    }

    private static void InitDialog89(LegacyMfcDialogSession session)
    {
        if (session.Data.GetInt(2) < 0) session.Data.SetInt(2, 0);
        if (session.Data.GetInt(4) < 0) session.Data.SetInt(4, 0);
        if (session.Data.GetInt(6) < 0) session.Data.SetInt(6, 0);
        InitEditInts(session, ("IDC_EDIT1", 0), ("IDC_EDIT2", 2), ("IDC_EDIT4", 4), ("IDC_EDIT6", 6));
        var items = session.DataSources.Item.Take(256 * (session.DataSources.ExtendedItems ? 2 : 1)).ToList();
        InitCombo(session, "IDC_COMBO1", items, session.Data.GetInt(1) >= 0 ? session.Data.GetInt(1) : 255);
        InitCombo(session, "IDC_COMBO3", items, session.Data.GetInt(3) >= 0 ? session.Data.GetInt(3) : 255);
        InitCombo(session, "IDC_COMBO5", items, session.Data.GetInt(5) >= 0 ? session.Data.GetInt(5) : 255);
        InitChecks(session, ("IDC_CHECK3", 7));
    }

    private static string? CommitDialog89(LegacyMfcDialogSession session)
    {
        var error = CommitEditInts(session, ("IDC_EDIT1", 0), ("IDC_EDIT2", 2), ("IDC_EDIT4", 4), ("IDC_EDIT6", 6));
        if (error != null) return error;
        session.Data.SetInt(1, session.GetComboIndex("IDC_COMBO1") % 256 != 255 ? session.GetComboIndex("IDC_COMBO1") : -1);
        session.Data.SetInt(3, session.GetComboIndex("IDC_COMBO3") % 256 != 255 ? session.GetComboIndex("IDC_COMBO3") : -1);
        session.Data.SetInt(5, session.GetComboIndex("IDC_COMBO5") % 256 != 255 ? session.GetComboIndex("IDC_COMBO5") : -1);
        return CommitChecks(session, ("IDC_CHECK3", 7));
    }

    private static void InitDialog107(LegacyMfcDialogSession session)
    {
        InitEditInts(session, ("IDC_EDIT1", 0), ("IDC_EDIT2", 1));
        InitCombo(session, "IDC_COMBO1", session.DataSources.MagicItems(), session.Data.GetInt(2) < 100 ? session.Data.GetInt(2) : session.Data.GetInt(2) - 100 + session.DataSources.MagicCount);
        InitChecks(session, ("IDC_CHECK1", 3));
    }

    private static string? CommitDialog107(LegacyMfcDialogSession session)
    {
        var selected = session.GetComboIndex("IDC_COMBO1");
        session.Data.SetInt(2, selected < session.DataSources.MagicCount ? selected : selected - session.DataSources.MagicCount + 100);
        return CommitAll(session, CommitEditInts(session, ("IDC_EDIT1", 0), ("IDC_EDIT2", 1)), CommitChecks(session, ("IDC_CHECK1", 3)));
    }

    private static void InitDialog109(LegacyMfcDialogSession session)
    {
        InitExistingCombo(session, "IDC_COMBO1", session.Data.GetInt(0));
        InitPer2Combo(session, "IDC_COMBO3", 1);
        InitPer2Combo(session, "IDC_COMBO4", 3);
        InitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 4), ("IDC_EDIT3", 5));
        InitSentinelCombo(session, "IDC_COMBO8", session.DataSources.Direction, 6, 4);
        InitChecks(session, ("IDC_CHECK1", 7));
        session.BindComboSelectionChanged("IDC_COMBO1", (_, _) => SyncDialog109Type(session));
        SyncDialog109Type(session);
    }

    private static string? CommitDialog109(LegacyMfcDialogSession session)
        => CommitAll(session,
            CommitCombos(session, ("IDC_COMBO1", 0)),
            CommitPer2Combos(session, ("IDC_COMBO3", 1), ("IDC_COMBO4", 3)),
            CommitEditInts(session, ("IDC_EDIT1", 2), ("IDC_EDIT2", 4), ("IDC_EDIT3", 5)),
            CommitSentinelCombos(session, ("IDC_COMBO8", 6, 4, -1)),
            CommitChecks(session, ("IDC_CHECK1", 7)));

    private static void SyncDialog109Type(LegacyMfcDialogSession session)
    {
        var p = session.GetComboIndex("IDC_COMBO1");
        session.SetEnabled(("IDC_COMBO3", p == 0), ("IDC_EDIT1", p != 0));
    }

    private static string? CommitDialog111(LegacyMfcDialogSession session)
    {
        var selected = session.GetComboIndex("IDC_COMBO1");
        session.Data.SetInt(0, selected < 255 ? selected : 65535);
        return null;
    }

    private static void InitDialog114(LegacyMfcDialogSession session)
    {
        InitEditInts(session, ("IDC_EDIT1", 0));
        InitText(session, "IDC_EDIT2");
        InitCombo(session, "IDC_COMBO1", session.DataSources.SpecialCommand, session.Data.GetInt(0) < session.DataSources.SpecialCommand.Count ? session.Data.GetInt(0) : 0);
        InitExistingCombo(session, "IDC_COMBO3", 0);
        if (session.DataSources.HasSpecialSkillCatalog && !session.ComboContains("IDC_COMBO3", "特技"))
        {
            session.AddComboItems("IDC_COMBO3", ["特技"]);
        }
        session.SetListItems("IDC_LIST1", session.DataSources.Dialog114ListItems(0));
        session.BindComboSelectionChanged("IDC_COMBO1", (_, _) => session.SetText("IDC_EDIT1", session.GetComboIndex("IDC_COMBO1").ToString(CultureInfo.InvariantCulture)));
        session.BindComboSelectionChanged("IDC_COMBO3", (_, _) => session.SetListItems("IDC_LIST1", session.DataSources.Dialog114ListItems(session.GetComboIndex("IDC_COMBO3"))));
        session.BindButtonClick("IDC_BUTTON1", (_, _) =>
        {
            if (Dialog114Templates.TryGetValue(session.GetComboIndex("IDC_COMBO1"), out var template))
            {
                session.SetText("IDC_EDIT2", template.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
            }
        });
    }

    private static string? CommitDialog114(LegacyMfcDialogSession session)
        => CommitAll(session, CommitEditInts(session, ("IDC_EDIT1", 0)), CommitText(session, "IDC_EDIT2"));

    private static string Safe(IReadOnlyList<string> values, int index)
        => index >= 0 && index < values.Count ? values[index] : index.ToString(CultureInfo.InvariantCulture);
}
