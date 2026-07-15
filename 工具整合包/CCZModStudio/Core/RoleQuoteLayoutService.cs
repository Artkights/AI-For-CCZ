using System.Buffers.Binary;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public enum RoleQuoteLayoutEvidenceStatus
{
    VerifiedWritable,
    ReadOnlyEvidence,
    Unsupported
}

public sealed record RoleQuoteLayoutComponent(
    string Name,
    string FileName,
    long Offset,
    int EntryCount,
    int EntrySize,
    RoleQuoteLayoutEvidenceStatus Status,
    long? FileLength,
    bool FitsInFile,
    string Evidence)
{
    public long ByteLength => (long)EntryCount * EntrySize;
    public long EndOffsetExclusive => Offset + ByteLength;
    public bool CanWrite => Status == RoleQuoteLayoutEvidenceStatus.VerifiedWritable;
    public string OffsetHex => $"0x{Offset:X}";
}

public sealed record RoleQuoteLayout(
    string Version,
    string EvidenceSource,
    RoleQuoteLayoutComponent CriticalText,
    RoleQuoteLayoutComponent RetreatText,
    RoleQuoteLayoutComponent SpecialCriticalMapping,
    bool Legacy66RetreatRegionMayContainText,
    string Warning)
{
    public object ToDiagnosticPayload() => new
    {
        Version,
        EvidenceSource,
        CriticalText,
        RetreatText,
        SpecialCriticalMapping,
        Legacy66RetreatRegionMayContainText,
        Warning
    };
}

public sealed class RoleQuoteLayoutService
{
    public const long CriticalTextOffset = 140_000;
    public const long RetreatText65Offset = 440_200;
    public const long RetreatText66Offset = 491_600;
    public const int CriticalTextCount = 99;
    public const int RetreatTextCount = 49;
    public const int TextRowSize = 200;
    public const long SpecialCriticalMappingOffset = 0x89C30;
    public const int SpecialCriticalMappingCount = 21;
    public const int SpecialCriticalMappingEntrySize = 4;
    public const long Verified65ExeSize = CczEngineProfileService.Version65ExeSize;
    public const long Verified66RevisedExeSize = CczEngineProfileService.Version66ExeSize;
    public const long VerifiedQinger66ExeSize = 1_413_120;

    public RoleQuoteLayout Resolve(CczProject project)
    {
        var profile = new CczEngineProfileService().Detect(project);
        var version = profile.VersionHint;
        var is65 = version.Equals("6.5", StringComparison.OrdinalIgnoreCase);
        var is66 = version.Equals("6.6", StringComparison.OrdinalIgnoreCase);
        var imsgPath = project.ResolveGameFile("Imsg.e5");
        var exePath = project.ResolveGameFile("Ekd5.exe");
        var imsgLength = TryGetLength(imsgPath);
        var exeLength = TryGetLength(exePath);
        var verifiedEngineFamily = is65 && exeLength == Verified65ExeSize ||
                                   is66 && exeLength is Verified66RevisedExeSize or VerifiedQinger66ExeSize;

        var critical = BuildTextComponent(
            "CriticalText",
            CriticalTextOffset,
            CriticalTextCount,
            imsgLength,
            verifiedEngineFamily,
            is66
                ? "6.6 local baselines confirm Imsg.e5 critical quotes at decimal offset 140000 (0x222E0), 99 fixed GBK rows of 200 bytes."
                : "6.5 HexTable and local baseline confirm Imsg.e5 critical quotes at decimal offset 140000, 99 fixed GBK rows of 200 bytes.");

        var retreatOffset = is66 ? RetreatText66Offset : RetreatText65Offset;
        var retreat = BuildTextComponent(
            "RetreatText",
            retreatOffset,
            RetreatTextCount,
            imsgLength,
            verifiedEngineFamily,
            is66
                ? "6.6 local baselines confirm the active retreat table at decimal offset 491600 (0x78050); 491600 + 49*200 equals the observed Imsg.e5 length 501400. The 6.5 offset 440200 is not an active 6.6 retreat-table address."
                : "6.5 HexTable and local baseline confirm the retreat table at decimal offset 440200, 49 fixed GBK rows of 200 bytes.");

        var mappingFits = Fits(exeLength, SpecialCriticalMappingOffset, SpecialCriticalMappingCount, SpecialCriticalMappingEntrySize);
        var reference = is66 && mappingFits
            ? VerifyExecutableReference(exePath, SpecialCriticalMappingOffset)
            : new PeReferenceEvidence(false, null, null, "PE executable-reference verification is only required for recognized 6.6 engines.");
        var mappingStatus = !mappingFits || !verifiedEngineFamily
            ? RoleQuoteLayoutEvidenceStatus.Unsupported
            : is66 && !reference.Verified
                ? RoleQuoteLayoutEvidenceStatus.ReadOnlyEvidence
                : RoleQuoteLayoutEvidenceStatus.VerifiedWritable;
        var mappingEvidence = is66
            ? reference.Explanation
            : "6.5 local engine evidence confirms 21 Int32 special-critical role IDs at Ekd5.exe file offset 0x89C30.";
        var mapping = new RoleQuoteLayoutComponent(
            "SpecialCriticalMapping",
            "Ekd5.exe",
            SpecialCriticalMappingOffset,
            SpecialCriticalMappingCount,
            SpecialCriticalMappingEntrySize,
            mappingStatus,
            exeLength,
            mappingFits,
            mappingEvidence);

        var legacy66Text = is66 && LooksLikeFixedTextRegion(imsgPath, RetreatText65Offset, RetreatTextCount, TextRowSize);
        var warning = is66
            ? "6.6 uses Imsg.e5 @ 0x78050 for retreat quotes. Content at the legacy 0x6B788 region is diagnostic evidence only and must never be migrated or overwritten automatically."
            : string.Empty;
        if ((is65 || is66) && !verifiedEngineFamily)
        {
            warning = $"{warning} Ekd5.exe length {exeLength?.ToString() ?? "unknown"} is outside the verified role-quote engine families; all quote components remain non-writable until this derivative is separately verified.".Trim();
        }
        return new RoleQuoteLayout(
            version,
            is66 ? "Local66Baselines+ExecutableReference" : is65 ? "Local65Baseline+HexTable" : "UnsupportedVersion",
            critical,
            retreat,
            mapping,
            legacy66Text,
            warning);
    }

