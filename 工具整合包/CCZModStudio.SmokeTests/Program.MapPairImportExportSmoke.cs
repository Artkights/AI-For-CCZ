using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

partial class Program
{
    static void RunMapPairImportExportSmoke(CczProject project)
    {
        var sourceMap = FindMapPairSmokeMap(project, "M000");
        var smokeRoot = CreateMapPairSmokeProjectRoot(project, sourceMap);
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var service = new MapPairImportExportService();
        var codec = new HexzmapTerrainBmpCodec();
        var reader = new HexzmapProbeReader();
        var mapItem = FindMapPairSmokeMap(testProject, "M000");
        var probe = reader.Read(testProject);
        var block = FindMapPairSmokeBlock(probe, "M000");
        if (block.Width != 20 || block.Height != 20 || block.BytesRead != 400 || !block.CanEdit)
        {
            throw new InvalidOperationException($"Map pair smoke requires editable M000 20x20. Actual={block.Width}x{block.Height}, bytes={block.BytesRead}, canEdit={block.CanEdit}.");
        }

        AssertDesktopHexzmapBmpSample(codec);
        var originalCells = reader.GetBlockCells(probe, block);
        var prefix = reader.GetBlockSegmentPrefix(probe, block);
        if (originalCells.Length != 400 || prefix.Length != HexzmapTerrainBmpCodec.PrefixSize)
        {
            throw new InvalidOperationException("Map pair smoke could not read M000 cells/prefix.");
        }

        var exportCells = originalCells.Select((value, index) => index % 7 == 0 ? (byte)(value ^ 0x11) : value).ToArray();
        var draft = new MapWorkbenchDraft
        {
            DraftId = "map-pair-smoke",
            BoundMapId = "M000",
            GridWidth = block.Width,
            GridHeight = block.Height,
            TileSize = MapResourceItem.MapTilePixelSize,
            BaseLayerPath = mapItem.Path,
            OriginalTerrainCells = exportCells.ToArray(),
            TerrainCells = exportCells.ToArray(),
            AutoGenerateMapFromTerrain = true,
            BeautifyGeneratedMap = false,
            MaterialRoot = string.Empty
        };

        var exportFolder = Path.Combine(smokeRoot, "MapPairExport");
        var export = service.ExportCurrentMapPair(testProject, draft, mapItem, probe, block, Array.Empty<MaterialAsset>(), exportFolder);
        if (!File.Exists(export.JpegPath) || !File.Exists(export.TerrainBmpPath))
        {
            throw new InvalidOperationException("Map pair export did not write both files.");
        }

        using (var image = Image.FromFile(export.JpegPath))
        {
            if (image.Width != 960 || image.Height != 960)
            {
                throw new InvalidOperationException($"Exported M000.JPG must be 960x960. Actual={image.Width}x{image.Height}.");
            }
        }

        var exportedBmp = codec.Decode(export.TerrainBmpPath);
        AssertMapPairEqual(exportedBmp.Width, 20, "Exported BMP width");
        AssertMapPairEqual(exportedBmp.Height, 20, "Exported BMP height");
        AssertMapPairEqual(new FileInfo(export.TerrainBmpPath).Length, 1480L, "Exported BMP file length");
        AssertBytes(prefix, exportedBmp.PrefixBytes, "Exported BMP prefix");
        AssertBytes(exportCells, exportedBmp.TerrainCells, "Exported BMP terrain cells");

        AssertNonFourMultipleWidthBmp(codec, smokeRoot);
        AssertMapPairImport(service, codec, reader, testProject, block, prefix, originalCells, smokeRoot);
        AssertMapPairImportBlockers(service, codec, testProject, block, prefix, smokeRoot);
        AssertMapPairButtonsUi(project);

        Console.WriteLine($"MAP_PAIR_IMPORT_EXPORT_SMOKE_OK root={smokeRoot} export={exportFolder}");
    }

