using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// Process-wide, read-only E5 sessions.  The preview/query path must never read an
/// entire E5 archive merely to inspect its directory or one image.
/// </summary>
public sealed class E5ImageReadSessionPool
{
    public static E5ImageReadSessionPool Shared { get; } = new();

    private readonly ConcurrentDictionary<string, E5ImageReadSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    public E5ImageReadSession GetSession(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var stamp = E5ResourceFingerprint.CreateFast(fullPath);
        var key = $"{fullPath}|{stamp.Length}|{stamp.LastWriteTimeUtcTicks}|{stamp.IndexSha256}";

        foreach (var stale in _sessions.Keys.Where(candidate =>
                     candidate.StartsWith(fullPath + "|", StringComparison.OrdinalIgnoreCase) &&
                     !candidate.Equals(key, StringComparison.OrdinalIgnoreCase)))
        {
            _sessions.TryRemove(stale, out _);
        }

        return _sessions.GetOrAdd(key, _ => new E5ImageReadSession(fullPath, stamp));
    }

    public void Invalidate(IEnumerable<string> paths)
    {
        var normalized = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToArray();
        if (normalized.Length == 0) return;

        foreach (var key in _sessions.Keys)
        {
            if (normalized.Any(path => key.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)))
            {
                _sessions.TryRemove(key, out _);
            }
        }
    }

    public void Clear() => _sessions.Clear();
}

public sealed class E5ImageReadSession
{
    private const int IndexOffset = 0x110;
    private const int IndexEntrySize = 12;
    private const int LsHeaderLength = 16;
    private const int LsDictionaryLength = 256;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _readGate = new(2, 2);
    private readonly E5ResourceFingerprint _fastFingerprint;
    private IReadOnlyList<E5ReadEntry>? _index;
    private E5IndexProbeResult? _indexProbe;
    private byte[]? _dictionary;
    private string _indexSha256 = string.Empty;
    private long _bytesRead;
    private long _entryReadCount;
    private long _decodeCount;
    private readonly ConcurrentDictionary<int, Lazy<byte[]>> _decodedEntryCache = new();
    private long _decodedEntryCacheBytes;
    private const long DecodedEntryCacheLimitBytes = 32L * 1024 * 1024;

    internal E5ImageReadSession(string path, E5ResourceFingerprint fastFingerprint)
    {
        Path = path;
        _fastFingerprint = fastFingerprint;
    }

    public string Path { get; }

    public E5ResourceFingerprint Fingerprint
    {
        get
        {
            EnsureIndex();
            return _fastFingerprint with { IndexSha256 = _indexSha256 };
        }
    }

    public E5ReadMetrics Metrics => new(
        Interlocked.Read(ref _bytesRead),
        Interlocked.Read(ref _entryReadCount),
        Interlocked.Read(ref _decodeCount));

    public IReadOnlyList<E5ReadEntry> ReadIndex()
    {
        EnsureIndex();
        return _index!;
    }

    public E5IndexProbeResult ProbeIndex()
    {
        EnsureIndex();
        return _indexProbe!;
    }

