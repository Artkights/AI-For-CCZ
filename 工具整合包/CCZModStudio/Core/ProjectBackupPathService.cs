using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public static class ProjectBackupPathService
{
    public const string LegacyBackupDirectoryName = "_CCZModStudio_Backups";
    private const int SettingsVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static string? ConfigPathOverrideForTests { get; set; }
    internal static string? DefaultParentOverrideForTests { get; set; }

    public static string GetBackupParent(CczProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var settings = LoadSettings();
        var projectKey = NormalizePath(project.GameRoot, "项目目录");
        if (settings.Projects.TryGetValue(projectKey, out var entry) &&
            !string.IsNullOrWhiteSpace(entry.BackupParent))
        {
            return ValidateBackupParent(project, entry.BackupParent);
        }

        return ValidateBackupParent(project, GetDefaultParent());
    }

    public static string GetBackupRoot(CczProject project)
    {
        var parent = GetBackupParent(project);
        return Path.Combine(parent, project.Name + "_备份");
    }

    public static bool IsCustomBackupParent(CczProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var settings = LoadSettings();
        var projectKey = NormalizePath(project.GameRoot, "项目目录");
        return settings.Projects.ContainsKey(projectKey);
    }

    public static string SetBackupParent(CczProject project, string backupParent)
    {
        ArgumentNullException.ThrowIfNull(project);
        var normalizedParent = ValidateBackupParent(project, backupParent);
        EnsureDirectoryWritable(Path.Combine(normalizedParent, project.Name + "_备份"));

        var settings = LoadSettingsForUpdate();
        var projectKey = NormalizePath(project.GameRoot, "项目目录");
        var defaultParent = NormalizePath(GetDefaultParent(), "默认备份上级目录");
        if (normalizedParent.Equals(defaultParent, StringComparison.OrdinalIgnoreCase))
        {
            settings.Projects.Remove(projectKey);
        }
        else
        {
            settings.Projects[projectKey] = new ProjectBackupSettingsEntry
            {
                BackupParent = normalizedParent
            };
        }

        SaveSettings(settings);
        return Path.Combine(normalizedParent, project.Name + "_备份");
    }

    public static IReadOnlyList<string> GetReadableBackupRoots(CczProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var roots = new List<string>();
        try
        {
            roots.Add(GetBackupRoot(project));
        }
        catch (InvalidOperationException)
        {
            // A damaged custom-path configuration must not hide legacy recovery files.
        }

        var legacyRoot = GetLegacyBackupRoot(project);
        if (!roots.Contains(legacyRoot, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(legacyRoot);
        }

        return roots;
    }

    public static string GetLegacyBackupRoot(CczProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Path.Combine(project.GameRoot, LegacyBackupDirectoryName);
    }

    public static string EnsureBackupRootWritable(CczProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var backupRoot = GetBackupRoot(project);
        EnsureDirectoryWritable(backupRoot);
        return backupRoot;
    }

    private static string ValidateBackupParent(CczProject project, string backupParent)
    {
        var normalizedParent = NormalizePath(backupParent, "备份上级目录");
        var projectRoot = NormalizePath(project.GameRoot, "项目目录");
        if (IsSameOrChildPath(normalizedParent, projectRoot))
        {
            throw new InvalidOperationException(
                "备份上级目录不能是当前项目目录或其子目录。请选择项目目录之外的位置。");
        }

        return normalizedParent;
    }

    private static bool IsSameOrChildPath(string candidate, string root)
    {
        if (candidate.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureDirectoryWritable(string directory)
    {
        var probePath = string.Empty;
        try
        {
            Directory.CreateDirectory(directory);
            probePath = Path.Combine(directory, $".cczmodstudio-write-test-{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                       probePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 1,
                       FileOptions.WriteThrough))
            {
                stream.WriteByte(0);
                stream.Flush(flushToDisk: true);
            }

            File.Delete(probePath);
            probePath = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"备份目录不可用，已阻止写入项目文件。请点击“备份路径”重新选择。\r\n\r\n目录：{directory}\r\n原因：{ex.Message}",
                ex);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(probePath))
            {
                try { File.Delete(probePath); } catch { }
            }
        }
    }

    private static BackupPathSettings LoadSettings()
    {
        var path = GetConfigPath();
        if (!File.Exists(path)) return new BackupPathSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<BackupPathSettings>(
                               File.ReadAllText(path, Encoding.UTF8),
                               JsonOptions)
                           ?? throw new JsonException("配置内容为空。");
            return NormalizeSettings(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"备份路径配置无法读取，已阻止写入项目文件。请点击“备份路径”重新选择并修复配置。\r\n\r\n配置：{path}\r\n原因：{ex.Message}",
                ex);
        }
    }

    private static BackupPathSettings LoadSettingsForUpdate()
    {
        try
        {
            return LoadSettings();
        }
        catch (InvalidOperationException)
        {
            // An explicit user selection is allowed to replace an unreadable configuration.
            return new BackupPathSettings();
        }
    }

    private static BackupPathSettings NormalizeSettings(BackupPathSettings settings)
    {
        if (settings.Version != SettingsVersion)
        {
            throw new InvalidOperationException($"不支持的备份路径配置版本：{settings.Version}。");
        }

        settings.Version = SettingsVersion;
        var normalized = new Dictionary<string, ProjectBackupSettingsEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.Projects ?? new Dictionary<string, ProjectBackupSettingsEntry>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value?.BackupParent))
            {
                throw new InvalidOperationException("配置中存在缺少项目目录或备份上级目录的条目。");
            }

            normalized[NormalizePath(pair.Key, "项目目录")] = new ProjectBackupSettingsEntry
            {
                BackupParent = NormalizePath(pair.Value.BackupParent, "备份上级目录")
            };
        }

        settings.Projects = normalized;
        return settings;
    }

    private static void SaveSettings(BackupPathSettings settings)
    {
        settings = NormalizeSettings(settings);
        var path = GetConfigPath();
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("备份路径配置文件缺少父目录：" + path);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"无法保存备份路径配置，原设置保持不变。\r\n\r\n配置：{path}\r\n原因：{ex.Message}",
                ex);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static string GetDefaultParent()
        => string.IsNullOrWhiteSpace(DefaultParentOverrideForTests)
            ? PortableInstallPaths.LauncherRoot
            : DefaultParentOverrideForTests;

    private static string GetConfigPath()
        => string.IsNullOrWhiteSpace(ConfigPathOverrideForTests)
            ? PortableInstallPaths.BackupSettingsPath
            : Path.GetFullPath(ConfigPathOverrideForTests);

    private static string NormalizePath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(label + "不能为空。");
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var pathRoot = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(pathRoot) && fullPath.Equals(pathRoot, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException($"{label}无效：{path}", ex);
        }
    }

    private sealed class BackupPathSettings
    {
        public int Version { get; set; } = SettingsVersion;
        public Dictionary<string, ProjectBackupSettingsEntry> Projects { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ProjectBackupSettingsEntry
    {
        public string BackupParent { get; set; } = string.Empty;
    }
}
