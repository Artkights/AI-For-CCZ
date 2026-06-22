using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public enum EquipmentTypeSourceConfidence
{
    Confirmed,
    ExeNameTable,
    ItemSamples,
    LegacyFallback,
    Unknown
}

public sealed record ProjectEquipmentTypeDefinition(
    int TypeId,
    string DisplayName,
    IReadOnlyList<string> SampleItemNames,
    EquipmentTypeSourceConfidence Source,
    string SourceNote,
    string SourceDetail = "")
{
    public string SampleText => SampleItemNames.Count == 0 ? string.Empty : string.Join("、", SampleItemNames);

    public string ShortDisplayName => string.IsNullOrWhiteSpace(DisplayName)
        ? $"类型码{TypeId}"
        : DisplayName;

    public string SourceDisplayName => ProjectEquipmentTypeProfileService.FormatSource(Source, SourceDetail);

    public string BuildComboText()
        => $"{TypeId}：{ShortDisplayName}";
}

public sealed record JobEquipmentPermissionSlotDefinition(
    int SlotIndex,
    string StorageColumnName,
    int TypeId,
    string DisplayName,
    IReadOnlyList<string> SampleItemNames,
    EquipmentTypeSourceConfidence Source,
    string SourceNote,
    string SourceDetail = "")
{
    public string SampleText => SampleItemNames.Count == 0 ? string.Empty : string.Join("、", SampleItemNames);

    public string SummaryName => string.IsNullOrWhiteSpace(DisplayName)
        ? $"槽{SlotIndex:D2}/类型码{TypeId}"
        : DisplayName;

    public string SourceDisplayName => ProjectEquipmentTypeProfileService.FormatSource(Source, SourceDetail);

    public string PreviewText
    {
        get
        {
            return $"槽{SlotIndex:D2}：{SummaryName}，类型码={TypeId}，来源={SourceDisplayName}";
        }
    }

    public string CheckBoxText
        => $"槽{SlotIndex:D2} {SummaryName}";
}

public sealed record ProjectEquipmentTypeProfile(
    string ProjectName,
    IReadOnlyDictionary<int, ProjectEquipmentTypeDefinition> Types,
    IReadOnlyList<JobEquipmentPermissionSlotDefinition> JobPermissionSlots,
    string NotesPath,
    EquipmentTypeNameTableProbeResult? NameTableProbe,
    IReadOnlyList<string> Diagnostics)
{
    public bool TryGetType(int typeId, out ProjectEquipmentTypeDefinition definition)
        => Types.TryGetValue(typeId, out definition!);

    public ProjectEquipmentTypeDefinition GetTypeOrFallback(int typeId, string majorCategory = "")
    {
        if (Types.TryGetValue(typeId, out var definition)) return definition;
        var name = ItemTypeCatalogService.BuildShortName(typeId, majorCategory);
        return new ProjectEquipmentTypeDefinition(
            typeId,
            name,
            Array.Empty<string>(),
            ItemTypeCatalogService.TryGetEntry(typeId, out _) ? EquipmentTypeSourceConfidence.LegacyFallback : EquipmentTypeSourceConfidence.Unknown,
            "未在当前项目物品样例或 EXE 类型名称表中确认");
    }
}

public sealed class ProjectEquipmentTypeProfileService
{
    public const long ExeTypeNameTableOffset = 0x8AC70;
    public const int ExeTypeNameProbeLength = 0x74;
    public const int JobPermissionSlotCount = 26;
    private const int MaxSampleCount = 4;

    private readonly HexTableReader _reader = new();
    private readonly EquipmentTypeNameTableProbeService _nameTableProbeService = new();

    public ProjectEquipmentTypeProfile Build(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        IReadOnlyList<string>? storageColumns = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tables);

