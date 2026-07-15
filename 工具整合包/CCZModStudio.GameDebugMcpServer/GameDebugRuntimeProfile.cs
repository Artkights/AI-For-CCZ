using System.Diagnostics;
using System.Globalization;
using CCZModStudio.Core;

namespace CCZModStudio.GameDebugMcpServer;

internal sealed record class GameDebugRuntimeProfile
{
    public string EngineVersion { get; init; } = "unknown";
    public string DetectionSource { get; init; } = "fallback";
    public bool IsKnown { get; init; }
    public bool IsRuntimeBattleLayoutVerified { get; init; }
    public string LayoutSource { get; init; } = string.Empty;
    public string UnsupportedReason { get; init; } = string.Empty;
    public uint UnitArrayAddress { get; init; }
    public int UnitStride { get; init; }
    public int UnitDataIdOffset { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitDataIdOffset;
    public int UnitDataIdByteWidth { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitDataIdWidth;
    public int UnitDisplayIdOffset { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitDisplayIdOffset;
    public int UnitDisplayIdByteWidth { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitDisplayIdWidth;
    public int UnitSideOffset { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitSideOffset;
    public int UnitXOffset { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitXOffset;
    public int UnitYOffset { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitYOffset;
    public int UnitActionOffset { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitActionOffset;
    public int UnitCurrentHpOffset { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitCurrentHpOffset;
    public int UnitCurrentHpByteWidth { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitCurrentValueWidth;
    public int UnitCurrentMpOffset { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitCurrentMpOffset;
    public int UnitCurrentMpByteWidth { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitCurrentValueWidth;
    public int UnitAttributesOffset { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitAttributesOffset;
    public int UnitAttributesLength { get; init; } = EngineRuntimeSemanticRegistry.TacticalUnitAttributesLength;
    public long? ExeSize { get; init; }
    public string ExeSha256 { get; init; } = string.Empty;
    public string VersionResourceText { get; init; } = string.Empty;
    public int? VersionResourceLowWord { get; init; }
    public string PathHint { get; init; } = string.Empty;
    public IReadOnlyList<GameDebugRuntimeProfileEvidence> Evidence { get; init; } = [];

    public object ToReport()
        => new
        {
            engine_version = EngineVersion,
            detection_source = DetectionSource,
            is_known = IsKnown,
            battle_layout_verified = IsRuntimeBattleLayoutVerified,
            layout_source = LayoutSource,
            unsupported_reason = UnsupportedReason,
            unit_array_address = UnitArrayAddress == 0 ? string.Empty : UnitArrayAddress.ToString("X8", CultureInfo.InvariantCulture),
            unit_stride = UnitStride == 0 ? string.Empty : UnitStride.ToString("X", CultureInfo.InvariantCulture),
            unit_data_id_offset = UnitDataIdOffset,
            unit_data_id_width = UnitDataIdByteWidth,
            unit_display_id_offset = UnitDisplayIdOffset,
            unit_display_id_width = UnitDisplayIdByteWidth,
            unit_hp_width = UnitCurrentHpByteWidth,
            unit_mp_width = UnitCurrentMpByteWidth,
            exe_size = ExeSize,
            exe_sha256 = ExeSha256,
            version_resource_text = VersionResourceText,
            version_resource_low_word = VersionResourceLowWord,
            path_hint = PathHint,
            evidence = Evidence
        };

    public static GameDebugRuntimeProfile Detect(GamePaths paths)
    {
        var exeSize = TryGetLength(paths.ExePath);
        var versionInfo = TryReadVersionInfo(paths.ExePath);
        var pathHint = Extract6xVersionHint(paths.GameRoot) ?? Extract6xVersionHint(paths.WorkspaceRoot);
        var evidence = BuildEvidence(exeSize, versionInfo.Text, versionInfo.LowWord, pathHint, paths.ExeSha256);
        var selected = evidence
            .Where(item => !string.IsNullOrWhiteSpace(item.VersionHint))
            .OrderBy(item => item.Priority)
            .FirstOrDefault();
        var version = selected?.VersionHint ?? "unknown";
        var profile = version switch
        {
            "6.5" => new GameDebugRuntimeProfile
            {
                EngineVersion = "6.5",
                DetectionSource = selected?.Kind ?? "fallback",
                IsKnown = true,
                IsRuntimeBattleLayoutVerified = true,
                LayoutSource = "current-project-debug-and-old-wrench-comparison",
                UnitArrayAddress = EngineRuntimeSemanticRegistry.TacticalUnitArrayAddress,
                UnitStride = EngineRuntimeSemanticRegistry.TacticalUnitStride
            },
            "6.6" => new GameDebugRuntimeProfile
            {
                EngineVersion = "6.6",
                DetectionSource = selected?.Kind ?? "fallback",
                IsKnown = true,
                UnsupportedReason = "6.6 runtime battle layout is not verified; refusing to read the 6.5 unit-array address."
            },
            "6.1" or "6.2" or "6.3" or "6.4" => new GameDebugRuntimeProfile
            {
                EngineVersion = version,
                DetectionSource = selected?.Kind ?? "fallback",
                IsKnown = true,
                UnsupportedReason = "Old-wrench runtime layout exists for this version, but this MCP battle reader has only verified the current 6.5 tactical-unit field map."
            },
            _ => new GameDebugRuntimeProfile
            {
                EngineVersion = version,
                DetectionSource = selected?.Kind ?? "fallback",
                UnsupportedReason = "Unable to identify a verified runtime battle layout."
            }
        };

        return profile with
        {
            ExeSize = exeSize,
            ExeSha256 = paths.ExeSha256,
            VersionResourceText = versionInfo.Text ?? string.Empty,
            VersionResourceLowWord = versionInfo.LowWord,
            PathHint = pathHint ?? string.Empty,
            Evidence = evidence
        };
    }

    public GameDebugRuntimeProfile RequireBattleLayout()
    {
        if (!IsRuntimeBattleLayoutVerified || UnitArrayAddress == 0 || UnitStride <= 0)
        {
            throw new InvalidOperationException($"Runtime battle layout is not verified for engine {EngineVersion}: {UnsupportedReason}");
        }

        return this;
    }

    private static IReadOnlyList<GameDebugRuntimeProfileEvidence> BuildEvidence(long? exeSize, string? versionText, int? lowWord, string? pathHint, string sha256)
    {
        var evidence = new List<GameDebugRuntimeProfileEvidence>();
        var shaVersion = GameDebugRuntime.IsKnown65EffectProfileSha(sha256) ? "6.5" : string.Empty;
        if (!string.IsNullOrWhiteSpace(sha256))
        {
            evidence.Add(new GameDebugRuntimeProfileEvidence("Ekd5.exe SHA256", sha256, shaVersion, 5));
        }

        var sizeVersion = exeSize switch
        {
            1_130_496 => "6.6",
            1_196_032 => "6.5",
            _ => string.Empty
        };
        if (exeSize is not null)
        {
            evidence.Add(new GameDebugRuntimeProfileEvidence("Ekd5.exe size", exeSize.Value.ToString(CultureInfo.InvariantCulture), sizeVersion, 10));
        }

        if (!string.IsNullOrWhiteSpace(versionText))
        {
            evidence.Add(new GameDebugRuntimeProfileEvidence("Ekd5.exe version resource", versionText, NormalizeVersionHint(versionText) ?? string.Empty, 20));
        }

        if (lowWord is not null)
        {
            evidence.Add(new GameDebugRuntimeProfileEvidence("old-wrench FileVersionLS low word", lowWord.Value.ToString(CultureInfo.InvariantCulture), MapOldWrenchLowWordToVersion(lowWord.Value), 30));
        }

        if (!string.IsNullOrWhiteSpace(pathHint))
        {
            evidence.Add(new GameDebugRuntimeProfileEvidence("path hint", pathHint, NormalizeVersionHint(pathHint) ?? string.Empty, 40));
        }

        return evidence;
    }

    private static string MapOldWrenchLowWordToVersion(int lowWord)
        => lowWord switch
        {
            4 => "6.4",
            3 => "6.3",
            2 => "6.2",
            _ => "6.1"
        };

    private static long? TryGetLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch
        {
            return null;
        }
    }

    private static (string? Text, int? LowWord) TryReadVersionInfo(string path)
    {
        try
        {
            if (!File.Exists(path)) return (null, null);
            var info = FileVersionInfo.GetVersionInfo(path);
            var text = FirstNonEmpty(info.FileVersion, info.ProductVersion);
            var lowWord = info.FilePrivatePart != 0 || info.FileBuildPart != 0 || info.FileMinorPart != 0 || info.FileMajorPart != 0
                ? info.FilePrivatePart
                : (int?)null;
            return (text, lowWord);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? NormalizeVersionHint(string? value)
    {
        var extracted = Extract6xVersionHint(value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(extracted)) return null;

        var suffix = extracted[2..];
        var digits = new string(suffix.TakeWhile(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits)) return null;
        return "6." + digits;
    }

    private static string? Extract6xVersionHint(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        for (var i = 0; i < value.Length - 2; i++)
        {
            if (value[i] != '6' || value[i + 1] != '.' || !char.IsLetterOrDigit(value[i + 2])) continue;
            var end = i + 3;
            while (end < value.Length && char.IsLetterOrDigit(value[end])) end++;
            return value[i..end];
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

internal sealed record GameDebugRuntimeProfileEvidence(string Kind, string Value, string VersionHint, int Priority);
