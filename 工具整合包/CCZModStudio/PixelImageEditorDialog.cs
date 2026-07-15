using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace CCZModStudio;

internal sealed class PixelImageEditorDialog : Form
{
    private enum ToolKind
    {
        Pencil,
        Eraser,
        Picker,
        Fill,
        Line,
        Rectangle
    }

    private enum InteractionState
    {
        Normal,
        ConfiguringColorReplacement,
        PickingColorReplacement
    }

    private EditableImageDocument _document;
    private readonly PixelEditResourceGroup? _group;
    private readonly PixelEditResourcePage _singlePage;
    private PixelCanvas _canvas = null!;
    private readonly Panel _scroll = new();
    private readonly TabControl _documentTabs = new();
    private readonly PictureBox _colorPreview = new();
    private readonly TrackBar _zoomBar = new();
    private readonly Label _statusLabel = new();
    private readonly CheckBox _gridCheckBox = new();
    private readonly ToolTip _toolTip = new();
    private readonly List<Control> _disabledDuringColorReplacement = new();
    private readonly Stack<PixelEditHistoryEntry> _undo = new();
    private readonly Stack<PixelEditHistoryEntry> _redo = new();
    private readonly PixelColorReplacementService _colorReplacementService = new();
    private ToolKind _tool = ToolKind.Pencil;
    private Color _primaryColor = Color.Black;
    private PixelColorReplaceDialog? _colorReplaceDialog;
    private ColorReplacementPickRequest? _pendingColorPick;
    private InteractionState _interactionState = InteractionState.Normal;
    private bool _painting;
    private Point _startPixel;
    private Point _lastPixel;
    private readonly bool _singleFrameMode;

    public PixelImageEditorDialog(EditableImageDocument document)
        : this(document, singleFrameMode: false)
    {
    }

    internal PixelImageEditorDialog(EditableImageDocument document, bool singleFrameMode)
    {
        _singleFrameMode = singleFrameMode;
        _document = document;
        _singlePage = new PixelEditResourcePage
        {
            Key = BuildDocumentKey(document),
            Label = document.Target.DisplayName,
            Document = document
        };
        InitializeDialog();
    }

    public PixelImageEditorDialog(PixelEditResourceGroup group)
    {
        _group = group;
        _document = group.ActivePage.Document;
        _singlePage = group.ActivePage;
        InitializeDialog();
    }

    private void InitializeDialog()
    {
        Text = "像素编辑 - " + _document.Target.DisplayName;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        Width = 1120;
        Height = 780;
        KeyPreview = true;

        _canvas = new PixelCanvas(_document.Bitmap)
        {
            Dock = DockStyle.None,
            Location = Point.Empty,
            FrameWidth = _document.FrameWidth,
            FrameHeight = _document.FrameHeight,
            ShowGrid = true
        };
        _canvas.PixelMouseDown += CanvasPixelMouseDown;
        _canvas.PixelMouseMove += CanvasPixelMouseMove;
        _canvas.PixelMouseUp += CanvasPixelMouseUp;
        _canvas.PixelHover += (_, point) => UpdateStatus(point);

        Controls.Add(BuildRoot());
        BuildDocumentTabs();
        UpdateColorPreview();
        UpdateStatus(null);
    }

    public Bitmap EditedBitmap => _document.Bitmap;

    internal PixelColorReplaceDialog? ColorReplaceDialogForSmoke => _colorReplaceDialog;
    internal string InteractionStateForSmoke => _interactionState.ToString();

    private Control BuildRoot()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _documentTabs.Dock = DockStyle.Top;
        _documentTabs.Height = 34;
        _documentTabs.Visible = _group?.ShowTabs == true;
        _documentTabs.SelectedIndexChanged += (_, _) => SwitchDocument(_documentTabs.SelectedIndex);
        root.Controls.Add(_documentTabs, 0, 0);
        root.Controls.Add(BuildToolbar(), 0, 1);

        _scroll.Dock = DockStyle.Fill;
        _scroll.AutoScroll = true;
        _scroll.BackColor = Color.FromArgb(36, 36, 40);
        _scroll.Controls.Add(_canvas);
        root.Controls.Add(_scroll, 0, 2);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 3,
            AutoSize = true
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(2, 8, 12, 0);
        bottom.Controls.Add(_statusLabel, 0, 0);
        var ok = new Button { Text = "保存写回", AutoSize = true, MinimumSize = new Size(96, 32), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", AutoSize = true, MinimumSize = new Size(80, 32), DialogResult = DialogResult.Cancel };
        _disabledDuringColorReplacement.Add(ok);
        bottom.Controls.Add(ok, 1, 0);
        bottom.Controls.Add(cancel, 2, 0);
        AcceptButton = ok;
        CancelButton = cancel;
        root.Controls.Add(bottom, 0, 3);
        return root;
    }

    private Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        bar.Controls.Add(MakeToolButton("铅笔", ToolKind.Pencil));
        bar.Controls.Add(MakeToolButton("橡皮", ToolKind.Eraser));
        bar.Controls.Add(MakeToolButton("取色", ToolKind.Picker));
        bar.Controls.Add(MakeToolButton("填充", ToolKind.Fill));
        bar.Controls.Add(MakeToolButton("直线", ToolKind.Line));
        bar.Controls.Add(MakeToolButton("矩形", ToolKind.Rectangle));
        if (!_document.RestrictToPalette)
        {
            bar.Controls.Add(MakeButton("颜色", PickColor));
        }
        _colorPreview.Width = 32;
        _colorPreview.Height = 28;
        _colorPreview.BorderStyle = BorderStyle.FixedSingle;
        _colorPreview.Margin = new Padding(2, 3, 10, 3);
        bar.Controls.Add(_colorPreview);

