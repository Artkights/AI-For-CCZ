using System.Text;
using System.Text.Json;

namespace CCZModStudio.Core;

public sealed record PackageSelfCheckIssue(string RelativePath, string FullPath, string Message);

public sealed record PackageSelfCheckResult(
    bool Passed,
    string RuntimeRoot,
    IReadOnlyList<PackageSelfCheckIssue> MissingFiles);

public static class PackageSelfCheckService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyList<(string RelativePath, string FullPath, string Message)> RequiredFiles =
    [
        ("ConfigTable\\HexTable.xml", PortableInstallPaths.HexTablePath, "缺少内置 HexTable 表定义，表格编辑和大部分数据读写不可用。"),
        ("LegacyResources\\a新剧本编辑器v0.23\\CczString.ini", PortableInstallPaths.LegacyResource("a新剧本编辑器v0.23", "CczString.ini"), "缺少旧剧本命令字典，R/S 剧本树和写回校验不可用。"),
        ("LegacyResources\\a新剧本编辑器v0.23\\CczSceneEditor2.ini", PortableInstallPaths.LegacyResource("a新剧本编辑器v0.23", "CczSceneEditor2.ini"), "缺少旧剧本编辑器配置，部分旧版参数布局不可用。"),
        ("LegacyResources\\a新剧本编辑器v0.23\\cczEditor2\\cczEditor2.rc", PortableInstallPaths.LegacyResource("a新剧本编辑器v0.23", "cczEditor2", "cczEditor2.rc"), "缺少旧 MFC 对话框资源定义，旧版对话框映射不可用。"),
        ("LegacyResources\\a新剧本编辑器v0.23\\cczEditor2\\cczEditor2View.cpp", PortableInstallPaths.LegacyResource("a新剧本编辑器v0.23", "cczEditor2", "cczEditor2View.cpp"), "缺少旧 MFC 事件映射源码，旧版对话框字段解释不可用。"),
        ("LegacyResources\\B形象指定器\\形象指定器6.5\\System.ini", PortableInstallPaths.LegacyResource("B形象指定器", "形象指定器6.5", "System.ini"), "缺少形象指定器 System.ini，装备分段和部分映射提示会退化。"),
        ("LegacyResources\\普罗-综合工具v0.3\\素材库\\地形\\1.png", PortableInstallPaths.LegacyResource("普罗-综合工具v0.3", "素材库", "地形", "1.png"), "缺少内置素材库，地图素材预览和素材索引会退化。"),
        ("Assets\\Palettes\\tsb", PortableInstallPaths.PaletteTsbPath, "缺少 tsb 调色板，RAW/E5 图像预览和编码可能不可用。"),
        ("Assets\\About\\Doro-white.ico", PortableInstallPaths.AboutAsset("Doro-white.ico"), "缺少应用图标 ico，窗口左上角和任务栏图标可能无法保持一致。"),
        ("Assets\\About\\Doro-white.png", PortableInstallPaths.AboutAsset("Doro-white.png"), "缺少应用图标 png，跨设备复用和图标素材预览会退化。"),
        ("Assets\\About\\doro.jpg", PortableInstallPaths.AboutAsset("doro.jpg"), "缺少关于页图片。"),
        ("Assets\\About\\Doro.webp", PortableInstallPaths.AboutAsset("Doro.webp"), "缺少关于页备用图片。"),
        ("Templates\\剧本文本导入AI说明模板.md", PortableInstallPaths.ScenarioTextImportTemplatePath, "缺少剧本文本导入模板。"),
        ("Package\\GUI-PACKAGE-MANIFEST.txt", PortableInstallPaths.PackageManifestPath, "缺少 GUI 包清单。"),
        ("Package\\self-check.json", PortableInstallPaths.SelfCheckPath, "缺少 GUI 包自检定义。")
    ];

    public static PackageSelfCheckResult Check()
    {
        var missing = RequiredFiles
            .Where(item => !File.Exists(item.FullPath))
            .Select(item => new PackageSelfCheckIssue(item.RelativePath, item.FullPath, item.Message))
            .ToList();

        var result = new PackageSelfCheckResult(
            missing.Count == 0,
            PortableInstallPaths.RuntimeRoot,
            missing);

        WriteRuntimeReport(result);
        return result;
    }

    public static string BuildUserMessage(PackageSelfCheckResult result)
    {
        if (result.Passed)
        {
            return "GUI 依赖自检通过。";
        }

        var builder = new StringBuilder();
        builder.AppendLine("GUI 依赖自检失败。请确认发送给别人的 net 文件夹完整，且不要只复制部分 DLL 或资源文件。");
        builder.AppendLine();
        builder.AppendLine("运行目录：");
        builder.AppendLine(result.RuntimeRoot);
        builder.AppendLine();
        builder.AppendLine("缺少文件：");
        foreach (var issue in result.MissingFiles)
        {
            builder.AppendLine("- " + issue.RelativePath);
            builder.AppendLine("  " + issue.Message);
        }

        return builder.ToString();
    }

    private static void WriteRuntimeReport(PackageSelfCheckResult result)
    {
        try
        {
            Directory.CreateDirectory(PortableInstallPaths.PackageRoot);
            var path = Path.Combine(PortableInstallPaths.PackageRoot, "last-self-check-result.json");
            File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions), Encoding.UTF8);
        }
        catch
        {
            // Startup diagnostics must never block the editor.
        }
    }
}
