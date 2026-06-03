using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// EEX 跨文件只读对比：把同编号 R/S、同分类邻近文件的区段角色、长度和字节分布放到一起。
/// 该服务不解压、不写入，只为后续识别动作/帧表/图像载荷规律提供证据。
/// </summary>
public sealed class EexCrossFileComparisonService
{
    private readonly EexEntryProbeReader _probeReader = new();

    public EexCrossFileComparisonResult Compare(EexArchiveInfo target, IReadOnlyList<EexArchiveInfo> allArchives, int maxPeers = 8)
    {
        if (string.IsNullOrWhiteSpace(target.Path) || !File.Exists(target.Path))
        {
            throw new FileNotFoundException("选中的 EEX 文件不存在。", target.Path);
        }

        var peers = SelectPeers(target, allArchives, Math.Max(2, maxPeers));
        var targetProbe = _probeReader.Probe(target.Path, target.Category);
        var targetStats = BuildRoleStats(target, targetProbe);
        var rows = new List<EexCrossFileComparisonRow>();

        foreach (var peer in peers)
        {
            IReadOnlyList<EexEntryProbeRow> probeRows;
            try
            {
                probeRows = Path.GetFullPath(peer.Path).Equals(Path.GetFullPath(target.Path), StringComparison.OrdinalIgnoreCase)
                    ? targetProbe
                    : _probeReader.Probe(peer.Path, peer.Category);
            }
            catch (Exception ex)
            {
                rows.Add(new EexCrossFileComparisonRow
                {
                    PeerKind = BuildPeerKind(target, peer),
                    Category = peer.Category,
                    FileName = peer.FileName,
                    Id = peer.Id,
                    FileLength = peer.Length,
                    MagicValid = peer.MagicValid,
                    RoleHint = "探针失败",
                    DifferenceHint = ex.Message,
                    Annotation = "该文件无法生成区段探针；请先确认 EEX 魔数、文件大小和读取权限。跨文件对比不会写入任何内容。",
                    Path = peer.Path
                });
                continue;
            }

            var roleStats = BuildRoleStats(peer, probeRows).Values.ToList();
            if (roleStats.Count == 0)
            {
                rows.Add(new EexCrossFileComparisonRow
                {
                    PeerKind = BuildPeerKind(target, peer),
                    Category = peer.Category,
                    FileName = peer.FileName,
                    Id = peer.Id,
                    FileLength = peer.Length,
                    MagicValid = peer.MagicValid,
                    RoleHint = "无区段候选",
                    DifferenceHint = "未切分出可对比区段。",
                    Annotation = "没有可对比区段；可能是文件过短、头字段异常或当前探针规则尚不适用。",
                    Path = peer.Path
                });
                continue;
            }

            foreach (var stat in roleStats)
            {
                targetStats.TryGetValue(stat.RoleHint, out var baseline);
                rows.Add(ToRow(target, peer, stat, baseline));
            }
        }

        var targetRoles = targetStats.Count == 0
            ? "无"
            : string.Join("，", targetStats.Values
                .OrderByDescending(x => x.TotalLength)
                .ThenBy(x => x.RoleHint, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => $"{x.RoleHint}:{x.SectionCount}段/{x.TotalLength:N0}B"));

        var summary =
            $"EEX 跨文件对比：基准 {target.Category}/{target.FileName}（ID={ValueOrDash(target.Id)}，{target.Length:N0}B）\r\n" +
            $"参与文件：{peers.Count}    对比行：{rows.Count}    基准角色：{targetRoles}\r\n" +
            "说明：对比同编号 R/S 与同分类邻近 EEX 的区段角色、长度、00占比、小整数16位词占比和文本线索；当前只读，不解压、不重封包、不写入。";

        return new EexCrossFileComparisonResult
        {
            TargetFileName = target.FileName,
            TargetCategory = target.Category,
            TargetId = target.Id,
            Summary = summary,
            Rows = rows
        };
    }

