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
    public object DetectProject(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.Name,
            project.WorkspaceRoot,
            project.GameRoot,
            project.HexTableXmlPath,
            project.SceneDictionaryPath,
            project.SceneEditorDirectory,
            project.ImageAssignerDirectory,
            project.MaterialLibraryRoot,
            project.PatchConfigRoot,
            project.IsTestCopy,
            Files = project.GetFileStatuses().Select(x => new
            {
                x.Name,
                x.Path,
                x.Exists,
                x.SizeBytes,
                x.Kind
            }),
            project.PathDiagnostics
        };
    }

    public object AuditProject(string? gameRoot, bool writeReport, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var items = _projectAudit.Analyze(project, tables);
        var effectiveLimit = NormalizeLimit(limit, 200, 1000);
        var reportPath = writeReport ? _projectAudit.WriteReport(project, items) : null;
        return new
        {
            project.GameRoot,
            project.IsTestCopy,
            ReportPath = reportPath,
            TotalItems = items.Count,
            ReturnedItems = Math.Min(items.Count, effectiveLimit),
            ErrorCount = items.Count(item => item.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)),
            WarningCount = items.Count(item => item.Severity.Equals("Warn", StringComparison.OrdinalIgnoreCase)),
            InfoCount = items.Count(item => item.Severity.Equals("Info", StringComparison.OrdinalIgnoreCase)),
            Items = items.Take(effectiveLimit).Select(item => new
            {
                item.Severity,
                item.Category,
                item.Name,
                item.Status,
                item.Detail,
                item.Path
            })
        };
    }

    public object CreateTestCopy(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        if (project.IsTestCopy)
        {
            throw new InvalidOperationException("create_test_copy must be run from a source project, not an existing test copy.");
        }

        var progressLines = new List<string>();
        var progress = new Progress<string>(line =>
        {
            if (progressLines.Count < 80) progressLines.Add(line);
        });
        var testCopyRoot = _backupManager.CreateTestCopy(project, progress);
        var testProject = _projectDetector.CreateProjectFromGameRoot(testCopyRoot);
        return new
        {
            SourceGameRoot = project.GameRoot,
            TestCopyRoot = testCopyRoot,
            MarkerPath = Path.Combine(testCopyRoot, "_CCZModStudio_TestCopy.txt"),
            testProject.IsTestCopy,
            Files = testProject.GetFileStatuses().Select(x => new
            {
                x.Name,
                x.Path,
                x.Exists,
                x.SizeBytes,
                x.Kind
            }),
            Progress = progressLines
        };
    }

    public object DiffTestCopy(string? gameRoot, bool writeReport, int limit)
    {
        var project = LoadProject(gameRoot);
        var items = _testCopyDiff.Analyze(project);
        var effectiveLimit = NormalizeLimit(limit, 200, 2000);
        var reportPath = writeReport ? _testCopyDiff.WriteReport(project, items) : null;
        return new
        {
            TestCopyRoot = project.GameRoot,
            SourceRoot = _testCopyDiff.ReadSourceRoot(project),
            ReportPath = reportPath,
            TotalItems = items.Count,
            ReturnedItems = Math.Min(items.Count, effectiveLimit),
            ModifiedItems = items.Count(item => item.Status.Equals("已修改", StringComparison.Ordinal)),
            AddedItems = items.Count(item => item.Status.Equals("新增", StringComparison.Ordinal)),
            MissingItems = items.Count(item => item.Status.Equals("缺失", StringComparison.Ordinal)),
            Items = items.Take(effectiveLimit).Select(item => new
            {
                item.Status,
                item.RelativePath,
                item.SourceSize,
                item.TestSize,
                item.SourceSha256,
                item.TestSha256,
                item.Detail,
                item.SourcePath,
                item.TestPath
            })
        };
    }

    public object CreateReleaseCopy(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var diffItems = project.IsTestCopy ? _testCopyDiff.Analyze(project) : Array.Empty<ProjectDiffItem>();
        var progressLines = new List<string>();
        var progress = new Progress<string>(line =>
        {
            if (progressLines.Count < 120) progressLines.Add(line);
        });
        var result = _releasePackage.CreateReleaseCopy(project, diffItems, progress);
        return new
        {
            result.ReleaseRoot,
            result.ManifestPath,
            result.FilesCopied,
            result.BytesCopied,
            result.ChangedItems,
            result.ModifiedItems,
            result.AddedItems,
            result.MissingItems,
            Progress = progressLines
        };
    }

    public object ListWorkflowGuide(string? gameRoot, bool includeDiagnostics, int maxActions)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var auditItems = _projectAudit.Analyze(project, tables);
        var resources = includeDiagnostics ? _gameResourceIndexer.Index(project) : Array.Empty<ResourceIndexItem>();
        var resourceDiagnostics = includeDiagnostics ? _resourceDiagnosticService.Analyze(resources) : Array.Empty<ResourceDiagnosticItem>();
        var diffItems = SafeAnalyzeDiff(project);
        var backupItems = SafeScanBackups(project);
        var creatorNotes = _creatorNoteService.Load(project);
        var scenarioMapLinks = includeDiagnostics
            ? BuildScenarioMapLinks(project, resources)
            : Array.Empty<ScenarioMapLinkInfo>();

        var dashboard = _projectWorkflowGuideService.BuildDashboard(
            project,
            tables.Count,
            auditItems,
            resourceDiagnostics,
            diffItems,
            backupItems,
            creatorNotes,
            scenarioMapLinks);
        var steps = _projectWorkflowGuideService.BuildSteps(
            project,
            tables.Count,
            auditItems.Count,
            diffItems.Count,
            backupItems.Count,
            creatorNotes.Count);
        var actions = _projectWorkflowGuideService.BuildActionItems(
            project,
            dashboard,
            creatorNotes,
            NormalizeLimit(maxActions, 6, 10));

        return new
        {
            project.GameRoot,
            project.IsTestCopy,
            IncludeDiagnostics = includeDiagnostics,
            Summary = _projectWorkflowGuideService.BuildSummary(project, steps, dashboard),
            ActionPlan = _projectWorkflowGuideService.BuildActionPlan(project, dashboard, NormalizeLimit(maxActions, 6, 10)),
            Dashboard = dashboard,
            Steps = steps,
            ActionItems = actions,
            Counts = new
            {
                Tables = tables.Count,
                AuditItems = auditItems.Count,
                ResourceDiagnostics = resourceDiagnostics.Count,
                ScenarioMapLinks = scenarioMapLinks.Count,
                DiffItems = diffItems.Count,
                BackupItems = backupItems.Count,
                CreatorNotes = creatorNotes.Count
            },
            SafetyNote = "Workflow guide is read-only. It summarizes audit, diagnostics, notes, backups, diffs, and evidence; it does not modify game files."
        };
    }

    public object ListProjectEvidence(string? gameRoot, string? category, string? kind, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var effectiveLimit = NormalizeLimit(limit, 80, 500);
        var evidence = _projectEvidenceService.Scan(project, maxItems: 500);
        var filtered = evidence
            .Where(item => string.IsNullOrWhiteSpace(category) || item.Category.Contains(category, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(kind) || item.Kind.Contains(kind, StringComparison.OrdinalIgnoreCase))
            .Where(item => MatchesProjectEvidenceKeyword(item, keyword))
            .ToList();

        return new
        {
            project.GameRoot,
            project.WorkspaceRoot,
            TotalEvidence = filtered.Count,
            ReturnedEvidence = Math.Min(filtered.Count, effectiveLimit),
            Summary = _projectEvidenceService.BuildSummary(project, filtered),
            CategoryCounts = evidence
                .GroupBy(item => item.Category)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { Category = group.Key, Count = group.Count() }),
            KindCounts = evidence
                .GroupBy(item => item.Kind)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { Kind = group.Key, Count = group.Count() }),
            Evidence = filtered.Take(effectiveLimit).Select(BuildProjectEvidencePayload),
            SafetyNote = "Project evidence entries are CCZModStudio reports, exports, previews, and write reports. Listing them is read-only."
        };
    }

    public object ReadProjectEvidence(string? gameRoot, string pathOrFile, int maxChars)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pathOrFile))
            {
                return new
                {
                    Found = false,
                    Error = "path_or_file is required.",
                    SafetyNote = "Evidence reading is limited to files discovered by ProjectEvidenceService under CCZModStudio report/export/backup evidence roots."
                };
            }

            var project = LoadProject(gameRoot);
            var item = ResolveProjectEvidenceItem(project, pathOrFile);
            var extension = Path.GetExtension(item.FullPath);
            var effectiveMaxChars = NormalizeLimit(maxChars, 20000, 100000);
            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                return new
                {
                    Found = true,
                    project.GameRoot,
                    Evidence = BuildProjectEvidencePayload(item),
                    Text = string.Empty,
                    TextLength = 0,
                    Truncated = false,
                    MaxChars = effectiveMaxChars,
                    SafetyNote = "PNG evidence is returned as metadata only. Use the ExportPath/FullPath to inspect the image in a UI."
                };
            }

            if (!IsTextEvidenceExtension(extension))
            {
                return new
                {
                    Found = false,
                    project.GameRoot,
                    Evidence = BuildProjectEvidencePayload(item),
                    Error = $"Evidence extension {extension} is not readable as text through MCP.",
                    SafetyNote = "Evidence reading is limited to files discovered by ProjectEvidenceService under CCZModStudio report/export/backup evidence roots."
                };
            }

            var text = File.ReadAllText(item.FullPath);
            var originalLength = text.Length;
            var truncated = text.Length > effectiveMaxChars;
            if (truncated)
            {
                text = text[..effectiveMaxChars];
            }

            return new
            {
                Found = true,
                project.GameRoot,
                Evidence = BuildProjectEvidencePayload(item),
                Text = text,
                TextLength = originalLength,
                ReturnedChars = text.Length,
                Truncated = truncated,
                MaxChars = effectiveMaxChars,
                SafetyNote = "Evidence reading is limited to files discovered by ProjectEvidenceService under CCZModStudio report/export/backup evidence roots."
            };
        }
        catch (Exception ex)
        {
            return new
            {
                Found = false,
                Error = ex.GetType().Name + ": " + ex.Message,
                Query = pathOrFile ?? string.Empty,
                SafetyNote = "Evidence reading is limited to files discovered by ProjectEvidenceService under CCZModStudio report/export/backup evidence roots."
            };
        }
    }

    public object WriteProjectDeliveryReport(
        string? gameRoot,
        bool includeResourceDiagnostics,
        bool includeScenarioMapLinks,
        bool includeCreatorNotes)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var auditItems = _projectAudit.Analyze(project, tables);
        var diffItems = SafeAnalyzeDiff(project);
        var backupItems = SafeScanBackups(project);
        var resources = includeResourceDiagnostics || includeScenarioMapLinks
            ? _gameResourceIndexer.Index(project)
            : Array.Empty<ResourceIndexItem>();
        var resourceDiagnostics = includeResourceDiagnostics
            ? _resourceDiagnosticService.Analyze(resources)
            : Array.Empty<ResourceDiagnosticItem>();
        var scenarioMapLinks = includeScenarioMapLinks
            ? BuildScenarioMapLinks(project, resources)
            : Array.Empty<ScenarioMapLinkInfo>();
        var creatorNotes = includeCreatorNotes
            ? _creatorNoteService.Load(project)
            : Array.Empty<CreatorNote>();

        var reportPath = _projectDeliveryReportService.WriteReport(
            project,
            tables,
            auditItems,
            diffItems,
            backupItems,
            resourceDiagnostics,
            scenarioMapLinks,
            creatorNotes);
        var reportInfo = new FileInfo(reportPath);

        return new
        {
            project.GameRoot,
            ReportPath = reportPath,
            ReportFileName = reportInfo.Name,
            SizeBytes = reportInfo.Length,
            IncludeResourceDiagnostics = includeResourceDiagnostics,
            IncludeScenarioMapLinks = includeScenarioMapLinks,
            IncludeCreatorNotes = includeCreatorNotes,
            Counts = new
            {
                Tables = tables.Count,
                AuditItems = auditItems.Count,
                DiffItems = diffItems.Count,
                BackupItems = backupItems.Count,
                ResourceDiagnostics = resourceDiagnostics.Count,
                ScenarioMapLinks = scenarioMapLinks.Count,
                CreatorNotes = creatorNotes.Count
            },
            SafetyNote = "Delivery report writes only a Markdown report under CCZModStudio_Reports and does not modify game files."
        };
    }

    public object ListCreatorNotes(string? gameRoot, string? scope, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var notes = _creatorNoteService.Load(project);
        var effectiveLimit = NormalizeLimit(limit, 100, 1000);
        var filtered = notes
            .Where(note => string.IsNullOrWhiteSpace(scope) || note.Scope.Contains(scope, StringComparison.OrdinalIgnoreCase))
            .Where(note => string.IsNullOrWhiteSpace(keyword) || MatchesCreatorNoteKeyword(note, keyword))
            .ToList();

        return new
        {
            project.GameRoot,
            StorePath = _creatorNoteService.GetStorePath(project),
            TotalNotes = filtered.Count,
            ReturnedNotes = Math.Min(filtered.Count, effectiveLimit),
            ScopeCounts = notes
                .GroupBy(note => note.Scope)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { Scope = group.Key, Count = group.Count() }),
            Summary = _creatorNoteService.BuildSummary(project, notes),
            Notes = filtered.Take(effectiveLimit).Select(BuildCreatorNotePayload),
            SafetyNote = "Creator notes are project-side JSON records under CCZModStudio_Notes. They do not modify game files and are excluded from release copies."
        };
    }

    public object UpsertCreatorNote(
        string? gameRoot,
        string? id,
        string? scope,
        string? targetKey,
        string? title,
        string content,
        string? tags,
        string? sourceHint)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("content is required for a creator note.");
        }

        var project = LoadProject(gameRoot);
        var saved = _creatorNoteService.Upsert(project, new CreatorNote
        {
            Id = id ?? string.Empty,
            Scope = scope ?? "全局项目",
            TargetKey = targetKey ?? string.Empty,
            Title = title ?? string.Empty,
            Content = content,
            Tags = tags ?? string.Empty,
            SourceHint = sourceHint ?? "MCP"
        });

        return new
        {
            project.GameRoot,
            StorePath = _creatorNoteService.GetStorePath(project),
            Note = BuildCreatorNotePayload(saved),
            SafetyNote = "Saved under CCZModStudio_Notes with JSON backup on overwrite; no game files were modified."
        };
    }

    public object DeleteCreatorNote(string? gameRoot, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("id is required.");
        }

        var project = LoadProject(gameRoot);
        var removed = _creatorNoteService.Delete(project, id);
        return new
        {
            project.GameRoot,
            StorePath = _creatorNoteService.GetStorePath(project),
            Id = id,
            Removed = removed,
            SafetyNote = "Deletion only updates the project-side creator notes JSON under CCZModStudio_Notes."
        };
    }

    public object ExportCreatorNotesCsv(string? gameRoot, string? scope, string? keyword)
    {
        var project = LoadProject(gameRoot);
        var notes = _creatorNoteService.Load(project)
            .Where(note => string.IsNullOrWhiteSpace(scope) || note.Scope.Contains(scope, StringComparison.OrdinalIgnoreCase))
            .Where(note => string.IsNullOrWhiteSpace(keyword) || MatchesCreatorNoteKeyword(note, keyword))
            .ToList();
        var path = _creatorNoteService.ExportCsv(project, notes);
        return new
        {
            project.GameRoot,
            ExportPath = path,
            ExportedNotes = notes.Count,
            SafetyNote = "CSV export is written under CCZModStudio_Exports/CreatorNotes and does not modify game files."
        };
    }

    public object ListKnowledgeEntries()
    {
        var root = FindKnowledgeRoot();
        var entries = Directory.Exists(root)
            ? Directory.GetFiles(root, "*.md", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase)
                .Select(path => new
                {
                    Name = Path.GetFileName(path),
                    RelativePath = Path.GetRelativePath(root, path),
                    Path = path,
                    SizeBytes = new FileInfo(path).Length
                })
                .ToList()
            : [];

        return new
        {
            KnowledgeRoot = root,
            Count = entries.Count,
            Entries = entries
        };
    }

    public object ReadKnowledgeEntry(string name)
    {
        var root = FindKnowledgeRoot();
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var fullPath = ResolveKnowledgeEntryPath(root, name);
        if (!File.Exists(fullPath) && !name.Contains('/') && !name.Contains('\\'))
        {
            var matches = Directory.GetFiles(root, "*.md", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 1)
            {
                fullPath = matches[0];
            }
            else if (matches.Count > 1)
            {
                var candidates = string.Join(", ", matches.Take(12).Select(path => Path.GetRelativePath(root, path)));
                throw new InvalidOperationException($"Knowledge entry name matched multiple files. Use a relative path. Matches: {candidates}");
            }
        }

        if (!File.Exists(fullPath)) throw new FileNotFoundException("Knowledge entry was not found.", fullPath);
        return new
        {
            Name = Path.GetFileName(fullPath),
            RelativePath = Path.GetRelativePath(root, fullPath),
            Path = fullPath,
            Text = File.ReadAllText(fullPath)
        };
    }

    public object SearchKnowledgeEntries(string keyword, int limit, int contextLines)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            throw new InvalidOperationException("keyword is required.");
        }

        var root = FindKnowledgeRoot();
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var effectiveLimit = NormalizeLimit(limit, 50, 500);
        var effectiveContextLines = contextLines < 0 ? 0 : Math.Min(contextLines, 3);
        var matches = new List<object>();
        var totalMatches = 0;

        foreach (var path in Directory.GetFiles(root, "*.md", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                totalMatches++;
                if (matches.Count >= effectiveLimit)
                {
                    continue;
                }

                var snippetStart = Math.Max(0, i - effectiveContextLines);
                var snippetEnd = Math.Min(lines.Length - 1, i + effectiveContextLines);
                matches.Add(new
                {
                    Name = Path.GetFileName(path),
                    RelativePath = Path.GetRelativePath(root, path),
                    Path = path,
                    LineNumber = i + 1,
                    MatchedLine = lines[i].Trim(),
                    Snippet = string.Join(Environment.NewLine, lines.Skip(snippetStart).Take(snippetEnd - snippetStart + 1))
                });
            }
        }

        return new
        {
            KnowledgeRoot = root,
            Keyword = keyword,
            TotalMatches = totalMatches,
            ReturnedMatches = matches.Count,
            ContextLines = effectiveContextLines,
            Matches = matches
        };
    }
}
