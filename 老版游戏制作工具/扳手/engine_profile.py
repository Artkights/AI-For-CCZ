"""Engine identification and capability data for the CaoCaoZhuan modifier.

Addresses in this module are reference virtual addresses (VA) for the original
0x00400000 image.  Runtime users must resolve them through MemorySession.
"""

from __future__ import annotations

import hashlib
import json
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
from runtime_regions import (iter_mapped_regions, iter_runtime_regions,
                             scan_image_regions)


REFERENCE_IMAGE_BASE = 0x00400000
# The script documentation defines 0..255 as local flags and 256..1535 as
# global flags.  The native accessors do not validate an index; bytes from
# index 2048 onward overlap a character DWORD table in the inspected 6.6s.
BOOL_LOCAL_COUNT_66 = 256
BOOL_USER_COUNT_66 = 1536
BOOL_BROWSE_COUNT_66 = 2048
BOOL_USER_BYTES_66 = (BOOL_USER_COUNT_66 + 7) // 8
BOOL_BROWSE_BYTES_66 = (BOOL_BROWSE_COUNT_66 + 7) // 8
BOOL_DOMAIN_BASIS_66 = "剧本资料域：0..255局部、256..1535全局；1536..2047保留只读（非原生总数证明）"
BOOL_DOCUMENTED_COUNT_LEGACY = 1536
BOOL_DWORD_READER_PATTERN = (
    "55 8B EC 8B 45 08 33 D2 83 7C 81 38 00 0F 95 C2 "
    "8B C2 5D C2 04 00")
BOOL_DWORD_WRITER_PATTERN = (
    "55 8B EC 8B 45 08 8B 55 0C 89 54 81 38 5D C2 08 00")
BOOL_DWORD_SAVE_LOOP_PATTERN = (
    "8B 45 FC 66 3D ?? ?? 73 1B 6A 04 8B 4D F0 8D 4C 81 38 "
    "51 FF 75 F8 E8 ?? ?? ?? ?? 83 C4 0C 83 45 F8 04 EB D9 "
    "8B 4D F0 81 C1 ?? ?? ?? ?? 6A 08")
BOOL_DWORD_LOAD_LOOP_PATTERN = (
    "8B 45 FC 66 3D ?? ?? 73 1B 6A 04 FF 75 F4 8B 4D F0 "
    "8D 4C 81 38 51 E8 ?? ?? ?? ?? 83 C4 0C 83 45 F4 04 EB D9 "
    "8B 4D F0 81 C1 ?? ?? ?? ?? 6A 08")
BOOL_SERIALIZER_HEADER_PATTERN = (
    "55 8B EC 6A 00 51 51 68 ?? ?? ?? ?? 83 EC 14")
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
CONSUMABLE_ID_MIN_SINGLE_EXE_66 = 0x100
CONSUMABLE_STORAGE_COUNT_SINGLE_EXE_66 = 0x100
EFFECT_ROW_SIZE_66 = 0x10
EFFECT_CHARACTER_SLOTS_66 = 4
EFFECT_JOB_SLOTS_66 = 2
EFFECT_CHARACTER_COUNT_66 = 1024
EFFECT_EMPTY_CHARACTER_66 = EFFECT_CHARACTER_COUNT_66
EFFECT_JOB_COUNT_66 = 80
ITEM_CODEC_65 = "item-65-17-v1"
ITEM_CODEC_66 = "item-66-15-v1"
ITEM_CODEC_LEGACY = "item-legacy-12-v1"
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
    addresses: Tuple[int, ...] = ()

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
        if self.addresses and (
                len(self.addresses) != len(self.ui_item_ids) or
                any(address <= 0 for address in self.addresses) or
                len(set(self.addresses)) != len(self.addresses)):
            raise ValueError("消耗品显式地址必须与 UI 项目逐项对应且不能重复")

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
            "addresses": list(self.addresses),
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
    # Runtime scans may observe a patch that this session already enabled.
    # These fields describe the observation only; the recipe remains the
    # static original->patched contract so evidence IDs and cross-build
    # fingerprints do not change when a live process is patched.
    observed_state: str = "original"
    observed_site_states: Tuple[str, ...] = ()

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


def _is_runtime_image(image: "PEImage") -> bool:
    """Return whether *image* contains live/mapped runtime bytes.

    Disk PE images intentionally keep strict original-byte recognition.  Both
    ``RuntimeModuleImage`` and ``MappedRuntimeImage`` advertise ``source`` as
    ``runtime`` without making the locator depend on a concrete adapter class.
    """
    return str(getattr(image, "source", "disk")).startswith("runtime")


def _wildcard_patch_signature(pattern: str, ranges: Sequence[Tuple[int, int]]) -> MaskedSignature:
    """Mask bytes occupied by a patch so an already-patched runtime can scan.

    The surrounding instruction bytes remain exact anchors.  A caller still
    validates every candidate's actual bytes against original/patched states,
    so wildcarding cannot turn an unrelated instruction into an accepted
    patch site.
    """
    parsed = MaskedSignature.parse(pattern)
    values = list(parsed.pattern)
    for start, length in ranges:
        start = int(start)
        end = start + int(length)
        if start < 0 or end > len(values):
            return parsed
        for index in range(start, end):
            values[index] = None
    return MaskedSignature(tuple(values))


def _patch_state(current: bytes, original: bytes, patched: bytes) -> str:
    if current == original:
        return "original"
    if current == patched:
        return "patched"
    return "foreign"


def _aggregate_patch_state(states: Sequence[str]) -> str:
    values = tuple(states)
    if values and all(item == "original" for item in values):
        return "original"
    if values and all(item == "patched" for item in values):
        return "patched"
    if any(item == "foreign" for item in values):
        return "foreign"
    return "partial"


