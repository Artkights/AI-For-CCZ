using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class CheatMakerCmfProbe
{
    public const string CmfSignature = "cmf0a";
    private static readonly Regex HexAddressRegex = new(@"(?<![0-9A-Fa-f])(?:00)?[4-5][0-9A-Fa-f]{5}(?![0-9A-Fa-f])", RegexOptions.Compiled);
    private const string CczRelevantRootSample = "CczRelevantRootSample";
    private const string CheatMakerFormatSample = "CheatMakerFormatSample";
    private const string UnknownOrRejected = "UnknownOrRejected";
    private static readonly IReadOnlySet<string> SupportedSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cmf04",
        "cmf05",
        "cmf0a"
    };

    private static readonly string[] VisibleKeywordNeedles =
    {
        "Ekd5",
        "Data.e5",
        "Item.e5",
        "Imsg",
        "Star.e5",
        "Addr",
        "Address",
        "Offset",
        "特效",
        "剧本",
        "曹操",
        "Star6",
        "star175",
        "Itemicon",
        "Mgcicon",
        "ts.e5"
    };

    public CheatMakerCmfProbeResult Probe(string path, string? baselinePath = null, string evidenceRole = "6.6 CMF evidence sample")
        => Probe(path, baselinePath, evidenceRole, rootPath: null);

    public CheatMakerCmfProbeResult Probe(string path, string? baselinePath, string evidenceRole, string? rootPath)
    {
        var warnings = new List<string>();
        if (!File.Exists(path))
        {
            return new CheatMakerCmfProbeResult
            {
                Path = path,
                RelativePath = BuildRelativePath(rootPath, path),
                EvidenceCategory = UnknownOrRejected,
                EvidenceRole = evidenceRole,
                Warnings = new[] { "CMF file was not found." },
                Summary = "CMF probe failed: file was not found."
            };
        }

        var bytes = File.ReadAllBytes(path);
        var signature = ReadUtf16Signature(bytes);
        var isCmf = bytes.Length >= 12 &&
                    bytes[0] == 0xFF &&
                    bytes[1] == 0xFE &&
                    SupportedSignatures.Contains(signature);
        var crlfOffsets = FindUtf16CrlfOffsets(bytes).ToArray();
        var searchText = DecodeSearchText(bytes);
        var keywordHits = FindVisibleKeywordHits(searchText);
        var segments = AnalyzeSegments(bytes, crlfOffsets).ToArray();
        var looksProtected = isCmf && LooksProtectedOrObfuscated(bytes, crlfOffsets);
        var relativePath = BuildRelativePath(rootPath, path);
        var evidenceCategory = ClassifyEvidence(path, rootPath, isCmf);

        if (!isCmf)
        {
            warnings.Add("File does not start with UTF-16LE BOM + a supported CheatMaker CMF signature.");
        }

        if (looksProtected)
        {
            warnings.Add("CMF payload appears protected or encoded; treat it as a high-trust tool source, but require CheatMaker export/UI field metadata before converting it to writable rules.");
        }

        var result = new CheatMakerCmfProbeResult
        {
            Path = Path.GetFullPath(path),
            RelativePath = relativePath,
            Exists = true,
            Length = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            HasUtf16Bom = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE,
            Signature = signature,
            FormatSignature = signature,
            FormatVersion = ResolveFormatVersion(signature),
            IsCheatMakerCmf = isCmf,
            Utf16CrlfCount = crlfOffsets.Length,
            FirstUtf16CrlfOffsets = crlfOffsets.Take(16).ToArray(),
            LooksProtectedOrObfuscated = looksProtected,
            VisibleKeywordHits = keywordHits,
            Segments = segments,
            EvidenceCategory = evidenceCategory,
            EvidenceRole = evidenceRole,
            Warnings = warnings,
            Summary = BuildSummary(path, bytes.Length, signature, isCmf, crlfOffsets.Length, looksProtected, evidenceCategory)
        };

        return string.IsNullOrWhiteSpace(baselinePath)
            ? result
            : WithComparison(result, bytes, baselinePath);
    }

    public CheatMakerCmfCorpusReport ScanCorpus(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("CMF corpus root is required.");
        }

        var fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot))
        {
            return new CheatMakerCmfCorpusReport
            {
                RootPath = fullRoot
            };
        }

        var entries = Directory.EnumerateFiles(fullRoot, "*.cmf", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(fullRoot, path), StringComparer.OrdinalIgnoreCase)
            .Select(path => ToCatalogEntry(Probe(path, null, "CMF corpus evidence sample", fullRoot)))
            .ToList();

        return new CheatMakerCmfCorpusReport
        {
            RootPath = fullRoot,
            TotalFiles = entries.Count,
            CheatMakerCmfCount = entries.Count(entry => entry.IsCheatMakerCmf),
            SignatureCounts = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.FormatSignature))
                .GroupBy(entry => entry.FormatSignature, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            CategoryCounts = entries
                .GroupBy(entry => entry.EvidenceCategory, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            Entries = entries
        };
    }

    public CheatMakerCmfComparison Compare(string leftPath, string rightPath)
    {
        var left = Probe(leftPath);
        return WithComparison(left, File.Exists(leftPath) ? File.ReadAllBytes(leftPath) : Array.Empty<byte>(), rightPath).Comparison
            ?? new CheatMakerCmfComparison { BaselinePath = rightPath, Summary = "CMF comparison failed." };
    }

    public static string? FindDefaultOldToolsRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) return null;

        foreach (var dir in EnumerateAncestorCandidates(workspaceRoot))
        {
            var direct = Path.Combine(dir, "老版游戏制作工具");
            if (Directory.Exists(direct)) return direct;
        }

        try
        {
            return Directory.EnumerateDirectories(workspaceRoot, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => Path.GetFileName(path).Contains("老版游戏制作工具", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    public static string? FindDefaultStar66XSample(string workspaceRoot)
        => FindByNameOrLength(
            workspaceRoot,
            file => Path.GetFileName(file).Contains("Star6.6X", StringComparison.OrdinalIgnoreCase),
            1_145_916);

    public static string? FindDefaultStar66KBaseline(string workspaceRoot)
        => FindByNameOrLength(
            workspaceRoot,
            file =>
            {
                var name = Path.GetFileName(file);
                return name.Contains("Star6.6", StringComparison.OrdinalIgnoreCase) &&
                       name.Contains("K", StringComparison.OrdinalIgnoreCase);
            },
            763_592);

    private static string? FindByNameOrLength(string workspaceRoot, Func<string, bool> namePredicate, long expectedLength)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot)) return null;

        try
        {
            var files = Directory.EnumerateFiles(workspaceRoot, "*.cmf", SearchOption.AllDirectories)
                .Select(file => new FileInfo(file))
                .Where(info => info.Exists)
                .ToList();

            return files
                       .Where(info => info.Length == expectedLength)
                       .Where(info => namePredicate(info.FullName))
                       .Select(info => info.FullName)
                       .FirstOrDefault()
                   ?? files
                       .Where(info => info.Length == expectedLength)
                       .Select(info => info.FullName)
                       .FirstOrDefault()
                   ?? files
                       .Where(info => namePredicate(info.FullName))
                       .Select(info => info.FullName)
                       .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static CheatMakerCmfProbeResult WithComparison(CheatMakerCmfProbeResult result, byte[] bytes, string baselinePath)
    {
        if (!File.Exists(baselinePath))
        {
            return CloneWithComparison(
                result,
                new CheatMakerCmfComparison
                {
                    BaselinePath = baselinePath,
                    Summary = "Baseline CMF file was not found."
                },
                result.Warnings.Concat(new[] { "Baseline CMF file was not found." }).ToArray());
        }

        var baselineBytes = File.ReadAllBytes(baselinePath);
        var baselineSignature = ReadUtf16Signature(baselineBytes);
        var baselineCrlfCount = FindUtf16CrlfOffsets(baselineBytes).Count;
        var comparableLength = Math.Min(bytes.Length, baselineBytes.Length);
        long prefix = 0;
        while (prefix < comparableLength && bytes[prefix] == baselineBytes[prefix]) prefix++;

        long equal = 0;
        for (var i = 0; i < comparableLength; i++)
        {
            if (bytes[i] == baselineBytes[i]) equal++;
        }

        var ratio = comparableLength == 0 ? 0 : Math.Round((double)equal / comparableLength, 4);
        var comparison = new CheatMakerCmfComparison
        {
            BaselinePath = Path.GetFullPath(baselinePath),
            BaselineExists = true,
            BaselineLength = baselineBytes.Length,
            BaselineSha256 = Convert.ToHexString(SHA256.HashData(baselineBytes)),
            BaselineSignature = baselineSignature,
            BaselineUtf16CrlfCount = baselineCrlfCount,
            ExtraUtf16CrlfSegments = result.Utf16CrlfCount - baselineCrlfCount,
            LengthDelta = result.Length - baselineBytes.Length,
            SamePrefixBytes = prefix,
            EqualBytesInComparableLength = equal,
            EqualRatioInComparableLength = ratio,
            Summary = string.Format(
                CultureInfo.InvariantCulture,
                "Baseline length={0}, target length={1}, UTF-16 segment delta={2}, same prefix={3} bytes, comparable equality={4:P2}.",
                baselineBytes.Length,
                bytes.Length,
                result.Utf16CrlfCount - baselineCrlfCount,
                prefix,
                ratio)
        };

        return CloneWithComparison(result, comparison, result.Warnings);
    }

    private static CheatMakerCmfProbeResult CloneWithComparison(
        CheatMakerCmfProbeResult result,
        CheatMakerCmfComparison comparison,
        IReadOnlyList<string> warnings)
        => new()
        {
            Path = result.Path,
            RelativePath = result.RelativePath,
            Exists = result.Exists,
            Length = result.Length,
            Sha256 = result.Sha256,
            HasUtf16Bom = result.HasUtf16Bom,
            Signature = result.Signature,
            FormatSignature = result.FormatSignature,
            FormatVersion = result.FormatVersion,
            IsCheatMakerCmf = result.IsCheatMakerCmf,
            Utf16CrlfCount = result.Utf16CrlfCount,
            FirstUtf16CrlfOffsets = result.FirstUtf16CrlfOffsets,
            LooksProtectedOrObfuscated = result.LooksProtectedOrObfuscated,
            VisibleKeywordHits = result.VisibleKeywordHits,
            Segments = result.Segments,
            EvidenceCategory = result.EvidenceCategory,
            EvidenceOnly = result.EvidenceOnly,
            AuthoritativeToolSource = result.AuthoritativeToolSource,
            SafetyNote = result.SafetyNote,
            EvidenceRole = result.EvidenceRole,
            Summary = result.Summary + " " + comparison.Summary,
            Warnings = warnings,
            Comparison = comparison
        };

    private static CheatMakerCmfCatalogEntry ToCatalogEntry(CheatMakerCmfProbeResult result)
        => new()
        {
            RelativePath = result.RelativePath,
            Path = result.Path,
            FileName = Path.GetFileName(result.Path),
            Length = result.Length,
            Sha256 = result.Sha256,
            FormatSignature = result.FormatSignature,
            FormatVersion = result.FormatVersion,
            Utf16CrlfCount = result.Utf16CrlfCount,
            IsCheatMakerCmf = result.IsCheatMakerCmf,
            LooksProtectedOrObfuscated = result.LooksProtectedOrObfuscated,
            VisibleKeywordHits = result.VisibleKeywordHits,
            Segments = result.Segments,
            EvidenceCategory = result.EvidenceCategory,
            EvidenceOnly = result.EvidenceOnly,
            AuthoritativeToolSource = result.AuthoritativeToolSource,
            Summary = result.Summary,
            Warnings = result.Warnings
        };

    private static string BuildRelativePath(string? rootPath, string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rootPath)) return Path.GetFileName(path);
            return Path.GetRelativePath(Path.GetFullPath(rootPath), Path.GetFullPath(path));
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }

    private static string ClassifyEvidence(string path, string? rootPath, bool isCmf)
    {
        if (!isCmf) return UnknownOrRejected;

        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            var relative = BuildRelativePath(rootPath, path);
            if (relative.StartsWith("CheatMaker" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("CheatMaker/", StringComparison.OrdinalIgnoreCase))
            {
                return CheatMakerFormatSample;
            }
        }

        return CczRelevantRootSample;
    }

    private static IReadOnlyList<string> FindVisibleKeywordHits(string text)
        => VisibleKeywordNeedles
            .Where(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ResolveFormatVersion(string signature)
        => signature.Equals("cmf0a", StringComparison.OrdinalIgnoreCase) ? "0a" :
           signature.StartsWith("cmf", StringComparison.OrdinalIgnoreCase) && signature.Length > 3 ? signature[3..] :
           string.Empty;

    private static IEnumerable<string> EnumerateAncestorCandidates(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            yield return dir.FullName;
            dir = dir.Parent;
        }
    }

    private static string ReadUtf16Signature(byte[] bytes)
    {
        if (bytes.Length < 12) return string.Empty;
        try
        {
            return Encoding.Unicode.GetString(bytes, 2, 10).TrimEnd('\0');
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<long> FindUtf16CrlfOffsets(byte[] bytes)
    {
        var offsets = new List<long>();
        for (var i = 0; i + 3 < bytes.Length; i += 2)
        {
            if (bytes[i] == 0x0D &&
                bytes[i + 1] == 0x00 &&
                bytes[i + 2] == 0x0A &&
                bytes[i + 3] == 0x00)
            {
                offsets.Add(i);
            }
        }

        return offsets;
    }

    private static IEnumerable<CmfSegmentAnalysis> AnalyzeSegments(byte[] bytes, IReadOnlyList<long> crlfOffsets)
    {
        var starts = new List<int> { 0 };
        starts.AddRange(crlfOffsets.Select(offset => checked((int)offset + 4)).Where(offset => offset < bytes.Length));
        var ends = crlfOffsets.Select(offset => checked((int)offset)).ToList();
        ends.Add(bytes.Length);

        for (var i = 0; i < starts.Count && i < ends.Count; i++)
        {
            var start = starts[i];
            var end = Math.Max(start, ends[i]);
            var length = end - start;
            var segmentBytes = new byte[length];
            if (length > 0) Buffer.BlockCopy(bytes, start, segmentBytes, 0, length);
            var text = DecodeSegmentText(segmentBytes);
            var hits = FindVisibleKeywordHits(text);
            yield return new CmfSegmentAnalysis
            {
                Index = i,
                ByteOffset = start,
                ByteLength = length,
                CharLength = text.Length,
                PrintableAsciiRatio = ComputePrintableAsciiRatio(text),
                CjkRatio = ComputeCjkRatio(text),
                ByteEntropy = Math.Round(ComputeEntropy(segmentBytes), 4),
                KeywordHits = hits,
                SuspectedKind = ClassifySegment(i, text, segmentBytes, hits),
                Preview = BuildPreview(text)
            };
        }
    }

    private static string ClassifySegment(int index, string text, byte[] bytes, IReadOnlyList<string> hits)
    {
        if (index == 0) return "Header";
        if (hits.Any(hit => hit.Contains("Ekd5", StringComparison.OrdinalIgnoreCase) ||
                            hit.Contains("Star", StringComparison.OrdinalIgnoreCase))) return "EngineModifierCandidate";
        if (hits.Any(hit => hit.Contains("鐗规晥", StringComparison.OrdinalIgnoreCase) ||
                            hit.Contains("Imsg", StringComparison.OrdinalIgnoreCase))) return "EffectModifierCandidate";
        if (HexAddressRegex.IsMatch(text)) return "AddressListCandidate";
        if (ComputeEntropy(bytes) > 6.5) return "ProtectedOrEncodedPayload";
        return "EncodedProjectSegment";
    }

    private static string DecodeSegmentText(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;
        try
        {
            return Encoding.Unicode.GetString(bytes);
        }
        catch
        {
            return Encoding.ASCII.GetString(bytes);
        }
    }

    private static string BuildPreview(string text)
        => new string(text
            .Take(160)
            .Select(ch => ch is >= ' ' and <= '~' || ch is >= '\u4e00' and <= '\u9fff' ? ch : '.')
            .ToArray())
            .Trim();

    private static double ComputePrintableAsciiRatio(string text)
    {
        if (text.Length == 0) return 0;
        return Math.Round((double)text.Count(ch => ch is >= ' ' and <= '~') / text.Length, 4);
    }

    private static double ComputeCjkRatio(string text)
    {
        if (text.Length == 0) return 0;
        return Math.Round((double)text.Count(ch => ch is >= '\u4e00' and <= '\u9fff') / text.Length, 4);
    }

    private static double ComputeEntropy(byte[] bytes)
    {
        if (bytes.Length == 0) return 0;
        Span<int> counts = stackalloc int[256];
        foreach (var b in bytes) counts[b]++;
        var entropy = 0d;
        foreach (var count in counts)
        {
            if (count == 0) continue;
            var p = (double)count / bytes.Length;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    private static bool LooksProtectedOrObfuscated(byte[] bytes, IReadOnlyList<long> crlfOffsets)
    {
        if (bytes.Length < 64) return false;
        if (crlfOffsets.Count <= 1) return true;

        var text = DecodeSearchText(bytes);
        var clearKeywords = new[]
        {
            "Address",
            "Offset",
            "Ekd5",
            "Data.e5",
            "Item.e5",
            "Table",
            "Cheat"
        };
        var clearKeywordHits = clearKeywords.Count(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (clearKeywordHits == 0 && crlfOffsets.Count < 32) return true;

        var sampledPairs = 0;
        var printableUtf16Pairs = 0;
        var max = Math.Min(bytes.Length - 1, 8192);
        for (var i = 12; i < max; i += 2)
        {
            sampledPairs++;
            if (bytes[i + 1] == 0x00 && bytes[i] >= 0x20 && bytes[i] <= 0x7E)
            {
                printableUtf16Pairs++;
            }
        }

        if (sampledPairs == 0) return false;
        var printableRatio = (double)printableUtf16Pairs / sampledPairs;
        return printableRatio < 0.60 && clearKeywordHits <= 1;
    }

    private static string DecodeSearchText(byte[] bytes)
    {
        var take = Math.Min(bytes.Length, 256 * 1024);
        var unicode = Encoding.Unicode.GetString(bytes, 0, take);
        var ascii = Encoding.ASCII.GetString(bytes, 0, take);
        return unicode + "\n" + ascii;
    }

    private static string BuildSummary(string path, long length, string signature, bool isCmf, int crlfCount, bool protectedPayload, string evidenceCategory)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0}: signature={1}, length={2}, utf16CrlfCount={3}, type={4}, category={5}, payload={6}.",
            Path.GetFileName(path),
            string.IsNullOrWhiteSpace(signature) ? "<none>" : signature,
            length,
            crlfCount,
            isCmf ? "CheatMaker CMF" : "unknown",
            evidenceCategory,
            protectedPayload ? "protected/obfuscated evidence" : "plain or lightly encoded");
}
