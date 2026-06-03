namespace CCZModStudio.Models;

public sealed class ReleasePackageResult
{
    public string ReleaseRoot { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public int FilesCopied { get; init; }
    public long BytesCopied { get; init; }
    public int ChangedItems { get; init; }
    public int ModifiedItems { get; init; }
    public int AddedItems { get; init; }
    public int MissingItems { get; init; }
}
