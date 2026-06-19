using CCZModStudio.Models;

namespace CCZModStudio.Core;

public static class Ccz66RevisedLayout
{
    public const string Version = "6.6";
    public const long Ekd5ExeSize = 1_130_496;
    public const string ReleasePostUrl = "https://www.xycq.org.cn/forum/thread-310256-1-1.html";

    public static readonly IReadOnlyList<string> RequiredE5Resources =
    [
        "E5\\Item.e5",
        "E5\\Mtem.e5",
        "E5\\DT.e5",
        "E5\\Fb.e5",
        "E5\\U_select.e5",
        "E5\\Pmap.e5"
    ];

    public static readonly IReadOnlySet<string> ObsoleteRuntimeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Itemicon.dll",
        "Mgcicon.dll",
        "MgcIcon.dll",
        "ts.e5"
    };

    public static bool Is66(CczProject project)
        => new CczEngineProfileService().Detect(project).VersionHint.Equals(Version, StringComparison.OrdinalIgnoreCase);

    public static bool Is66(CczEngineProfile profile)
        => profile.VersionHint.Equals(Version, StringComparison.OrdinalIgnoreCase) ||
           profile.TableVersionPrefix.Equals(Version, StringComparison.OrdinalIgnoreCase) ||
           profile.ExeSize == Ekd5ExeSize;

    public static string ResolveItemIconResourceFile(CczProject project)
        => Is66(project) ? "E5\\Item.e5" : "Itemicon.dll";

    public static string ResolveStrategyIconResourceFile(CczProject project)
        => Is66(project) ? "E5\\Mtem.e5" : "Mgcicon.dll";

    public static (int Small, int Large) ResolveItemIconImageNumbers(int fieldValue)
        => checked((fieldValue * 2 + 1, fieldValue * 2 + 2));

    public static int ResolveItemIconPreviewImageNumber(int fieldValue)
        => ResolveItemIconImageNumbers(fieldValue).Large;

    public static int ResolveStrategyIconImageNumber(int fieldValue)
        => checked(fieldValue + 1);

    public static bool IsItemIconResource(string resourceFileName)
        => NormalizeResourceFileName(resourceFileName).Equals("Item.e5", StringComparison.OrdinalIgnoreCase);

    public static bool IsStrategyIconResource(string resourceFileName)
        => NormalizeResourceFileName(resourceFileName).Equals("Mtem.e5", StringComparison.OrdinalIgnoreCase);

    public static bool IsE5IconResource(string resourceFileName)
        => IsItemIconResource(resourceFileName) || IsStrategyIconResource(resourceFileName);

    public static string ResolveResourcePath(CczProject project, string relativeOrFileName)
    {
        var normalized = relativeOrFileName.Replace('/', Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            Path.Combine(project.GameRoot, normalized),
            Path.Combine(project.GameRoot, Path.GetFileName(normalized)),
            Path.Combine(project.GameRoot, "E5", Path.GetFileName(normalized)),
            Path.Combine(project.WorkspaceRoot, normalized),
            Path.Combine(project.WorkspaceRoot, "E5", Path.GetFileName(normalized))
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static string NormalizeResourceFileName(string resourceFileName)
        => Path.GetFileName(resourceFileName.Replace('/', Path.DirectorySeparatorChar));
}
