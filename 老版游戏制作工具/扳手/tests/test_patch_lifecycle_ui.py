"""Regression coverage for control-page capability and patch lifetimes."""

import os
import struct
import unittest
import copy
from unittest.mock import Mock, patch

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from PyQt5 import QtCore, QtGui
from PyQt5.QtWidgets import QApplication

import mywindow
from engine_layout import FeatureAccess
from engine_profile import (DynamicCallSite, DynamicPatchSpec, PatchSite,
                            PatchSpec, legacy_profile)
from hook import HookError
from main import MyDesiger
from process_memory import FakeMemoryBackend, MemorySession


class PatchLifecycleUiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])

    def setUp(self):
        self.window = MyDesiger()
        self.backend = FakeMemoryBackend(0x400000, bytes(0x200000))
        self.session = MemorySession(1, 0x400000, 0x200000,
                                     backend=self.backend, owns_handle=False)
        self.profile = legacy_profile(0)
        self.profile.access_policy = "strict"
        self.profile.direct_write = False
        self.window.engine_profile = self.profile
        self.window.memory_session = self.session
        self.window.md = self.session
        self.window.p = self.session.handle
        self.window.process_id = 123
        self.window.ok = True
        self.window.widget_1.setEnabled(True)
        self.previous_globals = {
            key: getattr(mywindow, key) for key in
            ("version", "addr_war", "len_war", "cnt_wo", "cnt_you",
             "cnt_di", "auto_life", "lock_list", "lock_hp", "lock_mp",
             "lock_ids")
        }
        mywindow.version = 0
        mywindow.auto_life = False
        mywindow.lock_list, mywindow.lock_hp = [], []
        mywindow.lock_mp, mywindow.lock_ids = [], []

    def tearDown(self):
        self.window.close()
        for key, value in self.previous_globals.items():
            setattr(mywindow, key, value)

    def make_patch(self):
        key = "control_friendly"
        recipe = PatchSpec(key, "友军控制", (
            PatchSite(0x401000, b"\x74", b"\xEB"),
            PatchSite(0x401010, b"\x75", b"\x90"),
        ))
        self.profile.patches[key] = recipe
        self.profile.set_feature_access(key, FeatureAccess.READ_WRITE,
                                        runtime_probe_passed=True)
        for site in recipe.sites:
            self.backend.write(site.address, site.original)
        self.window._apply_capabilities()
        return recipe

    def click_friendly(self, checked):
        self.window.checkBox_1.setChecked(checked)
        self.window.onButtonClick(1)

    def test_owned_normal_patch_can_toggle_repeatedly_without_losing_capability(self):
        recipe = self.make_patch()
        for _iteration in range(10):
            self.click_friendly(True)
            self.window._sync_66_patch_controls()
            self.assertTrue(self.window.checkBox_1.isEnabled())
            self.assertTrue(self.window.checkBox_1.isChecked())
            for site in recipe.sites:
                self.assertEqual(site.patched,
                                 self.backend.read(site.address, len(site.patched)))
            self.click_friendly(False)
            self.window._sync_66_patch_controls()
            self.assertTrue(self.window.checkBox_1.isEnabled())
            self.assertFalse(self.window.checkBox_1.isChecked())
            for site in recipe.sites:
                self.assertEqual(site.original,
                                 self.backend.read(site.address, len(site.original)))

    def test_external_or_partial_patch_is_disabled_on_the_first_probe(self):
        recipe = self.make_patch()
        for policy in ("strict", "direct_compat"):
            self.profile.access_policy = policy
            for state in ("external", "partial"):
                with self.subTest(state=state, policy=policy):
                    for index, site in enumerate(recipe.sites):
                        raw = (site.patched if state == "external" or index == 0
                               else site.original)
                        self.backend.write(site.address, raw)
                    before = tuple(self.backend.read(site.address, len(site.original))
                                   for site in recipe.sites)
                    self.window._probe_patch_controls()
                    self.assertFalse(self.window.checkBox_1.isEnabled())
                    self.assertFalse(self.profile.can_write(recipe.key))
                    self.assertNotIn(recipe.key, self.window._profile_probe_failures)
                    self.assertEqual(before, tuple(
                        self.backend.read(site.address, len(site.original))
                        for site in recipe.sites))

    def test_two_transient_read_failures_preserve_owned_check_then_third_disables(self):
        recipe = self.make_patch()
        self.click_friendly(True)
        self.window._probe_patch_controls()
        self.assertTrue(self.window.checkBox_1.isChecked())
        with patch.object(self.session, "probe_patch",
                          return_value={"state": "UNREADABLE",
                                        "reason": "temporary read failure"}):
            for attempt in range(1, 4):
                self.window._probe_patch_controls()
                self.assertEqual(attempt,
                                 self.window._profile_probe_failures[recipe.key])
                self.assertEqual(attempt < 3,
                                 self.window.checkBox_1.isEnabled())
                if attempt < 3:
                    self.assertTrue(self.window.checkBox_1.isChecked())
            self.assertEqual("UNREADABLE_CONFIRMED",
                             self.profile.patch_runtime_states[recipe.key])
            self.assertEqual("ERROR", self.window._patch_control_states[recipe.key])
        self.window._probe_patch_controls()
        self.assertTrue(self.window.checkBox_1.isEnabled())
        self.assertTrue(self.window.checkBox_1.isChecked())
        self.assertNotIn(recipe.key, self.window._profile_probe_failures)

    def test_third_unreadable_preprobe_rejects_uncheck_and_preserves_owned_value(self):
        recipe = self.make_patch()
        self.click_friendly(True)
        before = tuple(self.backend.read(site.address, len(site.patched))
                       for site in recipe.sites)
        with patch.object(self.session, "probe_patch", return_value={
                "state": "UNREADABLE", "reason": "temporary read failure"}), \
                patch.object(self.session, "set_patch", wraps=self.session.set_patch) as write:
            self.window._probe_patch_controls()
            self.window._probe_patch_controls()
            self.assertTrue(self.window.checkBox_1.isEnabled())
            self.click_friendly(False)
            write.assert_not_called()
            self.assertTrue(self.window.checkBox_1.isChecked())
            self.assertFalse(self.window.checkBox_1.isEnabled())
            self.assertEqual("UNREADABLE_CONFIRMED",
                             self.profile.patch_runtime_states[recipe.key])
            self.assertIn(recipe.key, self.session._active_patches)
            self.assertEqual(before, tuple(
                self.backend.read(site.address, len(site.patched))
                for site in recipe.sites))
        self.window._probe_patch_controls()
        self.assertTrue(self.window.checkBox_1.isEnabled())
        self.assertTrue(self.window.checkBox_1.isChecked())
        self.click_friendly(False)
        self.assertFalse(self.window.checkBox_1.isChecked())
        self.assertNotIn(recipe.key, self.session._active_patches)

    def test_same_identity_scan_preserves_profile_object_and_patch_recipe(self):
        recipe = self.make_patch()
        candidate = copy.deepcopy(self.profile)
        candidate.patches.clear()
        candidate.dynamic_patches.clear()
        candidate.set_feature_access(recipe.key, FeatureAccess.UNAVAILABLE,
                                     reason="background read failure")
        candidate.runtime_probe_failures[recipe.key] = "background read failure"
        merged = self.window._merge_scanned_profile(candidate)
        self.assertIs(self.profile, merged)
        self.assertIs(recipe, self.profile.patches[recipe.key])
        self.window._probe_patch_controls()
        self.assertTrue(self.window.checkBox_1.isEnabled())

    def make_dynamic_patch(self):
        key = "control_friendly"
        original = b"\xE8\x01\x02\x03\x04"
        recipe = DynamicPatchSpec(key, "友军控制", (
            DynamicCallSite(0x401000, original, 5, 0xE9),), b"\x90\xC3")
        self.profile.patches.pop(key, None)
        self.profile.dynamic_patches[key] = recipe
        self.profile.set_feature_access(key, FeatureAccess.READ_WRITE,
                                        runtime_probe_passed=True)
        self.backend.write(0x401000, original)
        self.window._probe_patch_controls()
        return recipe

    def test_owned_dynamic_stub_survives_recipe_less_scan_and_can_be_disabled(self):
        recipe = self.make_dynamic_patch()
        self.click_friendly(True)
        self.assertTrue(self.window.checkBox_1.isChecked())
        lease = self.session._dynamic_leases[recipe.key]
        branch = self.backend.read(0x401000, 5)
        candidate = copy.deepcopy(self.profile)
        candidate.patches.clear()
        candidate.dynamic_patches.clear()
        candidate.set_feature_access(recipe.key, FeatureAccess.UNAVAILABLE,
                                     reason="independent scanner has no ownership")
        merged = self.window._merge_scanned_profile(candidate)
        self.assertIs(self.profile, merged)
        self.assertIs(recipe, self.profile.dynamic_patches[recipe.key])
        self.window._probe_patch_controls()
        self.assertTrue(self.window.checkBox_1.isEnabled())
        self.assertTrue(self.window.checkBox_1.isChecked())
        self.assertEqual(branch, self.backend.read(0x401000, 5))
        self.click_friendly(False)
        self.assertFalse(self.window.checkBox_1.isChecked())
        self.assertTrue(self.window.checkBox_1.isEnabled())
        self.assertNotIn(recipe.key, self.session._dynamic_leases)
        self.assertIn(lease.allocation, self.backend.freed)
        self.assertEqual(recipe.call_sites[0].original,
                         self.backend.read(0x401000, 5))

    def test_owned_dynamic_stub_byte_change_immediately_disables_control(self):
        recipe = self.make_dynamic_patch()
        self.click_friendly(True)
        lease = self.session._dynamic_leases[recipe.key]
        self.backend.write(lease.allocation, b"\xCC\xCC")
        self.window._probe_patch_controls()
        self.assertFalse(self.window.checkBox_1.isEnabled())
        self.assertFalse(self.profile.can_write(recipe.key))
        self.assertEqual("EXTERNAL_CHANGED", self.profile.patch_runtime_states[recipe.key])
        self.assertNotIn(recipe.key, self.window._profile_probe_failures)
        self.assertNotIn(lease.allocation, self.backend.freed)

    def test_ten_page_activations_reuse_ready_profile(self):
        self.make_patch()
        self.window._attach_context = (123, 0x400000, 0x200000,
                                       "unused.exe", None, None)
        self.window._page_readiness = {key: True for key in (
            "person", "battle", "warehouse", "effect", "effect_exclusive",
            "fatal", "variables_bool", "variables_int", "variables_ptr")}
        self.window.attach_timer.stop()
        with patch.object(self.window, "_queue_profile_scan") as queue:
            for _iteration in range(10):
                self.window.on_action_T_triggered()
        queue.assert_not_called()
        self.assertIs(self.profile, self.window.engine_profile)
        self.assertTrue(self.window.checkBox_1.isEnabled())
        self.assertFalse(self.window.attach_timer.isActive())

    def test_unready_page_reprobes_data_without_rebuilding_owned_patch_profile(self):
        recipe = self.make_patch()
        self.click_friendly(True)
        self.window._attach_context = (123, 0x400000, 0x200000,
                                       "unused.exe", None, None)
        self.window._page_readiness = {"person": False}
        self.window._last_data_probe_at = 0.0
        self.window.attach_timer.stop()
        with patch.object(mywindow, "hydrate_runtime_item_catalog") as hydrate, \
                patch.object(self.profile, "probe_runtime") as probe, \
                patch.object(self.window, "_apply_attached_profile") as apply, \
                patch.object(self.window, "_queue_profile_scan") as queue, \
                patch.object(mywindow, "detect_composite_profile") as detect:
            self.window.on_action_T_triggered()
        hydrate.assert_called_once_with(self.profile, self.session)
        probe.assert_called_once_with(self.session)
        apply.assert_called_once_with("unused.exe")
        queue.assert_not_called()
        detect.assert_not_called()
        self.assertIs(self.profile, self.window.engine_profile)
        self.assertIs(recipe, self.profile.patches[recipe.key])
        self.assertIn(recipe.key, self.session._active_patches)
        self.assertTrue(self.window.checkBox_1.isChecked())
        self.assertFalse(self.window.attach_timer.isActive())
        for site in recipe.sites:
            self.assertEqual(site.patched,
                             self.backend.read(site.address, len(site.patched)))

    def test_qt_activation_event_dispatches_to_real_ui_refresh_handler(self):
        callbacks = ("onData", "onWar", "onItem", "onPower", "onMk", "onVar", "DIY_Page")
        replacements = {name: Mock() for name in callbacks}
        with patch.object(self.window, "isActiveWindow", return_value=True), \
                patch.object(self.window, "_refresh_profile_on_demand") as refresh, \
                patch.multiple(self.window, **replacements):
            QApplication.sendEvent(self.window,
                                  QtCore.QEvent(QtCore.QEvent.ActivationChange))
        refresh.assert_called_once()
        for callback in replacements.values():
            callback.assert_called_once()

    def test_qt_close_event_dispatches_to_session_cleanup(self):
        event = QtGui.QCloseEvent()
        QApplication.sendEvent(self.window, event)
        self.assertTrue(event.isAccepted())
        self.assertTrue(self.window._closing)
        self.assertTrue(self.session.closed)
        self.assertIsNone(self.window.memory_session)
        self.assertIsNone(self.window.engine_profile)

    def test_old_attachment_result_cannot_replace_profile_or_current_worker(self):
        old_token = self.window._attach_token
        self.window._attach_token += 1
        active = Mock(token=self.window._attach_token)
        self.window._scan_task = active
        self.window._scan_tasks = [active]
        self.window._profile_scan_finished(old_token, {"profile": object()})
        self.assertIs(self.profile, self.window.engine_profile)
        self.assertIs(active, self.window._scan_task)
        self.assertEqual([active], self.window._scan_tasks)

    def test_old_scan_generation_and_process_identity_do_not_replace_current_worker(self):
        self.window._attach_context = (123, 0x400000, 0x200000,
                                       "unused.exe", None, None)
        self.window._profile_scan_generation = 10
        valid = {"scan_generation": 10,
                 "profile_generation": self.window._profile_generation,
                 "session_generation": self.session.session_generation,
                 "pid": 123, "module_base": 0x400000,
                 "profile": object()}
        for field, value in (("scan_generation", 9),
                             ("profile_generation", self.window._profile_generation - 1),
                             ("session_generation", "old-session"),
                             ("pid", 999), ("module_base", 0x500000)):
            with self.subTest(field=field):
                active = Mock(token=self.window._attach_token, scan_generation=10)
                self.window._scan_task = active
                self.window._scan_tasks = [active]
                result = dict(valid)
                result[field] = value
                with patch.object(self.window, "_apply_attached_profile") as apply:
                    self.window._profile_scan_finished(self.window._attach_token, result)
                apply.assert_not_called()
                self.assertIs(self.profile, self.window.engine_profile)
                self.assertIs(active, self.window._scan_task)

    def prepare_dead_unit(self):
        self.profile.set_feature_access("revive", FeatureAccess.READ_WRITE,
                                        runtime_probe_passed=True)
        self.profile.limits.update(battle_ours=1, battle_friends=0,
                                    battle_enemies=0)
        base = self.profile.addresses["battle_units"]
        stride = self.profile.limits["battle_stride"]
        mywindow.addr_war, mywindow.len_war = base, stride
        mywindow.cnt_wo, mywindow.cnt_you, mywindow.cnt_di = 1, 0, 0
        row = bytearray(stride)
        struct.pack_into("<H", row, 0, 7)
        row[6], row[7], row[0x0C], row[0x0F] = 10, 11, 3, 2
        self.backend.write(base, row)
        self.window.war_input_9.setPlainText("10")
        self.window.war_input_10.setPlainText("11")
        self.window.war_input_16.setCurrentIndex(0)
        self.window.war_life.setEnabled(True)
        self.window.checkBox_9.setEnabled(True)
        return base, bytes(row)

    def test_manual_revive_success_and_failure_restore_button(self):
        base, dead = self.prepare_dead_unit()
        style = self.window.war_life.styleSheet()
        for succeeds in (True, False):
            with self.subTest(succeeds=succeeds):
                self.backend.write(base, dead)
                def invoke(*args):
                    self.assertFalse(self.window.war_life.isEnabled())
                    if not succeeds:
                        raise HookError("temporary call failure")
                    self.backend.write(base + 0x10, struct.pack("<I", 100))
                    self.backend.write(base + 0x0C, b"\x00")
                helper = Mock()
                helper.life.side_effect = invoke
                with patch.object(self.window, "_selected_war_slot", return_value=0), \
                        patch.object(self.profile, "revive_entry_address", return_value=0x401234), \
                        patch.object(mywindow, "my_hook", return_value=helper), \
                        patch.object(self.window, "onWar"):
                    self.window.onLife()
                helper.life.assert_called_once()
                self.assertTrue(self.window.war_life.isEnabled())
                self.assertEqual(style, self.window.war_life.styleSheet())

    def test_automatic_revive_failure_pauses_and_allows_user_to_reenable(self):
        self.prepare_dead_unit()
        self.window.checkBox_9.setChecked(True)
        self.window.onButtonClick(9)
        self.assertTrue(mywindow.auto_life)
        helper = Mock()
        helper.life.side_effect = HookError("temporary call failure")
        with patch.object(mywindow.psutil, "pid_exists", return_value=True), \
                patch.object(self.profile, "revive_entry_address", return_value=0x401234), \
                patch.object(mywindow, "my_hook", return_value=helper):
            self.window.LockThread()
        self.assertFalse(mywindow.auto_life)
        self.assertFalse(self.window.checkBox_9.isChecked())
        self.assertTrue(self.window.checkBox_9.isEnabled())
        self.assertIn("可重新勾选", self.window.label_window2.toolTip())
        self.window.checkBox_9.setChecked(True)
        self.window.onButtonClick(9)
        self.assertTrue(mywindow.auto_life)
        self.assertTrue(self.window.checkBox_9.isChecked())
        self.assertEqual({}, self.window._auto_revive_attempts)