        foreach (var color in BuildPaletteColors())
        {
            var swatch = new Button
            {
                BackColor = color,
                Width = 24,
                Height = 24,
                Margin = new Padding(1, 5, 1, 3),
                FlatStyle = FlatStyle.Flat
            };
            swatch.Click += (_, _) =>
            {
                _primaryColor = color;
                UpdateColorPreview();
            };
            _disabledDuringColorReplacement.Add(swatch);
            bar.Controls.Add(swatch);
        }

        bar.Controls.Add(MakeButton("换色", ReplaceColors, "整组并行换色，最多五组"));
        bar.Controls.Add(MakeButton("导入PNG", ImportPng, "Ctrl+I"));
        bar.Controls.Add(MakeButton("导出PNG", ExportPng, "Ctrl+E"));
        bar.Controls.Add(MakeButton("撤销", Undo, "Ctrl+Z"));
        bar.Controls.Add(MakeButton("重做", Redo, "Ctrl+Y / Ctrl+Shift+Z"));
        if (_singleFrameMode)
        {
            bar.Controls.Add(new Label { Text = "对齐：", AutoSize = true, Padding = new Padding(10, 8, 0, 0) });
            bar.Controls.Add(MakeButton("左移", () => ShiftSingleFrame(RsFrameShiftDirection.Left), "Alt+左方向键"));
            bar.Controls.Add(MakeButton("右移", () => ShiftSingleFrame(RsFrameShiftDirection.Right), "Alt+右方向键"));
            bar.Controls.Add(MakeButton("上移", () => ShiftSingleFrame(RsFrameShiftDirection.Up), "Alt+上方向键"));
            bar.Controls.Add(MakeButton("下移", () => ShiftSingleFrame(RsFrameShiftDirection.Down), "Alt+下方向键"));
        }
        _gridCheckBox.Text = "网格";
        _gridCheckBox.Checked = true;
        _gridCheckBox.AutoSize = true;
        _gridCheckBox.Padding = new Padding(10, 7, 0, 0);
        _toolTip.SetToolTip(_gridCheckBox, "Ctrl+H / G");
        _gridCheckBox.CheckedChanged += (_, _) =>
        {
            _canvas.ShowGrid = _gridCheckBox.Checked;
            _canvas.Invalidate();
            UpdateStatus(null);
        };
        bar.Controls.Add(_gridCheckBox);
        bar.Controls.Add(new Label { Text = "缩放", AutoSize = true, Padding = new Padding(10, 8, 0, 0) });
        _zoomBar.Minimum = 2;
        _zoomBar.Maximum = 32;
        _zoomBar.Value = Math.Clamp(_canvas.Zoom, _zoomBar.Minimum, _zoomBar.Maximum);
        _zoomBar.Width = 140;
        _zoomBar.AutoSize = false;
        _zoomBar.Height = 28;
        _zoomBar.TickFrequency = 4;
        _zoomBar.ValueChanged += (_, _) =>
        {
            _canvas.Zoom = _zoomBar.Value;
            UpdateStatus(null);
        };
        bar.Controls.Add(_zoomBar);

