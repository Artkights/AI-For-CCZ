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
        IReadOnlyDictionary<byte, string>? terrainNames = null)
    {
        ProjectVersionGuardService.EnsureCoreFileCompatibleForWrite(project, "Hexzmap.e5");
        var expectedLength = checked(block.Width * block.Height);
        if (expectedLength <= 0 || newCells.Length != expectedLength)
        {
            throw new InvalidOperationException($"地形块长度必须是 {block.Width}x{block.Height} = {expectedLength} 字节。");
        }

        if (!block.CanEdit || block.SegmentLength != expectedLength + HexzmapProbeReader.TerrainHeaderSize)
        {
            throw new InvalidOperationException(
                $"当前 Hexzmap 地形块 {block.MapId} 的段长度与地图格数不一致，已拒绝写回。段长度={block.SegmentLength}，格数={expectedLength}。");
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

        var backupPath = CreateBeforeSaveBackup(project, filePath);
        File.WriteAllBytes(filePath, output);
        var verify = File.ReadAllBytes(filePath);
        if (verify.Length != output.Length || !verify.AsSpan(offset, newCells.Length).SequenceEqual(newCells))
        {
            throw new InvalidOperationException("Hexzmap 保存后复读校验失败。已生成保存前备份，请用备份历史核对目标文件。");
        }

        var reportPath = WriteStructuredReport(project, probe, block, filePath, backupPath, original, output, changedBytes, changes);

        return new HexzmapSaveResult
        {
            FilePath = filePath,
            BackupPath = backupPath,
            ReportJsonPath = reportPath,
            BlockIndex = block.Index,
            MapId = block.MapId,
            OffsetHex = "0x" + offset.ToString("X", CultureInfo.InvariantCulture),
            ChangedCells = changes.Count,
            ChangedBytes = changedBytes
        };
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
                ? "该报告由测试副本地形编辑流程自动生成。还原时请使用备份历史/回滚页的预览和再备份流程。"
                : "该报告由当前 MOD 项目地形编辑直接保存流程自动生成。保存前已备份 Hexzmap.e5；如需回退，请使用备份文件或备份历史功能。",
            Changes = changes.ToList(),
            Metadata =
            {
                ["MapId"] = block.MapId,
                ["BlockIndex"] = block.Index.ToString(CultureInfo.InvariantCulture),
                ["PayloadOffset"] = "0x" + probe.PayloadOffset.ToString("X", CultureInfo.InvariantCulture),
                ["BlockOffset"] = block.OffsetHex,
                ["SegmentOffset"] = "0x" + block.SegmentOffset.ToString("X", CultureInfo.InvariantCulture),
                ["SegmentLength"] = block.SegmentLength.ToString(CultureInfo.InvariantCulture),
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
            OffsetHex = "0x" + absoluteOffset.ToString("X", CultureInfo.InvariantCulture),
            ByteLength = 1,
            OldValue = FormatTerrainValue(oldValue, oldName),
            NewValue = FormatTerrainValue(newValue, newName),
            Annotation = $"{block.MapId} 地形格 ({x},{y}) 从 {FormatTerrainValue(oldValue, oldName)} 改为 {FormatTerrainValue(newValue, newName)}。"
        };
    }

    private static string FormatTerrainValue(byte value, string name)
        => string.IsNullOrWhiteSpace(name) ? $"0x{value:X2}" : $"0x{value:X2}（{name}）";

    private static string GetTerrainName(IReadOnlyDictionary<byte, string>? terrainNames, byte id)
        => terrainNames != null && terrainNames.TryGetValue(id, out var name) ? name : string.Empty;

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
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
