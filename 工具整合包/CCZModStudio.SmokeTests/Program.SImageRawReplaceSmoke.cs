using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

internal partial class Program
{
    static void RunSImageRawReplaceSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "SImageRawReplaceSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);
        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5", "Pmapobj.e5", "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5" })
        {
            var source = CharacterImageResourceService.ResolveGameFile(project, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, fileName), overwrite: false);
            }
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=S image RAW replace smoke\r\n");
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);

        var materialRoot = Path.Combine(smokeRoot, "_SImageRawMaterials");
        Directory.CreateDirectory(materialRoot);
        CreateSmokeBmp(Path.Combine(materialRoot, "MOV.BMP"), 48, 528, Color.FromArgb(255, 220, 30, 80), Color.FromArgb(255, 30, 160, 220));
        CreateSmokeBmp(Path.Combine(materialRoot, "ATK.BMP"), 64, 768, Color.FromArgb(255, 30, 180, 90), Color.FromArgb(255, 220, 140, 30));
        CreateSmokeBmp(Path.Combine(materialRoot, "SPC.BMP"), 48, 240, Color.FromArgb(255, 140, 80, 220), Color.FromArgb(255, 240, 220, 40));

        var service = new SImageReplaceService();
        var replace = new E5ImageReplaceService();
        var preview = service.Preview(testProject, new SImageReplaceRequest
        {
            SImageId = 250,
            MaterialFolder = materialRoot,
            WriteMode = "test_copy"
        });
        if (preview.Mapping.ImageNumbers.Count != 1 || preview.Mapping.ImageNumbers[0] != 554 || preview.TotalOperationCount != 3)
        {
            throw new InvalidOperationException("S>=33 一键 RAW 替换预览映射不符合预期。");
        }

        var result = service.Replace(testProject, new SImageReplaceRequest
        {
            SImageId = 250,
            MaterialFolder = materialRoot,
            WriteMode = "test_copy"
        });
        if (result.TotalOperationCount != 3 || !File.Exists(result.AggregateReportPath))
        {
            throw new InvalidOperationException("S>=33 一键 RAW 替换写入结果不符合预期。");
        }

        VerifyRawEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_mov.e5"), 554, 48 * 528);
        VerifyRawEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_atk.e5"), 554, 64 * 768);
        VerifyRawEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_spc.e5"), 554, 48 * 240);

        var staged = service.Replace(testProject, new SImageReplaceRequest
        {
            SImageId = 1,
            MaterialFolder = materialRoot,
            WriteMode = "test_copy"
        });
        if (!staged.Mapping.ImageNumbers.SequenceEqual(new[] { 241, 242, 243 }) || staged.TotalOperationCount != 9)
        {
            throw new InvalidOperationException("S=1 三阶段一键 RAW 替换写入结果不符合预期。");
        }

        foreach (var imageNumber in new[] { 241, 242, 243 })
        {
            VerifyRawEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_mov.e5"), imageNumber, 48 * 528);
            VerifyRawEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_atk.e5"), imageNumber, 64 * 768);
            VerifyRawEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_spc.e5"), imageNumber, 48 * 240);
        }

        Console.WriteLine($"S_IMAGE_RAW_REPLACE_SMOKE OK root={smokeRoot} s250=554 s1=241/242/243 report={Path.GetFileName(result.AggregateReportPath)}");
    }

    private static void CreateSmokeBmp(string path, int width, int height, Color primary, Color secondary)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Magenta);
        using var primaryBrush = new SolidBrush(primary);
        using var secondaryBrush = new SolidBrush(secondary);
        var frameHeight = width == 64 ? 64 : 48;
        for (var y = 0; y < height; y += frameHeight)
        {
            graphics.FillRectangle(primaryBrush, Math.Max(1, width / 6), y + 2, Math.Max(2, width / 3), Math.Max(2, frameHeight - 4));
            graphics.FillEllipse(secondaryBrush, Math.Max(2, width / 2), y + 8, Math.Max(4, width / 3), Math.Max(4, frameHeight / 2));
        }

        bitmap.Save(path, ImageFormat.Bmp);
    }

    private static void VerifyRawEntry(E5ImageReplaceService replace, string path, int imageNumber, int expectedLength)
    {
        var bytes = replace.ReadEntryBytes(path, imageNumber);
        if (bytes.Length != expectedLength)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} #{imageNumber} RAW 长度不符合预期：{bytes.Length} != {expectedLength}");
        }

        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} #{imageNumber} 仍然是 BMP 头。");
        }

        var entry = replace.ReadIndex(path)[imageNumber - 1];
        if (!entry.Kind.Equals("RAW", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} #{imageNumber} 未识别为 RAW：{entry.Kind}");
        }
    }
}
