using System.Globalization;
using Iced.Intel;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class X86InstructionScanner
{
    public X86ScanResult Scan(byte[] imageBytes, uint imageBase, IReadOnlyList<ExeSectionInfo> sections)
    {
        var executableSections = sections
            .Where(section => section.IsExecutable)
            .OrderBy(section => section.VirtualAddress)
            .ToArray();
        var all = new List<X86InstructionInfo>();
        var byAddress = new Dictionary<uint, X86InstructionInfo>();
        var bySection = new Dictionary<string, List<X86InstructionInfo>>(StringComparer.Ordinal);

        foreach (var section in executableSections)
        {
            var start = checked((int)section.RawPointer);
            var length = checked((int)Math.Min(section.RawSize, Math.Max(0, imageBytes.LongLength - start)));
            if (start < 0 || length <= 0 || start >= imageBytes.Length) continue;

            var sectionBytes = imageBytes.AsSpan(start, length).ToArray();
            var sectionVa = checked(imageBase + section.VirtualAddress);
            var decoder = Decoder.Create(32, sectionBytes, sectionVa, DecoderOptions.None);
            var instructions = new List<X86InstructionInfo>();
            while (decoder.IP < sectionVa + (uint)length)
            {
                var instruction = decoder.Decode();
                if (instruction.Length == 0) break;
                var address = checked((uint)instruction.IP);
                var fileOffset = checked(start + (int)(address - sectionVa));
                var bytes = fileOffset >= 0 && fileOffset + instruction.Length <= imageBytes.Length
                    ? imageBytes.AsSpan(fileOffset, instruction.Length).ToArray()
                    : [];
                var constantOffsets = decoder.GetConstantOffsets(in instruction);
                var info = BuildInstructionInfo(instruction, bytes, fileOffset, section.Name, constantOffsets, includeUsage: false);
                instructions.Add(info);
                all.Add(info);
                byAddress[info.Address] = info;
            }

            bySection[section.Name] = instructions;
        }

        return new X86ScanResult(all, byAddress, bySection);
    }

    public IReadOnlyList<X86InstructionInfo> DecodeBlock(byte[] bytes, uint baseAddress, string sectionName = "signature")
    {
        var decoder = Decoder.Create(32, bytes, baseAddress, DecoderOptions.None);
        var instructions = new List<X86InstructionInfo>();
        while (decoder.IP < baseAddress + (uint)bytes.Length)
        {
            var instruction = decoder.Decode();
            if (instruction.Length == 0) break;
            var offset = checked((int)(instruction.IP - baseAddress));
            var raw = offset >= 0 && offset + instruction.Length <= bytes.Length
                ? bytes.AsSpan(offset, instruction.Length).ToArray()
                : [];
            var constantOffsets = decoder.GetConstantOffsets(in instruction);
            instructions.Add(BuildInstructionInfo(instruction, raw, offset, sectionName, constantOffsets, includeUsage: true));
        }

        return instructions;
    }

    public static IReadOnlyList<X86StackArgument> BackwardSliceStackArguments(
        IReadOnlyList<X86InstructionInfo> sectionInstructions,
        int callIndex,
        int maximumLookbackInstructions = 36,
        int maximumArguments = 4)
    {
        if (callIndex < 0 || callIndex >= sectionInstructions.Count) return [];

        var registerValues = new Dictionary<string, X86StackArgument>(StringComparer.OrdinalIgnoreCase);
        var stackValues = new Dictionary<int, X86StackArgument>();
        var start = Math.Max(0, callIndex - maximumLookbackInstructions);
        for (var boundary = callIndex - 1; boundary >= start; boundary--)
        {
            var instruction = sectionInstructions[boundary];
            if (instruction.IsReturn || instruction.IsIndirectJump ||
                instruction.IsDirectJump && instruction.BranchTarget != instruction.EndAddress)
            {
                start = boundary + 1;
                break;
            }
        }

        for (var index = start; index < callIndex; index++)
        {
            var instruction = sectionInstructions[index];
            if (instruction.Mnemonic.Equals("push", StringComparison.OrdinalIgnoreCase) &&
                instruction.Operands.Count > 0)
            {
                ShiftStackSlots(stackValues, 4);
                if (TryResolveOperand(instruction.Operands[0], instruction, registerValues, stackValues, out var value))
                {
                    stackValues[0] = value with
                    {
                        InstructionAddress = instruction.Address,
                        PushInstructionAddress = instruction.Address,
                        SourceDescription = "push resolved from " + value.SourceDescription
                    };
                }

                continue;
            }

            if (instruction.Mnemonic.Equals("pop", StringComparison.OrdinalIgnoreCase))
            {
                ShiftStackSlots(stackValues, -4);
                TrackSimpleRegisterDefinition(instruction, registerValues);
                continue;
            }

            if (TryGetEspAdjustment(instruction, out var adjustment))
            {
                ShiftStackSlots(stackValues, -adjustment);
                continue;
            }

            if (instruction.Mnemonic.Equals("mov", StringComparison.OrdinalIgnoreCase) &&
                instruction.Operands.Count >= 2 &&
                TryParseEspSlot(instruction.Operands[0], out var stackOffset) &&
                TryResolveOperand(instruction.Operands[1], instruction, registerValues, stackValues, out var stored))
            {
                stackValues[stackOffset] = stored with
                {
                    InstructionAddress = instruction.Address,
                    PushInstructionAddress = instruction.Address,
                    SourceKind = X86ArgumentSourceKind.StackSlotDefinition,
                    SourceDescription = "stack slot resolved from " + stored.SourceDescription
                };
                continue;
            }

            TrackSimpleRegisterDefinition(instruction, registerValues);
        }

        var arguments = new List<X86StackArgument>();
        for (var argumentIndex = maximumArguments - 1; argumentIndex >= 0; argumentIndex--)
        {
            if (stackValues.TryGetValue(argumentIndex * 4, out var value)) arguments.Add(value);
        }

        return arguments;
    }

    private static bool TryResolveOperand(
        X86OperandInfo operand,
        X86InstructionInfo instruction,
        IReadOnlyDictionary<string, X86StackArgument> registerValues,
        IReadOnlyDictionary<int, X86StackArgument> stackValues,
        out X86StackArgument value)
    {
        value = default!;
        if (operand.Kind.Equals("Immediate", StringComparison.OrdinalIgnoreCase) && operand.Immediate.HasValue)
        {
            value = CreateImmediateArgument(instruction, operand.Immediate.Value, Math.Max(1, operand.ImmediateSize), X86ArgumentSourceKind.Immediate);
            return true;
        }

        if (operand.Kind.Equals("Register", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(operand.Register) &&
            registerValues.TryGetValue(operand.Register, out var registerValue))
        {
            value = registerValue with { IsRegisterResolved = true, SourceRegister = operand.Register };
            return true;
        }

        if (TryParseEspSlot(operand, out var offset) && stackValues.TryGetValue(offset, out var stackValue))
        {
            value = stackValue with { SourceDescription = "memory " + operand.MemoryText + " from " + stackValue.SourceDescription };
            return true;
        }

        return false;
    }

    private static X86StackArgument CreateImmediateArgument(
        X86InstructionInfo instruction,
        int value,
        int byteLength,
        string sourceKind)
    {
        var operandOffset = instruction.ImmediateOffset;
        var operandAddress = operandOffset.HasValue
            ? checked(instruction.Address + (uint)operandOffset.Value)
            : (uint?)null;
        var fileOffset = operandOffset.HasValue && instruction.FileOffset >= 0
            ? checked(instruction.FileOffset + operandOffset.Value)
            : (int?)null;
        return new X86StackArgument(
            InstructionAddress: instruction.Address,
            Value: value,
            ByteLength: byteLength,
            IsRegisterResolved: false,
            SourceRegister: string.Empty,
            SourceDescription: "immediate",
            DefinitionInstructionAddress: instruction.Address,
            OperandAddress: operandAddress,
            OperandFileOffset: fileOffset,
            OperandOffset: operandOffset,
            SourceKind: sourceKind,
            IsDirectlyPatchable: operandAddress.HasValue && byteLength is 1 or 2 or 4,
            PushInstructionAddress: instruction.Address,
            DefinitionChain: [$"0x{instruction.Address:X8}: immediate {value}"]);
    }

    private static bool TryParseEspSlot(X86OperandInfo operand, out int offset)
    {
        offset = 0;
        if (!operand.Kind.Equals("Memory", StringComparison.OrdinalIgnoreCase)) return false;
        var text = operand.MemoryText.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (text == "[esp]") return true;
        if (!text.StartsWith("[esp+0x", StringComparison.Ordinal) || !text.EndsWith(']')) return false;
        return int.TryParse(text[7..^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset);
    }

    private static bool TryGetEspAdjustment(X86InstructionInfo instruction, out int adjustment)
    {
        adjustment = 0;
        if (instruction.Operands.Count < 2 ||
            !instruction.Operands[0].Kind.Equals("Register", StringComparison.OrdinalIgnoreCase) ||
            !instruction.Operands[0].Register.Equals("esp", StringComparison.OrdinalIgnoreCase) ||
            instruction.Operands[1].Immediate is not int amount)
        {
            return false;
        }

        if (instruction.Mnemonic.Equals("add", StringComparison.OrdinalIgnoreCase)) adjustment = amount;
        else if (instruction.Mnemonic.Equals("sub", StringComparison.OrdinalIgnoreCase)) adjustment = -amount;
        else return false;
        return true;
    }

    private static void ShiftStackSlots(IDictionary<int, X86StackArgument> stackValues, int delta)
    {
        if (delta == 0 || stackValues.Count == 0) return;
        var shifted = stackValues.Select(pair => new KeyValuePair<int, X86StackArgument>(pair.Key + delta, pair.Value))
            .Where(pair => pair.Key >= 0 && pair.Key <= 0x100)
            .ToList();
        stackValues.Clear();
        foreach (var pair in shifted) stackValues[pair.Key] = pair.Value;
    }

    private static void TrackSimpleRegisterDefinition(
        X86InstructionInfo instruction,
        IDictionary<string, X86StackArgument> registerValues)
    {
        if (instruction.Operands.Count == 0) return;
        var destination = instruction.Operands[0];
        if (!destination.Kind.Equals("Register", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(destination.Register))
        {
            return;
        }

        var source = instruction.Operands.Count >= 2 ? instruction.Operands[1] : null;
        if (instruction.Mnemonic.Equals("mov", StringComparison.OrdinalIgnoreCase) &&
            source != null &&
            source.Kind.Equals("Immediate", StringComparison.OrdinalIgnoreCase) &&
            source.Immediate.HasValue)
        {
            var resolved = CreateImmediateArgument(
                instruction,
                source.Immediate.Value,
                Math.Max(1, source.ImmediateSize),
                X86ArgumentSourceKind.RegisterDefinitionImmediate);
            registerValues[destination.Register] = resolved with
            {
                IsRegisterResolved = true,
                SourceRegister = destination.Register,
                SourceDescription = "mov " + destination.Register + ", immediate",
                DefinitionChain = resolved.EffectiveDefinitionChain.Append($"register {destination.Register}").ToArray()
            };
            return;
        }

        if (instruction.Mnemonic.Equals("mov", StringComparison.OrdinalIgnoreCase) &&
            source != null &&
            source.Kind.Equals("Register", StringComparison.OrdinalIgnoreCase) &&
            registerValues.TryGetValue(source.Register, out var copied))
        {
            registerValues[destination.Register] = copied with
            {
                InstructionAddress = instruction.Address,
                IsRegisterResolved = true,
                SourceRegister = destination.Register,
                SourceDescription = "mov " + destination.Register + ", " + source.Register + " from " + copied.SourceDescription,
                DefinitionChain = copied.EffectiveDefinitionChain.Append($"0x{instruction.Address:X8}: mov {destination.Register}, {source.Register}").ToArray()
            };
            return;
        }

        if (instruction.Mnemonic.Equals("xor", StringComparison.OrdinalIgnoreCase) &&
            source != null &&
            source.Kind.Equals("Register", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(destination.Register, source.Register, StringComparison.OrdinalIgnoreCase))
        {
            registerValues[destination.Register] = new X86StackArgument(
                InstructionAddress: instruction.Address,
                Value: 0,
                ByteLength: 4,
                IsRegisterResolved: true,
                SourceRegister: destination.Register,
                SourceDescription: "xor " + destination.Register + ", " + destination.Register,
                DefinitionInstructionAddress: instruction.Address,
                SourceKind: X86ArgumentSourceKind.ComputedConstant,
                IsDirectlyPatchable: false,
                PushInstructionAddress: instruction.Address,
                DefinitionChain: [$"0x{instruction.Address:X8}: xor {destination.Register}, {destination.Register}"]);
            return;
        }

        if (IsRegisterWriteMnemonic(instruction.Mnemonic))
        {
            registerValues.Remove(destination.Register);
        }
    }

    public static bool TryFindDirectCoreCallInBlock(
        X86ScanResult scan,
        uint entryAddress,
        uint coreAddress,
        out X86InstructionInfo callInstruction,
        int maximumInstructions = 32,
        int maximumBytes = 96)
    {
        callInstruction = default!;
        var queue = new Queue<(uint Entry, int Depth)>();
        var visited = new HashSet<uint>();
        queue.Enqueue((entryAddress, 0));
        while (queue.Count > 0)
        {
            var (currentEntry, depth) = queue.Dequeue();
            if (!visited.Add(currentEntry) || depth > 3 || !scan.InstructionsByAddress.TryGetValue(currentEntry, out var entry)) continue;
            if (!scan.InstructionsBySection.TryGetValue(entry.SectionName, out var sectionInstructions)) continue;
            var entryIndex = sectionInstructions.FindIndex(instruction => instruction.Address == currentEntry);
            if (entryIndex < 0) continue;
            var endAddress = checked(currentEntry + (uint)Math.Max(0, maximumBytes));
            var limit = Math.Min(sectionInstructions.Count, entryIndex + Math.Max(1, maximumInstructions));
            for (var index = entryIndex; index < limit; index++)
            {
                var instruction = sectionInstructions[index];
                if (instruction.Address >= endAddress) break;
                if (instruction.IsDirectCall && instruction.BranchTarget == coreAddress)
                {
                    callInstruction = instruction;
                    return true;
                }

                if (depth < 3 && instruction.IsDirectCall && instruction.BranchTarget.HasValue && scan.InstructionsByAddress.ContainsKey(instruction.BranchTarget.Value))
                {
                    queue.Enqueue((instruction.BranchTarget.Value, depth + 1));
                }

                if (instruction.IsDirectJump && instruction.BranchTarget.HasValue)
                {
                    if (depth < 3 && scan.InstructionsByAddress.ContainsKey(instruction.BranchTarget.Value)) queue.Enqueue((instruction.BranchTarget.Value, depth + 1));
                    break;
                }

                if (index > entryIndex && (instruction.IsReturn || instruction.IsIndirectJump)) break;
            }
        }

        return false;
    }

    private static bool IsRegisterWriteMnemonic(string mnemonic)
        => mnemonic is "mov" or "lea" or "add" or "sub" or "and" or "or" or "xor" or "inc" or "dec" or "pop";

    private static X86InstructionInfo BuildInstructionInfo(
        Instruction instruction,
        byte[] bytes,
        int fileOffset,
        string sectionName,
        ConstantOffsets constantOffsets,
        bool includeUsage)
    {
        var operands = new List<X86OperandInfo>();
        for (var index = 0; index < instruction.OpCount; index++)
        {
            operands.Add(BuildOperandInfo(instruction, index));
        }

        var immediateOffset = constantOffsets.HasImmediate ? (int?)constantOffsets.ImmediateOffset : null;
        var immediateSize = constantOffsets.HasImmediate ? constantOffsets.ImmediateSize : 0;
        var displacementOffset = constantOffsets.HasDisplacement ? (int?)constantOffsets.DisplacementOffset : null;
        var displacementSize = constantOffsets.HasDisplacement ? constantOffsets.DisplacementSize : 0;
        var usage = includeUsage
            ? BuildUsageInfo(instruction)
            : new X86InstructionUsage([], [], [], []);
        return new X86InstructionInfo(
            Address: checked((uint)instruction.IP),
            Length: instruction.Length,
            FileOffset: fileOffset,
            SectionName: sectionName,
            Mnemonic: instruction.Mnemonic.ToString().ToLowerInvariant(),
            FlowControl: instruction.FlowControl.ToString(),
            BranchTarget: GetBranchTarget(instruction),
            Bytes: bytes,
            Operands: operands,
            ImmediateOffset: immediateOffset,
            ImmediateSize: immediateSize,
            DisplacementOffset: displacementOffset,
            DisplacementSize: displacementSize,
            RegistersRead: usage.RegistersRead,
            RegistersWritten: usage.RegistersWritten,
            MemoryReads: usage.MemoryReads,
            MemoryWrites: usage.MemoryWrites);
    }

    private static X86InstructionUsage BuildUsageInfo(Instruction instruction)
    {
        var factory = new InstructionInfoFactory();
        var info = factory.GetInfo(in instruction);
        var registersRead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registersWritten = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var memoryReads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var memoryWrites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var register in info.GetUsedRegisters())
        {
            var name = register.Register.ToString().ToLowerInvariant();
            if (register.Access is OpAccess.Read or OpAccess.CondRead or OpAccess.ReadWrite or OpAccess.ReadCondWrite)
            {
                registersRead.Add(name);
            }

            if (register.Access is OpAccess.Write or OpAccess.CondWrite or OpAccess.ReadWrite or OpAccess.ReadCondWrite)
            {
                registersWritten.Add(name);
            }
        }

        foreach (var memory in info.GetUsedMemory())
        {
            var text = FormatUsedMemory(memory);
            if (memory.Access is OpAccess.Read or OpAccess.CondRead or OpAccess.ReadWrite or OpAccess.ReadCondWrite)
            {
                memoryReads.Add(text);
            }

            if (memory.Access is OpAccess.Write or OpAccess.CondWrite or OpAccess.ReadWrite or OpAccess.ReadCondWrite)
            {
                memoryWrites.Add(text);
            }
        }

        return new X86InstructionUsage(
            registersRead.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            registersWritten.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            memoryReads.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            memoryWrites.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static string FormatUsedMemory(UsedMemory memory)
    {
        var parts = new List<string>();
        if (memory.Base != Register.None) parts.Add(memory.Base.ToString().ToLowerInvariant());
        if (memory.Index != Register.None)
        {
            var index = memory.Index.ToString().ToLowerInvariant();
            parts.Add(memory.Scale == 1 ? index : index + "*" + memory.Scale.ToString(CultureInfo.InvariantCulture));
        }

        if (memory.Displacement != 0)
        {
            parts.Add("0x" + memory.Displacement.ToString("X", CultureInfo.InvariantCulture));
        }

        return "[" + string.Join("+", parts) + "]:" + memory.MemorySize;
    }

    private static X86OperandInfo BuildOperandInfo(Instruction instruction, int index)
    {
        var kind = instruction.GetOpKind(index);
        if (kind == OpKind.Register)
        {
            return new X86OperandInfo("Register", instruction.GetOpRegister(index).ToString().ToLowerInvariant(), null, 0, null, string.Empty);
        }

        if (IsImmediateKind(kind))
        {
            var value = unchecked((int)instruction.GetImmediate(index));
            return new X86OperandInfo("Immediate", string.Empty, value, GetImmediateSize(kind), null, string.Empty);
        }

        if (IsBranchKind(kind))
        {
            return new X86OperandInfo("Branch", string.Empty, null, 0, checked((uint)instruction.NearBranchTarget), string.Empty);
        }

        if (kind == OpKind.Memory)
        {
            var memory = FormatMemoryOperand(instruction);
            var displacement = unchecked((int)(uint)instruction.MemoryDisplacement64);
            var width = instruction.MemorySize.GetSize();
            return new X86OperandInfo(
                "Memory", string.Empty, null, 0, null, memory,
                instruction.MemoryBase == Register.None ? string.Empty : instruction.MemoryBase.ToString().ToLowerInvariant(),
                instruction.MemoryIndex == Register.None ? string.Empty : instruction.MemoryIndex.ToString().ToLowerInvariant(),
                instruction.MemoryIndexScale,
                displacement,
                width);
        }

        return new X86OperandInfo(kind.ToString(), string.Empty, null, 0, null, string.Empty);
    }

    private static bool IsImmediateKind(OpKind kind)
        => kind is OpKind.Immediate8 or
            OpKind.Immediate8_2nd or
            OpKind.Immediate16 or
            OpKind.Immediate32 or
            OpKind.Immediate64 or
            OpKind.Immediate8to16 or
            OpKind.Immediate8to32 or
            OpKind.Immediate8to64 or
            OpKind.Immediate32to64;

    private static int GetImmediateSize(OpKind kind)
        => kind switch
        {
            OpKind.Immediate8 or OpKind.Immediate8_2nd or OpKind.Immediate8to16 or OpKind.Immediate8to32 or OpKind.Immediate8to64 => 1,
            OpKind.Immediate16 => 2,
            OpKind.Immediate32 or OpKind.Immediate32to64 => 4,
            OpKind.Immediate64 => 8,
            _ => 0
        };

    private static bool IsBranchKind(OpKind kind)
        => kind is OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64;

    private static uint? GetBranchTarget(Instruction instruction)
    {
        for (var index = 0; index < instruction.OpCount; index++)
        {
            if (IsBranchKind(instruction.GetOpKind(index)))
            {
                return checked((uint)instruction.NearBranchTarget);
            }
        }

        return null;
    }

    private static string FormatMemoryOperand(Instruction instruction)
    {
        var parts = new List<string>();
        if (instruction.MemoryBase != Register.None) parts.Add(instruction.MemoryBase.ToString().ToLowerInvariant());
        if (instruction.MemoryIndex != Register.None)
        {
            var index = instruction.MemoryIndex.ToString().ToLowerInvariant();
            parts.Add(instruction.MemoryIndexScale == 1 ? index : index + "*" + instruction.MemoryIndexScale.ToString(CultureInfo.InvariantCulture));
        }

        var displacement = unchecked((int)(uint)instruction.MemoryDisplacement64);
        if (displacement > 0)
        {
            parts.Add("0x" + displacement.ToString("X", CultureInfo.InvariantCulture));
        }
        var body = string.Join("+", parts);
        if (displacement < 0) body += "-0x" + (-displacement).ToString("X", CultureInfo.InvariantCulture);
        return "[" + body + "]";
    }
}

public sealed record X86ScanResult(
    IReadOnlyList<X86InstructionInfo> Instructions,
    IReadOnlyDictionary<uint, X86InstructionInfo> InstructionsByAddress,
    IReadOnlyDictionary<string, List<X86InstructionInfo>> InstructionsBySection);

public sealed record X86InstructionInfo(
    uint Address,
    int Length,
    int FileOffset,
    string SectionName,
    string Mnemonic,
    string FlowControl,
    uint? BranchTarget,
    byte[] Bytes,
    IReadOnlyList<X86OperandInfo> Operands,
    int? ImmediateOffset,
    int ImmediateSize,
    int? DisplacementOffset,
    int DisplacementSize,
    IReadOnlyList<string> RegistersRead,
    IReadOnlyList<string> RegistersWritten,
    IReadOnlyList<string> MemoryReads,
    IReadOnlyList<string> MemoryWrites)
{
    public uint EndAddress => checked(Address + (uint)Length);
    public bool IsDirectCall => FlowControl.Equals("Call", StringComparison.OrdinalIgnoreCase) && BranchTarget.HasValue;
    public bool IsIndirectCall => FlowControl.Equals("IndirectCall", StringComparison.OrdinalIgnoreCase);
    public bool IsDirectJump => FlowControl.Equals("UnconditionalBranch", StringComparison.OrdinalIgnoreCase) && BranchTarget.HasValue;
    public bool IsIndirectJump => FlowControl.Equals("IndirectBranch", StringComparison.OrdinalIgnoreCase);
    public bool IsConditionalBranch => FlowControl.Equals("ConditionalBranch", StringComparison.OrdinalIgnoreCase) && BranchTarget.HasValue;
    public bool IsReturn => FlowControl.Equals("Return", StringComparison.OrdinalIgnoreCase);
}

public sealed record X86OperandInfo(
    string Kind,
    string Register,
    int? Immediate,
    int ImmediateSize,
    uint? BranchTarget,
    string MemoryText,
    string MemoryBase = "",
    string MemoryIndex = "",
    int MemoryScale = 1,
    long SignedDisplacement = 0,
    int MemoryWidth = 0);

public sealed record X86StackArgument(
    uint InstructionAddress,
    int Value,
    int ByteLength,
    bool IsRegisterResolved,
    string SourceRegister,
    string SourceDescription,
    uint? DefinitionInstructionAddress = null,
    uint? OperandAddress = null,
    int? OperandFileOffset = null,
    int? OperandOffset = null,
    string SourceKind = X86ArgumentSourceKind.Unresolved,
    bool IsDirectlyPatchable = false,
    uint? PushInstructionAddress = null,
    IReadOnlyList<string>? DefinitionChain = null)
{
    public IReadOnlyList<string> EffectiveDefinitionChain => DefinitionChain ?? [];
}

public static class X86ArgumentSourceKind
{
    public const string Immediate = "Immediate";
    public const string RegisterDefinitionImmediate = "RegisterDefinitionImmediate";
    public const string StackSlotDefinition = "StackSlotDefinition";
    public const string MemoryBackedSource = "MemoryBackedSource";
    public const string ComputedConstant = "ComputedConstant";
    public const string Unresolved = "Unresolved";
}

public sealed record X86InstructionUsage(
    IReadOnlyList<string> RegistersRead,
    IReadOnlyList<string> RegistersWritten,
    IReadOnlyList<string> MemoryReads,
    IReadOnlyList<string> MemoryWrites);
