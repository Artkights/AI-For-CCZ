using System.Security.Cryptography;
using System.Text;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class GameTitleLayoutResolver
{
    private const string Known65Sha256 = "84E3A1DC085AE6F9900D1E8C388A9CD6766379832DDF51BC7BDF780C6615B4A3";
    private const string Known66Sha256 = "4A4FD8DDBF83E5F0B769D1B97BF8F6E6431C3AB42892024A354228212D3D06A4";

    private static readonly IReadOnlyDictionary<string, Candidate> Candidates =
        new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase)
        {
            ["6.5"] = new(
                "star65-title-v1", "6.5", "Ekd5.exe", 0x8D3C4, 0x48E9C4, 32,
                Known65Sha256,
                "6.5 独立窗口标题区；本地样本和窗口标题调用引用已验证。"),
            ["6.6"] = new(
                "star66-title-v1", "6.6", "Ekd5.exe", 0x8B2D8, 0x48C8D8, 14,
                Known66Sha256,
                "6.6 公共窗口标题字符串；配套形象指定器限制为 14 个 GBK 字节。")
        };

    public GlobalTitleSetting Resolve(CczProject project)
    {
        var profile = new CczEngineProfileService().Detect(project);
        var version = profile.VersionHint;
        if (!Candidates.TryGetValue(version, out var candidate))
        {
            return Unsupported(version, $"当前引擎版本“{version}”没有已验证的游戏标题布局。");
        }

        var filePath = project.ResolveGameFile(candidate.FileName);
        if (!File.Exists(filePath))
        {
            return Unsupported(version, $"找不到标题目标文件 {candidate.FileName}。", candidate.FileName);
        }

        try
        {
            var data = File.ReadAllBytes(filePath);
            var rangeEnd = checked(candidate.FileOffset + candidate.CapacityBytes);
            if (candidate.FileOffset < 0 || rangeEnd > data.LongLength)
            {
                return Unsupported(version,
                    $"{candidate.FileName} 长度不足，标题区 {Format(candidate.FileOffset)}+{candidate.CapacityBytes}B 超出文件范围。",
                    candidate.FileName);
            }

            var mapper = PeAddressMapper.Load(filePath);
            var mappedOffset = mapper.VirtualAddressToFileOffset(candidate.VirtualAddress);
            var mappedAddress = mapper.FileOffsetToVirtualAddress(candidate.FileOffset);
            if (mappedOffset != candidate.FileOffset || mappedAddress != candidate.VirtualAddress)
            {
                return Unsupported(version,
                    $"标题 PE 地址映射不一致：VA=0x{candidate.VirtualAddress:X8} -> {Format(mappedOffset)}，预期 {Format(candidate.FileOffset)}。",
                    candidate.FileName);
            }

            if (!HasWindowTitleReference(data, candidate.VirtualAddress))
            {
                return Unsupported(version,
                    $"未找到布局 {candidate.LayoutKey} 的窗口标题代码引用签名；为避免误写，已禁用游戏标题。",
                    candidate.FileName);
            }

            var offset = checked((int)candidate.FileOffset);
            var bytes = data.AsSpan(offset, candidate.CapacityBytes).ToArray();
            if (!HasTerminatorBoundary(data, offset, candidate.CapacityBytes))
            {
                return Unsupported(version,
                    $"标题区 {Format(candidate.FileOffset)} 没有安全的 NUL 结束边界；为避免覆盖相邻数据，已禁用游戏标题。",
                    candidate.FileName);
            }

            string title;
            try
            {
                title = DecodeStrictGbk(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Unsupported(version,
                    $"标题区 {Format(candidate.FileOffset)} 不是有效 GBK 文本；为避免误写，已禁用游戏标题。",
                    candidate.FileName);
            }

            var sha256 = Convert.ToHexString(SHA256.HashData(data));
            var knownHash = sha256.Equals(candidate.KnownSha256, StringComparison.OrdinalIgnoreCase);
            var diagnostic = knownHash
                ? $"已按已知 {version} 样本 SHA256 和代码引用签名确认布局。"
                : $"SHA256 与参考样本不同，但 PE 映射、标题边界和代码引用签名均通过，按兼容 {version} 变体启用。";

            return new GlobalTitleSetting
            {
                Title = title,
                CapacityBytes = candidate.CapacityBytes,
                FileName = candidate.FileName,
                Offset = candidate.FileOffset,
                VirtualAddress = candidate.VirtualAddress,
                EngineVersion = candidate.EngineVersion,
                LayoutKey = candidate.LayoutKey,
                CanRead = true,
                CanEdit = true,
                Diagnostic = diagnostic,
                OriginalBytesHex = Convert.ToHexString(bytes),
                ExeSha256AtLoad = sha256,
                Source = candidate.Evidence,
                Status = knownHash ? "已验证：版本布局 + 已知样本" : "已验证：版本布局结构签名"
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or OverflowException)
        {
            return Unsupported(version, $"游戏标题布局验证失败：{ex.Message}", candidate.FileName);
        }
    }

    private static bool HasWindowTitleReference(byte[] data, uint virtualAddress)
    {
        var pushAddress = new byte[5];
        pushAddress[0] = 0x68;
        BitConverter.GetBytes(virtualAddress).CopyTo(pushAddress, 1);
        ReadOnlySpan<byte> setWindowTextCall = [0xFF, 0x15, 0x60, 0x63, 0x48, 0x00];
        ReadOnlySpan<byte> updateWindowCall = [0xFF, 0x15, 0xA8, 0x62, 0x48, 0x00];

        for (var index = 0; index <= data.Length - pushAddress.Length; index++)
        {
            if (!data.AsSpan(index, pushAddress.Length).SequenceEqual(pushAddress)) continue;
            var windowLength = Math.Min(40, data.Length - index - pushAddress.Length);
            if (windowLength <= 0) continue;
            var window = data.AsSpan(index + pushAddress.Length, windowLength);
            if (window.IndexOf(setWindowTextCall) >= 0 && window.IndexOf(updateWindowCall) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTerminatorBoundary(byte[] data, int offset, int capacity)
    {
        if (data.AsSpan(offset, capacity).IndexOf((byte)0) >= 0) return true;
        return offset + capacity < data.Length && data[offset + capacity] == 0;
    }

    private static string DecodeStrictGbk(byte[] bytes)
    {
        EncodingService.EnsureCodePages();
        var nul = Array.IndexOf(bytes, (byte)0);
        var length = nul >= 0 ? nul : bytes.Length;
        while (length > 0 && bytes[length - 1] is 0x20 or 0xFF) length--;
        var encoding = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        return length == 0 ? string.Empty : encoding.GetString(bytes, 0, length).TrimEnd(' ', '\u3000');
    }

    private static GlobalTitleSetting Unsupported(string version, string diagnostic, string fileName = "Ekd5.exe")
        => new()
        {
            Title = string.Empty,
            CapacityBytes = 0,
            FileName = fileName,
            Offset = 0,
            VirtualAddress = 0,
            EngineVersion = version,
            LayoutKey = string.Empty,
            CanRead = false,
            CanEdit = false,
            Diagnostic = diagnostic,
            OriginalBytesHex = string.Empty,
            ExeSha256AtLoad = string.Empty,
            Source = "版本化游戏标题布局解析器",
            Status = "不支持：标题布局未通过安全验证"
        };

    private static string Format(long offset) => $"0x{offset:X}";

    private sealed record Candidate(
        string LayoutKey,
        string EngineVersion,
        string FileName,
        long FileOffset,
        uint VirtualAddress,
        int CapacityBytes,
        string KnownSha256,
        string Evidence);
}
