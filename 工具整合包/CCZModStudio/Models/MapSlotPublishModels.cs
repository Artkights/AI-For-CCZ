namespace CCZModStudio.Models;

public enum MapSlotState
{
    Complete,
    MissingMapImage,
    MissingTerrainBlock,
    SizeMismatch,
    InvalidImage,
    UnsupportedTerrainSegment
}

public enum MapSlotPublishMode
{
    OverwriteExisting,
    AppendNew
}

public enum MapSlotPublishFaultPoint
{
    AfterMapCommit,
    AfterHexzmapCommit,
    BeforeRereadVerification
}

public sealed class MapSlotCatalogEntry
{
    public int MapNumber { get; init; }
    public string MapId { get; init; } = string.Empty;
    public MapSlotState State { get; init; }
    public MapResourceItem? MapResource { get; init; }
    public HexzmapDirectoryEntry? TerrainEntry { get; init; }
    public int GridWidth { get; init; }
    public int GridHeight { get; init; }
    public string Detail { get; init; } = string.Empty;

    public bool CanOverwrite => State == MapSlotState.Complete && MapResource != null && TerrainEntry != null;
    public string DisplayName => $"{MapId}  {GridWidth}x{GridHeight}  {GetStateText(State)}";

    public override string ToString() => DisplayName;

    public static string GetStateText(MapSlotState state) => state switch
    {
        MapSlotState.Complete => "完整",
        MapSlotState.MissingMapImage => "缺少底图",
        MapSlotState.MissingTerrainBlock => "缺少地形块",
        MapSlotState.SizeMismatch => "尺寸不一致",
        MapSlotState.InvalidImage => "底图无效",
        MapSlotState.UnsupportedTerrainSegment => "地形段不支持写入",
        _ => state.ToString()
    };
}

public sealed class MapSlotPublishRequest
{
    public string DraftId { get; init; } = string.Empty;
    public MapSlotPublishMode Mode { get; init; } = MapSlotPublishMode.OverwriteExisting;
    public string TargetMapId { get; init; } = string.Empty;
    public bool AllowResizeExisting { get; init; }
    public string ExpectedTargetHash { get; init; } = string.Empty;
    public string ExpectedHexzmapHash { get; init; } = string.Empty;
    public string WriteMode { get; init; } = "direct";
    public byte TerrainFillId { get; init; }
    public bool ConfirmDestructiveCrop { get; init; }
}

public sealed class MapResizeRequest
{
    public int OldWidth { get; init; }
    public int OldHeight { get; init; }
    public int NewWidth { get; init; }
    public int NewHeight { get; init; }
    public byte TerrainFillId { get; init; }
}

public sealed class MapResizePreview
{
    public int OldWidth { get; init; }
    public int OldHeight { get; init; }
    public int NewWidth { get; init; }
    public int NewHeight { get; init; }
    public int AddedCells { get; init; }
    public int RemovedCells { get; init; }
    public int RemovedManualOverrides { get; init; }
    public int RemovedTerrainBaseCells { get; init; }
    public int RemovedGeneratedCells { get; init; }
    public int RemovedBuildingCells { get; init; }
    public int RemovedSceneryCells { get; init; }
    public int RemovedSceneryOverlays { get; init; }
    public bool IsDestructive => RemovedCells > 0 || RemovedManualOverrides > 0 || RemovedTerrainBaseCells > 0 ||
                                 RemovedGeneratedCells > 0 || RemovedBuildingCells > 0 || RemovedSceneryCells > 0 ||
                                 RemovedSceneryOverlays > 0;
}

public sealed class HexzmapLayoutBuildResult
{
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
    public int OldEntryCount { get; init; }
    public int NewEntryCount { get; init; }
    public int TargetIndex { get; init; }
    public int OldSegmentLength { get; init; }
    public int NewSegmentLength { get; init; }
    public int NewSegmentOffset { get; init; }
    public int DirectoryGrowthBytes { get; init; }
}

public sealed class MapSlotPublishPlan
{
    public MapSlotPublishMode Mode { get; init; }
    public string TargetMapId { get; init; } = string.Empty;
    public string TargetMapPath { get; init; } = string.Empty;
    public string HexzmapPath { get; init; } = string.Empty;
    public bool TargetMapExists { get; init; }
    public bool ResizesExisting { get; init; }
    public int OldGridWidth { get; init; }
    public int OldGridHeight { get; init; }
    public int NewGridWidth { get; init; }
    public int NewGridHeight { get; init; }
    public int OldDirectoryEntryCount { get; init; }
    public int NewDirectoryEntryCount { get; init; }
    public int OldTerrainSegmentLength { get; init; }
    public int NewTerrainSegmentLength { get; init; }
    public int DirectoryGrowthBytes { get; init; }
    public int NewTerrainSegmentOffset { get; init; }
    public string ExpectedTargetHash { get; init; } = string.Empty;
    public string ExpectedHexzmapHash { get; init; } = string.Empty;
    public string NewTargetHash { get; init; } = string.Empty;
    public string NewHexzmapHash { get; init; } = string.Empty;
    public byte[] JpegBytes { get; init; } = Array.Empty<byte>();
    public byte[] HexzmapBytes { get; init; } = Array.Empty<byte>();
    public MapResizePreview ResizePreview { get; init; } = new();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = string.Empty;
}

public sealed class MapSlotPublishResult
{
    public MapSlotPublishMode Mode { get; init; }
    public string MapId { get; init; } = string.Empty;
    public string MapPath { get; init; } = string.Empty;
    public string HexzmapPath { get; init; } = string.Empty;
    public string MapBackupPath { get; init; } = string.Empty;
    public string HexzmapBackupPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public string DraftPath { get; init; } = string.Empty;
    public int GridWidth { get; init; }
    public int GridHeight { get; init; }
    public int DirectoryEntryCount { get; init; }
    public string MapSha256 { get; init; } = string.Empty;
    public string HexzmapSha256 { get; init; } = string.Empty;
    public bool RollbackAttempted { get; init; }
    public bool RollbackSucceeded { get; init; }
}
