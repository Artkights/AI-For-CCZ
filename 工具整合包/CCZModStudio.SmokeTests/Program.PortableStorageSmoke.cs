using System.Drawing;
using System.Text;
using System.Text.Json;
using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

internal partial class Program
{
    private static void RunPortableStorageSmoke()
    {
        AssertPortableStorageProductionPaths();
        AssertPortableLauncherRootResolution();

        using var temp = new TemporarySmokeDirectory("PortableStorage");
        AssertPortableErrorLogging(temp.Path);
        AssertPortableImageCache(temp.Path);
        AssertPortableMaterialCache(temp.Path);
        AssertProjectBackupPaths(temp.Path);
        AssertPortableStorageWriteFailures(temp.Path);

        Console.WriteLine("PORTABLE_STORAGE_SMOKE_OK root=" + temp.Path);
    }

    private static void AssertPortableStorageProductionPaths()
    {
        AssertPathEqual(
            Path.Combine(PortableInstallPaths.LauncherRoot, "log"),
            PortableInstallPaths.LogRoot,
            "portable log root");
        AssertPathEqual(
            Path.Combine(PortableInstallPaths.LauncherRoot, "cache"),
            PortableInstallPaths.CacheRoot,
            "portable cache root");
        AssertPathEqual(
            Path.Combine(PortableInstallPaths.LauncherRoot, "config"),
            PortableInstallPaths.ConfigRoot,
            "portable config root");
        AssertPathEqual(
            Path.Combine(PortableInstallPaths.ConfigRoot, "backup-settings.json"),
            PortableInstallPaths.BackupSettingsPath,
            "portable backup settings path");
        AssertPathEqual(PortableInstallPaths.LogRoot, ApplicationErrorService.LogDirectory, "application error log root");
        AssertPathEqual(
            Path.Combine(PortableInstallPaths.CacheRoot, "ImagePreview", "v1"),
            ImagePreviewCache.Shared.CacheDirectory,
            "image preview cache root");

        using var materialCache = new MaterialLibraryCache(new MaterialLibraryIndexer());
        AssertPathEqual(
            Path.Combine(PortableInstallPaths.CacheRoot, "MaterialLibrary", "v1"),
            materialCache.CacheDirectory,
            "material library cache root");
    }

    private static void AssertPortableLauncherRootResolution()
    {
        using var temp = new TemporarySmokeDirectory("PortableLauncherRoot");
        var publishRoot = Path.Combine(temp.Path, "Published");
        var netRoot = Path.Combine(publishRoot, "net");
        Directory.CreateDirectory(netRoot);
        File.WriteAllBytes(Path.Combine(publishRoot, "普罗工具整合包.exe"), []);

        var published = PortableInstallPaths.ResolveLauncherRoot(
            netRoot,
            Path.Combine(netRoot, "CCZModStudio.exe"));
        AssertPathEqual(publishRoot, published, "published net layout resolves launcher root");
        AssertPathEqual(Path.Combine(publishRoot, "log"), Path.Combine(published, "log"), "published log is outside net");
        AssertPathEqual(Path.Combine(publishRoot, "cache"), Path.Combine(published, "cache"), "published cache is outside net");

        var developmentRoot = Path.Combine(temp.Path, "Development");
        Directory.CreateDirectory(developmentRoot);
        var development = PortableInstallPaths.ResolveLauncherRoot(
            developmentRoot,
            Path.Combine(developmentRoot, "CCZModStudio.SmokeTests.exe"));
        AssertPathEqual(developmentRoot, development, "development layout keeps runtime root");
    }

