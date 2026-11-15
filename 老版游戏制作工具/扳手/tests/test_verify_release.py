import contextlib
import hashlib
import importlib.util
import io
import json
import marshal
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
from types import CodeType
import unittest
import zlib

import verify_release


def pe_header(machine=0x14C, magic=0x10B):
    data = bytearray(0x200)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 0x80)
    data[0x80:0x84] = b"PE\0\0"
    struct.pack_into("<H", data, 0x84, machine)
    struct.pack_into("<H", data, 0x94, 0xE0)
    struct.pack_into("<H", data, 0x98, magic)
    return bytes(data)


def pyz_fixture(module, code):
    payload = zlib.compress(marshal.dumps(code))
    toc_offset = 17 + len(payload)
    header = b"PYZ\0" + importlib.util.MAGIC_NUMBER + struct.pack("!i", toc_offset) + bytes(5)
    return header + payload + marshal.dumps([(module, (0, 17, len(payload)))])


def carchive_fixture(entries):
    """Produce real reader-format fixtures without importing build dependencies."""
    payload = bytearray()
    toc = bytearray()
    for name, raw, compress, typecode in entries:
        encoded = zlib.compress(raw) if compress else raw
        offset = len(payload)
        payload.extend(encoded)
        name_bytes = name.encode("utf-8")
        entry_length = (18 + len(name_bytes) + 1 + 15) & ~15
        toc.extend(struct.pack("!iIIIBB", entry_length, offset, len(encoded),
                               len(raw), int(compress), ord(typecode)))
        toc.extend(name_bytes + bytes(entry_length - 18 - len(name_bytes)))
    cookie_length = struct.calcsize("!8sIIii64s")
    cookie = struct.pack("!8sIIii64s", b"MEI\x0c\x0b\n\x0b\x0e",
                         len(payload) + len(toc) + cookie_length,
                         len(payload), len(toc),
                         sys.version_info[0] * 100 + sys.version_info[1],
                         b"python.dll")
    return bytes(payload + toc) + cookie


class ReleaseVerificationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def build_archive(self, main_source="value = 1\n", worker_source=None):
        if worker_source is None:
            worker_source = "def calculate(value):\n    return [x + value for x in range(3)]\n"
        main = self.root / "main.py"
        worker = self.root / "worker.py"
        main.write_text(main_source, encoding="utf-8")
        worker.write_text(worker_source, encoding="utf-8")
        pyz = pyz_fixture("worker", compile(worker.read_bytes(), "worker.py", "exec",
                                             dont_inherit=True, optimize=0))
        main_code = compile(main.read_bytes(), "main.py", "exec", dont_inherit=True, optimize=0)
        package = carchive_fixture([
            ("main", marshal.dumps(main_code), True, "s"),
            ("PYZ-00.pyz", pyz, False, "z"),
        ])
        executable = self.root / "release.exe"
        executable.write_bytes(pe_header() + package)
        return executable

    def verify_fixture(self, executable, modules=("main", "worker")):
        return verify_release.verify_release(executable, self.root, modules)

    def test_real_carchive_and_pyz_match_current_source(self):
        executable = self.build_archive()
        report = self.verify_fixture(executable)
        self.assertTrue(report["passed"], report["errors"])
        self.assertEqual("CArchive:main", report["modules"]["main"]["storage"])
        self.assertEqual("PYZ:worker", report["modules"]["worker"]["storage"])
        self.assertTrue(report["executable"]["pe"]["isPE32"])
        self.assertTrue(report["executable"]["pe"]["isX86"])
        self.assertEqual(hashlib.sha256(executable.read_bytes()).hexdigest(),
                         report["executable"]["sha256"])
        self.assertEqual(importlib.util.MAGIC_NUMBER.hex(),
                         report["python"]["archiveBytecodeMagic"])
        self.assertEqual(report["modules"]["worker"]["compiledCodeSha256"],
                         report["modules"]["worker"]["archivedCodeSha256"])

    def test_recursive_filename_normalization_retains_nested_semantics(self):
        source = "class Example:\n    def method(self):\n        return [x + 2 for x in range(3)]\n"
        left = compile(source, "C:/build/source.py", "exec", dont_inherit=True)
        right = compile(source, "different/source.py", "exec", dont_inherit=True)
        self.assertEqual(verify_release.normalized_code_bytes(left),
                         verify_release.normalized_code_bytes(right))
        normalized = verify_release.normalize_code(left)
        stack = [normalized]
        count = 0
        while stack:
            item = stack.pop()
            count += 1
            self.assertEqual(verify_release.NORMALIZED_FILENAME, item.co_filename)
            stack.extend(value for value in item.co_consts if isinstance(value, CodeType))
        self.assertGreaterEqual(count, 4)
        changed = compile(source.replace("x + 2", "x + 3"), "different/source.py", "exec",
                          dont_inherit=True)
        self.assertNotEqual(verify_release.normalized_code_bytes(left),
                            verify_release.normalized_code_bytes(changed))

    def test_old_packaged_main_is_rejected(self):
        executable = self.build_archive()
        (self.root / "main.py").write_text("value = 2\n", encoding="utf-8")
        report = self.verify_fixture(executable)
        self.assertFalse(report["passed"])
        self.assertFalse(report["modules"]["main"]["matched"])
        self.assertTrue(report["modules"]["worker"]["matched"])

    def test_frozenset_constants_are_stable_across_packaging_and_hash_seeds(self):
        worker_source = "def check(value):\n    return value in {'alpha', 'beta', 'gamma', 'delta', 'epsilon'}\n"
        self.build_archive(worker_source=worker_source)
        script = (
            "import hashlib,marshal,sys; from pathlib import Path; "
            "from verify_release import normalized_code_bytes; "
            "p=Path(sys.argv[1]); c=compile(p.read_bytes(),str(p),'exec',dont_inherit=True); "
            "c=marshal.loads(marshal.dumps(c)); "
            "print(hashlib.sha256(normalized_code_bytes(c)).hexdigest())"
        )
        digests = []
        import os
        for seed in ("1", "2", "3"):
            environment = dict(os.environ, PYTHONHASHSEED=seed)
            result = subprocess.run([sys.executable, "-c", script, str(self.root / "worker.py")],
                                    cwd=str(Path(verify_release.__file__).resolve().parent),
                                    env=environment, check=True, capture_output=True, text=True)
            digests.append(result.stdout.strip())
        self.assertEqual(1, len(set(digests)))

    def test_old_nested_function_bytecode_is_rejected(self):
        executable = self.build_archive()
        worker = self.root / "worker.py"
        worker.write_text(worker.read_text(encoding="utf-8").replace("x + value", "x - value"),
                          encoding="utf-8")
        report = self.verify_fixture(executable)
        self.assertFalse(report["passed"])
        self.assertFalse(report["modules"]["worker"]["matched"])

    def test_missing_critical_module_fails_manifest(self):
        executable = self.build_archive()
        report = self.verify_fixture(executable, ("main", "worker", "missing"))
        self.assertFalse(report["passed"])
        self.assertFalse(report["modules"]["missing"]["matched"])
        self.assertIn("Missing critical module", report["modules"]["missing"]["error"])

    def test_pe64_or_wrong_machine_is_rejected(self):
        self.assertTrue(verify_release.inspect_pe32(pe_header())["valid"])
        for raw in (pe_header(0x8664, 0x20B), pe_header(0x8664, 0x10B),
                    b"MZ", bytes(0x200)):
            self.assertFalse(verify_release.inspect_pe32(raw)["valid"])
        executable = self.build_archive()
        executable.write_bytes(pe_header(0x8664, 0x20B) + executable.read_bytes()[0x200:])
        report = self.verify_fixture(executable)
        self.assertFalse(report["passed"])
        self.assertTrue(report["modules"]["main"]["matched"])

    def test_different_python_magic_is_rejected_before_unmarshal(self):
        executable = self.build_archive()
        data = bytearray(executable.read_bytes())
        offset = data.index(b"PYZ\0")
        data[offset + 4:offset + 8] = b"BAD!"
        executable.write_bytes(data)
        report = self.verify_fixture(executable)
        self.assertFalse(report["passed"])
        self.assertFalse(report["python"]["magicMatches"])
        self.assertEqual({}, report["modules"])

    def test_cli_writes_failed_manifest_and_returns_nonzero(self):
        executable = self.build_archive()
        output = self.root / "reports" / "manifest.json"
        with contextlib.redirect_stdout(io.StringIO()):
            result = verify_release.main([str(executable), "--source-root", str(self.root),
                                          "--output", str(output)])
        self.assertNotEqual(0, result)
        report = json.loads(output.read_text(encoding="utf-8"))
        self.assertFalse(report["passed"])
        self.assertIn("mywindow", report["modules"])
        self.assertTrue(report["modules"]["main"]["matched"])


if __name__ == "__main__":
    unittest.main()
