using System.Diagnostics;

namespace CCZModStudio.Core;

internal static class ActiveReleaseStartupService
{
    private const string ActiveRelativePath = "CCZModStudio_Releases\\Active";
    private const string LauncherName = "普罗工具整合包.exe";

    public static bool IsDeveloperBuildProcess
        => IsDeveloperBuildRequested(Environment.GetCommandLineArgs().Skip(1));

    public static bool IsDeveloperBuildRequested(IEnumerable<string> args)
        => args.Any(arg => arg.Equals("-DeveloperBuild", StringComparison.OrdinalIgnoreCase) ||
                           arg.Equals("--developer-build", StringComparison.OrdinalIgnoreCase)) ||
           Environment.GetEnvironmentVariable("CCZ_DEVELOPER_BUILD") is "1" or "true" or "TRUE";

    public static bool TryRedirectToActive(IReadOnlyList<string> args)
    {
        if (IsDeveloperBuildRequested(args)) return false;
        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable)) return false;
        var activeLauncher = FindActiveLauncher(currentExecutable, AppContext.BaseDirectory);
        if (string.IsNullOrWhiteSpace(activeLauncher) || PathsEqual(currentExecutable, activeLauncher)) return false;

        var start = new ProcessStartInfo
        {
            FileName = activeLauncher,
            WorkingDirectory = Path.GetDirectoryName(activeLauncher)!,
            UseShellExecute = false
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        Process.Start(start);
        return true;
    }

    internal static string? FindActiveLauncher(string currentExecutable, string baseDirectory)
    {
        foreach (var origin in new[] { Path.GetDirectoryName(currentExecutable), baseDirectory }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var directory = new DirectoryInfo(Path.GetFullPath(origin!));
            for (var depth = 0; directory != null && depth < 10; depth++, directory = directory.Parent)
            {
                if (directory.Name.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
                    directory.Parent?.Name.Equals("CCZModStudio_Releases", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var currentActive = Path.Combine(directory.FullName, LauncherName);
                    if (HasReleaseManifest(directory.FullName) && File.Exists(currentActive)) return currentActive;
                }

                var candidateRoot = Path.Combine(directory.FullName, ActiveRelativePath);
                var candidate = Path.Combine(candidateRoot, LauncherName);
                if (HasReleaseManifest(candidateRoot) && File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    internal static bool IsRunningFromActive(string executablePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(executablePath))!);
        return directory.Name.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
               directory.Parent?.Name.Equals("CCZModStudio_Releases", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool HasReleaseManifest(string root)
        => File.Exists(Path.Combine(root, "effect-release-manifest.json"));

    private static bool PathsEqual(string left, string right)
        => Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
