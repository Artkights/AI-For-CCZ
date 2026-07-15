namespace CCZModStudio.Core;

/// <summary>
/// Version-locked runtime semantics shared by authoring, analysis, and GameDebug.
/// Addresses in this registry are valid only for the registered 6.5 executable profile.
/// </summary>
public static class EngineRuntimeSemanticRegistry
{
    public const string EngineVersion = "6.5";

    public const uint TacticalUnitArrayAddress = 0x004A7B20;
    public const int TacticalUnitStride = 0x30;
    public const int TacticalUnitDataIdOffset = 0x00;
    public const int TacticalUnitDataIdWidth = 2;
    public const int TacticalUnitDisplayIdOffset = 0x04;
    public const int TacticalUnitDisplayIdWidth = 4;
    public const int TacticalUnitSideOffset = 0x05;
    public const int TacticalUnitXOffset = 0x06;
    public const int TacticalUnitYOffset = 0x07;
    public const int TacticalUnitActionOffset = 0x0D;
    public const int TacticalUnitCurrentHpOffset = 0x10;
    public const int TacticalUnitCurrentMpOffset = 0x14;
    public const int TacticalUnitCurrentValueWidth = 4;
    public const int TacticalUnitAttributesOffset = 0x18;
    public const int TacticalUnitAttributesLength = 6;
    public const int EmptyTacticalUnitDataId = 0xFFFF;

    public const uint RuntimeCharacterPointerSlotAddress = 0x004CEA00;
    public const int RuntimeCharacterStride = 0x48;
    public const int RuntimeCharacterMaximumMpOffset = 0x1F;
    public const int RuntimeCharacterMaximumMpWidth = 2;

    public const uint StrategyRecordBaseAddress = 0x004A3E77;
    public const int StrategyRecordStride = 0x61;

    public const uint PhysicalAttackContextAddress = 0x004927F0;
    public const uint StrategyContextAddress = 0x00497AF8;
    public const uint ItemContextAddress = 0x00497750;

    public const uint DataIdToRuntimeCharacterAddress = 0x004061E4;
    public const uint BattlefieldIdToTacticalUnitAddress = 0x004061F9;
    public const uint TacticalUnitToRuntimeCharacterAddress = 0x0040658F;
    public const uint StrategyIdToRecordAddress = 0x00484002;

    public const uint PhysicalRecoveryHookAddress = 0x00418335;
    public const uint LegacyPhysicalRecoveryBodyAddress = 0x004528FC;
    public const uint LegacyPhysicalRecoveryBodyEndAddress = 0x004529A6;

    public static IReadOnlyDictionary<uint, RuntimeFunctionSemantic> Functions { get; } =
        new Dictionary<uint, RuntimeFunctionSemantic>
        {
            [DataIdToRuntimeCharacterAddress] = new(
                DataIdToRuntimeCharacterAddress, "data_id_to_runtime_character", "DataId", "RuntimeCharacter*"),
            [BattlefieldIdToTacticalUnitAddress] = new(
                BattlefieldIdToTacticalUnitAddress, "battlefield_id_to_tactical_unit", "BattlefieldUnitId", "TacticalUnit*"),
            [TacticalUnitToRuntimeCharacterAddress] = new(
                TacticalUnitToRuntimeCharacterAddress, "tactical_unit_to_runtime_character", "TacticalUnit*", "RuntimeCharacter*"),
            [StrategyIdToRecordAddress] = new(
                StrategyIdToRecordAddress, "strategy_id_to_record", "StrategyId", "StrategyRecord*")
        };
}

public sealed record RuntimeFunctionSemantic(uint Address, string Name, string InputType, string OutputType);
