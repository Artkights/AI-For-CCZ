using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CmfDerivedCapabilityService
{
    private readonly CmfKnowledgeExtractor _extractor = new();
    private readonly CmfDesignerExtractionService _designerExtractionService = new();
    private readonly CmfDesignerSnapshotDiffService _designerSnapshotDiffService = new();
    private readonly CmfDesignerWriteVerificationService _designerWriteVerificationService = new();
    private readonly CmfManualSeedService _manualSeedService = new();

    public IReadOnlyList<CmfToolProject> LoadProjects(CczProject project)
    {
        var root = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot);
        if (string.IsNullOrWhiteSpace(root))
        {
            return Array.Empty<CmfToolProject>();
        }

        return _extractor.ExtractCorpus(root)
            .Select(cmf => TryImportLatestDesignerSnapshot(project, root, cmf))
            .Select(cmf => TryImportManualSeed(project, root, cmf))
            .ToList();
    }

    public CmfToolProject ExtractProject(CczProject project, string relativePath)
    {
        var root = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot)
            ?? throw new DirectoryNotFoundException("Old tools CMF root was not found.");
        var path = ResolvePath(root, relativePath);
        return TryImportManualSeed(project, root, TryImportLatestDesignerSnapshot(project, root, _extractor.Extract(path, root)));
    }

    public CmfToolProject ImportCheatMakerExport(CczProject project, string relativePath, string exportPath)
    {
        var root = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot)
            ?? throw new DirectoryNotFoundException("Old tools CMF root was not found.");
        var cmfPath = ResolvePath(root, relativePath);
        var resolvedExportPath = ResolveReadableExportPath(root, exportPath);
        return _extractor.ImportCheatMakerExport(cmfPath, resolvedExportPath, root);
    }

    public CmfDesignerExtractionResult ExtractDesignerSnapshot(
        CczProject project,
        string relativePath,
        CmfDesignerExtractionOptions? options = null)
        => _designerExtractionService.ExtractDesignerSnapshot(project, relativePath, options);

    public CmfToolProject ImportDesignerSnapshot(CczProject project, string relativePath, string snapshotPath)
    {
        var root = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot)
            ?? throw new DirectoryNotFoundException("Old tools CMF root was not found.");
        var cmfPath = ResolvePath(root, relativePath);
        var resolvedSnapshotPath = Path.GetFullPath(Path.IsPathRooted(snapshotPath)
            ? snapshotPath
            : Path.Combine(project.WorkspaceRoot, snapshotPath));
        return TryImportManualSeed(project, root, _extractor.ImportDesignerSnapshot(cmfPath, resolvedSnapshotPath, root));
    }

    public CmfDesignerSnapshotDiffReport CompareDesignerSnapshots(
        CczProject project,
        string leftRelativePath,
        string rightRelativePath,
        string? leftSnapshotPath = null,
        string? rightSnapshotPath = null)
    {
        var left = LoadDesignerSnapshot(project, leftRelativePath, leftSnapshotPath);
        var right = LoadDesignerSnapshot(project, rightRelativePath, rightSnapshotPath);
        return _designerSnapshotDiffService.Compare(project, left, right);
    }

    public CmfDesignerWriteVerificationReport VerifyDesignerWrites(
        CczProject project,
        string relativePath,
        CmfDesignerWriteVerificationOptions? options = null)
    {
        options ??= new CmfDesignerWriteVerificationOptions();
        var snapshot = LoadDesignerSnapshot(project, relativePath, options.SnapshotPath);
        return _designerWriteVerificationService.VerifyOnTestCopy(project, snapshot, options);
    }

    public IReadOnlyList<CmfDesignerFieldListItem> ListDesignerFields(CczProject project, string? relativePath = null)
    {
        IReadOnlyList<CmfToolProject> cmfProjects = string.IsNullOrWhiteSpace(relativePath)
            ? LoadProjects(project)
            : new[] { ExtractProject(project, relativePath) };

        return cmfProjects
            .Where(cmf => cmf.DesignerSnapshot != null)
            .SelectMany(cmf => ToDesignerFieldListItems(cmf.DesignerSnapshot!))
            .OrderBy(field => field.SourceCmfRelativePath, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(field => field.UeOffset ?? long.MaxValue)
            .ThenBy(field => field.PageName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(field => field.ModuleTitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(field => field.ControlName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<CmfDesignerFieldListItem> ListManualSeedFields(CczProject project, string? keyword = null)
    {
        var fields = _manualSeedService.LoadManualSeedSnapshots(project)
            .SelectMany(ToDesignerFieldListItems)
            .ToList();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            fields = fields
                .Where(field =>
                    field.SourceCmfRelativePath.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    field.PageName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    field.ModuleTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    field.ControlName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    field.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    field.UeOffsetHex.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    field.DataListPreview.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return fields
            .OrderBy(field => field.SourceCmfRelativePath, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(field => field.UeOffset ?? long.MaxValue)
            .ThenBy(field => field.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public CmfManualSeedValidationReport ValidateManualSeeds(CczProject project)
        => _manualSeedService.ValidateSeeds(project);

    public IReadOnlyList<CmfFeatureCandidate> ListFeatures(CczProject project, string? category = null, string? keyword = null)
    {
        var features = LoadProjects(project)
            .SelectMany(cmf => cmf.FeatureCandidates)
            .ToList();

        if (!string.IsNullOrWhiteSpace(category))
        {
            features = features
                .Where(feature => feature.Category.Equals(category, StringComparison.OrdinalIgnoreCase) ||
                                  feature.TargetSubsystem.Equals(category, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            features = features
                .Where(feature =>
                    feature.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    feature.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    feature.TargetSubsystem.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    feature.SourceCmfRelativePath.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    feature.EvidenceNotes.Any(note => note.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        return features
            .OrderBy(feature => feature.TargetSubsystem, StringComparer.OrdinalIgnoreCase)
            .ThenBy(feature => feature.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(feature => feature.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public CmfFeatureCandidate ReadFeature(CczProject project, string featureId)
        => ListFeatures(project)
               .FirstOrDefault(feature => feature.FeatureId.Equals(featureId, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException("CMF derived feature was not found: " + featureId);

    public CmfPromotionDraft PromoteFeature(CczProject project, string featureId)
    {
        foreach (var cmf in LoadProjects(project))
        {
            if (cmf.FeatureCandidates.Any(feature => feature.FeatureId.Equals(featureId, StringComparison.OrdinalIgnoreCase)))
            {
                return _extractor.PromoteFeature(cmf, featureId);
            }
        }

        throw new InvalidOperationException("CMF derived feature was not found: " + featureId);
    }

    public IReadOnlyList<CmfFeatureCandidate> ListGlobalSettingCandidates(CczProject project)
        => ListFeatures(project)
            .Where(feature => feature.TargetSubsystem.Equals("GlobalSettingsService", StringComparison.OrdinalIgnoreCase) ||
                              feature.Category.Equals("GlobalSetting", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public IReadOnlyList<CmfFeatureCandidate> ListEffectCandidates(CczProject project)
        => ListFeatures(project)
            .Where(feature => feature.TargetSubsystem.Equals("EffectPackageService", StringComparison.OrdinalIgnoreCase) ||
                              feature.Category.StartsWith("Effect", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public object BuildSummary(CczProject project)
    {
        var projects = LoadProjects(project);
        var features = projects.SelectMany(cmf => cmf.FeatureCandidates).ToList();
        return new
        {
            AuthoritativeToolSource = true,
            ExtractionMode = "StaticSegmentAnalysis",
            Policy = "CMF projects are high-trust old modifier sources. Static candidates require CheatMaker export/UI field extraction and reread validation before write rules.",
            ProjectCount = projects.Count,
            FeatureCount = features.Count,
            DesignerSnapshotCount = projects.Count(cmf => cmf.DesignerSnapshot != null),
            DesignerFieldCount = projects.SelectMany(cmf => cmf.DesignerSnapshot?.Bindings ?? Array.Empty<CmfDesignerBinding>()).Count(),
            ManualSeedValidation = _manualSeedService.ValidateSeeds(project),
            BySubsystem = features
                .GroupBy(feature => feature.TargetSubsystem, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            CoreFeatures = features
                .Where(feature => !feature.Category.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                .Take(24)
                .Select(feature => new
                {
                    feature.FeatureId,
                    feature.Name,
                    feature.Category,
                    feature.VersionScope,
                    feature.TargetSubsystem,
                    feature.ConversionStatus,
                    feature.SourceCmfRelativePath
                })
                .ToArray()
        };
    }

    private CmfToolProject TryImportLatestDesignerSnapshot(CczProject project, string root, CmfToolProject cmf)
    {
        try
        {
            var snapshot = _designerExtractionService.LoadLatestSnapshot(project, cmf.RelativePath);
            if (snapshot == null)
            {
                return cmf;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.SourceSha256) &&
                !string.IsNullOrWhiteSpace(cmf.Sha256) &&
                !snapshot.SourceSha256.Equals(cmf.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return cmf;
            }

            return _extractor.ImportDesignerSnapshot(cmf.SourcePath, snapshot, root);
        }
        catch
        {
            return cmf;
        }
    }

    private CmfToolProject TryImportManualSeed(CczProject project, string root, CmfToolProject cmf)
    {
        try
        {
            var manualSnapshot = _manualSeedService.TryCreateSnapshotForCmf(project, cmf);
            if (manualSnapshot == null)
            {
                return cmf;
            }

            var mergedSnapshot = cmf.DesignerSnapshot == null
                ? manualSnapshot
                : _manualSeedService.MergeSnapshots(cmf.DesignerSnapshot, manualSnapshot);
            return _extractor.ImportDesignerSnapshot(cmf.SourcePath, mergedSnapshot, root);
        }
        catch (Exception ex)
        {
            return new CmfToolProject
            {
                SourcePath = cmf.SourcePath,
                RelativePath = cmf.RelativePath,
                FileName = cmf.FileName,
                Sha256 = cmf.Sha256,
                Length = cmf.Length,
                FormatSignature = cmf.FormatSignature,
                FormatVersion = cmf.FormatVersion,
                IsCheatMakerCmf = cmf.IsCheatMakerCmf,
                AuthoritativeToolSource = cmf.AuthoritativeToolSource,
                ExtractionMode = cmf.ExtractionMode,
                ConversionPolicy = cmf.ConversionPolicy,
                Segments = cmf.Segments,
                Pages = cmf.Pages,
                Controls = cmf.Controls,
                DataBindings = cmf.DataBindings,
                AddressEntries = cmf.AddressEntries,
                ExportFields = cmf.ExportFields,
                DesignerSnapshot = cmf.DesignerSnapshot,
                FeatureCandidates = cmf.FeatureCandidates,
                Warnings = cmf.Warnings.Concat(["Manual seed import failed: " + ex.Message]).ToArray()
            };
        }
    }

    private CmfDesignerSnapshot LoadDesignerSnapshot(CczProject project, string relativePath, string? snapshotPath)
    {
        if (!string.IsNullOrWhiteSpace(snapshotPath))
        {
            var resolved = Path.GetFullPath(Path.IsPathRooted(snapshotPath)
                ? snapshotPath
                : Path.Combine(project.WorkspaceRoot, snapshotPath));
            var loaded = _designerExtractionService.LoadSnapshotFile(resolved);
            var manualForLoaded = _manualSeedService.TryCreateSnapshotForRelativePath(project, relativePath);
            return manualForLoaded == null ? loaded : _manualSeedService.MergeSnapshots(loaded, manualForLoaded);
        }

        var latest = _designerExtractionService.LoadLatestSnapshot(project, relativePath);
        var manual = _manualSeedService.TryCreateSnapshotForRelativePath(project, relativePath);
        if (latest != null && manual != null)
        {
            return _manualSeedService.MergeSnapshots(latest, manual);
        }

        return latest ?? manual
            ?? throw new FileNotFoundException("No CheatMaker Designer snapshot or manual seed was found for CMF: " + relativePath);
    }

    private static IEnumerable<CmfDesignerFieldListItem> ToDesignerFieldListItems(CmfDesignerSnapshot snapshot)
    {
        foreach (var binding in snapshot.Bindings)
        {
            var page = snapshot.Pages.FirstOrDefault(item => item.PageId.Equals(binding.PageId, StringComparison.OrdinalIgnoreCase));
            var module = snapshot.Modules.FirstOrDefault(item => item.ModuleId.Equals(binding.ModuleId, StringComparison.OrdinalIgnoreCase));
            yield return new CmfDesignerFieldListItem
            {
                SourceCmfRelativePath = snapshot.RelativePath,
                SourceSha256 = snapshot.SourceSha256,
                ExtractionMode = ResolveFieldExtractionMode(snapshot, binding),
                TrustLevel = ResolveFieldTrustLevel(snapshot, binding),
                PageName = page?.Name ?? binding.PageId,
                ModuleTitle = module?.Title ?? binding.ModuleId,
                BindingId = binding.BindingId,
                ControlName = binding.ControlName,
                ControlType = binding.ControlType,
                DisplayName = binding.DisplayName,
                TargetFile = binding.TargetFile,
                AddressKind = binding.AddressKind,
                UeOffsetHex = binding.UeOffsetHex,
                UeOffset = binding.UeOffset,
                ByteLength = binding.ByteLength,
                DataType = binding.DataType,
                FunctionType = binding.FunctionType,
                DefaultValueRaw = binding.DefaultValueRaw,
                ValidationStatus = binding.ValidationStatus,
                DataListPreview = BuildDataListPreview(binding.DataListRaw)
            };
        }
    }

    private static string ResolveFieldExtractionMode(CmfDesignerSnapshot snapshot, CmfDesignerBinding binding)
    {
        if (binding.SourceProperties.TryGetValue("sourceType", out var sourceType) && !string.IsNullOrWhiteSpace(sourceType))
        {
            return sourceType;
        }

        return snapshot.ExtractionMode;
    }

    private static string ResolveFieldTrustLevel(CmfDesignerSnapshot snapshot, CmfDesignerBinding binding)
    {
        if (binding.SourceProperties.TryGetValue("trustLevel", out var trustLevel) && !string.IsNullOrWhiteSpace(trustLevel))
        {
            return trustLevel;
        }

        if (binding.ValidationStatus.Equals("ManualConfirmed", StringComparison.OrdinalIgnoreCase) ||
            snapshot.ExtractionMode.Contains("ManualConfirmedSeed", StringComparison.OrdinalIgnoreCase))
        {
            return "ManualConfirmed";
        }

        return binding.ValidationStatus;
    }

    private static string BuildDataListPreview(string dataListRaw)
    {
        if (string.IsNullOrWhiteSpace(dataListRaw)) return string.Empty;
        var compact = dataListRaw.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return compact.Length <= 160 ? compact : compact[..160] + "...";
    }

    private static string ResolvePath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("CMF relative path is required.");
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(root, normalized));
        var rootWithSlash = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CMF path escapes the old tools root.");
        }

        return fullPath;
    }

    private static string ResolveReadableExportPath(string root, string exportPath)
    {
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            throw new InvalidOperationException("CheatMaker export path is required.");
        }

        var normalized = exportPath.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(root, normalized));
    }
}
