"""Declarative runtime layouts and lossless record snapshots.

This module deliberately has no dependency on the profile or memory modules.
It is shared by static engine detection, runtime probing and the historic Qt
UI without introducing import cycles.
"""

from __future__ import annotations

import time
from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Dict, Optional, Tuple


class FeatureAccess(str, Enum):
    UNAVAILABLE = "Unavailable"
    READ_ONLY = "ReadOnly"
    READ_WRITE = "ReadWrite"

    @property
    def readable(self) -> bool:
        return self in (FeatureAccess.READ_ONLY, FeatureAccess.READ_WRITE)

    @property
    def writable(self) -> bool:
        return self is FeatureAccess.READ_WRITE

    @property
    def label(self) -> str:
        return {
            FeatureAccess.UNAVAILABLE: "不可用",
            FeatureAccess.READ_ONLY: "只读",
            FeatureAccess.READ_WRITE: "可读写",
        }[self]


class AddressResolverKind(str, Enum):
    FIXED_RVA = "fixed-rva"
    POINTER_RVA = "pointer-rva"
    SIGNATURE_OPERAND = "signature-operand"
    DOUBLE_POINTER = "double-pointer"


class SemanticAnchorRole(str, Enum):
    INITIALIZER = "initializer"
    READER = "reader"
    WRITER = "writer"


@dataclass(frozen=True)
class FieldSpec:
    key: str
    offset: int
    width: int
    signed: bool = False
    minimum: Optional[int] = None
    maximum: Optional[int] = None
    empty_value: Optional[int] = None
    preserve_opaque_empty: bool = False

    def __post_init__(self) -> None:
        if self.offset < 0 or self.width not in (1, 2, 4, 8):
            raise ValueError("字段偏移或宽度无效")


@dataclass(frozen=True)
class TableLayoutSpec:
    key: str
    resolver: AddressResolverKind
    reference: int
    record_count: int
    stride: int
    fields: Tuple[FieldSpec, ...] = ()
    codec: str = "raw"
    pointer_offset: int = 0
    second_pointer_offset: int = 0
    structure_fingerprint: str = ""
    semantics_proven: bool = False
    writable: bool = True
    metadata: Dict[str, Any] = field(default_factory=dict)

    def __post_init__(self) -> None:
        if self.reference <= 0:
            raise ValueError("表地址引用必须为正数")
        if self.record_count <= 0 or self.stride <= 0:
            raise ValueError("表记录数和步长必须为正数")
        if any(item.offset + item.width > self.stride for item in self.fields):
            raise ValueError("字段超出记录边界")

    @property
    def byte_size(self) -> int:
        return self.record_count * self.stride

    @property
    def pointer_backed(self) -> bool:
        return self.resolver in (
            AddressResolverKind.POINTER_RVA,
            AddressResolverKind.DOUBLE_POINTER,
        )


@dataclass(frozen=True)
class ResolvedFeature:
    key: str
    access: FeatureAccess
    recipe_id: str = ""
    layout: Optional[TableLayoutSpec] = None
    resolved_address: int = 0
    structure_fingerprint: str = ""
    reason: str = ""
    reason_code: str = ""
    runtime_probe_passed: bool = False
    details: Dict[str, Any] = field(default_factory=dict)

    @property
    def readable(self) -> bool:
        return self.access.readable

    @property
    def writable(self) -> bool:
        return self.access.writable

    def with_runtime(self, access: FeatureAccess, resolved_address: int = 0,
                     reason: str = "", reason_code: str = "",
                     probe_passed: bool = True) -> "ResolvedFeature":
        return ResolvedFeature(
            self.key, FeatureAccess(access), self.recipe_id, self.layout,
            int(resolved_address), self.structure_fingerprint,
            reason, reason_code, bool(probe_passed), dict(self.details))


