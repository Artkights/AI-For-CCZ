using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace CCZModStudio.Core;

public enum ImagePreviewCacheSource
{
    Generated,
    Memory,
    Disk
}

public sealed record ImagePreviewCacheResult(byte[] Bytes, ImagePreviewCacheSource Source);

/// <summary>
/// Shared two-level cache for immutable preview PNG/frame bytes.  UI callers
/// always decode a private Bitmap and therefore never share disposable GDI objects.
/// </summary>
public sealed class ImagePreviewCache
{
    private const long MemoryLimitBytes = 128L * 1024 * 1024;
    private const long DiskLimitBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan DiskMaxAge = TimeSpan.FromDays(30);

    public static ImagePreviewCache Shared { get; } = new();

    private readonly ConcurrentDictionary<string, CacheItem> _memory = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<ImagePreviewCacheResult?>>> _inflight = new(StringComparer.Ordinal);
    private readonly object _trimGate = new();
    private long _memoryBytes;
    private int _cleanupStarted;

    public ImagePreviewCache()
        : this(Path.Combine(PortableInstallPaths.CacheRoot, "ImagePreview", "v1"))
    {
    }

    internal ImagePreviewCache(string cacheDirectory)
    {
        CacheDirectory = Path.GetFullPath(cacheDirectory);
    }

    public string CacheDirectory { get; }

    public async Task<ImagePreviewCacheResult?> GetOrCreateAsync(
        string key,
        Func<Task<byte[]?>> factory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_memory.TryGetValue(key, out var memory))
        {
            memory.Touch();
            return new(memory.Bytes, ImagePreviewCacheSource.Memory);
        }

