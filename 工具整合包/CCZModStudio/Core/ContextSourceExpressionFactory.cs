using CCZModStudio.Models;

namespace CCZModStudio.Core;

public static class ContextSourceExpressionFactory
{
    public static ContextSourceExpression DecodeMemory(byte[] instructionBytes, uint address, string derivedType = "", string relationship = ContextRelationshipStatus.Unknown)
    {
        var instruction = new X86InstructionScanner().DecodeBlock(instructionBytes, address).FirstOrDefault()
            ?? throw new InvalidOperationException("无法解码上下文来源指令。");
        var operand = instruction.Operands.FirstOrDefault(item => item.Kind.Equals("Memory", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"指令 0x{address:X8} 不包含内存操作数。");
        return FromOperand(operand, instruction.Address, derivedType, relationship,
            [$"0x{instruction.Address:X8}: {instruction.Mnemonic} {operand.MemoryText}"]);
    }

    public static ContextSourceExpression FromOperand(
        X86OperandInfo operand,
        uint definitionAddress,
        string derivedType = "",
        string relationship = ContextRelationshipStatus.Unknown,
        IEnumerable<string>? chain = null,
        int? dereferenceCount = null)
    {
        if (!operand.Kind.Equals("Memory", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("上下文来源不是 Iced 解码得到的内存操作数。");
        return new ContextSourceExpression
        {
            BaseRegister = operand.MemoryBase,
            IndexRegister = operand.MemoryIndex,
            Scale = Math.Max(1, operand.MemoryScale),
            SignedDisplacement = operand.SignedDisplacement,
            ReadWidth = operand.MemoryWidth <= 0 ? 4 : operand.MemoryWidth,
            DereferenceCount = dereferenceCount ?? 1,
            DerivedType = derivedType,
            DefinitionInstructionAddress = definitionAddress,
            DataFlowChain = chain?.ToList() ?? [],
            StaticConfidence = 1.0,
            RelationshipStatus = relationship
        };
    }

    public static ContextSourceExpression Register(string register, uint definitionAddress, string derivedType, IEnumerable<string> chain)
        => new()
        {
            BaseRegister = register,
            ReadWidth = 4,
            DereferenceCount = 0,
            DerivedType = derivedType,
            DefinitionInstructionAddress = definitionAddress,
            DataFlowChain = chain.ToList(),
            StaticConfidence = 0.8,
            RelationshipStatus = ContextRelationshipStatus.Unknown
        };
}
