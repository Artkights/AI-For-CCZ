using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ItemIconMappingService
{
    private readonly E5ImageReplaceService _e5ImageService = new();

    public ItemIconFieldMapping Resolve(CczProject project, int fieldValue, string kind)
    {
        if (fieldValue < 0)
        {
            return new ItemIconFieldMapping
            {
                Kind = kind,
                FieldValue = fieldValue,
                InRange = false,
                Error = "Icon field value must be non-negative."
            };
        }

        var normalizedKind = NormalizeKind(kind);
        var isItem = normalizedKind.Equals("item", StringComparison.OrdinalIgnoreCase);
        var relative = isItem
            ? Ccz66RevisedLayout.ResolveItemIconResourceFile(project)
            : Ccz66RevisedLayout.ResolveStrategyIconResourceFile(project);
        var path = Ccz66RevisedLayout.ResolveResourcePath(project, relative);
        var isE5 = Ccz66RevisedLayout.IsE5IconResource(relative);
        var warnings = new List<string>();
        var entryCount = 0;
        if (File.Exists(path) && isE5)
        {
            entryCount = _e5ImageService.ReadIndex(path).Count;
        }

        int? small = null;
        int large;
        string rule;
        string previewPolicy;
        if (isItem)
        {
            var pair = Ccz66RevisedLayout.ResolveItemIconImageNumbers(fieldValue);
            small = pair.Small;
            large = pair.Large;
            rule = "item field N -> E5/Item.e5 small #2N+1, large #2N+2";
            previewPolicy = "large";
        }
        else
        {
            large = Ccz66RevisedLayout.ResolveStrategyIconImageNumber(fieldValue);
            rule = "strategy field N -> E5/Mtem.e5 image #N+1";
            previewPolicy = "single";
        }

        var inRange = !isE5 || (large > 0 && large <= entryCount && (!small.HasValue || small.Value <= entryCount));
        var error = string.Empty;
        if (!File.Exists(path))
        {
            error = "Icon resource file was not found.";
        }
        else if (isE5 && entryCount == 0)
        {
            error = "E5 resource has no readable 0x110 image entries.";
        }
        else if (!inRange)
        {
            error = $"Icon field {fieldValue} maps outside {Path.GetFileName(path)} entries; small={small?.ToString() ?? "-"}, large={large}, entries={entryCount}.";
        }

        if (Ccz66RevisedLayout.Is66(project) && !isE5)
        {
            warnings.Add("6.6 project resolved to a non-E5 icon resource. This indicates stale version detection or an obsolete entry point.");
        }

        return new ItemIconFieldMapping
        {
            Kind = normalizedKind,
            FieldValue = fieldValue,
            ResourceRelativePath = relative,
            ResourcePath = path,
            EntryCount = entryCount,
            SmallImageNumber = small,
            LargeImageNumber = large,
            Is66E5Resource = Ccz66RevisedLayout.Is66(project) && isE5,
            InRange = inRange && string.IsNullOrWhiteSpace(error),
            MappingRule = rule,
            PreviewPolicy = previewPolicy,
            Error = error,
            Warnings = warnings
        };
    }

    public static bool IsObsolete66DllIconResource(CczProject project, string relativeOrFileName)
    {
        if (!Ccz66RevisedLayout.Is66(project)) return false;
        var fileName = Path.GetFileName(relativeOrFileName.Replace('/', Path.DirectorySeparatorChar));
        return fileName.Equals("Itemicon.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildObsolete66DllIconMessage(CczProject project, string relativeOrFileName, int fieldValue)
    {
        var fileName = Path.GetFileName(relativeOrFileName.Replace('/', Path.DirectorySeparatorChar));
        var kind = fileName.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase) ? "strategy" : "item";
        var mapping = new ItemIconMappingService().Resolve(project, fieldValue, kind);
        var target = kind.Equals("strategy", StringComparison.OrdinalIgnoreCase)
            ? $"E5/Mtem.e5 image #{mapping.LargeImageNumber}"
            : $"E5/Item.e5 small #{mapping.SmallImageNumber}, large #{mapping.LargeImageNumber}";
        return $"ObsoleteRuntimeResource: {fileName} is not an active 6.6 icon resource. Use {target} for field value {fieldValue}.";
    }

    private static string NormalizeKind(string kind)
    {
        var text = (kind ?? string.Empty).Trim();
        if (text.Equals("strategy", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("magic", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("mtem", StringComparison.OrdinalIgnoreCase))
        {
            return "strategy";
        }

        return "item";
    }
}

