namespace CCZModStudio.Core;

public static class PortableInstallPaths
{
    private const string LauncherFileName = "普罗工具整合包.exe";

    public static string RuntimeRoot => EnsureTrailingSlash(Path.GetFullPath(AppContext.BaseDirectory));

    public static string LauncherRoot => EnsureTrailingSlash(ResolveLauncherRoot());

    public static string LogRoot => Path.Combine(LauncherRoot, "log");

    public static string CacheRoot => Path.Combine(LauncherRoot, "cache");

    public static string ConfigRoot => Path.Combine(LauncherRoot, "config");

    public static string BackupSettingsPath => Path.Combine(ConfigRoot, "backup-settings.json");

    public static string ConfigTableRoot => Path.Combine(RuntimeRoot, "ConfigTable");

    public static string LegacyResourcesRoot => Path.Combine(RuntimeRoot, "LegacyResources");

    public static string AssetsRoot => Path.Combine(RuntimeRoot, "Assets");

    public static string PalettesRoot => Path.Combine(AssetsRoot, "Palettes");

    public static string PortraitFramesRoot => Path.Combine(AssetsRoot, "PortraitFrames");

    public static string TemplatesRoot => Path.Combine(RuntimeRoot, "Templates");

    public static string PackageRoot => Path.Combine(RuntimeRoot, "Package");

    public static string MapWorkbenchStandaloneNotesRoot => Path.Combine(LauncherRoot, "CCZModStudio_Notes");

    public static string MapWorkbenchStandaloneExportsRoot => Path.Combine(LauncherRoot, "CCZModStudio_Exports");

    public static string HexTablePath => Path.Combine(ConfigTableRoot, "HexTable.xml");

    public static string PaletteTsbPath => Path.Combine(PalettesRoot, "tsb");

    public static string ScenarioTextImportTemplatePath => Path.Combine(TemplatesRoot, "剧本文本导入AI说明模板.md");

    public static string PackageManifestPath => Path.Combine(PackageRoot, "GUI-PACKAGE-MANIFEST.txt");

    public static string SelfCheckPath => Path.Combine(PackageRoot, "self-check.json");

    public static string AboutAsset(string fileName) => Path.Combine(AssetsRoot, "About", fileName);

    public static string PortraitFrameAsset(string fileName) => Path.Combine(PortraitFramesRoot, fileName);

    public static string LegacyResource(params string[] relativeParts)
    {
        var parts = new string[relativeParts.Length + 1];
        parts[0] = LegacyResourcesRoot;
        Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
        return Path.Combine(parts);
    }

    private static string ResolveLauncherRoot()
        => ResolveLauncherRoot(AppContext.BaseDirectory, Environment.ProcessPath);

    internal static string ResolveLauncherRoot(string runtimeDirectory, string? processPath)
    {
        runtimeDirectory = Path.GetFullPath(runtimeDirectory);
        var processDirectory = Path.GetDirectoryName(processPath);
        var current = Path.GetFullPath(string.IsNullOrWhiteSpace(processDirectory) ? runtimeDirectory : processDirectory);
        if (!LooksLikeLauncherRoot(current) &&
            !Path.GetFileName(current).Equals("net", StringComparison.OrdinalIgnoreCase))
        {
            current = runtimeDirectory;
        }

        var parent = Directory.GetParent(current)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent) &&
            Path.GetFileName(current).Equals("net", StringComparison.OrdinalIgnoreCase) &&
            LooksLikeLauncherRoot(parent))
        {
            return parent;
        }

        if (LooksLikeLauncherRoot(current)) return current;

        return current;
    }

    private static bool LooksLikeLauncherRoot(string path)
        => File.Exists(Path.Combine(path, LauncherFileName)) ||
           File.Exists(Path.Combine(path, "Package", "GUI-PACKAGE-MANIFEST.txt")) ||
           File.Exists(Path.Combine(path, "普罗工具整合包使用说明.md"));

    private static string EnsureTrailingSlash(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
