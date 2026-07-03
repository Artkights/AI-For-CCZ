using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;

internal partial class Program
{
    static void RunTerrainGlobalTransitionFieldSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_TerrainGlobalTransitionFieldSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            Directory.CreateDirectory(materialRoot);
            var plainPath = Path.Combine(materialRoot, "plain.png");
            var grassPath = Path.Combine(materialRoot, "grass.png");
            var plain = Color.FromArgb(185, 190, 82);
            var grass = Color.FromArgb(66, 150, 54);
            SaveSolidBitmap(plainPath, 48, 48, plain);
            SaveSolidBitmap(grassPath, 48, 48, grass);

            var draft = new MapWorkbenchDraft
            {
                DraftId = "terrain-global-transition-field-smoke",
                GridWidth = 4,
                GridHeight = 1,
                TileSize = MapResourceItem.MapTilePixelSize,
                MaterialRoot = materialRoot,
                TerrainCells = [0, 0, 1, 1],
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                TerrainVisualProfile = new TerrainVisualProfile
                {
                    Seed = "global-transition",
                    RedrawChangedCellsOnly = false,
                    UseCurrentMapSamples = false,
                    UseGlobalTransitionField = true,
                    UseRegionTextureCanvas = false,
                    UseInteriorTextureSynthesis = false,
                    UseDirectionalBoundaryBlend = true,
                    TransitionFieldFeatherPixels = 18,
                    TransitionFieldJitterPixels = 0,
                    LocalColorTransferStrength = 0f,
                    ColorAlignmentStrength = 0f,
                    TextureNoiseStrength = 0f
                }
            };
            var materials = new List<MaterialAsset>
            {
                MakeTerrainStyleAsset(plainPath, 0, "plain", 0),
                MakeTerrainStyleAsset(grassPath, 1, "grass", 1)
            };

