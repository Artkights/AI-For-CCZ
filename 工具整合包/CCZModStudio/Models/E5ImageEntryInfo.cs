namespace CCZModStudio.Models;

public sealed class E5ImageEntryInfo
{
    public int ImageNumber { get; init; }
    public int IndexOffset { get; init; }
    public int DataOffset { get; init; }
    public int Length { get; init; }
    public int StoredLength { get; init; }
    public int DecodedLength { get; init; }
    public bool IsCompressed => StoredLength > 0 && DecodedLength > 0 && StoredLength != DecodedLength;
    public string Kind { get; init; } = string.Empty;
}
