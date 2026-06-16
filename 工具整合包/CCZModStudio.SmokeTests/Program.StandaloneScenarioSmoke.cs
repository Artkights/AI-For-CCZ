using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;

internal partial class Program
{
    static void RunStandaloneScenarioSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var service = new ModPackageService();
        var analysis = service.AnalyzeStandaloneScenarioRequest(
            project,
            "制作一个独立单关：三英战龙帝，包含开场剧情、完整S战斗、敌将台词、胜败条件和战后奖励。",
            "三英战龙帝",
            "standalone-smoke");

        var design = analysis.Design;
        if (design.Roles.Count < 14 ||
            design.EventGraph.Nodes.Count < 8 ||
            design.Battle.Units.Count < 14 ||
            design.Resources.Needs.Count == 0)
        {
            throw new InvalidOperationException(
                $"Standalone analysis incomplete: roles={design.Roles.Count}, nodes={design.EventGraph.Nodes.Count}, units={design.Battle.Units.Count}, resources={design.Resources.Needs.Count}");
        }

        var compiled = service.CompileStandaloneScenarioPackage(project, tables, design, 16);
        var package = compiled.Package;
        if (!ModPackageService.IsForceOpenPackage(package))
        {
            throw new InvalidOperationException("Standalone package did not mark constraint_mode=force_open.");
        }

        if (package.ScenarioPatches.Count < 2 ||
            package.ValidationPlan.SmokeCommands.All(command => !command.Equals("--standalone-scenario-smoke", StringComparison.OrdinalIgnoreCase)) ||
            compiled.Playability.Risks.Count != 0)
        {
            throw new InvalidOperationException(
                $"Standalone compile incomplete: patches={package.ScenarioPatches.Count}, smokes={package.ValidationPlan.SmokeCommands.Count}, risks={string.Join("|", compiled.Playability.Risks)}");
        }

        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(dictionaryPath))
        {
            throw new FileNotFoundException("Standalone scenario smoke requires CczString.ini.", dictionaryPath);
        }

        var dictionary = new SceneStringParser().Parse(dictionaryPath);
        var preview = service.Preview(project, tables, package, dictionary, allowStructuralScenarioWrites: true);
        if (!preview.CanApply)
        {
            var issues = string.Join(" | ", preview.Issues.Take(8).Select(issue => $"[{issue.Severity}] {issue.Category}:{issue.Message}"));
            throw new InvalidOperationException("Standalone force-open package preview failed: " + issues);
        }

        var hasJump = preview.Changes.Any(change => change.Field.Contains("11", StringComparison.OrdinalIgnoreCase));
        var hasDeployment = preview.Changes.Any(change =>
            change.Field.Contains("44", StringComparison.OrdinalIgnoreCase) ||
            change.Field.Contains("46", StringComparison.OrdinalIgnoreCase) ||
            change.Field.Contains("47", StringComparison.OrdinalIgnoreCase));
        var hasVictoryText = preview.Changes.Any(change => change.NewValue.Contains("胜利", StringComparison.OrdinalIgnoreCase));
        if (!hasJump || !hasDeployment || !hasVictoryText)
        {
            throw new InvalidOperationException($"Standalone preview missed playability signals: jump={hasJump}, deployment={hasDeployment}, victory={hasVictoryText}");
        }

        var scenarioPath = package.ScenarioPatches.First().RelativePath;
        var forceOpenUnsafePreview = service.PreviewScenarioPatch(project, new ModScenarioPatch
        {
            PatchId = "force-open-non-whitelist",
            RelativePath = scenarioPath,
            Operations =
            {
                new ModScenarioPatchOperation
                {
                    Operation = "append_command",
                    SceneIndex = 1,
                    SectionIndex = 1,
                    Commands =
                    {
                        new ModScenarioCommandDraft
                        {
                            CommandId = 0x7F,
                            Note = "force_open smoke non-whitelisted command"
                        }
                    }
                }
            }
        }, dictionary, allowStructuralScenarioWrites: true, forceOpenScenarioWrites: true);
        if (!forceOpenUnsafePreview.CanApply)
        {
            throw new InvalidOperationException("force_open did not allow a non-whitelisted draft command.");
        }

        Console.WriteLine($"STANDALONE_SCENARIO_SMOKE_OK roles={design.Roles.Count} units={design.Battle.Units.Count} nodes={design.EventGraph.Nodes.Count} changes={preview.Changes.Count}");
    }
}
