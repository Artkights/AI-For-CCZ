using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

internal static class ProgramRsPixelEditWorkspaceSmoke
{
    public static void Run(string[] args)
    {
        var detector = new ProjectDetector();
        var envGameRoot = Environment.GetEnvironmentVariable("CCZMODSTUDIO_GAME_ROOT");
        var project = string.IsNullOrWhiteSpace(envGameRoot)
            ? detector.DetectDefaultProject()
            : detector.CreateProjectFromGameRoot(envGameRoot);

        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "RsPixelEditWorkspaceSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        var referenceRoot = Path.Combine(smokeRoot, "format_ref");
        Directory.CreateDirectory(referenceRoot);
        CreateSmokeBmp(Path.Combine(referenceRoot, "front.bmp"), 48, 1280, Color.FromArgb(255, 90, 90, 90), Color.FromArgb(255, 180, 120, 40));
        CreateSmokeBmp(Path.Combine(referenceRoot, "back.bmp"), 48, 1280, Color.FromArgb(255, 80, 120, 80), Color.FromArgb(255, 170, 60, 60));
        CreateSmokeBmp(Path.Combine(referenceRoot, "mov.bmp"), 48, 528, Color.FromArgb(255, 90, 100, 180), Color.FromArgb(255, 180, 120, 40));
        CreateSmokeBmp(Path.Combine(referenceRoot, "atk.bmp"), 64, 768, Color.FromArgb(255, 70, 70, 160), Color.FromArgb(255, 220, 200, 80));
        CreateSmokeBmp(Path.Combine(referenceRoot, "spc.bmp"), 48, 240, Color.FromArgb(255, 120, 70, 160), Color.FromArgb(255, 220, 120, 70));

        var design = Path.Combine(smokeRoot, "sunce_design.png");
        CreateDesignStub(design);

        var service = new RsPixelEditWorkspaceService();
        var workspace = service.CreateWorkspace(project, new RsPixelEditWorkspaceRequest
        {
            PackageId = "Smoke_LocalPixelEditor_SingleSpear",
            DisplayName = "Smoke SunCe",
            UnitType = "spear_cavalry",
            DesignImagePath = design,
            FormatReferenceRoot = referenceRoot,
            OverwriteExisting = true
        });
        if (workspace.MaterialFiles.Count != 5 || workspace.Warnings.Count != 0)
        {
            throw new InvalidOperationException("R/S pixel edit workspace did not copy all five material files.");
        }

        var plan = service.BuildPlan(new RsPixelEditPlanRequest
        {
            PackageRoot = workspace.PackageRoot,
            UnitType = "spear_cavalry",
            CharacterBrief = "black-gold mounted commander",
            WeaponBrief = "single long spear only"
        });
        if (!File.Exists(plan.PlanPath) || plan.RecommendedOperations.Count == 0)
        {
            throw new InvalidOperationException("R/S pixel edit plan was not written.");
        }

        var edit = service.ApplyEdits(new RsPixelFrameEditBatchRequest
        {
            PackageRoot = workspace.PackageRoot,
            Operations =
            [
                new RsPixelFrameEditOperation
                {
                    Operation = "clean_face_box",
                    Target = "front",
                    Frames = [0, 1],
                    X = 17,
                    Y = 10,
                    Width = 12,
                    Height = 12,
                    Note = "smoke face clean"
                },
                new RsPixelFrameEditOperation
                {
                    Operation = "erase_effect_residue",
                    Target = "atk",
                    Frames = [0, 1, 2],
                    X = 16,
                    Y = 10,
                    Width = 30,
                    Height = 30,
                    Note = "smoke selective effect residue cleanup"
                },
                new RsPixelFrameEditOperation
                {
                    Operation = "draw_spear_axis",
                    Target = "atk",
                    Frames = [0, 1, 2],
                    X = 10,
                    Y = 42,
                    X2 = 58,
                    Y2 = 16,
                    Color = "#704B2A",
                    Note = "smoke single spear axis"
                },
                new RsPixelFrameEditOperation
                {
                    Operation = "draw_spear_tip",
                    Target = "atk",
                    Frames = [0, 1, 2],
                    X = 56,
                    Y = 14,
                    Width = 4,
                    Height = 4,
                    Color = "#F5EEB2",
                    Note = "smoke spear tip"
                },
                new RsPixelFrameEditOperation
                {
                    Operation = "magenta_key_cleanup",
                    Target = "atk",
                    Frames = [0, 1, 2],
                    Note = "smoke magenta cleanup"
                }
            ]
        });
        if (edit.OperationCount != 5 || edit.ChangedPixelCount <= 0 || !File.Exists(edit.EditLogPath))
        {
            throw new InvalidOperationException("R/S pixel edit operations did not modify the workspace as expected.");
        }

        var sheets = service.ExportContactSheets(new RsPixelContactSheetRequest
        {
            PackageRoot = workspace.PackageRoot,
            Scale = 4,
            Annotate = true
        });
        if (sheets.ContactSheets.Count != 5 || sheets.ContactSheets.Any(path => !File.Exists(path)))
        {
            throw new InvalidOperationException("R/S pixel edit contact sheets were not exported.");
        }

        var validation = service.Validate(project, new RsPixelEditValidationRequest
        {
            PackageRoot = workspace.PackageRoot
        });
        if (!validation.MaterialValidation.FormatPassed ||
            validation.FrameChecks.Count != 68 ||
            validation.FrameChecks.Any(check => !check.NonEmpty) ||
            !File.Exists(validation.ReportPath))
        {
            throw new InvalidOperationException("R/S pixel edit workspace validation failed.");
        }

        Console.WriteLine($"RS_PIXEL_EDIT_WORKSPACE_SMOKE OK root={workspace.PackageRoot} changed={edit.ChangedPixelCount} sheets={sheets.ContactSheets.Count}");
    }

    private static void CreateSmokeBmp(string path, int width, int height, Color primary, Color secondary)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Magenta);
        using var primaryBrush = new SolidBrush(primary);
        using var secondaryBrush = new SolidBrush(secondary);
        using var darkBrush = new SolidBrush(Color.FromArgb(255, 20, 20, 20));
        var frameHeight = width == 64 ? 64 : width == 48 && height == 1280 ? 64 : 48;
        for (var y = 0; y < height; y += frameHeight)
        {
            graphics.FillRectangle(primaryBrush, Math.Max(2, width / 5), y + frameHeight / 3, Math.Max(6, width / 3), Math.Max(8, frameHeight / 2));
            graphics.FillEllipse(secondaryBrush, Math.Max(3, width / 3), y + 6, Math.Max(8, width / 4), Math.Max(8, frameHeight / 4));
            graphics.FillRectangle(darkBrush, Math.Max(1, width / 5), y + frameHeight - 8, Math.Max(10, width / 2), 3);
        }
        bitmap.Save(path, ImageFormat.Bmp);
    }

    private static void CreateDesignStub(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(255, 30, 30, 30));
        using var gold = new SolidBrush(Color.FromArgb(255, 220, 180, 60));
        using var red = new SolidBrush(Color.FromArgb(255, 120, 30, 30));
        graphics.FillRectangle(gold, 48, 16, 32, 48);
        graphics.FillRectangle(red, 38, 56, 52, 54);
        bitmap.Save(path, ImageFormat.Png);
    }
}
