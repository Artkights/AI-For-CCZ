using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// Project-side battlefield unit review records and placement drafts.
/// These JSON records never write game files.
/// </summary>
public sealed class BattlefieldUnitReviewService
{
    private const string SafetyNoteText = "项目侧战场核对/布阵草稿：保存到 CCZModStudio_Notes，不写入游戏文件。";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string GetStorePath(CczProject project)
    {
        var root = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Notes");
        return Path.Combine(root, $"{MakeSafeFileName(project.Name)}_BattlefieldUnitReviews.json");
    }

    public IReadOnlyList<BattlefieldUnitReview> Load(CczProject project)
    {
        var path = GetStorePath(project);
        if (!File.Exists(path)) return Array.Empty<BattlefieldUnitReview>();

        try
        {
            return (JsonSerializer.Deserialize<List<BattlefieldUnitReview>>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                       ?? new List<BattlefieldUnitReview>())
                .Where(x => !string.IsNullOrWhiteSpace(x.TargetKey))
                .Select(Normalize)
                .OrderByDescending(x => x.UpdatedAtText, StringComparer.Ordinal)
                .ThenBy(x => x.ScenarioFileName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.TargetKey, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("战场出场核对 JSON 解析失败，请先备份并检查文件：" + path, ex);
        }
    }

    public IReadOnlyList<BattlefieldPlacedUnit> LoadPlacements(CczProject project, BattlefieldEditorDocument document)
    {
        return Load(project)
            .Where(x => x.IsPlacement && x.ScenarioFileName.Equals(document.Scenario.FileName, StringComparison.OrdinalIgnoreCase))
            .Where(x => !IsScriptBackedPlacementReview(x))
            .OrderBy(x => x.GridY)
            .ThenBy(x => x.GridX)
            .ThenBy(x => x.PersonId)
            .Select(x => new BattlefieldPlacedUnit
            {
                TargetKey = x.TargetKey,
                PersonId = x.PersonId,
                Name = x.UnitName,
                JobId = x.JobId,
                JobName = x.JobName,
                SImageId = x.SImageId,
                RImageId = x.RImageId,
                Faction = string.IsNullOrWhiteSpace(x.Faction) ? "我军" : x.Faction,
                LevelOffset = x.LevelOffset,
                LevelMode = string.IsNullOrWhiteSpace(x.LevelMode) ? "初级" : x.LevelMode,
                AiMode = string.IsNullOrWhiteSpace(x.AiMode) ? "被动" : x.AiMode,
                Hidden = x.Hidden,
                Reinforcement = x.Reinforcement,
                Direction = string.IsNullOrWhiteSpace(x.Direction) ? "下" : x.Direction,
                GridX = x.GridX,
                GridY = x.GridY,
                Source = "布阵草稿",
                PlacementNote = x.ReviewNote
            })
            .ToList();
    }

    public void Apply(CczProject project, BattlefieldEditorDocument document)
    {
        var reviews = Load(project)
            .Where(x => !x.IsPlacement)
            .Where(x => x.ScenarioFileName.Equals(document.Scenario.FileName, StringComparison.OrdinalIgnoreCase))
            .ToDictionaryFirstByKey(x => x.TargetKey, x => x, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in document.UnitCandidates)
        {
            if (!reviews.TryGetValue(candidate.TargetKey, out var review)) continue;
            candidate.ReviewStatus = review.ReviewStatus;
            candidate.ReviewNote = review.ReviewNote;
        }
    }

    public string Save(CczProject project, BattlefieldEditorDocument document, IEnumerable<BattlefieldUnitCandidate> candidates)
        => Save(project, document, candidates, placements: null);

    public string Save(
        CczProject project,
        BattlefieldEditorDocument document,
        IEnumerable<BattlefieldUnitCandidate> candidates,
        IEnumerable<BattlefieldPlacedUnit>? placements)
    {
        var path = GetStorePath(project);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var all = Load(project).Select(Clone).ToList();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.TargetKey)) continue;
            var existing = all.FirstOrDefault(x => !x.IsPlacement &&
                                                   x.TargetKey.Equals(candidate.TargetKey, StringComparison.OrdinalIgnoreCase) &&
                                                   x.ScenarioFileName.Equals(document.Scenario.FileName, StringComparison.OrdinalIgnoreCase));
            var empty = string.IsNullOrWhiteSpace(candidate.ReviewStatus) && string.IsNullOrWhiteSpace(candidate.ReviewNote);
            if (empty)
            {
                if (existing != null) all.Remove(existing);
                continue;
            }

