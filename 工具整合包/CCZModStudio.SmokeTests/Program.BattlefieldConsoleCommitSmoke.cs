using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunBattlefieldConsoleCommitSmoke()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var form = new MainForm();
                form.Show();
                Application.DoEvents();

                var root = Path.Combine(Path.GetTempPath(), "CCZModStudioBattlefieldConsoleCommitSmoke");
                Directory.CreateDirectory(root);
                SetBattlefieldCommitSmokeField(form, "_project", new CczProject
                {
                    WorkspaceRoot = root,
                    GameRoot = root,
                    HexTableXmlPath = string.Empty
                });

                var definition = BattlefieldDeploymentRecordDefinition.Friend;
                var document = new LegacyScenarioDocument { FilePath = Path.Combine(root, "RS", "S_SMOKE.eex") };
                var scene = new LegacyScenarioScene { SceneIndex = 1 };
                var section = new LegacyScenarioSection { SceneIndex = 1, SectionIndex = 0 };
                var command = new LegacyScenarioCommandNode
                {
                    SceneIndex = 1,
                    SectionIndex = 0,
                    CommandIndex = 0,
                    CommandOrdinal = 0,
                    CommandId = 0x46,
                    CommandName = "友军出场设定",
                    FileOffset = 0x200
                };
                for (var i = 0; i < definition.GroupSize * definition.RecordCount; i++)
                {
                    command.Parameters.Add(new LegacyScenarioCommandParameter
                    {
                        Index = i,
                        Kind = LegacyScenarioParameterKind.Word16,
                        IntValue = 0
                    });
                }
                command.Parameters[definition.PersonIndex].IntValue = 12;
                command.Parameters[definition.XIndex].IntValue = 3;
                command.Parameters[definition.YIndex].IntValue = 4;
                command.Parameters[definition.DirectionIndex].IntValue = 2;
                command.Parameters[definition.LevelIndex].IntValue = 0;
                command.Parameters[definition.JobLevelIndex].IntValue = 0;
                command.Parameters[definition.AiIndex].IntValue = 0;
                section.Commands.Add(command);
                scene.Sections.Add(section);
                document.Scenes.Add(scene);

                var targetKey = "Scene=1;Section=0;Command=0;Offset=000200;Id=46;Record=0";
                var unit = new BattlefieldPlacedUnit
                {
                    TargetKey = targetKey,
                    PersonId = 12,
                    PersonRawCode = 12,
                    Name = "提交烟测角色",
                    Faction = "友军",
                    LevelOffset = 0,
                    LevelMode = "初级",
                    AiMode = "被动",
                    Direction = "下",
                    GridX = 8,
                    GridY = 9
                };

                SetBattlefieldCommitSmokeField(form, "_currentBattlefieldLegacyScriptDocument", document);
                SetBattlefieldCommitSmokeField(form, "_currentBattlefieldDocument", new BattlefieldEditorDocument
                {
                    Scenario = new ScenarioFileInfo { FileName = "S_SMOKE.eex", Path = Path.Combine(root, "RS", "S_SMOKE.eex") }
                });
                GetBattlefieldCommitSmokeField<List<BattlefieldPlacedUnit>>(form, "_battlefieldPlacedUnits").Add(unit);
                SetBattlefieldCommitSmokeField(form, "_selectedBattlefieldPlacedUnit", unit);

                var dirtyKindType = typeof(MainForm).GetNestedType("BattlefieldConsoleDirtyKind", BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Missing BattlefieldConsoleDirtyKind.");
                var placementKind = Enum.Parse(dirtyKindType, "Placement");
                SetBattlefieldCommitSmokeField(form, "_battlefieldConsoleDirty", true);
                SetBattlefieldCommitSmokeField(form, "_battlefieldConsoleDirtyTargetKey", targetKey);
                SetBattlefieldCommitSmokeFieldObject(form, "_battlefieldConsoleDirtyKind", placementKind);
                var beforeUnit = new BattlefieldPlacedUnit
                {
                    TargetKey = targetKey,
                    PersonId = 12,
                    PersonRawCode = 12,
                    Name = unit.Name,
                    Faction = unit.Faction,
                    LevelOffset = unit.LevelOffset,
                    LevelMode = unit.LevelMode,
                    AiMode = unit.AiMode,
                    Direction = unit.Direction,
                    GridX = 3,
                    GridY = 4
                };
                SetBattlefieldCommitSmokeField(form, "_battlefieldConsoleBeforeEditSnapshot", beforeUnit);

                var stages = new List<string>();
                MainForm.BattlefieldConsoleCommitStageInterceptForSmoke = stages.Add;
                try
                {
                    var result = InvokeBattlefieldCommitSmokeResult(form, "TryCommitPendingBattlefieldConsoleChangesResult", false);
                    var status = result.GetType().GetProperty("Status")?.GetValue(result)?.ToString();
                    var allowsNavigation = (bool)(result.GetType().GetProperty("AllowsNavigation")?.GetValue(result) ?? false);
                    if (!string.Equals(status, "Committed", StringComparison.Ordinal) || !allowsNavigation)
                    {
                        throw new InvalidOperationException($"Placement-only commit failed: status={status}, allowsNavigation={allowsNavigation}.");
                    }
                    if (stages.Count(stage => stage == "ApplyPlacement") != 1)
                    {
                        throw new InvalidOperationException("Placement-only commit did not apply deployment exactly once.");
                    }
                    if (stages.Any(stage => stage == "ApplyStatus"))
                    {
                        throw new InvalidOperationException("Placement-only commit incorrectly invoked the status writer.");
                    }
                    if (command.Parameters[definition.XIndex].IntValue != 8 ||
                        command.Parameters[definition.YIndex].IntValue != 9)
                    {
                        throw new InvalidOperationException("Placement-only commit did not keep the requested coordinates in the S tree.");
                    }
                    if (GetBattlefieldCommitSmokeField<bool>(form, "_battlefieldConsoleDirty"))
                    {
                        throw new InvalidOperationException("Successful placement-only commit left the console dirty.");
                    }
                    if (GetBattlefieldCommitSmokeField<object?>(form, "_battlefieldUnsyncedDraftState") != null)
                    {
                        throw new InvalidOperationException("Successful placement-only commit created an unsynchronized draft.");
                    }

                    SetBattlefieldCommitSmokeField(form, "_battlefieldConsoleDirty", true);
                    SetBattlefieldCommitSmokeField(form, "_battlefieldConsoleDirtyTargetKey", targetKey);
                    SetBattlefieldCommitSmokeFieldObject(form, "_battlefieldConsoleDirtyKind", placementKind);
                    SetBattlefieldCommitSmokeField(form, "_battlefieldConsoleBeforeEditSnapshot", new BattlefieldPlacedUnit
                    {
                        TargetKey = targetKey,
                        PersonId = unit.PersonId,
                        PersonRawCode = unit.PersonRawCode,
                        Name = unit.Name,
                        Faction = unit.Faction,
                        LevelOffset = unit.LevelOffset,
                        LevelMode = unit.LevelMode,
                        AiMode = unit.AiMode,
                        Direction = unit.Direction,
                        GridX = unit.GridX,
                        GridY = unit.GridY
                    });
                    stages.Clear();
                    result = InvokeBattlefieldCommitSmokeResult(form, "TryCommitPendingBattlefieldConsoleChangesResult", false);
                    status = result.GetType().GetProperty("Status")?.GetValue(result)?.ToString();
                    if (!string.Equals(status, "NoChanges", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Restored placement did not return NoChanges: {status}.");
                    }
                    if (stages.Count != 0)
                    {
                        throw new InvalidOperationException("NoChanges placement invoked a script writer.");
                    }
                    if (GetBattlefieldCommitSmokeField<bool>(form, "_battlefieldConsoleDirty"))
                    {
                        throw new InvalidOperationException("NoChanges placement left the console dirty.");
                    }

                    var wrongPersonUnit = new BattlefieldPlacedUnit
                    {
                        TargetKey = targetKey,
                        PersonId = 13,
                        PersonRawCode = 13,
                        Name = "错误绑定角色",
                        Faction = "友军",
                        LevelMode = "初级",
                        AiMode = "被动",
                        Direction = "下",
                        GridX = 12,
                        GridY = 13
                    };
                    GetBattlefieldCommitSmokeField<List<BattlefieldPlacedUnit>>(form, "_battlefieldPlacedUnits").Remove(unit);
                    GetBattlefieldCommitSmokeField<List<BattlefieldPlacedUnit>>(form, "_battlefieldPlacedUnits").Add(wrongPersonUnit);
                    SetBattlefieldCommitSmokeField(form, "_selectedBattlefieldPlacedUnit", wrongPersonUnit);
                    SetBattlefieldCommitSmokeField(form, "_battlefieldConsoleDirty", true);
                    SetBattlefieldCommitSmokeField(form, "_battlefieldConsoleDirtyTargetKey", targetKey);
                    SetBattlefieldCommitSmokeFieldObject(form, "_battlefieldConsoleDirtyKind", placementKind);
                    SetBattlefieldCommitSmokeField(form, "_battlefieldConsoleBeforeEditSnapshot", new BattlefieldPlacedUnit
                    {
                        TargetKey = targetKey,
                        PersonId = 13,
                        PersonRawCode = 13,
                        Name = wrongPersonUnit.Name,
                        Faction = wrongPersonUnit.Faction,
                        LevelMode = wrongPersonUnit.LevelMode,
                        AiMode = wrongPersonUnit.AiMode,
                        Direction = wrongPersonUnit.Direction,
                        GridX = 8,
                        GridY = 9
                    });
                    stages.Clear();
                    result = InvokeBattlefieldCommitSmokeResult(form, "TryCommitPendingBattlefieldConsoleChangesResult", false);
                    status = result.GetType().GetProperty("Status")?.GetValue(result)?.ToString();
                    allowsNavigation = (bool)(result.GetType().GetProperty("AllowsNavigation")?.GetValue(result) ?? true);
                    if (!string.Equals(status, "WriteFailedRolledBack", StringComparison.Ordinal) || allowsNavigation)
                    {
                        throw new InvalidOperationException($"Stale person binding did not produce a blocking rolled-back result: {status}.");
                    }
                    if (command.Parameters[definition.PersonIndex].IntValue != 12 ||
                        command.Parameters[definition.XIndex].IntValue != 8 ||
                        command.Parameters[definition.YIndex].IntValue != 9)
                    {
                        throw new InvalidOperationException("Stale person binding changed the script record before rollback.");
                    }
                    if (GetBattlefieldCommitSmokeField<object?>(form, "_battlefieldUnsyncedDraftState") == null)
                    {
                        throw new InvalidOperationException("Stale person binding did not retain an unsynchronized draft.");
                    }
                    InvokeBattlefieldCommitSmoke(form, "DetachBattlefieldUnsyncedDraftFromScriptCore");

                    var localDraft = new BattlefieldPlacedUnit
                    {
                        TargetKey = "Placement#S_SMOKE#10,11#12",
                        PersonId = 12,
                        Name = "本地草稿",
                        GridX = 10,
                        GridY = 11,
                        Faction = "友军"
                    };
                    GetBattlefieldCommitSmokeField<List<BattlefieldPlacedUnit>>(form, "_battlefieldPlacedUnits").Add(localDraft);
                    SetBattlefieldCommitSmokeField(form, "_selectedBattlefieldPlacedUnit", localDraft);
                    SetBattlefieldCommitSmokeField(form, "_battlefieldConsoleDirty", true);
                    SetBattlefieldCommitSmokeField(form, "_battlefieldConsoleDirtyTargetKey", localDraft.TargetKey);
                    SetBattlefieldCommitSmokeFieldObject(form, "_battlefieldConsoleDirtyKind", placementKind);
                    stages.Clear();
                    result = InvokeBattlefieldCommitSmokeResult(form, "TryCommitPendingBattlefieldConsoleChangesResult", false);
                    status = result.GetType().GetProperty("Status")?.GetValue(result)?.ToString();
                    allowsNavigation = (bool)(result.GetType().GetProperty("AllowsNavigation")?.GetValue(result) ?? false);
                    if (!string.Equals(status, "DraftOnly", StringComparison.Ordinal) || !allowsNavigation)
                    {
                        throw new InvalidOperationException($"Unbound placement was not treated as navigable DraftOnly: {status}.");
                    }
                    if (stages.Count != 0)
                    {
                        throw new InvalidOperationException("Unbound placement draft invoked a script writer.");
                    }
                    var allowsSave = (bool)InvokeBattlefieldCommitSmokeResult(
                        form,
                        "BattlefieldConsoleCommitAllowsSave",
                        result);
                    if (allowsSave)
                    {
                        throw new InvalidOperationException("DraftOnly placement was incorrectly accepted by the S-script save gate.");
                    }
                }
                finally
                {
                    MainForm.BattlefieldConsoleCommitStageInterceptForSmoke = null;
                }

                form.Close();
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
            throw new InvalidOperationException("Battlefield console commit smoke failed.", failure);
        }
        Console.WriteLine("BATTLEFIELD_CONSOLE_COMMIT_SMOKE_OK");
    }

    private static void SetBattlefieldCommitSmokeField<T>(MainForm form, string name, T value)
        => SetBattlefieldCommitSmokeFieldObject(form, name, value);

    private static void SetBattlefieldCommitSmokeFieldObject(MainForm form, string name, object? value)
    {
        var field = typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing field: " + name);
        field.SetValue(form, value);
    }

    private static T GetBattlefieldCommitSmokeField<T>(MainForm form, string name)
    {
        var field = typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing field: " + name);
        return (T)field.GetValue(form)!;
    }

    private static object InvokeBattlefieldCommitSmokeResult(MainForm form, string name, params object?[] args)
    {
        var method = typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing method: " + name);
        return method.Invoke(form, args) ?? throw new InvalidOperationException(name + " returned null.");
    }

    private static void InvokeBattlefieldCommitSmoke(MainForm form, string name, params object?[] args)
    {
        var method = typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing method: " + name);
        method.Invoke(form, args);
    }
}
