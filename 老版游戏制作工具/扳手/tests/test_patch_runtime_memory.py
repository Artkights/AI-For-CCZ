"""Ownership and read-only observation of stable code-patch recipes."""

import unittest
from unittest.mock import patch

from engine_profile import DynamicCallSite, DynamicPatchSpec, PatchSite, PatchSpec
from process_memory import FakeMemoryBackend, MemoryAccessError, MemorySession, PatchError


BASE = 0x400000


class PatchRuntimeMemoryTests(unittest.TestCase):
    def setUp(self):
        self.backend = FakeMemoryBackend(BASE, bytes(0x1000))
        self.session = MemorySession(1, BASE, 0x1000, backend=self.backend,
                                     owns_handle=False, direct_write=True)
        self.spec = PatchSpec("control_auto_return", "自动回归", tuple(
            PatchSite(BASE + 0x100 + index * 0x10, original, b"\x90\x90")
            for index, original in enumerate((b"\x74\x13", b"\x74\x02", b"\x74\x02"))))
        self.restore_original_sites()

    def restore_original_sites(self):
        for site in self.spec.sites:
            self.backend.write(site.address, site.original)

    def site_bytes(self):
        return tuple(self.backend.read(site.address, len(site.original))
                     for site in self.spec.sites)

    def test_original_owned_and_restore_keep_all_three_sites_reversible(self):
        self.assertEqual("ORIGINAL", self.session.probe_patch(self.spec)["state"])
        for _cycle in range(3):
            self.session.set_patch(self.spec, True)
            for _probe in range(10):
                self.assertEqual("OWNED_PATCHED", self.session.probe_patch(self.spec)["state"])
            self.session.set_patch(self.spec, False)
            self.assertEqual(tuple(site.original for site in self.spec.sites), self.site_bytes())
            self.assertEqual("ORIGINAL", self.session.probe_patch(self.spec)["state"])

    def test_external_fully_patched_sites_are_neither_claimed_nor_restored(self):
        for site in self.spec.sites:
            self.backend.write(site.address, site.patched)
        before = self.site_bytes()
        self.assertEqual("EXTERNAL_PATCHED", self.session.probe_patch(self.spec)["state"])
        for enabled in (True, False):
            with self.assertRaises(PatchError):
                self.session.set_patch(self.spec, enabled)
        self.assertEqual(before, self.site_bytes())
        self.assertNotIn(self.spec.key, self.session._active_patches)

    def test_partial_sites_are_rejected_and_diagnostics_name_addresses(self):
        self.backend.write(self.spec.sites[0].address, self.spec.sites[0].patched)
        before = self.site_bytes()
        observation = self.session.probe_patch(self.spec)
        self.assertEqual("PARTIAL", observation["state"])
        for site in self.spec.sites:
            self.assertIn("0x%08X" % site.address, observation["reason"])
        with self.assertRaises(PatchError):
            self.session.set_patch(self.spec, True)
        self.assertEqual(before, self.site_bytes())

    def test_foreign_bytes_block_restore_without_discarding_ownership(self):
        self.session.set_patch(self.spec, True)
        site = self.spec.sites[1]
        self.backend.write(site.address, b"\xCC\xCC")
        before = self.site_bytes()
        observation = self.session.probe_patch(self.spec)
        self.assertEqual("EXTERNAL_CHANGED", observation["state"])
        self.assertIn("0x%08X" % site.address, observation["reason"])
        with self.assertRaises(PatchError):
            self.session.set_patch(self.spec, False)
        self.assertEqual(before, self.site_bytes())
        self.assertIn(self.spec.key, self.session._active_patches)

    def test_unreadable_probe_preserves_current_ownership(self):
        self.session.set_patch(self.spec, True)
        with patch.object(self.session, "read_bytes", side_effect=MemoryAccessError("temporary")):
            self.assertEqual("UNREADABLE", self.session.probe_patch(self.spec)["state"])
        self.assertIn(self.spec.key, self.session._active_patches)
        self.assertEqual("OWNED_PATCHED", self.session.probe_patch(self.spec)["state"])

    def test_confirmed_external_restore_releases_ownership(self):
        self.session.set_patch(self.spec, True)
        self.restore_original_sites()
        self.assertEqual("ORIGINAL", self.session.probe_patch(self.spec)["state"])
        self.assertNotIn(self.spec.key, self.session._active_patches)
        self.session.set_patch(self.spec, True)
        self.assertEqual("OWNED_PATCHED", self.session.probe_patch(self.spec)["state"])

    def test_short_write_remains_failed_even_after_verified_full_rollback(self):
        before = self.site_bytes()
        native_write = self.backend.write
        failed = [False]

        def short_once(address, raw):
            if address == self.spec.sites[0].address and not failed[0]:
                failed[0] = True
                native_write(address, raw[:1])
                return 1
            return native_write(address, raw)

        with patch.object(self.backend, "write", side_effect=short_once):
            with self.assertRaises(PatchError):
                self.session.set_patch(self.spec, True)
        self.assertEqual(before, self.site_bytes())
        self.assertEqual("WRITE_FAILED", self.session.probe_patch(self.spec)["state"])
        self.assertNotIn(self.spec.key, self.session._active_patches)
        with self.assertRaises(PatchError):
            self.session.set_patch(self.spec, True)
        self.assertEqual(before, self.site_bytes())

    def make_dynamic(self):
        original = b"\xE8\x01\x02\x03\x04"
        spec = DynamicPatchSpec("control_auto_return", "动态自动回归", tuple(
            DynamicCallSite(BASE + 0x200 + index * 0x10, original, 5, 0xE8)
            for index in range(2)), b"\x55\x8B\xEC\x5D\xC3")
        for site in spec.call_sites:
            self.backend.write(site.address, site.original)
        return spec

    def test_dynamic_owned_stub_survives_independent_readonly_session_probe(self):
        spec = self.make_dynamic()
        self.assertEqual("ORIGINAL", self.session.probe_dynamic_patch(spec)["state"])
        self.session.set_dynamic_patch(spec, True)
        observer = MemorySession(2, BASE, 0x1000, backend=self.backend,
                                  owns_handle=False, read_only=True)
        self.assertEqual("EXTERNAL_CHANGED", observer.probe_dynamic_patch(spec)["state"])
        self.assertEqual("OWNED_PATCHED", self.session.probe_dynamic_patch(spec)["state"])
        self.session.set_dynamic_patch(spec, False)
        self.assertEqual("ORIGINAL", self.session.probe_dynamic_patch(spec)["state"])
        self.assertEqual({}, self.backend.allocations)

    def test_dynamic_foreign_call_site_is_preserved_on_enable_and_disable(self):
        spec = self.make_dynamic()
        self.session.set_dynamic_patch(spec, True)
        allocation = self.session._dynamic_leases[spec.key].allocation
        self.backend.write(spec.call_sites[1].address, b"\xCC" * 5)
        before = bytes(self.backend.region.data)
        self.assertEqual("EXTERNAL_CHANGED", self.session.probe_dynamic_patch(spec)["state"])
        for enabled in (True, False):
            with self.assertRaises(PatchError):
                self.session.set_dynamic_patch(spec, enabled)
        self.assertEqual(before, bytes(self.backend.region.data))
        self.assertNotIn(allocation, self.backend.freed)
        self.assertIn(spec.key, self.session._dynamic_leases)

    def test_dynamic_corrupted_stub_is_detected_and_not_freed(self):
        spec = self.make_dynamic()
        self.session.set_dynamic_patch(spec, True)
        lease = self.session._dynamic_leases[spec.key]
        self.backend.write(lease.allocation, b"\xCC")
        before = bytes(self.backend.region.data)
        self.assertEqual("EXTERNAL_CHANGED", self.session.probe_dynamic_patch(spec)["state"])
        self.assertFalse(self.session.is_dynamic_patch_enabled(spec.key))
        with self.assertRaises(PatchError):
            self.session.set_dynamic_patch(spec, False)
        self.assertEqual(before, bytes(self.backend.region.data))
        self.assertNotIn(lease.allocation, self.backend.freed)

    def test_dynamic_released_stub_page_is_an_immediate_external_change(self):
        spec = self.make_dynamic()
        self.session.set_dynamic_patch(spec, True)
        lease = self.session._dynamic_leases[spec.key]
        native_query = self.backend.query

        def absent_stub(address):
            if lease.allocation <= address < lease.allocation + lease.allocation_size:
                raise MemoryAccessError("stub page was released")
            return native_query(address)

        before = tuple(self.backend.read(site.address, len(site.original))
                       for site in spec.call_sites)
        with patch.object(self.backend, "query", side_effect=absent_stub):
            observation = self.session.probe_dynamic_patch(spec)
            self.assertEqual("EXTERNAL_CHANGED", observation["state"])
            self.assertIn("0x%08X" % lease.allocation, observation["reason"])
            with self.assertRaises(PatchError):
                self.session.set_dynamic_patch(spec, False)
        self.assertEqual(before, tuple(self.backend.read(site.address, len(site.original))
                                       for site in spec.call_sites))
        self.assertNotIn(lease.allocation, self.backend.freed)


if __name__ == "__main__":
    unittest.main()