            if (existing == null)
            {
                existing = new BattlefieldUnitReview
                {
                    TargetKey = candidate.TargetKey,
                    ScenarioFileName = document.Scenario.FileName
                };
                all.Add(existing);
            }

            existing.SourceCommand = candidate.SourceCommand;
            existing.SceneSection = candidate.SceneSection;
            existing.OffsetHex = candidate.OffsetHex;
            existing.ReviewStatus = candidate.ReviewStatus.Trim();
            existing.ReviewNote = candidate.ReviewNote.Trim();
            existing.UpdatedAtText = now;
            existing.SafetyNote = SafetyNoteText;
        }

        if (placements != null)
        {
            all.RemoveAll(x => x.IsPlacement &&
                               x.ScenarioFileName.Equals(document.Scenario.FileName, StringComparison.OrdinalIgnoreCase));
            foreach (var placement in placements)
            {
                if (placement.GridX < 0 || placement.GridY < 0) continue;
                if (BattlefieldDeploymentWriteService.IsScriptPlacementWritable(placement)) continue;
                all.Add(new BattlefieldUnitReview
                {
                    TargetKey = string.IsNullOrWhiteSpace(placement.TargetKey)
                        ? BuildPlacementTargetKey(document.Scenario.FileName, placement)
                        : placement.TargetKey,
                    ScenarioFileName = document.Scenario.FileName,
                    SourceCommand = placement.Source,
                    SceneSection = $"Grid ({placement.GridX},{placement.GridY})",
                    OffsetHex = string.Empty,
                    ReviewStatus = "地图摆放",
                    ReviewNote = placement.PlacementNote.Trim(),
                    UpdatedAtText = now,
                    IsPlacement = true,
                    PersonId = placement.PersonId,
                    UnitName = placement.Name,
                    JobId = placement.JobId,
                    JobName = placement.JobName,
                    SImageId = placement.SImageId,
                    RImageId = placement.RImageId,
                    Faction = placement.Faction,
                    LevelOffset = placement.LevelOffset,
                    LevelMode = placement.LevelMode,
                    AiMode = placement.AiMode,
                    Hidden = placement.Hidden,
                    Reinforcement = placement.Reinforcement,
                    Direction = placement.Direction,
                    GridX = placement.GridX,
                    GridY = placement.GridY,
                    SafetyNote = SafetyNoteText
                });
            }
        }

        if (File.Exists(path))
        {
            File.Copy(path, BuildUniqueBackupPath(path), overwrite: false);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(all.Select(Normalize).OrderByDescending(x => x.UpdatedAtText, StringComparer.Ordinal).ToList(), JsonOptions), Encoding.UTF8);
        return path;
    }

    public static string BuildCoordinateReviewNote(BattlefieldUnitCandidate candidate, int x, int y)
    {
        return $"地图点选坐标：({x},{y})；原候选坐标：{candidate.CoordinateHint}；来源：{candidate.SourceCommand} / {candidate.SceneSection} / {candidate.OffsetHex}；请用旧剧本编辑器和实机战场核对后，再决定是否修改 S 剧本参数。";
    }

    public static string BuildQuickReviewNote(BattlefieldUnitCandidate candidate, string status)
    {
        var coordinate = BattlefieldEditorService.TryExtractFirstCoordinate(candidate, out var x, out var y)
            ? $"已解析候选坐标：({x},{y})。"
            : "当前候选没有明确坐标。";
        return $"快速标记：{status}；{coordinate} 来源：{candidate.SourceCommand} / {candidate.SceneSection} / {candidate.OffsetHex}。";
    }

    public static string AppendReviewLine(string existingRecord, string line)
    {
        existingRecord = existingRecord?.Trim() ?? string.Empty;
        line = line.Trim();
        if (string.IsNullOrWhiteSpace(line)) return existingRecord;
        if (existingRecord.Contains(line, StringComparison.Ordinal)) return existingRecord;
        return string.IsNullOrWhiteSpace(existingRecord)
            ? line
            : existingRecord + Environment.NewLine + line;
    }

    private static string BuildPlacementTargetKey(string scenarioFileName, BattlefieldPlacedUnit placement)
        => $"Placement#{scenarioFileName}#{placement.GridX},{placement.GridY}#{placement.PersonId}";

    private static bool IsScriptBackedPlacementReview(BattlefieldUnitReview review)
        => BattlefieldDeploymentWriteService.IsScriptPlacementWritable(new BattlefieldPlacedUnit
        {
            TargetKey = review.TargetKey,
            PersonId = review.PersonId,
            GridX = review.GridX,
            GridY = review.GridY
        });

    private static BattlefieldUnitReview Normalize(BattlefieldUnitReview review)
    {
        review.TargetKey = review.TargetKey?.Trim() ?? string.Empty;
        review.ScenarioFileName = review.ScenarioFileName?.Trim() ?? string.Empty;
        review.SourceCommand = review.SourceCommand?.Trim() ?? string.Empty;
        review.SceneSection = review.SceneSection?.Trim() ?? string.Empty;
        review.OffsetHex = review.OffsetHex?.Trim() ?? string.Empty;
        review.ReviewStatus = review.ReviewStatus?.Trim() ?? string.Empty;
        review.ReviewNote = review.ReviewNote?.Trim() ?? string.Empty;
        review.UpdatedAtText = review.UpdatedAtText?.Trim() ?? string.Empty;
        review.UnitName = review.UnitName?.Trim() ?? string.Empty;
        review.JobName = review.JobName?.Trim() ?? string.Empty;
        review.Faction = review.Faction?.Trim() ?? string.Empty;
        review.LevelMode = review.LevelMode?.Trim() ?? string.Empty;
        review.AiMode = review.AiMode?.Trim() ?? string.Empty;
        review.Direction = review.Direction?.Trim() ?? string.Empty;
        review.SafetyNote = string.IsNullOrWhiteSpace(review.SafetyNote)
            ? SafetyNoteText
            : review.SafetyNote.Trim();
        return review;
    }

    private static BattlefieldUnitReview Clone(BattlefieldUnitReview review) => new()
    {
        TargetKey = review.TargetKey,
        ScenarioFileName = review.ScenarioFileName,
        SourceCommand = review.SourceCommand,
        SceneSection = review.SceneSection,
        OffsetHex = review.OffsetHex,
        ReviewStatus = review.ReviewStatus,
        ReviewNote = review.ReviewNote,
        UpdatedAtText = review.UpdatedAtText,
        IsPlacement = review.IsPlacement,
        PersonId = review.PersonId,
        UnitName = review.UnitName,
        JobId = review.JobId,
        JobName = review.JobName,
        SImageId = review.SImageId,
        RImageId = review.RImageId,
        Faction = review.Faction,
        LevelOffset = review.LevelOffset,
        LevelMode = review.LevelMode,
        AiMode = review.AiMode,
        Hidden = review.Hidden,
        Reinforcement = review.Reinforcement,
        Direction = review.Direction,
        GridX = review.GridX,
        GridY = review.GridY,
        SafetyNote = review.SafetyNote
    };

    private static string BuildUniqueBackupPath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var candidate = Path.Combine(directory, $"{baseName}_{stamp}.bak.json");
        for (var index = 1; File.Exists(candidate); index++)
        {
            candidate = Path.Combine(directory, $"{baseName}_{stamp}_{index}.bak.json");
        }
        return candidate;
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(ch, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "Project" : name;
    }
}
