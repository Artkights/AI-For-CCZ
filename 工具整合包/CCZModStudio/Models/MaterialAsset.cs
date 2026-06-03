namespace CCZModStudio.Models;

public sealed class MaterialAsset
{
    public string Category { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string HexTag { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public long SizeBytes { get; init; }
    public string FilePath { get; init; } = string.Empty;
}
