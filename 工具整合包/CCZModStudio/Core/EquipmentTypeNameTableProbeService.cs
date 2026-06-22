using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed record EquipmentTypeNameTableCandidate(
    string FileName,
    long Offset,
    int Length,
    string SourceLabel);

public sealed record EquipmentTypeNameTableProbeResult(
    string FileName,
    string FilePath,
    long Offset,
    int Length,
    IReadOnlyList<string> Names,
    int Score,
    int KeywordHits,
    int SampleHits,
    string SourceLabel,
    IReadOnlyList<string> Diagnostics)
{
    public bool IsUsable => Names.Count >= EquipmentTypeNameTableProbeService.MinimumUsableNameCount &&
                            KeywordHits >= EquipmentTypeNameTableProbeService.MinimumKeywordHits;

    public string LocationText => $"{FileName}@0x{Offset:X}";

    public string SummaryText => $"{LocationText} len=0x{Length:X}，名称 {Names.Count}，关键词 {KeywordHits}，样例命中 {SampleHits}";
}

public sealed class EquipmentTypeNameTableProbeService
{
    public const int MinimumUsableNameCount = 8;
    public const int MinimumKeywordHits = 4;

    public static readonly IReadOnlyList<EquipmentTypeNameTableCandidate> DefaultCandidates =
    [
        new("Ekd5.exe", 0x8AC70, 0x74, "旧 6.4/6.5 搬运资料：装备类型名称"),
        new("Ekd5.exe", 0x48C270, 0x74, "6.6/补丁迁移资料：装备类型名称")
    ];

    private static readonly string[] EquipmentKeywordHints =
    [
        "剑",
        "枪",
        "弓",
        "刀",
        "车",
        "锤",
        "斧",
        "扇",
        "甲",
        "衣",
        "袍",
        "辅助",
        "道具"
    ];

    public EquipmentTypeNameTableProbeResult? ProbeBest(
        CczProject project,
        IReadOnlyDictionary<int, IReadOnlyList<string>> sampleNames,
        IList<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sampleNames);

        var results = ProbeAll(project, sampleNames).ToArray();
        foreach (var result in results)
        {
            if (diagnostics == null) continue;
            foreach (var line in result.Diagnostics)
            {
                diagnostics.Add(line);
            }
        }

