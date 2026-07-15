using System.Buffers.Binary;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class HexzmapLayoutWriter
{
    public const int DirectoryOffset = 0x110;
    public const int DirectoryStride = 12;
    public const int SegmentPrefixSize = 2;
    public const int MaximumPublishedGridSize = 40;
    public const int MaximumPublishedMapNumber = 99;

    public HexzmapLayoutBuildResult ReplaceSegment(
        byte[] original,
        IReadOnlyList<HexzmapDirectoryEntry> entries,
        int targetIndex,
        int width,
        int height,
        byte[] terrainCells)
    {
        ValidateDimensions(width, height, terrainCells);
        var layout = ValidateLayout(original, entries);
        if (targetIndex < 0 || targetIndex >= entries.Count)
        {
            throw new InvalidOperationException($"Hexzmap 目录项 {targetIndex} 不存在。");
        }

        var segments = ReadSegments(original, entries);
        var oldLength = segments[targetIndex].Length;
        segments[targetIndex] = BuildSegment(width, height, terrainCells);
        var output = Rebuild(original, entries.Count, layout.GapBytes, segments);
        var rebuiltEntries = ParseDirectory(output);
        ValidateUnchangedSegments(segments, output, rebuiltEntries, targetIndex);

        return new HexzmapLayoutBuildResult
        {
            Bytes = output,
            OldEntryCount = entries.Count,
            NewEntryCount = entries.Count,
            TargetIndex = targetIndex,
            OldSegmentLength = oldLength,
            NewSegmentLength = segments[targetIndex].Length,
            NewSegmentOffset = rebuiltEntries[targetIndex].FileOffset,
            DirectoryGrowthBytes = 0
        };
    }

    public HexzmapLayoutBuildResult AppendSegment(
        byte[] original,
        IReadOnlyList<HexzmapDirectoryEntry> entries,
        int width,
        int height,
        byte[] terrainCells)
    {
        ValidateDimensions(width, height, terrainCells);
        var layout = ValidateLayout(original, entries);
        if (entries.Count > MaximumPublishedMapNumber)
        {
            throw new InvalidOperationException($"地图编号已达到首版上限 M{MaximumPublishedMapNumber:000}。");
        }

        var segments = ReadSegments(original, entries).ToList();
        segments.Add(BuildSegment(width, height, terrainCells));
        var output = Rebuild(original, entries.Count + 1, layout.GapBytes, segments);
        var rebuiltEntries = ParseDirectory(output);
        ValidateUnchangedSegments(segments, output, rebuiltEntries, entries.Count);

        return new HexzmapLayoutBuildResult
        {
            Bytes = output,
            OldEntryCount = entries.Count,
            NewEntryCount = entries.Count + 1,
            TargetIndex = entries.Count,
            OldSegmentLength = 0,
            NewSegmentLength = segments[^1].Length,
            NewSegmentOffset = rebuiltEntries[^1].FileOffset,
            DirectoryGrowthBytes = DirectoryStride
        };
    }

    public IReadOnlyList<HexzmapDirectoryEntry> ParseDirectory(byte[] bytes)
    {
        var entries = new List<HexzmapDirectoryEntry>();
        var firstDataOffset = 0;
        for (var offset = DirectoryOffset; offset + DirectoryStride <= bytes.Length; offset += DirectoryStride)
        {
            if (firstDataOffset > 0 && offset >= firstDataOffset) break;
            var segmentLength = ReadInt32BigEndian(bytes, offset);
            var decodedLength = ReadInt32BigEndian(bytes, offset + 4);
            var fileOffset = ReadInt32BigEndian(bytes, offset + 8);
            if (segmentLength == 0 && decodedLength == 0 && fileOffset == 0) break;
            if (segmentLength <= 0 || decodedLength < segmentLength || fileOffset < 0 ||
                (long)fileOffset + segmentLength > bytes.Length)
            {
                break;
            }

            firstDataOffset = firstDataOffset == 0 ? fileOffset : firstDataOffset;
            entries.Add(new HexzmapDirectoryEntry
            {
                Index = entries.Count,
                EntryOffset = offset,
                SegmentLength = segmentLength,
                DecodedLength = decodedLength,
                FileOffset = fileOffset,
                NextSegmentLength = decodedLength,
                MapId = $"M{entries.Count:000}",
                IsValidSegment = true
            });
        }

        return entries;
    }

    public (int Width, int Height) ReadSegmentDimensions(byte[] bytes, HexzmapDirectoryEntry entry)
    {
        if (entry.FileOffset < 0 || entry.FileOffset + SegmentPrefixSize > bytes.Length)
        {
            return (0, 0);
        }

        var rawWidth = bytes[entry.FileOffset];
        var rawHeight = bytes[entry.FileOffset + 1];
        if (rawWidth == 0 || rawHeight == 0 || rawWidth % 3 != 0 || rawHeight % 3 != 0)
        {
            return (0, 0);
        }

        return (rawWidth / 3, rawHeight / 3);
    }

    private LayoutValidation ValidateLayout(byte[] original, IReadOnlyList<HexzmapDirectoryEntry> entries)
    {
        if (original.Length < DirectoryOffset || entries.Count == 0)
        {
            throw new InvalidOperationException("Hexzmap 目录为空或文件过短，不能执行结构写入。");
        }

        var parsed = ParseDirectory(original);
        if (parsed.Count != entries.Count)
        {
            throw new InvalidOperationException($"Hexzmap 目录复读数量不一致：探针 {entries.Count}，结构读取 {parsed.Count}。");
        }

        var directoryEnd = checked(DirectoryOffset + entries.Count * DirectoryStride);
        var firstDataOffset = entries[0].FileOffset;
        if (firstDataOffset < directoryEnd)
        {
            throw new InvalidOperationException("Hexzmap 目录与数据段发生重叠。");
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!entry.IsValidSegment || entry.SegmentLength != entry.DecodedLength)
            {
                throw new InvalidOperationException($"Hexzmap 目录项 {i} 不是可重建的未压缩段。");
            }

            if (entry.EntryOffset != DirectoryOffset + i * DirectoryStride ||
                entry.FileOffset < firstDataOffset || (long)entry.FileOffset + entry.SegmentLength > original.Length)
            {
                throw new InvalidOperationException($"Hexzmap 目录项 {i} 的偏移无效。");
            }

            if (i > 0 && entries[i - 1].FileOffset + entries[i - 1].SegmentLength != entry.FileOffset)
            {
                throw new InvalidOperationException($"Hexzmap 数据段 {i - 1} 与 {i} 不连续。");
            }

            var (width, height) = ReadSegmentDimensions(original, entry);
            if (width <= 0 || height <= 0 || checked(width * height + SegmentPrefixSize) != entry.SegmentLength)
            {
                throw new InvalidOperationException($"Hexzmap 目录项 {i} 的尺寸前缀与段长不一致。");
            }
        }

        var last = entries[^1];
        if (last.FileOffset + last.SegmentLength != original.Length)
        {
            throw new InvalidOperationException("Hexzmap 最后一个数据段没有结束在文件末尾，暂不支持结构写入。");
        }

        return new LayoutValidation(original.AsSpan(directoryEnd, firstDataOffset - directoryEnd).ToArray());
    }

    private static List<byte[]> ReadSegments(byte[] original, IReadOnlyList<HexzmapDirectoryEntry> entries)
        => entries.Select(entry => original.AsSpan(entry.FileOffset, entry.SegmentLength).ToArray()).ToList();

    private static byte[] BuildSegment(int width, int height, byte[] cells)
    {
        var segment = new byte[checked(SegmentPrefixSize + cells.Length)];
        segment[0] = checked((byte)(width * 3));
        segment[1] = checked((byte)(height * 3));
        cells.CopyTo(segment, SegmentPrefixSize);
        return segment;
    }

    private static byte[] Rebuild(byte[] original, int entryCount, byte[] gapBytes, IReadOnlyList<byte[]> segments)
    {
        if (segments.Count != entryCount) throw new InvalidOperationException("Hexzmap 目录数量与数据段数量不一致。");
        var firstDataOffset = checked(DirectoryOffset + entryCount * DirectoryStride + gapBytes.Length);
        var totalLength = checked(firstDataOffset + segments.Sum(segment => segment.Length));
        var output = new byte[totalLength];
        original.AsSpan(0, DirectoryOffset).CopyTo(output);
        gapBytes.CopyTo(output, DirectoryOffset + entryCount * DirectoryStride);

        var dataOffset = firstDataOffset;
        for (var i = 0; i < segments.Count; i++)
        {
            var entryOffset = DirectoryOffset + i * DirectoryStride;
            WriteInt32BigEndian(output, entryOffset, segments[i].Length);
            WriteInt32BigEndian(output, entryOffset + 4, segments[i].Length);
            WriteInt32BigEndian(output, entryOffset + 8, dataOffset);
            segments[i].CopyTo(output, dataOffset);
            dataOffset += segments[i].Length;
        }

        return output;
    }

    private static void ValidateUnchangedSegments(
        IReadOnlyList<byte[]> expectedSegments,
        byte[] output,
        IReadOnlyList<HexzmapDirectoryEntry> outputEntries,
        int changedIndex)
    {
        if (outputEntries.Count != expectedSegments.Count)
        {
            throw new InvalidOperationException("Hexzmap 重建后的目录数量校验失败。");
        }

        for (var i = 0; i < expectedSegments.Count; i++)
        {
            var actual = output.AsSpan(outputEntries[i].FileOffset, outputEntries[i].SegmentLength);
            if (!actual.SequenceEqual(expectedSegments[i]))
            {
                throw new InvalidOperationException($"Hexzmap 重建后的数据段 {i} 复读不一致。目标段={changedIndex}。");
            }
        }
    }

    private static void ValidateDimensions(int width, int height, byte[] cells)
    {
        if (width is < 1 or > MaximumPublishedGridSize || height is < 1 or > MaximumPublishedGridSize)
        {
            throw new InvalidOperationException($"首版发布尺寸必须在 1x1 到 {MaximumPublishedGridSize}x{MaximumPublishedGridSize} 格之间。");
        }

        if (cells.Length != checked(width * height))
        {
            throw new InvalidOperationException($"地形数据长度 {cells.Length} 与地图尺寸 {width}x{height} 不一致。");
        }
    }

    private static int ReadInt32BigEndian(byte[] bytes, int offset)
        => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));

    private static void WriteInt32BigEndian(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset, 4), value);

    private sealed record LayoutValidation(byte[] GapBytes);
}
