"""Semantic x86 anchors for independently locating engine tables."""

from __future__ import annotations

import hashlib
import struct
from dataclasses import dataclass
from typing import Dict, Optional, Sequence, Tuple

from engine_layout import (FieldSpec, ResolvedTableDescriptor,
                           SemanticAnchorEvidence, SemanticAnchorRole)


_DESCRIPTOR_CACHE: Dict[Tuple[str, str, str], ResolvedTableDescriptor] = {}


@dataclass(frozen=True)
class SemanticAnchorPattern:
    role: SemanticAnchorRole
    pattern: str
    pointer_operand_offset: Optional[int] = None
    window_size: int = 0
    source: str = "masked-signature"


class SemanticResolutionError(RuntimeError):
    pass


class _MaskedPattern:
    def __init__(self, text: str):
        self.values = tuple(
            None if part in ("?", "??") else int(part, 16)
            for part in text.split())
        if not self.values:
            raise ValueError("语义锚点签名不能为空")

    def find_all(self, data: bytes):
        size = len(self.values)
        runs = []
        run = bytearray()
        run_start = 0
        for index, value in enumerate(self.values + (None,)):
            if value is not None:
                if not run:
                    run_start = index
                run.append(value)
            elif run:
                runs.append((run_start, bytes(run)))
                run.clear()
        anchor_start, anchor = max(runs, key=lambda item: len(item[1]))
        hits = []
        search_at = 0
        while True:
            anchor_hit = data.find(anchor, search_at)
            if anchor_hit < 0:
                break
            start = anchor_hit - anchor_start
            if 0 <= start <= len(data) - size and all(
                    expected is None or data[start + offset] == expected
                    for offset, expected in enumerate(self.values)):
                hits.append(start)
            search_at = anchor_hit + 1
        return hits

    def matches(self, data: bytes) -> bool:
        return len(data) == len(self.values) and all(
            expected is None or data[index] == expected
            for index, expected in enumerate(self.values))


def _normalized_instruction_fingerprint(image, address: int,
                                        size: int,
                                        fallback_pattern: str) -> str:
    size = max(int(size), len(fallback_pattern.split()))
    raw = image.read_va(address, size)
    try:
        from capstone import (Cs, CS_ARCH_X86, CS_MODE_32,
                              CS_OP_IMM, CS_OP_MEM, CS_OP_REG)
        engine = Cs(CS_ARCH_X86, CS_MODE_32)
        engine.detail = True
        normalized = []
        image_end = image.image_base + image.size_of_image
        for instruction in engine.disasm(raw, address):
            operands = []
            for operand in instruction.operands:
                if operand.type == CS_OP_REG:
                    operands.append("reg")
                elif operand.type == CS_OP_IMM:
                    value = int(operand.imm) & 0xFFFFFFFF
                    operands.append(
                        "image-va" if image.image_base <= value < image_end
                        else "imm:%X" % value)
                elif operand.type == CS_OP_MEM:
                    displacement = int(operand.mem.disp) & 0xFFFFFFFF
                    operands.append(
                        "mem:r%s+i%s*s%d+d%s" %
                        ("1" if operand.mem.base else "0",
                         "1" if operand.mem.index else "0",
                         int(operand.mem.scale),
                         "image-va" if image.image_base <= displacement < image_end
                         else "%X" % displacement))
                else:
                    operands.append("other")
            normalized.append("%s(%s)" %
                              (instruction.mnemonic, ",".join(operands)))
        if normalized:
            material = "|".join(normalized).encode("ascii")
            return hashlib.sha256(material).hexdigest().upper()
    except (ImportError, OSError, ValueError):
        pass
    canonical = " ".join(
        "??" if part in ("?", "??") else part.upper()
        for part in fallback_pattern.split())
    return hashlib.sha256(canonical.encode("ascii")).hexdigest().upper()


