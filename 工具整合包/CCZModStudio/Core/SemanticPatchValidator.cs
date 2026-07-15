using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class SemanticPatchValidator
{
    public SemanticPatchValidationResult ValidateProgram(SemanticEffectProgram program, HookExecutionContract contract, CompiledSemanticBody body)
    {
        var result = new SemanticPatchValidationResult();
        if (!contract.AllowedActions.Contains(program.Action, StringComparer.OrdinalIgnoreCase)) result.WarningsZh.Add("动作超出执行契约白名单。");
        foreach (var slotId in body.WrittenSlots)
        {
            var slot = contract.Slots.FirstOrDefault(item => item.SlotId.Equals(slotId, StringComparison.OrdinalIgnoreCase));
            if (slot == null || slot.Access != ContextSlotAccess.ReadWrite) result.WarningsZh.Add($"槽位“{slotId}”没有写权限。");
            else if (!slot.AllowedActions.Contains(program.Action, StringComparer.OrdinalIgnoreCase)) result.WarningsZh.Add($"动作不能写入槽位“{slot.DisplayNameZh}”。");
        }
        if (!body.AssemblySource.Contains("pushad", StringComparison.OrdinalIgnoreCase) || !body.AssemblySource.Contains("popad", StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("生成体没有成对保护通用寄存器。");
        if (contract.PreserveFlags && (!body.AssemblySource.Contains("pushfd", StringComparison.OrdinalIgnoreCase) || !body.AssemblySource.Contains("popfd", StringComparison.OrdinalIgnoreCase)))
            result.WarningsZh.Add("生成体没有成对保护标志位。");
        if (!body.AssemblySource.Contains("call 0x004101D9", StringComparison.OrdinalIgnoreCase)) result.WarningsZh.Add("生成体没有调用当前契约允许的特效判定核心。");
        result.StackDelta = 0;
        result.RegistersWritten = ["eax", "ecx", "edx"];
        result.MemoryWrites = body.WrittenSlots.ToList();
        result.CallTargets = ["core_effect_engine"];
        result.IsValid = result.WarningsZh.Count == 0;
        result.SummaryZh = result.IsValid ? "语义动作通过槽位、寄存器、标志位和调用白名单检查。" : "语义动作校验失败：" + string.Join("；", result.WarningsZh);
        return result;
    }

    public SemanticPatchValidationResult ValidateCompiled(byte[] bytes, uint address, HookExecutionContract contract, uint returnAddress)
    {
        var result = new SemanticPatchValidationResult();
        var instructions = new X86InstructionScanner().DecodeBlock(bytes, address, "semantic-compiled");
        var stack = 0;
        var pushad = 0;
        var pushfd = 0;
        foreach (var instruction in instructions)
        {
            result.RegistersWritten.AddRange(instruction.RegistersWritten);
            result.MemoryWrites.AddRange(instruction.MemoryWrites);
            if (instruction.Mnemonic == "push") stack -= 4;
            else if (instruction.Mnemonic == "pop") stack += 4;
            else if (instruction.Mnemonic == "pushad") { stack -= 32; pushad++; }
            else if (instruction.Mnemonic == "popad") { stack += 32; pushad--; }
            else if (instruction.Mnemonic == "pushfd") { stack -= 4; pushfd++; }
            else if (instruction.Mnemonic == "popfd") { stack += 4; pushfd--; }
            if (instruction.IsDirectCall && instruction.BranchTarget == EffectPatchByteService.CoreEffectEngineAddress)
            {
                stack += 16;
                result.CallTargets.Add("core_effect_engine");
            }
            else if (instruction.IsDirectCall)
            {
                result.WarningsZh.Add($"调用了契约未登记的地址 0x{instruction.BranchTarget:X8}。");
            }
            if (instruction.IsReturn) result.WarningsZh.Add("语义代码洞包含 ret，必须显式跳回契约返回地址。");
        }
        result.StackDelta = stack;
        if (stack != contract.ExpectedStackDelta) result.WarningsZh.Add($"所有指令顺序模拟后的栈增量为 {stack}，契约要求 {contract.ExpectedStackDelta}。");
        if (pushad != 0) result.WarningsZh.Add("通用寄存器保护指令没有成对出现。");
        if (contract.PreserveFlags && pushfd != 0) result.WarningsZh.Add("标志位保护指令没有成对出现。");
        var expectedContinuation = contract.ContinuationPolicy == HookContinuationPolicies.ChainExistingJumpTarget
            ? contract.ContinuationAddress
            : returnAddress;
        if (!instructions.Any(item => item.IsDirectJump && item.BranchTarget == expectedContinuation))
            result.WarningsZh.Add($"没有显式跳往契约 continuation 0x{expectedContinuation:X8}。");
        result.RegistersWritten = result.RegistersWritten.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.MemoryWrites = result.MemoryWrites.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.CallTargets = result.CallTargets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.IsValid = result.WarningsZh.Count == 0;
        result.SummaryZh = result.IsValid ? "编译结果通过栈、寄存器、标志位、调用和返回路径检查。" : "编译结果校验失败：" + string.Join("；", result.WarningsZh);
        return result;
    }
}
