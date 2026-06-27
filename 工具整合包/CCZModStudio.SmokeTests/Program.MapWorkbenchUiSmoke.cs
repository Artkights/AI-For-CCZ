using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunMapWorkbenchUiSmoke(CczProject project)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var form = new MainForm();
                SetPrivateFieldForMapWorkbenchUiSmoke(form, "_project", project);

                InvokePrivateForMapWorkbenchUiSmoke(form, "LoadMapWorkbenchSettings");
                InvokePrivateForMapWorkbenchUiSmoke(form, "LoadMapImages");

                var list = GetPrivateFieldForMapWorkbenchUiSmoke<ListBox>(form, "_mapImageList");
                var draft = GetPrivateFieldForMapWorkbenchUiSmoke<MapWorkbenchDraft?>(form, "_currentMapWorkbenchDraft");
                var rendered = GetPrivateFieldForMapWorkbenchUiSmoke<Bitmap?>(form, "_mapViewerRenderedImage");
                var previewBox = GetPrivateFieldForMapWorkbenchUiSmoke<PictureBox>(form, "_mapViewerBox");
                var status = GetPrivateFieldForMapWorkbenchUiSmoke<ToolStripStatusLabel>(form, "_statusLabel");
                var showTerrain = GetPrivateFieldForMapWorkbenchUiSmoke<CheckBox>(form, "_mapMakerShowTerrainCheckBox");
                var beautifyButton = GetPrivateFieldForMapWorkbenchUiSmoke<Button>(form, "_mapMakerBeautifyCheckBox");
                var beautifyFilterCombo = GetPrivateFieldForMapWorkbenchUiSmoke<ComboBox>(form, "_mapMakerBeautifyFilterCombo");
                var rollbackButton = GetPrivateFieldForMapWorkbenchUiSmoke<Button>(form, "_mapMakerRollbackBeautifyButton");
                var materialPlanButton = GetPrivateFieldForMapWorkbenchUiSmoke<Button>(form, "_mapMakerMaterialPlanButton");
                var extractMaterialButton = GetPrivateFieldForMapWorkbenchUiSmoke<Button>(form, "_mapMakerExtractMaterialButton");
                var materialTree = GetPrivateFieldForMapWorkbenchUiSmoke<TreeView>(form, "_mapMakerMaterialTree");
                var materialList = GetPrivateFieldForMapWorkbenchUiSmoke<ListView>(form, "_mapMakerMaterialListView");
                var materials = GetPrivateFieldForMapWorkbenchUiSmoke<IReadOnlyList<MaterialAsset>>(form, "_currentMaterialAssets");

                if (list.Items.Count == 0)
                {
                    throw new InvalidOperationException("Map list did not load any map images.");
                }

                if (draft == null)
                {
                    throw new InvalidOperationException("Selecting the first map did not create a map workbench draft. Status=" + status.Text);
                }

                if (list.SelectedItem is not MapResourceItem selectedMap || string.IsNullOrWhiteSpace(selectedMap.Path) || !File.Exists(selectedMap.Path))
                {
                    throw new InvalidOperationException("Map workbench did not keep a real selected map image path.");
                }

                if (!draft.BaseLayerPath.Equals(Path.GetFullPath(selectedMap.Path), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Map workbench draft should bind the real Map image. expected={selectedMap.Path} actual={draft.BaseLayerPath}");
                }

                if (rendered == null || previewBox.Image == null || rendered.Width <= 0 || rendered.Height <= 0)
                {
                    throw new InvalidOperationException("Map workbench preview did not render an image. Status=" + status.Text);
                }

                if (showTerrain.Visible || showTerrain.Checked)
                {
                    throw new InvalidOperationException("Map workbench should hide the terrain color layer toggle.");
                }

                if (materialPlanButton.Visible)
                {
                    throw new InvalidOperationException("Main material settings should be hidden in material-paint workflow.");
                }

                if (!beautifyButton.Enabled || !beautifyButton.Text.Contains("美化", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Beautify should be exposed as an enabled button.");
                }

                if (!extractMaterialButton.Text.Contains("提取", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Map workbench should expose the material extraction entry.");
                }

                AssertMapWorkbenchBeautifyFilterCombo(beautifyFilterCombo);
                SelectMapWorkbenchBeautifyFilter(beautifyFilterCombo, TerrainBeautifyFilterProfiles.Autumn);
                if (!draft.BeautifyFilterProfile.Equals(TerrainBeautifyFilterProfiles.Autumn, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Beautify filter selection should update the current draft.");
                }

                SelectMapWorkbenchBeautifyFilter(beautifyFilterCombo, TerrainBeautifyFilterProfiles.Natural);

                if (rollbackButton.Enabled)
                {
                    throw new InvalidOperationException("Rollback beautify should be disabled until a beautified preview exists.");
                }

                var terrainMaterialCount = materials.Count(asset => asset.AssetType.Equals(MaterialAssetTypes.Terrain, StringComparison.OrdinalIgnoreCase));
                var buildingMaterialCount = materials.Count(asset => asset.AssetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase));
                var sceneryMaterialCount = materials.Count(asset => asset.AssetType.Equals(MaterialAssetTypes.Scenery, StringComparison.OrdinalIgnoreCase));
                if (terrainMaterialCount == 0 || buildingMaterialCount == 0 || sceneryMaterialCount == 0)
                {
                    throw new InvalidOperationException($"Material browser should index all three migrated material classes. terrain={terrainMaterialCount} building={buildingMaterialCount} scenery={sceneryMaterialCount}");
                }

                AssertWorkbenchMaterialLibraryUsesStandardFolders(materials, MaterialAssetTypes.Terrain, 1, "草原", minimumImages: 2);
                AssertWorkbenchMaterialLibraryUsesStandardFolders(materials, MaterialAssetTypes.Terrain, 2, "树林", minimumImages: 2);
                AssertWorkbenchMaterialLibraryUsesStandardFolders(materials, MaterialAssetTypes.Building, 14, "栅栏", minimumImages: 1);
                AssertAutoTileMaskMetadata(materials, "栅栏", "5.png");
                AssertAutoTileMaskMetadata(materials, "城墙", "1.png");
                AssertAutoTileMaskMetadata(materials, "水池", "1.png");
                AssertSingleFrameAutoTileMetadata(materials, "民居", "6.png");

                if (materialTree.Nodes.Count != 3 || !materialTree.Nodes.Cast<TreeNode>().Any(node => node.Text.Contains("地形", StringComparison.Ordinal)) ||
                    !materialTree.Nodes.Cast<TreeNode>().Any(node => node.Text.Contains("建筑", StringComparison.Ordinal)) ||
                    !materialTree.Nodes.Cast<TreeNode>().Any(node => node.Text.Contains("景物", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("Material browser should expose Terrain/Building/Scenery root nodes.");
                }

                if (materialList.Items.Count == 0 || materialList.LargeImageList == null || materialList.LargeImageList.Images.Count == 0)
                {
                    throw new InvalidOperationException("Material browser did not populate selectable material thumbnails.");
                }

                AssertMapWorkbenchAutoTileBrowserGroup(form, materialTree, materialList, "15：城墙", expectedItems: 3);
                AssertMapWorkbenchAutoTileBrowserGroup(form, materialTree, materialList, "23：民居", expectedItems: 7);
                AssertMapWorkbenchAutoTileBrowserGroup(form, materialTree, materialList, "25：水池", expectedItems: 1);

                var terrainBeforeExtraction = draft.TerrainCells.ToArray();
                var terrainBaseBeforeExtraction = draft.TerrainBaseCells.Count;
                var buildingBeforeExtraction = draft.BuildingOverlayCells.Count;
                var sceneryBeforeExtraction = draft.SceneryOverlays.Count;
                var mapUndoBeforeExtraction = GetPrivateStackCountForMapWorkbenchUiSmoke(form, "_mapMakerMapUndoStack");
                var terrainUndoBeforeExtraction = GetPrivateStackCountForMapWorkbenchUiSmoke(form, "_mapMakerTerrainUndoStack");
                var materialCountBeforeExtraction = materials.Count;
                SetPrivateFieldForMapWorkbenchUiSmoke(form, "_mapMakerSelectedCellRange", new Rectangle(0, 0, 1, 1));
                InvokePrivateForMapWorkbenchUiSmoke(form, "UpdateMapMakerEditingButtons");
                if (!extractMaterialButton.Enabled)
                {
                    throw new InvalidOperationException("Material extraction button should enable when a map cell range is selected.");
                }

                var extraction = InvokePrivateForMapWorkbenchUiSmokeResult(
                    form,
                    "ExtractMapMaterialSelectionDirect",
                    MapMaterialExtractionTargetType.Terrain,
                    (byte)12) as MapMaterialExtractionResult;
                if (extraction == null || extraction.Files.Count != 1 || !File.Exists(extraction.Files[0].Path))
                {
                    throw new InvalidOperationException("Material extraction did not write the expected UI smoke file.");
                }

                materials = GetPrivateFieldForMapWorkbenchUiSmoke<IReadOnlyList<MaterialAsset>>(form, "_currentMaterialAssets");
                if (materials.Count <= materialCountBeforeExtraction)
                {
                    throw new InvalidOperationException("Material extraction should refresh indexed material assets.");
                }

                if (!draft.TerrainCells.SequenceEqual(terrainBeforeExtraction) ||
                    draft.TerrainBaseCells.Count != terrainBaseBeforeExtraction ||
                    draft.BuildingOverlayCells.Count != buildingBeforeExtraction ||
                    draft.SceneryOverlays.Count != sceneryBeforeExtraction)
                {
                    throw new InvalidOperationException("Material extraction should not mutate the current map draft layers.");
                }

                if (GetPrivateStackCountForMapWorkbenchUiSmoke(form, "_mapMakerMapUndoStack") != mapUndoBeforeExtraction ||
                    GetPrivateStackCountForMapWorkbenchUiSmoke(form, "_mapMakerTerrainUndoStack") != terrainUndoBeforeExtraction)
                {
                    throw new InvalidOperationException("Material extraction should not enter the map paint undo stacks.");
                }

                InvokePrivateForMapWorkbenchUiSmoke(form, "RenderMapMakerPreview", true);
                rendered = GetPrivateFieldForMapWorkbenchUiSmoke<Bitmap?>(form, "_mapViewerRenderedImage");
                if (rendered == null)
                {
                    throw new InvalidOperationException("Map workbench preview disappeared after rerender.");
                }

                var visiblePixels = CountVisiblePixelsForMapWorkbenchUiSmoke(rendered);
                if (visiblePixels <= 0)
                {
                    throw new InvalidOperationException("Map workbench preview rendered a blank image.");
                }

                var terrainColorLikePixels = CountTerrainColorLikePixelsForMapWorkbenchUiSmoke(rendered, draft);
                if (terrainColorLikePixels > visiblePixels * 9 / 10)
                {
                    throw new InvalidOperationException("Generated map preview still looks like the terrain color layer.");
                }

                var realMapLikePixels = CountRealMapLikePixelsForMapWorkbenchUiSmoke(rendered, selectedMap.Path);
                if (realMapLikePixels < visiblePixels / 2)
                {
                    throw new InvalidOperationException($"Map workbench preview should show the real Map image, not a checkerboard draft. matched={realMapLikePixels} visible={visiblePixels} map={selectedMap.Path}");
                }

                Console.WriteLine($"MAP_WORKBENCH_UI_SMOKE_OK maps={list.Items.Count} draft={draft.GridWidth}x{draft.GridHeight} preview={rendered.Width}x{rendered.Height} visiblePixels={visiblePixels} realMapPixels={realMapLikePixels} materials={materials.Count} terrain={terrainMaterialCount} building={buildingMaterialCount} scenery={sceneryMaterialCount} browser={materialTree.Nodes.Count}/{materialList.Items.Count}");
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
            throw new InvalidOperationException("Map workbench UI smoke failed.", failure);
        }
    }

    private static int CountVisiblePixelsForMapWorkbenchUiSmoke(Bitmap bitmap)
    {
        var count = 0;
        var stepX = Math.Max(1, bitmap.Width / 80);
        var stepY = Math.Max(1, bitmap.Height / 80);
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A > 0 && (color.R > 8 || color.G > 8 || color.B > 8))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountTerrainColorLikePixelsForMapWorkbenchUiSmoke(Bitmap bitmap, MapWorkbenchDraft draft)
    {
        if (draft.TerrainCells.Length != draft.CellCount) return bitmap.Width * bitmap.Height;

        var count = 0;
        var stepX = Math.Max(1, bitmap.Width / 80);
        var stepY = Math.Max(1, bitmap.Height / 80);
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var cellX = Math.Clamp(x / Math.Max(1, draft.TileSize), 0, draft.GridWidth - 1);
                var cellY = Math.Clamp(y / Math.Max(1, draft.TileSize), 0, draft.GridHeight - 1);
                var terrainId = draft.TerrainCells[cellY * draft.GridWidth + cellX];
                var terrainColor = HexzmapTerrainRenderService.GetTerrainColor(terrainId);
                var color = bitmap.GetPixel(x, y);
                if (Math.Abs(color.R - terrainColor.R) <= 3 &&
                    Math.Abs(color.G - terrainColor.G) <= 3 &&
                    Math.Abs(color.B - terrainColor.B) <= 3)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountRealMapLikePixelsForMapWorkbenchUiSmoke(Bitmap rendered, string mapPath)
    {
        using var map = new Bitmap(mapPath);
        var count = 0;
        var stepX = Math.Max(1, rendered.Width / 80);
        var stepY = Math.Max(1, rendered.Height / 80);
        for (var y = 0; y < rendered.Height; y += stepY)
        {
            for (var x = 0; x < rendered.Width; x += stepX)
            {
                if (x >= map.Width || y >= map.Height) continue;
                var renderedColor = rendered.GetPixel(x, y);
                var mapColor = map.GetPixel(x, y);
                if (Math.Abs(renderedColor.R - mapColor.R) <= 8 &&
                    Math.Abs(renderedColor.G - mapColor.G) <= 8 &&
                    Math.Abs(renderedColor.B - mapColor.B) <= 8)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void AssertMapWorkbenchAutoTileBrowserGroup(
        MainForm form,
        TreeView materialTree,
        ListView materialList,
        string groupText,
        int expectedItems)
    {
        var node = materialTree.Nodes
            .Cast<TreeNode>()
            .SelectMany(root => root.Nodes.Cast<TreeNode>())
            .FirstOrDefault(candidate => candidate.Text.Contains(groupText, StringComparison.Ordinal));
        if (node == null)
        {
            throw new InvalidOperationException("Material browser did not expose group " + groupText);
        }

        materialTree.SelectedNode = node;
        InvokePrivateForMapWorkbenchUiSmoke(form, "PopulateMapWorkbenchMaterialListForSelection");
        if (materialList.Items.Count < expectedItems)
        {
            throw new InvalidOperationException($"Material browser should show selectable image sets for {groupText}. expectedAtLeast={expectedItems} actual={materialList.Items.Count}");
        }

        foreach (ListViewItem item in materialList.Items)
        {
            if (item.Text.Contains("straight", StringComparison.OrdinalIgnoreCase) ||
                item.Text.Contains("corner", StringComparison.OrdinalIgnoreCase) ||
                item.Text.Contains("tee", StringComparison.OrdinalIgnoreCase) ||
                item.Text.Contains("cross", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Material browser should not expose auto-tile roles as separate selectable items: " + item.Text);
            }
        }
    }

    private static void AssertMapWorkbenchBeautifyFilterCombo(ComboBox combo)
    {
        if (combo.DropDownStyle != ComboBoxStyle.DropDownList || combo.Items.Count != 6)
        {
            throw new InvalidOperationException("Beautify filter combo should expose the six selectable filter profiles.");
        }

        foreach (var expected in new[]
                 {
                     TerrainBeautifyFilterProfiles.Natural,
                     TerrainBeautifyFilterProfiles.Night,
                     TerrainBeautifyFilterProfiles.Autumn,
                     TerrainBeautifyFilterProfiles.Winter,
                     TerrainBeautifyFilterProfiles.WarmSun,
                     TerrainBeautifyFilterProfiles.Custom
                 })
        {
            if (!combo.Items.Cast<object>().Any(item => GetMapWorkbenchBeautifyFilterProfile(item).Equals(expected, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Beautify filter combo missing profile " + expected);
            }
        }
    }

    private static void SelectMapWorkbenchBeautifyFilter(ComboBox combo, string profile)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            var item = combo.Items[i];
            if (item != null && GetMapWorkbenchBeautifyFilterProfile(item).Equals(profile, StringComparison.Ordinal))
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        throw new InvalidOperationException("Beautify filter combo missing profile " + profile);
    }

    private static string GetMapWorkbenchBeautifyFilterProfile(object item)
        => item.GetType().GetProperty("Profile")?.GetValue(item) as string ?? string.Empty;

    private static void AssertWorkbenchMaterialLibraryUsesStandardFolders(
        IReadOnlyList<MaterialAsset> materials,
        string assetType,
        byte terrainId,
        string terrainName,
        int minimumImages)
    {
        var matching = materials
            .Where(asset => asset.AssetType.Equals(assetType, StringComparison.OrdinalIgnoreCase) && asset.TerrainId == terrainId)
            .ToList();
        if (matching.Count < minimumImages)
        {
            throw new InvalidOperationException($"{assetType} material id {terrainId} should have at least {minimumImages} indexed images after folder merge, actual={matching.Count}.");
        }

        if (matching.Any(asset => !asset.TerrainName.Equals(terrainName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"{assetType} material id {terrainId} should display the standard name {terrainName}.");
        }

        var folders = matching
            .Select(asset => Path.GetFileName(Path.GetDirectoryName(asset.FilePath) ?? string.Empty))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var expectedFolder = $"{terrainId}：{terrainName}";
        if (folders.Count != 1 || !folders[0].Equals(expectedFolder, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{assetType} material id {terrainId} should live in one standard folder {expectedFolder}, actual={string.Join(", ", folders)}.");
        }
    }

    private static void AssertAutoTileMaskMetadata(IReadOnlyList<MaterialAsset> materials, string terrainName, string fileName)
    {
        var assets = materials
            .Where(asset => asset.AssetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase))
            .Where(asset => asset.TerrainName.Equals(terrainName, StringComparison.Ordinal))
            .Where(asset => asset.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(asset => asset.AutoTilePriority)
            .ThenBy(asset => asset.SourceX)
            .ToList();
        if (assets.Count == 0)
        {
            throw new InvalidOperationException($"Auto-tile metadata missing for {terrainName}/{fileName}.");
        }

        if (assets.Any(asset => !asset.AutoTileMask.HasValue))
        {
            throw new InvalidOperationException($"Auto-tile metadata should use mask values for {terrainName}/{fileName}.");
        }

        if (terrainName == "栅栏" && fileName.Equals("5.png", StringComparison.OrdinalIgnoreCase))
        {
            if (assets[0].AutoTileMask != (MaterialAutoTileMasks.East | MaterialAutoTileMasks.West) ||
                assets[1].AutoTileMask != (MaterialAutoTileMasks.North | MaterialAutoTileMasks.South))
            {
                throw new InvalidOperationException("Fence sheet metadata should map first frame to horizontal and second frame to vertical.");
            }
        }
    }

    private static void AssertSingleFrameAutoTileMetadata(IReadOnlyList<MaterialAsset> materials, string terrainName, string fileName)
    {
        var assets = materials
            .Where(asset => asset.AssetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase))
            .Where(asset => asset.TerrainName.Equals(terrainName, StringComparison.Ordinal))
            .Where(asset => asset.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (assets.Count != 1 || assets[0].AutoTileMask != MaterialAutoTileMasks.None)
        {
            throw new InvalidOperationException($"Complex atlas {terrainName}/{fileName} should stay as one default selectable image until hand-authored masks exist.");
        }
    }

    private static T GetPrivateFieldForMapWorkbenchUiSmoke<T>(MainForm form, string fieldName)
    {
        var field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Field not found: " + fieldName);
        return (T)field.GetValue(form)!;
    }

    private static void SetPrivateFieldForMapWorkbenchUiSmoke<T>(MainForm form, string fieldName, T value)
    {
        var field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Field not found: " + fieldName);
        field.SetValue(form, value);
    }

    private static void InvokePrivateForMapWorkbenchUiSmoke(MainForm form, string methodName, params object?[] args)
    {
        var method = typeof(MainForm).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Method not found: " + methodName);
        method.Invoke(form, args);
    }

    private static object? InvokePrivateForMapWorkbenchUiSmokeResult(MainForm form, string methodName, params object?[] args)
    {
        var method = typeof(MainForm).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Method not found: " + methodName);
        return method.Invoke(form, args);
    }

    private static int GetPrivateStackCountForMapWorkbenchUiSmoke(MainForm form, string fieldName)
    {
        var value = GetPrivateFieldForMapWorkbenchUiSmoke<object>(form, fieldName);
        return value.GetType().GetProperty("Count")?.GetValue(value) is int count ? count : 0;
    }
}
