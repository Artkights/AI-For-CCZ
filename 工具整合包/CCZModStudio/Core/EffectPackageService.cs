using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectPackageService
{
    private const int NoPerson = 1024;
    private const int AnyJob = 255;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly HexTableReader _reader = new();
    private readonly HexTableWriter _writer = new();
    private readonly ItemEffectCatalogService _itemCatalog = new();
    private readonly PatchApplyService _patchApply = new();
    private readonly WriteOperationReportService _reportService = new();
    private readonly CczEngineProfileService _engineProfile = new();
    private readonly CmfDerivedCapabilityService _cmfDerivedCapabilityService = new();

    public IReadOnlyList<EffectCatalogEntry> ListEffects(CczProject project, IReadOnlyList<HexTableDefinition> tables, string domain, string? keyword, int limit)
    {
        var normalized = NormalizeDomain(domain);
        var entries = normalized switch
        {
            "item" => ListItemEffects(project, tables),
            "job" => ListJobEffects(project, tables),
            "personal" => ListPersonalEffects(project, tables),
            "patch" => ListPatchEffects(project),
            "cmf" => ListCmfDerivedEffects(project),
            _ => throw new InvalidOperationException("Unsupported effect domain: " + domain)
        };

        var filtered = entries
            .Where(entry => MatchesKeyword(entry, keyword))
            .Take(limit <= 0 ? 100 : Math.Min(limit, 1000))
            .ToList();
        return filtered;
    }

    public EffectCatalogEntry ReadEffect(CczProject project, IReadOnlyList<HexTableDefinition> tables, string domain, int effectId)
        => ListEffects(project, tables, domain, null, 1000)
            .FirstOrDefault(entry => entry.EffectId == effectId)
           ?? throw new InvalidOperationException($"Effect {domain}/{effectId} was not found.");

    public EffectPackage ExportPackage(CczProject project, IReadOnlyList<HexTableDefinition> tables, string domain, int effectId)
    {
        var entry = ReadEffect(project, tables, domain, effectId);
        var package = new EffectPackage
        {
            PackageId = BuildPackageId(entry.Domain, effectId),
            Domain = entry.Domain,
            EffectId = effectId,
            Name = entry.Name,
            Description = entry.Description,
            EffectValue = entry.EffectValue,
            BackupNote = "导出包用于重新导入或替换同编号特效；删除仍按固定槽位清空处理。",
            Metadata =
            {
                ["Source"] = entry.Source,
                ["ExportedAt"] = DateTime.Now.ToString("O", CultureInfo.InvariantCulture)
            }
        };

        if (entry.Details.TryGetValue("Bindings", out var bindings) && bindings is IEnumerable<EffectPackageBinding> typedBindings)
        {
            package.Bindings.AddRange(typedBindings);
        }

        return package;
    }

    public EffectPackagePreviewResult Preview(CczProject project, IReadOnlyList<HexTableDefinition> tables, EffectPackage package, string mode)
    {
        ValidatePackage(package);
        var normalizedMode = NormalizeMode(mode);
        var normalizedDomain = NormalizeDomain(package.Domain);
        var preview = new EffectPackagePreviewResult
        {
            Mode = normalizedMode,
            Domain = normalizedDomain,
            EffectId = package.EffectId,
            PackageId = NormalizePackageId(package),
            CanApply = true
        };

        switch (normalizedDomain)
        {
            case "item":
                PreviewItemPackage(project, tables, package, normalizedMode, preview);
                break;
            case "job":
                PreviewJobPackage(project, tables, package, normalizedMode, preview);
                break;
            case "personal":
                PreviewPersonalPackage(project, tables, package, normalizedMode, preview);
                break;
            case "patch":
                PreviewPatchPackage(project, package, normalizedMode, preview);
                break;
            default:
                throw new InvalidOperationException("Unsupported effect domain: " + package.Domain);
        }

        preview.CanApply = true;
        preview.Summary = $"{preview.Domain} effect {preview.EffectId} {preview.Mode}: {preview.Changes.Count} planned changes, warnings={preview.Warnings.Count}.";
        return preview;
    }

    public EffectPackageApplyResult Apply(CczProject project, IReadOnlyList<HexTableDefinition> tables, EffectPackage package, string mode)
    {
        var preview = Preview(project, tables, package, mode);

        var normalizedDomain = preview.Domain;
        var normalizedMode = preview.Mode;
        var manifest = CreateManifest(project, package, preview);
        var result = new EffectPackageApplyResult
        {
            Mode = normalizedMode,
            Domain = normalizedDomain,
            EffectId = package.EffectId,
            ManifestId = manifest.ManifestId
        };

        switch (normalizedDomain)
        {
            case "item":
                ApplyItemPackage(project, tables, package, normalizedMode, manifest, result);
                break;
            case "job":
                ApplyJobPackage(project, tables, package, normalizedMode, manifest, result);
                break;
            case "personal":
                ApplyPersonalPackage(project, tables, package, normalizedMode, manifest, result);
                break;
            case "patch":
                ApplyPatchPackage(project, package, normalizedMode, manifest, result);
                break;
            default:
                throw new InvalidOperationException("Unsupported effect domain: " + package.Domain);
        }

        result.ChangeCount = manifest.Changes.Count;
        result.ChangedBytes = manifest.Changes.Count(change => !string.Equals(change.OldValue, change.NewValue, StringComparison.Ordinal));
        result.BackupPaths.Clear();
        result.BackupPaths.AddRange(manifest.BackupPaths);
        result.ReportPaths.AddRange(manifest.ReportPaths);
        result.ManifestPath = SaveManifest(project, manifest);
        result.Summary = $"{normalizedDomain} effect {package.EffectId} {normalizedMode} applied. changes={result.ChangeCount}, manifest={result.ManifestPath}";
        return result;
    }

    public EffectPatchPreviewResult PreviewPatch(CczProject project, EffectPackage package)
    {
        ValidatePackage(package);
        var preview = new EffectPatchPreviewResult
        {
            CanApply = package.PatchSegments.Count > 0
        };

        if (!NormalizeDomain(package.Domain).Equals("patch", StringComparison.Ordinal))
        {
            preview.Warnings.Add("Only domain=patch packages can be previewed as effect patches.");
        }

        if (package.PatchSegments.Count == 0)
        {
            preview.Warnings.Add("Patch package has no patch segments.");
        }

        foreach (var group in package.PatchSegments.GroupBy(segment => FirstNonEmpty(segment.TargetFile, "Ekd5.exe"), StringComparer.OrdinalIgnoreCase))
        {
            var segments = group.ToList();
            PatchPreviewResult patchPreview;
            try
            {
                patchPreview = _patchApply.Preview(project, BuildPatchDocument(package, group.Key, segments), group.Key);
            }
            catch (Exception ex)
            {
                preview.Warnings.Add($"Patch preview failed for {group.Key}: {ex.Message}");
                continue;
            }

            for (var index = 0; index < patchPreview.Rows.Count; index++)
            {
                var row = patchPreview.Rows[index];
                var segment = segments[index];
                if (!row.CanApply)
                {
                    preview.Warnings.Add($"Patch segment {group.Key} #{row.Index} cannot apply: {row.Status}");
                }

                if (!string.IsNullOrWhiteSpace(segment.ExpectedOldBytesHex) &&
                    !HexBytesEqual(segment.ExpectedOldBytesHex, row.OldBytesHex))
                {
                    preview.Warnings.Add($"Patch segment {group.Key} #{row.Index} expected old bytes {NormalizeHexText(segment.ExpectedOldBytesHex)}, actual {NormalizeHexText(row.OldBytesHex)}.");
                }

                preview.Segments.Add(new EffectPackageChangePreview
                {
                    Category = "PatchSegment",
                    Target = group.Key,
                    RowId = row.Index,
                    Field = FirstNonEmpty(segment.HookPoint, segment.CodeCaveId, row.FileOffsetHex, "patch"),
                    OldValue = row.OldBytesHex,
                    NewValue = row.NewBytesHex,
                    Changed = row.Changed,
                    Note = string.IsNullOrWhiteSpace(segment.Comment)
                        ? row.Status
                        : $"{segment.Comment}; {row.Status}; offset={row.FileOffsetHex}"
                });
            }
        }

        preview.CanApply = true;
        preview.Summary = $"Patch package preview: segments={package.PatchSegments.Count}, warnings={preview.Warnings.Count}.";
        return preview;
    }

    public EffectPackageApplyResult ApplyPatch(CczProject project, EffectPackage package)
    {
        var preview = PreviewPatch(project, package);

        var aggregate = new EffectPackageApplyResult
        {
            Mode = "patch",
            Domain = "patch",
            EffectId = package.EffectId,
            ManifestId = CreateManifestId(package)
        };
        var manifest = new EffectManifest
        {
            ManifestId = aggregate.ManifestId,
            ProjectRoot = project.GameRoot,
            Mode = "patch",
            Domain = "patch",
            EffectId = package.EffectId,
            PackageId = NormalizePackageId(package),
            SourcePrompt = package.SourcePrompt,
            BackupNote = package.BackupNote,
            Package = package
        };

        foreach (var group in package.PatchSegments.GroupBy(segment => FirstNonEmpty(segment.TargetFile, "Ekd5.exe"), StringComparer.OrdinalIgnoreCase))
        {
            var document = BuildPatchDocument(package, group.Key, group.ToList());
            var apply = _patchApply.Apply(project, document, group.Key);
            AddBackup(manifest, aggregate, project, Path.Combine(project.GameRoot, group.Key), apply.BackupPath, "PatchSegment");
            if (!string.IsNullOrWhiteSpace(apply.ReportJsonPath))
            {
                aggregate.ReportPaths.Add(apply.ReportJsonPath);
                manifest.ReportPaths.Add(apply.ReportJsonPath);
            }
            manifest.Changes.AddRange(group.Select(segment => new EffectManifestChange
            {
                Category = "PatchSegment",
                TargetRelativePath = group.Key,
                Field = FirstNonEmpty(segment.HookPoint, segment.CodeCaveId, "patch"),
                OldValue = segment.ExpectedOldBytesHex,
                NewValue = ToHex(ParseHexBytes(segment.BytesHex)),
                Note = segment.Comment
            }));
        }

        aggregate.ChangeCount = manifest.Changes.Count;
        aggregate.ChangedBytes = manifest.Changes.Count;
        aggregate.ManifestPath = SaveManifest(project, manifest);
        aggregate.Summary = $"Patch effect {package.EffectId} applied with {aggregate.ChangeCount} segments.";
        return aggregate;
    }

    public IReadOnlyList<EffectTemplate> ListTemplates()
        =>
        [
            new EffectTemplate
            {
                TemplateId = "config.item.binding",
                Name = "宝物绑定已有特效",
                Domain = "item",
                Capability = "把已有特效号和特效值写到一个或多个物品。",
                SafetyLevel = "direct",
                RequiredParameters = ["effect_id", "name", "item_ids", "effect_value"],
                Description = "不生成新 EXE 逻辑，只维护宝物特效目录并写物品表。"
            },
            new EffectTemplate
            {
                TemplateId = "config.job.assignment",
                Name = "兵种/武将绑定已有特效",
                Domain = "job",
                Capability = "写入兵种特效说明、武将槽、兵种限制和特效值。",
                SafetyLevel = "direct",
                RequiredParameters = ["effect_id", "description", "person_ids", "job_id", "effect_value"],
                Description = "写 6.5-7-1/6.5-7-2，不修改 6.5-7 名称区。"
            },
            new EffectTemplate
            {
                TemplateId = "config.personal.exclusive",
                Name = "人物专属/套装特效",
                Domain = "personal",
                Capability = "写入 6.5-7-3 人物专属、两件装备专属、三件套或四件套槽。",
                SafetyLevel = "direct",
                RequiredParameters = ["effect_id", "slot_kind", "person_id", "item_ids", "effect_value"],
                Description = "删除时清空固定槽位，不改变表行数。"
            },
            new EffectTemplate
            {
                TemplateId = "patch.inline_stub.draft",
                Name = "内联桩补丁草案",
                Domain = "patch",
                Capability = "记录个人/装备特效号、效果值标志、叠加标志、Hook 点和代码洞。",
                SafetyLevel = "draft_only",
                RequiredParameters = ["effect_id", "personal_effect_id", "item_effect_id", "hook_point", "code_cave"],
                Description = "默认只生成待验证补丁草案；必须提供字节段并通过预览后才能应用。"
            }
        ];

    public EffectPackage BuildPackageFromTemplate(string templateId, Dictionary<string, string> parameters)
    {
        var template = ListTemplates().FirstOrDefault(x => x.TemplateId.Equals(templateId, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("Unknown effect template: " + templateId);
        var package = new EffectPackage
        {
            PackageId = BuildPackageId(template.Domain, ReadInt(parameters, "effect_id", 0)),
            Domain = template.Domain,
            EffectId = ReadInt(parameters, "effect_id", 0),
            Name = ReadString(parameters, "name", template.Name),
            Description = ReadString(parameters, "description", template.Description),
            SourcePrompt = ReadString(parameters, "source_prompt", string.Empty),
            BackupNote = "由模板生成。应用前必须先调用 preview_effect_package 或 preview_effect_patch。",
            Metadata =
            {
                ["TemplateId"] = template.TemplateId,
                ["SafetyLevel"] = template.SafetyLevel
            }
        };

        if (template.Domain == "item")
        {
            package.EffectValue = ReadInt(parameters, "effect_value", 0);
            foreach (var itemId in ReadIntList(parameters, "item_ids"))
            {
                package.Bindings.Add(new EffectPackageBinding
                {
                    Kind = "item",
                    ItemId = itemId,
                    EffectValue = package.EffectValue
                });
            }
        }
        else if (template.Domain == "job")
        {
            package.EffectValue = ReadInt(parameters, "effect_value", 0);
            var persons = ReadIntList(parameters, "person_ids").Take(3).ToList();
            package.Bindings.Add(new EffectPackageBinding
            {
                Kind = "job_assignment",
                RowId = package.EffectId,
                PersonId = persons.ElementAtOrDefault(0),
                PersonId2 = persons.ElementAtOrDefault(1),
                PersonId3 = persons.ElementAtOrDefault(2),
                JobId = ReadInt(parameters, "job_id", AnyJob),
                EffectValue = package.EffectValue
            });
        }
        else if (template.Domain == "personal")
        {
            package.EffectValue = TryReadInt(parameters, "effect_value");
            var itemIds = ReadIntList(parameters, "item_ids").ToList();
            package.Bindings.Add(new EffectPackageBinding
            {
                Kind = ReadString(parameters, "slot_kind", "person_item"),
                RowId = package.EffectId,
                PersonId = TryReadInt(parameters, "person_id"),
                ItemId = itemIds.Count > 0 ? itemIds[0] : null,
                ItemId2 = itemIds.Count > 1 ? itemIds[1] : null,
                ItemId3 = itemIds.Count > 2 ? itemIds[2] : null,
                ItemId4 = itemIds.Count > 3 ? itemIds[3] : null,
                EffectValue = package.EffectValue
            });
        }
        else if (template.Domain == "patch")
        {
            package.Metadata["PersonalEffectId"] = ReadString(parameters, "personal_effect_id", string.Empty);
            package.Metadata["ItemEffectId"] = ReadString(parameters, "item_effect_id", string.Empty);
            package.Metadata["HookPoint"] = ReadString(parameters, "hook_point", string.Empty);
            package.Metadata["CodeCave"] = ReadString(parameters, "code_cave", string.Empty);
        }

        return package;
    }

    public object GetSchemaResource()
        => new
        {
            Uri = "ccz://effects/schema",
            Schema = "EffectPackage",
            Domains = new[] { "item", "job", "personal", "patch" },
            Modes = new[] { "import", "replace", "delete" },
            DeleteSemantics = new
            {
                Item = "Clear item references to effect_id=0/effect_value=0.",
                Job = "Clear assignment row to person=1024/job=255/effect_value=0 and clear description.",
                Personal = "Clear the fixed 6.5-7-3 row to zeroes.",
                Patch = "Patch deletion has no automatic entry. Use manifest backup files manually when needed."
            }
        };

    private List<EffectCatalogEntry> ListItemEffects(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var result = new List<EffectCatalogEntry>();
        var hints = _engineProfile.Detect(project).TableHints;
        var catalog = _itemCatalog.Load(project).ToList();
        foreach (var group in catalog.GroupBy(x => x.EffectId).OrderBy(x => x.Key))
        {
            result.Add(new EffectCatalogEntry
            {
                Domain = "item",
                EffectId = group.Key,
                Name = string.Join(" / ", group.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x))),
                Description = string.Join("\n", group.Select(x => x.Description).Where(x => !string.IsNullOrWhiteSpace(x))),
                Source = "Project item effect catalog",
                Status = "catalog"
            });
        }

        foreach (var tableName in new[] { hints.ItemLowTable, hints.ItemHighTable })
        {
            var table = ResolveTable(project, tables, tableName);
            var read = _reader.Read(project, table, tables);
            foreach (DataRow row in read.Data.Rows)
            {
                var effectId = ReadInt(row, "装备特效号");
                if (effectId <= 0) continue;
                var existing = result.FirstOrDefault(x => x.EffectId == effectId);
                if (existing == null)
                {
                    existing = new EffectCatalogEntry
                    {
                        Domain = "item",
                        EffectId = effectId,
                        Name = $"#{effectId}",
                        Source = "Item table reference",
                        Status = "referenced"
                    };
                    result.Add(existing);
                }
            }
        }

        return result.OrderBy(x => x.EffectId).ToList();
    }

    private List<EffectCatalogEntry> ListJobEffects(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var hints = _engineProfile.Detect(project).TableHints;
        var description = ReadTable(project, tables, hints.JobEffectDescriptionTable);
        var assignment = ReadTable(project, tables, hints.JobEffectAssignmentTable);
        var result = new List<EffectCatalogEntry>();
        foreach (DataRow row in assignment.Data.Rows)
        {
            var id = ReadInt(row, "ID");
            var descRow = FindRowById(description.Data, id);
            var name = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            var desc = descRow == null ? string.Empty : Convert.ToString(descRow["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
            var value = ReadInt(row, "特效值");
            var entry = new EffectCatalogEntry
            {
                Domain = "job",
                EffectId = id,
                Name = string.IsNullOrWhiteSpace(name) ? $"#{id}" : name,
                Description = desc,
                EffectValue = value,
                Source = $"{description.Table.TableName}/{assignment.Table.TableName}",
                Status = IsJobAssignmentEmpty(row) && string.IsNullOrWhiteSpace(desc) ? "empty" : "configured"
            };
            entry.Details["Bindings"] = new[]
            {
                new EffectPackageBinding
                {
                    Kind = "job_assignment",
                    RowId = id,
                    PersonId = ReadInt(row, "1号武将"),
                    PersonId2 = ReadInt(row, "2号武将"),
                    PersonId3 = ReadInt(row, "3号武将"),
                    JobId = ReadInt(row, "兵种"),
                    EffectValue = value
                }
            };
            result.Add(entry);
        }

        return result;
    }

    private List<EffectCatalogEntry> ListPersonalEffects(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var hints = _engineProfile.Detect(project).TableHints;
        var exclusive = ReadTable(project, tables, hints.PersonalEffectTable);
        var result = new List<EffectCatalogEntry>();
        foreach (DataRow row in exclusive.Data.Rows)
        {
            var id = ReadInt(row, "ID");
            var name = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            var bindings = new[]
            {
                new EffectPackageBinding { Kind = "person_item_1", RowId = id, PersonId = ReadInt(row, "武将1"), ItemId = ReadInt(row, "装备1"), EffectValue = ReadInt(row, "特效值1") },
                new EffectPackageBinding { Kind = "person_item_2", RowId = id, PersonId = ReadInt(row, "武将2"), ItemId = ReadInt(row, "装备2"), EffectValue = ReadInt(row, "特效值2") },
                new EffectPackageBinding { Kind = "set_3", RowId = id, ItemId = ReadInt(row, "装备3-1"), ItemId2 = ReadInt(row, "装备3-2"), ItemId3 = ReadInt(row, "装备3-3"), EffectValue = ReadInt(row, "特效值3") },
                new EffectPackageBinding { Kind = "set_4", RowId = id, ItemId = ReadInt(row, "装备4-1"), ItemId2 = ReadInt(row, "装备4-2"), ItemId3 = ReadInt(row, "装备4-3"), EffectValue = ReadInt(row, "特效值4") }
            };
            var hasAny = bindings.Any(binding => BindingHasAnyValue(binding));
            var entry = new EffectCatalogEntry
            {
                Domain = "personal",
                EffectId = id,
                Name = string.IsNullOrWhiteSpace(name) ? $"#{id}" : name,
                Description = "人物专属、两件装备专属、三件套或四件套槽。",
                Source = exclusive.Table.TableName,
                Status = hasAny ? "configured" : "empty"
            };
            entry.Details["Bindings"] = bindings;
            result.Add(entry);
        }

        return result;
    }

    private List<EffectCatalogEntry> ListPatchEffects(CczProject project)
    {
        var root = GetManifestRoot(project);
        if (!Directory.Exists(root)) return [];
        return Directory.GetFiles(root, "*.json")
            .Select(path =>
            {
                try
                {
                    var manifest = JsonSerializer.Deserialize<EffectManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                    if (manifest == null) return null;
                    return new EffectCatalogEntry
                    {
                        Domain = "patch",
                        EffectId = manifest.EffectId,
                        Name = manifest.Package.Name,
                        Description = manifest.Package.Description,
                        Source = path,
                        Status = manifest.Mode,
                        Details =
                        {
                            ["ManifestId"] = manifest.ManifestId,
                            ["CreatedAt"] = manifest.CreatedAt,
                            ["ChangeCount"] = manifest.Changes.Count
                        }
                    };
                }
                catch
                {
                    return null;
                }
            })
            .Where(x => x != null)
            .Cast<EffectCatalogEntry>()
            .OrderByDescending(x => Convert.ToString(x.Details.GetValueOrDefault("CreatedAt"), CultureInfo.InvariantCulture))
            .ToList();
    }

    private List<EffectCatalogEntry> ListCmfDerivedEffects(CczProject project)
    {
        return _cmfDerivedCapabilityService.ListEffectCandidates(project)
            .Select((candidate, index) =>
            {
                var entry = new EffectCatalogEntry
                {
                    Domain = "cmf",
                    EffectId = index,
                    Name = candidate.Name,
                    Description = string.Join(" / ", candidate.EvidenceNotes),
                    Source = candidate.SourceCmfRelativePath,
                    Status = candidate.ConversionStatus
                };
                entry.Details["FeatureId"] = candidate.FeatureId;
                entry.Details["Category"] = candidate.Category;
                entry.Details["VersionScope"] = candidate.VersionScope;
                entry.Details["TrustLevel"] = candidate.TrustLevel.ToString();
                entry.Details["TargetSubsystem"] = candidate.TargetSubsystem;
                entry.Details["WritePolicy"] = candidate.WritePolicy;
                entry.Details["SourcePageId"] = candidate.SourcePageId;
                return entry;
            })
            .ToList();
    }

    private void PreviewItemPackage(CczProject project, IReadOnlyList<HexTableDefinition> tables, EffectPackage package, string mode, EffectPackagePreviewResult preview)
    {
        var catalog = _itemCatalog.Load(project).ToList();
        var oldCatalogRows = catalog.Where(x => x.EffectId == package.EffectId).ToList();
        if (mode != "delete")
        {
            AddPreview(preview, "ItemCatalog", _itemCatalog.GetStorePath(project), package.EffectId, "Name", string.Join(" / ", oldCatalogRows.Select(x => x.Name)), package.Name);
            AddPreview(preview, "ItemCatalog", _itemCatalog.GetStorePath(project), package.EffectId, "Description", string.Join(" / ", oldCatalogRows.Select(x => x.Description)), package.Description);
        }
        else
        {
            AddPreview(preview, "ItemCatalog", _itemCatalog.GetStorePath(project), package.EffectId, "DeleteCatalogRows", oldCatalogRows.Count.ToString(CultureInfo.InvariantCulture), "0");
        }

        var bindings = ResolveItemBindingsForMode(project, tables, package, mode).ToList();
        foreach (var binding in bindings.Where(binding => binding.ItemId.HasValue))
        {
            var itemId = binding.ItemId!.Value;
            var hints = _engineProfile.Detect(project).TableHints;
            var tableName = itemId < 104 ? hints.ItemLowTable : hints.ItemHighTable;
            var table = ReadTable(project, tables, tableName);
            var row = FindRowById(table.Data, itemId);
            if (row == null)
            {
                preview.Warnings.Add($"Item row {itemId} was not found.");
                continue;
            }

            var newEffect = mode == "delete" ? 0 : package.EffectId;
            var newValue = mode == "delete" ? 0 : binding.EffectValue ?? package.EffectValue ?? 0;
            AddPreview(preview, "ItemBinding", tableName, itemId, "装备特效号", row["装备特效号"], newEffect);
            AddPreview(preview, "ItemBinding", tableName, itemId, "装备特效号-效果值", row["装备特效号-效果值"], newValue);
        }
    }

    private void PreviewJobPackage(CczProject project, IReadOnlyList<HexTableDefinition> tables, EffectPackage package, string mode, EffectPackagePreviewResult preview)
    {
        var hints = _engineProfile.Detect(project).TableHints;
        var description = ReadTable(project, tables, hints.JobEffectDescriptionTable);
        var assignment = ReadTable(project, tables, hints.JobEffectAssignmentTable);
        var descRow = FindRowById(description.Data, package.EffectId);
        var assignRow = FindRowById(assignment.Data, package.EffectId);
        if (descRow == null || assignRow == null)
        {
            preview.Warnings.Add($"Job effect row {package.EffectId} was not found.");
            return;
        }

        var binding = package.Bindings.FirstOrDefault() ?? new EffectPackageBinding { RowId = package.EffectId };
        AddPreview(preview, "JobDescription", description.Table.TableName, package.EffectId, "介绍", descRow["介绍"], mode == "delete" ? string.Empty : package.Description);
        AddPreview(preview, "JobAssignment", assignment.Table.TableName, package.EffectId, "1号武将", assignRow["1号武将"], mode == "delete" ? NoPerson : binding.PersonId ?? NoPerson);
        AddPreview(preview, "JobAssignment", assignment.Table.TableName, package.EffectId, "2号武将", assignRow["2号武将"], mode == "delete" ? NoPerson : binding.PersonId2 ?? NoPerson);
        AddPreview(preview, "JobAssignment", assignment.Table.TableName, package.EffectId, "3号武将", assignRow["3号武将"], mode == "delete" ? NoPerson : binding.PersonId3 ?? NoPerson);
        AddPreview(preview, "JobAssignment", assignment.Table.TableName, package.EffectId, "兵种", assignRow["兵种"], mode == "delete" ? AnyJob : binding.JobId ?? AnyJob);
        AddPreview(preview, "JobAssignment", assignment.Table.TableName, package.EffectId, "特效值", assignRow["特效值"], mode == "delete" ? 0 : binding.EffectValue ?? package.EffectValue ?? 0);
    }

    private void PreviewPersonalPackage(CczProject project, IReadOnlyList<HexTableDefinition> tables, EffectPackage package, string mode, EffectPackagePreviewResult preview)
    {
        var hints = _engineProfile.Detect(project).TableHints;
        var exclusive = ReadTable(project, tables, hints.PersonalEffectTable);
        var row = FindRowById(exclusive.Data, package.EffectId);
        if (row == null)
        {
            preview.Warnings.Add($"Personal/set effect row {package.EffectId} was not found.");
            return;
        }

        preview.Warnings.AddRange(ValidatePersonalBindings(package, mode));
        if (preview.Warnings.Count > 0)
        {
            return;
        }

        var values = BuildPersonalValues(row, package, mode);
        foreach (var (column, value) in values)
        {
            AddPreview(preview, "PersonalExclusive", exclusive.Table.TableName, package.EffectId, column, row[column], value);
        }
    }

    private void PreviewPatchPackage(CczProject project, EffectPackage package, string mode, EffectPackagePreviewResult preview)
    {
        if (mode == "delete")
        {
            preview.Warnings.Add("Patch effects cannot be deleted by clearing a fixed row. No automatic delete entry is exposed; use manifest backup files manually when needed.");
            return;
        }

        var patchPreview = PreviewPatch(project, package);
        preview.Warnings.AddRange(patchPreview.Warnings);
        preview.Changes.AddRange(patchPreview.Segments);
    }

    private void ApplyItemPackage(CczProject project, IReadOnlyList<HexTableDefinition> tables, EffectPackage package, string mode, EffectManifest manifest, EffectPackageApplyResult result)
    {
        var catalog = _itemCatalog.Load(project).ToList();
        var catalogPath = _itemCatalog.GetStorePath(project);
        var catalogBackup = CreateManifestBackup(project, catalogPath, "ItemCatalog");
        AddBackup(manifest, result, project, catalogPath, catalogBackup.BackupPath, "ItemCatalog", catalogBackup.TargetExisted);
        var beforeCatalog = catalogBackup.TargetExisted ? File.ReadAllText(catalogBackup.BackupPath, Encoding.UTF8) : string.Empty;
        catalog.RemoveAll(x => x.EffectId == package.EffectId);
        if (mode != "delete")
        {
            catalog.Add(new ItemEffectCatalogEntry
            {
                EffectId = package.EffectId,
                Name = package.Name,
                Description = package.Description
            });
        }
        var savedCatalogPath = _itemCatalog.Save(project, catalog);
        var afterCatalog = File.ReadAllText(savedCatalogPath, Encoding.UTF8);
        manifest.Changes.Add(new EffectManifestChange
        {
            Category = "ItemCatalog",
            TargetRelativePath = TryWorkspaceRelative(project, savedCatalogPath),
            RowId = package.EffectId,
            Field = mode == "delete" ? "DeleteCatalogRows" : "CatalogEntry",
            OldValue = beforeCatalog,
            NewValue = afterCatalog,
            Note = "项目侧宝物特效 UTF-8 JSON 目录。"
        });

        var itemBindings = ResolveItemBindingsForMode(project, tables, package, mode)
            .Where(x => x.ItemId.HasValue)
            .ToList();
        var hints = _engineProfile.Detect(project).TableHints;
        foreach (var group in itemBindings.GroupBy(x => x.ItemId!.Value < 104 ? hints.ItemLowTable : hints.ItemHighTable))
        {
            var table = ResolveTable(project, tables, group.Key);
            var read = _reader.Read(project, table, tables);
            foreach (var binding in group)
            {
                var itemId = binding.ItemId!.Value;
                var row = FindRowById(read.Data, itemId) ?? throw new InvalidOperationException($"Item row {itemId} was not found.");
                var oldEffect = Convert.ToString(row["装备特效号"], CultureInfo.InvariantCulture) ?? string.Empty;
                var oldValue = Convert.ToString(row["装备特效号-效果值"], CultureInfo.InvariantCulture) ?? string.Empty;
                row["装备特效号"] = mode == "delete" ? 0 : package.EffectId;
                row["装备特效号-效果值"] = mode == "delete" ? 0 : binding.EffectValue ?? package.EffectValue ?? 0;
                manifest.Changes.Add(new EffectManifestChange { Category = "ItemBinding", TableName = group.Key, RowId = itemId, Field = "装备特效号", OldValue = oldEffect, NewValue = Convert.ToString(row["装备特效号"], CultureInfo.InvariantCulture) ?? string.Empty });
                manifest.Changes.Add(new EffectManifestChange { Category = "ItemBinding", TableName = group.Key, RowId = itemId, Field = "装备特效号-效果值", OldValue = oldValue, NewValue = Convert.ToString(row["装备特效号-效果值"], CultureInfo.InvariantCulture) ?? string.Empty });
            }

            var save = _writer.Save(project, table, read.Data);
            AddSaveResult(manifest, result, save);
        }
    }

    private void ApplyJobPackage(CczProject project, IReadOnlyList<HexTableDefinition> tables, EffectPackage package, string mode, EffectManifest manifest, EffectPackageApplyResult result)
    {
        var hints = _engineProfile.Detect(project).TableHints;
        var descriptionTable = ResolveTable(project, tables, hints.JobEffectDescriptionTable);
        var assignmentTable = ResolveTable(project, tables, hints.JobEffectAssignmentTable);
        var description = _reader.Read(project, descriptionTable, tables);
        var assignment = _reader.Read(project, assignmentTable, tables);
        var descRow = FindRowById(description.Data, package.EffectId) ?? throw new InvalidOperationException($"Job effect description row {package.EffectId} was not found.");
        var assignRow = FindRowById(assignment.Data, package.EffectId) ?? throw new InvalidOperationException($"Job effect assignment row {package.EffectId} was not found.");
        var binding = package.Bindings.FirstOrDefault() ?? new EffectPackageBinding();
        SetCell(descRow, "介绍", mode == "delete" ? string.Empty : package.Description, "JobDescription", descriptionTable.TableName, package.EffectId, manifest);
        SetCell(assignRow, "1号武将", mode == "delete" ? NoPerson : binding.PersonId ?? NoPerson, "JobAssignment", assignmentTable.TableName, package.EffectId, manifest);
        SetCell(assignRow, "2号武将", mode == "delete" ? NoPerson : binding.PersonId2 ?? NoPerson, "JobAssignment", assignmentTable.TableName, package.EffectId, manifest);
        SetCell(assignRow, "3号武将", mode == "delete" ? NoPerson : binding.PersonId3 ?? NoPerson, "JobAssignment", assignmentTable.TableName, package.EffectId, manifest);
        SetCell(assignRow, "兵种", mode == "delete" ? AnyJob : binding.JobId ?? AnyJob, "JobAssignment", assignmentTable.TableName, package.EffectId, manifest);
        SetCell(assignRow, "特效值", mode == "delete" ? 0 : binding.EffectValue ?? package.EffectValue ?? 0, "JobAssignment", assignmentTable.TableName, package.EffectId, manifest);

        if (description.Data.GetChanges() != null) AddSaveResult(manifest, result, _writer.Save(project, descriptionTable, description.Data));
        if (assignment.Data.GetChanges() != null) AddSaveResult(manifest, result, _writer.Save(project, assignmentTable, assignment.Data));
    }

    private void ApplyPersonalPackage(CczProject project, IReadOnlyList<HexTableDefinition> tables, EffectPackage package, string mode, EffectManifest manifest, EffectPackageApplyResult result)
    {
        var hints = _engineProfile.Detect(project).TableHints;
        var table = ResolveTable(project, tables, hints.PersonalEffectTable);
        var read = _reader.Read(project, table, tables);
        var row = FindRowById(read.Data, package.EffectId) ?? throw new InvalidOperationException($"Personal/set effect row {package.EffectId} was not found.");
        foreach (var (column, value) in BuildPersonalValues(row, package, mode))
        {
            SetCell(row, column, value, "PersonalExclusive", table.TableName, package.EffectId, manifest);
        }

        if (read.Data.GetChanges() != null) AddSaveResult(manifest, result, _writer.Save(project, table, read.Data));
    }

    private void ApplyPatchPackage(CczProject project, EffectPackage package, string mode, EffectManifest manifest, EffectPackageApplyResult result)
    {
        if (mode == "delete")
        {
            throw new InvalidOperationException("Patch delete is not exposed as an automatic operation. Use manifest backup files manually when needed.");
        }

        foreach (var segment in package.PatchSegments)
        {
            manifest.Changes.Add(new EffectManifestChange
            {
                Category = "PatchDraft",
                TargetRelativePath = FirstNonEmpty(segment.TargetFile, "Ekd5.exe"),
                Field = FirstNonEmpty(segment.HookPoint, segment.CodeCaveId, "patch"),
                OldValue = segment.ExpectedOldBytesHex,
                NewValue = ToHex(ParseHexBytes(segment.BytesHex)),
                Note = "Patch package recorded as manifest draft. Use apply_effect_patch for byte-level writes."
            });
        }
    }

    private static Dictionary<string, object> BuildPersonalValues(DataRow currentRow, EffectPackage package, string mode)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        var knownColumns = new HashSet<string>(PersonalEffectColumns, StringComparer.Ordinal);
        if (mode == "delete")
        {
            foreach (var column in PersonalEffectColumns)
            {
                values[column] = 0;
            }

            return values;
        }

        foreach (var binding in package.Bindings)
        {
            var kind = binding.Kind.Trim().ToLowerInvariant();
            var clearedColumns = new HashSet<string>(StringComparer.Ordinal);
            if (kind is "person_item_1" or "person_item" or "exclusive")
            {
                var effectValue = ResolvePersonalEffectValue(binding, package, "特效值1");
                SetPersonalSlot(values, clearedColumns, effectValue ?? 0, ("武将1", ResolvePersonalFieldValue(binding, "武将1", binding.PersonId)), ("装备1", ResolvePersonalFieldValue(binding, "装备1", binding.ItemId)), ("特效值1", effectValue));
            }
            else if (kind == "person_item_2")
            {
                var effectValue = ResolvePersonalEffectValue(binding, package, "特效值2");
                SetPersonalSlot(values, clearedColumns, effectValue ?? 0, ("武将2", ResolvePersonalFieldValue(binding, "武将2", binding.PersonId)), ("装备2", ResolvePersonalFieldValue(binding, "装备2", binding.ItemId)), ("特效值2", effectValue));
            }
            else if (kind is "set_3" or "three_piece")
            {
                var effectValue = ResolvePersonalEffectValue(binding, package, "特效值3");
                SetPersonalSlot(values, clearedColumns, effectValue ?? 0, ("装备3-1", ResolvePersonalFieldValue(binding, "装备3-1", binding.ItemId)), ("装备3-2", ResolvePersonalFieldValue(binding, "装备3-2", binding.ItemId2)), ("装备3-3", ResolvePersonalFieldValue(binding, "装备3-3", binding.ItemId3)), ("特效值3", effectValue));
            }
            else if (kind is "set_4" or "four_piece")
            {
                var effectValue = ResolvePersonalEffectValue(binding, package, "特效值4");
                SetPersonalSlot(values, clearedColumns, effectValue ?? 0, ("装备4-1", ResolvePersonalFieldValue(binding, "装备4-1", binding.ItemId)), ("装备4-2", ResolvePersonalFieldValue(binding, "装备4-2", binding.ItemId2)), ("装备4-3", ResolvePersonalFieldValue(binding, "装备4-3", binding.ItemId3)), ("特效值4", effectValue));
            }

            foreach (var (key, value) in binding.Values)
            {
                if (knownColumns.Contains(key) && !clearedColumns.Contains(key)) values[key] = value;
            }
        }

        foreach (var column in values.Keys.ToList())
        {
            if (!currentRow.Table.Columns.Contains(column))
            {
                values.Remove(column);
            }
        }

        return values;
    }

    private static IReadOnlyList<string> ValidatePersonalBindings(EffectPackage package, string mode)
    {
        if (mode == "delete") return Array.Empty<string>();
        var warnings = new List<string>();
        foreach (var binding in package.Bindings)
        {
            var kind = binding.Kind.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(kind))
            {
                warnings.Add("Personal binding kind is required. Use person_item_1, person_item_2, set_3, or set_4.");
                continue;
            }

            var effectColumn = ResolvePersonalEffectColumn(kind);
            var effectValue = effectColumn == null ? null : ResolvePersonalEffectValue(binding, package, effectColumn);
            if (!effectValue.HasValue)
            {
                warnings.Add($"Personal binding {kind} requires explicit effect_value. Use 0 only when intentionally clearing that slot.");
                continue;
            }

            if (effectValue.Value == 0)
            {
                continue;
            }

            if (kind is "person_item_1" or "person_item" or "exclusive")
            {
                RequirePersonalField(warnings, binding, "person_item_1", "武将1", "PersonId", binding.PersonId);
                RequirePersonalField(warnings, binding, "person_item_1", "装备1", "ItemId", binding.ItemId);
            }
            else if (kind == "person_item_2")
            {
                RequirePersonalField(warnings, binding, "person_item_2", "武将2", "PersonId", binding.PersonId);
                RequirePersonalField(warnings, binding, "person_item_2", "装备2", "ItemId", binding.ItemId);
            }
            else if (kind is "set_3" or "three_piece")
            {
                RequirePersonalField(warnings, binding, "set_3", "装备3-1", "ItemId", binding.ItemId);
                RequirePersonalField(warnings, binding, "set_3", "装备3-2", "ItemId2", binding.ItemId2);
                RequirePersonalField(warnings, binding, "set_3", "装备3-3", "ItemId3", binding.ItemId3);
            }
            else if (kind is "set_4" or "four_piece")
            {
                RequirePersonalField(warnings, binding, "set_4", "装备4-1", "ItemId", binding.ItemId);
                RequirePersonalField(warnings, binding, "set_4", "装备4-2", "ItemId2", binding.ItemId2);
                RequirePersonalField(warnings, binding, "set_4", "装备4-3", "ItemId3", binding.ItemId3);
            }
            else
            {
                warnings.Add("Unsupported personal binding kind: " + binding.Kind);
            }
        }

        return warnings;
    }

    private TableReadResult ReadTable(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName)
    {
        var table = ResolveTable(project, tables, tableName);
        return _reader.Read(project, table, tables);
    }

    private IEnumerable<EffectPackageBinding> ResolveItemBindingsForMode(CczProject project, IReadOnlyList<HexTableDefinition> tables, EffectPackage package, string mode)
    {
        var explicitBindings = package.Bindings.Where(binding => binding.ItemId.HasValue).ToList();
        if (mode != "delete" || explicitBindings.Count > 0)
        {
            return explicitBindings;
        }

        var references = new List<EffectPackageBinding>();
        var hints = _engineProfile.Detect(project).TableHints;
        foreach (var tableName in new[] { hints.ItemLowTable, hints.ItemHighTable })
        {
            var read = ReadTable(project, tables, tableName);
            foreach (DataRow row in read.Data.Rows)
            {
                if (ReadInt(row, "装备特效号") != package.EffectId) continue;
                references.Add(new EffectPackageBinding
                {
                    Kind = "item",
                    ItemId = ReadInt(row, "ID"),
                    EffectValue = ReadInt(row, "装备特效号-效果值"),
                    Note = "Auto-discovered item reference for delete mode."
                });
            }
        }

        return references;
    }

    private static HexTableDefinition ResolveTable(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName)
        => HexTableNameResolver.ResolveForProject(project, tables, tableName);

    private static readonly string[] PersonalEffectColumns =
    [
        "武将1", "装备1", "特效值1", "武将2", "装备2", "特效值2",
        "装备3-1", "装备3-2", "装备3-3", "特效值3",
        "装备4-1", "装备4-2", "装备4-3", "特效值4"
    ];

    private static int? ResolvePersonalEffectValue(EffectPackageBinding binding, EffectPackage package, string effectColumn)
        => binding.EffectValue ?? package.EffectValue ?? (binding.Values.TryGetValue(effectColumn, out var value) ? value : null);

    private static int? ResolvePersonalFieldValue(EffectPackageBinding binding, string columnName, int? typedValue)
        => typedValue ?? (binding.Values.TryGetValue(columnName, out var value) ? value : null);

    private static string? ResolvePersonalEffectColumn(string normalizedKind)
        => normalizedKind switch
        {
            "person_item_1" or "person_item" or "exclusive" => "特效值1",
            "person_item_2" => "特效值2",
            "set_3" or "three_piece" => "特效值3",
            "set_4" or "four_piece" => "特效值4",
            _ => null
        };

    private static void RequirePersonalField(List<string> warnings, EffectPackageBinding binding, string slotKind, string columnName, string apiName, int? value)
    {
        if (ResolvePersonalFieldValue(binding, columnName, value).HasValue) return;
        warnings.Add($"Personal binding {slotKind} requires {apiName}/{columnName} when effect_value is non-zero.");
    }

    private static void SetPersonalSlot(Dictionary<string, object> values, HashSet<string> clearedColumns, int effectValue, params (string Column, int? Value)[] fields)
    {
        if (effectValue == 0)
        {
            foreach (var (column, _) in fields)
            {
                values[column] = 0;
                clearedColumns.Add(column);
            }

            return;
        }

        foreach (var (column, value) in fields)
        {
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Personal effect field {column} is required when effect value is non-zero.");
            }

            values[column] = value.Value;
        }
    }

    private static void AddPreview(EffectPackagePreviewResult preview, string category, string target, int? rowId, string field, object? oldValue, object? newValue)
    {
        var oldText = Convert.ToString(oldValue, CultureInfo.InvariantCulture) ?? string.Empty;
        var newText = Convert.ToString(newValue, CultureInfo.InvariantCulture) ?? string.Empty;
        preview.Changes.Add(new EffectPackageChangePreview
        {
            Category = category,
            Target = target,
            RowId = rowId,
            Field = field,
            OldValue = oldText,
            NewValue = newText,
            Changed = !string.Equals(oldText, newText, StringComparison.Ordinal)
        });
    }

    private static void SetCell(DataRow row, string column, object value, string category, string tableName, int rowId, EffectManifest manifest)
    {
        var oldValue = Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty;
        row[column] = value;
        var newValue = Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty;
        manifest.Changes.Add(new EffectManifestChange
        {
            Category = category,
            TableName = tableName,
            RowId = rowId,
            Field = column,
            OldValue = oldValue,
            NewValue = newValue
        });
    }

    private static void AddSaveResult(EffectManifest manifest, EffectPackageApplyResult result, TableSaveResult save)
    {
        AddBackup(manifest, result, null, save.FilePath, save.BackupPath, "TableSave");
        if (!string.IsNullOrWhiteSpace(save.ReportJsonPath)) manifest.ReportPaths.Add(save.ReportJsonPath);
        if (!string.IsNullOrWhiteSpace(save.ReportJsonPath)) result.ReportPaths.Add(save.ReportJsonPath);
    }

    private static void AddBackup(
        EffectManifest manifest,
        EffectPackageApplyResult result,
        CczProject? project,
        string targetPath,
        string backupPath,
        string category,
        bool targetExisted = true)
    {
        if (string.IsNullOrWhiteSpace(backupPath)) return;
        manifest.BackupPaths.Add(backupPath);
        result.BackupPaths.Add(backupPath);
        manifest.Backups.Add(new EffectManifestBackup
        {
            TargetPath = targetPath,
            TargetRelativePath = project == null ? string.Empty : TryWorkspaceRelative(project, targetPath),
            BackupPath = backupPath,
            Category = category,
            TargetExisted = targetExisted
        });
    }

    private static EffectManifestBackup CreateManifestBackup(CczProject project, string targetPath, string category)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var existed = File.Exists(targetPath);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var safeName = Path.GetFileName(targetPath);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = MakeSafeFileStem(category);
        }

        var backupPath = Path.Combine(backupRoot, $"{stamp}_{category}_{safeName}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{stamp}_{category}_{suffix++}_{safeName}");
        }

        if (existed)
        {
            File.Copy(targetPath, backupPath, overwrite: false);
        }
        else
        {
            File.WriteAllText(backupPath, string.Empty, Encoding.UTF8);
        }

        return new EffectManifestBackup
        {
            TargetPath = targetPath,
            TargetRelativePath = TryWorkspaceRelative(project, targetPath),
            BackupPath = backupPath,
            Category = category,
            TargetExisted = existed
        };
    }

    private EffectManifest CreateManifest(CczProject project, EffectPackage package, EffectPackagePreviewResult preview)
        => new()
        {
            ManifestId = CreateManifestId(package),
            ProjectRoot = project.GameRoot,
            Mode = preview.Mode,
            Domain = preview.Domain,
            EffectId = package.EffectId,
            PackageId = NormalizePackageId(package),
            SourcePrompt = package.SourcePrompt,
            BackupNote = package.BackupNote,
            Package = package,
            Metadata =
            {
                ["PreviewSummary"] = preview.Summary,
                ["WarningCount"] = preview.Warnings.Count.ToString(CultureInfo.InvariantCulture)
            }
        };

    private string SaveManifest(CczProject project, EffectManifest manifest)
    {
        var root = GetManifestRoot(project);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"{manifest.ManifestId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8);
        return path;
    }

    private static string GetManifestRoot(CczProject project)
        => Path.Combine(project.WorkspaceRoot, "CCZModStudio_Notes", "EffectManifests");

    private static string ResolveManifestPath(CczProject project, string manifestId)
    {
        if (string.IsNullOrWhiteSpace(manifestId)) throw new InvalidOperationException("manifest_id is required.");
        var root = GetManifestRoot(project);
        var safe = Path.GetFileNameWithoutExtension(manifestId.Trim());
        var path = Path.Combine(root, safe + ".json");
        if (!File.Exists(path)) throw new FileNotFoundException("Effect manifest was not found.", path);
        return path;
    }

    private static PatchDocument BuildPatchDocument(EffectPackage package, string targetFile, IReadOnlyList<EffectPatchSegment> segments)
    {
        var addressKind = ParseAddressKind(segments.FirstOrDefault()?.AddressKind);
        return new PatchDocument
        {
            SourcePath = $"EffectPackage:{NormalizePackageId(package)}:{targetFile}",
            Version = "CCZ_EFFECT_PACKAGE_1",
            AddressKind = addressKind,
            Entries = segments.Select((segment, index) => new PatchEntry
            {
                Index = index + 1,
                Address = ResolveSegmentAddress(segment),
                Bytes = ParseHexBytes(segment.BytesHex),
                SourceLine = index + 1,
                Comment = segment.Comment
            }).ToList(),
            Comments = package.SourceLinks
        };
    }

    private static PatchAddressKind ParseAddressKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return PatchAddressKind.OdVirtualAddress;
        return value.Trim().ToLowerInvariant() switch
        {
            "o" or "od" or "odvirtualaddress" or "virtual" => PatchAddressKind.OdVirtualAddress,
            "u" or "ue" or "file" or "fileoffset" => PatchAddressKind.FileOffset,
            _ => throw new InvalidOperationException("Unsupported patch address kind: " + value)
        };
    }

    private static uint ResolveSegmentAddress(EffectPatchSegment segment)
    {
        if (segment.Address != 0) return segment.Address;
        var text = segment.AddressHex.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return value;
        throw new InvalidOperationException("Patch segment address is required.");
    }

    private static byte[] ParseHexBytes(string text)
    {
        var chars = text.Where(Uri.IsHexDigit).ToArray();
        if (chars.Length == 0) return [];
        if (chars.Length % 2 != 0) throw new InvalidOperationException("Hex byte string has odd length.");
        var result = new byte[chars.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = byte.Parse(new string(chars, i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static string ToHex(byte[] bytes)
        => BitConverter.ToString(bytes).Replace("-", " ");

    private static string NormalizeDomain(string domain)
    {
        var value = (domain ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "" => "item",
            "item" or "宝物" or "equipment" => "item",
            "job" or "兵种" => "job",
            "personal" or "person" or "exclusive" or "个人" or "套装" => "personal",
            "patch" or "补丁" => "patch",
            _ => value
        };
    }

    private static string NormalizeMode(string mode)
    {
        var value = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "" => "import",
            "import" or "导入" => "import",
            "replace" or "替换" => "replace",
            "delete" or "删除" => "delete",
            _ => throw new InvalidOperationException("Unsupported effect package mode: " + mode)
        };
    }

    private static void ValidatePackage(EffectPackage package)
    {
        if (package.EffectId < 0 || package.EffectId > ushort.MaxValue)
        {
            throw new InvalidOperationException("EffectId must be between 0 and 65535.");
        }
    }

    private static string NormalizePackageId(EffectPackage package)
        => string.IsNullOrWhiteSpace(package.PackageId) ? BuildPackageId(package.Domain, package.EffectId) : MakeSafeFileStem(package.PackageId);

    private static string BuildPackageId(string domain, int effectId)
        => $"{NormalizeDomain(domain)}-{effectId}-{DateTime.Now:yyyyMMddHHmmss}";

    private static string CreateManifestId(EffectPackage package)
        => $"{NormalizeDomain(package.Domain)}-{package.EffectId}-{DateTime.Now:yyyyMMddHHmmssfff}";

    private static string MakeSafeFileStem(string value)
    {
        var result = string.IsNullOrWhiteSpace(value) ? "effect-package" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
        return result.Replace(' ', '_');
    }

    private static bool MatchesKeyword(EffectCatalogEntry entry, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return Contains(entry.Domain, keyword) ||
               Contains(entry.Name, keyword) ||
               Contains(entry.Description, keyword) ||
               Contains(entry.Source, keyword) ||
               Contains(entry.Status, keyword) ||
               entry.EffectId.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string value, string keyword)
        => value.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static DataRow? FindRowById(DataTable table, int id)
        => table.Rows.Cast<DataRow>().FirstOrDefault(row => ReadInt(row, "ID") == id);

    private static int ReadInt(DataRow row, string column)
        => Convert.ToInt32(row[column], CultureInfo.InvariantCulture);

    private static bool IsJobAssignmentEmpty(DataRow row)
        => ReadInt(row, "1号武将") == NoPerson &&
           ReadInt(row, "2号武将") == NoPerson &&
           ReadInt(row, "3号武将") == NoPerson &&
           ReadInt(row, "兵种") == AnyJob &&
           ReadInt(row, "特效值") == 0;

    private static bool BindingHasAnyValue(EffectPackageBinding binding)
        => (binding.PersonId ?? 0) != 0 ||
           (binding.ItemId ?? 0) != 0 ||
           (binding.ItemId2 ?? 0) != 0 ||
           (binding.ItemId3 ?? 0) != 0 ||
           (binding.ItemId4 ?? 0) != 0 ||
           (binding.EffectValue ?? 0) != 0;

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static int ReadInt(Dictionary<string, string> values, string key, int fallback)
        => values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static int? TryReadInt(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string ReadString(Dictionary<string, string> values, string key, string fallback)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static IEnumerable<int> ReadIntList(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) yield break;
        foreach (var part in value.Split(new[] { ',', ';', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) yield return parsed;
        }
    }

    private static string TryWorkspaceRelative(CczProject project, string path)
    {
        try
        {
            return Path.GetRelativePath(project.WorkspaceRoot, path)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }

    private static bool HexBytesEqual(string left, string right)
        => ParseHexBytes(left).SequenceEqual(ParseHexBytes(right));

    private static string NormalizeHexText(string text)
        => ToHex(ParseHexBytes(text));
}
