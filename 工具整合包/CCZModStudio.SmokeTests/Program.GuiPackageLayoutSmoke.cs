internal partial class Program
{
    private static void RunGuiPackageLayoutSmoke()
    {
        var publishRoot = ResolveGuiPackagePublishRoot();
        if (!Directory.Exists(publishRoot))
        {
            throw new InvalidOperationException(
                "GUI package layout smoke requires a published CCZModStudio output. " +
                "Run: dotnet publish CCZModStudio\\CCZModStudio.csproj -c Release --artifacts-path _BuildCheck\\GuiPackageLayoutArtifacts");
        }

        var expectedRootEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "net",
            "普罗工具整合包.exe",
            "说明.txt",
            "普罗工具整合包使用说明.md",
            "剧本文本导入AI说明模板.md"
        };
        var actualRootEntries = Directory.EnumerateFileSystemEntries(publishRoot)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in expectedRootEntries)
        {
            AssertTrue(actualRootEntries.Contains(expected), "GUI package root contains " + expected);
        }

        var unexpected = actualRootEntries
            .Where(entry => !expectedRootEntries.Contains(entry))
            .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToList();
        AssertEqual(string.Empty, string.Join(", ", unexpected), "GUI package root has no extra runtime entries");

        foreach (var forbidden in new[]
                 {
                     "CCZModStudio.dll",
                     "CCZModStudio.exe",
                     "CCZModStudio.deps.json",
                     "CCZModStudio.runtimeconfig.json",
                     "Assets",
                     "ConfigTable",
                     "LegacyResources",
                     "Package",
                     "Templates"
                 })
        {
            AssertTrue(!actualRootEntries.Contains(forbidden), "GUI package root does not contain " + forbidden);
        }

        var netRoot = Path.Combine(publishRoot, "net");
        foreach (var relative in new[]
                 {
                     "CCZModStudio.dll",
                     "CCZModStudio.exe",
                     Path.Combine("ConfigTable", "HexTable.xml"),
                     "LegacyResources",
                     Path.Combine("Package", "self-check.json"),
                     Path.Combine("Templates", "剧本文本导入AI说明模板.md")
                 })
        {
            var path = Path.Combine(netRoot, relative);
            AssertTrue(File.Exists(path) || Directory.Exists(path), "GUI package net contains " + relative);
        }

        Console.WriteLine("GUI_PACKAGE_LAYOUT_SMOKE_OK root=" + publishRoot);
    }

    private static string ResolveGuiPackagePublishRoot()
    {
        var configured = Environment.GetEnvironmentVariable("CCZMODSTUDIO_GUI_PACKAGE_PUBLISH_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var baseDir = AppContext.BaseDirectory;
        var current = new DirectoryInfo(baseDir);
        for (var i = 0; current != null && i < 8; i++, current = current.Parent)
        {
            if (!File.Exists(Path.Combine(current.FullName, "CCZModStudio.sln")))
            {
                continue;
            }

            return Path.Combine(
                current.FullName,
                "_BuildCheck",
                "GuiPackageLayoutArtifacts",
                "publish",
                "CCZModStudio",
                "release_win-x64");
        }

        return Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "_BuildCheck",
            "GuiPackageLayoutArtifacts",
            "publish",
            "CCZModStudio",
            "release_win-x64"));
    }
}