def locate_auto_return_patch_66(image: "PEImage") -> Tuple[
        Optional[PatchSpec], Dict[str, object], str]:
    """Locate the native second-action gates used by 6.6 auto return."""
    runtime = _is_runtime_image(image)
    patterns = {
        "gate": AUTO_RETURN_GATE_PATTERN_66,
        "state": AUTO_RETURN_STATE_PATTERN_66,
        "effect": AUTO_RETURN_EFFECT_PATTERN_66,
    }
    # The normal signatures include the two-byte conditional branches that
    # this feature replaces.  A live process may already contain NOPs, so the
    # runtime fallback masks only those branch bytes and keeps all surrounding
    # instruction bytes exact.
    patch_ranges = {
        "gate": ((0x0B, 2),),
        "state": ((0x07, 2), (0x1E, 2)),
        "effect": (),
    }
    site_offsets = {
        "gate": ((0x0B, b"\x74\x13", b"\x90\x90"),),
        "state": ((0x07, b"\x74\x02", b"\x90\x90"),
                  (0x1E, b"\x74\x02", b"\x90\x90")),
    }

    def valid_hit(name: str, address: int) -> bool:
        for offset, original, patched in site_offsets.get(name, ()):
            try:
                current = image.read_va(address + offset, len(original))
            except ProfileError:
                return False
            state = _patch_state(current, original, patched)
            if state == "foreign" or (not runtime and state != "original"):
                return False
        return True

    hits: Dict[str, int] = {}
    for name, pattern in patterns.items():
        # Search both byte states together so an original candidate cannot
        # hide another already-patched candidate and bypass uniqueness.
        signature = (_wildcard_patch_signature(pattern, patch_ranges[name])
                     if runtime else MaskedSignature.parse(pattern))
        matches = image.find_signature(signature)
        valid = [address for address in matches if valid_hit(name, address)]
        if len(valid) != 1:
            return None, {}, "自动回归%s签名匹配数为%d" % (name, len(valid))
        hits[name] = valid[0]

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
    site_states = []
    try:
        for address, original in site_data:
            current = image.read_va(address, len(original))
            state = _patch_state(current, original, b"\x90\x90")
            if state == "foreign":
                return None, {}, "自动回归站点0x%08X原字节不匹配" % address
            site_states.append(state)
    except ProfileError as exc:
        return None, {}, "自动回归补丁点读取失败: %s" % exc
    observed_state = _aggregate_patch_state(site_states)
    if observed_state not in ("original", "patched"):
        return None, {}, "自动回归补丁站点状态不一致，拒绝接管"

    fingerprint = hashlib.sha256(
        b"native-second-action-unrestricted-v1\0" +
        image.read_va(hits["effect"], 18) +
        b"".join(original for _address, original in site_data)
    ).hexdigest().upper()
    spec = PatchSpec(
        "control_auto_return", "自动回归",
        tuple(PatchSite(address, original, b"\x90\x90")
              for address, original in site_data),
        tuple(patterns.values()), fingerprint, (2, 2, 2),
        observed_state=observed_state,
        observed_site_states=tuple(site_states))
    contract = {
        "mode": "native-second-action-unrestricted",
        "callSites": [gate_call, state_call],
        "nativeEffect": hits["effect"],
        "patchSites": [address for address, _original in site_data],
        "originalBytes": [original for _address, original in site_data],
        "patchedBytes": [b"\x90\x90"] * 3,
        "observedState": observed_state,
        "observedSiteStates": list(site_states),
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


def locate_consumable_mapping_single_exe_66(
        image: "PEImage") -> Tuple[Optional[ConsumableMappingSpec], str]:
    """Locate the 256..511 count array used by the standalone 6.6 derivative.

    All three accessors must subtract 0x100 and the initializer must clear
    exactly 0x100 bytes at the same base.  This proves the extended ID domain
    independently of the byte-sized equipment and 255-row metadata tables.
    """
    patterns = {
        "update": (
            "55 8B EC 2D 00 01 00 00 05 ?? ?? ?? ?? 84 C9 74 08 "
            "38 08 72 0B FE 08 EB 07 80 38 FF 74 02 FE 00 5D C3", 9),
        "read": (
            "55 8B EC 0F B7 C0 2D 00 01 00 00 0F B6 80 ?? ?? ?? ?? "
            "5D C3", 14),
        "write": (
            "55 8B EC 2D 00 01 00 00 8A 4D 08 88 88 ?? ?? ?? ?? 5D "
            "C2 04 00", 13),
        "clear": (
            "33 C0 99 66 3D 00 01 73 09 88 90 ?? ?? ?? ?? 40 EB F1", 11),
    }
    locations: Dict[str, int] = {}
    bases: Dict[str, int] = {}
    text_patterns: Dict[str, str] = {}
    for name, (pattern, operand_offset) in patterns.items():
        hits = image.find_signature(MaskedSignature.parse(pattern))
        if len(hits) != 1:
            return None, "单EXE 256项消耗品%s签名匹配数为%d" % (name, len(hits))
        locations[name] = hits[0]
        text_patterns[name] = pattern
        bases[name] = struct.unpack(
            "<I", image.read_va(hits[0] + operand_offset, 4))[0]
    if len(set(bases.values())) != 1:
        return None, "单EXE 256项消耗品读写与清零函数未指向同一基址"
    base = next(iter(bases.values()))
    if not 0x00010000 <= base <= 0x7FFFFFFF - 0xFF:
        return None, "单EXE 256项消耗品基址超出32位用户地址域"
    return ConsumableMappingSpec(
        "single-exe-66-256-v1", base,
        CONSUMABLE_ID_MIN_SINGLE_EXE_66,
        CONSUMABLE_STORAGE_COUNT_SINGLE_EXE_66,
        tuple(range(CONSUMABLE_ID_MIN_SINGLE_EXE_66,
                    CONSUMABLE_ID_MIN_SINGLE_EXE_66 +
                    CONSUMABLE_STORAGE_COUNT_SINGLE_EXE_66)),
        EQUIPMENT_COUNT_66, anchors=locations, patterns=text_patterns,
        structure_fingerprint=_consumable_fingerprint(
            text_patterns, base, CONSUMABLE_ID_MIN_SINGLE_EXE_66,
            CONSUMABLE_STORAGE_COUNT_SINGLE_EXE_66)), ""


def legacy_split_consumable_mapping() -> ConsumableMappingSpec:
    """The original tool's 6.1..6.3 MP+ contract, not a live ABI claim.

    Static CPython 3.5 evidence proves 43 names at item row 87, UInt8 reads
    and writes in two ranges, and bulk operations over exactly 43 counters.
    Runtime probing must validate every explicit address before enabling it.
    """
    addresses = tuple(range(0x004B09DB, 0x004B09DB + 17)) + tuple(
        range(0x00510C80, 0x00510C80 + 26))
    fingerprint = hashlib.sha256(
        ("legacy-split43|87:129|" + ",".join("%08X" % address
                                            for address in addresses)).encode("ascii")).hexdigest().upper()
    return ConsumableMappingSpec(
        "legacy-split-43-v1", addresses[0], 87, 43, tuple(range(87, 130)),
        154, addresses=addresses, structure_fingerprint=fingerprint)


def locate_consumable_mapping_composite(
        image: "PEImage", engine_version: int) -> Tuple[
            Optional[ConsumableMappingSpec], str]:
    version_value = EngineVersion(int(engine_version))
    if version_value in (EngineVersion.V64, EngineVersion.V65):
        return locate_consumable_mapping_legacy_expanded(image)
    if version_value in (EngineVersion.V61, EngineVersion.V62,
                         EngineVersion.V63, EngineVersion.V63_MP):
        return legacy_split_consumable_mapping(), ""

    standard_base, standard_mapping, standard_reason = (
        locate_consumable_mapping_66(image))
    legacy_spec, legacy_reason = locate_consumable_mapping_legacy_expanded(image)
    single_exe_spec, single_exe_reason = (
        locate_consumable_mapping_single_exe_66(image))
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
    if single_exe_spec is not None:
        candidates.append(single_exe_spec)
    if len(candidates) == 1:
        return candidates[0], ""
    if len(candidates) > 1:
        return None, "多个消耗品配方同时匹配，拒绝猜测"
    return None, "标准40项: %s；旧式105项: %s；单EXE 256项: %s" % (
        standard_reason, legacy_reason, single_exe_reason)


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
    # Follow callers which actually consume the icon byte.  This retains the
    # field contract when a MOD relocates the whole table to another address.
    icon_offsets = set()
    for region in scan_image_regions(image, True):
        raw = region.data
        cursor = 0
        while True:
            cursor = raw.find(b"\xE8", cursor)
            if cursor < 0:
                break
            if cursor + 5 <= len(raw):
                target = region.start + cursor + 5 + struct.unpack_from(
                    "<i", raw, cursor + 1)[0]
                if target == hits[0]:
                    following = raw[cursor + 5:cursor + 17]
                    # The lookup returns ECX.  MOVZX reg,BYTE[ECX+disp8]
                    # immediately following that call proves the icon field.
                    if (len(following) >= 4 and following[:2] == b"\x0F\xB6"
                            and following[2] & 0xC7 == 0x41
                            and following[3] in (0x0F, 0x14)):
                        icon_offsets.add(following[3])
            cursor += 1
    codec_by_base = {
        0x004A1140: ITEM_CODEC_65,
        0x0049F820: ITEM_CODEC_66,
    }
    codec = ({0x0F: ITEM_CODEC_66, 0x14: ITEM_CODEC_65}.get(
        next(iter(icon_offsets))) if len(icon_offsets) == 1
        else codec_by_base.get(base) if not icon_offsets else None)
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
EFFECT_LAYOUT_16_IMPORTED_HELPER_QUERY_PATTERN = (
    "55 8B EC 8B 4D 08 C1 E1 04 03 D1 52 6A 00 6A 00 "
    "50 FF 75 08 50 FF 15 ?? ?? ?? ?? 83 C4 08 59 0F B6 C0 50 "
    + "90 " * 34 +
    "89 45 F0 8B 4D 10 E8 ?? ?? ?? ?? 50 8B 45 F4 3C 02 73 25 "
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
    writer_pattern = (
        "0F B6 45 F8 3C B3 77 ?? 6B C8 08 03 0D ?? ?? ?? ?? "
        "8A 45 F9 3C 03 77 ?? 6B D0 02 03 CA 66 8B 55 FA "
        "3C 03 74 ?? 66 89 11 EB ?? 88 11")
    writers = image.find_signature(MaskedSignature.parse(writer_pattern))
    if len(writers) == 1:
        pointer = struct.unpack("<I", image.read_va(writers[0] + 13, 4))[0]
        fixed = struct.unpack("<I", image.read_va(pointer, 4))[0]
        detours = image.find_signature(MaskedSignature.parse(
            "2D 00 01 00 00 6B C8 08 03 0D ?? ?? ?? ?? E9 ?? ?? ?? ??"))
        queries = [address for address in detours
                   if struct.unpack("<I", image.read_va(address + 10, 4))[0]
                   == pointer]
        standard_queries = image.find_signature(
            MaskedSignature.parse(EFFECT_LAYOUT_8_QUERY_PATTERN))
        if len(queries) == 1 or len(standard_queries) == 1:
            query = (queries or standard_queries)[0]
            fingerprint = hashlib.sha256(
                b"static-pointer-effect-64-b3-v1\0" +
                image.read_va(writers[0], 45) + image.read_va(query, 19)
            ).hexdigest().upper()
            layout = effect_layout_8(pointer, 180, fingerprint)
            layout = replace(layout, metadata=dict(
                layout.metadata, staticPointer=True, initialBase=fixed,
                queryDetour=bool(queries)))
            if not queries:
                layout = replace(layout, resolver=AddressResolverKind.FIXED_RVA,
                                 reference=fixed)
            return layout, {"write": writers[0], "query": query,
                            "pointer": pointer, "fixed": fixed}, ""
        return None, {}, "6.4 B3写入锚点缺少相同指针的查询锚点"
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
                FieldSpec("unknown_06", 0x06, 2),
                FieldSpec("name", 0x08, 8),
                FieldSpec("derived_attack", 0x11, 2),
                FieldSpec("derived_defense", 0x13, 2),
                FieldSpec("derived_spirit", 0x15, 2),
                FieldSpec("derived_agility", 0x17, 2),
                FieldSpec("derived_morale", 0x19, 2),
                FieldSpec("hp_max", 0x1B, 4),
                FieldSpec("mp_max", 0x1F, 2),
                FieldSpec("base_strength", 0x21, 1),
                FieldSpec("base_command", 0x22, 1),
                FieldSpec("base_intelligence", 0x23, 1),
                FieldSpec("base_agility", 0x24, 1),
                FieldSpec("base_luck", 0x25, 1),
                FieldSpec("appearance", 0x27, 1),
                FieldSpec("unknown_28", 0x28, 1),
                FieldSpec("retreat", 0x29, 1),
                FieldSpec("friendly_flag", 0x2A, 1),
                FieldSpec("job", 0x2B, 1),
                FieldSpec("level", 0x2C, 1),
                FieldSpec("experience", 0x2D, 1),
                FieldSpec("weapon", 0x2E, 1),
                FieldSpec("weapon_level", 0x2F, 1),
                FieldSpec("weapon_experience", 0x30, 1),
                FieldSpec("armor", 0x31, 1),
                FieldSpec("armor_level", 0x32, 1),
                FieldSpec("armor_experience", 0x33, 1),
                FieldSpec("accessory", 0x34, 1),
                FieldSpec("kills", 0x35, 2),
                FieldSpec("unknown_44", 0x44, 2),
                FieldSpec("unknown_46", 0x46, 2),
            ), codec="person-48-v2", semantics_proven=True),
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
            addresses["bool_vars"], BOOL_BROWSE_BYTES_66, 1,
            (FieldSpec("packed_bits", 0, 1),), codec="bitset-msb0-v1",
            semantics_proven=True, metadata={
                "bitCount": BOOL_BROWSE_COUNT_66, "bitOrder": "msb0",
                "writableBitCount": BOOL_USER_COUNT_66,
                "writableBytes": BOOL_USER_BYTES_66,
                "domainBasis": BOOL_DOMAIN_BASIS_66}),
        "variables_int": TableLayoutSpec(
            "variables_int", AddressResolverKind.FIXED_RVA,
            addresses["int_vars"], limits["variable_int_count"], 4,
            (FieldSpec("value", 0, 4, signed=True),),
            codec="int32-array-v1", semantics_proven=True),
        "variables_ptr": TableLayoutSpec(
            "variables_ptr", AddressResolverKind.FIXED_RVA,
            addresses["ptr_vars"], limits["variable_ptr_count"], 4,
            (FieldSpec("value", 0, 4, signed=False),),
            codec="pointer-u32-v1", semantics_proven=True),
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
    if codec == ITEM_CODEC_LEGACY:
        if len(row) != ITEM_ROW_SIZE_66:
            raise ValueError("旧版物品行必须正好是0x19字节")
        name = row[:12].split(b"\0", 1)[0].decode("gb18030", errors="replace")
        # The legacy bulk action selects row+0x14 != FF.  Other legacy
        # metadata is deliberately not reinterpreted as the 6.5 field order.
        return ItemRow66(name, 0, 0, 0, 0, 0, 0, 0,
                         int(row[0x14] != 0xFF))
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


