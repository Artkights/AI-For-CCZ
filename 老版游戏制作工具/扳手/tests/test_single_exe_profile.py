"""Static-only regression coverage for the standalone Ekd5 derivative."""

import shutil
import struct
import tempfile
import unittest
from pathlib import Path

from capability_evidence import CapabilityState
from engine_profile import (
    EFFECT_LAYOUT_16_ABSOLUTE_COUNTER_QUERY_PATTERN,
    EFFECT_LAYOUT_16_IMPORTED_HELPER_QUERY_PATTERN,
    EFFECT_LAYOUT_16_QUERY_PATTERN,
    EngineVersion, ITEM_CODEC_66, MaskedSignature, PEImage,
    detect_composite_profile, locate_consumable_mapping_single_exe_66,
)
from fixture_support import FIXTURE_SKIP_REASON, WORKSPACE


SINGLE_EXE = WORKSPACE / "基底" / "Ekd5.exe"
TARGET_SHA256 = "719F710687CAE4BAEC2B3A4B028B473EA8AEB334A7ACFA6A692B46F207B5CDF8"


@unittest.skipUnless(SINGLE_EXE.is_file(), FIXTURE_SKIP_REASON)
class SingleExeProfileTests(unittest.TestCase):
    def test_target_is_66_by_resource_and_independent_layouts(self):
        image = PEImage(str(SINGLE_EXE))
        self.assertEqual(TARGET_SHA256, image.sha256)
        self.assertEqual(6200832, len(image.data))
        fixed_info = image.data.find(b"\xBD\x04\xEF\xFE")
        self.assertGreaterEqual(fixed_info, 0)
        version_ls = struct.unpack_from("<I", image.data, fixed_info + 12)[0]
        self.assertEqual(6, version_ls & 0xFFFF)

        profile = detect_composite_profile(str(SINGLE_EXE), version_part=6)
        self.assertEqual(EngineVersion.V66, profile.engine_version)
        self.assertEqual("单EXE扩展6.6（基底Ekd5）", profile.variant_name)
        self.assertEqual("imported-helper-4+2",
                         profile.metadata["effect_query_variant"])
        layout = profile.resolved_feature("effect").layout
        self.assertEqual(("effect-4+2-v1", 0x500C3B, 255, 16),
                         (layout.codec, layout.reference,
                          layout.record_count, layout.stride))
        anchors = {
            item.role.value: item.address
            for item in profile.metadata["table_descriptors"]["effect"].anchors
        }
        self.assertEqual({"initializer": 0x40116D, "writer": 0x41301E,
                          "reader": 0x4198BE}, anchors)
        self.assertEqual(0x511000,
                         struct.unpack("<I", image.read_va(0x500C3B, 4))[0])
        self.assertEqual(ITEM_CODEC_66, profile.metadata["item_row_codec"])
        self.assertEqual(0x49F820, profile.addresses["item_table"])

    def test_imported_reader_is_mutually_exclusive_with_prior_recipes(self):
        image = PEImage(str(SINGLE_EXE))
        for pattern, expected in (
            (EFFECT_LAYOUT_16_QUERY_PATTERN, []),
            (EFFECT_LAYOUT_16_ABSOLUTE_COUNTER_QUERY_PATTERN, []),
            (EFFECT_LAYOUT_16_IMPORTED_HELPER_QUERY_PATTERN, [0x4198BE]),
        ):
            with self.subTest(pattern=pattern):
                self.assertEqual(expected, image.find_signature(
                    MaskedSignature.parse(pattern)))

    def test_fatal_recalculate_and_move_recipes_follow_live_consumers(self):
        profile = detect_composite_profile(str(SINGLE_EXE), version_part=6)
        self.assertEqual("index-consumer-bounds",
                         profile.metadata["fatal_layout_source"])
        self.assertEqual(0x500C37, profile.addresses["fatal_pointer"])
        self.assertEqual((80, 16), (profile.limits["fatal_count"],
                                   profile.limits["fatal_stride"]))
        self.assertEqual(CapabilityState.STATIC_VERIFIED,
                         profile.capability("fatal").state)
        self.assertEqual("global-state-nops",
                         profile.metadata["recalculate_variant"])
        recalculate = profile.remote_calls["recalculate"]
        self.assertEqual((0x4075DF, "thiscall", 4,
                          "recalculate-person-v1"),
                         (recalculate.address,
                          recalculate.calling_convention,
                          recalculate.callee_cleanup,
                          recalculate.wrapper_kind))
        self.assertEqual(0x4073F3,
                         profile.capability("recalculate").recipe["internalCall"])
        move_site = profile.patches["control_move_255"].sites[0]
        self.assertEqual(0x43F74F, move_site.address)
        self.assertEqual(bytes.fromhex("558BEC83EC08"), move_site.original)

    def test_fatal_bound_must_call_the_same_indexer(self):
        with tempfile.TemporaryDirectory() as directory:
            changed = Path(directory) / "changed.exe"
            data = bytearray(SINGLE_EXE.read_bytes())
            image = PEImage(str(SINGLE_EXE))
            call = image.va_to_offset(0x41129E)
            data[call + 1] ^= 1
            changed.write_bytes(data)
            profile = detect_composite_profile(str(changed), version_part=6)
            self.assertEqual(CapabilityState.UNAVAILABLE,
                             profile.capability("fatal").state)

    def test_fatal_bound_second_call_must_reach_the_same_five_slot_consumer(self):
        with tempfile.TemporaryDirectory() as directory:
            changed = Path(directory) / "changed.exe"
            data = bytearray(SINGLE_EXE.read_bytes())
            image = PEImage(str(SINGLE_EXE))
            call = image.va_to_offset(0x4112A9)
            data[call + 1] ^= 1
            changed.write_bytes(data)
            profile = detect_composite_profile(str(changed), version_part=6)
            self.assertEqual(CapabilityState.UNAVAILABLE,
                             profile.capability("fatal").state)
            # Evidence state is diagnostic under the existing direct_compat
            # policy; the assertion above intentionally does not claim that
            # changing a signature disables all compatibility writes.

    def test_fatal_consumer_must_still_prove_five_slots_and_row_15(self):
        for address in (0x41875E, 0x418757):
            with self.subTest(address=address):
                with tempfile.TemporaryDirectory() as directory:
                    changed = Path(directory) / "changed.exe"
                    data = bytearray(SINGLE_EXE.read_bytes())
                    image = PEImage(str(SINGLE_EXE))
                    data[image.va_to_offset(address)] ^= 1
                    changed.write_bytes(data)
                    profile = detect_composite_profile(str(changed), version_part=6)
                    self.assertEqual(CapabilityState.UNAVAILABLE,
                                     profile.capability("fatal").state)

    def test_recalculate_internal_call_target_is_checked_for_nop_variant(self):
        with tempfile.TemporaryDirectory() as directory:
            changed = Path(directory) / "changed.exe"
            data = bytearray(SINGLE_EXE.read_bytes())
            image = PEImage(str(SINGLE_EXE))
            call = image.va_to_offset(0x4075DF + 0x28)
            data[call + 1] ^= 1
            changed.write_bytes(data)
            profile = detect_composite_profile(str(changed), version_part=6)
            self.assertEqual(CapabilityState.UNAVAILABLE,
                             profile.capability("recalculate").state)
            self.assertEqual(0, profile.remote_calls["recalculate"].address)

    def test_extended_consumable_ids_keep_their_own_storage_domain(self):
        profile = detect_composite_profile(str(SINGLE_EXE), version_part=6)
        spec = profile.metadata["consumable_mapping_spec"]
        self.assertEqual(("single-exe-66-256-v1", 0x750000, 256, 256, 511),
                         (spec.key, spec.base, spec.id_min,
                          spec.storage_count, spec.storage_id_max))
        self.assertEqual({"update": 0x420299, "read": 0x4202C4,
                          "write": 0x421894, "clear": 0x42BB2D},
                         spec.anchors)
        self.assertEqual(tuple(range(256, 512)), profile.item_ids("consumable"))
        self.assertTrue(profile.is_item_id(256, "consumable"))
        self.assertTrue(profile.is_item_id(511, "consumable"))
        self.assertFalse(profile.is_item_id(255, "consumable"))
        self.assertFalse(profile.is_item_id(512, "consumable"))
        self.assertEqual(tuple(range(160)), profile.item_ids("equipment"))
        self.assertEqual(tuple(range(255)), profile.item_ids("record"))
        self.assertEqual(512, profile.limits["item_record_count"])
        self.assertEqual(255, profile.limits["item_count"])
        self.assertEqual(CapabilityState.STATIC_VERIFIED,
                         profile.capability("item_consumables").state)

    def test_consumable_recipe_rejects_one_accessor_using_a_different_base(self):
        with tempfile.TemporaryDirectory() as directory:
            changed = Path(directory) / "changed.exe"
            data = bytearray(SINGLE_EXE.read_bytes())
            image = PEImage(str(SINGLE_EXE))
            write_operand = image.va_to_offset(0x421894 + 13)
            struct.pack_into("<I", data, write_operand, 0x760000)
            changed.write_bytes(data)
            spec, reason = locate_consumable_mapping_single_exe_66(
                PEImage(str(changed)))
            self.assertIsNone(spec)
            self.assertIn("同一基址", reason)

    def test_sha_identity_does_not_enable_a_changed_reader(self):
        with tempfile.TemporaryDirectory() as directory:
            changed = Path(directory) / "renamed.exe"
            shutil.copyfile(SINGLE_EXE, changed)
            data = bytearray(changed.read_bytes())
            image = PEImage(str(changed))
            # Replace an essential part of the remaining job-slot reader,
            # while preserving the initializer and all pointer cells.
            data[image.va_to_offset(0x419918)] = 0x90
            changed.write_bytes(data)
            profile = detect_composite_profile(str(changed), version_part=6)
            self.assertIsNone(profile.resolved_feature("effect").layout)
            self.assertEqual(CapabilityState.UNAVAILABLE,
                             profile.capability("effect").state)
            self.assertNotEqual(TARGET_SHA256, profile.sha256)


if __name__ == "__main__":
    unittest.main()
