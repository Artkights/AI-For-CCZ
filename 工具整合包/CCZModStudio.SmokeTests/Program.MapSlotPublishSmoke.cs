using System.Globalization;
using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

partial class Program
{
    static void RunMapSlotPublishSmoke(CczProject sourceProject)
    {
        var root = CreateMapSlotPublishSmokeRoot(sourceProject);
        var project = new ProjectDetector().CreateProjectFromGameRoot(root);
        AssertCatalogStates(project);
        AssertLayoutRebuilds(project);
        AssertDraftResizeMigration();
        AssertMapPublishDialogEmptySlots();
        AssertMapPublishDialogOverwriteSlot();
        AssertMapPublishDialogLayout();
        AssertTransactionalPublishing(project);
        Console.WriteLine("MAP_SLOT_PUBLISH_SMOKE_OK root=" + root);
    }

    private static void AssertMapPublishDialogEmptySlots()
    {
        var draft = BuildPatternDraft("dialog-empty-slots", 4, 3, string.Empty);
        using var dialog = new MapPublishDialog(
            draft,
            Array.Empty<MapSlotCatalogEntry>(),
            preferredMapId: string.Empty,
            nextMapNumber: 0,
            directoryEntryCount: 0,
            defaultMode: MapSlotPublishMode.AppendNew);
        AssertMapSlot(dialog.SelectedMode == MapSlotPublishMode.AppendNew, "empty-slot publish dialog must default to append mode");
        AssertMapSlot(string.IsNullOrWhiteSpace(dialog.SelectedTargetMapId), "append mode must not require an overwrite target");
    }

    private static void AssertMapPublishDialogOverwriteSlot()
    {
        var draft = BuildPatternDraft("dialog-overwrite-slot", 20, 20, "dummy.jpg");
        draft.BoundMapId = "M000";
        var slot = new MapSlotCatalogEntry
        {
            MapNumber = 0,
            MapId = "M000",
            State = MapSlotState.Complete,
            GridWidth = 20,
            GridHeight = 20,
            MapResource = new MapResourceItem
            {
                Id = "000",
                MapId = "M000",
                Name = "M000.JPG",
                Path = "dummy.jpg",
                Width = 20 * MapResourceItem.MapTilePixelSize,
                Height = 20 * MapResourceItem.MapTilePixelSize,
                GridWidthOverride = 20,
                GridHeightOverride = 20
            },
            TerrainEntry = new HexzmapDirectoryEntry
            {
                Index = 0,
                MapId = "M000",
                SegmentLength = 2 + 20 * 20,
                IsValidSegment = true
            },
            Detail = "ok"
        };

        using var dialog = new MapPublishDialog(
            draft,
            new[] { slot },
            preferredMapId: "M000",
            nextMapNumber: 1,
            directoryEntryCount: 1,
            defaultMode: MapSlotPublishMode.OverwriteExisting);
        AssertMapSlot(dialog.SelectedMode == MapSlotPublishMode.OverwriteExisting, "overwrite-slot publish dialog must default to overwrite mode");
        AssertMapSlot(dialog.SelectedTargetMapId == "M000", "overwrite-slot publish dialog must select the preferred target");
    }