def validate_item_table_66(table: bytes, codec: str = ITEM_CODEC_66,
                           record_count: int = ITEM_RECORD_COUNT_66) -> bytes:
    if codec not in (ITEM_CODEC_65, ITEM_CODEC_66, ITEM_CODEC_LEGACY):
        raise ProfileError("未知物品行编解码器: %s" % codec)
    expected = int(record_count) * ITEM_ROW_SIZE_66
    if len(table) != expected:
        raise ProfileError("物品表应为 255×0x19 字节，实际为 %d 字节" % len(table))
    if not any(table):
        raise ProfileError("物品表为全零数据")
    named = 0
    decodable = 0
    for index in range(record_count):
        row = table[index*ITEM_ROW_SIZE_66:(index+1)*ITEM_ROW_SIZE_66]
        name_size = (12 if codec == ITEM_CODEC_LEGACY else
                     0x0F if codec == ITEM_CODEC_66 else 0x11)
        raw_name = row[:name_size].split(b"\0", 1)[0]
        if not raw_name:
            continue
        named += 1
        try:
            raw_name.decode("gbk")
            decodable += 1
        except UnicodeDecodeError:
            pass
    minimum_names = min(128, max(1, record_count // 2))
    if named < minimum_names or decodable < minimum_names:
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
        self.session_generation = str(getattr(session, "session_generation", ""))
        self.code_generation = ""

    def refresh(self) -> None:
        self._section_cache.clear()
        self._runtime_regions_cache = None
        self.code_generation = ""

    def iter_scan_regions(self, executable_only: bool = True):
        return iter_runtime_regions(self, self.session,
                                    executable_only=executable_only)

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
        for region in scan_image_regions(self, executable_only):
            for local_offset in signature.find_all(region.data):
                hits.append(region.start + local_offset)
        return hits

    def find_literal_references(self, value: int,
                                executable_only: bool = True) -> List[int]:
        needle = struct.pack("<I", value)
        result: List[int] = []
        for region in scan_image_regions(self, executable_only):
            mapped = region.data
            start = 0
            while True:
                index = mapped.find(needle, start)
                if index < 0:
                    break
                result.append(region.start + index)
                start = index + 1
        return result


class MappedRuntimeImage(PEImage):
    """Read a captured RVA image using the identity PE's section geometry.

    VProtect 2.2.0 stores only a tiny encrypted file-backed prefix on disk.
    The process capture is a ``SizeOfImage`` byte buffer, so ordinary
    ``PEImage`` raw-offset reads would inspect the wrong bytes.  This adapter
    keeps the disk PE for identity/section metadata and resolves every RVA
    directly into the captured mapped buffer.  It is intentionally read-only
    and never writes the fixture or the target process.
    """

    source = "runtime"

    def __init__(self, identity_path: str, mapped_path: str):
        super().__init__(identity_path)
        with open(os.path.abspath(mapped_path), "rb") as stream:
            mapped = stream.read()
        if len(mapped) < self.size_of_image:
            raise ProfileError("运行时 RVA 映像短于 PE SizeOfImage")
        self.data = mapped[:self.size_of_image]
        self.session_generation = "capture:" + hashlib.sha256(mapped).hexdigest()
        self.code_generation = ""

    def iter_scan_regions(self, executable_only: bool = True):
        return iter_mapped_regions(self, executable_only=executable_only)

    def read_va(self, va: int, size: int) -> bytes:
        if size <= 0:
            raise ProfileError("运行时映像读取长度必须大于零")
        rva = int(va) - self.image_base
        if rva < 0 or rva + size > len(self.data):
            raise ProfileError(
                "运行时 RVA 0x%08X+0x%X 越过映像范围" % (va, size))
        return self.data[rva:rva + size]

    def section_data(self, section: PESection) -> bytes:
        start = int(section.virtual_address)
        size = int(max(section.virtual_size, section.raw_size))
        if start < 0 or start + size > len(self.data):
            raise ProfileError("运行时节范围越过映像边界")
        return self.data[start:start + size]

    def find_signature(self, signature: MaskedSignature,
                       executable_only: bool = True) -> List[int]:
        hits: List[int] = []
        for region in scan_image_regions(self, executable_only):
            for local_offset in signature.find_all(region.data):
                hits.append(region.start + local_offset)
        return hits

    def find_literal_references(self, value: int,
                                executable_only: bool = True) -> List[int]:
        needle = struct.pack("<I", value)
        result: List[int] = []
        for region in scan_image_regions(self, executable_only):
            mapped = region.data
            start = 0
            while True:
                index = mapped.find(needle, start)
                if index < 0:
                    break
                result.append(region.start + index)
                start = index + 1
        return result

BOOL_READER_PATTERN_66 = (
    "55 8B EC 8B 45 08 6A 08 E8 ?? ?? ?? ?? 0F B6 44 08 38 "
    "83 EA 07 F7 DA 8A CA D3 E8 24 01 34 00 5D C2 04 00")
BOOL_WRITER_PATTERN_66 = (
    "55 8B EC 8B 45 08 6A 08 E8 ?? ?? ?? ?? 8D 44 08 38 "
    "8A 8A ?? ?? ?? ?? 20 08 8B 4D 0C 87 CA 83 E9 07 F7 D9 "
    "D3 E2 08 10 5D C2 08 00")
BOOL_DISPATCH_PATTERN_66 = (
    "55 8B EC 51 E8 ?? ?? ?? ?? 50 8B 55 FC 88 42 2E 83 F8 7B "
    "77 34 3C 01 77 14 83 C0 03 3C 04 75 24 8B 52 0C 4A 4A "
    "89 15 ?? ?? ?? ?? EB 17 52 83 E8 02 FF 14 85 ?? ?? ?? ?? "
    "83 C4 04")
BOOL_OBJECT_CONSTRUCTOR_PATTERN_66 = (
    "55 8B EC 6A FF 68 ?? ?? ?? ?? 64 FF 35 00 00 00 00 "
    "64 89 25 00 00 00 00 51 E8 ?? ?? ?? ?? FF 45 FC "
    "68 ?? ?? ?? ?? 6A 73 6A 40 8B 45 F0 05 44 20 00 00 50 "
    "E8 ?? ?? ?? ?? 8B 4D F0 C7 01 ?? ?? ?? ?? FF 4D FC")
BOOL_CONDITION_CALL_PATTERN_66 = (
    "6A 01 8B 4D 08 E8 ?? ?? ?? ?? 66 3D 00 80 75 07 B8 05 00 00 00 "
    "EB 30 0F B7 C0 50 8B 4D 08 E8 ?? ?? ?? ?? F7 D8 1B C0")
BOOL_ASSIGN_PATTERN_66 = (
    "55 8B EC 6A 04 8B 4D 08 E8 ?? ?? ?? ?? 50 3D 00 00 00 80 "
    "74 15 6A 27 8B 4D 08 E8 ?? ?? ?? ?? 0F B7 C8 81 F9 00 80 00 00 "
    "75 07 B8 05 00 00 00 EB 15 F7 D9 1B C9 F7 D9 51 FF 75 FC "
    "8B 4D 08 E8 ?? ?? ?? ?? 6A 01 58 8B E5 5D C3")


def _boolean_reference_operand(image, value: int) -> int:
    """Normalize a relocated absolute operand to the profile's reference VA."""
    session = getattr(image, "session", None)
    actual_base = int(getattr(session, "module_base", image.image_base))
    if (actual_base != image.image_base and
            actual_base <= value < actual_base + image.size_of_image):
        return image.image_base + value - actual_base
    return int(value)


def _locate_boolean_layout(image, storage: str) -> Tuple[Optional[Dict[str, object]], str]:
    """Prove the accessors and script object independently of a version hint.

    A text renderer and a ceil(width/8) helper formerly certified this layout.
    Here both bit accessors must share a division helper, the writer must use
    the MSB clear-mask table, script opcodes 05/0B must call those accessors,
    and the global object's vtable must point to that script dispatcher.
    The conservative user domain is separately documented policy.
    """
    packed = storage == "bitset"
    reader_pattern = BOOL_READER_PATTERN_66 if packed else BOOL_DWORD_READER_PATTERN
    writer_pattern = BOOL_WRITER_PATTERN_66 if packed else BOOL_DWORD_WRITER_PATTERN
    patterns = {
        "reader": reader_pattern,
        "writer": writer_pattern,
        "constructor": BOOL_OBJECT_CONSTRUCTOR_PATTERN_66,
    }
    anchors = {}
    spans = []

    def u32(address):
        return struct.unpack("<I", image.read_va(address, 4))[0]

    def absolute(address):
        return _boolean_reference_operand(image, u32(address))

    def call_target(address):
        if image.read_va(address, 1) != b"\xE8":
            raise ProfileError("BOOL evidence does not contain a CALL rel32")
        return address + 5 + struct.unpack("<i", image.read_va(address + 1, 4))[0]

    def snapshot(address, size, absolute_offsets=()):
        raw = bytearray(image.read_va(address, size))
        for offset in absolute_offsets:
            struct.pack_into("<I", raw, offset,
                             _boolean_reference_operand(
                                 image, struct.unpack_from("<I", raw, offset)[0]))
        spans.append({"address": address, "expectedHex": bytes(raw).hex(),
                      "absoluteOffsets": list(absolute_offsets)})

    try:
        for name, pattern in patterns.items():
            hits = image.find_signature(MaskedSignature.parse(pattern))
            if len(hits) != 1:
                return None, "BOOL %s semantic candidates: %d" % (name, len(hits))
            anchors[name] = hits[0]
        reader, writer = anchors["reader"], anchors["writer"]
        if packed:
            divide = call_target(reader + 8)
            if (call_target(writer + 8) != divide or
                    image.read_va(divide, 9) != bytes.fromhex("33D2F77C2404C20400")):
                return None, "BOOL reader/writer do not share the division-by-eight helper"
            masks = absolute(writer + 19)
            if image.read_va(masks, 8) != bytes.fromhex("7FBFDFEFF7FBFDFE"):
                return None, "BOOL writer MSB clear masks do not match"
            anchors.update({"divide": divide, "masks": masks})

        dispatch_patterns = (BOOL_DISPATCH_PATTERN_66,) if packed else (
            BOOL_DISPATCH_PATTERN_66.replace("83 F8 7B", "83 F8 79"),
            BOOL_DISPATCH_PATTERN_66.replace("83 F8 7B", "83 F8 79").replace(
                "50 8B 55 FC 88 42 2E", "0F B7 C0 50 8B 55 FC"),
        )
        dispatch_matches = {}
        for pattern in dispatch_patterns:
            signature = MaskedSignature.parse(pattern)
            for hit in image.find_signature(signature):
                dispatch_matches[hit] = image.read_va(hit, len(signature.pattern))
        if len(dispatch_matches) != 1:
            return None, "BOOL dispatcher semantic candidates: %d" % len(dispatch_matches)
        dispatcher, dispatch_raw = next(iter(dispatch_matches.items()))
        anchors["dispatcher"] = dispatcher
        table_offset = dispatch_raw.index(b"\xFF\x14\x85") + 3
        table = absolute(dispatcher + table_offset)
        condition = absolute(table + (0x05 - 2) * 4)
        assignment = absolute(table + (0x0B - 2) * 4)
        condition_raw = image.read_va(condition, 0x88)
        condition_hits = MaskedSignature.parse(
            BOOL_CONDITION_CALL_PATTERN_66).find_all(condition_raw)
        if len(condition_hits) != 1:
            return None, "BOOL script opcode 05 condition is not recognized"
        condition_call = condition + condition_hits[0] + 30
        if call_target(condition_call) != reader:
            return None, "BOOL script opcode 05 does not call the bit reader"
        assignment_patterns = (
            BOOL_ASSIGN_PATTERN_66,
            BOOL_ASSIGN_PATTERN_66.replace("EB 15", "EB 17").replace(
                "6A 01 58 8B E5 5D C3", "B8 01 00 00 00 8B E5 5D C3"),
        )
        if not packed:
            # Legacy assignment wrappers differ in their return epilogue.  The
            # dispatch table is the stronger evidence: locate a CALL rel32 to
            # the proven DWORD writer inside the opcode-0B target.
            raw_assignment = image.read_va(assignment, 0x100)
            writer_calls = []
            for offset in range(max(0, len(raw_assignment) - 5)):
                if raw_assignment[offset] == 0xE8:
                    try:
                        if call_target(assignment + offset) == writer:
                            writer_calls.append(assignment + offset)
                    except (ProfileError, struct.error):
                        pass
            if len(writer_calls) != 1:
                return None, "BOOL opcode 0B writer calls: %d" % len(writer_calls)
            assignment_raw = raw_assignment[:max(16, writer_calls[0] - assignment + 5)]
            assignment_call = writer_calls[0]
        else:
            assignment_matches = []
            for pattern in assignment_patterns:
                signature = MaskedSignature.parse(pattern)
                raw = image.read_va(assignment, len(signature.pattern))
                if signature.find_all(raw) == [0]:
                    assignment_matches.append(raw)
            if len(assignment_matches) != 1:
                return None, "BOOL script opcode 0B assignment is not recognized"
            assignment_raw = assignment_matches[0]
            assignment_call = assignment + assignment_raw.index(
                b"\x51\xFF\x75\xFC\x8B\x4D\x08\xE8") + 7
        if call_target(assignment_call) != writer:
            return None, "BOOL script opcode 0B does not call the bit writer"

        constructor = anchors["constructor"]
        constructor_raw = image.read_va(constructor, len(MaskedSignature.parse(
            BOOL_OBJECT_CONSTRUCTOR_PATTERN_66).pattern))
        vtable_offset = constructor_raw.index(b"\x8B\x4D\xF0\xC7\x01") + 5
        vtable = absolute(constructor + vtable_offset)
        if absolute(vtable + 8) != dispatcher:
            return None, "BOOL script object vtable does not point to the dispatcher"
        # MOV ECX, object; CALL constructor proves the global this pointer.
        contexts = set()
        context_sites = []
        for region in scan_image_regions(image, True):
            raw = region.data
            for offset in MaskedSignature.parse(
                    "B9 ?? ?? ?? ?? E8 ?? ?? ?? ??").find_all(raw):
                site = region.start + offset
                if call_target(site + 5) == constructor:
                    contexts.add(absolute(site + 1))
                    context_sites.append(site)
        if len(contexts) != 1:
            return None, "BOOL global script object candidates: %d" % len(contexts)
        context = next(iter(contexts))
        base = context + 0x38
        if not (image.image_base <= base and
                base + BOOL_BROWSE_BYTES_66 <= image.image_base + image.size_of_image):
            return None, "BOOL object storage lies outside the main module"
        anchors.update({"dispatchTable": table,
                        "condition": condition, "assignment": assignment,
                        "vtable": vtable, "scriptObject": context,
                        "contextInitializer": context_sites[0]})
        snapshot(reader, len(MaskedSignature.parse(reader_pattern).pattern))
        snapshot(writer, len(MaskedSignature.parse(writer_pattern).pattern),
                 (19,) if packed else ())
        if packed:
            snapshot(divide, 9)
            snapshot(masks, 8)
        snapshot(dispatcher, len(dispatch_raw), (39, table_offset))
        snapshot(table, (0x0B - 2 + 1) * 4,
                 tuple(range(0, (0x0B - 2 + 1) * 4, 4)))
        snapshot(condition + condition_hits[0], len(MaskedSignature.parse(
            BOOL_CONDITION_CALL_PATTERN_66).pattern))
        snapshot(assignment, len(assignment_raw))
        snapshot(constructor, len(constructor_raw), (6, 34, vtable_offset))
        snapshot(vtable + 8, 4, (0,))
        snapshot(context_sites[0], 10, (1,))
        result = {"base": base, "formatVerified": True, "anchors": anchors,
                  "verificationSpans": spans, "storage": storage,
                  "nativeIndexBoundsVerified": False}
        if packed:
            result.update({
                "domainBasis": BOOL_DOMAIN_BASIS_66,
                "capacityBasis": "剧本资料域与相邻人物表重叠证据；未证明原生总数",
                "nativeCapacityVerified": False, "nativeRecordCount": None,
                "recordCount": BOOL_USER_COUNT_66,
                "writableRecordCount": BOOL_USER_COUNT_66,
                "browseRecordCount": BOOL_BROWSE_COUNT_66,
                "storageBytes": BOOL_USER_BYTES_66,
                "browseStorageBytes": BOOL_BROWSE_BYTES_66,
                "stride": 1, "width": 1, "signed": False, "bitOrder": "msb0",
            })
        else:
            counts = []
            copier = None
            for name, pattern in (("saveLoop", BOOL_DWORD_SAVE_LOOP_PATTERN),
                                  ("loadLoop", BOOL_DWORD_LOAD_LOOP_PATTERN)):
                signature = MaskedSignature.parse(pattern)
                hits = image.find_signature(signature)
                if len(hits) != 1:
                    return None, "BOOL %s capacity candidates: %d" % (name, len(hits))
                site = hits[0]
                raw = image.read_va(site, len(signature.pattern))
                count = struct.unpack_from("<H", raw, 5)[0]
                following_field = struct.unpack_from("<I", raw, 41)[0]
                if not 1 <= count <= 0x7FFF or following_field != 0x38 + count * 4:
                    return None, "BOOL serializer count overlaps its following field"
                current_copier = call_target(site + 22)
                if copier is not None and current_copier != copier:
                    return None, "BOOL save/load loops use different copy helpers"
                copier = current_copier
                headers = []
                header_signature = MaskedSignature.parse(BOOL_SERIALIZER_HEADER_PATTERN)
                for offset in header_signature.find_all(image.read_va(site - 0x120, 0x120)):
                    header = site - 0x120 + offset
                    if absolute(header + 8) == context:
                        headers.append(header)
                if len(headers) != 1:
                    return None, "BOOL serializer does not uniquely use the global script object"
                anchors[name] = site
                anchors[name + "Header"] = headers[0]
                snapshot(site, len(raw))
                snapshot(headers[0], len(header_signature.pattern), (8,))
                counts.append(count)
            if counts[0] != counts[1]:
                return None, "BOOL save/load capacity differs"
            count = counts[0]
            if base + count * 4 > image.image_base + image.size_of_image:
                return None, "BOOL DWORD storage lies outside the main module"
            result.update({
                "domainBasis": "native-dword-save-load-loop-count",
                "capacityBasis": "getter+setter+script-05-0B+save-load-count+following-field",
                "nativeCapacityVerified": True, "nativeRecordCount": count,
                "nativeIndexBoundsVerified": True,
                "recordCount": count, "writableRecordCount": count,
                "browseRecordCount": count,
                "storageBytes": count * 4, "browseStorageBytes": count * 4,
                "stride": 4, "width": 4, "signed": False,
                "truthEncoding": "nonzero-dword",
            })
        return result, ""
    except (ProfileError, ValueError, struct.error, IndexError) as exc:
        return None, "BOOL semantic verification failed: %s" % exc


def locate_boolean_layout_66(image) -> Tuple[Optional[Dict[str, object]], str]:
    return _locate_boolean_layout(image, "bitset")


def locate_boolean_layout_legacy(image) -> Tuple[Optional[Dict[str, object]], str]:
    return _locate_boolean_layout(image, "dword")


def locate_boolean_layout(image) -> Tuple[Optional[Dict[str, object]], str]:
    """Select the native representation from code, including mixed variants."""
    packed, packed_reason = locate_boolean_layout_66(image)
    dword, dword_reason = locate_boolean_layout_legacy(image)
    if packed is not None and dword is not None:
        return None, "BOOL native representation is ambiguous"
    if packed is not None or dword is not None:
        return packed if packed is not None else dword, ""
    return None, "%s; %s" % (packed_reason, dword_reason)


def _verify_boolean_runtime_spans(memory, layout: Dict[str, object]) -> None:
    spans = layout.get("verificationSpans", ())
    if layout.get("formatVerified") is not True or not spans:
        raise ProfileError("BOOL native access format has not been verified")
    reference_base = int(getattr(memory, "reference_image_base", REFERENCE_IMAGE_BASE))
    actual_base = int(getattr(memory, "module_base", reference_base))
    module_size = int(getattr(memory, "module_size", 0))
    for span in spans:
        expected = bytes.fromhex(span["expectedHex"])
        raw = bytearray(memory.read_bytes(int(span["address"]), len(expected)))
        if len(raw) != len(expected):
            raise ProfileError("BOOL runtime evidence read was incomplete")
        for offset in span.get("absoluteOffsets", ()):
            value = struct.unpack_from("<I", raw, offset)[0]
            if (actual_base != reference_base and
                    actual_base <= value < actual_base + module_size):
                struct.pack_into("<I", raw, offset,
                                 reference_base + value - actual_base)
        if bytes(raw) != expected:
            raise ProfileError("BOOL native code/object evidence changed at 0x%08X" %
                               int(span["address"]))


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
    # Runtime captures may be represented by RuntimeModuleImage (a live
    # process session), MappedRuntimeImage (an offline RVA capture), or a
    # project-specific adapter.  Use the explicit source marker so all of
    # these paths read the runtime table instead of the protected disk file.
    if str(getattr(image, "source", "disk")).startswith("runtime"):
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
    # Runtime byte observations and ownership belong to the main memory
    # session, independently of static capability evidence.  A successful
    # signature match alone cannot authorize taking over someone else's patch.
    patch_runtime_states: Dict[str, str] = field(default_factory=dict)
    patch_runtime_reasons: Dict[str, str] = field(default_factory=dict)

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
                                     if 0 <= int(item) <= 0xFFFF}))
            start = int(self.limits.get("consumable_id_min", 0))
            end = int(self.limits.get("consumable_ui_id_max", -1))
            return tuple(range(max(0, start), min(0xFFFF, end) + 1))
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

    def _requires_runtime_access(self, key: str) -> bool:
        # A split counter map spans independently protected memory ranges.
        # Its page probe must remain authoritative in direct compatibility
        # mode so an unavailable second range cannot be presented as writable.
        mapping = self.metadata.get("consumable_mapping")
        return ((self.is_66 and key == "variables_bool") or
                (key == "item_consumables" and isinstance(mapping, dict) and
                 mapping.get("recipeKey") == "legacy-split-43-v1"))

    def can_read(self, key: str) -> bool:
        if (self.direct_compatibility and self.direct_write and
                not self._requires_runtime_access(key)):
            return self._compat_recipe_available(key)
        return self.resolved_feature(key).readable

    def can_write(self, key: str) -> bool:
        if key.startswith("control_") and key in self.patch_runtime_states:
            # UNREADABLE is a bounded transient state: the main-session probe
            # retains the last known UI state for two failed reads, then emits
            # UNREADABLE_CONFIRMED.  Actual writes still validate live bytes.
            if self.patch_runtime_states[key] not in (
                    "ORIGINAL", "OWNED_PATCHED", "UNREADABLE"):
                return False
        if self.is_66 and key == "variables_bool":
            layout = self.metadata.get("variable_layouts", {}).get(key, {})
            if (layout.get("formatVerified") is not True or
                    int(layout.get("writableRecordCount", 0)) <= 0):
                return False
        if (not self.is_66 and key == "variables_bool"):
            layout = self.metadata.get("variable_layouts", {}).get(key, {})
            if layout.get("nativeCapacityVerified") is not True:
                return False
        if (self.direct_compatibility and self.direct_write and
                not self._requires_runtime_access(key)):
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

    def set_patch_runtime_state(self, key: str, state: str,
                                reason: str = "") -> None:
        """Record a main-session observation without changing static evidence.

        Accept enum values as well as strings so the memory layer can expose
        its own typed state without creating an import cycle.  Unknown future
        states remain non-writable until explicitly supported by ``can_write``.
        """
        key = str(key)
        value = str(getattr(state, "value", state)).upper()
        self.patch_runtime_states[key] = value
        self.patch_runtime_reasons[key] = str(reason or "")

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
                    "item_metadata", "effect_query_layout"}
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
        return validate_item_table_66(table, codec, min(
            ITEM_RECORD_COUNT_66, int(self.limits.get(
                "item_record_count", ITEM_RECORD_COUNT_66))))

    def item_row(self, item_id: int) -> ItemRow66:
        table = self.item_table_66()
        if not 0 <= int(item_id) < len(table) // ITEM_ROW_SIZE_66:
            raise ProfileError("物品 ID 超出当前元数据范围")
        offset = int(item_id) * ITEM_ROW_SIZE_66
        codec = str(self.metadata.get("item_row_codec", ITEM_CODEC_66))
        return parse_item_row(table[offset:offset + ITEM_ROW_SIZE_66], codec)

    def _probe_legacy_composite_runtime(self, memory) -> None:
        failures: Dict[str, str] = {}
        # Legacy BOOLs are DWORD records.  Probe the complete evidence-backed
        # native span before presenting a live page as writable; this keeps a
        # fixed-address profile readable while refusing an unmapped or
        # unproven 4096-row fallback.
        bool_layout = self.metadata.get("variable_layouts", {}).get(
            "variables_bool")
        if isinstance(bool_layout, dict):
            try:
                base = int(self.addresses["bool_vars"])
                count = int(bool_layout.get(
                    "nativeRecordCount", bool_layout.get("recordCount", 0)))
                stride = int(bool_layout.get("stride", 4))
                width = int(bool_layout.get("width", 4))
                if count <= 0 or stride < width:
                    raise ProfileError("旧版 BOOL 原生跨度描述无效")
                size = (count - 1) * stride + width
                readable = memory.validate_range(base, size, write=False)
                writable = memory.validate_range(base, size, write=True)
                if not readable:
                    raise ProfileError("旧版 BOOL 原生页不可读")
                proven = bool_layout.get("nativeCapacityVerified") is True
                access = (FeatureAccess.READ_WRITE if proven and writable else
                          FeatureAccess.READ_ONLY)
                reason = ("" if access.writable else
                          ("旧版 BOOL 原生页只读" if writable is False else
                           "旧版 BOOL 容量未通过原生证据核验"))
                self.set_feature_access(
                    "variables_bool", access, reason=reason,
                    reason_code=("" if access.writable else
                                 ("RUNTIME_READ_ONLY" if writable is False else
                                  "NATIVE_CAPACITY_NOT_VERIFIED")),
                    resolved_address=base, runtime_probe_passed=True)
            except Exception as exc:
                failures["variables_bool"] = "旧版 BOOL 运行时探针失败: %s" % exc
                self.set_feature_access(
                    "variables_bool", FeatureAccess.UNAVAILABLE,
                    failures["variables_bool"], "RUNTIME_PROBE_FAILED")
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
                explicit_addresses = tuple(int(value) for value in mapping.get("addresses", ()))
                if explicit_addresses:
                    if len(explicit_addresses) != len(mapping["itemIds"]):
                        raise ProfileError("消耗品显式地址与ID数量不一致")
                    width = int(mapping["width"])
                    readable = all(memory.validate_range(address, width, write=False)
                                   for address in explicit_addresses)
                    writable = all(memory.validate_range(address, width, write=True)
                                   for address in explicit_addresses)
                elif mapping.get("baseDereference"):
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
                explicit_addresses = tuple(int(value) for value in mapping.get("addresses", ()))
                if explicit_addresses:
                    if len(explicit_addresses) != len(mapping["itemIds"]):
                        raise ProfileError("消耗品显式地址与ID数量不一致")
                    width = int(mapping["width"])
                    readable = all(memory.validate_range(address, width, write=False)
                                   for address in explicit_addresses)
                    valid = all(memory.validate_range(address, width, write=True)
                                for address in explicit_addresses)
                elif mapping.get("baseDereference"):
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
                if capability_key == "variables_bool":
                    _verify_boolean_runtime_spans(memory, layout)
                    if not memory.validate_range(
                            base, BOOL_BROWSE_BYTES_66, write=False):
                        failures[capability_key] = "BOOL browse storage is unreadable"
                    elif not memory.validate_range(
                            base, BOOL_USER_BYTES_66, write=True):
                        failures[capability_key] = "BOOL 用户变量页只读"
                    elif getattr(memory, "read_only", False):
                        failures[capability_key] = "BOOL 当前进程会话只读"
                    continue
                count_offset = layout.get("countOffset")
                record_count = int(layout["recordCount"])
                if count_offset is not None:
                    actual_count = memory.read_u32(base + int(count_offset))
                    if actual_count != record_count:
                        failures[capability_key] = (
                            "%s数量头为 %d，预期 %d" %
                            (address_key, actual_count,
                             record_count))
                        continue
                size = (int(layout["dataOffset"]) +
                        ((record_count + 7) // 8
                         if layout.get("storage") == "bitset" else
                         (record_count - 1) *
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
# Local adaptation baselines.  These identities are kept separate from the
# 6.6 evidence quorum because they are used by the legacy/composite profile
# path.  The 6.4 sample is a statically readable PE; the 2.2.0 sample is a
# VProtect-wrapped disk image whose executable code is only available after
# the game has unpacked it in memory.
KNOWN_LEGACY_TARGETS = {
    "1F6E07BC5C6B1ECB1DFF254BADCDEA6B922F15D7CC1B1C5D5DE4A8BCE7DE95A0": {
        "version": EngineVersion.V64,
        "variant": "三国之召唤猛将6.4（60关版）",
        "fileSize": 1128960,
        "imageSize": 0x142200,
        "adaptation": "static-64-zhaohuan-60",
        "diskProtection": "none",
        "runtimeRequired": False,
        "targetEvidence": {
            "effectAnchor": 0x00422032,
            "effectAnchorExpected": 100,
            "battleUnitFieldOffset": 0x08,
        },
    },
    "1D8E83F73E7780C60B0B5DE5277D54490F72472270F727C97753F8E72AE4B381": {
        "version": EngineVersion.V65,
        "variant": "加强版2.2.0（VProtect，运行时解包）",
        "fileSize": 43263298,
        "imageSize": 0x246000,
        "adaptation": "runtime-65-220",
        "diskProtection": "VProtect",
        "runtimeRequired": True,
        # Captured from the unmodified 2.2.0 process image.  These are
        # diagnostic/runtime anchors; they never authorize writes to the
        # protected disk image by themselves.
        "runtimeLayout": {
            "native_super": 0x004250B3,
            "native_state": 0x004CE7BF,
            "native_busy": 0x00500E0C,
            "wait_wrapper": 0x0042C4E0,
            "timer_context": 0x004B1B18,
            "stage": 0x004750E0,
            "phase": 0x004B3D18,
            "speed": 0x004CE7BC,
            "phase_table": 0x004750DC,
            "counter": 0x004750DF,
            "condition_descriptor": 0x004E7600,
            "condition_executor": 0x004E7500,
        },
    },
}


def legacy_recipe_id(feature_key: str, recipe: Dict[str, object]) -> str:
    """Return a namespaced recipe id for 6.4/6.5 composite profiles.

    ``capability_evidence.recipe_id`` predates the composite profiles and
    labels every recipe ``ccz66``.  That made a 6.4 or 6.5 profile look like
    a 6.6 layout in diagnostics even when the layout itself was correct.
    Keep the digest format stable while making the engine family explicit.
    """
    digest = hashlib.sha256(
        json.dumps(recipe, ensure_ascii=True, sort_keys=True,
                   separators=(",", ":")).encode("utf-8")
    ).hexdigest()[:20].upper()
    return "legacy.%s.%s" % (feature_key, digest)

# Display-only identities must not expand the dual-baseline evidence quorum.
KNOWN_66_VARIANTS = {
    # Local Xinnai derivative: semantic person/variable/call contracts match,
    # but the different file identity grants no additional runtime evidence.
    "59CAB212E786F0771AE0FE8368BD4B8DAB7FF4B9AA3A25085FED36E45A88F081":
        ("信奈的野望12关版", 1139200),
    "9E3EE0ADE22EA855FF64ACAB864D8C14B7E9E800FD5CA08C8B27D1E5CF6889BE":
        ("信奈的野望12关版", 1132544),
    "719F710687CAE4BAEC2B3A4B028B473EA8AEB334A7ACFA6A692B46F207B5CDF8":
        ("单EXE扩展6.6（基底Ekd5）", 6200832),
    "4A5221B4842A261647D0DE23CDE3867974A5493A9C98C21015A6116AA5F2E063":
        ("8.14绝对反击6.6", 1132032),
    "402F1675DC863577AD356B16B20FCE058A94C66E13B0BDD5EFAC1CE8E752F26B":
        ("以和为贵1-demo 6.6", 7789311),
    "F13022762138E2DB3E5E263079D5A36F6407D0CFA533622F84B2550E80636727":
        ("信奈的野望12关版", 1132544),
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
        ("55 8B EC 83 EC 08 89 4D F8 FF 31 E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? "
         "89 45 FC 6A 02 8B 4D F8 E8 ?? ?? ?? ?? 85 C0 74 04", 0,
         b"\x55\x8B\xEC\x83\xEC\x08", b"\x33\xC0\x48\xC3\x90\x90", 6),
    ),
    "control_pass_through": (
        ("FC E8 ?? ?? ?? ?? 85 D2 74 11 83 7D F0 01", 8, b"\x74", b"\xEB", 2),
        ("90 90 90 90 90 90 90 90 55 8B EC 51 52 50 33 D2", 8,
         b"\x55\x8B\xEC", b"\x33\xC0\xC3", 3),
    ),
}


def _locate_patch(image: PEImage, key: str, label: str) -> Tuple[Optional[PatchSpec], str]:
    runtime = _is_runtime_image(image)
    sites: List[PatchSite] = []
    patterns: List[str] = []
    instruction_lengths: List[int] = []
    states: List[str] = []
    for pattern, target_offset, original, patched, instruction_length in _PATCH_LOCATORS[key]:
        # Search original/patched candidates in the same pass.  Falling back
        # only after a failed original scan would miss a second patched copy.
        signature = (_wildcard_patch_signature(
            pattern, ((target_offset, len(original)),))
                     if runtime else MaskedSignature.parse(pattern))
        hits = image.find_signature(signature)
        valid = []
        for hit in hits:
            address = hit + target_offset
            try:
                current = image.read_va(address, len(original))
            except ProfileError:
                continue
            state = _patch_state(current, original, patched)
            if state == "original" or (runtime and state == "patched"):
                valid.append((address, current))
        if len(valid) != 1:
            return None, "%s签名匹配数为 %d" % (label, len(valid))
        address, current = valid[0]
        sites.append(PatchSite(address, original, patched))
        patterns.append(pattern)
        instruction_lengths.append(instruction_length)
        states.append(_patch_state(current, original, patched))
    fingerprint = hashlib.sha256("|".join(patterns).encode("ascii")).hexdigest().upper()
    observed_state = _aggregate_patch_state(states)
    if observed_state not in ("original", "patched"):
        return None, "%s补丁站点状态不一致，拒绝接管" % label
    return PatchSpec(key, label, tuple(sites), tuple(patterns), fingerprint,
                     tuple(instruction_lengths),
                     observed_state=observed_state,
                     observed_site_states=tuple(states)), ""


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
        # The semantic locator above already materialized a complete
        # original/patched PatchSpec for a live image.  Evidence recipes are
        # disk contracts and historically require original bytes; reapplying
        # them here would turn a valid live patched state into an unavailable
        # capability on every refresh.
        if (_is_runtime_image(image) and key in patches and
                patches[key].observed_state in ("original", "patched")):
            continue
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
RECALCULATE_ABI_PATTERN_66 = (
    "55 8B EC FF 71 1B FF 71 1F FF 35 ?? ?? ?? ?? 33 D2 3B 4D 08 "
    "75 ?? 8A 51 2C C6 05 ?? ?? ?? ?? 01 C6 41 2C 00 51 6A 00 52 "
    "E8 ?? ?? ?? ?? 8A 45 F4 A2 ?? ?? ?? ?? 8B 4D 08 8B 45 FC "
    "89 41 1B 8B 45 F8 66 89 41 1F 99 EB 02 B2 11 8B E5 5D C2 04 00")
RECALCULATE_NOP_ABI_PATTERN_66 = (
    "55 8B EC FF 71 1B FF 71 1F 90 90 90 90 90 90 33 D2 3B 4D 08 "
    "75 32 8A 51 2C 90 90 90 90 90 90 90 C6 41 2C 00 51 6A 00 52 "
    "E8 ?? ?? ?? ?? 90 90 90 90 90 90 90 90 8B 4D 08 8B 45 FC "
    "89 41 1B 8B 45 F8 66 89 41 1F 99 EB 02 B2 11 8B E5 5D C2 04 00")
FATAL_INDEX_PATTERN_66 = (
    "55 8B EC 0F B6 C8 6B C9 10 03 0D ?? ?? ?? ?? 5D C3")
FATAL_CONSUMER_PATTERN_66 = (
    "55 8B EC 51 6A 00 8B 4D 0C E8 ?? ?? ?? ?? 8B 4D FC "
    "3A 41 0F 74 25 8B 45 F8 3C 05 73 22 6B C0 03 03 45 FC "
    "8B 55 08 66 3B 10 75 0B 8B 4D 0C 8A 51 2C 3A 50 02 "
    "73 05 FF 45 F8 EB DB B0 01 EB 02 B0 FF 8B E5 5D C2 08 00")
FATAL_BOUND_PATTERNS_66 = (
    "8B 45 FC 3C 50 73 1E E8 ?? ?? ?? ?? FF 75 F8 FF 75 F4 "
    "E8 ?? ?? ?? ?? 3C 01",
    "8B 45 FC 3C 50 73 31 E8 ?? ?? ?? ?? FF 75 F4 FF 75 F8 "
    "E8 ?? ?? ?? ?? 3C 01",
)
VARIABLE_INT_BOUNDS_PATTERN_66 = (
    "66 81 F9 00 10 73 ?? 8D 0C 8D 00 20 50 00")
VARIABLE_PTR_BOUNDS_PATTERN_66 = (
    "66 81 F9 E0 01 77 ?? 8D 0C 8D 00 60 50 00")


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
        "variable_bool_count": BOOL_USER_COUNT_66,
        "variable_int_count": 4096,
        "variable_ptr_count": 481,
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
                "bitOrder": "msb0", "storageBytes": BOOL_USER_BYTES_66,
                "browseStorageBytes": BOOL_BROWSE_BYTES_66,
                "browseRecordCount": BOOL_BROWSE_COUNT_66,
                "writableRecordCount": 0, "formatVerified": False,
                "domainBasis": BOOL_DOMAIN_BASIS_66,
                "nativeIndexBoundsVerified": False,
                "recordCount": limits["variable_bool_count"],
            },
            "variables_int": {
                "addressKey": "int_vars", "countOffset": None,
                "dataOffset": 0, "stride": 4, "width": 4,
                "signed": True,
                "recordCount": limits["variable_int_count"],
            },
            "variables_ptr": {
                "addressKey": "ptr_vars", "countOffset": None,
                "dataOffset": 0, "stride": 4, "width": 4,
                "signed": False, "codec": "pointer-u32-v1",
                "recordCount": limits["variable_ptr_count"],
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
        if consumable_spec.key == "single-exe-66-256-v1":
            # The single-exe initializer reads 512 metadata rows, while
            # equipment/warehouse slots remain byte-sized and keep 255 as
            # their empty sentinel.  Only the physical metadata count grows.
            limits["item_record_count"] = 512
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
    recalculate_candidates = (
        ("standard", RECALCULATE_ABI_PATTERN_66),
        ("global-state-nops", RECALCULATE_NOP_ABI_PATTERN_66),
    )
    recalculate_matches = [
        (name, pattern, address)
        for name, pattern in recalculate_candidates
        for address in image.find_signature(MaskedSignature.parse(pattern))
    ]
    recalculate_pattern = (recalculate_matches[0][1]
                           if len(recalculate_matches) == 1
                           else RECALCULATE_ABI_PATTERN_66)
    if len(recalculate_matches) == 1:
        metadata["recalculate_variant"] = recalculate_matches[0][0]
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
            recalculate_pattern, None),
        "revive": (
            "55 8B EC 6A 00 83 EC 34 8B 4D 08 E8 ?? ?? ?? ?? 85 C0 75 11 81 F9 00 04 00 00", None),
        "weather": (
            "55 8B EC 51 0F B6 51 11 6B C2 06 8A 55 08 8A 94 10 00 E1 48 00", None),
        "effect": (
            "55 8B EC 51 E8 ?? ?? ?? ?? 50 81 65 08 FF 00 00 00 FF 75 FC FF 75 10 FF 75 08 8B 15 ?? ?? ?? ?? E8 ?? ?? ?? ?? 50 FF 75 10", 28),
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

    if len(recalculate_matches) != 1:
        located.pop("recalculate", None)
        capabilities["recalculate"] = _capability(
            "recalculate", False,
            "人物重算入口候选总命中数为 %d" % len(recalculate_matches))

    if not capabilities["fatal"].statically_verified:
        fatal_patterns = {
            "index": FATAL_INDEX_PATTERN_66,
            "consumer": FATAL_CONSUMER_PATTERN_66,
            "bound_1": FATAL_BOUND_PATTERNS_66[0],
            "bound_2": FATAL_BOUND_PATTERNS_66[1],
        }
        fatal_hits = {key: image.find_signature(MaskedSignature.parse(pattern))
                      for key, pattern in fatal_patterns.items()}
        if all(len(hits) == 1 for hits in fatal_hits.values()):
            fatal_index = fatal_hits["index"][0]
            fatal_consumer = fatal_hits["consumer"][0]
            bounds_ok = True
            for key in ("bound_1", "bound_2"):
                for call_offset, target in ((7, fatal_index),
                                            (18, fatal_consumer)):
                    call_site = fatal_hits[key][0] + call_offset
                    call_bytes = image.read_va(call_site, 5)
                    call_target = call_site + 5 + struct.unpack(
                        "<i", call_bytes[1:])[0]
                    if call_bytes[0] != 0xE8 or call_target != target:
                        bounds_ok = False
                        break
                if not bounds_ok:
                    break
            if bounds_ok:
                fatal_pointer = struct.unpack(
                    "<I", image.read_va(fatal_index + 11, 4))[0]
                extracted["fatal"] = fatal_pointer
                located["fatal"] = fatal_index
                capabilities["fatal"] = _capability(
                    "fatal", True,
                    recipe={
                        "kind": "fatal-table-index-consumer-bounds",
                        "pointerAddress": fatal_pointer,
                        "recordCount": 80, "rowSize": 16,
                        "characterSlots": 5, "slotSize": 3,
                        "patterns": fatal_patterns,
                        "anchorHits": {key: hits[0]
                                       for key, hits in fatal_hits.items()},
                    }, index=fatal_index, consumer=fatal_consumer,
                    pointer=fatal_pointer)
                metadata["fatal_layout_source"] = "index-consumer-bounds"

    if capabilities["recalculate"].statically_verified:
        entry = located["recalculate"]
        call_offset = 0x28
        call_bytes = image.read_va(entry + call_offset, 5)
        internal_target = (entry + call_offset + 5 +
                           struct.unpack("<i", call_bytes[1:])[0])
        expected_target = entry - 0x1EC
        if call_bytes[0] != 0xE8 or internal_target != expected_target:
            capabilities["recalculate"] = _capability(
                "recalculate", False,
                "人物重算内部调用目标为 0x%08X，预期 0x%08X" %
                (internal_target, expected_target))
            located.pop("recalculate", None)
        else:
            capabilities["recalculate"] = _capability(
                "recalculate", True,
                recipe={
                    "kind": "remote-call",
                    "address": entry,
                    "callingConvention": "thiscall",
                    "ecxSource": "person_record_ptr",
                    "arguments": ["person_record_ptr:u32"],
                    "calleeCleanup": 4,
                    "wrapperKind": "recalculate-person-v1",
                    "abiPattern": recalculate_pattern,
                    "internalCall": internal_target,
                })

    effect_query_candidates = (
        ("standard-4+2", EFFECT_LAYOUT_16_QUERY_PATTERN),
        ("absolute-counter-4+2",
         EFFECT_LAYOUT_16_ABSOLUTE_COUNTER_QUERY_PATTERN),
        ("imported-helper-4+2",
         EFFECT_LAYOUT_16_IMPORTED_HELPER_QUERY_PATTERN),
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
    bool_semantics, bool_reason = locate_boolean_layout_66(image)
    bool_layout_ok = bool_semantics is not None
    if bool_layout_ok:
        addresses["bool_vars"] = int(bool_semantics["base"])
        metadata["variable_layouts"]["variables_bool"].update(bool_semantics)
        metadata["variable_layouts"]["variables_bool"][
            "writableRecordCount"] = BOOL_USER_COUNT_66

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
    variable_bounds = {
        "variables_int": VARIABLE_INT_BOUNDS_PATTERN_66,
        "variables_ptr": VARIABLE_PTR_BOUNDS_PATTERN_66,
    }
    for address_key, capability_key in (
            ("bool_vars", "variables_bool"),
            ("int_vars", "variables_int"),
            ("ptr_vars", "variables_ptr")):
        if address_key == "bool_vars":
            available = bool_layout_ok
            boundary_hits = ()
        else:
            boundary_hits = tuple(image.find_signature(MaskedSignature.parse(
                variable_bounds[capability_key])))
            available = len(boundary_hits) == 1
            metadata["variable_layouts"][capability_key][
                "boundaryAnchor"] = boundary_hits[0] if available else 0
        variable_results[capability_key] = available
        capabilities[capability_key] = _capability(
            capability_key, available,
            ("%s基址与边界联合签名匹配数为 %d" %
             (address_key, len(boundary_hits)))
            if not available and address_key != "bool_vars" else
            (bool_reason if address_key == "bool_vars" and not available else ""),
            recipe={
                "kind": "variable-table", "addressKey": address_key,
                "count": metadata["variable_layouts"][capability_key][
                    "recordCount"],
                **metadata["variable_layouts"][capability_key],
            })
    variable_refs_ok = all(variable_results.values())
    capabilities["variables"] = _capability(
        "variables", variable_refs_ok,
        "变量基址引用尚未通过" if not variable_refs_ok else "",
        recipe={"kind": "variable-groups", "groups": 3,
                "counts": {
                    key: metadata["variable_layouts"][key]["recordCount"]
                    for key in ("variables_bool", "variables_int",
                                "variables_ptr")}})
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

    recalculate_signature = (
        image.read_va(
            located["recalculate"],
            len(MaskedSignature.parse(recalculate_pattern).pattern))
        if capabilities["recalculate"].statically_verified else b"")
    remote_calls = {
        "recalculate": RemoteCallSpec(
            "recalculate", "人物能力重算", located.get("recalculate", 0),
            recalculate_signature,
            capabilities["recalculate"].statically_verified,
            structural["recalculate"][0], capabilities["recalculate"].recipe_id,
            "thiscall", "ECX=person_record_ptr",
            ("person_record_ptr:u32",), 4,
            ("人物指针有效",), ("人物派生能力已重算",), 0,
            "recalculate-person-v1"),
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
    legacy_effect_count = {EngineVersion.V64: 180, EngineVersion.V65: 180,
                           EngineVersion.V63_MP: 144, EngineVersion.V63: 144,
                           EngineVersion.V62: 91, EngineVersion.V61: 88}[version]
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
        "item_name_size": 17 if expanded else 12,
        "effect_count": legacy_effect_count,
        "effect_name_count": (EFFECT_NAME_RECORD_COUNT_65_66
                              if pointer_effect else
                              legacy_effect_count),
        "effect_record_count": 255 if pointer_effect else
                               legacy_effect_count,
        "effect_stride": 8,
        "effect_character_slots": 3,
        "effect_job_slots": 1,
        "effect_value_per_slot": False,
        "effect_job_count": 80,
        "fatal_count": 80 if expanded else 36,
        "fatal_stride": 16,
        "variable_count": 4096,
        "variable_bool_count": 4096,
        "variable_int_count": 4096,
        "variable_ptr_count": 481,
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
                                      0x004075DF, b"", True,
                                      calling_convention="thiscall",
                                      ecx_source="ECX=person_record_ptr",
                                      arguments=("person_record_ptr:u32",),
                                      callee_cleanup=4,
                                      wrapper_kind="recalculate-person-v1"),
        "revive": RemoteCallSpec("revive", "战场复活",
                                 revive_address, b"", False,
                                 calling_convention="stdcall",
                                 arguments=("direction:u32", "x:u32", "y:u32",
                                            "data_id:u32"),
                                 callee_cleanup=16,
                                 preconditions=("目标槽为空且坐标有效",),
                                 postconditions=("单位结构和地图占用已建立",),
                                 wrapper_kind="legacy-life-v1"),
        "weather": RemoteCallSpec("weather", "天气刷新",
                                   0x0041D9D1, b"", False,
                                   calling_convention="thiscall",
                                   ecx_source="ECX=&battle_state",
                                   arguments=("weather:u8",),
                                   callee_cleanup=4,
                                   context_reference=0x004B3D08,
                                   wrapper_kind="weather-v1"),
        "turn_refresh_direction": RemoteCallSpec(
            "turn_refresh_direction", "转向刷新", 0x00457428, b"", True,
            calling_convention="thiscall", ecx_source="ECX=&unit_lookup_context",
            arguments=("data_id:u32", "sentinel:u32", "direction:u32",
                       "flags:u32"), callee_cleanup=16,
            context_reference=0x004B5DF0, wrapper_kind="legacy-turn-v1"),
        "turn_refresh_position": RemoteCallSpec(
            "turn_refresh_position", "坐标刷新", 0x004594DD, b"", True,
            calling_convention="thiscall", ecx_source="ECX=&unit_lookup_context",
            arguments=("data_id:u32", "x:u32", "y:u32", "direction:u32"),
            callee_cleanup=16,
            context_reference=0x004B5DF0, wrapper_kind="legacy-position-v1"),
    }
    metadata = {
        "item_record_ids": list(range(limits["item_count"])),
        "equipment_item_ids": [item_id for item_id in range(limits["equipment_count"])
                               if expanded or not 87 <= item_id < 130],
        "item_row_codec": ITEM_CODEC_65 if expanded else ITEM_CODEC_LEGACY,
        "item_layout": {
            "runtimeTableBase": addresses["item_table"],
            "rowSize": ITEM_ROW_SIZE_66,
            "nameSize": 17 if expanded else 12,
            "legacyRuntimeContract": True,
        },
        "variable_layouts": {
            "variables_bool": {
                "addressKey": "bool_vars", "countOffset": None,
                "dataOffset": 0, "stride": 4, "width": 4,
                "signed": True,
                "recordCount": limits["variable_bool_count"],
                "nativeCapacityVerified": False,
                "nativeRecordCount": None,
                "capacityBasis": "等待运行时/磁盘 getter、setter 与保存循环证据",
                "domainBasis": "旧版默认候选域0..%d，未完成原生容量核验" %
                               (BOOL_DOCUMENTED_COUNT_LEGACY - 1),
            },
            "variables_int": {
                "addressKey": "int_vars", "countOffset": None,
                "dataOffset": 0, "stride": 4, "width": 4,
                "signed": True,
                "recordCount": limits["variable_int_count"],
            },
            "variables_ptr": {
                "addressKey": "ptr_vars", "countOffset": None,
                "dataOffset": 0, "stride": 4, "width": 4,
                "signed": False, "codec": "pointer-u32-v1",
                "recordCount": limits["variable_ptr_count"],
            },
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
    for key, address, count, stride in (
            ("effect_exclusive", addresses["exclusive_set"], limits["effect_count"], 16),
            ("fatal", addresses["fatal_table"], limits["fatal_count"], 16),
            ("person", addresses["person_pointer"], limits["person_count"], 0x48)):
        table_layouts[key] = TableLayoutSpec(
            key, (AddressResolverKind.POINTER_RVA if key == "person" else
                  AddressResolverKind.FIXED_RVA), address, count, stride,
            codec={"effect_exclusive": "exclusive-set-16-v1",
                   "fatal": "fatal-16-v1", "person": "person-48-v2"}[key],
            semantics_proven=True, metadata={"legacyRuntimeContract": True})
    addresses["fatal_names"] = (0x004A7510 if expanded else
                                 0x004FF551 if version in
                                 (EngineVersion.V63_MP, EngineVersion.V63) else
                                 0x004FF710)
    addresses["effect_names"] = {
        EngineVersion.V64: 0x004FF560, EngineVersion.V65: 0x004CC600,
        EngineVersion.V63_MP: 0x004FF7A0, EngineVersion.V63: 0x004FF7A0,
        EngineVersion.V62: 0x004FF960, EngineVersion.V61: 0x004FFA80}[version]
    if expanded:
        consumable_spec = ConsumableMappingSpec(
            "legacy-expanded-105-base-profile", 0x00510C80,
            CONSUMABLE_ID_MIN_LEGACY_EXPANDED,
            CONSUMABLE_STORAGE_COUNT_LEGACY_EXPANDED,
            tuple(range(CONSUMABLE_ID_MIN_LEGACY_EXPANDED,
                        CONSUMABLE_ID_MAX_LEGACY_EXPANDED + 1)),
            CONSUMABLE_ID_MIN_LEGACY_EXPANDED)
    else:
        consumable_spec = legacy_split_consumable_mapping()
        addresses["consumable_counts"] = consumable_spec.base
    limits.update(consumable_id_min=consumable_spec.id_min,
                  consumable_ui_id_max=consumable_spec.id_max,
                  consumable_storage_count=consumable_spec.storage_count)
    metadata["consumable_mapping_spec"] = consumable_spec
    metadata["consumable_mapping"] = consumable_spec.as_mapping()
    metadata["consumable_item_ids"] = list(consumable_spec.ui_item_ids)
    table_layouts["item_consumables"] = TableLayoutSpec(
            "item_consumables", AddressResolverKind.FIXED_RVA,
            consumable_spec.base, consumable_spec.storage_count, 1,
            (FieldSpec("count", 0, 1),), codec=("explicit-uint8-count-map-v1"
                if consumable_spec.addresses else "uint8-count-array-v1"),
            semantics_proven=True,
            metadata={"idMin": consumable_spec.id_min,
                      "uiIdMax": consumable_spec.id_max,
                      "addresses": consumable_spec.addresses,
                      "legacyRuntimeContract": True})
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
    for key in ("effect_exclusive", "fatal", "person"):
        layout = table_layouts[key]
        resolved_features[key] = ResolvedFeature(
            key, FeatureAccess.READ_WRITE, capabilities[key].recipe_id, layout,
            resolved_address=0 if layout.pointer_backed else layout.reference,
            details={"legacyRuntimeContract": True})
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
        key, CapabilityState.ENABLED, recipe_id=legacy_recipe_id(key, recipe),
        recipe=recipe)


def _disable_legacy_feature(profile: EngineProfile, key: str,
                            reason: str, reason_code: str) -> None:
    profile.capabilities[key] = FeatureCapability(
        key, CapabilityState.UNAVAILABLE, reason, reason_code)
    current = profile.resolved_feature(key)
    profile.resolved_features[key] = ResolvedFeature(
        key, FeatureAccess.UNAVAILABLE, current.recipe_id, current.layout,
        reason=reason, reason_code=reason_code)


def _legacy_target_spec(profile: EngineProfile, sha256: str) -> Optional[Dict[str, object]]:
    spec = KNOWN_LEGACY_TARGETS.get(str(sha256).upper())
    if not spec or spec.get("version") is not profile.engine_version:
        return None
    return spec


def _lock_protected_disk_profile(profile: EngineProfile) -> None:
    """Keep a protected disk image diagnostic-only until a live image exists."""
    profile.direct_write = False
    profile.access_policy = "evidence_gated"
    reason = "磁盘 Ekd5.exe 为 VProtect 加壳态，必须附加运行中的解包映像后才能写入"
    for key in tuple(profile.capabilities):
        current = profile.resolved_feature(key)
        profile.resolved_features[key] = current.with_runtime(
            FeatureAccess.UNAVAILABLE, reason=reason,
            reason_code="PROTECTED_DISK_RUNTIME_REQUIRED",
            probe_passed=False)


def _probe_220_runtime_anchors(
        image: PEImage, spec: Dict[str, object]) -> Dict[str, object]:
    """Check the audited 2.2.0 anchors in a mapped/live image.

    The VProtect image does not contain the ordinary legacy core signatures,
    so ``verify_engine_identity`` cannot be used as its only runtime gate.
    Probe a small set of stable function prologues captured from the unpacked
    process instead.  A candidate must contain at least three distinct code
    anchors; data-only addresses in the layout are deliberately ignored.
    """
    layout = spec.get("runtimeLayout")
    if not isinstance(layout, dict):
        return {"passed": False, "hits": {}, "required": 3}
    patterns = {
        "native_super": b"\x55\x8B\xEC",
        "wait_wrapper": b"\x55\x8B\xEC",
        "stage": b"\x55\x8B\xEC",
        "condition_descriptor": b"\xB8\x05\x00\x00\x00\xC3",
        "condition_executor": b"\x55\x89\xE5",
    }
    hits: Dict[str, str] = {}
    for key, expected in patterns.items():
        address = layout.get(key)
        if address is None:
            continue
        try:
            actual = image.read_va(int(address), len(expected))
        except (ProfileError, OSError, ValueError):
            continue
        if actual == expected:
            hits[key] = "0x%08X" % int(address)
    required = 3
    return {"passed": len(hits) >= required, "hits": hits,
            "required": required, "checked": len(patterns)}


def _apply_220_runtime_layout(profile: EngineProfile,
                              spec: Dict[str, object]) -> None:
    """Attach the audited 2.2.0 runtime anchors to a live 6.5 profile."""
    runtime_layout = spec.get("runtimeLayout")
    if not isinstance(runtime_layout, dict):
        return
    profile.metadata["runtime_layout"] = dict(runtime_layout)
    profile.metadata["runtime_layout_source"] = "2.2.0-unpacked-process-evidence"
    profile.metadata["runtime_layout_verified"] = True
    profile.metadata["runtime_required"] = True
    # Keep the runtime profile evidence-gated.  The fixed addresses come from
    # a read-only capture; effect and consumable writes remain blocked until a
    # live page-permission/semantic probe promotes those two features.
    profile.direct_write = False
    profile.access_policy = "evidence_gated"
    # The 2.2.0 process keeps the legacy 6.5 pointer-backed effect table and
    # the 150..254 consumable domain.  Disk signatures are unavailable under
    # VProtect, so these recipes are enabled only for image.source=runtime.
    pointer = 0x00500C3B
    layout = effect_layout_8(pointer, 255,
                             fingerprint="runtime-220-effect-65-v1")
    recipe = {
        "kind": "effect-assignment-pointer-8-runtime-220",
        "pointerAddress": pointer,
        "recordCount": layout.record_count,
        "rowSize": layout.stride,
        "runtimeAnchors": dict(runtime_layout),
    }
    profile.addresses["effect_assign_pointer"] = pointer
    profile.addresses["effect_assign"] = 0
    profile.limits["effect_record_count"] = layout.record_count
    profile.metadata.setdefault("table_layouts", {})["effect"] = layout
    profile.metadata["effect_layout_codec"] = layout.codec
    profile.capabilities["effect"] = _verified_legacy_capability("effect", recipe)
    profile.resolved_features["effect"] = ResolvedFeature(
        "effect", FeatureAccess.READ_ONLY, profile.capabilities["effect"].recipe_id,
        layout, reason="2.2.0 运行时特效表等待指针探针",
        reason_code="SESSION_PROBE_REQUIRED", details={"runtimeAnchors": dict(runtime_layout)})
    consumable = ConsumableMappingSpec(
        "runtime-220-consumables-105-v1", 0x00510C80,
        CONSUMABLE_ID_MIN_LEGACY_EXPANDED,
        CONSUMABLE_STORAGE_COUNT_LEGACY_EXPANDED,
        tuple(range(CONSUMABLE_ID_MIN_LEGACY_EXPANDED,
                    CONSUMABLE_ID_MAX_LEGACY_EXPANDED + 1)),
        # The captured layout exposes the 150..254 item domain; no separate
        # equipment range is inferred from the protected disk image.
        CONSUMABLE_ID_MIN_LEGACY_EXPANDED,
        structure_fingerprint="runtime-220-consumables-v1")
    consumable_layout = TableLayoutSpec(
        "item_consumables", AddressResolverKind.FIXED_RVA,
        consumable.base, consumable.storage_count, consumable.stride,
        (FieldSpec("count", 0, consumable.width),),
        codec="uint8-count-array-v1", semantics_proven=True, writable=False,
        structure_fingerprint=consumable.structure_fingerprint,
        metadata={"idMin": consumable.id_min, "uiIdMax": consumable.id_max})
    mapping = consumable.as_mapping()
    profile.addresses["consumable_counts"] = consumable.base
    profile.limits.update({
        "equipment_count": consumable.equipment_count,
        "consumable_id_min": consumable.id_min,
        "consumable_ui_id_max": consumable.id_max,
        "consumable_storage_count": consumable.storage_count,
    })
    profile.metadata.setdefault("table_layouts", {})[
        "item_consumables"] = consumable_layout
    profile.metadata["consumable_mapping_spec"] = consumable
    profile.metadata["consumable_mapping"] = mapping
    profile.metadata["consumable_item_ids"] = list(consumable.ui_item_ids)
    profile.metadata["consumable_runtime_recipe"] = {
        "base": consumable.base,
        "idMin": consumable.id_min,
        "idMax": consumable.id_max,
        "storageCount": consumable.storage_count,
        "structureFingerprint": consumable.structure_fingerprint,
    }
    consumable_recipe = {
        "kind": "consumable-count-array-runtime-220",
        "countBase": consumable.base,
        **mapping,
    }
    profile.capabilities["item_consumables"] = (
        _verified_legacy_capability("item_consumables", consumable_recipe))
    profile.resolved_features["item_consumables"] = ResolvedFeature(
        "item_consumables", FeatureAccess.READ_ONLY,
        profile.capabilities["item_consumables"].recipe_id,
        consumable_layout, resolved_address=consumable.base,
        structure_fingerprint=consumable.structure_fingerprint,
        reason="2.2.0 运行时消耗品表等待页面权限探针",
        reason_code="SESSION_PROBE_REQUIRED",
        details={"runtimeAnchors": dict(runtime_layout)})


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
    target_spec = _legacy_target_spec(profile, profile.sha256)
    is_220_runtime = False
    if target_spec is not None:
        profile.variant_name = str(target_spec["variant"])
        profile.metadata["adapted_target"] = str(target_spec["adaptation"])
        profile.metadata["target_identity"] = {
            "sha256": profile.sha256,
            "fileSize": profile.file_size,
            "imageSize": profile.image_size,
            "diskProtection": target_spec["diskProtection"],
            "runtimeRequired": bool(target_spec["runtimeRequired"]),
        }
        if target_spec.get("targetEvidence"):
            profile.metadata["target_evidence"] = dict(
                target_spec["targetEvidence"])
        if bool(target_spec["runtimeRequired"]):
            # A live module and an offline mapped capture are both runtime
            # evidence.  Keep accepting adapters that use a more specific
            # marker such as ``runtime-capture``.
            if not str(profile.metadata["image_source"]).startswith("runtime"):
                _lock_protected_disk_profile(profile)
                return profile
            runtime_probe = _probe_220_runtime_anchors(image, target_spec)
            profile.metadata["runtime_anchor_probe"] = runtime_probe
            if not runtime_probe.get("passed", False):
                profile.metadata["runtime_layout_error"] = (
                    "运行时映像未通过2.2.0专用锚点验证，拒绝启用布局")
                _lock_protected_disk_profile(profile)
                return profile
            _apply_220_runtime_layout(profile, target_spec)
            is_220_runtime = True

    if profile.version in (EngineVersion.V64, EngineVersion.V65):
        try:
            if profile.version == EngineVersion.V65:
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
            # The legacy runtime contract remains usable even if a derivative
            # replaces the lookup helper.  Hydration validates its live rows.
            item_base = (profile.addresses.get("item_table", 0x004A1140)
                         if str(getattr(image, "source", "disk")).startswith("runtime")
                         else 0)
            profile.addresses["item_table"] = item_base
            item_codec = ITEM_CODEC_65
            profile.metadata["item_layout"].update({
                "runtimeTableBase": item_base, "lookupAddress": 0,
                "lookupDiagnostic": item_reason,
                "legacyRuntimeContract": bool(item_base)})
        try:
            if str(getattr(image, "source", "disk")).startswith("runtime"):
                raise ProfileError("当前进程目录等待运行时读取")
            if item_codec != ITEM_CODEC_65 or not item_base:
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
            if not item_base:
                profile.metadata.pop("item_row_codec", None)
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

    if profile.version == EngineVersion.V65 and not is_220_runtime:
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
            if layout.pointer_backed:
                profile.addresses["effect_assign_pointer"] = layout.reference
                profile.addresses["effect_assign"] = int(
                    layout.metadata.get("initialBase", 0))
            else:
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
                resolved_address=profile.addresses["effect_assign"],
                structure_fingerprint=layout.structure_fingerprint,
                runtime_probe_passed=True, details={"anchorHits": hits})
        else:
            _disable_legacy_feature(
                profile, "effect", effect_reason,
                "EFFECT_64_SEMANTIC_ANCHOR_MISMATCH")

    if is_220_runtime:
        # The captured 2.2.0 image has a known table anchor, but its protected
        # disk signatures are intentionally absent.  Keep the runtime recipe
        # installed above and wait for a live page-permission probe.
        return profile

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
            codec=("explicit-uint8-count-map-v1" if consumable_spec.addresses
                   else "uint8-count-array-v1"), semantics_proven=True,
            structure_fingerprint=consumable_spec.structure_fingerprint,
            metadata={"idMin": consumable_spec.id_min,
                      "uiIdMax": consumable_spec.id_max,
                      "addresses": consumable_spec.addresses,
                      "legacyRuntimeContract": bool(consumable_spec.addresses)})
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


