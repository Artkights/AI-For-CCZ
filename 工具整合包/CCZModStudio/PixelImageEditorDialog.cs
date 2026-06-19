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

    private readonly EditableImageDocument _document;
    private readonly PixelCanvas _canvas;
    private readonly PictureBox _colorPreview = new();
    private readonly TrackBar _zoomBar = new();
    private readonly Label _statusLabel = new();
    private readonly Stack<Bitmap> _undo = new();
    private readonly Stack<Bitmap> _redo = new();
    private ToolKind _tool = ToolKind.Pencil;
    private Color _primaryColor = Color.Black;
    private bool _painting;
    private Point _startPixel;
    private Point _lastPixel;

    public PixelImageEditorDialog(EditableImageDocument document)
    {
        _document = document;
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
        UpdateStatus(null);
    }

    public Bitmap EditedBitmap => _document.Bitmap;

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
        root.Controls.Add(scroll, 0, 1);

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
        bottom.Controls.Add(ok, 1, 0);
        bottom.Controls.Add(cancel, 2, 0);
        AcceptButton = ok;
        CancelButton = cancel;
        root.Controls.Add(bottom, 0, 2);
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
        bar.Controls.Add(MakeButton("颜色", PickColor));
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

        bar.Controls.Add(MakeButton("导入PNG", ImportPng));
        bar.Controls.Add(MakeButton("导出PNG", ExportPng));
        bar.Controls.Add(MakeButton("撤销", Undo));
        bar.Controls.Add(MakeButton("重做", Redo));
        var grid = new CheckBox { Text = "网格", Checked = true, AutoSize = true, Padding = new Padding(10, 7, 0, 0) };
        grid.CheckedChanged += (_, _) =>
        {
            _canvas.ShowGrid = grid.Checked;
            _canvas.Invalidate();
        };
        bar.Controls.Add(grid);
        bar.Controls.Add(new Label { Text = "缩放", AutoSize = true, Padding = new Padding(10, 8, 0, 0) });
        _zoomBar.Minimum = 2;
        _zoomBar.Maximum = 32;
        _zoomBar.Value = Math.Clamp(_canvas.Zoom, _zoomBar.Minimum, _zoomBar.Maximum);
        _zoomBar.Width = 140;
        _zoomBar.AutoSize = false;
        _zoomBar.Height = 28;
        _zoomBar.TickFrequency = 4;
        _zoomBar.ValueChanged += (_, _) => _canvas.Zoom = _zoomBar.Value;
        bar.Controls.Add(_zoomBar);

        return bar;
    }

    private Button MakeToolButton(string text, ToolKind tool)
    {
        var button = MakeButton(text, () =>
        {
            _tool = tool;
            UpdateStatus(null);
        });
        return button;
    }

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(64, 30), Margin = new Padding(2) };
        button.Click += (_, _) => action();
        return button;
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
        _redo.Push(new Bitmap(_document.Bitmap));
        using var previous = _undo.Pop();
        using var g = Graphics.FromImage(_document.Bitmap);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(previous, 0, 0);
        _canvas.Invalidate();
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(new Bitmap(_document.Bitmap));
        using var next = _redo.Pop();
        using var g = Graphics.FromImage(_document.Bitmap);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(next, 0, 0);
        _canvas.Invalidate();
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
        SaveUndo();
        using var g = Graphics.FromImage(_document.Bitmap);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(raw, new Rectangle(0, 0, _document.Bitmap.Width, _document.Bitmap.Height));
        _canvas.Invalidate();
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
        _statusLabel.Text = $"{_document.LoadDetail}    工具 {_tool}    缩放 {_canvas.Zoom}x{frame}{pos}";
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
