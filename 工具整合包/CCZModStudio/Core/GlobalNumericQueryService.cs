using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class GlobalNumericQueryService
{
    private const int MaxCandidatesPerField = 80;
    private const int MaxSingletonHitsPerValue = 48;
    private const int ContextBytes = 8;
    private static readonly string[] CoreQueryFiles = ["Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5"];
    private static readonly Regex IntegerRegex = new(@"-?\d+", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public GlobalNumericQueryReport Query(
        CczProject project,
        IReadOnlyList<GlobalNumericSettingDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(definitions);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var evidenceRoot = Path.Combine(
            project.WorkspaceRoot,
            "CCZModStudio_Reports",
            "DebugEvidence",
            "global-numeric-query",
            stamp);
        Directory.CreateDirectory(evidenceRoot);

        var warnings = new List<string>();
        var files = LoadFiles(project, warnings);
        var runtimeResolvers = files.ToDictionary(
            file => file.RelativePath,
            file => BuildRuntimeAddressResolver(file.FullPath, file.RelativePath),
            StringComparer.OrdinalIgnoreCase);

        var fields = definitions
            .Select(definition => QueryField(definition, files, runtimeResolvers))
            .ToArray();

        var report = new GlobalNumericQueryReport
        {
            ProjectRoot = project.WorkspaceRoot,
            SourceGameRoot = project.GameRoot,
            EvidenceRoot = evidenceRoot,
            Fields = fields,
            Warnings = warnings
        };

        var reportPath = Path.Combine(evidenceRoot, "global-numeric-query-report.json");
        report.ReportPath = reportPath;
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));
        return report;
    }

    private static IReadOnlyList<QueryFile> LoadFiles(CczProject project, List<string> warnings)
    {
        var files = new List<QueryFile>();
        foreach (var relativePath in CoreQueryFiles)
        {
            var fullPath = project.ResolveGameFile(relativePath);
            if (!File.Exists(fullPath))
            {
                warnings.Add("Missing core file for global numeric query: " + fullPath);
                continue;
            }

            files.Add(new QueryFile(relativePath, fullPath, File.ReadAllBytes(fullPath)));
        }

        return files;
    }

    private static GlobalNumericQueryField QueryField(
        GlobalNumericSettingDefinition definition,
        IReadOnlyList<QueryFile> files,
        IReadOnlyDictionary<string, Func<long, long>> runtimeResolvers)
    {
        var parsedValues = ParseDefaultValues(definition.DefaultValueText).ToArray();
        var verifiedTargets = definition.WriteTargets
            .GroupBy(target => (target.TargetFileName, target.FileOffset), new TargetKeyComparer())
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                new TargetKeyComparer());
        var patterns = BuildPatterns(definition, parsedValues).ToArray();
        var candidates = new List<GlobalNumericQueryCandidate>();
        var patternReports = new List<GlobalNumericQueryPattern>();

        foreach (var pattern in patterns)
        {
            var hitCount = 0;
            if (pattern.Bytes.Length == 0)
            {
                patternReports.Add(ToPatternReport(pattern, hitCount, searchSkipped: true));
                continue;
            }

            foreach (var file in files)
            {
                foreach (var offset in FindAll(file.Bytes, pattern.Bytes))
                {
                    hitCount++;
                    if (candidates.Count >= MaxCandidatesPerField)
                    {
                        continue;
                    }

                    runtimeResolvers.TryGetValue(file.RelativePath, out var resolver);
                    var targetKey = (file.RelativePath, (long)offset);
                    verifiedTargets.TryGetValue(targetKey, out var verifiedTarget);
                    candidates.Add(new GlobalNumericQueryCandidate
                    {
                        RelativePath = file.RelativePath,
                        FileOffset = offset,
                        RuntimeAddress = resolver?.Invoke(offset) ?? 0,
                        ByteLength = pattern.Bytes.Length,
                        BytesHex = Convert.ToHexString(pattern.Bytes),
                        PatternKind = pattern.PatternKind,
                        ContextBeforeHex = ToHexSlice(file.Bytes, Math.Max(0, offset - ContextBytes), offset - Math.Max(0, offset - ContextBytes)),
                        ContextAfterHex = ToHexSlice(
                            file.Bytes,
                            offset + pattern.Bytes.Length,
                            Math.Min(ContextBytes, file.Bytes.Length - offset - pattern.Bytes.Length)),
                        IsVerifiedWriteTarget = verifiedTarget != null,
                        VerifiedPurpose = verifiedTarget?.Purpose ?? string.Empty,
                        Note = verifiedTarget != null
                            ? "This offset is already part of the verified write target set."
                            : "Static value match only; requires official single-field diff before promotion."
                    });
                }
            }

            patternReports.Add(ToPatternReport(pattern, hitCount, searchSkipped: false));
        }

        AddVerifiedTargetCandidates(definition, files, runtimeResolvers, verifiedTargets, candidates);

        var candidateArray = candidates
            .OrderByDescending(candidate => candidate.IsVerifiedWriteTarget)
            .ThenBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.FileOffset)
            .ToArray();

        return new GlobalNumericQueryField
        {
            Key = definition.Key,
            DisplayName = definition.DisplayName,
            DefaultValueText = definition.DefaultValueText,
            EvidenceStatus = definition.EvidenceStatus,
            EvidenceSource = definition.EvidenceSource,
            CanEdit = definition.CanEdit,
            TargetFileName = definition.TargetFileName,
            FileOffset = definition.FileOffset,
            RuntimeAddress = definition.RuntimeAddress,
            ByteLength = definition.ByteLength,
            WriteTargets = definition.WriteTargets,
            ValueKind = definition.ValueKind.ToString(),
            OracleCoverage = definition.OracleCoverage,
            ParsedDefaultValues = parsedValues,
            Patterns = patternReports,
            Candidates = candidateArray,
            TotalCandidateCount = patternReports.Sum(pattern => pattern.TotalHitCount),
            QueryConclusion = BuildConclusion(definition, candidateArray, patternReports)
        };
    }

    private static void AddVerifiedTargetCandidates(
        GlobalNumericSettingDefinition definition,
        IReadOnlyList<QueryFile> files,
        IReadOnlyDictionary<string, Func<long, long>> runtimeResolvers,
        IReadOnlyDictionary<(string TargetFileName, long FileOffset), GlobalNumericWriteTarget> verifiedTargets,
        List<GlobalNumericQueryCandidate> candidates)
    {
        if (!definition.CanEdit || verifiedTargets.Count == 0)
        {
            return;
        }

        foreach (var target in verifiedTargets.Values)
        {
            if (candidates.Any(candidate =>
                    candidate.IsVerifiedWriteTarget &&
                    candidate.FileOffset == target.FileOffset &&
                    candidate.RelativePath.Equals(target.TargetFileName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var file = files.FirstOrDefault(item => item.RelativePath.Equals(target.TargetFileName, StringComparison.OrdinalIgnoreCase));
            if (file == null ||
                target.FileOffset < 0 ||
                target.FileOffset + target.ByteLength > file.Bytes.Length)
            {
                continue;
            }

            var offset = checked((int)target.FileOffset);
            runtimeResolvers.TryGetValue(file.RelativePath, out var resolver);
            candidates.Add(new GlobalNumericQueryCandidate
            {
                RelativePath = file.RelativePath,
                FileOffset = target.FileOffset,
                RuntimeAddress = resolver?.Invoke(target.FileOffset) ?? target.RuntimeAddress,
                ByteLength = target.ByteLength,
                BytesHex = ToHexSlice(file.Bytes, offset, target.ByteLength),
                PatternKind = "VerifiedWriteTarget",
                ContextBeforeHex = ToHexSlice(file.Bytes, Math.Max(0, offset - ContextBytes), offset - Math.Max(0, offset - ContextBytes)),
                ContextAfterHex = ToHexSlice(
                    file.Bytes,
                    offset + target.ByteLength,
                    Math.Min(ContextBytes, file.Bytes.Length - offset - target.ByteLength)),
                IsVerifiedWriteTarget = true,
                VerifiedPurpose = target.Purpose,
                Note = "Verified write target added explicitly from GlobalSettingsService metadata."
            });
        }
    }

    private static IEnumerable<QueryPattern> BuildPatterns(GlobalNumericSettingDefinition definition, IReadOnlyList<int> values)
    {
        if (values.Count == 0)
        {
            yield return new QueryPattern("NoNumericDefault", definition.ValueKind.ToString(), values, []);
            yield break;
        }

        var fullSequence = EncodeSequence(definition.ValueKind, values);
        if (fullSequence.Length > 0)
        {
            yield return new QueryPattern("FullDefaultSequence", definition.ValueKind.ToString(), values, fullSequence);
        }

        if (values.Count > 1)
        {
            foreach (var adjacent in values.Zip(values.Skip(1), (left, right) => new[] { left, right }))
            {
                var bytes = EncodeSequence(definition.ValueKind, adjacent);
                if (bytes.Length > 0)
                {
                    yield return new QueryPattern("AdjacentPair", definition.ValueKind.ToString(), adjacent, bytes);
                }
            }
        }

        var singletonLimit = values.Distinct().Count() > 4 ? 0 : values.Distinct().Count();
        if (singletonLimit > 0)
        {
            foreach (var value in values.Distinct().Take(singletonLimit))
            {
                var bytes = EncodeSequence(definition.ValueKind, [value]);
                if (bytes.Length > 0)
                {
                    yield return new QueryPattern("SingleValue", definition.ValueKind.ToString(), [value], bytes);
                }
            }
        }
    }

    private static GlobalNumericQueryPattern ToPatternReport(QueryPattern pattern, int hitCount, bool searchSkipped)
    {
        var singletonNoise = pattern.PatternKind == "SingleValue" && hitCount > MaxSingletonHitsPerValue;
        return new GlobalNumericQueryPattern
        {
            PatternKind = pattern.PatternKind,
            ValueKind = pattern.ValueKind,
            Values = pattern.Values,
            BytesHex = Convert.ToHexString(pattern.Bytes),
            ByteLength = pattern.Bytes.Length,
            TotalHitCount = hitCount,
            SearchSkipped = searchSkipped,
            Note = searchSkipped
                ? "No byte pattern was generated."
                : singletonNoise
                    ? "Single-value pattern is noisy; use only as a breakpoint/diff hint."
                    : "Candidate count is informational only; promotion still requires official diff."
        };
    }

    private static string BuildConclusion(
        GlobalNumericSettingDefinition definition,
        IReadOnlyList<GlobalNumericQueryCandidate> candidates,
        IReadOnlyList<GlobalNumericQueryPattern> patterns)
    {
        if (definition.CanEdit)
        {
            return "已验证可写；静态查询用于复核官方 diff 目标，不扩大写入范围。";
        }

        var fullHits = patterns
            .Where(pattern => pattern.PatternKind == "FullDefaultSequence")
            .Sum(pattern => pattern.TotalHitCount);
        if (fullHits > 0)
        {
            return "完整默认序列有静态命中，但仍缺官方单字段 diff，保持只读。";
        }

        if (candidates.Count > 0)
        {
            return "仅存在短序列或单值命中，误报风险高，保持只读。";
        }

        return "未发现可解释静态候选，保持只读；必须继续使用官方工具单字段 diff 或运行时断点定位。";
    }

    private static IReadOnlyList<int> ParseDefaultValues(string text)
        => IntegerRegex.Matches(text ?? string.Empty)
            .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();

    private static byte[] EncodeSequence(GlobalNumericValueKind kind, IReadOnlyList<int> values)
    {
        var bytes = new List<byte>();
        foreach (var value in values)
        {
            switch (kind)
            {
                case GlobalNumericValueKind.BooleanRadio:
                case GlobalNumericValueKind.Byte:
                    if (value is < byte.MinValue or > byte.MaxValue) return [];
                    bytes.Add((byte)value);
                    break;
                case GlobalNumericValueKind.UInt16LE:
                    if (value is < ushort.MinValue or > ushort.MaxValue) return [];
                    bytes.AddRange(BitConverter.GetBytes((ushort)value));
                    break;
                case GlobalNumericValueKind.UInt32LE:
                    if (value < 0) return [];
                    bytes.AddRange(BitConverter.GetBytes((uint)value));
                    break;
                default:
                    return [];
            }
        }

        return bytes.ToArray();
    }

    private static IEnumerable<int> FindAll(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            yield break;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j])
                {
                    continue;
                }

                matched = false;
                break;
            }

            if (matched)
            {
                yield return i;
            }
        }
    }

    private static string ToHexSlice(byte[] bytes, int start, int length)
    {
        if (length <= 0 || start < 0 || start >= bytes.Length)
        {
            return string.Empty;
        }

        var actualLength = Math.Min(length, bytes.Length - start);
        return Convert.ToHexString(bytes.AsSpan(start, actualLength));
    }

    private static Func<long, long> BuildRuntimeAddressResolver(string filePath, string relativePath)
    {
        if (!relativePath.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
        {
            return _ => 0;
        }

        try
        {
            var sections = ReadPeSections(filePath);
            return fileOffset =>
            {
                foreach (var section in sections)
                {
                    if (fileOffset >= section.RawPointer && fileOffset < section.RawPointer + section.RawSize)
                    {
                        return section.ImageBase + section.VirtualAddress + (fileOffset - section.RawPointer);
                    }
                }

                return 0;
            };
        }
        catch
        {
            return _ => 0;
        }
    }

    private static IReadOnlyList<QueryPeSection> ReadPeSections(string exePath)
    {
        using var stream = File.OpenRead(exePath);
        using var reader = new BinaryReader(stream);
        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        stream.Position = peOffset;
        var signature = reader.ReadUInt32();
        if (signature != 0x00004550) return Array.Empty<QueryPeSection>();

        _ = reader.ReadUInt16();
        var sectionCount = reader.ReadUInt16();
        stream.Position += 12;
        var optionalHeaderSize = reader.ReadUInt16();
        stream.Position += 2;

        var optionalHeaderStart = stream.Position;
        var magic = reader.ReadUInt16();
        long imageBase;
        if (magic == 0x10B)
        {
            stream.Position = optionalHeaderStart + 28;
            imageBase = reader.ReadUInt32();
        }
        else if (magic == 0x20B)
        {
            stream.Position = optionalHeaderStart + 24;
            imageBase = checked((long)reader.ReadUInt64());
        }
        else
        {
            return Array.Empty<QueryPeSection>();
        }

        stream.Position = optionalHeaderStart + optionalHeaderSize;
        var sections = new List<QueryPeSection>();
        for (var i = 0; i < sectionCount; i++)
        {
            stream.Position += 8;
            var virtualSize = reader.ReadUInt32();
            var virtualAddress = reader.ReadUInt32();
            var rawSize = reader.ReadUInt32();
            var rawPointer = reader.ReadUInt32();
            stream.Position += 16;
            sections.Add(new QueryPeSection(imageBase, virtualAddress, virtualSize, rawPointer, rawSize));
        }

        return sections;
    }

    private sealed record QueryFile(string RelativePath, string FullPath, byte[] Bytes);

    private sealed record QueryPattern(string PatternKind, string ValueKind, IReadOnlyList<int> Values, byte[] Bytes);

    private sealed record QueryPeSection(long ImageBase, uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize);

    private sealed class TargetKeyComparer : IEqualityComparer<(string TargetFileName, long FileOffset)>
    {
        public bool Equals((string TargetFileName, long FileOffset) x, (string TargetFileName, long FileOffset) y)
            => x.FileOffset == y.FileOffset &&
               string.Equals(x.TargetFileName, y.TargetFileName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string TargetFileName, long FileOffset) obj)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TargetFileName), obj.FileOffset);
    }
}
