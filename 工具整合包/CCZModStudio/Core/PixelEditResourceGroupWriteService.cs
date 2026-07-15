using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Security.Cryptography;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class PixelEditResourceGroupWriteService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly EditableImageCodecService _codec;
    private readonly E5ImageReplaceService _e5 = new();

    internal Action<string, int>? BeforeFileWriteTestHook { get; set; }

    public PixelEditResourceGroupWriteService(EditableImageCodecService codec)
    {
        _codec = codec;
    }

    public PixelEditResourceGroupWritePreview Preview(CczProject project, PixelEditResourceGroup group)
    {
        var writePages = group.GetWritePages();
        if (writePages.Count == 0)
        {
            return new PixelEditResourceGroupWritePreview
            {
                ScopeDescription = group.ScopeDescription,
                SharedUsageWarning = group.SharedUsageWarning,
                ColorReplacementPreview = group.ColorReplacementPreview
            };
        }
        if (writePages.Count == 1 && writePages[0].Document.Target.Kind == EditableImageTargetKind.DllBitmapIcon)
        {
            var single = _codec.PreviewWrite(project, writePages[0].Document.Target, writePages[0].Document.Bitmap);
            return new PixelEditResourceGroupWritePreview
            {
                ScopeDescription = group.ScopeDescription,
                SinglePreview = single,
                Warnings = single.Warnings,
                SharedUsageWarning = group.SharedUsageWarning,
                ColorReplacementPreview = group.ColorReplacementPreview
            };
        }

        var prepared = Prepare(project, group);
        var previews = new List<E5ImageBatchReplacePreviewResult>();
        var warnings = new List<string>();
        foreach (var file in prepared)
        {
            var preview = _e5.PreviewBatchReplacement(project, file.TargetPath, file.Requests);
            previews.Add(preview);
            warnings.AddRange(file.Warnings);
            warnings.AddRange(preview.FormatWarnings);
        }

        return new PixelEditResourceGroupWritePreview
        {
            ScopeDescription = group.ScopeDescription,
            Files = previews,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
            SharedUsageWarning = group.SharedUsageWarning,
            ColorReplacementPreview = group.ColorReplacementPreview
        };
    }

    public PixelEditResourceGroupWriteResult Write(CczProject project, PixelEditResourceGroup group)
    {
        var writePages = group.GetWritePages();
        if (writePages.Count == 0)
        {
            return new PixelEditResourceGroupWriteResult
            {
                ScopeDescription = group.ScopeDescription
            };
        }
        if (writePages.Count == 1 && writePages[0].Document.Target.Kind == EditableImageTargetKind.DllBitmapIcon)
        {
            var single = _codec.Write(project, writePages[0].Document.Target, writePages[0].Document.Bitmap);
            return new PixelEditResourceGroupWriteResult
            {
                ScopeDescription = group.ScopeDescription,
                SingleResult = single,
                AggregateReportPath = single.ReportPath
            };
        }

        var prepared = Prepare(project, group);
        var transaction = new E5ArchiveSetTransaction(_e5)
        {
            BeforeCommitTestHook = BeforeFileWriteTestHook
        };
        var results = transaction.Execute(project, prepared.Select(file => new E5ArchiveMutationPlan(
            file.TargetPath,
            file.Requests,
            group.ScopeDescription))).Files.ToList();

        var reportPath = WriteAggregateReport(project, group, results);
        return new PixelEditResourceGroupWriteResult
        {
            ScopeDescription = group.ScopeDescription,
            Files = results,
            AggregateReportPath = reportPath
        };
    }

    private IReadOnlyList<PreparedFile> Prepare(CczProject project, PixelEditResourceGroup group)
    {
        var writes = group.GetWritePages()
            .Select(page => _codec.PrepareE5Write(project, page.Document.Target, page.Document.Bitmap))
            .ToArray();
        var files = new List<PreparedFile>();
        foreach (var grouping in writes.GroupBy(write => Path.GetFullPath(write.TargetPath), StringComparer.OrdinalIgnoreCase))
        {
            var requests = grouping.SelectMany(write => write.Requests).ToArray();
            var duplicate = requests.GroupBy(request => request.ImageNumber).FirstOrDefault(items => items.Count() > 1);
            if (duplicate != null)
            {
                throw new InvalidOperationException($"整组像素写回包含重复图号：{Path.GetFileName(grouping.Key)} #{duplicate.Key}。");
            }

            files.Add(new PreparedFile(
                grouping.Key,
                requests,
                grouping.SelectMany(write => write.Warnings).Distinct(StringComparer.Ordinal).ToArray()));
        }

        return files
            .OrderBy(file => GetFileOrder(Path.GetFileName(file.TargetPath)))
            .ThenBy(file => file.TargetPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int GetFileOrder(string fileName)
        => fileName.ToLowerInvariant() switch
        {
            "pmapobj.e5" => 0,
            "unit_mov.e5" => 1,
            "unit_atk.e5" => 2,
            "unit_spc.e5" => 3,
            _ => 10
        };

    private static void RestoreCompletedFile(E5ImageBatchReplaceResult result)
    {
        var tempPath = result.TargetPath + ".CCZModStudio.group-rollback." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(result.BackupPath, tempPath, overwrite: true);
            File.Move(tempPath, result.TargetPath, overwrite: true);
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.TargetPath)));
            if (!sha.Equals(result.OldFileSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Group rollback SHA-256 does not match the pre-save archive.");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static string WriteAggregateReport(
        CczProject project,
        PixelEditResourceGroup group,
        IReadOnlyList<E5ImageBatchReplaceResult> results)
    {
        var backupRoot = ProjectBackupPathService.GetBackupRoot(project);
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(backupRoot, $"{stamp}_PixelEditResourceGroupReport.json");
        var payload = new
        {
            OperationKind = "pixel edit resource group",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ProjectRoot = project.GameRoot,
            group.ScopeDescription,
            group.SharedUsageWarning,
            ColorReplacement = group.ColorReplacementPreview == null ? null : new
            {
                Rules = group.ColorReplacementPreview.Rules.Select((rule, index) => new
                {
                    Index = index + 1,
                    SourceArgb = PixelColorReplacementService.NormalizeArgb(rule.Source),
                    TargetArgb = PixelColorReplacementService.NormalizeArgb(rule.Target),
                    Matches = group.ColorReplacementPreview.RuleMatchCounts[index]
                }),
                group.ColorReplacementPreview.TotalMatches,
                Documents = group.ColorReplacementPreview.Documents.Select(document => new
                {
                    document.DocumentKey,
                    document.DisplayName,
                    document.RuleMatchCounts,
                    document.TotalMatches
                })
            },
            Targets = group.GetWritePages().Select(page => new
            {
                page.Label,
                page.Key,
                page.Document.Target.TargetPath,
                page.Document.Target.ImageNumber
            }),
            Files = results.Select(result => new
            {
                result.TargetRelativePath,
                result.BackupPath,
                result.ReportJsonPath,
                ImageNumbers = result.Operations.Select(operation => operation.ImageNumber)
            })
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
        return path;
    }

    private sealed record PreparedFile(
        string TargetPath,
        IReadOnlyList<E5ImageBatchReplaceRequest> Requests,
        IReadOnlyList<string> Warnings);
}
