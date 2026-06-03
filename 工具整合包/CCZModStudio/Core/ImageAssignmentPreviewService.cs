using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// Preview for person face/R/S image assignments.
/// Face uses the tutorial Data->Face.e5 mapping; R/S are explained through Pmapobj.e5 and Unit_*.e5.
/// </summary>
public sealed class ImageAssignmentPreviewService
{
    private const int PreviewWidth = 420;
    private const int PreviewHeight = 300;
    private readonly Dictionary<string, IReadOnlyList<PngSlice>> _facePngIndexCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<ImageSlice>> _e5ImageSliceCache = new(StringComparer.OrdinalIgnoreCase);

    public Bitmap RenderResourcePreview(CczProject project, string prefix, int id, string personName, int? faceId = null)
    {
        prefix = NormalizePrefix(prefix);
        var resolver = new CharacterImageResourceService();
        var status = prefix == "S" ? resolver.BuildSStatus(project, id) : resolver.BuildRStatus(project, id);
        var title = prefix == "S" ? $"S 形象 {id}" : $"R 形象 {id}";
        var bitmap = new Bitmap(PreviewWidth, PreviewHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(28, 30, 32));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var baseFont = SystemFonts.MessageBoxFont ?? Control.DefaultFont;
        using var titleFont = new Font(baseFont, FontStyle.Bold);
        using var normalFont = new Font(baseFont, FontStyle.Regular);
        using var smallFont = new Font(baseFont.FontFamily, 8, FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.White);
        using var textBrush = new SolidBrush(Color.Gainsboro);
        using var warnBrush = new SolidBrush(Color.FromArgb(255, 210, 120));
        using var missingBrush = new SolidBrush(Color.FromArgb(255, 140, 140));
        using var accentBrush = new SolidBrush(Color.FromArgb(122, 184, 255));
        using var borderPen = new Pen(Color.FromArgb(110, 120, 128));

        var faceCaption = string.Empty;
        var faceBitmap = faceId.HasValue ? TryLoadMappedFaceImage(project, faceId.Value, out faceCaption) : null;
        var imageBitmap = TryLoadMappedE5Image(project, prefix, id, out var imageCaption);
        try
        {
            g.DrawString($"{personName}  {title}", titleFont, titleBrush, 12, 10);
            if (faceBitmap != null)
            {
                using var faceBack = new SolidBrush(Color.FromArgb(16, 18, 20));
                g.FillRectangle(faceBack, 12, 48, 104, 104);
                g.DrawRectangle(borderPen, 12, 48, 104, 104);
                g.DrawImage(faceBitmap, new Rectangle(14, 50, 100, 100));
                g.DrawString(faceCaption, smallFont, accentBrush, 18, 156);
            }

            var textureRect = faceBitmap == null
                ? new Rectangle(12, 48, bitmap.Width - 24, 138)
                : new Rectangle(124, 48, bitmap.Width - 136, 138);

            using var fill = new SolidBrush(CharacterImageResourceService.IsMissingStatus(status.Status)
                ? Color.FromArgb(86, 37, 37)
                : Color.FromArgb(38, 54, 68));
            g.FillRectangle(fill, textureRect);
            g.DrawRectangle(borderPen, textureRect);

            if (imageBitmap != null)
            {
                var imageRect = new Rectangle(textureRect.Left + 6, textureRect.Top + 6, textureRect.Width - 12, textureRect.Height - 12);
                DrawCenteredImage(g, imageBitmap, imageRect, borderPen);
                if (!string.IsNullOrWhiteSpace(imageCaption))
                {
                    g.DrawString(imageCaption, smallFont, accentBrush, textureRect.Left + 10, textureRect.Bottom - 18);
                }
            }
            else
            {
                var statusBrush = CharacterImageResourceService.IsMissingStatus(status.Status) ? missingBrush : accentBrush;
                g.DrawString(status.Status, titleFont, statusBrush, textureRect.Left + 14, textureRect.Top + 12);
                g.DrawString(status.ResourceName, normalFont, titleBrush, new RectangleF(textureRect.Left + 14, textureRect.Top + 42, textureRect.Width - 28, 28));
                g.DrawString(status.Detail, smallFont, textBrush, new RectangleF(textureRect.Left + 14, textureRect.Top + 74, textureRect.Width - 28, 54));
            }

            var fileText = File.Exists(status.Path)
                ? $"已找到：{status.Path}"
                : $"未找到：{status.Path}";
            g.DrawString(fileText, smallFont, CharacterImageResourceService.IsMissingStatus(status.Status) ? warnBrush : textBrush,
                new RectangleF(18, 194, bitmap.Width - 36, 36));
            g.DrawString(prefix == "R"
                    ? "读取口径：Data/Ekd5 的 R 形象号 n -> Pmapobj.e5 图 2n+1 / 2n+2。当前不把 RS\\R_XX.eex 当人物图像。"
                    : "读取口径：S 形象号普通 0-139，特殊 140-156；资源候选 Unit_atk/mov/spc.e5。当前不把 RS\\S_XX.eex 当人物图像。",
                smallFont,
                warnBrush,
                new RectangleF(18, 236, bitmap.Width - 36, 44));
            return bitmap;
        }
        finally
        {
            faceBitmap?.Dispose();
            imageBitmap?.Dispose();
        }
    }

