"""Release gates that catch unusable or unverifiable Windows bundles."""

import hashlib
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build_release
from test_verify_release import carchive_fixture, pe_header


def import_pe(function="ExitProcess", delayed=None):
    """Small real PE32 file with one static import and an optional delay import."""
    data = bytearray(pe_header() + bytes(0x600))
    struct.pack_into("<H", data, 0x86, 1)  # one section
    struct.pack_into("<I", data, 0x98 + 28, 0x400000)
    struct.pack_into("<II", data, 0x98 + 32, 0x1000, 0x200)
    struct.pack_into("<I", data, 0x98 + 56, 0x2000)
    struct.pack_into("<I", data, 0x98 + 60, 0x200)
    struct.pack_into("<I", data, 0x98 + 92, 16)
    struct.pack_into("<II", data, 0x98 + 96 + 8, 0x1000, 40)
    data[0x178:0x180] = b".rdata\0\0"
    struct.pack_into("<IIII", data, 0x180, 0x600, 0x1000, 0x600, 0x200)
    struct.pack_into("<I", data, 0x178 + 36, 0x40000040)
    struct.pack_into("<IIIII", data, 0x200, 0x1040, 0, 0, 0x1060, 0x1050)
    struct.pack_into("<I", data, 0x240, 0x1080)
    struct.pack_into("<I", data, 0x250, 0x1080)
    data[0x260:0x26d] = b"KERNEL32.dll\0"
    encoded = function.encode("ascii") + b"\0"
    data[0x282:0x282 + len(encoded)] = encoded
    if delayed:
        struct.pack_into("<II", data, 0x98 + 96 + 13 * 8, 0x1100, 64)
        struct.pack_into("<IIIIIIII", data, 0x300, 1, 0x1150, 0, 0x1170, 0x1160, 0, 0, 0)
        data[0x350:0x35d] = b"KERNEL32.dll\0"
        struct.pack_into("<I", data, 0x360, 0x1180)
        struct.pack_into("<I", data, 0x370, 0x1180)
        encoded = delayed.encode("ascii") + b"\0"
        data[0x382:0x382 + len(encoded)] = encoded
    return bytes(data)


class ReleaseBuildTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def wheel(self):
        path = self.root / "example-1.0-py3-none-any.whl"
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("example-1.0.dist-info/METADATA",
                             "Name: example\nVersion: 1.0\n")
        lock = self.root / "requirements.txt"
        lock.write_text("example==1.0 --hash=sha256:%s\n" %
                        hashlib.sha256(path.read_bytes()).hexdigest(), encoding="utf-8")
        return path, lock

    def test_wheel_hash_tamper_rejected_before_manifest(self):
        wheel, lock = self.wheel()
        with mock.patch.object(build_release, "LOCK", lock):
            report = build_release.wheel_manifest(self.root)
            self.assertEqual(1, len(report["packages"]))
            with zipfile.ZipFile(wheel, "a") as archive:
                archive.writestr("payload.txt", "changed")
            with self.assertRaisesRegex(SystemExit, "hash mismatch"):
                build_release.wheel_manifest(self.root)

    def test_lock_rejects_missing_hash_and_duplicate_pin(self):
        lock = self.root / "requirements.txt"
        with mock.patch.object(build_release, "LOCK", lock):
            lock.write_text("example==1.0\n", encoding="utf-8")
            with self.assertRaisesRegex(SystemExit, "exact version and SHA256"):
                build_release.locked_packages()
            line = "example==1.0 --hash=sha256:%s\n" % ("a" * 64)
            lock.write_text(line + line, encoding="utf-8")
            with self.assertRaisesRegex(SystemExit, "duplicate"):
                build_release.locked_packages()

    def test_native_static_windows8_import_rejected_with_evidence(self):
        output = self.root / "audit.json"
        with self.assertRaisesRegex(SystemExit, "post-Win7 static APIs"):
            build_release.native_import_audit({"tool.exe": import_pe("GetSystemTimePreciseAsFileTime")}, output)
        report = json.loads(output.read_text())
        self.assertFalse(report["passed"])
        self.assertEqual(["GetSystemTimePreciseAsFileTime"], report["failures"][0]["functions"])
        self.assertEqual("tool.exe", report["files"][0]["path"])

    def test_delay_import_is_recorded_separately_without_static_failure(self):
        output = self.root / "audit.json"
        report = build_release.native_import_audit(
            {"tool.exe": import_pe(delayed="GetSystemTimePreciseAsFileTime")}, output)
        self.assertTrue(report["passed"])
        file_report = report["files"][0]
        self.assertEqual(["ExitProcess"], file_report["imports"][0]["functions"])
        self.assertEqual(["GetSystemTimePreciseAsFileTime"], file_report["delayImports"][0]["functions"])
        self.assertTrue(report["limitations"])

    def test_real_bundle_archive_missing_qwindows_is_rejected(self):
        try:
            import PyInstaller.archive.readers
        except ImportError:
            self.skipTest("PyInstaller not installed")
        exe = self.root / "tool.exe"
        entries = [(name, b"test", True, "b") for name in build_release.REQUIRED_RUNTIME
                   if name != "qwindows.dll"]
        exe.write_bytes(pe_header() + carchive_fixture(entries))
        with self.assertRaisesRegex(SystemExit, "qwindows.dll"):
            build_release.archive_files(exe, "onefile")
        entries.append(("PyQt5/Qt5/plugins/platforms/qwindows.dll", b"test", True, "b"))
        exe.write_bytes(pe_header() + carchive_fixture(entries))
        binaries = build_release.archive_files(exe, "onefile")
        self.assertIn("tool.exe", binaries, "the root executable must also be import-audited")

    def test_release_version_gate_requires_exact_x86_cpython_3810(self):
        with mock.patch.object(build_release.sys, "platform", "win32"), \
                mock.patch.object(build_release.platform, "python_implementation", return_value="CPython"), \
                mock.patch.object(build_release.struct, "calcsize", return_value=4), \
                mock.patch.object(build_release.sys, "version_info", (3, 8, 10)):
            build_release.assert_release_python()
            with mock.patch.object(build_release.sys, "version_info", (3, 8, 9)):
                with self.assertRaisesRegex(SystemExit, "3.8.10 x86"):
                    build_release.assert_release_python()
            with mock.patch.object(build_release.struct, "calcsize", return_value=8):
                with self.assertRaisesRegex(SystemExit, "3.8.10 x86"):
                    build_release.assert_release_python()

    def test_system_matrix_never_invents_completed_platform_tests(self):
        matrix = build_release.compatibility_matrix()
        self.assertEqual("pending", matrix["status"])
        for item in matrix["systems"]:
            self.assertEqual("pending", item["status"])
            self.assertIsNone(item["evidence"])
            self.assertEqual({"pending"}, set(item["checks"].values()))


if __name__ == "__main__":
    unittest.main()
