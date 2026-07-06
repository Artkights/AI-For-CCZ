using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunCmfKnowledgeSmoke()
    {
        var project = new ProjectDetector().DetectDefaultProject();
        var oldToolsRoot = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot)
            ?? throw new DirectoryNotFoundException("Old tools root was not found under workspace.");
        var corpus = new CheatMakerCmfProbe().ScanCorpus(oldToolsRoot);

        var extractor = new CmfKnowledgeExtractor();
        var star65 = extractor.Extract(FindCmfByLength(corpus, 768_391), oldToolsRoot);
        var star66 = extractor.Extract(FindCmfByLength(corpus, 1_145_916), oldToolsRoot);
        if (!star65.IsCheatMakerCmf || !star66.IsCheatMakerCmf)
        {
            throw new InvalidOperationException("Star engine CMF projects were not recognized as CheatMaker CMF.");
        }

        if (star66.Segments.Count <= star65.Segments.Count)
        {
            throw new InvalidOperationException($"Expected Star6.6X to expose more CMF segments than Star6.5, got 6.6={star66.Segments.Count}, 6.5={star65.Segments.Count}.");
        }

        if (!star65.FeatureCandidates.Any(feature => feature.Category == "EngineExe" || feature.Category == "GlobalSetting"))
        {
            throw new InvalidOperationException("Star6.5 CMF did not produce engine/global feature candidates.");
        }

        var effectName = extractor.Extract(FindCmfByLength(corpus, 15_346), oldToolsRoot);
        if (!effectName.FeatureCandidates.Any(feature => feature.Category == "EffectName"))
        {
            throw new InvalidOperationException("Effect-name CMF did not produce an EffectName candidate.");
        }

        var imsg = extractor.Extract(FindCmfByLength(corpus, 12_930), oldToolsRoot);
        if (!imsg.FeatureCandidates.Any(feature => feature.Category == "EffectDescription"))
        {
            throw new InvalidOperationException("Imsg effect-description CMF did not produce an EffectDescription candidate.");
        }

        var derived = new CmfDerivedCapabilityService();
        var features = derived.ListFeatures(project);
        if (features.Count < 7)
        {
            throw new InvalidOperationException("CMF derived feature list is unexpectedly small: " + features.Count);
        }

        var globalCandidates = derived.ListGlobalSettingCandidates(project);
        if (globalCandidates.Count == 0)
        {
            throw new InvalidOperationException("CMF derived global setting candidates were not found.");
        }

        var tables = new Ccz66HexTableAugmentationService().LoadForProject(project, new HexTableParser());
        var cmfEffects = new EffectPackageService().ListEffects(project, tables, "cmf", null, 100);
        if (cmfEffects.Count == 0)
        {
            throw new InvalidOperationException("CMF-derived effect domain returned no entries.");
        }

        var draft = derived.PromoteFeature(project, features[0].FeatureId);
        if (draft.CanWriteNow)
        {
            throw new InvalidOperationException("Static CMF feature promotion must not be writable before validation.");
        }

        var exportPath = Path.Combine(Path.GetTempPath(), "CCZModStudio_CmfExport_" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(
                exportPath,
                "等级上限\t0048D3C4\tByte\t1B\tEkd5.exe\r\n升级经验\t0048D3C5\tWord\t2B\tEkd5.exe\r\n",
                System.Text.Encoding.UTF8);
            var imported = extractor.ImportCheatMakerExport(FindCmfByLength(corpus, 768_391), exportPath, oldToolsRoot);
            if (imported.ExportFields.Count != 2)
            {
                throw new InvalidOperationException("CMF export import did not return the expected field count.");
            }

            if (!imported.FeatureCandidates.Any(feature => feature.TrustLevel == CmfTrustLevel.ExtractedFromCheatMakerExport))
            {
                throw new InvalidOperationException("CMF export import did not promote feature trust level.");
            }

            var importedDraft = extractor.PromoteFeature(imported, imported.FeatureCandidates[0].FeatureId);
            if (importedDraft.CanWriteNow)
            {
                throw new InvalidOperationException("CMF export import must still require validation before writes.");
            }
        }
        finally
        {
            try
            {
                if (File.Exists(exportPath))
                {
                    File.Delete(exportPath);
                }
            }
            catch
            {
                // Temp cleanup is best effort.
            }
        }

        Console.WriteLine(
            "CMF_KNOWLEDGE_SMOKE OK " +
            $"projects=7+ features={features.Count} globals={globalCandidates.Count} cmfEffects={cmfEffects.Count} " +
            $"star65Segments={star65.Segments.Count} star66Segments={star66.Segments.Count}");
    }

    private static string FindCmfByLength(CheatMakerCmfCorpusReport corpus, long length)
    {
        var entry = corpus.Entries.FirstOrDefault(item =>
            item.EvidenceCategory.Equals("CczRelevantRootSample", StringComparison.OrdinalIgnoreCase) &&
            item.Length == length)
            ?? throw new InvalidOperationException("CMF corpus entry was not found by length: " + length);
        return entry.Path;
    }
}
