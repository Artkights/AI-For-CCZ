using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class TerrainStylePackRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public IReadOnlyList<TerrainStylePackManifest> Load(string? root)
    {
        var result = new List<TerrainStylePackManifest>();
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
        {
            foreach (var path in Directory.EnumerateFiles(root, "_stylepack.json", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var manifest = JsonSerializer.Deserialize<TerrainStylePackManifest>(File.ReadAllText(path), JsonOptions);
                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id)) continue;
                    manifest.Id = manifest.Id.Trim();
                    manifest.Name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name.Trim();
                    manifest.Version = string.IsNullOrWhiteSpace(manifest.Version) ? "1" : manifest.Version.Trim();
                    manifest.SourceDirectory = Path.GetDirectoryName(path) ?? string.Empty;
                    manifest.Terrains ??= new List<TerrainStylePackTerrainRule>();
                    manifest.Transitions ??= new List<TerrainStylePackTransitionRule>();
                    manifest.Objects ??= new List<TerrainStylePackObjectRule>();
                    manifest.TonePresets ??= new List<TerrainStylePackTonePreset>();
                    result.Add(manifest);
                }
                catch (JsonException)
                {
                    // A malformed optional pack must not prevent the remaining packs from loading.
                }
            }
        }

        if (result.Count == 0)
        {
            result.Add(CreateBuiltInClassicPack());
        }

        return result
            .GroupBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public TerrainStylePackManifest MatchBest(
        IReadOnlyList<TerrainStylePackManifest> packs,
        MapWorkbenchDraft draft,
        CurrentMapStyleProfile? currentStyle)
    {
        if (packs.Count == 0) return CreateBuiltInClassicPack();
        var actual = BuildCurrentSignature(currentStyle);
        var terrainIds = draft.TerrainCells.ToHashSet();
        return packs
            .Select(pack => new
            {
                Pack = pack,
                Score = SignatureDistance(actual, pack.Signature) +
                        terrainIds.Count(id => pack.Terrains.All(rule => rule.TerrainId != id)) * 8.0
            })
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Pack.Id, StringComparer.OrdinalIgnoreCase)
            .First().Pack;
    }

    private static TerrainStyleSignature BuildCurrentSignature(CurrentMapStyleProfile? style)
    {
        var stats = style?.Terrains.Select(terrain => terrain.Stats).Where(item => item != TileVisualStats.Empty).ToList()
                    ?? new List<TileVisualStats>();
        if (stats.Count == 0) return new TerrainStyleSignature();
        var r = stats.Average(item => item.AverageR);
        var g = stats.Average(item => item.AverageG);
        var b = stats.Average(item => item.AverageB);
        var lab = RgbToLab(r, g, b);
        return new TerrainStyleSignature
        {
            LabL = lab.L,
            LabA = lab.A,
            LabB = lab.B,
            Saturation = stats.Average(item => item.Saturation),
            TextureFrequency = stats.Average(item => item.Texture),
            EdgeDensity = stats.Average(item => item.EdgeStrength)
        };
    }

    private static double SignatureDistance(TerrainStyleSignature actual, TerrainStyleSignature expected)
    {
        if (expected.LabL == 0f && expected.LabA == 0f && expected.LabB == 0f &&
            expected.Saturation == 0f && expected.TextureFrequency == 0f && expected.EdgeDensity == 0f)
        {
            return 0;
        }

        var dl = actual.LabL - expected.LabL;
        var da = actual.LabA - expected.LabA;
        var db = actual.LabB - expected.LabB;
        var ds = (actual.Saturation - expected.Saturation) * 30f;
        var dt = (actual.TextureFrequency - expected.TextureFrequency) * 20f;
        var de = (actual.EdgeDensity - expected.EdgeDensity) * 20f;
        return Math.Sqrt(dl * dl + da * da + db * db + ds * ds + dt * dt + de * de);
    }

    private static (float L, float A, float B) RgbToLab(float r, float g, float b)
    {
        static float Linear(float value)
        {
            value /= 255f;
            return value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
        }

        static float Pivot(float value) => value > 0.008856f ? MathF.Pow(value, 1f / 3f) : 7.787f * value + 16f / 116f;
        var lr = Linear(r);
        var lg = Linear(g);
        var lb = Linear(b);
        var x = Pivot((lr * 0.4124f + lg * 0.3576f + lb * 0.1805f) / 0.95047f);
        var y = Pivot(lr * 0.2126f + lg * 0.7152f + lb * 0.0722f);
        var z = Pivot((lr * 0.0193f + lg * 0.1192f + lb * 0.9505f) / 1.08883f);
        return (116f * y - 16f, 500f * (x - y), 200f * (y - z));
    }

    private static TerrainStylePackManifest CreateBuiltInClassicPack()
        => new()
        {
            Id = "ccz-classic",
            Name = "经典曹操传",
            Version = "1",
            Terrains = Enumerable.Range(0, 30)
                .Select(id => new TerrainStylePackTerrainRule
                {
                    TerrainId = (byte)id,
                    SurfaceKind = id switch
                    {
                        0 or 1 or 2 or 3 or 4 or 5 or 7 or 10 or 29 => TerrainVisualSurfaceKind.NaturalArea,
                        9 or 11 or 25 => TerrainVisualSurfaceKind.LiquidArea,
                        6 or 12 or 13 or 27 => TerrainVisualSurfaceKind.LinearTerrain,
                        8 or 14 or 15 or 17 => TerrainVisualSurfaceKind.StructureTerrain,
                        16 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 28 => TerrainVisualSurfaceKind.BuildingOverlay,
                        _ => TerrainVisualSurfaceKind.FallbackColor
                    },
                    AllowFlipX = id is 0 or 1 or 2 or 3 or 4 or 5 or 7 or 9 or 10 or 11 or 25 or 29,
                    AllowFlipY = id is 0 or 1 or 2 or 3 or 4 or 5 or 7 or 9 or 10 or 11 or 25 or 29,
                    AllowRotation90 = id is 0 or 1 or 2 or 3 or 4 or 5 or 7 or 10 or 29
                })
                .ToList()
        };
}