        return bar;
    }

    private Button MakeToolButton(string text, ToolKind tool)
    {
        var button = MakeButton(text, () =>
        {
            SetTool(tool);
        }, GetToolShortcutText(tool));
        _toolTip.SetToolTip(button, $"{GetToolDisplayName(tool)} {GetToolShortcutText(tool)}");
        return button;
    }

    private Button MakeButton(string text, Action action, string tooltip = "", bool disableDuringColorReplacement = true)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(64, 30), Margin = new Padding(2) };
        button.Click += (_, _) => action();
        if (!string.IsNullOrWhiteSpace(tooltip))
        {
            _toolTip.SetToolTip(button, tooltip);
        }
        if (disableDuringColorReplacement)
        {
            _disabledDuringColorReplacement.Add(button);
        }
        return button;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var keyCode = keyData & Keys.KeyCode;
        var modifiers = keyData & (Keys.Control | Keys.Shift | Keys.Alt);

        if (_interactionState != InteractionState.Normal)
        {
            if (modifiers == Keys.None && keyCode == Keys.Escape)
            {
                if (_interactionState == InteractionState.PickingColorReplacement)
                {
                    CancelColorReplacementPick();
                }
                else
                {
                    _colorReplaceDialog?.Show();
                    _colorReplaceDialog?.Activate();
                    UpdateStatus(null);
                }

                return true;
            }

            if (keyData == (Keys.Control | Keys.H))
            {
                ToggleGrid();
                return true;
            }

            if (keyData == (Keys.Shift | Keys.Oemplus))
            {
                ChangeZoom(1);
                return true;
            }

            if (modifiers == Keys.None)
            {
                switch (keyCode)
                {
                    case Keys.G:
                        ToggleGrid();
                        return true;
                    case Keys.Oemplus:
                    case Keys.Add:
                        ChangeZoom(1);
                        return true;
                    case Keys.OemMinus:
                    case Keys.Subtract:
                        ChangeZoom(-1);
                        return true;
                    case Keys.D0:
                    case Keys.NumPad0:
                        ResetZoom();
                        return true;
                    case Keys.P:
                    case Keys.E:
                    case Keys.I:
                    case Keys.F:
                    case Keys.L:
                    case Keys.R:
                        UpdateStatus(null);
                        return true;
                }
            }

            if (keyData is (Keys.Control | Keys.S) or
                (Keys.Control | Keys.Z) or
                (Keys.Control | Keys.Y) or
                (Keys.Control | Keys.Shift | Keys.Z) or
                (Keys.Control | Keys.I) or
                (Keys.Control | Keys.E))
            {
                UpdateStatus(null);
                return true;
            }
        }

        if (keyData == (Keys.Control | Keys.S))
        {
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Z))
        {
            Undo();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
        {
            Redo();
            return true;
        }

        if (keyData == (Keys.Control | Keys.H))
        {
            ToggleGrid();
            return true;
        }

        if (keyData == (Keys.Control | Keys.I))
        {
            ImportPng();
            return true;
        }

        if (keyData == (Keys.Control | Keys.E))
        {
            ExportPng();
            return true;
        }

        if (_singleFrameMode && modifiers == Keys.Alt)
        {
            switch (keyCode)
            {
                case Keys.Left:
                    ShiftSingleFrame(RsFrameShiftDirection.Left);
                    return true;
                case Keys.Right:
                    ShiftSingleFrame(RsFrameShiftDirection.Right);
                    return true;
                case Keys.Up:
                    ShiftSingleFrame(RsFrameShiftDirection.Up);
                    return true;
                case Keys.Down:
                    ShiftSingleFrame(RsFrameShiftDirection.Down);
                    return true;
            }
        }

        if (keyData == (Keys.Shift | Keys.Oemplus))
        {
            ChangeZoom(1);
            return true;
        }

        if (modifiers == Keys.None)
        {
            switch (keyCode)
            {
                case Keys.P:
                    SetTool(ToolKind.Pencil);
                    return true;
                case Keys.E:
                    SetTool(ToolKind.Eraser);
                    return true;
                case Keys.I:
                    SetTool(ToolKind.Picker);
                    return true;
                case Keys.F:
                    SetTool(ToolKind.Fill);
                    return true;
                case Keys.L:
                    SetTool(ToolKind.Line);
                    return true;
                case Keys.R:
                    SetTool(ToolKind.Rectangle);
                    return true;
                case Keys.G:
                    ToggleGrid();
                    return true;
                case Keys.Oemplus:
                case Keys.Add:
                    ChangeZoom(1);
                    return true;
                case Keys.OemMinus:
                case Keys.Subtract:
                    ChangeZoom(-1);
                    return true;
                case Keys.D0:
                case Keys.NumPad0:
                    ResetZoom();
                    return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SetTool(ToolKind tool)
    {
        ClearPreviewState();
        _tool = tool;
        UpdateStatus(null);
    }

    private void ToggleGrid()
    {
        _gridCheckBox.Checked = !_gridCheckBox.Checked;
    }

    private void ChangeZoom(int delta)
    {
        _zoomBar.Value = Math.Clamp(_zoomBar.Value + delta, _zoomBar.Minimum, _zoomBar.Maximum);
    }

    private void ResetZoom()
    {
        _zoomBar.Value = Math.Clamp(12, _zoomBar.Minimum, _zoomBar.Maximum);
    }

    private void ClearPreviewState()
    {
        _painting = false;
        _canvas.ClearPreview();
        _canvas.Invalidate();
    }

    private void ShiftSingleFrame(RsFrameShiftDirection direction)
    {
        if (!_singleFrameMode) return;
        SaveUndo();
        using var shifted = RsFrameShiftService.Shift(_document.Bitmap, direction);
        RestoreBitmap(_document.Bitmap, shifted);
        _canvas.Invalidate();
        UpdateStatus(null);
    }

    private static string GetToolDisplayName(ToolKind tool)
        => tool switch
        {
            ToolKind.Pencil => "铅笔",
            ToolKind.Eraser => "橡皮",
            ToolKind.Picker => "取色",
            ToolKind.Fill => "填充",
            ToolKind.Line => "直线",
            ToolKind.Rectangle => "矩形",
            _ => tool.ToString()
        };

    private static string GetToolShortcutText(ToolKind tool)
        => tool switch
        {
            ToolKind.Pencil => "P",
            ToolKind.Eraser => "E",
            ToolKind.Picker => "I",
            ToolKind.Fill => "F",
            ToolKind.Line => "L",
            ToolKind.Rectangle => "R",
            _ => string.Empty
        };

    private IReadOnlyList<Color> BuildPaletteColors()
    {
        if (_document.RestrictToPalette && _document.Palette.Count > 0)
        {
            return _document.Palette
                .Take(256)
                .Select(color => Color.FromArgb(255, color.R, color.G, color.B))
                .DistinctBy(color => color.ToArgb())
                .ToArray();
        }

        var colors = new List<Color>
        {
            Color.Transparent,
            Color.Black,
            Color.White,
            Color.Red,
            Color.Orange,
            Color.Yellow,
            Color.Green,
            Color.Cyan,
            Color.Blue,
            Color.Magenta,
            Color.FromArgb(96, 64, 32),
            Color.FromArgb(128, 128, 128)
        };

        foreach (var color in _document.Palette.Skip(1).Take(48))
        {
            if (colors.All(existing => existing.ToArgb() != color.ToArgb()))
            {
                colors.Add(color);
            }
        }

        return colors;
    }

    private void BuildDocumentTabs()
    {
        if (_group?.ShowTabs != true) return;
        _documentTabs.SuspendLayout();
        _documentTabs.TabPages.Clear();
        foreach (var page in _group.Pages)
        {
            _documentTabs.TabPages.Add(new TabPage(page.Label));
        }
        _documentTabs.SelectedIndex = Math.Clamp(_group.ActiveIndex, 0, _documentTabs.TabPages.Count - 1);
        _documentTabs.ResumeLayout();
    }

    private void SwitchDocument(int index)
    {
        if (_group?.ShowTabs != true || index < 0 || index >= _group.Pages.Count || index == _group.ActiveIndex) return;
        var current = _group.ActivePage;
        current.Zoom = _canvas.Zoom;
        current.ScrollPosition = new Point(-_scroll.AutoScrollPosition.X, -_scroll.AutoScrollPosition.Y);

        ClearPreviewState();
        _group.ActiveIndex = index;
        var next = _group.ActivePage;
        _document = next.Document;
        _canvas.SetBitmap(_document.Bitmap);
        _canvas.FrameWidth = _document.FrameWidth;
        _canvas.FrameHeight = _document.FrameHeight;
        _zoomBar.Value = Math.Clamp(next.Zoom, _zoomBar.Minimum, _zoomBar.Maximum);
        _scroll.AutoScrollPosition = next.ScrollPosition;
        Text = "像素编辑 - " + _document.Target.DisplayName;
        UpdateStatus(null);
    }

    private void ReplaceColors()
    {
        if (_colorReplaceDialog != null)
        {
            _colorReplaceDialog.Show();
            _colorReplaceDialog.Activate();
            return;
        }

        try
        {
            if (_group != null && !_group.EnsureReplacementScope(this)) return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "完整形象组载入失败：\r\n" + ex.Message, "整组换色", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var pages = GetAllPages();
        var dialog = new PixelColorReplaceDialog(pages, _group?.ScopeDescription ?? _document.Target.DisplayName);
        _colorReplaceDialog = dialog;
        dialog.PickRequested += ColorReplaceDialogPickRequested;
        dialog.ApplyRequested += ColorReplaceDialogApplyRequested;
        dialog.FormClosed += ColorReplaceDialogFormClosed;
        SetInteractionState(InteractionState.ConfiguringColorReplacement);
        dialog.Show(this);
    }

    internal void OpenColorReplacementForSmoke()
        => ReplaceColors();

    internal void PickColorReplacementPixelForSmoke(Point pixel, MouseButtons button)
        => CanvasPixelMouseDown(_canvas, new PixelMouseEventArgs(pixel, button));

    private void ColorReplaceDialogPickRequested(object? sender, PixelColorPickRequestedEventArgs e)
    {
        if (!ReferenceEquals(sender, _colorReplaceDialog)) return;
        _pendingColorPick = new ColorReplacementPickRequest(e.RuleIndex, e.FieldKind);
        SetInteractionState(InteractionState.PickingColorReplacement);
        ClearPreviewState();
        UpdateStatus(null);
        Activate();
    }

    private void ColorReplaceDialogApplyRequested(object? sender, EventArgs e)
    {
        if (sender is not PixelColorReplaceDialog dialog || !ReferenceEquals(dialog, _colorReplaceDialog)) return;
        ApplyColorReplacement(dialog);
    }

    private void ApplyColorReplacement(PixelColorReplaceDialog dialog)
    {
        if (dialog.Preview == null) return;
        var pages = GetAllPages();
        var changedKeys = dialog.Preview.Documents
            .Where(document => document.TotalMatches > 0)
            .Select(document => document.DocumentKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        SaveUndoPages(pages.Where(page => changedKeys.Contains(page.Key)));
        var result = _colorReplacementService.Apply(
            pages.Select(page => (page.Key, page.Label, page.Document.Bitmap)).ToArray(),
            dialog.Rules);
        if (_group != null)
        {
            _group.ColorReplacementPreview = result.Preview;
        }
        _canvas.Invalidate();
        _statusLabel.Text = $"整组换色完成：{dialog.Rules.Count} 组，并行替换 {result.Preview.TotalMatches:N0} 个像素，覆盖 {result.ChangedDocumentKeys.Count} 个形象条目。";
        dialog.Close();
    }

    private void ColorReplaceDialogFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (!ReferenceEquals(sender, _colorReplaceDialog)) return;
        var dialog = _colorReplaceDialog;
        if (dialog != null)
        {
            dialog.PickRequested -= ColorReplaceDialogPickRequested;
            dialog.ApplyRequested -= ColorReplaceDialogApplyRequested;
            dialog.FormClosed -= ColorReplaceDialogFormClosed;
            dialog.Dispose();
        }

        _colorReplaceDialog = null;
        _pendingColorPick = null;
        SetInteractionState(InteractionState.Normal, updateStatus: dialog?.IsApplying != true);
    }

    private void CanvasPixelMouseDown(object? sender, PixelMouseEventArgs e)
    {
        if (_interactionState == InteractionState.PickingColorReplacement)
        {
            if (e.Button == MouseButtons.Right)
            {
                CancelColorReplacementPick();
                return;
            }

            if (e.Button == MouseButtons.Left && IsInside(e.Pixel))
            {
                CompleteColorReplacementPick(e.Pixel);
                return;
            }
        }

        if (_interactionState != InteractionState.Normal) return;
        if (!IsInside(e.Pixel)) return;
        _painting = true;
        _startPixel = e.Pixel;
        _lastPixel = e.Pixel;

        if (_tool == ToolKind.Picker)
        {
            _primaryColor = SnapColor(_document.Bitmap.GetPixel(e.Pixel.X, e.Pixel.Y));
            UpdateColorPreview();
            _painting = false;
            return;
        }

        SaveUndo();
        if (_tool == ToolKind.Fill)
        {
            FloodFill(e.Pixel, SnapColor(_primaryColor));
            _painting = false;
            _canvas.Invalidate();
            return;
        }

        if (_tool is ToolKind.Pencil or ToolKind.Eraser)
        {
            DrawPixel(e.Pixel);
            _canvas.Invalidate();
        }
    }

    private void CanvasPixelMouseMove(object? sender, PixelMouseEventArgs e)
    {
        if (_interactionState != InteractionState.Normal)
        {
            UpdateStatus(IsInside(e.Pixel) ? e.Pixel : null);
            return;
        }

        if (!_painting || !IsInside(e.Pixel)) return;
        if (_tool is ToolKind.Pencil or ToolKind.Eraser)
        {
            DrawLine(_lastPixel, e.Pixel, _tool == ToolKind.Eraser ? Color.Transparent : SnapColor(_primaryColor));
            _lastPixel = e.Pixel;
            _canvas.Invalidate();
        }
        else if (_tool is ToolKind.Line or ToolKind.Rectangle)
        {
            _canvas.PreviewStart = _startPixel;
            _canvas.PreviewEnd = e.Pixel;
            _canvas.PreviewKind = _tool.ToString();
            _canvas.PreviewColor = SnapColor(_primaryColor);
            _canvas.Invalidate();
        }
    }

    private void CanvasPixelMouseUp(object? sender, PixelMouseEventArgs e)
    {
        if (_interactionState != InteractionState.Normal)
        {
            _painting = false;
            return;
        }

        if (!_painting) return;
        _painting = false;
        if (IsInside(e.Pixel))
        {
            if (_tool == ToolKind.Line)
            {
                DrawLine(_startPixel, e.Pixel, SnapColor(_primaryColor));
            }
            else if (_tool == ToolKind.Rectangle)
            {
                DrawRectangle(_startPixel, e.Pixel, SnapColor(_primaryColor));
            }
        }

        _canvas.ClearPreview();
        _canvas.Invalidate();
    }

    private void DrawPixel(Point point)
        => _document.Bitmap.SetPixel(point.X, point.Y, _tool == ToolKind.Eraser ? Color.Transparent : SnapColor(_primaryColor));

    private void DrawLine(Point a, Point b, Color color)
    {
        var x0 = a.X;
        var y0 = a.Y;
        var x1 = b.X;
        var y1 = b.Y;
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            if (IsInside(new Point(x0, y0))) _document.Bitmap.SetPixel(x0, y0, color);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private void DrawRectangle(Point a, Point b, Color color)
    {
        var left = Math.Min(a.X, b.X);
        var right = Math.Max(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var bottom = Math.Max(a.Y, b.Y);
        DrawLine(new Point(left, top), new Point(right, top), color);
        DrawLine(new Point(right, top), new Point(right, bottom), color);
        DrawLine(new Point(right, bottom), new Point(left, bottom), color);
        DrawLine(new Point(left, bottom), new Point(left, top), color);
    }

    private void FloodFill(Point start, Color color)
    {
        var old = _document.Bitmap.GetPixel(start.X, start.Y);
        if (old.ToArgb() == color.ToArgb()) return;
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            if (!IsInside(point)) continue;
            if (_document.Bitmap.GetPixel(point.X, point.Y).ToArgb() != old.ToArgb()) continue;
            _document.Bitmap.SetPixel(point.X, point.Y, color);
            queue.Enqueue(new Point(point.X + 1, point.Y));
            queue.Enqueue(new Point(point.X - 1, point.Y));
            queue.Enqueue(new Point(point.X, point.Y + 1));
            queue.Enqueue(new Point(point.X, point.Y - 1));
        }
    }

    private bool IsInside(Point point)
        => point.X >= 0 && point.Y >= 0 && point.X < _document.Bitmap.Width && point.Y < _document.Bitmap.Height;

    private void CompleteColorReplacementPick(Point pixel)
    {
        if (_pendingColorPick == null || _colorReplaceDialog == null || !IsInside(pixel)) return;

        var color = _document.Bitmap.GetPixel(pixel.X, pixel.Y);
        var request = _pendingColorPick;
        _pendingColorPick = null;
        SetInteractionState(InteractionState.ConfiguringColorReplacement);
        _colorReplaceDialog.ApplyPickedColor(request.RuleIndex, request.FieldKind, color);
    }

    private void CancelColorReplacementPick()
    {
        _pendingColorPick = null;
        SetInteractionState(_colorReplaceDialog == null ? InteractionState.Normal : InteractionState.ConfiguringColorReplacement);
        _colorReplaceDialog?.CancelPick();
        UpdateStatus(null);
    }

    private void SetInteractionState(InteractionState state, bool updateStatus = true)
    {
        _interactionState = state;
        _painting = false;
        _canvas.Cursor = state == InteractionState.ConfiguringColorReplacement ? Cursors.Default : Cursors.Cross;
        var commandsEnabled = state == InteractionState.Normal;
        foreach (var control in _disabledDuringColorReplacement.Where(control => !control.IsDisposed).Distinct())
        {
            control.Enabled = commandsEnabled;
        }

        if (updateStatus) UpdateStatus(null);
    }

    private void SaveUndo()
        => SaveUndoPages(new[] { GetActivePage() });

    private void SaveUndoPages(IEnumerable<PixelEditResourcePage> pages)
    {
        var snapshots = pages
            .DistinctBy(page => page.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(page => page.Key, page => new Bitmap(page.Document.Bitmap), StringComparer.OrdinalIgnoreCase);
        if (snapshots.Count == 0) return;
        _undo.Push(new PixelEditHistoryEntry(snapshots, _group?.ColorReplacementPreview));
        DisposeHistory(_redo);
        if (_undo.Count > 50)
        {
            var kept = _undo.Take(50).ToArray();
            foreach (var stale in _undo.Skip(50)) stale.Dispose();

            _undo.Clear();
            for (var i = kept.Length - 1; i >= 0; i--)
            {
                _undo.Push(kept[i]);
            }
        }
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        ClearPreviewState();
        ApplyHistory(_undo, _redo);
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        ClearPreviewState();
        ApplyHistory(_redo, _undo);
    }

    private void ApplyHistory(Stack<PixelEditHistoryEntry> source, Stack<PixelEditHistoryEntry> destination)
    {
        using var entry = source.Pop();
        var current = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in entry.Snapshots.Keys)
        {
            var page = GetAllPages().FirstOrDefault(candidate => candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (page == null) continue;
            current[key] = new Bitmap(page.Document.Bitmap);
            RestoreBitmap(page.Document.Bitmap, entry.Snapshots[key]);
        }
        destination.Push(new PixelEditHistoryEntry(current, _group?.ColorReplacementPreview));
        if (_group != null)
        {
            _group.ColorReplacementPreview = entry.ColorReplacementPreview;
        }
        _canvas.Invalidate();
        UpdateStatus(null);
    }

    private static void RestoreBitmap(Bitmap target, Bitmap source)
    {
        using var g = Graphics.FromImage(target);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImageUnscaled(source, 0, 0);
    }

    private PixelEditResourcePage GetActivePage() => _group?.ActivePage ?? _singlePage;

    private IReadOnlyList<PixelEditResourcePage> GetAllPages() => _group?.Pages ?? new[] { _singlePage };

    private static string BuildDocumentKey(EditableImageDocument document)
    {
        try
        {
            return PixelEditResourceGroup.BuildTargetKey(document.Target);
        }
        catch
        {
            return "document:" + document.Target.DisplayName;
        }
    }

    private static void DisposeHistory(Stack<PixelEditHistoryEntry> history)
    {
        while (history.Count > 0) history.Pop().Dispose();
    }

    private void PickColor()
    {
        using var dialog = new ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            Color = _primaryColor.A == 0 ? Color.White : _primaryColor
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _primaryColor = SnapColor(dialog.Color);
        UpdateColorPreview();
    }

    private void ImportPng()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入 PNG 到当前画布",
            Filter = _singleFrameMode
                ? "PNG 图片 (*.png)|*.png"
                : "PNG 图片 (*.png)|*.png|图片文件 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var raw = Image.FromFile(dialog.FileName);
        if (_singleFrameMode && raw.Size != _document.Bitmap.Size)
        {
            MessageBox.Show(this,
                $"单帧导入尺寸必须严格等于 {_document.Bitmap.Width}x{_document.Bitmap.Height}，当前图片为 {raw.Width}x{raw.Height}。",
                "单帧尺寸不匹配",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        SaveUndo();
        using var g = Graphics.FromImage(_document.Bitmap);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        if (_document.Target.IsItemIconPair)
        {
            using var normalized = new ItemIconRasterNormalizeService().NormalizeLargeBitmap(raw);
            g.DrawImageUnscaled(normalized, 0, 0);
        }
        else if (_document.Target.Kind == EditableImageTargetKind.DllBitmapIcon)
        {
            using var normalized = ImportDllBitmapIconSource(dialog.FileName, raw);
            using var quantized = QuantizeForDocument(normalized);
            g.DrawImageUnscaled(quantized, 0, 0);
        }
        else
        {
            g.DrawImage(raw, new Rectangle(0, 0, _document.Bitmap.Width, _document.Bitmap.Height));
        }
        _canvas.Invalidate();
        UpdateStatus(null);
    }

    private Bitmap ImportDllBitmapIconSource(string path, Image raw)
    {
        var codec = new DllBitmapIconCodecService();
        if (Path.GetExtension(path).Equals(".bmp", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(_document.Target.TargetPath))
        {
            var resources = codec.ParseBitmapResources(_document.Target.TargetPath);
            var pair = codec.ResolveBitmapResourcePair(resources, _document.Target.IconIndex);
            var classification = codec.ClassifyItemIconBmpImport(path, pair, resources);
            if (classification.PreserveStorage && classification.StoragePair != null)
            {
                using var decoded = DllBitmapIconCodecService.DecodeDib(classification.StoragePair.Large.DibBytes);
                if (decoded != null)
                {
                    return new Bitmap(decoded.Bitmap);
                }
            }
        }

        return codec.NormalizeLargeBitmap(raw);
    }

    private Bitmap QuantizeForDocument(Bitmap source)
        => _document.RestrictToPalette && _document.Palette.Count > 0
            ? DllBitmapIconCodecService.QuantizeBitmapToPalette(source, _document.Palette)
            : new Bitmap(source);

    private Color SnapColor(Color color)
        => _document.RestrictToPalette && _document.Palette.Count > 0
            ? DllBitmapIconCodecService.MapColorToPalette(color, _document.Palette)
            : color;

    private void ExportPng()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出当前画布 PNG",
            Filter = "PNG 图片 (*.png)|*.png",
            FileName = "pixel-edit.png"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _document.Bitmap.Save(dialog.FileName, ImageFormat.Png);
    }

    private void UpdateColorPreview()
    {
        using var bitmap = new Bitmap(_colorPreview.Width, _colorPreview.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            DrawChecker(g, new Rectangle(0, 0, bitmap.Width, bitmap.Height), 6);
            using var brush = new SolidBrush(_primaryColor);
            g.FillRectangle(brush, 0, 0, bitmap.Width, bitmap.Height);
        }
        var old = _colorPreview.Image;
        _colorPreview.Image = null;
        _colorPreview.Image = new Bitmap(bitmap);
        old?.Dispose();
    }

    private void UpdateStatus(Point? pixel)
    {
        if (_interactionState == InteractionState.PickingColorReplacement && _pendingColorPick != null)
        {
            var field = _pendingColorPick.FieldKind == PixelColorReplaceDialog.PickFieldKind.Source ? "源" : "目标";
            var hover = string.Empty;
            if (pixel.HasValue && IsInside(pixel.Value))
            {
                var color = PixelColorReplacementService.NormalizeColor(_document.Bitmap.GetPixel(pixel.Value.X, pixel.Value.Y));
                var colorText = color.A == 0
                    ? "透明"
                    : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                hover = $"    坐标 {pixel.Value.X},{pixel.Value.Y}    {colorText}";
            }

            _statusLabel.Text = $"正在为规则 {_pendingColorPick.RuleIndex + 1} 选择{field}颜色；左键取色，右键或 Esc 取消。缩放、滚动、网格和动作标签可用。{hover}";
            return;
        }

        if (_interactionState == InteractionState.ConfiguringColorReplacement)
        {
            _statusLabel.Text = "换色窗口已打开；主画布暂不绘图。点击换色窗口里的“取色”后可回到画布单击像素取色。";
            return;
        }

        var pos = pixel.HasValue && IsInside(pixel.Value)
            ? $"    坐标 {pixel.Value.X},{pixel.Value.Y}"
            : string.Empty;
        var frame = _document.HasFrameGrid
            ? $"    帧 {_document.FrameWidth}x{_document.FrameHeight}"
            : string.Empty;
        _statusLabel.Text = $"{_document.LoadDetail}    工具 {GetToolDisplayName(_tool)}    缩放 {_canvas.Zoom}x{frame}{pos}";
    }

    private static void DrawChecker(Graphics g, Rectangle rect, int size)
    {
        using var light = new SolidBrush(Color.FromArgb(230, 230, 230));
        using var dark = new SolidBrush(Color.FromArgb(190, 190, 190));
        for (var y = rect.Top; y < rect.Bottom; y += size)
        {
            for (var x = rect.Left; x < rect.Right; x += size)
            {
                var even = ((x / size) + (y / size)) % 2 == 0;
                g.FillRectangle(even ? light : dark, x, y, size, size);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var dialog = _colorReplaceDialog;
            _colorReplaceDialog = null;
            if (dialog != null)
            {
                dialog.PickRequested -= ColorReplaceDialogPickRequested;
                dialog.ApplyRequested -= ColorReplaceDialogApplyRequested;
                dialog.FormClosed -= ColorReplaceDialogFormClosed;
                dialog.Close();
                dialog.Dispose();
            }

            DisposeHistory(_undo);
            DisposeHistory(_redo);
        }
        base.Dispose(disposing);
    }

    private sealed class PixelEditHistoryEntry : IDisposable
    {
        public PixelEditHistoryEntry(
            IReadOnlyDictionary<string, Bitmap> snapshots,
            PixelColorReplacementPreview? colorReplacementPreview)
        {
            Snapshots = snapshots;
            ColorReplacementPreview = colorReplacementPreview;
        }

        public IReadOnlyDictionary<string, Bitmap> Snapshots { get; }
        public PixelColorReplacementPreview? ColorReplacementPreview { get; }

        public void Dispose()
        {
            foreach (var bitmap in Snapshots.Values) bitmap.Dispose();
        }
    }

    private sealed class PixelCanvas : Control
    {
        private Bitmap _bitmap;
        private int _zoom = 12;

        public PixelCanvas(Bitmap bitmap)
        {
            _bitmap = bitmap;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Cursor = Cursors.Cross;
            Size = new Size(_bitmap.Width * _zoom + 1, _bitmap.Height * _zoom + 1);
        }

        public event EventHandler<PixelMouseEventArgs>? PixelMouseDown;
        public event EventHandler<PixelMouseEventArgs>? PixelMouseMove;
        public event EventHandler<PixelMouseEventArgs>? PixelMouseUp;
        public event EventHandler<Point?>? PixelHover;

        public bool ShowGrid { get; set; }
        public int? FrameWidth { get; set; }
        public int? FrameHeight { get; set; }
        public string PreviewKind { get; set; } = string.Empty;
        public Point? PreviewStart { get; set; }
        public Point? PreviewEnd { get; set; }
        public Color PreviewColor { get; set; } = Color.Black;

        public int Zoom
        {
            get => _zoom;
            set
            {
                _zoom = Math.Clamp(value, 2, 32);
                Size = new Size(_bitmap.Width * _zoom + 1, _bitmap.Height * _zoom + 1);
                Invalidate();
            }
        }

        public void ClearPreview()
        {
            PreviewKind = string.Empty;
            PreviewStart = null;
            PreviewEnd = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.Clear(Color.FromArgb(44, 44, 48));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            DrawChecker(g, new Rectangle(0, 0, _bitmap.Width * Zoom, _bitmap.Height * Zoom), Math.Max(4, Zoom));
            DrawPixelScaledBitmap(g, _bitmap, Zoom);

            if (ShowGrid && Zoom >= 6)
            {
                using var gridPen = new Pen(Color.FromArgb(70, Color.Black));
                for (var x = 0; x <= _bitmap.Width; x++) g.DrawLine(gridPen, x * Zoom, 0, x * Zoom, _bitmap.Height * Zoom);
                for (var y = 0; y <= _bitmap.Height; y++) g.DrawLine(gridPen, 0, y * Zoom, _bitmap.Width * Zoom, y * Zoom);
            }

            if (!string.IsNullOrWhiteSpace(PreviewKind) && PreviewStart.HasValue && PreviewEnd.HasValue)
            {
                using var pen = new Pen(PreviewColor.A == 0 ? Color.Red : PreviewColor, Math.Max(1, Zoom / 5f));
                var a = PreviewStart.Value;
                var b = PreviewEnd.Value;
                if (PreviewKind == ToolKind.Line.ToString())
                {
                    g.DrawLine(pen, a.X * Zoom + Zoom / 2, a.Y * Zoom + Zoom / 2, b.X * Zoom + Zoom / 2, b.Y * Zoom + Zoom / 2);
                }
                else
                {
                    var left = Math.Min(a.X, b.X) * Zoom;
                    var top = Math.Min(a.Y, b.Y) * Zoom;
                    var width = (Math.Abs(a.X - b.X) + 1) * Zoom;
                    var height = (Math.Abs(a.Y - b.Y) + 1) * Zoom;
                    g.DrawRectangle(pen, left, top, width, height);
                }
            }
        }

        public void SetBitmap(Bitmap bitmap)
        {
            _bitmap = bitmap;
            Size = new Size(_bitmap.Width * _zoom + 1, _bitmap.Height * _zoom + 1);
            ClearPreview();
            Invalidate();
        }

        private static void DrawPixelScaledBitmap(Graphics g, Bitmap bitmap, int zoom)
        {
            var brushes = new Dictionary<int, SolidBrush>();
            try
            {
                for (var y = 0; y < bitmap.Height; y++)
                {
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var color = bitmap.GetPixel(x, y);
                        if (color.A == 0) continue;
                        var key = color.ToArgb();
                        if (!brushes.TryGetValue(key, out var brush))
                        {
                            brush = new SolidBrush(color);
                            brushes[key] = brush;
                        }

                        g.FillRectangle(brush, x * zoom, y * zoom, zoom, zoom);
                    }
                }
            }
            finally
            {
                foreach (var brush in brushes.Values)
                {
                    brush.Dispose();
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            PixelMouseDown?.Invoke(this, new PixelMouseEventArgs(ToPixel(e.Location), e.Button));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pixel = ToPixel(e.Location);
            PixelHover?.Invoke(this, pixel.X >= 0 && pixel.Y >= 0 && pixel.X < _bitmap.Width && pixel.Y < _bitmap.Height ? pixel : null);
            PixelMouseMove?.Invoke(this, new PixelMouseEventArgs(pixel, e.Button));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            PixelMouseUp?.Invoke(this, new PixelMouseEventArgs(ToPixel(e.Location), e.Button));
        }

        private Point ToPixel(Point point)
            => new(point.X / Math.Max(1, Zoom), point.Y / Math.Max(1, Zoom));
    }

    private sealed class PixelMouseEventArgs(Point pixel, MouseButtons button) : EventArgs
    {
        public Point Pixel { get; } = pixel;
        public MouseButtons Button { get; } = button;
    }

    private sealed record ColorReplacementPickRequest(int RuleIndex, PixelColorReplaceDialog.PickFieldKind FieldKind);
}
