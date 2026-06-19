using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class BatchRImageReplaceService
{
    private const int RStripWidth = 48;
    private const int RFrameHeight = 64;
    private const int RFrameCount = 20;
    private const int RStripHeight = RFrameHeight * RFrameCount;

    private static readonly Regex FolderIdRegex = new(@"^(?:R_?)?(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private static readonly E5RawImageSpec RStripSpec = new(
        E5RawImageCodec.PmapobjSpec.FileName,
        E5RawImageCodec.PmapobjSpec.Width,
        E5RawImageCodec.PmapobjSpec.FrameHeight,
        RStripHeight);

    private readonly E5RawImageCodec _codec = new();
    private readonly E5ImageReplaceService _replace = new();

    public BatchRImageReplacePreviewResult Preview(CczProject project, BatchRImageReplaceRequest request)
    {
        var materialRoot = ResolveMaterialRoot(request.MaterialRoot);
        var folders = Directory.EnumerateDirectories(materialRoot)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var skipped = new List<BatchImageImportSkippedItem>();
        var warnings = new List<string>();
        var candidates = new List<(int Id, string Folder)>();

        foreach (var folder in folders)
        {
            var id = TryParseFolderId(folder);
            if (!id.HasValue)
            {
                skipped.Add(Skip(Path.GetFileName(folder), folder, BatchImageImportSkipReasons.InvalidName));
                continue;
            }

            if (request.IncludeOnlySelectedOrFiltered &&
                request.AllowedRImageIds.Count > 0 &&
                !request.AllowedRImageIds.Contains(id.Value))
            {
                skipped.Add(Skip(id.Value.ToString(CultureInfo.InvariantCulture), folder, BatchImageImportSkipReasons.Unused));
                continue;
            }

            candidates.Add((id.Value, folder));
        }

        foreach (var duplicate in candidates.GroupBy(x => x.Id).Where(group => group.Count() > 1))
        {
            foreach (var item in duplicate)
            {
                skipped.Add(Skip(item.Id.ToString(CultureInfo.InvariantCulture), item.Folder, BatchImageImportSkipReasons.DuplicateId));
            }
        }

        var targetPath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        var entries = _replace.ReadIndex(targetPath);
        var items = new List<BatchRImageReplaceItemPreview>();
        var requests = new List<E5ImageBatchReplaceRequest>();

        foreach (var candidate in candidates.GroupBy(x => x.Id).Where(group => group.Count() == 1).Select(group => group.Single()))
        {
            var frontPath = ResolveMaterialFile(candidate.Folder, ["front.bmp", "x.bmp", "1.bmp"]);
            var backPath = ResolveMaterialFile(candidate.Folder, ["back.bmp", "y.bmp", "2.bmp"]);
            if (frontPath == null)
            {
                skipped.Add(Skip(candidate.Id.ToString(CultureInfo.InvariantCulture), candidate.Folder, BatchImageImportSkipReasons.MissingFile, "front.bmp"));
                continue;
            }

            if (backPath == null)
            {
                skipped.Add(Skip(candidate.Id.ToString(CultureInfo.InvariantCulture), candidate.Folder, BatchImageImportSkipReasons.MissingFile, "back.bmp"));
                continue;
            }

            var frontImageNumber = checked(candidate.Id * 2 + 1);
            var backImageNumber = checked(candidate.Id * 2 + 2);
            if (frontImageNumber <= 0 || backImageNumber > entries.Count)
            {
                skipped.Add(Skip(candidate.Id.ToString(CultureInfo.InvariantCulture), candidate.Folder, BatchImageImportSkipReasons.MissingFile, "target image number out of range"));
                continue;
            }

            var frontEncode = _codec.EncodeFile(project, frontPath, RStripSpec, strictHeight: true);
            var backEncode = _codec.EncodeFile(project, backPath, RStripSpec, strictHeight: true);
            warnings.AddRange(frontEncode.Warnings.Select(warning => $"R{candidate.Id} front: {warning}"));
            warnings.AddRange(backEncode.Warnings.Select(warning => $"R{candidate.Id} back: {warning}"));
            items.Add(new BatchRImageReplaceItemPreview
            {
                RImageId = candidate.Id,
                MaterialFolder = candidate.Folder,
                FrontImageNumber = frontImageNumber,
                BackImageNumber = backImageNumber,
                FrontSourcePath = frontPath,
                BackSourcePath = backPath,
                FrontEncode = frontEncode,
                BackEncode = backEncode
            });
            requests.Add(BuildRequest(frontImageNumber, frontEncode, candidate.Id, "front"));
            requests.Add(BuildRequest(backImageNumber, backEncode, candidate.Id, "back"));
        }

        var preview = requests.Count == 0 ? null : _replace.PreviewBatchReplacement(project, targetPath, requests);
        return new BatchRImageReplacePreviewResult
        {
            Request = request,
            Items = items.OrderBy(item => item.RImageId).ToArray(),
            SkippedItems = skipped.ToArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
            BatchPreview = preview
        };
    }

    public BatchRImageReplaceResult Replace(CczProject project, BatchRImageReplaceRequest request)
    {
        var preview = Preview(project, request);
        if (!preview.CanWrite)
        {
            throw new InvalidOperationException("Batch R image import has blocking skipped items.");
        }

        if (preview.BatchPreview == null || preview.Items.Count == 0)
        {
            throw new InvalidOperationException("Batch R image import has no writable items.");
        }

        var targetPath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        var batchRequests = preview.Items.SelectMany(item => new[]
        {
            BuildRequest(item.FrontImageNumber, item.FrontEncode, item.RImageId, "front"),
            BuildRequest(item.BackImageNumber, item.BackEncode, item.RImageId, "back")
        }).ToArray();
        var result = _replace.ReplaceBatch(project, targetPath, batchRequests);
        var payload = new BatchRImageReplaceResult
        {
            Request = preview.Request,
            Items = preview.Items,
            SkippedItems = preview.SkippedItems,
            Warnings = preview.Warnings,
            BatchPreview = preview.BatchPreview,
            WriteResult = result
        };

        return new BatchRImageReplaceResult
        {
            Request = payload.Request,
            Items = payload.Items,
            SkippedItems = payload.SkippedItems,
            Warnings = payload.Warnings,
            BatchPreview = payload.BatchPreview,
            WriteResult = payload.WriteResult,
            AggregateReportPath = WriteAggregateReport(project, payload)
        };
    }

    private static E5ImageBatchReplaceRequest BuildRequest(int imageNumber, E5RawEncodeResult encode, int rImageId, string role)
        => new()
        {
            ImageNumber = imageNumber,
            SourceBytes = encode.RawBytes,
            SourceBytesAreRaw = true,
            SourceLabel = $"{encode.SourcePath} -> RAW",
            OperationKind = $"batch R{rImageId} {role}"
        };

    private static string ResolveMaterialRoot(string materialRoot)
    {
        if (string.IsNullOrWhiteSpace(materialRoot))
        {
            throw new InvalidOperationException("Missing batch R image material root.");
        }

        var root = Path.GetFullPath(materialRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Batch R image material root not found: {root}");
        }

        return root;
    }

    private static int? TryParseFolderId(string folder)
    {
        var match = FolderIdRegex.Match(Path.GetFileName(folder));
        if (!match.Success) return null;
        return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    private static string? ResolveMaterialFile(string folder, IReadOnlyList<string> candidateNames)
    {
        var files = Directory.EnumerateFiles(folder).ToArray();
        foreach (var candidateName in candidateNames)
        {
            var match = files.FirstOrDefault(file => Path.GetFileName(file).Equals(candidateName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match)) return match;
        }

        return null;
    }

    private static BatchImageImportSkippedItem Skip(string key, string sourcePath, string reason, string detail = "")
        => new()
        {
            Key = key,
            SourcePath = sourcePath,
            Reason = string.IsNullOrWhiteSpace(detail) ? reason : $"{reason}: {detail}"
        };

    private static string WriteAggregateReport(CczProject project, BatchRImageReplaceResult result)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_BatchRImageRawReplaceReport.json");
        var payload = new
        {
            OperationKind = "Batch R image RAW replace",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ProjectRoot = project.GameRoot,
            result.Request.MaterialRoot,
            result.Request.IncludeOnlySelectedOrFiltered,
            result.TotalOperationCount,
            result.Warnings,
            result.SkippedItems,
            Target = result.WriteResult == null ? null : new
            {
                result.WriteResult.TargetRelativePath,
                result.WriteResult.OperationCount,
                result.WriteResult.BackupPath,
                result.WriteResult.ReportPath,
                result.WriteResult.ReportJsonPath
            },
            Items = result.Items.Select(item => new
            {
                item.RImageId,
                item.MaterialFolder,
                item.FrontImageNumber,
                item.BackImageNumber,
                item.FrontSourcePath,
                item.BackSourcePath
            })
        };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, JsonOptions));
        return reportPath;
    }
}