@dataclass(frozen=True)
class SemanticAnchorEvidence:
    role: SemanticAnchorRole
    address: int
    pointer_reference: int = 0
    record_count: int = 0
    stride: int = 0
    field_offsets: Tuple[int, ...] = ()
    field_widths: Tuple[int, ...] = ()
    person_domain: Tuple[int, int] = ()
    job_domain: Tuple[int, int] = ()
    normalized_fingerprint: str = ""
    source: str = ""
    signature_pattern: str = ""

    def __post_init__(self) -> None:
        if self.address <= 0:
            raise ValueError("语义锚点地址必须为正数")
        if self.field_widths and len(self.field_widths) != len(self.field_offsets):
            raise ValueError("语义锚点字段偏移和宽度数量不一致")


@dataclass(frozen=True)
class ResolvedTableDescriptor:
    key: str
    pointer_reference: int
    runtime_base: int
    record_count: int
    stride: int
    fields: Tuple[FieldSpec, ...]
    person_domain: Tuple[int, int] = ()
    job_domain: Tuple[int, int] = ()
    empty_predicate: str = ""
    structure_fingerprint: str = ""
    generation: str = ""
    page_ranges: Tuple[Tuple[int, int], ...] = ()
    anchors: Tuple[SemanticAnchorEvidence, ...] = ()

    def __post_init__(self) -> None:
        if not self.key or self.pointer_reference <= 0:
            raise ValueError("语义表描述符缺少功能键或指针来源")
        if self.record_count <= 0 or self.stride <= 0:
            raise ValueError("语义表描述符记录数或步长无效")
        if any(item.offset + item.width > self.stride for item in self.fields):
            raise ValueError("语义表描述符字段越过记录边界")
        roles = {item.role for item in self.anchors}
        if SemanticAnchorRole.READER not in roles:
            raise ValueError("语义表描述符缺少读取锚点")

    @property
    def byte_size(self) -> int:
        return self.record_count * self.stride

    @property
    def roles(self) -> Tuple[SemanticAnchorRole, ...]:
        return tuple(sorted({item.role for item in self.anchors},
                            key=lambda item: item.value))

    @property
    def writable_semantics(self) -> bool:
        roles = set(self.roles)
        return (SemanticAnchorRole.READER in roles and
                SemanticAnchorRole.WRITER in roles)

    def to_layout(self, codec: str, writable: Optional[bool] = None,
                  metadata: Optional[Dict[str, Any]] = None) -> TableLayoutSpec:
        details = dict(metadata or {})
        details.update({
            "personDomain": self.person_domain,
            "jobDomain": self.job_domain,
            "emptyPredicate": self.empty_predicate,
            "anchorRoles": tuple(item.value for item in self.roles),
            "descriptorGeneration": self.generation,
        })
        return TableLayoutSpec(
            self.key, AddressResolverKind.POINTER_RVA,
            self.pointer_reference, self.record_count, self.stride,
            self.fields, codec=codec,
            structure_fingerprint=self.structure_fingerprint,
            semantics_proven=True,
            writable=(self.writable_semantics if writable is None else writable),
            metadata=details)


@dataclass(frozen=True)
class OpaqueEmptyToken:
    raw_value: int
    domain: str


@dataclass(frozen=True)
class RawRecordSnapshot:
    feature_key: str
    address: int
    raw: bytes
    decoded: Any
    table_base: int
    pointer_reference: Optional[int] = None
    pointer_value: Optional[int] = None
    generation: str = ""
    read_time: float = field(default_factory=time.time)

    def __post_init__(self) -> None:
        if self.address <= 0 or self.table_base <= 0 or not self.raw:
            raise ValueError("原始记录快照缺少有效地址或字节")
        if (self.pointer_reference is None) != (self.pointer_value is None):
            raise ValueError("指针来源和值必须同时存在或同时为空")


@dataclass(frozen=True)
class CompareWriteEntry:
    address: int
    expected: bytes
    replacement: bytes
    actual_address: bool = False

    def __post_init__(self) -> None:
        if self.address <= 0 or not self.expected:
            raise ValueError("比较写入条目无效")
        if len(self.expected) != len(self.replacement):
            raise ValueError("比较写入的原值与新值必须等长")
