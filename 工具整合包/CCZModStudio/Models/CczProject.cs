using CCZModStudio.Core;

namespace CCZModStudio.Models;

public sealed class CczProject
{
    public required string WorkspaceRoot { get; init; }
    public required string GameRoot { get; init; }
    public required string HexTableXmlPath { get; init; }
    public string SceneDictionaryPath { get; init; } = string.Empty;
    public string? SceneEditorDirectory { get; init; }
    public string? ImageAssignerDirectory { get; init; }
    public string? ImageAssignerSystemIniPath { get; init; }
    public string? MaterialLibraryRoot { get; init; }
    public string? PatchConfigRoot { get; init; }
    public IReadOnlyList<string> PathDiagnostics { get; init; } = Array.Empty<string>();

    public string Name => Path.GetFileName(GameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public bool IsTestCopy => File.Exists(Path.Combine(GameRoot, "_CCZModStudio_TestCopy.txt"));

    public string ResolveGameFile(string fileName) => Path.Combine(GameRoot, fileName);

    public IReadOnlyList<ProjectFileStatus> GetFileStatuses()
    {
        var core = new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" };
        var is66 = Ccz66RevisedLayout.Is66(this);
        var optional = is66
            ? new[] { "Hexzmap.e5" }
            : new[] { "Hexzmap.e5", "Smlmap.e5", "Spalet.e5" };
        var result = new List<ProjectFileStatus>();

        foreach (var file in core)
        {
            AddFileStatus(result, file, "core");
        }

        foreach (var file in optional)
        {
            AddFileStatus(result, file, "optional");
        }

        if (is66)
        {
            foreach (var file in Ccz66RevisedLayout.RequiredE5Resources)
            {
                AddFileStatus(result, file, "6.6-required-resource");
            }

            foreach (var file in new[] { "Smlmap.e5", "Spalet.e5" })
            {
                AddFileStatus(result, file, "legacy-optional", false, "6.6 revised baseline does not require this legacy resource.");
            }

            foreach (var file in new[] { "Itemicon.dll", "Mgcicon.dll", "ts.e5" })
            {
                AddFileStatus(result, file, "6.6-obsolete", false, "6.6 revised moved this resource to E5 files; do not count as missing.");
            }

            AddFileStatus(
                result,
                "Mapatr.dll",
                "legacy-terrain-editor-dependency",
                false,
                "Runtime terrain image moved to E5\\U_select.e5 #32; legacy terrain editors may still need this DLL.");
        }

        var rsPath = ResolveGameFile("RS");
        var rsInfo = new DirectoryInfo(rsPath);
        result.Add(new ProjectFileStatus("RS", rsPath, rsInfo.Exists, null, "resource-directory"));

        return result;
    }

    private void AddFileStatus(
        List<ProjectFileStatus> result,
        string file,
        string kind,
        bool countsAsMissing = true,
        string note = "")
    {
        var path = ResolveGameFile(file);
        var info = new FileInfo(path);
        result.Add(new ProjectFileStatus(file, path, info.Exists, info.Exists ? info.Length : null, kind, countsAsMissing, note));
    }
}
