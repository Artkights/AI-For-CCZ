using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectTriggerDispatcherService
{
    public const string Magic = "CCZD";
    public const int EntrySize = 24;

    public byte[] BuildRegistry(EffectTriggerDispatcherDraft draft)
    {
        if (draft.Capacity is not 16 and not 32 and not 64) throw new InvalidOperationException("共享调度注册表容量只能是 16、32 或 64。");
        if (draft.Entries.Count > draft.Capacity) throw new InvalidOperationException("共享调度注册表条目超过容量。");
        var duplicateOrders = draft.Entries.GroupBy(item => item.ExecutionOrder).Where(group => group.Count() > 1).ToList();
        if (duplicateOrders.Count > 0) throw new InvalidOperationException("共享调度注册表的执行顺序不能重复。");
        var bytes = new List<byte>(16 + draft.Capacity * EntrySize);
        bytes.AddRange(Encoding.ASCII.GetBytes(Magic));
        bytes.AddRange(BitConverter.GetBytes((ushort)draft.RegistryVersion));
        bytes.AddRange(BitConverter.GetBytes((ushort)draft.Capacity));
        bytes.AddRange(BitConverter.GetBytes((ushort)draft.Entries.Count));
        bytes.AddRange([0, 0]);
        bytes.AddRange(BitConverter.GetBytes(ContractKey(draft.HookContractId)));
        foreach (var entry in draft.Entries.OrderBy(item => item.ExecutionOrder)) bytes.AddRange(BuildEntry(entry));
        while (bytes.Count < 16 + draft.Capacity * EntrySize) bytes.Add(0);
        return bytes.ToArray();
    }

    public CompiledEffectDispatcher Compile(CczProject project, EffectTriggerDispatcherDraft draft)
    {
        var result = new CompiledEffectDispatcher();
        HookExecutionContract contract;
        try { contract = new HookExecutionContractService().Read(project, draft.HookContractId); }
        catch (Exception ex) { result.WarningsZh.Add(ex.Message); return Finish(result); }
        result.Contract = contract;
        try { result.RegistryBytes = BuildRegistry(draft); }
        catch (Exception ex) { result.WarningsZh.Add(ex.Message); return Finish(result); }
        if (draft.Entries.Count == 0) result.WarningsZh.Add("共享调度器至少需要一个特效条目。");
        if (draft.Entries.Any(item => !contract.AllowedActions.Contains(item.Action, StringComparer.OrdinalIgnoreCase)))
            result.WarningsZh.Add("共享调度器包含执行契约不允许的动作。");
        var physicalRecovery = contract.ContinuationPolicy == HookContinuationPolicies.ChainExistingJumpTarget &&
                               contract.ContinuationAddress == EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyAddress &&
                               draft.Entries.All(item => item.Action == SemanticEffectAction.RestoreMpFixed);
        if (!physicalRecovery && draft.Entries.Any(item => item.Action is SemanticEffectAction.RestoreHpFixed or SemanticEffectAction.RestoreMpFixed or
                SemanticEffectAction.RestoreHpMaxPercent or SemanticEffectAction.RestoreMpMaxPercent))
            result.WarningsZh.Add("恢复动作尚未完成当前值/最大值成对槽位和运行时钳制代码，不能登记到生产调度器。");
        if (result.WarningsZh.Count > 0) return Finish(result);
        if (physicalRecovery) return CompilePhysicalRecovery(project, draft, contract, result);
        var subject = contract.Slots.FirstOrDefault(item => item.SlotId == contract.UnitPointerSlotId && item.IsStaticallyResolved);
        var target = contract.Slots.FirstOrDefault(item => item.Access == ContextSlotAccess.ReadWrite && item.IsStaticallyResolved);
        if (subject == null || target == null)
        {
            result.WarningsZh.Add("执行契约没有同时提供判定对象和可写动作槽位。");
            return Finish(result);
        }
        if (contract.ContractVersion < 2 || subject.StructuredSource == null || target.StructuredSource == null)
        {
            result.WarningsZh.Add("共享调度器只接受执行契约 v2 的结构化槽位；旧字符串表达式不能继续用于生成写入代码。");
            return Finish(result);
        }
        var subjectExpression = subject.StructuredSource.ToAssemblyExpression();
        var targetExpression = target.StructuredSource.ToAssemblyExpression();
        var minimum = target.Minimum ?? 1;
        var maximum = target.Maximum ?? int.MaxValue;
        var lines = new List<string>
        {
            "pushfd", "pushad", "mov esi, dispatcher_registry", "movzx edi, word [esi+8]", "add esi, 16",
            ".dispatcher_loop:", "test edi, edi", "jz .dispatcher_done", "cmp byte [esi], 0", "je .dispatcher_next",
            $"mov ecx, {subjectExpression}", "movzx eax, byte [esi+3]", "push eax",
            "movzx eax, byte [esi+4]", "push eax", "movzx eax, byte [esi+2]", "push eax",
            "movzx eax, byte [esi+1]", "push eax", "call 0x004101D9", "test eax, eax", "jz .dispatcher_next",
            "cmp byte [esi+6], 2", "je .dispatcher_value_ready", "mov eax, [esi+8]", ".dispatcher_value_ready:",
            "cmp byte [esi+5], 1", "je .dispatcher_add_fixed", "cmp byte [esi+5], 2", "je .dispatcher_sub_fixed",
            "cmp byte [esi+5], 3", "je .dispatcher_add_percent", "cmp byte [esi+5], 4", "je .dispatcher_sub_percent",
            "jmp .dispatcher_next",
            ".dispatcher_add_fixed:", $"add {targetExpression}, eax", "jo .dispatcher_clamp_max",
            $"cmp {targetExpression}, 0x{maximum:X8}", "jle .dispatcher_next", ".dispatcher_clamp_max:",
            $"mov {targetExpression}, 0x{maximum:X8}", "jmp .dispatcher_next",
            ".dispatcher_sub_fixed:", $"sub {targetExpression}, eax", $"cmp {targetExpression}, 0x{minimum:X8}",
            "jge .dispatcher_next", $"mov {targetExpression}, 0x{minimum:X8}", "jmp .dispatcher_next",
            ".dispatcher_add_percent:", "mov ebx, eax", $"mov eax, {targetExpression}", "imul ebx", "mov ecx, 100", "idiv ecx",
            $"add {targetExpression}, eax", "jo .dispatcher_clamp_max", $"cmp {targetExpression}, 0x{maximum:X8}",
            "jle .dispatcher_next", "jmp .dispatcher_clamp_max",
            ".dispatcher_sub_percent:", "mov ebx, eax", $"mov eax, {targetExpression}", "imul ebx", "mov ecx, 100", "idiv ecx",
            $"sub {targetExpression}, eax", $"cmp {targetExpression}, 0x{minimum:X8}", "jge .dispatcher_next",
            $"mov {targetExpression}, 0x{minimum:X8}",
            ".dispatcher_next:", $"add esi, {EntrySize}", "dec edi", "jmp .dispatcher_loop",
            ".dispatcher_done:", "popad", "popfd", "{original}", "jmp {return}",
            "dispatcher_registry:", BuildRegistryDb(result.RegistryBytes)
        };
        result.AssemblySource = string.Join('\n', lines);
        result.CanCompile = true;
        var boundsComplete = target.Minimum.HasValue && target.Maximum.HasValue;
        if (!boundsComplete) result.WarningsZh.Add($"槽位“{target.DisplayNameZh}”尚未声明经过验证的数值上下限，不能安装算术调度器。");
        result.CanPreview = boundsComplete && contract.AllowSemanticPreview && contract.VerificationStatus == HookContractVerificationStatus.DynamicVerified;
        if (!result.CanPreview) result.WarningsZh.Add("共享调度代码已生成，但执行契约尚未通过当前 SHA 动态验证，不能安装入口。");
        return Finish(result);
    }

    private static CompiledEffectDispatcher CompilePhysicalRecovery(
        CczProject project,
        EffectTriggerDispatcherDraft draft,
        HookExecutionContract contract,
        CompiledEffectDispatcher result)
    {
        var current = contract.Slots.FirstOrDefault(item => item.SlotId == "current-mp" && item.IsStaticallyResolved);
        var maximum = contract.Slots.FirstOrDefault(item => item.SlotId == current?.ClampMaximumSlotId && item.IsStaticallyResolved);
        var subject = contract.Slots.FirstOrDefault(item => item.SlotId == contract.UnitPointerSlotId && item.IsStaticallyResolved);
        if (current == null || maximum == null || subject == null)
        {
            result.WarningsZh.Add("物理恢复契约没有同时提供特效对象、当前 MP 和动态最大 MP 槽位。");
            return Finish(result);
        }

        var lines = new List<string>
        {
            "pushfd", "pushad", "mov esi, dispatcher_registry", "movzx edi, word [esi+8]", "add esi, 16",
            ".dispatcher_loop:", "test edi, edi", "jz .dispatcher_done", "cmp byte [esi], 0", "je .dispatcher_next",
            "cmp byte [esi+5], 6", "jne .dispatcher_next", "mov ebx, dword [ebp+0x08]", "test ebx, ebx", "jz .dispatcher_next",
            "cmp dword [ebx+0x84], 0", "jle .dispatcher_next", "mov ecx, dword [ebx+0x0C]", "test ecx, ecx", "jz .dispatcher_next",
            "movzx eax, byte [esi+3]", "push eax", "movzx eax, byte [esi+4]", "push eax",
            "movzx eax, byte [esi+2]", "push eax", "movzx eax, byte [esi+1]", "push eax",
            "call 0x004101D9", "test eax, eax", "jz .dispatcher_next", "mov eax, dword [esi+8]",
            "test eax, eax", "js .dispatcher_next", "mov edx, dword [ebx+0x0C]", "mov ecx, dword [ebx+0x08]",
            "test edx, edx", "jz .dispatcher_next", "test ecx, ecx", "jz .dispatcher_next", "movzx ecx, word [ecx+0x1F]",
            "add dword [edx+0x14], eax", "jc .dispatcher_clamp", "cmp dword [edx+0x14], ecx", "jle .dispatcher_next",
            ".dispatcher_clamp:", "mov dword [edx+0x14], ecx",
            ".dispatcher_next:", $"add esi, {EntrySize}", "dec edi", "jmp .dispatcher_loop",
            ".dispatcher_done:", "popad", "popfd", $"jmp 0x{contract.ContinuationAddress:X8}",
            "dispatcher_registry:", BuildRegistryDb(result.RegistryBytes)
        };
        result.AssemblySource = string.Join('\n', lines);
        result.CanCompile = true;
        var sandboxStatic = EffectSandboxService.IsSandbox(project) && contract.VerificationStatus == HookContractVerificationStatus.StaticCandidate;
        result.CanPreview = sandboxStatic || contract.AllowSemanticPreview && contract.VerificationStatus == HookContractVerificationStatus.DynamicVerified;
        if (!result.CanPreview) result.WarningsZh.Add("物理恢复调度器只允许在静态契约匹配的验证副本，或通过 V3 的正式契约中预览。");
        return Finish(result);
    }

    public EffectTriggerDispatcherDraft ReadRegistry(byte[] bytes, string dispatcherId, string contractId)
    {
        if (bytes.Length < 16 || Encoding.ASCII.GetString(bytes, 0, 4) != Magic) throw new InvalidOperationException("数据不是 CCZD 共享调度注册表。");
        var version = BitConverter.ToUInt16(bytes, 4);
        var capacity = BitConverter.ToUInt16(bytes, 6);
        var count = BitConverter.ToUInt16(bytes, 8);
        if (capacity is not 16 and not 32 and not 64 || bytes.Length < 16 + capacity * EntrySize || count > capacity)
            throw new InvalidOperationException("共享调度注册表头部无效。");
        var result = new EffectTriggerDispatcherDraft { DispatcherId = dispatcherId, HookContractId = contractId, RegistryVersion = version, Capacity = capacity };
        for (var index = 0; index < count; index++) result.Entries.Add(ReadEntry(bytes, 16 + index * EntrySize));
        return result;
    }

    public int NextCapacity(int current, int needed)
    {
        foreach (var value in new[] { 16, 32, 64 }) if (value >= current && value >= needed) return value;
        throw new InvalidOperationException("单个共享调度器最多登记 64 个特效；请拆分为独立触发契约。");
    }

    public ModularEffectManifestV2 BuildManifest(ModularCompositeEffectBlueprint blueprint, IReadOnlyList<SemanticEffectProgram> programs, IReadOnlyList<EffectTriggerDispatcherDraft> dispatchers, EffectPackage package, string beforeSha, string afterSha)
    {
        var contracts = programs.Select(item => item.HookContractId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var manifest = new ModularEffectManifestV2
        {
            ManifestId = string.IsNullOrWhiteSpace(blueprint.BlueprintId) ? package.PackageId : blueprint.BlueprintId,
            ExeSha256Before = beforeSha, ExeSha256After = afterSha, Blueprint = blueprint,
            Programs = programs.ToList(), Dispatchers = dispatchers.ToList(), Package = package
        };
        foreach (var id in contracts)
        {
            manifest.ContractVersions[id] = id.Equals("physical-after-damage-recovery-v2", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            manifest.ContractHashes[id] = package.Metadata.GetValueOrDefault("HookExecutionContractHash") ?? string.Empty;
        }
        return manifest;
    }

    private static byte[] BuildEntry(EffectDispatcherEntry entry)
    {
        if (entry.PersonalEffectId is < 0 or > 0xFF || entry.ItemEffectId is < 0 or > 0xFF) throw new InvalidOperationException("调度条目的特效号必须在 00-FF 范围内。");
        var bytes = new byte[EntrySize];
        bytes[0] = entry.Enabled ? (byte)1 : (byte)0;
        bytes[1] = (byte)entry.PersonalEffectId;
        bytes[2] = (byte)entry.ItemEffectId;
        bytes[3] = checked((byte)entry.EffectValueMode);
        bytes[4] = checked((byte)entry.StackingMode);
        bytes[5] = ActionCode(entry.Action);
        bytes[6] = ValueSourceCode(entry.ValueSource);
        BitConverter.GetBytes(entry.Value).CopyTo(bytes, 8);
        BitConverter.GetBytes(entry.ExecutionOrder).CopyTo(bytes, 12);
        BitConverter.GetBytes(StableKey(entry.EntryId)).CopyTo(bytes, 16);
        BitConverter.GetBytes((uint)0).CopyTo(bytes, 20);
        return bytes;
    }

    private static EffectDispatcherEntry ReadEntry(byte[] bytes, int offset) => new()
    {
        Enabled = bytes[offset] != 0, PersonalEffectId = bytes[offset + 1], ItemEffectId = bytes[offset + 2],
        EffectValueMode = bytes[offset + 3], StackingMode = bytes[offset + 4],
        Action = ActionName(bytes[offset + 5]), ValueSource = ValueSourceName(bytes[offset + 6]),
        Value = BitConverter.ToInt32(bytes, offset + 8), ExecutionOrder = BitConverter.ToInt32(bytes, offset + 12),
        EntryId = "entry-" + BitConverter.ToUInt32(bytes, offset + 16).ToString("X8")
    };

    private static byte ActionCode(string action) => action switch
    {
        SemanticEffectAction.AddDamageFixed => 1, SemanticEffectAction.SubtractDamageFixed => 2,
        SemanticEffectAction.AddDamagePercent => 3, SemanticEffectAction.SubtractDamagePercent => 4,
        SemanticEffectAction.RestoreHpFixed => 5, SemanticEffectAction.RestoreMpFixed => 6,
        SemanticEffectAction.RestoreHpMaxPercent => 7, SemanticEffectAction.RestoreMpMaxPercent => 8,
        _ => throw new InvalidOperationException("共享调度器不支持动作：" + action)
    };

    private static string ActionName(byte code) => code switch
    {
        1 => SemanticEffectAction.AddDamageFixed, 2 => SemanticEffectAction.SubtractDamageFixed,
        3 => SemanticEffectAction.AddDamagePercent, 4 => SemanticEffectAction.SubtractDamagePercent,
        5 => SemanticEffectAction.RestoreHpFixed, 6 => SemanticEffectAction.RestoreMpFixed,
        7 => SemanticEffectAction.RestoreHpMaxPercent, 8 => SemanticEffectAction.RestoreMpMaxPercent,
        _ => "Unknown"
    };

    private static byte ValueSourceCode(string value) => value switch { SemanticEffectValueSource.Constant => 1, SemanticEffectValueSource.CoreReturnValue => 2, SemanticEffectValueSource.ParameterBlock => 3, _ => 0 };
    private static string ValueSourceName(byte value) => value switch { 1 => SemanticEffectValueSource.Constant, 2 => SemanticEffectValueSource.CoreReturnValue, 3 => SemanticEffectValueSource.ParameterBlock, _ => "Unknown" };
    private static uint ContractKey(string value) => StableKey(value);
    private static uint StableKey(string value) => BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)), 0);
    private static string BuildRegistryDb(byte[] bytes)
        => "db " + string.Join(", ", bytes.Select(value => $"0x{value:X2}"));

    private static CompiledEffectDispatcher Finish(CompiledEffectDispatcher result)
    {
        result.SummaryZh = result.CanPreview
            ? $"共享调度器编译通过，共 {result.RegistryBytes.Length} 字节注册表。"
            : result.CanCompile ? "共享调度器结构已编译，但当前不可安装：" + string.Join("；", result.WarningsZh)
            : "共享调度器编译失败：" + string.Join("；", result.WarningsZh);
        return result;
    }
}
