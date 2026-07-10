using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CmfManualSeedService
{
    public const string ExtractionMode = "ManualConfirmedSeed";
    public const string TrustLevel = "ManualConfirmed";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public IReadOnlyList<CmfManualSeedDocument> LoadSeedDocuments(CczProject? project = null)
        => ResolveSeedFiles(project)
            .Select(path => JsonSerializer.Deserialize<CmfManualSeedDocument>(File.ReadAllText(path, Encoding.UTF8), JsonOptions))
            .Where(seed => seed != null)
            .Cast<CmfManualSeedDocument>()
            .DistinctBy(seed => NormalizeRelativePath(seed.SourceCmfRelativePath) + "|" + seed.SourceSha256, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public CmfManualSeedValidationReport ValidateSeeds(CczProject? project = null)
    {
        var seeds = LoadSeedDocuments(project);
        var issues = new List<CmfManualSeedValidationIssue>();
        foreach (var seed in seeds)
        {
            ValidateSeed(seed, issues);
        }

        return new CmfManualSeedValidationReport
        {
            SeedCount = seeds.Count,
            FieldCount = seeds.Sum(seed => seed.Fields.Count),
            TableCount = seeds.Sum(seed => seed.Tables.Count),
            ExpandedTableEntryCount = seeds.Sum(seed => seed.Tables.Sum(table => table.Entries.Count)),
            Issues = issues
        };
    }

    public IReadOnlyList<CmfDesignerSnapshot> LoadManualSeedSnapshots(CczProject project, string? relativePath = null)
    {
        var oldToolsRoot = CCZModStudio.Formats.CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot);
        return LoadSeedDocuments(project)
            .Where(seed => string.IsNullOrWhiteSpace(relativePath) || MatchesRelativePath(seed.SourceCmfRelativePath, relativePath))
            .Select(seed => BuildSnapshotForSeed(project, oldToolsRoot, seed, relativePath))
            .ToArray();
    }

    public CmfDesignerSnapshot? TryCreateSnapshotForCmf(CczProject project, CmfToolProject cmf)
    {
        var seed = LoadSeedDocuments(project)
            .FirstOrDefault(item => MatchesRelativePath(item.SourceCmfRelativePath, cmf.RelativePath));
        return seed == null
            ? null
            : BuildSnapshot(seed, cmf.SourcePath, cmf.RelativePath, cmf.Sha256, cmf.Length);
    }

    public CmfDesignerSnapshot? TryCreateSnapshotForRelativePath(CczProject project, string relativePath)
    {
        var oldToolsRoot = CCZModStudio.Formats.CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot);
        var seed = LoadSeedDocuments(project)
            .FirstOrDefault(item => MatchesRelativePath(item.SourceCmfRelativePath, relativePath));
        return seed == null ? null : BuildSnapshotForSeed(project, oldToolsRoot, seed, relativePath);
    }

    public CmfDesignerSnapshot MergeSnapshots(CmfDesignerSnapshot primary, CmfDesignerSnapshot manual)
    {
        if (primary.Bindings.Count == 0)
        {
            return manual;
        }

        var pages = primary.Pages.Concat(manual.Pages)
            .DistinctBy(page => page.PageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var modules = primary.Modules.Concat(manual.Modules)
            .DistinctBy(module => module.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var controls = primary.Controls.Concat(manual.Controls)
            .DistinctBy(control => control.ControlId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var warnings = primary.Warnings.Concat(manual.Warnings).ToList();
        var bindings = primary.Bindings.ToList();

        foreach (var manualBinding in manual.Bindings)
        {
            var existingIndex = bindings.FindIndex(binding => SameAddressBinding(binding, manualBinding));
            if (existingIndex < 0)
            {
                bindings.Add(manualBinding);
                continue;
            }

            var existing = bindings[existingIndex];
            if (IsCompatibleBinding(existing, manualBinding))
            {
                bindings[existingIndex] = MergeCompatibleBinding(existing, manualBinding);
                continue;
            }

            warnings.Add(
                "Manual seed conflict: " +
                $"{manualBinding.TargetFile}@{manualBinding.UeOffsetHex} " +
                $"{manualBinding.DisplayName} conflicts with existing binding {existing.DisplayName}.");
            bindings.Add(CloneBindingWithStatus(manualBinding, "NeedsManualReview", "人工清单冲突"));
        }

        return new CmfDesignerSnapshot
        {
            SchemaVersion = primary.SchemaVersion,
            SourcePath = primary.SourcePath,
            RelativePath = string.IsNullOrWhiteSpace(primary.RelativePath) ? manual.RelativePath : primary.RelativePath,
            SourceSha256 = string.IsNullOrWhiteSpace(primary.SourceSha256) ? manual.SourceSha256 : primary.SourceSha256,
            SourceLength = primary.SourceLength == 0 ? manual.SourceLength : primary.SourceLength,
            ExtractedAtUtc = primary.ExtractedAtUtc,
            CheatMakerExePath = primary.CheatMakerExePath,
            CheatMakerVersion = primary.CheatMakerVersion,
            ExtractionMode = primary.ExtractionMode.Contains(ExtractionMode, StringComparison.OrdinalIgnoreCase)
                ? primary.ExtractionMode
                : primary.ExtractionMode + "+" + ExtractionMode,
            ReportDirectory = primary.ReportDirectory,
            Pages = pages,
            Modules = modules,
            Controls = controls,
            Bindings = bindings,
            RawUiTree = primary.RawUiTree,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static CmfDesignerSnapshot BuildSnapshotForSeed(
        CczProject project,
        string? oldToolsRoot,
        CmfManualSeedDocument seed,
        string? relativePath)
    {
        var effectiveRelativePath = string.IsNullOrWhiteSpace(relativePath)
            ? seed.SourceCmfRelativePath
            : relativePath!;
        var sourcePath = string.IsNullOrWhiteSpace(oldToolsRoot)
            ? string.Empty
            : Path.GetFullPath(Path.Combine(oldToolsRoot, effectiveRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        var sourceSha = string.Empty;
        long sourceLength = 0;
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
        {
            sourceSha = ComputeSha256(sourcePath);
            sourceLength = new FileInfo(sourcePath).Length;
        }

        return BuildSnapshot(seed, sourcePath, effectiveRelativePath, sourceSha, sourceLength);
    }

    private static CmfDesignerSnapshot BuildSnapshot(
        CmfManualSeedDocument seed,
        string sourcePath,
        string relativePath,
        string actualSourceSha,
        long actualSourceLength)
    {
        var issues = new List<CmfManualSeedValidationIssue>();
        ValidateSeed(seed, issues);

        var warnings = issues
            .Select(issue => $"{issue.Severity} {issue.Code}: {issue.Message}")
            .ToList();
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            warnings.Add("Manual seed source CMF file was not found; fields remain read-only evidence.");
        }

        var sourceSha = string.IsNullOrWhiteSpace(actualSourceSha) ? seed.SourceSha256 : actualSourceSha;
        var shaMismatch = !string.IsNullOrWhiteSpace(actualSourceSha) &&
                          !string.IsNullOrWhiteSpace(seed.SourceSha256) &&
                          !actualSourceSha.Equals(seed.SourceSha256, StringComparison.OrdinalIgnoreCase);
        if (shaMismatch)
        {
            warnings.Add(
                "Manual seed SHA mismatch. Expected " + seed.SourceSha256 +
                ", current CMF is " + actualSourceSha +
                ". Fields are marked NeedsManualReview.");
        }

        var bindingStatus = shaMismatch ? "NeedsManualReview" : TrustLevel;
        var seedPrefix = GetSeedIdPrefix(seed);
        var pageId = seedPrefix + "-manual-cmf-page";
        var modules = BuildModules(seed, pageId, seedPrefix).ToArray();
        var controls = new List<CmfDesignerControl>();
        var bindings = new List<CmfDesignerBinding>();
        var moduleLookup = modules.ToDictionary(module => module.Title, module => module.ModuleId, StringComparer.OrdinalIgnoreCase);
        var y = 24;

        foreach (var field in seed.Fields)
        {
            var moduleId = ResolveModuleId(moduleLookup, field.Module);
            var controlId = seedPrefix + "-manual-control-" + field.FieldId;
            controls.Add(new CmfDesignerControl
            {
                ControlId = controlId,
                PageId = pageId,
                ModuleId = moduleId,
                ControlType = NormalizeControlType(field.UiControl),
                Name = field.FieldId,
                Text = field.DisplayName,
                Bounds = new CmfUiRect(16, y, 320, 22),
                Properties = BuildFieldSourceProperties(seed, field)
            });
            bindings.Add(new CmfDesignerBinding
            {
                BindingId = seedPrefix + "-manual-field-" + field.FieldId,
                PageId = pageId,
                ModuleId = moduleId,
                ControlId = controlId,
                ControlName = field.FieldId,
                ControlType = NormalizeControlType(field.UiControl),
                DisplayName = field.DisplayName,
                TargetFile = seed.TargetFile,
                AddressKind = seed.AddressKind,
                UeOffsetHex = NormalizeHex(field.UeOffsetHex),
                UeOffset = TryParseHex(field.UeOffsetHex, out var offset) ? offset : null,
                ByteLength = field.ByteLength,
                DataType = field.DataType,
                FunctionType = string.IsNullOrWhiteSpace(field.FunctionType) ? "人工确认" : field.FunctionType,
                DefaultValueRaw = field.DefaultValueRaw,
                DefaultValueParsed = NormalizeDefaultValue(field.DefaultValueRaw, field.ByteLength),
                DataListRaw = BuildCheckBoxDataList(field),
                ValidationStatus = bindingStatus,
                SourceProperties = BuildFieldSourceProperties(seed, field)
            });
            y += 24;
        }

        foreach (var table in seed.Tables)
        {
            var moduleId = ResolveModuleId(moduleLookup, table.Module);
            var flags = string.Join(";", table.BitFlags.Select(flag => $"{NormalizeHex(flag.Hex)}={flag.Name}"));
            foreach (var entry in table.Entries)
            {
                var controlId = seedPrefix + "-manual-control-" + table.TableId + "-" + entry.EntryId.ToString("X2", CultureInfo.InvariantCulture);
                var displayName = table.DisplayName + "：" + entry.Name;
                controls.Add(new CmfDesignerControl
                {
                    ControlId = controlId,
                    PageId = pageId,
                    ModuleId = moduleId,
                    ControlType = "TableEntry",
                    Name = table.TableId + "-" + entry.EntryIdHex,
                    Text = displayName,
                    Bounds = new CmfUiRect(360, y, 320, 22),
                    Properties = BuildSourceProperties(seed, table.Notes.Concat([$"entryId={entry.EntryIdHex}", "bitFlags=" + flags]), false)
                });
                bindings.Add(new CmfDesignerBinding
                {
                    BindingId = seedPrefix + "-manual-table-" + table.TableId + "-" + entry.EntryId.ToString("X2", CultureInfo.InvariantCulture),
                    PageId = pageId,
                    ModuleId = moduleId,
                    ControlId = controlId,
                    ControlName = table.TableId + "-" + entry.EntryIdHex,
                    ControlType = "TableEntry",
                    DisplayName = displayName,
                    TargetFile = seed.TargetFile,
                    AddressKind = seed.AddressKind,
                    UeOffsetHex = NormalizeHex(entry.UeOffsetHex),
                    UeOffset = TryParseHex(entry.UeOffsetHex, out var offset) ? offset : null,
                    ByteLength = table.EntryByteLength,
                    DataType = string.IsNullOrWhiteSpace(table.DataType) ? table.ValueKind : table.DataType,
                    FunctionType = string.IsNullOrWhiteSpace(table.FunctionType) ? "人工确认" : table.FunctionType,
                    DefaultValueRaw = entry.DefaultValueRaw,
                    DefaultValueParsed = NormalizeDefaultValue(entry.DefaultValueRaw, table.EntryByteLength),
                    ValidationStatus = bindingStatus,
                    SourceProperties = BuildSourceProperties(seed, table.Notes.Concat([$"entryId={entry.EntryIdHex}", "valueKind={table.ValueKind}", "bitFlags=" + flags]), false)
                });
                y += 24;
            }
        }

        var modulesWithBindings = modules.Select(module => new CmfDesignerModule
        {
            ModuleId = module.ModuleId,
            PageId = module.PageId,
            Title = module.Title,
            Bounds = module.Bounds,
            Notes = module.Notes,
            ControlIds = controls
                .Where(control => control.ModuleId.Equals(module.ModuleId, StringComparison.OrdinalIgnoreCase))
                .Select(control => control.ControlId)
                .ToArray(),
            BindingIds = bindings
                .Where(binding => binding.ModuleId.Equals(module.ModuleId, StringComparison.OrdinalIgnoreCase))
                .Select(binding => binding.BindingId)
                .ToArray()
        }).ToArray();

        return new CmfDesignerSnapshot
        {
            SourcePath = sourcePath,
            RelativePath = string.IsNullOrWhiteSpace(relativePath) ? seed.SourceCmfRelativePath : relativePath,
            SourceSha256 = sourceSha,
            SourceLength = actualSourceLength,
            ExtractedAtUtc = DateTime.UtcNow,
            CheatMakerExePath = string.Empty,
            CheatMakerVersion = "Manual seed",
            ExtractionMode = ExtractionMode,
            Pages =
            [
                new CmfDesignerPage
                {
                    PageId = pageId,
                    Name = FormatManualSeedPageName(seed),
                    WindowTitle = seed.SourceCmfRelativePath,
                    Bounds = new CmfUiRect(0, 0, 1024, Math.Max(720, y + 24))
                }
            ],
            Modules = modulesWithBindings,
            Controls = controls,
            Bindings = bindings,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static IEnumerable<CmfDesignerModule> BuildModules(CmfManualSeedDocument seed, string pageId, string seedPrefix)
    {
        var moduleNames = seed.Fields.Select(field => field.Module)
            .Concat(seed.Tables.Select(table => table.Module))
            .Where(module => !string.IsNullOrWhiteSpace(module))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var top = 12;
        foreach (var moduleName in moduleNames)
        {
            yield return new CmfDesignerModule
            {
                ModuleId = seedPrefix + "-manual-module-" + StableToken(moduleName),
                PageId = pageId,
                Title = moduleName,
                Bounds = new CmfUiRect(8, top, 720, 120),
                Notes =
                [
                    new CmfModuleNote
                    {
                        Text = "人工确认来源；只读候选，正式写入需测试副本 WriteVerified。",
                        Bounds = new CmfUiRect(12, top + 22, 520, 18),
                        Color = "ManualConfirmed"
                    }
                ]
            };
            top += 132;
        }
    }

    private static string GetSeedIdPrefix(CmfManualSeedDocument seed)
    {
        if (seed.VersionScope.Contains("6.6", StringComparison.OrdinalIgnoreCase)) return "star66";
        if (seed.VersionScope.Contains("6.5", StringComparison.OrdinalIgnoreCase)) return "star65";
        return "manual-" + StableToken(seed.VersionScope);
    }

    private static string FormatManualSeedPageName(CmfManualSeedDocument seed)
        => string.IsNullOrWhiteSpace(seed.VersionScope)
            ? "引擎 EXE 人工清单"
            : seed.VersionScope.Replace("Star", "Star", StringComparison.OrdinalIgnoreCase) + " 引擎 EXE 人工清单";

    private static void ValidateSeed(CmfManualSeedDocument seed, List<CmfManualSeedValidationIssue> issues)
    {
        AddIf(string.IsNullOrWhiteSpace(seed.SourceCmfRelativePath), issues, "Error", "MissingSourceCmf", "$.sourceCmfRelativePath", "sourceCmfRelativePath is required.");
        AddIf(string.IsNullOrWhiteSpace(seed.SourceSha256) || seed.SourceSha256.Length != 64, issues, "Error", "InvalidSourceSha256", "$.sourceSha256", "sourceSha256 must be a 64-character SHA256 hex string.");
        AddIf(string.IsNullOrWhiteSpace(seed.TargetFile), issues, "Error", "MissingTargetFile", "$.targetFile", "targetFile is required.");
        AddIf(!string.IsNullOrWhiteSpace(seed.TargetSha256) && seed.TargetSha256.Length != 64, issues, "Error", "InvalidTargetSha256", "$.targetSha256", "targetSha256 must be a 64-character SHA256 hex string when provided.");
        AddIf(!seed.AddressKind.Equals("UeFileOffset", StringComparison.OrdinalIgnoreCase), issues, "Error", "UnsupportedAddressKind", "$.addressKind", "manual seed currently supports UeFileOffset only.");

        var offsets = new Dictionary<long, string>();
        for (var i = 0; i < seed.Fields.Count; i++)
        {
            var field = seed.Fields[i];
            var path = "$.fields[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            AddIf(string.IsNullOrWhiteSpace(field.FieldId), issues, "Error", "MissingFieldId", path + ".fieldId", "fieldId is required.");
            AddIf(string.IsNullOrWhiteSpace(field.DisplayName), issues, "Error", "MissingDisplayName", path + ".displayName", "displayName is required.");
            AddIf(field.ByteLength <= 0, issues, "Error", "InvalidByteLength", path + ".byteLength", "byteLength must be greater than zero.");
            AddIf(string.IsNullOrWhiteSpace(field.DataType), issues, "Error", "MissingDataType", path + ".dataType", "dataType is required.");
            if (!TryParseHex(field.UeOffsetHex, out var offset))
            {
                issues.Add(new CmfManualSeedValidationIssue { Severity = "Error", Code = "InvalidOffset", Path = path + ".ueOffsetHex", Message = "ueOffsetHex is not a valid hex integer." });
            }
            else
            {
                AddDuplicateOffset(offsets, offset, path, issues);
            }

            if (field.LengthIsManualDefault && !field.Notes.Any(note => note.Contains("默认长度", StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new CmfManualSeedValidationIssue { Severity = "Error", Code = "MissingDefaultLengthNote", Path = path + ".notes", Message = "manual-default byte length fields must include 默认长度 note." });
            }

            if (field.DisplayFormat.Equals("BareHex", StringComparison.OrdinalIgnoreCase) ||
                field.ValueKind.Equals("HexByteBare", StringComparison.OrdinalIgnoreCase))
            {
                AddIf(field.ByteLength != 1, issues, "Error", "InvalidBareHexLength", path + ".byteLength", "BareHex fields must use 1-byte values.");
            }

            if (field.ValueKind.Equals("ShiftedTwoBitDecimal", StringComparison.OrdinalIgnoreCase))
            {
                var invalidShift = field.Shift == null || field.Shift.Value < 0 || field.Shift.Value > 6;
                AddIf(field.ByteLength != 1, issues, "Error", "InvalidShiftedTwoBitLength", path + ".byteLength", "ShiftedTwoBitDecimal fields must use 1-byte values.");
                AddIf(invalidShift, issues, "Error", "InvalidShiftedTwoBitShift", path + ".shift", "ShiftedTwoBitDecimal shift must be 0..6.");
                if (!TryParseHex(field.MaskHex, out var mask) || mask < 0 || mask > 0xFF)
                {
                    issues.Add(new CmfManualSeedValidationIssue { Severity = "Error", Code = "InvalidShiftedTwoBitMask", Path = path + ".maskHex", Message = "ShiftedTwoBitDecimal maskHex must be a byte hex value." });
                }
                else if (!invalidShift)
                {
                    var expectedMask = 0x03 << field.Shift.GetValueOrDefault();
                    AddIf(mask != expectedMask, issues, "Error", "UnexpectedShiftedTwoBitMask", path + ".maskHex", $"expected 0x{expectedMask:X2}, got {NormalizeHex(field.MaskHex)}.");
                }
            }
        }

        for (var i = 0; i < seed.Tables.Count; i++)
        {
            var table = seed.Tables[i];
            var path = "$.tables[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            AddIf(string.IsNullOrWhiteSpace(table.TableId), issues, "Error", "MissingTableId", path + ".tableId", "tableId is required.");
            AddIf(table.EntryByteLength <= 0, issues, "Error", "InvalidEntryByteLength", path + ".entryByteLength", "entryByteLength must be greater than zero.");
            if (table.ValueKind.Equals("FixedGbkText", StringComparison.OrdinalIgnoreCase))
            {
                AddIf(table.TextByteLength <= 0, issues, "Error", "InvalidTextByteLength", path + ".textByteLength", "textByteLength must be greater than zero for FixedGbkText tables.");
                AddIf(table.EntryByteLength != table.TextByteLength, issues, "Error", "TextLengthMismatch", path + ".entryByteLength", "FixedGbkText entryByteLength must match textByteLength.");
                AddIf(table.SlotStride > 0 && table.SlotStride < table.TextByteLength, issues, "Error", "InvalidSlotStride", path + ".slotStride", "slotStride must be greater than or equal to textByteLength.");
            }

            if (table.ValueKind.Equals("HexByte", StringComparison.OrdinalIgnoreCase))
            {
                AddIf(table.EntryByteLength != 1, issues, "Error", "InvalidHexByteLength", path + ".entryByteLength", "HexByte tables must use 1-byte entries.");
            }

            AddIf(table.ExpectedEntryCount > 0 && table.Entries.Count != table.ExpectedEntryCount, issues, "Error", "UnexpectedEntryCount", path + ".entries", $"expected {table.ExpectedEntryCount} entries, got {table.Entries.Count}.");
            if (!TryParseHex(table.BaseUeOffsetHex, out var baseOffset))
            {
                issues.Add(new CmfManualSeedValidationIssue { Severity = "Error", Code = "InvalidBaseOffset", Path = path + ".baseUeOffsetHex", Message = "baseUeOffsetHex is not a valid hex integer." });
                continue;
            }

            for (var j = 0; j < table.Entries.Count; j++)
            {
                var entry = table.Entries[j];
                var entryPath = path + ".entries[" + j.ToString(CultureInfo.InvariantCulture) + "]";
                if (!TryParseHex(entry.UeOffsetHex, out var entryOffset))
                {
                    issues.Add(new CmfManualSeedValidationIssue { Severity = "Error", Code = "InvalidTableOffset", Path = entryPath + ".ueOffsetHex", Message = "entry ueOffsetHex is not a valid hex integer." });
                    continue;
                }

                var expected = baseOffset + entry.EntryId;
                if (entryOffset != expected)
                {
                    issues.Add(new CmfManualSeedValidationIssue
                    {
                        Severity = "Error",
                        Code = "UnexpectedTableOffset",
                        Path = entryPath + ".ueOffsetHex",
                        Message = $"expected 0x{expected.ToString("X", CultureInfo.InvariantCulture)}, got {NormalizeHex(entry.UeOffsetHex)}."
                    });
                }

                AddDuplicateOffset(offsets, entryOffset, entryPath, issues);
                if (table.ValueKind.Equals("HexByte", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(entry.DefaultValueRaw))
                {
                    if (!TryParseHex(entry.DefaultValueRaw, out var parsedDefault) || parsedDefault < 0 || parsedDefault > 0xFF)
                    {
                        issues.Add(new CmfManualSeedValidationIssue
                        {
                            Severity = "Error",
                            Code = "InvalidHexByteDefault",
                            Path = entryPath + ".defaultValueRaw",
                            Message = "HexByte defaultValueRaw must be in 0x00..0xFF."
                        });
                    }
                }
            }
        }
    }

    private static void AddIf(bool condition, List<CmfManualSeedValidationIssue> issues, string severity, string code, string path, string message)
    {
        if (!condition) return;
        issues.Add(new CmfManualSeedValidationIssue { Severity = severity, Code = code, Path = path, Message = message });
    }

    private static void AddDuplicateOffset(Dictionary<long, string> offsets, long offset, string path, List<CmfManualSeedValidationIssue> issues)
    {
        if (offsets.TryGetValue(offset, out var existing))
        {
            issues.Add(new CmfManualSeedValidationIssue
            {
                Severity = "Error",
                Code = "DuplicateOffset",
                Path = path,
                Message = $"offset 0x{offset.ToString("X", CultureInfo.InvariantCulture)} duplicates {existing}."
            });
            return;
        }

        offsets[offset] = path;
    }

    private static IReadOnlyDictionary<string, string> BuildSourceProperties(CmfManualSeedDocument seed, IEnumerable<string> notes, bool lengthIsManualDefault)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceType"] = ExtractionMode,
            ["trustLevel"] = TrustLevel,
            ["versionScope"] = seed.VersionScope,
            ["expectedSourceSha256"] = seed.SourceSha256,
            ["expectedTargetSha256"] = seed.TargetSha256,
            ["evidenceDate"] = seed.EvidenceDate,
            ["evidenceSource"] = seed.EvidenceSource,
            ["notes"] = string.Join(" | ", notes.Where(note => !string.IsNullOrWhiteSpace(note)))
        };
        if (lengthIsManualDefault)
        {
            result["lengthStatus"] = "人工默认长度，待复读确认";
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildFieldSourceProperties(CmfManualSeedDocument seed, CmfManualSeedField field)
    {
        var result = new Dictionary<string, string>(BuildSourceProperties(seed, field.Notes, field.LengthIsManualDefault), StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(field.ValueKind))
        {
            result["valueKind"] = field.ValueKind;
        }

        if (!string.IsNullOrWhiteSpace(field.DisplayFormat))
        {
            result["displayFormat"] = field.DisplayFormat;
        }

        if (field.Shift != null)
        {
            result["shift"] = field.Shift.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(field.MaskHex))
        {
            result["maskHex"] = NormalizeHex(field.MaskHex);
        }

        return result;
    }

    private static CmfDesignerBinding MergeCompatibleBinding(CmfDesignerBinding existing, CmfDesignerBinding manual)
    {
        var properties = new Dictionary<string, string>(existing.SourceProperties, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in manual.SourceProperties)
        {
            properties["manualSeed." + pair.Key] = pair.Value;
        }

        properties["sourceType"] = string.IsNullOrWhiteSpace(existing.SourceProperties.GetValueOrDefault("sourceType"))
            ? ExtractionMode
            : existing.SourceProperties.GetValueOrDefault("sourceType") + "+" + ExtractionMode;
        properties["trustLevel"] = TrustLevel;

        return new CmfDesignerBinding
        {
            BindingId = existing.BindingId,
            PageId = existing.PageId,
            ModuleId = existing.ModuleId,
            ControlId = existing.ControlId,
            ControlName = existing.ControlName,
            ControlType = existing.ControlType,
            DisplayName = existing.DisplayName,
            TargetFile = existing.TargetFile,
            AddressKind = existing.AddressKind,
            UeOffsetHex = existing.UeOffsetHex,
            UeOffset = existing.UeOffset,
            OdVirtualAddressHex = existing.OdVirtualAddressHex,
            OdVirtualAddress = existing.OdVirtualAddress,
            ByteLength = existing.ByteLength,
            DataType = existing.DataType,
            FunctionType = existing.FunctionType,
            DefaultValueRaw = string.IsNullOrWhiteSpace(existing.DefaultValueRaw) ? manual.DefaultValueRaw : existing.DefaultValueRaw,
            DefaultValueParsed = string.IsNullOrWhiteSpace(existing.DefaultValueParsed) ? manual.DefaultValueParsed : existing.DefaultValueParsed,
            DataListRaw = string.IsNullOrWhiteSpace(existing.DataListRaw) ? manual.DataListRaw : existing.DataListRaw,
            Script = existing.Script,
            ValidationStatus = existing.ValidationStatus.Equals("NeedsManualReview", StringComparison.OrdinalIgnoreCase)
                ? existing.ValidationStatus
                : TrustLevel,
            SourceProperties = properties
        };
    }

    private static CmfDesignerBinding CloneBindingWithStatus(CmfDesignerBinding binding, string status, string suffix)
        => new()
        {
            BindingId = binding.BindingId + "-conflict",
            PageId = binding.PageId,
            ModuleId = binding.ModuleId,
            ControlId = binding.ControlId,
            ControlName = binding.ControlName,
            ControlType = binding.ControlType,
            DisplayName = string.IsNullOrWhiteSpace(suffix) ? binding.DisplayName : binding.DisplayName + "（" + suffix + "）",
            TargetFile = binding.TargetFile,
            AddressKind = binding.AddressKind,
            UeOffsetHex = binding.UeOffsetHex,
            UeOffset = binding.UeOffset,
            OdVirtualAddressHex = binding.OdVirtualAddressHex,
            OdVirtualAddress = binding.OdVirtualAddress,
            ByteLength = binding.ByteLength,
            DataType = binding.DataType,
            FunctionType = binding.FunctionType,
            DefaultValueRaw = binding.DefaultValueRaw,
            DefaultValueParsed = binding.DefaultValueParsed,
            DataListRaw = binding.DataListRaw,
            Script = binding.Script,
            ValidationStatus = status,
            SourceProperties = binding.SourceProperties
        };

    private static bool SameAddressBinding(CmfDesignerBinding left, CmfDesignerBinding right)
        => left.TargetFile.Equals(right.TargetFile, StringComparison.OrdinalIgnoreCase) &&
           NormalizeHex(left.UeOffsetHex).Equals(NormalizeHex(right.UeOffsetHex), StringComparison.OrdinalIgnoreCase) &&
           !string.IsNullOrWhiteSpace(left.UeOffsetHex);

    private static bool IsCompatibleBinding(CmfDesignerBinding left, CmfDesignerBinding right)
        => left.ByteLength == right.ByteLength &&
           left.DataType.Equals(right.DataType, StringComparison.OrdinalIgnoreCase);

    private static string BuildCheckBoxDataList(CmfManualSeedField field)
    {
        if (string.IsNullOrWhiteSpace(field.CheckedBytesHex) || string.IsNullOrWhiteSpace(field.UncheckedBytesHex))
        {
            return string.Empty;
        }

        return NormalizeHexByteText(field.UncheckedBytesHex) + "-取消\r\n" +
               NormalizeHexByteText(field.CheckedBytesHex) + "-启用";
    }

    private static string NormalizeDefaultValue(string value, int byteLength)
    {
        if (string.IsNullOrWhiteSpace(value) || byteLength <= 0) return string.Empty;
        if (!TryParseHex(value, out var parsed)) return string.Empty;
        return "0x" + parsed.ToString("X" + Math.Max(2, byteLength * 2).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    private static string NormalizeHexByteText(string value)
    {
        var normalized = NormalizeHex(value);
        return normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? normalized[2..]
            : normalized;
    }

    private static string ResolveModuleId(Dictionary<string, string> moduleLookup, string module)
        => moduleLookup.TryGetValue(module, out var moduleId) ? moduleId : "star65-manual-module-misc";

    private static string NormalizeControlType(string value)
    {
        if (value.Equals("NumericBox", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("TextBox", StringComparison.OrdinalIgnoreCase))
        {
            return "NumericBox";
        }

        if (value.Equals("CheckBox", StringComparison.OrdinalIgnoreCase))
        {
            return "CheckBox";
        }

        return string.IsNullOrWhiteSpace(value) ? "Control" : value;
    }

    private static string StableToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "empty";
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }

        var token = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(token) ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant() : token;
    }

    private static bool MatchesRelativePath(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual)) return false;
        var expectedNorm = NormalizeRelativePath(expected);
        var actualNorm = NormalizeRelativePath(actual);
        return expectedNorm.Equals(actualNorm, StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(expectedNorm).Equals(Path.GetFileName(actualNorm), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string value)
        => value.Replace('\\', '/').TrimStart('/');

    private static string NormalizeHex(string value)
    {
        if (!TryParseHex(value, out var parsed)) return value;
        return "0x" + parsed.ToString("X", CultureInfo.InvariantCulture);
    }

    private static bool TryParseHex(string value, out long parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        return value.Length > 0 &&
               value.All(Uri.IsHexDigit) &&
               long.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static IEnumerable<string> ResolveSeedFiles(CczProject? project)
    {
        var directories = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "CmfManualSeeds"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "CCZModStudio", "Assets", "CmfManualSeeds")),
            project == null ? string.Empty : Path.Combine(project.WorkspaceRoot, "工具整合包", "CCZModStudio", "Assets", "CmfManualSeeds"),
            Path.Combine(Environment.CurrentDirectory, "工具整合包", "CCZModStudio", "Assets", "CmfManualSeeds")
        };

        return directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
