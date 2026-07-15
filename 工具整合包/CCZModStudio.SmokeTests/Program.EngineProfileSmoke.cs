using CCZModStudio.Core;

internal partial class Program
{
    static void RunEngineProfileSmoke()
    {
        AssertEngineProfileEqual("6.4", CczEngineProfileService.MapOldWrenchLowWordToVersion(4), "old wrench low-word 4");
        AssertEngineProfileEqual("6.3", CczEngineProfileService.MapOldWrenchLowWordToVersion(3), "old wrench low-word 3");
        AssertEngineProfileEqual("6.2", CczEngineProfileService.MapOldWrenchLowWordToVersion(2), "old wrench low-word 2");
        AssertEngineProfileEqual("6.1", CczEngineProfileService.MapOldWrenchLowWordToVersion(1), "old wrench low-word 1");
        AssertEngineProfileEqual("6.5", CczEngineProfileService.MapOldWrenchLowWordToVersion(5), "old wrench low-word 5");
        AssertEngineProfileEqual("6.6", CczEngineProfileService.MapOldWrenchLowWordToVersion(6), "old wrench low-word 6");
        AssertEngineProfileEqual<string?>(null, CczEngineProfileService.MapOldWrenchLowWordToVersion(null), "old wrench low-word null");
        AssertEngineProfileEqual<string?>(null, CczEngineProfileService.MapOldWrenchLowWordToVersion(7), "old wrench low-word unknown");
        AssertEngineProfileEqual("6.6", CczEngineProfileService.InferVersionFromSha256("4A4FD8DDBF83E5F0B769D1B97BF8F6E6431C3AB42892024A354228212D3D06A4"), "6.6 known sha");

        var profile65 = CczEngineProfileService.BuildProfileForTest(
            CczEngineProfileService.Version65ExeSize,
            versionText: null,
            versionLowWord: null,
            pathHint: "测试路径 6.6");
        AssertEngineProfileEqual("6.5", profile65.VersionHint, "6.5 size priority");
        AssertEngineProfileEqual("6.5", profile65.TableVersionPrefix, "6.5 table prefix");
        AssertEngineProfileEqual(0x4A7B20u, profile65.LegacyRuntimeLayout?.WarArrayAddress, "6.5 war array");
        AssertEngineProfileEqual(0x30, profile65.LegacyRuntimeLayout?.WarRecordSize, "6.5 war stride");
        if (!profile65.Warnings.Any(w => w.Contains("冲突", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Conflicting path/version evidence did not produce a warning.");
        }

        var profile66 = CczEngineProfileService.BuildProfileForTest(
            CczEngineProfileService.Version66ExeSize,
            versionText: null,
            versionLowWord: null,
            pathHint: "测试路径 6.5");
        AssertEngineProfileEqual("6.6", profile66.VersionHint, "6.6 size priority");
        AssertEngineProfileEqual("6.6", profile66.TableVersionPrefix, "6.6 table prefix");
        AssertEngineProfileEqual(null, profile66.LegacyRuntimeLayout, "6.6 no verified legacy runtime layout");

        var profileQinger = CczEngineProfileService.BuildProfileForTest(
            exeSize: 1_413_120,
            versionText: "1, 0, 0, 1",
            versionLowWord: 6,
            pathHint: @"基底\清儿吕布传 path 6.5");
        AssertEngineProfileEqual("6.6", profileQinger.VersionHint, "Qinger low-word 6 profile");
        AssertEngineProfileEqual("6.6", profileQinger.TableVersionPrefix, "Qinger 6.6 table prefix");
        AssertEngineProfileEqual("old-wrench FileVersionLS low word", profileQinger.DetectionSource, "Qinger detection source");
        AssertEngineProfileEqual(null, profileQinger.LegacyRuntimeLayout, "Qinger 6.6 no legacy runtime layout");
        if (!profileQinger.Warnings.Any(w => w.Contains("冲突", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Qinger low-word/path conflict did not produce a warning.");
        }

        var qingerRoot = Path.Combine(Environment.CurrentDirectory, "基底", "清儿吕布传");
        if (File.Exists(Path.Combine(qingerRoot, "Ekd5.exe")))
        {
            var qingerProject = new ProjectDetector().CreateProjectFromGameRoot(qingerRoot);
            var realQingerProfile = new CczEngineProfileService().Detect(qingerProject);
            AssertEngineProfileEqual("6.6", realQingerProfile.VersionHint, "real Qinger project profile");
            AssertEngineProfileEqual("6.6", realQingerProfile.TableVersionPrefix, "real Qinger table prefix");
            AssertEngineProfileEqual(6, realQingerProfile.VersionResourceLowWord, "real Qinger low word");
            AssertEngineProfileEqual(1_413_120L, realQingerProfile.ExeSize, "real Qinger exe size");
            AssertEngineProfileEqual("old-wrench FileVersionLS low word", realQingerProfile.DetectionSource, "real Qinger detection source");
        }

        var profileLowWord65 = CczEngineProfileService.BuildProfileForTest(
            exeSize: 1_413_120,
            versionText: "1, 0, 0, 1",
            versionLowWord: 5,
            pathHint: null);
        AssertEngineProfileEqual("6.5", profileLowWord65.VersionHint, "old wrench low-word 5 profile");

        var profileLowWordBeatsSize = CczEngineProfileService.BuildProfileForTest(
            CczEngineProfileService.Version65ExeSize,
            versionText: null,
            versionLowWord: 6,
            pathHint: null);
        AssertEngineProfileEqual("6.6", profileLowWordBeatsSize.VersionHint, "old wrench low-word priority over exe size");

        var profile64 = CczEngineProfileService.BuildProfileForTest(
            exeSize: 123,
            versionText: null,
            versionLowWord: 4,
            pathHint: null);
        AssertEngineProfileEqual("6.4", profile64.VersionHint, "old wrench low-word profile");
        AssertEngineProfileEqual("6.5", profile64.TableVersionPrefix, "6.4 table fallback prefix");
        AssertEngineProfileEqual(0x4A7B20u, profile64.LegacyRuntimeLayout?.WarArrayAddress, "6.4 war array");
        AssertEngineProfileEqual(0x1B, profile64.LegacyRuntimeLayout?.CharacterMaxHpOffset, "6.4 max HP offset");
        AssertEngineProfileEqual(2, profile64.LegacyRuntimeLayout?.CharacterMaxMpByteWidth, "6.4 max MP width");

        var profile63 = CczEngineProfileService.BuildProfileForTest(
            exeSize: 123,
            versionText: "6.3.0.0",
            versionLowWord: null,
            pathHint: null);
        AssertEngineProfileEqual("6.3", profile63.VersionHint, "version resource text profile");
        AssertEngineProfileEqual(0x4B2C50u, profile63.LegacyRuntimeLayout?.WarArrayAddress, "6.3 war array");
        AssertEngineProfileEqual(0x24, profile63.LegacyRuntimeLayout?.WarRecordSize, "6.3 war stride");
        AssertEngineProfileEqual(0x1C, profile63.LegacyRuntimeLayout?.CharacterMaxHpOffset, "6.3 max HP offset");
        AssertEngineProfileEqual(1, profile63.LegacyRuntimeLayout?.CharacterMaxMpByteWidth, "6.3 max MP width");

        RunEngineProfileCacheSmoke();

        Console.WriteLine("ENGINE_PROFILE_SMOKE_OK");
    }

    static void RunEngineProfileCacheSmoke()
    {
        var root = Path.Combine(Path.GetTempPath(), "CCZModStudio_EngineProfileCache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "Ekd5.exe"), new byte[4096]);
            File.WriteAllBytes(Path.Combine(root, "Data.e5"), [1]);
            File.WriteAllBytes(Path.Combine(root, "Imsg.e5"), [2]);
            File.WriteAllBytes(Path.Combine(root, "Star.e5"), [3]);
            var project = new ProjectDetector().CreateProjectFromGameRoot(root);
            CczEngineProfileService.ClearCache();
            PerformanceMetrics.Reset();
            var profiles = Enumerable.Range(0, 50)
                .AsParallel()
                .Select(_ => new CczEngineProfileService().Detect(project))
                .ToArray();
            var snapshot = PerformanceMetrics.GetSnapshot();
            var hashCount = snapshot.Counters.GetValueOrDefault("ExecutableAnalysis.HashCount");
            AssertEngineProfileEqual(1L, hashCount, "concurrent shared EXE hash count");
            AssertEngineProfileEqual(0L, snapshot.Counters.GetValueOrDefault("EngineProfile.ExeHashCount"), "engine profile must not hash independently");

            profiles[0].Warnings.Add("caller mutation");
            var clean = new CczEngineProfileService().Detect(project);
            if (clean.Warnings.Contains("caller mutation", StringComparer.Ordinal))
                throw new InvalidOperationException("Engine profile cache returned a shared mutable warnings list.");

            CczEngineProfileService.Invalidate(Path.Combine(root, "Ekd5.exe"));
            _ = new CczEngineProfileService().Detect(project);
            snapshot = PerformanceMetrics.GetSnapshot();
            AssertEngineProfileEqual(2L, snapshot.Counters.GetValueOrDefault("ExecutableAnalysis.HashCount"), "shared hash count after explicit invalidation");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            CczEngineProfileService.ClearCache();
        }
    }

    static void AssertEngineProfileEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected={expected}, actual={actual}");
        }
    }
}
