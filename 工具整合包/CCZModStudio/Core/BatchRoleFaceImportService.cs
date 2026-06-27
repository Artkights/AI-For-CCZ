using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class BatchRoleFaceImportService
{
    private const int ExpectedFaceWidth = 120;
    private const int ExpectedFaceHeight = 120;
    private const string FaceFormatRequirement = "源图支持任意尺寸 BMP/JPG/PNG；导入时居中裁切并缩放为 Face.e5 需要的 120x120 PNG。";
    private static readonly byte[] PngMagic = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly E5ImageReplaceService _e5Replace = new();

    public BatchRoleFaceImportPreviewResult Preview(CczProject project, BatchRoleFaceImportRequest request)
    {
        if (request.SourceFiles.Count == 0 && string.IsNullOrWhiteSpace(request.SourceRoot))
        {
            throw new InvalidOperationException("No role face source files were selected.");
        }

        var targetPath = CharacterImageResourceService.ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", "Face.e5");
        var skipped = new List<BatchImageImportSkippedItem>();
        var matched = MatchTargets(request, skipped);
        AddDuplicateTargetSkips(matched, skipped);
        var uniqueMatched = matched
            .GroupBy(item => item.Target.FaceId)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(item => item.Target.RowId)
            .ToArray();
        var usable = ValidateUsableSources(uniqueMatched, skipped);

        return PreviewE5(project, request, targetPath, usable, skipped);
    }

    public BatchRoleFaceImportResult Replace(CczProject project, BatchRoleFaceImportRequest request)
    {
        var preview = Preview(project, request);
        if (!preview.CanWrite)
        {
            throw new InvalidOperationException("Batch role face import has blocking skipped items.");
        }

        if (preview.Items.Count == 0)
        {
            throw new InvalidOperationException("Batch role face import has no writable items.");
        }

        var requests = preview.Items.SelectMany(item => item.TargetImageNumbers.Select(imageNumber => new E5ImageBatchReplaceRequest
        {
            ImageNumber = imageNumber,
            SourcePath = item.SourcePath,
            SourceBytes = item.OutputBytes,
            SourceLabel = $"{item.SourcePath} -> 120x120 PNG",
            OperationKind = "batch role face import"
        })).ToArray();

        var result = _e5Replace.ReplaceBatch(project, preview.TargetPath, requests);
        var payload = new BatchRoleFaceImportResult
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

        return new BatchRoleFaceImportResult
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

    private BatchRoleFaceImportPreviewResult PreviewE5(
        CczProject project,
        BatchRoleFaceImportRequest request,
        string targetPath,
        IReadOnlyList<RoleFaceSourceCandidate> usable,
        IReadOnlyList<BatchImageImportSkippedItem> skipped)
    {
        var e5Requests = usable.SelectMany(item =>
        {
            var mapping = ResolveWritableFaceMapping(item.Target.FaceId);
            return mapping.ImageNumbers.Select(imageNumber => new E5ImageBatchReplaceRequest
            {
                ImageNumber = imageNumber,
                SourcePath = item.SourcePath,
                SourceBytes = item.OutputBytes,
                SourceLabel = $"{item.SourcePath} -> 120x120 PNG",
                OperationKind = "batch role face import"
            });
        }).ToArray();

        var preview = e5Requests.Length == 0 ? null : _e5Replace.PreviewBatchReplacement(project, targetPath, e5Requests);
        var warnings = new List<string>();
        if (preview != null) warnings.AddRange(preview.FormatWarnings);
        warnings.Add("头像导入仅写入 Face.e5 小头像；Tou.dll 真彩头像资源不会自动同步。");
        warnings.AddRange(usable
            .Where(item => item.SourceInfo.Width != ExpectedFaceWidth ||
                           item.SourceInfo.Height != ExpectedFaceHeight ||
                           !item.SourceInfo.Kind.Equals("PNG", StringComparison.OrdinalIgnoreCase))
            .Select(item => $"头像#{item.Target.FaceId} 来源为 {item.SourceInfo.Kind} {item.SourceInfo.Width}x{item.SourceInfo.Height}，导入时会转换为 120x120 PNG。"));
        warnings.AddRange(usable
            .Select(item => ResolveWritableFaceMapping(item.Target.FaceId).Warning)
            .Where(warning => !string.IsNullOrWhiteSpace(warning))!);
        return new BatchRoleFaceImportPreviewResult
        {
            Request = request,
            TargetPath = Path.GetFullPath(targetPath),
            TargetRelativePath = preview?.TargetRelativePath ?? WriteOperationReportService.ToProjectRelativePath(project, targetPath),
            Items = usable.Select(item =>
            {
                var mapping = ResolveWritableFaceMapping(item.Target.FaceId);
                return new BatchRoleFaceImportItemPreview
                {
                    RowId = item.Target.RowId,
                    DisplayName = item.Target.DisplayName,
                    FaceId = item.Target.FaceId,
                    SourcePath = item.SourcePath,
                    SourceKind = item.SourceInfo.Kind,
                    SourceWidth = item.SourceInfo.Width,
                    SourceHeight = item.SourceInfo.Height,
                    OutputKind = "PNG",
                    OutputWidth = ExpectedFaceWidth,
                    OutputHeight = ExpectedFaceHeight,
                    OutputBytes = item.OutputBytes,
                    FormatRequirement = FaceFormatRequirement,
                    TargetImageNumbers = mapping.ImageNumbers,
                    TrueColorResourceIds = mapping.TrueColorResourceIds
                };
            }).ToArray(),
            SkippedItems = skipped.ToArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
            E5Preview = preview
        };
    }

    private static WritableFaceMapping ResolveWritableFaceMapping(int faceId)
    {
        var mapping = new CharacterImageResourceService().MapFaceId(faceId);
        if (faceId <= 0)
        {
            return new WritableFaceMapping(
                new[] { mapping.FaceImageNumbers.First() },
                new[] { mapping.TrueColorResourceIds.First() },
                "Data 头像号 0 是 Face.e5 #1-#8 的遗留多表情槽；本次单图导入只写 #1，不自动覆盖 #2-#8。");
        }

        return new WritableFaceMapping(mapping.FaceImageNumbers, mapping.TrueColorResourceIds, string.Empty);
    }

    private static IReadOnlyList<(BatchRoleFaceTargetRow Target, string SourcePath)> MatchTargets(
        BatchRoleFaceImportRequest request,
        List<BatchImageImportSkippedItem> skipped)
    {
        var sourceCandidates = BatchImageSourceResolver.Resolve(
            BatchImageSourceKind.Face,
            request.SourceFiles,
            request.SourceRoot);

        foreach (var sourceFile in sourceCandidates.Select(candidate => candidate.SourcePath).Where(path => !File.Exists(path)))
        {
            skipped.Add(Skip(Path.GetFileName(sourceFile), sourceFile, BatchImageImportSkipReasons.MissingFile));
        }

        sourceCandidates = sourceCandidates.Where(candidate => File.Exists(candidate.SourcePath)).ToArray();
        var targetRows = request.TargetRows.ToArray();
        var strictRowOrder = request.MatchMode.Equals("selected-row-order", StringComparison.OrdinalIgnoreCase);
        if (strictRowOrder && targetRows.Length > 0 && sourceCandidates.Count == targetRows.Length)
        {
            return targetRows.Zip(sourceCandidates, (row, source) => (row, source.SourcePath)).ToArray();
        }

        if (strictRowOrder && targetRows.Length > 0 && sourceCandidates.Count != targetRows.Length)
        {
            skipped.Add(Skip("selected rows", string.Empty, BatchImageImportSkipReasons.CountMismatch, $"rows={targetRows.Length}, files={sourceCandidates.Count}"));
            return Array.Empty<(BatchRoleFaceTargetRow Target, string SourcePath)>();
        }

        var targetByFace = targetRows
            .GroupBy(row => row.FaceId)
            .ToDictionary(group => group.Key, group => group.First());
        var matched = new List<(BatchRoleFaceTargetRow Target, string SourcePath)>();
        if (targetByFace.Count > 0)
        {
            var unresolved = new List<BatchImageSourceCandidate>();
            foreach (var source in sourceCandidates)
            {
                var faceId = source.FieldValue ?? ExtractLastNumber(Path.GetFileNameWithoutExtension(source.SourcePath));
                if (!faceId.HasValue)
                {
                    unresolved.Add(source);
                    continue;
                }

                if (!targetByFace.TryGetValue(faceId.Value, out var mappedTarget))
                {
                    skipped.Add(Skip(faceId.Value.ToString(CultureInfo.InvariantCulture), source.SourcePath, BatchImageImportSkipReasons.UnmatchedFile));
                    continue;
                }

                matched.Add((mappedTarget, source.SourcePath));
            }

            if (matched.Count > 0)
            {
                foreach (var source in unresolved)
                {
                    skipped.Add(Skip(Path.GetFileName(source.SourcePath), source.SourcePath, BatchImageImportSkipReasons.InvalidName));
                }

                return matched;
            }

            if (sourceCandidates.Count == targetRows.Length)
            {
                return targetRows.Zip(sourceCandidates, (row, source) => (row, source.SourcePath)).ToArray();
            }

            skipped.Add(Skip("selected rows", string.Empty, BatchImageImportSkipReasons.CountMismatch, $"rows={targetRows.Length}, files={sourceCandidates.Count}"));
            return Array.Empty<(BatchRoleFaceTargetRow Target, string SourcePath)>();
        }

        foreach (var source in sourceCandidates)
        {
            var faceId = source.FieldValue ?? ExtractLastNumber(Path.GetFileNameWithoutExtension(source.SourcePath));
            if (!faceId.HasValue)
            {
                skipped.Add(Skip(Path.GetFileName(source.SourcePath), source.SourcePath, BatchImageImportSkipReasons.InvalidName));
                continue;
            }

            var target = new BatchRoleFaceTargetRow(faceId.Value, $"face #{faceId.Value}", faceId.Value);
            matched.Add((target, source.SourcePath));
        }

        return matched;
    }

    private static void AddDuplicateTargetSkips(
        IReadOnlyList<(BatchRoleFaceTargetRow Target, string SourcePath)> matched,
        List<BatchImageImportSkippedItem> skipped)
    {
        foreach (var duplicate in matched.GroupBy(item => item.Target.FaceId).Where(group => group.Count() > 1))
        {
            skipped.Add(Skip(duplicate.Key.ToString(CultureInfo.InvariantCulture), string.Join("; ", duplicate.Select(item => item.SourcePath)), BatchImageImportSkipReasons.DuplicateTarget));
        }
    }

    private static IReadOnlyList<RoleFaceSourceCandidate> ValidateUsableSources(
        IReadOnlyList<(BatchRoleFaceTargetRow Target, string SourcePath)> matched,
        List<BatchImageImportSkippedItem> skipped)
    {
        var usable = new List<RoleFaceSourceCandidate>();
        foreach (var item in matched)
        {
            if (!TryBuildFaceSource(item.SourcePath, out var info, out var outputBytes, out var error))
            {
                skipped.Add(Skip(item.Target.FaceId.ToString(CultureInfo.InvariantCulture), item.SourcePath, BatchImageImportSkipReasons.InvalidFormat, error));
                continue;
            }

            usable.Add(new RoleFaceSourceCandidate(item.Target, item.SourcePath, info, outputBytes));
        }

        return usable;
    }

    private static bool TryBuildFaceSource(string sourcePath, out RoleFaceSourceInfo info, out byte[] outputBytes, out string error)
    {
        info = new RoleFaceSourceInfo(string.Empty, null, null);
        outputBytes = Array.Empty<byte>();
        error = string.Empty;
        byte[] header;
        try
        {
            using var stream = File.OpenRead(sourcePath);
            header = new byte[Math.Min(16, checked((int)Math.Min(stream.Length, 16)))];
            _ = stream.Read(header, 0, header.Length);
            stream.Position = 0;
            var kind = DetectSourceKind(header);
            if (kind is not ("BMP" or "JPG" or "PNG"))
            {
                error = $"source kind={kind}; required BMP/JPG/PNG";
                return false;
            }

            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            info = new RoleFaceSourceInfo(kind, image.Width, image.Height);
            outputBytes = BuildFacePngBytes(image);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = "image decode failed: " + ex.Message;
            return false;
        }
        catch (ExternalException ex)
        {
            error = "image decode failed: " + ex.Message;
            return false;
        }
        catch (OutOfMemoryException ex)
        {
            error = "image decode failed: " + ex.Message;
            return false;
        }
        catch (IOException ex)
        {
            error = "file read failed: " + ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = "file read failed: " + ex.Message;
            return false;
        }
    }

    private static string DetectSourceKind(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M') return "BMP";
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8) return "JPG";
        if (bytes.Length >= PngMagic.Length && bytes[..PngMagic.Length].SequenceEqual(PngMagic)) return "PNG";
        return bytes.Length == 0 ? "EMPTY" : "UNKNOWN";
    }

    private static byte[] BuildFacePngBytes(Image source)
    {
        var sourceWidth = source.Width;
        var sourceHeight = source.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new InvalidOperationException("Source face image has invalid dimensions.");
        }

        var cropSize = Math.Min(sourceWidth, sourceHeight);
        var cropX = (sourceWidth - cropSize) / 2;
        var cropY = (sourceHeight - cropSize) / 2;
        using var bitmap = new Bitmap(ExpectedFaceWidth, ExpectedFaceHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, ExpectedFaceWidth, ExpectedFaceHeight),
                new Rectangle(cropX, cropY, cropSize, cropSize),
                GraphicsUnit.Pixel);
        }

        using var output = new MemoryStream();
        bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        return output.ToArray();
    }

    private static int? ExtractLastNumber(string text)
    {
        var matches = NumberRegex.Matches(text);
        if (matches.Count == 0) return null;
        return int.TryParse(matches[^1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static BatchImageImportSkippedItem Skip(string key, string sourcePath, string reason, string detail = "")
        => new()
        {
            Key = key,
            SourcePath = sourcePath,
            Reason = string.IsNullOrWhiteSpace(detail) ? reason : $"{reason}: {detail}"
        };

    private static string WriteAggregateReport(CczProject project, BatchRoleFaceImportResult result)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_BatchRoleFaceImportReport.json");
        var payload = new
        {
            OperationKind = "Batch role face import",
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
                item.SourcePath,
                item.SourceKind,
                item.SourceWidth,
                item.SourceHeight,
                item.OutputKind,
                item.OutputWidth,
                item.OutputHeight,
                item.TargetImageNumbers,
                item.TrueColorResourceIds
            })
        };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, JsonOptions));
        return reportPath;
    }

    private sealed record WritableFaceMapping(
        IReadOnlyList<int> ImageNumbers,
        IReadOnlyList<int> TrueColorResourceIds,
        string Warning);

    private sealed record RoleFaceSourceCandidate(
        BatchRoleFaceTargetRow Target,
        string SourcePath,
        RoleFaceSourceInfo SourceInfo,
        byte[] OutputBytes);

    private sealed record RoleFaceSourceInfo(string Kind, int? Width, int? Height);
}
