namespace CCZModStudio.Models;

public sealed class MapCellOverride
{
    public int Index { get; set; }
    public string MaterialRelativePath { get; set; } = string.Empty;
    public string MaterialCategory { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
