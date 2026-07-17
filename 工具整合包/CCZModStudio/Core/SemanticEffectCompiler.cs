using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class SemanticEffectCompiler
{
    public CompiledSemanticBody Compile(CczProject project, SemanticEffectProgram program)
    {
        var result = new CompiledSemanticBody();
        HookExecutionContract contract;
        try { contract = new HookExecutionContractService().Read(project, program.HookContractId); }
        catch (Exception ex) { result.WarningsZh.Add(ex.Message); return result; }
        result.Contract = contract;
        ValidateProgram(program, contract, result.WarningsZh);
        if (result.WarningsZh.Count > 0) return result;

        ContextSlotContract subject;
        ContextSlotContract actionSlot;
        try
        {
            subject = RequireSlot(contract, program.SubjectSlotId, ContextSlotAccess.Read);
            actionSlot = ResolveActionSlot(contract, program.Action);
        }
        catch (Exception ex)
        {
            result.WarningsZh.Add(ex.Message);
            return result;
        }
        result.ReadSlots.Add(subject.SlotId);
        result.ReadSlots.Add(actionSlot.SlotId);
        result.WrittenSlots.Add(actionSlot.SlotId);
        result.RequiredSymbols.Add("core_effect_engine");
        var chainRecovery = program.Action == SemanticEffectAction.RestoreMpFixed &&
                            contract.ContinuationPolicy == HookContinuationPolicies.ChainExistingJumpTarget;
        result.AssemblySource = chainRecovery
            ? BuildPhysicalRecoverySource(program, contract)
            : BuildSource(program, subject, actionSlot);
        result.MeaningZh = Explain(program);
        result.CanCompile = true;
        var boundsComplete = actionSlot.Minimum.HasValue && actionSlot.Maximum.HasValue ||
                             chainRecovery && contract.Slots.Any(item =>
                                 item.SlotId.Equals(actionSlot.ClampMaximumSlotId, StringComparison.OrdinalIgnoreCase) && item.IsStaticallyResolved);
        if (!boundsComplete) result.WarningsZh.Add($"槽位“{actionSlot.DisplayNameZh}”尚未声明经过验证的数值上下限，不能开放算术写入。");
        var sandboxStatic = EffectSandboxService.IsSandbox(project) && contract.VerificationStatus == HookContractVerificationStatus.StaticCandidate;
        result.CanPreview = boundsComplete && (contract.AllowSemanticPreview && contract.VerificationStatus == HookContractVerificationStatus.DynamicVerified || sandboxStatic);
        if (!result.CanPreview)
            result.WarningsZh.Add("语义代码已经生成，但当前执行契约尚未通过同一 SHA 的动态验证，因此不能生成可写预览包。");
        result.Validation = new SemanticPatchValidator().ValidateProgram(program, contract, result);
        return result;
    }

    private static void ValidateProgram(SemanticEffectProgram program, HookExecutionContract contract, List<string> warnings)
    {
        if (contract.ContractVersion < 2 || string.IsNullOrWhiteSpace(contract.ContractCodeIdentityHash))
            warnings.Add("行为逻辑只接受带代码身份的执行契约 v2。");
        if (contract.Slots.Any(slot => slot.IsStaticallyResolved && slot.StructuredSource == null))
            warnings.Add("执行契约包含未结构化的栈槽表达式，拒绝直接拼接历史字符串。");
        if (!contract.AllowedActions.Contains(program.Action, StringComparer.OrdinalIgnoreCase)) warnings.Add("所选动作不属于该执行契约允许范围。");
        if (program.Channel != CompositeEffectChannel.PersonalJob) warnings.Add("首批语义注入只开放人物/兵种渠道。");
        if (program.PersonalEffectId is < 0 or > 0xFE) warnings.Add("个人/兵种特效号必须在 00-FE 范围内。");
        if (program.ItemEffectId != 0) warnings.Add("人物/兵种渠道必须把宝物渠道明确设为 00（未启用）。");
        if (program.StackingMode is < 0 or > 2) warnings.Add("叠加方式必须是 0、1 或 2。");
        if (program.Action.Contains("Percent", StringComparison.OrdinalIgnoreCase) && program.Value is < 0 or > 100)
            warnings.Add("百分比必须在 0-100 之间。");
        if (!program.Action.Contains("Percent", StringComparison.OrdinalIgnoreCase) && program.Value is < 0 or > 65535)
            warnings.Add("固定点数必须在 0-65535 之间。");
        if (program.Action is SemanticEffectAction.RestoreHpFixed or SemanticEffectAction.RestoreHpMaxPercent or SemanticEffectAction.RestoreMpMaxPercent ||
            program.Action == SemanticEffectAction.RestoreMpFixed && contract.ContinuationPolicy != HookContinuationPolicies.ChainExistingJumpTarget)
            warnings.Add("恢复动作尚未证明当前值、逐单位最大值和界面刷新顺序，当前只保留研究模块。");
        if (program.Action == SemanticEffectAction.RestoreMpFixed &&
            (program.PersonalEffectId is < 1 or > 0xFE || program.ItemEffectId != 0 ||
             program.StackingMode != 1 || program.EffectValueMode != 0 || program.Value != 5))
            warnings.Add("首轮物理恢复试点固定为个人/兵种号 01-FE、宝物号 00、叠加方式 1、固定恢复 5 MP。");
    }

    private static ContextSlotContract RequireSlot(HookExecutionContract contract, string slotId, string requiredAccess)
    {
        var slot = contract.Slots.FirstOrDefault(item => item.SlotId.Equals(slotId, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"执行契约没有声明槽位“{slotId}”。");
        if (!slot.IsStaticallyResolved) throw new InvalidOperationException($"槽位“{slot.DisplayNameZh}”尚未完成静态定位。");
        if (requiredAccess == ContextSlotAccess.ReadWrite && slot.Access != ContextSlotAccess.ReadWrite)
            throw new InvalidOperationException($"槽位“{slot.DisplayNameZh}”不允许写入。");
        return slot;
    }

    private static ContextSlotContract ResolveActionSlot(HookExecutionContract contract, string action)
    {
        var id = action switch
        {
            SemanticEffectAction.AddDamageFixed or SemanticEffectAction.SubtractDamageFixed or
            SemanticEffectAction.AddDamagePercent or SemanticEffectAction.SubtractDamagePercent => "strategy-current-damage",
            SemanticEffectAction.RestoreHpFixed or SemanticEffectAction.RestoreHpMaxPercent => "current-hp",
            SemanticEffectAction.RestoreMpFixed or SemanticEffectAction.RestoreMpMaxPercent => "current-mp",
            _ => string.Empty
        };
        return RequireSlot(contract, id, ContextSlotAccess.ReadWrite);
    }

    private static string BuildSource(SemanticEffectProgram program, ContextSlotContract subject, ContextSlotContract target)
    {
        var subjectExpression = RequireStructuredExpression(subject);
        var targetExpression = RequireStructuredExpression(target);
        var minimum = target.Minimum ?? 1;
        var maximum = target.Maximum ?? int.MaxValue;
        var lines = new List<string>
        {
            "pushfd", "pushad", $"mov ecx, {subjectExpression}",
            $"push dword 0x{program.EffectValueMode:X8}", $"push dword 0x{program.StackingMode:X8}",
            $"push dword 0x{program.ItemEffectId:X8}", $"push dword 0x{program.PersonalEffectId:X8}",
            "call 0x004101D9", "test eax, eax", "jz .semantic_done"
        };
        var value = program.ValueSource == SemanticEffectValueSource.CoreReturnValue ? string.Empty : $"mov eax, 0x{program.Value:X8}";
        if (!string.IsNullOrWhiteSpace(value)) lines.Add(value);
        if (program.Action is SemanticEffectAction.AddDamagePercent or SemanticEffectAction.SubtractDamagePercent)
        {
            lines.Add("mov ecx, eax");
            lines.Add($"mov eax, {targetExpression}");
            lines.Add("imul ecx");
            lines.Add("mov ecx, 100");
            lines.Add("idiv ecx");
        }
        lines.Add(program.Action switch
        {
            SemanticEffectAction.AddDamageFixed or SemanticEffectAction.AddDamagePercent => $"add {targetExpression}, eax",
            SemanticEffectAction.SubtractDamageFixed or SemanticEffectAction.SubtractDamagePercent => $"sub {targetExpression}, eax",
            SemanticEffectAction.RestoreHpFixed or SemanticEffectAction.RestoreHpMaxPercent or
            SemanticEffectAction.RestoreMpFixed or SemanticEffectAction.RestoreMpMaxPercent => $"add {targetExpression}, eax",
            _ => "nop"
        });
        if (program.Action is SemanticEffectAction.AddDamageFixed or SemanticEffectAction.AddDamagePercent)
        {
            lines.Add("jo .semantic_clamp_max");
            lines.Add($"cmp {targetExpression}, 0x{maximum:X8}");
            lines.Add("jle .semantic_done");
            lines.Add(".semantic_clamp_max:");
            lines.Add($"mov {targetExpression}, 0x{maximum:X8}");
        }
        if (program.Action is SemanticEffectAction.SubtractDamageFixed or SemanticEffectAction.SubtractDamagePercent)
        {
            lines.Add($"cmp {targetExpression}, 0x{minimum:X8}");
            lines.Add("jge .semantic_done");
            lines.Add($"mov {targetExpression}, 0x{minimum:X8}");
        }
        lines.AddRange([".semantic_done:", "popad", "popfd", "{original}", "jmp {return}"]);
        return string.Join('\n', lines);
    }

    private static string BuildPhysicalRecoverySource(SemanticEffectProgram program, HookExecutionContract contract)
        => string.Join('\n',
        [
            "pushfd", "pushad", "mov ebx, dword [ebp+0x08]", "test ebx, ebx", "jz .semantic_done",
            "cmp dword [ebx+0x84], 0", "jle .semantic_done", "mov ecx, dword [ebx+0x0C]", "test ecx, ecx", "jz .semantic_done",
            $"push dword 0x{program.EffectValueMode:X8}", $"push dword 0x{program.StackingMode:X8}",
            $"push dword 0x{program.ItemEffectId:X8}", $"push dword 0x{program.PersonalEffectId:X8}",
            "call 0x004101D9", "test eax, eax", "jz .semantic_done",
            "mov edx, dword [ebx+0x0C]", "mov ecx, dword [ebx+0x08]", "test edx, edx", "jz .semantic_done",
            "test ecx, ecx", "jz .semantic_done", "movzx ecx, word [ecx+0x1F]", "mov eax, dword [edx+0x14]",
            "test eax, eax", "js .semantic_done", $"add eax, 0x{program.Value:X8}", "jc .semantic_clamp",
            "cmp eax, ecx", "jbe .semantic_write", ".semantic_clamp:", "mov eax, ecx", ".semantic_write:",
            "mov dword [edx+0x14], eax", ".semantic_done:", "popad", "popfd", $"jmp 0x{contract.ContinuationAddress:X8}"
        ]);

    private static string RequireStructuredExpression(ContextSlotContract slot)
    {
        var expression = slot.StructuredSource?.ToAssemblyExpression()
            ?? throw new InvalidOperationException($"槽位“{slot.DisplayNameZh}”缺少契约 v2 结构化来源。");
        if (expression.Contains("[ebp-0xF0]", StringComparison.OrdinalIgnoreCase) ||
            expression.Contains("[ebp-0xFC]", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("检测到未经有符号规范化的历史栈槽表达式。");
        return expression;
    }

    private static string Explain(SemanticEffectProgram program)
    {
        var action = program.Action switch
        {
            SemanticEffectAction.AddDamageFixed => $"增加 {program.Value} 点策略伤害",
            SemanticEffectAction.SubtractDamageFixed => $"减少 {program.Value} 点策略伤害，最低保留 1 点",
            SemanticEffectAction.AddDamagePercent => $"按当前伤害增加 {program.Value}% 策略伤害",
            SemanticEffectAction.SubtractDamagePercent => $"按当前伤害减少 {program.Value}% 策略伤害，最低保留 1 点",
            SemanticEffectAction.RestoreHpFixed => $"恢复 {program.Value} 点生命",
            SemanticEffectAction.RestoreMpFixed => $"恢复 {program.Value} 点策略值",
            SemanticEffectAction.RestoreHpMaxPercent => $"按最大生命恢复 {program.Value}%",
            SemanticEffectAction.RestoreMpMaxPercent => $"按最大策略值恢复 {program.Value}%",
            _ => "执行已验证动作"
        };
        return $"在执行契约“{program.HookContractId}”触发时，若人物/兵种特效 {program.PersonalEffectId:X2} 判定通过，则{action}。";
    }
}
