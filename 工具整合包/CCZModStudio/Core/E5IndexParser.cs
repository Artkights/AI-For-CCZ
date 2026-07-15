using System.Buffers.Binary;
using System.Security.Cryptography;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal static class E5IndexParser
{
    internal const int IndexOffset = 0x110;
    internal const int IndexEntrySize = 12;
    private static readonly byte[] PngMagic = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static E5IndexProbeResult Probe(byte[] data, string? logicalFileName = null, string? path = null)
        => Probe(
            data.LongLength,
            (offset, count) => data.AsSpan(checked((int)offset), count).ToArray(),
            logicalFileName,
            path);

    public static E5IndexProbeResult Probe(
        long fileLength,
        Func<long, int, byte[]> readRange,
        string? logicalFileName = null,
        string? path = null)
    {
        if (fileLength < IndexOffset + IndexEntrySize)
            return Failure(path, fileLength, 0, 0, null, "The file is too short to contain the E5 index header.");

        byte[] first;
        try
        {
            first = readRange(IndexOffset, IndexEntrySize);
        }
        catch (Exception ex)
        {
            return Failure(path, fileLength, 0, 0, 1, "The first index entry could not be read: " + ex.Message);
        }

        if (first.Length != IndexEntrySize)
            return Failure(path, fileLength, 0, 0, 1, "The first index entry is incomplete.");

        var firstStored = BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(0, 4));
        var firstDecoded = BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(4, 4));
        var firstDataOffset = BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(8, 4));
        if (firstDataOffset <= IndexOffset)
            return Failure(path, fileLength, 0, 0, 1, $"The first payload offset 0x{firstDataOffset:X} overlaps the index header.");

        var directoryRegionLength64 = (long)firstDataOffset - IndexOffset;
        var trailerLength = (int)(directoryRegionLength64 % IndexEntrySize);
        if (trailerLength is not (0 or 4))
            return Failure(path, fileLength, 0, 0, 1,
                $"The index directory has an unsupported {trailerLength}-byte trailer; only no trailer or the verified 4-byte zero trailer is accepted.");
        var directoryLength64 = directoryRegionLength64 - trailerLength;
        if (directoryLength64 <= 0 || directoryRegionLength64 > int.MaxValue || IndexOffset + directoryRegionLength64 > fileLength)
            return Failure(path, fileLength, 0, 0, 1, "The index directory extends outside the file.");

        var expectedCount = checked((int)(directoryLength64 / IndexEntrySize));
        if (expectedCount <= 0)
            return Failure(path, fileLength, 0, 0, 1, "The index directory contains no entries.");

        byte[] directory;
        try
        {
            directory = readRange(IndexOffset, checked((int)directoryRegionLength64));
        }
        catch (Exception ex)
        {
            return Failure(path, fileLength, expectedCount, 0, 1, "The index directory could not be read: " + ex.Message);
        }

        if (directory.Length != directoryRegionLength64)
            return Failure(path, fileLength, expectedCount, 0, 1, "The index directory is truncated.");
        if (trailerLength == 4 && directory.AsSpan(directory.Length - trailerLength).IndexOfAnyExcept((byte)0) >= 0)
            return Failure(path, fileLength, expectedCount, 0, expectedCount,
                "The 4-byte E5 index trailer is not all zero bytes.");

        var directorySha = Convert.ToHexString(SHA256.HashData(directory));
        var entries = new List<E5ImageEntryInfo>(expectedCount);
        var rawEntries = new List<E5IndexRawEntry>(expectedCount);
        int? firstInvalid = null;
        var failureReason = string.Empty;
        for (var index = 0; index < expectedCount; index++)
        {
            var relative = checked(index * IndexEntrySize);
            var stored = BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(relative, 4));
            var decoded = BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(relative + 4, 4));
            var offset = BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(relative + 8, 4));
            var reason = ValidateEntry(fileLength, firstDataOffset, stored, decoded, offset);
            rawEntries.Add(new E5IndexRawEntry
            {
                ImageNumber = index + 1,
                IndexOffset = IndexOffset + relative,
                StoredLength = stored,
                DecodedLength = decoded,
                DataOffset = offset,
                IsValid = string.IsNullOrEmpty(reason),
                FailureReason = reason
            });
            if (!string.IsNullOrEmpty(reason))
            {
                firstInvalid ??= index + 1;
                if (failureReason.Length == 0) failureReason = reason;
                continue;
            }

            if (firstInvalid.HasValue) continue;
            var kind = stored != decoded
                ? "LS12"
                : DetectKind(readRange, offset, checked((int)stored), logicalFileName);
            entries.Add(new E5ImageEntryInfo
            {
                ImageNumber = index + 1,
                IndexOffset = IndexOffset + relative,
                DataOffset = checked((int)offset),
                Length = checked((int)stored),
                StoredLength = checked((int)stored),
                DecodedLength = checked((int)decoded),
                Kind = kind
            });
        }

        var complete = !firstInvalid.HasValue && entries.Count == expectedCount;
        var (shared, overlaps) = complete ? InspectTopology(entries) : (0, 0);
        return new E5IndexProbeResult
        {
            Path = path ?? string.Empty,
            FileLength = fileLength,
            ExpectedEntryCount = expectedCount,
            DirectoryTrailerLength = trailerLength,
            IsComplete = complete,
            FirstInvalidImageNumber = firstInvalid,
            FailureReason = complete ? string.Empty : failureReason,
            DirectorySha256 = directorySha,
            SharedPayloadGroupCount = shared,
            OverlapPairCount = overlaps,
            Entries = entries,
            RawEntries = rawEntries
        };
    }

    private static string ValidateEntry(long fileLength, uint firstDataOffset, uint stored, uint decoded, uint offset)
    {
        if (stored == 0) return "Stored length is zero.";
        if (decoded == 0) return "Decoded length is zero.";
        if (stored > int.MaxValue) return $"Stored length {stored} exceeds the supported range.";
        if (decoded > int.MaxValue) return $"Decoded length {decoded} exceeds the supported range.";
        if (offset < firstDataOffset) return $"Payload offset 0x{offset:X} overlaps the index directory ending at 0x{firstDataOffset:X}.";
        if (offset >= fileLength) return $"Payload offset 0x{offset:X} is beyond file length 0x{fileLength:X}.";
        if ((ulong)stored > (ulong)(fileLength - offset))
            return $"Payload range 0x{offset:X}-0x{(ulong)offset + stored:X} exceeds file length 0x{fileLength:X}.";
        return string.Empty;
    }

    private static string DetectKind(
        Func<long, int, byte[]> readRange,
        uint offset,
        int length,
        string? logicalFileName)
    {
        var prefix = readRange(offset, Math.Min(length, PngMagic.Length));
        if (prefix.Length >= 2 && prefix[0] == (byte)'B' && prefix[1] == (byte)'M') return "BMP";
        if (prefix.Length >= 2 && prefix[0] == 0xFF && prefix[1] == 0xD8) return "JPG";
        if (prefix.Length >= PngMagic.Length && prefix.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic)) return "PNG";
        if (LooksLikeKnownRoleRaw(logicalFileName, length)) return "RAW";
        if (prefix.Length > 0 && prefix[0] == 0) return "RAW";
        return prefix.Length == 0 ? "EMPTY" : prefix[0].ToString("X2");
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

    private static (int Shared, int Overlaps) InspectTopology(IReadOnlyList<E5ImageEntryInfo> entries)
    {
        var shared = entries
            .GroupBy(entry => (entry.DataOffset, entry.StoredLength))
            .Count(group => group.Count() > 1);
        var overlaps = 0;
        for (var left = 0; left < entries.Count; left++)
        for (var right = left + 1; right < entries.Count; right++)
        {
            var a = entries[left];
            var b = entries[right];
            if (a.DataOffset == b.DataOffset && a.StoredLength == b.StoredLength) continue;
            if ((long)a.DataOffset < (long)b.DataOffset + b.StoredLength &&
                (long)b.DataOffset < (long)a.DataOffset + a.StoredLength)
                overlaps++;
        }
        return (shared, overlaps);
    }

    private static E5IndexProbeResult Failure(
        string? path,
        long fileLength,
        int expectedCount,
        int parsedCount,
        int? invalidImage,
        string reason)
        => new()
        {
            Path = path ?? string.Empty,
            FileLength = fileLength,
            ExpectedEntryCount = expectedCount,
            IsComplete = false,
            FirstInvalidImageNumber = invalidImage,
            FailureReason = reason,
            Entries = parsedCount == 0 ? Array.Empty<E5ImageEntryInfo>() : throw new ArgumentOutOfRangeException(nameof(parsedCount)),
            RawEntries = Array.Empty<E5IndexRawEntry>()
        };
}
