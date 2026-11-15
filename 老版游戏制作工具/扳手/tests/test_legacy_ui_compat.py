"""UI regressions for protected/legacy runtime attachment and lossless edits."""

import os
import struct
import threading
import time
import tempfile
import unittest
from dataclasses import replace
from pathlib import Path
from unittest.mock import Mock, patch

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from PyQt5 import QtCore
from PyQt5.QtWidgets import QApplication, QListWidgetItem, QTableWidgetItem
import mywindow
from main import MyDesiger
from engine_layout import AddressResolverKind, FeatureAccess, TableLayoutSpec
from engine_profile import PatchSite, PatchSpec, legacy_profile
from process_memory import FakeMemoryBackend, MemoryAccessError, MemorySession


class LegacyUiCompatibilityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])

    def setUp(self):
        self.window = MyDesiger()
        self.backend = FakeMemoryBackend(0x400000, bytes(0x200000))
        self.session = MemorySession(1, 0x400000, 0x200000,
                                     backend=self.backend, owns_handle=False)
        self.window.memory_session = self.session
        self.window.engine_profile = legacy_profile(0)
        self.window.md = self.session
        self.window.p = self.session.handle
        self.profile = self.window.engine_profile
        self.previous_version = mywindow.version
        mywindow.version = 0

    def tearDown(self):
        self.window.close()
        mywindow.version = self.previous_version

    def enable(self, key, layout=None):
        if layout is not None:
            self.profile.resolved_features[key] = replace(
                self.profile.resolved_feature(key), layout=layout)
        self.profile.set_feature_access(key, FeatureAccess.READ_WRITE)

    def prepare_fatal(self):
        base = 0x511800
        layout = TableLayoutSpec("fatal", AddressResolverKind.FIXED_RVA,
                                 base, 80, 16)
        self.enable("fatal", layout)
        for control in (self.window.mk_input_1_1, self.window.mk_input_1_3,
                        self.window.mk_input_1_5, self.window.mk_input_1_7,
                        self.window.mk_input_1_9):
            control.addItems(str(i) for i in range(1025))
        self.window.listview_mk.blockSignals(True)
        self.window.listview_mk.addItem("0")
        self.window.listview_mk.setCurrentRow(0)
        self.window.listview_mk.blockSignals(False)
        raw = bytearray(16)
        for index, token in enumerate((0xFFFF, 1278, 1, 1025, 1024)):
            struct.pack_into("<HB", raw, index * 3, token, index + 11)
        raw[15] = 31
        self.backend.write(base, raw)
        self.window.onMk()
        return base, bytes(raw)

    def test_legacy_fatal_preserves_all_opaque_people_when_value_changes(self):
        base, original = self.prepare_fatal()
        self.window.mk_input_1_11.setText("77")
        self.window.saveMk()
        self.assertEqual(original[:15] + b"M", self.backend.read(base, 16))
        self.assertEqual(0xFFFF, struct.unpack_from("<H", self.backend.read(base, 16))[0])

    def test_legacy_fatal_rejects_concurrent_game_changes(self):
        base, original = self.prepare_fatal()
        self.backend.write(base + 15, b"\x55")
        self.window.mk_input_1_11.setText("77")
        self.window.saveMk()
        self.assertEqual(original[:15] + b"\x55", self.backend.read(base, 16))
        self.assertIn("读取后变化", self.window.label_window2.toolTip())

    def test_legacy_fatal_partial_write_restores_complete_record(self):
        base, original = self.prepare_fatal()
        self.backend.partial_write_at = base
        self.window.mk_input_1_2.setText("99")
        self.window.saveMk()
        self.assertEqual(original, self.backend.read(base, 16))

    def test_fatal_save_rejects_changed_fixed_table_reference(self):
        base, original = self.prepare_fatal()
        self.enable("fatal", TableLayoutSpec(
            "fatal", AddressResolverKind.FIXED_RVA, base + 0x1000, 80, 16))
        self.window.mk_input_1_11.setText("77")
        self.window.saveMk()
        self.assertEqual(original, self.backend.read(base, 16))
        self.assertIn("数组已变化", self.window.label_window2.toolTip())

    def test_effect_only_snapshot_commits_when_exclusive_is_unavailable(self):
        base = 0x508998
        layout = TableLayoutSpec("effect", AddressResolverKind.FIXED_RVA,
                                 base, 180, 8, codec="effect-3+1-v1")
        self.enable("effect", layout)
        self.profile.set_feature_access("effect_exclusive", FeatureAccess.UNAVAILABLE)
        original = struct.pack("<HHHBB", 1, 1025, 1278, 255, 31)
        self.backend.write(base, original)
        self.window._read_effect_snapshot(0)
        changed = original[:-1] + b"\x4D"
        self.window._commit_power_snapshots(0, changed, None)
        self.assertEqual(changed, self.backend.read(base, 8))

    def test_exclusive_only_snapshot_commits_without_effect_descriptor(self):
        base = 0x50E400
        layout = TableLayoutSpec("effect_exclusive", AddressResolverKind.FIXED_RVA,
                                 base, 180, 16)
        self.enable("effect_exclusive", layout)
        self.profile.set_feature_access("effect", FeatureAccess.UNAVAILABLE)
        original = bytes(range(16))
        self.backend.write(base, original)
        self.window._read_exclusive_snapshot(0)
        changed = original[:3] + b"\x4D" + original[4:]
        self.window._commit_power_snapshots(0, None, changed)
        self.assertEqual(changed, self.backend.read(base, 16))

    def test_exclusive_save_rejects_changed_fixed_table_reference(self):
        base = 0x50E400
        self.enable("effect_exclusive", TableLayoutSpec(
            "effect_exclusive", AddressResolverKind.FIXED_RVA, base, 180, 16))
        original = bytes(range(16))
        self.backend.write(base, original)
        self.window._read_exclusive_snapshot(0)
        self.enable("effect_exclusive", TableLayoutSpec(
            "effect_exclusive", AddressResolverKind.FIXED_RVA, base + 0x1000, 180, 16))
        with self.assertRaisesRegex(MemoryAccessError, "数组已变化"):
            self.window._commit_power_snapshots(0, None, original[:-1] + b"M")
        self.assertEqual(original, self.backend.read(base, 16))

    def test_unknown_legacy_control_never_falls_back_to_fixed_address_writes(self):
        self.profile.patches.pop("control_friendly", None)
        self.profile.dynamic_patches.pop("control_friendly", None)
        self.window.checkBox_1.setChecked(True)
        before = self.backend.read(0x43D574, 1)
        self.window.onButtonClick(1)
        self.assertFalse(self.window.checkBox_1.isChecked())
        self.assertEqual(before, self.backend.read(0x43D574, 1))
        self.assertIn("补丁配方", self.window.label_window2.toolTip())

    def test_known_legacy_control_uses_the_session_patch_transaction(self):
        recipe = PatchSpec("control_friendly", "友军控制", (
            PatchSite(0x401000, b"\x74", b"\xEB"),))
        self.backend.write(0x401000, b"\x74")
        self.profile.patches["control_friendly"] = recipe
        self.window.checkBox_1.setChecked(True)
        with patch.object(self.session, "set_patch", wraps=self.session.set_patch) as apply:
            self.window.onButtonClick(1)
        apply.assert_called_once_with(recipe, True)
        self.assertEqual(b"\xEB", self.backend.read(0x401000, 1))
        self.assertTrue(self.window.checkBox_1.isChecked())

    def test_effect_save_rejects_layout_change_after_read(self):
        base = 0x508998
        original = struct.pack("<HHHBB", 1, 1025, 1278, 255, 31)
        self.enable("effect", TableLayoutSpec(
            "effect", AddressResolverKind.FIXED_RVA, base, 180, 8,
            codec="effect-3+1-v1"))
        self.backend.write(base, original)
        self.window._read_effect_snapshot(0)
        self.enable("effect", TableLayoutSpec(
            "effect", AddressResolverKind.FIXED_RVA, base, 255, 16,
            codec="effect-4+2-v1"))
        with self.assertRaisesRegex(MemoryAccessError, "表布局已变化"):
            self.window._commit_power_snapshots(0, original[:-1] + b"M", None)
        self.assertEqual(original, self.backend.read(base, 8))

    def test_warehouse_level_edit_saves_without_replacing_the_item(self):
        previous_count = mywindow.cnt_item
        self.addCleanup(setattr, mywindow, "cnt_item", previous_count)
        mywindow.cnt_item = self.profile.limits["item_count"]
        self.enable("warehouse")
        for control, labels in (
                (self.window.listview_item, ["empty"] * 200),
                (self.window.item_input_1,
                 [str(i) for i in range(mywindow.cnt_item + 1)])):
            self.window._fill_choices(control, labels)
        base = self.profile.addresses["warehouse"]
        self.backend.write(base, b"\x01\x05\x0A")
        self.window.onItem()
        self.window.item_input_2.setText("9")
        self.window.item_input_3.setText("88")
        self.window.saveItem()
        self.assertEqual(b"\x01\x09\x58", self.backend.read(base, 3))

    def test_stale_attach_result_does_not_replace_new_session_profile(self):
        token = self.window._attach_token
        self.window._attach_token += 1
        self.window._profile_scan_finished(token, {"profile": object()})
        self.assertIs(self.profile, self.window.engine_profile)

    def test_scan_deadline_keeps_stable_profile_and_allows_initial_attach_retry(self):
        self.window._attach_context = (123, 0x400000, 0x200000, "unused.exe", None, None)
        self.window._scan_deadline = time.monotonic() - 1
        self.window.attach_timer.start()
        self.window._queue_profile_scan()
        self.assertFalse(self.window.attach_timer.isActive())
        with patch.object(self.window, "_queue_profile_scan") as queue, \
                patch.object(self.window, "_probe_patch_controls") as probe:
            self.window.on_action_T_triggered()
        queue.assert_not_called()
        probe.assert_called_once()
        self.assertFalse(self.window.attach_timer.isActive())
        self.assertIs(self.profile, self.window.engine_profile)
        self.window.engine_profile = None
        with patch.object(self.window, "_queue_profile_scan") as queue:
            self.window.on_action_T_triggered()
        queue.assert_called_once()
        self.assertTrue(self.window.attach_timer.isActive())
        self.assertGreater(self.window._scan_deadline, time.monotonic() + 29)

    def test_background_scan_does_not_block_qt_event_delivery(self):
        released = threading.Event()
        entered = threading.Event()
        def slow_scan(task):
            entered.set()
            released.wait(2)
            task.signals.completed.emit(task.token, {"error": "not ready"})
        self.window._attach_context = (123, 0x400000, 0x200000, "unused.exe", None, None)
        self.window._scan_deadline = time.monotonic() + 30
        ticks = []
        with patch.object(mywindow._ProfileScanTask, "run", slow_scan):
            self.window._queue_profile_scan()
            QtCore.QTimer.singleShot(10, lambda: ticks.append(True))
            deadline = time.monotonic() + 1
            while time.monotonic() < deadline and not ticks:
                self.app.processEvents()
                time.sleep(0.002)
            self.assertTrue(entered.is_set())
            self.assertTrue(ticks, "Qt event loop was blocked by runtime scanning")
            released.set()
            self.window._scan_pool.waitForDone(2000)
            self.app.processEvents()

    def test_close_cancels_and_joins_an_active_scan(self):
        entered = threading.Event()
        def cancellable_scan(task):
            entered.set()
            task.cancelled.wait(2)
        self.window._attach_context = (123, 0x400000, 0x200000, "unused.exe", None, None)
        self.window._scan_deadline = time.monotonic() + 30
        with patch.object(mywindow._ProfileScanTask, "run", cancellable_scan):
            self.window._queue_profile_scan()
            self.assertTrue(entered.wait(1))
            self.window.close()
        self.assertEqual(0, self.window._scan_pool.activeThreadCount())
        self.assertIsNone(self.window.memory_session)

    def prepare_battle(self):
        self.enable("battle")
        self.profile.limits.update(battle_ours=2, battle_friends=1, battle_enemies=1)
        for key, value in (("addr_war", self.profile.addresses["battle_units"]),
                           ("len_war", self.profile.limits["battle_stride"]),
                           ("cnt_wo", 2), ("cnt_you", 1), ("cnt_di", 1),
                           ("condition_change", False)):
            self.addCleanup(setattr, mywindow, key, getattr(mywindow, key))
            setattr(mywindow, key, value)
        self.window._fill_choices(self.window.war_input_1, [str(i) for i in range(1024)])
        self.window.war_kind_1.setChecked(True)
        item = QListWidgetItem("0:unit")
        item.setData(QtCore.Qt.UserRole, 0)
        self.window.listview_war.blockSignals(True)
        self.window.listview_war.addItem(item)
        self.window.listview_war.setCurrentRow(0)
        self.window.listview_war.blockSignals(False)
        base = self.profile.addresses["battle_units"]
        stride = self.profile.limits["battle_stride"]
        original = bytearray(stride)
        struct.pack_into("<H", original, 0, 1)
        struct.pack_into("<II", original, 0x10, 10, 20)
        original[0x18] = 2
        original[-1] = 193
        self.backend.write(base, original)
        self.backend.write(self.profile.addresses["save_state"] + 6, b"\x01")
        self.window.onWar(1)
        return base, bytes(original)

    def test_battle_hp_edit_preserves_opaque_and_normalized_buff_bytes(self):
        base, original = self.prepare_battle()
        self.window.war_input_2.setText("77")
        with patch.object(self.window, "onTurn"), patch.object(self.window, "onWar"):
            self.window.saveWar()
        expected = bytearray(original)
        struct.pack_into("<I", expected, 0x10, 77)
        self.assertEqual(bytes(expected), self.backend.read(base, len(original)))

    def test_battle_rejects_concurrent_changes_anywhere_in_the_edited_unit(self):
        base, original = self.prepare_battle()
        self.backend.write(base + len(original) - 1, b"M")
        self.window.war_input_2.setText("77")
        with patch.object(self.window, "onTurn") as helper:
            self.window.saveWar()
        helper.assert_not_called()
        self.assertEqual(original[:-1] + b"M", self.backend.read(base, len(original)))
        self.assertIn("读取后变化", self.window.label_window2.toolTip())

    def test_battle_partial_write_restores_the_entire_unit(self):
        base, original = self.prepare_battle()
        self.backend.partial_write_at = base
        self.window.war_input_2.setText("77")
        with patch.object(self.window, "onTurn") as helper:
            self.window.saveWar()
        helper.assert_not_called()
        self.assertEqual(original, self.backend.read(base, len(original)))

    def test_battle_health_edits_preserve_unexposed_flag_bits(self):
        base, original = self.prepare_battle()
        before = bytearray(original)
        before[0x1E] = 0xA1
        before[0x0D] = 0x87
        self.backend.write(base, before)
        self.window.onWar(1)
        self.window.war_input_5.setChecked(True)
        self.window.war_input_15.setChecked(False)
        with patch.object(self.window, "onTurn"), patch.object(self.window, "onWar"):
            self.window.saveWar()
        expected = bytearray(before)
        expected[0x1E] = 0xA3
        expected[0x0D] = 0x81
        self.assertEqual(bytes(expected), self.backend.read(base, len(original)))

    def test_battle_save_rejects_changed_unit_count(self):
        base, original = self.prepare_battle()
        self.profile.limits["battle_friends"] += 1
        self.window.war_input_2.setText("77")
        with patch.object(self.window, "onTurn") as helper:
            self.window.saveWar()
        helper.assert_not_called()
        self.assertEqual(original, self.backend.read(base, len(original)))
        self.assertIn("布局已变化", self.window.label_window2.toolTip())

    def test_battle_invalid_input_never_invokes_direction_helper(self):
        base, original = self.prepare_battle()
        self.window.war_input_2.setText("invalid")
        with patch.object(self.window, "onTurn") as helper:
            self.window.saveWar()
        helper.assert_not_called()
        self.assertEqual(original, self.backend.read(base, len(original)))

    def test_bulk_battle_edits_roll_back_all_affected_rows(self):
        base, original = self.prepare_battle()
        stride = len(original)
        before = self.backend.read(base, stride * 4)
        self.window.war_input_29.setCurrentIndex(2)
        self.backend.partial_write_at = base + stride
        with patch.object(self.window, "onTurn") as helper:
            self.window.saveWar()
        helper.assert_not_called()
        self.assertEqual(before, self.backend.read(base, stride * 4))

    def prepare_inventory(self):
        self.enable("warehouse")
        self.enable("item_consumables")
        self.addCleanup(setattr, mywindow, "cnt_item", mywindow.cnt_item)
        mywindow.cnt_item = self.profile.limits["item_count"]
        for control, labels in (
                (self.window.listview_item, ["empty"] * 200),
                (self.window.item_input_1, [str(i) for i in range(mywindow.cnt_item + 1)]),
                (self.window.listview_item_2, [str(i) for i in self.window._consumable_ids()])):
            self.window._fill_choices(control, labels)
        warehouse = self.profile.addresses["warehouse"]
        raw = bytearray(b"\xFF\x03\x04" * 200)
        raw[:3] = b"\x01\x05\x0A"
        self.backend.write(warehouse, raw)
        self.window.onItem()
        return warehouse, bytes(raw)

    def test_inventory_multi_field_failure_restores_warehouse_and_money(self):
        warehouse, before = self.prepare_inventory()
        money = self.profile.addresses["money"]
        self.window.item_input_2.setText("9")
        self.window.item_input_5.setText("123")
        self.backend.partial_write_at = warehouse
        self.window.saveItem()
        self.assertEqual(before, self.backend.read(warehouse, 600))
        self.assertEqual(bytes(4), self.backend.read(money, 4))

    def test_bulk_inventory_failure_restores_all_consumables(self):
        self.prepare_inventory()
        ids = self.window._consumable_ids()
        addresses = [self.window._consumable_address(item_id) for item_id in ids]
        fail_at = addresses[2]
        original_write = self.backend.write
        failed = []
        def fail_once(address, payload):
            if address == fail_at and not failed:
                failed.append(True)
                return 0
            return original_write(address, payload)
        with patch.object(self.backend, "write", side_effect=fail_once):
            self.window.allItem(2)
        self.assertTrue(failed)
        self.assertTrue(all(self.backend.read(address, 1) == b"\0" for address in addresses))

    def test_clear_inventory_rejects_concurrent_opaque_byte_change(self):
        warehouse, before = self.prepare_inventory()
        self.backend.write(warehouse + 599, b"M")
        self.window.allItem(3)
        self.assertEqual(before[:-1] + b"M", self.backend.read(warehouse, 600))
        self.assertIn("读取后变化", self.window.label_window2.toolTip())

    def prepare_diy(self):
        self.enable("diy")
        address = 0x510000
        self.backend.write(address, b"\x03\x02")
        self.window.listview_diy.setRowCount(1)
        for column, value in ((1, "77"), (3, "%X" % address), (4, "2")):
            self.window.listview_diy.setItem(0, column, QTableWidgetItem(value))
        self.window.listview_diy.setCurrentCell(0, 1)
        self.window._diy_snapshots = {0: (address, 2, b"\x03\x02", self.session.session_generation)}
        return address

    def test_diy_compare_save_rejects_stale_data_and_session(self):
        address = self.prepare_diy()
        self.backend.write(address, b"\x04\x02")
        self.window.saveDIY()
        self.assertEqual(b"\x04\x02", self.backend.read(address, 2))
        self.assertIn("读取后变化", self.window.label_window2.toolTip())
        self.window._diy_snapshots[0] = (address, 2, b"\x04\x02", "stale-session")
        self.window.saveDIY()
        self.assertIn("旧游戏会话", self.window.label_window2.toolTip())

    def test_diy_page_keeps_address_and_width_readonly_and_saves_value(self):
        self.enable("diy")
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "DIY.csv").write_text(
                "group,,,,,\nname,type,address,width,count,note\nvalue,,510000,2,1,note\n",
                encoding="utf-8")
            self.window.base_dir = Path(directory)
            self.backend.write(0x510000, b"\x03\x02")
            self.window.DIY_Page(0)
            self.window.DIY_Page(1)
            self.assertEqual(1, self.window.listview_diy.rowCount())
            for column in (3, 4):
                self.assertFalse(self.window.listview_diy.item(0, column).flags() & QtCore.Qt.ItemIsEditable)
            self.window.listview_diy.setCurrentCell(0, 1)
            self.window.listview_diy.item(0, 1).setText("77")
            self.window.saveDIY()
            self.assertEqual(b"M\0", self.backend.read(0x510000, 2))

    def test_missing_version_resource_still_queues_runtime_detection(self):
        target = Mock(pid=123)
        target.exe.return_value = "unused.exe"
        scan_contexts = []
        def queue():
            scan_contexts.append(self.window._attach_context)
        disk = Mock(image_base=0x400000)
        session = Mock(handle=1)
        with patch.object(mywindow.psutil, "Process", return_value=target), \
                patch.object(mywindow, "get_process_module", return_value=(0x400000, 0x200000, "unused.exe")), \
                patch.object(mywindow.os.path, "samefile", return_value=True), \
                patch.object(mywindow, "PEImage", return_value=disk), \
                patch.object(mywindow.MemorySession, "open_pid", return_value=session), \
                patch.object(self.window, "getFileVersion", side_effect=OSError("no resource")), \
                patch.object(self.window, "_queue_profile_scan", side_effect=queue):
            self.assertTrue(self.window.attach_pid(123))
        self.assertEqual(1, len(scan_contexts))
        self.assertIsNone(scan_contexts[0][4])
        self.assertTrue(self.window.attach_timer.isActive())


if __name__ == "__main__":
    unittest.main()
