using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CmfDesignerExtractionService
{
    private sealed record CmfDesignerSnapshotCandidate(CmfDesignerSnapshot Snapshot, DateTime LastWriteTimeUtc);

    private const int CbGetCount = 0x0146;
    private const int CbGetLbText = 0x0148;
    private const int CbGetLbTextLen = 0x0149;
    private const int LbGetCount = 0x018B;
    private const int LbGetText = 0x0189;
    private const int LbGetTextLen = 0x018A;
    private const int BmClick = 0x00F5;
    private const int WmCommand = 0x0111;
    private const int CbnEditChange = 5;
    private const int IdOk = 1;
    private const int DesignerOpenCommand = 57601;
    private const int GwlStyle = -16;
    private const int GwChild = 5;
    private const int GwHwndNext = 2;
    private const int SwShow = 5;
    private const int BsGroupBox = 0x0007;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string DropDownClassName => "Combo" + "Box";

    private static bool IsDropDownClass(string className)
        => className.Contains(DropDownClassName, StringComparison.OrdinalIgnoreCase);

    public CmfDesignerExtractionResult ExtractDesignerSnapshot(
        CczProject project,
        string relativePath,
        CmfDesignerExtractionOptions? options = null)
    {
        options ??= new CmfDesignerExtractionOptions();
        var mode = NormalizeMode(options.Mode);
        var oldToolsRoot = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot)
            ?? throw new DirectoryNotFoundException("Old tools CMF root was not found.");
        var cmfPath = ResolveCmfPath(oldToolsRoot, relativePath);
        if (!File.Exists(cmfPath))
        {
            throw new FileNotFoundException("CMF file was not found.", cmfPath);
        }

        var sourceSha = ComputeSha256(cmfPath);
        var sourceLength = new FileInfo(cmfPath).Length;
        var cheatMakerExe = ResolveCheatMakerExe(oldToolsRoot, options.CheatMakerExePath);
        var warnings = new List<string>();
        var reportDirectory = CreateReportDirectory(project, cmfPath);

        CmfDesignerSnapshot snapshot;
        if (!string.IsNullOrWhiteSpace(options.FixtureSnapshotPath))
        {
            snapshot = NormalizeSnapshotMetadata(
                LoadSnapshotFile(options.FixtureSnapshotPath),
                cmfPath,
                oldToolsRoot,
                sourceSha,
                sourceLength,
                cheatMakerExe,
                mode,
                reportDirectory,
                ["Loaded designer snapshot from fixture/import file: " + options.FixtureSnapshotPath]);
        }
        else if (mode.Equals("StaticOnly", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("StaticOnly mode records source identity and report files only; use UiProbe to launch CheatMaker and enumerate the designer UI.");
            snapshot = BuildEmptySnapshot(cmfPath, oldToolsRoot, sourceSha, sourceLength, cheatMakerExe, mode, reportDirectory, warnings);
        }
        else
        {
            snapshot = ProbeDesignerUi(project, oldToolsRoot, cmfPath, sourceSha, sourceLength, cheatMakerExe, mode, reportDirectory, options);
            warnings.AddRange(snapshot.Warnings);
        }

        var afterSha = ComputeSha256(cmfPath);
        if (!afterSha.Equals(sourceSha, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Original CMF SHA256 changed during extraction. Treat this run as invalid and inspect the source file.");
        }

        snapshot = NormalizeSnapshotMetadata(
            snapshot,
            cmfPath,
            oldToolsRoot,
            sourceSha,
            sourceLength,
            cheatMakerExe,
            mode,
            reportDirectory,
            warnings.Concat(snapshot.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        return WriteReports(snapshot, reportDirectory);
    }

    public CmfDesignerSnapshot? LoadLatestSnapshot(CczProject project, string relativePath)
    {
        var oldToolsRoot = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot);
        if (string.IsNullOrWhiteSpace(oldToolsRoot)) return null;

        var cmfPath = ResolveCmfPath(oldToolsRoot, relativePath);
        var root = Path.Combine(GetReportRoot(project), BuildSafeReportName(cmfPath));
        if (!Directory.Exists(root)) return null;

        var sourceSha = File.Exists(cmfPath) ? ComputeSha256(cmfPath) : string.Empty;
        var snapshot = Directory
            .EnumerateFiles(root, "snapshot.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Select(info => TryLoadSnapshotCandidate(info))
            .Where(candidate => candidate != null)
            .Select(candidate => candidate!)
            .Where(candidate => IsAutoLoadableSnapshot(candidate.Snapshot, sourceSha))
            .OrderByDescending(candidate => GetAutoLoadPriority(candidate.Snapshot))
            .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.Snapshot;

        return snapshot;
    }

    public CmfDesignerSnapshot LoadSnapshotFile(string snapshotPath)
    {
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            throw new InvalidOperationException("Designer snapshot path is required.");
        }

        if (!File.Exists(snapshotPath))
        {
            throw new FileNotFoundException("Designer snapshot file was not found.", snapshotPath);
        }

        var snapshot = JsonSerializer.Deserialize<CmfDesignerSnapshot>(File.ReadAllText(snapshotPath, Encoding.UTF8), JsonOptions);
        return snapshot ?? throw new InvalidOperationException("Designer snapshot JSON was empty or invalid: " + snapshotPath);
    }

    private CmfDesignerSnapshotCandidate? TryLoadSnapshotCandidate(FileInfo info)
    {
        try
        {
            return new CmfDesignerSnapshotCandidate(LoadSnapshotFile(info.FullName), info.LastWriteTimeUtc);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAutoLoadableSnapshot(CmfDesignerSnapshot snapshot, string sourceSha)
    {
        if (IsFixtureOrImportReport(snapshot)) return false;
        if (!string.IsNullOrWhiteSpace(sourceSha) &&
            !string.IsNullOrWhiteSpace(snapshot.SourceSha256) &&
            !sourceSha.Equals(snapshot.SourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int GetAutoLoadPriority(CmfDesignerSnapshot snapshot)
    {
        if (snapshot.ExtractionMode.Equals("UiProbe", StringComparison.OrdinalIgnoreCase))
        {
            return snapshot.Bindings.Count > 0 ? 400 : 300;
        }

        return snapshot.Bindings.Count > 0 ? 200 : 100;
    }

    private static bool IsFixtureOrImportReport(CmfDesignerSnapshot snapshot)
        => snapshot.ExtractionMode.Equals("Fixture", StringComparison.OrdinalIgnoreCase) ||
           snapshot.Warnings.Any(warning =>
               warning.Contains("fixture/import file", StringComparison.OrdinalIgnoreCase) ||
               warning.Contains("fixture", StringComparison.OrdinalIgnoreCase));

    public string ResolveCmfPath(CczProject project, string relativePath)
    {
        var oldToolsRoot = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot)
            ?? throw new DirectoryNotFoundException("Old tools CMF root was not found.");
        return ResolveCmfPath(oldToolsRoot, relativePath);
    }

    private static CmfDesignerSnapshot ProbeDesignerUi(
        CczProject project,
        string oldToolsRoot,
        string cmfPath,
        string sourceSha,
        long sourceLength,
        string cheatMakerExe,
        string mode,
        string reportDirectory,
        CmfDesignerExtractionOptions options)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(cheatMakerExe) || !File.Exists(cheatMakerExe))
        {
            warnings.Add("CheatMaker.exe was not found under the old tool bundle. Designer UI extraction could not start.");
            return BuildEmptySnapshot(cmfPath, oldToolsRoot, sourceSha, sourceLength, cheatMakerExe, mode, reportDirectory, warnings);
        }

        var tempDir = string.Empty;
        var workingCmf = cmfPath;
        Process? process = null;
        try
        {
            if (options.UseTempCopy)
            {
                tempDir = Path.Combine(Path.GetTempPath(), "CCZModStudio_CmfDesigner_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                workingCmf = Path.Combine(tempDir, Path.GetFileName(cmfPath));
                File.Copy(cmfPath, workingCmf, overwrite: true);
            }

            var start = new ProcessStartInfo
            {
                FileName = cheatMakerExe,
                WorkingDirectory = Path.GetDirectoryName(cheatMakerExe) ?? oldToolsRoot,
                Arguments = "\"" + workingCmf + "\"",
                UseShellExecute = false
            };
            process = Process.Start(start);
            if (process == null)
            {
                warnings.Add("CheatMaker process did not start.");
                return BuildEmptySnapshot(cmfPath, oldToolsRoot, sourceSha, sourceLength, cheatMakerExe, mode, reportDirectory, warnings);
            }

            try
            {
                process.WaitForInputIdle(Math.Min(Math.Max(options.TimeoutMs, 1000), 30000));
            }
            catch
            {
                // Some legacy Win32 apps do not expose input-idle state reliably.
            }

            var mainWindow = WaitForMainWindow(process, options.TimeoutMs);
            if (mainWindow == IntPtr.Zero)
            {
                warnings.Add("CheatMaker main window was not found before timeout.");
                return BuildEmptySnapshot(cmfPath, oldToolsRoot, sourceSha, sourceLength, cheatMakerExe, mode, reportDirectory, warnings);
            }

            var captureWindow = TryPrepareDesignerWindow(process.Id, mainWindow, workingCmf, options.TimeoutMs, warnings);
            var rawRoot = BuildRawUiTree(captureWindow, process.Id);
            var pageNodes = FindDesignerPageNodes(rawRoot).ToArray();
            var pages = BuildPagesFromRawTree(rawRoot, pageNodes).ToArray();
            var controls = BuildControlsFromRawTree(rawRoot, pages, pageNodes).ToArray();
            var modules = BuildModulesFromControls(pages, controls).ToArray();
            var bindings = BuildBindingsFromPropertyCandidates(rawRoot, pages, controls, modules, cmfPath).ToArray();

            if (mode.Equals("UiProbe", StringComparison.OrdinalIgnoreCase) && bindings.Length == 0)
            {
                warnings.Add("UI probe captured the Win32 tree but did not find an exposed 地址(HEX) property row. CheatMaker may use a custom-drawn property grid; use the raw-ui-tree report to extend the reader or import a designer snapshot fixture.");
            }

            return new CmfDesignerSnapshot
            {
                SourcePath = cmfPath,
                RelativePath = Path.GetRelativePath(oldToolsRoot, cmfPath),
                SourceSha256 = sourceSha,
                SourceLength = sourceLength,
                ExtractedAtUtc = DateTime.UtcNow,
                CheatMakerExePath = cheatMakerExe,
                CheatMakerVersion = GetFileVersion(cheatMakerExe),
                ExtractionMode = mode,
                ReportDirectory = reportDirectory,
                Pages = pages,
                Modules = modules,
                Controls = controls,
                Bindings = bindings,
                RawUiTree = rawRoot,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            warnings.Add("Designer UI extraction failed: " + ex.Message);
            return BuildEmptySnapshot(cmfPath, oldToolsRoot, sourceSha, sourceLength, cheatMakerExe, mode, reportDirectory, warnings);
        }
        finally
        {
            if (!options.KeepProcessOpen && process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(2000))
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                }
                catch
                {
                    // Extraction must remain best-effort and read-only.
                }
            }

            if (!string.IsNullOrWhiteSpace(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Temp cleanup is best effort.
                }
            }
        }
    }

    private static CmfDesignerSnapshot BuildEmptySnapshot(
        string cmfPath,
        string oldToolsRoot,
        string sourceSha,
        long sourceLength,
        string cheatMakerExe,
        string mode,
        string reportDirectory,
        IReadOnlyList<string> warnings)
        => new()
        {
            SourcePath = cmfPath,
            RelativePath = Path.GetRelativePath(oldToolsRoot, cmfPath),
            SourceSha256 = sourceSha,
            SourceLength = sourceLength,
            ExtractedAtUtc = DateTime.UtcNow,
            CheatMakerExePath = cheatMakerExe,
            CheatMakerVersion = GetFileVersion(cheatMakerExe),
            ExtractionMode = mode,
            ReportDirectory = reportDirectory,
            Warnings = warnings
        };

    private static CmfDesignerSnapshot NormalizeSnapshotMetadata(
        CmfDesignerSnapshot snapshot,
        string cmfPath,
        string oldToolsRoot,
        string sourceSha,
        long sourceLength,
        string cheatMakerExe,
        string mode,
        string reportDirectory,
        IReadOnlyList<string> warnings)
        => new()
        {
            SchemaVersion = string.IsNullOrWhiteSpace(snapshot.SchemaVersion) ? "1.0" : snapshot.SchemaVersion,
            SourcePath = cmfPath,
            RelativePath = Path.GetRelativePath(oldToolsRoot, cmfPath),
            SourceSha256 = sourceSha,
            SourceLength = sourceLength,
            ExtractedAtUtc = snapshot.ExtractedAtUtc == default ? DateTime.UtcNow : snapshot.ExtractedAtUtc,
            CheatMakerExePath = string.IsNullOrWhiteSpace(snapshot.CheatMakerExePath) ? cheatMakerExe : snapshot.CheatMakerExePath,
            CheatMakerVersion = string.IsNullOrWhiteSpace(snapshot.CheatMakerVersion) ? GetFileVersion(cheatMakerExe) : snapshot.CheatMakerVersion,
            ExtractionMode = mode,
            ReportDirectory = reportDirectory,
            Pages = snapshot.Pages,
            Modules = snapshot.Modules,
            Controls = snapshot.Controls,
            Bindings = snapshot.Bindings,
            RawUiTree = snapshot.RawUiTree,
            Warnings = warnings
        };

    private static CmfDesignerExtractionResult WriteReports(CmfDesignerSnapshot snapshot, string reportDirectory)
    {
        Directory.CreateDirectory(reportDirectory);
        var snapshotJsonPath = Path.Combine(reportDirectory, "snapshot.json");
        var rawUiTreeJsonPath = Path.Combine(reportDirectory, "raw-ui-tree.json");
        var fieldsCsvPath = Path.Combine(reportDirectory, "fields.csv");
        var modulesMarkdownPath = Path.Combine(reportDirectory, "modules.md");
        var addressesMarkdownPath = Path.Combine(reportDirectory, "addresses.md");

        File.WriteAllText(snapshotJsonPath, JsonSerializer.Serialize(snapshot, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(rawUiTreeJsonPath, JsonSerializer.Serialize(snapshot.RawUiTree, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(fieldsCsvPath, BuildFieldsCsv(snapshot), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(modulesMarkdownPath, BuildModulesMarkdown(snapshot), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(addressesMarkdownPath, BuildAddressesMarkdown(snapshot), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        return new CmfDesignerExtractionResult
        {
            Snapshot = snapshot,
            ReportDirectory = reportDirectory,
            SnapshotJsonPath = snapshotJsonPath,
            FieldsCsvPath = fieldsCsvPath,
            ModulesMarkdownPath = modulesMarkdownPath,
            AddressesMarkdownPath = addressesMarkdownPath,
            RawUiTreeJsonPath = rawUiTreeJsonPath,
            Warnings = snapshot.Warnings
        };
    }

    private static string BuildFieldsCsv(CmfDesignerSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("source_cmf,page,module,control_type,control_name,display_name,ue_offset_hex,byte_length,data_type,function_type,default_value,validation_status,data_list_raw");
        foreach (var binding in snapshot.Bindings.OrderBy(binding => binding.UeOffset ?? long.MaxValue).ThenBy(binding => binding.ControlName, StringComparer.OrdinalIgnoreCase))
        {
            var page = snapshot.Pages.FirstOrDefault(item => item.PageId.Equals(binding.PageId, StringComparison.OrdinalIgnoreCase));
            var module = snapshot.Modules.FirstOrDefault(item => item.ModuleId.Equals(binding.ModuleId, StringComparison.OrdinalIgnoreCase));
            builder.AppendLine(string.Join(",",
                Csv(snapshot.RelativePath),
                Csv(page?.Name ?? binding.PageId),
                Csv(module?.Title ?? binding.ModuleId),
                Csv(binding.ControlType),
                Csv(binding.ControlName),
                Csv(binding.DisplayName),
                Csv(binding.UeOffsetHex),
                binding.ByteLength.ToString(CultureInfo.InvariantCulture),
                Csv(binding.DataType),
                Csv(binding.FunctionType),
                Csv(binding.DefaultValueRaw),
                Csv(binding.ValidationStatus),
                Csv(binding.DataListRaw)));
        }

        return builder.ToString();
    }

    private static string BuildModulesMarkdown(CmfDesignerSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# CheatMaker Designer Modules");
        builder.AppendLine();
        builder.AppendLine("- Source CMF: `" + snapshot.RelativePath + "`");
        builder.AppendLine("- SHA256: `" + snapshot.SourceSha256 + "`");
        builder.AppendLine("- Extraction mode: `" + snapshot.ExtractionMode + "`");
        builder.AppendLine();

        if (snapshot.Pages.Count == 0)
        {
            builder.AppendLine("No designer pages were extracted.");
            return builder.ToString();
        }

        foreach (var page in snapshot.Pages)
        {
            builder.AppendLine("## " + MarkdownText(page.Name));
            var pageModules = snapshot.Modules.Where(module => module.PageId.Equals(page.PageId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (pageModules.Length == 0)
            {
                builder.AppendLine();
                builder.AppendLine("No modules were classified for this page.");
                builder.AppendLine();
                continue;
            }

            foreach (var module in pageModules)
            {
                builder.AppendLine();
                builder.AppendLine("### " + MarkdownText(module.Title));
                foreach (var note in module.Notes.Where(note => !string.IsNullOrWhiteSpace(note.Text)))
                {
                    builder.AppendLine("- Note: " + MarkdownText(note.Text));
                }

                var moduleControls = snapshot.Controls
                    .Where(control => module.ControlIds.Contains(control.ControlId, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                var buttons = moduleControls
                    .Where(control => control.ControlType.Equals("Button", StringComparison.OrdinalIgnoreCase))
                    .Select(control => string.IsNullOrWhiteSpace(control.Text)
                        ? control.Name
                        : control.Text + " (" + control.Name + ")")
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (buttons.Length > 0)
                {
                    builder.AppendLine("- Buttons: " + MarkdownText(string.Join("; ", buttons)));
                }

                var bindings = snapshot.Bindings
                    .Where(binding => binding.ModuleId.Equals(module.ModuleId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(binding => binding.UeOffset ?? long.MaxValue)
                    .ThenBy(binding => binding.ControlName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (bindings.Length == 0)
                {
                    builder.AppendLine("- Fields: none");
                    continue;
                }

                builder.AppendLine();
                builder.AppendLine("| Field | Control | UE Offset | Length | Type | Function | Status |");
                builder.AppendLine("|---|---|---:|---:|---|---|---|");
                foreach (var binding in bindings)
                {
                    builder.AppendLine(
                        "| " + MarkdownCell(binding.DisplayName) +
                        " | " + MarkdownCell(binding.ControlName) +
                        " | `" + binding.UeOffsetHex + "`" +
                        " | " + binding.ByteLength.ToString(CultureInfo.InvariantCulture) +
                        " | " + MarkdownCell(binding.DataType) +
                        " | " + MarkdownCell(binding.FunctionType) +
                        " | " + MarkdownCell(binding.ValidationStatus) + " |");
                }

                var scriptSummaries = bindings
                    .Where(binding => !string.IsNullOrWhiteSpace(binding.Script))
                    .Select(binding => binding.ControlName + ": " + CollapseMultiline(binding.Script, 240))
                    .ToArray();
                foreach (var script in scriptSummaries)
                {
                    builder.AppendLine("- Script: " + MarkdownText(script));
                }

                var manualReview = bindings
                    .Where(binding => binding.ValidationStatus.Equals("NeedsManualReview", StringComparison.OrdinalIgnoreCase))
                    .Select(binding => string.IsNullOrWhiteSpace(binding.DisplayName)
                        ? binding.ControlName
                        : binding.DisplayName + " (" + binding.ControlName + ")")
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                if (manualReview.Length > 0)
                {
                    builder.AppendLine("- Needs manual review: " + MarkdownText(string.Join("; ", manualReview)));
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildAddressesMarkdown(CmfDesignerSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# CheatMaker Designer UE Addresses");
        builder.AppendLine();
        builder.AppendLine("- Source CMF: `" + snapshot.RelativePath + "`");
        builder.AppendLine("- Address rule: `地址(HEX)` is recorded as UE file offset first. OD/VA mapping is a later validation step.");
        builder.AppendLine();
        builder.AppendLine("| UE Offset | Length | Page | Module | Control | Field | Data Type | Function Type | Default | Status |");
        builder.AppendLine("|---:|---:|---|---|---|---|---|---|---|---|");
        foreach (var binding in snapshot.Bindings
                     .Where(binding => binding.UeOffset.HasValue || !string.IsNullOrWhiteSpace(binding.UeOffsetHex))
                     .OrderBy(binding => binding.UeOffset ?? long.MaxValue)
                     .ThenBy(binding => binding.ControlName, StringComparer.OrdinalIgnoreCase))
        {
            var page = snapshot.Pages.FirstOrDefault(item => item.PageId.Equals(binding.PageId, StringComparison.OrdinalIgnoreCase));
            var module = snapshot.Modules.FirstOrDefault(item => item.ModuleId.Equals(binding.ModuleId, StringComparison.OrdinalIgnoreCase));
            builder.AppendLine(
                "| `" + binding.UeOffsetHex + "`" +
                " | " + binding.ByteLength.ToString(CultureInfo.InvariantCulture) +
                " | " + MarkdownCell(page?.Name ?? binding.PageId) +
                " | " + MarkdownCell(module?.Title ?? binding.ModuleId) +
                " | " + MarkdownCell(binding.ControlName) +
                " | " + MarkdownCell(binding.DisplayName) +
                " | " + MarkdownCell(binding.DataType) +
                " | " + MarkdownCell(binding.FunctionType) +
                " | " + MarkdownCell(binding.DefaultValueRaw) +
                " | " + MarkdownCell(binding.ValidationStatus) + " |");
        }

        return builder.ToString();
    }

    private static IEnumerable<CmfDesignerPage> BuildPagesFromRawTree(
        CmfDesignerRawNode root,
        IReadOnlyList<CmfDesignerRawNode> pageNodes)
    {
        if (pageNodes.Count == 0)
        {
            yield return new CmfDesignerPage
            {
                PageId = BuildStableId(root.Handle, "page", root.Text + root.Bounds),
                Name = string.IsNullOrWhiteSpace(root.Text) ? "CheatMaker Designer" : root.Text,
                WindowTitle = root.Text,
                Bounds = root.Bounds
            };
            yield break;
        }

        foreach (var node in pageNodes)
        {
            yield return new CmfDesignerPage
            {
                PageId = BuildStableId(root.Handle, "page", node.Handle + "|" + node.Text),
                Name = string.IsNullOrWhiteSpace(node.Text) ? "CheatMaker Designer Page" : node.Text,
                WindowTitle = node.Text,
                Bounds = node.Bounds
            };
        }
    }

    private static IEnumerable<CmfDesignerControl> BuildControlsFromRawTree(
        CmfDesignerRawNode root,
        IReadOnlyList<CmfDesignerPage> pages,
        IReadOnlyList<CmfDesignerRawNode> pageNodes)
    {
        var index = 0;
        if (pageNodes.Count == 0)
        {
            foreach (var control in BuildControlsForPage(root, pages.FirstOrDefault()?.PageId ?? "page-main", root, ref index))
            {
                yield return control;
            }

            yield break;
        }

        var pageById = pages.ToDictionary(page => page.PageId, StringComparer.OrdinalIgnoreCase);
        foreach (var pageNode in pageNodes)
        {
            var pageId = BuildStableId(root.Handle, "page", pageNode.Handle + "|" + pageNode.Text);
            if (!pageById.ContainsKey(pageId)) continue;
            foreach (var control in BuildControlsForPage(root, pageId, pageNode, ref index))
            {
                yield return control;
            }
        }
    }

    private static IReadOnlyList<CmfDesignerControl> BuildControlsForPage(
        CmfDesignerRawNode root,
        string pageId,
        CmfDesignerRawNode pageNode,
        ref int index)
    {
        var controls = new List<CmfDesignerControl>();
        foreach (var node in FlattenRawNodes(pageNode).Where(node => node != pageNode && !IsDesignerPageNode(node, root)))
        {
            var controlType = ClassifyControlType(node.ClassName, node.Properties);
            controls.Add(new CmfDesignerControl
            {
                ControlId = BuildStableId(root.Handle, "control", index.ToString(CultureInfo.InvariantCulture) + "|" + node.Handle + "|" + node.ClassName + "|" + node.Text),
                PageId = pageId,
                ControlType = controlType,
                Name = node.Properties.TryGetValue("AutomationName", out var automationName) && !string.IsNullOrWhiteSpace(automationName)
                    ? automationName
                    : node.Handle,
                Text = node.Text,
                Bounds = node.Bounds,
                Properties = node.Properties
            });
            index++;
        }

        return controls;
    }

    private static IEnumerable<CmfDesignerRawNode> FindDesignerPageNodes(CmfDesignerRawNode root)
    {
        var candidates = FlattenRawNodes(root)
            .Where(node => node != root && IsDesignerPageNode(node, root))
            .DistinctBy(node => node.Handle, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0) return Array.Empty<CmfDesignerRawNode>();

        var deepest = candidates
            .Where(candidate => !candidates.Any(other =>
                !ReferenceEquals(candidate, other) &&
                IsRawNodeAncestor(candidate, other)))
            .OrderBy(node => node.Text, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(node => node.Handle, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return deepest.Length == 0 ? candidates : deepest;
    }

    private static bool IsDesignerPageNode(CmfDesignerRawNode node, CmfDesignerRawNode root)
    {
        if (ReferenceEquals(node, root)) return false;
        if (string.IsNullOrWhiteSpace(node.Text)) return false;
        if (node.Bounds.IsEmpty || node.Bounds.Width < 180 || node.Bounds.Height < 120) return false;
        if (!node.ClassName.StartsWith("Afx:", StringComparison.OrdinalIgnoreCase)) return false;
        if (node.ClassName.Contains("ToolBar", StringComparison.OrdinalIgnoreCase) ||
            node.ClassName.Contains("ControlBar", StringComparison.OrdinalIgnoreCase) ||
            node.ClassName.Contains("PropList", StringComparison.OrdinalIgnoreCase) ||
            node.ClassName.Contains("DockPane", StringComparison.OrdinalIgnoreCase) ||
            node.ClassName.Contains("StatusBar", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsRawNodeAncestor(CmfDesignerRawNode ancestor, CmfDesignerRawNode descendant)
    {
        foreach (var child in ancestor.Children)
        {
            if (ReferenceEquals(child, descendant) || IsRawNodeAncestor(child, descendant))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<CmfDesignerModule> BuildModulesFromControls(
        IReadOnlyList<CmfDesignerPage> pages,
        IReadOnlyList<CmfDesignerControl> controls)
    {
        foreach (var page in pages)
        {
            var pageControls = controls.Where(control => control.PageId.Equals(page.PageId, StringComparison.OrdinalIgnoreCase)).ToArray();
            var groupBoxes = pageControls
                .Where(control => control.ControlType.Equals("GroupBox", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (groupBoxes.Length == 0 && pageControls.Length > 0)
            {
                var allIds = pageControls.Select(control => control.ControlId).ToArray();
                yield return new CmfDesignerModule
                {
                    ModuleId = BuildStableId(page.PageId, "module", page.Name),
                    PageId = page.PageId,
                    Title = page.Name,
                    Bounds = page.Bounds,
                    Notes = pageControls
                        .Where(control => control.ControlType.Equals("Label", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(control.Text))
                        .Select(control => new CmfModuleNote { Text = control.Text, Bounds = control.Bounds, SourceControlId = control.ControlId })
                        .ToArray(),
                    ControlIds = allIds
                };
                continue;
            }

            foreach (var groupBox in groupBoxes)
            {
                var contained = pageControls
                    .Where(control => groupBox.Bounds.Contains(control.Bounds) && !control.ControlId.Equals(groupBox.ControlId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                yield return new CmfDesignerModule
                {
                    ModuleId = BuildStableId(page.PageId, "module", groupBox.ControlId),
                    PageId = page.PageId,
                    Title = string.IsNullOrWhiteSpace(groupBox.Text) ? groupBox.Name : groupBox.Text,
                    Bounds = groupBox.Bounds,
                    Notes = contained
                        .Where(control => control.ControlType.Equals("Label", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(control.Text))
                        .Select(control => new CmfModuleNote { Text = control.Text, Bounds = control.Bounds, SourceControlId = control.ControlId })
                        .ToArray(),
                    ControlIds = contained.Select(control => control.ControlId).Prepend(groupBox.ControlId).ToArray()
                };
            }
        }
    }

    private static IEnumerable<CmfDesignerBinding> BuildBindingsFromPropertyCandidates(
        CmfDesignerRawNode root,
        IReadOnlyList<CmfDesignerPage> pages,
        IReadOnlyList<CmfDesignerControl> controls,
        IReadOnlyList<CmfDesignerModule> modules,
        string cmfPath)
    {
        var properties = ExtractVisiblePropertyPairs(root);
        if (!TryGetProperty(properties, "地址(HEX)", out var addressText) &&
            !TryGetProperty(properties, "Address", out addressText) &&
            !TryGetProperty(properties, "Offset", out addressText))
        {
            yield break;
        }

        if (!TryParseHex(addressText, out var address))
        {
            yield break;
        }

        TryGetProperty(properties, "名称", out var controlName);
        TryGetProperty(properties, "控件类型", out var controlType);
        TryGetProperty(properties, "数据大小", out var byteLengthText);
        TryGetProperty(properties, "数据类型", out var dataTypeText);
        TryGetProperty(properties, "功能类型", out var functionType);
        TryGetProperty(properties, "默认值", out var defaultValue);
        TryGetProperty(properties, "数据列表", out var dataListRaw);
        TryGetProperty(properties, "脚本", out var script);

        var byteLength = TryParseInt(byteLengthText, out var parsedLength) ? parsedLength : 0;
        var page = pages.FirstOrDefault();
        var control = controls.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(controlName) &&
            (item.Name.Equals(controlName, StringComparison.OrdinalIgnoreCase) ||
             item.Text.Equals(controlName, StringComparison.OrdinalIgnoreCase)));
        var module = control == null
            ? modules.FirstOrDefault()
            : modules.FirstOrDefault(item => item.ControlIds.Contains(control.ControlId, StringComparer.OrdinalIgnoreCase)) ?? modules.FirstOrDefault();

        var displayName = !string.IsNullOrWhiteSpace(control?.Text)
            ? control.Text
            : !string.IsNullOrWhiteSpace(controlName)
                ? controlName
                : Path.GetFileNameWithoutExtension(cmfPath) + " field";
        var normalizedDataType = NormalizeDesignerDataType(dataTypeText);
        var validationStatus = byteLength <= 0 || string.IsNullOrWhiteSpace(normalizedDataType) || normalizedDataType.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? "NeedsManualReview"
            : "ExtractedFromDesigner";

        yield return new CmfDesignerBinding
        {
            BindingId = BuildStableId(cmfPath, "designer-binding", (controlName ?? string.Empty) + "|" + address.ToString("X", CultureInfo.InvariantCulture)),
            PageId = page?.PageId ?? string.Empty,
            ModuleId = module?.ModuleId ?? string.Empty,
            ControlId = control?.ControlId ?? string.Empty,
            ControlName = controlName ?? string.Empty,
            ControlType = controlType ?? control?.ControlType ?? string.Empty,
            DisplayName = displayName,
            TargetFile = InferTargetFileFromCmf(cmfPath),
            AddressKind = "UeFileOffset",
            UeOffsetHex = "0x" + address.ToString("X", CultureInfo.InvariantCulture),
            UeOffset = address,
            ByteLength = byteLength,
            DataType = normalizedDataType,
            FunctionType = functionType ?? string.Empty,
            DefaultValueRaw = defaultValue ?? string.Empty,
            DefaultValueParsed = ParseDefaultValue(defaultValue),
            DataListRaw = dataListRaw ?? string.Empty,
            Script = script ?? string.Empty,
            ValidationStatus = validationStatus,
            SourceProperties = properties
        };
    }

    private static Dictionary<string, string> ExtractVisiblePropertyPairs(CmfDesignerRawNode root)
    {
        var nodes = FlattenRawNodes(root)
            .Where(node => !string.IsNullOrWhiteSpace(node.Text) && !node.Bounds.IsEmpty)
            .ToArray();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in nodes.Where(node => IsDesignerPropertyName(node.Text)))
        {
            var value = nodes
                .Where(node => node != label &&
                               node.Bounds.Left >= label.Bounds.Right - 4 &&
                               Math.Abs(MidY(node.Bounds) - MidY(label.Bounds)) <= 10 &&
                               !IsDesignerPropertyName(node.Text))
                .OrderBy(node => node.Bounds.Left - label.Bounds.Right)
                .ThenBy(node => Math.Abs(MidY(node.Bounds) - MidY(label.Bounds)))
                .FirstOrDefault();
            if (value != null)
            {
                result[TrimPropertyName(label.Text)] = value.Text.Trim();
            }
        }

        return result;
    }

    private static bool IsDesignerPropertyName(string text)
    {
        var normalized = TrimPropertyName(text);
        return normalized.Equals("地址(HEX)", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("地址", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Address", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Offset", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("名称", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("控件类型", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("坐标", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("大小", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("数据列表", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("数据大小", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("默认值", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("功能类型", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("数据类型", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("列表高度", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("脚本", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetProperty(Dictionary<string, string> properties, string key, out string value)
    {
        if (properties.TryGetValue(key, out value!)) return true;
        foreach (var item in properties)
        {
            if (item.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                value = item.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static CmfDesignerRawNode BuildRawUiTree(IntPtr hwnd, int processId)
    {
        var handles = new List<IntPtr> { hwnd };
        var seen = new HashSet<IntPtr> { hwnd };
        EnumChildWindows(hwnd, (child, _) =>
        {
            if (handles.Count >= 3000) return false;
            if (seen.Add(child))
            {
                handles.Add(child);
            }

            return true;
        }, IntPtr.Zero);

        var handleSet = seen;
        var childrenByParent = new Dictionary<IntPtr, List<IntPtr>>();
        foreach (var child in handles.Skip(1))
        {
            var parent = GetParent(child);
            if (parent == IntPtr.Zero || !handleSet.Contains(parent))
            {
                parent = hwnd;
            }

            if (!childrenByParent.TryGetValue(parent, out var children))
            {
                children = [];
                childrenByParent[parent] = children;
            }

            children.Add(child);
        }

        var visited = new HashSet<IntPtr>();
        return BuildRawSubtree(hwnd, processId, childrenByParent, visited);
    }

    private static CmfDesignerRawNode BuildRawSubtree(
        IntPtr hwnd,
        int processId,
        IReadOnlyDictionary<IntPtr, List<IntPtr>> childrenByParent,
        HashSet<IntPtr> visited)
    {
        if (!visited.Add(hwnd))
        {
            return BuildRawNode(hwnd, processId, Array.Empty<CmfDesignerRawNode>());
        }

        var children = new List<CmfDesignerRawNode>();
        if (childrenByParent.TryGetValue(hwnd, out var childHandles))
        {
            foreach (var child in childHandles)
            {
                children.Add(BuildRawSubtree(child, processId, childrenByParent, visited));
            }
        }

        return BuildRawNode(hwnd, processId, children);
    }

    private static IEnumerable<IntPtr> EnumerateDirectChildWindows(IntPtr parent)
    {
        var child = GetWindow(parent, GwChild);
        var guard = 0;
        while (child != IntPtr.Zero && guard++ < 5000)
        {
            yield return child;
            child = GetWindow(child, GwHwndNext);
        }
    }

    private static CmfDesignerRawNode BuildRawNode(IntPtr hwnd, int processId, IReadOnlyList<CmfDesignerRawNode> children)
    {
        var className = GetWindowClassName(hwnd);
        var style = GetWindowStyle(hwnd);
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["StyleHex"] = "0x" + ((long)style).ToString("X", CultureInfo.InvariantCulture),
            ["ControlType"] = ClassifyControlType(className, style)
        };

        var items = ReadListItems(hwnd, className);
        if (items.Count > 0)
        {
            properties["ItemCount"] = items.Count.ToString(CultureInfo.InvariantCulture);
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        return new CmfDesignerRawNode
        {
            Handle = "0x" + hwnd.ToInt64().ToString("X", CultureInfo.InvariantCulture),
            ProcessId = pid == 0 ? processId : (int)pid,
            ClassName = className,
            Text = GetWindowCaption(hwnd),
            Bounds = GetWindowBounds(hwnd),
            Properties = properties,
            Items = items,
            Children = children
        };
    }

    private static IReadOnlyList<string> ReadListItems(IntPtr hwnd, string className)
    {
        if (IsDropDownClass(className))
        {
            return ReadItems(hwnd, CbGetCount, CbGetLbTextLen, CbGetLbText);
        }

        if (className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
        {
            return ReadItems(hwnd, LbGetCount, LbGetTextLen, LbGetText);
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> ReadItems(IntPtr hwnd, int countMessage, int textLengthMessage, int textMessage)
    {
        var count = SendMessage(hwnd, countMessage, IntPtr.Zero, IntPtr.Zero).ToInt64();
        if (count <= 0 || count > 5000) return Array.Empty<string>();

        var result = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var length = SendMessage(hwnd, textLengthMessage, new IntPtr(i), IntPtr.Zero).ToInt64();
            if (length < 0 || length > 8192) continue;
            var builder = new StringBuilder((int)length + 2);
            var read = SendMessageString(hwnd, textMessage, new IntPtr(i), builder).ToInt64();
            if (read >= 0)
            {
                result.Add(builder.ToString());
            }
        }

        return result;
    }

    private static IntPtr WaitForMainWindow(Process process, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(timeoutMs, 1000));
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;

            var handle = FindMainWindow(process.Id);
            if (handle != IntPtr.Zero) return handle;
            Thread.Sleep(200);
        }

        return FindMainWindow(process.Id);
    }

    private static IntPtr TryPrepareDesignerWindow(int processId, IntPtr mainWindow, string workingCmf, int timeoutMs, List<string> warnings)
    {
        var designerWindow = WaitForDesignerWindow(processId, Math.Min(Math.Max(timeoutMs / 2, 1000), 10000));
        if (designerWindow == IntPtr.Zero)
        {
            if (TryClickDesignerButton(mainWindow))
            {
                Thread.Sleep(1000);
                designerWindow = WaitForDesignerWindow(processId, Math.Min(Math.Max(timeoutMs / 2, 1000), 10000));
            }
            else
            {
                warnings.Add("CheatMaker main window was found, but the 运行设计器 button was not exposed through Win32 child windows.");
            }
        }

        if (designerWindow == IntPtr.Zero)
        {
            warnings.Add("CheatMaker designer window was not found; capturing the main window raw UI tree instead.");
            return mainWindow;
        }

        if (!IsWindowVisible(designerWindow))
        {
            ShowWindow(designerWindow, SwShow);
            Thread.Sleep(500);
        }

        SetForegroundWindow(designerWindow);
        Thread.Sleep(500);
        if (!TryOpenDesignerProject(processId, designerWindow, workingCmf, timeoutMs, warnings))
        {
            warnings.Add("CheatMaker designer window was captured, but automatic CMF open did not complete. The raw UI tree may represent an empty designer surface.");
        }

        return designerWindow;
    }

    private static bool TryOpenDesignerProject(int processId, IntPtr designerWindow, string workingCmf, int timeoutMs, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(workingCmf) || !File.Exists(workingCmf))
        {
            warnings.Add("Temporary CMF copy was not available for designer open.");
            return false;
        }

        PostMessage(designerWindow, WmCommand, new IntPtr(DesignerOpenCommand), IntPtr.Zero);
        var dialog = WaitForOpenDialog(processId, Math.Min(Math.Max(timeoutMs / 2, 1000), 10000));
        if (dialog == IntPtr.Zero)
        {
            warnings.Add("CheatMaker designer did not expose an Open Modifier dialog after command 57601.");
            return false;
        }

        var fileNameEdit = FindFileNameEditControl(dialog);
        if (fileNameEdit == IntPtr.Zero)
        {
            warnings.Add("CheatMaker Open dialog did not expose a filename Edit control.");
            return false;
        }

        SetWindowText(fileNameEdit, workingCmf);
        var fileNameCombo = GetParent(fileNameEdit);
        if (fileNameCombo != IntPtr.Zero && IsDropDownClass(GetWindowClassName(fileNameCombo)))
        {
            SetWindowText(fileNameCombo, workingCmf);
            var comboId = GetDlgCtrlID(fileNameCombo);
            if (comboId > 0)
            {
                SendMessage(dialog, WmCommand, MakeWParam(comboId, CbnEditChange), fileNameCombo);
            }
        }

        Thread.Sleep(200);
        var openButton = FindOpenDialogConfirmButton(dialog);
        if (openButton != IntPtr.Zero)
        {
            SendMessage(openButton, BmClick, IntPtr.Zero, IntPtr.Zero);
            SendMessage(dialog, WmCommand, new IntPtr(IdOk), openButton);
        }
        else
        {
            PostMessage(dialog, WmCommand, new IntPtr(IdOk), IntPtr.Zero);
        }

        if (!WaitForDialogToClose(dialog, Math.Min(Math.Max(timeoutMs / 2, 1000), 10000)))
        {
            warnings.Add("CheatMaker Open dialog did not close after selecting the temporary CMF copy.");
            return false;
        }

        Thread.Sleep(2000);
        return true;
    }

    private static IntPtr FindFileNameEditControl(IntPtr dialog)
    {
        return EnumerateDescendantWindows(dialog)
            .Where(hwnd => GetWindowClassName(hwnd).Equals("Edit", StringComparison.OrdinalIgnoreCase))
            .Select(hwnd => new { Handle = hwnd, Bounds = GetWindowBounds(hwnd), ParentClass = GetWindowClassName(GetParent(hwnd)) })
            .Where(item => !item.Bounds.IsEmpty && item.Bounds.Width >= 80)
            .OrderByDescending(item => IsDropDownClass(item.ParentClass))
            .ThenByDescending(item => item.Bounds.Top)
            .ThenByDescending(item => item.Bounds.Width)
            .Select(item => item.Handle)
            .FirstOrDefault();
    }

    private static IntPtr FindOpenDialogConfirmButton(IntPtr dialog)
    {
        return EnumerateDescendantWindows(dialog)
            .Where(hwnd => GetWindowClassName(hwnd).Equals("Button", StringComparison.OrdinalIgnoreCase))
            .Select(hwnd => new { Handle = hwnd, Text = GetWindowCaption(hwnd), Bounds = GetWindowBounds(hwnd) })
            .Where(item => !item.Bounds.IsEmpty &&
                           (item.Text.Contains("打开", StringComparison.OrdinalIgnoreCase) ||
                            item.Text.Contains("Open", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.Bounds.Top)
            .ThenBy(item => item.Bounds.Left)
            .Select(item => item.Handle)
            .FirstOrDefault();
    }

    private static IEnumerable<IntPtr> EnumerateDescendantWindows(IntPtr parent)
    {
        var result = new List<IntPtr>();
        EnumChildWindows(parent, (child, _) =>
        {
            result.Add(child);
            return result.Count < 2000;
        }, IntPtr.Zero);
        return result;
    }

    private static IntPtr WaitForOpenDialog(int processId, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(timeoutMs, 1000));
        while (DateTime.UtcNow < deadline)
        {
            var handle = FindOpenDialog(processId);
            if (handle != IntPtr.Zero) return handle;
            Thread.Sleep(200);
        }

        return FindOpenDialog(processId);
    }

    private static IntPtr FindOpenDialog(int processId)
    {
        var candidates = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            var title = GetWindowCaption(hwnd);
            var className = GetWindowClassName(hwnd);
            if (pid == processId &&
                IsWindowVisible(hwnd) &&
                className.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
                (title.Contains("打开", StringComparison.OrdinalIgnoreCase) ||
                 title.Contains("Open", StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        return candidates
            .OrderByDescending(hwnd => GetWindowBounds(hwnd).Width * GetWindowBounds(hwnd).Height)
            .FirstOrDefault();
    }

    private static bool WaitForDialogToClose(IntPtr dialog, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(timeoutMs, 1000));
        while (DateTime.UtcNow < deadline && IsWindowVisible(dialog))
        {
            Thread.Sleep(200);
        }

        return !IsWindowVisible(dialog);
    }

    private static IntPtr FindDescendantWindow(IntPtr parent, Func<string, string, bool> predicate)
    {
        var result = IntPtr.Zero;
        EnumChildWindows(parent, (child, _) =>
        {
            if (predicate(GetWindowClassName(child), GetWindowCaption(child)))
            {
                result = child;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static IntPtr WaitForDesignerWindow(int processId, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(timeoutMs, 1000));
        while (DateTime.UtcNow < deadline)
        {
            var handle = FindDesignerWindow(processId);
            if (handle != IntPtr.Zero) return handle;
            Thread.Sleep(200);
        }

        return FindDesignerWindow(processId);
    }

    private static IntPtr FindDesignerWindow(int processId)
    {
        var candidates = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == processId && IsDesignerWindowTitle(GetWindowCaption(hwnd)))
            {
                candidates.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        return candidates
            .OrderByDescending(IsWindowVisible)
            .ThenByDescending(hwnd => GetWindowBounds(hwnd).Width * GetWindowBounds(hwnd).Height)
            .FirstOrDefault();
    }

    private static bool TryClickDesignerButton(IntPtr mainWindow)
    {
        var buttons = new List<IntPtr>();
        EnumChildWindows(mainWindow, (child, _) =>
        {
            var text = GetWindowCaption(child);
            if (text.Contains("运行设计器", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("设计器", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Designer", StringComparison.OrdinalIgnoreCase))
            {
                buttons.Add(child);
            }

            return true;
        }, IntPtr.Zero);

        var button = buttons.FirstOrDefault();
        if (button == IntPtr.Zero) return false;
        SendMessage(button, BmClick, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    private static bool IsDesignerWindowTitle(string title)
        => title.Contains("CheatMaker 设计器", StringComparison.OrdinalIgnoreCase) ||
           title.Contains("设计器", StringComparison.OrdinalIgnoreCase) ||
           title.Contains("Designer", StringComparison.OrdinalIgnoreCase);

    private static IntPtr FindMainWindow(int processId)
    {
        var candidates = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == processId && IsWindowVisible(hwnd))
            {
                candidates.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        return candidates
            .OrderByDescending(hwnd => GetWindowCaption(hwnd).Length)
            .ThenByDescending(hwnd => GetWindowBounds(hwnd).Width * GetWindowBounds(hwnd).Height)
            .FirstOrDefault();
    }

    private static IEnumerable<CmfDesignerRawNode> FlattenRawNodes(CmfDesignerRawNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var nested in FlattenRawNodes(child))
            {
                if (!ReferenceEquals(nested, child))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string ResolveCheatMakerExe(string oldToolsRoot, string explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var direct = Path.Combine(oldToolsRoot, "CheatMaker", "CheatMaker.exe");
        if (File.Exists(direct)) return direct;

        try
        {
            return Directory.EnumerateFiles(oldToolsRoot, "CheatMaker.exe", SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .FirstOrDefault() ?? direct;
        }
        catch
        {
            return direct;
        }
    }

    private static string ResolveCmfPath(string oldToolsRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("CMF relative path is required.");
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(oldToolsRoot, normalized));
        var rootWithSlash = Path.GetFullPath(oldToolsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CMF path escapes the old tools root.");
        }

        return fullPath;
    }

    private static string CreateReportDirectory(CczProject project, string cmfPath)
        => Path.Combine(
            GetReportRoot(project),
            BuildSafeReportName(cmfPath),
            DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));

    private static string GetReportRoot(CczProject project)
        => Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports", "CmfDesignerExtraction");

    private static string BuildSafeReportName(string cmfPath)
    {
        var name = Path.GetFileNameWithoutExtension(cmfPath);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "cmf" : name;
    }

    private static string NormalizeMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "StaticOnly";
        return mode.Trim() switch
        {
            "static" or "Static" or "StaticOnly" => "StaticOnly",
            "launch" or "Launch" or "LaunchOnly" => "LaunchOnly",
            "ui" or "Ui" or "probe" or "UiProbe" => "UiProbe",
            var value => value
        };
    }

    private static string ClassifyControlType(string className, IReadOnlyDictionary<string, string> properties)
    {
        if (properties.TryGetValue("ControlType", out var controlType) && !string.IsNullOrWhiteSpace(controlType))
        {
            return controlType;
        }

        return ClassifyControlType(className, 0);
    }

    private static string ClassifyControlType(string className, nint style)
    {
        if (IsDropDownClass(className)) return DropDownClassName;
        if (className.Contains("ListBox", StringComparison.OrdinalIgnoreCase)) return "ListBox";
        if (className.Contains("Edit", StringComparison.OrdinalIgnoreCase)) return "TextBox";
        if (className.Contains("Static", StringComparison.OrdinalIgnoreCase)) return "Label";
        if (className.Contains("Button", StringComparison.OrdinalIgnoreCase))
        {
            return (((long)style) & 0xF) == BsGroupBox ? "GroupBox" : "Button";
        }

        return string.IsNullOrWhiteSpace(className) ? "Window" : className;
    }

    private static string NormalizeDesignerDataType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unknown";
        var trimmed = value.Trim();
        if (trimmed.Contains("十六进制", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("hex", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("hexadecimal", StringComparison.OrdinalIgnoreCase))
        {
            return "Hex";
        }

        if (trimmed.Contains("十进制", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("decimal", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("int", StringComparison.OrdinalIgnoreCase))
        {
            return "Decimal";
        }

        if (trimmed.Contains("文本", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("文字", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("text", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("string", StringComparison.OrdinalIgnoreCase))
        {
            return "GbkText";
        }

        return trimmed;
    }

    private static string ParseDefaultValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var token = value.Trim().Split(['-', ' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token != null && TryParseHex(token, out var parsed)
            ? "0x" + parsed.ToString("X", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static bool TryParseHex(string? value, out long parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var cleaned = value.Trim()
            .TrimStart('$')
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("&H", string.Empty, StringComparison.OrdinalIgnoreCase);
        cleaned = new string(cleaned.TakeWhile(Uri.IsHexDigit).ToArray());
        return cleaned.Length > 0 && long.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool TryParseInt(string? value, out int parsed)
        => int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

    private static string InferTargetFileFromCmf(string cmfPath)
    {
        var name = Path.GetFileName(cmfPath).ToLowerInvariant();
        return name.Contains("exe", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("引擎", StringComparison.OrdinalIgnoreCase)
            ? "Ekd5.exe"
            : string.Empty;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string GetFileVersion(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return string.Empty;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? string.Empty : info.FileVersion;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildStableId(string seed, string kind, string value)
    {
        var input = seed + "|" + kind + "|" + value;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).Substring(0, 12).ToLowerInvariant();
        return kind + "-" + hash;
    }

    private static string TrimPropertyName(string text)
        => text.Trim().TrimEnd(':', '：').Trim();

    private static int MidY(CmfUiRect rect)
        => rect.Top + rect.Height / 2;

    private static IntPtr MakeWParam(int lowWord, int highWord)
        => new((highWord << 16) | (lowWord & 0xFFFF));

    private static string Csv(string value)
        => "\"" + (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string MarkdownText(string value)
        => string.IsNullOrWhiteSpace(value) ? "(unnamed)" : value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string MarkdownCell(string? value)
        => MarkdownText(value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal);

    private static string CollapseMultiline(string value, int maxLength)
    {
        var compact = (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
    }

    private static string GetWindowCaption(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        var builder = new StringBuilder(Math.Max(length + 1, 256));
        GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        GetClassName(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static CmfUiRect GetWindowBounds(IntPtr hwnd)
    {
        return GetWindowRect(hwnd, out var rect)
            ? new CmfUiRect(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top))
            : CmfUiRect.Empty;
    }

    private static nint GetWindowStyle(IntPtr hwnd)
    {
        if (IntPtr.Size == 8)
        {
            return GetWindowLongPtr64(hwnd, GwlStyle);
        }

        return GetWindowLong32(hwnd, GwlStyle);
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageString(IntPtr hWnd, int msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
