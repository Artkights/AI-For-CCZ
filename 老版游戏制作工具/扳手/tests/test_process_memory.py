import ctypes
import struct
import unittest
from types import SimpleNamespace

from capability_evidence import CapabilityState, FeatureCapability
from engine_layout import CompareWriteEntry
from engine_profile import (DynamicCallSite, DynamicPatchSpec, PatchSite,
                            PatchSpec, RemoteCallSpec, StubRelocation)
from hook import HookError, my_hook
from process_memory import (FakeMemoryBackend, MemoryAccessError, MemorySession,
                            MemoryPageInfo, PatchError, MEM_COMMIT, MEM_IMAGE,
                            PAGE_EXECUTE_READ, PAGE_EXECUTE_READWRITE,
                            PAGE_EXECUTE_WRITECOPY)


BASE = 0x100000


def make_session(data=b"\0" * 0x100, direct_write=False):
    backend = FakeMemoryBackend(BASE, data)
    session = MemorySession(BASE, BASE, len(data), backend=backend,
                            owns_handle=False, reference_image_base=BASE,
                            direct_write=direct_write)
    return session, backend


class ProcessMemoryTests(unittest.TestCase):
    def test_read_only_session_rejects_all_mutating_entry_points(self):
        backend = FakeMemoryBackend(BASE, b"\x74" + b"\0" * 0xFF)
        session = MemorySession(
            1, BASE, 0x100, backend=backend, owns_handle=False,
            reference_image_base=BASE, direct_write=True, read_only=True)
        original = backend.read(BASE, 0x100)
        with self.assertRaises(MemoryAccessError):
            session.write_u8(BASE, 0x90)
        with self.assertRaises(MemoryAccessError):
            session.write_actual_bytes(BASE, b"\x90")
        with self.assertRaises(MemoryAccessError):
            session.write_code_bytes(BASE, b"\x90", expected=b"\x74")
        patch_spec = PatchSpec(
            "readonly", "只读补丁", (PatchSite(BASE, b"\x74", b"\xEB"),))
        with self.assertRaises(MemoryAccessError):
            session.set_patch(patch_spec, True)
        dynamic_spec = DynamicPatchSpec(
            "readonly-dynamic", "只读动态补丁",
            (DynamicCallSite(BASE, b"\x74\0\0\0\0", 5, 0xE9),),
            b"\xC3")
        with self.assertRaises(MemoryAccessError):
            session.set_dynamic_patch(dynamic_spec, True)
        with self.assertRaises(MemoryAccessError):
            with session.remote_backend():
                pass
        source = ctypes.create_string_buffer(b"\x90")
        self.assertEqual(0, session.WriteProcessMemory(
            session.handle, BASE, source, 1, None))
        self.assertIn("只读进程会话", session.last_error)
        self.assertEqual(original, backend.read(BASE, 0x100))
        self.assertEqual({}, backend.allocations)

    def test_code_patch_forces_and_restores_execute_writecopy_page(self):
        backend = FakeMemoryBackend(
            BASE, b"\x74" + b"\0" * 0x20,
            protection=PAGE_EXECUTE_WRITECOPY)
        session = MemorySession(BASE, BASE, 0x21, backend=backend,
                                owns_handle=False, reference_image_base=BASE,
                                direct_write=False)
        spec = PatchSpec("no_ai", "禁止AI", (
            PatchSite(BASE, b"\x74", b"\xEB"),
        ))
        session.set_patch(spec, True)
        self.assertEqual(b"\xEB", backend.read(BASE, 1))
        self.assertEqual(PAGE_EXECUTE_WRITECOPY, backend.region.protection)
        self.assertEqual(PAGE_EXECUTE_READWRITE,
                         backend.protect_calls[0][2])
        self.assertEqual(PAGE_EXECUTE_WRITECOPY,
                         backend.protect_calls[-1][2])
        self.assertTrue(session.last_code_patch_diagnostic["protectionRestored"])

    def test_code_patch_calls_protect_even_when_page_is_already_rwx(self):
        backend = FakeMemoryBackend(
            BASE, b"\x74" + b"\0" * 0x20,
            protection=PAGE_EXECUTE_READWRITE)
        session = MemorySession(BASE, BASE, 0x21, backend=backend,
                                owns_handle=False, reference_image_base=BASE,
                                direct_write=False)
        session.write_code_bytes(BASE, b"\xEB", expected=b"\x74")
        self.assertGreaterEqual(len(backend.protect_calls), 2)
        self.assertTrue(all(call[2] == PAGE_EXECUTE_READWRITE
                            for call in backend.protect_calls))

    def test_code_patch_rolls_back_partial_write_before_protection_restore(self):
        backend = FakeMemoryBackend(
            BASE, b"\x74\x02" + b"\0" * 0x20,
            protection=PAGE_EXECUTE_READ)
        session = MemorySession(BASE, BASE, 0x22, backend=backend,
                                owns_handle=False, reference_image_base=BASE,
                                direct_write=False)
        backend.partial_write_at = BASE
        with self.assertRaises(PatchError):
            session.write_code_bytes(
                BASE, b"\x90\x90", expected=b"\x74\x02",
                feature_key="test", label="测试代码补丁")
        self.assertEqual(b"\x74\x02", backend.read(BASE, 2))
        self.assertEqual(PAGE_EXECUTE_READ, backend.region.protection)

    def test_code_patch_protect_failure_does_not_write(self):
        backend = FakeMemoryBackend(
            BASE, b"\x74" + b"\0" * 0x20,
            protection=PAGE_EXECUTE_READ)
        session = MemorySession(BASE, BASE, 0x21, backend=backend,
                                owns_handle=False, reference_image_base=BASE,
                                direct_write=False)
        backend.fail_protect_value = PAGE_EXECUTE_READWRITE
        with self.assertRaisesRegex(PatchError, "已定位"):
            session.write_code_bytes(BASE, b"\xEB", expected=b"\x74")
        self.assertEqual(b"\x74", backend.read(BASE, 1))

    def test_code_patch_flush_failure_rolls_back(self):
        backend = FakeMemoryBackend(
            BASE, b"\x74" + b"\0" * 0x20,
            protection=PAGE_EXECUTE_READ)
        session = MemorySession(BASE, BASE, 0x21, backend=backend,
                                owns_handle=False, reference_image_base=BASE,
                                direct_write=False)
        backend.fail_flush_at = BASE
        with self.assertRaises(PatchError):
            session.write_code_bytes(BASE, b"\xEB", expected=b"\x74")
        self.assertEqual(b"\x74", backend.read(BASE, 1))
        self.assertEqual(PAGE_EXECUTE_READ, backend.region.protection)

    def test_code_patch_restore_failure_rolls_back_and_poisons_session(self):
        backend = FakeMemoryBackend(
            BASE, b"\x74" + b"\0" * 0x20,
            protection=PAGE_EXECUTE_WRITECOPY)
        session = MemorySession(BASE, BASE, 0x21, backend=backend,
                                owns_handle=False, reference_image_base=BASE,
                                direct_write=False)
        backend.fail_protect_value = PAGE_EXECUTE_WRITECOPY
        with self.assertRaisesRegex(PatchError, "代码页保护恢复失败"):
            session.write_code_bytes(BASE, b"\xEB", expected=b"\x74")
        self.assertEqual(b"\x74", backend.read(BASE, 1))
        self.assertTrue(session.code_patch_poisoned)
        with self.assertRaisesRegex(PatchError, "重新启动游戏"):
            session.write_code_bytes(BASE, b"\xEB", expected=b"\x74")

    def test_code_patch_crosses_two_pages_and_restores_each(self):
        class PagedBackend(FakeMemoryBackend):
            def __init__(self):
                super().__init__(BASE, b"\0" * 0x2000,
                                 protection=PAGE_EXECUTE_READ)
                self.protections = {
                    BASE: PAGE_EXECUTE_READ,
                    BASE + 0x1000: PAGE_EXECUTE_WRITECOPY,
                }

            def query_page(self, address):
                page = address & ~0xFFF
                return MemoryPageInfo(
                    page, BASE, PAGE_EXECUTE_READ, 0x1000, MEM_COMMIT,
                    self.protections[page], MEM_IMAGE)

            def protect(self, address, size, protection):
                page = address & ~0xFFF
                self.protect_calls.append((address, size, protection))
                old = self.protections[page]
                self.protections[page] = protection
                return old

        backend = PagedBackend()
        backend.region.data[0xFFF:0x1001] = b"\x74\x02"
        session = MemorySession(BASE, BASE, 0x2000, backend=backend,
                                owns_handle=False, reference_image_base=BASE,
                                direct_write=False)
        session.write_code_bytes(
            BASE + 0xFFF, b"\x90\x90", expected=b"\x74\x02")
        self.assertEqual(b"\x90\x90", backend.read(BASE + 0xFFF, 2))
        self.assertEqual(PAGE_EXECUTE_READ, backend.protections[BASE])
        self.assertEqual(PAGE_EXECUTE_WRITECOPY,
                         backend.protections[BASE + 0x1000])

    def test_width_specific_reads_are_zero_extended(self):
        session, _ = make_session(b"\xFE\xDC\xBA\x98" + b"\0" * 0x20)
        self.assertEqual(0xFE, session.read_u8(BASE))
        self.assertEqual(0xDCFE, session.read_u16(BASE))
        self.assertEqual(0x98BADCFE, session.read_u32(BASE))
        legacy = ctypes.c_uint32(0xFFFFFFFF)
        self.assertEqual(1, session.ReadProcessMemory(
            session.handle, BASE, ctypes.byref(legacy), 1, None))
        self.assertEqual(0xFE, legacy.value)

    def test_write_checks_width_range_and_reads_back(self):
        session, backend = make_session()
        session.write_u8(BASE, 255)
        session.write_u16(BASE + 1, 65535)
        session.write_u32(BASE + 3, 0xFFFFFFFF)
        self.assertEqual(b"\xFF\xFF\xFF\xFF\xFF\xFF\xFF",
                         backend.read(BASE, 7))
        with self.assertRaises(ValueError):
            session.write_u8(BASE, 256)

    def test_out_of_range_access_is_rejected(self):
        session, _ = make_session()
        with self.assertRaises(MemoryAccessError):
            session.read_u32(BASE + 0xFF)
        with self.assertRaises(MemoryAccessError):
            session.write_u16(BASE - 1, 1)

    def test_legacy_read_failure_reports_address_to_ui_callback(self):
        session, _ = make_session()
        messages = []
        session.error_handler = messages.append
        value = ctypes.c_uint32(0xFFFFFFFF)
        self.assertEqual(0, session.ReadProcessMemory(
            session.handle, BASE + 0x100, ctypes.byref(value), 4, None))
        self.assertEqual(0, value.value)
        self.assertEqual(1, len(messages))
        self.assertIn("0x00100100+0x4", messages[0])

    def test_legacy_write_failure_reports_address_to_ui_callback(self):
        session, _ = make_session()
        messages = []
        session.error_handler = messages.append
        value = ctypes.c_uint32(1)
        self.assertEqual(0, session.WriteProcessMemory(
            session.handle, BASE + 0x100, ctypes.byref(value), 4, None))
        self.assertEqual(1, len(messages))
        self.assertIn("0x00100100+0x4", messages[0])

    def test_reference_va_is_relocated_to_actual_module_base(self):
        actual_base = 0x700000
        backend = FakeMemoryBackend(actual_base, b"\0" * 16 + b"\x34\x12" + b"\0" * 16)
        session = MemorySession(actual_base, actual_base, 34, backend=backend,
                                owns_handle=False,
                                reference_image_base=0x400000)
        self.assertEqual(0x1234, session.read_u16(0x400010))
        session.write_u16(0x400010, 0xABCD)
        self.assertEqual(b"\xCD\xAB", backend.read(actual_base + 16, 2))

    def test_patch_failure_rolls_back_earlier_sites(self):
        session, backend = make_session(b"\x74" + b"\0" * 15 + b"\x74" + b"\0" * 32)
        spec = PatchSpec("test", "测试补丁", (
            PatchSite(BASE, b"\x74", b"\xEB"),
            PatchSite(BASE + 16, b"\x74", b"\xEB"),
        ))
        backend.fail_write_at = BASE + 16
        with self.assertRaises(MemoryAccessError):
            session.set_patch(spec, True)
        self.assertEqual(b"\x74", backend.read(BASE, 1))
        self.assertEqual(b"\x74", backend.read(BASE + 16, 1))

    def test_patch_repeated_toggle_and_external_change(self):
        session, backend = make_session(b"\x74" + b"\0" * 31)
        spec = PatchSpec("test", "测试补丁", (
            PatchSite(BASE, b"\x74", b"\xEB"),
        ))
        for _ in range(3):
            session.set_patch(spec, True)
            session.set_patch(spec, True)
            self.assertEqual(b"\xEB", backend.read(BASE, 1))
            session.set_patch(spec, False)
            session.set_patch(spec, False)
            self.assertEqual(b"\x74", backend.read(BASE, 1))
        session.set_patch(spec, True)
        backend.write(BASE, b"\x90")
        with self.assertRaises(PatchError):
            session.set_patch(spec, False)
        self.assertEqual(b"\x90", backend.read(BASE, 1))

    def test_session_snapshot_patch_is_restored_on_close(self):
        session, backend = make_session(b"\x03\x4D\xF8" + b"\0" * 29)
        spec = PatchSpec("forced", "强制快照", (
            PatchSite(BASE, backend.read(BASE, 1), b"\x90"),
        ))
        session.set_patch(spec, True)
        self.assertEqual(b"\x90", backend.read(BASE, 1))
        session.close()
        self.assertEqual(b"\x03", backend.read(BASE, 1))

    def test_prepatched_external_site_is_not_claimed(self):
        session, _ = make_session(b"\xEB" + b"\0" * 31)
        spec = PatchSpec("test", "测试补丁", (
            PatchSite(BASE, b"\x74", b"\xEB"),
        ))
        session.set_patch(spec, True)
        with self.assertRaises(PatchError):
            session.set_patch(spec, False)

    def test_partially_prepatched_transaction_is_rejected_without_writes(self):
        session, backend = make_session(b"\xEB" + b"\0" * 15 + b"\x74" + b"\0" * 32)
        spec = PatchSpec("test", "测试补丁", (
            PatchSite(BASE, b"\x74", b"\xEB"),
            PatchSite(BASE + 16, b"\x74", b"\xEB"),
        ))
        with self.assertRaises(PatchError):
            session.set_patch(spec, True)
        self.assertEqual(b"\xEB", backend.read(BASE, 1))
        self.assertEqual(b"\x74", backend.read(BASE + 16, 1))

    def test_data_transaction_rolls_back_partial_failure(self):
        session, backend = make_session(b"\x01" + b"\0" * 15 + b"\x02" + b"\0" * 32)
        backend.fail_write_at = BASE + 16
        with self.assertRaises(MemoryAccessError):
            session.write_transaction(((BASE, b"\x11"), (BASE + 16, b"\x22")))
        self.assertEqual(b"\x01", backend.read(BASE, 1))
        self.assertEqual(b"\x02", backend.read(BASE + 16, 1))

    def test_data_transaction_rejects_overlapping_ranges(self):
        session, _ = make_session()
        with self.assertRaises(MemoryAccessError):
            session.write_transaction(((BASE, b"\x01\x02"),
                                       (BASE + 1, b"\x03")))

    def test_direct_data_transaction_keeps_earlier_write_on_failure(self):
        session, backend = make_session(
            b"\x01" + b"\0" * 15 + b"\x02" + b"\0" * 32,
            direct_write=True)
        backend.fail_write_at = BASE + 16
        with self.assertRaises(MemoryAccessError):
            session.write_transaction(((BASE, b"\x11"),
                                       (BASE + 16, b"\x22")))
        self.assertEqual(b"\x11", backend.read(BASE, 1))
        self.assertEqual(b"\x02", backend.read(BASE + 16, 1))

    def test_direct_compare_exchange_ignores_stale_expected_bytes(self):
        session, backend = make_session(b"\x90" + b"\0" * 31,
                                        direct_write=True)
        session.compare_exchange_transaction((
            CompareWriteEntry(BASE, b"\x74", b"\xEB"),
        ))
        self.assertEqual(b"\xEB", backend.read(BASE, 1))

    def test_direct_patch_overwrites_external_bytes_and_restores_declared_original(self):
        session, backend = make_session(b"\x90" + b"\0" * 31,
                                        direct_write=True)
        spec = PatchSpec("test", "直接补丁", (
            PatchSite(BASE, b"\x74", b"\xEB"),
        ))
        session.set_patch(spec, True)
        self.assertEqual(b"\xEB", backend.read(BASE, 1))
        backend.write(BASE, b"\xCC")
        session.set_patch(spec, False)
        self.assertEqual(b"\x74", backend.read(BASE, 1))

    def test_dynamic_stub_enable_restore_and_release(self):
        data = bytearray(b"\0" * 0x100)
        data[0x10:0x15] = b"\xE8\x01\x02\x03\x04"
        session, backend = make_session(bytes(data))
        spec = DynamicPatchSpec(
            "dynamic", "动态补丁",
            (DynamicCallSite(BASE + 0x10, b"\xE8\x01\x02\x03\x04",
                             5, 0xE9),),
            b"\xB8\0\0\0\0\xC3",
            (StubRelocation(1, "absolute_reference", BASE + 0x20),))
        session.set_dynamic_patch(spec, True)
        lease = session._dynamic_leases["dynamic"]
        branch = backend.read(BASE + 0x10, 5)
        self.assertEqual(0xE9, branch[0])
        target = BASE + 0x15 + int.from_bytes(branch[1:], "little", signed=True)
        self.assertEqual(lease.allocation, target)
        self.assertTrue(backend.flushed)
        session.set_dynamic_patch(spec, False)
        self.assertEqual(b"\xE8\x01\x02\x03\x04",
                         backend.read(BASE + 0x10, 5))
        self.assertIn(lease.allocation, backend.freed)

    def test_dynamic_patch_enabled_query_tracks_session_ownership(self):
        data = bytearray(b"\0" * 0x100)
        data[0x10:0x15] = b"\xE8\x01\x02\x03\x04"
        session, backend = make_session(bytes(data))
        spec = DynamicPatchSpec(
            "dynamic", "动态补丁",
            (DynamicCallSite(BASE + 0x10, b"\xE8\x01\x02\x03\x04",
                             5, 0xE9),), b"\xC3")
        self.assertFalse(session.is_dynamic_patch_enabled(spec.key))
        session.set_dynamic_patch(spec, True)
        self.assertTrue(session.is_dynamic_patch_enabled(spec.key))
        backend.write(BASE + 0x10, b"\x90" * 5)
        self.assertFalse(session.is_dynamic_patch_enabled(spec.key))
        lease = session._dynamic_leases[spec.key]
        backend.write(BASE + 0x10, lease.branch_bytes[0])
        session.set_dynamic_patch(spec, False)
        self.assertFalse(session.is_dynamic_patch_enabled(spec.key))

    def test_dynamic_stub_refuses_external_branch_change_without_free(self):
        data = bytearray(b"\0" * 0x100)
        data[0x10:0x15] = b"\xE8\x01\x02\x03\x04"
        session, backend = make_session(bytes(data))
        spec = DynamicPatchSpec(
            "dynamic", "动态补丁",
            (DynamicCallSite(BASE + 0x10, b"\xE8\x01\x02\x03\x04",
                             5, 0xE9),), b"\xC3")
        session.set_dynamic_patch(spec, True)
        lease = session._dynamic_leases["dynamic"]
        backend.write(BASE + 0x10, b"\x90" * 5)
        with self.assertRaises(PatchError):
            session.set_dynamic_patch(spec, False)
        self.assertNotIn(lease.allocation, backend.freed)

    def test_dynamic_stub_refuses_release_while_thread_eip_is_inside(self):
        data = bytearray(b"\0" * 0x100)
        data[0x10:0x15] = b"\xE8\x01\x02\x03\x04"
        session, backend = make_session(bytes(data))
        spec = DynamicPatchSpec(
            "dynamic", "动态补丁",
            (DynamicCallSite(BASE + 0x10, b"\xE8\x01\x02\x03\x04",
                             5, 0xE9),), b"\x90\xC3")
        session.set_dynamic_patch(spec, True)
        lease = session._dynamic_leases["dynamic"]
        backend.active_eips = [lease.allocation + 1]
        with self.assertRaises(PatchError):
            session.set_dynamic_patch(spec, False)
        self.assertNotIn(lease.allocation, backend.freed)

    def test_direct_dynamic_stub_restores_without_ownership_or_eip_checks(self):
        data = bytearray(b"\0" * 0x100)
        original = b"\xE8\x01\x02\x03\x04"
        data[0x10:0x15] = original
        session, backend = make_session(bytes(data), direct_write=True)
        spec = DynamicPatchSpec(
            "dynamic-direct", "动态直写补丁",
            (DynamicCallSite(BASE + 0x10, original, 5, 0xE9),),
            b"\x90\xC3")
        session.set_dynamic_patch(spec, True)
        lease = session._dynamic_leases[spec.key]
        backend.write(BASE + 0x10, b"\xCC" * 5)
        backend.active_eips = [lease.allocation + 1]
        session.set_dynamic_patch(spec, False)
        self.assertEqual(original, backend.read(BASE + 0x10, 5))
        self.assertIn(lease.allocation, backend.freed)

    def test_dynamic_call_operand_only_owns_four_rel32_bytes(self):
        data = bytearray(b"\0" * 0x100)
        original = b"\xE8\x01\x02\x03\x04"
        data[0x10:0x15] = original
        session, backend = make_session(bytes(data))
        writes = []
        backend_write = backend.write

        def recording_write(address, value):
            writes.append((address, bytes(value)))
            return backend_write(address, value)

        backend.write = recording_write
        spec = DynamicPatchSpec(
            "operand", "rel32操作数补丁",
            (DynamicCallSite(BASE + 0x10, original, 5, 0xE8,
                             operand_only=True),), b"\xC3")
        session.set_dynamic_patch(spec, True)
        self.assertEqual(0xE8, backend.read(BASE + 0x10, 1)[0])
        self.assertIn(BASE + 0x11, [address for address, _value in writes])
        self.assertNotIn(BASE + 0x10, [address for address, value in writes
                                      if len(value) == 5])
        lease = session._dynamic_leases["operand"]
        self.assertEqual(lease.branch_bytes[0][1:],
                         backend.read(BASE + 0x11, 4))
        session.set_dynamic_patch(spec, False)
        self.assertEqual(original, backend.read(BASE + 0x10, 5))
        self.assertIn(lease.allocation, backend.freed)

    def test_operand_only_patch_refuses_external_opcode_change(self):
        data = bytearray(b"\0" * 0x100)
        original = b"\xE8\x01\x02\x03\x04"
        data[0x10:0x15] = original
        session, backend = make_session(bytes(data))
        spec = DynamicPatchSpec(
            "operand", "rel32操作数补丁",
            (DynamicCallSite(BASE + 0x10, original, 5, 0xE8,
                             operand_only=True),), b"\xC3")
        session.set_dynamic_patch(spec, True)
        lease = session._dynamic_leases["operand"]
        backend.write(BASE + 0x10, b"\x90")
        with self.assertRaises(PatchError):
            session.set_dynamic_patch(spec, False)
        self.assertNotIn(lease.allocation, backend.freed)

    def test_remote_call_releases_both_allocations_and_thread(self):
        session, backend = make_session(b"\0" * 0x100)
        helper = my_hook(session=session)
        helper.recal(session, BASE + 0x20)
        self.assertEqual(2, len(backend.freed))
        self.assertEqual([1], backend.waited_threads)
        self.assertEqual([1], backend.closed_threads)
        self.assertEqual({}, backend.allocations)

    def test_remote_call_failure_still_releases_both_allocations(self):
        session, backend = make_session(b"\0" * 0x100)
        def fail_thread(_start, _parameter):
            raise MemoryAccessError("模拟远程线程失败")
        backend.create_thread = fail_thread
        with self.assertRaises(HookError):
            my_hook(session=session).recal(session, BASE + 0x20)
        self.assertEqual(2, len(backend.freed))
        self.assertEqual({}, backend.allocations)

    def test_remote_timeout_poison_keeps_allocations_and_blocks_next_call(self):
        session, backend = make_session(b"\0" * 0x100)

        def timeout(_thread, _timeout_ms):
            raise MemoryAccessError("模拟远程线程执行超时")

        backend.wait_thread = timeout
        with self.assertRaises(HookError):
            my_hook(session=session).recal(session, BASE + 0x20)
        self.assertTrue(session.remote_call_poisoned)
        self.assertEqual(2, len(session._orphaned_allocations))
        self.assertEqual(0, len(backend.freed))
        with self.assertRaises(HookError):
            my_hook(session=session).recal(session, BASE + 0x20)

    def test_remote_call_allows_unverified_abi_but_keeps_address_guard(self):
        unavailable = FeatureCapability(
            "turn_refresh_direction", CapabilityState.UNAVAILABLE,
            "ABI未验证")
        profile = SimpleNamespace(
            is_66=True,
            force_unverified_66=True,
            capability=lambda _key: unavailable,
            can_use=lambda _key: True,
            remote_calls={
                "turn_refresh_direction": RemoteCallSpec(
                    "turn_refresh_direction", "转向刷新", 0x004575B8,
                    b"\x55\x8B\xEC", True,
                    calling_convention="stdcall",
                    arguments=("data_id:u32", "sentinel:u32", "direction:u32",
                               "reserved:u32", "refresh:u32", "redraw:u32"),
                    callee_cleanup=24,
                    wrapper_kind="turn-66-six-args-v1"),
                "recalculate": RemoteCallSpec(
                    "recalculate", "能力重算", 0x004075DF, b"", False),
            })
        helper = my_hook(profile=profile)
        self.assertEqual(0x004575B8, helper._call_address(
            "turn_refresh_direction", 0x00457428))
        self.assertEqual(0x004075DF,
                         helper._call_address("recalculate", 0x00400000))

        session, _backend = make_session()
        captured = {}
        helper.session = session
        helper._run = lambda current, shellcode, data: captured.update(
            session=current, shellcode=shellcode, data=data)
        helper.changeDir(session, 3, 42)
        self.assertEqual((3, 42, 0x004575B8),
                         struct.unpack("<III", captured["data"]))
        self.assertIn(b"\x6A\x01\x6A\x01\x6A\x00", captured["shellcode"])
        self.assertIn(b"\x68\xFF\xFF\x00\x00", captured["shellcode"])
        self.assertIn(b"\xFF\x70\x04\xFF\x50\x08", captured["shellcode"])

        profile.remote_calls["turn_refresh_direction"] = RemoteCallSpec(
            "turn_refresh_direction", "转向刷新", 0, b"", False)
        with self.assertRaises(HookError):
            helper.changeDir(session, 3, 42)

    def test_revive_wrapper_rejects_invalid_arguments_before_remote_thread(self):
        session, backend = make_session()
        helper = my_hook(session=session)
        for args in ((4, 1, 1, 1), (0, 256, 1, 1),
                     (0, 1, 256, 1), (0, 1, 1, 0xFFFF)):
            with self.subTest(args=args):
                with self.assertRaises(HookError):
                    helper.life(session, *args)
        self.assertEqual({}, backend.allocations)

    def test_close_is_idempotent_and_closes_owned_backend(self):
        session, backend = make_session()
        session.owns_handle = True
        session.close()
        session.close()
        self.assertTrue(backend.closed)


if __name__ == "__main__":
    unittest.main()
