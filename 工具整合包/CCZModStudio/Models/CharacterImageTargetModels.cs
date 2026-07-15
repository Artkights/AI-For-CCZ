namespace CCZModStudio.Models;

public enum CharacterImageLogicalKind
{
    R,
    S,
    DefaultJob,
    Face,
    PhysicalE5
}

public sealed record CharacterImageTargetDescriptor(
    CharacterImageLogicalKind LogicalKind,
    int LogicalId,
    int RequestedStageSlot,
    int EffectiveStageSlot,
    string DirectionOrAction,
    string TargetArchiveName,
    string TargetPath,
    int PhysicalImageNumber,
    string MappingExplanation)
{
    public bool IsOneStageFallback => RequestedStageSlot != EffectiveStageSlot;

    public string DisplayText =>
        $"{LogicalKind}{LogicalId} -> {TargetArchiveName} Unit #{PhysicalImageNumber}" +
        (RequestedStageSlot == EffectiveStageSlot
            ? string.Empty
            : $" (requested stage {RequestedStageSlot}, effective stage {EffectiveStageSlot})");
}
