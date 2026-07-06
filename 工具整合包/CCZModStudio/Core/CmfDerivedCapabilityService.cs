using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CmfDerivedCapabilityService
{
    private readonly CmfKnowledgeExtractor _extractor = new();

    public IReadOnlyList<CmfToolProject> LoadProjects(CczProject project)
    {
        var root = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot);
        return string.IsNullOrWhiteSpace(root) ? Array.Empty<CmfToolProject>() : _extractor.ExtractCorpus(root);
    }

    public CmfToolProject ExtractProject(CczProject project, string relativePath)
    {
        var root = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot)
            ?? throw new DirectoryNotFoundException("Old tools CMF root was not found.");
        var path = ResolvePath(root, relativePath);
        return _extractor.Extract(path, root);
    }

    public CmfToolProject ImportCheatMakerExport(CczProject project, string relativePath, string exportPath)
    {
        var root = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot)
            ?? throw new DirectoryNotFoundException("Old tools CMF root was not found.");
        var cmfPath = ResolvePath(root, relativePath);
        var resolvedExportPath = ResolveReadableExportPath(root, exportPath);
        return _extractor.ImportCheatMakerExport(cmfPath, resolvedExportPath, root);
    }

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
