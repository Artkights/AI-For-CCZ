"""Engine identification and capability data for the CaoCaoZhuan modifier.

Addresses in this module are reference virtual addresses (VA) for the original
0x00400000 image.  Runtime users must resolve them through MemorySession.
"""

from __future__ import annotations

import hashlib
import os
import struct
from dataclasses import dataclass, field, replace
from enum import IntEnum
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

from capability_evidence import (CapabilityState, EvidenceError,
                                 EvidenceRepository, FeatureCapability,
                                 recipe_id)
from engine_layout import (AddressResolverKind, FeatureAccess, FieldSpec,
                           ResolvedFeature, ResolvedTableDescriptor,
                           SemanticAnchorRole, TableLayoutSpec)
from semantic_resolver import (SemanticAnchorPattern, SemanticTableResolver,
                               verify_runtime_descriptor)


REFERENCE_IMAGE_BASE = 0x00400000
IMAGE_FILE_MACHINE_I386 = 0x014C
PE32_MAGIC = 0x010B
ITEM_ROW_SIZE_66 = 0x19
ITEM_LOW_OFFSET_66 = 33164
ITEM_LOW_COUNT_66 = 104
ITEM_HIGH_OFFSET_66 = 0
ITEM_HIGH_COUNT_66 = 151
ITEM_RECORD_COUNT_66 = ITEM_LOW_COUNT_66 + ITEM_HIGH_COUNT_66
EQUIPMENT_COUNT_66 = 160
CONSUMABLE_ID_MIN_66 = 160
CONSUMABLE_ID_MAX_66 = 199
CONSUMABLE_STORAGE_COUNT_66 = 352
CONSUMABLE_ID_MIN_LEGACY_EXPANDED = 150
CONSUMABLE_ID_MAX_LEGACY_EXPANDED = 254
CONSUMABLE_STORAGE_COUNT_LEGACY_EXPANDED = 105
EFFECT_ROW_SIZE_66 = 0x10
EFFECT_CHARACTER_SLOTS_66 = 4
EFFECT_JOB_SLOTS_66 = 2
EFFECT_CHARACTER_COUNT_66 = 1024
EFFECT_EMPTY_CHARACTER_66 = EFFECT_CHARACTER_COUNT_66
EFFECT_JOB_COUNT_66 = 80
ITEM_CODEC_65 = "item-65-17-v1"
ITEM_CODEC_66 = "item-66-15-v1"
EFFECT_NAME_POOL_OFFSET_65_66 = 0x9E800
EFFECT_NAME_RUNTIME_ADDRESS_65_66 = 0x004CC600
EFFECT_NAME_RECORD_COUNT_65_66 = 255
EFFECT_NAME_SLOT_SIZE_65_66 = 16


class ProfileError(RuntimeError):
    pass


class EngineVersion(IntEnum):
    """Stable identifiers used by the UI and composite profile resolver."""

    V64 = 0
    V63_MP = 1
    V63 = 2
    V62 = 3
    V61 = 4
    V65 = 5
    V66 = 6


def engine_version_from_resource(version_part: int) -> EngineVersion:
    return {
        6: EngineVersion.V66,
        5: EngineVersion.V65,
        4: EngineVersion.V64,
        3: EngineVersion.V63,
        2: EngineVersion.V62,
    }.get(int(version_part), EngineVersion.V61)


@dataclass(frozen=True)
class ConsumableMappingSpec:
    key: str
    base: int
    id_min: int
    storage_count: int
    ui_item_ids: Tuple[int, ...]
    equipment_count: int
    width: int = 1
    stride: int = 1
    base_dereference: bool = False
    anchors: Dict[str, int] = field(default_factory=dict)
    patterns: Dict[str, str] = field(default_factory=dict)
    structure_fingerprint: str = ""

    def __post_init__(self) -> None:
        if self.base <= 0 or self.storage_count <= 0:
            raise ValueError("消耗品映射缺少有效基址或容量")
        if self.width not in (1, 2, 4) or self.stride < self.width:
            raise ValueError("消耗品映射宽度或步长无效")
        if not self.ui_item_ids:
            raise ValueError("消耗品映射没有 UI 项目")
        storage_max = self.id_min + self.storage_count - 1
        if any(item < self.id_min or item > storage_max
               for item in self.ui_item_ids):
            raise ValueError("消耗品 UI ID 超出物理存储范围")

    @property
    def id_max(self) -> int:
        return max(self.ui_item_ids)

    @property
    def storage_id_max(self) -> int:
        return self.id_min + self.storage_count - 1

    def as_mapping(self) -> Dict[str, object]:
        return {
            "recipeKey": self.key,
            "idMin": self.id_min,
            "idMax": self.id_max,
            "storageIdMax": self.storage_id_max,
            "storageCount": self.storage_count,
            "stride": self.stride,
            "width": self.width,
            "baseDereference": self.base_dereference,
            "itemIds": list(self.ui_item_ids),
            "equipmentCount": self.equipment_count,
            "locations": dict(self.anchors),
            "patterns": dict(self.patterns),
            "structureFingerprint": self.structure_fingerprint,
        }


@dataclass(frozen=True)
class PatchSite:
    address: int
    original: bytes
    patched: bytes

    def __post_init__(self) -> None:
        if not self.original or len(self.original) != len(self.patched):
            raise ValueError("补丁原字节与目标字节必须等长且不能为空")


@dataclass(frozen=True)
class PatchSpec:
    key: str
    label: str
    sites: Tuple[PatchSite, ...]
    signature_patterns: Tuple[str, ...] = ()
    normalized_fingerprint: str = ""
    instruction_lengths: Tuple[int, ...] = ()
    rollback_rule: str = "owned-bytes-only"

    def recipe(self) -> Dict[str, object]:
        return {
            "kind": "patch",
            "signaturePatterns": list(self.signature_patterns),
            "normalizedFingerprint": self.normalized_fingerprint,
            "instructionLengths": list(self.instruction_lengths),
            "rollbackRule": self.rollback_rule,
            "sites": [{
                "originalHex": item.original.hex().upper(),
                "patchedHex": item.patched.hex().upper(),
            } for item in self.sites],
        }


@dataclass(frozen=True)
class DynamicCallSite:
    address: int
    original: bytes
    instruction_length: int
    branch_opcode: int
    stub_offset: int = 0
    operand_only: bool = False

    def __post_init__(self) -> None:
        if self.instruction_length < 5 or len(self.original) != self.instruction_length:
            raise ValueError("动态调用点必须覆盖至少 5 个完整指令字节")
        if self.branch_opcode not in (0xE8, 0xE9):
            raise ValueError("动态调用点只支持 CALL/JMP rel32")


@dataclass(frozen=True)
class StubRelocation:
    offset: int
    kind: str
    value: int
    addend: int = 0


@dataclass(frozen=True)
class DynamicPatchSpec:
    key: str
    label: str
    call_sites: Tuple[DynamicCallSite, ...]
    stub_template: bytes
    relocations: Tuple[StubRelocation, ...] = ()
    signature_patterns: Tuple[str, ...] = ()
    normalized_fingerprint: str = ""
    rollback_rule: str = "owned-branches-and-no-eip-in-stub"

    def recipe(self) -> Dict[str, object]:
        return {
            "kind": "dynamic-stub",
            "signaturePatterns": list(self.signature_patterns),
            "normalizedFingerprint": self.normalized_fingerprint,
            "rollbackRule": self.rollback_rule,
            "stubTemplateHex": self.stub_template.hex().upper(),
            "relocations": [{
                "offset": item.offset,
                "kind": item.kind,
                "value": item.value,
                "addend": item.addend,
            } for item in self.relocations],
            "callSites": [{
                "originalHex": item.original.hex().upper(),
                "instructionLength": item.instruction_length,
                "branchOpcode": item.branch_opcode,
                "stubOffset": item.stub_offset,
                "operandOnly": item.operand_only,
            } for item in self.call_sites],
        }


AUTO_RETURN_GATE_PATTERN_66 = (
    "38 02 75 1C E8 ?? ?? ?? ?? 85 C0 74 13 8B 4D FC "
    "E8 ?? ?? ?? ?? B8 01 00 00 00")
AUTO_RETURN_STATE_PATTERN_66 = (
    "E8 ?? ?? ?? ?? 84 C0 74 02 B0 01 34 01 8B 4D DC "
    "0F B6 51 04 81 C2 ?? ?? ?? ?? 80 3A FF 90 74 02 88 02")
AUTO_RETURN_EFFECT_PATTERN_66 = (
    "55 8B EC 6A 01 6A 01 6A 5A 6A 38 E8 ?? ?? ?? ?? 5D C3")


def _relative_call_target(image: "PEImage", address: int) -> int:
    instruction = image.read_va(address, 5)
    if instruction[0] != 0xE8:
        raise ProfileError("0x%08X 不是 CALL rel32" % address)
    return address + 5 + struct.unpack("<i", instruction[1:5])[0]


def locate_auto_return_patch_66(image: "PEImage") -> Tuple[
        Optional[PatchSpec], Dict[str, object], str]:
    """Locate the native second-action gates used by 6.6 auto return."""
    patterns = {
        "gate": AUTO_RETURN_GATE_PATTERN_66,
        "state": AUTO_RETURN_STATE_PATTERN_66,
        "effect": AUTO_RETURN_EFFECT_PATTERN_66,
    }
    hits: Dict[str, int] = {}
    for name, pattern in patterns.items():
        matches = image.find_signature(MaskedSignature.parse(pattern))
        if len(matches) != 1:
            return None, {}, "自动回归%s签名匹配数为%d" % (name, len(matches))
        hits[name] = matches[0]

    gate_call = hits["gate"] + 4
    state_call = hits["state"]
    try:
        targets = (_relative_call_target(image, gate_call),
                   _relative_call_target(image, state_call))
    except ProfileError as exc:
        return None, {}, "自动回归调用关系解析失败: %s" % exc
    if targets[0] != hits["effect"] or targets[1] != hits["effect"]:
        return None, {}, "自动回归两个调用点未共同指向原生二次行动函数"

    site_data = (
        (hits["gate"] + 0x0B, b"\x74\x13"),
        (hits["state"] + 0x07, b"\x74\x02"),
        (hits["state"] + 0x1E, b"\x74\x02"),
    )
    try:
        for address, original in site_data:
            if image.read_va(address, len(original)) != original:
                return None, {}, "自动回归站点0x%08X原字节不匹配" % address
    except ProfileError as exc:
        return None, {}, "自动回归补丁点读取失败: %s" % exc

    fingerprint = hashlib.sha256(
        b"native-second-action-unrestricted-v1\0" +
        image.read_va(hits["effect"], 18) +
        b"".join(original for _address, original in site_data)
    ).hexdigest().upper()
    spec = PatchSpec(
        "control_auto_return", "自动回归",
        tuple(PatchSite(address, original, b"\x90\x90")
              for address, original in site_data),
        tuple(patterns.values()), fingerprint, (2, 2, 2))
    contract = {
        "mode": "native-second-action-unrestricted",
        "callSites": [gate_call, state_call],
        "nativeEffect": hits["effect"],
        "patchSites": [address for address, _original in site_data],
        "originalBytes": [original for _address, original in site_data],
        "patchedBytes": [b"\x90\x90"] * 3,
        "signatureHits": hits,
        "fingerprint": fingerprint,
    }
    return spec, contract, ""


def locate_consumable_mapping_66(
        image: "PEImage") -> Tuple[Optional[int], Dict[str, object], str]:
    """Locate the shared 6.6 item-count array from three independent users."""
    patterns = {
        "update": (
            "55 8B EC 2D A0 00 00 00 05 ?? ?? ?? ?? 84 C9 74 08 "
            "38 08 72 0B FE 08 EB 07 80 38 FF 74 02 FE 00 5D C3", 9),
        "read": (
            "55 8B EC 0F B7 C0 2D A0 00 00 00 0F B6 80 ?? ?? ?? ?? "
            "5D C3", 14),
        "write": (
            "55 8B EC 2D A0 00 00 00 8A 4D 08 88 88 ?? ?? ?? ?? 5D "
            "C2 04 00", 13),
        "clear": (
            "33 C0 99 66 3D 60 01 73 09 88 90 ?? ?? ?? ?? 40 EB F1", 11),
    }
    locations: Dict[str, int] = {}
    bases: Dict[str, int] = {}
    for name, (pattern, operand_offset) in patterns.items():
        hits = image.find_signature(MaskedSignature.parse(pattern))
        if len(hits) != 1:
            return None, {}, "6.6道具数量%s签名匹配数为%d" % (name, len(hits))
        locations[name] = hits[0]
        bases[name] = struct.unpack(
            "<I", image.read_va(hits[0] + operand_offset, 4))[0]
    if len(set(bases.values())) != 1:
        return None, {}, "6.6道具数量读写函数未指向同一基址"
    base = next(iter(bases.values()))
    mapping: Dict[str, object] = {
        "idMin": CONSUMABLE_ID_MIN_66,
        "idMax": CONSUMABLE_ID_MAX_66,
        "storageIdMax": CONSUMABLE_ID_MIN_66 + CONSUMABLE_STORAGE_COUNT_66 - 1,
        "storageCount": CONSUMABLE_STORAGE_COUNT_66,
        "stride": 1,
        "width": 1,
        "baseDereference": False,
        "itemIds": list(range(CONSUMABLE_ID_MIN_66,
                              CONSUMABLE_ID_MAX_66 + 1)),
        "locations": locations,
        "patterns": {name: value[0] for name, value in patterns.items()},
    }
    return base, mapping, ""


def _consumable_fingerprint(patterns: Dict[str, str], base: int,
                            id_min: int, storage_count: int) -> str:
    material = "|".join(
        ["%s:%s" % item for item in sorted(patterns.items())] +
        ["%08X" % base, str(id_min), str(storage_count)])
    return hashlib.sha256(material.encode("ascii")).hexdigest().upper()


def locate_consumable_mapping_legacy_expanded(
        image: "PEImage") -> Tuple[Optional[ConsumableMappingSpec], str]:
    """Locate the 150..254 byte array shared by verified 6.4/6.5 engines.

    The same structure is also used by the Houhei hybrid engine.  Four
    independent users must agree on the base and the 105-entry range, so the
    PL variant with removed ``itemId-150`` operations cannot match.
    """
    patterns = {
        "update": (
            "55 8B EC 0F B6 C9 80 E9 96 81 C1 ?? ?? ?? ?? 84 C0 74 04 "
            "FE 09 EB 07 80 39 FF 74 02 FE 01 5D C3", 11),
        "read": (
            "55 8B EC 0F B6 D2 80 EA 96 B9 ?? ?? ?? ?? 03 CA 0F B6 01 "
            "5D C3", 10),
        "write": (
            "55 8B EC 8B 45 08 2C 96 8A 4D 0C 88 88 ?? ?? ?? ?? 5D "
            "C2 08 00", 13),
        "clear": (
            "3C 69 73 ?? C6 80 ?? ?? ?? ?? 00", 6),
    }
    locations: Dict[str, int] = {}
    bases: Dict[str, int] = {}
    text_patterns: Dict[str, str] = {}
    for name, (pattern, operand_offset) in patterns.items():
        hits = image.find_signature(MaskedSignature.parse(pattern))
        if len(hits) != 1:
            return None, "105项消耗品%s签名匹配数为%d" % (name, len(hits))
        locations[name] = hits[0]
        text_patterns[name] = pattern
        bases[name] = struct.unpack(
            "<I", image.read_va(hits[0] + operand_offset, 4))[0]
    if len(set(bases.values())) != 1:
        return None, "105项消耗品读写函数未指向同一基址"
    base = next(iter(bases.values()))
    spec = ConsumableMappingSpec(
        "legacy-expanded-105-v1", base,
        CONSUMABLE_ID_MIN_LEGACY_EXPANDED,
        CONSUMABLE_STORAGE_COUNT_LEGACY_EXPANDED,
        tuple(range(CONSUMABLE_ID_MIN_LEGACY_EXPANDED,
                    CONSUMABLE_ID_MAX_LEGACY_EXPANDED + 1)),
        CONSUMABLE_ID_MIN_LEGACY_EXPANDED,
        anchors=locations, patterns=text_patterns,
        structure_fingerprint=_consumable_fingerprint(
            text_patterns, base, CONSUMABLE_ID_MIN_LEGACY_EXPANDED,
            CONSUMABLE_STORAGE_COUNT_LEGACY_EXPANDED))
    return spec, ""


def locate_consumable_mapping_composite(
        image: "PEImage", engine_version: int) -> Tuple[
            Optional[ConsumableMappingSpec], str]:
    version_value = EngineVersion(int(engine_version))
    if version_value in (EngineVersion.V64, EngineVersion.V65):
        return locate_consumable_mapping_legacy_expanded(image)
    if version_value is not EngineVersion.V66:
        return None, "当前旧版画像不使用扩展消耗品语义解析器"

    standard_base, standard_mapping, standard_reason = (
        locate_consumable_mapping_66(image))
    legacy_spec, legacy_reason = locate_consumable_mapping_legacy_expanded(image)
    candidates: List[ConsumableMappingSpec] = []
    if standard_base is not None:
        patterns = dict(standard_mapping.get("patterns", {}))
        candidates.append(ConsumableMappingSpec(
            "standard-66-40-v1", standard_base,
            CONSUMABLE_ID_MIN_66, CONSUMABLE_STORAGE_COUNT_66,
            tuple(range(CONSUMABLE_ID_MIN_66, CONSUMABLE_ID_MAX_66 + 1)),
            EQUIPMENT_COUNT_66,
            anchors=dict(standard_mapping.get("locations", {})),
            patterns=patterns,
            structure_fingerprint=_consumable_fingerprint(
                patterns, standard_base, CONSUMABLE_ID_MIN_66,
                CONSUMABLE_STORAGE_COUNT_66)))
    if legacy_spec is not None:
        candidates.append(legacy_spec)
    if len(candidates) == 1:
        return candidates[0], ""
    if len(candidates) > 1:
        return None, "标准40项与旧式105项消耗品配方同时匹配，拒绝猜测"
    return None, "标准40项: %s；旧式105项: %s" % (
        standard_reason, legacy_reason)


ITEM_RUNTIME_TABLE_PATTERN = (
    "55 8B EC 6B C9 19 81 C1 ?? ?? ?? ?? 5D C3")


