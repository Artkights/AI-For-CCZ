using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CCZModStudio.Core;

public sealed class ItemIconPreviewService
{
    private readonly Dictionary<string, IReadOnlyList<BitmapResource>> _bitmapResourceCache = new(StringComparer.OrdinalIgnoreCase);

    public ItemIconPreviewResult BuildPreview(CczProject project, int iconIndex, int canvasSize = 96)
        => BuildPreview(project, iconIndex, "Itemicon.dll", "物品图标", canvasSize);

    public ItemIconPreviewResult BuildPreview(
        CczProject project,
        int iconIndex,
        string resourceFileName,
        string displayName,
        int canvasSize = 96)
    {
        var sourcePath = ResolveIconDll(project, resourceFileName);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return new ItemIconPreviewResult(
                sourcePath ?? Path.Combine(project.GameRoot, resourceFileName),
                iconIndex,
                0,
                null,
                $"未找到 {resourceFileName}，暂无法显示{displayName}。");
        }

        var extractIconCount = GetIconCount(sourcePath);
        if (extractIconCount > 0)
        {
            if (iconIndex < 0 || iconIndex >= extractIconCount)
            {
                return new ItemIconPreviewResult(
                    sourcePath,
                    iconIndex,
                    extractIconCount,
                    null,
                    $"{displayName}编号 {iconIndex} 超出 {resourceFileName} 可枚举范围 0-{extractIconCount - 1}。");
            }

            var iconBitmap = ExtractIconBitmap(sourcePath, iconIndex, canvasSize);
            var iconMessage = iconBitmap == null
                ? $"{displayName}编号 {iconIndex} 在 {resourceFileName} 中枚举到，但提取图像失败。"
                : $"来源 {resourceFileName}；字段图标={iconIndex}；可枚举图标={extractIconCount}。当前按 Windows 图标资源顺序预览，最终对应关系仍建议结合旧工具/实机确认。";
            return new ItemIconPreviewResult(sourcePath, iconIndex, extractIconCount, iconBitmap, iconMessage);
        }

