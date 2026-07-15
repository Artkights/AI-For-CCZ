using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class BackupManager
{
    public string CreateTestCopy(CczProject project, IProgress<string>? progress = null)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var testRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", $"{stamp}_{project.Name}");
        Directory.CreateDirectory(testRoot);

        foreach (var filePath in Directory.GetFiles(project.GameRoot))
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.Equals("_CCZModStudio_TestCopy.txt", StringComparison.OrdinalIgnoreCase)) continue;
            progress?.Report($"复制 {fileName}");
            File.Copy(filePath, Path.Combine(testRoot, fileName), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(project.GameRoot))
        {
            var dirName = Path.GetFileName(directory);
            if (dirName.Equals(ProjectBackupPathService.LegacyBackupDirectoryName, StringComparison.OrdinalIgnoreCase)) continue;
            progress?.Report($"复制 {dirName} 目录");
            CopyDirectory(directory, Path.Combine(testRoot, dirName));
        }

        File.WriteAllText(Path.Combine(testRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\n", EncodingService.Gbk);

        return testRoot;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(directory, Path.Combine(targetDir, Path.GetFileName(directory)));
        }
    }
}
