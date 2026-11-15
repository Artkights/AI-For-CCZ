import struct
import unittest
from dataclasses import FrozenInstanceError
from types import SimpleNamespace
from unittest.mock import patch
import zlib

from process_memory import FakeMemoryBackend, MemoryAccessError, MemorySession
from variable_storage import (ARCHIVE_HEADER, ARCHIVE_MAGIC,
                              BooleanLayout, BooleanSnapshot,
                              commit_boolean_changes, decode_boolean_archive,
                              encode_boolean_archive, read_boolean_snapshot)


BASE = 0x00400000


def make_session(raw, direct_write=True):
    backend = FakeMemoryBackend(BASE, raw)
    session = MemorySession(1, BASE, len(raw), backend=backend,
                            owns_handle=False, reference_image_base=BASE,
                            direct_write=direct_write)
    return session, backend


def bit_layout(count=32, writable=None, browse=None, order="msb0", base=BASE):
    return BooleanLayout(base, "bitset", order, 1, 1, count,
                         count if writable is None else writable,
                         count if browse is None else browse)


def array_layout(count=4, stride=4, width=4, writable=None, base=BASE):
    return BooleanLayout(base, "array", "lsb0", stride, width, count,
                         count if writable is None else writable, count)


def archive_with(count, payload, magic=ARCHIVE_MAGIC):
    checksum = zlib.crc32(struct.pack("<I", count) + payload) & 0xFFFFFFFF
    return ARCHIVE_HEADER.pack(magic, count, checksum) + payload


class BooleanLayoutTests(unittest.TestCase):
    def test_native_layout_from_profile_resolves_offset_and_verified_domain(self):
        profile = SimpleNamespace(
            addresses={"bool_vars": BASE}, metadata={"variable_layouts": {
                "variables_bool": {"storage": "bitset", "bitOrder": "msb0",
                                   "recordCount": 1536,
                                   "writableRecordCount": 1536,
                                   "browseRecordCount": 2048,
                                   "formatVerified": True, "dataOffset": 8}}})
        layout = BooleanLayout.from_profile(profile)
        self.assertEqual(BASE + 8, layout.base)
        self.assertEqual((1536, 1536, 2048),
                         (layout.record_count, layout.writable_count,
                          layout.browse_count))
        self.assertEqual(256, layout.byte_size)
        self.assertFalse(hasattr(layout, "__dict__"))

    def test_unverified_bitset_cannot_become_writable(self):
        for verified in (False, None, "true", 1):
            with self.subTest(verified=verified):
                layout = BooleanLayout.from_mapping(BASE, {
                    "storage": "bitset", "recordCount": 32,
                    "formatVerified": verified})
                self.assertEqual(0, layout.writable_count)

    def test_legacy_defaults_use_dword_array(self):
        layout = BooleanLayout.from_mapping(BASE, {"recordCount": 1000})
        self.assertEqual(("array", 4, 4, 4000),
                         (layout.storage, layout.width, layout.stride,
                          layout.byte_size))

    def test_unverified_legacy_dword_cannot_become_writable(self):
        layout = BooleanLayout.from_mapping(BASE, {
            "storage": "dword", "recordCount": 2048,
            "nativeCapacityVerified": False, "stride": 4, "width": 4})
        self.assertEqual(0, layout.writable_count)
        self.assertEqual(8192, layout.byte_size)

    def test_physical_address_masks_and_non_aligned_spans(self):
        msb, lsb = bit_layout(), bit_layout(order="lsb0")
        for index, address, mask in ((0, BASE, 0x80), (1, BASE, 0x40),
                                     (7, BASE, 1), (8, BASE + 1, 0x80),
                                     (31, BASE + 3, 1)):
            self.assertEqual(address, msb.address(index))
            self.assertEqual(mask, msb.mask(index))
            self.assertEqual(1 << (index % 8), lsb.mask(index))
        self.assertEqual((BASE, 2), msb.span(7, 2))
        self.assertEqual((BASE + 1, 2), msb.span(9, 15))
        self.assertEqual((BASE + 4, 0), msb.span(32, 0))
        native = array_layout(stride=8)
        self.assertEqual((BASE + 8, 12), native.span(1, 2))
        self.assertEqual(28, native.byte_size)
        with self.assertRaises(ValueError):
            native.mask(0)

    def test_invalid_layouts_and_ranges_rejected(self):
        valid = [BASE, "bitset", "msb0", 1, 1, 32, 32, 32]
        for position, value in ((0, 0), (0, 0x80000000), (1, "unknown"),
                                (2, "unknown"), (3, 2), (4, 2), (5, 0),
                                (6, -1), (6, 33), (7, 31), (5, 2.0)):
            args = list(valid)
            args[position] = value
            with self.subTest(position=position, value=value):
                with self.assertRaises(ValueError):
                    BooleanLayout(*args)
        with self.assertRaises(ValueError):
            bit_layout(base=0x7FFFFFFF)
        for width, stride in ((3, 4), (4, 2), (1, 0)):
            with self.assertRaises(ValueError):
                array_layout(stride=stride, width=width)
        layout = bit_layout()
        for start, count in ((-1, 1), (0, -1), (31, 2), (33, 0), (0.0, 1)):
            with self.assertRaises(ValueError):
                layout.span(start, count)
        for index in (-1, 32, "1", 1.5):
            with self.assertRaises(ValueError):
                layout.address(index)


