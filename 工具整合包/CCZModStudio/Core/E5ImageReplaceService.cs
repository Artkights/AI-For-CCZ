using System.Buffers.Binary;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class E5ImageReplaceService
{
    private const int E5ImageIndexOffset = 0x110;
    private const int E5ImageIndexEntrySize = 12;
    private const int LsHeaderLength = 16;
    private const int LsDictionaryLength = 256;
    private static readonly byte[] PngMagic = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly WriteOperationReportService _reportService = new();

    internal Action<string>? PreReplaceVerificationTestHook { get; set; }
    internal Action<string>? PostReplaceVerificationTestHook { get; set; }

    public IReadOnlyList<E5ImageEntryInfo> ReadIndex(string path)
        => ReadIndex(path, Path.GetFileName(path));

    public E5IndexProbeResult ProbeIndex(string path)
        => ProbeIndex(path, Path.GetFileName(path));

    public E5IndexProbeResult ProbeIndex(string path, string? logicalFileName)
    {
        if (!File.Exists(path))
        {
            return new E5IndexProbeResult
            {
                Path = Path.GetFullPath(path),
                IsComplete = false,
                FailureReason = "The E5 archive does not exist."
            };
        }

        var data = File.ReadAllBytes(path);
        return E5IndexParser.Probe(data, logicalFileName, Path.GetFullPath(path));
    }

    public string ComputeArchiveSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public string ComputeIndexSha256(string path)
    {
        return ProbeIndex(path).DirectorySha256;
    }

    public E5ArchiveTopologySnapshot CaptureTopology(string path, string? logicalFileName = null)
    {
        var data = File.ReadAllBytes(path);
        var probe = E5IndexParser.Probe(data, logicalFileName ?? Path.GetFileName(path), Path.GetFullPath(path));
        var entries = RequireCompleteIndex(probe);
        var topologyEntries = entries.Select(entry => new E5ArchiveTopologyEntry
        {
            ImageNumber = entry.ImageNumber,
            IndexOffset = entry.IndexOffset,
            DataOffset = entry.DataOffset,
            StoredLength = entry.StoredLength,
            DecodedLength = entry.DecodedLength,
            Kind = entry.Kind,
            StoredSha256 = Convert.ToHexString(SHA256.HashData(data.AsSpan(entry.DataOffset, entry.StoredLength)))
        }).ToArray();
        var distinctPayloads = entries
            .GroupBy(entry => (entry.DataOffset, entry.StoredLength))
            .Select(group => group.First())
            .OrderBy(entry => entry.DataOffset)
            .ToArray();
        long gaps = 0;
        long cursor = distinctPayloads.Length == 0 ? data.Length : distinctPayloads[0].DataOffset;
        foreach (var entry in distinctPayloads)
        {
            if (entry.DataOffset > cursor) gaps += entry.DataOffset - cursor;
            cursor = Math.Max(cursor, (long)entry.DataOffset + entry.StoredLength);
        }
        var overlapPairs = 0;
        for (var left = 0; left < entries.Count; left++)
        for (var right = left + 1; right < entries.Count; right++)
        {
            var a = entries[left];
            var b = entries[right];
            if (a.DataOffset == b.DataOffset && a.StoredLength == b.StoredLength) continue;
            if ((long)a.DataOffset < (long)b.DataOffset + b.StoredLength &&
                (long)b.DataOffset < (long)a.DataOffset + a.StoredLength) overlapPairs++;
        }
        return new E5ArchiveTopologySnapshot
        {
            FileSha256 = Convert.ToHexString(SHA256.HashData(data)),
            IndexSha256 = probe.DirectorySha256,
            HeaderSha256 = Convert.ToHexString(SHA256.HashData(data.AsSpan(0, Math.Min(E5ImageIndexOffset, data.Length)))),
            FileLength = data.LongLength,
            EntryCount = entries.Count,
            ActivePayloadBytes = distinctPayloads.Sum(entry => (long)entry.StoredLength),
            GapBytes = gaps,
            TailBytes = Math.Max(0, data.LongLength - cursor),
            SharedPayloadGroupCount = entries.GroupBy(entry => (entry.DataOffset, entry.StoredLength)).Count(group => group.Count() > 1),
            OverlapPairCount = overlapPairs,
            Entries = topologyEntries
        };
    }

    public IReadOnlyList<E5ImageEntryInfo> ReadIndex(string path, string? logicalFileName)
    {
        if (!File.Exists(path)) return Array.Empty<E5ImageEntryInfo>();
        return ProbeIndex(path, logicalFileName).Entries;
    }

    public byte[] ReadEntryBytes(string path, int imageNumber)
    {
        var data = File.ReadAllBytes(path);
        var entries = ReadIndex(data, Path.GetFileName(path));
        if (imageNumber <= 0 || imageNumber > entries.Count)
        {
            throw new InvalidOperationException($"E5 图号越界：#{imageNumber}/{entries.Count}。");
        }

        var entry = entries[imageNumber - 1];
        return ReadEntryBytes(data, entry);
    }

    public byte[] ReadStoredEntryBytes(string path, int imageNumber)
    {
        var data = File.ReadAllBytes(path);
        var entries = ReadIndex(data, Path.GetFileName(path));
        if (imageNumber <= 0 || imageNumber > entries.Count)
            throw new InvalidOperationException($"E5 图号越界：#{imageNumber}/{entries.Count}。");
        var entry = entries[imageNumber - 1];
        var stored = new byte[entry.StoredLength];
        Buffer.BlockCopy(data, entry.DataOffset, stored, 0, stored.Length);
        return stored;
    }

    public E5ImageReplacePreviewResult PreviewReplacement(CczProject project, string targetPath, int imageNumber, string sourcePath)
    {
        var sourceBytes = File.ReadAllBytes(sourcePath);
        return BuildPreview(project, targetPath, imageNumber, sourcePath, sourceBytes).ToPreviewResult();
    }

    public E5ImageReplacePreviewResult PreviewReplacementFromEntry(CczProject project, string targetPath, int imageNumber, string sourceE5Path)
        => PreviewReplacementFromEntry(project, targetPath, imageNumber, sourceE5Path, imageNumber);

    public E5ImageReplacePreviewResult PreviewReplacementFromEntry(CczProject project, string targetPath, int imageNumber, string sourceE5Path, int sourceImageNumber)
    {
        var sourceBytes = ReadEntryBytes(sourceE5Path, sourceImageNumber);
        return BuildPreview(project, targetPath, imageNumber, $"{sourceE5Path}#{sourceImageNumber}", sourceBytes).ToPreviewResult();
    }

    public E5ImageReplaceResult Replace(CczProject project, string targetPath, int imageNumber, string sourcePath)
    {
        var sourceBytes = File.ReadAllBytes(sourcePath);
        return ReplaceSingleThroughBatch(project, targetPath, imageNumber, sourcePath, sourceBytes);
    }

    public E5ImageReplaceResult ReplaceFromEntry(CczProject project, string targetPath, int imageNumber, string sourceE5Path)
        => ReplaceFromEntry(project, targetPath, imageNumber, sourceE5Path, imageNumber);

    public E5ImageReplaceResult ReplaceFromEntry(CczProject project, string targetPath, int imageNumber, string sourceE5Path, int sourceImageNumber)
    {
        var sourceBytes = ReadEntryBytes(sourceE5Path, sourceImageNumber);
        return ReplaceSingleThroughBatch(project, targetPath, imageNumber, $"{sourceE5Path}#{sourceImageNumber}", sourceBytes);
    }

    private E5ImageReplaceResult ReplaceSingleThroughBatch(
        CczProject project,
        string targetPath,
        int imageNumber,
        string sourceLabel,
        byte[] sourceBytes)
    {
        var result = ReplaceBatch(project, targetPath, new[]
        {
            new E5ImageBatchReplaceRequest
            {
                ImageNumber = imageNumber,
                SourceBytes = sourceBytes,
                SourceBytesAreRaw = DetectKind(sourceBytes, Path.GetFileName(targetPath)) == "RAW",
                SourceLabel = sourceLabel,
                OperationKind = "single image import",
                AllowFormatConversion = true,
                PlacementPolicy = E5ImageWritePlacementPolicy.AllowAppend
            }
        });
        var operation = result.Operations.Single();
        return new E5ImageReplaceResult
        {
            TargetPath = result.TargetPath,
            TargetRelativePath = result.TargetRelativePath,
            SourcePath = operation.SourcePath,
            ImageNumber = operation.ImageNumber,
            IndexOffset = operation.IndexOffset,
            OldDataOffset = operation.OldDataOffset,
            NewDataOffset = operation.NewDataOffset,
            OldSizeBytes = operation.OldSizeBytes,
            NewSizeBytes = operation.NewSizeBytes,
            OldFileSizeBytes = result.OldFileSizeBytes,
            NewFileSizeBytes = result.NewFileSizeBytes,
            ChangedBytesEstimate = result.ChangedBytesEstimate,
            OldFileSha256 = result.OldFileSha256,
            NewFileSha256 = result.NewFileSha256,
            SourceSha256 = operation.SourceSha256,
            OldKind = operation.OldKind,
            NewKind = operation.NewKind,
            SourceWidth = operation.SourceWidth,
            SourceHeight = operation.SourceHeight,
            Placement = operation.Placement,
            FormatWarnings = operation.FormatWarnings,
            RiskSummary = result.RiskSummary,
            BackupPath = result.BackupPath,
            ReportPath = result.ReportPath,
            ReportJsonPath = result.ReportJsonPath
        };
    }

    public E5ImageBatchReplacePreviewResult PreviewBatchReplacement(CczProject project, string targetPath, IEnumerable<E5ImageBatchReplaceRequest> requests)
        => BuildBatchPreview(project, targetPath, requests).ToPreviewResult();

    public E5ImageBatchReplaceResult ReplaceBatch(CczProject project, string targetPath, IEnumerable<E5ImageBatchReplaceRequest> requests)
    {
        var preview = BuildBatchPreview(project, targetPath, requests);
        var beforeData = File.ReadAllBytes(preview.TargetPath);
        var currentSha = Convert.ToHexString(SHA256.HashData(beforeData));
        if (!currentSha.Equals(preview.OldFileSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The E5 archive changed between preview and commit. Reload before saving.");

        var backupPath = CreateBeforeSaveBackup(project, preview.TargetPath);
        EnsureFileSha256(backupPath, preview.OldFileSha256,
            "The E5 archive changed while its safety backup was being created. Reload before saving.");
        var tempPath = preview.TargetPath + ".CCZModStudio." + Guid.NewGuid().ToString("N") + ".tmp";
        var replaced = false;
        try
        {
            File.WriteAllBytes(tempPath, preview.NewFileBytes);
            VerifyBatchArchiveMutation(beforeData, File.ReadAllBytes(tempPath), preview.Operations, Path.GetFileName(preview.TargetPath));
            PreReplaceVerificationTestHook?.Invoke(preview.TargetPath);
            EnsureFileSha256(preview.TargetPath, preview.OldFileSha256,
                "The E5 archive changed before the atomic replacement. Reload before saving.");
            File.Move(tempPath, preview.TargetPath, overwrite: true);
            replaced = true;
            PostReplaceVerificationTestHook?.Invoke(preview.TargetPath);
            VerifyBatchArchiveMutation(beforeData, File.ReadAllBytes(preview.TargetPath), preview.Operations, Path.GetFileName(preview.TargetPath));
        }
        catch
        {
            if (replaced)
                RestoreBackupOrThrow(backupPath, preview.TargetPath, preview.OldFileSha256);
            throw;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }

        var reportPath = WriteBatchTextReport(project, preview, backupPath);
        var reportJsonPath = WriteBatchStructuredReport(project, preview, backupPath, reportPath);
        return preview.ToResult(backupPath, reportPath, reportJsonPath);
    }

    private ReplacementPreviewData BuildPreview(CczProject project, string targetPath, int imageNumber, string sourcePath, byte[] sourceBytes)
    {
        targetPath = Path.GetFullPath(targetPath);
        EnsureTargetInsideProject(project, targetPath);
        if (!File.Exists(targetPath)) throw new FileNotFoundException("目标 E5 文件不存在。", targetPath);
        if (sourceBytes.Length == 0) throw new InvalidOperationException("来源图片为空，不能写入 E5。");

        var oldFileBytes = File.ReadAllBytes(targetPath);
        if (oldFileBytes.Length < E5ImageIndexOffset + E5ImageIndexEntrySize)
        {
            throw new InvalidOperationException("目标 E5 文件过短，无法读取 0x110 图片索引表。");
        }

        var entries = ReadIndex(oldFileBytes, Path.GetFileName(targetPath));
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("目标 E5 未读取到有效图片索引项，已拒绝写入。");
        }

        if (imageNumber <= 0 || imageNumber > entries.Count)
        {
            throw new InvalidOperationException($"E5 图号越界：#{imageNumber}/{entries.Count}。");
        }

        var sourceInfo = ValidateReplacementBytes(sourceBytes);
        var entry = entries[imageNumber - 1];
        var newOffset = sourceBytes.Length <= entry.StoredLength ? entry.DataOffset : oldFileBytes.Length;
        if ((uint)newOffset != newOffset || (uint)sourceBytes.Length != sourceBytes.Length)
        {
            throw new InvalidOperationException("E5 条目偏移或图片长度超过 32 位索引可表达范围，已拒绝写入。");
        }

        var newFileBytes = BuildNewFileBytes(oldFileBytes, entry, sourceBytes, newOffset);
        var warnings = BuildWarnings(oldFileBytes, entry, sourceInfo, newFileBytes.LongLength, newOffset);
        var riskSummary = BuildRiskSummary(entry, sourceInfo, oldFileBytes.LongLength, newFileBytes.LongLength, newOffset, warnings);

        return new ReplacementPreviewData(
            targetPath,
            WriteOperationReportService.ToProjectRelativePath(project, targetPath),
            sourcePath,
            imageNumber,
            entry.IndexOffset,
            entry.DataOffset,
            newOffset,
            entry.Length,
            sourceBytes.Length,
            oldFileBytes.LongLength,
            newFileBytes.LongLength,
            EstimateChangedBytes(oldFileBytes, newFileBytes),
            WriteOperationReportService.ComputeSha256(oldFileBytes),
            WriteOperationReportService.ComputeSha256(newFileBytes),
            WriteOperationReportService.ComputeSha256(sourceBytes),
            entry.Kind,
            sourceInfo.Kind,
            sourceInfo.Width,
            sourceInfo.Height,
            newOffset == entry.DataOffset ? "原址覆盖" : "追加到文件末尾并更新索引",
            warnings,
            riskSummary,
            newFileBytes);
    }

    private BatchReplacementPreviewData BuildBatchPreview(CczProject project, string targetPath, IEnumerable<E5ImageBatchReplaceRequest> requests)
    {
        targetPath = Path.GetFullPath(targetPath);
        EnsureTargetInsideProject(project, targetPath);
        if (!File.Exists(targetPath)) throw new FileNotFoundException("目标 E5 文件不存在。", targetPath);

        var requestList = requests.ToList();
        if (requestList.Count == 0)
        {
            throw new InvalidOperationException("没有可执行的 E5 图片批量操作。");
        }

        var duplicated = requestList.GroupBy(x => x.ImageNumber).FirstOrDefault(group => group.Count() > 1);
        if (duplicated != null)
        {
            throw new InvalidOperationException($"E5 批量操作中图号 #{duplicated.Key} 重复。请保证每个图号只出现一次。");
        }

        var oldFileBytes = File.ReadAllBytes(targetPath);
        var baseEntries = ReadIndex(oldFileBytes, Path.GetFileName(targetPath));
        if (baseEntries.Count == 0)
        {
            throw new InvalidOperationException("目标 E5 未读取到有效图片索引项，已拒绝批量写入。");
        }

        ValidateArchiveSnapshotContract(oldFileBytes, baseEntries, requestList);

        var resolved = new List<ResolvedBatchRequest>();
        foreach (var request in requestList.OrderBy(x => x.ImageNumber))
        {
            if (request.CharacterTarget != null)
                CharacterImageTargetResolver.ValidateRequestTarget(request.CharacterTarget, targetPath, request.ImageNumber);
            if (request.ImageNumber <= 0 || request.ImageNumber > baseEntries.Count)
            {
                throw new InvalidOperationException($"E5 图号越界：#{request.ImageNumber}/{baseEntries.Count}。");
            }

            var sourceBytes = ResolveRequestSourceBytes(request);
            if (sourceBytes.Length == 0)
            {
                throw new InvalidOperationException($"图号 #{request.ImageNumber} 的来源图片为空，不能写入 E5。");
            }

            var sourceInfo = request.SourceBytesAreRaw
                ? new ReplacementSourceInfo("RAW", null, null)
                : ValidateReplacementBytes(sourceBytes);
            var entry = baseEntries[request.ImageNumber - 1];
            if (request.PlacementPolicy is E5ImageWritePlacementPolicy.RequireInPlace
                or E5ImageWritePlacementPolicy.RequireStableOffset)
                EnsureTargetPayloadIsExclusive(baseEntries, entry);
            ValidateWriteContract(oldFileBytes, entry, request, sourceInfo, sourceBytes);
            resolved.Add(new ResolvedBatchRequest(request, entry, sourceBytes, sourceInfo));
        }

        var compactRewrite = resolved.Any(item => item.Request.PlacementPolicy == E5ImageWritePlacementPolicy.CompactRewrite);
        byte[] currentBytes;
        IReadOnlyDictionary<int, int> compactOffsets = new Dictionary<int, int>();
        if (compactRewrite)
        {
            currentBytes = BuildCompactArchive(oldFileBytes, baseEntries, resolved, out compactOffsets);
        }
        else
        {
            currentBytes = oldFileBytes;
            foreach (var item in resolved)
            {
                var entries = ReadIndex(currentBytes, Path.GetFileName(targetPath));
                var entry = entries[item.Request.ImageNumber - 1];
                var newOffset = item.Request.PlacementPolicy is E5ImageWritePlacementPolicy.RequireInPlace
                    or E5ImageWritePlacementPolicy.RequireStableOffset
                    ? entry.DataOffset
                    : item.SourceBytes.Length <= entry.StoredLength ? entry.DataOffset : currentBytes.Length;
                currentBytes = BuildNewFileBytes(currentBytes, entry, item.SourceBytes, newOffset);
            }
        }

        var operations = new List<BatchOperationData>();
        foreach (var item in resolved)
        {
            var request = item.Request;
            var entry = item.Entry;
            var sourceBytes = item.SourceBytes;
            var sourceInfo = item.SourceInfo;
            var newOffset = compactRewrite ? compactOffsets[request.ImageNumber] :
                request.PlacementPolicy is E5ImageWritePlacementPolicy.RequireInPlace
                    or E5ImageWritePlacementPolicy.RequireStableOffset || sourceBytes.Length <= entry.StoredLength
                    ? entry.DataOffset
                    : FindWrittenOffset(currentBytes, request.ImageNumber, Path.GetFileName(targetPath));
            if ((uint)newOffset != newOffset || (uint)sourceBytes.Length != sourceBytes.Length)
            {
                throw new InvalidOperationException("E5 条目偏移或图片长度超过 32 位索引可表达范围，已拒绝批量写入。");
            }
            if (request.PlacementPolicy == E5ImageWritePlacementPolicy.RequireInPlace && newOffset != entry.DataOffset)
                throw new InvalidOperationException($"图号 #{request.ImageNumber} 要求原址写回，但紧凑重建会把偏移从 0x{entry.DataOffset:X} 改为 0x{newOffset:X}，已拒绝写入。");

            if (request.PlacementPolicy == E5ImageWritePlacementPolicy.RequireStableOffset && newOffset != entry.DataOffset)
                throw new InvalidOperationException($"Image #{request.ImageNumber} must keep offset 0x{entry.DataOffset:X}.");

            var warnings = BuildWarnings(oldFileBytes, entry, sourceInfo, currentBytes.LongLength, newOffset);
            var displaySource = string.IsNullOrWhiteSpace(request.DisplaySource)
                ? $"<内存来源 #{request.ImageNumber}>"
                : request.DisplaySource;
            operations.Add(new BatchOperationData(
                request.ImageNumber,
                entry.IndexOffset,
                entry.DataOffset,
                newOffset,
                entry.Length,
                sourceBytes.Length,
                entry.Kind,
                sourceInfo.Kind,
                displaySource,
                string.IsNullOrWhiteSpace(request.OperationKind) ? "替换" : request.OperationKind,
                WriteOperationReportService.ComputeSha256(sourceBytes),
                sourceInfo.Width,
                sourceInfo.Height,
                compactRewrite
                    ? newOffset == entry.DataOffset ? "紧凑重建（偏移不变）" : "紧凑重建（清理孤立载荷）"
                    : newOffset == entry.DataOffset ? "原址覆盖" : "追加到文件末尾并更新索引",
                request.PlacementPolicy,
                warnings,
                sourceBytes,
                request.CharacterTarget));
        }

        var allWarnings = operations.SelectMany(x => x.FormatWarnings).Distinct(StringComparer.Ordinal).ToArray();
        var finalEntries = ReadIndex(currentBytes, Path.GetFileName(targetPath));
        var indexEntriesChanged = baseEntries.Zip(finalEntries).Count(pair =>
            pair.First.StoredLength != pair.Second.StoredLength ||
            pair.First.DecodedLength != pair.Second.DecodedLength ||
            pair.First.DataOffset != pair.Second.DataOffset);
        return new BatchReplacementPreviewData(
            targetPath,
            WriteOperationReportService.ToProjectRelativePath(project, targetPath),
            oldFileBytes.LongLength,
            currentBytes.LongLength,
            EstimateChangedBytes(oldFileBytes, currentBytes),
            indexEntriesChanged,
            baseEntries.Count - operations.Count,
            WriteOperationReportService.ComputeSha256(oldFileBytes),
            WriteOperationReportService.ComputeSha256(currentBytes),
            operations,
            allWarnings,
            BuildBatchRiskSummary(operations, oldFileBytes.LongLength, currentBytes.LongLength, allWarnings),
            currentBytes);
    }

    private static byte[] ResolveRequestSourceBytes(E5ImageBatchReplaceRequest request)
    {
        if (request.SourceBytes is { Length: > 0 } bytes) return bytes;
        if (string.IsNullOrWhiteSpace(request.SourcePath))
        {
            throw new InvalidOperationException($"图号 #{request.ImageNumber} 缺少来源文件。");
        }

        return File.ReadAllBytes(request.SourcePath);
    }

    private static void ValidateWriteContract(
        byte[] oldFileBytes,
        E5ImageEntryInfo entry,
        E5ImageBatchReplaceRequest request,
        ReplacementSourceInfo sourceInfo,
        byte[] sourceBytes)
    {
        if (!string.IsNullOrWhiteSpace(request.ExpectedTargetKind) &&
            !entry.Kind.Equals(request.ExpectedTargetKind, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"图号 #{request.ImageNumber} 的原格式已变化：编辑载入时预期 {request.ExpectedTargetKind}，当前为 {entry.Kind}。请重新载入后再保存。");
        }
        if (!string.IsNullOrWhiteSpace(request.ExpectedTargetKind) &&
            !request.AllowFormatConversion &&
            !sourceInfo.Kind.Equals(request.ExpectedTargetKind, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"图号 #{request.ImageNumber} 禁止格式变化：原格式 {request.ExpectedTargetKind}，待写格式 {sourceInfo.Kind}。");
        }
        if (!string.IsNullOrWhiteSpace(request.ExpectedTargetSha256))
        {
            var stored = oldFileBytes.AsSpan(entry.DataOffset, entry.StoredLength);
            var currentSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stored));
            if (!currentSha.Equals(request.ExpectedTargetSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"图号 #{request.ImageNumber} 在编辑期间已变化。请重新载入后再保存。");
        }
        if (request.PlacementPolicy == E5ImageWritePlacementPolicy.RequireInPlace &&
            sourceBytes.Length != entry.StoredLength)
        {
            throw new InvalidOperationException(
                $"图号 #{request.ImageNumber} 要求原址等长写回：原槽位 {entry.StoredLength:N0} 字节，待写 {sourceBytes.Length:N0} 字节。");
        }
        if (request.PlacementPolicy == E5ImageWritePlacementPolicy.RequireStableOffset &&
            sourceBytes.Length > entry.StoredLength)
        {
            throw new InvalidOperationException(
                $"Image #{request.ImageNumber} exceeds its stable slot: original {entry.StoredLength:N0} bytes, pending {sourceBytes.Length:N0} bytes. Use the explicit archive maintenance/import workflow.");
        }
    }

    private static void ValidateArchiveSnapshotContract(
        byte[] oldFileBytes,
        IReadOnlyList<E5ImageEntryInfo> entries,
        IReadOnlyList<E5ImageBatchReplaceRequest> requests)
    {
        var archiveSha = Convert.ToHexString(SHA256.HashData(oldFileBytes));
        var indexSha = E5IndexParser.Probe(oldFileBytes).DirectorySha256;
        foreach (var request in requests)
        {
            if (!string.IsNullOrWhiteSpace(request.ExpectedArchiveSha256) &&
                !archiveSha.Equals(request.ExpectedArchiveSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The E5 archive changed after the pixel editor loaded it. Reload before saving.");
            if (!string.IsNullOrWhiteSpace(request.ExpectedIndexSha256) &&
                !indexSha.Equals(request.ExpectedIndexSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The E5 index changed after the pixel editor loaded it. Reload before saving.");
        }
    }

    private static void EnsureTargetPayloadIsExclusive(
        IReadOnlyList<E5ImageEntryInfo> entries,
        E5ImageEntryInfo target)
    {
        var targetStart = (long)target.DataOffset;
        var targetEnd = targetStart + target.StoredLength;
        foreach (var other in entries)
        {
            if (other.ImageNumber == target.ImageNumber) continue;
            var otherStart = (long)other.DataOffset;
            var otherEnd = otherStart + other.StoredLength;
            if (targetStart >= otherEnd || otherStart >= targetEnd) continue;
            var relationship = targetStart == otherStart && targetEnd == otherEnd
                ? "shares its payload"
                : "overlaps its payload range";
            throw new InvalidOperationException(
                $"Image #{target.ImageNumber} {relationship} with image #{other.ImageNumber}; pixel editing is refused to prevent cross-entry corruption.");
        }
    }

    private static byte[] BuildCompactArchive(
        byte[] oldFileBytes,
        IReadOnlyList<E5ImageEntryInfo> entries,
        IReadOnlyList<ResolvedBatchRequest> replacements,
        out IReadOnlyDictionary<int, int> replacementOffsets)
    {
        if (entries.Count == 0) throw new InvalidOperationException("E5 索引为空，不能紧凑重建。");
        if (replacements.Any(item => item.Request.PlacementPolicy == E5ImageWritePlacementPolicy.RequireInPlace))
            throw new InvalidOperationException("同一个 E5 写回批次不能同时要求 RAW/BMP 原址覆盖和 PNG 紧凑重建。请分别保存这些条目。");

        var firstDataOffset = entries.Min(entry => entry.DataOffset);
        var indexEnd = checked(E5ImageIndexOffset + entries.Count * E5ImageIndexEntrySize);
        if (firstDataOffset < indexEnd || firstDataOffset > oldFileBytes.Length)
            throw new InvalidOperationException("E5 头部或索引区布局无效，不能紧凑重建。");

        var replacementByNumber = replacements.ToDictionary(item => item.Request.ImageNumber);
        var payloads = new List<byte[]>();
        var payloadKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        var entryPayloadIndices = new int[entries.Count];
        var entryStoredLengths = new int[entries.Count];
        var entryDecodedLengths = new int[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            byte[] payload;
            string key;
            int decodedLength;
            if (replacementByNumber.TryGetValue(entry.ImageNumber, out var replacement))
            {
                payload = replacement.SourceBytes;
                decodedLength = payload.Length;
                key = $"replacement:{entry.ImageNumber}";
            }
            else
            {
                payload = oldFileBytes.AsSpan(entry.DataOffset, entry.StoredLength).ToArray();
                decodedLength = entry.DecodedLength;
                key = $"stored:{entry.DataOffset}:{entry.StoredLength}:{entry.DecodedLength}";
            }

            if (!payloadKeys.TryGetValue(key, out var payloadIndex))
            {
                payloadIndex = payloads.Count;
                payloadKeys.Add(key, payloadIndex);
                payloads.Add(payload);
            }
            entryPayloadIndices[index] = payloadIndex;
            entryStoredLengths[index] = payload.Length;
            entryDecodedLengths[index] = decodedLength;
        }

        var payloadOffsets = new int[payloads.Count];
        var length = firstDataOffset;
        for (var index = 0; index < payloads.Count; index++)
        {
            payloadOffsets[index] = length;
            length = checked(length + payloads[index].Length);
        }
        var output = new byte[length];
        Buffer.BlockCopy(oldFileBytes, 0, output, 0, firstDataOffset);
        for (var index = 0; index < payloads.Count; index++)
            Buffer.BlockCopy(payloads[index], 0, output, payloadOffsets[index], payloads[index].Length);

        var offsets = new Dictionary<int, int>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var offset = payloadOffsets[entryPayloadIndices[index]];
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entry.IndexOffset, 4), checked((uint)entryStoredLengths[index]));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entry.IndexOffset + 4, 4), checked((uint)entryDecodedLengths[index]));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entry.IndexOffset + 8, 4), checked((uint)offset));
            if (replacementByNumber.ContainsKey(entry.ImageNumber)) offsets[entry.ImageNumber] = offset;
        }
        replacementOffsets = offsets;
        return output;
    }

    private static int FindWrittenOffset(byte[] data, int imageNumber, string fileName)
    {
        var entries = ReadIndex(data, fileName);
        if (imageNumber <= 0 || imageNumber > entries.Count)
            throw new InvalidOperationException($"紧凑重建后图号 #{imageNumber} 越界。");
        return entries[imageNumber - 1].DataOffset;
    }

    private static void VerifyBatchArchiveMutation(
        byte[] oldData,
        byte[] newData,
        IReadOnlyList<BatchOperationData> operations,
        string logicalFileName)
    {
        var oldEntries = ReadIndex(oldData, logicalFileName);
        var newEntries = ReadIndex(newData, logicalFileName);
        if (oldEntries.Count != newEntries.Count)
            throw new InvalidOperationException("E5 verification failed: the image entry count changed.");

        var changed = operations.ToDictionary(operation => operation.ImageNumber);
        var compactRewrite = operations.Any(operation =>
            operation.PlacementPolicy == E5ImageWritePlacementPolicy.CompactRewrite);
        var allowAppendGrowth = operations.Any(operation =>
            operation.PlacementPolicy == E5ImageWritePlacementPolicy.AllowAppend &&
            operation.NewDataOffset != operation.OldDataOffset);
        for (var index = 0; index < oldEntries.Count; index++)
        {
            var imageNumber = index + 1;
            var before = oldEntries[index];
            var after = newEntries[index];
            if (changed.TryGetValue(imageNumber, out var operation))
            {
                if (after.DataOffset != operation.NewDataOffset ||
                    after.StoredLength != operation.NewSizeBytes ||
                    !after.Kind.Equals(operation.NewKind, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"E5 verification failed for target image #{imageNumber}: index or format mismatch.");
                var decoded = ReadEntryBytes(newData, after);
                if (!decoded.SequenceEqual(operation.SourceBytes))
                    throw new InvalidOperationException($"E5 verification failed for target image #{imageNumber}: payload mismatch.");
                continue;
            }

            if (before.StoredLength != after.StoredLength || before.DecodedLength != after.DecodedLength)
                throw new InvalidOperationException($"E5 verification failed: untouched image #{imageNumber} length changed.");
            if (!compactRewrite && before.DataOffset != after.DataOffset)
                throw new InvalidOperationException($"E5 verification failed: untouched image #{imageNumber} offset changed.");
            if (!oldData.AsSpan(before.DataOffset, before.StoredLength)
                    .SequenceEqual(newData.AsSpan(after.DataOffset, after.StoredLength)))
                throw new InvalidOperationException($"E5 verification failed: untouched image #{imageNumber} payload changed.");
        }

        if (compactRewrite)
        {
            if (!oldData.AsSpan(0, E5ImageIndexOffset).SequenceEqual(newData.AsSpan(0, E5ImageIndexOffset)))
                throw new InvalidOperationException("E5 compact verification failed: header or LS dictionary changed.");
            return;
        }

        if (!allowAppendGrowth && oldData.Length != newData.Length)
            throw new InvalidOperationException("E5 in-place verification failed: archive length changed.");
        if (allowAppendGrowth && newData.Length < oldData.Length)
            throw new InvalidOperationException("E5 append verification failed: archive length shrank.");
        var allowed = new bool[oldData.Length];
        foreach (var operation in operations)
        {
            var before = oldEntries[operation.ImageNumber - 1];
            Array.Fill(allowed, true, before.IndexOffset, E5ImageIndexEntrySize);
            if (operation.NewDataOffset == operation.OldDataOffset)
                Array.Fill(allowed, true, before.DataOffset, before.StoredLength);
        }
        for (var offset = 0; offset < oldData.Length; offset++)
        {
            if (!allowed[offset] && oldData[offset] != newData[offset])
                throw new InvalidOperationException($"E5 in-place verification failed: unexpected byte change at 0x{offset:X}.");
        }
    }

    private static void RestoreBackupOrThrow(string backupPath, string targetPath, string expectedSha256)
    {
        var restoreTemp = targetPath + ".CCZModStudio.rollback." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(backupPath, restoreTemp, overwrite: true);
            File.Move(restoreTemp, targetPath, overwrite: true);
            var restoredSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(targetPath)));
            if (!restoredSha.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Automatic E5 rollback completed but its SHA-256 did not match the pre-save archive.");
        }
        finally
        {
            if (File.Exists(restoreTemp)) File.Delete(restoreTemp);
        }
    }

    internal void RestoreVerifiedBackup(string backupPath, string targetPath, string expectedSha256)
        => RestoreBackupOrThrow(backupPath, targetPath, expectedSha256);

    private static void EnsureFileSha256(string path, string expectedSha256, string message)
    {
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message);
    }

    private static void VerifyUntouchedStoredEntries(string oldPath, string newPath, IEnumerable<int> changedImageNumbers)
    {
        var oldData = File.ReadAllBytes(oldPath);
        var newData = File.ReadAllBytes(newPath);
        var oldEntries = ReadIndex(oldData, Path.GetFileName(oldPath));
        var newEntries = ReadIndex(newData, Path.GetFileName(newPath));
        if (oldEntries.Count != newEntries.Count)
            throw new InvalidOperationException("E5 紧凑重建后索引条目数量发生变化。");
        var changed = changedImageNumbers.ToHashSet();
        for (var index = 0; index < oldEntries.Count; index++)
        {
            if (changed.Contains(index + 1)) continue;
            var oldEntry = oldEntries[index];
            var newEntry = newEntries[index];
            if (oldEntry.StoredLength != newEntry.StoredLength || oldEntry.DecodedLength != newEntry.DecodedLength ||
                !oldData.AsSpan(oldEntry.DataOffset, oldEntry.StoredLength)
                    .SequenceEqual(newData.AsSpan(newEntry.DataOffset, newEntry.StoredLength)))
            {
                throw new InvalidOperationException($"E5 紧凑重建复读失败：未修改条目 #{index + 1} 的存储字节发生变化。");
            }
        }
    }

    private static IReadOnlyList<E5ImageEntryInfo> ReadIndex(byte[] data, string? fileName = null)
        => RequireCompleteIndex(E5IndexParser.Probe(data, fileName));

    private static IReadOnlyList<E5ImageEntryInfo> RequireCompleteIndex(E5IndexProbeResult probe)
    {
        if (!probe.IsComplete) throw new E5ArchiveIntegrityException(probe);
        return probe.Entries;
    }

    private static byte[] ReadEntryBytes(byte[] data, E5ImageEntryInfo entry)
    {
        var storedBytes = new byte[entry.StoredLength];
        Buffer.BlockCopy(data, entry.DataOffset, storedBytes, 0, storedBytes.Length);
        if (!entry.IsCompressed)
        {
            return storedBytes;
        }

        if (data.Length < LsHeaderLength + LsDictionaryLength)
        {
            throw new InvalidOperationException("E5 压缩条目解码失败：文件缺少 LS 字典。");
        }

        var dictionary = new byte[LsDictionaryLength];
        Buffer.BlockCopy(data, LsHeaderLength, dictionary, 0, dictionary.Length);
        if (!TryDecodeLsEntry(dictionary, storedBytes, entry.DecodedLength, out var decoded))
        {
            throw new InvalidOperationException($"E5 压缩条目解码失败：图号 #{entry.ImageNumber}。");
        }

        return decoded;
    }

    private static bool TryDecodeLsEntry(byte[] dictionary, byte[] encoded, int decodedLength, out byte[] decoded)
    {
        decoded = new byte[decodedLength];
        if (encoded.Length == decodedLength)
        {
            Buffer.BlockCopy(encoded, 0, decoded, 0, decodedLength);
            return true;
        }

        var inputIndex = 0;
        var bitPosition = 7;
        var outputIndex = 0;
        var backDistance = 0;

        while (outputIndex < decodedLength)
        {
            if (inputIndex >= encoded.Length) return false;

            uint code = 0;
            var bitLength = 0;
            int bitSet;
            do
            {
                bitSet = (encoded[inputIndex] >> bitPosition) & 0x01;
                code = (code << 1) | (uint)bitSet;
                bitLength++;
                bitPosition--;
                if (bitPosition < 0)
                {
                    bitPosition = 7;
                    inputIndex++;
                }
            } while (bitSet != 0);

            uint mask = 0;
            while (bitLength-- > 0)
            {
                if (inputIndex >= encoded.Length) return false;
                bitSet = (encoded[inputIndex] >> bitPosition) & 0x01;
                mask = (mask << 1) | (uint)bitSet;
                bitPosition--;
                if (bitPosition < 0)
                {
                    bitPosition = 7;
                    inputIndex++;
                }
            }

            code += mask;
            if (backDistance == 0 && code >= LsDictionaryLength)
            {
                backDistance = checked((int)(code - LsDictionaryLength));
                if (backDistance == 0) return false;
                continue;
            }

            if (backDistance == 0)
            {
                if (code >= LsDictionaryLength) return false;
                decoded[outputIndex++] = dictionary[(int)code];
                continue;
            }

            var copyCount = checked((int)code + 3);
            while (copyCount-- > 0)
            {
                if (outputIndex >= decodedLength) return false;
                var sourceIndex = outputIndex - backDistance;
                if (sourceIndex < 0) return false;
                decoded[outputIndex++] = decoded[sourceIndex];
            }

            backDistance = 0;
        }

        return true;
    }

    private static ReplacementSourceInfo ValidateReplacementBytes(byte[] bytes)
    {
        var kind = DetectKind(bytes);
        if (kind is "BMP" or "JPG" or "PNG")
        {
            try
            {
                using var memory = new MemoryStream(bytes, writable: false);
                using var image = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: true);
                return new ReplacementSourceInfo(kind, image.Width, image.Height);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException("来源图片头部像 BMP/JPG/PNG，但图像解码失败，已拒绝写入。", ex);
            }
            catch (ExternalException ex)
            {
                throw new InvalidOperationException("来源图片头部像 BMP/JPG/PNG，但图像解码失败，已拒绝写入。", ex);
            }
        }

        if (kind == "RAW")
        {
            return new ReplacementSourceInfo(kind, null, null);
        }

        throw new InvalidOperationException($"来源文件不是可识别的 E5 图片条目。仅支持 BMP/JPG/PNG 或首字节 0x00 的原始帧条；当前识别为 {kind}。");
    }

    private static byte[] BuildNewFileBytes(byte[] oldFileBytes, E5ImageEntryInfo entry, byte[] sourceBytes, int newOffset)
    {
        var newLength = newOffset == oldFileBytes.Length
            ? checked(oldFileBytes.Length + sourceBytes.Length)
            : oldFileBytes.Length;
        var result = new byte[newLength];
        Buffer.BlockCopy(oldFileBytes, 0, result, 0, oldFileBytes.Length);
        Buffer.BlockCopy(sourceBytes, 0, result, newOffset, sourceBytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(entry.IndexOffset, 4), checked((uint)sourceBytes.Length));
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(entry.IndexOffset + 4, 4), checked((uint)sourceBytes.Length));
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(entry.IndexOffset + 8, 4), checked((uint)newOffset));
        return result;
    }

    private static string DetectKind(ReadOnlySpan<byte> bytes, string? fileName = null)
    {
        if (bytes.Length == 0) return "EMPTY";
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M') return "BMP";
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8) return "JPG";
        if (bytes.Length >= PngMagic.Length && bytes[..PngMagic.Length].SequenceEqual(PngMagic)) return "PNG";
        if (LooksLikeKnownRoleRaw(fileName, bytes.Length)) return "RAW";
        if (bytes[0] == 0) return "RAW";
        return HexDisplayFormatter.FormatByte(bytes[0]);
    }

    private static bool LooksLikeKnownRoleRaw(string? fileName, int length)
    {
        if (length <= 0 || string.IsNullOrWhiteSpace(fileName)) return false;
        var name = Path.GetFileName(fileName);
        var expectedLength = name.Equals("Pmapobj.e5", StringComparison.OrdinalIgnoreCase) ? 48 * 64 * 20 :
            name.Equals("Unit_atk.e5", StringComparison.OrdinalIgnoreCase) ? 64 * 64 * 12 :
            name.Equals("Unit_mov.e5", StringComparison.OrdinalIgnoreCase) ? 48 * 48 * 11 :
            name.Equals("Unit_spc.e5", StringComparison.OrdinalIgnoreCase) ? 48 * 48 * 5 :
            (int?)null;
        return expectedLength.HasValue && length is var actual &&
               (actual == expectedLength.Value || actual == expectedLength.Value + 2);
    }

    private static IReadOnlyList<string> BuildWarnings(byte[] oldFileBytes, E5ImageEntryInfo entry, ReplacementSourceInfo sourceInfo, long newFileLength, int newOffset)
    {
        var warnings = new List<string>();
        if (oldFileBytes.Length < 4 || Encoding.ASCII.GetString(oldFileBytes, 0, 4) is not ("Ls10" or "Ls11" or "Ls12"))
        {
            warnings.Add("目标文件未识别到 Ls10/Ls11/Ls12 头；虽然索引表有效，仍建议实机复查。");
        }

        if (newOffset != entry.DataOffset)
        {
            warnings.Add("来源图片大于原条目，工具将图片追加到文件末尾并更新索引，文件大小会增加。");
        }

        if (!string.Equals(entry.Kind, sourceInfo.Kind, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"图片条目格式会从 {entry.Kind} 变为 {sourceInfo.Kind}；请确认游戏可读取该格式。");
        }

        if (entry.IsCompressed)
        {
            warnings.Add($"目标条目原为 LS12 压缩载荷，stored={entry.StoredLength:N0} decoded={entry.DecodedLength:N0}；写入后会转为未压缩条目。");
        }

        if (sourceInfo.Kind == "RAW")
        {
            warnings.Add("来源是原始索引帧条，工具无法校验宽高和调色板，只做字节级写入。");
        }

        if (newFileLength > uint.MaxValue)
        {
            warnings.Add("写入后文件超过 4GB，E5 32 位索引可能不可用。");
        }

        return warnings;
    }

    private static string BuildRiskSummary(E5ImageEntryInfo entry, ReplacementSourceInfo sourceInfo, long oldFileLength, long newFileLength, int newOffset, IReadOnlyList<string> warnings)
    {
        var risks = new List<string>();
        if (newOffset != entry.DataOffset)
        {
            risks.Add($"文件将增大 {newFileLength - oldFileLength:N0} 字节；旧条目数据会留在文件中但不再被索引引用。");
        }

        if (sourceInfo.Kind == "JPG")
        {
            risks.Add("JPG 可能压缩透明底色，人物帧建议优先使用已验证的 BMP/PNG。");
        }

        if (warnings.Count > 0)
        {
            risks.Add("存在格式提示：" + string.Join("；", warnings));
        }

        return risks.Count == 0
            ? "按 0x110 索引表替换单个图片条目；写入前会备份，写入后会复读索引和条目字节。"
            : string.Join("；", risks);
    }

    private static string BuildBatchRiskSummary(
        IReadOnlyList<BatchOperationData> operations,
        long oldFileLength,
        long newFileLength,
        IReadOnlyList<string> warnings)
    {
        var risks = new List<string>
        {
            $"批量操作 {operations.Count} 条；写入前会备份，写入后逐条复读索引和条目字节。"
        };

        if (newFileLength != oldFileLength)
        {
            risks.Add($"文件大小变化 {newFileLength - oldFileLength:+#;-#;0} 字节；大于原槽位的条目会追加到文件末尾，旧条目数据保留但不再被索引引用。");
        }

        var compressedCount = operations.Count(x => x.OldKind.Equals("LS12", StringComparison.OrdinalIgnoreCase));
        if (compressedCount > 0)
        {
            risks.Add($"其中 {compressedCount} 条原为 LS12 压缩条目，写入后会转为未压缩条目。");
        }

        var rawCount = operations.Count(x => x.NewKind.Equals("RAW", StringComparison.OrdinalIgnoreCase));
        if (rawCount > 0)
        {
            risks.Add($"其中 {rawCount} 条来源为 RAW，工具无法校验宽高和调色板。");
        }

        if (warnings.Count > 0)
        {
            risks.Add("格式提示：" + string.Join("；", warnings));
        }

        return string.Join("；", risks);
    }

    private static int EstimateChangedBytes(byte[] oldBytes, byte[] newBytes)
    {
        long count = Math.Abs((long)oldBytes.Length - newBytes.Length);
        var common = Math.Min(oldBytes.Length, newBytes.Length);
        for (var i = 0; i < common; i++)
        {
            if (oldBytes[i] != newBytes[i]) count++;
        }

        return count > int.MaxValue ? int.MaxValue : (int)count;
    }

    private static void EnsureTargetInsideProject(CczProject project, string targetPath)
    {
        var gameRoot = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!targetPath.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标 E5 文件不在当前项目目录内，禁止写入：" + targetPath);
        }
    }

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = ProjectBackupPathService.EnsureBackupRootWritable(project);
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var safeRelative = MakeSafeRelativeName(project, filePath);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{safeRelative}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{stamp}_{suffix++}_{safeRelative}");
        }

        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }

    private static string MakeSafeRelativeName(CczProject project, string filePath)
    {
        var relative = Path.GetRelativePath(project.GameRoot, filePath);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            relative = relative.Replace(invalid, '_');
        }

        return relative.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
    }

    private static string WriteTextReport(CczProject project, ReplacementPreviewData preview, string backupPath)
    {
        var backupRoot = ProjectBackupPathService.GetBackupRoot(project);
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_E5ImageReplaceReport.txt");
        var lines = new[]
        {
            "CCZModStudio E5 Image Replace Report",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "GameRoot=" + project.GameRoot,
            "Target=" + preview.TargetPath,
            "TargetRelative=" + preview.TargetRelativePath,
            "ImageNumber=" + preview.ImageNumber.ToString(CultureInfo.InvariantCulture),
            "IndexOffset=" + HexDisplayFormatter.FormatOffset(preview.IndexOffset),
            "OldDataOffset=" + HexDisplayFormatter.FormatOffset(preview.OldDataOffset),
            "NewDataOffset=" + HexDisplayFormatter.FormatOffset(preview.NewDataOffset),
            "OldSize=" + preview.OldSizeBytes.ToString(CultureInfo.InvariantCulture),
            "NewSize=" + preview.NewSizeBytes.ToString(CultureInfo.InvariantCulture),
            "OldKind=" + preview.OldKind,
            "NewKind=" + preview.NewKind,
            "Placement=" + preview.Placement,
            "Source=" + preview.SourcePath,
            "Backup=" + backupPath,
            "OldFileSHA256=" + preview.OldFileSha256,
            "NewFileSHA256=" + preview.NewFileSha256,
            "SourceSHA256=" + preview.SourceSha256,
            "Warnings=" + (preview.FormatWarnings.Count == 0 ? "无" : string.Join(" | ", preview.FormatWarnings)),
            "RiskSummary=" + preview.RiskSummary,
            string.Empty,
            "说明：这是 E5 内部图片索引条目替换报告。工具只更新 0x110 索引表中指定图号的 size/offset，并写入对应图片字节；不会重排其它条目。"
        };
        File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        return reportPath;
    }

    private string WriteStructuredReport(CczProject project, ReplacementPreviewData preview, string backupPath, string reportPath)
    {
        var report = new WriteOperationReport
        {
            OperationKind = "E5图片条目替换",
            SourceAction = "E5 0x110 图片索引表单条目写入前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = preview.TargetRelativePath,
            TargetPath = preview.TargetPath,
            BackupPath = backupPath,
            TextReportPath = reportPath,
            BeforeSha256 = preview.OldFileSha256,
            AfterSha256 = preview.NewFileSha256,
            ChangedBytes = preview.ChangedBytesEstimate,
            Summary = $"替换 {preview.TargetRelativePath} 的 E5 图 #{preview.ImageNumber}：{preview.OldKind} {preview.OldSizeBytes:N0} 字节 -> {preview.NewKind} {preview.NewSizeBytes:N0} 字节，{preview.Placement}。",
            SafetyNotes = "当前规则：E5 图片索引表从绝对偏移 0x110 开始，每项 12 字节，前 4 字节和中间 4 字节为图片总字节数，后 4 字节为图片起始偏移，均按大端写入。写入只影响指定索引项和对应图片字节。",
            FormatCheckSummary = $"来源格式 {preview.NewKind}" + (preview.SourceWidth.HasValue ? $"，尺寸 {preview.SourceWidth}x{preview.SourceHeight}" : string.Empty),
            RiskSummary = preview.RiskSummary,
            Changes =
            [
                new WriteOperationChange
                {
                    Category = "E5图片条目",
                    TableName = preview.TargetRelativePath,
                    RowIndex = preview.ImageNumber,
                    ColumnName = $"图#{preview.ImageNumber}",
                    OffsetHex = HexDisplayFormatter.FormatOffset(preview.IndexOffset),
                    ByteLength = preview.NewSizeBytes,
                    OldValue = $"offset={HexDisplayFormatter.FormatOffset(preview.OldDataOffset)}; size={preview.OldSizeBytes}; kind={preview.OldKind}",
                    NewValue = $"offset={HexDisplayFormatter.FormatOffset(preview.NewDataOffset)}; size={preview.NewSizeBytes}; kind={preview.NewKind}; source={preview.SourcePath}",
                    Annotation = $"按 E5 0x110 索引表替换单个图片条目。{preview.Placement}。"
                }
            ],
            Metadata =
            {
                ["ImageNumber"] = preview.ImageNumber.ToString(CultureInfo.InvariantCulture),
                ["IndexOffsetHex"] = HexDisplayFormatter.FormatOffset(preview.IndexOffset),
                ["OldDataOffsetHex"] = HexDisplayFormatter.FormatOffset(preview.OldDataOffset),
                ["NewDataOffsetHex"] = HexDisplayFormatter.FormatOffset(preview.NewDataOffset),
                ["OldSizeBytes"] = preview.OldSizeBytes.ToString(CultureInfo.InvariantCulture),
                ["NewSizeBytes"] = preview.NewSizeBytes.ToString(CultureInfo.InvariantCulture),
                ["OldKind"] = preview.OldKind,
                ["NewKind"] = preview.NewKind,
                ["SourcePath"] = preview.SourcePath,
                ["SourceSha256"] = preview.SourceSha256,
                ["Placement"] = preview.Placement,
                ["FormatWarnings"] = preview.FormatWarnings.Count == 0 ? "无" : string.Join("；", preview.FormatWarnings)
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static string WriteBatchTextReport(CczProject project, BatchReplacementPreviewData preview, string backupPath)
    {
        var backupRoot = ProjectBackupPathService.GetBackupRoot(project);
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_E5ImageBatchReplaceReport.txt");
        var lines = new List<string>
        {
            "CCZModStudio E5 Image Batch Replace Report",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "GameRoot=" + project.GameRoot,
            "Target=" + preview.TargetPath,
            "TargetRelative=" + preview.TargetRelativePath,
            "OperationCount=" + preview.Operations.Count.ToString(CultureInfo.InvariantCulture),
            "Backup=" + backupPath,
            "OldFileSize=" + preview.OldFileSizeBytes.ToString(CultureInfo.InvariantCulture),
            "NewFileSize=" + preview.NewFileSizeBytes.ToString(CultureInfo.InvariantCulture),
            "ChangedBytesEstimate=" + preview.ChangedBytesEstimate.ToString(CultureInfo.InvariantCulture),
            "OldFileSHA256=" + preview.OldFileSha256,
            "NewFileSHA256=" + preview.NewFileSha256,
            "Warnings=" + (preview.FormatWarnings.Count == 0 ? "无" : string.Join(" | ", preview.FormatWarnings)),
            "RiskSummary=" + preview.RiskSummary,
            string.Empty,
            "Operations:"
        };
        foreach (var operation in preview.Operations)
        {
            lines.Add(
                $"#{operation.ImageNumber} {operation.OperationKind} source={operation.SourcePath} oldOffset={HexDisplayFormatter.FormatOffset(operation.OldDataOffset)} newOffset={HexDisplayFormatter.FormatOffset(operation.NewDataOffset)} oldSize={operation.OldSizeBytes} newSize={operation.NewSizeBytes} kind={operation.OldKind}->{operation.NewKind} placement={operation.Placement}");
        }

        File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        return reportPath;
    }

    private string WriteBatchStructuredReport(CczProject project, BatchReplacementPreviewData preview, string backupPath, string reportPath)
    {
        var report = new WriteOperationReport
        {
            OperationKind = "E5图片条目批量替换",
            SourceAction = "E5 0x110 图片索引表多条目写入前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = preview.TargetRelativePath,
            TargetPath = preview.TargetPath,
            BackupPath = backupPath,
            TextReportPath = reportPath,
            BeforeSha256 = preview.OldFileSha256,
            AfterSha256 = preview.NewFileSha256,
            ChangedBytes = preview.ChangedBytesEstimate,
            Summary = $"批量写入 {preview.TargetRelativePath} 的 {preview.Operations.Count} 个 E5 图片条目，文件大小 {preview.OldFileSizeBytes:N0} -> {preview.NewFileSizeBytes:N0} 字节。",
            SafetyNotes = "批量写入仍只更新 0x110 图片索引表中的指定条目，不重排其它条目；写入后逐条复读校验。",
            FormatCheckSummary = preview.FormatWarnings.Count == 0 ? "批量来源格式检查通过" : string.Join("；", preview.FormatWarnings),
            RiskSummary = preview.RiskSummary,
            Changes = preview.Operations.Select(operation => new WriteOperationChange
            {
                Category = "E5图片条目批量",
                TableName = preview.TargetRelativePath,
                RowIndex = operation.ImageNumber,
                ColumnName = $"图#{operation.ImageNumber}",
                OffsetHex = HexDisplayFormatter.FormatOffset(operation.IndexOffset),
                ByteLength = operation.NewSizeBytes,
                OldValue = $"offset={HexDisplayFormatter.FormatOffset(operation.OldDataOffset)}; size={operation.OldSizeBytes}; kind={operation.OldKind}",
                NewValue = $"offset={HexDisplayFormatter.FormatOffset(operation.NewDataOffset)}; size={operation.NewSizeBytes}; kind={operation.NewKind}; source={operation.SourcePath}",
                Annotation = $"{operation.OperationKind}；{operation.Placement}"
            }).ToList(),
            Metadata =
            {
                ["OperationCount"] = preview.Operations.Count.ToString(CultureInfo.InvariantCulture),
                ["OldFileSizeBytes"] = preview.OldFileSizeBytes.ToString(CultureInfo.InvariantCulture),
                ["NewFileSizeBytes"] = preview.NewFileSizeBytes.ToString(CultureInfo.InvariantCulture),
                ["FormatWarnings"] = preview.FormatWarnings.Count == 0 ? "无" : string.Join("；", preview.FormatWarnings)
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    private sealed record ReplacementSourceInfo(string Kind, int? Width, int? Height);

    private sealed record ResolvedBatchRequest(
        E5ImageBatchReplaceRequest Request,
        E5ImageEntryInfo Entry,
        byte[] SourceBytes,
        ReplacementSourceInfo SourceInfo);

    private sealed record BatchOperationData(
        int ImageNumber,
        int IndexOffset,
        int OldDataOffset,
        int NewDataOffset,
        int OldSizeBytes,
        int NewSizeBytes,
        string OldKind,
        string NewKind,
        string SourcePath,
        string OperationKind,
        string SourceSha256,
        int? SourceWidth,
        int? SourceHeight,
        string Placement,
        E5ImageWritePlacementPolicy PlacementPolicy,
        IReadOnlyList<string> FormatWarnings,
        byte[] SourceBytes,
        CharacterImageTargetDescriptor? CharacterTarget)
    {
        public E5ImageBatchOperationPreviewResult ToPreviewResult()
            => new()
            {
                ImageNumber = ImageNumber,
                IndexOffset = IndexOffset,
                OldDataOffset = OldDataOffset,
                NewDataOffset = NewDataOffset,
                OldSizeBytes = OldSizeBytes,
                NewSizeBytes = NewSizeBytes,
                OldKind = OldKind,
                NewKind = NewKind,
                SourcePath = SourcePath,
                OperationKind = OperationKind,
                SourceSha256 = SourceSha256,
                SourceWidth = SourceWidth,
                SourceHeight = SourceHeight,
                Placement = Placement,
                FormatWarnings = FormatWarnings,
                CharacterTarget = CharacterTarget
            };
    }

    private sealed record BatchReplacementPreviewData(
        string TargetPath,
        string TargetRelativePath,
        long OldFileSizeBytes,
        long NewFileSizeBytes,
        int ChangedBytesEstimate,
        int IndexEntriesChanged,
        int UntouchedEntriesVerified,
        string OldFileSha256,
        string NewFileSha256,
        IReadOnlyList<BatchOperationData> Operations,
        IReadOnlyList<string> FormatWarnings,
        string RiskSummary,
        byte[] NewFileBytes)
    {
        public E5ImageBatchReplacePreviewResult ToPreviewResult()
            => new()
            {
                TargetPath = TargetPath,
                TargetRelativePath = TargetRelativePath,
                OperationCount = Operations.Count,
                OldFileSizeBytes = OldFileSizeBytes,
                NewFileSizeBytes = NewFileSizeBytes,
                ChangedBytesEstimate = ChangedBytesEstimate,
                IndexEntriesChanged = IndexEntriesChanged,
                UntouchedEntriesVerified = UntouchedEntriesVerified,
                OldFileSha256 = OldFileSha256,
                NewFileSha256 = NewFileSha256,
                Operations = Operations.Select(x => x.ToPreviewResult()).ToArray(),
                FormatWarnings = FormatWarnings,
                RiskSummary = RiskSummary
            };

        public E5ImageBatchReplaceResult ToResult(string backupPath, string reportPath, string reportJsonPath)
            => new()
            {
                TargetPath = TargetPath,
                TargetRelativePath = TargetRelativePath,
                OperationCount = Operations.Count,
                OldFileSizeBytes = OldFileSizeBytes,
                NewFileSizeBytes = NewFileSizeBytes,
                ChangedBytesEstimate = ChangedBytesEstimate,
                IndexEntriesChanged = IndexEntriesChanged,
                UntouchedEntriesVerified = UntouchedEntriesVerified,
                OldFileSha256 = OldFileSha256,
                NewFileSha256 = NewFileSha256,
                Operations = Operations.Select(x => x.ToPreviewResult()).ToArray(),
                FormatWarnings = FormatWarnings,
                RiskSummary = RiskSummary,
                BackupPath = backupPath,
                ReportPath = reportPath,
                ReportJsonPath = reportJsonPath
            };
    }

    private sealed record ReplacementPreviewData(
        string TargetPath,
        string TargetRelativePath,
        string SourcePath,
        int ImageNumber,
        int IndexOffset,
        int OldDataOffset,
        int NewDataOffset,
        int OldSizeBytes,
        int NewSizeBytes,
        long OldFileSizeBytes,
        long NewFileSizeBytes,
        int ChangedBytesEstimate,
        string OldFileSha256,
        string NewFileSha256,
        string SourceSha256,
        string OldKind,
        string NewKind,
        int? SourceWidth,
        int? SourceHeight,
        string Placement,
        IReadOnlyList<string> FormatWarnings,
        string RiskSummary,
        byte[] NewFileBytes)
    {
        public E5ImageReplacePreviewResult ToPreviewResult()
        {
            return new E5ImageReplacePreviewResult
            {
                TargetPath = TargetPath,
                TargetRelativePath = TargetRelativePath,
                SourcePath = SourcePath,
                ImageNumber = ImageNumber,
                IndexOffset = IndexOffset,
                OldDataOffset = OldDataOffset,
                NewDataOffset = NewDataOffset,
                OldSizeBytes = OldSizeBytes,
                NewSizeBytes = NewSizeBytes,
                OldFileSizeBytes = OldFileSizeBytes,
                NewFileSizeBytes = NewFileSizeBytes,
                ChangedBytesEstimate = ChangedBytesEstimate,
                OldFileSha256 = OldFileSha256,
                NewFileSha256 = NewFileSha256,
                SourceSha256 = SourceSha256,
                OldKind = OldKind,
                NewKind = NewKind,
                SourceWidth = SourceWidth,
                SourceHeight = SourceHeight,
                Placement = Placement,
                FormatWarnings = FormatWarnings,
                RiskSummary = RiskSummary
            };
        }
    }
}
