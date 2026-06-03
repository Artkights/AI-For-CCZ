namespace CCZModStudio.Models;

public sealed class MapWorkbenchMissingAsset
{
    public int Index { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public string ExpectedPath { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