    private static void AssertMapPublishDialogLayout()
    {
        var draft = BuildPatternDraft("dialog-layout", 20, 20, "dummy.jpg");
        draft.BoundMapId = "M002";
        var completeSlot = BuildDialogSlot("M002", MapSlotState.Complete, "ok", hasMap: true, hasTerrain: true);
        var missingMap = BuildDialogSlot(
            "M044",
            MapSlotState.MissingMapImage,
            "存在 Hexzmap 地形块，但 Map 目录没有同编号底图；这是一个用于验证长异常说明不会被单行截断的布局测试。",
            hasMap: false,
            hasTerrain: true);
        var missingTerrain = BuildDialogSlot(
            "M100",
            MapSlotState.MissingTerrainBlock,
            "存在底图，但缺少 Hexzmap 地形块；该槽不能作为普通覆盖发布目标。",
            hasMap: true,
            hasTerrain: false);

        using var dialog = new MapPublishDialog(
            draft,
            new[] { completeSlot, missingMap, missingTerrain },
            preferredMapId: "M002",
            nextMapNumber: 59,
            directoryEntryCount: 59,
            defaultMode: MapSlotPublishMode.OverwriteExisting);
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(-2000, -2000);
        dialog.CreateControl();
        dialog.Show();
        dialog.PerformLayout();

        AssertMapSlot(dialog.ClientSize.Width >= 760 && dialog.ClientSize.Height >= 620, "publish dialog client size must be enlarged");
        AssertMapSlot(dialog.MinimumSize.Width >= 760 && dialog.MinimumSize.Height >= 620, "publish dialog minimum size must be enforced");
        AssertMapSlot(dialog.FormBorderStyle == FormBorderStyle.Sizable, "publish dialog must be resizable");
        AssertMapSlot(dialog.AutoScroll, "publish dialog must support autoscroll at high DPI");

        var summary = FindDialogControl<TextBox>(dialog, "mapPublishSummaryBox");
        AssertMapSlot(summary.Multiline && summary.ScrollBars == ScrollBars.Vertical, "summary must be a multiline scrollable text box");
        AssertMapSlot(summary.MinimumSize.Height >= 220, "summary must reserve enough height");
        AssertMapSlot(!string.IsNullOrWhiteSpace(summary.Text) && summary.Text.Contains("目标：M002", StringComparison.Ordinal), "summary must show selected target");

        var validation = FindDialogControl<TextBox>(dialog, "mapPublishValidationBox");
        AssertMapSlot(validation.Visible, "validation box must be visible when abnormal slots exist");
        AssertMapSlot(validation.Multiline && validation.ScrollBars == ScrollBars.Vertical, "validation box must be multiline and scrollable");
        AssertMapSlot(validation.Text.Contains("M044", StringComparison.Ordinal) && validation.Text.Contains("M100", StringComparison.Ordinal), "validation box must list all abnormal slots");

        AssertControlInsideClient(dialog, FindDialogControl<ComboBox>(dialog, "mapPublishTargetCombo"), "target combo");
        AssertControlInsideClient(dialog, summary, "summary box");
        AssertControlInsideClient(dialog, validation, "validation box");
        AssertControlInsideClient(dialog, FindDialogControl<Button>(dialog, "mapPublishButton"), "publish button");
        AssertControlInsideClient(dialog, FindDialogControl<Button>(dialog, "mapPublishCancelButton"), "cancel button");
        dialog.Close();
    }

    private static MapSlotCatalogEntry BuildDialogSlot(string mapId, MapSlotState state, string detail, bool hasMap, bool hasTerrain)
    {
        var mapNumber = int.Parse(mapId[1..], CultureInfo.InvariantCulture);
        return new MapSlotCatalogEntry
        {
            MapNumber = mapNumber,
            MapId = mapId,
            State = state,
            GridWidth = 20,
            GridHeight = 20,
            MapResource = hasMap
                ? new MapResourceItem
                {
                    Id = mapNumber.ToString("000", CultureInfo.InvariantCulture),
                    MapId = mapId,
                    Name = mapId + ".JPG",
                    Path = "dummy.jpg",
                    Width = 20 * MapResourceItem.MapTilePixelSize,
                    Height = 20 * MapResourceItem.MapTilePixelSize,
                    GridWidthOverride = 20,
                    GridHeightOverride = 20
                }
                : null,
            TerrainEntry = hasTerrain
                ? new HexzmapDirectoryEntry
                {
                    Index = mapNumber,
                    MapId = mapId,
                    SegmentLength = 2 + 20 * 20,
                    IsValidSegment = true
                }
                : null,
            Detail = detail
        };
    }

    private static T FindDialogControl<T>(Control root, string name) where T : Control
    {
        var matches = root.Controls.Find(name, searchAllChildren: true);
        if (matches.Length == 0) throw new InvalidOperationException("Map slot publish smoke: missing control " + name);
        if (matches[0] is not T typed) throw new InvalidOperationException($"Map slot publish smoke: control {name} is {matches[0].GetType().Name}, expected {typeof(T).Name}");
        return typed;
    }

    private static void AssertControlInsideClient(Form dialog, Control control, string name)
    {
        var bounds = dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
        AssertMapSlot(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= dialog.ClientSize.Width && bounds.Bottom <= dialog.ClientSize.Height,
            $"{name} must be visible inside dialog client area: bounds={bounds}, client={dialog.ClientSize}");
    }

