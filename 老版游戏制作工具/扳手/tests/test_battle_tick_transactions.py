"""Full-record transactions for rectangle kill, managed AI and HP/MP locks."""

import os
import struct
import unittest
from unittest.mock import patch

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from PyQt5.QtWidgets import QApplication

import mywindow
from engine_layout import FeatureAccess
from engine_profile import legacy_profile
from main import MyDesiger
from process_memory import FakeMemoryBackend, MemorySession


class BattleTickTransactionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])

    def setUp(self):
        self.window = MyDesiger()
        self.backend = FakeMemoryBackend(0x400000, bytes(0x200000))
        self.session = MemorySession(1, 0x400000, 0x200000,
                                     backend=self.backend, owns_handle=False)
        self.profile = legacy_profile(0)
        self.profile.limits.update(battle_ours=2, battle_friends=1, battle_enemies=4)
        self.profile.set_feature_access("battle", FeatureAccess.READ_WRITE)
        self.window.engine_profile = self.profile
        self.window.memory_session = self.session
        self.window.md = self.session
        self.window.p = self.session.handle
        self.window.process_id = 123
        self.window.ok = True
        self.base = self.profile.addresses["battle_units"]
        self.stride = self.profile.limits["battle_stride"]
        values = {
            "addr_war": self.base, "len_war": self.stride,
            "cnt_wo": 2, "cnt_you": 1, "cnt_di": 4,
            "auto_life": False, "version": 0,
            "lock_list": [], "lock_hp": [], "lock_mp": [], "lock_ids": [],
        }
        self.previous_globals = {key: getattr(mywindow, key) for key in values}
        for key, value in values.items():
            setattr(mywindow, key, value)
        self.rows = [
            self.unit(1, 0, 10, 10),
            self.unit(0xFFFF, 0, 10, 10),
            self.unit(2, 1, 10, 10),
            self.unit(3, 2, 10, 10),
            self.unit(4, 2, 20, 20),
            self.unit(0xFFFF, 2, 10, 10),
            self.unit(6, 1, 10, 10),  # A friendly unit occupying an enemy slot.
        ]
        self.backend.write(self.base, b"".join(self.rows))

    def tearDown(self):
        self.window.close()
        for key, value in self.previous_globals.items():
            setattr(mywindow, key, value)

    def unit(self, data_id, faction, x, y):
        row = bytearray((index * 7 + 11) % 256 for index in range(self.stride))
        struct.pack_into("<H", row, 0, data_id)
        row[5:8] = bytes((faction, x, y))
        row[0x0E] = 11
        struct.pack_into("<II", row, 0x10, 100, 150)
        return bytes(row)

    def rectangle(self, x1="10", y1="10", x2="20", y2="20"):
        for widget, value in zip(
                (self.window.war_input_47, self.window.war_input_48,
                 self.window.war_input_49, self.window.war_input_50),
                (x1, y1, x2, y2)):
            blocked = widget.blockSignals(True)
            widget.setPlainText(value)
            widget.blockSignals(blocked)

    def read_all(self):
        return self.backend.read(self.base, len(self.rows) * self.stride)

    def toggle_managed(self, checked):
        blocked = self.window.checkBox_8.blockSignals(True)
        self.window.checkBox_8.setChecked(checked)
        self.window.checkBox_8.blockSignals(blocked)
        self.window.onButtonClick(8)

    def test_rectangle_kill_rejects_invalid_bounds_without_writes_or_slot_exception(self):
        original = self.read_all()
        for values in (("", "10", "20", "20"), ("-1", "10", "20", "20"),
                       ("10", "10", "256", "20"), ("21", "10", "20", "20"),
                       ("10", "21", "20", "20")):
            with self.subTest(values=values):
                self.rectangle(*values)
                with patch.object(self.backend, "write", wraps=self.backend.write) as write:
                    self.window.onKill()
                write.assert_not_called()
                self.assertEqual(original, self.read_all())
                self.assertTrue(self.window.label_window2.toolTip())

    def test_rectangle_kill_only_changes_nonempty_enemy_hp_and_rolls_back_every_target(self):
        original = self.read_all()
        self.rectangle()
        expected = list(self.rows)
        for slot in (3, 4):
            changed = bytearray(expected[slot])
            changed[0x10:0x14] = bytes(4)
            expected[slot] = bytes(changed)
        with patch.object(self.session, "guarded_compare_exchange_transaction",
                          wraps=self.session.guarded_compare_exchange_transaction) as commit:
            self.window.onKill()
        self.assertEqual(b"".join(expected), self.read_all())
        self.assertEqual(1, commit.call_count)
        entries = tuple(commit.call_args[0][0])
        self.assertEqual(2, len(entries))
        self.assertTrue(all(len(entry.expected) == self.stride for entry in entries))

        self.backend.write(self.base, original)
        self.backend.partial_write_at = self.base + 4 * self.stride
        self.window.onKill()
        self.assertEqual(original, self.read_all())
        self.assertTrue(self.window.label_window2.toolTip())

    def test_managed_control_preserves_unknown_bytes_and_rolls_back_rows_and_checkbox(self):
        original = self.read_all()
        self.toggle_managed(True)
        enabled = list(self.rows)
        for slot in (0, 2):
            row = bytearray(enabled[slot])
            row[0x0E] = 1
            enabled[slot] = bytes(row)
        self.assertEqual(b"".join(enabled), self.read_all())
        self.toggle_managed(False)
        disabled = list(self.rows)
        for slot in (0, 2):
            row = bytearray(disabled[slot])
            row[0x0E] = 7
            disabled[slot] = bytes(row)
        self.assertEqual(b"".join(disabled), self.read_all())

        self.backend.write(self.base, original)
        self.backend.partial_write_at = self.base + 2 * self.stride
        self.toggle_managed(True)
        self.assertFalse(self.window.checkBox_8.isChecked())
        self.assertEqual(original, self.read_all())
        self.assertTrue(self.window.label_window2.toolTip())

    def test_lock_tick_rejects_concurrency_retries_fresh_and_never_splits_hp_mp(self):
        mywindow.lock_list[:] = [0, 2]
        mywindow.lock_hp[:] = [999, 777]
        mywindow.lock_mp[:] = [888, 666]
        mywindow.lock_ids[:] = [1, 2]
        self.window.listview_war_2.addItem("locked unit")
        self.window.listview_war_2.addItem("second locked unit")
        original = self.rows[0]
        second_address = self.base + 2 * self.stride
        second_original = self.rows[2]
        real_commit = self.session.guarded_compare_exchange_transaction

        def concurrent_change(entries):
            self.backend.write(self.base + self.stride - 1, b"M")
            return real_commit(entries)

        with patch.object(mywindow.psutil, "pid_exists", return_value=True), \
                patch.object(self.session, "guarded_compare_exchange_transaction",
                             side_effect=concurrent_change):
            self.window.LockThread()
        expected = original[:-1] + b"M"
        self.assertEqual(expected, self.backend.read(self.base, self.stride))
        self.assertEqual(second_original, self.backend.read(second_address, self.stride))
        self.assertEqual([0, 2], mywindow.lock_list)
        self.assertIn("读取后变化", self.window.label_window2.toolTip())

        with patch.object(mywindow.psutil, "pid_exists", return_value=True):
            self.window.LockThread()
        updated = bytearray(expected)
        struct.pack_into("<II", updated, 0x10, 999, 888)
        self.assertEqual(bytes(updated), self.backend.read(self.base, self.stride))
        second_updated = bytearray(second_original)
        struct.pack_into("<II", second_updated, 0x10, 777, 666)
        self.assertEqual(bytes(second_updated), self.backend.read(second_address, self.stride))

        # A short write of the second row restores both HP/MP pairs and the
        # earlier successfully written row in this same timer tick.
        self.backend.write(self.base, expected)
        self.backend.write(second_address, second_original)
        self.backend.partial_write_at = second_address
        with patch.object(mywindow.psutil, "pid_exists", return_value=True):
            self.window.LockThread()
        self.assertEqual(expected, self.backend.read(self.base, self.stride))
        self.assertEqual(second_original, self.backend.read(second_address, self.stride))
        self.assertEqual([0, 2], mywindow.lock_list)

        # Reusing the same slot for another Data ID removes its old lock.
        self.backend.partial_write_at = None
        replacement = bytearray(expected)
        struct.pack_into("<H", replacement, 0, 9)
        struct.pack_into("<II", replacement, 0x10, 22, 33)
        self.backend.write(self.base, replacement)
        with patch.object(mywindow.psutil, "pid_exists", return_value=True):
            self.window.LockThread()
        self.assertEqual(bytes(replacement), self.backend.read(self.base, self.stride))
        self.assertEqual(bytes(second_updated), self.backend.read(second_address, self.stride))
        self.assertEqual([2], mywindow.lock_list)
        self.assertEqual([777], mywindow.lock_hp)
        self.assertEqual([666], mywindow.lock_mp)
        self.assertEqual([2], mywindow.lock_ids)
        self.assertEqual(1, self.window.listview_war_2.count())
        self.backend.write(second_address, b"\x0A\x00")
        before_last_tick = self.backend.read(second_address, self.stride)
        with patch.object(mywindow.psutil, "pid_exists", return_value=True):
            self.window.LockThread()
        self.assertEqual(before_last_tick, self.backend.read(second_address, self.stride))
        self.assertEqual([], mywindow.lock_list)
        self.assertEqual([], mywindow.lock_hp)
        self.assertEqual([], mywindow.lock_mp)
        self.assertEqual([], mywindow.lock_ids)
        self.assertEqual(0, self.window.listview_war_2.count())


if __name__ == "__main__":
    unittest.main()
