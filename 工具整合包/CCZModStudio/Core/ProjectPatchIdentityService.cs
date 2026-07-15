using System.Security.Cryptography;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ProjectPatchIdentityService
{
    public ProjectPatchIdentity Build(CczProject project, string targetFileName = "Ekd5.exe")
    {
        var target = project.ResolveGameFile(targetFileName);
        if (!File.Exists(target))
            return BuildKnown(project, targetFileName, 0, string.Empty);

        if (Path.GetFileName(targetFileName).Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase))
        {
            var executable = ExecutableAnalysisSnapshotCache.Shared.GetBase(project, targetFileName);
            return BuildKnown(project, targetFileName, executable.Bytes.LongLength, executable.Sha256);
        }

        return BuildKnown(project, targetFileName, new FileInfo(target).Length, EffectPatchByteService.Sha256(target));
    }

    public ProjectPatchIdentity BuildKnown(
        CczProject project,
        string targetFileName,
        long targetFileSize,
        string currentSha256,
        string engineProfileId = EngineEffectProfileRegistry.Profile65Id)
    {
        var gameRoot = NormalizePath(project.GameRoot);
        return new ProjectPatchIdentity
        {
            ProjectId = ComputeProjectId(gameRoot, targetFileName, targetFileSize),
            GameRoot = gameRoot,
            TargetFileName = Path.GetFileName(targetFileName),
            TargetFileSize = targetFileSize,
            BaselineSha256 = EngineEffectProfileRegistry.Canonical65Sha256,
            CurrentSha256 = currentSha256,
            EngineProfileId = engineProfileId
        };
    }

    public bool Matches(CczProject project, ProjectPatchIdentity? identity, string? legacyProjectRoot = null)
    {
        var targetFileName = identity?.TargetFileName ?? "Ekd5.exe";
        var target = project.ResolveGameFile(targetFileName);
        var targetSize = File.Exists(target) ? new FileInfo(target).Length : 0;
        var current = BuildKnown(project, targetFileName, targetSize, string.Empty);
        if (identity != null)
        {
            return identity.ProjectId.Equals(current.ProjectId, StringComparison.OrdinalIgnoreCase) &&
                   NormalizePath(identity.GameRoot).Equals(current.GameRoot, StringComparison.OrdinalIgnoreCase) &&
                   identity.TargetFileSize == current.TargetFileSize &&
                   identity.TargetFileName.Equals(current.TargetFileName, StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(legacyProjectRoot) &&
               NormalizePath(legacyProjectRoot).Equals(current.GameRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static string EffectManifestRoot(CczProject project)
        => Path.Combine(project.GameRoot, "CCZModStudio_Notes", "EffectManifests");

    public static string CompositeManifestRoot(CczProject project)
        => Path.Combine(project.GameRoot, "CCZModStudio_Notes", "CompositeEffectManifests");

    public static string ModularManifestRoot(CczProject project)
        => Path.Combine(project.GameRoot, "CCZModStudio_Notes", "ModularEffectManifests");

    public static string DispatcherManifestRoot(CczProject project)
        => Path.Combine(project.GameRoot, "CCZModStudio_Notes", "EffectDispatcherManifests");

    public static string ComputeProjectId(string gameRoot, string targetFileName, long targetFileSize)
    {
        var token = $"{NormalizePath(gameRoot)}|{Path.GetFileName(targetFileName).ToUpperInvariant()}|{targetFileSize}|{EngineEffectProfileRegistry.Profile65Id}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..24].ToLowerInvariant();
    }

    public static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
