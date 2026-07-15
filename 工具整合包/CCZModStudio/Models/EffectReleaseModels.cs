namespace CCZModStudio.Models;

public sealed class EffectReleaseManifest
{
    public string SchemaVersion { get; set; } = "effect-release-manifest-v1";
    public string EffectCapabilitySchemaVersion { get; set; } = string.Empty;
    public string BuildChannel { get; set; } = string.Empty;
    public string BuildIdentity { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<EffectReleaseComponent> Components { get; set; } = [];
}

public sealed class EffectReleaseComponent
{
    public string ComponentId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string FileVersion { get; set; } = string.Empty;
    public string BuildIdentity { get; set; } = string.Empty;
}

public sealed class EffectReleaseConsistencyReport
{
    public bool HasReleaseManifest { get; set; }
    public bool IsConsistent { get; set; }
    public bool CanWrite => !HasReleaseManifest || IsConsistent;
    public string ManifestPath { get; set; } = string.Empty;
    public string ReleaseRoot { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string BuildChannel { get; set; } = string.Empty;
    public string BuildIdentity { get; set; } = string.Empty;
    public string StatusZh { get; set; } = string.Empty;
    public string ReasonZh { get; set; } = string.Empty;
    public List<string> WarningsZh { get; set; } = [];
    public List<EffectReleaseComponent> Components { get; set; } = [];
}
