import json
import tempfile
import unittest
from pathlib import Path

import engine_profile
from capability_evidence import (COLLECTOR_ID, CapabilityState, EvidenceError,
                                 EvidenceRepository, FeatureCapability,
                                 make_bundle, recipe_id, validate_bundle)
from engine_profile import detect_engine


WORKSPACE = Path(__file__).resolve().parents[3]
QINGER = WORKSPACE / "基底" / "清儿吕布传" / "Ekd5.exe"
CAOCAO = WORKSPACE / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"


def verified_bundle(feature, sha, size, recipe, checks=None):
    checks = checks or {"runtime_probe": True, "behavior_verified": True}
    return make_bundle(
        feature, sha, size, recipe_id(feature, recipe), recipe, "runtime",
        {"signatureUnique": True}, [{"sample": 1}],
        {"collector": COLLECTOR_ID, "status": "verified",
         "automated": True, "checks": checks})


class CapabilityEvidenceTests(unittest.TestCase):
    def test_available_is_only_true_for_enabled_state(self):
        capability = FeatureCapability(
            "test", CapabilityState.UNAVAILABLE, recipe_id="test.recipe")
        self.assertFalse(capability.available)
        capability = capability.promote(CapabilityState.STATIC_VERIFIED)
        self.assertFalse(capability.available)
        capability = capability.promote(CapabilityState.RUNTIME_VERIFIED)
        self.assertFalse(capability.available)
        capability = capability.promote(CapabilityState.ENABLED)
        self.assertTrue(capability.available)

    def test_state_machine_rejects_skipped_or_reverse_transition(self):
        capability = FeatureCapability(
            "test", CapabilityState.UNAVAILABLE, recipe_id="test.recipe")
        with self.assertRaises(ValueError):
            capability.promote(CapabilityState.RUNTIME_VERIFIED)
        static = capability.promote(CapabilityState.STATIC_VERIFIED)
        with self.assertRaises(ValueError):
            static.promote(CapabilityState.UNAVAILABLE)

    def test_bundle_digest_rejects_tampering(self):
        recipe = {"kind": "test"}
        bundle = verified_bundle("battle", "A" * 64, 100, recipe)
        validate_bundle(bundle, runtime_required=True)
        bundle["runtimeSamples"][0]["sample"] = 2
        with self.assertRaises(EvidenceError):
            validate_bundle(bundle, runtime_required=True)

    def test_verified_recipe_requires_both_baselines_and_same_recipe(self):
        recipe = {"kind": "test", "version": 1}
        with tempfile.TemporaryDirectory() as directory:
            repository = EvidenceRepository(directory)
            repository.write_bundle(verified_bundle(
                "battle", "A" * 64, 100, recipe))
            self.assertIsNone(repository.verified_recipe(
                "battle", ("A" * 64, "B" * 64)))
            repository.write_bundle(verified_bundle(
                "battle", "B" * 64, 200, recipe))
            verified = repository.verified_recipe(
                "battle", ("A" * 64, "B" * 64))
            self.assertIsNotNone(verified)
            self.assertEqual(recipe_id("battle", recipe), verified.recipe_id)

    def test_manifest_rejects_changed_evidence_file(self):
        recipe = {"kind": "test"}
        with tempfile.TemporaryDirectory() as directory:
            repository = EvidenceRepository(directory)
            path = repository.write_bundle(verified_bundle(
                "battle", "A" * 64, 100, recipe))
            content = json.loads(path.read_text(encoding="utf-8"))
            content["target"]["fileSize"] = 101
            path.write_text(json.dumps(content), encoding="utf-8")
            with self.assertRaises(EvidenceError):
                repository.load_bundles()

    def test_profile_promotes_only_after_two_matching_runtime_bundles(self):
        baseline = detect_engine(str(CAOCAO), version_part=6)
        recipe = baseline.capability("battle").recipe
        with tempfile.TemporaryDirectory() as directory:
            repository = EvidenceRepository(directory)
            repository.write_bundle(verified_bundle(
                "battle", baseline.sha256, baseline.file_size, recipe))
            qinger = detect_engine(str(QINGER), version_part=6)
            repository.write_bundle(verified_bundle(
                "battle", qinger.sha256, qinger.file_size, recipe))
            original = engine_profile._EVIDENCE_REPOSITORY
            engine_profile._EVIDENCE_REPOSITORY = repository
            try:
                promoted = detect_engine(str(CAOCAO), version_part=6)
            finally:
                engine_profile._EVIDENCE_REPOSITORY = original
            self.assertEqual(CapabilityState.RUNTIME_VERIFIED,
                             promoted.capability("battle").state)
            self.assertFalse(promoted.capability("battle").available)


if __name__ == "__main__":
    unittest.main()
