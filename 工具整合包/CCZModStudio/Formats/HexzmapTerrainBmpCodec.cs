using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class HexzmapTerrainBmpCodec
{
    public const int BmpHeaderSize = 14;
    public const int DibHeaderSize = 40;
    public const int PaletteEntryCount = 256;
    public const int PaletteSize = PaletteEntryCount * 4;
    public const int PixelOffset = BmpHeaderSize + DibHeaderSize + PaletteSize;
    public const int BitsPerPixel = 8;
    public const int PrefixSize = HexzmapProbeReader.TerrainHeaderSize;
    private static readonly byte[] LegacyPaletteBytes = Convert.FromBase64String(
        "/wD3AP8A9wD/APcA/wD3AP8A9wD/APcA/wD3AP8A9wD/APcA/wD3AGOM/wBKa/8AOUr3ACkxrQBa3v8AUs7/AFq97wBSnL0A////AO7u7gDd3d0AzMzMALu7uwCqqqoAmZmZAIiIiAB3d3cAZmZmAFVVVQBEREQAIiIiAAAAAADh7/0AtNn9AJbK/gBps/4ALZX/AAB//wAAbv8AAEz/AAAA/wAAAOcAAADDAAAAtwAAAJ4AAACGAAAAbgAAAEoA/f3SAP39tAD9/pYA/f5pAP//AAD/3QAA/7sAAP+ZAAD/dwAA/1UAAP8AAADnAAAAwwAAAKsAAAB6AAAAVgAAAOH9/QC0/f0Ah/7+AFr+/gAt//8AAP//AADt7QAA29sAAMnJAACysgAAn58AAIuLAAB4eAAAZWUAAFFRAAA+PgDh/eEAwf3BAKH+oQCB/oEAYP5gAAD/AAAA7QAAANsAAADJAAAAtwAAAKUAAACSAAAAgAAAAG4AAABcAAAASgAAbomuAF52mABIYnUAP1JkADZASwAeJS4Apc7nAIjD7gByreEAc6LZAGyazgC91ucAuNHpAKXG5wCUvd4AhLXeAP3h9wDmwd4AzqHEALeBqwCfYZIAiEF5AHAhXwBZAUYA/eH9AP6p/gD9hv0A/2r/AP8A/wDoF+gA0RTRALoSugDO8PgAq9DZAIiwugBlkJsAVX+KAEVvegA0XmkAJE1YAPLeqwDNuYUAqZNeAIRuOAB3YzAAa1coAF5MIABRQBgAgMOAAHC0cABhpWEAUZZRAEGIQQAxeTEAImoiABJbEgBdkroAUISrAER2nQA3aI4AKluAAB1NcQARP2MABDFUACnGvQApva0ASq2tADGtlAAxnIwAMZRzADGEcwAxe2MAv6AiAK6AGgCdYBEAjD8JAHsfAAACVcIANqz/ACFj5wAYQtYAGEKtABAxjAAfJCoAWAFEAAABOgAAAG8ADjgfAA9CKAAQSi0ABTBUABI/YgATUDEAOD9KABNWNQAUUzMAFl06ABVaNwAZYz4AF2E9ABplQAAaaEIAOl88ABtsRQAUY0sAMlBcAB5McQBgbkQAVWQ+AFloQgBgcUkATmtLABRpUQAUbVQAFnBXAB1wRwAVc1kAK1p/AENxTwAYdVwAN2FqABl4XQBreEcAY3RMAGd4UQBWdFMAPXhYAFlvVQBjeloAbn9WABl7YAAZgWUAG4VoADhnjgAbfmMAMHJtACZ9bABZdlwANH13AFt8agA6dncAQX2BAHSGXABggnMAR3WcAE2DiwBVhKoAU5q7AHafygB0ueEA/wD3AP8A9wD/APcA/wD3AP8A9wD/APcA/wD3AP8A9wD/APcA/wD3AA==");

    public void Encode(HexzmapBlockInfo block, byte[] prefixBytes, byte[] terrainCells, string outputPath)
    {
        if (block.Width <= 0 || block.Height <= 0)
        {
            throw new InvalidOperationException("Hexzmap block size is invalid.");
        }

        var expectedCells = checked(block.Width * block.Height);
        if (prefixBytes.Length != PrefixSize)
        {
            throw new InvalidOperationException($"Hexzmap terrain BMP prefix must be {PrefixSize} bytes.");
        }

        if (terrainCells.Length != expectedCells)
        {
            throw new InvalidOperationException($"Hexzmap terrain BMP cells must be {expectedCells} bytes.");
        }

        var payloadLength = checked(PrefixSize + terrainCells.Length);
        var fileSize = checked(PixelOffset + payloadLength);
        var bytes = new byte[fileSize];

        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        WriteUInt32LittleEndian(bytes, 2, fileSize);
        WriteUInt32LittleEndian(bytes, 10, PixelOffset);
        WriteUInt32LittleEndian(bytes, 14, DibHeaderSize);
        WriteInt32LittleEndian(bytes, 18, block.Width);
        WriteInt32LittleEndian(bytes, 22, -block.Height);
        WriteUInt16LittleEndian(bytes, 26, 1);
        WriteUInt16LittleEndian(bytes, 28, BitsPerPixel);
        WriteUInt32LittleEndian(bytes, 30, 0);
        WriteUInt32LittleEndian(bytes, 34, 0);
        WriteInt32LittleEndian(bytes, 38, 0);
        WriteInt32LittleEndian(bytes, 42, 0);
        WriteUInt32LittleEndian(bytes, 46, 0);
        WriteUInt32LittleEndian(bytes, 50, 0);

        WritePalette(bytes);
        Buffer.BlockCopy(prefixBytes, 0, bytes, PixelOffset, PrefixSize);
        Buffer.BlockCopy(terrainCells, 0, bytes, PixelOffset + PrefixSize, terrainCells.Length);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, bytes);
    }

    public HexzmapTerrainBmpData Decode(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Hexzmap terrain BMP does not exist.", path);
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < PixelOffset + PrefixSize)
        {
            throw new InvalidOperationException("Hexzmap terrain BMP is too small: " + path);
        }

        if (bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
        {
            throw new InvalidOperationException("Hexzmap terrain BMP signature must be BM: " + path);
        }

        var fileSize = ReadUInt32LittleEndian(bytes, 2);
        var pixelOffset = ReadUInt32LittleEndian(bytes, 10);
        var dibSize = ReadUInt32LittleEndian(bytes, 14);
        var width = ReadInt32LittleEndian(bytes, 18);
        var heightRaw = ReadInt32LittleEndian(bytes, 22);
        var planes = ReadUInt16LittleEndian(bytes, 26);
        var bitsPerPixel = ReadUInt16LittleEndian(bytes, 28);
        var compression = ReadUInt32LittleEndian(bytes, 30);

        if (dibSize != DibHeaderSize)
        {
            throw new InvalidOperationException($"Hexzmap terrain BMP DIB header must be {DibHeaderSize} bytes.");
        }

        if (width <= 0 || heightRaw >= 0)
        {
            throw new InvalidOperationException("Hexzmap terrain BMP must use positive width and negative top-down height.");
        }

        if (planes != 1 || bitsPerPixel != BitsPerPixel || compression != 0)
        {
            throw new InvalidOperationException("Hexzmap terrain BMP must be uncompressed 8bpp indexed BMP.");
        }

        if (pixelOffset != PixelOffset)
        {
            throw new InvalidOperationException($"Hexzmap terrain BMP pixel offset must be {PixelOffset}.");
        }

        var height = Math.Abs(heightRaw);
        var cellCount = checked(width * height);
        var expectedLength = checked(PixelOffset + PrefixSize + cellCount);
        if (bytes.Length != expectedLength)
        {
            throw new InvalidOperationException(
                $"Hexzmap terrain BMP file length must be {expectedLength} bytes for {width}x{height}; actual={bytes.Length}.");
        }

        var warnings = new List<string>();
        if (fileSize != bytes.Length)
        {
            warnings.Add($"BMP file-size header {fileSize} differs from actual length {bytes.Length}.");
        }

        var palette = bytes.AsSpan(BmpHeaderSize + DibHeaderSize, PaletteSize).ToArray();
        if (!palette.SequenceEqual(LegacyPaletteBytes))
        {
            warnings.Add("BMP palette differs from the Hexzmap1.BMP legacy palette; terrain cells will still be imported from raw bytes.");
        }

        var prefix = bytes.AsSpan(PixelOffset, PrefixSize).ToArray();
        var cells = bytes.AsSpan(PixelOffset + PrefixSize, cellCount).ToArray();
        return new HexzmapTerrainBmpData
        {
            Path = path,
            Width = width,
            Height = height,
            PixelOffset = pixelOffset,
            BitsPerPixel = bitsPerPixel,
            PrefixBytes = prefix,
            TerrainCells = cells,
            PaletteBytes = palette,
            Sha256 = WriteOperationReportService.ComputeSha256(bytes),
            Warnings = warnings
        };
    }

    private static void WritePalette(byte[] bytes)
        => Buffer.BlockCopy(LegacyPaletteBytes, 0, bytes, BmpHeaderSize + DibHeaderSize, PaletteSize);

    private static ushort ReadUInt16LittleEndian(byte[] bytes, int offset)
        => BitConverter.ToUInt16(bytes, offset);

    private static int ReadUInt32LittleEndian(byte[] bytes, int offset)
        => checked((int)BitConverter.ToUInt32(bytes, offset));

    private static int ReadInt32LittleEndian(byte[] bytes, int offset)
        => BitConverter.ToInt32(bytes, offset);

    private static void WriteUInt16LittleEndian(byte[] bytes, int offset, int value)
        => BitConverter.GetBytes((ushort)value).CopyTo(bytes, offset);

    private static void WriteUInt32LittleEndian(byte[] bytes, int offset, int value)
        => BitConverter.GetBytes((uint)value).CopyTo(bytes, offset);

    private static void WriteInt32LittleEndian(byte[] bytes, int offset, int value)
        => BitConverter.GetBytes(value).CopyTo(bytes, offset);
}
