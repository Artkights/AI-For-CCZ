namespace CCZModStudio.Models;

public sealed class ResourceDiagnosticNavigationTarget
{
    public bool IsRecognized { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string ScenarioFileName { get; init; } = string.Empty;
    public string ScenarioPath { get; init; } = string.Empty;
    public string MapId { get; init; } = string.Empty;
    public string MapImageName { get; init; } = string.Empty;
    public string MapImagePath { get; init; } = string.Empty;
    public bool MapImageExists { get; init; }
    public string HexzmapOffsetHex { get; init; } = string.Empty;
    public bool HexzmapBlockExists { get; init; }
    public string ResourceCategory { get; init; } = string.Empty;
    public string ResourceName { get; init; } = string.Empty;
    public string ResourcePath { get; init; } = string.Empty;
    public string ImageAssignmentPrefix { get; init; } = string.Empty;
    public int? ImageAssignmentRowId { get; init; }
    public int? ImageResourceId { get; init; }
    public string TableName { get; init; } = string.Empty;
    public string TableRowId { get; init; } = string.Empty;
    public string TableFieldName { get; init; } = string.Empty;

    public bool CanOpenScenarioMapLink => !string.IsNullOrWhiteSpace(ScenarioFileName) || !string.IsNullOrWhiteSpace(MapId);
    public bool CanJumpScenario => !string.IsNullOrWhiteSpace(ScenarioFileName) || !string.IsNullOrWhiteSpace(ScenarioPath);
    public bool CanJumpHexzmap => !string.IsNullOrWhiteSpace(MapId) || !string.IsNullOrWhiteSpace(HexzmapOffsetHex);
    public bool CanJumpMapViewer =>
        MapImageExists ||
        ResourceCategory.Equals("地图图片", StringComparison.OrdinalIgnoreCase) ||
        IsImagePath(ResourcePath) ||
        IsImagePath(MapImagePath);
    public bool CanJumpImageAssignment =>
        ImageAssignmentPrefix is "R" or "S" &&
        (ImageAssignmentRowId.HasValue || ImageResourceId.HasValue || ResourceCategory is "R形象" or "S形象");
    public bool CanJumpDataTable =>
        !string.IsNullOrWhiteSpace(TableName) &&
        !string.IsNullOrWhiteSpace(TableRowId) &&
        !string.IsNullOrWhiteSpace(TableFieldName);

    private static bool IsImagePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }
}