    private static string CreateMapSlotPublishSmokeRoot(CczProject sourceProject)
    {
        var root = Path.Combine(
            sourceProject.WorkspaceRoot,
            "CCZModStudio_TestCopies",
            "MapSlotPublishSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);
        File.Copy(sourceProject.ResolveGameFile("Hexzmap.e5"), Path.Combine(root, "Hexzmap.e5"));
        File.Copy(sourceProject.ResolveGameFile("Ekd5.exe"), Path.Combine(root, "Ekd5.exe"));
        var mapRoot = Path.Combine(root, "Map");
        Directory.CreateDirectory(mapRoot);
        CopyMapIfPresent(sourceProject, mapRoot, "M000");
        CopyMapIfPresent(sourceProject, mapRoot, "M100");
        File.WriteAllText(
            Path.Combine(root, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={sourceProject.GameRoot}\r\nPurpose=Map slot publish smoke\r\n");
        return root;
    }

    private static void CopyMapIfPresent(CczProject sourceProject, string targetRoot, string mapId)
    {
        var item = new MapResourceIndexer().Index(sourceProject)
            .FirstOrDefault(map => map.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase));
        if (item != null) File.Copy(item.Path, Path.Combine(targetRoot, Path.GetFileName(item.Path)));
    }

    private static void AssertCatalogStates(CczProject project)
    {
        var catalog = new MapSlotCatalogService().List(project);
        AssertMapSlot(catalog.Single(slot => slot.MapId == "M000").State == MapSlotState.Complete, "M000 must be complete");
        AssertMapSlot(catalog.Single(slot => slot.MapId == "M044").State == MapSlotState.MissingMapImage, "M044 must report missing image");
        var m100 = catalog.SingleOrDefault(slot => slot.MapId == "M100");
        if (m100 != null) AssertMapSlot(m100.State == MapSlotState.MissingTerrainBlock, "M100 must report missing terrain block");
        AssertMapSlot(new MapSlotCatalogService().GetNextAppendMapNumber(project) == 59, "first append id must be M059");
    }

    private static void AssertLayoutRebuilds(CczProject project)
    {
        var bytes = File.ReadAllBytes(project.ResolveGameFile("Hexzmap.e5"));
        var writer = new HexzmapLayoutWriter();
        var entries = writer.ParseDirectory(bytes);
        AssertMapSlot(entries.Count == 59, "baseline directory must contain 59 entries");
        var originalSegments = ReadMapSlotSegments(bytes, entries);
        var appendCells = Enumerable.Range(0, 12).Select(index => (byte)(index + 1)).ToArray();
        var append = writer.AppendSegment(bytes, entries, 4, 3, appendCells);
        var appendedEntries = writer.ParseDirectory(append.Bytes);
        AssertMapSlot(appendedEntries.Count == 60, "append must create entry 59");
        AssertMapSlot(append.Bytes.Length == bytes.Length + HexzmapLayoutWriter.DirectoryStride + 2 + appendCells.Length, "append file length mismatch");
        for (var i = 0; i < originalSegments.Count; i++)
        {
            AssertMapSlot(originalSegments[i].SequenceEqual(append.Bytes.AsSpan(appendedEntries[i].FileOffset, appendedEntries[i].SegmentLength).ToArray()), $"append changed old segment {i}");
        }
        AssertSegment(writer, append.Bytes, appendedEntries[59], 4, 3, appendCells);

        var growCells = Enumerable.Range(0, 24 * 22).Select(index => (byte)(index % 251)).ToArray();
        var grow = writer.ReplaceSegment(bytes, entries, 0, 24, 22, growCells);
        var growEntries = writer.ParseDirectory(grow.Bytes);
        AssertSegment(writer, grow.Bytes, growEntries[0], 24, 22, growCells);
        var growDelta = (2 + growCells.Length) - entries[0].SegmentLength;
        AssertMapSlot(growEntries[1].FileOffset == entries[1].FileOffset + growDelta, "grow did not shift following offset");
        for (var i = 1; i < originalSegments.Count; i++)
        {
            AssertMapSlot(originalSegments[i].SequenceEqual(grow.Bytes.AsSpan(growEntries[i].FileOffset, growEntries[i].SegmentLength).ToArray()), $"grow changed segment {i}");
        }

        var shrinkCells = Enumerable.Range(0, 16 * 18).Select(index => (byte)(255 - index % 251)).ToArray();
        var shrink = writer.ReplaceSegment(bytes, entries, 0, 16, 18, shrinkCells);
        var shrinkEntries = writer.ParseDirectory(shrink.Bytes);
        AssertSegment(writer, shrink.Bytes, shrinkEntries[0], 16, 18, shrinkCells);
        var shrinkDelta = (2 + shrinkCells.Length) - entries[0].SegmentLength;
        AssertMapSlot(shrinkEntries[1].FileOffset == entries[1].FileOffset + shrinkDelta, "shrink did not shift following offset");
    }

    private static void AssertDraftResizeMigration()
    {
        var service = new MapResizeService();
        var draft = BuildPatternDraft("resize-grow", 20, 20, string.Empty);
        draft.GeneratedMapCells.Add(BuildCell(19 + 19 * 20, "generated"));
        draft.MapCellOverrides.Add(BuildCell(19 + 19 * 20, "manual"));
        draft.SceneryOverlays.Add(new MapSceneryOverlay { OverlayId = "inside", X = 10, Y = 10, Width = 48, Height = 48 });
        draft.SceneryOverlays.Add(new MapSceneryOverlay { OverlayId = "outside", X = 2000, Y = 2000, Width = 48, Height = 48 });
        var old = draft.TerrainCells.ToArray();
        service.Apply(draft, new MapResizeRequest { OldWidth = 20, OldHeight = 20, NewWidth = 24, NewHeight = 22, TerrainFillId = 0 });
        AssertMapSlot(draft.GridWidth == 24 && draft.GridHeight == 22, "grow dimensions mismatch");
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 20; x++) AssertMapSlot(draft.TerrainCells[y * 24 + x] == old[y * 20 + x], "grow overlap changed");
        }
        AssertMapSlot(draft.TerrainCells[23] == 0 && draft.TerrainCells[21 * 24 + 23] == 0, "grow fill must be 0x00");
        AssertMapSlot(draft.GeneratedMapCells.Single().Index == 19 + 19 * 24, "generated layer was not remapped");
        AssertMapSlot(draft.MapCellOverrides.Single().Index == 19 + 19 * 24, "manual layer was not remapped");
        AssertMapSlot(draft.SceneryOverlays.Count == 1 && draft.SceneryOverlays[0].OverlayId == "inside", "whole-image scenery clipping mismatch");

        var shrinkDraft = BuildPatternDraft("resize-shrink", 20, 20, string.Empty);
        shrinkDraft.GeneratedMapCells.Add(BuildCell(18 + 18 * 20, "removed"));
        shrinkDraft.BuildingOverlayCells.Add(BuildCell(15 + 17 * 20, "retained"));
        var preview = service.Preview(shrinkDraft, new MapResizeRequest { OldWidth = 20, OldHeight = 20, NewWidth = 16, NewHeight = 18 });
        AssertMapSlot(preview.RemovedCells == 112 && preview.RemovedGeneratedCells == 1, "shrink preview counts mismatch");
        service.Apply(shrinkDraft, new MapResizeRequest { OldWidth = 20, OldHeight = 20, NewWidth = 16, NewHeight = 18 });
        AssertMapSlot(shrinkDraft.GeneratedMapCells.Count == 0, "shrink retained out-of-range generated cell");
        AssertMapSlot(shrinkDraft.BuildingOverlayCells.Single().Index == 15 + 17 * 16, "shrink retained layer index mismatch");
    }