        var diagnostics = new List<string>();
        var samples = BuildItemSamples(project, tables, diagnostics);
        var probe = _nameTableProbeService.ProbeBest(project, BuildSampleNameLookup(samples), diagnostics);
        var exeNames = BuildNamesFromProbe(probe);
        var confirmed = LoadConfirmedNames(project, diagnostics);
        var typeDefinitions = BuildTypeDefinitions(samples, exeNames, probe, confirmed.TypeNames);
        var slots = BuildSlotDefinitions(typeDefinitions, confirmed.SlotNames, storageColumns);
        var notesPath = BuildNotesPath(project);

        diagnostics.Insert(0, $"项目化装备类型：物品类型样例 {samples.Count} 组，名称表 {(probe == null ? "未采用" : probe.SummaryText)}，人工确认 {confirmed.TypeNames.Count + confirmed.SlotNames.Count} 条。");
        return new ProjectEquipmentTypeProfile(project.Name, typeDefinitions, slots, notesPath, probe, diagnostics);
    }

    public static string FormatSource(EquipmentTypeSourceConfidence source)
        => FormatSource(source, string.Empty);

    public static string FormatSource(EquipmentTypeSourceConfidence source, string sourceDetail)
    {
        var baseName = source switch
        {
            EquipmentTypeSourceConfidence.Confirmed => "人工确认",
            EquipmentTypeSourceConfidence.ExeNameTable => "名称表",
            EquipmentTypeSourceConfidence.ItemSamples => "物品样例",
            EquipmentTypeSourceConfidence.LegacyFallback => "旧内置兜底",
            _ => "待确认"
        };

        return string.IsNullOrWhiteSpace(sourceDetail) ? baseName : $"{baseName}({sourceDetail})";
    }

    public static string BuildNotesPath(CczProject project)
    {
        var root = string.IsNullOrWhiteSpace(project.WorkspaceRoot) ? project.GameRoot : project.WorkspaceRoot;
        return Path.Combine(root, "CCZModStudio_Notes", $"{SanitizeFileName(project.Name)}_EquipmentTypeProfile.json");
    }

    private IReadOnlyDictionary<int, ProjectEquipmentTypeDefinition> BuildTypeDefinitions(
        IReadOnlyDictionary<int, IReadOnlyList<ItemTypeSample>> samples,
        IReadOnlyDictionary<int, string> exeNames,
        EquipmentTypeNameTableProbeResult? probe,
        IReadOnlyDictionary<int, string> confirmedTypeNames)
    {
        var typeIds = new SortedSet<int>(samples.Keys);
        typeIds.UnionWith(exeNames.Keys);
        typeIds.UnionWith(confirmedTypeNames.Keys);
        typeIds.UnionWith(Enumerable.Range(0, JobPermissionSlotCount));

        var result = new Dictionary<int, ProjectEquipmentTypeDefinition>();
        foreach (var typeId in typeIds)
        {
            samples.TryGetValue(typeId, out var sampleRows);
            var sampleNames = (sampleRows ?? Array.Empty<ItemTypeSample>())
                .Select(sample => sample.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxSampleCount)
                .ToArray();

            string displayName;
            EquipmentTypeSourceConfidence source;
            string sourceNote;
            string sourceDetail = string.Empty;
            if (confirmedTypeNames.TryGetValue(typeId, out var confirmedName) &&
                !string.IsNullOrWhiteSpace(confirmedName))
            {
                displayName = confirmedName.Trim();
                source = EquipmentTypeSourceConfidence.Confirmed;
                sourceNote = "来自项目 JSON 人工确认";
                sourceDetail = "JSON";
            }
            else if (exeNames.TryGetValue(typeId, out var exeName) &&
                     !string.IsNullOrWhiteSpace(exeName))
            {
                var sampleName = BuildNameFromSamples(sampleNames);
                displayName = BuildExeAndSampleDisplayName(typeId, exeName.Trim(), sampleName);
                source = EquipmentTypeSourceConfidence.ExeNameTable;
                sourceNote = probe == null
                    ? "来自装备类型名称表探测"
                    : $"来自 {probe.LocationText}，{probe.SourceLabel}";
                sourceDetail = probe?.LocationText ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(sampleName) &&
                    !NamesLookCompatible(sampleName, exeName.Trim()))
                {
                    sourceNote += "；名称表与物品样例存在差异，建议人工确认";
                }
            }
            else if (sampleNames.Length > 0)
            {
                displayName = BuildNameFromSamples(sampleNames);
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = string.Join("/", sampleNames.Take(2));
                }
                source = EquipmentTypeSourceConfidence.ItemSamples;
                sourceNote = "按当前 Data.e5/Star.e5 物品样例推断";
            }
            else if (ItemTypeCatalogService.TryGetEntry(typeId, out var legacy))
            {
                displayName = legacy.Name;
                source = EquipmentTypeSourceConfidence.LegacyFallback;
                sourceNote = "旧内置目录兜底，未覆盖当前项目作者自定义命名";
            }
            else
            {
                displayName = $"类型码{typeId}";
                source = EquipmentTypeSourceConfidence.Unknown;
                sourceNote = "未找到当前项目样例或名称表";
            }

            result[typeId] = new ProjectEquipmentTypeDefinition(typeId, CleanDisplayName(displayName), sampleNames, source, sourceNote, sourceDetail);
        }

        return result;
    }

    private static IReadOnlyList<JobEquipmentPermissionSlotDefinition> BuildSlotDefinitions(
        IReadOnlyDictionary<int, ProjectEquipmentTypeDefinition> typeDefinitions,
        IReadOnlyDictionary<int, string> confirmedSlotNames,
        IReadOnlyList<string>? storageColumns)
    {
        var columns = storageColumns?.ToArray() ?? Array.Empty<string>();
        var result = new List<JobEquipmentPermissionSlotDefinition>();
        for (var slot = 0; slot < JobPermissionSlotCount; slot++)
        {
            var storageColumn = slot < columns.Length && !string.IsNullOrWhiteSpace(columns[slot])
                ? columns[slot]
                : $"装备许可{slot:D2}";
            var typeId = slot;
            typeDefinitions.TryGetValue(typeId, out var type);
            var displayName = type?.DisplayName ?? $"槽{slot:D2}/类型码{typeId}（待命名）";
            var source = type?.Source ?? EquipmentTypeSourceConfidence.Unknown;
            var note = type?.SourceNote ?? "未找到当前项目样例或名称表";
            var sourceDetail = type?.SourceDetail ?? string.Empty;
            if (confirmedSlotNames.TryGetValue(slot, out var confirmedSlotName) &&
                !string.IsNullOrWhiteSpace(confirmedSlotName))
            {
                displayName = confirmedSlotName.Trim();
                source = EquipmentTypeSourceConfidence.Confirmed;
                note = "来自项目 JSON 槽位人工确认";
                sourceDetail = "JSON";
            }

            if (source == EquipmentTypeSourceConfidence.Unknown)
            {
                displayName = $"槽{slot:D2}/类型码{typeId}（待命名）";
            }

            result.Add(new JobEquipmentPermissionSlotDefinition(
                slot,
                storageColumn,
                typeId,
                displayName,
                type?.SampleItemNames ?? Array.Empty<string>(),
                source,
                note,
                sourceDetail));
        }

        return result;
    }

    private IReadOnlyDictionary<int, IReadOnlyList<ItemTypeSample>> BuildItemSamples(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        List<string> diagnostics)
    {
        var boundary = ItemCategoryBoundaryService.Resolve(project);
        var result = new Dictionary<int, List<ItemTypeSample>>();
        var itemTables = HexTableNameResolver.ResolveItemTables(project, tables);
        if (itemTables.Count == 0)
        {
            diagnostics.Add("未找到可读取的 6.x 物品表，装备类型 profile 只能使用 EXE/兜底目录。");
            return new Dictionary<int, IReadOnlyList<ItemTypeSample>>();
        }

        foreach (var table in itemTables.OrderBy(table => table.BeginId).ThenBy(table => table.Id))
        {
            TableReadResult read;
            try
            {
                read = _reader.Read(project, table, tables);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"读取物品表失败：{table.TableName}，{ex.Message}");
                continue;
            }

            if (!read.Validation.IsUsable)
            {
                diagnostics.Add($"跳过不可用物品表：{table.TableName}，fileExists={read.Validation.FileExists}, fits={read.Validation.FitsInFile}, columnsMatch={read.Validation.ColumnsMatchBytes}");
                continue;
            }

            foreach (DataRow row in read.Data.Rows)
            {
                if (!row.Table.Columns.Contains("类型")) continue;
                var typeId = ReadInt(row, "类型");
                if (typeId < 0 || typeId > byte.MaxValue) continue;
                var name = ReadString(row, "名称").Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var id = ReadInt(row, "ID");
                var classification = ItemClassificationService.Classify(row, boundary);
                if (!result.TryGetValue(typeId, out var list))
                {
                    list = new List<ItemTypeSample>();
                    result[typeId] = list;
                }

                if (list.Count < MaxSampleCount * 2 &&
                    !list.Any(item => item.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                {
                    list.Add(new ItemTypeSample(id, name, classification.DisplayName));
                }
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ItemTypeSample>)pair.Value
                .OrderBy(sample => sample.ItemId)
                .Take(MaxSampleCount)
                .ToArray());
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<string>> BuildSampleNameLookup(
        IReadOnlyDictionary<int, IReadOnlyList<ItemTypeSample>> samples)
    {
        return samples.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.Select(sample => sample.Name).ToArray());
    }

    private static IReadOnlyDictionary<int, string> BuildNamesFromProbe(EquipmentTypeNameTableProbeResult? probe)
    {
        var result = new Dictionary<int, string>();
        if (probe == null || !probe.IsUsable) return result;
        var usableGroups = Math.Min((JobPermissionSlotCount + 1) / 2, probe.Names.Count);
        for (var group = 0; group < usableGroups; group++)
        {
            var evenTypeId = group * 2;
            var oddTypeId = evenTypeId + 1;
            result[evenTypeId] = probe.Names[group];
            if (oddTypeId < JobPermissionSlotCount)
            {
                result[oddTypeId] = probe.Names[group];
            }
        }

        return result;
    }

    private static string BuildNameFromSamples(IReadOnlyList<string> sampleNames)
    {
        if (sampleNames.Count == 0) return string.Empty;
        if (sampleNames.Count == 1) return sampleNames[0];
        var common = LongestCommonPrefix(sampleNames);
        if (common.Length >= 2) return common;
        common = LongestCommonSuffix(sampleNames);
        return common.Length >= 2 ? common : string.Empty;
    }

    private static string LongestCommonPrefix(IReadOnlyList<string> values)
    {
        if (values.Count == 0) return string.Empty;
        var prefix = values[0];
        foreach (var value in values.Skip(1))
        {
            var length = Math.Min(prefix.Length, value.Length);
            var i = 0;
            while (i < length && prefix[i] == value[i]) i++;
            prefix = prefix[..i];
            if (prefix.Length == 0) break;
        }

        return prefix.Trim(' ', '\u3000');
    }

    private static string LongestCommonSuffix(IReadOnlyList<string> values)
    {
        if (values.Count == 0) return string.Empty;
        var suffix = values[0];
        foreach (var value in values.Skip(1))
        {
            var length = Math.Min(suffix.Length, value.Length);
            var i = 0;
            while (i < length && suffix[suffix.Length - 1 - i] == value[value.Length - 1 - i]) i++;
            suffix = i == 0 ? string.Empty : suffix[^i..];
            if (suffix.Length == 0) break;
        }

        return suffix.Trim(' ', '\u3000');
    }

    private static string BuildExeAndSampleDisplayName(int typeId, string exeBaseName, string sampleName)
    {
        var baseName = string.IsNullOrWhiteSpace(sampleName)
            ? exeBaseName
            : NamesLookCompatible(sampleName, exeBaseName)
                ? exeBaseName
                : $"{sampleName}/{exeBaseName}";

        if (string.IsNullOrWhiteSpace(baseName)) return $"类型码{typeId}";
        if (typeId < JobPermissionSlotCount && !baseName.Contains('/'))
        {
            var prefix = typeId % 2 == 0 ? "普通" : "特殊";
            if (!baseName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return prefix + baseName;
            }
        }

        return baseName;
    }

    private static string CleanDisplayName(string value)
    {
        var text = value.Trim();
        foreach (var prefix in new[] { "木制", "铜制", "铁制", "银制", "金制" })
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal) && text.Length > prefix.Length)
            {
                text = text[prefix.Length..];
                break;
            }
        }

        if (text.StartsWith("制", StringComparison.Ordinal) && text.Length > 1)
        {
            text = text[1..];
        }

        return string.IsNullOrWhiteSpace(text) ? value : text;
    }

    private static bool NamesLookCompatible(string sampleName, string exeBaseName)
    {
        if (string.IsNullOrWhiteSpace(sampleName) || string.IsNullOrWhiteSpace(exeBaseName)) return false;
        return sampleName.Contains(exeBaseName, StringComparison.CurrentCulture) ||
               exeBaseName.Contains(sampleName, StringComparison.CurrentCulture);
    }

    private static ConfirmedEquipmentTypeProfile LoadConfirmedNames(CczProject project, List<string> diagnostics)
    {
        var path = BuildNotesPath(project);
        if (!File.Exists(path)) return new ConfirmedEquipmentTypeProfile();

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var profile = JsonSerializer.Deserialize<ConfirmedEquipmentTypeProfile>(File.ReadAllText(path), options)
                          ?? new ConfirmedEquipmentTypeProfile();
            diagnostics.Add($"已读取项目装备类型人工确认：{path}");
            return profile.Normalize();
        }
        catch (Exception ex)
        {
            diagnostics.Add($"项目装备类型人工确认 JSON 读取失败：{path}，{ex.Message}");
            return new ConfirmedEquipmentTypeProfile();
        }
    }

    private static string SanitizeFileName(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "Project" : value.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(ch, '_');
        }

        return text;
    }

    private static int ReadInt(DataRow row, string columnName)
        => row.Table.Columns.Contains(columnName)
            ? Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture)
            : 0;

    private static string ReadString(DataRow row, string columnName)
        => row.Table.Columns.Contains(columnName)
            ? Convert.ToString(row[columnName], CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;

    private sealed record ItemTypeSample(int ItemId, string Name, string MajorCategory);

    private sealed class ConfirmedEquipmentTypeProfile
    {
        [JsonPropertyName("TypeNames")]
        public Dictionary<string, string> TypeNamesRaw { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("SlotNames")]
        public Dictionary<string, string> SlotNamesRaw { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonIgnore]
        public IReadOnlyDictionary<int, string> TypeNames { get; private set; } = new Dictionary<int, string>();

        [JsonIgnore]
        public IReadOnlyDictionary<int, string> SlotNames { get; private set; } = new Dictionary<int, string>();

        public ConfirmedEquipmentTypeProfile Normalize()
        {
            TypeNames = NormalizeDictionary(TypeNamesRaw);
            SlotNames = NormalizeDictionary(SlotNamesRaw);
            return this;
        }

        private static IReadOnlyDictionary<int, string> NormalizeDictionary(IReadOnlyDictionary<string, string>? source)
        {
            if (source == null || source.Count == 0) return new Dictionary<int, string>();
            var result = new Dictionary<int, string>();
            foreach (var pair in source)
            {
                if (!int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var key)) continue;
                if (key < 0 || key > byte.MaxValue) continue;
                if (string.IsNullOrWhiteSpace(pair.Value)) continue;
                result[key] = pair.Value.Trim();
            }

            return result;
        }
    }
}
