"""Exercise the expanded ID UI using synthetic memory, without a game."""

import os
from pathlib import Path
import tempfile
import unittest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from engine_profile import ConsumableMappingSpec, legacy_profile
from engine_layout import FeatureAccess
from process_memory import FakeMemoryBackend, MemoryAccessError, MemorySession

try:
    from PyQt5.QtWidgets import QApplication
    from main import MyDesiger
except ImportError:
    QApplication = None


@unittest.skipIf(QApplication is None, "PyQt5 is not installed")
class SingleExeUiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])

    def test_expanded_names_and_endpoint_count_addresses(self):
        window = MyDesiger()
        self.addCleanup(window.close)
        profile = legacy_profile(5)
        profile.limits["item_record_count"] = 512
        spec = ConsumableMappingSpec(
            "synthetic-extended", 0x750000, 256, 256,
            tuple(range(256, 512)), 160)
        profile.metadata["consumable_mapping"] = spec.as_mapping()
        profile.metadata["consumable_item_ids"] = list(spec.ui_item_ids)
        profile.addresses["consumable_counts"] = spec.base
        profile.set_feature_access("item_consumables", FeatureAccess.READ_WRITE)
        backend = FakeMemoryBackend(0x400000, bytes(0x400000))
        session = MemorySession(
            1, 0x400000, 0x400000, backend=backend, owns_handle=False)
        window.engine_profile = profile
        window.memory_session = session
        self.addCleanup(session.close)

        # Names are absent in a single-file static fixture.  The UI must not
        # index the 255-row equipment catalog with a 16-bit consumable ID.
        self.assertEqual("道具256", window._consumable_name(256))
        self.assertEqual("道具511", window._consumable_name(511))
        names = [""] * 512
        names[511] = "扩展道具"
        profile.metadata["runtime_item_names"] = tuple(names)
        self.assertEqual("扩展道具", window._consumable_name(511))
        self.assertEqual(list(range(256, 512)), window._consumable_ids())
        self.assertEqual(0x750000, window._consumable_address(256))
        self.assertEqual(0x7500FF, window._consumable_address(511))
        for item_id in (255, 512):
            with self.assertRaises(MemoryAccessError):
                window._consumable_address(item_id)
        for item_id, value in ((256, 17), (511, 255)):
            address, raw = window._consumable_entry(item_id, value)
            session.write_bytes(address, raw)
            self.assertEqual(value, window._read_consumable(item_id))
        self.assertEqual(0, session.read_u8(0x74FFFF))
        self.assertEqual(0, session.read_u8(0x750100))

    def test_missing_fatal_labels_do_not_disable_resolved_assignments(self):
        window = MyDesiger()
        self.addCleanup(window.close)
        window.engine_profile = legacy_profile(5)
        window.engine_profile.set_feature_access("fatal", FeatureAccess.READ_WRITE)
        window.memory_session = object()
        with tempfile.TemporaryDirectory() as directory:
            exe_path = Path(directory) / "Ekd5.exe"
            window._populate_profile_fatal_names_66(exe_path)
            self.assertEqual(80, window.listview_mk.count())
            self.assertEqual("79:必杀79", window.listview_mk.item(79).text())
            self.assertTrue(window.item_save_4.isEnabled())
            self.assertIn("fatal_name_error", window.engine_profile.metadata)

            # A truncated file also leaves all valid record IDs selectable.
            data_file = Path(directory) / "Data.e5"
            data_file.write_bytes(bytes(59540 + 16))
            window._populate_profile_fatal_names_66(exe_path)
            self.assertEqual(80, window.listview_mk.count())
            self.assertTrue(window.item_save_4.isEnabled())

            rows = bytearray(80 * 16)
            rows[79*16:79*16+4] = "测试".encode("gbk")
            data_file.write_bytes(bytes(59540) + rows)
            window._populate_profile_fatal_names_66(exe_path)
            self.assertEqual("79:测试", window.listview_mk.item(79).text())
            self.assertNotIn("fatal_name_error", window.engine_profile.metadata)
            self.assertEqual("", window.listview_mk.toolTip())
            window.memory_session = None
            window._populate_profile_fatal_names_66(exe_path)
            self.assertFalse(window.item_save_4.isEnabled())


if __name__ == "__main__":
    unittest.main()
