using System.Data;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.McpServer;

public sealed partial class CczMcpRuntime
{
    public object ReadScenarioTexts(string? gameRoot, string relativePath, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var filePath = ResolveProjectFile(project, relativePath, mustExist: true);
        EnsureScenarioTargetAllowed(project, filePath);
        var effectiveLimit = NormalizeLimit(limit, 100, 2000);
        var entries = _scenarioTextReader.Read(filePath, maxItems: 4096)
            .Where(entry => string.IsNullOrWhiteSpace(keyword) ||
                            entry.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                            entry.Preview.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Take(effectiveLimit)
            .Select(entry => new
            {
                entry.Index,
                entry.OffsetHex,
                entry.ByteLength,
                entry.CharLength,
                entry.Kind,
                entry.HasNewLines,
                entry.Preview,
                entry.Text,
                entry.GbkByteCount,
                entry.RemainingBytes,
                entry.WriteStatus,
                entry.Annotation
            })
            .ToList();

        return new
        {
            RelativePath = NormalizeProjectRelativePath(project, filePath),
            FilePath = filePath,
            ReturnedRows = entries.Count,
            Entries = entries
        };
    }

    public object ListScenarioFiles(string? gameRoot, string? kind, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var effectiveLimit = NormalizeLimit(limit, 200, 1000);
        var files = _scenarioFileReader.ReadAllIndex(project)
            .Where(file => MatchesScenarioFileKind(file, kind))
            .Where(file => string.IsNullOrWhiteSpace(keyword) ||
                           file.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                           file.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                           file.Kind.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                           file.Annotation.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new
        {
            project.GameRoot,
            TotalFiles = files.Count,
            ReturnedFiles = Math.Min(files.Count, effectiveLimit),
            RFiles = files.Count(file => file.Kind.Equals("R剧本", StringComparison.Ordinal)),
            SFiles = files.Count(file => file.Kind.Equals("S剧本", StringComparison.Ordinal)),
            Files = files.Take(effectiveLimit).Select(BuildScenarioFilePayload)
        };
    }

    public object ReadScenarioCommands(
        string? gameRoot,
        string relativePath,
        int? sceneIndex,
        int? sectionIndex,
        string? commandFilter,
        string? keyword,
        int limit)
    {
        var project = LoadProject(gameRoot);
        var filePath = ResolveScenarioFile(project, relativePath);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var document = _legacyScenarioReader.Read(filePath, dictionary);
        var effectiveLimit = NormalizeLimit(limit, 200, 2000);
        var commands = document.EnumerateCommands()
            .Where(command => !sceneIndex.HasValue || command.SceneIndex == sceneIndex.Value)
            .Where(command => !sectionIndex.HasValue || command.SectionIndex == sectionIndex.Value)
            .Where(command => MatchesLegacyCommandFilter(command, commandFilter))
            .Where(command => MatchesLegacyCommandKeyword(command, keyword))
            .ToList();

        return new
        {
            FilePath = filePath,
            RelativePath = NormalizeProjectRelativePath(project, filePath),
            document.Summary,
            document.SceneCount,
            document.SectionCount,
            document.CommandCount,
            TotalMatches = commands.Count,
            ReturnedCommands = Math.Min(commands.Count, effectiveLimit),
            Commands = commands.Take(effectiveLimit).Select(BuildLegacyScenarioCommandPayload),
            SafetyNote = "Read-only legacy scenario command view. Command structure writes must continue to use the verified legacy writer path and are not exposed by this MCP tool."
        };
    }

    public object SearchScenarioScripts(
        string? gameRoot,
        string keyword,
        string? relativePath,
        string? fileKind,
        int limit,
        int maxFiles)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            throw new InvalidOperationException("keyword is required.");
        }

        var project = LoadProject(gameRoot);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var effectiveLimit = NormalizeLimit(limit, 100, 1000);
        var effectiveMaxFiles = NormalizeLimit(maxFiles, 20, 200);
        var candidatePaths = ResolveScenarioSearchFiles(project, relativePath, fileKind)
            .Take(effectiveMaxFiles)
            .ToList();
        var matches = new List<object>();
        var errors = new List<object>();

        foreach (var path in candidatePaths)
        {
            try
            {
                var document = _legacyScenarioReader.Read(path, dictionary);
                foreach (var command in document.EnumerateCommands().Where(command => MatchesLegacyCommandKeyword(command, keyword)))
                {
                    if (matches.Count >= effectiveLimit) break;
                    matches.Add(new
                    {
                        Kind = "Command",
                        FileName = Path.GetFileName(path),
                        RelativePath = NormalizeProjectRelativePath(project, path),
                        Command = BuildLegacyScenarioCommandPayload(command)
                    });
                }

                if (matches.Count < effectiveLimit)
                {
                    foreach (var text in _scenarioTextReader.Read(path, maxItems: 4096).Where(text => MatchesScenarioTextKeyword(text, keyword)))
                    {
                        if (matches.Count >= effectiveLimit) break;
                        matches.Add(new
                        {
                            Kind = "Text",
                            FileName = Path.GetFileName(path),
                            RelativePath = NormalizeProjectRelativePath(project, path),
                            text.Index,
                            text.OffsetHex,
                            text.ByteLength,
                            TextKind = text.Kind,
                            Preview = TrimForMcp(FirstNonEmpty(text.Preview, text.Text), 400),
                            Text = TrimForMcp(text.Text, 800),
                            text.Annotation
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(new
                {
                    FileName = Path.GetFileName(path),
                    RelativePath = NormalizeProjectRelativePath(project, path),
                    Error = ex.Message
                });
            }

            if (matches.Count >= effectiveLimit) break;
        }

        return new
        {
            project.GameRoot,
            Keyword = keyword,
            FilesScanned = candidatePaths.Count,
            ReturnedMatches = matches.Count,
            Errors = errors,
            Matches = matches,
            SafetyNote = "Read-only R/S eex search. Use read_scenario_commands before any manual planning; structure writes are not exposed here."
        };
    }

    public object WriteScenarioTexts(string? gameRoot, string relativePath, List<ScenarioTextUpdate> updates, string? writeMode)
    {
        try
        {
            if (updates.Count == 0) throw new InvalidOperationException("updates must contain at least one scenario text update.");

            var project = LoadProject(gameRoot);
            EnsureWriteMode(project, writeMode);
            var filePath = ResolveProjectFile(project, relativePath, mustExist: true);
            EnsureScenarioTargetAllowed(project, filePath);
            var normalizedRelative = NormalizeProjectRelativePath(project, filePath);
            var dictionary = LoadRequiredScenarioCommandDictionary(project);
            var beforeDocument = _legacyScenarioReader.Read(filePath, dictionary);
            var entries = _scenarioTextReader.Read(filePath, maxItems: 4096).ToList();

            foreach (var update in updates)
            {
                var entry = entries.FirstOrDefault(x => x.Index == update.Index)
                    ?? throw new InvalidOperationException($"Scenario text index {update.Index} was not found.");
                entry.Text = update.Text ?? string.Empty;
            }

            var tempPath = CreateScenarioTempCopy(filePath);
            try
            {
                var tempSave = _scenarioTextWriter.SaveInPlaceFile(
                    project,
                    normalizedRelative,
                    tempPath,
                    entries,
                    createBackup: false,
                    sourceAction: "MCP scenario text write preflight");
                var tempDocument = _legacyScenarioReader.Read(tempPath, dictionary);
                EnsureSameScenarioStructure(beforeDocument, tempDocument, "temporary scenario text write preflight");

                if (tempSave.EntriesWritten == 0)
                {
                    return new
                    {
                        FilePath = filePath,
                        BackupPath = string.Empty,
                        ReportJsonPath = string.Empty,
                        EntriesWritten = 0,
                        ChangedBytes = 0,
                        RelativePath = normalizedRelative,
                        BeforeStructure = BuildScenarioStructureSummary(beforeDocument),
                        TempStructure = BuildScenarioStructureSummary(tempDocument),
                        AfterStructure = BuildScenarioStructureSummary(beforeDocument),
                        Validation = "No text entries changed; formal write skipped after preflight."
                    };
                }
            }
            finally
            {
                TryDeleteFile(tempPath);
            }

            var save = _scenarioTextWriter.SaveInPlace(project, normalizedRelative, entries, "MCP scenario text write");
            LegacyScenarioDocument afterDocument;
            try
            {
                afterDocument = _legacyScenarioReader.Read(filePath, dictionary);
                EnsureSameScenarioStructure(beforeDocument, afterDocument, "formal scenario text write reread");
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(save.BackupPath) && File.Exists(save.BackupPath))
                {
                    File.Copy(save.BackupPath, filePath, overwrite: true);
                }

                throw new InvalidOperationException("Formal scenario text write failed legacy reread validation and was rolled back from the writer backup.", ex);
            }

            return new
            {
                save.FilePath,
                save.BackupPath,
                save.ReportJsonPath,
                save.EntriesWritten,
                save.ChangedBytes,
                RelativePath = normalizedRelative,
                BeforeStructure = BuildScenarioStructureSummary(beforeDocument),
                AfterStructure = BuildScenarioStructureSummary(afterDocument),
                Validation = "Temporary preflight and formal reread both passed with unchanged Scene/Section/Command counts."
            };
        }
        catch (Exception ex)
        {
            return BuildScenarioToolError("write_scenario_texts", ex);
        }
    }

    public object RestoreScenarioBackup(string? gameRoot, string relativePath, string backupPath, string expectedBackupSha256, string? writeMode)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(backupPath)) throw new InvalidOperationException("backup_path is required.");
            if (string.IsNullOrWhiteSpace(expectedBackupSha256)) throw new InvalidOperationException("expected_backup_sha256 is required.");

            var project = LoadProject(gameRoot);
            EnsureWriteMode(project, writeMode);
            var targetPath = ResolveProjectFile(project, relativePath, mustExist: true);
            EnsureScenarioTargetAllowed(project, targetPath);
            var normalizedRelative = NormalizeProjectRelativePath(project, targetPath);
            var resolvedBackupPath = ResolveScenarioBackupPath(project, backupPath);
            var actualBackupSha256 = WriteOperationReportService.ComputeSha256(resolvedBackupPath);
            if (!actualBackupSha256.Equals(expectedBackupSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Backup hash mismatch. Expected {expectedBackupSha256}, actual {actualBackupSha256}.");
            }

            var beforeBytes = File.ReadAllBytes(targetPath);
            var restoreBytes = File.ReadAllBytes(resolvedBackupPath);
            var currentBackupPath = CreateScenarioBackup(project, targetPath);
            File.WriteAllBytes(targetPath, restoreBytes);

            var dictionary = LoadRequiredScenarioCommandDictionary(project);
            LegacyScenarioDocument restoredDocument;
            try
            {
                restoredDocument = _legacyScenarioReader.Read(targetPath, dictionary);
            }
            catch (Exception ex)
            {
                File.WriteAllBytes(targetPath, beforeBytes);
                throw new InvalidOperationException("Scenario backup restore failed legacy reread validation; target was rolled back to the pre-restore bytes.", ex);
            }

            var afterBytes = File.ReadAllBytes(targetPath);
            var reportPath = WriteScenarioRestoreReport(
                project,
                normalizedRelative,
                targetPath,
                currentBackupPath,
                resolvedBackupPath,
                beforeBytes,
                afterBytes,
                restoredDocument);

            return new
            {
                FilePath = targetPath,
                RelativePath = normalizedRelative,
                BackupPath = currentBackupPath,
                RestoredFrom = resolvedBackupPath,
                ExpectedBackupSha256 = expectedBackupSha256,
                ActualBackupSha256 = actualBackupSha256,
                ReportJsonPath = reportPath,
                RestoredStructure = BuildScenarioStructureSummary(restoredDocument),
                Validation = "Restore completed and legacy scenario reread passed."
            };
        }
        catch (Exception ex)
        {
            return BuildScenarioToolError("restore_scenario_backup", ex);
        }
    }

    private static string CreateScenarioTempCopy(string sourcePath)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_ScenarioTextPreflight");
        Directory.CreateDirectory(tempRoot);
        var tempPath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}_{Path.GetFileName(sourcePath)}");
        File.Copy(sourcePath, tempPath, overwrite: false);
        return tempPath;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void EnsureSameScenarioStructure(LegacyScenarioDocument before, LegacyScenarioDocument after, string stage)
    {
        if (before.SceneCount == after.SceneCount &&
            before.SectionCount == after.SectionCount &&
            before.CommandCount == after.CommandCount)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{stage} changed legacy scenario structure counts. " +
            $"Before Scene={before.SceneCount}, Section={before.SectionCount}, Command={before.CommandCount}; " +
            $"After Scene={after.SceneCount}, Section={after.SectionCount}, Command={after.CommandCount}.");
    }

    private static object BuildScenarioStructureSummary(LegacyScenarioDocument document)
        => new
        {
            document.Summary,
            document.SceneCount,
            document.SectionCount,
            document.CommandCount
        };

    private static object BuildScenarioToolError(string toolName, Exception ex)
        => new
        {
            Succeeded = false,
            Tool = toolName,
            ErrorType = ex.GetType().FullName,
            ex.Message,
            InnerErrorType = ex.InnerException?.GetType().FullName,
            InnerMessage = ex.InnerException?.Message,
            Stack = ex.StackTrace
        };

    private static string ResolveScenarioBackupPath(CczProject project, string backupPath)
    {
        var normalized = backupPath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(project.GameRoot, normalized));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Scenario backup file was not found.", fullPath);
        }

        return fullPath;
    }

    private static string CreateScenarioBackup(CczProject project, string targetPath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backupFileName = $"{stamp}_before_restore_{Path.GetFileName(targetPath)}";
        var backup = Path.Combine(backupRoot, backupFileName);
        var suffix = 1;
        while (File.Exists(backup))
        {
            backup = Path.Combine(backupRoot, $"{stamp}_{suffix++}_before_restore_{Path.GetFileName(targetPath)}");
        }

        File.Copy(targetPath, backup, overwrite: false);
        return backup;
    }

    private string WriteScenarioRestoreReport(
        CczProject project,
        string relativePath,
        string targetPath,
        string currentBackupPath,
        string restoredFrom,
        byte[] beforeBytes,
        byte[] afterBytes,
        LegacyScenarioDocument restoredDocument)
    {
        var changedBytes = CountChangedBytes(beforeBytes, afterBytes);
        var report = new WriteOperationReport
        {
            OperationKind = "R/S eex scenario backup restore",
            SourceAction = "MCP restore_scenario_backup",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = relativePath,
            TargetPath = targetPath,
            BackupPath = currentBackupPath,
            BeforeSha256 = WriteOperationReportService.ComputeSha256(beforeBytes),
            AfterSha256 = WriteOperationReportService.ComputeSha256(afterBytes),
            ChangedBytes = changedBytes,
            Summary = $"Restored {relativePath} from {restoredFrom}; changed {changedBytes:N0} bytes.",
            SafetyNotes = "The current target was backed up before restore. The restored file was validated by the legacy scenario reader.",
            Changes =
            [
                new WriteOperationChange
                {
                    Category = "ScenarioRestore",
                    TableName = Path.GetFileName(relativePath),
                    OldValue = WriteOperationReportService.ComputeSha256(beforeBytes),
                    NewValue = WriteOperationReportService.ComputeSha256(afterBytes),
                    Annotation = $"Restored from backup {restoredFrom}; structure {restoredDocument.Summary}."
                }
            ],
            Metadata =
            {
                ["RestoredFrom"] = restoredFrom,
                ["SceneCount"] = restoredDocument.SceneCount.ToString(CultureInfo.InvariantCulture),
                ["SectionCount"] = restoredDocument.SectionCount.ToString(CultureInfo.InvariantCulture),
                ["CommandCount"] = restoredDocument.CommandCount.ToString(CultureInfo.InvariantCulture)
            }
        };

        return new WriteOperationReportService().WriteJsonReport(report, currentBackupPath);
    }

    private static int CountChangedBytes(byte[] before, byte[] after)
    {
        var length = Math.Min(before.Length, after.Length);
        var changed = Math.Abs(before.Length - after.Length);
        for (var i = 0; i < length; i++)
        {
            if (before[i] != after[i]) changed++;
        }

        return changed;
    }

    public object ListScenarioCommandTemplates(string? gameRoot, string? keyword, string? category, string? status, int limit)
    {
        var project = LoadProject(gameRoot);
        var (dictionary, dictionaryPath) = LoadScenarioCommandDictionary(project);
        var effectiveLimit = NormalizeLimit(limit, 100, 1000);
        var filtered = _scenarioCommandTemplates.BuildCatalogItems(dictionary)
            .Where(item => MatchesScenarioCommandKeyword(item, keyword))
            .Where(item => string.IsNullOrWhiteSpace(category) || item.Category.Contains(category, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(status) || item.Status.Contains(status, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new
        {
            project.GameRoot,
            SceneDictionaryPath = dictionaryPath,
            DictionaryLoaded = dictionary != null,
            DictionaryCommandCount = dictionary?.Commands.Count ?? 0,
            TotalItems = filtered.Count,
            ReturnedItems = Math.Min(filtered.Count, effectiveLimit),
            CoveredItems = filtered.Count(item => item.Status.Equals("已覆盖", StringComparison.Ordinal)),
            MissingItems = filtered.Count(item => item.Status.Equals("待补充", StringComparison.Ordinal)),
            Categories = filtered.Select(item => item.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            Items = filtered.Take(effectiveLimit).Select(BuildScenarioCommandTemplatePayload)
        };
    }

    public object ReadScenarioCommandTemplate(string command, string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException("command is required. Use a command id such as 0x78 or a command/template name.");
        }

        var project = LoadProject(gameRoot);
        var (dictionary, dictionaryPath) = LoadScenarioCommandDictionary(project);
        var items = _scenarioCommandTemplates.BuildCatalogItems(dictionary);
        var matches = FindScenarioCommandTemplates(items, command.Trim()).ToList();

        if (matches.Count == 0)
        {
            return new
            {
                project.GameRoot,
                SceneDictionaryPath = dictionaryPath,
                DictionaryLoaded = dictionary != null,
                Query = command,
                MatchCount = 0,
                Message = "No scenario command template matched the query."
            };
        }

        if (matches.Count > 1)
        {
            return new
            {
                project.GameRoot,
                SceneDictionaryPath = dictionaryPath,
                DictionaryLoaded = dictionary != null,
                Query = command,
                MatchCount = matches.Count,
                Message = "Multiple scenario command templates matched the query. Use a precise id such as 0x78.",
                Candidates = matches.Take(20).Select(BuildScenarioCommandTemplatePayload)
            };
        }

        var item = matches[0];
        return new
        {
            project.GameRoot,
            SceneDictionaryPath = dictionaryPath,
            DictionaryLoaded = dictionary != null,
            Query = command,
            MatchCount = 1,
            Template = BuildScenarioCommandTemplatePayload(item),
            Detail = _scenarioCommandTemplates.BuildCatalogItemDetail(item)
        };
    }
}