class PatchOwnershipSnapshotTests(unittest.TestCase):
    def test_dynamic_ownership_snapshot_is_detached_from_the_lease_table(self):
        backend = FakeMemoryBackend(0x400000, bytes(0x1000))
        session = MemorySession(1, 0x400000, 0x1000,
                                 backend=backend, owns_handle=False)
        original = b"\xE8\x01\x02\x03\x04"
        backend.write(0x400100, original)
        recipe = DynamicPatchSpec(
            "control_friendly", "友军控制",
            (DynamicCallSite(0x400100, original, 5, 0xE9),), b"\xC3")
        try:
            session.set_dynamic_patch(recipe, True)
            snapshot = session.dynamic_patch_ownership()
            owned_sites = snapshot[recipe.key]
            self.assertIsInstance(owned_sites, tuple)
            self.assertIsInstance(owned_sites[0], tuple)
            self.assertIsInstance(owned_sites[0][1], bytes)
            self.assertEqual(0x400100, owned_sites[0][0])
            self.assertEqual(backend.read(0x400100, 5), owned_sites[0][1])
            snapshot.clear()
            self.assertTrue(session.is_dynamic_patch_enabled(recipe.key))
            session.set_dynamic_patch(recipe, False)
            self.assertEqual({}, session.dynamic_patch_ownership())
            self.assertEqual(1, len(owned_sites))
        finally:
            session.close()


if __name__ == "__main__":
    unittest.main()