        var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<ImagePreviewCacheResult?>>(
            () => LoadOrCreateCoreAsync(key, factory),
            LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompleted)
            {
                _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<ImagePreviewCacheResult?>>>(key, lazy));
            }
        }
    }

    public void Invalidate(IEnumerable<string> resourcePaths)
    {
        var tokens = resourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path).ToUpperInvariant())
            .ToArray();
        if (tokens.Length == 0) return;

        foreach (var pair in _memory)
        {
            if (!tokens.Any(token => pair.Value.ResourceIdentity.Contains(token, StringComparison.OrdinalIgnoreCase))) continue;
            if (_memory.TryRemove(pair.Key, out var removed)) Interlocked.Add(ref _memoryBytes, -removed.Bytes.LongLength);
        }

        foreach (var pair in _inflight)
        {
            if (tokens.Any(token => pair.Key.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                _inflight.TryRemove(pair.Key, out _);
            }
        }

        // Fingerprints already prevent stale disk hits.  Delete matching metadata in
        // the background as best effort so explicit in-tool writes free old space early.
        _ = Task.Run(() => DeleteDiskEntries(tokens));
    }

    public void InvalidateKeyPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return;

        foreach (var pair in _memory)
        {
            if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (_memory.TryRemove(pair.Key, out var removed))
                Interlocked.Add(ref _memoryBytes, -removed.Bytes.LongLength);
        }

        foreach (var key in _inflight.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            _inflight.TryRemove(key, out _);
        }

        _ = Task.Run(() => DeleteDiskEntriesByPrefix(prefix));
    }

    public void ClearMemory()
    {
        _memory.Clear();
        _inflight.Clear();
        Interlocked.Exchange(ref _memoryBytes, 0);
    }

    public void ClearDisk()
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;
            foreach (var path in Directory.EnumerateFiles(CacheDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                TryDelete(path);
            }
        }
        catch
        {
        }
    }

    private async Task<ImagePreviewCacheResult?> LoadOrCreateCoreAsync(string key, Func<Task<byte[]?>> factory)
    {
        StartDiskCleanupOnce();
        var disk = TryReadDisk(key);
        if (disk != null)
        {
            AddMemory(key, disk, key);
            return new(disk, ImagePreviewCacheSource.Disk);
        }

        var generated = await factory().ConfigureAwait(false);
        if (generated == null || generated.Length == 0) return null;
        AddMemory(key, generated, key);
        _ = Task.Run(() => TryWriteDisk(key, generated));
        return new(generated, ImagePreviewCacheSource.Generated);
    }

    private void AddMemory(string key, byte[] bytes, string identity)
    {
        var item = new CacheItem(bytes, identity);
        if (_memory.TryGetValue(key, out var prior))
        {
            Interlocked.Add(ref _memoryBytes, -prior.Bytes.LongLength);
        }
        _memory[key] = item;
        Interlocked.Add(ref _memoryBytes, bytes.LongLength);
        TrimMemory();
    }

    private void TrimMemory()
    {
        if (Interlocked.Read(ref _memoryBytes) <= MemoryLimitBytes) return;
        lock (_trimGate)
        {
            foreach (var pair in _memory.OrderBy(pair => pair.Value.LastAccessUtcTicks))
            {
                if (Interlocked.Read(ref _memoryBytes) <= MemoryLimitBytes) break;
                if (_memory.TryRemove(pair.Key, out var removed))
                {
                    Interlocked.Add(ref _memoryBytes, -removed.Bytes.LongLength);
                }
            }
        }
    }

    private byte[]? TryReadDisk(string key)
    {
        try
        {
            var path = GetDataPath(key);
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) { File.Delete(path); return null; }
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    private void TryWriteDisk(string key, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var path = GetDataPath(key);
            var metadataPath = Path.ChangeExtension(path, ".key");
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            File.WriteAllText(metadataPath, key, Encoding.UTF8);
        }
        catch
        {
            // Persistent cache is optional; memory and live rendering remain usable.
        }
    }

    private string GetDataPath(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(CacheDirectory, hash + ".png");
    }

    private void StartDiskCleanupOnce()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0) return;
        _ = Task.Run(CleanupDisk);
    }

    private void CleanupDisk()
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;
            var files = new DirectoryInfo(CacheDirectory)
                .EnumerateFiles("*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.LastAccessTimeUtc)
                .ToArray();
            var total = files.Sum(file => file.Length);
            var cutoff = DateTime.UtcNow - DiskMaxAge;
            foreach (var file in files)
            {
                if (file.LastAccessTimeUtc >= cutoff && total <= DiskLimitBytes) continue;
                total -= file.Length;
                TryDelete(file.FullName);
                TryDelete(Path.ChangeExtension(file.FullName, ".key"));
            }
        }
        catch
        {
        }
    }

    private void DeleteDiskEntries(IReadOnlyList<string> pathTokens)
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;
            foreach (var metadataPath in Directory.EnumerateFiles(CacheDirectory, "*.key"))
            {
                string key;
                try { key = File.ReadAllText(metadataPath, Encoding.UTF8); }
                catch { continue; }
                if (!pathTokens.Any(token => key.Contains(token, StringComparison.OrdinalIgnoreCase))) continue;
                TryDelete(Path.ChangeExtension(metadataPath, ".png"));
                TryDelete(metadataPath);
            }
        }
        catch
        {
        }
    }

    private void DeleteDiskEntriesByPrefix(string prefix)
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;
            foreach (var metadataPath in Directory.EnumerateFiles(CacheDirectory, "*.key"))
            {
                string key;
                try { key = File.ReadAllText(metadataPath, Encoding.UTF8); }
                catch { continue; }
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                TryDelete(Path.ChangeExtension(metadataPath, ".png"));
                TryDelete(metadataPath);
            }
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private sealed class CacheItem
    {
        private long _lastAccessUtcTicks = DateTime.UtcNow.Ticks;
        public CacheItem(byte[] bytes, string resourceIdentity) { Bytes = bytes; ResourceIdentity = resourceIdentity; }
        public byte[] Bytes { get; }
        public string ResourceIdentity { get; }
        public long LastAccessUtcTicks => Interlocked.Read(ref _lastAccessUtcTicks);
        public void Touch() => Interlocked.Exchange(ref _lastAccessUtcTicks, DateTime.UtcNow.Ticks);
    }
}
