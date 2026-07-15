namespace CCZModStudio.Models;

using System.Text.Json.Serialization;

public sealed class EffectAnalysisPerformance
{
    public double TotalMilliseconds { get; set; }
    public string CacheState { get; set; } = string.Empty;
    public Dictionary<string, long> Counters { get; set; } = new(StringComparer.Ordinal);
}

public sealed class EffectAnalysisSnapshot
{
    public string AnalysisFingerprint { get; set; } = string.Empty;
    public string TargetFilePath { get; set; } = string.Empty;
    public string FullExeSha256 { get; set; } = string.Empty;
    public long ExeLength { get; set; }
    public uint ImageBase { get; set; }
    [JsonIgnore]
    public byte[] ExecutableBytes { get; set; } = [];
    public string CacheState { get; set; } = string.Empty;
    public List<string> CompletedStages { get; set; } = [];
    public ExecutableProfileAuditResult ProfileAudit { get; set; } = new();
    public EffectInventoryReport Inventory { get; set; } = new();
    public EffectIdLocationIndex LocationIndex { get; set; } = new();
    public EngineEffectMechanismProfile MechanismProfile { get; set; } = new();
    public List<HookExecutionContract> HookContracts { get; set; } = [];
    public EffectModuleCatalog ModuleCatalog { get; set; } = new();
    public EffectReleaseConsistencyReport ReleaseConsistency { get; set; } = new();
    public EffectAnalysisPerformance Performance { get; set; } = new();
}
