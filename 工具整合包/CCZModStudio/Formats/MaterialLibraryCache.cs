using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class MaterialLibraryCache
{
    private readonly MaterialLibraryIndexer _indexer;
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public MaterialLibraryCache(MaterialLibraryIndexer indexer)
    {
        _indexer = indexer;
    }

    public IReadOnlyList<MaterialAsset> GetOrIndexExplicitRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return Array.Empty<MaterialAsset>();

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(root);
        }
        catch
        {
            return Array.Empty<MaterialAsset>();
        }

        if (!Directory.Exists(fullPath)) return Array.Empty<MaterialAsset>();

        var fingerprint = MaterialLibraryFingerprint.Create(fullPath);
        if (_entries.TryGetValue(fullPath, out var cached) && cached.Fingerprint.Equals(fingerprint))
        {
            return cached.Assets;
        }

        var assets = _indexer.IndexExplicitRoot(fullPath);
        _entries[fullPath] = new CacheEntry(fingerprint, assets);
        return assets;
    }

    public void Clear()
    {
        _entries.Clear();
    }

    private sealed record CacheEntry(MaterialLibraryFingerprint Fingerprint, IReadOnlyList<MaterialAsset> Assets);

    private readonly record struct MaterialLibraryFingerprint(int FileCount, long TotalBytes, long LastWriteTicks)
    {
        public static MaterialLibraryFingerprint Create(string root)
        {
            var fileCount = 0;
            long totalBytes = 0;
            long lastWriteTicks = 0;

            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(path);
                        if (!info.Exists) continue;
                        fileCount++;
                        totalBytes += info.Length;
                        lastWriteTicks = Math.Max(lastWriteTicks, info.LastWriteTimeUtc.Ticks);
                    }
                    catch
                    {
                        // Ignore files that disappear during indexing; the next call will refresh if needed.
                    }
                }
            }
            catch
            {
                // Treat unreadable roots as stable empty fingerprints and let the indexer return what it can.
            }

            return new MaterialLibraryFingerprint(fileCount, totalBytes, lastWriteTicks);
        }
    }
}
