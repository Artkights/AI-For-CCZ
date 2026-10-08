import argparse
import ctypes
import importlib.util
import tempfile
import unittest
from pathlib import Path

from capability_evidence import (COLLECTOR_ID, EvidenceRepository, make_bundle,
                                 recipe_id)
from evidence_collector import (CollectorError, WindowsBreakpointCollector,
                                build_parser, collect_static,
                                verify_captured_bundle)
from process_memory import FakeMemoryBackend, MemorySession


WORKSPACE = Path(__file__).resolve().parents[3]
CAOCAO = WORKSPACE / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"
HAS_CAPSTONE = importlib.util.find_spec("capstone") is not None


class EvidenceCollectorTests(unittest.TestCase):
    @unittest.skipUnless(HAS_CAPSTONE, "Capstone development dependency is absent")
    def test_static_collector_writes_manifest_and_normalized_code(self):
        with tempfile.TemporaryDirectory() as directory:
            written = collect_static(
                str(CAOCAO), directory, ("battle",), None)
            self.assertEqual(1, len(written))
            bundles = EvidenceRepository(directory).load_bundles()
            self.assertEqual(1, len(bundles))
            self.assertEqual("battle", bundles[0]["featureKey"])
            windows = bundles[0]["staticEvidence"]["windows"]
            self.assertTrue(any(item["normalizedInstructions"]
                                for item in windows))

    @unittest.skipUnless(HAS_CAPSTONE, "Capstone development dependency is absent")
    def test_auto_return_static_collection_contains_native_patch_contract(self):
        with tempfile.TemporaryDirectory() as directory:
            collect_static(str(CAOCAO), directory,
                           ("control_auto_return",), None)
            bundle = EvidenceRepository(directory).load_bundles()[0]
            addresses = set(bundle["staticEvidence"]["addresses"])
            for address in (0x00412D7B, 0x0043D522, 0x00421175,
                            0x00412D82, 0x0043D529, 0x0043D540):
                self.assertIn("0x%08X" % address, addresses)
            self.assertEqual("patch", bundle["recipe"]["kind"])
            self.assertEqual("native-second-action-unrestricted",
                             bundle["recipe"]["mode"])

    @unittest.skipUnless(ctypes.sizeof(ctypes.c_void_p) == 8,
                         "WOW64 selection is specific to a 64-bit host")
    def test_runtime_collector_uses_wow64_context_api(self):
        backend = FakeMemoryBackend(0x100000, b"\0" * 64)
        session = MemorySession(0x100000, 0x100000, 64, backend=backend,
                                owns_handle=False,
                                reference_image_base=0x100000)
        collector = WindowsBreakpointCollector(1, session, (0x100000,), ())
        self.assertTrue(collector.wow64)
        self.assertIn("Wow64", collector.get_context.__name__)

    def test_verifier_derives_checks_from_samples_instead_of_manual_boolean(self):
        recipe = {
            "kind": "test",
            "verificationContract": {"checks": {
                "runtime_probe": {"type": "all-of", "predicates": [
                    {"type": "min-samples", "minimum": 2},
                    {"type": "field-present", "path": "nextEip"},
                ]},
                "behavior_verified": {
                    "type": "watch-relation", "tag": "phase",
                    "watch": "unit", "left": "baseline", "right": "patched",
                    "relation": "different",
                },
            }},
        }
        samples = [
            {"nextEip": "0x00401000", "tags": {"phase": "baseline"},
             "watches": {"unit": {"bytesHex": "00"}}},
            {"nextEip": "0x00401001", "tags": {"phase": "patched"},
             "watches": {"unit": {"bytesHex": "01"}}},
        ]
        bundle = make_bundle(
            "battle", "A" * 64, 100, recipe_id("battle", recipe), recipe,
            "runtime", {}, samples,
            {"collector": COLLECTOR_ID, "status": "captured",
             "automated": True, "checks": {}})
        with tempfile.TemporaryDirectory() as captured_dir, \
                tempfile.TemporaryDirectory() as verified_dir:
            captured = EvidenceRepository(captured_dir).write_bundle(bundle)
            verified = verify_captured_bundle(str(captured), verified_dir)
            self.assertTrue(verified.is_file())
            loaded = EvidenceRepository(verified_dir).load_bundles(
                runtime_only=True)
            self.assertEqual("verified", loaded[0]["verification"]["status"])

    def test_verifier_rejects_missing_machine_contract(self):
        recipe = {"kind": "test"}
        bundle = make_bundle(
            "battle", "A" * 64, 100, recipe_id("battle", recipe), recipe,
            "runtime", {}, [{"manuallyObserved": True}])
        with tempfile.TemporaryDirectory() as captured_dir, \
                tempfile.TemporaryDirectory() as verified_dir:
            captured = EvidenceRepository(captured_dir).write_bundle(bundle)
            with self.assertRaises(CollectorError):
                verify_captured_bundle(str(captured), verified_dir)

    def test_obsolete_auto_return_matrix_commands_are_not_exposed(self):
        parser = build_parser()
        subparsers = next(action for action in parser._actions
                          if isinstance(action, argparse._SubParsersAction))
        self.assertNotIn("auto-return", subparsers.choices)
        self.assertNotIn("auto-return-report", subparsers.choices)


if __name__ == "__main__":
    unittest.main()