def locate_item_row_codec(
        image: "PEImage") -> Tuple[Optional[str], Optional[int], Dict[str, int], str]:
    """Resolve the runtime 25-byte item table and its proven field family."""
    hits = image.find_signature(MaskedSignature.parse(
        ITEM_RUNTIME_TABLE_PATTERN))
    if len(hits) != 1:
        return None, None, {}, "物品25字节索引函数签名匹配数为%d" % len(hits)
    base = struct.unpack("<I", image.read_va(hits[0] + 8, 4))[0]
    codec_by_base = {
        0x004A1140: ITEM_CODEC_65,
        0x0049F820: ITEM_CODEC_66,
    }
    codec = codec_by_base.get(base)
    if codec is None:
        return None, base, {"lookup": hits[0]}, (
            "物品索引函数提取到未知表基址0x%08X，字段顺序尚未证明" % base)
    return codec, base, {"lookup": hits[0]}, ""


@dataclass(frozen=True)
class RemoteCallSpec:
    key: str
    label: str
    address: int
    signature: bytes
    abi_verified: bool = True
    signature_pattern: str = ""
    normalized_fingerprint: str = ""
    calling_convention: str = "unknown"
    ecx_source: str = ""
    arguments: Tuple[str, ...] = ()
    callee_cleanup: int = 0
    preconditions: Tuple[str, ...] = ()
    postconditions: Tuple[str, ...] = ()
    context_reference: int = 0
    wrapper_kind: str = ""

    def recipe(self) -> Dict[str, object]:
        return {
            "kind": "remote-call",
            "signaturePattern": self.signature_pattern,
            "normalizedFingerprint": self.normalized_fingerprint,
            "callingConvention": self.calling_convention,
            "ecxSource": self.ecx_source,
            "arguments": list(self.arguments),
            "calleeCleanup": self.callee_cleanup,
            "preconditions": list(self.preconditions),
            "postconditions": list(self.postconditions),
            "contextReference": self.context_reference,
            "wrapperKind": self.wrapper_kind,
        }


@dataclass(frozen=True)
class ItemRow66:
    name: str
    icon: int
    effect_or_type: int
    equipment_effect_or_category: int
    price: int
    ability: int
    effect_value: int
    growth: int
    catalog_flag: int

    @property
    def is_treasure(self) -> bool:
        return self.catalog_flag == 1


@dataclass(frozen=True)
class EffectAssignmentSlot66:
    target_id: int
    effect_value: int

    def __post_init__(self) -> None:
        if not 0 <= self.target_id <= 0xFFFF:
            raise ValueError("6.6特效目标 ID 超出 UInt16 范围")
        if not 0 <= self.effect_value <= 0xFF:
            raise ValueError("6.6特效值超出 UInt8 范围")


@dataclass(frozen=True)
class EffectAssignmentRow66:
    characters: Tuple[EffectAssignmentSlot66, ...]
    jobs: Tuple[EffectAssignmentSlot66, ...]

    def __post_init__(self) -> None:
        if len(self.characters) != EFFECT_CHARACTER_SLOTS_66:
            raise ValueError("6.6特效行必须包含 4 个角色槽")
        if len(self.jobs) != EFFECT_JOB_SLOTS_66:
            raise ValueError("6.6特效行必须包含 2 个兵种槽")
        if any(item.target_id > 0xFF for item in self.jobs):
            raise ValueError("6.6兵种 ID 超出 UInt8 范围")


@dataclass(frozen=True)
class EffectAssignmentRow8:
    characters: Tuple[int, int, int]
    job_id: int
    effect_value: int

    def __post_init__(self) -> None:
        if any(not 0 <= value <= 0xFFFF for value in self.characters):
            raise ValueError("旧式特效角色 ID 超出 UInt16 范围")
        if not 0 <= self.job_id <= 0xFF or not 0 <= self.effect_value <= 0xFF:
            raise ValueError("旧式特效兵种或特效值超出 UInt8 范围")


def parse_effect_assignment_row_66(row: bytes) -> EffectAssignmentRow66:
    if len(row) != EFFECT_ROW_SIZE_66:
        raise ValueError("6.6特效分配行必须正好是 0x10 字节")
    characters = tuple(
        EffectAssignmentSlot66(
            struct.unpack_from("<H", row, index * 3)[0],
            row[index * 3 + 2])
        for index in range(EFFECT_CHARACTER_SLOTS_66))
    jobs = tuple(
        EffectAssignmentSlot66(row[0x0C + index * 2],
                               row[0x0D + index * 2])
        for index in range(EFFECT_JOB_SLOTS_66))
    return EffectAssignmentRow66(characters, jobs)


def encode_effect_assignment_row_66(row: EffectAssignmentRow66) -> bytes:
    result = bytearray()
    for item in row.characters:
        result.extend(struct.pack("<HB", item.target_id, item.effect_value))
    for item in row.jobs:
        result.extend(struct.pack("<BB", item.target_id, item.effect_value))
    if len(result) != EFFECT_ROW_SIZE_66:
        raise ValueError("6.6特效分配行编码长度错误")
    return bytes(result)


def parse_effect_assignment_row_8(row: bytes) -> EffectAssignmentRow8:
    if len(row) != 8:
        raise ValueError("旧式特效分配行必须正好是 8 字节")
    return EffectAssignmentRow8(
        struct.unpack_from("<HHH", row, 0), row[6], row[7])


def encode_effect_assignment_row_8(row: EffectAssignmentRow8) -> bytes:
    return struct.pack("<HHHBB", *(row.characters +
                                    (row.job_id, row.effect_value)))


def detect_effect_empty_characters_66(table: bytes) -> Tuple[int, ...]:
    if len(table) % EFFECT_ROW_SIZE_66:
        raise ValueError("6.6特效分配表长度不是 0x10 的整数倍")
    sentinel_counts = [dict() for _ in range(EFFECT_CHARACTER_SLOTS_66)]
    for offset in range(0, len(table), EFFECT_ROW_SIZE_66):
        row = parse_effect_assignment_row_66(
            table[offset:offset + EFFECT_ROW_SIZE_66])
        for index, item in enumerate(row.characters):
            if item.target_id < EFFECT_CHARACTER_COUNT_66:
                continue
            counts = sentinel_counts[index]
            counts[item.target_id] = counts.get(item.target_id, 0) + 1
    result = []
    for counts in sentinel_counts:
        if not counts:
            result.append(EFFECT_EMPTY_CHARACTER_66)
            continue
        result.append(max(counts, key=lambda value: (counts[value], -value)))
    return tuple(result)


def detect_effect_empty_character_66(table: bytes) -> int:
    values = detect_effect_empty_characters_66(table)
    return max(set(values), key=lambda value: (values.count(value), -value))


EFFECT_LAYOUT_8_WRITE_PATTERN = (
    "0F B6 45 F8 3C FE 77 24 6B C8 08 03 0D ?? ?? ?? ?? "
    "8A 45 F9 3C 03 77 14 6B D0 02 03 CA 66 8B 55 FA "
    "3C 03 74 05 66 89 11 EB 02 88 11")
EFFECT_LAYOUT_8_QUERY_PATTERN = (
    "55 8B EC C1 65 08 03 03 55 08 52 6A 00 EB 03 FF 45 F8 "
    "8B 55 F8 83 FA 03 73 13 D1 E2 03 55 FC 66 3B 02 75 EB "
    "8B 55 FC 0F B6 52 07")
EFFECT_LAYOUT_16_QUERY_PATTERN = (
    "55 8B EC 8B 4D 08 C1 E1 04 03 D1 52 6A 00 6A 00 "
    "8B 55 F8 83 FA 04 73 16 6B D2 03 03 55 FC 66 3B 02 74 05 "
    "FF 45 F8 EB E8 0F B6 52 02 EB 02 33 D2 52 50 FF 75 08 "
    "E8 ?? ?? ?? ?? 8B 55 F0 8B 4D 0C E8 ?? ?? ?? ?? 89 45 F0 "
    "8B 4D 10 E8 ?? ?? ?? ?? 50 8B 45 F4 3C 02 73 25 "
    "D1 E0 83 C0 0C")
EFFECT_LAYOUT_16_ABSOLUTE_COUNTER_QUERY_PATTERN = (
    "55 8B EC 8B 4D 08 C1 E1 04 03 D1 52 6A 00 6A 00 "
    "50 FF 75 08 50 FF 15 ?? ?? ?? ?? 83 C4 08 59 0F B6 C0 50 "
    "90 90 90 90 90 90 90 90 90 90 90 90 90 90 "
    "51 FF 75 08 E8 ?? ?? ?? ?? "
    "8B 55 F0 8B 4D 0C E8 ?? ?? ?? ?? 89 45 F0 "
    "8B 4D 10 E8 ?? ?? ?? ?? 50 8B 45 F4 3C 02 73 25 "
    "D1 E0 83 C0 0C 03 45 FC")
EFFECT_LAYOUT_16_INITIALIZER_PATTERN = (
    "50 83 EC 08 54 68 ?? ?? ?? ?? 6A 04 6A 00 FF 75 E8 6A FE "
    "E8 ?? ?? ?? ?? 54 68 ?? ?? ?? ?? 6A 02 6A 01 FF 75 E8 6A FE")
EFFECT_LAYOUT_8_INITIALIZER_PATTERN = (
    "50 83 EC 08 68 FE 00 00 00 68 ?? ?? ?? ?? 6A 03 6A 00 "
    "FF 75 E8 8D 4D E0 51 E8 ?? ?? ?? ?? 68 FE 00 00 00 68 "
    "?? ?? ?? ?? 6A 01 6A 01")
PERSON_INITIALIZER_PATTERN_66 = (
    "68 00 04 00 00 6A 48 FF 35 ?? ?? ?? ?? E8 ?? ?? ?? ?? 5D C3")
PERSON_WRITER_PATTERN_66 = (
    "8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 4D F4 88 41 01 "
    "8B 55 F8 8A 42")


def effect_layout_16(pointer_reference: int, record_count: int = 255,
                     fingerprint: str = "") -> TableLayoutSpec:
    fields = []
    for index in range(4):
        fields.extend((
            FieldSpec("character_%d" % (index + 1), index * 3, 2,
                      minimum=0, maximum=0xFFFF,
                      preserve_opaque_empty=True),
            FieldSpec("character_value_%d" % (index + 1), index * 3 + 2, 1,
                      minimum=0, maximum=0xFF),
        ))
    for index in range(2):
        fields.extend((
            FieldSpec("job_%d" % (index + 1), 0x0C + index * 2, 1,
                      minimum=0, maximum=0xFF,
                      preserve_opaque_empty=True),
            FieldSpec("job_value_%d" % (index + 1), 0x0D + index * 2, 1,
                      minimum=0, maximum=0xFF),
        ))
    return TableLayoutSpec(
        "effect", AddressResolverKind.POINTER_RVA, pointer_reference,
        record_count, 16, tuple(fields), codec="effect-4+2-v1",
        structure_fingerprint=fingerprint, semantics_proven=True,
        metadata={"characterSlots": 4, "jobSlots": 2,
                  "valuePerSlot": True, "personDomain": (0, 1023),
                  "jobDomain": (0, 79), "canonicalCharacterEmpty": 1024,
                  "canonicalJobEmpty": 255})


def effect_layout_8(pointer_reference: int, record_count: int = 255,
                    fingerprint: str = "") -> TableLayoutSpec:
    fields = tuple(
        [FieldSpec("character_%d" % (index + 1), index * 2, 2,
                   minimum=0, maximum=0xFFFF,
                   preserve_opaque_empty=True)
         for index in range(3)] + [
            FieldSpec("job_1", 6, 1, minimum=0, maximum=0xFF,
                      preserve_opaque_empty=True),
            FieldSpec("shared_value", 7, 1, minimum=0, maximum=0xFF),
        ])
    return TableLayoutSpec(
        "effect", AddressResolverKind.POINTER_RVA, pointer_reference,
        record_count, 8, fields, codec="effect-3+1-v1",
        structure_fingerprint=fingerprint, semantics_proven=True,
        metadata={"characterSlots": 3, "jobSlots": 1,
                  "valuePerSlot": False, "personDomain": (0, 1023),
                  "jobDomain": (0, 79), "canonicalCharacterEmpty": 1024,
                  "canonicalJobEmpty": 255})


def locate_pointer_effect_layout_8(
        image: "PEImage") -> Tuple[Optional[TableLayoutSpec], Dict[str, int], str]:
    initializer_hits = image.find_signature(
        MaskedSignature.parse(EFFECT_LAYOUT_8_INITIALIZER_PATTERN))
    write_hits = image.find_signature(
        MaskedSignature.parse(EFFECT_LAYOUT_8_WRITE_PATTERN))
    query_hits = image.find_signature(
        MaskedSignature.parse(EFFECT_LAYOUT_8_QUERY_PATTERN))
    if (len(initializer_hits) != 1 or len(write_hits) != 1 or
            len(query_hits) != 1):
        return None, {}, "8字节特效布局初始化/写入/查询签名匹配数为 %d/%d/%d" % (
            len(initializer_hits), len(write_hits), len(query_hits))
    initializer_pointer = struct.unpack(
        "<I", image.read_va(initializer_hits[0] + 10, 4))[0]
    writer_pointer = struct.unpack(
        "<I", image.read_va(write_hits[0] + 13, 4))[0]
    if initializer_pointer != writer_pointer:
        return None, {}, "8字节特效布局初始化与写入锚点指向不同指针单元"
    fingerprint = hashlib.sha256(
        image.read_va(initializer_hits[0], 42) +
        image.read_va(write_hits[0], 45) + image.read_va(query_hits[0], 44)
    ).hexdigest().upper()
    return effect_layout_8(writer_pointer, fingerprint=fingerprint), {
        "initializer": initializer_hits[0], "write": write_hits[0],
        "query": query_hits[0], "pointer": writer_pointer,
    }, ""


def locate_fixed_effect_layout_64(
        image: "PEImage") -> Tuple[Optional[TableLayoutSpec], Dict[str, int], str]:
    """Confirm the fixed 6.4 8-byte table without treating it as 6.5."""
    query_hits = image.find_signature(
        MaskedSignature.parse(EFFECT_LAYOUT_8_QUERY_PATTERN))
    pointer_write_hits = image.find_signature(
        MaskedSignature.parse(EFFECT_LAYOUT_8_WRITE_PATTERN))
    if len(query_hits) != 1 or pointer_write_hits:
        return None, {}, "6.4固定8字节特效查询/指针写入匹配数为 %d/%d" % (
            len(query_hits), len(pointer_write_hits))
    try:
        pointer_value = struct.unpack("<I", image.read_va(0x00500C3B, 4))[0]
    except ProfileError as exc:
        return None, {}, "6.4特效表锚点不可读: %s" % exc
    if pointer_value != 0x00508998:
        return None, {}, "6.4特效静态表锚点为0x%08X，预期0x00508998" % pointer_value
    fingerprint = hashlib.sha256(
        b"fixed-effect-64-v1\0" + image.read_va(query_hits[0], 44) +
        struct.pack("<I", pointer_value)).hexdigest().upper()
    pointer_fields = effect_layout_8(pointer_value, 180, fingerprint).fields
    layout = TableLayoutSpec(
        "effect", AddressResolverKind.FIXED_RVA, pointer_value, 180, 8,
        pointer_fields, codec="effect-3+1-v1",
        structure_fingerprint=fingerprint, semantics_proven=True,
        metadata={"characterSlots": 3, "jobSlots": 1,
                  "valuePerSlot": False, "personDomain": (0, 1023),
                  "jobDomain": (0, 79), "canonicalCharacterEmpty": 1024,
                  "canonicalJobEmpty": 255})
    return layout, {"query": query_hits[0], "fixed": pointer_value}, ""


def data_layouts_66(addresses: Dict[str, int],
                    limits: Dict[str, int]) -> Dict[str, TableLayoutSpec]:
    """Describe the independently resolved tables used by the eight pages."""
    battle_count = (limits["battle_ours"] + limits["battle_friends"] +
                    limits["battle_enemies"])
    layouts = {
        "battle": TableLayoutSpec(
            "battle", AddressResolverKind.FIXED_RVA,
            addresses["battle_units"], battle_count,
            limits["battle_stride"], (
                FieldSpec("data_id", 0, 2), FieldSpec("x", 8, 1),
                FieldSpec("y", 9, 1), FieldSpec("action", 0x0D, 1),
                FieldSpec("ai", 0x0E, 1), FieldSpec("direction", 0x0F, 1),
                FieldSpec("hp", 0x10, 4), FieldSpec("mp", 0x14, 4),
            ), codec="battle-unit-v1", semantics_proven=True),
        "person": TableLayoutSpec(
            "person", AddressResolverKind.POINTER_RVA,
            addresses["person_pointer"], limits["person_count"],
            limits["person_stride"], (
                FieldSpec("job", 6, 2), FieldSpec("ability_a", 0x11, 2),
                FieldSpec("ability_b", 0x13, 2),
                FieldSpec("ability_c", 0x15, 2),
                FieldSpec("ability_d", 0x17, 2),
                FieldSpec("ability_e", 0x19, 2),
                FieldSpec("level", 0x2A, 1),
                FieldSpec("experience", 0x2C, 1),
                FieldSpec("kills", 0x35, 2),
            ), codec="person-48-v1", semantics_proven=True),
        "warehouse": TableLayoutSpec(
            "warehouse", AddressResolverKind.FIXED_RVA,
            addresses["warehouse"], limits["warehouse_count"], 3, (
                FieldSpec("item_id", 0, 1), FieldSpec("level", 1, 1),
                FieldSpec("experience", 2, 1),
            ), codec="warehouse-3-v1", semantics_proven=True),
        "effect_exclusive": TableLayoutSpec(
            "effect_exclusive", AddressResolverKind.FIXED_RVA,
            addresses["exclusive_set"], limits["effect_count"], 16,
            codec="exclusive-set-16-v1", semantics_proven=True),
        "fatal": TableLayoutSpec(
            "fatal", AddressResolverKind.POINTER_RVA,
            addresses["fatal_pointer"], limits["fatal_count"],
            limits["fatal_stride"], tuple(
                [FieldSpec("character_%d" % (index + 1), index * 3, 2)
                 for index in range(5)] +
                [FieldSpec("level_%d" % (index + 1), index * 3 + 2, 1)
                 for index in range(5)] +
                [FieldSpec("effect_value", 15, 1)]),
            codec="fatal-5-v1", semantics_proven=True),
        "variables_bool": TableLayoutSpec(
            "variables_bool", AddressResolverKind.FIXED_RVA,
            addresses["bool_vars"], 512, 1,
            (FieldSpec("packed_bits", 0, 1),), codec="bitset-msb0-v1",
            semantics_proven=True, metadata={"bitCount": 4096,
                                              "bitOrder": "msb0"}),
        "variables_int": TableLayoutSpec(
            "variables_int", AddressResolverKind.FIXED_RVA,
            addresses["int_vars"], limits["variable_count"], 4,
            (FieldSpec("value", 0, 4, signed=True),),
            codec="int32-array-v1", semantics_proven=True),
        "variables_ptr": TableLayoutSpec(
            "variables_ptr", AddressResolverKind.FIXED_RVA,
            addresses["ptr_vars"], limits["variable_count"], 4,
            (FieldSpec("value", 0, 4, signed=True),),
            codec="int32-array-v1", semantics_proven=True),
    }
    if addresses.get("consumable_counts"):
        storage_count = limits.get(
            "consumable_storage_count", CONSUMABLE_STORAGE_COUNT_66)
        id_min = limits.get("consumable_id_min", CONSUMABLE_ID_MIN_66)
        ui_id_max = limits.get("consumable_ui_id_max", CONSUMABLE_ID_MAX_66)
        layouts["item_consumables"] = TableLayoutSpec(
            "item_consumables", AddressResolverKind.FIXED_RVA,
            addresses["consumable_counts"], storage_count, 1,
            (FieldSpec("count", 0, 1),), codec="uint8-count-array-v1",
            semantics_proven=True,
            metadata={"idMin": id_min, "uiIdMax": ui_id_max})
    return layouts