def _infer_engine_family(image, version_part: Optional[int]):
    """Prefer code contracts to an often stale version resource."""
    fallback = (engine_version_from_resource(version_part)
                if version_part is not None else None)
    # A modified 6.6 can deliberately retain an 8-byte effect table; do not
    # confuse that table's codec with the executable's version family.
    patterns_66 = (EFFECT_LAYOUT_16_QUERY_PATTERN,
                   EFFECT_LAYOUT_16_ABSOLUTE_COUNTER_QUERY_PATTERN,
                   EFFECT_LAYOUT_16_IMPORTED_HELPER_QUERY_PATTERN)
    hits66 = [hit for pattern in patterns_66
              for hit in image.find_signature(MaskedSignature.parse(pattern))]
    if hits66:
        return EngineVersion.V66, {"source": "code", "contract": "effect-16",
                                   "anchors": hits66}
    initializer = image.find_signature(MaskedSignature.parse(
        EFFECT_LAYOUT_8_INITIALIZER_PATTERN))
    if initializer and fallback != EngineVersion.V66:
        return EngineVersion.V65, {"source": "code", "contract": "effect-8-fe",
                                   "anchors": initializer}
    writers64 = image.find_signature(MaskedSignature.parse(
        "0F B6 45 F8 3C B3 77 ?? 6B C8 08 03 0D ?? ?? ?? ??"))
    if writers64:
        return EngineVersion.V64, {"source": "code", "contract": "effect-8-b3",
                                   "anchors": writers64}
    identity = KNOWN_LEGACY_TARGETS.get(PEImage(image.path).sha256)
    if identity is not None:
        return identity["version"], {"source": "known-identity",
                                     "contract": identity["adaptation"]}
    if fallback is None:
        raise ProfileError("运行时代码尚未识别引擎版本，等待游戏初始化或手动选择")
    return fallback, {"source": "version-resource", "contract": "candidate-only"}


