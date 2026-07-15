using System.Buffers.Binary;
using System.Drawing;
using System.Security.Cryptography;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EditableImageStorageService
{
    private static readonly Color DefaultGameMagenta = Color.FromArgb(247, 0, 255);
    private readonly E5ImageReplaceService _e5 = new();

    public EditableImageStorageInfo Inspect(EditableImageTarget target)
    {
        if (target.Kind == EditableImageTargetKind.DllBitmapIcon)
        {
            return new EditableImageStorageInfo
            {
                Kind = EditableImageStorageKind.DllBitmap,
                CanEdit = true
            };
        }

        var entries = _e5.ReadIndex(target.TargetPath);
        if (target.ImageNumber <= 0 || target.ImageNumber > entries.Count)
            throw new InvalidOperationException($"E5 图号越界：#{target.ImageNumber}/{entries.Count}。");
        var entry = entries[target.ImageNumber - 1];
        var stored = _e5.ReadStoredEntryBytes(target.TargetPath, target.ImageNumber);
        var decoded = _e5.ReadEntryBytes(target.TargetPath, target.ImageNumber);
        var common = new StorageCommon(
            entry.StoredLength,
            entry.DecodedLength,
            entry.DataOffset,
            Convert.ToHexString(SHA256.HashData(stored)),
            Convert.ToHexString(stored.AsSpan(0, Math.Min(16, stored.Length))));

        if (entry.IsCompressed)
        {
            var dimensions = TryReadImageDimensions(decoded);
            return Build(common, EditableImageStorageKind.Ls12, dimensions.Width, dimensions.Height,
                canEdit: false,
                "当前为 LS12 压缩条目，项目缺少经过验证的可靠回写编码器；允许查看和导出，但不能像素保存。");
        }

        if (entry.Kind.Equals("RAW", StringComparison.OrdinalIgnoreCase))
        {
            var spec = EditableImageCodecService.TryResolveRawFrameSpec(target.TargetPath);
            if (spec == null)
            {
                return Build(common, EditableImageStorageKind.Raw, 0, 0, false,
                    "RAW 条目不属于已知 R/S 帧条，不能安全写回。");
            }

            var probe = RsStripLayoutService.Probe(target.TargetPath, entry.Kind, decoded);
            var width = probe.DecodedWidth;
            var height = probe.DecodedHeight;
            var valid = probe.IsSupportedLayout;
            return Build(common, EditableImageStorageKind.Raw, width, height, valid,
                valid ? string.Empty : probe.Detail);
        }

        if (entry.Kind.Equals("PNG", StringComparison.OrdinalIgnoreCase))
        {
            var dimensions = TryReadImageDimensions(decoded);
            var valid = dimensions.Width > 0 && dimensions.Height > 0;
            return Build(common, EditableImageStorageKind.Png, dimensions.Width, dimensions.Height, valid,
                valid ? string.Empty : "PNG 条目无法完整解码，不能安全写回。");
        }

        if (entry.Kind.Equals("JPG", StringComparison.OrdinalIgnoreCase))
        {
            var dimensions = TryReadImageDimensions(decoded);
            return Build(common, EditableImageStorageKind.Jpeg, dimensions.Width, dimensions.Height, false,
                "当前为 JPG 条目；重新编码会产生不可逆损失、背景键扩散和不可预测的体积变化。请使用专门导入流程或原始素材。",
                DetectStandardImageBackgroundKey(decoded));
        }

        if (entry.Kind.Equals("BMP", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadBmp24(decoded, out var bmp, out var reason))
            {
                return new EditableImageStorageInfo
                {
                    Kind = EditableImageStorageKind.Unknown,
                    OriginalStoredLength = common.StoredLength,
                    OriginalDecodedLength = common.DecodedLength,
                    DataOffset = common.DataOffset,
                    EntrySha256 = common.Sha256,
                    HeaderHex = common.HeaderHex,
                    ReadOnlyReason = reason,
                    CanEdit = false
                };
            }

            return new EditableImageStorageInfo
            {
                Kind = EditableImageStorageKind.Bmp24,
                OriginalStoredLength = common.StoredLength,
                OriginalDecodedLength = common.DecodedLength,
                DataOffset = common.DataOffset,
                EntrySha256 = common.Sha256,
                HeaderHex = common.HeaderHex,
                Width = bmp.Width,
                Height = bmp.Height,
                BmpBitsPerPixel = bmp.BitsPerPixel,
                BmpCompression = bmp.Compression,
                BmpTopDown = bmp.TopDown,
                BackgroundKeyColor = DetectBmpBackgroundKey(decoded, bmp),
                CanEdit = true
            };
        }

        var unknownDimensions = TryReadImageDimensions(decoded);
        return Build(common, EditableImageStorageKind.Unknown, unknownDimensions.Width, unknownDimensions.Height, false,
            $"未知 E5 图片格式；头部 {common.HeaderHex}，stored={common.StoredLength:N0}，decoded={common.DecodedLength:N0}。已保持只读。");
    }

    public byte[] EncodeBmp24PreservingContainer(EditableImageStorageInfo storage, byte[] originalBmp, Bitmap bitmap)
    {
        if (storage.Kind != EditableImageStorageKind.Bmp24 || !storage.CanEdit)
            throw new InvalidOperationException("当前条目不是可安全写回的 24 位 BI_RGB BMP。");
        if (!TryReadBmp24(originalBmp, out var bmp, out var reason)) throw new InvalidOperationException(reason);
        if (bitmap.Width != bmp.Width || bitmap.Height != bmp.Height)
            throw new InvalidOperationException($"BMP 尺寸必须保持 {bmp.Width}x{bmp.Height}，当前为 {bitmap.Width}x{bitmap.Height}。");

        var output = originalBmp.ToArray();
        var key = storage.BackgroundKeyColor;
        for (var y = 0; y < bmp.Height; y++)
        {
            var storedRow = bmp.TopDown ? y : bmp.Height - 1 - y;
            var rowOffset = checked(bmp.PixelOffset + storedRow * bmp.Stride);
            for (var x = 0; x < bmp.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A == 0) color = key;
                var offset = rowOffset + x * 3;
                output[offset] = color.B;
                output[offset + 1] = color.G;
                output[offset + 2] = color.R;
            }
        }
        return output;
    }

    public byte[] EncodeBmp24PreservingUnchangedPixels(
        EditableImageStorageInfo storage,
        byte[] originalBmp,
        Bitmap bitmap,
        IReadOnlyList<int> originalArgbPixels)
    {
        if (storage.Kind != EditableImageStorageKind.Bmp24 || !storage.CanEdit)
            throw new InvalidOperationException("The entry is not a supported 24-bit BI_RGB BMP.");
        if (!TryReadBmp24(originalBmp, out var bmp, out var reason))
            throw new InvalidOperationException(reason);
        if (bitmap.Width != bmp.Width || bitmap.Height != bmp.Height)
            throw new InvalidOperationException($"BMP dimensions must remain {bmp.Width}x{bmp.Height}.");
        if (originalArgbPixels.Count != checked(bmp.Width * bmp.Height))
            throw new InvalidOperationException("BMP source snapshot pixel count does not match the image dimensions.");

        var output = originalBmp.ToArray();
        var key = storage.BackgroundKeyColor;
        for (var y = 0; y < bmp.Height; y++)
        {
            var storedRow = bmp.TopDown ? y : bmp.Height - 1 - y;
            var rowOffset = checked(bmp.PixelOffset + storedRow * bmp.Stride);
            for (var x = 0; x < bmp.Width; x++)
            {
                var pixelIndex = y * bmp.Width + x;
                var color = bitmap.GetPixel(x, y);
                if (PixelsEquivalent(color, originalArgbPixels[pixelIndex])) continue;
                if (color.A == 0) color = key;
                var offset = rowOffset + x * 3;
                output[offset] = color.B;
                output[offset + 1] = color.G;
                output[offset + 2] = color.R;
            }
        }

        return output;
    }

    private static EditableImageStorageInfo Build(
        StorageCommon common,
        EditableImageStorageKind kind,
        int width,
        int height,
        bool canEdit,
        string reason,
        Color? backgroundKey = null)
        => new()
        {
            Kind = kind,
            OriginalStoredLength = common.StoredLength,
            OriginalDecodedLength = common.DecodedLength,
            DataOffset = common.DataOffset,
            EntrySha256 = common.Sha256,
            HeaderHex = common.HeaderHex,
            Width = width,
            Height = height,
            BackgroundKeyColor = backgroundKey ?? DefaultGameMagenta,
            CanEdit = canEdit,
            ReadOnlyReason = reason
        };

    private static (int Width, int Height) TryReadImageDimensions(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var image = Image.FromStream(stream, false, true);
            return (image.Width, image.Height);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static Color DetectStandardImageBackgroundKey(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var image = Image.FromStream(stream, false, true);
            using var bitmap = new Bitmap(image);
            var counts = new Dictionary<int, int>();
            void Count(int x, int y)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.R < 170 || color.B < 170 || color.G > 130) return;
                var quantized = Color.FromArgb((color.R / 8) * 8, (color.G / 8) * 8, (color.B / 8) * 8).ToArgb();
                counts[quantized] = counts.TryGetValue(quantized, out var value) ? value + 1 : 1;
            }
            for (var x = 0; x < bitmap.Width; x++) { Count(x, 0); Count(x, bitmap.Height - 1); }
            for (var y = 1; y < bitmap.Height - 1; y++) { Count(0, y); Count(bitmap.Width - 1, y); }
            return counts.Count == 0 ? DefaultGameMagenta : Color.FromArgb(counts.MaxBy(pair => pair.Value).Key);
        }
        catch { return DefaultGameMagenta; }
    }

    private static bool TryReadBmp24(byte[] bytes, out Bmp24Info info, out string reason)
    {
        info = default;
        reason = string.Empty;
        if (bytes.Length < 54 || bytes[0] != 'B' || bytes[1] != 'M')
        {
            reason = "BMP 头部不完整，不能安全写回。";
            return false;
        }
        var dibSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(14, 4));
        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4));
        var signedHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4));
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26, 2));
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28, 2));
        var compression = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(30, 4));
        var pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, 4));
        var height = Math.Abs(signedHeight);
        if (dibSize < 40 || width <= 0 || height <= 0 || planes != 1 || bits != 24 || compression != 0)
        {
            reason = $"当前 BMP 不是受支持的未压缩 24 位 BI_RGB（DIB={dibSize}, {width}x{signedHeight}, bpp={bits}, compression={compression}）。";
            return false;
        }
        var stride = checked((width * 3 + 3) & ~3);
        if (pixelOffset < 14 + dibSize || (long)pixelOffset + (long)stride * height > bytes.Length)
        {
            reason = "BMP 像素区越界或布局无效，不能安全写回。";
            return false;
        }
        info = new Bmp24Info(width, height, signedHeight < 0, bits, compression, pixelOffset, stride);
        return true;
    }

    private static Color DetectBmpBackgroundKey(byte[] bytes, Bmp24Info bmp)
    {
        var counts = new Dictionary<int, int>();
        void Count(int x, int y)
        {
            var storedRow = bmp.TopDown ? y : bmp.Height - 1 - y;
            var offset = bmp.PixelOffset + storedRow * bmp.Stride + x * 3;
            var color = Color.FromArgb(bytes[offset + 2], bytes[offset + 1], bytes[offset]);
            if (color.R < 180 || color.B < 180 || color.G > 100) return;
            var key = color.ToArgb();
            counts[key] = counts.TryGetValue(key, out var value) ? value + 1 : 1;
        }
        for (var x = 0; x < bmp.Width; x++) { Count(x, 0); Count(x, bmp.Height - 1); }
        for (var y = 1; y < bmp.Height - 1; y++) { Count(0, y); Count(bmp.Width - 1, y); }
        return counts.Count == 0 ? DefaultGameMagenta : Color.FromArgb(counts.MaxBy(pair => pair.Value).Key);
    }

    private static bool PixelsEquivalent(Color current, int originalArgb)
        => current.ToArgb() == originalArgb ||
           (current.A == 0 && ((uint)originalArgb >> 24) == 0);

    private readonly record struct StorageCommon(int StoredLength, int DecodedLength, int DataOffset, string Sha256, string HeaderHex);
    private readonly record struct Bmp24Info(int Width, int Height, bool TopDown, int BitsPerPixel, int Compression, int PixelOffset, int Stride);
}
