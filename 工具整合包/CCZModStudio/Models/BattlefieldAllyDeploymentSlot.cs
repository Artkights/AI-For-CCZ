namespace CCZModStudio.Models;

public sealed class BattlefieldAllyDeploymentSlot
{
    public int Order { get; init; }
    public int DisplayOrder => Order + 1;
    public int GridX { get; init; }
    public int GridY { get; init; }
    public int DirectionCode { get; init; }
    public string Direction { get; init; } = "下";
    public int Flag { get; init; }
    public bool Hidden { get; init; }
    public int? PersonId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int? JobId { get; init; }
    public string JobName { get; init; } = string.Empty;
    public int? SImageId { get; init; }
    public int? RImageId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string SourceFileName { get; init; } = string.Empty;
    public string SourceLocator { get; init; } = string.Empty;
    public string SourceValues { get; init; } = string.Empty;
    public bool IsForced => PersonId.HasValue;
}
