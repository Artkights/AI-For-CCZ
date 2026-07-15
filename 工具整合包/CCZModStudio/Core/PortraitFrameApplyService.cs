using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class PortraitFrameApplyService
{
    private const int ExpectedFaceWidth = 120;
    private const int ExpectedFaceHeight = 120;
    private static readonly byte[] PngMagic = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly CharacterImageResourceService _resourceService = new();
    private readonly E5ImageReplaceService _e5Replace = new();
    private readonly E5ImageRenderService _renderService = new();

    public PortraitFrameApplyPreviewResult Preview(CczProject project, PortraitFrameApplyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FramePath))
        {
            throw new InvalidOperationException("请先选择头像框图片。");
        }

        if (request.TargetRows.Count == 0)
        {
            throw new InvalidOperationException("没有可套用头像框的目标行。");
        }

        var targetPath = CharacterImageResourceService.ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", "Face.e5");
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException("找不到 Face.e5，无法给头像套框。", targetPath);
        }

        var skipped = new List<BatchImageImportSkippedItem>();
        var warnings = new List<string>
        {
            "头像框导入只写 Face.e5 小头像；不会同步 Tou.dll 真彩头像资源。"
        };

        if (!TryLoadFrame(request.FramePath, skipped, warnings, out var frame))
        {
            return BuildPreviewResult(project, request, targetPath, Array.Empty<PortraitFrameApplyItemPreview>(), skipped, warnings, null);
        }

        using (frame)
        {
            var uniqueTargets = BuildUniqueTargets(request.TargetRows, warnings);
            var items = new List<PortraitFrameApplyItemPreview>();
            var e5Requests = new List<E5ImageBatchReplaceRequest>();
            foreach (var target in uniqueTargets)
            {
                if (target.FaceId < 0)
                {
                    skipped.Add(Skip(
                        target.FaceId.ToString(CultureInfo.InvariantCulture),
                        targetPath,
                        BatchImageImportSkipReasons.InvalidFormat,
                        "头像编号不能小于 0"));
                    continue;
                }

                var mapping = ResolveWritableFaceMapping(target.FaceId);
                if (!string.IsNullOrWhiteSpace(mapping.Warning))
                {
                    warnings.Add(mapping.Warning);
                }

                var outputBytesByImageNumber = new Dictionary<int, byte[]>();
                foreach (var imageNumber in mapping.ImageNumbers)
                {
                    if (!TryBuildFramedFaceBytes(targetPath, imageNumber, frame, out var outputBytes, out var error))
                    {
                        skipped.Add(Skip(
                            target.FaceId.ToString(CultureInfo.InvariantCulture),
                            targetPath,
                            BatchImageImportSkipReasons.InvalidFormat,
                            $"Face.e5 #{imageNumber}: {error}"));
                        continue;
                    }

                    outputBytesByImageNumber[imageNumber] = outputBytes;
                    e5Requests.Add(new E5ImageBatchReplaceRequest
                    {
                        ImageNumber = imageNumber,
                        SourceBytes = outputBytes,
                        SourceLabel = $"{request.FramePath} + Face.e5 #{imageNumber} -> framed 120x120 PNG",
                        OperationKind = "portrait frame apply"
                    });
                }

                if (outputBytesByImageNumber.Count == 0)
                {
                    continue;
                }

                items.Add(new PortraitFrameApplyItemPreview
                {
                    RowId = target.RowId,
                    DisplayName = target.DisplayName,
                    FaceId = target.FaceId,
                    FramePath = request.FramePath,
                    TargetImageNumbers = outputBytesByImageNumber.Keys.OrderBy(value => value).ToArray(),
                    OutputWidth = ExpectedFaceWidth,
                    OutputHeight = ExpectedFaceHeight,
                    OutputBytes = outputBytesByImageNumber.Values.First(),
                    OutputBytesByImageNumber = outputBytesByImageNumber
                });
            }

            E5ImageBatchReplacePreviewResult? e5Preview = null;
            if (e5Requests.Count > 0)
            {
                e5Preview = _e5Replace.PreviewBatchReplacement(project, targetPath, e5Requests);
                warnings.AddRange(e5Preview.FormatWarnings);
            }

            return BuildPreviewResult(project, request, targetPath, items, skipped, warnings, e5Preview);
        }
    }

    public PortraitFrameApplyResult Replace(CczProject project, PortraitFrameApplyRequest request)
    {
        var preview = Preview(project, request);
        if (!preview.CanWrite)
        {
            throw new InvalidOperationException("头像框导入存在阻断项，已停止写入。");
        }

        if (preview.Items.Count == 0)
        {
            throw new InvalidOperationException("头像框导入没有可写入的头像条目。");
        }

        var requests = preview.Items.SelectMany(item =>
            item.TargetImageNumbers.Select(imageNumber => new E5ImageBatchReplaceRequest
            {
                ImageNumber = imageNumber,
                SourceBytes = BuildOutputBytesForImageNumber(preview, imageNumber),
                SourceLabel = $"{item.FramePath} + Face.e5 #{imageNumber} -> framed 120x120 PNG",
                OperationKind = "portrait frame apply"
            })).ToArray();

        var result = _e5Replace.ReplaceBatch(project, preview.TargetPath, requests);
        var payload = new PortraitFrameApplyResult
        {
            Request = preview.Request,
            TargetPath = preview.TargetPath,
            TargetRelativePath = preview.TargetRelativePath,
            Items = preview.Items,
            SkippedItems = preview.SkippedItems,
            Warnings = preview.Warnings,
            E5Preview = preview.E5Preview,
            E5Result = result
        };

        return new PortraitFrameApplyResult
        {
            Request = payload.Request,
            TargetPath = payload.TargetPath,
            TargetRelativePath = payload.TargetRelativePath,
            Items = payload.Items,
            SkippedItems = payload.SkippedItems,
            Warnings = payload.Warnings,
            E5Preview = payload.E5Preview,
            E5Result = payload.E5Result,
            AggregateReportPath = WriteAggregateReport(project, payload)
        };
    }

    private static byte[] BuildOutputBytesForImageNumber(PortraitFrameApplyPreviewResult preview, int imageNumber)
    {
        var item = preview.Items.FirstOrDefault(item => item.TargetImageNumbers.Contains(imageNumber));
        if (item == null)
        {
            throw new InvalidOperationException($"头像框导入预览缺少 Face.e5 #{imageNumber} 的输出字节。");
        }

        if (item.OutputBytesByImageNumber.TryGetValue(imageNumber, out var bytes))
        {
            return bytes;
        }

        if (item.OutputBytes != null && item.TargetImageNumbers.Count == 1)
        {
            return item.OutputBytes;
        }

        throw new InvalidOperationException($"头像框导入预览缺少 Face.e5 #{imageNumber} 的输出字节。");
    }

    private static IReadOnlyList<PortraitFrameTargetRow> BuildUniqueTargets(
        IReadOnlyList<PortraitFrameTargetRow> targets,
        List<string> warnings)
    {
        var result = new List<PortraitFrameTargetRow>();
        foreach (var group in targets.GroupBy(target => target.FaceId).OrderBy(group => group.First().RowId))
        {
            var first = group.First();
            result.Add(first);
            if (group.Count() <= 1) continue;

            var rows = string.Join(", ", group.Select(target => $"ID={target.RowId} {target.DisplayName}".Trim()));
            warnings.Add($"头像编号 {group.Key} 被多个人物共用，本次只写入一次：{rows}");
        }

        return result;
    }

    private static bool TryLoadFrame(
        string framePath,
        List<BatchImageImportSkippedItem> skipped,
        List<string> warnings,
        out Bitmap frame)
    {
        frame = null!;
        if (!File.Exists(framePath))
        {
            skipped.Add(Skip(Path.GetFileName(framePath), framePath, BatchImageImportSkipReasons.MissingFile));
            return false;
        }

        try
        {
            using var stream = File.OpenRead(framePath);
            var header = new byte[Math.Min(16, checked((int)Math.Min(stream.Length, 16)))];
            _ = stream.Read(header, 0, header.Length);
            stream.Position = 0;

            var kind = DetectSourceKind(header);
            if (kind is not ("BMP" or "JPG" or "PNG"))
            {
                skipped.Add(Skip(Path.GetFileName(framePath), framePath, BatchImageImportSkipReasons.InvalidFormat, $"source kind={kind}; required BMP/JPG/PNG"));
                return false;
            }

            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            if (image.Width != ExpectedFaceWidth || image.Height != ExpectedFaceHeight)
            {
                warnings.Add($"头像框 {Path.GetFileName(framePath)} 为 {image.Width}x{image.Height}，导入时会缩放为 120x120。");
            }

            frame = StretchToFaceBitmap(image);
            if (!HasPartialTransparency(frame))
            {
                warnings.Add($"头像框 {Path.GetFileName(framePath)} 未检测到透明区域，可能会遮住头像。");
            }

            if (!kind.Equals("PNG", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"头像框 {Path.GetFileName(framePath)} 为 {kind}，建议使用带透明通道的 PNG。");
            }

            return true;
        }
        catch (ArgumentException ex)
        {
            skipped.Add(Skip(Path.GetFileName(framePath), framePath, BatchImageImportSkipReasons.InvalidFormat, "image decode failed: " + ex.Message));
            return false;
        }
        catch (ExternalException ex)
        {
            skipped.Add(Skip(Path.GetFileName(framePath), framePath, BatchImageImportSkipReasons.InvalidFormat, "image decode failed: " + ex.Message));
            return false;
        }
        catch (OutOfMemoryException ex)
        {
            skipped.Add(Skip(Path.GetFileName(framePath), framePath, BatchImageImportSkipReasons.InvalidFormat, "image decode failed: " + ex.Message));
            return false;
        }
        catch (IOException ex)
        {
            skipped.Add(Skip(Path.GetFileName(framePath), framePath, BatchImageImportSkipReasons.MissingFile, "file read failed: " + ex.Message));
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            skipped.Add(Skip(Path.GetFileName(framePath), framePath, BatchImageImportSkipReasons.MissingFile, "file read failed: " + ex.Message));
            return false;
        }
    }

    private bool TryBuildFramedFaceBytes(
        string facePath,
        int imageNumber,
        Bitmap frame,
        out byte[] outputBytes,
        out string error)
    {
        outputBytes = Array.Empty<byte>();
        error = string.Empty;

        try
        {
            var entryBytes = _e5Replace.ReadEntryBytes(facePath, imageNumber);
            using var current = _renderService.TryDecodeStandardImage(entryBytes);
            if (current == null)
            {
                error = "当前头像条目不是可解码的标准 BMP/JPG/PNG 图片。";
                return false;
            }

            using var normalized = NormalizeToFaceBitmap(current);
            using var composed = new Bitmap(ExpectedFaceWidth, ExpectedFaceHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(composed))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(normalized, new Rectangle(0, 0, ExpectedFaceWidth, ExpectedFaceHeight));
                graphics.DrawImage(frame, new Rectangle(0, 0, ExpectedFaceWidth, ExpectedFaceHeight));
            }

            using var output = new MemoryStream();
            composed.Save(output, ImageFormat.Png);
            outputBytes = output.ToArray();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ExternalException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static Bitmap NormalizeToFaceBitmap(Image source)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            throw new InvalidOperationException("头像图片尺寸无效。");
        }

        var cropSize = Math.Min(source.Width, source.Height);
        var cropX = (source.Width - cropSize) / 2;
        var cropY = (source.Height - cropSize) / 2;
        var bitmap = new Bitmap(ExpectedFaceWidth, ExpectedFaceHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, ExpectedFaceWidth, ExpectedFaceHeight),
            new Rectangle(cropX, cropY, cropSize, cropSize),
            GraphicsUnit.Pixel);
        return bitmap;
    }

    private static Bitmap StretchToFaceBitmap(Image source)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            throw new InvalidOperationException("头像框图片尺寸无效。");
        }

        var bitmap = new Bitmap(ExpectedFaceWidth, ExpectedFaceHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, ExpectedFaceWidth, ExpectedFaceHeight));
        return bitmap;
    }

    private static bool HasPartialTransparency(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A < 255)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private WritableFaceMapping ResolveWritableFaceMapping(int faceId)
    {
        var mapping = _resourceService.MapFaceId(faceId);
        if (faceId <= 0)
        {
            return new WritableFaceMapping(
                mapping.FaceImageNumbers,
                "头像号 0 使用 Face.e5 #1-#8 旧多表情槽；本次会给 #1-#8 全部套用同一头像框。");
        }

        return new WritableFaceMapping(mapping.FaceImageNumbers, string.Empty);
    }

    private static PortraitFrameApplyPreviewResult BuildPreviewResult(
        CczProject project,
        PortraitFrameApplyRequest request,
        string targetPath,
        IReadOnlyList<PortraitFrameApplyItemPreview> items,
        IReadOnlyList<BatchImageImportSkippedItem> skipped,
        IReadOnlyList<string> warnings,
        E5ImageBatchReplacePreviewResult? e5Preview)
        => new()
        {
            Request = request,
            TargetPath = Path.GetFullPath(targetPath),
            TargetRelativePath = e5Preview?.TargetRelativePath ?? WriteOperationReportService.ToProjectRelativePath(project, targetPath),
            Items = items,
            SkippedItems = skipped.ToArray(),
            Warnings = warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)).Distinct(StringComparer.Ordinal).ToArray(),
            E5Preview = e5Preview
        };

    private static string DetectSourceKind(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M') return "BMP";
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8) return "JPG";
        if (bytes.Length >= PngMagic.Length && bytes[..PngMagic.Length].SequenceEqual(PngMagic)) return "PNG";
        return bytes.Length == 0 ? "EMPTY" : "UNKNOWN";
    }

    private static BatchImageImportSkippedItem Skip(string key, string sourcePath, string reason, string detail = "")
        => new()
        {
            Key = key,
            SourcePath = sourcePath,
            Reason = string.IsNullOrWhiteSpace(detail) ? reason : $"{reason}: {detail}"
        };

    private static string WriteAggregateReport(CczProject project, PortraitFrameApplyResult result)
    {
        var backupRoot = ProjectBackupPathService.GetBackupRoot(project);
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_PortraitFrameApplyReport.json");
        var payload = new
        {
            OperationKind = "Portrait frame apply",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ProjectRoot = project.GameRoot,
            result.TargetRelativePath,
            result.TotalOperationCount,
            result.Warnings,
            result.SkippedItems,
            E5Target = result.E5Result == null ? null : new
            {
                result.E5Result.TargetRelativePath,
                result.E5Result.OperationCount,
                result.E5Result.BackupPath,
                result.E5Result.ReportPath,
                result.E5Result.ReportJsonPath
            },
            Items = result.Items.Select(item => new
            {
                item.RowId,
                item.DisplayName,
                item.FaceId,
                item.FramePath,
                item.TargetImageNumbers,
                item.OutputWidth,
                item.OutputHeight
            })
        };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, JsonOptions));
        return reportPath;
    }

    private sealed record WritableFaceMapping(IReadOnlyList<int> ImageNumbers, string Warning);
}