def _legacy_control_patches(profile, image) -> None:
    """Materialize the historic control operations as owned code recipes."""
    # Prefer movable semantic signatures.  Fixed legacy sites are retained
    # only when their complete native/modified instruction bytes agree.
    for key, label in (
            ("control_friendly", "友军控制"),
            ("control_all_factions", "全阵营控制"),
            ("control_no_ai", "禁用AI"),
            ("control_move_after_wait", "待机后移动"),
            ("control_move_255", "移动255"),
            ("control_pass_through", "穿越单位")):
        spec, _reason = _locate_patch(image, key, label)
        if spec is not None:
            profile.patches[key] = spec

    expanded = profile.version in (EngineVersion.V64, EngineVersion.V65)
    sites_by_key = {
        "control_friendly": ((0x43D574, b"\x74", b"\xEB"),
            (0x43D5A5, b"\x74", b"\xEB"), (0x43E359, b"\x74", b"\xEB"),
            (0x44E748, b"\x75\xC8", b"\x90\x90"),
            (0x44E754, b"\x75\xBC", b"\x90\x90"),
            (0x455379, b"\x74", b"\xEB")),
        "control_all_factions": (
            (0x4242AF if expanded else 0x4242B0,
             b"\x74\xDD" if expanded else b"\x32", b"\x90\x90" if expanded else b"\x90"),
            (0x437BC0, b"\x74\x34", b"\x90\x90"),
            (0x454405, b"\x75\x27", b"\x90\x90"),
            (0x455646, b"\x75", b"\xEB")),
        "control_no_ai": ((0x44E9D3, b"\x74", b"\xEB"),),
        "control_move_after_wait": ((0x43D5DC, b"\x74", b"\xEB"),),
        "control_move_255": ((0x43F74F, b"\x55\x8B\xEC\x83", b"\x33\xC0\x48\xC3"),),
        "control_pass_through": ((0x4387B7, b"\x74", b"\xEB"),
            (0x437D59, b"\x55\x8B\xEC", b"\x33\xC0\xC3")),
    }
    for key, contracts in sites_by_key.items():
        if key in profile.patches:
            continue
        sites = []
        for address, original, replacement in contracts:
            try:
                current = image.read_va(address, len(original))
                if current not in (original, replacement):
                    break
                sites.append(PatchSite(address, original, replacement))
            except ProfileError:
                break
        else:
            spec = PatchSpec(key, key, tuple(sites),
                normalized_fingerprint=hashlib.sha256(
                    b"legacy-control-v1" + b"".join(
                        struct.pack("<I", site.address) + site.original
                        for site in sites)).hexdigest().upper())
            profile.patches[key] = spec
    native_auto, contract, _reason = locate_auto_return_patch_66(image)
    if native_auto is not None:
        profile.patches["control_auto_return"] = native_auto
        profile.metadata["auto_return_layout"] = contract
    else:
        dynamic_auto, reason = _legacy_auto_return_recipe(profile, image)
        if dynamic_auto is not None:
            profile.dynamic_patches["control_auto_return"] = dynamic_auto
            profile.capabilities["control_auto_return"] = _verified_legacy_capability(
                "control_auto_return", dynamic_auto.recipe())
            profile.resolved_features["control_auto_return"] = ResolvedFeature(
                "control_auto_return", FeatureAccess.READ_WRITE,
                profile.capabilities["control_auto_return"].recipe_id,
                resolved_address=dynamic_auto.call_sites[0].address)
        else:
            profile.metadata["auto_return_recipe_error"] = reason
    for key, spec in profile.patches.items():
        profile.capabilities[key] = _verified_legacy_capability(key, spec.recipe())
        profile.resolved_features[key] = ResolvedFeature(
            key, FeatureAccess.READ_WRITE, profile.capabilities[key].recipe_id,
            resolved_address=spec.sites[0].address)