class BooleanSnapshotTests(unittest.TestCase):
    def test_full_domain_read_is_one_packed_read_and_immutable_snapshot(self):
        session, _backend = make_session(bytes(range(192)) + b"adjacent")
        layout = bit_layout(1536)
        with patch.object(session, "read_bytes", wraps=session.read_bytes) as read:
            snapshot = read_boolean_snapshot(session, layout)
        read.assert_called_once_with(BASE, 192)
        self.assertEqual(bytes(range(192)), snapshot.raw)
        self.assertFalse(hasattr(snapshot, "__dict__"))
        with self.assertRaises(FrozenInstanceError):
            snapshot.raw = b"x"
        self.assertEqual(bool(191 & 1), snapshot.value(1535))

    def test_non_aligned_snapshot_only_exposes_its_logical_range(self):
        session, _backend = make_session(b"\x01\x80\x01\xFF")
        snapshot = read_boolean_snapshot(session, bit_layout(), 7, 10)
        self.assertEqual(b"\x01\x80\x01", snapshot.raw)
        self.assertEqual(BASE, snapshot.first_address)
        self.assertEqual((True, True) + (False,) * 8, tuple(snapshot.values()))
        self.assertEqual((True, False), tuple(snapshot.values(8, 2)))
        self.assertEqual(0x80, snapshot.raw_byte(BASE + 1))
        for index in (6, 17):
            with self.assertRaises(ValueError):
                snapshot.value(index)
        with self.assertRaises(ValueError):
            snapshot.raw_byte(BASE + 3)
        with self.assertRaises(ValueError):
            tuple(snapshot.values(6, 1))
        with self.assertRaises(ValueError):
            tuple(snapshot.values(8, 10))

    def test_tail_and_empty_reads_are_exact(self):
        session, _backend = make_session(b"\xA5\xF0neighbor")
        layout = bit_layout(12)
        snapshot = read_boolean_snapshot(session, layout, 10, 2)
        self.assertEqual(b"\xF0", snapshot.raw)
        self.assertEqual((True, True), tuple(snapshot.values()))
        with patch.object(session, "read_bytes", wraps=session.read_bytes) as read:
            empty = read_boolean_snapshot(session, layout, 12, 0)
        read.assert_not_called()
        self.assertEqual((), tuple(empty.values()))
        self.assertEqual(b"", empty.raw)

    def test_snapshot_rejects_wrong_byte_span(self):
        for start, count, raw in ((0, 32, b"\0"), (7, 2, b"\0"),
                                  (0, 0, b"\0")):
            with self.assertRaises(ValueError):
                BooleanSnapshot(bit_layout(), start, count, raw, "generation")


