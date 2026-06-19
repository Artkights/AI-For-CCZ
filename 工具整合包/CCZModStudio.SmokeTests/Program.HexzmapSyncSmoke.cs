using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;

partial class Program
{
    static void RunHexzmapSyncSmoke(CczProject project)
    {
        var reader = new HexzmapProbeReader();
        var probe = reader.Read(project);
        if (probe.DirectoryEntries.Count < 50)
        {
            throw new InvalidOperationException($"Hexzmap index should expose the current 6.5 terrain table. Entries={probe.DirectoryEntries.Count}.");
        }

        AssertEditableBlock(probe, reader, "M000", 20, 20);
        AssertEditableBlock(probe, reader, "M003", 28, 20);
        var m045 = AssertEditableBlock(probe, reader, "M045", 40, 24);
        if (m045.Index != 45)
        {
            throw new InvalidOperationException($"M045 must remain bound to Hexzmap index 45 after missing M044. Actual index={m045.Index}.");
        }

        if (probe.Blocks.Any(block => block.MapId.Equals("M100", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("M100.jpg is an extra map image and must not be bound to a Hexzmap index entry.");
        }

        var editable = probe.Blocks.Count(block => block.CanEdit);
        if (editable < 50)
        {
            throw new InvalidOperationException($"Expected most Hexzmap blocks to be editable after index-based sync. Editable={editable}, total={probe.Blocks.Count}.");
        }

        Console.WriteLine($"HEXZMAP_SYNC_SMOKE_OK entries={probe.DirectoryEntries.Count} blocks={probe.Blocks.Count} editable={editable} file={Path.GetFileName(probe.Path)}");
    }

    static void RunHexzmapWriteSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "HexzmapWriteSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);
        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5", "Hexzmap.e5" })
        {
            var source = Path.Combine(project.GameRoot, fileName);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Hexzmap write smoke requires core project files.", source);
            }

            File.Copy(source, Path.Combine(smokeRoot, fileName), overwrite: false);
        }

        var sourceMapRoot = Path.Combine(project.GameRoot, "Map");
        var targetMapRoot = Path.Combine(smokeRoot, "Map");
        Directory.CreateDirectory(targetMapRoot);
        foreach (var mapFile in Directory.GetFiles(sourceMapRoot, "M*.jpg", SearchOption.TopDirectoryOnly))
        {
            File.Copy(mapFile, Path.Combine(targetMapRoot, Path.GetFileName(mapFile)), overwrite: false);
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=Hexzmap write smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var reader = new HexzmapProbeReader();
        var beforeProbe = reader.Read(testProject);
        var block = beforeProbe.Blocks.FirstOrDefault(block => block.MapId.Equals("M000", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Hexzmap write smoke could not find M000.");
        var beforeCells = reader.GetBlockCells(beforeProbe, block);
        if (beforeCells.Length == 0)
        {
            throw new InvalidOperationException("Hexzmap write smoke could not read M000 cells.");
        }

        var replacement = beforeCells.ToArray();
        replacement[0] = replacement[0] == byte.MaxValue ? (byte)0 : (byte)(replacement[0] + 1);
        var editor = new HexzmapEditorService();
        var save = editor.SaveBlock(testProject, beforeProbe, block, replacement);
        if (save.ChangedCells != 1 || !File.Exists(save.BackupPath) || !File.Exists(save.ReportJsonPath))
        {
            throw new InvalidOperationException("Hexzmap write smoke did not produce expected backup/report evidence.");
        }

        var afterProbe = reader.Read(testProject);
        var afterBlock = afterProbe.Blocks.First(item => item.MapId.Equals("M000", StringComparison.OrdinalIgnoreCase));
        var afterCells = reader.GetBlockCells(afterProbe, afterBlock);
        if (afterCells.Length != replacement.Length || afterCells[0] != replacement[0])
        {
            throw new InvalidOperationException("Hexzmap write smoke reread verification failed.");
        }

        Console.WriteLine($"HEXZMAP_WRITE_SMOKE_OK root={smokeRoot} map={save.MapId} offset={save.OffsetHex} changed={save.ChangedCells} backup={Path.GetFileName(save.BackupPath)}");
    }

    static void RunMapTerrainConsistencySmoke(CczProject project)
    {
        var reader = new HexzmapProbeReader();
        var probe = reader.Read(project);
        if (probe.Blocks.Count == 0)
        {
            throw new InvalidOperationException("Map terrain consistency smoke found no Hexzmap blocks.");
        }

        var checkedBlocks = 0;
        foreach (var block in probe.Blocks.Where(block => block.CanEdit).Take(12))
        {
            var mapPath = Path.Combine(project.GameRoot, "Map", block.MapId + ".jpg");
            if (!File.Exists(mapPath))
            {
                throw new InvalidOperationException($"Map terrain consistency smoke missing map image for {block.MapId}.");
            }

            var expectedCells = checked(block.Width * block.Height);
            if (block.BytesRead != expectedCells ||
                block.DecodedLength != expectedCells + block.DataPrefixLength)
            {
                throw new InvalidOperationException($"Map terrain consistency mismatch for {block.MapId}: size={block.Width}x{block.Height}, bytes={block.BytesRead}, decoded={block.DecodedLength}, prefix={block.DataPrefixLength}.");
            }

            var cells = reader.GetBlockCells(probe, block);
            if (cells.Length != expectedCells)
            {
                throw new InvalidOperationException($"Map terrain consistency cell read mismatch for {block.MapId}: {cells.Length}/{expectedCells}.");
            }

            checkedBlocks++;
        }

        if (checkedBlocks == 0)
        {
            throw new InvalidOperationException("Map terrain consistency smoke found no editable map/Hexzmap pairs.");
        }

        Console.WriteLine($"MAP_TERRAIN_CONSISTENCY_SMOKE_OK checked={checkedBlocks} blocks={probe.Blocks.Count} file={Path.GetFileName(probe.Path)}");
    }

    private static HexzmapBlockInfo AssertEditableBlock(HexzmapProbeResult probe, HexzmapProbeReader reader, string mapId, int width, int height)
    {
        var block = probe.Blocks.FirstOrDefault(block => block.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Hexzmap block {mapId} was not found.");

        var expectedCells = checked(width * height);
        if (block.Width != width ||
            block.Height != height ||
            block.BytesRead != expectedCells ||
            block.DecodedLength != expectedCells + block.DataPrefixLength ||
            !block.CanEdit)
        {
            throw new InvalidOperationException(
                $"Hexzmap block {mapId} did not sync with current map logic. " +
                $"size={block.Width}x{block.Height}, bytes={block.BytesRead}, decoded={block.DecodedLength}, prefix={block.DataPrefixLength}, canEdit={block.CanEdit}.");
        }

        var cells = reader.GetBlockCells(probe, block);
        if (cells.Length != expectedCells)
        {
            throw new InvalidOperationException($"Hexzmap block {mapId} cell read length mismatch: {cells.Length}/{expectedCells}.");
        }

        return block;
    }
}
