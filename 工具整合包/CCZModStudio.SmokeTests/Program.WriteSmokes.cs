using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

internal partial class Program
{
    static void RunBattlefieldTextWriteSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "BattlefieldTextWriteSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("战场标题/胜败条件写入烟测缺少核心文件。", source);
            }

            File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
        }

        var rsRoot = Path.Combine(smokeRoot, "RS");
        Directory.CreateDirectory(rsRoot);
        var sourceBattlefieldScenarioPath = Path.Combine(project.GameRoot, "RS", "S_00.eex");
        if (!File.Exists(sourceBattlefieldScenarioPath))
        {
            sourceBattlefieldScenarioPath = Directory.GetFiles(Path.Combine(project.GameRoot, "RS"), "S_*.eex", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault()
                ?? throw new FileNotFoundException("Battlefield text write smoke could not find S_*.eex.", Path.Combine(project.GameRoot, "RS", "S_*.eex"));
        }

        var battlefieldScenarioFileName = Path.GetFileName(sourceBattlefieldScenarioPath);
        File.Copy(sourceBattlefieldScenarioPath, Path.Combine(rsRoot, battlefieldScenarioFileName), overwrite: false);
        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=Battlefield title/condition write smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        RunBattlefieldTextWriteSmokeLayered(project, testProject, tables, battlefieldScenarioFileName);
        Console.WriteLine($"BATTLEFIELD_TEXT_WRITE_ONLY_SMOKE_OK root={smokeRoot}");
    }

    static void RunBattlefieldDeploymentWriteSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "BattlefieldDeploymentWriteSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5", "Hexzmap.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Battlefield deployment write smoke requires core project files.", source);
            }

            File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
        }

        var rsRoot = Path.Combine(smokeRoot, "RS");
        Directory.CreateDirectory(rsRoot);
        var sourceBattlefieldScenarioPath = Path.Combine(project.GameRoot, "RS", "S_00.eex");
        if (!File.Exists(sourceBattlefieldScenarioPath))
        {
            sourceBattlefieldScenarioPath = Directory.GetFiles(Path.Combine(project.GameRoot, "RS"), "S_*.eex", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault()
                ?? throw new FileNotFoundException("Battlefield deployment write smoke could not find S_*.eex.", Path.Combine(project.GameRoot, "RS", "S_*.eex"));
        }

        var battlefieldScenarioFileName = Path.GetFileName(sourceBattlefieldScenarioPath);
        File.Copy(sourceBattlefieldScenarioPath, Path.Combine(rsRoot, battlefieldScenarioFileName), overwrite: false);
        var sourceS01 = Path.Combine(project.GameRoot, "RS", "S_01.eex");
        if (File.Exists(sourceS01) && !battlefieldScenarioFileName.Equals("S_01.eex", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceS01, Path.Combine(rsRoot, "S_01.eex"), overwrite: false);
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=Battlefield deployment write smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        RunBattlefieldDeploymentWriteSmoke(project, testProject, tables, battlefieldScenarioFileName);
        Console.WriteLine($"BATTLEFIELD_DEPLOYMENT_WRITE_ONLY_SMOKE_OK root={smokeRoot}");
    }

    static void RunRsWriteSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        RunRsSmoke(project, tables);
    
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "RsWriteSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5", "Hexzmap.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("R/S 写入烟测缺少核心文件。", source);
            }
    
            File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
        }
    
        var rsRoot = Path.Combine(smokeRoot, "RS");
        Directory.CreateDirectory(rsRoot);
        var sourceScenarioPath = Path.Combine(project.GameRoot, "RS", "R_00.eex");
        if (!File.Exists(sourceScenarioPath))
        {
            sourceScenarioPath = Directory.GetFiles(Path.Combine(project.GameRoot, "RS"), "R_*.eex", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault()
                ?? throw new FileNotFoundException("R/S 写入烟测找不到 R_*.eex。", Path.Combine(project.GameRoot, "RS", "R_*.eex"));
        }
    
        var scenarioFileName = Path.GetFileName(sourceScenarioPath);
        var testScenarioPath = Path.Combine(rsRoot, scenarioFileName);
        File.Copy(sourceScenarioPath, testScenarioPath, overwrite: false);
        var sourceBattlefieldScenarioPath = Path.Combine(project.GameRoot, "RS", "S_00.eex");
        if (!File.Exists(sourceBattlefieldScenarioPath))
        {
            sourceBattlefieldScenarioPath = Directory.GetFiles(Path.Combine(project.GameRoot, "RS"), "S_*.eex", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault()
                ?? throw new FileNotFoundException("R/S write smoke could not find S_*.eex.", Path.Combine(project.GameRoot, "RS", "S_*.eex"));
        }
    
        var battlefieldScenarioFileName = Path.GetFileName(sourceBattlefieldScenarioPath);
        var testBattlefieldScenarioPath = Path.Combine(rsRoot, battlefieldScenarioFileName);
        File.Copy(sourceBattlefieldScenarioPath, testBattlefieldScenarioPath, overwrite: false);
    
        var sourceMapRoot = Path.Combine(project.GameRoot, "Map");
        var smokeMapRoot = Path.Combine(smokeRoot, "Map");
        Directory.CreateDirectory(smokeMapRoot);
        var sourceMapPath = FindFirstJpegMap(sourceMapRoot)
            ?? throw new FileNotFoundException("地图底图写入烟测找不到 Map\\*.jpg。", sourceMapRoot);
        File.Copy(sourceMapPath, Path.Combine(smokeMapRoot, Path.GetFileName(sourceMapPath)), overwrite: false);
    
        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=R/S eex write smoke\r\n");
    
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var scenarioIndex = new ScenarioFileReader().ReadAllIndex(testProject);
        if (scenarioIndex.Count != 2 ||
            !scenarioIndex.Any(x => x.FileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase)) ||
            !scenarioIndex.Any(x => x.FileName.Equals(battlefieldScenarioFileName, StringComparison.OrdinalIgnoreCase)) ||
            scenarioIndex.Any(x => !ScenarioFileReader.IsRsScriptFile(x.FileName)))
        {
            throw new InvalidOperationException("R/S 写入烟测索引未限定在测试副本 RS eex 文件。");
        }
    
        var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(sceneStringPath))
        {
            throw new FileNotFoundException("R/S legacy full-structure write smoke requires CczString.ini.", sceneStringPath);
        }
    
        var sceneDoc = new SceneStringParser().Parse(sceneStringPath);
        var legacyDocument = new LegacyScenarioReader().Read(testScenarioPath, sceneDoc);
        var legacySave = new LegacyScenarioWriter().Save(
            testProject,
            Path.Combine("RS", scenarioFileName),
            legacyDocument,
            sceneDoc,
            "R/S eex legacy full-structure write smoke");
        var legacyVerify = new LegacyScenarioReader().Read(testScenarioPath, sceneDoc);
        if (legacyVerify.SceneCount != legacyDocument.SceneCount ||
            legacyVerify.SectionCount != legacyDocument.SectionCount ||
            legacyVerify.CommandCount != legacyDocument.CommandCount ||
            string.IsNullOrWhiteSpace(legacySave.BackupPath) ||
            !File.Exists(legacySave.BackupPath) ||
            string.IsNullOrWhiteSpace(legacySave.ReportJsonPath) ||
            !File.Exists(legacySave.ReportJsonPath))
        {
            throw new InvalidOperationException("R/S eex legacy full-structure write reread, backup, or report validation failed.");
        }
        Console.WriteLine($"LEGACY_SCENARIO_WRITE_OK file={scenarioFileName} scenes={legacyVerify.SceneCount} sections={legacyVerify.SectionCount} commands={legacyVerify.CommandCount} changedBytes={legacySave.ChangedBytes} backup={Path.GetFileName(legacySave.BackupPath)}");
    
        RunRScenePositionWriteSmoke(testProject, sceneDoc, scenarioFileName);
    
        var textReader = new ScenarioTextReader();
        var textRows = textReader.Read(testScenarioPath, maxItems: 80).ToList();
        var writableText = textRows.FirstOrDefault(x => x.ByteLength >= EncodingService.GetGbkByteCount("烟测") &&
                                                        !string.Equals(BattlefieldEditorService.NormalizeText(x.Text), "烟测", StringComparison.Ordinal))
                           ?? textRows.FirstOrDefault(x => x.ByteLength >= EncodingService.GetGbkByteCount("写测"))
                           ?? throw new InvalidOperationException($"{scenarioFileName} 没有可用于 R/S eex 原地短写回烟测的文本线索。");
        var originalText = writableText.Text;
        var replacementText = string.Equals(BattlefieldEditorService.NormalizeText(originalText), "烟测", StringComparison.Ordinal)
            ? "写测"
            : "烟测";
        if (EncodingService.GetGbkByteCount(replacementText) > writableText.ByteLength)
        {
            throw new InvalidOperationException($"R/S eex 文本容量不足：{scenarioFileName} {writableText.OffsetHex} capacity={writableText.ByteLength}");
        }
    
        writableText.Text = replacementText;
        var textSave = new ScenarioTextWriter().SaveInPlace(
            testProject,
            Path.Combine("RS", scenarioFileName),
            new[] { writableText },
            "R/S eex 写入烟测前自动备份");
        var textVerify = textReader.Read(testScenarioPath, maxItems: 80).FirstOrDefault(x => x.Offset == writableText.Offset);
        if (textVerify == null ||
            !string.Equals(BattlefieldEditorService.NormalizeText(textVerify.Text), replacementText, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(textSave.BackupPath) ||
            !File.Exists(textSave.BackupPath) ||
            string.IsNullOrWhiteSpace(textSave.ReportJsonPath) ||
            !File.Exists(textSave.ReportJsonPath) ||
            !File.ReadAllText(textSave.ReportJsonPath).Contains("\"OperationKind\": \"R/S eex 剧本文本写回\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("R/S eex 文本短写回复读、备份或结构化报告验证失败。");
        }
    
        var imageAssignmentService = new ImageAssignmentService();
        var imageData = imageAssignmentService.Load(testProject, tables);
        if (imageData.Columns["头像编号"]!.ReadOnly)
        {
            throw new InvalidOperationException("人物形象设定写回烟测要求头像编号列可编辑。");
        }

        var originalFace = Convert.ToInt32(imageData.Rows[0]["头像编号"], CultureInfo.InvariantCulture);
        var changedFace = originalFace == 1 ? 2 : 1;
        var originalR = Convert.ToInt32(imageData.Rows[0]["R形象编号"], CultureInfo.InvariantCulture);
        var changedR = originalR == 0 ? 1 : 0;
        imageData.Rows[0]["头像编号"] = changedFace;
        imageData.Rows[0]["R形象编号"] = changedR;
        var imageSave = imageAssignmentService.SaveToTestCopy(testProject, tables, imageData);
        var imageVerify = imageAssignmentService.Load(testProject, tables);
        var actualFace = Convert.ToInt32(imageVerify.Rows[0]["头像编号"], CultureInfo.InvariantCulture);
        var actualR = Convert.ToInt32(imageVerify.Rows[0]["R形象编号"], CultureInfo.InvariantCulture);
        if (actualFace != changedFace ||
            actualR != changedR ||
            imageSave.Saves.Count == 0 ||
            imageSave.Saves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
        {
            throw new InvalidOperationException($"人物形象设定写回复读失败：face expected={changedFace}, actual={actualFace}; R expected={changedR}, actual={actualR}");
        }
    
        Console.WriteLine($"RS_WRITE_TEXT_OK file={scenarioFileName} offset={writableText.OffsetHex} '{originalText}'->'{textVerify.Text}' changedBytes={textSave.ChangedBytes} backup={Path.GetFileName(textSave.BackupPath)}");
        Console.WriteLine($"RS_WRITE_IMAGE_ASSIGN_OK row=0 Face={originalFace}->{actualFace} R={originalR}->{actualR} saves={imageSave.Saves.Count} backups={imageSave.BackupSummary}");
    
        RunRoleWriteSmoke(testProject, tables);
        RunItemWriteSmoke(testProject, tables);
        RunItemEffectCatalogSmoke(testProject, smokeRoot);
        RunJobWriteSmoke(testProject, tables);
        RunBattlefieldTextWriteSmokeLayered(project, testProject, tables, battlefieldScenarioFileName);
        RunBattlefieldDeploymentWriteSmoke(project, testProject, tables, battlefieldScenarioFileName);
        RunMapImageWriteSmoke(testProject);
        RunHexzmapWriteSmoke(project, testProject);
        RunMapWorkbenchSmoke(project, testProject);
        Console.WriteLine($"RS_WRITE_SMOKE OK root={smokeRoot}");
    }

    static void RunJobWriteOnlySmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "JobWriteSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("详细兵种写入烟测缺少核心文件。", source);
            }

            File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=Job write smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        RunJobWriteSmoke(testProject, tables);
        Console.WriteLine($"JOB_WRITE_ONLY_SMOKE_OK root={smokeRoot}");
    }

    static void RunBattlefieldUnitStatusWriteSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        _ = tables;
        RunBattlefieldUnitConsoleDeltaSmoke();

        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "BattlefieldUnitStatusWriteSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5", "Hexzmap.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
            }
        }

        var rsRoot = Path.Combine(smokeRoot, "RS");
        Directory.CreateDirectory(rsRoot);
        var sourceBattlefieldScenarioPath = Path.Combine(project.GameRoot, "RS", "S_00.eex");
        if (!File.Exists(sourceBattlefieldScenarioPath))
        {
            sourceBattlefieldScenarioPath = Directory.GetFiles(Path.Combine(project.GameRoot, "RS"), "S_*.eex", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault()
                ?? throw new FileNotFoundException("Battlefield unit status write smoke could not find S_*.eex.", Path.Combine(project.GameRoot, "RS", "S_*.eex"));
        }

        var battlefieldScenarioFileName = Path.GetFileName(sourceBattlefieldScenarioPath);
        File.Copy(sourceBattlefieldScenarioPath, Path.Combine(rsRoot, battlefieldScenarioFileName), overwrite: false);
        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=Battlefield unit status write smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(dictionaryPath))
        {
            throw new FileNotFoundException("Battlefield unit status write smoke requires CczString.ini.", dictionaryPath);
        }

        var dictionary = new SceneStringParser().Parse(dictionaryPath);
        var scenario = new ScenarioFileReader()
            .ReadAllIndex(testProject)
            .FirstOrDefault(x => x.FileName.Equals(battlefieldScenarioFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Battlefield unit status write smoke could not find copied {battlefieldScenarioFileName}.");
        var legacyDocument = new LegacyScenarioReader().Read(scenario.Path, dictionary);
        var target = FindSmokeWritableStatusTarget(legacyDocument)
            ?? throw new InvalidOperationException($"Battlefield unit status write smoke found no writable 46/47 record in {battlefieldScenarioFileName}.");
        var targetKey = BuildSmokeUnitTargetKey(target.Command, target.RecordIndex);

        RunBattlefieldUnitStatusWriteSmoke(testProject, scenario, dictionary, targetKey, target.PersonId);
    }

    static void RunBattlefieldUnitConsoleDeltaSmoke()
    {
        var service = new BattlefieldUnitStatusWriteService();
        var boundary = new ItemCategoryBoundary(0, 70, 109, "console delta smoke", IsFallback: false);
        var defaults = new BattlefieldUnitDataDefaults
        {
            Found = true,
            PersonId = 32,
            PersonName = "SmokeUnit",
            JobId = 5,
            WeaponId = 3,
            WeaponLevel = 2,
            ArmorId = 72,
            ArmorLevel = 3,
            AssistId = 111,
            Abilities = new Dictionary<int, int>
            {
                [10] = 80,
                [11] = 81,
                [12] = 82,
                [13] = 83,
                [14] = 84
            }
        };

        var baseDraft = CreateBattlefieldConsoleDeltaDraft(defaults);
        var noChange = service.BuildDeltaDraftFromEffectiveValues(
            baseDraft,
            defaults,
            boundary,
            defaults.WeaponId,
            defaults.WeaponLevel,
            defaults.ArmorId,
            defaults.ArmorLevel,
            defaults.AssistId,
            defaults.JobId,
            defaults.Abilities.ToDictionary(pair => pair.Key, pair => (Operation: 0, Value: (int?)pair.Value)));
        AssertBattlefieldConsoleDelta(noChange, expectDelta: false, "Data defaults should not create script overrides.");

        var diff = service.BuildDeltaDraftFromEffectiveValues(
            baseDraft,
            defaults,
            boundary,
            weaponId: 4,
            weaponLevel: 5,
            armorId: defaults.ArmorId,
            armorLevel: defaults.ArmorLevel,
            assistId: defaults.AssistId,
            jobId: 6,
            abilities: defaults.Abilities.ToDictionary(pair => pair.Key, pair =>
                pair.Key == 10
                    ? (Operation: 0, Value: (int?)90)
                    : (Operation: 0, Value: (int?)pair.Value)));
        AssertBattlefieldConsoleDelta(diff, expectDelta: true, "Changed values should create script overrides.");
        if (diff.RemoveEquipmentOverride ||
            diff.RemoveJobOverride ||
            diff.RemoveAbilityOverrides.Count != 0 ||
            diff.Weapon != BattlefieldUnitDataDefaultService.ToScriptEquipmentCode(4, boundary, BattlefieldEquipmentSlot.Weapon) ||
            diff.WeaponLevel != 5 ||
            diff.Armor != 0 ||
            diff.ArmorLevel != 0 ||
            diff.Assist != 0 ||
            diff.JobId != 6 ||
            diff.Abilities.First(x => x.AbilityId == 10).Operation != 0 ||
            diff.Abilities.First(x => x.AbilityId == 10).Value != 90 ||
            diff.Abilities.Where(x => x.AbilityId != 10).Any(x => x.Value.HasValue))
        {
            throw new InvalidOperationException("Battlefield console delta smoke failed to create the expected equipment/job/ability overrides.");
        }

        var overridden = CreateBattlefieldConsoleDeltaDraft(defaults);
        overridden.HasEquipmentCommand = true;
        overridden.Weapon = BattlefieldUnitDataDefaultService.ToScriptEquipmentCode(4, boundary, BattlefieldEquipmentSlot.Weapon);
        overridden.WeaponLevel = 5;
        overridden.Armor = 0;
        overridden.ArmorLevel = 0;
        overridden.Assist = 0;
        overridden.HasJobCommand = true;
        overridden.JobId = 6;
        var ability10 = overridden.Abilities.First(x => x.AbilityId == 10);
        ability10.HasCommand = true;
        ability10.Operation = 0;
        ability10.Value = 90;

        var reverted = service.BuildDeltaDraftFromEffectiveValues(
            overridden,
            defaults,
            boundary,
            defaults.WeaponId,
            defaults.WeaponLevel,
            defaults.ArmorId,
            defaults.ArmorLevel,
            defaults.AssistId,
            defaults.JobId,
            defaults.Abilities.ToDictionary(pair => pair.Key, pair => (Operation: 0, Value: (int?)pair.Value)));
        AssertBattlefieldConsoleDelta(reverted, expectDelta: true, "Reverting overrides to Data defaults should remove script overrides.");
        if (!reverted.RemoveEquipmentOverride ||
            !reverted.RemoveJobOverride ||
            !reverted.RemoveAbilityOverrides.Contains(10) ||
            reverted.RemoveAbilityOverrides.Count != 1 ||
            reverted.Weapon.HasValue ||
            reverted.WeaponLevel.HasValue ||
            reverted.Armor.HasValue ||
            reverted.ArmorLevel.HasValue ||
            reverted.Assist.HasValue ||
            reverted.JobId.HasValue ||
            reverted.Abilities.Any(x => x.Value.HasValue))
        {
            throw new InvalidOperationException("Battlefield console delta smoke failed to request removal when values returned to Data defaults.");
        }

        var plusMode = service.BuildDeltaDraftFromEffectiveValues(
            baseDraft,
            defaults,
            boundary,
            defaults.WeaponId,
            defaults.WeaponLevel,
            defaults.ArmorId,
            defaults.ArmorLevel,
            defaults.AssistId,
            defaults.JobId,
            defaults.Abilities.ToDictionary(pair => pair.Key, pair =>
                pair.Key == 11
                    ? (Operation: 1, Value: (int?)3)
                    : (Operation: 0, Value: (int?)pair.Value)));
        var plusAbility = plusMode.Abilities.First(x => x.AbilityId == 11);
        if (plusAbility.Operation != 1 || plusAbility.Value != 3)
        {
            throw new InvalidOperationException("Battlefield console delta smoke failed to preserve +/- ability operation mode.");
        }

        var unsetDefaults = new BattlefieldUnitDataDefaults
        {
            Found = true,
            PersonId = 33,
            PersonName = "SmokeUnsetEquipment",
            JobId = 5,
            WeaponId = BattlefieldUnitDataDefaultService.DataEquipmentUnset,
            WeaponLevel = 0,
            ArmorId = BattlefieldUnitDataDefaultService.DataEquipmentUnset,
            ArmorLevel = 0,
            AssistId = BattlefieldUnitDataDefaultService.DataEquipmentUnset,
            Abilities = defaults.Abilities
        };
        var unsetDraft = CreateBattlefieldConsoleDeltaDraft(unsetDefaults);
        var unsetNoChange = service.BuildDeltaDraftFromEffectiveValues(
            unsetDraft,
            unsetDefaults,
            boundary,
            weaponId: null,
            weaponLevel: 0,
            armorId: null,
            armorLevel: 0,
            assistId: null,
            jobId: unsetDefaults.JobId,
            abilities: unsetDefaults.Abilities.ToDictionary(pair => pair.Key, pair => (Operation: 0, Value: (int?)pair.Value)));
        AssertBattlefieldConsoleDelta(unsetNoChange, expectDelta: false, "Data=255 default equipment should not create 0x48 overrides.");
        if (BattlefieldUnitDataDefaultService.ToScriptEquipmentCode(BattlefieldUnitDataDefaultService.DataEquipmentUnset, boundary, BattlefieldEquipmentSlot.Weapon) != 0 ||
            BattlefieldUnitDataDefaultService.FromScriptEquipmentCode(0, boundary, BattlefieldEquipmentSlot.Weapon, BattlefieldUnitDataDefaultService.DataEquipmentUnset).HasValue ||
            unsetDefaults.FormatItem(BattlefieldUnitDataDefaultService.DataEquipmentUnset) != "使用默认配装（Data=255）")
        {
            throw new InvalidOperationException("Battlefield console delta smoke failed Data=255 default equipment handling.");
        }

        var scene2Placement = new BattlefieldPlacedUnit
        {
            TargetKey = "Scene=2;Section=0;Command=0;Offset=000000;Id=0x46;Record=0",
            PersonId = defaults.PersonId,
            GridX = 1,
            GridY = 1
        };
        if (BattlefieldUnitStatusWriteService.IsWritableStatusTarget(scene2Placement) ||
            !BattlefieldUnitStatusWriteService.IsScene2PlusStatusTarget(scene2Placement))
        {
            throw new InvalidOperationException("Battlefield console delta smoke failed Scene2+ status write guard.");
        }

        Console.WriteLine("BATTLEFIELD_UNIT_CONSOLE_DELTA_SMOKE_OK");
    }

    static BattlefieldUnitStatusDraft CreateBattlefieldConsoleDeltaDraft(BattlefieldUnitDataDefaults defaults)
    {
        var draft = new BattlefieldUnitStatusDraft
        {
            TargetKey = "Scene=1;Section=0;Command=0;Offset=000000;Id=0x46;Record=0",
            ScenarioFileName = "S_00.eex",
            PersonId = defaults.PersonId,
            PersonName = defaults.PersonName,
            CommandId = 0x46,
            RecordIndex = 0,
            LevelBonus = 0,
            JobLevel = 0,
            AiPolicy = 0,
            DataDefaults = defaults
        };
        foreach (var ability in draft.Abilities)
        {
            ability.DataDefaultValue = defaults.GetAbility(ability.AbilityId);
        }

        return draft;
    }

    static void AssertBattlefieldConsoleDelta(BattlefieldUnitStatusDraft draft, bool expectDelta, string message)
    {
        var hasDelta =
            draft.RemoveEquipmentOverride ||
            draft.RemoveJobOverride ||
            draft.RemoveAbilityOverrides.Count > 0 ||
            draft.Weapon.HasValue ||
            draft.WeaponLevel.HasValue ||
            draft.Armor.HasValue ||
            draft.ArmorLevel.HasValue ||
            draft.Assist.HasValue ||
            draft.JobId.HasValue ||
            draft.Abilities.Any(ability => ability.Value.HasValue);
        if (hasDelta != expectDelta)
        {
            throw new InvalidOperationException(message);
        }
    }
    
    static void RunItemEffectCatalogSmoke(CczProject testProject, string smokeRoot)
    {
        var isolatedProject = new CczProject
        {
            WorkspaceRoot = smokeRoot,
            GameRoot = testProject.GameRoot,
            HexTableXmlPath = testProject.HexTableXmlPath,
            SceneDictionaryPath = testProject.SceneDictionaryPath,
            SceneEditorDirectory = testProject.SceneEditorDirectory,
            ImageAssignerDirectory = testProject.ImageAssignerDirectory,
            ImageAssignerSystemIniPath = testProject.ImageAssignerSystemIniPath,
            MaterialLibraryRoot = testProject.MaterialLibraryRoot,
            PatchConfigRoot = testProject.PatchConfigRoot,
            PathDiagnostics = testProject.PathDiagnostics
        };
    
        var service = new ItemEffectCatalogService();
        var entries = new[]
        {
            new ItemEffectCatalogEntry { EffectId = 42, Name = "神火护体", Description = "变长中文说明：用于烟测 UTF-8 保存与读取。" },
            new ItemEffectCatalogEntry { EffectId = 42, Name = "烈焰护盾·改", Description = "允许同一特效号重复，并保留第二条自定义说明。" },
            new ItemEffectCatalogEntry { EffectId = 99, Name = "超长特效名称-烟测-天地无双护身真诀", Description = "验证特效名不是固定字段长，而是项目侧 UTF-8 变长文本。" }
        };
        var storePath = service.Save(isolatedProject, entries);
        var loaded = service.Load(isolatedProject);
        var lookup = service.BuildDisplayLookup(loaded);
        if (!File.Exists(storePath) ||
            loaded.Count < 3 ||
            !lookup.TryGetValue(42, out var duplicateName) ||
            !duplicateName.Contains("神火护体", StringComparison.Ordinal) ||
            !duplicateName.Contains("烈焰护盾·改", StringComparison.Ordinal) ||
            !lookup.TryGetValue(99, out var longName) ||
            !longName.Contains("天地无双护身真诀", StringComparison.Ordinal) ||
            !File.ReadAllText(storePath, Encoding.UTF8).Contains("烈焰护盾·改", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("宝物特效目录烟测失败：重复特效号、变长中文或 UTF-8 回读不符合预期。");
        }
    
        Console.WriteLine($"ITEM_EFFECT_CATALOG_OK file={Path.GetFileName(storePath)} dup42={duplicateName} long99={longName}");
    }
    
    static void RunRScenePositionWriteSmoke(CczProject testProject, SceneStringDocument sceneDoc, string scenarioFileName)
    {
        var relativePath = Path.Combine("RS", scenarioFileName);
        var fullPath = Path.Combine(testProject.GameRoot, relativePath);
        var document = new LegacyScenarioReader().Read(fullPath, sceneDoc);
        var command = document.EnumerateCommands().FirstOrDefault(command =>
            command.CommandId == 0x30 &&
            command.Parameters.Count > 2 &&
            command.Parameters[1].Kind == LegacyScenarioParameterKind.Dword32 &&
            command.Parameters[2].Kind == LegacyScenarioParameterKind.Dword32)
            ?? throw new InvalidOperationException($"{scenarioFileName} 没有可用于 R 场景坐标写回烟测的 30 武将出现命令。");
    
        var originalX = command.Parameters[1].IntValue;
        var originalY = command.Parameters[2].IntValue;
        var targetX = originalX <= 0 ? 1 : originalX - 1;
        var targetY = originalY <= 0 ? 1 : originalY - 1;
        if (targetX == originalX && targetY == originalY)
        {
            targetX = originalX + 1;
        }
    
        command.Parameters[1].IntValue = targetX;
        command.Parameters[2].IntValue = targetY;
        var save = new LegacyScenarioWriter().Save(
            testProject,
            relativePath,
            document,
            sceneDoc,
            "R场景制作 30 武将出现坐标写回烟测");
    
        var verify = new LegacyScenarioReader().Read(fullPath, sceneDoc);
        var verifiedCommand = verify.EnumerateCommands().FirstOrDefault(candidate =>
            candidate.SceneIndex == command.SceneIndex &&
            candidate.SectionIndex == command.SectionIndex &&
            candidate.CommandIndex == command.CommandIndex &&
            candidate.CommandId == command.CommandId)
            ?? throw new InvalidOperationException("R 场景坐标写回复读失败：找不到原 30 命令。");
    
        var actualX = verifiedCommand.Parameters.Count > 2 ? verifiedCommand.Parameters[1].IntValue : int.MinValue;
        var actualY = verifiedCommand.Parameters.Count > 2 ? verifiedCommand.Parameters[2].IntValue : int.MinValue;
        if (actualX != targetX ||
            actualY != targetY ||
            string.IsNullOrWhiteSpace(save.BackupPath) ||
            !File.Exists(save.BackupPath) ||
            string.IsNullOrWhiteSpace(save.ReportJsonPath) ||
            !File.Exists(save.ReportJsonPath))
        {
            throw new InvalidOperationException($"R 场景坐标写回复读、备份或报告失败：expected=({targetX},{targetY}), actual=({actualX},{actualY})。");
        }
    
        Console.WriteLine($"RSCENE_POSITION_WRITE_SMOKE_OK file={scenarioFileName} command=Scene={command.SceneIndex};Section={command.SectionIndex};Command={command.CommandIndex};Id={command.CommandIdHex} coord=({originalX},{originalY})->({actualX},{actualY}) backup={Path.GetFileName(save.BackupPath)} changedBytes={save.ChangedBytes}");
    }
    
    static void RunRoleWriteSmoke(CczProject testProject, IReadOnlyList<HexTableDefinition> tables)
    {
        var reader = new HexTableReader();
        var writer = new HexTableWriter();
        var personTable = tables.Single(t => t.TableName == "6.5-0 人物");
        var biographyTable = tables.Single(t => t.TableName == "6.5-0-1 人物列传");
        var personRead = reader.Read(testProject, personTable, tables);
        var biographyRead = reader.Read(testProject, biographyTable, tables);
        if (!personRead.Validation.IsUsable || !biographyRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("角色写入烟测读取人物表或人物列传失败。");
        }
    
        var roleId = 0;
        var personRow = FindSmokeRowById(personRead.Data, roleId);
        var biographyRow = FindSmokeRowById(biographyRead.Data, roleId);
        var originalName = Convert.ToString(personRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var changedName = originalName == "烟测曹操" ? "写测曹操" : "烟测曹操";
        var originalFace = Convert.ToInt32(personRow["头像"], CultureInfo.InvariantCulture);
        var changedFace = originalFace == 0 ? 1 : 0;
        var originalJob = Convert.ToInt32(personRow["职业"], CultureInfo.InvariantCulture);
        var changedJob = originalJob == 0 ? 1 : 0;
        var originalLevel = Convert.ToInt32(personRow["级别"], CultureInfo.InvariantCulture);
        var changedLevel = originalLevel == 1 ? 2 : 1;
        var originalAbility = Convert.ToInt32(personRow["武力"], CultureInfo.InvariantCulture);
        var changedAbility = originalAbility == 99 ? 98 : 99;
    
        personRow["名称"] = changedName;
        personRow["头像"] = changedFace;
        personRow["职业"] = changedJob;
        personRow["级别"] = changedLevel;
        personRow["武力"] = changedAbility;
        biographyRow["介绍"] = "CCZ人物列传烟测";
    
        var saves = new[]
        {
            writer.Save(testProject, personTable, personRead.Data),
            writer.Save(testProject, biographyTable, biographyRead.Data)
        };
    
        var personVerify = reader.Read(testProject, personTable, tables);
        var biographyVerify = reader.Read(testProject, biographyTable, tables);
        var verifyPerson = FindSmokeRowById(personVerify.Data, roleId);
        var actualName = Convert.ToString(verifyPerson["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var actualFace = Convert.ToInt32(verifyPerson["头像"], CultureInfo.InvariantCulture);
        var actualJob = Convert.ToInt32(verifyPerson["职业"], CultureInfo.InvariantCulture);
        var actualLevel = Convert.ToInt32(verifyPerson["级别"], CultureInfo.InvariantCulture);
        var actualAbility = Convert.ToInt32(verifyPerson["武力"], CultureInfo.InvariantCulture);
        var actualBiography = Convert.ToString(FindSmokeRowById(biographyVerify.Data, roleId)["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
        if (actualName != changedName ||
            actualFace != changedFace ||
            actualJob != changedJob ||
            actualLevel != changedLevel ||
            actualAbility != changedAbility ||
            !actualBiography.Contains("CCZ人物列传烟测", StringComparison.Ordinal) ||
            saves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
        {
            throw new InvalidOperationException($"角色写入烟测复读失败：name={actualName}, face={actualFace}, job={actualJob}, level={actualLevel}, ability={actualAbility}");
        }
    
        Console.WriteLine($"ROLE_WRITE_SMOKE_OK id={roleId}:{originalName}->{actualName} 头像={originalFace}->{actualFace} 职业={originalJob}->{actualJob} 级别={originalLevel}->{actualLevel} 武力={originalAbility}->{actualAbility} saves={saves.Length}");
    }
    
    static void RunItemWriteSmoke(CczProject testProject, IReadOnlyList<HexTableDefinition> tables)
    {
        var reader = new HexTableReader();
        var writer = new HexTableWriter();
        var itemLowTable = tables.Single(t => t.TableName == "6.5-1 物品（0-103）");
        var itemHighTable = tables.Single(t => t.TableName == "6.5-2 物品（104-255）");
        var descLowTable = tables.Single(t => t.TableName == "6.5-1-1 物品说明（0-103）");
        var descHighTable = tables.Single(t => t.TableName == "6.5-2-1 物品说明（104-255）");
    
        var itemLow = reader.Read(testProject, itemLowTable, tables);
        var itemHigh = reader.Read(testProject, itemHighTable, tables);
        var descLow = reader.Read(testProject, descLowTable, tables);
        var descHigh = reader.Read(testProject, descHighTable, tables);
        if (!itemLow.Validation.IsUsable || !itemHigh.Validation.IsUsable || !descLow.Validation.IsUsable || !descHigh.Validation.IsUsable)
        {
            throw new InvalidOperationException("宝物写入烟测读取物品表/说明表失败。");
        }
    
        var lowItemId = 0;
        var lowItemRow = FindSmokeRowById(itemLow.Data, lowItemId);
        var lowDescRow = FindSmokeRowById(descLow.Data, lowItemId);
        var originalLowName = Convert.ToString(lowItemRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var originalLowPrice = Convert.ToInt32(lowItemRow["价格（/100）"], CultureInfo.InvariantCulture);
        var newLowName = originalLowName == "烟测宝物" ? "写测宝物" : "烟测宝物";
        var newLowPrice = originalLowPrice == 1 ? 2 : 1;
        lowItemRow["名称"] = newLowName;
        lowItemRow["价格（/100）"] = newLowPrice;
        lowDescRow["介绍"] = "烟测说明";
    
        var highItemId = Convert.ToInt32(itemHigh.Data.Rows[0]["ID"], CultureInfo.InvariantCulture);
        var highItemRow = FindSmokeRowById(itemHigh.Data, highItemId);
        var highDescRow = FindSmokeRowById(descHigh.Data, highItemId);
        var originalHighName = Convert.ToString(highItemRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var newHighName = originalHighName == "烟测扩展" ? "写测扩展" : "烟测扩展";
        highItemRow["名称"] = newHighName;
        highDescRow["介绍"] = "扩展烟测说明";
    
        var saves = new[]
        {
            writer.Save(testProject, itemLowTable, itemLow.Data),
            writer.Save(testProject, descLowTable, descLow.Data),
            writer.Save(testProject, itemHighTable, itemHigh.Data),
            writer.Save(testProject, descHighTable, descHigh.Data)
        };
    
        var itemLowVerify = reader.Read(testProject, itemLowTable, tables);
        var itemHighVerify = reader.Read(testProject, itemHighTable, tables);
        var descLowVerify = reader.Read(testProject, descLowTable, tables);
        var descHighVerify = reader.Read(testProject, descHighTable, tables);
        if ((Convert.ToString(FindSmokeRowById(itemLowVerify.Data, lowItemId)["名称"], CultureInfo.InvariantCulture) ?? string.Empty) != newLowName ||
            Convert.ToInt32(FindSmokeRowById(itemLowVerify.Data, lowItemId)["价格（/100）"], CultureInfo.InvariantCulture) != newLowPrice ||
            !(Convert.ToString(FindSmokeRowById(descLowVerify.Data, lowItemId)["介绍"], CultureInfo.InvariantCulture) ?? string.Empty).Contains("烟测说明", StringComparison.Ordinal) ||
            (Convert.ToString(FindSmokeRowById(itemHighVerify.Data, highItemId)["名称"], CultureInfo.InvariantCulture) ?? string.Empty) != newHighName ||
            !(Convert.ToString(FindSmokeRowById(descHighVerify.Data, highItemId)["介绍"], CultureInfo.InvariantCulture) ?? string.Empty).Contains("扩展烟测说明", StringComparison.Ordinal) ||
            saves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
        {
            throw new InvalidOperationException("宝物/物品写入烟测复读、备份或说明写回验证失败。");
        }
    
        Console.WriteLine($"ITEM_WRITE_SMOKE_OK low={lowItemId}:{originalLowName}->{newLowName} price={originalLowPrice}->{newLowPrice} high={highItemId}:{originalHighName}->{newHighName} saves={saves.Length}");
    }
    
    static void RunJobWriteSmoke(CczProject testProject, IReadOnlyList<HexTableDefinition> tables)
    {
        var reader = new HexTableReader();
        var writer = new HexTableWriter();
    
        var detailedJobTable = tables.Single(t => t.TableName == "6.5-4 详细兵种");
        var jobDescriptionTable = tables.Single(t => t.TableName == "6.5-4-1 兵种说明");
        var jobGrowthTable = tables.Single(t => t.TableName == "6.5-4-2 兵种成长");
        var jobPierceTable = tables.Single(t => t.TableName == "6.5-4-3 兵种穿透");
    
        var detailedJobRead = reader.Read(testProject, detailedJobTable, tables);
        var jobDescriptionRead = reader.Read(testProject, jobDescriptionTable, tables);
        var jobGrowthRead = reader.Read(testProject, jobGrowthTable, tables);
        var jobPierceRead = reader.Read(testProject, jobPierceTable, tables);
        if (!detailedJobRead.Validation.IsUsable || !jobDescriptionRead.Validation.IsUsable ||
            !jobGrowthRead.Validation.IsUsable || !jobPierceRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("兵种写入烟测读取详细兵种/说明/成长/穿透失败。");
        }
    
        var jobId = 0;
        var detailedJobRow = FindSmokeRowById(detailedJobRead.Data, jobId);
        var jobDescriptionRow = FindSmokeRowById(jobDescriptionRead.Data, jobId);
        var jobGrowthRow = FindSmokeRowById(jobGrowthRead.Data, jobId);
        var jobPierceRow = FindSmokeRowById(jobPierceRead.Data, jobId);
        var originalJobName = Convert.ToString(detailedJobRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var changedJobName = originalJobName == "烟测兵种" ? "写测兵种" : "烟测兵种";
        var growthField = jobGrowthRead.Data.Columns.Contains("移动力")
            ? "移动力"
            : jobGrowthRead.Data.Columns.Cast<DataColumn>().First(c => c.ColumnName is not "ID" and not "名称").ColumnName;
        var originalGrowth = Convert.ToInt32(jobGrowthRow[growthField], CultureInfo.InvariantCulture);
        var changedGrowth = originalGrowth == 1 ? 2 : 1;
        var originalPierce = Convert.ToInt32(jobPierceRow["穿透"], CultureInfo.InvariantCulture);
        var changedPierce = originalPierce == 0 ? 1 : 0;
    
        detailedJobRow["名称"] = changedJobName;
        jobDescriptionRow["介绍"] = "CCZ兵种烟测";
        jobGrowthRow[growthField] = changedGrowth;
        jobPierceRow["穿透"] = changedPierce;
    
        var detailedSaves = new[]
        {
            writer.Save(testProject, detailedJobTable, detailedJobRead.Data),
            writer.Save(testProject, jobDescriptionTable, jobDescriptionRead.Data),
            writer.Save(testProject, jobGrowthTable, jobGrowthRead.Data),
            writer.Save(testProject, jobPierceTable, jobPierceRead.Data)
        };
    
        var detailedJobVerify = reader.Read(testProject, detailedJobTable, tables);
        var jobDescriptionVerify = reader.Read(testProject, jobDescriptionTable, tables);
        var jobGrowthVerify = reader.Read(testProject, jobGrowthTable, tables);
        var jobPierceVerify = reader.Read(testProject, jobPierceTable, tables);
        var actualJobName = Convert.ToString(FindSmokeRowById(detailedJobVerify.Data, jobId)["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var actualJobDescription = Convert.ToString(FindSmokeRowById(jobDescriptionVerify.Data, jobId)["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
        var actualGrowth = Convert.ToInt32(FindSmokeRowById(jobGrowthVerify.Data, jobId)[growthField], CultureInfo.InvariantCulture);
        var actualPierce = Convert.ToInt32(FindSmokeRowById(jobPierceVerify.Data, jobId)["穿透"], CultureInfo.InvariantCulture);
        if (actualJobName != changedJobName ||
            !actualJobDescription.Contains("CCZ兵种烟测", StringComparison.Ordinal) ||
            actualGrowth != changedGrowth ||
            actualPierce != changedPierce ||
            detailedSaves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
        {
            throw new InvalidOperationException($"详细兵种写入烟测复读失败：name={actualJobName}, {growthField}={actualGrowth}, pierce={actualPierce}");
        }
    
        Console.WriteLine($"JOB_WRITE_SMOKE_OK id={jobId}:{originalJobName}->{actualJobName} {growthField}={originalGrowth}->{actualGrowth} 穿透={originalPierce}->{actualPierce} saves={detailedSaves.Length}");
    
        var jobSeriesTable = tables.Single(t => t.TableName == "6.5-3 兵种系");
        var jobTerrainPowerTable = tables.Single(t => t.TableName == "6.5-3-1 地形发挥");
        var jobMoveCostTable = tables.Single(t => t.TableName == "6.5-3-2 移动消耗");
        var jobRestraintTable = tables.Single(t => t.TableName == "6.5-3-3 兵种相克");
        var jobSeriesRead = reader.Read(testProject, jobSeriesTable, tables);
        var jobTerrainPowerRead = reader.Read(testProject, jobTerrainPowerTable, tables);
        var jobMoveCostRead = reader.Read(testProject, jobMoveCostTable, tables);
        var jobRestraintRead = reader.Read(testProject, jobRestraintTable, tables);
        if (!jobSeriesRead.Validation.IsUsable || !jobTerrainPowerRead.Validation.IsUsable || !jobMoveCostRead.Validation.IsUsable ||
            !jobRestraintRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("兵种系/地形/矩阵写入烟测读取失败。");
        }
    
        var seriesId = 0;
        var jobSeriesRow = FindSmokeRowById(jobSeriesRead.Data, seriesId);
        var terrainPowerRow = FindSmokeRowById(jobTerrainPowerRead.Data, seriesId);
        var moveCostRow = FindSmokeRowById(jobMoveCostRead.Data, seriesId);
        var jobSeriesOriginal = Convert.ToString(jobSeriesRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var jobSeriesChanged = jobSeriesOriginal == "烟测兵" ? "写测骑" : "烟测兵";
        var terrainField = jobTerrainPowerRead.Data.Columns.Contains("平原")
            ? "平原"
            : jobTerrainPowerRead.Data.Columns.Cast<DataColumn>().First(c => c.ColumnName is not "ID" and not "名称").ColumnName;
        var powerOriginal = Convert.ToInt32(terrainPowerRow[terrainField], CultureInfo.InvariantCulture);
        var powerChanged = powerOriginal == 100 ? 90 : 100;
        var moveOriginal = Convert.ToInt32(moveCostRow[terrainField], CultureInfo.InvariantCulture);
        var moveChanged = moveOriginal == 1 ? 2 : 1;
        jobSeriesRow["名称"] = jobSeriesChanged;
        terrainPowerRow[terrainField] = powerChanged;
        moveCostRow[terrainField] = moveChanged;
        var terrainSaves = new[]
        {
            writer.Save(testProject, jobSeriesTable, jobSeriesRead.Data),
            writer.Save(testProject, jobTerrainPowerTable, jobTerrainPowerRead.Data),
            writer.Save(testProject, jobMoveCostTable, jobMoveCostRead.Data)
        };
        var jobSeriesVerify = reader.Read(testProject, jobSeriesTable, tables);
        var jobTerrainPowerVerify = reader.Read(testProject, jobTerrainPowerTable, tables);
        var jobMoveCostVerify = reader.Read(testProject, jobMoveCostTable, tables);
        var jobSeriesActual = Convert.ToString(FindSmokeRowById(jobSeriesVerify.Data, seriesId)["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var powerActual = Convert.ToInt32(FindSmokeRowById(jobTerrainPowerVerify.Data, seriesId)[terrainField], CultureInfo.InvariantCulture);
        var moveActual = Convert.ToInt32(FindSmokeRowById(jobMoveCostVerify.Data, seriesId)[terrainField], CultureInfo.InvariantCulture);
        if (jobSeriesActual != jobSeriesChanged ||
            powerActual != powerChanged ||
            moveActual != moveChanged ||
            terrainSaves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
        {
            throw new InvalidOperationException($"兵种系/地形写入烟测复读失败：series={jobSeriesActual}, power={powerActual}, move={moveActual}");
        }
    
        Console.WriteLine($"JOB_TERRAIN_WRITE_SMOKE_OK id={seriesId}:{jobSeriesOriginal}->{jobSeriesActual} {terrainField}发挥={powerOriginal}->{powerActual} {terrainField}消耗={moveOriginal}->{moveActual} saves={terrainSaves.Length}");
    
        var restraintColumn = jobRestraintRead.Data.Columns.Contains("1") ? "1" : jobRestraintRead.Data.Columns.Cast<DataColumn>().First(c => c.ColumnName is not "ID" and not "名称").ColumnName;
        var restraintRow = FindSmokeRowById(jobRestraintRead.Data, seriesId);
        var restraintOriginal = Convert.ToInt32(restraintRow[restraintColumn], CultureInfo.InvariantCulture);
        var restraintChanged = restraintOriginal == 100 ? 95 : 100;
        restraintRow[restraintColumn] = restraintChanged;
        var equipmentColumn = jobGrowthRead.Data.Columns.Contains("普通剑")
            ? "普通剑"
            : jobGrowthRead.Data.Columns.Cast<DataColumn>().First(c => c.ColumnName is not "ID" and not "名称" and not "移动力" and not "攻击范围" and not "攻击" and not "防御" and not "精神" and not "爆发" and not "士气" and not "HP" and not "MP").ColumnName;
        var equipmentColumnIndex = jobGrowthRead.Data.Columns.IndexOf(equipmentColumn);
        var equipmentFieldOffset = jobGrowthTable.Fields
            .Take(Math.Max(0, equipmentColumnIndex - 1))
            .Where(field => field.ConsumesBytes)
            .Sum(field => field.Size);
        var equipmentFilePath = testProject.ResolveGameFile(jobGrowthTable.FileName);
        var beforeEquipmentBytes = File.ReadAllBytes(equipmentFilePath);
        var expectedEquipmentOffset = jobGrowthTable.DataPos + ((long)jobId * jobGrowthTable.RowSize) + equipmentFieldOffset;
        var equipmentRow = FindSmokeRowById(jobGrowthRead.Data, jobId);
        var equipmentOriginal = Convert.ToInt32(equipmentRow[equipmentColumn], CultureInfo.InvariantCulture);
        var equipmentChanged = equipmentOriginal == 0 ? 1 : 0;
        equipmentRow[equipmentColumn] = equipmentChanged;
        var matrixSaves = new[]
        {
            writer.Save(testProject, jobRestraintTable, jobRestraintRead.Data),
            writer.Save(testProject, jobGrowthTable, jobGrowthRead.Data)
        };
        var jobRestraintVerify = reader.Read(testProject, jobRestraintTable, tables);
        var jobGrowthEquipmentVerify = reader.Read(testProject, jobGrowthTable, tables);
        var afterEquipmentBytes = File.ReadAllBytes(equipmentFilePath);
        var equipmentChangedOffsets = beforeEquipmentBytes
            .Select((value, index) => (value, index))
            .Where(pair => pair.value != afterEquipmentBytes[pair.index])
            .Select(pair => (long)pair.index)
            .ToArray();
        var restraintActual = Convert.ToInt32(FindSmokeRowById(jobRestraintVerify.Data, seriesId)[restraintColumn], CultureInfo.InvariantCulture);
        var equipmentActual = Convert.ToInt32(FindSmokeRowById(jobGrowthEquipmentVerify.Data, jobId)[equipmentColumn], CultureInfo.InvariantCulture);
        if (restraintActual != restraintChanged ||
            equipmentActual != equipmentChanged ||
            !equipmentChangedOffsets.SequenceEqual(new[] { expectedEquipmentOffset }) ||
            matrixSaves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
        {
            throw new InvalidOperationException($"兵种相克/可装备类别写入烟测复读失败：restraint={restraintActual}, {equipmentColumn}={equipmentActual}, offsets={string.Join(",", equipmentChangedOffsets.Select(offset => offset.ToString("X", CultureInfo.InvariantCulture)))} expected={expectedEquipmentOffset:X}");
        }
    
        Console.WriteLine($"JOB_MATRIX_WRITE_SMOKE_OK 相克[{seriesId},{restraintColumn}]={restraintOriginal}->{restraintActual} 详细兵种[{jobId},{equipmentColumn}]={equipmentOriginal}->{equipmentActual} byteOffset=0x{expectedEquipmentOffset:X} saves={matrixSaves.Length}");
    
        var jobEffectDescriptionTable = tables.Single(t => t.TableName == "6.5-7-1 兵种特效说明");
        var jobEffectAssignmentTable = tables.Single(t => t.TableName == "6.5-7-2 兵种特效分配");
        var jobEffectDescriptionRead = reader.Read(testProject, jobEffectDescriptionTable, tables);
        var jobEffectAssignmentRead = reader.Read(testProject, jobEffectAssignmentTable, tables);
        if (!jobEffectDescriptionRead.Validation.IsUsable || !jobEffectAssignmentRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("兵种特效写入烟测读取说明/分配失败。");
        }
    
        var effectId = 0;
        var effectDescriptionRow = FindSmokeRowById(jobEffectDescriptionRead.Data, effectId);
        var effectAssignmentRow = FindSmokeRowById(jobEffectAssignmentRead.Data, effectId);
        var effectPersonOriginal = Convert.ToInt32(effectAssignmentRow["1号武将"], CultureInfo.InvariantCulture);
        var effectPersonChanged = effectPersonOriginal == 0 ? 1 : 0;
        var effectJobOriginal = Convert.ToInt32(effectAssignmentRow["兵种"], CultureInfo.InvariantCulture);
        var effectJobChanged = effectJobOriginal == 255 ? 0 : 255;
        var effectValueOriginal = Convert.ToInt32(effectAssignmentRow["特效值"], CultureInfo.InvariantCulture);
        var effectValueChanged = effectValueOriginal == 1 ? 2 : 1;
        effectDescriptionRow["介绍"] = "CCZ兵种特效烟测";
        effectAssignmentRow["1号武将"] = effectPersonChanged;
        effectAssignmentRow["兵种"] = effectJobChanged;
        effectAssignmentRow["特效值"] = effectValueChanged;
        var effectSaves = new[]
        {
            writer.Save(testProject, jobEffectDescriptionTable, jobEffectDescriptionRead.Data),
            writer.Save(testProject, jobEffectAssignmentTable, jobEffectAssignmentRead.Data)
        };
        var jobEffectDescriptionVerify = reader.Read(testProject, jobEffectDescriptionTable, tables);
        var jobEffectAssignmentVerify = reader.Read(testProject, jobEffectAssignmentTable, tables);
        var effectDescriptionActual = Convert.ToString(FindSmokeRowById(jobEffectDescriptionVerify.Data, effectId)["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
        var effectAssignmentVerifyRow = FindSmokeRowById(jobEffectAssignmentVerify.Data, effectId);
        var effectPersonActual = Convert.ToInt32(effectAssignmentVerifyRow["1号武将"], CultureInfo.InvariantCulture);
        var effectJobActual = Convert.ToInt32(effectAssignmentVerifyRow["兵种"], CultureInfo.InvariantCulture);
        var effectValueActual = Convert.ToInt32(effectAssignmentVerifyRow["特效值"], CultureInfo.InvariantCulture);
        if (!effectDescriptionActual.Contains("CCZ兵种特效烟测", StringComparison.Ordinal) ||
            effectPersonActual != effectPersonChanged ||
            effectJobActual != effectJobChanged ||
            effectValueActual != effectValueChanged ||
            effectSaves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
        {
            throw new InvalidOperationException($"兵种特效写入烟测复读失败：desc={effectDescriptionActual}, person={effectPersonActual}, job={effectJobActual}, value={effectValueActual}");
        }
    
        Console.WriteLine($"JOB_EFFECT_WRITE_SMOKE_OK id={effectId} 1号武将={effectPersonOriginal}->{effectPersonActual} 兵种={effectJobOriginal}->{effectJobActual} 特效值={effectValueOriginal}->{effectValueActual} saves={effectSaves.Length}");
    
        RunJobStrategyWriteSmokeCore(testProject, tables);
    }
    
    static void RunJobStrategyWriteSmokeCore(CczProject testProject, IReadOnlyList<HexTableDefinition> tables)
    {
        var reader = new HexTableReader();
        var writer = new HexTableWriter();
        var strategyTable = tables.Single(t => t.TableName == "6.5-5 策略");
        var strategyLearnTable = tables.Single(t => t.TableName == "6.5-5-7 学会策略");
        var strategyBattleAiTable = tables.Single(t => t.TableName == "6.5-5-8 战场AI策略限制");
        var strategyRead = reader.Read(testProject, strategyTable, tables);
        var strategyLearnRead = reader.Read(testProject, strategyLearnTable, tables);
        var strategyBattleAiRead = reader.Read(testProject, strategyBattleAiTable, tables);
        if (!strategyRead.Validation.IsUsable || !strategyLearnRead.Validation.IsUsable || !strategyBattleAiRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("兵种策略写入烟测读取策略主表或 EKD5 附表失败。");
        }
    
        var strategyId = 0;
        var strategyRow = FindSmokeRowById(strategyRead.Data, strategyId);
        var strategyLearnRow = FindSmokeRowById(strategyLearnRead.Data, strategyId);
        var strategyBattleAiRow = FindSmokeRowById(strategyBattleAiRead.Data, strategyId);
        var jobLevelColumn = strategyRead.Data.Columns.Contains("0")
            ? "0"
            : strategyRead.Data.Columns.Cast<DataColumn>().First(c => int.TryParse(c.ColumnName, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)).ColumnName;
        var strategyLevelOriginal = Convert.ToInt32(strategyRow[jobLevelColumn], CultureInfo.InvariantCulture);
        var strategyLevelChanged = strategyLevelOriginal == 1 ? 2 : 1;
        var strategyLearnOriginal = Convert.ToInt32(strategyLearnRow["内容"], CultureInfo.InvariantCulture);
        var strategyLearnChanged = strategyLearnOriginal == 1 ? 2 : 1;
        var strategyBattleAiOriginal = Convert.ToInt32(strategyBattleAiRow["内容"], CultureInfo.InvariantCulture);
        var strategyBattleAiChanged = strategyBattleAiOriginal == 1 ? 2 : 1;
        strategyRow[jobLevelColumn] = strategyLevelChanged;
        strategyLearnRow["内容"] = strategyLearnChanged;
        strategyBattleAiRow["内容"] = strategyBattleAiChanged;
        var strategySaves = new[]
        {
            writer.Save(testProject, strategyTable, strategyRead.Data),
            writer.Save(testProject, strategyLearnTable, strategyLearnRead.Data),
            writer.Save(testProject, strategyBattleAiTable, strategyBattleAiRead.Data)
        };
        var strategyVerify = reader.Read(testProject, strategyTable, tables);
        var strategyLearnVerify = reader.Read(testProject, strategyLearnTable, tables);
        var strategyBattleAiVerify = reader.Read(testProject, strategyBattleAiTable, tables);
        var strategyLevelActual = Convert.ToInt32(FindSmokeRowById(strategyVerify.Data, strategyId)[jobLevelColumn], CultureInfo.InvariantCulture);
        var strategyLearnActual = Convert.ToInt32(FindSmokeRowById(strategyLearnVerify.Data, strategyId)["内容"], CultureInfo.InvariantCulture);
        var strategyBattleAiActual = Convert.ToInt32(FindSmokeRowById(strategyBattleAiVerify.Data, strategyId)["内容"], CultureInfo.InvariantCulture);
        if (strategyLevelActual != strategyLevelChanged ||
            strategyLearnActual != strategyLearnChanged ||
            strategyBattleAiActual != strategyBattleAiChanged ||
            strategySaves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
        {
            throw new InvalidOperationException($"兵种策略写入烟测复读失败：level={strategyLevelActual}, learn={strategyLearnActual}, battleAi={strategyBattleAiActual}");
        }
    
        Console.WriteLine($"JOB_STRATEGY_WRITE_SMOKE_OK id={strategyId} 学会等级[{jobLevelColumn}]={strategyLevelOriginal}->{strategyLevelActual} 效果索引={strategyLearnOriginal}->{strategyLearnActual} AI战场={strategyBattleAiOriginal}->{strategyBattleAiActual} saves={strategySaves.Length}");
    }
    
    static void RunBattlefieldTextWriteSmokeLayered(CczProject sourceProject, CczProject testProject, IReadOnlyList<HexTableDefinition> tables, string scenarioFileName)
    {
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(sourceProject);
        var dictionary = File.Exists(dictionaryPath) ? new SceneStringParser().Parse(dictionaryPath) : null;
        var scenario = new ScenarioFileReader()
            .ReadAllIndex(testProject)
            .FirstOrDefault(x => x.FileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Battlefield text write smoke could not find copied scenario: {scenarioFileName}");
        var battlefieldService = new BattlefieldEditorService();
        var document = battlefieldService.Load(testProject, scenario, dictionary, tables);
        var canWriteTitle = document.CanWriteCampaignTitle &&
                            document.CampaignTitleCapacityBytes > 0 &&
                            document.CampaignId >= 0;
        if (!canWriteTitle && document.ConditionEntry == null)
        {
            Console.WriteLine($"BATTLEFIELD_TEXT_WRITE_SMOKE_SKIPPED file={scenarioFileName} reason=no_title_capacity_or_condition titleCapacity={document.CampaignTitleCapacityBytes}");
            return;
        }

        var originalTitle = BattlefieldEditorService.NormalizeText(document.CampaignTitle);
        var titleReplacement = string.Equals(originalTitle, "SmokeA", StringComparison.Ordinal) ? "SmokeB" : "SmokeA";
        if (canWriteTitle && EncodingService.GetGbkByteCount(titleReplacement) > document.CampaignTitleCapacityBytes)
        {
            Console.WriteLine($"BATTLEFIELD_TITLE_WRITE_SMOKE_SKIPPED file={scenarioFileName} reason=capacity titleCapacity={document.CampaignTitleCapacityBytes}");
            canWriteTitle = false;
        }

        var originalConditions = document.ConditionEntry == null
            ? string.Empty
            : BattlefieldEditorService.NormalizeText(document.ConditionEntry.Text);
        var conditions = originalConditions;
        if (dictionary != null && document.ConditionEntry != null)
        {
            conditions = originalConditions + " smoke";
            if (EncodingService.GetGbkByteCount(conditions) <= document.ConditionEntry.ByteLength)
            {
                conditions += " overflow-probe";
            }
        }

        var save = battlefieldService.SaveTitleAndConditions(testProject, document, canWriteTitle ? titleReplacement : originalTitle, conditions, dictionary);
        if (save.BackupPaths.Count == 0 ||
            save.BackupPaths.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)) ||
            save.ReportJsonPaths.Count == 0 ||
            save.ReportJsonPaths.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)) ||
            (canWriteTitle && save.TitleSave == null))
        {
            throw new InvalidOperationException("Battlefield text write smoke did not produce backup/report evidence.");
        }

        var actualTitle = originalTitle;
        if (canWriteTitle)
        {
            var titleTable = HexTableNameResolver.ResolveForProject(
                testProject,
                tables,
                new CczEngineProfileService().Detect(testProject).TableHints.CampaignNameTable);
            var titleRead = new HexTableReader().Read(testProject, titleTable, tables);
            actualTitle = BattlefieldEditorService.NormalizeText(
                Convert.ToString(FindSmokeRowById(titleRead.Data, document.CampaignId)["鍚嶇О"], CultureInfo.InvariantCulture));
            if (!string.Equals(actualTitle, titleReplacement, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Battlefield title reread failed: expected={titleReplacement}, actual={actualTitle}");
            }
        }
        else
        {
            Console.WriteLine($"BATTLEFIELD_TITLE_WRITE_SMOKE_SKIPPED file={scenarioFileName} reason=no_writable_title titleCapacity={document.CampaignTitleCapacityBytes}");
        }

        if (dictionary != null && document.ConditionEntry != null)
        {
            if (save.ConditionSave == null)
            {
                throw new InvalidOperationException("Battlefield condition write smoke did not report a condition save.");
            }

            var verifyPath = Path.Combine(testProject.GameRoot, "RS", scenarioFileName);
            var legacyVerify = new LegacyScenarioReader().Read(verifyPath, dictionary);
            var conditionFound = legacyVerify
                .EnumerateCommands()
                .SelectMany(command => command.TextParameters)
                .Any(parameter => string.Equals(
                    BattlefieldEditorService.NormalizeText(parameter.Text),
                    conditions,
                    StringComparison.Ordinal));
            if (!conditionFound)
            {
                throw new InvalidOperationException("Battlefield condition reread failed.");
            }
        }

        Console.WriteLine($"BATTLEFIELD_TEXT_WRITE_SMOKE_OK file={scenarioFileName} title={(canWriteTitle ? $"'{originalTitle}'->'{actualTitle}'" : "skipped")} condition={(document.ConditionEntry == null ? "none" : "expanded")} backups={save.BackupPaths.Count}");
    }

    static void RunBattlefieldTextWriteSmoke(CczProject sourceProject, CczProject testProject, IReadOnlyList<HexTableDefinition> tables, string scenarioFileName)
    {
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(sourceProject);
        var dictionary = File.Exists(dictionaryPath) ? new SceneStringParser().Parse(dictionaryPath) : null;
        var scenario = new ScenarioFileReader()
            .ReadAllIndex(testProject)
            .FirstOrDefault(x => x.FileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"战场制作写入烟测未找到测试副本剧本：{scenarioFileName}");
        var battlefieldService = new BattlefieldEditorService();
        var document = battlefieldService.Load(testProject, scenario, dictionary, tables);
        if (!document.CanWriteCampaignTitle)
        {
            throw new InvalidOperationException($"战场制作写入烟测未在 {scenarioFileName} 找到战役名称表标题。");
        }
    
        var originalTitle = BattlefieldEditorService.NormalizeText(document.CampaignTitle);
        var titleReplacement = string.Equals(originalTitle, "烟测关", StringComparison.Ordinal) ? "写测关" : "烟测关";
        if (EncodingService.GetGbkByteCount(titleReplacement) > document.CampaignTitleCapacityBytes)
        {
            titleReplacement = string.Equals(originalTitle, "烟测", StringComparison.Ordinal) ? "写测" : "烟测";
        }
        if (EncodingService.GetGbkByteCount(titleReplacement) > document.CampaignTitleCapacityBytes)
        {
            throw new InvalidOperationException($"战场制作标题容量不足：file={scenarioFileName}, capacity={document.CampaignTitleCapacityBytes}");
        }
    
        var originalConditions = document.ConditionEntry == null
            ? string.Empty
            : BattlefieldEditorService.NormalizeText(document.ConditionEntry.Text);
        var conditions = originalConditions;
        if (dictionary != null && document.ConditionEntry != null)
        {
            conditions = originalConditions + " 烟测扩容";
            if (EncodingService.GetGbkByteCount(conditions) <= document.ConditionEntry.ByteLength)
            {
                conditions += " 继续追加到超过原容量";
            }
        }

        var save = battlefieldService.SaveTitleAndConditions(testProject, document, titleReplacement, conditions, dictionary);
        if (save.BackupPaths.Count == 0 ||
            save.BackupPaths.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)) ||
            save.ReportJsonPaths.Count == 0 ||
            save.ReportJsonPaths.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)) ||
            save.TitleSave == null ||
            save.ReportJsonPaths
                .Select(File.ReadAllText)
                .All(text => !text.Contains("\"OperationKind\": \"数据表保存\"", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("战场制作标题保存未生成 Imsg.e5 备份或数据表写入报告。");
        }
        if (dictionary != null && document.ConditionEntry != null)
        {
            if (save.ConditionSave == null ||
                save.ReportJsonPaths
                    .Select(File.ReadAllText)
                    .All(text => !text.Contains("\"OperationKind\": \"Legacy R/S eex full-structure write\"", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("战场制作胜败条件扩容保存未生成 R/S eex 完整结构写入报告。");
            }
        }
    
        var titleTable = HexTableNameResolver.ResolveForProject(
            testProject,
            tables,
            new CczEngineProfileService().Detect(testProject).TableHints.CampaignNameTable);
        var titleRead = new HexTableReader().Read(testProject, titleTable, tables);
        var actualTitle = BattlefieldEditorService.NormalizeText(
            Convert.ToString(FindSmokeRowById(titleRead.Data, document.CampaignId)["名称"], CultureInfo.InvariantCulture));
        if (!string.Equals(actualTitle, titleReplacement, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"战场制作标题复读失败：expected={titleReplacement}, actual={actualTitle}");
        }

        if (dictionary != null && document.ConditionEntry != null)
        {
            var verifyPath = Path.Combine(testProject.GameRoot, "RS", scenarioFileName);
            var legacyVerify = new LegacyScenarioReader().Read(verifyPath, dictionary);
            var conditionFound = legacyVerify
                .EnumerateCommands()
                .SelectMany(command => command.TextParameters)
                .Any(parameter => string.Equals(
                    BattlefieldEditorService.NormalizeText(parameter.Text),
                    conditions,
                    StringComparison.Ordinal));
            if (!conditionFound)
            {
                throw new InvalidOperationException("战场制作胜败条件扩容复读失败。");
            }
        }
    
        Console.WriteLine($"BATTLEFIELD_TEXT_WRITE_SMOKE_OK file={scenarioFileName} title='{originalTitle}'->'{actualTitle}' condition={(document.ConditionEntry == null ? "none" : "expanded")} backups={save.BackupPaths.Count}");
    }
    
    static void RunBattlefieldDeploymentWriteSmoke(CczProject sourceProject, CczProject testProject, IReadOnlyList<HexTableDefinition> tables, string scenarioFileName)
    {
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(sourceProject);
        if (!File.Exists(dictionaryPath))
        {
            throw new FileNotFoundException("Battlefield deployment write smoke requires CczString.ini.", dictionaryPath);
        }
    
        var dictionary = new SceneStringParser().Parse(dictionaryPath);
        var scenario = new ScenarioFileReader()
            .ReadAllIndex(testProject)
            .FirstOrDefault(x => x.FileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Battlefield deployment write smoke could not find {scenarioFileName}.");
        var service = new BattlefieldEditorService();
        var document = service.Load(testProject, scenario, dictionary, tables);
        var candidate = document.UnitCandidates.FirstOrDefault(x =>
            x.TargetKey.Contains("Record=", StringComparison.OrdinalIgnoreCase) &&
            BattlefieldEditorService.TryExtractFirstCoordinate(x, out _, out _) &&
            BattlefieldEditorService.TryExtractPersonId(x, out _));
        if (candidate == null)
        {
            var sample = string.Join(" | ", document.UnitCandidates.Take(12).Select(x => $"{x.Category}:{x.PersonHint}:{x.CoordinateHint}:{x.TargetKey}"));
            throw new InvalidOperationException($"Battlefield deployment write smoke found no writable 46/47 direct-coordinate candidate in {scenarioFileName}. sample={sample}");
        }
    
        if (!BattlefieldEditorService.TryExtractFirstCoordinate(candidate, out var originalX, out var originalY) ||
            !BattlefieldEditorService.TryExtractPersonId(candidate, out var personId))
        {
            throw new InvalidOperationException("Battlefield deployment candidate coordinate/person parse failed.");
        }
    
        var changedX = originalX == 0 ? 1 : 0;
        var changedY = originalY;
        var placement = new BattlefieldPlacedUnit
        {
            TargetKey = candidate.TargetKey,
            PersonId = personId,
            Name = "SmokeUnit",
            Faction = candidate.Category.Contains("敌军", StringComparison.Ordinal) ? "敌军" :
                      candidate.Category.Contains("友军", StringComparison.Ordinal) ? "友军" : "我军",
            AiMode = candidate.Category == "我军出场" ? "被动" : "主动",
            GridX = changedX,
            GridY = changedY,
            Source = "S剧本预览",
            PlacementNote = "Smoke battlefield deployment write"
        };
    
        var legacyDocument = new LegacyScenarioReader().Read(scenario.Path, dictionary);
        var locator = ParseBattlefieldStatusSmokeLocator(candidate.TargetKey);
        var applyOnly = new BattlefieldDeploymentWriteService().ApplyScriptPlacements(legacyDocument, new[] { placement });
        if (applyOnly.WrittenRecordCount != 1)
        {
            throw new InvalidOperationException("Battlefield deployment in-memory apply did not update exactly one record.");
        }

        var memoryCommand = FindSmokeCommand(legacyDocument, locator)
            ?? throw new InvalidOperationException("Battlefield deployment in-memory apply lost target command.");
        var layout = GetDeploymentRecordLayout(memoryCommand.CommandId);
        var start = locator.RecordIndex * layout.GroupSize;
        var xIndex = memoryCommand.CommandId == 0x46 ? 2 : 3;
        var yIndex = memoryCommand.CommandId == 0x46 ? 3 : 4;
        AssertSmokeParameter(memoryCommand, start + xIndex, changedX, "inMemoryX");
        AssertSmokeParameter(memoryCommand, start + yIndex, changedY, "inMemoryY");

        var write = new BattlefieldDeploymentWriteService().SaveScriptPlacements(
            testProject,
            scenario,
            dictionary,
            legacyDocument,
            new[] { placement });
        if (write.WrittenRecordCount != 1 ||
            string.IsNullOrWhiteSpace(write.BackupPath) ||
            !File.Exists(write.BackupPath) ||
            string.IsNullOrWhiteSpace(write.ReportJsonPath) ||
            !File.Exists(write.ReportJsonPath))
        {
            throw new InvalidOperationException("Battlefield deployment write did not produce reread, backup, or report evidence.");
        }
    
        var verifyDocument = service.Load(testProject, scenario, dictionary, tables);
        var verifyCandidate = verifyDocument.UnitCandidates.FirstOrDefault(x => x.TargetKey.Equals(candidate.TargetKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Battlefield deployment write reread lost target candidate.");
        if (!BattlefieldEditorService.TryExtractFirstCoordinate(verifyCandidate, out var actualX, out var actualY) ||
            actualX != changedX ||
            actualY != changedY)
        {
            throw new InvalidOperationException($"Battlefield deployment write reread failed: expected=({changedX},{changedY}), actual=({actualX},{actualY}).");
        }
    
        Console.WriteLine($"BATTLEFIELD_DEPLOYMENT_WRITE_SMOKE_OK file={scenarioFileName} target={candidate.TargetKey} person={personId} coord=({originalX},{originalY})->({actualX},{actualY}) backup={Path.GetFileName(write.BackupPath)} changedBytes={write.ChangedBytes}");

        RunBattlefieldUnitStatusWriteSmoke(testProject, scenario, dictionary, candidate.TargetKey, personId);
    
        var reviewService = new BattlefieldUnitReviewService();
        var localPlacement = new BattlefieldPlacedUnit
        {
            TargetKey = $"Placement#{scenarioFileName}#99,99#{personId}",
            PersonId = personId,
            Name = "SmokeLocalOnly",
            Faction = placement.Faction,
            AiMode = "被动",
            GridX = 2,
            GridY = 2,
            Source = "拖放",
            PlacementNote = "Smoke local-only placement"
        };
        var reviewPath = reviewService.Save(testProject, verifyDocument, verifyDocument.UnitCandidates, new[] { placement, localPlacement });
        var savedPlacementReviews = reviewService.Load(testProject)
            .Where(x => x.IsPlacement && x.ScenarioFileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var reloadedPlacements = reviewService.LoadPlacements(testProject, verifyDocument);
        if (savedPlacementReviews.Any(x => x.TargetKey.Equals(placement.TargetKey, StringComparison.OrdinalIgnoreCase)) ||
            reloadedPlacements.Any(x => x.TargetKey.Equals(placement.TargetKey, StringComparison.OrdinalIgnoreCase)) ||
            reloadedPlacements.Count(x => x.TargetKey.Equals(localPlacement.TargetKey, StringComparison.OrdinalIgnoreCase)) != 1)
        {
            throw new InvalidOperationException("Battlefield script-backed placement review cache was reloaded as a duplicate map unit.");
        }
    
        Console.WriteLine($"BATTLEFIELD_DEPLOYMENT_CACHE_DEDUP_OK file={scenarioFileName} local={localPlacement.TargetKey} scriptSkipped={placement.TargetKey} notes={Path.GetFileName(reviewPath)}");

        var clearDocument = new LegacyScenarioReader().Read(scenario.Path, dictionary);
        var clearResult = new BattlefieldDeploymentWriteService().ClearFriendEnemyScriptPlacements(clearDocument, new[] { placement });
        if (clearResult.WrittenRecordCount != 1)
        {
            throw new InvalidOperationException("Battlefield deployment clear did not update exactly one 46/47 record.");
        }

        var clearCommand = FindSmokeCommand(clearDocument, locator)
            ?? throw new InvalidOperationException("Battlefield deployment clear lost target command.");
        var clearLayout = GetDeploymentRecordLayout(clearCommand.CommandId);
        var clearStart = locator.RecordIndex * clearLayout.GroupSize;
        for (var index = 0; index < clearLayout.GroupSize; index++)
        {
            AssertSmokeParameter(clearCommand, clearStart + index, 0, "cleared slot " + index);
        }

        var clearWrite = new LegacyScenarioWriter().Save(
            testProject,
            Path.Combine("RS", scenario.FileName),
            clearDocument,
            dictionary,
            "Smoke clear battlefield 46/47 deployment slot from current tree");
        var clearVerifyDocument = service.Load(testProject, scenario, dictionary, tables);
        if (clearVerifyDocument.UnitCandidates.Any(x => x.TargetKey.Equals(candidate.TargetKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Battlefield deployment clear reread still exposed the cleared 46/47 record as a unit candidate.");
        }

        Console.WriteLine($"BATTLEFIELD_DEPLOYMENT_CLEAR_SLOT_OK file={scenarioFileName} target={candidate.TargetKey} backup={Path.GetFileName(clearWrite.BackupPath)} changedBytes={clearWrite.ChangedBytes}");
    
        var emptySlot = FindOrCreateEmptyBattlefieldDeploymentSlot(testProject, scenario, dictionary, service, tables);
        if (emptySlot != null)
        {
            var emptyPlacement = new BattlefieldPlacedUnit
            {
                TargetKey = emptySlot.TargetKey,
                PersonId = personId,
                Name = "SmokeAutoDrop",
                Faction = emptySlot.Category.Contains("敌军", StringComparison.Ordinal) ? "敌军" : "友军",
                AiMode = "主动",
                GridX = changedX == 0 ? 1 : 0,
                GridY = changedY,
                Source = "纯拖放自动绑定",
                PlacementNote = "Smoke battlefield empty slot auto-bind write"
            };
            var emptyWrite = new BattlefieldDeploymentWriteService().SaveScriptPlacements(
                testProject,
                scenario,
                dictionary,
                new[] { emptyPlacement });
            var emptyVerifyDocument = service.Load(testProject, scenario, dictionary, tables);
            var emptyVerify = emptyVerifyDocument.UnitCandidates.FirstOrDefault(x => x.TargetKey.Equals(emptySlot.TargetKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Battlefield empty deployment slot write did not become a visible candidate after reread.");
            var emptyPersonParsed = BattlefieldEditorService.TryExtractPersonId(emptyVerify, out var emptyPersonId);
            var emptyCoordinateParsed = BattlefieldEditorService.TryExtractFirstCoordinate(emptyVerify, out var emptyActualX, out var emptyActualY);
            if (!emptyPersonParsed ||
                !emptyCoordinateParsed ||
                emptyPersonId != personId ||
                emptyActualX != emptyPlacement.GridX ||
                emptyActualY != emptyPlacement.GridY)
            {
                throw new InvalidOperationException($"Battlefield empty slot auto-bind write reread failed: person={emptyPersonId}, coord=({emptyActualX},{emptyActualY}).");
            }
    
            Console.WriteLine($"BATTLEFIELD_DEPLOYMENT_EMPTY_SLOT_WRITE_OK file={scenarioFileName} target={emptySlot.TargetKey} person={personId} coord=({emptyPlacement.GridX},{emptyPlacement.GridY}) changedBytes={emptyWrite.ChangedBytes}");
        }
    
        var allyScenario = new ScenarioFileReader()
            .ReadAllIndex(testProject)
            .FirstOrDefault(x => x.FileName.Equals("S_01.eex", StringComparison.OrdinalIgnoreCase))
            ?? scenario;
        var allyDocument = service.Load(testProject, allyScenario, dictionary, tables);
        var allySlot = BattlefieldEditorService.BuildDeploymentSlotInfos(allyDocument)
            .FirstOrDefault(x => x.IsAllySlot && x.GridX >= 0 && x.GridY >= 0);
        if (allySlot != null)
        {
            var allyPlacement = new BattlefieldPlacedUnit
            {
                TargetKey = allySlot.TargetKey,
                PersonId = personId,
                Name = "SmokeAllySlot",
                Faction = "我军",
                AiMode = "被动",
                Direction = "下",
                Hidden = false,
                GridX = allySlot.GridX == 0 ? 1 : 0,
                GridY = allySlot.GridY,
                Source = "纯拖放自动绑定",
                PlacementNote = "Smoke battlefield 4B slot auto-bind write"
            };
            var allyWrite = new BattlefieldDeploymentWriteService().SaveScriptPlacements(
                testProject,
                allyScenario,
                dictionary,
                new[] { allyPlacement });
            var allyVerifyDocument = service.Load(testProject, allyScenario, dictionary, tables);
            var allyVerify = allyVerifyDocument.UnitCandidates.FirstOrDefault(x => x.TargetKey.Equals(allySlot.TargetKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Battlefield 4B slot write reread lost target candidate.");
            if (!BattlefieldEditorService.TryExtractFirstCoordinate(allyVerify, out var allyActualX, out var allyActualY) ||
                allyActualX != allyPlacement.GridX ||
                allyActualY != allyPlacement.GridY)
            {
                throw new InvalidOperationException($"Battlefield 4B slot write reread failed: expected=({allyPlacement.GridX},{allyPlacement.GridY}), actual=({allyActualX},{allyActualY}).");
            }
    
            Console.WriteLine($"BATTLEFIELD_DEPLOYMENT_ALLY_SLOT_WRITE_OK file={allyScenario.FileName} target={allySlot.TargetKey} coord=({allySlot.GridX},{allySlot.GridY})->({allyActualX},{allyActualY}) changedBytes={allyWrite.ChangedBytes}");
        }
    }

    static void RunBattlefieldUnitStatusWriteSmoke(
        CczProject testProject,
        ScenarioFileInfo scenario,
        SceneStringDocument dictionary,
        string targetKey,
        int personId)
    {
        var service = new BattlefieldUnitStatusWriteService();
        var placement = new BattlefieldPlacedUnit
        {
            TargetKey = targetKey,
            PersonId = personId,
            Name = "SmokeUnitStatus",
            GridX = 1,
            GridY = 1
        };
        if (!BattlefieldUnitStatusWriteService.IsWritableStatusTarget(placement))
        {
            Console.WriteLine($"BATTLEFIELD_UNIT_STATUS_WRITE_SMOKE_SKIPPED target={targetKey}");
            return;
        }

        var before = new LegacyScenarioReader().Read(scenario.Path, dictionary);
        var locator = ParseBattlefieldStatusSmokeLocator(targetKey);
        var beforeCommand = FindSmokeCommand(before, locator)
            ?? throw new InvalidOperationException("Battlefield unit status smoke could not find source deployment command before write.");
        var beforeIndex = FindSmokeCommandList(before, beforeCommand, out var beforeList)
            ? beforeList.IndexOf(beforeCommand)
            : -1;
        if (beforeIndex < 0)
        {
            throw new InvalidOperationException("Battlefield unit status smoke could not locate source command list.");
        }
        var beforeBlockEnd = FindSmokeDeploymentBlockEnd(beforeList, beforeIndex);

        var draft = service.LoadDraft(before, scenario.FileName, placement);
        draft.LevelBonus = draft.LevelBonus == 3 ? 4 : 3;
        draft.JobLevel = draft.JobLevel == 2 ? 1 : 2;
        draft.AiPolicy = draft.AiPolicy == 1 ? 2 : 1;
        draft.Weapon = 2;
        draft.WeaponLevel = 1;
        draft.Armor = 2;
        draft.ArmorLevel = 1;
        draft.Assist = 2;
        draft.JobId = 1;
        for (var i = 0; i < draft.Abilities.Count; i++)
        {
            draft.Abilities[i].Operation = 0;
            draft.Abilities[i].Value = 80 + i;
        }

        var applyOnly = service.Apply(testProject, before, draft);
        if (applyOnly.InsertedCommandCount + applyOnly.UpdatedCommandCount < 7)
        {
            throw new InvalidOperationException("Battlefield unit status in-memory apply did not create expected commands.");
        }

        var memoryCommand = FindSmokeCommand(before, locator)
            ?? throw new InvalidOperationException("Battlefield unit status in-memory apply lost source deployment command.");
        var memoryLayout = GetDeploymentRecordLayout(memoryCommand.CommandId);
        var memoryLevelIndex = memoryCommand.CommandId == 0x46 ? 5 : 6;
        var memoryJobLevelIndex = memoryCommand.CommandId == 0x46 ? 6 : 7;
        var memoryAiIndex = memoryCommand.CommandId == 0x46 ? 7 : 8;
        var memoryStart = locator.RecordIndex * memoryLayout.GroupSize;
        AssertSmokeParameter(memoryCommand, memoryStart + memoryLevelIndex, draft.LevelBonus!.Value, "inMemoryLevel");
        AssertSmokeParameter(memoryCommand, memoryStart + memoryJobLevelIndex, draft.JobLevel!.Value, "inMemoryJobLevel");
        AssertSmokeParameter(memoryCommand, memoryStart + memoryAiIndex, draft.AiPolicy!.Value, "inMemoryAi");

        var write = service.Save(testProject, scenario, dictionary, before, draft);
        if (write.InsertedCommandCount + write.UpdatedCommandCount < 7 ||
            string.IsNullOrWhiteSpace(write.BackupPath) ||
            !File.Exists(write.BackupPath) ||
            string.IsNullOrWhiteSpace(write.ReportJsonPath) ||
            !File.Exists(write.ReportJsonPath))
        {
            throw new InvalidOperationException("Battlefield unit status write smoke did not create expected commands, backup, or report.");
        }

        var verify = new LegacyScenarioReader().Read(scenario.Path, dictionary);
        var command = FindSmokeCommand(verify, locator)
            ?? throw new InvalidOperationException("Battlefield unit status smoke reread lost source deployment command.");
        var layout = GetDeploymentRecordLayout(command.CommandId);
        var levelIndex = command.CommandId == 0x46 ? 5 : 6;
        var jobLevelIndex = command.CommandId == 0x46 ? 6 : 7;
        var aiIndex = command.CommandId == 0x46 ? 7 : 8;
        var start = locator.RecordIndex * layout.GroupSize;
        AssertSmokeParameter(command, start + levelIndex, draft.LevelBonus!.Value, "level");
        AssertSmokeParameter(command, start + jobLevelIndex, draft.JobLevel!.Value, "jobLevel");
        AssertSmokeParameter(command, start + aiIndex, draft.AiPolicy!.Value, "ai");

        if (!FindSmokeCommandList(verify, command, out var list))
        {
            throw new InvalidOperationException("Battlefield unit status smoke could not locate reread command list.");
        }
        var statusSegmentEnd = FindSmokeStatusSegmentEnd(list, beforeBlockEnd);
        var deploymentStatusNodes = list
            .Skip(beforeBlockEnd)
            .Take(statusSegmentEnd - beforeBlockEnd)
            .ToList();
        var deploymentStatusCommands = FlattenSmokeStatusCommands(deploymentStatusNodes);
        var equipmentBlockTitle = BattlefieldUnitStatusWriteService.GetEquipmentStatusBlockTitle(command.CommandId);
        var runtimeBlockTitle = BattlefieldUnitStatusWriteService.GetRuntimeStatusBlockTitle(command.CommandId);
        var equipmentBlock = AssertSmokeInternalInfoBlock(
            list,
            beforeBlockEnd,
            statusSegmentEnd,
            equipmentBlockTitle,
            personId,
            expectEquipment: true,
            expectRuntime: false);
        var equipment = deploymentStatusCommands.FirstOrDefault(x => x.CommandId == 0x48 && x.Parameters.Count >= 6 && x.Parameters[0].IntValue == personId)
            ?? throw new InvalidOperationException("Battlefield unit status smoke did not reread inserted 48.");
        AssertSmokeParameter(equipment, 1, 2, "weapon");
        AssertSmokeParameter(equipment, 2, 1, "weaponLevel");
        AssertSmokeParameter(equipment, 3, 2, "armor");
        AssertSmokeParameter(equipment, 4, 1, "armorLevel");
        AssertSmokeParameter(equipment, 5, 2, "assist");

        var drawingStatusCommands = FindSmokeDrawingStatusCommands(
            verify,
            command.SceneIndex,
            command.SectionIndex,
            out var drawingList,
            out var drawingIndex,
            out var drawingStatusNodes);
        var runtimeBlock = AssertSmokeInternalInfoBlock(
            drawingList,
            drawingIndex + 1,
            drawingIndex + 1 + drawingStatusNodes.Count,
            runtimeBlockTitle,
            personId,
            expectEquipment: false,
            expectRuntime: true);
        var runtimeBlockCount = drawingStatusNodes.Count(command =>
            command.CommandId == 0x02 &&
            command.ChildBlock != null &&
            string.Equals(command.TextParameters.FirstOrDefault()?.Text?.Trim(), runtimeBlockTitle, StringComparison.Ordinal));
        if (runtimeBlockCount != 1)
        {
            throw new InvalidOperationException($"Battlefield unit status smoke expected one runtime folded block for consecutive 52/38 commands, actual={runtimeBlockCount}.");
        }
        var job = drawingStatusCommands.FirstOrDefault(x => x.CommandId == 0x52 && x.Parameters.Count >= 2 && x.Parameters[0].IntValue == personId)
            ?? throw new InvalidOperationException("Battlefield unit status smoke did not reread inserted 52.");
        AssertSmokeParameter(job, 1, 1, "job");
        var runtimeBlockCommands = runtimeBlock.ChildBlock?.Commands
            ?? throw new InvalidOperationException("Battlefield unit status smoke runtime block lost child block.");
        var jobIndex = runtimeBlockCommands.IndexOf(job);
        if (jobIndex <= 0 ||
            jobIndex >= runtimeBlockCommands.Count - 1 ||
            !IsSmokeAbilityRecalcToggle(runtimeBlockCommands[jobIndex - 1], 1) ||
            !IsSmokeAbilityRecalcToggle(runtimeBlockCommands[jobIndex + 1], 0))
        {
            throw new InvalidOperationException("Battlefield unit status smoke did not wrap 52 with 4081 ability recalculation toggles.");
        }

        foreach (var ability in draft.Abilities)
        {
            var abilityCommand = drawingStatusCommands.FirstOrDefault(x =>
                x.CommandId == 0x38 &&
                x.Parameters.Count >= 4 &&
                x.Parameters[0].IntValue == personId &&
                x.Parameters[1].IntValue == ability.AbilityId)
                ?? throw new InvalidOperationException($"Battlefield unit status smoke did not reread inserted 38 ability={ability.AbilityId}.");
            AssertSmokeParameter(abilityCommand, 2, 0, ability.Name + " op");
            AssertSmokeParameter(abilityCommand, 3, ability.Value!.Value, ability.Name);
        }

        var firstStatusIndex = list.FindIndex(x => ReferenceEquals(x, equipmentBlock));
        if (firstStatusIndex < beforeBlockEnd)
        {
            throw new InvalidOperationException($"Battlefield unit status smoke inserted status command before deployment block end: insert={firstStatusIndex}, blockEnd={beforeBlockEnd}.");
        }

        var firstRuntimeStatusIndex = drawingList.FindIndex(x =>
            drawingStatusNodes.Any(commandNode => ReferenceEquals(commandNode, x)));
        if (firstRuntimeStatusIndex <= drawingIndex)
        {
            throw new InvalidOperationException($"Battlefield unit status smoke did not insert runtime status commands below 1C drawing: drawing={drawingIndex}, status={firstRuntimeStatusIndex}.");
        }

        var combinedBlockCount = CountSmokeInternalInfoBlocks(verify, BattlefieldUnitStatusWriteService.CombinedStatusBlockTitle, personId);
        if (combinedBlockCount != 0)
        {
            throw new InvalidOperationException($"Battlefield unit status smoke should not create combined status blocks, actual={combinedBlockCount}.");
        }

        var equipmentBlockCountBeforeSecondSave = CountSmokeInternalInfoBlocks(verify, equipmentBlockTitle, personId);
        var runtimeBlockCountBeforeSecondSave = CountSmokeInternalInfoBlocks(verify, runtimeBlockTitle, personId);
        var secondWrite = service.Save(testProject, scenario, dictionary, draft);
        var secondVerify = new LegacyScenarioReader().Read(scenario.Path, dictionary);
        var equipmentBlockCountAfterSecondSave = CountSmokeInternalInfoBlocks(secondVerify, equipmentBlockTitle, personId);
        var runtimeBlockCountAfterSecondSave = CountSmokeInternalInfoBlocks(secondVerify, runtimeBlockTitle, personId);
        if (equipmentBlockCountAfterSecondSave != equipmentBlockCountBeforeSecondSave ||
            runtimeBlockCountAfterSecondSave != runtimeBlockCountBeforeSecondSave)
        {
            throw new InvalidOperationException($"Battlefield unit status smoke duplicated fixed comment blocks on second save: equipment={equipmentBlockCountBeforeSecondSave}->{equipmentBlockCountAfterSecondSave}, runtime={runtimeBlockCountBeforeSecondSave}->{runtimeBlockCountAfterSecondSave}.");
        }
        if (secondWrite.InsertedCommandCount != 0)
        {
            throw new InvalidOperationException($"Battlefield unit status smoke second save should update existing folded blocks, inserted={secondWrite.InsertedCommandCount}.");
        }

        Console.WriteLine($"BATTLEFIELD_UNIT_STATUS_WRITE_SMOKE_OK file={scenario.FileName} target={targetKey} person={personId} level={draft.LevelBonus} jobLevel={draft.JobLevel} ai={draft.AiPolicy} inserted={write.InsertedCommandCount} updated={write.UpdatedCommandCount} backup={Path.GetFileName(write.BackupPath)} changedBytes={write.ChangedBytes}");
    }
    
    static BattlefieldDeploymentSlotInfo? FindOrCreateEmptyBattlefieldDeploymentSlot(
        CczProject testProject,
        ScenarioFileInfo scenario,
        SceneStringDocument dictionary,
        BattlefieldEditorService service,
        IReadOnlyList<HexTableDefinition> tables)
    {
        var document = service.Load(testProject, scenario, dictionary, tables);
        var existing = BattlefieldEditorService.BuildDeploymentSlotInfos(document)
            .FirstOrDefault(x => !x.IsAllySlot && x.IsBlank && x.WritesPerson);
        if (existing != null) return existing;
    
        var legacyDocument = new LegacyScenarioReader().Read(scenario.Path, dictionary);
        var command = legacyDocument.EnumerateCommands()
            .FirstOrDefault(x => x.CommandId is 0x46 or 0x47 && TryGetDeploymentRecordLayout(x.CommandId, out var layout) && x.Parameters.Count >= layout.GroupSize);
        if (command == null) return null;
    
        var (groupSize, recordCount) = GetDeploymentRecordLayout(command.CommandId);
        for (var recordIndex = recordCount - 1; recordIndex >= 0; recordIndex--)
        {
            var start = recordIndex * groupSize;
            if (start + groupSize > command.Parameters.Count) continue;
    
            for (var index = 0; index < groupSize; index++)
            {
                var parameter = command.Parameters[start + index];
                parameter.IntValue = 0;
                parameter.Text = string.Empty;
                parameter.Values.Clear();
            }
    
            new LegacyScenarioWriter().Save(
                testProject,
                Path.Combine("RS", scenario.FileName),
                legacyDocument,
                dictionary,
                "Smoke synthesize empty battlefield 46/47 deployment slot");
    
            var reread = service.Load(testProject, scenario, dictionary, tables);
            return BattlefieldEditorService.BuildDeploymentSlotInfos(reread)
                .FirstOrDefault(x => !x.IsAllySlot && x.IsBlank && x.WritesPerson);
        }
    
        return null;
    }
    
    static bool TryGetDeploymentRecordLayout(int commandId, out (int GroupSize, int RecordCount) layout)
    {
        if (commandId == 0x46)
        {
            layout = (11, 20);
            return true;
        }
    
        if (commandId == 0x47)
        {
            layout = (12, 80);
            return true;
        }
    
        layout = default;
        return false;
    }
    
    static (int GroupSize, int RecordCount) GetDeploymentRecordLayout(int commandId)
        => TryGetDeploymentRecordLayout(commandId, out var layout)
            ? layout
            : throw new ArgumentOutOfRangeException(nameof(commandId), commandId, "Unsupported deployment command.");

    static string BuildSmokeUnitTargetKey(LegacyScenarioCommandNode command, int recordIndex)
        => $"Scene={command.SceneIndex};Section={command.SectionIndex};Command={command.CommandIndex};Offset={command.FileOffset:X6};Id={command.CommandIdHex};Record={recordIndex.ToString(CultureInfo.InvariantCulture)}";

    static BattlefieldStatusSmokeTarget? FindSmokeWritableStatusTarget(LegacyScenarioDocument document)
    {
        foreach (var command in document.EnumerateCommands().Where(x => x.SceneIndex == 1 && (x.CommandId is 0x46 or 0x47)))
        {
            if (!TryGetDeploymentRecordLayout(command.CommandId, out var layout)) continue;
            if (!document.EnumerateCommands().Any(x =>
                    x.SceneIndex == command.SceneIndex &&
                    x.SectionIndex == command.SectionIndex &&
                    x.CommandId == 0x1C))
            {
                continue;
            }

            for (var recordIndex = 0; recordIndex < layout.RecordCount; recordIndex++)
            {
                var start = recordIndex * layout.GroupSize;
                if (start + layout.GroupSize > command.Parameters.Count) break;

                var personId = command.Parameters[start].IntValue;
                if (personId > 0)
                {
                    return new BattlefieldStatusSmokeTarget(command, recordIndex, personId);
                }
            }
        }

        return null;
    }

    static BattlefieldStatusSmokeLocator ParseBattlefieldStatusSmokeLocator(string targetKey)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in targetKey.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = part.IndexOf('=');
            if (index <= 0) continue;
            values[part[..index].Trim()] = part[(index + 1)..].Trim();
        }

        return new BattlefieldStatusSmokeLocator(
            int.Parse(values["Scene"], CultureInfo.InvariantCulture),
            int.Parse(values["Section"], CultureInfo.InvariantCulture),
            int.Parse(values["Command"], CultureInfo.InvariantCulture),
            values.GetValueOrDefault("Offset", string.Empty),
            values.GetValueOrDefault("Id", string.Empty),
            int.Parse(values["Record"], CultureInfo.InvariantCulture));
    }

    static LegacyScenarioCommandNode? FindSmokeCommand(LegacyScenarioDocument document, BattlefieldStatusSmokeLocator locator)
        => document.EnumerateCommands().FirstOrDefault(command =>
            command.SceneIndex == locator.SceneIndex &&
            command.SectionIndex == locator.SectionIndex &&
            command.CommandIndex == locator.CommandIndex &&
            (string.IsNullOrWhiteSpace(locator.OffsetHex) || CCZModStudio.Core.HexDisplayFormatter.EqualsText(CCZModStudio.Core.HexDisplayFormatter.Format(command.FileOffset, 6), locator.OffsetHex)) &&
            (string.IsNullOrWhiteSpace(locator.CommandIdHex) || string.Equals(command.CommandIdHex, locator.CommandIdHex, StringComparison.OrdinalIgnoreCase)));

    static bool FindSmokeCommandList(
        LegacyScenarioDocument document,
        LegacyScenarioCommandNode target,
        out List<LegacyScenarioCommandNode> list)
    {
        foreach (var section in document.Scenes.SelectMany(scene => scene.Sections))
        {
            if (FindSmokeCommandList(section.Commands, target, out list))
            {
                return true;
            }
        }

        list = null!;
        return false;
    }

    static bool FindSmokeCommandList(
        List<LegacyScenarioCommandNode> commands,
        LegacyScenarioCommandNode target,
        out List<LegacyScenarioCommandNode> list)
    {
        foreach (var command in commands)
        {
            if (ReferenceEquals(command, target))
            {
                list = commands;
                return true;
            }

            if (command.ChildBlock != null && FindSmokeCommandList(command.ChildBlock.Commands, target, out list))
            {
                return true;
            }
        }

        list = null!;
        return false;
    }

    static int FindSmokeDeploymentBlockEnd(IReadOnlyList<LegacyScenarioCommandNode> commands, int sourceIndex)
    {
        var end = sourceIndex + 1;
        while (end < commands.Count && commands[end].CommandId is 0x46 or 0x47 or 0x4B)
        {
            end++;
        }

        return end;
    }

    static int FindSmokeStatusSegmentEnd(IReadOnlyList<LegacyScenarioCommandNode> commands, int sourceIndex)
    {
        var end = sourceIndex;
        while (end < commands.Count && IsSmokeStatusSequenceStart(commands, end, includeEquipment: true))
        {
            end += GetSmokeStatusSequenceLength(commands, end, includeEquipment: true);
        }

        return end;
    }

    static IReadOnlyList<LegacyScenarioCommandNode> FindSmokeDrawingStatusCommands(
        LegacyScenarioDocument document,
        int sceneIndex,
        int sectionIndex,
        out List<LegacyScenarioCommandNode> drawingList,
        out int drawingIndex,
        out IReadOnlyList<LegacyScenarioCommandNode> statusNodes)
    {
        var drawing = document.EnumerateCommands()
            .Where(command =>
                command.SceneIndex == sceneIndex &&
                command.SectionIndex == sectionIndex &&
                command.CommandId == 0x1C)
            .OrderBy(command => command.CommandIndex)
            .LastOrDefault()
            ?? throw new InvalidOperationException($"Battlefield unit status smoke could not find 1C drawing in Scene={sceneIndex} Section={sectionIndex}.");
        if (!FindSmokeCommandList(document, drawing, out drawingList))
        {
            throw new InvalidOperationException("Battlefield unit status smoke could not locate drawing command list.");
        }

        drawingIndex = drawingList.IndexOf(drawing);
        if (drawingIndex < 0)
        {
            throw new InvalidOperationException("Battlefield unit status smoke drawing command is not in its located list.");
        }

        var end = drawingIndex + 1;
        while (end < drawingList.Count && IsSmokeStatusSequenceStart(drawingList, end, includeEquipment: false))
        {
            end += GetSmokeStatusSequenceLength(drawingList, end, includeEquipment: false);
        }

        statusNodes = drawingList
            .Skip(drawingIndex + 1)
            .Take(end - drawingIndex - 1)
            .ToList();
        return FlattenSmokeStatusCommands(statusNodes);
    }

    static LegacyScenarioCommandNode AssertSmokeInternalInfoBlock(
        IReadOnlyList<LegacyScenarioCommandNode> list,
        int start,
        int end,
        string expectedTitle,
        int personId,
        bool expectEquipment,
        bool expectRuntime)
    {
        for (var i = start; i < end; i++)
        {
            if (list[i].CommandId != 0x01 || i + 1 >= end || list[i + 1].CommandId != 0x02)
            {
                continue;
            }

            var block = list[i + 1];
            var title = block.TextParameters.FirstOrDefault()?.Text.Trim() ?? string.Empty;
            if (!title.Equals(expectedTitle, StringComparison.Ordinal))
            {
                continue;
            }

            if (!block.OpensSubEventBlock || block.ChildBlock?.Kind != "SubEvent")
            {
                throw new InvalidOperationException("Battlefield unit status smoke fixed comment block is not a SubEvent child block.");
            }

            var childCommands = block.ChildBlock.Commands;
            if (childCommands.Count == 0 || childCommands[^1].CommandId != 0x00 || !childCommands[^1].EndsSubEventBlock)
            {
                throw new InvalidOperationException("Battlefield unit status smoke fixed comment block does not end with 00.");
            }

            var logical = FlattenSmokeStatusCommands(new[] { block });
            if (expectEquipment &&
                !logical.Any(x => x.CommandId == 0x48 && x.Parameters.Count > 0 && x.Parameters[0].IntValue == personId))
            {
                throw new InvalidOperationException("Battlefield unit status smoke fixed comment block does not contain expected 48.");
            }

            if (expectRuntime &&
                !logical.Any(x => x.CommandId is 0x52 or 0x38 && x.Parameters.Count > 0 && x.Parameters[0].IntValue == personId))
            {
                throw new InvalidOperationException("Battlefield unit status smoke fixed comment block does not contain expected runtime status command.");
            }

            return block;
        }

        throw new InvalidOperationException($"Battlefield unit status smoke did not find folded block '{expectedTitle}'.");
    }

    static int CountSmokeInternalInfoBlocks(LegacyScenarioDocument document, string title, int personId)
        => document.EnumerateCommands().Count(command =>
            command.CommandId == 0x02 &&
            command.ChildBlock != null &&
            (command.TextParameters.FirstOrDefault()?.Text.Trim() ?? string.Empty).Equals(title, StringComparison.Ordinal) &&
            FlattenSmokeStatusCommands(new[] { command })
                .Any(child => child.Parameters.Count > 0 && child.Parameters[0].IntValue == personId));

    static List<LegacyScenarioCommandNode> FlattenSmokeStatusCommands(IEnumerable<LegacyScenarioCommandNode> commands)
    {
        var result = new List<LegacyScenarioCommandNode>();
        foreach (var command in commands)
        {
            AddSmokeLogicalCommands(command, result);
        }

        return result;
    }

    static void AddSmokeLogicalCommands(LegacyScenarioCommandNode command, List<LegacyScenarioCommandNode> result)
    {
        if (command.CommandId != 0x01 && command.CommandId != 0x02 && command.CommandId != 0x00)
        {
            result.Add(command);
        }

        if (command.ChildBlock == null)
        {
            return;
        }

        foreach (var child in command.ChildBlock.Commands)
        {
            AddSmokeLogicalCommands(child, result);
        }
    }

    static void AssertSmokeParameter(LegacyScenarioCommandNode command, int index, int expected, string label)
    {
        var actual = index >= 0 && index < command.Parameters.Count ? command.Parameters[index].IntValue : int.MinValue;
        if (actual != expected)
        {
            throw new InvalidOperationException($"Smoke parameter mismatch {command.CommandIdHex} {label}: expected={expected}, actual={actual}.");
        }
    }

    static bool IsSmokeAbilityRecalcToggle(LegacyScenarioCommandNode command, int expectedValue)
        => command.CommandId == 0x77 &&
           command.Parameters.Count >= 5 &&
           command.Parameters[0].IntValue == 2 &&
           command.Parameters[1].IntValue == 4081 &&
           command.Parameters[2].IntValue == 2 &&
           command.Parameters[3].IntValue == 0 &&
           command.Parameters[4].IntValue == expectedValue;

    static bool IsSmokeStatusCommand(LegacyScenarioCommandNode command)
        => command.CommandId is 0x38 or 0x48 or 0x4E or 0x52 ||
           IsSmokeAbilityRecalcToggle(command, 0) ||
           IsSmokeAbilityRecalcToggle(command, 1);

    static bool IsSmokeDrawingStatusCommand(LegacyScenarioCommandNode command)
        => command.CommandId is 0x38 or 0x4E or 0x52 ||
           IsSmokeAbilityRecalcToggle(command, 0) ||
           IsSmokeAbilityRecalcToggle(command, 1);

    static bool IsSmokeStatusSequenceStart(
        IReadOnlyList<LegacyScenarioCommandNode> commandList,
        int index,
        bool includeEquipment)
    {
        if (index < 0 || index >= commandList.Count)
        {
            return false;
        }

        var command = commandList[index];
        if (includeEquipment ? IsSmokeStatusCommand(command) : IsSmokeDrawingStatusCommand(command))
        {
            return true;
        }

        if (command.CommandId == 0x01 &&
            index + 1 < commandList.Count &&
            IsSmokeInternalInfoStatusBlock(commandList[index + 1], includeEquipment))
        {
            return true;
        }

        return IsSmokeInternalInfoStatusBlock(command, includeEquipment);
    }

    static bool IsSmokeInternalInfoStatusBlock(LegacyScenarioCommandNode command, bool includeEquipment)
    {
        if (command.CommandId != 0x02 || command.ChildBlock == null)
        {
            return false;
        }

        var title = command.TextParameters.FirstOrDefault()?.Text.Trim() ?? string.Empty;
        return title == BattlefieldUnitStatusWriteService.FriendEquipmentStatusBlockTitle ||
               title == BattlefieldUnitStatusWriteService.EnemyEquipmentStatusBlockTitle ||
               title == BattlefieldUnitStatusWriteService.FriendRuntimeStatusBlockTitle ||
               title == BattlefieldUnitStatusWriteService.EnemyRuntimeStatusBlockTitle ||
               title == BattlefieldUnitStatusWriteService.CombinedStatusBlockTitle ||
               title == BattlefieldUnitStatusWriteService.RuntimeStatusBlockTitle ||
               (includeEquipment && title == BattlefieldUnitStatusWriteService.EquipmentStatusBlockTitle);
    }

    static int GetSmokeStatusSequenceLength(
        IReadOnlyList<LegacyScenarioCommandNode> commandList,
        int index,
        bool includeEquipment)
        => index >= 0 &&
           index + 1 < commandList.Count &&
           commandList[index].CommandId == 0x01 &&
           IsSmokeInternalInfoStatusBlock(commandList[index + 1], includeEquipment)
            ? 2
            : 1;

    readonly record struct BattlefieldStatusSmokeLocator(
        int SceneIndex,
        int SectionIndex,
        int CommandIndex,
        string OffsetHex,
        string CommandIdHex,
        int RecordIndex);

    readonly record struct BattlefieldStatusSmokeTarget(
        LegacyScenarioCommandNode Command,
        int RecordIndex,
        int PersonId);
    
    static void RunMapImageWriteSmoke(CczProject testProject)
    {
        var mapRoot = Path.Combine(testProject.GameRoot, "Map");
        var targetPath = FindFirstJpegMap(mapRoot)
            ?? throw new FileNotFoundException("地图底图写入烟测找不到测试副本 Map\\*.jpg。", mapRoot);
        var replacementRoot = Path.Combine(testProject.GameRoot, "_CCZModStudio_SmokeInputs");
        Directory.CreateDirectory(replacementRoot);
        var replacementPath = Path.Combine(replacementRoot, "Replacement_" + Path.GetFileName(targetPath));
    
        int width;
        int height;
        using (var sourceImage = System.Drawing.Image.FromFile(targetPath))
        {
            width = sourceImage.Width;
            height = sourceImage.Height;
            using var bitmap = new System.Drawing.Bitmap(width, height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(sourceImage, 0, 0, width, height);
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(220, 32, 64));
                graphics.FillRectangle(brush, 0, 0, Math.Min(12, width), Math.Min(12, height));
            }
    
            bitmap.Save(replacementPath, System.Drawing.Imaging.ImageFormat.Jpeg);
        }
    
        var result = new MapImageReplaceService().ReplaceMapImage(testProject, targetPath, replacementPath);
        var targetHash = WriteOperationReportService.ComputeSha256(File.ReadAllBytes(targetPath));
        var replacementHash = WriteOperationReportService.ComputeSha256(File.ReadAllBytes(replacementPath));
        if (!targetHash.Equals(replacementHash, StringComparison.OrdinalIgnoreCase) ||
            result.NewWidth != width ||
            result.NewHeight != height ||
            string.IsNullOrWhiteSpace(result.BackupPath) ||
            !File.Exists(result.BackupPath) ||
            string.IsNullOrWhiteSpace(result.ReportJsonPath) ||
            !File.Exists(result.ReportJsonPath) ||
            !File.ReadAllText(result.ReportJsonPath).Contains("\"OperationKind\": \"地图底图替换\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("地图底图替换烟测复读、备份或结构化报告验证失败。");
        }
    
        Console.WriteLine($"MAP_IMAGE_WRITE_SMOKE_OK map={Path.GetFileName(targetPath)} size={result.OldWidth}x{result.OldHeight}->{result.NewWidth}x{result.NewHeight} changed={result.ChangedBytesEstimate} backup={Path.GetFileName(result.BackupPath)}");
    }
    
    static void RunHexzmapWriteSmoke(CczProject sourceProject, CczProject testProject)
    {
        var terrainNameLookup = HexzmapProbeReader.BuildTerrainNameLookup(new MaterialLibraryIndexer().Index(sourceProject));
        var reader = new HexzmapProbeReader();
        var probe = reader.Read(testProject, terrainNameLookup);
        if (probe.Blocks.Count == 0)
        {
            throw new InvalidOperationException("Hexzmap 写入烟测没有读取到任何候选地形块。");
        }
    
        var block = probe.Blocks[0];
        var cells = reader.GetBlockCells(probe, block);
        var original0 = cells[0];
        var changed0 = original0 == 0 ? (byte)1 : (byte)0;
        var original1 = cells[1];
        var changed1 = original1 == 2 ? (byte)3 : (byte)2;
        cells[0] = changed0;
        cells[1] = changed1;
    
        HexzmapSaveResult save;
        try
        {
            save = new HexzmapEditorService().SaveBlock(testProject, probe, block, cells, terrainNameLookup);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("6.5/6.6x", StringComparison.Ordinal) &&
                                                   ex.Message.Contains("已拒绝写入", StringComparison.Ordinal))
        {
            Console.WriteLine($"HEXZMAP_WRITE_SMOKE_SKIPPED guard={ex.Message}");
            return;
        }
        var verifyProbe = reader.Read(testProject, terrainNameLookup);
        var verifyBlock = verifyProbe.Blocks.First(x => x.Index == block.Index);
        var verifyCells = reader.GetBlockCells(verifyProbe, verifyBlock);
        if (verifyCells[0] != changed0 ||
            verifyCells[1] != changed1 ||
            save.ChangedCells != 2 ||
            string.IsNullOrWhiteSpace(save.BackupPath) ||
            !File.Exists(save.BackupPath) ||
            string.IsNullOrWhiteSpace(save.ReportJsonPath) ||
            !File.Exists(save.ReportJsonPath) ||
            !File.ReadAllText(save.ReportJsonPath).Contains("Hexzmap地形格写入", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Hexzmap 地形写入烟测失败：expected={changed0}/{changed1}, actual={verifyCells[0]}/{verifyCells[1]}, changed={save.ChangedCells}");
        }
    
        Console.WriteLine($"HEXZMAP_WRITE_SMOKE_OK {block.MapId} cell0={original0:X2}->{changed0:X2} cell1={original1:X2}->{changed1:X2} changed={save.ChangedCells} backup={Path.GetFileName(save.BackupPath)}");
    }
    
    static void RunMapWorkbenchSmoke(CczProject sourceProject, CczProject testProject)
    {
        var materialRoot = MaterialLibraryIndexer.ResolveMaterialLibraryRoot(sourceProject)
            ?? throw new DirectoryNotFoundException("Map workbench smoke requires a material library.");
        var materials = new MaterialLibraryIndexer().IndexExplicitRoot(materialRoot);
        var material = materials.FirstOrDefault()
            ?? throw new InvalidOperationException("Map workbench smoke could not find any material image.");
    
        var resources = new MapResourceIndexer().Index(testProject);
        var mapItem = resources
            .Where(x => x.Category == "地图图片" && x.GridWidth > 0 && x.GridHeight > 0)
            .OrderBy(x => x.Id)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Map workbench smoke could not find a grid-aligned map image.");
    
        var terrainLookup = HexzmapProbeReader.BuildTerrainNameLookup(materials);
        var hexReader = new HexzmapProbeReader();
        var probe = hexReader.Read(testProject, terrainLookup);
        var mapId = Path.GetFileNameWithoutExtension(mapItem.Name);
        if (mapId.Length > 1 && (mapId[0] == 'M' || mapId[0] == 'm') &&
            int.TryParse(mapId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapNumber))
        {
            mapId = $"M{mapNumber:D3}";
        }
    
        var block = probe.Blocks.FirstOrDefault(x =>
            x.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase) &&
            x.Width == mapItem.GridWidth &&
            x.Height == mapItem.GridHeight &&
            x.BytesRead == mapItem.GridCellCount &&
            x.CanEdit)
            ?? throw new InvalidOperationException($"Map workbench smoke could not find a matching Hexzmap block for {mapItem.Name}.");
    
        var draftService = new MapDraftService();
        var composePublishService = new MapCanvasPublishService();
        var draft = draftService.CreateDraftFromMap(testProject, mapItem, materialRoot);
        draft.TerrainCells = hexReader.GetBlockCells(probe, block);
        draft.MapCellOverrides.Add(new MapCellOverride
        {
            Index = 0,
            MaterialRelativePath = MapDraftService.GetMaterialRelativePath(materialRoot, material.FilePath),
            MaterialCategory = material.Category,
            DisplayName = material.FileName
        });
        if (draft.TerrainCells.Length < 2)
        {
            throw new InvalidOperationException("Map workbench smoke requires at least two terrain cells.");
        }
    
        var terrain0 = draft.TerrainCells[0];
        var terrain1 = draft.TerrainCells[1];
        draft.TerrainCells[0] = terrain0 == 7 ? (byte)8 : (byte)7;
        draft.TerrainCells[1] = terrain1 == 9 ? (byte)10 : (byte)9;
    
        draftService.SaveDraft(testProject, draft);
        var reloaded = draftService.LoadDraft(testProject, draft.DraftId);
        if (reloaded.GridWidth != draft.GridWidth ||
            reloaded.GridHeight != draft.GridHeight ||
            reloaded.MapCellOverrides.Count != 1 ||
            !reloaded.TerrainCells.SequenceEqual(draft.TerrainCells))
        {
            throw new InvalidOperationException("Map workbench draft save/reload smoke failed.");
        }
    
        var settings = new MapWorkbenchSettings
        {
            LastDraftId = draft.DraftId,
            LastBoundMapId = draft.BoundMapId,
            LastMaterialRoot = materialRoot
        };
        draftService.SaveSettings(testProject, settings);
        var reloadedSettings = draftService.LoadSettings(testProject);
        if (reloadedSettings.LastDraftId != draft.DraftId ||
            !reloadedSettings.LastMaterialRoot.Equals(materialRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Map workbench settings save/reload smoke failed.");
        }
    
        var exportPath = Path.Combine(testProject.WorkspaceRoot, "CCZModStudio_Exports", "MapWorkbenchSmoke", $"{Path.GetFileNameWithoutExtension(mapItem.Name)}_workbench.jpg");
        composePublishService.ExportJpeg(reloaded, exportPath);
        using (var exported = System.Drawing.Image.FromFile(exportPath))
        {
            var expectedWidth = draft.GridWidth * MapResourceItem.MapTilePixelSize;
            var expectedHeight = draft.GridHeight * MapResourceItem.MapTilePixelSize;
            if (exported.Width != expectedWidth || exported.Height != expectedHeight)
            {
                throw new InvalidOperationException($"Map workbench export dimensions failed: {exported.Width}x{exported.Height}, expected {expectedWidth}x{expectedHeight}.");
            }
        }
    
        var mapPublish = composePublishService.PublishToMapImage(testProject, reloaded, mapItem);
        if (string.IsNullOrWhiteSpace(mapPublish.BackupPath) ||
            !File.Exists(mapPublish.BackupPath) ||
            string.IsNullOrWhiteSpace(mapPublish.ReportJsonPath) ||
            !File.Exists(mapPublish.ReportJsonPath) ||
            !File.ReadAllText(mapPublish.ReportJsonPath).Contains("地图工作台底图发布", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Map workbench map publish backup/report smoke failed.");
        }
        using (var mapVerify = System.Drawing.Image.FromFile(mapItem.Path))
        {
            if (mapVerify.Width != reloaded.PixelWidth || mapVerify.Height != reloaded.PixelHeight)
            {
                throw new InvalidOperationException("Map workbench map publish reread dimension smoke failed.");
            }
        }
    
        HexzmapSaveResult terrainSave;
        try
        {
            terrainSave = new HexzmapEditorService().SaveBlock(testProject, probe, block, reloaded.TerrainCells, terrainLookup);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("6.5/6.6x", StringComparison.Ordinal) &&
                                                   ex.Message.Contains("已拒绝写入", StringComparison.Ordinal))
        {
            Console.WriteLine($"MAP_WORKBENCH_TERRAIN_SMOKE_SKIPPED map={mapItem.Name} export={Path.GetFileName(exportPath)} mapBackup={Path.GetFileName(mapPublish.BackupPath)} guard={ex.Message}");
            return;
        }
        var verifyProbe = hexReader.Read(testProject, terrainLookup);
        var verifyBlock = verifyProbe.Blocks.First(x => x.Index == block.Index);
        var verifyCells = hexReader.GetBlockCells(verifyProbe, verifyBlock);
        if (!verifyCells.SequenceEqual(reloaded.TerrainCells) ||
            string.IsNullOrWhiteSpace(terrainSave.BackupPath) ||
            !File.Exists(terrainSave.BackupPath) ||
            string.IsNullOrWhiteSpace(terrainSave.ReportJsonPath) ||
            !File.Exists(terrainSave.ReportJsonPath))
        {
            throw new InvalidOperationException("Map workbench terrain publish reread, backup, or report smoke failed.");
        }
    
        Console.WriteLine($"MAP_WORKBENCH_SMOKE_OK map={mapItem.Name} grid={draft.GridWidth}x{draft.GridHeight} material={material.Category}/{material.FileName} export={Path.GetFileName(exportPath)} mapBackup={Path.GetFileName(mapPublish.BackupPath)} terrainChanged={terrainSave.ChangedCells}");
    }
    
    static string BuildBattlefieldCommandSignature(BattlefieldEditorDocument document)
        => string.Join("|", document.CommandCandidates.Take(20).Select(command =>
            $"{command.SceneIndex}:{command.SectionIndex}:{command.CommandIndex}:{command.OffsetHex}:{command.CommandIdHex}:{command.CommandName}"));
    
    static string BuildBattlefieldUnitSignature(BattlefieldEditorDocument document)
        => string.Join("|", document.UnitCandidates.Take(30).Select(unit =>
            $"{unit.TargetKey}:{unit.Category}:{unit.SourceCommand}:{unit.PersonHint}:{unit.CoordinateHint}:{unit.AiHint}"));
    
    static int CountColorfulPixels(System.Drawing.Bitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A == 0) continue;
                var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                if (max - min >= 16) count++;
            }
        }
    
        return count;
    }
    
    static int CountTransparentPixels(System.Drawing.Bitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0)
                {
                    count++;
                }
            }
        }
    
        return count;
    }
    
    static bool AreHorizontalMirrors(System.Drawing.Bitmap left, System.Drawing.Bitmap right)
    {
        if (left.Width != right.Width || left.Height != right.Height)
        {
            return false;
        }
    
        for (var y = 0; y < left.Height; y++)
        {
            for (var x = 0; x < left.Width; x++)
            {
                if (left.GetPixel(x, y).ToArgb() != right.GetPixel(left.Width - 1 - x, y).ToArgb())
                {
                    return false;
                }
            }
        }
    
        return true;
    }
}
