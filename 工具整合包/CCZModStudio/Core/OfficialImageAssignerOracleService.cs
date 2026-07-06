using System.Diagnostics;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class OfficialImageAssignerOracleService
{
    public const long Expected65ExeLength = 1_449_984;
    public const long Expected66xExeLength = 1_519_616;
    public const long ExpectedSImageOffset = 0xD2800;
    public const long ExpectedRImageOffset = 0xE1000;
    public const long ExpectedUserXkOffset = 0xA3280;
    public const int ExpectedDefId = 70;
    public const int ExpectedAssId = 109;
    public const int ExpectedStrategyCount = 144;
    public const int Expected65StarItemCount = 96;
    public const int Expected66xStarItemCount = 368;

    private static readonly string[] DependencyRelativePaths =
    [
        "MSFLXGRD.OCX",
        "zlib.dll",
        "Mark.DEC",
        Path.Combine("Source", "addPmap.e5")
    ];

    private static readonly string[] StrategyExtensionKeys =
    [
        "MgID",
        "MgAIYN1",
        "MgAIYN2",
        "MgHit",
        "MgHurt",
        "MgHurtYN",
        "MgMeff",
        "MgMcall",
        "Mg8",
        "MgMF"
    ];

    public ImageAssignerOracleProfile Detect(CczProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var engine = new CczEngineProfileService().Detect(project);
        var preferred = Ccz66RevisedLayout.Is66(engine) ? "6.6x" : "6.5";
        var directories = BuildCandidateDirectories(project, preferred);
        ImageAssignerOracleProfile? fallback = null;
        foreach (var directory in directories)
        {
            var profile = BuildProfile(project, engine, directory);
            if (!profile.Found) continue;
            if (profile.Config.Found && !string.IsNullOrWhiteSpace(profile.ExecutablePath) && File.Exists(profile.ExecutablePath))
            {
                return profile;
            }

            fallback ??= profile;
        }

        if (fallback != null) return fallback;

        return new ImageAssignerOracleProfile
        {
            Found = false,
            CompatibilityStatus = "Missing",
            Warnings =
            [
                "Official image assigner directory was not found under the project, workspace, or packaged legacy resources."
            ]
        };
    }

    public ImageAssignerOracleConfig ReadConfig(CczProject project)
        => Detect(project).Config;

    public ImageAssignerOracleComparison Compare(CczProject project, IReadOnlyList<HexTableDefinition> tables, bool includeGlobalCandidates)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tables);

        var profile = Detect(project);
        if (!profile.Found || !profile.Config.Found)
        {
            return new ImageAssignerOracleComparison
            {
                HasOracle = false,
                OracleStatus = "ConfigMissing",
                ProjectVersion = new CczEngineProfileService().Detect(project).VersionHint,
                OracleVersion = profile.VersionKind,
                Warnings = profile.Warnings.Count == 0 ? ["Official image assigner System.ini is missing."] : profile.Warnings
            };
        }

        var engine = new CczEngineProfileService().Detect(project);
        var checks = new List<ImageAssignerOracleCheck>
        {
            CompareTableOffset(project, tables, "R形象", "RFileHead", ExpectedRImageOffset),
            CompareTableOffset(project, tables, "S形象", "FileHead", ExpectedSImageOffset),
            CompareTableOffset(project, tables, "兵种相克", "UserXK", ExpectedUserXkOffset),
            CompareConfigValue(profile.Config, "DefID", ExpectedDefId, "Item defense boundary"),
            CompareConfigValue(profile.Config, "AssID", ExpectedAssId, "Item accessory boundary"),
            CompareConfigValue(profile.Config, "SMagic", ExpectedStrategyCount, "Strategy count")
        };

        if (includeGlobalCandidates)
        {
            checks.Add(new ImageAssignerOracleCheck
            {
                Key = "GlobalNumericSettings",
                Status = "NeedsUiOrDiffExtraction",
                Expected = "Official output diff or UI export",
                Actual = "Static System.ini does not contain global numeric offsets",
                Detail = "Pending global settings must be promoted through official-tool diff plus reread/runtime validation."
            });
        }

        var warnings = new List<string>(profile.Warnings);
        if (profile.CompatibilityStatus == "CrossVersionOracle")
        {
            warnings.Add("Official image assigner version differs from the detected engine version; use as read-only evidence only.");
        }

        var status = ResolveOverallStatus(profile, checks);
        return new ImageAssignerOracleComparison
        {
            HasOracle = true,
            OracleStatus = status,
            ProjectVersion = engine.VersionHint,
            OracleVersion = profile.VersionKind,
            Checks = checks,
            StrategyExtensionReadOnlyCandidates = profile.Config.StrategyExtensionAddresses,
            Warnings = warnings
        };
    }

    public string ResolveTableOracleStatus(CczProject project, HexTableDefinition table)
    {
        if (!IsImageAssignmentTable(table)) return string.Empty;
        var profile = Detect(project);
        if (!profile.Found || !profile.Config.Found) return "ConfigMissing";
        if (profile.CompatibilityStatus == "CrossVersionOracle") return "CrossVersionOracle";

        var expected = table.TableName.Contains("R", StringComparison.OrdinalIgnoreCase)
            ? GetNumeric(profile.Config, "RFileHead") ?? ExpectedRImageOffset
            : GetNumeric(profile.Config, "FileHead") ?? ExpectedSImageOffset;
        return table.FileName.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) && table.DataPos == expected
            ? "MatchedOfficialImageAssigner"
            : "OffsetMismatch";
    }

    public void EnsureImageAssignmentTableMatchesOracle(CczProject project, HexTableDefinition table)
    {
        var status = ResolveTableOracleStatus(project, table);
        if (status == "OffsetMismatch")
        {
            throw new InvalidOperationException($"Image assignment table {table.TableName} does not match the official image assigner oracle.");
        }
    }

    public ImageAssignerValidationPlan BuildValidationPlan(CczProject project, string changeKind, int? rowId)
    {
        var profile = Detect(project);
        var normalized = NormalizeChangeKind(changeKind);
        var expectedRanges = BuildExpectedRanges(normalized, rowId);
        return new ImageAssignerValidationPlan
        {
            ChangeKind = normalized,
            RowId = rowId,
            OracleVersion = profile.VersionKind,
            OfficialToolPath = profile.ExecutablePath,
            RequiredCopies =
            [
                "before: unchanged test copy of the project",
                "official_case: copy modified by the official image assigner",
                "ccz_case: copy modified by CCZModStudio using the same intended edit"
            ],
            OfficialObservationSteps = BuildOfficialObservationSteps(normalized, rowId),
            CczModStudioSteps = BuildCczSteps(normalized, rowId),
            CompareTargets = ["Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5"],
            ExpectedByteRanges = expectedRanges,
            PromotionCriteria =
            [
                "Official and CCZ changed-file sets match, or all differences are explained in the report.",
                "Changed byte ranges match the official oracle range for the requested edit.",
                "CCZ reread returns the same semantic value as the official output.",
                "Global numeric candidates additionally require address classification and runtime validation before writes are enabled."
            ]
        };
    }

    public object RunSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables, string? mode)
    {
        var normalized = string.IsNullOrWhiteSpace(mode) ? "static" : mode.Trim().ToLowerInvariant();
        if (normalized is not ("static" or "launch_only" or "ui_probe"))
        {
            throw new InvalidOperationException("mode must be static, launch_only, or ui_probe.");
        }

        var profile = Detect(project);
        var comparison = Compare(project, tables, includeGlobalCandidates: true);
        object? launch = null;
        if (normalized is "launch_only" or "ui_probe")
        {
            launch = LaunchOfficialToolReadOnly(profile, normalized == "ui_probe");
        }

        return new
        {
            Mode = normalized,
            Profile = profile,
            Comparison = comparison,
            Launch = launch,
            SafetyNote = "Smoke never clicks save. launch_only/ui_probe only starts the official tool for read-only process/window inspection."
        };
    }

    public ImageAssignerOutputDiffReport CompareOutputs(string beforeRoot, string officialAfterRoot, string cczAfterRoot)
    {
        beforeRoot = Path.GetFullPath(beforeRoot);
        officialAfterRoot = Path.GetFullPath(officialAfterRoot);
        cczAfterRoot = Path.GetFullPath(cczAfterRoot);

        var warnings = new List<string>();
        foreach (var root in new[] { beforeRoot, officialAfterRoot, cczAfterRoot })
        {
            if (!Directory.Exists(root)) warnings.Add("Missing directory: " + root);
        }

        if (warnings.Count > 0)
        {
            return new ImageAssignerOutputDiffReport
            {
                BeforeRoot = beforeRoot,
                OfficialAfterRoot = officialAfterRoot,
                CczAfterRoot = cczAfterRoot,
                Matches = false,
                Warnings = warnings
            };
        }

        var relativePaths = Directory.EnumerateFiles(beforeRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(beforeRoot, path))
            .Concat(Directory.EnumerateFiles(officialAfterRoot, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(officialAfterRoot, path)))
            .Concat(Directory.EnumerateFiles(cczAfterRoot, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(cczAfterRoot, path)))
            .Where(IsCoreOracleDiffTarget)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var files = relativePaths
            .Select(relative => CompareOneFile(beforeRoot, officialAfterRoot, cczAfterRoot, relative))
            .ToList();

        return new ImageAssignerOutputDiffReport
        {
            BeforeRoot = beforeRoot,
            OfficialAfterRoot = officialAfterRoot,
            CczAfterRoot = cczAfterRoot,
            Matches = files.All(file => file.Matches),
            Files = files,
            Warnings = warnings
        };
    }

    public ImageAssignerOracleExperimentResult RunAssignmentWriteExperiment(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        string changeKind,
        int rowId,
        int? requestedNewValue)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tables);

        var normalized = NormalizeAssignmentExperimentKind(changeKind);
        var profile = Detect(project);
        if (!profile.Found || !profile.Config.Found)
        {
            throw new InvalidOperationException("Official image assigner oracle config is missing.");
        }

        var table = ResolveAssignmentTable(project, tables, normalized);
        var columnName = ResolveAssignmentColumn(table, normalized);
        var reader = new HexTableReader();
        var read = reader.Read(project, table, tables);
        if (!read.Validation.IsUsable)
        {
            throw new InvalidOperationException("Assignment table is not readable: " + read.Validation.TableStatus);
        }

        var sourceRow = read.Data.Rows.Cast<DataRow>()
            .FirstOrDefault(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == rowId)
            ?? throw new InvalidOperationException($"Row {rowId} was not found in {table.TableName}.");
        var originalValue = Convert.ToInt32(sourceRow[columnName], CultureInfo.InvariantCulture);
        var newValue = requestedNewValue ?? ChooseDifferentUInt16Value(originalValue);
        if (newValue is < 0 or > ushort.MaxValue)
        {
            throw new InvalidOperationException("New assignment value must fit UInt16.");
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var experimentId = $"ImageAssignerOracle_{normalized}_{rowId}_{stamp}";
        var experimentRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", experimentId);
        Directory.CreateDirectory(experimentRoot);

        var beforeRoot = CreateNamedTestCopy(project, experimentRoot, "before");
        var officialRoot = CreateNamedTestCopy(project, experimentRoot, "official_case");
        var cczRoot = CreateNamedTestCopy(project, experimentRoot, "ccz_case");

        var officialOffset = ResolveOfficialAssignmentOffset(profile.Config, normalized, rowId);
        var officialFile = Path.Combine(officialRoot, "Ekd5.exe");
        WriteUInt16AtOffset(officialFile, officialOffset, newValue);

        var cczProject = new ProjectDetector().CreateProjectFromGameRoot(cczRoot);
        var cczTables = new HexTableParser().Load(cczProject.HexTableXmlPath);
        var cczTable = ResolveAssignmentTable(cczProject, cczTables, normalized);
        var cczColumnName = ResolveAssignmentColumn(cczTable, normalized);
        var cczRead = reader.Read(cczProject, cczTable, cczTables);
        var cczRow = cczRead.Data.Rows.Cast<DataRow>()
            .FirstOrDefault(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == rowId)
            ?? throw new InvalidOperationException($"Row {rowId} was not found in CCZ test copy {cczTable.TableName}.");
        cczRow[cczColumnName] = newValue;
        new HexTableWriter().SaveToTestCopy(cczProject, cczTable, cczRead.Data);

        var reread = reader.Read(cczProject, cczTable, cczTables);
        var rereadRow = reread.Data.Rows.Cast<DataRow>()
            .First(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == rowId);
        var rereadValue = Convert.ToInt32(rereadRow[cczColumnName], CultureInfo.InvariantCulture);
        var cczOffset = cczTable.DataPos + ((long)(rowId - cczTable.BeginId) * cczTable.RowSize);
        var diff = CompareOutputs(beforeRoot, officialRoot, cczRoot);
        var target = diff.Files.FirstOrDefault(file => file.RelativePath.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase));
        var sameBytes = target?.Matches == true;
        var sameOffset = officialOffset == cczOffset;

        return new ImageAssignerOracleExperimentResult
        {
            ExperimentId = experimentId,
            ChangeKind = normalized,
            RowId = rowId,
            OriginalValue = originalValue,
            NewValue = newValue,
            BeforeRoot = beforeRoot,
            OfficialCaseRoot = officialRoot,
            CczCaseRoot = cczRoot,
            TargetFile = "Ekd5.exe",
            OfficialOffset = officialOffset,
            CczOffset = cczOffset,
            OfficialOffsetHex = HexDisplayFormatter.FormatOffset(officialOffset),
            CczOffsetHex = HexDisplayFormatter.FormatOffset(cczOffset),
            SameOffset = sameOffset,
            SameBytes = sameBytes,
            RereadMatches = rereadValue == newValue,
            OracleStatus = ResolveTableOracleStatus(project, table),
            TableName = table.TableName,
            ColumnName = columnName,
            DiffReport = diff,
            Evidence =
            [
                $"{normalized} row {rowId}: original={originalValue}, new={newValue}.",
                $"Official oracle write used System.ini offset {HexDisplayFormatter.FormatOffset(officialOffset)}.",
                $"CCZ write used {table.TableName} offset {HexDisplayFormatter.FormatOffset(cczOffset)}.",
                sameOffset ? "Official oracle offset and CCZ table offset are identical." : "Official oracle offset and CCZ table offset differ.",
                sameBytes ? "Official-case and CCZ-case Ekd5.exe bytes are identical after the edit." : "Official-case and CCZ-case Ekd5.exe bytes differ after the edit.",
                rereadValue == newValue ? "CCZ reread returned the expected value." : $"CCZ reread mismatch: expected={newValue}, actual={rereadValue}."
            ]
        };
    }

    public static bool IsImageAssignmentTable(HexTableDefinition table)
        => table.TableName.Contains("R形象", StringComparison.OrdinalIgnoreCase) ||
           table.TableName.Contains("S形象", StringComparison.OrdinalIgnoreCase) ||
           table.TableName.Contains("R褰", StringComparison.OrdinalIgnoreCase) ||
           table.TableName.Contains("S褰", StringComparison.OrdinalIgnoreCase);

    private static ImageAssignerOracleProfile BuildProfile(CczProject project, CczEngineProfile engine, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new ImageAssignerOracleProfile { Found = false };
        }

        var systemIni = Path.Combine(directory, "System.ini");
        var exe = ResolveExecutable(directory);
        var versionKind = InferOracleVersion(directory, exe);
        var config = ParseConfig(systemIni);
        var warnings = new List<string>();
        if (!File.Exists(systemIni)) warnings.Add("System.ini is missing: " + systemIni);
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) warnings.Add("Official image assigner executable is missing in " + directory);

        var length = TryGetLength(exe);
        if (versionKind == "6.5" && length is not null and not Expected65ExeLength)
        {
            warnings.Add($"6.5 image assigner executable length is {length.Value}, expected {Expected65ExeLength}.");
        }
        else if (versionKind == "6.6x" && length is not null and not Expected66xExeLength)
        {
            warnings.Add($"6.6x image assigner executable length is {length.Value}, expected {Expected66xExeLength}.");
        }

        var compatibility = ResolveCompatibility(engine, versionKind);
        if (compatibility == "CrossVersionOracle")
        {
            warnings.Add($"Detected engine {engine.VersionHint} is paired with official image assigner {versionKind}; use this oracle as read-only evidence.");
        }

        return new ImageAssignerOracleProfile
        {
            Found = true,
            VersionKind = versionKind,
            CompatibilityStatus = compatibility,
            DirectoryPath = Path.GetFullPath(directory),
            SystemIniPath = systemIni,
            ExecutablePath = exe,
            ExecutableLength = length,
            ExecutableSha256 = ComputeSha256(exe),
            Config = config,
            Dependencies = BuildDependencyStatuses(directory),
            Warnings = warnings
        };
    }

    private static IReadOnlyList<string> BuildCandidateDirectories(CczProject project, string preferred)
    {
        var result = new List<string>();
        AddIfDirectory(result, project.ImageAssignerDirectory);

        var preferredNames = preferred == "6.6x"
            ? new[] { "6.6x形象指定器", "形象指定器6.6", "形象指定器66x" }
            : new[] { "形象指定器6.5", "6.5形象指定器", "形象指定器65" };
        var fallbackNames = preferred == "6.6x"
            ? new[] { "形象指定器6.5", "6.5形象指定器", "形象指定器65" }
            : new[] { "6.6x形象指定器", "形象指定器6.6", "形象指定器66x" };

        foreach (var root in new[] { project.GameRoot, project.WorkspaceRoot, PortableInstallPaths.LegacyResourcesRoot }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var name in preferredNames.Concat(fallbackNames))
            {
                AddIfDirectory(result, Path.Combine(root, "老版游戏制作工具", "B形象指定器", name));
                AddIfDirectory(result, Path.Combine(root, "B形象指定器", name));
                AddIfDirectory(result, Path.Combine(root, name));
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddIfDirectory(List<string> result, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            result.Add(Path.GetFullPath(path));
        }
    }

    private static string ResolveExecutable(string directory)
    {
        if (!Directory.Exists(directory)) return string.Empty;
        var candidates = Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => Path.GetFileName(path).Contains("66", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(path => Path.GetFileName(path).Contains("65", StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return candidates.FirstOrDefault() ?? string.Empty;
    }

    private static string InferOracleVersion(string directory, string exePath)
    {
        var text = directory + " " + exePath;
        if (text.Contains("66", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("6.6", StringComparison.OrdinalIgnoreCase))
        {
            return "6.6x";
        }

        if (text.Contains("65", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("6.5", StringComparison.OrdinalIgnoreCase))
        {
            return "6.5";
        }

        return "unknown";
    }

    private static string ResolveCompatibility(CczEngineProfile engine, string oracleVersion)
    {
        if (oracleVersion == "unknown") return "Unknown";
        if (engine.VersionHint == "6.6") return oracleVersion == "6.6x" ? "Compatible" : "CrossVersionOracle";
        if (engine.VersionHint == "6.5") return oracleVersion == "6.5" ? "Compatible" : "CrossVersionOracle";
        return "Unknown";
    }

    private static ImageAssignerOracleConfig ParseConfig(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new ImageAssignerOracleConfig { Found = false, Path = path };
        }

        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var numeric = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var numericHex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var rawLine in File.ReadLines(path, EncodingService.Gbk))
        {
            var line = rawLine.Split(';')[0].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("[", StringComparison.Ordinal)) continue;
            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;
            raw[key] = value;
            if (TryParseOracleNumber(value, out var parsed))
            {
                numeric[key] = parsed;
                numericHex[key] = "0x" + parsed.ToString("X", CultureInfo.InvariantCulture);
            }
            else if (IsKnownNumericKey(key))
            {
                warnings.Add($"Key {key} has non-numeric value: {value}");
            }
        }

        var strategy = StrategyExtensionKeys
            .Where(numeric.ContainsKey)
            .ToDictionary(key => key, key => numeric[key], StringComparer.OrdinalIgnoreCase);

        return new ImageAssignerOracleConfig
        {
            Found = true,
            Path = Path.GetFullPath(path),
            RawValues = raw,
            NumericValues = numeric,
            NumericHexValues = numericHex,
            StrategyExtensionAddresses = strategy,
            ParseWarnings = warnings
        };
    }

    private static bool TryParseOracleNumber(string value, out long parsed)
    {
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
        }

        var looksHex = value.Length > 0 && value.All(Uri.IsHexDigit) && value.Any(char.IsLetter);
        if (looksHex)
        {
            return long.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool IsKnownNumericKey(string key)
        => key is "FileHead" or "RFileHead" or "UserXK" or "BzXG" or "SMagic" or "Xgh" or "SCount" or
           "DefID" or "AssID" or "MarkCount" or "CountBZ3" or "CountBZ1" or "Three" or "CountSV" ||
           StrategyExtensionKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<ImageAssignerOracleDependencyStatus> BuildDependencyStatuses(string directory)
        => DependencyRelativePaths.Select(relative =>
        {
            var path = Path.Combine(directory, relative);
            var info = new FileInfo(path);
            return new ImageAssignerOracleDependencyStatus
            {
                Name = relative.Replace(Path.DirectorySeparatorChar, '/'),
                Path = path,
                Exists = info.Exists,
                Length = info.Exists ? info.Length : null,
                Note = relative.EndsWith(".OCX", StringComparison.OrdinalIgnoreCase)
                    ? "VB6 grid dependency; may require registration for UI automation."
                    : string.Empty
            };
        }).ToList();

    private static ImageAssignerOracleCheck CompareTableOffset(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        string semanticName,
        string configKey,
        long fallbackExpected)
    {
        var profile = new OfficialImageAssignerOracleService().Detect(project);
        var expected = GetNumeric(profile.Config, configKey) ?? fallbackExpected;
        var table = tables.FirstOrDefault(item => MatchesSemanticTable(item, semanticName));
        if (table == null)
        {
            return new ImageAssignerOracleCheck
            {
                Key = semanticName,
                Status = "TableMissing",
                Expected = $"Ekd5.exe@{HexDisplayFormatter.FormatOffset(expected)}",
                Actual = "<missing>",
                Detail = $"No HexTable matching {semanticName} was found."
            };
        }

        var actual = $"{table.FileName}@{HexDisplayFormatter.FormatOffset(table.DataPos)}";
        var ok = table.FileName.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) && table.DataPos == expected;
        return new ImageAssignerOracleCheck
        {
            Key = semanticName,
            Status = ok ? "MatchedOfficialImageAssigner" : "OffsetMismatch",
            Expected = $"Ekd5.exe@{HexDisplayFormatter.FormatOffset(expected)}",
            Actual = actual,
            Detail = $"Oracle key {configKey}; table={table.TableName}; project={project.GameRoot}"
        };
    }

    private static ImageAssignerOracleCheck CompareConfigValue(ImageAssignerOracleConfig config, string key, long expected, string detail)
    {
        var actual = GetNumeric(config, key);
        var status = actual == expected ? "MatchedOfficialImageAssigner" : actual == null ? "ConfigMissing" : "ConfigMismatch";
        return new ImageAssignerOracleCheck
        {
            Key = key,
            Status = status,
            Expected = expected.ToString(CultureInfo.InvariantCulture),
            Actual = actual?.ToString(CultureInfo.InvariantCulture) ?? "<missing>",
            Detail = detail
        };
    }

    private static bool MatchesSemanticTable(HexTableDefinition table, string semanticName)
    {
        if (semanticName == "R形象")
        {
            return table.TableName.Contains("R形象", StringComparison.OrdinalIgnoreCase) ||
                   table.TableName.Contains("R褰", StringComparison.OrdinalIgnoreCase);
        }

        if (semanticName == "S形象")
        {
            return table.TableName.Contains("S形象", StringComparison.OrdinalIgnoreCase) ||
                   table.TableName.Contains("S褰", StringComparison.OrdinalIgnoreCase);
        }

        if (semanticName == "兵种相克")
        {
            return table.TableName.Contains("兵种相克", StringComparison.OrdinalIgnoreCase) ||
                   table.TableName.Contains("相克", StringComparison.OrdinalIgnoreCase) ||
                   table.TableName.Contains("鐩稿厠", StringComparison.OrdinalIgnoreCase);
        }

        return table.TableName.Contains(semanticName, StringComparison.OrdinalIgnoreCase);
    }

    private static long? GetNumeric(ImageAssignerOracleConfig config, string key)
        => config.NumericValues.TryGetValue(key, out var value) ? value : null;

    private static string ResolveOverallStatus(ImageAssignerOracleProfile profile, IReadOnlyList<ImageAssignerOracleCheck> checks)
    {
        if (!profile.Found || !profile.Config.Found) return "ConfigMissing";
        if (profile.CompatibilityStatus == "CrossVersionOracle") return "CrossVersionOracle";
        if (checks.Any(check => check.Status == "OffsetMismatch")) return "OffsetMismatch";
        return "MatchedOfficialImageAssigner";
    }

    private static string NormalizeChangeKind(string changeKind)
    {
        if (string.IsNullOrWhiteSpace(changeKind)) return "image_assignment";
        var normalized = changeKind.Trim().ToLowerInvariant();
        if (normalized.Contains("r") && normalized.Contains("image")) return "r_image_assignment";
        if (normalized.Contains("s") && normalized.Contains("image")) return "s_image_assignment";
        if (normalized.Contains("face")) return "face_assignment";
        if (normalized.Contains("global")) return "global_numeric_setting";
        if (normalized.Contains("item")) return "item_boundary_or_item_setting";
        return normalized;
    }

    private static IReadOnlyList<string> BuildExpectedRanges(string changeKind, int? rowId)
    {
        var row = rowId.GetValueOrDefault(0);
        return changeKind switch
        {
            "r_image_assignment" => [$"Ekd5.exe@{HexDisplayFormatter.FormatOffset(ExpectedRImageOffset + row * 2L)} length=2"],
            "s_image_assignment" => [$"Ekd5.exe@{HexDisplayFormatter.FormatOffset(ExpectedSImageOffset + row * 2L)} length=2"],
            "face_assignment" => ["Data.e5 person table face field; exact offset comes from HexTable person row and face column."],
            "global_numeric_setting" => ["Unknown until official output diff identifies candidate offset."],
            _ => ["Depends on selected feature; compare official_case and ccz_case against before."]
        };
    }

    private static IReadOnlyList<string> BuildOfficialObservationSteps(string changeKind, int? rowId)
        =>
        [
            "Open the official image assigner from the returned OfficialToolPath.",
            "Point the official tool at the official_case test copy, never the original project.",
            $"Apply the requested {changeKind} edit" + (rowId.HasValue ? $" for row/person {rowId.Value}." : "."),
            "Save from the official tool and close it before running compare_image_assigner_output."
        ];

    private static IReadOnlyList<string> BuildCczSteps(string changeKind, int? rowId)
        =>
        [
            "Apply the same intended edit through CCZModStudio/MCP on the ccz_case test copy.",
            "Keep the generated backup and write report.",
            "Reread the edited table or setting through MCP before diff comparison."
        ];

    private static object LaunchOfficialToolReadOnly(ImageAssignerOracleProfile profile, bool includeWindowProbe)
    {
        if (string.IsNullOrWhiteSpace(profile.ExecutablePath) || !File.Exists(profile.ExecutablePath))
        {
            return new { Started = false, Reason = "Official image assigner executable not found." };
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = profile.ExecutablePath,
            WorkingDirectory = profile.DirectoryPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Minimized
        });
        if (process == null) return new { Started = false, Reason = "Process.Start returned null." };

        Thread.Sleep(includeWindowProbe ? 1200 : 300);
        process.Refresh();
        return new
        {
            Started = true,
            process.Id,
            process.ProcessName,
            HasExited = process.HasExited,
            MainWindowTitle = includeWindowProbe && !process.HasExited ? process.MainWindowTitle : string.Empty,
            Note = "Read-only launch smoke; no UI save action was performed."
        };
    }

    private static bool IsCoreOracleDiffTarget(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var file = Path.GetFileName(normalized);
        return file.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("Data.e5", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("Star.e5", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("Imsg.e5", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAssignmentExperimentKind(string changeKind)
    {
        var normalized = NormalizeChangeKind(changeKind);
        return normalized switch
        {
            "r_image_assignment" => normalized,
            "s_image_assignment" => normalized,
            _ => throw new InvalidOperationException("Only r_image_assignment and s_image_assignment are supported by this automated oracle write experiment.")
        };
    }

    private static HexTableDefinition ResolveAssignmentTable(CczProject project, IReadOnlyList<HexTableDefinition> tables, string normalized)
        => normalized == "r_image_assignment"
            ? HexTableNameResolver.ResolveForProject(project, tables, "6.5-0-4 R形象")
            : HexTableNameResolver.ResolveForProject(project, tables, "6.5-0-5 S形象");

    private static string ResolveAssignmentColumn(HexTableDefinition table, string normalized)
    {
        var preferred = normalized == "r_image_assignment" ? "R形象编号" : "S形象编号";
        var column = table.Fields.FirstOrDefault(field => field.ColumnName.Equals(preferred, StringComparison.OrdinalIgnoreCase))?.ColumnName;
        if (!string.IsNullOrWhiteSpace(column)) return column;

        column = table.Fields.FirstOrDefault(field =>
            normalized == "r_image_assignment"
                ? field.ColumnName.Contains("R", StringComparison.OrdinalIgnoreCase)
                : field.ColumnName.Contains("S", StringComparison.OrdinalIgnoreCase))?.ColumnName;
        if (!string.IsNullOrWhiteSpace(column)) return column;

        throw new InvalidOperationException($"Could not resolve assignment column for {table.TableName}.");
    }

    private static int ChooseDifferentUInt16Value(int originalValue)
    {
        var candidate = originalValue + 1;
        return candidate <= ushort.MaxValue ? candidate : Math.Max(0, originalValue - 1);
    }

    private static long ResolveOfficialAssignmentOffset(ImageAssignerOracleConfig config, string normalized, int rowId)
    {
        var baseOffset = normalized == "r_image_assignment"
            ? GetNumeric(config, "RFileHead") ?? ExpectedRImageOffset
            : GetNumeric(config, "FileHead") ?? ExpectedSImageOffset;
        return baseOffset + rowId * 2L;
    }

    private static void WriteUInt16AtOffset(string filePath, long offset, int value)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (offset < 0 || offset + 2 > bytes.Length)
        {
            throw new InvalidOperationException($"Official oracle write offset is out of range: {filePath}@{HexDisplayFormatter.FormatOffset(offset)}.");
        }

        var encoded = BitConverter.GetBytes((ushort)value);
        bytes[offset] = encoded[0];
        bytes[offset + 1] = encoded[1];
        File.WriteAllBytes(filePath, bytes);
    }

    private static string CreateNamedTestCopy(CczProject project, string experimentRoot, string name)
    {
        var temp = new BackupManager().CreateTestCopy(project);
        var target = Path.Combine(experimentRoot, name);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.Move(temp, target);
        File.AppendAllText(
            Path.Combine(target, "_CCZModStudio_TestCopy.txt"),
            $"ExperimentCase={name}{Environment.NewLine}",
            EncodingService.Gbk);
        return target;
    }

    private static ImageAssignerFileDiffComparison CompareOneFile(string beforeRoot, string officialAfterRoot, string cczAfterRoot, string relative)
    {
        var before = Path.Combine(beforeRoot, relative);
        var official = Path.Combine(officialAfterRoot, relative);
        var ccz = Path.Combine(cczAfterRoot, relative);
        var beforeExists = File.Exists(before);
        var officialExists = File.Exists(official);
        var cczExists = File.Exists(ccz);
        var officialRanges = beforeExists && officialExists ? BuildChangedRanges(File.ReadAllBytes(before), File.ReadAllBytes(official)) : [];
        var cczRanges = beforeExists && cczExists ? BuildChangedRanges(File.ReadAllBytes(before), File.ReadAllBytes(ccz)) : [];
        var officialSha = ComputeSha256(official);
        var cczSha = ComputeSha256(ccz);
        return new ImageAssignerFileDiffComparison
        {
            RelativePath = relative,
            OfficialStatus = officialExists ? (officialRanges.Count == 0 ? "Unchanged" : "Changed") : "Missing",
            CczStatus = cczExists ? (cczRanges.Count == 0 ? "Unchanged" : "Changed") : "Missing",
            Matches = officialExists == cczExists && officialSha == cczSha,
            BeforeSha256 = ComputeSha256(before),
            OfficialSha256 = officialSha,
            CczSha256 = cczSha,
            OfficialChangedRanges = officialRanges,
            CczChangedRanges = cczRanges
        };
    }

    private static IReadOnlyList<ImageAssignerChangedRange> BuildChangedRanges(byte[] before, byte[] after)
    {
        var ranges = new List<ImageAssignerChangedRange>();
        var max = Math.Max(before.Length, after.Length);
        var index = 0;
        while (index < max)
        {
            var same = index < before.Length && index < after.Length && before[index] == after[index];
            if (same)
            {
                index++;
                continue;
            }

            var start = index;
            while (index < max)
            {
                var innerSame = index < before.Length && index < after.Length && before[index] == after[index];
                if (innerSame) break;
                index++;
            }

            var length = index - start;
            ranges.Add(new ImageAssignerChangedRange
            {
                Offset = start,
                Length = length,
                OffsetHex = HexDisplayFormatter.FormatOffset(start),
                EndOffsetHex = HexDisplayFormatter.FormatOffset(start + length)
            });
        }

        return ranges;
    }

    private static long? TryGetLength(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : new FileInfo(path).Length;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeSha256(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return string.Empty;
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return string.Empty;
        }
    }
}
