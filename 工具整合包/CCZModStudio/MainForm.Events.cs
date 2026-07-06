using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private void WireEvents()
    {
        _battlefieldUnitAnimationTimer.Tick += (_, _) => AdvanceBattlefieldUnitAnimation();
        _battlefieldUnitAnimationTimer.Start();
        _rScenePlaybackTimer.Tick += (_, _) => AdvanceRScenePlayback();
        _jobStrategyAnimationTimer.Tick += (_, _) => AdvanceJobStrategyAnimationPreview();
        _mapMakerDirtyBaseRefreshTimer.Tick += (_, _) => FlushMapMakerDirtyBasePreview(runBeautify: false);
        _mainTabs.SelectedIndexChanged += (_, _) => HandleMainTabSelectionChanged();
        _openProjectButton.Click += (_, _) => OpenProjectDialog();
        _reloadButton.Click += (_, _) => ReloadCurrentProject();
        _saveTableButton.Click += (_, _) => SaveCurrentTable();
        _exportCsvButton.Click += (_, _) => ExportCurrentTableCsv();
        _importCsvButton.Click += (_, _) => ImportCurrentTableCsv();
        _copyTableSelectionButton.Click += (_, _) => CopyGridSelection(_dataGrid);
        _pasteTableSelectionButton.Click += (_, _) => PasteGridSelection(_dataGrid, (_, _) => { }, null, RefreshGenericTableCellsAfterEdit);
        _batchFillTableColumnButton.Click += (_, _) => FillSelectedGridColumnWithCurrentValue(_dataGrid, (_, _) => { }, null, RefreshGenericTableCellsAfterEdit);
        _batchModifyTableButton.Click += (_, _) => ShowGridBatchModifyDialog(_dataGrid, (_, _) => { }, null, RefreshGenericTableCellsAfterEdit);
        _undoTableEditButton.Click += (_, _) => UndoGridEdit(_dataGrid, (_, _) => { }, null, RefreshGenericTableCellsAfterEdit);
        _redoTableEditButton.Click += (_, _) => RedoGridEdit(_dataGrid, (_, _) => { }, null, RefreshGenericTableCellsAfterEdit);
        _openPlanButton.Click += (_, _) => OpenPlan();
        _loadRoleEditorButton.Click += (_, _) => LoadRoleEditor();
        _saveRoleEditorButton.Click += (_, _) => SaveRoleEditor();
        _importRoleFaceButton.Click += (_, _) => ImportSelectedRoleFace();
        _batchImportRoleFaceButton.Click += (_, _) => BatchImportSelectedRoleFaces();
        _exportRoleFaceBmpButton.Click += (_, _) => ExportSelectedRoleFacesBmp();
        _saveRoleTextDetailButton.Click += (_, _) => SaveSelectedRoleTextDetails();
        _openRoleInTableEditorButton.Click += (_, _) => OpenCoreTable("6.5-0 人物");
        _openRolePersonalEffectButton.Click += (_, _) => OpenRolePersonalEffectEditor();
        _openRoleEffectButton.Click += (_, _) => OpenRolePersonalEffectTableEditor();
        _openGlobalSettingsButton.Click += (_, _) => OpenGlobalSettingsDialog();
        _exportRoleEditorCsvButton.Click += (_, _) => ExportRoleEditorCsv();
        _importRoleEditorCsvButton.Click += (_, _) => ImportRoleEditorCsv();
        _copyRoleEditorSelectionButton.Click += (_, _) => CopyGridSelection(_roleEditorGrid);
        _pasteRoleEditorSelectionButton.Click += (_, _) => PasteGridSelection(_roleEditorGrid, UpdateRoleEditorDerivedCells, null, RefreshRoleEditorCellsAfterEdit);
        _batchFillRoleEditorColumnButton.Click += (_, _) => FillSelectedGridColumnWithCurrentValue(_roleEditorGrid, UpdateRoleEditorDerivedCells, null, RefreshRoleEditorCellsAfterEdit);
        _filterRoleEditorButton.Click += (_, _) => ApplyRoleEditorFilter();
        _clearRoleEditorFilterButton.Click += (_, _) => ClearRoleEditorFilter();
        _roleWeaponCombo.SelectionChangeCommitted += (_, _) => ApplyRoleEquipmentDetailSelection("武器", _roleWeaponCombo);
        _roleArmorCombo.SelectionChangeCommitted += (_, _) => ApplyRoleEquipmentDetailSelection("防具", _roleArmorCombo);
        _roleAssistCombo.SelectionChangeCommitted += (_, _) => ApplyRoleEquipmentDetailSelection("辅助", _roleAssistCombo);
        _roleCriticalQuoteModeCombo.SelectionChangeCommitted += (_, _) => ChangeRoleCriticalQuoteModeFromUi();
        _roleCriticalQuoteAssignmentCombo.SelectionChangeCommitted += (_, _) => ChangeRoleCriticalQuoteAssignmentFromUi();
        _roleEditorSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyRoleEditorFilter();
            e.SuppressKeyPress = true;
        };
        _roleEditorGrid.SelectionChanged += (_, _) => ShowSelectedRoleEditorCell();
        _roleEditorGrid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            SetStatus("角色设定单元格显示值无法匹配，请重新导入或检查职业等下拉字段。");
        };
        _roleEditorGrid.CellValidating += (_, e) => ValidateRoleEditorCell(e);
        _roleEditorGrid.CellEndEdit += (_, e) =>
        {
            UpdateRoleEditorDerivedCells(e.RowIndex, e.ColumnIndex);
            RefreshRoleEditorRowStyle(e.RowIndex);
            ShowSelectedRoleEditorCell();
        };
        _loadJobEditorButton.Click += (_, _) => LoadJobEditor();
        _saveJobEditorButton.Click += (_, _) => SaveJobEditor();
        _editAccessoryJobGroupsButton.Click += (_, _) => EditAccessoryJobGroups();
        _replaceJobSImageButton.Click += (_, _) => ReplaceSelectedJobSImage();
        _batchReplaceJobSImageButton.Click += (_, _) => BatchReplaceSelectedJobSImages();
        _exportJobSImageBmpButton.Click += (_, _) => ExportSelectedJobSImagesBmp();
        _openJobSeriesTableButton.Click += (_, _) => OpenCoreTable("6.5-3 兵种系");
        _openJobEffectTableButton.Click += (_, _) => OpenJobEffectEditor();
        _exportJobEditorCsvButton.Click += (_, _) => ExportJobEditorCsv();
        _importJobEditorCsvButton.Click += (_, _) => ImportJobEditorCsv();
        _copyJobEditorSelectionButton.Click += (_, _) => CopyGridSelection(_jobEditorGrid);
        _pasteJobEditorSelectionButton.Click += (_, _) => PasteJobEditorSelection();
        _batchFillJobEditorColumnButton.Click += (_, _) => FillJobEditorSelectionWithCurrentValue();
        _undoJobEditorButton.Click += (_, _) => UndoJobEditorChange();
        _redoJobEditorButton.Click += (_, _) => RedoJobEditorChange();
        _filterJobEditorButton.Click += (_, _) => ApplyJobEditorFilter();
        _clearJobEditorFilterButton.Click += (_, _) => ClearJobEditorFilter();
        _jobEditorSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyJobEditorFilter();
            e.SuppressKeyPress = true;
        };
        _jobEditorGrid.SelectionChanged += (_, _) => HandleJobEditorSelectionChanged();
        _jobEditorGrid.CellMouseDown += (_, _) => MarkJobEditorSelectionChangeFromMouse();
        _jobEditorGrid.KeyDown += (_, e) =>
        {
            if (IsPotentialJobEditorTextInput(e)) SnapshotJobEditorSelectionForEdit();
        };
        _jobEditorGrid.CellBeginEdit += (_, e) => BeginJobEditorCellEdit(e.RowIndex, e.ColumnIndex);
        _jobEditorGrid.CellValidating += (_, e) => ValidateJobEditorCell(e);
        _jobEditorGrid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                var columnName = _jobEditorGrid.Columns[e.ColumnIndex].DataPropertyName;
                _jobEditorInfoBox.Text = $"兵种设定单元格显示失败：{columnName}；{e.Exception?.Message}";
            }
        };
        _jobEditorGrid.CellEndEdit += (_, e) =>
        {
            CompleteJobEditorCellEdit(e.RowIndex, e.ColumnIndex);
            RefreshJobEditorRowStyle(e.RowIndex);
            ShowSelectedJobEditorCell();
        };
        _jobAreaPreviewInfoBox.TextChanged += (_, _) => ApplyJobDescriptionBoxEdit();
        _loadItemEditorButton.Click += (_, _) => LoadItemEditor();
        _saveItemEditorButton.Click += (_, _) => SaveItemEditor();
        _openItemEffectCatalogButton.Click += (_, _) => OpenItemEffectCatalogEditor();
        _exportItemEditorCsvButton.Click += (_, _) => ExportItemEditorCsv();
        _importItemEditorCsvButton.Click += (_, _) => ImportItemEditorCsv();
        _copyItemEditorSelectionButton.Click += (_, _) => CopyGridSelection(_itemEditorGrid);
        _pasteItemEditorSelectionButton.Click += (_, _) => PasteItemEditorSelection();
        _batchFillItemEditorColumnButton.Click += (_, _) => FillItemEditorSelectionWithCurrentValue();
        _batchImportItemIconButton.Click += (_, _) => BatchImportSelectedItemIcons();
        _editItemIconButton.Click += (_, _) => EditSelectedItemIcon();
        _exportItemIconBmpButton.Click += (_, _) => ExportSelectedItemIconsBmp();
        _undoItemEditorButton.Click += (_, _) => UndoItemEditorChange();
        _redoItemEditorButton.Click += (_, _) => RedoItemEditorChange();
        _filterItemEditorButton.Click += (_, _) => ApplyItemEditorFilter();
        _clearItemEditorFilterButton.Click += (_, _) => ClearItemEditorFilter();
        _itemEditorSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyItemEditorFilter();
            e.SuppressKeyPress = true;
        };
        _itemEditorGrid.SelectionChanged += (_, _) => HandleItemEditorSelectionChanged();
        _itemEditorGrid.CellMouseDown += (_, _) => MarkItemEditorSelectionChangeFromMouse();
        _itemEditorGrid.KeyDown += (_, e) =>
        {
            if (IsPotentialItemEditorTextInput(e)) SnapshotItemEditorSelectionForEdit();
        };
        _itemEditorGrid.CellBeginEdit += (_, e) => BeginItemEditorCellEdit(e.RowIndex, e.ColumnIndex);
        _itemEditorGrid.CellValidating += (_, e) => ValidateItemEditorCell(e);
        _itemEditorGrid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            SetStatus("宝物设定单元格显示值无法匹配，请重新读取或检查类型映射。");
        };
        _itemEditorGrid.CellEndEdit += (_, e) =>
        {
            CompleteItemEditorCellEdit(e.RowIndex, e.ColumnIndex);
            RefreshItemEditorRowStyle(e.RowIndex);
            ShowSelectedItemEditorCell();
        };
        _loadShopEditorButton.Click += (_, _) => LoadShopEditor();
        _saveShopEditorButton.Click += (_, _) => SaveShopEditor();
        _exportShopEditorCsvButton.Click += (_, _) => ExportShopEditorCsv();
        _importShopEditorCsvButton.Click += (_, _) => ImportShopEditorCsv();
        _copyShopEditorSelectionButton.Click += (_, _) => CopyGridSelection(_shopEditorGrid);
        _pasteShopEditorSelectionButton.Click += (_, _) => PasteShopEditorSelection();
        _batchFillShopEditorColumnButton.Click += (_, _) => FillShopEditorSelectionWithCurrentValue();
        _filterShopEditorButton.Click += (_, _) => ApplyShopEditorFilter();
        _clearShopEditorFilterButton.Click += (_, _) => ClearShopEditorFilter();
        _shopBatchSetButton.Click += (_, _) => ApplyShopBatchSet();
        _shopBatchClearButton.Click += (_, _) => ApplyShopBatchClear();
        _shopBatchReplaceButton.Click += (_, _) => ApplyShopBatchReplace();
        _shopEditorSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyShopEditorFilter();
            e.SuppressKeyPress = true;
        };
        _shopEditorGrid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            SetStatus("商店编辑单元格显示值无法匹配，请重新读取或检查物品映射。");
        };
        _shopEditorGrid.SelectionChanged += (_, _) => ShowSelectedShopEditorCell();
        _shopEditorGrid.CellValidating += (_, e) => ValidateShopEditorCell(e);
        _shopEditorGrid.CellEndEdit += (_, e) =>
        {
            UpdateShopEditorDerivedCells(e.RowIndex, e.ColumnIndex);
            RefreshShopEditorRowStyle(e.RowIndex);
            ShowSelectedShopEditorCell();
        };
        _loadJobTerrainButton.Click += (_, _) => LoadJobTerrainEditor();
        _saveJobTerrainButton.Click += (_, _) => SaveJobTerrainEditor();
        _openJobRestraintTableButton.Click += (_, _) => OpenCoreTable("6.5-3-3 兵种相克");
        _filterJobTerrainButton.Click += (_, _) => ApplyJobTerrainFilter();
        _clearJobTerrainFilterButton.Click += (_, _) => ClearJobTerrainFilter();
        _jobTerrainSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyJobTerrainFilter();
            e.SuppressKeyPress = true;
        };
        _jobTerrainGrid.SelectionChanged += (_, _) => ShowSelectedJobTerrainCell();
        _jobTerrainGrid.CellValidating += (_, e) => ValidateJobTerrainCell(e);
        _jobTerrainGrid.CellEndEdit += (_, e) => RefreshJobTerrainRowStyle(e.RowIndex);
        _loadJobMatrixButton.Click += (_, _) => LoadJobMatrixEditor();
        _saveJobMatrixButton.Click += (_, _) => SaveJobMatrixEditor();
        _openJobMatrixAttributeTableButton.Click += (_, _) => OpenCoreTable("6.5-3-4 兵种属性");
        _openJobMatrixRestraintTableButton.Click += (_, _) => OpenCoreTable("6.5-3-3 兵种相克");
        _jobRestraintGrid.SelectionChanged += (_, _) => ShowSelectedJobMatrixCell(_jobRestraintGrid, "兵种相克");
        _jobRestraintGrid.CellValidating += (_, e) => ValidateJobMatrixCell(_jobRestraintGrid, e);
        _jobRestraintGrid.CellEndEdit += (_, e) => RefreshJobMatrixRowStyle(_jobRestraintGrid, e.RowIndex);
        _jobAttributeGrid.SelectionChanged += (_, _) => ShowSelectedJobMatrixCell(_jobAttributeGrid, "兵种属性");
        _jobAttributeGrid.CellParsing += (_, e) => ParseJobAttributeMatrixCell(e);
        _jobAttributeGrid.CellValidating += (_, e) => ValidateJobMatrixCell(_jobAttributeGrid, e);
        _jobAttributeGrid.CellEndEdit += (_, e) => RefreshJobMatrixRowStyle(_jobAttributeGrid, e.RowIndex);
        _loadJobStrategyEditorButton.Click += (_, _) => LoadJobStrategyEditor();
        _saveJobStrategyEditorButton.Click += (_, _) => SaveJobStrategyEditor();
        _importJobStrategyIconButton.Click += (_, _) => ImportSelectedJobStrategyIcons();
        _editJobStrategyIconButton.Click += (_, _) => EditSelectedJobStrategyIcon();
        _exportJobStrategyIconBmpButton.Click += (_, _) => ExportSelectedJobStrategyIconsBmp();
        _openJobStrategyTableButton.Click += (_, _) => OpenCoreTable("6.5-5 策略");
        _filterJobStrategyEditorButton.Click += (_, _) => ApplyJobStrategyFilter();
        _clearJobStrategyEditorFilterButton.Click += (_, _) => ClearJobStrategyFilter();
        _jobStrategyEditorSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyJobStrategyFilter();
            e.SuppressKeyPress = true;
        };
        _jobStrategyEditorGrid.SelectionChanged += (_, _) => HandleJobStrategySelectionChanged();
        _jobStrategyEditorGrid.CellValidating += (_, e) => ValidateJobStrategyCell(e);
        _jobStrategyEditorGrid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                _jobStrategyEditorInfoBox.Text = $"兵种策略单元格显示失败：{e.Exception?.Message}";
            }
        };
        _jobStrategyEditorGrid.CellEndEdit += (_, e) =>
        {
            UpdateJobStrategyDerivedCells(e.RowIndex, e.ColumnIndex);
            RefreshJobStrategyRowStyle(e.RowIndex);
            ShowSelectedJobStrategyCell();
        };
        _jobStrategyLearningEditorGrid.CellValidating += (_, e) => ValidateJobStrategyLearningEditorCell(e);
        _jobStrategyLearningEditorGrid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            _jobStrategyLearningEditorStatusLabel.Text = $"学习等级无效：{e.Exception?.Message}";
        };
        _jobStrategyLearningEditorGrid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                _jobStrategyLearningEditorGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = string.Empty;
            }

            UpdateJobStrategyLearningEditorStatus();
        };
        _loadJobEffectEditorButton.Click += (_, _) => LoadJobEffectEditor();
        _saveJobEffectEditorButton.Click += (_, _) => SaveJobEffectEditor();
        _openJobExclusiveEffectTableButton.Click += (_, _) => OpenCoreTable("6.5-7-3 人物专属、套装专属");
        _filterJobEffectEditorButton.Click += (_, _) => ApplyJobEffectFilter();
        _clearJobEffectEditorFilterButton.Click += (_, _) => ClearJobEffectFilter();
        _jobEffectEditorSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyJobEffectFilter();
            e.SuppressKeyPress = true;
        };
        _jobEffectEditorGrid.SelectionChanged += (_, _) => ShowSelectedJobEffectCell();
        _jobEffectEditorGrid.CellValidating += (_, e) => ValidateJobEffectCell(e);
        _jobEffectEditorGrid.CellEndEdit += (_, e) =>
        {
            UpdateJobEffectDerivedCells(e.RowIndex, e.ColumnIndex);
            RefreshJobEffectRowStyle(e.RowIndex);
            ShowSelectedJobEffectCell();
        };
        _showAllTables.CheckedChanged += (_, _) => RefreshTableList();
        _currentPageDecimalButton.CheckedChanged += (_, _) => RefreshCurrentPageNumberBaseDisplay(updateStatus: true);
        _currentPageHexButton.CheckedChanged += (_, _) => RefreshCurrentPageNumberBaseDisplay(updateStatus: true);
        AttachNumberBaseHandlers(this);
        _tableList.SelectedIndexChanged += (_, _) => LoadSelectedTable();
        _dataGrid.CellValidating += (_, e) => ValidateEditedCell(e);
        _dataGrid.CellEndEdit += (_, e) =>
        {
            RefreshCurrentTableDerivedCellsAfterCellEdit(e.RowIndex, e.ColumnIndex);
            ShowSelectedDataCellAnnotation(e.RowIndex, e.ColumnIndex);
        };
        _dataGrid.CellEnter += (_, e) => ShowSelectedDataCellAnnotation(e.RowIndex, e.ColumnIndex);
        _dataGrid.SelectionChanged += (_, _) =>
        {
            if (_dataGrid.CurrentCell != null)
            {
                ShowSelectedDataCellAnnotation(_dataGrid.CurrentCell.RowIndex, _dataGrid.CurrentCell.ColumnIndex);
            }
        };
        _jumpTableReferenceButton.Click += (_, _) => JumpCurrentTableReferenceTarget();
        _renderChartButton.Click += (_, _) => RenderCurrentTableChart();
        _filterTableColumnsButton.Click += (_, _) => ApplyTableColumnFilter();
        _clearTableColumnFilterButton.Click += (_, _) => ClearTableColumnFilter();
        _dangerTableColumnsOnly.CheckedChanged += (_, _) => ApplyTableColumnFilter();
        _exportFieldAnnotationsButton.Click += (_, _) => ExportCurrentTableFieldAnnotations();
        _exportVisibleColumnsCsvButton.Click += (_, _) => ExportVisibleTableColumnsCsv();
        _filterTableRowsButton.Click += (_, _) => ApplyTableRowFilter();
        _clearTableRowFilterButton.Click += (_, _) => ClearTableRowFilter();
        _changedTableRowsOnly.CheckedChanged += (_, _) => ApplyTableRowFilter();
        AttachGridEditShortcuts(_dataGrid, (_, _) => { }, null, afterCellsChanged: RefreshGenericTableCellsAfterEdit);
        AttachGridEditShortcuts(_roleEditorGrid, UpdateRoleEditorDerivedCells, null, afterCellsChanged: RefreshRoleEditorCellsAfterEdit);
        AttachGridEditShortcuts(
            _jobEditorGrid,
            (_, _) => { },
            RefreshJobEditorAfterBulkEdit,
            PasteJobEditorSelection,
            FillJobEditorSelectionWithCurrentValue,
            UndoJobEditorChange,
            RedoJobEditorChange);
        AttachGridEditShortcuts(
            _itemEditorGrid,
            (_, _) => { },
            RefreshItemEditorAfterBulkEdit,
            PasteItemEditorSelection,
            FillItemEditorSelectionWithCurrentValue,
            UndoItemEditorChange,
            RedoItemEditorChange);
        AttachGridEditShortcuts(
            _shopEditorGrid,
            (_, _) => { },
            RefreshShopEditorAfterBulkEdit,
            PasteShopEditorSelection,
            FillShopEditorSelectionWithCurrentValue);
        AttachGridEditShortcuts(_jobTerrainGrid, (_, _) => { }, null, afterCellsChanged: RefreshJobTerrainCellsAfterEdit);
        AttachGridEditShortcuts(_jobRestraintGrid, (_, _) => { }, null, afterCellsChanged: RefreshJobMatrixCellsAfterEdit);
        AttachGridEditShortcuts(_jobAttributeGrid, (_, _) => { }, null, afterCellsChanged: RefreshJobMatrixCellsAfterEdit);
        AttachGridEditShortcuts(_jobStrategyEditorGrid, UpdateJobStrategyDerivedCells, null, afterCellsChanged: RefreshJobStrategyCellsAfterEdit);
        AttachGridEditShortcuts(_jobEffectEditorGrid, UpdateJobEffectDerivedCells, null, afterCellsChanged: RefreshJobEffectCellsAfterEdit);
        AttachGridEditShortcuts(_imageAssignmentGrid, (row, _) => UpdateImageAssignmentResourceStatus(row), null, afterCellsChanged: RefreshImageAssignmentCellsAfterEdit);
        _loadImageAssignmentsButton.Click += (_, _) => LoadImageAssignments();
        _loadImageResourcesButton.Click += (_, _) => LoadImageResources();
        _openImageResourceButton.Click += (_, _) => OpenSelectedImageResourceLocation();
        _replaceImageResourceEntryButton.Click += (_, _) => ImportOrReplaceSelectedImageResourceEntry(restoreMode: false);
        _editImageResourceEntryButton.Click += (_, _) => EditSelectedImageResourceEntry();
        _restoreImageResourceEntryButton.Click += (_, _) => ImportOrReplaceSelectedImageResourceEntry(restoreMode: true);
        _batchImportImageResourceEntriesButton.Click += (_, _) => BatchImportSelectedImageResourceEntries();
        _batchClearImageResourceEntriesButton.Click += (_, _) => BatchClearSelectedImageResourceEntries();
        _normalizeRoleRawImagesButton.Click += (_, _) => NormalizeRoleRawImages();
        _exportImageResourceEntriesButton.Click += (_, _) => ExportImageResourceEntriesCsv();
        _filterImageResourcesButton.Click += (_, _) => ApplyImageResourceFilter();
        _clearImageResourceFilterButton.Click += (_, _) => ClearImageResourceFilter();
        _imageResourceCategoryFilterCombo.SelectedIndexChanged += (_, _) => ApplyImageResourceFilter();
        _imageResourceSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyImageResourceFilter();
            e.SuppressKeyPress = true;
        };
        _imageResourceFileGrid.SelectionChanged += (_, _) => ShowSelectedImageResourceFile();
        _imageResourceEntryGrid.SelectionChanged += (_, _) => ShowSelectedImageResourceEntry();
        _saveImageAssignmentsButton.Click += (_, _) => SaveImageAssignments();
        _queryFreeFaceIdsButton.Click += (_, _) => ShowFreeImageAssignmentIdsDialog(ImageAssignmentResourceKind.Face);
        _queryFreeRImageIdsButton.Click += (_, _) => ShowFreeImageAssignmentIdsDialog(ImageAssignmentResourceKind.R);
        _queryFreeSImageIdsButton.Click += (_, _) => ShowFreeImageAssignmentIdsDialog(ImageAssignmentResourceKind.S);
        _openRsDirectoryButton.Click += (_, _) => OpenRsDirectory();
        _filterImageAssignmentsButton.Click += (_, _) => ApplyImageAssignmentFilter();
        _clearImageAssignmentFilterButton.Click += (_, _) => ClearImageAssignmentFilter();
        _imageAssignmentMissingOnlyCheckBox.CheckedChanged += (_, _) => ApplyImageAssignmentFilter();
        _imageAssignmentSPreviewFactionCombo.SelectedIndexChanged += (_, _) => ShowSelectedImageAssignmentDetail();
        _imageAssignmentSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyImageAssignmentFilter();
            e.SuppressKeyPress = true;
        };
        _locateImageResourceButton.Click += (_, _) => LocateSelectedImageResource();
        _replaceImageResourceButton.Click += (_, _) => ImportOrReplaceSelectedImageResource(restoreMode: false);
        _editRImageResourceButton.Click += (_, _) => EditSelectedImageAssignmentResource(ImageAssignmentResourceKind.R);
        _editSImageResourceButton.Click += (_, _) => EditSelectedImageAssignmentResource(ImageAssignmentResourceKind.S);
        _replaceRImageSetButton.Click += (_, _) => ReplaceSelectedRImageSet();
        _replaceSImageSetButton.Click += (_, _) => ReplaceSelectedSImageSet();
        _batchReplaceRImageSetButton.Click += (_, _) => BatchReplaceRImageSets();
        _batchReplaceSImageSetButton.Click += (_, _) => BatchReplaceSImageSets();
        _importImageAssignmentFaceButton.Click += (_, _) => ImportSelectedImageAssignmentFace();
        _batchImportImageAssignmentFaceButton.Click += (_, _) => BatchImportSelectedImageAssignmentFaces();
        _applyImageAssignmentFaceFrameButton.Click += (_, _) => ApplySelectedImageAssignmentFaceFrame();
        _batchApplyImageAssignmentFaceFrameButton.Click += (_, _) => BatchApplySelectedImageAssignmentFaceFrames();
        _exportRImageBmpButton.Click += (_, _) => ExportSelectedImageAssignmentBmp(ImageAssignmentResourceKind.R);
        _exportSImageBmpButton.Click += (_, _) => ExportSelectedImageAssignmentBmp(ImageAssignmentResourceKind.S);
        _exportImageAssignmentFaceBmpButton.Click += (_, _) => ExportSelectedImageAssignmentBmp(ImageAssignmentResourceKind.Face);
        _restoreImageResourceButton.Click += (_, _) => ImportOrReplaceSelectedImageResource(restoreMode: true);
        _exportMissingImageResourcesButton.Click += (_, _) => ExportMissingImageResourceReport();
        _imageAssignmentGrid.CellValidating += (_, e) => ValidateImageAssignmentCell(e);
        _imageAssignmentGrid.CellEndEdit += (_, e) => UpdateImageAssignmentResourceStatus(e.RowIndex);
        _imageAssignmentGrid.SelectionChanged += (_, _) => ShowSelectedImageAssignmentDetail();
        _loadBattlefieldButton.Click += async (_, _) => await LoadBattlefieldScenariosAsync();
        _battlefieldScenarioCombo.SelectedIndexChanged += async (_, _) =>
        {
            if (!TryCommitPendingBattlefieldConsoleChanges()) return;
            await LoadSelectedBattlefieldScenarioAsync();
        };
        _saveBattlefieldTextsButton.Click += (_, _) => SaveBattlefieldTexts();
        _saveBattlefieldUnitReviewsButton.Click += (_, _) => SaveBattlefieldUnitReviews();
        _writeBattlefieldDeploymentButton.Click += async (_, _) => await WriteBattlefieldDeploymentAsync();
        _jumpBattlefieldMapButton.Click += (_, _) => JumpBattlefieldMapMaker();
        _jumpBattlefieldScenarioButton.Click += async (_, _) => await JumpBattlefieldScenarioStructureAsync();
        _battlefieldTitleBox.TextChanged += (_, _) => UpdateBattlefieldCapacityLabels();
        _battlefieldConditionsBox.TextChanged += (_, _) => UpdateBattlefieldCapacityLabels();
        _filterBattlefieldUnitsButton.Click += (_, _) => ApplyBattlefieldUnitFilter();
        _clearBattlefieldUnitFilterButton.Click += (_, _) => ClearBattlefieldUnitFilter();
        _battlefieldUnitCategoryFilterCombo.SelectedIndexChanged += (_, _) => ApplyBattlefieldUnitFilter();
        _markBattlefieldUnitReviewedButton.Click += (_, _) => MarkSelectedBattlefieldUnit("已核对");
        _markBattlefieldUnitNeedsChangeButton.Click += (_, _) => MarkSelectedBattlefieldUnit("需修改");
        _jumpBattlefieldUnitScriptButton.Click += async (_, _) => await JumpSelectedBattlefieldUnitToScriptCommandAsync();
        _battlefieldUnitGrid.SelectionChanged += (_, _) =>
        {
            if (!TryCommitPendingBattlefieldConsoleChanges()) return;
            ShowSelectedBattlefieldUnitCandidate();
        };
        _battlefieldUnitGrid.CellDoubleClick += (_, e) => SelectBattlefieldUnitCandidateInScriptTree(e.RowIndex);
        _battlefieldCommandGrid.CellDoubleClick += (_, e) => SelectBattlefieldCommandCandidateInScriptTree(e.RowIndex);
        _battlefieldMapPreviewBox.MouseDown += (_, e) => BeginBattlefieldPlacedUnitInteraction(e);
        _battlefieldMapPreviewBox.MouseMove += (_, e) =>
        {
            UpdateBattlefieldMapHover(e.Location);
            ContinueBattlefieldPlacedUnitInteraction(e);
        };
        _battlefieldMapPreviewBox.MouseUp += (_, e) => EndBattlefieldPlacedUnitInteraction(e.Location);
        _battlefieldMapPreviewBox.MouseDoubleClick += (_, e) => FocusBattlefieldConsoleFromMapDoubleClick(e);
        _battlefieldMapPreviewBox.MouseLeave += (_, _) =>
        {
            ClearBattlefieldMapHover();
            EndBattlefieldPlacedUnitInteraction(null);
        };
        _battlefieldMapPreviewBox.MouseWheel += (_, e) => HandleBattlefieldMapMouseWheel(e);
        _battlefieldMapPreviewBox.MouseEnter += (_, _) => _battlefieldMapScrollPanel.Focus();
        _battlefieldMapScrollPanel.MouseWheel += (_, e) => HandleBattlefieldMapMouseWheel(e);
        _battlefieldMapScrollPanel.MouseEnter += (_, _) => _battlefieldMapScrollPanel.Focus();
        _battlefieldMapZoomResetButton.Click += (_, _) => ResetBattlefieldMapZoom();
        _battlefieldDeploymentPreviewFilterCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingBattlefieldScenarioDocument)
            {
                RefreshBattlefieldDeploymentPreviewFilter();
            }
        };
        _markBattlefieldCommand25Button.Click += (_, _) => ToggleBattlefieldCommand25Preview();
        _battlefieldScriptTree.AfterSelect += (_, _) =>
        {
            if (!TryCommitPendingBattlefieldConsoleChanges()) return;
            ShowSelectedBattlefieldScriptNode();
        };
        _battlefieldScriptTree.AfterCheck += (_, e) => HandleLegacyScriptTreeNodeAfterCheck(LegacyScriptEditorScope.Battlefield, e);
        _battlefieldScriptTree.NodeMouseClick += (_, e) => HandleLegacyScriptTreeNodeMouseClick(LegacyScriptEditorScope.Battlefield, e);
        _battlefieldScriptTree.NodeMouseDoubleClick += (_, e) => HandleBattlefieldScriptTreeNodeMouseDoubleClick(e);
        _battlefieldScriptTree.KeyDown += (_, e) => HandleLegacyScriptTreeKeyDown(LegacyScriptEditorScope.Battlefield, e);
        _battlefieldScriptTextBox.TextChanged += (_, _) => UpdateBattlefieldScriptTextCapacityLabel();
        _saveBattlefieldScriptTextButton.Click += async (_, _) => await SaveSelectedBattlefieldScriptTextAsync();
        _saveBattlefieldScriptStructureButton.Click += async (_, _) => await SaveCurrentBattlefieldLegacyScriptStructureAsync();
        _showBattlefieldVariablesButton.Click += (_, _) => ShowScriptVariableUsageDialog(LegacyScriptEditorScope.Battlefield);
        _battlefieldScriptParameterGrid.SelectionChanged += (_, _) => ShowSelectedBattlefieldScriptParameter();
        _battlefieldScriptParameterGrid.CellDoubleClick += (_, _) => QueueEditSelectedBattlefieldScriptParameters();
        _battlefieldScriptParameterValueBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || !_applyBattlefieldScriptParameterButton.Enabled) return;
            ApplySelectedBattlefieldScriptParameterValue();
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
        _applyBattlefieldScriptParameterButton.Click += (_, _) => ApplySelectedBattlefieldScriptParameterValue();
        _editBattlefieldScriptParametersButton.Click += (_, _) => EditSelectedBattlefieldScriptParameters();
        _battlefieldScriptSearchButton.Click += (_, _) => ApplyBattlefieldScriptSearch();
        _battlefieldScriptClearSearchButton.Click += (_, _) => ClearBattlefieldScriptSearch();
        _battlefieldScriptSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyBattlefieldScriptSearch();
            e.SuppressKeyPress = true;
        };
        _battlefieldScriptReplaceBox.TextChanged += (_, _) => UpdateLegacyScriptReplaceButton(LegacyScriptEditorScope.Battlefield);
        _battlefieldScriptReplaceButton.Click += (_, _) => ReplaceLegacyScriptSearchMatches(LegacyScriptEditorScope.Battlefield);
        _battlefieldScriptSearchResultGrid.CellDoubleClick += (_, _) => ShowSelectedLegacyScriptSearchResult(LegacyScriptEditorScope.Battlefield);
        _battlefieldUnitPaletteFilterBox.TextChanged += (_, _) => ApplyBattlefieldUnitPaletteFilter();
        _battlefieldUnitListBox.SelectedIndexChanged += (_, _) => ShowSelectedBattlefieldPaletteUnit();
        _battlefieldUnitListBox.MouseDown += (_, e) => BeginBattlefieldUnitDrag(e.Location);
        _battlefieldUnitListBox.MouseMove += (_, e) => ContinueBattlefieldUnitDrag(e.Location, e.Button);
        _battlefieldUnitListBox.MouseUp += (_, _) => ClearBattlefieldUnitDrag();
        _battlefieldMapPreviewBox.AllowDrop = true;
        _battlefieldMapPreviewBox.DragEnter += (_, e) => HandleBattlefieldMapDragEnter(e);
        _battlefieldMapPreviewBox.DragDrop += (_, e) => HandleBattlefieldMapDragDrop(e);
        _battlefieldRemovePlacedUnitButton.Click += (_, _) =>
        {
            if (!TryCommitPendingBattlefieldConsoleChanges()) return;
            RemoveSelectedBattlefieldPlacedUnit();
        };
        _battlefieldClearPlacedUnitsButton.Click += (_, _) => ClearBattlefieldPlacedUnits();
        _battlefieldFactionAllyRadio.CheckedChanged += (_, _) =>
        {
            if (!_battlefieldFactionAllyRadio.Checked) return;
            if (!TryCommitPendingBattlefieldConsoleChanges()) return;
            HandleBattlefieldFactionChanged();
        };
        _battlefieldFactionFriendRadio.CheckedChanged += (_, _) =>
        {
            if (!_battlefieldFactionFriendRadio.Checked) return;
            if (!TryCommitPendingBattlefieldConsoleChanges()) return;
            HandleBattlefieldFactionChanged();
        };
        _battlefieldFactionEnemyRadio.CheckedChanged += (_, _) =>
        {
            if (!_battlefieldFactionEnemyRadio.Checked) return;
            if (!TryCommitPendingBattlefieldConsoleChanges()) return;
            HandleBattlefieldFactionChanged();
        };
        _battlefieldHiddenCheckBox.CheckedChanged += (_, _) => MarkBattlefieldConsolePlacementDirty(BattlefieldBatchEditField.Hidden);
        _battlefieldLevelOffsetInput.ValueChanged += (_, _) => MarkBattlefieldConsolePlacementDirty(BattlefieldBatchEditField.LevelOffset);
        _battlefieldLevelModeCombo.SelectedIndexChanged += (_, _) =>
        {
            MarkBattlefieldConsolePlacementDirty(BattlefieldBatchEditField.LevelMode);
            RefreshBattlefieldPaletteUnitPreview(_battlefieldUnitListBox.SelectedItem as BattlefieldUnitPaletteItem);
        };
        _battlefieldAiModeCombo.SelectedIndexChanged += (_, _) => MarkBattlefieldConsolePlacementDirty(BattlefieldBatchEditField.AiMode);
        _battlefieldDirectionCombo.SelectedIndexChanged += (_, _) =>
        {
            MarkBattlefieldConsolePlacementDirty(BattlefieldBatchEditField.Direction);
            RefreshBattlefieldPaletteUnitPreview(_battlefieldUnitListBox.SelectedItem as BattlefieldUnitPaletteItem);
        };
        _battlefieldConsoleWeaponCombo.SelectedIndexChanged += (_, _) => MarkBattlefieldConsoleStatusDirty(BattlefieldConsoleDirtyKind.Equipment, BattlefieldBatchEditField.Weapon);
        _battlefieldConsoleWeaponLevelInput.ValueChanged += (_, _) => MarkBattlefieldConsoleStatusDirty(BattlefieldConsoleDirtyKind.Equipment, BattlefieldBatchEditField.WeaponLevel);
        _battlefieldConsoleArmorCombo.SelectedIndexChanged += (_, _) => MarkBattlefieldConsoleStatusDirty(BattlefieldConsoleDirtyKind.Equipment, BattlefieldBatchEditField.Armor);
        _battlefieldConsoleArmorLevelInput.ValueChanged += (_, _) => MarkBattlefieldConsoleStatusDirty(BattlefieldConsoleDirtyKind.Equipment, BattlefieldBatchEditField.ArmorLevel);
        _battlefieldConsoleAssistCombo.SelectedIndexChanged += (_, _) => MarkBattlefieldConsoleStatusDirty(BattlefieldConsoleDirtyKind.Equipment, BattlefieldBatchEditField.Assist);
        _battlefieldConsoleJobCombo.SelectedIndexChanged += (_, _) => MarkBattlefieldConsoleStatusDirty(BattlefieldConsoleDirtyKind.RuntimeAbility, BattlefieldBatchEditField.Job);
        _battlefieldConsoleAbilityGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_battlefieldConsoleAbilityGrid.IsCurrentCellDirty)
            {
                _battlefieldConsoleAbilityGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _battlefieldConsoleAbilityGrid.CellValueChanged += (_, _) => MarkBattlefieldConsoleStatusDirty(BattlefieldConsoleDirtyKind.RuntimeAbility, BattlefieldBatchEditField.Ability);
        _battlefieldConsoleAbilityGrid.CellEndEdit += (_, _) =>
        {
            if (IsBattlefieldBatchEditingActive) return;
            MarkBattlefieldConsoleStatusDirty(BattlefieldConsoleDirtyKind.RuntimeAbility, BattlefieldBatchEditField.Ability);
        };
        _battlefieldConsoleAbilityGrid.DataError += (_, e) => e.ThrowException = false;
        RegisterBattlefieldConsoleDeferredCommitHandlers();
        _loadRSceneButton.Click += async (_, _) => await LoadRSceneScenariosAsync();
        _rSceneScenarioCombo.SelectedIndexChanged += async (_, _) => await LoadSelectedRSceneScenarioAsync();
        _saveRSceneDraftButton.Click += (_, _) => SaveRSceneDraft();
        _saveRSceneScriptStructureButton.Click += async (_, _) => await SaveCurrentRSceneLegacyScriptStructureAsync();
        _showRSceneVariablesButton.Click += (_, _) => ShowScriptVariableUsageDialog(LegacyScriptEditorScope.RScene);
        _jumpRSceneScriptButton.Click += async (_, _) => await JumpRSceneScriptAsync();
        _rSceneScriptSearchButton.Click += (_, _) => ApplyLegacyScriptSearch(LegacyScriptEditorScope.RScene);
        _rSceneScriptClearSearchButton.Click += (_, _) => ClearLegacyScriptSearch(LegacyScriptEditorScope.RScene);
        _rSceneScriptSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyLegacyScriptSearch(LegacyScriptEditorScope.RScene);
            e.SuppressKeyPress = true;
        };
        _rSceneScriptReplaceBox.TextChanged += (_, _) => UpdateLegacyScriptReplaceButton(LegacyScriptEditorScope.RScene);
        _rSceneScriptReplaceButton.Click += (_, _) => ReplaceLegacyScriptSearchMatches(LegacyScriptEditorScope.RScene);
        _rSceneScriptSearchResultGrid.CellDoubleClick += (_, _) => ShowSelectedLegacyScriptSearchResult(LegacyScriptEditorScope.RScene);
        _applyRSceneInlineDialogButton.Click += (_, _) => ApplyInlineRSceneScriptDialog();
        _resetRSceneInlineDialogButton.Click += (_, _) => LoadInlineRSceneScriptDialogForSelection();
        _rSceneScriptTree.AfterSelect += (_, _) => ShowSelectedRSceneScriptNode();
        _rSceneScriptTree.AfterCheck += (_, e) => HandleLegacyScriptTreeNodeAfterCheck(LegacyScriptEditorScope.RScene, e);
        _rSceneScriptTree.NodeMouseClick += (_, e) => HandleLegacyScriptTreeNodeMouseClick(LegacyScriptEditorScope.RScene, e);
        _rSceneScriptTree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _rSceneScriptTree.SelectedNode = e.Node;
            ShowSelectedRSceneScriptNode();
            EditSelectedRSceneScriptCommand();
        };
        _rSceneScriptTree.KeyDown += (_, e) => HandleLegacyScriptTreeKeyDown(LegacyScriptEditorScope.RScene, e);
        _rSceneCommandGrid.SelectionChanged += (_, _) => ShowSelectedRSceneCommandCandidate();
        _rSceneCommandGrid.CellDoubleClick += (_, e) =>
        {
            SelectRSceneCommandCandidateInScriptTree(e.RowIndex);
            EditSelectedRSceneScriptCommand();
        };
        _rSceneActorFilterBox.TextChanged += (_, _) => ApplyRSceneActorPaletteFilter();
        _rSceneActorListBox.SelectedIndexChanged += (_, _) => ShowSelectedRScenePaletteActor();
        _rSceneFrameListView.SelectedIndexChanged += (_, _) => SelectRSceneFrameFromList();
        _rSceneFrameListView.MouseDown += (_, e) => BeginRSceneFrameDrag(e.Location);
        _rSceneFrameListView.MouseMove += (_, e) => ContinueRSceneFrameDrag(e.Location, e.Button);
        _rSceneFrameListView.MouseUp += (_, _) => ClearRSceneFrameDrag();
        _rSceneCanvasBox.AllowDrop = true;
        _rSceneCanvasBox.DragEnter += (_, e) => HandleRSceneCanvasDragEnter(e);
        _rSceneCanvasBox.DragOver += (_, e) => HandleRSceneCanvasDragOver(e);
        _rSceneCanvasBox.DragLeave += (_, _) => ClearRSceneCanvasDragPreview();
        _rSceneCanvasBox.DragDrop += (_, e) => HandleRSceneCanvasDragDrop(e);
        _rSceneCanvasBox.MouseDown += (_, e) => BeginRSceneCanvasActorInteraction(e);
        _rSceneCanvasBox.MouseMove += (_, e) => ContinueRSceneCanvasActorInteraction(e);
        _rSceneCanvasBox.MouseUp += (_, _) => EndRSceneCanvasActorInteraction();
        _rSceneCanvasBox.MouseDoubleClick += (_, e) => HandleRSceneCanvasActorDoubleClick(e);
        _rSceneCanvasBox.MouseWheel += (_, e) => HandleRSceneCanvasMouseWheel(e);
        _rSceneCanvasBox.MouseEnter += (_, _) => _rSceneCanvasScrollPanel.Focus();
        _rSceneCanvasScrollPanel.MouseWheel += (_, e) => HandleRSceneCanvasMouseWheel(e);
        _rSceneCanvasScrollPanel.MouseEnter += (_, _) => _rSceneCanvasScrollPanel.Focus();
        _rSceneZoomResetButton.Click += (_, _) => ResetRSceneCanvasZoom();
        _rScenePreviewLockButton.Click += (_, _) => ToggleRScenePreviewLock();
        _rSceneBackgroundCombo.SelectedIndexChanged += (_, _) => HandleRSceneBackgroundSelectionChanged();
        _rSceneGridSizeInput.ValueChanged += (_, _) => RenderRSceneCanvasIfNotSuppressed();
        _rSceneShowGridCheckBox.CheckedChanged += (_, _) => RenderRSceneCanvasIfNotSuppressed();
        _rSceneDialoguePreviewCheckBox.CheckedChanged += (_, _) => RenderRSceneCanvasIfNotSuppressed();
        _rSceneFacingCombo.SelectedIndexChanged += (_, _) =>
        {
            ApplyRSceneControlPanelToSelectedActor();
            RefreshRScenePaletteActorPreview(_rSceneActorListBox.SelectedItem as RSceneActorPaletteItem);
        };
        _rSceneStanceInput.ValueChanged += (_, _) =>
        {
            if (_bindingRSceneFrameSelection) return;
            ApplyRSceneControlPanelToSelectedActor();
            RefreshRScenePaletteActorPreview(_rSceneActorListBox.SelectedItem as RSceneActorPaletteItem);
        };
        _rScenePlaybackButton.Click += (_, _) => ToggleRScenePlayback();
        _rScenePlaybackDelayInput.ValueChanged += (_, _) => UpdateRScenePlaybackTimerInterval();
        _loadScriptButton.Click += async (_, _) => await LoadScriptScenariosAsync();
        _scriptScenarioCombo.SelectedIndexChanged += async (_, _) => await LoadSelectedScriptScenarioAsync();
        _scriptSearchButton.Click += (_, _) => ApplyScriptSearch();
        _scriptClearSearchButton.Click += (_, _) => ClearScriptSearch();
        _scriptSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyScriptSearch();
            e.SuppressKeyPress = true;
        };
        _scriptReplaceBox.TextChanged += (_, _) => UpdateLegacyScriptReplaceButton(LegacyScriptEditorScope.Script);
        _scriptReplaceButton.Click += (_, _) => ReplaceLegacyScriptSearchMatches(LegacyScriptEditorScope.Script);
        _showScriptVariablesButton.Click += (_, _) => ShowScriptVariableUsageDialog(LegacyScriptEditorScope.Script);
        _locateScriptCommandButton.Click += (_, _) => LocateSelectedScriptCommandInTree();
        _copyScriptCommandButton.Click += (_, _) => CopySelectedScriptCommandSummary();
        _cutScriptCommandButton.Click += (_, _) => CutSelectedLegacyScriptCommand();
        _previewPasteScriptCommandButton.Click += (_, _) => PreviewPasteScriptCommandCandidate();
        _scriptNewCommandCombo.SelectedIndexChanged += (_, _) => UpdateScriptStructureEditButtons();
        _appendScriptCommandToSectionButton.Click += (_, _) => AppendLegacyScriptCommandToSection();
        _insertScriptCommandBeforeButton.Click += (_, _) => InsertLegacyScriptCommandNearSelected(beforeSelected: true);
        _insertScriptCommandAfterButton.Click += (_, _) => InsertLegacyScriptCommandNearSelected(beforeSelected: false);
        _appendScriptCommandToChildBlockButton.Click += (_, _) => AppendLegacyScriptCommandToChildBlock();
        _deleteScriptCommandButton.Click += (_, _) => DeleteSelectedLegacyScriptCommand();
        _pasteScriptCommandBeforeButton.Click += (_, _) => PasteCopiedLegacyScriptCommandNearSelected(beforeSelected: true);
        _pasteScriptCommandAfterButton.Click += (_, _) => PasteCopiedLegacyScriptCommandNearSelected(beforeSelected: false);
        _moveScriptCommandUpButton.Click += (_, _) => MoveSelectedLegacyScriptCommand(up: true);
        _moveScriptCommandDownButton.Click += (_, _) => MoveSelectedLegacyScriptCommand(up: false);
        _saveScriptStructureButton.Click += async (_, _) => await SaveCurrentLegacyScriptStructureAsync();
        _jumpScriptBattlefieldButton.Click += async (_, _) => await JumpScriptBattlefieldAsync();
        _scriptTree.AfterSelect += (_, _) => ShowSelectedScriptTreeNode();
        _scriptSearchResultGrid.CellDoubleClick += (_, _) => ShowSelectedLegacyScriptSearchResult(LegacyScriptEditorScope.Script);
        _scriptParameterGrid.SelectionChanged += (_, _) => ShowSelectedLegacyScriptParameter();
        _scriptParameterGrid.CellDoubleClick += (_, _) => EditSelectedLegacyScriptParameters();
        _scriptParameterValueBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            EditSelectedLegacyItemDataCommand();
            e.SuppressKeyPress = true;
        };
        _applyScriptParameterValueButton.Click += (_, _) => EditSelectedLegacyItemDataCommand();
        _editScriptParametersButton.Click += (_, _) => EditSelectedLegacyItemDataCommand();
        _applyScriptInlineDialogButton.Click += (_, _) => ApplyInlineLegacyScriptDialog();
        _resetScriptInlineDialogButton.Click += (_, _) => LoadInlineLegacyScriptDialogForSelection();
        _scriptTextEditorBox.TextChanged += (_, _) => UpdateScriptTextCapacityLabel();
        _loadMapImagesButton.Click += (_, _) => LoadMapImages();
        _mapImageList.SelectedIndexChanged += (_, _) => LoadSelectedMapImage();
        _mapMakerNewDraftButton.Click += (_, _) => CreateNewMapWorkbenchDraftFromInputs();
        _mapMakerLoadLastDraftButton.Click += (_, _) => LoadLastMapWorkbenchDraft();
        _mapMakerSaveDraftButton.Click += (_, _) => SaveCurrentMapWorkbenchDraft();
        _mapWorkbenchModeTabs.SelectedIndexChanged += (_, _) => HandleMapWorkbenchModeTabChanged();
        _mapMakerGridWidthInput.ValueChanged += (_, _) => ResizeCurrentMapWorkbenchDraftFromInputs();
        _mapMakerGridHeightInput.ValueChanged += (_, _) => ResizeCurrentMapWorkbenchDraftFromInputs();
        _mapMakerSelectMaterialRootButton.Click += (_, _) => SelectMapWorkbenchMaterialRoot();
        _mapZoomTrackBar.ValueChanged += (_, _) => ApplyMapZoom();
        _mapFitButton.Click += (_, _) => FitMapToView();
        _mapActualButton.Click += (_, _) =>
        {
            _mapZoomTrackBar.Value = 100;
            ApplyMapZoom();
        };
        _mapMakerShowTerrainCheckBox.CheckedChanged += (_, _) =>
        {
            if (_mapMakerShowTerrainCheckBox.Checked)
            {
                TryLoadMapMakerTerrainForSelectedMap(showMessage: false);
            }
            else
            {
                FlushMapMakerDirtyBasePreview(runBeautify: false);
            }

            RenderMapMakerPreview();
        };
        _mapMakerShowGridCheckBox.CheckedChanged += (_, _) => RenderMapMakerPreview();
        _mapMakerAutoGenerateCheckBox.CheckedChanged += (_, _) =>
        {
            if (_currentMapWorkbenchDraft != null)
            {
                _currentMapWorkbenchDraft.AutoGenerateMapFromTerrain = true;
            }
            if (!_mapMakerAutoGenerateCheckBox.Visible)
            {
                if (!_mapMakerAutoGenerateCheckBox.Checked)
                {
                    _mapMakerAutoGenerateCheckBox.Checked = true;
                }

                return;
            }

            RenderMapMakerPreview(force: true);
        };
        _mapMakerBeautifyCheckBox.Click += (_, _) => _ = BeautifyCurrentGeneratedMapAsync();
        _mapMakerRollbackBeautifyButton.Click += (_, _) => RollbackCurrentMapBeautify();
        _mapMakerBeautifyFilterCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingMapMakerBeautifyFilterSelection) return;
            if (_currentMapWorkbenchDraft != null)
            {
                var previousProfile = _currentMapWorkbenchDraft.BeautifyFilterProfile;
                var selectedProfile = GetSelectedBeautifyFilterProfile();
                if (selectedProfile.Equals(TerrainBeautifyFilterProfiles.Custom, StringComparison.OrdinalIgnoreCase) &&
                    !TryConfigureCustomBeautifyFilter(requireDialog: true))
                {
                    SetSelectedBeautifyFilterProfile(previousProfile);
                    return;
                }

                _currentMapWorkbenchDraft.BeautifyFilterProfile = selectedProfile;
            }
            MarkCurrentGeneratedMapNeedsBeautify();
            RenderMapMakerPreview(force: true);
        };
        _mapMakerBeautifyStrengthInput.ValueChanged += (_, _) =>
        {
            if (_currentMapWorkbenchDraft != null)
            {
                _currentMapWorkbenchDraft.BeautifyStrength = (int)_mapMakerBeautifyStrengthInput.Value;
            }
            MarkCurrentGeneratedMapNeedsBeautify();
            RenderMapMakerPreview(force: true);
        };
        _mapMakerFeatherRadiusInput.ValueChanged += (_, _) =>
        {
            if (_currentMapWorkbenchDraft != null)
            {
                _currentMapWorkbenchDraft.FeatherRadius = (int)_mapMakerFeatherRadiusInput.Value;
            }
            MarkCurrentGeneratedMapNeedsBeautify();
            RenderMapMakerPreview(force: true);
        };
        _mapMakerTerrainOpacityTrackBar.ValueChanged += (_, _) =>
        {
            _mapMakerTerrainOpacityLabel.Text = $"地形透明度 {_mapMakerTerrainOpacityTrackBar.Value}%";
            RenderMapMakerPreview();
        };
        _mapMakerTerrainLayerViewRadio.CheckedChanged += (_, _) =>
        {
            if (_mapMakerTerrainLayerViewRadio.Checked)
            {
                RenderMapMakerPreview(force: true);
            }
        };
        _mapMakerTerrainGeneratedViewRadio.CheckedChanged += (_, _) =>
        {
            if (_mapMakerTerrainGeneratedViewRadio.Checked)
            {
                RenderMapMakerPreview(force: true);
            }
        };
        _mapMakerTerrainPresetCombo.SelectedIndexChanged += (_, _) => SelectMapMakerTerrainPreset();
        _mapMakerTerrainBrushInput.ValueChanged += (_, _) => UpdateMapMakerBrushLabel();
        _mapMakerSaveTerrainButton.Click += (_, _) => SaveCurrentMapWorkbenchDraft();
        _mapMakerUndoTerrainButton.Click += (_, _) => UndoMapWorkbenchPaint();
        _mapMakerRedoTerrainButton.Click += (_, _) => RedoMapWorkbenchPaint();
        _mapMakerReplaceMapImageButton.Click += (_, _) => ReplaceCurrentMapImage();
        _mapMakerExportPreviewButton.Click += (_, _) => ExportCurrentMapMakerPreviewPng();
        _mapMakerExportJpgButton.Click += (_, _) => ExportCurrentMapWorkbenchJpg();
        _mapMakerExtractMaterialButton.Click += (_, _) => OpenMapMaterialExtractionDialogFromSelection();
        _mapMakerMaterialPlanButton.Click += (_, _) => OpenMapWorkbenchMaterialPlanDialog();
        _mapMakerTerrainStyleButton.Click += (_, _) => GenerateTerrainStyleAlignedPreviewFromPage();
        _mapMakerPublishAllButton.Click += (_, _) => PublishCurrentMapWorkbenchMapAndTerrain();
        _mapMakerPublishMapButton.Click += (_, _) => PublishCurrentMapWorkbenchMapImage();
        _mapMakerPublishTerrainButton.Click += (_, _) => PublishCurrentMapWorkbenchTerrain();
        _mapMakerMaterialSearchBox.TextChanged += (_, _) => HandleMapWorkbenchMaterialSearchChanged();
        _mapMakerMaterialSearchBox.Enter += (_, _) => HandleMapWorkbenchMaterialBrowserInteraction();
        _mapMakerMaterialSearchBox.MouseDown += (_, _) => HandleMapWorkbenchMaterialBrowserInteraction();
        _mapMakerMaterialTree.Enter += (_, _) => HandleMapWorkbenchMaterialBrowserInteraction();
        _mapMakerMaterialTree.MouseDown += (_, _) => HandleMapWorkbenchMaterialBrowserInteraction();
        _mapMakerMaterialTree.AfterSelect += (_, _) => PopulateMapWorkbenchMaterialListForSelection();
        _mapMakerMaterialListView.Enter += (_, _) => HandleMapWorkbenchMaterialBrowserInteraction();
        _mapMakerMaterialListView.MouseDown += (_, _) => HandleMapWorkbenchMaterialBrowserInteraction();
        _mapMakerMaterialListView.SelectedIndexChanged += (_, _) => SelectMapWorkbenchMaterialFromListView();
        ConfigureMapViewerContextMenu();
        _mapViewerBox.MouseDown += (_, e) => BeginMapMakerTerrainPaint(e);
        _mapViewerBox.MouseMove += (_, e) => ContinueMapMakerTerrainPaint(e);
        _mapViewerBox.MouseUp += (_, _) => EndMapMakerTerrainPaint();
        _mapViewerBox.Paint += (_, e) => PaintMapWorkbenchScenerySelection(e.Graphics);
        _mapViewerBox.PreviewKeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Delete or Keys.Oemplus or Keys.Add or Keys.OemMinus or Keys.Subtract or Keys.OemOpenBrackets or Keys.OemCloseBrackets)
            {
                e.IsInputKey = true;
            }
        };
        _mapViewerBox.KeyDown += (_, e) => HandleMapWorkbenchSceneryKeyDown(e);
        _mapViewerBox.MouseLeave += (_, _) =>
        {
            EndMapMakerTerrainPaint();
            ClearMapMakerCellPreview();
        };
        _loadHexzmapProbeButton.Click += (_, _) => LoadHexzmapProbe();
        _exportHexzmapProbeCsvButton.Click += (_, _) => ExportHexzmapProbeCsv();
        _exportHexzmapOverlayPngButton.Click += (_, _) => ExportHexzmapOverlayPng();
        _hexzmapGrid.SelectionChanged += (_, _) => ShowSelectedHexzmapBlock();
        _hexzmapOverlayMapCheckBox.CheckedChanged += (_, _) => ShowSelectedHexzmapBlock();
        _hexzmapOverlayOpacityTrackBar.ValueChanged += (_, _) =>
        {
            _hexzmapOverlayOpacityLabel.Text = $"地形透明度 {_hexzmapOverlayOpacityTrackBar.Value}%";
            ShowSelectedHexzmapBlock();
        };
        _hexzmapPreviewBox.MouseMove += (_, e) => UpdateHexzmapCellPreview(e.Location);
        _hexzmapPreviewBox.MouseLeave += (_, _) => ClearHexzmapCellPreview();
    }
}
