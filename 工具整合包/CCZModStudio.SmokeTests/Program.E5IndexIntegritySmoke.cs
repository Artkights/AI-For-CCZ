using System.Buffers.Binary;
using System.Security.Cryptography;
using CCZModStudio.Core;
using CCZModStudio.Models;

internal partial class Program
{
    private static readonly IReadOnlyDictionary<string, (long Length, string FinalSha)> S241ExpectedFinal =
        new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Unit_mov.e5"] = (17_526_517, "3DD9C745307A26CE1F8BFDE25F6D5B60E238AED96DB9ACFEDE13728EDEDB5849"),
            ["Unit_atk.e5"] = (34_159_543, "9A202273AAD4A6F3B86079FD6671B8EDED4CF3C2A317B31E9CDCF6DA3CCE0D38"),
            ["Unit_spc.e5"] = (7_992_401, "699CA58110C4A45E603027A87C1861A98419B1288388F604E981EFAD3A774E45")
        };

    private static void RunE5IndexIntegritySmoke(CczProject sourceProject)
    {
        RunSyntheticIndexDamageSmoke(sourceProject);
        RunLogicalTargetMappingSmoke(sourceProject);

        var service = new RsArchiveRepairService();
        var sourcePaths = S241ExpectedFinal.Keys.ToDictionary(
            fileName => fileName,
            fileName => CharacterImageResourceService.ResolveGameFile(sourceProject, fileName),
            StringComparer.OrdinalIgnoreCase);
        if (sourcePaths.Values.Any(path => !File.Exists(path)))
            throw new InvalidOperationException("The S241 real-archive smoke requires all three Unit archives.");
        var sourceShas = sourcePaths.ToDictionary(pair => pair.Key, pair => Sha256(pair.Value), StringComparer.OrdinalIgnoreCase);

        var directProbeService = new E5ImageReplaceService();
        foreach (var pair in sourcePaths)
        {
            var probe = directProbeService.ProbeIndex(pair.Value);
            Console.WriteLine(
                $"DIRECT_INDEX_PROBE {pair.Key} complete={probe.IsComplete} parsed={probe.ParsedEntryCount}/{probe.ExpectedEntryCount} " +
                $"firstInvalid={probe.FirstInvalidImageNumber} reason={probe.FailureReason}");
        }
        var layout = new CharacterImageLayoutService().Resolve(sourceProject);
        if (layout.IsArchiveIntegrityValid || layout.UnitEntryCount != 556)
            throw new InvalidOperationException(
                $"Damaged Unit layout must retain theoretical count 556 and report invalid integrity: {layout.Evidence}");
        var catalog = new ImageResourceCatalogService().BuildCatalog(sourceProject)
            .Where(item => S241ExpectedFinal.ContainsKey(item.FileName))
            .ToArray();
        if (catalog.Length != 3 || catalog.Any(item => item.IsIndexComplete || item.CanReplace || item.ExpectedEntryCount != 556))
            throw new InvalidOperationException("Image resource catalog did not mark all damaged Unit archives as non-writable 556-entry indexes.");

        var sourcePreview = service.Scan(sourceProject);
        if (!sourcePreview.CanExecuteLegacyIndexShiftRecovery)
            throw new InvalidOperationException(
                "Read-only S241 recovery scan did not produce three verified candidates:\n" +
                string.Join("\n", sourcePreview.Archives.Select(archive => $"{archive.FileName}: {archive.Diagnostic}")));
        foreach (var candidate in sourcePreview.LegacyIndexShiftRecoveries)
        {
            Console.WriteLine(
                $"S241_RECOVERY_PREFLIGHT {candidate.FileName} parsed={candidate.ParsedEntryCount}/{candidate.ExpectedEntryCount} " +
                $"delta={candidate.WrongOffsetDelta} historical={candidate.HistoricalSha256} final={candidate.RepairedSha256}");
        }

        var copyRoot = Path.Combine(
            sourceProject.WorkspaceRoot,
            "CCZModStudio_TestCopies",
            "S241LegacyIndexRecovery_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        Directory.CreateDirectory(copyRoot);
        var copyBackups = Path.Combine(copyRoot, ProjectBackupPathService.LegacyBackupDirectoryName);
        Directory.CreateDirectory(copyBackups);
        var misplacedPayloads = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in sourcePreview.LegacyIndexShiftRecoveries)
        {
            File.Copy(candidate.TargetPath, Path.Combine(copyRoot, candidate.FileName), overwrite: true);
            File.Copy(candidate.BaselineBackupPath, Path.Combine(copyBackups, Path.GetFileName(candidate.BaselineBackupPath)), overwrite: true);
            File.Copy(candidate.EvidenceReportPath, Path.Combine(copyBackups, Path.GetFileName(candidate.EvidenceReportPath)), overwrite: true);
            misplacedPayloads[candidate.FileName] = ReadRawIndexedStoredPayload(candidate.TargetPath, 241);
        }

        var copyProject = new ProjectDetector().CreateProjectFromGameRoot(copyRoot);
        var copyPreview = service.Scan(copyProject);
        if (!copyPreview.CanExecuteLegacyIndexShiftRecovery)
            throw new InvalidOperationException("The test-copy recovery preview did not reproduce all three verified candidates.");
        var result = service.Execute(copyProject, copyPreview, RsArchiveRepairMode.LegacyIndexShiftRecovery);
        if (result.ChangedArchives.Count != 3)
            throw new InvalidOperationException($"Legacy recovery changed {result.ChangedArchives.Count} archives instead of 3.");

        var e5 = new E5ImageReplaceService();
        foreach (var pair in S241ExpectedFinal)
        {
            var path = Path.Combine(copyRoot, pair.Key);
            var info = new FileInfo(path);
            var sha = Sha256(path);
            var probe = e5.ProbeIndex(path);
            if (!probe.IsComplete || probe.ExpectedEntryCount != 556)
                throw new InvalidOperationException($"Recovered {pair.Key} index is not complete: {probe.Diagnostic}");
            if (info.Length != pair.Value.Length || !sha.Equals(pair.Value.FinalSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Recovered {pair.Key} mismatch: length={info.Length}, SHA={sha}.");
            if (!e5.ReadStoredEntryBytes(path, 545).SequenceEqual(misplacedPayloads[pair.Key]))
                throw new InvalidOperationException($"Recovered {pair.Key} #545 does not contain the preserved legacy #241 edit payload.");
            var baseline = copyPreview.LegacyIndexShiftRecoveries.Single(candidate => candidate.FileName.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            if (!e5.ReadStoredEntryBytes(path, 241)
                    .SequenceEqual(e5.ReadStoredEntryBytes(baseline.BaselineBackupPath, 241)))
                throw new InvalidOperationException($"Recovered {pair.Key} #241 was not restored to the verified baseline payload.");
            Console.WriteLine($"S241_RECOVERY_COPY_OK {pair.Key} entries=556 length={info.Length} sha={sha}");
        }

        foreach (var pair in sourcePaths)
        {
            if (!Sha256(pair.Value).Equals(sourceShas[pair.Key], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Read-only source archive changed during smoke: {pair.Value}");
        }
        Console.WriteLine($"E5_INDEX_INTEGRITY_OK copy={copyRoot}");
    }

    private static void RunLogicalTargetMappingSmoke(CczProject project)
    {
        var s241 = CharacterImageTargetResolver.ResolveS(project, 241, null, 1, 3, "Unit_mov.e5", "mov");
        if (s241.PhysicalImageNumber != 545 || s241.EffectiveStageSlot != 1)
            throw new InvalidOperationException($"S241 mapping mismatch: {s241.DisplayText}");
        var s1 = Enumerable.Range(1, 3)
            .Select(stage => CharacterImageTargetResolver.ResolveS(project, 1, null, 1, stage, "Unit_mov.e5", "mov").PhysicalImageNumber)
            .ToArray();
        if (!s1.SequenceEqual(new[] { 241, 242, 243 }))
            throw new InvalidOperationException("S1 stage mapping must be Unit #241/#242/#243.");
        if (!CharacterImageTargetResolver.DescribePhysicalUnitImage(241).Contains("not logical S241", StringComparison.Ordinal))
            throw new InvalidOperationException("Physical Unit #241 diagnostic does not distinguish it from logical S241.");
        Console.WriteLine("S_LOGICAL_TARGET_MAPPING_OK S241=#545 S1=#241/#242/#243");
    }

    private static void RunSyntheticIndexDamageSmoke(CczProject project)
    {
        var root = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "E5IndexDamageSynthetic");
        Directory.CreateDirectory(root);
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(root);
        var damaged = Path.Combine(root, "Unit_mov.e5");
        WriteFixedSlotE5(damaged, 556, 32, shortImageNumber: 241, shortLength: 8);
        var bytes = File.ReadAllBytes(damaged);
        var entry241 = 0x110 + 240 * 12;
        var oldLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry241, 4)));
        var newLength = 32;
        var delta = newLength - oldLength;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(entry241, 4), (uint)newLength);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(entry241 + 4, 4), (uint)newLength);
        var data241 = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry241 + 8, 4)));
        bytes[data241] = (byte)'B';
        bytes[data241 + 1] = (byte)'M';
        for (var imageNumber = 242; imageNumber <= 556; imageNumber++)
        {
            var index = 0x110 + (imageNumber - 1) * 12 + 8;
            var offset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(index, 4));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(index, 4), checked(offset + (uint)delta));
        }
        File.WriteAllBytes(damaged, bytes);

        var e5 = new E5ImageReplaceService();
        var probe = e5.ProbeIndex(damaged);
        if (probe.IsComplete || probe.ExpectedEntryCount != 556 || probe.FirstInvalidImageNumber != 556)
            throw new InvalidOperationException($"Synthetic shifted index was not diagnosed correctly: {probe.Diagnostic}");
        try
        {
            e5.PreviewBatchReplacement(testProject, damaged,
            [
                new E5ImageBatchReplaceRequest
                {
                    ImageNumber = 1,
                    SourceBytes = new byte[32],
                    SourceBytesAreRaw = true,
                    PlacementPolicy = E5ImageWritePlacementPolicy.RequireExactInPlace
                }
            ]);
        }
        catch (E5ArchiveIntegrityException)
        {
            Console.WriteLine($"SYNTHETIC_E5_SHIFT_REJECTED parsed={probe.ParsedEntryCount}/{probe.ExpectedEntryCount}");
            RunArchiveSetRollbackSmoke(testProject, root);
            return;
        }
        throw new InvalidOperationException("A write preview accepted the synthetic damaged E5 index.");
    }

    private static void RunArchiveSetRollbackSmoke(CczProject project, string root)
    {
        var first = Path.Combine(root, "rollback-a.e5");
        var second = Path.Combine(root, "rollback-b.e5");
        WriteFixedSlotE5(first, 2, 32);
        WriteFixedSlotE5(second, 2, 32);
        var beforeFirst = Sha256(first);
        var beforeSecond = Sha256(second);
        var e5 = new E5ImageReplaceService();
        E5ImageBatchReplaceRequest Request(string path, byte marker)
        {
            var source = e5.ReadStoredEntryBytes(path, 1);
            source[1] = marker;
            return new E5ImageBatchReplaceRequest
            {
                ImageNumber = 1,
                SourceBytes = source,
                SourceBytesAreRaw = true,
                ExpectedTargetKind = "RAW",
                ExpectedArchiveSha256 = e5.ComputeArchiveSha256(path),
                ExpectedIndexSha256 = e5.ComputeIndexSha256(path),
                PlacementPolicy = E5ImageWritePlacementPolicy.RequireExactInPlace
            };
        }
        var transaction = new E5ArchiveSetTransaction(e5)
        {
            BeforeCommitTestHook = (_, index) =>
            {
                if (index == 1) throw new IOException("Injected second-archive commit failure.");
            }
        };
        try
        {
            transaction.Execute(project,
            [
                new E5ArchiveMutationPlan(first, new[] { Request(first, 0x41) }),
                new E5ArchiveMutationPlan(second, new[] { Request(second, 0x42) })
            ]);
        }
        catch (InvalidOperationException)
        {
            if (Sha256(first) != beforeFirst || Sha256(second) != beforeSecond)
                throw new InvalidOperationException("Archive-set rollback did not restore both pre-save SHAs.");
            Console.WriteLine("E5_ARCHIVE_SET_ROLLBACK_OK");
            return;
        }
        throw new InvalidOperationException("Injected archive-set failure did not abort the transaction.");
    }

    private static void WriteFixedSlotE5(
        string path,
        int entryCount,
        int slotLength,
        int shortImageNumber = 0,
        int shortLength = 0)
    {
        var firstDataOffset = checked(0x110 + entryCount * 12);
        var bytes = new byte[checked(firstDataOffset + entryCount * slotLength)];
        for (var imageNumber = 1; imageNumber <= entryCount; imageNumber++)
        {
            var index = 0x110 + (imageNumber - 1) * 12;
            var length = imageNumber == shortImageNumber ? shortLength : slotLength;
            var offset = firstDataOffset + (imageNumber - 1) * slotLength;
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(index, 4), checked((uint)length));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(index + 4, 4), checked((uint)length));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(index + 8, 4), checked((uint)offset));
            bytes[offset] = 0;
            bytes[offset + 1] = checked((byte)(imageNumber & 0xFF));
        }
        File.WriteAllBytes(path, bytes);
    }

    private static byte[] ReadRawIndexedStoredPayload(string path, int imageNumber)
    {
        var bytes = File.ReadAllBytes(path);
        var index = 0x110 + (imageNumber - 1) * 12;
        var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(index, 4)));
        var offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(index + 8, 4)));
        return bytes.AsSpan(offset, length).ToArray();
    }

    private static string Sha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
