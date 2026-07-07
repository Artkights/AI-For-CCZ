using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;
using System.Text;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private TabPage BuildEffectInjectionPage()
    {
        var page = new TabPage("特效注入");
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        page.Controls.Add(tabs);

        var patchGrid = CreateEffectInjectionGrid();
        var moduleGrid = CreateEffectInjectionGrid();
        var hookGrid = CreateEffectInjectionGrid();
        tabs.TabPages.Add(BuildEffectInjectionDiscoveryPage(patchGrid, moduleGrid, hookGrid));

        var caveGrid = CreateEffectInjectionGrid();
        var caveInfoBox = CreateEffectInjectionInfoBox();
        tabs.TabPages.Add(BuildEffectInjectionCodeCavePage(caveGrid, caveInfoBox));

        var previewSummaryBox = CreateEffectInjectionInfoBox();
        var previewSegmentsGrid = CreateEffectInjectionGrid();
        var previewWarningsGrid = CreateEffectInjectionGrid();
        var assemblyControls = new EffectInjectionAssemblyControls();
        tabs.TabPages.Add(BuildEffectInjectionAssemblyPage(assemblyControls, previewSummaryBox, previewSegmentsGrid, previewWarningsGrid));

        var applyReportBox = CreateEffectInjectionInfoBox();
        tabs.TabPages.Add(BuildEffectInjectionApplyPage(applyReportBox));

        return page;
    }

    private TabPage BuildEffectInjectionDiscoveryPage(
        DataGridView patchGrid,
        DataGridView moduleGrid,
        DataGridView hookGrid)
    {
        var page = new TabPage("已注入识别");
        var layout = CreateEffectInjectionPageLayout();
        page.Controls.Add(layout);

        var scanButton = new Button { Text = "扫描已注入特效" };
        ConfigureToolbarButton(scanButton, 128);
        var showAllJumpsCheck = new CheckBox { Text = "显示全部跳转候选" };
        ConfigureToolbarCheckBox(showAllJumpsCheck);
        var summaryLabel = CreateToolbarLabel("尚未扫描", 12);
        summaryLabel.AutoSize = true;

        var toolbar = CreateToolbarStack(1);
        AddToolbarRow(toolbar, 0, scanButton, showAllJumpsCheck, summaryLabel);
        layout.Controls.Add(toolbar, 0, 0);

        var split = CreateResizableSplit(
            "EffectInjection.Discovery.PatchModuleSplit",
            Orientation.Vertical,
            desiredDistance: 720,
            desiredPanel1Min: 340,
            desiredPanel2Min: 340);
        layout.Controls.Add(split, 0, 1);

        split.Panel1.Controls.Add(CreateTitledPanel("已识别特技补丁", patchGrid, new Padding(0, 0, 0, 4)));
        var rightTabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        var modulePage = CreateControlTabPage("补丁模块结构", moduleGrid);
        var hookPage = CreateControlTabPage("高级跳转候选", hookGrid);
        rightTabs.TabPages.Add(modulePage);
        split.Panel2.Controls.Add(rightTabs);

        List<InjectedEffectCandidate> currentCandidates = [];
        InjectedEffectDiscoveryReport? currentReport = null;

        void UpdateModuleGrid()
        {
            if (patchGrid.CurrentRow == null ||
                !patchGrid.Columns.Contains("索引") ||
                patchGrid.CurrentRow.Cells["索引"].Value is not string indexText ||
                !int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
                index < 0 ||
                index >= currentCandidates.Count)
            {
                moduleGrid.DataSource = BuildInjectedEffectModuleTable([]);
                ConfigureEffectInjectionGrid(moduleGrid);
                return;
            }

            moduleGrid.DataSource = BuildInjectedEffectModuleTable(currentCandidates[index].Modules);
            ConfigureEffectInjectionGrid(moduleGrid);
        }

        void UpdateHookVisibility()
        {
            if (showAllJumpsCheck.Checked)
            {
                if (!rightTabs.TabPages.Contains(hookPage))
                {
                    rightTabs.TabPages.Add(hookPage);
                }

                hookGrid.DataSource = BuildInjectedEffectHookTable(currentReport?.HookCandidates ?? []);
                ConfigureEffectInjectionGrid(hookGrid);
            }
            else if (rightTabs.TabPages.Contains(hookPage))
            {
                rightTabs.TabPages.Remove(hookPage);
            }

            if (currentReport != null)
            {
                summaryLabel.Text = BuildDiscoverySummary(currentReport, showAllJumpsCheck.Checked);
            }
        }

        patchGrid.SelectionChanged += (_, _) => UpdateModuleGrid();
        showAllJumpsCheck.CheckedChanged += (_, _) => UpdateHookVisibility();

        scanButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project)) return;

            try
            {
                Cursor = Cursors.WaitCursor;
                var report = _injectedEffectDiscoveryService.Discover(project);
                currentReport = report;
                currentCandidates = report.Candidates.ToList();
                patchGrid.DataSource = BuildInjectedEffectPatchTable(currentCandidates);
                ConfigureEffectInjectionGrid(patchGrid);
                UpdateModuleGrid();
                UpdateHookVisibility();
                SetStatus(BuildDiscoverySummary(report, showAllJumpsCheck.Checked));
            }
            catch (Exception ex)
            {
                currentReport = null;
                currentCandidates = [];
                patchGrid.DataSource = null;
                moduleGrid.DataSource = null;
                hookGrid.DataSource = null;
                summaryLabel.Text = "扫描失败：" + ex.Message;
                MessageBox.Show(this, ex.Message, "扫描已注入特效失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        };

        return page;
    }

    private TabPage BuildEffectInjectionCodeCavePage(
        DataGridView caveGrid,
        TextBox infoBox)
    {
        var page = new TabPage("代码空洞");
        var layout = CreateEffectInjectionPageLayout();
        page.Controls.Add(layout);

        var minLengthInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 4096,
            Value = 8,
            Width = 64,
            Margin = new Padding(3)
        };
        var includeZeroCheck = new CheckBox { Text = "显示 00 填充" };
        ConfigureToolbarCheckBox(includeZeroCheck);
        var includeMixedCheck = new CheckBox { Text = "显示 mixed 填充" };
        ConfigureToolbarCheckBox(includeMixedCheck);
        var scanButton = new Button { Text = "扫描空洞" };
        ConfigureToolbarButton(scanButton, 96);

        var toolbar = CreateToolbarStack(1);
        AddToolbarRow(toolbar, 0,
            CreateToolbarLabel("最小长度", 0),
            minLengthInput,
            includeZeroCheck,
            includeMixedCheck,
            scanButton);
        layout.Controls.Add(toolbar, 0, 0);

        var split = CreateResizableSplit(
            "EffectInjection.CodeCaves.MainSplit",
            Orientation.Horizontal,
            desiredDistance: 420,
            desiredPanel1Min: 220,
            desiredPanel2Min: 120);
        layout.Controls.Add(split, 0, 1);
        split.Panel1.Controls.Add(CreateTitledPanel("空洞候选", caveGrid, new Padding(0, 0, 0, 4)));
        split.Panel2.Controls.Add(CreateTitledPanel("扫描说明", infoBox, new Padding(0, 4, 0, 4)));

        scanButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project)) return;

            try
            {
                Cursor = Cursors.WaitCursor;
                var scan = _exeCodeCaveScanner.Scan(
                    project,
                    "Ekd5.exe",
                    minimumLength: decimal.ToInt32(minLengthInput.Value),
                    includeZeroFill: includeZeroCheck.Checked,
                    includeMixedFill: includeMixedCheck.Checked);
                var allocations = _codeCaveRegistry.LoadExistingAllocations(project, "Ekd5.exe");
                caveGrid.DataSource = BuildCodeCaveTable(scan.Candidates, allocations);
                ConfigureEffectInjectionGrid(caveGrid);
                infoBox.Text = FormatCodeCaveReport(scan, allocations);
                SetStatus($"代码空洞扫描完成：候选 {scan.Candidates.Count}，manifest 占用 {allocations.Count}");
            }
            catch (Exception ex)
            {
                caveGrid.DataSource = null;
                infoBox.Text = ex.ToString();
                MessageBox.Show(this, ex.Message, "扫描代码空洞失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        };

        return page;
    }

    private TabPage BuildEffectInjectionAssemblyPage(
        EffectInjectionAssemblyControls controls,
        TextBox summaryBox,
        DataGridView segmentsGrid,
        DataGridView warningsGrid)
    {
        var page = new TabPage("汇编注入");
        var layout = CreateEffectInjectionPageLayout();
        page.Controls.Add(layout);

        controls.TemplateCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 150,
            Margin = new Padding(3)
        };
        controls.TemplateCombo.Items.AddRange(new object[] { "手写汇编", "条件增伤四模块模板", "条件减伤四模块模板" });
        controls.TemplateCombo.SelectedIndex = 0;
        controls.HookAddressBox = CreateEffectInjectionTextBox("00400000", 100);
        controls.OverwriteLengthInput = new NumericUpDown
        {
            Minimum = 5,
            Maximum = 64,
            Value = 5,
            Width = 64,
            Margin = new Padding(3)
        };
        controls.ExpectedOldBytesBox = CreateEffectInjectionTextBox(string.Empty, 220);
        controls.ReturnAddressBox = CreateEffectInjectionTextBox(string.Empty, 100);
        controls.RequiredBytesInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 4096,
            Value = 16,
            Width = 64,
            Margin = new Padding(3)
        };
        controls.PolicyCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120,
            Margin = new Padding(3)
        };
        controls.PolicyCombo.Items.AddRange(new object[] { "smallest-fit", "largest-fit" });
        controls.PolicyCombo.SelectedIndex = 0;
        controls.EffectIdInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 65535,
            Value = 0,
            Width = 72,
            Margin = new Padding(3)
        };
        controls.PersonalIdInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 65535,
            Value = 0xB0,
            Width = 72,
            Margin = new Padding(3),
            Hexadecimal = true
        };
        controls.EquipmentIdInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 65535,
            Value = 0,
            Width = 72,
            Margin = new Padding(3),
            Hexadecimal = true
        };
        controls.GuardFunctionBox = CreateEffectInjectionTextBox("004101D9", 100);
        controls.HookPointBox = CreateEffectInjectionTextBox("manual-hook", 180);
        controls.PromptBox = CreateEffectInjectionTextBox("特效注入草案", 240);

        var draftButton = new Button { Text = "生成草案" };
        ConfigureToolbarButton(draftButton, 88);
        var previewButton = new Button { Text = "预览汇编补丁" };
        ConfigureToolbarButton(previewButton, 118);

        var toolbar = CreateToolbarStack(4);
        AddToolbarRow(toolbar, 0,
            CreateToolbarLabel("模板", 0),
            controls.TemplateCombo,
            CreateToolbarLabel("Hook"),
            controls.HookAddressBox,
            CreateToolbarLabel("覆盖"),
            controls.OverwriteLengthInput,
            CreateToolbarLabel("旧字节锁"),
            controls.ExpectedOldBytesBox,
            CreateToolbarLabel("回跳"),
            controls.ReturnAddressBox);
        AddToolbarRow(toolbar, 1,
            CreateToolbarLabel("空洞字节", 0),
            controls.RequiredBytesInput,
            CreateToolbarLabel("策略"),
            controls.PolicyCombo,
            CreateToolbarLabel("包特效号"),
            controls.EffectIdInput,
            CreateToolbarLabel("Hook 点"),
            controls.HookPointBox);
        AddToolbarRow(toolbar, 2,
            CreateToolbarLabel("个人号", 0),
            controls.PersonalIdInput,
            CreateToolbarLabel("装备号"),
            controls.EquipmentIdInput,
            CreateToolbarLabel("判定函数"),
            controls.GuardFunctionBox,
            CreateToolbarLabel("说明"),
            controls.PromptBox);
        AddToolbarRow(toolbar, 3,
            draftButton,
            previewButton);
        layout.Controls.Add(toolbar, 0, 0);

        var split = CreateResizableSplit(
            "EffectInjection.Assembly.MainSplit",
            Orientation.Vertical,
            desiredDistance: 520,
            desiredPanel1Min: 260,
            desiredPanel2Min: 320);
        layout.Controls.Add(split, 0, 1);

        controls.AssemblySourceBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            AcceptsReturn = true,
            AcceptsTab = true,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9F),
            Text = "nop\r\njmp {return}"
        };
        split.Panel1.Controls.Add(CreateTitledPanel("汇编源码", controls.AssemblySourceBox, new Padding(0, 0, 0, 4)));

        var rightTabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        split.Panel2.Controls.Add(rightTabs);
        rightTabs.TabPages.Add(CreateControlTabPage("预览摘要", summaryBox));
        rightTabs.TabPages.Add(CreateControlTabPage("写入段", segmentsGrid));
        rightTabs.TabPages.Add(CreateControlTabPage("警告", warningsGrid));

        controls.TemplateCombo.SelectedIndexChanged += (_, _) =>
        {
            if (GetAssemblyTemplateName(controls) == "手写汇编") return;
            controls.AssemblySourceBox.Text = "nop";
            controls.RequiredBytesInput.Value = 128;
        };

        draftButton.Click += (_, _) =>
        {
            try
            {
                var templateName = GetAssemblyTemplateName(controls);
                if (templateName == "手写汇编")
                {
                    var draft = _assemblyPatchCompiler.Draft(
                        controls.PromptBox.Text,
                        "6.5",
                        decimal.ToInt32(controls.EffectIdInput.Value),
                        controls.HookPointBox.Text);
                    controls.RequiredBytesInput.Value = Math.Min(controls.RequiredBytesInput.Maximum, Math.Max(controls.RequiredBytesInput.Minimum, draft.RequiredCodeCaveBytes));
                    controls.AssemblySourceBox.Text = string.IsNullOrWhiteSpace(draft.AssemblySource)
                        ? "nop\r\njmp {return}"
                        : draft.AssemblySource.Replace("\n", "\r\n", StringComparison.Ordinal);
                    controls.HookPointBox.Text = draft.HookPoint;
                    summaryBox.Text = FormatAssemblyPatchDraft(draft);
                    warningsGrid.DataSource = BuildStringListTable("提示", draft.Risks.Concat(draft.DynamicValidationPlan));
                }
                else
                {
                    controls.RequiredBytesInput.Value = Math.Max(controls.RequiredBytesInput.Value, 96);
                    controls.HookPointBox.Text = templateName.Contains("增伤", StringComparison.Ordinal) ? "conditional-damage-up" : "conditional-damage-down";
                    controls.PromptBox.Text = templateName;
                    controls.AssemblySourceBox.Text = BuildDamageTemplateAssemblySource(controls).Replace("\n", "\r\n", StringComparison.Ordinal);
                    summaryBox.Text = FormatDamageTemplateDraft(controls);
                    warningsGrid.DataSource = BuildStringListTable("提示", BuildDamageTemplateWarnings(controls));
                }

                segmentsGrid.DataSource = null;
                ConfigureEffectInjectionGrid(warningsGrid);
                _currentEffectInjectionPreviewPackage = null;
                SetStatus("汇编草案已生成；请补齐 Hook 地址、旧字节锁和功能函数后预览");
            }
            catch (Exception ex)
            {
                summaryBox.Text = ex.ToString();
                MessageBox.Show(this, ex.Message, "生成汇编草案失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        previewButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project)) return;

            try
            {
                Cursor = Cursors.WaitCursor;
                var policy = Convert.ToString(controls.PolicyCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "smallest-fit";
                var preview = PreviewEffectInjectionPatchFromUi(project, controls, policy);
                _currentEffectInjectionPreviewPackage = preview.CanApply ? preview.Package : null;
                summaryBox.Text = FormatAssemblyPatchPreview(preview);
                segmentsGrid.DataSource = BuildPatchSegmentTable(preview.Package.PatchSegments);
                warningsGrid.DataSource = BuildStringListTable("警告", preview.Warnings);
                ConfigureEffectInjectionGrid(segmentsGrid);
                ConfigureEffectInjectionGrid(warningsGrid);
                SetStatus(preview.CanApply
                    ? "汇编补丁预览通过，可切换到“应用与报告”执行写入"
                    : "汇编补丁预览未通过，请检查警告");
            }
            catch (Exception ex)
            {
                _currentEffectInjectionPreviewPackage = null;
                summaryBox.Text = ex.ToString();
                segmentsGrid.DataSource = null;
                warningsGrid.DataSource = BuildStringListTable("错误", [ex.Message]);
                ConfigureEffectInjectionGrid(warningsGrid);
                MessageBox.Show(this, ex.Message, "预览汇编补丁失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        };

        return page;
    }

    private TabPage BuildEffectInjectionApplyPage(TextBox reportBox)
    {
        var page = new TabPage("应用与报告");
        var layout = CreateEffectInjectionPageLayout();
        page.Controls.Add(layout);

        var applyButton = new Button { Text = "应用补丁" };
        ConfigureToolbarButton(applyButton, 96);
        var refreshButton = new Button { Text = "查看当前预览" };
        ConfigureToolbarButton(refreshButton, 112);
        var toolbar = CreateToolbarStack(1);
        AddToolbarRow(toolbar, 0, applyButton, refreshButton);
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(CreateTitledPanel("应用报告", reportBox, new Padding(0, 4, 0, 4)), 0, 1);

        refreshButton.Click += (_, _) =>
        {
            reportBox.Text = _currentEffectInjectionPreviewPackage == null
                ? "当前没有可应用的汇编补丁预览。请先在“汇编注入”页完成预览。"
                : FormatEffectPackageForApply(_currentEffectInjectionPreviewPackage);
        };

        applyButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project)) return;
            if (_currentEffectInjectionPreviewPackage == null)
            {
                MessageBox.Show(this, "当前没有可应用的汇编补丁预览。请先在“汇编注入”页完成预览。", "不能应用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show(
                    this,
                    "即将写入 Ekd5.exe。应用前会重新预览、校验旧字节锁和 EXE SHA，并生成备份与 manifest。是否继续？",
                    "确认应用汇编补丁",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                var result = ApplyEffectInjectionPreviewPackage(project, _currentEffectInjectionPreviewPackage);
                reportBox.Text = FormatAssemblyPatchApply(result);
                SetStatus("汇编补丁应用完成");
                MessageBox.Show(this, result.Summary, "应用完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                reportBox.Text = ex.ToString();
                MessageBox.Show(this, ex.Message, "应用汇编补丁失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        };

        return page;
    }

    private bool TryGetEffectInjectionProject(out CczProject project)
    {
        if (_project == null)
        {
            project = null!;
            MessageBox.Show(this, "请先加载项目。", "未加载项目", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        project = _project;
        return true;
    }

    private static TableLayoutPanel CreateEffectInjectionPageLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return layout;
    }

    private static DataGridView CreateEffectInjectionGrid()
        => new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };

    private static TextBox CreateEffectInjectionInfoBox()
        => new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9F)
        };

    private static TextBox CreateEffectInjectionTextBox(string text, int width)
    {
        var box = new TextBox
        {
            Text = text
        };
        ConfigureToolbarInput(box, width, Math.Min(width, 80));
        return box;
    }

    private static TabPage CreateControlTabPage(string title, Control control)
    {
        var page = new TabPage(title);
        page.Controls.Add(control);
        return page;
    }

    private static void ConfigureEffectInjectionGrid(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.ReadOnly = true;
            if (column.HeaderText == "索引")
            {
                column.Visible = false;
                continue;
            }

            if (column.HeaderText.Contains("说明", StringComparison.Ordinal) ||
                column.HeaderText.Contains("证据", StringComparison.Ordinal) ||
                column.HeaderText.Contains("路径", StringComparison.Ordinal) ||
                column.HeaderText.Contains("字节", StringComparison.Ordinal) ||
                column.HeaderText.Contains("结论", StringComparison.Ordinal) ||
                column.HeaderText.Contains("建议", StringComparison.Ordinal))
            {
                column.Width = Math.Min(Math.Max(column.Width, 220), 520);
            }
        }

        foreach (DataGridViewRow row in grid.Rows)
        {
            var risk = Convert.ToString(row.Cells.Cast<DataGridViewCell>().FirstOrDefault(cell =>
                cell.OwningColumn.HeaderText.Contains("风险", StringComparison.Ordinal) ||
                cell.OwningColumn.HeaderText.Contains("警告", StringComparison.Ordinal) ||
                cell.OwningColumn.HeaderText.Contains("建议", StringComparison.Ordinal))?.Value, CultureInfo.InvariantCulture);
            if (risk?.Contains("人工", StringComparison.OrdinalIgnoreCase) == true ||
                risk?.Contains("谨慎", StringComparison.OrdinalIgnoreCase) == true ||
                risk?.Contains("候选", StringComparison.OrdinalIgnoreCase) == true)
            {
                row.DefaultCellStyle.BackColor = Color.LemonChiffon;
            }
        }
    }

    private static DataTable BuildInjectedEffectPatchTable(IReadOnlyList<InjectedEffectCandidate> candidates)
    {
        var table = new DataTable();
        table.Columns.Add("索引");
        table.Columns.Add("补丁名称");
        table.Columns.Add("识别结论");
        table.Columns.Add("补丁类型");
        table.Columns.Add("判定组数");
        table.Columns.Add("可识别参数位");
        table.Columns.Add("个人特技号");
        table.Columns.Add("装备/宝物号");
        table.Columns.Add("跳出点");
        table.Columns.Add("代码洞入口");
        table.Columns.Add("回到原流程");
        table.Columns.Add("可信度");
        table.Columns.Add("处理建议");

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            table.Rows.Add(
                index.ToString(CultureInfo.InvariantCulture),
                FirstNonEmpty(candidate.Name, "未知特技补丁"),
                FirstNonEmpty(candidate.StructureDiagnosis, candidate.UserReadableDiagnosis, FormatDiscoveryType(candidate.Type)),
                FormatPatchCategory(candidate.PatchCategory, candidate.PatternKind, candidate.Type),
                candidate.CheckGroups.Count.ToString(CultureInfo.InvariantCulture),
                FormatParameterSlotSummary(candidate.ParameterSlots),
                FormatNullableHex(candidate.PersonalEffectId),
                FormatNullableHex(candidate.EquipmentEffectId),
                FormatMaybeAddress(candidate.JumpOutAddress, candidate.HookPoint),
                FormatMaybeAddress(candidate.CodeCaveEntryAddress, candidate.CodeCave),
                FormatMaybeAddress(candidate.ReturnAddress, string.Empty),
                FormatConfidence(candidate.Confidence),
                FormatRiskAsAdvice(candidate.Risk, candidate.PatternKind));
        }

        return table;
    }

    private static DataTable BuildInjectedEffectModuleTable(IEnumerable<InjectedEffectModuleInfo> modules)
    {
        var table = new DataTable();
        table.Columns.Add("模块");
        table.Columns.Add("地址");
        table.Columns.Add("作用");
        table.Columns.Add("当前内容");
        table.Columns.Add("可否编辑");
        table.Columns.Add("说明");

        foreach (var module in modules)
        {
            table.Rows.Add(
                module.ModuleName,
                module.AddressHex,
                module.Role,
                module.CurrentContent,
                module.Editable,
                module.Description);
        }

        return table;
    }

    private static DataTable BuildInjectedEffectHookTable(IEnumerable<InjectedEffectHookCandidate> hooks)
    {
        var table = new DataTable();
        table.Columns.Add("地址");
        table.Columns.Add("跳转目标");
        table.Columns.Add("节");
        table.Columns.Add("类型");
        table.Columns.Add("说明");
        table.Columns.Add("证据");

        foreach (var hook in hooks)
        {
            table.Rows.Add(
                FormatAddressText(hook.AddressHex),
                FormatAddressText(hook.TargetHex),
                hook.SectionName,
                FormatHookClassification(hook.Classification),
                FormatHookRisk(hook.Risk),
                FormatEvidence(hook.Evidence));
        }

        return table;
    }

    private static DataTable BuildCodeCaveTable(
        IEnumerable<ExeCodeCaveCandidate> candidates,
        IReadOnlyList<AllocatedCodeCaveRange> allocations)
    {
        var table = new DataTable();
        table.Columns.Add("起始 VA");
        table.Columns.Add("结束 VA");
        table.Columns.Add("文件偏移");
        table.Columns.Add("长度");
        table.Columns.Add("节");
        table.Columns.Add("填充类型");
        table.Columns.Add("推荐");
        table.Columns.Add("已占用");
        table.Columns.Add("风险");
        table.Columns.Add("说明");
        table.Columns.Add("CaveId");

        foreach (var candidate in candidates)
        {
            var occupied = allocations.Any(range => RangesIntersect(
                candidate.StartVirtualAddress,
                candidate.EndVirtualAddress,
                range.StartVirtualAddress,
                range.EndVirtualAddress));
            table.Rows.Add(
                candidate.StartVirtualAddressHex,
                candidate.EndVirtualAddressHex,
                candidate.FileOffsetHex,
                candidate.Length.ToString(CultureInfo.InvariantCulture),
                candidate.SectionName,
                FormatFillKind(candidate.FillKind),
                candidate.IsRecommended ? "是" : "否",
                occupied ? "是" : "否",
                FormatCaveRisk(candidate.RiskLevel),
                FormatCaveReason(candidate.Reason),
                candidate.CaveId);
        }

        return table;
    }

    private static DataTable BuildPatchSegmentTable(IEnumerable<EffectPatchSegment> segments)
    {
        var table = new DataTable();
        table.Columns.Add("目标文件");
        table.Columns.Add("地址");
        table.Columns.Add("写入字节");
        table.Columns.Add("旧字节锁");
        table.Columns.Add("Hook 点");
        table.Columns.Add("代码洞");
        table.Columns.Add("分配区间");
        table.Columns.Add("说明");

        foreach (var segment in segments)
        {
            table.Rows.Add(
                segment.TargetFile,
                FirstNonEmpty(segment.AddressHex, segment.Address == 0 ? string.Empty : $"0x{segment.Address:X8}"),
                segment.BytesHex,
                segment.ExpectedOldBytesHex,
                segment.HookPoint,
                segment.CodeCaveId,
                segment.AllocatedRange,
                FormatPatchSegmentComment(segment.Comment));
        }

        return table;
    }

    private static DataTable BuildStringListTable(string columnName, IEnumerable<string> values)
    {
        var table = new DataTable();
        table.Columns.Add(columnName);
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            table.Rows.Add(TranslateTechnicalText(value));
        }

        return table;
    }

    private AssemblyPatchDraft BuildAssemblyPatchDraftFromUi(EffectInjectionAssemblyControls controls)
    {
        if (!TryParseEffectInjectionAddress(controls.HookAddressBox.Text, out var hookAddress))
        {
            throw new InvalidOperationException("Hook 地址必须填写为十六进制 VA，例如 00418335 或 0x00418335。");
        }

        var overwriteLength = decimal.ToInt32(controls.OverwriteLengthInput.Value);
        var expectedOldBytes = NormalizeHexByteText(controls.ExpectedOldBytesBox.Text);
        var expectedByteCount = CountHexBytes(expectedOldBytes);
        if (expectedByteCount != overwriteLength)
        {
            throw new InvalidOperationException($"旧字节锁长度必须等于覆盖长度。当前旧字节锁 {expectedByteCount} 字节，覆盖长度 {overwriteLength} 字节。");
        }

        var draft = new AssemblyPatchDraft
        {
            Prompt = controls.PromptBox.Text,
            TargetFile = "Ekd5.exe",
            EngineVersion = "6.5",
            EffectId = decimal.ToInt32(controls.EffectIdInput.Value),
            HookPoint = string.IsNullOrWhiteSpace(controls.HookPointBox.Text) ? "manual-hook" : controls.HookPointBox.Text.Trim(),
            HookAddress = hookAddress,
            HookAddressHex = $"0x{hookAddress:X8}",
            OverwriteLength = overwriteLength,
            ExpectedOldBytesHex = expectedOldBytes,
            RequiredCodeCaveBytes = decimal.ToInt32(controls.RequiredBytesInput.Value),
            AssemblySource = controls.AssemblySourceBox.Text.Replace("\r\n", "\n", StringComparison.Ordinal)
        };

        if (!string.IsNullOrWhiteSpace(controls.ReturnAddressBox.Text))
        {
            if (!TryParseEffectInjectionAddress(controls.ReturnAddressBox.Text, out var returnAddress))
            {
                throw new InvalidOperationException("回跳地址必须填写为十六进制 VA，或留空表示 Hook + 覆盖长度。");
            }

            draft.ReturnAddress = returnAddress;
            draft.ReturnAddressHex = $"0x{returnAddress:X8}";
        }

        var templateName = GetAssemblyTemplateName(controls);
        if (templateName != "手写汇编")
        {
            draft.Metadata["TemplateId"] = templateName.Contains("增伤", StringComparison.Ordinal)
                ? "conditional-damage-up-four-module"
                : "conditional-damage-down-four-module";
            draft.Metadata["TemplateName"] = templateName;
            draft.Metadata["PersonalEffectId"] = $"0x{decimal.ToInt32(controls.PersonalIdInput.Value):X2}";
            draft.Metadata["EquipmentEffectId"] = $"0x{decimal.ToInt32(controls.EquipmentIdInput.Value):X2}";
            draft.Metadata["GuardFunction"] = NormalizeAddressText(controls.GuardFunctionBox.Text);
            draft.Metadata["ParameterLayout"] = "push equipment; push personal; call guard; branch to return on failure; feature body; jump return";
        }

        return draft;
    }

    private AssemblyPatchPreviewResult PreviewEffectInjectionPatchFromUi(CczProject project, EffectInjectionAssemblyControls controls, string policy)
    {
        if (GetAssemblyTemplateName(controls) == "鎵嬪啓姹囩紪")
        {
            return _assemblyPatchCompiler.Preview(project, BuildAssemblyPatchDraftFromUi(controls), policy);
        }

        var specialPreview = _specialSkillInjectionService.Preview(project, BuildSpecialSkillDraftFromUi(project, controls), policy);
        return new AssemblyPatchPreviewResult
        {
            CanApply = specialPreview.CanApply,
            Summary = specialPreview.Summary,
            Warnings = specialPreview.Warnings.ToList(),
            Package = specialPreview.Package,
            Allocation = specialPreview.AssemblyPreview.Allocation,
            CodeCaveBytes = specialPreview.AssemblyPreview.CodeCaveBytes,
            HookBytes = specialPreview.AssemblyPreview.HookBytes,
            DisassemblyPreview = specialPreview.AssemblyPreview.DisassemblyPreview,
            PatchPreview = specialPreview.PatchPreview
        };
    }

    private InlineSpecialSkillPatchDraft BuildSpecialSkillDraftFromUi(CczProject project, EffectInjectionAssemblyControls controls)
    {
        var hookHint = string.IsNullOrWhiteSpace(controls.HookPointBox.Text) ||
                       controls.HookPointBox.Text.Contains("conditional-damage", StringComparison.OrdinalIgnoreCase)
            ? "strategy-damage-adjust-after-move"
            : controls.HookPointBox.Text.Trim();
        var draft = _specialSkillInjectionService.Draft(
            project,
            controls.PromptBox.Text,
            decimal.ToInt32(controls.EffectIdInput.Value),
            hookHint,
            decimal.ToInt32(controls.PersonalIdInput.Value),
            decimal.ToInt32(controls.EquipmentIdInput.Value),
            "damage-adjust");

        draft.FunctionAssemblySource = NormalizeSpecialSkillFunctionBody(controls.AssemblySourceBox.Text);
        if (TryParseEffectInjectionAddress(controls.HookAddressBox.Text, out var hookAddress))
        {
            draft.HookAddress = hookAddress;
            draft.HookAddressHex = $"0x{hookAddress:X8}";
        }

        if (!string.IsNullOrWhiteSpace(controls.ExpectedOldBytesBox.Text))
        {
            draft.ExpectedOldBytesHex = NormalizeHexByteText(controls.ExpectedOldBytesBox.Text);
        }

        draft.OverwriteLength = decimal.ToInt32(controls.OverwriteLengthInput.Value);
        if (!string.IsNullOrWhiteSpace(controls.ReturnAddressBox.Text) &&
            TryParseEffectInjectionAddress(controls.ReturnAddressBox.Text, out var returnAddress))
        {
            draft.ReturnAddress = returnAddress;
            draft.ReturnAddressHex = $"0x{returnAddress:X8}";
        }

        draft.RequiredCodeCaveBytes = decimal.ToInt32(controls.RequiredBytesInput.Value);
        return draft;
    }

    private AssemblyPatchApplyResult ApplyEffectInjectionPreviewPackage(CczProject project, EffectPackage package)
    {
        if (package.Metadata.TryGetValue("LogicalPatchKind", out var kind) &&
            kind.Equals("inline-special-skill", StringComparison.OrdinalIgnoreCase))
        {
            var specialApply = _specialSkillInjectionService.Apply(project, package);
            return new AssemblyPatchApplyResult
            {
                Applied = specialApply.Applied,
                Summary = specialApply.Summary,
                PatchApplyResult = specialApply.PatchApplyResult
            };
        }

        return _assemblyPatchCompiler.Apply(project, package);
    }

    private static string NormalizeSpecialSkillFunctionBody(string source)
    {
        var normalized = (source ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return "nop";
        if (normalized.Contains("call 0x004101D9", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("call 004101D9", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("jmp {return}", StringComparison.OrdinalIgnoreCase))
        {
            return "nop";
        }

        return normalized;
    }

    private static string FormatCodeCaveReport(ExeCodeCaveScanResult scan, IReadOnlyList<AllocatedCodeCaveRange> allocations)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"目标：{scan.TargetFilePath}");
        builder.AppendLine($"大小：{scan.ExeSize} 字节");
        builder.AppendLine($"ImageBase：{scan.ImageBaseHex}");
        builder.AppendLine($"SHA256：{scan.ExeSha256}");
        builder.AppendLine($"引擎：{scan.EngineVersionHint}，已知基底：{(scan.IsKnownEngine ? "是" : "否")}");
        builder.AppendLine($"扫描参数：最小长度={scan.MinimumLength}，显示 00 填充={(scan.IncludeZeroFill ? "是" : "否")}，显示 mixed 填充={(scan.IncludeMixedFill ? "是" : "否")}");
        builder.AppendLine($"候选：{scan.Candidates.Count}，manifest 占用：{allocations.Count}");
        AppendLines(builder, "禁用区间", scan.BlockedRanges.Select(range => $"{range.CaveId} {range.StartVirtualAddressHex}-{range.EndVirtualAddressHex} {FormatCaveReason(range.Reason)}"));
        AppendLines(builder, "manifest 已占用", allocations.Select(range => $"{range.CaveId} {range.StartVirtualAddressHex}-{range.EndVirtualAddressHex} 长度={range.Length} {range.Reason}"));
        AppendLines(builder, "警告", scan.Warnings.Select(TranslateTechnicalText));
        return builder.ToString();
    }

    private static string FormatAssemblyPatchDraft(AssemblyPatchDraft draft)
    {
        var builder = new StringBuilder();
        builder.AppendLine("汇编草案已生成。");
        builder.AppendLine($"目标文件：{draft.TargetFile}");
        builder.AppendLine($"引擎版本：{draft.EngineVersion}");
        builder.AppendLine($"包特效号：{draft.EffectId}");
        builder.AppendLine($"Hook 点：{draft.HookPoint}");
        builder.AppendLine($"需要代码洞：{draft.RequiredCodeCaveBytes} 字节");
        builder.AppendLine($"寄存器约束：{TranslateTechnicalText(draft.RegisterStrategy)}");
        AppendLines(builder, "风险", draft.Risks.Select(TranslateTechnicalText));
        AppendLines(builder, "动态验证计划", draft.DynamicValidationPlan.Select(TranslateTechnicalText));
        return builder.ToString();
    }

    private static string FormatDamageTemplateDraft(EffectInjectionAssemblyControls controls)
    {
        var builder = new StringBuilder();
        builder.AppendLine(GetAssemblyTemplateName(controls) + "已生成。");
        builder.AppendLine("结构：Hook 跳出点 -> 代码洞桩函数 -> 判定通过后执行功能函数 -> 回到原流程。");
        builder.AppendLine($"个人号：{FormatNullableHex(decimal.ToInt32(controls.PersonalIdInput.Value))}");
        builder.AppendLine($"装备号：{FormatNullableHex(decimal.ToInt32(controls.EquipmentIdInput.Value))}");
        builder.AppendLine($"判定函数：{NormalizeAddressText(controls.GuardFunctionBox.Text)}");
        builder.AppendLine("下一步：把“拥有特技时执行”的功能函数替换为实际增伤/减伤逻辑，然后预览。");
        return builder.ToString();
    }

    private static IEnumerable<string> BuildDamageTemplateWarnings(EffectInjectionAssemblyControls controls)
    {
        yield return "四模块模板只适用于条件增伤/条件减伤类补丁；其它特技请使用手写汇编并人工确认。";
        yield return "默认只生成结构骨架，不证明判定函数和战斗上下文寄存器已经正确。";
        if (decimal.ToInt32(controls.PersonalIdInput.Value) > 0x7F || decimal.ToInt32(controls.EquipmentIdInput.Value) > 0x7F)
        {
            yield return "当前个人号或装备号超过 0x7F，模板会使用 push imm32，避免 6A imm8 的符号扩展问题。";
        }
    }

    private static string FormatAssemblyPatchPreview(AssemblyPatchPreviewResult preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine(TranslateTechnicalText(preview.Summary));
        builder.AppendLine();
        builder.AppendLine($"是否可应用：{(preview.CanApply ? "是" : "否")}");
        if (preview.Allocation.Allocation != null)
        {
            builder.AppendLine($"代码洞：{preview.Allocation.Allocation.StartVirtualAddressHex}-{preview.Allocation.Allocation.EndVirtualAddressHex}，长度={preview.Allocation.Allocation.Length}");
            builder.AppendLine($"CaveId：{preview.Allocation.Allocation.CaveId}");
        }
        else
        {
            builder.AppendLine($"代码洞分配：{TranslateTechnicalText(preview.Allocation.Status)} {TranslateTechnicalText(preview.Allocation.Reason)}");
        }

        builder.AppendLine($"Hook 字节：{FormatBytes(preview.HookBytes)}");
        builder.AppendLine($"代码洞字节：{FormatBytes(preview.CodeCaveBytes)}");
        builder.AppendLine();
        builder.AppendLine("反汇编预览：");
        builder.AppendLine(string.IsNullOrWhiteSpace(preview.DisassemblyPreview) ? "未生成；可能未找到 ndisasm.exe。" : preview.DisassemblyPreview);
        AppendLines(builder, "警告", preview.Warnings.Select(TranslateTechnicalText));
        return builder.ToString();
    }

    private static string FormatEffectPackageForApply(EffectPackage package)
    {
        var builder = new StringBuilder();
        builder.AppendLine("当前可应用预览：");
        builder.AppendLine($"PackageId：{package.PackageId}");
        builder.AppendLine($"名称：{package.Name}");
        builder.AppendLine($"领域：{package.Domain}");
        builder.AppendLine($"包特效号：{package.EffectId}");
        builder.AppendLine($"写入段：{package.PatchSegments.Count}");
        if (package.Metadata.TryGetValue("EngineProfileSha256", out var sha))
        {
            builder.AppendLine($"EXE SHA 锁：{sha}");
        }
        if (package.Metadata.TryGetValue("AllocatedRange", out var allocated))
        {
            builder.AppendLine($"代码洞分配：{allocated}");
        }
        if (package.Metadata.TryGetValue("TemplateName", out var templateName))
        {
            builder.AppendLine($"模板：{templateName}");
        }
        builder.AppendLine();
        foreach (var segment in package.PatchSegments)
        {
            builder.AppendLine($"{segment.TargetFile} {FirstNonEmpty(segment.AddressHex, $"0x{segment.Address:X8}")} 写入={CountHexBytes(segment.BytesHex)} 字节，旧字节锁={CountHexBytes(segment.ExpectedOldBytesHex)} 字节，{FormatPatchSegmentComment(segment.Comment)}");
        }

        return builder.ToString();
    }

    private static string FormatAssemblyPatchApply(AssemblyPatchApplyResult result)
    {
        var apply = result.PatchApplyResult;
        var builder = new StringBuilder();
        builder.AppendLine(result.Summary);
        builder.AppendLine();
        builder.AppendLine($"已应用：{(result.Applied ? "是" : "否")}");
        builder.AppendLine($"Manifest：{apply.ManifestPath}");
        builder.AppendLine($"变化字节：{apply.ChangedBytes}");
        builder.AppendLine($"变化项：{apply.ChangeCount}");
        AppendLines(builder, "备份", apply.BackupPaths);
        AppendLines(builder, "报告", apply.ReportPaths);
        builder.AppendLine();
        builder.AppendLine("下一步建议：用 x32dbg 在 Hook 点、代码洞入口和回跳地址设置断点，触发对应战斗行为后核对寄存器、栈和内存状态。");
        return builder.ToString();
    }

    private static void AppendLines(StringBuilder builder, string title, IEnumerable<string> lines)
    {
        var list = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        if (list.Count == 0) return;

        builder.AppendLine();
        builder.AppendLine(title + "：");
        foreach (var line in list)
        {
            builder.AppendLine("- " + line);
        }
    }

    private static string BuildDiscoverySummary(InjectedEffectDiscoveryReport report, bool showingHooks)
    {
        var structures = report.Candidates.Count(candidate =>
            candidate.PatternKind == InjectedEffectPatternKind.FourModuleDamageModifier ||
            candidate.PatternKind == InjectedEffectPatternKind.FourModuleLikeCandidate ||
            candidate.PatternKind == InjectedEffectPatternKind.KnownPatch);
        var hidden = showingHooks ? 0 : report.HookCandidates.Count;
        return showingHooks
            ? $"识别完成：已识别 {structures} 个补丁结构，正在显示 {report.HookCandidates.Count} 条跳转候选"
            : $"识别完成：已识别 {structures} 个补丁结构，低置信 Hook 候选 {hidden} 条已隐藏";
    }

    private static string BuildDamageTemplateAssemblySource(EffectInjectionAssemblyControls controls)
    {
        var equipment = decimal.ToInt32(controls.EquipmentIdInput.Value);
        var personal = decimal.ToInt32(controls.PersonalIdInput.Value);
        var guard = NormalizeAddressText(controls.GuardFunctionBox.Text);
        var guardAddress = string.IsNullOrWhiteSpace(guard) ? "0x004101D9" : guard;
        var featureComment = GetAssemblyTemplateName(controls).Contains("增伤", StringComparison.Ordinal)
            ? "replace this block with damage increase logic"
            : "replace this block with damage reduction logic";

        var builder = new StringBuilder();
        builder.AppendLine("; four-module conditional damage template");
        builder.AppendLine("; module 2: guard stub");
        builder.AppendLine(PushImmediateLine(equipment, "equipment effect id"));
        builder.AppendLine(PushImmediateLine(personal, "personal effect id"));
        builder.AppendLine($"call {guardAddress}");
        builder.AppendLine("test eax, eax");
        builder.AppendLine("jz .no_effect");
        builder.AppendLine();
        builder.AppendLine("; feature function: " + featureComment);
        builder.AppendLine("nop");
        builder.AppendLine();
        builder.AppendLine(".no_effect:");
        builder.AppendLine("jmp {return}");
        return builder.ToString();
    }

    private static string PushImmediateLine(int value, string comment)
        => value is >= 0 and <= 0x7F
            ? $"push byte 0x{value:X2} ; {comment}"
            : $"push dword 0x{value:X8} ; {comment}";

    private static string GetAssemblyTemplateName(EffectInjectionAssemblyControls controls)
        => Convert.ToString(controls.TemplateCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "手写汇编";

    private static string FormatDiscoveryType(string type)
        => type switch
        {
            "KnownPatch" => "本地补丁签名命中",
            "InlineStub" => "内联特技桩",
            "ExecutableJump" => "普通跳转候选",
            _ => "未知识别来源"
        };

    private static string FormatPatternKind(string pattern, string fallbackType)
        => pattern switch
        {
            InjectedEffectPatternKind.KnownPatch => "已收录补丁",
            InjectedEffectPatternKind.InlineCoreStub => "内联特技判定桩",
            InjectedEffectPatternKind.FourModuleDamageModifier => "四模块条件伤害模板",
            InjectedEffectPatternKind.FourModuleLikeCandidate => "四模块样式候选",
            InjectedEffectPatternKind.RawJumpCandidate => "普通跳转候选",
            _ => FormatDiscoveryType(fallbackType)
        };

    private static string FormatPatchCategory(string category, string pattern, string fallbackType)
        => category switch
        {
            InjectedEffectPatchCategory.SimpleFourModuleSpecialEffect => "单判定特技补丁",
            InjectedEffectPatchCategory.MultiCheckSpecialEffect => "多判定特技补丁",
            InjectedEffectPatchCategory.ComplexMultiHookPatch => "复杂多入口补丁",
            InjectedEffectPatchCategory.FunctionExtensionPatch => "功能/引擎扩展补丁",
            InjectedEffectPatchCategory.KnownPatchSignatureOnly => "已收录补丁签名",
            InjectedEffectPatchCategory.InlineCoreStub => "内联特技判定桩",
            InjectedEffectPatchCategory.UnknownCandidate => "结构相似候选",
            _ => FormatPatternKind(pattern, fallbackType)
        };

    private static string FormatParameterSlotSummary(IReadOnlyList<InjectedEffectParameterSlot> slots)
    {
        if (slots.Count == 0) return "未识别";
        var personal = slots.Count(slot => slot.Role == InjectedEffectParameterRole.Personal);
        var equipment = slots.Count(slot => slot.Role == InjectedEffectParameterRole.Equipment);
        var config = slots.Count - personal - equipment;
        var parts = new List<string>();
        if (equipment > 0) parts.Add($"宝物/装备 {equipment}");
        if (personal > 0) parts.Add($"个人/特技 {personal}");
        if (config > 0) parts.Add($"其它配置 {config}");
        return string.Join("，", parts);
    }

    private static string FormatConfidence(string confidence)
        => confidence switch
        {
            "KnownPatchExact" => "高：完整命中本地补丁",
            "KnownPatchVariant" => "中：疑似本地补丁变体",
            "KnownEffectIdInlineStub" => "中：特技号命中知识库",
            "InlineStubDetected" => "中：识别到特技判定桩",
            "CallToCoreEngine" => "低：仅识别到特技核心调用",
            "FourModuleLikeCandidate" => "低：结构相似，需人工确认",
            _ => string.IsNullOrWhiteSpace(confidence) ? "未知" : TranslateTechnicalText(confidence)
        };

    private static string FormatRiskAsAdvice(string risk, string pattern)
        => risk switch
        {
            "known-sample" => "可作为已注入补丁处理；改动前仍建议备份。",
            "partial-match-review" => "只命中部分补丁段，先人工复核差异。",
            "semantic-review-required" => pattern == InjectedEffectPatternKind.FourModuleLikeCandidate
                ? "不要直接认定为增伤/减伤；先用实战或断点确认用途。"
                : "需要结合战斗行为确认效果。",
            "parameter-parse-incomplete" => "参数不完整，暂不建议编辑。",
            "multi-check-readonly-review" => "多判定补丁：可查看多组参数位，不建议套用单判定四模块模板。",
            "complex-multihook-readonly-review" => "复杂多入口补丁：已识别参数位也应先人工复核，不建议一键重注入。",
            "function-extension-not-special-template" => "功能扩展补丁：不是特技判定模板，仅作为已收录补丁识别。",
            _ => string.IsNullOrWhiteSpace(risk) ? "无明确风险" : TranslateTechnicalText(risk)
        };

    private static string FormatHookClassification(string classification)
        => classification switch
        {
            "KnownHookJump" => "已知 Hook 跳转",
            "ExecutableJump" => "普通跳转候选",
            _ => TranslateTechnicalText(classification)
        };

    private static string FormatHookRisk(string risk)
        => risk switch
        {
            "known-hook-review-current-bytes" => "命中已知 Hook 点，请复核当前字节是否被二次改写。",
            "jump-graph-candidate" => "仅为跳转候选，默认不认定为特技。",
            _ => TranslateTechnicalText(risk)
        };

    private static string FormatEvidence(string evidence)
        => evidence switch
        {
            "E9 rel32" => "E9 相对跳转指令",
            _ => TranslateTechnicalText(evidence)
        };

    private static string FormatMaybeAddress(uint? address, string fallback)
        => address.HasValue ? $"0x{address.Value:X8}" : FormatAddressText(fallback);

    private static string FormatAddressText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => TryParseEffectInjectionAddress(part, out var address) ? $"0x{address:X8}" : part)
            .DefaultIfEmpty(string.Empty)
            .Aggregate((left, right) => left + ", " + right);
    }

    private static string NormalizeAddressText(string? text)
        => TryParseEffectInjectionAddress(text, out var address) ? $"0x{address:X8}" : string.Empty;

    private static string FormatFillKind(string fillKind)
        => fillKind switch
        {
            "nop" => "NOP 填充",
            "zero" => "00 填充",
            "mixed" => "混合填充",
            _ => TranslateTechnicalText(fillKind)
        };

    private static string FormatCaveRisk(string risk)
        => risk switch
        {
            "low" => "低",
            "medium" => "中",
            "high" => "高",
            "blocked" => "禁用",
            _ => TranslateTechnicalText(risk)
        };

    private static string FormatCaveReason(string reason)
        => TranslateTechnicalText(reason)
            .Replace("recommended nop cave", "推荐的 NOP 空洞", StringComparison.OrdinalIgnoreCase)
            .Replace("zero fill cave", "00 填充空洞", StringComparison.OrdinalIgnoreCase)
            .Replace("mixed fill cave", "混合填充空洞", StringComparison.OrdinalIgnoreCase);

    private static string FormatPatchSegmentComment(string comment)
        => comment switch
        {
            "Assembly patch hook jump." => "Hook 跳转段",
            "Assembly patch compiled code cave body." => "代码洞主体",
            _ => TranslateTechnicalText(comment)
        };

    private static string TranslateTechnicalText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text
            .Replace("KnownPatchExact", "完整命中本地补丁", StringComparison.OrdinalIgnoreCase)
            .Replace("KnownPatchVariant", "疑似本地补丁变体", StringComparison.OrdinalIgnoreCase)
            .Replace("InlineStubDetected", "识别到特技判定桩", StringComparison.OrdinalIgnoreCase)
            .Replace("CallToCoreEngine", "仅识别到特技核心调用", StringComparison.OrdinalIgnoreCase)
            .Replace("jump-graph-candidate", "仅为跳转候选", StringComparison.OrdinalIgnoreCase)
            .Replace("semantic-review-required", "需要结合战斗行为确认效果", StringComparison.OrdinalIgnoreCase)
            .Replace("E9 rel32", "E9 相对跳转指令", StringComparison.OrdinalIgnoreCase)
            .Replace("canApply=True", "可应用=是", StringComparison.OrdinalIgnoreCase)
            .Replace("canApply=False", "可应用=否", StringComparison.OrdinalIgnoreCase)
            .Replace("Assembly patch preview", "汇编补丁预览", StringComparison.OrdinalIgnoreCase)
            .Replace("warnings=", "警告=", StringComparison.OrdinalIgnoreCase)
            .Replace("codeBytes=", "代码字节=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseEffectInjectionAddress(string? text, out uint value)
    {
        value = 0;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        return normalized.Length > 0 && uint.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeHexByteText(string text)
    {
        var hex = new string((text ?? string.Empty).Where(Uri.IsHexDigit).ToArray());
        if (hex.Length == 0) return string.Empty;
        if (hex.Length % 2 != 0) throw new InvalidOperationException("旧字节锁包含奇数个十六进制字符。");
        return string.Join(' ', Enumerable.Range(0, hex.Length / 2).Select(index => hex.Substring(index * 2, 2).ToUpperInvariant()));
    }

    private static int CountHexBytes(string text)
    {
        var count = (text ?? string.Empty).Count(Uri.IsHexDigit);
        if (count == 0) return 0;
        if (count % 2 != 0) throw new InvalidOperationException("十六进制字节串长度必须为偶数。");
        return count / 2;
    }

    private static string FormatBytes(byte[] bytes)
        => bytes.Length == 0 ? string.Empty : BitConverter.ToString(bytes).Replace("-", " ");

    private static string FormatNullableHex(int? value)
        => value.HasValue ? $"0x{value.Value:X2} / {value.Value}" : string.Empty;

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool RangesIntersect(uint leftStart, uint leftEnd, uint rightStart, uint rightEnd)
        => leftStart <= rightEnd && rightStart <= leftEnd;

    private sealed class EffectInjectionAssemblyControls
    {
        public ComboBox TemplateCombo { get; set; } = null!;
        public TextBox HookAddressBox { get; set; } = null!;
        public NumericUpDown OverwriteLengthInput { get; set; } = null!;
        public TextBox ExpectedOldBytesBox { get; set; } = null!;
        public TextBox ReturnAddressBox { get; set; } = null!;
        public NumericUpDown RequiredBytesInput { get; set; } = null!;
        public ComboBox PolicyCombo { get; set; } = null!;
        public NumericUpDown EffectIdInput { get; set; } = null!;
        public NumericUpDown PersonalIdInput { get; set; } = null!;
        public NumericUpDown EquipmentIdInput { get; set; } = null!;
        public TextBox GuardFunctionBox { get; set; } = null!;
        public TextBox HookPointBox { get; set; } = null!;
        public TextBox PromptBox { get; set; } = null!;
        public TextBox AssemblySourceBox { get; set; } = null!;
    }
}
