using System.Drawing.Drawing2D;

namespace CCZModStudio;

internal sealed class AspectRatioPictureBox : PictureBox
{
    public bool ShowCheckerboard { get; set; }

    public InterpolationMode InterpolationMode { get; set; } = InterpolationMode.NearestNeighbor;

    public PixelOffsetMode PixelOffsetMode { get; set; } = PixelOffsetMode.Half;

    public AspectRatioPictureBox()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        SizeMode = PictureBoxSizeMode.Normal;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (ShowCheckerboard)
        {
            DrawCheckerboard(e.Graphics, ClientRectangle, Math.Max(4, DeviceDpi / 12));
        }
        else
        {
            e.Graphics.Clear(BackColor);
        }
        var image = Image;
        if (image == null)
        {
            return;
        }

        var target = AspectRatioDisplay.CalculateContainRectangle(image.Size, ClientRectangle);
        if (target.Width <= 0 || target.Height <= 0)
        {
            return;
        }

        e.Graphics.InterpolationMode = InterpolationMode;
        e.Graphics.PixelOffsetMode = PixelOffsetMode;
        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.CompositingQuality = InterpolationMode == InterpolationMode.NearestNeighbor
            ? CompositingQuality.HighSpeed
            : CompositingQuality.HighQuality;
        e.Graphics.DrawImage(
            image,
            target,
            new Rectangle(Point.Empty, image.Size),
            GraphicsUnit.Pixel);
    }

    private static void DrawCheckerboard(Graphics graphics, Rectangle bounds, int cellSize)
    {
        graphics.Clear(Color.FromArgb(224, 224, 224));
        using var dark = new SolidBrush(Color.FromArgb(188, 188, 188));
        for (var y = bounds.Top; y < bounds.Bottom; y += cellSize)
        for (var x = bounds.Left; x < bounds.Right; x += cellSize)
        {
            if ((((x - bounds.Left) / cellSize) + ((y - bounds.Top) / cellSize)) % 2 == 0) continue;
            graphics.FillRectangle(dark, x, y, Math.Min(cellSize, bounds.Right - x), Math.Min(cellSize, bounds.Bottom - y));
        }
    }
}

internal static class AspectRatioDisplay
{
    public static Rectangle CalculateContainRectangle(Size sourceSize, Rectangle bounds)
    {
        if (sourceSize.Width <= 0 ||
            sourceSize.Height <= 0 ||
            bounds.Width <= 0 ||
            bounds.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var scale = Math.Min(
            bounds.Width / (double)sourceSize.Width,
            bounds.Height / (double)sourceSize.Height);
        var width = Math.Min(bounds.Width, Math.Max(1, (int)Math.Round(sourceSize.Width * scale)));
        var height = Math.Min(bounds.Height, Math.Max(1, (int)Math.Round(sourceSize.Height * scale)));
        return new Rectangle(
            bounds.Left + (bounds.Width - width) / 2,
            bounds.Top + (bounds.Height - height) / 2,
            width,
            height);
    }
}