    public Bitmap? TryRenderFaceImage(CczProject project, int dataFaceId)
    {
        return TryLoadMappedFaceImage(project, dataFaceId, out _);
    }

    public Bitmap? TryRenderCharacterResourceImage(CczProject project, string prefix, int id)
    {
        prefix = NormalizePrefix(prefix);
        // R=0 / S=0 表示使用普通形象（与兵种/初始设定相关）。当前工具尚未实现“按兵种取默认帧”的权威选择，
        // 因此对 0 号仅返回 null（预览区留空），避免展示一个可能误导的候选切片。
        if (id == 0) return null;
        return TryLoadMappedE5Image(project, prefix, id, out _);
    }

    private static void DrawCenteredImage(Graphics g, Image image, Rectangle rect, Pen borderPen)
    {
        using var back = new SolidBrush(Color.FromArgb(16, 18, 20));
        g.FillRectangle(back, rect);
        g.DrawRectangle(borderPen, rect);

        var scale = Math.Min(rect.Width / (float)image.Width, rect.Height / (float)image.Height);
        var w = (int)Math.Round(image.Width * scale);
        var h = (int)Math.Round(image.Height * scale);
        var x = rect.Left + (rect.Width - w) / 2;
        var y = rect.Top + (rect.Height - h) / 2;
        g.DrawImage(image, new Rectangle(x, y, w, h));
    }

    private Bitmap? TryLoadMappedE5Image(CczProject project, string prefix, int id, out string caption)
    {
        caption = string.Empty;
        try
        {
            if (prefix == "R")
            {
                // Tutorial mapping: R id n => Pmapobj.e5 image # (2n+1) / (2n+2), 1-based.
                var file = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
                if (!File.Exists(file)) return null;
                // R n => front = 2n+1 (1-based). Convert to 0-based slice index.
                var sliceIndex = checked(id * 2);
                var slices = GetE5ImageSlices(file, ImageSliceKind.Jpeg);
                if (slices.Count == 0) return null;
                if (sliceIndex < 0 || sliceIndex >= slices.Count) sliceIndex = 0;
                caption = $"Pmapobj #{sliceIndex + 1}";
                return LoadJpegSlice(file, slices[sliceIndex]);
            }

            // S preview is best-effort: show one of Unit_* slices at the same index if possible.
            // (Full S-frame selection by job/animation is not confirmed yet.)
            foreach (var unit in new[] { "Unit_atk.e5", "Unit_mov.e5", "Unit_spc.e5" })
            {
                var file = CharacterImageResourceService.ResolveGameFile(project, unit);
                if (!File.Exists(file)) continue;
                var slices = GetE5ImageSlices(file, ImageSliceKind.Bmp);
                if (slices.Count == 0) continue;
                var sliceIndex = Math.Clamp(id, 0, slices.Count - 1);
                caption = $"{unit} #{sliceIndex + 1}";
                return LoadFixedLengthSlice(file, slices[sliceIndex]);
            }

            return null;
        }
        catch
        {
            caption = string.Empty;
            return null;
        }
    }

