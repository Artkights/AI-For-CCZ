using CCZModStudio.Models;

namespace CCZModStudio.Core;

public static class PortraitFrameAssetDirectoryService
{
    private static readonly string[] SupportedExtensions =
    [
        ".png",
        ".bmp",
        ".jpg",
        ".jpeg"
    ];

    public static string ResolveInitialDirectory(CczProject project)
        => ResolveInitialDirectory(project, PortableInstallPaths.PortraitFramesRoot);

    public static string ResolveInitialDirectory(CczProject project, string bundledPortraitFramesRoot)
    {
        if (HasSupportedImages(bundledPortraitFramesRoot))
        {
            return bundledPortraitFramesRoot;
        }

        var workspaceFrameRoot = Path.Combine(project.WorkspaceRoot, "老版游戏制作工具", "头像框");
        if (HasSupportedImages(workspaceFrameRoot))
        {
            return workspaceFrameRoot;
        }

        if (Directory.Exists(project.GameRoot))
        {
            return project.GameRoot;
        }

        return Directory.GetCurrentDirectory();
    }

    public static IReadOnlyList<string> GetKnownFrameDirectories(CczProject project)
    {
        var directories = new[]
            {
                PortableInstallPaths.PortraitFramesRoot,
                Path.Combine(project.WorkspaceRoot, "老版游戏制作工具", "头像框"),
                project.GameRoot
            }
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return directories;
    }

    private static bool HasSupportedImages(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Any(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    }
}
