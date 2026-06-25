using CCZModStudio.Core;

namespace CCZModStudio.Models;

public sealed record AccessoryJobGroup(
    int GroupIndex,
    IReadOnlyList<int> JobSeriesIds,
    IReadOnlyList<string> JobSeriesNames)
{
    public int PrimaryJobSeriesId => JobSeriesIds.Count == 0 ? -1 : JobSeriesIds[0];

    public string PrimaryDisplayName => JobSeriesIds.Count == 0
        ? "空分组"
        : $"{JobSeriesIds[0]:D2} {JobSeriesNames.ElementAtOrDefault(0) ?? string.Empty}".TrimEnd();

    public string SummaryText
        => JobSeriesIds.Count == 0
            ? $"组{GroupIndex + 1:D2}：空"
            : $"组{GroupIndex + 1:D2}：" + string.Join("、", JobSeriesIds.Select((id, index) =>
                $"{id:D2} {JobSeriesNames.ElementAtOrDefault(index) ?? string.Empty}".TrimEnd()));
}

public sealed class AccessoryJobGroupProfile
{
    public const uint DefaultStartVirtualAddress = 0x0044C341;

    public uint StartVirtualAddress { get; init; } = DefaultStartVirtualAddress;
    public long FileOffset { get; init; }
    public string FileOffsetHex => HexDisplayFormatter.FormatOffset(FileOffset);
    public int WritableLength { get; init; }
    public IReadOnlyList<byte> RawBytes { get; init; } = Array.Empty<byte>();
    public string RawBytesHex => HexDisplayFormatter.FormatByteList(RawBytes.ToArray());
    public IReadOnlyList<AccessoryJobGroup> Groups { get; init; } = Array.Empty<AccessoryJobGroup>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    public string SummaryText
        => $"辅助装备多兵种分组：地址 0x{StartVirtualAddress:X8} -> {FileOffsetHex}，可写 {WritableLength} 字节，分组 {Groups.Count} 个。";
}

public sealed class AccessoryJobGroupPreview
{
    public uint StartVirtualAddress { get; init; } = AccessoryJobGroupProfile.DefaultStartVirtualAddress;
    public long FileOffset { get; init; }
    public string FileOffsetHex => HexDisplayFormatter.FormatOffset(FileOffset);
    public int WritableLength { get; init; }
    public IReadOnlyList<AccessoryJobGroup> Groups { get; init; } = Array.Empty<AccessoryJobGroup>();
    public IReadOnlyList<byte> PayloadBytes { get; init; } = Array.Empty<byte>();
    public IReadOnlyList<byte> PaddedBytes { get; init; } = Array.Empty<byte>();
    public string PayloadBytesHex => HexDisplayFormatter.FormatByteList(PayloadBytes.ToArray());
    public string PaddedBytesHex => HexDisplayFormatter.FormatByteList(PaddedBytes.ToArray());
    public bool CanWrite { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public sealed class AccessoryJobGroupSaveResult
{
    public string TargetFilePath { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public int BytesWritten { get; init; }
    public int ChangedBytes { get; init; }
    public AccessoryJobGroupPreview Preview { get; init; } = new();
}
