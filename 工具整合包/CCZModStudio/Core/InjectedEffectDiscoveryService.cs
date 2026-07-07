using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed partial class InjectedEffectDiscoveryService
{
    private const uint CoreEffectEngineAddress = 0x004101D9;
    private const int InlineStubLookbackBytes = 48;
    private const int BodyScanBytes = 256;
    private static readonly Regex HexStripRegex = new("[^0-9A-Fa-f]", RegexOptions.Compiled);

    public InjectedEffectDiscoveryReport Discover(CczProject project, string targetFileName = "Ekd5.exe")
    {
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

        var pe = ReadPe(targetPath);
        var profile = new EnginePatchProfileService().Build(project);
        var report = new InjectedEffectDiscoveryReport
        {
            TargetFilePath = targetPath,
            TargetFileName = targetFile,
            ExeSha256 = ComputeSha256(pe.Bytes),
            ExeSize = pe.Bytes.LongLength,
            ImageBase = pe.ImageBase,
            EngineVersionHint = profile.EngineVersion,
            IsKnownEngine = profile.IsKnown
        };
        report.Warnings.AddRange(profile.Warnings);

        var patchSignatures = LoadPatchSignatures(project.WorkspaceRoot).ToList();
        AddKnownPatchMatches(report, pe, patchSignatures);
        AddInlineStubCandidates(report, pe);
        AddHookCandidates(report, pe, profile);
        AddFourModuleHookStructures(report, pe);
        MergeKnownLabels(report);
        AttachDefaultModules(report);

        report.Candidates.Sort(CompareCandidates);
        report.HookCandidates.Sort((left, right) => left.Address.CompareTo(right.Address));
        report.Summary = $"识别完成：candidates={report.Candidates.Count}, hooks={report.HookCandidates.Count}, knownPatchSignatures={patchSignatures.Count}, warnings={report.Warnings.Count}.";
        return report;
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
            foreach (var segment in signature.Segments)
            {
                if (segment.Bytes.Length == 0) continue;
                if (!TryVirtualAddressToFileOffset(pe, segment.Address, out var offset)) continue;
                if (offset < 0 || offset + segment.Bytes.Length > pe.Bytes.Length) continue;
                if (pe.Bytes.AsSpan(offset, segment.Bytes.Length).SequenceEqual(segment.Bytes))
                {
                    matchedSegments++;
                }
            }

            if (matchedSegments == 0) continue;

            var firstAddress = signature.Segments.FirstOrDefault()?.Address ?? 0;
            var confidence = matchedSegments == signature.Segments.Count ? "KnownPatchExact" : "KnownPatchVariant";
            var candidate = BuildKnownPatchCandidate(signature, firstAddress, confidence, matchedSegments);
            report.Candidates.Add(candidate);
        }
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
                    Name = parsed ? TryKnownEffectName(personalId, equipmentId) ?? "未知特技桩" : "仅识别到特技核心调用",
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
            SourceComment = sourceComment,
            Editability = IsUnsafeImm8(push) ? "需要改写指令" : "可编辑",
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

    private static BodyAnalysis AnalyzeCodeBody(byte[] bytes, uint baseAddress)
    {
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

    private static string? TryKnownEffectName(int personalId, int equipmentId)
        => KnownEffectLabels()
            .FirstOrDefault(item =>
                (item.PersonalEffectId.HasValue && item.PersonalEffectId.Value == personalId) ||
                (item.EquipmentEffectId.HasValue && item.EquipmentEffectId.Value == equipmentId))
            ?.Name;

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
    private sealed record KnownEffectLabel(string Name, int? PersonalEffectId, int? EquipmentEffectId, uint? HookAddress);

    private sealed record PushImmediate(
        int Offset,
        uint InstructionAddress,
        uint OperandAddress,
        int RawValue,
        int OperandSize,
        bool IsImm8);

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
