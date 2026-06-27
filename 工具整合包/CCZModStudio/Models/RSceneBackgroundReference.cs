using System.Globalization;

namespace CCZModStudio.Models;

public sealed class RSceneBackgroundReference
{
    public int Category { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public int RawValue { get; init; }
    public int? ResolvedImageNumber { get; init; }
    public string TargetResourceKind { get; init; } = string.Empty;
    public string Warning { get; init; } = string.Empty;

    public bool IsMmapBackground
        => TargetResourceKind.Equals("Mmap", StringComparison.OrdinalIgnoreCase);

    public string DisplayText
    {
        get
        {
            var resolved = ResolvedImageNumber.HasValue
                ? $" -> {TargetResourceKind} #{ResolvedImageNumber.Value.ToString(CultureInfo.InvariantCulture)}"
                : string.Empty;
            var warning = string.IsNullOrWhiteSpace(Warning) ? string.Empty : $"；{Warning}";
            return $"{CategoryName} 原图号 {RawValue.ToString(CultureInfo.InvariantCulture)}{resolved}{warning}";
        }
    }
}
