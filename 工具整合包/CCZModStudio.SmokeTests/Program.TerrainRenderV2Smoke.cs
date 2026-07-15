using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Threading;

internal partial class Program
{
    static void RunTerrainRenderV2Smoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_TerrainRenderV2Smoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            AssertDraftMigration(tempRoot);
            AssertStylePackLoading(tempRoot);
            AssertDeterministicFullCanvasAndObjectPreservation(tempRoot);
            Console.WriteLine("TERRAIN_RENDER_V2_SMOKE_OK migration=ok stylePack=ok fullCanvas=ok deterministic=ok seedVariation=ok immutable=ok objects=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void AssertDraftMigration(string tempRoot)
    {
        var service = new MapDraftService();
        var profile = "migration";
        var draftId = "legacy-v1";
        var store = service.GetDraftStoreRoot(tempRoot, profile);
        Directory.CreateDirectory(store);
        var json = """
                   {
                     "DraftId": "legacy-v1",
                     "GridWidth": 1,
                     "GridHeight": 1,
                     "TerrainCells": "AA==",
                     "OriginalTerrainCells": "AA==",
                     "BeautifyGeneratedMap": true,
                     "BeautifyStrength": 2,
                     "BeautifyFilterProfile": "Night"
                   }
                   """;
        File.WriteAllText(Path.Combine(store, draftId + ".json"), json, Encoding.UTF8);
        var loaded = service.LoadDraft(tempRoot, profile, draftId);
        if (loaded.SchemaVersion != 2 || loaded.TerrainRenderSettings.RedrawEnabled ||
            loaded.TerrainRenderSettings.ToneProfile != TerrainToneProfiles.Night ||
            loaded.TerrainRenderSettings.ToneAmount <= 0f)
        {
            throw new InvalidOperationException("旧草稿没有正确迁移到 v2，或迁移时错误开启了整图重绘。");
        }
    }

    private static void AssertStylePackLoading(string tempRoot)
    {
        var packRoot = Path.Combine(tempRoot, "packs", "green");
        Directory.CreateDirectory(packRoot);
        File.WriteAllText(Path.Combine(packRoot, "_stylepack.json"), """
            {
              "id": "green-pack",
              "name": "Green Pack",
              "version": "2",
              "terrains": [
                { "terrainId": 1, "surfaceKind": "NaturalArea", "textureSources": [] },
                { "terrainId": 15, "surfaceKind": "StructureTerrain", "textureSources": [] }
              ]
            }
            """);
        var packs = new TerrainStylePackRepository().Load(Path.Combine(tempRoot, "packs"));
        if (packs.Count != 1 || packs[0].Id != "green-pack" || packs[0].Terrains.Count != 2)
        {
            throw new InvalidOperationException("_stylepack.json 没有正确加载。");
        }
    }

    private static void AssertDeterministicFullCanvasAndObjectPreservation(string tempRoot)
    {
        var texturePath = Path.Combine(tempRoot, "large-texture.png");
        using (var texture = new Bitmap(192, 192, PixelFormat.Format32bppArgb))
        {
            using var graphics = Graphics.FromImage(texture);
            graphics.Clear(Color.ForestGreen);
            using var a = new SolidBrush(Color.FromArgb(35, 115, 55));
            using var b = new SolidBrush(Color.FromArgb(90, 150, 45));
            using var c = new SolidBrush(Color.FromArgb(45, 95, 130));
            graphics.FillRectangle(a, 0, 0, 96, 96);
            graphics.FillRectangle(b, 96, 0, 96, 96);
            graphics.FillRectangle(c, 0, 96, 96, 96);
            texture.Save(texturePath, ImageFormat.Png);
        }

        var basePath = Path.Combine(tempRoot, "base.png");
        using (var baseMap = new Bitmap(4 * 48, 2 * 48, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(baseMap))
        {
            graphics.Clear(Color.FromArgb(55, 130, 55));
            using var objectBrush = new SolidBrush(Color.Magenta);
            graphics.FillRectangle(objectBrush, 0, 0, 48, 48);
            baseMap.Save(basePath, ImageFormat.Png);
        }

        var material = new MaterialAsset
        {
            Category = "terrain",
            FileName = Path.GetFileName(texturePath),
            FilePath = texturePath,
            AssetType = MaterialAssetTypes.Terrain,
            TerrainId = 1,
            TerrainName = "grass",
            GroupKey = "Terrain:1:grass",
            AutoTileSetKey = "Terrain:1:grass",
            AutoTileMode = MaterialAutoTileModes.Default,
            AutoTileRole = MaterialAutoTileRoles.Default,
            AutoTileMask = MaterialAutoTileMasks.None,
            SourceWidth = 48,
            SourceHeight = 48,
            Width = 192,
            Height = 192,
            SamplingMode = MaterialSamplingMode.FullCanvasPatches,
            SampleBoundsWidth = 192,
            SampleBoundsHeight = 192,
            SafeBorder = 8,
            PreferredPatchWidth = 48,
            PreferredPatchHeight = 48
        };
        var draft = new MapWorkbenchDraft
        {
            DraftId = "render-v2",
            BoundMapId = "M000",
            GridWidth = 4,
            GridHeight = 2,
            TileSize = 48,
            BaseLayerPath = basePath,
            GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
            TerrainCells = new byte[] { 15, 1, 1, 1, 1, 1, 1, 1 },
            OriginalTerrainCells = new byte[] { 15, 1, 1, 1, 1, 1, 1, 1 },
            TerrainRenderSettings = new TerrainRenderSettings
            {
                RedrawEnabled = true,
                Seed = "fixed-seed",
                ObjectPolicy = TerrainObjectPolicy.PreserveOriginal,
                ToneProfile = TerrainToneProfiles.Neutral
            },
            TerrainVisualProfile = new TerrainVisualProfile
            {
                Seed = "fixed-seed",
                RedrawChangedCellsOnly = false,
                UseCurrentMapSamples = false,
                UseInteriorTextureSynthesis = false,
                UseRegionTextureCanvas = false,
                UseDirectionalBoundaryBlend = false,
                UseGlobalTransitionField = false,
                LocalColorTransferStrength = 0f,
                ColorAlignmentStrength = 0f,
                TextureNoiseStrength = 0f
            }
        };
        var originalCells = draft.TerrainCells.ToArray();
        var pack = new TerrainStylePackManifest
        {
            Id = "test-pack",
            Version = "1",
            Terrains =
            [
                new TerrainStylePackTerrainRule { TerrainId = 1, SurfaceKind = TerrainVisualSurfaceKind.NaturalArea },
                new TerrainStylePackTerrainRule { TerrainId = 15, SurfaceKind = TerrainVisualSurfaceKind.StructureTerrain }
            ]
        };

        using var service = new TerrainRenderService();
        using var first = service.RenderAsync(new TerrainRenderRequest
        {
            Draft = draft,
            Materials = new[] { material },
            StylePack = pack,
            Quality = TerrainRenderQuality.Final
        }).GetAwaiter().GetResult();
        using var second = service.RenderAsync(new TerrainRenderRequest
        {
            Draft = draft,
            Materials = new[] { material },
            StylePack = pack,
            Quality = TerrainRenderQuality.Final
        }).GetAwaiter().GetResult();
        AssertSameBitmap(first.Bitmap, second.Bitmap, "v2 final render same seed");
        if (first.Fingerprint != second.Fingerprint) throw new InvalidOperationException("同一内容生成了不同渲染指纹。");
        var computedFingerprint = service.ComputeFingerprint(new TerrainRenderRequest
        {
            Draft = draft,
            Materials = new[] { material },
            StylePack = pack,
            Quality = TerrainRenderQuality.Final
        });
        if (computedFingerprint != first.Fingerprint) throw new InvalidOperationException("发布前指纹计算必须与最终渲染指纹一致。");
        if (!draft.TerrainCells.SequenceEqual(originalCells)) throw new InvalidOperationException("渲染修改了真实 TerrainCells。");
        if (first.Bitmap.GetPixel(24, 24).ToArgb() != Color.Magenta.ToArgb())
        {
            throw new InvalidOperationException("中性色调下低可信结构对象格没有保留原图像素。");
        }

        draft.TerrainRenderSettings.Seed = "different-seed";
        draft.TerrainVisualProfile.Seed = "different-seed";
        var staleFingerprint = service.ComputeFingerprint(new TerrainRenderRequest
        {
            Draft = draft,
            Materials = new[] { material },
            StylePack = pack,
            Quality = TerrainRenderQuality.Final
        });
        if (staleFingerprint == first.Fingerprint) throw new InvalidOperationException("最终图输入变化后发布前指纹没有变化。");
        using var different = service.RenderAsync(new TerrainRenderRequest
        {
            Draft = draft,
            Materials = new[] { material },
            StylePack = pack,
            Quality = TerrainRenderQuality.Final
        }).GetAwaiter().GetResult();
        if (different.Fingerprint == first.Fingerprint) throw new InvalidOperationException("不同种子没有改变渲染指纹。");
        if (CountTerrainRenderDifferentPixels(first.Bitmap, different.Bitmap) == 0) throw new InvalidOperationException("完整画布取样没有随种子产生可控变化。");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try
        {
            service.RenderAsync(new TerrainRenderRequest
            {
                Draft = draft,
                Materials = new[] { material },
                StylePack = pack,
                Quality = TerrainRenderQuality.Final,
                CancellationToken = cts.Token
            }).GetAwaiter().GetResult();
            throw new InvalidOperationException("已取消的视觉重绘请求没有抛出取消异常。");
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }
    }

    private static int CountTerrainRenderDifferentPixels(Bitmap first, Bitmap second)
    {
        var count = 0;
        for (var y = 0; y < first.Height; y += 4)
        {
            for (var x = 0; x < first.Width; x += 4)
            {
                if (first.GetPixel(x, y).ToArgb() != second.GetPixel(x, y).ToArgb()) count++;
            }
        }

        return count;
    }
}
