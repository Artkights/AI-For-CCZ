using System.Globalization;
using System.Security.Cryptography;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public static class EffectPatchByteService
{
    public const uint CoreEffectEngineAddress = 0x004101D9;

    public static string ToHex(IEnumerable<byte> bytes) => Convert.ToHexString(bytes.ToArray());

    public static byte[] ParseHex(string value)
    {
        var chars = (value ?? string.Empty).Where(Uri.IsHexDigit).ToArray();
        if (chars.Length % 2 != 0) throw new InvalidOperationException("十六进制字节字符串长度必须为偶数。");
        var bytes = new byte[chars.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(new string(chars, i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        return bytes;
    }

    public static string ReadFileBytes(CczProject project, string targetFile, long offset, int length)
    {
        var path = project.ResolveGameFile(targetFile);
        if (Path.GetFileName(path).Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase))
            return ReadFileBytes(ExecutableAnalysisSnapshotCache.Shared.GetBase(path), offset, length);
        return ToHex(ReadRange(path, offset, length));
    }

    internal static string ReadFileBytes(ExecutableAnalysisSnapshot snapshot, long offset, int length)
        => ToHex(snapshot.ReadFileRange(offset, length).Span.ToArray());

    public static byte[] ReadRange(string path, long offset, int length)
    {
        if (offset < 0 || length < 0) throw new InvalidOperationException("文件读取范围无效。");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, Math.Max(4096, length), FileOptions.RandomAccess);
        if (offset + length > stream.Length) throw new InvalidOperationException("文件读取范围越界。");
        stream.Position = offset;
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        PerformanceMetrics.Increment("EffectPatchBytes.RangeReadCount");
        PerformanceMetrics.Increment("EffectPatchBytes.RangeReadBytes", length);
        return bytes;
    }

    public static string ReadVirtualBytes(CczProject project, uint address, int length, string targetFile = "Ekd5.exe")
    {
        var path = project.ResolveGameFile(targetFile);
        if (Path.GetFileName(path).Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = ExecutableAnalysisSnapshotCache.Shared.GetBase(path);
            return ToHex(snapshot.ReadVirtualRange(address, length).Span.ToArray());
        }
        var offset = PeAddressMapper.Load(path).VirtualAddressToFileOffset(address);
        return ToHex(ReadRange(path, offset, length));
    }

    internal static string ReadVirtualBytes(ExecutableAnalysisSnapshot snapshot, uint address, int length)
        => ToHex(snapshot.ReadVirtualRange(address, length).Span.ToArray());

    public static byte[] BuildRelativeCall(uint source, uint target) => BuildRelativeTransfer(0xE8, source, target);
    public static byte[] BuildRelativeJump(uint source, uint target) => BuildRelativeTransfer(0xE9, source, target);

    private static byte[] BuildRelativeTransfer(byte opcode, uint source, uint target)
    {
        var delta = checked((long)target - ((long)source + 5));
        if (delta is < int.MinValue or > int.MaxValue) throw new InvalidOperationException("相对控制流目标超出 32 位范围。");
        return [opcode, .. BitConverter.GetBytes((int)delta)];
    }

    public static bool IsDirectCallToCore(CczProject project, uint callAddress)
        => IsDirectCallTo(project, callAddress, CoreEffectEngineAddress);

    public static bool IsDirectCallTo(CczProject project, uint callAddress, uint targetAddress)
    {
        var bytes = ParseHex(ReadVirtualBytes(project, callAddress, 5));
        if (bytes.Length != 5 || bytes[0] != 0xE8) return false;
        var target = checked((uint)((long)callAddress + 5 + BitConverter.ToInt32(bytes, 1)));
        return target == targetAddress;
    }

    public static string Sha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
