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
        AssertIntSequence([0, 2, 4], freeFace.Select(x => x.Id), "free Face diff includes zero and excludes assigned ids");
        var allFace = ImageAssignmentFreeIdService.BuildAllCandidates([0, 1, 2, 3, 4], ImageAssignmentResourceKind.Face, 1);
        AssertIntSequence([0, 1, 2, 3, 4], allFace.Select(x => x.Id), "all Face candidates include zero and keep assigned ids");

        var assignedR = ImageAssignmentFreeIdService.CollectAssignedIds(table, ImageAssignmentResourceKind.R);
        var freeR = ImageAssignmentFreeIdService.BuildFreeCandidates([0, 1, 2, 3, 4], assignedR, ImageAssignmentResourceKind.R, 1);
        AssertIntSequence([0, 2, 4], freeR.Select(x => x.Id), "free R diff includes zero and excludes assigned ids");
        var allR = ImageAssignmentFreeIdService.BuildAllCandidates([0, 1, 2, 3, 4], ImageAssignmentResourceKind.R, 1);
        AssertIntSequence([0, 1, 2, 3, 4], allR.Select(x => x.Id), "all R candidates include zero and keep assigned ids");

        table.Rows[1]["R形象编号"] = 4;
        var assignedAfterCurrentEdit = ImageAssignmentFreeIdService.CollectAssignedIds(table, ImageAssignmentResourceKind.R);
        var freeAfterCurrentEdit = ImageAssignmentFreeIdService.BuildFreeCandidates([0, 1, 2, 3, 4], assignedAfterCurrentEdit, ImageAssignmentResourceKind.R, 1);
        AssertIntSequence([0, 2, 3], freeAfterCurrentEdit.Select(x => x.Id), "free R diff uses DataRowVersion.Current");

        table.DefaultView.RowFilter = "ID = 0";
        var assignedWithFilter = ImageAssignmentFreeIdService.CollectAssignedIds(table, ImageAssignmentResourceKind.R);
        var freeWithFilter = ImageAssignmentFreeIdService.BuildFreeCandidates([0, 1, 2, 3, 4], assignedWithFilter, ImageAssignmentResourceKind.R, 1);
        AssertIntSequence([0, 2, 3], freeWithFilter.Select(x => x.Id), "free R diff ignores DataView filter");

        table.Rows[0]["头像编号"] = 4;
        var assignedFaceAfterCurrentEdit = ImageAssignmentFreeIdService.CollectAssignedIds(table, ImageAssignmentResourceKind.Face);
        var freeFaceAfterCurrentEdit = ImageAssignmentFreeIdService.BuildFreeCandidates([0, 1, 2, 3, 4], assignedFaceAfterCurrentEdit, ImageAssignmentResourceKind.Face, 1);
        AssertIntSequence([0, 1, 2], freeFaceAfterCurrentEdit.Select(x => x.Id), "free Face diff uses DataRowVersion.Current");
        AssertAnyReadableSyntheticDiscovery();
        AssertForceFreshSameStampDiscovery();
    }

    private static void AssertAnyReadableSyntheticDiscovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "CCZModStudio_RsDiscoverySmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteSyntheticE5(Path.Combine(root, "Pmapobj.e5"), new[]
            {
                new byte[48 * 64 * 20 + 2],
                new byte[17],
                new byte[48 * 64 * 20],
                new byte[19],
                new byte[23]
            });

            static byte[][] UnitEntries(int validIndex, int validLength)
            {
                var entries = Enumerable.Range(1, 243).Select(_ => new byte[1]).ToArray();
                entries[validIndex - 1] = new byte[validLength];
                return entries;
            }

            WriteSyntheticE5(Path.Combine(root, "Unit_mov.e5"), UnitEntries(validIndex: 242, validLength: 7));
            WriteSyntheticE5(Path.Combine(root, "Unit_atk.e5"), UnitEntries(validIndex: 242, validLength: 9));
            WriteSyntheticE5(Path.Combine(root, "Unit_spc.e5"), UnitEntries(validIndex: 241, validLength: 48 * 48 * 5 + 2));
            var project = new CczProject { WorkspaceRoot = root, GameRoot = root, HexTableXmlPath = string.Empty };
            var service = new ImageAssignmentPreviewService();
            AssertIntSequence([0, 1], service.GetAvailableCharacterImageIds(project, "R", includeZero: true),
                "R discovery includes RAW+2 and a front-only final candidate, but excludes an invalid-only candidate");
            AssertIntSequence([1], service.GetAvailableCharacterImageIds(project, "S", includeZero: false),
                "S discovery includes a candidate whose only readable resource is Unit_spc RAW+2");
        }
        finally
        {
            E5ImageReadSessionPool.Shared.Invalidate(new[]
            {
                Path.Combine(root, "Pmapobj.e5"),
                Path.Combine(root, "Unit_mov.e5"),
                Path.Combine(root, "Unit_atk.e5"),
                Path.Combine(root, "Unit_spc.e5")
            });
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteSyntheticE5(string path, IReadOnlyList<byte[]> payloads)
    {
        const int indexOffset = 0x110;
        const int entrySize = 12;
        var dataOffset = checked(indexOffset + payloads.Count * entrySize);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.SetLength(dataOffset);
        stream.Position = indexOffset;
        var offset = dataOffset;
        Span<byte> entry = stackalloc byte[entrySize];
        foreach (var payload in payloads)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(entry[..4], checked((uint)payload.Length));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(entry.Slice(4, 4), checked((uint)payload.Length));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(entry.Slice(8, 4), checked((uint)offset));
            stream.Write(entry);
            offset = checked(offset + payload.Length);
        }

        foreach (var payload in payloads) stream.Write(payload);
    }

    private static void AssertForceFreshSameStampDiscovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "CCZModStudio_RsForceFreshSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Pmapobj.e5");
        try
        {
            var payloadLength = 48 * 64 * 20;
            var invalidBmp = new byte[payloadLength];
            invalidBmp[0] = (byte)'B';
            invalidBmp[1] = (byte)'M';
            WriteSyntheticE5(path, new[] { invalidBmp });
            var originalWriteTime = File.GetLastWriteTimeUtc(path);
            var project = new CczProject { WorkspaceRoot = root, GameRoot = root, HexTableXmlPath = string.Empty };
            var service = new ImageAssignmentPreviewService();

            var initial = service.ScanAvailableCharacterImageIds(project, "R", includeZero: true, forceFresh: false);
            if (initial.AvailableIds.Count != 0 || initial.FromCache)
                throw new InvalidOperationException("The invalid same-length BMP-like payload should be rejected on the initial fresh scan.");

            const int firstPayloadOffset = 0x110 + 12;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                stream.Position = firstPayloadOffset;
                stream.Write(new byte[payloadLength]);
                stream.Flush(flushToDisk: true);
            }
            File.SetLastWriteTimeUtc(path, originalWriteTime);

            var stale = service.ScanAvailableCharacterImageIds(project, "R", includeZero: true, forceFresh: false);
            if (!stale.FromCache || stale.AvailableIds.Count != 0)
                throw new InvalidOperationException("A normal scan should demonstrate the stale same-length/same-timestamp availability snapshot before explicit rescan.");

            var refreshed = service.ScanAvailableCharacterImageIds(project, "R", includeZero: true, forceFresh: true);
            AssertIntSequence([0], refreshed.AvailableIds, "force-fresh scan discovers an in-place RAW replacement");
            if (refreshed.FromCache)
                throw new InvalidOperationException("A ForceFresh availability scan must not report a cache hit.");
            if (refreshed.SourceFingerprints.Count != 1 ||
                refreshed.SourceFingerprints[0].EntryCount != 1 ||
                string.IsNullOrWhiteSpace(refreshed.SourceFingerprints[0].IndexSha256))
            {
                throw new InvalidOperationException("ForceFresh availability diagnostics did not retain a complete source fingerprint.");
            }
        }
        finally
        {
            E5ImageReadSessionPool.Shared.Invalidate(new[] { path });
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
        var allFaceResult = freeIdService.Build(project, assignments, ImageAssignmentResourceKind.Face, 1, freeOnly: false);
        var allRResult = freeIdService.Build(project, assignments, ImageAssignmentResourceKind.R, 1, freeOnly: false);
        var allSResult = freeIdService.Build(project, assignments, ImageAssignmentResourceKind.S, 1, freeOnly: false);
        AssertCacheFlag(faceResult, expected: false, "first Face free-id query");
        AssertCacheFlag(rResult, expected: false, "first R free-id query");
        AssertCacheFlag(sResult, expected: false, "first S free-id query");
        AssertCacheFlag(allFaceResult, expected: true, "all Face query should reuse first Face available-id scan");
        AssertCacheFlag(allRResult, expected: true, "all R query should reuse first R available-id scan");
        if (allFaceResult.FreeOnly || allRResult.FreeOnly || allSResult.FreeOnly)
        {
            throw new InvalidOperationException("全部头像/R 查询不应标记为 FreeOnly。");
        }
        AssertRealBaseCandidateCounts(project, allRResult, allSResult);
        if (allFaceResult.Items.Count != allFaceResult.CandidateResourceCount ||
            allRResult.Items.Count != allRResult.CandidateResourceCount ||
            !allFaceResult.Items.Select(x => x.Id).SequenceEqual(allFaceResult.Items.Select(x => x.Id).OrderBy(id => id)) ||
            !allRResult.Items.Select(x => x.Id).SequenceEqual(allRResult.Items.Select(x => x.Id).OrderBy(id => id)))
        {
            throw new InvalidOperationException("全部头像/R 查询应返回全部可用编号并按 ID 升序排列。");
        }

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
        if (!allFaceResult.Items.Any(x => x.Id == 0) ||
            !allRResult.Items.Any(x => x.Id == 0))
        {
            throw new InvalidOperationException("全部头像/R 编号结果应包含 0。");
        }
        if (sResult.FreeCandidates.Any(x => x.Id == 0))
        {
            throw new InvalidOperationException("S 编号结果不应包含 0。");
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
        AssertStancePreviewClone(project, freeIdService, rResult, ImageAssignmentResourceKind.R, 1, ImageAssignmentPreviewStance.Front);
        AssertStancePreviewClone(project, freeIdService, rResult, ImageAssignmentResourceKind.R, 1, ImageAssignmentPreviewStance.Back);
        AssertStancePreviewClone(project, freeIdService, sResult, ImageAssignmentResourceKind.S, 1, ImageAssignmentPreviewStance.Front);
        AssertStancePreviewClone(project, freeIdService, sResult, ImageAssignmentResourceKind.S, 1, ImageAssignmentPreviewStance.Side);
        AssertStancePreviewClone(project, freeIdService, sResult, ImageAssignmentResourceKind.S, 1, ImageAssignmentPreviewStance.Back);
        AssertConcurrentPreviewCache(project, freeIdService, faceResult, ImageAssignmentResourceKind.Face, 1, ImageAssignmentPreviewStance.Front);
        AssertConcurrentPreviewCache(project, freeIdService, rResult, ImageAssignmentResourceKind.R, 1, ImageAssignmentPreviewStance.Front);
        AssertConcurrentPreviewCache(project, freeIdService, rResult, ImageAssignmentResourceKind.R, 1, ImageAssignmentPreviewStance.Back);
        AssertConcurrentPreviewCache(project, freeIdService, sResult, ImageAssignmentResourceKind.S, 1, ImageAssignmentPreviewStance.Front);
        AssertConcurrentPreviewCache(project, freeIdService, sResult, ImageAssignmentResourceKind.S, 1, ImageAssignmentPreviewStance.Side);
        AssertConcurrentPreviewCache(project, freeIdService, sResult, ImageAssignmentResourceKind.S, 1, ImageAssignmentPreviewStance.Back);
        AssertDetailedCandidateContract(project, freeIdService, allSResult);
        AssertKnownMalformedUnitAttack(project, previewService);

        var firstReadableR = allRResult.Items.FirstOrDefault()?.Id;
        if (firstReadableR != null)
        {
            using var invalidFrame = previewService.TryRenderRScenePhysicalStripFrame(
                project,
                firstReadableR.Value,
                20,
                "下",
                out var invalidFrameDetail);
            if (invalidFrame != null || !invalidFrameDetail.Contains("请求 20", StringComparison.Ordinal))
                throw new InvalidOperationException("R physical-frame rendering must reject index 20 instead of clamping it to frame 19.");
        }

        foreach (var id in rResult.FreeCandidates.Take(3).Select(x => x.Id))
        {
            using var preview = previewService.TryRenderRScenePhysicalStripFrame(project, id, 0, "下", out _);
            if (preview == null)
            {
                throw new InvalidOperationException($"空闲 R{id} 未能生成预览。");
            }
        }

        foreach (var id in sResult.FreeCandidates.Take(3).Select(x => x.Id))
        {
            using var preview = previewService.TryRenderBattlefieldMoveIdleFrame(project, id, null, 1, "下", 0, out _);
            if (preview == null)
            {
                throw new InvalidOperationException($"空闲 S{id} 未能生成预览。");
            }
        }

        Console.WriteLine($"FREE_FACE={faceResult.FreeCandidates.Count} ALL_FACE={allFaceResult.Items.Count} CANDIDATE_FACE={faceResult.CandidateResourceCount} ASSIGNED_FACE={faceResult.AssignedCount}");
        Console.WriteLine($"FREE_R={rResult.FreeCandidates.Count} ALL_R={allRResult.Items.Count} CANDIDATE_R={rResult.CandidateResourceCount} ASSIGNED_R={rResult.AssignedCount}");
        Console.WriteLine($"FREE_S={sResult.FreeCandidates.Count} ALL_S={allSResult.Items.Count} CANDIDATE_S={sResult.CandidateResourceCount} ASSIGNED_S={sResult.AssignedCount}");
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

    private static void AssertStancePreviewClone(
        CczProject project,
        ImageAssignmentFreeIdService freeIdService,
        ImageAssignmentFreeIdResult result,
        ImageAssignmentResourceKind kind,
        int sFactionSlot,
        ImageAssignmentPreviewStance stance)
    {
        var id = result.FreeCandidates.FirstOrDefault()?.Id;
        if (id == null) return;

        using var first = freeIdService.RenderCandidatePreview(project, kind, id.Value, sFactionSlot, stance);
        using var second = freeIdService.RenderCandidatePreview(project, kind, id.Value, sFactionSlot, stance);
        if (first == null || second == null)
        {
            throw new InvalidOperationException($"空闲 {kind} 候选 {id} {ImageAssignmentFreeIdService.GetStanceDisplayText(stance)}未能生成缓存预览。");
        }

        if (ReferenceEquals(first, second))
        {
            throw new InvalidOperationException($"空闲 {kind} 候选 {id} {ImageAssignmentFreeIdService.GetStanceDisplayText(stance)}预览缓存不应返回同一个 Bitmap 实例。");
        }

        if (kind == ImageAssignmentResourceKind.S && first.Size != new Size(48, 48))
        {
            throw new InvalidOperationException($"S 查询预览应只显示 Unit_mov.e5 单个 48x48 待机帧，实际尺寸 {first.Width}x{first.Height}。");
        }
    }

    private static void AssertConcurrentPreviewCache(
        CczProject project,
        ImageAssignmentFreeIdService freeIdService,
        ImageAssignmentFreeIdResult result,
        ImageAssignmentResourceKind kind,
        int sFactionSlot,
        ImageAssignmentPreviewStance stance)
    {
        var id = result.FreeCandidates.FirstOrDefault()?.Id;
        if (id == null) return;

        var previews = Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            freeIdService.RenderCandidatePreviewAsync(project, kind, id.Value, sFactionSlot, stance, CancellationToken.None)))
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        if (previews.Any(preview => preview == null))
        {
            throw new InvalidOperationException($"并发 {kind} 候选 {id} {ImageAssignmentFreeIdService.GetStanceDisplayText(stance)}预览不应返回空。");
        }

        using var first = previews[0]!.CreateBitmap();
        using var second = previews[1]!.CreateBitmap();
        if (ReferenceEquals(first, second))
        {
            throw new InvalidOperationException($"并发 {kind} 候选 {id} {ImageAssignmentFreeIdService.GetStanceDisplayText(stance)}预览不应共享 Bitmap 实例。");
        }

        var secondSize = second.Size;
        first.Dispose();
        if (second.Size != secondSize)
        {
            throw new InvalidOperationException($"并发 {kind} 候选 {id} {ImageAssignmentFreeIdService.GetStanceDisplayText(stance)}预览的独立 Bitmap 在另一个实例释放后不可用。");
        }

        if (kind == ImageAssignmentResourceKind.S && previews.Any(preview => preview!.Size != new Size(48, 48)))
        {
            throw new InvalidOperationException($"并发 S 查询预览应只缓存 Unit_mov.e5 单个 48x48 待机帧。");
        }
    }

    private static void AssertDetailedCandidateContract(
        CczProject project,
        ImageAssignmentFreeIdService freeIdService,
        ImageAssignmentFreeIdResult allSResult)
    {
        var candidate = allSResult.Items.FirstOrDefault(item => item.Id == 219) ?? allSResult.Items.FirstOrDefault();
        if (candidate == null) return;
        if (candidate.Id == 219)
        {
            AssertS219OneStageFallback(project, freeIdService);
            AssertThreeStageMapping(project);
            return;
        }
        var detail = freeIdService.RenderCandidateDetailsAsync(
                project,
                ImageAssignmentResourceKind.S,
                candidate.Id,
                1,
                ImageAssignmentPreviewStance.Front,
                1,
                CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        if (!detail.SelectedStageAvailable || detail.Representative == null || detail.Representative.Size != new Size(48, 48))
            throw new InvalidOperationException($"S{candidate.Id} detailed representative should be a complete 48x48 physical frame.");
        if (!detail.Representative.CacheKey.StartsWith("thumbnail-v4|compact-rs-v1|" + RsStripLayoutService.ContractVersion + "|", StringComparison.Ordinal))
            throw new InvalidOperationException("Candidate thumbnail did not use the thumbnail-v4 compact strip contract.");
    }

    private static void AssertS219OneStageFallback(CczProject project, ImageAssignmentFreeIdService freeIdService)
    {
        var previews = new List<CachedPreviewImage>();
        try
        {
            foreach (var requestedStage in new[] { 1, 2, 3 })
            {
                var resolution = CharacterImageResourceService.ResolveSPreviewStage(project, 219, jobId: null, factionSlot: 1, requestedStage);
                if (resolution.Target?.ImageNumber != 523 || resolution.EffectiveStageSlot != 1 ||
                    resolution.IsOneStageFallback != (requestedStage != 1))
                {
                    throw new InvalidOperationException(
                        $"S219 stage fallback mismatch: requested={requestedStage}, effective={resolution.EffectiveStageSlot}, image=#{resolution.Target?.ImageNumber}.");
                }

                var detail = freeIdService.RenderCandidateDetailsAsync(
                        project, ImageAssignmentResourceKind.S, 219, 1,
                        ImageAssignmentPreviewStance.Front, requestedStage, CancellationToken.None)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                if (!detail.SelectedStageAvailable || detail.Representative == null || detail.Representative.Size != new Size(48, 48))
                    throw new InvalidOperationException($"S219 requested stage {requestedStage} did not render its effective one-stage representative.");
                if (!detail.Representative.CacheKey.StartsWith("thumbnail-v4|compact-rs-v1|" + RsStripLayoutService.ContractVersion + "|", StringComparison.Ordinal))
                    throw new InvalidOperationException("S219 fallback preview did not use thumbnail-v4.");
                previews.Add(detail.Representative);

                using var battlefield = freeIdService.PreviewService.TryRenderBattlefieldMoveIdleFrame(
                    project, 219, jobId: null, factionSlot: 1, direction: "下", framePhase: 0,
                    stageSlot: requestedStage, out var battlefieldDetail);
                if (battlefield == null || (requestedStage != 1 && !battlefieldDetail.Contains("实际使用第一转", StringComparison.Ordinal)))
                    throw new InvalidOperationException($"S219 battlefield fallback failed for requested stage {requestedStage}: {battlefieldDetail}");

                using var catalog = new RsSingleFrameCatalogService().BuildS(project, 219, jobId: null, 1, requestedStage, "fallback smoke");
                if (catalog.StageSlot != 1 || catalog.Frames.Where(frame => frame.ImageNumber > 0).Any(frame => frame.ImageNumber != 523))
                    throw new InvalidOperationException($"S219 all-frames fallback targeted the wrong stage or image for request {requestedStage}.");

                AssertSActionSequenceContract(project, freeIdService.PreviewService, requestedStage, catalog);
            }

            if (!previews.Skip(1).All(preview => preview.PngBytes.SequenceEqual(previews[0].PngBytes)))
                throw new InvalidOperationException("S219 stage 1/2/3 compact representatives must have identical pixels after one-stage fallback.");
        }
        finally
        {
            previews.Clear();
        }
    }

    private static void AssertSActionSequenceContract(
        CczProject project,
        ImageAssignmentPreviewService previewService,
        int requestedStage,
        RsFrameCatalog catalog)
    {
        var expected = new (string Id, string FileName, string Action, RsFacing? Facing, int[] Frames)[]
        {
            ("move-front", "Unit_mov.e5", "待机/移动", RsFacing.Front, [0, 1]),
            ("move-back", "Unit_mov.e5", "待机/移动", RsFacing.Back, [2, 3]),
            ("move-side", "Unit_mov.e5", "待机/移动", RsFacing.Side, [4, 5]),
            ("guard-front", "Unit_mov.e5", "防御/受击", RsFacing.Front, [6]),
            ("guard-back", "Unit_mov.e5", "防御/受击", RsFacing.Back, [7]),
            ("guard-side", "Unit_mov.e5", "防御/受击", RsFacing.Side, [8]),
            ("defeat", "Unit_mov.e5", "倒地/退场", null, [9, 10]),
            ("attack-front", "Unit_atk.e5", "攻击", RsFacing.Front, [0, 1, 2, 3]),
            ("attack-back", "Unit_atk.e5", "攻击", RsFacing.Back, [4, 5, 6, 7]),
            ("attack-side", "Unit_atk.e5", "攻击", RsFacing.Side, [8, 9, 10, 11]),
            ("special", "Unit_spc.e5", "特技", null, [0, 1, 2, 3, 4])
        };

        using var preview = previewService.BuildSAnimationPreview(project, 219, jobId: null, factionSlot: 1, requestedStage);
        if (preview.StageSlot != 1 || preview.Sequences.Count != expected.Length ||
            preview.ReadableFrameCount != 28 || preview.MissingFrameCount != 0)
        {
            throw new InvalidOperationException(
                $"S219 action preview contract mismatch for requested stage {requestedStage}: stage={preview.StageSlot}, sequences={preview.Sequences.Count}, readable={preview.ReadableFrameCount}, missing={preview.MissingFrameCount}.");
        }

        for (var sequenceIndex = 0; sequenceIndex < expected.Length; sequenceIndex++)
        {
            var contract = expected[sequenceIndex];
            var sequence = preview.Sequences[sequenceIndex];
            if (sequence.Definition.Id != contract.Id ||
                !sequence.Definition.SourceFile.Equals(contract.FileName, StringComparison.OrdinalIgnoreCase) ||
                sequence.Definition.ActionLabel != contract.Action ||
                sequence.Definition.Facing != contract.Facing ||
                !sequence.Definition.PhysicalFrameIndices.SequenceEqual(contract.Frames) ||
                !sequence.Frames.Select(frame => frame.PhysicalFrameIndex).SequenceEqual(contract.Frames) ||
                sequence.ImageNumber != 523 ||
                sequence.RequestedStageSlot != requestedStage ||
                sequence.EffectiveStageSlot != 1)
            {
                throw new InvalidOperationException($"S219 action sequence {sequenceIndex} does not match the shared physical-frame contract.");
            }

            foreach (var frame in sequence.Frames)
            {
                var descriptor = catalog.Frames.Single(candidate =>
                    Path.GetFileName(candidate.TargetPath).Equals(contract.FileName, StringComparison.OrdinalIgnoreCase) &&
                    candidate.PhysicalFrameIndex == frame.PhysicalFrameIndex);
                if (!descriptor.IsReadable || descriptor.Bitmap == null || frame.Bitmap == null)
                    throw new InvalidOperationException($"S219 {contract.Id} physical frame {frame.PhysicalFrameIndex} is missing from animation or all-frames view.");
                if (!descriptor.DisplayLabel.Contains($"物理帧{frame.PhysicalFrameIndex}", StringComparison.Ordinal) ||
                    !descriptor.DisplayLabel.Contains(contract.Action, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"S219 {contract.Id} label is inconsistent with the shared action contract: {descriptor.DisplayLabel}");
                }
                AssertRsActionBitmapEqual(descriptor.Bitmap, frame.Bitmap, $"S219 {contract.Id} physical frame {frame.PhysicalFrameIndex}");
            }
        }

        if (!preview.Sequences.SelectMany(sequence => sequence.Frames).Select(frame => frame.PhysicalFrameIndex)
                .Where((_, index) => index < 11)
                .OrderBy(index => index)
                .SequenceEqual(Enumerable.Range(0, 11)) ||
            preview.Sequences.Count(sequence => sequence.Definition.SourceFile.Equals("Unit_spc.e5", StringComparison.OrdinalIgnoreCase)) != 1)
        {
            throw new InvalidOperationException("S action preview must include Unit_mov 0-10 exactly once and Unit_spc as one five-frame sequence.");
        }

        var sideIndex = preview.Sequences.ToList().FindIndex(sequence => sequence.Definition.Id == "move-side");
        using var left = previewService.BuildAnimationSequenceFrame(preview, sideIndex, 0, mirrorHorizontal: false);
        using var right = previewService.BuildAnimationSequenceFrame(preview, sideIndex, 0, mirrorHorizontal: true);
        AssertRsActionHorizontalMirror(left, right, "S219 move-side right-facing mirror");

        if (requestedStage == 1)
        {
            using var cached = previewService.LoadAnimationAsync(
                    project,
                    ImageAssignmentResourceKind.S,
                    219,
                    jobId: null,
                    factionSlot: 1,
                    stageSlot: 1,
                    CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            if (cached.Sequences.Count != 11 || cached.PrecomposedFrames.Count != 28 ||
                cached.PrecomposedFrames.Any(frame =>
                    !frame.CacheKey.StartsWith("animation-v4|" + RsActionSequenceCatalog.ContractVersion + "|", StringComparison.Ordinal) ||
                    !frame.CacheKey.Contains("|entry=", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("S animation cache did not use the action-layout and entry-content identity contract.");
            }
        }
    }

    private static void AssertRsActionBitmapEqual(Bitmap expected, Bitmap actual, string label)
    {
        if (expected.Size != actual.Size)
            throw new InvalidOperationException($"{label}: size mismatch {expected.Size}/{actual.Size}.");
        for (var y = 0; y < expected.Height; y++)
        for (var x = 0; x < expected.Width; x++)
        {
            if (expected.GetPixel(x, y).ToArgb() != actual.GetPixel(x, y).ToArgb())
                throw new InvalidOperationException($"{label}: pixel mismatch at {x},{y}.");
        }
    }

    private static void AssertRsActionHorizontalMirror(Bitmap left, Bitmap right, string label)
    {
        if (left.Size != right.Size)
            throw new InvalidOperationException($"{label}: size mismatch {left.Size}/{right.Size}.");
        for (var y = 0; y < left.Height; y++)
        for (var x = 0; x < left.Width; x++)
        {
            if (left.GetPixel(x, y).ToArgb() != right.GetPixel(right.Width - 1 - x, y).ToArgb())
                throw new InvalidOperationException($"{label}: mirrored pixel mismatch at {x},{y}.");
        }
    }

    private static void AssertThreeStageMapping(CczProject project)
    {
        var targets = new[] { 1, 2, 3 }
            .Select(stage => CharacterImageResourceService.ResolveSPreviewStage(project, 1, jobId: null, factionSlot: 1, stage))
            .ToArray();
        if (targets.Any(target => target.IsOneStageFallback || target.EffectiveStageSlot != target.RequestedStageSlot) ||
            targets.Select(target => target.Target?.ImageNumber).Distinct().Count() != 3)
        {
            throw new InvalidOperationException("S1 must preserve three distinct stage mappings without one-stage fallback.");
        }
    }

    private static void AssertRealBaseCandidateCounts(
        CczProject project,
        ImageAssignmentFreeIdResult allRResult,
        ImageAssignmentFreeIdResult allSResult)
    {
        var baseName = Path.GetFileName(project.GameRoot.TrimEnd(Path.DirectorySeparatorChar));
        var expected = baseName switch
        {
            "加强版6.5未加密版" => (R: 329, S: 252),
            var value when value.Contains("神话三国志2026新春版", StringComparison.Ordinal) => (R: 340, S: 525),
            _ => ((int R, int S)?)null
        };
        if (expected.HasValue &&
            (allRResult.Items.Count != expected.Value.R || allSResult.Items.Count != expected.Value.S))
        {
            throw new InvalidOperationException(
                $"Strict any-readable candidate count mismatch for {baseName}: R={allRResult.Items.Count}/{expected.Value.R}, S={allSResult.Items.Count}/{expected.Value.S}.");
        }
    }

    private static void AssertKnownMalformedUnitAttack(CczProject project, ImageAssignmentPreviewService previewService)
    {
        if (!project.GameRoot.Contains("神话三国志2026新春版", StringComparison.Ordinal)) return;
        var path = CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5");
        if (!previewService.TryProbeRsEntry(path, 513, out var probe, out var detail) || probe == null)
            throw new InvalidOperationException("Failed to inspect known malformed Unit_atk.e5 #513: " + detail);
        if (probe.Strip.IsSupportedLayout ||
            probe.Strip.DecodedWidth != 64 ||
            probe.Strip.DecodedHeight != 959 ||
            !probe.Strip.Detail.Contains("要求 64x768", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Known Unit_atk.e5 #513 64x959 entry was not rejected as a malformed 12x64 strip: " + probe.Strip.Detail);
        }
        Console.WriteLine("KNOWN_MALFORMED_UNIT_ATK_513_REJECTED=" + probe.Strip.Detail);
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