def parse_item_row_66(row: bytes) -> ItemRow66:
    if len(row) != 0x19:
        raise ValueError("6.6物品行必须正好是 0x19 字节")
    raw_name = row[0:0x0F].split(b"\0", 1)[0]
    try:
        name = raw_name.decode("gbk").rstrip("\r\n")
    except UnicodeDecodeError:
        name = "非法字符"
    return ItemRow66(name, row[0x0F], row[0x11], row[0x12], row[0x13],
                     row[0x15], row[0x16], row[0x17], row[0x18])


def parse_item_row_65(row: bytes) -> ItemRow66:
    """Parse the 6.4/6.5 physical 17-byte-name item row."""
    if len(row) != ITEM_ROW_SIZE_66:
        raise ValueError("6.5物品行必须正好是 0x19 字节")
    raw_name = row[0:0x11].split(b"\0", 1)[0]
    try:
        name = raw_name.decode("gbk").rstrip("\r\n")
    except UnicodeDecodeError:
        name = "非法字符"
    return ItemRow66(name, row[0x14], row[0x11], row[0x12], row[0x13],
                     row[0x15], row[0x16], row[0x17], row[0x18])


def parse_item_row(row: bytes, codec: str) -> ItemRow66:
    if codec == ITEM_CODEC_66:
        return parse_item_row_66(row)
    if codec == ITEM_CODEC_65:
        return parse_item_row_65(row)
    raise ValueError("未知物品行编解码器: %s" % codec)


