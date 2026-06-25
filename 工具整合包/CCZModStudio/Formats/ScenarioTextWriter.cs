using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class ScenarioTextWriter
{
    private readonly WriteOperationReportService _reportService = new();

    public ScenarioTextSaveResult SaveInPlaceToTestCopy(CczProject project, string relativeScenarioPath, IReadOnlyList<ScenarioTextEntry> entries)
        => SaveInPlaceCore(project, relativeScenarioPath, entries, requireTestCopy: true, sourceAction: "R/S eex text write backup");

    public ScenarioTextSaveResult SaveInPlace(CczProject project, string relativeScenarioPath, IReadOnlyList<ScenarioTextEntry> entries, string sourceAction = "R/S eex text write backup")
        => SaveInPlaceCore(project, relativeScenarioPath, entries, requireTestCopy: false, sourceAction: sourceAction);

    public ScenarioTextSaveResult SaveInPlaceFile(
        CczProject project,
        string relativeScenarioPath,
        string filePath,
        IReadOnlyList<ScenarioTextEntry> entries,
        bool createBackup,
        string sourceAction)
        => SaveInPlaceCore(project, relativeScenarioPath, entries, requireTestCopy: false, sourceAction, filePath, createBackup);

    private ScenarioTextSaveResult SaveInPlaceCore(
        CczProject project,
        string relativeScenarioPath,
        IReadOnlyList<ScenarioTextEntry> entries,
        bool requireTestCopy,
        string sourceAction,
        string? filePathOverride = null,
        bool createBackup = true)
    {
        _ = requireTestCopy;

        if (entries.Count == 0)
        {
            throw new InvalidOperationException("No scenario text entries were supplied for writing.");
        }

        var entriesToWrite = entries
            .Where(entry => string.IsNullOrEmpty(entry.OriginalText)
                            || !string.Equals(NormalizeLineBreaks(entry.Text), NormalizeLineBreaks(entry.OriginalText), StringComparison.Ordinal))
            .ToList();
        var filePath = filePathOverride ?? ResolveScenarioPath(project, relativeScenarioPath);
        if (entriesToWrite.Count == 0)
        {
            return new ScenarioTextSaveResult
            {
                FilePath = filePath,
                BackupPath = string.Empty,
                ReportJsonPath = string.Empty,
                EntriesWritten = 0,
                ChangedBytes = 0
            };
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Target scenario file was not found.", filePath);
        }

        var original = File.ReadAllBytes(filePath);
        var output = (byte[])original.Clone();
        var changes = new List<WriteOperationChange>();
        foreach (var entry in entriesToWrite)
        {
            ValidateEntry(entry, original);
            var normalizedText = NormalizeLineBreaks(entry.Text);
            var writableText = entry.BuildWritableText(normalizedText);
            var encoded = EncodingService.Gbk.GetBytes(writableText);
            if (encoded.Length > entry.ByteLength)
            {
                throw new InvalidOperationException($"Scenario text #{entry.Index} exceeds original GBK capacity: {encoded.Length}>{entry.ByteLength}.");
            }

            if (encoded.Length < 4)
            {
                throw new InvalidOperationException($"Scenario text #{entry.Index} is too short for safe legacy text scanning: GBK {encoded.Length} bytes.");
            }

            var padded = BuildPaddedTextBytes(encoded, entry.ByteLength);
            Buffer.BlockCopy(padded, 0, output, entry.Offset, padded.Length);
            changes.Add(new WriteOperationChange
            {
                Category = "ScenarioText",
                TableName = Path.GetFileName(relativeScenarioPath),
                RowIndex = entry.Index,
                ColumnName = entry.Kind,
                OffsetHex = entry.OffsetHex,
                ByteLength = entry.ByteLength,
                OldValue = NormalizeLineBreaks(entry.OriginalText),
                NewValue = normalizedText,
                Annotation = $"R/S eex text #{entry.Index} ({entry.Kind}) at {entry.OffsetHex}; fixed capacity {entry.ByteLength} GBK bytes."
            });
        }

        if (output.Length != original.Length)
        {
            throw new InvalidOperationException("R/S eex text write changed the file length; refusing to write.");
        }

        var changedBytes = 0;
        for (var i = 0; i < original.Length; i++)
        {
            if (original[i] != output[i]) changedBytes++;
        }

        var backupPath = createBackup ? CreateBeforeSaveBackup(project, filePath) : string.Empty;
        File.WriteAllBytes(filePath, output);
        var reportJsonPath = createBackup
            ? WriteStructuredReport(project, relativeScenarioPath, filePath, backupPath, original, output, changedBytes, entriesToWrite.Count, changes, sourceAction)
            : string.Empty;

        return new ScenarioTextSaveResult
        {
            FilePath = filePath,
            BackupPath = backupPath,
            ReportJsonPath = reportJsonPath,
            EntriesWritten = entriesToWrite.Count,
            ChangedBytes = changedBytes
        };
    }

    private string WriteStructuredReport(
        CczProject project,
        string relativeScenarioPath,
        string filePath,
        string backupPath,
        byte[] original,
        byte[] output,
        int changedBytes,
        int entriesWritten,
        List<WriteOperationChange> changes,
        string sourceAction)
    {
        var normalizedRelativePath = relativeScenarioPath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var report = new WriteOperationReport
        {
            OperationKind = "R/S eex scenario text write",
            SourceAction = sourceAction,
            ProjectRoot = project.GameRoot,
            TargetRelativePath = normalizedRelativePath,
            TargetPath = filePath,
            BackupPath = backupPath,
            BeforeSha256 = WriteOperationReportService.ComputeSha256(original),
            AfterSha256 = WriteOperationReportService.ComputeSha256(output),
            ChangedBytes = changedBytes,
            Summary = $"Wrote {entriesWritten} R/S eex text entr{(entriesWritten == 1 ? "y" : "ies")} to {normalizedRelativePath}; changed {changedBytes:N0} bytes.",
            SafetyNotes = "Fixed-capacity in-place GBK text write. The writer pads shortened text with 0x20 spaces, preserves the original terminator, does not move text data, and refuses file length changes.",
            Changes = changes,
            Metadata =
            {
                ["RelativeScenarioPath"] = normalizedRelativePath,
                ["EntriesWritten"] = entriesWritten.ToString(CultureInfo.InvariantCulture)
            }
        };
        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static string ResolveScenarioPath(CczProject project, string relativeScenarioPath)
    {
        var filePath = Path.GetFullPath(Path.Combine(project.GameRoot, relativeScenarioPath));
        var gameRoot = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Target scenario file is outside the project root: " + filePath);
        }

        return filePath;
    }

    private static void ValidateEntry(ScenarioTextEntry entry, byte[] original)
    {
        if (entry.Offset < 0 || entry.ByteLength <= 0 || checked(entry.Offset + entry.ByteLength) > original.Length)
        {
            throw new InvalidOperationException($"Scenario text #{entry.Index} has an invalid offset or byte length. Offset={entry.OffsetHex}, ByteLength={entry.ByteLength}.");
        }

        var terminatorOffset = entry.Offset + entry.ByteLength;
        if (terminatorOffset >= original.Length || original[terminatorOffset] != 0x00)
        {
            throw new InvalidOperationException($"Scenario text #{entry.Index} does not have a stable null terminator immediately after its detected GBK byte range; refusing in-place text write.");
        }
    }

    private static byte[] BuildPaddedTextBytes(byte[] encoded, int byteLength)
    {
        var output = new byte[byteLength];
        Buffer.BlockCopy(encoded, 0, output, 0, encoded.Length);
        Array.Fill(output, (byte)0x20, encoded.Length, output.Length - encoded.Length);
        return output;
    }

    private static string NormalizeLineBreaks(string? text)
        => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{Path.GetFileName(filePath)}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{stamp}_{suffix++}_{Path.GetFileName(filePath)}");
        }

        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }
}