    public Task<IReadOnlyList<E5ReadEntry>> ReadIndexAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(ReadIndex, cancellationToken);
    }

    public byte[] ReadDecodedEntry(int imageNumber)
    {
        var lazy = _decodedEntryCache.GetOrAdd(imageNumber, number => new Lazy<byte[]>(
            () => ReadDecodedEntryCore(number),
            LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return lazy.Value;
        }
        catch
        {
            _decodedEntryCache.TryRemove(new KeyValuePair<int, Lazy<byte[]>>(imageNumber, lazy));
            throw;
        }
    }

    private byte[] ReadDecodedEntryCore(int imageNumber)
    {
        var index = ReadIndex();
        if (imageNumber <= 0 || imageNumber > index.Count)
        {
            if (_indexProbe is { IsComplete: false } damaged)
                throw new E5ArchiveIntegrityException(damaged);
            throw new InvalidOperationException($"E5 图号越界：#{imageNumber}/{index.Count}。");
        }

        var entry = index[imageNumber - 1];
        var stored = ReadRange(entry.DataOffset, entry.StoredLength);
        Interlocked.Increment(ref _entryReadCount);
        if (!entry.IsCompressed)
        {
            AccountDecodedEntry(imageNumber, stored);
            return stored;
        }

        var dictionary = GetDictionary();
        if (!TryDecodeLsEntry(dictionary, stored, entry.DecodedLength, out var decoded))
        {
            throw new InvalidOperationException($"E5 压缩条目解码失败：图号 #{imageNumber}。");
        }

        Interlocked.Increment(ref _decodeCount);
        AccountDecodedEntry(imageNumber, decoded);
        return decoded;
    }

    public Task<byte[]> ReadDecodedEntryAsync(int imageNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => ReadDecodedEntry(imageNumber), cancellationToken);
    }

    private void EnsureIndex()
    {
        if (_index != null) return;
        lock (_gate)
        {
            if (_index != null) return;
            if (!File.Exists(Path) || _fastFingerprint.Length < IndexOffset + IndexEntrySize)
            {
                _indexProbe = new E5IndexProbeResult
                {
                    Path = Path,
                    FileLength = _fastFingerprint.Length,
                    IsComplete = false,
                    FailureReason = "The E5 archive is missing or too short."
                };
                _index = Array.Empty<E5ReadEntry>();
                return;
            }

            _indexProbe = E5IndexParser.Probe(
                _fastFingerprint.Length,
                ReadRange,
                System.IO.Path.GetFileName(Path),
                Path);
            _indexSha256 = _indexProbe.DirectorySha256;
            _index = _indexProbe.Entries.Select(entry => new E5ReadEntry(
                entry.ImageNumber,
                entry.IndexOffset,
                entry.DataOffset,
                entry.StoredLength,
                entry.DecodedLength,
                entry.Kind)).ToArray();
        }
    }

    private byte[] GetDictionary()
    {
        if (_dictionary != null) return _dictionary;
        lock (_gate)
        {
            _dictionary ??= ReadRange(LsHeaderLength, LsDictionaryLength);
            if (_dictionary.Length != LsDictionaryLength)
            {
                throw new InvalidOperationException("E5 压缩条目解码失败：文件缺少 LS 字典。");
            }

            return _dictionary;
        }
    }

    private byte[] ReadRange(long offset, int count)
    {
        if (count <= 0) return Array.Empty<byte>();
        _readGate.Wait();
        try
        {
            var bytes = new byte[count];
            using var stream = new FileStream(
                Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                options: FileOptions.RandomAccess);
            stream.Position = offset;
            stream.ReadExactly(bytes);
            Interlocked.Add(ref _bytesRead, bytes.Length);
            return bytes;
        }
        finally
        {
            _readGate.Release();
        }
    }

    private void AccountDecodedEntry(int imageNumber, byte[] bytes)
    {
        Interlocked.Add(ref _decodedEntryCacheBytes, bytes.LongLength);

        if (Interlocked.Read(ref _decodedEntryCacheBytes) <= DecodedEntryCacheLimitBytes) return;
        foreach (var key in _decodedEntryCache.Keys.OrderBy(value => value))
        {
            if (Interlocked.Read(ref _decodedEntryCacheBytes) <= DecodedEntryCacheLimitBytes / 2) break;
            if (_decodedEntryCache.TryRemove(key, out var removed) && removed.IsValueCreated)
            {
                Interlocked.Add(ref _decodedEntryCacheBytes, -removed.Value.LongLength);
            }
        }
    }

    private static bool TryDecodeLsEntry(byte[] dictionary, byte[] encoded, int decodedLength, out byte[] decoded)
    {
        decoded = new byte[decodedLength];
        if (encoded.Length == decodedLength)
        {
            Buffer.BlockCopy(encoded, 0, decoded, 0, decodedLength);
            return true;
        }

        var inputIndex = 0;
        var bitPosition = 7;
        var outputIndex = 0;
        var backDistance = 0;
        while (outputIndex < decodedLength)
        {
            if (inputIndex >= encoded.Length) return false;
            uint code = 0;
            var bitLength = 0;
            int bitSet;
            do
            {
                bitSet = (encoded[inputIndex] >> bitPosition) & 1;
                code = (code << 1) | (uint)bitSet;
                bitLength++;
                if (--bitPosition < 0) { bitPosition = 7; inputIndex++; }
            } while (bitSet != 0);

            uint mask = 0;
            while (bitLength-- > 0)
            {
                if (inputIndex >= encoded.Length) return false;
                bitSet = (encoded[inputIndex] >> bitPosition) & 1;
                mask = (mask << 1) | (uint)bitSet;
                if (--bitPosition < 0) { bitPosition = 7; inputIndex++; }
            }

            code += mask;
            if (backDistance == 0 && code >= LsDictionaryLength)
            {
                backDistance = checked((int)(code - LsDictionaryLength));
                if (backDistance == 0) return false;
                continue;
            }

            if (backDistance == 0)
            {
                if (code >= LsDictionaryLength) return false;
                decoded[outputIndex++] = dictionary[(int)code];
                continue;
            }

            var copyCount = checked((int)code + 3);
            while (copyCount-- > 0)
            {
                if (outputIndex >= decodedLength) return false;
                var source = outputIndex - backDistance;
                if (source < 0) return false;
                decoded[outputIndex++] = decoded[source];
            }
            backDistance = 0;
        }

        return true;
    }
}

public sealed record E5ReadEntry(
    int ImageNumber,
    int IndexOffset,
    int DataOffset,
    int StoredLength,
    int DecodedLength,
    string Kind)
{
    public bool IsCompressed => StoredLength != DecodedLength;
}

public sealed record E5ResourceFingerprint(long Length, long LastWriteTimeUtcTicks, string IndexSha256)
{
    public static E5ResourceFingerprint CreateFast(string path)
    {
        if (!File.Exists(path)) return new(0, 0, "missing");
        var info = new FileInfo(path);
        var indexSha = string.Empty;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
            if (stream.Length >= E5IndexParser.IndexOffset + E5IndexParser.IndexEntrySize)
            {
                Span<byte> first = stackalloc byte[E5IndexParser.IndexEntrySize];
                stream.Position = E5IndexParser.IndexOffset;
                stream.ReadExactly(first);
                var firstOffset = BinaryPrimitives.ReadUInt32BigEndian(first[8..12]);
                var directoryLength = (long)firstOffset - E5IndexParser.IndexOffset;
                var trailerLength = directoryLength % E5IndexParser.IndexEntrySize;
                if (directoryLength > 0 && directoryLength <= int.MaxValue &&
                    trailerLength is 0 or 4 &&
                    E5IndexParser.IndexOffset + directoryLength <= stream.Length)
                {
                    var directory = new byte[checked((int)directoryLength)];
                    stream.Position = E5IndexParser.IndexOffset;
                    stream.ReadExactly(directory);
                    indexSha = Convert.ToHexString(SHA256.HashData(directory));
                }
            }
        }
        catch
        {
            indexSha = "unreadable";
        }
        return new(info.Length, info.LastWriteTimeUtc.Ticks, indexSha);
    }

    public override string ToString() => $"{Length}:{LastWriteTimeUtcTicks}:{IndexSha256}";
}

public sealed record E5ReadMetrics(long BytesRead, long EntryReadCount, long DecodeCount);
