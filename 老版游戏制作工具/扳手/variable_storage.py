"""Compact boolean snapshots and strict writes for native variable layouts.

The snapshot stores native bytes once.  Packed flags stay packed; legacy
integer-backed booleans retain every original byte until explicitly changed.
All addresses here are reference addresses resolved by MemorySession.
"""

from dataclasses import dataclass
import operator
import struct
import zlib

from engine_layout import CompareWriteEntry
from process_memory import MemoryAccessError


ARCHIVE_MAGIC = b"CCZBOOL1"
ARCHIVE_HEADER = struct.Struct("<8sII")
MAX_ARCHIVE_COUNT = 65536


def _integer(value, label):
    try:
        return operator.index(value)
    except (TypeError, ValueError, OverflowError) as exc:
        raise ValueError("%s必须是整数" % label) from exc


@dataclass(frozen=True)
class BooleanLayout:
    __slots__ = ("base", "storage", "bit_order", "stride", "width",
                 "record_count", "writable_count", "browse_count")

    base: int
    storage: str
    bit_order: str
    stride: int
    width: int
    record_count: int
    writable_count: int
    browse_count: int

    def __post_init__(self):
        for name in ("base", "stride", "width", "record_count",
                     "writable_count", "browse_count"):
            object.__setattr__(self, name, _integer(getattr(self, name), name))
        # ``dword`` is the explicit native name emitted by legacy profiles;
        # it has the same addressing semantics as the generic array form.
        if self.storage not in ("bitset", "array", "dword"):
            raise ValueError("未知布尔存储格式: %s" % self.storage)
        if self.bit_order not in ("msb0", "lsb0"):
            raise ValueError("未知布尔位序: %s" % self.bit_order)
        if not (0 < self.record_count <= self.browse_count <= 0xFFFFFFFF):
            raise ValueError("布尔记录数或浏览范围无效")
        if not 0 <= self.writable_count <= self.record_count:
            raise ValueError("布尔可写范围越过记录范围")
        if self.storage == "bitset":
            if self.width != 1 or self.stride != 1:
                raise ValueError("位图布尔必须以字节承载")
        elif self.width not in (1, 2, 4) or self.stride < self.width:
            raise ValueError("布尔整数存储宽度或步长无效")
        if not 0x10000 <= self.base <= 0x7FFFFFFF:
            raise ValueError("布尔基址不在32位用户地址域")
        if self.base + self.byte_size - 1 > 0x7FFFFFFF:
            raise ValueError("布尔存储跨度越过32位用户地址域")

    @classmethod
    def from_profile(cls, profile):
        mapping = profile.metadata.get("variable_layouts", {}).get("variables_bool")
        if not isinstance(mapping, dict):
            raise ValueError("当前画像缺少布尔存储布局")
        key = mapping.get("addressKey", "bool_vars")
        if key not in profile.addresses:
            raise ValueError("当前画像缺少布尔基址")
        return cls.from_mapping(profile.addresses[key], mapping)

    @classmethod
    def from_mapping(cls, base, mapping):
        storage = mapping.get("storage", "array")
        record_count = _integer(mapping.get("recordCount", 0), "recordCount")
        writable_count = _integer(mapping.get("writableRecordCount", record_count),
                                  "writableRecordCount")
        if (storage == "bitset" and mapping.get("formatVerified") is not True or
                storage == "dword" and
                mapping.get("nativeCapacityVerified") is not True):
            writable_count = 0
        return cls(
            _integer(base, "base") + _integer(mapping.get("dataOffset", 0), "dataOffset"),
            storage, mapping.get("bitOrder", "lsb0"),
            _integer(mapping.get("stride", 1 if storage == "bitset" else 4), "stride"),
            _integer(mapping.get("width", 1 if storage == "bitset" else 4), "width"),
            record_count, writable_count,
            _integer(mapping.get("browseRecordCount", record_count), "browseRecordCount"))

    @property
    def byte_size(self):
        if self.storage == "bitset":
            return (self.browse_count + 7) // 8
        return (self.browse_count - 1) * self.stride + self.width

    def _check_index(self, index):
        index = _integer(index, "布尔编号")
        if not 0 <= index < self.browse_count:
            raise ValueError("布尔编号必须在 0..%d" % (self.browse_count - 1))
        return index

    def address(self, index):
        index = self._check_index(index)
        return self.base + (index // 8 if self.storage == "bitset" else
                            index * self.stride)

    def mask(self, index):
        index = self._check_index(index)
        if self.storage != "bitset":
            raise ValueError("整数布尔没有位掩码")
        return (0x80 >> (index % 8) if self.bit_order == "msb0" else
                1 << (index % 8))

    def span(self, start, count):
        start, count = _integer(start, "起始编号"), _integer(count, "数量")
        if start < 0 or count < 0 or start + count > self.browse_count:
            raise ValueError("布尔读取范围越界")
        if count == 0:
            if start > self.browse_count:
                raise ValueError("布尔读取范围越界")
            return self.base + (start // 8 if self.storage == "bitset" else
                                start * self.stride), 0
        address = self.address(start)
        if self.storage == "bitset":
            size = (start + count + 7) // 8 - start // 8
        else:
            size = (count - 1) * self.stride + self.width
        return address, size


