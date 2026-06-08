using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Globalization;
using System.Windows.Forms;

internal partial class Program
{
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
