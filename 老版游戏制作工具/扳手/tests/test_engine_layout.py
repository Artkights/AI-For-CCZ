import unittest

from engine_layout import (AddressResolverKind, CompareWriteEntry,
                           FeatureAccess, FieldSpec, OpaqueEmptyToken,
                           RawRecordSnapshot, ResolvedFeature,
                           TableLayoutSpec)
from process_memory import FakeMemoryBackend, MemoryAccessError, MemorySession


BASE = 0x00400000


class EngineLayoutTests(unittest.TestCase):
    def test_access_levels_do_not_mix_read_and_write(self):
        self.assertFalse(FeatureAccess.UNAVAILABLE.readable)
        self.assertTrue(FeatureAccess.READ_ONLY.readable)
        self.assertFalse(FeatureAccess.READ_ONLY.writable)
        self.assertTrue(FeatureAccess.READ_WRITE.writable)
        feature = ResolvedFeature("effect", FeatureAccess.READ_ONLY)
        self.assertTrue(feature.readable)
        self.assertFalse(feature.writable)

    def test_layout_rejects_fields_outside_record(self):
        with self.assertRaises(ValueError):
            TableLayoutSpec(
                "bad", AddressResolverKind.FIXED_RVA, BASE,
                1, 8, (FieldSpec("outside", 7, 2),))

    def test_snapshot_requires_pointer_source_and_value_together(self):
        with self.assertRaises(ValueError):
            RawRecordSnapshot("effect", BASE, b"\0" * 8, None, BASE,
                              pointer_reference=BASE + 4)
        token = OpaqueEmptyToken(1278, "character")
        snapshot = RawRecordSnapshot(
            "effect", BASE, b"\0" * 8, token, BASE)
        self.assertEqual(1278, snapshot.decoded.raw_value)

    def test_compare_exchange_writes_only_when_all_expected_bytes_match(self):
        backend = FakeMemoryBackend(BASE, b"\x01" + b"\0" * 15 + b"\x02")
        session = MemorySession(1, BASE, 17, backend=backend,
                                owns_handle=False,
                                reference_image_base=BASE,
                                direct_write=False)
        session.compare_exchange_transaction((
            CompareWriteEntry(BASE, b"\x01", b"\x11"),
            CompareWriteEntry(BASE + 16, b"\x02", b"\x22"),
        ))
        self.assertEqual(b"\x11", backend.read(BASE, 1))
        self.assertEqual(b"\x22", backend.read(BASE + 16, 1))

        backend.write(BASE + 16, b"\x33")
        with self.assertRaisesRegex(MemoryAccessError, "刷新后重试"):
            session.compare_exchange_transaction((
                CompareWriteEntry(BASE, b"\x11", b"\x44"),
                CompareWriteEntry(BASE + 16, b"\x22", b"\x55"),
            ))
        self.assertEqual(b"\x11", backend.read(BASE, 1))
        self.assertEqual(b"\x33", backend.read(BASE + 16, 1))

    def test_compare_exchange_rolls_back_short_write(self):
        original = b"abcdefgh"
        backend = FakeMemoryBackend(BASE, original + b"\0" * 16)
        session = MemorySession(1, BASE, 24, backend=backend,
                                owns_handle=False,
                                reference_image_base=BASE,
                                direct_write=False)
        backend.partial_write_at = BASE
        with self.assertRaises(MemoryAccessError):
            session.compare_exchange_transaction((
                CompareWriteEntry(BASE, original, b"ABCDEFGH"),
            ))
        self.assertEqual(original, backend.read(BASE, len(original)))

    def test_legacy_write_guard_checks_full_record_and_tracks_field_writes(self):
        backend = FakeMemoryBackend(BASE, b"abcdefgh" + b"\0" * 8)
        session = MemorySession(1, BASE, 16, backend=backend,
                                owns_handle=False,
                                reference_image_base=BASE,
                                direct_write=False)
        session.set_write_guard("person.main", BASE, b"abcdefgh")
        session.verify_write_guards("person.")
        import ctypes
        value = ctypes.c_uint16(0x5A59)
        self.assertEqual(1, session.WriteProcessMemory(
            1, BASE + 2, ctypes.byref(value), 2, None))
        self.assertEqual(b"abYZefgh", backend.read(BASE, 8))
        session.verify_write_guards("person.")
        backend.write(BASE + 7, b"!")
        with self.assertRaisesRegex(MemoryAccessError, "刷新后重试"):
            session.verify_write_guards("person.")


if __name__ == "__main__":
    unittest.main()