@dataclass(frozen=True)
class BooleanSnapshot:
    __slots__ = ("layout", "start", "count", "raw", "session_generation")

    layout: BooleanLayout
    start: int
    count: int
    raw: bytes
    session_generation: str

    def __post_init__(self):
        if not isinstance(self.layout, BooleanLayout):
            raise ValueError("布尔快照布局无效")
        object.__setattr__(self, "start", _integer(self.start, "起始编号"))
        object.__setattr__(self, "count", _integer(self.count, "数量"))
        object.__setattr__(self, "raw", bytes(self.raw))
        object.__setattr__(self, "session_generation", str(self.session_generation))
        _address, size = self.layout.span(self.start, self.count)
        if len(self.raw) != size:
            raise ValueError("布尔快照原始字节数与布局不符")

    @property
    def first_address(self):
        return self.layout.span(self.start, self.count)[0]

    def covers(self, index):
        index = _integer(index, "布尔编号")
        return self.start <= index < self.start + self.count

    def address(self, index):
        if not self.covers(index):
            raise ValueError("布尔编号不在当前快照覆盖范围内")
        return self.layout.address(index)

    def raw_byte(self, reference_address):
        offset = _integer(reference_address, "参考地址") - self.first_address
        if not 0 <= offset < len(self.raw):
            raise ValueError("字节地址不在当前快照覆盖范围内")
        return self.raw[offset]

    def expected(self, index):
        offset = self.address(index) - self.first_address
        return self.raw[offset:offset + self.layout.width]

    def value(self, index):
        raw = self.expected(index)
        if self.layout.storage == "bitset":
            return bool(raw[0] & self.layout.mask(index))
        return bool(int.from_bytes(raw, "little", signed=False))

    def values(self, start=None, count=None):
        start = self.start if start is None else _integer(start, "起始编号")
        count = self.start + self.count - start if count is None else _integer(count, "数量")
        if start < self.start or count < 0 or start + count > self.start + self.count:
            raise ValueError("布尔遍历范围不在当前快照内")
        return (self.value(index) for index in range(start, start + count))


def read_boolean_snapshot(memory, layout, start=0, count=None):
    start = _integer(start, "起始编号")
    count = layout.browse_count - start if count is None else _integer(count, "数量")
    address, size = layout.span(start, count)
    raw = memory.read_bytes(address, size) if size else b""
    return BooleanSnapshot(layout, start, count, raw, memory.session_generation)


