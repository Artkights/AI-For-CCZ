import os
import inspect
import struct
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

try:
    import mywindow as mywindow_module
    from PyQt5.QtWidgets import QApplication
    from engine_profile import (EffectAssignmentRow8, EffectAssignmentRow66,
                                EffectAssignmentSlot66, detect_engine,
                                detect_composite_profile, legacy_profile,
                                encode_effect_assignment_row_8,
                                encode_effect_assignment_row_66)
    from engine_layout import FeatureAccess
    from main import MyDesiger
    from process_memory import (FakeMemoryBackend, MemoryAccessError,
                                MemorySession)
except ImportError:
    QApplication = None
    MyDesiger = None


@unittest.skipIf(QApplication is None, "PyQt5 is not installed")
class UiCompatibilityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])

    def setUp(self):
        self.window = MyDesiger()

    def tearDown(self):
        self.window.close()

    def test_fixed_window_and_original_eight_pages(self):
        self.assertEqual(700, self.window.width())
        self.assertEqual(530, self.window.height())
        self.assertEqual(700, self.window.minimumWidth())
        self.assertEqual(530, self.window.minimumHeight())
        self.assertEqual(700, self.window.maximumWidth())
        self.assertEqual(530, self.window.maximumHeight())
        for index in range(1, 9):
            widget = getattr(self.window, "widget_%d" % index)
            self.assertEqual("widget_%d" % index, widget.objectName())

    def test_expanded_65_item_domains_are_visible_but_write_domains_stay_split(self):
        profile = legacy_profile(5)
        self.window.engine_profile = profile
        previous_count = mywindow_module.cnt_item
        self.addCleanup(setattr, mywindow_module, "cnt_item", previous_count)
        mywindow_module.cnt_item = profile.limits["item_count"]
        self.assertEqual(255, len(self.window._item_ids("record")))
        self.assertEqual(150, len(self.window._item_ids("equipment")))
        self.assertEqual(105, len(self.window._item_ids("consumable")))
        self.assertTrue(self.window._is_item_record(254))
        self.assertFalse(self.window._is_item_record(255))
        self.assertTrue(self.window._is_equipment_item(149))
        self.assertFalse(self.window._is_equipment_item(150))

    def test_version_selector_geometry_labels_and_auto_mapping(self):
        expected = {
            "version_1": ("自动", (20, 475, 60, 19)),
            "version_5": ("6.6", (90, 475, 50, 19)),
            "version_6": ("6.5", (150, 475, 50, 19)),
            "version_0": ("6.4", (210, 475, 50, 19)),
            "version_2": ("6.3", (270, 475, 50, 19)),
            "version_3": ("6.2", (330, 475, 50, 19)),
            "version_4": ("6.1", (390, 475, 50, 19)),
        }
        for name, (text, geometry) in expected.items():
            control = getattr(self.window, name)
            self.assertEqual(text, control.text())
            self.assertEqual(geometry, control.geometry().getRect())
        self.window.version_1.setChecked(True)
        for version_part, expected_version in (
                (6, 6), (5, 5), (4, 0), (3, 2), (2, 3), (1, 4)):
            self.assertEqual(expected_version,
                             self.window._resolve_engine_version(version_part))
            self.assertTrue(self.window.version_1.isChecked())
        self.window.version_5.setChecked(True)
        self.assertEqual(6, self.window._resolve_engine_version(4))
        self.window.version_6.setChecked(True)
        self.assertEqual(5, self.window._resolve_engine_version(6))
        self.window.version_0.setChecked(True)
        self.assertEqual(0, self.window._resolve_engine_version(6))

        ui_path = Path(__file__).resolve().parents[1] / "untitled.ui"
        root = ET.parse(ui_path).getroot()
        widgets = {item.attrib.get("name"): item
                   for item in root.iter("widget")}
        for name, (text, geometry) in expected.items():
            widget = widgets[name]
            self.assertEqual(text, widget.find("./property[@name='text']/string").text)
            rect = widget.find("./property[@name='geometry']/rect")
            actual = tuple(int(rect.find(field).text)
                           for field in ("x", "y", "width", "height"))
            self.assertEqual(geometry, actual)

    def test_effect_name_list_uses_current_profile_and_ui_boundary(self):
        workspace = Path(__file__).resolve().parents[3]
        base65 = workspace / "基底" / "加强版6.5未加密版" / "Ekd5.exe"
        pl65 = (workspace / "基底" /
                "6.5pl加强版神话三国志2026新春版+攻略【密码shsgz65】" /
                "Ekd5.exe")
        base66 = workspace / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"

        self.window.engine_profile = detect_composite_profile(
            str(base65), version_part=5)
        self.window._populate_profile_effect_names()
        self.assertEqual(180, self.window.listview_item_3.count())
        self.assertEqual("0:每回合恢复HP",
                         self.window.listview_item_3.item(0).text())
        self.assertEqual("63:步兵攻击",
                         self.window.listview_item_3.item(63).text())

        self.window.engine_profile = detect_engine(str(base66), version_part=6)
        self.window._populate_profile_effect_names()
        self.assertEqual(255, self.window.listview_item_3.count())
        self.assertEqual("254:FE特效",
                         self.window.listview_item_3.item(254).text())

        absolute_counter = next(
            (workspace / "基底").glob("8.14-*Ekd5.exe"))
        self.window.engine_profile = detect_engine(
            str(absolute_counter), version_part=6)
        self.window._populate_profile_effect_names()
        self.assertEqual(255, self.window.listview_item_3.count())
        self.assertEqual("127:深策缚影",
                         self.window.listview_item_3.item(127).text())
        self.assertEqual("254:☆神圣庇佑☆",
                         self.window.listview_item_3.item(254).text())

        self.window.engine_profile = detect_composite_profile(
            str(pl65), version_part=5)
        self.window._populate_profile_effect_names()
        self.assertEqual(180, self.window.listview_item_3.count())
        self.assertEqual("63:恶路移动",
                         self.window.listview_item_3.item(63).text())

    def test_original_page_actions_and_pivot_signal_surface_exist(self):
        for name in ("action_C", "action_M", "action_B", "action_W",
                     "action_T", "action_V", "action_D", "action_K"):
            self.assertTrue(hasattr(self.window, name), name)
        self.assertEqual("label_pivot", self.window.label_pivot.objectName())
        self.assertTrue(callable(self.window.mousePressEvent))
        self.assertTrue(callable(self.window.mouseReleaseEvent))
        self.assertEqual("MyDesiger.mouseReleaseEvent",
                         self.window.mouseReleaseEvent.__func__.__qualname__)

    def test_attach_pipeline_opens_memory_before_runtime_profile_detection(self):
        source = inspect.getsource(
            mywindow_module.Ui_MainWindow.mouseReleaseEvent)
        open_at = source.index("MemorySession.open_pid")
        runtime_at = source.index("RuntimeModuleImage")
        detect_at = source.index("detect_composite_profile")
        self.assertLess(open_at, runtime_at)
        self.assertLess(runtime_at, detect_at)
        self.assertNotIn("if not identity[\"core_signature_passed\"]",
                         source)

    def test_effect_layout_switches_without_moving_lower_sections(self):
        lower_geometry = (self.window.power_2.geometry().getRect(),
                          self.window.power_3.geometry().getRect(),
                          self.window.item_save_2.geometry().getRect())
        for index in range(6, 13):
            self.assertTrue(getattr(
                self.window, "power_input_1_%d" % index).isHidden())
        self.assertEqual((185, 40, 136, 25),
                         self.window.power_input_1_1.geometry().getRect())
        self.assertEqual((355, 80, 136, 25),
                         self.window.power_input_1_5.geometry().getRect())

        self.window._set_effect_layout(True)
        expected = {
            "power_input_1_1": (185, 40, 96, 25),
            "power_input_1_5": (285, 40, 36, 25),
            "power_input_1_4": (185, 80, 96, 25),
            "power_input_1_8": (285, 80, 36, 25),
            "power_input_1_9": (355, 80, 96, 25),
            "power_input_1_10": (455, 80, 36, 25),
            "power_input_1_11": (525, 80, 96, 25),
            "power_input_1_12": (625, 80, 36, 25),
        }
        for name, geometry in expected.items():
            control = getattr(self.window, name)
            self.assertFalse(control.isHidden(), name)
            self.assertEqual(geometry, control.geometry().getRect(), name)
        self.assertEqual(lower_geometry,
                         (self.window.power_2.geometry().getRect(),
                          self.window.power_3.geometry().getRect(),
                          self.window.item_save_2.geometry().getRect()))

        self.window._set_effect_layout(False)
        self.assertEqual((185, 40, 136, 25),
                         self.window.power_input_1_1.geometry().getRect())
        self.assertEqual((355, 80, 136, 25),
                         self.window.power_input_1_5.geometry().getRect())

    def test_ui_source_contains_the_extended_effect_controls(self):
        ui_path = Path(__file__).resolve().parents[1] / "untitled.ui"
        root = ET.parse(ui_path).getroot()
        names = {item.attrib.get("name") for item in root.iter("widget")}
        for index in range(1, 13):
            self.assertIn("power_input_1_%d" % index, names)
        for index in range(2, 7):
            self.assertIn("power_label_1_%d" % index, names)

    def _prepare_effect_memory(self, row):
        address = 0x00511000
        encoded = encode_effect_assignment_row_66(row)
        backend = FakeMemoryBackend(address, encoded + b"\0" * 0x100)
        session = MemorySession(
            1, 0x00400000, 0x143000, backend=backend,
            owns_handle=False, reference_image_base=0x00400000)
        workspace = Path(__file__).resolve().parents[3]
        self.window.engine_profile = detect_engine(str(
            workspace / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"),
            version_part=6)
        self.window.memory_session = session
        mywindow_module.addr_tianfu = address
        for control in (self.window.power_input_1_1,
                        self.window.power_input_1_2,
                        self.window.power_input_1_3,
                        self.window.power_input_1_4):
            control.addItems(str(index) for index in range(1025))
        for control in (self.window.power_input_1_9,
                        self.window.power_input_1_11):
            control.addItems(str(index) for index in range(81))
        return address, backend

    def test_effect_assignment_66_ui_round_trip_and_empty_normalization(self):
        row = EffectAssignmentRow66(
            (EffectAssignmentSlot66(1, 0),
             EffectAssignmentSlot66(1023, 255),
             EffectAssignmentSlot66(1024, 17),
             EffectAssignmentSlot66(148, 20)),
            (EffectAssignmentSlot66(72, 30),
             EffectAssignmentSlot66(255, 20)))
        address, backend = self._prepare_effect_memory(row)
        self.window._show_effect_assignment_66(
            self.window._read_effect_assignment_66(0))
        self.assertEqual(148, self.window.power_input_1_4.currentIndex())
        self.assertEqual(80, self.window.power_input_1_11.currentIndex())
        self.assertEqual("255", self.window.power_input_1_6.toPlainText())
        collected = self.window._collect_effect_assignment_66()
        self.assertEqual(255, collected.jobs[1].target_id)
        self.assertEqual(row, collected)
        self.window._write_effect_assignment_66(0, collected)
        self.assertEqual(encode_effect_assignment_row_66(row),
                         backend.read(address, 16))

    def test_effect_assignment_66_v716_empty_character_round_trip(self):
        row = EffectAssignmentRow66(
            (EffectAssignmentSlot66(31, 20),
             EffectAssignmentSlot66(20, 25),
             EffectAssignmentSlot66(1025, 30),
             EffectAssignmentSlot66(1025, 0)),
            (EffectAssignmentSlot66(30, 20),
             EffectAssignmentSlot66(255, 0)))
        address, backend = self._prepare_effect_memory(row)
        self.window.engine_profile.metadata["effect_empty_character"] = 1025
        self.window._show_effect_assignment_66(
            self.window._read_effect_assignment_66(0))
        self.assertEqual(31, self.window.power_input_1_1.currentIndex())
        self.assertEqual(20, self.window.power_input_1_2.currentIndex())
        self.assertEqual(1024, self.window.power_input_1_3.currentIndex())
        self.assertEqual(1024, self.window.power_input_1_4.currentIndex())
        self.assertEqual(30, self.window.power_input_1_9.currentIndex())
        self.assertEqual(80, self.window.power_input_1_11.currentIndex())
        self.window.power_input_1_5.setText("21")
        collected = self.window._collect_effect_assignment_66()
        self.assertEqual((1025, 1025), tuple(
            item.target_id for item in collected.characters[2:]))
        self.assertEqual(21, collected.characters[0].effect_value)
        self.window._write_effect_assignment_66(0, collected)
        self.assertEqual(encode_effect_assignment_row_66(collected),
                         backend.read(address, 16))

    def test_effect_assignment_66_mixed_empty_characters_round_trip(self):
        row = EffectAssignmentRow66(
            (EffectAssignmentSlot66(1024, 20),
             EffectAssignmentSlot66(1024, 25),
             EffectAssignmentSlot66(1024, 30),
             EffectAssignmentSlot66(1025, 0)),
            (EffectAssignmentSlot66(255, 20),
             EffectAssignmentSlot66(255, 0)))
        address, backend = self._prepare_effect_memory(row)
        self.window.engine_profile.metadata["effect_empty_characters"] = (
            1024, 1024, 1024, 1025)
        self.window._show_effect_assignment_66(
            self.window._read_effect_assignment_66(0))
        self.assertEqual([1024, 1024, 1024, 1024], [
            control.currentIndex() for control in (
                self.window.power_input_1_1, self.window.power_input_1_2,
                self.window.power_input_1_3, self.window.power_input_1_4)])
        collected = self.window._collect_effect_assignment_66()
        self.assertEqual(row, collected)
        self.window._write_effect_assignment_66(0, collected)
        self.assertEqual(encode_effect_assignment_row_66(row),
                         backend.read(address, 16))

    def test_effect_assignment_66_partial_write_keeps_written_prefix(self):
        original = EffectAssignmentRow66(
            tuple(EffectAssignmentSlot66(1024, 1) for _ in range(4)),
            (EffectAssignmentSlot66(255, 1),
             EffectAssignmentSlot66(255, 1)))
        changed = EffectAssignmentRow66(
            tuple(EffectAssignmentSlot66(index, index + 10)
                  for index in range(4)),
            (EffectAssignmentSlot66(1, 20),
             EffectAssignmentSlot66(2, 21)))
        address, backend = self._prepare_effect_memory(original)
        backend.partial_write_at = address
        with self.assertRaises(MemoryAccessError):
            self.window._write_effect_assignment_66(0, changed)
        expected = (encode_effect_assignment_row_66(changed)[:-1] +
                    encode_effect_assignment_row_66(original)[-1:])
        self.assertEqual(expected, backend.read(address, 16))

    def test_hybrid_8_byte_effect_round_trip_preserves_opaque_empty_tokens(self):
        address = 0x00507800
        row = EffectAssignmentRow8((1, 1025, 1278), 200, 37)
        encoded = encode_effect_assignment_row_8(row)
        backend = FakeMemoryBackend(address, encoded + b"\0" * 0x100)
        session = MemorySession(
            1, 0x00400000, 0x143000, backend=backend,
            owns_handle=False, reference_image_base=0x00400000)
        workspace = Path(__file__).resolve().parents[3]
        self.window.engine_profile = detect_engine(str(
            workspace / "基底" / "厚黑6.6测（7.9水杯版）" / "Ekd5.exe"),
            version_part=6)
        self.window.memory_session = session
        mywindow_module.addr_tianfu = address
        for control in (self.window.power_input_1_1,
                        self.window.power_input_1_2,
                        self.window.power_input_1_3):
            control.addItems(str(index) for index in range(1025))
        self.window.power_input_1_4.addItems(
            str(index) for index in range(81))
        snapshot = self.window._read_effect_snapshot(0)
        self.window._show_effect_assignment_8(snapshot.decoded)
        self.assertEqual((1, 1024, 1024), tuple(
            control.currentIndex() for control in (
                self.window.power_input_1_1, self.window.power_input_1_2,
                self.window.power_input_1_3)))
        collected = self.window._collect_effect_assignment_8()
        self.assertEqual(row, collected)
        self.window._write_effect_assignment_8(0, collected)
        self.assertEqual(encoded, backend.read(address, 8))

    def _prepare_exclusive_memory(self, raw):
        address = 0x0050E000
        backend = FakeMemoryBackend(address, raw + b"\0" * 0x100)
        session = MemorySession(
            1, 0x00400000, 0x143000, backend=backend,
            owns_handle=False, reference_image_base=0x00400000)
        workspace = Path(__file__).resolve().parents[3]
        self.window.engine_profile = detect_engine(str(
            workspace / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"),
            version_part=6)
        self.window.memory_session = session
        mywindow_module.addr_zhuanshu = address
        mywindow_module.cnt_item = 160
        for control in (self.window.power_input_2_1,
                        self.window.power_input_2_4):
            control.addItems(str(index) for index in range(1025))
        for control in (self.window.power_input_2_2,
                        self.window.power_input_2_5,
                        self.window.power_input_3_1,
                        self.window.power_input_3_2,
                        self.window.power_input_3_3,
                        self.window.power_input_3_5,
                        self.window.power_input_3_6,
                        self.window.power_input_3_7):
            control.addItems(str(index) for index in range(161))
        snapshot = self.window._read_exclusive_snapshot(0)
        self.window._show_exclusive_assignment(snapshot.raw)
        return address, backend

    def test_exclusive_round_trip_preserves_opaque_person_and_item_tokens(self):
        raw = (
            (1025).to_bytes(2, "little") + bytes((200, 37)) +
            (1278).to_bytes(2, "little") + bytes((254, 0)) +
            bytes((200, 201, 254, 20, 10, 255, 240, 0)))
        address, backend = self._prepare_exclusive_memory(raw)
        self.assertEqual(1024, self.window.power_input_2_1.currentIndex())
        self.assertEqual(160, self.window.power_input_2_2.currentIndex())
        self.assertEqual(1024, self.window.power_input_2_4.currentIndex())
        self.assertEqual(160, self.window.power_input_3_1.currentIndex())

        self.window.power_input_2_3.setText("38")
        self.window.power_input_3_4.setText("21")
        replacement = self.window._collect_exclusive_assignment()
        expected = bytearray(raw)
        expected[3] = 38
        expected[11] = 21
        self.assertEqual(bytes(expected), replacement)
        self.window._write_exclusive_assignment(0, replacement)
        self.assertEqual(bytes(expected), backend.read(address, 16))

    def test_exclusive_explicit_empty_and_zero_use_canonical_old_semantics(self):
        raw = ((10).to_bytes(2, "little") + bytes((20, 30)) +
               (11).to_bytes(2, "little") + bytes((21, 31)) +
               bytes((22, 23, 24, 32, 25, 26, 27, 33)))
        self._prepare_exclusive_memory(raw)
        self.window.power_input_2_1.setCurrentIndex(1024)
        self.window.power_input_2_2.setCurrentIndex(160)
        replacement = self.window._collect_exclusive_assignment()
        self.assertEqual(1024,
                         int.from_bytes(replacement[0:2], "little"))
        self.assertEqual(255, replacement[2])
        self.assertEqual(30, replacement[3])

        self.window.power_input_2_3.setText("0")
        replacement = self.window._collect_exclusive_assignment()
        self.assertEqual(b"\0" * 4, replacement[:4])

    def test_exclusive_save_without_cache_uses_current_memory_as_base(self):
        raw = (
            (1025).to_bytes(2, "little") + bytes((200, 37)) +
            (1278).to_bytes(2, "little") + bytes((254, 38)) +
            bytes((200, 201, 254, 20, 205, 206, 207, 40)))
        address, backend = self._prepare_exclusive_memory(raw)
        current = bytearray(raw)
        current[0:2] = (4095).to_bytes(2, "little")
        backend.write(address, bytes(current))
        self.window._exclusive_snapshot = None
        self.window._shown_exclusive_state = None
        self.window.power_input_2_3.setText("41")

        replacement = self.window._collect_exclusive_assignment(0)

        expected = bytearray(current)
        expected[3] = 41
        self.assertEqual(bytes(expected), replacement)

    def test_power_direct_write_keeps_effect_when_exclusive_write_fails(self):
        memory_base = 0x00500000
        effect_base = 0x00511000
        exclusive_base = 0x0050E000
        original_effect = EffectAssignmentRow66(
            tuple(EffectAssignmentSlot66(1025, 10 + index)
                  for index in range(4)),
            (EffectAssignmentSlot66(255, 20),
             EffectAssignmentSlot66(255, 21)))
        encoded_effect = encode_effect_assignment_row_66(original_effect)
        original_exclusive = (
            (1025).to_bytes(2, "little") + bytes((200, 37)) +
            (1025).to_bytes(2, "little") + bytes((201, 38)) +
            bytes((202, 203, 204, 39, 205, 206, 207, 40)))
        memory = bytearray(0x13000)
        pointer_offset = 0x00500C3B - memory_base
        memory[pointer_offset:pointer_offset + 4] = effect_base.to_bytes(
            4, "little")
        effect_offset = effect_base - memory_base
        memory[effect_offset:effect_offset + 16] = encoded_effect
        exclusive_offset = exclusive_base - memory_base
        memory[exclusive_offset:exclusive_offset + 16] = original_exclusive
        backend = FakeMemoryBackend(memory_base, bytes(memory))
        session = MemorySession(
            1, 0x00400000, 0x200000, backend=backend,
            owns_handle=False, reference_image_base=0x00400000)
        workspace = Path(__file__).resolve().parents[3]
        self.window.engine_profile = detect_engine(str(
            workspace / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"),
            version_part=6)
        self.window.memory_session = session
        mywindow_module.addr_zhuanshu = exclusive_base
        mywindow_module.cnt_item = 160
        for control in (self.window.power_input_1_1,
                        self.window.power_input_1_2,
                        self.window.power_input_1_3,
                        self.window.power_input_1_4,
                        self.window.power_input_2_1,
                        self.window.power_input_2_4):
            control.addItems(str(index) for index in range(1025))
        for control in (self.window.power_input_1_9,
                        self.window.power_input_1_11):
            control.addItems(str(index) for index in range(81))
        for control in (self.window.power_input_2_2,
                        self.window.power_input_2_5,
                        self.window.power_input_3_1,
                        self.window.power_input_3_2,
                        self.window.power_input_3_3,
                        self.window.power_input_3_5,
                        self.window.power_input_3_6,
                        self.window.power_input_3_7):
            control.addItems(str(index) for index in range(161))
        effect_snapshot = self.window._read_effect_snapshot(0)
        self.window._show_effect_assignment_66(effect_snapshot.decoded)
        exclusive_snapshot = self.window._read_exclusive_snapshot(0)
        self.window._show_exclusive_assignment(exclusive_snapshot.raw)
        changed_effect = EffectAssignmentRow66(
            (EffectAssignmentSlot66(1025, 99),) +
            original_effect.characters[1:], original_effect.jobs)
        self.window.power_input_2_3.setText("41")
        changed_exclusive = self.window._collect_exclusive_assignment()
        backend.partial_write_at = exclusive_base
        with self.assertRaises(MemoryAccessError):
            self.window._commit_power_snapshots(
                0, encode_effect_assignment_row_66(changed_effect),
                changed_exclusive)
        self.assertEqual(encode_effect_assignment_row_66(changed_effect),
                         backend.read(effect_base, 16))
        self.assertEqual(changed_exclusive[:-1],
                         backend.read(exclusive_base, 15))
        self.assertEqual(original_exclusive[-1:],
                         backend.read(exclusive_base + 15, 1))

    def test_direct_66_profile_enables_write_controls(self):
        workspace = Path(__file__).resolve().parents[3]
        profile = detect_engine(str(
            workspace / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"),
            version_part=6)
        self.window.engine_profile = profile
        self.window.widget_1.setEnabled(True)
        self.window.widget_2.setEnabled(True)
        self.window._apply_capabilities()
        for control in (
                self.window.checkBox_1, self.window.checkBox_2,
                self.window.checkBox_3, self.window.checkBox_4,
                self.window.checkBox_5, self.window.checkBox_6,
                self.window.checkBox_7, self.window.checkBox_8,
                self.window.checkBox_9, self.window.data_save,
                self.window.data_recal, self.window.war_save,
                self.window.item_save, self.window.item_all_1,
                self.window.item_all_2, self.window.item_all_3,
                self.window.item_save_2, self.window.item_save_4):
            self.assertTrue(control.isEnabled(), control.objectName())

    def test_all_factions_patch_uses_complete_66_instructions(self):
        base = 0x00400000
        data = bytearray(b"\0" * 0x90000)
        data[0x242AA:0x242AC] = b"\x74\x0F"
        data[0x242AF:0x242B2] = b"\x03\x4D\xF8"
        data[0x37BC0:0x37BC2] = b"\x74\x34"
        data[0x54405:0x54407] = b"\x75\x27"
        data[0x55646:0x55648] = b"\x75\x23"
        backend = FakeMemoryBackend(base, bytes(data))
        session = MemorySession(base, base, len(data), backend=backend,
                                owns_handle=False,
                                reference_image_base=base)
        self.window.memory_session = session
        workspace = Path(__file__).resolve().parents[3]
        profile = detect_engine(str(
            workspace / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"),
            version_part=6)
        spec = profile.patches["control_all_factions"]
        session.set_patch(spec, True)
        self.assertEqual(b"\x90\x90", backend.read(0x004242AA, 2))
        self.assertEqual(b"\x03\x4D\xF8", backend.read(0x004242AF, 3))
        self.assertEqual(b"\x90\x90", backend.read(0x00437BC0, 2))
        self.assertEqual(b"\xEB\x23", backend.read(0x00455646, 2))
        session.set_patch(spec, False)
        self.assertEqual(b"\x74\x0F", backend.read(0x004242AA, 2))
        self.assertEqual(b"\x74\x34", backend.read(0x00437BC0, 2))
        session.close()
        self.window.memory_session = None

    def test_66_auto_return_button_uses_native_three_site_patch(self):
        base = 0x00400000
        data = bytearray(b"\0" * 0x90000)
        data[0x12D82:0x12D84] = b"\x74\x13"
        data[0x3D529:0x3D52B] = b"\x74\x02"
        data[0x3D540:0x3D542] = b"\x74\x02"
        data[0x4E575:0x4E57A] = b"\xE8\xD8\x47\xFC\xFF"
        data[0x4EA73:0x4EA7B] = b"\xFF\x75\xF0\xE8\xD7\x42\xFC\xFF"
        data[0x85E00:0x85E49] = b"\0" * 0x49
        backend = FakeMemoryBackend(base, bytes(data))
        session = MemorySession(base, base, len(data), backend=backend,
                                owns_handle=False,
                                reference_image_base=base)
        self.window.memory_session = session
        workspace = Path(__file__).resolve().parents[3]
        self.window.engine_profile = detect_engine(str(
            workspace / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"),
            version_part=6)
        self.window.engine_profile.set_feature_access(
            "control_auto_return", FeatureAccess.READ_WRITE,
            runtime_probe_passed=True)
        previous_version = mywindow_module.version
        self.addCleanup(setattr, mywindow_module, "version", previous_version)
        mywindow_module.version = 6
        self.window.checkBox_7.setChecked(True)
        self.window.onButtonClick(7)
        self.assertEqual(b"\x90\x90", backend.read(0x00412D82, 2))
        self.assertEqual(b"\x90\x90", backend.read(0x0043D529, 2))
        self.assertEqual(b"\x90\x90", backend.read(0x0043D540, 2))
        self.assertEqual(b"\xE8\xD8\x47\xFC\xFF",
                         backend.read(0x0044E575, 5))
        self.assertEqual(b"\xFF\x75\xF0\xE8\xD7\x42\xFC\xFF",
                         backend.read(0x0044EA73, 8))
        self.assertEqual(b"\0" * 0x49, backend.read(0x00485E00, 0x49))
        self.window.checkBox_7.setChecked(False)
        self.window.onButtonClick(7)
        self.assertEqual(b"\x74\x13", backend.read(0x00412D82, 2))
        self.assertEqual(b"\x74\x02", backend.read(0x0043D529, 2))
        self.assertEqual(b"\x74\x02", backend.read(0x0043D540, 2))
        session.close()
        self.window.memory_session = None

    def test_66_auto_return_partial_patch_is_not_claimed(self):
        base = 0x00400000
        data = bytearray(b"\0" * 0x90000)
        data[0x12D82:0x12D84] = b"\x90\x90"
        data[0x3D529:0x3D52B] = b"\x74\x02"
        data[0x3D540:0x3D542] = b"\x74\x02"
        backend = FakeMemoryBackend(base, bytes(data))
        session = MemorySession(base, base, len(data), backend=backend,
                                owns_handle=False,
                                reference_image_base=base)
        workspace = Path(__file__).resolve().parents[3]
        self.window.engine_profile = detect_engine(str(
            workspace / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"),
            version_part=6)
        self.window.memory_session = session
        self.window._sync_66_patch_controls()
        self.assertFalse(self.window.checkBox_7.isChecked())
        self.assertIn("部分补丁站点", self.window.checkBox_7.toolTip())
        self.assertIn("部分补丁站点", self.window.label_window2.toolTip())
        session.close()
        self.window.memory_session = None

    def test_66_boolean_variables_use_msb_first_bitset(self):
        base = 0x00400000
        data = bytearray(b"\0" * 0x143000)
        offset = 0x00492FC8 - base
        data[offset] = 0xA1
        data[offset+1] = 0x80
        data[offset+33] = 0x80
        data[offset+58] = 0x30
        backend = FakeMemoryBackend(base, bytes(data))
        session = MemorySession(base, base, len(data), backend=backend,
                                owns_handle=False,
                                reference_image_base=base)
        workspace = Path(__file__).resolve().parents[3]
        self.window.engine_profile = detect_engine(str(
            workspace / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"),
            version_part=6)
        self.window.memory_session = session
        self.window.md = session
        self.window.p = session.handle
        previous_version = mywindow_module.version
        self.addCleanup(setattr, mywindow_module, "version", previous_version)
        mywindow_module.version = 6
        self.assertEqual(0x00492FC8,
                          self.window._variable_address("variables_bool", 0))
        self.assertEqual(0x00492FC8,
                         self.window._variable_address("variables_bool", 7))
        self.assertEqual(0x00492FC9,
                         self.window._variable_address("variables_bool", 8))
        self.assertEqual(0x00492FCC,
                         self.window._variable_address("variables_bool", 32))
        self.assertEqual(0x004931C7,
                         self.window._variable_address("variables_bool", 4095))
        layout = self.window._variable_layout("variables_bool")
        self.assertEqual(0x80, self.window._variable_bit_mask(layout, 264))
        self.assertEqual(0x20, self.window._variable_bit_mask(layout, 466))
        self.assertEqual(0x10, self.window._variable_bit_mask(layout, 467))
        self.assertEqual(0x01, self.window._variable_bit_mask(layout, 4095))
        values = self.window._variable_values("variables_bool", 0)
        self.assertEqual(1, values[0])
        self.assertEqual(0, values[1])
        self.assertEqual(1, values[2])
        self.assertEqual(1, values[7])
        self.assertEqual(1, values[8])
        values = self.window._variable_values("variables_bool", 250)
        self.assertEqual(1, values[264 - 250])
        values = self.window._variable_values("variables_bool", 400)
        self.assertEqual(1, values[466 - 400])
        self.assertEqual(1, values[467 - 400])
        self.window.onVar()
        self.assertEqual("492FC8",
                          self.window.listview_var_1.item(0, 2).text())
        self.assertEqual("True",
                         self.window.listview_var_1.item(0, 1).text())
        self.assertEqual("False",
                         self.window.listview_var_1.item(1, 1).text())
        self.window.listview_var_1.item(1, 1).setText("True")
        self.window.listview_var_1.setCurrentCell(1, 1)
        self.window.engine_profile.set_feature_access(
            "variables_bool", FeatureAccess.READ_WRITE,
            runtime_probe_passed=True)
        self.window.saveVar(1)
        self.assertEqual(0xE1, session.read_u8(0x00492FC8))
        self.assertEqual(0x80, session.read_u8(0x00492FC9))

    def _prepare_66_item_profile(self):
        workspace = Path(__file__).resolve().parents[3]
        self.window.engine_profile = detect_engine(str(
            workspace / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"),
            version_part=6)
        previous_version = mywindow_module.version
        self.addCleanup(setattr, mywindow_module, "version", previous_version)
        mywindow_module.version = 6

    def test_66_consumable_range_and_single_byte_round_trip(self):
        self._prepare_66_item_profile()
        base = 0x00400000
        data = bytearray(b"\0" * 0x143000)
        backend = FakeMemoryBackend(base, bytes(data))
        session = MemorySession(base, base, len(data), backend=backend,
                                owns_handle=False,
                                reference_image_base=base)
        self.window.memory_session = session
        self.assertEqual(list(range(160, 200)),
                         self.window._consumable_ids())
        self.assertEqual(0x00493AF0,
                         self.window._consumable_address(160))
        self.assertEqual(0x00493B17,
                         self.window._consumable_address(199))
        with self.assertRaises(MemoryAccessError):
            self.window._consumable_address(159)
        with self.assertRaises(MemoryAccessError):
            self.window._consumable_address(200)
        session.write_u8(0x00493AF0, 2)
        self.assertEqual(2, self.window._read_consumable(160))
        entry = self.window._consumable_entry(199, 255)
        session.write_transaction((entry,))
        self.assertEqual(255, session.read_u8(0x00493B17))

    def test_66_bulk_consumable_transactions_touch_exactly_40_items(self):
        self._prepare_66_item_profile()
        self.window.engine_profile.set_feature_access(
            "item_consumables", FeatureAccess.READ_WRITE,
            runtime_probe_passed=True)
        self.window.engine_profile.set_feature_access(
            "warehouse", FeatureAccess.READ_WRITE,
            runtime_probe_passed=True)
        transactions = []
        base = 0x00400000
        data = bytearray(b"\0" * 0x143000)
        backend = FakeMemoryBackend(base, bytes(data))
        session = MemorySession(base, base, len(data), backend=backend,
                                owns_handle=False,
                                reference_image_base=base)
        original_write_transaction = session.write_transaction

        def record_transaction(entries):
            entries = tuple(entries)
            transactions.append(entries)
            original_write_transaction(entries)

        session.write_transaction = record_transaction
        self.window.memory_session = session
        self.window.onItem = lambda: None
        self.window.allItem(2)
        self.assertEqual(40, len(transactions[0]))
        self.assertEqual(list(range(0x00493AF0, 0x00493B18)),
                         [address for address, _value in transactions[0]])
        self.assertTrue(all(value == b"\xFF"
                            for _address, value in transactions[0]))

        self.window.allItem(3)
        self.assertEqual(240, len(transactions[1]))
        consumables = transactions[1][200:]
        self.assertEqual(list(range(0x00493AF0, 0x00493B18)),
                         [address for address, _value in consumables])
        self.assertTrue(all(value == b"\0"
                            for _address, value in consumables))

    def test_consumable_only_save_does_not_require_warehouse_selection(self):
        self._prepare_66_item_profile()
        self.window.engine_profile.set_feature_access(
            "item_consumables", FeatureAccess.READ_WRITE,
            runtime_probe_passed=True)
        self.window.engine_profile.set_feature_access(
            "warehouse", FeatureAccess.READ_WRITE,
            runtime_probe_passed=True)
        base = 0x00400000
        data = bytearray(b"\0" * 0x143000)
        backend = FakeMemoryBackend(base, bytes(data))
        session = MemorySession(base, base, len(data), backend=backend,
                                owns_handle=False,
                                reference_image_base=base)
        self.window.memory_session = session
        self.window.md = session
        self.window.p = session.handle
        self.window.listview_item.clear()
        for _index in range(200):
            self.window.listview_item.addItem("空")
        self.window.listview_item.clearSelection()
        self.window.listview_item.setCurrentRow(-1)
        self.window.listview_item_2.clear()
        for item_id in range(160, 200):
            self.window.listview_item_2.addItem(str(item_id))
        self.window.listview_item_2.setCurrentRow(0)
        address = 0x00493AF0
        session.write_u8(address, 2)
        self.window._consumable_snapshots = {address: b"\x02"}
        self.window._item_global_snapshot = {
            "money": b"\0" * 4, "alignment": b"\0", "merit": b"\0" * 4,
        }
        self.window.item_input_4.setText("9")
        self.window.item_input_5.setText("0")
        self.window.item_input_6.setText("0")
        self.window.item_input_7.setText("0")
        self.window.onItem = lambda: None
        self.window.saveItem()
        self.assertEqual(9, session.read_u8(address))

    def test_66_warehouse_preserves_opaque_high_item_when_saving_money(self):
        self._prepare_66_item_profile()
        self.window.engine_profile.set_feature_access(
            "warehouse", FeatureAccess.READ_WRITE,
            runtime_probe_passed=True)
        previous_count = mywindow_module.cnt_item
        self.addCleanup(setattr, mywindow_module, "cnt_item", previous_count)
        mywindow_module.cnt_item = 160
        base = 0x00400000
        data = bytearray(b"\0" * 0x143000)
        warehouse = 0x004B0783
        warehouse_offset = warehouse - base
        data[warehouse_offset:warehouse_offset + 600] = b"\xFF" * 600
        data[warehouse_offset:warehouse_offset + 3] = bytes((200, 3, 4))
        backend = FakeMemoryBackend(base, bytes(data))
        session = MemorySession(base, base, len(data), backend=backend,
                                owns_handle=False,
                                reference_image_base=base)
        self.window.memory_session = session
        self.window.md = session
        self.window.p = session.handle
        self.window.item_input_1.clear()
        self.window.item_input_1.addItems(
            str(index) for index in range(161))
        self.window.listview_item.clear()
        for _index in range(200):
            self.window.listview_item.addItem("空")
        self.window.listview_item.setCurrentRow(0)
        self.window.listview_item_2.clearSelection()
        self.window.listview_item_2.setCurrentRow(-1)
        self.window.onItem()
        self.assertEqual(160, self.window.item_input_1.currentIndex())
        self.assertEqual("无效编号:200",
                         self.window.listview_item.item(0).text())
        self.window.item_input_5.setText("1")
        self.window.onItem = lambda: None
        self.window.saveItem()
        self.assertEqual(bytes((200, 3, 4)),
                         session.read_bytes(warehouse, 3))
        self.assertEqual(1, session.read_u32(0x004B077C))

    def test_war_save_keeps_original_helper_call_surface(self):
        source = inspect.getsource(mywindow_module.Ui_MainWindow.saveWar)
        self.assertNotIn("changePos", source)
        self.assertNotIn("changeWeather", source)
        self.assertIn("self.onTurn()", source)
        self.assertIn("self.onWar(1)", source)

    def test_person_save_does_not_implicitly_recalculate(self):
        source = inspect.getsource(mywindow_module.Ui_MainWindow.saveData)
        self.assertNotIn("self.recal(", source)
        self.assertNotIn("hk.recal(", source)
        self.assertNotIn("my_hook(", source)

    def test_item_save_keeps_original_refresh_order(self):
        source = inspect.getsource(mywindow_module.Ui_MainWindow.saveItem)
        self.assertTrue(source.rstrip().endswith("self.onItem()"))
        self.assertEqual(1, source.count("self.onItem()"))

    def test_power_save_keeps_zero_value_member_clearing(self):
        collector_source = inspect.getsource(
            mywindow_module.Ui_MainWindow._collect_exclusive_assignment)
        self.assertEqual(2, collector_source.count(
            'replacement[offset:offset + 4] = b"\\0" * 4'))
        save_source = inspect.getsource(mywindow_module.Ui_MainWindow.savePower)
        self.assertTrue(save_source.rstrip().endswith("self.onPower()"))

    def test_fatal_save_does_not_add_refresh_helper(self):
        source = inspect.getsource(mywindow_module.Ui_MainWindow.saveMk)
        self.assertNotIn("self.onMk(", source)
        self.assertNotIn("my_hook(", source)

    def test_clear_item_keeps_warehouse_boundary_at_200_slots(self):
        source = inspect.getsource(mywindow_module.Ui_MainWindow.allItem)
        self.assertIn("for slot in range(0, 200)", source)
        self.assertIn("for i in range(0,200)", source)
        self.assertNotIn("range(0,256)", source)
        self.assertNotIn("range(0, 256)", source)


if __name__ == "__main__":
    unittest.main()
