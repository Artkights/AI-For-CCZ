"""The old tool's split-43 recipe, exercised without a real lower-version game."""

import os
import struct
import unittest
from unittest.mock import patch

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from PyQt5.QtWidgets import QApplication
import mywindow
from main import MyDesiger
from engine_layout import AddressResolverKind, FeatureAccess, TableLayoutSpec
from engine_profile import (EngineVersion, legacy_profile,
                            legacy_split_consumable_mapping,
                            locate_consumable_mapping_composite)
from process_memory import FakeMemoryBackend, MemoryAccessError, MemorySession
from runtime_catalog import hydrate_runtime_item_catalog


class LegacySplitConsumableTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])

    def setUp(self):
        self.window = MyDesiger()
        self.backend = FakeMemoryBackend(0x400000, bytes(0x200000))
        self.session = MemorySession(1, 0x400000, 0x200000,
                                     backend=self.backend, owns_handle=False)
        self.profile = legacy_profile(EngineVersion.V63)
        self.window.engine_profile = self.profile
        self.window.memory_session = self.session
        self.window.md = self.session
        self.window.p = self.session.handle
        self.old_version, self.old_count = mywindow.version, mywindow.cnt_item
        mywindow.version, mywindow.cnt_item = EngineVersion.V63, 154
        self.window._fill_choices(self.window.listview_item, ["empty"] * 200)
        self.window._fill_choices(self.window.item_input_1, [str(i) for i in range(155)])
        self.window._fill_choices(self.window.listview_item_2,
                                  [str(i) for i in self.window._consumable_ids()])
        self.backend.write(self.profile.addresses["warehouse"], b"\xFF\0\0" * 200)
        self.window.onItem()

    def tearDown(self):
        self.window.close()
        mywindow.version, mywindow.cnt_item = self.old_version, self.old_count

    def test_four_lower_profiles_share_the_proven_two_ranges_and_exclude_equipment_ids(self):
        for version in (EngineVersion.V61, EngineVersion.V62, EngineVersion.V63,
                        EngineVersion.V63_MP):
            with self.subTest(version=version):
                profile = legacy_profile(version)
                spec, reason = locate_consumable_mapping_composite(object(), version)
                self.assertEqual("", reason)
                self.assertEqual(tuple(range(87, 130)), spec.ui_item_ids)
                self.assertEqual(43, len(spec.addresses))
                self.assertEqual((0x4B09DB, 0x4B09EB, 0x510C80, 0x510C99),
                                 tuple(spec.addresses[i] for i in (0, 16, 17, 42)))
                self.assertEqual(tuple(range(87, 130)), profile.item_ids("consumable"))
                self.assertTrue({0, 86, 130, 153}.issubset(profile.item_ids("equipment")))
                self.assertTrue(set(range(87, 130)).isdisjoint(profile.item_ids("equipment")))

    def test_runtime_probe_checks_the_second_range_before_enabling_writes(self):
        validate = self.session.validate_range
        def inaccessible_second_range(address, size, write=False):
            if 0x510C80 <= address < 0x510C9A:
                return False
            return validate(address, size, write)
        with patch.object(self.session, "validate_range", side_effect=inaccessible_second_range):
            self.profile.probe_runtime(self.session)
        self.assertFalse(self.profile.can_read("item_consumables"))
        self.assertIn("item_consumables", self.profile.runtime_probe_failures)
        self.profile.probe_runtime(self.session)
        self.assertTrue(self.profile.can_write("item_consumables"))

    def test_readonly_second_range_allows_display_but_blocks_single_and_bulk_writes(self):
        validate = self.session.validate_range
        def readonly_second_range(address, size, write=False):
            if write and 0x510C80 <= address < 0x510C9A:
                return False
            return validate(address, size, write)
        with patch.object(self.session, "validate_range", side_effect=readonly_second_range):
            self.profile.probe_runtime(self.session)
        self.assertEqual(FeatureAccess.READ_ONLY,
                         self.profile.resolved_feature("item_consumables").access)
        self.assertTrue(self.profile.can_read("item_consumables"))
        self.assertFalse(self.profile.can_write("item_consumables"))
        self.window.listview_item_2.setCurrentRow(17)
        self.window.onItem()
        self.window.item_input_4.setText("9")
        with patch.object(self.session, "guarded_compare_exchange_transaction") as transaction:
            self.window.saveItem()
            self.window.allItem(2)
        transaction.assert_not_called()
        self.assertTrue(all(self.backend.read(address, 1) == b"\0"
                            for address in legacy_split_consumable_mapping().addresses))

    def test_single_saves_at_both_edges_of_each_split_range_touch_exactly_one_counter(self):
        addresses = legacy_split_consumable_mapping().addresses
        for row in (0, 16, 17, 42):
            with self.subTest(row=row):
                self.window.listview_item_2.setCurrentRow(row)
                self.window.onItem()
                before = self.backend.read(0x400000, 0x200000)
                self.window.item_input_4.setText("9")
                self.window.saveItem()
                after = self.backend.read(0x400000, 0x200000)
                self.assertEqual([addresses[row] - 0x400000],
                                 [i for i in range(len(before)) if before[i] != after[i]])

    def test_bulk_fill_and_clear_touch_all_43_counters_without_the_gap(self):
        addresses = legacy_split_consumable_mapping().addresses
        self.window.allItem(2)
        self.assertTrue(all(self.backend.read(address, 1) == b"\xFF" for address in addresses))
        self.assertEqual(b"\0", self.backend.read(0x4B09EC, 1))
        self.assertEqual(b"\0", self.backend.read(0x510C9A, 1))
        self.window.allItem(3)
        self.assertTrue(all(self.backend.read(address, 1) == b"\0" for address in addresses))

    def test_split_bulk_failure_in_second_range_rolls_back_first_range(self):
        self.backend.fail_write_at = 0x510C80
        self.window.allItem(2)
        self.assertTrue(all(self.backend.read(address, 1) == b"\0"
                            for address in legacy_split_consumable_mapping().addresses))
        self.assertIn("批量修改失败", self.window.label_window2.toolTip())

    def test_bulk_treasures_exclude_43_consumables_even_when_raw_flag_claims_treasure(self):
        rows = []
        for item_id in range(154):
            row = bytearray(25)
            name = ("item%d" % item_id).encode()
            row[:len(name)] = name
            rows.append(bytes(row))
        self.backend.write(self.profile.addresses["item_table"], b"".join(rows))
        self.assertTrue(hydrate_runtime_item_catalog(self.profile, self.session))
        self.window.item_input_2.setText("1")
        self.window.item_input_3.setText("2")
        self.window.allItem(1)
        ids = self.backend.read(self.profile.addresses["warehouse"], 600)[::3]
        self.assertEqual(set(range(154)) - set(range(87, 130)), set(ids) - {255})

    def test_person_noncanonical_flag_is_preserved_until_checkbox_changes(self):
        self.profile = legacy_profile(EngineVersion.V64)
        self.window.engine_profile = self.profile
        mywindow.version, mywindow.cnt_item = EngineVersion.V64, 255
        self.window._fill_choices(self.window.listview_data, ["person"])
        self.window._fill_choices(self.window.data_input_31, [str(i) for i in range(80)])
        for control in (self.window.data_input_32, self.window.data_input_35,
                        self.window.data_input_38):
            self.window._fill_choices(control, [str(i) for i in range(256)])
        base = 0x550000
        self.backend.write(self.profile.addresses["person_pointer"], struct.pack("<I", base))
        raw = bytearray(72)
        struct.pack_into("<H", raw, 0x11, 20)
        struct.pack_into("<I", raw, 0x1B, 100)
        raw[0x2A], raw[0x31], raw[0x34] = 7, 70, 109
        self.backend.write(base, raw)
        self.window.onData()
        self.window.data_input_4.setText("21")
        self.window.saveData()
        self.assertEqual(7, self.backend.read(base + 0x2A, 1)[0])
        self.window.data_input_11.setChecked(True)
        self.window.saveData()
        self.assertEqual(0, self.backend.read(base + 0x2A, 1)[0])
        self.window.data_input_11.setChecked(False)
        self.window.saveData()
        self.assertEqual(255, self.backend.read(base + 0x2A, 1)[0])

    def test_live_pointer_read_failure_never_uses_the_previous_global_table(self):
        self.profile.resolved_features["effect"] = self.profile.resolved_feature("effect").__class__(
            "effect", FeatureAccess.READ_WRITE, layout=TableLayoutSpec(
                "effect", AddressResolverKind.POINTER_RVA, 0x500C3B, 180, 8,
                codec="effect-3+1-v1"))
        self.session.pid = 123
        previous = mywindow.addr_tianfu
        self.addCleanup(setattr, mywindow, "addr_tianfu", previous)
        mywindow.addr_tianfu = 0x508998
        with patch.object(self.session, "read_u32", side_effect=MemoryAccessError("pointer inaccessible")):
            with self.assertRaisesRegex(MemoryAccessError, "pointer inaccessible"):
                self.window._read_effect_snapshot(0)
        self.assertIsNone(self.window._effect_snapshot)


if __name__ == "__main__":
    unittest.main()