        var bitmapResources = GetBitmapResources(sourcePath);
        var bitmapIconCount = EstimateBitmapIconCount(bitmapResources);
        if (bitmapIconCount <= 0)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                0,
                null,
                $"{resourceFileName} 未能枚举到标准图标资源；也未解析到 RT_BITMAP 候选图标。");
        }

        if (iconIndex < 0 || iconIndex >= bitmapIconCount)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                bitmapIconCount,
                null,
                $"{displayName}编号 {iconIndex} 超出 {resourceFileName} RT_BITMAP 候选范围 0-{bitmapIconCount - 1}。");
        }

        var resource = ResolveBitmapResource(bitmapResources, iconIndex);
        if (resource == null)
        {
            return new ItemIconPreviewResult(
                sourcePath,
                iconIndex,
                bitmapIconCount,
                null,
                $"{displayName}编号 {iconIndex} 没有匹配的 {resourceFileName} RT_BITMAP 候选资源。");
        }

        var bitmap = RenderDib(resource.DibBytes, canvasSize);
        var message = bitmap == null
            ? $"{displayName}编号 {iconIndex} 匹配到 {resourceFileName} RT_BITMAP 资源 ID={resource.Id}，但 DIB 转图像失败。"
            : $"来源 {resourceFileName}；字段图标={iconIndex}；RT_BITMAP 资源ID={resource.Id}；候选图标数={bitmapIconCount}。当前按资源ID成对规则预览，最终对应关系仍建议结合旧工具/实机确认。";
        return new ItemIconPreviewResult(sourcePath, iconIndex, bitmapIconCount, bitmap, message);
    }

    public int GetIconCount(CczProject project)
    {
        var sourcePath = ResolveItemIconDll(project);
        return string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)
            ? 0
            : GetIconCount(sourcePath);
    }

    private static string ResolveItemIconDll(CczProject project)
        => ResolveIconDll(project, "Itemicon.dll");

    private static string ResolveIconDll(CczProject project, string resourceFileName)
    {
        var candidates = new[]
        {
            Path.Combine(project.GameRoot, resourceFileName),
            Path.Combine(project.WorkspaceRoot, resourceFileName)
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static int GetIconCount(string sourcePath)
    {
        try
        {
            return checked((int)ExtractIconEx(sourcePath, -1, null, null, 0));
        }
        catch
        {
            return 0;
        }
    }

    private static Bitmap? ExtractIconBitmap(string sourcePath, int iconIndex, int canvasSize)
    {
        var large = new IntPtr[1];
        var small = new IntPtr[1];
        try
        {
            var extracted = ExtractIconEx(sourcePath, iconIndex, large, small, 1);
            if (extracted == 0) return null;

            var handle = large[0] != IntPtr.Zero ? large[0] : small[0];
            if (handle == IntPtr.Zero) return null;

            using var icon = (Icon)Icon.FromHandle(handle).Clone();
            using var raw = icon.ToBitmap();
            var targetSize = Math.Max(32, canvasSize);
            var canvas = new Bitmap(targetSize, targetSize);
            using var g = Graphics.FromImage(canvas);
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            var scale = Math.Min((targetSize - 12) / (float)raw.Width, (targetSize - 12) / (float)raw.Height);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) scale = 1;
            var width = Math.Max(1, (int)Math.Round(raw.Width * scale));
            var height = Math.Max(1, (int)Math.Round(raw.Height * scale));
            var x = (targetSize - width) / 2;
            var y = (targetSize - height) / 2;
            g.DrawImage(raw, new Rectangle(x, y, width, height));
            return canvas;
        }
        finally
        {
            if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
            if (small[0] != IntPtr.Zero && small[0] != large[0]) DestroyIcon(small[0]);
        }
    }

    private IReadOnlyList<BitmapResource> GetBitmapResources(string sourcePath)
    {
        if (_bitmapResourceCache.TryGetValue(sourcePath, out var cached)) return cached;
        var parsed = ParseBitmapResources(sourcePath);
        _bitmapResourceCache[sourcePath] = parsed;
        return parsed;
    }

    private static int EstimateBitmapIconCount(IReadOnlyList<BitmapResource> resources)
    {
        if (resources.Count == 0) return 0;
        var minId = resources.Min(x => x.Id);
        var maxId = resources.Max(x => x.Id);
        if (minId >= 100 && resources.Count >= 2)
        {
            return ((maxId - minId) / 2) + 1;
        }

        return resources.Count;
    }

    private static BitmapResource? ResolveBitmapResource(IReadOnlyList<BitmapResource> resources, int iconIndex)
    {
        if (resources.Count == 0) return null;
        var minId = resources.Min(x => x.Id);
        if (minId >= 100)
        {
            var preferredLargeId = minId + iconIndex * 2 + 1;
            var preferredSmallId = minId + iconIndex * 2;
            return resources.FirstOrDefault(x => x.Id == preferredLargeId)
                   ?? resources.FirstOrDefault(x => x.Id == preferredSmallId);
        }

        return iconIndex < resources.Count ? resources[iconIndex] : null;
    }

    private static Bitmap? RenderDib(byte[] dibBytes, int canvasSize)
    {
        if (dibBytes.Length < 40) return null;
        var dibHeaderSize = BitConverter.ToInt32(dibBytes, 0);
        if (dibHeaderSize <= 0 || dibHeaderSize > dibBytes.Length) return null;
        var bitCount = BitConverter.ToUInt16(dibBytes, 14);
        var compression = dibBytes.Length >= 20 ? BitConverter.ToInt32(dibBytes, 16) : 0;
        var colorUsed = dibBytes.Length >= 36 ? BitConverter.ToInt32(dibBytes, 32) : 0;
        var paletteEntries = bitCount <= 8
            ? (colorUsed > 0 ? colorUsed : 1 << bitCount)
            : 0;
        var masksBytes = dibHeaderSize == 40 && compression == 3 ? 12 : 0;
        var pixelOffset = 14 + dibHeaderSize + masksBytes + paletteEntries * 4;
        var bmpBytes = new byte[14 + dibBytes.Length];
        bmpBytes[0] = (byte)'B';
        bmpBytes[1] = (byte)'M';
        BitConverter.GetBytes(bmpBytes.Length).CopyTo(bmpBytes, 2);
        BitConverter.GetBytes(pixelOffset).CopyTo(bmpBytes, 10);
        Buffer.BlockCopy(dibBytes, 0, bmpBytes, 14, dibBytes.Length);

        try
        {
            using var stream = new MemoryStream(bmpBytes);
            using var raw = new Bitmap(stream);
            return RenderBitmapToCanvas(raw, canvasSize);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap RenderBitmapToCanvas(Bitmap raw, int canvasSize)
    {
        var targetSize = Math.Max(32, canvasSize);
        var canvas = new Bitmap(targetSize, targetSize);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        var scale = Math.Min((targetSize - 12) / (float)raw.Width, (targetSize - 12) / (float)raw.Height);
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) scale = 1;
        var width = Math.Max(1, (int)Math.Round(raw.Width * scale));
        var height = Math.Max(1, (int)Math.Round(raw.Height * scale));
        var x = (targetSize - width) / 2;
        var y = (targetSize - height) / 2;
        g.DrawImage(raw, new Rectangle(x, y, width, height));
        return canvas;
    }

    private static IReadOnlyList<BitmapResource> ParseBitmapResources(string sourcePath)
    {
        try
        {
            var data = File.ReadAllBytes(sourcePath);
            if (data.Length < 0x40 || data[0] != 'M' || data[1] != 'Z') return Array.Empty<BitmapResource>();
            var peOffset = BitConverter.ToInt32(data, 0x3C);
            if (peOffset <= 0 || peOffset + 248 >= data.Length) return Array.Empty<BitmapResource>();
            var sectionCount = BitConverter.ToUInt16(data, peOffset + 6);
            var optionalHeaderSize = BitConverter.ToUInt16(data, peOffset + 20);
            var optionalHeaderOffset = peOffset + 24;
            var resourceRva = BitConverter.ToInt32(data, optionalHeaderOffset + 96 + 2 * 8);
            if (resourceRva <= 0) return Array.Empty<BitmapResource>();
            var sectionOffset = optionalHeaderOffset + optionalHeaderSize;
            var sections = new List<PeSection>();
            for (var i = 0; i < sectionCount; i++)
            {
                var offset = sectionOffset + i * 40;
                if (offset + 40 > data.Length) break;
                sections.Add(new PeSection(
                    BitConverter.ToInt32(data, offset + 12),
                    Math.Max(BitConverter.ToInt32(data, offset + 8), BitConverter.ToInt32(data, offset + 16)),
                    BitConverter.ToInt32(data, offset + 20)));
            }

            var resourceBaseOffset = RvaToFileOffset(resourceRva, sections);
            if (resourceBaseOffset < 0 || resourceBaseOffset + 16 > data.Length) return Array.Empty<BitmapResource>();
            var result = new List<BitmapResource>();
            ReadResourceDirectory(data, sections, resourceBaseOffset, resourceBaseOffset, 0, new List<int>(), result);
            return result
                .Where(x => IsLikelyBitmapDib(x.DibBytes))
                .OrderBy(x => x.Id)
                .ToList();
        }
        catch
        {
            return Array.Empty<BitmapResource>();
        }
    }

    private static void ReadResourceDirectory(
        byte[] data,
        IReadOnlyList<PeSection> sections,
        int resourceBaseOffset,
        int directoryOffset,
        int level,
        List<int> path,
        List<BitmapResource> output)
    {
        if (directoryOffset < 0 || directoryOffset + 16 > data.Length || level > 3) return;
        var namedCount = BitConverter.ToUInt16(data, directoryOffset + 12);
        var idCount = BitConverter.ToUInt16(data, directoryOffset + 14);
        var entryCount = namedCount + idCount;
        var entriesOffset = directoryOffset + 16;
        for (var i = 0; i < entryCount; i++)
        {
            var entryOffset = entriesOffset + i * 8;
            if (entryOffset + 8 > data.Length) return;
            var nameRaw = BitConverter.ToInt32(data, entryOffset);
            var valueRaw = BitConverter.ToInt32(data, entryOffset + 4);
            var nameIsString = (nameRaw & unchecked((int)0x80000000)) != 0;
            if (nameIsString) continue;
            var id = nameRaw & 0x7FFFFFFF;
            var valueOffset = valueRaw & 0x7FFFFFFF;
            var isDirectory = (valueRaw & unchecked((int)0x80000000)) != 0;
            if (isDirectory)
            {
                path.Add(id);
                ReadResourceDirectory(data, sections, resourceBaseOffset, resourceBaseOffset + valueOffset, level + 1, path, output);
                path.RemoveAt(path.Count - 1);
                continue;
            }

            if (path.Count < 2 || path[0] != 2) continue; // RT_BITMAP
            var dataEntryOffset = resourceBaseOffset + valueOffset;
            if (dataEntryOffset + 16 > data.Length) continue;
            var dataRva = BitConverter.ToInt32(data, dataEntryOffset);
            var size = BitConverter.ToInt32(data, dataEntryOffset + 4);
            var fileOffset = RvaToFileOffset(dataRva, sections);
            if (fileOffset < 0 || size <= 0 || fileOffset + size > data.Length) continue;
            var bytes = new byte[size];
            Buffer.BlockCopy(data, fileOffset, bytes, 0, size);
            output.Add(new BitmapResource(path[1], bytes));
        }
    }

    private static bool IsLikelyBitmapDib(byte[] bytes)
    {
        if (bytes.Length < 40) return false;
        var headerSize = BitConverter.ToInt32(bytes, 0);
        var width = BitConverter.ToInt32(bytes, 4);
        var height = BitConverter.ToInt32(bytes, 8);
        var planes = BitConverter.ToUInt16(bytes, 12);
        var bitCount = BitConverter.ToUInt16(bytes, 14);
        return headerSize is 12 or 40 or 108 or 124
               && width > 0
               && height > 0
               && planes == 1
               && bitCount is 1 or 4 or 8 or 16 or 24 or 32;
    }

    private static int RvaToFileOffset(int rva, IReadOnlyList<PeSection> sections)
    {
        foreach (var section in sections)
        {
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.Size)
            {
                return section.RawPointer + (rva - section.VirtualAddress);
            }
        }

        return -1;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

internal sealed record PeSection(int VirtualAddress, int Size, int RawPointer);

internal sealed record BitmapResource(int Id, byte[] DibBytes);

public sealed record ItemIconPreviewResult(
    string SourcePath,
    int IconIndex,
    int AvailableIconCount,
    Bitmap? Bitmap,
    string Message);
