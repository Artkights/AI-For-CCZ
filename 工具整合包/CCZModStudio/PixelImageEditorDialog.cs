using CCZModStudio.Models;
using CCZModStudio.Core;
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

    private readonly EditableImageDocument _document;
    private readonly Func<Bitmap, bool>? _writeBackAction;
    private readonly PixelCanvas _canvas;
    private readonly PictureBox _colorPreview = new();
    private readonly PictureBox _largeGamePreview = new();
    private readonly PictureBox _smallGamePreview = new();
    private readonly TrackBar _zoomBar = new();
    private readonly Label _statusLabel = new();
    private readonly ToolTip _toolTip = new();
    private CheckBox? _gridCheckBox;
    private readonly Stack<Bitmap> _undo = new();
    private readonly Stack<Bitmap> _redo = new();
    private ToolKind _tool = ToolKind.Pencil;
    private Color _primaryColor = Color.Black;
    private bool _painting;
    private Point _startPixel;
    private Point _lastPixel;
    private int _editRevision;
    private int _lastWriteBackRevision = -1;

    public PixelImageEditorDialog(EditableImageDocument document, Func<Bitmap, bool>? writeBackAction = null)
    {
        _document = document;
        _writeBackAction = writeBackAction;
        Text = "像素编辑 - " + document.Target.DisplayName;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        Width = 1120;
        Height = 780;
        KeyPreview = true;

        _canvas = new PixelCanvas(document.Bitmap)
        {
            Dock = DockStyle.None,
            Location = Point.Empty,
            FrameWidth = document.FrameWidth,
            FrameHeight = document.FrameHeight,
            ShowGrid = true
        };
        _canvas.PixelMouseDown += CanvasPixelMouseDown;
        _canvas.PixelMouseMove += CanvasPixelMouseMove;
        _canvas.PixelMouseUp += CanvasPixelMouseUp;
        _canvas.PixelHover += (_, point) => UpdateStatus(point);

        Controls.Add(BuildRoot());
        UpdateColorPreview();
        RefreshGamePreview();
        UpdateStatus(null);
    }

    public Bitmap EditedBitmap => _document.Bitmap;
    public bool IsCurrentRevisionWritten => _lastWriteBackRevision >= 0 && _lastWriteBackRevision == _editRevision;
    internal string CurrentToolDisplayNameForSmoke => GetToolDisplayName(_tool);
    internal bool IsGridVisibleForSmoke => _canvas.ShowGrid;
    internal int ZoomForSmoke => _canvas.Zoom;

    private Control BuildRoot()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildToolbar(), 0, 0);

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(36, 36, 40)
        };
        scroll.Controls.Add(_canvas);
        if (_document.IconSlotInfo == null)
        {
            root.Controls.Add(scroll, 0, 1);
        }
        else
        {
            var middle = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 1,
                ColumnCount = 2
            };
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            middle.Controls.Add(scroll, 0, 0);
            middle.Controls.Add(BuildGamePreviewPanel(), 1, 0);
            root.Controls.Add(middle, 0, 1);
        }

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 4,
            AutoSize = true
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(2, 8, 12, 0);
        bottom.Controls.Add(_statusLabel, 0, 0);
        var writeBack = new Button { Text = "写回游戏", AutoSize = true, MinimumSize = new Size(96, 32), Enabled = _writeBackAction != null };
        _toolTip.SetToolTip(writeBack, "写回游戏 Ctrl+S");
        writeBack.Click += (_, _) => WriteBackToGame();
        var ok = new Button { Text = "保存写回并关闭", AutoSize = true, MinimumSize = new Size(118, 32), DialogResult = DialogResult.OK };
        _toolTip.SetToolTip(ok, "保存写回并关闭 Ctrl+Enter");
        var cancel = new Button { Text = "取消", AutoSize = true, MinimumSize = new Size(80, 32), DialogResult = DialogResult.Cancel };
        bottom.Controls.Add(writeBack, 1, 0);
        bottom.Controls.Add(ok, 2, 0);
        bottom.Controls.Add(cancel, 3, 0);
        AcceptButton = ok;
        CancelButton = cancel;
        root.Controls.Add(bottom, 0, 2);
        return root;
    }

    private Control BuildGamePreviewPanel()
    {
        var slotInfo = _document.IconSlotInfo;
        if (slotInfo == null) throw new InvalidOperationException("当前图片没有 DLL 图标槽信息。");

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Padding = new Padding(8, 0, 0, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = "写前模拟大图",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 4)
        }, 0, 0);
        ConfigurePreviewBox(_largeGamePreview);
        panel.Controls.Add(_largeGamePreview, 0, 1);

        panel.Controls.Add(new Label
        {
            Text = "写前模拟小图",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(0, 8, 0, 4)
        }, 0, 2);
        ConfigurePreviewBox(_smallGamePreview);
        panel.Controls.Add(_smallGamePreview, 0, 3);

        var info = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Text = slotInfo.DisplayText
        };
        panel.Controls.Add(info, 0, 4);
        return panel;
    }

    private static void ConfigurePreviewBox(PictureBox box)
    {
        box.Dock = DockStyle.Fill;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.SizeMode = PictureBoxSizeMode.CenterImage;
        box.BackColor = Color.FromArgb(44, 44, 48);
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

        bar.Controls.Add(MakeToolButton("铅笔", ToolKind.Pencil, "铅笔 P"));
        bar.Controls.Add(MakeToolButton("橡皮", ToolKind.Eraser, "橡皮 E"));
        bar.Controls.Add(MakeToolButton("取色", ToolKind.Picker, "取色 I"));
        bar.Controls.Add(MakeToolButton("填充", ToolKind.Fill, "填充 F"));
        bar.Controls.Add(MakeToolButton("直线", ToolKind.Line, "直线 L"));
        bar.Controls.Add(MakeToolButton("矩形", ToolKind.Rectangle, "矩形 R"));
        bar.Controls.Add(MakeButton("颜色", PickColor, "选择颜色"));
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
            bar.Controls.Add(swatch);
        }

        bar.Controls.Add(MakeButton("导入PNG", ImportPng, "导入 PNG Ctrl+I"));
        bar.Controls.Add(MakeButton("导出PNG", ExportPng, "导出 PNG Ctrl+E"));
        if (_writeBackAction != null)
        {
            bar.Controls.Add(MakeButton("写回游戏", () => WriteBackToGame(), "写回游戏 Ctrl+S"));
        }
        bar.Controls.Add(MakeButton("撤销", Undo, "撤销 Ctrl+Z"));
        bar.Controls.Add(MakeButton("重做", Redo, "重做 Ctrl+Y / Ctrl+Shift+Z"));
        var gridCheckBox = new CheckBox { Text = "网格", Checked = true, AutoSize = true, Padding = new Padding(10, 7, 0, 0) };
        _gridCheckBox = gridCheckBox;
        _toolTip.SetToolTip(gridCheckBox, "显示/隐藏网格 Ctrl+H / G");
        gridCheckBox.CheckedChanged += (_, _) =>
        {
            _canvas.ShowGrid = gridCheckBox.Checked;
            _canvas.Invalidate();
        };
        bar.Controls.Add(gridCheckBox);
        bar.Controls.Add(new Label { Text = "缩放", AutoSize = true, Padding = new Padding(10, 8, 0, 0) });
        _zoomBar.Minimum = 2;
        _zoomBar.Maximum = 32;
        _zoomBar.Value = Math.Clamp(_canvas.Zoom, _zoomBar.Minimum, _zoomBar.Maximum);
        _zoomBar.Width = 140;
        _zoomBar.AutoSize = false;
        _zoomBar.Height = 28;
        _zoomBar.TickFrequency = 4;
        _zoomBar.ValueChanged += (_, _) => _canvas.Zoom = _zoomBar.Value;
        _toolTip.SetToolTip(_zoomBar, "缩放 +/-，重置 0");
        bar.Controls.Add(_zoomBar);

        return bar;
    }

    private Button MakeToolButton(string text, ToolKind tool, string tooltip)
    {
        var button = MakeButton(text, () => SetTool(tool), tooltip);
        return button;
    }

    private Button MakeButton(string text, Action action, string? tooltip = null)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(64, 30), Margin = new Padding(2) };
        button.Click += (_, _) => action();
        if (!string.IsNullOrWhiteSpace(tooltip)) _toolTip.SetToolTip(button, tooltip);
        return button;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ShouldIgnoreShortcut()) return base.ProcessCmdKey(ref msg, keyData);
        return TryHandleShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);
    }

    internal bool TryHandleShortcutForSmoke(Keys keyData)
        => TryHandleShortcut(keyData);

    private bool TryHandleShortcut(Keys keyData)
    {
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

        if (keyData == (Keys.Control | Keys.S))
        {
            if (_writeBackAction == null) return false;
            WriteBackToGame();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Enter))
        {
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        var modifiers = keyData & (Keys.Control | Keys.Alt | Keys.Shift);
        if (modifiers != Keys.None) return false;

        switch (keyData & Keys.KeyCode)
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
            case Keys.Add:
            case Keys.Oemplus:
                CancelPaintingPreview();
                SetZoom(_canvas.Zoom + 1);
                return true;
            case Keys.Subtract:
            case Keys.OemMinus:
                CancelPaintingPreview();
                SetZoom(_canvas.Zoom - 1);
                return true;
            case Keys.D0:
            case Keys.NumPad0:
                CancelPaintingPreview();
                SetZoom(12);
                return true;
            default:
                return false;
        }
    }

    private bool ShouldIgnoreShortcut()
        => ActiveControl is TextBoxBase or ComboBox;

    private void SetTool(ToolKind tool)
    {
        CancelPaintingPreview();
        _tool = tool;
        UpdateStatus(null);
    }

    private void ToggleGrid()
    {
        if (_gridCheckBox != null)
        {
            _gridCheckBox.Checked = !_gridCheckBox.Checked;
        }
        else
        {
            _canvas.ShowGrid = !_canvas.ShowGrid;
            _canvas.Invalidate();
        }
    }

    private void SetZoom(int value)
    {
        var next = Math.Clamp(value, _zoomBar.Minimum, _zoomBar.Maximum);
        _zoomBar.Value = next;
        _canvas.Zoom = next;
        UpdateStatus(null);
    }

    private void CancelPaintingPreview()
    {
        _painting = false;
        _canvas.ClearPreview();
        _canvas.Invalidate();
    }

    private void RefreshGamePreview()
    {
        var slot = _document.IconSlotInfo;
        if (slot == null) return;

        SetPreviewImage(_largeGamePreview, BuildVariantPreview(slot.LargeVariant));
        SetPreviewImage(_smallGamePreview, BuildVariantPreview(slot.SmallVariant));
    }

    private Bitmap? BuildVariantPreview(IconResourceVariantInfo? variant)
    {
        if (variant == null) return null;
        IconTransparencyNormalizeResult? normalized = null;
        try
        {
            var source = _document.Bitmap;
            if (_document.Target.Kind == EditableImageTargetKind.DllBitmapIcon)
            {
                normalized = DllIconBitmapCodec.NormalizeIconSource(source, useCornerBackgroundKey: false);
                source = normalized.Bitmap;
            }

            using var scaled = DllIconBitmapCodec.ScaleToFit(source, variant.Width, variant.Height);
            return DllIconBitmapCodec.RenderPixelPreview(scaled, 96);
        }
        finally
        {
            normalized?.Dispose();
        }
    }

    private static void SetPreviewImage(PictureBox box, Image? image)
    {
        var old = box.Image;
        box.Image = image;
        old?.Dispose();
    }

    private IReadOnlyList<Color> BuildPaletteColors()
    {
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

    private void CanvasPixelMouseDown(object? sender, PixelMouseEventArgs e)
    {
        if (!IsInside(e.Pixel)) return;
        _painting = true;
        _startPixel = e.Pixel;
        _lastPixel = e.Pixel;

        if (_tool == ToolKind.Picker)
        {
            _primaryColor = _document.Bitmap.GetPixel(e.Pixel.X, e.Pixel.Y);
            UpdateColorPreview();
            _painting = false;
            return;
        }

        SaveUndo();
        if (_tool == ToolKind.Fill)
        {
            FloodFill(e.Pixel, _primaryColor);
            _painting = false;
            _canvas.Invalidate();
            RefreshGamePreview();
            return;
        }

        if (_tool is ToolKind.Pencil or ToolKind.Eraser)
        {
            DrawPixel(e.Pixel);
            _canvas.Invalidate();
            RefreshGamePreview();
        }
    }

    private void CanvasPixelMouseMove(object? sender, PixelMouseEventArgs e)
    {
        if (!_painting || !IsInside(e.Pixel)) return;
        if (_tool is ToolKind.Pencil or ToolKind.Eraser)
        {
            DrawLine(_lastPixel, e.Pixel, _tool == ToolKind.Eraser ? Color.Transparent : _primaryColor);
            _lastPixel = e.Pixel;
            _canvas.Invalidate();
        }
        else if (_tool is ToolKind.Line or ToolKind.Rectangle)
        {
            _canvas.PreviewStart = _startPixel;
            _canvas.PreviewEnd = e.Pixel;
            _canvas.PreviewKind = _tool.ToString();
            _canvas.PreviewColor = _primaryColor;
            _canvas.Invalidate();
        }
    }

    private void CanvasPixelMouseUp(object? sender, PixelMouseEventArgs e)
    {
        if (!_painting) return;
        _painting = false;
        if (IsInside(e.Pixel))
        {
            if (_tool == ToolKind.Line)
            {
                DrawLine(_startPixel, e.Pixel, _primaryColor);
            }
            else if (_tool == ToolKind.Rectangle)
            {
                DrawRectangle(_startPixel, e.Pixel, _primaryColor);
            }
        }

        _canvas.ClearPreview();
        _canvas.Invalidate();
        RefreshGamePreview();
    }

    private void DrawPixel(Point point)
        => _document.Bitmap.SetPixel(point.X, point.Y, _tool == ToolKind.Eraser ? Color.Transparent : _primaryColor);

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

    private void SaveUndo()
    {
        _undo.Push(new Bitmap(_document.Bitmap));
        _redo.Clear();
        MarkEdited();
        if (_undo.Count > 50)
        {
            var kept = _undo.Take(50).ToArray();
            foreach (var stale in _undo.Skip(50))
            {
                stale.Dispose();
            }

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
        CancelPaintingPreview();
        _redo.Push(new Bitmap(_document.Bitmap));
        using var previous = _undo.Pop();
        using var g = Graphics.FromImage(_document.Bitmap);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(previous, 0, 0);
        MarkEdited();
        _canvas.Invalidate();
        RefreshGamePreview();
        UpdateStatus(null);
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        CancelPaintingPreview();
        _undo.Push(new Bitmap(_document.Bitmap));
        using var next = _redo.Pop();
        using var g = Graphics.FromImage(_document.Bitmap);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(next, 0, 0);
        MarkEdited();
        _canvas.Invalidate();
        RefreshGamePreview();
        UpdateStatus(null);
    }

    private void WriteBackToGame()
    {
        if (_writeBackAction == null) return;
        if (_writeBackAction(_document.Bitmap))
        {
            _lastWriteBackRevision = _editRevision;
        }
        RefreshGamePreview();
        UpdateStatus(null);
    }

    private void MarkEdited()
    {
        unchecked
        {
            _editRevision++;
        }
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
        _primaryColor = dialog.Color;
        UpdateColorPreview();
    }

    private void ImportPng()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入 PNG 到当前画布",
            Filter = "PNG 图片 (*.png)|*.png|图片文件 (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var raw = Image.FromFile(dialog.FileName);
        using var scaled = BuildImportedCanvasBitmap(raw, _document.Target.Kind, _document.Bitmap.Size);
        SaveUndo();
        for (var y = 0; y < _document.Bitmap.Height; y++)
        {
            for (var x = 0; x < _document.Bitmap.Width; x++)
            {
                _document.Bitmap.SetPixel(x, y, scaled.GetPixel(x, y));
            }
        }

        MarkEdited();
        _canvas.Invalidate();
        RefreshGamePreview();
        UpdateStatus(null);
    }

    private static Bitmap BuildImportedCanvasBitmap(Image raw, EditableImageTargetKind targetKind, Size canvasSize)
    {
        var width = Math.Max(1, canvasSize.Width);
        var height = Math.Max(1, canvasSize.Height);
        if (targetKind == EditableImageTargetKind.DllBitmapIcon)
        {
            var codec = new DllIconBitmapCodec();
            using var prepared = codec.PrepareIconSource(
                raw,
                new Size(width, height),
                new Size(width, height),
                new IconSourcePrepareOptions(UseCornerBackgroundKey: true));
            return new Bitmap(prepared.LargeBitmap);
        }

        using var normalized = DllIconBitmapCodec.NormalizeIconSource(raw, useCornerBackgroundKey: false);
        return StretchToCanvas(normalized.Bitmap, width, height);
    }

    private static Bitmap StretchToCanvas(Bitmap source, int width, int height)
    {
        var output = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(output);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.Clear(Color.FromArgb(0, 0, 0, 0));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(source, new Rectangle(0, 0, output.Width, output.Height));
        return output;
    }

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
        _colorPreview.Image = new Bitmap(bitmap);
        old?.Dispose();
    }

    private void UpdateStatus(Point? pixel)
    {
        var pos = pixel.HasValue && IsInside(pixel.Value)
            ? $"    坐标 {pixel.Value.X},{pixel.Value.Y}"
            : string.Empty;
        var frame = _document.HasFrameGrid
            ? $"    帧 {_document.FrameWidth}x{_document.FrameHeight}"
            : string.Empty;
        var slot = _document.IconSlotInfo == null ? string.Empty : $"    {_document.IconSlotInfo.DisplayText}    写后重读结果见写回信息";
        _statusLabel.Text = $"{_document.LoadDetail}{slot}    工具 {GetToolDisplayName(_tool)}    缩放 {_canvas.Zoom}x{frame}{pos}";
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

    private sealed class PixelCanvas : Control
    {
        private readonly Bitmap _bitmap;
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
            g.DrawImage(_bitmap, new Rectangle(0, 0, _bitmap.Width * Zoom, _bitmap.Height * Zoom), new Rectangle(0, 0, _bitmap.Width, _bitmap.Height), GraphicsUnit.Pixel);

            if (ShowGrid && Zoom >= 6)
            {
                using var gridPen = new Pen(Color.FromArgb(70, Color.Black));
                for (var x = 0; x <= _bitmap.Width; x++) g.DrawLine(gridPen, x * Zoom, 0, x * Zoom, _bitmap.Height * Zoom);
                for (var y = 0; y <= _bitmap.Height; y++) g.DrawLine(gridPen, 0, y * Zoom, _bitmap.Width * Zoom, y * Zoom);
            }

            var frameWidth = FrameWidth.GetValueOrDefault();
            var frameHeight = FrameHeight.GetValueOrDefault();
            if (frameWidth > 0 && frameHeight > 0)
            {
                using var framePen = new Pen(Color.FromArgb(210, Color.DeepSkyBlue), 2);
                for (var x = frameWidth; x < _bitmap.Width; x += frameWidth) g.DrawLine(framePen, x * Zoom, 0, x * Zoom, _bitmap.Height * Zoom);
                for (var y = frameHeight; y < _bitmap.Height; y += frameHeight) g.DrawLine(framePen, 0, y * Zoom, _bitmap.Width * Zoom, y * Zoom);
                using var frameFont = new Font(FontFamily.GenericSansSerif, Math.Max(7f, Math.Min(11f, Zoom * 0.55f)), FontStyle.Bold, GraphicsUnit.Pixel);
                using var frameBack = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
                using var frameBrush = new SolidBrush(Color.White);
                var frameIndex = 0;
                for (var y = 0; y < _bitmap.Height; y += frameHeight)
                {
                    for (var x = 0; x < _bitmap.Width; x += frameWidth)
                    {
                        var text = (++frameIndex).ToString(CultureInfo.InvariantCulture);
                        var size = g.MeasureString(text, frameFont);
                        var rect = new RectangleF(x * Zoom + 2, y * Zoom + 2, size.Width + 5, size.Height + 2);
                        g.FillRectangle(frameBack, rect);
                        g.DrawString(text, frameFont, frameBrush, rect.Left + 2, rect.Top + 1);
                    }
                }
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            PixelMouseDown?.Invoke(this, new PixelMouseEventArgs(ToPixel(e.Location)));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pixel = ToPixel(e.Location);
            PixelHover?.Invoke(this, pixel.X >= 0 && pixel.Y >= 0 && pixel.X < _bitmap.Width && pixel.Y < _bitmap.Height ? pixel : null);
            PixelMouseMove?.Invoke(this, new PixelMouseEventArgs(pixel));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            PixelMouseUp?.Invoke(this, new PixelMouseEventArgs(ToPixel(e.Location)));
        }

        private Point ToPixel(Point point)
            => new(point.X / Math.Max(1, Zoom), point.Y / Math.Max(1, Zoom));
    }

    private sealed class PixelMouseEventArgs(Point pixel) : EventArgs
    {
        public Point Pixel { get; } = pixel;
    }
}
