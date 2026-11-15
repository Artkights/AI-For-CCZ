"""Readable module regions shared by runtime signature and semantic scans.

Disk section flags describe the protected file, not the unpacked process.
Unreadable pages are gaps, never zero-filled signature input.  Addresses in
ScanRegion.start use the image's preferred VA; actual_start is a process VA.
This module has no dependency on engine_profile/process_memory.
"""

from dataclasses import dataclass
import hashlib
import struct


@dataclass(frozen=True)
class ScanRegion:
    start: int
    data: bytes
    executable: bool = False
    actual_start: int = 0


_READABLE = frozenset((0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80))
_EXECUTABLE = frozenset((0x10, 0x20, 0x40, 0x80))


def _coalesce(regions):
    result = []
    pending = []
    start = actual = end = 0
    executable = False
    for region in regions:
        if not region.data:
            continue
        if pending and (region.start != end or
                        region.actual_start != actual + end - start):
            result.append(ScanRegion(start, b"".join(pending), executable, actual))
            pending = []
        if not pending:
            start, actual = region.start, region.actual_start
            executable = False
        pending.append(region.data)
        end = region.start + len(region.data)
        executable = executable or region.executable
    if pending:
        result.append(ScanRegion(start, b"".join(pending), executable, actual))
    return tuple(result)


def _set_generation(image, regions):
    digest = hashlib.sha256()
    for region in regions:
        digest.update(struct.pack("<II", region.start, len(region.data)))
        digest.update(region.data)
    image.code_generation = digest.hexdigest()
    return regions


def iter_runtime_regions(image, session, executable_only=True):
    """Snapshot committed readable pages of the *live* main module.

    executable_only intentionally does not trust disk PE execute flags: a
    protector can restore executable code in a disk READONLY section.  Callers
    verify instruction semantics after finding candidates in readable memory.
    refresh() on the image invalidates this per-scan snapshot.
    """
    cached = getattr(image, "_runtime_regions_cache", None)
    if cached is not None:
        yield from cached
        return
    reference = int(image.image_base)
    actual_base = int(session.module_base)
    length = min(int(image.size_of_image), int(session.module_size))
    limit = actual_base + length
    cursor = actual_base
    regions = []
    holes = []
    while cursor < limit:
        try:
            info = session.query_page(cursor, actual_address=True)
            end = min(limit, int(info.base_address) + int(info.region_size))
            if end <= cursor:
                raise ValueError("non-advancing VirtualQueryEx region")
            protect = int(info.protect)
            valid = (int(info.state) == 0x1000 and
                     not protect & (0x01 | 0x100) and
                     protect & 0xFF in _READABLE)
        except Exception as exc:
            end = min(limit, (cursor // 0x1000 + 1) * 0x1000)
            holes.append((reference + cursor - actual_base, end - cursor, str(exc)))
            cursor = end
            continue
        if not valid:
            holes.append((reference + cursor - actual_base, end - cursor,
                          "uncommitted, guarded or inaccessible"))
            cursor = end
            continue
        # A read can race with protection changes.  Isolate failed pages while
        # retaining adjacent readable pages and valid cross-page signatures.
        while cursor < end:
            page_end = min(end, (cursor // 0x1000 + 1) * 0x1000)
            size = page_end - cursor
            try:
                raw = session.read_actual_bytes(cursor, size)
                if len(raw) != size:
                    raise ValueError("short ReadProcessMemory result")
                regions.append(ScanRegion(reference + cursor - actual_base,
                                          bytes(raw), protect & 0xFF in _EXECUTABLE,
                                          cursor))
            except Exception as exc:
                holes.append((reference + cursor - actual_base, size, str(exc)))
            cursor = page_end
    cached = _set_generation(image, _coalesce(regions))
    image._runtime_regions_cache = cached
    image.runtime_holes = tuple(holes)
    image.session_generation = str(getattr(session, "session_generation", id(session)))
    yield from cached


def iter_mapped_regions(image, executable_only=True):
    """Scan an RVA capture as an RVA image, including protected original code.

    Capture producers may supply unreadable_ranges as (RVA, length) pairs.
    With no page map this is static capture evidence, not a live page probe.
    """
    raw = bytes(image.data[:image.size_of_image])
    holes = sorted(getattr(image, "unreadable_ranges", ()))
    cursor = 0
    regions = []
    for start, size in holes:
        start = min(len(raw), max(cursor, int(start)))
        if start > cursor:
            regions.append(ScanRegion(image.image_base + cursor, raw[cursor:start],
                                      False, image.image_base + cursor))
        cursor = min(len(raw), start + max(0, int(size)))
    if cursor < len(raw):
        regions.append(ScanRegion(image.image_base + cursor, raw[cursor:], False,
                                  image.image_base + cursor))
    yield from _set_generation(image, tuple(regions))


def scan_image_regions(image, executable_only=True):
    provider = getattr(image, "iter_scan_regions", None)
    if provider is not None:
        yield from provider(executable_only=executable_only)
        return
    for section in image.sections:
        if executable_only and not section.executable:
            continue
        if hasattr(image, "section_data"):
            raw = image.section_data(section)
        else:
            raw = image.data[section.raw_offset:section.raw_offset + section.raw_size]
        yield ScanRegion(image.image_base + section.virtual_address, bytes(raw),
                         bool(section.executable),
                         image.image_base + section.virtual_address)


def scan_identity(image, regions):
    """A session and content identity, including captures with the same disk SHA."""
    digest = hashlib.sha256()
    for region in regions:
        digest.update(struct.pack("<II", region.start, len(region.data)))
        digest.update(region.data)
    session = getattr(image, "session", None)
    return (str(image.sha256), str(getattr(image, "source", "disk")),
            str(getattr(session, "session_generation",
                        getattr(image, "session_generation", "disk"))),
            int(getattr(session, "module_base", image.image_base)),
            digest.hexdigest())