def treasure_ids_66(table: bytes,
                    equipment_count: int = EQUIPMENT_COUNT_66,
                    codec: str = ITEM_CODEC_66) -> List[int]:
    if len(table) % 0x19:
        raise ValueError("6.6物品表长度不是 0x19 的整数倍")
    count = min(len(table) // 0x19, int(equipment_count))
    return [index for index in range(count)
            if parse_item_row(
                table[index*0x19:(index+1)*0x19], codec).is_treasure]


def validate_item_table_66(table: bytes, codec: str = ITEM_CODEC_66) -> bytes:
    if codec not in (ITEM_CODEC_65, ITEM_CODEC_66):
        raise ProfileError("未知物品行编解码器: %s" % codec)
    expected = (ITEM_LOW_COUNT_66 + ITEM_HIGH_COUNT_66) * ITEM_ROW_SIZE_66
    if len(table) != expected:
        raise ProfileError("物品表应为 255×0x19 字节，实际为 %d 字节" % len(table))
    if not any(table):
        raise ProfileError("物品表为全零数据")
    named = 0
    decodable = 0
    for index in range(ITEM_LOW_COUNT_66 + ITEM_HIGH_COUNT_66):
        row = table[index*ITEM_ROW_SIZE_66:(index+1)*ITEM_ROW_SIZE_66]
        name_size = 0x0F if codec == ITEM_CODEC_66 else 0x11
        raw_name = row[:name_size].split(b"\0", 1)[0]
        if not raw_name:
            continue
        named += 1
        try:
            raw_name.decode("gbk")
            decodable += 1
        except UnicodeDecodeError:
            pass
    if named < 128 or decodable < 128:
        raise ProfileError(
            "物品表名称语义探针失败: 非空 %d，GBK可解码 %d" %
            (named, decodable))
    return table


def load_item_table_66(game_path: str, codec: str = ITEM_CODEC_66) -> bytes:
    """Load the split 6.6 item rows without modifying game data files."""
    root = os.path.dirname(os.path.abspath(game_path)) if os.path.isfile(game_path) else os.path.abspath(game_path)
    low_path = os.path.join(root, "Data.e5")
    high_path = os.path.join(root, "Star.e5")
    try:
        with open(low_path, "rb") as stream:
            low_data = stream.read()
        with open(high_path, "rb") as stream:
            high_data = stream.read()
    except OSError as exc:
        raise ProfileError("物品元数据文件不可读: %s" % exc) from exc
    low_end = ITEM_LOW_OFFSET_66 + ITEM_LOW_COUNT_66 * ITEM_ROW_SIZE_66
    high_end = ITEM_HIGH_OFFSET_66 + ITEM_HIGH_COUNT_66 * ITEM_ROW_SIZE_66
    if len(low_data) < low_end:
        raise ProfileError("Data.e5 长度不足，无法读取 0..103 物品行")
    if len(high_data) < high_end:
        raise ProfileError("Star.e5 长度不足，无法读取 104..254 物品行")
    table = (low_data[ITEM_LOW_OFFSET_66:low_end] +
             high_data[ITEM_HIGH_OFFSET_66:high_end])
    return validate_item_table_66(table, codec)


@dataclass(frozen=True)
class MaskedSignature:
    """A byte signature where None represents one wildcard byte."""

    pattern: Tuple[Optional[int], ...]

    @classmethod
    def parse(cls, text: str) -> "MaskedSignature":
        values: List[Optional[int]] = []
        for part in text.split():
            values.append(None if part in ("?", "??") else int(part, 16))
        if not values:
            raise ValueError("签名不能为空")
        return cls(tuple(values))

    def find_all(self, data: bytes) -> List[int]:
        size = len(self.pattern)
        runs: List[Tuple[int, bytes]] = []
        run_start = 0
        run = bytearray()
        for index, value in enumerate(self.pattern + (None,)):
            if value is not None:
                if not run:
                    run_start = index
                run.append(value)
            elif run:
                runs.append((run_start, bytes(run)))
                run.clear()
        anchor_start, anchor = max(runs, key=lambda item: len(item[1]))
        hits: List[int] = []
        search_at = 0
        while True:
            anchor_hit = data.find(anchor, search_at)
            if anchor_hit < 0:
                break
            offset = anchor_hit - anchor_start
            if 0 <= offset <= len(data) - size and all(
                    expected is None or data[offset + index] == expected
                    for index, expected in enumerate(self.pattern)):
                hits.append(offset)
            search_at = anchor_hit + 1
        return hits


@dataclass(frozen=True)
class PESection:
    name: str
    virtual_address: int
    virtual_size: int
    raw_offset: int
    raw_size: int
    characteristics: int

    @property
    def executable(self) -> bool:
        return bool(self.characteristics & 0x20000000)


class PEImage:
    """Small dependency-free PE32 reader used by profile detection and tests."""

    def __init__(self, path: str):
        self.path = os.path.abspath(path)
        with open(self.path, "rb") as stream:
            self.data = stream.read()
        if len(self.data) < 0x100 or self.data[:2] != b"MZ":
            raise ProfileError("目标文件不是有效的 PE 文件")
        pe_offset = self._u32(0x3C)
        if pe_offset + 24 > len(self.data) or self.data[pe_offset:pe_offset + 4] != b"PE\0\0":
            raise ProfileError("目标文件缺少 PE 头")
        self.machine = self._u16(pe_offset + 4)
        section_count = self._u16(pe_offset + 6)
        optional_size = self._u16(pe_offset + 20)
        optional = pe_offset + 24
        self.optional_magic = self._u16(optional)
        if self.optional_magic != PE32_MAGIC:
            raise ProfileError("扳手只支持 32 位 PE32 游戏进程")
        self.image_base = self._u32(optional + 28)
        self.size_of_image = self._u32(optional + 56)
        section_table = optional + optional_size
        self.sections: List[PESection] = []
        for index in range(section_count):
            offset = section_table + index * 40
            if offset + 40 > len(self.data):
                raise ProfileError("PE 节表已截断")
            raw_name = self.data[offset:offset + 8].split(b"\0", 1)[0]
            self.sections.append(PESection(
                raw_name.decode("ascii", errors="replace"),
                self._u32(offset + 12),
                self._u32(offset + 8),
                self._u32(offset + 20),
                self._u32(offset + 16),
                self._u32(offset + 36),
            ))

    def _u16(self, offset: int) -> int:
        return struct.unpack_from("<H", self.data, offset)[0]

    def _u32(self, offset: int) -> int:
        return struct.unpack_from("<I", self.data, offset)[0]

    @property
    def is_x86(self) -> bool:
        return self.machine == IMAGE_FILE_MACHINE_I386

    @property
    def sha256(self) -> str:
        return hashlib.sha256(self.data).hexdigest().upper()

    def va_to_offset(self, va: int) -> int:
        rva = va - self.image_base
        for section in self.sections:
            span = max(section.virtual_size, section.raw_size)
            if section.virtual_address <= rva < section.virtual_address + span:
                delta = rva - section.virtual_address
                if delta >= section.raw_size:
                    raise ProfileError("地址位于未初始化 PE 数据区，文件中没有对应字节")
                return section.raw_offset + delta
        if 0 <= rva < min(len(self.data), 0x1000):
            return rva
        raise ProfileError("地址 0x%08X 不在 PE 文件映射范围内" % va)

    def offset_to_va(self, offset: int) -> int:
        for section in self.sections:
            if section.raw_offset <= offset < section.raw_offset + section.raw_size:
                return self.image_base + section.virtual_address + offset - section.raw_offset
        if 0 <= offset < 0x1000:
            return self.image_base + offset
        raise ProfileError("文件偏移 0x%X 不在 PE 节中" % offset)

    def read_va(self, va: int, size: int) -> bytes:
        if size <= 0:
            raise ProfileError("PE 读取长度必须大于零")
        offset = self.va_to_offset(va)
        rva = va - self.image_base
        for section in self.sections:
            if (section.virtual_address <= rva <
                    section.virtual_address + max(section.virtual_size,
                                                  section.raw_size)):
                delta = rva - section.virtual_address
                if delta + size > section.raw_size:
                    raise ProfileError("读取 PE 字节时越过当前文件后备节边界")
                break
        else:
            if rva + size > min(len(self.data), 0x1000):
                raise ProfileError("读取 PE 头时越过头部映射范围")
        result = self.data[offset:offset + size]
        if len(result) != size:
            raise ProfileError("读取 PE 字节时越过文件末尾")
        return result

    def find_signature(self, signature: MaskedSignature,
                       executable_only: bool = True) -> List[int]:
        hits: List[int] = []
        sections = [item for item in self.sections
                    if item.executable or not executable_only]
        for section in sections:
            raw = self.data[section.raw_offset:section.raw_offset + section.raw_size]
            for local_offset in signature.find_all(raw):
                hits.append(self.image_base + section.virtual_address + local_offset)
        return hits

    def section_data(self, section: PESection) -> bytes:
        return self.data[
            section.raw_offset:section.raw_offset + section.raw_size]

    def find_literal_references(self, value: int,
                                executable_only: bool = True) -> List[int]:
        needle = struct.pack("<I", value)
        result: List[int] = []
        sections = [item for item in self.sections
                    if item.executable or not executable_only]
        for section in sections:
            raw = self.data[section.raw_offset:section.raw_offset + section.raw_size]
            start = 0
            while True:
                index = raw.find(needle, start)
                if index < 0:
                    break
                result.append(self.image_base + section.virtual_address + index)
                start = index + 1
        return result


class RuntimeModuleImage(PEImage):
    """PE identity and section geometry backed by a live module image."""

    source = "runtime"

    def __init__(self, path: str, session):
        super().__init__(path)
        self.session = session
        self._section_cache: Dict[int, bytes] = {}

    def refresh(self) -> None:
        self._section_cache.clear()

    def read_va(self, va: int, size: int) -> bytes:
        if size <= 0:
            raise ProfileError("运行时映像读取长度必须大于零")
        rva = int(va) - self.image_base
        if rva < 0 or rva + size > self.size_of_image:
            raise ProfileError(
                "地址 0x%08X 不在运行时主模块映像范围内" % va)
        try:
            return self.session.read_bytes(int(va), int(size))
        except Exception as exc:
            raise ProfileError(
                "读取运行时映像 0x%08X+0x%X 失败: %s" %
                (va, size, exc)) from exc

    def _mapped_section(self, section: PESection) -> bytes:
        cache_key = int(section.virtual_address)
        cached = self._section_cache.get(cache_key)
        if cached is not None:
            return cached
        span = int(section.virtual_size or section.raw_size)
        if span <= 0:
            return b""
        reference = self.image_base + section.virtual_address
        result = bytearray(span)
        page_size = 0x1000
        for offset in range(0, span, page_size):
            size = min(page_size, span - offset)
            address = reference + offset
            try:
                if self.session.validate_range(address, size, write=False):
                    result[offset:offset + size] = self.session.read_bytes(
                        address, size)
            except Exception:
                continue
        mapped = bytes(result)
        self._section_cache[cache_key] = mapped
        return mapped

    def section_data(self, section: PESection) -> bytes:
        return self._mapped_section(section)

    def find_signature(self, signature: MaskedSignature,
                       executable_only: bool = True) -> List[int]:
        hits: List[int] = []
        sections = [item for item in self.sections
                    if item.executable or not executable_only]
        for section in sections:
            mapped = self._mapped_section(section)
            for local_offset in signature.find_all(mapped):
                hits.append(
                    self.image_base + section.virtual_address + local_offset)
        return hits

    def find_literal_references(self, value: int,
                                executable_only: bool = True) -> List[int]:
        needle = struct.pack("<I", value)
        result: List[int] = []
        sections = [item for item in self.sections
                    if item.executable or not executable_only]
        for section in sections:
            mapped = self._mapped_section(section)
            start = 0
            while True:
                index = mapped.find(needle, start)
                if index < 0:
                    break
                result.append(
                    self.image_base + section.virtual_address + index)
                start = index + 1
        return result


def _decode_effect_name_catalog_65_66(
        raw_table: bytes, source_label: str) -> Tuple[str, ...]:
    expected_size = (EFFECT_NAME_RECORD_COUNT_65_66 *
                     EFFECT_NAME_SLOT_SIZE_65_66)
    if len(raw_table) != expected_size:
        raise ProfileError(
            "特效名称表长度不足: %d/%d" %
            (len(raw_table), expected_size))

    names: List[str] = []
    meaningful_count = 0
    invalid_count = 0
    for index in range(EFFECT_NAME_RECORD_COUNT_65_66):
        start = index * EFFECT_NAME_SLOT_SIZE_65_66
        slot = raw_table[start:start + EFFECT_NAME_SLOT_SIZE_65_66]
        encoded = slot.split(b"\0", 1)[0]
        try:
            name = encoded.decode("gbk")
            if name and not name.isprintable():
                raise UnicodeDecodeError(
                    "gbk", encoded, 0, len(encoded), "包含控制字符")
        except UnicodeDecodeError:
            name = "非法字符"
            invalid_count += 1
        else:
            if name:
                meaningful_count += 1
        names.append(name)

    if meaningful_count < EFFECT_NAME_RECORD_COUNT_65_66 // 2:
        raise ProfileError(
            "%s 特效名称槽校验失败: 有效文本 %d/%d，非法槽 %d" %
            (source_label, meaningful_count,
             EFFECT_NAME_RECORD_COUNT_65_66, invalid_count))
    return tuple(names)


def load_effect_name_catalog_65_66(exe_path: str) -> Tuple[str, ...]:
    """Read the 6.5/6.6 fixed-width effect-name catalog from Ekd5.exe."""
    expected_size = (EFFECT_NAME_RECORD_COUNT_65_66 *
                     EFFECT_NAME_SLOT_SIZE_65_66)
    try:
        with open(exe_path, "rb") as stream:
            stream.seek(EFFECT_NAME_POOL_OFFSET_65_66)
            raw_table = stream.read(expected_size)
    except OSError as exc:
        raise ProfileError("读取特效名称表失败: %s" % exc) from exc
    return _decode_effect_name_catalog_65_66(
        raw_table, "文件偏移0x%X" % EFFECT_NAME_POOL_OFFSET_65_66)


def _register_effect_name_catalog_65_66(
        image: PEImage, addresses: Dict[str, int],
        metadata: Dict[str, object]) -> None:
    if isinstance(image, RuntimeModuleImage):
        runtime_address = EFFECT_NAME_RUNTIME_ADDRESS_65_66
        expected_size = (EFFECT_NAME_RECORD_COUNT_65_66 *
                         EFFECT_NAME_SLOT_SIZE_65_66)
        names = _decode_effect_name_catalog_65_66(
            image.read_va(runtime_address, expected_size),
            "运行时地址0x%08X" % runtime_address)
        source_kind = "runtime-fixed-slots"
    else:
        names = load_effect_name_catalog_65_66(image.path)
        runtime_address = image.offset_to_va(EFFECT_NAME_POOL_OFFSET_65_66)
        source_kind = "fixed-file-slots"
    addresses["effect_names"] = runtime_address
    metadata["effect_names"] = names
    metadata["effect_name_source"] = {
        "kind": source_kind,
        "file": "Ekd5.exe",
        "offset": EFFECT_NAME_POOL_OFFSET_65_66,
        "recordCount": EFFECT_NAME_RECORD_COUNT_65_66,
        "stride": EFFECT_NAME_SLOT_SIZE_65_66,
        "encoding": "gbk",
        "runtimeAddress": runtime_address,
    }


@dataclass
class EngineProfile:
    version: int
    display_version: str
    variant_name: str
    sha256: str
    file_size: int
    preferred_image_base: int
    image_size: int
    addresses: Dict[str, int]
    limits: Dict[str, int]
    capabilities: Dict[str, FeatureCapability]
    patches: Dict[str, PatchSpec]
    remote_calls: Dict[str, RemoteCallSpec]
    runtime_probe_failures: Dict[str, str] = field(default_factory=dict)
    dynamic_patches: Dict[str, DynamicPatchSpec] = field(default_factory=dict)
    metadata: Dict[str, object] = field(default_factory=dict)
    force_unverified_66: bool = False
    direct_write: bool = True
    resolved_features: Dict[str, ResolvedFeature] = field(default_factory=dict)
    # Capability evidence is diagnostic in the Python wrench.  The default
    # keeps the historical direct-write behavior while still requiring every
    # operation to resolve a concrete address/layout before it can run.
    access_policy: str = "direct_compat"

    def __post_init__(self) -> None:
        """Build the access map and normalize compatibility aliases.

        ``force_unverified_66`` was used by older local builds as a hidden
        switch.  Treat it as an explicit alias for ``direct_compat`` so old
        saved profiles keep working without restoring a second bypass path.
        """
        if self.force_unverified_66:
            self.access_policy = "direct_compat"
        if self.access_policy not in ("direct_compat", "evidence_gated"):
            raise ProfileError("未知能力访问策略: %s" % self.access_policy)
        for key, capability in self.capabilities.items():
            if key in self.resolved_features:
                continue
            access = ((FeatureAccess.READ_ONLY if self.is_66
                       else FeatureAccess.READ_WRITE)
                      if capability.statically_verified
                      else FeatureAccess.UNAVAILABLE)
            self.resolved_features[key] = ResolvedFeature(
                key=key, access=access, recipe_id=capability.recipe_id,
                reason=capability.reason, reason_code=capability.reason_code,
                runtime_probe_passed=not self.is_66)

    @property
    def is_66(self) -> bool:
        return self.version == EngineVersion.V66

    @property
    def engine_version(self) -> EngineVersion:
        return EngineVersion(int(self.version))

    @property
    def is_65(self) -> bool:
        return self.version == EngineVersion.V65

    @property
    def is_64(self) -> bool:
        return self.version == EngineVersion.V64

    def item_ids(self, domain: str = "record") -> Tuple[int, ...]:
        """Return the validated item-id domain for UI and write guards.

        ``item_count`` describes the physical metadata table (0..254 on the
        expanded 6.5 layouts).  It must not be used as an equipment boundary:
        the high records are consumables in the legacy expanded profile.
        """
        domain = str(domain).lower()
        if domain in ("record", "metadata", "all"):
            explicit = self.metadata.get("item_record_ids")
            if isinstance(explicit, (list, tuple)):
                return tuple(sorted({int(item) for item in explicit
                                     if 0 <= int(item) < 0xFF}))
            count = int(self.limits.get("item_count", 0))
            return tuple(range(max(0, min(0xFF, count))))
        if domain in ("equipment", "equip"):
            explicit = self.metadata.get("equipment_item_ids")
            if isinstance(explicit, (list, tuple)) and explicit:
                return tuple(sorted({int(item) for item in explicit
                                     if 0 <= int(item) < 0xFF}))
            count = int(self.limits.get(
                "equipment_count", self.limits.get("item_count", 0)))
            return tuple(range(max(0, min(0xFF, count))))
        if domain in ("consumable", "consumables", "item"):
            explicit = self.metadata.get("consumable_item_ids")
            if isinstance(explicit, (list, tuple)):
                return tuple(sorted({int(item) for item in explicit
                                     if 0 <= int(item) < 0xFF}))
            start = int(self.limits.get("consumable_id_min", 0))
            end = int(self.limits.get("consumable_ui_id_max", -1))
            return tuple(range(max(0, start), min(0xFE, end) + 1))
        raise ValueError("未知物品 ID 域: %s" % domain)

    def is_item_id(self, item_id: int, domain: str = "record") -> bool:
        return int(item_id) in self.item_ids(domain)

    def capability(self, key: str) -> FeatureCapability:
        return self.capabilities.get(
            key, FeatureCapability(
                key, CapabilityState.UNAVAILABLE, "当前引擎画像未定义此功能",
                "PROFILE_UNDEFINED"))

    def can_use(self, key: str) -> bool:
        """Compatibility alias for command/write callers."""
        return self.can_write(key)

    @property
    def direct_compatibility(self) -> bool:
        """Whether evidence state is allowed to remain diagnostic only."""
        return (self.access_policy == "direct_compat" or
                self.force_unverified_66)

    @staticmethod
    def _valid_code_address(address: int) -> bool:
        # All supported games are PE32.  Keep null, low-page and kernel-space
        # values out even when a profile contains a stale fallback.
        value = int(address)
        return 0x00010000 <= value <= 0x7FFFFFFF

    def _compat_recipe_available(self, key: str) -> bool:
        """Return whether a direct-mode operation has a concrete recipe.

        This intentionally does not inspect ``CapabilityState`` or runtime
        evidence.  It does reject undefined features, missing remote entry
        points and the 6.6 position helper, whose ABI is not implemented.
        """
        if key not in self.capabilities:
            return False
        spec = self.remote_calls.get(key)
        if spec is not None:
            return self._valid_code_address(spec.address)
        if key in self.patches:
            return bool(self.patches[key].sites)
        if key in self.dynamic_patches:
            return bool(self.dynamic_patches[key].call_sites)
        feature = self.resolved_feature(key)
        if feature.layout is not None and feature.layout.reference > 0:
            return True
        capability = self.capability(key)
        if any(self._valid_code_address(value)
               for value in capability.addresses.values()):
            return True
        if key == "diy":
            return True
        recipe = capability.recipe
        if isinstance(recipe, dict) and recipe.get("kind"):
            return True
        # The legacy profiles intentionally retain their fixed-address branch
        # and are already covered by the historic UI's version-specific code.
        return not self.is_66

    def resolved_feature(self, key: str) -> ResolvedFeature:
        feature = self.resolved_features.get(key)
        if feature is not None:
            return feature
        capability = self.capability(key)
        return ResolvedFeature(
            key, FeatureAccess.UNAVAILABLE, capability.recipe_id,
            reason=capability.reason or "当前引擎画像未解析此功能",
            reason_code=capability.reason_code or "PROFILE_UNRESOLVED")

    def can_read(self, key: str) -> bool:
        if self.direct_compatibility and self.direct_write:
            return self._compat_recipe_available(key)
        return self.resolved_feature(key).readable

    def can_write(self, key: str) -> bool:
        if self.direct_compatibility and self.direct_write:
            return self._compat_recipe_available(key)
        return self.resolved_feature(key).writable

    def revive_entry_address(self) -> int:
        """Return the version-local battlefield revive entry point."""
        spec = self.remote_calls.get("revive")
        if spec is not None and self._valid_code_address(spec.address):
            return int(spec.address)
        # These are deliberately version-local fallbacks.  Never borrow a
        # 6.5 address for 6.6 (or vice versa) when a profile is incomplete.
        fallbacks = {
            EngineVersion.V66: 0x004092C0,
            EngineVersion.V65: 0x004092C7,
            EngineVersion.V64: 0x004092C7,
            EngineVersion.V63: 0x004092E0,
            EngineVersion.V63_MP: 0x004092E0,
            EngineVersion.V62: 0x004092E0,
            EngineVersion.V61: 0x0040930F,
        }
        address = fallbacks.get(self.engine_version, 0)
        if not self._valid_code_address(address):
            raise ProfileError("当前画像没有可用的战场复活入口")
        return address

    def compatibility_warning(self, key: str) -> str:
        """Describe risk without turning the warning into an execution gate."""
        if not self.direct_compatibility:
            return ""
        capability = self.capability(key)
        messages = []
        if capability.state is not CapabilityState.ENABLED:
            messages.append(capability.state.label)
        spec = self.remote_calls.get(key)
        if spec is not None and not spec.abi_verified:
            messages.append("ABI未完成实机验证")
        if messages:
            return "直接兼容执行：" + "，".join(messages)
        return ""

    def note_compatibility_call(self, key: str, address: int,
                                arguments: Tuple[int, ...] = ()) -> None:
        """Keep a structured, in-profile audit trail for risky calls."""
        calls = self.metadata.setdefault("compatibility_calls", [])
        if not isinstance(calls, list):
            calls = []
            self.metadata["compatibility_calls"] = calls
        calls.append({
            "feature": str(key),
            "address": "0x%08X" % int(address),
            "arguments": [int(value) for value in arguments],
            "abiVerified": bool(getattr(
                self.remote_calls.get(key), "abi_verified", True)),
        })

    def set_feature_access(self, key: str, access: FeatureAccess,
                           reason: str = "", reason_code: str = "",
                           resolved_address: int = 0,
                           runtime_probe_passed: bool = False) -> None:
        current = self.resolved_feature(key)
        self.resolved_features[key] = current.with_runtime(
            access, resolved_address=resolved_address,
            reason=reason, reason_code=reason_code,
            probe_passed=runtime_probe_passed)

    def refresh_table_descriptor_runtime(
            self, key: str, memory,
            runtime_base: Optional[int] = None) -> Optional[ResolvedTableDescriptor]:
        descriptors = self.metadata.get("table_descriptors", {})
        descriptor = descriptors.get(key) if isinstance(descriptors, dict) else None
        if not isinstance(descriptor, ResolvedTableDescriptor):
            return None
        base = (memory.read_u32(descriptor.pointer_reference)
                if runtime_base is None else int(runtime_base))
        if base <= 0 or not memory.validate_actual_range(
                base, descriptor.byte_size, write=False):
            raise ProfileError("%s运行时表范围不可读: 0x%08X+0x%X" %
                               (key, base, descriptor.byte_size))
        page_ranges = []
        cursor = base
        end = base + descriptor.byte_size
        if hasattr(memory, "query_page"):
            while cursor < end:
                info = memory.query_page(cursor, actual_address=True)
                page_ranges.append((info.base_address, info.region_size))
                if info.end_address <= cursor:
                    raise ProfileError("%s运行时页范围无效" % key)
                cursor = min(end, info.end_address)
        else:
            page_ranges.append((base, descriptor.byte_size))
        generation = hashlib.sha256(
            ("%s|%s|%08X|%08X|%d|%d" %
             (self.sha256, key, descriptor.pointer_reference, base,
              descriptor.record_count, descriptor.stride)).encode("ascii")
        ).hexdigest().upper()
        updated = replace(
            descriptor, runtime_base=base, generation=generation,
            page_ranges=tuple(page_ranges))
        if getattr(memory, "pid", None) is not None:
            verify_runtime_descriptor(memory, updated)
        descriptors[key] = updated
        return updated

    def public_capabilities(self) -> List[FeatureCapability]:
        internal = {"person_r", "person_s", "person_face", "variables",
                    "variables_bool_anchor", "item_metadata",
                    "variables_bool_storage", "effect_query_layout"}
        return [item for key, item in self.capabilities.items()
                if key not in internal]

    def state_counts(self) -> Dict[CapabilityState, int]:
        result = {state: 0 for state in CapabilityState}
        for capability in self.public_capabilities():
            result[capability.state] += 1
        return result

    def available_count(self) -> Tuple[int, int]:
        values = self.public_capabilities()
        return sum(1 for item in values if item.available), len(values)

    def summary(self) -> str:
        counts = self.state_counts()
        public = self.public_capabilities()
        read_write = sum(1 for item in public if self.can_write(item.key))
        read_only = sum(1 for item in public
                        if self.can_read(item.key) and not self.can_write(item.key))
        unavailable = len(public) - read_write - read_only
        mode = " / 直接兼容执行" if self.direct_compatibility else ""
        text = ("%s / %s%s / 可读写 %d / 只读 %d / 禁用 %d / 静态确认 %d" %
                (self.display_version, self.variant_name, mode,
                 read_write, read_only,
                 unavailable, counts[CapabilityState.STATIC_VERIFIED]))
        unverified = sum(
            1 for item in public
            if self.compatibility_warning(item.key))
        if unverified:
            text += " / 未完成验证但可执行 %d" % unverified
        identity = self.metadata.get("identity_diagnostics")
        if isinstance(identity, dict):
            text += " / PE32/x86主模块已附加"
            if identity.get("source") == "runtime":
                text += " / 运行时画像"
            if not identity.get("core_signature_passed", False):
                text += " / 核心签名仅诊断(人物%d/战场%d)" % (
                    len(identity.get("person_hits", ())),
                    len(identity.get("battle_hits", ())))
        blocked = [item.reason for item in self.public_capabilities()
                   if not self.can_read(item.key) and item.reason]
        if blocked:
            text += " / " + blocked[0]
        composition = self.composition_summary()
        if composition:
            text += " / " + composition
        return text

    def composition_summary(self) -> str:
        parts = []
        if self.can_read("person"):
            parts.append("人物:%s/0x%X" %
                         ("66" if self.is_66 else self.display_version,
                          self.limits.get("person_stride", 0)))
        effect = self.resolved_feature("effect")
        if effect.readable and effect.layout is not None:
            layout_name = ("4+2" if effect.layout.codec == "effect-4+2-v1"
                           else "3+1")
            resolver_name = ("指针" if effect.layout.pointer_backed else "固定")
            parts.append("天赋:%dB/%s/%s" %
                         (effect.layout.stride, layout_name, resolver_name))
        if self.can_read("fatal"):
            fatal_kind = "指针" if "fatal_pointer" in self.addresses else "固定"
            parts.append("必杀:%dB/%s" %
                         (self.limits.get("fatal_stride", 0), fatal_kind))
        return " | ".join(parts)

    def reference_va(self, key: str) -> int:
        return self.addresses[key]

    def effect_name_catalog(self) -> Tuple[str, ...]:
        names = self.metadata.get("effect_names")
        if not isinstance(names, tuple):
            raise ProfileError(str(self.metadata.get(
                "effect_name_error", "当前画像没有可用的特效名称表")))
        expected = self.limits.get(
            "effect_name_count", EFFECT_NAME_RECORD_COUNT_65_66)
        if len(names) != expected:
            raise ProfileError(
                "特效名称表记录数不符: %d/%d" % (len(names), expected))
        return names

    def item_table_66(self) -> bytes:
        table = self.metadata.get("item_table_66")
        if not isinstance(table, bytes):
            raise ProfileError(self.capability("item_metadata").reason or
                               "物品元数据当前不可用")
        codec = str(self.metadata.get("item_row_codec", ITEM_CODEC_66))
        return validate_item_table_66(table, codec)

    def item_row(self, item_id: int) -> ItemRow66:
        table = self.item_table_66()
        if not 0 <= int(item_id) < len(table) // ITEM_ROW_SIZE_66:
            raise ProfileError("物品 ID 超出当前元数据范围")
        offset = int(item_id) * ITEM_ROW_SIZE_66
        codec = str(self.metadata.get("item_row_codec", ITEM_CODEC_66))
        return parse_item_row(table[offset:offset + ITEM_ROW_SIZE_66], codec)

    def _probe_legacy_composite_runtime(self, memory) -> None:
        failures: Dict[str, str] = {}
        effect = self.resolved_feature("effect")
        layout = effect.layout
        if isinstance(layout, TableLayoutSpec) and layout.pointer_backed:
            try:
                effect_base = memory.read_u32(layout.reference)
                if not memory.validate_actual_range(
                        effect_base, layout.byte_size, write=False):
                    raise ProfileError(
                        "特效分配表运行时指针不可读: 0x%08X" % effect_base)
                self.addresses["effect_assign"] = effect_base
                writable = memory.validate_actual_range(
                    effect_base, layout.byte_size, write=True)
                self.set_feature_access(
                    "effect", (FeatureAccess.READ_WRITE if writable else
                               FeatureAccess.READ_ONLY),
                    reason=("" if writable else "特效分配表运行时页只读"),
                    reason_code=("" if writable else "RUNTIME_READ_ONLY"),
                    resolved_address=effect_base, runtime_probe_passed=True)
            except Exception as exc:
                failures["effect"] = "特效结构探针失败: %s" % exc
                self.addresses["effect_assign"] = 0
                self.set_feature_access(
                    "effect", FeatureAccess.UNAVAILABLE,
                    failures["effect"], "RUNTIME_PROBE_FAILED")

        capability = self.capabilities.get("item_consumables")
        mapping = self.metadata.get("consumable_mapping")
        if (capability is not None and capability.statically_verified and
                isinstance(mapping, dict)):
            try:
                count_base = self.addresses["consumable_counts"]
                size = int(mapping["storageCount"]) * int(mapping["stride"])
                if mapping.get("baseDereference"):
                    count_base = memory.read_u32(count_base)
                    readable = memory.validate_actual_range(
                        count_base, size, write=False)
                    writable = memory.validate_actual_range(
                        count_base, size, write=True)
                else:
                    readable = memory.validate_range(
                        count_base, size, write=False)
                    writable = memory.validate_range(
                        count_base, size, write=True)
                if not readable:
                    raise ProfileError("消耗品计数表运行时页不可读")
                self.set_feature_access(
                    "item_consumables",
                    FeatureAccess.READ_WRITE if writable else FeatureAccess.READ_ONLY,
                    reason=("" if writable else "消耗品计数表运行时页只读"),
                    reason_code=("" if writable else "RUNTIME_READ_ONLY"),
                    resolved_address=count_base, runtime_probe_passed=True)
            except Exception as exc:
                failures["item_consumables"] = "消耗品映射运行时探针失败: %s" % exc
                self.set_feature_access(
                    "item_consumables", FeatureAccess.UNAVAILABLE,
                    failures["item_consumables"], "RUNTIME_PROBE_FAILED")
        self.runtime_probe_failures = failures

    def probe_runtime(self, memory) -> None:
        """Apply cheap structural probes without writing to the target."""
        if not self.is_66:
            self._probe_legacy_composite_runtime(memory)
            return
        failures: Dict[str, str] = {}
        for key, patch in self.patches.items():
            try:
                for site in patch.sites:
                    current = memory.read_bytes(site.address, len(site.original))
                    if current not in (site.original, site.patched):
                        failures[key] = "%s运行时原字节已变化" % patch.label
                        break
            except Exception as exc:
                failures[key] = "%s运行时字节不可读: %s" % (patch.label, exc)
        for key, patch in self.dynamic_patches.items():
            try:
                for site in patch.call_sites:
                    current = memory.read_bytes(site.address, len(site.original))
                    if current != site.original and not memory.is_dynamic_patch_enabled(key):
                        failures[key] = "%s运行时调用点原字节已变化" % patch.label
                        break
            except Exception as exc:
                failures[key] = "%s运行时调用点不可读: %s" % (patch.label, exc)
        for key, call in self.remote_calls.items():
            if not call.signature:
                continue
            try:
                if memory.read_bytes(call.address, len(call.signature)) != call.signature:
                    failures[key] = "%s运行时函数签名已变化" % call.label
            except Exception as exc:
                failures[key] = "%s运行时函数不可读: %s" % (call.label, exc)
        try:
            first_id = memory.read_u16(self.addresses["battle_units"])
            if first_id != 0xFFFF and first_id >= self.limits["person_count"]:
                failures["battle"] = "战场单位 Data ID 探针失败"
            battle_count = (self.limits["battle_ours"] +
                            self.limits["battle_friends"] +
                            self.limits["battle_enemies"])
            battle_size = battle_count * self.limits["battle_stride"]
            if not memory.validate_range(
                    self.addresses["battle_units"], battle_size, write=False):
                failures["battle"] = "战场单位表运行时页不可读"
            elif not memory.validate_range(
                    self.addresses["battle_units"], battle_size, write=True):
                failures["battle"] = "战场单位表运行时页只读"
        except Exception as exc:
            failures["battle"] = "战场单位表不可读: %s" % exc
        try:
            person_base = memory.read_u32(self.addresses["person_pointer"])
            person_size = (self.limits["person_count"] *
                           self.limits["person_stride"])
            if not memory.validate_actual_range(
                    person_base, person_size, write=False):
                failures["person"] = "人物数组运行时指针不可读"
            else:
                self.refresh_table_descriptor_runtime(
                    "person", memory, person_base)
                if not memory.validate_actual_range(
                        person_base, person_size, write=True):
                    failures["person"] = "人物数组运行时页只读"
        except Exception as exc:
            failures["person"] = "人物数组指针不可读: %s" % exc
        try:
            fatal_base = memory.read_u32(self.addresses["fatal_pointer"])
            if not memory.validate_actual_range(fatal_base, 16, write=False):
                failures["fatal"] = "必杀分配运行时指针不可读"
            else:
                self.addresses["fatal_table"] = fatal_base
                if not memory.validate_actual_range(fatal_base, 16, write=True):
                    failures["fatal"] = "必杀分配运行时页只读"
        except Exception as exc:
            failures["fatal"] = "必杀分配指针不可读: %s" % exc
        try:
            warehouse_size = self.limits["warehouse_count"] * 3
            if not memory.validate_range(
                    self.addresses["warehouse"], warehouse_size, write=False):
                failures["warehouse"] = "仓库表运行时页不可读"
            elif not memory.validate_range(
                    self.addresses["warehouse"], warehouse_size, write=True):
                failures["warehouse"] = "仓库表运行时页只读"
        except Exception as exc:
            failures["warehouse"] = "仓库结构探针失败: %s" % exc
        try:
            self.item_table_66()
        except (ProfileError, ValueError) as exc:
            failures["item_metadata"] = "6.6物品元数据探针失败: %s" % exc
        consumable = self.capabilities.get("item_consumables")
        if consumable is not None and consumable.statically_verified:
            try:
                mapping = self.metadata["consumable_mapping"]
                count_base = self.addresses["consumable_counts"]
                mapping_size = (int(mapping["storageCount"]) *
                                int(mapping["stride"]))
                if mapping.get("baseDereference"):
                    count_base = memory.read_u32(count_base)
                    readable = memory.validate_actual_range(
                        count_base, mapping_size, write=False)
                    valid = memory.validate_actual_range(
                        count_base, mapping_size, write=True)
                else:
                    readable = memory.validate_range(
                        count_base, mapping_size, write=False)
                    valid = memory.validate_range(
                        count_base, mapping_size, write=True)
                if not readable:
                    failures["item_consumables"] = "消耗品计数表运行时页不可读"
                elif not valid:
                    failures["item_consumables"] = "消耗品计数表运行时页只读"
            except Exception as exc:
                failures["item_consumables"] = "消耗品映射运行时探针失败: %s" % exc
        try:
            self.addresses["effect_assign"] = 0
            layout = self.metadata.get("table_layouts", {}).get("effect")
            if not isinstance(layout, TableLayoutSpec):
                raise ProfileError("特效表缺少声明式布局")
            effect_base = memory.read_u32(
                self.addresses["effect_assign_pointer"])
            effect_size = layout.byte_size
            if not memory.validate_actual_range(
                    effect_base, effect_size, write=False):
                failures["effect"] = (
                    "特效分配表运行时指针不可读: 0x%08X" % effect_base)
            else:
                self.refresh_table_descriptor_runtime(
                    "effect", memory, effect_base)
                effect_table = memory.read_actual_bytes(effect_base, effect_size)
                if isinstance(layout, TableLayoutSpec) and layout.stride == 16:
                    empty_characters = detect_effect_empty_characters_66(
                        effect_table)
                    self.metadata["effect_empty_characters"] = empty_characters
                    self.metadata["effect_empty_character"] = max(
                        set(empty_characters),
                        key=lambda value: (empty_characters.count(value), -value))
                elif isinstance(layout, TableLayoutSpec) and layout.stride == 8:
                    for offset in range(0, len(effect_table), 8):
                        row = parse_effect_assignment_row_8(
                            effect_table[offset:offset + 8])
                        if row.job_id < 80 or row.job_id >= 80:
                            continue
                else:
                    raise ProfileError("特效表缺少声明式布局")
                self.addresses["effect_assign"] = effect_base
                if not memory.validate_actual_range(
                        effect_base, effect_size, write=True):
                    failures["effect"] = "特效分配表运行时页只读"
            exclusive_size = self.limits["effect_count"] * 16
            if not memory.validate_range(
                    self.addresses["exclusive_set"], exclusive_size,
                    write=False):
                failures["effect_exclusive"] = "专属/套装表运行时页不可读"
            elif not memory.validate_range(
                    self.addresses["exclusive_set"], exclusive_size,
                    write=True):
                failures["effect_exclusive"] = "专属/套装表运行时页只读"
        except Exception as exc:
            failures["effect"] = "特效结构探针失败: %s" % exc
        for address_key, capability_key in (
                ("bool_vars", "variables_bool"),
                ("int_vars", "variables_int"),
                ("ptr_vars", "variables_ptr")):
            try:
                layout = self.metadata["variable_layouts"][capability_key]
                base = self.addresses[address_key]
                count_offset = layout.get("countOffset")
                if count_offset is not None:
                    actual_count = memory.read_u32(base + int(count_offset))
                    if actual_count != self.limits["variable_count"]:
                        failures[capability_key] = (
                            "%s数量头为 %d，预期 %d" %
                            (address_key, actual_count,
                             self.limits["variable_count"]))
                        continue
                size = (int(layout["dataOffset"]) +
                        ((self.limits["variable_count"] + 7) // 8
                         if layout.get("storage") == "bitset" else
                         (self.limits["variable_count"] - 1) *
                         int(layout["stride"]) + int(layout["width"])))
                if not memory.validate_range(base, size, write=False):
                    failures[capability_key] = "%s运行时页不可读" % address_key
                elif not memory.validate_range(base, size, write=True):
                    failures[capability_key] = "%s运行时页只读" % address_key
            except Exception as exc:
                failures[capability_key] = "变量表结构探针失败: %s" % exc
        self.runtime_probe_failures = failures
        for key, capability in self.capabilities.items():
            remote_call = self.remote_calls.get(key)
            if remote_call is not None and not remote_call.abi_verified:
                self.set_feature_access(
                    key, FeatureAccess.UNAVAILABLE,
                    capability.reason or "远程调用 ABI 尚未完整验证",
                    "REMOTE_ABI_NOT_VERIFIED")
            elif not capability.statically_verified:
                self.set_feature_access(
                    key, FeatureAccess.UNAVAILABLE,
                    capability.reason or "静态结构未识别",
                    capability.reason_code or "STATIC_VERIFICATION_FAILED")
            elif key in failures:
                access = (FeatureAccess.READ_ONLY
                          if "只读" in failures[key]
                          else FeatureAccess.UNAVAILABLE)
                self.set_feature_access(
                    key, access, failures[key],
                    ("RUNTIME_READ_ONLY" if access.readable
                     else "RUNTIME_PROBE_FAILED"),
                    runtime_probe_passed=access.readable)
            else:
                current = self.resolved_feature(key)
                address = current.resolved_address
                if key == "effect":
                    address = self.addresses.get("effect_assign", 0)
                elif key == "fatal":
                    address = self.addresses.get("fatal_table", 0)
                access = (FeatureAccess.READ_WRITE
                          if current.layout is None or current.layout.writable
                          else FeatureAccess.READ_ONLY)
                self.set_feature_access(
                    key, access,
                    reason=("" if access.writable else
                            "读取语义已确认，写入语义尚未形成唯一锚点"),
                    reason_code=("" if access.writable else
                                 "WRITER_SEMANTICS_NOT_VERIFIED"),
                    resolved_address=address, runtime_probe_passed=True)
        dependency_failures = {
            "warehouse": ("item_bulk_treasure",),
            "item_metadata": ("item_bulk_treasure",),
            "person": ("recalculate",),
            "battle": ("revive",),
        }
        for dependency, dependents in dependency_failures.items():
            if dependency not in failures:
                continue
            for dependent in dependents:
                self.set_feature_access(
                    dependent, FeatureAccess.UNAVAILABLE,
                    failures[dependency], "DEPENDENCY_RUNTIME_PROBE_FAILED")
        for key, current in list(self.capabilities.items()):
            if (key not in failures and
                    current.state is CapabilityState.RUNTIME_VERIFIED):
                self.capabilities[key] = current.promote(CapabilityState.ENABLED)

        for key, call in list(self.remote_calls.items()):
            capability = self.capabilities.get(key)
            if capability is not None and capability.available and not call.abi_verified:
                self.remote_calls[key] = RemoteCallSpec(
                    call.key, call.label, call.address, call.signature, True,
                    call.signature_pattern, call.normalized_fingerprint,
                    call.calling_convention, call.ecx_source, call.arguments,
                    call.callee_cleanup, call.preconditions, call.postconditions,
                    call.context_reference, call.wrapper_kind)


KNOWN_66 = {
    "88EC708A24718868090F70256D09086E83C92FAB0613C7B8D3742EF974D3564D":
        ("清儿吕布传", 1413120),
    "4A4FD8DDBF83E5F0B769D1B97BF8F6E6431C3AB42892024A354228212D3D06A4":
        ("新改曹操傳6.6修正版", 1130496),
}
# Display-only identities must not expand the dual-baseline evidence quorum.
KNOWN_66_VARIANTS = {
    "4A5221B4842A261647D0DE23CDE3867974A5493A9C98C21015A6116AA5F2E063":
        ("8.14绝对反击6.6", 1132032),
    "402F1675DC863577AD356B16B20FCE058A94C66E13B0BDD5EFAC1CE8E752F26B":
        ("以和为贵1-demo 6.6", 7789311),
}
_KNOWN_66_IDENTITIES = dict(KNOWN_66)
_KNOWN_66_IDENTITIES.update(KNOWN_66_VARIANTS)
_BASELINE_SHA256 = tuple(KNOWN_66)
_EVIDENCE_REPOSITORY = EvidenceRepository()


_PATCH_LOCATORS = {
    "control_all_factions": (
        ("E8 ?? ?? ?? ?? 3B 45 F0 74 0F 8B 4D F4 03 4D F8", 8,
         b"\x74\x0F", b"\x90\x90", 2),
        ("8B 55 F4 3B 55 EC 74 34 8B 4D F0 E8 ?? ?? ?? ??", 6,
         b"\x74\x34", b"\x90\x90", 2),
        ("5A 33 C9 3B D0 0F 94 C1 39 4D DC 75 27 0F B7 45 0C", 11,
         b"\x75\x27", b"\x90\x90", 2),
        ("90 90 90 90 90 90 3B F0 75 23 68 ?? ?? ?? ?? E8", 8,
         b"\x75\x23", b"\xEB\x23", 2),
    ),
    "control_friendly": (
        ("DC E8 ?? ?? ?? ?? 3C 01 74 14 33 C0 A0 30 42 4B 00", 8, b"\x74", b"\xEB", 2),
        ("DC E8 ?? ?? ?? ?? 3C 07 74 15 33 C9 8A 0D 30 42 4B 00", 8, b"\x74", b"\xEB", 2),
        ("F8 E8 ?? ?? ?? ?? 3C 07 74 15 33 C0 A0 30 42 4B 00", 8, b"\x74", b"\xEB", 2),
        ("D8 E8 ?? ?? ?? ?? 3C 02 75 C8 8B 4D D8 E8", 8, b"\x75\xC8", b"\x90\x90", 2),
        ("D8 E8 ?? ?? ?? ?? 3C 07 75 BC 8B 4D D8 E8", 8, b"\x75\xBC", b"\x90\x90", 2),
        ("E4 E8 ?? ?? ?? ?? 3C 07 74 18 33 C0 A0 30 42 4B 00", 8, b"\x74", b"\xEB", 2),
    ),
    "control_no_ai": (
        ("E0 E8 ?? ?? ?? ?? 3C 07 74 B8 6A 04 8B 4D E0", 8, b"\x74", b"\xEB", 2),
    ),
    "control_move_after_wait": (
        ("DC E8 ?? ?? ?? ?? 85 C0 74 04 C6 45 FC 06", 8, b"\x74", b"\xEB", 2),
    ),
    "control_move_255": (
        ("51 14 8B E5 5D C2 04 00 55 8B EC 83 EC 08 89 4D F8", 8,
         b"\x55\x8B\xEC\x83\xEC\x08", b"\x33\xC0\x48\xC3\x90\x90", 6),
    ),
    "control_pass_through": (
        ("FC E8 ?? ?? ?? ?? 85 D2 74 11 83 7D F0 01", 8, b"\x74", b"\xEB", 2),
        ("90 90 90 90 90 90 90 90 55 8B EC 51 52 50 33 D2", 8,
         b"\x55\x8B\xEC", b"\x33\xC0\xC3", 3),
    ),
}


def _locate_patch(image: PEImage, key: str, label: str) -> Tuple[Optional[PatchSpec], str]:
    sites: List[PatchSite] = []
    patterns: List[str] = []
    instruction_lengths: List[int] = []
    for pattern, target_offset, original, patched, instruction_length in _PATCH_LOCATORS[key]:
        hits = image.find_signature(MaskedSignature.parse(pattern))
        if len(hits) != 1:
            return None, "%s签名匹配数为 %d" % (label, len(hits))
        address = hits[0] + target_offset
        current = image.read_va(address, len(original))
        if current != original:
            return None, "%s原字节不匹配" % label
        sites.append(PatchSite(address, original, patched))
        patterns.append(pattern)
        instruction_lengths.append(instruction_length)
    fingerprint = hashlib.sha256("|".join(patterns).encode("ascii")).hexdigest().upper()
    return PatchSpec(key, label, tuple(sites), tuple(patterns), fingerprint,
                     tuple(instruction_lengths)), ""


def _capability(key: str, static_verified: bool, reason: str = "",
                recipe: Optional[Dict[str, object]] = None,
                reason_code: str = "", **addresses: int) -> FeatureCapability:
    if not static_verified:
        return FeatureCapability(
            key, CapabilityState.UNAVAILABLE, reason or "静态验证未通过",
            reason_code or "STATIC_VERIFICATION_FAILED", "", (), addresses)
    current_recipe = recipe or {
        "kind": "profile-contract",
        "feature": key,
        "addresses": {name: value for name, value in sorted(addresses.items())},
    }
    current_recipe_id = recipe_id(key, current_recipe)
    capability = FeatureCapability(
        key, CapabilityState.STATIC_VERIFIED,
        "静态签名已通过，等待双基底实机证据", "RUNTIME_EVIDENCE_REQUIRED",
        current_recipe_id, (), addresses, current_recipe)
    try:
        verified = _EVIDENCE_REPOSITORY.verified_recipe(key, _BASELINE_SHA256)
    except EvidenceError as exc:
        return capability.with_state(
            CapabilityState.STATIC_VERIFIED,
            reason="证据清单无效，保持仅静态确认: %s" % exc,
            reason_code="EVIDENCE_INVALID")
    if verified is not None and verified.recipe_id == current_recipe_id:
        return capability.with_state(
            CapabilityState.RUNTIME_VERIFIED,
            reason="双基底实机证据已通过，等待当前进程探针",
            reason_code="SESSION_PROBE_REQUIRED",
            evidence_ids=verified.evidence_ids)
    return capability


def _recipe_bytes(value: object, label: str) -> bytes:
    try:
        result = bytes.fromhex(str(value))
    except ValueError as exc:
        raise ProfileError("证据配方中的%s不是合法十六进制" % label) from exc
    if not result:
        raise ProfileError("证据配方中的%s不能为空" % label)
    return result


def _recipe_int(value: object, default: int = 0) -> int:
    if value is None:
        return default
    if isinstance(value, str):
        return int(value, 0)
    return int(value)


def _recipe_locator(image: PEImage, locator: Dict[str, object],
                    label: str) -> Tuple[int, str]:
    pattern = str(locator.get("pattern", ""))
    if not pattern:
        raise ProfileError("%s证据配方缺少定位签名" % label)
    hits = image.find_signature(MaskedSignature.parse(pattern))
    if len(hits) != 1:
        raise ProfileError("%s证据签名匹配数为 %d" % (label, len(hits)))
    return hits[0] + _recipe_int(locator.get("targetOffset"), 0), pattern


def _materialize_verified_recipes(
        image: PEImage, capabilities: Dict[str, FeatureCapability],
        patches: Dict[str, PatchSpec], dynamic_patches: Dict[str, DynamicPatchSpec],
        remote_calls: Dict[str, RemoteCallSpec], addresses: Dict[str, int],
        limits: Dict[str, int], metadata: Dict[str, object]) -> None:
    labels = {
        "control_all_factions": "不分敌我",
        "control_auto_return": "自动回归",
        "turn_refresh_direction": "转向刷新",
        "turn_refresh_position": "坐标刷新",
        "item_consumables": "消耗品计数",
    }
    for key, label in labels.items():
        try:
            verified = _EVIDENCE_REPOSITORY.verified_recipe(key, _BASELINE_SHA256)
        except EvidenceError as exc:
            capabilities[key] = capabilities[key].with_state(
                CapabilityState.UNAVAILABLE,
                reason="证据清单无效: %s" % exc,
                reason_code="EVIDENCE_INVALID")
            continue
        if verified is None:
            continue
        recipe = verified.recipe
        kind = str(recipe.get("kind", ""))
        try:
            if kind == "patch":
                sites: List[PatchSite] = []
                patterns: List[str] = []
                lengths: List[int] = []
                for locator in recipe.get("locators", []):
                    address, pattern = _recipe_locator(image, locator, label)
                    original = _recipe_bytes(locator.get("originalHex"), "原字节")
                    patched = _recipe_bytes(locator.get("patchedHex"), "补丁字节")
                    if len(original) != len(patched):
                        raise ProfileError("%s证据补丁长度不一致" % label)
                    if image.read_va(address, len(original)) != original:
                        raise ProfileError("%s证据原字节不匹配" % label)
                    instruction_length = _recipe_int(
                        locator.get("instructionLength"), len(original))
                    if instruction_length != len(original):
                        raise ProfileError("%s证据没有覆盖完整指令" % label)
                    sites.append(PatchSite(address, original, patched))
                    patterns.append(pattern)
                    lengths.append(instruction_length)
                if not sites:
                    raise ProfileError("%s证据没有补丁站点" % label)
                spec = PatchSpec(
                    key, label, tuple(sites), tuple(patterns),
                    str(recipe.get("normalizedFingerprint", "")), tuple(lengths),
                    str(recipe.get("rollbackRule", "owned-bytes-only")))
                patches[key] = spec
            elif kind == "dynamic-stub":
                call_sites: List[DynamicCallSite] = []
                patterns = []
                for locator in recipe.get("callLocators", []):
                    address, pattern = _recipe_locator(image, locator, label)
                    original = _recipe_bytes(locator.get("originalHex"), "调用点原字节")
                    length = _recipe_int(locator.get("instructionLength"),
                                         len(original))
                    if len(original) != length or length < 5:
                        raise ProfileError("%s调用点没有覆盖完整 rel32 指令" % label)
                    if image.read_va(address, length) != original:
                        raise ProfileError("%s调用点原字节不匹配" % label)
                    call_sites.append(DynamicCallSite(
                        address, original, length,
                        (_recipe_int(locator.get("branchOpcodeHex"))
                         if isinstance(locator.get("branchOpcodeHex"), int)
                         else int(str(locator.get("branchOpcodeHex", "E9")), 16)),
                        _recipe_int(locator.get("stubOffset"), 0),
                        bool(locator.get("operandOnly", False))))
                    patterns.append(pattern)
                relocations = tuple(StubRelocation(
                    _recipe_int(item["offset"]), str(item["kind"]),
                    _recipe_int(item["value"]), _recipe_int(item.get("addend"), 0))
                    for item in recipe.get("relocations", []))
                dynamic_patches[key] = DynamicPatchSpec(
                    key, label, tuple(call_sites),
                    _recipe_bytes(recipe.get("stubTemplateHex"), "stub模板"),
                    relocations, tuple(patterns),
                    str(recipe.get("normalizedFingerprint", "")),
                    str(recipe.get("rollbackRule",
                                   "owned-branches-and-no-eip-in-stub")))
            elif kind == "remote-call":
                locator = dict(recipe.get("locator", {}))
                address, pattern = _recipe_locator(image, locator, label)
                signature = _recipe_bytes(locator.get("prologueHex"), "函数入口字节")
                if image.read_va(address, len(signature)) != signature:
                    raise ProfileError("%s函数入口原字节不匹配" % label)
                expected_wrapper = {
                    "turn_refresh_direction": "legacy-turn-v1",
                    "turn_refresh_position": "legacy-position-v1",
                }.get(key, "")
                if expected_wrapper and recipe.get("wrapperKind") != expected_wrapper:
                    raise ProfileError("%s调用包装尚未由扳手实现" % label)
                remote_calls[key] = RemoteCallSpec(
                    key, label, address, signature, True, pattern,
                    str(recipe.get("normalizedFingerprint", "")),
                    str(recipe.get("callingConvention", "unknown")),
                    str(recipe.get("ecxSource", "")),
                    tuple(str(item) for item in recipe.get("arguments", [])),
                    _recipe_int(recipe.get("calleeCleanup"), 0),
                    tuple(str(item) for item in recipe.get("preconditions", [])),
                    tuple(str(item) for item in recipe.get("postconditions", [])),
                    _recipe_int(recipe.get("contextReference"), 0),
                    str(recipe.get("wrapperKind", "")))
            elif kind == "item-mapping":
                locator = dict(recipe.get("locator", {}))
                anchor, _pattern = _recipe_locator(image, locator, label)
                operand_offset = _recipe_int(locator.get("operandOffset"), 0)
                base = struct.unpack("<I", image.read_va(anchor + operand_offset, 4))[0]
                id_min = _recipe_int(recipe.get("idMin"), -1)
                id_max = _recipe_int(recipe.get("idMax"), -1)
                width = _recipe_int(recipe.get("width"), 0)
                stride = _recipe_int(recipe.get("stride"), 0)
                if not (0 <= id_min <= id_max < limits["item_count"]):
                    raise ProfileError("消耗品证据 ID 范围无效")
                if width not in (1, 2, 4) or stride < width:
                    raise ProfileError("消耗品证据计数宽度或步长无效")
                item_ids = [_recipe_int(item) for item in recipe.get("itemIds", [])]
                if (len(set(item_ids)) != len(item_ids) or
                        any(item < id_min or item > id_max for item in item_ids)):
                    raise ProfileError("消耗品证据显式 ID 无效或重复")
                category_pairs = [list(item) for item in recipe.get("categoryPairs", [])]
                if any(len(item) != 2 or any(
                        not 0 <= _recipe_int(value) <= 255 for value in item)
                       for item in category_pairs):
                    raise ProfileError("消耗品证据类别矩阵无效")
                category_pairs = [[_recipe_int(value) for value in item]
                                  for item in category_pairs]
                addresses["consumable_counts"] = base
                metadata["consumable_mapping"] = {
                    "idMin": id_min, "idMax": id_max, "width": width,
                    "stride": stride,
                    "baseDereference": bool(recipe.get("baseDereference", False)),
                    "itemIds": item_ids,
                    "categoryPairs": category_pairs,
                }
            else:
                raise ProfileError("%s证据配方类型不受支持: %s" % (label, kind))
            capabilities[key] = _capability(key, True, recipe=recipe)
        except (KeyError, TypeError, ValueError, ProfileError) as exc:
            capabilities[key] = capabilities[key].with_state(
                CapabilityState.UNAVAILABLE,
                reason="%s证据配方无法应用: %s" % (label, exc),
                reason_code="EVIDENCE_RECIPE_REJECTED")


CORE_PERSON_PATTERN = (
    "55 8B EC 0F B7 4D 08 6B C9 48 03 0D ?? ?? ?? ?? 5D C2 04 00")
CORE_PERSON_LEGACY_PATTERN = "6B ?? 48 03 ?? 00 EA 4C 00"
CORE_BATTLE_PATTERNS = (
    "55 8B EC 0F B6 C9 6B C9 30 81 C1 ?? ?? ?? ?? 5D C3",
    "55 8B EC 0F B6 C9 6B C9 24 81 C1 ?? ?? ?? ?? 5D C3",
    "6B ?? 24 81 C1 50 2C 4B 00",
)
CORE_FATAL_PATTERN = (
    "55 8B EC 83 EC 0C 54 6A 02 6A 04 FF 75 08 E8 ?? ?? ?? ?? "
    "0F B6 45 F4 3C 50 73 25 6B C8 10 03 0D ?? ?? ?? ?? "
    "8A 45 F6 6B D0 03 03 CA 8B 55 FA 3C 05")


def verify_engine_identity(
        exe_path: str, image: Optional[PEImage] = None) -> Dict[str, object]:
    """Inspect architecture and independent cores without gating attach."""
    disk_image = PEImage(exe_path)
    scan_image = image if image is not None else disk_image
    if not disk_image.is_x86 or not scan_image.is_x86:
        raise ProfileError("目标主模块不是 32 位 x86 PE 程序")
    person_hits = scan_image.find_signature(MaskedSignature.parse(
        CORE_PERSON_PATTERN))
    if not person_hits:
        person_hits = scan_image.find_signature(MaskedSignature.parse(
            CORE_PERSON_LEGACY_PATTERN))
    battle_hits = []
    for pattern in CORE_BATTLE_PATTERNS:
        battle_hits.extend(scan_image.find_signature(
            MaskedSignature.parse(pattern)))
    effect_hits = []
    for pattern in (EFFECT_LAYOUT_16_INITIALIZER_PATTERN,
                    EFFECT_LAYOUT_8_INITIALIZER_PATTERN):
        effect_hits.extend(scan_image.find_signature(
            MaskedSignature.parse(pattern)))
    fatal_hits = scan_image.find_signature(
        MaskedSignature.parse(CORE_FATAL_PATTERN))
    checks = {
        "pe32_x86": True,
        "person_index": bool(person_hits),
        "battle_index": bool(battle_hits),
        "person_hits": tuple(person_hits),
        "battle_hits": tuple(battle_hits),
        "effect_hits": tuple(effect_hits),
        "fatal_hits": tuple(fatal_hits),
        "core_signature_passed": bool(person_hits) and bool(battle_hits),
        "runtime_anchor_found": bool(
            person_hits or battle_hits or effect_hits or fatal_hits),
        "source": getattr(scan_image, "source", "disk"),
        "sha256": disk_image.sha256,
    }
    if not checks["core_signature_passed"]:
        checks["reason"] = (
            "曹操传核心指纹仅作诊断：人物索引 %d，战场索引 %d" %
            (len(person_hits), len(battle_hits)))
    return checks


def detect_engine(exe_path: str, version_part: Optional[int] = None,
                  image: Optional[PEImage] = None) -> EngineProfile:
    if version_part is not None and int(version_part) != EngineVersion.V66:
        raise ProfileError("detect_engine仅解析6.6；旧版请使用组合画像入口")
    disk_image = PEImage(exe_path)
    image = image if image is not None else disk_image
    if not disk_image.is_x86 or not image.is_x86:
        raise ProfileError("目标主模块不是 32 位 x86 程序")
    sha = disk_image.sha256
    known = _KNOWN_66_IDENTITIES.get(sha)
    if known and os.path.getsize(exe_path) != known[1]:
        raise ProfileError("已知基底的文件长度与 SHA 身份不一致")
    variant = known[0] if known else "未知 6.6 派生版"
    addresses = {
        "battle_units": 0x004A7B20,
        "person_pointer": 0x004CEA00,
        "person_r": 0x0050F800,
        "person_s": 0x00501000,
        "person_face": 0x0050F000,
        "merit": 0x00508000,
        "warehouse": 0x004B0783,
        "money": 0x004B077C,
        "alignment": 0x004B0782,
        "merit_pool": 0x00505F40,
        "save_state": 0x004B0770,
        "battle_state": 0x004B3D08,
        "battle_sp": 0x00501C00,
        "job_names": 0x005000D0,
        "item_table": 0,
        "effect_assign_pointer": 0x00500C3B,
        "effect_assign": 0,
        "exclusive_set": 0x0050E000,
        "fatal_pointer": 0x00500C37,
        "bool_vars": 0x00492FC8,
        "int_vars": 0x00502000,
        "ptr_vars": 0x00506000,
        "consumable_counts": 0,
    }
    limits = {
        "battle_ours": 20,
        "battle_friends": 40,
        "battle_enemies": 190,
        "battle_stride": 0x30,
        "person_count": 1024,
        "person_stride": 0x48,
        "merit_count": 102,
        "warehouse_count": 200,
        "item_count": ITEM_RECORD_COUNT_66,
        "item_record_count": ITEM_RECORD_COUNT_66,
        "equipment_count": EQUIPMENT_COUNT_66,
        "item_stride": 0x19,
        "item_name_size": 15,
        "effect_count": 255,
        "effect_name_count": EFFECT_NAME_RECORD_COUNT_65_66,
        "effect_stride": EFFECT_ROW_SIZE_66,
        "effect_character_slots": EFFECT_CHARACTER_SLOTS_66,
        "effect_job_slots": EFFECT_JOB_SLOTS_66,
        "effect_value_per_slot": True,
        "effect_job_count": EFFECT_JOB_COUNT_66,
        "fatal_count": 80,
        "fatal_stride": 16,
        "variable_count": 4096,
    }

    capabilities: Dict[str, FeatureCapability] = {}
    patches: Dict[str, PatchSpec] = {}
    dynamic_patches: Dict[str, DynamicPatchSpec] = {}
    metadata: Dict[str, object] = {
        "image_source": getattr(image, "source", "disk"),
        "identity_diagnostics": verify_engine_identity(exe_path, image),
        "item_layout": {
            "lowFile": "Data.e5", "lowOffset": ITEM_LOW_OFFSET_66,
            "lowCount": ITEM_LOW_COUNT_66,
            "highFile": "Star.e5", "highOffset": ITEM_HIGH_OFFSET_66,
            "highCount": ITEM_HIGH_COUNT_66,
            "rowSize": ITEM_ROW_SIZE_66,
        },
        "consumable_item_ids": list(range(
            CONSUMABLE_ID_MIN_66, CONSUMABLE_ID_MAX_66 + 1)),
        "effect_empty_character": EFFECT_EMPTY_CHARACTER_66,
        "effect_empty_characters": tuple(
            EFFECT_EMPTY_CHARACTER_66
            for _ in range(EFFECT_CHARACTER_SLOTS_66)),
        "auto_return_layout": {},
        "variable_layouts": {
            "variables_bool": {
                "addressKey": "bool_vars", "countOffset": None,
                "dataOffset": 0, "stride": 1, "width": 1,
                "signed": False, "storage": "bitset",
                "bitOrder": "msb0", "storageBytes": 512,
            },
            "variables_int": {
                "addressKey": "int_vars", "countOffset": None,
                "dataOffset": 0, "stride": 4, "width": 4,
                "signed": True,
            },
            "variables_ptr": {
                "addressKey": "ptr_vars", "countOffset": None,
                "dataOffset": 0, "stride": 4, "width": 4,
                "signed": True,
            },
        },
    }
    try:
        _register_effect_name_catalog_65_66(image, addresses, metadata)
    except ProfileError as exc:
        metadata["effect_name_error"] = str(exc)
    auto_patch, auto_contract, auto_reason = locate_auto_return_patch_66(image)
    if auto_patch is not None:
        patches[auto_patch.key] = auto_patch
        metadata["auto_return_layout"].update(auto_contract)
    consumable_spec, consumable_reason = locate_consumable_mapping_composite(
        image, EngineVersion.V66)
    consumable_base = consumable_spec.base if consumable_spec is not None else None
    consumable_mapping = (consumable_spec.as_mapping()
                          if consumable_spec is not None else {})
    if consumable_spec is not None:
        addresses["consumable_counts"] = consumable_spec.base
        metadata["consumable_mapping_spec"] = consumable_spec
        metadata["consumable_mapping"] = consumable_mapping
        metadata["consumable_item_ids"] = list(consumable_spec.ui_item_ids)
        limits.update({
            "equipment_count": consumable_spec.equipment_count,
            "consumable_id_min": consumable_spec.id_min,
            "consumable_ui_id_max": consumable_spec.id_max,
            "consumable_storage_count": consumable_spec.storage_count,
        })
    item_row_codec, item_runtime_base, item_codec_hits, item_codec_reason = (
        locate_item_row_codec(image))
    item_metadata_ok = False
    item_metadata_reason = item_codec_reason
    if item_row_codec is not None and item_runtime_base is not None:
        addresses["item_table"] = item_runtime_base
        limits["item_name_size"] = (
            0x11 if item_row_codec == ITEM_CODEC_65 else 0x0F)
        metadata["item_row_codec"] = item_row_codec
        metadata["item_layout"].update({
            "codec": item_row_codec,
            "nameSize": limits["item_name_size"],
            "runtimeTableBase": item_runtime_base,
            "lookupAddress": item_codec_hits.get("lookup", 0),
        })
        try:
            metadata["item_table_66"] = load_item_table_66(
                exe_path, item_row_codec)
            item_metadata_ok = True
            item_metadata_reason = ""
        except ProfileError as exc:
            item_metadata_reason = str(exc)
    for key, label in (
        ("control_all_factions", "不分敌我"),
        ("control_friendly", "敌友军可控"),
        ("control_no_ai", "禁止AI"),
        ("control_move_after_wait", "待命后移动"),
        ("control_move_255", "移动力255"),
        ("control_pass_through", "穿越移动"),
    ):
        patch, reason = _locate_patch(image, key, label)
        capabilities[key] = _capability(
            key, patch is not None, reason,
            recipe=patch.recipe() if patch is not None else None)
        if patch:
            patches[key] = patch

    # These structure/function signatures are deliberately independent of SHA.
    # The third tuple member is the operand offset. -4 means the final four
    # bytes of the signature; None means the signature identifies a function.
    structural = {
        "battle": (
            "55 8B EC 0F B6 C9 6B C9 30 81 C1 ?? ?? ?? ?? 5D C3", 11),
        "person": (
            "55 8B EC 0F B7 4D 08 6B C9 48 03 0D ?? ?? ?? ?? 5D C2 04 00", 12),
        "person_r": (
            "55 8B EC 51 E8 ?? ?? ?? ?? 0F B7 04 45 ?? ?? ?? ?? 59 5D C3", 13),
        "person_s": (
            "68 00 00 03 00 68 ?? ?? ?? ?? 8D 4D F8 E8 ?? ?? ?? ?? 01 45 FC", 6),
        "person_face": (
            "55 8B EC E8 ?? ?? ?? ?? 0F B7 04 45 ?? ?? ?? ?? 85 C0 74 03 83 C0 07", 12),
        "fatal": (
            CORE_FATAL_PATTERN, 32),
        "recalculate": (
            "55 8B EC FF 71 1B FF 71 1F FF 35 F8 5F 50 00 33 D2 3B 4D 08", None),
        "revive": (
            "55 8B EC 6A 00 83 EC 34 8B 4D 08 E8 ?? ?? ?? ?? 85 C0 75 11 81 F9 00 04 00 00", None),
        "weather": (
            "55 8B EC 51 0F B6 51 11 6B C2 06 8A 55 08 8A 94 10 00 E1 48 00", None),
        "effect": (
            "55 8B EC 51 E8 ?? ?? ?? ?? 50 81 65 08 FF 00 00 00 FF 75 FC FF 75 10 FF 75 08 8B 15 ?? ?? ?? ?? E8 ?? ?? ?? ?? 50 FF 75 10", 28),
        "variables_bool_anchor": (
            "5D C2 0C 00 55 8B EC 68 ?? ?? ?? ?? FF 75 0C FF 45 08 FF 75 08 8B 4D FC", 8),
        "variables_bool_storage": (
            "55 8B EC 0F B7 45 08 C1 F8 03 50 0F B7 45 08 99 33 C2 "
            "83 E0 07 33 C2 85 C0 74 03 FF 45 FC 58 5D C2 04 00", None),
        "turn_refresh_direction": (
            "55 8B EC 6A 00 6A 00 83 EC 10 8B 4D 08 E8 ?? ?? ?? ?? 85 C0 74 04 8B C8", None),
    }
    located: Dict[str, int] = {}
    extracted: Dict[str, int] = {}
    for key, (pattern, operand_offset) in structural.items():
        signature = MaskedSignature.parse(pattern)
        hits = image.find_signature(signature)
        if len(hits) == 1:
            located[key] = hits[0]
            details = {"signature": hits[0]}
            if operand_offset is not None:
                if operand_offset < 0:
                    operand_offset = len(signature.pattern) + operand_offset
                operand = struct.unpack("<I", image.read_va(
                    hits[0] + operand_offset, 4))[0]
                extracted[key] = operand
                details["operand"] = operand
            capabilities[key] = _capability(
                key, True,
                recipe={
                    "kind": "masked-signature",
                    "pattern": pattern,
                    "operandOffset": operand_offset,
                }, **details)
        else:
            capabilities[key] = _capability(
                key, False, "%s结构签名匹配数为 %d" % (key, len(hits)))

    effect_query_candidates = (
        ("standard-4+2", EFFECT_LAYOUT_16_QUERY_PATTERN),
        ("absolute-counter-4+2",
         EFFECT_LAYOUT_16_ABSOLUTE_COUNTER_QUERY_PATTERN),
    )
    effect_query_matches = []
    effect_query_counts = {}
    for variant_key, pattern in effect_query_candidates:
        hits = image.find_signature(MaskedSignature.parse(pattern))
        effect_query_counts[variant_key] = len(hits)
        effect_query_matches.extend(
            (variant_key, pattern, address) for address in hits)
    if len(effect_query_matches) == 1:
        variant_key, pattern, address = effect_query_matches[0]
        structural["effect_query_layout"] = (pattern, None)
        located["effect_query_layout"] = address
        capabilities["effect_query_layout"] = _capability(
            "effect_query_layout", True,
            recipe={
                "kind": "masked-signature-variant",
                "variant": variant_key,
                "pattern": pattern,
            }, signature=address)
        metadata["effect_query_variant"] = variant_key
        metadata["effect_query_candidate_counts"] = effect_query_counts
    else:
        # Keep a deterministic pattern for the diagnostic resolver below. The
        # capability remains unavailable because the candidate set is ambiguous.
        structural["effect_query_layout"] = (
            EFFECT_LAYOUT_16_QUERY_PATTERN, None)
        reason = "特效16字节reader候选总命中数为%d（%s）" % (
            len(effect_query_matches), ", ".join(
                "%s=%d" % item for item in effect_query_counts.items()))
        capabilities["effect_query_layout"] = _capability(
            "effect_query_layout", False, reason)
        metadata["effect_query_candidate_counts"] = effect_query_counts

    if capabilities["battle"].statically_verified:
        addresses["battle_units"] = extracted["battle"]
    if capabilities["person"].statically_verified:
        addresses["person_pointer"] = extracted["person"]
    if capabilities["person_r"].statically_verified:
        addresses["person_r"] = extracted["person_r"]
    if capabilities["person_s"].statically_verified:
        addresses["person_s"] = extracted["person_s"]
    if capabilities["person_face"].statically_verified:
        addresses["person_face"] = extracted["person_face"]
    if capabilities["fatal"].statically_verified:
        addresses["fatal_pointer"] = extracted["fatal"]
    effect_table_layout: Optional[TableLayoutSpec] = None
    effect_descriptor: Optional[ResolvedTableDescriptor] = None
    semantic_resolver = SemanticTableResolver(image)
    fields_16 = effect_layout_16(
        addresses["effect_assign_pointer"], limits["effect_count"]).fields
    effect_descriptor, semantic_hits, semantic_reason_16 = semantic_resolver.resolve(
        "effect", limits["effect_count"], 16, fields_16, (
            SemanticAnchorPattern(
                SemanticAnchorRole.INITIALIZER,
                EFFECT_LAYOUT_16_INITIALIZER_PATTERN, 6),
            SemanticAnchorPattern(
                SemanticAnchorRole.WRITER, structural["effect"][0], 28),
            SemanticAnchorPattern(
                SemanticAnchorRole.READER,
                structural["effect_query_layout"][0]),
        ), person_domain=(0, limits["person_count"] - 1),
        job_domain=(0, EFFECT_JOB_COUNT_66 - 1),
        empty_predicate="character>=person_count;job>=job_count")
    if effect_descriptor is not None:
        addresses["effect_assign_pointer"] = effect_descriptor.pointer_reference
        effect_table_layout = effect_descriptor.to_layout(
            "effect-4+2-v1", metadata={
                "characterSlots": 4, "jobSlots": 2,
                "valuePerSlot": True,
                "canonicalCharacterEmpty": limits["person_count"],
                "canonicalJobEmpty": 255,
            })
        capabilities["effect"] = _capability(
            "effect", True,
            recipe={
                "kind": "effect-assignment-semantic-16",
                "pointerAddress": effect_descriptor.pointer_reference,
                "rowSize": 16, "recordCount": limits["effect_count"],
                "anchorHits": semantic_hits,
                "structureFingerprint":
                    effect_descriptor.structure_fingerprint,
            }, **semantic_hits)
    else:
        fields_8 = effect_layout_8(
            addresses["effect_assign_pointer"],
            limits["effect_count"]).fields
        effect_descriptor, semantic_hits, semantic_reason_8 = (
            semantic_resolver.resolve(
                "effect", limits["effect_count"], 8, fields_8, (
                    SemanticAnchorPattern(
                        SemanticAnchorRole.INITIALIZER,
                        EFFECT_LAYOUT_8_INITIALIZER_PATTERN, 10),
                    SemanticAnchorPattern(
                        SemanticAnchorRole.WRITER,
                        EFFECT_LAYOUT_8_WRITE_PATTERN, 13),
                    SemanticAnchorPattern(
                        SemanticAnchorRole.READER,
                        EFFECT_LAYOUT_8_QUERY_PATTERN),
                ), person_domain=(0, limits["person_count"] - 1),
                job_domain=(0, EFFECT_JOB_COUNT_66 - 1),
                empty_predicate="character>=person_count;job>=job_count"))
        if effect_descriptor is not None:
            addresses["effect_assign_pointer"] = effect_descriptor.pointer_reference
            limits.update({
                "effect_stride": 8,
                "effect_character_slots": 3,
                "effect_job_slots": 1,
                "effect_value_per_slot": False,
            })
            effect_table_layout = effect_descriptor.to_layout(
                "effect-3+1-v1", metadata={
                    "characterSlots": 3, "jobSlots": 1,
                    "valuePerSlot": False,
                    "canonicalCharacterEmpty": limits["person_count"],
                    "canonicalJobEmpty": 255,
                })
            capabilities["effect"] = _capability(
                "effect", True,
                recipe={
                    "kind": "effect-assignment-semantic-8",
                    "pointerAddress": effect_descriptor.pointer_reference,
                    "rowSize": 8, "recordCount": limits["effect_count"],
                    "anchorHits": semantic_hits,
                    "structureFingerprint":
                        effect_descriptor.structure_fingerprint,
                }, **semantic_hits)
        else:
            capabilities["effect"] = _capability(
                "effect", False,
                "特效分配布局未识别；16字节：%s；8字节：%s" %
                (semantic_reason_16, semantic_reason_8))
    if effect_table_layout is not None:
        metadata.setdefault("table_layouts", {})["effect"] = effect_table_layout
        metadata["effect_layout_codec"] = effect_table_layout.codec
        metadata.setdefault("table_descriptors", {})["effect"] = effect_descriptor
    bool_layout_ok = all(capabilities[key].statically_verified for key in
                         ("variables_bool_anchor", "variables_bool_storage"))
    if bool_layout_ok:
        addresses["bool_vars"] = extracted["variables_bool_anchor"] + 0x90

    person_tables_ok = all(capabilities[key].statically_verified for key in
                           ("person", "person_r", "person_s", "person_face"))
    merit_refs = image.find_literal_references(addresses["merit"])
    if len(merit_refs) != 2:
        person_tables_ok = False
    if not person_tables_ok:
        capabilities["person"] = _capability(
            "person", False, "人物数组、R/S/头像或功勋表签名未全部通过")

    exclusive_refs = image.find_literal_references(addresses["exclusive_set"])
    capabilities["effect_exclusive"] = _capability(
        "effect_exclusive", len(exclusive_refs) == 5,
        "专属/套装表引用数为 %d" % len(exclusive_refs)
        if len(exclusive_refs) != 5 else "",
        recipe={"kind": "exclusive-set-table", "address":
                addresses["exclusive_set"], "rowSize": 16,
                "referenceCount": len(exclusive_refs)})
    warehouse_refs_ok = all(image.find_literal_references(addresses[key])
                            for key in ("warehouse", "money", "merit_pool"))
    warehouse_refs_ok = (warehouse_refs_ok and
                         addresses["alignment"] == addresses["money"] + 6)
    capabilities["warehouse"] = _capability(
        "warehouse", bool(warehouse_refs_ok),
        "仓库表、金钱或功勋池引用未通过"
        if not warehouse_refs_ok else "",
        recipe={
            "kind": "runtime-tables",
            "battleStride": limits["battle_stride"],
            "itemStride": limits["item_stride"],
            "warehouseStride": 3,
            "warehouseRefs": bool(warehouse_refs_ok),
        })
    capabilities["item_metadata"] = _capability(
        "item_metadata", item_metadata_ok,
        item_metadata_reason if not item_metadata_ok else "",
        recipe={
            "kind": "split-file-item-table",
            "lowFile": "Data.e5", "lowOffset": ITEM_LOW_OFFSET_66,
            "lowCount": ITEM_LOW_COUNT_66,
            "highFile": "Star.e5", "highOffset": ITEM_HIGH_OFFSET_66,
            "highCount": ITEM_HIGH_COUNT_66,
            "rowSize": ITEM_ROW_SIZE_66,
            "nameSize": metadata["item_layout"].get("nameSize", 0),
            "codec": item_row_codec,
            "runtimeTableBase": item_runtime_base,
            "lookupAddress": item_codec_hits.get("lookup", 0),
        })
    variable_results = {}
    for address_key, capability_key in (
            ("bool_vars", "variables_bool"),
            ("int_vars", "variables_int"),
            ("ptr_vars", "variables_ptr")):
        if address_key == "bool_vars":
            available = bool_layout_ok
        else:
            available = bool(image.find_literal_references(addresses[address_key]))
        variable_results[capability_key] = available
        capabilities[capability_key] = _capability(
            capability_key, available,
            "%s基址引用尚未通过" % address_key if not available else "",
            recipe={
                "kind": "variable-table", "addressKey": address_key,
                "count": limits["variable_count"],
                **metadata["variable_layouts"][capability_key],
            })
    variable_refs_ok = all(variable_results.values())
    capabilities["variables"] = _capability(
        "variables", variable_refs_ok,
        "变量基址引用尚未通过" if not variable_refs_ok else "",
        recipe={"kind": "variable-groups", "groups": 3,
                "count": limits["variable_count"]})
    capabilities["diy"] = _capability(
        "diy", True,
        recipe={"kind": "validated-custom-write", "widths": [1, 2, 4],
                "pageCheck": True, "readback": True})
    capabilities["item_bulk_treasure"] = _capability(
        "item_bulk_treasure",
        (capabilities["warehouse"].statically_verified and
         capabilities["item_metadata"].statically_verified),
        "6.6仓库或物品文件表语义探针未通过"
        if not (capabilities["warehouse"].statically_verified and
                capabilities["item_metadata"].statically_verified) else "",
        recipe={"kind": "treasure-filter", "rowStride": 0x19,
                 "catalogOffset": 0x18, "catalogValue": 1,
                "equipmentCount": limits["equipment_count"],
                 "warehouseCount": 200,
                 "itemSource": "Data.e5[0..103]+Star.e5[104..254]"})
    capabilities["item_consumables"] = _capability(
        "item_consumables", consumable_base is not None,
        consumable_reason if consumable_base is None else "",
        reason_code=("CONSUMABLE_SIGNATURE_MISMATCH"
                     if consumable_base is None else ""),
        recipe={
            "kind": "consumable-count-array",
            "countBase": consumable_base,
            **consumable_mapping,
        } if consumable_base is not None else None)
    capabilities["control_auto_return"] = _capability(
        "control_auto_return", auto_patch is not None,
        auto_reason,
        reason_code=("AUTO_RETURN_STATIC_NOT_LOCATED" if auto_reason else ""),
        recipe={
            **auto_patch.recipe(),
            "mode": auto_contract["mode"],
            "callSites": ["0x%08X" % value
                          for value in auto_contract["callSites"]],
            "nativeEffect": "0x%08X" % auto_contract["nativeEffect"],
            "patchSites": ["0x%08X" % value
                           for value in auto_contract["patchSites"]],
        } if auto_patch is not None else None)
    turn_located = capabilities["turn_refresh_direction"].statically_verified
    if turn_located:
        capabilities["turn_refresh_direction"] = _capability(
            "turn_refresh_direction", True,
            recipe={
                "kind": "remote-call",
                "address": located["turn_refresh_direction"],
                "callingConvention": "stdcall",
                "arguments": [
                    "data_id:u32", "sentinel:u32", "direction:u32",
                    "reserved:u32", "refresh:u32", "redraw:u32",
                ],
                "calleeCleanup": 24,
                "wrapperKind": "turn-66-six-args-v1",
            })
    capabilities["turn_refresh_position"] = _capability(
        "turn_refresh_position", False, "6.6坐标刷新ABI尚未动态验证",
        reason_code="POSITION_ABI_NOT_VERIFIED")

    remote_calls = {
        "recalculate": RemoteCallSpec(
            "recalculate", "人物能力重算", located.get("recalculate", 0x004075DF),
            b"\x55\x8B\xEC\xFF\x71\x1B", capabilities["recalculate"].runtime_verified,
            structural["recalculate"][0], capabilities["recalculate"].recipe_id,
            "thiscall", "ECX=人物记录", ("data_id:u16",), 0,
            ("人物指针有效",), ("人物派生能力已重算",)),
        "revive": RemoteCallSpec(
            "revive", "战场复活", located.get("revive", 0x004092C0),
            b"\x55\x8B\xEC\x6A\x00\x83\xEC\x34", capabilities["revive"].runtime_verified,
            structural["revive"][0], capabilities["revive"].recipe_id,
            "stdcall", "", ("direction:u32", "x:u32", "y:u32", "data_id:u32"),
            16, ("目标槽为空且坐标有效",), ("单位结构和地图占用已建立",)),
        "weather": RemoteCallSpec(
            "weather", "天气刷新", located.get("weather", 0x0041D9D1),
            b"\x55\x8B\xEC\x51", capabilities["weather"].runtime_verified,
            structural["weather"][0], capabilities["weather"].recipe_id,
            "thiscall", "ECX=天气上下文", ("weather:u8",), 0,
            ("天气值在有效范围",), ("天气状态和画面已刷新",)),
        "turn_refresh_direction": RemoteCallSpec(
            "turn_refresh_direction", "转向刷新",
            located.get("turn_refresh_direction", 0),
            b"\x55\x8B\xEC\x6A\x00\x6A\x00\x83" if turn_located else b"",
            turn_located,
            structural["turn_refresh_direction"][0],
            capabilities["turn_refresh_direction"].recipe_id,
            "stdcall", "",
            ("data_id:u32", "sentinel:u32", "direction:u32",
             "reserved:u32", "refresh:u32", "redraw:u32"),
            24, ("战场单位有效",), ("方向已立即刷新",), 0,
            "turn-66-six-args-v1"),
        "turn_refresh_position": RemoteCallSpec(
            "turn_refresh_position", "坐标刷新", 0, b"", False),
    }
    _materialize_verified_recipes(
        image, capabilities, patches, dynamic_patches, remote_calls,
        addresses, limits, metadata)
    table_layouts = metadata.setdefault("table_layouts", {})
    table_layouts.update(data_layouts_66(addresses, limits))
    if effect_table_layout is not None:
        table_layouts["effect"] = effect_table_layout
    person_layout = table_layouts["person"]
    person_descriptor, person_semantic_hits, person_semantic_reason = (
        semantic_resolver.resolve(
            "person", limits["person_count"], limits["person_stride"],
            person_layout.fields, (
                SemanticAnchorPattern(
                    SemanticAnchorRole.INITIALIZER,
                    PERSON_INITIALIZER_PATTERN_66, 9),
                SemanticAnchorPattern(
                    SemanticAnchorRole.READER, structural["person"][0], 12),
                SemanticAnchorPattern(
                    SemanticAnchorRole.WRITER,
                    PERSON_WRITER_PATTERN_66, 2),
            ), person_domain=(0, limits["person_count"] - 1),
            empty_predicate="character>=person_count"))
    if person_descriptor is not None:
        table_layouts["person"] = person_descriptor.to_layout(
            person_layout.codec, metadata={
                "anchorHits": person_semantic_hits,
                "personDomain": (0, limits["person_count"] - 1),
            })
        metadata.setdefault("table_descriptors", {})["person"] = (
            person_descriptor)
    else:
        table_layouts["person"] = replace(
            person_layout, writable=False,
            metadata=dict(person_layout.metadata,
                          semanticReason=person_semantic_reason))
    resolved_features: Dict[str, ResolvedFeature] = {}
    for key, layout in table_layouts.items():
        capability = capabilities.get(key)
        if capability is None:
            continue
        access = (FeatureAccess.READ_ONLY if capability.statically_verified
                  else FeatureAccess.UNAVAILABLE)
        resolved_features[key] = ResolvedFeature(
            key, access, capability.recipe_id, layout,
            structure_fingerprint=layout.structure_fingerprint,
            reason=capability.reason, reason_code=capability.reason_code,
            details={"tableDescriptor":
                     metadata.get("table_descriptors", {}).get(key)})
    for key, call in remote_calls.items():
        capability = capabilities.get(key)
        if capability is None:
            continue
        access = (FeatureAccess.READ_WRITE if call.abi_verified and call.address
                  else FeatureAccess.UNAVAILABLE)
        resolved_features[key] = ResolvedFeature(
            key, access, capability.recipe_id,
            resolved_address=call.address if access.writable else 0,
            reason=(capability.reason if access.writable else
                    (capability.reason or "远程调用 ABI 尚未完整验证")),
            reason_code=(capability.reason_code if access.writable else
                         "REMOTE_ABI_NOT_VERIFIED"),
            runtime_probe_passed=False)
    return EngineProfile(
        6, "6.6", variant, sha, os.path.getsize(exe_path),
        disk_image.image_base, disk_image.size_of_image,
        addresses, limits, capabilities, patches, remote_calls,
        dynamic_patches=dynamic_patches, metadata=metadata,
        force_unverified_66=False, resolved_features=resolved_features)


def legacy_profile(version: int) -> EngineProfile:
    """Preserve the old 6.1-6.5 branches while using MemorySession."""
    names = {0: "6.4", 1: "6.3 MP+", 2: "6.3", 3: "6.2",
             4: "6.1", 5: "6.5"}
    if version not in names:
        raise ProfileError("不支持的旧引擎版本")
    expanded = version in (EngineVersion.V64, EngineVersion.V65)
    pointer_effect = version == EngineVersion.V65
    addresses = {
        "battle_units": 0x004A7B20 if expanded else 0x004B2C50,
        "person_pointer": 0x004CEA00,
        "person_r": 0x0050F800,
        "person_s": 0x00501000,
        "person_face": 0x0050F000,
        "merit": 0x00508000 if expanded else 0x00508400,
        "warehouse": 0x004B0783,
        "money": 0x004B077C,
        "alignment": 0x004B0782,
        "merit_pool": 0x00505F40,
        "save_state": 0x004B0770,
        "battle_state": 0x004B3D08,
        "battle_sp": 0x00501C00,
        "job_names": 0x005000D0,
        "item_table": 0x004A1140,
        "effect_assign": (0 if pointer_effect else
                          0x00508998 if expanded else 0x005089B0),
        "exclusive_set": (0x0050E800 if version in
                          (EngineVersion.V62, EngineVersion.V61)
                          else 0x0050E400),
        "fatal_table": 0x00511800 if expanded else 0x00508800,
        "bool_vars": 0x00492FC8,
        "int_vars": 0x00502000,
        "ptr_vars": 0x00506000,
    }
    if pointer_effect:
        addresses["effect_assign_pointer"] = 0x00500C3B
    if expanded:
        addresses["consumable_counts"] = 0x00510C80
    limits = {
        "battle_ours": 20 if expanded else 16,
        "battle_friends": 40 if expanded else 19,
        "battle_enemies": 190 if expanded else 80,
        "battle_stride": 0x30 if expanded else 0x24,
        "person_count": 1024,
        "person_stride": 0x48,
        "merit_count": 102,
        "warehouse_count": 200,
        "item_count": 255 if expanded else 154,
        "item_record_count": 255 if expanded else 154,
        "equipment_count": 150 if expanded else 154,
        "item_stride": 0x19,
        "item_name_size": 12,
        "effect_count": 180 if expanded else 144,
        "effect_name_count": (EFFECT_NAME_RECORD_COUNT_65_66
                              if pointer_effect else
                              180 if expanded else 144),
        "effect_record_count": 255 if pointer_effect else
                               180 if expanded else 144,
        "effect_stride": 8,
        "effect_character_slots": 3,
        "effect_job_slots": 1,
        "effect_value_per_slot": False,
        "effect_job_count": 80,
        "fatal_count": 80 if expanded else 36,
        "fatal_stride": 16,
        "variable_count": 4096,
    }
    if expanded:
        limits.update({
            "consumable_id_min": CONSUMABLE_ID_MIN_LEGACY_EXPANDED,
            "consumable_ui_id_max": CONSUMABLE_ID_MAX_LEGACY_EXPANDED,
            "consumable_storage_count":
                CONSUMABLE_STORAGE_COUNT_LEGACY_EXPANDED,
        })
    legacy_keys = (
        "battle", "person", "warehouse", "variables", "variables_bool",
        "variables_int", "variables_ptr", "diy", "fatal",
        "effect", "effect_exclusive", "recalculate", "revive", "weather",
        "turn_refresh_direction", "turn_refresh_position",
        "item_bulk_treasure", "item_consumables", "control_friendly",
        "control_all_factions", "control_no_ai", "control_move_after_wait",
        "control_move_255", "control_pass_through", "control_auto_return")
    capabilities = {
        key: FeatureCapability(
            key, CapabilityState.ENABLED, recipe_id="legacy.%d.%s" % (version, key))
        for key in legacy_keys
    }
    revive_address = 0x004092C7 if expanded else 0x004092E0
    if version == 4:
        revive_address = 0x0040930F
    remote_calls = {
        "recalculate": RemoteCallSpec("recalculate", "人物能力重算",
                                      0x004075DF, b"", True),
        "revive": RemoteCallSpec("revive", "战场复活",
                                 revive_address, b"", True),
        "weather": RemoteCallSpec("weather", "天气刷新",
                                   0x0041D9D1, b"", True),
        "turn_refresh_direction": RemoteCallSpec(
            "turn_refresh_direction", "转向刷新", 0x00457428, b"", True,
            calling_convention="thiscall", ecx_source="DWORD[0x004B5DF0]",
            arguments=("direction:u32", "data_id:u32", "flags:u32"),
            context_reference=0x004B5DF0, wrapper_kind="legacy-turn-v1"),
        "turn_refresh_position": RemoteCallSpec(
            "turn_refresh_position", "坐标刷新", 0x004594DD, b"", True,
            calling_convention="thiscall", ecx_source="DWORD[0x004B5DF0]",
            arguments=("direction:u32", "y:u32", "x:u32", "data_id:u32"),
            context_reference=0x004B5DF0, wrapper_kind="legacy-position-v1"),
    }
    metadata = {
        "item_record_ids": list(range(limits["item_count"])),
        "equipment_item_ids": list(range(limits["equipment_count"])),
        "variable_layouts": {
            key: {
                "addressKey": address_key, "countOffset": None,
                "dataOffset": 0, "stride": 4, "width": 4,
                "signed": True,
            }
            for key, address_key in (
                ("variables_bool", "bool_vars"),
                ("variables_int", "int_vars"),
                ("variables_ptr", "ptr_vars"),
            )
        },
    }
    effect_reference = (addresses["effect_assign_pointer"] if pointer_effect
                        else addresses["effect_assign"])
    effect_record_count = limits["effect_record_count"]
    if pointer_effect:
        legacy_effect_layout = effect_layout_8(
            effect_reference, effect_record_count,
            fingerprint="legacy-65-pointer-8-pending-static-scan")
    else:
        legacy_effect_layout = TableLayoutSpec(
            "effect", AddressResolverKind.FIXED_RVA,
            effect_reference, effect_record_count, 8,
            effect_layout_8(effect_reference, effect_record_count).fields,
            codec="effect-3+1-v1", semantics_proven=True,
            metadata={"characterSlots": 3, "jobSlots": 1,
                      "valuePerSlot": False, "personDomain": (0, 1023),
                      "jobDomain": (0, 79), "canonicalCharacterEmpty": 1024,
                      "canonicalJobEmpty": 255})
    table_layouts = {"effect": legacy_effect_layout}
    if expanded:
        consumable_spec = ConsumableMappingSpec(
            "legacy-expanded-105-base-profile", 0x00510C80,
            CONSUMABLE_ID_MIN_LEGACY_EXPANDED,
            CONSUMABLE_STORAGE_COUNT_LEGACY_EXPANDED,
            tuple(range(CONSUMABLE_ID_MIN_LEGACY_EXPANDED,
                        CONSUMABLE_ID_MAX_LEGACY_EXPANDED + 1)),
            CONSUMABLE_ID_MIN_LEGACY_EXPANDED)
        metadata["consumable_mapping_spec"] = consumable_spec
        metadata["consumable_mapping"] = consumable_spec.as_mapping()
        metadata["consumable_item_ids"] = list(consumable_spec.ui_item_ids)
        table_layouts["item_consumables"] = TableLayoutSpec(
            "item_consumables", AddressResolverKind.FIXED_RVA,
            consumable_spec.base, consumable_spec.storage_count, 1,
            (FieldSpec("count", 0, 1),), codec="uint8-count-array-v1",
            semantics_proven=True,
            metadata={"idMin": consumable_spec.id_min,
                      "uiIdMax": consumable_spec.id_max})
    metadata["table_layouts"] = table_layouts
    resolved_features = {
        "effect": ResolvedFeature(
            "effect", (FeatureAccess.READ_ONLY if pointer_effect else
                       FeatureAccess.READ_WRITE),
            capabilities["effect"].recipe_id, legacy_effect_layout,
            resolved_address=addresses["effect_assign"],
            reason=("等待附加后解引用6.5特效表" if pointer_effect else ""),
            runtime_probe_passed=not pointer_effect)
    }
    if expanded:
        resolved_features["item_consumables"] = ResolvedFeature(
            "item_consumables", FeatureAccess.READ_WRITE,
            capabilities["item_consumables"].recipe_id,
            table_layouts["item_consumables"],
            resolved_address=addresses["consumable_counts"],
            runtime_probe_passed=False)
    variant = ("6.5独立基础画像" if pointer_effect else
               "6.4独立基础画像" if expanded else "旧版兼容画像")
    return EngineProfile(version, names[version], variant, "", 0,
                         REFERENCE_IMAGE_BASE, 0x200000, addresses, limits,
                         capabilities, {}, remote_calls, metadata=metadata,
                         resolved_features=resolved_features)


def _verified_legacy_capability(key: str, recipe: Dict[str, object]) -> FeatureCapability:
    return FeatureCapability(
        key, CapabilityState.ENABLED, recipe_id=recipe_id(key, recipe),
        recipe=recipe)


def _disable_legacy_feature(profile: EngineProfile, key: str,
                            reason: str, reason_code: str) -> None:
    profile.capabilities[key] = FeatureCapability(
        key, CapabilityState.UNAVAILABLE, reason, reason_code)
    current = profile.resolved_feature(key)
    profile.resolved_features[key] = ResolvedFeature(
        key, FeatureAccess.UNAVAILABLE, current.recipe_id, current.layout,
        reason=reason, reason_code=reason_code)


def _apply_legacy_composite_detection(
        profile: EngineProfile, exe_path: str,
        image: Optional[PEImage] = None) -> EngineProfile:
    disk_image = PEImage(exe_path)
    image = image if image is not None else disk_image
    profile.sha256 = disk_image.sha256
    profile.file_size = os.path.getsize(exe_path)
    profile.preferred_image_base = disk_image.image_base
    profile.image_size = disk_image.size_of_image
    profile.metadata["version_profile"] = profile.display_version
    profile.metadata["image_source"] = getattr(image, "source", "disk")
    profile.metadata["identity_diagnostics"] = verify_engine_identity(
        exe_path, image)

    if profile.version == EngineVersion.V65:
        try:
            _register_effect_name_catalog_65_66(
                image, profile.addresses, profile.metadata)
        except ProfileError as exc:
            profile.addresses.pop("effect_names", None)
            profile.metadata["effect_name_error"] = str(exc)

        item_codec, item_base, item_hits, item_reason = locate_item_row_codec(
            image)
        if item_codec == ITEM_CODEC_65 and item_base is not None:
            profile.metadata["item_row_codec"] = item_codec
            profile.metadata["item_layout"] = {
                "lowFile": "Data.e5", "lowOffset": ITEM_LOW_OFFSET_66,
                "lowCount": ITEM_LOW_COUNT_66,
                "highFile": "Star.e5", "highOffset": ITEM_HIGH_OFFSET_66,
                "highCount": ITEM_HIGH_COUNT_66,
                "rowSize": ITEM_ROW_SIZE_66, "nameSize": 0x11,
                "codec": item_codec, "runtimeTableBase": item_base,
                "lookupAddress": item_hits.get("lookup", 0),
            }
            profile.addresses["item_table"] = item_base
            profile.limits["item_name_size"] = 0x11
        else:
            item_reason = item_reason or (
                "6.5物品索引函数未证明17字节名称布局")
            profile.addresses["item_table"] = 0
        try:
            if item_codec != ITEM_CODEC_65 or item_base is None:
                raise ProfileError(item_reason)
            profile.metadata["item_table_66"] = load_item_table_66(
                exe_path, item_codec)
            item_recipe = {
                "kind": "split-file-item-table",
                "lowFile": "Data.e5", "lowOffset": ITEM_LOW_OFFSET_66,
                "lowCount": ITEM_LOW_COUNT_66,
                "highFile": "Star.e5", "highOffset": ITEM_HIGH_OFFSET_66,
                "highCount": ITEM_HIGH_COUNT_66,
                "rowSize": ITEM_ROW_SIZE_66, "nameSize": 0x11,
                "codec": item_codec, "runtimeTableBase": item_base,
                "lookupAddress": item_hits.get("lookup", 0),
            }
            profile.capabilities["item_metadata"] = (
                _verified_legacy_capability("item_metadata", item_recipe))
            profile.resolved_features["item_metadata"] = ResolvedFeature(
                "item_metadata", FeatureAccess.READ_ONLY,
                profile.capabilities["item_metadata"].recipe_id,
                reason="6.5物品元数据来自Data.e5/Star.e5，只读解析",
                runtime_probe_passed=True)
        except ProfileError as exc:
            profile.metadata.pop("item_table_66", None)
            profile.capabilities["item_metadata"] = FeatureCapability(
                "item_metadata", CapabilityState.UNAVAILABLE, str(exc),
                "ITEM_METADATA_INVALID")
            profile.resolved_features["item_metadata"] = ResolvedFeature(
                "item_metadata", FeatureAccess.UNAVAILABLE,
                reason=str(exc), reason_code="ITEM_METADATA_INVALID")
            _disable_legacy_feature(
                profile, "item_bulk_treasure",
                "6.5物品元数据不可用，禁止一键全宝: %s" % exc,
                "ITEM_METADATA_INVALID")

    if profile.version == EngineVersion.V65:
        layout, hits, effect_reason = locate_pointer_effect_layout_8(image)
        if layout is not None:
            recipe = {
                "kind": "effect-assignment-pointer-8",
                "pointerAddress": layout.reference,
                "recordCount": layout.record_count,
                "rowSize": layout.stride,
                "anchorHits": hits,
                "structureFingerprint": layout.structure_fingerprint,
            }
            profile.addresses["effect_assign_pointer"] = layout.reference
            profile.addresses["effect_assign"] = 0
            profile.limits["effect_record_count"] = layout.record_count
            profile.metadata["table_layouts"]["effect"] = layout
            profile.metadata["effect_layout_codec"] = layout.codec
            profile.capabilities["effect"] = _verified_legacy_capability(
                "effect", recipe)
            profile.resolved_features["effect"] = ResolvedFeature(
                "effect", FeatureAccess.READ_ONLY,
                profile.capabilities["effect"].recipe_id, layout,
                structure_fingerprint=layout.structure_fingerprint,
                reason="6.5指针型8字节布局已定位，等待运行时解引用",
                reason_code="SESSION_PROBE_REQUIRED",
                details={"anchorHits": hits})
        else:
            _disable_legacy_feature(
                profile, "effect", effect_reason,
                "EFFECT_65_SEMANTIC_ANCHOR_MISMATCH")
    elif profile.version == EngineVersion.V64:
        layout, hits, effect_reason = locate_fixed_effect_layout_64(image)
        if layout is not None:
            recipe = {
                "kind": "effect-assignment-fixed-8",
                "address": layout.reference,
                "recordCount": layout.record_count,
                "rowSize": layout.stride,
                "anchorHits": hits,
                "structureFingerprint": layout.structure_fingerprint,
            }
            profile.addresses.pop("effect_assign_pointer", None)
            profile.addresses["effect_assign"] = layout.reference
            profile.limits["effect_record_count"] = layout.record_count
            profile.metadata["table_layouts"]["effect"] = layout
            profile.metadata["effect_layout_codec"] = layout.codec
            profile.capabilities["effect"] = _verified_legacy_capability(
                "effect", recipe)
            profile.resolved_features["effect"] = ResolvedFeature(
                "effect", FeatureAccess.READ_WRITE,
                profile.capabilities["effect"].recipe_id, layout,
                resolved_address=layout.reference,
                structure_fingerprint=layout.structure_fingerprint,
                runtime_probe_passed=True, details={"anchorHits": hits})
        else:
            _disable_legacy_feature(
                profile, "effect", effect_reason,
                "EFFECT_64_SEMANTIC_ANCHOR_MISMATCH")

    consumable_spec, consumable_reason = locate_consumable_mapping_composite(
        image, profile.version)
    if consumable_spec is not None:
        mapping = consumable_spec.as_mapping()
        profile.addresses["consumable_counts"] = consumable_spec.base
        profile.limits.update({
            "equipment_count": consumable_spec.equipment_count,
            "consumable_id_min": consumable_spec.id_min,
            "consumable_ui_id_max": consumable_spec.id_max,
            "consumable_storage_count": consumable_spec.storage_count,
        })
        profile.metadata["consumable_mapping_spec"] = consumable_spec
        profile.metadata["consumable_mapping"] = mapping
        profile.metadata["consumable_item_ids"] = list(
            consumable_spec.ui_item_ids)
        layout = TableLayoutSpec(
            "item_consumables", AddressResolverKind.FIXED_RVA,
            consumable_spec.base, consumable_spec.storage_count,
            consumable_spec.stride,
            (FieldSpec("count", 0, consumable_spec.width),),
            codec="uint8-count-array-v1", semantics_proven=True,
            structure_fingerprint=consumable_spec.structure_fingerprint,
            metadata={"idMin": consumable_spec.id_min,
                      "uiIdMax": consumable_spec.id_max})
        profile.metadata["table_layouts"]["item_consumables"] = layout
        recipe = {"kind": "consumable-count-array", **mapping,
                  "countBase": consumable_spec.base}
        profile.capabilities["item_consumables"] = (
            _verified_legacy_capability("item_consumables", recipe))
        profile.resolved_features["item_consumables"] = ResolvedFeature(
            "item_consumables", FeatureAccess.READ_ONLY,
            profile.capabilities["item_consumables"].recipe_id, layout,
            resolved_address=consumable_spec.base,
            structure_fingerprint=consumable_spec.structure_fingerprint,
            reason="消耗品映射已定位，等待运行时页权限探针",
            reason_code="SESSION_PROBE_REQUIRED")
    else:
        profile.metadata.pop("consumable_mapping_spec", None)
        profile.metadata.pop("consumable_mapping", None)
        profile.metadata["consumable_item_ids"] = []
        profile.addresses.pop("consumable_counts", None)
        _disable_legacy_feature(
            profile, "item_consumables", consumable_reason,
            "CONSUMABLE_SEMANTIC_ANCHOR_MISMATCH")
    return profile


def detect_composite_profile(
        exe_path: str, version_part: Optional[int] = None,
        manual_hint: Optional[int] = None,
        image: Optional[PEImage] = None) -> EngineProfile:
    """Compose a profile without allowing a previous attachment to leak.

    The version resource selects the base family in automatic mode.  A manual
    hint selects a candidate family, while each migrated feature still has to
    pass its own semantic anchors before it becomes writable.
    """
    if manual_hint is None:
        if version_part is None:
            raise ProfileError("自动画像缺少版本资源")
        selected = engine_version_from_resource(int(version_part))
    else:
        selected = EngineVersion(int(manual_hint))
    if selected is EngineVersion.V66:
        return detect_engine(
            exe_path, version_part=EngineVersion.V66, image=image)
    profile = legacy_profile(int(selected))
    if selected in (EngineVersion.V64, EngineVersion.V65):
        return _apply_legacy_composite_detection(
            profile, exe_path, image=image)
    return profile
