using System.Text;
using System.Text.Json;

namespace CCZModStudio;

public sealed class UiLayoutSettings
{
    public int Version { get; set; } = UiLayoutSettingsStore.Version;
    public Dictionary<string, double> SplitRatios { get; set; } = new(StringComparer.Ordinal);
    public int WindowLeft { get; set; }
    public int WindowTop { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}

public static class UiLayoutSettingsStore
{
    public const int Version = 2;

    private const string FileName = "ui-layout.json";
    private const string PathOverrideEnvironmentVariable = "CCZMODSTUDIO_UI_LAYOUT_PATH";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<string, string> LegacySplitKeyAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BuildLayout.tableSplit"] = "BuildLayout.GenericTableChart",
        ["BuildLayout.imageResourceSplit"] = "BuildImageResourcePage.ListPreview",
        ["BuildLayout.imageResourceLeft"] = "BuildImageResourcePage.FileEntryList",
        ["BuildLayout.imageSplit"] = "BuildImageAssignmentPage.GridPreview",
        ["BuildLayout.eexSplit"] = "BuildEexProbePage.ArchiveDetail",
        ["BuildLayout.eexProbeHeatmapSplit"] = "BuildEexProbePage.ProbeHeatmap",
        ["BuildLayout.scenarioSplit"] = "BuildScenarioProbePage.FileDetail",
        ["BuildLayout.structureSplit"] = "BuildScenarioProbePage.StructurePreview",
        ["BuildLayout.lsResourceSplit"] = "BuildLsResourcePage.ResourceHeatmap",
        ["BuildLayout.hexzmapSplit"] = "BuildHexzmapPage.GridPreview",
        ["BuildLayout.mapSplit"] = "BuildMapEditorPage.MapListEditor",
        ["BuildLayout.mapEditorSplit"] = "BuildMapEditorPage.CanvasMaterials",
        ["BuildMapEditorPage.mapSplit"] = "BuildMapEditorPage.MapListEditor",
        ["BuildMapEditorPage.mapEditorSplit"] = "BuildMapEditorPage.CanvasMaterials",
        ["BuildRoleEditorPage.roleSplit"] = "BuildRoleEditorPage.GridTextDetail",
        ["BuildScriptEditorPage.mainSplit"] = "BuildScriptEditorPage.TreeDetail"
    };

    public static UiLayoutSettings Load()
    {
        try
        {
            var path = GetPath();
            if (!File.Exists(path)) return new UiLayoutSettings();

            var settings = JsonSerializer.Deserialize<UiLayoutSettings>(
                File.ReadAllText(path, Encoding.UTF8),
                JsonOptions);
            if (settings == null) return new UiLayoutSettings();

            Normalize(settings);
            return settings;
        }
        catch
        {
            return new UiLayoutSettings();
        }
    }

    public static void Save(UiLayoutSettings settings, IEnumerable<string>? splitKeysToPersist = null, bool updateWindow = true)
    {
        try
        {
            Normalize(settings);
            var target = Load();
            target.Version = Version;

            if (updateWindow)
            {
                target.WindowLeft = settings.WindowLeft;
                target.WindowTop = settings.WindowTop;
                target.WindowWidth = settings.WindowWidth;
                target.WindowHeight = settings.WindowHeight;
                target.WindowMaximized = settings.WindowMaximized;
            }

            var keys = splitKeysToPersist?.Select(NormalizeKey).Distinct(StringComparer.Ordinal).ToList();
            var ratios = keys == null
                ? settings.SplitRatios
                : settings.SplitRatios.Where(pair => keys.Contains(pair.Key));
            foreach (var pair in ratios)
            {
                if (!IsValidRatio(pair.Value)) continue;
                target.SplitRatios[NormalizeKey(pair.Key)] = pair.Value;
            }

            var path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(target, JsonOptions), Encoding.UTF8);
        }
        catch
        {
            // Layout persistence is best effort; failed saves should not block editing.
        }
    }

    public static void SaveSplitRatio(string layoutKey, double ratio)
    {
        var settings = Load();
        var key = NormalizeKey(layoutKey);
        settings.SplitRatios[key] = Math.Clamp(ratio, 0.01, 0.99);
        Save(settings, [key], updateWindow: false);
    }

    public static double? GetSplitRatio(UiLayoutSettings settings, string layoutKey)
    {
        Normalize(settings);
        var key = NormalizeKey(layoutKey);
        return settings.SplitRatios.TryGetValue(key, out var ratio) && IsValidRatio(ratio)
            ? ratio
            : null;
    }

    public static string GetPath()
    {
        var overridePath = Environment.GetEnvironmentVariable(PathOverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrWhiteSpace(appData)
            ? Path.Combine(AppContext.BaseDirectory, FileName)
            : Path.Combine(appData, "CCZModStudio", FileName);
    }

    public static string NormalizeKey(string layoutKey)
        => layoutKey.Trim();

    private static void Normalize(UiLayoutSettings settings)
    {
        settings.Version = Version;
        if (settings.SplitRatios == null)
        {
            settings.SplitRatios = new Dictionary<string, double>(StringComparer.Ordinal);
            return;
        }

        var splitRatios = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in settings.SplitRatios)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || !IsValidRatio(pair.Value)) continue;
            var key = NormalizeKey(pair.Key);
            splitRatios[key] = pair.Value;
            if (LegacySplitKeyAliases.TryGetValue(key, out var currentKey) && !splitRatios.ContainsKey(currentKey))
            {
                splitRatios[currentKey] = pair.Value;
            }
        }

        settings.SplitRatios = splitRatios;
    }

    private static bool IsValidRatio(double ratio)
        => !double.IsNaN(ratio) && !double.IsInfinity(ratio) && ratio > 0 && ratio < 1;
}
