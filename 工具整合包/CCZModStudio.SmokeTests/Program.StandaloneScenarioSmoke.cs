using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;

internal partial class Program
{
    private static readonly string StandaloneGoldenPrompt =
        "\u6211\u8981\u5236\u4F5C\u4E00\u4E2A\u66F9\u64CD\u4F20\u52A0\u5F3A\u7248 6.5 \u672A\u52A0\u5BC6\u57FA\u5E95\u7684\u72EC\u7ACB\u5355\u5173 MOD\uFF1A\u4E09\u82F1\u6218\u3002\n" +
        "\u6545\u4E8B\uFF1A\u5317\u9099\u5C71\u4E0B\uFF0C\u9F99\u5E1D\u4EE5\u4E07\u4EBA\u8840\u796D\u5F00\u542F\u9B54\u9635\u3002\n" +
        "\u89D2\u8272\uFF1A\u5CB3\u591C\uFF0C\u5C71\u8D3C\u738B\u8005\uFF0C\u864E\u5578\u5C71\u6797\uFF0C\u667A\u52C7\u53CC\u5168\uFF1B\u8944\u614E\uFF0C\u6D77\u4E0A\u9526\u5E06\uFF0C\u527D\u63A0\u56DB\u65B9\uFF1B\u5218\u751F\uFF0C\u5C11\u6797\u626B\u5730\u50E7\uFF0C\u624B\u6301\u5218\u751F\u626B\u628A\uFF1B\u9F99\u5E1D\uFF0C\u7978\u4E71\u5929\u4E0B\u7684\u5927\u9B54\u738B\uFF0C\u6B8B\u66B4\u55DC\u8840\u3002\n" +
        "\u8981\u6C42\uFF1A4 \u540D\u6211\u65B9\u30011 \u540D\u53CB\u519B\u300110 \u540D\u654C\u519B\uFF1B\u5FC5\u987B\u6709 R \u5F00\u573A\u3001R \u8DF3 S\u3001S \u90E8\u7F72\u3001AI\u3001\u80DC\u8D25\u6761\u4EF6\u3001\u81F3\u5C11 2 \u4E2A\u6218\u4E2D\u4E8B\u4EF6\u3001\u9F99\u5E1D\u53F0\u8BCD\u3001\u80DC\u5229\u5267\u60C5\u3001\u5931\u8D25\u5206\u652F\u3001\u5956\u52B1\u7ED3\u7B97\u3001\u6218\u540E\u6536\u675F\u3002\n" +
        "\u5730\u56FE\uFF1A\u5BFC\u5165 Map/M003.JPG\uFF0CHexzmap M003 \u5C3D\u91CF\u540C\u6B65\u3002";

