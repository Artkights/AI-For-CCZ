using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ProjectDetector
{
    private const int MaxRecursiveSearchDepth = 6;

    public static string FindSceneDictionaryPath(string workspaceRoot)
    {
        var roots = BuildSearchRoots(workspaceRoot, Environment.CurrentDirectory, AppContext.BaseDirectory);
        return FindSceneDictionaryPathInRoots(roots) ?? BuildSceneDictionaryCandidates(NormalizeDirectory(workspaceRoot)).First();
    }

    public static string FindSceneDictionaryPath(CczProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.SceneDictionaryPath))
        {
            return project.SceneDictionaryPath;
        }

        var roots = BuildSearchRoots(project.WorkspaceRoot, project.GameRoot, Path.GetDirectoryName(project.HexTableXmlPath), Environment.CurrentDirectory, AppContext.BaseDirectory);
        return FindSceneDictionaryPathInRoots(roots) ?? BuildSceneDictionaryCandidates(project.WorkspaceRoot).First();
    }

    public static string BuildMissingHexTableMessage(CczProject project)
    {
        var lines = new List<string>
        {
            "找不到 HexTable.xml。跨设备迁移后，请确认旧 CczRSX 配置仍随项目一起复制，或把 HexTable.xml 放到以下任一结构：",
            "- <工作区>\\老版游戏制作工具\\CczRSX 6.5\\ConfigTable\\HexTable.xml",
            "- <工作区>\\CczRSX 6.5\\ConfigTable\\HexTable.xml",
            "- <游戏目录或工作区>\\ConfigTable\\HexTable.xml",
            "",
            "当前解析结果：",
            "WorkspaceRoot=" + project.WorkspaceRoot,
            "GameRoot=" + project.GameRoot,
            "HexTable=" + project.HexTableXmlPath
        };

        if (project.PathDiagnostics.Count > 0)
        {
            lines.Add("");
            lines.Add("搜索诊断：");
            lines.AddRange(project.PathDiagnostics.Take(12));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string? FindPortableDirectory(string workspaceRoot, string directoryName, params string[] relativeCandidates)
    {
        var roots = BuildSearchRoots(workspaceRoot, Environment.CurrentDirectory, AppContext.BaseDirectory);
        return FindPortableDirectoryInRoots(roots, directoryName, relativeCandidates);
    }

    public static string? FindPortableDirectory(CczProject project, string directoryName, params string[] relativeCandidates)
    {
        var known = FindKnownPortableDirectory(project, directoryName);
        if (!string.IsNullOrWhiteSpace(known))
        {
            return known;
        }

        var roots = BuildSearchRoots(project.WorkspaceRoot, project.GameRoot, Path.GetDirectoryName(project.HexTableXmlPath), Environment.CurrentDirectory, AppContext.BaseDirectory);
        return FindPortableDirectoryInRoots(roots, directoryName, relativeCandidates);
    }

    public static string? FindPortableFile(CczProject project, string fileName, params string[] relativeCandidates)
    {
        if (fileName.Equals("System.ini", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.ImageAssignerSystemIniPath) &&
            File.Exists(project.ImageAssignerSystemIniPath))
        {
            return project.ImageAssignerSystemIniPath;
        }

        var roots = BuildSearchRoots(project.WorkspaceRoot, project.GameRoot, Path.GetDirectoryName(project.HexTableXmlPath), Environment.CurrentDirectory, AppContext.BaseDirectory);
        return FindPortableFileInRoots(roots, fileName, relativeCandidates);
    }

    public CczProject DetectDefaultProject()
    {
        var diagnostics = new List<string>();
        var roots = BuildSearchRoots(Environment.CurrentDirectory, AppContext.BaseDirectory);
        diagnostics.Add("SearchRoots=" + string.Join(" | ", roots.Take(10)));

        var gameRoot = FindGameRootInRoots(roots);
        var hexTable = FindHexTableInRoots(roots);
        var workspace = ResolveWorkspaceRoot(gameRoot, hexTable, roots.FirstOrDefault() ?? Environment.CurrentDirectory);

        gameRoot ??= ResolveDefaultGameRoot(workspace);
        hexTable ??= BuildHexTableCandidates(workspace).First();

        diagnostics.Add("ResolvedWorkspace=" + workspace);
        diagnostics.Add("ResolvedGameRoot=" + gameRoot);
        diagnostics.Add("ResolvedHexTable=" + hexTable);

        return CreateProject(workspace, gameRoot, hexTable, roots, diagnostics);
    }

    public CczProject CreateProjectFromGameRoot(string gameRoot)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        var diagnostics = new List<string>();
        var roots = BuildSearchRoots(gameRoot, Environment.CurrentDirectory, AppContext.BaseDirectory);
        diagnostics.Add("SearchRoots=" + string.Join(" | ", roots.Take(10)));

        var hexTable = FindHexTableInRoots(roots);
        var workspace = ResolveWorkspaceRoot(gameRoot, hexTable, roots.FirstOrDefault() ?? Environment.CurrentDirectory);
        hexTable ??= BuildHexTableCandidates(workspace).First();

        diagnostics.Add("ResolvedWorkspace=" + workspace);
        diagnostics.Add("ResolvedGameRoot=" + gameRoot);
        diagnostics.Add("ResolvedHexTable=" + hexTable);

        return CreateProject(workspace, gameRoot, hexTable, roots, diagnostics);
    }

    private static CczProject CreateProject(string workspace, string gameRoot, string hexTable, IReadOnlyList<string> roots, List<string> diagnostics)
    {
        var sceneDictionary = FindSceneDictionaryPathInRoots(roots) ?? BuildSceneDictionaryCandidates(workspace).First();
        var sceneEditorDirectory = FindPortableDirectoryInRoots(
            roots,
            "a新剧本编辑器v0.23",
            new[] { Path.Combine("老版游戏制作工具", "a新剧本编辑器v0.23"), "a新剧本编辑器v0.23" });
        var imageAssignerDirectory = FindPortableDirectoryInRoots(
            roots,
            "形象指定器6.5",
            new[]
            {
                Path.Combine("老版游戏制作工具", "B形象指定器", "形象指定器6.5"),
                Path.Combine("B形象指定器", "形象指定器6.5")
            });
        var imageAssignerSystemIni = imageAssignerDirectory == null ? null : Path.Combine(imageAssignerDirectory, "System.ini");
        if (imageAssignerSystemIni == null || !File.Exists(imageAssignerSystemIni))
        {
            imageAssignerSystemIni = FindPortableFileInRoots(
                roots,
                "System.ini",
                new[]
                {
                    Path.Combine("老版游戏制作工具", "B形象指定器", "形象指定器6.5", "System.ini"),
                    Path.Combine("B形象指定器", "形象指定器6.5", "System.ini")
                });
        }

        var materialLibraryRoot = FindPortableDirectoryInRoots(
            roots,
            "素材库",
            new[]
            {
                Path.Combine("普罗-综合工具v0.3", "素材库"),
                Path.Combine("老版游戏制作工具", "普罗-综合工具v0.3", "素材库"),
                Path.Combine("老版游戏制作工具", "素材库"),
                "素材库"
            });
        var patchConfigRoot = FindPortableDirectoryInRoots(roots, "普罗-搬运 注入", new[] { "普罗-搬运 注入" });

        diagnostics.Add("ResolvedSceneDictionary=" + sceneDictionary);
        diagnostics.Add("ResolvedSceneEditorDirectory=" + (sceneEditorDirectory ?? "<missing>"));
        diagnostics.Add("ResolvedImageAssignerDirectory=" + (imageAssignerDirectory ?? "<missing>"));
        diagnostics.Add("ResolvedImageAssignerSystemIni=" + (imageAssignerSystemIni ?? "<missing>"));
        diagnostics.Add("ResolvedMaterialLibraryRoot=" + (materialLibraryRoot ?? "<missing>"));
        diagnostics.Add("ResolvedPatchConfigRoot=" + (patchConfigRoot ?? "<missing>"));

        return new CczProject
        {
            WorkspaceRoot = workspace,
            GameRoot = gameRoot,
            HexTableXmlPath = hexTable,
            SceneDictionaryPath = sceneDictionary,
            SceneEditorDirectory = sceneEditorDirectory,
            ImageAssignerDirectory = imageAssignerDirectory,
            ImageAssignerSystemIniPath = imageAssignerSystemIni,
            MaterialLibraryRoot = materialLibraryRoot,
            PatchConfigRoot = patchConfigRoot,
            PathDiagnostics = diagnostics
        };
    }

    private static string? FindKnownPortableDirectory(CczProject project, string directoryName)
    {
        if (directoryName.Equals("a新剧本编辑器v0.23", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.SceneEditorDirectory) &&
            Directory.Exists(project.SceneEditorDirectory))
        {
            return project.SceneEditorDirectory;
        }

        if (directoryName.Equals("形象指定器6.5", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.ImageAssignerDirectory) &&
            Directory.Exists(project.ImageAssignerDirectory))
        {
            return project.ImageAssignerDirectory;
        }

        if (directoryName.Equals("素材库", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.MaterialLibraryRoot) &&
            Directory.Exists(project.MaterialLibraryRoot))
        {
            return project.MaterialLibraryRoot;
        }

        if (directoryName.Equals("普罗-搬运 注入", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.PatchConfigRoot) &&
            Directory.Exists(project.PatchConfigRoot))
        {
            return project.PatchConfigRoot;
        }

        return null;
    }

    private static string ResolveWorkspaceRoot(string? gameRoot, string? hexTable, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(gameRoot) && !string.IsNullOrWhiteSpace(hexTable))
        {
            var common = FindCommonAncestor(gameRoot, hexTable);
            if (!string.IsNullOrWhiteSpace(common) && !IsDriveRoot(common))
            {
                return common;
            }
        }

        if (!string.IsNullOrWhiteSpace(gameRoot))
        {
            var gameWorkspace = FindWorkspaceRootAroundGameRoot(gameRoot);
            if (gameWorkspace != null) return gameWorkspace;
            return Directory.GetParent(gameRoot)?.FullName ?? gameRoot;
        }

        if (!string.IsNullOrWhiteSpace(hexTable))
        {
            return FindWorkspaceRootAroundHexTable(hexTable) ?? Directory.GetParent(hexTable)?.FullName ?? fallback;
        }

        return NormalizeDirectory(fallback);
    }

    private static string? FindWorkspaceRootAroundGameRoot(string gameRoot)
    {
        var dir = new DirectoryInfo(gameRoot);
        while (dir != null)
        {
            if (LooksLikeWorkspaceRoot(dir.FullName)) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "基底")) ||
                Directory.Exists(Path.Combine(dir.FullName, "老版游戏制作工具")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string? FindWorkspaceRootAroundHexTable(string hexTable)
    {
        var dir = Directory.GetParent(hexTable);
        while (dir != null)
        {
            if (dir.Name.Equals("老版游戏制作工具", StringComparison.OrdinalIgnoreCase))
            {
                return dir.Parent?.FullName;
            }

            if (dir.Name.Equals("CczRSX 6.5", StringComparison.OrdinalIgnoreCase))
            {
                var parent = dir.Parent;
                if (parent?.Name.Equals("老版游戏制作工具", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return parent.Parent?.FullName;
                }

                return parent?.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool LooksLikeWorkspaceRoot(string path) =>
        BuildHexTableCandidates(path).Any(File.Exists) ||
        Directory.Exists(Path.Combine(path, "基底", "加强版6.5未加密版")) ||
        Directory.Exists(Path.Combine(path, "老版游戏制作工具"));

    private static string? FindGameRootInRoots(IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            foreach (var candidate in BuildGameRootCandidates(root).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (LooksLikeGameRoot(candidate)) return Path.GetFullPath(candidate);
            }
        }

        foreach (var root in roots.Where(Directory.Exists).Where(root => !IsDriveRoot(root)))
        {
            var found = EnumerateDirectoriesLimited(root, MaxRecursiveSearchDepth)
                .Where(LooksLikeGameRoot)
                .OrderBy(path => ScoreGameRoot(path))
                .ThenBy(path => path.Length)
                .FirstOrDefault();
            if (found != null) return Path.GetFullPath(found);
        }

        return null;
    }

    private static bool LooksLikeGameRoot(string path)
    {
        if (!Directory.Exists(path)) return false;
        var coreFiles = new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" };
        var coreCount = coreFiles.Count(file => File.Exists(Path.Combine(path, file)));
        return coreCount >= 3 ||
               coreCount >= 1 && Directory.Exists(Path.Combine(path, "RS")) ||
               File.Exists(Path.Combine(path, "_CCZModStudio_TestCopy.txt"));
    }

    private static int ScoreGameRoot(string path)
    {
        var score = 0;
        if (Path.GetFileName(path).Contains("加强版", StringComparison.Ordinal)) score += 40;
        if (Directory.Exists(Path.Combine(path, "RS"))) score += 30;
        if (Directory.Exists(Path.Combine(path, "Map"))) score += 10;
        if (File.Exists(Path.Combine(path, "_CCZModStudio_TestCopy.txt"))) score += 50;
        return -score;
    }

    private static string ResolveDefaultGameRoot(string workspace)
    {
        return BuildGameRootCandidates(workspace).FirstOrDefault(Directory.Exists) ?? BuildGameRootCandidates(workspace).First();
    }

    private static IReadOnlyList<string> BuildGameRootCandidates(string workspace)
    {
        workspace = NormalizeDirectory(workspace);
        return new[]
        {
            Path.Combine(workspace, "基底", "加强版6.5未加密版"),
            Path.Combine(workspace, "加强版6.5未加密版"),
            workspace
        };
    }

    private static string? FindHexTableInRoots(IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            var direct = BuildHexTableCandidates(root).FirstOrDefault(File.Exists);
            if (direct != null) return Path.GetFullPath(direct);
        }

        var matches = new List<string>();
        foreach (var root in roots.Where(Directory.Exists).Where(root => !IsDriveRoot(root)))
        {
            matches.AddRange(EnumerateFilesLimited(root, "HexTable.xml", MaxRecursiveSearchDepth));
        }

        return matches
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ScoreHexTable)
            .ThenBy(path => path.Length)
            .FirstOrDefault();
    }

    private static IReadOnlyList<string> BuildHexTableCandidates(string workspace)
    {
        workspace = NormalizeDirectory(workspace);
        return new[]
        {
            Path.Combine(workspace, "老版游戏制作工具", "CczRSX 6.5", "ConfigTable", "HexTable.xml"),
            Path.Combine(workspace, "CczRSX 6.5", "ConfigTable", "HexTable.xml"),
            Path.Combine(workspace, "ConfigTable", "HexTable.xml"),
            Path.Combine(workspace, "HexTable.xml")
        };
    }

    private static int ScoreHexTable(string path)
    {
        var score = 0;
        if (path.Contains("老版游戏制作工具", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (path.Contains("CczRSX 6.5", StringComparison.OrdinalIgnoreCase)) score += 80;
        if (path.Contains("ConfigTable", StringComparison.OrdinalIgnoreCase)) score += 60;
        if (path.Contains("TestCopies", StringComparison.OrdinalIgnoreCase)) score -= 20;
        if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) score -= 40;
        if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) score -= 40;
        return score;
    }

    private static string? FindSceneDictionaryPathInRoots(IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            var direct = BuildSceneDictionaryCandidates(root).FirstOrDefault(File.Exists);
            if (direct != null) return Path.GetFullPath(direct);
        }

        var matches = new List<string>();
        foreach (var root in roots.Where(Directory.Exists).Where(root => !IsDriveRoot(root)))
        {
            matches.AddRange(EnumerateFilesLimited(root, "CczString.ini", MaxRecursiveSearchDepth));
        }

        return matches
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ScoreSceneDictionary)
            .ThenBy(path => path.Length)
            .FirstOrDefault();
    }

    private static string? FindPortableDirectoryInRoots(IReadOnlyList<string> roots, string directoryName, IReadOnlyList<string> relativeCandidates)
    {
        foreach (var root in roots)
        {
            foreach (var relative in relativeCandidates)
            {
                var candidate = BuildPortableCandidate(root, relative);
                if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }

        var matches = new List<string>();
        foreach (var root in roots.Where(Directory.Exists).Where(root => !IsDriveRoot(root)))
        {
            matches.AddRange(EnumerateDirectoriesLimited(root, MaxRecursiveSearchDepth)
                .Where(path => IsDirectoryName(path, directoryName)));
        }

        return matches
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => ScorePortablePath(path, relativeCandidates))
            .ThenBy(path => path.Length)
            .FirstOrDefault();
    }

    private static string? FindPortableFileInRoots(IReadOnlyList<string> roots, string fileName, IReadOnlyList<string> relativeCandidates)
    {
        foreach (var root in roots)
        {
            foreach (var relative in relativeCandidates)
            {
                var candidate = BuildPortableCandidate(root, relative);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }

        var matches = new List<string>();
        foreach (var root in roots.Where(Directory.Exists).Where(root => !IsDriveRoot(root)))
        {
            matches.AddRange(EnumerateFilesLimited(root, fileName, MaxRecursiveSearchDepth));
        }

        return matches
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => ScorePortablePath(path, relativeCandidates))
            .ThenBy(path => path.Length)
            .FirstOrDefault();
    }

    private static string BuildPortableCandidate(string root, string relativePath) =>
        Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(root, relativePath);

    private static bool IsDirectoryName(string path, string directoryName)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return !string.IsNullOrWhiteSpace(name) && name.Equals(directoryName, StringComparison.OrdinalIgnoreCase);
    }

    private static int ScorePortablePath(string path, IReadOnlyList<string> relativeCandidates)
    {
        var normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var score = 0;
        foreach (var relative in relativeCandidates)
        {
            var normalizedRelative = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (normalizedPath.Contains(normalizedRelative, StringComparison.OrdinalIgnoreCase)) score += 80;
        }

        if (path.Contains("老版游戏制作工具", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) score -= 40;
        if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) score -= 40;
        if (path.Contains("TestCopies", StringComparison.OrdinalIgnoreCase)) score -= 20;
        return score;
    }

    private static IReadOnlyList<string> BuildSceneDictionaryCandidates(string workspaceRoot)
    {
        workspaceRoot = NormalizeDirectory(workspaceRoot);
        return new[]
        {
            Path.Combine(workspaceRoot, "老版游戏制作工具", "a新剧本编辑器v0.23", "CczString.ini"),
            Path.Combine(workspaceRoot, "a新剧本编辑器v0.23", "CczString.ini"),
            Path.Combine(workspaceRoot, "CczString.ini")
        };
    }

    private static int ScoreSceneDictionary(string path)
    {
        var score = 0;
        if (path.Contains("老版游戏制作工具", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (path.Contains("a新剧本编辑器v0.23", StringComparison.OrdinalIgnoreCase)) score += 80;
        if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) score -= 40;
        if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) score -= 40;
        return score;
    }

    private static IReadOnlyList<string> BuildSearchRoots(params string?[] starts)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var start in starts)
        {
            var dir = NormalizeDirectory(start);
            if (string.IsNullOrWhiteSpace(dir)) continue;

            var current = new DirectoryInfo(dir);
            while (current != null)
            {
                AddRoot(current.FullName);
                current = current.Parent;
            }
        }

        return result;

        void AddRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }

            fullPath = NormalizeSearchRoot(fullPath);
            if (seen.Add(fullPath)) result.Add(fullPath);
        }
    }

    private static string NormalizeDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Environment.CurrentDirectory;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath)) return Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
            return fullPath;
        }
        catch
        {
            return Environment.CurrentDirectory;
        }
    }

    private static IEnumerable<string> EnumerateFilesLimited(string root, string pattern, int maxDepth)
    {
        foreach (var directory in EnumerateDirectoriesWithSelfLimited(root, maxDepth))
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesLimited(string root, int maxDepth) =>
        EnumerateDirectoriesWithSelfLimited(root, maxDepth).Skip(1);

    private static IEnumerable<string> EnumerateDirectoriesWithSelfLimited(string root, int maxDepth)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (path, depth) = queue.Dequeue();
            yield return path;
            if (depth >= maxDepth) continue;

            string[] children;
            try
            {
                children = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                if (ShouldSkipSearchDirectory(child)) continue;
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static bool ShouldSkipSearchDirectory(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            name.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CCZModStudio_Exports", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CCZModStudio_Reports", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("_CCZModStudio_Backups", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static string? FindCommonAncestor(string left, string right)
    {
        try
        {
            left = Path.GetFullPath(left);
            right = Path.GetFullPath(right);
        }
        catch
        {
            return null;
        }

        var leftParts = SplitPath(left);
        var rightParts = SplitPath(right);
        var count = Math.Min(leftParts.Count, rightParts.Count);
        var common = new List<string>();

        for (var i = 0; i < count; i++)
        {
            if (!leftParts[i].Equals(rightParts[i], StringComparison.OrdinalIgnoreCase)) break;
            common.Add(leftParts[i]);
        }

        if (common.Count == 0) return null;
        var root = Path.GetPathRoot(left);
        if (root == null) return null;
        if (common.Count == 1 && common[0].Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        var combinedParts = new[] { root }.Concat(common.Skip(1)).ToArray();
        return Path.Combine(combinedParts);
    }

    private static List<string> SplitPath(string path)
    {
        var root = Path.GetPathRoot(path);
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(root))
        {
            parts.Add(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            path = path[root.Length..];
        }

        parts.AddRange(path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries));
        return parts;
    }

    private static bool IsDriveRoot(string path)
    {
        try
        {
            var fullPath = NormalizeSearchRoot(Path.GetFullPath(path));
            var root = Path.GetPathRoot(fullPath);
            return root != null && fullPath.Equals(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeSearchRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        if (root != null &&
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
