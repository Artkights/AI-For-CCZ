using CCZModStudio.Core;
using System.Text;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private const string UsageGuideFileName = "普罗工具整合包使用说明.md";

    private void ShowUsageGuideDialog()
    {
        try
        {
            using var dialog = new UsageGuideDialog(LoadUsageGuideMarkdown());
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("打开使用指南失败：" + ex);
            MessageBox.Show(this, ex.Message, "打开使用指南失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string LoadUsageGuideMarkdown()
    {
        foreach (var path in EnumerateUsageGuideCandidates())
        {
            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("读取使用指南失败：" + path + "；" + ex.Message);
            }
        }

        return UsageGuideDialog.FallbackMarkdown;
    }

    private static IEnumerable<string> EnumerateUsageGuideCandidates()
    {
        yield return Path.Combine(PortableInstallPaths.LauncherRoot, UsageGuideFileName);
        yield return Path.Combine(PortableInstallPaths.RuntimeRoot, UsageGuideFileName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), UsageGuideFileName);

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; current != null && i < 8; i++, current = current.Parent)
        {
            yield return Path.Combine(current.FullName, UsageGuideFileName);
            yield return Path.Combine(current.FullName, "Assets", "Package", "RootDocs", UsageGuideFileName);
            yield return Path.Combine(current.FullName, "CCZModStudio", "Assets", "Package", "RootDocs", UsageGuideFileName);
        }
    }
}
