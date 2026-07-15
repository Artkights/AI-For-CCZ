using CCZModStudio.Models;

namespace CCZModStudio.Core;

public static class CharacterImageTargetResolver
{
    public static CharacterImageTargetDescriptor ResolveS(
        CczProject project,
        int sImageId,
        int? jobId,
        int factionSlot,
        int requestedStageSlot,
        string targetArchiveName,
        string action)
    {
        if (!IsUnitArchive(targetArchiveName))
            throw new InvalidOperationException($"S image target must be Unit_mov/atk/spc.e5, not {targetArchiveName}.");

        var stage = CharacterImageResourceService.ResolveSPreviewStage(
            project,
            sImageId,
            jobId,
            factionSlot,
            requestedStageSlot);
        if (stage.Target == null)
            throw new InvalidOperationException(stage.Mapping.Detail);

        var path = CharacterImageResourceService.ResolveGameFile(project, targetArchiveName);
        var explanation = string.Join(" ", new[]
        {
            stage.Mapping.Detail,
            stage.FallbackDetail,
            $"Action={action}; archive={targetArchiveName}; physical Unit image=#{stage.Target.ImageNumber}."
        }.Where(text => !string.IsNullOrWhiteSpace(text)));
        return new CharacterImageTargetDescriptor(
            sImageId == 0 ? CharacterImageLogicalKind.DefaultJob : CharacterImageLogicalKind.S,
            sImageId == 0 ? jobId ?? -1 : sImageId,
            stage.RequestedStageSlot,
            stage.EffectiveStageSlot,
            action,
            targetArchiveName,
            path,
            stage.Target.ImageNumber,
            explanation);
    }

    public static CharacterImageTargetDescriptor ResolveR(
        CczProject project,
        int rImageId,
        bool back,
        string action = "strip")
    {
        if (rImageId < 0) throw new ArgumentOutOfRangeException(nameof(rImageId));
        var imageNumber = checked(rImageId * 2 + (back ? 2 : 1));
        const string archive = "Pmapobj.e5";
        return new CharacterImageTargetDescriptor(
            CharacterImageLogicalKind.R,
            rImageId,
            1,
            1,
            back ? "back" : action,
            archive,
            CharacterImageResourceService.ResolveGameFile(project, archive),
            imageNumber,
            $"R{rImageId} {(back ? "back" : "front")} -> Pmapobj.e5 #{imageNumber}.");
    }

    public static string DescribePhysicalUnitImage(int imageNumber)
    {
        if (imageNumber <= 0) return $"Invalid Unit image #{imageNumber}.";
        if (imageNumber <= CharacterImageLayoutService.DefaultUnitImageStart)
        {
            var jobId = (imageNumber - 1) / CharacterImageLayoutService.DefaultFactionSlots;
            var faction = ((imageNumber - 1) % CharacterImageLayoutService.DefaultFactionSlots) + 1;
            return $"Unit #{imageNumber} is a default job image (job {jobId}, faction slot {faction}).";
        }

        if (imageNumber <= CharacterImageLayoutService.DefaultOneStageSpecialStart)
        {
            var offset = imageNumber - (CharacterImageLayoutService.DefaultUnitImageStart + 1);
            var sId = offset / 3 + 1;
            var stage = offset % 3 + 1;
            var warning = imageNumber == 241
                ? " Unit #241 is S1 stage 1, not logical S241; logical S241 maps to Unit #545."
                : string.Empty;
            return $"Unit #{imageNumber} = logical S{sId}, stage {stage}.{warning}";
        }

        var oneStageSId = imageNumber - CharacterImageLayoutService.DefaultOneStageSpecialStart +
                          CharacterImageLayoutService.DefaultThreeStageSpecialCount;
        return $"Unit #{imageNumber} = logical S{oneStageSId}, one-stage image.";
    }

    public static void ValidateRequestTarget(
        CharacterImageTargetDescriptor descriptor,
        string targetPath,
        int imageNumber)
    {
        if (descriptor.PhysicalImageNumber != imageNumber)
            throw new InvalidOperationException(
                $"Logical image mapping changed before commit: {descriptor.DisplayText}, request targets #{imageNumber}.");
        if (!Path.GetFullPath(descriptor.TargetPath).Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Logical image mapping targets {descriptor.TargetPath}, but the write request targets {targetPath}.");
    }

    private static bool IsUnitArchive(string fileName)
        => fileName.Equals("Unit_mov.e5", StringComparison.OrdinalIgnoreCase) ||
           fileName.Equals("Unit_atk.e5", StringComparison.OrdinalIgnoreCase) ||
           fileName.Equals("Unit_spc.e5", StringComparison.OrdinalIgnoreCase);
}
