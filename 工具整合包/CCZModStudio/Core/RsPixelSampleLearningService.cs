using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RsPixelSampleLearningService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Color MagentaKey = Color.FromArgb(255, 255, 0, 255);
    private static readonly RsStripSpec[] StripSpecs =
    [
        new("front", "r_actor", "front.bmp", 48, 1280, 48, 64),
        new("back", "r_actor", "back.bmp", 48, 1280, 48, 64),
        new("mov", "s_unit", "mov.bmp", 48, 528, 48, 48),
        new("atk", "s_unit", "atk.bmp", 64, 768, 64, 64),
        new("spc", "s_unit", "spc.bmp", 48, 240, 48, 48)
    ];

    public RsPixelSampleLearningResult Build(CczProject project, RsPixelSampleLearningRequest request)
    {
        var unitType = string.IsNullOrWhiteSpace(request.UnitType) ? "spear_cavalry" : request.UnitType.Trim();
        var outputId = NormalizeId(string.IsNullOrWhiteSpace(request.OutputId) ? $"{unitType}_mvp" : request.OutputId);
        var outputRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "RS_PixelDesign", "_sample_learning", outputId);
        if (Directory.Exists(outputRoot) && request.OverwriteExisting)
        {
            Directory.Delete(outputRoot, recursive: true);
        }
        if (Directory.Exists(outputRoot) && Directory.EnumerateFileSystemEntries(outputRoot).Any())
        {
            throw new InvalidOperationException($"Sample learning output already exists: {outputRoot}. Set overwrite_existing=true to rebuild it.");
        }

        var candidatesRoot = Path.Combine(outputRoot, "candidates");
        var contactRoot = Path.Combine(outputRoot, "contact_sheets");
        var metricsRoot = Path.Combine(outputRoot, "metrics");
        var annotationsRoot = Path.Combine(outputRoot, "annotations");
        var positiveRoot = Path.Combine(outputRoot, "selected", "positive_candidates");
        var negativeRoot = Path.Combine(outputRoot, "selected", "negative_cases");
        var reportsRoot = Path.Combine(outputRoot, "reports");
        foreach (var directory in new[] { candidatesRoot, contactRoot, metricsRoot, annotationsRoot, positiveRoot, negativeRoot, reportsRoot })
        {
            Directory.CreateDirectory(directory);
        }

        var warnings = new List<string>();
        var referenceRoots = ResolveReferenceRoots(project, request.ReferenceRoots, warnings);
        var negativeRoots = ResolveNegativeRoots(project, request.NegativeRoots, warnings);
        var discovered = DiscoverCandidates(referenceRoots, negativeRoots, warnings);
        var normalized = new List<LearningCandidate>();
        foreach (var candidate in discovered.OrderBy(candidate => candidate.SortKey, StringComparer.OrdinalIgnoreCase))
        {
            normalized.Add(NormalizeCandidate(candidate, candidatesRoot, contactRoot, metricsRoot, unitType, warnings));
        }

        var ranked = normalized
            .OrderByDescending(candidate => candidate.Score.Total)
            .ThenBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var topCount = Math.Clamp(request.TopReviewCount <= 0 ? 12 : request.TopReviewCount, 1, 50);
        var top = BuildTopReviewSet(ranked, topCount);
        foreach (var candidate in top.Where(candidate => candidate.Score.MachineClass == "positive_candidate"))
        {
            WriteSelectionPointer(candidate, positiveRoot);
        }
        foreach (var candidate in ranked.Where(candidate => candidate.Score.MachineClass == "negative_case").Take(Math.Max(3, topCount / 2)))
        {
            WriteSelectionPointer(candidate, negativeRoot);
        }

        var reports = WriteReports(
            outputRoot,
            reportsRoot,
            annotationsRoot,
            unitType,
            referenceRoots,
            negativeRoots,
            ranked,
            top,
            warnings);
        var contactSheets = ranked.SelectMany(candidate => candidate.ContactSheets).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var result = new RsPixelSampleLearningResult
        {
            UnitType = unitType,
            OutputRoot = outputRoot,
            CandidateCount = normalized.Count,
            CompleteCandidateCount = normalized.Count(candidate => candidate.IsComplete),
            RGroupCount = normalized.Count(candidate => candidate.HasR),
            SGroupCount = normalized.Count(candidate => candidate.HasS),
            StrongMachineCandidateCount = normalized.Count(candidate => candidate.Score.MachineClass == "positive_candidate"),
            PartialCandidateCount = normalized.Count(candidate => candidate.Score.MachineClass == "partial_reference"),
            NegativeCandidateCount = normalized.Count(candidate => candidate.Score.MachineClass == "negative_case"),
            Reports = reports,
            ContactSheets = contactSheets,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
            SafetyNote = "Read-only R/S sample learning output. No game resources, test copies, or local_sample_index.json entries were written."
        };
        File.WriteAllText(Path.Combine(reportsRoot, "sample_learning_result.json"), JsonSerializer.Serialize(result, JsonOptions));
        return result;
    }

    private static IReadOnlyList<string> ResolveReferenceRoots(CczProject project, IReadOnlyList<string> requested, List<string> warnings)
    {
        var roots = new List<string>();
        if (requested.Count > 0)
        {
            roots.AddRange(requested.Select(path => ResolvePath(project, path)).Where(Directory.Exists));
        }
        else
        {
            var referenceRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "RS_PixelDesign", "_reference_samples");
            foreach (var relative in new[]
                     {
                         "true_mounted_spear_rescan",
                         "single_spear_cavalry_v1",
                         "spear_cavalry_candidates_mcp"
                     })
            {
                var path = Path.Combine(referenceRoot, relative);
                if (Directory.Exists(path)) roots.Add(path);
                else warnings.Add($"reference root not found: {path}");
            }

            var baseRoot = Path.Combine(project.WorkspaceRoot, "基底");
            foreach (var relative in new[]
                     {
                         "加强版6.5未加密版",
                         "东吴霸王传6.5",
                         "东吴霸王传",
                         "重生之氪金桓王传"
                     })
            {
                var path = Path.Combine(baseRoot, relative);
                if (Directory.Exists(path)) roots.Add(path);
                else warnings.Add($"base root not found for future export scan: {path}");
            }
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> ResolveNegativeRoots(CczProject project, IReadOnlyList<string> requested, List<string> warnings)
    {
        var roots = new List<string>();
        if (requested.Count > 0)
        {
            roots.AddRange(requested.Select(path => ResolvePath(project, path)).Where(Directory.Exists));
        }
        else
        {
            var pixelRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "RS_PixelDesign");
            foreach (var relative in new[]
                     {
                         "SunCe_LocalPixelEditor_SingleSpearCavalry_v1",
                         "SunCe_LocalPixelEditor_SingleSpearCavalry_v2",
                         "SunCe_LocalPixelEditor_SingleSpearCavalry_v2_1",
                         "SunCe_LocalPixelEditor_TrueMountedSpear_v1",
                         "SunCe_LocalPixelEditor_TrueMountedSpear_v2",
                         "SunCe_LocalPixelEditor_TrueMountedSpear_v3",
                         "SunCe_LocalPixelEditor_TrueMountedSpear_v3_failed_aggressive_pass_20260706_0033",
                         "SunCe_MCP_SingleSpearCavalry_v1",
                         "Infantry_Generic_v2",
                         "Smoke_LocalPixelEditor_SingleSpear"
                     })
            {
                var path = Path.Combine(pixelRoot, relative);
                if (Directory.Exists(path)) roots.Add(path);
            }
        }

        if (roots.Count == 0) warnings.Add("No explicit negative roots were found; negative reports will rely on detected incomplete/low-score candidates.");
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<DiscoveredCandidate> DiscoverCandidates(IReadOnlyList<string> referenceRoots, IReadOnlyList<string> negativeRoots, List<string> warnings)
    {
        var byKey = new Dictionary<string, DiscoveredCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in referenceRoots)
        {
            DiscoverUnderRoot(root, isNegativeSource: false, byKey, warnings);
        }
        foreach (var root in negativeRoots)
        {
            DiscoverUnderRoot(root, isNegativeSource: true, byKey, warnings);
        }

        return byKey.Values.ToArray();
    }

    private static void DiscoverUnderRoot(string root, bool isNegativeSource, Dictionary<string, DiscoveredCandidate> byKey, List<string> warnings)
    {
        if (!Directory.Exists(root)) return;
        var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root);
        foreach (var directory in directories)
        {
            if (IsNestedMaterialMirror(directory)) continue;
            var files = ResolveStripFiles(directory);
            if (files.Count == 0) continue;
            var key = Path.GetFullPath(directory);
            if (byKey.ContainsKey(key)) continue;
            var isLikelyMounted = LooksLikeMountedSpearPath(directory) || files.Count >= 5;
            var isLikelyNegative = isNegativeSource || LooksLikeNegativePath(directory);
            if (!isLikelyMounted && !isLikelyNegative) continue;
            byKey[key] = new DiscoveredCandidate
            {
                SourceDirectory = key,
                StripFiles = files,
                IsNegativeSource = isLikelyNegative,
                SortKey = key
            };
        }

        if (byKey.Count == 0 && isNegativeSource)
        {
            warnings.Add($"negative root had no R/S strip candidates: {root}");
        }
    }

    private static LearningCandidate NormalizeCandidate(
        DiscoveredCandidate discovered,
        string candidatesRoot,
        string contactRoot,
        string metricsRoot,
        string unitType,
        List<string> warnings)
    {
        var id = BuildCandidateId(discovered.SourceDirectory);
        var candidateRoot = Path.Combine(candidatesRoot, id);
        var materialsR = Path.Combine(candidateRoot, "materials", "r_actor");
        var materialsS = Path.Combine(candidateRoot, "materials", "s_unit");
        var candidateReports = Path.Combine(candidateRoot, "reports");
        Directory.CreateDirectory(materialsR);
        Directory.CreateDirectory(materialsS);
        Directory.CreateDirectory(candidateReports);

        var stripMetrics = new Dictionary<string, StripMetrics>(StringComparer.OrdinalIgnoreCase);
        var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var spec in StripSpecs)
        {
            if (!discovered.StripFiles.TryGetValue(spec.Role, out var source))
            {
                missing.Add(spec.FileName);
                continue;
            }

            var destination = Path.Combine(spec.Group == "r_actor" ? materialsR : materialsS, spec.FileName);
            try
            {
                using var bitmap = LoadBitmap(source);
                bitmap.Save(destination, ImageFormat.Bmp);
                copied[spec.Role] = destination;
                stripMetrics[spec.Role] = AnalyzeStrip(bitmap, spec);
            }
            catch (Exception ex)
            {
                missing.Add($"{spec.FileName}: {ex.Message}");
                warnings.Add($"{id} failed to load {source}: {ex.Message}");
            }
        }

        var hasR = copied.ContainsKey("front") && copied.ContainsKey("back");
        var hasS = copied.ContainsKey("mov") && copied.ContainsKey("atk") && copied.ContainsKey("spc");
        var isComplete = hasR && hasS;
        var sourceSummary = BuildSourceSummary(discovered.SourceDirectory);
        var score = ScoreCandidate(id, sourceSummary, stripMetrics, isComplete, hasR, hasS, discovered.IsNegativeSource);
        var candidateContactRoot = Path.Combine(contactRoot, id);
        Directory.CreateDirectory(candidateContactRoot);
        var contactSheets = new List<string>();
        foreach (var spec in StripSpecs)
        {
            if (!copied.TryGetValue(spec.Role, out var path)) continue;
            using var bitmap = LoadBitmap(path);
            var output = Path.Combine(candidateContactRoot, $"contact_sheet_{spec.Role}_x6.png");
            using var sheet = BuildContactSheet(bitmap, spec, 6);
            sheet.Save(output, ImageFormat.Png);
            contactSheets.Add(output);
        }
        if (copied.Count > 0)
        {
            var reviewSheet = Path.Combine(candidateContactRoot, "candidate_review_sheet_x6.png");
            using var sheet = BuildReviewSheet(copied, 6);
            sheet.Save(reviewSheet, ImageFormat.Png);
            contactSheets.Add(reviewSheet);
        }

        var metrics = new CandidateMetrics
        {
            Id = id,
            UnitType = unitType,
            SourceDirectory = discovered.SourceDirectory,
            SourceSummary = sourceSummary,
            IsNegativeSource = discovered.IsNegativeSource,
            IsComplete = isComplete,
            HasR = hasR,
            HasS = hasS,
            Missing = missing,
            CopiedFiles = copied,
            StripMetrics = stripMetrics,
            Score = score,
            ContactSheets = contactSheets
        };
        var metricsPath = Path.Combine(metricsRoot, id + ".json");
        File.WriteAllText(metricsPath, JsonSerializer.Serialize(metrics, JsonOptions));
        File.WriteAllText(Path.Combine(candidateReports, "raw_export_manifest.json"), JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            CandidateId = id,
            discovered.SourceDirectory,
            SourceSummary = sourceSummary,
            discovered.IsNegativeSource,
            Missing = missing,
            CopiedFiles = copied.Select(pair => new
            {
                Role = pair.Key,
                Path = pair.Value,
                Sha256 = ComputeSha256(pair.Value)
            }),
            Note = "Normalized by RsPixelSampleLearningService from existing local R/S reference exports. No game resources were written."
        }, JsonOptions));

        return new LearningCandidate
        {
            Id = id,
            SourceDirectory = discovered.SourceDirectory,
            SourceSummary = sourceSummary,
            CandidateRoot = candidateRoot,
            MetricsPath = metricsPath,
            ContactSheets = contactSheets,
            IsNegativeSource = discovered.IsNegativeSource,
            IsComplete = isComplete,
            HasR = hasR,
            HasS = hasS,
            Missing = missing,
            CopiedFiles = copied,
            StripMetrics = stripMetrics,
            Score = score
        };
    }

    private static CandidateScore ScoreCandidate(
        string id,
        SourceSummary source,
        IReadOnlyDictionary<string, StripMetrics> strips,
        bool isComplete,
        bool hasR,
        bool hasS,
        bool isNegativeSource)
    {
        double score = 0;
        var reasons = new List<string>();
        if (isComplete)
        {
            score += 25;
            reasons.Add("complete five-strip R/S package");
        }
        else
        {
            score += hasR ? 6 : 0;
            score += hasS ? 12 : 0;
            reasons.Add("incomplete candidate; useful only as partial reference");
        }

        if (source.MountedKeywordHit)
        {
            score += 14;
            reasons.Add("path/source suggests mounted or cavalry reference");
        }
        if (source.SpearKeywordHit)
        {
            score += 10;
            reasons.Add("path/source suggests spear/polearm reference");
        }
        if (source.TrueReferenceRoot)
        {
            score += 10;
            reasons.Add("comes from local real-reference export area");
        }
        if (source.SelectedFormat)
        {
            score += 8;
            reasons.Add("already selected previously as format candidate");
        }
        if (source.OldSunCeContamination)
        {
            score -= 60;
            reasons.Add("old S67/SunCe contamination risk; cannot be a positive spear-cavalry sample");
        }

        if (strips.TryGetValue("mov", out var mov))
        {
            score += Math.Min(10, mov.AverageColorCount / 4.0);
            score += Math.Min(8, mov.AverageContourTurns / 8.0);
            if (mov.AverageFlatBlockRisk > 12) score -= 6;
        }
        if (strips.TryGetValue("atk", out var atk))
        {
            score += Math.Min(12, atk.AverageColorCount / 3.5);
            score += Math.Min(12, atk.AverageContourTurns / 7.0);
            score += Math.Min(10, atk.FrameBboxDeltaAverage / 1.8);
            score += Math.Min(10, atk.BrightClusterAverage * 1.5);
            if (atk.AverageFlatBlockRisk > 16) score -= 8;
            if (atk.BrightClusterAverage > 5.5)
            {
                score -= 10;
                reasons.Add("high bright-cluster count may indicate sword arc, double weapon, or noisy effects");
            }
        }
        if (strips.TryGetValue("spc", out var spc))
        {
            score += Math.Min(6, spc.FrameBboxDeltaAverage / 2.0);
            if (spc.BrightClusterAverage > 6) score -= 4;
        }

        if (source.NegativeKeywordHit || isNegativeSource)
        {
            score -= 35;
            reasons.Add("marked as negative, failed, smoke, procedural, or old SunCe attempt");
        }
        if (id.Contains("S67", StringComparison.OrdinalIgnoreCase))
        {
            score -= 12;
            reasons.Add("S67 old SunCe contamination risk");
        }

        var machineClass = score >= 58 && isComplete && !isNegativeSource && !source.NegativeKeywordHit && !source.OldSunCeContamination
            ? "positive_candidate"
            : score >= 32 && !isNegativeSource && !source.NegativeKeywordHit && !source.OldSunCeContamination
                ? "partial_reference"
                : "negative_case";
        return new CandidateScore
        {
            Total = Math.Round(score, 2),
            MachineClass = machineClass,
            Reasons = reasons
        };
    }

    private static IReadOnlyList<string> WriteReports(
        string outputRoot,
        string reportsRoot,
        string annotationsRoot,
        string unitType,
        IReadOnlyList<string> referenceRoots,
        IReadOnlyList<string> negativeRoots,
        IReadOnlyList<LearningCandidate> ranked,
        IReadOnlyList<LearningCandidate> top,
        IReadOnlyList<string> warnings)
    {
        var reports = new List<string>();
        var corpusPath = Path.Combine(reportsRoot, "corpus_summary.json");
        File.WriteAllText(corpusPath, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            UnitType = unitType,
            OutputRoot = outputRoot,
            ReferenceRoots = referenceRoots,
            NegativeRoots = negativeRoots,
            CandidateCount = ranked.Count,
            CompleteCandidateCount = ranked.Count(candidate => candidate.IsComplete),
            RGroupCount = ranked.Count(candidate => candidate.HasR),
            SGroupCount = ranked.Count(candidate => candidate.HasS),
            MachineClassCounts = ranked.GroupBy(candidate => candidate.Score.MachineClass).ToDictionary(group => group.Key, group => group.Count()),
            Warnings = warnings,
            SamplingNote = "MVP normalizes existing local R/S strip exports and any already-exported BMP strip folders found under the configured roots. Base game roots are recorded as expansion roots; no table-driven bulk export or game-resource write is performed by this service.",
            SafetyNote = "Sample learning only. No game resources or sample index entries were written."
        }, JsonOptions));
        reports.Add(corpusPath);

        var rankingPath = Path.Combine(reportsRoot, "candidate_ranking.md");
        File.WriteAllText(rankingPath, BuildRankingMarkdown(outputRoot, ranked, top));
        reports.Add(rankingPath);

        var grammarPath = Path.Combine(reportsRoot, "style_grammar_findings.md");
        File.WriteAllText(grammarPath, BuildGrammarMarkdown(ranked));
        reports.Add(grammarPath);

        var negativePath = Path.Combine(reportsRoot, "negative_case_lessons.md");
        File.WriteAllText(negativePath, BuildNegativeMarkdown(ranked));
        reports.Add(negativePath);

        var restartPath = Path.Combine(reportsRoot, "sunce_restart_requirements.md");
        File.WriteAllText(restartPath, BuildSunCeRestartMarkdown(top, ranked));
        reports.Add(restartPath);

        var annotationsCsv = Path.Combine(annotationsRoot, "candidate_annotations_template.csv");
        File.WriteAllText(annotationsCsv, BuildAnnotationsCsv(top), Encoding.UTF8);
        reports.Add(annotationsCsv);

        var annotationsJson = Path.Combine(annotationsRoot, "candidate_annotations_template.json");
        File.WriteAllText(annotationsJson, JsonSerializer.Serialize(top.Select(candidate => new
        {
            candidate.Id,
            candidate.SourceDirectory,
            candidate.Score.Total,
            candidate.Score.MachineClass,
            visualClass = "",
            mountedReadability = "",
            singleSpearReadability = "",
            ccz65Style = "",
            actionGrammar = "",
            contamination = "",
            reuseRole = "",
            humanNotes = ""
        }), JsonOptions));
        reports.Add(annotationsJson);

        return reports;
    }

    private static string BuildRankingMarkdown(string outputRoot, IReadOnlyList<LearningCandidate> ranked, IReadOnlyList<LearningCandidate> top)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Spear Cavalry R/S Candidate Ranking");
        builder.AppendLine();
        builder.AppendLine("Machine ranking is a triage aid only. Human visual review of the x6 contact sheets is required before any candidate can become a positive sample.");
        builder.AppendLine();
        builder.AppendLine("| Rank | Candidate | Score | Class | Complete | Source | Key reasons |");
        builder.AppendLine("| --- | --- | ---: | --- | --- | --- | --- |");
        var rank = 1;
        foreach (var candidate in ranked.Take(40))
        {
            builder.Append("| ");
            builder.Append(rank.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | `");
            builder.Append(candidate.Id);
            builder.Append("` | ");
            builder.Append(candidate.Score.Total.ToString("0.##", CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(candidate.Score.MachineClass);
            builder.Append(" | ");
            builder.Append(candidate.IsComplete ? "yes" : "no");
            builder.Append(" | `");
            builder.Append(EscapePipe(candidate.SourceSummary.DisplayName));
            builder.Append("` | ");
            builder.Append(EscapePipe(string.Join("; ", candidate.Score.Reasons.Take(4))));
            builder.AppendLine(" |");
            rank++;
        }

        builder.AppendLine();
        builder.AppendLine("## Top Review Set");
        foreach (var candidate in top)
        {
            builder.AppendLine();
            builder.AppendLine($"- `{candidate.Id}` score `{candidate.Score.Total:0.##}` class `{candidate.Score.MachineClass}`");
            builder.AppendLine($"  - source: `{candidate.SourceSummary.DisplayName}`");
            foreach (var sheet in candidate.ContactSheets.Where(path => path.EndsWith("candidate_review_sheet_x6.png", StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine($"  - review sheet: `{ToReportRelativePath(outputRoot, sheet)}`");
            }
        }

        return builder.ToString();
    }

    private static string BuildGrammarMarkdown(IReadOnlyList<LearningCandidate> ranked)
    {
        var positives = ranked.Where(candidate => candidate.Score.MachineClass == "positive_candidate").Take(8).ToArray();
        var partials = ranked.Where(candidate => candidate.Score.MachineClass == "partial_reference").Take(8).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("# Spear Cavalry Style Grammar Findings");
        builder.AppendLine();
        builder.AppendLine("This file summarizes machine-observed tendencies from local 6.5 R/S exports. It is not a final art rulebook until the candidate contact sheets are manually reviewed.");
        builder.AppendLine();
        builder.AppendLine("## Current Machine Findings");
        builder.AppendLine();
        builder.AppendLine("- Complete five-strip candidates are preferred over isolated S or R exports, but partial S references can still teach attack rhythm.");
        builder.AppendLine("- Strong candidates usually combine higher foreground color counts with irregular contours; low-color flat blocks are treated as sticker/procedural risk.");
        builder.AppendLine("- Attack strips need frame-to-frame bbox and bright-tip movement; standing body plus detached light is treated as weak action grammar.");
        builder.AppendLine("- Excess bright clusters are only a risk signal: they may be sword arcs, second weapons, white horses, gold armor, or valid spear effects. Human review decides.");
        builder.AppendLine();
        builder.AppendLine("## Machine Positive Candidates");
        foreach (var candidate in positives)
        {
            builder.AppendLine($"- `{candidate.Id}`: score `{candidate.Score.Total:0.##}`, source `{candidate.SourceSummary.DisplayName}`.");
        }
        if (positives.Length == 0) builder.AppendLine("- None yet. Expand sampling or relax candidate filters before restarting Sun Ce.");
        builder.AppendLine();
        builder.AppendLine("## Partial References");
        foreach (var candidate in partials)
        {
            builder.AppendLine($"- `{candidate.Id}`: score `{candidate.Score.Total:0.##}`, missing `{string.Join(", ", candidate.Missing)}`.");
        }

        return builder.ToString();
    }

    private static string BuildNegativeMarkdown(IReadOnlyList<LearningCandidate> ranked)
    {
        var negatives = ranked.Where(candidate => candidate.Score.MachineClass == "negative_case").Take(20).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("# Negative Case Lessons");
        builder.AppendLine();
        builder.AppendLine("- Old SunCe local pixel editor attempts are negative evidence only: they show reference-overpaint, weapon residue, face pollution, and over-aggressive erasure risks.");
        builder.AppendLine("- Smoke/procedural packages can test pipelines but must not become style references.");
        builder.AppendLine("- Incomplete S-only or R-only candidates can be partial references, but cannot be promoted as reusable positive samples.");
        builder.AppendLine("- Machine metrics are allowed to flag sword arcs, double weapons, flat blocks, and face risks; final classification must be human visual review.");
        builder.AppendLine();
        builder.AppendLine("## Detected Negative / Low-Priority Candidates");
        foreach (var candidate in negatives)
        {
            builder.AppendLine($"- `{candidate.Id}` score `{candidate.Score.Total:0.##}` source `{candidate.SourceSummary.DisplayName}` reasons: {string.Join("; ", candidate.Score.Reasons)}");
        }

        return builder.ToString();
    }

    private static string BuildSunCeRestartMarkdown(IReadOnlyList<LearningCandidate> top, IReadOnlyList<LearningCandidate> ranked)
    {
        var positives = top.Where(candidate => candidate.Score.MachineClass == "positive_candidate").Take(3).ToArray();
        var partials = ranked.Where(candidate => candidate.Score.MachineClass == "partial_reference").Take(3).ToArray();
        var negatives = ranked.Where(candidate => candidate.Score.MachineClass == "negative_case").Take(3).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("# SunCe Restart Requirements");
        builder.AppendLine();
        builder.AppendLine("Do not restart Sun Ce drawing until this file is reviewed together with the top x6 contact sheets.");
        builder.AppendLine();
        builder.AppendLine("## Required References Before Drawing");
        builder.AppendLine();
        builder.AppendLine("- Pick at least two human-approved positive mounted/spear references from the top review set.");
        builder.AppendLine("- Pick at least one negative case as an explicit do-not-copy constraint.");
        builder.AppendLine("- Use references by role: one for mounted/action grammar, one for CCZ 6.5 outline/palette density, one for spear-tip trajectory if available.");
        builder.AppendLine("- R100/S90 may remain a partial reference, but it must not be the only drawing source.");
        builder.AppendLine();
        builder.AppendLine("## Current Machine Suggestions");
        builder.AppendLine();
        builder.AppendLine("Positive candidates to inspect:");
        foreach (var candidate in positives)
        {
            builder.AppendLine($"- `{candidate.Id}` from `{candidate.SourceSummary.DisplayName}`");
        }
        if (positives.Length == 0) builder.AppendLine("- None yet. Expand sampling before drawing.");
        builder.AppendLine();
        builder.AppendLine("Partial references to inspect:");
        foreach (var candidate in partials)
        {
            builder.AppendLine($"- `{candidate.Id}` from `{candidate.SourceSummary.DisplayName}`");
        }
        builder.AppendLine();
        builder.AppendLine("Negative constraints:");
        foreach (var candidate in negatives)
        {
            builder.AppendLine($"- `{candidate.Id}` from `{candidate.SourceSummary.DisplayName}`");
        }
        builder.AppendLine();
        builder.AppendLine("## Drawing Gate");
        builder.AppendLine();
        builder.AppendLine("The next Sun Ce package must cite the chosen positive/partial/negative sample ids in its manifest before any frame editing starts.");
        return builder.ToString();
    }

    private static string BuildAnnotationsCsv(IReadOnlyList<LearningCandidate> top)
    {
        var builder = new StringBuilder();
        builder.AppendLine("id,score,machineClass,visualClass,mountedReadability,singleSpearReadability,ccz65Style,actionGrammar,contamination,reuseRole,humanNotes,sourceDirectory");
        foreach (var candidate in top)
        {
            builder.Append('"').Append(candidate.Id).Append("\",");
            builder.Append(candidate.Score.Total.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
            builder.Append('"').Append(candidate.Score.MachineClass).Append("\",");
            builder.Append(",,,,,,,,");
            builder.Append('"').Append(candidate.SourceDirectory.Replace("\"", "\"\"")).AppendLine("\"");
        }
        return builder.ToString();
    }

    private static void WriteSelectionPointer(LearningCandidate candidate, string root)
    {
        var directory = Path.Combine(root, candidate.Id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "README.md"), $"""
# {candidate.Id}

Machine class: `{candidate.Score.MachineClass}`

Score: `{candidate.Score.Total.ToString("0.##", CultureInfo.InvariantCulture)}`

Source: `{candidate.SourceDirectory}`

This is a pointer for human review. Do not promote this candidate into `local_sample_index.json` until visual acceptance, format/type validation, and MCP preview are complete.
""");
    }

    private static Dictionary<string, string> ResolveStripFiles(string directory)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in StripSpecs)
        {
            var candidates = new[]
            {
                Path.Combine(directory, spec.FileName),
                Path.Combine(directory, spec.Group, spec.FileName),
                Path.Combine(directory, "materials", spec.Group, spec.FileName),
                Path.Combine(directory, "refs", spec.FileName)
            };
            var found = candidates.FirstOrDefault(File.Exists);
            if (found != null) result[spec.Role] = found;
        }
        return result;
    }

    private static StripMetrics AnalyzeStrip(Bitmap bitmap, RsStripSpec spec)
    {
        var frameMetrics = new List<FrameMetrics>();
        for (var frame = 0; frame < spec.FrameCount; frame++)
        {
            frameMetrics.Add(AnalyzeFrame(bitmap, spec, frame));
        }
        var bboxDeltas = new List<double>();
        for (var i = 1; i < frameMetrics.Count; i++)
        {
            bboxDeltas.Add(BboxDistance(frameMetrics[i - 1].BoundingBox, frameMetrics[i].BoundingBox));
        }

        return new StripMetrics
        {
            Role = spec.Role,
            Width = bitmap.Width,
            Height = bitmap.Height,
            ExpectedWidth = spec.Width,
            ExpectedHeight = spec.Height,
            SizePassed = bitmap.Width == spec.Width && bitmap.Height == spec.Height,
            FrameCount = spec.FrameCount,
            EmptyFrameCount = frameMetrics.Count(frame => frame.NonMagentaPixelCount == 0),
            NearMagentaNonStrictPixelCount = frameMetrics.Sum(frame => frame.NearMagentaNonStrictPixelCount),
            AverageColorCount = Math.Round(frameMetrics.AverageOrZero(frame => frame.UniqueForegroundColorCount), 2),
            AverageContourTurns = Math.Round(frameMetrics.AverageOrZero(frame => frame.ContourTurns), 2),
            AverageFlatBlockRisk = Math.Round(frameMetrics.AverageOrZero(frame => frame.FlatBlockRisk), 2),
            BrightClusterAverage = Math.Round(frameMetrics.AverageOrZero(frame => frame.BrightClusters), 2),
            FrameBboxDeltaAverage = Math.Round(bboxDeltas.Count == 0 ? 0 : bboxDeltas.Average(), 2),
            FrameMetrics = frameMetrics
        };
    }

    private static FrameMetrics AnalyzeFrame(Bitmap bitmap, RsStripSpec spec, int frame)
    {
        var top = frame * spec.FrameHeight;
        var colors = new HashSet<int>();
        var nonMagenta = 0;
        var nearNonStrict = 0;
        var brightClusters = 0;
        var minX = spec.FrameWidth;
        var minY = spec.FrameHeight;
        var maxX = -1;
        var maxY = -1;
        var foreground = new bool[spec.FrameWidth, spec.FrameHeight];
        for (var y = 0; y < spec.FrameHeight; y++)
        {
            for (var x = 0; x < spec.FrameWidth; x++)
            {
                var color = bitmap.GetPixel(x, top + y);
                if (IsStrictMagenta(color)) continue;
                if (IsNearMagenta(color)) nearNonStrict++;
                if (IsMagenta(color)) continue;
                nonMagenta++;
                colors.Add(Color.FromArgb(255, color.R, color.G, color.B).ToArgb());
                foreground[x, y] = true;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        var contourTurns = CountContourTurns(foreground, spec.FrameWidth, spec.FrameHeight);
        var flatRisk = CountFlatBlockRisk(bitmap, spec, top);
        brightClusters = CountBrightClusters(bitmap, spec, top);
        return new FrameMetrics
        {
            FrameIndex = frame,
            NonMagentaPixelCount = nonMagenta,
            UniqueForegroundColorCount = colors.Count,
            NearMagentaNonStrictPixelCount = nearNonStrict,
            ContourTurns = contourTurns,
            FlatBlockRisk = flatRisk,
            BrightClusters = brightClusters,
            BoundingBox = nonMagenta == 0 ? "empty" : $"{minX},{minY},{maxX - minX + 1},{maxY - minY + 1}"
        };
    }

    private static int CountContourTurns(bool[,] foreground, int width, int height)
    {
        var turns = 0;
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                if (!foreground[x, y]) continue;
                var mask = 0;
                if (foreground[x, y - 1]) mask |= 1;
                if (foreground[x + 1, y]) mask |= 2;
                if (foreground[x, y + 1]) mask |= 4;
                if (foreground[x - 1, y]) mask |= 8;
                if (mask is 3 or 6 or 9 or 12 or 5 or 10) turns++;
            }
        }
        return turns;
    }

    private static int CountFlatBlockRisk(Bitmap bitmap, RsStripSpec spec, int top)
    {
        var risk = 0;
        for (var y = 0; y <= spec.FrameHeight - 6; y++)
        {
            for (var x = 0; x <= spec.FrameWidth - 6; x++)
            {
                var first = bitmap.GetPixel(x, top + y);
                if (IsMagenta(first)) continue;
                var same = true;
                for (var yy = 0; yy < 6 && same; yy++)
                {
                    for (var xx = 0; xx < 6; xx++)
                    {
                        if (bitmap.GetPixel(x + xx, top + y + yy).ToArgb() != first.ToArgb())
                        {
                            same = false;
                            break;
                        }
                    }
                }
                if (same) risk++;
            }
        }
        return risk;
    }

    private static int CountBrightClusters(Bitmap bitmap, RsStripSpec spec, int top)
    {
        var visited = new bool[spec.FrameWidth, spec.FrameHeight];
        var clusters = 0;
        for (var y = 0; y < spec.FrameHeight; y++)
        {
            for (var x = 0; x < spec.FrameWidth; x++)
            {
                if (visited[x, y]) continue;
                var color = bitmap.GetPixel(x, top + y);
                if (!IsBrightWeaponLike(color))
                {
                    visited[x, y] = true;
                    continue;
                }
                clusters++;
                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue((x, y));
                visited[x, y] = true;
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    foreach (var (nx, ny) in new[] { (cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1) })
                    {
                        if (nx < 0 || ny < 0 || nx >= spec.FrameWidth || ny >= spec.FrameHeight || visited[nx, ny]) continue;
                        visited[nx, ny] = true;
                        if (IsBrightWeaponLike(bitmap.GetPixel(nx, top + ny))) queue.Enqueue((nx, ny));
                    }
                }
            }
        }
        return clusters;
    }

    private static Bitmap BuildContactSheet(Bitmap strip, RsStripSpec spec, int scale)
    {
        var columns = spec.Role == "atk" ? 4 : 5;
        var rows = (int)Math.Ceiling(spec.FrameCount / (double)columns);
        var cellWidth = spec.FrameWidth * scale;
        var cellHeight = spec.FrameHeight * scale;
        var sheet = new Bitmap(columns * cellWidth, rows * cellHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(sheet);
        graphics.Clear(Color.FromArgb(255, 36, 36, 36));
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        using var pen = new Pen(Color.FromArgb(255, 80, 80, 80));
        for (var frame = 0; frame < spec.FrameCount; frame++)
        {
            var col = frame % columns;
            var row = frame / columns;
            var dest = new Rectangle(col * cellWidth, row * cellHeight, cellWidth, cellHeight);
            var src = new Rectangle(0, frame * spec.FrameHeight, spec.FrameWidth, spec.FrameHeight);
            graphics.DrawImage(strip, dest, src, GraphicsUnit.Pixel);
            graphics.DrawRectangle(pen, dest);
        }
        return sheet;
    }

    private static Bitmap BuildReviewSheet(IReadOnlyDictionary<string, string> copiedFiles, int scale)
    {
        var parts = new List<(string Role, Bitmap Sheet)>();
        foreach (var spec in StripSpecs)
        {
            if (!copiedFiles.TryGetValue(spec.Role, out var path)) continue;
            using var bitmap = LoadBitmap(path);
            parts.Add((spec.Role, BuildContactSheet(bitmap, spec, scale)));
        }
        var width = parts.Max(part => part.Sheet.Width);
        var height = parts.Sum(part => part.Sheet.Height + 20);
        var review = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(review);
        graphics.Clear(Color.FromArgb(255, 24, 24, 24));
        using var font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        var y = 0;
        foreach (var part in parts)
        {
            graphics.DrawString(part.Role, font, brush, 4, y + 2);
            y += 20;
            graphics.DrawImage(part.Sheet, 0, y);
            y += part.Sheet.Height;
            part.Sheet.Dispose();
        }
        return review;
    }

    private static SourceSummary BuildSourceSummary(string sourceDirectory)
    {
        var path = sourceDirectory.Replace('\\', '/');
        var lower = path.ToLowerInvariant();
        return new SourceSummary
        {
            DisplayName = Path.GetFileName(sourceDirectory),
            MountedKeywordHit = ContainsAny(lower, "骑", "mounted", "cavalry", "horse", "s90", "s84", "s85", "s99", "s110", "s112"),
            SpearKeywordHit = ContainsAny(lower, "枪", "矛", "戟", "spear", "lance", "pole", "zhaoyun", "machao", "zhangfei", "taishici"),
            NegativeKeywordHit = ContainsAny(lower, "failed", "smoke", "procedural", "generic", "sunce_localpixeleditor", "sunce_mcp", "v3_failed", "失败"),
            OldSunCeContamination = ContainsAny(lower, "row0_r100_s67", "/s67", "\\s67", "_s67_", "_s67", "s67_"),
            TrueReferenceRoot = ContainsAny(lower, "_reference_samples", "jiaqiang65", "huanwang", "dongwu"),
            SelectedFormat = ContainsAny(lower, "selected_format")
        };
    }

    private static double BboxDistance(string a, string b)
    {
        if (!TryParseBbox(a, out var ax, out var ay, out var aw, out var ah) ||
            !TryParseBbox(b, out var bx, out var by, out var bw, out var bh))
        {
            return 0;
        }
        return Math.Abs(ax - bx) + Math.Abs(ay - by) + Math.Abs(aw - bw) + Math.Abs(ah - bh);
    }

    private static bool TryParseBbox(string text, out int x, out int y, out int w, out int h)
    {
        x = y = w = h = 0;
        var parts = text.Split(',');
        return parts.Length == 4 &&
               int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) &&
               int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y) &&
               int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out w) &&
               int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out h);
    }

    private static string BuildCandidateId(string sourceDirectory)
    {
        var parts = sourceDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .TakeLast(2)
            .Select(NormalizeId)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        var id = string.Join("__", parts);
        if (id.Length > 56) id = id[^56..];
        return id + "__" + HashShort(sourceDirectory);
    }

    private static string NormalizeId(string value)
    {
        var normalized = Regex.Replace(value.Trim(), @"[^\p{L}\p{Nd}_\-]+", "_");
        normalized = Regex.Replace(normalized, "_{2,}", "_").Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "candidate" : normalized;
    }

    private static string ResolvePath(CczProject project, string path)
        => Path.IsPathFullyQualified(path) ? path : Path.Combine(project.WorkspaceRoot, path);

    private static bool LooksLikeMountedSpearPath(string path)
    {
        var lower = path.ToLowerInvariant();
        return ContainsAny(lower, "骑", "枪", "矛", "戟", "spear", "lance", "cavalry", "mounted", "horse", "zhaoyun", "machao", "zhangfei", "taishici", "s90", "s84", "s85", "s99", "s110", "s112", "r100", "r99");
    }

    private static bool LooksLikeNegativePath(string path)
    {
        var lower = path.ToLowerInvariant();
        return ContainsAny(lower, "sunce_localpixeleditor", "sunce_mcp", "failed", "smoke", "procedural", "generic", "v3_failed", "失败");
    }

    private static IReadOnlyList<LearningCandidate> BuildTopReviewSet(IReadOnlyList<LearningCandidate> ranked, int topCount)
    {
        var selected = new List<LearningCandidate>();
        AddUnique(selected, ranked.Where(candidate => candidate.Score.MachineClass == "positive_candidate").Take(Math.Max(3, topCount / 3)));
        AddUnique(selected, ranked.Where(candidate => candidate.Score.MachineClass == "partial_reference").Take(Math.Max(3, topCount / 3)));
        AddUnique(selected, ranked.Where(candidate => candidate.Score.MachineClass == "negative_case").Take(Math.Max(3, topCount / 3)));
        AddUnique(selected, ranked);
        return selected.Take(topCount).ToArray();
    }

    private static void AddUnique(List<LearningCandidate> selected, IEnumerable<LearningCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (selected.Any(existing => existing.Id.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase))) continue;
            selected.Add(candidate);
        }
    }

    private static bool IsNestedMaterialMirror(string directory)
    {
        var name = Path.GetFileName(directory);
        if (!name.Equals("materials", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("r_actor", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("s_unit", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parent = Directory.GetParent(directory);
        if (parent == null) return false;
        if (ResolveStripFiles(parent.FullName).Count > 0) return true;
        if (name is "r_actor" or "s_unit")
        {
            var grandParent = parent.Parent;
            return grandParent != null && ResolveStripFiles(grandParent.FullName).Count > 0;
        }
        return false;
    }

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static Bitmap LoadBitmap(string path)
    {
        using var image = Image.FromFile(path);
        return new Bitmap(image);
    }

    private static bool IsStrictMagenta(Color color)
        => color.R == 255 && color.G == 0 && color.B == 255;

    private static bool IsNearMagenta(Color color)
        => color.R >= 235 && color.G <= 35 && color.B >= 235;

    private static bool IsMagenta(Color color)
        => color.A == 0 || IsNearMagenta(color);

    private static bool IsBrightWeaponLike(Color color)
        => !IsMagenta(color) && color.R >= 205 && color.G >= 165 && color.B >= 70;

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashShort(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..10];

    private static string EscapePipe(string value)
        => value.Replace("|", "\\|");

    private static string ToReportRelativePath(string root, string path)
    {
        try
        {
            return Path.GetRelativePath(root, path).Replace('\\', '/');
        }
        catch
        {
            return path;
        }
    }

    private sealed record RsStripSpec(string Role, string Group, string FileName, int Width, int Height, int FrameWidth, int FrameHeight)
    {
        public int FrameCount => Height / FrameHeight;
    }

    private sealed class DiscoveredCandidate
    {
        public string SourceDirectory { get; init; } = string.Empty;
        public IReadOnlyDictionary<string, string> StripFiles { get; init; } = new Dictionary<string, string>();
        public bool IsNegativeSource { get; init; }
        public string SortKey { get; init; } = string.Empty;
    }

    private sealed class LearningCandidate
    {
        public string Id { get; init; } = string.Empty;
        public string SourceDirectory { get; init; } = string.Empty;
        public SourceSummary SourceSummary { get; init; } = new();
        public string CandidateRoot { get; init; } = string.Empty;
        public string MetricsPath { get; init; } = string.Empty;
        public IReadOnlyList<string> ContactSheets { get; init; } = Array.Empty<string>();
        public bool IsNegativeSource { get; init; }
        public bool IsComplete { get; init; }
        public bool HasR { get; init; }
        public bool HasS { get; init; }
        public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, string> CopiedFiles { get; init; } = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, StripMetrics> StripMetrics { get; init; } = new Dictionary<string, StripMetrics>();
        public CandidateScore Score { get; init; } = new();
    }

    private sealed class CandidateMetrics
    {
        public string Id { get; init; } = string.Empty;
        public string UnitType { get; init; } = string.Empty;
        public string SourceDirectory { get; init; } = string.Empty;
        public SourceSummary SourceSummary { get; init; } = new();
        public bool IsNegativeSource { get; init; }
        public bool IsComplete { get; init; }
        public bool HasR { get; init; }
        public bool HasS { get; init; }
        public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, string> CopiedFiles { get; init; } = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, StripMetrics> StripMetrics { get; init; } = new Dictionary<string, StripMetrics>();
        public CandidateScore Score { get; init; } = new();
        public IReadOnlyList<string> ContactSheets { get; init; } = Array.Empty<string>();
    }

    private sealed class SourceSummary
    {
        public string DisplayName { get; init; } = string.Empty;
        public bool MountedKeywordHit { get; init; }
        public bool SpearKeywordHit { get; init; }
        public bool NegativeKeywordHit { get; init; }
        public bool OldSunCeContamination { get; init; }
        public bool TrueReferenceRoot { get; init; }
        public bool SelectedFormat { get; init; }
    }

    private sealed class CandidateScore
    {
        public double Total { get; init; }
        public string MachineClass { get; init; } = "negative_case";
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    }

    private sealed class StripMetrics
    {
        public string Role { get; init; } = string.Empty;
        public int Width { get; init; }
        public int Height { get; init; }
        public int ExpectedWidth { get; init; }
        public int ExpectedHeight { get; init; }
        public bool SizePassed { get; init; }
        public int FrameCount { get; init; }
        public int EmptyFrameCount { get; init; }
        public int NearMagentaNonStrictPixelCount { get; init; }
        public double AverageColorCount { get; init; }
        public double AverageContourTurns { get; init; }
        public double AverageFlatBlockRisk { get; init; }
        public double BrightClusterAverage { get; init; }
        public double FrameBboxDeltaAverage { get; init; }
        public IReadOnlyList<FrameMetrics> FrameMetrics { get; init; } = Array.Empty<FrameMetrics>();
    }

    private sealed class FrameMetrics
    {
        public int FrameIndex { get; init; }
        public int NonMagentaPixelCount { get; init; }
        public int UniqueForegroundColorCount { get; init; }
        public int NearMagentaNonStrictPixelCount { get; init; }
        public int ContourTurns { get; init; }
        public int FlatBlockRisk { get; init; }
        public int BrightClusters { get; init; }
        public string BoundingBox { get; init; } = string.Empty;
    }
}

internal static class RsPixelSampleLearningEnumerableExtensions
{
    public static double AverageOrZero<T>(this IEnumerable<T> source, Func<T, double> selector)
    {
        var values = source.Select(selector).ToArray();
        return values.Length == 0 ? 0 : values.Average();
    }
}
