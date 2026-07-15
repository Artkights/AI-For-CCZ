using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RsArchiveRepairService
{
    private static readonly string[] ArchiveNames = ["Pmapobj.e5", "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5"];
    private static readonly IReadOnlyDictionary<string, LegacyRecoveryEvidence> KnownLegacyRecoveries =
        new Dictionary<string, LegacyRecoveryEvidence>(StringComparer.OrdinalIgnoreCase)
        {
            ["Unit_mov.e5"] = new(65_300, 17_526_517,
                "970EEA8D9E027000CD5B02FB1AF87B64B09F1EEEDF4870C53D2A2445B9A3D446",
                "3DD9C745307A26CE1F8BFDE25F6D5B60E238AED96DB9ACFEDE13728EDEDB5849"),
            ["Unit_atk.e5"] = new(131_818, 34_159_543,
                "5A0177E91D506491F2E987DA549F32112D9562D3CEBB59A3E3E3EADBC615FCFA",
                "9A202273AAD4A6F3B86079FD6671B8EDED4CF3C2A317B31E9CDCF6DA3CCE0D38"),
            ["Unit_spc.e5"] = new(28_853, 7_992_401,
                "6E59DD865D4B6839559E467EB74EE9C6EFC39BF9FF6095CB10245D59FCBFEB51",
                "699CA58110C4A45E603027A87C1861A98419B1288388F604E981EFAD3A774E45")
        };
    private readonly E5ImageReplaceService _e5 = new();
    private readonly E5RawImageCodec _raw = new();
    private readonly EditableImageStorageService _storage = new();

    public RsArchiveRepairPreview Scan(CczProject project)
    {
        var archives = new List<RsArchiveRepairArchivePreview>();
        foreach (var fileName in ArchiveNames)
        {
            var path = CharacterImageResourceService.ResolveGameFile(project, fileName);
            if (!File.Exists(path))
            {
                archives.Add(new RsArchiveRepairArchivePreview { FileName = fileName, TargetPath = path, Diagnostic = "文件不存在。" });
                continue;
            }
            archives.Add(ScanArchive(project, path));
        }
        return new RsArchiveRepairPreview { Archives = archives };
    }

    public RsArchiveRepairResult Execute(
        CczProject project,
        RsArchiveRepairPreview preview,
        RsArchiveRepairMode mode,
        string? wholeBackupPath = null)
    {
        if (mode == RsArchiveRepairMode.RestoreWholeBackup)
        {
            if (string.IsNullOrWhiteSpace(wholeBackupPath) || !File.Exists(wholeBackupPath))
                throw new InvalidOperationException("没有选择有效的整档备份。");
            var archive = preview.Archives.FirstOrDefault(item =>
                item.CompatibleBackups.Contains(wholeBackupPath, StringComparer.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("所选备份不属于本次扫描确认的兼容备份链。");
            return RestoreWholeArchive(project, archive, wholeBackupPath);
        }

        if (mode == RsArchiveRepairMode.LegacyIndexShiftRecovery)
            return ExecuteLegacyIndexShiftRecovery(project, preview);

        var changed = new List<string>();
        var backups = new List<string>();
        long oldTotal = 0;
        long newTotal = 0;
        foreach (var archive in preview.Archives.Where(item => File.Exists(item.TargetPath)))
        {
            var requests = mode == RsArchiveRepairMode.PreservePixelsAndRestoreFormat
                ? archive.Candidates.Where(candidate => candidate.Selected).Select(candidate => BuildRecoveryRequest(candidate)).ToList()
                : new List<E5ImageBatchReplaceRequest>();
            if (requests.Count == 0)
            {
                var compactRequest = BuildCompactOnlyRequest(archive.TargetPath);
                if (compactRequest == null || archive.OrphanBytes <= 0) continue;
                requests.Add(compactRequest);
            }

            var before = new FileInfo(archive.TargetPath).Length;
            var preflight = _e5.PreviewBatchReplacement(project, archive.TargetPath, requests);
            if (mode == RsArchiveRepairMode.PreservePixelsAndRestoreFormat &&
                preflight.Operations.Any(operation => operation.OldKind != "PNG" || operation.NewKind is not ("RAW" or "BMP")))
                throw new InvalidOperationException($"{archive.FileName} 恢复预览出现非预期格式变化，已拒绝执行。");
            var result = _e5.ReplaceBatch(project, archive.TargetPath, requests);
            VerifyRecoveredEntries(project, archive, requests);
            changed.Add(archive.TargetPath);
            backups.Add(result.BackupPath);
            oldTotal += before;
            newTotal += new FileInfo(archive.TargetPath).Length;
        }

        var report = WriteReport(project, mode, changed, backups, oldTotal, newTotal, preview);
        Invalidate(changed);
        return new RsArchiveRepairResult
        {
            ChangedArchives = changed,
            BackupPaths = backups,
            OldTotalSize = oldTotal,
            NewTotalSize = newTotal,
            ReportPath = report
        };
    }

    private RsArchiveRepairArchivePreview ScanArchive(CczProject project, string path)
    {
        var indexProbe = _e5.ProbeIndex(path);
        if (!indexProbe.IsComplete)
            return ScanDamagedArchive(project, path, indexProbe);

        var entries = _e5.ReadIndex(path);
        var fileBytes = File.ReadAllBytes(path);
        var topology = _e5.CaptureTopology(path);
        var compactSize = EstimateCompactSize(entries);
        var backups = FindCompatibleBackups(project, path, entries.Count, fileBytes).ToArray();
        var reports = FindConversionReports(project, path).ToArray();
        var backupSnapshots = backups.Select(backup => new BackupSnapshot(
            backup,
            _e5.ReadIndex(backup, Path.GetFileName(path)),
            File.ReadAllBytes(backup))).ToArray();
        var integrityFindings = BuildIntegrityFindings(path, topology, backups.FirstOrDefault());
        var candidates = new List<RsArchiveRepairCandidate>();
        foreach (var current in entries.Where(entry => entry.Kind.Equals("PNG", StringComparison.OrdinalIgnoreCase)))
        {
            var currentBytes = fileBytes.AsSpan(current.DataOffset, current.StoredLength).ToArray();
            using var currentBitmap = DecodeArgb(currentBytes);
            if (currentBitmap == null) continue;
            foreach (var backup in backupSnapshots)
            {
                if (current.ImageNumber > backup.Entries.Count) continue;
                var original = backup.Entries[current.ImageNumber - 1];
                if (original.Kind is not ("RAW" or "BMP") || original.IsCompressed) continue;
                var originalBytes = backup.Bytes.AsSpan(original.DataOffset, original.StoredLength).ToArray();
                if (!TryBuildRestoredBytes(project, path, backup.Path, current.ImageNumber, currentBitmap, original, originalBytes,
                        out var restored, out var nearest, out var warning)) continue;
                candidates.Add(new RsArchiveRepairCandidate
                {
                    ImageNumber = current.ImageNumber,
                    CurrentKind = "PNG",
                    RestoreKind = original.Kind,
                    CurrentLength = current.StoredLength,
                    RestoredLength = restored.Length,
                    Width = currentBitmap.Width,
                    Height = currentBitmap.Height,
                    NearestPalettePixels = nearest,
                    BackupPath = backup.Path,
                    Warning = warning,
                    RestoredBytes = restored,
                    CurrentStoredSha256 = Convert.ToHexString(SHA256.HashData(currentBytes))
                });
                break;
            }
        }

        var diagnostic = candidates.Count == 0
            ? backups.Length == 0
                ? "未找到索引结构兼容的备份；仅生成膨胀诊断，不猜测 PNG 的原格式。"
                : "兼容备份中没有能证明并安全恢复的 RAW/BMP→PNG 条目。"
            : $"发现 {candidates.Count} 个有备份证据的格式恢复候选。";
        if (integrityFindings.Count > 0)
            diagnostic += Environment.NewLine + string.Join(Environment.NewLine, integrityFindings.Select(item => "- " + item));
        return new RsArchiveRepairArchivePreview
        {
            FileName = Path.GetFileName(path),
            TargetPath = path,
            CurrentSize = fileBytes.LongLength,
            CompactSize = compactSize,
            EntryCount = entries.Count,
            CompatibleBackups = backups,
            EvidenceReports = reports,
            Candidates = candidates,
            Topology = topology,
            IntegrityFindings = integrityFindings,
            Diagnostic = diagnostic
        };
    }

    private RsArchiveRepairArchivePreview ScanDamagedArchive(
        CczProject project,
        string path,
        E5IndexProbeResult probe)
    {
        var fileName = Path.GetFileName(path);
        var recovery = TryBuildLegacyIndexShiftRecovery(project, path, probe);
        var findings = new List<string> { probe.Diagnostic };
        if (recovery != null) findings.Add(recovery.Diagnostic);
        return new RsArchiveRepairArchivePreview
        {
            FileName = fileName,
            TargetPath = path,
            CurrentSize = new FileInfo(path).Length,
            CompactSize = 0,
            EntryCount = probe.ExpectedEntryCount,
            CompatibleBackups = recovery == null ? Array.Empty<string>() : new[] { recovery.BaselineBackupPath },
            EvidenceReports = recovery == null ? Array.Empty<string>() : new[] { recovery.EvidenceReportPath },
            IntegrityFindings = findings,
            Diagnostic = recovery == null
                ? probe.Diagnostic + " No verified legacy index-shift recovery evidence was found."
                : recovery.Diagnostic,
            LegacyIndexShiftRecovery = recovery
        };
    }

    private RsLegacyIndexShiftRecoveryCandidate? TryBuildLegacyIndexShiftRecovery(
        CczProject project,
        string targetPath,
        E5IndexProbeResult currentProbe)
    {
        var fileName = Path.GetFileName(targetPath);
        if (!KnownLegacyRecoveries.TryGetValue(fileName, out var expected)) return null;
        try
        {
            string? root = null;
            LegacyReportEvidence? evidence = null;
            foreach (var candidateRoot in ProjectBackupPathService.GetReadableBackupRoots(project).Where(Directory.Exists))
            {
                evidence = FindLegacyEvidenceReport(candidateRoot, fileName, expected.HistoricalSha256);
                if (evidence == null) continue;
                root = candidateRoot;
                break;
            }

            if (evidence == null) return null;
            if (root == null) return null;
            var backupPath = Path.Combine(root, Path.GetFileName(evidence.BackupPath));
            if (!File.Exists(backupPath)) return null;

            var currentBytes = File.ReadAllBytes(targetPath);
            var currentSha = Convert.ToHexString(SHA256.HashData(currentBytes));
            var backupBytes = File.ReadAllBytes(backupPath);
            if (!Convert.ToHexString(SHA256.HashData(backupBytes))
                    .Equals(evidence.BeforeSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The baseline backup SHA does not match its write report.");
            var backupProbe = E5IndexParser.Probe(backupBytes, fileName, backupPath);
            if (!backupProbe.IsComplete || backupProbe.ExpectedEntryCount != 556)
                throw new InvalidOperationException("The baseline backup is not a complete 556-entry Unit archive.");

            var historical = RebuildReportedHistoricalArchive(currentBytes, backupBytes, fileName, evidence);
            var historicalSha = Convert.ToHexString(SHA256.HashData(historical));
            if (!historicalSha.Equals(expected.HistoricalSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"The reconstructed historical SHA is {historicalSha}, expected {expected.HistoricalSha256}.");
            var historicalProbe = E5IndexParser.Probe(historical, fileName);
            if (!historicalProbe.IsComplete || historicalProbe.ExpectedEntryCount != 556)
                throw new InvalidOperationException("The reconstructed historical archive does not contain 556 valid entries.");
            if (currentBytes.LongLength != expected.FileLength || historical.LongLength != expected.FileLength)
                throw new InvalidOperationException(
                    $"Archive length does not match the verified evidence: current={currentBytes.LongLength}, expected={expected.FileLength}.");
            if (currentProbe.ExpectedEntryCount != 556 || currentProbe.RawEntries.Count != 556)
                throw new InvalidOperationException(
                    $"The damaged directory does not expose the expected 556 raw index entries ({currentProbe.RawEntries.Count}).");

            const int wrongTarget = 241;
            const int correctTarget = 545;
            var oldWrong = historicalProbe.Entries[wrongTarget - 1];
            var currentWrong = currentProbe.RawEntries[wrongTarget - 1];
            var correct = historicalProbe.Entries[correctTarget - 1];
            var delta = checked((int)currentWrong.StoredLength - oldWrong.StoredLength);
            if (delta != expected.WrongOffsetDelta)
                throw new InvalidOperationException($"Index shift delta is {delta}, expected {expected.WrongOffsetDelta}.");
            if (currentWrong.DataOffset != oldWrong.DataOffset)
                throw new InvalidOperationException("The legacy writer moved #241 instead of filling its reserved slot.");
            if (currentWrong.StoredLength != correct.StoredLength)
                throw new InvalidOperationException(
                    $"Edited #241 length {currentWrong.StoredLength} does not equal the correct #545 slot {correct.StoredLength}.");
            if ((ulong)currentWrong.DataOffset + currentWrong.StoredLength > (ulong)currentBytes.LongLength)
                throw new InvalidOperationException("The edited #241 payload lies outside the current archive.");

            for (var index = wrongTarget; index < historicalProbe.Entries.Count; index++)
            {
                var baseline = historicalProbe.Entries[index];
                var damaged = currentProbe.RawEntries[index];
                if (damaged.StoredLength != baseline.StoredLength || damaged.DecodedLength != baseline.DecodedLength ||
                    damaged.DataOffset != checked((uint)(baseline.DataOffset + delta)))
                    throw new InvalidOperationException(
                        $"Image #{index + 1} does not follow the uniform +{delta} legacy index-shift signature.");
            }

            var editedPayload = currentBytes.AsSpan(
                checked((int)currentWrong.DataOffset),
                checked((int)currentWrong.StoredLength)).ToArray();
            if (editedPayload.Length < 2 || editedPayload[0] != (byte)'B' || editedPayload[1] != (byte)'M')
                throw new InvalidOperationException("The misplaced #241 payload is not the expected BMP edit result.");
            if (!currentBytes.AsSpan(correct.DataOffset, correct.StoredLength)
                    .SequenceEqual(historical.AsSpan(correct.DataOffset, correct.StoredLength)))
                throw new InvalidOperationException("The physical #545 payload was already changed; automatic migration is unsafe.");

            var repaired = historical.ToArray();
            editedPayload.CopyTo(repaired.AsSpan(correct.DataOffset, correct.StoredLength));
            var repairedProbe = E5IndexParser.Probe(repaired, fileName);
            if (!repairedProbe.IsComplete || repairedProbe.ExpectedEntryCount != 556)
                throw new InvalidOperationException("The staged repair does not contain a complete 556-entry index.");
            var repairedSha = Convert.ToHexString(SHA256.HashData(repaired));
            if (!repairedSha.Equals(expected.RepairedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"The staged repair SHA is {repairedSha}, expected {expected.RepairedSha256}.");

            return new RsLegacyIndexShiftRecoveryCandidate
            {
                FileName = fileName,
                TargetPath = targetPath,
                BaselineBackupPath = backupPath,
                EvidenceReportPath = evidence.ReportPath,
                DamagedPhysicalImageNumber = wrongTarget,
                CorrectPhysicalImageNumber = correctTarget,
                WrongOffsetDelta = delta,
                ExpectedEntryCount = currentProbe.ExpectedEntryCount,
                ParsedEntryCount = currentProbe.ParsedEntryCount,
                EditedPayloadLength = editedPayload.Length,
                CurrentArchiveSha256 = currentSha,
                HistoricalSha256 = historicalSha,
                ExpectedHistoricalSha256 = expected.HistoricalSha256,
                RepairedSha256 = repairedSha,
                ExpectedRepairedSha256 = expected.RepairedSha256,
                IsVerified = true,
                Diagnostic =
                    $"Verified legacy index shift: logical S241 was written to physical #241; " +
                    $"images #242-#556 have a false +{delta:N0} index offset. " +
                    $"The current BMP will be preserved and migrated to physical #545.",
                HistoricalBytes = historical,
                RepairedBytes = repaired,
                EditedPayload = editedPayload
            };
        }
        catch (Exception ex)
        {
            return new RsLegacyIndexShiftRecoveryCandidate
            {
                FileName = fileName,
                TargetPath = targetPath,
                ExpectedEntryCount = currentProbe.ExpectedEntryCount,
                ParsedEntryCount = currentProbe.ParsedEntryCount,
                ExpectedHistoricalSha256 = expected.HistoricalSha256,
                ExpectedRepairedSha256 = expected.RepairedSha256,
                IsVerified = false,
                Diagnostic = "Legacy index-shift signature was suspected but could not be verified: " + ex.Message
            };
        }
    }

    private static LegacyReportEvidence? FindLegacyEvidenceReport(
        string backupRoot,
        string fileName,
        string expectedAfterSha)
    {
        foreach (var path in Directory.EnumerateFiles(backupRoot, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                var root = document.RootElement;
                if (!root.TryGetProperty("TargetRelativePath", out var target) ||
                    !string.Equals(Path.GetFileName(target.GetString()), fileName, StringComparison.OrdinalIgnoreCase) ||
                    !root.TryGetProperty("AfterSha256", out var after) ||
                    !string.Equals(after.GetString(), expectedAfterSha, StringComparison.OrdinalIgnoreCase) ||
                    !root.TryGetProperty("BeforeSha256", out var before) ||
                    !root.TryGetProperty("BackupPath", out var backup) ||
                    !root.TryGetProperty("Changes", out var changes) ||
                    changes.GetArrayLength() != 1)
                    continue;
                var change = changes[0];
                if (change.GetProperty("RowIndex").GetInt32() != 371) continue;
                var newValue = change.GetProperty("NewValue").GetString() ?? string.Empty;
                var match = Regex.Match(newValue, @"offset=([0-9A-Fa-f]+);\s*size=(\d+)");
                if (!match.Success) continue;
                return new LegacyReportEvidence(
                    path,
                    backup.GetString() ?? string.Empty,
                    before.GetString() ?? string.Empty,
                    after.GetString() ?? string.Empty,
                    371,
                    Convert.ToInt32(match.Groups[1].Value, 16),
                    int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
            catch
            {
                // Unrelated or older report schema.
            }
        }
        return null;
    }

    private static byte[] RebuildReportedHistoricalArchive(
        byte[] currentBytes,
        byte[] backupBytes,
        string fileName,
        LegacyReportEvidence evidence)
    {
        if (evidence.NewDataOffset < 0 || evidence.NewStoredLength <= 0 ||
            (long)evidence.NewDataOffset + evidence.NewStoredLength > currentBytes.LongLength)
            throw new InvalidOperationException("The report's #371 payload range is not present in the current archive.");
        var requiredLength = Math.Max(backupBytes.Length, checked(evidence.NewDataOffset + evidence.NewStoredLength));
        var historical = new byte[requiredLength];
        Buffer.BlockCopy(backupBytes, 0, historical, 0, backupBytes.Length);
        Buffer.BlockCopy(currentBytes, evidence.NewDataOffset, historical, evidence.NewDataOffset, evidence.NewStoredLength);
        var indexOffset = E5IndexParser.IndexOffset + (evidence.ImageNumber - 1) * E5IndexParser.IndexEntrySize;
        BinaryPrimitives.WriteUInt32BigEndian(historical.AsSpan(indexOffset, 4), checked((uint)evidence.NewStoredLength));
        BinaryPrimitives.WriteUInt32BigEndian(historical.AsSpan(indexOffset + 4, 4), checked((uint)evidence.NewStoredLength));
        BinaryPrimitives.WriteUInt32BigEndian(historical.AsSpan(indexOffset + 8, 4), checked((uint)evidence.NewDataOffset));
        var probe = E5IndexParser.Probe(historical, fileName);
        if (!probe.IsComplete) throw new E5ArchiveIntegrityException(probe);
        return historical;
    }

    private IReadOnlyList<string> BuildIntegrityFindings(
        string targetPath,
        E5ArchiveTopologySnapshot current,
        string? backupPath)
    {
        var findings = new List<string>();
        if (current.SharedPayloadGroupCount > 0)
            findings.Add($"当前档案存在 {current.SharedPayloadGroupCount} 组共享载荷；共享目标禁止普通像素原址写回。");
        if (current.OverlapPairCount > 0)
            findings.Add($"当前档案存在 {current.OverlapPairCount} 对部分重叠载荷；必须先恢复或整理。");
        if (current.GapBytes > 0) findings.Add($"活动载荷之间包含 {current.GapBytes:N0} 字节间隙。");
        if (current.TailBytes > 0) findings.Add($"最后一个活动载荷之后包含 {current.TailBytes:N0} 字节尾部数据。");
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath)) return findings;

        var backup = _e5.CaptureTopology(backupPath, Path.GetFileName(targetPath));
        if (backup.EntryCount != current.EntryCount)
        {
            findings.Add($"最近兼容备份条目数不同：当前 {current.EntryCount}，备份 {backup.EntryCount}。");
            return findings;
        }
        var pairs = backup.Entries.Zip(current.Entries).ToArray();
        var moved = pairs.Count(pair => pair.First.DataOffset != pair.Second.DataOffset);
        var payloadChanged = pairs.Count(pair => !pair.First.StoredSha256.Equals(pair.Second.StoredSha256, StringComparison.OrdinalIgnoreCase));
        var formatChanged = pairs.Count(pair => !pair.First.Kind.Equals(pair.Second.Kind, StringComparison.OrdinalIgnoreCase));
        var untouchedMoved = pairs.Count(pair => pair.First.DataOffset != pair.Second.DataOffset &&
                                                 pair.First.StoredSha256.Equals(pair.Second.StoredSha256, StringComparison.OrdinalIgnoreCase));
        if (moved > 0) findings.Add($"相对最近兼容备份有 {moved} 个索引偏移变化，其中 {untouchedMoved} 个载荷内容未变但被整体移动。");
        if (payloadChanged > 0) findings.Add($"相对最近兼容备份有 {payloadChanged} 个条目存储载荷发生变化。");
        if (formatChanged > 0) findings.Add($"相对最近兼容备份有 {formatChanged} 个条目发生格式变化。");
        if (moved == 0 && payloadChanged == 0 && formatChanged == 0)
            findings.Add("当前档案与最近兼容备份的索引、格式和活动载荷一致。");
        return findings;
    }

    private bool TryBuildRestoredBytes(
        CczProject project,
        string currentPath,
        string backupPath,
        int imageNumber,
        Bitmap currentBitmap,
        E5ImageEntryInfo original,
        byte[] originalBytes,
        out byte[] restored,
        out int nearest,
        out string warning)
    {
        restored = Array.Empty<byte>();
        nearest = 0;
        warning = string.Empty;
        if (original.Kind == "RAW")
        {
            var spec = _raw.ResolveSpec(currentPath);
            if (currentBitmap.Width != spec.Width || currentBitmap.Height % spec.FrameHeight != 0) return false;
            var encoded = _raw.EncodeBitmap(project, currentBitmap, $"repair {Path.GetFileName(currentPath)} #{imageNumber}", spec, strictHeight: false);
            if (encoded.RawBytes.Length != original.DecodedLength) return false;
            using var decoded = _raw.DecodeRawBytes(project, encoded.RawBytes, "repair verify", new E5RawImageSpec(spec.FileName, spec.Width, spec.FrameHeight, null), false);
            if (decoded.Size != currentBitmap.Size) return false;
            restored = encoded.RawBytes;
            nearest = encoded.NearestPalettePixels;
            warning = nearest > 0 ? $"{nearest:N0} 个像素将量化为 RAW 调色板最近色。" : string.Empty;
            return true;
        }

        var backupTarget = new EditableImageTarget
        {
            Kind = EditableImageTargetKind.E5Standard,
            TargetPath = backupPath,
            ImageNumber = imageNumber
        };
        var storage = _storage.Inspect(backupTarget);
        if (storage.Kind != EditableImageStorageKind.Bmp24 || storage.Width != currentBitmap.Width || storage.Height != currentBitmap.Height)
            return false;
        restored = _storage.EncodeBmp24PreservingContainer(storage, originalBytes, currentBitmap);
        using var decodedBmp = DecodeArgb(restored);
        if (decodedBmp == null || decodedBmp.Size != currentBitmap.Size) return false;
        var bmpRoundTrip = _storage.EncodeBmp24PreservingContainer(storage, restored, decodedBmp);
        if (!bmpRoundTrip.SequenceEqual(restored)) return false;
        warning = string.Empty;
        return true;
    }

    private IEnumerable<string> FindCompatibleBackups(CczProject project, string targetPath, int entryCount, byte[] targetBytes)
    {
        var safeRelative = Path.GetRelativePath(project.GameRoot, targetPath)
            .Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in ProjectBackupPathService.GetReadableBackupRoots(project).Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(root, "*" + Path.GetFileName(targetPath), SearchOption.TopDirectoryOnly)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                if (!emitted.Add(path)) continue;
                var name = Path.GetFileName(path);
                if (!name.EndsWith(safeRelative, StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(Path.GetFileName(targetPath), StringComparison.OrdinalIgnoreCase)) continue;
                IReadOnlyList<E5ImageEntryInfo> entries;
                byte[] bytes;
                try { entries = _e5.ReadIndex(path, Path.GetFileName(targetPath)); bytes = File.ReadAllBytes(path); }
                catch { continue; }
                if (entries.Count != entryCount || bytes.Length < 0x110 || targetBytes.Length < 0x110) continue;
                if (!bytes.AsSpan(0, 0x110).SequenceEqual(targetBytes.AsSpan(0, 0x110))) continue;
                yield return path;
            }
        }
    }

    private static IEnumerable<string> FindConversionReports(CczProject project, string targetPath)
    {
        var fileName = Path.GetFileName(targetPath);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in ProjectBackupPathService.GetReadableBackupRoots(project).Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(root, "*Report*.*", SearchOption.TopDirectoryOnly)
                         .Where(path => Path.GetExtension(path) is ".json" or ".txt")
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                if (!emitted.Add(path)) continue;
                string text;
                try { text = File.ReadAllText(path); }
                catch { continue; }
                if (!text.Contains(fileName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!(text.Contains("RAW", StringComparison.OrdinalIgnoreCase) || text.Contains("BMP", StringComparison.OrdinalIgnoreCase)) ||
                    !text.Contains("PNG", StringComparison.OrdinalIgnoreCase)) continue;
                yield return path;
            }
        }
    }

    private E5ImageBatchReplaceRequest BuildRecoveryRequest(RsArchiveRepairCandidate candidate)
        => new()
        {
            ImageNumber = candidate.ImageNumber,
            SourceBytes = candidate.RestoredBytes,
            SourceBytesAreRaw = candidate.RestoreKind == "RAW",
            SourceLabel = $"档案修复：保留当前像素，恢复为 {candidate.RestoreKind}",
            OperationKind = "R/S 档案格式恢复",
            ExpectedTargetKind = "PNG",
            ExpectedTargetSha256 = candidate.CurrentStoredSha256,
            AllowFormatConversion = true,
            PlacementPolicy = E5ImageWritePlacementPolicy.CompactRewrite
        };

    private E5ImageBatchReplaceRequest? BuildCompactOnlyRequest(string path)
    {
        var entry = _e5.ReadIndex(path).FirstOrDefault(item => !item.IsCompressed && item.Kind is "RAW" or "BMP" or "PNG" or "JPG");
        if (entry == null) return null;
        var stored = _e5.ReadStoredEntryBytes(path, entry.ImageNumber);
        return new E5ImageBatchReplaceRequest
        {
            ImageNumber = entry.ImageNumber,
            SourceBytes = stored,
            SourceBytesAreRaw = entry.Kind == "RAW",
            SourceLabel = "仅整理活动载荷",
            OperationKind = "R/S 档案紧凑整理",
            ExpectedTargetKind = entry.Kind,
            ExpectedTargetSha256 = Convert.ToHexString(SHA256.HashData(stored)),
            PlacementPolicy = E5ImageWritePlacementPolicy.CompactRewrite
        };
    }

    private RsArchiveRepairResult RestoreWholeArchive(CczProject project, RsArchiveRepairArchivePreview archive, string backupPath)
    {
        var currentEntries = _e5.ReadIndex(archive.TargetPath);
        var backupEntries = _e5.ReadIndex(backupPath, archive.FileName);
        if (currentEntries.Count != backupEntries.Count)
            throw new InvalidOperationException("整档备份的索引条目数量与当前档案不一致。");
        var beforeSize = new FileInfo(archive.TargetPath).Length;
        var beforeSha = _e5.ComputeArchiveSha256(archive.TargetPath);
        var safetyBackup = CreateSafetyBackup(project, archive.TargetPath);
        var temp = archive.TargetPath + ".CCZModStudio.repair." + Guid.NewGuid().ToString("N") + ".tmp";
        var replaced = false;
        try
        {
            File.Copy(backupPath, temp, true);
            if (_e5.ReadIndex(temp, archive.FileName).Count != backupEntries.Count)
                throw new InvalidOperationException("整档备份的索引无法完整复读。");
            File.Move(temp, archive.TargetPath, true);
            replaced = true;
            if (_e5.ReadIndex(archive.TargetPath).Count != backupEntries.Count)
                throw new InvalidOperationException("整档恢复后复读索引失败。");
        }
        catch
        {
            if (replaced)
            {
                File.Copy(safetyBackup, temp, true);
                File.Move(temp, archive.TargetPath, true);
                if (!_e5.ComputeArchiveSha256(archive.TargetPath).Equals(beforeSha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("整档恢复失败，且自动回滚后的 SHA-256 与恢复前不一致。");
            }
            throw;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        var afterSize = new FileInfo(archive.TargetPath).Length;
        Invalidate([archive.TargetPath]);
        var report = WriteReport(project, RsArchiveRepairMode.RestoreWholeBackup, [archive.TargetPath], [safetyBackup], beforeSize, afterSize,
            new RsArchiveRepairPreview { Archives = [archive] });
        return new RsArchiveRepairResult
        {
            ChangedArchives = [archive.TargetPath], BackupPaths = [safetyBackup], OldTotalSize = beforeSize,
            NewTotalSize = afterSize, ReportPath = report
        };
    }

    private RsArchiveRepairResult ExecuteLegacyIndexShiftRecovery(
        CczProject project,
        RsArchiveRepairPreview preview)
    {
        var candidates = preview.LegacyIndexShiftRecoveries
            .OrderBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length != 3 || candidates.Any(candidate => !candidate.IsVerified))
            throw new InvalidOperationException(
                "Legacy S241 recovery requires verified Unit_mov.e5, Unit_atk.e5 and Unit_spc.e5 candidates.");

        var staged = new List<(RsLegacyIndexShiftRecoveryCandidate Candidate, string TempPath)>();
        var backups = new List<string>();
        var replaced = new List<(RsLegacyIndexShiftRecoveryCandidate Candidate, string BackupPath)>();
        var oldTotal = candidates.Sum(candidate => new FileInfo(candidate.TargetPath).Length);
        try
        {
            foreach (var candidate in candidates)
            {
                var currentSha = _e5.ComputeArchiveSha256(candidate.TargetPath);
                if (!currentSha.Equals(candidate.CurrentArchiveSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"{candidate.FileName} changed after the recovery preview. Scan again before repairing.");
                var temp = candidate.TargetPath + ".CCZModStudio.legacy-s241." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, candidate.RepairedBytes);
                var tempProbe = _e5.ProbeIndex(temp, candidate.FileName);
                if (!tempProbe.IsComplete || tempProbe.ExpectedEntryCount != candidate.ExpectedEntryCount)
                    throw new E5ArchiveIntegrityException(tempProbe);
                var tempSha = _e5.ComputeArchiveSha256(temp);
                if (!tempSha.Equals(candidate.ExpectedRepairedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"{candidate.FileName} staged SHA {tempSha} does not match {candidate.ExpectedRepairedSha256}.");
                VerifyLegacyRecoveryPayloads(candidate, File.ReadAllBytes(temp), tempProbe.Entries);
                staged.Add((candidate, temp));
            }

            // Only create the complete safety-backup set after every staged file
            // has passed index, payload and expected-SHA verification.
            foreach (var item in staged)
            {
                var backup = CreateSafetyBackup(project, item.Candidate.TargetPath);
                if (!_e5.ComputeArchiveSha256(backup)
                        .Equals(item.Candidate.CurrentArchiveSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Safety backup verification failed for {item.Candidate.FileName}.");
                backups.Add(backup);
            }

            for (var index = 0; index < staged.Count; index++)
            {
                var item = staged[index];
                var backup = backups[index];
                File.Move(item.TempPath, item.Candidate.TargetPath, overwrite: true);
                replaced.Add((item.Candidate, backup));
                var finalSha = _e5.ComputeArchiveSha256(item.Candidate.TargetPath);
                if (!finalSha.Equals(item.Candidate.ExpectedRepairedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Post-replace SHA verification failed for {item.Candidate.FileName}.");
                var finalProbe = _e5.ProbeIndex(item.Candidate.TargetPath, item.Candidate.FileName);
                if (!finalProbe.IsComplete || finalProbe.ExpectedEntryCount != 556)
                    throw new E5ArchiveIntegrityException(finalProbe);
            }
        }
        catch
        {
            foreach (var item in replaced.AsEnumerable().Reverse())
            {
                var rollback = item.Candidate.TargetPath + ".CCZModStudio.legacy-rollback." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(item.BackupPath, rollback, overwrite: true);
                    File.Move(rollback, item.Candidate.TargetPath, overwrite: true);
                    if (!_e5.ComputeArchiveSha256(item.Candidate.TargetPath)
                            .Equals(item.Candidate.CurrentArchiveSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Rollback SHA verification failed for {item.Candidate.FileName}.");
                }
                finally
                {
                    if (File.Exists(rollback)) File.Delete(rollback);
                }
            }
            throw;
        }
        finally
        {
            foreach (var item in staged)
                if (File.Exists(item.TempPath)) File.Delete(item.TempPath);
        }

        var paths = candidates.Select(candidate => candidate.TargetPath).ToArray();
        var newTotal = paths.Sum(path => new FileInfo(path).Length);
        var report = WriteReport(
            project,
            RsArchiveRepairMode.LegacyIndexShiftRecovery,
            paths,
            backups,
            oldTotal,
            newTotal,
            preview);
        Invalidate(paths);
        return new RsArchiveRepairResult
        {
            ChangedArchives = paths,
            BackupPaths = backups,
            OldTotalSize = oldTotal,
            NewTotalSize = newTotal,
            ReportPath = report
        };
    }

    private static void VerifyLegacyRecoveryPayloads(
        RsLegacyIndexShiftRecoveryCandidate candidate,
        byte[] repaired,
        IReadOnlyList<E5ImageEntryInfo> repairedEntries)
    {
        var historicalProbe = E5IndexParser.Probe(candidate.HistoricalBytes, candidate.FileName);
        if (!historicalProbe.IsComplete || historicalProbe.Entries.Count != repairedEntries.Count)
            throw new InvalidOperationException($"Historical verification baseline is invalid for {candidate.FileName}.");
        for (var index = 0; index < repairedEntries.Count; index++)
        {
            var imageNumber = index + 1;
            var before = historicalProbe.Entries[index];
            var after = repairedEntries[index];
            if (before.DataOffset != after.DataOffset || before.StoredLength != after.StoredLength ||
                before.DecodedLength != after.DecodedLength)
                throw new InvalidOperationException($"Recovery changed index entry #{imageNumber} in {candidate.FileName}.");
            var expected = imageNumber == candidate.CorrectPhysicalImageNumber
                ? candidate.EditedPayload.AsSpan()
                : candidate.HistoricalBytes.AsSpan(before.DataOffset, before.StoredLength);
            if (!repaired.AsSpan(after.DataOffset, after.StoredLength).SequenceEqual(expected))
                throw new InvalidOperationException(
                    $"Recovery changed unexpected payload bytes for image #{imageNumber} in {candidate.FileName}.");
        }
    }

    private void VerifyRecoveredEntries(CczProject project, RsArchiveRepairArchivePreview archive, IReadOnlyList<E5ImageBatchReplaceRequest> requests)
    {
        var entries = _e5.ReadIndex(archive.TargetPath);
        foreach (var request in requests)
        {
            var entry = entries[request.ImageNumber - 1];
            var expectedKind = request.SourceBytesAreRaw ? "RAW" : request.SourceBytes![0] == 'B' ? "BMP" : entry.Kind;
            if (!entry.Kind.Equals(expectedKind, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{archive.FileName} #{request.ImageNumber} 恢复后格式复读失败：{entry.Kind}。");
            if (!_e5.ReadStoredEntryBytes(archive.TargetPath, request.ImageNumber).SequenceEqual(request.SourceBytes!))
                throw new InvalidOperationException($"{archive.FileName} #{request.ImageNumber} 恢复后存储字节复读失败。");
        }
    }

    private static long EstimateCompactSize(IReadOnlyList<E5ImageEntryInfo> entries)
    {
        if (entries.Count == 0) return 0;
        var firstData = entries.Min(entry => entry.DataOffset);
        return firstData + entries
            .GroupBy(entry => (entry.DataOffset, entry.StoredLength))
            .Sum(group => (long)group.Key.StoredLength);
    }

    private static Bitmap? DecodeArgb(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, false);
            using var image = Image.FromStream(stream, false, true);
            var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImage(image, new Rectangle(Point.Empty, bitmap.Size));
            return bitmap;
        }
        catch { return null; }
    }

    private static string CreateSafetyBackup(CczProject project, string path)
    {
        var root = ProjectBackupPathService.EnsureBackupRootWritable(project);
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_BeforeRsArchiveRepair_{Path.GetFileName(path)}");
        File.Copy(path, target, false);
        return target;
    }

    private static string WriteReport(CczProject project, RsArchiveRepairMode mode, IReadOnlyList<string> changed,
        IReadOnlyList<string> backups, long before, long after, RsArchiveRepairPreview preview)
    {
        var root = ProjectBackupPathService.GetBackupRoot(project);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_RsArchiveRepairReport.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            CreatedAt = DateTime.Now,
            Mode = mode.ToString(), ChangedArchives = changed, SafetyBackups = backups,
            OldTotalSize = before, NewTotalSize = after, Delta = after - before,
            Archives = preview.Archives.Select(archive => new
            {
                archive.FileName, archive.TargetPath, archive.CurrentSize, archive.CompactSize, archive.OrphanBytes,
                archive.EvidenceReports,
                archive.IntegrityFindings,
                Topology = new
                {
                    archive.Topology.FileSha256,
                    archive.Topology.IndexSha256,
                    archive.Topology.EntryCount,
                    archive.Topology.ActivePayloadBytes,
                    archive.Topology.GapBytes,
                    archive.Topology.TailBytes,
                    archive.Topology.SharedPayloadGroupCount,
                    archive.Topology.OverlapPairCount
                },
                Candidates = archive.Candidates.Select(candidate => new
                {
                    candidate.ImageNumber, candidate.CurrentKind, candidate.RestoreKind, candidate.CurrentLength,
                    candidate.RestoredLength, candidate.NearestPalettePixels, candidate.BackupPath, candidate.Warning, candidate.Selected
                })
            })
        }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        return path;
    }

    private static void Invalidate(IEnumerable<string> paths)
    {
        var list = paths.ToArray();
        E5ImageReadSessionPool.Shared.Invalidate(list);
        ImagePreviewCache.Shared.Invalidate(list);
        ProjectResourceInvalidationBus.Publish(list, ProjectResourceKind.Image);
    }

    private sealed record BackupSnapshot(string Path, IReadOnlyList<E5ImageEntryInfo> Entries, byte[] Bytes);

    private sealed record LegacyRecoveryEvidence(
        int WrongOffsetDelta,
        int FileLength,
        string HistoricalSha256,
        string RepairedSha256);

    private sealed record LegacyReportEvidence(
        string ReportPath,
        string BackupPath,
        string BeforeSha256,
        string AfterSha256,
        int ImageNumber,
        int NewDataOffset,
        int NewStoredLength);
}
