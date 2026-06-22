namespace CCZModStudio.Models;

public sealed class MapWorkbenchSettings
{
    public string LastMaterialRoot { get; set; } = string.Empty;
    public string LastDraftId { get; set; } = string.Empty;
    public string LastBoundMapId { get; set; } = string.Empty;
    public List<PersistedTerrainMaterialPlan> PersistedTerrainMaterialPlans { get; set; } = new();
}
