import hashlib
import struct
import unittest
from types import SimpleNamespace

from engine_layout import FieldSpec, SemanticAnchorRole
from engine_profile import MaskedSignature
from process_memory import (FakeMemoryBackend, MemorySession, MemoryPageInfo,
                            MEM_COMMIT, MEM_IMAGE, PAGE_READONLY,
                            PAGE_EXECUTE_READ, PAGE_NOACCESS)
from runtime_regions import (iter_runtime_regions, iter_mapped_regions,
                             scan_image_regions, scan_identity)
from semantic_resolver import SemanticAnchorPattern, SemanticTableResolver


BASE = 0x00400000


class RuntimeRegionTests(unittest.TestCase):
    def make(self, raw, protections=None, relocated=BASE):
        class PagedBackend(FakeMemoryBackend):
            def query_page(self, address):
                index = (address - relocated) // 0x1000
                return MemoryPageInfo(relocated + index * 0x1000, relocated,
                                      PAGE_READONLY, 0x1000, MEM_COMMIT,
                                      (protections or {}).get(index, PAGE_READONLY),
                                      MEM_IMAGE)
        backend = PagedBackend(relocated, bytes(raw), protection=PAGE_READONLY)
        session = MemorySession(1, relocated, len(raw), backend=backend,
                                owns_handle=False, reference_image_base=BASE)
        image = SimpleNamespace(image_base=BASE, size_of_image=len(raw),
                                sha256="same-disk-sha", session=session,
                                source="runtime")
        image.iter_scan_regions = lambda executable_only=True: iter_runtime_regions(
            image, session, executable_only)
        image.read_va = lambda address, size: session.read_bytes(address, size)
        return image, session, backend

    def test_live_readonly_pages_are_scanned_even_without_execute_flag(self):
        raw = bytearray(0x2000)
        raw[0x1120:0x1124] = b"\x55\x8B\xEC\xC3"
        image, _session, _backend = self.make(raw)
        regions = list(scan_image_regions(image))
        hits = [r.start + o for r in regions for o in
                MaskedSignature.parse("55 8B EC C3").find_all(r.data)]
        self.assertEqual([BASE + 0x1120], hits)
        self.assertEqual((), image.runtime_holes)

    def test_signature_crosses_adjacent_pages_and_keeps_reference_address(self):
        raw = bytearray(0x2000)
        raw[0xFFE:0x1002] = b"\x55\x8B\xEC\xC3"
        image, _session, _backend = self.make(raw, {1: PAGE_EXECUTE_READ}, BASE + 0x100000)
        regions = list(scan_image_regions(image))
        self.assertEqual(1, len(regions))
        self.assertEqual(BASE, regions[0].start)
        self.assertEqual(BASE + 0x100000, regions[0].actual_start)
        self.assertEqual([0xFFE], MaskedSignature.parse("55 8B EC C3").find_all(regions[0].data))

    def test_unreadable_hole_is_never_zero_filled_or_joined(self):
        raw = bytearray(0x3000)
        raw[0xFFF] = 0xAA
        raw[0x2000] = 0xBB
        image, _session, _backend = self.make(raw, {1: PAGE_NOACCESS})
        regions = list(scan_image_regions(image))
        self.assertEqual([0x1000, 0x1000], [len(r.data) for r in regions])
        self.assertEqual(1, len(image.runtime_holes))
        self.assertTrue(all(not MaskedSignature.parse("AA 00").find_all(r.data) for r in regions))

    def test_refresh_and_new_session_invalidate_content_identity(self):
        image, session, backend = self.make(bytes(0x1000))
        first = scan_identity(image, tuple(scan_image_regions(image)))
        backend.region.data[0x40] = 1
        image._runtime_regions_cache = None
        second = scan_identity(image, tuple(scan_image_regions(image)))
        self.assertNotEqual(first, second)
        other, other_session, _ = self.make(bytes(0x1000))
        self.assertNotEqual(session.session_generation, other_session.session_generation)
        self.assertNotEqual(first, scan_identity(other, tuple(scan_image_regions(other))))

    def test_rva_capture_ignores_protected_disk_section_flags(self):
        image = SimpleNamespace(data=b"\x90\x55\x8B\xEC\xC3", size_of_image=5,
                                image_base=BASE, sections=[])
        regions = list(iter_mapped_regions(image))
        self.assertEqual(b"\x90\x55\x8B\xEC\xC3", regions[0].data)

    def test_semantic_cache_cannot_cross_sessions_or_mutated_runtime_code(self):
        raw = bytearray(b"\x90" * 0x1000)
        raw[0x20:0x27] = b"\x68" + struct.pack("<I", 0x500C3B) + b"\x6A\x10"
        raw[0x60:0x65] = b"\x6B\xC0\x10\x3B\xC1"
        image, _session, backend = self.make(raw)
        anchors = (SemanticAnchorPattern(SemanticAnchorRole.INITIALIZER,
                                         "68 ?? ?? ?? ?? 6A 10", 1),
                   SemanticAnchorPattern(SemanticAnchorRole.READER,
                                         "6B C0 10 3B C1"))
        first, _, reason = SemanticTableResolver(image).resolve(
            "effect", 255, 16, (FieldSpec("person", 0, 2),), anchors,
            runtime_base=0x507800, generation="first")
        self.assertEqual("", reason)
        second, _, _ = SemanticTableResolver(image).resolve(
            "effect", 255, 16, (FieldSpec("person", 0, 2),), anchors,
            runtime_base=0x511000, generation="second")
        self.assertEqual(0x511000, second.runtime_base)
        self.assertEqual("second", second.generation)
        self.assertIsNot(first, second)
        backend.region.data[0x60] = 0x90
        image._runtime_regions_cache = None
        descriptor, _, reason = SemanticTableResolver(image).resolve(
            "effect", 255, 16, (FieldSpec("person", 0, 2),), anchors,
            runtime_base=0x507800, generation="first")
        self.assertIsNone(descriptor)
        self.assertIn("reader", reason)


if __name__ == "__main__":
    unittest.main()