    private static void AssertTransactionalPublishing(CczProject project)
    {
        var map000 = new MapResourceIndexer().Index(project).Single(map => map.MapId == "M000");
        var service = new MapSlotPublishService();
        var overwriteDraft = BuildPatternDraft("unbound-overwrite", 20, 20, map000.Path);
        overwriteDraft.BoundMapId = string.Empty;
        var overwritePlan = service.Preview(project, overwriteDraft, Array.Empty<MaterialAsset>(), new MapSlotPublishRequest
        {
            DraftId = overwriteDraft.DraftId,
            Mode = MapSlotPublishMode.OverwriteExisting,
            TargetMapId = "M000"
        });
        var overwriteResult = service.Apply(project, overwriteDraft, overwritePlan);
        AssertMapSlot(overwriteResult.MapId == "M000" && overwriteDraft.BoundMapId == "M000", "unbound overwrite did not bind M000");

        var appendDraft = BuildPatternDraft("append-59", 4, 3, map000.Path);
        var beforeAppend = File.ReadAllBytes(project.ResolveGameFile("Hexzmap.e5"));
        var beforeEntries = new HexzmapLayoutWriter().ParseDirectory(beforeAppend);
        var oldSegments = ReadMapSlotSegments(beforeAppend, beforeEntries);
        var appendPlan = service.Preview(project, appendDraft, Array.Empty<MaterialAsset>(), new MapSlotPublishRequest
        {
            DraftId = appendDraft.DraftId,
            Mode = MapSlotPublishMode.AppendNew
        });
        AssertMapSlot(appendPlan.TargetMapId == "M059", "first transactional append must target M059");
        var appendResult = service.Apply(project, appendDraft, appendPlan);
        AssertMapSlot(File.Exists(Path.Combine(project.GameRoot, "Map", "M059.JPG")), "M059.JPG was not created");
        var afterAppend = File.ReadAllBytes(project.ResolveGameFile("Hexzmap.e5"));
        var afterEntries = new HexzmapLayoutWriter().ParseDirectory(afterAppend);
        AssertMapSlot(afterEntries.Count == 60 && appendResult.DirectoryEntryCount == 60, "transactional append did not create directory entry 59");
        for (var i = 0; i < oldSegments.Count; i++)
        {
            AssertMapSlot(oldSegments[i].SequenceEqual(afterAppend.AsSpan(afterEntries[i].FileOffset, afterEntries[i].SegmentLength).ToArray()), $"transactional append changed old segment {i}");
        }

        var secondDraft = BuildPatternDraft("append-60", 3, 2, map000.Path);
        var secondPlan = service.Preview(project, secondDraft, Array.Empty<MaterialAsset>(), new MapSlotPublishRequest { DraftId = secondDraft.DraftId, Mode = MapSlotPublishMode.AppendNew });
        AssertMapSlot(secondPlan.TargetMapId == "M060", "second append must target M060");
        service.Apply(project, secondDraft, secondPlan);

        foreach (var point in Enum.GetValues<MapSlotPublishFaultPoint>())
        {
            AssertPublishRollback(project, map000.Path, point);
        }
    }

