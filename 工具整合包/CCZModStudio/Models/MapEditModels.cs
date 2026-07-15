namespace CCZModStudio.Models;

public enum TerrainEditTool
{
    Pencil,
    Restore,
    FloodFill,
    Rectangle,
    Line,
    Eyedropper
}

public interface IMapEditCommand
{
    string Description { get; }
    DateTime CreatedAtUtc { get; }
    IReadOnlyCollection<int> AffectedCellIndexes { get; }
    void Execute();
    void Undo();
}

public sealed class HexzmapBlockBinding
{
    public string MapId { get; set; } = string.Empty;
    public int DirectoryEntryIndex { get; set; } = -1;
    public int Width { get; set; }
    public int Height { get; set; }
    public HexzmapBindingSource Source { get; set; } = HexzmapBindingSource.Unresolved;
    public float Confidence { get; set; }
    public bool UserConfirmed { get; set; }
    public string Evidence { get; set; } = string.Empty;

    public bool AuthorizesWrite
        => DirectoryEntryIndex >= 0 &&
           (Source == HexzmapBindingSource.ExactMapNumber ||
            (Source == HexzmapBindingSource.Manual && UserConfirmed));
}

public enum HexzmapBindingSource
{
    Unresolved,
    ExactMapNumber,
    Manual,
    SizeSuggestion
}