        var best = results
            .Where(result => result.IsUsable)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Offset)
            .FirstOrDefault();

        if (best == null)
        {
            diagnostics?.Add("装备类型名称表探测：没有候选通过校验，继续使用 Data.e5/Star.e5 物品样例和旧内置兜底。");
            return null;
        }

        diagnostics?.Add($"装备类型名称表采用：{best.SummaryText}；来源={best.SourceLabel}。");
        return best;
    }

    public IReadOnlyList<EquipmentTypeNameTableProbeResult> ProbeAll(
        CczProject project,
        IReadOnlyDictionary<int, IReadOnlyList<string>> sampleNames)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sampleNames);

        var results = new List<EquipmentTypeNameTableProbeResult>();
        foreach (var candidate in DefaultCandidates)
        {
            results.Add(ProbeCandidate(project, candidate, sampleNames));
        }

        AddDataFileDiagnostic(project, results);
        return results;
    }

    private EquipmentTypeNameTableProbeResult ProbeCandidate(
        CczProject project,
        EquipmentTypeNameTableCandidate candidate,
        IReadOnlyDictionary<int, IReadOnlyList<string>> sampleNames)
    {
        var path = project.ResolveGameFile(candidate.FileName);
        var diagnostics = new List<string>();
        if (!File.Exists(path))
        {
            diagnostics.Add($"装备类型名称表候选缺失：{candidate.FileName}@0x{candidate.Offset:X}。");
            return Empty(candidate, path, diagnostics);
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length <= candidate.Offset)
            {
                diagnostics.Add($"装备类型名称表候选越界：{candidate.FileName}@0x{candidate.Offset:X}，文件长度 0x{info.Length:X}。");
                return Empty(candidate, path, diagnostics);
            }

            var length = (int)Math.Min(candidate.Length, info.Length - candidate.Offset);
            var buffer = new byte[length];
            using (var stream = File.OpenRead(path))
            {
                stream.Position = candidate.Offset;
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read < buffer.Length) Array.Resize(ref buffer, read);
            }

            var names = DecodeNullSeparatedGbkStrings(buffer)
                .Where(IsPlausibleTypeName)
                .Take(64)
                .ToArray();
            var keywordHits = CountKeywordHits(names);
            var sampleHits = CountSampleHits(names, sampleNames);
            var score = names.Length * 4 + keywordHits * 12 + sampleHits * 3 - Math.Abs(names.Length - 15);
            var status = names.Length >= MinimumUsableNameCount && keywordHits >= MinimumKeywordHits
                ? "通过"
                : "未通过";
            diagnostics.Add($"装备类型名称表候选{status}：{candidate.FileName}@0x{candidate.Offset:X} len=0x{length:X}，名称 {names.Length}，关键词 {keywordHits}，样例命中 {sampleHits}。");

            return new EquipmentTypeNameTableProbeResult(
                candidate.FileName,
                path,
                candidate.Offset,
                length,
                names,
                score,
                keywordHits,
                sampleHits,
                candidate.SourceLabel,
                diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"装备类型名称表候选读取失败：{candidate.FileName}@0x{candidate.Offset:X}，{ex.Message}");
            return Empty(candidate, path, diagnostics);
        }
    }

    private static EquipmentTypeNameTableProbeResult Empty(
        EquipmentTypeNameTableCandidate candidate,
        string path,
        IReadOnlyList<string> diagnostics)
        => new(
            candidate.FileName,
            path,
            candidate.Offset,
            candidate.Length,
            Array.Empty<string>(),
            0,
            0,
            0,
            candidate.SourceLabel,
            diagnostics);

    private static IReadOnlyList<string> DecodeNullSeparatedGbkStrings(byte[] bytes)
    {
        EncodingService.EnsureCodePages();
        var names = new List<string>();
        var buffer = new List<byte>();
        foreach (var value in bytes)
        {
            if (value is 0x00 or 0xFF or 0x90)
            {
                Flush();
                continue;
            }

            buffer.Add(value);
        }

        Flush();
        return names;

        void Flush()
        {
            if (buffer.Count == 0) return;
            var text = EncodingService.Gbk.GetString(buffer.ToArray()).Trim('\0', ' ', '\u3000');
            buffer.Clear();
            if (!string.IsNullOrWhiteSpace(text)) names.Add(text);
        }
    }

    private static bool IsPlausibleTypeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.Length > 16) return false;
        if (text.Contains('\uFFFD', StringComparison.Ordinal)) return false;
        if (text.Any(char.IsControl)) return false;
        return text.Any(ch => char.IsLetterOrDigit(ch) || IsCjk(ch));
    }

    private static int CountKeywordHits(IReadOnlyList<string> names)
        => names.Take(20)
            .Count(name => EquipmentKeywordHints.Any(word => name.Contains(word, StringComparison.CurrentCulture)));

    private static int CountSampleHits(
        IReadOnlyList<string> baseNames,
        IReadOnlyDictionary<int, IReadOnlyList<string>> sampleNames)
    {
        var hits = 0;
        var usableGroups = Math.Min((ProjectEquipmentTypeProfileService.JobPermissionSlotCount + 1) / 2, baseNames.Count);
        for (var group = 0; group < usableGroups; group++)
        {
            var baseName = baseNames[group];
            var evenTypeId = group * 2;
            var oddTypeId = evenTypeId + 1;
            if (SamplesContainName(sampleNames, evenTypeId, baseName)) hits++;
            if (oddTypeId < ProjectEquipmentTypeProfileService.JobPermissionSlotCount &&
                SamplesContainName(sampleNames, oddTypeId, baseName))
            {
                hits++;
            }
        }

        return hits;
    }

    private static bool SamplesContainName(
        IReadOnlyDictionary<int, IReadOnlyList<string>> sampleNames,
        int typeId,
        string baseName)
    {
        if (!sampleNames.TryGetValue(typeId, out var samples)) return false;
        return samples.Any(sample => sample.Contains(baseName, StringComparison.CurrentCulture) ||
                                     baseName.Contains(sample, StringComparison.CurrentCulture));
    }

    private static bool IsCjk(char ch)
        => ch >= '\u3400' && ch <= '\u9FFF';

    private static void AddDataFileDiagnostic(CczProject project, List<EquipmentTypeNameTableProbeResult> results)
    {
        var dataPath = project.ResolveGameFile("Data.e5");
        if (!File.Exists(dataPath)) return;
        var info = new FileInfo(dataPath);
        var firstExeCandidate = DefaultCandidates.FirstOrDefault(candidate => candidate.FileName.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase));
        if (firstExeCandidate == null) return;
        if (info.Length <= firstExeCandidate.Offset)
        {
            results.Add(new EquipmentTypeNameTableProbeResult(
                "Data.e5",
                dataPath,
                firstExeCandidate.Offset,
                firstExeCandidate.Length,
                Array.Empty<string>(),
                0,
                0,
                0,
                "CheatMaker Data 视图排除诊断",
                new[]
                {
                    $"Data.e5 长度 0x{info.Length:X} 小于 0x{firstExeCandidate.Offset:X}，截图/工具中的 Data 地址不能当作离线 Data.e5 文件偏移；按内存/地址列表来源处理。"
                }));
        }
    }
}
