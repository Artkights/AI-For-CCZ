using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Text.Json;

internal static class ProgramLuBuColorfulEnhanceUtility
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Run(string[] args)
    {
        var detector = new ProjectDetector();
        var gameRoot = ReadOptionalArg(args, "--game-root") ??
                       Environment.GetEnvironmentVariable("CCZMODSTUDIO_GAME_ROOT") ??
                       Path.Combine(Environment.CurrentDirectory, "基底", "加强版6.5未加密版");
        var project = detector.CreateProjectFromGameRoot(gameRoot);
        var packageRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "RS_PixelDesign", "LuBu_ColorfulEnhance_v1");
        var originalRoot = Path.Combine(packageRoot, "refs", "original_export");
        var reportsRoot = Path.Combine(packageRoot, "reports");
        Directory.CreateDirectory(reportsRoot);

        var exportService = new BmpImageExportService();
        var rResult = exportService.Export(project, new BmpExportRequest
        {
            Kind = BmpExportKind.RImage,
            OutputRoot = originalRoot,
            SingleMode = true,
            OverwriteExisting = true,
            Targets =
            [
                new BmpExportTarget { RowId = 119, DisplayName = "吕布", FieldValue = 119 }
            ]
        });
        var sResult = exportService.Export(project, new BmpExportRequest
        {
            Kind = BmpExportKind.SImage,
            OutputRoot = originalRoot,
            SingleMode = true,
            OverwriteExisting = true,
            Targets =
            [
                new BmpExportTarget { RowId = 119, DisplayName = "吕布", FieldValue = 73 }
            ]
        });

        File.WriteAllText(Path.Combine(reportsRoot, "original_export_r_result.json"), JsonSerializer.Serialize(rResult, JsonOptions));
        File.WriteAllText(Path.Combine(reportsRoot, "original_export_s_result.json"), JsonSerializer.Serialize(sResult, JsonOptions));

        var designPath = ResolveDesignPath(project.WorkspaceRoot);
        var workspace = new RsPixelEditWorkspaceService();
        var workspaceResult = workspace.CreateWorkspace(project, new RsPixelEditWorkspaceRequest
        {
            PackageId = "LuBu_ColorfulEnhance_v1",
            DisplayName = "吕布",
            UnitType = "mounted_warrior",
            DesignImagePath = designPath,
            FormatReferenceRoot = originalRoot,
            OverwriteExisting = true
        });
        File.WriteAllText(Path.Combine(reportsRoot, "workspace_create_result.json"), JsonSerializer.Serialize(workspaceResult, JsonOptions));

        var contactResult = workspace.ExportContactSheets(new RsPixelContactSheetRequest
        {
            PackageRoot = packageRoot,
            Scale = 6,
            Annotate = false
        });
        File.WriteAllText(Path.Combine(reportsRoot, "original_contact_sheet_result.json"), JsonSerializer.Serialize(contactResult, JsonOptions));
        CopyContactSheets(packageRoot, "original");

        var editResult = workspace.ApplyEdits(new RsPixelFrameEditBatchRequest
        {
            PackageRoot = packageRoot,
            Operations = BuildEnhanceOperations()
        });
        File.WriteAllText(Path.Combine(reportsRoot, "semantic_edit_report.json"), JsonSerializer.Serialize(editResult, JsonOptions));

        var enhancedContactResult = workspace.ExportContactSheets(new RsPixelContactSheetRequest
        {
            PackageRoot = packageRoot,
            Scale = 6,
            Annotate = false
        });
        File.WriteAllText(Path.Combine(reportsRoot, "enhanced_contact_sheet_result.json"), JsonSerializer.Serialize(enhancedContactResult, JsonOptions));
        CopyContactSheets(packageRoot, "enhanced");

        var manifest = new
        {
            Character = "吕布",
            PersonId = 119,
            RImageId = 119,
            SImageId = 73,
            BaseGameRoot = project.GameRoot,
            OutputPackage = packageRoot,
            ExportMode = "local_cczmodstudio_services",
            StandardImageExportFix = "BmpImageExportService.ExportRawEntry decodes BMP/JPG/PNG entries before RAW fallback.",
            EditMode = "controlled_local_pixel_enhancement",
            EditScope = "weapon edge/tip highlights, armor highlights, attack/special colorful glints only",
            EditChangedPixels = editResult.ChangedPixelCount,
            RFiles = rResult.Files.Select(ToFileInfo).ToArray(),
            SFiles = sResult.Files.Select(ToFileInfo).ToArray(),
            SafetyNote = "Only exported BMP materials/contact sheets/reports. No game resources were modified."
        };
        File.WriteAllText(Path.Combine(reportsRoot, "original_manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        File.WriteAllText(Path.Combine(reportsRoot, "visual_acceptance_report.md"),
            "# LuBu Colorful Enhance v1\n\n" +
            "- Scope: controlled local pixel edits on original Lu Bu R=119/S=73 only.\n" +
            "- Changed layers: weapon highlights, armor highlights, attack/special colorful glints.\n" +
            "- Not changed: silhouette, frame order, motion grammar, face, horse/body structure, game resources.\n" +
            "- Review files: drafts/contact_sheet_*_original_x6.png and drafts/contact_sheet_*_enhanced_x6.png.\n");

        Console.WriteLine("LUBU_COLORFUL_EXPORT_OK");
        Console.WriteLine(packageRoot);
    }

    private static IReadOnlyList<RsPixelFrameEditOperation> BuildEnhanceOperations()
        =>
        [
            new()
            {
                Operation = "paint_pixel_runs",
                Target = "front",
                Frames = [0, 1, 2, 6, 7, 12, 13, 18, 19],
                Runs =
                [
                    Run(22, 25, 4, "#8A3DFF"), Run(27, 25, 3, "#3F6DFF"),
                    Run(19, 29, 5, "#F2D36B"), Run(29, 29, 5, "#E4A339"),
                    Run(16, 19, 3, "#F8F0B8"), Run(31, 19, 3, "#B8E8FF")
                ],
                Note = "R front controlled colorful armor highlights on chest, belt, shoulder."
            },
            new()
            {
                Operation = "paint_pixel_runs",
                Target = "back",
                Frames = [0, 1, 2, 5, 7, 8, 10, 11, 16, 17],
                Runs =
                [
                    Run(17, 22, 4, "#F2D36B"), Run(28, 22, 4, "#E4A339"),
                    Run(21, 31, 5, "#8A3DFF"), Run(27, 31, 4, "#3F6DFF")
                ],
                Note = "R back restrained armor and cape-edge glow."
            },
            new()
            {
                Operation = "paint_pixel_runs",
                Target = "front",
                Frames = [15, 16, 18],
                Runs =
                [
                    Run(7, 50, 6, "#B8E8FF"), Run(8, 51, 4, "#F8F0B8"), Run(32, 46, 7, "#F2D36B")
                ],
                Note = "R front weapon tip and polearm edge highlights only."
            },
            new()
            {
                Operation = "draw_polyline",
                Target = "atk",
                Frames = [1, 2, 5, 6, 9, 10],
                Points = [Point(20, 11), Point(34, 6, "#8A3DFF"), Point(48, 8, "#B8E8FF")],
                Color = "#8A3DFF",
                Note = "Subtle purple-blue edge following existing weapon arc top side."
            },
            new()
            {
                Operation = "draw_polyline",
                Target = "atk",
                Frames = [2, 5, 6, 9, 10],
                Points = [Point(18, 50), Point(31, 56, "#FF8A2A"), Point(47, 54, "#FFD84D")],
                Color = "#FF8A2A",
                Note = "Small gold-orange lower impact sparks following original attack motion."
            },
            new()
            {
                Operation = "draw_polyline",
                Target = "spc",
                Frames = [0, 2, 4],
                Points = [Point(8, 9), Point(25, 4, "#8A3DFF"), Point(41, 9, "#B8E8FF")],
                Color = "#8A3DFF",
                Note = "Special frame weapon-top blue-purple light edge."
            },
            new()
            {
                Operation = "paint_pixel_runs",
                Target = "spc",
                Frames = [2, 3],
                Runs =
                [
                    Run(31, 25, 5, "#FFD84D"), Run(35, 26, 3, "#FF8A2A"),
                    Run(25, 19, 4, "#B8E8FF"), Run(26, 20, 3, "#F8F0B8")
                ],
                Note = "Special restrained sparks and blue-white hit glints."
            },
            Magenta("front", 20),
            Magenta("back", 20),
            Magenta("mov", 11),
            Magenta("atk", 12),
            Magenta("spc", 5)
        ];

    private static RsPixelRun Run(int x, int y, int length, string color)
        => new() { X = x, Y = y, Length = length, Color = color };

    private static RsPixelPoint Point(int x, int y, string color = "")
        => new() { X = x, Y = y, Color = color };

    private static RsPixelFrameEditOperation Magenta(string target, int count)
        => new()
        {
            Operation = "magenta_key_cleanup",
            Target = target,
            Frames = Enumerable.Range(0, count).ToArray(),
            Note = $"Normalize {target} background after small edits."
        };

    private static void CopyContactSheets(string packageRoot, string suffix)
    {
        var drafts = Path.Combine(packageRoot, "drafts");
        foreach (var role in new[] { "front", "back", "mov", "atk", "spc" })
        {
            var source = Path.Combine(drafts, $"contact_sheet_{role}_x6.png");
            var destination = Path.Combine(drafts, $"contact_sheet_{role}_{suffix}_x6.png");
            if (File.Exists(source)) File.Copy(source, destination, overwrite: true);
        }
    }

    private static object ToFileInfo(BmpExportedFile file)
        => new
        {
            file.Role,
            file.SourceRelativePath,
            file.ImageNumber,
            file.Width,
            file.Height,
            file.OutputPath,
            Sha256 = File.Exists(file.OutputPath) ? WriteOperationReportService.ComputeSha256(file.OutputPath) : string.Empty,
            Readable = File.Exists(file.OutputPath) && HasReadableForeground(file.OutputPath)
        };

    private static bool HasReadableForeground(string path)
    {
        using var bitmap = new Bitmap(path);
        var nonMagenta = 0;
        var colors = new HashSet<int>();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.R == 255 && color.G == 0 && color.B == 255) continue;
                nonMagenta++;
                colors.Add(color.ToArgb());
                if (nonMagenta > 100 && colors.Count > 8) return true;
            }
        }

        return false;
    }

    private static string ResolveDesignPath(string workspaceRoot)
    {
        var candidates = new[]
        {
            Path.Combine(workspaceRoot, "吕布.png"),
            Path.Combine(workspaceRoot, "孙策.png")
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return candidates[1];
    }

    private static string? ReadOptionalArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
