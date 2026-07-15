using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class TerrainRenderService : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cacheByKey = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly int _cacheCapacity;
    private CancellationTokenSource? _latestRequestCancellation;
    private long _latestRequestId;
    private bool _disposed;

    public TerrainRenderService(int cacheCapacity = 6)
    {
        _cacheCapacity = Math.Clamp(cacheCapacity, 1, 24);
    }

    public Task<TerrainRenderResult> RenderAsync(TerrainRenderRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.Draft == null) throw new ArgumentException("必须提供地图草稿。", nameof(request));

        CancellationTokenSource linked;
        long requestId;
        lock (_gate)
        {
            _latestRequestCancellation?.Cancel();
            _latestRequestCancellation?.Dispose();
            linked = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken);
            _latestRequestCancellation = linked;
            requestId = ++_latestRequestId;
        }

        return Task.Run(() => RenderCore(request, requestId, linked.Token), linked.Token);
    }

    public string ComputeFingerprint(TerrainRenderRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.Draft == null) throw new ArgumentException("必须提供地图草稿。", nameof(request));
        var materials = FilterMaterials(
            request.Draft,
            request.Materials,
            request.StylePack,
            request.Quality == TerrainRenderQuality.Draft ? request.DirtyCellIndexes : null);
        return BuildFingerprint(request, materials);
    }

    private TerrainRenderResult RenderCore(TerrainRenderRequest request, long requestId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var materials = FilterMaterials(
            request.Draft,
            request.Materials,
            request.StylePack,
            request.Quality == TerrainRenderQuality.Draft ? request.DirtyCellIndexes : null);
        var fingerprint = BuildFingerprint(request, materials);
        if (TryGetCached(fingerprint, request.Quality, out var cached)) return cached;

        var watch = Stopwatch.StartNew();
        var beforeMemory = GC.GetTotalMemory(forceFullCollection: false);
        var renderDraft = CloneDraft(request.Draft);
        ApplyRequestSettings(renderDraft, request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Quality == TerrainRenderQuality.Draft && request.DirtyCellIndexes?.Count == 0)
        {
            return RenderUnchangedDraft(request, renderDraft, requestId, cancellationToken, fingerprint, watch, beforeMemory);
        }

        using var synthesisService = new TerrainVisualSynthesisService();
        using var synthesis = synthesisService.Synthesize(new TerrainVisualSynthesisRequest
        {
            Draft = renderDraft,
            Materials = materials,
            StyleProfile = request.CurrentMapStyle,
            RedrawIndexes = request.Quality == TerrainRenderQuality.Draft ? request.DirtyCellIndexes : null,
            CancellationToken = cancellationToken
        });
        cancellationToken.ThrowIfCancellationRequested();

        var composed = new Bitmap(synthesis.Bitmap);
        cancellationToken.ThrowIfCancellationRequested();
        PreserveUncertainBakedObjects(composed, renderDraft, request, out var preservedObjectCells);
        using (var materialService = new MaterialDrivenTerrainService())
        using (var graphics = Graphics.FromImage(composed))
        {
            cancellationToken.ThrowIfCancellationRequested();
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            materialService.DrawOverlays(graphics, renderDraft, materials, drawScenery: true);
        }

        Bitmap finalBitmap;
        using (composed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            finalBitmap = new MapToneFilterService().Apply(composed, renderDraft.TerrainRenderSettings, renderDraft.CustomBeautifyFilter);
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (requestId != _latestRequestId) throw new OperationCanceledException("该渲染结果已被更新的请求取代。", cancellationToken);
        }

        watch.Stop();
        var diagnostics = new TerrainRenderDiagnostics
        {
            Quality = request.Quality,
            StylePackId = request.StylePack?.Id ?? string.Empty,
            Fingerprint = fingerprint,
            ElapsedMilliseconds = watch.ElapsedMilliseconds,
            PeakManagedBytes = Math.Max(beforeMemory, GC.GetTotalMemory(forceFullCollection: false)),
            MissingMaterialCount = synthesis.Diagnostics.MissingTerrainIds.Count,
            PreservedObjectCellCount = preservedObjectCells,
            LowConfidenceObjectCellCount = preservedObjectCells,
            FallbackCellCount = synthesis.Diagnostics.FallbackCellCount,
            RepeatedPatchCount = synthesis.Diagnostics.RepeatedPatchPenaltyCount,
            Synthesis = synthesis.Diagnostics
        };
        if (preservedObjectCells > 0)
        {
            diagnostics.Warnings.Add($"{preservedObjectCells} 个烘焙对象格因遮罩可信度不足而整格保留。");
        }

        StoreCached(fingerprint, request.Quality, finalBitmap, diagnostics);
        return new TerrainRenderResult { Bitmap = finalBitmap, Diagnostics = diagnostics, Fingerprint = fingerprint };
    }

    private TerrainRenderResult RenderUnchangedDraft(
        TerrainRenderRequest request,
        MapWorkbenchDraft draft,
        long requestId,
        CancellationToken cancellationToken,
        string fingerprint,
        Stopwatch watch,
        long beforeMemory)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Bitmap source;
        if (!string.IsNullOrWhiteSpace(draft.BaseLayerPath) && File.Exists(draft.BaseLayerPath))
        {
            using var image = Image.FromFile(draft.BaseLayerPath);
            source = new Bitmap(image);
        }
        else
        {
            source = new Bitmap(draft.PixelWidth, draft.PixelHeight);
            using var graphics = Graphics.FromImage(source);
            graphics.Clear(Color.Black);
        }

        Bitmap finalBitmap;
        using (source)
        {
            finalBitmap = new MapToneFilterService().Apply(source, draft.TerrainRenderSettings, draft.CustomBeautifyFilter);
        }

        lock (_gate)
        {
            if (requestId != _latestRequestId)
            {
                finalBitmap.Dispose();
                throw new OperationCanceledException("该渲染结果已被更新的请求取代。", cancellationToken);
            }
        }

        watch.Stop();
        var diagnostics = new TerrainRenderDiagnostics
        {
            Quality = TerrainRenderQuality.Draft,
            StylePackId = request.StylePack?.Id ?? string.Empty,
            Fingerprint = fingerprint,
            ElapsedMilliseconds = watch.ElapsedMilliseconds,
            PeakManagedBytes = Math.Max(beforeMemory, GC.GetTotalMemory(forceFullCollection: false)),
            Synthesis = new TerrainVisualSynthesisDiagnostics
            {
                PreservedCellCount = draft.CellCount,
                RedrawnCellCount = 0,
                FastPipelineEnabled = true,
                TotalMs = watch.ElapsedMilliseconds
            }
        };
        StoreCached(fingerprint, TerrainRenderQuality.Draft, finalBitmap, diagnostics);
        return new TerrainRenderResult { Bitmap = finalBitmap, Diagnostics = diagnostics, Fingerprint = fingerprint };
    }

    private static void ApplyRequestSettings(MapWorkbenchDraft draft, TerrainRenderRequest request)
    {
        draft.SchemaVersion = 2;
        draft.GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual;
        draft.TerrainRenderSettings.RedrawEnabled = true;
        draft.TerrainVisualProfile.Seed = draft.TerrainRenderSettings.Seed;
        if (request.StylePack != null)
        {
            draft.TerrainRenderSettings.StylePackId = request.StylePack.Id;
            draft.TerrainVisualProfile.SurfaceOverrides = request.StylePack.Terrains
                .Select(rule => new TerrainSurfaceOverride { TerrainId = rule.TerrainId, SurfaceKind = rule.SurfaceKind })
                .ToList();
        }

        if (request.Quality == TerrainRenderQuality.Draft)
        {
            draft.TerrainVisualProfile.UseRegionTextureCanvas = false;
            draft.TerrainVisualProfile.UseGlobalTransitionField = false;
            draft.TerrainVisualProfile.UseInteriorTextureSynthesis = false;
            draft.TerrainVisualProfile.UseInteriorSeamBlend = false;
            draft.TerrainVisualProfile.UseObjectGroundInpaint = false;
            draft.TerrainVisualProfile.UseObjectContactBlend = false;
            draft.TerrainVisualProfile.UseGlobalBuildingStyle = false;
            draft.TerrainVisualProfile.QuiltingCandidateCount = Math.Min(3, draft.TerrainVisualProfile.QuiltingCandidateCount);
            draft.TerrainVisualProfile.RedrawChangedCellsOnly = request.DirtyCellIndexes != null;
        }
        else
        {
            draft.TerrainVisualProfile.RedrawChangedCellsOnly = false;
        }
    }

    private static IReadOnlyList<MaterialAsset> FilterMaterials(
        MapWorkbenchDraft draft,
        IReadOnlyList<MaterialAsset> materials,
        TerrainStylePackManifest? stylePack,
        IReadOnlyCollection<int>? dirtyCellIndexes)
    {
        var rules = stylePack?.Terrains.ToDictionary(rule => rule.TerrainId)
                    ?? new Dictionary<byte, TerrainStylePackTerrainRule>();
        var activeTerrainIds = dirtyCellIndexes == null
            ? draft.TerrainCells.ToHashSet()
            : ExpandDirtyTerrainIds(draft, dirtyCellIndexes);
        var referencedPaths = draft.BuildingOverlayCells
            .Concat(draft.SceneryOverlayCells)
            .Select(cell => cell.MaterialRelativePath)
            .Concat(draft.SceneryOverlays.Select(overlay => overlay.MaterialRelativePath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFileName(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return materials.Where(asset =>
        {
            if (stylePack != null && !string.IsNullOrWhiteSpace(asset.StylePackId) && !asset.StylePackId.Equals(stylePack.Id, StringComparison.OrdinalIgnoreCase)) return false;
            if (!asset.TerrainId.HasValue) return referencedPaths.Contains(asset.FileName);
            if (!activeTerrainIds.Contains(asset.TerrainId.Value)) return false;
            if (!rules.TryGetValue(asset.TerrainId.Value, out var rule) || rule.TextureSources.Count == 0) return true;
            return rule.TextureSources.Any(source =>
                asset.FilePath.EndsWith(source, StringComparison.OrdinalIgnoreCase) ||
                asset.FileName.Equals(Path.GetFileName(source), StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }

    private static HashSet<byte> ExpandDirtyTerrainIds(MapWorkbenchDraft draft, IReadOnlyCollection<int> dirtyCellIndexes)
    {
        var result = new HashSet<byte>();
        foreach (var index in dirtyCellIndexes)
        {
            if ((uint)index >= (uint)draft.CellCount) continue;
            var x = index % draft.GridWidth;
            var y = index / draft.GridWidth;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= draft.GridWidth || ny >= draft.GridHeight) continue;
                    result.Add(draft.TerrainCells[ny * draft.GridWidth + nx]);
                }
            }
        }

        return result;
    }

    private static void PreserveUncertainBakedObjects(
        Bitmap target,
        MapWorkbenchDraft draft,
        TerrainRenderRequest request,
        out int preservedCells)
    {
        preservedCells = 0;
        if (draft.TerrainRenderSettings.ObjectPolicy == TerrainObjectPolicy.RedrawAll ||
            string.IsNullOrWhiteSpace(draft.BaseLayerPath) || !File.Exists(draft.BaseLayerPath) ||
            draft.TerrainCells.Length != draft.CellCount)
        {
            return;
        }

        using var source = Image.FromFile(draft.BaseLayerPath);
        if (source.Width != target.Width || source.Height != target.Height) return;
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        using var graphics = Graphics.FromImage(target);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        for (var index = 0; index < draft.TerrainCells.Length; index++)
        {
            var kind = TerrainVisualSurfaceClassifier.Classify(draft.TerrainVisualProfile, draft.TerrainCells[index]);
            if (kind is not TerrainVisualSurfaceKind.StructureTerrain and not TerrainVisualSurfaceKind.BuildingOverlay) continue;
            var rect = new Rectangle(index % draft.GridWidth * tileSize, index / draft.GridWidth * tileSize, tileSize, tileSize);
            graphics.DrawImage(source, rect, rect, GraphicsUnit.Pixel);
            preservedCells++;
        }
    }

    private bool TryGetCached(string fingerprint, TerrainRenderQuality quality, out TerrainRenderResult result)
    {
        var key = fingerprint + "|" + quality;
        lock (_gate)
        {
            if (_cacheByKey.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                result = new TerrainRenderResult
                {
                    Bitmap = new Bitmap(node.Value.Bitmap),
                    Diagnostics = CloneDiagnostics(node.Value.Diagnostics),
                    Fingerprint = fingerprint
                };
                return true;
            }
        }

        result = null!;
        return false;
    }

    private void StoreCached(string fingerprint, TerrainRenderQuality quality, Bitmap bitmap, TerrainRenderDiagnostics diagnostics)
    {
        var key = fingerprint + "|" + quality;
        lock (_gate)
        {
            if (_cacheByKey.Remove(key, out var existing))
            {
                _lru.Remove(existing);
                existing.Value.Bitmap.Dispose();
            }

            var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, new Bitmap(bitmap), CloneDiagnostics(diagnostics)));
            _lru.AddFirst(node);
            _cacheByKey[key] = node;
            while (_lru.Count > _cacheCapacity)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _cacheByKey.Remove(last.Value.Key);
                last.Value.Bitmap.Dispose();
            }
        }
    }

    private static string BuildFingerprint(TerrainRenderRequest request, IReadOnlyList<MaterialAsset> materials)
    {
        var draft = request.Draft;
        var builder = new StringBuilder()
            .Append(draft.SchemaVersion).Append('|').Append(draft.GridWidth).Append('x').Append(draft.GridHeight).Append('|')
            .Append(draft.TileSize).Append('|').Append(draft.BoundMapId).Append('|').Append(request.Quality).Append('|')
            .Append(draft.TerrainRenderSettings.StylePackId).Append('|').Append(request.StylePack?.Version).Append('|')
            .Append(StylePackFingerprint(request.StylePack)).Append('|')
            .Append(draft.TerrainRenderSettings.Seed).Append('|').Append(draft.TerrainRenderSettings.ObjectPolicy).Append('|')
            .Append(draft.TerrainRenderSettings.ToneProfile).Append('|').Append(draft.TerrainRenderSettings.ToneAmount).Append('|')
            .Append(draft.MaterialRoot).Append('|')
            .Append(draft.TerrainVisualProfile.StyleSampleRoot).Append('|')
            .Append(TerrainVisualProfileFingerprint(draft.TerrainVisualProfile)).Append('|')
            .Append(CustomFilterFingerprint(draft.CustomBeautifyFilter)).Append('|')
            .Append(Convert.ToHexString(SHA256.HashData(draft.TerrainCells))).Append('|')
            .Append(Convert.ToHexString(SHA256.HashData(draft.OriginalTerrainCells))).Append('|')
            .Append(FileFingerprint(draft.BaseLayerPath)).Append('|')
            .Append(request.DirtyCellIndexes == null ? "*" : string.Join(',', request.DirtyCellIndexes.OrderBy(index => index))).Append('|');
        foreach (var sample in EnumerateStyleSamples(request.CurrentMapStyle))
        {
            builder.Append(FileFingerprint(sample.FilePath)).Append(':').Append(sample.TerrainId).Append(':').Append(sample.CellIndex).Append(':')
                .Append(sample.IsBoundary).Append(':').Append(sample.IsContaminated).Append(';');
        }

        foreach (var item in draft.TerrainMaterialPlan.OrderBy(item => item.VisualFamilyKey, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.MaterialRelativePath, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(item.MapId).Append(':').Append(item.TerrainId).Append(':').Append(item.VisualFamilyKey).Append(':')
                .Append(item.MaterialRelativePath).Append(':').Append(item.SelectionMode).Append(':').Append(item.MaterialRootFingerprint).Append(';');
        }

        foreach (var item in draft.TerrainVisualProfile.SurfaceOverrides.OrderBy(item => item.TerrainId))
        {
            builder.Append(item.TerrainId).Append(':').Append(item.SurfaceKind).Append(';');
        }

        foreach (var item in draft.TerrainVisualProfile.MaterialOverrides.OrderBy(item => item.TerrainId).ThenBy(item => item.MaterialRelativePath, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(item.TerrainId).Append(':').Append(item.MaterialRelativePath).Append(';');
        }

        foreach (var material in materials.OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.SourceX).ThenBy(item => item.SourceY))
        {
            builder.Append(FileFingerprint(material.FilePath)).Append(':').Append(material.TerrainId).Append(':')
                .Append(material.SamplingMode).Append(':').Append(material.SourceX).Append(',').Append(material.SourceY).Append(',')
                .Append(material.SourceWidth).Append(',').Append(material.SourceHeight).Append(';');
        }

        foreach (var cell in draft.BuildingOverlayCells.Concat(draft.SceneryOverlayCells).Concat(draft.MapCellOverrides).OrderBy(item => item.Index))
        {
            builder.Append(cell.Index).Append(':').Append(cell.MaterialRelativePath).Append(':').Append(cell.Source).Append(';');
        }

        foreach (var overlay in draft.SceneryOverlays.OrderBy(item => item.ZOrder).ThenBy(item => item.OverlayId))
        {
            builder.Append(overlay.MaterialRelativePath).Append(':').Append(overlay.X).Append(',').Append(overlay.Y).Append(',')
                .Append(overlay.Width).Append(',').Append(overlay.Height).Append(',').Append(overlay.RotationDegrees).Append(',').Append(overlay.ZOrder).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static IEnumerable<CurrentMapStyleTileSample> EnumerateStyleSamples(CurrentMapStyleProfile? styleProfile)
        => styleProfile?.Terrains
               .OrderBy(terrain => terrain.TerrainId)
               .SelectMany(terrain => terrain.Samples
                   .Concat(terrain.PureSamples)
                   .Concat(terrain.BoundarySamples)
                   .Concat(terrain.ContaminatedSamples)
                   .Where(sample => !string.IsNullOrWhiteSpace(sample.FilePath))
                   .DistinctBy(sample => sample.FilePath + "|" + sample.CellIndex + "|" + sample.IsBoundary + "|" + sample.IsContaminated)
                   .OrderBy(sample => sample.FilePath, StringComparer.OrdinalIgnoreCase)
                   .ThenBy(sample => sample.CellIndex))
           ?? Enumerable.Empty<CurrentMapStyleTileSample>();

    private static string CustomFilterFingerprint(BeautifyCustomFilterSettings? settings)
    {
        if (settings == null) return string.Empty;
        static string F(float value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        return string.Join(
            ',',
            F(settings.PhotoR),
            F(settings.PhotoG),
            F(settings.PhotoB),
            F(settings.PhotoDensity),
            F(settings.BalanceR),
            F(settings.BalanceG),
            F(settings.BalanceB),
            F(settings.Saturation),
            F(settings.Brightness),
            F(settings.Contrast),
            F(settings.HighlightCompression),
            F(settings.ShadowLift),
            F(settings.MidtoneGamma),
            settings.PreserveLuminosity);
    }

    private static string TerrainVisualProfileFingerprint(TerrainVisualProfile profile)
        => JsonSerializer.Serialize(profile);

    private static string StylePackFingerprint(TerrainStylePackManifest? stylePack)
        => stylePack == null ? string.Empty : JsonSerializer.Serialize(stylePack);

    private static string FileFingerprint(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "missing:" + path;
        var info = new FileInfo(path);
        return Path.GetFullPath(path).ToUpperInvariant() + ':' + info.Length + ':' + info.LastWriteTimeUtc.Ticks;
    }

    private static MapWorkbenchDraft CloneDraft(MapWorkbenchDraft source)
        => new()
        {
            SchemaVersion = source.SchemaVersion,
            DraftId = source.DraftId,
            BoundMapId = source.BoundMapId,
            GridWidth = source.GridWidth,
            GridHeight = source.GridHeight,
            TileSize = source.TileSize,
            BaseLayerPath = source.BaseLayerPath,
            MaterialRoot = source.MaterialRoot,
            TerrainMaterialPlan = source.TerrainMaterialPlan.Select(ClonePlanItem).ToList(),
            MapCellOverrides = source.MapCellOverrides.Select(CloneCell).ToList(),
            TerrainBaseCells = source.TerrainBaseCells.Select(CloneCell).ToList(),
            GeneratedMapCells = source.GeneratedMapCells.Select(CloneCell).ToList(),
            BuildingOverlayCells = source.BuildingOverlayCells.Select(CloneCell).ToList(),
            SceneryOverlayCells = source.SceneryOverlayCells.Select(CloneCell).ToList(),
            SceneryOverlays = source.SceneryOverlays.Select(CloneOverlay).ToList(),
            OriginalTerrainCells = source.OriginalTerrainCells.ToArray(),
            TerrainCells = source.TerrainCells.ToArray(),
            GenerationMode = source.GenerationMode,
            TerrainVisualProfile = source.TerrainVisualProfile.Clone(),
            TerrainRenderSettings = source.TerrainRenderSettings.Clone(),
            HexzmapBinding = source.HexzmapBinding == null ? null : new HexzmapBlockBinding
            {
                MapId = source.HexzmapBinding.MapId,
                DirectoryEntryIndex = source.HexzmapBinding.DirectoryEntryIndex,
                Width = source.HexzmapBinding.Width,
                Height = source.HexzmapBinding.Height,
                Source = source.HexzmapBinding.Source,
                Confidence = source.HexzmapBinding.Confidence,
                UserConfirmed = source.HexzmapBinding.UserConfirmed,
                Evidence = source.HexzmapBinding.Evidence
            },
            AutoGenerateMapFromTerrain = source.AutoGenerateMapFromTerrain,
            BeautifyGeneratedMap = source.BeautifyGeneratedMap,
            BeautifyStrength = source.BeautifyStrength,
            FeatherRadius = source.FeatherRadius,
            BeautifyFilterProfile = source.BeautifyFilterProfile,
            CustomBeautifyFilter = source.CustomBeautifyFilter?.Clone(),
            CreatedAtText = source.CreatedAtText,
            UpdatedAtText = source.UpdatedAtText
        };

    private static TerrainMaterialPlanItem ClonePlanItem(TerrainMaterialPlanItem source)
        => new()
        {
            MapId = source.MapId,
            TerrainId = source.TerrainId,
            VisualFamilyKey = source.VisualFamilyKey,
            MaterialRelativePath = source.MaterialRelativePath,
            MaterialCategory = source.MaterialCategory,
            DisplayName = source.DisplayName,
            SelectionMode = source.SelectionMode,
            MaterialRootFingerprint = source.MaterialRootFingerprint
        };

    private static MapCellOverride CloneCell(MapCellOverride source)
        => new() { Index = source.Index, MaterialRelativePath = source.MaterialRelativePath, MaterialCategory = source.MaterialCategory, DisplayName = source.DisplayName, Source = source.Source };

    private static MapSceneryOverlay CloneOverlay(MapSceneryOverlay source)
        => new()
        {
            OverlayId = source.OverlayId,
            MaterialRelativePath = source.MaterialRelativePath,
            MaterialCategory = source.MaterialCategory,
            DisplayName = source.DisplayName,
            X = source.X,
            Y = source.Y,
            Width = source.Width,
            Height = source.Height,
            RotationDegrees = source.RotationDegrees,
            ZOrder = source.ZOrder
        };

    private static TerrainRenderDiagnostics CloneDiagnostics(TerrainRenderDiagnostics source)
        => new()
        {
            Quality = source.Quality,
            StylePackId = source.StylePackId,
            Fingerprint = source.Fingerprint,
            ElapsedMilliseconds = source.ElapsedMilliseconds,
            PeakManagedBytes = source.PeakManagedBytes,
            MissingMaterialCount = source.MissingMaterialCount,
            PreservedObjectCellCount = source.PreservedObjectCellCount,
            LowConfidenceObjectCellCount = source.LowConfidenceObjectCellCount,
            FallbackCellCount = source.FallbackCellCount,
            RepeatedPatchCount = source.RepeatedPatchCount,
            Synthesis = source.Synthesis,
            Warnings = source.Warnings.ToList()
        };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            _latestRequestCancellation?.Cancel();
            _latestRequestCancellation?.Dispose();
            _latestRequestCancellation = null;
            foreach (var entry in _lru) entry.Bitmap.Dispose();
            _lru.Clear();
            _cacheByKey.Clear();
        }
    }

    private sealed record CacheEntry(string Key, Bitmap Bitmap, TerrainRenderDiagnostics Diagnostics);
}
