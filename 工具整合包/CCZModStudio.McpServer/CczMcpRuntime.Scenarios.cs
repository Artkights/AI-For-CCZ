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
        if (updates.Count == 0) throw new InvalidOperationException("updates must contain at least one scenario text update.");

        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var filePath = ResolveProjectFile(project, relativePath, mustExist: true);
        EnsureScenarioTargetAllowed(project, filePath);
        var normalizedRelative = NormalizeProjectRelativePath(project, filePath);
        var entries = _scenarioTextReader.Read(filePath, maxItems: 4096).ToList();

        foreach (var update in updates)
        {
            var entry = entries.FirstOrDefault(x => x.Index == update.Index)
                ?? throw new InvalidOperationException($"Scenario text index {update.Index} was not found.");
            entry.Text = update.Text ?? string.Empty;
        }

        var save = _scenarioTextWriter.SaveInPlace(project, normalizedRelative, entries, "MCP scenario text write");

        return new
        {
            save.FilePath,
            save.BackupPath,
            save.ReportJsonPath,
            save.EntriesWritten,
            save.ChangedBytes,
            RelativePath = normalizedRelative
        };
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
