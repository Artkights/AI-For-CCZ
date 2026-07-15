using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;

internal partial class Program
{
    static void RunGameTitleVersionedSmoke(string workspaceRoot, string hexTableXmlPath)
    {
        var tables = new HexTableParser().Load(hexTableXmlPath);
        var baseline65 = Path.Combine(workspaceRoot, "基底", "加强版6.5未加密版");
        var baseline66 = Path.Combine(workspaceRoot, "基底", "新改曹操傳6.6修正版");
        AssertGameTitleBaseline(baseline65, tables, "6.5", "star65-title-v1", 0x8D3C4, 32);
        AssertGameTitleBaseline(baseline66, tables, "6.6", "star66-title-v1", 0x8B2D8, 14);
        Assert65TitleRoundTrip(baseline65, workspaceRoot, tables);

        var smokeRoot = Path.Combine(workspaceRoot, "CCZModStudio_TestCopies", "GameTitleVersionedSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        CopyGameTitleFixture(baseline66, smokeRoot);
        var project = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var service = new GlobalSettingsService();

        var document = service.Load(project, tables);
        var exePath = project.ResolveGameFile("Ekd5.exe");
        var before = File.ReadAllBytes(exePath);
        var neighborBefore = before.AsSpan(0x8B2D8 + 14, 16).ToArray();
        document.GameTitle.Title = "七字标题烟测一"; // 7 GBK double-byte characters = 14 bytes.
        service.ValidateGameTitleUpdate(document.GameTitle, document.GameTitle.Title);
        var save = service.Save(project, tables, document, false, false, true);
        var after = File.ReadAllBytes(exePath);
        var verify = service.Load(project, tables);
        if (verify.GameTitle.Title != document.GameTitle.Title ||
            !neighborBefore.SequenceEqual(after.AsSpan(0x8B2D8 + 14, 16).ToArray()) ||
            save.BackupPaths.Count != 1 || save.ReportJsonPaths.Count != 1)
        {
            throw new InvalidOperationException("6.6 14-byte title round-trip, neighbor, backup, or report validation failed.");
        }

        var overflowHash = ComputeFileSha256(exePath);
        var overflowBackupCount = CountBackups(smokeRoot);
        try
        {
            service.ValidateGameTitleUpdate(verify.GameTitle, "七字标题烟测一A");
            throw new InvalidOperationException("6.6 15-byte title was not rejected.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("超过字段容量", StringComparison.Ordinal))
        {
        }
        if (ComputeFileSha256(exePath) != overflowHash || CountBackups(smokeRoot) != overflowBackupCount)
        {
            throw new InvalidOperationException("Overflow validation changed the EXE or created a backup.");
        }

        // Change an unrelated DOS-stub byte: SHA no longer matches, but the structural layout remains valid.
        after[0x40] ^= 0x01;
        File.WriteAllBytes(exePath, after);
        var compatibleVariant = service.Load(project, tables);
        if (!compatibleVariant.GameTitle.CanEdit || !compatibleVariant.GameTitle.Diagnostic.Contains("SHA256 与参考样本不同", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Structurally compatible 6.6 variant was not accepted after an unrelated hash change.");
        }

        var concurrentDocument = service.Load(project, tables);
        var concurrentBytes = File.ReadAllBytes(exePath);
        concurrentBytes[0x8B2D8 + 12] = (byte)'A';
        File.WriteAllBytes(exePath, concurrentBytes);
        concurrentDocument.GameTitle.Title = "并发修改检查";
        var concurrentHash = ComputeFileSha256(exePath);
        var concurrentBackups = CountBackups(smokeRoot);
        try
        {
            service.Save(project, tables, concurrentDocument, false, false, true);
            throw new InvalidOperationException("Concurrent title modification was not rejected.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("加载后已被其他程序修改", StringComparison.Ordinal))
        {
        }
        if (ComputeFileSha256(exePath) != concurrentHash || CountBackups(smokeRoot) != concurrentBackups)
        {
            throw new InvalidOperationException("Concurrent modification rejection changed the EXE or created a backup.");
        }

        var signatureRoot = smokeRoot + "_signature";
        CopyGameTitleFixture(baseline66, signatureRoot);
        var signatureProject = new ProjectDetector().CreateProjectFromGameRoot(signatureRoot);
        var signatureExe = signatureProject.ResolveGameFile("Ekd5.exe");
        var signatureBytes = File.ReadAllBytes(signatureExe);
        var signature = new byte[] { 0x68, 0xD8, 0xC8, 0x48, 0x00 };
        var replacedSignatures = ReplaceAllBytes(signatureBytes, signature, new byte[] { 0x68, 0xD9, 0xC8, 0x48, 0x00 });
        if (replacedSignatures <= 0) throw new InvalidOperationException("6.6 title reference signature fixture was not found.");
        File.WriteAllBytes(signatureExe, signatureBytes);
        var unsupported = service.Load(signatureProject, tables);
        if (unsupported.GameTitle.CanRead || unsupported.GameTitle.CanEdit || unsupported.GameTitle.CapacityBytes != 0 ||
            !unsupported.GameTitle.Diagnostic.Contains("代码引用签名", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Broken title signature did not degrade to a non-editable title capability.");
        }
        var unsupportedHash = ComputeFileSha256(signatureExe);
        var unsupportedBackups = CountBackups(signatureRoot);
        try
        {
            service.ValidateGameTitleUpdate(unsupported.GameTitle, "禁止写入");
            throw new InvalidOperationException("Unsupported title layout was not rejected.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("不支持安全读写游戏标题", StringComparison.Ordinal))
        {
        }
        if (ComputeFileSha256(signatureExe) != unsupportedHash || CountBackups(signatureRoot) != unsupportedBackups)
        {
            throw new InvalidOperationException("Unsupported-layout rejection changed the EXE or created a backup.");
        }

        Console.WriteLine($"GAME_TITLE_VERSIONED_SMOKE_OK v65=0x8D3C4/32B v66=0x8B2D8/14B root={smokeRoot}");
    }

    private static void Assert65TitleRoundTrip(
        string baseline65,
        string workspaceRoot,
        IReadOnlyList<HexTableDefinition> tables)
    {
        var smokeRoot = Path.Combine(workspaceRoot, "CCZModStudio_TestCopies", "GameTitle65Regression_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        CopyGameTitleFixture(baseline65, smokeRoot);
        var project = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var service = new GlobalSettingsService();
        var document = service.Load(project, tables);
        document.GameTitle.Title = "六五标题回归烟测";
        service.ValidateGameTitleUpdate(document.GameTitle, document.GameTitle.Title);
        var save = service.Save(project, tables, document, false, false, true);
        var reread = service.Load(project, tables).GameTitle;
        if (reread.Title != document.GameTitle.Title || reread.Offset != 0x8D3C4 || reread.CapacityBytes != 32 ||
            save.BackupPaths.Count != 1 || save.ReportJsonPaths.Count != 1)
        {
            throw new InvalidOperationException("6.5 title regression round-trip failed.");
        }
    }

    private static void AssertGameTitleBaseline(
        string gameRoot,
        IReadOnlyList<HexTableDefinition> tables,
        string version,
        string layoutKey,
        long offset,
        int capacity)
    {
        if (!Directory.Exists(gameRoot)) throw new DirectoryNotFoundException(gameRoot);
        var project = new ProjectDetector().CreateProjectFromGameRoot(gameRoot);
        var title = new GlobalSettingsService().Load(project, tables).GameTitle;
        if (!title.CanRead || !title.CanEdit || title.EngineVersion != version || title.LayoutKey != layoutKey ||
            title.Offset != offset || title.CapacityBytes != capacity || string.IsNullOrWhiteSpace(title.Title))
        {
            throw new InvalidOperationException(
                $"{version} title baseline mismatch: read={title.CanRead}, edit={title.CanEdit}, layout={title.LayoutKey}, offset=0x{title.Offset:X}, capacity={title.CapacityBytes}, title={title.Title}, diagnostic={title.Diagnostic}");
        }
    }

    private static void CopyGameTitleFixture(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5" })
        {
            File.Copy(Path.Combine(sourceRoot, fileName), Path.Combine(destinationRoot, fileName), overwrite: false);
        }
        File.WriteAllText(Path.Combine(destinationRoot, "_CCZModStudio_TestCopy.txt"), "Purpose=versioned game title smoke\r\n", Encoding.UTF8);
    }

    private static int ReplaceAllBytes(byte[] haystack, byte[] needle, byte[] replacement)
    {
        if (needle.Length != replacement.Length) throw new ArgumentException("Replacement length must match needle length.");
        var replaced = 0;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (!haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) continue;
            replacement.CopyTo(haystack, i);
            replaced++;
            i += needle.Length - 1;
        }
        return replaced;
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static int CountBackups(string gameRoot)
    {
        var project = new ProjectDetector().CreateProjectFromGameRoot(gameRoot);
        var backupRoot = ProjectBackupPathService.GetBackupRoot(project);
        return Directory.Exists(backupRoot) ? Directory.EnumerateFiles(backupRoot).Count() : 0;
    }

    static void RunGlobalSettingsDialogSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var dialog = new GlobalSettingsDialog(project, tables);
                if (dialog.Controls.Count == 0)
                {
                    throw new InvalidOperationException("全局设定对话框没有创建任何控件。");
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw new InvalidOperationException("全局设定对话框构造 smoke 失败。", failure);
        }

        Console.WriteLine("GLOBAL_SETTINGS_DIALOG_SMOKE_OK");
    }

    static void RunGlobalSettingsWriteSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "GlobalSettingsSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("全局设定写入烟测缺少核心文件。", source);
            }

            File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=global settings write smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var service = new GlobalSettingsService();
        var document = service.Load(testProject, tables);

        var seriesRow = FindSmokeRowById(document.JobSeriesNames, 0);
        var detailedRow = FindSmokeRowById(document.DetailedJobNames, 0);
        var originalSeries = Convert.ToString(seriesRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var originalDetailed = Convert.ToString(detailedRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var originalTitle = document.GameTitle.Title;

        var changedSeries = originalSeries == "烟测全局" ? "写测全局" : "烟测全局";
        var changedDetailed = originalDetailed == "烟测职业" ? "写测职业" : "烟测职业";
        var changedTitle = originalTitle == "全局设定烟测" ? "全局设定写测" : "全局设定烟测";

        seriesRow["名称"] = changedSeries;
        detailedRow["名称"] = changedDetailed;
        document.GameTitle.Title = changedTitle;

        var save = service.Save(testProject, tables, document, saveJobSeries: true, saveDetailedJobs: true, saveGameTitle: true);
        if (save.ChangedBytes <= 0 ||
            save.BackupPaths.Count < 3 ||
            save.BackupPaths.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)) ||
            save.ReportJsonPaths.Count < 3 ||
            save.ReportJsonPaths.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)))
        {
            throw new InvalidOperationException("全局设定写入烟测备份、报告或变化字节验证失败。");
        }

        var verify = service.Load(testProject, tables);
        var actualSeries = Convert.ToString(FindSmokeRowById(verify.JobSeriesNames, 0)["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var actualDetailed = Convert.ToString(FindSmokeRowById(verify.DetailedJobNames, 0)["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var actualTitle = verify.GameTitle.Title;
        if (actualSeries != changedSeries || actualDetailed != changedDetailed || actualTitle != changedTitle)
        {
            throw new InvalidOperationException($"全局设定写入烟测复读失败：series={actualSeries}, detailed={actualDetailed}, title={actualTitle}");
        }

        Console.WriteLine($"GLOBAL_SETTINGS_WRITE_SMOKE_OK series={originalSeries}->{actualSeries} detailed={originalDetailed}->{actualDetailed} title={originalTitle}->{actualTitle} changedBytes={save.ChangedBytes} root={smokeRoot}");
    }
}
