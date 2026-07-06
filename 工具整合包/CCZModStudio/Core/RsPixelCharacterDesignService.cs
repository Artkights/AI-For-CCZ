using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RsPixelCharacterDesignService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly AiImageAssetService _aiImageAssetService = new();
    private readonly BmpImageExportService _bmpExportService = new();

    public RsPixelCharacterDesignResult Build(
        CczProject project,
        RsPixelCharacterDesignRequest request,
        Func<AiImagePromptPlan, string, object?>? previewFactory = null)
    {
        var packageId = NormalizePackageId(request.PackageId);
        var unitType = NormalizeUnitType(request.UnitType);
        var warnings = new List<string>();
        var errors = new List<string>();
        var reports = new List<string>();
        var packageRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "RS_PixelDesign", packageId);
        EnsurePackageFolders(packageRoot);

        var designSourcePath = ResolveExistingFile(project, request.DesignImagePath);
        var designPath = CopyFile(designSourcePath, Path.Combine(packageRoot, "refs", "design", "sunce_design" + Path.GetExtension(designSourcePath)));
        var formatActionPath = ResolveOrPrepareFormatReference(project, packageRoot, request, warnings);

        var characterBrief = BuildCharacterBrief(request, unitType);
        var sDescription = BuildSUnitDescription(characterBrief, unitType);
        var rDescription = BuildRActorDescription(characterBrief, unitType);
        var referencePaths = new[] { designPath, formatActionPath };
        var referenceRoles = new[] { "design", "format_action" };

        var sPlan = _aiImageAssetService.BuildPromptPlan(
            project,
            "s_unit",
            sDescription,
            targetRelativePath: null,
            imageNumber: null,
            rImageId: null,
            sImageId: request.SImageId,
            faceId: null,
            jobId: request.JobId,
            factionSlot: request.FactionSlot,
            outputFormat: "bmp",
            width: 48,
            height: 528,
            referenceImagePaths: referencePaths,
            referenceRoles: referenceRoles);

        var rPlan = _aiImageAssetService.BuildPromptPlan(
            project,
            "r_actor",
            rDescription,
            targetRelativePath: null,
            imageNumber: null,
            rImageId: request.RImageId,
            sImageId: null,
            faceId: null,
            jobId: null,
            factionSlot: request.FactionSlot,
            outputFormat: "bmp",
            width: 48,
            height: 1280,
            referenceImagePaths: referencePaths,
            referenceRoles: referenceRoles);

        reports.Add(WriteJson(Path.Combine(packageRoot, "reports", "manifest.json"), BuildManifest(project, request, packageId, unitType, designPath, formatActionPath)));
        reports.Add(WriteJson(Path.Combine(packageRoot, "reports", "generation_plan.json"), new
        {
            PackageId = packageId,
            UnitType = unitType,
            ReferenceImages = new[]
            {
                BuildReferenceRecord("design", designPath),
                BuildReferenceRecord("format_action", formatActionPath)
            },
            SUnitPlan = sPlan,
            RActorPlan = rPlan,
            Rules = BuildRules(unitType),
            SafetyNote = "Plans only. They do not write game resources."
        }));
        reports.Add(WriteText(Path.Combine(packageRoot, "prompts", "mcp_reference_generation_plan.md"), BuildPromptMarkdown(packageId, unitType, designPath, formatActionPath, sPlan, rPlan)));

        AiImageDrawResult? sDraw = null;
        AiImageDrawResult? rDraw = null;
        var generationStatus = "planned_only";

        if (request.GenerateNow)
        {
            try
            {
                sDraw = _aiImageAssetService.DrawAsync(project, sPlan, request.DryRun, previewFactory).GetAwaiter().GetResult();
                rDraw = _aiImageAssetService.DrawAsync(project, rPlan, request.DryRun, previewFactory).GetAwaiter().GetResult();
                if (request.DryRun)
                {
                    generationStatus = "dry_run_completed";
                }
                else
                {
                    var copiedMaterials = CopyGeneratedMaterials(packageRoot, rDraw, sDraw);
                    reports.Add(WriteJson(Path.Combine(packageRoot, "reports", "generated_materials_report.json"), new
                    {
                        PackageId = packageId,
                        CopiedMaterials = copiedMaterials,
                        RequiredFilesPresent = RequiredMaterialFiles(packageRoot).All(File.Exists),
                        SafetyNote = "Generated files were copied into the package material folders only after MCP upstream generation. No local procedural fallback was used."
                    }));
                    generationStatus = "generated_pending_visual_acceptance";
                }
            }
            catch (Exception ex)
            {
                generationStatus = "blocked_generation_failed";
                errors.Add(ex.Message);
            }
        }
        else
        {
            warnings.Add("generate_now=false; package, reference records, and prompt plans were created without network generation.");
        }

        if (!request.GenerateNow || errors.Count > 0)
        {
            var apiKeyPresent = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AiImageAssetService.EnvRetroDiffusionApiKey));
            var baseUrlPresent = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AiImageAssetService.EnvRetroDiffusionBaseUrl));
            reports.Add(WriteJson(Path.Combine(packageRoot, "reports", "mcp_generation_report.json"), new
            {
                PackageId = packageId,
                GenerationStatus = generationStatus,
                GenerateNow = request.GenerateNow,
                request.DryRun,
                Environment = new
                {
                    RetroDiffusionApiKeyPresent = apiKeyPresent,
                    RetroDiffusionBaseUrlPresent = baseUrlPresent,
                    RetroDiffusionModelPresent = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AiImageAssetService.EnvRetroDiffusionModel)),
                    PixelProviderPresent = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AiImageAssetService.EnvPixelProvider))
                },
                Blockers = BuildGenerationBlockers(request, apiKeyPresent, baseUrlPresent, errors),
                Warnings = warnings,
                Errors = errors,
                NoFallback = "No local Pillow/procedural R/S final material generation is performed by this workflow."
            }));
        }

        reports.Add(WriteText(Path.Combine(packageRoot, "reports", "visual_acceptance_report.md"), BuildVisualAcceptanceReport(generationStatus)));

        return new RsPixelCharacterDesignResult
        {
            PackageId = packageId,
            PackageRoot = packageRoot,
            GenerationStatus = generationStatus,
            DesignImagePath = designPath,
            FormatActionImagePath = formatActionPath,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
            Errors = errors.Distinct(StringComparer.Ordinal).ToArray(),
            Reports = reports.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SUnitPlan = sPlan,
            RActorPlan = rPlan,
            SUnitDraw = sDraw,
            RActorDraw = rDraw,
            SafetyNote = "MCP-first R/S workflow. It records design and format reference images and never writes formal game resources."
        };
    }

    private string ResolveOrPrepareFormatReference(
        CczProject project,
        string packageRoot,
        RsPixelCharacterDesignRequest request,
        List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(request.FormatActionImagePath))
        {
            var path = ResolveExistingFile(project, request.FormatActionImagePath);
            return CopyFile(path, Path.Combine(packageRoot, "refs", "selected_format", Path.GetFileName(path)));
        }

        if (!string.IsNullOrWhiteSpace(request.FormatReferenceFolder))
        {
            var folder = ResolveExistingDirectory(project, request.FormatReferenceFolder);
            return CopyFormatFolder(project, packageRoot, folder, warnings);
        }

        var known = Path.Combine(
            project.WorkspaceRoot,
            "CCZModStudio_Exports",
            "RS_PixelDesign",
            "_reference_samples",
            "spear_cavalry_candidates_mcp",
            "huanwang_s",
            "S64",
            "S64_all_frames_x4.png");
        if (File.Exists(known))
        {
            warnings.Add("No format reference was supplied; selected existing local S64 mounted reference sample as format_action.");
            return CopyFile(known, Path.Combine(packageRoot, "refs", "selected_format", "S64_huanwang_format_action", Path.GetFileName(known)));
        }

        if (!string.IsNullOrWhiteSpace(request.FormatReferenceGameRoot) && request.FormatReferenceSImageId.HasValue)
        {
            var exportFolder = Path.Combine(packageRoot, "refs", "format_candidates", "mcp_export_S" + request.FormatReferenceSImageId.Value.ToString(CultureInfo.InvariantCulture));
            var exportProject = new CczProject
            {
                WorkspaceRoot = project.WorkspaceRoot,
                GameRoot = Path.GetFullPath(request.FormatReferenceGameRoot),
                HexTableXmlPath = project.HexTableXmlPath
            };
            _bmpExportService.Export(exportProject, new BmpExportRequest
            {
                Kind = BmpExportKind.SImage,
                OutputRoot = exportFolder,
                SingleMode = true,
                OverwriteExisting = true,
                FactionSlot = request.FactionSlot,
                Targets =
                [
                    new BmpExportTarget
                    {
                        RowId = request.FormatReferenceRowId ?? request.FormatReferenceSImageId.Value,
                        DisplayName = string.IsNullOrWhiteSpace(request.FormatReferenceDisplayName) ? "format_reference" : request.FormatReferenceDisplayName,
                        FieldValue = request.FormatReferenceSImageId.Value
                    }
                ]
            });
            return CopyFormatFolder(project, packageRoot, exportFolder, warnings);
        }

        throw new InvalidOperationException("A format_action reference image or folder is required when the default local S64 reference is not available.");
    }

    private static string CopyFormatFolder(CczProject project, string packageRoot, string folder, List<string> warnings)
    {
        var selected = Path.Combine(packageRoot, "refs", "selected_format", MakeSafeName(Path.GetFileName(folder)));
        Directory.CreateDirectory(selected);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".bmp" or ".png" or ".jpg" or ".jpeg")
            {
                File.Copy(file, Path.Combine(selected, Path.GetFileName(file)), overwrite: true);
            }
        }

        var sheet = Directory.EnumerateFiles(selected)
            .FirstOrDefault(file => Path.GetFileName(file).Contains("all_frames", StringComparison.OrdinalIgnoreCase) && Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(sheet)) return sheet;

        var png = Directory.EnumerateFiles(selected, "*.png").FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(png)) return png;

        var atk = Directory.EnumerateFiles(selected, "atk.bmp").FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(atk))
        {
            warnings.Add("Format folder has no contact sheet; using atk.bmp as format_action reference. A contact sheet is recommended.");
            return atk;
        }

        var anyImage = Directory.EnumerateFiles(selected).FirstOrDefault(file => IsImageExtension(Path.GetExtension(file)));
        return anyImage ?? throw new InvalidOperationException("Format reference folder does not contain a usable image.");
    }

    private static object BuildManifest(CczProject project, RsPixelCharacterDesignRequest request, string packageId, string unitType, string designPath, string formatActionPath)
        => new
        {
            SchemaVersion = 1,
            PackageId = packageId,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? packageId : request.DisplayName,
            UnitType = unitType,
            SourceMode = "mcp_reference_images",
            PlannedOnly = !request.GenerateNow,
            Role = new
            {
                request.RImageId,
                request.SImageId,
                request.JobId,
                request.FactionSlot
            },
            References = new[]
            {
                BuildReferenceRecord("design", designPath),
                BuildReferenceRecord("format_action", formatActionPath)
            },
            Materials = new
            {
                RFront = "materials/r_actor/front.bmp",
                RBack = "materials/r_actor/back.bmp",
                SMov = "materials/s_unit/mov.bmp",
                SAtk = "materials/s_unit/atk.bmp",
                SSpc = "materials/s_unit/spc.bmp"
            },
            Project = new
            {
                project.WorkspaceRoot,
                project.GameRoot
            }
        };

    private static object BuildReferenceRecord(string role, string path)
        => new
        {
            Role = role,
            Path = path,
            Sha256 = ComputeSha256(File.ReadAllBytes(path))
        };

    private static string BuildCharacterBrief(RsPixelCharacterDesignRequest request, string unitType)
    {
        var brief = string.IsNullOrWhiteSpace(request.CharacterBrief)
            ? "Sun Ce, Jiangdong commander, black-and-gold heavy armor, gold crown and topknot, red-black cloak, mounted CCZ 6.5 short-bodied tactical sprite."
            : request.CharacterBrief.Trim();
        var weapon = string.IsNullOrWhiteSpace(request.WeaponBrief)
            ? "Weapon override: the only weapon is one long spear or lance. Ignore any sword, short blade, second weapon, crossed weapon, or white sword arc visible in the design reference. Attacks are charge-up, thrust, upward spear lift, white-gold spear-tip burst, and recovery."
            : request.WeaponBrief.Trim();
        var forbidden = string.IsNullOrWhiteSpace(request.ForbiddenReadings)
            ? "Forbidden: sword, short blade, waist sword, back sword, second spear, dual weapons, wide sword slash arc, modern pixel sticker, procedural geometric shapes, blackened face, red face pollution."
            : request.ForbiddenReadings.Trim();

        return $"unitType={unitType}. {brief} {weapon} {forbidden}";
    }

    private static string BuildSUnitDescription(string characterBrief, string unitType)
        => characterBrief
           + " Build a CCZ 6.5 S-unit source sheet: exactly 4 columns x 6 rows, 24 separate action cells, later post-processed into mov/atk/spc. "
           + "Reference separation is mandatory: the design image controls identity, armor, palette, crown, cloak, and commander silhouette only; the format/action image controls CCZ 6.5 pose grammar, short body ratio, cell occupancy, hard-edged low-pixel style, and magenta key only. "
           + "The text weapon brief overrides every visible weapon in the design image. Keep a single-spear cavalry reading in all cells.";

    private static string BuildRActorDescription(string characterBrief, string unitType)
        => characterBrief
           + " Build a CCZ 6.5 R-actor source sheet: exactly 2 columns x 20 rows. Left column is front/front-side frames, right column is back/back-side frames. "
           + "Post-processing cuts the two columns into front.bmp and back.bmp; it must not duplicate one frame. "
           + "Front and back must be separately drawn. Back frames must show cloak, back armor, rear topknot/crown shape, and single spear back-carry or diagonal spear relationship.";

    private static object BuildRules(string unitType)
        => new
        {
            UnitType = unitType,
            SingleSpear = new[]
            {
                "Exactly one long spear/lance axis per key frame.",
                "No sword, short blade, waist sword, back sword, second spear, dual weapon, or wide sword arc.",
                "Spear effect must originate from the spear tip.",
                "Attack frames must show continuous spear-tip movement."
            },
            Acceptance = new[]
            {
                "Five BMP materials must exist before package promotion.",
                "Visual review must confirm authentic CCZ 6.5 R/S style.",
                "MCP preview is required before any test-copy write.",
                "Formal-base write is not allowed by this workflow."
            }
        };

    private static string BuildPromptMarkdown(string packageId, string unitType, string designPath, string formatActionPath, AiImagePromptPlan sPlan, AiImagePromptPlan rPlan)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# MCP R/S Reference Generation Plan");
        builder.AppendLine();
        builder.AppendLine($"Package: `{packageId}`");
        builder.AppendLine($"unitType: `{unitType}`");
        builder.AppendLine();
        builder.AppendLine("## References");
        builder.AppendLine($"- role=`design`: `{designPath}`");
        builder.AppendLine($"- role=`format_action`: `{formatActionPath}`");
        builder.AppendLine();
        builder.AppendLine("## S Unit");
        builder.AppendLine("```text");
        builder.AppendLine(sPlan.Description);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## R Actor");
        builder.AppendLine("```text");
        builder.AppendLine(rPlan.Description);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Gates");
        builder.AppendLine("- Do not promote until visual acceptance, format validation, single-spear validation, and MCP preview pass.");
        builder.AppendLine("- Do not call replace_* or write a formal base from this workflow.");
        return builder.ToString();
    }

    private static string BuildVisualAcceptanceReport(string status)
        => "# Visual Acceptance Report\n\n"
           + $"Status: {status}\n\n"
           + "No visual acceptance is granted until five generated BMP materials exist and are manually checked at original frame size.\n";

    private static IReadOnlyList<string> BuildGenerationBlockers(
        RsPixelCharacterDesignRequest request,
        bool retroDiffusionApiKeyPresent,
        bool retroDiffusionBaseUrlPresent,
        IReadOnlyList<string> errors)
    {
        var blockers = new List<string>();
        if (!request.GenerateNow)
        {
            blockers.Add("generate_now=false; this run intentionally produced only package scaffolding, references, and prompt plans.");
        }

        if (!retroDiffusionApiKeyPresent)
        {
            blockers.Add("RETRO_DIFFUSION_API_KEY is not configured; MCP-first R/S generation cannot call RetroDiffusion.");
        }

        if (!retroDiffusionBaseUrlPresent)
        {
            blockers.Add("RETRO_DIFFUSION_BASE_URL is not configured; the service can use its default only after the running MCP process is restarted with the updated code.");
        }

        if (errors.Count > 0)
        {
            blockers.Add("Generation failed before all five BMP materials were created.");
        }

        return blockers;
    }

    private static IReadOnlyList<object> CopyGeneratedMaterials(string packageRoot, AiImageDrawResult? rDraw, AiImageDrawResult? sDraw)
    {
        if (rDraw?.Prepared == null || sDraw?.Prepared == null)
        {
            throw new InvalidOperationException("MCP generation did not produce prepared R and S material files.");
        }

        var copies = new List<object>();
        foreach (var file in rDraw.Prepared.PreparedFiles)
        {
            var name = file.Role switch
            {
                "front" => "front.bmp",
                "back" => "back.bmp",
                _ => null
            };
            if (name == null) continue;
            copies.Add(CopyGeneratedMaterial(file.OutputPath, Path.Combine(packageRoot, "materials", "r_actor", name), file.Role));
        }

        foreach (var file in sDraw.Prepared.PreparedFiles)
        {
            var name = file.Role switch
            {
                "move" => "mov.bmp",
                "attack" => "atk.bmp",
                "special" => "spc.bmp",
                _ => null
            };
            if (name == null) continue;
            copies.Add(CopyGeneratedMaterial(file.OutputPath, Path.Combine(packageRoot, "materials", "s_unit", name), file.Role));
        }

        var missing = RequiredMaterialFiles(packageRoot).Where(path => !File.Exists(path)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException("MCP generation did not produce all required R/S material files: " + string.Join(", ", missing.Select(Path.GetFileName)));
        }

        return copies;
    }

    private static object CopyGeneratedMaterial(string source, string destination, string role)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        var bytes = File.ReadAllBytes(destination);
        return new
        {
            Role = role,
            SourcePath = source,
            MaterialPath = Path.GetFullPath(destination),
            SizeBytes = bytes.Length,
            Sha256 = ComputeSha256(bytes)
        };
    }

    private static IEnumerable<string> RequiredMaterialFiles(string packageRoot)
    {
        yield return Path.Combine(packageRoot, "materials", "r_actor", "front.bmp");
        yield return Path.Combine(packageRoot, "materials", "r_actor", "back.bmp");
        yield return Path.Combine(packageRoot, "materials", "s_unit", "mov.bmp");
        yield return Path.Combine(packageRoot, "materials", "s_unit", "atk.bmp");
        yield return Path.Combine(packageRoot, "materials", "s_unit", "spc.bmp");
    }

    private static void EnsurePackageFolders(string packageRoot)
    {
        foreach (var relative in new[]
                 {
                     "refs/design",
                     "refs/format_candidates",
                     "refs/selected_format",
                     "drafts",
                     "materials/r_actor",
                     "materials/s_unit",
                     "reports",
                     "prompts"
                 })
        {
            Directory.CreateDirectory(Path.Combine(packageRoot, relative));
        }
    }

    private static string ResolveExistingFile(CczProject project, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("A file path is required.");
        var trimmed = path.Trim().Trim('"');
        var candidates = Path.IsPathRooted(trimmed)
            ? new[] { trimmed }
            : new[]
            {
                Path.Combine(project.WorkspaceRoot, trimmed),
                Path.Combine(project.GameRoot, trimmed),
                Path.Combine(Directory.GetCurrentDirectory(), trimmed)
            };
        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return full;
        }

        throw new FileNotFoundException("File was not found.", Path.GetFullPath(candidates[0]));
    }

    private static string ResolveExistingDirectory(CczProject project, string path)
    {
        var trimmed = path.Trim().Trim('"');
        var candidates = Path.IsPathRooted(trimmed)
            ? new[] { trimmed }
            : new[]
            {
                Path.Combine(project.WorkspaceRoot, trimmed),
                Path.Combine(project.GameRoot, trimmed),
                Path.Combine(Directory.GetCurrentDirectory(), trimmed)
            };
        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full)) return full;
        }

        throw new DirectoryNotFoundException(Path.GetFullPath(candidates[0]));
    }

    private static string CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        return Path.GetFullPath(destination);
    }

    private static string WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8);
        return Path.GetFullPath(path);
    }

    private static string WriteText(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value, Encoding.UTF8);
        return Path.GetFullPath(path);
    }

    private static string NormalizePackageId(string value)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? "RsPixelCharacterDesign_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) : value.Trim();
        return MakeSafeName(raw);
    }

    private static string NormalizeUnitType(string value)
        => string.IsNullOrWhiteSpace(value) ? "spear_cavalry" : value.Trim().Replace('-', '_').ToLowerInvariant();

    private static string MakeSafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var safe = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "unnamed" : safe;
    }

    private static bool IsImageExtension(string extension)
        => extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp";

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
