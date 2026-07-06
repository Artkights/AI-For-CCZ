using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Text;

internal partial class Program
{
    static void RunGlobalNumericEvidenceSmoke()
    {
        var project = new ProjectDetector().DetectDefaultProject();
        var tables = new HexTableParser().Load(project.HexTableXmlPath);
        var document = new GlobalSettingsService().Load(project, tables);

        if (document.NumericSettings.Count < 10)
        {
            throw new InvalidOperationException("Expected at least 10 global numeric settings.");
        }

        var editable = document.NumericSettings.Where(setting => setting.CanEdit).ToList();
        var editableKeys = editable.Select(setting => setting.Key).OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!editableKeys.SequenceEqual([
                "EquipmentLevelLimitNormal",
                "EquipmentLevelLimitSpecial",
                "EquipmentLevelRaiseNormal",
                "EquipmentLevelRaiseSpecial",
                "LevelLimit",
                "PromotionLevelFirst",
                "UpgradeExperience"
            ], StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unexpected editable global numeric settings after official diff validation: " +
                                                string.Join(", ", editable.Select(setting => setting.Key)));
        }

        foreach (var setting in document.NumericSettings)
        {
            if (setting.CanEdit)
            {
                if (setting.Status != "已验证" ||
                    setting.TargetFileName != "Ekd5.exe" ||
                    setting.ByteLength != 1 ||
                    setting.WriteTargets.Count == 0)
                {
                    throw new InvalidOperationException($"Unexpected verified global numeric metadata for {setting.Key}.");
                }

                continue;
            }

            if (setting.OracleCoverage == "ParentKeyUseLeaf")
            {
                if (setting.Status != "组合项" ||
                    setting.ByteLength != 0 ||
                    !string.IsNullOrWhiteSpace(setting.TargetFileName))
                {
                    throw new InvalidOperationException($"Unexpected parent global numeric status for {setting.Key}.");
                }

                continue;
            }

            if (setting.OracleCoverage != "NeedsUiOrDiffExtraction" ||
                setting.Status != "待验证" ||
                setting.ByteLength != 0 ||
                !string.IsNullOrWhiteSpace(setting.TargetFileName))
            {
                throw new InvalidOperationException($"Unexpected global numeric status for {setting.Key}: status={setting.Status}, oracle={setting.OracleCoverage}, target={setting.TargetFileName}, bytes={setting.ByteLength}.");
            }
        }

        var oracle = new OfficialImageAssignerOracleService().Detect(project);
        if (!oracle.Found || !oracle.Config.Found)
        {
            throw new InvalidOperationException("Official image assigner oracle config was not found.");
        }

        foreach (var unexpected in document.NumericSettings.Select(setting => setting.Key))
        {
            if (oracle.Config.RawValues.ContainsKey(unexpected) || oracle.Config.NumericValues.ContainsKey(unexpected))
            {
                throw new InvalidOperationException("System.ini unexpectedly exposes a global numeric key: " + unexpected);
            }
        }

        AssertParentNumericKeyBlocked(document, "PromotionLevel");
        AssertParentNumericKeyBlocked(document, "EquipmentLevelLimit");
        AssertParentNumericKeyBlocked(document, "EquipmentLevelRaise");

        var exePath = project.ResolveGameFile("Ekd5.exe");
        var mapper = PeAddressMapper.Load(exePath);
        var offset = mapper.VirtualAddressToFileOffset(0x0048D3C4);
        var bytes = File.ReadAllBytes(exePath);
        if (offset < 0 || offset + 3 > bytes.Length)
        {
            throw new InvalidOperationException("CMF example address 0048D3C4 did not map into Ekd5.exe.");
        }

        if (bytes[offset] == 60 && bytes[offset + 1] == 73)
        {
            throw new InvalidOperationException("CMF example address 0048D3C4 unexpectedly matches level/exp defaults; re-evaluate global numeric locks.");
        }

        var preview = Encoding.GetEncoding(936).GetString(bytes, checked((int)offset), Math.Min(16, bytes.Length - checked((int)offset)));
        if (preview.Length == 0)
        {
            throw new InvalidOperationException("CMF example address 0048D3C4 produced an empty preview.");
        }

        Console.WriteLine(
            "GLOBAL_NUMERIC_EVIDENCE_SMOKE_OK " +
            $"editable={editable.Count} locked={document.NumericSettings.Count - editable.Count} " +
            $"cmfExampleVA=0048D3C4 fileOffset=0x{offset:X} firstBytes={bytes[offset]:X2}-{bytes[offset + 1]:X2}-{bytes[offset + 2]:X2} " +
            $"preview={preview.Replace('\r', ' ').Replace('\n', ' ')}");
    }

    private static void AssertParentNumericKeyBlocked(GlobalSettingsDocument document, string key)
    {
        try
        {
            new GlobalSettingsService().PreviewNumericSettings(
                document,
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [key] = 1 });
            throw new InvalidOperationException("Parent global numeric key was not blocked: " + key);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("leaf key", StringComparison.OrdinalIgnoreCase) ||
                                                   ex.Message.Contains("组合", StringComparison.OrdinalIgnoreCase))
        {
            // Expected: parent keys are display-only.
        }
    }
}
