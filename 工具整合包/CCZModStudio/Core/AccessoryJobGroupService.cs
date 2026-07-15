using System.Data;
using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class AccessoryJobGroupService
{
    public const uint StartVirtualAddress = AccessoryJobGroupProfile.DefaultStartVirtualAddress;
    public const int MaxJobSeriesId = 39;
    private const byte Separator = 0xFF;
    private const byte Padding = 0x90;
    private const int MaxProbeLength = 256;

    private readonly HexTableReader _tableReader = new();
    private readonly WriteOperationReportService _reportService = new();

    public AccessoryJobGroupProfile Read(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tables);

        var targetPath = ResolveTargetPath(project);
        var bytes = ReadAllBytesShared(targetPath);
        var fileOffset = MapStartAddress(targetPath);
        var writableLength = DetectWritableLength(bytes, fileOffset, out var payloadLength, out var diagnostics);
        var rawBytes = bytes.Skip(checked((int)fileOffset)).Take(payloadLength).ToArray();
        var jobSeriesNames = BuildJobSeriesNameLookup(project, tables, diagnostics);
        var groups = ParseRawGroups(rawBytes, jobSeriesNames, diagnostics);

        return new AccessoryJobGroupProfile
        {
            StartVirtualAddress = StartVirtualAddress,
            FileOffset = fileOffset,
            WritableLength = writableLength,
            RawBytes = rawBytes,
            Groups = groups,
            Diagnostics = diagnostics
        };
    }

    public AccessoryJobGroupPreview Preview(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        IReadOnlyList<IReadOnlyList<int>> groupIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(groupIds);

        var targetPath = ResolveTargetPath(project);
        var bytes = ReadAllBytesShared(targetPath);
        var fileOffset = MapStartAddress(targetPath);
        var writableLength = DetectWritableLength(bytes, fileOffset, out _, out var diagnostics);
        var jobSeriesNames = BuildJobSeriesNameLookup(project, tables, diagnostics);
        var normalized = NormalizeInputGroups(groupIds, jobSeriesNames, diagnostics);
        var payload = SerializeGroups(normalized.Select(group => group.JobSeriesIds).ToArray(), diagnostics);
        var canWrite = diagnostics.All(line => !line.StartsWith("错误：", StringComparison.Ordinal));

        byte[] padded;
        if (payload.Length <= writableLength)
        {
            padded = Enumerable.Repeat(Padding, writableLength).ToArray();
            Buffer.BlockCopy(payload, 0, padded, 0, payload.Length);
        }
        else
        {
            diagnostics.Add($"错误：序列化后 {payload.Length} 字节超过当前可写安全区 {writableLength} 字节。");
            canWrite = false;
            padded = payload;
        }

        return new AccessoryJobGroupPreview
        {
            StartVirtualAddress = StartVirtualAddress,
            FileOffset = fileOffset,
            WritableLength = writableLength,
            Groups = normalized,
            PayloadBytes = payload,
            PaddedBytes = padded,
            CanWrite = canWrite,
            Diagnostics = diagnostics
        };
    }

    public AccessoryJobGroupSaveResult Save(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        IReadOnlyList<IReadOnlyList<int>> groupIds)
    {
        ProjectVersionGuardService.EnsureCoreFileCompatibleForWrite(project, "Ekd5.exe");
        var preview = Preview(project, tables, groupIds);
        if (!preview.CanWrite)
        {
            throw new InvalidOperationException("辅助装备多兵种分组预览存在错误，已取消写入：\r\n" + string.Join("\r\n", preview.Diagnostics));
        }

        var targetPath = ResolveTargetPath(project);
        var before = ReadAllBytesShared(targetPath);
        var output = (byte[])before.Clone();
        Buffer.BlockCopy(preview.PaddedBytes.ToArray(), 0, output, checked((int)preview.FileOffset), preview.PaddedBytes.Count);
        var changedBytes = CountDifferences(before, output);
        var backupPath = CreateBeforeSaveBackup(project, targetPath);
        File.WriteAllBytes(targetPath, output);

        var reportPath = WriteReport(project, targetPath, backupPath, before, output, preview, changedBytes);
        return new AccessoryJobGroupSaveResult
        {
            TargetFilePath = targetPath,
            BackupPath = backupPath,
            ReportJsonPath = reportPath,
            BytesWritten = preview.PaddedBytes.Count,
            ChangedBytes = changedBytes,
            Preview = preview
        };
    }

    public static byte[] SerializeForTest(IReadOnlyList<IReadOnlyList<int>> groupIds)
    {
        var diagnostics = new List<string>();
        var normalized = NormalizeInputGroups(groupIds, new Dictionary<int, string>(), diagnostics);
        var bytes = SerializeGroups(normalized.Select(group => group.JobSeriesIds).ToArray(), diagnostics);
        var errors = diagnostics.Where(line => line.StartsWith("错误：", StringComparison.Ordinal)).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException(string.Join("\r\n", errors));
        return bytes;
    }

    public static IReadOnlyList<IReadOnlyList<int>> ParseForTest(IReadOnlyList<byte> bytes)
    {
        var diagnostics = new List<string>();
        var groups = ParseRawGroupIds(bytes.ToArray(), diagnostics);
        var errors = diagnostics.Where(line => line.StartsWith("错误：", StringComparison.Ordinal)).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException(string.Join("\r\n", errors));
        return groups;
    }

    private static string ResolveTargetPath(CczProject project)
    {
        var path = project.ResolveGameFile("Ekd5.exe");
        if (!File.Exists(path)) throw new FileNotFoundException("找不到 Ekd5.exe，无法读取辅助装备多兵种分组。", path);
        return path;
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) break;
            offset += read;
        }

        if (offset == bytes.Length) return bytes;
        Array.Resize(ref bytes, offset);
        return bytes;
    }

    private static long MapStartAddress(string exePath)
        => PeAddressMapper.Load(exePath).VirtualAddressToFileOffset(StartVirtualAddress);

    private static int DetectWritableLength(byte[] bytes, long fileOffset, out int payloadLength, out List<string> diagnostics)
    {
        diagnostics = [];
        if (fileOffset < 0 || fileOffset >= bytes.LongLength)
        {
            diagnostics.Add($"错误：映射文件偏移 {HexDisplayFormatter.FormatOffset(fileOffset)} 越界。");
            payloadLength = 0;
            return 0;
        }

        var start = checked((int)fileOffset);
        var maxEnd = Math.Min(bytes.Length, start + MaxProbeLength);
        var firstPadding = -1;
        for (var i = start; i < maxEnd; i++)
        {
            var value = bytes[i];
            if (value == Padding)
            {
                firstPadding = i;
                break;
            }

            if (value == Separator || value <= MaxJobSeriesId) continue;
            diagnostics.Add($"错误：安全区探测遇到未知字节 {HexDisplayFormatter.FormatByte(value)}，位置 {HexDisplayFormatter.FormatOffset(i)}。");
            payloadLength = Math.Max(0, i - start);
            return payloadLength;
        }

        if (firstPadding < 0)
        {
            diagnostics.Add($"错误：从 {HexDisplayFormatter.FormatOffset(fileOffset)} 起 {MaxProbeLength} 字节内没有找到 0x90 填充，无法确认安全区长度。");
            payloadLength = 0;
            return 0;
        }

        var paddingEnd = firstPadding;
        while (paddingEnd < bytes.Length && bytes[paddingEnd] == Padding && paddingEnd - start < MaxProbeLength)
        {
            paddingEnd++;
        }

        payloadLength = firstPadding - start;
        var trailingSeparatorCount = 0;
        for (var i = start + payloadLength - 1; i >= start && bytes[i] == Separator; i--)
        {
            trailingSeparatorCount++;
        }

        if (trailingSeparatorCount > 1)
        {
            payloadLength -= trailingSeparatorCount - 1;
            diagnostics.Add($"安全区探测：表尾存在 {trailingSeparatorCount - 1} 个额外 FF 预留填充，解析时按单个结尾 FF 处理。");
        }

        var writableLength = paddingEnd - start;
        if (payloadLength <= 0)
        {
            diagnostics.Add("错误：当前地址处没有可解析的 FF 分组数据。");
        }
        else if (bytes[start] != Separator)
        {
            diagnostics.Add($"错误：分组数据必须以 FF 开头，当前为 {HexDisplayFormatter.FormatByte(bytes[start])}。");
        }
        else if (bytes[start + payloadLength - 1] != Separator)
        {
            diagnostics.Add($"错误：分组数据必须以 FF 结尾，当前结尾为 {HexDisplayFormatter.FormatByte(bytes[start + payloadLength - 1])}。");
        }

        diagnostics.Add($"安全区探测：分组数据 {payloadLength} 字节，连续 0x90 填充 {writableLength - payloadLength} 字节，总可写 {writableLength} 字节。");
        return writableLength;
    }

    private IReadOnlyDictionary<int, string> BuildJobSeriesNameLookup(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        List<string> diagnostics)
    {
        var profile = new CczEngineProfileService().Detect(project);
        var tableName = profile.TableHints.JobSeriesTable;
        var table = tables.FirstOrDefault(table => table.TableName.Equals(tableName, StringComparison.Ordinal))
                    ?? tables.FirstOrDefault(table => table.TableName.EndsWith("-3 兵种系", StringComparison.Ordinal));
        if (table == null)
        {
            diagnostics.Add("警告：未找到兵种系表，分组只能显示编号。");
            return new Dictionary<int, string>();
        }

        try
        {
            var read = _tableReader.Read(project, table, tables);
            if (!read.Validation.IsUsable)
            {
                diagnostics.Add($"警告：兵种系表不可用：{table.TableName}。");
                return new Dictionary<int, string>();
            }

            return read.Data.Rows
                .Cast<DataRow>()
                .Where(row => row.Table.Columns.Contains("ID"))
                .ToDictionary(
                    row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture),
                    row => row.Table.Columns.Contains("名称")
                        ? Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty
                        : string.Empty);
        }
        catch (Exception ex)
        {
            diagnostics.Add("警告：读取兵种系名称失败：" + ex.Message);
            return new Dictionary<int, string>();
        }
    }

    private static IReadOnlyList<AccessoryJobGroup> ParseRawGroups(
        byte[] rawBytes,
        IReadOnlyDictionary<int, string> jobSeriesNames,
        List<string> diagnostics)
    {
        var groupIds = ParseRawGroupIds(rawBytes, diagnostics);
        return NormalizeInputGroups(groupIds, jobSeriesNames, diagnostics, allowExistingDuplicates: true);
    }

    private static IReadOnlyList<IReadOnlyList<int>> ParseRawGroupIds(byte[] rawBytes, List<string> diagnostics)
    {
        var groups = new List<IReadOnlyList<int>>();
        if (rawBytes.Length == 0)
        {
            diagnostics.Add("错误：分组字节为空。");
            return groups;
        }

        if (rawBytes[0] != Separator)
        {
            diagnostics.Add($"错误：分组数据必须以 FF 开头，当前为 {HexDisplayFormatter.FormatByte(rawBytes[0])}。");
            return groups;
        }

        if (rawBytes[^1] != Separator)
        {
            diagnostics.Add($"错误：分组数据必须以 FF 结尾，当前为 {HexDisplayFormatter.FormatByte(rawBytes[^1])}。");
        }

        var current = new List<int>();
        for (var i = 1; i < rawBytes.Length; i++)
        {
            var value = rawBytes[i];
            if (value == Separator)
            {
                if (current.Count == 0)
                {
                    if (i != rawBytes.Length - 1)
                    {
                        diagnostics.Add($"错误：第 {i} 字节出现空分组。");
                    }
                }
                else
                {
                    groups.Add(current.ToArray());
                    current.Clear();
                }
                continue;
            }

            if (value > MaxJobSeriesId)
            {
                diagnostics.Add($"错误：兵种系 ID 超出 0..{MaxJobSeriesId}：{value}，位置 {i}。");
            }
            current.Add(value);
        }

        if (current.Count > 0)
        {
            diagnostics.Add("错误：最后一个分组没有 FF 结尾。");
        }

        return groups;
    }

    private static IReadOnlyList<AccessoryJobGroup> NormalizeInputGroups(
        IReadOnlyList<IReadOnlyList<int>> groupIds,
        IReadOnlyDictionary<int, string> jobSeriesNames,
        List<string> diagnostics,
        bool allowExistingDuplicates = false)
    {
        var result = new List<AccessoryJobGroup>();
        var seen = new Dictionary<int, int>();
        for (var groupIndex = 0; groupIndex < groupIds.Count; groupIndex++)
        {
            var source = groupIds[groupIndex];
            if (source.Count == 0)
            {
                diagnostics.Add($"错误：组 {groupIndex + 1} 不能为空。");
                continue;
            }

            var normalized = new List<int>();
            var local = new HashSet<int>();
            foreach (var id in source)
            {
                if (id < 0 || id > MaxJobSeriesId)
                {
                    diagnostics.Add($"错误：组 {groupIndex + 1} 包含越界兵种系 ID {id}，合法范围 0..{MaxJobSeriesId}。");
                    continue;
                }

                if (!local.Add(id))
                {
                    diagnostics.Add($"错误：组 {groupIndex + 1} 内重复兵种系 ID {id:D2}。");
                    continue;
                }

                if (!allowExistingDuplicates && seen.TryGetValue(id, out var existingGroup))
                {
                    diagnostics.Add($"错误：兵种系 ID {id:D2} 同时出现在组 {existingGroup + 1} 和组 {groupIndex + 1}。");
                    continue;
                }

                seen[id] = groupIndex;
                normalized.Add(id);
            }

            if (normalized.Count == 0) continue;
            result.Add(new AccessoryJobGroup(
                result.Count,
                normalized,
                normalized.Select(id => jobSeriesNames.TryGetValue(id, out var name) ? name : string.Empty).ToArray()));
        }

        return result;
    }

    private static byte[] SerializeGroups(IReadOnlyList<IReadOnlyList<int>> groupIds, List<string> diagnostics)
    {
        if (groupIds.Count == 0)
        {
            diagnostics.Add("错误：至少需要一个辅助装备兵种分组。");
            return Array.Empty<byte>();
        }

        var output = new List<byte> { Separator };
        for (var groupIndex = 0; groupIndex < groupIds.Count; groupIndex++)
        {
            var group = groupIds[groupIndex];
            if (group.Count == 0)
            {
                diagnostics.Add($"错误：组 {groupIndex + 1} 不能为空。");
                continue;
            }

            foreach (var id in group)
            {
                if (id < 0 || id > MaxJobSeriesId)
                {
                    diagnostics.Add($"错误：组 {groupIndex + 1} 包含越界兵种系 ID {id}，合法范围 0..{MaxJobSeriesId}。");
                    continue;
                }

                output.Add((byte)id);
            }

            output.Add(Separator);
        }

        return output.ToArray();
    }

    private static int CountDifferences(byte[] before, byte[] after)
    {
        if (before.Length != after.Length) throw new InvalidOperationException("内部错误：比较字节数组长度不同。");
        var count = 0;
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i]) count++;
        }
        return count;
    }

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = ProjectBackupPathService.EnsureBackupRootWritable(project);
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{Path.GetFileName(filePath)}");
        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }

    private string WriteReport(
        CczProject project,
        string targetPath,
        string backupPath,
        byte[] before,
        byte[] after,
        AccessoryJobGroupPreview preview,
        int changedBytes)
    {
        var targetRelative = WriteOperationReportService.ToProjectRelativePath(project, targetPath);
        var oldBytes = before.Skip(checked((int)preview.FileOffset)).Take(preview.PaddedBytes.Count).ToArray();
        var newBytes = preview.PaddedBytes.ToArray();
        var report = new WriteOperationReport
        {
            OperationKind = "辅助装备多兵种分组",
            SourceAction = "辅助装备多兵种分组保存前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = targetRelative,
            TargetPath = targetPath,
            BackupPath = backupPath,
            BeforeSha256 = WriteOperationReportService.ComputeSha256(before),
            AfterSha256 = WriteOperationReportService.ComputeSha256(after),
            ChangedBytes = changedBytes,
            Summary = $"写入辅助装备多兵种分组到 {targetRelative}，地址 0x{StartVirtualAddress:X8}，文件偏移 {preview.FileOffsetHex}，分组 {preview.Groups.Count} 个，写入 {preview.PaddedBytes.Count} 字节。",
            SafetyNotes = "写入方式为定长覆盖：序列化 FF 分组后用 0x90 补齐当前探测到的安全区，不插入、不删除、不扩展 EXE。",
            FormatCheckSummary = "格式要求：必须以 FF 开头、以 FF 结尾；每组为 FF + 1..N 个 0..39 的 6.5-3 兵种系 ID。",
            RiskSummary = project.IsTestCopy ? "当前目标是测试副本。" : "当前目标不是测试副本，写入前请确认已备份。",
            Changes =
            {
                new WriteOperationChange
                {
                    Category = "EXE字节",
                    TableName = targetRelative,
                    ColumnName = "0044C341 辅助装备多兵种分组",
                    OffsetHex = preview.FileOffsetHex,
                    ByteLength = preview.PaddedBytes.Count,
                    OldValue = HexDisplayFormatter.FormatByteList(oldBytes),
                    NewValue = HexDisplayFormatter.FormatByteList(newBytes),
                    Annotation = string.Join("；", preview.Groups.Select(group => group.SummaryText))
                }
            },
            Metadata =
            {
                ["StartVirtualAddress"] = $"0x{StartVirtualAddress:X8}",
                ["FileOffset"] = preview.FileOffsetHex,
                ["WritableLength"] = preview.WritableLength.ToString(CultureInfo.InvariantCulture),
                ["PayloadLength"] = preview.PayloadBytes.Count.ToString(CultureInfo.InvariantCulture),
                ["PaddedLength"] = preview.PaddedBytes.Count.ToString(CultureInfo.InvariantCulture),
                ["GroupCount"] = preview.Groups.Count.ToString(CultureInfo.InvariantCulture)
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }
}
