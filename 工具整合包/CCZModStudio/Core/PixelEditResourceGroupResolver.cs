using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class PixelEditResourceGroupResolver
{
    public IReadOnlyList<EditableImageTarget> BuildJobSGroup(CczProject project, int jobId, int factionSlot)
    {
        var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(project, 0, jobId, factionSlot);
        if (mapping.ImageNumbers.Count != 1) throw new InvalidOperationException(mapping.Detail);
        var faction = CharacterImageResourceService.BuildSPreviewFactionText(factionSlot);
        return BuildUnitActionTargets(project, mapping.ImageNumbers[0], $"兵种 {jobId:D2} {faction}", 0, jobId, factionSlot, 1);
    }

    public IReadOnlyList<EditableImageTarget> BuildCharacterGroup(
        CczProject project,
        int rImageId,
        int sImageId,
        int? jobId,
        IReadOnlyList<int> sFactionSlots)
    {
        var result = new List<EditableImageTarget>();
        result.AddRange(BuildRGroup(project, rImageId));

        var mappings = sImageId == 0
            ? sFactionSlots.Distinct().OrderBy(slot => slot)
                .Select(slot => CharacterImageResourceService.ResolveSUnitImageMapping(project, 0, jobId, slot))
                .ToArray()
            : new[] { CharacterImageResourceService.ResolveSUnitImageMapping(project, sImageId, jobId, 1) };

        foreach (var mapping in mappings)
        {
            if (mapping.ImageNumbers.Count == 0) throw new InvalidOperationException(mapping.Detail);
            var stageTargets = CharacterImageResourceService.ResolveSImageStageTargets(
                project,
                mapping,
                Array.Empty<int>(),
                defaultAllStages: true);
            foreach (var stage in stageTargets)
            {
                var prefix = sImageId == 0
                    ? $"S0 {CharacterImageResourceService.BuildSPreviewFactionText(mapping.FactionSlot)}"
                    : $"S{sImageId} {stage.DisplayName}";
                result.AddRange(BuildUnitActionTargets(
                    project, stage.ImageNumber, prefix, sImageId, jobId, mapping.FactionSlot, stage.StageSlot));
            }
        }

        return Deduplicate(result);
    }

    public IReadOnlyList<EditableImageTarget> ExpandSemanticGroup(CczProject project, EditableImageTarget target)
    {
        var name = Path.GetFileName(target.TargetPath);
        if (name.Equals("Unit_mov.e5", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Unit_atk.e5", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Unit_spc.e5", StringComparison.OrdinalIgnoreCase))
        {
            return BuildUnitActionTargets(project, target.ImageNumber, $"Unit #{target.ImageNumber}");
        }

        if (name.Equals("Pmapobj.e5", StringComparison.OrdinalIgnoreCase) && target.ImageNumber > 0)
        {
            var rImageId = target.ImageNumber % 2 == 1
                ? (target.ImageNumber - 1) / 2
                : (target.ImageNumber - 2) / 2;
            return BuildRGroup(project, rImageId);
        }

        return new[] { target };
    }

    public IReadOnlyList<EditableImageTarget> BuildRGroup(CczProject project, int rImageId)
    {
        if (rImageId < 0) throw new InvalidOperationException($"R 形象编号不能小于 0：{rImageId}");
        var path = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        return
        [
            BuildRawTarget(path, checked(rImageId * 2 + 1), $"R{rImageId} 正面", "R形象整组换色",
                CharacterImageTargetResolver.ResolveR(project, rImageId, back: false)),
            BuildRawTarget(path, checked(rImageId * 2 + 2), $"R{rImageId} 背面", "R形象整组换色",
                CharacterImageTargetResolver.ResolveR(project, rImageId, back: true))
        ];
    }

    private static IReadOnlyList<EditableImageTarget> BuildUnitActionTargets(
        CczProject project,
        int imageNumber,
        string prefix,
        int? sImageId = null,
        int? jobId = null,
        int factionSlot = 1,
        int stageSlot = 1)
        =>
        [
            BuildRawTarget(CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5"), imageNumber, $"{prefix} 移动 11帧", "S形象整组换色",
                sImageId.HasValue ? CharacterImageTargetResolver.ResolveS(project, sImageId.Value, jobId, factionSlot, stageSlot, "Unit_mov.e5", "mov") : null),
            BuildRawTarget(CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5"), imageNumber, $"{prefix} 攻击 12帧", "S形象整组换色",
                sImageId.HasValue ? CharacterImageTargetResolver.ResolveS(project, sImageId.Value, jobId, factionSlot, stageSlot, "Unit_atk.e5", "atk") : null),
            BuildRawTarget(CharacterImageResourceService.ResolveGameFile(project, "Unit_spc.e5"), imageNumber, $"{prefix} 特技 5帧", "S形象整组换色",
                sImageId.HasValue ? CharacterImageTargetResolver.ResolveS(project, sImageId.Value, jobId, factionSlot, stageSlot, "Unit_spc.e5", "spc") : null)
        ];

    private static EditableImageTarget BuildRawTarget(
        string path,
        int imageNumber,
        string displayName,
        string operationKind,
        CharacterImageTargetDescriptor? characterTarget = null)
    {
        var spec = EditableImageCodecService.TryResolveRawFrameSpec(path);
        return new EditableImageTarget
        {
            Kind = EditableImageTargetKind.E5RawStrip,
            DisplayName = displayName,
            TargetPath = path,
            ImageNumber = imageNumber,
            ResourceFormat = "E5 RAW 帧条",
            FrameWidth = spec?.Width,
            FrameHeight = spec?.FrameHeight,
            OperationKind = operationKind,
            CharacterTarget = characterTarget
        };
    }

    private static IReadOnlyList<EditableImageTarget> Deduplicate(IEnumerable<EditableImageTarget> targets)
        => targets
            .GroupBy(target => $"{Path.GetFullPath(target.TargetPath)}|{target.ImageNumber}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
}