def commit_boolean_changes(memory, snapshot, edits):
    """Validate every edit, merge native ranges, then commit one transaction."""
    if getattr(memory, "closed", False):
        raise MemoryAccessError("游戏进程会话已关闭")
    if getattr(memory, "read_only", False):
        raise MemoryAccessError("只读进程会话禁止保存布尔变量")
    if str(memory.session_generation) != snapshot.session_generation:
        raise MemoryAccessError("布尔快照属于旧游戏会话，请刷新后重试")
    requested = {}
    for index, value in edits:
        index = _integer(index, "布尔编号")
        value = _integer(value, "布尔值")
        if value not in (0, 1):
            raise ValueError("布尔值仅接受 True/False/1/0")
        if not snapshot.covers(index):
            raise ValueError("布尔编号不在当前快照覆盖范围内")
        if not 0 <= index < snapshot.layout.writable_count:
            raise MemoryAccessError("布尔编号 %d 只读或超出原生可写范围" % index)
        if index in requested and requested[index] != value:
            raise ValueError("同一布尔编号包含互相冲突的修改")
        requested[index] = value
    changes = {index: value for index, value in requested.items()
               if snapshot.value(index) != bool(value)}
    if not changes:
        return snapshot, 0
    replacement = bytearray(snapshot.raw)
    ranges = []
    for index, value in sorted(changes.items()):
        offset = snapshot.address(index) - snapshot.first_address
        if snapshot.layout.storage == "bitset":
            mask = snapshot.layout.mask(index)
            replacement[offset] = (replacement[offset] | mask if value else
                                   replacement[offset] & ~mask)
            end = offset + 1
        else:
            end = offset + snapshot.layout.width
            replacement[offset:end] = value.to_bytes(snapshot.layout.width, "little")
        # Adjacent edited records can share one strict compare/write.  Gaps
        # are excluded; array records still compare their full native width.
        if ranges and offset <= ranges[-1][1]:
            ranges[-1] = (ranges[-1][0], max(ranges[-1][1], end))
        else:
            ranges.append((offset, end))
    entries = tuple(CompareWriteEntry(
        snapshot.first_address + start, snapshot.raw[start:end],
        bytes(replacement[start:end])) for start, end in ranges)
    memory.guarded_compare_exchange_transaction(entries)
    return BooleanSnapshot(snapshot.layout, snapshot.start, snapshot.count,
                           bytes(replacement), snapshot.session_generation), len(changes)


def encode_boolean_archive(snapshot):
    """Encode the complete writable logical domain as a compact MSB bitset."""
    count = snapshot.layout.writable_count
    if not 0 < count <= MAX_ARCHIVE_COUNT:
        raise ValueError("布尔导出数量必须在 1..%d" % MAX_ARCHIVE_COUNT)
    if snapshot.start != 0 or snapshot.count < count:
        raise ValueError("全量导出需要覆盖原生可写域的完整快照")
    if snapshot.layout.storage == "bitset" and snapshot.layout.bit_order == "msb0":
        payload = bytearray(snapshot.raw[:(count + 7) // 8])
    else:
        payload = bytearray((count + 7) // 8)
        for index in range(count):
            if snapshot.value(index):
                payload[index // 8] |= 0x80 >> (index % 8)
    if count % 8:
        payload[-1] &= (0xFF << (8 - count % 8)) & 0xFF
    material = struct.pack("<I", count) + bytes(payload)
    checksum = zlib.crc32(material) & 0xFFFFFFFF
    return ARCHIVE_HEADER.pack(ARCHIVE_MAGIC, count, checksum) + payload


def decode_boolean_archive(data, expected_count=None):
    data = bytes(data)
    if len(data) < ARCHIVE_HEADER.size:
        raise ValueError("布尔归档头不完整")
    magic, count, checksum = ARCHIVE_HEADER.unpack_from(data)
    if magic != ARCHIVE_MAGIC:
        raise ValueError("未知布尔归档格式")
    if not 0 < count <= MAX_ARCHIVE_COUNT:
        raise ValueError("布尔归档数量越界")
    if expected_count is not None and count != _integer(expected_count, "预期数量"):
        raise ValueError("布尔归档数量 %d 与当前原生可写数量 %d 不符" %
                         (count, expected_count))
    if len(data) != ARCHIVE_HEADER.size + (count + 7) // 8:
        raise ValueError("布尔归档长度不符或存在额外字节")
    payload = data[ARCHIVE_HEADER.size:]
    if (zlib.crc32(struct.pack("<I", count) + payload) & 0xFFFFFFFF) != checksum:
        raise ValueError("布尔归档CRC校验失败")
    if count % 8 and payload[-1] & ((1 << (8 - count % 8)) - 1):
        raise ValueError("布尔归档尾部填充位必须为零")
    return tuple(bool(payload[index // 8] & (0x80 >> (index % 8)))
                 for index in range(count))
