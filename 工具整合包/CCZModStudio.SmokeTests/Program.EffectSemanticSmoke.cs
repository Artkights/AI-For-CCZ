using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Globalization;

internal partial class Program
{
    private static void RunEffectSemanticSmoke(CczProject project)
    {
        var sourceProject = ResolveEffectInjectionDiscoverySmokeSourceProject(project);
        var catalog = new EffectKnowledgeFusionService().Build(sourceProject, "Ekd5.exe", writeReports: true);

        if (string.IsNullOrWhiteSpace(catalog.TargetFilePath) ||
            !File.Exists(catalog.TargetFilePath))
        {
            throw new FileNotFoundException("effect semantic smoke requires Ekd5.exe.", catalog.TargetFilePath);
        }

        if (string.IsNullOrWhiteSpace(catalog.ExeSha256) ||
            catalog.ExeSha256.Length != 64 ||
            catalog.ImageBase == 0)
        {
            throw new InvalidOperationException("Effect semantic catalog did not populate PE identity fields.");
        }

        if (catalog.Functions.Count == 0)
        {
            throw new InvalidOperationException("Effect semantic catalog did not produce function records.");
        }

        if (catalog.Effects.Count == 0)
        {
            throw new InvalidOperationException("Effect semantic catalog did not produce effect meaning records.");
        }

        foreach (var address in new[] { 0x004101D9u, 0x0042518Fu, 0x0041301Eu, 0x00413009u, 0x004927F0u })
        {
            AssertSemanticFunction(catalog, address);
        }

        var core = catalog.Functions.First(item => item.Address == 0x004101D9u);
        if (core.EvidenceLevel is not EffectSemanticEvidenceLevel.VerifiedStatic and not EffectSemanticEvidenceLevel.VerifiedDynamic ||
            core.SemanticKind != EffectSemanticKind.SwitchEffect)
        {
            throw new InvalidOperationException("004101D9 was not classified as a verified switch-effect core function.");
        }

        if (!catalog.Effects.Any(effect => effect.EvidenceLevel == EffectSemanticEvidenceLevel.KnownSample))
        {
            throw new InvalidOperationException("Effect semantic catalog did not include KnownSample effect meanings from local signatures.");
        }

        if (catalog.AgentKnowledge.CallableFunctions.Count == 0 ||
            catalog.AgentKnowledge.EffectTemplates.Count == 0 ||
            string.IsNullOrWhiteSpace(catalog.AgentKnowledge.AgentContext))
        {
            throw new InvalidOperationException("Effect semantic catalog did not build a usable local-agent special-effect knowledge pack.");
        }

        if (catalog.AgentKnowledge.AgentContext.Length > catalog.AgentKnowledge.ContextBudgetCharacters)
        {
            throw new InvalidOperationException("Local-agent special-effect knowledge pack exceeded its fixed character budget.");
        }

        if (!catalog.AgentKnowledge.AgentContext.Contains("004101D9", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Local-agent special-effect knowledge pack did not include the core 004101D9 anchor.");
        }

        if (!catalog.AgentKnowledge.Guardrails.Any(item => item.Contains("Do not write patches directly", StringComparison.OrdinalIgnoreCase)) ||
            !catalog.AgentKnowledge.Guardrails.Any(item => item.Contains("Complex multi-hook", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Local-agent special-effect knowledge pack is missing write-safety or complex-patch guardrails.");
        }

        if (catalog.ReportPaths.Count < 2 ||
            catalog.ReportPaths.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)))
        {
            throw new InvalidOperationException("Effect semantic catalog did not write JSON/Markdown reports.");
        }

        if (!catalog.ReportPaths.Any(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ||
            !catalog.ReportPaths.Any(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Effect semantic reports must include both JSON and Markdown outputs.");
        }

        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"EFFECT_SEMANTIC_SMOKE_OK root={sourceProject.GameRoot} functions={catalog.Functions.Count} effects={catalog.Effects.Count} agentFunctions={catalog.AgentKnowledge.CallableFunctions.Count} reports={catalog.ReportPaths.Count} sha={catalog.ExeSha256[..8]}"));
    }

    private static void AssertSemanticFunction(FunctionSemanticCatalog catalog, uint address)
    {
        var function = catalog.Functions.FirstOrDefault(item => item.Address == address);
        if (function == null)
        {
            throw new InvalidOperationException($"Missing semantic function record 0x{address:X8}.");
        }

        if (string.IsNullOrWhiteSpace(function.Name) ||
            string.IsNullOrWhiteSpace(function.Role) ||
            string.IsNullOrWhiteSpace(function.EvidenceLevel) ||
            function.ConfidenceScore <= 0)
        {
            throw new InvalidOperationException($"Semantic function record 0x{address:X8} is incomplete.");
        }
    }
}