    private EexCrossFileComparisonRow ToRow(EexArchiveInfo target, EexArchiveInfo peer, RoleStats stat, RoleStats? baseline)
    {
        var isTarget = Path.GetFullPath(peer.Path).Equals(Path.GetFullPath(target.Path), StringComparison.OrdinalIgnoreCase);
        var difference = isTarget
            ? "基准文件：作为同角色区段对比参照。"
            : BuildDifferenceHint(stat, baseline);

        return new EexCrossFileComparisonRow
        {
            PeerKind = BuildPeerKind(target, peer),
            Category = peer.Category,
            FileName = peer.FileName,
            Id = peer.Id,
            FileLength = peer.Length,
            MagicValid = peer.MagicValid,
            RoleHint = stat.RoleHint,
            SectionCount = stat.SectionCount,
            TotalLength = stat.TotalLength,
            MinLength = stat.MinLength,
            MaxLength = stat.MaxLength,
            AverageLength = stat.AverageLength,
            AverageZeroPercent = stat.AverageZeroPercent,
            AverageSmallWordPercent = stat.AverageSmallWordPercent,
            TextHintCount = stat.TextHintCount,
            FirstOffsets = stat.FirstOffsets,
            DifferenceHint = difference,
            Annotation = BuildAnnotation(isTarget, stat, baseline),
            Path = peer.Path
        };
    }

    private static string BuildDifferenceHint(RoleStats stat, RoleStats? baseline)
    {
        if (baseline == null)
        {
            return "基准文件没有同角色区段；该角色可能是此文件特有、误分类或头字段切分差异。";
        }

        var lengthDelta = stat.TotalLength - baseline.TotalLength;
        var lengthPercent = baseline.TotalLength == 0 ? 0 : lengthDelta * 100.0 / baseline.TotalLength;
        var zeroDelta = stat.AverageZeroPercent - baseline.AverageZeroPercent;
        var smallDelta = stat.AverageSmallWordPercent - baseline.AverageSmallWordPercent;
        var sectionDelta = stat.SectionCount - baseline.SectionCount;

        return
            $"段数差 {sectionDelta:+#;-#;0}；总长度差 {lengthDelta:+#,0;-#,0;0}B（{lengthPercent:+0.0;-0.0;0.0}%）；" +
            $"00占比差 {zeroDelta:+0.0;-0.0;0.0}%；小整数占比差 {smallDelta:+0.0;-0.0;0.0}%。";
    }

    private static string BuildAnnotation(bool isTarget, RoleStats stat, RoleStats? baseline)
    {
        if (isTarget)
        {
            return "基准角色统计。建议先观察长度最大或文本线索最多的区段，再与同编号 S/R 或邻近编号对比。";
        }

        if (baseline == null)
        {
            return "该角色未在基准文件中出现，适合重点检查是否为特殊动作、额外说明、空壳资源或探针误分组。";
        }

        var warnings = new List<string>();
        if (Math.Abs(stat.SectionCount - baseline.SectionCount) >= 2) warnings.Add("同角色段数差异明显");
        if (baseline.TotalLength > 0 && Math.Abs(stat.TotalLength - baseline.TotalLength) > Math.Max(1024, baseline.TotalLength / 4)) warnings.Add("同角色总长度差异较大");
        if (Math.Abs(stat.AverageZeroPercent - baseline.AverageZeroPercent) >= 20) warnings.Add("00占比分布差异较大");
        if (Math.Abs(stat.AverageSmallWordPercent - baseline.AverageSmallWordPercent) >= 20) warnings.Add("小整数比例差异较大");
        if (stat.TextHintCount != baseline.TextHintCount) warnings.Add("文本线索数量不同");

        var prefix = warnings.Count == 0 ? "与基准同角色区段接近" : string.Join("；", warnings);
        return prefix + "。该结论只用于格式研究和资源定位，不代表已确认帧表/图像编码。";
    }

    private static IReadOnlyDictionary<string, RoleStats> BuildRoleStats(EexArchiveInfo archive, IReadOnlyList<EexEntryProbeRow> rows)
    {
        return rows
            .Where(row => row.NodeType == "区段候选")
            .GroupBy(row => row.RoleHint)
            .Select(group => BuildRoleStats(archive, group.Key, group.ToList()))
            .ToDictionary(stat => stat.RoleHint, stat => stat, StringComparer.Ordinal);
    }

    private static RoleStats BuildRoleStats(EexArchiveInfo archive, string roleHint, IReadOnlyList<EexEntryProbeRow> rows)
    {
        var total = rows.Sum(row => (long)row.Length);
        return new RoleStats(
            archive,
            roleHint,
            rows.Count,
            total,
            rows.Min(row => row.Length),
            rows.Max(row => row.Length),
            rows.Average(row => row.Length),
            rows.Average(row => row.ZeroPercent),
            rows.Average(row => row.SmallWordPercent),
            rows.Sum(row => row.TextHintCount),
            string.Join(" / ", rows.OrderBy(row => ParseHexOffset(row.OffsetHex)).Take(4).Select(row => row.OffsetHex)));
    }

