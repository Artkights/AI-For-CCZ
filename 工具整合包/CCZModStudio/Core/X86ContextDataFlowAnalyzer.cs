using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// Conservative backward ECX data-flow for the effect engine ABI. It resolves
/// simple mov/lea/copies and two verified helper summaries, but deliberately
/// returns multiple/unknown candidates at control-flow joins and unknown calls.
/// </summary>
public sealed class X86ContextDataFlowAnalyzer
{
    public const uint CoreEffectEngineAddress = 0x004101D9;
    public const uint BattlefieldUnitIdToPointerAddress = EngineRuntimeSemanticRegistry.BattlefieldIdToTacticalUnitAddress;
    public const uint DataIdToRuntimeCharacterAddress = EngineRuntimeSemanticRegistry.DataIdToRuntimeCharacterAddress;
    public const uint TacticalUnitToRuntimeCharacterAddress = EngineRuntimeSemanticRegistry.TacticalUnitToRuntimeCharacterAddress;
    public const uint StrategyIdToRecordAddress = EngineRuntimeSemanticRegistry.StrategyIdToRecordAddress;

    public ContextPointerInference Analyze(CczProject project, uint callAddress, string targetFile = "Ekd5.exe")
        => Analyze(ExecutableAnalysisSnapshotCache.Shared.GetBase(project, targetFile).InstructionScan, callAddress);

    public ContextPointerInference Analyze(X86ScanResult scan, uint callAddress, int maximumLookbackInstructions = 48)
    {
        var section = scan.InstructionsBySection.Values.FirstOrDefault(items => items.Any(item => item.Address == callAddress));
        if (section == null) return Blocked(callAddress, "CALL_NOT_DECODED", "调用地址不在共享反汇编快照中。");
        var callIndex = section.FindIndex(item => item.Address == callAddress);
        if (callIndex < 0) return Blocked(callAddress, "CALL_NOT_DECODED", "调用地址不在共享反汇编快照中。");
        return Analyze(section, callIndex, maximumLookbackInstructions);
    }

