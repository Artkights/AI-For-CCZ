using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class HookExecutionContractService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<HookExecutionContract>>>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> CacheAccessOrder = new(StringComparer.OrdinalIgnoreCase);
    private static long _cacheAccessSequence;

    public IReadOnlyList<HookExecutionContract> BuildContracts(CczProject project)
        => BuildContractsAsync(project).GetAwaiter().GetResult();

    public async Task<IReadOnlyList<HookExecutionContract>> BuildContractsAsync(CczProject project, CancellationToken cancellationToken = default)
    {
        var executable = ExecutableAnalysisSnapshotCache.Shared.GetBase(project);
        var audit = new ExecutableProfileAuditService().AuditSnapshot(executable);
        var evidenceRoots = new[]
        {
            Path.Combine(project.GameRoot, "CCZModStudio_Notes", "EffectContractEvidenceV2"),
            Path.Combine(project.GameRoot, "CCZModStudio_Notes", "EffectContractEvidenceV3")
        };
        var evidenceFingerprint = evidenceRoots.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            .Select(path => new FileInfo(path)).Where(info => info.Exists)
            .Select(info => info.LastWriteTimeUtc.Ticks ^ info.Length).DefaultIfEmpty().Max();
        var key = string.Join("|", executable.Fingerprint.Path, audit.ProfileId, audit.NormalizedProfileIdentity,
            executable.Sha256, evidenceFingerprint, "contract-v3");
        var candidate = new Lazy<Task<IReadOnlyList<HookExecutionContract>>>(
            () => Task.Run(() => BuildContractsCore(project, executable, audit), CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = Cache.GetOrAdd(key, candidate);
        CacheAccessOrder[key] = Interlocked.Increment(ref _cacheAccessSequence);
        var miss = ReferenceEquals(lazy, candidate);
        try
        {
            var value = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            PerformanceMetrics.Increment(miss ? "HookExecutionContract.CacheMisses" : "HookExecutionContract.CacheHits");
            Trim(executable.Fingerprint.Path);
            return value;
        }
        catch
        {
            if (lazy.IsValueCreated && lazy.Value.IsFaulted)
            {
                Cache.TryRemove(new KeyValuePair<string, Lazy<Task<IReadOnlyList<HookExecutionContract>>>>(key, lazy));
                CacheAccessOrder.TryRemove(key, out _);
            }
            throw;
        }
    }

    private static void Trim(string executablePath)
    {
        var prefix = executablePath + "|";
        var expired = CacheAccessOrder
            .Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Value)
            .Skip(2)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in expired)
        {
            Cache.TryRemove(key, out _);
            CacheAccessOrder.TryRemove(key, out _);
        }
    }

    private static IReadOnlyList<HookExecutionContract> BuildContractsCore(
        CczProject project,
        ExecutableAnalysisSnapshot executable,
        ExecutableProfileAuditResult audit)
    {
        var profile = new EnginePatchProfileService().Build(project);
        if (profile.EngineVersion != "6.5") return [];

        var contracts = new List<HookExecutionContract>
        {
            BuildStrategyDamageContract(executable, profile, audit),
            BuildPhysicalRecoveryContract(executable, profile, audit)
        };
        for (var index = 0; index < contracts.Count; index++)
        {
            var contract = contracts[index];
            // 契约身份只由静态 ABI、Hook 字节和槽位定义决定。必须先计算稳定 hash，
            // 再读取同一 hash 的可信动态证据，否则新格式证据永远无法匹配默认空 hash。
            contract.ContractHash = ComputeHash(contract);
            ApplyStoredEvidence(project, contract);
            contracts[index] = TryRestoreInstalledContract(project, contract) ?? contract;
        }
        return contracts;
    }

    public HookExecutionContract Read(CczProject project, string contractId)
        => BuildContracts(project).FirstOrDefault(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException($"没有找到执行契约“{contractId}”。");

    public EffectContractProbePlan CreateProbePlan(CczProject project, string contractId)
    {
        var contract = Read(project, contractId);
        return new EffectContractProbePlan
        {
            PlanId = $"probe-{contract.ContractId}-{contract.ExeSha256[..Math.Min(8, contract.ExeSha256.Length)]}",
            ContractId = contract.ContractId,
            ExeSha256 = contract.ExeSha256,
            BreakpointAddresses = contract.ContractFamilyId == "strategy-damage-formula"
                ? [0x0043C2A9, 0x0043C2B0, 0x0043C2B5, 0x0043C2C9]
                : [0x00418330, 0x00418335, 0x00405AD5, 0x0043F70C],
            RequiredCapturesZh =
            [
                "断点命中次数和触发战斗阶段",
                "EAX、ECX、EDX、ESP、EBP、ESI、EDI 与 EFLAGS",
                "调用栈和 Hook 前后至少 64 字节反汇编",
                "所有候选上下文槽位的地址、修改前值和修改后值",
                "当前攻击者、受击者、显示伤害以及实际生命/策略值变化"
            ],
            ScenariosZh = contract.DynamicValidationPlanZh.ToList(),
            Batches = BuildProbeBatches(contract),
            SummaryZh = "该计划只采集证据，不写入 EXE。证据必须来自相同 SHA，且至少覆盖普通命中和边界场景。"
        };
    }

    public EffectContractEvidenceImportResult ImportEvidence(CczProject project, EffectContractEvidence evidence)
    {
        var result = new EffectContractEvidenceImportResult();
        HookExecutionContract contract;
        try { contract = Read(project, evidence.ContractId); }
        catch (Exception ex) { result.WarningsZh.Add(ex.Message); result.SummaryZh = "执行契约证据导入失败。"; return result; }
        result.Contract = contract;
        if (!evidence.ExeSha256.Equals(contract.ExeSha256, StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("证据 EXE 摘要与当前执行契约不一致，不能跨版本提升写入能力。");
        if (evidence.HookHitCount < 2) result.WarningsZh.Add("至少需要两次可复查的 Hook 动态命中。");
        if (evidence.CompletedScenariosZh.Count < 2) result.WarningsZh.Add("至少需要普通场景和一个边界场景。");
        if (evidence.SourceTool.IndexOf("GameDebug", StringComparison.OrdinalIgnoreCase) < 0 &&
            evidence.SourceTool.IndexOf("x32dbg", StringComparison.OrdinalIgnoreCase) < 0)
            result.WarningsZh.Add("证据来源必须标明 GameDebug MCP 或 x32dbg。");
        foreach (var slot in contract.Slots.Where(item => item.IsStaticallyResolved))
        {
            if (!evidence.ObservedSlots.ContainsKey(slot.SlotId)) result.WarningsZh.Add($"缺少槽位“{slot.DisplayNameZh}”的动态观测。");
            if (slot.Access == ContextSlotAccess.ReadWrite &&
                (!evidence.ObservedSlotMinimums.ContainsKey(slot.SlotId) || !evidence.ObservedSlotMaximums.ContainsKey(slot.SlotId)))
                result.WarningsZh.Add($"缺少可写槽位“{slot.DisplayNameZh}”的验证上下限。");
        }
        if (evidence.CallStackZh.Count == 0) result.WarningsZh.Add("缺少可复查调用栈。");
        if (contract.Slots.Count(item => item.IsStaticallyResolved) == 0)
            result.WarningsZh.Add("该执行契约尚未定义任何可验证上下文槽位，必须先完成静态定位，不能直接提升。");
        result.WarningsZh.Add("旧结构化证据只作为诊断记录保存，不能提升执行契约；请使用 GameDebug 签发的原始证据包。");

        evidence.EvidenceId = string.IsNullOrWhiteSpace(evidence.EvidenceId)
            ? $"{contract.ContractId}-{DateTime.Now:yyyyMMddHHmmssfff}"
            : Path.GetFileNameWithoutExtension(evidence.EvidenceId);
        var root = EvidenceRoot(project, contract.ExeSha256, contract.ContractId);
        Directory.CreateDirectory(root);
        result.SavedPath = Path.Combine(root, evidence.EvidenceId + ".json");
        File.WriteAllText(result.SavedPath, JsonSerializer.Serialize(evidence, JsonOptions), Encoding.UTF8);
        result.Accepted = true;
        result.ContractPromoted = false;
        result.SummaryZh = "旧结构化证据已保存为诊断材料，但不会提升写入能力。";
        return result;
    }

    public EffectEvidenceBundleImportResult ImportEvidenceBundle(CczProject project, EffectEvidenceBundleV1 bundle)
    {
        HookExecutionContract contract;
        try { contract = Read(project, bundle.ContractId); }
        catch (Exception ex) { return new EffectEvidenceBundleImportResult { WarningsZh = [ex.Message], SummaryZh = "证据包导入失败。" }; }
        var warnings = new List<string>
        {
            "EffectEvidenceBundleV1 只保留为诊断材料；契约 v2 不接受 v1 证据提升。请使用 import_effect_evidence_bundle_v2。"
        };
        if (!bundle.ContractHash.Equals(contract.ContractHash, StringComparison.OrdinalIgnoreCase))
            warnings.Add("证据包绑定的执行契约摘要已变化，不能提升当前契约。");
        var requiredSlots = contract.Slots.Where(item => item.IsStaticallyResolved).Select(item => item.SlotId).ToList();
        var observed = bundle.DerivedObservations.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredScenarios = RequiredScenarioIds(contract);
        foreach (var scenario in requiredScenarios.Where(id => !bundle.CompletedScenarioIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
            warnings.Add("缺少动态场景：" + scenario + "。");
        foreach (var slot in requiredSlots.Where(id => !observed.Contains(id)))
            warnings.Add("证据包无法从原始采集中复算槽位：" + slot + "。");
        foreach (var slot in contract.Slots.Where(item => item.IsStaticallyResolved && item.Access == ContextSlotAccess.ReadWrite))
        {
            var boundary = bundle.DerivedObservations.Where(item => item.Key.Equals(slot.SlotId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!boundary.Any(item => item.VerifiedMinimum.HasValue) || !boundary.Any(item => item.VerifiedMaximum.HasValue))
                warnings.Add($"可写槽位“{slot.DisplayNameZh}”只有样本观测范围，尚未从边界场景证明合法上下限。");
        }
        return new EffectEvidenceBundleImportResult
        {
            Accepted = false,
            ContractPromoted = false,
            WarningsZh = warnings,
            SummaryZh = "旧 v1 证据未提升契约；原文件不会被删除。"
        };
    }

    public EffectEvidenceBundleImportResult ImportEvidenceBundleV2(CczProject project, EffectEvidenceBundleV2 bundle)
    {
        HookExecutionContract contract;
        try { contract = Read(project, bundle.ContractId); }
        catch (Exception ex) { return new EffectEvidenceBundleImportResult { WarningsZh = [ex.Message], SummaryZh = "v2 证据包导入失败。" }; }
        var warnings = new List<string>();
        if (contract.ContractVersion != 2 || bundle.ContractVersion != 2) warnings.Add("执行契约或证据包不是 v2。");
        if (!bundle.ContractHash.Equals(contract.ContractHash, StringComparison.OrdinalIgnoreCase)) warnings.Add("证据包契约摘要与当前 v2 契约不一致。");
        if (!bundle.ContractCodeIdentityHash.Equals(contract.ContractCodeIdentityHash, StringComparison.OrdinalIgnoreCase)) warnings.Add("证据包代码身份与当前 Hook/函数代码身份不一致。");
        if (!bundle.ProfileId.Equals(contract.ProfileId, StringComparison.Ordinal) ||
            !bundle.NormalizedProfileIdentity.Equals(contract.NormalizedProfileIdentity, StringComparison.OrdinalIgnoreCase))
            warnings.Add("证据包规范档案身份与当前契约不一致。");
        var requiredSlots = contract.Slots.Where(item => item.IsStaticallyResolved).Select(item => item.SlotId).ToList();
        var observed = bundle.DerivedObservations.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var scenario in RequiredScenarioIds(contract).Where(id => !bundle.CompletedScenarioIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
            warnings.Add("缺少动态场景：" + scenario + "。");
        foreach (var slot in requiredSlots.Where(id => !observed.Contains(id))) warnings.Add("v2 证据无法从原始采集中复算槽位：" + slot + "。");
        foreach (var slot in contract.Slots.Where(item => item.IsStaticallyResolved && item.Access == ContextSlotAccess.ReadWrite))
        {
            var boundary = bundle.DerivedObservations.Where(item => item.Key.Equals(slot.SlotId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!boundary.Any(item => item.VerifiedMinimum.HasValue) || !boundary.Any(item => item.VerifiedMaximum.HasValue))
                warnings.Add($"可写槽位“{slot.DisplayNameZh}”尚未从边界场景证明合法上下限。");
        }
        if (warnings.Count > 0) return new EffectEvidenceBundleImportResult { WarningsZh = warnings, SummaryZh = "v2 证据包身份、场景、槽位或边界不完整，未提升契约。" };
        var result = EffectEvidenceBundleService.VerifyAndStoreV2(project, bundle);
        result.ContractPromoted = result.Accepted;
        if (result.Accepted) result.SummaryZh = "可信 v2 证据已导入；当前代码身份的行为逻辑契约可以重新计算能力。";
        return result;
    }

    public EffectEvidenceBundleImportResult ImportEvidenceBundleV3(CczProject project, EffectEvidenceBundleV3 bundle)
    {
        HookExecutionContract contract;
        try { contract = Read(project, bundle.ContractId); }
        catch (Exception ex) { return new EffectEvidenceBundleImportResult { WarningsZh = [ex.Message], SummaryZh = "V3 证据包导入失败。" }; }
        var warnings = new List<string>();
        if (contract.ContractVersion != 2 || bundle.ContractVersion != 2)
            warnings.Add("执行契约或证据包不是契约 v2/V3 协议。");
        if (!bundle.ContractHash.Equals(contract.ContractHash, StringComparison.OrdinalIgnoreCase))
            warnings.Add("V3 证据包契约摘要与当前契约不一致。");
        if (!bundle.ContractCodeIdentityHash.Equals(contract.ContractCodeIdentityHash, StringComparison.OrdinalIgnoreCase))
            warnings.Add("V3 证据包代码身份与当前 Hook/函数代码身份不一致。");
        if (!bundle.NormalizedProfileIdentity.Equals(contract.NormalizedProfileIdentity, StringComparison.OrdinalIgnoreCase))
            warnings.Add("V3 证据包规范化档案身份与当前契约不一致。");
        if (!bundle.ProbeRestored) warnings.Add("V3 证据没有通过探针恢复验证。");
        if (warnings.Count > 0)
            return new EffectEvidenceBundleImportResult { WarningsZh = warnings, SummaryZh = "V3 证据身份或恢复状态不完整，未提升契约。" };
        var result = EffectEvidenceBundleService.VerifyAndStoreV3(project, bundle);
        result.ContractPromoted = result.Accepted;
        if (result.Accepted)
        {
            result.SummaryZh = "可信 V3 证据已导入；契约缓存已失效，重新读取权限矩阵即可获得新权限。";
        }
        return result;
    }

    private static HookExecutionContract BuildStrategyDamageContract(ExecutableAnalysisSnapshot executable, EnginePatchProfile profile, ExecutableProfileAuditResult audit)
    {
        var bytes = EffectPatchByteService.ReadVirtualBytes(executable, 0x0043C2B0, 25);
        var padding = EffectPatchByteService.ParseHex(bytes).All(value => value is 0x90 or 0xCC);
        var contract = new HookExecutionContract
        {
            ContractId = "strategy-damage-formula-v2",
            ContractFamilyId = "strategy-damage-formula",
            ContractVersion = 2,
            DisplayNameZh = "策略伤害调整执行契约",
            ExeSha256 = profile.ExeSha256,
            ProfileId = audit.ProfileId,
            NormalizedProfileIdentity = audit.NormalizedProfileIdentity,
            ContractCodeIdentityHash = ComputeCodeIdentity(executable,
                (0x0043C280, 0x90), (0x004101D9, 0x180), (0x0042518F, 0x40)),
            HookAddress = 0x0043C2B0,
            ExpectedOldBytesHex = bytes,
            NormalizedLocatorSignature = "call ?; mov [ebp-04],eax; {25-byte padding}; push [ebp+14]; push [ebp-04]; push [ebp+08]; push [ebp-10]",
            TriggerPhaseZh = "策略伤害公式形成后、后续策略结算前",
            CallingConventionZh = "当前函数 EBP 栈帧；注入体必须保持 ESP、非易失寄存器和标志位",
            OriginalOperationOrderZh = "保留原策略伤害计算和后续调用顺序；共享调度器只占用连续填充区",
            ClampOrderZh = "最低伤害和后续公式顺序尚需动态确认",
            UnitPointerSlotId = "strategy-effect-subject",
            TargetPointerSlotId = "strategy-target-context",
            ConflictGroup = "strategy-damage-formula",
            AllowedActions = [SemanticEffectAction.AddDamageFixed, SemanticEffectAction.SubtractDamageFixed, SemanticEffectAction.AddDamagePercent, SemanticEffectAction.SubtractDamagePercent],
            AllowedCallSymbols = ["core_effect_engine"],
            PreservedRegisters = ["ebx", "esi", "edi", "ebp", "esp"],
            ExpectedStackDelta = 0,
            PreserveFlags = true,
            VerificationStatus = padding ? HookContractVerificationStatus.StaticCandidate : HookContractVerificationStatus.BytesChanged,
            VerificationStatusZh = padding ? "静态候选，等待动态命中" : "当前字节已变化",
            AllowSemanticPreview = false,
            MissingEvidenceZh =
            [
                "连续填充区是否在所有策略伤害路径中自然执行",
                "[ebp-10] 判定对象与 [ebp-04] 当前伤害的动态对应关系",
                "最低伤害、暴击、减伤和写回顺序"
            ],
            DynamicValidationPlanZh =
            [
                "普通策略命中时确认 0043C2B0/0043C2B5 是否自然命中并记录 [ebp-10]/[ebp-04]。",
                "分别验证增伤、减伤、暴击和最低伤害边界，确认修改 [ebp-04] 会进入最终结算。"
            ]
        };
        var subject = ContextSourceExpressionFactory.DecodeMemory([0x8B, 0x4D, 0xF0], 0x0043C2B0,
            "TacticalUnit*", ContextRelationshipStatus.EffectSubjectCandidate);
        var damage = ContextSourceExpressionFactory.DecodeMemory([0x01, 0x45, 0xFC], 0x0043C2B5,
            "StrategyDamageValue", ContextRelationshipStatus.Unknown);
        var context = ContextSourceExpressionFactory.DecodeMemory([0xFF, 0x75, 0x08], 0x0043C2C9,
            "StrategyContextCandidate", ContextRelationshipStatus.Unknown);
        contract.Slots.AddRange([
            Slot("strategy-effect-subject", "特效判定对象", subject, ContextSlotAccess.Read, [.. contract.AllowedActions], padding, "Iced 将 8B 4D F0 的 disp8 F0 有符号解码为 -16；本地策略冲锋和聚势伐谋样本均在该位置装入 ECX。"),
            Slot("strategy-current-damage", "当前策略伤害", damage, ContextSlotAccess.ReadWrite, [.. contract.AllowedActions], padding, "Iced 将 01 45 FC 的 disp8 FC 有符号解码为 -4；两个样本在此累加伤害。"),
            Slot("strategy-target-context", "策略上下文候选", context, ContextSlotAccess.Read, [], padding, "填充区后的原生调用把 [ebp+08] 作为参数传递；静态阶段不强行命名为目标单位指针。")
        ]);
        contract.PointerInference = new ContextPointerInference
        {
            Register = "ecx",
            Candidates =
            [
                new ContextPointerCandidate
                {
                    Source = subject,
                    UnderlyingType = "TacticalUnit*",
                    RelationshipSemantic = ContextRelationshipStatus.EffectSubjectCandidate,
                    Confidence = "High",
                    EvidenceZh = ["策略冲锋与聚势伐谋均从同一 [ebp-0x10] 槽装入 ECX；攻击者/受击者角色仍需场景证据。"]
                }
            ],
            IsUnique = true,
            CanUseForWrite = false,
            BlockerCodes = ["RELATIONSHIP_SEMANTIC_UNVERIFIED"],
            ReasonsZh = ["底层 TacticalUnit* 来源已恢复，但尚未把它提升为攻击者或受击者角色。"]
        };
        return contract;
    }

    private static HookExecutionContract BuildPhysicalRecoveryContract(ExecutableAnalysisSnapshot executable, EnginePatchProfile profile, ExecutableProfileAuditResult audit)
    {
        var hookAddress = EngineRuntimeSemanticRegistry.PhysicalRecoveryHookAddress;
        var bytes = EffectPatchByteService.ReadVirtualBytes(executable, hookAddress, 5);
        var raw = EffectPatchByteService.ParseHex(bytes);
        var existingTarget = raw.Length == 5 && raw[0] == 0xE9
            ? unchecked((uint)(hookAddress + 5 + BitConverter.ToInt32(raw, 1)))
            : 0;
        var isExpectedJump = existingTarget == EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyAddress;
        var contract = new HookExecutionContract
        {
            ContractId = "physical-after-damage-recovery-v2",
            ContractFamilyId = "physical-after-damage",
            ContractVersion = 3,
            DisplayNameZh = "物理伤害后恢复执行契约",
            ExeSha256 = profile.ExeSha256,
            ProfileId = audit.ProfileId,
            NormalizedProfileIdentity = audit.NormalizedProfileIdentity,
            ContractCodeIdentityHash = ComputeCodeIdentity(executable,
                (0x0041832D, 0x28),
                (EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyAddress, 0xAB),
                (0x004101D9, 0x180),
                (EngineRuntimeSemanticRegistry.BattlefieldIdToTacticalUnitAddress, 0x20),
                (EngineRuntimeSemanticRegistry.TacticalUnitToRuntimeCharacterAddress, 0x20),
                (0x0040728F, 0x60)),
            HookAddress = hookAddress,
            ExpectedOldBytesHex = bytes,
            NormalizedLocatorSignature = "00418335:E9 rel32 -> 004528FC; preserve legacy body and both original returns",
            TriggerPhaseZh = "物理伤害应用后",
            CallingConventionZh = "复用父函数 EBP 栈帧；新调度器必须保持 ESP、通用寄存器和 EFLAGS",
            OriginalOperationOrderZh = "新调度成员完成后尾跳 004528FC；不得复制、覆盖或重编译遗留代码体",
            ClampOrderZh = "当前 MP 加固定值后按人物运行时记录 +1F WORD 动态封顶",
            UnitPointerSlotId = "physical-effect-subject",
            TargetPointerSlotId = "physical-attacker-unit",
            ConflictGroup = "physical-after-damage",
            ContinuationPolicy = HookContinuationPolicies.ChainExistingJumpTarget,
            ContinuationAddress = EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyAddress,
            AllowedActions = [SemanticEffectAction.RestoreMpFixed],
            AllowedCallSymbols = ["core_effect_engine"],
            PreservedRegisters = ["ebx", "esi", "edi", "ebp", "esp"],
            ExpectedStackDelta = 0,
            PreserveFlags = true,
            VerificationStatus = isExpectedJump ? HookContractVerificationStatus.StaticCandidate : HookContractVerificationStatus.BytesChanged,
            VerificationStatusZh = isExpectedJump ? "遗留跳转和代码身份已静态锁定，等待沙箱动态验证" : "00418335 未指向登记的 004528FC 遗留链",
            AllowSemanticPreview = false,
            MissingEvidenceZh = ["当前攻击者、特效判定对象和 MP 写入对象的动态同一性", "普通/暴击/反击/连击触发次数", "MP=0、最大值边界和负向场景"],
            DynamicValidationPlanZh =
            [
                "在普通、暴击、反击和连击中记录当前攻击者上下文、伤害段和 MP 写入次数。",
                "验证 MP=0、接近最大 MP 和无特效负向场景，并复核新调度器尾跳 004528FC。"
            ],
            ValidationRecipe = BuildPhysicalRecoveryRecipe()
        };
        var context = ContextSourceExpressionFactory.DecodeMemory([0x8B, 0x45, 0x08], hookAddress,
            "PhysicalAttackContext*", ContextRelationshipStatus.Unknown);
        ContextSourcePath Path(params ContextPathStep[] steps) => new() { Root = context, Steps = steps.ToList() };
        ContextPathStep Step(int offset, int width, string type) => new() { Offset = offset, ReadWidth = width, ResultType = type };
        contract.Slots.AddRange([
            PathSlot("physical-context", "物理攻击上下文", Path(), ContextSlotAccess.Read, ContextSlotEvidenceKind.Observation, 4, isExpectedJump),
            PathSlot("physical-attacker-unit", "当前攻击者战术单位", Path(Step(0x0C, 4, "TacticalUnit*")), ContextSlotAccess.Read, ContextSlotEvidenceKind.Relationship, 4, isExpectedJump),
            PathSlot("physical-attacker-character", "当前攻击者人物记录", Path(Step(0x08, 4, "RuntimeCharacter*")), ContextSlotAccess.Read, ContextSlotEvidenceKind.Relationship, 4, isExpectedJump),
            PathSlot("physical-current-damage", "当前物理伤害", Path(Step(0x84, 4, "PhysicalDamageValue")), ContextSlotAccess.Read, ContextSlotEvidenceKind.Observation, 4, isExpectedJump),
            PathSlot("current-mp", "当前攻击者 MP", Path(Step(0x0C, 4, "TacticalUnit*"), Step(0x14, 4, "CurrentMp")), ContextSlotAccess.ReadWrite, ContextSlotEvidenceKind.StateTransition, 4, isExpectedJump, "maximum-mp"),
            PathSlot("maximum-mp", "当前攻击者最大 MP", Path(Step(0x08, 4, "RuntimeCharacter*"), Step(0x1F, 2, "MaximumMp")), ContextSlotAccess.Read, ContextSlotEvidenceKind.DynamicBoundary, 2, isExpectedJump),
            PathSlot("physical-effect-subject", "物理特效判定对象", Path(Step(0x0C, 4, "TacticalUnit*")), ContextSlotAccess.Read, ContextSlotEvidenceKind.Relationship, 4, isExpectedJump)
        ]);
        contract.PointerInference = new ContextPointerInference
        {
            Register = "ecx",
            Candidates =
            [
                new ContextPointerCandidate
                {
                    Source = context, UnderlyingType = "TacticalUnit*",
                    RelationshipSemantic = ContextRelationshipStatus.EffectSubjectCandidate,
                    Confidence = "High", EvidenceZh = ["physicalContext+0C 是当前攻击者战术单位指针；仍需 V3 场景断言。"]
                }
            ],
            IsUnique = true, CanUseForWrite = false,
            BlockerCodes = ["RELATIONSHIP_SEMANTIC_UNVERIFIED"],
            ReasonsZh = ["静态路径唯一，但攻击者、反击者和 MP 受益对象仍需同场景动态确认。"]
        };
        return contract;
    }

    private static ContextSlotContract PathSlot(
        string id, string name, ContextSourcePath path, string access, string evidenceKind, int width, bool resolved,
        string clampMaximumSlotId = "")
        => new()
        {
            SlotId = id, DisplayNameZh = name, StructuredSource = path.Root, StructuredPath = path,
            SourceExpression = path.ToDisplayExpression(), Access = access, EvidenceKind = evidenceKind,
            ByteWidth = width, ClampMaximumSlotId = clampMaximumSlotId, IsStaticallyResolved = resolved,
            AllowedActions = access == ContextSlotAccess.ReadWrite ? [SemanticEffectAction.RestoreMpFixed] : [],
            LifetimeZh = "仅在 00418335 所在物理结算调用期间有效",
            StaticEvidenceZh = ["规范 EXE 指令链、004927F0 物理上下文和共享运行时语义注册表。"]
        };

    private static EffectValidationRecipe BuildPhysicalRecoveryRecipe()
        => new()
        {
            RecipeId = "ccz65-physical-after-damage-recovery-v3",
            RecipeVersion = 3,
            BreakpointAddresses = [0x00418335, 0x004528FC, 0x0045290A, 0x00452975, 0x00405AD5, 0x0043F70C],
            RequiredObservationKeys = ["physical-context", "physical-attacker-unit", "physical-attacker-character", "physical-current-damage", "current-mp", "maximum-mp"],
            RequiredRelationshipSlots = ["physical-effect-subject", "physical-attacker-unit", "physical-attacker-character"],
            Scenarios =
            [
                Scenario("physical-normal", "普通攻击", "执行一次普通物理攻击。", "MP 增加 5 点。"),
                Scenario("physical-critical", "暴击", "执行一次物理暴击。", "每个实际伤害段恢复一次。"),
                Scenario("physical-counter", "反击", "触发一次反击。", "当前反击者作为攻击者恢复。"),
                Scenario("physical-combo", "连击", "触发一次两段均命中的连击。", "两个实际伤害段分别恢复。"),
                Scenario("physical-mp-zero-boundary", "MP 为 0", "令攻击者当前 MP 为 0 后攻击。", "结果为 5，且不低于 0。"),
                Scenario("physical-mp-maximum-boundary", "接近最大 MP", "令攻击者只缺少 1 至 4 点 MP 后攻击。", "精确封顶到最大 MP。"),
                Scenario("physical-negative", "无特效负向场景", "使用未绑定试点特效的单位攻击。", "MP 不变化且不进入试点写入。")
            ]
        };

    private static EffectValidationScenarioDefinition Scenario(string id, string name, string instruction, string transition)
        => new() { ScenarioId = id, DisplayNameZh = name, InstructionZh = instruction, ExpectedTransition = transition };

    private static ContextSlotContract Slot(string id, string name, ContextSourceExpression expression, string access, List<string> actions, bool resolved, string evidence)
        => new()
        {
            SlotId = id, DisplayNameZh = name, StructuredSource = expression, SourceExpression = expression.ToAssemblyExpression(), Access = access,
            AllowedActions = actions, IsStaticallyResolved = resolved, LifetimeZh = "仅在当前 Hook 调用期间有效",
            StaticEvidenceZh = [evidence]
        };

    private static void ApplyStoredEvidence(CczProject project, HookExecutionContract contract)
    {
        if (contract.ContractVersion >= 2)
        {
            var acceptedV3 = EffectEvidenceBundleService.ReadTrustedV3(project, contract.ExeSha256, contract.ContractId)
                .Where(bundle => bundle.ContractHash.Equals(contract.ContractHash, StringComparison.OrdinalIgnoreCase) &&
                                 bundle.ContractCodeIdentityHash.Equals(contract.ContractCodeIdentityHash, StringComparison.OrdinalIgnoreCase) &&
                                 bundle.NormalizedProfileIdentity.Equals(contract.NormalizedProfileIdentity, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (acceptedV3.Count > 0)
            {
                ApplyV3Evidence(contract, acceptedV3);
                return;
            }
            var acceptedV2 = EffectEvidenceBundleService.ReadTrustedV2(project, contract.ExeSha256, contract.ContractId)
                .Where(bundle => bundle.ContractHash.Equals(contract.ContractHash, StringComparison.OrdinalIgnoreCase) &&
                                 bundle.ContractCodeIdentityHash.Equals(contract.ContractCodeIdentityHash, StringComparison.OrdinalIgnoreCase) &&
                                 bundle.NormalizedProfileIdentity.Equals(contract.NormalizedProfileIdentity, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ApplyV2Evidence(contract, acceptedV2);
            return;
        }
        var accepted = EffectEvidenceBundleService.ReadTrusted(project, contract.ExeSha256, contract.ContractId)
            .Where(bundle => bundle.ContractHash.Equals(contract.ContractHash, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (accepted.Count == 0) return;
        var requiredScenarios = RequiredScenarioIds(contract);
        var completeBundles = accepted
            .Where(bundle => requiredScenarios.All(id => bundle.CompletedScenarioIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        if (completeBundles.Count == 0) return;
        foreach (var slot in contract.Slots)
        {
            var observations = completeBundles.SelectMany(item => item.DerivedObservations)
                .Where(item => item.Key.Equals(slot.SlotId, StringComparison.OrdinalIgnoreCase)).ToList();
            slot.IsDynamicallyVerified = observations.Count > 0;
            slot.DynamicEvidenceZh.AddRange(completeBundles
                .Where(item => item.DerivedObservations.Any(observation => observation.Key.Equals(slot.SlotId, StringComparison.OrdinalIgnoreCase)))
                .Select(item => item.BundleId));
            var minimums = observations.Where(item => item.VerifiedMinimum.HasValue).Select(item => item.VerifiedMinimum!.Value).ToList();
            var maximums = observations.Where(item => item.VerifiedMaximum.HasValue).Select(item => item.VerifiedMaximum!.Value).ToList();
            if (minimums.Count > 0) slot.Minimum = minimums.Min();
            if (maximums.Count > 0) slot.Maximum = maximums.Max();
        }
        var requiredSlots = contract.Slots.Where(item => item.IsStaticallyResolved).ToList();
        var slotsVerified = requiredSlots.Count > 0 && requiredSlots.All(slot => slot.IsDynamicallyVerified);
        var boundsVerified = requiredSlots
            .Where(slot => slot.Access == ContextSlotAccess.ReadWrite)
            .All(slot => slot.Minimum.HasValue && slot.Maximum.HasValue);
        contract.AllowSemanticPreview = slotsVerified && boundsVerified;
        if (contract.AllowSemanticPreview)
        {
            contract.VerificationStatus = HookContractVerificationStatus.DynamicVerified;
            contract.VerificationStatusZh = "动态验证通过";
        }
        else
        {
            contract.VerificationStatusZh = "可信动态证据已导入，槽位或合法边界尚未闭环";
        }
    }

    private static void ApplyV2Evidence(HookExecutionContract contract, IReadOnlyList<EffectEvidenceBundleV2> bundles)
    {
        var complete = bundles.Where(bundle => RequiredScenarioIds(contract).All(id => bundle.CompletedScenarioIds.Contains(id, StringComparer.OrdinalIgnoreCase))).ToList();
        if (complete.Count == 0) return;
        foreach (var slot in contract.Slots.Where(item => item.IsStaticallyResolved))
        {
            var observations = complete.SelectMany(item => item.DerivedObservations)
                .Where(item => item.Key.Equals(slot.SlotId, StringComparison.OrdinalIgnoreCase)).ToList();
            slot.IsDynamicallyVerified = observations.Count > 0;
            slot.DynamicEvidenceZh.AddRange(complete.Where(bundle => bundle.DerivedObservations.Any(item => item.Key.Equals(slot.SlotId, StringComparison.OrdinalIgnoreCase))).Select(bundle => bundle.BundleId));
            var minimums = observations.Where(item => item.VerifiedMinimum.HasValue).Select(item => item.VerifiedMinimum!.Value).ToList();
            var maximums = observations.Where(item => item.VerifiedMaximum.HasValue).Select(item => item.VerifiedMaximum!.Value).ToList();
            if (minimums.Count > 0) slot.Minimum = minimums.Min();
            if (maximums.Count > 0) slot.Maximum = maximums.Max();
        }
        var required = contract.Slots.Where(item => item.IsStaticallyResolved).ToList();
        contract.AllowSemanticPreview = required.Count > 0 && required.All(item => item.IsDynamicallyVerified) &&
                                        required.Where(item => item.Access == ContextSlotAccess.ReadWrite).All(item => item.Minimum.HasValue && item.Maximum.HasValue);
        if (contract.AllowSemanticPreview)
        {
            contract.VerificationStatus = HookContractVerificationStatus.DynamicVerified;
            contract.VerificationStatusZh = "同档案、同代码身份的 v2 动态验证通过";
            contract.PointerInference.CanUseForWrite = contract.PointerInference.IsUnique;
        }
    }

    private static void ApplyV3Evidence(HookExecutionContract contract, IReadOnlyList<EffectEvidenceBundleV3> bundles)
    {
        var compatible = bundles.Where(bundle => bundle.ProbeRestored &&
                                                  bundle.RelationshipAssertions.Any(item => item.Verified && item.CallChainVerified))
            .Select(bundle => new EffectEvidenceBundleV2
            {
                BundleId = bundle.BundleId,
                ContractId = bundle.ContractId,
                ContractVersion = bundle.ContractVersion,
                ContractHash = bundle.ContractHash,
                ContractCodeIdentityHash = bundle.ContractCodeIdentityHash,
                ProfileId = bundle.ProfileId,
                NormalizedProfileIdentity = bundle.NormalizedProfileIdentity,
                ProjectId = bundle.ProjectId,
                GameRoot = bundle.OriginalGameRoot,
                SessionRoot = bundle.SessionRoot,
                ProcessId = bundle.ProcessId,
                ProcessPath = bundle.ProcessPath,
                LoadedModulePath = bundle.LoadedModulePath,
                LoadedModuleSize = bundle.LoadedModuleSize,
                LoadedModuleSha256 = bundle.LoadedModuleSha256,
                DebuggerVersion = bundle.DebuggerVersion,
                ToolBuildId = bundle.ToolBuildId,
                CreatedAtUtc = bundle.CreatedAtUtc,
                CompletedScenarioIds = bundle.CompletedScenarioIds,
                RawFiles = bundle.RawFiles,
                DerivedObservations = bundle.DerivedObservations
            }).ToList();
        ApplyV2Evidence(contract, compatible);
        if (contract.AllowSemanticPreview)
        {
            contract.VerificationStatusZh = "同档案、同代码身份的 V3 边界和关系语义验证通过";
            contract.PointerInference.CanUseForWrite = contract.PointerInference.IsUnique;
        }
    }

    private static HookExecutionContract? TryRestoreInstalledContract(CczProject project, HookExecutionContract currentView)
    {
        EffectDispatcherManifestV2? dispatcher;
        try
        {
            var lifecycle = new ModularEffectLifecycleService();
            dispatcher = lifecycle.ListDispatchers(project).FirstOrDefault(item =>
                item.ContractId.Equals(currentView.ContractId, StringComparison.OrdinalIgnoreCase) &&
                item.ContractSnapshot != null &&
                lifecycle.ResolveDispatcherStatus(project, item) == CompositeInstallationStatus.Complete);
        }
        catch
        {
            return null;
        }
        if (dispatcher?.ContractSnapshot == null ||
            dispatcher.ContractVersion < 2 || dispatcher.ContractSnapshot.ContractVersion < 2 ||
            dispatcher.ContractVersion != dispatcher.ContractSnapshot.ContractVersion ||
            !dispatcher.ContractHash.Equals(dispatcher.ContractSnapshot.ContractHash, StringComparison.OrdinalIgnoreCase) ||
            !new EffectWritableProfileService().Evaluate(project).CanWrite)
            return null;

        var snapshot = JsonSerializer.Deserialize<HookExecutionContract>(JsonSerializer.Serialize(dispatcher.ContractSnapshot, JsonOptions), JsonOptions);
        if (snapshot == null || !snapshot.ContractId.Equals(currentView.ContractId, StringComparison.OrdinalIgnoreCase)) return null;
        snapshot.VerificationStatus = HookContractVerificationStatus.StaticCandidate;
        snapshot.VerificationStatusZh = "已安装调度器契约快照，正在复验动态证据";
        snapshot.AllowSemanticPreview = false;
        foreach (var slot in snapshot.Slots)
        {
            slot.IsDynamicallyVerified = false;
            slot.Minimum = null;
            slot.Maximum = null;
            slot.DynamicEvidenceZh.Clear();
        }
        var computed = ComputeHash(snapshot);
        if (!computed.Equals(dispatcher.ContractHash, StringComparison.OrdinalIgnoreCase)) return null;
        snapshot.ContractHash = computed;
        ApplyStoredEvidence(project, snapshot);
        return snapshot.VerificationStatus == HookContractVerificationStatus.DynamicVerified && snapshot.AllowSemanticPreview
            ? snapshot
            : null;
    }

    private static IEnumerable<EffectContractEvidence> ReadAcceptedEvidence(CczProject project, HookExecutionContract contract)
    {
        var root = EvidenceRoot(project, contract.ExeSha256, contract.ContractId);
        if (!Directory.Exists(root)) yield break;
        foreach (var path in Directory.GetFiles(root, "*.json"))
        {
            EffectContractEvidence? item = null;
            try { item = JsonSerializer.Deserialize<EffectContractEvidence>(File.ReadAllText(path, Encoding.UTF8), JsonOptions); } catch { }
            if (item != null && item.ExeSha256.Equals(contract.ExeSha256, StringComparison.OrdinalIgnoreCase)) yield return item;
        }
    }

    private static bool IsPromotionEvidence(HookExecutionContract contract, EffectContractEvidence evidence)
    {
        var slots = contract.Slots.Where(item => item.IsStaticallyResolved).ToList();
        return slots.Count > 0 && evidence.HookHitCount >= 2 && evidence.CompletedScenariosZh.Count >= 2 &&
               evidence.CallStackZh.Count > 0 && slots.All(slot => evidence.ObservedSlots.ContainsKey(slot.SlotId)) &&
               slots.Where(slot => slot.Access == ContextSlotAccess.ReadWrite).All(slot =>
                   evidence.ObservedSlotMinimums.ContainsKey(slot.SlotId) && evidence.ObservedSlotMaximums.ContainsKey(slot.SlotId));
    }

    private static string EvidenceRoot(CczProject project, string sha, string contractId)
        => EffectEvidenceBundleService.EvidenceRoot(project, sha, contractId);

    private static string[] RequiredScenarioIds(HookExecutionContract contract)
        => contract.ContractFamilyId == "strategy-damage-formula"
            ? ["strategy-normal", "strategy-fixed-adjust", "strategy-percent-adjust", "strategy-minimum-boundary"]
            : ["physical-normal", "physical-critical", "physical-counter", "physical-combo"];

    private static List<EffectProbeBatch> BuildProbeBatches(HookExecutionContract contract)
    {
        var addresses = contract.ContractFamilyId == "strategy-damage-formula"
            ? new[] { 0x0043C2A9u, 0x0043C2B0u, 0x0043C2B5u, 0x0043C2C9u }
            : new[] { 0x00418330u, 0x00418335u, 0x00405AD5u, 0x0043F70Cu };
        return RequiredScenarioIds(contract).Select((scenario, index) => new EffectProbeBatch
        {
            BatchIndex = index + 1,
            ScenarioId = scenario,
            DisplayNameZh = scenario,
            X32dbgCommands = addresses.Select(address => $"bp {address:X8}").ToList()
        }).ToList();
    }

    private static string ComputeHash(HookExecutionContract contract)
    {
        var text = string.Join("|", contract.ContractId, contract.ContractVersion, contract.ProfileId,
            contract.NormalizedProfileIdentity, contract.ContractCodeIdentityHash, contract.HookAddress,
            contract.ExpectedOldBytesHex, contract.NormalizedLocatorSignature,
            string.Join(";", contract.Slots.Select(slot => $"{slot.SlotId}:{slot.SourceExpression}:{slot.StructuredSource?.SignedDisplacement}:{slot.StructuredSource?.DereferenceCount}:{slot.StructuredSource?.DerivedType}:{slot.Access}:{slot.ByteWidth}:{slot.EvidenceKind}:{slot.ClampMaximumSlotId}")),
            contract.ContinuationPolicy, contract.ContinuationAddress,
            contract.ValidationRecipe.RecipeId, contract.ValidationRecipe.RecipeVersion,
            string.Join(";", contract.ValidationRecipe.Scenarios.Select(item => item.ScenarioId)),
            string.Join(";", contract.AllowedActions), string.Join(";", contract.AllowedCallSymbols));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string ComputeCodeIdentity(ExecutableAnalysisSnapshot executable, params (uint Address, int Length)[] ranges)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var range in ranges.OrderBy(item => item.Address))
        {
            hash.AppendData(BitConverter.GetBytes(range.Address));
            hash.AppendData(executable.ReadVirtualRange(range.Address, range.Length).Span);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
