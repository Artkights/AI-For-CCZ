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

        VerifyRawEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Pmapobj.e5"), 3, 48 * 1280);
        VerifyRawEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Pmapobj.e5"), 4, 48 * 1280);

        Console.WriteLine($"R_IMAGE_RAW_REPLACE_SMOKE OK root={smokeRoot} r1=3/4 report={Path.GetFileName(result.AggregateReportPath)}");
    }
}