    public static string BuildSummary(RoleQuoteLayout layout)
        => $"{layout.Version}: 暴击 {layout.CriticalText.FileName} @ {layout.CriticalText.OffsetHex} " +
           $"[{layout.CriticalText.Status}]；撤退 {layout.RetreatText.FileName} @ {layout.RetreatText.OffsetHex} " +
           $"[{layout.RetreatText.Status}]；特殊暴击 {layout.SpecialCriticalMapping.FileName} @ {layout.SpecialCriticalMapping.OffsetHex} " +
           $"[{layout.SpecialCriticalMapping.Status}]。";

    private static RoleQuoteLayoutComponent BuildTextComponent(
        string name,
        long offset,
        int count,
        long? fileLength,
        bool supportedVersion,
        string evidence)
    {
        var fits = Fits(fileLength, offset, count, TextRowSize);
        var status = supportedVersion && fits
            ? RoleQuoteLayoutEvidenceStatus.VerifiedWritable
            : supportedVersion && fileLength.HasValue
                ? RoleQuoteLayoutEvidenceStatus.Unsupported
                : RoleQuoteLayoutEvidenceStatus.Unsupported;
        return new RoleQuoteLayoutComponent(name, "Imsg.e5", offset, count, TextRowSize, status, fileLength, fits, evidence);
    }

    private static bool Fits(long? fileLength, long offset, int count, int entrySize)
        => fileLength.HasValue && offset >= 0 && count > 0 && entrySize > 0 &&
           fileLength.Value >= checked(offset + (long)count * entrySize);

    private static long? TryGetLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool LooksLikeFixedTextRegion(string path, long offset, int count, int rowSize)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var stream = File.OpenRead(path);
            if (stream.Length < offset + (long)count * rowSize) return false;
            stream.Position = offset;
            var buffer = new byte[count * rowSize];
            stream.ReadExactly(buffer);
            var populated = 0;
            for (var row = 0; row < count; row++)
            {
                var span = buffer.AsSpan(row * rowSize, rowSize);
                if (span.IndexOfAnyExcept((byte)0, (byte)0x20) >= 0) populated++;
            }

            return populated >= 3;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static PeReferenceEvidence VerifyExecutableReference(string exePath, long targetFileOffset)
    {
        try
        {
            var bytes = File.ReadAllBytes(exePath);
            if (bytes.Length < 0x100 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
                return new(false, null, null, "Ekd5.exe is not a valid MZ/PE image; the 6.6 mapping remains read-only evidence.");
            var pe = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x3C, 4));
            if (pe < 0 || pe + 24 > bytes.Length || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pe, 4)) != 0x00004550)
                return new(false, null, null, "Ekd5.exe has no valid PE signature; the 6.6 mapping remains read-only evidence.");

            var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pe + 6, 2));
            var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pe + 20, 2));
            var optional = pe + 24;
            if (optional + optionalSize > bytes.Length || optionalSize < 32)
                return new(false, null, null, "Ekd5.exe PE optional header is incomplete; the 6.6 mapping remains read-only evidence.");
            var imageBase = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(optional + 28, 4));
            var sectionTable = optional + optionalSize;
            var sections = new List<PeSection>();
            for (var i = 0; i < sectionCount; i++)
            {
                var position = sectionTable + i * 40;
                if (position + 40 > bytes.Length) break;
                sections.Add(new PeSection(
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 12, 4)),
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 16, 4)),
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 20, 4)),
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 36, 4))));
            }

            var targetSection = sections.FirstOrDefault(section =>
                targetFileOffset >= section.RawOffset && targetFileOffset < (long)section.RawOffset + section.RawSize);
            if (targetSection == null)
                return new(false, null, null, "The special-critical file offset is not mapped by any PE section.");
            var targetVa = checked(imageBase + targetSection.VirtualAddress + (uint)(targetFileOffset - targetSection.RawOffset));
            Span<byte> needle = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(needle, targetVa);
            foreach (var section in sections.Where(section => (section.Characteristics & 0x20000000u) != 0))
            {
                var start = checked((int)section.RawOffset);
                var length = checked((int)Math.Min(section.RawSize, (uint)Math.Max(0, bytes.Length - start)));
                if (start < 0 || start >= bytes.Length || length < 4) continue;
                var relative = bytes.AsSpan(start, length).IndexOf(needle);
                if (relative < 0) continue;
                var referenceOffset = start + relative;
                return new(
                    true,
                    targetVa,
                    referenceOffset,
                    $"Verified executable PE reference to VA 0x{targetVa:X8} (file offset 0x{targetFileOffset:X}) at executable file offset 0x{referenceOffset:X}.");
            }

            return new(false, targetVa, null, $"No executable PE section references expected VA 0x{targetVa:X8}; 6.6 mapping data may be read as evidence but cannot be written.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            return new(false, null, null, $"PE reference verification failed: {ex.Message}");
        }
    }

    private sealed record PeSection(uint VirtualAddress, uint RawSize, uint RawOffset, uint Characteristics);
    private sealed record PeReferenceEvidence(bool Verified, uint? TargetVirtualAddress, int? ReferenceFileOffset, string Explanation);
}
