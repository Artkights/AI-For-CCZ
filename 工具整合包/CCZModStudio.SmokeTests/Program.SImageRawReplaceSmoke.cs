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

        var moveOnlyRoot = Path.Combine(smokeRoot, "_SImageRawMaterialsMoveOnly");
        Directory.CreateDirectory(moveOnlyRoot);
        CreateSmokeBmp(Path.Combine(moveOnlyRoot, "MOV.BMP"), 48, 528, Color.FromArgb(255, 90, 180, 230), Color.FromArgb(255, 230, 90, 150));
        var moveOnlyPreview = service.Preview(testProject, new SImageReplaceRequest
        {
            SImageId = 249,
            MaterialFolder = moveOnlyRoot,
            WriteMode = "test_copy"
        });
        if (moveOnlyPreview.Mapping.ImageNumbers.Count != 1 ||
            moveOnlyPreview.Mapping.ImageNumbers[0] != 553 ||
            moveOnlyPreview.TotalOperationCount != 1 ||
            moveOnlyPreview.Files.Count != 1 ||
            moveOnlyPreview.Files[0].TargetFileName != "Unit_mov.e5")
        {
            throw new InvalidOperationException("S 部分导入预览未只匹配移动素材。");
        }

        var moveOnlyResult = service.Replace(testProject, new SImageReplaceRequest
        {
            SImageId = 249,
            MaterialFolder = moveOnlyRoot,
            WriteMode = "test_copy"
        });
        if (moveOnlyResult.TotalOperationCount != 1 || !File.Exists(moveOnlyResult.AggregateReportPath))
        {
            throw new InvalidOperationException("S 部分导入写入结果不符合预期。");
        }

        VerifyRawEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_mov.e5"), 553, 48 * 528);

        var atkOnlyRoot = Path.Combine(smokeRoot, "_SImageRawMaterialsAtkOnly");
        Directory.CreateDirectory(atkOnlyRoot);
        CreateSmokeBmp(Path.Combine(atkOnlyRoot, "ATK.BMP"), 64, 768, Color.FromArgb(255, 190, 80, 220), Color.FromArgb(255, 80, 220, 180));
        var atkOnly = service.Replace(testProject, new SImageReplaceRequest
        {
            SImageId = 1,
            MaterialFolder = atkOnlyRoot,
            WriteMode = "test_copy"
        });
        if (!atkOnly.Mapping.ImageNumbers.SequenceEqual(new[] { 241, 242, 243 }) ||
            atkOnly.TotalOperationCount != 3 ||
            atkOnly.Files.Count != 1 ||
            atkOnly.Files[0].TargetFileName != "Unit_atk.e5")
        {
            throw new InvalidOperationException("S=1 部分导入未只写入攻击素材对应三张 Unit 图。");
        }

        foreach (var imageNumber in new[] { 241, 242, 243 })
        {
            VerifyRawEntry(replace, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_atk.e5"), imageNumber, 64 * 768);
        }

        var emptyRoot = Path.Combine(smokeRoot, "_SImageRawMaterialsEmpty");
        Directory.CreateDirectory(emptyRoot);
        File.WriteAllText(Path.Combine(emptyRoot, "readme.txt"), "unused");
        AssertPreviewFails(
            () => service.Preview(testProject, new SImageReplaceRequest
            {
                SImageId = 248,
                MaterialFolder = emptyRoot,
                WriteMode = "test_copy"
            }),
            "没有找到可导入的 S 形象素材");

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

        RunJobSImageRawReplaceSmoke(testProject, replace);

        Console.WriteLine($"S_IMAGE_RAW_REPLACE_SMOKE OK root={smokeRoot} s250=554 s1=241/242/243 report={Path.GetFileName(result.AggregateReportPath)}");
    }

    private static void RunJobSImageRawReplaceSmoke(CczProject testProject, E5ImageReplaceService replace)
    {
        var materialRoot = Path.Combine(testProject.GameRoot, "_JobSImageRawMaterials");
        Directory.CreateDirectory(materialRoot);
        CreateSmokeBmp(Path.Combine(materialRoot, "MOV.BMP"), 48, 528, Color.FromArgb(255, 20, 80, 210), Color.FromArgb(255, 240, 220, 90));
        CreateSmokeBmp(Path.Combine(materialRoot, "ATK.BMP"), 64, 768, Color.FromArgb(255, 200, 40, 100), Color.FromArgb(255, 70, 210, 170));
        CreateSmokeBmp(Path.Combine(materialRoot, "SPC.BMP"), 48, 240, Color.FromArgb(255, 90, 40, 210), Color.FromArgb(255, 240, 120, 30));

        var service = new JobSImageReplaceService();
        var movPath = CharacterImageResourceService.ResolveGameFile(testProject, "Unit_mov.e5");
        var atkPath = CharacterImageResourceService.ResolveGameFile(testProject, "Unit_atk.e5");
        var spcPath = CharacterImageResourceService.ResolveGameFile(testProject, "Unit_spc.e5");
        var before32 = replace.ReadEntryBytes(movPath, 32);
        var before33 = replace.ReadEntryBytes(movPath, 33);
        var beforeAtk32 = replace.ReadEntryBytes(atkPath, 32);
        var beforeAtk33 = replace.ReadEntryBytes(atkPath, 33);
        var beforeSpc32 = replace.ReadEntryBytes(spcPath, 32);
        var beforeSpc33 = replace.ReadEntryBytes(spcPath, 33);

        var previewOne = service.Preview(testProject, new JobSImageReplaceRequest
        {
            JobId = 10,
            MaterialFolder = materialRoot,
            FactionSlots = new[] { 1 },
            WriteMode = "test_copy"
        });
        if (previewOne.TotalOperationCount != 3 ||
            previewOne.Factions.Count != 1 ||
            !previewOne.Factions[0].Preview.Mapping.ImageNumbers.SequenceEqual(new[] { 31 }))
        {
            throw new InvalidOperationException("Job S image preview for faction 1 did not map only Unit #31.");
        }

        var resultOne = service.Replace(testProject, new JobSImageReplaceRequest
        {
            JobId = 10,
            MaterialFolder = materialRoot,
            FactionSlots = new[] { 1 },
            WriteMode = "test_copy"
        });
        if (resultOne.TotalOperationCount != 3)
        {
            throw new InvalidOperationException("Job S image replace for faction 1 did not write exactly 3 entries.");
        }

        VerifyRawEntry(replace, movPath, 31, 48 * 528);
        VerifyRawEntry(replace, atkPath, 31, 64 * 768);
        VerifyRawEntry(replace, spcPath, 31, 48 * 240);
        var afterFactionOneMov31 = replace.ReadEntryBytes(movPath, 31);
        var afterFactionOneAtk31 = replace.ReadEntryBytes(atkPath, 31);
        var afterFactionOneSpc31 = replace.ReadEntryBytes(spcPath, 31);
        AssertEntryBytesEqual(replace.ReadEntryBytes(movPath, 32), before32, "Unit_mov.e5 #32 changed even though faction 2 was not selected.");
        AssertEntryBytesEqual(replace.ReadEntryBytes(movPath, 33), before33, "Unit_mov.e5 #33 changed even though faction 3 was not selected.");
        AssertEntryBytesEqual(replace.ReadEntryBytes(atkPath, 32), beforeAtk32, "Unit_atk.e5 #32 changed even though faction 2 was not selected.");
        AssertEntryBytesEqual(replace.ReadEntryBytes(atkPath, 33), beforeAtk33, "Unit_atk.e5 #33 changed even though faction 3 was not selected.");
        AssertEntryBytesEqual(replace.ReadEntryBytes(spcPath, 32), beforeSpc32, "Unit_spc.e5 #32 changed even though faction 2 was not selected.");
        AssertEntryBytesEqual(replace.ReadEntryBytes(spcPath, 33), beforeSpc33, "Unit_spc.e5 #33 changed even though faction 3 was not selected.");

        var materialRootTwo = Path.Combine(testProject.GameRoot, "_JobSImageRawMaterialsTwo");
        Directory.CreateDirectory(materialRootTwo);
        CreateSmokeBmp(Path.Combine(materialRootTwo, "MOV.BMP"), 48, 528, Color.FromArgb(255, 40, 210, 80), Color.FromArgb(255, 210, 70, 220));
        CreateSmokeBmp(Path.Combine(materialRootTwo, "ATK.BMP"), 64, 768, Color.FromArgb(255, 230, 120, 40), Color.FromArgb(255, 40, 140, 230));
        CreateSmokeBmp(Path.Combine(materialRootTwo, "SPC.BMP"), 48, 240, Color.FromArgb(255, 180, 40, 210), Color.FromArgb(255, 80, 230, 120));

        var previewTwo = service.Preview(testProject, new JobSImageReplaceRequest
        {
            JobId = 10,
            MaterialFolder = materialRootTwo,
            FactionSlots = new[] { 3, 2, 2 },
            WriteMode = "test_copy"
        });
        if (previewTwo.TotalOperationCount != 6 ||
            !previewTwo.Factions.SelectMany(x => x.Preview.Mapping.ImageNumbers).SequenceEqual(new[] { 32, 33 }))
        {
            throw new InvalidOperationException("Job S image preview for factions 2/3 did not map only Unit #32/#33.");
        }

        var resultTwo = service.Replace(testProject, new JobSImageReplaceRequest
        {
            JobId = 10,
            MaterialFolder = materialRootTwo,
            FactionSlots = new[] { 2, 3 },
            WriteMode = "test_copy"
        });
        if (resultTwo.TotalOperationCount != 6)
        {
            throw new InvalidOperationException("Job S image replace for factions 2/3 did not write exactly 6 entries.");
        }

        VerifyRawEntry(replace, movPath, 32, 48 * 528);
        VerifyRawEntry(replace, atkPath, 32, 64 * 768);
        VerifyRawEntry(replace, spcPath, 32, 48 * 240);
        VerifyRawEntry(replace, movPath, 33, 48 * 528);
        VerifyRawEntry(replace, atkPath, 33, 64 * 768);
        VerifyRawEntry(replace, spcPath, 33, 48 * 240);
        AssertEntryBytesEqual(replace.ReadEntryBytes(movPath, 31), afterFactionOneMov31, "Unit_mov.e5 #31 changed when only factions 2/3 were selected.");
        AssertEntryBytesEqual(replace.ReadEntryBytes(atkPath, 31), afterFactionOneAtk31, "Unit_atk.e5 #31 changed when only factions 2/3 were selected.");
        AssertEntryBytesEqual(replace.ReadEntryBytes(spcPath, 31), afterFactionOneSpc31, "Unit_spc.e5 #31 changed when only factions 2/3 were selected.");

        var previewAll = service.Preview(testProject, new JobSImageReplaceRequest
        {
            JobId = 10,
            MaterialFolder = materialRoot,
            FactionSlots = new[] { 1, 2, 3 },
            WriteMode = "test_copy"
        });
        if (previewAll.TotalOperationCount != 9 ||
            !previewAll.Factions.SelectMany(x => x.Preview.Mapping.ImageNumbers).SequenceEqual(new[] { 31, 32, 33 }))
        {
            throw new InvalidOperationException("Job S image preview for all factions did not map Unit #31/#32/#33.");
        }

        var resultAll = service.Replace(testProject, new JobSImageReplaceRequest
        {
            JobId = 10,
            MaterialFolder = materialRoot,
            FactionSlots = new[] { 1, 2, 3 },
            WriteMode = "test_copy"
        });
        if (resultAll.TotalOperationCount != 9 ||
            !resultAll.Factions.SelectMany(x => x.Result.Mapping.ImageNumbers).SequenceEqual(new[] { 31, 32, 33 }))
        {
            throw new InvalidOperationException("Job S image replace for all factions did not write Unit #31/#32/#33.");
        }

        foreach (var imageNumber in new[] { 31, 32, 33 })
        {
            VerifyRawEntry(replace, movPath, imageNumber, 48 * 528);
            VerifyRawEntry(replace, atkPath, imageNumber, 64 * 768);
            VerifyRawEntry(replace, spcPath, imageNumber, 48 * 240);
        }
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

    private static void AssertEntryBytesEqual(byte[] actual, byte[] expected, string message)
    {
        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException(message);
        }
    }
}
