using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Globalization;

internal partial class Program
{
    private static void RunBackupPathTransactionSmoke(CczProject sourceProject)
    {
        var smokeRoot = Path.Combine(
            sourceProject.WorkspaceRoot,
            "CCZModStudio_TestCopies",
            "BackupPathTransactionSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" })
        {
            var sourcePath = sourceProject.ResolveGameFile(coreFile);
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("事务备份烟测缺少核心文件。", sourcePath);
            File.Copy(sourcePath, Path.Combine(smokeRoot, coreFile), overwrite: false);
        }

        File.WriteAllText(
            Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={sourceProject.GameRoot}\r\nPurpose=backup path transaction smoke\r\n");
        var project = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);

        var targetA = project.ResolveGameFile("BackupTransactionA.bin");
        var targetB = project.ResolveGameFile("BackupTransactionB.bin");
        byte[] originalA = [0x10, 0x20, 0x30, 0x40];
        byte[] originalB = [0x50, 0x60, 0x70, 0x80];
        byte[] updatedA = [0x11, 0x21, 0x31, 0x41];
        byte[] updatedB = [0x51, 0x61, 0x71, 0x81];
        File.WriteAllBytes(targetA, originalA);
        File.WriteAllBytes(targetB, originalB);

        var package = new EffectPackage
        {
            PackageId = "backup-path-transaction-smoke",
            Domain = "native",
            EffectId = 0xFE,
            Name = "事务备份路径烟测",
            PatchSegments =
            {
                new EffectPatchSegment
                {
                    TargetFile = Path.GetFileName(targetA),
                    AddressKind = "WholeFile",
                    BytesHex = EffectPatchByteService.ToHex(updatedA),
                    ExpectedOldBytesHex = EffectPatchByteService.ToHex(originalA),
                    HookPoint = "backup-path-smoke-a"
                },
                new EffectPatchSegment
                {
                    TargetFile = Path.GetFileName(targetB),
                    AddressKind = "WholeFile",
                    BytesHex = EffectPatchByteService.ToHex(updatedB),
                    ExpectedOldBytesHex = EffectPatchByteService.ToHex(originalB),
                    HookPoint = "backup-path-smoke-b"
                }
            }
        };

        const string manifestKind = "backup-path-transaction-smoke";
        var service = new EffectTransactionalPatchService();
        var preview = service.Preview(project, package);
        if (!preview.CanApply || preview.Segments.Count != 2)
        {
            throw new InvalidOperationException("事务备份路径预览失败：" + preview.Summary);
        }

        new LockedEffectWriteReceiptService().Issue(project, package, manifestKind);
        var result = service.Apply(project, package, manifestKind);
        var expectedTransactionRoot = Path.Combine(ProjectBackupPathService.GetBackupRoot(project), "EffectTransactions") +
                                      Path.DirectorySeparatorChar;
        if (!result.Applied || result.BackupPaths.Count != 2 ||
            result.BackupPaths.Any(path => !File.Exists(path) ||
                                           !Path.GetFullPath(path).StartsWith(expectedTransactionRoot, StringComparison.OrdinalIgnoreCase)) ||
            !File.ReadAllBytes(targetA).SequenceEqual(updatedA) ||
            !File.ReadAllBytes(targetB).SequenceEqual(updatedB))
        {
            throw new InvalidOperationException("双文件特效事务没有生成新目录下的完整备份，或写后复读失败。");
        }

        service.Restore(project, result);
        if (!File.ReadAllBytes(targetA).SequenceEqual(originalA) ||
            !File.ReadAllBytes(targetB).SequenceEqual(originalB))
        {
            throw new InvalidOperationException("双文件特效事务恢复失败。");
        }

        Console.WriteLine(
            $"BACKUP_PATH_TRANSACTION_SMOKE_OK root={ProjectBackupPathService.GetBackupRoot(project)} backups={result.BackupPaths.Count}");
    }
}