    private IReadOnlyList<ImageSlice> GetE5ImageSlices(string path, ImageSliceKind kind)
    {
        path = Path.GetFullPath(path);
        var key = $"{path}::{kind}";
        if (_e5ImageSliceCache.TryGetValue(key, out var cached)) return cached;

        var bytes = File.ReadAllBytes(path);
        var payloadOffset = bytes.Length >= 16 && bytes[0] == (byte)'L' && bytes[1] == (byte)'s' && bytes[2] == (byte)'1' ? 16 : 0;
        var result = kind == ImageSliceKind.Bmp
            ? ScanBmpSlices(bytes, payloadOffset)
            : ScanJpegSlices(bytes, payloadOffset);

        _e5ImageSliceCache[key] = result;
        return result;
    }

    private static List<ImageSlice> ScanBmpSlices(byte[] bytes, int payloadOffset)
    {
        var list = new List<ImageSlice>();
        for (var i = payloadOffset; i + 6 < bytes.Length; i++)
        {
            if (bytes[i] != 0x42 || bytes[i + 1] != 0x4D) continue; // "BM"
            var size = BitConverter.ToInt32(bytes, i + 2);
            if (size <= 0 || i + size > bytes.Length) continue;
            list.Add(new ImageSlice(i, size));
            i += Math.Max(0, size - 1);
        }
        return list;
    }

    private static List<ImageSlice> ScanJpegSlices(byte[] bytes, int payloadOffset)
    {
        var list = new List<ImageSlice>();
        for (var i = payloadOffset; i + 3 < bytes.Length; i++)
        {
            if (bytes[i] != 0xFF || bytes[i + 1] != 0xD8 || bytes[i + 2] != 0xFF) continue; // SOI
            // We don't slice by EOI only; JPEG may contain embedded thumbnails and trailing bytes.
            list.Add(new ImageSlice(i, 0));
        }
        return list;
    }

    private static Bitmap? LoadFixedLengthSlice(string path, ImageSlice slice)
    {
        var bytes = File.ReadAllBytes(path);
        if (slice.Length <= 0 || slice.Offset < 0 || slice.Offset + slice.Length > bytes.Length) return null;
        using var ms = new MemoryStream(bytes, slice.Offset, slice.Length, writable: false);
        using var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
        return new Bitmap(img);
    }

    private static Bitmap? LoadJpegSlice(string path, ImageSlice slice)
    {
        var bytes = File.ReadAllBytes(path);
        if (slice.Offset < 0 || slice.Offset >= bytes.Length) return null;
        var len = Math.Min(200_000, bytes.Length - slice.Offset);
        using var ms = new MemoryStream();
        ms.Write(bytes, slice.Offset, len);
        ms.Position = 0;
        using var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
        return new Bitmap(img);
    }

    private enum ImageSliceKind { Bmp, Jpeg }
    private sealed record ImageSlice(int Offset, int Length);

    public string BuildResourceInfo(CczProject project, string prefix, int id, string personName, int? faceId = null)
    {
        prefix = NormalizePrefix(prefix);
        var resolver = new CharacterImageResourceService();
        var status = prefix == "S" ? resolver.BuildSStatus(project, id) : resolver.BuildRStatus(project, id);
        var toolDir = ResolveImageAssignerToolDirectory(project);
        var assignerConfig = LoadImageAssignerConfig(project);
        var sceneConfig = LoadSceneEditorConfig(project);
        var sb = new StringBuilder();
        sb.AppendLine($"{personName}  {prefix} 形象编号：{id}");
        if (faceId.HasValue)
        {
            var facePath = ResolveFaceFile(project);
            var faceCount = facePath != null ? GetFacePngIndex(facePath).Count : 0;
            sb.AppendLine("头像映射：" + resolver.BuildFaceHint(project, faceId.Value));
            sb.AppendLine($"头像预览：{(facePath == null ? "未找到 E5\\Face.e5" : $"E5\\Face.e5 内 PNG 候选 {faceCount} 张；按教程映射取图")}");
        }
        sb.AppendLine($"资源定位：{status.Status}：{status.ResourceName}");
        sb.AppendLine($"资源路径：{status.Path}");
        sb.AppendLine($"解释：{status.Detail}");
        sb.AppendLine("人物 R/S 编号来源：Ekd5.exe 中的人物 R/S 指定表，不是 E5S 存档信息，也不是 RS\\R_XX.eex / S_XX.eex 人物图像。");
        sb.AppendLine($"B形象指定器目录：{toolDir ?? "未找到 B形象指定器\\形象指定器6.5"}");
        sb.AppendLine($"B形象指定器配置：FileHead={assignerConfig.GetValueOrDefault("FileHead", "未找到")}，RFileHead={assignerConfig.GetValueOrDefault("RFileHead", "未找到")}，UserPath2={assignerConfig.GetValueOrDefault("UserPath2", "未找到")}");
        sb.AppendLine($"新剧本编辑器配置：RSMax={sceneConfig.GetValueOrDefault("RSMax", "未找到")}，ExePath={sceneConfig.GetValueOrDefault("ExePath", "未找到")}");

        if (!File.Exists(status.Path))
        {
            sb.AppendLine("资源文件未定位：请检查 Pmapobj.e5 / Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5 是否在项目根目录。");
            return sb.ToString();
        }

        var info = new FileInfo(status.Path);
        sb.AppendLine($"文件状态：已找到，大小 {info.Length:N0} 字节，修改时间 {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(prefix == "R"
            ? "说明：R 形象当前按 Pmapobj.e5 正反图号定位；封包内图片重排/替换尚未开放写入。"
            : "说明：S 形象当前按 Unit_*.e5 普通/特殊编号解释；按职业/兵种取帧与三套动作封包解码仍需继续验证。");
        return sb.ToString();
    }

