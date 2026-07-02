using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

internal partial class Program
{
    static void RunRImageRawReplaceSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "RImageRawReplaceSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);
        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5", "Pmapobj.e5" })
        {
            var source = CharacterImageResourceService.ResolveGameFile(project, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, fileName), overwrite: false);
            }
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=R image RAW replace smoke\r\n");
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);

        var materialRoot = Path.Combine(smokeRoot, "_RImageRawMaterials");
        Directory.CreateDirectory(materialRoot);
        CreateSmokeBmp(Path.Combine(materialRoot, "X.BMP"), 48, 1280, Color.FromArgb(255, 220, 40, 60), Color.FromArgb(255, 40, 160, 220));
        CreateSmokeBmp(Path.Combine(materialRoot, "Y.BMP"), 48, 1280, Color.FromArgb(255, 40, 180, 90), Color.FromArgb(255, 220, 160, 40));

        var service = new RImageReplaceService();
        var replace = new E5ImageReplaceService();
        var preview = service.Preview(testProject, new RImageReplaceRequest
        {
            RImageId = 1,
            MaterialFolder = materialRoot,
            WriteMode = "test_copy"
        });
        if (preview.Mapping.FrontImageNumber != 3 ||
            preview.Mapping.BackImageNumber != 4 ||
            !preview.Mapping.ImageNumbers.SequenceEqual(new[] { 3, 4 }) ||
            preview.TotalOperationCount != 2)
        {
            throw new InvalidOperationException("R=1 一键 RAW 替换预览映射不符合预期。");
        }

        var result = service.Replace(testProject, new RImageReplaceRequest
        {
            RImageId = 1,
            MaterialFolder = materialRoot,
            WriteMode = "test_copy"
        });
        if (result.TotalOperationCount != 2 || !File.Exists(result.AggregateReportPath))
        {
            throw new InvalidOperationException("R=1 一键 RAW 替换写入结果不符合预期。");
        }

        VerifyTrueColorEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Pmapobj.e5"), 3, 48 * 1280);
        VerifyTrueColorEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Pmapobj.e5"), 4, 48 * 1280);

        var frontOnlyRoot = Path.Combine(smokeRoot, "_RImageRawMaterialsFrontOnly");
        Directory.CreateDirectory(frontOnlyRoot);
        CreateSmokeBmp(Path.Combine(frontOnlyRoot, "X.BMP"), 48, 1280, Color.FromArgb(255, 90, 180, 230), Color.FromArgb(255, 230, 80, 120));
        var frontOnlyPreview = service.Preview(testProject, new RImageReplaceRequest
        {
            RImageId = 2,
            MaterialFolder = frontOnlyRoot,
            WriteMode = "test_copy"
        });
        if (frontOnlyPreview.TotalOperationCount != 1 ||
            frontOnlyPreview.Files.Count != 1 ||
            frontOnlyPreview.Files[0].ImageNumber != 5 ||
            frontOnlyPreview.Files[0].Role != "正面")
        {
            throw new InvalidOperationException("R 部分导入预览未只匹配正面素材。");
        }

        var frontOnlyResult = service.Replace(testProject, new RImageReplaceRequest
        {
            RImageId = 2,
            MaterialFolder = frontOnlyRoot,
            WriteMode = "test_copy"
        });
        if (frontOnlyResult.TotalOperationCount != 1 || !File.Exists(frontOnlyResult.AggregateReportPath))
        {
            throw new InvalidOperationException("R 部分导入写入结果不符合预期。");
        }

        VerifyTrueColorEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Pmapobj.e5"), 5, 48 * 1280);

        var duplicateRoot = Path.Combine(smokeRoot, "_RImageRawMaterialsDuplicate");
        Directory.CreateDirectory(duplicateRoot);
        CreateSmokeBmp(Path.Combine(duplicateRoot, "front.bmp"), 48, 1280, Color.FromArgb(255, 180, 80, 220), Color.FromArgb(255, 80, 220, 180));
        CreateSmokeBmp(Path.Combine(duplicateRoot, "x.bmp"), 48, 1280, Color.FromArgb(255, 240, 120, 80), Color.FromArgb(255, 90, 90, 230));
        var duplicatePreview = service.Preview(testProject, new RImageReplaceRequest
        {
            RImageId = 3,
            MaterialFolder = duplicateRoot,
            WriteMode = "test_copy"
        });
        if (duplicatePreview.TotalOperationCount != 1 ||
            duplicatePreview.Files.Count != 1 ||
            !Path.GetFileName(duplicatePreview.Files[0].SourcePath).Equals("front.bmp", StringComparison.OrdinalIgnoreCase) ||
            !duplicatePreview.Warnings.Any(warning => warning.Contains("多个候选", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("R 重复候选未按文件名排序取第一张并发出警告。");
        }

        var emptyRoot = Path.Combine(smokeRoot, "_RImageRawMaterialsEmpty");
        Directory.CreateDirectory(emptyRoot);
        File.WriteAllText(Path.Combine(emptyRoot, "readme.txt"), "unused");
        AssertPreviewFails(
            () => service.Preview(testProject, new RImageReplaceRequest
            {
                RImageId = 4,
                MaterialFolder = emptyRoot,
                WriteMode = "test_copy"
            }),
            "没有找到可导入的 R 形象素材");

        Console.WriteLine($"R_IMAGE_RAW_REPLACE_SMOKE OK root={smokeRoot} r1=3/4 report={Path.GetFileName(result.AggregateReportPath)}");
    }
}
