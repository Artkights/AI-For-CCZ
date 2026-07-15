using System.Drawing;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class PixelColorReplacementService
{
    public PixelColorReplacementPreview Preview(
        IReadOnlyList<(string Key, string DisplayName, Bitmap Bitmap)> documents,
        IReadOnlyList<PixelColorReplacementRule> rules)
    {
        ValidateRules(rules);
        var sourceLookup = rules
            .Select((rule, index) => (Key: NormalizeArgb(rule.Source), Index: index))
            .ToDictionary(item => item.Key, item => item.Index);
        var totals = new int[rules.Count];
        var matches = new List<PixelColorDocumentMatch>(documents.Count);

        foreach (var document in documents)
        {
            var counts = new int[rules.Count];
            for (var y = 0; y < document.Bitmap.Height; y++)
            {
                for (var x = 0; x < document.Bitmap.Width; x++)
                {
                    if (!sourceLookup.TryGetValue(NormalizeArgb(document.Bitmap.GetPixel(x, y)), out var index)) continue;
                    counts[index]++;
                    totals[index]++;
                }
            }

            matches.Add(new PixelColorDocumentMatch(document.Key, document.DisplayName, counts));
        }

        return new PixelColorReplacementPreview
        {
            Rules = rules.ToArray(),
            Documents = matches,
            RuleMatchCounts = totals
        };
    }

    public PixelColorReplacementResult Apply(
        IReadOnlyList<(string Key, string DisplayName, Bitmap Bitmap)> documents,
        IReadOnlyList<PixelColorReplacementRule> rules)
    {
        var preview = Preview(documents, rules);
        var replacements = rules.ToDictionary(
            rule => NormalizeArgb(rule.Source),
            rule => NormalizeColor(rule.Target));
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            for (var y = 0; y < document.Bitmap.Height; y++)
            {
                for (var x = 0; x < document.Bitmap.Width; x++)
                {
                    var original = document.Bitmap.GetPixel(x, y);
                    if (!replacements.TryGetValue(NormalizeArgb(original), out var replacement)) continue;
                    document.Bitmap.SetPixel(x, y, replacement);
                    changed.Add(document.Key);
                }
            }
        }

        return new PixelColorReplacementResult
        {
            Preview = preview,
            ChangedDocumentKeys = changed.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public IReadOnlyDictionary<int, int> CountColors(IEnumerable<Bitmap> bitmaps)
    {
        var counts = new Dictionary<int, int>();
        foreach (var bitmap in bitmaps)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var key = NormalizeArgb(bitmap.GetPixel(x, y));
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }
        }

        return counts;
    }

    public static int NormalizeArgb(Color color) => color.A == 0 ? 0 : color.ToArgb();

    public static Color NormalizeColor(Color color) => color.A == 0 ? Color.Transparent : color;

    private static void ValidateRules(IReadOnlyList<PixelColorReplacementRule> rules)
    {
        if (rules.Count is < 1 or > 5)
        {
            throw new InvalidOperationException("换色规则必须为 1 至 5 组。");
        }

        var duplicated = rules
            .GroupBy(rule => NormalizeArgb(rule.Source))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicated != null)
        {
            throw new InvalidOperationException("同一种源颜色不能重复配置多条换色规则。");
        }

        if (rules.Any(rule => NormalizeArgb(rule.Source) == NormalizeArgb(rule.Target)))
        {
            throw new InvalidOperationException("源颜色与目标颜色不能相同。");
        }
    }
}