    private static IReadOnlyList<EexArchiveInfo> SelectPeers(EexArchiveInfo target, IReadOnlyList<EexArchiveInfo> allArchives, int maxPeers)
    {
        var selected = new List<EexArchiveInfo> { target };
        var targetIdNumber = TryParseId(target.Id);
        var candidates = allArchives
            .Where(item => !string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path))
            .Where(item => !Path.GetFullPath(item.Path).Equals(Path.GetFullPath(target.Path), StringComparison.OrdinalIgnoreCase))
            .Select(item => new
            {
                Archive = item,
                Rank = BuildPeerRank(target, item, targetIdNumber),
                Distance = ComputeIdDistance(targetIdNumber, item.Id)
            })
            .Where(item => item.Rank < 900)
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Distance)
            .ThenBy(item => item.Archive.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Archive.FileName, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item.Archive);

        foreach (var candidate in candidates)
        {
            if (selected.Count >= maxPeers) break;
            if (selected.Any(item => Path.GetFullPath(item.Path).Equals(Path.GetFullPath(candidate.Path), StringComparison.OrdinalIgnoreCase))) continue;
            selected.Add(candidate);
        }

        return selected;
    }

    private static int BuildPeerRank(EexArchiveInfo target, EexArchiveInfo peer, int? targetIdNumber)
    {
        var sameId = !string.IsNullOrWhiteSpace(target.Id) && target.Id.Equals(peer.Id, StringComparison.OrdinalIgnoreCase);
        var sameCategory = target.Category.Equals(peer.Category, StringComparison.OrdinalIgnoreCase);
        var bothShape = target.Category.Contains("形象", StringComparison.Ordinal) && peer.Category.Contains("形象", StringComparison.Ordinal);
        if (sameId && bothShape) return 0;
        if (sameId) return 1;
        if (sameCategory && ComputeIdDistance(targetIdNumber, peer.Id) <= 3) return 2;
        if (sameCategory) return 3;
        if (bothShape && ComputeIdDistance(targetIdNumber, peer.Id) <= 3) return 4;
        return 900;
    }

    private static int ComputeIdDistance(int? targetIdNumber, string peerId)
    {
        if (!targetIdNumber.HasValue) return int.MaxValue / 2;
        var peerNumber = TryParseId(peerId);
        return peerNumber.HasValue ? Math.Abs(peerNumber.Value - targetIdNumber.Value) : int.MaxValue / 2;
    }

    private static string BuildPeerKind(EexArchiveInfo target, EexArchiveInfo peer)
    {
        if (Path.GetFullPath(peer.Path).Equals(Path.GetFullPath(target.Path), StringComparison.OrdinalIgnoreCase)) return "选中文件";
        if (!string.IsNullOrWhiteSpace(target.Id) && target.Id.Equals(peer.Id, StringComparison.OrdinalIgnoreCase) &&
            target.Category.Contains("形象", StringComparison.Ordinal) && peer.Category.Contains("形象", StringComparison.Ordinal))
        {
            return "同编号R/S";
        }
        if (!string.IsNullOrWhiteSpace(target.Id) && target.Id.Equals(peer.Id, StringComparison.OrdinalIgnoreCase)) return "同编号";
        if (target.Category.Equals(peer.Category, StringComparison.OrdinalIgnoreCase)) return "同分类邻近";
        return "参考文件";
    }

    private static int? TryParseId(string id)
    {
        return int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static int ParseHexOffset(string offsetHex)
    {
        if (string.IsNullOrWhiteSpace(offsetHex)) return int.MaxValue;
        var text = offsetHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? offsetHex[2..] : offsetHex;
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;
    }

    private static string ValueOrDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private sealed record RoleStats(
        EexArchiveInfo Archive,
        string RoleHint,
        int SectionCount,
        long TotalLength,
        int MinLength,
        int MaxLength,
        double AverageLength,
        double AverageZeroPercent,
        double AverageSmallWordPercent,
        int TextHintCount,
        string FirstOffsets);
}
