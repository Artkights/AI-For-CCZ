using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;

internal partial class Program
{
    static void RunImageAssignmentFreeIdSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        RunImageAssignmentFreeIdPureLogicSmoke();
        RunImageAssignmentFreeIdProjectSmoke(project, tables);
        Console.WriteLine("IMAGE_ASSIGNMENT_FREE_ID_SMOKE_OK");
    }

    private static void RunImageAssignmentFreeIdPureLogicSmoke()
    {
        var table = new DataTable("ImageAssignmentsSmoke");
        table.Columns.Add("ID", typeof(int));
        table.Columns.Add("头像编号", typeof(int));
        table.Columns.Add("R形象编号", typeof(int));
        table.Columns.Add("S形象编号", typeof(int));
        table.Rows.Add(0, 1, 1, 1);
        table.Rows.Add(1, 3, 3, 3);
        table.AcceptChanges();

        var assignedFace = ImageAssignmentFreeIdService.CollectAssignedIds(table, ImageAssignmentResourceKind.Face);
        var freeFace = ImageAssignmentFreeIdService.BuildFreeCandidates([0, 1, 2, 3, 4], assignedFace, ImageAssignmentResourceKind.Face, 1);
        AssertIntSequence([2, 4], freeFace.Select(x => x.Id), "free Face diff excludes zero and assigned ids");

        var assignedR = ImageAssignmentFreeIdService.CollectAssignedIds(table, ImageAssignmentResourceKind.R);
        var freeR = ImageAssignmentFreeIdService.BuildFreeCandidates([0, 1, 2, 3, 4], assignedR, ImageAssignmentResourceKind.R, 1);
        AssertIntSequence([2, 4], freeR.Select(x => x.Id), "free R diff excludes zero and assigned ids");

        table.Rows[1]["R形象编号"] = 4;
        var assignedAfterCurrentEdit = ImageAssignmentFreeIdService.CollectAssignedIds(table, ImageAssignmentResourceKind.R);
        var freeAfterCurrentEdit = ImageAssignmentFreeIdService.BuildFreeCandidates([0, 1, 2, 3, 4], assignedAfterCurrentEdit, ImageAssignmentResourceKind.R, 1);
        AssertIntSequence([2, 3], freeAfterCurrentEdit.Select(x => x.Id), "free R diff uses DataRowVersion.Current");

        table.DefaultView.RowFilter = "ID = 0";
        var assignedWithFilter = ImageAssignmentFreeIdService.CollectAssignedIds(table, ImageAssignmentResourceKind.R);
        var freeWithFilter = ImageAssignmentFreeIdService.BuildFreeCandidates([0, 1, 2, 3, 4], assignedWithFilter, ImageAssignmentResourceKind.R, 1);
        AssertIntSequence([2, 3], freeWithFilter.Select(x => x.Id), "free R diff ignores DataView filter");

        table.Rows[0]["头像编号"] = 4;
        var assignedFaceAfterCurrentEdit = ImageAssignmentFreeIdService.CollectAssignedIds(table, ImageAssignmentResourceKind.Face);
        var freeFaceAfterCurrentEdit = ImageAssignmentFreeIdService.BuildFreeCandidates([0, 1, 2, 3, 4], assignedFaceAfterCurrentEdit, ImageAssignmentResourceKind.Face, 1);
        AssertIntSequence([1, 2], freeFaceAfterCurrentEdit.Select(x => x.Id), "free Face diff uses DataRowVersion.Current");
    }

    private static void RunImageAssignmentFreeIdProjectSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        if (!File.Exists(Path.Combine(project.GameRoot, "Ekd5.exe")) ||
            !File.Exists(Path.Combine(project.GameRoot, "Data.e5")))
        {
            Console.WriteLine("IMAGE_ASSIGNMENT_FREE_ID_PROJECT_SMOKE_SKIPPED=missing_game_files");
            return;
        }

        var assignmentService = new ImageAssignmentService();
        var previewService = new ImageAssignmentPreviewService();
        var freeIdService = new ImageAssignmentFreeIdService(previewService);
        var assignments = assignmentService.Load(project, tables);
        if (assignments.Columns["头像编号"]!.ReadOnly)
        {
            throw new InvalidOperationException("人物形象设定表的头像编号列应可编辑。");
        }

        var rResult = freeIdService.Build(project, assignments, ImageAssignmentResourceKind.R, 1);
        var sResult = freeIdService.Build(project, assignments, ImageAssignmentResourceKind.S, 1);
        var faceResult = freeIdService.Build(project, assignments, ImageAssignmentResourceKind.Face, 1);
        AssertCacheFlag(faceResult, expected: false, "first Face free-id query");
        AssertCacheFlag(rResult, expected: false, "first R free-id query");
        AssertCacheFlag(sResult, expected: false, "first S free-id query");

        var cachedFaceResult = freeIdService.Build(project, assignments, ImageAssignmentResourceKind.Face, 1);
        var cachedRResult = freeIdService.Build(project, assignments, ImageAssignmentResourceKind.R, 1);
        var cachedSResult = freeIdService.Build(project, assignments, ImageAssignmentResourceKind.S, 1);
        AssertCacheFlag(cachedFaceResult, expected: true, "second Face free-id query");
        AssertCacheFlag(cachedRResult, expected: true, "second R free-id query");
        AssertCacheFlag(cachedSResult, expected: true, "second S free-id query");

        if (!faceResult.FreeCandidates.Select(x => x.Id).SequenceEqual(cachedFaceResult.FreeCandidates.Select(x => x.Id)) ||
            !rResult.FreeCandidates.Select(x => x.Id).SequenceEqual(cachedRResult.FreeCandidates.Select(x => x.Id)) ||
            !sResult.FreeCandidates.Select(x => x.Id).SequenceEqual(cachedSResult.FreeCandidates.Select(x => x.Id)))
        {
            throw new InvalidOperationException("空闲头像/R/S 编号缓存前后候选列表不一致。");
        }

        AssertCurrentEditAffectsCachedFreeResult(project, freeIdService, assignments, faceResult, ImageAssignmentResourceKind.Face, "头像编号");
        AssertCurrentEditAffectsCachedFreeResult(project, freeIdService, assignments, rResult, ImageAssignmentResourceKind.R, "R形象编号");
        AssertCurrentEditAffectsCachedFreeResult(project, freeIdService, assignments, sResult, ImageAssignmentResourceKind.S, "S形象编号");

        freeIdService.ClearCache();
        AssertCacheFlag(freeIdService.Build(project, assignments, ImageAssignmentResourceKind.Face, 1), expected: false, "Face cache miss after free-id cache clear");

        AssertNoAssignedOverlap(assignments, "头像编号", faceResult.FreeCandidates.Select(x => x.Id), "Face");
        AssertNoAssignedOverlap(assignments, "R形象编号", rResult.FreeCandidates.Select(x => x.Id), "R");
        AssertNoAssignedOverlap(assignments, "S形象编号", sResult.FreeCandidates.Select(x => x.Id), "S");
        if (faceResult.FreeCandidates.Any(x => x.Id == 0) ||
            rResult.FreeCandidates.Any(x => x.Id == 0) ||
            sResult.FreeCandidates.Any(x => x.Id == 0))
        {
            throw new InvalidOperationException("空闲头像/R/S 编号结果不应包含 0。");
        }

        foreach (var id in faceResult.FreeCandidates.Take(3).Select(x => x.Id))
        {
            using var preview = previewService.TryRenderFaceImage(project, id);
            if (preview == null)
            {
                throw new InvalidOperationException($"空闲头像 {id} 未能生成预览。");
            }
        }

        AssertCandidatePreviewClone(project, freeIdService, faceResult, ImageAssignmentResourceKind.Face, 1);
        AssertCandidatePreviewClone(project, freeIdService, rResult, ImageAssignmentResourceKind.R, 1);
        AssertCandidatePreviewClone(project, freeIdService, sResult, ImageAssignmentResourceKind.S, 1);

        foreach (var id in rResult.FreeCandidates.Take(3).Select(x => x.Id))
        {
            using var preview = previewService.TryRenderCharacterResourceImage(project, "R", id);
            if (preview == null)
            {
                throw new InvalidOperationException($"空闲 R{id} 未能生成预览。");
            }
        }

        foreach (var id in sResult.FreeCandidates.Take(3).Select(x => x.Id))
        {
            using var preview = previewService.TryRenderCharacterResourceImage(project, "S", id, jobId: null, sFactionSlot: 1);
            if (preview == null)
            {
                throw new InvalidOperationException($"空闲 S{id} 未能生成预览。");
            }
        }

        Console.WriteLine($"FREE_FACE={faceResult.FreeCandidates.Count} CANDIDATE_FACE={faceResult.CandidateResourceCount} ASSIGNED_FACE={faceResult.AssignedCount}");
        Console.WriteLine($"FREE_R={rResult.FreeCandidates.Count} CANDIDATE_R={rResult.CandidateResourceCount} ASSIGNED_R={rResult.AssignedCount}");
        Console.WriteLine($"FREE_S={sResult.FreeCandidates.Count} CANDIDATE_S={sResult.CandidateResourceCount} ASSIGNED_S={sResult.AssignedCount}");
    }

    private static void AssertCacheFlag(ImageAssignmentFreeIdResult result, bool expected, string label)
    {
        if (result.AvailableIdsFromCache != expected)
        {
            throw new InvalidOperationException($"{label}: expected AvailableIdsFromCache={expected}, actual={result.AvailableIdsFromCache}");
        }
    }

    private static void AssertCurrentEditAffectsCachedFreeResult(
        CczProject project,
        ImageAssignmentFreeIdService freeIdService,
        DataTable assignments,
        ImageAssignmentFreeIdResult baseline,
        ImageAssignmentResourceKind kind,
        string columnName)
    {
        var candidate = baseline.FreeCandidates.FirstOrDefault();
        if (candidate == null) return;

        var row = assignments.Rows.Cast<DataRow>().FirstOrDefault(dataRow =>
            Convert.ToInt32(dataRow[columnName], CultureInfo.InvariantCulture) > 0);
        if (row == null) return;

        var original = row[columnName];
        try
        {
            row[columnName] = candidate.Id;
            var afterEdit = freeIdService.Build(project, assignments, kind, 1);
            if (!afterEdit.AvailableIdsFromCache)
            {
                throw new InvalidOperationException($"{columnName} 当前编辑后的空闲编号查询应复用可用编号缓存。");
            }

            if (afterEdit.FreeCandidates.Any(x => x.Id == candidate.Id))
            {
                throw new InvalidOperationException($"{columnName} 当前编辑后，编号 {candidate.Id} 不应继续出现在空闲列表。");
            }
        }
        finally
        {
            row[columnName] = original;
        }
    }

    private static void AssertCandidatePreviewClone(
        CczProject project,
        ImageAssignmentFreeIdService freeIdService,
        ImageAssignmentFreeIdResult result,
        ImageAssignmentResourceKind kind,
        int sFactionSlot)
    {
        var id = result.FreeCandidates.FirstOrDefault()?.Id;
        if (id == null) return;

        using var first = freeIdService.RenderCandidatePreview(project, kind, id.Value, sFactionSlot);
        using var second = freeIdService.RenderCandidatePreview(project, kind, id.Value, sFactionSlot);
        if (first == null || second == null)
        {
            throw new InvalidOperationException($"空闲 {kind} 候选 {id} 未能生成缓存预览。");
        }

        if (ReferenceEquals(first, second))
        {
            throw new InvalidOperationException($"空闲 {kind} 候选 {id} 预览缓存不应返回同一个 Bitmap 实例。");
        }

        var secondSize = second.Size;
        first.Dispose();
        if (second.Size != secondSize)
        {
            throw new InvalidOperationException($"空闲 {kind} 候选 {id} 预览缓存克隆在 Dispose 后不可用。");
        }
    }

    private static void AssertNoAssignedOverlap(DataTable assignments, string columnName, IEnumerable<int> freeIds, string label)
    {
        var assigned = assignments.Rows.Cast<DataRow>()
            .Select(row => Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture))
            .Where(id => id > 0)
            .ToHashSet();
        var overlaps = freeIds.Where(assigned.Contains).OrderBy(id => id).ToArray();
        if (overlaps.Length > 0)
        {
            throw new InvalidOperationException($"空闲 {label} 编号包含已分配编号：{string.Join(",", overlaps)}");
        }
    }

    private static void AssertIntSequence(IReadOnlyList<int> expected, IEnumerable<int> actual, string label)
    {
        var actualArray = actual.ToArray();
        if (!expected.SequenceEqual(actualArray))
        {
            throw new InvalidOperationException($"{label}: expected={string.Join(",", expected)} actual={string.Join(",", actualArray)}");
        }
    }
}