    private static void AssertPublishRollback(CczProject project, string mapPath, MapSlotPublishFaultPoint point)
    {
        var beforeMap = WriteOperationReportService.ComputeSha256(mapPath);
        var beforeHex = WriteOperationReportService.ComputeSha256(project.ResolveGameFile("Hexzmap.e5"));
        var draft = BuildPatternDraft("rollback-" + point, 20, 20, mapPath);
        draft.TerrainCells[0] ^= 0x7F;
        var service = new MapSlotPublishService(actual =>
        {
            if (actual == point) throw new IOException("Injected publish failure at " + point);
        });
        var plan = service.Preview(project, draft, Array.Empty<MaterialAsset>(), new MapSlotPublishRequest
        {
            DraftId = draft.DraftId,
            Mode = MapSlotPublishMode.OverwriteExisting,
            TargetMapId = "M000"
        });
        try
        {
            service.Apply(project, draft, plan);
            throw new InvalidOperationException("Fault injection did not fail at " + point);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("地图发布失败", StringComparison.Ordinal))
        {
        }
        AssertMapSlot(WriteOperationReportService.ComputeSha256(mapPath) == beforeMap, $"map rollback failed at {point}");
        AssertMapSlot(WriteOperationReportService.ComputeSha256(project.ResolveGameFile("Hexzmap.e5")) == beforeHex, $"Hexzmap rollback failed at {point}");
        AssertMapSlot(string.IsNullOrWhiteSpace(draft.BoundMapId), $"draft binding rollback failed at {point}");
    }

    private static MapWorkbenchDraft BuildPatternDraft(string draftId, int width, int height, string baseLayerPath)
    {
        var cells = Enumerable.Range(0, width * height).Select(index => (byte)(index % 251)).ToArray();
        return new MapWorkbenchDraft
        {
            DraftId = draftId,
            GridWidth = width,
            GridHeight = height,
            TileSize = 48,
            BaseLayerPath = baseLayerPath,
            OriginalTerrainCells = cells.ToArray(),
            TerrainCells = cells.ToArray(),
            AutoGenerateMapFromTerrain = true
        };
    }

    private static MapCellOverride BuildCell(int index, string name)
        => new() { Index = index, DisplayName = name, MaterialRelativePath = name + ".png" };

    private static List<byte[]> ReadMapSlotSegments(byte[] bytes, IReadOnlyList<HexzmapDirectoryEntry> entries)
        => entries.Select(entry => bytes.AsSpan(entry.FileOffset, entry.SegmentLength).ToArray()).ToList();

    private static void AssertSegment(HexzmapLayoutWriter writer, byte[] bytes, HexzmapDirectoryEntry entry, int width, int height, byte[] cells)
    {
        var dimensions = writer.ReadSegmentDimensions(bytes, entry);
        AssertMapSlot(dimensions.Width == width && dimensions.Height == height, "segment dimensions mismatch");
        AssertMapSlot(entry.SegmentLength == 2 + cells.Length, "segment length mismatch");
        AssertMapSlot(bytes.AsSpan(entry.FileOffset + 2, cells.Length).SequenceEqual(cells), "segment terrain cells mismatch");
    }

    private static void AssertMapSlot(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Map slot publish smoke: " + message);
    }
}
