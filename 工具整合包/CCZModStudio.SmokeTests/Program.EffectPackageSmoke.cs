using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

internal partial class Program
{
    static void RunJobStrategyWriteSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "JobStrategyWriteSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("兵种策略写入烟测缺少核心文件。", source);
            }
    
            File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
        }
    
        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=job strategy write smoke\r\n");
    
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        RunJobStrategyWriteSmokeCore(testProject, tables);
        Console.WriteLine($"JOB_STRATEGY_WRITE_SMOKE_ROOT {smokeRoot}");
    }
    
    static void RunEffectPackageSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "EffectPackageSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("特效包烟测缺少核心文件。", source);
            }
    
            File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
        }
    
        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=effect package smoke\r\n");
    
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var service = new EffectPackageService();
        var reader = new HexTableReader();
    
        VerifyJobEffectNamesAreVisible(project, tables, reader);
    
        var templates = service.ListTemplates();
        if (templates.Count < 4 ||
            templates.All(x => x.TemplateId != "config.item.binding") ||
            templates.All(x => x.TemplateId != "patch.inline_stub.draft"))
        {
            throw new InvalidOperationException("特效模板清单缺少预期模板。");
        }
    
        var schema = service.GetSchemaResource();
        if (schema == null || !schema.ToString()!.Contains("EffectPackage", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("EffectPackage schema resource 未生成。");
        }
        var schemaJson = JsonSerializer.Serialize(schema);
        if (!schemaJson.Contains("expectedOldBytesHex", StringComparison.Ordinal) ||
            !schemaJson.Contains("scan_exe_code_caves", StringComparison.Ordinal) ||
            !schemaJson.Contains("apply_assembly_patch", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("EffectPackage schema resource 缺少汇编补丁工作流字段。");
        }
    
        var itemPackage = service.BuildPackageFromTemplate("config.item.binding", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["effect_id"] = "42",
            ["name"] = "烟测宝物特效",
            ["description"] = "EffectPackage 宝物导入烟测",
            ["item_ids"] = "0,104",
            ["effect_value"] = "7",
            ["source_prompt"] = "烟测：把物品 0 和 104 绑定到特效 42。"
        });
        var itemPreview = service.Preview(testProject, tables, itemPackage, "import");
        if (!itemPreview.CanApply || itemPreview.Changes.Count < 6)
        {
            throw new InvalidOperationException($"宝物特效包预览失败：can={itemPreview.CanApply}, warnings={string.Join(';', itemPreview.Warnings)}");
        }
    
        var itemApply = service.Apply(testProject, tables, itemPackage, "import");
        VerifyEffectApplyResult(itemApply, expectedDomain: "item", minChanges: 5);
        var itemLowTable = tables.Single(t => t.TableName == "6.5-1 物品（0-103）");
        var itemHighTable = tables.Single(t => t.TableName == "6.5-2 物品（104-255）");
        var itemLowVerify = reader.Read(testProject, itemLowTable, tables);
        var itemHighVerify = reader.Read(testProject, itemHighTable, tables);
        var item0 = FindSmokeRowById(itemLowVerify.Data, 0);
        var item104 = FindSmokeRowById(itemHighVerify.Data, 104);
        if (Convert.ToInt32(item0["装备特效号"], CultureInfo.InvariantCulture) != 42 ||
            Convert.ToInt32(item0["装备特效号-效果值"], CultureInfo.InvariantCulture) != 7 ||
            Convert.ToInt32(item104["装备特效号"], CultureInfo.InvariantCulture) != 42 ||
            Convert.ToInt32(item104["装备特效号-效果值"], CultureInfo.InvariantCulture) != 7)
        {
            throw new InvalidOperationException("宝物特效包导入后复读物品绑定失败。");
        }
    
        var itemCatalogEntries = new ItemEffectCatalogService().Load(testProject);
        if (!itemCatalogEntries.Any(x => x.EffectId == 42 && x.Name.Contains("烟测宝物特效", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("宝物特效包未写入项目侧 UTF-8 JSON 目录。");
        }
    
        var exportedItemPackage = service.ExportPackage(testProject, tables, "item", 42);
        if (exportedItemPackage.EffectId != 42 || exportedItemPackage.Domain != "item")
        {
            throw new InvalidOperationException("宝物特效导出包内容不正确。");
        }
    
        var itemDeletePreview = service.Preview(testProject, tables, new EffectPackage
        {
            Domain = "item",
            EffectId = 42,
            Name = "烟测宝物特效"
        }, "delete");
        if (!itemDeletePreview.CanApply || itemDeletePreview.Changes.Count(change => change.Category == "ItemBinding") < 4)
        {
            throw new InvalidOperationException("宝物特效删除预览未自动发现并清理所有引用。");
        }
    
        var itemDelete = service.Apply(testProject, tables, new EffectPackage
        {
            Domain = "item",
            EffectId = 42,
            Name = "烟测宝物特效"
        }, "delete");
        VerifyEffectApplyResult(itemDelete, expectedDomain: "item", minChanges: 5);
        var itemLowDeleted = reader.Read(testProject, itemLowTable, tables);
        var itemHighDeleted = reader.Read(testProject, itemHighTable, tables);
        if (Convert.ToInt32(FindSmokeRowById(itemLowDeleted.Data, 0)["装备特效号"], CultureInfo.InvariantCulture) != 0 ||
            Convert.ToInt32(FindSmokeRowById(itemLowDeleted.Data, 0)["装备特效号-效果值"], CultureInfo.InvariantCulture) != 0 ||
            Convert.ToInt32(FindSmokeRowById(itemHighDeleted.Data, 104)["装备特效号"], CultureInfo.InvariantCulture) != 0 ||
            Convert.ToInt32(FindSmokeRowById(itemHighDeleted.Data, 104)["装备特效号-效果值"], CultureInfo.InvariantCulture) != 0)
        {
            throw new InvalidOperationException("宝物特效删除后仍存在物品引用。");
        }
    
        if (itemDelete.BackupPaths.Count < 2 || string.IsNullOrWhiteSpace(itemDelete.ManifestPath))
        {
            throw new InvalidOperationException("宝物特效删除未生成备份文件或 manifest。");
        }
    
        var jobPackage = service.BuildPackageFromTemplate("config.job.assignment", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["effect_id"] = "43",
            ["description"] = "EffectPackage 兵种特效说明烟测",
            ["person_ids"] = "0,1,2",
            ["job_id"] = "3",
            ["effect_value"] = "9"
        });
        var jobPreview = service.Preview(testProject, tables, jobPackage, "replace");
        if (!jobPreview.CanApply || jobPreview.Changes.Count < 6)
        {
            throw new InvalidOperationException($"兵种特效包预览失败：{string.Join(';', jobPreview.Warnings)}");
        }
    
        var jobApply = service.Apply(testProject, tables, jobPackage, "replace");
        VerifyEffectApplyResult(jobApply, expectedDomain: "job", minChanges: 6);
        var jobDescTable = tables.Single(t => t.TableName == "6.5-7-1 兵种特效说明");
        var jobAssignTable = tables.Single(t => t.TableName == "6.5-7-2 兵种特效分配");
        var jobDescVerify = reader.Read(testProject, jobDescTable, tables);
        var jobAssignVerify = reader.Read(testProject, jobAssignTable, tables);
        var jobDescRow = FindSmokeRowById(jobDescVerify.Data, 43);
        var jobAssignRow = FindSmokeRowById(jobAssignVerify.Data, 43);
        if (!(Convert.ToString(jobDescRow["介绍"], CultureInfo.InvariantCulture) ?? string.Empty).Contains("兵种特效说明烟测", StringComparison.Ordinal) ||
            Convert.ToInt32(jobAssignRow["1号武将"], CultureInfo.InvariantCulture) != 0 ||
            Convert.ToInt32(jobAssignRow["2号武将"], CultureInfo.InvariantCulture) != 1 ||
            Convert.ToInt32(jobAssignRow["3号武将"], CultureInfo.InvariantCulture) != 2 ||
            Convert.ToInt32(jobAssignRow["兵种"], CultureInfo.InvariantCulture) != 3 ||
            Convert.ToInt32(jobAssignRow["特效值"], CultureInfo.InvariantCulture) != 9)
        {
            throw new InvalidOperationException("兵种特效包写入后复读失败。");
        }
    
        var jobDelete = service.Apply(testProject, tables, new EffectPackage { Domain = "job", EffectId = 43 }, "delete");
        VerifyEffectApplyResult(jobDelete, expectedDomain: "job", minChanges: 6);
        var jobAssignDeleted = FindSmokeRowById(reader.Read(testProject, jobAssignTable, tables).Data, 43);
        if (Convert.ToInt32(jobAssignDeleted["1号武将"], CultureInfo.InvariantCulture) != 1024 ||
            Convert.ToInt32(jobAssignDeleted["2号武将"], CultureInfo.InvariantCulture) != 1024 ||
            Convert.ToInt32(jobAssignDeleted["3号武将"], CultureInfo.InvariantCulture) != 1024 ||
            Convert.ToInt32(jobAssignDeleted["兵种"], CultureInfo.InvariantCulture) != 255 ||
            Convert.ToInt32(jobAssignDeleted["特效值"], CultureInfo.InvariantCulture) != 0)
        {
            throw new InvalidOperationException("兵种特效删除未按固定槽位清空规则写入。");
        }
    
        var personalPackage = service.BuildPackageFromTemplate("config.personal.exclusive", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["effect_id"] = "44",
            ["slot_kind"] = "person_item_1",
            ["person_id"] = "0",
            ["item_ids"] = "1",
            ["effect_value"] = "5"
        });
        var personalTable = tables.Single(t => t.TableName == "6.5-7-3 人物专属、套装专属");
        var personalBefore = FindSmokeRowById(reader.Read(testProject, personalTable, tables).Data, 44);
        var personalBeforeSlot1Person = Convert.ToInt32(personalBefore["武将1"], CultureInfo.InvariantCulture);
        var personalBeforeSlot1Item = Convert.ToInt32(personalBefore["装备1"], CultureInfo.InvariantCulture);
        var beforeSlot2Person = Convert.ToInt32(personalBefore["武将2"], CultureInfo.InvariantCulture);
        var beforeSlot2Item = Convert.ToInt32(personalBefore["装备2"], CultureInfo.InvariantCulture);
        var beforeSlot2Value = Convert.ToInt32(personalBefore["特效值2"], CultureInfo.InvariantCulture);
        var missingPersonalPackage = service.BuildPackageFromTemplate("config.personal.exclusive", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["effect_id"] = "44",
            ["slot_kind"] = "person_item_1",
            ["person_id"] = "0",
            ["effect_value"] = "5"
        });
        var missingPersonalPreview = service.Preview(testProject, tables, missingPersonalPackage, "replace");
        if (missingPersonalPreview.CanApply ||
            missingPersonalPreview.Warnings.All(warning => !warning.Contains("ItemId", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("个人/套装特效包缺少必要装备字段时未拒绝写入。");
        }

        var missingEffectValuePackage = new EffectPackage
        {
            Domain = "personal",
            EffectId = 44,
            Bindings =
            {
                new EffectPackageBinding
                {
                    Kind = "person_item_1",
                    PersonId = 0,
                    ItemId = 1
                }
            }
        };
        var missingEffectValuePreview = service.Preview(testProject, tables, missingEffectValuePackage, "replace");
        if (missingEffectValuePreview.CanApply ||
            missingEffectValuePreview.Warnings.All(warning => !warning.Contains("effect_value", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("个人/套装特效包缺少特效值时未拒绝写入。");
        }

        var personalPreview = service.Preview(testProject, tables, personalPackage, "replace");
        if (!personalPreview.CanApply ||
            personalPreview.Changes.Count(change => change.Category == "PersonalExclusive") != 3 ||
            personalPreview.Changes.Any(change => change.Field is "武将2" or "装备2" or "特效值2"))
        {
            throw new InvalidOperationException($"个人/套装特效包预览失败：{string.Join(';', personalPreview.Warnings)}");
        }
    
        var personalApply = service.Apply(testProject, tables, personalPackage, "replace");
        VerifyEffectApplyResult(personalApply, expectedDomain: "personal", minChanges: 3);
        var personalRow = FindSmokeRowById(reader.Read(testProject, personalTable, tables).Data, 44);
        if (Convert.ToInt32(personalRow["武将1"], CultureInfo.InvariantCulture) != 0 ||
            Convert.ToInt32(personalRow["装备1"], CultureInfo.InvariantCulture) != 1 ||
            Convert.ToInt32(personalRow["特效值1"], CultureInfo.InvariantCulture) != 5 ||
            Convert.ToInt32(personalRow["武将2"], CultureInfo.InvariantCulture) != beforeSlot2Person ||
            Convert.ToInt32(personalRow["装备2"], CultureInfo.InvariantCulture) != beforeSlot2Item ||
            Convert.ToInt32(personalRow["特效值2"], CultureInfo.InvariantCulture) != beforeSlot2Value)
        {
            throw new InvalidOperationException("个人/套装特效包写入后复读失败，或误改了未指定槽位。");
        }

        var clearSlotPackage = new EffectPackage
        {
            Domain = "personal",
            EffectId = 44,
            EffectValue = 0,
            Bindings =
            {
                new EffectPackageBinding
                {
                    Kind = "person_item_1",
                    PersonId = 999,
                    ItemId = 88
                }
            }
        };
        var clearSlotPreview = service.Preview(testProject, tables, clearSlotPackage, "replace");
        if (!clearSlotPreview.CanApply ||
            clearSlotPreview.Changes.Count(change => change.Category == "PersonalExclusive") != 3 ||
            clearSlotPreview.Changes.Any(change => change.NewValue != "0"))
        {
            throw new InvalidOperationException("个人/套装特效值=0 未按旧扳手规则预览为清空当前槽。");
        }

        var clearSlotApply = service.Apply(testProject, tables, clearSlotPackage, "replace");
        VerifyEffectApplyResult(clearSlotApply, expectedDomain: "personal", minChanges: 3);
        var personalCleared = FindSmokeRowById(reader.Read(testProject, personalTable, tables).Data, 44);
        if (Convert.ToInt32(personalCleared["武将1"], CultureInfo.InvariantCulture) != 0 ||
            Convert.ToInt32(personalCleared["装备1"], CultureInfo.InvariantCulture) != 0 ||
            Convert.ToInt32(personalCleared["特效值1"], CultureInfo.InvariantCulture) != 0 ||
            Convert.ToInt32(personalCleared["武将2"], CultureInfo.InvariantCulture) != beforeSlot2Person ||
            Convert.ToInt32(personalCleared["装备2"], CultureInfo.InvariantCulture) != beforeSlot2Item ||
            Convert.ToInt32(personalCleared["特效值2"], CultureInfo.InvariantCulture) != beforeSlot2Value)
        {
            throw new InvalidOperationException("个人/套装特效值=0 写入未清空当前槽，或误改了未指定槽位。");
        }
    
        var personalDelete = service.Apply(testProject, tables, new EffectPackage { Domain = "personal", EffectId = 44 }, "delete");
        VerifyEffectApplyResult(personalDelete, expectedDomain: "personal", minChanges: 14);
        var personalDeleted = FindSmokeRowById(reader.Read(testProject, personalTable, tables).Data, 44);
        foreach (var column in new[] { "武将1", "装备1", "特效值1", "武将2", "装备2", "特效值2", "装备3-1", "装备3-2", "装备3-3", "特效值3", "装备4-1", "装备4-2", "装备4-3", "特效值4" })
        {
            if (Convert.ToInt32(personalDeleted[column], CultureInfo.InvariantCulture) != 0)
            {
                throw new InvalidOperationException("个人/套装特效删除未清空固定槽位：" + column);
            }
        }

        Console.WriteLine($"PERSONAL_EFFECT_WRITE_SMOKE_OK effect=44 beforeSlot1=({personalBeforeSlot1Person},{personalBeforeSlot1Item}) importManifest={Path.GetFileName(personalApply.ManifestPath)} clearManifest={Path.GetFileName(clearSlotApply.ManifestPath)} deleteManifest={Path.GetFileName(personalDelete.ManifestPath)}");
    
        var patchPackage = service.BuildPackageFromTemplate("patch.inline_stub.draft", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["effect_id"] = "45",
            ["personal_effect_id"] = "44",
            ["item_effect_id"] = "42",
            ["hook_point"] = "smoke-hook",
            ["code_cave"] = "smoke-cave"
        });
        if (patchPackage.Domain != "patch" ||
            !patchPackage.Metadata.TryGetValue("SafetyLevel", out var safety) ||
            safety != "draft_only")
        {
            throw new InvalidOperationException("补丁模板未生成 draft_only 特效包。");
        }
    
        var patchPreview = service.PreviewPatch(testProject, patchPackage);
        if (patchPreview.CanApply || patchPreview.Warnings.Count == 0)
        {
            throw new InvalidOperationException("空补丁包不应允许直接应用。");
        }
    
        Console.WriteLine($"EFFECT_PACKAGE_SMOKE_OK root={smokeRoot} itemManifest={Path.GetFileName(itemApply.ManifestPath)} jobManifest={Path.GetFileName(jobApply.ManifestPath)} personalManifest={Path.GetFileName(personalApply.ManifestPath)} templates={templates.Count}");
    }
}
