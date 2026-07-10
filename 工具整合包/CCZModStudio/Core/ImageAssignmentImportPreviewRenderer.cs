using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ImageAssignmentImportPreviewRenderer
{
    private const int PlaceholderWidth = 320;
    private const int PlaceholderHeight = 220;
    private static readonly Size DefaultPreviewSize = new(PlaceholderWidth, PlaceholderHeight);

    private readonly E5ImageReplaceService _e5Replace = new();

    public Bitmap RenderCurrentTarget(CczProject project, ImageAssignmentImportPreviewItem item)
        => RenderCurrentTarget(project, item, DefaultPreviewSize);

    public Bitmap RenderCurrentTarget(CczProject project, ImageAssignmentImportPreviewItem item, Size targetSize)
    {
        targetSize = NormalizeTargetSize(targetSize);
        try
        {
            var targetPath = ResolveTargetPath(project, item);
            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
            {
                return RenderPlaceholder("目标文件不存在。", targetSize);
            }

            var bytes = _e5Replace.ReadEntryBytes(targetPath, item.TargetImageNumber);
            using var image = TryDecodeImage(bytes);
            if (image == null)
            {
                return RenderPlaceholder("当前目标不是标准图片格式，无法直接预览。", targetSize);
            }

            return RenderImageForItem(image, item, targetSize);
        }
        catch (Exception ex)
        {
            return RenderPlaceholder("读取当前目标失败：" + ex.Message, targetSize);
        }
    }

    public Bitmap RenderOutputPreview(ImageAssignmentImportPreviewItem item)
        => RenderOutputPreview(item, DefaultPreviewSize);

    public Bitmap RenderOutputPreview(ImageAssignmentImportPreviewItem item, Size targetSize)
    {
        targetSize = NormalizeTargetSize(targetSize);
        try
        {
            if (item.OutputBytes is not { Length: > 0 })
            {
                return RenderPlaceholder("没有可预览的导入后图像。", targetSize);
            }

            using var image = TryDecodeImage(item.OutputBytes);
            if (image == null)
            {
                return RenderPlaceholder("导入后图像不是标准图片格式。", targetSize);
            }

            return RenderImageForItem(image, item, targetSize);
        }
        catch (Exception ex)
        {
            return RenderPlaceholder("渲染导入后图像失败：" + ex.Message, targetSize);
        }
    }

    public Bitmap RenderStripContactSheet(Bitmap strip, int frameWidth, int frameHeight)
        => RenderStripContactSheet(strip, frameWidth, frameHeight, DefaultPreviewSize);

    public Bitmap RenderStripContactSheet(Bitmap strip, int frameWidth, int frameHeight, Size targetSize)
    {
        targetSize = NormalizeTargetSize(targetSize);
        frameWidth = Math.Max(1, frameWidth);
        frameHeight = Math.Max(1, frameHeight);
        var frameCount = Math.Max(1, strip.Height / frameHeight);
        var layout = ResolveContactSheetLayout(targetSize, frameCount, frameWidth, frameHeight);
        var sheet = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(28, 30, 32));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var border = new Pen(Color.FromArgb(95, 105, 112));
        using var labelBrush = new SolidBrush(Color.Gainsboro);
        using var labelFont = new Font("Microsoft YaHei UI", layout.LabelFontSize, FontStyle.Regular, GraphicsUnit.Point);

        for (var i = 0; i < frameCount; i++)
        {
            var sourceRect = new Rectangle(
                0,
                i * frameHeight,
                Math.Min(frameWidth, strip.Width),
                Math.Min(frameHeight, strip.Height - i * frameHeight));
            var col = i % layout.Columns;
            var row = i / layout.Columns;
            var x = layout.OriginX + col * layout.CellWidth + layout.CellPadding;
            var y = layout.OriginY + row * layout.CellHeight + layout.CellPadding;
            var frameRect = new Rectangle(x, y, layout.FrameDrawWidth, layout.FrameDrawHeight);
            g.DrawRectangle(border, frameRect.Left - 1, frameRect.Top - 1, frameRect.Width + 1, frameRect.Height + 1);
            g.DrawImage(strip, frameRect, sourceRect, GraphicsUnit.Pixel);
            g.DrawString((i + 1).ToString(), labelFont, labelBrush, x, y + layout.FrameDrawHeight + 1);
        }

        return sheet;
    }

    public Bitmap RenderPlaceholder(string message)
        => RenderPlaceholder(message, DefaultPreviewSize);

    public Bitmap RenderPlaceholder(string message, Size targetSize)
    {
        targetSize = NormalizeTargetSize(targetSize);
        var bitmap = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(34, 38, 42));
        using var border = new Pen(Color.FromArgb(95, 105, 112));
        using var textBrush = new SolidBrush(Color.Gainsboro);
        using var font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        g.DrawRectangle(border, 0, 0, bitmap.Width - 1, bitmap.Height - 1);
        g.DrawString(message, font, textBrush, new RectangleF(14, 14, Math.Max(1, bitmap.Width - 28), Math.Max(1, bitmap.Height - 28)));
        return bitmap;
    }

    private Bitmap RenderImageForItem(Bitmap image, ImageAssignmentImportPreviewItem item, Size targetSize)
    {
        if (item.Kind.Equals("Face", StringComparison.OrdinalIgnoreCase))
        {
            return RenderSingleImageCanvas(image, targetSize, InterpolationMode.HighQualityBicubic);
        }

        if (item.FrameWidth > 0 &&
            item.FrameHeight > 0 &&
            image.Height >= item.FrameHeight * 2 &&
            image.Width >= item.FrameWidth)
        {
            return RenderStripContactSheet(image, item.FrameWidth, item.FrameHeight, targetSize);
        }

        return RenderSingleImageCanvas(image, targetSize, InterpolationMode.NearestNeighbor);
    }

    private static Bitmap RenderSingleImageCanvas(Bitmap image, Size targetSize, InterpolationMode interpolationMode)
    {
        targetSize = NormalizeTargetSize(targetSize);
        var canvas = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.FromArgb(28, 30, 32));
        g.InterpolationMode = interpolationMode;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var scale = Math.Min(
            targetSize.Width / (double)Math.Max(1, image.Width),
            targetSize.Height / (double)Math.Max(1, image.Height));
        var width = Math.Max(1, (int)Math.Floor(image.Width * scale));
        var height = Math.Max(1, (int)Math.Floor(image.Height * scale));
        var x = (targetSize.Width - width) / 2;
        var y = (targetSize.Height - height) / 2;
        using var border = new Pen(Color.FromArgb(95, 105, 112));
        g.DrawImage(image, new Rectangle(x, y, width, height), new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        g.DrawRectangle(border, x, y, width - 1, height - 1);
        return canvas;
    }

    private static ContactSheetLayout ResolveContactSheetLayout(Size targetSize, int frameCount, int frameWidth, int frameHeight)
    {
        var best = default(ContactSheetLayout);
        var bestScore = double.MinValue;
        var maxColumns = Math.Max(1, Math.Min(frameCount, 12));

        for (var columns = 1; columns <= maxColumns; columns++)
        {
            var rows = (int)Math.Ceiling(frameCount / (double)columns);
            var padding = targetSize.Width >= 360 && targetSize.Height >= 360 ? 6 : 4;
            var labelHeight = targetSize.Height >= 320 ? 18 : 14;
            var availableWidth = targetSize.Width - padding * 2 * columns;
            var availableHeight = targetSize.Height - (padding * 2 + labelHeight) * rows;
            if (availableWidth <= 0 || availableHeight <= 0) continue;

            var scaleX = availableWidth / (double)(columns * frameWidth);
            var scaleY = availableHeight / (double)(rows * frameHeight);
            var scale = Math.Min(scaleX, scaleY);
            if (scale <= 0) continue;

            if (scale >= 1)
            {
                scale = Math.Max(1, Math.Floor(scale));
            }

            var drawWidth = Math.Max(1, (int)Math.Floor(frameWidth * scale));
            var drawHeight = Math.Max(1, (int)Math.Floor(frameHeight * scale));
            var cellWidth = drawWidth + padding * 2;
            var cellHeight = drawHeight + padding * 2 + labelHeight;
            var usedWidth = cellWidth * columns;
            var usedHeight = cellHeight * rows;
            if (usedWidth > targetSize.Width || usedHeight > targetSize.Height) continue;

            var fillRatio = usedWidth * usedHeight / (double)(targetSize.Width * targetSize.Height);
            var frameArea = drawWidth * drawHeight;
            var aspectPenalty = Math.Abs(
                usedWidth / (double)Math.Max(1, usedHeight) -
                targetSize.Width / (double)Math.Max(1, targetSize.Height));
            var score = frameArea * 1000.0 + fillRatio * 100.0 - aspectPenalty;
            if (score <= bestScore) continue;

            bestScore = score;
            best = new ContactSheetLayout(
                columns,
                rows,
                cellWidth,
                cellHeight,
                drawWidth,
                drawHeight,
                padding,
                Math.Max(7, Math.Min(11, (int)Math.Round(labelHeight * 0.55))),
                Math.Max(0, (targetSize.Width - usedWidth) / 2),
                Math.Max(0, (targetSize.Height - usedHeight) / 2));
        }

        if (best.Columns > 0) return best;

        var fallbackScale = Math.Max(0.1, Math.Min(targetSize.Width / (double)frameWidth, targetSize.Height / (double)frameHeight));
        return new ContactSheetLayout(
            1,
            frameCount,
            targetSize.Width,
            targetSize.Height,
            Math.Max(1, (int)Math.Floor(frameWidth * fallbackScale)),
            Math.Max(1, (int)Math.Floor(frameHeight * fallbackScale)),
            2,
            7,
            0,
            0);
    }

    private static Size NormalizeTargetSize(Size targetSize)
        => new(Math.Max(1, targetSize.Width), Math.Max(1, targetSize.Height));

    private static Bitmap? TryDecodeImage(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveTargetPath(CczProject project, ImageAssignmentImportPreviewItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.TargetPath)) return item.TargetPath;
        if (!string.IsNullOrWhiteSpace(item.TargetFileName))
        {
            return CharacterImageResourceService.ResolveGameFile(project, item.TargetFileName);
        }

        return string.Empty;
    }

    private readonly record struct ContactSheetLayout(
        int Columns,
        int Rows,
        int CellWidth,
        int CellHeight,
        int FrameDrawWidth,
        int FrameDrawHeight,
        int CellPadding,
        int LabelFontSize,
        int OriginX,
        int OriginY);
}
