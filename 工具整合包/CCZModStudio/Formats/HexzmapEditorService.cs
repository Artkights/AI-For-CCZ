using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class HexzmapEditorService
{
    private readonly WriteOperationReportService _reportService = new();

    public HexzmapSaveResult SaveBlock(
        CczProject project,
        HexzmapProbeResult probe,
        HexzmapBlockInfo block,
        byte[] newCells,
        IReadOnlyDictionary<byte, string>? terrainNames = null,
        HexzmapBlockBinding? binding = null)
    {
        var expectedLength = checked(block.Width * block.Height);
        if (expectedLength <= 0 || newCells.Length != expectedLength)
        {
            throw new InvalidOperationException($"地形块长度必须是 {block.Width}x{block.Height} = {expectedLength} 字节。");
        }

        if (!block.CanEdit || block.DecodedLength != expectedLength + block.DataPrefixLength)
        {
            throw new InvalidOperationException(
                $"当前 Hexzmap 地形块 {block.MapId} 的段长度与地图格数不一致，已拒绝写回。段长度={block.SegmentLength}，格数={expectedLength}。");
        }

        binding ??= block.Binding ?? BuildLegacyExactBinding(block);
        if (binding == null || !binding.AuthorizesWrite ||
            !binding.MapId.Equals(block.MapId, StringComparison.OrdinalIgnoreCase) ||
            binding.DirectoryEntryIndex != block.Index ||
            binding.Width != block.Width || binding.Height != block.Height)
        {
            throw new InvalidOperationException("Hexzmap 块缺少可信绑定。只有精确编号绑定或用户确认并持久化的手动绑定允许写回；尺寸候选只能只读预览。");
        }

        var filePath = project.ResolveGameFile("Hexzmap.e5");
        var original = File.ReadAllBytes(filePath);
        var offset = block.DataOffset;
        if (offset < 0 || offset + newCells.Length > original.Length)
        {
            throw new InvalidOperationException("Hexzmap 写入范围越界，已拒绝保存。");
        }

        var changes = new List<WriteOperationChange>();
        var output = (byte[])original.Clone();
        var changedBytes = 0;
        for (var i = 0; i < newCells.Length; i++)
        {
            var absolute = offset + i;
            if (output[absolute] == newCells[i]) continue;
            var oldValue = output[absolute];
            var newValue = newCells[i];
            output[absolute] = newValue;
            changedBytes++;
            changes.Add(BuildChange(block, i, absolute, oldValue, newValue, terrainNames));
        }

        if (changedBytes == 0)
        {
            throw new InvalidOperationException("当前地形块没有检测到改动，无需保存。");
        }

        var tempPath = Path.Combine(Path.GetDirectoryName(filePath)!, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, output);
        var staged = File.ReadAllBytes(tempPath);
        var outputHash = WriteOperationReportService.ComputeSha256(output);
        if (!staged.AsSpan().SequenceEqual(output) ||
            !WriteOperationReportService.ComputeSha256(staged).Equals(outputHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(tempPath);
            throw new InvalidOperationException("Hexzmap 临时输出完整复读或哈希校验失败，目标文件未修改。");
        }

        var backupPath = CreateBeforeSaveBackup(project, filePath);
        AtomicReplaceWithRollback(tempPath, filePath, backupPath, output, outputHash);
        var verify = File.ReadAllBytes(filePath);
        if (!verify.AsSpan().SequenceEqual(output) ||
            !WriteOperationReportService.ComputeSha256(verify).Equals(outputHash, StringComparison.OrdinalIgnoreCase) ||
            !verify.AsSpan(offset, newCells.Length).SequenceEqual(newCells))
        {
            throw new InvalidOperationException("Hexzmap 保存后复读校验失败。已生成保存前备份，请用备份文件核对并恢复目标文件。");
        }

        var reportPath = WriteStructuredReport(project, probe, block, filePath, backupPath, original, output, changedBytes, changes);

        return new HexzmapSaveResult
        {
            FilePath = filePath,
            BackupPath = backupPath,
            ReportJsonPath = reportPath,
            BlockIndex = block.Index,
            MapId = block.MapId,
            OffsetHex = HexDisplayFormatter.FormatOffset(offset),
            ChangedCells = changes.Count,
            ChangedBytes = changedBytes
        };
    }

    private static HexzmapBlockBinding? BuildLegacyExactBinding(HexzmapBlockInfo block)
    {
        if (block.MapId.Length < 2 ||
            !block.MapId.StartsWith("M", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(block.MapId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapNumber) ||
            mapNumber != block.Index)
        {
            return null;
        }

        return new HexzmapBlockBinding
        {
            MapId = block.MapId,
            DirectoryEntryIndex = block.Index,
            Width = block.Width,
            Height = block.Height,
            Source = HexzmapBindingSource.ExactMapNumber,
            Confidence = 1f,
            Evidence = "兼容旧调用：地图编号与目录项索引一致。"
        };
    }

    private static void AtomicReplaceWithRollback(
        string tempPath,
        string targetPath,
        string backupPath,
        byte[] expected,
        string expectedHash)
    {
        try
        {
            try
            {
                File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(tempPath, targetPath, overwrite: true);
            }

            var verify = File.ReadAllBytes(targetPath);
            if (!verify.AsSpan().SequenceEqual(expected) ||
                !WriteOperationReportService.ComputeSha256(verify).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Hexzmap 原子替换后整文件复读校验失败。");
            }
        }
        catch
        {
            File.Copy(backupPath, targetPath, overwrite: true);
            throw;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private string WriteStructuredReport(
        CczProject project,
        HexzmapProbeResult probe,
        HexzmapBlockInfo block,
        string filePath,
        string backupPath,
        byte[] original,
        byte[] output,
        int changedBytes,
        IReadOnlyList<WriteOperationChange> changes)
    {
        var targetRelative = WriteOperationReportService.ToProjectRelativePath(project, filePath);
        var report = new WriteOperationReport
        {
            OperationKind = "Hexzmap地形格写入",
            SourceAction = "地形编辑保存前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = targetRelative,
            TargetPath = filePath,
            BackupPath = backupPath,
            BeforeSha256 = WriteOperationReportService.ComputeSha256(original),
            AfterSha256 = WriteOperationReportService.ComputeSha256(output),
            ChangedBytes = changedBytes,
            Summary = $"保存 Hexzmap 地形块 {block.MapId}（#{block.Index}），尺寸 {block.Width}x{block.Height}，修改地形格 {changes.Count} 个，字节改动 {changedBytes:N0}。",
            SafetyNotes = project.IsTestCopy
                ? "该报告由测试副本地形编辑流程自动生成。还原时请使用保存前生成的备份文件手动恢复。"
                : "该报告由当前 MOD 项目地形编辑直接保存流程自动生成。保存前已备份 Hexzmap.e5；如需回退，请使用备份文件手动恢复。",
            Changes = changes.ToList(),
            Metadata =
            {
                ["MapId"] = block.MapId,
                ["BlockIndex"] = block.Index.ToString(CultureInfo.InvariantCulture),
                ["PayloadOffset"] = HexDisplayFormatter.FormatOffset(probe.PayloadOffset),
                ["BlockOffset"] = block.OffsetHex,
                ["SegmentOffset"] = HexDisplayFormatter.FormatOffset(block.SegmentOffset),
                ["SegmentLength"] = block.SegmentLength.ToString(CultureInfo.InvariantCulture),
                ["DecodedLength"] = block.DecodedLength.ToString(CultureInfo.InvariantCulture),
                ["DataPrefixLength"] = block.DataPrefixLength.ToString(CultureInfo.InvariantCulture),
                ["Width"] = block.Width.ToString(CultureInfo.InvariantCulture),
                ["Height"] = block.Height.ToString(CultureInfo.InvariantCulture),
                ["MapPixelWidth"] = block.MapPixelWidth.ToString(CultureInfo.InvariantCulture),
                ["MapPixelHeight"] = block.MapPixelHeight.ToString(CultureInfo.InvariantCulture)
            }
        };
        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static WriteOperationChange BuildChange(
        HexzmapBlockInfo block,
        int cellIndex,
        int absoluteOffset,
        byte oldValue,
        byte newValue,
        IReadOnlyDictionary<byte, string>? terrainNames)
    {
        var x = cellIndex % block.Width;
        var y = cellIndex / block.Width;
        var oldName = GetTerrainName(terrainNames, oldValue);
        var newName = GetTerrainName(terrainNames, newValue);
        return new WriteOperationChange
        {
            Category = "Hexzmap地形格",
            TableName = "Hexzmap.e5",
            RowIndex = block.Index,
            ColumnName = $"({x},{y})",
            OffsetHex = HexDisplayFormatter.FormatOffset(absoluteOffset),
            ByteLength = 1,
            OldValue = FormatTerrainValue(oldValue, oldName),
            NewValue = FormatTerrainValue(newValue, newName),
            Annotation = $"{block.MapId} 地形格 ({x},{y}) 从 {FormatTerrainValue(oldValue, oldName)} 改为 {FormatTerrainValue(newValue, newName)}。"
        };
    }

    private static string FormatTerrainValue(byte value, string name)
        => string.IsNullOrWhiteSpace(name) ? HexDisplayFormatter.FormatByte(value) : $"{HexDisplayFormatter.FormatByte(value)}（{name}）";

    private static string GetTerrainName(IReadOnlyDictionary<byte, string>? terrainNames, byte id)
        => terrainNames != null && terrainNames.TryGetValue(id, out var name) ? name : string.Empty;

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = ProjectBackupPathService.EnsureBackupRootWritable(project);
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{Path.GetFileName(filePath)}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{stamp}_{suffix++}_{Path.GetFileName(filePath)}");
        }
        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }
}