class BooleanCommitTests(unittest.TestCase):
    def test_same_byte_and_adjacent_edits_merge_clean_gap_is_excluded(self):
        session, backend = make_session(b"\0" * 8)
        snapshot = read_boolean_snapshot(session, bit_layout(64))
        with patch.object(session, "guarded_compare_exchange_transaction",
                          wraps=session.guarded_compare_exchange_transaction) as transaction:
            updated, changed = commit_boolean_changes(
                session, snapshot, ((0, True), (1, True), (7, True),
                                    (8, True), (24, True)))
        entries = transaction.call_args[0][0]
        self.assertEqual(2, len(entries))
        self.assertEqual((BASE, b"\0\0", b"\xC1\x80"),
                         (entries[0].address, entries[0].expected,
                          entries[0].replacement))
        self.assertEqual((BASE + 3, b"\0", b"\x80"),
                         (entries[1].address, entries[1].expected,
                          entries[1].replacement))
        self.assertEqual(5, changed)
        self.assertEqual(b"\xC1\x80\0\x80\0\0\0\0", backend.read(BASE, 8))
        self.assertEqual(b"\0" * 8, snapshot.raw)
        self.assertEqual(backend.read(BASE, 8), updated.raw)

    def test_all_native_bits_can_toggle_and_restore_without_neighbor_changes(self):
        for order in ("msb0", "lsb0"):
            for count in (1, 9, 255, 256, 1536):
                with self.subTest(order=order, count=count):
                    size = (count + 7) // 8
                    raw = bytes((index * 37 + 11) & 255 for index in range(size))
                    session, backend = make_session(raw + b"person-data")
                    original = read_boolean_snapshot(
                        session, bit_layout(count, order=order))
                    enabled, _ = commit_boolean_changes(
                        session, original, ((index, 1) for index in range(count)))
                    self.assertTrue(all(enabled.values()))
                    disabled, _ = commit_boolean_changes(
                        session, enabled, ((index, 0) for index in range(count)))
                    self.assertFalse(any(disabled.values()))
                    restored, _ = commit_boolean_changes(
                        session, disabled, enumerate(original.values()))
                    self.assertEqual(raw, restored.raw)
                    self.assertEqual(raw + b"person-data", backend.read(BASE, size + 11))

    def test_non_aligned_edit_preserves_outside_bits_and_tail_padding(self):
        session, backend = make_session(b"\xAB\x5A\xA5")
        original = read_boolean_snapshot(session, bit_layout(20), 7, 13)
        updated, _ = commit_boolean_changes(
            session, original, ((7, 0), (8, 1), (19, 1)))
        self.assertEqual(b"\xAA\xDA\xB5", updated.raw)
        self.assertEqual(b"\xAA\xDA\xB5", backend.read(BASE, 3))

    def test_reserved_and_person_table_indexes_rejected_before_transaction(self):
        session, backend = make_session(b"\0" * 256 + struct.pack("<I", 0x11223344))
        snapshot = read_boolean_snapshot(session, bit_layout(1536, browse=2048))
        before = bytes(backend.region.data)
        for index in (1536, 2047, 2048, 4095, -1):
            with self.subTest(index=index):
                with patch.object(session, "guarded_compare_exchange_transaction") as transaction:
                    with self.assertRaises((ValueError, MemoryAccessError)):
                        commit_boolean_changes(session, snapshot, ((index, 1),))
                    transaction.assert_not_called()
        self.assertEqual(before, bytes(backend.region.data))

    def test_invalid_edits_fail_before_any_valid_write(self):
        session, backend = make_session(b"\0" * 4)
        snapshot = read_boolean_snapshot(session, bit_layout())
        for index, value in ((1.0, 1), ("1", 1), (1, "True"), (1, 2),
                             (1, -1), (1, 0.0)):
            with self.subTest(index=index, value=value):
                with self.assertRaises(ValueError):
                    commit_boolean_changes(session, snapshot, ((0, 1), (index, value)))
                self.assertEqual(b"\0" * 4, backend.read(BASE, 4))

    def test_unchanged_and_duplicate_edits_do_not_create_redundant_writes(self):
        session, backend = make_session(b"\x80\0\0\0")
        snapshot = read_boolean_snapshot(session, bit_layout())
        with patch.object(session, "guarded_compare_exchange_transaction") as transaction:
            same, count = commit_boolean_changes(session, snapshot, ((0, 1), (0, True)))
        transaction.assert_not_called()
        self.assertIs(snapshot, same)
        self.assertEqual(0, count)
        updated, count = commit_boolean_changes(session, snapshot, ((1, 1), (1, True)))
        self.assertEqual(1, count)
        self.assertEqual(b"\xC0\0\0\0", updated.raw)
        with self.assertRaises(ValueError):
            commit_boolean_changes(session, updated, ((1, 0), (1, 1)))
        self.assertEqual(updated.raw, backend.read(BASE, 4))

    def test_session_lifecycle_guards_also_apply_in_direct_compat_mode(self):
        session, backend = make_session(b"\0" * 4)
        snapshot = read_boolean_snapshot(session, bit_layout())
        backend.write(BASE, b"\x01")
        with self.assertRaisesRegex(MemoryAccessError, "刷新后重试"):
            commit_boolean_changes(session, snapshot, ((0, 1),))
        self.assertEqual(b"\x01", backend.read(BASE, 1))
        old_generation = session.session_generation
        session.session_generation = "different-generation"
        with self.assertRaisesRegex(MemoryAccessError, "旧游戏会话"):
            commit_boolean_changes(session, snapshot, ((0, 1),))
        session.session_generation = old_generation
        session.read_only = True
        with self.assertRaisesRegex(MemoryAccessError, "只读"):
            commit_boolean_changes(session, snapshot, ((0, 1),))
        session.read_only = False
        session.close()
        with self.assertRaisesRegex(MemoryAccessError, "关闭"):
            commit_boolean_changes(session, snapshot, ((0, 1),))

    def test_partial_write_rolls_back_all_ranges_and_keeps_original_snapshot(self):
        session, backend = make_session(b"\0" * 8)
        original = read_boolean_snapshot(session, bit_layout(64))
        real_write = backend.write
        attempts = []

        def fail_once(address, data):
            attempts.append(address)
            if address == BASE + 4 and attempts.count(address) == 1:
                real_write(address, data[:1])
                return 1
            return real_write(address, data)

        with patch.object(backend, "write", side_effect=fail_once):
            with self.assertRaises(MemoryAccessError):
                commit_boolean_changes(session, original,
                                       ((0, 1), (32, 1), (40, 1)))
        self.assertEqual(b"\0" * 8, backend.read(BASE, 8))
        self.assertEqual(b"\0" * 8, original.raw)
        self.assertIn(BASE, attempts)
        self.assertEqual(2, attempts.count(BASE + 4))

    def test_legacy_dword_preserves_truthy_values_and_stride_padding(self):
        raw = struct.pack("<I", 0xFFFFFFFF) + b"PAD!" + struct.pack("<I", 0)
        session, backend = make_session(raw + b"neighbor")
        original = read_boolean_snapshot(session, array_layout(2, stride=8))
        updated, count = commit_boolean_changes(session, original, ((0, True), (1, True)))
        self.assertEqual(1, count)
        self.assertEqual(struct.pack("<I", 0xFFFFFFFF) + b"PAD!" + b"\x01\0\0\0",
                         updated.raw)
        cleared, count = commit_boolean_changes(session, updated, ((0, False),))
        self.assertEqual(1, count)
        self.assertEqual(b"\0\0\0\0PAD!\x01\0\0\0", cleared.raw)
        self.assertEqual(b"neighbor", backend.read(BASE + 12, 8))

    def test_legacy_conflict_compares_high_bytes_and_excludes_clean_padding(self):
        session, backend = make_session(b"\0\0\0\0PAD!\0\0\0\0")
        original = read_boolean_snapshot(session, array_layout(2, stride=8))
        backend.write(BASE + 3, b"\x01")
        with self.assertRaisesRegex(MemoryAccessError, "刷新后重试"):
            commit_boolean_changes(session, original, ((0, 1), (1, 1)))
        self.assertEqual(b"\0\0\0\0", backend.read(BASE + 8, 4))
        refreshed = read_boolean_snapshot(session, original.layout)
        backend.write(BASE + 4, b"free")
        with patch.object(session, "guarded_compare_exchange_transaction",
                          wraps=session.guarded_compare_exchange_transaction) as transaction:
            updated, _ = commit_boolean_changes(session, refreshed, ((0, 0), (1, 1)))
        self.assertEqual(2, len(transaction.call_args[0][0]))
        self.assertEqual(b"free", backend.read(BASE + 4, 4))
        self.assertEqual(b"\0\0\0\0", updated.expected(0))
        self.assertEqual(b"\x01\0\0\0", updated.expected(1))