    private static void AssertDesktopHexzmapBmpSample(HexzmapTerrainBmpCodec codec)
    {
        var samplePath = @"C:\Users\Arknights\Desktop\Hexzmap1.BMP";
        if (!File.Exists(samplePath))
        {
            Console.WriteLine("MAP_PAIR_SAMPLE_SKIPPED missing=" + samplePath);
            return;
        }

        var bytes = File.ReadAllBytes(samplePath);
        var sample = codec.Decode(samplePath);
        AssertMapPairEqual(bytes[0], (byte)'B', "Sample BMP signature B");
        AssertMapPairEqual(bytes[1], (byte)'M', "Sample BMP signature M");
        AssertMapPairEqual(BitConverter.ToUInt32(bytes, 14), 40u, "Sample DIB header size");
        AssertMapPairEqual(sample.BitsPerPixel, 8, "Sample bits per pixel");
        AssertMapPairEqual(BitConverter.ToInt32(bytes, 22), -20, "Sample top-down height");
        AssertMapPairEqual(sample.PixelOffset, HexzmapTerrainBmpCodec.PixelOffset, "Sample pixel offset");
        AssertMapPairEqual(bytes.Length - sample.PixelOffset, 2 + sample.Width * sample.Height, "Sample pixel payload length");
        AssertMapPairEqual(sample.Width, 20, "Sample width");
        AssertMapPairEqual(sample.Height, 20, "Sample height");
        AssertMapPairEqual(sample.TerrainCells.Length, 400, "Sample terrain length");
        AssertMapPairEqual(bytes.Length, 1480, "Sample file length");
    }

    private static void AssertNonFourMultipleWidthBmp(HexzmapTerrainBmpCodec codec, string smokeRoot)
    {
        var block = new HexzmapBlockInfo
        {
            Index = 56,
            MapId = "M056",
            DataPrefixLength = HexzmapTerrainBmpCodec.PrefixSize,
            DecodedLength = HexzmapTerrainBmpCodec.PrefixSize + 37 * 30,
            SegmentLength = HexzmapTerrainBmpCodec.PrefixSize + 37 * 30,
            CanEdit = true,
            Width = 37,
            Height = 30,
            BytesRead = 37 * 30,
            MapPixelWidth = 37 * MapResourceItem.MapTilePixelSize,
            MapPixelHeight = 30 * MapResourceItem.MapTilePixelSize
        };
        var cells = Enumerable.Range(0, block.BytesRead).Select(index => (byte)(index % 256)).ToArray();
        var path = Path.Combine(smokeRoot, "M056.BMP");
        var prefix = new byte[] { 0x12, 0x34 };
        codec.Encode(block, prefix, cells, path);
        AssertMapPairEqual(new FileInfo(path).Length, HexzmapTerrainBmpCodec.PixelOffset + 2 + 37 * 30, "Non-4-width BMP file length");
        var decoded = codec.Decode(path);
        AssertMapPairEqual(decoded.Width, 37, "Non-4-width decoded width");
        AssertMapPairEqual(decoded.Height, 30, "Non-4-width decoded height");
        AssertBytes(prefix, decoded.PrefixBytes, "Non-4-width prefix");
        AssertBytes(cells, decoded.TerrainCells, "Non-4-width cells");
    }

