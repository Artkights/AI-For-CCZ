using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class GlobalNumericDiscoveryService
{
    private static readonly string[] CoreDiffFiles = ["Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5"];
    private static readonly IReadOnlyList<LowRiskNumericCaseSpec> LowRiskCases =
    [
        new("PromotionLevelFirst", "转职等级（一转）", 20, 21, "promotion_level_first_case", "official_tool_promotion_level_first"),
        new("PromotionLevelSecond", "转职等级（二转）", 40, 41, "promotion_level_second_case", "official_tool_promotion_level_second"),
        new("EquipmentLevelLimitNormal", "普装等级上限", 5, 6, "equipment_level_limit_normal_case", "official_tool_equipment_level_limit_normal"),
        new("EquipmentLevelLimitSpecial", "特装等级上限", 9, 10, "equipment_level_limit_special_case", "official_tool_equipment_level_limit_special"),
        new("EquipmentLevelRaiseNormal", "普装提升等级", 4, 5, "equipment_level_raise_normal_case", "official_tool_equipment_level_raise_normal"),
        new("EquipmentLevelRaiseSpecial", "特装提升等级", 6, 7, "equipment_level_raise_special_case", "official_tool_equipment_level_raise_special"),
        new("MiddleEquipmentLevel", "中级装备出现等级", 20, 21, "middle_equipment_level_case", "official_tool_middle_equipment_level")
    ];
    private static readonly string[] OfficialToolRuntimeFiles =
    [
        "AutoDLL.ini",
        "CczEdit.ini",
        "Cmdicon.dll",
        "Font.e5",
        "Hexzmap.e5",
        "Itemicon.dll",
        "Jpg.dll",
        "Koeia.dll",
        "Koeicda.dll",
        "Logo.avi",
        "Mapatr.dll",
        "Mgcicon.dll",
        "Pmapobj.e5",
        "Unit_atk.e5",
        "Unit_mov.e5",
        "Unit_spc.e5",
        "zlib.dll"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public GlobalNumericDiscoveryReport PrepareManualDiffExperiment(
        CczProject project,
        IReadOnlyList<GlobalNumericSettingDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(definitions);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var evidenceRoot = Path.Combine(
            project.WorkspaceRoot,
            "CCZModStudio_Reports",
            "DebugEvidence",
            "global-numeric-discovery",
            stamp);
        var beforeRoot = Path.Combine(evidenceRoot, "before");
        var officialCaseRoot = Path.Combine(evidenceRoot, "official_case");
        var officialToolRoot = Path.Combine(evidenceRoot, "official_tool");
        Directory.CreateDirectory(beforeRoot);
        Directory.CreateDirectory(officialCaseRoot);
        Directory.CreateDirectory(officialToolRoot);

        var warnings = new List<string>();
        CopyCoreFiles(project.GameRoot, beforeRoot, warnings);
        CopyCoreFiles(project.GameRoot, officialCaseRoot, warnings);
        CopyOfficialTool(project, officialToolRoot, officialCaseRoot, warnings);

        var diffs = CoreDiffFiles
            .Select(fileName => BuildFileDiff(beforeRoot, officialCaseRoot, fileName))
            .ToArray();

        var fields = definitions
            .Select((definition, index) => BuildDiscoveryField(definition, index))
            .ToArray();

        var report = new GlobalNumericDiscoveryReport
        {
            Status = "NeedsManualOfficialDiff",
            ProjectRoot = project.WorkspaceRoot,
            SourceGameRoot = project.GameRoot,
            EvidenceRoot = evidenceRoot,
            BeforeRoot = beforeRoot,
            OfficialCaseRoot = officialCaseRoot,
            OfficialToolRoot = officialToolRoot,
            Fields = fields,
            FileDiffs = diffs,
            Warnings = warnings,
            ManualSteps = BuildManualSteps(officialToolRoot, officialCaseRoot)
        };

        var reportPath = Path.Combine(evidenceRoot, "discovery-report.json");
        report.ReportPath = reportPath;
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));
        return report;
    }

    public GlobalNumericLowRiskExperimentReport PrepareLowRiskManualDiffExperiment(CczProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var evidenceRoot = Path.Combine(
            project.WorkspaceRoot,
            "CCZModStudio_Reports",
            "DebugEvidence",
            "global-numeric-discovery",
            stamp);
        var beforeRoot = Path.Combine(evidenceRoot, "before");
        var noopCaseRoot = Path.Combine(evidenceRoot, "noop_case");
        var noopOfficialToolRoot = Path.Combine(evidenceRoot, "official_tool_noop");
        Directory.CreateDirectory(beforeRoot);
        Directory.CreateDirectory(noopCaseRoot);
        Directory.CreateDirectory(noopOfficialToolRoot);

        var warnings = new List<string>();
        CopyCoreFiles(project.GameRoot, beforeRoot, warnings);
        CopyCoreFiles(project.GameRoot, noopCaseRoot, warnings);
        CopyOfficialTool(project, noopOfficialToolRoot, noopCaseRoot, warnings);

        var cases = new List<GlobalNumericLowRiskCase>();
        foreach (var spec in LowRiskCases)
        {
            var caseRoot = Path.Combine(evidenceRoot, spec.CaseDirectoryName);
            var officialToolRoot = Path.Combine(evidenceRoot, spec.OfficialToolDirectoryName);
            Directory.CreateDirectory(caseRoot);
            Directory.CreateDirectory(officialToolRoot);
            CopyCoreFiles(project.GameRoot, caseRoot, warnings);
            CopyOfficialTool(project, officialToolRoot, caseRoot, warnings);
            cases.Add(new GlobalNumericLowRiskCase
            {
                Key = spec.Key,
                DisplayName = spec.DisplayName,
                OldValue = spec.OldValue,
                NewValue = spec.NewValue,
                CaseDirectoryName = spec.CaseDirectoryName,
                OfficialToolDirectoryName = spec.OfficialToolDirectoryName,
                CaseRoot = caseRoot,
                OfficialToolRoot = officialToolRoot,
                Instruction = $"打开 {officialToolRoot} 中的形象指定器，进入全局设置，只把“{spec.DisplayName}”从 {spec.OldValue} 改为 {spec.NewValue} 后保存。"
            });
        }

        var report = new GlobalNumericLowRiskExperimentReport
        {
            ProjectRoot = project.WorkspaceRoot,
            SourceGameRoot = project.GameRoot,
            EvidenceRoot = evidenceRoot,
            BeforeRoot = beforeRoot,
            NoopCaseRoot = noopCaseRoot,
            NoopOfficialToolRoot = noopOfficialToolRoot,
            Cases = cases,
            Warnings = warnings,
            ManualSteps = BuildLowRiskManualSteps(noopOfficialToolRoot, noopCaseRoot, cases)
        };

        var reportPath = Path.Combine(evidenceRoot, "low-risk-experiment-report.json");
        report.ReportPath = reportPath;
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));
        return report;
    }

    public GlobalNumericLowRiskCompareReport CompareLowRiskCaseDiffs(CczProject project, string evidenceRoot)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(evidenceRoot)) throw new InvalidOperationException("evidenceRoot is required.");
        evidenceRoot = Path.GetFullPath(evidenceRoot);
        if (!Directory.Exists(evidenceRoot)) throw new DirectoryNotFoundException(evidenceRoot);

        var noopCaseRoot = Path.Combine(evidenceRoot, "noop_case");
        if (!Directory.Exists(noopCaseRoot)) throw new DirectoryNotFoundException(noopCaseRoot);

        var warnings = new List<string>();
        var caseDiffs = LowRiskCases
            .Select(spec => BuildLowRiskCaseDiff(noopCaseRoot, Path.Combine(evidenceRoot, spec.CaseDirectoryName), spec))
            .ToArray();
        var sharedOffsets = caseDiffs
            .SelectMany(caseDiff => caseDiff.CandidateWriteTargets.Select(target => new
            {
                caseDiff.Key,
                OffsetKey = $"{target.TargetFileName}@{HexDisplayFormatter.FormatOffset(target.FileOffset)}"
            }))
            .GroupBy(item => item.OffsetKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sharedSet = new HashSet<string>(sharedOffsets, StringComparer.OrdinalIgnoreCase);
        caseDiffs = caseDiffs.Select(caseDiff => ApplySharedOffsetStatus(caseDiff, sharedSet)).ToArray();

        var report = new GlobalNumericLowRiskCompareReport
        {
            Status = caseDiffs.Any(item => item.HasChanges) ? "ManualDiffCaptured" : "NeedsManualOfficialDiff",
            ProjectRoot = project.WorkspaceRoot,
            SourceGameRoot = project.GameRoot,
            EvidenceRoot = evidenceRoot,
            NoopCaseRoot = noopCaseRoot,
            Cases = caseDiffs,
            SharedOffsetKeys = sharedOffsets,
            Warnings = warnings
        };

        var reportPath = Path.Combine(evidenceRoot, "low-risk-case-diff-report.json");
        report.ReportPath = reportPath;
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));
        return report;
    }

    public GlobalNumericDiscoveryReport CompareManualDiffExperiment(
        CczProject project,
        string beforeRoot,
        string officialCaseRoot,
        IReadOnlyList<GlobalNumericSettingDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(definitions);
        if (string.IsNullOrWhiteSpace(beforeRoot)) throw new InvalidOperationException("beforeRoot is required.");
        if (string.IsNullOrWhiteSpace(officialCaseRoot)) throw new InvalidOperationException("officialCaseRoot is required.");
        if (!Directory.Exists(beforeRoot)) throw new DirectoryNotFoundException(beforeRoot);
        if (!Directory.Exists(officialCaseRoot)) throw new DirectoryNotFoundException(officialCaseRoot);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var evidenceRoot = Path.Combine(
            project.WorkspaceRoot,
            "CCZModStudio_Reports",
            "DebugEvidence",
            "global-numeric-discovery",
            stamp);
        Directory.CreateDirectory(evidenceRoot);

        var diffs = CoreDiffFiles
            .Select(fileName => BuildFileDiff(beforeRoot, officialCaseRoot, fileName))
            .ToArray();
        var allChanges = diffs.SelectMany(diff => diff.Changes).ToArray();
        var fields = definitions
            .Select((definition, index) => BuildDiscoveryField(definition, index, allChanges))
            .ToArray();

        var report = new GlobalNumericDiscoveryReport
        {
            Status = allChanges.Length == 0 ? "NeedsManualOfficialDiff" : "ManualDiffCaptured",
            ProjectRoot = project.WorkspaceRoot,
            SourceGameRoot = project.GameRoot,
            EvidenceRoot = evidenceRoot,
            BeforeRoot = Path.GetFullPath(beforeRoot),
            OfficialCaseRoot = Path.GetFullPath(officialCaseRoot),
            OfficialToolRoot = string.Empty,
            Fields = fields,
            FileDiffs = diffs,
            Warnings = Array.Empty<string>(),
            ManualSteps = BuildManualSteps(string.Empty, Path.GetFullPath(officialCaseRoot))
        };

        var reportPath = Path.Combine(evidenceRoot, "discovery-report.json");
        report.ReportPath = reportPath;
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));
        return report;
    }

    private static void CopyCoreFiles(string sourceRoot, string targetRoot, List<string> warnings)
    {
        foreach (var fileName in CoreDiffFiles)
        {
            var source = Path.Combine(sourceRoot, fileName);
            var target = Path.Combine(targetRoot, fileName);
            if (!File.Exists(source))
            {
                warnings.Add($"Core file missing and was not copied: {source}");
                continue;
            }

            File.Copy(source, target, overwrite: false);
        }

        foreach (var fileName in OfficialToolRuntimeFiles)
        {
            var source = Path.Combine(sourceRoot, fileName);
            var target = Path.Combine(targetRoot, fileName);
            if (!File.Exists(source))
            {
                continue;
            }

            File.Copy(source, target, overwrite: false);
        }

        File.WriteAllText(
            Path.Combine(targetRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={sourceRoot}\r\nPurpose=global numeric discovery\r\n",
            EncodingService.Gbk);
    }

    private static void CopyOfficialTool(CczProject project, string officialToolRoot, string officialCaseRoot, List<string> warnings)
    {
        var sourceRoot = ResolveOfficialToolRoot(project);
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            warnings.Add("Official image assigner directory was not found; official_tool is empty.");
            return;
        }

        foreach (var file in Directory.GetFiles(sourceRoot))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(officialToolRoot, name), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceRoot))
        {
            var target = Path.Combine(officialToolRoot, Path.GetFileName(directory));
            CopyDirectory(directory, target);
        }

        var systemIni = Path.Combine(officialToolRoot, "System.ini");
        if (!File.Exists(systemIni))
        {
            warnings.Add("Copied official tool does not contain System.ini: " + officialToolRoot);
            return;
        }

        RewriteUserPaths(systemIni, officialCaseRoot);
    }

    private static string? ResolveOfficialToolRoot(CczProject project)
    {
        foreach (var candidate in BuildOfficialToolRootCandidates(project))
        {
            if (DirectoryContainsOfficialTool(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> BuildOfficialToolRootCandidates(CczProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.ImageAssignerDirectory))
        {
            yield return project.ImageAssignerDirectory;
        }

        if (!string.IsNullOrWhiteSpace(project.ImageAssignerSystemIniPath) &&
            File.Exists(project.ImageAssignerSystemIniPath))
        {
            var directory = Path.GetDirectoryName(project.ImageAssignerSystemIniPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                yield return directory;
            }
        }

        var roots = new[]
            {
                project.GameRoot,
                project.WorkspaceRoot,
                PortableInstallPaths.LegacyResourcesRoot
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var names = new[]
        {
            "形象指定器6.5",
            "6.5形象指定器",
            "形象指定器65",
            "6.6x形象指定器",
            "形象指定器6.6",
            "形象指定器66x"
        };

        foreach (var root in roots)
        {
            foreach (var name in names)
            {
                yield return Path.Combine(root, "老版游戏制作工具", "B形象指定器", name);
                yield return Path.Combine(root, "B形象指定器", name);
                yield return Path.Combine(root, name);
            }
        }
    }

    private static bool DirectoryContainsOfficialTool(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        return Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
            .Any(path => Path.GetFileName(path).Contains("形象指定器", StringComparison.OrdinalIgnoreCase));
    }

    private static void CopyDirectory(string sourceRoot, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        foreach (var file in Directory.GetFiles(sourceRoot))
        {
            File.Copy(file, Path.Combine(targetRoot, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceRoot))
        {
            CopyDirectory(directory, Path.Combine(targetRoot, Path.GetFileName(directory)));
        }
    }

    private static void RewriteUserPaths(string systemIniPath, string officialCaseRoot)
    {
        var normalizedRoot = Path.GetFullPath(officialCaseRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var lines = File.ReadAllLines(systemIniPath, EncodingService.Gbk);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UserPath1", "UserPath2", "UserPath3" };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var equal = line.IndexOf('=');
            if (equal <= 0) continue;

            var key = line[..equal].Trim();
            if (!keys.Contains(key)) continue;

            lines[i] = key + "=" + normalizedRoot;
            seen.Add(key);
        }

        foreach (var key in keys.Where(key => !seen.Contains(key)))
        {
            lines = lines.Append(key + "=" + normalizedRoot).ToArray();
        }

        File.WriteAllLines(systemIniPath, lines, EncodingService.Gbk);
    }

    private static GlobalNumericDiscoveryField BuildDiscoveryField(
        GlobalNumericSettingDefinition definition,
        int index,
        IReadOnlyList<GlobalNumericDiscoveryChange>? allChanges = null)
    {
        return new GlobalNumericDiscoveryField
        {
            Key = definition.Key,
            UiControlIndex = index,
            DisplayName = definition.DisplayName,
            DefaultValueText = definition.DefaultValueText,
            OldValueText = definition.CurrentValueText,
            NewValueText = SuggestNextValue(definition.DefaultValueText),
            EvidenceStatus = definition.EvidenceStatus,
            EvidenceSource = definition.EvidenceSource,
            TargetFileName = definition.TargetFileName,
            FileOffset = definition.FileOffset,
            RuntimeAddress = definition.RuntimeAddress,
            ByteLength = definition.ByteLength,
            WriteTargets = definition.WriteTargets,
            ValueKind = definition.ValueKind.ToString(),
            UniqueDiff = allChanges?.Count == 1,
            Changes = allChanges ?? Array.Empty<GlobalNumericDiscoveryChange>()
        };
    }

    private static GlobalNumericDiscoveryFileDiff BuildFileDiff(string beforeRoot, string afterRoot, string relativePath)
    {
        var beforePath = Path.Combine(beforeRoot, relativePath);
        var afterPath = Path.Combine(afterRoot, relativePath);
        var beforeExists = File.Exists(beforePath);
        var afterExists = File.Exists(afterPath);
        var before = beforeExists ? File.ReadAllBytes(beforePath) : Array.Empty<byte>();
        var after = afterExists ? File.ReadAllBytes(afterPath) : Array.Empty<byte>();
        var runtimeAddressResolver = BuildRuntimeAddressResolver(beforePath, relativePath);
        var changes = beforeExists && afterExists
            ? BuildChangedRanges(relativePath, before, after, runtimeAddressResolver).ToArray()
            : Array.Empty<GlobalNumericDiscoveryChange>();

        return new GlobalNumericDiscoveryFileDiff
        {
            RelativePath = relativePath,
            BeforeExists = beforeExists,
            AfterExists = afterExists,
            BeforeLength = before.LongLength,
            AfterLength = after.LongLength,
            BeforeSha256 = beforeExists ? WriteOperationReportService.ComputeSha256(before) : string.Empty,
            AfterSha256 = afterExists ? WriteOperationReportService.ComputeSha256(after) : string.Empty,
            ChangedByteCount = CountChangedBytes(before, after),
            Changes = changes
        };
    }

    private static GlobalNumericLowRiskCaseDiff BuildLowRiskCaseDiff(string noopCaseRoot, string caseRoot, LowRiskNumericCaseSpec spec)
    {
        var caseExists = Directory.Exists(caseRoot);
        var diffs = CoreDiffFiles
            .Select(fileName => caseExists
                ? BuildFileDiff(noopCaseRoot, caseRoot, fileName)
                : BuildMissingCaseDiff(noopCaseRoot, fileName))
            .ToArray();
        var changedDiffs = diffs.Where(diff => diff.ChangedByteCount > 0 || diff.Changes.Count > 0).ToArray();
        var hasChanges = changedDiffs.Length > 0;
        var ekd5Only = hasChanges && changedDiffs.All(diff => diff.RelativePath.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase));
        var candidates = diffs
            .Where(diff => diff.RelativePath.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase))
            .SelectMany(diff => diff.Changes.Select((change, index) => ToLowRiskCandidate(spec, change, index + 1)))
            .ToArray();
        var byteIncrementShape = candidates.Length > 0 && candidates.All(candidate => candidate.ExpectedDeltaShape);
        var minimalPromotable = caseExists && hasChanges && ekd5Only && byteIncrementShape;

        return new GlobalNumericLowRiskCaseDiff
        {
            Key = spec.Key,
            DisplayName = spec.DisplayName,
            OldValue = spec.OldValue,
            NewValue = spec.NewValue,
            CaseRoot = caseRoot,
            CaseExists = caseExists,
            HasChanges = hasChanges,
            Ekd5Only = ekd5Only,
            ByteIncrementShape = byteIncrementShape,
            HasSharedOffsets = false,
            MinimalPromotableCandidate = minimalPromotable,
            Conclusion = BuildLowRiskConclusion(caseExists, hasChanges, ekd5Only, byteIncrementShape, hasSharedOffsets: false),
            CandidateWriteTargets = candidates,
            FileDiffs = diffs
        };
    }

    private static GlobalNumericDiscoveryFileDiff BuildMissingCaseDiff(string noopCaseRoot, string relativePath)
    {
        var beforePath = Path.Combine(noopCaseRoot, relativePath);
        var beforeExists = File.Exists(beforePath);
        var before = beforeExists ? File.ReadAllBytes(beforePath) : Array.Empty<byte>();
        return new GlobalNumericDiscoveryFileDiff
        {
            RelativePath = relativePath,
            BeforeExists = beforeExists,
            AfterExists = false,
            BeforeLength = before.LongLength,
            AfterLength = 0,
            BeforeSha256 = beforeExists ? WriteOperationReportService.ComputeSha256(before) : string.Empty,
            AfterSha256 = string.Empty,
            ChangedByteCount = 0,
            Changes = Array.Empty<GlobalNumericDiscoveryChange>()
        };
    }

    private static GlobalNumericLowRiskTargetCandidate ToLowRiskCandidate(LowRiskNumericCaseSpec spec, GlobalNumericDiscoveryChange change, int index)
    {
        var expectedDelta = IsSingleByteIncrement(change);
        return new GlobalNumericLowRiskTargetCandidate
        {
            TargetFileName = change.RelativePath,
            FileOffset = change.FileOffset,
            RuntimeAddress = change.RuntimeAddress,
            ByteLength = change.ByteLength,
            OldBytesHex = change.OldBytesHex,
            NewBytesHex = change.NewBytesHex,
            ValueDelta = 0,
            ExpectedDeltaShape = expectedDelta,
            Purpose = $"{spec.DisplayName} 候选常量 #{index}"
        };
    }

    private static bool IsSingleByteIncrement(GlobalNumericDiscoveryChange change)
    {
        if (change.ByteLength != 1 ||
            change.OldBytesHex.Length != 2 ||
            change.NewBytesHex.Length != 2 ||
            !byte.TryParse(change.OldBytesHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var oldValue) ||
            !byte.TryParse(change.NewBytesHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var newValue))
        {
            return false;
        }

        return newValue == oldValue + 1;
    }

    private static GlobalNumericLowRiskCaseDiff ApplySharedOffsetStatus(GlobalNumericLowRiskCaseDiff caseDiff, HashSet<string> sharedOffsets)
    {
        var hasSharedOffsets = caseDiff.CandidateWriteTargets.Any(target => sharedOffsets.Contains($"{target.TargetFileName}@{HexDisplayFormatter.FormatOffset(target.FileOffset)}"));
        return new GlobalNumericLowRiskCaseDiff
        {
            Key = caseDiff.Key,
            DisplayName = caseDiff.DisplayName,
            OldValue = caseDiff.OldValue,
            NewValue = caseDiff.NewValue,
            CaseRoot = caseDiff.CaseRoot,
            CaseExists = caseDiff.CaseExists,
            HasChanges = caseDiff.HasChanges,
            Ekd5Only = caseDiff.Ekd5Only,
            ByteIncrementShape = caseDiff.ByteIncrementShape,
            HasSharedOffsets = hasSharedOffsets,
            MinimalPromotableCandidate = caseDiff.MinimalPromotableCandidate && !hasSharedOffsets,
            Conclusion = BuildLowRiskConclusion(caseDiff.CaseExists, caseDiff.HasChanges, caseDiff.Ekd5Only, caseDiff.ByteIncrementShape, hasSharedOffsets),
            CandidateWriteTargets = caseDiff.CandidateWriteTargets,
            FileDiffs = caseDiff.FileDiffs
        };
    }

    private static string BuildLowRiskConclusion(bool caseExists, bool hasChanges, bool ekd5Only, bool byteIncrementShape, bool hasSharedOffsets)
    {
        if (!caseExists) return "case 目录不存在，等待生成实验目录或人工操作。";
        if (!hasChanges) return "尚未捕获人工单字段 diff，保持只读。";
        if (!ekd5Only) return "diff 触及 Ekd5.exe 以外文件，本批不晋级。";
        if (!byteIncrementShape) return "diff 不是 1 字节 +1 形态，本批不晋级。";
        if (hasSharedOffsets) return "offset 被多个 leaf 字段同时命中，等待人工复核。";
        return "形成最小可解释候选；仍需写入内置 VerifiedDefinition 并跑 CCZ round-trip 后才能开放。";
    }

    private static IEnumerable<GlobalNumericDiscoveryChange> BuildChangedRanges(
        string relativePath,
        byte[] before,
        byte[] after,
        Func<long, long> runtimeAddressResolver)
    {
        var max = Math.Max(before.Length, after.Length);
        var index = 0;
        while (index < max)
        {
            if (ByteAt(before, index) == ByteAt(after, index))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < max && ByteAt(before, index) != ByteAt(after, index))
            {
                index++;
            }

            var length = index - start;
            yield return new GlobalNumericDiscoveryChange
            {
                RelativePath = relativePath,
                FileOffset = start,
                RuntimeAddress = runtimeAddressResolver(start),
                ByteLength = length,
                OldBytesHex = ToHexSlice(before, start, length),
                NewBytesHex = ToHexSlice(after, start, length)
            };
        }
    }

    private static int CountChangedBytes(byte[] before, byte[] after)
    {
        var max = Math.Max(before.Length, after.Length);
        var count = 0;
        for (var i = 0; i < max; i++)
        {
            if (ByteAt(before, i) != ByteAt(after, i)) count++;
        }

        return count;
    }

    private static int ByteAt(byte[] bytes, int index)
        => index >= 0 && index < bytes.Length ? bytes[index] : -1;

    private static string ToHexSlice(byte[] bytes, int start, int length)
    {
        if (start >= bytes.Length || length <= 0) return string.Empty;
        var actualLength = Math.Min(length, bytes.Length - start);
        return Convert.ToHexString(bytes.AsSpan(start, actualLength));
    }

    private static Func<long, long> BuildRuntimeAddressResolver(string beforePath, string relativePath)
    {
        if (!relativePath.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(beforePath))
        {
            return _ => 0;
        }

        try
        {
            var sections = ReadPeSections(beforePath);
            return fileOffset =>
            {
                foreach (var section in sections)
                {
                    if (fileOffset >= section.RawPointer && fileOffset < section.RawPointer + section.RawSize)
                    {
                        return section.ImageBase + section.VirtualAddress + (fileOffset - section.RawPointer);
                    }
                }

                return 0;
            };
        }
        catch
        {
            return _ => 0;
        }
    }

    private static IReadOnlyList<DiscoveryPeSection> ReadPeSections(string exePath)
    {
        using var stream = File.OpenRead(exePath);
        using var reader = new BinaryReader(stream);
        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        stream.Position = peOffset;
        var signature = reader.ReadUInt32();
        if (signature != 0x00004550) return Array.Empty<DiscoveryPeSection>();

        _ = reader.ReadUInt16();
        var sectionCount = reader.ReadUInt16();
        stream.Position += 12;
        var optionalHeaderSize = reader.ReadUInt16();
        stream.Position += 2;

        var optionalHeaderStart = stream.Position;
        var magic = reader.ReadUInt16();
        long imageBase;
        if (magic == 0x10B)
        {
            stream.Position = optionalHeaderStart + 28;
            imageBase = reader.ReadUInt32();
        }
        else if (magic == 0x20B)
        {
            stream.Position = optionalHeaderStart + 24;
            imageBase = checked((long)reader.ReadUInt64());
        }
        else
        {
            return Array.Empty<DiscoveryPeSection>();
        }

        stream.Position = optionalHeaderStart + optionalHeaderSize;
        var sections = new List<DiscoveryPeSection>();
        for (var i = 0; i < sectionCount; i++)
        {
            stream.Position += 8;
            var virtualSize = reader.ReadUInt32();
            var virtualAddress = reader.ReadUInt32();
            var rawSize = reader.ReadUInt32();
            var rawPointer = reader.ReadUInt32();
            stream.Position += 16;
            sections.Add(new DiscoveryPeSection(imageBase, virtualAddress, virtualSize, rawPointer, rawSize));
        }

        return sections;
    }

    private sealed record DiscoveryPeSection(long ImageBase, uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize);

    private static string SuggestNextValue(string text)
    {
        var firstNumber = new string((text ?? string.Empty)
            .SkipWhile(c => !char.IsDigit(c))
            .TakeWhile(char.IsDigit)
            .ToArray());
        if (int.TryParse(firstNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return (value + 1).ToString(CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> BuildManualSteps(string officialToolRoot, string officialCaseRoot)
    {
        var launchTarget = string.IsNullOrWhiteSpace(officialToolRoot)
            ? "<official_tool>"
            : officialToolRoot;
        return
        [
            "Open the copied official image assigner from: " + launchTarget,
            "Use the copied project path: " + officialCaseRoot,
            "Open Ekd5.exe, enter the global settings page, change exactly one field, and save.",
            "Compare before vs official_case with CompareManualDiffExperiment or run the smoke again after preserving the edited official_case.",
            "Do not promote any field to CanEdit=true until the diff is unique, reread-stable, and runtime-validated."
        ];
    }

    private static IReadOnlyList<string> BuildLowRiskManualSteps(
        string noopOfficialToolRoot,
        string noopCaseRoot,
        IReadOnlyList<GlobalNumericLowRiskCase> cases)
    {
        var steps = new List<string>
        {
            "1. 先打开 noop 官方工具副本：" + noopOfficialToolRoot,
            "2. 指向 noop_case 后进入全局设置，不改任何字段直接保存：" + noopCaseRoot,
            "3. 再逐个打开下面每个 official_tool_<caseKey>，一次只改对应字段并保存。"
        };
        steps.AddRange(cases.Select(item => item.Instruction));
        steps.Add("4. 保存完全部 case 后，运行 low-risk compare，生成 low-risk-case-diff-report.json。");
        steps.Add("5. 只有 Ekd5.exe-only、1 字节 +1、无共享 offset 的 case 才能进入后续 CCZ 写回复读。");
        return steps;
    }

    private sealed record LowRiskNumericCaseSpec(
        string Key,
        string DisplayName,
        int OldValue,
        int NewValue,
        string CaseDirectoryName,
        string OfficialToolDirectoryName);
}
