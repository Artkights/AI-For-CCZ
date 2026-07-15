using System.Security.Cryptography;
using System.Text;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ExeCodeCaveScanner
{
    private const uint ImageScnMemExecute = 0x20000000;
    private static readonly BlockedCodeCaveRange LargeCaveEBlocked = new()
    {
        CaveId = "legacy-large-cave-E",
        StartVirtualAddress = 0x00601A9A,
        EndVirtualAddress = 0x00602500,
        Status = "blocked-unmapped",
        Reason = "当前 6.5 未加密基底无法将 601A9A-602500 映射到 PE 文件偏移，知识库已禁止作为默认代码洞。"
    };

    private readonly EnginePatchProfileService _engineProfile = new();

    public ExeCodeCaveScanResult Scan(CczProject project, string targetFileName = "Ekd5.exe", int minimumLength = 8, bool includeZeroFill = false, bool includeMixedFill = false)
    {
        if (minimumLength < 1) minimumLength = 1;
        var targetPath = project.ResolveGameFile(string.IsNullOrWhiteSpace(targetFileName) ? "Ekd5.exe" : targetFileName);
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException("目标 EXE 不存在。", targetPath);
        }

        var pe = ReadPe(targetPath);
        var engine = _engineProfile.Build(project);
        var candidates = new List<ExeCodeCaveCandidate>();

        foreach (var section in pe.Sections.Where(section => section.IsExecutable))
        {
            var rawStart = checked((int)section.RawPointer);
            var rawEnd = checked((int)Math.Min((long)section.RawPointer + section.RawSize, pe.Bytes.LongLength));
            if (rawStart < 0 || rawStart >= rawEnd || rawEnd > pe.Bytes.Length) continue;

            AddRuns(pe.Bytes, section, pe.ImageBase, 0x90, "nop", minimumLength, candidates);
            if (includeZeroFill)
            {
                AddRuns(pe.Bytes, section, pe.ImageBase, 0x00, "zero", minimumLength, candidates);
            }
            if (includeMixedFill)
            {
                AddMixedRuns(pe.Bytes, section, pe.ImageBase, minimumLength, candidates);
            }
        }

        candidates.Sort(CompareCandidates);

        var warnings = new List<string>();
        warnings.AddRange(engine.Warnings);
        if (!includeZeroFill)
        {
            warnings.Add("默认只返回连续 NOP 候选；连续 00 区间需 include_zero=true 才会列出，且默认低可信。");
        }

        if (!includeMixedFill)
        {
            warnings.Add("Mixed 90/00/CC fill candidates are hidden by default; use include_mixed=true only for manual inspection.");
        }

        return new ExeCodeCaveScanResult
        {
            TargetFilePath = targetPath,
            TargetFileName = Path.GetFileName(targetPath),
            ExeSha256 = ComputeSha256(pe.Bytes),
            ExeSize = pe.Bytes.LongLength,
            ImageBase = pe.ImageBase,
            EngineVersionHint = engine.EngineVersion,
            IsKnownEngine = engine.IsKnown,
            MinimumLength = minimumLength,
            IncludeZeroFill = includeZeroFill,
            IncludeMixedFill = includeMixedFill,
            Sections = pe.Sections,
            Candidates = candidates,
            BlockedRanges = engine.BlockedRanges.Count == 0 ? [LargeCaveEBlocked] : engine.BlockedRanges,
            Warnings = warnings
        };
    }

    internal static PeImage ReadPe(string path)
        => ExecutableAnalysisSnapshotCache.Shared.GetBase(path).PeImage;

    internal static PeImage ParsePe(byte[] bytes)
    {
        ushort ReadUInt16(int offset) => BitConverter.ToUInt16(bytes, offset);
        uint ReadUInt32(int offset) => BitConverter.ToUInt32(bytes, offset);

        var peOffset = checked((int)ReadUInt32(0x3C));
        if (peOffset < 0 || peOffset + 24 > bytes.Length) throw new InvalidOperationException("PE header offset is out of range.");
        var signature = ReadUInt32(peOffset);
        if (signature != 0x00004550) throw new InvalidOperationException("不是有效 PE 文件。");

        var sectionCount = ReadUInt16(peOffset + 6);
        var optionalHeaderSize = ReadUInt16(peOffset + 20);
        var optionalHeaderStart = peOffset + 24;
        var magic = ReadUInt16(optionalHeaderStart);
        uint imageBase;
        if (magic == 0x10B)
        {
            imageBase = ReadUInt32(optionalHeaderStart + 28);
        }
        else if (magic == 0x20B)
        {
            throw new InvalidOperationException("Ekd5.exe 应为 32-bit PE，暂不支持 PE32+ 注入扫描。");
        }
        else
        {
            throw new InvalidOperationException($"未知 PE OptionalHeader magic：0x{magic:X}");
        }

        var sectionStart = optionalHeaderStart + optionalHeaderSize;
        var sections = new List<ExeSectionInfo>();
        for (var i = 0; i < sectionCount; i++)
        {
            var offset = sectionStart + i * 40;
            if (offset + 40 > bytes.Length) throw new InvalidOperationException("PE section table is truncated.");
            var name = Encoding.ASCII.GetString(bytes, offset, 8).TrimEnd('\0');
            var virtualSize = ReadUInt32(offset + 8);
            var virtualAddress = ReadUInt32(offset + 12);
            var rawSize = ReadUInt32(offset + 16);
            var rawPointer = ReadUInt32(offset + 20);
            var characteristics = ReadUInt32(offset + 36);
            sections.Add(new ExeSectionInfo
            {
                Name = name,
                VirtualAddress = virtualAddress,
                VirtualSize = virtualSize,
                RawPointer = rawPointer,
                RawSize = rawSize,
                Characteristics = characteristics,
                IsExecutable = (characteristics & ImageScnMemExecute) != 0
            });
        }

        return new PeImage(bytes, imageBase, sections);
    }

    private static void AddRuns(
        byte[] bytes,
        ExeSectionInfo section,
        uint imageBase,
        byte fill,
        string fillKind,
        int minimumLength,
        List<ExeCodeCaveCandidate> candidates)
    {
        var start = checked((int)section.RawPointer);
        var end = checked((int)Math.Min((long)section.RawPointer + section.RawSize, bytes.LongLength));
        var runStart = -1;
        var runLength = 0;

        for (var i = start; i < end; i++)
        {
            if (bytes[i] == fill)
            {
                if (runStart < 0) runStart = i;
                runLength++;
                continue;
            }

            AddRunIfLongEnough(bytes, section, imageBase, runStart, runLength, fillKind, minimumLength, candidates);
            runStart = -1;
            runLength = 0;
        }

        AddRunIfLongEnough(bytes, section, imageBase, runStart, runLength, fillKind, minimumLength, candidates);
    }

    private static void AddMixedRuns(
        byte[] bytes,
        ExeSectionInfo section,
        uint imageBase,
        int minimumLength,
        List<ExeCodeCaveCandidate> candidates)
    {
        var start = checked((int)section.RawPointer);
        var end = checked((int)Math.Min((long)section.RawPointer + section.RawSize, bytes.LongLength));
        var runStart = -1;
        var runLength = 0;
        var seenNop = false;
        var seenZero = false;
        var seenInt3 = false;

        for (var i = start; i < end; i++)
        {
            if (IsMixedFillByte(bytes[i]))
            {
                if (runStart < 0) runStart = i;
                runLength++;
                seenNop |= bytes[i] == 0x90;
                seenZero |= bytes[i] == 0x00;
                seenInt3 |= bytes[i] == 0xCC;
                continue;
            }

            AddMixedRunIfLongEnough(bytes, section, imageBase, runStart, runLength, seenNop, seenZero, seenInt3, minimumLength, candidates);
            runStart = -1;
            runLength = 0;
            seenNop = false;
            seenZero = false;
            seenInt3 = false;
        }

        AddMixedRunIfLongEnough(bytes, section, imageBase, runStart, runLength, seenNop, seenZero, seenInt3, minimumLength, candidates);
    }

    private static void AddMixedRunIfLongEnough(
        byte[] bytes,
        ExeSectionInfo section,
        uint imageBase,
        int runStart,
        int runLength,
        bool seenNop,
        bool seenZero,
        bool seenInt3,
        int minimumLength,
        List<ExeCodeCaveCandidate> candidates)
    {
        if (runStart < 0 || runLength < minimumLength) return;
        var distinct = (seenNop ? 1 : 0) + (seenZero ? 1 : 0) + (seenInt3 ? 1 : 0);
        if (distinct < 2) return;

        AddRunIfLongEnough(bytes, section, imageBase, runStart, runLength, "mixed", minimumLength, candidates);
    }

    private static void AddRunIfLongEnough(
        byte[] bytes,
        ExeSectionInfo section,
        uint imageBase,
        int runStart,
        int runLength,
        string fillKind,
        int minimumLength,
        List<ExeCodeCaveCandidate> candidates)
    {
        if (runStart < 0 || runLength < minimumLength) return;

        var rva = checked(section.VirtualAddress + (uint)(runStart - section.RawPointer));
        var startVa = checked(imageBase + rva);
        var endVa = checked(startVa + (uint)runLength - 1);
        var risk = fillKind == "nop"
            ? runLength >= 32 ? "candidate-recommended" : "candidate-small"
            : "candidate-low-confidence";
        var status = fillKind == "nop" ? "candidate" : "candidate-needs-manual-validation";
        var reason = fillKind == "nop"
            ? "连续 NOP 位于可执行节；仍需原字节锁、占用锁和动态验证。"
            : "连续 00 位于可执行节；可能是未初始化/填充空间，默认不自动分配。";

        if (fillKind == "mixed")
        {
            reason = "Mixed 90/00/CC fill in an executable section. Treat as a manual-inspection candidate only; never auto-allocate by default.";
        }

        candidates.Add(new ExeCodeCaveCandidate
        {
            CaveId = $"{section.Name}:{fillKind}:{startVa:X8}-{endVa:X8}",
            SectionName = section.Name,
            FillKind = fillKind,
            StartVirtualAddress = startVa,
            EndVirtualAddress = endVa,
            FileOffset = runStart,
            Length = runLength,
            RiskLevel = risk,
            Status = status,
            Reason = reason,
            FillBytesSummary = BuildFillBytesSummary(fillKind, bytes, runStart, runLength),
            FillByteCounts = BuildFillByteCounts(bytes, runStart, runLength),
            IsRecommended = fillKind == "nop" && runLength >= 32
        });
    }

    private static int CompareCandidates(ExeCodeCaveCandidate left, ExeCodeCaveCandidate right)
    {
        var leftFill = FillSortOrder(left.FillKind);
        var rightFill = FillSortOrder(right.FillKind);
        var fillCompare = leftFill.CompareTo(rightFill);
        if (fillCompare != 0) return fillCompare;
        var sizeCompare = right.Length.CompareTo(left.Length);
        return sizeCompare != 0 ? sizeCompare : left.StartVirtualAddress.CompareTo(right.StartVirtualAddress);
    }

    private static bool IsMixedFillByte(byte value)
        => value is 0x90 or 0x00 or 0xCC;

    private static int FillSortOrder(string fillKind)
        => fillKind.Equals("nop", StringComparison.OrdinalIgnoreCase) ? 0
            : fillKind.Equals("zero", StringComparison.OrdinalIgnoreCase) ? 1
            : 2;

    private static string BuildFillBytesSummary(string fillKind, byte[] bytes, int runStart, int runLength)
    {
        if (!fillKind.Equals("mixed", StringComparison.OrdinalIgnoreCase))
        {
            return $"{fillKind} len={runLength}";
        }

        var counts = BuildFillByteCounts(bytes, runStart, runLength);
        return $"mixed len={runLength} 90={counts.GetValueOrDefault("90")} 00={counts.GetValueOrDefault("00")} CC={counts.GetValueOrDefault("CC")}";
    }

    private static Dictionary<string, int> BuildFillByteCounts(byte[] bytes, int runStart, int runLength)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = runStart; i < runStart + runLength; i++)
        {
            var key = bytes[i].ToString("X2");
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    internal sealed record PeImage(byte[] Bytes, uint ImageBase, List<ExeSectionInfo> Sections);
}
