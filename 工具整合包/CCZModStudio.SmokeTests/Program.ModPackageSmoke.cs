using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

internal partial class Program
{
    static void RunModPackageSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var service = new ModPackageService();
        var slots = service.ListAvailableSlots(project, tables, 8);
        if (slots.Groups.Count < 4)
        {
            throw new InvalidOperationException($"ModPackage slot scan returned too few groups: {slots.Groups.Count}");
        }

        var analysis = service.AnalyzeRequest(project, "制作一个水战奇袭小关卡，包含四名新角色、一个 R 剧情和一场 S 战斗。", "自动烟测关", "auto-smoke");
        if (analysis.Design.Roles.Count < 4 || string.IsNullOrWhiteSpace(analysis.Design.Theme))
        {
            throw new InvalidOperationException("ModPackage automation analysis did not produce a usable ModDesign.");
        }

        var compiled = service.CompilePackage(project, tables, analysis.Design, 8);
        if (compiled.Slots.Groups.Count == 0 ||
            compiled.Package.ValidationPlan.SmokeCommands.Count == 0)
        {
            throw new InvalidOperationException("ModPackage automation compile did not select slots or validation smokes.");
        }

        var (table, rowId, columnName, currentValue, capacity) = FindModPackageSmokeStringField(project, tables);
        var replacement = currentValue == "MOD" ? "AI" : "MOD";
        if (EncodingService.GetGbkByteCount(replacement) > capacity)
        {
            replacement = string.Empty;
        }

        var package = new ModPackage
        {
            Metadata = new ModPackageMetadata
            {
                PackageId = "mod-package-smoke",
                Name = "ModPackage smoke",
                TargetVersion = "6.5",
                Theme = "preview-only"
            },
            TableUpdates =
            {
                new ModTableUpdate
                {
                    TableName = table.TableName,
                    RowId = rowId,
                    Values =
                    {
                        [columnName] = JsonSerializer.SerializeToElement(replacement)
                    },
                    Note = "Preview-only smoke update."
                }
            }
        };

        var preview = service.Preview(project, tables, package);
        if (!preview.CanApply ||
            preview.Changes.Count(change => change.Category == "table") != 1 ||
            preview.RequiredSmokeCommands.Count == 0)
        {
            throw new InvalidOperationException($"ModPackage valid preview failed: can={preview.CanApply}, issues={preview.Issues.Count}, changes={preview.Changes.Count}");
        }

