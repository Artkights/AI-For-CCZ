using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;
using System.Text.Json;

internal partial class Program
{
    static void RunGlobalNumericWriteSmoke()
    {
        var project = new ProjectDetector().DetectDefaultProject();
        var tables = new HexTableParser().Load(project.HexTableXmlPath);
        var service = new GlobalSettingsService();
        var document = service.Load(project, tables);

        AssertPendingNumericStillBlocked(service, document);

        var smokeRoot = Path.Combine(
            project.WorkspaceRoot,
            "CCZModStudio_TestCopies",
            "GlobalNumericWriteSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);

        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" })
        {
            var source = Path.Combine(project.GameRoot, fileName);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("全局数字参数写入烟测缺少核心文件。", source);
            }

            File.Copy(source, Path.Combine(smokeRoot, fileName), overwrite: false);
        }

        File.WriteAllText(
            Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=global numeric write smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var verifiedKeys = service.Load(testProject, tables)
            .NumericSettings
            .Where(setting => setting.CanEdit)
            .Select(setting => setting.Key)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var key in verifiedKeys)
        {
            RoundTripNumericSetting(testProject, tables, key);
        }

        Console.WriteLine("GLOBAL_NUMERIC_WRITE_SMOKE_OK verified=" + string.Join(",", verifiedKeys) + " root=" + smokeRoot);
    }

    private static void AssertPendingNumericStillBlocked(GlobalSettingsService service, GlobalSettingsDocument document)
    {
        foreach (var pendingKey in new[] { "AbilityThreshold", "EquipmentExp", "Merit" })
        {
            try
            {
                service.PreviewNumericSettings(
                    document,
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [pendingKey] = 136 });
                throw new InvalidOperationException("Unverified global numeric setting preview was not blocked: " + pendingKey);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("diff", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("未完成", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("不能", StringComparison.OrdinalIgnoreCase))
            {
                // Expected: only fields with official single-field diff evidence may write.
            }
        }

        foreach (var parentKey in new[] { "PromotionLevel", "EquipmentLevelLimit", "EquipmentLevelRaise" })
        {
            try
            {
                service.PreviewNumericSettings(
                    document,
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [parentKey] = 1 });
                throw new InvalidOperationException("Parent global numeric key preview was not blocked: " + parentKey);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("leaf key", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("组合", StringComparison.OrdinalIgnoreCase))
            {
                // Expected: parent keys are display-only.
            }
        }

        foreach (var pendingLeaf in new[]
                 {
                     "PromotionLevelSecond",
                     "MiddleEquipmentLevel"
                 })
        {
            try
            {
                service.PreviewNumericSettings(
                    document,
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [pendingLeaf] = 21 });
                throw new InvalidOperationException("Pending low-risk leaf preview was not blocked: " + pendingLeaf);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("diff", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("未完成", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("不能", StringComparison.OrdinalIgnoreCase))
            {
                // Expected: low-risk leaf keys require official diff before writing.
            }
        }
    }

    private static void RoundTripNumericSetting(CczProject testProject, IReadOnlyList<HexTableDefinition> tables, string key)
    {
        var service = new GlobalSettingsService();
        var before = service.Load(testProject, tables);
        var setting = before.NumericSettings.Single(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (!setting.CanEdit)
        {
            throw new InvalidOperationException("Expected verified numeric setting to be editable: " + key);
        }

        if (!int.TryParse(setting.CurrentValueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var original))
        {
            throw new InvalidOperationException($"Numeric setting {key} did not read a numeric current value: {setting.CurrentValueText}");
        }

        var changed = original < setting.MaxValue ? original + 1 : original - 1;
        var preview = service.PreviewNumericSettings(
            testProject,
            before,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [key] = changed });
        if (preview.Count != 1)
        {
            throw new InvalidOperationException("Numeric setting preview did not report exactly one semantic field: " + key);
        }
        var targetPreviews = ExtractPreviewTargets(preview[0], key);

        var write = service.SaveNumericSettings(
            testProject,
            before,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [key] = changed });
        if (write.ChangedBytes <= 0 ||
            write.BackupPaths.Count == 0 ||
            write.ReportJsonPaths.Count == 0 ||
            write.BackupPaths.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)) ||
            write.ReportJsonPaths.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)))
        {
                throw new InvalidOperationException("Numeric setting write did not create backup/report or changed no bytes: " + key);
        }
        AssertPreviewTargetsWritten(testProject, targetPreviews, key);

        var after = service.Load(testProject, tables);
        var reread = after.NumericSettings.Single(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (reread.CurrentValueText != changed.ToString(CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException($"Numeric setting reread failed for {key}: expected {changed}, actual {reread.CurrentValueText}");
        }

        var restore = service.SaveNumericSettings(
            testProject,
            after,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [key] = original });
        if (restore.ChangedBytes <= 0)
        {
            throw new InvalidOperationException("Numeric setting restore changed no bytes: " + key);
        }

        var restored = service.Load(testProject, tables)
            .NumericSettings.Single(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (restored.CurrentValueText != original.ToString(CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException($"Numeric setting restore reread failed for {key}: expected {original}, actual {restored.CurrentValueText}");
        }
    }

    private static IReadOnlyList<PreviewTarget> ExtractPreviewTargets(object preview, string key)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(preview));
        if (!doc.RootElement.TryGetProperty("Targets", out var targets) || targets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Numeric setting preview did not expose target offsets: " + key);
        }

        var result = new List<PreviewTarget>();
        foreach (var target in targets.EnumerateArray())
        {
            result.Add(new PreviewTarget(
                target.GetProperty("TargetFileName").GetString() ?? string.Empty,
                ParseHexOffset(target.GetProperty("Offset").GetString() ?? string.Empty),
                target.GetProperty("NewBytesHex").GetString() ?? string.Empty));
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException("Numeric setting preview had no target offsets: " + key);
        }

        return result;
    }

    private static void AssertPreviewTargetsWritten(CczProject project, IReadOnlyList<PreviewTarget> targets, string key)
    {
        foreach (var target in targets)
        {
            var path = project.ResolveGameFile(target.TargetFileName);
            var bytes = File.ReadAllBytes(path);
            var expected = Convert.FromHexString(target.NewBytesHex);
            for (var i = 0; i < expected.Length; i++)
            {
                var actual = bytes[checked((int)target.FileOffset) + i];
                if (actual != expected[i])
                {
                    throw new InvalidOperationException(
                        $"Numeric setting target byte mismatch for {key}: {target.TargetFileName}@0x{target.FileOffset:X} expected={target.NewBytesHex}.");
                }
            }
        }
    }

    private static long ParseHexOffset(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return long.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private sealed record PreviewTarget(string TargetFileName, long FileOffset, string NewBytesHex);
}
