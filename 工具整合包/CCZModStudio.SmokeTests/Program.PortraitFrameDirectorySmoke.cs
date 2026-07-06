using CCZModStudio.Core;
using CCZModStudio.Models;

internal partial class Program
{
    private static void RunPortraitFrameDirectorySmoke()
    {
        var smokeRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_PortraitFrameDirectorySmoke_" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspaceRoot = Path.Combine(smokeRoot, "workspace");
            var gameRoot = Path.Combine(smokeRoot, "game");
            var bundledRoot = Path.Combine(smokeRoot, "runtime", "net", "Assets", "PortraitFrames");
            var workspaceFrameRoot = Path.Combine(workspaceRoot, "老版游戏制作工具", "头像框");
            Directory.CreateDirectory(workspaceRoot);
            Directory.CreateDirectory(gameRoot);

            var project = new CczProject
            {
                WorkspaceRoot = workspaceRoot,
                GameRoot = gameRoot,
                HexTableXmlPath = Path.Combine(workspaceRoot, "HexTable.xml")
            };

            AssertEqual(gameRoot, PortraitFrameAssetDirectoryService.ResolveInitialDirectory(project, bundledRoot), "portrait frame directory falls back to game root");

            Directory.CreateDirectory(workspaceFrameRoot);
            File.WriteAllBytes(Path.Combine(workspaceFrameRoot, "local.png"), [0x89, 0x50, 0x4E, 0x47]);
            AssertEqual(workspaceFrameRoot, PortraitFrameAssetDirectoryService.ResolveInitialDirectory(project, bundledRoot), "portrait frame directory falls back to workspace frames");

            Directory.CreateDirectory(bundledRoot);
            File.WriteAllBytes(Path.Combine(bundledRoot, "heiy01.png"), [0x89, 0x50, 0x4E, 0x47]);
            AssertEqual(bundledRoot, PortraitFrameAssetDirectoryService.ResolveInitialDirectory(project, bundledRoot), "portrait frame directory prefers bundled frames");

            var knownDirectories = PortraitFrameAssetDirectoryService.GetKnownFrameDirectories(project);
            AssertTrue(knownDirectories.Contains(PortableInstallPaths.PortraitFramesRoot, StringComparer.OrdinalIgnoreCase) ||
                       !Directory.Exists(PortableInstallPaths.PortraitFramesRoot), "portrait frame known directories tolerates bundled runtime path");
            AssertTrue(knownDirectories.Contains(workspaceFrameRoot, StringComparer.OrdinalIgnoreCase), "portrait frame known directories include workspace frames");
            AssertTrue(knownDirectories.Contains(gameRoot, StringComparer.OrdinalIgnoreCase), "portrait frame known directories include game root");

            Console.WriteLine("PORTRAIT_FRAME_DIRECTORY_SMOKE_OK root=" + smokeRoot);
        }
        finally
        {
            try
            {
                if (Directory.Exists(smokeRoot))
                {
                    Directory.Delete(smokeRoot, recursive: true);
                }
            }
            catch
            {
                // Temp cleanup is best effort.
            }
        }
    }
}