    private static void AssertPortableErrorLogging(string tempRoot)
    {
        var logRoot = Path.Combine(tempRoot, "log");
        var marker = "portable-log-" + Guid.NewGuid().ToString("N");
        ApplicationErrorService.LogDirectoryOverrideForTests = logRoot;
        try
        {
            var report = ApplicationErrorService.Report(
                new InvalidOperationException(marker),
                "Portable storage smoke",
                notify: false);
            AssertPortableTrue(!string.IsNullOrWhiteSpace(report.LogPath), "portable error report returns a log path");
            AssertPathEqual(logRoot, Path.GetDirectoryName(report.LogPath)!, "portable error report directory");
            AssertPortableTrue(File.Exists(report.LogPath), "portable text log exists");
            var textLog = File.ReadAllText(report.LogPath, Encoding.UTF8);
            AssertPortableTrue(textLog.Contains(marker, StringComparison.Ordinal), "portable text log contains marker");
            foreach (var field in new[]
                     {
                         "Version:",
                         "InformationalVersion:",
                         "FileVersion:",
                         "Framework:",
                         "OperatingSystem:",
                         "ProcessArchitecture:",
                         "HighDpiMode:",
                         "DeviceDpi:"
                     })
            {
                AssertPortableTrue(textLog.Contains(field, StringComparison.Ordinal), "portable text log contains " + field);
                AssertPortableTrue(report.Details.Contains(field, StringComparison.Ordinal), "copyable error details contain " + field);
            }

            var jsonlPath = Path.Combine(logRoot, "exceptions.jsonl");
            AssertPortableTrue(File.Exists(jsonlPath), "portable exceptions jsonl exists");
            var jsonLine = File.ReadLines(jsonlPath, Encoding.UTF8)
                .Last(line => line.Contains(marker, StringComparison.Ordinal));
            using var document = JsonDocument.Parse(jsonLine);
            AssertPathEqual(
                report.LogPath,
                document.RootElement.GetProperty("logPath").GetString() ?? string.Empty,
                "jsonl logPath uses portable log root");
            foreach (var property in new[]
                     {
                         "version",
                         "informationalVersion",
                         "fileVersion",
                         "framework",
                         "operatingSystem",
                         "processArchitecture",
                         "highDpiMode",
                         "deviceDpi"
                     })
            {
                AssertPortableTrue(document.RootElement.TryGetProperty(property, out _), "jsonl contains diagnostic property " + property);
            }
        }
        finally
        {
            ApplicationErrorService.LogDirectoryOverrideForTests = null;
        }
    }

    private static void AssertPortableImageCache(string tempRoot)
    {
        var cacheRoot = Path.Combine(tempRoot, "cache", "ImagePreview", "v1");
        var cache = new ImagePreviewCache(cacheRoot);
        var key = "portable-image-cache-" + Guid.NewGuid().ToString("N");
        var expected = Encoding.UTF8.GetBytes(key);

        var generated = cache.GetOrCreateAsync(key, () => Task.FromResult<byte[]?>(expected))
            .GetAwaiter().GetResult();
        AssertPortableTrue(generated?.Source == ImagePreviewCacheSource.Generated, "image cache cold request is generated");
        WaitForPortableStorage(
            () => Directory.Exists(cacheRoot) &&
                  Directory.GetFiles(cacheRoot, "*.png").Length == 1 &&
                  Directory.GetFiles(cacheRoot, "*.key").Length == 1,
            "image cache disk files");

        cache.ClearMemory();
        var factoryCalled = false;
        var disk = cache.GetOrCreateAsync(key, () =>
        {
            factoryCalled = true;
            return Task.FromResult<byte[]?>(null);
        }).GetAwaiter().GetResult();
        AssertPortableTrue(disk?.Source == ImagePreviewCacheSource.Disk, "image cache warm request is a disk hit");
        AssertPortableTrue(!factoryCalled, "image cache disk hit skips generator");
        AssertPortableTrue(disk!.Bytes.SequenceEqual(expected), "image cache disk bytes round trip");
    }

