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

    public string Name => System.IO.Path.GetFileName(GameRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

    public bool IsTestCopy => File.Exists(System.IO.Path.Combine(GameRoot, "_CCZModStudio_TestCopy.txt"));

    public string ResolveGameFile(string fileName) => System.IO.Path.Combine(GameRoot, fileName);

    public IReadOnlyList<ProjectFileStatus> GetFileStatuses()
    {
        var core = new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" };
        var optional = new[] { "Hexzmap.e5", "Smlmap.e5", "Spalet.e5" };
        var result = new List<ProjectFileStatus>();

        foreach (var file in core)
        {
            var path = ResolveGameFile(file);
            var info = new FileInfo(path);
            result.Add(new ProjectFileStatus(file, path, info.Exists, info.Exists ? info.Length : null, "核心"));
        }

        foreach (var file in optional)
        {
            var path = ResolveGameFile(file);
            var info = new FileInfo(path);
            result.Add(new ProjectFileStatus(file, path, info.Exists, info.Exists ? info.Length : null, "可选"));
        }

        var rsPath = ResolveGameFile("RS");
        var rsInfo = new DirectoryInfo(rsPath);
        result.Add(new ProjectFileStatus("RS", rsPath, rsInfo.Exists, null, "资源目录"));

        return result;
    }
}
