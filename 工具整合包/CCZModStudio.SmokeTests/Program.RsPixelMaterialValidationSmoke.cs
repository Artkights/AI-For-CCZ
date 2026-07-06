using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Globalization;

internal partial class Program
{
    static void RunRsPixelMaterialValidationSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "RsPixelMaterialValidationSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
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
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=R/S pixel material validation smoke\r\n");
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);

        var materialRoot = Path.Combine(smokeRoot, "RS_PixelDesign", "SmokeCharacter");
        var rFolder = Path.Combine(materialRoot, "materials", "r_actor");
        var sFolder = Path.Combine(materialRoot, "materials", "s_unit");
        Directory.CreateDirectory(rFolder);
        Directory.CreateDirectory(sFolder);

        CreateSmokeBmp(Path.Combine(rFolder, "front.bmp"), 48, 1280, Color.FromArgb(255, 220, 40, 60), Color.FromArgb(255, 40, 160, 220));
        CreateSmokeBmp(Path.Combine(rFolder, "back.bmp"), 48, 1280, Color.FromArgb(255, 40, 180, 90), Color.FromArgb(255, 220, 160, 40));
        CreateSmokeBmp(Path.Combine(sFolder, "mov.bmp"), 48, 528, Color.FromArgb(255, 90, 180, 230), Color.FromArgb(255, 230, 90, 150));
        CreateSmokeBmp(Path.Combine(sFolder, "atk.bmp"), 64, 768, Color.FromArgb(255, 190, 80, 220), Color.FromArgb(255, 80, 220, 180));
        CreateSmokeBmp(Path.Combine(sFolder, "spc.bmp"), 48, 240, Color.FromArgb(255, 140, 80, 220), Color.FromArgb(255, 240, 220, 40));

        var service = new RsPixelMaterialValidationService();
        var result = service.Validate(testProject, new RsPixelMaterialValidationRequest
        {
            MaterialRoot = materialRoot,
            RImageId = 1,
            SImageId = 250
        });

        if (!result.FormatPassed ||
            !result.PreviewPassed ||
            !result.ReadyForTestCopyWrite ||
            !result.RequiresTestCopyWrite ||
            result.Files.Count != 5 ||
            result.Errors.Count != 0)
        {
            throw new InvalidOperationException("R/S pixel material validation did not pass for a complete smoke package.");
        }

        if (result.RPreview == null ||
            result.RPreview.Mapping.FrontImageNumber != 3 ||
            result.RPreview.Mapping.BackImageNumber != 4 ||
            result.RPreview.TotalOperationCount != 2)
        {
            throw new InvalidOperationException("R/S pixel validation R preview mapping failed.");
        }

        if (result.SPreview == null ||
            !result.SPreview.Mapping.ImageNumbers.SequenceEqual(new[] { 554 }) ||
            result.SPreview.TotalOperationCount != 3)
        {
            throw new InvalidOperationException("R/S pixel validation S preview mapping failed.");
        }

        if (result.PreviewOperations.Count != 5 ||
            result.PreviewOperations.Any(operation =>
                !operation.OldKind.Equals("RAW", StringComparison.OrdinalIgnoreCase) ||
                !operation.NewKind.Equals("PNG", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("R/S pixel validation should expose RAW->PNG preview operations for test-copy risk tracking.");
        }

        var mov = result.Files.Single(file => file.ExpectedFileName.Equals("mov.bmp", StringComparison.OrdinalIgnoreCase));
        if (!mov.MagentaKeyLikely ||
            mov.NearMagentaPixelCount <= 0 ||
            mov.StrictMagentaPixelCount <= 0 ||
            !mov.DimensionPassed)
        {
            throw new InvalidOperationException("R/S pixel validation did not detect the expected magenta-key background.");
        }

        Console.WriteLine($"RS_PIXEL_MATERIAL_VALIDATION_SMOKE OK root={smokeRoot} r={string.Join('/', result.RPreview.Mapping.ImageNumbers)} s={string.Join('/', result.SPreview.Mapping.ImageNumbers)} ops={result.PreviewOperations.Count}");
    }
}
