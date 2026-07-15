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
        var mapToolbar = CreateToolbarStack(2);
        _loadMapImagesButton.Text = "读取 Map 图片";
        ConfigureToolbarButton(_loadMapImagesButton, 104);
        _mapMakerNewDraftButton.Text = "新建草稿";
        ConfigureToolbarButton(_mapMakerNewDraftButton, 88);
        _mapMakerLoadLastDraftButton.Text = "载入上次草稿";
        ConfigureToolbarButton(_mapMakerLoadLastDraftButton, 118);
        _mapMakerSaveDraftButton.Text = "保存草稿";
        ConfigureToolbarButton(_mapMakerSaveDraftButton, 88);
        _mapMakerSaveDraftButton.Enabled = false;
        _mapMakerExportPairButton.Text = "一键导出地图";
        ConfigureToolbarButton(_mapMakerExportPairButton, 118);
        _mapMakerExportPairButton.Enabled = false;
        _mapMakerImportPairButton.Text = "一键导入地图";
        ConfigureToolbarButton(_mapMakerImportPairButton, 118);
        _mapMakerImportPairButton.Enabled = false;
        _mapMakerGridWidthInput.Minimum = 1;
        _mapMakerGridWidthInput.Maximum = 256;
        _mapMakerGridWidthInput.Value = 30;
        _mapMakerGridWidthInput.Width = 58;
        _mapMakerGridHeightInput.Minimum = 1;
        _mapMakerGridHeightInput.Maximum = 256;
        _mapMakerGridHeightInput.Value = 30;
        _mapMakerGridHeightInput.Width = 58;
        _mapMakerGridWidthInput.Margin = new Padding(3);
        _mapMakerGridHeightInput.Margin = new Padding(3);
        _mapMakerResizeButton.Text = "调整尺寸";
        ConfigureToolbarButton(_mapMakerResizeButton, 88);
        _mapMakerResizeButton.Enabled = false;
        _mapMakerBrushModeCombo.Visible = false;
        _mapMakerSelectMaterialRootButton.Visible = false;
        _mapFitButton.Text = "适应窗口";
        ConfigureToolbarButton(_mapFitButton, 88);
        _mapActualButton.Text = "100%";
        ConfigureToolbarButton(_mapActualButton, 72);
        _mapZoomTrackBar.Minimum = 10;
        _mapZoomTrackBar.Maximum = 300;
        _mapZoomTrackBar.Value = 100;
        _mapZoomTrackBar.TickFrequency = 50;
        _mapZoomTrackBar.Width = 140;
        _mapZoomTrackBar.Margin = new Padding(3);
        _mapMakerShowTerrainCheckBox.Text = "查看地形层";
        ConfigureToolbarCheckBox(_mapMakerShowTerrainCheckBox);
        _mapMakerShowTerrainCheckBox.Checked = false;
        _mapMakerShowTerrainCheckBox.Visible = false;
        _mapMakerShowGridCheckBox.Text = "显示网格";
        ConfigureToolbarCheckBox(_mapMakerShowGridCheckBox);
        _mapMakerShowGridCheckBox.Checked = true;
        _mapMakerAutoGenerateCheckBox.Text = "地形驱动";
        ConfigureToolbarCheckBox(_mapMakerAutoGenerateCheckBox);
        _mapMakerAutoGenerateCheckBox.Checked = true;
        _mapMakerAutoGenerateCheckBox.Visible = false;
        _mapMakerBeautifyCheckBox.Text = "美化当前地图";
        ConfigureToolbarCheckBox(_mapMakerBeautifyCheckBox);
        _mapMakerBeautifyCheckBox.Enabled = false;
        _mapMakerBeautifyFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _mapMakerBeautifyFilterCombo.Width = 110;
        _mapMakerBeautifyFilterCombo.Margin = new Padding(3);
        _mapMakerBeautifyFilterCombo.Items.AddRange(new object[]
        {
            new BeautifyFilterComboItem(TerrainBeautifyFilterProfiles.Natural, "自然融合"),
            new BeautifyFilterComboItem(TerrainBeautifyFilterProfiles.Night, "黑夜"),
            new BeautifyFilterComboItem(TerrainBeautifyFilterProfiles.Autumn, "秋天"),
            new BeautifyFilterComboItem(TerrainBeautifyFilterProfiles.Winter, "冬天"),
            new BeautifyFilterComboItem(TerrainBeautifyFilterProfiles.WarmSun, "暖阳"),
            new BeautifyFilterComboItem(TerrainBeautifyFilterProfiles.Custom, "自定义")
        });
        _mapMakerBeautifyFilterCombo.SelectedIndex = 0;
        _mapMakerRollbackBeautifyButton.Text = "回退美化";
        ConfigureToolbarButton(_mapMakerRollbackBeautifyButton, 88);
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
        _mapMakerTerrainToolCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _mapMakerTerrainToolCombo.Width = 112;
        _mapMakerTerrainToolCombo.Items.AddRange(new object[] { "铅笔", "恢复", "连通填充", "矩形", "线段", "吸管" });
        _mapMakerTerrainToolCombo.SelectedIndex = 0;
        _mapMakerTerrainBrushSizeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _mapMakerTerrainBrushSizeCombo.Width = 72;
        _mapMakerTerrainBrushSizeCombo.Items.AddRange(new object[] { "1 格", "3 格", "5 格" });
        _mapMakerTerrainBrushSizeCombo.SelectedIndex = 0;
        _mapMakerTerrainBrushInput.Minimum = 0;
        _mapMakerTerrainBrushInput.Maximum = 255;
        _mapMakerTerrainBrushInput.Width = 62;
        _mapMakerTerrainBrushInput.Visible = false;
        _mapMakerBrushNameLabel.Text = "地形：0x00";
        _mapMakerBrushNameLabel.AutoSize = true;
        _mapMakerBrushNameLabel.Padding = new Padding(0, 7, 0, 0);
        _mapMakerSaveTerrainButton.Text = "保存草稿";
        ConfigureToolbarButton(_mapMakerSaveTerrainButton, 88);
        _mapMakerSaveTerrainButton.Enabled = false;
        _mapMakerSaveTerrainButton.Visible = false;
        _mapMakerUndoTerrainButton.Text = "撤销";
        ConfigureToolbarButton(_mapMakerUndoTerrainButton, 72);
        _mapMakerUndoTerrainButton.Enabled = false;
        _mapMakerRedoTerrainButton.Text = "重做";
        ConfigureToolbarButton(_mapMakerRedoTerrainButton, 72);
        _mapMakerRedoTerrainButton.Enabled = false;
        _mapMakerReplaceMapImageButton.Text = "旧版替换底图";
        ConfigureToolbarButton(_mapMakerReplaceMapImageButton, 118);
        _mapMakerReplaceMapImageButton.Enabled = false;
        _mapMakerReplaceMapImageButton.Visible = false;
        _mapMakerExportPreviewButton.Text = "导出PNG";
        ConfigureToolbarButton(_mapMakerExportPreviewButton, 88);
        _mapMakerExportPreviewButton.Enabled = false;
        _mapMakerExportPreviewButton.Visible = false;
        _mapMakerExportJpgButton.Text = "导出美化JPG";
        ConfigureToolbarButton(_mapMakerExportJpgButton, 118);
        _mapMakerExportJpgButton.Enabled = false;
        _mapMakerExportJpgButton.Visible = false;
        _mapMakerExtractMaterialButton.Text = "提取素材";
        ConfigureToolbarButton(_mapMakerExtractMaterialButton, 88);
        _mapMakerExtractMaterialButton.Enabled = false;
        _mapMakerMaterialPlanButton.Text = "主素材设置";
        ConfigureToolbarButton(_mapMakerMaterialPlanButton, 104);
        _mapMakerMaterialPlanButton.Enabled = false;
        _mapMakerMaterialPlanButton.Visible = false;
        _mapMakerTerrainStyleButton.Text = "\u4e00\u952e\u751f\u6210";
        ConfigureToolbarButton(_mapMakerTerrainStyleButton, 104);
        _mapMakerTerrainStyleButton.Enabled = false;
        _mapMakerTerrainStyleButton.Visible = false;
        _mapMakerPublishMapButton.Text = "高级：仅底图";
        ConfigureToolbarButton(_mapMakerPublishMapButton, 118);
        _mapMakerPublishMapButton.Enabled = false;
        _mapMakerPublishTerrainButton.Text = "高级：仅地形";
        ConfigureToolbarButton(_mapMakerPublishTerrainButton, 118);
        _mapMakerPublishTerrainButton.Enabled = false;
        _mapMakerPublishAllButton.Text = "发布地图";
        ConfigureToolbarButton(_mapMakerPublishAllButton, 88);
        _mapMakerPublishAllButton.Enabled = false;
        AddToolbarRow(mapToolbar, 0,
            _loadMapImagesButton,
            _mapMakerNewDraftButton,
            _mapMakerLoadLastDraftButton,
            CreateToolbarLabel("尺寸："),
            _mapMakerGridWidthInput,
            CreateToolbarLabel("x", 0),
            _mapMakerGridHeightInput,
            _mapMakerResizeButton,
            _mapMakerSaveDraftButton,
            _mapMakerExportPairButton,
            _mapMakerImportPairButton,
            _mapFitButton,
            _mapActualButton,
            CreateToolbarLabel("缩放："),
            _mapZoomTrackBar,
            _mapMakerShowGridCheckBox);
        AddToolbarRow(mapToolbar, 1,
            _mapMakerBeautifyFilterCombo,
            _mapMakerBeautifyCheckBox,
            _mapMakerRollbackBeautifyButton,
            _mapMakerBrushNameLabel,
            _mapMakerUndoTerrainButton,
            _mapMakerRedoTerrainButton,
            _mapMakerTerrainStyleButton,
            _mapMakerPublishAllButton);
        foreach (Control row in mapToolbar.Controls)
        {
            row.Margin = Padding.Empty;
            row.Padding = Padding.Empty;
        }
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
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(6, 0, 0, 0)
        };
        materialBrowserPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        materialBrowserPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        materialBrowserPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        materialBrowserPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        materialBrowserPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        materialBrowserPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        _mapMakerExtractMaterialButton.Dock = DockStyle.Fill;
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
        materialBrowserPanel.Controls.Add(_mapMakerExtractMaterialButton, 0, 0);
        materialBrowserPanel.Controls.Add(_mapMakerMaterialSearchBox, 0, 1);
        materialBrowserPanel.Controls.Add(_mapMakerMaterialTree, 0, 2);
        materialBrowserPanel.Controls.Add(_mapMakerMaterialListView, 0, 3);
        materialBrowserPanel.Controls.Add(_mapMakerMaterialPreview, 0, 4);
        materialBrowserPanel.Controls.Add(_mapMakerMaterialInfoBox, 0, 5);
        _mapWorkbenchCanvasControl = mapRightLayout;
        _mapWorkbenchMaterialPaintSplit = mapEditorSplit;
        mapEditorSplit.Panel1.Controls.Add(mapRightLayout);
        mapEditorSplit.Panel2.Controls.Add(materialBrowserPanel);

        var terrainGenerationPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Padding = new Padding(6, 0, 0, 0)
        };
        terrainGenerationPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        terrainGenerationPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        terrainGenerationPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        terrainGenerationPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        terrainGenerationPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var terrainViewPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _mapMakerTerrainLayerViewRadio.Text = "\u5730\u5f62\u89c6\u56fe";
        _mapMakerTerrainLayerViewRadio.AutoSize = true;
        _mapMakerTerrainLayerViewRadio.Checked = true;
        _mapMakerTerrainGeneratedViewRadio.Text = "\u751f\u6210\u9884\u89c8";
        _mapMakerTerrainGeneratedViewRadio.AutoSize = true;
        terrainViewPanel.Controls.Add(_mapMakerTerrainLayerViewRadio);
        terrainViewPanel.Controls.Add(_mapMakerTerrainGeneratedViewRadio);

        var terrainBrushPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _mapMakerTerrainPresetCombo.Visible = true;
        _mapMakerTerrainBrushInput.Visible = true;
        _mapMakerSaveTerrainButton.Visible = true;
        terrainBrushPanel.Controls.Add(_mapMakerTerrainToolCombo);
        terrainBrushPanel.Controls.Add(_mapMakerTerrainBrushSizeCombo);
        terrainBrushPanel.Controls.Add(_mapMakerTerrainPresetCombo);
        terrainBrushPanel.Controls.Add(_mapMakerTerrainBrushInput);
        terrainBrushPanel.Controls.Add(_mapMakerSaveTerrainButton);

        var terrainHintLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 6),
            Text = "当前页只修改草稿中的地形格；保存草稿不会写入 Hexzmap.e5，发布前会单独显示差异与绑定证据。"
        };
        _mapMakerTerrainGenerationInfoBox.Dock = DockStyle.Fill;
        _mapMakerTerrainGenerationInfoBox.Multiline = true;
        _mapMakerTerrainGenerationInfoBox.ReadOnly = true;
        _mapMakerTerrainGenerationInfoBox.ScrollBars = ScrollBars.Vertical;
        _mapMakerTerrainGenerationInfoBox.WordWrap = true;
        _mapMakerTerrainGenerationInfoBox.Text = "完成快速预览或最终生成后，这里会显示素材缺失、对象保留、回退格、耗时和渲染指纹。";

        terrainGenerationPanel.Controls.Add(terrainViewPanel, 0, 0);
        terrainGenerationPanel.Controls.Add(terrainBrushPanel, 0, 1);
        terrainGenerationPanel.Controls.Add(terrainHintLabel, 0, 2);
        terrainGenerationPanel.Controls.Add(_mapMakerTerrainGenerationInfoBox, 0, 4);

        var terrainGenerateSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildMapEditorPage.CanvasTerrainGenerate", terrainGenerateSplit, desiredDistance: 760, desiredPanel1Min: 25, desiredPanel2Min: 25);
        _mapWorkbenchTerrainGenerateSplit = terrainGenerateSplit;
        terrainGenerateSplit.Panel2.Controls.Add(terrainGenerationPanel);

        _mapWorkbenchModeTabs.Dock = DockStyle.Fill;
        _mapWorkbenchModeTabs.TabPages.Clear();
        var materialPaintPage = new TabPage("\u5730\u5f62\u7f16\u8f91");
        var terrainGeneratePage = new TabPage("\u89c6\u89c9\u91cd\u7ed8");
        materialPaintPage.Controls.Add(mapEditorSplit);
        terrainGeneratePage.Controls.Add(terrainGenerateSplit);
        _mapWorkbenchModeTabs.TabPages.Add(materialPaintPage);
        _mapWorkbenchModeTabs.TabPages.Add(terrainGeneratePage);
        AddCollapsibleSplitPanel(mapSplit, 1, "地图列表", _mapImageList, "BuildMapEditorPage.MapListEditor.MapList");
        AddCollapsibleSplitPanel(mapSplit, 2, "\u5730\u56fe\u7ed8\u5236\u65b9\u5f0f", _mapWorkbenchModeTabs, "BuildMapEditorPage.MapListEditor.MapPreview");
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

        var toolbar = CreateToolbarStack(2);
        _loadRoleEditorButton.Text = "读取角色";
        ConfigureToolbarButton(_loadRoleEditorButton, 88);
        _saveRoleEditorButton.Text = "保存角色";
        ConfigureToolbarButton(_saveRoleEditorButton, 88);
        _saveRoleEditorButton.Enabled = false;
        _openRoleInTableEditorButton.Text = "打开通用人物表";
        ConfigureToolbarButton(_openRoleInTableEditorButton, 128);
        _openRolePersonalEffectButton.Text = "剧本专属/套装(72/10)";
        ConfigureToolbarButton(_openRolePersonalEffectButton, 160);
        _openRoleEffectButton.Text = "个人特效页";
        ConfigureToolbarButton(_openRoleEffectButton, 128);
        _importRoleFaceButton.Text = "\u4e00\u952e\u5bfc\u5165\u5934\u50cf";
        ConfigureToolbarButton(_importRoleFaceButton, 118);
        _importRoleFaceButton.Enabled = false;
        _importRoleFaceButton.Visible = false;
        _batchImportRoleFaceButton.Text = "\u6279\u91cf\u5bfc\u5165\u5934\u50cf";
        ConfigureToolbarButton(_batchImportRoleFaceButton, 118);
        _batchImportRoleFaceButton.Enabled = false;
        _batchImportRoleFaceButton.Visible = false;
        _exportRoleFaceBmpButton.Text = "\u5bfc\u51fa\u5934\u50cfBMP";
        ConfigureToolbarButton(_exportRoleFaceBmpButton, 118);
        _exportRoleFaceBmpButton.Enabled = false;
        _exportRoleFaceBmpButton.Visible = false;
        _exportRoleEditorCsvButton.Text = "导出CSV";
        ConfigureToolbarButton(_exportRoleEditorCsvButton, 88);
        _exportRoleEditorCsvButton.Enabled = false;
        _importRoleEditorCsvButton.Text = "导入CSV";
        ConfigureToolbarButton(_importRoleEditorCsvButton, 88);
        _importRoleEditorCsvButton.Enabled = false;
        _copyRoleEditorSelectionButton.Text = "复制";
        ConfigureToolbarButton(_copyRoleEditorSelectionButton, 72);
        _pasteRoleEditorSelectionButton.Text = "粘贴";
        ConfigureToolbarButton(_pasteRoleEditorSelectionButton, 72);
        _batchFillRoleEditorColumnButton.Text = "批量填列";
        ConfigureToolbarButton(_batchFillRoleEditorColumnButton, 88);
        ConfigureToolbarInput(_roleEditorSearchBox, 180, 140);
        _roleEditorSearchBox.PlaceholderText = "姓名/编号/职业/形象";
        _filterRoleEditorButton.Text = "筛选";
        ConfigureToolbarButton(_filterRoleEditorButton, 72);
        _clearRoleEditorFilterButton.Text = "清除";
        ConfigureToolbarButton(_clearRoleEditorFilterButton, 72);
        AddToolbarRow(toolbar, 0,
            _loadRoleEditorButton,
            _saveRoleEditorButton,
            _openRolePersonalEffectButton,
            _openRoleEffectButton);
        AddToolbarRow(toolbar, 1,
            _exportRoleEditorCsvButton,
            _importRoleEditorCsvButton,
            _copyRoleEditorSelectionButton,
            _pasteRoleEditorSelectionButton,
            _batchFillRoleEditorColumnButton,
            CreateToolbarLabel("搜索："),
            _roleEditorSearchBox,
            _filterRoleEditorButton,
            _clearRoleEditorFilterButton);
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
        AddCollapsibleSplitPanel(roleSplit, 2, "当前角色详情", BuildRoleTextDetailPanel(), "BuildRoleEditorPage.GridTextDetail.TextDetail");

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
            RowCount = 9,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 16));
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

        var top = CreateToolbarRow();
        top.Controls.Add(new Label
        {
            Text = "当前角色详情",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            Padding = new Padding(0, 7, 12, 0)
        });
        _saveRoleTextDetailButton.Text = "保存列传/台词";
        ConfigureToolbarButton(_saveRoleTextDetailButton, 128);
        _saveRoleTextDetailButton.Enabled = false;
        top.Controls.Add(_saveRoleTextDetailButton);
        detail.Controls.Add(top, 0, 0);

        ConfigureRoleTextBox(_roleBiographyBox);
        var equipmentPanel = BuildRoleEquipmentDetailPanel();
        var criticalAssignmentPanel = BuildRoleCriticalQuoteAssignmentPanel();
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

        detail.Controls.Add(_roleBiographyBox, 0, 1);
        detail.Controls.Add(MakeHeader("默认装备"), 0, 2);
        detail.Controls.Add(equipmentPanel, 0, 3);
        detail.Controls.Add(MakeHeader("暴击台词"), 0, 4);
        detail.Controls.Add(criticalAssignmentPanel, 0, 5);
        detail.Controls.Add(criticalQuotePanel, 0, 6);
        detail.Controls.Add(MakeHeader("撤退台词"), 0, 7);
        detail.Controls.Add(_roleRetreatQuoteBox, 0, 8);
        return detail;
    }

    private Control BuildRoleCriticalQuoteAssignmentPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 2)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        ConfigureRoleCriticalQuoteCombo(_roleCriticalQuoteModeCombo);
        ConfigureRoleCriticalQuoteCombo(_roleCriticalQuoteAssignmentCombo);

        panel.Controls.Add(BuildRoleEquipmentLabel("模式"), 0, 0);
        panel.Controls.Add(_roleCriticalQuoteModeCombo, 1, 0);
        panel.Controls.Add(BuildRoleEquipmentLabel("分配"), 0, 1);
        panel.Controls.Add(_roleCriticalQuoteAssignmentCombo, 1, 1);
        return panel;
    }

    private Control BuildRoleEquipmentDetailPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3,
            Margin = new Padding(0, 0, 0, 2)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        ConfigureRoleEquipmentCombo(_roleWeaponCombo);
        ConfigureRoleEquipmentCombo(_roleArmorCombo);
        ConfigureRoleEquipmentCombo(_roleAssistCombo);

        panel.Controls.Add(BuildRoleEquipmentLabel("武器"), 0, 0);
        panel.Controls.Add(_roleWeaponCombo, 1, 0);
        panel.Controls.Add(BuildRoleEquipmentLabel("防具"), 0, 1);
        panel.Controls.Add(_roleArmorCombo, 1, 1);
        panel.Controls.Add(BuildRoleEquipmentLabel("辅助"), 0, 2);
        panel.Controls.Add(_roleAssistCombo, 1, 2);
        return panel;
    }

    private static Label BuildRoleEquipmentLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(0, 0, 8, 0)
    };

    private static void ConfigureRoleEquipmentCombo(ComboBox combo)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.DisplayMember = "显示";
        combo.ValueMember = "ID";
        combo.Enabled = false;
    }

    private static void ConfigureRoleCriticalQuoteCombo(ComboBox combo)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.DisplayMember = "显示";
        combo.ValueMember = "ID";
        combo.Enabled = false;
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
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        page.Controls.Add(tabs);

        var dataPage = new TabPage("data设定");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        dataPage.Controls.Add(layout);

        var toolbar = CreateToolbarStack(2);
        _loadItemEditorButton.Text = "读取宝物/物品";
        ConfigureToolbarButton(_loadItemEditorButton, 118);
        _saveItemEditorButton.Text = "保存宝物/物品";
        ConfigureToolbarButton(_saveItemEditorButton, 118);
        _saveItemEditorButton.Enabled = false;
        _openItemEffectCatalogButton.Text = "宝物特效";
        ConfigureToolbarButton(_openItemEffectCatalogButton, 88);
        _exportItemEditorCsvButton.Text = "导出CSV";
        ConfigureToolbarButton(_exportItemEditorCsvButton, 88);
        _exportItemEditorCsvButton.Enabled = false;
        _importItemEditorCsvButton.Text = "导入CSV";
        ConfigureToolbarButton(_importItemEditorCsvButton, 88);
        _importItemEditorCsvButton.Enabled = false;
        _copyItemEditorSelectionButton.Text = "复制";
        ConfigureToolbarButton(_copyItemEditorSelectionButton, 72);
        _pasteItemEditorSelectionButton.Text = "粘贴";
        ConfigureToolbarButton(_pasteItemEditorSelectionButton, 72);
        _batchFillItemEditorColumnButton.Text = "批量填列";
        ConfigureToolbarButton(_batchFillItemEditorColumnButton, 88);
        _undoItemEditorButton.Text = "撤回";
        ConfigureToolbarButton(_undoItemEditorButton, 72);
        _undoItemEditorButton.Enabled = false;
        _redoItemEditorButton.Text = "前进";
        ConfigureToolbarButton(_redoItemEditorButton, 72);
        _redoItemEditorButton.Enabled = false;
        _queryItemIconButton.Text = "\u67e5\u8be2\u5b9d\u7269\u56fe\u6807";
        ConfigureToolbarButton(_queryItemIconButton, 118);
        _queryItemIconButton.Enabled = false;
        _batchImportItemIconButton.Text = "\u6279\u91cf\u5bfc\u5165\u56fe\u6807";
        ConfigureToolbarButton(_batchImportItemIconButton, 118);
        _batchImportItemIconButton.Enabled = false;
        _editItemIconButton.Text = "\u7f16\u8f91\u56fe\u6807";
        ConfigureToolbarButton(_editItemIconButton, 88);
        _editItemIconButton.Enabled = false;
        _exportItemIconBmpButton.Text = "\u5bfc\u51faBMP";
        ConfigureToolbarButton(_exportItemIconBmpButton, 88);
        _exportItemIconBmpButton.Enabled = false;
        ConfigureToolbarInput(_itemEditorSearchBox, 210, 150);
        _itemEditorSearchBox.PlaceholderText = "名称/ID/类型/特效/说明";
        _filterItemEditorButton.Text = "筛选";
        ConfigureToolbarButton(_filterItemEditorButton, 72);
        _clearItemEditorFilterButton.Text = "清除";
        ConfigureToolbarButton(_clearItemEditorFilterButton, 72);
        AddToolbarRow(toolbar, 0,
            _loadItemEditorButton,
            _saveItemEditorButton,
            _openItemEffectCatalogButton,
            _exportItemEditorCsvButton,
            _importItemEditorCsvButton,
            _copyItemEditorSelectionButton,
            _pasteItemEditorSelectionButton,
            _batchFillItemEditorColumnButton,
            _undoItemEditorButton,
            _redoItemEditorButton);
        AddToolbarRow(toolbar, 1,
            _queryItemIconButton,
            _batchImportItemIconButton,
            _editItemIconButton,
            _exportItemIconBmpButton,
            CreateToolbarLabel("搜索："),
            _itemEditorSearchBox,
            _filterItemEditorButton,
            _clearItemEditorFilterButton);
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
        previewPanel.SuspendLayout();
        previewPanel.Controls.Clear();
        previewPanel.RowStyles.Clear();
        previewPanel.ColumnStyles.Clear();
        previewPanel.RowCount = 1;
        previewPanel.ColumnCount = 1;
        previewPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _itemIconPreviewSplit.Dock = DockStyle.Fill;
        _itemIconPreviewSplit.Orientation = Orientation.Horizontal;
        _itemIconPreviewSplit.SplitterWidth = 6;
        _itemIconPreviewSplit.Panel1MinSize = 80;
        _itemIconPreviewSplit.Panel2MinSize = 80;
        _itemIconPreviewSplit.FixedPanel = FixedPanel.None;
        previewPanel.Controls.Add(_itemIconPreviewSplit, 0, 0);

        var largePreviewPane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 3) };
        _itemIconLargePreviewTitle.Text = "大图";
        _itemIconLargePreviewTitle.Dock = DockStyle.Top;
        _itemIconLargePreviewTitle.Height = 24;
        _itemIconLargePreviewTitle.Font = new Font(Font, FontStyle.Bold);
        _itemIconLargePreviewTitle.TextAlign = ContentAlignment.MiddleLeft;
        _itemIconLargePreviewScrollPanel.Dock = DockStyle.Fill;
        _itemIconLargePreviewScrollPanel.AutoScroll = true;
        _itemIconLargePreviewScrollPanel.BackColor = Color.FromArgb(44, 44, 48);
        _itemIconLargePreviewScrollPanel.BorderStyle = BorderStyle.FixedSingle;
        _itemIconLargePreviewScrollPanel.TabStop = true;
        _itemIconPreviewBox.Dock = DockStyle.None;
        _itemIconPreviewBox.Location = Point.Empty;
        _itemIconPreviewBox.SizeMode = PictureBoxSizeMode.Normal;
        _itemIconPreviewBox.BackColor = Color.FromArgb(44, 44, 48);
        _itemIconPreviewBox.BorderStyle = BorderStyle.None;
        _itemIconLargePreviewScrollPanel.Controls.Add(_itemIconPreviewBox);
        largePreviewPane.Controls.Add(_itemIconLargePreviewScrollPanel);
        largePreviewPane.Controls.Add(_itemIconLargePreviewTitle);
        _itemIconPreviewSplit.Panel1.Controls.Add(largePreviewPane);

        var smallPreviewPane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 3, 0, 0) };
        _itemIconSmallPreviewTitle.Text = "小图";
        _itemIconSmallPreviewTitle.Dock = DockStyle.Top;
        _itemIconSmallPreviewTitle.Height = 24;
        _itemIconSmallPreviewTitle.Font = new Font(Font, FontStyle.Bold);
        _itemIconSmallPreviewTitle.TextAlign = ContentAlignment.MiddleLeft;
        _itemIconSmallPreviewScrollPanel.Dock = DockStyle.Fill;
        _itemIconSmallPreviewScrollPanel.AutoScroll = true;
        _itemIconSmallPreviewScrollPanel.BackColor = Color.FromArgb(44, 44, 48);
        _itemIconSmallPreviewScrollPanel.BorderStyle = BorderStyle.FixedSingle;
        _itemIconSmallPreviewScrollPanel.TabStop = true;
        _itemIconSmallPreviewBox.Dock = DockStyle.None;
        _itemIconSmallPreviewBox.Location = Point.Empty;
        _itemIconSmallPreviewBox.SizeMode = PictureBoxSizeMode.Normal;
        _itemIconSmallPreviewBox.BackColor = Color.FromArgb(44, 44, 48);
        _itemIconSmallPreviewBox.BorderStyle = BorderStyle.None;
        _itemIconSmallPreviewScrollPanel.Controls.Add(_itemIconSmallPreviewBox);
        smallPreviewPane.Controls.Add(_itemIconSmallPreviewScrollPanel);
        smallPreviewPane.Controls.Add(_itemIconSmallPreviewTitle);
        _itemIconPreviewSplit.Panel2.Controls.Add(smallPreviewPane);

        _itemIconLargePreviewScrollPanel.MouseEnter += (_, _) => _itemIconLargePreviewScrollPanel.Focus();
        _itemIconSmallPreviewScrollPanel.MouseEnter += (_, _) => _itemIconSmallPreviewScrollPanel.Focus();
        _itemIconPreviewBox.MouseEnter += (_, _) => _itemIconLargePreviewScrollPanel.Focus();
        _itemIconSmallPreviewBox.MouseEnter += (_, _) => _itemIconSmallPreviewScrollPanel.Focus();
        _itemIconLargePreviewScrollPanel.MouseWheel += (_, e) => HandleItemIconPreviewMouseWheel(ItemIconPreviewRole.Large, e);
        _itemIconSmallPreviewScrollPanel.MouseWheel += (_, e) => HandleItemIconPreviewMouseWheel(ItemIconPreviewRole.Small, e);
        _itemIconPreviewBox.MouseWheel += (_, e) => HandleItemIconPreviewMouseWheel(ItemIconPreviewRole.Large, e);
        _itemIconSmallPreviewBox.MouseWheel += (_, e) => HandleItemIconPreviewMouseWheel(ItemIconPreviewRole.Small, e);
        _itemIconPreviewBox.DoubleClick += (_, _) => ResetItemIconPreviewZoom(ItemIconPreviewRole.Large);
        _itemIconSmallPreviewBox.DoubleClick += (_, _) => ResetItemIconPreviewZoom(ItemIconPreviewRole.Small);
        _itemIconLargePreviewScrollPanel.Resize += (_, _) =>
        {
            if (_itemIconLargeZoomPercent == 0) RenderItemIconPreview(ItemIconPreviewRole.Large);
        };
        _itemIconSmallPreviewScrollPanel.Resize += (_, _) =>
        {
            if (_itemIconSmallZoomPercent == 0) RenderItemIconPreview(ItemIconPreviewRole.Small);
        };
        _itemIconPreviewInfoBox.Visible = false;
        previewPanel.ResumeLayout();

        _itemEditorInfoBox.Dock = DockStyle.Fill;
        _itemEditorInfoBox.Multiline = true;
        _itemEditorInfoBox.ReadOnly = true;
        _itemEditorInfoBox.ScrollBars = ScrollBars.Vertical;
        _itemEditorInfoBox.WordWrap = true;
        _itemEditorInfoBox.Text = "宝物设定：按旧版格式显示 ID、图标、名称、类别、能力、价格、特效和介绍；特效名随特效号动态映射，可通过“宝物特效”维护项目侧 UTF-8 特效目录。";
        AddCollapsibleSplitPanel(body, 2, "图标预览", previewPanel, "BuildItemEditorPage.GridPreview.Preview");
        layout.Controls.Add(body, 0, 1);
        tabs.TabPages.Add(dataPage);
        tabs.TabPages.Add(BuildItemEquipmentTypeSettingsPage());
        return page;
    }

    private TabPage BuildItemEquipmentTypeSettingsPage()
    {
        var page = new TabPage("装备类型");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        page.Controls.Add(layout);

        var toolbar = CreateToolbarRow();
        _loadItemEquipmentTypeSettingsButton.Text = "读取装备类型";
        ConfigureToolbarButton(_loadItemEquipmentTypeSettingsButton, 118);
        _saveItemEquipmentTypeSettingsButton.Text = "保存装备类型";
        ConfigureToolbarButton(_saveItemEquipmentTypeSettingsButton, 118);
        _saveItemEquipmentTypeSettingsButton.Enabled = false;
        toolbar.Controls.Add(_loadItemEquipmentTypeSettingsButton);
        toolbar.Controls.Add(_saveItemEquipmentTypeSettingsButton);
        layout.Controls.Add(toolbar, 0, 0);

        _itemEquipmentTypeGrid.Dock = DockStyle.Fill;
        _itemEquipmentTypeGrid.AutoGenerateColumns = false;
        _itemEquipmentTypeGrid.AllowUserToAddRows = false;
        _itemEquipmentTypeGrid.AllowUserToDeleteRows = false;
        _itemEquipmentTypeGrid.RowHeadersVisible = false;
        _itemEquipmentTypeGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _itemEquipmentTypeGrid.MultiSelect = false;
        _itemEquipmentTypeGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _itemEquipmentTypeGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Description",
            DataPropertyName = "Description",
            HeaderText = "说明",
            ReadOnly = true,
            FillWeight = 24
        });
        _itemEquipmentTypeGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            DataPropertyName = "Name",
            HeaderText = "名称",
            FillWeight = 22
        });
        _itemEquipmentTypeGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Visible",
            DataPropertyName = "Visible",
            HeaderText = "显示",
            ThreeState = true,
            TrueValue = true,
            FalseValue = false,
            IndeterminateValue = DBNull.Value,
            FillWeight = 12
        });
        _itemEquipmentTypeGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "EquipmentSummary",
            DataPropertyName = "EquipmentSummary",
            HeaderText = "可装备部队",
            ReadOnly = true,
            FillWeight = 42
        });

        _itemEquipmentTypeJobGrid.Dock = DockStyle.Fill;
        _itemEquipmentTypeJobGrid.AutoGenerateColumns = false;
        _itemEquipmentTypeJobGrid.AllowUserToAddRows = false;
        _itemEquipmentTypeJobGrid.AllowUserToDeleteRows = false;
        _itemEquipmentTypeJobGrid.RowHeadersVisible = false;
        _itemEquipmentTypeJobGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _itemEquipmentTypeJobGrid.MultiSelect = false;
        _itemEquipmentTypeJobGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _itemEquipmentTypeJobGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SeriesName",
            DataPropertyName = "SeriesName",
            HeaderText = "兵种系",
            ReadOnly = true,
            FillWeight = 32
        });
        _itemEquipmentTypeJobGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "JobName",
            DataPropertyName = "JobName",
            HeaderText = "部队",
            ReadOnly = true,
            FillWeight = 40
        });
        _itemEquipmentTypeJobGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "State",
            DataPropertyName = "State",
            HeaderText = "可装备",
            ThreeState = true,
            TrueValue = CheckState.Checked,
            FalseValue = CheckState.Unchecked,
            IndeterminateValue = CheckState.Indeterminate,
            FillWeight = 18
        });

        var split = CreateResizableSplit("BuildItemEquipmentTypeSettingsPage.Main", Orientation.Vertical, 720);
        AddCollapsibleSplitPanel(split, 1, "装备类型", _itemEquipmentTypeGrid, "BuildItemEquipmentTypeSettingsPage.TypeGrid");
        AddCollapsibleSplitPanel(split, 2, "可装备部队", _itemEquipmentTypeJobGrid, "BuildItemEquipmentTypeSettingsPage.JobGrid");
        layout.Controls.Add(split, 0, 1);

        _itemEquipmentTypeInfoBox.Dock = DockStyle.Fill;
        _itemEquipmentTypeInfoBox.Multiline = true;
        _itemEquipmentTypeInfoBox.ReadOnly = true;
        _itemEquipmentTypeInfoBox.ScrollBars = ScrollBars.Vertical;
        _itemEquipmentTypeInfoBox.WordWrap = true;
        _itemEquipmentTypeInfoBox.Text = "读取后编辑装备类型名称、显示状态和可装备部队。";
        layout.Controls.Add(_itemEquipmentTypeInfoBox, 0, 2);

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

        var toolbar = CreateToolbarStack(2);
        _loadJobEditorButton.Text = "读取兵种";
        ConfigureToolbarButton(_loadJobEditorButton, 88);
        _saveJobEditorButton.Text = "保存兵种";
        ConfigureToolbarButton(_saveJobEditorButton, 88);
        _saveJobEditorButton.Enabled = false;
        _editAccessoryJobGroupsButton.Text = "辅助分组";
        ConfigureToolbarButton(_editAccessoryJobGroupsButton, 88);
        _editAccessoryJobGroupsButton.Enabled = false;
        _replaceJobSImageButton.Text = "一键替换兵种形象";
        ConfigureToolbarButton(_replaceJobSImageButton, 142);
        _replaceJobSImageButton.Enabled = false;
        _playJobSImageButton.Text = "播放S";
        ConfigureToolbarButton(_playJobSImageButton, 78);
        _playJobSImageButton.Enabled = false;
        _viewJobSSingleFramesButton.Text = "查看单帧S";
        ConfigureToolbarButton(_viewJobSSingleFramesButton, 104);
        _viewJobSSingleFramesButton.Enabled = false;
        _editJobSImagePixelsButton.Text = "像素编辑兵种S";
        ConfigureToolbarButton(_editJobSImagePixelsButton, 128);
        _editJobSImagePixelsButton.Enabled = false;
        _batchReplaceJobSImageButton.Text = "批量导入兵种S";
        ConfigureToolbarButton(_batchReplaceJobSImageButton, 132);
        _batchReplaceJobSImageButton.Enabled = false;
        _exportJobSImageBmpButton.Text = "导出兵种S";
        ConfigureToolbarButton(_exportJobSImageBmpButton, 104);
        _exportJobSImageBmpButton.Enabled = false;
        _openJobSeriesTableButton.Text = "通用兵种系表";
        ConfigureToolbarButton(_openJobSeriesTableButton, 118);
        _openJobEffectTableButton.Text = "兵种特效页";
        ConfigureToolbarButton(_openJobEffectTableButton, 104);
        _exportJobEditorCsvButton.Text = "导出CSV";
        ConfigureToolbarButton(_exportJobEditorCsvButton, 88);
        _exportJobEditorCsvButton.Enabled = false;
        _importJobEditorCsvButton.Text = "导入CSV";
        ConfigureToolbarButton(_importJobEditorCsvButton, 88);
        _importJobEditorCsvButton.Enabled = false;
        _copyJobEditorSelectionButton.Text = "复制";
        ConfigureToolbarButton(_copyJobEditorSelectionButton, 72);
        _pasteJobEditorSelectionButton.Text = "粘贴";
        ConfigureToolbarButton(_pasteJobEditorSelectionButton, 72);
        _batchFillJobEditorColumnButton.Text = "批量填列";
        ConfigureToolbarButton(_batchFillJobEditorColumnButton, 88);
        _undoJobEditorButton.Text = "后退";
        ConfigureToolbarButton(_undoJobEditorButton, 72);
        _undoJobEditorButton.Enabled = false;
        _redoJobEditorButton.Text = "前进";
        ConfigureToolbarButton(_redoJobEditorButton, 72);
        _redoJobEditorButton.Enabled = false;
        ConfigureToolbarInput(_jobEditorSearchBox, 180, 140);
        _jobEditorSearchBox.PlaceholderText = "兵种名/说明/数值";
        _filterJobEditorButton.Text = "筛选";
        ConfigureToolbarButton(_filterJobEditorButton, 72);
        _clearJobEditorFilterButton.Text = "清除";
        ConfigureToolbarButton(_clearJobEditorFilterButton, 72);
        AddToolbarRow(toolbar, 0,
            _loadJobEditorButton,
            _saveJobEditorButton,
            _editAccessoryJobGroupsButton,
            _replaceJobSImageButton,
            _playJobSImageButton,
            _viewJobSSingleFramesButton,
            _editJobSImagePixelsButton,
            _batchReplaceJobSImageButton,
            _exportJobSImageBmpButton,
            _openJobEffectTableButton);
        AddToolbarRow(toolbar, 1,
            _exportJobEditorCsvButton,
            _importJobEditorCsvButton,
            _copyJobEditorSelectionButton,
            _pasteJobEditorSelectionButton,
            _batchFillJobEditorColumnButton,
            _undoJobEditorButton,
            _redoJobEditorButton,
            CreateToolbarLabel("搜索："),
            _jobEditorSearchBox,
            _filterJobEditorButton,
            _clearJobEditorFilterButton);
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
        previewPanel.Controls.Add(new Label { Text = "预览", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        var previewContentPanel = new Panel
        {
            Dock = DockStyle.Fill
        };
        previewPanel.Controls.Add(previewContentPanel, 0, 1);
        var imagePreviewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        imagePreviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        imagePreviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        previewContentPanel.Controls.Add(imagePreviewLayout);
        _jobAreaPreviewBox.Dock = DockStyle.Fill;
        _jobAreaPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _jobAreaPreviewBox.BackColor = Color.White;
        imagePreviewLayout.Controls.Add(_jobAreaPreviewBox, 0, 0);
        _jobAreaPreviewInfoBox.Dock = DockStyle.Fill;
        _jobAreaPreviewInfoBox.Multiline = true;
        _jobAreaPreviewInfoBox.ReadOnly = false;
        _jobAreaPreviewInfoBox.ScrollBars = ScrollBars.Vertical;
        _jobAreaPreviewInfoBox.WordWrap = true;
        _jobAreaPreviewInfoBox.Text = "读取兵种后，在此编辑当前兵种介绍。";
        imagePreviewLayout.Controls.Add(_jobAreaPreviewInfoBox, 0, 1);

        _jobEquipmentEditorPanel.Dock = DockStyle.Fill;
        _jobEquipmentEditorPanel.Visible = false;
        _jobEquipmentEditorPanel.BorderStyle = BorderStyle.FixedSingle;
        _jobEquipmentEditorPanel.Padding = new Padding(8);
        var equipmentEditorRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        equipmentEditorRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        equipmentEditorRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        equipmentEditorRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _jobEquipmentEditorPanel.Controls.Add(equipmentEditorRoot);

        _jobEquipmentEditorTitleLabel.Dock = DockStyle.Fill;
        _jobEquipmentEditorTitleLabel.AutoSize = true;
        _jobEquipmentEditorTitleLabel.Font = new Font(Font, FontStyle.Bold);
        equipmentEditorRoot.Controls.Add(_jobEquipmentEditorTitleLabel, 0, 0);

        var equipmentScrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(0, 6, 0, 6)
        };
        equipmentEditorRoot.Controls.Add(equipmentScrollPanel, 0, 1);

        _jobEquipmentCheckGrid.Dock = DockStyle.Top;
        _jobEquipmentCheckGrid.AutoSize = true;
        _jobEquipmentCheckGrid.ColumnCount = 2;
        _jobEquipmentCheckGrid.RowCount = 0;
        _jobEquipmentCheckGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _jobEquipmentCheckGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        equipmentScrollPanel.Controls.Add(_jobEquipmentCheckGrid);

        _jobEquipmentEditorStatusLabel.Dock = DockStyle.Fill;
        _jobEquipmentEditorStatusLabel.AutoSize = true;
        _jobEquipmentEditorStatusLabel.Padding = new Padding(0, 6, 0, 0);
        equipmentEditorRoot.Controls.Add(_jobEquipmentEditorStatusLabel, 0, 2);
        previewContentPanel.Controls.Add(_jobEquipmentEditorPanel);
        AddCollapsibleSplitPanel(detailBody, 2, "预览", previewPanel, "BuildJobEditorPage.DetailGridPreview.Preview");

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

        var terrainToolbar = CreateToolbarRow();
        _loadJobTerrainButton.Text = "读取兵种系/地形";
        ConfigureToolbarButton(_loadJobTerrainButton, 128);
        _saveJobTerrainButton.Text = "保存兵种系/地形";
        ConfigureToolbarButton(_saveJobTerrainButton, 128);
        _saveJobTerrainButton.Enabled = false;
        _openJobRestraintTableButton.Text = "通用兵种相克表";
        ConfigureToolbarButton(_openJobRestraintTableButton, 128);
        _openJobRestraintTableButton.Visible = false;
        ConfigureToolbarInput(_jobTerrainSearchBox, 200, 150);
        _jobTerrainSearchBox.PlaceholderText = "兵种系/地形/数值";
        _filterJobTerrainButton.Text = "筛选";
        ConfigureToolbarButton(_filterJobTerrainButton, 72);
        _clearJobTerrainFilterButton.Text = "清除";
        ConfigureToolbarButton(_clearJobTerrainFilterButton, 72);
        terrainToolbar.Controls.AddRange(new Control[]
        {
            _loadJobTerrainButton,
            _saveJobTerrainButton,
            CreateToolbarLabel("搜索："),
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

        var matrixToolbar = CreateToolbarRow();
        _loadJobMatrixButton.Text = "读取矩阵";
        ConfigureToolbarButton(_loadJobMatrixButton, 88);
        _saveJobMatrixButton.Text = "保存矩阵";
        ConfigureToolbarButton(_saveJobMatrixButton, 88);
        _saveJobMatrixButton.Enabled = false;
        _openJobMatrixRestraintTableButton.Text = "通用兵种相克表";
        ConfigureToolbarButton(_openJobMatrixRestraintTableButton, 128);
        _openJobMatrixRestraintTableButton.Visible = false;
        matrixToolbar.Controls.AddRange(new Control[]
        {
            _loadJobMatrixButton,
            _saveJobMatrixButton
        });
        matrixLayout.Controls.Add(matrixToolbar, 0, 0);

        _jobRestraintGrid.Dock = DockStyle.Fill;
        _jobRestraintGrid.AllowUserToAddRows = false;
        _jobRestraintGrid.AllowUserToDeleteRows = false;
        _jobRestraintGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _jobRestraintGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        matrixLayout.Controls.Add(CreateSearchableGridPanel(_jobRestraintGrid, "兵种/数值"), 0, 1);
        tabs.TabPages.Add(matrixPage);

        var attributePage = new TabPage("兵种属性");
        var attributeLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        attributeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        attributeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        attributePage.Controls.Add(attributeLayout);

        var attributeToolbar = CreateToolbarRow();
        _loadJobAttributeMatrixButton.Text = "读取矩阵";
        ConfigureToolbarButton(_loadJobAttributeMatrixButton, 88);
        _saveJobAttributeMatrixButton.Text = "保存矩阵";
        ConfigureToolbarButton(_saveJobAttributeMatrixButton, 88);
        _saveJobAttributeMatrixButton.Enabled = false;
        _openJobMatrixAttributeTableButton.Text = "通用兵种属性表";
        ConfigureToolbarButton(_openJobMatrixAttributeTableButton, 128);
        _openJobMatrixAttributeTableButton.Visible = false;
        _exportJobAttributeCsvButton.Text = "导出属性CSV";
        ConfigureToolbarButton(_exportJobAttributeCsvButton, 108);
        _exportJobAttributeCsvButton.Enabled = false;
        _importJobAttributeCsvButton.Text = "导入属性CSV";
        ConfigureToolbarButton(_importJobAttributeCsvButton, 108);
        _importJobAttributeCsvButton.Enabled = false;
        _pasteJobMatrixSelectionButton.Text = "粘贴";
        ConfigureToolbarButton(_pasteJobMatrixSelectionButton, 64);
        _pasteJobMatrixSelectionButton.Enabled = false;
        _fillJobMatrixSelectionButton.Text = "填充所选";
        ConfigureToolbarButton(_fillJobMatrixSelectionButton, 88);
        _fillJobMatrixSelectionButton.Enabled = false;
        _batchModifyJobMatrixButton.Text = "批量编辑";
        ConfigureToolbarButton(_batchModifyJobMatrixButton, 88);
        _batchModifyJobMatrixButton.Enabled = false;
        attributeToolbar.Controls.AddRange(new Control[]
        {
            _loadJobAttributeMatrixButton,
            _saveJobAttributeMatrixButton,
            _exportJobAttributeCsvButton,
            _importJobAttributeCsvButton,
            _pasteJobMatrixSelectionButton,
            _fillJobMatrixSelectionButton,
            _batchModifyJobMatrixButton
        });
        attributeLayout.Controls.Add(attributeToolbar, 0, 0);

        _jobAttributeGrid.Dock = DockStyle.Fill;
        _jobAttributeGrid.AllowUserToAddRows = false;
        _jobAttributeGrid.AllowUserToDeleteRows = false;
        _jobAttributeGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _jobAttributeGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        attributeLayout.Controls.Add(CreateSearchableGridPanel(_jobAttributeGrid, "字段/兵种/数值"), 0, 1);

        _jobMatrixInfoBox.Dock = DockStyle.Fill;
        _jobMatrixInfoBox.Multiline = true;
        _jobMatrixInfoBox.ReadOnly = true;
        _jobMatrixInfoBox.ScrollBars = ScrollBars.Vertical;
        _jobMatrixInfoBox.WordWrap = true;
        _jobMatrixInfoBox.Text = "相克/属性矩阵：读取后可编辑 40x40 兵种相克矩阵和 8x40 兵种属性矩阵；保存前自动备份，保存后复读校验。";
        tabs.TabPages.Add(attributePage);

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

        var strategyToolbar = CreateToolbarStack(2);
        _loadJobStrategyEditorButton.Text = "读取兵种策略";
        ConfigureToolbarButton(_loadJobStrategyEditorButton, 118);
        _saveJobStrategyEditorButton.Text = "保存兵种策略";
        ConfigureToolbarButton(_saveJobStrategyEditorButton, 118);
        _saveJobStrategyEditorButton.Enabled = false;
        _importJobStrategyIconButton.Text = "导入策略图标";
        ConfigureToolbarButton(_importJobStrategyIconButton, 118);
        _importJobStrategyIconButton.Enabled = false;
        _editJobStrategyIconButton.Text = "\u7f16\u8f91\u56fe\u6807";
        ConfigureToolbarButton(_editJobStrategyIconButton, 88);
        _editJobStrategyIconButton.Enabled = false;
        _exportJobStrategyIconBmpButton.Text = "\u5bfc\u51faBMP";
        ConfigureToolbarButton(_exportJobStrategyIconBmpButton, 88);
        _exportJobStrategyIconBmpButton.Enabled = false;
        _openJobStrategyTableButton.Text = "通用策略表";
        ConfigureToolbarButton(_openJobStrategyTableButton, 104);
        ConfigureToolbarInput(_jobStrategyEditorSearchBox, 220, 150);
        _jobStrategyEditorSearchBox.PlaceholderText = "策略名/兵种/等级/属性";
        _filterJobStrategyEditorButton.Text = "筛选";
        ConfigureToolbarButton(_filterJobStrategyEditorButton, 72);
        _clearJobStrategyEditorFilterButton.Text = "清除";
        ConfigureToolbarButton(_clearJobStrategyEditorFilterButton, 72);
        AddToolbarRow(strategyToolbar, 0,
            _loadJobStrategyEditorButton,
            _saveJobStrategyEditorButton,
            _importJobStrategyIconButton,
            _editJobStrategyIconButton,
            _exportJobStrategyIconBmpButton);
        AddToolbarRow(strategyToolbar, 1,
            CreateToolbarLabel("搜索：", 0),
            _jobStrategyEditorSearchBox,
            _filterJobStrategyEditorButton,
            _clearJobStrategyEditorFilterButton);
        strategyLayout.Controls.Add(strategyToolbar, 0, 0);

        _jobStrategyEditorGrid.Dock = DockStyle.Fill;
        _jobStrategyEditorGrid.AllowUserToAddRows = false;
        _jobStrategyEditorGrid.AllowUserToDeleteRows = false;
        _jobStrategyEditorGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _jobStrategyEditorGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _jobStrategyEditorGrid.MultiSelect = true;

        var strategyBody = CreateResizableSplit("BuildJobEditorPage.StrategyGridPreview", Orientation.Vertical, 760);
        AddCollapsibleSplitPanel(strategyBody, 1, "策略表", _jobStrategyEditorGrid, "BuildJobEditorPage.StrategyGridPreview.Grid");

        var strategyPreviewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(8, 0, 0, 0)
        };
        strategyPreviewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        strategyPreviewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        strategyPreviewPanel.Controls.Add(new Label { Text = "策略预览", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);

        var strategyPreviewContentPanel = new Panel
        {
            Dock = DockStyle.Fill
        };
        strategyPreviewPanel.Controls.Add(strategyPreviewContentPanel, 0, 1);

        var strategyImagePreviewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1
        };
        strategyImagePreviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        strategyImagePreviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        strategyImagePreviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        strategyPreviewContentPanel.Controls.Add(strategyImagePreviewLayout);

        _jobStrategyPreviewBox.Dock = DockStyle.Fill;
        _jobStrategyPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _jobStrategyPreviewBox.BackColor = Color.White;
        _jobStrategyPreviewBox.BorderStyle = BorderStyle.FixedSingle;
        strategyImagePreviewLayout.Controls.Add(_jobStrategyPreviewBox, 0, 0);
        _jobStrategyPreviewInfoBox.Dock = DockStyle.Fill;
        _jobStrategyPreviewInfoBox.Multiline = true;
        _jobStrategyPreviewInfoBox.ReadOnly = true;
        _jobStrategyPreviewInfoBox.ScrollBars = ScrollBars.Vertical;
        _jobStrategyPreviewInfoBox.WordWrap = true;
        _jobStrategyPreviewInfoBox.Text = "读取兵种策略后，单击“名称”可在右侧编辑 80 个兵种的学习等级；选择“施法范围”“穿透范围”“策略图标”“小动画”“大动画”会显示对应资源预览；其它字段显示兵种学习情况和可编辑策略介绍。";
        strategyImagePreviewLayout.Controls.Add(_jobStrategyPreviewInfoBox, 0, 1);

        _jobStrategyDescriptionBox.Dock = DockStyle.Fill;
        _jobStrategyDescriptionBox.Multiline = true;
        _jobStrategyDescriptionBox.ReadOnly = false;
        _jobStrategyDescriptionBox.ScrollBars = ScrollBars.Vertical;
        _jobStrategyDescriptionBox.WordWrap = true;
        strategyImagePreviewLayout.Controls.Add(_jobStrategyDescriptionBox, 0, 2);

        _jobStrategyLearningEditorPanel.Dock = DockStyle.Fill;
        _jobStrategyLearningEditorPanel.Visible = false;
        _jobStrategyLearningEditorPanel.BorderStyle = BorderStyle.FixedSingle;
        _jobStrategyLearningEditorPanel.Padding = new Padding(8);
        var strategyLearningEditorRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        strategyLearningEditorRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        strategyLearningEditorRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        strategyLearningEditorRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        strategyLearningEditorRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _jobStrategyLearningEditorPanel.Controls.Add(strategyLearningEditorRoot);

        _jobStrategyLearningEditorTitleLabel.Dock = DockStyle.Fill;
        _jobStrategyLearningEditorTitleLabel.AutoSize = true;
        _jobStrategyLearningEditorTitleLabel.Font = new Font(Font, FontStyle.Bold);
        strategyLearningEditorRoot.Controls.Add(_jobStrategyLearningEditorTitleLabel, 0, 0);

        _jobStrategyLearningEditorGrid.Dock = DockStyle.Fill;
        _jobStrategyLearningEditorGrid.AllowUserToAddRows = false;
        _jobStrategyLearningEditorGrid.AllowUserToDeleteRows = false;
        _jobStrategyLearningEditorGrid.AutoGenerateColumns = true;
        _jobStrategyLearningEditorGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _jobStrategyLearningEditorGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _jobStrategyLearningEditorGrid.MultiSelect = false;
        strategyLearningEditorRoot.Controls.Add(_jobStrategyLearningEditorGrid, 0, 1);

        _jobStrategyLearningDescriptionBox.Dock = DockStyle.Fill;
        _jobStrategyLearningDescriptionBox.Multiline = true;
        _jobStrategyLearningDescriptionBox.ReadOnly = false;
        _jobStrategyLearningDescriptionBox.ScrollBars = ScrollBars.Vertical;
        _jobStrategyLearningDescriptionBox.WordWrap = true;
        _jobStrategyLearningDescriptionBox.Margin = new Padding(0, 8, 0, 0);
        _jobStrategyLearningDescriptionBox.Visible = false;
        strategyLearningEditorRoot.Controls.Add(_jobStrategyLearningDescriptionBox, 0, 2);

        _jobStrategyLearningEditorStatusLabel.Dock = DockStyle.Fill;
        _jobStrategyLearningEditorStatusLabel.AutoSize = true;
        _jobStrategyLearningEditorStatusLabel.Padding = new Padding(0, 6, 0, 0);
        strategyLearningEditorRoot.Controls.Add(_jobStrategyLearningEditorStatusLabel, 0, 3);
        strategyPreviewContentPanel.Controls.Add(_jobStrategyLearningEditorPanel);

        _jobStrategyBitFlagsEditorPanel.Dock = DockStyle.Fill;
        _jobStrategyBitFlagsEditorPanel.Visible = false;
        _jobStrategyBitFlagsEditorPanel.BorderStyle = BorderStyle.FixedSingle;
        _jobStrategyBitFlagsEditorPanel.Padding = new Padding(8);
        var strategyBitFlagsRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5
        };
        strategyBitFlagsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        strategyBitFlagsRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        strategyBitFlagsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        strategyBitFlagsRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        strategyBitFlagsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _jobStrategyBitFlagsEditorPanel.Controls.Add(strategyBitFlagsRoot);

        _jobStrategyBitFlagsTitleLabel.Dock = DockStyle.Fill;
        _jobStrategyBitFlagsTitleLabel.AutoSize = true;
        _jobStrategyBitFlagsTitleLabel.Font = new Font(Font, FontStyle.Bold);
        strategyBitFlagsRoot.Controls.Add(_jobStrategyBitFlagsTitleLabel, 0, 0);

        _jobStrategyBitFlagsInfoBox.Dock = DockStyle.Fill;
        _jobStrategyBitFlagsInfoBox.Multiline = true;
        _jobStrategyBitFlagsInfoBox.ReadOnly = true;
        _jobStrategyBitFlagsInfoBox.ScrollBars = ScrollBars.Vertical;
        _jobStrategyBitFlagsInfoBox.WordWrap = true;
        _jobStrategyBitFlagsInfoBox.Margin = new Padding(0, 6, 0, 6);
        strategyBitFlagsRoot.Controls.Add(_jobStrategyBitFlagsInfoBox, 0, 1);

        var strategyBitFlagsNumericPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 6)
        };
        strategyBitFlagsNumericPanel.Controls.Add(new Label
        {
            Text = "原始值",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 4, 6, 0)
        });
        _jobStrategyBitFlagsNumeric.Minimum = 0;
        _jobStrategyBitFlagsNumeric.Maximum = 255;
        _jobStrategyBitFlagsNumeric.Width = 86;
        strategyBitFlagsNumericPanel.Controls.Add(_jobStrategyBitFlagsNumeric);
        strategyBitFlagsRoot.Controls.Add(strategyBitFlagsNumericPanel, 0, 2);

        var strategyBitFlagsCheckPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Margin = new Padding(0, 0, 0, 6)
        };
        strategyBitFlagsCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        strategyBitFlagsCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var rowIndex = 0; rowIndex < 4; rowIndex++)
        {
            strategyBitFlagsCheckPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        }

        for (var i = 0; i < _jobStrategyBitFlagCheckBoxes.Length; i++)
        {
            var checkBox = _jobStrategyBitFlagCheckBoxes[i];
            checkBox.Dock = DockStyle.Fill;
            checkBox.AutoEllipsis = true;
            checkBox.Margin = new Padding(0, 2, 8, 2);
            strategyBitFlagsCheckPanel.Controls.Add(checkBox, i % 2, i / 2);
        }

        strategyBitFlagsRoot.Controls.Add(strategyBitFlagsCheckPanel, 0, 3);

        _jobStrategyBitFlagsStatusLabel.Dock = DockStyle.Fill;
        _jobStrategyBitFlagsStatusLabel.AutoSize = true;
        _jobStrategyBitFlagsStatusLabel.Padding = new Padding(0, 6, 0, 0);
        strategyBitFlagsRoot.Controls.Add(_jobStrategyBitFlagsStatusLabel, 0, 4);
        strategyPreviewContentPanel.Controls.Add(_jobStrategyBitFlagsEditorPanel);

        SetJobStrategyPreviewLayout(JobStrategyPreviewLayoutMode.LearningAndDescription);
        AddCollapsibleSplitPanel(strategyBody, 2, "策略预览", strategyPreviewPanel, "BuildJobEditorPage.StrategyGridPreview.Preview");

        _jobStrategyEditorInfoBox.Dock = DockStyle.Fill;
        _jobStrategyEditorInfoBox.Multiline = true;
        _jobStrategyEditorInfoBox.ReadOnly = true;
        _jobStrategyEditorInfoBox.ScrollBars = ScrollBars.Vertical;
        _jobStrategyEditorInfoBox.WordWrap = true;
        _jobStrategyEditorInfoBox.Text = "兵种策略：读取后可编辑策略基础属性、EKD5 策略附表属性；单击名称列可在右侧编辑各详细兵种的学会等级，学会等级为 0 表示该兵种不能学习。";
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

        var effectToolbar = CreateToolbarRow();
        _loadJobEffectEditorButton.Text = "读取兵种特效";
        ConfigureToolbarButton(_loadJobEffectEditorButton, 118);
        _saveJobEffectEditorButton.Text = "保存兵种特效";
        ConfigureToolbarButton(_saveJobEffectEditorButton, 118);
        _saveJobEffectEditorButton.Enabled = false;
        _openJobExclusiveEffectTableButton.Text = "通用专属/套装表";
        ConfigureToolbarButton(_openJobExclusiveEffectTableButton, 138);
        ConfigureToolbarInput(_jobEffectEditorSearchBox, 220, 150);
        _jobEffectEditorSearchBox.PlaceholderText = "特效名/说明/武将/兵种";
        _filterJobEffectEditorButton.Text = "筛选";
        ConfigureToolbarButton(_filterJobEffectEditorButton, 72);
        _clearJobEffectEditorFilterButton.Text = "清除";
        ConfigureToolbarButton(_clearJobEffectEditorFilterButton, 72);
        effectToolbar.Controls.AddRange(new Control[]
        {
            _loadJobEffectEditorButton,
            _saveJobEffectEditorButton,
            CreateToolbarLabel("搜索："),
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

        var toolbar = CreateToolbarStack(2);
        _loadBattlefieldButton.Text = "读取关卡";
        ConfigureToolbarButton(_loadBattlefieldButton, 88);
        _battlefieldScenarioCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_battlefieldScenarioCombo, 260, 180);
        _saveBattlefieldTextsButton.Text = "保存标题/胜败条件";
        ConfigureToolbarButton(_saveBattlefieldTextsButton, 150);
        _saveBattlefieldTextsButton.Enabled = false;
        _saveBattlefieldUnitReviewsButton.Text = "保存出场核对";
        ConfigureToolbarButton(_saveBattlefieldUnitReviewsButton, 118);
        _saveBattlefieldUnitReviewsButton.Enabled = false;
        _writeBattlefieldDeploymentButton.Text = "写回出场到S剧本";
        ConfigureToolbarButton(_writeBattlefieldDeploymentButton, 142);
        _writeBattlefieldDeploymentButton.Enabled = false;
        _jumpBattlefieldMapButton.Text = "跳到地图编辑";
        ConfigureToolbarButton(_jumpBattlefieldMapButton, 118);
        _jumpBattlefieldMapButton.Enabled = false;
        _jumpBattlefieldScenarioButton.Text = "跳到剧本编辑";
        ConfigureToolbarButton(_jumpBattlefieldScenarioButton, 118);
        _jumpBattlefieldScenarioButton.Enabled = false;
        AddToolbarRow(toolbar, 0,
            _loadBattlefieldButton,
            CreateToolbarLabel("关卡："),
            _battlefieldScenarioCombo,
            _saveBattlefieldTextsButton,
            _saveBattlefieldUnitReviewsButton);
        AddToolbarRow(toolbar, 1,
            _writeBattlefieldDeploymentButton,
            _jumpBattlefieldMapButton,
            _jumpBattlefieldScenarioButton);
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

        var scriptToolbar = CreateToolbarStack(2);
        ConfigureToolbarInput(_battlefieldScriptSearchBox, 160, 120);
        _battlefieldScriptSearchBox.PlaceholderText = "搜索S剧本";
        _battlefieldScriptSearchButton.Text = "搜索";
        ConfigureToolbarButton(_battlefieldScriptSearchButton, 72);
        _battlefieldScriptClearSearchButton.Text = "清除";
        ConfigureToolbarButton(_battlefieldScriptClearSearchButton, 72);
        ConfigureToolbarInput(_battlefieldScriptReplaceBox, 150, 110);
        _battlefieldScriptReplaceBox.PlaceholderText = "替换为";
        _battlefieldScriptReplaceButton.Text = "替换文本";
        ConfigureToolbarButton(_battlefieldScriptReplaceButton, 88);
        _battlefieldScriptReplaceButton.Enabled = false;
        _saveBattlefieldScriptTextButton.Text = "保存选中文本";
        ConfigureToolbarButton(_saveBattlefieldScriptTextButton, 118);
        _saveBattlefieldScriptTextButton.Enabled = false;
        _saveBattlefieldScriptStructureButton.Text = "完整保存S剧本 Ctrl+S";
        ConfigureToolbarButton(_saveBattlefieldScriptStructureButton, 160);
        _saveBattlefieldScriptStructureButton.Enabled = false;
        _showBattlefieldVariablesButton.Text = "变量 Ctrl+L";
        ConfigureToolbarButton(_showBattlefieldVariablesButton, 104);
        _showBattlefieldVariablesButton.Enabled = false;
        ConfigureTextWrapLimitInput(_battlefieldScriptTextWrapLimitInput);
        ConfigureTextWrapLimitPanel(_battlefieldScriptTextWrapLimitPanel, _battlefieldScriptTextWrapLimitInput);
        AddToolbarRow(scriptToolbar, 0,
            _battlefieldScriptSearchBox,
            _battlefieldScriptSearchButton,
            _battlefieldScriptClearSearchButton,
            _showBattlefieldVariablesButton);
        AddToolbarRow(scriptToolbar, 1,
            CreateToolbarLabel("替换：", 0),
            _battlefieldScriptReplaceBox,
            _battlefieldScriptReplaceButton,
            _saveBattlefieldScriptTextButton,
            _saveBattlefieldScriptStructureButton);
        scriptLayout.Controls.Add(scriptToolbar, 0, 0);

        _battlefieldScriptTree.Dock = DockStyle.Fill;
        _battlefieldScriptTree.HideSelection = false;
        _battlefieldScriptTree.ShowNodeToolTips = true;
        _battlefieldScriptTree.FullRowSelect = true;
        _battlefieldScriptTree.CheckBoxes = true;
        EnableDoubleBuffering(_battlefieldScriptTree);
        RegisterSearchableTree(_battlefieldScriptTree);
        ConfigureLegacyStyleScriptTreeContextMenu(_battlefieldScriptTreeContextMenu, LegacyScriptEditorScope.Battlefield);
        _battlefieldScriptTree.ContextMenuStrip = _battlefieldScriptTreeContextMenu;

        var battlefieldScriptTabs = new TabControl { Dock = DockStyle.Fill };
        var battlefieldScriptTreePage = new TabPage("S剧本");
        var battlefieldScriptSearchPage = new TabPage("搜索结果");
        battlefieldScriptTreePage.Controls.Add(_battlefieldScriptTree);
        ConfigureLegacyScriptSearchResultGrid(_battlefieldScriptSearchResultGrid);
        battlefieldScriptSearchPage.Controls.Add(_battlefieldScriptSearchResultGrid);
        battlefieldScriptTabs.TabPages.Add(battlefieldScriptTreePage);
        battlefieldScriptTabs.TabPages.Add(battlefieldScriptSearchPage);

        scriptLayout.Controls.Add(battlefieldScriptTabs, 0, 1);
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
        _battlefieldInfoBox.Visible = false;
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
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = Padding.Empty
        };
        _battlefieldMapZoomLabel.Text = "缩放 100%";
        _battlefieldMapZoomLabel.AutoSize = true;
        _battlefieldMapZoomLabel.Padding = new Padding(8, 4, 0, 0);
        _battlefieldMapZoomResetButton.Text = "1:1";
        ConfigureToolbarButton(_battlefieldMapZoomResetButton, 56);
        _viewBattlefieldSingleFramesButton.Text = "查看全部S帧";
        ConfigureToolbarButton(_viewBattlefieldSingleFramesButton, 108);
        _battlefieldDeploymentPreviewFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _battlefieldDeploymentPreviewFilterCombo.Items.AddRange(new object[] { "初始部署", "全部部署记录", "隐藏与援军", "当前选中命令" });
        _battlefieldDeploymentPreviewFilterCombo.SelectedIndex = 0;
        ConfigureToolbarInput(_battlefieldDeploymentPreviewFilterCombo, 132, 120);
        _markBattlefieldCommand25Button.Text = "指定地点测试";
        ConfigureToolbarButton(_markBattlefieldCommand25Button, 118);
        _battlefieldMapHintLabel.Text = "地图：拖动左侧角色到格子；可右键选中已摆放单位并调整。";
        _battlefieldMapHintLabel.AutoSize = true;
        _battlefieldMapHintLabel.Padding = new Padding(0, 4, 0, 0);
        _battlefieldMapHintLabel.Visible = true;
        battlefieldMapHintPanel.Controls.AddRange(new Control[]
        {
            _battlefieldMapZoomLabel,
            _battlefieldMapZoomResetButton,
            _viewBattlefieldSingleFramesButton,
            _battlefieldDeploymentPreviewFilterCombo,
            _markBattlefieldCommand25Button,
            _battlefieldMapHintLabel
        });
        mapLayout.Controls.Add(battlefieldMapHintPanel, 0, 1);

        var controlOptionsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        controlOptionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        controlOptionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        controlOptionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        controlOptionsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var placementPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Margin = new Padding(0, 0, 0, 6)
        };
        placementPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        placementPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddPercentRows(placementPanel, 6);

        var factionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 2, 0, 2) };
        _battlefieldFactionAllyRadio.Text = "我军";
        _battlefieldFactionFriendRadio.Text = "友军";
        _battlefieldFactionEnemyRadio.Text = "敌军";
        _battlefieldFactionAllyRadio.AutoSize = true;
        _battlefieldFactionFriendRadio.AutoSize = true;
        _battlefieldFactionEnemyRadio.AutoSize = true;
        _battlefieldFactionAllyRadio.Checked = true;
        factionPanel.Controls.AddRange(new Control[] { _battlefieldFactionAllyRadio, _battlefieldFactionFriendRadio, _battlefieldFactionEnemyRadio });
        AddBattlefieldConsoleField(placementPanel, 0, "阵营", factionPanel);
        _battlefieldHiddenCheckBox.Text = "隐藏出场";
        _battlefieldHiddenCheckBox.AutoSize = true;
        AddBattlefieldConsoleField(placementPanel, 1, string.Empty, _battlefieldHiddenCheckBox);
        _battlefieldLevelOffsetInput.Minimum = -99;
        _battlefieldLevelOffsetInput.Maximum = 99;
        _battlefieldLevelOffsetInput.Width = 72;
        AddBattlefieldConsoleField(placementPanel, 2, "等级修正", _battlefieldLevelOffsetInput);
        _battlefieldLevelModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _battlefieldLevelModeCombo.Items.AddRange(new object[] { "初级", "中级", "高级" });
        _battlefieldLevelModeCombo.SelectedIndex = 0;
        AddBattlefieldConsoleField(placementPanel, 3, "等级阶段", _battlefieldLevelModeCombo);
        _battlefieldAiModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _battlefieldAiModeCombo.Items.AddRange(new object[] { "被动", "主动", "坚守", "攻击", "到点", "跟随", "逃离" });
        _battlefieldAiModeCombo.SelectedIndex = 0;
        AddBattlefieldConsoleField(placementPanel, 4, "AI", _battlefieldAiModeCombo);
        _battlefieldDirectionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _battlefieldDirectionCombo.Items.AddRange(new object[] { "上", "右", "下", "左" });
        _battlefieldDirectionCombo.SelectedIndex = 2;
        AddBattlefieldConsoleField(placementPanel, 5, "方向", _battlefieldDirectionCombo);
        controlOptionsPanel.Controls.Add(placementPanel, 0, 0);

        var equipmentPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Margin = new Padding(0, 0, 0, 6)
        };
        equipmentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        equipmentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddPercentRows(equipmentPanel, 6);
        AddBattlefieldConsoleField(equipmentPanel, 0, "武器", _battlefieldConsoleWeaponCombo);
        _battlefieldConsoleWeaponLevelInput.Minimum = 0;
        _battlefieldConsoleWeaponLevelInput.Maximum = 16;
        AddBattlefieldConsoleField(equipmentPanel, 1, "武器等级", _battlefieldConsoleWeaponLevelInput);
        AddBattlefieldConsoleField(equipmentPanel, 2, "防具", _battlefieldConsoleArmorCombo);
        _battlefieldConsoleArmorLevelInput.Minimum = 0;
        _battlefieldConsoleArmorLevelInput.Maximum = 16;
        AddBattlefieldConsoleField(equipmentPanel, 3, "防具等级", _battlefieldConsoleArmorLevelInput);
        AddBattlefieldConsoleField(equipmentPanel, 4, "辅助", _battlefieldConsoleAssistCombo);
        AddBattlefieldConsoleField(equipmentPanel, 5, "兵种", _battlefieldConsoleJobCombo);
        controlOptionsPanel.Controls.Add(equipmentPanel, 0, 1);

        _battlefieldConsoleAbilityGrid.Dock = DockStyle.Fill;
        _battlefieldConsoleAbilityGrid.Margin = new Padding(0, 0, 0, 6);
        _battlefieldConsoleAbilityGrid.MinimumSize = new Size(0, 96);
        _battlefieldConsoleAbilityGrid.AllowUserToAddRows = false;
        _battlefieldConsoleAbilityGrid.AllowUserToDeleteRows = false;
        _battlefieldConsoleAbilityGrid.RowHeadersVisible = false;
        _battlefieldConsoleAbilityGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _battlefieldConsoleAbilityGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _battlefieldConsoleAbilityGrid.MultiSelect = false;
        controlOptionsPanel.Controls.Add(_battlefieldConsoleAbilityGrid, 0, 2);

        var consoleInfoPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 6)
        };
        consoleInfoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        consoleInfoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _battlefieldDataDefaultsBox.Dock = DockStyle.Fill;
        _battlefieldDataDefaultsBox.Multiline = true;
        _battlefieldDataDefaultsBox.ReadOnly = true;
        _battlefieldDataDefaultsBox.ScrollBars = ScrollBars.Vertical;
        _battlefieldDataDefaultsBox.WordWrap = true;
        _battlefieldDataDefaultsBox.Text = "Data.e5 默认值：未读取。";
        _battlefieldConsoleStatusPreviewBox.Dock = DockStyle.Fill;
        _battlefieldConsoleStatusPreviewBox.Multiline = true;
        _battlefieldConsoleStatusPreviewBox.ReadOnly = true;
        _battlefieldConsoleStatusPreviewBox.ScrollBars = ScrollBars.Vertical;
        _battlefieldConsoleStatusPreviewBox.WordWrap = true;
        _battlefieldConsoleStatusPreviewBox.Text = "脚本覆盖摘要：未选中可管理单位。";
        consoleInfoPanel.Controls.Add(_battlefieldDataDefaultsBox, 0, 0);
        consoleInfoPanel.Controls.Add(_battlefieldConsoleStatusPreviewBox, 1, 0);
        consoleInfoPanel.Visible = false;

        var unsyncedDraftActionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty
        };
        _battlefieldRetryUnsyncedDraftButton.Text = "重试同步";
        _battlefieldRestoreScriptValuesButton.Text = "恢复脚本值";
        _battlefieldDetachUnsyncedDraftButton.Text = "保留草稿并解除绑定";
        ConfigureToolbarButton(_battlefieldRetryUnsyncedDraftButton, 92);
        ConfigureToolbarButton(_battlefieldRestoreScriptValuesButton, 104);
        ConfigureToolbarButton(_battlefieldDetachUnsyncedDraftButton, 156);
        _battlefieldRetryUnsyncedDraftButton.Visible = false;
        _battlefieldRestoreScriptValuesButton.Visible = false;
        _battlefieldDetachUnsyncedDraftButton.Visible = false;
        unsyncedDraftActionPanel.Controls.AddRange(new Control[]
        {
            _battlefieldRetryUnsyncedDraftButton,
            _battlefieldRestoreScriptValuesButton,
            _battlefieldDetachUnsyncedDraftButton
        });

        _battlefieldRemovePlacedUnitButton.Text = "移除选中";
        _battlefieldRemovePlacedUnitButton.AutoSize = true;
        _battlefieldRemovePlacedUnitButton.Dock = DockStyle.Fill;
        _battlefieldClearPlacedUnitsButton.Text = "清空摆放";
        _battlefieldClearPlacedUnitsButton.AutoSize = true;
        _battlefieldClearPlacedUnitsButton.Dock = DockStyle.Fill;
        var actionPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        actionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        actionPanel.Controls.Add(_battlefieldRemovePlacedUnitButton, 0, 1);
        actionPanel.Controls.Add(unsyncedDraftActionPanel, 0, 2);
        _battlefieldClearPlacedUnitsButton.Visible = false;
        controlOptionsPanel.Controls.Add(actionPanel, 0, 3);

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
        var unitToolbar = CreateToolbarStack(2);
        _battlefieldUnitCategoryFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_battlefieldUnitCategoryFilterCombo, 140, 120);
        ConfigureToolbarInput(_battlefieldUnitFilterBox, 120, 100);
        _battlefieldUnitFilterBox.PlaceholderText = "候选筛选";
        _filterBattlefieldUnitsButton.Text = "筛选";
        ConfigureToolbarButton(_filterBattlefieldUnitsButton, 72);
        _clearBattlefieldUnitFilterButton.Text = "全部";
        ConfigureToolbarButton(_clearBattlefieldUnitFilterButton, 72);
        _markBattlefieldUnitReviewedButton.Text = "已核对";
        ConfigureToolbarButton(_markBattlefieldUnitReviewedButton, 72);
        _markBattlefieldUnitNeedsChangeButton.Text = "需改";
        ConfigureToolbarButton(_markBattlefieldUnitNeedsChangeButton, 72);
        _jumpBattlefieldUnitScriptButton.Text = "跳命令";
        ConfigureToolbarButton(_jumpBattlefieldUnitScriptButton, 72);
        AddToolbarRow(unitToolbar, 0,
            _battlefieldUnitCategoryFilterCombo,
            _battlefieldUnitFilterBox,
            _filterBattlefieldUnitsButton,
            _clearBattlefieldUnitFilterButton);
        AddToolbarRow(unitToolbar, 1,
            _markBattlefieldUnitReviewedButton,
            _markBattlefieldUnitNeedsChangeButton,
            _jumpBattlefieldUnitScriptButton);
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
        commandPage.Controls.Add(CreateSearchableGridPanel(_battlefieldCommandGrid, "命令/坐标/人物"));
        unitTabs.TabPages.Add(unitPage);
        unitTabs.TabPages.Add(commandPage);
        unitTabs.Visible = false;
        _battlefieldScriptDetailBox.Dock = DockStyle.Fill;
        _battlefieldScriptDetailBox.Multiline = true;
        _battlefieldScriptDetailBox.ReadOnly = true;
        _battlefieldScriptDetailBox.ScrollBars = ScrollBars.Vertical;
        _battlefieldScriptDetailBox.WordWrap = true;
        _battlefieldScriptDetailBox.Text = "选择左侧 S 剧本命令后显示参数和修改预览。";

        _battlefieldScriptParameterGrid.Dock = DockStyle.Fill;
        _battlefieldScriptParameterGrid.ReadOnly = true;
        _battlefieldScriptParameterGrid.AllowUserToAddRows = false;
        _battlefieldScriptParameterGrid.AllowUserToDeleteRows = false;
        _battlefieldScriptParameterGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _battlefieldScriptParameterGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _battlefieldScriptParameterGrid.MultiSelect = false;
        _battlefieldScriptParameterGrid.RowHeadersVisible = false;
        _battlefieldScriptParameterGrid.BorderStyle = BorderStyle.FixedSingle;

        _battlefieldScriptTextBox.Dock = DockStyle.Fill;
        _battlefieldScriptTextBox.Multiline = true;
        _battlefieldScriptTextBox.ScrollBars = ScrollBars.Vertical;
        _battlefieldScriptTextBox.WordWrap = true;
        _battlefieldScriptTextBox.AcceptsReturn = true;
        _battlefieldScriptTextBox.AcceptsTab = true;
        _battlefieldScriptTextCapacityLabel.AutoSize = true;
        _battlefieldScriptTextCapacityLabel.Text = "文本容量：未选择";
        _battlefieldScriptTextCapacityLabel.Padding = new Padding(0, 4, 0, 0);

        _battlefieldScriptImagePreviewBox.Dock = DockStyle.Fill;
        _battlefieldScriptImagePreviewBox.BackColor = Color.Black;
        _battlefieldScriptImagePreviewBox.BorderStyle = BorderStyle.FixedSingle;
        _battlefieldScriptImagePreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _battlefieldScriptImagePreviewBox.Margin = Padding.Empty;
        _battlefieldScriptImagePreviewInfoBox.Dock = DockStyle.Fill;
        _battlefieldScriptImagePreviewInfoBox.Multiline = true;
        _battlefieldScriptImagePreviewInfoBox.ReadOnly = true;
        _battlefieldScriptImagePreviewInfoBox.ScrollBars = ScrollBars.Vertical;
        _battlefieldScriptImagePreviewInfoBox.WordWrap = true;
        _battlefieldScriptImagePreviewInfoBox.BorderStyle = BorderStyle.FixedSingle;
        _battlefieldScriptImagePreviewInfoBox.Text = "无图片预览";

        var battlefieldScriptParameterEditToolbar = CreateToolbarRow();
        _battlefieldScriptParameterValueBox.Width = 160;
        _battlefieldScriptParameterValueBox.MinimumSize = new Size(120, 0);
        _battlefieldScriptParameterValueBox.Margin = new Padding(3);
        _applyBattlefieldScriptParameterButton.Text = "应用修改";
        ConfigureToolbarButton(_applyBattlefieldScriptParameterButton, 88);
        _applyBattlefieldScriptParameterButton.Enabled = false;
        _resetBattlefieldInlineDialogButton.Text = "重置";
        ConfigureToolbarButton(_resetBattlefieldInlineDialogButton, 72);
        _resetBattlefieldInlineDialogButton.Enabled = false;
        _editBattlefieldScriptParametersButton.Text = "弹窗修改";
        ConfigureToolbarButton(_editBattlefieldScriptParametersButton, 88);
        _editBattlefieldScriptParametersButton.Enabled = false;
        battlefieldScriptParameterEditToolbar.Controls.AddRange(new Control[]
        {
            _applyBattlefieldScriptParameterButton,
            _resetBattlefieldInlineDialogButton,
            _battlefieldScriptTextWrapLimitPanel
        });

        var battlefieldScriptTextPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        battlefieldScriptTextPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        battlefieldScriptTextPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        battlefieldScriptTextPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        battlefieldScriptTextPanel.Controls.Add(_battlefieldScriptTextBox, 0, 0);
        battlefieldScriptTextPanel.Controls.Add(_battlefieldScriptTextCapacityLabel, 0, 1);

        var battlefieldScriptParameterPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        battlefieldScriptParameterPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        battlefieldScriptParameterPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        battlefieldScriptParameterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _battlefieldInlineDialogHost.Margin = Padding.Empty;
        battlefieldScriptParameterPanel.Controls.Add(_battlefieldInlineDialogHost, 0, 0);
        battlefieldScriptParameterPanel.Controls.Add(battlefieldScriptParameterEditToolbar, 0, 1);

        var hiddenBattlefieldScriptBindingsPanel = new Panel
        {
            Visible = false,
            Size = Size.Empty
        };
        hiddenBattlefieldScriptBindingsPanel.Controls.AddRange(new Control[]
        {
            _battlefieldScriptDetailBox,
            _battlefieldScriptParameterGrid,
            _battlefieldScriptParameterValueBox,
            _editBattlefieldScriptParametersButton,
            battlefieldScriptTextPanel,
            _battlefieldScriptImagePreviewInfoBox
        });
        page.Controls.Add(hiddenBattlefieldScriptBindingsPanel);

        _battlefieldScriptPreviewPanel.Dock = DockStyle.Fill;
        _battlefieldScriptPreviewPanel.Controls.Add(battlefieldScriptParameterPanel);
        _battlefieldConsolePreviewPanel.Dock = DockStyle.Fill;
        _battlefieldConsolePreviewPanel.Controls.Add(controlOptionsPanel);

        var battlefieldPreviewModeHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        battlefieldPreviewModeHost.Controls.Add(_battlefieldConsolePreviewPanel);
        battlefieldPreviewModeHost.Controls.Add(_battlefieldScriptPreviewPanel);

        var battlefieldPreviewPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        battlefieldPreviewPanel.Controls.Add(battlefieldPreviewModeHost);

        var mapControlSplit = CreateResizableSplit("BuildBattlefieldEditorPage.MapControl", Orientation.Vertical, 620, 320, 320);
        AddCollapsibleSplitPanel(mapControlSplit, 1, "战场地图", mapLayout, "BuildBattlefieldEditorPage.MapControl.Map");
        AddCollapsibleSplitPanel(mapControlSplit, 2, "预览/控制台", battlefieldPreviewPanel, "BuildBattlefieldEditorPage.MapControl.Console");
        var unitMapControlSplit = CreateResizableSplit("BuildBattlefieldEditorPage.UnitMapControl", Orientation.Vertical, 190, 120, 360);
        unitMapControlSplit.Panel1.Controls.Add(unitPanel);
        unitMapControlSplit.Panel2.Controls.Add(mapControlSplit);

        mainSplit.Panel2.Controls.Add(unitMapControlSplit);
        layout.Controls.Add(mainSplit, 0, 1);

        return page;
    }

    private static void AddBattlefieldConsoleField(TableLayoutPanel panel, int row, string label, Control editor)
    {
        while (panel.RowStyles.Count <= row)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        editor.Dock = DockStyle.Fill;
        editor.Margin = new Padding(0, 2, 0, 2);
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 5, 6, 0)
        }, 0, row);
        panel.Controls.Add(editor, 1, row);
    }

    private static void AddPercentRows(TableLayoutPanel panel, int rowCount)
    {
        panel.RowStyles.Clear();
        for (var i = 0; i < rowCount; i++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rowCount));
        }
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

        var toolbar = CreateToolbarStack(2);
        _loadRSceneButton.Text = "读取R剧情";
        ConfigureToolbarButton(_loadRSceneButton, 88);
        _rSceneScenarioCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_rSceneScenarioCombo, 260, 180);
        _saveRSceneDraftButton.Text = "保存场景草稿";
        ConfigureToolbarButton(_saveRSceneDraftButton, 118);
        _saveRSceneDraftButton.Enabled = false;
        _saveRSceneScriptStructureButton.Text = "完整保存R剧本 Ctrl+S";
        ConfigureToolbarButton(_saveRSceneScriptStructureButton, 160);
        _saveRSceneScriptStructureButton.Enabled = false;
        _showRSceneVariablesButton.Text = "变量 Ctrl+L";
        ConfigureToolbarButton(_showRSceneVariablesButton, 104);
        _showRSceneVariablesButton.Enabled = false;
        _jumpRSceneScriptButton.Text = "跳到剧本编辑";
        ConfigureToolbarButton(_jumpRSceneScriptButton, 118);
        _jumpRSceneScriptButton.Enabled = false;
        ConfigureToolbarInput(_rSceneScriptSearchBox, 160, 120);
        _rSceneScriptSearchBox.PlaceholderText = "搜索R剧本";
        _rSceneScriptSearchButton.Text = "搜索";
        ConfigureToolbarButton(_rSceneScriptSearchButton, 72);
        _rSceneScriptClearSearchButton.Text = "清除";
        ConfigureToolbarButton(_rSceneScriptClearSearchButton, 72);
        ConfigureToolbarInput(_rSceneScriptReplaceBox, 150, 110);
        _rSceneScriptReplaceBox.PlaceholderText = "替换为";
        _rSceneScriptReplaceButton.Text = "替换文本";
        ConfigureToolbarButton(_rSceneScriptReplaceButton, 88);
        _rSceneScriptReplaceButton.Enabled = false;
        AddToolbarRow(toolbar, 0,
            _loadRSceneButton,
            CreateToolbarLabel("R剧情："),
            _rSceneScenarioCombo,
            _saveRSceneDraftButton,
            CreateToolbarLabel("搜索："),
            _rSceneScriptSearchBox,
            _rSceneScriptSearchButton,
            _rSceneScriptClearSearchButton);
        AddToolbarRow(toolbar, 1,
            CreateToolbarLabel("替换：", 0),
            _rSceneScriptReplaceBox,
            _rSceneScriptReplaceButton,
            _saveRSceneScriptStructureButton,
            _showRSceneVariablesButton,
            _jumpRSceneScriptButton);
        layout.Controls.Add(toolbar, 0, 0);

        var mainSplit = CreateResizableSplit("BuildRSceneEditorPage.ScriptScene", Orientation.Vertical, 520, 300, 420);

        var leftTabs = new TabControl { Dock = DockStyle.Fill };
        var scriptPage = new TabPage("R剧本");
        var searchPage = new TabPage("搜索结果");
        _rSceneScriptTree.Dock = DockStyle.Fill;
        _rSceneScriptTree.HideSelection = false;
        _rSceneScriptTree.ShowNodeToolTips = true;
        _rSceneScriptTree.FullRowSelect = true;
        _rSceneScriptTree.CheckBoxes = true;
        EnableDoubleBuffering(_rSceneScriptTree);
        RegisterSearchableTree(_rSceneScriptTree);
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
        ConfigureLegacyScriptSearchResultGrid(_rSceneScriptSearchResultGrid);
        searchPage.Controls.Add(_rSceneScriptSearchResultGrid);

        var commandPage = new TabPage("R场景候选集");
        _rSceneCommandGrid.Dock = DockStyle.Fill;
        _rSceneCommandGrid.ReadOnly = true;
        _rSceneCommandGrid.AllowUserToAddRows = false;
        _rSceneCommandGrid.AllowUserToDeleteRows = false;
        _rSceneCommandGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _rSceneCommandGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        commandPage.Controls.Add(CreateSearchableGridPanel(_rSceneCommandGrid, "场景/命令/摘要"));
        leftTabs.TabPages.Add(scriptPage);
        leftTabs.TabPages.Add(searchPage);
        leftTabs.TabPages.Add(commandPage);
        leftTabs.Selected += (_, e) =>
        {
            if (ReferenceEquals(e.TabPage, commandPage))
            {
                ScheduleSelectedRSceneCandidateScroll();
            }
        };
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
        var canvasToolbar = CreateToolbarStack(2);
        _rSceneBackgroundCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_rSceneBackgroundCombo, 220, 150);
        _rSceneGridSizeInput.Minimum = RSceneTileWidth;
        _rSceneGridSizeInput.Maximum = RSceneTileWidth;
        _rSceneGridSizeInput.Value = 16;
        _rSceneGridSizeInput.Width = 64;
        _rSceneShowGridCheckBox.Text = "网格";
        ConfigureToolbarCheckBox(_rSceneShowGridCheckBox);
        _rSceneShowGridCheckBox.Checked = true;
        _rSceneDialoguePreviewCheckBox.Text = "对白预览";
        ConfigureToolbarCheckBox(_rSceneDialoguePreviewCheckBox);
        _rSceneDialoguePreviewCheckBox.Checked = true;
        ConfigureTextWrapLimitInput(_rSceneTextWrapLimitInput);
        ConfigureTextWrapLimitPanel(_rSceneTextWrapLimitPanel, _rSceneTextWrapLimitInput);
        _rSceneZoomLabel.Text = "缩放 100%";
        _rSceneZoomLabel.AutoSize = true;
        _rSceneZoomLabel.Padding = new Padding(8, 5, 0, 0);
        _rSceneZoomLabel.Margin = new Padding(3);
        _rSceneZoomResetButton.Text = "1:1";
        ConfigureToolbarButton(_rSceneZoomResetButton, 56);
        _viewRSceneSingleFramesButton.Text = "查看全部R帧";
        ConfigureToolbarButton(_viewRSceneSingleFramesButton, 108);
        _rScenePreviewLockButton.Text = "锁定预览";
        ConfigureToolbarButton(_rScenePreviewLockButton, 88);
        _rScenePreviewLockButton.Enabled = false;
        _rSceneCanvasHintLabel.Text = "背景：读取 R 剧情后选择 Mmap.e5 图号；拖动角色到背景格子。";
        _rSceneCanvasHintLabel.AutoSize = true;
        _rSceneCanvasHintLabel.Padding = new Padding(0, 5, 0, 0);
        _rSceneCanvasHintLabel.Margin = new Padding(3);
        AddToolbarRow(canvasToolbar, 0,
            CreateToolbarLabel("背景", 0),
            _rSceneBackgroundCombo,
            CreateToolbarLabel("坐标格"),
            CreateToolbarLabel("斜菱形 16x8", 0),
            _rSceneShowGridCheckBox,
            _rSceneDialoguePreviewCheckBox,
            _rSceneZoomLabel,
            _rSceneZoomResetButton,
            _viewRSceneSingleFramesButton,
            _rScenePreviewLockButton);
        AddToolbarRow(canvasToolbar, 1, _rSceneCanvasHintLabel);
        canvasLayout.Controls.Add(canvasToolbar, 0, 0);
        canvasLayout.Controls.Add(_rSceneCanvasScrollPanel, 0, 1);

        var rSceneParameterEditToolbar = CreateToolbarRow();
        _applyRSceneInlineDialogButton.Text = "应用修改";
        ConfigureToolbarButton(_applyRSceneInlineDialogButton, 88);
        _applyRSceneInlineDialogButton.Enabled = false;
        _resetRSceneInlineDialogButton.Text = "重置";
        ConfigureToolbarButton(_resetRSceneInlineDialogButton, 72);
        _resetRSceneInlineDialogButton.Enabled = false;
        rSceneParameterEditToolbar.Controls.AddRange(new Control[]
        {
            _applyRSceneInlineDialogButton,
            _resetRSceneInlineDialogButton,
            _rSceneTextWrapLimitPanel
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
        var stancePanel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
        stancePanel.Controls.AddRange(new Control[]
        {
            new Label { Text = "动作帧", AutoSize = true, Padding = new Padding(0, 5, 0, 0) },
            _rSceneStanceInput
        });
        controlPanel.Controls.Add(stancePanel, 0, 2);
        _rScenePlaybackButton.Text = "开始";
        ConfigureToolbarButton(_rScenePlaybackButton, 72);
        _rScenePlaybackButton.Enabled = false;
        _rScenePlaybackDelayInput.Minimum = 50;
        _rScenePlaybackDelayInput.Maximum = 10000;
        _rScenePlaybackDelayInput.Increment = 50;
        _rScenePlaybackDelayInput.Value = 500;
        _rScenePlaybackDelayInput.Width = 86;
        var playbackPanel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
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

        var toolbar = CreateToolbarStack(2);
        _loadScriptButton.Text = "读取剧本列表";
        ConfigureToolbarButton(_loadScriptButton, 118);
        _scriptScenarioCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_scriptScenarioCombo, 260, 180);
        ConfigureToolbarInput(_scriptSearchBox, 180, 140);
        _scriptSearchBox.PlaceholderText = "搜索命令/文本";
        _scriptSearchButton.Text = "搜索";
        ConfigureToolbarButton(_scriptSearchButton, 72);
        _scriptClearSearchButton.Text = "清除";
        ConfigureToolbarButton(_scriptClearSearchButton, 72);
        ConfigureToolbarInput(_scriptReplaceBox, 160, 120);
        _scriptReplaceBox.PlaceholderText = "替换为";
        _scriptReplaceButton.Text = "替换命中文本";
        ConfigureToolbarButton(_scriptReplaceButton, 112);
        _scriptReplaceButton.Enabled = false;
        _showScriptVariablesButton.Text = "变量 Ctrl+L";
        ConfigureToolbarButton(_showScriptVariablesButton, 104);
        _showScriptVariablesButton.Enabled = false;
        _locateScriptCommandButton.Text = "定位";
        ConfigureToolbarButton(_locateScriptCommandButton, 72);
        _locateScriptCommandButton.Enabled = false;
        _copyScriptCommandButton.Text = "复制";
        ConfigureToolbarButton(_copyScriptCommandButton, 72);
        _copyScriptCommandButton.Enabled = false;
        _cutScriptCommandButton.Text = "剪切";
        ConfigureToolbarButton(_cutScriptCommandButton, 72);
        _cutScriptCommandButton.Enabled = false;
        _previewPasteScriptCommandButton.Text = "粘贴预览";
        ConfigureToolbarButton(_previewPasteScriptCommandButton, 88);
        _previewPasteScriptCommandButton.Enabled = false;
        _scriptNewCommandCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_scriptNewCommandCombo, 220, 160);
        _scriptNewCommandCombo.Enabled = false;
        _appendScriptCommandToSectionButton.Text = "正文末尾";
        ConfigureToolbarButton(_appendScriptCommandToSectionButton, 88);
        _appendScriptCommandToSectionButton.Enabled = false;
        _insertScriptCommandBeforeButton.Text = "前插";
        ConfigureToolbarButton(_insertScriptCommandBeforeButton, 72);
        _insertScriptCommandBeforeButton.Enabled = false;
        _insertScriptCommandAfterButton.Text = "后插";
        ConfigureToolbarButton(_insertScriptCommandAfterButton, 72);
        _insertScriptCommandAfterButton.Enabled = false;
        _appendScriptCommandToChildBlockButton.Text = "子块";
        ConfigureToolbarButton(_appendScriptCommandToChildBlockButton, 72);
        _appendScriptCommandToChildBlockButton.Enabled = false;
        _deleteScriptCommandButton.Text = "删除";
        ConfigureToolbarButton(_deleteScriptCommandButton, 72);
        _deleteScriptCommandButton.Enabled = false;
        _pasteScriptCommandBeforeButton.Text = "粘到前面";
        ConfigureToolbarButton(_pasteScriptCommandBeforeButton, 88);
        _pasteScriptCommandBeforeButton.Enabled = false;
        _pasteScriptCommandAfterButton.Text = "粘到后面";
        ConfigureToolbarButton(_pasteScriptCommandAfterButton, 88);
        _pasteScriptCommandAfterButton.Enabled = false;
        _moveScriptCommandUpButton.Text = "上移";
        ConfigureToolbarButton(_moveScriptCommandUpButton, 72);
        _moveScriptCommandUpButton.Enabled = false;
        _moveScriptCommandDownButton.Text = "下移";
        ConfigureToolbarButton(_moveScriptCommandDownButton, 72);
        _moveScriptCommandDownButton.Enabled = false;
        _saveScriptTextButton.Text = "保存文本";
        ConfigureToolbarButton(_saveScriptTextButton, 88);
        _saveScriptTextButton.Enabled = false;
        _saveScriptStructureButton.Text = "完整保存剧本 Ctrl+S";
        ConfigureToolbarButton(_saveScriptStructureButton, 150);
        _saveScriptStructureButton.Enabled = false;
        _jumpScriptBattlefieldButton.Text = "战场";
        ConfigureToolbarButton(_jumpScriptBattlefieldButton, 72);
        _jumpScriptBattlefieldButton.Enabled = false;
        AddToolbarRow(toolbar, 0,
            _loadScriptButton,
            CreateToolbarLabel("剧本："),
            _scriptScenarioCombo,
            _scriptSearchBox,
            _scriptSearchButton,
            _scriptClearSearchButton,
            _showScriptVariablesButton,
            _locateScriptCommandButton,
            _saveScriptStructureButton,
            _jumpScriptBattlefieldButton);
        AddToolbarRow(toolbar, 1,
            CreateToolbarLabel("替换：", 0),
            _scriptReplaceBox,
            _scriptReplaceButton,
            _copyScriptCommandButton,
            _cutScriptCommandButton,
            _deleteScriptCommandButton,
            _previewPasteScriptCommandButton,
            _pasteScriptCommandBeforeButton,
            _pasteScriptCommandAfterButton,
            _moveScriptCommandUpButton,
            _moveScriptCommandDownButton);
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
        RegisterSearchableTree(_scriptTree);
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
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = Padding.Empty
        };
        structureToolbar.Controls.AddRange(new Control[]
        {
            new Label { Text = "新增：", AutoSize = true, Padding = new Padding(0, 7, 0, 0) },
            _scriptNewCommandCombo,
            _appendScriptCommandToSectionButton,
            _insertScriptCommandBeforeButton,
            _insertScriptCommandAfterButton,
            _appendScriptCommandToChildBlockButton
        });
        structureToolbar.Visible = false;
        treePanel.Controls.Add(structureToolbar, 0, 1);
        _scriptLowerLeftTabs.Dock = DockStyle.Fill;
        var scriptTreePage = new TabPage("事件树");
        var scriptSearchPage = new TabPage("搜索结果");
        scriptTreePage.Controls.Add(_scriptTree);
        scriptSearchPage.Controls.Add(_scriptSearchResultGrid);
        _scriptLowerLeftTabs.TabPages.Clear();
        _scriptLowerLeftTabs.TabPages.Add(scriptTreePage);
        _scriptLowerLeftTabs.TabPages.Add(scriptSearchPage);
        treePanel.Controls.Add(_scriptLowerLeftTabs, 0, 2);
        AddCollapsibleSplitPanel(mainSplit, 1, "事件树", treePanel, "BuildScriptEditorPage.TreeDetail.Tree");

        ConfigureHiddenScriptGrids();
        var hiddenBindingsPanel = new Panel
        {
            Visible = false,
            Size = Size.Empty
        };
        hiddenBindingsPanel.Controls.AddRange(new Control[] { _scriptCommandGrid, _scriptTextGrid });
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

        var parameterEditToolbar = CreateToolbarRow();
        _applyScriptInlineDialogButton.Text = "应用修改";
        ConfigureToolbarButton(_applyScriptInlineDialogButton, 88);
        _applyScriptInlineDialogButton.Enabled = false;
        _resetScriptInlineDialogButton.Text = "重置";
        ConfigureToolbarButton(_resetScriptInlineDialogButton, 72);
        _resetScriptInlineDialogButton.Enabled = false;
        ConfigureTextWrapLimitInput(_scriptTextWrapLimitInput);
        ConfigureTextWrapLimitPanel(_scriptTextWrapLimitPanel, _scriptTextWrapLimitInput);
        parameterEditToolbar.Controls.AddRange(new Control[]
        {
            _applyScriptInlineDialogButton,
            _resetScriptInlineDialogButton,
            _scriptTextWrapLimitPanel
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
