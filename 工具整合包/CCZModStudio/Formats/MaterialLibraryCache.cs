using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

/// <summary>
/// Material index cache. A cache hit is O(1); recursive verification is explicit or
/// performed after a watcher marks the root dirty, never on every lookup.
/// </summary>
public sealed class MaterialLibraryCache : IDisposable
{
    private const string ManifestVersion = "material-library-v2";
    private readonly MaterialLibraryIndexer _indexer;
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public MaterialLibraryCache(MaterialLibraryIndexer indexer)
        : this(indexer, Path.Combine(PortableInstallPaths.CacheRoot, "MaterialLibrary", "v1"))
    {
    }

    internal MaterialLibraryCache(MaterialLibraryIndexer indexer, string cacheDirectory)
    {
        _indexer = indexer;
        CacheDirectory = Path.GetFullPath(cacheDirectory);
    }

    internal string CacheDirectory { get; }

    public IReadOnlyList<MaterialAsset> GetOrIndexExplicitRoot(string? root)
    {
        var fullPath = NormalizeRoot(root);
        if (fullPath == null) return Array.Empty<MaterialAsset>();

        lock (_gate)
        {
            if (_entries.TryGetValue(fullPath, out var cached) && !cached.Dirty)
            {
                PerformanceMetrics.Increment("MaterialLibrary.MemoryHits");
                return cached.Assets;
            }

            if (!_entries.ContainsKey(fullPath) && TryLoadManifest(fullPath, out var manifestAssets))
            {
                var diskEntry = CreateEntry(fullPath, manifestAssets);
                _entries[fullPath] = diskEntry;
                PerformanceMetrics.Increment("MaterialLibrary.DiskHits");
                return diskEntry.Assets;
            }
        }

        return ForceRefresh(fullPath);
    }

    public Task<IReadOnlyList<MaterialAsset>> GetOrIndexExplicitRootAsync(string? root, CancellationToken cancellationToken = default)
        => Task.Run(() => GetOrIndexExplicitRoot(root), cancellationToken);

    public IReadOnlyList<MaterialAsset> ForceRefresh(string root)
    {
        var fullPath = NormalizeRoot(root);
        if (fullPath == null) return Array.Empty<MaterialAsset>();
        using var operation = PerformanceMetrics.Begin("MaterialLibrary.FullIndex");
        var assets = _indexer.IndexExplicitRoot(fullPath);
        var entry = CreateEntry(fullPath, assets);
        lock (_gate)
        {
            if (_entries.Remove(fullPath, out var previous)) previous.Dispose();
            _entries[fullPath] = entry;
        }
        TryWriteManifest(fullPath, assets);
        PerformanceMetrics.Increment("MaterialLibrary.LiveGenerations");
        return assets;
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Values) entry.Dispose();
            _entries.Clear();
        }
    }

    public void Dispose() => Clear();

    private CacheEntry CreateEntry(string root, IReadOnlyList<MaterialAsset> assets)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            void Dirty(object? _, FileSystemEventArgs __) => MarkDirty(root);
            void Renamed(object? _, RenamedEventArgs __) => MarkDirty(root);
            watcher.Changed += Dirty;
            watcher.Created += Dirty;
            watcher.Deleted += Dirty;
            watcher.Renamed += Renamed;
            watcher.Error += (_, _) => MarkDirty(root);
        }
        catch
        {
            watcher?.Dispose();
            watcher = null;
        }
        return new CacheEntry(assets, watcher);
    }

    private void MarkDirty(string root)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(root, out var entry)) entry.Dirty = true;
        }
        PerformanceMetrics.Increment("MaterialLibrary.WatcherInvalidations");
    }

    private static string? NormalizeRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        try
        {
            var fullPath = Path.GetFullPath(root);
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch { return null; }
    }

    private string GetManifestPath(string root)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
        return Path.Combine(CacheDirectory, hash + ".json");
    }

    private bool TryLoadManifest(string root, out IReadOnlyList<MaterialAsset> assets)
    {
        assets = Array.Empty<MaterialAsset>();
        try
        {
            var path = GetManifestPath(root);
            if (!File.Exists(path)) return false;
            var document = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path));
            if (document?.Version != ManifestVersion || !document.Root.Equals(root, StringComparison.OrdinalIgnoreCase)) return false;
            if (document.Assets.Any(asset => !File.Exists(asset.FilePath))) return false;
            assets = document.Assets;
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            if (document.Files == null || document.Files.Length == 0)
                MarkDirty(root);
            else
                _ = Task.Run(() => VerifyManifest(root, document));
            return true;
        }
        catch { return false; }
    }

    private void TryWriteManifest(string root, IReadOnlyList<MaterialAsset> assets)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var path = GetManifestPath(root);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var files = assets.Select(asset => asset.FilePath).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    return new ManifestFile(path, info.Exists ? info.Length : -1, info.Exists ? info.LastWriteTimeUtc.Ticks : 0);
                }).ToArray();
            File.WriteAllText(temp, JsonSerializer.Serialize(new Manifest(ManifestVersion, root, assets.ToArray(), files)));
            File.Move(temp, path, overwrite: true);
        }
        catch { }
    }

    private sealed class CacheEntry(IReadOnlyList<MaterialAsset> assets, FileSystemWatcher? watcher) : IDisposable
    {
        public IReadOnlyList<MaterialAsset> Assets { get; } = assets;
        public FileSystemWatcher? Watcher { get; } = watcher;
        public bool Dirty { get; set; }
        public void Dispose() => Watcher?.Dispose();
    }

    private void VerifyManifest(string root, Manifest manifest)
    {
        try
        {
            foreach (var file in manifest.Files ?? [])
            {
                var info = new FileInfo(file.Path);
                if (!info.Exists || info.Length != file.Length || info.LastWriteTimeUtc.Ticks != file.LastWriteTimeUtcTicks)
                {
                    MarkDirty(root);
                    return;
                }
            }
        }
        catch { MarkDirty(root); }
    }

    private sealed record Manifest(string Version, string Root, MaterialAsset[] Assets, ManifestFile[]? Files);
    private sealed record ManifestFile(string Path, long Length, long LastWriteTimeUtcTicks);
}
