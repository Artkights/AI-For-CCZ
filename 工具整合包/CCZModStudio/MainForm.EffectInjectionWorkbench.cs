using System.Data;
using System.Globalization;
using System.Text;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private EffectInventoryReport? _currentEffectInventory;
    private EffectParameterUpdatePreview? _currentEffectParameterPreview;
    private CompositeEffectPreview? _currentCompositeEffectPreview;
    private ModularEffectPreview? _currentModularEffectPreview;
    private CompositeEffectMaintenancePreview? _currentCompositeMaintenancePreview;
    private EffectAnalysisSnapshot? _currentEffectAnalysisSnapshot;

    private TabPage BuildCurrentEffectsWorkbenchPage()
    {
        var page = new TabPage("特效清单");
        var releaseConsistency = new EffectReleaseConsistencyService().Read();
        var layout = CreateEffectInjectionPageLayout();
        page.Controls.Add(layout);

        var scanButton = new Button { Text = "扫描" };
        ConfigureToolbarButton(scanButton, 88);
        var sourceFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        sourceFilter.Items.AddRange(["全部", "原生", "注入", "复合", "研究中"]);
        sourceFilter.SelectedIndex = 0;
        var searchBox = CreateEffectInjectionTextBox(string.Empty, 220);
        searchBox.PlaceholderText = "搜索名称或效果";
        var validationButton = new Button { Text = "验证", AutoSize = true };
        ConfigureToolbarButton(validationButton, 82);
        var advancedButton = new Button { Text = "高级诊断" };
        ConfigureToolbarButton(advancedButton, 106);
        var summaryLabel = new EffectStageControl
        {
            Text = $"尚未扫描；组件：{releaseConsistency.StatusZh}",
            BackColor = SystemColors.Control,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 4, 3, 2)
        };
        var toolbar = CreateToolbarStack(1);
        AddToolbarRow(toolbar, 0, scanButton, sourceFilter, searchBox, validationButton, advancedButton);
        layout.RowCount = 3;
        layout.RowStyles.Clear();
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(summaryLabel, 0, 1);

        var split = CreateResizableSplit("EffectInjection.Workbench.MainSplit", Orientation.Vertical, 660, 420, 560);
        layout.Controls.Add(split, 0, 2);
        var grid = CreateEffectInjectionGrid();
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        split.Panel1.Controls.Add(grid);

        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        split.Panel2.Controls.Add(details);
        var titleLabel = new Label { Text = "请选择特技", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 6, 3, 8) };
        details.Controls.Add(titleLabel, 0, 0);
        var detailTabs = new TabControl { Dock = DockStyle.Fill };
        var parameterPage = new TabPage("参数与证据");
        var parameterLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(6) };
        parameterLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        parameterLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        parameterPage.Controls.Add(parameterLayout);
        detailTabs.TabPages.Add(parameterPage);
        details.Controls.Add(detailTabs, 0, 1);
        var parameterPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        parameterLayout.Controls.Add(parameterPanel, 0, 0);
        var meaningBox = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = SystemColors.Window, WordWrap = true };
        parameterLayout.Controls.Add(meaningBox, 0, 1);
        var previewButton = new Button { Text = "预览修改", Enabled = false };
        ConfigureToolbarButton(previewButton, 106);
        var applyButton = new Button { Text = "写入修改", Enabled = false };
        ConfigureToolbarButton(applyButton, 106);
        var toggleButton = new Button { Text = "停用", Enabled = false, Visible = false };
        ConfigureToolbarButton(toggleButton, 82);
        var repairButton = new Button { Text = "修复", Enabled = false, Visible = false };
        ConfigureToolbarButton(repairButton, 82);
        var removeButton = new Button { Text = "删除", Enabled = false, Visible = false };
        ConfigureToolbarButton(removeButton, 82);
        var buttonRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttonRow.Controls.Add(previewButton);
        buttonRow.Controls.Add(applyButton);
        buttonRow.Controls.Add(toggleButton);
        buttonRow.Controls.Add(repairButton);
        buttonRow.Controls.Add(removeButton);
        details.Controls.Add(buttonRow, 0, 2);
        var previewLabel = new Label { AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = SystemColors.GrayText };
        details.Controls.Add(previewLabel, 0, 3);

        var visibleInstances = new List<LogicalEffectInstance>();
        var editors = new Dictionary<string, Func<int>>(StringComparer.OrdinalIgnoreCase);
        EffectModuleCatalog? currentModuleCatalog = null;

        void RefreshGrid()
        {
            var source = (_currentEffectInventory?.Effects ?? []).Where(item => sourceFilter.SelectedIndex switch
            {
                1 => item.SourceKind == EffectInstanceSourceKind.Native,
                2 => item.HasInjectedImplementation && item.PatchCategory is not "CompositeEffect" and not "ModularSemanticEffectV2",
                3 => item.PatchCategory is "CompositeEffect" or "ModularSemanticEffectV2",
                4 => item.WriteDecision?.CanApply != true,
                _ => true
            });
            var query = searchBox.Text.Trim();
            visibleInstances = source.Where(item => string.IsNullOrWhiteSpace(query) ||
                                                    item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                                    item.NaturalLanguageDescription.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            grid.DataSource = BuildLogicalEffectTable(visibleInstances);
            ConfigureEffectInjectionGrid(grid);
        }

        void RefreshDetails()
        {
            editors.Clear();
            parameterPanel.Controls.Clear();
            previewButton.Enabled = false;
            applyButton.Enabled = false;
            toggleButton.Visible = repairButton.Visible = removeButton.Visible = false;
            while (detailTabs.TabPages.Count > 1)
            {
                var oldPage = detailTabs.TabPages[0];
                detailTabs.TabPages.RemoveAt(0);
                oldPage.Dispose();
            }
            _currentCompositeMaintenancePreview = null;
            _currentEffectParameterPreview = null;
            var instance = GetSelectedLogicalEffect(grid, visibleInstances);
            if (instance == null)
            {
                titleLabel.Text = "请选择特技";
                meaningBox.Text = string.Empty;
                previewLabel.Text = string.Empty;
                return;
            }

            titleLabel.Text = instance.Name;
            if (instance.PersonalChannel != null)
            {
                detailTabs.TabPages.Insert(0, BuildNativeEffectConfigurationPage(
                    CompositeEffectChannel.PersonalJob, instance.PersonalChannel.EffectId, embedded: true));
            }
            if (instance.ItemChannel?.IsEnabled == true)
            {
                var position = instance.PersonalChannel == null ? 0 : 1;
                detailTabs.TabPages.Insert(position, BuildNativeEffectConfigurationPage(
                    CompositeEffectChannel.Item, instance.ItemChannel.EffectId, embedded: true));
            }
            detailTabs.SelectedIndex = 0;
            var tags = currentModuleCatalog?.InstanceTags.FirstOrDefault(item => item.InstanceId == instance.InstanceId);
            meaningBox.Text = BuildEffectChannelDetail(instance) +
                              (tags?.TagsZh.Count > 0 ? Environment.NewLine + Environment.NewLine + "模块标签：" + string.Join("、", tags.TagsZh) : string.Empty);
            foreach (var parameter in instance.Parameters)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = parameter.DisplayName, Width = 112, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 6, 4, 0) });
                if (!parameter.IsConsistent)
                {
                    row.Controls.Add(new Label { Text = "配置不一致", AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(0, 6, 0, 0) });
                }
                else if (instance.WriteDecision?.CanEdit == true && parameter.WriteDecision?.CanEdit == true &&
                         parameter.Role is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment)
                {
                    var options = parameter.Role == InjectedEffectParameterRole.Personal
                        ? _currentEffectInventory?.PersonalJobOptions ?? []
                        : _currentEffectInventory?.ItemOptions ?? [];
                    var editor = new ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        DisplayMember = nameof(EffectChannelReference.DisplayName),
                        ValueMember = nameof(EffectChannelReference.EffectId),
                        DataSource = options.ToList(),
                        Width = 220
                    };
                    editor.SelectedValue = parameter.Value.GetValueOrDefault();
                    row.Controls.Add(editor);
                    editors[parameter.SlotId] = () => Convert.ToInt32(editor.SelectedValue, CultureInfo.InvariantCulture);
                }
                else if (instance.WriteDecision?.CanEdit == true && parameter.WriteDecision?.CanEdit == true && parameter.Value.HasValue)
                {
                    var editor = new NumericUpDown
                    {
                        Minimum = parameter.Minimum ?? 0,
                        Maximum = parameter.Maximum ?? 255,
                        Value = Math.Clamp(parameter.Value.GetValueOrDefault(), parameter.Minimum ?? 0, parameter.Maximum ?? 255),
                        Width = 92
                    };
                    row.Controls.Add(editor);
                    editors[parameter.SlotId] = () => decimal.ToInt32(editor.Value);
                }
                else
                {
                    row.Controls.Add(new Label { Text = FormatLogicalParameter(parameter), AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
                }
                parameterPanel.Controls.Add(row);
            }
            var navigation = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            if (instance.PersonalChannel != null)
            {
                var jobButton = new Button { Text = "兵种特效", AutoSize = true };
                jobButton.Click += (_, _) => OpenJobEffectEditor(instance.PersonalChannel.EffectId);
                navigation.Controls.Add(jobButton);
                var personalButton = new Button { Text = "人物专属", AutoSize = true };
                personalButton.Click += (_, _) => OpenRolePersonalEffectTableEditor(instance.PersonalChannel.EffectId);
                navigation.Controls.Add(personalButton);
            }
            if (instance.ItemChannel?.IsEnabled == true)
            {
                var itemButton = new Button { Text = "宝物配置", AutoSize = true };
                itemButton.Click += (_, _) => OpenItemEffectCatalogEditor(instance.ItemChannel.EffectId);
                navigation.Controls.Add(itemButton);
            }
            if (navigation.Controls.Count > 0) parameterPanel.Controls.Add(navigation);
            previewButton.Enabled = instance.WriteDecision?.CanPreview == true && editors.Count > 0;
            var isComposite = instance.PatchCategory == "CompositeEffect";
            var isModular = instance.PatchCategory == "ModularSemanticEffectV2";
            toggleButton.Visible = removeButton.Visible = repairButton.Visible = isComposite || isModular;
            toggleButton.Text = instance.InstallationStatus == CompositeInstallationStatus.Disabled ? "启用" : "停用";
            var currentRelease = new EffectReleaseConsistencyService().Read(forceRefresh: true);
            toggleButton.Enabled = currentRelease.CanWrite && (isComposite || isModular) && instance.InstallationStatus is CompositeInstallationStatus.Complete or CompositeInstallationStatus.Disabled;
            repairButton.Enabled = currentRelease.CanWrite && (isComposite || isModular) && instance.InstallationStatus is CompositeInstallationStatus.Repairable or CompositeInstallationStatus.Incomplete;
            removeButton.Enabled = currentRelease.CanWrite && (isComposite || isModular) && instance.InstallationStatus is CompositeInstallationStatus.Complete or CompositeInstallationStatus.Disabled;
            previewLabel.Text = isComposite || isModular
                ? $"状态：{instance.InstallationStatusZh}。{instance.EditabilityReason}"
                : instance.WriteDecision == null
                    ? instance.EditabilityReason
                    : $"{string.Join(",", instance.WriteDecision.BlockerCodes)} {string.Join("；", instance.WriteDecision.ReasonsZh)}";
        }

        scanButton.Click += async (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project)) return;
            var projectKey = ProjectPatchIdentityService.NormalizePath(project.GameRoot);
            IProgress<string> progress = new Progress<string>(stage =>
            {
                if (!IsDisposed) summaryLabel.Text = stage + "……";
            });
            try
            {
                scanButton.Enabled = false;
                summaryLabel.Text = "正在后台读取 EXE 并构建特效库存……";
                await _asyncLoadCoordinator.RunLatestAsync(
                    "effect-workbench-scan",
                    projectKey,
                    cancellation => EffectAnalysisCoordinator.Shared.ScanAsync(project, "Ekd5.exe", progress, cancellation),
                    snapshot =>
                    {
                        if (IsDisposed || !TryGetEffectInjectionProject(out var currentProject) ||
                            !ProjectPatchIdentityService.NormalizePath(currentProject.GameRoot).Equals(projectKey, StringComparison.OrdinalIgnoreCase))
                            return Task.CompletedTask;
                        _currentEffectAnalysisSnapshot = snapshot;
                        _currentEffectInventory = snapshot.Inventory;
                        currentModuleCatalog = snapshot.ModuleCatalog;
                        summaryLabel.Text = $"{snapshot.Inventory.Summary}；{snapshot.CacheState}；{snapshot.ReleaseConsistency.StatusZh}";
                        RefreshGrid();
                        RefreshDetails();
                        SetStatus(summaryLabel.Text);
                        return Task.CompletedTask;
                    });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { if (!IsDisposed) scanButton.Enabled = true; }
        };
        sourceFilter.SelectedIndexChanged += (_, _) => { RefreshGrid(); RefreshDetails(); };
        searchBox.TextChanged += (_, _) =>
            _debouncedUiAction.Schedule("EffectInventorySearch", TimeSpan.FromMilliseconds(200), () =>
            {
                RefreshGrid();
                RefreshDetails();
            });
        grid.SelectionChanged += (_, _) => RefreshDetails();
        advancedButton.Click += (_, _) => ShowEffectAdvancedDiagnostics(_currentEffectAnalysisSnapshot);
        var validationMenu = new ContextMenuStrip();
        validationMenu.Items.Add("验证缺失权限", null, async (_, _) => await RunEffectValidationAsync(summaryLabel));
        validationMenu.Items.Add("验证并支持此 EXE", null, async (_, _) => await PrepareEffectProfileAsync(summaryLabel));
        validationMenu.Items.Add(new ToolStripSeparator());
        validationMenu.Items.Add("诊断当前版本", null, (_, _) => ShowEffectRuntimeDiagnostics());
        validationButton.Click += (_, _) => validationMenu.Show(validationButton, new Point(0, validationButton.Height));
        page.Disposed += (_, _) => validationMenu.Dispose();

        previewButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project)) return;
            var instance = GetSelectedLogicalEffect(grid, visibleInstances);
            if (instance == null) return;
            if (instance.PatchCategory == "ModularSemanticEffectV2")
            {
                var value = editors.Values.Select(read => read()).FirstOrDefault();
                _currentCompositeMaintenancePreview = new ModularEffectLifecycleService().PreviewUpdate(project,
                    new ModularEffectMaintenanceDraft { ManifestId = instance.WritableContractId, Value = value });
                previewLabel.Text = _currentCompositeMaintenancePreview.SummaryZh;
                applyButton.Enabled = new EffectReleaseConsistencyService().Read(forceRefresh: true).CanWrite && _currentCompositeMaintenancePreview.CanApply;
                return;
            }
            var request = new EffectParameterUpdateRequest { InstanceId = instance.InstanceId };
            request.Updates.AddRange(editors.Select(pair => new EffectParameterValueUpdate { SlotId = pair.Key, Value = pair.Value() }));
            _currentEffectParameterPreview = new EffectInventoryService().PreviewParameterUpdate(project, request);
            previewLabel.Text = _currentEffectParameterPreview.Summary;
            applyButton.Enabled = new EffectReleaseConsistencyService().Read(forceRefresh: true).CanWrite && _currentEffectParameterPreview.CanApply;
        };

        applyButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project) ||
                _currentEffectParameterPreview?.CanApply != true && _currentCompositeMaintenancePreview?.CanApply != true) return;
            if (MessageBox.Show(this, "将写入已预览的特技参数，并自动创建备份和记录。", "确认写入", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            try
            {
                new EffectReleaseConsistencyService().EnsureWriteAllowed();
                var instance = GetSelectedLogicalEffect(grid, visibleInstances);
                if (instance?.PatchCategory == "ModularSemanticEffectV2" && _currentCompositeMaintenancePreview?.CanApply == true)
                {
                    var result = new ModularEffectLifecycleService().ApplyMaintenance(project, _currentCompositeMaintenancePreview.Package);
                    previewLabel.Text = result.SummaryZh;
                }
                else
                {
                    var result = new EffectPackageService().ApplyPatch(project, _currentEffectParameterPreview!.Package);
                    previewLabel.Text = result.Summary;
                }
                applyButton.Enabled = false;
                scanButton.PerformClick();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        toggleButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project)) return;
            var instance = GetSelectedLogicalEffect(grid, visibleInstances);
            if (instance == null || instance.PatchCategory is not "CompositeEffect" and not "ModularSemanticEffectV2") return;
            var enable = instance.InstallationStatus == CompositeInstallationStatus.Disabled;
            try
            {
                new EffectReleaseConsistencyService().EnsureWriteAllowed();
                _currentCompositeMaintenancePreview = instance.PatchCategory == "ModularSemanticEffectV2"
                    ? new ModularEffectLifecycleService().PreviewToggle(project, instance.WritableContractId, enable)
                    : new CompositeEffectService().PreviewToggle(project, instance.WritableContractId, enable);
                previewLabel.Text = _currentCompositeMaintenancePreview.SummaryZh;
                if (!_currentCompositeMaintenancePreview.CanApply) return;
                if (MessageBox.Show(this, enable ? "重新启用这个复合特效？" : "停用后保留名称、参数和绑定，是否继续？",
                        enable ? "确认启用" : "确认停用", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                if (instance.PatchCategory == "ModularSemanticEffectV2")
                    new ModularEffectLifecycleService().ApplyMaintenance(project, _currentCompositeMaintenancePreview.Package);
                else new CompositeEffectService().ApplyMaintenance(project, _currentCompositeMaintenancePreview.Package);
                scanButton.PerformClick();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        repairButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project)) return;
            var instance = GetSelectedLogicalEffect(grid, visibleInstances);
            if (instance == null) return;
            try
            {
                new EffectReleaseConsistencyService().EnsureWriteAllowed();
                _currentCompositeMaintenancePreview = instance.PatchCategory == "ModularSemanticEffectV2"
                    ? new ModularEffectLifecycleService().PreviewRepair(project, instance.WritableContractId)
                    : new CompositeEffectService().PreviewRepair(project, instance.WritableContractId);
                previewLabel.Text = _currentCompositeMaintenancePreview.SummaryZh;
                if (!_currentCompositeMaintenancePreview.CanApply) return;
                if (MessageBox.Show(this, "将恢复已确认退回安装前内容的锁定位置。", "确认修复", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                if (instance.PatchCategory == "ModularSemanticEffectV2")
                    new ModularEffectLifecycleService().ApplyMaintenance(project, _currentCompositeMaintenancePreview.Package);
                else new CompositeEffectService().ApplyMaintenance(project, _currentCompositeMaintenancePreview.Package);
                scanButton.PerformClick();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "修复失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        removeButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project)) return;
            var instance = GetSelectedLogicalEffect(grid, visibleInstances);
            if (instance == null) return;
            try
            {
                new EffectReleaseConsistencyService().EnsureWriteAllowed();
                var modularPreview = instance.PatchCategory == "ModularSemanticEffectV2"
                    ? new ModularEffectLifecycleService().PreviewRemove(project, instance.WritableContractId)
                    : null;
                var package = modularPreview?.Package ?? new CompositeEffectService().PreviewRemove(project, instance.WritableContractId);
                if (MessageBox.Show(this, "将恢复安装前内容并删除这个复合特效。", "确认删除", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                if (instance.PatchCategory == "ModularSemanticEffectV2")
                    new ModularEffectLifecycleService().ApplyMaintenance(project, package);
                else new CompositeEffectService().Remove(project, package);
                scanButton.PerformClick();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        return page;
    }

    private TabPage BuildNativeEffectConfigurationPage(string? fixedChannel = null, int? fixedEffectId = null, bool embedded = false)
    {
        var page = new TabPage(embedded
            ? fixedChannel == CompositeEffectChannel.Item ? "宝物配置" : "人物/兵种配置"
            : "原生配置");
        var releaseConsistency = new EffectReleaseConsistencyService().Read();
        var layout = CreateEffectInjectionPageLayout();
        page.Controls.Add(layout);
        var channelBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 138 };
        channelBox.Items.AddRange(["人物/兵种", "宝物"]);
        channelBox.SelectedIndex = fixedChannel == CompositeEffectChannel.Item ? 1 : 0;
        var idBox = new NumericUpDown { Minimum = 0, Maximum = 255, Hexadecimal = true, Width = 76 };
        if (fixedEffectId.HasValue) idBox.Value = fixedEffectId.Value;
        var readButton = new Button { Text = "读取" };
        ConfigureToolbarButton(readButton, 82);
        var previewButton = new Button { Text = "预览修改", Enabled = false };
        ConfigureToolbarButton(previewButton, 106);
        var applyButton = new Button { Text = "写入修改", Enabled = false };
        ConfigureToolbarButton(applyButton, 106);
        var status = CreateToolbarLabel($"请选择渠道和编号；组件：{releaseConsistency.StatusZh}", 0);
        status.AutoSize = true;
        var toolbar = CreateToolbarStack(1);
        if (embedded) AddToolbarRow(toolbar, 0, readButton, previewButton, applyButton, status);
        else AddToolbarRow(toolbar, 0, channelBox, idBox, readButton, previewButton, applyButton, status);
        layout.Controls.Add(toolbar, 0, 0);

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, RowCount = 6, Padding = new Padding(10) };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(editor, 0, 1);
        var nameBox = new TextBox { Dock = DockStyle.Top };
        var descriptionBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
        var stubBox = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(NativeEffectStubDefinition.DisplayNameZh) };
        var personalBox = new NumericUpDown { Minimum = 0, Maximum = 255, Hexadecimal = true, Width = 90 };
        var itemBox = new NumericUpDown { Minimum = 0, Maximum = 255, Hexadecimal = true, Width = 90 };
        var modePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var valueModeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        valueModeBox.Items.AddRange(["读取特效值", "只判断是否拥有"]);
        var stackingBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        stackingBox.Items.AddRange(["个人与宝物数值相加", "个人与宝物不相加", "宝物优先，个人作为备选"]);
        modePanel.Controls.AddRange([new Label { Text = "个人号", AutoSize = true, Margin = new Padding(0, 7, 4, 0) }, personalBox,
            new Label { Text = "宝物号", AutoSize = true, Margin = new Padding(12, 7, 4, 0) }, itemBox, valueModeBox, stackingBox]);
        var bindingPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        bindingPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bindingPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var bindingToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var addBindingButton = new Button { Text = "添加绑定", AutoSize = true };
        var removeBindingButton = new Button { Text = "删除选中", AutoSize = true };
        bindingToolbar.Controls.AddRange([addBindingButton, removeBindingButton]);
        var bindingGrid = CreateEffectInjectionGrid();
        bindingGrid.AllowUserToAddRows = false;
        bindingGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        bindingGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "类型", HeaderText = "绑定类型", FlatStyle = FlatStyle.Flat,
            DisplayMember = nameof(NativeBindingKindOption.DisplayNameZh),
            ValueMember = nameof(NativeBindingKindOption.Kind),
            DataSource = NativeBindingKindOption.All
        });
        bindingGrid.Columns.Add("武将1", "武将1");
        bindingGrid.Columns.Add("武将2", "武将2");
        bindingGrid.Columns.Add("武将3", "武将3");
        bindingGrid.Columns.Add("兵种", "兵种");
        bindingGrid.Columns.Add("物品1", "物品1");
        bindingGrid.Columns.Add("物品2", "物品2");
        bindingGrid.Columns.Add("物品3", "物品3");
        bindingGrid.Columns.Add("特效值", "特效值");
        bindingGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "删除", HeaderText = "删除" });
        bindingGrid.Columns["类型"].Width = 120;
        foreach (DataGridViewColumn column in bindingGrid.Columns.Cast<DataGridViewColumn>().Where(column => column.Name != "类型"))
            column.Width = 78;
        bindingGrid.DataError += (_, e) => e.ThrowException = false;
        bindingPanel.Controls.Add(bindingToolbar, 0, 0);
        bindingPanel.Controls.Add(bindingGrid, 0, 1);
        var impactBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Window, ScrollBars = ScrollBars.Vertical };
        AddEditorRow(0, "名称", nameBox);
        AddEditorRow(1, "说明", descriptionBox);
        AddEditorRow(2, "原生判定", stubBox);
        AddEditorRow(3, "判定参数", modePanel);
        AddEditorRow(4, "当前配置", bindingPanel);
        AddEditorRow(5, "影响范围", impactBox);
        NativeEffectDefinition? definition = null;
        NativeEffectEditPreview? currentPreview = null;

        NativeEffectFieldCapability? Capability(string id)
            => definition?.FieldCapabilities.FirstOrDefault(item => item.FieldId.Equals(id, StringComparison.OrdinalIgnoreCase));

        void AddEditorRow(int row, string title, Control control)
        {
            editor.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 7, 6, 0) }, 0, row);
            editor.Controls.Add(control, 1, row);
        }

        void RefreshStub()
        {
            var stub = stubBox.SelectedItem as NativeEffectStubDefinition;
            if (stub == null)
            {
                modePanel.Enabled = false;
                impactBox.Text = definition?.EditabilityReasonZh ?? string.Empty;
                return;
            }
            var personalCapability = Capability("personal:" + stub.InstanceId);
            var itemCapability = Capability("item:" + stub.InstanceId);
            var valueModeCapability = Capability("value-mode:" + stub.InstanceId);
            var stackingCapability = Capability("stacking:" + stub.InstanceId);
            personalBox.Enabled = personalCapability?.CanEdit == true;
            itemBox.Enabled = itemCapability?.CanEdit == true;
            valueModeBox.Enabled = valueModeCapability?.CanEdit == true;
            stackingBox.Enabled = stackingCapability?.CanEdit == true;
            modePanel.Enabled = new[] { personalCapability, itemCapability, valueModeCapability, stackingCapability }.Any(item => item?.CanEdit == true);
            personalBox.Value = stub.PersonalEffectId ?? 0;
            itemBox.Value = stub.ItemEffectId ?? 0;
            valueModeBox.SelectedIndex = Math.Clamp(stub.EffectValueMode ?? 1, 0, 1);
            stackingBox.SelectedIndex = Math.Clamp(stub.StackingMode ?? 1, 0, 2);
            impactBox.Text = string.Join(Environment.NewLine,
                new[] { personalCapability, itemCapability, valueModeCapability, stackingCapability }
                    .Where(item => item != null)
                    .Select(item => $"{item!.DisplayNameZh}：{item.WriteCapabilityZh}。{item.ReasonZh}")) +
                Environment.NewLine + $"该判定会影响 {stub.CallSites.Count} 个已确认调用环节。";
        }

        void RefreshBindings()
        {
            bindingGrid.Rows.Clear();
            foreach (var reference in definition?.Bindings ?? [])
            {
                var binding = reference.PackageBinding;
                if (binding == null) continue;
                bindingGrid.Rows.Add(binding.Kind, binding.PersonId, binding.PersonId2, binding.PersonId3, binding.JobId,
                    binding.ItemId, binding.ItemId2, binding.ItemId3, binding.EffectValue ?? reference.EffectValue, false);
            }
        }

        addBindingButton.Click += (_, _) =>
        {
            var kind = definition?.Channel == CompositeEffectChannel.Item ? "item" : "job_assignment";
            bindingGrid.Rows.Add(kind, null, null, null, null, null, null, null, 0, false);
        };
        removeBindingButton.Click += (_, _) =>
        {
            foreach (DataGridViewRow row in bindingGrid.SelectedRows) row.Cells["删除"].Value = true;
        };

        readButton.Click += async (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project)) return;
            var channel = channelBox.SelectedIndex == 1 ? CompositeEffectChannel.Item : CompositeEffectChannel.PersonalJob;
            var effectId = decimal.ToInt32(idBox.Value);
            var projectKey = ProjectPatchIdentityService.NormalizePath(project.GameRoot);
            var featureKey = "native-effect-configuration:" + channel;
            status.Text = "正在读取表并分析 EXE…";
            currentPreview = null;
            applyButton.Enabled = false;
            readButton.Enabled = false;
            try
            {
                await _asyncLoadCoordinator.RunLatestAsync(
                    featureKey,
                    projectKey,
                    cancellation => new NativeEffectConfigurationService().ReadAsync(project, channel, effectId, cancellation),
                    loaded =>
                    {
                        if (page.IsDisposed || IsDisposed || !TryGetEffectInjectionProject(out var currentProject) ||
                            !ProjectPatchIdentityService.NormalizePath(currentProject.GameRoot).Equals(projectKey, StringComparison.OrdinalIgnoreCase))
                            return Task.CompletedTask;
                        definition = loaded;
                        nameBox.Text = string.IsNullOrWhiteSpace(definition.GameName) ? definition.Name : definition.GameName;
                        descriptionBox.Text = definition.Description;
                        stubBox.DataSource = definition.Stubs.ToList();
                        var nameCapability = Capability("name");
                        var descriptionCapability = Capability("description");
                        var bindingCapability = Capability("bindings");
                        nameBox.Enabled = nameCapability?.CanEdit == true;
                        descriptionBox.Enabled = descriptionCapability?.CanEdit == true;
                        bindingPanel.Enabled = bindingCapability?.CanEdit == true;
                        previewButton.Enabled = definition.FieldCapabilities.Any(item => item.CanEdit);
                        applyButton.Enabled = false;
                        status.Text = string.Join("；", definition.FieldCapabilities
                            .Where(item => item.FieldId is "name" or "description" or "bindings")
                            .Select(item => $"{item.DisplayNameZh}{item.WriteCapabilityZh}")) +
                            (definition.Channel == CompositeEffectChannel.PersonalJob
                                ? "；扩展绑定：" + definition.ExtendedBindingCapability.StatusZh
                                : string.Empty);
                        RefreshStub();
                        RefreshBindings();
                        return Task.CompletedTask;
                    });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { if (!IsDisposed) readButton.Enabled = true; }
        };
        channelBox.SelectedIndexChanged += (_, _) =>
        {
            _asyncLoadCoordinator.Cancel("native-effect-configuration:" + CompositeEffectChannel.PersonalJob);
            _asyncLoadCoordinator.Cancel("native-effect-configuration:" + CompositeEffectChannel.Item);
        };
        idBox.ValueChanged += (_, _) => _asyncLoadCoordinator.Cancel("native-effect-configuration:" +
            (channelBox.SelectedIndex == 1 ? CompositeEffectChannel.Item : CompositeEffectChannel.PersonalJob));
        stubBox.SelectedIndexChanged += (_, _) => RefreshStub();
        page.Disposed += (_, _) =>
        {
            _asyncLoadCoordinator.Cancel("native-effect-configuration:" + CompositeEffectChannel.PersonalJob);
            _asyncLoadCoordinator.Cancel("native-effect-configuration:" + CompositeEffectChannel.Item);
        };
        previewButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project) || definition == null) return;
            var stub = stubBox.SelectedItem as NativeEffectStubDefinition;
            var bindingDrafts = bindingGrid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow)
                .Select(row => new EffectPackageBinding
                {
                    Kind = Convert.ToString(row.Cells["类型"].Value, CultureInfo.InvariantCulture) ?? string.Empty,
                    PersonId = ParseOptionalGridInt(row.Cells["武将1"].Value),
                    PersonId2 = ParseOptionalGridInt(row.Cells["武将2"].Value),
                    PersonId3 = ParseOptionalGridInt(row.Cells["武将3"].Value),
                    JobId = ParseOptionalGridInt(row.Cells["兵种"].Value),
                    ItemId = ParseOptionalGridInt(row.Cells["物品1"].Value),
                    ItemId2 = ParseOptionalGridInt(row.Cells["物品2"].Value),
                    ItemId3 = ParseOptionalGridInt(row.Cells["物品3"].Value),
                    EffectValue = ParseOptionalGridInt(row.Cells["特效值"].Value),
                    Remove = row.Cells["删除"].Value is true
                }).ToList();
            currentPreview = new NativeEffectConfigurationService().Preview(project, new NativeEffectEditDraft
            {
                Channel = definition.Channel, EffectId = definition.EffectId, Name = nameBox.Text, Description = descriptionBox.Text,
                InstanceId = stub?.InstanceId,
                PersonalEffectId = stub != null && Capability("personal:" + stub.InstanceId)?.CanEdit == true ? decimal.ToInt32(personalBox.Value) : null,
                ItemEffectId = stub != null && Capability("item:" + stub.InstanceId)?.CanEdit == true ? decimal.ToInt32(itemBox.Value) : null,
                EffectValueMode = stub != null && Capability("value-mode:" + stub.InstanceId)?.CanEdit == true ? valueModeBox.SelectedIndex : null,
                StackingMode = stub != null && Capability("stacking:" + stub.InstanceId)?.CanEdit == true ? stackingBox.SelectedIndex : null,
                ReplaceAllBindings = true,
                Bindings = bindingDrafts
            });
            status.Text = currentPreview.SummaryZh;
            if (currentPreview.ExtendedBindingPreview?.RequiresExtension == true)
                impactBox.Text = currentPreview.ExtendedBindingPreview.SummaryZh + Environment.NewLine +
                                 string.Join(Environment.NewLine, currentPreview.ExtendedBindingPreview.WarningsZh);
            applyButton.Enabled = new EffectReleaseConsistencyService().Read(forceRefresh: true).CanWrite && currentPreview.CanApply;
        };
        applyButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project) || currentPreview?.CanApply != true) return;
            if (MessageBox.Show(this, "将写入已预览的原生特效配置，并统一备份所有目标文件。", "确认写入", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            try
            {
                new EffectReleaseConsistencyService().EnsureWriteAllowed();
                var result = new NativeEffectConfigurationService().Apply(project, currentPreview.Package);
                EffectInventoryService.Invalidate(project);
                status.Text = result.SummaryZh;
                applyButton.Enabled = false;
                readButton.PerformClick();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        if (embedded)
        {
            page.HandleCreated += (_, _) => BeginInvoke((Action)(() =>
            {
                if (!page.IsDisposed) readButton.PerformClick();
            }));
        }
        return page;
    }

    private static int? ParseOptionalGridInt(object? value)
        => int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private sealed record NativeBindingKindOption(string Kind, string DisplayNameZh)
    {
        public static IReadOnlyList<NativeBindingKindOption> All { get; } =
        [
            new("job_assignment", "武将/兵种分配"),
            new("person_item_1", "人物装备专属一"),
            new("person_item_2", "人物装备专属二"),
            new("set_3", "三件套"),
            new("set_4", "四件套"),
            new("item", "物品绑定")
        ];
    }

    private TabPage BuildCompositeEffectPage()
    {
        var page = new TabPage("复合制作");
        var releaseConsistency = new EffectReleaseConsistencyService().Read();
        var layout = CreateEffectInjectionPageLayout();
        page.Controls.Add(layout);
        var scanButton = new Button { Text = "刷新可用特效" };
        ConfigureToolbarButton(scanButton, 124);
        var channelBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 138 };
        channelBox.Items.AddRange(["人物/兵种", "宝物"]);
        channelBox.SelectedIndex = 0;
        var recipeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        var researchBox = new CheckBox { Text = "显示研究中", AutoSize = true, Margin = new Padding(6, 6, 2, 0) };
        var idBox = new TextBox { Width = 58, MaxLength = 2, PlaceholderText = "自动" };
        var selectIdButton = new Button { Text = "选择编号", AutoSize = true };
        var nameBox = new TextBox { Width = 180, PlaceholderText = "复合特效名称" };
        var previewButton = new Button { Text = "预览", Enabled = false };
        ConfigureToolbarButton(previewButton, 142);
        var applyButton = new Button { Text = "写入", Enabled = false, Visible = true };
        ConfigureToolbarButton(applyButton, 88);
        var status = new EffectStageControl
        {
            Text = $"请先读取可用成员；组件：{releaseConsistency.StatusZh}",
            BackColor = SystemColors.Control,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 4, 3, 2)
        };
        var toolbar = CreateToolbarStack(1);
        AddToolbarRow(toolbar, 0, scanButton, recipeBox, researchBox, channelBox, idBox, selectIdButton, nameBox, previewButton, applyButton);
        layout.RowCount = 3;
        layout.RowStyles.Clear();
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(status, 0, 1);
        var split = CreateResizableSplit("EffectInjection.Composite.MainSplit", Orientation.Vertical, 680, 360, 320);
        layout.Controls.Add(split, 0, 2);
        var memberGrid = CreateEffectInjectionGrid();
        memberGrid.AutoGenerateColumns = false;
        memberGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        memberGrid.AllowUserToAddRows = false;
        memberGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "选择", HeaderText = "选择", DataPropertyName = "选择", FillWeight = 15 });
        memberGrid.Columns.Add("索引", "索引");
        memberGrid.Columns["索引"].Visible = false;
        memberGrid.Columns.Add("名称", "名称");
        memberGrid.Columns.Add("触发时机", "触发时机");
        memberGrid.Columns.Add("实际效果", "实际效果");
        memberGrid.Columns.Add("模块标签", "模块标签");
        memberGrid.Columns.Add("兼容性", "兼容性");
        memberGrid.Columns.Add("原因", "原因");
        foreach (DataGridViewColumn column in memberGrid.Columns.Cast<DataGridViewColumn>().Where(column => column.Name != "选择"))
            column.DataPropertyName = column.Name;
        memberGrid.DataError += (_, e) => e.ThrowException = false;
        split.Panel1.Controls.Add(memberGrid);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        split.Panel2.Controls.Add(right);
        var modulePanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, ColumnCount = 4, RowCount = 3, Margin = new Padding(0, 0, 0, 6) };
        modulePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        modulePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        modulePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        modulePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var triggerModuleBox = CreateModuleComboBox();
        var subjectModuleBox = CreateModuleComboBox();
        var targetModuleBox = CreateModuleComboBox();
        var actionModuleBox = CreateModuleComboBox();
        var valueModuleBox = CreateModuleComboBox();
        triggerModuleBox.Items.Add(new ModuleOption(string.Empty, "不指定", true, string.Empty));
        subjectModuleBox.Items.Add(new ModuleOption(string.Empty, "不指定", true, string.Empty));
        targetModuleBox.Items.Add(new ModuleOption(string.Empty, "不指定", true, string.Empty));
        actionModuleBox.Items.Add(new ModuleOption("action.compose-existing", "叠加现有特效", true, string.Empty));
        valueModuleBox.Items.Add(new ModuleOption("value.fixed", "固定值", true, string.Empty));
        triggerModuleBox.SelectedIndex = subjectModuleBox.SelectedIndex = targetModuleBox.SelectedIndex = 0;
        actionModuleBox.SelectedIndex = valueModuleBox.SelectedIndex = 0;
        var effectValueInput = new NumericUpDown { Minimum = 0, Maximum = 65535, Value = 10, Dock = DockStyle.Fill, Margin = new Padding(3, 2, 8, 2) };
        AddModuleSelector(modulePanel, 0, 0, "触发时机", triggerModuleBox);
        AddModuleSelector(modulePanel, 2, 0, "判定对象", subjectModuleBox);
        AddModuleSelector(modulePanel, 0, 1, "作用目标", targetModuleBox);
        AddModuleSelector(modulePanel, 2, 1, "实际效果", actionModuleBox);
        AddModuleSelector(modulePanel, 0, 2, "数值方式", valueModuleBox);
        AddModuleSelector(modulePanel, 2, 2, "效果数值", effectValueInput);
        var descriptionBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, PlaceholderText = "复合特效说明", ScrollBars = ScrollBars.Vertical };
        var parameterGrid = CreateEffectInjectionGrid();
        parameterGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        parameterGrid.Columns.Add("实例", "成员实例");
        parameterGrid.Columns["实例"].Visible = false;
        parameterGrid.Columns.Add("成员", "成员");
        parameterGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "数值", HeaderText = "复合数值" });
        var compositeBindingPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        compositeBindingPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        compositeBindingPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var compositeBindingToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var addCompositeBindingButton = new Button { Text = "添加绑定", AutoSize = true };
        var removeCompositeBindingButton = new Button { Text = "删除选中", AutoSize = true };
        compositeBindingToolbar.Controls.AddRange([addCompositeBindingButton, removeCompositeBindingButton]);
        var compositeBindingGrid = CreateEffectInjectionGrid();
        compositeBindingGrid.AllowUserToAddRows = false;
        compositeBindingGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        compositeBindingGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "类型", HeaderText = "绑定类型", FlatStyle = FlatStyle.Flat,
            DisplayMember = nameof(NativeBindingKindOption.DisplayNameZh),
            ValueMember = nameof(NativeBindingKindOption.Kind),
            DataSource = NativeBindingKindOption.All
        });
        compositeBindingGrid.Columns.Add("武将1", "武将1");
        compositeBindingGrid.Columns.Add("武将2", "武将2");
        compositeBindingGrid.Columns.Add("武将3", "武将3");
        compositeBindingGrid.Columns.Add("兵种", "兵种");
        compositeBindingGrid.Columns.Add("物品1", "物品1");
        compositeBindingGrid.Columns.Add("物品2", "物品2");
        compositeBindingGrid.Columns.Add("物品3", "物品3");
        compositeBindingGrid.Columns.Add("特效值", "特效值");
        compositeBindingGrid.DataError += (_, e) => e.ThrowException = false;
        compositeBindingPanel.Controls.Add(compositeBindingToolbar, 0, 0);
        compositeBindingPanel.Controls.Add(compositeBindingGrid, 0, 1);
        var resultBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Window, ScrollBars = ScrollBars.Vertical };
        right.Controls.Add(modulePanel, 0, 0);
        right.Controls.Add(descriptionBox, 0, 1);
        right.Controls.Add(parameterGrid, 0, 2);
        right.Controls.Add(compositeBindingPanel, 0, 3);
        right.Controls.Add(resultBox, 0, 4);
        var members = new List<LogicalEffectInstance>();
        EffectModuleCatalog? moduleCatalog = null;
        IReadOnlyDictionary<string, CompositeEffectCompatibility> compatibilityByInstance =
            new Dictionary<string, CompositeEffectCompatibility>(StringComparer.OrdinalIgnoreCase);
        var freeIdsByChannel = new Dictionary<string, FreeEffectIdReport>(StringComparer.OrdinalIgnoreCase);
        var freeIdOptionsByChannel = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var availableIdOptions = new List<string>();
        var boundModuleBoxes = new HashSet<ComboBox>();
        var recipeOptionsExpanded = false;

        void BindModuleOnDemand(ComboBox box, string kind, bool allowEmpty)
        {
            box.DropDown += (_, _) =>
            {
                if (moduleCatalog == null || boundModuleBoxes.Contains(box)) return;
                var selected = SelectedModuleId(box);
                BindModuleOptions(box, moduleCatalog, kind, allowEmpty);
                if (!string.IsNullOrWhiteSpace(selected)) SelectModule(box, selected);
                boundModuleBoxes.Add(box);
            };
        }
        BindModuleOnDemand(triggerModuleBox, EffectModuleKind.Trigger, allowEmpty: true);
        BindModuleOnDemand(subjectModuleBox, EffectModuleKind.Subject, allowEmpty: true);
        BindModuleOnDemand(targetModuleBox, EffectModuleKind.Target, allowEmpty: true);
        BindModuleOnDemand(actionModuleBox, EffectModuleKind.Action, allowEmpty: false);
        BindModuleOnDemand(valueModuleBox, EffectModuleKind.Value, allowEmpty: false);

        void RefreshRecipes(bool expandAll = false)
        {
            if (moduleCatalog == null) return;
            var selectedRecipeId = (recipeBox.SelectedItem as ModuleRecipeOption)?.RecipeId;
            var recipes = moduleCatalog.Recipes.Where(item => researchBox.Checked || item.IsAvailable)
                .Select(item => new ModuleRecipeOption(item.RecipeId, item.DisplayNameZh, item.IsAvailable, item.ReasonZh)).ToList();
            var visibleRecipes = expandAll ? recipes : recipes.Take(1).ToList();
            recipeBox.BeginUpdate();
            try
            {
                recipeBox.DataSource = null;
                recipeBox.Items.Clear();
                recipeBox.Items.AddRange(visibleRecipes.Cast<object>().ToArray());
                var selectedIndex = visibleRecipes.FindIndex(item => item.RecipeId.Equals(selectedRecipeId, StringComparison.Ordinal));
                if (recipeBox.Items.Count > 0) recipeBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            }
            finally { recipeBox.EndUpdate(); }
            recipeOptionsExpanded = expandAll;
        }

        void RefreshFreeIds()
        {
            var channel = channelBox.SelectedIndex == 1 ? CompositeEffectChannel.Item : CompositeEffectChannel.PersonalJob;
            if (!freeIdsByChannel.TryGetValue(channel, out var report))
            {
                availableIdOptions.Clear();
                previewButton.Enabled = false;
                return;
            }
            availableIdOptions = freeIdOptionsByChannel.GetValueOrDefault(channel) ?? [];
            var recommended = availableIdOptions.FirstOrDefault()?[..2] ?? "无";
            using (PerformanceMetrics.Begin("EffectWorkbench.CompositeStatusText"))
                status.Text = report.SummaryZh + "；推荐编号 " + recommended;
        }

        recipeBox.DropDown += (_, _) =>
        {
            if (!recipeOptionsExpanded) RefreshRecipes(expandAll: true);
        };

        selectIdButton.Click += (_, _) =>
        {
            if (availableIdOptions.Count == 0) return;
            using var dialog = new Form
            {
                Text = "选择复合特效编号",
                Width = 520,
                Height = 560,
                StartPosition = FormStartPosition.CenterParent
            };
            var list = new ListBox { Dock = DockStyle.Fill };
            list.Items.AddRange(availableIdOptions.Cast<object>().ToArray());
            var confirm = new Button { Text = "确定", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 34 };
            dialog.Controls.Add(list);
            dialog.Controls.Add(confirm);
            dialog.AcceptButton = confirm;
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            list.DoubleClick += (_, _) => dialog.DialogResult = DialogResult.OK;
            if (dialog.ShowDialog(this) == DialogResult.OK && list.SelectedItem != null)
                idBox.Text = Convert.ToString(list.SelectedItem, CultureInfo.InvariantCulture)?[..2] ?? string.Empty;
        };

        scanButton.Click += async (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project)) return;
            var projectKey = ProjectPatchIdentityService.NormalizePath(project.GameRoot);
            IProgress<string> progress = new Progress<string>(stage =>
            {
                if (!IsDisposed) status.Text = stage + "……";
            });
            scanButton.Enabled = false;
            previewButton.Enabled = false;
            applyButton.Enabled = false;
            try
            {
                await _asyncLoadCoordinator.RunLatestAsync(
                    "composite-effect-scan",
                    projectKey,
                    async cancellation =>
                    {
                        var snapshot = await EffectAnalysisCoordinator.Shared.ScanAsync(project, "Ekd5.exe", progress, cancellation);
                        cancellation.ThrowIfCancellationRequested();
                        progress.Report("评估复合成员");
                        var compatibility = new CompositeEffectMemberCompatibilityService()
                            .EvaluateBatch(project, snapshot.Inventory, snapshot.MechanismProfile);
                        cancellation.ThrowIfCancellationRequested();
                        progress.Report("查找双渠道空闲编号");
                        var service = new CompositeEffectService();
                        var personalFree = service.FindFreeIds(project, CompositeEffectChannel.PersonalJob, snapshot.Inventory);
                        var itemFree = service.FindFreeIds(project, CompositeEffectChannel.Item, snapshot.Inventory);
                        var members = snapshot.Inventory.Effects.ToList();
                        var memberTable = BuildCompositeMemberTable(snapshot, compatibility, members);
                        return new CompositeWorkbenchLoad(
                            snapshot,
                            compatibility,
                            personalFree,
                            itemFree,
                            members,
                            memberTable,
                            BuildFreeEffectIdOptions(personalFree),
                            BuildFreeEffectIdOptions(itemFree));
                    },
                    async loaded =>
                    {
                        using var commitTiming = PerformanceMetrics.Begin("EffectWorkbench.CompositeUiCommit");
                        if (IsDisposed || !TryGetEffectInjectionProject(out var currentProject) ||
                            !ProjectPatchIdentityService.NormalizePath(currentProject.GameRoot).Equals(projectKey, StringComparison.OrdinalIgnoreCase))
                            return;
                        _currentEffectAnalysisSnapshot = loaded.Snapshot;
                        _currentEffectInventory = loaded.Snapshot.Inventory;
                        moduleCatalog = loaded.Snapshot.ModuleCatalog;
                        boundModuleBoxes.Clear();
                        compatibilityByInstance = loaded.Compatibility;
                        freeIdsByChannel = new Dictionary<string, FreeEffectIdReport>(StringComparer.OrdinalIgnoreCase)
                        {
                            [CompositeEffectChannel.PersonalJob] = loaded.PersonalFreeIds,
                            [CompositeEffectChannel.Item] = loaded.ItemFreeIds
                        };
                        freeIdOptionsByChannel = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                        {
                            [CompositeEffectChannel.PersonalJob] = loaded.PersonalFreeIdOptions,
                            [CompositeEffectChannel.Item] = loaded.ItemFreeIdOptions
                        };
                        using (PerformanceMetrics.Begin("EffectWorkbench.CompositeModuleBind"))
                        {
                            using (PerformanceMetrics.Begin("EffectWorkbench.CompositeRecipeBind")) RefreshRecipes();
                        }
                        await Task.Delay(30);
                        members = loaded.Members;
                        using (PerformanceMetrics.Begin("EffectWorkbench.CompositeGridBind"))
                        {
                            memberGrid.SuspendLayout();
                            try
                            {
                                memberGrid.DataSource = loaded.MemberTable;
                            }
                            finally { memberGrid.ResumeLayout(); }
                        }
                        await Task.Delay(30);
                        using (PerformanceMetrics.Begin("EffectWorkbench.CompositeFreeIdBind")) RefreshFreeIds();
                        await Task.Delay(30);
                        var available = compatibilityByInstance.Values.Count(item => item.IsCompatible);
                        status.Text = $"扫描完成：成员 {members.Count} 个，可加入 {available} 个；{loaded.Snapshot.CacheState}";
                        previewButton.Enabled = availableIdOptions.Count > 0 && recipeBox.Items.Count > 0;
                    });
            }
            catch (Exception ex)
            {
                status.Text = "扫描失败：" + ex.Message;
                MessageBox.Show(this, ex.Message, "复合特效扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed) scanButton.Enabled = true;
            }
        };
        researchBox.CheckedChanged += (_, _) => RefreshRecipes(recipeOptionsExpanded);
        channelBox.SelectedIndexChanged += (_, _) =>
        {
            compositeBindingGrid.Rows.Clear();
            RefreshFreeIds();
        };
        addCompositeBindingButton.Click += (_, _) =>
        {
            var kind = channelBox.SelectedIndex == 1 ? "item" : "job_assignment";
            compositeBindingGrid.Rows.Add(kind, null, null, null, null, null, null, null, 0);
        };
        removeCompositeBindingButton.Click += (_, _) =>
        {
            foreach (DataGridViewRow row in compositeBindingGrid.SelectedRows)
                compositeBindingGrid.Rows.Remove(row);
        };
        memberGrid.CellValueChanged += (_, e) =>
        {
            if (e.ColumnIndex != memberGrid.Columns["选择"].Index) return;
            parameterGrid.Rows.Clear();
            foreach (DataGridViewRow row in memberGrid.Rows)
            {
                if (row.Cells["选择"].Value is not true) continue;
                var index = Convert.ToInt32(row.Cells["索引"].Value, CultureInfo.InvariantCulture);
                var member = members[index];
                var defaultValue = member.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.EffectValue)?.Value == 0 ? 1 : 1;
                parameterGrid.Rows.Add(member.InstanceId, member.Name, defaultValue);
            }
        };
        memberGrid.CurrentCellDirtyStateChanged += (_, _) => { if (memberGrid.IsCurrentCellDirty) memberGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        memberGrid.CellBeginEdit += (_, e) =>
        {
            if (e.ColumnIndex == memberGrid.Columns["选择"].Index &&
                Convert.ToString(memberGrid.Rows[e.RowIndex].Cells["兼容性"].Value, CultureInfo.InvariantCulture) == "不可加入")
                e.Cancel = true;
        };
        page.Disposed += (_, _) => _asyncLoadCoordinator.Cancel("composite-effect-scan");
        previewButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project)) return;
            var idText = idBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(idText) && availableIdOptions.Count > 0) idText = availableIdOptions[0][..2];
            if (idText.Length < 2 || !int.TryParse(idText[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var selectedEffectId))
            {
                MessageBox.Show(this, "请输入候选列表中的两位十六进制特效号。", "编号无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var selected = parameterGrid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).Select(row => new CompositeEffectMember
            {
                InstanceId = Convert.ToString(row.Cells["实例"].Value, CultureInfo.InvariantCulture) ?? string.Empty,
                EffectValue = int.TryParse(Convert.ToString(row.Cells["数值"].Value, CultureInfo.InvariantCulture), out var value) ? value : 1
            }).ToList();
            var recipe = recipeBox.SelectedItem as ModuleRecipeOption;
            var bindings = compositeBindingGrid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow)
                .Select(row => new EffectPackageBinding
                {
                    Kind = Convert.ToString(row.Cells["类型"].Value, CultureInfo.InvariantCulture) ?? string.Empty,
                    PersonId = ParseOptionalGridInt(row.Cells["武将1"].Value),
                    PersonId2 = ParseOptionalGridInt(row.Cells["武将2"].Value),
                    PersonId3 = ParseOptionalGridInt(row.Cells["武将3"].Value),
                    JobId = ParseOptionalGridInt(row.Cells["兵种"].Value),
                    ItemId = ParseOptionalGridInt(row.Cells["物品1"].Value),
                    ItemId2 = ParseOptionalGridInt(row.Cells["物品2"].Value),
                    ItemId3 = ParseOptionalGridInt(row.Cells["物品3"].Value),
                    EffectValue = ParseOptionalGridInt(row.Cells["特效值"].Value)
                }).ToList();
            var blueprint = new ModularCompositeEffectBlueprint
            {
                BlueprintId = "modular-" + Guid.NewGuid().ToString("N"), RecipeId = recipe?.RecipeId ?? "recipe.compose-existing-effects",
                Channel = channelBox.SelectedIndex == 1 ? CompositeEffectChannel.Item : CompositeEffectChannel.PersonalJob,
                EffectId = selectedEffectId,
                Name = nameBox.Text, Description = descriptionBox.Text,
                TriggerModuleId = SelectedModuleId(triggerModuleBox), SubjectModuleId = SelectedModuleId(subjectModuleBox),
                TargetModuleId = SelectedModuleId(targetModuleBox), ConditionModuleIds = ["condition.personal-or-item"],
                ActionModuleId = SelectedModuleId(actionModuleBox), ValueModuleId = SelectedModuleId(valueModuleBox),
                Value = decimal.ToInt32(effectValueInput.Value),
                BindingModuleIds = [channelBox.SelectedIndex == 1 ? "binding.item" : "binding.personal-job"],
                SafetyModuleId = "safety.direct-core", Members = selected, Bindings = bindings
            };
            _currentModularEffectPreview = new ModularEffectAuthoringService().Preview(project, blueprint);
            _currentCompositeEffectPreview = _currentModularEffectPreview.CompositePreview;
            var memberSummary = _currentModularEffectPreview.CompositePreview.Members.Count == 0
                ? string.Empty
                : Environment.NewLine + string.Join(Environment.NewLine,
                    _currentModularEffectPreview.CompositePreview.Members.Select(item => $"{item.DisplayNameZh}：{item.ReasonZh}"));
            resultBox.Text = _currentModularEffectPreview.SummaryZh + Environment.NewLine +
                              (_currentModularEffectPreview.Validation.CompiledSemanticBody?.MeaningZh ?? string.Empty) + memberSummary;
            var currentRelease = new EffectReleaseConsistencyService().Read(forceRefresh: true);
            applyButton.Enabled = _currentModularEffectPreview.CanApply && currentRelease.CanWrite;
            if (_currentModularEffectPreview.CanApply && !currentRelease.CanWrite)
            {
                resultBox.AppendText(Environment.NewLine + "组件不一致，当前只完成预览，禁止写入：" + currentRelease.ReasonZh);
            }
        };
        applyButton.Click += (_, _) =>
        {
            if (!TryGetEffectInjectionProject(out var project) || _currentModularEffectPreview?.CanApply != true) return;
            if (MessageBox.Show(this, "将写入已预览并锁定的复合特效事务。", "确认写入",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            ApplyModularPreview(project, resultBox, applyButton);
        };
        return page;

        void ApplyModularPreview(CczProject project, TextBox output, Button trigger)
        {
            try
            {
                new EffectReleaseConsistencyService().EnsureWriteAllowed();
                var result = new ModularEffectAuthoringService().Apply(project, _currentModularEffectPreview!.Package);
                EffectInventoryService.Invalidate(project);
                output.Text = result.SummaryZh;
                trigger.Enabled = false;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "注入失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    private sealed record CompositeWorkbenchLoad(
        EffectAnalysisSnapshot Snapshot,
        IReadOnlyDictionary<string, CompositeEffectCompatibility> Compatibility,
        FreeEffectIdReport PersonalFreeIds,
        FreeEffectIdReport ItemFreeIds,
        List<LogicalEffectInstance> Members,
        DataTable MemberTable,
        List<string> PersonalFreeIdOptions,
        List<string> ItemFreeIdOptions);

    private static DataTable BuildCompositeMemberTable(
        EffectAnalysisSnapshot snapshot,
        IReadOnlyDictionary<string, CompositeEffectCompatibility> compatibilityByInstance,
        IReadOnlyList<LogicalEffectInstance> members)
    {
        using var timing = PerformanceMetrics.Begin("EffectWorkbench.CompositeRowBuild");
        var tagsByInstance = snapshot.ModuleCatalog.InstanceTags
            .ToDictionary(item => item.InstanceId, item => item.SummaryZh, StringComparer.OrdinalIgnoreCase);
        var table = new DataTable();
        table.Columns.Add("选择", typeof(bool));
        foreach (var name in new[] { "索引", "名称", "触发时机", "实际效果", "模块标签", "兼容性", "原因" }) table.Columns.Add(name);
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var memberCompatibility = compatibilityByInstance[member.InstanceId];
            var compatibility = memberCompatibility.IsCompatible
                ? memberCompatibility.CompatibilityKind switch
                {
                    EffectMemberCompatibilityKind.VerifiedComplexFamily => "家族契约可用",
                    EffectMemberCompatibilityKind.VerifiedWrapper => "包装链可用",
                    _ => "直接可用"
                }
                : "不可加入";
            var tags = tagsByInstance.GetValueOrDefault(member.InstanceId) ?? "尚未完整解析";
            table.Rows.Add(false, i.ToString(CultureInfo.InvariantCulture), member.Name, member.TriggerPhase,
                member.NaturalLanguageDescription, tags, compatibility, memberCompatibility.ReasonZh);
        }

        return table;
    }

    private static List<string> BuildFreeEffectIdOptions(FreeEffectIdReport report)
        => report.FreeIds.Select(id => $"{id:X2}")
            .Concat(report.ReclaimableIds.Select(id => $"{id:X2}（替换未引用的“{report.ReclaimableNames[id]}”）"))
            .ToList();

    private sealed class EffectStageControl : Control
    {
        private string _displayText = string.Empty;

        public EffectStageControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string Text
        {
            get => _displayText;
            set
            {
                value ??= string.Empty;
                if (_displayText.Equals(value, StringComparison.Ordinal)) return;
                _displayText = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            TextRenderer.DrawText(e.Graphics, _displayText, Font, ClientRectangle, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    private sealed record ModuleRecipeOption(string RecipeId, string NameZh, bool IsAvailable, string ReasonZh)
    {
        public string DisplayText => IsAvailable ? NameZh : NameZh + "（待验证）";
        public override string ToString() => DisplayText;
    }

    private sealed record ModuleOption(string ModuleId, string DisplayNameZh, bool IsAvailable, string ReasonZh)
    {
        public string DisplayText => string.IsNullOrWhiteSpace(ModuleId) ? "不指定" : IsAvailable ? DisplayNameZh : DisplayNameZh + "（待验证）";
        public override string ToString() => DisplayText;
    }

    private static ComboBox CreateModuleComboBox()
        => new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, MinimumSize = new Size(120, 0), Margin = new Padding(3, 2, 8, 2) };

    private static void AddModuleSelector(TableLayoutPanel panel, int column, int row, string label, ComboBox box)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 4, 0) }, column, row);
        panel.Controls.Add(box, column + 1, row);
    }

    private static void AddModuleSelector(TableLayoutPanel panel, int column, int row, string label, NumericUpDown input)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 4, 0) }, column, row);
        panel.Controls.Add(input, column + 1, row);
    }

    private static void BindModuleOptions(ComboBox box, EffectModuleCatalog catalog, string kind, bool allowEmpty)
    {
        var options = catalog.Modules.Where(item => item.Kind == kind)
            .Select(item => new ModuleOption(item.ModuleId, item.DisplayNameZh, item.IsAvailableForAuthoring, item.ReasonZh)).ToList();
        if (allowEmpty) options.Insert(0, new ModuleOption(string.Empty, "不指定", true, string.Empty));
        box.BeginUpdate();
        try
        {
            box.DataSource = null;
            box.Items.Clear();
            box.Items.AddRange(options.Cast<object>().ToArray());
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }
        finally { box.EndUpdate(); }
    }

    private static string SelectedModuleId(ComboBox box) => (box.SelectedItem as ModuleOption)?.ModuleId ?? string.Empty;

    private static void SelectModule(ComboBox box, string moduleId)
    {
        for (var index = 0; index < box.Items.Count; index++)
        {
            if (box.Items[index] is not ModuleOption option || !option.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase)) continue;
            box.SelectedIndex = index;
            return;
        }
    }

    private static DataTable BuildLogicalEffectTable(IReadOnlyList<LogicalEffectInstance> instances)
    {
        var table = new DataTable();
        foreach (var column in new[] { "索引", "编号", "名称", "渠道", "来源", "证据", "写入", "实际效果", "当前参数" }) table.Columns.Add(column);
        for (var index = 0; index < instances.Count; index++)
        {
            var item = instances[index];
            var ids = string.Join(" / ", new[]
            {
                item.PersonalChannel == null ? null : $"人物 {item.PersonalChannel.EffectId:X2}",
                item.ItemChannel?.IsEnabled == true ? $"宝物 {item.ItemChannel.EffectId:X2}" : null
            }.Where(value => value != null));
            var channel = item.PersonalChannel != null && item.ItemChannel?.IsEnabled == true ? "双渠道"
                : item.PersonalChannel != null ? "人物/兵种"
                : item.ItemChannel?.IsEnabled == true ? "宝物" : "未绑定";
            var source = item.PatchCategory is "CompositeEffect" or "ModularSemanticEffectV2" ? "复合"
                : item.SourceKind switch
                {
                    EffectInstanceSourceKind.Injected => "注入",
                    EffectInstanceSourceKind.Combined => "原生+注入",
                    _ => "原生"
                };
            var evidence = item.WriteDecision?.CanApply == true ? "写入证据满足"
                : item.WriteDecision?.CanPreview == true ? "结构确认"
                : item.HasInjectedImplementation ? "语义推断" : "物理确认";
            var write = item.WriteDecision?.CanApply == true ? "可写"
                : item.WriteDecision?.CanPreview == true ? "仅预览" : "只读";
            table.Rows.Add(index.ToString(CultureInfo.InvariantCulture), ids, item.Name, channel, source,
                evidence, write, item.NaturalLanguageDescription, item.CurrentParameterSummary);
        }
        return table;
    }

    private static LogicalEffectInstance? GetSelectedLogicalEffect(DataGridView grid, IReadOnlyList<LogicalEffectInstance> instances)
    {
        var text = grid.CurrentRow == null || !grid.Columns.Contains("索引") ? null : Convert.ToString(grid.CurrentRow.Cells["索引"].Value, CultureInfo.InvariantCulture);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < instances.Count ? instances[index] : null;
    }

    private async Task RunEffectValidationAsync(Control status)
    {
        if (!TryGetEffectInjectionProject(out var project)) return;
        try
        {
            status.Text = "正在准备签名验证副本和行为契约……";
            var snapshot = _currentEffectAnalysisSnapshot ?? await EffectAnalysisCoordinator.Shared.ScanAsync(project);
            var matrix = await Task.Run(() => new EffectWriteAuthorizationService().ReadMatrix(project));
            var missing = matrix.Decisions.Where(item => !item.CanApply)
                .SelectMany(item => item.BlockerCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (missing.Length == 0)
            {
                MessageBox.Show(this, "当前权限矩阵没有待验证项。", "权限验证", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var plan = await Task.Run(() => new LocalEffectProfileService().Prepare(project));
            var contracts = snapshot.HookContracts.Where(contract =>
                    !string.IsNullOrWhiteSpace(contract.ContractHash) &&
                    !string.IsNullOrWhiteSpace(contract.ContractCodeIdentityHash))
                .ToList();
            status.Text = $"验证副本：{plan.SandboxRoot}；待验证契约：{contracts.Count}；{string.Join(", ", missing)}";
            using var dialog = new EffectValidationDialog(project, plan, contracts);
            dialog.ShowDialog(this);
            if (dialog.ValidationCompleted)
            {
                EffectAnalysisCoordinator.Shared.Invalidate([project.ResolveGameFile("Ekd5.exe")]);
                _currentEffectAnalysisSnapshot = null;
                status.Text = "验证证据已导入，请重新扫描刷新权限。";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "验证准备失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task PrepareEffectProfileAsync(Control status)
    {
        if (!TryGetEffectInjectionProject(out var project)) return;
        try
        {
            var plan = await Task.Run(() => new LocalEffectProfileService().Prepare(project));
            status.Text = $"{plan.ProfileTrustTier}；副本 {plan.SandboxRoot}；{plan.SummaryZh}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "EXE 验证失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowEffectRuntimeDiagnostics()
    {
        var release = new EffectReleaseConsistencyService().Read(forceRefresh: true);
        var lines = new List<string>
        {
            "程序：" + Application.ExecutablePath,
            "运行目录：" + AppContext.BaseDirectory,
            "构建：" + EffectCapabilityVersion.BuildIdentity,
            "能力：" + EffectCapabilityVersion.SchemaVersion,
            "发布清单：" + (string.IsNullOrWhiteSpace(release.ManifestPath) ? "未找到" : release.ManifestPath),
            "组件状态：" + release.StatusZh
        };
        lines.AddRange(release.Components.Select(component =>
            $"{component.DisplayNameZh}：{component.FileVersion}  {component.Sha256}"));
        if (_currentEffectAnalysisSnapshot != null)
        {
            lines.Add("游戏 EXE：" + _currentEffectAnalysisSnapshot.TargetFilePath);
            lines.Add("完整 SHA：" + _currentEffectAnalysisSnapshot.FullExeSha256);
            lines.Add("档案：" + _currentEffectAnalysisSnapshot.ProfileAudit.ProfileId + " / " + _currentEffectAnalysisSnapshot.ProfileAudit.TrustStatus);
            lines.AddRange(_currentEffectAnalysisSnapshot.HookContracts.Select(contract =>
                $"契约 {contract.ContractId}：{contract.ContractCodeIdentityHash}"));
        }
        using var dialog = new Form { Text = "特效运行诊断", Width = 920, Height = 620, StartPosition = FormStartPosition.CenterParent };
        dialog.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Text = string.Join(Environment.NewLine, lines)
        });
        dialog.ShowDialog(this);
    }

    private void ShowEffectAdvancedDiagnostics(EffectAnalysisSnapshot? snapshot)
    {
        var report = snapshot?.Inventory;
        if (report == null) { MessageBox.Show(this, "请先扫描特效清单。", "高级诊断", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var dialog = new Form { Text = "特效高级诊断", Width = 1100, Height = 720, StartPosition = FormStartPosition.CenterParent };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var candidateGrid = CreateEffectInjectionGrid();
        candidateGrid.DataSource = BuildInjectedEffectPatchTable(report.Discovery.Candidates);
        ConfigureEffectInjectionGrid(candidateGrid);
        tabs.TabPages.Add(CreateControlTabPage("底层候选", candidateGrid));
        var hookGrid = CreateEffectInjectionGrid();
        hookGrid.DataSource = BuildInjectedEffectHookTable(report.Discovery.HookCandidates);
        ConfigureEffectInjectionGrid(hookGrid);
        tabs.TabPages.Add(CreateControlTabPage("跳转与注入点", hookGrid));
        var diagnosticBox = CreateEffectInjectionInfoBox();
        diagnosticBox.Text = string.Join(Environment.NewLine, report.Diagnostics.Select(item => $"{item.Category} {item.AddressHex} {item.Summary} {item.Evidence}"));
        tabs.TabPages.Add(CreateControlTabPage("诊断", diagnosticBox));
        if (snapshot != null)
        {
            if (TryGetEffectInjectionProject(out var project))
            {
                var ledger = new EffectConfirmationLedgerService().Build(project, snapshot);
                var ledgerGrid = CreateEffectInjectionGrid();
                ledgerGrid.DataSource = ledger.Effects.Select(item => new
                {
                    item.Name,
                    人物号 = item.PersonalEffectId.HasValue ? $"{item.PersonalEffectId:X2}" : string.Empty,
                    宝物号 = item.ItemEffectId.HasValue ? $"{item.ItemEffectId:X2}" : string.Empty,
                    来源 = item.SourceKind,
                    物理证据 = item.PhysicalEvidenceZh,
                    结构证据 = item.StructuralEvidenceZh,
                    动态证据 = item.DynamicEvidenceZh,
                    结论 = item.ConclusionZh,
                    阻断码 = string.Join(",", item.BlockerCodes),
                    下一步 = item.NextActionZh
                }).ToList();
                tabs.TabPages.Add(CreateControlTabPage("确认台账", ledgerGrid));
            }
            var locationGrid = CreateEffectInjectionGrid();
            locationGrid.DataSource = snapshot.LocationIndex.Locations.Select(item => new
            {
                item.LocationId,
                渠道 = item.ChannelZh,
                特效 = item.EffectNameZh,
                编号 = item.EffectIdHex,
                类型 = item.KindZh,
                参数 = item.ParameterRoleZh,
                文件 = item.TargetFile,
                偏移 = item.FileOffset.HasValue ? $"0x{item.FileOffset:X}" : string.Empty,
                写入能力 = item.WriteCapabilityZh,
                原因 = item.BlockingReasonZh
            }).ToList();
            tabs.TabPages.Add(CreateControlTabPage("特效号位置", locationGrid));
        }
        dialog.Controls.Add(tabs);
        dialog.ShowDialog(this);
    }

    private static string FormatLogicalParameter(LogicalEffectParameter parameter)
        => !parameter.IsConsistent ? "配置不一致"
            : parameter.MeaningKind is EffectParameterMeaningKind.Percentage or EffectParameterMeaningKind.Probability ? parameter.Value + "%"
            : parameter.Role is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment ? $"{parameter.Value:X2}"
            : parameter.Value?.ToString(CultureInfo.InvariantCulture) ?? "未知";

    private static string BuildEffectChannelDetail(LogicalEffectInstance instance)
    {
        var builder = new StringBuilder(instance.NaturalLanguageDescription);
        AppendChannel(instance.PersonalChannel, "人物/兵种渠道");
        AppendChannel(instance.ItemChannel?.IsEnabled == true ? instance.ItemChannel : null, "宝物渠道");
        return builder.ToString();

        void AppendChannel(EffectChannelReference? channel, string title)
        {
            if (channel == null) return;
            builder.AppendLine().AppendLine();
            builder.Append(title).Append("：").Append(channel.DisplayName);
            if (channel.Bindings.Count == 0) builder.Append("；尚未配置来源");
            else
            {
                foreach (var binding in channel.Bindings.Take(4))
                {
                    builder.AppendLine().Append("- ").Append(binding.Kind).Append("：").Append(binding.Summary);
                    if (binding.EffectValue.HasValue) builder.Append("，特效值 ").Append(binding.EffectValue.Value);
                }
            }
            foreach (var conflict in channel.Conflicts) builder.AppendLine().Append("注意：").Append(conflict);
        }
    }
}