            using var synthesis = new TerrainVisualSynthesisService();
            using var result = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = draft,
                Materials = materials
            });

            if (result.Diagnostics.TransitionFieldPixelCount == 0 ||
                result.Diagnostics.BoundaryMaskPixelCount == 0 ||
                result.Diagnostics.MixedTerrainCellCount == 0)
            {
                throw new InvalidOperationException(
                    $"Global transition field diagnostics failed. field={result.Diagnostics.TransitionFieldPixelCount}, mask={result.Diagnostics.BoundaryMaskPixelCount}, mixed={result.Diagnostics.MixedTerrainCellCount}");
            }

            var boundaryX = 2 * 48;
            var lastRed = 256;
            for (var x = boundaryX - 16; x <= boundaryX + 16; x += 2)
            {
                var color = result.Bitmap.GetPixel(x, 24);
                if (color.R > lastRed + 2)
                {
                    throw new InvalidOperationException($"Boundary red channel was not monotonic at x={x}. prev={lastRed}, actual={color.R}");
                }

                lastRed = color.R;
            }

            Console.WriteLine("TERRAIN_GLOBAL_TRANSITION_FIELD_SMOKE_OK field=ok monotonic=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    static void RunTerrainRegionTextureCanvasSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_TerrainRegionTextureCanvasSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            Directory.CreateDirectory(materialRoot);
            var grassA = Path.Combine(materialRoot, "grass-a.png");
            var grassB = Path.Combine(materialRoot, "grass-b.png");
            SaveInteriorNaturalPatch(grassA, Color.FromArgb(62, 130, 50), Color.FromArgb(112, 158, 64), diagonal: false);
            SaveInteriorNaturalPatch(grassB, Color.FromArgb(76, 120, 44), Color.FromArgb(130, 150, 78), diagonal: true);

            var draft = new MapWorkbenchDraft
            {
                DraftId = "terrain-region-texture-canvas-smoke",
                GridWidth = 5,
                GridHeight = 4,
                TileSize = MapResourceItem.MapTilePixelSize,
                MaterialRoot = materialRoot,
                TerrainCells = Enumerable.Repeat((byte)1, 20).ToArray(),
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                TerrainVisualProfile = new TerrainVisualProfile
                {
                    Seed = "region-texture-canvas",
                    RedrawChangedCellsOnly = false,
                    UseCurrentMapSamples = false,
                    UseGlobalTransitionField = false,
                    UseRegionTextureCanvas = true,
                    UseInteriorTextureSynthesis = true,
                    UseInteriorSeamBlend = true,
                    QuiltingOverlapPixels = 12,
                    QuiltingCandidateCount = 2,
                    InteriorSecondaryBlendStrength = 0.18f,
                    MacroNoiseStrength = 0.12f,
                    RegionNoiseScalePixels = 96,
                    LocalColorTransferStrength = 0f,
                    ColorAlignmentStrength = 0f,
                    TextureNoiseStrength = 0f
                }
            };
            var materials = new List<MaterialAsset>
            {
                MakeInteriorTerrainAsset(grassA, 1, "grass-a", "Terrain:1:grass", 0, MaterialAutoTileModes.Default),
                MakeInteriorTerrainAsset(grassB, 1, "grass-b", "Terrain:1:grass", 1, MaterialAutoTileModes.Default)
            };

            using var synthesis = new TerrainVisualSynthesisService();
            using var result = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = draft,
                Materials = materials
            });

            if (result.Diagnostics.RegionTextureCanvasCount != 1 ||
                result.Diagnostics.QuiltedPatchCount != draft.CellCount ||
                result.Diagnostics.MacroNoiseAppliedPixels == 0 ||
                result.Diagnostics.InteriorSeamBlendPixelCount == 0)
            {
                throw new InvalidOperationException(
                    "Region texture canvas diagnostics failed. " +
                    $"canvases={result.Diagnostics.RegionTextureCanvasCount}, quilted={result.Diagnostics.QuiltedPatchCount}, macro={result.Diagnostics.MacroNoiseAppliedPixels}, seam={result.Diagnostics.InteriorSeamBlendPixelCount}");
            }

            var seamLeft = result.Bitmap.GetPixel(47, 24);
            var seamRight = result.Bitmap.GetPixel(48, 24);
            if (Math.Abs(seamLeft.R - seamRight.R) > 70 ||
                Math.Abs(seamLeft.G - seamRight.G) > 70 ||
                Math.Abs(seamLeft.B - seamRight.B) > 70)
            {
                throw new InvalidOperationException($"Region canvas seam remained too visible. left={seamLeft}, right={seamRight}");
            }

            using var repeat = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = draft,
                Materials = materials
            });
            AssertSameBitmap(result.Bitmap, repeat.Bitmap, "terrain region texture canvas same seed");

            Console.WriteLine("TERRAIN_REGION_TEXTURE_CANVAS_SMOKE_OK canvas=ok quilting=ok seam=ok deterministic=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    static void RunBuildingContactBlendSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_BuildingContactBlendSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            Directory.CreateDirectory(materialRoot);
            var plainPath = Path.Combine(materialRoot, "plain.png");
            var tentPath = Path.Combine(materialRoot, "tent.png");
            var ground = Color.FromArgb(72, 150, 72);
            var tent = Color.FromArgb(180, 120, 64);
            SaveSolidBitmap(plainPath, 48, 48, ground);
            SaveContactBlendBuilding(tentPath, tent);

            var draft = new MapWorkbenchDraft
            {
                DraftId = "building-contact-blend-smoke",
                GridWidth = 1,
                GridHeight = 1,
                TileSize = MapResourceItem.MapTilePixelSize,
                MaterialRoot = materialRoot,
                TerrainCells = [0],
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                TerrainVisualProfile = new TerrainVisualProfile
                {
                    Seed = "building-contact",
                    RedrawChangedCellsOnly = false,
                    UseCurrentMapSamples = false,
                    UseGlobalTransitionField = true,
                    UseRegionTextureCanvas = false,
                    UseInteriorTextureSynthesis = false,
                    UseObjectContactBlend = true,
                    ObjectContactBlendPixels = 6,
                    ObjectContactShadowStrength = 0.55f,
                    LocalColorTransferStrength = 0f,
                    ColorAlignmentStrength = 0f,
                    TextureNoiseStrength = 0f
                },
                BuildingOverlayCells =
                [
                    new MapCellOverride
                    {
                        Index = 0,
                        MaterialRelativePath = Path.GetFileName(tentPath),
                        MaterialCategory = "18",
                        DisplayName = Path.GetFileName(tentPath),
                        Source = MapCellOverrideSources.BuildingOverlay
                    }
                ]
            };
            var materials = new List<MaterialAsset>
            {
                MakeBuildingStyleAsset(plainPath, 0, "plain", "Terrain:0:plain", MaterialAssetTypes.Terrain, MaterialAutoTileModes.Default),
                MakeBuildingStyleAsset(tentPath, 18, "tent", "Tent:default", MaterialAssetTypes.Building, MaterialAutoTileModes.Default)
            };

            using var service = new MaterialDrivenTerrainService();
            using var composed = service.ComposeVisualMap(draft, materials, checkerboardBlank: false, beautifyTerrain: false);
            var contact = composed.GetPixel(24, 42);
            if (ColorDistance(contact, ground) < 8 || contact.R >= ground.R || contact.G >= ground.G)
            {
                throw new InvalidOperationException($"Building contact pixel was not shaded. contact={contact}, ground={ground}");
            }

            var objectCenter = composed.GetPixel(24, 24);
            if (ColorDistance(objectCenter, tent) > 4)
            {
                throw new InvalidOperationException($"Building body was altered by contact blend. center={objectCenter}, expected={tent}");
            }

            Console.WriteLine("BUILDING_CONTACT_BLEND_SMOKE_OK contact=ok bodyPreserve=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    static void RunObjectAlphaRepairSmoke()
    {
        using var source = new Bitmap(48, 48, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.Black);
            using var body = new SolidBrush(Color.FromArgb(170, 118, 68));
            g.FillRectangle(body, 10, 10, 28, 28);
            using var innerLine = new Pen(Color.Black, 2);
            g.DrawLine(innerLine, 18, 16, 18, 32);
        }

        var service = new ObjectAlphaRepairService();
        using var repaired = service.Repair(source, new TerrainVisualProfile
        {
            AlphaRepairBlackThreshold = 24,
            AlphaRepairEdgeConnectivity = true
        });

        if (!repaired.Repaired || repaired.RepairedPixelCount == 0)
        {
            throw new InvalidOperationException("Object alpha repair did not remove edge-connected black background.");
        }

        if (repaired.Bitmap.GetPixel(2, 2).A != 0)
        {
            throw new InvalidOperationException("Object alpha repair left the edge-connected black background opaque.");
        }

        var bodyPixel = repaired.Bitmap.GetPixel(24, 24);
        if (bodyPixel.A < 250 || ColorDistance(bodyPixel, Color.FromArgb(170, 118, 68)) > 4)
        {
            throw new InvalidOperationException($"Object alpha repair damaged the building body. actual={bodyPixel}");
        }

        var linePixel = repaired.Bitmap.GetPixel(18, 24);
        if (linePixel.A < 250 || linePixel.R > 8 || linePixel.G > 8 || linePixel.B > 8)
        {
            throw new InvalidOperationException($"Object alpha repair removed an internal black detail. actual={linePixel}");
        }

        Console.WriteLine("OBJECT_ALPHA_REPAIR_SMOKE_OK edgeBlack=transparent internalBlack=kept body=kept");
    }

    static void RunBuildingGroundInpaintSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_BuildingGroundInpaintSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            Directory.CreateDirectory(materialRoot);
            var plainPath = Path.Combine(materialRoot, "plain.png");
            var buildingPath = Path.Combine(materialRoot, "black-bg-building.png");
            var ground = Color.FromArgb(70, 146, 66);
            var building = Color.FromArgb(176, 112, 62);
            SaveSolidBitmap(plainPath, 48, 48, ground);
            SaveBlackBackgroundBuilding(buildingPath, building);

            var draft = new MapWorkbenchDraft
            {
                DraftId = "building-ground-inpaint-smoke",
                GridWidth = 1,
                GridHeight = 1,
                TileSize = MapResourceItem.MapTilePixelSize,
                MaterialRoot = materialRoot,
                TerrainCells = [0],
                OriginalTerrainCells = [0],
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                TerrainVisualProfile = new TerrainVisualProfile
                {
                    Seed = "building-ground-inpaint",
                    RedrawChangedCellsOnly = false,
                    UseCurrentMapSamples = false,
                    UseRegionTextureCanvas = false,
                    UseInteriorTextureSynthesis = false,
                    UseGlobalTransitionField = true,
                    RegenerateGroundUnderBuildingOverlays = true,
                    ObjectGroundContextRadiusCells = 1,
                    UseObjectContactBlend = false,
                    AlphaRepairBlackThreshold = 24,
                    LocalColorTransferStrength = 0f,
                    ColorAlignmentStrength = 0f,
                    TextureNoiseStrength = 0f
                },
                BuildingOverlayCells =
                [
                    new MapCellOverride
                    {
                        Index = 0,
                        MaterialRelativePath = Path.GetFileName(buildingPath),
                        MaterialCategory = "18",
                        DisplayName = Path.GetFileName(buildingPath),
                        Source = MapCellOverrideSources.BuildingOverlay
                    }
                ]
            };
            var materials = new List<MaterialAsset>
            {
                MakeBuildingStyleAsset(plainPath, 0, "plain", "Terrain:0:plain", MaterialAssetTypes.Terrain, MaterialAutoTileModes.Default),
                MakeBuildingStyleAsset(buildingPath, 18, "black-bg-building", "Building:black-bg", MaterialAssetTypes.Building, MaterialAutoTileModes.Default)
            };

            using var service = new MaterialDrivenTerrainService();
            using var composed = service.ComposeVisualMap(draft, materials, checkerboardBlank: false, beautifyTerrain: false);
            var corner = composed.GetPixel(3, 3);
            if (ColorDistance(corner, ground) >= ColorDistance(corner, Color.Black))
            {
                throw new InvalidOperationException($"Building ground inpaint left a black background at transparent corner. actual={corner}");
            }

            var center = composed.GetPixel(20, 24);
            if (ColorDistance(center, building) > 4)
            {
                throw new InvalidOperationException($"Building body was not drawn after ground inpaint. actual={center}");
            }

            Console.WriteLine("BUILDING_GROUND_INPAINT_SMOKE_OK blackBg=removed ground=preserved body=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    static void RunCurrentMapPureSamplePrioritySmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_CurrentMapPureSamplePrioritySmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            var sampleRoot = Path.Combine(tempRoot, "samples");
            Directory.CreateDirectory(materialRoot);
            var basePath = Path.Combine(tempRoot, "M003.png");
            var libraryPath = Path.Combine(materialRoot, "library-grass.png");
            var buildingPath = Path.Combine(materialRoot, "sample-polluter.png");
            var currentGrass = Color.FromArgb(64, 138, 54);
            var libraryGrass = Color.FromArgb(190, 48, 170);
            SaveCurrentMapSamplePriorityBase(basePath, currentGrass);
            SaveSolidBitmap(libraryPath, 48, 48, libraryGrass);
            SaveBlackBackgroundBuilding(buildingPath, Color.FromArgb(180, 120, 64));

            var draft = new MapWorkbenchDraft
            {
                DraftId = "current-map-pure-sample-priority-smoke",
                BoundMapId = "M003",
                GridWidth = 5,
                GridHeight = 5,
                TileSize = MapResourceItem.MapTilePixelSize,
                BaseLayerPath = basePath,
                MaterialRoot = materialRoot,
                OriginalTerrainCells = Enumerable.Repeat((byte)1, 25).ToArray(),
                TerrainCells = Enumerable.Repeat((byte)1, 25).ToArray(),
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                TerrainVisualProfile = new TerrainVisualProfile
                {
                    Seed = "current-map-pure-priority",
                    UseCurrentMapSamples = true,
                    AutoExtractCurrentMapSamples = true,
                    RedrawChangedCellsOnly = false,
                    UseRegionTextureCanvas = true,
                    UseGlobalTransitionField = false,
                    UseInteriorTextureSynthesis = true,
                    MinPureSamplesPerTerrain = 4,
                    StyleSampleRoot = sampleRoot,
                    LocalColorTransferStrength = 0f,
                    ColorAlignmentStrength = 0f,
                    TextureNoiseStrength = 0f,
                    MacroNoiseStrength = 0f
                },
                BuildingOverlayCells =
                [
                    new MapCellOverride
                    {
                        Index = 12,
                        MaterialRelativePath = Path.GetFileName(buildingPath),
                        MaterialCategory = "18",
                        DisplayName = Path.GetFileName(buildingPath),
                        Source = MapCellOverrideSources.BuildingOverlay
                    }
                ]
            };
            var materials = new List<MaterialAsset>
            {
                MakeInteriorTerrainAsset(libraryPath, 1, "library-grass", "Terrain:1:library", 0, MaterialAutoTileModes.Default),
                MakeBuildingStyleAsset(buildingPath, 18, "sample-polluter", "Building:polluter", MaterialAssetTypes.Building, MaterialAutoTileModes.Default)
            };

            var styleService = new CurrentMapStyleProfileService();
            var styleProfile = styleService.BuildProfile(draft, sampleRoot, writeSamples: true);
            if (styleProfile.RejectedSampleCount == 0)
            {
                throw new InvalidOperationException("Current-map style profile did not reject the object-covered sample.");
            }

            var terrainProfile = styleProfile.FindTerrain(1) ?? throw new InvalidOperationException("Terrain 1 was not sampled.");
            if (terrainProfile.PureSamples.Count == 0 || terrainProfile.Samples.Any(sample => sample.CellIndex == 12))
            {
                throw new InvalidOperationException("Current-map style samples did not prefer pure terrain samples over object-covered cells.");
            }

            using var synthesis = new TerrainVisualSynthesisService();
            using var result = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = draft,
                Materials = materials,
                StyleProfile = styleProfile
            });
            var actual = result.Bitmap.GetPixel(24, 24);
            if (ColorDistance(actual, currentGrass) >= ColorDistance(actual, libraryGrass))
            {
                throw new InvalidOperationException($"Terrain synthesis did not prefer current-map pure samples. actual={actual}");
            }

            if (result.Diagnostics.CurrentMapPureSampleUsedCount == 0 ||
                result.Diagnostics.CurrentMapSampleRejectedCount == 0)
            {
                throw new InvalidOperationException(
                    $"Current-map priority diagnostics failed. pure={result.Diagnostics.CurrentMapPureSampleUsedCount}, rejected={result.Diagnostics.CurrentMapSampleRejectedCount}");
            }

            Console.WriteLine("CURRENT_MAP_PURE_SAMPLE_PRIORITY_SMOKE_OK purePriority=ok rejectedPollution=ok diagnostics=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    static void RunTerrainObjectGroundInpaintSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_TerrainObjectGroundInpaintSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            Directory.CreateDirectory(materialRoot);
            var basePath = Path.Combine(tempRoot, "M004.png");
            var grassPath = Path.Combine(materialRoot, "grass.png");
            var grass = Color.FromArgb(72, 146, 64);
            SaveObjectFootprintBase(basePath, 3, 3, index => index == 4 ? Color.Black : grass);
            SaveSolidBitmap(grassPath, 48, 48, grass);

            var terrain = Enumerable.Repeat((byte)1, 9).ToArray();
            terrain[4] = 15;
            var draft = new MapWorkbenchDraft
            {
                DraftId = "terrain-object-ground-inpaint-smoke",
                BoundMapId = "M004",
                GridWidth = 3,
                GridHeight = 3,
                TileSize = MapResourceItem.MapTilePixelSize,
                BaseLayerPath = basePath,
                MaterialRoot = materialRoot,
                OriginalTerrainCells = terrain.ToArray(),
                TerrainCells = terrain,
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                TerrainVisualProfile = new TerrainVisualProfile
                {
                    Seed = "terrain-object-ground-inpaint",
                    UseCurrentMapSamples = false,
                    RedrawChangedCellsOnly = true,
                    UseObjectGroundInpaint = true,
                    GroundInpaintIncludesTerrainObjects = true,
                    ObjectGroundInferenceRadiusCells = 3,
                    ObjectGroundContextRadiusCells = 1,
                    UseGlobalTransitionField = true,
                    UseRegionTextureCanvas = false,
                    UseInteriorTextureSynthesis = false,
                    LocalColorTransferStrength = 0f,
                    ColorAlignmentStrength = 0f,
                    TextureNoiseStrength = 0f
                }
            };

            var materials = new List<MaterialAsset>
            {
                MakeTerrainStyleAsset(grassPath, 1, "grass", 0)
            };

            using var synthesis = new TerrainVisualSynthesisService();
            using var result = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = draft,
                Materials = materials
            });

            var center = result.Bitmap.GetPixel(48 + 24, 48 + 24);
            if (ColorDistance(center, grass) >= ColorDistance(center, Color.Black))
            {
                throw new InvalidOperationException($"Terrain object ground inpaint left the old black footprint. actual={center}");
            }

            if (draft.TerrainCells[4] != 15)
            {
                throw new InvalidOperationException("Terrain object ground inpaint mutated the real terrain cells.");
            }

            if (result.Diagnostics.ObjectGroundFootprintCellCount == 0 ||
                result.Diagnostics.ObjectGroundInferredCellCount == 0 ||
                result.Diagnostics.TerrainObjectOverlayCellCount == 0)
            {
                throw new InvalidOperationException(
                    "Terrain object ground inpaint diagnostics failed. " +
                    $"footprints={result.Diagnostics.ObjectGroundFootprintCellCount}, inferred={result.Diagnostics.ObjectGroundInferredCellCount}, terrainObjects={result.Diagnostics.TerrainObjectOverlayCellCount}");
            }

            Console.WriteLine("TERRAIN_OBJECT_GROUND_INPAINT_SMOKE_OK ground=rebuilt terrainPreserved=ok diagnostics=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    static void RunBridgeGroundInferenceSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_BridgeGroundInferenceSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            Directory.CreateDirectory(materialRoot);
            var basePath = Path.Combine(tempRoot, "M005.png");
            var waterPath = Path.Combine(materialRoot, "river.png");
            var water = Color.FromArgb(54, 104, 170);
            SaveObjectFootprintBase(basePath, 3, 3, index => index == 4 ? Color.Black : water);
            SaveSolidBitmap(waterPath, 48, 48, water);

            var terrain = Enumerable.Repeat((byte)13, 9).ToArray();
            terrain[4] = 8;
            var draft = new MapWorkbenchDraft
            {
                DraftId = "bridge-ground-inference-smoke",
                BoundMapId = "M005",
                GridWidth = 3,
                GridHeight = 3,
                TileSize = MapResourceItem.MapTilePixelSize,
                BaseLayerPath = basePath,
                MaterialRoot = materialRoot,
                OriginalTerrainCells = terrain.ToArray(),
                TerrainCells = terrain,
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                TerrainVisualProfile = new TerrainVisualProfile
                {
                    Seed = "bridge-ground-inference",
                    UseCurrentMapSamples = false,
                    RedrawChangedCellsOnly = true,
                    UseObjectGroundInpaint = true,
                    GroundInpaintIncludesTerrainObjects = true,
                    ObjectGroundInferenceRadiusCells = 3,
                    ObjectGroundContextRadiusCells = 1,
                    UseGlobalTransitionField = true,
                    UseRegionTextureCanvas = false,
                    UseInteriorTextureSynthesis = false,
                    LocalColorTransferStrength = 0f,
                    ColorAlignmentStrength = 0f,
                    TextureNoiseStrength = 0f
                }
            };

            var materials = new List<MaterialAsset>
            {
                MakeTerrainStyleAsset(waterPath, 13, "river", 0)
            };

            using var synthesis = new TerrainVisualSynthesisService();
            using var result = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = draft,
                Materials = materials
            });

            var center = result.Bitmap.GetPixel(48 + 24, 48 + 24);
            if (ColorDistance(center, water) >= ColorDistance(center, Color.Black))
            {
                throw new InvalidOperationException($"Bridge ground inference did not prefer adjacent water. actual={center}");
            }

            if (draft.TerrainCells[4] != 8)
            {
                throw new InvalidOperationException("Bridge ground inference mutated the real terrain cells.");
            }

            if (result.Diagnostics.ObjectGroundInferredCellCount == 0 ||
                result.Diagnostics.ObjectGroundFallbackCellCount != 0)
            {
                throw new InvalidOperationException(
                    $"Bridge inference diagnostics failed. inferred={result.Diagnostics.ObjectGroundInferredCellCount}, fallback={result.Diagnostics.ObjectGroundFallbackCellCount}");
            }

            Console.WriteLine("BRIDGE_GROUND_INFERENCE_SMOKE_OK water=preferred terrainPreserved=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    static void RunObjectFootprintColorContinuitySmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_ObjectFootprintColorContinuitySmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            Directory.CreateDirectory(materialRoot);
            var basePath = Path.Combine(tempRoot, "M006.png");
            var grassPath = Path.Combine(materialRoot, "grass.png");
            var plainPath = Path.Combine(materialRoot, "plain.png");
            var grass = Color.FromArgb(62, 148, 58);
            var plain = Color.FromArgb(184, 190, 90);
            SaveObjectFootprintBase(basePath, 5, 1, index => index == 2 ? Color.Black : index < 2 ? grass : plain);
            SaveSolidBitmap(grassPath, 48, 48, grass);
            SaveSolidBitmap(plainPath, 48, 48, plain);

            var terrain = new byte[] { 1, 1, 15, 0, 0 };
            var draft = new MapWorkbenchDraft
            {
                DraftId = "object-footprint-color-continuity-smoke",
                BoundMapId = "M006",
                GridWidth = 5,
                GridHeight = 1,
                TileSize = MapResourceItem.MapTilePixelSize,
                BaseLayerPath = basePath,
                MaterialRoot = materialRoot,
                OriginalTerrainCells = terrain.ToArray(),
                TerrainCells = terrain,
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                TerrainVisualProfile = new TerrainVisualProfile
                {
                    Seed = "object-footprint-continuity",
                    UseCurrentMapSamples = false,
                    RedrawChangedCellsOnly = true,
                    UseObjectGroundInpaint = true,
                    GroundInpaintIncludesTerrainObjects = true,
                    ObjectGroundInferenceRadiusCells = 3,
                    ObjectGroundContextRadiusCells = 1,
                    UseGlobalTransitionField = true,
                    TransitionFieldFeatherPixels = 18,
                    TransitionFieldJitterPixels = 0,
                    UseRegionTextureCanvas = false,
                    UseInteriorTextureSynthesis = false,
                    LocalColorTransferStrength = 0f,
                    ColorAlignmentStrength = 0f,
                    TextureNoiseStrength = 0f
                }
            };

            var materials = new List<MaterialAsset>
            {
                MakeTerrainStyleAsset(grassPath, 1, "grass", 0),
                MakeTerrainStyleAsset(plainPath, 0, "plain", 1)
            };

            using var synthesis = new TerrainVisualSynthesisService();
            using var result = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = draft,
                Materials = materials
            });

            var footprintCenter = result.Bitmap.GetPixel(2 * 48 + 24, 24);
            if (ColorDistance(footprintCenter, Color.Black) < ColorDistance(footprintCenter, grass) ||
                ColorDistance(footprintCenter, Color.Black) < ColorDistance(footprintCenter, plain))
            {
                throw new InvalidOperationException($"Object footprint continuity left a black patch. actual={footprintCenter}");
            }

            if (result.Diagnostics.TransitionFieldPixelCount == 0 ||
                result.Diagnostics.ObjectGroundInpaintCellCount == 0)
            {
                throw new InvalidOperationException(
                    $"Object footprint continuity diagnostics failed. transition={result.Diagnostics.TransitionFieldPixelCount}, inpaint={result.Diagnostics.ObjectGroundInpaintCellCount}");
            }

            Console.WriteLine("OBJECT_FOOTPRINT_COLOR_CONTINUITY_SMOKE_OK noBlack=ok transition=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void SaveContactBlendBuilding(string path, Color color)
    {
        using var bitmap = new Bitmap(48, 48, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, 12, 12, 24, 28);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void SaveBlackBackgroundBuilding(string path, Color color)
    {
        using var bitmap = new Bitmap(48, 48, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Black);
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, 12, 12, 24, 24);
        using var detail = new Pen(Color.Black, 2);
        g.DrawLine(detail, 24, 16, 24, 32);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void SaveCurrentMapSamplePriorityBase(string path, Color grass)
    {
        using var bitmap = new Bitmap(5 * 48, 5 * 48, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(grass);
        g.FillRectangle(brush, 0, 0, bitmap.Width, bitmap.Height);
        using var black = new SolidBrush(Color.Black);
        g.FillRectangle(black, 2 * 48, 2 * 48, 48, 48);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void SaveObjectFootprintBase(string path, int gridWidth, int gridHeight, Func<int, Color> colorByIndex)
    {
        using var bitmap = new Bitmap(gridWidth * 48, gridHeight * 48, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        for (var y = 0; y < gridHeight; y++)
        {
            for (var x = 0; x < gridWidth; x++)
            {
                var index = y * gridWidth + x;
                using var brush = new SolidBrush(colorByIndex(index));
                graphics.FillRectangle(brush, x * 48, y * 48, 48, 48);
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }
}
