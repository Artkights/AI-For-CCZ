using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class TestCopyDiffService
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

    public IReadOnlyList<ProjectDiffItem> Analyze(CczProject testProject)
    {
        if (!testProject.IsTestCopy)
        {
            throw new InvalidOperationException("当前项目不是 CCZModStudio 测试副本，无法生成测试副本差异。");
        }

        var sourceRoot = ReadSourceRoot(testProject);
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            throw new InvalidOperationException("测试副本标记中没有有效 Source 路径，无法与原始项目比较。");
        }

        sourceRoot = Path.GetFullPath(sourceRoot);
        var testRoot = Path.GetFullPath(testProject.GameRoot);

        var sourceFiles = EnumerateComparableFiles(sourceRoot)
            .ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
        var testFiles = EnumerateComparableFiles(testRoot)
            .ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);

        var allRelativePaths = sourceFiles.Keys
            .Concat(testFiles.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase);

        var result = new List<ProjectDiffItem>();
        foreach (var relativePath in allRelativePaths)
        {
            var hasSource = sourceFiles.TryGetValue(relativePath, out var source);
            var hasTest = testFiles.TryGetValue(relativePath, out var test);

            if (!hasSource && hasTest)
            {
                result.Add(new ProjectDiffItem
                {
                    Status = "新增",
                    RelativePath = relativePath,
                    TestSize = test!.Size,
                    TestSha256 = ComputeSha256(test.FullPath),
                    Detail = "测试副本中存在，原始项目中不存在。",
                    TestPath = test.FullPath
                });
                continue;
            }

            if (hasSource && !hasTest)
            {
                result.Add(new ProjectDiffItem
                {
                    Status = "缺失",
                    RelativePath = relativePath,
                    SourceSize = source!.Size,
                    SourceSha256 = ComputeSha256(source.FullPath),
                    Detail = "原始项目中存在，测试副本中不存在。",
                    SourcePath = source.FullPath
                });
                continue;
            }

            var sourceSha = ComputeSha256(source!.FullPath);
            var testSha = ComputeSha256(test!.FullPath);
            var same = source.Size == test.Size && sourceSha.Equals(testSha, StringComparison.OrdinalIgnoreCase);
            if (same) continue;

            result.Add(new ProjectDiffItem
            {
                Status = "已修改",
                RelativePath = relativePath,
                SourceSize = source.Size,
                TestSize = test.Size,
                SourceSha256 = sourceSha,
                TestSha256 = testSha,
                Detail = source.Size == test.Size ? "尺寸相同但 SHA256 不同。" : "尺寸和 SHA256 不同。",
                SourcePath = source.FullPath,
                TestPath = test.FullPath
            });
        }

        return result;
    }

    public string WriteReport(CczProject testProject, IReadOnlyList<ProjectDiffItem> items)
    {
        if (!testProject.IsTestCopy)
        {
            throw new InvalidOperationException("当前项目不是 CCZModStudio 测试副本，不能导出差异报告。");
        }

        var reportRoot = Path.Combine(testProject.GameRoot, "_CCZModStudio_Reports");
        Directory.CreateDirectory(reportRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(reportRoot, $"{stamp}_TestCopyDiff.txt");
        var sourceRoot = ReadSourceRoot(testProject);

        var lines = new List<string>
        {
            "CCZModStudio Test Copy Diff",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "SourceRoot=" + sourceRoot,
            "TestRoot=" + testProject.GameRoot,
            "ChangedItems=" + items.Count,
            string.Empty,
            "Status\tRelativePath\tSourceSize\tTestSize\tSourceSha256\tTestSha256\tDetail\tSourcePath\tTestPath"
        };

        lines.AddRange(items.Select(x => string.Join('\t',
            Sanitize(x.Status),
            Sanitize(x.RelativePath),
            x.SourceSize?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            x.TestSize?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Sanitize(x.SourceSha256),
            Sanitize(x.TestSha256),
            Sanitize(x.Detail),
            Sanitize(x.SourcePath),
            Sanitize(x.TestPath))));

        File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        return reportPath;
    }

    public string ReadSourceRoot(CczProject testProject)
    {
        var marker = Path.Combine(testProject.GameRoot, "_CCZModStudio_TestCopy.txt");
        if (!File.Exists(marker)) return string.Empty;

        foreach (var line in ReadAllLinesSmart(marker))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Source=", StringComparison.OrdinalIgnoreCase)) continue;
            return trimmed["Source=".Length..].Trim();
        }

        return string.Empty;
    }

    private static string[] ReadAllLinesSmart(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
        }
        catch (DecoderFallbackException)
        {
            return EncodingService.Gbk
                .GetString(bytes)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
        }
    }

    private static IReadOnlyList<ComparableFile> EnumerateComparableFiles(string root)
    {
        var result = new List<ComparableFile>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (ShouldIgnore(path, root)) continue;
            var relative = Path.GetRelativePath(root, path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var info = new FileInfo(path);
            result.Add(new ComparableFile(relative, path, info.Length));
        }
        return result;
    }

    private static bool ShouldIgnore(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        var parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => IgnoredDirectoryNames.Contains(p, StringComparer.OrdinalIgnoreCase))) return true;
        return parts.Length > 0 && IgnoredFileNames.Contains(parts[^1], StringComparer.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string Sanitize(string value) =>
        value.Replace('\t', ' ').Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private sealed record ComparableFile(string RelativePath, string FullPath, long Size);
}
