using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class CmfKnowledgeExtractor
{
    private static readonly Regex HexAddressRegex = new(@"(?<![0-9A-Fa-f])(?:00)?[4-5][0-9A-Fa-f]{5}(?![0-9A-Fa-f])", RegexOptions.Compiled);
    private static readonly Regex ExplicitHexAddressRegex = new(@"(?<![0-9A-Fa-f])(?:0x|&H|＄|\$)?([0-9A-Fa-f]{5,8})(?![0-9A-Fa-f])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyList<(string Keyword, string Category, string Subsystem)> FeatureKeywords =
    [
        ("特效名", "EffectName", "EffectPackageService"),
        ("特效名字", "EffectName", "EffectPackageService"),
        ("特效介绍", "EffectDescription", "EffectPackageService"),
        ("imsg", "EffectDescription", "EffectPackageService"),
        ("特效", "Effect", "EffectPackageService"),
        ("引擎", "EngineExe", "ExePatchCatalogService"),
        ("exe", "EngineExe", "ExePatchCatalogService"),
        ("等级", "GlobalSetting", "GlobalSettingsService"),
        ("经验", "GlobalSetting", "GlobalSettingsService"),
        ("转职", "GlobalSetting", "GlobalSettingsService"),
        ("功勋", "GlobalSetting", "GlobalSettingsService"),
        ("能力", "GlobalSetting", "GlobalSettingsService"),
        ("变量", "GlobalVariable", "ScriptVariableUsageService"),
        ("剧本", "ScriptEffect", "ScenarioServices"),
        ("Item.e5", "ResourceFile", "ImageResourceServices"),
        ("Data.e5", "DataTable", "HexTableReader"),
        ("Ekd5", "EngineExe", "ExePatchCatalogService")
    ];

    public CmfToolProject Extract(string path, string? rootPath = null, string extractionMode = "StaticSegmentAnalysis")
    {
        var probe = new CheatMakerCmfProbe().Probe(path, null, "CMF authoritative tool-source extraction", rootPath);
        if (!File.Exists(path))
        {
            return new CmfToolProject
            {
                SourcePath = path,
                RelativePath = probe.RelativePath,
                FileName = Path.GetFileName(path),
                IsCheatMakerCmf = false,
                Warnings = probe.Warnings
            };
        }

        var bytes = File.ReadAllBytes(path);
        var segments = AnalyzeSegments(bytes).ToList();
        var visibleText = DecodeSearchText(bytes);
        var inferredPages = BuildPages(probe.RelativePath, Path.GetFileName(path), segments, visibleText).ToList();
        var addresses = ExtractAddressEntries(visibleText, probe.RelativePath).ToList();
        var controls = BuildControls(inferredPages).ToList();
        var bindings = BuildBindings(inferredPages, controls, addresses).ToList();
        var features = BuildFeatureCandidates(probe.RelativePath, Path.GetFileName(path), inferredPages, bindings, visibleText).ToList();

        var warnings = new List<string>(probe.Warnings);
        if (features.All(feature => feature.TrustLevel == CmfTrustLevel.StaticSegmentOnly))
        {
            warnings.Add("CMF was analyzed as an authoritative old-tool source, but only static segments were available; use CheatMaker export/UI enumeration to promote candidates to field-level rules.");
        }

        return new CmfToolProject
        {
            SourcePath = probe.Path,
            RelativePath = probe.RelativePath,
            FileName = Path.GetFileName(probe.Path),
            Sha256 = probe.Sha256,
            Length = probe.Length,
            FormatSignature = probe.FormatSignature,
            FormatVersion = probe.FormatVersion,
            IsCheatMakerCmf = probe.IsCheatMakerCmf,
            ExtractionMode = extractionMode,
            Segments = segments,
            Pages = inferredPages,
            Controls = controls,
            DataBindings = bindings,
            AddressEntries = addresses,
            FeatureCandidates = features,
            Warnings = warnings
        };
    }

    public IReadOnlyList<CmfToolProject> ExtractCorpus(string oldToolsRoot)
    {
        if (string.IsNullOrWhiteSpace(oldToolsRoot) || !Directory.Exists(oldToolsRoot))
        {
            return Array.Empty<CmfToolProject>();
        }

        return Directory.EnumerateFiles(oldToolsRoot, "*.cmf", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .Select(path => Extract(path, oldToolsRoot))
            .ToList();
    }

    public CmfToolProject ImportCheatMakerExport(string cmfPath, string exportPath, string? rootPath = null)
    {
        var project = Extract(cmfPath, rootPath, "CheatMakerExportImport");
        if (!File.Exists(exportPath))
        {
            return CloneProjectWithExport(
                project,
                Array.Empty<CmfExportFieldRecord>(),
                Array.Empty<CmfDataBinding>(),
                project.FeatureCandidates,
                ["CheatMaker export file was not found: " + exportPath]);
        }

        var exportText = ReadLooseText(exportPath);
        var fields = ParseExportFields(exportText, project.RelativePath, project.FileName).ToList();
        var exportBindings = fields.Select(field => new CmfDataBinding
        {
            BindingId = field.FieldId,
            PageId = project.Pages.FirstOrDefault()?.PageId ?? string.Empty,
            ControlId = project.Controls.FirstOrDefault()?.ControlId ?? string.Empty,
            Name = field.Name,
            TargetFile = field.TargetFile,
            DataType = field.DataType,
            ByteLength = field.ByteLength,
            AddressSemantic = field.AddressSemantic,
            Address = field.Address,
            AddressText = field.AddressText,
            TrustLevel = CmfTrustLevel.ExtractedFromCheatMakerExport,
            ConversionStatus = field.AddressSemantic == CmfAddressSemantic.ExeImageAddress
                ? "NeedsPeMappingAndReread"
                : field.AddressSemantic == CmfAddressSemantic.FileOffset || field.AddressSemantic == CmfAddressSemantic.ResourceFile
                    ? "NeedsBoundsAndReread"
                    : "NeedsAddressClassification",
            SourceNote = "Field imported from a normal CheatMaker export/data-list text file."
        }).ToList();

        var promotedFeatures = PromoteFeaturesWithExport(project, fields, exportBindings).ToList();
        var warnings = new List<string>(project.Warnings);
        warnings.Add(fields.Count == 0
            ? "CheatMaker export import found no address-like field records; keep static CMF candidates only."
            : $"CheatMaker export import found {fields.Count} field record(s). These are stronger than static segment evidence but still require version, mapping, bounds, and reread validation before writes.");

        return CloneProjectWithExport(project, fields, exportBindings, promotedFeatures, warnings);
    }

    public CmfPromotionDraft PromoteFeature(CmfToolProject project, string featureId)
    {
        var feature = project.FeatureCandidates.FirstOrDefault(candidate => candidate.FeatureId.Equals(featureId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("CMF feature candidate was not found: " + featureId);

        var relatedBindings = feature.RelatedBindings
            .Select(id => project.DataBindings.FirstOrDefault(binding => binding.BindingId.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(binding => binding != null)
            .Cast<CmfDataBinding>()
            .ToList();

        var blockers = new List<string>();
        if (feature.TrustLevel == CmfTrustLevel.StaticSegmentOnly)
        {
            blockers.Add("Only static CMF segment evidence is available; field-level metadata from CheatMaker export or UI enumeration is still required.");
        }

        if (relatedBindings.Count == 0)
        {
            blockers.Add("No concrete data binding was extracted for this feature.");
        }

        if (relatedBindings.Any(binding => binding.AddressSemantic is CmfAddressSemantic.Unknown or CmfAddressSemantic.DynamicPointer))
        {
            blockers.Add("At least one extracted address is unknown or dynamic and cannot be converted to an offline write rule.");
        }

        if (relatedBindings.Any(binding => binding.AddressSemantic == CmfAddressSemantic.RuntimeStaticMemory))
        {
            blockers.Add("At least one extracted address is runtime memory; it must be mapped to a persistent file rule or kept in the runtime-variable catalog.");
        }

        if (relatedBindings.Any(binding => binding.ByteLength <= 0))
        {
            blockers.Add("Byte length/type metadata is missing.");
        }

        if (!feature.ConversionStatus.Equals("VerifiedWritable", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("Feature has not passed version match, target-file bounds check, test-copy write, and reread validation.");
        }

        return new CmfPromotionDraft
        {
            FeatureId = feature.FeatureId,
            Name = feature.Name,
            TargetSubsystem = feature.TargetSubsystem,
            RuleKind = feature.Category switch
            {
                "EffectName" or "EffectDescription" or "Effect" => "CmfDerivedEffectRuleDraft",
                "EngineExe" or "GlobalSetting" => "CmfDerivedExeOrGlobalSettingRuleDraft",
                "GlobalVariable" => "CmfDerivedGlobalVariableRuleDraft",
                _ => "CmfDerivedCapabilityRuleDraft"
            },
            CanWriteNow = blockers.Count == 0 && feature.ConversionStatus.Equals("VerifiedWritable", StringComparison.OrdinalIgnoreCase),
            RequiredValidation = "Require version match, address classification, PE/file mapping when needed, target-file bounds check, test-copy write, and reread validation.",
            BlockingReasons = blockers,
            SourceFeature = feature
        };
    }

    private static CmfToolProject CloneProjectWithExport(
        CmfToolProject project,
        IReadOnlyList<CmfExportFieldRecord> exportFields,
        IReadOnlyList<CmfDataBinding> exportBindings,
        IReadOnlyList<CmfFeatureCandidate> features,
        IReadOnlyList<string> warnings)
        => new()
        {
            SourcePath = project.SourcePath,
            RelativePath = project.RelativePath,
            FileName = project.FileName,
            Sha256 = project.Sha256,
            Length = project.Length,
            FormatSignature = project.FormatSignature,
            FormatVersion = project.FormatVersion,
            IsCheatMakerCmf = project.IsCheatMakerCmf,
            AuthoritativeToolSource = project.AuthoritativeToolSource,
            ExtractionMode = exportFields.Count == 0 ? project.ExtractionMode : "CheatMakerExportImport",
            ConversionPolicy = project.ConversionPolicy,
            Segments = project.Segments,
            Pages = project.Pages,
            Controls = project.Controls,
            DataBindings = project.DataBindings.Concat(exportBindings).ToArray(),
            AddressEntries = project.AddressEntries,
            ExportFields = exportFields,
            FeatureCandidates = features,
            Warnings = warnings
        };

    private static IEnumerable<CmfFeatureCandidate> PromoteFeaturesWithExport(
        CmfToolProject project,
        IReadOnlyList<CmfExportFieldRecord> fields,
        IReadOnlyList<CmfDataBinding> exportBindings)
    {
        var candidates = project.FeatureCandidates.Select(feature =>
        {
            var relatedBindings = feature.RelatedBindings.Concat(exportBindings.Select(binding => binding.BindingId)).Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToArray();
            return new CmfFeatureCandidate
            {
                FeatureId = feature.FeatureId,
                Name = feature.Name,
                Category = feature.Category,
                VersionScope = feature.VersionScope,
                SourceCmfRelativePath = feature.SourceCmfRelativePath,
                SourcePageId = feature.SourcePageId,
                TargetSubsystem = feature.TargetSubsystem,
                TrustLevel = fields.Count == 0 ? feature.TrustLevel : CmfTrustLevel.ExtractedFromCheatMakerExport,
                ConversionStatus = fields.Count == 0 ? feature.ConversionStatus : "ExportFieldMetadataImported",
                WritePolicy = fields.Count == 0
                    ? feature.WritePolicy
                    : "CheatMaker export field metadata is imported. Writes still require version match, address classification, PE/file mapping, bounds check, test-copy write, and reread validation.",
                EvidenceNotes = feature.EvidenceNotes.Concat(fields.Take(12).Select(field => $"Export field: {field.Name} {field.AddressText} {field.DataType}/{field.ByteLength}B")).ToArray(),
                RelatedBindings = relatedBindings
            };
        }).ToList();

        if (candidates.Count == 0 && fields.Count > 0)
        {
            yield return new CmfFeatureCandidate
            {
                FeatureId = BuildStableId(project.RelativePath, "feature", "CheatMakerExportFields"),
                Name = "CheatMaker 导出字段清单",
                Category = "ExportFieldList",
                VersionScope = InferVersionScope(project.FileName),
                SourceCmfRelativePath = project.RelativePath,
                SourcePageId = project.Pages.FirstOrDefault()?.PageId ?? string.Empty,
                TargetSubsystem = "CmfDerivedCapabilityService",
                TrustLevel = CmfTrustLevel.ExtractedFromCheatMakerExport,
                ConversionStatus = "ExportFieldMetadataImported",
                WritePolicy = "CheatMaker export field metadata is imported. Writes still require version match, address classification, PE/file mapping, bounds check, test-copy write, and reread validation.",
                EvidenceNotes = fields.Take(12).Select(field => $"Export field: {field.Name} {field.AddressText} {field.DataType}/{field.ByteLength}B").ToArray(),
                RelatedBindings = exportBindings.Select(binding => binding.BindingId).Take(64).ToArray()
            };
            yield break;
        }

        foreach (var candidate in candidates)
        {
            yield return candidate;
        }
    }

    private static IEnumerable<CmfExportFieldRecord> ParseExportFields(string text, string relativePath, string fileName)
    {
        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries);
        var index = 0;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            var addressMatch = ExplicitHexAddressRegex.Matches(line)
                .Cast<Match>()
                .Select(match => match.Groups[1].Value)
                .FirstOrDefault(value => TryParseAddress(value, out var address) && address >= 0x1000);
            if (string.IsNullOrWhiteSpace(addressMatch)) continue;
            if (!TryParseAddress(addressMatch, out var parsedAddress)) continue;

            var tokens = SplitExportLine(line).ToList();
            var name = tokens.FirstOrDefault(token => !LooksLikeAddressToken(token) && !LooksLikeTypeToken(token) && !LooksLikeLengthToken(token))
                       ?? "CMF export field " + index.ToString(CultureInfo.InvariantCulture);
            var dataType = tokens.FirstOrDefault(LooksLikeTypeToken) ?? InferDataTypeFromLine(line);
            var byteLength = InferByteLengthFromLine(line, dataType);
            var semantic = ClassifyExportAddress(parsedAddress, line);
            yield return new CmfExportFieldRecord
            {
                FieldId = BuildStableId(relativePath, "export", index.ToString(CultureInfo.InvariantCulture) + "|" + line),
                Name = name,
                PageName = InferCategoryFromName(fileName, text),
                Category = InferFieldCategory(name + " " + line),
                TargetFile = InferTargetFile(semantic, line),
                DataType = dataType,
                ByteLength = byteLength,
                AddressSemantic = semantic,
                Address = parsedAddress,
                AddressText = "0x" + parsedAddress.ToString("X", CultureInfo.InvariantCulture),
                VersionScope = InferVersionScope(fileName),
                SourceLine = line
            };
            index++;
        }
    }

    private static IReadOnlyList<string> SplitExportLine(string line)
        => line.Split(['\t', ',', ';', '|', '，', '；', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryParseAddress(string token, out long address)
    {
        var cleaned = token.Trim()
            .TrimStart('$')
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("&H", string.Empty, StringComparison.OrdinalIgnoreCase);
        return long.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }

    private static bool LooksLikeAddressToken(string token)
        => ExplicitHexAddressRegex.IsMatch(token);

    private static bool LooksLikeTypeToken(string token)
    {
        var normalized = token.Trim().ToLowerInvariant();
        return normalized is "byte" or "u8" or "uint8" or "char" or "word" or "short" or "u16" or "uint16" or "dword" or "int" or "u32" or "uint32" or "float" or "string" or "text" or "gbk" or "ansi" ||
               normalized.Contains("字节", StringComparison.Ordinal) ||
               normalized.Contains("整数", StringComparison.Ordinal) ||
               normalized.Contains("文本", StringComparison.Ordinal);
    }

    private static bool LooksLikeLengthToken(string token)
    {
        var normalized = token.Trim().ToLowerInvariant();
        return normalized.EndsWith("b", StringComparison.Ordinal) && int.TryParse(normalized.TrimEnd('b'), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static string InferDataTypeFromLine(string line)
    {
        var lower = line.ToLowerInvariant();
        if (lower.Contains("string", StringComparison.Ordinal) || lower.Contains("text", StringComparison.Ordinal) || line.Contains("文本", StringComparison.Ordinal)) return "String";
        if (lower.Contains("dword", StringComparison.Ordinal) || lower.Contains("u32", StringComparison.Ordinal) || lower.Contains("uint32", StringComparison.Ordinal)) return "UInt32";
        if (lower.Contains("word", StringComparison.Ordinal) || lower.Contains("short", StringComparison.Ordinal) || lower.Contains("u16", StringComparison.Ordinal) || lower.Contains("uint16", StringComparison.Ordinal)) return "UInt16";
        if (lower.Contains("float", StringComparison.Ordinal)) return "Single";
        if (lower.Contains("byte", StringComparison.Ordinal) || lower.Contains("u8", StringComparison.Ordinal) || lower.Contains("uint8", StringComparison.Ordinal)) return "Byte";
        return "Unknown";
    }

    private static int InferByteLengthFromLine(string line, string dataType)
    {
        foreach (Match match in Regex.Matches(line, @"(?<!\d)(\d{1,3})\s*(?:B|byte|bytes|字节)(?!\w)", RegexOptions.IgnoreCase))
        {
            if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) && length > 0)
            {
                return length;
            }
        }

        return dataType switch
        {
            "Byte" => 1,
            "UInt16" => 2,
            "UInt32" or "Single" => 4,
            _ => 0
        };
    }

    private static CmfAddressSemantic ClassifyExportAddress(long address, string line)
    {
        var lower = line.ToLowerInvariant();
        if (lower.Contains("file", StringComparison.Ordinal) || line.Contains("文件", StringComparison.Ordinal) || line.Contains("偏移", StringComparison.Ordinal)) return CmfAddressSemantic.FileOffset;
        if (lower.Contains("imsg", StringComparison.Ordinal) || lower.Contains(".e5", StringComparison.Ordinal) || lower.Contains("ekd5.exe", StringComparison.Ordinal)) return CmfAddressSemantic.ResourceFile;
        return ClassifyAddress(address);
    }

    private static string InferTargetFile(CmfAddressSemantic semantic, string line)
    {
        var lower = line.ToLowerInvariant();
        if (lower.Contains("ekd5", StringComparison.Ordinal) || semantic == CmfAddressSemantic.ExeImageAddress) return "Ekd5.exe";
        foreach (var target in new[] { "Data.e5", "Imsg.e5", "Item.e5", "Mtem.e5", "DT.e5", "Fb.e5", "U_select.e5", "Pmap.e5" })
        {
            if (lower.Contains(target.ToLowerInvariant(), StringComparison.Ordinal)) return target;
        }

        return semantic == CmfAddressSemantic.FileOffset ? "UnknownFile" : string.Empty;
    }

    private static string InferFieldCategory(string text)
    {
        foreach (var (keyword, category, _) in FeatureKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        return "ExportField";
    }

    private static string ReadLooseText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936).GetString(bytes);
        }
    }

    private static IEnumerable<CmfSegmentAnalysis> AnalyzeSegments(byte[] bytes)
    {
        var offsets = FindUtf16CrlfOffsets(bytes).ToList();
        var starts = new List<int> { 0 };
        starts.AddRange(offsets.Select(offset => checked((int)offset + 4)).Where(offset => offset < bytes.Length));
        var ends = offsets.Select(offset => checked((int)offset)).ToList();
        ends.Add(bytes.Length);

        for (var i = 0; i < starts.Count && i < ends.Count; i++)
        {
            var start = starts[i];
            var end = Math.Max(start, ends[i]);
            var length = end - start;
            var segmentBytes = new byte[length];
            if (length > 0) Buffer.BlockCopy(bytes, start, segmentBytes, 0, length);
            var text = DecodeSegmentText(segmentBytes);
            yield return new CmfSegmentAnalysis
            {
                Index = i,
                ByteOffset = start,
                ByteLength = length,
                CharLength = text.Length,
                PrintableAsciiRatio = ComputePrintableAsciiRatio(text),
                CjkRatio = ComputeCjkRatio(text),
                ByteEntropy = Math.Round(ComputeEntropy(segmentBytes), 4),
                KeywordHits = FindFeatureKeywords(text),
                SuspectedKind = ClassifySegment(i, text, segmentBytes),
                Preview = BuildPreview(text)
            };
        }
    }

    private static IEnumerable<CmfPageDefinition> BuildPages(string relativePath, string fileName, IReadOnlyList<CmfSegmentAnalysis> segments, string visibleText)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var primaryCategory = InferCategoryFromName(fileName, visibleText);
        yield return new CmfPageDefinition
        {
            PageId = BuildStableId(relativePath, "page", "main"),
            Name = string.IsNullOrWhiteSpace(primaryCategory) ? baseName : primaryCategory,
            SegmentIndex = segments.Count > 1 ? 1 : 0,
            TrustLevel = CmfTrustLevel.StaticSegmentOnly,
            SourceNote = "Inferred from CMF filename and static segment profile."
        };

        foreach (var segment in segments.Where(segment => segment.Index > 0 && segment.KeywordHits.Count > 0).Take(12))
        {
            yield return new CmfPageDefinition
            {
                PageId = BuildStableId(relativePath, "segment", segment.Index.ToString(CultureInfo.InvariantCulture)),
                Name = segment.KeywordHits.First() + " candidate segment " + segment.Index.ToString(CultureInfo.InvariantCulture),
                SegmentIndex = segment.Index,
                TrustLevel = CmfTrustLevel.StaticSegmentOnly,
                SourceNote = $"Static segment keyword hits: {string.Join(", ", segment.KeywordHits)}."
            };
        }
    }

    private static IEnumerable<CmfControlDefinition> BuildControls(IReadOnlyList<CmfPageDefinition> pages)
    {
        foreach (var page in pages)
        {
            yield return new CmfControlDefinition
            {
                ControlId = page.PageId + ":static-page",
                PageId = page.PageId,
                Name = page.Name,
                ControlKind = "InferredPage",
                TrustLevel = page.TrustLevel,
                SourceNote = page.SourceNote
            };
        }
    }

    private static IEnumerable<CmfDataBinding> BuildBindings(
        IReadOnlyList<CmfPageDefinition> pages,
        IReadOnlyList<CmfControlDefinition> controls,
        IReadOnlyList<CmfAddressEntry> addresses)
    {
        foreach (var address in addresses.Take(128))
        {
            var page = pages.FirstOrDefault() ?? new CmfPageDefinition { PageId = "unknown", Name = "Unknown" };
            var control = controls.FirstOrDefault(control => control.PageId.Equals(page.PageId, StringComparison.OrdinalIgnoreCase));
            yield return new CmfDataBinding
            {
                BindingId = address.EntryId,
                PageId = page.PageId,
                ControlId = control?.ControlId ?? string.Empty,
                Name = address.SourceText,
                TargetFile = address.TargetFile,
                DataType = "Unknown",
                ByteLength = address.ByteLength ?? 0,
                AddressSemantic = address.Semantic,
                Address = address.Address,
                AddressText = address.AddressText,
                TrustLevel = CmfTrustLevel.StaticSegmentOnly,
                ConversionStatus = address.Semantic == CmfAddressSemantic.ExeImageAddress ? "NeedsPeMappingAndReread" : "ReadOnlyCandidate",
                SourceNote = "Address-like token extracted from CMF static text. Confirm through CheatMaker export/UI enumeration before writing."
            };
        }
    }

    private static IEnumerable<CmfFeatureCandidate> BuildFeatureCandidates(
        string relativePath,
        string fileName,
        IReadOnlyList<CmfPageDefinition> pages,
        IReadOnlyList<CmfDataBinding> bindings,
        string visibleText)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var versionScope = InferVersionScope(fileName);
        var candidates = new List<CmfFeatureCandidate>();
        void Add(string name, string category, string subsystem, IEnumerable<string>? notes = null)
        {
            if (candidates.Any(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
            candidates.Add(new CmfFeatureCandidate
            {
                FeatureId = BuildStableId(relativePath, "feature", name),
                Name = name,
                Category = category,
                VersionScope = versionScope,
                SourceCmfRelativePath = relativePath,
                SourcePageId = pages.FirstOrDefault()?.PageId ?? string.Empty,
                TargetSubsystem = subsystem,
                TrustLevel = CmfTrustLevel.StaticSegmentOnly,
                ConversionStatus = bindings.Count > 0 ? "NeedsValidation" : "ReadOnlyCandidate",
                WritePolicy = "Treat CMF as authoritative old-tool knowledge, but require exported/UI field metadata, version match, address classification, and reread validation before writes.",
                EvidenceNotes = (notes ?? Array.Empty<string>()).Append("Source CMF: " + fileName).ToArray(),
                RelatedBindings = bindings.Select(binding => binding.BindingId).Take(16).ToArray()
            });
        }

        var lowerName = fileName.ToLowerInvariant();
        if (lowerName.Contains("修改") && lowerName.Contains("特效") && lowerName.Contains("名"))
        {
            Add("特效名修改", "EffectName", "EffectPackageService", ["Filename indicates a mature CheatMaker effect-name modifier."]);
        }

        if (lowerName.Contains("imsg"))
        {
            Add("Imsg 特效介绍读取", "EffectDescription", "EffectPackageService", ["Filename indicates effect descriptions are read from Imsg."]);
        }

        if (lowerName.Contains("剧本") && lowerName.Contains("特效") && lowerName.Contains("名字"))
        {
            Add("剧本特效名字读取", "EffectName", "EffectPackageService", ["Filename indicates engine-backed scenario effect names."]);
        }

        if (lowerName.Contains("特效cm") || lowerName.Contains("特效"))
        {
            Add("特效修改器功能覆盖", "Effect", "EffectPackageService", ["CMF belongs to the effect modifier family."]);
        }

        if (lowerName.Contains("引擎") || lowerName.Contains("exe"))
        {
            Add(versionScope + " 引擎 EXE 修改项", "EngineExe", "ExePatchCatalogService", ["CMF is an engine EXE modifier project."]);
            Add(versionScope + " 全局参数候选", "GlobalSetting", "GlobalSettingsService", ["Engine modifier CMF should be used to prioritize pending global numeric settings."]);
        }

        foreach (var (keyword, category, subsystem) in FeatureKeywords)
        {
            if (visibleText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                Add(baseName + " / " + keyword, category, subsystem, ["Keyword was visible in decoded CMF text."]);
            }
        }

        if (candidates.Count == 0)
        {
            Add(baseName, "Unknown", "CmfDerivedCapabilityService", ["No semantic keyword was visible; keep as a CMF-derived capability placeholder."]);
        }

        return candidates;
    }

    private static IEnumerable<CmfAddressEntry> ExtractAddressEntries(string text, string relativePath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in HexAddressRegex.Matches(text))
        {
            var raw = match.Value;
            if (!seen.Add(raw)) continue;
            if (!long.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address)) continue;
            var semantic = ClassifyAddress(address);
            yield return new CmfAddressEntry
            {
                EntryId = BuildStableId(relativePath, "addr", raw),
                SourceText = raw,
                TargetFile = semantic == CmfAddressSemantic.ExeImageAddress ? "Ekd5.exe" : string.Empty,
                Semantic = semantic,
                Address = address,
                AddressText = "0x" + address.ToString("X", CultureInfo.InvariantCulture),
                ValidationStatus = semantic == CmfAddressSemantic.ExeImageAddress ? "NeedsPeAddressMapper" : "NeedsManualClassification"
            };
        }
    }

    private static CmfAddressSemantic ClassifyAddress(long address)
    {
        if (address is >= 0x00400000 and < 0x00600000)
        {
            return address is >= 0x004A0000 and < 0x00520000
                ? CmfAddressSemantic.RuntimeStaticMemory
                : CmfAddressSemantic.ExeImageAddress;
        }

        return CmfAddressSemantic.Unknown;
    }

    private static string InferCategoryFromName(string fileName, string visibleText)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.Contains("修改") && lower.Contains("特效") && lower.Contains("名")) return "特效名修改";
        if (lower.Contains("imsg")) return "Imsg 特效介绍读取";
        if (lower.Contains("剧本") && lower.Contains("特效") && lower.Contains("名字")) return "剧本特效名字读取";
        if (lower.Contains("特效cm") || lower.Contains("特效")) return "特效修改";
        if (lower.Contains("6.6")) return "6.6X 引擎修改";
        if (lower.Contains("6.5")) return "6.5 引擎修改";
        if (lower.Contains("6.4") || lower.Contains("star175")) return "6.4/star175 引擎修改";
        return visibleText.Contains("Ekd5", StringComparison.OrdinalIgnoreCase) ? "引擎修改" : string.Empty;
    }

    private static string InferVersionScope(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.Contains("6.6")) return "6.6";
        if (lower.Contains("6.5")) return "6.5";
        if (lower.Contains("6.4") || lower.Contains("star175")) return "6.4";
        return "Unknown";
    }

    private static string ClassifySegment(int index, string text, byte[] bytes)
    {
        if (index == 0) return "Header";
        var hits = FindFeatureKeywords(text);
        if (hits.Any(hit => hit.Contains("Ekd5", StringComparison.OrdinalIgnoreCase) || hit.Contains("exe", StringComparison.OrdinalIgnoreCase))) return "EngineModifierCandidate";
        if (hits.Any(hit => hit.Contains("特效", StringComparison.OrdinalIgnoreCase) || hit.Contains("imsg", StringComparison.OrdinalIgnoreCase))) return "EffectModifierCandidate";
        if (HexAddressRegex.IsMatch(text)) return "AddressListCandidate";
        if (ComputeEntropy(bytes) > 6.5) return "ProtectedOrEncodedPayload";
        return "EncodedProjectSegment";
    }

    private static IReadOnlyList<string> FindFeatureKeywords(string text)
        => FeatureKeywords
            .Select(item => item.Keyword)
            .Where(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildStableId(string relativePath, string kind, string value)
    {
        var input = relativePath + "|" + kind + "|" + value;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).Substring(0, 12).ToLowerInvariant();
        return kind + "-" + hash;
    }

    private static string BuildPreview(string text)
    {
        var filtered = new string(text
            .Take(300)
            .Select(ch => IsReasonablyPrintable(ch) ? ch : '.')
            .ToArray());
        return filtered.Trim();
    }

    private static bool IsReasonablyPrintable(char ch)
        => ch is >= ' ' and <= '~' || ch is >= '\u4e00' and <= '\u9fff';

    private static double ComputePrintableAsciiRatio(string text)
    {
        if (text.Length == 0) return 0;
        return Math.Round((double)text.Count(ch => ch is >= ' ' and <= '~') / text.Length, 4);
    }

    private static double ComputeCjkRatio(string text)
    {
        if (text.Length == 0) return 0;
        return Math.Round((double)text.Count(ch => ch is >= '\u4e00' and <= '\u9fff') / text.Length, 4);
    }

    private static double ComputeEntropy(byte[] bytes)
    {
        if (bytes.Length == 0) return 0;
        Span<int> counts = stackalloc int[256];
        foreach (var b in bytes) counts[b]++;
        var entropy = 0d;
        foreach (var count in counts)
        {
            if (count == 0) continue;
            var p = (double)count / bytes.Length;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    private static IReadOnlyList<long> FindUtf16CrlfOffsets(byte[] bytes)
    {
        var offsets = new List<long>();
        for (var i = 0; i + 3 < bytes.Length; i += 2)
        {
            if (bytes[i] == 0x0D &&
                bytes[i + 1] == 0x00 &&
                bytes[i + 2] == 0x0A &&
                bytes[i + 3] == 0x00)
            {
                offsets.Add(i);
            }
        }

        return offsets;
    }

    private static string DecodeSearchText(byte[] bytes)
    {
        var take = Math.Min(bytes.Length, 512 * 1024);
        var unicode = Encoding.Unicode.GetString(bytes, 0, take);
        var ascii = Encoding.ASCII.GetString(bytes, 0, take);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gbk = Encoding.GetEncoding(936).GetString(bytes, 0, take);
        return unicode + "\n" + ascii + "\n" + gbk;
    }

    private static string DecodeSegmentText(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;
        try
        {
            return Encoding.Unicode.GetString(bytes);
        }
        catch
        {
            return Encoding.ASCII.GetString(bytes);
        }
    }
}
