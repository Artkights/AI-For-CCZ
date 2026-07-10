using CCZModStudio.Core;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunCmfManualSeedSmoke()
    {
        var sourceCmf = Path.GetFullPath(Path.Combine(
            Environment.CurrentDirectory,
            "老版游戏制作工具",
            "Star6.5引擎exe修改器.cmf"));
        if (!File.Exists(sourceCmf))
        {
            throw new FileNotFoundException("Star6.5 CMF source file was not found for manual seed smoke.", sourceCmf);
        }

        var sourceCmf66 = Path.GetFullPath(Path.Combine(
            Environment.CurrentDirectory,
            "老版游戏制作工具",
            "Star6.6X 引擎.cmf"));

        using var temp = new TemporarySmokeDirectory("CmfManualSeed");
        var oldToolsRoot = Path.Combine(temp.Path, Path.GetFileName(Path.GetDirectoryName(sourceCmf)!));
        Directory.CreateDirectory(oldToolsRoot);
        var cmfCopy = Path.Combine(oldToolsRoot, Path.GetFileName(sourceCmf));
        File.Copy(sourceCmf, cmfCopy);
        if (File.Exists(sourceCmf66))
        {
            File.Copy(sourceCmf66, Path.Combine(oldToolsRoot, Path.GetFileName(sourceCmf66)));
        }

        var gameRoot = Path.Combine(temp.Path, "Game");
        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(Path.Combine(gameRoot, "_CCZModStudio_TestCopy.txt"), "CMF manual seed smoke.");
        var exePath = Path.Combine(gameRoot, "Ekd5.exe");
        var exeBytes = new byte[0x40000];
        exeBytes[0x207A4] = 0x74;
        exeBytes[0x2CD9] = 0xEB;
        File.WriteAllBytes(exePath, exeBytes);
        var hexTablePath = Path.Combine(temp.Path, "HexTable.xml");
        File.WriteAllText(hexTablePath, "<Root />");

        var project = new CczProject
        {
            WorkspaceRoot = temp.Path,
            GameRoot = gameRoot,
            HexTableXmlPath = hexTablePath
        };

        var seedService = new CmfManualSeedService();
        var validation = seedService.ValidateSeeds(project);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("CMF manual seed validation failed: " + string.Join("; ", validation.Issues.Select(issue => issue.Code + "=" + issue.Message)));
        }

        if (validation.FieldCount != 53 || validation.TableCount != 5 || validation.ExpandedTableEntryCount != 86)
        {
            throw new InvalidOperationException($"Unexpected manual seed counts: fields={validation.FieldCount}, tables={validation.TableCount}, entries={validation.ExpandedTableEntryCount}");
        }

        var snapshot = seedService.TryCreateSnapshotForRelativePath(project, "Star6.5引擎exe修改器.cmf")
            ?? throw new InvalidOperationException("Manual seed snapshot was not created.");
        if (snapshot.Bindings.Count != 86)
        {
            throw new InvalidOperationException("Manual seed snapshot should expose 86 bindings: " + snapshot.Bindings.Count);
        }

        var snapshot66 = seedService.TryCreateSnapshotForRelativePath(project, "Star6.6X 引擎.cmf")
            ?? throw new InvalidOperationException("Star6.6 manual seed snapshot was not created.");
        if (snapshot66.Bindings.Count != 53)
        {
            throw new InvalidOperationException("Star6.6 manual seed snapshot should expose 53 bindings: " + snapshot66.Bindings.Count);
        }

        var treasureMutationBinding = snapshot.Bindings.Single(binding => binding.BindingId == "star65-manual-field-treasure-mutation-level");
        if (treasureMutationBinding.UeOffset != 0x1F53E ||
            treasureMutationBinding.ByteLength != 1 ||
            !treasureMutationBinding.DataType.Equals("Decimal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Manual seed treasure mutation level binding was not imported correctly.");
        }

        var strategyBlockBinding = snapshot.Bindings.Single(binding => binding.BindingId == "star65-manual-field-strategy-block-weapon-exp");
        if (strategyBlockBinding.UeOffset != 0x2086C ||
            strategyBlockBinding.ByteLength != 1 ||
            !strategyBlockBinding.DataType.Equals("Decimal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Manual seed strategy block weapon exp binding should use corrected offset 0x2086C.");
        }

        var strategyBlockBinding66 = snapshot66.Bindings.Single(binding => binding.BindingId == "star66-manual-field-strategy-block-weapon-exp");
        if (strategyBlockBinding66.UeOffset != 0x2086C ||
            strategyBlockBinding66.ByteLength != 1 ||
            !strategyBlockBinding66.DataType.Equals("Decimal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Star6.6 manual seed strategy block weapon exp binding should use offset 0x2086C.");
        }

        var treasureMutationBinding66 = snapshot66.Bindings.Single(binding => binding.BindingId == "star66-manual-field-treasure-mutation-level");
        var treasureLeapBinding66 = snapshot66.Bindings.Single(binding => binding.BindingId == "star66-manual-field-treasure-leap-level");
        if (treasureMutationBinding66.UeOffset != 0x1F50F ||
            treasureLeapBinding66.UeOffset != 0x1F557)
        {
            throw new InvalidOperationException("Star6.6 treasure mutation/leap bindings should use 0x1F50F/0x1F557.");
        }

        if (snapshot66.Bindings.Any(binding =>
                (binding.BindingId.Contains("treasure-mutation-level", StringComparison.OrdinalIgnoreCase) && binding.UeOffset == 0x1F53E) ||
                (binding.BindingId.Contains("treasure-leap-level", StringComparison.OrdinalIgnoreCase) && binding.UeOffset == 0x1F56D)))
        {
            throw new InvalidOperationException("Star6.6 manual seed must not expose rejected 6.5 treasure level conflict offsets.");
        }

        var abnormalAbilityBinding = snapshot.Bindings.Single(binding => binding.BindingId == "star65-manual-field-abnormal-ability-attack");
        if (!abnormalAbilityBinding.SourceProperties.TryGetValue("displayFormat", out var displayFormat) ||
            !displayFormat.Equals("BareHex", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Manual seed abnormal ability fields should expose BareHex display metadata.");
        }

        var abnormalTurnBinding = snapshot.Bindings.Single(binding => binding.BindingId == "star65-manual-field-abnormal-turn-poison");
        if (!abnormalTurnBinding.SourceProperties.TryGetValue("valueKind", out var valueKind) ||
            !valueKind.Equals("ShiftedTwoBitDecimal", StringComparison.OrdinalIgnoreCase) ||
            !abnormalTurnBinding.SourceProperties.TryGetValue("shift", out var shift) ||
            shift != "6" ||
            !abnormalTurnBinding.SourceProperties.TryGetValue("maskHex", out var maskHex) ||
            !maskHex.Equals("0xC0", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Manual seed abnormal turn fields should expose shifted two-bit metadata.");
        }

        var terrainBindings = snapshot.Bindings
            .Where(binding => binding.BindingId.StartsWith("star65-manual-table-terrain-strategy-availability-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(binding => binding.UeOffset)
            .ToArray();
        if (terrainBindings.Length != 30 ||
            terrainBindings.First().UeOffset != 0x1FECC ||
            terrainBindings.Last().UeOffset != 0x1FEE9)
        {
            throw new InvalidOperationException("Manual terrain table did not expand to 0x1FECC..0x1FEE9.");
        }

        var equipmentTypeNameBindings = snapshot.Bindings
            .Where(binding => binding.BindingId.StartsWith("star65-manual-table-equipment-type-name-table-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(binding => binding.UeOffset)
            .ToArray();
        if (equipmentTypeNameBindings.Length != 15 ||
            equipmentTypeNameBindings.First().UeOffset != 0x8AC70 ||
            equipmentTypeNameBindings.Last().UeOffset != 0x8ACB6 ||
            equipmentTypeNameBindings.Any(binding => binding.ByteLength != 4 || binding.DataType != "GbkText"))
        {
            throw new InvalidOperationException("Manual equipment type name table did not expand to 15 fixed GBK entries.");
        }

        var equipmentTypeDisplayBindings = snapshot.Bindings
            .Where(binding => binding.BindingId.StartsWith("star65-manual-table-equipment-type-display-table-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(binding => binding.UeOffset)
            .ToArray();
        if (equipmentTypeDisplayBindings.Length != 13 ||
            equipmentTypeDisplayBindings.First().UeOffset != 0x81827 ||
            equipmentTypeDisplayBindings.Last().UeOffset != 0x81833 ||
            equipmentTypeDisplayBindings.Any(binding => binding.ByteLength != 1 || binding.DataType != "Hex"))
        {
            throw new InvalidOperationException("Manual equipment type display table did not expand to 13 hex entries.");
        }

        var defaultLengthFields = snapshot.Bindings
            .Where(binding => binding.SourceProperties.TryGetValue("lengthStatus", out var value) &&
                              value.Contains("默认长度", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (defaultLengthFields.Length < 13)
        {
            throw new InvalidOperationException("Expected default-length notes on manually defaulted fields.");
        }

        var derivedService = new CmfDerivedCapabilityService();
        var fields = derivedService.ListDesignerFields(project, "Star6.5引擎exe修改器.cmf");
        if (fields.All(field => !field.DisplayName.Contains("奋战攻击次数", StringComparison.OrdinalIgnoreCase)) ||
            fields.All(field => !field.DisplayName.Contains("地形可使用策略：地下", StringComparison.OrdinalIgnoreCase)) ||
            fields.All(field => !field.DisplayName.Contains("装备类型名称：物理武器 1", StringComparison.OrdinalIgnoreCase)) ||
            fields.All(field => !field.DisplayName.Contains("装备类型显示：护具 1", StringComparison.OrdinalIgnoreCase)) ||
            fields.All(field => !field.TrustLevel.Equals("ManualConfirmed", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Manual seed fields were not visible through ListDesignerFields.");
        }

        var keywordFields = derivedService.ListManualSeedFields(project, "经验");
        if (keywordFields.Count != 20)
        {
            throw new InvalidOperationException("Manual seed keyword filter for 经验 should return 20 fields, got " + keywordFields.Count);
        }

        var writeReport = derivedService.VerifyDesignerWrites(
            project,
            "Star6.5引擎exe修改器.cmf",
            new CmfDesignerWriteVerificationOptions
            {
                BindingIds =
                [
                    "star65-manual-field-weapon-strategy-exp",
                    "star65-manual-field-spirit-weapon-physical-exp"
                ],
                MaxFields = 2
            });

        if (writeReport.WriteVerifiedCount != 2 || writeReport.Fields.Any(field => field.FinalStatus != "WriteVerified"))
        {
            throw new InvalidOperationException("Manual checkbox fields did not reach WriteVerified on the test copy.");
        }

        if (File.ReadAllBytes(exePath)[0x207A4] != 0x74 || File.ReadAllBytes(exePath)[0x2CD9] != 0xEB)
        {
            throw new InvalidOperationException("Manual seed write verification modified the source project.");
        }

        var testExe = Path.Combine(writeReport.TestCopyRoot, "Ekd5.exe");
        var testBytes = File.ReadAllBytes(testExe);
        if (testBytes[0x207A4] != 0xEB || testBytes[0x2CD9] != 0x74)
        {
            throw new InvalidOperationException("Manual seed write verification did not flip checkbox bytes in the test copy.");
        }

        var numericReport = derivedService.VerifyDesignerWrites(
            project,
            "Star6.5引擎exe修改器.cmf",
            new CmfDesignerWriteVerificationOptions
            {
                BindingIds = [ "star65-manual-field-kill-ability-five-dim-demand" ],
                MaxFields = 1
            });
        var numericField = numericReport.Fields.Single();
        if (numericField.FinalStatus == "WriteVerified" || numericField.CanPromoteToWrite)
        {
            throw new InvalidOperationException("Manual numeric field without a test value should not become WriteVerified.");
        }

        Console.WriteLine(
            "CMF_MANUAL_SEED_SMOKE_OK " +
            $"fields={validation.FieldCount} seed66={snapshot66.Bindings.Count} terrain={terrainBindings.Length} equipmentType={equipmentTypeNameBindings.Length + equipmentTypeDisplayBindings.Length} " +
            $"designerFields={fields.Count} writeVerified={writeReport.WriteVerifiedCount} " +
            $"numericStatus={numericField.FinalStatus}");
    }
}
