namespace CCZModStudio.Core;

public static class PortableInstallPaths
{
    public static string RuntimeRoot => EnsureTrailingSlash(Path.GetFullPath(AppContext.BaseDirectory));

    public static string ConfigTableRoot => Path.Combine(RuntimeRoot, "ConfigTable");

    public static string LegacyResourcesRoot => Path.Combine(RuntimeRoot, "LegacyResources");

    public static string AssetsRoot => Path.Combine(RuntimeRoot, "Assets");

    public static string PalettesRoot => Path.Combine(AssetsRoot, "Palettes");

    public static string TemplatesRoot => Path.Combine(RuntimeRoot, "Templates");

    public static string PackageRoot => Path.Combine(RuntimeRoot, "Package");

    public static string HexTablePath => Path.Combine(ConfigTableRoot, "HexTable.xml");

    public static string PaletteTsbPath => Path.Combine(PalettesRoot, "tsb");

    public static string ScenarioTextImportTemplatePath => Path.Combine(TemplatesRoot, "剧本文本导入AI说明模板.md");

    public static string PackageManifestPath => Path.Combine(PackageRoot, "GUI-PACKAGE-MANIFEST.txt");

    public static string SelfCheckPath => Path.Combine(PackageRoot, "self-check.json");

    public static string AboutAsset(string fileName) => Path.Combine(AssetsRoot, "About", fileName);

    public static string LegacyResource(params string[] relativeParts)
    {
        var parts = new string[relativeParts.Length + 1];
        parts[0] = LegacyResourcesRoot;
        Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
        return Path.Combine(parts);
    }

    private static string EnsureTrailingSlash(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
