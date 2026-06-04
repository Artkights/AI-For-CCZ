using System.Buffers.Binary;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
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

    public IReadOnlyList<E5ImageEntryInfo> ReadIndex(string path)
    {
        if (!File.Exists(path)) return Array.Empty<E5ImageEntryInfo>();
        var data = File.ReadAllBytes(path);
        return ReadIndex(data);
    }

    public byte[] ReadEntryBytes(string path, int imageNumber)
    {
        var data = File.ReadAllBytes(path);
        var entries = ReadIndex(data);
        if (imageNumber <= 0 || imageNumber > entries.Count)
        {
            throw new InvalidOperationException($"E5 图号越界：#{imageNumber}/{entries.Count}。");
        }

        var entry = entries[imageNumber - 1];
        return ReadEntryBytes(data, entry);
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
        return ReplaceCore(project, targetPath, imageNumber, sourcePath, sourceBytes);
    }

    public E5ImageReplaceResult ReplaceFromEntry(CczProject project, string targetPath, int imageNumber, string sourceE5Path)
        => ReplaceFromEntry(project, targetPath, imageNumber, sourceE5Path, imageNumber);

    public E5ImageReplaceResult ReplaceFromEntry(CczProject project, string targetPath, int imageNumber, string sourceE5Path, int sourceImageNumber)
    {
        var sourceBytes = ReadEntryBytes(sourceE5Path, sourceImageNumber);
        return ReplaceCore(project, targetPath, imageNumber, $"{sourceE5Path}#{sourceImageNumber}", sourceBytes);
    }

    private E5ImageReplaceResult ReplaceCore(CczProject project, string targetPath, int imageNumber, string sourcePath, byte[] sourceBytes)
    {
        var preview = BuildPreview(project, targetPath, imageNumber, sourcePath, sourceBytes);
        var backupPath = CreateBeforeSaveBackup(project, preview.TargetPath);
        var tempPath = preview.TargetPath + ".CCZModStudio.tmp";
        File.WriteAllBytes(tempPath, preview.NewFileBytes);
        File.Move(tempPath, preview.TargetPath, overwrite: true);

        var writtenData = File.ReadAllBytes(preview.TargetPath);
        var writtenEntries = ReadIndex(writtenData);
        if (imageNumber <= 0 || imageNumber > writtenEntries.Count)
        {
            throw new InvalidOperationException($"E5 写入后复读失败：图号 #{imageNumber} 越界。");
        }

        var writtenEntry = writtenEntries[imageNumber - 1];
        if (writtenEntry.DataOffset != preview.NewDataOffset || writtenEntry.Length != preview.NewSizeBytes)
        {
            throw new InvalidOperationException(
                $"E5 写入后复读失败：索引项不匹配。预期 offset=0x{preview.NewDataOffset:X}, size={preview.NewSizeBytes}；实际 offset=0x{writtenEntry.DataOffset:X}, size={writtenEntry.Length}。");
        }

        var writtenBytes = ReadEntryBytes(writtenData, writtenEntry);
        if (!writtenBytes.SequenceEqual(sourceBytes))
        {
            throw new InvalidOperationException("E5 写入后复读失败：目标条目字节与来源字节不一致。");
        }

        var reportPath = WriteTextReport(project, preview, backupPath);
        var reportJsonPath = WriteStructuredReport(project, preview, backupPath, reportPath);

        return new E5ImageReplaceResult
        {
            TargetPath = preview.TargetPath,
            TargetRelativePath = preview.TargetRelativePath,
            SourcePath = preview.SourcePath,
            ImageNumber = preview.ImageNumber,
            IndexOffset = preview.IndexOffset,
            OldDataOffset = preview.OldDataOffset,
            NewDataOffset = preview.NewDataOffset,
            OldSizeBytes = preview.OldSizeBytes,
            NewSizeBytes = preview.NewSizeBytes,
            OldFileSizeBytes = preview.OldFileSizeBytes,
            NewFileSizeBytes = preview.NewFileSizeBytes,
            ChangedBytesEstimate = preview.ChangedBytesEstimate,
            OldFileSha256 = preview.OldFileSha256,
            NewFileSha256 = preview.NewFileSha256,
            SourceSha256 = preview.SourceSha256,
            OldKind = preview.OldKind,
            NewKind = preview.NewKind,
            SourceWidth = preview.SourceWidth,
            SourceHeight = preview.SourceHeight,
            Placement = preview.Placement,
            FormatWarnings = preview.FormatWarnings,
            RiskSummary = preview.RiskSummary,
            BackupPath = backupPath,
            ReportPath = reportPath,
            ReportJsonPath = reportJsonPath
        };
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

        var entries = ReadIndex(oldFileBytes);
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

    private static IReadOnlyList<E5ImageEntryInfo> ReadIndex(byte[] data)
    {
        var result = new List<E5ImageEntryInfo>();
        uint firstDataOffset = 0;
        for (var indexOffset = E5ImageIndexOffset; indexOffset + E5ImageIndexEntrySize <= data.Length; indexOffset += E5ImageIndexEntrySize)
        {
            if (firstDataOffset > 0 && indexOffset >= firstDataOffset)
            {
                break;
            }

            var storedSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(indexOffset, 4));
            var decodedSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(indexOffset + 4, 4));
            var dataOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(indexOffset + 8, 4));
            if (storedSize == 0 ||
                decodedSize == 0 ||
                dataOffset >= data.Length ||
                storedSize > data.Length - dataOffset ||
                storedSize > int.MaxValue ||
                decodedSize > int.MaxValue)
            {
                break;
            }

            if (firstDataOffset == 0)
            {
                firstDataOffset = dataOffset;
            }

            var compressed = storedSize != decodedSize;
            result.Add(new E5ImageEntryInfo
            {
                ImageNumber = result.Count + 1,
                IndexOffset = indexOffset,
                DataOffset = checked((int)dataOffset),
                Length = checked((int)storedSize),
                StoredLength = checked((int)storedSize),
                DecodedLength = checked((int)decodedSize),
                Kind = compressed
                    ? "LS12"
                    : DetectKind(data.AsSpan(checked((int)dataOffset), checked((int)storedSize)))
            });
        }

        return result;
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

    private static string DetectKind(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return "EMPTY";
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M') return "BMP";
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8) return "JPG";
        if (bytes.Length >= PngMagic.Length && bytes[..PngMagic.Length].SequenceEqual(PngMagic)) return "PNG";
        if (bytes[0] == 0) return "RAW";
        return $"0x{bytes[0]:X2}";
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
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
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
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
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
            "IndexOffset=0x" + preview.IndexOffset.ToString("X", CultureInfo.InvariantCulture),
            "OldDataOffset=0x" + preview.OldDataOffset.ToString("X", CultureInfo.InvariantCulture),
            "NewDataOffset=0x" + preview.NewDataOffset.ToString("X", CultureInfo.InvariantCulture),
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
                    OffsetHex = "0x" + preview.IndexOffset.ToString("X", CultureInfo.InvariantCulture),
                    ByteLength = preview.NewSizeBytes,
                    OldValue = $"offset=0x{preview.OldDataOffset:X}; size={preview.OldSizeBytes}; kind={preview.OldKind}",
                    NewValue = $"offset=0x{preview.NewDataOffset:X}; size={preview.NewSizeBytes}; kind={preview.NewKind}; source={preview.SourcePath}",
                    Annotation = $"按 E5 0x110 索引表替换单个图片条目。{preview.Placement}。"
                }
            ],
            Metadata =
            {
                ["ImageNumber"] = preview.ImageNumber.ToString(CultureInfo.InvariantCulture),
                ["IndexOffsetHex"] = "0x" + preview.IndexOffset.ToString("X", CultureInfo.InvariantCulture),
                ["OldDataOffsetHex"] = "0x" + preview.OldDataOffset.ToString("X", CultureInfo.InvariantCulture),
                ["NewDataOffsetHex"] = "0x" + preview.NewDataOffset.ToString("X", CultureInfo.InvariantCulture),
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

    private sealed record ReplacementSourceInfo(string Kind, int? Width, int? Height);

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