class BooleanArchiveTests(unittest.TestCase):
    def test_full_domain_archives_are_compact_portable_and_roundtrip(self):
        for order in ("msb0", "lsb0"):
            for count in (1, 7, 8, 9, 1536, 65536):
                with self.subTest(order=order, count=count):
                    raw = bytes((index * 13 + 39) & 255
                                for index in range((count + 7) // 8))
                    session, _backend = make_session(raw)
                    snapshot = read_boolean_snapshot(session, bit_layout(count, order=order))
                    archive = encode_boolean_archive(snapshot)
                    self.assertEqual(16 + (count + 7) // 8, len(archive))
                    self.assertEqual(tuple(snapshot.values()),
                                     decode_boolean_archive(archive, count))
        raw = struct.pack("<IIII", 0xFFFFFFFF, 0, 0x80000000, 1)
        session, _backend = make_session(raw)
        snapshot = read_boolean_snapshot(session, array_layout())
        self.assertEqual((True, False, True, True),
                         decode_boolean_archive(encode_boolean_archive(snapshot), 4))

    def test_reserved_browse_records_are_not_exported(self):
        session, _backend = make_session(b"\xA5\xFF\xFF\xFF")
        snapshot = read_boolean_snapshot(session, bit_layout(8, browse=32))
        self.assertEqual((True, False, True, False, False, True, False, True),
                         decode_boolean_archive(encode_boolean_archive(snapshot), 8))
        with self.assertRaises(ValueError):
            encode_boolean_archive(read_boolean_snapshot(session, snapshot.layout, 1, 7))
        with self.assertRaises(ValueError):
            encode_boolean_archive(read_boolean_snapshot(
                session, bit_layout(8, writable=0, browse=32)))

    def test_invalid_archives_are_rejected_before_import(self):
        valid = archive_with(9, b"\xA5\x80")
        invalid = [b"", valid[:15], valid[:-1], valid + b"\0",
                   archive_with(9, b"\xA5\x80", b"CCZBOOL2"),
                   archive_with(0, b""), archive_with(65537, b""),
                   archive_with(9, b"\xA5\x81")]
        corrupted = bytearray(valid)
        corrupted[-1] ^= 0x80
        invalid.append(bytes(corrupted))
        corrupted_count = bytearray(valid)
        corrupted_count[8] = 10
        invalid.append(bytes(corrupted_count))
        for raw in invalid:
            with self.subTest(raw=raw[:20]):
                with self.assertRaises(ValueError):
                    decode_boolean_archive(raw)
        with self.assertRaises(ValueError):
            decode_boolean_archive(valid, 10)


if __name__ == "__main__":
    unittest.main()