class SemanticTableResolver:
    """Resolve a table only when independent code anchors agree."""

    def __init__(self, image):
        self.image = image

    def _find(self, pattern: str):
        signature = _MaskedPattern(pattern)
        result = []
        for section in self.image.sections:
            if not section.executable:
                continue
            if hasattr(self.image, "section_data"):
                raw = self.image.section_data(section)
            else:
                raw = self.image.data[
                    section.raw_offset:section.raw_offset + section.raw_size]
            for local in signature.find_all(raw):
                result.append(self.image.image_base +
                              section.virtual_address + local)
        return result

    def resolve(self, key: str, record_count: int, stride: int,
                fields: Tuple[FieldSpec, ...],
                anchors: Sequence[SemanticAnchorPattern],
                person_domain: Tuple[int, int] = (),
                job_domain: Tuple[int, int] = (),
                empty_predicate: str = "",
                runtime_base: int = 0,
                generation: str = "") -> Tuple[
                    Optional[ResolvedTableDescriptor], Dict[str, int], str]:
        recipe_material = "|".join(
            [key, str(record_count), str(stride),
             ",".join("%d:%d" % (item.offset, item.width)
                      for item in fields)] +
            ["%s:%s" % (anchor.role.value, anchor.pattern)
             for anchor in anchors])
        recipe_fingerprint = hashlib.sha256(
            recipe_material.encode("ascii")).hexdigest().upper()
        image_identity = "%s:%s" % (
            self.image.sha256, getattr(self.image, "source", "disk"))
        cache_key = (image_identity, key, recipe_fingerprint)
        cached = _DESCRIPTOR_CACHE.get(cache_key)
        if cached is not None:
            return cached, {
                item.role.value: item.address for item in cached.anchors
            }, ""
        evidence = []
        hits_by_role: Dict[str, int] = {}
        pointer_values = []
        for anchor in anchors:
            hits = self._find(anchor.pattern)
            role_key = anchor.role.value
            if len(hits) != 1:
                return (None, hits_by_role,
                        "%s语义锚点匹配数为 %d" % (role_key, len(hits)))
            address = hits[0]
            hits_by_role[role_key] = address
            pointer_reference = 0
            if anchor.pointer_operand_offset is not None:
                pointer_reference = struct.unpack(
                    "<I", self.image.read_va(
                        address + anchor.pointer_operand_offset, 4))[0]
                pointer_values.append(pointer_reference)
            fingerprint = _normalized_instruction_fingerprint(
                self.image, address,
                anchor.window_size or len(anchor.pattern.split()),
                anchor.pattern)
            evidence.append(SemanticAnchorEvidence(
                anchor.role, address, pointer_reference,
                record_count, stride,
                tuple(item.offset for item in fields),
                tuple(item.width for item in fields),
                person_domain, job_domain, fingerprint, anchor.source,
                anchor.pattern))

        if not pointer_values:
            return None, hits_by_role, "语义锚点没有提供表指针来源"
        if len(set(pointer_values)) != 1:
            return None, hits_by_role, "初始化/读写锚点提取到不同表指针"
        roles = {item.role for item in evidence}
        if SemanticAnchorRole.READER not in roles:
            return None, hits_by_role, "语义描述符缺少读取锚点"
        material = "|".join(
            [key, str(record_count), str(stride),
             ",".join("%d:%d" % (item.offset, item.width)
                      for item in fields)] +
            sorted(item.normalized_fingerprint for item in evidence))
        descriptor = ResolvedTableDescriptor(
            key, pointer_values[0], int(runtime_base), record_count, stride,
            fields, person_domain, job_domain, empty_predicate,
            hashlib.sha256(material.encode("ascii")).hexdigest().upper(),
            generation, (), tuple(evidence))
        _DESCRIPTOR_CACHE[cache_key] = descriptor
        return descriptor, hits_by_role, ""


def verify_runtime_descriptor(memory, descriptor: ResolvedTableDescriptor) -> None:
    """Recheck every static anchor against the attached main-module image."""
    for anchor in descriptor.anchors:
        if not anchor.signature_pattern:
            raise SemanticResolutionError(
                "%s的%s锚点缺少运行时签名" %
                (descriptor.key, anchor.role.value))
        pattern = _MaskedPattern(anchor.signature_pattern)
        actual = memory.resolve_reference(anchor.address)
        raw = memory.read_actual_bytes(actual, len(pattern.values))
        if not pattern.matches(raw):
            raise SemanticResolutionError(
                "%s的%s锚点在运行时已变化: 0x%08X" %
                (descriptor.key, anchor.role.value, actual))
    runtime_base = memory.read_u32(descriptor.pointer_reference)
    if descriptor.runtime_base and runtime_base != descriptor.runtime_base:
        raise SemanticResolutionError(
            "%s表指针代次已变化: 0x%08X -> 0x%08X" %
            (descriptor.key, descriptor.runtime_base, runtime_base))
    if not memory.validate_actual_range(
            runtime_base, descriptor.byte_size, write=False):
        raise SemanticResolutionError(
            "%s运行时表范围不可读: 0x%08X+0x%X" %
            (descriptor.key, runtime_base, descriptor.byte_size))
