"""Exercise the real variable editor against isolated process-memory bytes."""

import tempfile
import unittest
from pathlib import Path

import test_ui as ui_support


@unittest.skipIf(ui_support.QApplication is None, "PyQt5 is not installed")
@unittest.skipUnless(ui_support.HAS_FIXTURES, ui_support.FIXTURE_SKIP_REASON)
class BooleanVariableUiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = (ui_support.QApplication.instance() or
                   ui_support.QApplication([]))

    def setUp(self):
        self.window = ui_support.MyDesiger()
        self.profile = ui_support.detect_engine(str(
            ui_support.WORKSPACE / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"),
            version_part=6)
        self.profile.set_feature_access(
            "variables_bool", ui_support.FeatureAccess.READ_WRITE,
            runtime_probe_passed=True)
        self.window.engine_profile = self.profile
        self.base = 0x00400000
        self.bool_base = 0x00492FC8
        self.actor_base = 0x004930C8
        raw = bytearray(0x143000)
        offset = self.bool_base - self.base
        for relative, value in ((0, 0xA1), (1, 0x80), (31, 0x01),
                                (32, 0x80), (191, 0x01), (192, 0x80),
                                (255, 0x01)):
            raw[offset + relative] = value
        raw[self.actor_base-self.base:self.actor_base-self.base+4] = (
            0x11223344).to_bytes(4, "little")
        raw[self.actor_base-self.base+252:self.actor_base-self.base+256] = (
            0x55667788).to_bytes(4, "little")
        self.backend = ui_support.FakeMemoryBackend(self.base, bytes(raw))
        self.session = ui_support.MemorySession(
            self.base, self.base, len(raw), backend=self.backend,
            owns_handle=False, reference_image_base=self.base)
        self.window.memory_session = self.session
        self.window.md = self.session
        self.window.p = self.session.handle
        self.statuses = []
        original_status = self.window._set_status

        def remember_status(message):
            self.statuses.append(message)
            original_status(message)

        self.window._set_status = remember_status

    def tearDown(self):
        self.session.close()
        self.window.memory_session = None
        self.window.close()

    def jump(self, index):
        # Include malformed values which the digit-only widget normally filters.
        self.window.var_input_1.blockSignals(True)
        try:
            self.window.var_input_1.setPlainText(str(index))
        finally:
            self.window.var_input_1.blockSignals(False)
        self.window.onVar(1)
        return self.window.listview_var_1

    def save(self, row, value):
        table = self.window.listview_var_1
        table.item(row, 1).setText(value)
        table.setCurrentCell(row, 1)
        self.window.saveVar(1)

    def test_bit_addresses_masks_and_editable_domain_edges(self):
        for index, address, bit, expected in (
                (0, 0x492FC8, 7, "True"),
                (1, 0x492FC8, 6, "False"),
                (7, 0x492FC8, 0, "True"),
                (8, 0x492FC9, 7, "True"),
                (255, 0x492FE7, 0, "True"),
                (256, 0x492FE8, 7, "True"),
                (1535, 0x493087, 0, "True")):
            with self.subTest(index=index):
                table = self.jump(index)
                self.assertEqual("%06X.%d" % (address, bit),
                                 table.item(0, 2).text())
                self.assertEqual(expected, table.item(0, 1).text())
                self.assertTrue(table.item(0, 1).flags() &
                                ui_support.mywindow_module.QtCore.Qt.ItemIsEditable)
                self.assertIn("0x%08X" % address, table.item(0, 2).toolTip())
                self.assertIn("0x%02X" % (1 << bit), table.item(0, 2).toolTip())
                self.save(0, "False" if expected == "True" else "True")
                self.assertEqual(0 if expected == "True" else 1 << bit,
                                 self.session.read_u8(address) & (1 << bit))

    def test_bit_display_explains_shared_byte_within_existing_width(self):
        table = self.jump(0)
        self.assertEqual("地址.位", table.horizontalHeaderItem(2).text())
        tip = table.item(0, 2).toolTip()
        self.assertIn("bit7", tip)
        self.assertIn("原始字节：0xA1", tip)
        self.assertIn("0..7", tip)
        self.assertIn("8 个布尔变量共用 1 个字节", tip)
        self.assertLessEqual(sum(table.columnWidth(i) for i in range(3)), 190)

    def test_rebased_process_displays_actual_address_and_saves_reference_snapshot(self):
        raw = self.backend.read(self.base, 0x143000)
        self.session.close()
        actual_base = 0x00700000
        self.backend = ui_support.FakeMemoryBackend(actual_base, raw)
        self.session = ui_support.MemorySession(
            actual_base, actual_base, len(raw), backend=self.backend,
            owns_handle=False, reference_image_base=self.base)
        self.window.memory_session = self.session
        table = self.jump(1)
        self.assertEqual("792FC8.6", table.item(0, 2).text())
        self.assertIn("字节地址：0x00792FC8", table.item(0, 2).toolTip())
        self.assertIn("参考地址：0x00492FC8", table.item(0, 2).toolTip())
        self.assertIsNotNone(self.window._boolean_snapshot)
        self.assertEqual(self.bool_base,
                         self.window._boolean_snapshot.first_address)
        self.save(0, "True")
        self.assertEqual(b"\xE1", self.backend.read(0x00792FC8, 1))

    def test_reserved_bits_are_read_only_and_save_rejects_programmatic_edits(self):
        for index in (1536, 2047):
            with self.subTest(index=index):
                table = self.jump(index)
                self.assertFalse(table.item(0, 1).flags() &
                                 ui_support.mywindow_module.QtCore.Qt.ItemIsEditable)
                self.assertIn("用途未确认", table.item(0, 1).toolTip())
                before = self.session.read_bytes(self.bool_base, 256)
                self.save(0, "False")
                self.assertEqual(before, self.session.read_bytes(self.bool_base, 256))
                self.assertIn("只读", self.statuses[-1])

    def test_page_crossing_user_boundary_keeps_reserved_values_read_only(self):
        table = self.jump(1533)
        editable = ui_support.mywindow_module.QtCore.Qt.ItemIsEditable
        self.assertTrue(table.item(2, 1).flags() & editable)
        self.assertFalse(table.item(3, 1).flags() & editable)
        self.assertEqual("1536", table.item(3, 0).text())

    def test_non_aligned_browse_tail_stops_before_actor_table(self):
        table = self.jump(2043)
        self.assertEqual(5, table.rowCount())
        self.assertEqual("2047", table.item(4, 0).text())
        self.assertEqual("4930C7.0", table.item(4, 2).text())
        self.assertIsNotNone(self.window._boolean_snapshot)
        self.assertEqual(256, len(self.window._boolean_snapshot.raw))
        self.assertEqual(self.bool_base, self.window._boolean_snapshot.first_address)

    def test_out_of_range_jump_clears_rows_and_old_snapshots(self):
        for index in (2048, 4095, -1, "", "word"):
            with self.subTest(index=index):
                self.jump(0)
                table = self.jump(index)
                self.assertEqual(0, table.rowCount())
                self.assertIsNone(self.window._boolean_snapshot)

    def test_forged_old_rows_cannot_modify_actor_dwords_even_in_direct_mode(self):
        self.profile.direct_write = True
        for index, address, actor in ((2048, self.actor_base, 0),
                                      (4095, self.actor_base + 255, 63)):
            with self.subTest(index=index):
                table = self.jump(0)
                table.item(0, 0).setText(str(index))
                before = self.session.read_u32(self.actor_base + actor * 4)
                self.save(0, "True")
                self.assertEqual(before, self.session.read_u32(
                    self.actor_base + actor * 4))
                self.assertIn("0..2047", self.statuses[-1])

    def test_unknown_native_format_cannot_be_saved_through_compatibility(self):
        layout = self.profile.metadata["variable_layouts"]["variables_bool"]
        layout["formatVerified"] = False
        self.profile.direct_write = True
        table = self.jump(0)
        self.assertFalse(table.item(0, 1).flags() &
                         ui_support.mywindow_module.QtCore.Qt.ItemIsEditable)
        self.save(0, "False")
        self.assertEqual(0xA1, self.session.read_u8(self.bool_base))
        self.assertIn("格式未通过", self.statuses[-1])

    def test_old_unbounded_profile_does_not_expose_actor_rows(self):
        layout = self.profile.metadata["variable_layouts"]["variables_bool"]
        layout.pop("browseRecordCount")
        layout["recordCount"] = 4096
        self.assertEqual(0, self.jump(2048).rowCount())

    def test_input_tokens_preserve_other_bits_and_neighboring_byte(self):
        for token, expected in (("TRUE", 0xE1), ("false", 0xA1),
                                ("1", 0xE1), ("0", 0xA1)):
            self.jump(0)
            self.save(1, token)
            self.assertEqual(expected, self.session.read_u8(self.bool_base))
            self.assertEqual(0x80, self.session.read_u8(self.bool_base + 1))
        for token in ("yes", "2", "", "-1"):
            self.jump(0)
            self.save(1, token)
            self.assertEqual(0xA1, self.session.read_u8(self.bool_base))
            self.assertIn("True/False/1/0", self.statuses[-1])

    def test_single_row_save_keeps_other_unsaved_page_edits(self):
        table = self.jump(0)
        table.item(0, 1).setText("False")
        table.item(1, 1).setText("True")
        table.setCurrentCell(0, 1)
        self.window.saveVar(1)

        # Row 0 was committed, while row 1 remains an in-memory UI edit.
        self.assertEqual(0x21, self.session.read_u8(self.bool_base))
        self.assertEqual("False", table.item(0, 1).text())
        self.assertEqual("True", table.item(1, 1).text())
        self.window.saveBooleanPage()
        self.assertEqual(0x61, self.session.read_u8(self.bool_base))

    def test_page_save_merges_same_byte_and_cross_byte_edits(self):
        table = self.jump(0)
        for row, value in ((0, "False"), (1, "True"),
                           (8, "False"), (16, "True")):
            table.item(row, 1).setText(value)
        table.setCurrentCell(16, 1)
        self.window.saveBooleanPage()

        self.assertEqual(0x61, self.session.read_u8(self.bool_base))
        self.assertEqual(0x00, self.session.read_u8(self.bool_base + 1))
        self.assertEqual(0x80, self.session.read_u8(self.bool_base + 2))
        self.assertEqual(0x00, self.session.read_u8(self.bool_base + 23))
        self.assertEqual(0x44, self.session.read_u8(self.actor_base))

    def test_full_export_import_round_trip_uses_temp_archive(self):
        self.jump(0)
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "flags.cczbool"
            self.window.exportBooleanArchive(str(archive))
            self.assertTrue(archive.is_file())
            exported = archive.read_bytes()
            self.assertTrue(exported.startswith(b"CCZBOOL1"))

            self.backend.write(self.bool_base, b"\x00")
            self.backend.write(self.bool_base + 1, b"\xFF")
            self.backend.write(self.bool_base + 191, b"\x00")
            actor_before = self.session.read_u32(self.actor_base)
            self.window.importBooleanArchive(str(archive))

            self.assertEqual(0xA1, self.session.read_u8(self.bool_base))
            self.assertEqual(0x80, self.session.read_u8(self.bool_base + 1))
            self.assertEqual(0x01, self.session.read_u8(self.bool_base + 191))
            self.assertEqual(actor_before, self.session.read_u32(self.actor_base))
            self.assertIn("已全量导入", self.statuses[-1])

    def test_archive_count_or_crc_mismatch_rejects_without_touching_actor_data(self):
        self.jump(0)
        actor_before = self.session.read_u32(self.actor_base)
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "valid.cczbool"
            self.window.exportBooleanArchive(str(source))
            valid = source.read_bytes()
            for suffix, mutate in (
                    ("count", lambda data: data.__setitem__(slice(8, 12),
                                                               (1535).to_bytes(4, "little"))),
                    ("crc", lambda data: data.__setitem__(-1, data[-1] ^ 0x80))):
                with self.subTest(suffix=suffix):
                    target = Path(directory) / (suffix + ".cczbool")
                    damaged = bytearray(valid)
                    mutate(damaged)
                    target.write_bytes(damaged)
                    self.window.importBooleanArchive(str(target))
                    self.assertEqual(actor_before,
                                     self.session.read_u32(self.actor_base))
                    self.assertIn("布尔全量导入失败", self.statuses[-1])

    def test_boolean_save_rejects_expired_session_or_changed_layout(self):
        self.jump(0)
        self.session.session_generation += ":expired"
        self.save(0, "False")
        self.assertIn("游戏会话", self.statuses[-1])
        self.assertEqual(0x11223344, self.session.read_u32(self.actor_base))

        self.session.session_generation = self.window._boolean_snapshot.session_generation
        self.jump(0)
        layout = self.profile.metadata["variable_layouts"]["variables_bool"]
        layout["recordCount"] = int(layout["recordCount"]) - 1
        self.save(0, "False")
        self.assertIn("保存变量失败", self.statuses[-1])
        self.assertEqual(0x11223344, self.session.read_u32(self.actor_base))

    def test_boolean_context_actions_are_reachable_from_table_and_save_button(self):
        labels = [action.text() for action in self.window._boolean_actions]
        self.assertEqual(["保存选中行", "保存本页全部修改", "全量导出…",
                          "全量导入并保存…"], labels)
        policy = ui_support.mywindow_module.QtCore.Qt.ActionsContextMenu
        self.assertEqual(policy, self.window.listview_var_1.contextMenuPolicy())
        self.assertEqual(policy, self.window.var_save_1.contextMenuPolicy())
        for action in self.window._boolean_actions:
            self.assertIn(action, self.window.listview_var_1.actions())
            self.assertIn(action, self.window.var_save_1.actions())

    def test_game_changes_to_same_byte_are_rejected_without_overwriting_neighbor(self):
        self.jump(0)
        self.backend.write(self.bool_base, b"\xA3")
        self.save(1, "True")
        self.assertEqual(0xA3, self.session.read_u8(self.bool_base))
        self.assertIn("内存已在读取后变化", self.statuses[-1])

    def test_legacy_non_bitmap_boolean_keeps_plain_address(self):
        profile = ui_support.detect_composite_profile(
            str(ui_support.WORKSPACE / "基底" / "加强版6.5未加密版" / "Ekd5.exe"),
            version_part=5)
        self.window.engine_profile = profile
        table = self.jump(1)
        self.assertEqual("地址", table.horizontalHeaderItem(2).text())
        self.assertEqual("492FCC", table.item(0, 2).text())
        self.assertTrue(table.item(0, 1).flags() &
                        ui_support.mywindow_module.QtCore.Qt.ItemIsEditable)


if __name__ == "__main__":
    unittest.main()
