using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RsPixelEditWorkspaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Color MagentaKey = Color.FromArgb(255, 255, 0, 255);
    private static readonly Color SkinBase = Color.FromArgb(255, 210, 158, 104);
    private static readonly Color SkinShadow = Color.FromArgb(255, 146, 91, 55);
    private static readonly Color ArmorGold = Color.FromArgb(255, 222, 178, 72);
    private static readonly Color ArmorDark = Color.FromArgb(255, 35, 32, 30);
    private static readonly Color CapeRed = Color.FromArgb(255, 142, 24, 31);
    private static readonly Color SpearWood = Color.FromArgb(255, 112, 75, 42);
    private static readonly Color SpearTip = Color.FromArgb(255, 245, 238, 178);
    private static readonly Color SpearGlow = Color.FromArgb(255, 255, 218, 84);

    private static readonly RsPixelStripSpec[] StripSpecs =
    [
        new("front", "r_actor", "front.bmp", 48, 1280, 48, 64),
        new("back", "r_actor", "back.bmp", 48, 1280, 48, 64),
        new("mov", "s_unit", "mov.bmp", 48, 528, 48, 48),
        new("atk", "s_unit", "atk.bmp", 64, 768, 64, 64),
        new("spc", "s_unit", "spc.bmp", 48, 240, 48, 48)
    ];

    private readonly RsPixelMaterialValidationService _materialValidation = new();

    public RsPixelEditWorkspaceResult CreateWorkspace(CczProject project, RsPixelEditWorkspaceRequest request)
    {
        var packageId = NormalizePackageId(request.PackageId);
        var packageRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "RS_PixelDesign", packageId);
        if (Directory.Exists(packageRoot) && !request.OverwriteExisting)
        {
            var hasFiles = Directory.EnumerateFileSystemEntries(packageRoot).Any();
            if (hasFiles)
            {
                throw new InvalidOperationException($"R/S pixel edit workspace already exists: {packageRoot}. Set overwrite_existing=true to rebuild it.");
            }
        }

        var warnings = new List<string>();
        var refsDesign = Path.Combine(packageRoot, "refs", "design");
        var refsFormat = Path.Combine(packageRoot, "refs", "selected_format");
        var workspaceStrips = Path.Combine(packageRoot, "workspace", "strips");
        var workspaceLayers = Path.Combine(packageRoot, "workspace", "layers");
        var materialsR = Path.Combine(packageRoot, "materials", "r_actor");
        var materialsS = Path.Combine(packageRoot, "materials", "s_unit");
        var drafts = Path.Combine(packageRoot, "drafts");
        var reports = Path.Combine(packageRoot, "reports");
        Directory.CreateDirectory(refsDesign);
        Directory.CreateDirectory(refsFormat);
        Directory.CreateDirectory(workspaceStrips);
        Directory.CreateDirectory(workspaceLayers);
        Directory.CreateDirectory(materialsR);
        Directory.CreateDirectory(materialsS);
        Directory.CreateDirectory(drafts);
        Directory.CreateDirectory(reports);

        var designPath = ResolveExistingFile(project, request.DesignImagePath);
        var designCopy = Path.Combine(refsDesign, "sunce_design" + Path.GetExtension(designPath));
        File.Copy(designPath, designCopy, overwrite: true);

        var formatRoot = ResolveExistingDirectory(project, request.FormatReferenceRoot);
        var materialFiles = new List<RsPixelEditMaterialFile>();
        foreach (var spec in StripSpecs)
        {
            var source = ResolveStripSource(formatRoot, spec);
            if (string.IsNullOrWhiteSpace(source))
            {
                warnings.Add($"{spec.FileName} was not found under format reference root: {formatRoot}");
                continue;
            }

            var refsCopy = Path.Combine(refsFormat, spec.FileName);
            var workspaceCopy = Path.Combine(workspaceStrips, spec.FileName);
            var materialCopy = Path.Combine(spec.Group == "r_actor" ? materialsR : materialsS, spec.FileName);
            CopyNormalizedBmp(source, refsCopy, spec);
            CopyNormalizedBmp(source, workspaceCopy, spec);
            CopyNormalizedBmp(source, materialCopy, spec);
            materialFiles.Add(BuildMaterialFile(spec, source, workspaceCopy, materialCopy));
        }

        var manifest = new
        {
            SchemaVersion = 1,
            PackageId = packageId,
            request.DisplayName,
            UnitType = string.IsNullOrWhiteSpace(request.UnitType) ? "spear_cavalry" : request.UnitType,
            SourceMode = "local_mcp_pixel_editor",
            UsesRetroDiffusion = false,
            UsesImageStudio = false,
            UsesSystemImageGen = false,
            Design = new { SourcePath = designPath, CopyPath = designCopy, Sha256 = WriteOperationReportService.ComputeSha256(designCopy) },
            FormatReferenceRoot = formatRoot,
            MaterialFiles = materialFiles,
            SafetyNote = "Local CCZModStudio MCP pixel edit workspace only. No external image generation is used."
        };
        var manifestPath = Path.Combine(reports, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

        var metadataPath = Path.Combine(reports, "frame_metadata.json");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(BuildFrameMetadata(materialFiles), JsonOptions));

        return new RsPixelEditWorkspaceResult
        {
            PackageId = packageId,
            PackageRoot = packageRoot,
            WorkspaceRoot = Path.Combine(packageRoot, "workspace"),
            MaterialsRoot = Path.Combine(packageRoot, "materials"),
            MaterialFiles = materialFiles,
            Reports = [manifestPath, metadataPath],
            Warnings = warnings,
            SafetyNote = "Workspace creation copies local design/reference/material BMPs only; it does not write game resources."
        };
    }

    public RsPixelEditPlanResult BuildPlan(RsPixelEditPlanRequest request)
    {
        var packageRoot = ResolveExistingDirectory(null, request.PackageRoot);
        var unitType = string.IsNullOrWhiteSpace(request.UnitType) ? "spear_cavalry" : request.UnitType.Trim();
        var ops = new List<RsPixelFrameEditOperation>
        {
            new()
            {
                Operation = "clean_face_box",
                Target = "front",
                Frames = Enumerable.Range(0, 20).ToArray(),
                X = 17,
                Y = 10,
                Width = 14,
                Height = 13,
                Note = "Keep face readable; remove dark-red pollution from R front frames."
            },
            new()
            {
                Operation = "recolor_palette",
                Target = "front",
                Frames = Enumerable.Range(0, 20).ToArray(),
                X = 9,
                Y = 22,
                Width = 30,
                Height = 30,
                Color = "#231F1E",
                SecondaryColor = "#DEB248",
                Note = "Black-gold armor block pass."
            },
            new()
            {
                Operation = "draw_spear_axis",
                Target = "atk",
                Frames = Enumerable.Range(0, 12).ToArray(),
                X = 12,
                Y = 38,
                X2 = 56,
                Y2 = 18,
                Color = "#704B2A",
                Note = "Single long spear axis across attack frames."
            },
            new()
            {
                Operation = "draw_spear_tip",
                Target = "atk",
                Frames = Enumerable.Range(0, 12).ToArray(),
                X = 53,
                Y = 16,
                Width = 5,
                Height = 5,
                Color = "#F5EEB2",
                Note = "Bright spear tip, one weapon only."
            },
            new()
            {
                Operation = "draw_spear_effect",
                Target = "atk",
                Frames = [3, 4, 8, 9, 10],
                X = 44,
                Y = 12,
                X2 = 62,
                Y2 = 8,
                Color = "#FFDA54",
                SecondaryColor = "#FFFFFF",
                Note = "Gold-white spear glow around the spear tip."
            },
            new()
            {
                Operation = "magenta_key_cleanup",
                Target = "atk",
                Frames = Enumerable.Range(0, 12).ToArray(),
                Note = "Normalize background to strict #FF00FF."
            }
        };

        var plan = new
        {
            UnitType = unitType,
            request.CharacterBrief,
            request.WeaponBrief,
            RecommendedOperations = ops,
            ReviewGates = BuildReviewGates(unitType),
            SafetyNote = "Plan is deterministic local pixel-edit recipe. It does not call any image generation provider."
        };
        var path = Path.Combine(packageRoot, "reports", "local_pixel_edit_plan.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(plan, JsonOptions));

        return new RsPixelEditPlanResult
        {
            PackageRoot = packageRoot,
            PlanPath = path,
            RecommendedOperations = ops,
            ReviewGates = BuildReviewGates(unitType),
            SafetyNote = "Use apply_rs_pixel_frame_edits to execute selected operations; inspect contact sheets after each pass."
        };
    }

    public RsPixelFrameEditBatchResult ApplyEdits(RsPixelFrameEditBatchRequest request)
    {
        var packageRoot = ResolveExistingDirectory(null, request.PackageRoot);
        if (request.Operations.Count == 0) throw new InvalidOperationException("operations must contain at least one frame edit operation.");

        var warnings = new List<string>();
        var entries = new List<RsPixelFrameEditLogEntry>();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalChanged = 0;

        foreach (var group in request.Operations.GroupBy(operation => NormalizeTarget(operation.Target)))
        {
            var spec = ResolveSpec(group.Key);
            var path = ResolveMaterialPath(packageRoot, spec);
            using var bitmap = LoadBitmap(path);
            foreach (var operation in group)
            {
                var frames = ResolveFrames(operation, spec);
                var before = CountNonMagenta(bitmap);
                var changed = ApplyOperation(bitmap, spec, operation, frames, warnings);
                var after = CountNonMagenta(bitmap);
                totalChanged += changed;
                entries.Add(new RsPixelFrameEditLogEntry
                {
                    Timestamp = DateTimeOffset.Now,
                    Operation = NormalizeOperation(operation.Operation),
                    Target = spec.Role,
                    Frames = frames,
                    X = operation.X,
                    Y = operation.Y,
                    Width = operation.Width,
                    Height = operation.Height,
                    ChangedPixelCount = changed,
                    Note = string.IsNullOrWhiteSpace(operation.Note)
                        ? $"nonMagenta {before.ToString(CultureInfo.InvariantCulture)}->{after.ToString(CultureInfo.InvariantCulture)}"
                        : operation.Note
                });
            }

            SaveBmp(bitmap, path);
            var workspacePath = Path.Combine(packageRoot, "workspace", "strips", spec.FileName);
            SaveBmp(bitmap, workspacePath);
            written.Add(path);
            written.Add(workspacePath);
        }

        var logPath = Path.Combine(packageRoot, "reports", "edit_log.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllLines(logPath, entries.Select(entry => JsonSerializer.Serialize(entry, JsonOptions)));
        return new RsPixelFrameEditBatchResult
        {
            PackageRoot = packageRoot,
            OperationCount = request.Operations.Count,
            ChangedPixelCount = totalChanged,
            EditLogEntries = entries,
            WrittenFiles = written.ToArray(),
            EditLogPath = logPath,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    public RsPixelContactSheetResult ExportContactSheets(RsPixelContactSheetRequest request)
    {
        var packageRoot = ResolveExistingDirectory(null, request.PackageRoot);
        var scale = Math.Clamp(request.Scale, 1, 12);
        var outputRoot = Path.Combine(packageRoot, "drafts");
        Directory.CreateDirectory(outputRoot);
        var outputs = new List<string>();
        foreach (var spec in StripSpecs)
        {
            var path = ResolveMaterialPath(packageRoot, spec);
            using var bitmap = LoadBitmap(path);
            using var sheet = BuildContactSheet(bitmap, spec, scale, request.Annotate);
            var output = Path.Combine(outputRoot, $"contact_sheet_{spec.Role}_x{scale.ToString(CultureInfo.InvariantCulture)}.png");
            sheet.Save(output, ImageFormat.Png);
            outputs.Add(output);
        }

        var reportPath = Path.Combine(packageRoot, "reports", "contact_sheet_report.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Scale = scale, Annotated = request.Annotate, ContactSheets = outputs }, JsonOptions));
        return new RsPixelContactSheetResult
        {
            PackageRoot = packageRoot,
            Scale = scale,
            ContactSheets = outputs,
            ReportPath = reportPath
        };
    }

    public RsPixelEditValidationResult Validate(CczProject project, RsPixelEditValidationRequest request)
    {
        var packageRoot = ResolveExistingDirectory(project, request.PackageRoot);
        var materialValidation = _materialValidation.Validate(project, new RsPixelMaterialValidationRequest
        {
            MaterialRoot = packageRoot,
            RImageId = request.RImageId,
            SImageId = request.SImageId,
            JobId = request.JobId,
            FactionSlot = request.FactionSlot
        });
        var frameChecks = BuildFrameChecks(packageRoot);
        var singleSpearRisks = DetectSingleSpearRisks(packageRoot);
        var faceRisks = DetectFaceRisks(packageRoot);
        var warnings = new List<string>();
        if (singleSpearRisks.Count > 0) warnings.Add("single spear risk frames detected.");
        if (faceRisks.Count > 0) warnings.Add("face safe-box risk frames detected.");
        if (frameChecks.Any(check => !check.NonEmpty)) warnings.Add("empty frame detected.");

        var passed = materialValidation.FormatPassed &&
                     materialValidation.PreviewPassed &&
                     materialValidation.Errors.Count == 0 &&
                     frameChecks.All(check => check.NonEmpty) &&
                     singleSpearRisks.Count == 0 &&
                     faceRisks.Count == 0;
        var reportPath = Path.Combine(packageRoot, "reports", "local_pixel_edit_validation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var result = new RsPixelEditValidationResult
        {
            PackageRoot = packageRoot,
            MaterialValidation = materialValidation,
            FrameChecks = frameChecks,
            SingleSpearRiskFrames = singleSpearRisks,
            FaceRiskFrames = faceRisks,
            LocalPixelEditPassed = passed,
            ReportPath = reportPath,
            Warnings = warnings
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(result, JsonOptions));
        return result;
    }

    private static IReadOnlyList<string> BuildReviewGates(string unitType)
    {
        var gates = new List<string>
        {
            "Original-size visual review must look like CCZ 6.5 R/S, not modern pixel sticker art.",
            "No local script or external image-generation fallback may replace manual/MCP pixel edits.",
            "Face safe boxes must be clean and readable.",
            "Magenta key must remain strict background, not interior actor color."
        };
        if (unitType.Equals("spear_cavalry", StringComparison.OrdinalIgnoreCase))
        {
            gates.Add("All key frames must read as mounted single-spear cavalry: one spear axis, one tip, no sword or second spear.");
        }

        return gates;
    }

    private static int ApplyOperation(Bitmap bitmap, RsPixelStripSpec spec, RsPixelFrameEditOperation operation, IReadOnlyList<int> frames, List<string> warnings)
    {
        var op = NormalizeOperation(operation.Operation);
        var changed = 0;
        foreach (var frame in frames)
        {
            var frameTop = frame * spec.FrameHeight;
            changed += op switch
            {
                "magenta_key_cleanup" => MagentaKeyCleanup(bitmap, spec, frameTop),
                "clean_face_box" => FillFaceBox(bitmap, spec, frameTop, operation),
                "erase_weapon_residue" => EraseWeaponResidue(bitmap, spec, frameTop, operation),
                "erase_effect_residue" => EraseEffectResidue(bitmap, spec, frameTop, operation),
                "erase_rect_to_magenta" => FillRect(bitmap, spec, frameTop, operation, MagentaKey, allowMagenta: true),
                "recolor_palette" => RecolorPalette(bitmap, spec, frameTop, operation),
                "tint_region_by_luminance" => TintRegionByLuminance(bitmap, spec, frameTop, operation),
                "repaint_armor_blocks" => FillRect(bitmap, spec, frameTop, operation, ParseColor(operation.Color, ArmorDark)),
                "repaint_cape_blocks" => FillRect(bitmap, spec, frameTop, operation, ParseColor(operation.Color, CapeRed)),
                "draw_spear_axis" => DrawLine(bitmap, spec, frameTop, operation, ParseColor(operation.Color, SpearWood)),
                "draw_spear_tip" => FillRect(bitmap, spec, frameTop, operation, ParseColor(operation.Color, SpearTip)),
                "draw_spear_tip_diamond" => DrawSpearTipDiamond(bitmap, spec, frameTop, operation),
                "draw_spear_effect" => DrawSpearEffect(bitmap, spec, frameTop, operation),
                "draw_polyline" => DrawPolyline(bitmap, spec, frameTop, operation),
                "paint_pixel_runs" => PaintPixelRuns(bitmap, spec, frame, frameTop, operation),
                "paint_region_mask" => PaintRegionMask(bitmap, spec, frameTop, operation),
                "copy_region_from_reference" => CopyRegionWithinFrame(bitmap, spec, frameTop, operation, warnings),
                "copy_region_from_frame" => CopyRegionFromFrame(bitmap, spec, frame, frameTop, operation, warnings),
                _ => throw new InvalidOperationException("Unsupported R/S pixel edit operation: " + operation.Operation)
            };
        }

        return changed;
    }

    private static int PaintPixelRuns(Bitmap bitmap, RsPixelStripSpec spec, int frame, int frameTop, RsPixelFrameEditOperation operation)
    {
        if (operation.Runs.Count == 0 && operation.Points.Count == 0)
        {
            throw new InvalidOperationException("paint_pixel_runs requires at least one run or point.");
        }

        var changed = 0;
        foreach (var run in operation.Runs)
        {
            if (run.Frame >= 0 && run.Frame != frame) continue;
            var y = Math.Clamp(run.Y, 0, spec.FrameHeight - 1);
            var x0 = Math.Clamp(run.X, 0, spec.FrameWidth - 1);
            var length = Math.Clamp(run.Length <= 0 ? 1 : run.Length, 1, spec.FrameWidth - x0);
            var color = ParseColor(string.IsNullOrWhiteSpace(run.Color) ? operation.Color : run.Color, ArmorGold);
            if (IsMagenta(color)) throw new InvalidOperationException("paint_pixel_runs cannot paint magenta; use erase_rect_to_magenta.");
            for (var dx = 0; dx < length; dx++)
            {
                changed += SetPixelIfChanged(bitmap, x0 + dx, frameTop + y, color);
            }
        }

        foreach (var point in operation.Points)
        {
            if (point.Frame >= 0 && point.Frame != frame) continue;
            var color = ParseColor(string.IsNullOrWhiteSpace(point.Color) ? operation.Color : point.Color, ArmorGold);
            if (IsMagenta(color)) throw new InvalidOperationException("paint_pixel_runs cannot paint magenta; use erase_rect_to_magenta.");
            changed += SetPixelIfChanged(bitmap, Math.Clamp(point.X, 0, spec.FrameWidth - 1), frameTop + Math.Clamp(point.Y, 0, spec.FrameHeight - 1), color);
        }

        return changed;
    }

    private static int PaintRegionMask(Bitmap bitmap, RsPixelStripSpec spec, int frameTop, RsPixelFrameEditOperation operation)
    {
        if (operation.Mask.Count == 0) throw new InvalidOperationException("paint_region_mask requires mask rows.");
        var originX = Math.Clamp(operation.X, 0, spec.FrameWidth - 1);
        var originY = Math.Clamp(operation.Y, 0, spec.FrameHeight - 1);
        var palette = ParseMaskPalette(operation);
        var changed = 0;
        for (var row = 0; row < operation.Mask.Count; row++)
        {
            var y = originY + row;
            if (y >= spec.FrameHeight) break;
            var line = operation.Mask[row] ?? string.Empty;
            for (var col = 0; col < line.Length; col++)
            {
                var x = originX + col;
                if (x >= spec.FrameWidth) break;
                var key = line[col];
                if (key is '.' or ' ' or '_') continue;
                if (!palette.TryGetValue(key, out var color)) continue;
                if (IsMagenta(color)) throw new InvalidOperationException("paint_region_mask cannot paint magenta; use erase_rect_to_magenta.");
                changed += SetPixelIfChanged(bitmap, x, frameTop + y, color);
            }
        }

        return changed;
    }

    private static int DrawPolyline(Bitmap bitmap, RsPixelStripSpec spec, int frameTop, RsPixelFrameEditOperation operation)
    {
        var points = operation.Points;
        if (points.Count < 2)
        {
            return DrawLine(bitmap, spec, frameTop, operation, ParseColor(operation.Color, SpearWood));
        }

        var changed = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var previous = points[i - 1];
            var current = points[i];
            var color = ParseColor(string.IsNullOrWhiteSpace(current.Color) ? operation.Color : current.Color, SpearWood);
            changed += DrawLine(bitmap, spec, frameTop, new RsPixelFrameEditOperation
            {
                X = previous.X,
                Y = previous.Y,
                X2 = current.X,
                Y2 = current.Y,
                Color = current.Color
            }, color);
        }

        return changed;
    }

    private static int FillFaceBox(Bitmap bitmap, RsPixelStripSpec spec, int frameTop, RsPixelFrameEditOperation operation)
    {
        var rect = ResolveRect(spec, operation, defaultWidth: 12, defaultHeight: 12);
        var changed = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var color = ((x + y) % 5 == 0) ? SkinShadow : SkinBase;
                changed += SetPixelIfChanged(bitmap, x, frameTop + y, color);
            }
        }

        var eyeY = Math.Clamp(rect.Top + rect.Height / 3, rect.Top, rect.Bottom - 1);
        changed += SetPixelIfChanged(bitmap, Math.Clamp(rect.Left + rect.Width / 3, rect.Left, rect.Right - 1), frameTop + eyeY, Color.Black);
        changed += SetPixelIfChanged(bitmap, Math.Clamp(rect.Right - rect.Width / 3, rect.Left, rect.Right - 1), frameTop + eyeY, Color.Black);
        return changed;
    }

    private static int RecolorPalette(Bitmap bitmap, RsPixelStripSpec spec, int frameTop, RsPixelFrameEditOperation operation)
    {
        var rect = ResolveRect(spec, operation, defaultWidth: spec.FrameWidth, defaultHeight: spec.FrameHeight);
        var primary = ParseColor(operation.Color, ArmorDark);
        var secondary = ParseColor(operation.SecondaryColor, ArmorGold);
        var changed = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var existing = bitmap.GetPixel(x, frameTop + y);
                if (IsMagenta(existing)) continue;
                var replacement = ((x + y) % 4 == 0) ? secondary : primary;
                changed += SetPixelIfChanged(bitmap, x, frameTop + y, replacement);
            }
        }

        return changed;
    }

    private static int TintRegionByLuminance(Bitmap bitmap, RsPixelStripSpec spec, int frameTop, RsPixelFrameEditOperation operation)
    {
        var rect = ResolveRect(spec, operation, defaultWidth: spec.FrameWidth, defaultHeight: spec.FrameHeight);
        var shadow = ParseColor(operation.Color, ArmorDark);
        var highlight = ParseColor(operation.SecondaryColor, ArmorGold);
        var mid = Blend(shadow, highlight, 0.45);
        var changed = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var existing = bitmap.GetPixel(x, frameTop + y);
                if (IsMagenta(existing)) continue;

                var brightness = Luminance(existing);
                if (brightness < 35)
                {
                    continue; // Keep native black outlines.
                }

                var replacement = brightness switch
                {
                    >= 150 => highlight,
                    >= 85 => mid,
                    _ => shadow
                };
                changed += SetPixelIfChanged(bitmap, x, frameTop + y, replacement);
            }
        }

        return changed;
    }

    private static int FillRect(Bitmap bitmap, RsPixelStripSpec spec, int frameTop, RsPixelFrameEditOperation operation, Color color, bool allowMagenta = false)
    {
        if (!allowMagenta && IsMagenta(color)) throw new InvalidOperationException("Actor paint operations cannot use magenta; use magenta_key_cleanup or erase_weapon_residue.");
        var rect = ResolveRect(spec, operation, defaultWidth: 1, defaultHeight: 1);
        var changed = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                changed += SetPixelIfChanged(bitmap, x, frameTop + y, color);
            }
        }

        return changed;
    }

    private static int EraseWeaponResidue(Bitmap bitmap, RsPixelStripSpec spec, int frameTop, RsPixelFrameEditOperation operation)
    {
        var rect = ResolveRect(spec, operation, defaultWidth: 1, defaultHeight: 1);
        var changed = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var existing = bitmap.GetPixel(x, frameTop + y);
                if (!IsWeaponBright(existing)) continue;
                changed += SetPixelIfChanged(bitmap, x, frameTop + y, MagentaKey);
            }
        }

        return changed;
    }

    private static int EraseEffectResidue(Bitmap bitmap, RsPixelStripSpec spec, int frameTop, RsPixelFrameEditOperation operation)
    {
        var rect = ResolveRect(spec, operation, defaultWidth: 1, defaultHeight: 1);
        var changed = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var existing = bitmap.GetPixel(x, frameTop + y);
                if (!IsEffectResidue(existing)) continue;
                changed += SetPixelIfChanged(bitmap, x, frameTop + y, MagentaKey);
            }
        }

        return changed;
    }

    private static int DrawLine(Bitmap bitmap, RsPixelStripSpec spec, int frameTop, RsPixelFrameEditOperation operation, Color color)
    {
        var x0 = Math.Clamp(operation.X, 0, spec.FrameWidth - 1);
        var y0 = Math.Clamp(operation.Y, 0, spec.FrameHeight - 1);
        var x1 = Math.Clamp(operation.X2 == 0 && operation.Y2 == 0 ? operation.X + 20 : operation.X2, 0, spec.FrameWidth - 1);
        var y1 = Math.Clamp(operation.X2 == 0 && operation.Y2 == 0 ? operation.Y - 12 : operation.Y2, 0, spec.FrameHeight - 1);
        var changed = 0;
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            changed += SetPixelIfChanged(bitmap, x0, frameTop + y0, color);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }

        return changed;
    }

    private static int DrawSpearEffect(Bitmap bitmap, RsPixelStripSpec spec, int frameTop, RsPixelFrameEditOperation operation)
    {
        var primary = ParseColor(operation.Color, SpearGlow);
        var secondary = ParseColor(operation.SecondaryColor, Color.White);
        var changed = DrawLine(bitmap, spec, frameTop, operation, primary);
        changed += SetPixelIfChanged(bitmap, Math.Clamp(operation.X2, 0, spec.FrameWidth - 1), frameTop + Math.Clamp(operation.Y2, 0, spec.FrameHeight - 1), secondary);
        changed += SetPixelIfChanged(bitmap, Math.Clamp(operation.X2 - 1, 0, spec.FrameWidth - 1), frameTop + Math.Clamp(operation.Y2 + 1, 0, spec.FrameHeight - 1), secondary);
        return changed;
    }

    private static int DrawSpearTipDiamond(Bitmap bitmap, RsPixelStripSpec spec, int frameTop, RsPixelFrameEditOperation operation)
    {
        var centerX = Math.Clamp(operation.X, 0, spec.FrameWidth - 1);
        var centerY = Math.Clamp(operation.Y, 0, spec.FrameHeight - 1);
        var radius = Math.Clamp(operation.Width <= 0 ? 1 : operation.Width, 1, 2);
        var fill = ParseColor(operation.Color, SpearTip);
        var edge = ParseColor(operation.SecondaryColor, Color.FromArgb(255, 112, 75, 42));
        var changed = 0;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var distance = Math.Abs(dx) + Math.Abs(dy);
                if (distance > radius) continue;
                var color = distance == radius ? edge : fill;
                changed += SetPixelIfChanged(bitmap, centerX + dx, frameTop + centerY + dy, color);
            }
        }

        changed += SetPixelIfChanged(bitmap, centerX, frameTop + centerY, fill);
        return changed;
    }

    private static int MagentaKeyCleanup(Bitmap bitmap, RsPixelStripSpec spec, int frameTop)
    {
        var changed = 0;
        for (var y = 0; y < spec.FrameHeight; y++)
        {
            for (var x = 0; x < spec.FrameWidth; x++)
            {
                var color = bitmap.GetPixel(x, frameTop + y);
                if (IsNearMagenta(color))
                {
                    changed += SetPixelIfChanged(bitmap, x, frameTop + y, MagentaKey);
                }
            }
        }

        return changed;
    }

    private static int CopyRegionWithinFrame(Bitmap bitmap, RsPixelStripSpec spec, int frameTop, RsPixelFrameEditOperation operation, List<string> warnings)
    {
        var rect = ResolveRect(spec, operation, defaultWidth: 1, defaultHeight: 1);
        var targetX = Math.Clamp(operation.X2, 0, spec.FrameWidth - 1);
        var targetY = Math.Clamp(operation.Y2, 0, spec.FrameHeight - 1);
        var colors = new Color[rect.Width, rect.Height];
        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                colors[x, y] = bitmap.GetPixel(rect.Left + x, frameTop + rect.Top + y);
            }
        }

        var changed = 0;
        for (var y = 0; y < rect.Height; y++)
        {
            var yy = targetY + y;
            if (yy >= spec.FrameHeight) continue;
            for (var x = 0; x < rect.Width; x++)
            {
                var xx = targetX + x;
                if (xx >= spec.FrameWidth) continue;
                changed += SetPixelIfChanged(bitmap, xx, frameTop + yy, colors[x, y]);
            }
        }

        warnings.Add("copy_region_from_reference duplicates local pixels; use only for small controlled details, not fake frame generation.");
        return changed;
    }

    private static int CopyRegionFromFrame(Bitmap bitmap, RsPixelStripSpec spec, int targetFrame, int targetTop, RsPixelFrameEditOperation operation, List<string> warnings)
    {
        var sourceFrame = operation.SourceFrame ?? throw new InvalidOperationException("copy_region_from_frame requires source_frame.");
        if (sourceFrame < 0 || sourceFrame >= spec.FrameCount)
        {
            throw new InvalidOperationException($"source_frame {sourceFrame} is outside {spec.Role} bounds 0..{spec.FrameCount - 1}.");
        }
        if (sourceFrame == targetFrame)
        {
            warnings.Add($"copy_region_from_frame skipped {spec.Role}:{targetFrame} because source_frame equals target frame.");
            return 0;
        }

        var rect = ResolveRect(spec, operation, defaultWidth: spec.FrameWidth, defaultHeight: spec.FrameHeight);
        var targetX = Math.Clamp(operation.X2, 0, spec.FrameWidth - 1);
        var targetY = Math.Clamp(operation.Y2, 0, spec.FrameHeight - 1);
        var sourceTop = sourceFrame * spec.FrameHeight;
        var colors = new Color[rect.Width, rect.Height];
        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                colors[x, y] = bitmap.GetPixel(rect.Left + x, sourceTop + rect.Top + y);
            }
        }

        var changed = 0;
        for (var y = 0; y < rect.Height; y++)
        {
            var yy = targetY + y;
            if (yy >= spec.FrameHeight) continue;
            for (var x = 0; x < rect.Width; x++)
            {
                var xx = targetX + x;
                if (xx >= spec.FrameWidth) continue;
                changed += SetPixelIfChanged(bitmap, xx, targetTop + yy, colors[x, y]);
            }
        }

        warnings.Add($"copy_region_from_frame duplicated {spec.Role}:{sourceFrame} -> {targetFrame}; use only for filling known unused R/S slots from nearby native frames.");
        return changed;
    }

    private static Bitmap BuildContactSheet(Bitmap strip, RsPixelStripSpec spec, int scale, bool annotate)
    {
        var columns = spec.Role == "atk" ? 4 : 5;
        var rows = (int)Math.Ceiling(spec.FrameCount / (double)columns);
        var labelHeight = annotate ? 12 : 0;
        var cellWidth = spec.FrameWidth * scale;
        var cellHeight = spec.FrameHeight * scale + labelHeight;
        var sheet = new Bitmap(columns * cellWidth, rows * cellHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(sheet);
        graphics.Clear(Color.FromArgb(255, 38, 38, 38));
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        using var pen = new Pen(Color.FromArgb(255, 80, 80, 80));
        using var facePen = new Pen(Color.FromArgb(255, 30, 220, 255));
        using var spearPen = new Pen(Color.FromArgb(255, 255, 230, 64));
        using var font = new Font(FontFamily.GenericSansSerif, 8);
        using var textBrush = new SolidBrush(Color.White);
        for (var frame = 0; frame < spec.FrameCount; frame++)
        {
            var col = frame % columns;
            var row = frame / columns;
            var dest = new Rectangle(col * cellWidth, row * cellHeight + labelHeight, spec.FrameWidth * scale, spec.FrameHeight * scale);
            var src = new Rectangle(0, frame * spec.FrameHeight, spec.FrameWidth, spec.FrameHeight);
            graphics.DrawImage(strip, dest, src, GraphicsUnit.Pixel);
            graphics.DrawRectangle(pen, dest);
            if (annotate)
            {
                graphics.DrawString($"{spec.Role} {frame.ToString(CultureInfo.InvariantCulture)}", font, textBrush, col * cellWidth + 2, row * cellHeight);
                var face = GuessFaceBox(spec, frame);
                graphics.DrawRectangle(facePen, dest.Left + face.X * scale, dest.Top + face.Y * scale, face.Width * scale, face.Height * scale);
                if (spec.Role is "atk" or "mov" or "spc")
                {
                    graphics.DrawLine(spearPen, dest.Left + 12 * scale, dest.Top + (spec.FrameHeight - 10) * scale, dest.Left + (spec.FrameWidth - 6) * scale, dest.Top + 14 * scale);
                }
            }
        }

        return sheet;
    }

    private static IReadOnlyList<RsPixelFrameQualityCheck> BuildFrameChecks(string packageRoot)
    {
        var checks = new List<RsPixelFrameQualityCheck>();
        foreach (var spec in StripSpecs)
        {
            using var bitmap = LoadBitmap(ResolveMaterialPath(packageRoot, spec));
            for (var frame = 0; frame < spec.FrameCount; frame++)
            {
                checks.Add(BuildFrameCheck(bitmap, spec, frame));
            }
        }

        return checks;
    }

    private static RsPixelFrameQualityCheck BuildFrameCheck(Bitmap bitmap, RsPixelStripSpec spec, int frame)
    {
        var top = frame * spec.FrameHeight;
        var nonMagenta = 0;
        var strictMagenta = 0;
        var nearNonStrict = 0;
        var minX = spec.FrameWidth;
        var minY = spec.FrameHeight;
        var maxX = -1;
        var maxY = -1;
        unchecked
        {
            var hash = 17;
            for (var y = 0; y < spec.FrameHeight; y++)
            {
                for (var x = 0; x < spec.FrameWidth; x++)
                {
                    var color = bitmap.GetPixel(x, top + y);
                    hash = hash * 31 + color.ToArgb();
                    if (IsStrictMagenta(color))
                    {
                        strictMagenta++;
                        continue;
                    }
                    if (IsNearMagenta(color)) nearNonStrict++;
                    if (!IsMagenta(color))
                    {
                        nonMagenta++;
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            return new RsPixelFrameQualityCheck
            {
                Target = spec.Role,
                FrameIndex = frame,
                NonEmpty = nonMagenta > 0,
                NonMagentaPixelCount = nonMagenta,
                StrictMagentaPixelCount = strictMagenta,
                NearMagentaNonStrictPixelCount = nearNonStrict,
                BoundingBox = nonMagenta == 0 ? "empty" : $"{minX},{minY},{maxX - minX + 1},{maxY - minY + 1}",
                Hash = hash.ToString("X8", CultureInfo.InvariantCulture)
            };
        }
    }

    private static IReadOnlyList<string> DetectSingleSpearRisks(string packageRoot)
    {
        var risks = new List<string>();
        foreach (var spec in StripSpecs.Where(spec => spec.Role is "atk" or "mov" or "spc" or "front" or "back"))
        {
            using var bitmap = LoadBitmap(ResolveMaterialPath(packageRoot, spec));
            for (var frame = 0; frame < spec.FrameCount; frame++)
            {
                var brightClusters = CountBrightClusters(bitmap, spec, frame);
                if (spec.Role == "atk" && brightClusters > 3)
                {
                    risks.Add($"{spec.Role}:{frame.ToString(CultureInfo.InvariantCulture)} brightClusters={brightClusters.ToString(CultureInfo.InvariantCulture)}");
                }
            }
        }

        return risks;
    }

    private static IReadOnlyList<string> DetectFaceRisks(string packageRoot)
    {
        var risks = new List<string>();
        foreach (var spec in StripSpecs.Where(spec => spec.Role is "front" or "mov" or "atk" or "spc"))
        {
            using var bitmap = LoadBitmap(ResolveMaterialPath(packageRoot, spec));
            for (var frame = 0; frame < spec.FrameCount; frame++)
            {
                var face = GuessFaceBox(spec, frame);
                var redPollution = 0;
                var tooDark = 0;
                for (var y = face.Top; y < face.Bottom; y++)
                {
                    for (var x = face.Left; x < face.Right; x++)
                    {
                        var color = bitmap.GetPixel(x, frame * spec.FrameHeight + y);
                        if (IsMagenta(color)) continue;
                        var brightness = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
                        if (color.R > 95 && color.G < 55 && color.B < 55) redPollution++;
                        if (brightness < 42) tooDark++;
                    }
                }
                if (redPollution > 0 || tooDark > face.Width * face.Height / 2)
                {
                    risks.Add($"{spec.Role}:{frame.ToString(CultureInfo.InvariantCulture)} red={redPollution.ToString(CultureInfo.InvariantCulture)} dark={tooDark.ToString(CultureInfo.InvariantCulture)}");
                }
            }
        }

        return risks;
    }

    private static int CountBrightClusters(Bitmap bitmap, RsPixelStripSpec spec, int frame)
    {
        var top = frame * spec.FrameHeight;
        var visited = new bool[spec.FrameWidth, spec.FrameHeight];
        var clusters = 0;
        for (var y = 0; y < spec.FrameHeight; y++)
        {
            for (var x = 0; x < spec.FrameWidth; x++)
            {
                if (visited[x, y]) continue;
                var color = bitmap.GetPixel(x, top + y);
                if (!IsWeaponBright(color))
                {
                    visited[x, y] = true;
                    continue;
                }
                clusters++;
                FloodBright(bitmap, spec, top, x, y, visited);
            }
        }

        return clusters;
    }

    private static void FloodBright(Bitmap bitmap, RsPixelStripSpec spec, int top, int startX, int startY, bool[,] visited)
    {
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((startX, startY));
        visited[startX, startY] = true;
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (nx < 0 || ny < 0 || nx >= spec.FrameWidth || ny >= spec.FrameHeight || visited[nx, ny]) continue;
                visited[nx, ny] = true;
                if (IsWeaponBright(bitmap.GetPixel(nx, top + ny))) queue.Enqueue((nx, ny));
            }
        }
    }

    private static object BuildFrameMetadata(IReadOnlyList<RsPixelEditMaterialFile> files)
        => new
        {
            Files = files.Select(file => new
            {
                file.Role,
                file.MaterialPath,
                file.Width,
                file.Height,
                file.FrameWidth,
                file.FrameHeight,
                file.FrameCount,
                Frames = Enumerable.Range(0, file.FrameCount).Select(frame => new { Frame = frame, X = 0, Y = frame * file.FrameHeight, file.FrameWidth, file.FrameHeight })
            })
        };

    private static RsPixelEditMaterialFile BuildMaterialFile(RsPixelStripSpec spec, string sourcePath, string workspacePath, string materialPath)
    {
        using var bitmap = LoadBitmap(materialPath);
        return new RsPixelEditMaterialFile
        {
            Role = spec.Role,
            SourcePath = sourcePath,
            WorkspacePath = workspacePath,
            MaterialPath = materialPath,
            Width = bitmap.Width,
            Height = bitmap.Height,
            FrameWidth = spec.FrameWidth,
            FrameHeight = spec.FrameHeight,
            FrameCount = spec.FrameCount,
            Sha256 = WriteOperationReportService.ComputeSha256(materialPath)
        };
    }

    private static void CopyNormalizedBmp(string source, string destination, RsPixelStripSpec spec)
    {
        using var bitmap = LoadBitmap(source);
        if (bitmap.Width != spec.Width || bitmap.Height != spec.Height)
        {
            throw new InvalidOperationException($"{Path.GetFileName(source)} size {bitmap.Width}x{bitmap.Height} does not match {spec.Width}x{spec.Height} for {spec.Role}.");
        }
        SaveBmp(bitmap, destination);
    }

    private static string? ResolveStripSource(string root, RsPixelStripSpec spec)
    {
        var candidates = new[]
        {
            Path.Combine(root, spec.FileName),
            Path.Combine(root, spec.Group, spec.FileName),
            Path.Combine(root, "materials", spec.Group, spec.FileName),
            Path.Combine(root, "refs", spec.FileName)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ResolveMaterialPath(string packageRoot, RsPixelStripSpec spec)
    {
        var path = Path.Combine(packageRoot, "materials", spec.Group, spec.FileName);
        if (!File.Exists(path)) throw new FileNotFoundException("R/S material file was not found.", path);
        return path;
    }

    private static IReadOnlyList<int> ResolveFrames(RsPixelFrameEditOperation operation, RsPixelStripSpec spec)
    {
        var frames = operation.Frames.Count == 0 ? Enumerable.Range(0, spec.FrameCount) : operation.Frames;
        var result = frames.Distinct().OrderBy(x => x).ToArray();
        foreach (var frame in result)
        {
            if (frame < 0 || frame >= spec.FrameCount) throw new InvalidOperationException($"Frame {frame} is outside {spec.Role} bounds 0..{spec.FrameCount - 1}.");
        }
        return result;
    }

    private static Rectangle ResolveRect(RsPixelStripSpec spec, RsPixelFrameEditOperation operation, int defaultWidth, int defaultHeight)
    {
        var x = Math.Clamp(operation.X, 0, spec.FrameWidth - 1);
        var y = Math.Clamp(operation.Y, 0, spec.FrameHeight - 1);
        var width = Math.Clamp(operation.Width <= 0 ? defaultWidth : operation.Width, 1, spec.FrameWidth - x);
        var height = Math.Clamp(operation.Height <= 0 ? defaultHeight : operation.Height, 1, spec.FrameHeight - y);
        return new Rectangle(x, y, width, height);
    }

    private static Rectangle GuessFaceBox(RsPixelStripSpec spec, int frame)
    {
        if (spec.Role == "back") return new Rectangle(17, 9, 14, 12);
        if (spec.Role == "atk") return new Rectangle(24, 12, 14, 13);
        if (spec.Role is "mov" or "spc") return new Rectangle(17, 10, 13, 12);
        return new Rectangle(17, 10, 14, 13);
    }

    private static RsPixelStripSpec ResolveSpec(string target)
        => StripSpecs.FirstOrDefault(spec => spec.Role.Equals(target, StringComparison.OrdinalIgnoreCase) || spec.FileName.Equals(target, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException("Unknown R/S pixel edit target: " + target);

    private static string NormalizeTarget(string target)
        => ResolveSpec(string.IsNullOrWhiteSpace(target) ? "front" : target.Trim()).Role;

    private static string NormalizeOperation(string operation)
        => string.IsNullOrWhiteSpace(operation) ? string.Empty : operation.Trim().ToLowerInvariant();

    private static string NormalizePackageId(string value)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? "RsPixelEdit_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) : value.Trim();
        var chars = raw.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray();
        return new string(chars).Trim('_');
    }

    private static string ResolveExistingFile(CczProject? project, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("file path is required.");
        var candidates = BuildPathCandidates(project, path);
        var resolved = candidates.FirstOrDefault(File.Exists);
        if (resolved == null) throw new FileNotFoundException("File was not found.", path);
        return Path.GetFullPath(resolved);
    }

    private static string ResolveExistingDirectory(CczProject? project, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("directory path is required.");
        var candidates = BuildPathCandidates(project, path);
        var resolved = candidates.FirstOrDefault(Directory.Exists);
        if (resolved == null) throw new DirectoryNotFoundException("Directory was not found: " + path);
        return Path.GetFullPath(resolved);
    }

    private static IReadOnlyList<string> BuildPathCandidates(CczProject? project, string path)
    {
        if (Path.IsPathFullyQualified(path)) return [path];
        var candidates = new List<string> { Path.GetFullPath(path) };
        if (project != null)
        {
            candidates.Add(Path.Combine(project.WorkspaceRoot, path));
            candidates.Add(Path.Combine(project.GameRoot, path));
        }
        return candidates;
    }

    private static Bitmap LoadBitmap(string path)
    {
        using var image = Image.FromFile(path);
        return new Bitmap(image);
    }

    private static void SaveBmp(Bitmap bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                output.SetPixel(x, y, color.A == 0 ? MagentaKey : Color.FromArgb(255, color.R, color.G, color.B));
            }
        }
        output.Save(path, ImageFormat.Bmp);
    }

    private static Color ParseColor(string value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = value.Trim();
        if (value.StartsWith('#') && value.Length == 7 &&
            int.TryParse(value[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
            int.TryParse(value[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
            int.TryParse(value[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return Color.FromArgb(255, r, g, b);
        }
        return Color.FromName(value).ToArgb() == 0 ? fallback : Color.FromName(value);
    }

    private static Dictionary<char, Color> ParseMaskPalette(RsPixelFrameEditOperation operation)
    {
        var palette = new Dictionary<char, Color>
        {
            ['A'] = ParseColor(operation.Color, ArmorDark),
            ['B'] = ParseColor(operation.SecondaryColor, ArmorGold),
            ['K'] = Color.Black,
            ['S'] = SkinBase,
            ['s'] = SkinShadow,
            ['G'] = ArmorGold,
            ['D'] = ArmorDark,
            ['R'] = CapeRed,
            ['W'] = SpearTip,
            ['Y'] = SpearGlow,
            ['P'] = SpearWood,
            ['H'] = Color.FromArgb(255, 238, 222, 170),
            ['M'] = Color.FromArgb(255, 120, 120, 120)
        };

        if (string.IsNullOrWhiteSpace(operation.Palette)) return palette;
        foreach (var entry in operation.Palette.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0) continue;
            palette[parts[0][0]] = ParseColor(parts[1], palette.TryGetValue(parts[0][0], out var existing) ? existing : ArmorGold);
        }

        return palette;
    }

    private static int SetPixelIfChanged(Bitmap bitmap, int x, int y, Color color)
    {
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height) return 0;
        var current = bitmap.GetPixel(x, y);
        var next = Color.FromArgb(255, color.R, color.G, color.B);
        if (current.ToArgb() == next.ToArgb()) return 0;
        bitmap.SetPixel(x, y, next);
        return 1;
    }

    private static int Luminance(Color color)
        => (color.R * 299 + color.G * 587 + color.B * 114) / 1000;

    private static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        var r = (int)Math.Round(a.R + (b.R - a.R) * t);
        var g = (int)Math.Round(a.G + (b.G - a.G) * t);
        var blue = (int)Math.Round(a.B + (b.B - a.B) * t);
        return Color.FromArgb(255, r, g, blue);
    }

    private static int CountNonMagenta(Bitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (!IsMagenta(bitmap.GetPixel(x, y))) count++;
            }
        }
        return count;
    }

    private static bool IsStrictMagenta(Color color)
        => color.R == 255 && color.G == 0 && color.B == 255;

    private static bool IsNearMagenta(Color color)
        => color.R >= 235 && color.G <= 35 && color.B >= 235;

    private static bool IsMagenta(Color color)
        => color.A == 0 || IsNearMagenta(color);

    private static bool IsWeaponBright(Color color)
        => !IsMagenta(color) && color.R >= 210 && color.G >= 170 && color.B >= 70;

    private static bool IsEffectResidue(Color color)
    {
        if (IsMagenta(color)) return false;
        var brightness = Luminance(color);
        var whiteOrGraySlash = brightness >= 150 && Math.Abs(color.R - color.G) <= 45 && Math.Abs(color.G - color.B) <= 65;
        var palePinkSlash = color.R >= 190 && color.G >= 105 && color.B >= 150;
        return whiteOrGraySlash || palePinkSlash;
    }

    private sealed record RsPixelStripSpec(string Role, string Group, string FileName, int Width, int Height, int FrameWidth, int FrameHeight)
    {
        public int FrameCount => Height / FrameHeight;
    }
}