def _legacy_auto_return_recipe(profile, image):
    """Relocate the old wrench's 73-byte behavior into an owned allocation.

    The final DEC intentionally uses the caller's EBP, exactly as the legacy
    wrapper does after LEAVE.  Its two call sites expect that caller-local
    action counter update; replacing it with a normal callee RET changes play.
    """
    try:
        first = next((address for address in (0x44E570, 0x44E575)
                      if image.read_va(address, 1) == b"\xE8"), None)
        second = 0x44EA73
        if first is None or image.read_va(second, 1) != b"\xE8":
            return None, "旧自动回归两个CALL站点未识别"
        target = _relative_call_target(image, first)
        if _relative_call_target(image, second) != target:
            return None, "旧自动回归两个站点未调用同一原函数"
        if image.read_va(target, 3) != b"\x55\x8B\xEC":
            return None, "旧自动回归原函数入口未就绪"
        if image.read_va(0x43E6AA, 3) != b"\x55\x8B\xEC":
            return None, "旧自动回归动作刷新函数未就绪"
        expanded = profile.version in (EngineVersion.V64, EngineVersion.V65)
        stub = bytearray(bytes.fromhex(
            "558BEC83EC04FF7508E8000000008B4D0881E1FF000000"
            "6BC90081C100000000894DFCB806000000F7D020410D"
            "68880000006A068B4DFCFF71049090E8000000008BE55DFE4DFCC300"))
        stub[0x19] = profile.limits["battle_stride"]
        relocations = [StubRelocation(0x0A, "relative_reference", target),
                       StubRelocation(0x1C, "absolute_reference",
                                      profile.addresses["battle_units"]),
                       StubRelocation(0x3D, "relative_reference", 0x43E6AA)]
        if not expanded:
            if image.read_va(0x43E4D6, 3) != b"\x55\x8B\xEC":
                return None, "旧自动回归人物转换函数未就绪"
            stub[0x2D:0x3C] = bytes.fromhex("FF31E80000000068880000006A0650")
            relocations.append(StubRelocation(0x30, "relative_reference", 0x43E4D6))
        sites = tuple(DynamicCallSite(address, image.read_va(address, 5), 5, 0xE8)
                      for address in (first, second))
        spec = DynamicPatchSpec(
            "control_auto_return", "自动回归", sites, bytes(stub), tuple(relocations),
            normalized_fingerprint=hashlib.sha256(
                b"legacy-auto-return-relocated-v1" + bytes(stub)).hexdigest().upper())
        return spec, ""
    except (ProfileError, struct.error, ValueError) as exc:
        return None, str(exc)