        var longText = new string('测', capacity + 8);
        var invalidLengthPackage = new ModPackage
        {
            Metadata = new ModPackageMetadata { PackageId = "mod-package-smoke-long", Name = "Long text", TargetVersion = "6.5" },
            TableUpdates =
            {
                new ModTableUpdate
                {
                    TableName = table.TableName,
                    RowId = rowId,
                    Values =
                    {
                        [columnName] = JsonSerializer.SerializeToElement(longText)
                    }
                }
            }
        };
        var invalidLengthPreview = service.Preview(project, tables, invalidLengthPackage);
        if (invalidLengthPreview.CanApply ||
            invalidLengthPreview.Issues.All(issue => !issue.Message.Contains("GBK byte length", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("ModPackage preview did not reject an over-capacity GBK fixed string.");
        }

        var scenarioPath = FindFirstScenarioRelativePath(project);
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(dictionaryPath))
        {
            throw new FileNotFoundException("ModPackage smoke requires CczString.ini.", dictionaryPath);
        }

        var dictionary = new SceneStringParser().Parse(dictionaryPath);
        var compiledPreview = service.Preview(project, tables, compiled.Package, dictionary, allowStructuralScenarioWrites: true);
        if (!compiledPreview.CanApply)
        {
            var issues = string.Join(" | ", compiledPreview.Issues.Take(5).Select(issue => $"[{issue.Severity}] {issue.Category}:{issue.Message}"));
            throw new InvalidOperationException("Compiled ModPackage did not pass aggressive preview: " + issues);
        }

        if (compiledPreview.RequiredSmokeCommands.All(command => !command.Equals("--mod-package-smoke", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Compiled ModPackage preview did not require --mod-package-smoke.");
        }

        var structuralPreview = service.PreviewScenarioPatch(project, new ModScenarioPatch
        {
            PatchId = "blocked-append",
            RelativePath = scenarioPath,
            Operations =
            {
                new ModScenarioPatchOperation
                {
                    Operation = "append_command",
                    Commands =
                    {
                        new ModScenarioCommandDraft
                        {
                            CommandId = 0x14
                        }
                    }
                }
            }
        }, dictionary);
        if (structuralPreview.CanApply ||
            structuralPreview.Issues.All(issue => !issue.Severity.Equals("blocked", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("ModPackage scenario structural operation was not blocked in V1 preview.");
        }

        var aggressiveStructuralPreview = service.PreviewScenarioPatch(project, new ModScenarioPatch
        {
            PatchId = "aggressive-append",
            RelativePath = scenarioPath,
            Operations =
            {
                new ModScenarioPatchOperation
                {
                    Operation = "append_command",
                    SceneIndex = 1,
                    SectionIndex = 1,
                    Commands =
                    {
                        new ModScenarioCommandDraft
                        {
                            CommandId = 0x14,
                            Parameters =
                            {
                                new ModScenarioParameterDraft
                                {
                                    LayoutCode = 0x05,
                                    Kind = "text",
                                    Text = "自动化剧本补丁烟测"
                                }
                            }
                        }
                    }
                }
            }
        }, dictionary, allowStructuralScenarioWrites: true);
        if (!aggressiveStructuralPreview.CanApply ||
            aggressiveStructuralPreview.Changes.All(change => !change.Field.Contains("append_command", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("ModPackage aggressive structural scenario preview did not allow a whitelisted append command.");
        }

        var unsafeCommandPreview = service.PreviewScenarioPatch(project, new ModScenarioPatch
        {
            PatchId = "unsafe-command",
            RelativePath = scenarioPath,
            Operations =
            {
                new ModScenarioPatchOperation
                {
                    Operation = "append_command",
                    Commands =
                    {
                        new ModScenarioCommandDraft
                        {
                            CommandId = 0x7F
                        }
                    }
                }
            }
        }, dictionary);
        if (unsafeCommandPreview.Issues.All(issue => !issue.Message.Contains("not in the V1 safe whitelist", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("ModPackage scenario preview did not reject an unsafe command id.");
        }

        var report = service.ExportReport(project, package, "smoke-preview", preview);
        if (!File.Exists(report.JsonPath) || !File.Exists(report.MarkdownPath))
        {
            throw new InvalidOperationException("ModPackage report export did not create JSON/Markdown reports.");
        }

        Console.WriteLine($"MOD_PACKAGE_SMOKE_OK slots={slots.Groups.Count} table={table.TableName} row={rowId} column={columnName} previewChanges={preview.Changes.Count} report={Path.GetFileName(report.MarkdownPath)}");
    }

    private static (HexTableDefinition Table, int RowId, string ColumnName, string CurrentValue, int Capacity) FindModPackageSmokeStringField(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables)
    {
        var reader = new HexTableReader();
        var profile = new CczEngineProfileService().Detect(project);
        foreach (var table in tables
                     .Where(table => table.Fields.Any(field => field.Kind == HexFieldKind.FixedString && field.ConsumesBytes))
                     .OrderByDescending(table => table.Version.Equals(profile.TableVersionPrefix, StringComparison.OrdinalIgnoreCase))
                     .ThenBy(table => table.Id))
        {
            var read = reader.Read(project, table, tables);
            if (!read.Validation.IsUsable || read.Data.Rows.Count == 0) continue;

            foreach (DataColumn column in read.Data.Columns)
            {
                var field = column.ExtendedProperties["FieldDefinition"] as HexFieldDefinition;
                if (field is not { Kind: HexFieldKind.FixedString, ConsumesBytes: true } || field.Size < 3) continue;

                var row = read.Data.Rows[0];
                return (
                    table,
                    Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture),
                    column.ColumnName,
                    Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty,
                    field.Size);
            }
        }

        throw new InvalidOperationException("ModPackage smoke could not find a readable fixed-string HexTable field.");
    }

    private static string FindFirstScenarioRelativePath(CczProject project)
    {
        var scenario = new ScenarioFileReader()
            .ReadAllIndex(project)
            .Where(file => ScenarioFileReader.IsRsScriptFile(file.FileName))
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("ModPackage smoke could not find an R/S scenario file.");
        return Path.Combine("RS", scenario.FileName);
    }
}
