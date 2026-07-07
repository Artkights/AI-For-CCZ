using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Globalization;

internal partial class Program
{
    static void RunEffectInjectionDiscoverySmoke(CczProject project)
    {
        var sourceProject = ResolveEffectInjectionDiscoverySmokeSourceProject(project);
        var report = new InjectedEffectDiscoveryService().Discover(sourceProject, "Ekd5.exe");
        if (string.IsNullOrWhiteSpace(report.TargetFilePath) ||
            !File.Exists(report.TargetFilePath))
        {
            throw new FileNotFoundException("effect injection discovery smoke requires Ekd5.exe.", report.TargetFilePath);
        }

        if (report.ExeSize <= 0 ||
            report.ImageBase == 0 ||
            string.IsNullOrWhiteSpace(report.ExeSha256) ||
            report.ExeSha256.Length != 64)
        {
            throw new InvalidOperationException("Injected effect discovery did not populate PE identity fields.");
        }

        if (string.IsNullOrWhiteSpace(report.Summary) ||
            !report.Summary.Contains("candidates=", StringComparison.OrdinalIgnoreCase) ||
            !report.Summary.Contains("hooks=", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Injected effect discovery summary is missing candidate/hook counts.");
        }

        if (report.Candidates.Any(candidate =>
                string.IsNullOrWhiteSpace(candidate.AddressHex) ||
                string.IsNullOrWhiteSpace(candidate.Type) ||
                string.IsNullOrWhiteSpace(candidate.PatternKind) ||
                string.IsNullOrWhiteSpace(candidate.Confidence)))
        {
            throw new InvalidOperationException("Injected effect discovery returned an incomplete candidate row.");
        }

        if (report.HookCandidates.Any(hook =>
                string.IsNullOrWhiteSpace(hook.AddressHex) ||
                string.IsNullOrWhiteSpace(hook.TargetHex) ||
                string.IsNullOrWhiteSpace(hook.Classification)))
        {
            throw new InvalidOperationException("Injected effect discovery returned an incomplete hook row.");
        }

        if (report.Candidates.Any(candidate =>
                candidate.Type.Equals("InlineStub", StringComparison.OrdinalIgnoreCase) &&
                candidate.Confidence.Equals("InlineStubDetected", StringComparison.OrdinalIgnoreCase) &&
                (!candidate.PersonalEffectId.HasValue ||
                 !candidate.EquipmentEffectId.HasValue ||
                 !candidate.EffectValueFlag.HasValue ||
                 !candidate.StackingFlag.HasValue)))
        {
            throw new InvalidOperationException("InlineStubDetected candidate did not expose all four parsed parameters.");
        }

        if (report.Candidates.Count > 0 && report.Candidates.All(candidate => candidate.Modules.Count == 0))
        {
            throw new InvalidOperationException("Injected effect discovery did not expose module structure rows.");
        }

        if (report.Candidates.Any(candidate =>
                candidate.PatternKind == InjectedEffectPatternKind.FourModuleDamageModifier &&
                (!candidate.JumpOutAddress.HasValue ||
                 !candidate.CodeCaveEntryAddress.HasValue ||
                 !candidate.PersonalEffectId.HasValue ||
                 !candidate.EquipmentEffectId.HasValue)))
        {
            throw new InvalidOperationException("Four-module damage modifier candidate is missing key structure fields.");
        }

        var rawJumpAsPatch = report.Candidates.Any(candidate =>
            candidate.PatternKind == InjectedEffectPatternKind.RawJumpCandidate ||
            candidate.Risk.Equals("jump-graph-candidate", StringComparison.OrdinalIgnoreCase));
        if (rawJumpAsPatch)
        {
            throw new InvalidOperationException("Raw jump candidates must not be promoted to injected effect patches by default.");
        }

        var signatureRoot = Path.Combine(sourceProject.WorkspaceRoot, "特效整理", "6.5");
        if (!Directory.Exists(signatureRoot))
        {
            var parent = Directory.GetParent(sourceProject.WorkspaceRoot)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                signatureRoot = Path.Combine(parent, "特效整理", "6.5");
            }
        }

        var signatureFileCount = Directory.Exists(signatureRoot)
            ? Directory.GetFiles(signatureRoot, "*.txt").Length
            : 0;
        if (signatureFileCount > 0 &&
            !report.Summary.Contains("knownPatchSignatures=", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Injected effect discovery summary did not report known patch signature count.");
        }

        if (signatureFileCount > 0)
        {
            if (signatureFileCount != 17)
            {
                throw new InvalidOperationException($"Expected 17 local 6.5 injection signatures, got {signatureFileCount}.");
            }

            var catalog = new InjectedEffectDiscoveryService().LoadKnownPatchCatalog(sourceProject);
            if (catalog.Count != 17)
            {
                throw new InvalidOperationException($"Expected 17 local known patch catalog entries, got {catalog.Count}.");
            }

            AssertKnownPatch(catalog, "策略冲锋", InjectedEffectPatchCategory.SimpleFourModuleSpecialEffect, expectPersonal: true, expectEquipment: true);
            AssertKnownPatch(catalog, "聚势伐谋", InjectedEffectPatchCategory.SimpleFourModuleSpecialEffect, expectPersonal: true, expectEquipment: true);

            var limit = AssertKnownPatch(catalog, "策略保底", InjectedEffectPatchCategory.MultiCheckSpecialEffect, expectPersonal: true, expectEquipment: true);
            if (limit.ParameterSlots.Count(slot => slot.Role is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment) < 4)
            {
                throw new InvalidOperationException("策略保底&策略限伤 should expose at least four equipment/personal parameter slots.");
            }

            var massacre = AssertKnownPatch(catalog, "大杀四方", InjectedEffectPatchCategory.ComplexMultiHookPatch, expectPersonal: true, expectEquipment: true);
            if (massacre.PatternKind == InjectedEffectPatternKind.FourModuleDamageModifier)
            {
                throw new InvalidOperationException("大杀四方 must not be classified as a simple four-module damage modifier.");
            }

            var guard = AssertKnownPatch(catalog, "护卫", InjectedEffectPatchCategory.ComplexMultiHookPatch, expectPersonal: true, expectEquipment: true);
            AssertParameterSlot(guard, InjectedEffectParameterRole.Range, "护卫 should expose range configuration.");
            AssertParameterSlot(guard, InjectedEffectParameterRole.BooleanOption, "护卫 should expose speaking toggle configuration.");
            AssertParameterSlot(guard, InjectedEffectParameterRole.MessageText, "护卫 should expose message text configuration.");

            AssertKnownPatch(catalog, "信息传送29", InjectedEffectPatchCategory.FunctionExtensionPatch, expectPersonal: false, expectEquipment: false);
            AssertKnownPatch(catalog, "整形4003", InjectedEffectPatchCategory.FunctionExtensionPatch, expectPersonal: false, expectEquipment: false);
            AssertKnownPatch(catalog, "无视策略减伤", null, expectPersonal: true, expectEquipment: true);

            var pierce = AssertKnownPatch(catalog, "强化攻击穿透", null, expectPersonal: false, expectEquipment: false);
            if (!pierce.ParameterSlots.Any(slot => slot.Role == InjectedEffectParameterRole.UnknownCombined))
            {
                throw new InvalidOperationException("强化攻击穿透 should keep 宝物-个人 as an ambiguous combined parameter.");
            }
        }

        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"EFFECT_INJECTION_DISCOVERY_SMOKE_OK root={sourceProject.GameRoot} candidates={report.Candidates.Count} hooks={report.HookCandidates.Count} signatures={signatureFileCount} sha={report.ExeSha256[..8]}"));
    }

    private static InjectedEffectCandidate AssertKnownPatch(
        IReadOnlyList<InjectedEffectCandidate> candidates,
        string namePart,
        string? expectedCategory,
        bool expectPersonal,
        bool expectEquipment)
    {
        var candidate = candidates.FirstOrDefault(item =>
            item.Type.StartsWith("KnownPatch", StringComparison.OrdinalIgnoreCase) &&
            item.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase));
        if (candidate == null)
        {
            throw new InvalidOperationException($"Missing known injected effect patch: {namePart}.");
        }

        if (!string.IsNullOrWhiteSpace(expectedCategory) &&
            !candidate.PatchCategory.Equals(expectedCategory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{namePart} category mismatch: expected {expectedCategory}, got {candidate.PatchCategory}.");
        }

        if (expectPersonal && !candidate.ParameterSlots.Any(slot => slot.Role == InjectedEffectParameterRole.Personal))
        {
            throw new InvalidOperationException($"{namePart} did not expose a personal/special-skill parameter slot.");
        }

        if (expectEquipment && !candidate.ParameterSlots.Any(slot => slot.Role == InjectedEffectParameterRole.Equipment))
        {
            throw new InvalidOperationException($"{namePart} did not expose an equipment/treasure parameter slot.");
        }

        return candidate;
    }

    private static void AssertParameterSlot(InjectedEffectCandidate candidate, string role, string message)
    {
        if (!candidate.ParameterSlots.Any(slot => slot.Role == role))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static CczProject ResolveEffectInjectionDiscoverySmokeSourceProject(CczProject project)
    {
        var detector = new ProjectDetector();
        var standardRoot = Path.Combine(project.WorkspaceRoot, "基底", "加强版6.5未加密版", "加强版6.5未加密版");
        if (File.Exists(Path.Combine(standardRoot, "Ekd5.exe")))
        {
            return detector.CreateProjectFromGameRoot(standardRoot);
        }

        var simpleRoot = Path.Combine(project.WorkspaceRoot, "基底", "加强版6.5未加密版");
        if (File.Exists(Path.Combine(simpleRoot, "Ekd5.exe")))
        {
            return detector.CreateProjectFromGameRoot(simpleRoot);
        }

        if (File.Exists(project.ResolveGameFile("Ekd5.exe")))
        {
            return project;
        }

        var fallback = Directory.EnumerateFiles(project.WorkspaceRoot, "Ekd5.exe", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return detector.CreateProjectFromGameRoot(Path.GetDirectoryName(fallback)!);
        }

        return project;
    }
}