def _read_legacy_name_catalog(image, address, count):
    if address <= 0 or count <= 0:
        return None
    try:
        raw = image.read_va(address, count * 16)
        names = tuple(raw[index*16:index*16+15].split(b"\0", 1)[0]
                      .decode("gbk") for index in range(count))
        if sum(bool(name) and name.isprintable() for name in names) >= max(1, count // 2):
            return names
    except (ProfileError, UnicodeDecodeError):
        pass
    return None


_SHUHAN64_REMOTE_ABI_SHA256 = (
    "DEF7DB403D541F18B08B2399F69FEDCF567046923A042E3B79594B9D0581BF10")
_SHUHAN64_VERIFIED_REMOTE_CALLS = {
    "recalculate": (0x004075DF, "558BECFF711BFF71", "recalculate-person-v1",
                    "remote-recalculate"),
    "revive": (0x004092C7, "558BEC68FF000000", "legacy-life-v1", "remote-revive"),
    "turn_refresh_direction": (0x00457428, "558BEC83EC1C894D", "legacy-turn-v1",
                               "remote-direction"),
    "turn_refresh_position": (0x004594EA, "558BEC83EC18518B", "legacy-position-v1",
                              "remote-position"),
}


def _register_legacy_remote_abi_evidence(profile, image):
    """Apply the four observed Shuhan calls only to that exact live image.

    The three-cycle UI readback/restore evidence is local to this SHA, entry
    signature and wrapper contract; other 6.4 engines inherit no ABI claim.
    Weather remains unverified until an independent actual-call record exists.
    """
    if (profile.version != EngineVersion.V64 or
            profile.sha256.upper() != _SHUHAN64_REMOTE_ABI_SHA256 or
            not str(getattr(image, "source", "disk")).startswith("runtime")):
        return
    for key, (address, signature_hex, wrapper, check) in _SHUHAN64_VERIFIED_REMOTE_CALLS.items():
        call = profile.remote_calls.get(key)
        signature = bytes.fromhex(signature_hex)
        if (call is None or call.address != address or call.signature != signature or
                call.wrapper_kind != wrapper):
            continue
        evidence = {
            "kind": "sha-scoped-legacy-remote-abi",
            "sha256": profile.sha256.upper(), "address": address,
            "entrySignature": signature_hex, "wrapper": wrapper,
            "artifact": "shuhan64-ui-remote-roundtrip.json", "check": check,
            "cycles": 3, "readbackPassed": True, "restored": True,
            "gameplayObserved": False,
        }
        evidence_id = "local.shuhan64.20261105." + check
        profile.remote_calls[key] = replace(call, abi_verified=True)
        profile.metadata.setdefault("legacy_remote_abi_evidence", {})[key] = evidence
        profile.capabilities[key] = FeatureCapability(
            key, CapabilityState.ENABLED,
            reason="当前模块SHA、入口签名及调用包装匹配已通过三次实机复读恢复的证据",
            recipe_id=legacy_recipe_id(key, evidence),
            evidence_ids=(evidence_id,), addresses={"entry": address}, recipe=evidence)


def _finish_profile(profile, image, detection) -> EngineProfile:
    if not profile.sha256 and getattr(image, "path", None):
        disk_image = PEImage(image.path)
        profile.sha256 = disk_image.sha256
        profile.file_size = os.path.getsize(image.path)
        profile.preferred_image_base = disk_image.image_base
        profile.image_size = disk_image.size_of_image
    profile.metadata.update({
        "detection_evidence": detection,
        "session_generation": str(getattr(image, "session_generation", "")),
        "code_generation": str(getattr(image, "code_generation", "")),
        "image_source": str(getattr(image, "source", "disk")),
    })
    if not profile.is_66:
        # Legacy BOOLs use four-byte records.  Keep the profile read-only
        # until the actual getter/setter, script dispatch, and matching
        # save/load loops prove a count for this executable.
        bool_layout = profile.metadata.get("variable_layouts", {}).get(
            "variables_bool")
        if isinstance(bool_layout, dict):
            native_bool, native_reason = locate_boolean_layout_legacy(image)
            if native_bool is not None:
                profile.limits["variable_bool_count"] = int(
                    native_bool["nativeRecordCount"])
                bool_layout.update(native_bool)
                bool_layout["recordCount"] = int(
                    native_bool["nativeRecordCount"])
                profile.capabilities["variables_bool"] = _capability(
                    "variables_bool", True, recipe={
                        "kind": "variable-table",
                        "addressKey": "bool_vars", **native_bool})
            else:
                bool_layout["nativeCapacityVerified"] = False
                bool_layout["capacityReason"] = native_reason
                profile.capabilities["variables_bool"] = _capability(
                    "variables_bool", False,
                    "旧版 BOOL 容量未通过原生 getter/setter 与保存循环核验")
    if not profile.is_66:
        # The original 6.2 tool supports a separately expanded 126-name
        # table.  Its content, rather than just a nonzero pointer, must prove
        # that this runtime uses the expanded domain.
        if profile.version == EngineVersion.V62:
            alternate_names = _read_legacy_name_catalog(image, 0x004F0000, 126)
            if alternate_names is not None:
                profile.addresses["effect_names"] = 0x004F0000
                for key in ("effect_count", "effect_name_count", "effect_record_count"):
                    profile.limits[key] = 126
                profile.metadata["effect_names"] = alternate_names
                for key in ("effect", "effect_exclusive"):
                    layout = replace(profile.metadata["table_layouts"][key],
                                     record_count=126)
                    profile.metadata["table_layouts"][key] = layout
                    profile.resolved_features[key] = replace(
                        profile.resolved_features[key], layout=layout)
                profile.metadata["legacy_effect_name_variant"] = "6.2-expanded-126"
        _legacy_control_patches(profile, image)
        # Preserve the known calling convention while recording what was
        # actually observed in this module rather than asserting ABI proof.
        for key, call in tuple(profile.remote_calls.items()):
            entry_address = call.address
            try:
                signature = image.read_va(call.address, 8)
                code_present = signature[:3] in (b"\x55\x8B\xEC", b"\x55\x89\xE5")
                if not code_present and signature.startswith(b"\x90"):
                    entry_bytes = image.read_va(call.address, 40)
                    padding = len(entry_bytes) - len(entry_bytes.lstrip(b"\x90"))
                    if (0 < padding <= 32 and
                            entry_bytes[padding:padding+3] in
                            (b"\x55\x8B\xEC", b"\x55\x89\xE5")):
                        entry_address += padding
                        signature = entry_bytes[padding:padding+8]
                        code_present = True
                        profile.metadata.setdefault("remote_call_entry_aliases", {})[key] = {
                            "requested": call.address, "entry": entry_address,
                            "padding": padding}
            except ProfileError:
                signature, code_present = b"", False
            profile.remote_calls[key] = replace(
                call, address=entry_address,
                signature=signature if code_present else b"", abi_verified=False)
        _register_legacy_remote_abi_evidence(profile, image)
        for key, count, address in (
                ("effect_names", profile.limits["effect_name_count"],
                 profile.addresses.get("effect_names", 0)),
                ("runtime_fatal_names", profile.limits["fatal_count"],
                 profile.addresses.get("fatal_names", 0))):
            if address <= 0 or key in profile.metadata:
                continue
            names = _read_legacy_name_catalog(image, address, count)
            if names is not None:
                profile.metadata[key] = names
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
    image = image if image is not None else PEImage(exe_path)
    if manual_hint is None:
        selected, detection = _infer_engine_family(image, version_part)
    else:
        selected = EngineVersion(int(manual_hint))
        detection = {"source": "manual", "selected": selected.name}
    if selected is EngineVersion.V66:
        profile = detect_engine(
            exe_path, version_part=EngineVersion.V66, image=image)
        return _finish_profile(profile, image, detection)
    profile = legacy_profile(int(selected))
    if selected in (EngineVersion.V64, EngineVersion.V65):
        profile = _apply_legacy_composite_detection(
            profile, exe_path, image=image)
    return _finish_profile(profile, image, detection)
