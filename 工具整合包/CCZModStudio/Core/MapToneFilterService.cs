using System.Drawing;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MapToneFilterService
{
    private readonly TerrainMapBeautifyService _legacyFilter = new();

    public Bitmap Apply(
        Bitmap source,
        TerrainRenderSettings settings,
        BeautifyCustomFilterSettings? custom = null)
    {
        if (settings == null ||
            settings.ToneAmount <= 0.001f ||
            settings.ToneProfile.Equals(TerrainToneProfiles.Neutral, StringComparison.OrdinalIgnoreCase))
        {
            return new Bitmap(source);
        }

        var draft = new MapWorkbenchDraft
        {
            GridWidth = Math.Max(1, source.Width / MapResourceItem.MapTilePixelSize),
            GridHeight = Math.Max(1, source.Height / MapResourceItem.MapTilePixelSize),
            BeautifyGeneratedMap = true,
            BeautifyStrength = Math.Clamp((int)MathF.Ceiling(settings.ToneAmount * 3f), 1, 3),
            BeautifyFilterProfile = ToLegacyProfile(settings.ToneProfile),
            CustomBeautifyFilter = custom?.Clone()
        };
        return _legacyFilter.ApplyFilter(draft, source, custom);
    }

    private static string ToLegacyProfile(string profile)
        => profile switch
        {
            TerrainToneProfiles.Night => TerrainBeautifyFilterProfiles.Night,
            TerrainToneProfiles.Autumn => TerrainBeautifyFilterProfiles.Autumn,
            TerrainToneProfiles.Winter => TerrainBeautifyFilterProfiles.Winter,
            TerrainToneProfiles.WarmSun => TerrainBeautifyFilterProfiles.WarmSun,
            TerrainToneProfiles.Custom => TerrainBeautifyFilterProfiles.Custom,
            _ => TerrainBeautifyFilterProfiles.Natural
        };
}
