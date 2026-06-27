using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ProjectDetector
{
    private const int MaxRecursiveSearchDepth = 6;

    public static string FindSceneDictionaryPath(string workspaceRoot)
    {
        var roots = BuildDefaultProjectSearchRoots(workspaceRoot);
        return FindSceneDictionaryPathInRoots(roots) ??
               FindBuiltInLegacyFile(Path.Combine("a新剧本编辑器v0.23", "CczString.ini")) ??
               BuildSceneDictionaryCandidates(NormalizeDirectory(workspaceRoot)).First();
    }

    public static string FindSceneDictionaryPath(CczProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.SceneDictionaryPath))
        {
            return project.SceneDictionaryPath;
        }

        var roots = BuildProjectResourceRoots(project);
        return FindSceneDictionaryPathInRoots(roots) ??
               FindBuiltInLegacyFile(Path.Combine("a新剧本编辑器v0.23", "CczString.ini")) ??
               BuildSceneDictionaryCandidates(project.WorkspaceRoot).First();
    }

    public static string BuildMissingHexTableMessage(CczProject project)
    {
        var lines = new List<string>
        {
            "找不到项目内 HexTable.xml，且 net 内置备份也不可用。跨设备迁移后，请确认 net 文件夹完整，或把项目专用 HexTable.xml 放到当前游戏项目目录的以下任一位置：",
            "- <游戏项目目录>\\ConfigTable\\HexTable.xml",
            "- <游戏项目目录>\\HexTable.xml",
            "- <游戏项目目录>\\CczRSX 6.X\\ConfigTable\\HexTable.xml（例如 CczRSX 6.6 / 6.5 / 6.4）",
            "- <游戏项目目录>\\老版游戏制作工具\\CczRSX 6.X\\ConfigTable\\HexTable.xml",
            "- <net>\\ConfigTable\\HexTable.xml（内置备份）",
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
        var roots = BuildDefaultProjectSearchRoots(workspaceRoot);
        return FindPortableDirectoryInRoots(roots, directoryName, relativeCandidates) ??
               FindBuiltInLegacyDirectory(directoryName, relativeCandidates);
    }

    public static string? FindPortableDirectory(CczProject project, string directoryName, params string[] relativeCandidates)
    {
        var known = FindKnownPortableDirectory(project, directoryName);
        if (!string.IsNullOrWhiteSpace(known))
        {
            return known;
        }

        var roots = BuildProjectResourceRoots(project);
        return FindPortableDirectoryInRoots(roots, directoryName, relativeCandidates) ??
               FindBuiltInLegacyDirectory(directoryName, relativeCandidates);
    }

    public static string? FindPortableFile(CczProject project, string fileName, params string[] relativeCandidates)
    {
        if (fileName.Equals("System.ini", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.ImageAssignerSystemIniPath) &&
            File.Exists(project.ImageAssignerSystemIniPath))
        {
            return project.ImageAssignerSystemIniPath;
        }

        var roots = BuildProjectResourceRoots(project);
        return FindPortableFileInRoots(roots, fileName, relativeCandidates) ??
               FindBuiltInLegacyFile(relativeCandidates);
    }

    public CczProject DetectDefaultProject()
    {
        var diagnostics = new List<string>();
        var roots = BuildDefaultProjectSearchRoots(Environment.CurrentDirectory);
        diagnostics.Add("SearchRoots=" + string.Join(" | ", roots.Take(10)));

        var gameRoot = FindGameRootInRoots(roots);
        var fallbackRoot = roots.FirstOrDefault() ?? PortableInstallPaths.RuntimeRoot;
        var workspace = ResolveWorkspaceRoot(gameRoot, fallbackRoot);
        gameRoot ??= ResolveDefaultGameRoot(workspace);
        var hexTable = ResolveHexTablePath(workspace, gameRoot, diagnostics);

        diagnostics.Add("ResolvedWorkspace=" + workspace);
        diagnostics.Add("ResolvedGameRoot=" + gameRoot);
        diagnostics.Add("ResolvedHexTable=" + hexTable);

        return CreateProject(workspace, gameRoot, hexTable, roots, diagnostics);
    }

    public CczProject CreateProjectFromGameRoot(string gameRoot)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        var diagnostics = new List<string>();
        var roots = BuildSearchRoots(gameRoot);
        diagnostics.Add("SearchRoots=" + string.Join(" | ", roots.Take(10)));

        var workspace = ResolveWorkspaceRoot(gameRoot, roots.FirstOrDefault() ?? gameRoot);
        var hexTable = ResolveHexTablePath(workspace, gameRoot, diagnostics);

        diagnostics.Add("ResolvedWorkspace=" + workspace);
        diagnostics.Add("ResolvedGameRoot=" + gameRoot);
        diagnostics.Add("ResolvedHexTable=" + hexTable);

        return CreateProject(workspace, gameRoot, hexTable, roots, diagnostics);
    }

    private static CczProject CreateProject(string workspace, string gameRoot, string hexTable, IReadOnlyList<string> roots, List<string> diagnostics)
    {
        var projectResourceRoots = BuildCurrentGameRoots(gameRoot);
        var sceneDictionary = FindSceneDictionaryPathInRoots(projectResourceRoots) ??
                              FindBuiltInLegacyFile(Path.Combine("a新剧本编辑器v0.23", "CczString.ini")) ??
                              BuildSceneDictionaryCandidates(workspace).First();
        var sceneEditorDirectory = FindPortableDirectoryInRoots(
            projectResourceRoots,
            "a新剧本编辑器v0.23",
            new[] { Path.Combine("老版游戏制作工具", "a新剧本编辑器v0.23"), "a新剧本编辑器v0.23" }) ??
            FindBuiltInLegacyDirectory(
                "a新剧本编辑器v0.23",
                new[] { "a新剧本编辑器v0.23" });
        var imageAssignerCandidates = BuildImageAssignerCandidates(workspace, gameRoot);
        var imageAssignerDirectory = FindPortableDirectoryInRoots(
            projectResourceRoots,
            imageAssignerCandidates.DirectoryName,
            imageAssignerCandidates.RelativeCandidates) ??
            FindBuiltInLegacyDirectory(
                "形象指定器6.5",
                new[] { Path.Combine("B形象指定器", "形象指定器6.5"), "形象指定器6.5" });
        var imageAssignerSystemIni = imageAssignerDirectory == null ? null : Path.Combine(imageAssignerDirectory, "System.ini");
        if (imageAssignerSystemIni == null || !File.Exists(imageAssignerSystemIni))
        {
            imageAssignerSystemIni = FindPortableFileInRoots(
                projectResourceRoots,
                "System.ini",
                imageAssignerCandidates.SystemIniRelativeCandidates) ??
                FindBuiltInLegacyFile(
                    Path.Combine("B形象指定器", "形象指定器6.5", "System.ini"),
                    Path.Combine("形象指定器6.5", "System.ini"));
        }

        var materialLibraryRoot = FindPortableDirectoryInRoots(
            projectResourceRoots,
            "素材库",
            new[]
            {
                Path.Combine("普罗-综合工具v0.3", "素材库"),
                Path.Combine("老版游戏制作工具", "普罗-综合工具v0.3", "素材库"),
                Path.Combine("老版游戏制作工具", "素材库"),
                "素材库"
            }) ??
            FindBuiltInLegacyDirectory(
                "素材库",
                new[]
                {
                    Path.Combine("普罗-综合工具v0.3", "素材库"),
                    "素材库"
                });
        var patchConfigRoot = FindPortableDirectoryInRoots(projectResourceRoots, "普罗-搬运 注入", new[] { "普罗-搬运 注入" }) ??
                              FindBuiltInLegacyDirectory("普罗-搬运 注入", new[] { "普罗-搬运 注入" });

        diagnostics.Add("ProjectResourceRoots=" + string.Join(" | ", projectResourceRoots));

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

        if (directoryName.Contains("形象指定器", StringComparison.OrdinalIgnoreCase) &&
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

    private static string ResolveWorkspaceRoot(string? gameRoot, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(gameRoot))
        {
            var gameWorkspace = FindWorkspaceRootAroundGameRoot(gameRoot);
            if (gameWorkspace != null) return gameWorkspace;
            return Directory.GetParent(gameRoot)?.FullName ?? gameRoot;
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

    private static string ResolveHexTablePath(string workspace, string gameRoot, List<string> diagnostics)
    {
        var projectRoots = BuildCurrentGameRoots(gameRoot);
        var versionHint = Build6xVersionHint(workspace, gameRoot);
        diagnostics.Add("HexTableProjectRoots=" + string.Join(" | ", projectRoots));
        diagnostics.Add("HexTableVersionHint=" + (versionHint ?? "未知"));

        var projectHexTable = FindHexTableInProjectRoots(projectRoots, versionHint);
        if (projectHexTable != null)
        {
            AddHexTableSourceDiagnostics(diagnostics, projectHexTable, versionHint, "Project");
            return projectHexTable;
        }

        var builtInHexTable = FindBuiltInHexTableBackup();
        if (builtInHexTable != null)
        {
            AddHexTableSourceDiagnostics(diagnostics, builtInHexTable, versionHint, "BuiltInBackup");
            return builtInHexTable;
        }

        diagnostics.Add("ResolvedHexTableSource=Missing");
        return BuildHexTableCandidates(gameRoot, versionHint).Concat(BuildHexTableCandidates(workspace, versionHint)).First();
    }

    private static void AddHexTableSourceDiagnostics(List<string> diagnostics, string hexTablePath, string? requestedPrefix, string source)
    {
        var actualPrefixes = ReadHexTableVersionPrefixes(hexTablePath);
        var fallback = !string.IsNullOrWhiteSpace(requestedPrefix) &&
                       requestedPrefix.Equals("6.6", StringComparison.OrdinalIgnoreCase) &&
                       !actualPrefixes.Contains("6.6", StringComparer.OrdinalIgnoreCase);

        diagnostics.Add("ResolvedHexTableSource=" + (fallback ? "CrossVersionFallback" : source));
        if (fallback)
        {
            diagnostics.Add("ResolvedHexTableOriginalSource=" + source);
            diagnostics.Add("requestedPrefix=6.6");
            diagnostics.Add("actualPrefixes=" + (actualPrefixes.Count == 0 ? "<none>" : string.Join(",", actualPrefixes)));
            diagnostics.Add("tableFallback=true");
            diagnostics.Add("CrossVersionFallback=6.6 engine is using a non-6.6 HexTable.xml; writes are not blocked, but offsets must be reviewed.");
        }
        else
        {
            diagnostics.Add("requestedPrefix=" + (requestedPrefix ?? "<none>"));
            diagnostics.Add("actualPrefixes=" + (actualPrefixes.Count == 0 ? "<none>" : string.Join(",", actualPrefixes)));
            diagnostics.Add("tableFallback=false");
        }
    }

    private static IReadOnlyList<string> ReadHexTableVersionPrefixes(string hexTablePath)
    {
        try
        {
            var document = System.Xml.Linq.XDocument.Load(hexTablePath);
            return document
                .Descendants("TableName")
                .Select(element => Extract6xVersionHint(element.Value) ?? string.Empty)
                .Where(value => value.StartsWith("6.", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> BuildCurrentProjectRoots(string workspace, string gameRoot)
    {
        return new[] { gameRoot, workspace }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> BuildProjectResourceRoots(CczProject project) =>
        BuildCurrentGameRoots(project.GameRoot);

    private static IReadOnlyList<string> BuildCurrentGameRoots(string gameRoot)
    {
        return new[] { gameRoot }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? FindHexTableInProjectRoots(IReadOnlyList<string> roots, string? versionHint)
    {
        foreach (var candidate in roots.SelectMany(root => BuildHexTableCandidates(root, versionHint)))
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static IReadOnlyList<string> BuildHexTableCandidates(string workspace, string? versionHint = null)
    {
        workspace = NormalizeDirectory(workspace);
        var candidates = new List<string>();
        candidates.AddRange(BuildVersionedHexTableCandidates(workspace, versionHint));
        candidates.AddRange(new[]
        {
            Path.Combine(workspace, "老版游戏制作工具", "CczRSX 6.6", "ConfigTable", "HexTable.xml"),
            Path.Combine(workspace, "老版游戏制作工具", "CczRSX 6.5", "ConfigTable", "HexTable.xml"),
            Path.Combine(workspace, "老版游戏制作工具", "CczRSX 6.4", "ConfigTable", "HexTable.xml"),
            Path.Combine(workspace, "CczRSX 6.6", "ConfigTable", "HexTable.xml"),
            Path.Combine(workspace, "CczRSX 6.5", "ConfigTable", "HexTable.xml"),
            Path.Combine(workspace, "CczRSX 6.4", "ConfigTable", "HexTable.xml"),
            Path.Combine(workspace, "ConfigTable", "HexTable.xml"),
            Path.Combine(workspace, "HexTable.xml")
        });
        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ImageAssignerCandidates BuildImageAssignerCandidates(string workspace, string gameRoot)
        => BuildImageAssignerCandidates(Build6xVersionHint(workspace, gameRoot) ?? "6.5");

    private static ImageAssignerCandidates BuildImageAssignerCandidates(string versionHint)
    {
        var is66 = versionHint.StartsWith("6.6", StringComparison.OrdinalIgnoreCase);
        var primaryNames = is66
            ? new[] { "6.6x形象指定器", "形象指定器6.6", "形象指定器66x" }
            : new[] { "形象指定器6.5", "6.5形象指定器", "形象指定器65" };
        var fallbackNames = is66
            ? new[] { "形象指定器6.5", "6.5形象指定器", "形象指定器65" }
            : new[] { "6.6x形象指定器", "形象指定器6.6", "形象指定器66x" };
        var names = primaryNames.Concat(fallbackNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var relatives = names
            .SelectMany(name => new[]
            {
                Path.Combine("老版游戏制作工具", "B形象指定器", name),
                Path.Combine("B形象指定器", name),
                name
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var systemIniRelatives = relatives
            .Select(relative => Path.Combine(relative, "System.ini"))
            .ToList();

        return new ImageAssignerCandidates(names[0], relatives, systemIniRelatives);
    }

    private static IEnumerable<string> BuildVersionedHexTableCandidates(string workspace, string? versionHint)
    {
        versionHint ??= Extract6xVersionHint(workspace);
        var roots = new[]
            {
                Path.Combine(workspace, "老版游戏制作工具"),
                workspace
            }
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var directories = new List<(string Path, string Version)>();
        foreach (var root in roots)
        {
            try
            {
                directories.AddRange(Directory.EnumerateDirectories(root, "CczRSX 6.*", SearchOption.TopDirectoryOnly)
                    .Select(path => (Path: path, Version: Extract6xVersionHint(Path.GetFileName(path)) ?? string.Empty)));
            }
            catch
            {
                // 搜索目录可能不存在或无权限；继续使用固定候选路径。
            }
        }

        return directories
            .Where(item => item.Version.StartsWith("6.", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => !string.IsNullOrWhiteSpace(versionHint) &&
                                       item.Version.Equals(versionHint, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(item => Parse6xVersionRank(item.Version))
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(item => Path.Combine(item.Path, "ConfigTable", "HexTable.xml"));
    }

    private static string? Build6xVersionHint(string workspace, string gameRoot)
        => Infer6xVersionHintFromGameRoot(gameRoot) ??
           Extract6xVersionHint(gameRoot) ??
           Extract6xVersionHint(workspace);

    private static string? Extract6xVersionHint(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        for (var i = 0; i < value.Length - 2; i++)
        {
            if (value[i] != '6' || value[i + 1] != '.' || !char.IsLetterOrDigit(value[i + 2])) continue;
            var end = i + 3;
            while (end < value.Length && char.IsLetterOrDigit(value[end])) end++;
            return value[i..end];
        }

        return null;
    }

    private static int Parse6xVersionRank(string version)
    {
        if (!version.StartsWith("6.", StringComparison.OrdinalIgnoreCase)) return 0;
        var digits = new string(version[2..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var rank) ? rank : 0;
    }

    private static string? Infer6xVersionHintFromGameRoot(string gameRoot)
    {
        try
        {
            var ekd5 = Path.Combine(gameRoot, "Ekd5.exe");
            if (!File.Exists(ekd5)) return null;
            var size = new FileInfo(ekd5).Length;
            return size switch
            {
                1_130_496 => "6.6",
                1_196_032 => "6.5",
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private sealed record ImageAssignerCandidates(
        string DirectoryName,
        IReadOnlyList<string> RelativeCandidates,
        IReadOnlyList<string> SystemIniRelativeCandidates);

    private static string? FindBuiltInHexTableBackup()
    {
        return File.Exists(PortableInstallPaths.HexTablePath)
            ? PortableInstallPaths.HexTablePath
            : null;
    }

    private static string? FindBuiltInLegacyFile(params string[] relativeCandidates)
    {
        foreach (var root in BuildBuiltInLegacyResourceRoots())
        {
            foreach (var relative in relativeCandidates)
            {
                var candidate = BuildPortableCandidate(root, StripLegacyToolPrefix(relative));
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static string? FindBuiltInLegacyDirectory(string directoryName, IReadOnlyList<string> relativeCandidates)
    {
        foreach (var root in BuildBuiltInLegacyResourceRoots())
        {
            foreach (var relative in relativeCandidates)
            {
                var candidate = BuildPortableCandidate(root, StripLegacyToolPrefix(relative));
                if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            }

            var directMatches = Directory.Exists(root)
                ? EnumerateDirectoriesLimited(root, MaxRecursiveSearchDepth)
                    .Where(path => IsDirectoryName(path, directoryName))
                    .OrderByDescending(path => ScorePortablePath(path, relativeCandidates))
                    .ThenBy(path => path.Length)
                    .ToList()
                : new List<string>();
            var found = directMatches.FirstOrDefault();
            if (found != null) return Path.GetFullPath(found);
        }

        return null;
    }

    private static IReadOnlyList<string> BuildBuiltInLegacyResourceRoots()
    {
        return new[] { PortableInstallPaths.LegacyResourcesRoot }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    private static string StripLegacyToolPrefix(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return relativePath;
        if (Path.IsPathRooted(relativePath)) return relativePath;

        var prefixes = new[]
        {
            "老版游戏制作工具"
        };

        foreach (var prefix in prefixes)
        {
            if (relativePath.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (relativePath.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(prefix + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return relativePath[(prefix.Length + 1)..];
            }
        }

        return relativePath;
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

    private static IReadOnlyList<string> BuildDefaultProjectSearchRoots(string? start)
    {
        var root = NormalizeDirectory(start);
        return string.IsNullOrWhiteSpace(root)
            ? Array.Empty<string>()
            : new[] { root };
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