    private static void DrawByteTexture(Graphics g, byte[] bytes, EexPreviewMetadata meta, IReadOnlyList<Color> palette, Rectangle rect)
    {
        using var frameBack = new SolidBrush(Color.FromArgb(18, 20, 22));
        using var borderPen = new Pen(Color.FromArgb(120, 128, 136));
        g.FillRectangle(frameBack, rect);
        g.DrawRectangle(borderPen, rect);
        if (bytes.Length == 0) return;

        var dataStart = meta.PlausibleOffsets.FirstOrDefault(x => x >= meta.HeaderSize && x < bytes.Length);
        if (dataStart <= 0) dataStart = Math.Min(Math.Max(meta.HeaderSize, 14), bytes.Length - 1);
        var dataLength = Math.Max(1, bytes.Length - dataStart);

        using var texture = new Bitmap(140, 72, PixelFormat.Format32bppArgb);
        for (var y = 0; y < texture.Height; y++)
        {
            for (var x = 0; x < texture.Width; x++)
            {
                var sample = dataStart + (int)(((long)(y * texture.Width + x) * dataLength) / (texture.Width * texture.Height));
                if (sample >= bytes.Length) sample = bytes.Length - 1;
                var value = bytes[sample];
                var color = palette.Count > 0 ? palette[value % palette.Count] : DefaultColor(value);
                texture.SetPixel(x, y, Color.FromArgb(232, color));
            }
        }

        g.DrawImage(texture, rect);
    }

    private static void DrawSectionBar(Graphics g, EexPreviewMetadata meta, int length, Rectangle rect)
    {
        using var back = new SolidBrush(Color.FromArgb(44, 48, 52));
        using var border = new Pen(Color.FromArgb(120, 128, 136));
        g.FillRectangle(back, rect);
        if (length <= 0)
        {
            g.DrawRectangle(border, rect);
            return;
        }

        var colors = new[]
        {
            Color.FromArgb(86, 156, 214),
            Color.FromArgb(197, 134, 192),
            Color.FromArgb(78, 201, 176),
            Color.FromArgb(220, 220, 170),
            Color.FromArgb(206, 145, 120)
        };
        var offsets = meta.PlausibleOffsets.Where(x => x >= 0 && x < length).Distinct().OrderBy(x => x).ToList();
        if (offsets.Count == 0) offsets.Add(Math.Min(meta.HeaderSize, Math.Max(0, length - 1)));
        offsets.Add(length);
        var start = 0;
        for (var i = 0; i < offsets.Count; i++)
        {
            var end = offsets[i];
            if (end <= start) continue;
            var x = rect.Left + (int)Math.Round(start * rect.Width / (double)length);
            var w = Math.Max(1, (int)Math.Round((end - start) * rect.Width / (double)length));
            using var brush = new SolidBrush(Color.FromArgb(210, colors[i % colors.Length]));
            g.FillRectangle(brush, x, rect.Top, Math.Min(w, rect.Right - x), rect.Height);
            start = end;
        }
        g.DrawRectangle(border, rect);
    }

    private static Color DefaultColor(byte value)
    {
        var r = (value * 73 + 40) % 256;
        var gr = (value * 37 + 90) % 256;
        var b = (value * 19 + 140) % 256;
        return Color.FromArgb(r, gr, b);
    }

