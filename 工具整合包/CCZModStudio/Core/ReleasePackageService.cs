using System.Globalization;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ReleasePackageService
{
    private static readonly string[] IgnoredDirectoryNames =
    {
        "_CCZModStudio_Backups",
        "_CCZModStudio_Reports",
        "_CCZModStudio_Exports"
    };

    private static readonly string[] IgnoredFileNames =
    {
        "_CCZModStudio_TestCopy.txt"
    };

    private readonly TestCopyDiffService _diffService = new();

    public ReleasePackageResult CreateReleaseCopy(CczProject testProject, IReadOnlyList<ProjectDiffItem>? diffItems = null, IProgress<string>? progress = null)
    {
        if (!testProject.IsTestCopy)
        {
            throw new InvalidOperationException("生成发布副本必须从 CCZModStudio 测试副本执行，禁止直接把原始目录当作发布输入。");
        }

        diffItems ??= _diffService.Analyze(testProject);
        var sourceRoot = _diffService.ReadSourceRoot(testProject);
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            throw new InvalidOperationException("测试副本标记中缺少 Source 路径，无法生成可追溯的发布副本。");
        }

        var releaseRootParent = Path.Combine(testProject.WorkspaceRoot, "CCZModStudio_Releases");
        Directory.CreateDirectory(releaseRootParent);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var releaseRoot = Path.Combine(releaseRootParent, $"{stamp}_{MakeSafeFileName(testProject.Name)}_Release");
        Directory.CreateDirectory(releaseRoot);

        var filesCopied = 0;
        long bytesCopied = 0;
        foreach (var sourcePath in Directory.EnumerateFiles(testProject.GameRoot, "*", SearchOption.AllDirectories))
        {
            if (ShouldIgnore(sourcePath, testProject.GameRoot)) continue;
            var relative = Path.GetRelativePath(testProject.GameRoot, sourcePath);
            var targetPath = Path.Combine(releaseRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            progress?.Report("复制发布文件 " + relative);
            File.Copy(sourcePath, targetPath, overwrite: false);
            filesCopied++;
            bytesCopied += new FileInfo(sourcePath).Length;
        }

        var manifestPath = Path.Combine(releaseRoot, "_CCZModStudio_ReleaseManifest.txt");
        WriteManifest(manifestPath, testProject, sourceRoot, releaseRoot, diffItems, filesCopied, bytesCopied);
        filesCopied++;
        bytesCopied += new FileInfo(manifestPath).Length;

        return new ReleasePackageResult
        {
            ReleaseRoot = releaseRoot,
            ManifestPath = manifestPath,
            FilesCopied = filesCopied,
            BytesCopied = bytesCopied,
            ChangedItems = diffItems.Count,
            ModifiedItems = diffItems.Count(x => x.Status == "已修改"),
            AddedItems = diffItems.Count(x => x.Status == "新增"),
            MissingItems = diffItems.Count(x => x.Status == "缺失")
        };
    }

    private static void WriteManifest(
        string manifestPath,
        CczProject testProject,
        string sourceRoot,
        string releaseRoot,
        IReadOnlyList<ProjectDiffItem> diffItems,
        int filesCopied,
        long bytesCopied)
    {
        var lines = new List<string>
        {
            "CCZModStudio Release Manifest",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "SourceRoot=" + sourceRoot,
            "TestRoot=" + testProject.GameRoot,
            "ReleaseRoot=" + releaseRoot,
            "FilesCopied=" + filesCopied.ToString(CultureInfo.InvariantCulture),
            "BytesCopied=" + bytesCopied.ToString(CultureInfo.InvariantCulture),
            "ChangedItems=" + diffItems.Count.ToString(CultureInfo.InvariantCulture),
            "ModifiedItems=" + diffItems.Count(x => x.Status == "已修改").ToString(CultureInfo.InvariantCulture),
            "AddedItems=" + diffItems.Count(x => x.Status == "新增").ToString(CultureInfo.InvariantCulture),
            "MissingItems=" + diffItems.Count(x => x.Status == "缺失").ToString(CultureInfo.InvariantCulture),
            string.Empty,
            "说明：本目录由测试副本生成，已排除 _CCZModStudio_TestCopy.txt、备份目录、报告目录和导出目录。",
            "建议：正式封包前请再次运行游戏实测，并确认缺失项不是误删的发布必需文件。",
            string.Empty,
            "Status\tRelativePath\tSourceSize\tTestSize\tDetail"
        };

        lines.AddRange(diffItems.Select(x => string.Join('\t',
            Sanitize(x.Status),
            Sanitize(x.RelativePath),
            x.SourceSize?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            x.TestSize?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Sanitize(x.Detail))));

        File.WriteAllLines(manifestPath, lines, Encoding.UTF8);
    }

    private static bool ShouldIgnore(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        var parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => IgnoredDirectoryNames.Contains(p, StringComparer.OrdinalIgnoreCase))) return true;
        return parts.Length > 0 && IgnoredFileNames.Contains(parts[^1], StringComparer.OrdinalIgnoreCase);
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(ch, '_');
        }
        return name;
    }

    private static string Sanitize(string value) =>
        value.Replace('\t', ' ').Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