    private static void AssertMapPairImport(
        MapPairImportExportService service,
        HexzmapTerrainBmpCodec codec,
        HexzmapProbeReader reader,
        CczProject testProject,
        HexzmapBlockInfo block,
        byte[] prefix,
        byte[] originalCells,
        string smokeRoot)
    {
        var importFolder = Path.Combine(smokeRoot, "MapPairImport");
        Directory.CreateDirectory(importFolder);
        var sourceJpg = Path.Combine(importFolder, "M000.JPG");
        SaveSolidJpeg(sourceJpg, block.Width * MapResourceItem.MapTilePixelSize, block.Height * MapResourceItem.MapTilePixelSize, Color.FromArgb(28, 96, 172));
        var importCells = originalCells.Select(value => (byte)(value ^ 0x5A)).ToArray();
        codec.Encode(block, prefix, importCells, Path.Combine(importFolder, "M000.BMP"));

        var preview = service.PreviewImportFolder(testProject, importFolder);
        if (preview.JpegIdentical || preview.TerrainIdentical || preview.ChangedTerrainCells <= 0)
        {
            throw new InvalidOperationException("Map pair import preview should detect both JPG and terrain changes.");
        }

        var result = service.ImportMapPair(testProject, preview);
        if (result.MapImageResult == null || result.TerrainResult == null ||
            !File.Exists(result.MapImageResult.BackupPath) ||
            !File.Exists(result.MapImageResult.ReportJsonPath) ||
            !File.Exists(result.TerrainResult.BackupPath) ||
            !File.Exists(result.TerrainResult.ReportJsonPath))
        {
            throw new InvalidOperationException("Map pair import did not produce expected backup/report evidence.");
        }

        var targetJpg = testProject.ResolveGameFile(Path.Combine("Map", "M000.jpg"));
        if (!File.Exists(targetJpg))
        {
            targetJpg = testProject.ResolveGameFile(Path.Combine("Map", "M000.JPG"));
        }

        var sourceSha = WriteOperationReportService.ComputeSha256(sourceJpg);
        var targetSha = WriteOperationReportService.ComputeSha256(targetJpg);
        if (!sourceSha.Equals(targetSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Imported JPG hash does not match target Map/M000.JPG.");
        }

        var afterProbe = reader.Read(testProject);
        var afterBlock = FindMapPairSmokeBlock(afterProbe, "M000");
        var afterCells = reader.GetBlockCells(afterProbe, afterBlock);
        AssertBytes(importCells, afterCells, "Imported Hexzmap terrain cells");
    }

    private static void AssertMapPairImportBlockers(
        MapPairImportExportService service,
        HexzmapTerrainBmpCodec codec,
        CczProject testProject,
        HexzmapBlockInfo targetBlock,
        byte[] prefix,
        string smokeRoot)
    {
        var validCells = Enumerable.Range(0, targetBlock.BytesRead).Select(index => (byte)(index % 251)).ToArray();

        var mismatchFolder = Path.Combine(smokeRoot, "Blocker_MismatchIds");
        Directory.CreateDirectory(mismatchFolder);
        SaveSolidJpeg(Path.Combine(mismatchFolder, "M000.JPG"), targetBlock.MapPixelWidth, targetBlock.MapPixelHeight, Color.DarkRed);
        codec.Encode(targetBlock, prefix, validCells, Path.Combine(mismatchFolder, "M001.BMP"));
        ExpectThrows(() => service.ListImportCandidates(mismatchFolder), "same Mxxx id");

        var badJpgSizeFolder = Path.Combine(smokeRoot, "Blocker_BadJpgSize");
        Directory.CreateDirectory(badJpgSizeFolder);
        SaveSolidJpeg(Path.Combine(badJpgSizeFolder, "M000.JPG"), 49, 48, Color.DarkGreen);
        codec.Encode(targetBlock, prefix, validCells, Path.Combine(badJpgSizeFolder, "M000.BMP"));
        ExpectThrows(() => service.PreviewImportFolder(testProject, badJpgSizeFolder), "divisible by 48");

        var bmpMismatchFolder = Path.Combine(smokeRoot, "Blocker_BmpGridMismatch");
        Directory.CreateDirectory(bmpMismatchFolder);
        SaveSolidJpeg(Path.Combine(bmpMismatchFolder, "M000.JPG"), targetBlock.MapPixelWidth, targetBlock.MapPixelHeight, Color.DarkBlue);
        var mismatchedBlock = CreateSyntheticBlock("M000", 19, 20);
        codec.Encode(mismatchedBlock, prefix, new byte[19 * 20], Path.Combine(bmpMismatchFolder, "M000.BMP"));
        ExpectThrows(() => service.PreviewImportFolder(testProject, bmpMismatchFolder), "does not match JPG-derived grid");

        var corruptBmpFolder = Path.Combine(smokeRoot, "Blocker_CorruptBmpLength");
        Directory.CreateDirectory(corruptBmpFolder);
        SaveSolidJpeg(Path.Combine(corruptBmpFolder, "M000.JPG"), targetBlock.MapPixelWidth, targetBlock.MapPixelHeight, Color.DarkCyan);
        var corruptBmp = Path.Combine(corruptBmpFolder, "M000.BMP");
        codec.Encode(targetBlock, prefix, validCells, corruptBmp);
        var corruptBytes = File.ReadAllBytes(corruptBmp);
        Array.Resize(ref corruptBytes, corruptBytes.Length - 1);
        File.WriteAllBytes(corruptBmp, corruptBytes);
        ExpectThrows(() => service.PreviewImportFolder(testProject, corruptBmpFolder), "file length");

        var targetSizeMismatchFolder = Path.Combine(smokeRoot, "Blocker_TargetMapSizeMismatch");
        Directory.CreateDirectory(targetSizeMismatchFolder);
        SaveSolidJpeg(Path.Combine(targetSizeMismatchFolder, "M000.JPG"), 48, 48, Color.DarkMagenta);
        var oneCellBlock = CreateSyntheticBlock("M000", 1, 1);
        codec.Encode(oneCellBlock, prefix, new byte[] { 7 }, Path.Combine(targetSizeMismatchFolder, "M000.BMP"));
        ExpectThrows(() => service.PreviewImportFolder(testProject, targetSizeMismatchFolder), "does not match import JPG");

        var noHexzmapFolder = Path.Combine(smokeRoot, "Blocker_NoHexzmapBlock");
        Directory.CreateDirectory(noHexzmapFolder);
        var extraMapPath = testProject.ResolveGameFile(Path.Combine("Map", "M999.jpg"));
        SaveSolidJpeg(extraMapPath, targetBlock.MapPixelWidth, targetBlock.MapPixelHeight, Color.DarkOrange);
        SaveSolidJpeg(Path.Combine(noHexzmapFolder, "M999.JPG"), targetBlock.MapPixelWidth, targetBlock.MapPixelHeight, Color.DarkOrange);
        var extraBlock = CreateSyntheticBlock("M999", targetBlock.Width, targetBlock.Height);
        codec.Encode(extraBlock, prefix, validCells, Path.Combine(noHexzmapFolder, "M999.BMP"));
        ExpectThrows(() => service.PreviewImportFolder(testProject, noHexzmapFolder), "Hexzmap block");
    }

    private static void AssertMapPairButtonsUi(CczProject project)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using (var offlineForm = new MainForm())
                {
                    InvokePrivateForMapWorkbenchUiSmoke(offlineForm, "UpdateMapMakerEditingButtons");
                    var offlineExport = GetPrivateFieldForMapWorkbenchUiSmoke<Button>(offlineForm, "_mapMakerExportPairButton");
                    var offlineImport = GetPrivateFieldForMapWorkbenchUiSmoke<Button>(offlineForm, "_mapMakerImportPairButton");
                    if (offlineExport.Enabled || offlineImport.Enabled)
                    {
                        throw new InvalidOperationException("Map pair buttons should be disabled in standalone/offline mode.");
                    }

                    if (!(offlineImport.AccessibleDescription ?? string.Empty).Contains("需要打开 MOD 项目", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Offline import button should expose the disabled reason.");
                    }
                }

                using var form = new MainForm();
                SetPrivateFieldForMapWorkbenchUiSmoke(form, "_project", project);
                InvokePrivateForMapWorkbenchUiSmoke(form, "LoadMapWorkbenchSettings");
                InvokePrivateForMapWorkbenchUiSmoke(form, "LoadMapImages");
                var exportButton = GetPrivateFieldForMapWorkbenchUiSmoke<Button>(form, "_mapMakerExportPairButton");
                var importButton = GetPrivateFieldForMapWorkbenchUiSmoke<Button>(form, "_mapMakerImportPairButton");
                if (!exportButton.Text.Contains("一键导出地图", StringComparison.Ordinal) ||
                    !importButton.Text.Contains("一键导入地图", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Map pair buttons were not added with the expected labels.");
                }

                if (!exportButton.Enabled || !importButton.Enabled)
                {
                    throw new InvalidOperationException($"Map pair buttons should be enabled for compatible project map. export={exportButton.Enabled}:{exportButton.AccessibleDescription}, import={importButton.Enabled}:{importButton.AccessibleDescription}");
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw new InvalidOperationException("Map pair UI smoke failed.", failure);
        }
    }

    private static string CreateMapPairSmokeProjectRoot(CczProject project, MapResourceItem mapItem)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "MapPairImportExportSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);
        File.Copy(project.ResolveGameFile("Hexzmap.e5"), Path.Combine(smokeRoot, "Hexzmap.e5"), overwrite: false);
        var mapRoot = Path.Combine(smokeRoot, "Map");
        Directory.CreateDirectory(mapRoot);
        File.Copy(mapItem.Path, Path.Combine(mapRoot, Path.GetFileName(mapItem.Path)), overwrite: false);
        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=Map pair import/export smoke\r\n");
        return smokeRoot;
    }

    private static MapResourceItem FindMapPairSmokeMap(CczProject project, string mapId)
    {
        var indexer = new MapResourceIndexer();
        var map = indexer.Index(project)
            .FirstOrDefault(item => item.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetFileNameWithoutExtension(item.Name).Equals(mapId, StringComparison.OrdinalIgnoreCase));
        return map ?? throw new InvalidOperationException("Map pair smoke could not find " + mapId + " in " + project.GameRoot);
    }

    private static HexzmapBlockInfo FindMapPairSmokeBlock(HexzmapProbeResult probe, string mapId)
        => probe.Blocks.FirstOrDefault(block => block.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException("Map pair smoke could not find Hexzmap block " + mapId);

    private static HexzmapBlockInfo CreateSyntheticBlock(string mapId, int width, int height)
        => new()
        {
            Index = int.TryParse(mapId.TrimStart('M', 'm'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : 0,
            MapId = mapId,
            DataPrefixLength = HexzmapTerrainBmpCodec.PrefixSize,
            DecodedLength = HexzmapTerrainBmpCodec.PrefixSize + width * height,
            SegmentLength = HexzmapTerrainBmpCodec.PrefixSize + width * height,
            CanEdit = true,
            Width = width,
            Height = height,
            BytesRead = width * height,
            MapPixelWidth = width * MapResourceItem.MapTilePixelSize,
            MapPixelHeight = height * MapResourceItem.MapTilePixelSize
        };

    private static void SaveSolidJpeg(string path, int width, int height, Color color)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(color);
        }

        bitmap.Save(path, ImageFormat.Jpeg);
    }

    private static void ExpectThrows(Action action, string expectedMessagePart)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains(expectedMessagePart, StringComparison.OrdinalIgnoreCase) ||
                ex.InnerException?.Message.Contains(expectedMessagePart, StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            throw new InvalidOperationException($"Expected exception containing '{expectedMessagePart}', actual='{ex.Message}'.", ex);
        }

        throw new InvalidOperationException("Expected exception containing: " + expectedMessagePart);
    }

    private static void AssertBytes(byte[] expected, byte[] actual, string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"{label} mismatch. expectedLength={expected.Length}, actualLength={actual.Length}.");
        }
    }

    private static void AssertMapPairEqual<T>(T actual, T expected, string label)
        where T : IEquatable<T>
    {
        if (!actual.Equals(expected))
        {
            throw new InvalidOperationException($"{label} mismatch. expected={expected}, actual={actual}.");
        }
    }
}
