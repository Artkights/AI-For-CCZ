using System.Collections.Concurrent;
using System.Drawing;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class E5RawPaletteService
{
    private static readonly ConcurrentDictionary<string, E5RawPalette> Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly E5ImageReplaceService _e5 = new();

    public E5RawPalette Load(CczProject project)
    {
        var spaletPath = ResolveLocalSpaletPath(project);
        if (spaletPath != null)
        {
            var key = "spalet:" + BuildFileCacheKey(spaletPath);
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var colors = TryLoadSpaletPalette(spaletPath);
            if (colors.Count >= 256)
            {
                var palette = new E5RawPalette(colors, "RAW+Spalet.e5", spaletPath);
                Cache[key] = palette;
                return palette;
            }
        }

        var cleanPath = ResolveCleanPalettePath(project);
        if (cleanPath != null)
        {
            var key = "clean:" + BuildFileCacheKey(cleanPath);
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var colors = LoadCleanPalette(cleanPath);
            if (colors.Count >= 256)
            {
                var palette = new E5RawPalette(colors, "RAW+clean palette", cleanPath);
                Cache[key] = palette;
                return palette;
            }
        }

        return new E5RawPalette(Array.Empty<Color>(), "RAW grayscale", string.Empty);
    }

    public static string? ResolveLocalSpaletPath(CczProject project)
    {
        var candidates = new[]
        {
            Path.Combine(project.GameRoot, "Spalet.e5"),
            Path.Combine(project.GameRoot, "E5", "Spalet.e5"),
            Path.Combine(Directory.GetParent(project.GameRoot)?.FullName ?? project.WorkspaceRoot, "E5", "Spalet.e5")
        };

        return candidates.FirstOrDefault(path => File.Exists(path) && new FileInfo(path).Length >= 0x110 + 12);
    }

    public static string? ResolveCleanPalettePath(CczProject project)
    {
        var candidates = new[]
        {
            PortableInstallPaths.PaletteTsbPath,
            Path.Combine(project.GameRoot, "tsb")
        };

        return candidates.FirstOrDefault(path => File.Exists(path) && new FileInfo(path).Length >= 256 * 4);
    }

    private IReadOnlyList<Color> TryLoadSpaletPalette(string path)
    {
        try
        {
            foreach (var entry in _e5.ReadIndex(path))
            {
                if (entry.DecodedLength < 256 * 3) continue;

                var bytes = _e5.ReadEntryBytes(path, entry.ImageNumber);
                if (bytes.Length < 256 * 3) continue;

                var colors = new Color[256];
                for (var i = 0; i < colors.Length; i++)
                {
                    var offset = i * 3;
                    var b = bytes[offset];
                    var r = bytes[offset + 1];
                    var g = bytes[offset + 2];
                    colors[i] = Color.FromArgb(255, r, g, b);
                }

                return colors;
            }
        }
        catch
        {
            return Array.Empty<Color>();
        }

        return Array.Empty<Color>();
    }

    private static IReadOnlyList<Color> LoadCleanPalette(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 256 * 4) return Array.Empty<Color>();

        var colors = new Color[256];
        for (var i = 0; i < colors.Length; i++)
        {
            var offset = i * 4;
            var b = bytes[offset];
            var g = bytes[offset + 1];
            var r = bytes[offset + 2];
            colors[i] = Color.FromArgb(255, r, g, b);
        }

        return colors;
    }

    private static string BuildFileCacheKey(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return fullPath + "|missing";

        try
        {
            var info = new FileInfo(fullPath);
            return $"{fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return fullPath + "|unknown";
        }
    }
}

public sealed record E5RawPalette(IReadOnlyList<Color> Colors, string Mode, string Path);
