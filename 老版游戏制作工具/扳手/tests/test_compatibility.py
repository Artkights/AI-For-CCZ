import os
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import compatibility


class CompatibilityTests(unittest.TestCase):
    def test_migration_is_one_time_and_does_not_overwrite_user_file(self):
        with tempfile.TemporaryDirectory() as root:
            source = Path(root) / "install"
            target = Path(root) / "profile"
            source.mkdir()
            target.mkdir()
            (source / "DIY.csv").write_text("legacy", encoding="utf-8")
            self.assertEqual([target / "DIY.csv"],
                             compatibility.migrate_user_files(source, target))
            (source / "DIY.csv").write_text("newer-install-copy", encoding="utf-8")
            self.assertEqual([], compatibility.migrate_user_files(source, target))
            self.assertEqual("legacy", (target / "DIY.csv").read_text(encoding="utf-8"))

    def test_candidates_keep_legacy_location_after_migration(self):
        with tempfile.TemporaryDirectory() as root:
            base = Path(root) / "profile"
            legacy = Path(root) / "install"
            resource = Path(root) / "resources"
            candidates = compatibility.data_file_candidates(base, legacy, resource)
            self.assertEqual([base / "DIY.csv", legacy / "DIY.csv", resource / "DIY.csv"],
                             candidates)

    def test_win7_without_sp1_is_warning_only(self):
        version = type("Version", (), {
            "major": 6, "minor": 1, "build": 7600, "service_pack": ""
        })()
        with patch.object(compatibility.os, "name", "nt"), \
                patch.object(compatibility.sys, "getwindowsversion", return_value=version):
            report = compatibility.probe_runtime()
        self.assertTrue(report.supported)
        self.assertTrue(any("SP1" in warning for warning in report.warnings))

    def test_pre_win7_is_rejected(self):
        version = type("Version", (), {
            "major": 6, "minor": 0, "build": 6000, "service_pack": "2"
        })()
        with patch.object(compatibility.os, "name", "nt"), \
                patch.object(compatibility.sys, "getwindowsversion", return_value=version):
            report = compatibility.probe_runtime()
        self.assertFalse(report.supported)
        self.assertIn("Windows 7", report.errors[0])

    def test_public_report_aliases_preserve_structured_result(self):
        report = compatibility.collect_compatibility_report()
        self.assertIsInstance(report, compatibility.CompatibilityReport)
        self.assertIn(report.message(),
                      compatibility.format_compatibility_errors(report))

    def test_non_windows_probe_is_nonfatal_for_offline_tests(self):
        with patch.object(compatibility.os, "name", "posix"):
            report = compatibility.probe_runtime()
        self.assertTrue(report.supported)
        self.assertTrue(any("离线测试" in warning for warning in report.warnings))

    def test_duplicate_data_candidates_are_removed_case_insensitively(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)
            candidates = compatibility.data_file_candidates(
                path, path, path, name="DIY.csv")
        self.assertEqual([path / "DIY.csv"], candidates)

    def test_64_bit_interpreter_is_reported_for_x86_game_users(self):
        version = type("Version", (), {
            "major": 6, "minor": 1, "build": 7601,
            "service_pack": "Service Pack 1"
        })()
        with patch.object(compatibility.os, "name", "nt"), \
                patch.object(compatibility.sys, "getwindowsversion", return_value=version), \
                patch.object(compatibility.struct, "calcsize", return_value=8):
            report = compatibility.collect_compatibility_report()
        self.assertEqual(report.process_bits, 64)
        self.assertTrue(any("32 位扳手" in warning for warning in report.warnings))

    def test_python_newer_than_38_is_warning_on_win7(self):
        version = type("Version", (), {
            "major": 6, "minor": 1, "build": 7601,
            "service_pack": "Service Pack 1"
        })()
        with patch.object(compatibility.os, "name", "nt"), \
                patch.object(compatibility.sys, "getwindowsversion", return_value=version), \
                patch.object(compatibility.sys, "version_info", (3, 10, 19)):
            report = compatibility.collect_compatibility_report()
        self.assertTrue(any("Python 3.8" in warning for warning in report.warnings))
        self.assertIn("3.10.19", report.python_version)

    def test_user_data_uses_local_profile_before_roaming_profile(self):
        with tempfile.TemporaryDirectory() as root:
            local = Path(root) / "本地配置"
            roaming = Path(root) / "漫游配置"
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local),
                                         "APPDATA": str(roaming)}, clear=True):
                actual = compatibility.user_data_dir(Path(root) / "Program Files")
            self.assertEqual(local / "CaoCaoZhuan" / "Banshou", actual)

    def test_user_data_can_fallback_to_roaming_profile(self):
        with tempfile.TemporaryDirectory() as root:
            roaming = Path(root) / "漫游配置"
            with patch.dict(os.environ, {"APPDATA": str(roaming)}, clear=True):
                actual = compatibility.user_data_dir(Path(root) / "install")
            self.assertEqual(roaming / "CaoCaoZhuan" / "Banshou", actual)

    def test_user_data_without_profile_variables_uses_user_home(self):
        with tempfile.TemporaryDirectory() as root:
            install = Path(root) / "install"
            home = Path(root) / "home"
            with patch.dict(os.environ, {}, clear=True), \
                    patch.object(compatibility.Path, "home", return_value=home):
                actual = compatibility.user_data_dir(install)
            self.assertEqual(home / "AppData" / "Local" / "CaoCaoZhuan" / "Banshou", actual)

    def test_user_data_with_unknown_home_uses_temporary_user_directory(self):
        with tempfile.TemporaryDirectory() as root:
            install = Path(root) / "Program Files"
            fallback = Path(root) / "temp"
            with patch.dict(os.environ, {}, clear=True), \
                    patch.object(compatibility.Path, "home", side_effect=RuntimeError), \
                    patch.object(compatibility.tempfile, "gettempdir", return_value=str(fallback)):
                actual = compatibility.user_data_dir(install)
            self.assertEqual(fallback / "CaoCaoZhuan" / "Banshou", actual)

    def test_frozen_win7_rtm_is_rejected_in_strict_mode(self):
        version = type("Version", (), {
            "major": 6, "minor": 1, "build": 7600,
            "service_pack": "", "service_pack_major": 0
        })()
        with patch.object(compatibility.os, "name", "nt"), \
                patch.object(compatibility.sys, "getwindowsversion", return_value=version), \
                patch.object(compatibility.sys, "version_info", (3, 8, 10)), \
                patch.object(compatibility.struct, "calcsize", return_value=4):
            report = compatibility.collect_compatibility_report(strict=True)
        self.assertFalse(report.supported)
        self.assertTrue(any("SP1" in error for error in report.errors))

    def test_frozen_release_rejects_64_bit_interpreter(self):
        version = type("Version", (), {
            "major": 10, "minor": 0, "build": 19045,
            "service_pack": "", "service_pack_major": 0
        })()
        with patch.object(compatibility.os, "name", "nt"), \
                patch.object(compatibility.sys, "getwindowsversion", return_value=version), \
                patch.object(compatibility.sys, "version_info", (3, 8, 10)), \
                patch.object(compatibility.struct, "calcsize", return_value=8):
            report = compatibility.collect_compatibility_report(strict=True)
        self.assertFalse(report.supported)
        self.assertTrue(any("32 位扳手" in error for error in report.errors))

    def test_frozen_release_rejects_python_other_than_3810(self):
        version = type("Version", (), {
            "major": 10, "minor": 0, "build": 19045,
            "service_pack": "", "service_pack_major": 0
        })()
        with patch.object(compatibility.os, "name", "nt"), \
                patch.object(compatibility.sys, "getwindowsversion", return_value=version), \
                patch.object(compatibility.sys, "version_info", (3, 10, 19)), \
                patch.object(compatibility.struct, "calcsize", return_value=4):
            report = compatibility.collect_compatibility_report(strict=True)
        self.assertFalse(report.supported)
        self.assertTrue(any("3.8.10" in error for error in report.errors))

    def test_dependency_import_failure_is_in_structured_report(self):
        report = compatibility.CompatibilityReport("win32", "6.1.7601", "3.8.10", 32)
        with patch.object(compatibility.importlib, "import_module",
                          side_effect=ImportError("missing test dependency")):
            compatibility.check_dependencies(report)
        self.assertFalse(report.supported)
        self.assertFalse(report.details["dependencies"]["_ctypes"]["loaded"])
        self.assertTrue(any("VC++ x86/UCRT" in error for error in report.errors))

    def test_write_report_replaces_previous_json_and_preserves_unicode(self):
        with tempfile.TemporaryDirectory() as root:
            destination = Path(root) / "诊断" / "startup.json"
            compatibility.write_report(destination, {"state": "旧值"})
            compatibility.write_report(destination, {"state": "已更新"})
            self.assertIn("已更新", destination.read_text(encoding="utf-8"))
            self.assertNotIn("旧值", destination.read_text(encoding="utf-8"))
            self.assertEqual([], list(destination.parent.glob("*.tmp")))

    def test_migration_copy_failure_removes_partial_destination_and_keeps_source(self):
        with tempfile.TemporaryDirectory() as root:
            source = Path(root) / "install"
            target = Path(root) / "profile"
            source.mkdir()
            (source / "DIY.csv").write_text("legacy", encoding="utf-8")
            with patch.object(compatibility.shutil, "copyfileobj",
                              side_effect=OSError("simulated interrupted copy")):
                self.assertEqual([], compatibility.migrate_user_files(source, target))
            self.assertFalse((target / "DIY.csv").exists())
            self.assertEqual("legacy", (source / "DIY.csv").read_text(encoding="utf-8"))

    def test_migration_unwritable_destination_is_nonfatal(self):
        with tempfile.TemporaryDirectory() as root:
            source = Path(root) / "install"
            source.mkdir()
            (source / "DIY.csv").write_text("legacy", encoding="utf-8")
            with patch.object(compatibility.Path, "mkdir",
                              side_effect=PermissionError("simulated denied")):
                copied = compatibility.migrate_user_files(source, Path(root) / "profile")
            self.assertEqual([], copied)
            self.assertEqual("legacy", (source / "DIY.csv").read_text(encoding="utf-8"))

    def test_qt_plugin_environment_preserves_chinese_package_path(self):
        with tempfile.TemporaryDirectory() as root:
            package = Path(root) / "中文安装目录" / "PyQt5"
            plugins = package / "Qt5" / "plugins"
            (plugins / "platforms").mkdir(parents=True)
            (plugins / "platforms" / "qwindows.dll").write_bytes(b"test plugin")
            spec = SimpleNamespace(submodule_search_locations=[str(package)])
            with patch.object(compatibility.importlib.util, "find_spec", return_value=spec), \
                    patch.dict(os.environ, {"QT_QPA_PLATFORM_PLUGIN_PATH": "C:/????/plugins"}):
                actual = compatibility.configure_qt_environment()
                self.assertEqual(plugins, actual)
                self.assertEqual(str(plugins), os.environ["QT_QPA_PLATFORM_PLUGIN_PATH"])
                self.assertIn("中文安装目录", os.environ["QT_QPA_PLATFORM_PLUGIN_PATH"])
                self.assertNotIn("?", os.environ["QT_QPA_PLATFORM_PLUGIN_PATH"])

    def test_qt_plugin_environment_keeps_existing_setting_if_package_is_missing(self):
        with patch.object(compatibility.importlib.util, "find_spec", return_value=None), \
                patch.dict(os.environ, {"QT_QPA_PLATFORM_PLUGIN_PATH": "C:/custom/plugins"}):
            self.assertIsNone(compatibility.configure_qt_environment())
            self.assertEqual("C:/custom/plugins", os.environ["QT_QPA_PLATFORM_PLUGIN_PATH"])


if __name__ == "__main__":
    unittest.main()
