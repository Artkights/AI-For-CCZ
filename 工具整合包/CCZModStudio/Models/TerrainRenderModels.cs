using System.Drawing;
using System.Text.Json.Serialization;

namespace CCZModStudio.Models;

public enum TerrainRenderQuality
{
    Draft,
    Final
}

public enum TerrainObjectPolicy
{
    PreserveOriginal,
    PreserveWholeCellWhenUncertain,
    RedrawAll
}

public enum TerrainStyleSelectionMode
{
    Automatic,
    ManualLocked
}

public sealed class TerrainRenderSettings
{
    public bool RedrawEnabled { get; set; }
    public TerrainStyleSelectionMode StyleSelectionMode { get; set; } = TerrainStyleSelectionMode.Automatic;
    public string StylePackId { get; set; } = string.Empty;
    public string Seed { get; set; } = Guid.NewGuid().ToString("N");
    public TerrainRenderQuality PreviewQuality { get; set; } = TerrainRenderQuality.Draft;
    public TerrainRenderQuality FinalQuality { get; set; } = TerrainRenderQuality.Final;
    public TerrainObjectPolicy ObjectPolicy { get; set; } = TerrainObjectPolicy.PreserveOriginal;
    public string ToneProfile { get; set; } = TerrainToneProfiles.Neutral;
    public float ToneAmount { get; set; }
    public string LastConfirmedFinalFingerprint { get; set; } = string.Empty;

    public TerrainRenderSettings Clone()
        => new()
        {
            RedrawEnabled = RedrawEnabled,
            StyleSelectionMode = StyleSelectionMode,
            StylePackId = StylePackId,
            Seed = Seed,
            PreviewQuality = PreviewQuality,
            FinalQuality = FinalQuality,
            ObjectPolicy = ObjectPolicy,
            ToneProfile = ToneProfile,
            ToneAmount = ToneAmount,
            LastConfirmedFinalFingerprint = LastConfirmedFinalFingerprint
        };
}

public static class TerrainToneProfiles
{
    public const string Neutral = "Neutral";
    public const string Night = "Night";
    public const string Autumn = "Autumn";
    public const string Winter = "Winter";
    public const string WarmSun = "WarmSun";
    public const string Custom = "Custom";
}

public sealed class TerrainRenderRequest
{
    public MapWorkbenchDraft Draft { get; init; } = null!;
    public IReadOnlyList<MaterialAsset> Materials { get; init; } = Array.Empty<MaterialAsset>();
    public CurrentMapStyleProfile? CurrentMapStyle { get; init; }
    public TerrainStylePackManifest? StylePack { get; init; }
    public TerrainRenderQuality Quality { get; init; } = TerrainRenderQuality.Draft;
    public IReadOnlyCollection<int>? DirtyCellIndexes { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public sealed class TerrainRenderResult : IDisposable
{
    public Bitmap Bitmap { get; init; } = null!;
    public TerrainRenderDiagnostics Diagnostics { get; init; } = new();
    public string Fingerprint { get; init; } = string.Empty;

    public void Dispose() => Bitmap.Dispose();
}

public sealed class ConfirmedTerrainRenderSnapshot : IDisposable
{
    public Bitmap Bitmap { get; init; } = null!;
    public string Fingerprint { get; init; } = string.Empty;
    public TerrainRenderQuality Quality { get; init; } = TerrainRenderQuality.Final;
    public string StylePackId { get; init; } = string.Empty;
    public string StylePackVersion { get; init; } = string.Empty;
    public string MaterialRootKey { get; init; } = string.Empty;
    public string BaseImageKey { get; init; } = string.Empty;
    public string ToneProfile { get; init; } = string.Empty;
    public float ToneAmount { get; init; }
    public DateTime ConfirmedAtUtc { get; init; } = DateTime.UtcNow;

    public void Dispose() => Bitmap.Dispose();
}

public sealed class TerrainRenderDiagnostics
{
    public TerrainRenderQuality Quality { get; set; }
    public string StylePackId { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public long ElapsedMilliseconds { get; set; }
    public long PeakManagedBytes { get; set; }
    public int MissingMaterialCount { get; set; }
    public int PreservedObjectCellCount { get; set; }
    public int LowConfidenceObjectCellCount { get; set; }
    public int FallbackCellCount { get; set; }
    public int RepeatedPatchCount { get; set; }
    public TerrainVisualSynthesisDiagnostics Synthesis { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class TerrainStylePackManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1";
    public List<TerrainStylePackTerrainRule> Terrains { get; set; } = new();
    public List<TerrainStylePackTransitionRule> Transitions { get; set; } = new();
    public List<TerrainStylePackObjectRule> Objects { get; set; } = new();
    public List<TerrainStylePackTonePreset> TonePresets { get; set; } = new();
    public TerrainStyleSignature Signature { get; set; } = new();
    [JsonIgnore]
    public string SourceDirectory { get; set; } = string.Empty;
}

public sealed class TerrainStyleSignature
{
    public float LabL { get; set; }
    public float LabA { get; set; }
    public float LabB { get; set; }
    public float Saturation { get; set; }
    public float TextureFrequency { get; set; }
    public float EdgeDensity { get; set; }
}

public sealed class TerrainStylePackTerrainRule
{
    public byte TerrainId { get; set; }
    public TerrainVisualSurfaceKind SurfaceKind { get; set; } = TerrainVisualSurfaceKind.FallbackColor;
    public List<string> TextureSources { get; set; } = new();
    public bool AllowFlipX { get; set; }
    public bool AllowFlipY { get; set; }
    public bool AllowRotation90 { get; set; }
}

public sealed class TerrainStylePackTransitionRule
{
    public byte FromTerrainId { get; set; }
    public byte ToTerrainId { get; set; }
    public string Rule { get; set; } = string.Empty;
    public int FeatherPixels { get; set; } = 12;
}

public sealed class TerrainStylePackObjectRule
{
    public byte TerrainId { get; set; }
    public TerrainObjectPolicy Policy { get; set; } = TerrainObjectPolicy.PreserveOriginal;
    public float MinimumMaskConfidence { get; set; } = 0.75f;
}

public sealed class TerrainStylePackTonePreset
{
    public string Id { get; set; } = string.Empty;
    public string Profile { get; set; } = TerrainToneProfiles.Neutral;
    public float Amount { get; set; }
}
