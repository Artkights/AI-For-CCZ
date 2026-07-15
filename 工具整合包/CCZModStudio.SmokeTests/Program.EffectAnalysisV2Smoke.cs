using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Diagnostics;

internal partial class Program
{
    static void RunEffectAnalysisV2Smoke(CczProject project)
    {
        AssertExpression([0x8B, 0x4D, 0xF0], 0x00401000, "dword [ebp-0x10]");
        AssertExpression([0x01, 0x45, 0xFC], 0x00401010, "dword [ebp-0x04]");
        AssertExpression([0x8B, 0x4D, 0xAC], 0x00401020, "dword [ebp-0x54]");

        const uint code = 0x00510000;
        var helper = X86ContextDataFlowAnalyzer.BattlefieldUnitIdToPointerAddress;
        var core = X86ContextDataFlowAnalyzer.CoreEffectEngineAddress;
        var bytes = new List<byte>();
        bytes.AddRange([0x8B, 0x4D, 0xF0]); // mov ecx,[ebp-10]
        bytes.AddRange(BuildCall(code + (uint)bytes.Count, helper));
        bytes.AddRange([0x89, 0xCA]); // mov edx,ecx
        bytes.AddRange([0x89, 0xD1]); // mov ecx,edx
        bytes.AddRange(BuildCall(code + (uint)bytes.Count, core));
        var instructions = new X86InstructionScanner().DecodeBlock(bytes.ToArray(), code);
        var callIndex = instructions.ToList().FindIndex(item => item.IsDirectCall && item.BranchTarget == core);
        var inference = new X86ContextDataFlowAnalyzer().Analyze(instructions, callIndex);
        if (!inference.IsUnique || inference.Candidates.Single().UnderlyingType != "TacticalUnit*" || inference.CanUseForWrite)
            throw new InvalidOperationException("ECX mov/寄存器复制/004061F9 摘要没有恢复为唯一 TacticalUnit*，或在角色语义未验证时错误开放写入。");

        var ordinary = new X86InstructionScanner().DecodeBlock([
            0x8B, 0x4D, 0x08,
            .. BuildCall(code + 3, core)
        ], code);
        var ordinaryInference = new X86ContextDataFlowAnalyzer().Analyze(ordinary, 1);
        if (ordinaryInference.Candidates.Single().UnderlyingType == "TacticalUnit*" || ordinaryInference.CanUseForWrite)
            throw new InvalidOperationException("普通上下文指针仅因进入 ECX 被误判为 TacticalUnit*。");

        var branch = new X86InstructionScanner().DecodeBlock([
            0x74, 0x03,             // jz +3
            0x8B, 0x4D, 0xF0,       // mov ecx,[ebp-10]
            .. BuildCall(code + 5, core)
        ], code);
        var branchInference = new X86ContextDataFlowAnalyzer().Analyze(branch, 2);
        if (branchInference.IsUnique || !branchInference.BlockerCodes.Contains("CONTROL_FLOW_JOIN_UNRESOLVED"))
            throw new InvalidOperationException("ECX 分支多来源没有保守阻断。");

        var source = ResolveEffectInjectionDiscoverySmokeSourceProject(project);
        var audit = new ExecutableProfileAuditService().Audit(source);
        if (!audit.CanWriteRegisteredData || audit.ProfileId != EngineEffectProfileRegistry.Profile65Id ||
            audit.TrustStatus is not ExecutableProfileTrustStatus.ExactCanonical and not ExecutableProfileTrustStatus.AutoDerivedDataOnly)
            throw new InvalidOperationException("当前 6.5 规范档案或已知数据型变体没有通过审计：" + audit.SummaryZh);

        var knownVariant = File.ReadAllBytes(source.ResolveGameFile("Ekd5.exe"));
        knownVariant[0xA2CE0] = 0x90;
        var variantAudit = new ExecutableProfileAuditService().AuditBytesForTest(knownVariant);
        if (!variantAudit.CanWriteRegisteredData || variantAudit.TrustStatus != ExecutableProfileTrustStatus.AutoDerivedDataOnly ||
            variantAudit.CurrentSha256 != EngineEffectProfileRegistry.Known65VariantSha256 || variantAudit.RegisteredDifferences.Count != 1)
            throw new InvalidOperationException("84E3 已知数据型变体没有通过登记字段自动派生审计。");
        var illegalField = (byte[])knownVariant.Clone();
        illegalField[0xA2CE0] = 0x92;
        var illegalAudit = new ExecutableProfileAuditService().AuditBytesForTest(illegalField);
        if (illegalAudit.CanWriteRegisteredData || !illegalAudit.BlockerCodes.Contains("REGISTERED_VALUE_INVALID"))
            throw new InvalidOperationException("登记字段非法枚举值没有被阻断。");

        using var temp = new TemporarySmokeDirectory("EffectProfileAuditV2");
        var copy = Path.Combine(temp.Path, "Ekd5.exe");
        File.Copy(source.ResolveGameFile("Ekd5.exe"), copy);
        var image = File.ReadAllBytes(copy);
        image[0x500] ^= 0x01; // unregistered executable code byte
        var unknownAudit = new ExecutableProfileAuditService().AuditBytesForTest(image);
        if (unknownAudit.CanWriteRegisteredData || !unknownAudit.BlockerCodes.Contains("UNKNOWN_DIFFERENCE"))
            throw new InvalidOperationException("未登记代码区即使只改 1 字节也被错误自动放行。");

        PerformanceMetrics.Reset();
        EffectAnalysisCoordinator.Shared.ResetCachesForTests(source);
        var analyses = Enumerable.Range(0, 10).Select(_ => EffectAnalysisCoordinator.Shared.ScanAsync(source)).ToArray();
        Task.WaitAll(analyses);
        var complete = analyses[0].Result;
        if (analyses.Any(task => !ReferenceEquals(complete, task.Result)))
            throw new InvalidOperationException("并发完整分析没有合并为同一个不可变快照。");
        var metrics = PerformanceMetrics.GetSnapshot().Counters;
        AssertCounter(metrics, "ExecutableAnalysis.FullReadCount", 1);
        AssertCounter(metrics, "ExecutableAnalysis.HashCount", 1);
        AssertCounter(metrics, "ExecutableAnalysis.PeParseCount", 1);
        AssertCounter(metrics, "ExecutableAnalysis.InstructionScanCount", 1);
        AssertCounter(metrics, "EffectAnalysis.CacheMisses", 1);
        AssertCounter(metrics, "EffectAnalysis.CacheHits", 9);
        var beforeBatch = PerformanceMetrics.GetSnapshot().Counters;
        var compatibility = new CompositeEffectMemberCompatibilityService()
            .EvaluateBatch(source, complete.Inventory, complete.MechanismProfile);
        var afterBatch = PerformanceMetrics.GetSnapshot().Counters;
        if (compatibility.Count != complete.Inventory.Effects.Count)
            throw new InvalidOperationException("复合成员批量兼容结果没有覆盖完整库存。");
        foreach (var counter in new[]
                 {
                     "ExecutableAnalysis.FullReadCount", "ExecutableAnalysis.HashCount",
                     "ExecutableAnalysis.PeParseCount", "ExecutableAnalysis.InstructionScanCount"
                 })
        {
            if (afterBatch.GetValueOrDefault(counter) != beforeBatch.GetValueOrDefault(counter))
                throw new InvalidOperationException($"批量兼容评估重复执行昂贵分析：{counter}。");
        }
        var ledger = new EffectConfirmationLedgerService().Build(source, complete);
        if (ledger.Effects.Count != complete.Inventory.Effects.Count || ledger.Effects.Any(item => item.Fields.Count == 0))
            throw new InvalidOperationException("特效确认台账没有覆盖全部逻辑特效及其字段。 ");
        var ledgerExport = new EffectConfirmationLedgerService().Export(source, complete);
        if (!File.Exists(ledgerExport.JsonPath) || !File.Exists(ledgerExport.MarkdownPath) ||
            ledgerExport.EffectCount != complete.Inventory.Effects.Count)
            throw new InvalidOperationException("特效确认台账导出不完整。 ");
        var discoveryCandidates = complete.Inventory.Discovery.Candidates;
        if (discoveryCandidates.Any(item => item.InstallationEvidence.Status != "DiagnosticOnly" || item.InstallationEvidence.IsPresent))
            throw new InvalidOperationException("库存构建污染了共享发现候选的安装证据。");
        var inventoryTimer = Stopwatch.StartNew();
        _ = new EffectInventoryService().Scan(source);
        inventoryTimer.Stop();
        if (inventoryTimer.ElapsedMilliseconds > 300)
            throw new InvalidOperationException($"热缓存库存扫描超过 300ms：{inventoryTimer.ElapsedMilliseconds}ms。");
        _ = new NativeEffectConfigurationService().Read(source, CompositeEffectChannel.PersonalJob, 1);
        var nativeReadTimer = Stopwatch.StartNew();
        var native = new NativeEffectConfigurationService().Read(source, CompositeEffectChannel.PersonalJob, 1);
        nativeReadTimer.Stop();
        if (nativeReadTimer.ElapsedMilliseconds > 200)
            throw new InvalidOperationException($"热缓存单个原生特效读取超过 200ms：{nativeReadTimer.ElapsedMilliseconds}ms。");
        var previewTimer = Stopwatch.StartNew();
        _ = new NativeEffectConfigurationService().Preview(source, new NativeEffectEditDraft
        {
            Channel = CompositeEffectChannel.PersonalJob,
            EffectId = 1,
            Name = string.IsNullOrWhiteSpace(native.GameName) ? native.Name : native.GameName
        });
        previewTimer.Stop();
        if (previewTimer.ElapsedMilliseconds > 300)
            throw new InvalidOperationException($"热缓存原生预览超过 300ms：{previewTimer.ElapsedMilliseconds}ms。");
        Console.WriteLine($"EFFECT_ANALYSIS_V2_SMOKE_OK trust={audit.TrustStatus} fullRead={metrics.GetValueOrDefault("ExecutableAnalysis.FullReadCount")} contracts={complete.HookContracts.Count} hotInventoryMs={inventoryTimer.ElapsedMilliseconds} hotNativeMs={nativeReadTimer.ElapsedMilliseconds} hotPreviewMs={previewTimer.ElapsedMilliseconds}");
    }

    private static void AssertExpression(byte[] bytes, uint address, string expected)
    {
        var expression = ContextSourceExpressionFactory.DecodeMemory(bytes, address).ToAssemblyExpression();
        if (!expression.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"有符号位移解码错误：期望 {expected}，实际 {expression}。");
    }

    private static byte[] BuildCall(uint address, uint target)
        => EffectPatchByteService.BuildRelativeCall(address, target);

    private static void AssertCounter(IReadOnlyDictionary<string, long> counters, string key, long expected)
    {
        if (counters.GetValueOrDefault(key) != expected)
            throw new InvalidOperationException($"昂贵操作计数 {key} 期望 {expected}，实际 {counters.GetValueOrDefault(key)}。");
    }
}
