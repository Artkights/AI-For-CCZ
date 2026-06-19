using System.Globalization;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.McpServer;

public sealed partial class CczMcpRuntime
{
    private readonly ModPackageService _modPackageService = new();

    public object AnalyzeModRequest(string? gameRoot, string prompt, string? name, string? packageId)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Analysis = _modPackageService.AnalyzeRequest(project, prompt, name, packageId),
            AutomationNote = "analyze_mod_request fills missing design inputs automatically; compile_mod_package can consume the returned ModDesign directly."
        };
    }

    public object CompileModPackage(string? gameRoot, ModDesign design, int slotLimit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var result = _modPackageService.CompilePackage(project, tables, design, NormalizeLimit(slotLimit, 32, 200));
        return new
        {
            project.GameRoot,
            Compile = result,
            AutomationNote = "compile_mod_package automatically selects slots and emits a ModPackage draft; use auto_make_mod for preview/apply/validate orchestration."
        };
    }

    public object AnalyzeStandaloneScenarioRequest(string? gameRoot, string prompt, string? title, string? packageId)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Analysis = _modPackageService.AnalyzeStandaloneScenarioRequest(project, prompt, title, packageId),
            AutomationNote = "analyze_standalone_scenario_request expands one prompt into a full standalone R+S single-stage design with force_open constraints."
        };
    }

    public object CompileStandaloneScenarioPackage(string? gameRoot, StandaloneScenarioDesign design, int slotLimit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var result = _modPackageService.CompileStandaloneScenarioPackage(project, tables, design, NormalizeLimit(slotLimit, 64, 200));
        return new
        {
            project.GameRoot,
            Compile = result,
            AutomationNote = "compile_standalone_scenario_package emits a force_open ModPackage with complete R/S event graph, deployment, validation, and resource plan."
        };
    }

    public object ListAvailableSlots(string? gameRoot, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        return new
        {
            project.GameRoot,
            Slots = _modPackageService.ListAvailableSlots(project, tables, NormalizeLimit(limit, 32, 200))
        };
    }

    public object PreviewModPackage(string? gameRoot, ModPackage package, string? automationMode)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var dictionary = TryLoadScenarioDictionaryForModPackage(project, package);
        var normalizedAutomation = NormalizeAutomationMode(automationMode);
        var allowStructuralScenarioWrites = normalizedAutomation is "aggressive_test_copy" or "force_preview_report" or "force_open" or "strict_playable_preview";
        var strictPlayablePreview = normalizedAutomation == "strict_playable_preview";
        return new
        {
            project.GameRoot,
            Preview = _modPackageService.Preview(project, tables, package, dictionary, allowStructuralScenarioWrites, strictPlayablePreview),
            AutomationMode = normalizedAutomation,
            SafetyNote = strictPlayablePreview
                ? "preview_mod_package is read-only. strict_playable_preview reports blocking evidence that prevents playable/runtime tier claims."
                : allowStructuralScenarioWrites
                ? "preview_mod_package is read-only. force/aggressive automation allows structural scenario append/insert in preview."
                : "preview_mod_package is read-only. apply_mod_package refuses to write unless this preview has no blocking issues."
        };
    }

    public object ApplyModPackage(string? gameRoot, ModPackage package, string? writeMode, string? automationMode)
    {
        var project = LoadProject(gameRoot);
        var normalizedAutomation = NormalizeAutomationMode(automationMode);
        if (normalizedAutomation == "force_preview_report")
        {
            var forceTables = LoadTables(project);
            var forceDictionary = TryLoadScenarioDictionaryForModPackage(project, package);
            var forcePreview = _modPackageService.Preview(project, forceTables, package, forceDictionary, allowStructuralScenarioWrites: true);
            var forceReport = _modPackageService.ExportReport(project, package, "force-preview", forcePreview);
            return new
            {
                project.GameRoot,
                Applied = false,
                Preview = forcePreview,
                Report = forceReport,
                AutomationMode = normalizedAutomation
            };
        }

        if (normalizedAutomation == "aggressive_test_copy" && !project.IsTestCopy)
        {
            var testCopyRoot = _backupManager.CreateTestCopy(project);
            project = LoadProject(testCopyRoot);
            writeMode = "test_copy";
        }

        EnsureWriteMode(project, writeMode);
        var allowStructuralScenarioWrites = normalizedAutomation is "aggressive_test_copy" or "force_open";
        return ApplyModPackageCore(project, package, writeMode, allowStructuralScenarioWrites, normalizedAutomation);
    }

    private object ApplyModPackageCore(
        CczProject project,
        ModPackage package,
        string? writeMode,
        bool allowStructuralScenarioWrites,
        string automationMode)
    {
        var tables = LoadTables(project);
        var dictionary = TryLoadScenarioDictionaryForModPackage(project, package);
        var preview = _modPackageService.Preview(project, tables, package, dictionary, allowStructuralScenarioWrites);
        if (!preview.CanApply)
        {
            return new
            {
                project.GameRoot,
                Applied = false,
                Preview = preview,
                AutomationMode = automationMode,
                SafetyNote = "ModPackage was not applied because preview reported blocking issues."
            };
        }

        var result = new ModPackageApplyResult
        {
            ProjectRoot = project.GameRoot,
            PackageId = ModPackageService.NormalizePackageId(package),
            Applied = true
        };

        try
        {
            var tableSaves = _modPackageService.ApplyTableUpdates(project, tables, package.TableUpdates);
            foreach (var save in tableSaves)
            {
                result.BackupPaths.Add(save.BackupPath);
                if (!string.IsNullOrWhiteSpace(save.ReportJsonPath)) result.ReportPaths.Add(save.ReportJsonPath);
                result.ApplyResults.Add(new
                {
                    Kind = "table",
                    save.Table.TableName,
                    save.FilePath,
                    save.BackupPath,
                    save.ReportJsonPath,
                    save.ChangedBytes
                });
            }

            if (dictionary != null)
            {
                foreach (var patch in package.ScenarioPatches)
                {
                    var scenarioApply = allowStructuralScenarioWrites
                        ? _modPackageService.ApplyScenarioPatchAggressive(project, patch, dictionary, IsForceOpenAutomation(automationMode, package))
                        : _modPackageService.ApplyScenarioPatch(project, patch, dictionary);
                    result.ApplyResults.Add(scenarioApply);
                    if (!scenarioApply.Applied)
                    {
                        result.Issues.AddRange(scenarioApply.Issues);
                        result.Applied = false;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(scenarioApply.BackupPath)) result.BackupPaths.Add(scenarioApply.BackupPath);
                    if (!string.IsNullOrWhiteSpace(scenarioApply.ReportJsonPath)) result.ReportPaths.Add(scenarioApply.ReportJsonPath);
                }
            }

            foreach (var effectPackage in package.EffectPackages)
            {
                var effectApply = _effectPackageService.Apply(project, tables, effectPackage, "import");
                result.ApplyResults.Add(effectApply);
                result.BackupPaths.AddRange(effectApply.BackupPaths);
                result.ReportPaths.AddRange(effectApply.ReportPaths);
                if (!string.IsNullOrWhiteSpace(effectApply.ManifestPath)) result.ReportPaths.Add(effectApply.ManifestPath);
            }

            foreach (var resourceUpdate in package.ResourceUpdates)
            {
                var resourceApply = ApplyModResourceUpdate(project, resourceUpdate);
                result.ApplyResults.Add(resourceApply.Payload);
                if (!string.IsNullOrWhiteSpace(resourceApply.BackupPath)) result.BackupPaths.Add(resourceApply.BackupPath);
                if (!string.IsNullOrWhiteSpace(resourceApply.ReportPath)) result.ReportPaths.Add(resourceApply.ReportPath);
                if (resourceUpdate.Operation.Equals("generate_task", StringComparison.OrdinalIgnoreCase))
                {
                    result.Applied = false;
                    result.Issues.Add(new ModPackageValidationIssue
                    {
                        Severity = "warning",
                        Category = "resource",
                        Target = resourceUpdate.TargetRelativePath,
                        Message = "Resource generation task was recorded, but no game resource was imported."
                    });
                }
            }
        }
        catch (Exception ex)
        {
            result.Applied = false;
            result.Issues.Add(new ModPackageValidationIssue
            {
                Severity = "error",
                Category = "apply",
                Target = result.PackageId,
                Message = ex.Message
            });
        }

        result.BackupPaths = result.BackupPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.ReportPaths = result.ReportPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.Summary = $"ModPackage apply: applied={result.Applied}, operations={result.ApplyResults.Count}, backups={result.BackupPaths.Count}, reports={result.ReportPaths.Count}, issues={result.Issues.Count}.";
        var report = _modPackageService.ExportReport(project, package, "apply", result);

        return new
        {
            project.GameRoot,
            Preview = preview,
            Result = result,
            Report = report,
            AutomationMode = automationMode,
            SafetyNote = allowStructuralScenarioWrites
                ? "Aggressive ModPackage apply runs preview first, then writes the test copy with structural scenario append/insert enabled for whitelisted commands."
                : "ModPackage apply runs preview first, then uses dedicated table/scenario/effect/resource writers with backups and reports."
        };
    }

    public object AutoMakeMod(string? gameRoot, string prompt, string? automationMode, int maxRepairAttempts)
    {
        var project = LoadProject(gameRoot);
        var normalizedAutomation = NormalizeAutomationMode(automationMode);
        if (normalizedAutomation is not "aggressive_test_copy" and not "force_open")
        {
            normalizedAutomation = "aggressive_test_copy";
        }

        var sourceTables = LoadTables(project);
        var standalone = prompt.Contains("独立单关", StringComparison.OrdinalIgnoreCase) ||
                         prompt.Contains("standalone", StringComparison.OrdinalIgnoreCase) ||
                         normalizedAutomation == "force_open";
        var analysis = standalone ? null : _modPackageService.AnalyzeRequest(project, prompt, null, null);
        var standaloneAnalysis = standalone ? _modPackageService.AnalyzeStandaloneScenarioRequest(project, prompt, null, null) : null;
        var compile = standalone
            ? null
            : _modPackageService.CompilePackage(project, sourceTables, analysis!.Design, 64);
        var standaloneCompile = standalone
            ? _modPackageService.CompileStandaloneScenarioPackage(project, sourceTables, standaloneAnalysis!.Design, 64)
            : null;
        var package = standalone ? standaloneCompile!.Package : compile!.Package;
        var testCopyRoot = _backupManager.CreateTestCopy(project);
        var testProject = LoadProject(testCopyRoot);
        var testTables = LoadTables(testProject);
        var dictionary = TryLoadScenarioDictionaryForModPackage(testProject, package);
        var attempts = new List<ModAutomationAttempt>();
        ModPackagePreviewResult? preview = null;
        var repairLimit = Math.Clamp(maxRepairAttempts <= 0 ? 3 : maxRepairAttempts, 1, 10);

        for (var attempt = 1; attempt <= repairLimit; attempt++)
        {
            preview = _modPackageService.Preview(testProject, testTables, package, dictionary, allowStructuralScenarioWrites: true);
            attempts.Add(new ModAutomationAttempt
            {
                Attempt = attempt,
                Stage = "preview",
                Passed = preview.CanApply,
                Summary = preview.Summary,
                Issues = preview.Issues.ToList()
            });
            if (preview.CanApply) break;

            var repair = _modPackageService.RepairPackage(testProject, testTables, package, preview, standalone ? standaloneCompile!.Slots : compile!.Slots, dictionary);
            attempts.Add(new ModAutomationAttempt
            {
                Attempt = attempt,
                Stage = "repair",
                Passed = repair.Changed,
                Summary = repair.Changed ? string.Join("; ", repair.Repairs.Take(8)) : "No automatic repair was available.",
                Issues = repair.RemainingIssues
            });
            package = repair.Package;
            if (!repair.Changed) break;
        }

        var result = new ModAutoMakeResult
        {
            ProjectRoot = project.GameRoot,
            TestCopyRoot = testCopyRoot,
            AutomationMode = normalizedAutomation,
            Design = standalone ? new ModDesign
            {
                DesignId = standaloneAnalysis!.Design.DesignId,
                SourcePrompt = standaloneAnalysis.Design.SourcePrompt,
                Name = standaloneAnalysis.Design.Title,
                Theme = standaloneAnalysis.Design.Theme,
                TargetVersion = standaloneAnalysis.Design.TargetVersion,
                StorySynopsis = standaloneAnalysis.Design.Synopsis,
                ResourceStyle = standaloneAnalysis.Design.Resources.Style,
                GameplayGoals = { "standalone_single_stage", "force_open", standaloneAnalysis.Design.Battle.Objective },
                RequestedResources = standaloneAnalysis.Design.Resources.Needs.Select(need => need.Kind + ":" + need.Target).ToList(),
                Assumptions =
                {
                    ["standalone_design_id"] = standaloneAnalysis.Design.DesignId,
                    ["constraint_mode"] = "force_open"
                }
            } : analysis!.Design,
            Package = package,
            Preview = preview,
            Attempts = attempts
        };

        if (preview?.CanApply == true)
        {
            var applyObject = ApplyModPackageCore(testProject, package, "test_copy", allowStructuralScenarioWrites: true, standalone ? "force_open" : normalizedAutomation);
            var applyPayload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(applyObject));
            if (applyPayload.TryGetProperty("Result", out var applyElement))
            {
                result.Apply = applyElement.Deserialize<ModPackageApplyResult>();
            }

            var autoValidation = _modPackageService.AutoValidate(testProject, testTables, package, dictionary, runSmokes: false);
            if (autoValidation.Preview != null)
            {
                result.Validation = new ModPackageValidationResult
                {
                    ProjectRoot = testProject.GameRoot,
                    PackageId = ModPackageService.NormalizePackageId(package),
                    Passed = autoValidation.Passed,
                    Preview = autoValidation.Preview,
                    Issues = autoValidation.Issues.ToList(),
                    PlannedSmokeCommands = autoValidation.SmokeRuns.Select(run => run.Command).ToList(),
                    ManualChecks = autoValidation.Preview.ManualChecks.ToList(),
                    Summary = autoValidation.Summary + " Package is static-only until auto_validate_mod run_smokes=true passes every required smoke."
                };
                result.ReportPaths.Add(autoValidation.ReportPath);
            }

            result.Completed = result.Apply?.Applied == true && result.Validation?.Passed == true;
        }

        result.Summary = $"auto_make_mod: completed={result.Completed}, mode={(standalone ? "standalone_single_stage" : "legacy_mod_package")}, testCopy={testCopyRoot}, attempts={attempts.Count}, previewCanApply={preview?.CanApply}.";
        var report = _modPackageService.ExportReport(testProject, package, "auto-make", result);
        result.ReportPaths.Add(report.JsonPath);
        result.ReportPaths.Add(report.MarkdownPath);

        return new
        {
            project.GameRoot,
            Result = result,
            Report = report,
            AutomationNote = "auto_make_mod creates and writes a test copy automatically. Formal base promotion is handled only by promote_test_copy_mod with confirm_promote=true."
        };
    }

    public object AutoValidateMod(string? gameRoot, ModPackage package, bool runSmokes)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var dictionary = TryLoadScenarioDictionaryForModPackage(project, package);
        var validation = _modPackageService.AutoValidate(project, tables, package, dictionary, runSmokes);
        return new
        {
            project.GameRoot,
            Validation = validation
        };
    }

    public object PromoteTestCopyMod(string? gameRoot, ModPackage package, bool confirmPromote)
    {
        var project = LoadProject(gameRoot);
        if (!confirmPromote)
        {
            return new
            {
                project.GameRoot,
                Promoted = false,
                Message = "confirm_promote=true is required before applying a ModPackage to the formal base."
            };
        }

        var apply = ApplyModPackageCore(project, package, "direct", allowStructuralScenarioWrites: true, "promote_confirmed");
        return new
        {
            project.GameRoot,
            Promoted = true,
            Apply = apply
        };
    }

    public object ValidateModPackage(string? gameRoot, ModPackage package, bool runSmokes)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var dictionary = TryLoadScenarioDictionaryForModPackage(project, package);
        if (runSmokes)
        {
            var autoValidation = _modPackageService.AutoValidate(project, tables, package, dictionary, runSmokes: true);
            return new
            {
                project.GameRoot,
                Validation = new ModPackageValidationResult
                {
                    ProjectRoot = project.GameRoot,
                    PackageId = ModPackageService.NormalizePackageId(package),
                    Preview = autoValidation.Preview,
                    Passed = autoValidation.Passed,
                    Issues = autoValidation.Issues.ToList(),
                    PlannedSmokeCommands = autoValidation.SmokeRuns.Select(run => run.Command).ToList(),
                    ManualChecks = autoValidation.Preview?.ManualChecks.ToList() ?? [],
                    Summary = autoValidation.Summary
                },
                AutoValidation = autoValidation
            };
        }

        var preview = _modPackageService.Preview(project, tables, package, dictionary, allowStructuralScenarioWrites: ModPackageService.IsForceOpenPackage(package));
        var validation = new ModPackageValidationResult
        {
            ProjectRoot = project.GameRoot,
            PackageId = ModPackageService.NormalizePackageId(package),
            Preview = preview,
            Passed = false,
            Issues = preview.Issues.ToList(),
            PlannedSmokeCommands = preview.RequiredSmokeCommands.ToList(),
            ManualChecks = preview.ManualChecks.ToList()
        };
        validation.Issues.Add(new ModPackageValidationIssue
        {
            Severity = "warning",
            Category = "validation",
            Target = "run_smokes",
            Message = "Required smoke commands were not run; validation status is static-only."
        });

        validation.Summary = $"ModPackage validation: staticPassed={preview.CanApply}, passed=False, smokeCommands={validation.PlannedSmokeCommands.Count}, manualChecks={validation.ManualChecks.Count}, issues={validation.Issues.Count}.";
        return new
        {
            project.GameRoot,
            Validation = validation
        };
    }

    public object ExportModReport(string? gameRoot, ModPackage package, string? reportKind)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var dictionary = TryLoadScenarioDictionaryForModPackage(project, package);
        var preview = _modPackageService.Preview(project, tables, package, dictionary, allowStructuralScenarioWrites: ModPackageService.IsForceOpenPackage(package));
        var report = _modPackageService.ExportReport(project, package, reportKind ?? "preview", preview);
        return new
        {
            project.GameRoot,
            Report = report,
            Preview = preview,
            SafetyNote = "Report export writes only CCZModStudio_Reports/ModPackages under the workspace."
        };
    }

    public object PreviewScenarioPatch(string? gameRoot, ModScenarioPatch patch)
    {
        var project = LoadProject(gameRoot);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        return new
        {
            project.GameRoot,
            Preview = _modPackageService.PreviewScenarioPatch(project, patch, dictionary),
            SafeCommandIds = ModPackageService.GetSafeScenarioCommandIds().Select(id => CCZModStudio.Core.HexDisplayFormatter.Format(id, 2)),
            SafetyNote = "V1 applies only parameter replacement on existing whitelisted commands. Insert/append operations are preview-only."
        };
    }

    public object ApplyScenarioPatch(string? gameRoot, ModScenarioPatch patch, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var result = _modPackageService.ApplyScenarioPatch(project, patch, dictionary);
        return new
        {
            project.GameRoot,
            Result = result,
            SafetyNote = "Scenario patch writes use LegacyScenarioWriter backup, full structure rebuild, and round-trip validation."
        };
    }

    public object CompileScenarioPatch(string? gameRoot, ScenarioPatchCompileRequest request)
    {
        var project = LoadProject(gameRoot);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        return new
        {
            project.GameRoot,
            Result = _modPackageService.CompileScenarioPatch(project, request, dictionary),
            AutomationNote = "compile_scenario_patch emits a whitelisted template flow block. Use apply_scenario_patch_aggressive on a test copy to write append/insert operations."
        };
    }

    public object ApplyScenarioPatchAggressive(string? gameRoot, ModScenarioPatch patch, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var result = _modPackageService.ApplyScenarioPatchAggressive(project, patch, dictionary, forceOpenScenarioWrites: true);
        return new
        {
            project.GameRoot,
            Result = result,
            SafetyNote = "Aggressive scenario patch allows append/insert only for whitelisted commands and still writes through LegacyScenarioWriter round-trip validation."
        };
    }

    private SceneStringDocument? TryLoadScenarioDictionaryForModPackage(CczProject project, ModPackage package)
    {
        if (package.ScenarioPatches.Count == 0) return null;
        return LoadRequiredScenarioCommandDictionary(project);
    }

    private static string NormalizeAutomationMode(string? automationMode)
    {
        var normalized = string.IsNullOrWhiteSpace(automationMode) ? "safe" : automationMode.Trim().ToLowerInvariant();
        return normalized is "safe" or "aggressive_test_copy" or "force_preview_report" or "promote_confirmed" or "force_open" or "strict_playable_preview"
            ? normalized
            : "safe";
    }

    private static bool IsForceOpenAutomation(string automationMode, ModPackage package)
        => automationMode.Equals("force_open", StringComparison.OrdinalIgnoreCase) || ModPackageService.IsForceOpenPackage(package);

    private (object Payload, string BackupPath, string ReportPath) ApplyModResourceUpdate(CczProject project, ModResourceUpdate update)
    {
        if (update.Operation.Equals("generate_task", StringComparison.OrdinalIgnoreCase))
        {
            var reportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports", "ModPackages", "ResourceTasks");
            Directory.CreateDirectory(reportRoot);
            var stem = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + MakeSafeFileStem(FirstNonEmpty(update.Kind, "resource-task"));
            var reportPath = Path.Combine(reportRoot, stem + ".md");
            File.WriteAllLines(reportPath,
            [
                "# Resource Generation Task",
                string.Empty,
                "- Kind: " + update.Kind,
                "- Target: " + update.TargetRelativePath,
                "- Operation: " + update.Operation,
                "- Note: " + update.Note
            ]);
            return (new
            {
                Kind = "resource_generation_task",
                update.TargetRelativePath,
                update.Operation,
                update.Note,
                ReportPath = reportPath
            }, string.Empty, reportPath);
        }

        var targetPath = ResolveProjectFile(project, update.TargetRelativePath, mustExist: true);
        var extension = Path.GetExtension(targetPath);
        if (extension.Equals(".e5", StringComparison.OrdinalIgnoreCase))
        {
            if (!update.ImageNumber.HasValue) throw new InvalidOperationException($"E5 update {update.TargetRelativePath} requires image_number.");
            EnsureE5ImageTargetAllowed(project, targetPath);
            var sourcePath = ResolveExternalFile(project, update.ReplacementPath);
            var replace = _e5ImageReplace.Replace(project, targetPath, update.ImageNumber.Value, sourcePath);
            return (new
            {
                Kind = "e5_image",
                update.TargetRelativePath,
                update.ImageNumber,
                replace.BackupPath,
                replace.ReportPath,
                replace.ReportJsonPath,
                Preview = BuildE5ImageReplacePayload(replace)
            }, replace.BackupPath, FirstNonEmpty(replace.ReportJsonPath, replace.ReportPath));
        }

        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            if (!update.IconIndex.HasValue) throw new InvalidOperationException($"DLL icon update {update.TargetRelativePath} requires icon_index.");
            var dllPath = ResolveDllIconTarget(project, update.TargetRelativePath);
            if (update.Operation.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                var clear = _iconResourceReplace.ClearBitmapIcon(project, dllPath, update.IconIndex.Value);
                return (new
                {
                    Kind = "dll_icon_clear",
                    update.TargetRelativePath,
                    update.IconIndex,
                    clear.BackupPath,
                    clear.ReportPath,
                    clear.ReportJsonPath,
                    Preview = BuildDllIconReplacePayload(clear)
                }, clear.BackupPath, FirstNonEmpty(clear.ReportJsonPath, clear.ReportPath));
            }

            var sourcePath = ResolveExternalFile(project, update.ReplacementPath);
            var replace = _iconResourceReplace.ReplaceBitmapIcon(project, dllPath, update.IconIndex.Value, sourcePath);
            return (new
            {
                Kind = "dll_icon",
                update.TargetRelativePath,
                update.IconIndex,
                replace.BackupPath,
                replace.ReportPath,
                replace.ReportJsonPath,
                Preview = BuildDllIconReplacePayload(replace)
            }, replace.BackupPath, FirstNonEmpty(replace.ReportJsonPath, replace.ReportPath));
        }

        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            var sourcePath = ResolveExternalFile(project, update.ReplacementPath);
            var replace = _mapImageReplace.ReplaceMapImage(project, targetPath, sourcePath);
            return (new
            {
                Kind = "map_image",
                update.TargetRelativePath,
                replace.BackupPath,
                replace.ReportJsonPath,
                replace.OldSizeBytes,
                replace.NewSizeBytes,
                replace.OldWidth,
                replace.OldHeight,
                replace.NewWidth,
                replace.NewHeight,
                replace.ChangedBytesEstimate,
                replace.FormatCheckSummary,
                replace.Warning
            }, replace.BackupPath, replace.ReportJsonPath);
        }

        EnsureGenericResourceAllowed(project, targetPath);
        var genericSource = ResolveExternalFile(project, update.ReplacementPath);
        var generic = _resourceReplace.Replace(project, targetPath, genericSource, requireSameExtension: true);
        return (new
        {
            Kind = "resource",
            update.TargetRelativePath,
            generic.BackupPath,
            generic.ReportPath,
            generic.ReportJsonPath,
            generic.OldSizeBytes,
            generic.NewSizeBytes,
            generic.ChangedBytesEstimate,
            generic.FormatCheckSummary,
            generic.FormatWarnings,
            generic.RiskSummary
        }, generic.BackupPath, FirstNonEmpty(generic.ReportJsonPath, generic.ReportPath));
    }
}
