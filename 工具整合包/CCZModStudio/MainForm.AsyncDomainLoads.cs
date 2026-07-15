using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private async Task LoadRoleEditorAsync()
    {
        if (!TryCaptureDomainLoad("角色设定", out var project, out var tables)) return;
        _loadRoleEditorButton.Enabled = false;
        _roleEditorInfoBox.Text = "正在后台读取人物、R/S、职业和装备引用……";
        try
        {
            await _asyncLoadCoordinator.RunLatestAsync("RoleEditor", project.GameRoot,
                token => Task.Run(() => BuildRoleEditorData(project, tables), token), data =>
                {
                    if (!ReferenceEquals(_project, project) || IsDisposed) return Task.CompletedTask;
                    _currentRoleEditorData = data;
                    LoadRoleTextTables();
                    _roleEditorGrid.DataSource = data;
                    ConfigureRoleEditorGrid();
                    ConfigureRoleEquipmentDetailControls();
                    ShowSelectedRoleEditorCell();
                    _saveRoleEditorButton.Enabled = _importRoleFaceButton.Enabled = _batchImportRoleFaceButton.Enabled = true;
                    _exportRoleFaceBmpButton.Enabled = _exportRoleEditorCsvButton.Enabled = _importRoleEditorCsvButton.Enabled = true;
                    _roleEditorInfoBox.Text = BuildRoleEditorSummary(data);
                    SetStatus($"角色设定读取完成：{data.Rows.Count} 行");
                    return Task.CompletedTask;
                });
        }
        catch (Exception ex) { ShowDomainLoadError("角色设定", _roleEditorInfoBox, ex); }
        finally { if (!IsDisposed) _loadRoleEditorButton.Enabled = true; }
    }

    private async Task LoadJobEditorAsync()
    {
        if (!TryCaptureDomainLoad("兵种设定", out var project, out var tables)) return;
        CommitJobDescriptionBoxEdit();
        CommitJobEquipmentEditorChanges();
        _loadJobEditorButton.Enabled = false;
        _jobEditorInfoBox.Text = "正在后台读取兵种名称、说明、成长和穿透……";
        try
        {
            await _asyncLoadCoordinator.RunLatestAsync("JobEditor", project.GameRoot,
                token => Task.Run(() => BuildJobEditorData(project, tables), token), data =>
                {
                    if (!ReferenceEquals(_project, project) || IsDisposed) return Task.CompletedTask;
                    ClearJobDescriptionBox("读取兵种后，在此编辑当前兵种介绍。");
                    _jobEquipmentEditorBoundRow = null;
                    HideJobEquipmentEditor();
                    _currentJobEditorData = data;
                    _jobEditorGrid.DataSource = data;
                    ConfigureJobEditorGrid();
                    ResetJobEditorHistory();
                    _saveJobEditorButton.Enabled = true;
                    _editAccessoryJobGroupsButton.Enabled = _currentAccessoryJobGroupProfile != null;
                    _replaceJobSImageButton.Enabled = _playJobSImageButton.Enabled = _viewJobSSingleFramesButton.Enabled = true;
                    _editJobSImagePixelsButton.Enabled = _batchReplaceJobSImageButton.Enabled = true;
                    _exportJobSImageBmpButton.Enabled = _exportJobEditorCsvButton.Enabled = _importJobEditorCsvButton.Enabled = true;
                    _jobEditorInfoBox.Text = BuildJobEditorSummary(data);
                    ShowSelectedJobEditorCell();
                    SetStatus($"兵种设定读取完成：{data.Rows.Count} 行");
                    return Task.CompletedTask;
                });
        }
        catch (Exception ex) { ShowDomainLoadError("兵种设定", _jobEditorInfoBox, ex); }
        finally { if (!IsDisposed) _loadJobEditorButton.Enabled = true; }
    }

    private async Task LoadItemEditorAsync()
    {
        if (!TryCaptureDomainLoad("宝物设定", out var project, out var tables)) return;
        _loadItemEditorButton.Enabled = false;
        _itemEditorInfoBox.Text = "正在后台读取物品、说明、类型和特效引用……";
        try
        {
            await _asyncLoadCoordinator.RunLatestAsync("ItemEditor", project.GameRoot,
                token => Task.Run(() => BuildItemEditorData(project, tables), token), data =>
                {
                    if (!ReferenceEquals(_project, project) || IsDisposed) return Task.CompletedTask;
                    _currentItemEditorData = data;
                    _itemEditorGrid.DataSource = data;
                    ConfigureItemEditorGrid();
                    _saveItemEditorButton.Enabled = _queryItemIconButton.Enabled = true;
                    _batchImportItemIconButton.Enabled = _editItemIconButton.Enabled = _exportItemIconBmpButton.Enabled = true;
                    _exportItemEditorCsvButton.Enabled = _importItemEditorCsvButton.Enabled = true;
                    ResetItemEditorHistory();
                    _itemEditorInfoBox.Text = BuildItemEditorSummary(data);
                    ShowSelectedItemEditorCell();
                    SetStatus($"宝物设定读取完成：{data.Rows.Count} 行");
                    return Task.CompletedTask;
                });
        }
        catch (Exception ex) { ShowDomainLoadError("宝物设定", _itemEditorInfoBox, ex); }
        finally { if (!IsDisposed) _loadItemEditorButton.Enabled = true; }
    }

    private async Task LoadShopEditorAsync()
    {
        if (!TryCaptureDomainLoad("商店编辑", out var project, out var tables)) return;
        _loadShopEditorButton.Enabled = false;
        _shopEditorInfoBox.Text = "正在后台读取战役、商店和物品引用……";
        try
        {
            await _asyncLoadCoordinator.RunLatestAsync("ShopEditor", project.GameRoot,
                token => Task.Run(() => BuildShopEditorData(project, tables), token), data =>
                {
                    if (!ReferenceEquals(_project, project) || IsDisposed) return Task.CompletedTask;
                    _currentShopEditorData = data;
                    _shopEditorGrid.DataSource = data;
                    ConfigureShopEditorGrid();
                    _saveShopEditorButton.Enabled = _exportShopEditorCsvButton.Enabled = _importShopEditorCsvButton.Enabled = true;
                    _shopEditorInfoBox.Text = BuildShopEditorSummary(data);
                    ShowSelectedShopEditorCell();
                    SetStatus($"商店编辑读取完成：{data.Rows.Count} 行");
                    return Task.CompletedTask;
                });
        }
        catch (Exception ex) { ShowDomainLoadError("商店编辑", _shopEditorInfoBox, ex); }
        finally { if (!IsDisposed) _loadShopEditorButton.Enabled = true; }
    }

    private bool TryCaptureDomainLoad(string feature, out CczProject project, out IReadOnlyList<HexTableDefinition> tables)
    {
        project = _project!;
        tables = _tables;
        if (_project == null) return false;
        project = _project;
        if (tables.Count > 0) return true;
        SetStatus($"{feature}：项目仍在初始化，请稍后重试");
        return false;
    }

    private void ShowDomainLoadError(string feature, TextBox infoBox, Exception exception)
    {
        infoBox.Text = exception.ToString();
        System.Diagnostics.Debug.WriteLine($"读取{feature}失败：" + exception);
        if (!IsDisposed) MessageBox.Show(this, exception.Message, $"读取{feature}失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
