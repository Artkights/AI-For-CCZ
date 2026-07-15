using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed partial class InjectedEffectDiscoveryService
{
    private const uint CoreEffectEngineAddress = 0x004101D9;
    private const int InlineStubLookbackBytes = 48;
    private const int BodyScanBytes = 256;
    private static readonly Regex HexStripRegex = new("[^0-9A-Fa-f]", RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<string, Lazy<InjectedEffectDiscoveryReport>> DiscoveryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> DiscoveryAccessOrder = new(StringComparer.OrdinalIgnoreCase);
    private static long _discoveryAccessSequence;

    public InjectedEffectDiscoveryReport Discover(CczProject project, string targetFileName = "Ekd5.exe")
    {
        var targetFile = string.IsNullOrWhiteSpace(targetFileName) ? "Ekd5.exe" : targetFileName;
        var targetPath = project.ResolveGameFile(targetFile);
        var fingerprint = ProjectResourceFingerprint.Create(targetPath, "effect-discovery-v2");
        var signatureFingerprint = BuildSignatureDirectoryFingerprint(project.WorkspaceRoot);
        var key = string.Join("|", fingerprint.Path, fingerprint.Length, fingerprint.LastWriteTimeUtcTicks,
            fingerprint.ChangeGeneration, signatureFingerprint);
        var candidate = new Lazy<InjectedEffectDiscoveryReport>(
            () => DiscoverCore(project, targetFile),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = DiscoveryCache.GetOrAdd(key, candidate);
        DiscoveryAccessOrder[key] = Interlocked.Increment(ref _discoveryAccessSequence);
        var miss = ReferenceEquals(lazy, candidate);
        try
        {
            var report = lazy.Value;
            PerformanceMetrics.Increment(miss ? "EffectDiscovery.CacheMisses" : "EffectDiscovery.CacheHits");
            Trim(targetPath);
            return report;
        }
        catch
        {
            DiscoveryCache.TryRemove(new KeyValuePair<string, Lazy<InjectedEffectDiscoveryReport>>(key, lazy));
            DiscoveryAccessOrder.TryRemove(key, out _);
            throw;
        }
    }

    public static void Invalidate(string? path = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            DiscoveryCache.Clear();
            DiscoveryAccessOrder.Clear();
            return;
        }
        var normalized = ProjectResourceFingerprint.Normalize(path);
        ProjectResourceFingerprint.Invalidate(normalized);
        foreach (var key in DiscoveryCache.Keys.Where(key => key.StartsWith(normalized + "|", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            DiscoveryCache.TryRemove(key, out _);
            DiscoveryAccessOrder.TryRemove(key, out _);
        }
    }

    private static void Trim(string targetPath)
    {
        var prefix = ProjectResourceFingerprint.Normalize(targetPath) + "|";
        foreach (var key in DiscoveryAccessOrder.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(pair => pair.Value).Skip(2).Select(pair => pair.Key).ToArray())
        {
            DiscoveryCache.TryRemove(key, out _);
            DiscoveryAccessOrder.TryRemove(key, out _);
        }
    }

    private static string BuildSignatureDirectoryFingerprint(string workspaceRoot)
    {
        try
        {
            var latest = Directory.Exists(workspaceRoot)
                ? Directory.EnumerateFiles(workspaceRoot, "*.txt", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path)).Where(info => info.Exists)
                    .Select(info => info.LastWriteTimeUtc.Ticks ^ info.Length).DefaultIfEmpty().Max()
                : 0;
            return latest.ToString(CultureInfo.InvariantCulture);
        }
        catch { return "0"; }
    }

    private InjectedEffectDiscoveryReport DiscoverCore(CczProject project, string targetFileName)
    {
        using var operation = PerformanceMetrics.Begin("EffectDiscovery.Build");
        var targetFile = string.IsNullOrWhiteSpace(targetFileName) ? "Ekd5.exe" : targetFileName;
        var targetPath = project.ResolveGameFile(targetFile);
        if (!File.Exists(targetPath))
        {
            return new InjectedEffectDiscoveryReport
            {
                TargetFilePath = targetPath,
                TargetFileName = targetFile,
                Summary = "识别失败：找不到目标 EXE。",
                Warnings = { "找不到目标 EXE：" + targetPath }
            };
        }

        var executable = ExecutableAnalysisSnapshotCache.Shared.GetBase(targetPath);
        var pe = new PeImage(executable.Bytes, executable.PeImage.ImageBase, executable.PeImage.Sections);
        var profile = new EnginePatchProfileService().Build(project);
        var report = new InjectedEffectDiscoveryReport
        {
            TargetFilePath = targetPath,
            TargetFileName = targetFile,
            ExeSha256 = executable.Sha256,
            ExeSize = pe.Bytes.LongLength,
            ImageBase = pe.ImageBase,
            EngineVersionHint = profile.EngineVersion,
            IsKnownEngine = profile.IsKnown
        };
        report.Warnings.AddRange(profile.Warnings);

        var patchSignatures = LoadPatchSignatures(project.WorkspaceRoot).ToList();
        var instructionScan = executable.InstructionScan;
        AddKnownPatchMatches(report, pe, patchSignatures);
        AddMaskedSignatureMatches(report, pe, patchSignatures);
        AddInlineStubCandidates(report, pe, instructionScan);
        AddHookCandidates(report, pe, profile);
        AddIndirectHookDiagnostics(report, instructionScan);
        AddFourModuleHookStructures(report, pe);
        AddLegacyPhysicalRecoveryChain(report, pe);
        AddHookTargetDiagnostics(report);
        MergeKnownLabels(report);
        AttachDefaultModules(report);
        ApplyDetectionDefaults(report);

        report.Candidates.Sort(CompareCandidates);
        report.HookCandidates.Sort((left, right) => left.Address.CompareTo(right.Address));
        report.Summary = $"识别完成：候选 {report.Candidates.Count} 个，跳转 {report.HookCandidates.Count} 个，已知签名 {patchSignatures.Count} 个，警告 {report.Warnings.Count} 条。";
        var variantCount = report.Candidates.Count(candidate =>
            candidate.DetectionLevel.Equals("KnownVariant", StringComparison.OrdinalIgnoreCase) ||
            candidate.Confidence.Equals("KnownPatchVariant", StringComparison.OrdinalIgnoreCase));
        report.Summary = $"特效注入识别完成：候选 {report.Candidates.Count} 个，跳转 {report.HookCandidates.Count} 个，变体 {variantCount} 个，已知签名 {patchSignatures.Count} 个，诊断 {report.Diagnostics.Count} 条，警告 {report.Warnings.Count} 条。";
        return report;
    }

    private static void AddLegacyPhysicalRecoveryChain(InjectedEffectDiscoveryReport report, PeImage pe)
    {
        const uint hookAddress = EngineRuntimeSemanticRegistry.PhysicalRecoveryHookAddress;
        const uint bodyAddress = EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyAddress;
        if (!TryReadVirtualBytes(pe, hookAddress, 7, out var hook) || hook.Length < 7 || hook[0] != 0xE9 ||
            ResolveRelativeTarget(hook, 0, hookAddress) != bodyAddress ||
            !TryReadVirtualBytes(pe, bodyAddress, 0xAB, out var body) || body.Length < 0xAB)
        {
            return;
        }

        var hasCore = HasRelativeTarget(body, bodyAddress, 0x004101D9, 0xE8);
        var hasUnitToCharacter = HasRelativeTarget(body, bodyAddress, EngineRuntimeSemanticRegistry.TacticalUnitToRuntimeCharacterAddress, 0xE8);
        var hasMaximumMp = HasRelativeTarget(body, bodyAddress, 0x0040728F, 0xE8);
        var hasMpWrite = FindSequence(body, [0x01, 0x42, 0x14]) >= 0;
        var hasNormalReturn = HasRelativeTarget(body, bodyAddress, 0x0041833A, 0xE9);
        var hasEarlyReturn = HasNearConditionalTarget(body, bodyAddress, 0x00418351);
        if (!hasCore || !hasUnitToCharacter || !hasMaximumMp || !hasMpWrite || !hasNormalReturn || !hasEarlyReturn)
        {
            return;
        }

        var equipmentId = body[7] == 0x6A ? body[8] : -1;
        var personalId = body[9] == 0x68 ? BitConverter.ToInt32(body, 10) : -1;
        if (equipmentId is < 0 or > 0xFF || personalId is < 0 or > 0xFF) return;

        var candidate = report.Candidates.FirstOrDefault(item => item.JumpOutAddress == hookAddress || item.CodeCaveEntryAddress == bodyAddress)
                        ?? new InjectedEffectCandidate { Address = bodyAddress, AddressHex = FormatVa(bodyAddress) };
        if (!report.Candidates.Contains(candidate)) report.Candidates.Add(candidate);
        report.Candidates.RemoveAll(item => !ReferenceEquals(item, candidate) &&
                                            item.Name.StartsWith("回MP攻击", StringComparison.OrdinalIgnoreCase) &&
                                            !item.MatchedAnchors.Any(anchor => anchor.StartsWith("hook-current:", StringComparison.OrdinalIgnoreCase)));

        candidate.Type = "LegacyPhysicalRecoveryVariant";
        candidate.PatternKind = InjectedEffectPatternKind.KnownPatch;
        candidate.Name = $"回MP攻击（当前个人号 {personalId:X2}）";
        candidate.PersonalEffectId = personalId;
        candidate.EquipmentEffectId = equipmentId;
        candidate.EffectValueFlag = body[4];
        candidate.StackingFlag = body[6];
        candidate.HookPoint = FormatVa(hookAddress);
        candidate.CodeCave = $"{FormatVa(bodyAddress)}-{FormatVa(EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyEndAddress)}";
        candidate.JumpOutAddress = hookAddress;
        candidate.CodeCaveEntryAddress = bodyAddress;
        candidate.GuardStartAddress = bodyAddress;
        candidate.FeatureStartAddress = 0x00452913;
        candidate.ReturnAddress = 0x0041833A;
        candidate.EquipmentIdPatchAddress = bodyAddress + 8;
        candidate.PersonalIdPatchAddress = bodyAddress + 10;
        candidate.Confidence = "KnownPatchVariant";
        candidate.DetectionLevel = "KnownVariant";
        candidate.DetectionScore = 94;
        candidate.Risk = "legacy-present-chain-only";
        candidate.PatchCategory = InjectedEffectPatchCategory.FunctionExtensionPatch;
        candidate.NormalizedSignatureId = "legacy-physical-recovery-chain-v1";
        candidate.RelocationEvidence = "fixed-current-hook-and-body";
        candidate.MatchedAnchors.Clear();
        candidate.MatchedAnchors.AddRange([
            $"hook-current:{FormatVa(hookAddress)}->{FormatVa(bodyAddress)}",
            $"body-current:{FormatVa(bodyAddress)}",
            "body-signature:physical-recovery-control-flow",
            "core-call:004101D9",
            "call-current:0040658F",
            "call-current:0040728F",
            "write-current:unit+14",
            "return-current:0041833A",
            "return-current:00418351"
        ]);
        candidate.MissingAnchors.Clear();
        if (personalId != 0xAD) candidate.MissingAnchors.Add($"catalog-personal-id:expected-AD-current-{personalId:X2}");
        candidate.ParameterSlots.Clear();
        candidate.ParameterSlots.AddRange([
            new InjectedEffectParameterSlot
            {
                Role = InjectedEffectParameterRole.Equipment, DisplayName = "当前宝物特效号", Address = bodyAddress + 8,
                Value = equipmentId, ByteLength = 1, DefinitionInstructionAddress = bodyAddress + 7,
                SourceKind = "CurrentExecutableOperand", Editability = "LegacyReadOnly", SourceComment = "从当前 EXE push imm8 恢复；不使用样本默认值。"
            },
            new InjectedEffectParameterSlot
            {
                Role = InjectedEffectParameterRole.Personal, DisplayName = "当前个人特效号", Address = bodyAddress + 10,
                Value = personalId, ByteLength = 4, DefinitionInstructionAddress = bodyAddress + 9,
                SourceKind = "CurrentExecutableOperand", Editability = "LegacyReadOnly", SourceComment = "从当前 EXE push imm32 恢复；样本期望 AD。"
            }
        ]);
        candidate.CheckGroups.Clear();
        candidate.CheckGroups.Add(new InjectedEffectCheckGroup
        {
            GroupName = "legacy-physical-recovery", GuardStartAddress = bodyAddress, GuardCallAddress = 0x0045290A,
            GuardFunctionAddress = 0x004101D9, FeatureStartAddress = 0x00452913, ReturnAddress = 0x0041833A,
            EquipmentSlot = candidate.ParameterSlots[0], PersonalSlot = candidate.ParameterSlots[1],
            Diagnosis = "现有物理恢复链；只允许在新调度器执行后尾跳保留。"
        });
        candidate.Evidence = $"current hook {FormatVa(hookAddress)} -> {FormatVa(bodyAddress)}; current ids personal={personalId:X2}, equipment={equipmentId:X2}; legacy body and both returns verified";
        candidate.UserReadableDiagnosis = "当前 EXE 已存在完整物理回 MP 遗留链，但没有工具受管清单；新增行为必须链式保留该实现。";
        candidate.StructureDiagnosis = candidate.UserReadableDiagnosis;
    }

    private static bool HasRelativeTarget(byte[] bytes, uint baseAddress, uint target, byte opcode)
    {
        for (var offset = 0; offset <= bytes.Length - 5; offset++)
        {
            if (bytes[offset] != opcode) continue;
            var resolved = unchecked((uint)(baseAddress + (uint)offset + 5 + BitConverter.ToInt32(bytes, offset + 1)));
            if (resolved == target) return true;
        }
        return false;
    }

    private static bool HasNearConditionalTarget(byte[] bytes, uint baseAddress, uint target)
    {
        for (var offset = 0; offset <= bytes.Length - 6; offset++)
        {
            if (bytes[offset] != 0x0F || bytes[offset + 1] is < 0x80 or > 0x8F) continue;
            var resolved = unchecked((uint)(baseAddress + (uint)offset + 6 + BitConverter.ToInt32(bytes, offset + 2)));
            if (resolved == target) return true;
        }
        return false;
    }

    private static int FindSequence(byte[] bytes, byte[] sequence)
    {
        for (var offset = 0; offset <= bytes.Length - sequence.Length; offset++)
            if (bytes.AsSpan(offset, sequence.Length).SequenceEqual(sequence)) return offset;
        return -1;
    }

    public IReadOnlyList<InjectedEffectCandidate> LoadKnownPatchCatalog(CczProject project)
    {
        var candidates = new List<InjectedEffectCandidate>();
        foreach (var signature in LoadPatchSignatures(project.WorkspaceRoot))
        {
            var firstAddress = signature.Segments.FirstOrDefault()?.Address ?? 0;
            var candidate = BuildKnownPatchCandidate(signature, firstAddress, "KnownPatchExact", signature.Segments.Count);
            candidate.Type = "KnownPatchCatalog";
            candidate.Confidence = "KnownPatchCatalog";
            candidate.Evidence = $"catalogSegments={signature.Segments.Count}";
            candidate.UserReadableDiagnosis = "来自本地补丁库样本，不代表当前 EXE 已注入。" + BuildSignatureStructureDiagnosis(signature);
            candidate.StructureDiagnosis = candidate.UserReadableDiagnosis;
            ApplyCategoryDefaults(candidate);
            candidates.Add(candidate);
        }

        var report = new InjectedEffectDiscoveryReport();
        report.Candidates.AddRange(candidates);
        ApplyDetectionDefaults(report);
        candidates.Sort(CompareCandidates);
        return candidates;
    }

    private static void AddKnownPatchMatches(
        InjectedEffectDiscoveryReport report,
        PeImage pe,
        IReadOnlyList<PatchSignature> signatures)
    {
        foreach (var signature in signatures)
        {
            var matchedSegments = 0;
            var matchedAddresses = new List<uint>();
            foreach (var segment in signature.Segments)
            {
                if (segment.Bytes.Length == 0) continue;
                if (!TryVirtualAddressToFileOffset(pe, segment.Address, out var offset)) continue;
                if (offset < 0 || offset + segment.Bytes.Length > pe.Bytes.Length) continue;
                if (pe.Bytes.AsSpan(offset, segment.Bytes.Length).SequenceEqual(segment.Bytes))
                {
                    matchedSegments++;
                    matchedAddresses.Add(segment.Address);
                }
            }

            if (matchedSegments == 0) continue;

            var firstAddress = signature.Segments.FirstOrDefault()?.Address ?? 0;
            var confidence = matchedSegments == signature.Segments.Count ? "KnownPatchExact" : "KnownPatchVariant";
            var candidate = BuildKnownPatchCandidate(signature, firstAddress, confidence, matchedSegments);
            candidate.MatchedAnchors.AddRange(matchedAddresses.Select(address => "segment-current:" + FormatVa(address)));
            candidate.MissingAnchors.AddRange(signature.Segments
                .Where(segment => !matchedAddresses.Contains(segment.Address))
                .Select(segment => "segment:" + FormatVa(segment.Address)));
            if (matchedSegments == signature.Segments.Count)
            {
                if (candidate.JumpOutAddress.HasValue) candidate.MatchedAnchors.Add("hook-current:" + FormatVa(candidate.JumpOutAddress.Value));
                if (candidate.CodeCaveEntryAddress.HasValue) candidate.MatchedAnchors.Add("body-current:" + FormatVa(candidate.CodeCaveEntryAddress.Value));
            }
            report.Candidates.Add(candidate);
        }
    }

    private static void AddMaskedSignatureMatches(
        InjectedEffectDiscoveryReport report,
        PeImage pe,
        IReadOnlyList<PatchSignature> signatures)
    {
        var represented = report.Candidates
            .Where(candidate => candidate.NormalizedSignatureId.Length > 0 || candidate.Type == "KnownPatch")
            .Select(candidate => candidate.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var signature in signatures)
        {
            if (represented.Contains(signature.Name)) continue;
            var manifest = BuildPatchSignatureManifest(signature);
            if (manifest.Anchors.Count == 0) continue;

            var bestScore = 0;
            var bestAddress = 0u;
            var bestMatched = new List<string>();
            var bestMissing = new List<string>();

            foreach (var segment in signature.Segments.Where(segment => segment.Bytes.Length >= 8))
            {
                foreach (var hit in FindMaskedSegmentMatches(pe, segment))
                {
                    var matched = new List<string>();
                    var missing = new List<string>();
                    ScoreSignatureAtDelta(signature, manifest, pe, unchecked((int)(hit - segment.Address)), matched, missing, out var score);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestAddress = hit;
                    bestMatched = matched;
                    bestMissing = missing;
                }
            }

            if (bestScore < 80) continue;
            var candidate = BuildKnownPatchCandidate(signature, bestAddress, "KnownPatchVariant", Math.Max(1, bestMatched.Count));
            candidate.Type = "KnownPatchVariant";
            candidate.Confidence = "KnownPatchVariant";
            candidate.Risk = "partial-match-review";
            candidate.DetectionScore = Math.Min(bestScore, 94);
            candidate.DetectionLevel = "KnownVariant";
            candidate.NormalizedSignatureId = manifest.SignatureId;
            candidate.RelocationEvidence = bestAddress == signature.Segments.First().Address
                ? "normalized-signature"
                : $"relocated-delta=0x{unchecked((uint)(bestAddress - signature.Segments.First().Address)):X8}";
            candidate.MatchedAnchors.AddRange(bestMatched);
            candidate.MissingAnchors.AddRange(bestMissing);
            candidate.JumpOutAddress = null;
            candidate.CodeCaveEntryAddress = null;
            candidate.ReturnAddress = null;
            candidate.HookPoint = string.Empty;
            candidate.CodeCave = string.Empty;
            candidate.ParameterSlots.Clear();
            candidate.CheckGroups.Clear();
            candidate.Evidence = AppendEvidence(candidate.Evidence, "maskedSignatureScore=" + bestScore.ToString(CultureInfo.InvariantCulture));
            report.Candidates.Add(candidate);
            AddDiagnostic(
                report,
                "partialSignatureOnly",
                bestAddress,
                null,
                "Known patch matched by normalized byte/control-flow signature instead of exact fixed-address bytes.",
                candidate.Evidence);
        }
    }

    private static IEnumerable<uint> FindMaskedSegmentMatches(PeImage pe, PatchSignatureSegment segment)
    {
        var pattern = BuildMaskedPattern(segment);
        if (pattern.FixedByteCount < 6) yield break;

        foreach (var section in pe.Sections.Where(section => section.IsExecutable))
        {
            var start = checked((int)section.RawPointer);
            var end = checked((int)Math.Min((long)section.RawPointer + section.RawSize, pe.Bytes.LongLength));
            for (var offset = start; offset <= end - pattern.Bytes.Length; offset++)
            {
                if (!MaskedPatternMatches(pe.Bytes, offset, pattern)) continue;
                yield return FileOffsetToVirtualAddress(pe, section, offset);
            }
        }
    }

    private static bool MaskedPatternMatches(byte[] bytes, int offset, MaskedBytePattern pattern)
    {
        for (var index = 0; index < pattern.Bytes.Length; index++)
        {
            if (pattern.Mask[index] && bytes[offset + index] != pattern.Bytes[index]) return false;
        }

        return true;
    }

    private static MaskedBytePattern BuildMaskedPattern(PatchSignatureSegment segment)
    {
        var bytes = segment.Bytes.ToArray();
        var mask = Enumerable.Repeat(true, bytes.Length).ToArray();
        var instructions = new X86InstructionScanner().DecodeBlock(bytes, segment.Address);
        foreach (var instruction in instructions)
        {
            if (instruction.ImmediateOffset.HasValue)
            {
                ClearMask(mask, instruction.FileOffset + instruction.ImmediateOffset.Value, instruction.ImmediateSize);
            }

            if (instruction.DisplacementOffset.HasValue)
            {
                ClearMask(mask, instruction.FileOffset + instruction.DisplacementOffset.Value, instruction.DisplacementSize);
            }

            if (instruction.FlowControl is "Call" or "UnconditionalBranch" or "ConditionalBranch")
            {
                ClearMask(mask, instruction.FileOffset + 1, Math.Max(0, instruction.Length - 1));
            }
        }

        for (var index = bytes.Length - 1; index >= 0 && bytes[index] == 0x90; index--)
        {
            mask[index] = false;
        }

        return new MaskedBytePattern(bytes, mask);
    }

    private static void ClearMask(bool[] mask, int offset, int length)
    {
        if (length <= 0) return;
        for (var index = Math.Max(0, offset); index < Math.Min(mask.Length, offset + length); index++)
        {
            mask[index] = false;
        }
    }

    private static PatchSignatureManifest BuildPatchSignatureManifest(PatchSignature signature)
    {
        var anchors = new List<string>();
        if (signature.HookAddresses.Count > 0) anchors.AddRange(signature.HookAddresses.Select(address => "hook:" + FormatVa(address)));
        if (signature.Segments.Any(segment => segment.Bytes.Any(b => b == 0xE8))) anchors.Add("has-call");
        if (signature.ParameterSlots.Any(slot => slot.Role == InjectedEffectParameterRole.Personal)) anchors.Add("personal-slot");
        if (signature.ParameterSlots.Any(slot => slot.Role == InjectedEffectParameterRole.Equipment)) anchors.Add("equipment-slot");
        if (signature.Segments.Count >= 3) anchors.Add("multi-segment");

        var tokenBuilder = new StringBuilder(signature.Name);
        foreach (var segment in signature.Segments.Where(segment => segment.Bytes.Length >= 8))
        {
            var pattern = BuildMaskedPattern(segment);
            tokenBuilder.Append('|').Append(segment.Bytes.Length.ToString(CultureInfo.InvariantCulture));
            tokenBuilder.Append(':').Append(pattern.FixedByteCount.ToString(CultureInfo.InvariantCulture));
        }

        return new PatchSignatureManifest(
            SignatureId: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenBuilder.ToString())))[..16],
            Anchors: anchors);
    }

    private static void ScoreSignatureAtDelta(
        PatchSignature signature,
        PatchSignatureManifest manifest,
        PeImage pe,
        int delta,
        List<string> matched,
        List<string> missing,
        out int score)
    {
        var total = 0;
        var hits = 0;
        foreach (var segment in signature.Segments.Where(segment => segment.Bytes.Length > 0))
        {
            total++;
            var relocated = unchecked((uint)(segment.Address + delta));
            if (TryVirtualAddressToFileOffset(pe, relocated, out var offset) &&
                offset >= 0 &&
                offset + segment.Bytes.Length <= pe.Bytes.Length &&
                MaskedPatternMatches(pe.Bytes, offset, BuildMaskedPattern(segment)))
            {
                hits++;
                matched.Add("segment:" + FormatVa(segment.Address) + "->" + FormatVa(relocated));
            }
            else
            {
                missing.Add("segment:" + FormatVa(segment.Address));
            }
        }

        var ratio = total == 0 ? 0 : hits * 100 / total;
        score = Math.Min(94, 70 + ratio / 4 + Math.Min(10, manifest.Anchors.Count));
    }

    private static InjectedEffectCandidate BuildKnownPatchCandidate(
        PatchSignature signature,
        uint firstAddress,
        string confidence,
        int matchedSegments)
    {
        var candidate = new InjectedEffectCandidate
        {
            Address = firstAddress,
            AddressHex = FormatVa(firstAddress),
            Type = "KnownPatch",
            PatternKind = InjectedEffectPatternKind.KnownPatch,
            Name = signature.Name,
            PersonalEffectId = signature.PersonalEffectId,
            EquipmentEffectId = signature.EquipmentEffectId,
            HookPoint = string.Join(", ", signature.HookAddresses.Select(FormatVa)),
            CodeCave = BuildCodeCaveSummary(signature.Segments),
            Confidence = confidence,
            Risk = matchedSegments == signature.Segments.Count ? "known-sample" : "partial-match-review",
            Evidence = $"matchedSegments={matchedSegments}/{signature.Segments.Count}",
            Source = signature.SourcePath,
            PatchCategory = signature.PatchCategory,
            StructureDiagnosis = BuildSignatureStructureDiagnosis(signature)
        };

        var hookSegment = signature.Segments.FirstOrDefault(segment => segment.Bytes.Length >= 5 && segment.Bytes[0] == 0xE9);
        var bodySegment = default(PatchSignatureSegment);
        uint? hookTarget = null;
        if (hookSegment != null)
        {
            hookTarget = ResolveRelativeTarget(hookSegment.Bytes, 0, hookSegment.Address);
            bodySegment = signature.Segments.FirstOrDefault(segment => segment.Address == hookTarget.Value)
                          ?? signature.Segments.Where(segment => segment.Bytes.Length >= 16 && segment.Address != hookSegment.Address)
                              .OrderByDescending(segment => segment.Bytes.Length)
                              .FirstOrDefault();
        }
        else
        {
            bodySegment = signature.Segments.Where(segment => segment.Bytes.Length >= 16)
                .OrderByDescending(segment => segment.Bytes.Length)
                .FirstOrDefault();
        }

        BodyAnalysis? body = null;
        if (bodySegment != null)
        {
            body = AnalyzeCodeBody(bodySegment.Bytes, bodySegment.Address);
            ApplyBodyAnalysis(candidate, body);
        }

        if (hookSegment != null)
        {
            candidate.JumpOutAddress = hookSegment.Address;
            candidate.CodeCaveEntryAddress = hookTarget;
            candidate.HookPoint = FormatVa(hookSegment.Address);
            candidate.CodeCave = hookTarget.HasValue ? FormatVa(hookTarget.Value) : candidate.CodeCave;
        }
        else if (bodySegment != null)
        {
            candidate.CodeCaveEntryAddress = bodySegment.Address;
            candidate.CodeCave = FormatVa(bodySegment.Address);
        }

        ApplySignatureParameterSegments(candidate, signature);
        candidate.Modules = BuildModules(candidate, body);
        candidate.ModuleSummary = BuildModuleSummary(candidate);

        if (body?.IsFourModuleLike == true)
        {
            candidate.PatternKind = IsDamageSemantic(signature.SemanticText) &&
                                    IsCompleteFourModuleDamageCandidate(candidate) &&
                                    signature.PatchCategory != InjectedEffectPatchCategory.MultiCheckSpecialEffect &&
                                    signature.PatchCategory != InjectedEffectPatchCategory.ComplexMultiHookPatch
                ? InjectedEffectPatternKind.FourModuleDamageModifier
                : InjectedEffectPatternKind.FourModuleLikeCandidate;
            if (candidate.PatternKind == InjectedEffectPatternKind.FourModuleDamageModifier)
            {
                candidate.UserReadableDiagnosis = "本地补丁签名完整命中，并识别到“跳出点 -> 代码洞 -> 特技判定桩 -> 功能函数 -> 回原流程”的条件伤害类四模块结构。";
            }
            else
            {
                candidate.UserReadableDiagnosis = "本地补丁签名命中，结构像四模块特技补丁；但文本语义未证明它属于条件增伤/减伤，需要结合战斗行为确认。";
            }
        }
        else
        {
            candidate.UserReadableDiagnosis = BuildKnownPatchDiagnosis(signature, confidence);
        }

        ApplyCategoryDefaults(candidate);

        return candidate;
    }

    private static bool IsCompleteFourModuleDamageCandidate(InjectedEffectCandidate candidate)
        => candidate.JumpOutAddress.HasValue &&
           candidate.CodeCaveEntryAddress.HasValue &&
           candidate.GuardStartAddress.HasValue &&
           candidate.ReturnAddress.HasValue &&
           candidate.PersonalEffectId.HasValue &&
           candidate.EquipmentEffectId.HasValue;

    private static void AddInlineStubCandidates(InjectedEffectDiscoveryReport report, PeImage pe, X86ScanResult instructionScan)
    {
        var knownFunctionEntries = instructionScan.Instructions
            .Where(item => item.IsDirectCall && item.BranchTarget.HasValue)
            .Select(item => item.BranchTarget!.Value)
            .ToHashSet();
        foreach (var pair in instructionScan.InstructionsBySection)
        {
            var sectionInstructions = pair.Value;
            for (var index = 0; index < sectionInstructions.Count; index++)
            {
                var instruction = sectionInstructions[index];
                if (!instruction.IsDirectCall || instruction.BranchTarget != CoreEffectEngineAddress) continue;

                var callAddress = instruction.Address;
                var pushes = BuildPushImmediatesFromArguments(
                    X86InstructionScanner.BackwardSliceStackArguments(sectionInstructions, index),
                    callAddress).ToList();
                var parsed = TryParseInlineStub(pushes, out var effectValueFlag, out var stackingFlag, out var equipmentId, out var personalId);
                var candidate = new InjectedEffectCandidate
                {
                    Address = callAddress,
                    AddressHex = FormatVa(callAddress),
                    ConsumerFunctionAddress = FindConsumerFunctionEntry(sectionInstructions, index, knownFunctionEntries),
                    Type = "InlineStub",
                    PatternKind = InjectedEffectPatternKind.InlineCoreStub,
                    Name = parsed ? "个人/宝物特技判定桩" : "仅识别到特技核心调用",
                    PersonalEffectId = parsed ? personalId : null,
                    EquipmentEffectId = parsed ? equipmentId : null,
                    EffectValueFlag = parsed ? effectValueFlag : null,
                    StackingFlag = parsed ? stackingFlag : null,
                    GuardStartAddress = pushes.FirstOrDefault()?.InstructionAddress,
                    PersonalIdPatchAddress = parsed ? pushes.TakeLast(1).First().OperandAddress : null,
                    EquipmentIdPatchAddress = parsed ? pushes.Skip(Math.Max(0, pushes.Count - 2)).First().OperandAddress : null,
                    Confidence = parsed ? "InlineStubDetected" : "CallToCoreEngine",
                    Risk = parsed ? "semantic-review-required" : "parameter-parse-incomplete",
                    Evidence = $"call {FormatVa(CoreEffectEngineAddress)}; decodedStackArgs={pushes.Count}",
                    Source = report.TargetFileName,
                    PatchCategory = InjectedEffectPatchCategory.InlineCoreStub,
                    MatchedAnchors = { "core-call:004101D9" },
                    UserReadableDiagnosis = parsed
                        ? "识别到内联特技判定桩：调用 4101D9 前可解析 4 个入栈参数；具体战斗语义仍需结合上下文。"
                        : "识别到 4101D9 调用，但前置参数不足或无法反向解析，暂不确定是哪一个特技。"
                };
                candidate.PointerInference = new X86ContextDataFlowAnalyzer().Analyze(sectionInstructions, index);
                if (parsed)
                {
                    var orderedPushes = pushes.TakeLast(4).OrderBy(push => push.InstructionAddress).ToArray();
                    AddParameterSlot(candidate, CreateParameterSlotFromPush(InjectedEffectParameterRole.EffectValue, "效果值标志", "内联判定桩", orderedPushes[0], "来自调用 4101D9 前的效果值标志。"));
                    AddParameterSlot(candidate, CreateParameterSlotFromPush(InjectedEffectParameterRole.BooleanOption, "叠加标志", "内联判定桩", orderedPushes[1], "来自调用 4101D9 前的叠加标志。"));
                    AddParameterSlot(candidate, CreateParameterSlotFromPush(InjectedEffectParameterRole.Equipment, "装备号", "内联判定桩", orderedPushes[2], "来自调用 4101D9 前的装备号入栈参数。"));
                    AddParameterSlot(candidate, CreateParameterSlotFromPush(InjectedEffectParameterRole.Personal, "个人号", "内联判定桩", orderedPushes[3], "来自调用 4101D9 前的个人号入栈参数。"));
                    candidate.CheckGroups.Add(new InjectedEffectCheckGroup
                    {
                        GroupName = "内联判定桩",
                        GuardStartAddress = orderedPushes[0].InstructionAddress,
                        GuardCallAddress = callAddress,
                        GuardFunctionAddress = CoreEffectEngineAddress,
                        EquipmentSlot = candidate.ParameterSlots.FirstOrDefault(slot => slot.Role == InjectedEffectParameterRole.Equipment),
                        PersonalSlot = candidate.ParameterSlots.FirstOrDefault(slot => slot.Role == InjectedEffectParameterRole.Personal),
                        Diagnosis = "调用 4101D9 前识别到四个参数：效果值标志、叠加标志、装备号、个人号。"
                    });
                }
                else
                {
                    candidate.FailureReasons.Add("coreCallUnparsed: stack parameter slice did not resolve four arguments");
                    AddDiagnostic(
                        report,
                        "coreCallUnparsed",
                        callAddress,
                        CoreEffectEngineAddress,
                        "Found direct call to 004101D9 but did not resolve four stack arguments.",
                        "decoded-call; stackArgs=" + pushes.Count.ToString(CultureInfo.InvariantCulture));
                }

                candidate.Modules = BuildInlineStubModules(candidate, pushes, callAddress);
                candidate.ModuleSummary = BuildModuleSummary(candidate);
                report.Candidates.Add(candidate);
            }
        }

        AddWrapperCoreCallCandidates(report, instructionScan);
    }

    private static void AddWrapperCoreCallCandidates(
        InjectedEffectDiscoveryReport report,
        X86ScanResult instructionScan)
    {
        var knownFunctionEntries = instructionScan.Instructions
            .Where(item => item.IsDirectCall && item.BranchTarget.HasValue)
            .Select(item => item.BranchTarget!.Value)
            .ToHashSet();
        var matches = new List<WrapperCoreCallMatch>();
        var wrapperCache = new Dictionary<uint, X86InstructionInfo?>();
        foreach (var pair in instructionScan.InstructionsBySection)
        {
            var sectionInstructions = pair.Value;
            for (var index = 0; index < sectionInstructions.Count; index++)
            {
                var instruction = sectionInstructions[index];
                if (!instruction.IsDirectCall ||
                    !instruction.BranchTarget.HasValue ||
                    instruction.BranchTarget == CoreEffectEngineAddress)
                {
                    continue;
                }

                var wrapperEntry = instruction.BranchTarget.Value;
                if (!wrapperCache.TryGetValue(wrapperEntry, out var wrapperCoreCall))
                {
                    wrapperCoreCall = X86InstructionScanner.TryFindDirectCoreCallInBlock(
                        instructionScan,
                        wrapperEntry,
                        CoreEffectEngineAddress,
                        out var resolvedCoreCall)
                        ? resolvedCoreCall
                        : null;
                    wrapperCache[wrapperEntry] = wrapperCoreCall;
                }

                if (wrapperCoreCall == null) continue;

                var pushes = BuildPushImmediatesFromArguments(
                    X86InstructionScanner.BackwardSliceStackArguments(sectionInstructions, index),
                    instruction.Address).ToList();
                if (!TryParseInlineStub(pushes, out var effectValueFlag, out var stackingFlag, out var equipmentId, out var personalId))
                {
                    AddDiagnostic(
                        report,
                        "wrapperCoreCallUnparsed",
                        instruction.Address,
                        wrapperEntry,
                        "Found call to wrapper that reaches 004101D9, but caller stack arguments did not resolve.",
                        $"wrapperCoreCall={FormatVa(wrapperCoreCall.Address)}; stackArgs={pushes.Count}");
                    continue;
                }

                matches.Add(new WrapperCoreCallMatch(
                    instruction,
                    wrapperCoreCall,
                    wrapperEntry,
                    FindConsumerFunctionEntry(sectionInstructions, index, knownFunctionEntries),
                    pushes,
                    effectValueFlag,
                    stackingFlag,
                    equipmentId,
                    personalId));
            }
        }

        foreach (var group in matches
                     .GroupBy(match => string.Create(
                         CultureInfo.InvariantCulture,
                         $"{match.ConsumerFunctionAddress:X8}:{match.WrapperEntry:X8}:{match.EffectValueFlag:X}:{match.StackingFlag:X}:{match.EquipmentId:X}:{match.PersonalId:X}")))
        {
            var first = group.OrderBy(match => match.Caller.Address).First();
            var pushes = first.Pushes.ToList();
            var orderedPushes = pushes.TakeLast(4).OrderBy(push => push.InstructionAddress).ToArray();
            var candidate = new InjectedEffectCandidate
            {
                Address = first.Caller.Address,
                AddressHex = FormatVa(first.Caller.Address),
                ConsumerFunctionAddress = first.ConsumerFunctionAddress,
                Type = "WrapperInlineStub",
                PatternKind = InjectedEffectPatternKind.InlineCoreStub,
                Name = "包装特技判定桩",
                PersonalEffectId = first.PersonalId,
                EquipmentEffectId = first.EquipmentId,
                EffectValueFlag = first.EffectValueFlag,
                StackingFlag = first.StackingFlag,
                HookPoint = FormatVa(first.Caller.Address),
                CodeCave = string.Empty,
                WrapperEntryAddress = first.WrapperEntry,
                GuardStartAddress = orderedPushes.FirstOrDefault()?.InstructionAddress,
                PersonalIdPatchAddress = orderedPushes.Length >= 4 ? orderedPushes[3].OperandAddress : null,
                EquipmentIdPatchAddress = orderedPushes.Length >= 3 ? orderedPushes[2].OperandAddress : null,
                Confidence = "WrapperCoreCallDetected",
                Risk = "semantic-review-required",
                Evidence = $"call wrapper {FormatVa(first.WrapperEntry)}; wrapperCoreCall={FormatVa(first.CoreCall.Address)}; callerCount={group.Count()}",
                Source = report.TargetFileName,
                PatchCategory = InjectedEffectPatchCategory.InlineCoreStub,
                DetectionScore = 68,
                DetectionLevel = "SemanticCandidate",
                MatchedAnchors = { "wrapper-entry:" + FormatVa(first.WrapperEntry), "core-call:004101D9" },
                UserReadableDiagnosis = "Detected caller-prepared stack arguments flowing through a wrapper that reaches 004101D9; keep as read-only semantic candidate."
            };

            if (orderedPushes.Length >= 4)
            {
                AddParameterSlot(candidate, CreateParameterSlotFromPush(InjectedEffectParameterRole.EffectValue, "effect value flag", "wrapper caller", orderedPushes[0], "Argument prepared before the wrapper call."));
                AddParameterSlot(candidate, CreateParameterSlotFromPush(InjectedEffectParameterRole.BooleanOption, "stacking flag", "wrapper caller", orderedPushes[1], "Argument prepared before the wrapper call."));
                AddParameterSlot(candidate, CreateParameterSlotFromPush(InjectedEffectParameterRole.Equipment, "equipment id", "wrapper caller", orderedPushes[2], "Argument prepared before the wrapper call."));
                AddParameterSlot(candidate, CreateParameterSlotFromPush(InjectedEffectParameterRole.Personal, "personal id", "wrapper caller", orderedPushes[3], "Argument prepared before the wrapper call."));
                candidate.CheckGroups.Add(new InjectedEffectCheckGroup
                {
                    GroupName = "wrapper caller",
                    GuardStartAddress = orderedPushes[0].InstructionAddress,
                    GuardCallAddress = first.Caller.Address,
                    GuardFunctionAddress = CoreEffectEngineAddress,
                    EquipmentSlot = candidate.ParameterSlots.FirstOrDefault(slot => slot.Role == InjectedEffectParameterRole.Equipment),
                    PersonalSlot = candidate.ParameterSlots.FirstOrDefault(slot => slot.Role == InjectedEffectParameterRole.Personal),
                    Diagnosis = "Caller prepares four stack arguments before calling a wrapper that reaches 004101D9."
                });
            }

            candidate.Modules = BuildInlineStubModules(candidate, pushes, first.Caller.Address);
            candidate.ModuleSummary = BuildModuleSummary(candidate);
            report.Candidates.Add(candidate);
        }
    }

    private static uint FindConsumerFunctionEntry(
        IReadOnlyList<X86InstructionInfo> instructions,
        int callIndex,
        IReadOnlySet<uint> knownFunctionEntries)
    {
        if (callIndex < 0 || callIndex >= instructions.Count) return 0;
        var callAddress = instructions[callIndex].Address;
        var minimumAddress = callAddress > 0x800 ? callAddress - 0x800 : 0;
        for (var index = callIndex; index >= 0; index--)
        {
            var instruction = instructions[index];
            if (instruction.Address < minimumAddress) break;
            if (knownFunctionEntries.Contains(instruction.Address)) return instruction.Address;
            if (index + 1 < instructions.Count &&
                instruction.Mnemonic.Equals("push", StringComparison.OrdinalIgnoreCase) &&
                instruction.Operands.FirstOrDefault()?.Kind.Equals("Register", StringComparison.OrdinalIgnoreCase) == true &&
                instruction.Operands[0].Register.Equals("ebp", StringComparison.OrdinalIgnoreCase) &&
                instructions[index + 1].Mnemonic.Equals("mov", StringComparison.OrdinalIgnoreCase) &&
                instructions[index + 1].Operands.Count >= 2 &&
                instructions[index + 1].Operands[0].Register.Equals("ebp", StringComparison.OrdinalIgnoreCase) &&
                instructions[index + 1].Operands[1].Register.Equals("esp", StringComparison.OrdinalIgnoreCase))
            {
                return instruction.Address;
            }
        }
        return callAddress;
    }

    private static IEnumerable<PushImmediate> BuildPushImmediatesFromArguments(
        IReadOnlyList<X86StackArgument> arguments,
        uint callAddress)
    {
        foreach (var argument in arguments)
        {
            var size = argument.ByteLength is 1 or 2 or 4 ? argument.ByteLength : 4;
            var instructionAddress = argument.DefinitionInstructionAddress ?? argument.InstructionAddress;
            yield return new PushImmediate(
                Offset: argument.OperandFileOffset ?? -1,
                InstructionAddress: instructionAddress,
                OperandAddress: argument.OperandAddress,
                RawValue: argument.Value,
                OperandSize: size,
                IsImm8: size == 1,
                SourceKind: argument.SourceKind,
                IsDirectlyPatchable: argument.IsDirectlyPatchable,
                DefinitionChain: argument.EffectiveDefinitionChain,
                OperandOffset: argument.OperandOffset);
        }
    }

    private static void AddInlineStubCandidates(InjectedEffectDiscoveryReport report, PeImage pe)
    {
        foreach (var section in pe.Sections.Where(section => section.IsExecutable))
        {
            var start = checked((int)section.RawPointer);
            var end = checked((int)Math.Min((long)section.RawPointer + section.RawSize, pe.Bytes.LongLength));
            for (var offset = start; offset <= end - 5; offset++)
            {
                if (pe.Bytes[offset] != 0xE8) continue;
                var callAddress = FileOffsetToVirtualAddress(pe, section, offset);
                var target = ResolveRelativeTarget(pe.Bytes, offset, callAddress);
                if (target != CoreEffectEngineAddress) continue;

                var pushes = ReadPreviousPushImmediates(pe, section, offset).ToList();
                var parsed = TryParseInlineStub(pushes, out var effectValueFlag, out var stackingFlag, out var equipmentId, out var personalId);
                var candidate = new InjectedEffectCandidate
                {
                    Address = callAddress,
                    AddressHex = FormatVa(callAddress),
                    Type = "InlineStub",
                    PatternKind = InjectedEffectPatternKind.InlineCoreStub,
                    Name = parsed ? "个人/宝物特技判定桩" : "仅识别到特技核心调用",
                    PersonalEffectId = parsed ? personalId : null,
                    EquipmentEffectId = parsed ? equipmentId : null,
                    EffectValueFlag = parsed ? effectValueFlag : null,
                    StackingFlag = parsed ? stackingFlag : null,
                    GuardStartAddress = pushes.FirstOrDefault()?.InstructionAddress,
                    PersonalIdPatchAddress = parsed ? pushes.TakeLast(1).First().OperandAddress : null,
                    EquipmentIdPatchAddress = parsed ? pushes.Skip(Math.Max(0, pushes.Count - 2)).First().OperandAddress : null,
                    Confidence = parsed ? "InlineStubDetected" : "CallToCoreEngine",
                    Risk = parsed ? "semantic-review-required" : "parameter-parse-incomplete",
                    Evidence = $"call {FormatVa(CoreEffectEngineAddress)}; pushes={pushes.Count}",
                    Source = report.TargetFileName,
                    PatchCategory = InjectedEffectPatchCategory.InlineCoreStub,
                    UserReadableDiagnosis = parsed
                        ? "识别到内联特技判定桩：调用 4101D9 前有 4 个入栈参数。它证明这里在查特技，但具体战斗语义仍需结合上下文。"
                        : "只识别到 4101D9 调用，前置参数不足，暂不能确定是哪一个特技。"
                };
                if (parsed)
                {
                    var orderedPushes = pushes.TakeLast(4).OrderBy(push => push.Offset).ToArray();
                    AddParameterSlot(candidate, CreateParameterSlotFromPush(InjectedEffectParameterRole.EffectValue, "效果值标志", "内联判定桩", orderedPushes[0], "来自调用 4101D9 前的效果值标志。"));
                    AddParameterSlot(candidate, CreateParameterSlotFromPush(InjectedEffectParameterRole.BooleanOption, "叠加标志", "内联判定桩", orderedPushes[1], "来自调用 4101D9 前的叠加标志。"));
                    AddParameterSlot(candidate, CreateParameterSlotFromPush(InjectedEffectParameterRole.Equipment, "装备号", "内联判定桩", orderedPushes[2], "来自调用 4101D9 前的装备号入栈参数。"));
                    AddParameterSlot(candidate, CreateParameterSlotFromPush(InjectedEffectParameterRole.Personal, "个人号", "内联判定桩", orderedPushes[3], "来自调用 4101D9 前的个人号入栈参数。"));
                    candidate.CheckGroups.Add(new InjectedEffectCheckGroup
                    {
                        GroupName = "内联判定桩",
                        GuardStartAddress = orderedPushes[0].InstructionAddress,
                        GuardCallAddress = callAddress,
                        GuardFunctionAddress = CoreEffectEngineAddress,
                        EquipmentSlot = candidate.ParameterSlots.FirstOrDefault(slot => slot.Role == InjectedEffectParameterRole.Equipment),
                        PersonalSlot = candidate.ParameterSlots.FirstOrDefault(slot => slot.Role == InjectedEffectParameterRole.Personal),
                        Diagnosis = "调用 4101D9 前识别到四个参数：效果值标志、叠加标志、装备号、个人号。"
                    });
                }
                candidate.Modules = BuildInlineStubModules(candidate, pushes, callAddress);
                candidate.ModuleSummary = BuildModuleSummary(candidate);
                report.Candidates.Add(candidate);
            }
        }
    }

    private static void AddHookCandidates(InjectedEffectDiscoveryReport report, PeImage pe, EnginePatchProfile profile)
    {
        var knownHookAddresses = profile.HookPoints
            .Select(pair => TryParseUInt(pair.Value, out var value) ? value : 0)
            .Where(value => value != 0)
            .ToHashSet();

        foreach (var section in pe.Sections.Where(section => section.IsExecutable))
        {
            var start = checked((int)section.RawPointer);
            var end = checked((int)Math.Min((long)section.RawPointer + section.RawSize, pe.Bytes.LongLength));
            for (var offset = start; offset <= end - 5; offset++)
            {
                if (pe.Bytes[offset] != 0xE9) continue;
                var address = FileOffsetToVirtualAddress(pe, section, offset);
                var target = ResolveRelativeTarget(pe.Bytes, offset, address);
                var targetInExecutable = IsVirtualAddressInExecutableSection(pe, target);
                var isKnownHook = knownHookAddresses.Contains(address);
                if (!isKnownHook && !targetInExecutable) continue;

                report.HookCandidates.Add(new InjectedEffectHookCandidate
                {
                    Address = address,
                    AddressHex = FormatVa(address),
                    Target = target,
                    TargetHex = FormatVa(target),
                    SectionName = section.Name,
                    Classification = isKnownHook ? "KnownHookJump" : "ExecutableJump",
                    Risk = isKnownHook ? "known-hook-review-current-bytes" : "jump-graph-candidate",
                    Evidence = "E9 rel32"
                });
            }
        }
    }

    private static void AddFourModuleHookStructures(InjectedEffectDiscoveryReport report, PeImage pe)
    {
        var representedHooks = report.Candidates
            .Where(candidate => candidate.JumpOutAddress.HasValue)
            .Select(candidate => candidate.JumpOutAddress!.Value)
            .ToHashSet();
        var representedCaves = report.Candidates
            .Where(candidate => candidate.CodeCaveEntryAddress.HasValue)
            .Select(candidate => candidate.CodeCaveEntryAddress!.Value)
            .ToHashSet();

        foreach (var hook in report.HookCandidates)
        {
            if (representedHooks.Contains(hook.Address) || representedCaves.Contains(hook.Target)) continue;
            if (!TryReadVirtualBytes(pe, hook.Target, BodyScanBytes, out var bodyBytes)) continue;

            var body = AnalyzeCodeBody(bodyBytes, hook.Target);
            if (!body.IsFourModuleLike) continue;

            var candidate = new InjectedEffectCandidate
            {
                Address = hook.Address,
                AddressHex = FormatVa(hook.Address),
                Type = "ExecutableJump",
                PatternKind = InjectedEffectPatternKind.FourModuleLikeCandidate,
                Name = "四模块样式候选",
                HookPoint = FormatVa(hook.Address),
                CodeCave = FormatVa(hook.Target),
                JumpOutAddress = hook.Address,
                CodeCaveEntryAddress = hook.Target,
                Confidence = "FourModuleLikeCandidate",
                Risk = "semantic-review-required",
                Evidence = "E9 rel32; guard-call-branch-return",
                Source = "Ekd5.exe",
                PatchCategory = InjectedEffectPatchCategory.UnknownCandidate,
                UserReadableDiagnosis = "发现从原流程跳到代码洞、先判定特技、再分支执行功能并回到原流程的结构。未命中本地补丁语义，不能直接当作条件增伤/减伤。"
            };
            ApplyBodyAnalysis(candidate, body);
            candidate.Modules = BuildModules(candidate, body);
            candidate.ModuleSummary = BuildModuleSummary(candidate);
            report.Candidates.Add(candidate);
        }
    }

    private static void ApplyBodyAnalysis(InjectedEffectCandidate candidate, BodyAnalysis body)
    {
        candidate.GuardStartAddress ??= body.GuardStartAddress;
        candidate.FeatureStartAddress ??= body.FeatureStartAddress;
        candidate.ReturnAddress ??= body.ReturnAddress;
        candidate.PersonalEffectId ??= body.PersonalEffectId;
        candidate.EquipmentEffectId ??= body.EquipmentEffectId;
        candidate.PersonalIdPatchAddress ??= body.PersonalIdPatchAddress;
        candidate.EquipmentIdPatchAddress ??= body.EquipmentIdPatchAddress;
        if (string.IsNullOrWhiteSpace(candidate.CodeCave) && body.BaseAddress != 0)
        {
            candidate.CodeCave = FormatVa(body.BaseAddress);
        }

        AddBodyCheckGroups(candidate, body);
    }

    private static void ApplySignatureParameterSegments(InjectedEffectCandidate candidate, PatchSignature signature)
    {
        AddParameterSlots(candidate, signature.ParameterSlots);
        ApplyPrimaryParameterFields(candidate);
        AssignSignatureSlotsToCheckGroups(candidate);
    }

    private static void AddBodyCheckGroups(InjectedEffectCandidate candidate, BodyAnalysis body)
    {
        if (body.CheckGroups.Count == 0) return;

        for (var index = 0; index < body.CheckGroups.Count; index++)
        {
            var group = body.CheckGroups[index];
            var equipmentPush = group.GuardPushes.Count >= 2 ? group.GuardPushes[^2] : null;
            var personalPush = group.GuardPushes.Count >= 1 ? group.GuardPushes[^1] : null;
            var groupName = body.CheckGroups.Count == 1
                ? "主判定"
                : $"判定组 {index + 1}";
            var equipmentSlot = equipmentPush == null
                ? null
                : CreateParameterSlotFromPush(
                    InjectedEffectParameterRole.Equipment,
                    groupName + "装备号",
                    groupName,
                    equipmentPush,
                    "来自代码洞判定桩的装备号入栈参数。");
            var personalSlot = personalPush == null
                ? null
                : CreateParameterSlotFromPush(
                    InjectedEffectParameterRole.Personal,
                    groupName + "个人号",
                    groupName,
                    personalPush,
                    "来自代码洞判定桩的个人号入栈参数。");
            if (equipmentSlot != null) AddParameterSlot(candidate, equipmentSlot);
            if (personalSlot != null) AddParameterSlot(candidate, personalSlot);

            candidate.CheckGroups.Add(new InjectedEffectCheckGroup
            {
                GroupName = groupName,
                GuardStartAddress = group.GuardPushes.FirstOrDefault()?.InstructionAddress,
                GuardCallAddress = group.GuardCall.Address,
                GuardFunctionAddress = group.GuardCall.Target,
                EquipmentSlot = equipmentSlot,
                PersonalSlot = personalSlot,
                FailureBranchAddress = group.ConditionalBranch?.Target,
                FeatureStartAddress = group.ConditionalBranch?.EndAddress ?? group.GuardCall.EndAddress,
                ReturnAddress = group.ReturnAddress,
                Diagnosis = group.ConditionalBranch == null
                    ? "识别到特技判定调用，但未确认失败分支。"
                    : "识别到参数入栈、判定调用和条件分支。"
            });
        }

        ApplyPrimaryParameterFields(candidate);
    }

    private static InjectedEffectParameterSlot CreateParameterSlotFromPush(
        string role,
        string displayName,
        string groupName,
        PushImmediate push,
        string sourceComment)
        => new()
        {
            Role = role,
            DisplayName = displayName,
            GroupName = groupName,
            Address = push.OperandAddress,
            Value = push.RawValue,
            ByteLength = push.OperandSize,
            DefinitionInstructionAddress = push.InstructionAddress,
            OperandFileOffset = push.Offset >= 0 ? push.Offset : null,
            OperandOffset = push.OperandOffset,
            SourceKind = push.SourceKind,
            IsDirectlyPatchable = push.IsDirectlyPatchable,
            DefinitionChain = push.DefinitionChain?.ToList() ?? [],
            SourceComment = sourceComment,
            Editability = !push.IsDirectlyPatchable ? "已解析，来源暂不可直接修改" : IsUnsafeImm8(push) ? "需要改写指令" : "可编辑",
            SafeRangeDescription = BuildSafeRangeDescription(push.OperandSize, push.IsImm8, push.RawValue)
        };

    private static void AddParameterSlots(InjectedEffectCandidate candidate, IEnumerable<InjectedEffectParameterSlot> slots)
    {
        foreach (var slot in slots)
        {
            AddParameterSlot(candidate, slot);
        }
    }

    private static void AddParameterSlot(InjectedEffectCandidate candidate, InjectedEffectParameterSlot slot)
    {
        var existing = candidate.ParameterSlots.FirstOrDefault(item =>
            item.Address.HasValue &&
            slot.Address.HasValue &&
            item.Address.Value == slot.Address.Value &&
            string.Equals(item.Role, slot.Role, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (!slot.SourceComment.Contains("代码洞判定桩", StringComparison.Ordinal))
            {
                existing.DisplayName = FirstNonEmpty(slot.DisplayName, existing.DisplayName);
                existing.GroupName = FirstNonEmpty(slot.GroupName, existing.GroupName);
                existing.SourceComment = FirstNonEmpty(slot.SourceComment, existing.SourceComment);
                existing.Editability = FirstNonEmpty(slot.Editability, existing.Editability);
                existing.SafeRangeDescription = FirstNonEmpty(slot.SafeRangeDescription, existing.SafeRangeDescription);
                existing.ByteLength = slot.ByteLength == 0 ? existing.ByteLength : slot.ByteLength;
                existing.DefinitionInstructionAddress ??= slot.DefinitionInstructionAddress;
                existing.OperandFileOffset ??= slot.OperandFileOffset;
                existing.OperandOffset ??= slot.OperandOffset;
                existing.SourceKind = FirstNonEmpty(slot.SourceKind, existing.SourceKind);
                existing.IsDirectlyPatchable |= slot.IsDirectlyPatchable;
                if (existing.DefinitionChain.Count == 0) existing.DefinitionChain = slot.DefinitionChain.ToList();
                existing.Value ??= slot.Value;
            }

            return;
        }

        candidate.ParameterSlots.Add(slot);
    }

    private static void ApplyPrimaryParameterFields(InjectedEffectCandidate candidate)
    {
        var personal = candidate.ParameterSlots.FirstOrDefault(slot => slot.Role == InjectedEffectParameterRole.Personal && slot.Value.HasValue);
        if (personal != null)
        {
            candidate.PersonalIdPatchAddress ??= personal.Address;
            candidate.PersonalEffectId ??= personal.Value;
        }

        var equipment = candidate.ParameterSlots.FirstOrDefault(slot => slot.Role == InjectedEffectParameterRole.Equipment && slot.Value.HasValue);
        if (equipment != null)
        {
            candidate.EquipmentIdPatchAddress ??= equipment.Address;
            candidate.EquipmentEffectId ??= equipment.Value;
        }
    }

    private static void AssignSignatureSlotsToCheckGroups(InjectedEffectCandidate candidate)
    {
        if (candidate.CheckGroups.Count == 0 || candidate.ParameterSlots.Count == 0) return;

        foreach (var group in candidate.CheckGroups)
        {
            var matchingSlots = candidate.ParameterSlots
                .Where(slot => !string.IsNullOrWhiteSpace(slot.GroupName) &&
                               !slot.SourceComment.Contains("代码洞判定桩", StringComparison.Ordinal) &&
                               (slot.GroupName.Contains(group.GroupName, StringComparison.Ordinal) ||
                                group.GroupName.Contains(slot.GroupName, StringComparison.Ordinal)))
                .ToArray();
            group.EquipmentSlot = matchingSlots.FirstOrDefault(slot => slot.Role == InjectedEffectParameterRole.Equipment) ?? group.EquipmentSlot;
            group.PersonalSlot = matchingSlots.FirstOrDefault(slot => slot.Role == InjectedEffectParameterRole.Personal) ?? group.PersonalSlot;
        }
    }

    private static void MergeKnownLabels(InjectedEffectDiscoveryReport report)
    {
        var knownByPersonal = KnownEffectLabels()
            .Where(item => item.PersonalEffectId.HasValue)
            .GroupBy(item => item.PersonalEffectId!.Value)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var candidate in report.Candidates)
        {
            if (!candidate.PersonalEffectId.HasValue ||
                !knownByPersonal.TryGetValue(candidate.PersonalEffectId.Value, out var label) ||
                !candidate.Name.Contains("未知", StringComparison.Ordinal))
            {
                continue;
            }

            candidate.Name = label.Name;
            if (label.HookAddress.HasValue && string.IsNullOrWhiteSpace(candidate.HookPoint))
            {
                candidate.HookPoint = FormatVa(label.HookAddress.Value);
            }

            if (candidate.Confidence == "InlineStubDetected")
            {
                candidate.Confidence = "KnownEffectIdInlineStub";
            }

            candidate.Evidence = AppendEvidence(candidate.Evidence, "label=knowledge-base-effect-id");
        }
    }

    private static void AttachDefaultModules(InjectedEffectDiscoveryReport report)
    {
        foreach (var candidate in report.Candidates)
        {
            if (candidate.Modules.Count > 0) continue;

            if (candidate.PatternKind == InjectedEffectPatternKind.InlineCoreStub)
            {
                candidate.Modules = BuildInlineStubModules(candidate, [], candidate.Address);
            }
            else
            {
                candidate.Modules = BuildModules(candidate, null);
            }

            candidate.ModuleSummary = BuildModuleSummary(candidate);
        }
    }

    private static void AddIndirectHookDiagnostics(InjectedEffectDiscoveryReport report, X86ScanResult instructionScan)
    {
        foreach (var instruction in instructionScan.Instructions)
        {
            if (!instruction.IsIndirectCall && !instruction.IsIndirectJump) continue;
            AddDiagnostic(
                report,
                "indirectPatchCandidate",
                instruction.Address,
                null,
                "Indirect call/jump found; may represent wrapper, dispatch table, or function-pointer patch.",
                instruction.Mnemonic + " " + string.Join(",", instruction.Operands.Select(FormatX86Operand)));
        }
    }

    private static void AddHookTargetDiagnostics(InjectedEffectDiscoveryReport report)
    {
        var representedHooks = report.Candidates
            .Where(candidate => candidate.JumpOutAddress.HasValue)
            .Select(candidate => candidate.JumpOutAddress!.Value)
            .ToHashSet();

        foreach (var hook in report.HookCandidates)
        {
            if (representedHooks.Contains(hook.Address)) continue;
            AddDiagnostic(
                report,
                "hookTargetNoCfg",
                hook.Address,
                hook.Target,
                "Jump candidate did not satisfy four-module/control-flow recognition.",
                hook.Evidence);
        }
    }

    private static void ApplyDetectionDefaults(InjectedEffectDiscoveryReport report)
    {
        foreach (var candidate in report.Candidates)
        {
            if (candidate.DetectionScore == 0)
            {
                candidate.DetectionScore = candidate.Confidence switch
                {
                    "KnownPatchExact" => 100,
                    "KnownPatchVariant" => 88,
                    "KnownEffectIdInlineStub" => 76,
                    "InlineStubDetected" => 70,
                    "FourModuleLikeCandidate" => 64,
                    "CallToCoreEngine" => 45,
                    _ => candidate.PatternKind == InjectedEffectPatternKind.KnownPatch ? 95 : 50
                };
            }

            if (string.IsNullOrWhiteSpace(candidate.DetectionLevel))
            {
                candidate.DetectionLevel = candidate.DetectionScore >= 95
                    ? "KnownExact"
                    : candidate.DetectionScore >= 80
                        ? "KnownVariant"
                        : candidate.DetectionScore >= 60
                            ? "SemanticCandidate"
                            : "DiagnosticOnly";
            }

            if (candidate.PatternKind == InjectedEffectPatternKind.KnownPatch &&
                string.IsNullOrWhiteSpace(candidate.NormalizedSignatureId))
            {
                candidate.NormalizedSignatureId = "exact:" + candidate.Name;
            }

            if (candidate.MatchedAnchors.Count == 0)
            {
                if (candidate.PatternKind == InjectedEffectPatternKind.KnownPatch) candidate.MatchedAnchors.Add("known-signature");
                if (candidate.CodeCaveEntryAddress.HasValue) candidate.MatchedAnchors.Add("code-cave:" + FormatVa(candidate.CodeCaveEntryAddress.Value));
                if (candidate.JumpOutAddress.HasValue) candidate.MatchedAnchors.Add("hook:" + FormatVa(candidate.JumpOutAddress.Value));
                if (candidate.CheckGroups.Any(group => group.GuardFunctionAddress == CoreEffectEngineAddress)) candidate.MatchedAnchors.Add("core-call:004101D9");
            }
        }

        report.DiagnosticCounts.Clear();
        foreach (var group in report.Diagnostics.GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase))
        {
            report.DiagnosticCounts[group.Key] = group.Count();
        }

        AddCandidateDerivedDiagnostics(report);
        report.DiagnosticSummaries = BuildDiagnosticSummaries(report);
    }

    private static void AddCandidateDerivedDiagnostics(InjectedEffectDiscoveryReport report)
    {
        var existing = report.Diagnostics
            .Select(item => (item.Category, Address: item.Address ?? 0, Target: item.Target ?? 0))
            .ToHashSet();

        foreach (var candidate in report.Candidates)
        {
            if (candidate.FailureReasons.Any(reason => reason.Contains("coreCallUnparsed", StringComparison.OrdinalIgnoreCase)))
            {
                AddDiagnosticIfMissing(
                    report,
                    existing,
                    "coreCallUnparsed",
                    candidate.Address,
                    candidate.CodeCaveEntryAddress,
                    "Candidate contains a 004101D9 path but parameter recovery is incomplete.",
                    string.Join("; ", candidate.FailureReasons.Take(4)));
            }

            if (candidate.MissingAnchors.Count > 0 &&
                candidate.MatchedAnchors.Count > 0 &&
                candidate.DetectionLevel.Equals("KnownVariant", StringComparison.OrdinalIgnoreCase))
            {
                AddDiagnosticIfMissing(
                    report,
                    existing,
                    "partialSignatureOnly",
                    candidate.Address,
                    candidate.CodeCaveEntryAddress,
                    "Known signature is only partially represented in the current EXE.",
                    "matched=" + string.Join(",", candidate.MatchedAnchors.Take(4)) + "; missing=" + string.Join(",", candidate.MissingAnchors.Take(4)));
            }

            if (candidate.PatchCategory == InjectedEffectPatchCategory.ComplexMultiHookPatch)
            {
                AddDiagnosticIfMissing(
                    report,
                    existing,
                    "complexPatchGrouped",
                    candidate.Address,
                    candidate.CodeCaveEntryAddress,
                    "Complex multi-hook patch grouped as read-only semantic evidence.",
                    candidate.Name + "; " + candidate.ModuleSummary);
            }
        }

        report.DiagnosticCounts.Clear();
        foreach (var group in report.Diagnostics.GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase))
        {
            report.DiagnosticCounts[group.Key] = group.Count();
        }
    }

    private static void AddDiagnosticIfMissing(
        InjectedEffectDiscoveryReport report,
        ISet<(string Category, uint Address, uint Target)> existing,
        string category,
        uint? address,
        uint? target,
        string summary,
        string evidence)
    {
        var key = (category, address ?? 0, target ?? 0);
        if (!existing.Add(key)) return;
        AddDiagnostic(report, category, address, target, summary, evidence);
    }

    private static List<InjectedEffectDiagnosticSummary> BuildDiagnosticSummaries(InjectedEffectDiscoveryReport report)
        => report.Diagnostics
            .GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new InjectedEffectDiagnosticSummary
            {
                Category = group.Key,
                Count = group.Count(),
                Meaning = ExplainDiagnosticCategory(group.Key),
                RecommendedAction = RecommendDiagnosticAction(group.Key),
                SampleAddresses = group
                    .Select(item => string.IsNullOrWhiteSpace(item.AddressHex) ? item.TargetHex : item.AddressHex)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList()
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string ExplainDiagnosticCategory(string category)
        => category switch
        {
            "coreCallUnparsed" => "Found 004101D9 usage but did not recover the four stack parameters.",
            "wrapperCoreCallUnparsed" => "Found a wrapper that reaches 004101D9 but caller parameters are incomplete.",
            "hookTargetNoCfg" => "Jump candidate target did not match the semantic/four-module recognizers.",
            "partialSignatureOnly" => "Known sample signature matched only partially; relocation or parameter mutation is likely.",
            "indirectPatchCandidate" => "Indirect call/jump may represent wrapper, dispatch table, or function pointer patching.",
            "complexPatchGrouped" => "Complex multi-hook or extension patch was grouped for read-only review.",
            _ => "Diagnostic-only evidence; do not treat as an installed special-effect patch without review."
        };

    private static string RecommendDiagnosticAction(string category)
        => category switch
        {
            "coreCallUnparsed" => "Inspect nearby instructions and extend parameter slicing before generating local-agent write drafts.",
            "wrapperCoreCallUnparsed" => "Review wrapper caller convention and confirm stack/register arguments.",
            "hookTargetNoCfg" => "Open target block in disassembly; keep as hook diagnostic unless CFG evidence improves.",
            "partialSignatureOnly" => "Compare normalized signature roles and relocated rel32 values against the sample.",
            "indirectPatchCandidate" => "Check pointer tables and FF /2 or FF /4 targets before classification.",
            "complexPatchGrouped" => "Use prompt-only/manual-review flow; never generate a single-hook patch automatically.",
            _ => "Keep in diagnostics and require manual/dynamic validation."
        };

    private static void AddDiagnostic(
        InjectedEffectDiscoveryReport report,
        string category,
        uint? address,
        uint? target,
        string summary,
        string evidence)
    {
        report.Diagnostics.Add(new InjectedEffectDiagnostic
        {
            Category = category,
            Address = address,
            AddressHex = address.HasValue ? FormatVa(address.Value) : string.Empty,
            Target = target,
            TargetHex = target.HasValue ? FormatVa(target.Value) : string.Empty,
            Summary = summary,
            Evidence = evidence
        });
    }

    private static string FormatX86Operand(X86OperandInfo operand)
        => operand.Kind switch
        {
            "Register" => operand.Register,
            "Immediate" => "0x" + (operand.Immediate ?? 0).ToString("X", CultureInfo.InvariantCulture),
            "Branch" => operand.BranchTarget.HasValue ? FormatVa(operand.BranchTarget.Value) : "branch",
            "Memory" => operand.MemoryText,
            _ => operand.Kind
        };

    private static BodyAnalysis? AnalyzeCodeBodyWithInstructions(byte[] bytes, uint baseAddress)
    {
        var instructions = new X86InstructionScanner().DecodeBlock(bytes, baseAddress);
        if (instructions.Count == 0) return null;

        var pushes = new List<PushImmediate>();
        var calls = new List<RelativeInstruction>();
        var jumps = new List<RelativeInstruction>();
        var conditionalJumps = new List<RelativeInstruction>();
        var tests = new List<uint>();

        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            if (instruction.Mnemonic == "push")
            {
                if (instruction.Operands.FirstOrDefault() is { Kind: "Immediate", Immediate: { } value } operand)
                {
                    pushes.Add(new PushImmediate(
                        instruction.FileOffset,
                        instruction.Address,
                        checked(instruction.Address + 1),
                        value,
                        operand.ImmediateSize,
                        operand.ImmediateSize == 1));
                }
            }

            if (instruction.IsDirectCall && instruction.BranchTarget.HasValue)
            {
                calls.Add(new RelativeInstruction(instruction.Address, instruction.BranchTarget.Value, "call", instruction.Length));
            }
            else if (instruction.IsDirectJump && instruction.BranchTarget.HasValue)
            {
                jumps.Add(new RelativeInstruction(instruction.Address, instruction.BranchTarget.Value, "jmp", instruction.Length));
            }
            else if (instruction.IsConditionalBranch && instruction.BranchTarget.HasValue)
            {
                conditionalJumps.Add(new RelativeInstruction(instruction.Address, instruction.BranchTarget.Value, "jcc", instruction.Length));
            }
            else if (instruction.IsReturn)
            {
                jumps.Add(new RelativeInstruction(instruction.Address, instruction.EndAddress, "ret", instruction.Length));
            }

            if (instruction.Mnemonic is "test" or "cmp")
            {
                tests.Add(instruction.Address);
            }
        }

        var checkGroups = BuildBodyCheckGroups(pushes, calls, conditionalJumps, jumps, baseAddress, Math.Min(bytes.Length, BodyScanBytes));
        var primaryGroup = checkGroups.FirstOrDefault();
        var call = primaryGroup?.GuardCall ?? SelectGuardCall(pushes, calls);
        if (call == null)
        {
            return new BodyAnalysis(
                BaseAddress: baseAddress,
                GuardStartAddress: null,
                FeatureStartAddress: null,
                ReturnAddress: jumps.FirstOrDefault()?.Target,
                EquipmentEffectId: null,
                PersonalEffectId: null,
                EquipmentIdPatchAddress: null,
                PersonalIdPatchAddress: null,
                EquipmentPush: null,
                PersonalPush: null,
                GuardCall: null,
                ConditionalBranch: null,
                ReturnJump: jumps.FirstOrDefault(),
                CheckGroups: [],
                HasCoreCall: calls.Any(item => item.Target == CoreEffectEngineAddress),
                HasTestOrCompare: tests.Count > 0,
                IsFourModuleLike: false);
        }

        var guardPushes = primaryGroup?.GuardPushes.ToArray() ??
                          pushes.Where(push => push.InstructionAddress < call.Address && call.Address - push.InstructionAddress <= 48)
                              .OrderBy(push => push.InstructionAddress)
                              .TakeLast(4)
                              .ToArray();
        var equipmentPush = guardPushes.Length >= 2 ? guardPushes[^2] : null;
        var personalPush = guardPushes.Length >= 1 ? guardPushes[^1] : null;
        var jcc = primaryGroup?.ConditionalBranch ??
                  conditionalJumps.FirstOrDefault(jump => jump.Address > call.Address && jump.Address - call.Address <= 64);
        var returnJump = primaryGroup?.ReturnJump ??
                         jumps.Where(jump => jump.Address > call.Address)
                             .OrderBy(jump => IsExternalTarget(jump.Target, baseAddress, Math.Min(bytes.Length, BodyScanBytes)) ? 0 : 1)
                             .ThenBy(jump => jump.Address)
                             .FirstOrDefault();
        var returnAddress = primaryGroup?.ReturnAddress ?? returnJump?.Target;
        if (!returnAddress.HasValue &&
            jcc != null &&
            IsExternalTarget(jcc.Target, baseAddress, Math.Min(bytes.Length, BodyScanBytes)))
        {
            returnAddress = jcc.Target;
        }

        var featureStart = jcc == null ? call.EndAddress : jcc.EndAddress;
        var isFourModuleLike = call != null &&
                               equipmentPush != null &&
                               personalPush != null &&
                               (jcc != null || tests.Count > 0) &&
                               returnAddress.HasValue;

        return new BodyAnalysis(
            BaseAddress: baseAddress,
            GuardStartAddress: equipmentPush?.InstructionAddress ?? guardPushes.FirstOrDefault()?.InstructionAddress,
            FeatureStartAddress: featureStart,
            ReturnAddress: returnAddress,
            EquipmentEffectId: equipmentPush?.RawValue,
            PersonalEffectId: personalPush?.RawValue,
            EquipmentIdPatchAddress: equipmentPush?.OperandAddress,
            PersonalIdPatchAddress: personalPush?.OperandAddress,
            EquipmentPush: equipmentPush,
            PersonalPush: personalPush,
            GuardCall: call,
            ConditionalBranch: jcc,
            ReturnJump: returnJump,
            CheckGroups: checkGroups,
            HasCoreCall: calls.Any(item => item.Target == CoreEffectEngineAddress),
            HasTestOrCompare: tests.Count > 0,
            IsFourModuleLike: isFourModuleLike);
    }

    private static BodyAnalysis AnalyzeCodeBody(byte[] bytes, uint baseAddress)
    {
        var decoded = AnalyzeCodeBodyWithInstructions(bytes, baseAddress);
        if (decoded != null) return decoded;

        var pushes = new List<PushImmediate>();
        var calls = new List<RelativeInstruction>();
        var jumps = new List<RelativeInstruction>();
        var conditionalJumps = new List<RelativeInstruction>();
        var tests = new List<uint>();

        var max = Math.Min(bytes.Length, BodyScanBytes);
        for (var offset = 0; offset < max;)
        {
            var address = checked(baseAddress + (uint)offset);
            var opcode = bytes[offset];
            if (opcode == 0x6A && offset + 1 < max)
            {
                pushes.Add(new PushImmediate(
                    offset,
                    address,
                    checked(address + 1),
                    bytes[offset + 1],
                    1,
                    IsImm8: true));
                offset += 2;
                continue;
            }

            if (opcode == 0x68 && offset + 4 < max)
            {
                pushes.Add(new PushImmediate(
                    offset,
                    address,
                    checked(address + 1),
                    unchecked((int)BitConverter.ToUInt32(bytes, offset + 1)),
                    4,
                    IsImm8: false));
                offset += 5;
                continue;
            }

            if ((opcode == 0xE8 || opcode == 0xE9) && offset + 4 < max)
            {
                var target = ResolveRelativeTarget(bytes, offset, address);
                var instruction = new RelativeInstruction(address, target, opcode == 0xE8 ? "call" : "jmp", 5);
                if (opcode == 0xE8) calls.Add(instruction); else jumps.Add(instruction);
                offset += 5;
                continue;
            }

            if (opcode == 0xEB && offset + 1 < max)
            {
                var target = unchecked((uint)(address + 2 + (sbyte)bytes[offset + 1]));
                jumps.Add(new RelativeInstruction(address, target, "jmp short", 2));
                offset += 2;
                continue;
            }

            if (opcode is >= 0x70 and <= 0x7F && offset + 1 < max)
            {
                var target = unchecked((uint)(address + 2 + (sbyte)bytes[offset + 1]));
                conditionalJumps.Add(new RelativeInstruction(address, target, JccName(opcode), 2));
                offset += 2;
                continue;
            }

            if (opcode == 0x0F && offset + 5 < max && bytes[offset + 1] is >= 0x80 and <= 0x8F)
            {
                var target = unchecked((uint)(address + 6 + BitConverter.ToInt32(bytes, offset + 2)));
                conditionalJumps.Add(new RelativeInstruction(address, target, JccName(bytes[offset + 1]), 6));
                offset += 6;
                continue;
            }

            if (opcode == 0x85 && offset + 1 < max && bytes[offset + 1] == 0xC0)
            {
                tests.Add(address);
                offset += 2;
                continue;
            }

            if (opcode == 0x83 && offset + 2 < max && bytes[offset + 1] == 0xF8 && bytes[offset + 2] == 0x00)
            {
                tests.Add(address);
                offset += 3;
                continue;
            }

            if (opcode == 0x3D && offset + 4 < max)
            {
                tests.Add(address);
                offset += 5;
                continue;
            }

            offset++;
        }

        var checkGroups = BuildBodyCheckGroups(pushes, calls, conditionalJumps, jumps, baseAddress, max);
        var primaryGroup = checkGroups.FirstOrDefault();
        var call = primaryGroup?.GuardCall ?? SelectGuardCall(pushes, calls);
        var guardPushes = primaryGroup?.GuardPushes.ToArray() ?? (call == null
            ? Array.Empty<PushImmediate>()
            : pushes.Where(push => push.InstructionAddress < call.Address && call.Address - push.InstructionAddress <= 32)
                .OrderBy(push => push.InstructionAddress)
                .TakeLast(Math.Min(4, pushes.Count))
                .ToArray());

        var equipmentPush = guardPushes.Length >= 2 ? guardPushes[^2] : null;
        var personalPush = guardPushes.Length >= 1 ? guardPushes[^1] : null;
        var jcc = call == null
            ? null
            : conditionalJumps.FirstOrDefault(jump => jump.Address > call.Address && jump.Address - call.Address <= 32);
        var returnJump = jumps
            .Where(jump => jump.Address >= baseAddress)
            .OrderByDescending(jump => jump.Address)
            .FirstOrDefault();
        var returnAddress = returnJump?.Target;
        if (returnAddress == null && jcc != null && (jcc.Target < baseAddress || jcc.Target > baseAddress + max))
        {
            returnAddress = jcc.Target;
        }

        var featureStart = jcc == null ? call?.EndAddress : jcc.EndAddress;
        var isFourModuleLike = call != null &&
                               equipmentPush != null &&
                               personalPush != null &&
                               jcc != null &&
                               returnAddress.HasValue;

        return new BodyAnalysis(
            BaseAddress: baseAddress,
            GuardStartAddress: equipmentPush?.InstructionAddress ?? guardPushes.FirstOrDefault()?.InstructionAddress,
            FeatureStartAddress: featureStart,
            ReturnAddress: returnAddress,
            EquipmentEffectId: equipmentPush?.RawValue,
            PersonalEffectId: personalPush?.RawValue,
            EquipmentIdPatchAddress: equipmentPush?.OperandAddress,
            PersonalIdPatchAddress: personalPush?.OperandAddress,
            EquipmentPush: equipmentPush,
            PersonalPush: personalPush,
            GuardCall: call,
            ConditionalBranch: jcc,
            ReturnJump: returnJump,
            CheckGroups: checkGroups,
            HasCoreCall: calls.Any(item => item.Target == CoreEffectEngineAddress),
            HasTestOrCompare: tests.Count > 0,
            IsFourModuleLike: isFourModuleLike);
    }

    private static IReadOnlyList<BodyCheckGroup> BuildBodyCheckGroups(
        IReadOnlyList<PushImmediate> pushes,
        IReadOnlyList<RelativeInstruction> calls,
        IReadOnlyList<RelativeInstruction> conditionalJumps,
        IReadOnlyList<RelativeInstruction> jumps,
        uint baseAddress,
        int scanLength)
    {
        var groups = new List<BodyCheckGroup>();
        foreach (var call in calls)
        {
            var guardPushes = pushes
                .Where(push => push.InstructionAddress < call.Address && call.Address - push.InstructionAddress <= 32)
                .OrderBy(push => push.InstructionAddress)
                .TakeLast(4)
                .ToArray();
            if (guardPushes.Length < 2) continue;

            var conditionalBranch = conditionalJumps
                .Where(jump => jump.Address > call.Address && jump.Address - call.Address <= 48)
                .OrderBy(jump => jump.Address)
                .FirstOrDefault();
            var returnJump = jumps
                .Where(jump => jump.Address > call.Address)
                .OrderBy(jump => IsExternalTarget(jump.Target, baseAddress, scanLength) ? 0 : 1)
                .ThenBy(jump => jump.Address)
                .FirstOrDefault();
            var returnAddress = returnJump?.Target;
            if (!returnAddress.HasValue &&
                conditionalBranch != null &&
                IsExternalTarget(conditionalBranch.Target, baseAddress, scanLength))
            {
                returnAddress = conditionalBranch.Target;
            }

            groups.Add(new BodyCheckGroup(
                GuardPushes: guardPushes,
                GuardCall: call,
                ConditionalBranch: conditionalBranch,
                ReturnJump: returnJump,
                ReturnAddress: returnAddress));
        }

        return groups;
    }

    private static bool IsExternalTarget(uint target, uint baseAddress, int scanLength)
        => target < baseAddress || target > baseAddress + scanLength;

    private static RelativeInstruction? SelectGuardCall(IReadOnlyList<PushImmediate> pushes, IReadOnlyList<RelativeInstruction> calls)
    {
        foreach (var call in calls)
        {
            var nearbyPushes = pushes
                .Where(push => push.InstructionAddress < call.Address && call.Address - push.InstructionAddress <= 32)
                .OrderBy(push => push.InstructionAddress)
                .ToArray();
            if (nearbyPushes.Length >= 2)
            {
                return call;
            }
        }

        return null;
    }

    private static List<InjectedEffectModuleInfo> BuildModules(InjectedEffectCandidate candidate, BodyAnalysis? body)
    {
        var modules = new List<InjectedEffectModuleInfo>();
        if (candidate.JumpOutAddress.HasValue)
        {
            modules.Add(new InjectedEffectModuleInfo
            {
                ModuleName = "模块 1：跳出点",
                Address = candidate.JumpOutAddress,
                Role = "从原引擎流程跳到代码洞",
                CurrentContent = candidate.CodeCaveEntryAddress.HasValue ? "jmp " + FormatVa(candidate.CodeCaveEntryAddress.Value) : "jmp 代码洞",
                Editable = "不建议直接编辑",
                Description = "这里通常只放 5 字节 E9 相对跳转；改错会破坏原流程。"
            });
        }

        if (candidate.CodeCaveEntryAddress.HasValue || candidate.GuardStartAddress.HasValue)
        {
            modules.Add(new InjectedEffectModuleInfo
            {
                ModuleName = "模块 2：桩函数 + 功能函数",
                Address = candidate.CodeCaveEntryAddress ?? candidate.GuardStartAddress,
                Role = "先检查是否拥有特技，通过后执行补丁主体",
                CurrentContent = BuildGuardContent(candidate, body),
                Editable = "功能汇编可编辑",
                Description = BuildGuardDescription(candidate, body)
            });
        }

        foreach (var checkGroup in candidate.CheckGroups)
        {
            modules.Add(new InjectedEffectModuleInfo
            {
                ModuleName = "判定组：" + FirstNonEmpty(checkGroup.GroupName, "未命名"),
                Address = checkGroup.GuardStartAddress ?? checkGroup.GuardCallAddress,
                Role = "检查是否拥有对应特技",
                CurrentContent = BuildCheckGroupContent(checkGroup),
                Editable = "不建议直接编辑",
                Description = FirstNonEmpty(checkGroup.Diagnosis, "识别到一组特技判定调用。")
            });
        }

        if (candidate.PersonalEffectId.HasValue || candidate.PersonalIdPatchAddress.HasValue)
        {
            modules.Add(new InjectedEffectModuleInfo
            {
                ModuleName = "模块 3：个人号",
                Address = candidate.PersonalIdPatchAddress,
                Role = "个人特技编号",
                CurrentContent = FormatEffectId(candidate.PersonalEffectId),
                Editable = IsUnsafeImm8(body?.PersonalPush) ? "需改写指令后再编辑" : "可编辑",
                Description = BuildParameterDescription(body?.PersonalPush)
            });
        }

        if (candidate.EquipmentEffectId.HasValue || candidate.EquipmentIdPatchAddress.HasValue)
        {
            modules.Add(new InjectedEffectModuleInfo
            {
                ModuleName = "模块 4：装备号",
                Address = candidate.EquipmentIdPatchAddress,
                Role = "装备特技编号",
                CurrentContent = FormatEffectId(candidate.EquipmentEffectId),
                Editable = IsUnsafeImm8(body?.EquipmentPush) ? "需改写指令后再编辑" : "可编辑",
                Description = BuildParameterDescription(body?.EquipmentPush)
            });
        }

        if (candidate.ReturnAddress.HasValue)
        {
            modules.Add(new InjectedEffectModuleInfo
            {
                ModuleName = "回到原流程",
                Address = candidate.ReturnAddress,
                Role = "判定失败或功能结束后回到原引擎",
                CurrentContent = FormatVa(candidate.ReturnAddress.Value),
                Editable = "不建议直接编辑",
                Description = "回跳地址应等于 Hook 地址加覆盖长度，或等于人工确认的续接点。"
            });
        }

        foreach (var slot in candidate.ParameterSlots
                     .Where(slot => slot.Address.HasValue)
                     .OrderBy(slot => slot.Address!.Value)
                     .ThenBy(slot => slot.DisplayName, StringComparer.CurrentCulture))
        {
            modules.Add(new InjectedEffectModuleInfo
            {
                ModuleName = "参数位：" + FirstNonEmpty(slot.DisplayName, FormatParameterRole(slot.Role)),
                Address = slot.Address,
                Role = FormatParameterRole(slot.Role),
                CurrentContent = FirstNonEmpty(slot.ValueText, $"长度 {slot.ByteLength} 字节"),
                Editable = FirstNonEmpty(slot.Editability, "需人工确认"),
                Description = BuildParameterSlotDescription(slot)
            });
        }

        return modules;
    }

    private static List<InjectedEffectModuleInfo> BuildInlineStubModules(
        InjectedEffectCandidate candidate,
        IReadOnlyList<PushImmediate> pushes,
        uint callAddress)
    {
        var modules = new List<InjectedEffectModuleInfo>
        {
            new()
            {
                ModuleName = "内联特技桩",
                Address = candidate.GuardStartAddress ?? callAddress,
                Role = "调用引擎内置特技判定函数",
                CurrentContent = "call " + FormatVa(CoreEffectEngineAddress),
                Editable = "不建议直接编辑",
                Description = "这是原地调用 4101D9 的判定桩；它本身不一定包含完整跳出点和功能函数。"
            }
        };

        if (candidate.EffectValueFlag.HasValue)
        {
            modules.Add(new InjectedEffectModuleInfo
            {
                ModuleName = "效果值标志",
                Address = pushes.Count >= 4 ? pushes[^4].OperandAddress : null,
                Role = "决定是否读取特效值",
                CurrentContent = FormatEffectId(candidate.EffectValueFlag),
                Editable = "谨慎编辑",
                Description = "通常 0 表示不读特效值，1 表示读取特效值。"
            });
        }

        if (candidate.StackingFlag.HasValue)
        {
            modules.Add(new InjectedEffectModuleInfo
            {
                ModuleName = "叠加标志",
                Address = pushes.Count >= 3 ? pushes[^3].OperandAddress : null,
                Role = "决定多个来源是否叠加",
                CurrentContent = FormatEffectId(candidate.StackingFlag),
                Editable = "谨慎编辑",
                Description = "通常 0 表示不叠加，1 表示叠加。"
            });
        }

        if (candidate.PersonalEffectId.HasValue || candidate.PersonalIdPatchAddress.HasValue)
        {
            modules.Add(new InjectedEffectModuleInfo
            {
                ModuleName = "个人号",
                Address = candidate.PersonalIdPatchAddress,
                Role = "个人特技编号",
                CurrentContent = FormatEffectId(candidate.PersonalEffectId),
                Editable = "可编辑",
                Description = "来自调用 4101D9 前的个人号入栈参数。"
            });
        }

        if (candidate.EquipmentEffectId.HasValue || candidate.EquipmentIdPatchAddress.HasValue)
        {
            modules.Add(new InjectedEffectModuleInfo
            {
                ModuleName = "装备号",
                Address = candidate.EquipmentIdPatchAddress,
                Role = "装备特技编号",
                CurrentContent = FormatEffectId(candidate.EquipmentEffectId),
                Editable = "可编辑",
                Description = "来自调用 4101D9 前的装备号入栈参数。"
            });
        }

        return modules;
    }

    private static string BuildCheckGroupContent(InjectedEffectCheckGroup group)
    {
        var parts = new List<string>();
        if (group.EquipmentSlot?.Value != null) parts.Add("装备/宝物号 " + group.EquipmentSlot.ValueText);
        if (group.PersonalSlot?.Value != null) parts.Add("个人/特技号 " + group.PersonalSlot.ValueText);
        if (group.GuardFunctionAddress.HasValue) parts.Add("调用判定函数 " + FormatVa(group.GuardFunctionAddress.Value));
        if (group.FailureBranchAddress.HasValue) parts.Add("失败分支 " + FormatVa(group.FailureBranchAddress.Value));
        return parts.Count == 0 ? "判定结构" : string.Join("；", parts);
    }

    private static string BuildParameterSlotDescription(InjectedEffectParameterSlot slot)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(slot.GroupName)) parts.Add("分组：" + slot.GroupName);
        if (!string.IsNullOrWhiteSpace(slot.SafeRangeDescription)) parts.Add(slot.SafeRangeDescription);
        if (!string.IsNullOrWhiteSpace(slot.SourceComment)) parts.Add("来源：" + NormalizeCommentLabel(slot.SourceComment));
        return string.Join("；", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildGuardContent(InjectedEffectCandidate candidate, BodyAnalysis? body)
    {
        var parts = new List<string>();
        if (candidate.EquipmentEffectId.HasValue) parts.Add("装备号 " + FormatEffectId(candidate.EquipmentEffectId));
        if (candidate.PersonalEffectId.HasValue) parts.Add("个人号 " + FormatEffectId(candidate.PersonalEffectId));
        if (body?.GuardCall != null) parts.Add("调用判定函数 " + FormatVa(body.GuardCall.Target));
        if (body?.ConditionalBranch != null) parts.Add("失败时跳过功能函数");
        return parts.Count == 0 ? "代码洞主体" : string.Join("；", parts);
    }

    private static string BuildGuardDescription(InjectedEffectCandidate candidate, BodyAnalysis? body)
    {
        if (body?.IsFourModuleLike == true)
        {
            return "结构符合常见特技补丁：未拥有特技时回到原流程，拥有特技时继续执行功能函数。";
        }

        if (candidate.PatternKind == InjectedEffectPatternKind.KnownPatch)
        {
            return "来自本地补丁文本签名；具体分支语义需要结合补丁说明或调试验证。";
        }

        return "已定位代码洞主体，但未完整证明四模块条件伤害结构。";
    }

    private static string BuildParameterDescription(PushImmediate? push)
    {
        if (push == null) return "来自补丁文本中的独立参数位置。";
        if (push.IsImm8 && push.RawValue > 0x7F)
        {
            return "当前位置是 6A imm8。0x80-0xFF 会被 CPU 符号扩展，不应只改一个字节；需要改写为 68 imm32 后再写入。";
        }

        return push.IsImm8
            ? "当前位置是 6A imm8，适合 0x00-0x7F 范围内的编号。"
            : "当前位置是 68 imm32，可安全表达 0x00-0xFFFF 范围内的编号。";
    }

    private static bool IsUnsafeImm8(PushImmediate? push)
        => push is { IsImm8: true, RawValue: > 0x7F };

    private static string BuildModuleSummary(InjectedEffectCandidate candidate)
        => candidate.Modules.Count == 0
            ? string.Empty
            : string.Join("；", candidate.Modules.Select(module => $"{module.ModuleName} {module.AddressHex}".Trim()));

    private static IEnumerable<PatchSignature> LoadPatchSignatures(string workspaceRoot)
    {
        var root = ResolvePatchSignatureRoot(workspaceRoot);
        if (root == null) yield break;

        foreach (var path in Directory.EnumerateFiles(root, "*.txt", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            PatchSignature? signature = null;
            try
            {
                signature = ParsePatchSignature(path);
            }
            catch
            {
                // 历史补丁文本格式不统一，坏样本不应阻塞 EXE 扫描。
            }

            if (signature != null && signature.Segments.Count > 0)
            {
                yield return signature;
            }
        }
    }

    private static string? ResolvePatchSignatureRoot(string workspaceRoot)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            candidates.Add(Path.Combine(workspaceRoot, "特效整理", "6.5"));
            var parent = Directory.GetParent(workspaceRoot)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent)) candidates.Add(Path.Combine(parent, "特效整理", "6.5"));
        }

        candidates.Add(Path.Combine(Environment.CurrentDirectory, "特效整理", "6.5"));
        return candidates
            .Select(path =>
            {
                try { return Path.GetFullPath(path); }
                catch { return path; }
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(Directory.Exists);
    }

    private static PatchSignature? ParsePatchSignature(string path)
    {
        var lines = ReadAllLinesSmart(path);
        var segments = new List<PatchSignatureSegment>();
        uint? currentAddress = null;
        var bytes = new List<byte>();
        var comments = new StringBuilder();
        var hookAddresses = new List<uint>();
        var pendingComments = new List<string>();

        void Flush()
        {
            if (!currentAddress.HasValue) return;
            if (bytes.Count > 0)
            {
                var comment = string.Join(" / ", pendingComments.Where(item => !string.IsNullOrWhiteSpace(item)));
                var segment = new PatchSignatureSegment(currentAddress.Value, bytes.ToArray(), comment);
                segments.Add(segment);
                if (bytes[0] == 0xE9 ||
                    bytes[0] == 0xE8 ||
                    (bytes.Count >= 2 && bytes[0] == 0x0F && bytes[1] is 0x84 or 0x85))
                {
                    hookAddresses.Add(currentAddress.Value);
                }
            }

            bytes.Clear();
            pendingComments.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#'))
            {
                if (currentAddress.HasValue && bytes.Count > 0)
                {
                    Flush();
                    currentAddress = null;
                }

                var comment = line.TrimStart('#').Trim();
                if (comments.Length < 1200) comments.Append(comment).Append(' ');
                pendingComments.Add(comment);
                continue;
            }

            if (IsAddressLine(line))
            {
                Flush();
                currentAddress = ParseAddress(line);
                continue;
            }

            if (!currentAddress.HasValue) continue;
            bytes.AddRange(ParseHexBytes(line));
        }

        Flush();
        if (segments.Count == 0) return null;

        var semanticText = (Path.GetFileNameWithoutExtension(path) + " " + comments).Trim();
        var label = KnownEffectLabels().FirstOrDefault(item => Path.GetFileName(path).Contains(item.Name, StringComparison.OrdinalIgnoreCase));
        var parameterSlots = BuildSignatureParameterSlots(segments);
        var patchCategory = ClassifyPatchSignature(path, semanticText, segments, hookAddresses, parameterSlots);
        return new PatchSignature
        {
            Name = label?.Name ?? BuildPatchDisplayName(path),
            SourcePath = path,
            SemanticText = semanticText,
            Segments = segments,
            HookAddresses = hookAddresses,
            PersonalEffectId = label?.PersonalEffectId ?? ExtractSegmentEffectId(segments, personal: true) ?? ExtractEffectId(semanticText, "个人"),
            EquipmentEffectId = label?.EquipmentEffectId ?? ExtractSegmentEffectId(segments, personal: false) ?? ExtractEffectId(semanticText, "装备"),
            ParameterSlots = parameterSlots,
            PatchCategory = patchCategory
        };
    }

    private static int? ExtractSegmentEffectId(IEnumerable<PatchSignatureSegment> segments, bool personal)
    {
        var segment = segments.FirstOrDefault(item =>
            item.Bytes.Length is 1 or 4 &&
            (personal ? IsPersonalComment(item.Comment) : IsEquipmentComment(item.Comment)));
        return segment == null ? null : SegmentImmediateValue(segment);
    }

    private static List<InjectedEffectParameterSlot> BuildSignatureParameterSlots(IReadOnlyList<PatchSignatureSegment> segments)
    {
        var slots = new List<InjectedEffectParameterSlot>();
        foreach (var segment in segments)
        {
            var role = ClassifyParameterRole(segment.Comment, segment.Bytes.Length);
            if (role == null) continue;
            if (!IsParameterSegment(segment, role)) continue;

            int? value = segment.Bytes.Length is 1 or 2 or 4 ? SegmentImmediateValue(segment) : null;
            slots.Add(new InjectedEffectParameterSlot
            {
                Role = role,
                DisplayName = BuildParameterDisplayName(segment.Comment, role),
                GroupName = ExtractParameterGroupName(segment.Comment, role),
                Address = segment.Address,
                Value = value,
                ByteLength = segment.Bytes.Length,
                SourceComment = segment.Comment,
                Editability = BuildSegmentEditability(role, segment.Bytes.Length, value),
                SafeRangeDescription = BuildSafeRangeDescription(segment.Bytes.Length, isImm8: segment.Bytes.Length == 1, value ?? 0)
            });
        }

        return slots;
    }

    private static string? ClassifyParameterRole(string comment, int byteLength)
    {
        if (string.IsNullOrWhiteSpace(comment)) return null;
        if (ContainsAny(comment, "宝物-个人", "寶物-個人")) return InjectedEffectParameterRole.UnknownCombined;
        if (ContainsAny(comment, "提示语", "提示語")) return InjectedEffectParameterRole.MessageText;
        if (ContainsAny(comment, "是否说话", "是否說話")) return InjectedEffectParameterRole.BooleanOption;
        if (ContainsAny(comment, "生效范围", "生效範圍")) return InjectedEffectParameterRole.Range;
        if (ContainsAny(comment, "特效值")) return InjectedEffectParameterRole.EffectValue;
        if (IsEquipmentComment(comment)) return InjectedEffectParameterRole.Equipment;
        if (IsPersonalComment(comment)) return InjectedEffectParameterRole.Personal;
        if (byteLength is 1 or 2 or 4 && ContainsAny(comment, "参数", "配置")) return InjectedEffectParameterRole.Unknown;
        return null;
    }

    private static bool IsParameterSegment(PatchSignatureSegment segment, string role)
    {
        if (role == InjectedEffectParameterRole.MessageText) return segment.Bytes.Length > 0;
        if (role == InjectedEffectParameterRole.UnknownCombined) return segment.Bytes.Length is 1 or 2 or 4;
        return segment.Bytes.Length is 1 or 2 or 4;
    }

    private static string BuildParameterDisplayName(string comment, string role)
    {
        var normalized = NormalizeCommentLabel(comment);
        if (!string.IsNullOrWhiteSpace(normalized)) return normalized;

        return role switch
        {
            InjectedEffectParameterRole.Equipment => "装备/宝物号",
            InjectedEffectParameterRole.Personal => "个人/特技号",
            InjectedEffectParameterRole.EffectValue => "特效值配置",
            InjectedEffectParameterRole.Range => "生效范围",
            InjectedEffectParameterRole.BooleanOption => "开关配置",
            InjectedEffectParameterRole.MessageText => "提示语文本",
            InjectedEffectParameterRole.UnknownCombined => "宝物-个人合并参数",
            _ => "未知参数"
        };
    }

    private static string NormalizeCommentLabel(string comment)
    {
        var label = comment.Trim();
        var slash = label.IndexOf(" / ", StringComparison.Ordinal);
        if (slash >= 0) label = label[(slash + 3)..].Trim();
        label = Regex.Replace(label, @"^【[^】]+】：?", string.Empty).Trim();
        label = Regex.Replace(label, @"（[^）]*默认[^）]*）", string.Empty).Trim();
        label = Regex.Replace(label, @"\([^)]*默认[^)]*\)", string.Empty).Trim();
        return label;
    }

    private static string ExtractParameterGroupName(string comment, string role)
    {
        var label = NormalizeCommentLabel(comment);
        foreach (var suffix in new[]
                 {
                     "个人特技号", "装备特技号", "个人号", "装备号", "宝物号", "特技号",
                     "特效号", "生效范围", "是否说话", "提示语", "宝物-个人"
                 })
        {
            if (label.EndsWith(suffix, StringComparison.Ordinal))
            {
                return label[..^suffix.Length].Trim(' ', '：', ':', '（', '(');
            }
        }

        return role switch
        {
            InjectedEffectParameterRole.Equipment => "装备/宝物",
            InjectedEffectParameterRole.Personal => "个人/特技",
            _ => string.Empty
        };
    }

    private static string BuildSegmentEditability(string role, int byteLength, int? value)
    {
        if (role == InjectedEffectParameterRole.MessageText) return "只建议预览后编辑";
        if (role == InjectedEffectParameterRole.UnknownCombined) return "需人工确认";
        if (byteLength == 1 && value > 0x7F) return "需要改写指令";
        return byteLength is 1 or 2 or 4 ? "可编辑" : "不建议编辑";
    }

    private static string BuildSafeRangeDescription(int byteLength, bool isImm8, int value)
    {
        if (isImm8 && value > 0x7F) return "当前是 6A imm8 高位值，需改写为 68 imm32 后再编辑";
        return byteLength switch
        {
            1 => "0x00-0x7F 为安全单字节编号",
            2 => "0x0000-0xFFFF",
            4 => "0x00000000-0xFFFFFFFF",
            _ => "按补丁文本说明人工确认"
        };
    }

    private static int SegmentImmediateValue(PatchSignatureSegment segment)
    {
        if (segment.Bytes.Length == 1) return segment.Bytes[0];
        if (segment.Bytes.Length == 2) return BitConverter.ToUInt16(segment.Bytes, 0);
        return unchecked((int)BitConverter.ToUInt32(segment.Bytes, 0));
    }

    private static IEnumerable<PushImmediate> ReadPreviousPushImmediates(PeImage pe, ExeSectionInfo section, int callOffset)
    {
        var sectionStart = checked((int)section.RawPointer);
        var scanStart = Math.Max(sectionStart, callOffset - InlineStubLookbackBytes);
        var pushes = new List<PushImmediate>();
        for (var offset = scanStart; offset < callOffset;)
        {
            var address = FileOffsetToVirtualAddress(pe, section, offset);
            var opcode = pe.Bytes[offset];
            if (opcode == 0x6A && offset + 1 < callOffset)
            {
                pushes.Add(new PushImmediate(offset, address, checked(address + 1), pe.Bytes[offset + 1], 1, IsImm8: true));
                offset += 2;
                continue;
            }

            if (opcode == 0x68 && offset + 4 < callOffset)
            {
                pushes.Add(new PushImmediate(offset, address, checked(address + 1), unchecked((int)BitConverter.ToUInt32(pe.Bytes, offset + 1)), 4, IsImm8: false));
                offset += 5;
                continue;
            }

            offset++;
        }

        return pushes.OrderByDescending(push => push.Offset).Take(4).OrderBy(push => push.Offset);
    }

    private static bool TryParseInlineStub(
        IReadOnlyList<PushImmediate> pushes,
        out int effectValueFlag,
        out int stackingFlag,
        out int equipmentId,
        out int personalId)
    {
        effectValueFlag = stackingFlag = equipmentId = personalId = 0;
        if (pushes.Count < 4) return false;

        var values = pushes.TakeLast(4).OrderBy(push => push.Offset).Select(push => push.RawValue).ToArray();
        effectValueFlag = values[0];
        stackingFlag = values[1];
        equipmentId = values[2];
        personalId = values[3];
        return effectValueFlag is 0 or 1 &&
               stackingFlag is 0 or 1 &&
               equipmentId is >= 0 and <= 0xFFFF &&
               personalId is >= 0 and <= 0xFFFF;
    }

    private static PeImage ReadPe(string path)
    {
        var bytes = File.ReadAllBytes(path);
        ushort ReadUInt16(int offset) => BitConverter.ToUInt16(bytes, offset);
        uint ReadUInt32(int offset) => BitConverter.ToUInt32(bytes, offset);

        var peOffset = checked((int)ReadUInt32(0x3C));
        if (peOffset < 0 || peOffset + 24 > bytes.Length) throw new InvalidOperationException("PE 头偏移越界。");
        if (ReadUInt32(peOffset) != 0x00004550) throw new InvalidOperationException("不是有效的 PE 文件。");

        var sectionCount = ReadUInt16(peOffset + 6);
        var optionalHeaderSize = ReadUInt16(peOffset + 20);
        var optionalHeaderStart = peOffset + 24;
        var magic = ReadUInt16(optionalHeaderStart);
        uint imageBase = magic switch
        {
            0x10B => ReadUInt32(optionalHeaderStart + 28),
            _ => throw new InvalidOperationException($"不支持的 PE Optional Header magic：0x{magic:X}。")
        };

        var sectionStart = optionalHeaderStart + optionalHeaderSize;
        var sections = new List<ExeSectionInfo>();
        for (var index = 0; index < sectionCount; index++)
        {
            var offset = sectionStart + index * 40;
            if (offset + 40 > bytes.Length) throw new InvalidOperationException("PE 节表被截断。");
            var name = Encoding.ASCII.GetString(bytes, offset, 8).TrimEnd('\0');
            var virtualSize = ReadUInt32(offset + 8);
            var virtualAddress = ReadUInt32(offset + 12);
            var rawSize = ReadUInt32(offset + 16);
            var rawPointer = ReadUInt32(offset + 20);
            var characteristics = ReadUInt32(offset + 36);
            sections.Add(new ExeSectionInfo
            {
                Name = name,
                VirtualAddress = virtualAddress,
                VirtualSize = virtualSize,
                RawPointer = rawPointer,
                RawSize = rawSize,
                Characteristics = characteristics,
                IsExecutable = (characteristics & 0x20000000) != 0
            });
        }

        return new PeImage(bytes, imageBase, sections);
    }

    private static bool TryVirtualAddressToFileOffset(PeImage pe, uint virtualAddress, out int offset)
    {
        offset = 0;
        if (virtualAddress < pe.ImageBase) return false;
        var rva = virtualAddress - pe.ImageBase;
        foreach (var section in pe.Sections)
        {
            var size = Math.Max(section.VirtualSize, section.RawSize);
            if (rva < section.VirtualAddress || rva >= section.VirtualAddress + size) continue;
            var raw = section.RawPointer + (rva - section.VirtualAddress);
            if (raw > int.MaxValue) return false;
            offset = (int)raw;
            return true;
        }

        return false;
    }

    private static bool TryReadVirtualBytes(PeImage pe, uint virtualAddress, int maximumLength, out byte[] bytes)
    {
        bytes = [];
        if (!TryVirtualAddressToFileOffset(pe, virtualAddress, out var offset)) return false;
        if (offset < 0 || offset >= pe.Bytes.Length) return false;
        var length = Math.Min(maximumLength, pe.Bytes.Length - offset);
        bytes = pe.Bytes.AsSpan(offset, length).ToArray();
        return bytes.Length > 0;
    }

    private static uint FileOffsetToVirtualAddress(PeImage pe, ExeSectionInfo section, int fileOffset)
        => checked(pe.ImageBase + section.VirtualAddress + (uint)(fileOffset - section.RawPointer));

    private static uint ResolveRelativeTarget(byte[] bytes, int fileOffset, uint instructionAddress)
    {
        var rel = BitConverter.ToInt32(bytes, fileOffset + 1);
        return unchecked((uint)(instructionAddress + 5 + rel));
    }

    private static bool IsVirtualAddressInExecutableSection(PeImage pe, uint virtualAddress)
    {
        if (virtualAddress < pe.ImageBase) return false;
        var rva = virtualAddress - pe.ImageBase;
        return pe.Sections.Any(section =>
            section.IsExecutable &&
            rva >= section.VirtualAddress &&
            rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.RawSize));
    }

    private static byte[] ParseHexBytes(string text)
    {
        var hex = HexStripRegex.Replace(text, string.Empty);
        if (hex.Length == 0) return [];
        if (hex.Length % 2 != 0) return [];

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return bytes;
    }

    private static string[] ReadAllLinesSmart(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            return new UTF8Encoding(false, true)
                .GetString(bytes)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936)
                .GetString(bytes)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
        }
    }

    private static bool IsAddressLine(string text)
    {
        if (text.Length < 2 || text[0] != '-') return false;
        var body = text[1..].Trim();
        return body.Length > 0 && body.All(Uri.IsHexDigit);
    }

    private static uint ParseAddress(string text)
        => uint.Parse(text.Trim().TrimStart('-').Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static int? ExtractEffectId(string text, string label)
    {
        var pattern = label + @"(?:特效|特技)?(?:号|號)?\s*(?:=|：|:)?\s*(?:0x)?([0-9A-Fa-f]{1,4})";
        var match = Regex.Match(text, pattern);
        return match.Success
            ? int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : null;
    }

    private static string BuildPatchDisplayName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name
            .Replace("【6.5】", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("【Star6.5】", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("【star6.5】", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("（注入版）", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(注入版)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string ClassifyPatchSignature(
        string path,
        string semanticText,
        IReadOnlyList<PatchSignatureSegment> segments,
        IReadOnlyCollection<uint> hookAddresses,
        IReadOnlyList<InjectedEffectParameterSlot> parameterSlots)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (ContainsAny(fileName, "信息传送") ||
            ContainsAny(semanticText, "信息传送", "兵种指定特殊", "整形变量4003"))
        {
            return InjectedEffectPatchCategory.FunctionExtensionPatch;
        }

        if (ContainsAny(fileName, "大杀四方", "护卫") ||
            hookAddresses.Count >= 3 ||
            segments.Count >= 10)
        {
            return InjectedEffectPatchCategory.ComplexMultiHookPatch;
        }

        if (ContainsAny(fileName, "策略保底", "限伤") ||
            parameterSlots.Count(slot => slot.Role is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment) >= 4)
        {
            return InjectedEffectPatchCategory.MultiCheckSpecialEffect;
        }

        if (hookAddresses.Count == 1 && parameterSlots.Any(slot => slot.Role == InjectedEffectParameterRole.Personal))
        {
            return InjectedEffectPatchCategory.SimpleFourModuleSpecialEffect;
        }

        return InjectedEffectPatchCategory.KnownPatchSignatureOnly;
    }

    private static string BuildSignatureStructureDiagnosis(PatchSignature signature)
        => signature.PatchCategory switch
        {
            InjectedEffectPatchCategory.SimpleFourModuleSpecialEffect => "本地文本显示为单入口特技补丁，通常可按“跳出点、判定桩、功能函数、参数位”阅读。",
            InjectedEffectPatchCategory.MultiCheckSpecialEffect => "本地文本显示为多判定特技补丁，同一补丁内可能有多组宝物号和特技号。",
            InjectedEffectPatchCategory.ComplexMultiHookPatch => "本地文本显示为复杂多入口补丁，包含多个 Hook 或 helper 函数，只建议先只读识别和人工复核。",
            InjectedEffectPatchCategory.FunctionExtensionPatch => "本地文本显示为功能扩展或引擎扩展，不按普通特技注入模板处理。",
            _ => "本地补丁签名已收录，但结构类型需要结合字节和注释人工确认。"
        };

    private static string BuildKnownPatchDiagnosis(PatchSignature signature, string confidence)
    {
        var prefix = confidence == "KnownPatchExact"
            ? "本地补丁签名完整命中。"
            : "命中了本地补丁的一部分，可能是补丁变体或已被二次改写。";
        return prefix + (signature.PatchCategory switch
        {
            InjectedEffectPatchCategory.MultiCheckSpecialEffect => "这是多判定特技补丁；可查看多组参数位，但不建议套用单判定四模块模板。",
            InjectedEffectPatchCategory.ComplexMultiHookPatch => "这是复杂多入口补丁；已识别参数位时也应先人工复核，不建议一键重注入。",
            InjectedEffectPatchCategory.FunctionExtensionPatch => "这是功能扩展补丁；不是特技判定模板，仅作为已收录补丁识别。",
            InjectedEffectPatchCategory.SimpleFourModuleSpecialEffect => "可以确认这是已收录的单入口特技注入补丁。",
            _ => "可以确认这是已收录的注入补丁。"
        });
    }

    private static void ApplyCategoryDefaults(InjectedEffectCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.PatchCategory))
        {
            candidate.PatchCategory = candidate.PatternKind switch
            {
                InjectedEffectPatternKind.InlineCoreStub => InjectedEffectPatchCategory.InlineCoreStub,
                InjectedEffectPatternKind.FourModuleDamageModifier => InjectedEffectPatchCategory.SimpleFourModuleSpecialEffect,
                InjectedEffectPatternKind.FourModuleLikeCandidate => InjectedEffectPatchCategory.UnknownCandidate,
                _ => InjectedEffectPatchCategory.KnownPatchSignatureOnly
            };
        }

        if (candidate.PatchCategory == InjectedEffectPatchCategory.MultiCheckSpecialEffect)
        {
            candidate.Risk = "multi-check-readonly-review";
        }
        else if (candidate.PatchCategory == InjectedEffectPatchCategory.ComplexMultiHookPatch)
        {
            candidate.Risk = "complex-multihook-readonly-review";
        }
        else if (candidate.PatchCategory == InjectedEffectPatchCategory.FunctionExtensionPatch)
        {
            candidate.Risk = "function-extension-not-special-template";
        }

        if (string.IsNullOrWhiteSpace(candidate.StructureDiagnosis))
        {
            candidate.StructureDiagnosis = candidate.UserReadableDiagnosis;
        }
    }

    private static string BuildCodeCaveSummary(IReadOnlyList<PatchSignatureSegment> segments)
    {
        var cave = segments.Where(segment => segment.Bytes.Length >= 16).Skip(1).FirstOrDefault()
                   ?? segments.Where(segment => segment.Bytes.Length >= 16).FirstOrDefault();
        return cave == null ? string.Empty : $"{FormatVa(cave.Address)}+{cave.Bytes.Length}";
    }

    private static bool IsDamageSemantic(string text)
        => ContainsAny(text, "增伤", "增加伤害", "减伤", "减少伤害", "限伤", "限制伤害", "保底", "伤害不小于", "伤害固定");

    private static bool IsPersonalComment(string text)
        => ContainsAny(text, "个人", "特技号", "特效号") && !IsEquipmentComment(text);

    private static bool IsEquipmentComment(string text)
        => ContainsAny(text, "装备", "宝物", "寶物");

    private static bool ContainsAny(string text, params string[] needles)
        => needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<KnownEffectLabel> KnownEffectLabels()
        =>
        [
            new("回MP攻击", 0xAD, 0x00, 0x00418335),
            new("策略偷钱", 0xAA, 0x00, 0x004259B4),
            new("策略冲锋", 0xB0, 0x00, 0x0043C2B0),
            new("聚势伐谋", 0xB1, 0x00, 0x0043C2B5),
            new("殭屍大法", 0xB2, 0x00, 0x00406006),
            new("多多益善", 0xB3, 0x00, 0x0043F936),
            new("敌潮逆噬", 0xB4, 0x00, 0x0043CB4C),
            new("盛气凌人", 0xBF, 0x00, 0x00472D92),
            new("噬心毒咒", 0xC0, 0x00, 0x004259AF),
            new("大杀四方", 0xC1, 0x00, 0x004037FF),
            new("护卫", 0xD1, 0x00, 0x004105C8),
            new("强化攻击穿透", 0xDD, 0x00, 0x00407831),
            new("无视策略减伤", 0xEF, 0x00, 0x0043C242),
            new("策略保底/限伤", 0xFF, 0x00, 0x0043C2D5)
        ];

    private static int CompareCandidates(InjectedEffectCandidate left, InjectedEffectCandidate right)
    {
        var confidence = ConfidenceRank(left.Confidence).CompareTo(ConfidenceRank(right.Confidence));
        if (confidence != 0) return confidence;

        var pattern = PatternRank(left.PatternKind).CompareTo(PatternRank(right.PatternKind));
        return pattern != 0 ? pattern : left.Address.CompareTo(right.Address);
    }

    private static int ConfidenceRank(string confidence)
        => confidence switch
        {
            "KnownPatchExact" => 0,
            "KnownPatchVariant" => 1,
            "KnownEffectIdInlineStub" => 2,
            "InlineStubDetected" => 3,
            "FourModuleLikeCandidate" => 4,
            _ => 5
        };

    private static int PatternRank(string pattern)
        => pattern switch
        {
            InjectedEffectPatternKind.FourModuleDamageModifier => 0,
            InjectedEffectPatternKind.KnownPatch => 1,
            InjectedEffectPatternKind.InlineCoreStub => 2,
            InjectedEffectPatternKind.FourModuleLikeCandidate => 3,
            _ => 4
        };

    private static bool TryParseUInt(string? text, out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[2..];
        return uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatVa(uint value) => $"0x{value:X8}";

    private static string FormatEffectId(int? value)
        => value.HasValue ? $"0x{value.Value:X2} / {value.Value}" : string.Empty;

    private static string FormatParameterRole(string role)
        => role switch
        {
            InjectedEffectParameterRole.Equipment => "装备/宝物编号",
            InjectedEffectParameterRole.Personal => "个人/特技编号",
            InjectedEffectParameterRole.EffectValue => "特效值配置",
            InjectedEffectParameterRole.Range => "范围配置",
            InjectedEffectParameterRole.BooleanOption => "开关配置",
            InjectedEffectParameterRole.MessageText => "提示语文本",
            InjectedEffectParameterRole.UnknownCombined => "合并参数",
            _ => "未知参数"
        };

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string AppendEvidence(string evidence, string item)
        => string.IsNullOrWhiteSpace(evidence) ? item : evidence + "; " + item;

    private static string JccName(byte opcode)
        => opcode switch
        {
            0x74 or 0x84 => "je/jz",
            0x75 or 0x85 => "jne/jnz",
            0x72 or 0x82 => "jb/jc",
            0x73 or 0x83 => "jae/jnc",
            0x76 or 0x86 => "jbe",
            0x77 or 0x87 => "ja",
            _ => "jcc"
        };

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record PeImage(byte[] Bytes, uint ImageBase, List<ExeSectionInfo> Sections);

    private sealed record PatchSignature
    {
        public string Name { get; init; } = string.Empty;
        public string SourcePath { get; init; } = string.Empty;
        public string SemanticText { get; init; } = string.Empty;
        public List<PatchSignatureSegment> Segments { get; init; } = [];
        public List<uint> HookAddresses { get; init; } = [];
        public int? PersonalEffectId { get; init; }
        public int? EquipmentEffectId { get; init; }
        public List<InjectedEffectParameterSlot> ParameterSlots { get; init; } = [];
        public string PatchCategory { get; init; } = string.Empty;
    }

    private sealed record PatchSignatureSegment(uint Address, byte[] Bytes, string Comment);
    private sealed record PatchSignatureManifest(string SignatureId, IReadOnlyList<string> Anchors);
    private sealed record MaskedBytePattern(byte[] Bytes, bool[] Mask)
    {
        public int FixedByteCount => Mask.Count(item => item);
    }
    private sealed record KnownEffectLabel(string Name, int? PersonalEffectId, int? EquipmentEffectId, uint? HookAddress);

    private sealed record WrapperCoreCallMatch(
        X86InstructionInfo Caller,
        X86InstructionInfo CoreCall,
        uint WrapperEntry,
        uint ConsumerFunctionAddress,
        IReadOnlyList<PushImmediate> Pushes,
        int EffectValueFlag,
        int StackingFlag,
        int EquipmentId,
        int PersonalId);

    private sealed record PushImmediate(
        int Offset,
        uint InstructionAddress,
        uint? OperandAddress,
        int RawValue,
        int OperandSize,
        bool IsImm8,
        string SourceKind = X86ArgumentSourceKind.Immediate,
        bool IsDirectlyPatchable = true,
        IReadOnlyList<string>? DefinitionChain = null,
        int? OperandOffset = null);

    private sealed record RelativeInstruction(uint Address, uint Target, string Kind, int Size)
    {
        public uint EndAddress => checked(Address + (uint)Size);
    }

    private sealed record BodyAnalysis(
        uint BaseAddress,
        uint? GuardStartAddress,
        uint? FeatureStartAddress,
        uint? ReturnAddress,
        int? EquipmentEffectId,
        int? PersonalEffectId,
        uint? EquipmentIdPatchAddress,
        uint? PersonalIdPatchAddress,
        PushImmediate? EquipmentPush,
        PushImmediate? PersonalPush,
        RelativeInstruction? GuardCall,
        RelativeInstruction? ConditionalBranch,
        RelativeInstruction? ReturnJump,
        IReadOnlyList<BodyCheckGroup> CheckGroups,
        bool HasCoreCall,
        bool HasTestOrCompare,
        bool IsFourModuleLike);

    private sealed record BodyCheckGroup(
        IReadOnlyList<PushImmediate> GuardPushes,
        RelativeInstruction GuardCall,
        RelativeInstruction? ConditionalBranch,
        RelativeInstruction? ReturnJump,
        uint? ReturnAddress);
}