    private static EexPreviewMetadata ReadMetadata(byte[] bytes)
    {
        var magic = bytes.Length >= 14 && bytes[0] == (byte)'E' && bytes[1] == (byte)'E' && bytes[2] == (byte)'X' && bytes[3] == 0;
        var headerSize = 14;
        var offsets = new List<int>();
        if (magic)
        {
            headerSize = checked((int)BitConverter.ToUInt32(bytes, 10));
            if (headerSize < 14 || headerSize > 256 || headerSize > bytes.Length)
            {
                headerSize = 14;
            }

            for (var offset = 14; offset + 4 <= headerSize && offset + 4 <= bytes.Length; offset += 4)
            {
                var value = checked((int)BitConverter.ToUInt32(bytes, offset));
                if (value >= headerSize && value < bytes.Length)
                {
                    offsets.Add(value);
                }
            }
        }

        var textHints = BinaryTextScanner
            .ScanGbkNullTerminatedStrings(bytes, minByteLength: 5, maxItems: 5)
            .Select(x => x.Length > 24 ? x[..24] + "…" : x)
            .ToList();

        return new EexPreviewMetadata(
            magic,
            bytes.Length >= 6 ? "0x" + BitConverter.ToUInt16(bytes, 4).ToString("X4", CultureInfo.InvariantCulture) : "??",
            headerSize,
            offsets.Distinct().OrderBy(x => x).ToList(),
            textHints);
    }

