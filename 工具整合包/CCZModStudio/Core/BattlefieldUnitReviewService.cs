using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 战场出场/坐标候选的项目侧核对状态服务。只读写 CCZModStudio_Notes 下的 JSON，不修改 SV/E5S。
/// </summary>
public sealed class BattlefieldUnitReviewService
{
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
            throw new InvalidOperationException("战场出场核对状态 JSON 解析失败，请先备份并检查文件：" + path, ex);
        }
    }

    public void Apply(CczProject project, BattlefieldEditorDocument document)
    {
        var reviews = Load(project)
            .Where(x => x.ScenarioFileName.Equals(document.Scenario.FileName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.TargetKey, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in document.UnitCandidates)
        {
            if (!reviews.TryGetValue(candidate.TargetKey, out var review)) continue;
            candidate.ReviewStatus = review.ReviewStatus;
            candidate.CreatorMemo = review.CreatorMemo;
        }
    }

    public string Save(CczProject project, BattlefieldEditorDocument document, IEnumerable<BattlefieldUnitCandidate> candidates)
    {
        var path = GetStorePath(project);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var all = Load(project).Select(Clone).ToList();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.TargetKey)) continue;
            var existing = all.FirstOrDefault(x => x.TargetKey.Equals(candidate.TargetKey, StringComparison.OrdinalIgnoreCase)
                                                && x.ScenarioFileName.Equals(document.Scenario.FileName, StringComparison.OrdinalIgnoreCase));
            var empty = string.IsNullOrWhiteSpace(candidate.ReviewStatus) && string.IsNullOrWhiteSpace(candidate.CreatorMemo);
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
            existing.CreatorMemo = candidate.CreatorMemo.Trim();
            existing.UpdatedAtText = now;
            existing.SafetyNote = "项目侧战场核对备注：保存在 CCZModStudio_Notes，不写入 SV/E5S，不参与发布封包。";
        }

        if (File.Exists(path))
        {
            File.Copy(path, BuildUniqueBackupPath(path), overwrite: false);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(all.Select(Normalize).OrderByDescending(x => x.UpdatedAtText, StringComparer.Ordinal).ToList(), JsonOptions), Encoding.UTF8);
        return path;
    }

    public static string BuildCoordinateReviewMemo(BattlefieldUnitCandidate candidate, int x, int y)
    {
        return $"地图点选坐标：({x},{y})；原候选坐标：{candidate.CoordinateHint}；来源：{candidate.SourceCommand} / {candidate.SceneSection} / {candidate.OffsetHex}；请用旧剧本编辑器和实机战场核对后，再决定是否修改 SV 参数。";
    }

    public static string BuildQuickReviewMemo(BattlefieldUnitCandidate candidate, string status)
    {
        var coordinate = BattlefieldEditorService.TryExtractFirstCoordinate(candidate, out var x, out var y)
            ? $"已解析候选坐标：({x},{y})。"
            : "当前候选没有明确坐标。";
        return $"快速标记：{status}；{coordinate} 来源：{candidate.SourceCommand} / {candidate.SceneSection} / {candidate.OffsetHex}。";
    }

    public static string AppendMemoLine(string existingMemo, string line)
    {
        existingMemo = existingMemo?.Trim() ?? string.Empty;
        line = line.Trim();
        if (string.IsNullOrWhiteSpace(line)) return existingMemo;
        if (existingMemo.Contains(line, StringComparison.Ordinal)) return existingMemo;
        return string.IsNullOrWhiteSpace(existingMemo)
            ? line
            : existingMemo + Environment.NewLine + line;
    }

    private static BattlefieldUnitReview Normalize(BattlefieldUnitReview review)
    {
        review.TargetKey = review.TargetKey?.Trim() ?? string.Empty;
        review.ScenarioFileName = review.ScenarioFileName?.Trim() ?? string.Empty;
        review.SourceCommand = review.SourceCommand?.Trim() ?? string.Empty;
        review.SceneSection = review.SceneSection?.Trim() ?? string.Empty;
        review.OffsetHex = review.OffsetHex?.Trim() ?? string.Empty;
        review.ReviewStatus = review.ReviewStatus?.Trim() ?? string.Empty;
        review.CreatorMemo = review.CreatorMemo?.Trim() ?? string.Empty;
        review.UpdatedAtText = review.UpdatedAtText?.Trim() ?? string.Empty;
        review.SafetyNote = string.IsNullOrWhiteSpace(review.SafetyNote)
            ? "项目侧战场核对备注：不写入游戏文件，不参与发布封包。"
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
        CreatorMemo = review.CreatorMemo,
        UpdatedAtText = review.UpdatedAtText,
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
