namespace CCZModStudio.Models;

public enum HexFieldKind
{
    Derived,
    UInt8,
    UInt16,
    UInt32,
    FixedString,
    RawBytes
}

public sealed class HexFieldDefinition
{
    public required string ColumnName { get; init; }
    public required int Size { get; init; }
    public required HexFieldKind Kind { get; init; }
    public bool VisibleByDefault { get; init; } = true;
    public bool ConsumesBytes => Size > 0;

    public override string ToString() => $"{ColumnName} ({Kind}, {Size})";
}