    private static IReadOnlyList<Color> LoadSceneEditorPalette(CczProject project)
    {
        var toolDir = ResolveSceneEditorToolDirectory(project);
        if (toolDir == null) return Array.Empty<Color>();
        var path = Path.Combine(toolDir, "CczCustom.ini");
        if (!File.Exists(path)) return Array.Empty<Color>();

        var result = new List<Color>();
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3) continue;
            if (byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) &&
                byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) &&
                byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            {
                result.Add(Color.FromArgb(r, g, b));
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> LoadSceneEditorConfig(CczProject project)
    {
        var toolDir = ResolveSceneEditorToolDirectory(project);
        if (toolDir == null) return new Dictionary<string, string>();
        return LoadIniKeyValues(Path.Combine(toolDir, "CczSceneEditor2.ini"));
    }

    private static IReadOnlyDictionary<string, string> LoadImageAssignerConfig(CczProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.ImageAssignerSystemIniPath) &&
            File.Exists(project.ImageAssignerSystemIniPath))
        {
            return LoadIniKeyValues(project.ImageAssignerSystemIniPath);
        }

        var toolDir = ResolveImageAssignerToolDirectory(project);
        if (toolDir == null) return new Dictionary<string, string>();
        return LoadIniKeyValues(Path.Combine(toolDir, "System.ini"));
    }

    private static IReadOnlyDictionary<string, string> LoadIniKeyValues(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, string>();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("[", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal)) continue;
            var comment = line.IndexOf(';');
            if (comment >= 0) line = line[..comment].Trim();
            var index = line.IndexOf('=', StringComparison.Ordinal);
            if (index <= 0) continue;
            result[line[..index].Trim()] = line[(index + 1)..].Trim();
        }

        return result;
    }

    private Bitmap? TryLoadFaceImage(CczProject project, int faceId)
    {
        var facePath = ResolveFaceFile(project);
        if (facePath == null) return null;
        var slices = GetFacePngIndex(facePath);
        if (faceId < 0 || faceId >= slices.Count) return null;

        var slice = slices[faceId];
        var bytes = File.ReadAllBytes(facePath);
        using var stream = new MemoryStream(bytes, slice.Offset, slice.Length, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
        return new Bitmap(image);
    }

    private Bitmap? TryLoadMappedFaceImage(CczProject project, int dataFaceId, out string caption)
    {
        caption = $"头像号 {dataFaceId}";
        var facePath = ResolveFaceFile(project);
        if (facePath == null) return null;

        var mapping = new CharacterImageResourceService().MapFaceId(dataFaceId);
        var slices = GetFacePngIndex(facePath);
        if (slices.Count == 0) return null;

        // 教程口径：Face.e5 图号为 1-based；slice 下标为 0-based。
        var preferredFaceNumber = mapping.FaceImageNumbers.FirstOrDefault();
        var index = preferredFaceNumber - 1;
        if (index < 0 || index >= slices.Count) index = 0;

        caption = mapping.FaceImageNumbers.Count == 1
            ? $"头像号 {dataFaceId} -> Face#{preferredFaceNumber}"
            : $"头像号 {dataFaceId} -> Face#{mapping.FaceImageNumbers.First()}-{mapping.FaceImageNumbers.Last()}";

        var slice = slices[index];
        var bytes = File.ReadAllBytes(facePath);
        using var stream = new MemoryStream(bytes, slice.Offset, slice.Length, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
        return new Bitmap(image);
    }

    private IReadOnlyList<PngSlice> GetFacePngIndex(string facePath)
    {
        facePath = Path.GetFullPath(facePath);
        if (_facePngIndexCache.TryGetValue(facePath, out var cached)) return cached;
        var bytes = File.ReadAllBytes(facePath);
        var result = new List<PngSlice>();
        for (var offset = 0; offset + PngMagic.Length <= bytes.Length; offset++)
        {
            if (!Matches(bytes, offset, PngMagic)) continue;
            if (TryReadPngLength(bytes, offset, out var length))
            {
                result.Add(new PngSlice(offset, length));
                offset += Math.Max(0, length - 1);
            }
        }

        _facePngIndexCache[facePath] = result;
        return result;
    }

    private static string? ResolveFaceFile(CczProject project)
    {
        var candidates = new[]
        {
            Path.Combine(project.GameRoot, "E5", "Face.e5"),
            Path.Combine(project.GameRoot, "Face.e5"),
            Path.Combine(Directory.GetParent(project.GameRoot)?.FullName ?? project.WorkspaceRoot, "E5", "Face.e5")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool TryReadPngLength(byte[] bytes, int offset, out int length)
    {
        length = 0;
        var pos = offset + PngMagic.Length;
        while (pos + 12 <= bytes.Length)
        {
            var chunkLength = ReadBigEndianInt32(bytes, pos);
            if (chunkLength < 0 || chunkLength > bytes.Length - pos - 12) return false;
            var typeOffset = pos + 4;
            pos += 12 + chunkLength;
            if (bytes[typeOffset] == (byte)'I' &&
                bytes[typeOffset + 1] == (byte)'E' &&
                bytes[typeOffset + 2] == (byte)'N' &&
                bytes[typeOffset + 3] == (byte)'D')
            {
                length = pos - offset;
                return length > PngMagic.Length;
            }
        }

        return false;
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset)
        => (bytes[offset] << 24) |
           (bytes[offset + 1] << 16) |
           (bytes[offset + 2] << 8) |
           bytes[offset + 3];

    private static bool Matches(byte[] bytes, int offset, byte[] magic)
    {
        for (var i = 0; i < magic.Length; i++)
        {
            if (bytes[offset + i] != magic[i]) return false;
        }

        return true;
    }

    private static string? ResolveSceneEditorToolDirectory(CczProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.SceneEditorDirectory) &&
            Directory.Exists(project.SceneEditorDirectory))
        {
            return project.SceneEditorDirectory;
        }

        return ProjectDetector.FindPortableDirectory(
            project,
            "a新剧本编辑器v0.23",
            Path.Combine("老版游戏制作工具", "a新剧本编辑器v0.23"),
            "a新剧本编辑器v0.23");
    }

    private static string? ResolveImageAssignerToolDirectory(CczProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.ImageAssignerDirectory) &&
            Directory.Exists(project.ImageAssignerDirectory))
        {
            return project.ImageAssignerDirectory;
        }

        return ProjectDetector.FindPortableDirectory(
            project,
            "形象指定器6.5",
            Path.Combine("老版游戏制作工具", "B形象指定器", "形象指定器6.5"),
            Path.Combine("B形象指定器", "形象指定器6.5"));
    }

    private static string NormalizePrefix(string prefix)
    {
        if (prefix.Equals("S", StringComparison.OrdinalIgnoreCase)) return "S";
        return "R";
    }

    private static readonly byte[] PngMagic = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

    private sealed record PngSlice(int Offset, int Length);

    private sealed record EexPreviewMetadata(
        bool MagicValid,
        string VersionHex,
        int HeaderSize,
        IReadOnlyList<int> PlausibleOffsets,
        IReadOnlyList<string> TextHints);
}
