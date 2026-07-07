using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ItemEffectResolutionService
{
    private readonly ItemEffectNameReader _nameReader = new();
    private readonly ItemEffectCatalogService _catalogService = new();
    private readonly Ccz66ItemEffectNameService _ccz66NameService = new();

    public ItemEffectResolutionResult Resolve(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        string majorCategory,
        int typeId,
        int rawEffectId)
    {
        var is66 = Ccz66RevisedLayout.Is66(project);
        if (rawEffectId == 0)
        {
            return BuiltIn(rawEffectId, 0, "None", "No equipment effect / empty slot.");
        }

        if (rawEffectId == 255)
        {
            return BuiltIn(rawEffectId, 255, "Common equipment", "Common shop equipment marker; no extended item effect is enabled.");
        }

        if (ConsumableItemEffectCatalogService.IsConsumableCategory(majorCategory))
        {
            var consumableEffectiveId = rawEffectId is 3 ? typeId : rawEffectId;
            if (consumableEffectiveId == 0)
            {
                return BuiltIn(rawEffectId, 0, "None", "No consumable effect / empty slot.");
            }

            if (consumableEffectiveId == 255)
            {
                return BuiltIn(rawEffectId, 255, "Common item", "Common item marker; no verified consumable effect is enabled.");
            }

            return new ItemEffectResolutionResult
            {
                RawEffectId = rawEffectId,
                EffectiveEffectId = consumableEffectiveId,
                CategoryMarker = rawEffectId == 3 ? rawEffectId : null,
                DisplayName = ConsumableItemEffectCatalogService.BuildDisplayName(consumableEffectiveId),
                Description = ConsumableItemEffectCatalogService.BuildDescription(consumableEffectiveId),
                Source = "ImageAssignerConsumableCatalog",
                Confidence = ConsumableItemEffectCatalogService.TryResolve(consumableEffectiveId, out _)
                    ? "ImageAssignerAligned"
                    : "UnverifiedConsumableEffect",
                IsCategoryMarker = rawEffectId == 3,
                Warnings = rawEffectId == 3
                    ? [$"Consumable row: raw equipment-effect value {rawEffectId} is a category marker; type value {typeId} is displayed as the actual consumable effect id."]
                    : []
            };
        }

        if (is66 && IsAccessoryOrConsumable(majorCategory) && rawEffectId is 2 or 3)
        {
            var resolved = typeId switch
            {
                0 => BuiltIn(typeId, 0, "None", "No equipment effect / empty slot."),
                255 => BuiltIn(typeId, 255, "Common equipment", "Common shop equipment marker; no extended item effect is enabled."),
                _ => ResolveDirect(project, tables, majorCategory, typeId, typeId, is66)
            };
            return new ItemEffectResolutionResult
            {
                RawEffectId = rawEffectId,
                EffectiveEffectId = typeId,
                CategoryMarker = rawEffectId,
                DisplayName = resolved.DisplayName,
                Description = resolved.Description,
                Source = resolved.Source,
                Confidence = resolved.Confidence,
                Is66Candidate = resolved.Source.Equals(Ccz66ItemEffectNameService.SlotFallbackSourceName, StringComparison.Ordinal),
                IsCategoryMarker = true,
                Warnings = new[]
                {
                    $"6.6 accessory/item row: raw row+0x12 value {rawEffectId} is a category marker; row+0x11 value {typeId} is displayed as the actual effect id."
                }.Concat(resolved.Warnings).ToArray()
            };
        }

        var effectiveId = is66
            ? rawEffectId
            : ItemEffectInterpretationService.ResolveEffectiveEffectId(majorCategory, typeId, rawEffectId);
        return ResolveDirect(project, tables, majorCategory, rawEffectId, effectiveId, is66);
    }

    private ItemEffectResolutionResult ResolveDirect(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        string majorCategory,
        int rawEffectId,
        int effectiveId,
        bool is66)
    {
        var projectCatalogExists = ProjectCatalogExists(project);
        var projectMatches = LoadProjectCatalog(project)
            .Where(entry => entry.EffectId == effectiveId)
            .ToArray();
        if (projectCatalogExists && projectMatches.Length > 0)
        {
            var name = JoinDistinct(projectMatches.Select(entry => entry.Name));
            var description = JoinDistinct(projectMatches.Select(entry => entry.Description));
            return new ItemEffectResolutionResult
            {
                RawEffectId = rawEffectId,
                EffectiveEffectId = effectiveId,
                DisplayName = string.IsNullOrWhiteSpace(name) ? $"project catalog effect {effectiveId}" : name,
                Description = description,
                Source = "ProjectCatalogOverride",
                Confidence = is66 ? "ProjectOverrideNeedsEvidence" : "ProjectOverride",
                Warnings = is66 ? ["project-side item effect catalog overrides engine name table; verify catalog version/evidence before treating it as native 6.6 fact"] : []
            };
        }

        if (is66 && _ccz66NameService.TryResolve(project, effectiveId, out var name66, out var source66, out var confidence66, out var warnings66))
        {
            return new ItemEffectResolutionResult
            {
                RawEffectId = rawEffectId,
                EffectiveEffectId = effectiveId,
                DisplayName = name66,
                Description = name66,
                Source = source66,
                Confidence = confidence66,
                Is66Candidate = source66.Equals(Ccz66ItemEffectNameService.SlotFallbackSourceName, StringComparison.Ordinal),
                Warnings = warnings66
            };
        }

        var baseNames = _nameReader.ReadBaseNames(project, tables);
        if (baseNames.TryGetValue(effectiveId, out var baseName) && !string.IsNullOrWhiteSpace(baseName))
        {
            return new ItemEffectResolutionResult
            {
                RawEffectId = rawEffectId,
                EffectiveEffectId = effectiveId,
                DisplayName = baseName,
                Description = baseName,
                Source = is66 ? Ccz66ItemEffectNameService.SourceName : "BaseExeNameTable",
                Confidence = is66 ? "Verified66ImageAssignerMapping" : "BaseName",
                Warnings = is66 ? ["6.6 item effect names come from the image-assigner binding list and Ekd5.exe 0x9E800 name pool, not the old 6.5 HexTable name blocks."] : []
            };
        }

        return new ItemEffectResolutionResult
        {
            RawEffectId = rawEffectId,
            EffectiveEffectId = effectiveId,
            DisplayName = $"unnamed/unverified effect 0x{effectiveId:X2}",
            Description = "No project catalog entry or base EXE name-table entry was found.",
            Source = "Unresolved",
            Confidence = "Unknown",
            Warnings = ["effect id has no verified display name"]
        };
    }

    private bool ProjectCatalogExists(CczProject project)
        => File.Exists(_catalogService.GetStorePath(project));

    private IReadOnlyList<ItemEffectCatalogEntry> LoadProjectCatalog(CczProject project)
    {
        var path = _catalogService.GetStorePath(project);
        return File.Exists(path) ? _catalogService.Load(project) : Array.Empty<ItemEffectCatalogEntry>();
    }

    private static ItemEffectResolutionResult BuiltIn(int rawEffectId, int effectiveEffectId, string name, string description)
        => new()
        {
            RawEffectId = rawEffectId,
            EffectiveEffectId = effectiveEffectId,
            DisplayName = name,
            Description = description,
            Source = "BuiltInMarker",
            Confidence = "VerifiedMarker"
        };

    public static bool IsAccessoryOrConsumable(string majorCategory)
        => majorCategory.Contains("杈呭姪", StringComparison.Ordinal) ||
           majorCategory.Contains("閬撳叿", StringComparison.Ordinal) ||
           majorCategory.Contains("辅助", StringComparison.Ordinal) ||
           majorCategory.Contains("道具", StringComparison.Ordinal) ||
           majorCategory.Contains("Consumable", StringComparison.OrdinalIgnoreCase) ||
           majorCategory.Contains("Accessory", StringComparison.OrdinalIgnoreCase);

    private static string JoinDistinct(IEnumerable<string?> values)
        => string.Join(" / ", values
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCulture));
}
