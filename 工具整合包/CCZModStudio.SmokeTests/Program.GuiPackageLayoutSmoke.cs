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

        AssertTrue(!actualRootEntries.Contains("log"), "clean GUI package root does not precreate log");
        AssertTrue(!actualRootEntries.Contains("cache"), "clean GUI package root does not precreate cache");

        var usageGuidePath = Path.Combine(publishRoot, "普罗工具整合包使用说明.md");
        var usageGuideMarkdown = File.ReadAllText(usageGuidePath, System.Text.Encoding.UTF8);
        foreach (var heading in new[] { "## 通用操作", "## 角色设定", "## 兵种设定", "## 图片设定", "## 形象设定", "## 地图编辑", "## 写回与备份边界" })
        {
            AssertTrue(usageGuideMarkdown.Contains(heading, StringComparison.Ordinal), "GUI package usage guide contains " + heading);
        }

        foreach (var forbidden in new[] { "个人特效(EXE表)", "导出PNG", "导出美化JPG", "角色RAW统一", "还原E5条目", "导出缺失报告" })
        {
            AssertTrue(!usageGuideMarkdown.Contains(forbidden, StringComparison.Ordinal), "GUI package usage guide omits hidden entry " + forbidden);
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
                     Path.Combine("Assets", "PortraitFrames"),
                     Path.Combine("Package", "self-check.json"),
                     Path.Combine("Templates", "剧本文本导入AI说明模板.md")
                 })
        {
            var path = Path.Combine(netRoot, relative);
            AssertTrue(File.Exists(path) || Directory.Exists(path), "GUI package net contains " + relative);
        }

        var portraitFrameRoot = Path.Combine(netRoot, "Assets", "PortraitFrames");
        var portraitFramePngs = Directory.Exists(portraitFrameRoot)
            ? Directory.GetFiles(portraitFrameRoot, "*.png", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
        AssertTrue(portraitFramePngs.Length >= 21, "GUI package net contains bundled portrait frame PNGs");
        foreach (var frameFileName in new[] { "heiy01.png", "魏.png", "蜀.png", "吴.png", "群.png", "魔.png" })
        {
            AssertTrue(File.Exists(Path.Combine(portraitFrameRoot, frameFileName)), "GUI package portrait frames contain " + frameFileName);
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