    static void RunStandaloneScenarioSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var service = new ModPackageService();
        var analysis = service.AnalyzeStandaloneScenarioRequest(
            project,
            StandaloneGoldenPrompt,
            "\u4E09\u82F1\u6218",
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

        foreach (var expectedName in new[] { "\u5CB3\u591C", "\u8944\u614E", "\u5218\u751F", "\u9F99\u5E1D" })
        {
            if (design.Roles.All(role => !role.Name.Equals(expectedName, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Standalone role parser missed expected role: " + expectedName);
            }
        }

        foreach (var badName in new[] { "\u5C71\u8D3C\u738B\u8005", "\u864E\u5578\u5C71\u6797", "\u667A\u52C7\u53CC\u5168" })
        {
            if (design.Roles.Any(role => role.Name.Equals(badName, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Standalone role parser treated a description as a role name: " + badName);
            }
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

        var typedEvents = package.ScenarioPatches
            .SelectMany(patch => patch.Operations)
            .SelectMany(operation => operation.Commands)
            .Where(command => command.Note.Contains("typed_event:", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (typedEvents.Count < 5 ||
            typedEvents.Any(command => command.CommandId == 0x14 && command.Note.Contains("turn_start", StringComparison.OrdinalIgnoreCase)) ||
            typedEvents.Any(command => command.CommandId == 0x14 && command.Note.Contains("area_entered", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Standalone compiler did not emit typed event command drafts.");
        }

        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(dictionaryPath))
        {
            throw new FileNotFoundException("Standalone scenario smoke requires CczString.ini.", dictionaryPath);
        }

        var dictionary = new SceneStringParser().Parse(dictionaryPath);
        var preview = service.Preview(project, tables, package, dictionary, allowStructuralScenarioWrites: true);
        var blockingIssues = preview.Issues
            .Where(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase) ||
                            issue.Severity.Equals("blocked", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (blockingIssues.Any(issue => !issue.Category.Equals("resource", StringComparison.OrdinalIgnoreCase)))
        {
            var issues = string.Join(" | ", blockingIssues.Where(issue => !issue.Category.Equals("resource", StringComparison.OrdinalIgnoreCase)).Take(8).Select(issue => $"[{issue.Severity}] {issue.Category}:{issue.Message}"));
            throw new InvalidOperationException("Standalone force-open package preview failed: " + issues);
        }

        if (!preview.PlayableTier.Equals("draft", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Standalone preview should remain draft while final resources are missing.");
        }

        if (preview.Issues.All(issue => !issue.Category.Equals("resource", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Standalone preview did not flag missing final resource tasks.");
        }

        var compiledCommands = package.ScenarioPatches
            .SelectMany(patch => patch.Operations)
            .SelectMany(operation => operation.Commands)
            .ToList();
        var hasJump = compiledCommands.Any(command => command.CommandId == 0x11);
        var hasDeployment = compiledCommands.Any(command => command.CommandId is 0x44 or 0x46 or 0x47);
        var hasVictoryEvent = typedEvents.Any(command => command.Note.Contains("typed_event:victory", StringComparison.OrdinalIgnoreCase));
        if (!hasJump || !hasDeployment || !hasVictoryEvent)
        {
            throw new InvalidOperationException($"Standalone preview missed playability signals: jump={hasJump}, deployment={hasDeployment}, victory={hasVictoryEvent}");
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

        Console.WriteLine($"STANDALONE_SCENARIO_SMOKE_OK roles={design.Roles.Count} units={design.Battle.Units.Count} nodes={design.EventGraph.Nodes.Count} changes={preview.Changes.Count} tier={preview.PlayableTier}");
    }

    static void RunStandaloneSemanticSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var service = new ModPackageService();
        var analysis = service.AnalyzeStandaloneScenarioRequest(project, StandaloneGoldenPrompt, "\u4E09\u82F1\u6218", "standalone-semantic-smoke");
        var compiled = service.CompileStandaloneScenarioPackage(project, tables, analysis.Design, 16);
        var commands = compiled.Package.ScenarioPatches
            .SelectMany(patch => patch.Operations)
            .SelectMany(operation => operation.Commands)
            .ToList();
        var typedEvents = commands.Where(command => command.Note.Contains("typed_event:", StringComparison.OrdinalIgnoreCase)).ToList();
        var required = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["jump_to_battle"] = 0x11,
            ["deploy"] = 0x5A,
            ["turn_start"] = 0x77,
            ["area_entered"] = 0x12,
            ["unit_defeated"] = 0x13,
            ["victory"] = 0x11,
            ["defeat"] = 0x11,
            ["reward"] = 0x72
        };
        foreach (var (kind, commandId) in required)
        {
            if (typedEvents.All(command => !command.Note.Contains("typed_event:" + kind, StringComparison.OrdinalIgnoreCase) || command.CommandId != commandId))
            {
                throw new InvalidOperationException($"Standalone semantic smoke missed typed event command: {kind}/{commandId:X2}.");
            }
        }

        if (typedEvents.Any(command => command.CommandId == 0x14 && (command.Note.Contains("turn_start", StringComparison.OrdinalIgnoreCase) || command.Note.Contains("area_entered", StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("Standalone semantic smoke found a text command pretending to be a battle trigger.");
        }

        if (commands.Any(command => command.Parameters.Any(parameter => string.IsNullOrWhiteSpace(parameter.Kind) || !parameter.LayoutCode.HasValue)))
        {
            throw new InvalidOperationException("Standalone semantic smoke found a command parameter without kind/layout evidence.");
        }

        Console.WriteLine($"STANDALONE_SEMANTIC_SMOKE_OK typedEvents={typedEvents.Count} commands={commands.Count}");
    }

    static void RunStrictPlayablePreviewSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var service = new ModPackageService();
        var analysis = service.AnalyzeStandaloneScenarioRequest(project, StandaloneGoldenPrompt, "\u4E09\u82F1\u6218", "strict-playable-smoke");
        var compiled = service.CompileStandaloneScenarioPackage(project, tables, analysis.Design, 16);
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(dictionaryPath))
        {
            throw new FileNotFoundException("Strict playable preview smoke requires CczString.ini.", dictionaryPath);
        }

        var dictionary = new SceneStringParser().Parse(dictionaryPath);
        var preview = service.Preview(project, tables, compiled.Package, dictionary, allowStructuralScenarioWrites: true, strictPlayablePreview: true);
        if (preview.CanApply ||
            preview.Issues.All(issue => !issue.Category.Equals("playability", StringComparison.OrdinalIgnoreCase) && !issue.Category.Equals("runtime", StringComparison.OrdinalIgnoreCase)) ||
            preview.PlayabilityEvidence.Count == 0)
        {
            throw new InvalidOperationException($"Strict playable preview should block draft standalone packages: can={preview.CanApply}, issues={preview.Issues.Count}, evidence={preview.PlayabilityEvidence.Count}.");
        }

        Console.WriteLine($"STRICT_PLAYABLE_PREVIEW_SMOKE_OK canApply={preview.CanApply} issues={preview.Issues.Count} evidence={preview.PlayabilityEvidence.Count} tier={preview.PlayableTier}");
    }

    static void RunStandaloneGoldenSamplesSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var samples = new[]
        {
            new GoldenStandaloneSample(
                "three-heroes",
                "\u4E09\u82F1\u6218",
                StandaloneGoldenPrompt,
                new[] { "\u5CB3\u591C", "\u8944\u614E", "\u5218\u751F", "\u9F99\u5E1D" }),
            new GoldenStandaloneSample(
                "rescue",
                "\u5317\u95E8\u6551\u63F4\u6218",
                "\u72EC\u7ACB\u5355\u5173 MOD\uFF1A\u5317\u95E8\u6551\u63F4\u6218\u3002\u89D2\u8272\uFF1A\u6797\u9706\uFF0C\u5B88\u519B\u6821\u5C09\uFF1B\u82CF\u665A\uFF0C\u519B\u533B\uFF1B\u8D75\u7433\uFF0C\u5F13\u9A91\uFF1B\u9ED1\u65D7\u90FD\u5C09\uFF0C\u56F4\u57CE\u654C\u5C06\u3002\u8981\u6C42\uFF1A4 \u540D\u6211\u65B9\u30011 \u540D\u53CB\u519B\u30018 \u540D\u654C\u519B\uFF1BR \u5F00\u573A\u8DF3 S\uFF0CS \u6709\u6551\u63F4\u533A\u57DF\u89E6\u53D1\u3001\u80DC\u8D25\u6761\u4EF6\u3001\u5956\u52B1\u548C\u6218\u540E\u6536\u675F\u3002\u5730\u56FE\uFF1AMap/M000.JPG\u3002",
                new[] { "\u6797\u9706", "\u82CF\u665A", "\u8D75\u7433", "\u9ED1\u65D7\u90FD\u5C09" }),
            new GoldenStandaloneSample(
                "boss-duel",
                "\u9B3C\u95E8\u51B3\u6218",
                "\u72EC\u7ACB\u5355\u5173 MOD\uFF1A\u9B3C\u95E8\u51B3\u6218\u3002\u89D2\u8272\uFF1A\u9646\u5CE5\uFF0C\u5251\u5BA2\uFF1B\u963F\u73AF\uFF0C\u7B56\u58EB\uFF1B\u90D1\u5B89\uFF0C\u76FE\u536B\uFF1B\u9B3C\u738B\u5B5F\u7EDD\uFF0C\u6700\u7EC8 Boss\u3002\u8981\u6C42\uFF1A4 \u540D\u6211\u65B9\u30011 \u540D\u53CB\u519B\u300110 \u540D\u654C\u519B\uFF1B\u6709 Boss \u9996\u6218\u53F0\u8BCD\u3001\u56DE\u5408\u4E8B\u4EF6\u3001\u51FB\u7834\u80DC\u5229\u3001\u5931\u8D25\u5206\u652F\u548C\u5956\u52B1\u7ED3\u7B97\u3002\u5730\u56FE\uFF1AMap/M003.JPG\u3002",
                new[] { "\u9646\u5CE5", "\u90D1\u5B89", "\u9B3C\u738B\u5B5F\u7EDD" })
        };

        var service = new ModPackageService();
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(dictionaryPath))
        {
            throw new FileNotFoundException("Standalone golden samples smoke requires CczString.ini.", dictionaryPath);
        }

        var dictionary = new SceneStringParser().Parse(dictionaryPath);
        foreach (var sample in samples)
        {
            var analysis = service.AnalyzeStandaloneScenarioRequest(project, sample.Prompt, sample.Title, "golden-" + sample.Id);
            foreach (var expectedRole in sample.ExpectedRoles)
            {
                if (analysis.Design.Roles.All(role => !role.Name.Equals(expectedRole, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException($"Golden sample {sample.Id} missed role {expectedRole}.");
                }
            }

            var compiled = service.CompileStandaloneScenarioPackage(project, tables, analysis.Design, 16);
            var commands = compiled.Package.ScenarioPatches.SelectMany(patch => patch.Operations).SelectMany(operation => operation.Commands).ToList();
            if (!commands.Any(command => command.CommandId == 0x11) ||
                !commands.Any(command => command.CommandId is 0x44 or 0x46 or 0x47) ||
                !commands.Any(command => command.Note.Contains("typed_event:victory", StringComparison.OrdinalIgnoreCase)) ||
                !commands.Any(command => command.Note.Contains("typed_event:reward", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Golden sample {sample.Id} command graph is incomplete.");
            }

            var preview = service.Preview(project, tables, compiled.Package, dictionary, allowStructuralScenarioWrites: true, strictPlayablePreview: false);
            if (!preview.PlayableTier.Equals("draft", StringComparison.OrdinalIgnoreCase) ||
                preview.Issues.All(issue => !issue.Category.Equals("resource", StringComparison.OrdinalIgnoreCase)) ||
                preview.PlayabilityEvidence.Count == 0)
            {
                throw new InvalidOperationException($"Golden sample {sample.Id} did not remain blocked as draft with resource/evidence reporting.");
            }
        }

        Console.WriteLine($"STANDALONE_GOLDEN_SAMPLES_SMOKE_OK samples={samples.Length}");
    }

    private sealed record GoldenStandaloneSample(string Id, string Title, string Prompt, IReadOnlyList<string> ExpectedRoles);
}