    public ContextPointerInference Analyze(IReadOnlyList<X86InstructionInfo> instructions, int callIndex, int maximumLookbackInstructions = 48)
    {
        if (callIndex < 0 || callIndex >= instructions.Count)
            return Blocked(0, "CALL_NOT_DECODED", "调用索引无效。");
        var call = instructions[callIndex];
        var result = new ContextPointerInference { CallAddress = call.Address };
        if (!call.IsDirectCall || call.BranchTarget != CoreEffectEngineAddress)
        {
            result.BlockerCodes.Add("NOT_CORE_EFFECT_CALL");
            result.ReasonsZh.Add("目标不是直接 call 004101D9，不能套用特效 ECX ABI。");
            return result;
        }

        var start = Math.Max(0, callIndex - maximumLookbackInstructions);
        var branchCount = instructions.Skip(start).Take(callIndex - start).Count(item => item.IsConditionalBranch);
        var trace = TraceRegister(instructions, "ecx", callIndex - 1, start, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        result.Candidates.AddRange(trace.Candidates);
        result.BlockerCodes.AddRange(trace.Blockers.Distinct(StringComparer.Ordinal));
        result.ReasonsZh.AddRange(trace.Reasons.Distinct(StringComparer.Ordinal));
        if (branchCount > 0)
        {
            result.BlockerCodes.Add("CONTROL_FLOW_JOIN_UNRESOLVED");
            result.ReasonsZh.Add("ECX 定义窗口包含尚未合并的条件分支，可能存在多个来源。");
        }
        result.IsUnique = result.Candidates.Count == 1 && result.BlockerCodes.Count == 0;
        result.CanUseForWrite = result.IsUnique && result.Candidates[0].UnderlyingType.Equals("TacticalUnit*", StringComparison.Ordinal) &&
                                result.Candidates[0].RelationshipSemantic.Equals(ContextRelationshipStatus.Verified, StringComparison.Ordinal);
        if (result.IsUnique && !result.CanUseForWrite)
        {
            result.BlockerCodes.Add("RELATIONSHIP_SEMANTIC_UNVERIFIED");
            result.ReasonsZh.Add("底层指针来源已恢复，但攻击者/受击者/护卫者等关系语义尚未由场景证据验证。");
        }
        if (result.Candidates.Count == 0 && result.BlockerCodes.Count == 0)
        {
            result.BlockerCodes.Add("ECX_SOURCE_UNRESOLVED");
            result.ReasonsZh.Add("在安全回溯窗口内没有找到 ECX 的唯一来源。");
        }
        if (result.Candidates.Count > 1)
        {
            result.IsUnique = false;
            result.CanUseForWrite = false;
            result.BlockerCodes.Add("ECX_MULTIPLE_CANDIDATES");
            result.ReasonsZh.Add("ECX 存在多个不同来源；保持只读，不能假装唯一。");
        }
        return result;
    }

    private static TraceResult TraceRegister(
        IReadOnlyList<X86InstructionInfo> instructions,
        string register,
        int index,
        int start,
        ISet<string> visited)
    {
        var visitKey = register + "@" + index;
        if (!visited.Add(visitKey)) return TraceResult.Block("DATA_FLOW_CYCLE", "寄存器定义链形成循环。");
        for (var i = index; i >= start; i--)
        {
            var instruction = instructions[i];
            if (instruction.IsReturn || instruction.IsIndirectJump)
                return TraceResult.Block("CONTROL_FLOW_BOUNDARY", "ECX 回溯遇到返回或间接跳转边界。");

            if (instruction.IsDirectCall)
            {
                if (register.Equals("ecx", StringComparison.OrdinalIgnoreCase) &&
                    instruction.BranchTarget is BattlefieldUnitIdToPointerAddress or DataIdToRuntimeCharacterAddress or
                        TacticalUnitToRuntimeCharacterAddress or StrategyIdToRecordAddress)
                {
                    var semantic = EngineRuntimeSemanticRegistry.Functions[instruction.BranchTarget.Value];
                    var type = semantic.OutputType;
                    var input = TraceRegister(instructions, "ecx", i - 1, start, visited);
                    var chain = input.Candidates.SelectMany(item => item.Source.DataFlowChain).ToList();
                    chain.Add($"0x{instruction.Address:X8}: call 0x{instruction.BranchTarget:X8} => {type}");
                    return TraceResult.One(new ContextPointerCandidate
                    {
                        Source = ContextSourceExpressionFactory.Register("ecx", instruction.Address, type, chain),
                        UnderlyingType = type,
                        RelationshipSemantic = ContextRelationshipStatus.EffectSubjectCandidate,
                        Confidence = "High",
                        EvidenceZh = [$"{instruction.BranchTarget:X8} 函数摘要：{semantic.InputType} -> {semantic.OutputType}。"]
                    });
                }
                // cdecl/stdcall calls may clobber ECX. Do not trace across unknown calls.
                return TraceResult.Block("UNKNOWN_CALL_CLOBBER", $"0x{instruction.Address:X8} 的未知调用可能改写 ECX。");
            }

            if (!TryGetRegisterDestination(instruction, register, out var source)) continue;
            if (instruction.Mnemonic.Equals("mov", StringComparison.OrdinalIgnoreCase))
            {
                if (source.Kind.Equals("Register", StringComparison.OrdinalIgnoreCase))
                {
                    var copied = TraceRegister(instructions, source.Register, i - 1, start, visited);
                    foreach (var candidate in copied.Candidates)
                    {
                        candidate.Source.DataFlowChain.Add($"0x{instruction.Address:X8}: mov {register}, {source.Register}");
                        candidate.Source.DefinitionInstructionAddress = instruction.Address;
                    }
                    return copied;
                }
                if (source.Kind.Equals("Memory", StringComparison.OrdinalIgnoreCase))
                {
                    var expression = ContextSourceExpressionFactory.FromOperand(source, instruction.Address, "UnknownPointer",
                        ContextRelationshipStatus.EffectSubjectCandidate,
                        [$"0x{instruction.Address:X8}: mov {register}, {source.MemoryText}"]);
                    return TraceResult.One(new ContextPointerCandidate
                    {
                        Source = expression,
                        UnderlyingType = "UnknownPointer",
                        RelationshipSemantic = ContextRelationshipStatus.EffectSubjectCandidate,
                        Confidence = "Medium",
                        EvidenceZh = ["确认该值进入特效判定 ECX，但没有仅凭寄存器用途把它误命名为攻击者或受击者。"]
                    });
                }
                return TraceResult.Block("ECX_NON_POINTER_DEFINITION", $"0x{instruction.Address:X8} 把非指针来源写入 ECX。");
            }
            if (instruction.Mnemonic.Equals("lea", StringComparison.OrdinalIgnoreCase) && source.Kind.Equals("Memory", StringComparison.OrdinalIgnoreCase))
            {
                var expression = ContextSourceExpressionFactory.FromOperand(source, instruction.Address, "AddressCandidate",
                    ContextRelationshipStatus.EffectSubjectCandidate,
                    [$"0x{instruction.Address:X8}: lea {register}, {source.MemoryText}"], dereferenceCount: 0);
                return TraceResult.One(new ContextPointerCandidate
                {
                    Source = expression,
                    UnderlyingType = "AddressCandidate",
                    RelationshipSemantic = ContextRelationshipStatus.EffectSubjectCandidate,
                    Confidence = "Medium",
                    EvidenceZh = ["LEA 只证明地址来源；结构类型仍需函数摘要或场景证据。"]
                });
            }
            return TraceResult.Block("ECX_COMPLEX_DEFINITION", $"0x{instruction.Address:X8} 使用未支持的 {instruction.Mnemonic} 定义 ECX。");
        }
        return TraceResult.Block("ECX_SOURCE_UNRESOLVED", "在回溯窗口内没有找到 ECX 定义。");
    }

    private static bool TryGetRegisterDestination(X86InstructionInfo instruction, string register, out X86OperandInfo source)
    {
        source = default!;
        if (instruction.Operands.Count < 2) return false;
        var destination = instruction.Operands[0];
        if (!destination.Kind.Equals("Register", StringComparison.OrdinalIgnoreCase) ||
            !destination.Register.Equals(register, StringComparison.OrdinalIgnoreCase)) return false;
        source = instruction.Operands[1];
        return true;
    }

    private static ContextPointerInference Blocked(uint callAddress, string code, string reason)
        => new() { CallAddress = callAddress, BlockerCodes = [code], ReasonsZh = [reason] };

    private sealed class TraceResult
    {
        public List<ContextPointerCandidate> Candidates { get; } = [];
        public List<string> Blockers { get; } = [];
        public List<string> Reasons { get; } = [];
        public static TraceResult One(ContextPointerCandidate candidate) { var result = new TraceResult(); result.Candidates.Add(candidate); return result; }
        public static TraceResult Block(string code, string reason) { var result = new TraceResult(); result.Blockers.Add(code); result.Reasons.Add(reason); return result; }
    }
}
