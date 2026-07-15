using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class AbilityTierPatchService
{
    private const string TargetFileName = "Ekd5.exe";
    private const long CalcFunctionVa = 0x0041A534;
    private const long ClampVa = 0x00406CB4;
    private const long DisplayPointerVa = 0x00478205;
    private const long DefaultDisplayTableVa = 0x00472DD7;
    private const long DefaultLabelStartVa = 0x0048C522;
    private const long DefaultLabelStartOffset = 0x08AF22;
    private const int MaxMergeTierCount = 7;

    private static readonly long[] ExpectedCallSiteVas =
    [
        0x00407461,
        0x00407D48,
        0x00408B28,
        0x00436E32,
        0x004781F3
    ];

    private static readonly (string Label, int Tier, long CompareVa, long ThresholdVa, long MovVa, long ReturnVa)[] KnownBranchMap =
    [
        ("Z", 7, 0x0041A542, 0x0041A543, 0x0041A546, 0x0041A547),
        ("V", 6, 0x0041A54A, 0x0041A54B, 0x0041A54E, 0x0041A54F),
        ("X", 5, 0x0041A552, 0x0041A553, 0x0041A556, 0x0041A557),
        ("S", 4, 0x0041A55A, 0x0041A55B, 0x0041A55E, 0x0041A55F),
        ("A", 3, 0x0041A562, 0x0041A563, 0x0041A566, 0x0041A567),
        ("B", 2, 0x0041A56A, 0x0041A56B, 0x0041A56E, 0x0041A56F)
    ];

    private static readonly (string Purpose, long Va, int Length)[] CaveProbeMap =
    [
        ("旧帖能力函数代码洞候选", 0x004748F0, 0x70),
        ("旧帖显示表候选", 0x00474961, 0x60)
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly WriteOperationReportService _reportService = new();

    public AbilityTierScanReport Scan(CczProject project, bool writeReport = true)
    {
        ArgumentNullException.ThrowIfNull(project);

        var exePath = project.ResolveGameFile(TargetFileName);
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("Cannot find Ekd5.exe for ability tier scan.", exePath);
        }

        var bytes = File.ReadAllBytes(exePath);
        var pe = PeMap.Parse(bytes);
        var report = new AbilityTierScanReport
        {
            ProjectRoot = project.WorkspaceRoot,
            GameRoot = project.GameRoot,
            ExePath = exePath,
            ExeSha256 = WriteOperationReportService.ComputeSha256(bytes),
            ImageBase = pe.ImageBase,
            Status = "Scanned",
            PatchModeRecommendation = "MergeOriginalBranches"
        };

        AddThresholdAndReturnRules(report, bytes, pe);
        AddCallSites(report, bytes, pe);
        AddClamp(report, bytes, pe);
        AddDisplayPointerAndLabels(report, bytes, pe);
        AddCaveCandidates(report, bytes, pe);
        EvaluateScan(report);

        if (writeReport)
        {
            var root = CreateReportRoot(project, "ability-tier-query");
            report.ReportPath = Path.Combine(root, "ability-tier-query-report.json");
            File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, JsonOptions));
        }

        return report;
    }

    public AbilityTierProfile BuildDefaultProfile(int tierCount, string displayMode = "Letter")
    {
        if (tierCount is < 4 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(tierCount), "Ability tier count must be between 4 and 10.");
        }

        var labels = displayMode.Equals("Number", StringComparison.OrdinalIgnoreCase)
            ? Enumerable.Range(1, tierCount).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToList()
            : BuildDefaultLetterLabels(tierCount).ToList();

        return new AbilityTierProfile
        {
            ProfileName = $"Default{tierCount}Tier{displayMode}",
            TierCount = tierCount,
            DisplayMode = displayMode,
            Labels = labels,
            PatchMode = tierCount <= MaxMergeTierCount ? "MergeOriginalBranches" : "RelocateCalculationFunction"
        };
    }

    public AbilityTierPatchPreview Preview(CczProject project, AbilityTierProfile profile, bool writeReport = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);

        var scan = Scan(project, writeReport: false);
        var preview = new AbilityTierPatchPreview
        {
            ProjectRoot = project.WorkspaceRoot,
            GameRoot = project.GameRoot,
            ExePath = scan.ExePath,
            ExeSha256 = scan.ExeSha256,
            RequestedProfile = profile,
            Status = "Previewed"
        };

        ValidateProfile(profile, preview.Warnings);
        if (!scan.CanPatchMergeProfiles)
        {
            preview.Warnings.Add("Current Ekd5.exe signature is not eligible for automatic merge-profile patching.");
        }

        if (profile.TierCount > MaxMergeTierCount)
        {
            preview.Status = "RequiresRelocation";
            preview.CanWrite = false;
            preview.Warnings.Add("8-10 tier profiles require calculation-function relocation; first implementation keeps this as preview-only.");
        }
        else if (preview.Warnings.Count == 0)
        {
            AddMergeProfileChanges(scan, profile, preview);
            preview.CanWrite = preview.Warnings.Count == 0;
            preview.Status = preview.CanWrite ? "Previewed" : "Blocked";
        }
        else
        {
            preview.Status = "Blocked";
            preview.CanWrite = false;
        }

        if (writeReport)
        {
            var root = CreateReportRoot(project, "ability-tier-preview");
            preview.ReportPath = Path.Combine(root, "ability-tier-preview-report.json");
            File.WriteAllText(preview.ReportPath, JsonSerializer.Serialize(preview, JsonOptions));
        }

        return preview;
    }

    public AbilityTierPatchWriteResult Write(CczProject project, AbilityTierProfile profile)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);

        var preview = Preview(project, profile, writeReport: false);
        if (!preview.CanWrite)
        {
            throw new InvalidOperationException("Ability tier profile is not writable: " + string.Join("; ", preview.Warnings));
        }

        ProjectVersionGuardService.EnsureCoreFileCompatibleForWrite(project, TargetFileName);

        var exePath = project.ResolveGameFile(TargetFileName);
        var beforeBytes = File.ReadAllBytes(exePath);
        var beforeSha = WriteOperationReportService.ComputeSha256(beforeBytes);
        if (!beforeSha.Equals(preview.ExeSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ekd5.exe changed between preview and write; rescan before writing.");
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backupRoot = Path.Combine(ProjectBackupPathService.EnsureBackupRootWritable(project), "AbilityTier");
        Directory.CreateDirectory(backupRoot);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_Ekd5.exe.bak");
        File.Copy(exePath, backupPath, overwrite: false);

        var afterBytes = beforeBytes.ToArray();
        foreach (var change in preview.Changes)
        {
            var offset = checked((int)change.FileOffset);
            var oldBytes = Convert.FromHexString(change.OldBytesHex);
            var newBytes = Convert.FromHexString(change.NewBytesHex);
            if (!afterBytes.AsSpan(offset, oldBytes.Length).SequenceEqual(oldBytes))
            {
                throw new InvalidOperationException($"Ability tier write signature mismatch at 0x{change.FileOffset:X}.");
            }

            newBytes.CopyTo(afterBytes.AsSpan(offset));
        }

        File.WriteAllBytes(exePath, afterBytes);
        var afterSha = WriteOperationReportService.ComputeSha256(afterBytes);
        var readBack = Scan(project, writeReport: false);
        AssertReadBack(profile, readBack);

        var report = new WriteOperationReport
        {
            OperationKind = "AbilityTierPatch",
            SourceAction = "write_ability_tier_profile",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = TargetFileName,
            TargetPath = exePath,
            BackupPath = backupPath,
            BeforeSha256 = beforeSha,
            AfterSha256 = afterSha,
            ChangedBytes = CountChangedBytes(beforeBytes, afterBytes),
            Summary = $"Ability tier profile applied: {profile.TierCount} tiers, labels={string.Join("/", profile.Labels)}.",
            SafetyNotes = "Signature-checked Ekd5.exe offsets only; backup created before write; read-back profile verified after write.",
            FormatCheckSummary = "PE mapping and known 6.5 ability-tier signatures validated before patch.",
            RiskSummary = profile.TierCount <= MaxMergeTierCount
                ? "Merge-profile patch only; high-tier relocation remains disabled."
                : "Relocation profile should not reach write path."
        };

        foreach (var change in preview.Changes)
        {
            report.Changes.Add(new WriteOperationChange
            {
                Category = change.Category,
                TableName = "AbilityTier",
                OffsetHex = "0x" + change.FileOffset.ToString("X"),
                ByteLength = change.ByteLength,
                OldValue = change.OldBytesHex,
                NewValue = change.NewBytesHex,
                Annotation = $"{change.Purpose} VA=0x{change.Va:X8}"
            });
        }

        report.Metadata["TierCount"] = profile.TierCount.ToString(CultureInfo.InvariantCulture);
        report.Metadata["Labels"] = string.Join("/", profile.Labels);
        report.Metadata["PatchMode"] = profile.PatchMode;
        var reportJsonPath = _reportService.WriteJsonReport(report, backupPath);

        return new AbilityTierPatchWriteResult
        {
            ProjectRoot = project.WorkspaceRoot,
            GameRoot = project.GameRoot,
            ExePath = exePath,
            BackupPath = backupPath,
            ReportJsonPath = reportJsonPath,
            BeforeSha256 = beforeSha,
            AfterSha256 = afterSha,
            ChangedBytes = report.ChangedBytes,
            RequestedProfile = profile,
            ReadBack = readBack,
            Changes = preview.Changes
        };
    }

    private static void AddThresholdAndReturnRules(AbilityTierScanReport report, byte[] bytes, PeMap pe)
    {
        foreach (var branch in KnownBranchMap)
        {
            var compareOffset = pe.VaToOffset(branch.CompareVa);
            var thresholdOffset = pe.VaToOffset(branch.ThresholdVa);
            var movOffset = pe.VaToOffset(branch.MovVa);
            var returnOffset = pe.VaToOffset(branch.ReturnVa);
            if (!IsReadable(bytes, compareOffset, 2) ||
                !IsReadable(bytes, movOffset, 2) ||
                bytes[compareOffset] != 0x3C ||
                bytes[movOffset] != 0xB0)
            {
                report.Warnings.Add($"Ability tier branch signature mismatch: {branch.Label}.");
                continue;
            }

            report.ThresholdRules.Add(new AbilityTierThresholdRule
            {
                Label = branch.Label,
                Tier = branch.Tier,
                CompareInstructionVa = branch.CompareVa,
                ThresholdVa = branch.ThresholdVa,
                FileOffset = thresholdOffset,
                Threshold = bytes[thresholdOffset]
            });
            report.ReturnRules.Add(new AbilityTierReturnRule
            {
                Label = branch.Label,
                Tier = branch.Tier,
                MovInstructionVa = branch.MovVa,
                ReturnValueVa = branch.ReturnVa,
                FileOffset = returnOffset,
                ReturnValue = bytes[returnOffset]
            });
        }
    }

    private static void AddCallSites(AbilityTierScanReport report, byte[] bytes, PeMap pe)
    {
        foreach (var va in ExpectedCallSiteVas)
        {
            var offset = pe.VaToOffset(va);
            if (!IsReadable(bytes, offset, 5) || bytes[offset] != 0xE8)
            {
                report.Warnings.Add($"Ability tier call-site signature mismatch at 0x{va:X8}.");
                continue;
            }

            var rel = BitConverter.ToInt32(bytes, checked((int)offset + 1));
            var target = va + 5 + rel;
            report.CallSites.Add(new AbilityTierPatchPoint
            {
                Purpose = "calc_attr_tier call site",
                Va = va,
                FileOffset = offset,
                ByteLength = 5,
                BytesHex = ToHex(bytes, offset, 5),
                TargetVa = target
            });
            if (target != CalcFunctionVa)
            {
                report.Warnings.Add($"Ability tier call-site at 0x{va:X8} targets 0x{target:X8}, expected 0x{CalcFunctionVa:X8}.");
            }
        }
    }

    private static void AddClamp(AbilityTierScanReport report, byte[] bytes, PeMap pe)
    {
        var offset = pe.VaToOffset(ClampVa);
        if (!IsReadable(bytes, offset, 6) ||
            bytes[offset] != 0x3C ||
            bytes[offset + 2] != 0x72 ||
            bytes[offset + 3] != 0x02 ||
            bytes[offset + 4] != 0xB0)
        {
            report.Warnings.Add("Ability tier clamp signature mismatch.");
            return;
        }

        report.ClampSite = new AbilityTierPatchPoint
        {
            Purpose = "tier clamp",
            Va = ClampVa,
            FileOffset = offset,
            ByteLength = 6,
            BytesHex = ToHex(bytes, offset, 6)
        };
    }

    private static void AddDisplayPointerAndLabels(AbilityTierScanReport report, byte[] bytes, PeMap pe)
    {
        var offset = pe.VaToOffset(DisplayPointerVa);
        if (!IsReadable(bytes, offset, 5) || bytes[offset] != 0xBA)
        {
            report.Warnings.Add("Ability tier display pointer signature mismatch.");
            return;
        }

        var tableVa = BitConverter.ToUInt32(bytes, checked((int)offset + 1));
        var tableOffset = pe.TryVaToOffset(tableVa);
        if (tableOffset == null)
        {
            report.Warnings.Add($"Ability tier display table VA cannot be mapped: 0x{tableVa:X8}.");
            return;
        }

        report.DisplayPointer = new AbilityTierDisplayPointer
        {
            InstructionVa = DisplayPointerVa,
            FileOffset = offset,
            TableVa = tableVa,
            TableFileOffset = tableOffset.Value,
            InstructionBytesHex = ToHex(bytes, offset, 5)
        };

        ReadDisplayLabels(report, bytes, pe, tableOffset.Value);
    }

    private static void ReadDisplayLabels(AbilityTierScanReport report, byte[] bytes, PeMap pe, long tableOffset)
    {
        for (var index = 1; index <= 10; index++)
        {
            var pointerOffset = tableOffset + index * 4L;
            if (!IsReadable(bytes, pointerOffset, 4))
            {
                break;
            }

            var labelVa = BitConverter.ToUInt32(bytes, checked((int)pointerOffset));
            var labelOffset = pe.TryVaToOffset(labelVa);
            if (labelOffset == null || !IsReadable(bytes, labelOffset.Value, 1))
            {
                break;
            }

            var label = ReadNullTerminatedAscii(bytes, labelOffset.Value, maxBytes: 16);
            if (string.IsNullOrEmpty(label))
            {
                break;
            }

            report.DisplayLabels.Add(label);
            report.DisplayPointer?.LabelPointers.Add(new AbilityTierDisplayLabelPointer
            {
                Tier = index,
                PointerVa = (report.DisplayPointer.TableVa + index * 4L),
                PointerFileOffset = pointerOffset,
                LabelVa = labelVa,
                LabelFileOffset = labelOffset.Value,
                Label = label
            });
        }
    }

    private static void AddCaveCandidates(AbilityTierScanReport report, byte[] bytes, PeMap pe)
    {
        foreach (var cave in CaveProbeMap)
        {
            var offset = pe.TryVaToOffset(cave.Va);
            if (offset == null || !IsReadable(bytes, offset.Value, cave.Length))
            {
                continue;
            }

            report.CaveCandidates.Add(new AbilityTierCaveCandidate
            {
                Purpose = cave.Purpose,
                Va = cave.Va,
                FileOffset = offset.Value,
                Length = cave.Length,
                IsAllNop = bytes.AsSpan(checked((int)offset.Value), cave.Length).ToArray().All(value => value == 0x90)
            });
        }
    }

    private static void EvaluateScan(AbilityTierScanReport report)
    {
        report.EngineTierCapacity = report.ReturnRules.Count == 0 ? 0 : report.ReturnRules.Max(rule => rule.ReturnValue);
        report.EffectiveTierCount = Math.Min(report.EngineTierCapacity, report.DisplayLabels.Count);
        if (report.EngineTierCapacity == 7 &&
            report.DisplayLabels.Count >= 7 &&
            report.DisplayLabels.Take(7).SequenceEqual(["C", "B", "A", "S", "X", "V", "Z"], StringComparer.Ordinal))
        {
            report.PatchModeRecommendation = "MergeOriginalBranches";
        }

        var signaturesOk =
            report.ThresholdRules.Count == KnownBranchMap.Length &&
            report.ReturnRules.Count == KnownBranchMap.Length &&
            report.CallSites.Count == ExpectedCallSiteVas.Length &&
            report.CallSites.All(site => site.TargetVa == CalcFunctionVa) &&
            report.ClampSite != null &&
            report.DisplayPointer != null;
        report.CanPatchMergeProfiles = signaturesOk && report.Warnings.Count == 0;
        if (!report.CanPatchMergeProfiles && report.Warnings.Count == 0)
        {
            report.Warnings.Add("Ability tier scan did not collect enough patch metadata for automatic write.");
        }
    }

    private static void ValidateProfile(AbilityTierProfile profile, List<string> warnings)
    {
        if (profile.TierCount is < 4 or > 10)
        {
            warnings.Add("TierCount must be between 4 and 10.");
        }

        if (profile.Labels.Count != profile.TierCount)
        {
            warnings.Add($"Label count must equal TierCount ({profile.TierCount}).");
        }

        var gbk = EncodingService.Gbk;
        foreach (var label in profile.Labels)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                warnings.Add("Tier labels must not be empty.");
                continue;
            }

            if (label.Contains('\0', StringComparison.Ordinal))
            {
                warnings.Add("Tier labels must not contain NUL characters.");
            }

            var bytes = gbk.GetBytes(label);
            if (bytes.Length > 8)
            {
                warnings.Add($"Tier label is too long for first implementation: {label}");
            }
        }
    }

    private static void AddMergeProfileChanges(
        AbilityTierScanReport scan,
        AbilityTierProfile profile,
        AbilityTierPatchPreview preview)
    {
        foreach (var returnRule in scan.ReturnRules)
        {
            var newReturn = Math.Min(returnRule.Tier, profile.TierCount);
            if (returnRule.ReturnValue == newReturn)
            {
                continue;
            }

            preview.Changes.Add(new AbilityTierByteChange
            {
                Category = "ReturnMerge",
                Purpose = $"{returnRule.Label} branch return -> tier {newReturn}",
                Va = returnRule.ReturnValueVa,
                FileOffset = returnRule.FileOffset,
                OldBytesHex = returnRule.ReturnValueHex,
                NewBytesHex = ((byte)newReturn).ToString("X2"),
                ByteLength = 1
            });
        }

        var clamp = scan.ClampSite ?? throw new InvalidOperationException("Scan result has no clamp site.");
        var oldClamp = Convert.FromHexString(clamp.BytesHex);
        var newClamp = new byte[] { 0x3C, checked((byte)(profile.TierCount + 1)), 0x72, 0x02, 0xB0, checked((byte)profile.TierCount) };
        if (!oldClamp.SequenceEqual(newClamp))
        {
            preview.Changes.Add(new AbilityTierByteChange
            {
                Category = "Clamp",
                Purpose = $"Clamp max tier to {profile.TierCount}",
                Va = clamp.Va,
                FileOffset = clamp.FileOffset,
                OldBytesHex = Convert.ToHexString(oldClamp),
                NewBytesHex = Convert.ToHexString(newClamp),
                ByteLength = newClamp.Length
            });
        }

        AddDisplayLabelChanges(scan, profile, preview);
    }

    private static void AddDisplayLabelChanges(
        AbilityTierScanReport scan,
        AbilityTierProfile profile,
        AbilityTierPatchPreview preview)
    {
        if (scan.DisplayLabels.Count < profile.TierCount)
        {
            preview.Warnings.Add("Current display table exposes fewer labels than requested; relocation display table is required.");
            preview.CanWrite = false;
            return;
        }

        _ = scan.DisplayPointer?.TableFileOffset
            ?? throw new InvalidOperationException("Scan result has no display table pointer.");
        if (scan.DisplayPointer.TableVa != DefaultDisplayTableVa)
        {
            preview.Warnings.Add($"Current display table is 0x{scan.DisplayPointer.TableVa:X8}; first implementation only edits the default 0x{DefaultDisplayTableVa:X8} table in place.");
            preview.CanWrite = false;
            return;
        }

        var gbk = EncodingService.Gbk;
        for (var tierIndex = 1; tierIndex <= profile.TierCount; tierIndex++)
        {
            var label = profile.Labels[tierIndex - 1];
            var labelBytes = gbk.GetBytes(label).Concat(new byte[] { 0 }).ToArray();
            // First implementation only writes existing single-byte labels in place.
            if (labelBytes.Length != 2)
            {
                preview.Warnings.Add("Multi-byte or multi-character labels require a relocated display table; preview remains read-only for label: " + label);
                preview.CanWrite = false;
                continue;
            }
        }

        if (preview.Warnings.Count != 0)
        {
            return;
        }

        // Current 6.5 default labels live as C\0B\0A\0S\0X\0V\0Z\0. Keep v1 scoped to that verified layout.
        for (var i = 0; i < profile.TierCount; i++)
        {
            var newByte = gbk.GetBytes(profile.Labels[i])[0];
            var labelPointer = scan.DisplayPointer!.LabelPointers.SingleOrDefault(pointer => pointer.Tier == i + 1);
            var offset = labelPointer?.LabelFileOffset ?? DefaultLabelStartOffset + i * 2L;
            var va = labelPointer?.LabelVa ?? DefaultLabelStartVa + i * 2L;
            var oldLabel = i < scan.DisplayLabels.Count ? scan.DisplayLabels[i] : string.Empty;
            var oldByte = string.IsNullOrEmpty(oldLabel) ? (byte)0 : gbk.GetBytes(oldLabel)[0];
            if (oldByte == newByte)
            {
                continue;
            }

            preview.Changes.Add(new AbilityTierByteChange
            {
                Category = "DisplayLabel",
                Purpose = $"Tier {i + 1} label {oldLabel} -> {profile.Labels[i]}",
                Va = va,
                FileOffset = offset,
                OldBytesHex = oldByte.ToString("X2"),
                NewBytesHex = newByte.ToString("X2"),
                ByteLength = 1
            });
        }
    }

    private static void AssertReadBack(AbilityTierProfile profile, AbilityTierScanReport readBack)
    {
        if (!readBack.CanPatchMergeProfiles)
        {
            throw new InvalidOperationException("Ability tier patch read-back signature check failed.");
        }

        var maxReturn = readBack.ReturnRules.Max(rule => rule.ReturnValue);
        if (maxReturn > profile.TierCount)
        {
            throw new InvalidOperationException($"Ability tier read-back returned max tier {maxReturn}, expected <= {profile.TierCount}.");
        }

        var clampBytes = readBack.ClampSite?.BytesHex ?? string.Empty;
        var expectedClamp = Convert.ToHexString(new byte[] { 0x3C, checked((byte)(profile.TierCount + 1)), 0x72, 0x02, 0xB0, checked((byte)profile.TierCount) });
        if (!clampBytes.Equals(expectedClamp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Ability tier clamp read-back mismatch: {clampBytes} != {expectedClamp}.");
        }
    }

    private static IEnumerable<string> BuildDefaultLetterLabels(int tierCount)
    {
        var defaults = new[] { "C", "B", "A", "S", "X", "V", "Z" };
        if (tierCount <= defaults.Length)
        {
            return defaults.Take(tierCount);
        }

        return Enumerable.Range(1, tierCount).Select(value => value.ToString(CultureInfo.InvariantCulture));
    }

    private static string CreateReportRoot(CczProject project, string kind)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var root = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports", "DebugEvidence", kind, stamp);
        Directory.CreateDirectory(root);
        return root;
    }

    private static int CountChangedBytes(byte[] before, byte[] after)
    {
        var count = 0;
        var length = Math.Min(before.Length, after.Length);
        for (var i = 0; i < length; i++)
        {
            if (before[i] != after[i]) count++;
        }

        return count + Math.Abs(before.Length - after.Length);
    }

    private static bool IsReadable(byte[] bytes, long offset, int length)
        => offset >= 0 && offset + length <= bytes.Length;

    private static string ToHex(byte[] bytes, long offset, int length)
        => Convert.ToHexString(bytes.AsSpan(checked((int)offset), length));

    private static string ReadNullTerminatedAscii(byte[] bytes, long offset, int maxBytes)
    {
        var end = checked((int)offset);
        var max = Math.Min(bytes.Length, end + maxBytes);
        while (end < max && bytes[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(bytes, checked((int)offset), end - checked((int)offset));
    }

    private sealed class PeMap
    {
        private PeMap(long imageBase, IReadOnlyList<PeSection> sections)
        {
            ImageBase = imageBase;
            Sections = sections;
        }

        public long ImageBase { get; }
        private IReadOnlyList<PeSection> Sections { get; }

        public static PeMap Parse(byte[] bytes)
        {
            if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z')
            {
                throw new InvalidOperationException("Ekd5.exe is not a valid MZ executable.");
            }

            var peOffset = ReadUInt32(bytes, 0x3C);
            if (!IsReadable(bytes, peOffset, 0x18) ||
                bytes[peOffset] != 'P' ||
                bytes[peOffset + 1] != 'E')
            {
                throw new InvalidOperationException("Ekd5.exe PE header is invalid.");
            }

            var sectionCount = ReadUInt16(bytes, peOffset + 6);
            var optionalHeaderSize = ReadUInt16(bytes, peOffset + 20);
            var optionalHeader = peOffset + 24;
            var magic = ReadUInt16(bytes, optionalHeader);
            var imageBase = magic == 0x10B
                ? ReadUInt32(bytes, optionalHeader + 28)
                : (long)BitConverter.ToUInt64(bytes, checked((int)optionalHeader + 24));
            var sectionOffset = optionalHeader + optionalHeaderSize;
            var sections = new List<PeSection>();
            for (var i = 0; i < sectionCount; i++)
            {
                var offset = sectionOffset + i * 40L;
                if (!IsReadable(bytes, offset, 40))
                {
                    break;
                }

                var name = Encoding.ASCII.GetString(bytes, checked((int)offset), 8).TrimEnd('\0');
                sections.Add(new PeSection(
                    name,
                    ReadUInt32(bytes, offset + 12),
                    ReadUInt32(bytes, offset + 8),
                    ReadUInt32(bytes, offset + 20),
                    ReadUInt32(bytes, offset + 16)));
            }

            return new PeMap(imageBase, sections);
        }

        public long VaToOffset(long va)
            => TryVaToOffset(va) ?? throw new InvalidOperationException($"VA cannot be mapped to file offset: 0x{va:X8}.");

        public long? TryVaToOffset(long va)
        {
            var rva = va - ImageBase;
            foreach (var section in Sections)
            {
                var size = Math.Max(section.VirtualSize, section.RawSize);
                if (rva < section.Rva || rva >= section.Rva + size)
                {
                    continue;
                }

                var delta = rva - section.Rva;
                return delta < section.RawSize ? section.RawPtr + delta : null;
            }

            return null;
        }

        private static ushort ReadUInt16(byte[] bytes, long offset)
            => BitConverter.ToUInt16(bytes, checked((int)offset));

        private static uint ReadUInt32(byte[] bytes, long offset)
            => BitConverter.ToUInt32(bytes, checked((int)offset));
    }

    private sealed record PeSection(string Name, long Rva, long VirtualSize, long RawPtr, long RawSize);
}
