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
    private TabPage BuildXiaoAnMessagePage()
    {
        var page = new TabPage("小暗的话");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var logoPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8)
        };
        var logoBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        var logo = LoadXiaoAnLogo();
        if (logo != null)
        {
            logoBox.Image = logo;
            logoPanel.Controls.Add(logoBox);
        }
        else
        {
            logoPanel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "未找到 doro.jpg",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(96, 96, 96)
            });
        }

        var messageBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            DetectUrls = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = true,
            Text = """
大家好，我是一个想要用AI把普罗大佬的曹操传工具整合起来，并在整合的过程中，建立一个曹操传mod制作知识库的曹操传mod爱好者，传圈艺名小暗，兽设是doro
我觉得，现有的AI，能力是完全足够支持曹操传mod制作的，但是因为缺乏相关的数据支持，所以才表现出技术缺陷，难以直接完成端到端的曹操传mod编写。
基于这个观点，我打算先将现有的制作工具喂给AI，让AI掌握曹操传mod的编辑过程以及经验，制作出一款AI能够理解掌握的曹操传mod编辑工具，并且在制作过程中总结出适合AI学习的经验，以更好的辅助AI完成曹操传mod的编辑
对此，我使用AI完成了对现有的普罗工具的大整合，并在此基础上增加了一些便利创作者的功能。同时，必须承认，现有的工具包还存在着很多问题，比如：目前只是基于6.5引擎完成的制作，可能不能适配其他版本引擎；UI不合理，使用不便捷，缺少便利创作者的快捷功能；单挑编辑器尚未完成；存在隐藏bug.......这些缺少同好者去测试，需要大家向我反馈以便做出改进，希望能和大家携手共进，一起创造更便捷的工具，维护更好的传圈生态！
我的QQ号是：1033891854   常驻QQ群号：708348576   欢迎大家来QQ群中与其他曹操传mod同好者一起交流曹操传mod、接受最新工具整合包更新成果、向我提出改进意见！（群里的大佬真的很多哦！）
Github源码链接：https://github.com/Artkights/AI-For-CCZ.git
贴吧发布帖（不定时在末尾更新）：https://tieba.baidu.com/p/10770768879?fr=personpage
"""
        };

        layout.Controls.Add(logoPanel, 0, 0);
        layout.Controls.Add(messageBox, 0, 1);
        return page;
    }

    private static Image? LoadXiaoAnLogo()
    {
        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "About", "doro.jpg"),
            Path.Combine(AppContext.BaseDirectory, "doro.jpg"),
            Path.Combine(Environment.CurrentDirectory, "工具整合包", "CCZModStudio", "Assets", "About", "doro.jpg"),
            Path.Combine(Environment.CurrentDirectory, "doro.jpg")
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;

            try
            {
                using var image = Image.FromFile(path);
                return new Bitmap(image);
            }
            catch
            {
                // The page is informational; keep it usable even if the optional logo cannot load.
            }
        }

        return null;
    }

    private TabPage BuildMapEditorPage()
    {
        var mapViewerPage = new TabPage("地图编辑");
        var mapLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        mapLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mapLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mapViewerPage.Controls.Add(mapLayout);
        var mapToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadMapImagesButton.Text = "读取 Map 图片";
        _loadMapImagesButton.AutoSize = true;
        _mapMakerNewDraftButton.Text = "新建草稿";
        _mapMakerNewDraftButton.AutoSize = true;
        _mapMakerLoadLastDraftButton.Text = "载入上次草稿";
        _mapMakerLoadLastDraftButton.AutoSize = true;
        _mapMakerSaveDraftButton.Text = "保存草稿";
        _mapMakerSaveDraftButton.AutoSize = true;
        _mapMakerSaveDraftButton.Enabled = false;
        _mapMakerGridWidthInput.Minimum = 1;
        _mapMakerGridWidthInput.Maximum = 256;
        _mapMakerGridWidthInput.Value = 30;
        _mapMakerGridWidthInput.Width = 58;
        _mapMakerGridHeightInput.Minimum = 1;
        _mapMakerGridHeightInput.Maximum = 256;
        _mapMakerGridHeightInput.Value = 30;
        _mapMakerGridHeightInput.Width = 58;
        _mapMakerBrushModeCombo.Visible = false;
        _mapMakerSelectMaterialRootButton.Visible = false;
        _mapFitButton.Text = "适应窗口";
        _mapFitButton.AutoSize = true;
        _mapActualButton.Text = "100%";
        _mapActualButton.AutoSize = true;
        _mapZoomTrackBar.Minimum = 10;
        _mapZoomTrackBar.Maximum = 300;
        _mapZoomTrackBar.Value = 100;
        _mapZoomTrackBar.TickFrequency = 50;
        _mapZoomTrackBar.Width = 140;
        _mapMakerShowTerrainCheckBox.Text = "查看地形层";
        _mapMakerShowTerrainCheckBox.AutoSize = true;
        _mapMakerShowTerrainCheckBox.Checked = false;
        _mapMakerShowTerrainCheckBox.Visible = false;
        _mapMakerShowGridCheckBox.Text = "显示网格";
        _mapMakerShowGridCheckBox.AutoSize = true;
        _mapMakerShowGridCheckBox.Checked = true;
        _mapMakerAutoGenerateCheckBox.Text = "地形驱动";
        _mapMakerAutoGenerateCheckBox.AutoSize = true;
        _mapMakerAutoGenerateCheckBox.Checked = true;
        _mapMakerAutoGenerateCheckBox.Visible = false;
        _mapMakerBeautifyCheckBox.Text = "美化当前地图";
        _mapMakerBeautifyCheckBox.AutoSize = true;
        _mapMakerBeautifyCheckBox.Enabled = false;
        _mapMakerRollbackBeautifyButton.Text = "回退美化";
        _mapMakerRollbackBeautifyButton.AutoSize = true;
        _mapMakerRollbackBeautifyButton.Enabled = false;
        _mapMakerBeautifyStrengthInput.Minimum = 0;
        _mapMakerBeautifyStrengthInput.Maximum = 3;
        _mapMakerBeautifyStrengthInput.Value = 2;
        _mapMakerBeautifyStrengthInput.Width = 48;
        _mapMakerBeautifyStrengthInput.Visible = false;
        _mapMakerFeatherRadiusInput.Minimum = 0;
        _mapMakerFeatherRadiusInput.Maximum = 24;
        _mapMakerFeatherRadiusInput.Value = 8;
        _mapMakerFeatherRadiusInput.Width = 52;
        _mapMakerFeatherRadiusInput.Visible = false;
        _mapMakerEditTerrainCheckBox.Text = "地形画笔";
        _mapMakerEditTerrainCheckBox.AutoSize = true;
        _mapMakerEditTerrainCheckBox.Enabled = false;
        _mapMakerEditTerrainCheckBox.Visible = false;
        _mapMakerTerrainOpacityTrackBar.Minimum = 0;
        _mapMakerTerrainOpacityTrackBar.Maximum = 100;
        _mapMakerTerrainOpacityTrackBar.Value = 45;
        _mapMakerTerrainOpacityTrackBar.TickFrequency = 25;
        _mapMakerTerrainOpacityTrackBar.Width = 110;
        _mapMakerTerrainOpacityTrackBar.Visible = false;
        _mapMakerTerrainOpacityLabel.Text = "地形透明度 45%";
        _mapMakerTerrainOpacityLabel.AutoSize = true;
        _mapMakerTerrainOpacityLabel.Padding = new Padding(0, 7, 0, 0);
        _mapMakerTerrainOpacityLabel.Visible = false;
        _mapMakerTerrainPresetCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _mapMakerTerrainPresetCombo.Width = 170;
        _mapMakerTerrainBrushInput.Minimum = 0;
        _mapMakerTerrainBrushInput.Maximum = 255;
        _mapMakerTerrainBrushInput.Width = 62;
        _mapMakerTerrainBrushInput.Visible = false;
        _mapMakerBrushNameLabel.Text = "地形：0x00";
        _mapMakerBrushNameLabel.AutoSize = true;
        _mapMakerBrushNameLabel.Padding = new Padding(0, 7, 0, 0);
        _mapMakerSaveTerrainButton.Text = "保存草稿";
        _mapMakerSaveTerrainButton.AutoSize = true;
        _mapMakerSaveTerrainButton.Enabled = false;
        _mapMakerSaveTerrainButton.Visible = false;
        _mapMakerUndoTerrainButton.Text = "撤销";
        _mapMakerUndoTerrainButton.AutoSize = true;
        _mapMakerUndoTerrainButton.Enabled = false;
        _mapMakerRedoTerrainButton.Text = "重做";
        _mapMakerRedoTerrainButton.AutoSize = true;
        _mapMakerRedoTerrainButton.Enabled = false;
        _mapMakerReplaceMapImageButton.Text = "旧版替换底图";
        _mapMakerReplaceMapImageButton.AutoSize = true;
        _mapMakerReplaceMapImageButton.Enabled = false;
        _mapMakerReplaceMapImageButton.Visible = false;
        _mapMakerExportPreviewButton.Text = "导出PNG";
        _mapMakerExportPreviewButton.AutoSize = true;
        _mapMakerExportPreviewButton.Enabled = false;
        _mapMakerExportJpgButton.Text = "导出美化JPG";
        _mapMakerExportJpgButton.AutoSize = true;
        _mapMakerExportJpgButton.Enabled = false;
        _mapMakerMaterialPlanButton.Text = "主素材设置";
        _mapMakerMaterialPlanButton.AutoSize = true;
        _mapMakerMaterialPlanButton.Enabled = false;
        _mapMakerMaterialPlanButton.Visible = false;
        _mapMakerPublishMapButton.Text = "高级：仅底图";
        _mapMakerPublishMapButton.AutoSize = true;
        _mapMakerPublishMapButton.Enabled = false;
        _mapMakerPublishTerrainButton.Text = "高级：仅地形";
        _mapMakerPublishTerrainButton.AutoSize = true;
        _mapMakerPublishTerrainButton.Enabled = false;
        _mapMakerPublishAllButton.Text = "一键发布";
        _mapMakerPublishAllButton.AutoSize = true;
        _mapMakerPublishAllButton.Enabled = false;
        mapToolbar.Controls.AddRange(new Control[]
        {
            _loadMapImagesButton,
            _mapMakerNewDraftButton,
            _mapMakerLoadLastDraftButton,
            new Label { Text = "尺寸：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _mapMakerGridWidthInput,
            new Label { Text = "x", AutoSize = true, Padding = new Padding(0, 7, 0, 0) },
            _mapMakerGridHeightInput,
            _mapMakerSaveDraftButton,
            _mapFitButton,
            _mapActualButton,
            new Label { Text = "缩放：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _mapZoomTrackBar,
            _mapMakerShowGridCheckBox,
            _mapMakerBeautifyCheckBox,
            _mapMakerRollbackBeautifyButton,
            _mapMakerBrushNameLabel,
            _mapMakerUndoTerrainButton,
            _mapMakerRedoTerrainButton,
            _mapMakerExportPreviewButton,
            _mapMakerExportJpgButton,
            _mapMakerPublishAllButton
        });
        mapLayout.Controls.Add(mapToolbar, 0, 0);
        var mapSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildMapEditorPage.MapListEditor", mapSplit, desiredDistance: 260, desiredPanel1Min: 25, desiredPanel2Min: 25);
        _mapImageList.Dock = DockStyle.Fill;
        _mapImageList.HorizontalScrollbar = true;
        var mapEditorSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildMapEditorPage.CanvasMaterials", mapEditorSplit, desiredDistance: 760, desiredPanel1Min: 25, desiredPanel2Min: 25);
        var mapRightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        mapRightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mapRightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _mapViewerCellPreviewLabel.Dock = DockStyle.Fill;
        _mapViewerCellPreviewLabel.AutoSize = false;
        _mapViewerCellPreviewLabel.Height = 30;
        _mapViewerCellPreviewLabel.Padding = new Padding(8, 6, 8, 0);
        _mapViewerCellPreviewLabel.BackColor = Color.FromArgb(245, 245, 245);
        _mapViewerCellPreviewLabel.ForeColor = Color.FromArgb(32, 32, 32);
        _mapViewerCellPreviewLabel.BorderStyle = BorderStyle.FixedSingle;
        _mapViewerCellPreviewLabel.TextAlign = ContentAlignment.MiddleLeft;
        _mapViewerCellPreviewLabel.Text = "地形：-    坐标：-";
        var mapScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            TabStop = true,
            BackColor = Color.FromArgb(36, 36, 36)
        };
        mapScroll.MouseWheel += (_, e) => HandleMapViewerMouseWheel(e);
        mapScroll.MouseEnter += (_, _) => mapScroll.Focus();
        _mapViewerBox.Location = new Point(0, 0);
        _mapViewerBox.SizeMode = PictureBoxSizeMode.StretchImage;
        EnableDoubleBuffering(_mapViewerBox);
        _mapViewerBox.MouseWheel += (_, e) => HandleMapViewerMouseWheel(e);
        _mapViewerBox.MouseEnter += (_, _) => mapScroll.Focus();
        mapScroll.Controls.Add(_mapViewerBox);
        _mapViewerInfoBox.Dock = DockStyle.Fill;
        _mapViewerInfoBox.Multiline = true;
        _mapViewerInfoBox.ReadOnly = true;
        _mapViewerInfoBox.ScrollBars = ScrollBars.Vertical;
        _mapViewerInfoBox.WordWrap = true;
        _mapViewerInfoBox.Text = "地图编辑：左侧选择 Mxxx 地图后显示真实底图；右侧素材库按地形 / 建筑 / 景物选择图片绘制；点击“美化当前地图”生成美化预览，可用“回退美化”恢复普通预览。";
        mapRightLayout.Controls.Add(_mapViewerCellPreviewLabel, 0, 0);
        mapRightLayout.Controls.Add(mapScroll, 0, 1);
        var materialBrowserPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Padding = new Padding(6, 0, 0, 0)
        };
        materialBrowserPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        materialBrowserPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        materialBrowserPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        materialBrowserPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        materialBrowserPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        _mapMakerMaterialSearchBox.Dock = DockStyle.Fill;
        _mapMakerMaterialSearchBox.PlaceholderText = "搜索编号 / 名称 / 文件";
        _mapMakerMaterialTree.Dock = DockStyle.Fill;
        _mapMakerMaterialTree.HideSelection = false;
        _mapMakerMaterialTree.FullRowSelect = true;
        _mapMakerMaterialTree.ShowNodeToolTips = true;
        _mapMakerMaterialImageList.ImageSize = new Size(48, 48);
        _mapMakerMaterialImageList.ColorDepth = ColorDepth.Depth32Bit;
        _mapMakerMaterialListView.Dock = DockStyle.Fill;
        _mapMakerMaterialListView.View = View.LargeIcon;
        _mapMakerMaterialListView.LargeImageList = _mapMakerMaterialImageList;
        _mapMakerMaterialListView.MultiSelect = false;
        _mapMakerMaterialListView.HideSelection = false;
        _mapMakerMaterialPreview.Dock = DockStyle.Fill;
        _mapMakerMaterialPreview.SizeMode = PictureBoxSizeMode.Zoom;
        _mapMakerMaterialPreview.BorderStyle = BorderStyle.FixedSingle;
        _mapMakerMaterialInfoBox.Dock = DockStyle.Fill;
        _mapMakerMaterialInfoBox.Multiline = true;
        _mapMakerMaterialInfoBox.ReadOnly = true;
        _mapMakerMaterialInfoBox.ScrollBars = ScrollBars.Vertical;
        _mapMakerMaterialInfoBox.WordWrap = true;
        materialBrowserPanel.Controls.Add(_mapMakerMaterialSearchBox, 0, 0);
        materialBrowserPanel.Controls.Add(_mapMakerMaterialTree, 0, 1);
        materialBrowserPanel.Controls.Add(_mapMakerMaterialListView, 0, 2);
        materialBrowserPanel.Controls.Add(_mapMakerMaterialPreview, 0, 3);
        materialBrowserPanel.Controls.Add(_mapMakerMaterialInfoBox, 0, 4);
        mapEditorSplit.Panel1.Controls.Add(mapRightLayout);
        mapEditorSplit.Panel2.Controls.Add(materialBrowserPanel);
        AddCollapsibleSplitPanel(mapSplit, 1, "地图列表", _mapImageList, "BuildMapEditorPage.MapListEditor.MapList");
        AddCollapsibleSplitPanel(mapSplit, 2, "地图预览 / 素材库", mapEditorSplit, "BuildMapEditorPage.MapListEditor.MapPreview");
        mapLayout.Controls.Add(mapSplit, 0, 1);
        return mapViewerPage;
    }
    private TabPage BuildRoleEditorPage()
    {
        var page = new TabPage("角色设定");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadRoleEditorButton.Text = "读取角色";
        _loadRoleEditorButton.AutoSize = true;
        _saveRoleEditorButton.Text = "保存角色";
        _saveRoleEditorButton.AutoSize = true;
        _saveRoleEditorButton.Enabled = false;
        _openRoleInTableEditorButton.Text = "打开通用人物表";
        _openRoleInTableEditorButton.AutoSize = true;
        _openRolePersonalEffectButton.Text = "剧本专属/套装(72/10)";
        _openRolePersonalEffectButton.AutoSize = true;
        _openRoleEffectButton.Text = "个人特效(EXE表)";
        _openRoleEffectButton.AutoSize = true;
        _openGlobalSettingsButton.Text = "全局设定";
        _openGlobalSettingsButton.AutoSize = true;
        _exportRoleEditorCsvButton.Text = "导出CSV";
        _exportRoleEditorCsvButton.AutoSize = true;
        _exportRoleEditorCsvButton.Enabled = false;
        _importRoleEditorCsvButton.Text = "导入CSV";
        _importRoleEditorCsvButton.AutoSize = true;
        _importRoleEditorCsvButton.Enabled = false;
        _copyRoleEditorSelectionButton.Text = "复制";
        _copyRoleEditorSelectionButton.AutoSize = true;
        _pasteRoleEditorSelectionButton.Text = "粘贴";
        _pasteRoleEditorSelectionButton.AutoSize = true;
        _batchFillRoleEditorColumnButton.Text = "批量填列";
        _batchFillRoleEditorColumnButton.AutoSize = true;
        _roleEditorSearchBox.Width = 180;
        _roleEditorSearchBox.PlaceholderText = "姓名/编号/职业/形象";
        _filterRoleEditorButton.Text = "筛选";
        _filterRoleEditorButton.AutoSize = true;
        _clearRoleEditorFilterButton.Text = "清除";
        _clearRoleEditorFilterButton.AutoSize = true;
        toolbar.Controls.AddRange(new Control[]
        {
            _loadRoleEditorButton,
            _saveRoleEditorButton,
            _openRolePersonalEffectButton,
            _openRoleEffectButton,
            _openGlobalSettingsButton,
            _exportRoleEditorCsvButton,
            _importRoleEditorCsvButton,
            _copyRoleEditorSelectionButton,
            _pasteRoleEditorSelectionButton,
            _batchFillRoleEditorColumnButton,
            new Label { Text = "搜索：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _roleEditorSearchBox,
            _filterRoleEditorButton,
            _clearRoleEditorFilterButton
        });
        layout.Controls.Add(toolbar, 0, 0);

        _roleEditorGrid.Dock = DockStyle.Fill;
        _roleEditorGrid.AllowUserToAddRows = false;
        _roleEditorGrid.AllowUserToDeleteRows = false;
        _roleEditorGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _roleEditorGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _roleEditorGrid.MultiSelect = true;
        var roleSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildRoleEditorPage.GridTextDetail", roleSplit, desiredDistance: 760, desiredPanel1Min: 25, desiredPanel2Min: 25);
        AddCollapsibleSplitPanel(roleSplit, 1, "角色表", _roleEditorGrid, "BuildRoleEditorPage.GridTextDetail.Grid");
        AddCollapsibleSplitPanel(roleSplit, 2, "角色文本", BuildRoleTextDetailPanel(), "BuildRoleEditorPage.GridTextDetail.TextDetail");

        _roleEditorInfoBox.Dock = DockStyle.Fill;
        _roleEditorInfoBox.Multiline = true;
        _roleEditorInfoBox.ReadOnly = true;
        _roleEditorInfoBox.ScrollBars = ScrollBars.Vertical;
        _roleEditorInfoBox.WordWrap = true;
        _roleEditorInfoBox.Text = "角色设定：读取后可编辑人物基础字段、头像、职业、等级、能力和 R/S 形象编号；保存前自动备份，保存后复读校验。";
        layout.Controls.Add(roleSplit, 0, 1);
        return page;
    }

    private Control BuildRoleTextDetailPanel()
    {
        var detail = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 6,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 22));

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        top.Controls.Add(new Label
        {
            Text = "角色文本",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            Padding = new Padding(0, 7, 12, 0)
        });
        _saveRoleTextDetailButton.Text = "保存列传/台词";
        _saveRoleTextDetailButton.AutoSize = true;
        _saveRoleTextDetailButton.Enabled = false;
        top.Controls.Add(_saveRoleTextDetailButton);
        detail.Controls.Add(top, 0, 0);

        ConfigureRoleTextBox(_roleBiographyBox);
        var criticalQuotePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = _roleCriticalQuoteBoxes.Length,
            ColumnCount = 2
        };
        criticalQuotePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        criticalQuotePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < _roleCriticalQuoteBoxes.Length; i++)
        {
            criticalQuotePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / _roleCriticalQuoteBoxes.Length));
            _roleCriticalQuoteLabels[i].Dock = DockStyle.Fill;
            _roleCriticalQuoteLabels[i].TextAlign = ContentAlignment.MiddleLeft;
            ConfigureRoleTextBox(_roleCriticalQuoteBoxes[i]);
            criticalQuotePanel.Controls.Add(_roleCriticalQuoteLabels[i], 0, i);
            criticalQuotePanel.Controls.Add(_roleCriticalQuoteBoxes[i], 1, i);
        }
        ConfigureRoleTextBox(_roleRetreatQuoteBox);
        ConfigureRoleTextBox(_roleTextDetailInfoBox, readOnly: true);

        detail.Controls.Add(_roleBiographyBox, 0, 1);
        detail.Controls.Add(MakeHeader("暴击台词"), 0, 2);
        detail.Controls.Add(criticalQuotePanel, 0, 3);
        detail.Controls.Add(MakeHeader("撤退台词"), 0, 4);
        detail.Controls.Add(_roleRetreatQuoteBox, 0, 5);
        return detail;
    }

    private static void ConfigureRoleTextBox(TextBox box, bool readOnly = false)
    {
        box.Dock = DockStyle.Fill;
        box.Multiline = true;
        box.ScrollBars = ScrollBars.Vertical;
        box.WordWrap = true;
        box.ReadOnly = readOnly;
    }

    private TabPage BuildItemEditorPage()
    {
        var page = new TabPage("宝物设定");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadItemEditorButton.Text = "读取宝物/物品";
        _loadItemEditorButton.AutoSize = true;
        _saveItemEditorButton.Text = "保存宝物/物品";
        _saveItemEditorButton.AutoSize = true;
        _saveItemEditorButton.Enabled = false;
        _openItemEffectCatalogButton.Text = "宝物特效";
        _openItemEffectCatalogButton.AutoSize = true;
        _exportItemEditorCsvButton.Text = "导出CSV";
        _exportItemEditorCsvButton.AutoSize = true;
        _exportItemEditorCsvButton.Enabled = false;
        _importItemEditorCsvButton.Text = "导入CSV";
        _importItemEditorCsvButton.AutoSize = true;
        _importItemEditorCsvButton.Enabled = false;
        _copyItemEditorSelectionButton.Text = "复制";
        _copyItemEditorSelectionButton.AutoSize = true;
        _pasteItemEditorSelectionButton.Text = "粘贴";
        _pasteItemEditorSelectionButton.AutoSize = true;
        _batchFillItemEditorColumnButton.Text = "批量填列";
        _batchFillItemEditorColumnButton.AutoSize = true;
        _undoItemEditorButton.Text = "撤回";
        _undoItemEditorButton.AutoSize = true;
        _undoItemEditorButton.Enabled = false;
        _redoItemEditorButton.Text = "前进";
        _redoItemEditorButton.AutoSize = true;
        _redoItemEditorButton.Enabled = false;
        _itemEditorSearchBox.Width = 210;
        _itemEditorSearchBox.PlaceholderText = "名称/ID/类型/特效/说明";
        _filterItemEditorButton.Text = "筛选";
        _filterItemEditorButton.AutoSize = true;
        _clearItemEditorFilterButton.Text = "清除";
        _clearItemEditorFilterButton.AutoSize = true;
        toolbar.Controls.AddRange(new Control[]
        {
            _loadItemEditorButton,
            _saveItemEditorButton,
            _openItemEffectCatalogButton,
            _exportItemEditorCsvButton,
            _importItemEditorCsvButton,
            _copyItemEditorSelectionButton,
            _pasteItemEditorSelectionButton,
            _batchFillItemEditorColumnButton,
            _undoItemEditorButton,
            _redoItemEditorButton,
            new Label { Text = "搜索：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _itemEditorSearchBox,
            _filterItemEditorButton,
            _clearItemEditorFilterButton
        });
        layout.Controls.Add(toolbar, 0, 0);

        _itemEditorGrid.Dock = DockStyle.Fill;
        _itemEditorGrid.AllowUserToAddRows = false;
        _itemEditorGrid.AllowUserToDeleteRows = false;
        _itemEditorGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _itemEditorGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _itemEditorGrid.MultiSelect = true;

        var body = CreateResizableSplit("BuildItemEditorPage.GridPreview", Orientation.Vertical, 760);
        AddCollapsibleSplitPanel(body, 1, "宝物表", _itemEditorGrid, "BuildItemEditorPage.GridPreview.Grid");

        var previewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        previewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewPanel.Controls.Add(new Label
        {
            Text = "图标预览",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        }, 0, 0);
        _itemIconPreviewBox.Dock = DockStyle.Fill;
        _itemIconPreviewBox.BorderStyle = BorderStyle.FixedSingle;
        _itemIconPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _itemIconPreviewBox.BackColor = Color.White;
        previewPanel.Controls.Add(_itemIconPreviewBox, 0, 1);
        _itemIconPreviewInfoBox.Dock = DockStyle.Fill;
        _itemIconPreviewInfoBox.Multiline = true;
        _itemIconPreviewInfoBox.ReadOnly = true;
        _itemIconPreviewInfoBox.ScrollBars = ScrollBars.Vertical;
        _itemIconPreviewInfoBox.WordWrap = true;
        _itemIconPreviewInfoBox.Text = "读取宝物/物品后，选择某行会按“图标”字段从 Itemicon.dll 显示候选图标。";
        _itemEditorInfoBox.Dock = DockStyle.Fill;
        _itemEditorInfoBox.Multiline = true;
        _itemEditorInfoBox.ReadOnly = true;
        _itemEditorInfoBox.ScrollBars = ScrollBars.Vertical;
        _itemEditorInfoBox.WordWrap = true;
        _itemEditorInfoBox.Text = "宝物设定：按旧版格式显示 ID、图标、名称、类别、能力、价格、特效和介绍；特效名随特效号动态映射，可通过“宝物特效”维护项目侧 UTF-8 特效目录。";
        AddCollapsibleSplitPanel(body, 2, "图标预览", previewPanel, "BuildItemEditorPage.GridPreview.Preview");
        layout.Controls.Add(body, 0, 1);
        return page;
    }

    private TabPage BuildJobEditorPage()
    {
        var page = new TabPage("兵种设定");
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        _jobEditorTabs = tabs;
        page.Controls.Add(tabs);

        var detailPage = new TabPage("详细兵种");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detailPage.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadJobEditorButton.Text = "读取兵种";
        _loadJobEditorButton.AutoSize = true;
        _saveJobEditorButton.Text = "保存兵种";
        _saveJobEditorButton.AutoSize = true;
        _saveJobEditorButton.Enabled = false;
        _openJobSeriesTableButton.Text = "通用兵种系表";
        _openJobSeriesTableButton.AutoSize = true;
        _openJobEffectTableButton.Text = "兵种特效页";
        _openJobEffectTableButton.AutoSize = true;
        _exportJobEditorCsvButton.Text = "导出CSV";
        _exportJobEditorCsvButton.AutoSize = true;
        _exportJobEditorCsvButton.Enabled = false;
        _importJobEditorCsvButton.Text = "导入CSV";
        _importJobEditorCsvButton.AutoSize = true;
        _importJobEditorCsvButton.Enabled = false;
        _copyJobEditorSelectionButton.Text = "复制";
        _copyJobEditorSelectionButton.AutoSize = true;
        _pasteJobEditorSelectionButton.Text = "粘贴";
        _pasteJobEditorSelectionButton.AutoSize = true;
        _batchFillJobEditorColumnButton.Text = "批量填列";
        _batchFillJobEditorColumnButton.AutoSize = true;
        _undoJobEditorButton.Text = "后退";
        _undoJobEditorButton.AutoSize = true;
        _undoJobEditorButton.Enabled = false;
        _redoJobEditorButton.Text = "前进";
        _redoJobEditorButton.AutoSize = true;
        _redoJobEditorButton.Enabled = false;
        _jobEditorSearchBox.Width = 180;
        _jobEditorSearchBox.PlaceholderText = "兵种名/说明/数值";
        _filterJobEditorButton.Text = "筛选";
        _filterJobEditorButton.AutoSize = true;
        _clearJobEditorFilterButton.Text = "清除";
        _clearJobEditorFilterButton.AutoSize = true;
        toolbar.Controls.AddRange(new Control[]
        {
            _loadJobEditorButton,
            _saveJobEditorButton,
            _openJobEffectTableButton,
            _exportJobEditorCsvButton,
            _importJobEditorCsvButton,
            _copyJobEditorSelectionButton,
            _pasteJobEditorSelectionButton,
            _batchFillJobEditorColumnButton,
            _undoJobEditorButton,
            _redoJobEditorButton,
            new Label { Text = "搜索：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _jobEditorSearchBox,
            _filterJobEditorButton,
            _clearJobEditorFilterButton
        });
        layout.Controls.Add(toolbar, 0, 0);

        var detailBody = CreateResizableSplit("BuildJobEditorPage.DetailGridPreview", Orientation.Vertical, 760);

        _jobEditorGrid.Dock = DockStyle.Fill;
        _jobEditorGrid.AllowUserToAddRows = false;
        _jobEditorGrid.AllowUserToDeleteRows = false;
        _jobEditorGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _jobEditorGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _jobEditorGrid.MultiSelect = true;
        AddCollapsibleSplitPanel(detailBody, 1, "兵种表", _jobEditorGrid, "BuildJobEditorPage.DetailGridPreview.Grid");

        var previewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(8, 0, 0, 0)
        };
        previewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewPanel.Controls.Add(new Label { Text = "范围预览", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        _jobAreaPreviewBox.Dock = DockStyle.Fill;
        _jobAreaPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _jobAreaPreviewBox.BackColor = Color.White;
        previewPanel.Controls.Add(_jobAreaPreviewBox, 0, 1);
        _jobAreaPreviewInfoBox.Dock = DockStyle.Fill;
        _jobAreaPreviewInfoBox.Multiline = true;
        _jobAreaPreviewInfoBox.ReadOnly = true;
        _jobAreaPreviewInfoBox.ScrollBars = ScrollBars.Vertical;
        _jobAreaPreviewInfoBox.WordWrap = true;
        _jobAreaPreviewInfoBox.Text = "读取兵种后，单击兵种行显示可装备类别；选择“攻击范围”或“穿透”单元格会显示 Hitarea.e5 / Effarea.e5 中的范围图。双击兵种行可编辑可装备类别。";
        AddCollapsibleSplitPanel(detailBody, 2, "范围预览", previewPanel, "BuildJobEditorPage.DetailGridPreview.Preview");

        _jobEditorInfoBox.Dock = DockStyle.Fill;
        _jobEditorInfoBox.Multiline = true;
        _jobEditorInfoBox.ReadOnly = true;
        _jobEditorInfoBox.ScrollBars = ScrollBars.Vertical;
        _jobEditorInfoBox.WordWrap = true;
        _jobEditorInfoBox.Text = "兵种设定：读取后可编辑详细兵种名称、说明、成长参数、可装备类别和穿透；保存前自动备份，保存后复读校验。";
        layout.Controls.Add(detailBody, 0, 1);
        tabs.TabPages.Add(detailPage);

        var terrainPage = new TabPage("兵种系/地形");
        var terrainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        terrainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        terrainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        terrainPage.Controls.Add(terrainLayout);

        var terrainToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadJobTerrainButton.Text = "读取兵种系/地形";
        _loadJobTerrainButton.AutoSize = true;
        _saveJobTerrainButton.Text = "保存兵种系/地形";
        _saveJobTerrainButton.AutoSize = true;
        _saveJobTerrainButton.Enabled = false;
        _openJobRestraintTableButton.Text = "通用兵种相克表";
        _openJobRestraintTableButton.AutoSize = true;
        _jobTerrainSearchBox.Width = 200;
        _jobTerrainSearchBox.PlaceholderText = "兵种系/地形/数值";
        _filterJobTerrainButton.Text = "筛选";
        _filterJobTerrainButton.AutoSize = true;
        _clearJobTerrainFilterButton.Text = "清除";
        _clearJobTerrainFilterButton.AutoSize = true;
        terrainToolbar.Controls.AddRange(new Control[]
        {
            _loadJobTerrainButton,
            _saveJobTerrainButton,
            new Label { Text = "搜索：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _jobTerrainSearchBox,
            _filterJobTerrainButton,
            _clearJobTerrainFilterButton
        });
        terrainLayout.Controls.Add(terrainToolbar, 0, 0);

        _jobTerrainGrid.Dock = DockStyle.Fill;
        _jobTerrainGrid.AllowUserToAddRows = false;
        _jobTerrainGrid.AllowUserToDeleteRows = false;
        _jobTerrainGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _jobTerrainGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;

        _jobTerrainInfoBox.Dock = DockStyle.Fill;
        _jobTerrainInfoBox.Multiline = true;
        _jobTerrainInfoBox.ReadOnly = true;
        _jobTerrainInfoBox.ScrollBars = ScrollBars.Vertical;
        _jobTerrainInfoBox.WordWrap = true;
        _jobTerrainInfoBox.Text = "兵种系/地形：读取后可编辑兵种系名称、各地形发挥和移动消耗；保存前自动备份，保存后复读校验。";
        terrainLayout.Controls.Add(_jobTerrainGrid, 0, 1);
        tabs.TabPages.Add(terrainPage);

        var matrixPage = new TabPage("相克矩阵");
        var matrixLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        matrixLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        matrixLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        matrixPage.Controls.Add(matrixLayout);

        var matrixToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadJobMatrixButton.Text = "读取矩阵";
        _loadJobMatrixButton.AutoSize = true;
        _saveJobMatrixButton.Text = "保存矩阵";
        _saveJobMatrixButton.AutoSize = true;
        _saveJobMatrixButton.Enabled = false;
        _openJobMatrixRestraintTableButton.Text = "通用兵种相克表";
        _openJobMatrixRestraintTableButton.AutoSize = true;
        matrixToolbar.Controls.AddRange(new Control[]
        {
            _loadJobMatrixButton,
            _saveJobMatrixButton
        });
        matrixLayout.Controls.Add(matrixToolbar, 0, 0);

        var matrixTabs = new TabControl { Dock = DockStyle.Fill };
        var restraintPage = new TabPage("兵种相克");
        _jobRestraintGrid.Dock = DockStyle.Fill;
        _jobRestraintGrid.AllowUserToAddRows = false;
        _jobRestraintGrid.AllowUserToDeleteRows = false;
        _jobRestraintGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _jobRestraintGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        restraintPage.Controls.Add(_jobRestraintGrid);
        matrixTabs.TabPages.Add(restraintPage);

        _jobMatrixInfoBox.Dock = DockStyle.Fill;
        _jobMatrixInfoBox.Multiline = true;
        _jobMatrixInfoBox.ReadOnly = true;
        _jobMatrixInfoBox.ScrollBars = ScrollBars.Vertical;
        _jobMatrixInfoBox.WordWrap = true;
        _jobMatrixInfoBox.Text = "相克矩阵：读取后可编辑 40x40 兵种相克矩阵；保存前自动备份，保存后复读校验。";
        matrixLayout.Controls.Add(matrixTabs, 0, 1);
        tabs.TabPages.Add(matrixPage);

        var strategyPage = new TabPage("兵种策略");
        var strategyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        strategyLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        strategyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        strategyPage.Controls.Add(strategyLayout);

        var strategyToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadJobStrategyEditorButton.Text = "读取兵种策略";
        _loadJobStrategyEditorButton.AutoSize = true;
        _saveJobStrategyEditorButton.Text = "保存兵种策略";
        _saveJobStrategyEditorButton.AutoSize = true;
        _saveJobStrategyEditorButton.Enabled = false;
        _importJobStrategyIconButton.Text = "导入策略图标";
        _importJobStrategyIconButton.AutoSize = true;
        _importJobStrategyIconButton.Enabled = false;
        _openJobStrategyTableButton.Text = "通用策略表";
        _openJobStrategyTableButton.AutoSize = true;
        _jobStrategyEditorSearchBox.Width = 220;
        _jobStrategyEditorSearchBox.PlaceholderText = "策略名/兵种/等级/属性";
        _filterJobStrategyEditorButton.Text = "筛选";
        _filterJobStrategyEditorButton.AutoSize = true;
        _clearJobStrategyEditorFilterButton.Text = "清除";
        _clearJobStrategyEditorFilterButton.AutoSize = true;
        strategyToolbar.Controls.AddRange(new Control[]
        {
            _loadJobStrategyEditorButton,
            _saveJobStrategyEditorButton,
            _importJobStrategyIconButton,
            new Label { Text = "搜索：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _jobStrategyEditorSearchBox,
            _filterJobStrategyEditorButton,
            _clearJobStrategyEditorFilterButton
        });
        strategyLayout.Controls.Add(strategyToolbar, 0, 0);

        _jobStrategyEditorGrid.Dock = DockStyle.Fill;
        _jobStrategyEditorGrid.AllowUserToAddRows = false;
        _jobStrategyEditorGrid.AllowUserToDeleteRows = false;
        _jobStrategyEditorGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _jobStrategyEditorGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;

        var strategyBody = CreateResizableSplit("BuildJobEditorPage.StrategyGridPreview", Orientation.Vertical, 760);
        AddCollapsibleSplitPanel(strategyBody, 1, "策略表", _jobStrategyEditorGrid, "BuildJobEditorPage.StrategyGridPreview.Grid");

        var strategyPreviewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(8, 0, 0, 0)
        };
        strategyPreviewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        strategyPreviewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        strategyPreviewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        strategyPreviewPanel.Controls.Add(new Label { Text = "策略预览", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        _jobStrategyPreviewBox.Dock = DockStyle.Fill;
        _jobStrategyPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _jobStrategyPreviewBox.BackColor = Color.White;
        _jobStrategyPreviewBox.BorderStyle = BorderStyle.FixedSingle;
        strategyPreviewPanel.Controls.Add(_jobStrategyPreviewBox, 0, 1);
        _jobStrategyPreviewInfoBox.Dock = DockStyle.Fill;
        _jobStrategyPreviewInfoBox.Multiline = true;
        _jobStrategyPreviewInfoBox.ReadOnly = true;
        _jobStrategyPreviewInfoBox.ScrollBars = ScrollBars.Vertical;
        _jobStrategyPreviewInfoBox.WordWrap = true;
        _jobStrategyPreviewInfoBox.Text = "读取兵种策略后，选择“施法范围”“穿透范围”“策略图标”“小动画”“大动画”会显示对应资源预览；其它字段显示兵种学习情况。";
        strategyPreviewPanel.Controls.Add(_jobStrategyPreviewInfoBox, 0, 2);
        SetJobStrategyPreviewImageVisible(false);
        AddCollapsibleSplitPanel(strategyBody, 2, "策略预览", strategyPreviewPanel, "BuildJobEditorPage.StrategyGridPreview.Preview");

        _jobStrategyEditorInfoBox.Dock = DockStyle.Fill;
        _jobStrategyEditorInfoBox.Multiline = true;
        _jobStrategyEditorInfoBox.ReadOnly = true;
        _jobStrategyEditorInfoBox.ScrollBars = ScrollBars.Vertical;
        _jobStrategyEditorInfoBox.WordWrap = true;
        _jobStrategyEditorInfoBox.Text = "兵种策略：读取后可编辑策略基础属性、EKD5 策略附表属性，以及各详细兵种的学会等级；学会等级为 0 表示该兵种不能学习。";
        strategyLayout.Controls.Add(strategyBody, 0, 1);
        tabs.TabPages.Add(strategyPage);

        var effectPage = new TabPage("兵种特效");
        var effectLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        effectLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        effectLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        effectPage.Controls.Add(effectLayout);

        var effectToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadJobEffectEditorButton.Text = "读取兵种特效";
        _loadJobEffectEditorButton.AutoSize = true;
        _saveJobEffectEditorButton.Text = "保存兵种特效";
        _saveJobEffectEditorButton.AutoSize = true;
        _saveJobEffectEditorButton.Enabled = false;
        _openJobExclusiveEffectTableButton.Text = "通用专属/套装表";
        _openJobExclusiveEffectTableButton.AutoSize = true;
        _jobEffectEditorSearchBox.Width = 220;
        _jobEffectEditorSearchBox.PlaceholderText = "特效名/说明/武将/兵种";
        _filterJobEffectEditorButton.Text = "筛选";
        _filterJobEffectEditorButton.AutoSize = true;
        _clearJobEffectEditorFilterButton.Text = "清除";
        _clearJobEffectEditorFilterButton.AutoSize = true;
        effectToolbar.Controls.AddRange(new Control[]
        {
            _loadJobEffectEditorButton,
            _saveJobEffectEditorButton,
            new Label { Text = "搜索：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _jobEffectEditorSearchBox,
            _filterJobEffectEditorButton,
            _clearJobEffectEditorFilterButton
        });
        effectLayout.Controls.Add(effectToolbar, 0, 0);

        _jobEffectEditorGrid.Dock = DockStyle.Fill;
        _jobEffectEditorGrid.AllowUserToAddRows = false;
        _jobEffectEditorGrid.AllowUserToDeleteRows = false;
        _jobEffectEditorGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _jobEffectEditorGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;

        _jobEffectEditorInfoBox.Dock = DockStyle.Fill;
        _jobEffectEditorInfoBox.Multiline = true;
        _jobEffectEditorInfoBox.ReadOnly = true;
        _jobEffectEditorInfoBox.ScrollBars = ScrollBars.Vertical;
        _jobEffectEditorInfoBox.WordWrap = true;
        _jobEffectEditorInfoBox.Text = "兵种特效：读取后可编辑特效说明、武将/兵种分配和特效值；特效名称来自 6.5-7 原始名称区，当前只读显示。";
        effectLayout.Controls.Add(_jobEffectEditorGrid, 0, 1);
        tabs.TabPages.Add(effectPage);
        return page;
    }

    private TabPage BuildBattlefieldEditorPage()
    {
        var page = new TabPage("战场编辑");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadBattlefieldButton.Text = "读取关卡";
        _loadBattlefieldButton.AutoSize = true;
        _battlefieldScenarioCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _battlefieldScenarioCombo.Width = 260;
        _saveBattlefieldTextsButton.Text = "保存标题/胜败条件";
        _saveBattlefieldTextsButton.AutoSize = true;
        _saveBattlefieldTextsButton.Enabled = false;
        _saveBattlefieldUnitReviewsButton.Text = "保存出场核对";
        _saveBattlefieldUnitReviewsButton.AutoSize = true;
        _saveBattlefieldUnitReviewsButton.Enabled = false;
        _writeBattlefieldDeploymentButton.Text = "写回出场到S剧本";
        _writeBattlefieldDeploymentButton.AutoSize = true;
        _writeBattlefieldDeploymentButton.Enabled = false;
        _jumpBattlefieldMapButton.Text = "跳到地图编辑";
        _jumpBattlefieldMapButton.AutoSize = true;
        _jumpBattlefieldMapButton.Enabled = false;
        _jumpBattlefieldScenarioButton.Text = "跳到剧本编辑";
        _jumpBattlefieldScenarioButton.AutoSize = true;
        _jumpBattlefieldScenarioButton.Enabled = false;
        toolbar.Controls.AddRange(new Control[]
        {
            _loadBattlefieldButton,
            new Label { Text = "关卡：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _battlefieldScenarioCombo,
            _saveBattlefieldTextsButton,
            _saveBattlefieldUnitReviewsButton,
            _writeBattlefieldDeploymentButton,
            _jumpBattlefieldMapButton,
            _jumpBattlefieldScenarioButton
        });
        layout.Controls.Add(toolbar, 0, 0);

        var mainSplit = CreateResizableSplit("BuildBattlefieldEditorPage.ScriptMap", Orientation.Vertical, 520);

        _battlefieldLeftTabs.Dock = DockStyle.Fill;
        _battlefieldLeftTabs.TabPages.Clear();
        var scriptPage = new TabPage("S剧本");
        var scriptLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(0, 0, 6, 0)
        };
        scriptLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        scriptLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var scriptToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _battlefieldScriptSearchBox.Width = 160;
        _battlefieldScriptSearchBox.PlaceholderText = "搜索S剧本";
        _battlefieldScriptSearchButton.Text = "搜索";
        _battlefieldScriptSearchButton.AutoSize = true;
        _battlefieldScriptClearSearchButton.Text = "清除";
        _battlefieldScriptClearSearchButton.AutoSize = true;
        _saveBattlefieldScriptTextButton.Text = "保存选中文本";
        _saveBattlefieldScriptTextButton.AutoSize = true;
        _saveBattlefieldScriptTextButton.Enabled = false;
        _saveBattlefieldScriptStructureButton.Text = "完整保存S剧本 Ctrl+S";
        _saveBattlefieldScriptStructureButton.AutoSize = true;
        _saveBattlefieldScriptStructureButton.Enabled = false;
        _showBattlefieldVariablesButton.Text = "变量 Ctrl+L";
        _showBattlefieldVariablesButton.AutoSize = true;
        _showBattlefieldVariablesButton.Enabled = false;
        scriptToolbar.Controls.AddRange(new Control[]
        {
            _battlefieldScriptSearchBox,
            _battlefieldScriptSearchButton,
            _battlefieldScriptClearSearchButton,
            _saveBattlefieldScriptTextButton,
            _saveBattlefieldScriptStructureButton,
            _showBattlefieldVariablesButton
        });
        scriptLayout.Controls.Add(scriptToolbar, 0, 0);

        _battlefieldScriptTree.Dock = DockStyle.Fill;
        _battlefieldScriptTree.HideSelection = false;
        _battlefieldScriptTree.ShowNodeToolTips = true;
        _battlefieldScriptTree.FullRowSelect = true;
        _battlefieldScriptTree.CheckBoxes = true;
        EnableDoubleBuffering(_battlefieldScriptTree);
        ConfigureLegacyStyleScriptTreeContextMenu(_battlefieldScriptTreeContextMenu, LegacyScriptEditorScope.Battlefield);
        _battlefieldScriptTree.ContextMenuStrip = _battlefieldScriptTreeContextMenu;

        scriptLayout.Controls.Add(_battlefieldScriptTree, 0, 1);
        scriptPage.Controls.Add(scriptLayout);

        var textPage = new TabPage("标题/胜败");
        var titlePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1
        };
        titlePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titlePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        titlePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titlePanel.Controls.Add(new Label { Text = "关卡标题/地点", AutoSize = true }, 0, 0);
        _battlefieldTitleBox.Dock = DockStyle.Fill;
        titlePanel.Controls.Add(_battlefieldTitleBox, 0, 1);
        _battlefieldTitleBytesLabel.Text = "标题容量：未读取";
        _battlefieldTitleBytesLabel.AutoSize = true;
        titlePanel.Controls.Add(_battlefieldTitleBytesLabel, 0, 2);

        var conditionsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1
        };
        conditionsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        conditionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        conditionsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        conditionsPanel.Controls.Add(new Label { Text = "胜败条件", AutoSize = true }, 0, 0);
        _battlefieldConditionsBox.Dock = DockStyle.Fill;
        _battlefieldConditionsBox.Multiline = true;
        _battlefieldConditionsBox.ScrollBars = ScrollBars.Vertical;
        _battlefieldConditionsBox.WordWrap = true;
        conditionsPanel.Controls.Add(_battlefieldConditionsBox, 0, 1);
        _battlefieldConditionsBytesLabel.Text = "胜败条件容量：未读取";
        _battlefieldConditionsBytesLabel.AutoSize = true;
        conditionsPanel.Controls.Add(_battlefieldConditionsBytesLabel, 0, 2);
        _battlefieldInfoBox.Dock = DockStyle.Fill;
        _battlefieldInfoBox.Multiline = true;
        _battlefieldInfoBox.ReadOnly = true;
        _battlefieldInfoBox.ScrollBars = ScrollBars.Vertical;
        _battlefieldInfoBox.WordWrap = true;
        _battlefieldInfoBox.Text = "战场编辑：读取关卡后可编辑标题/胜败条件文本，并联动显示地图底图、地形层和战场命令候选。未知 R/S eex 命令结构不强写。";
        var titleConditionSplit = CreateResizableSplit("BuildBattlefieldEditorPage.TitleCondition", Orientation.Horizontal, 96, 54, 80);
        AddCollapsibleSplitPanel(titleConditionSplit, 1, "关卡标题", titlePanel, "BuildBattlefieldEditorPage.TitleCondition.Title");
        AddCollapsibleSplitPanel(titleConditionSplit, 2, "胜败条件", conditionsPanel, "BuildBattlefieldEditorPage.TitleCondition.Conditions");
        textPage.Controls.Add(titleConditionSplit);
        _battlefieldLeftTabs.TabPages.Add(scriptPage);
        _battlefieldLeftTabs.TabPages.Add(textPage);
        AddCollapsibleSplitPanel(mainSplit, 1, "S剧本", _battlefieldLeftTabs, "BuildBattlefieldEditorPage.ScriptMap.Script");

        var unitPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(0, 0, 6, 0)
        };
        unitPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        unitPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _battlefieldUnitPaletteFilterBox.Dock = DockStyle.Fill;
        _battlefieldUnitPaletteFilterBox.PlaceholderText = "搜索序号/姓名";
        unitPanel.Controls.Add(_battlefieldUnitPaletteFilterBox, 0, 0);
        _battlefieldUnitListBox.Dock = DockStyle.Fill;
        _battlefieldUnitListBox.DisplayMember = nameof(BattlefieldUnitPaletteItem.DisplayText);
        _battlefieldUnitPreviewBox.Dock = DockStyle.Fill;
        _battlefieldUnitPreviewBox.BackColor = Color.FromArgb(32, 32, 36);
        _battlefieldUnitPreviewBox.BorderStyle = BorderStyle.FixedSingle;
        _battlefieldUnitPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _battlefieldUnitPreviewInfoBox.Dock = DockStyle.Fill;
        _battlefieldUnitPreviewInfoBox.Multiline = true;
        _battlefieldUnitPreviewInfoBox.ReadOnly = true;
        _battlefieldUnitPreviewInfoBox.ScrollBars = ScrollBars.Vertical;
        _battlefieldUnitPreviewInfoBox.WordWrap = true;
        _battlefieldUnitPreviewInfoBox.Text = "选择角色后显示 S 形象。";
        var unitListPreviewSplit = CreateResizableSplit("BuildBattlefieldEditorPage.UnitListPreview", Orientation.Horizontal, 330, 80, 80);
        AddCollapsibleSplitPanel(unitListPreviewSplit, 1, "角色列表", _battlefieldUnitListBox, "BuildBattlefieldEditorPage.UnitListPreview.List");
        AddCollapsibleSplitPanel(unitListPreviewSplit, 2, "角色预览", _battlefieldUnitPreviewBox, "BuildBattlefieldEditorPage.UnitListPreview.Preview");
        unitPanel.Controls.Add(unitListPreviewSplit, 0, 1);

        _battlefieldMapScrollPanel.Dock = DockStyle.Fill;
        _battlefieldMapScrollPanel.AutoScroll = true;
        _battlefieldMapScrollPanel.TabStop = true;
        _battlefieldMapScrollPanel.BackColor = Color.FromArgb(26, 28, 30);
        EnableDoubleBuffering(_battlefieldMapScrollPanel);
        _battlefieldMapPreviewBox.Location = new Point(0, 0);
        _battlefieldMapPreviewBox.BackColor = Color.Black;
        _battlefieldMapPreviewBox.SizeMode = PictureBoxSizeMode.StretchImage;
        EnableDoubleBuffering(_battlefieldMapPreviewBox);
        _battlefieldMapScrollPanel.Controls.Add(_battlefieldMapPreviewBox);
        var mapLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        mapLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mapLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mapLayout.Controls.Add(_battlefieldMapScrollPanel, 0, 0);
        var battlefieldMapHintPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 0)
        };
        _battlefieldMapZoomLabel.Text = "缩放 100%";
        _battlefieldMapZoomLabel.AutoSize = true;
        _battlefieldMapZoomLabel.Padding = new Padding(8, 4, 0, 0);
        _battlefieldMapZoomResetButton.Text = "1:1";
        _battlefieldMapZoomResetButton.AutoSize = true;
        _markBattlefieldCommand25Button.Text = "指定地点测试";
        _markBattlefieldCommand25Button.AutoSize = true;
        _battlefieldMapHintLabel.Text = "地图：拖动左侧角色到格子；可右键选中已摆放单位并调整。";
        _battlefieldMapHintLabel.AutoSize = true;
        _battlefieldMapHintLabel.Padding = new Padding(0, 4, 0, 0);
        battlefieldMapHintPanel.Controls.AddRange(new Control[] { _battlefieldMapZoomLabel, _battlefieldMapZoomResetButton, _markBattlefieldCommand25Button, _battlefieldMapHintLabel });
        mapLayout.Controls.Add(battlefieldMapHintPanel, 0, 1);

        var controlOptionsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            RowCount = 9,
            ColumnCount = 1,
            Padding = new Padding(6, 0, 0, 0)
        };
        for (var i = 0; i < controlOptionsPanel.RowCount; i++)
        {
            controlOptionsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        controlOptionsPanel.Controls.Add(new Label { Text = "控制台", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        var factionPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _battlefieldFactionAllyRadio.Text = "我军";
        _battlefieldFactionFriendRadio.Text = "友军";
        _battlefieldFactionEnemyRadio.Text = "敌军";
        _battlefieldFactionAllyRadio.AutoSize = true;
        _battlefieldFactionFriendRadio.AutoSize = true;
        _battlefieldFactionEnemyRadio.AutoSize = true;
        _battlefieldFactionAllyRadio.Checked = true;
        factionPanel.Controls.AddRange(new Control[] { _battlefieldFactionAllyRadio, _battlefieldFactionFriendRadio, _battlefieldFactionEnemyRadio });
        controlOptionsPanel.Controls.Add(factionPanel, 0, 1);
        _battlefieldHiddenCheckBox.Text = "隐藏出场";
        _battlefieldHiddenCheckBox.AutoSize = true;
        controlOptionsPanel.Controls.Add(_battlefieldHiddenCheckBox, 0, 2);
        _battlefieldLevelOffsetInput.Minimum = -99;
        _battlefieldLevelOffsetInput.Maximum = 99;
        _battlefieldLevelOffsetInput.Width = 72;
        var levelOffsetPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        levelOffsetPanel.Controls.AddRange(new Control[]
        {
            new Label { Text = "等级修正", AutoSize = true, Padding = new Padding(0, 5, 0, 0) },
            _battlefieldLevelOffsetInput
        });
        controlOptionsPanel.Controls.Add(levelOffsetPanel, 0, 3);
        _battlefieldLevelModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _battlefieldLevelModeCombo.Items.AddRange(new object[] { "初级", "中级", "高级" });
        _battlefieldLevelModeCombo.SelectedIndex = 0;
        controlOptionsPanel.Controls.Add(_battlefieldLevelModeCombo, 0, 4);
        _battlefieldAiModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _battlefieldAiModeCombo.Items.AddRange(new object[] { "被动", "主动", "坚守", "攻击", "到点", "跟随", "逃离" });
        _battlefieldAiModeCombo.SelectedIndex = 0;
        controlOptionsPanel.Controls.Add(_battlefieldAiModeCombo, 0, 5);
        _battlefieldDirectionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _battlefieldDirectionCombo.Items.AddRange(new object[] { "上", "右", "下", "左" });
        _battlefieldDirectionCombo.SelectedIndex = 2;
        controlOptionsPanel.Controls.Add(_battlefieldDirectionCombo, 0, 6);
        _battlefieldRemovePlacedUnitButton.Text = "移除选中";
        _battlefieldRemovePlacedUnitButton.AutoSize = true;
        _battlefieldClearPlacedUnitsButton.Text = "清空摆放";
        _battlefieldClearPlacedUnitsButton.AutoSize = true;
        controlOptionsPanel.Controls.Add(_battlefieldRemovePlacedUnitButton, 0, 7);
        controlOptionsPanel.Controls.Add(_battlefieldClearPlacedUnitsButton, 0, 8);

        var unitTabs = new TabControl { Dock = DockStyle.Fill };
        var unitPage = new TabPage("候选");
        var unitLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        unitLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        unitLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var unitToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _battlefieldUnitCategoryFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _battlefieldUnitCategoryFilterCombo.Width = 140;
        _battlefieldUnitFilterBox.Width = 120;
        _battlefieldUnitFilterBox.PlaceholderText = "候选筛选";
        _filterBattlefieldUnitsButton.Text = "筛选";
        _filterBattlefieldUnitsButton.AutoSize = true;
        _clearBattlefieldUnitFilterButton.Text = "全部";
        _clearBattlefieldUnitFilterButton.AutoSize = true;
        _markBattlefieldUnitReviewedButton.Text = "已核对";
        _markBattlefieldUnitReviewedButton.AutoSize = true;
        _markBattlefieldUnitNeedsChangeButton.Text = "需改";
        _markBattlefieldUnitNeedsChangeButton.AutoSize = true;
        _jumpBattlefieldUnitScriptButton.Text = "跳命令";
        _jumpBattlefieldUnitScriptButton.AutoSize = true;
        unitToolbar.Controls.AddRange(new Control[]
        {
            _battlefieldUnitCategoryFilterCombo,
            _battlefieldUnitFilterBox,
            _filterBattlefieldUnitsButton,
            _clearBattlefieldUnitFilterButton,
            _markBattlefieldUnitReviewedButton,
            _markBattlefieldUnitNeedsChangeButton,
            _jumpBattlefieldUnitScriptButton
        });
        unitLayout.Controls.Add(unitToolbar, 0, 0);
        _battlefieldUnitGrid.Dock = DockStyle.Fill;
        _battlefieldUnitGrid.ReadOnly = false;
        _battlefieldUnitGrid.AllowUserToAddRows = false;
        _battlefieldUnitGrid.AllowUserToDeleteRows = false;
        _battlefieldUnitGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _battlefieldUnitGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        unitLayout.Controls.Add(_battlefieldUnitGrid, 0, 1);
        unitPage.Controls.Add(unitLayout);

        var commandPage = new TabPage("命令");
        _battlefieldCommandGrid.Dock = DockStyle.Fill;
        _battlefieldCommandGrid.ReadOnly = true;
        _battlefieldCommandGrid.AllowUserToAddRows = false;
        _battlefieldCommandGrid.AllowUserToDeleteRows = false;
        _battlefieldCommandGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _battlefieldCommandGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        commandPage.Controls.Add(_battlefieldCommandGrid);
        unitTabs.TabPages.Add(unitPage);
        unitTabs.TabPages.Add(commandPage);
        var candidateOptionsSplit = CreateResizableSplit("BuildBattlefieldEditorPage.CandidateOptions", Orientation.Horizontal, 420, 220, 150);
        AddCollapsibleSplitPanel(candidateOptionsSplit, 1, "候选表", unitTabs, "BuildBattlefieldEditorPage.CandidateOptions.Candidates");
        AddCollapsibleSplitPanel(candidateOptionsSplit, 2, "控制台", controlOptionsPanel, "BuildBattlefieldEditorPage.CandidateOptions.Options");
        var mapControlSplit = CreateResizableSplit("BuildBattlefieldEditorPage.MapControl", Orientation.Vertical, 620, 320, 320);
        AddCollapsibleSplitPanel(mapControlSplit, 1, "战场地图", mapLayout, "BuildBattlefieldEditorPage.MapControl.Map");
        mapControlSplit.Panel2.Controls.Add(candidateOptionsSplit);
        var unitMapControlSplit = CreateResizableSplit("BuildBattlefieldEditorPage.UnitMapControl", Orientation.Vertical, 190, 120, 360);
        unitMapControlSplit.Panel1.Controls.Add(unitPanel);
        unitMapControlSplit.Panel2.Controls.Add(mapControlSplit);

        mainSplit.Panel2.Controls.Add(unitMapControlSplit);
        layout.Controls.Add(mainSplit, 0, 1);

        return page;
    }

    private TabPage BuildRSceneEditorPage()
    {
        var page = new TabPage("场景编辑");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadRSceneButton.Text = "读取R剧情";
        _loadRSceneButton.AutoSize = true;
        _rSceneScenarioCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _rSceneScenarioCombo.Width = 260;
        _saveRSceneDraftButton.Text = "保存场景草稿";
        _saveRSceneDraftButton.AutoSize = true;
        _saveRSceneDraftButton.Enabled = false;
        _saveRSceneScriptStructureButton.Text = "完整保存R剧本 Ctrl+S";
        _saveRSceneScriptStructureButton.AutoSize = true;
        _saveRSceneScriptStructureButton.Enabled = false;
        _showRSceneVariablesButton.Text = "变量 Ctrl+L";
        _showRSceneVariablesButton.AutoSize = true;
        _showRSceneVariablesButton.Enabled = false;
        _jumpRSceneScriptButton.Text = "跳到剧本编辑";
        _jumpRSceneScriptButton.AutoSize = true;
        _jumpRSceneScriptButton.Enabled = false;
        toolbar.Controls.AddRange(new Control[]
        {
            _loadRSceneButton,
            new Label { Text = "R剧情：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _rSceneScenarioCombo,
            _saveRSceneDraftButton,
            _saveRSceneScriptStructureButton,
            _showRSceneVariablesButton,
            _jumpRSceneScriptButton
        });
        layout.Controls.Add(toolbar, 0, 0);

        var mainSplit = CreateResizableSplit("BuildRSceneEditorPage.ScriptScene", Orientation.Vertical, 520, 300, 420);

        var leftTabs = new TabControl { Dock = DockStyle.Fill };
        var scriptPage = new TabPage("R剧本");
        _rSceneScriptTree.Dock = DockStyle.Fill;
        _rSceneScriptTree.HideSelection = false;
        _rSceneScriptTree.ShowNodeToolTips = true;
        _rSceneScriptTree.FullRowSelect = true;
        _rSceneScriptTree.CheckBoxes = true;
        EnableDoubleBuffering(_rSceneScriptTree);
        ConfigureLegacyStyleScriptTreeContextMenu(_rSceneScriptTreeContextMenu, LegacyScriptEditorScope.RScene);
        _rSceneScriptTree.ContextMenuStrip = _rSceneScriptTreeContextMenu;
        _rSceneScriptDetailBox.Dock = DockStyle.Fill;
        _rSceneScriptDetailBox.Multiline = true;
        _rSceneScriptDetailBox.ReadOnly = true;
        _rSceneScriptDetailBox.ScrollBars = ScrollBars.Vertical;
        _rSceneScriptDetailBox.WordWrap = true;
        _rSceneScriptDetailBox.Text = "读取 R 剧情后显示对应 R 剧本树。";
        _rSceneScriptDetailBox.Visible = false;
        scriptPage.Controls.Add(_rSceneScriptTree);

        var commandPage = new TabPage("R场景候选集");
        _rSceneCommandGrid.Dock = DockStyle.Fill;
        _rSceneCommandGrid.ReadOnly = true;
        _rSceneCommandGrid.AllowUserToAddRows = false;
        _rSceneCommandGrid.AllowUserToDeleteRows = false;
        _rSceneCommandGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _rSceneCommandGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        commandPage.Controls.Add(_rSceneCommandGrid);
        leftTabs.TabPages.Add(scriptPage);
        leftTabs.TabPages.Add(commandPage);
        AddCollapsibleSplitPanel(mainSplit, 1, "R剧本", leftTabs, "BuildRSceneEditorPage.ScriptScene.Script");

        var actorPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(0, 0, 6, 0)
        };
        actorPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actorPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rSceneActorFilterBox.Dock = DockStyle.Fill;
        _rSceneActorFilterBox.PlaceholderText = "搜索序号/姓名/R编号";
        actorPanel.Controls.Add(_rSceneActorFilterBox, 0, 0);
        _rSceneActorListBox.Dock = DockStyle.Fill;
        _rSceneActorListBox.DisplayMember = nameof(RSceneActorPaletteItem.DisplayText);
        _rSceneFrameListView.Dock = DockStyle.Fill;
        _rSceneFrameListView.View = View.LargeIcon;
        _rSceneFrameListView.MultiSelect = false;
        _rSceneFrameListView.HideSelection = false;
        _rSceneFrameListView.BorderStyle = BorderStyle.FixedSingle;
        _rSceneFrameListView.BackColor = Color.FromArgb(34, 36, 40);
        _rSceneFrameListView.ForeColor = Color.White;
        var actorSplit = CreateResizableSplit("BuildRSceneEditorPage.ActorListPreview", Orientation.Horizontal, 330, 120, 100);
        AddCollapsibleSplitPanel(actorSplit, 1, "角色列表", _rSceneActorListBox, "BuildRSceneEditorPage.ActorListPreview.List");
        AddCollapsibleSplitPanel(actorSplit, 2, "动作帧", _rSceneFrameListView, "BuildRSceneEditorPage.ActorListPreview.Frames");
        actorPanel.Controls.Add(actorSplit, 0, 1);

        _rSceneCanvasScrollPanel.Dock = DockStyle.Fill;
        _rSceneCanvasScrollPanel.AutoScroll = true;
        _rSceneCanvasScrollPanel.TabStop = true;
        _rSceneCanvasScrollPanel.BackColor = Color.FromArgb(26, 28, 30);
        _rSceneCanvasBox.Location = new Point(0, 0);
        _rSceneCanvasBox.BackColor = Color.Black;
        _rSceneCanvasBox.SizeMode = PictureBoxSizeMode.StretchImage;
        _rSceneCanvasScrollPanel.Controls.Add(_rSceneCanvasBox);

        var canvasLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        canvasLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        canvasLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var canvasToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _rSceneBackgroundCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _rSceneBackgroundCombo.Width = 220;
        _rSceneGridSizeInput.Minimum = RSceneTileWidth;
        _rSceneGridSizeInput.Maximum = RSceneTileWidth;
        _rSceneGridSizeInput.Value = 16;
        _rSceneGridSizeInput.Width = 64;
        _rSceneShowGridCheckBox.Text = "网格";
        _rSceneShowGridCheckBox.AutoSize = true;
        _rSceneShowGridCheckBox.Checked = true;
        _rSceneDialoguePreviewCheckBox.Text = "对白预览";
        _rSceneDialoguePreviewCheckBox.AutoSize = true;
        _rSceneDialoguePreviewCheckBox.Checked = true;
        _rSceneZoomLabel.Text = "缩放 100%";
        _rSceneZoomLabel.AutoSize = true;
        _rSceneZoomLabel.Padding = new Padding(8, 5, 0, 0);
        _rSceneZoomResetButton.Text = "1:1";
        _rSceneZoomResetButton.AutoSize = true;
        _rScenePreviewLockButton.Text = "锁定预览";
        _rScenePreviewLockButton.AutoSize = true;
        _rScenePreviewLockButton.Enabled = false;
        _rSceneCanvasHintLabel.Text = "背景：读取 R 剧情后选择 Mmap.e5 图号；拖动角色到背景格子。";
        _rSceneCanvasHintLabel.AutoSize = true;
        _rSceneCanvasHintLabel.Padding = new Padding(8, 5, 0, 0);
        canvasToolbar.Controls.AddRange(new Control[]
        {
            new Label { Text = "背景", AutoSize = true, Padding = new Padding(0, 5, 0, 0) },
            _rSceneBackgroundCombo,
            new Label { Text = "坐标格", AutoSize = true, Padding = new Padding(8, 5, 0, 0) },
            new Label { Text = "斜菱形 16x8", AutoSize = true, Padding = new Padding(0, 5, 0, 0) },
            _rSceneShowGridCheckBox,
            _rSceneDialoguePreviewCheckBox,
            _rSceneZoomLabel,
            _rSceneZoomResetButton,
            _rScenePreviewLockButton,
            _rSceneCanvasHintLabel
        });
        canvasLayout.Controls.Add(canvasToolbar, 0, 0);
        canvasLayout.Controls.Add(_rSceneCanvasScrollPanel, 0, 1);

        var rSceneParameterEditToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 4),
            Margin = Padding.Empty
        };
        _applyRSceneInlineDialogButton.Text = "应用修改";
        _applyRSceneInlineDialogButton.AutoSize = true;
        _applyRSceneInlineDialogButton.Enabled = false;
        _resetRSceneInlineDialogButton.Text = "重置";
        _resetRSceneInlineDialogButton.AutoSize = true;
        _resetRSceneInlineDialogButton.Enabled = false;
        rSceneParameterEditToolbar.Controls.AddRange(new Control[]
        {
            _applyRSceneInlineDialogButton,
            _resetRSceneInlineDialogButton
        });

        var rSceneParameterPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        rSceneParameterPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rSceneParameterPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rSceneParameterPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rSceneParameterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rSceneParameterPanel.Controls.Add(new Label
        {
            Text = "当前指令参数",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 4),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point)
        }, 0, 0);
        _rSceneInlineDialogHost.Margin = Padding.Empty;
        rSceneParameterPanel.Controls.Add(_rSceneInlineDialogHost, 0, 1);
        rSceneParameterPanel.Controls.Add(rSceneParameterEditToolbar, 0, 2);

        var canvasParameterSplit = CreateResizableSplit("BuildRSceneEditorPage.CanvasParameter", Orientation.Horizontal, 430, 220, 180);
        AddCollapsibleSplitPanel(canvasParameterSplit, 1, "场景画布", canvasLayout, "BuildRSceneEditorPage.CanvasParameter.Canvas");
        AddCollapsibleSplitPanel(canvasParameterSplit, 2, "指令参数", rSceneParameterPanel, "BuildRSceneEditorPage.CanvasParameter.Parameter");

        var controlPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            RowCount = 8,
            ColumnCount = 1,
            Padding = new Padding(6, 0, 0, 0)
        };
        for (var i = 0; i < controlPanel.RowCount; i++)
        {
            controlPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        controlPanel.Controls.Add(new Label { Text = "R场景控制", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        _rSceneFacingCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _rSceneFacingCombo.Items.AddRange(new object[] { "下", "上", "左", "右" });
        _rSceneFacingCombo.SelectedIndex = 0;
        controlPanel.Controls.Add(_rSceneFacingCombo, 0, 1);
        _rSceneStanceInput.Minimum = 0;
        _rSceneStanceInput.Maximum = RSceneFrameCount - 1;
        _rSceneStanceInput.Value = 0;
        _rSceneStanceInput.Width = 64;
        var stancePanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        stancePanel.Controls.AddRange(new Control[]
        {
            new Label { Text = "动作帧", AutoSize = true, Padding = new Padding(0, 5, 0, 0) },
            _rSceneStanceInput
        });
        controlPanel.Controls.Add(stancePanel, 0, 2);
        _rScenePlaybackButton.Text = "开始";
        _rScenePlaybackButton.AutoSize = true;
        _rScenePlaybackButton.Enabled = false;
        _rScenePlaybackDelayInput.Minimum = 50;
        _rScenePlaybackDelayInput.Maximum = 10000;
        _rScenePlaybackDelayInput.Increment = 50;
        _rScenePlaybackDelayInput.Value = 500;
        _rScenePlaybackDelayInput.Width = 86;
        var playbackPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        playbackPanel.Controls.AddRange(new Control[]
        {
            _rScenePlaybackButton,
            new Label { Text = "停顿(ms)", AutoSize = true, Padding = new Padding(8, 5, 0, 0) },
            _rScenePlaybackDelayInput
        });
        _rScenePlaybackStatusLabel.Text = "播放：未开始";
        _rScenePlaybackStatusLabel.AutoSize = true;
        _rScenePlaybackStatusLabel.Padding = new Padding(0, 5, 0, 0);
        controlPanel.Controls.Add(playbackPanel, 0, 5);
        controlPanel.Controls.Add(_rScenePlaybackStatusLabel, 0, 6);

        var canvasControlSplit = CreateResizableSplit("BuildRSceneEditorPage.CanvasControl", Orientation.Vertical, 680, 360, 220);
        canvasControlSplit.Panel1.Controls.Add(canvasParameterSplit);
        AddCollapsibleSplitPanel(canvasControlSplit, 2, "场景控制", controlPanel, "BuildRSceneEditorPage.CanvasControl.Control");
        var actorSceneSplit = CreateResizableSplit("BuildRSceneEditorPage.ActorScene", Orientation.Vertical, 190, 120, 360);
        actorSceneSplit.Panel1.Controls.Add(actorPanel);
        actorSceneSplit.Panel2.Controls.Add(canvasControlSplit);
        mainSplit.Panel2.Controls.Add(actorSceneSplit);
        layout.Controls.Add(mainSplit, 0, 1);

        return page;
    }

    private TabPage BuildScriptEditorPage()
    {
        var page = new TabPage("剧本编辑");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadScriptButton.Text = "读取剧本列表";
        _loadScriptButton.AutoSize = true;
        _scriptScenarioCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _scriptScenarioCombo.Width = 260;
        _scriptSearchBox.Width = 180;
        _scriptSearchBox.PlaceholderText = "搜索命令/文本";
        _scriptSearchButton.Text = "搜索";
        _scriptSearchButton.AutoSize = true;
        _scriptClearSearchButton.Text = "清除";
        _scriptClearSearchButton.AutoSize = true;
        _showScriptVariablesButton.Text = "变量 Ctrl+L";
        _showScriptVariablesButton.AutoSize = true;
        _showScriptVariablesButton.Enabled = false;
        _locateScriptCommandButton.Text = "定位";
        _locateScriptCommandButton.AutoSize = true;
        _locateScriptCommandButton.Enabled = false;
        _copyScriptCommandButton.Text = "复制";
        _copyScriptCommandButton.AutoSize = true;
        _copyScriptCommandButton.Enabled = false;
        _previewPasteScriptCommandButton.Text = "粘贴预览";
        _previewPasteScriptCommandButton.AutoSize = true;
        _previewPasteScriptCommandButton.Enabled = false;
        _scriptNewCommandCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _scriptNewCommandCombo.Width = 220;
        _scriptNewCommandCombo.Enabled = false;
        _appendScriptCommandToSectionButton.Text = "正文末尾";
        _appendScriptCommandToSectionButton.AutoSize = true;
        _appendScriptCommandToSectionButton.Enabled = false;
        _insertScriptCommandBeforeButton.Text = "前插";
        _insertScriptCommandBeforeButton.AutoSize = true;
        _insertScriptCommandBeforeButton.Enabled = false;
        _insertScriptCommandAfterButton.Text = "后插";
        _insertScriptCommandAfterButton.AutoSize = true;
        _insertScriptCommandAfterButton.Enabled = false;
        _appendScriptCommandToChildBlockButton.Text = "子块";
        _appendScriptCommandToChildBlockButton.AutoSize = true;
        _appendScriptCommandToChildBlockButton.Enabled = false;
        _deleteScriptCommandButton.Text = "删除";
        _deleteScriptCommandButton.AutoSize = true;
        _deleteScriptCommandButton.Enabled = false;
        _pasteScriptCommandBeforeButton.Text = "粘到前面";
        _pasteScriptCommandBeforeButton.AutoSize = true;
        _pasteScriptCommandBeforeButton.Enabled = false;
        _pasteScriptCommandAfterButton.Text = "粘到后面";
        _pasteScriptCommandAfterButton.AutoSize = true;
        _pasteScriptCommandAfterButton.Enabled = false;
        _moveScriptCommandUpButton.Text = "上移";
        _moveScriptCommandUpButton.AutoSize = true;
        _moveScriptCommandUpButton.Enabled = false;
        _moveScriptCommandDownButton.Text = "下移";
        _moveScriptCommandDownButton.AutoSize = true;
        _moveScriptCommandDownButton.Enabled = false;
        _saveScriptTextButton.Text = "保存文本";
        _saveScriptTextButton.AutoSize = true;
        _saveScriptTextButton.Enabled = false;
        _saveScriptStructureButton.Text = "完整保存剧本 Ctrl+S";
        _saveScriptStructureButton.AutoSize = true;
        _saveScriptStructureButton.Enabled = false;
        _jumpScriptBattlefieldButton.Text = "战场";
        _jumpScriptBattlefieldButton.AutoSize = true;
        _jumpScriptBattlefieldButton.Enabled = false;
        toolbar.Controls.AddRange(new Control[]
        {
            _loadScriptButton,
            new Label { Text = "剧本：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _scriptScenarioCombo,
            _scriptSearchBox,
            _scriptSearchButton,
            _scriptClearSearchButton,
            _showScriptVariablesButton,
            _locateScriptCommandButton,
            _copyScriptCommandButton,
            _previewPasteScriptCommandButton,
            _pasteScriptCommandBeforeButton,
            _pasteScriptCommandAfterButton,
            _saveScriptStructureButton,
            _jumpScriptBattlefieldButton
        });
        layout.Controls.Add(toolbar, 0, 0);

        _scriptHeaderLabel.Dock = DockStyle.Fill;
        _scriptHeaderLabel.AutoSize = true;
        _scriptHeaderLabel.ForeColor = Color.FromArgb(210, 210, 210);
        _scriptHeaderLabel.Padding = new Padding(2, 0, 0, 6);
        _scriptHeaderLabel.Text = "字典：未加载    剧本：未选择    双击指令使用旧版 Dialog 修改；右侧可内嵌修改参数";
        layout.Controls.Add(_scriptHeaderLabel, 0, 1);

        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildScriptEditorPage.TreeDetail", mainSplit, desiredDistance: 560, desiredPanel1Min: 360, desiredPanel2Min: 420);

        _scriptTree.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _scriptTree.Dock = DockStyle.Fill;
        _scriptTree.HideSelection = false;
        _scriptTree.FullRowSelect = true;
        _scriptTree.ShowLines = true;
        _scriptTree.ShowNodeToolTips = true;
        _scriptTree.CheckBoxes = true;
        _scriptTree.BorderStyle = BorderStyle.FixedSingle;
        EnableDoubleBuffering(_scriptTree);
        ConfigureLegacyStyleScriptTreeContextMenu();
        _scriptTree.ContextMenuStrip = _legacyScriptTreeContextMenu;
        _scriptTree.NodeMouseClick += (_, e) => HandleScriptTreeNodeMouseClick(e);
        _scriptTree.NodeMouseDoubleClick += (_, e) => HandleScriptTreeNodeMouseDoubleClick(e);
        _scriptTree.KeyDown += (_, e) => HandleScriptTreeKeyDown(e);
        _scriptTree.AfterCheck += (_, e) => HandleScriptTreeNodeAfterCheck(e);

        var treePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(0)
        };
        treePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        treePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        treePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        treePanel.Controls.Add(new Label
        {
            Text = "事件树",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 6),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point)
        }, 0, 0);
        var structureToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 6)
        };
        structureToolbar.Controls.AddRange(new Control[]
        {
            new Label { Text = "新增：", AutoSize = true, Padding = new Padding(0, 7, 0, 0) },
            _scriptNewCommandCombo,
            _appendScriptCommandToSectionButton,
            _insertScriptCommandBeforeButton,
            _insertScriptCommandAfterButton,
            _appendScriptCommandToChildBlockButton,
            _deleteScriptCommandButton
        });
        structureToolbar.Visible = false;
        treePanel.Controls.Add(structureToolbar, 0, 1);
        treePanel.Controls.Add(_scriptTree, 0, 2);
        AddCollapsibleSplitPanel(mainSplit, 1, "事件树", treePanel, "BuildScriptEditorPage.TreeDetail.Tree");

        ConfigureHiddenScriptGrids();
        var hiddenBindingsPanel = new Panel
        {
            Visible = false,
            Size = Size.Empty
        };
        hiddenBindingsPanel.Controls.AddRange(new Control[] { _scriptCommandGrid, _scriptTextGrid, _scriptSearchResultGrid });
        page.Controls.Add(hiddenBindingsPanel);

        _scriptImagePreviewBox.Dock = DockStyle.Fill;
        _scriptImagePreviewBox.BackColor = Color.Black;
        _scriptImagePreviewBox.BorderStyle = BorderStyle.FixedSingle;
        _scriptImagePreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _scriptImagePreviewBox.Margin = Padding.Empty;
        _scriptImagePreviewInfoBox.Dock = DockStyle.Fill;
        _scriptImagePreviewInfoBox.Multiline = true;
        _scriptImagePreviewInfoBox.ReadOnly = true;
        _scriptImagePreviewInfoBox.ScrollBars = ScrollBars.Vertical;
        _scriptImagePreviewInfoBox.WordWrap = true;
        _scriptImagePreviewInfoBox.BorderStyle = BorderStyle.FixedSingle;
        _scriptImagePreviewInfoBox.Text = "无图片预览";

        _scriptParameterGrid.Dock = DockStyle.Fill;
        _scriptParameterGrid.ReadOnly = true;
        _scriptParameterGrid.AllowUserToAddRows = false;
        _scriptParameterGrid.AllowUserToDeleteRows = false;
        _scriptParameterGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _scriptParameterGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _scriptParameterGrid.MultiSelect = false;
        _scriptParameterGrid.RowHeadersVisible = false;
        _scriptParameterGrid.BorderStyle = BorderStyle.FixedSingle;

        var parameterEditToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 4),
            Margin = Padding.Empty
        };
        _applyScriptInlineDialogButton.Text = "应用修改";
        _applyScriptInlineDialogButton.AutoSize = true;
        _applyScriptInlineDialogButton.Enabled = false;
        _resetScriptInlineDialogButton.Text = "重置";
        _resetScriptInlineDialogButton.AutoSize = true;
        _resetScriptInlineDialogButton.Enabled = false;
        parameterEditToolbar.Controls.AddRange(new Control[]
        {
            _applyScriptInlineDialogButton,
            _resetScriptInlineDialogButton
        });

        var parameterPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        parameterPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        parameterPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        parameterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _scriptInlineDialogHost.Margin = Padding.Empty;
        parameterPanel.Controls.Add(_scriptInlineDialogHost, 0, 0);
        parameterPanel.Controls.Add(parameterEditToolbar, 0, 1);
        var detailLayout = CreateResizableSplit("BuildScriptEditorPage.ImageParameter", Orientation.Horizontal, 430, 180, 220);
        AddCollapsibleSplitPanel(detailLayout, 1, "图片预览", _scriptImagePreviewBox, "BuildScriptEditorPage.ImageParameter.Image");
        AddCollapsibleSplitPanel(detailLayout, 2, "指令参数", parameterPanel, "BuildScriptEditorPage.ImageParameter.Parameter");

        mainSplit.Panel2.Controls.Add(detailLayout);
        layout.Controls.Add(mainSplit, 0, 2);

        return page;
    }
}
