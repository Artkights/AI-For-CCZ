using CCZModStudio.Core;
using CCZModStudio.Models;
using CCZModStudio;

internal partial class Program
{
    static void RunCmSettingsSmoke()
    {
        using var temp = new TemporarySmokeDirectory("CmSettings");
        var gameRoot = Path.Combine(temp.Path, "Game");
        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(Path.Combine(gameRoot, "_CCZModStudio_TestCopy.txt"), "CM settings smoke.");

        var exePath = Path.Combine(gameRoot, "Ekd5.exe");
        var exeBytes = new byte[0x90000];
        exeBytes[0x3B8E4] = 3;
        exeBytes[0x1F53E] = 5;
        exeBytes[0x1F56D] = 8;
        exeBytes[0x207A4] = 0x74;
        exeBytes[0x2CD9] = 0xEB;
        exeBytes[0x2086C] = 3;
        exeBytes[0x20877] = 0xF8;
        exeBytes[0x3EA17] = 0x12;
        exeBytes[0x3EA43] = 0x12;
        exeBytes[0x3EA6F] = 0x12;
        exeBytes[0x3EA9B] = 0x22;
        exeBytes[0x3EAC7] = 0x22;
        exeBytes[0x234D1] = 0xC0;
        exeBytes[0x234CE] = 0x02;
        exeBytes[0x234D0] = 0x20;
        exeBytes[0x234CF] = 0x0C;
        exeBytes[0x1FECC] = 0x03;
        exeBytes[0x1FEE9] = 0x08;
        File.WriteAllBytes(exePath, exeBytes);

        var hexTablePath = Path.Combine(temp.Path, "HexTable.xml");
        File.WriteAllText(hexTablePath, "<Root />");
        var project = new CczProject
        {
            WorkspaceRoot = temp.Path,
            GameRoot = gameRoot,
            HexTableXmlPath = hexTablePath
        };

        var service = new CmSettingsService();
        var document = service.Load(project);
        if (document.Groups.Sum(group => group.Items.Count) != 28 ||
            document.TerrainStrategyRows.Count != 30 ||
            document.Groups.Any(group => group.GroupKey == "equipment-type"))
        {
            throw new InvalidOperationException("CM settings did not load 28 fields and 30 terrain rows without equipment type entries.");
        }

        var growth = document.Groups.Single(group => group.GroupKey == "growth");
        if (growth.Items.All(item => item.DisplayName != "杀敌加能力，五维上升1%需求"))
        {
            throw new InvalidOperationException("CM settings growth display names were not bound.");
        }

        var equipmentExp = document.Groups.Single(group => group.GroupKey == "equipment-exp");
        if (equipmentExp.Items.Count != 10 ||
            equipmentExp.Items.All(item => item.Key != "treasure-mutation-level") ||
            equipmentExp.Items.All(item => item.Key != "strategy-block-weapon-exp"))
        {
            throw new InvalidOperationException("CM settings equipment-exp group did not include the expanded 10 entries.");
        }
        AssertCmSettingText(equipmentExp, "strategy-block-weapon-exp", "3");

        var abnormal = document.Groups.Single(group => group.GroupKey == "abnormal-state");
        AssertCmSettingText(abnormal, "abnormal-ability-attack", "12");
        AssertCmSettingText(abnormal, "abnormal-ability-defense", "12");
        AssertCmSettingText(abnormal, "abnormal-ability-spirit", "12");
        AssertCmSettingText(abnormal, "abnormal-ability-agility", "22");
        AssertCmSettingText(abnormal, "abnormal-ability-morale", "22");
        AssertCmSettingText(abnormal, "abnormal-turn-poison", "3");
        AssertCmSettingText(abnormal, "abnormal-turn-paralysis", "2");
        AssertCmSettingText(abnormal, "abnormal-turn-confusion", "2");
        AssertCmSettingText(abnormal, "abnormal-turn-seal", "3");

        var preview = service.Preview(project, new CmSettingsUpdate
        {
            Values =
            {
                ["kill-ability-five-dim-demand"] = "9",
                ["treasure-mutation-level"] = "10",
                ["weapon-strategy-exp"] = "true",
                ["abnormal-ability-attack"] = "13",
                ["abnormal-turn-poison"] = "2",
                ["abnormal-turn-paralysis"] = "3",
                ["abnormal-turn-confusion"] = "1",
                ["abnormal-turn-seal"] = "2",
                ["strategy-block-weapon-exp"] = "5"
            },
            TerrainStrategy =
            {
                [0] = new CmTerrainStrategyUpdate { Fire = false, Water = true, Wind = true, Earth = false },
                [0x1D] = new CmTerrainStrategyUpdate { Fire = true, Water = false, Wind = false, Earth = true }
            }
        });
        if (preview.Count != 11)
        {
            throw new InvalidOperationException("CM settings preview should report eleven changes.");
        }

        var save = service.Save(project, new CmSettingsUpdate
        {
            Values =
            {
                ["kill-ability-five-dim-demand"] = "9",
                ["treasure-mutation-level"] = "10",
                ["weapon-strategy-exp"] = "true",
                ["abnormal-ability-attack"] = "13",
                ["abnormal-turn-poison"] = "2",
                ["abnormal-turn-paralysis"] = "3",
                ["abnormal-turn-confusion"] = "1",
                ["abnormal-turn-seal"] = "2",
                ["strategy-block-weapon-exp"] = "5"
            },
            TerrainStrategy =
            {
                [0] = new CmTerrainStrategyUpdate { Fire = false, Water = true, Wind = true, Earth = false },
                [0x1D] = new CmTerrainStrategyUpdate { Fire = true, Water = false, Wind = false, Earth = true }
            }
        });

        var written = File.ReadAllBytes(exePath);
        if (written[0x3B8E4] != 9 ||
            written[0x1F53E] != 10 ||
            written[0x207A4] != 0xEB ||
            written[0x3EA17] != 0x13 ||
            written[0x234D1] != 0x80 ||
            written[0x234CE] != 0x03 ||
            written[0x234D0] != 0x10 ||
            written[0x234CF] != 0x08 ||
            written[0x2086C] != 5 ||
            written[0x20877] != 0xF8 ||
            written[0x1FECC] != 0x06 ||
            written[0x1FEE9] != 0x09)
        {
            throw new InvalidOperationException("CM settings write/reread bytes did not match expected values.");
        }

        foreach (var invalidTurnValue in new[] { "4", "255", "-1", "abc" })
        {
            var rejected = false;
            try
            {
                service.Preview(project, new CmSettingsUpdate { Values = { ["abnormal-turn-poison"] = invalidTurnValue } });
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            if (!rejected)
            {
                throw new InvalidOperationException("CM settings should reject abnormal turn value: " + invalidTurnValue);
            }
        }

        if (save.ChangedFieldCount != 11 ||
            save.ChangedBytes != 11 ||
            string.IsNullOrWhiteSpace(save.BackupPath) ||
            string.IsNullOrWhiteSpace(save.ReportJsonPath) ||
            !File.Exists(save.BackupPath) ||
            !File.Exists(save.ReportJsonPath))
        {
            throw new InvalidOperationException("CM settings save result did not include backup/report metadata.");
        }

        using var form = new MainForm();
        if (FindTabPage(form, "CM设定") != null)
        {
            throw new InvalidOperationException("MainForm should not expose the old CM设定 tab.");
        }

        var globalTab = FindTabPage(form, "全局设定")
            ?? throw new InvalidOperationException("MainForm did not expose 全局设定 tab.");
        var subTabs = globalTab.Controls.OfType<TableLayoutPanel>()
            .SelectMany(layout => layout.Controls.OfType<TabControl>())
            .FirstOrDefault()
            ?? throw new InvalidOperationException("全局设定 tab did not contain grouped sub tabs.");
        var expectedTabs = new[] { "全局参数", "游戏标题", "成长", "装备经验", "战斗公式", "异常状态", "地形策略" };
        foreach (var tabName in expectedTabs)
        {
            if (subTabs.TabPages.Cast<TabPage>().All(page => page.Text != tabName))
            {
                throw new InvalidOperationException("全局设定 tab missing group: " + tabName);
            }
        }

        var globalNumericPage = subTabs.TabPages.Cast<TabPage>().First(page => page.Text == "全局参数");
        var globalNumericGrid = EnumerateControls<DataGridView>(globalNumericPage).SingleOrDefault()
            ?? throw new InvalidOperationException("全局参数页缺少表格。");
        var globalNumericHeaders = globalNumericGrid.Columns.Cast<DataGridViewColumn>()
            .Select(column => column.HeaderText)
            .ToArray();
        if (!globalNumericHeaders.SequenceEqual(new[] { "名称", "当前值", "新值" }))
        {
            throw new InvalidOperationException("全局参数页只能显示 名称/当前值/新值 三列，实际：" + string.Join(",", globalNumericHeaders));
        }

        var roleTab = FindTabPage(form, "角色设定")
            ?? throw new InvalidOperationException("MainForm did not expose 角色设定 tab.");
        var roleVisibleText = string.Join("|", EnumerateControlTexts(roleTab));
        if (roleVisibleText.Contains("全局设定", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("角色设定 toolbar should not expose 全局设定.");
        }

        var allVisibleText = string.Join("|", EnumerateControlTexts(globalTab));
        foreach (var forbidden in new[] { "UE Offset", "ManualConfirmed", "NeedsManualReview", "默认长度", "证据", "FixedGbkText", "CMF", "CMF候选", "字段地址", "兵种名", "职业名", "地址", "来源" })
        {
            if (allVisibleText.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("全局设定 UI exposed diagnostic text: " + forbidden);
            }
        }

        if (subTabs.TabPages.Cast<TabPage>().Any(page => page.Text == "装备类型"))
        {
            throw new InvalidOperationException("全局设定不应暴露装备类型页签。");
        }

        Console.WriteLine($"CM_SETTINGS_SMOKE_OK fields=28 terrain=30 changed={save.ChangedFieldCount} bytes={save.ChangedBytes}");
    }

    static void RunCmSettings66Smoke()
    {
        var baselineExe = FindStar66BaselineExe();
        using var temp = new TemporarySmokeDirectory("CmSettings66");
        var gameRoot = Path.Combine(temp.Path, "Game");
        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(Path.Combine(gameRoot, "_CCZModStudio_TestCopy.txt"), "CM settings 6.6 smoke.");

        var exePath = Path.Combine(gameRoot, "Ekd5.exe");
        File.Copy(baselineExe, exePath);

        var hexTablePath = Path.Combine(temp.Path, "HexTable.xml");
        File.WriteAllText(hexTablePath, "<Root />");
        var project = new CczProject
        {
            WorkspaceRoot = temp.Path,
            GameRoot = gameRoot,
            HexTableXmlPath = hexTablePath
        };

        var service = new CmSettingsService();
        var document = service.Load(project);
        if (document.Groups.Sum(group => group.Items.Count) != 25 ||
            document.TerrainStrategyRows.Count != 0)
        {
            throw new InvalidOperationException($"Star6.6 CM settings should load 25 fields and no terrain rows, got fields={document.Groups.Sum(group => group.Items.Count)} terrain={document.TerrainStrategyRows.Count}.");
        }

        var growth = document.Groups.Single(group => group.GroupKey == "growth");
        if (growth.Items.Count != 3 ||
            growth.Items.Any(item => item.Key.Equals("kill-ability-hp-mp-demand", StringComparison.OrdinalIgnoreCase)) ||
            growth.Items.All(item => item.Key != "kill-ability-hp-demand") ||
            growth.Items.All(item => item.Key != "kill-ability-mp-demand"))
        {
            throw new InvalidOperationException("Star6.6 growth fields should expose HP and MP separately, without the 6.5 HP/MP combined field.");
        }

        AssertCmSettingText(growth, "kill-ability-five-dim-demand", "30");
        AssertCmSettingText(growth, "kill-ability-hp-demand", "15");
        AssertCmSettingText(growth, "kill-ability-mp-demand", "15");

        var equipmentExp = document.Groups.Single(group => group.GroupKey == "equipment-exp");
        if (equipmentExp.Items.Count != 10)
        {
            throw new InvalidOperationException("Star6.6 equipment-exp group should expose 10 CM fields.");
        }

        AssertCmSettingText(equipmentExp, "treasure-mutation-level", "7");
        AssertCmSettingText(equipmentExp, "treasure-leap-level", "10");
        AssertCmSettingText(equipmentExp, "strategy-block-weapon-exp", "4");

        var battle = document.Groups.Single(group => group.GroupKey == "battle-formula");
        if (battle.Items.Count != 3 ||
            battle.Items.Any(item => item.Key.Equals("floating-damage", StringComparison.OrdinalIgnoreCase)) ||
            battle.Items.Any(item => item.Key.Equals("side-attack-multiplier", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Star6.6 battle-formula group should only expose the provided 6.6 fields.");
        }

        var abnormal = document.Groups.Single(group => group.GroupKey == "abnormal-state");
        AssertCmSettingText(abnormal, "abnormal-ability-attack", "22");
        AssertCmSettingText(abnormal, "abnormal-ability-defense", "22");
        AssertCmSettingText(abnormal, "abnormal-ability-spirit", "22");
        AssertCmSettingText(abnormal, "abnormal-ability-agility", "22");
        AssertCmSettingText(abnormal, "abnormal-ability-morale", "22");
        AssertCmSettingText(abnormal, "abnormal-turn-poison", "3");
        AssertCmSettingText(abnormal, "abnormal-turn-paralysis", "1");
        AssertCmSettingText(abnormal, "abnormal-turn-confusion", "1");
        AssertCmSettingText(abnormal, "abnormal-turn-seal", "2");

        var rejectedTerrain = false;
        try
        {
            service.Preview(project, new CmSettingsUpdate
            {
                TerrainStrategy =
                {
                    [0] = new CmTerrainStrategyUpdate { Fire = true }
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("未提供地形策略地址", StringComparison.OrdinalIgnoreCase))
        {
            rejectedTerrain = true;
        }

        if (!rejectedTerrain)
        {
            throw new InvalidOperationException("Star6.6 CM settings should reject terrain writes because the 6.6 seed has no terrain table.");
        }

        var preview = service.Preview(project, new CmSettingsUpdate
        {
            Values =
            {
                ["kill-ability-mp-demand"] = "16",
                ["strategy-block-weapon-exp"] = "5",
                ["abnormal-turn-poison"] = "2"
            }
        });
        if (preview.Count != 3)
        {
            throw new InvalidOperationException("Star6.6 CM settings preview should report three changes.");
        }

        var save = service.Save(project, new CmSettingsUpdate
        {
            Values =
            {
                ["kill-ability-mp-demand"] = "16",
                ["strategy-block-weapon-exp"] = "5",
                ["abnormal-turn-poison"] = "2"
            }
        });

        var written = File.ReadAllBytes(exePath);
        if (written[0x66A4] != 0x10 ||
            written[0x2086C] != 0x05 ||
            written[0x20877] != File.ReadAllBytes(baselineExe)[0x20877] ||
            written[0x234D1] != 0x80 ||
            written[0x1F53E] != File.ReadAllBytes(baselineExe)[0x1F53E] ||
            written[0x1F56D] != File.ReadAllBytes(baselineExe)[0x1F56D])
        {
            throw new InvalidOperationException("Star6.6 CM settings write/reread bytes did not match expected 6.6 offsets.");
        }

        var reloaded = service.Load(project);
        AssertCmSettingText(reloaded.Groups.Single(group => group.GroupKey == "growth"), "kill-ability-mp-demand", "16");
        AssertCmSettingText(reloaded.Groups.Single(group => group.GroupKey == "equipment-exp"), "strategy-block-weapon-exp", "5");
        AssertCmSettingText(reloaded.Groups.Single(group => group.GroupKey == "abnormal-state"), "abnormal-turn-poison", "2");

        foreach (var invalidTurnValue in new[] { "4", "255", "-1", "abc" })
        {
            var rejected = false;
            try
            {
                service.Preview(project, new CmSettingsUpdate { Values = { ["abnormal-turn-poison"] = invalidTurnValue } });
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            if (!rejected)
            {
                throw new InvalidOperationException("Star6.6 CM settings should reject abnormal turn value: " + invalidTurnValue);
            }
        }

        if (save.ChangedFieldCount != 3 ||
            save.ChangedBytes != 3 ||
            string.IsNullOrWhiteSpace(save.BackupPath) ||
            string.IsNullOrWhiteSpace(save.ReportJsonPath) ||
            !File.Exists(save.BackupPath) ||
            !File.Exists(save.ReportJsonPath))
        {
            throw new InvalidOperationException("Star6.6 CM settings save result did not include backup/report metadata.");
        }

        Console.WriteLine($"CM_SETTINGS_66_SMOKE_OK fields=25 terrain=0 changed={save.ChangedFieldCount} bytes={save.ChangedBytes}");
    }

    private static string FindStar66BaselineExe()
    {
        var direct = Path.GetFullPath(Path.Combine(
            Environment.CurrentDirectory,
            "基底",
            "新改曹操傳6.6修正版",
            "Ekd5.exe"));
        if (File.Exists(direct)) return direct;

        var baselineRoot = Path.Combine(Environment.CurrentDirectory, "基底");
        if (Directory.Exists(baselineRoot))
        {
            var discovered = Directory
                .EnumerateFiles(baselineRoot, "Ekd5.exe", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains("6.6", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(discovered)) return discovered;
        }

        throw new FileNotFoundException("Star6.6 baseline Ekd5.exe was not found for CM settings smoke.", direct);
    }

    private static void AssertCmSettingText(CmSettingGroup group, string key, string expected)
    {
        var item = group.Items.SingleOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("CM setting not found: " + key);
        if (!item.CurrentValueText.Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"CM setting {key} expected {expected}, got {item.CurrentValueText}.");
        }
    }

    private static TabPage? FindTabPage(Control root, string text)
    {
        foreach (Control control in root.Controls)
        {
            if (control is TabControl tabs)
            {
                foreach (TabPage page in tabs.TabPages)
                {
                    if (page.Text.Equals(text, StringComparison.Ordinal)) return page;
                    var found = FindTabPage(page, text);
                    if (found != null) return found;
                }
            }

            var nested = FindTabPage(control, text);
            if (nested != null) return nested;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateControlTexts(Control root)
    {
        if (!string.IsNullOrWhiteSpace(root.Text)) yield return root.Text;
        foreach (Control child in root.Controls)
        {
            foreach (var text in EnumerateControlTexts(child))
            {
                yield return text;
            }
        }
    }

    private static IEnumerable<T> EnumerateControls<T>(Control root) where T : Control
    {
        if (root is T typed) yield return typed;
        foreach (Control child in root.Controls)
        {
            foreach (var item in EnumerateControls<T>(child))
            {
                yield return item;
            }
        }
    }
}