    private static void AssertPortableMaterialCache(string tempRoot)
    {
        var materialRoot = Path.Combine(tempRoot, "MaterialSource");
        var categoryRoot = Path.Combine(materialRoot, "0：平原");
        Directory.CreateDirectory(categoryRoot);
        using (var bitmap = new Bitmap(8, 8))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.ForestGreen);
            bitmap.Save(Path.Combine(categoryRoot, "1.png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        var cacheRoot = Path.Combine(tempRoot, "cache", "MaterialLibrary", "v1");
        var indexer = new MaterialLibraryIndexer();
        using (var coldCache = new MaterialLibraryCache(indexer, cacheRoot))
        {
            var cold = coldCache.GetOrIndexExplicitRoot(materialRoot);
            AssertPortableTrue(cold.Count == 1, "material cache cold index returns fixture");
        }
        AssertPortableTrue(Directory.GetFiles(cacheRoot, "*.json").Length == 1, "material cache manifest exists");

        PerformanceMetrics.Reset();
        using (var warmCache = new MaterialLibraryCache(indexer, cacheRoot))
        {
            var warm = warmCache.GetOrIndexExplicitRoot(materialRoot);
            AssertPortableTrue(warm.Count == 1, "material cache warm index returns fixture");
        }
        var counters = PerformanceMetrics.GetSnapshot().Counters;
        AssertPortableTrue(counters.GetValueOrDefault("MaterialLibrary.DiskHits") == 1, "material cache warm request is a disk hit");
    }

    private static void AssertProjectBackupPaths(string tempRoot)
    {
        var oldConfigOverride = ProjectBackupPathService.ConfigPathOverrideForTests;
        var oldParentOverride = ProjectBackupPathService.DefaultParentOverrideForTests;
        var launcherRoot = Path.Combine(tempRoot, "普罗工具整合包");
        var configPath = Path.Combine(launcherRoot, "config", "backup-settings.json");
        var projectRootA = Path.Combine(tempRoot, "项目 A（测试）");
        var projectRootB = Path.Combine(tempRoot, "项目 B");
        Directory.CreateDirectory(launcherRoot);
        Directory.CreateDirectory(projectRootA);
        Directory.CreateDirectory(projectRootB);
        var projectA = BuildBackupSmokeProject(tempRoot, projectRootA);
        var projectB = BuildBackupSmokeProject(tempRoot, projectRootB);

        ProjectBackupPathService.ConfigPathOverrideForTests = configPath;
        ProjectBackupPathService.DefaultParentOverrideForTests = launcherRoot;
        try
        {
            AssertPathEqual(
                Path.Combine(launcherRoot, projectA.Name + "_备份"),
                ProjectBackupPathService.GetBackupRoot(projectA),
                "default project backup root");
            AssertPortableTrue(!ProjectBackupPathService.IsCustomBackupParent(projectA), "default backup path is not custom");

            var customParentA = Path.Combine(tempRoot, "作者备份", "甲");
            var customRootA = ProjectBackupPathService.SetBackupParent(projectA, customParentA);
            AssertPathEqual(Path.Combine(customParentA, projectA.Name + "_备份"), customRootA, "custom parent appends project folder");
            AssertPortableTrue(File.Exists(configPath), "backup settings file persists");
            AssertPortableTrue(ProjectBackupPathService.IsCustomBackupParent(projectA), "project A custom mapping persists");
            AssertPathEqual(customRootA, ProjectBackupPathService.GetBackupRoot(projectA), "project A custom mapping reloads");
            AssertPathEqual(
                Path.Combine(launcherRoot, projectB.Name + "_备份"),
                ProjectBackupPathService.GetBackupRoot(projectB),
                "project B remains on independent default");

            var customParentB = Path.Combine(tempRoot, "作者备份", "乙");
            ProjectBackupPathService.SetBackupParent(projectB, customParentB);
            AssertPathEqual(customRootA, ProjectBackupPathService.GetBackupRoot(projectA), "project B selection does not change project A");
            AssertPathEqual(
                Path.Combine(customParentB, projectB.Name + "_备份"),
                ProjectBackupPathService.GetBackupRoot(projectB),
                "project B custom mapping persists");

            ProjectBackupPathService.SetBackupParent(projectA, launcherRoot);
            AssertPortableTrue(!ProjectBackupPathService.IsCustomBackupParent(projectA), "selecting launcher root clears custom mapping");
            AssertPathEqual(
                Path.Combine(launcherRoot, projectA.Name + "_备份"),
                ProjectBackupPathService.GetBackupRoot(projectA),
                "cleared mapping restores default");

            AssertPortableThrows<InvalidOperationException>(
                () => ProjectBackupPathService.SetBackupParent(projectA, Path.Combine(projectA.GameRoot, "备份")),
                "project-contained backup parent is rejected");

            var legacyRoot = ProjectBackupPathService.GetLegacyBackupRoot(projectA);
            Directory.CreateDirectory(legacyRoot);
            var legacyMarker = Path.Combine(legacyRoot, "legacy-marker.bak");
            File.WriteAllText(legacyMarker, "legacy", Encoding.UTF8);
            var readableRoots = ProjectBackupPathService.GetReadableBackupRoots(projectA);
            AssertPathEqual(ProjectBackupPathService.GetBackupRoot(projectA), readableRoots[0], "new backup root has read priority");
            AssertPortableTrue(readableRoots.Contains(legacyRoot, StringComparer.OrdinalIgnoreCase), "legacy backup root remains readable");
            AssertPortableTrue(File.Exists(legacyMarker), "legacy backup remains untouched");

            var successfulTarget = Path.Combine(projectRootB, "successful-resource.dat");
            var successfulReplacement = Path.Combine(tempRoot, "successful-replacement.dat");
            File.WriteAllText(successfulTarget, "before-success", Encoding.UTF8);
            File.WriteAllText(successfulReplacement, "after-success", Encoding.UTF8);
            var successfulReplace = new ResourceReplaceService().Replace(projectB, successfulTarget, successfulReplacement);
            var projectBBackupRoot = ProjectBackupPathService.GetBackupRoot(projectB) + Path.DirectorySeparatorChar;
            AssertPortableTrue(File.ReadAllText(successfulTarget, Encoding.UTF8) == "after-success", "resource replacement writes target");
            AssertPortableTrue(File.Exists(successfulReplace.BackupPath), "resource replacement creates backup");
            AssertPortableTrue(File.Exists(successfulReplace.ReportPath), "resource replacement creates text report");
            AssertPortableTrue(File.Exists(successfulReplace.ReportJsonPath), "resource replacement creates structured report");
            AssertPortableTrue(
                Path.GetFullPath(successfulReplace.BackupPath).StartsWith(projectBBackupRoot, StringComparison.OrdinalIgnoreCase),
                "resource replacement backup uses configured project root");

            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, "{ damaged json", Encoding.UTF8);
            AssertPortableThrows<InvalidOperationException>(
                () => ProjectBackupPathService.GetBackupRoot(projectA),
                "damaged backup settings block normal resolution");
            AssertPortableTrue(
                ProjectBackupPathService.GetReadableBackupRoots(projectA).Contains(legacyRoot, StringComparer.OrdinalIgnoreCase),
                "damaged settings do not hide legacy recovery root");

            var repairedParent = Path.Combine(tempRoot, "修复后的备份上级目录");
            ProjectBackupPathService.SetBackupParent(projectA, repairedParent);
            AssertPathEqual(
                Path.Combine(repairedParent, projectA.Name + "_备份"),
                ProjectBackupPathService.GetBackupRoot(projectA),
                "explicit selection repairs damaged settings");

            var blocker = Path.Combine(tempRoot, "not-a-backup-directory");
            File.WriteAllText(blocker, "block directory creation", Encoding.UTF8);
            var target = Path.Combine(projectRootB, "resource.dat");
            var replacement = Path.Combine(tempRoot, "replacement.dat");
            File.WriteAllText(target, "before", Encoding.UTF8);
            File.WriteAllText(replacement, "after", Encoding.UTF8);
            var invalidSettings = new
            {
                version = 1,
                projects = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    [Path.GetFullPath(projectRootB)] = new { backupParent = blocker }
                }
            };
            File.WriteAllText(configPath, JsonSerializer.Serialize(invalidSettings), Encoding.UTF8);
            AssertPortableThrows<InvalidOperationException>(
                () => new ResourceReplaceService().Replace(projectB, target, replacement),
                "unwritable configured backup blocks resource replacement");
            AssertPortableTrue(File.ReadAllText(target, Encoding.UTF8) == "before", "backup failure leaves target bytes unchanged");
        }
        finally
        {
            ProjectBackupPathService.ConfigPathOverrideForTests = oldConfigOverride;
            ProjectBackupPathService.DefaultParentOverrideForTests = oldParentOverride;
        }
    }

    private static CczProject BuildBackupSmokeProject(string workspaceRoot, string gameRoot)
        => new()
        {
            WorkspaceRoot = workspaceRoot,
            GameRoot = gameRoot,
            HexTableXmlPath = Path.Combine(workspaceRoot, "ConfigTable", "HexTable.xml")
        };

    private static void AssertPortableThrows<TException>(Action action, string description) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException("Expected " + typeof(TException).Name + ": " + description);
    }

    private static void AssertPortableStorageWriteFailures(string tempRoot)
    {
        var blocker = Path.Combine(tempRoot, "not-a-directory");
        File.WriteAllText(blocker, "block directory creation", Encoding.UTF8);

        ApplicationErrorService.LogDirectoryOverrideForTests = blocker;
        try
        {
            var report = ApplicationErrorService.Report(
                new InvalidOperationException("portable-log-write-failure"),
                "Portable storage unwritable log smoke",
                notify: false);
            AssertPortableTrue(string.IsNullOrWhiteSpace(report.LogPath), "unwritable log does not throw and returns no path");
        }
        finally
        {
            ApplicationErrorService.LogDirectoryOverrideForTests = null;
        }

        var imageCache = new ImagePreviewCache(blocker);
        var generated = imageCache.GetOrCreateAsync(
                "unwritable-image-cache",
                () => Task.FromResult<byte[]?>([1, 2, 3]))
            .GetAwaiter().GetResult();
        AssertPortableTrue(generated?.Source == ImagePreviewCacheSource.Generated, "unwritable image cache falls back to generation");

        var materialRoot = Path.Combine(tempRoot, "UnwritableMaterialSource");
        var categoryRoot = Path.Combine(materialRoot, "景物");
        Directory.CreateDirectory(categoryRoot);
        using (var bitmap = new Bitmap(4, 4))
        {
            bitmap.Save(Path.Combine(categoryRoot, "1.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        using var materialCache = new MaterialLibraryCache(new MaterialLibraryIndexer(), blocker);
        var materials = materialCache.GetOrIndexExplicitRoot(materialRoot);
        AssertPortableTrue(materials.Count == 1, "unwritable material cache falls back to live indexing");
        AssertPortableTrue(File.Exists(blocker), "cache failure does not replace blocker file");
    }

    private static void WaitForPortableStorage(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            Thread.Sleep(25);
        }

        throw new InvalidOperationException("Timed out waiting for " + description + ".");
    }

    private static void AssertPathEqual(string expected, string actual, string description)
    {
        var expectedFullPath = Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var actualFullPath = Path.GetFullPath(actual).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!expectedFullPath.Equals(actualFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{description}: expected={expectedFullPath}, actual={actualFullPath}");
        }
    }

    private static void AssertPortableTrue(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed: " + description);
        }
    }
}
