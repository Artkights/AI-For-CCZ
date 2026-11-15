import struct
import unittest
from pathlib import Path

from engine_profile import (
    BOOL_BROWSE_BYTES_66, BOOL_BROWSE_COUNT_66, BOOL_USER_BYTES_66,
    BOOL_USER_COUNT_66, EngineVersion, PEImage, RuntimeModuleImage,
    detect_engine, detect_composite_profile, legacy_profile,
    locate_boolean_layout_66, locate_boolean_layout_legacy,
)
from fixture_support import FIXTURE_SKIP_REASON, HAS_FIXTURES, WORKSPACE
from process_memory import FakeMemoryBackend, MemorySession


STANDARD = WORKSPACE / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"
LEGACY_STANDARD = WORKSPACE / "基底" / "加强版6.5未加密版" / "Ekd5.exe"
LEGACY_64 = WORKSPACE / "基底" / "洗隋" / "Ekd5.exe"


class BooleanDomainContractTests(unittest.TestCase):
    def test_domain_byte_counts_are_derived_without_changing_legacy_arrays(self):
        self.assertEqual((1536, 2048, 192, 256), (
            BOOL_USER_COUNT_66, BOOL_BROWSE_COUNT_66,
            BOOL_USER_BYTES_66, BOOL_BROWSE_BYTES_66))
        profile = legacy_profile(EngineVersion.V65)
        self.assertEqual(4096, profile.limits["variable_bool_count"])
        self.assertEqual(4, profile.metadata["variable_layouts"][
            "variables_bool"]["stride"])
        self.assertFalse(profile.metadata["variable_layouts"][
            "variables_bool"]["nativeCapacityVerified"])
        self.assertFalse(profile.can_write("variables_bool"))

    @unittest.skipUnless(HAS_FIXTURES, FIXTURE_SKIP_REASON)
    def test_legacy_native_dword_capacity_is_proven_by_serializer(self):
        profile = detect_composite_profile(str(LEGACY_STANDARD), version_part=5)
        layout = profile.metadata["variable_layouts"]["variables_bool"]
        self.assertEqual("dword", layout["storage"])
        self.assertEqual(2048, layout["recordCount"])
        self.assertEqual(2048, layout["nativeRecordCount"])
        self.assertTrue(layout["nativeCapacityVerified"])
        self.assertEqual(4, layout["stride"])
        self.assertTrue(profile.can_read("variables_bool"))
        self.assertTrue(profile.can_write("variables_bool"))

    @unittest.skipUnless(HAS_FIXTURES, FIXTURE_SKIP_REASON)
    def test_legacy_64_and_65_share_the_proven_dword_contract(self):
        for path in (LEGACY_64, LEGACY_STANDARD):
            with self.subTest(path=path.name):
                mapping, reason = locate_boolean_layout_legacy(PEImage(str(path)))
                self.assertIsNotNone(mapping, reason)
                self.assertEqual("dword", mapping["storage"])
                self.assertEqual(2048, mapping["nativeRecordCount"])

    def test_direct_compat_cannot_write_a_66_bool_without_explicit_format_proof(self):
        profile = legacy_profile(EngineVersion.V65)
        profile.version = EngineVersion.V66
        profile.force_unverified_66 = True
        profile.metadata["variable_layouts"]["variables_bool"].update({
            "writableRecordCount": BOOL_USER_COUNT_66,
        })
        self.assertFalse(profile.can_write("variables_bool"))
        profile.metadata["variable_layouts"]["variables_bool"]["formatVerified"] = False
        self.assertFalse(profile.can_write("variables_bool"))


@unittest.skipUnless(HAS_FIXTURES, FIXTURE_SKIP_REASON)
class BooleanNativeEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.image = PEImage(str(STANDARD))
        self.mapping, reason = locate_boolean_layout_66(self.image)
        self.assertIsNotNone(self.mapping, reason)

    def mutate_image(self, address, raw):
        image = PEImage(str(STANDARD))
        data = bytearray(image.data)
        offset = image.va_to_offset(address)
        data[offset:offset + len(raw)] = raw
        image.data = bytes(data)
        return image

    def runtime_image(self, read_only=False, relocate=False):
        actual_base = 0x00700000
        mapped = bytearray(self.image.size_of_image)
        for section in self.image.sections:
            start = section.virtual_address
            raw = self.image.section_data(section)
            mapped[start:start + len(raw)] = raw
        if relocate:
            for span in self.mapping["verificationSpans"]:
                start = span["address"] - self.image.image_base
                for offset in span["absoluteOffsets"]:
                    value = struct.unpack_from("<I", mapped, start + offset)[0]
                    if (self.image.image_base <= value <
                            self.image.image_base + len(mapped)):
                        struct.pack_into("<I", mapped, start + offset,
                                         value + actual_base - self.image.image_base)
        backend = FakeMemoryBackend(actual_base, bytes(mapped))
        session = MemorySession(
            1, actual_base, len(mapped), backend=backend,
            owns_handle=False, read_only=read_only,
            reference_image_base=self.image.image_base)
        return RuntimeModuleImage(str(STANDARD), session), session, backend

    def test_native_getter_setter_and_script_object_are_the_evidence(self):
        anchors = self.mapping["anchors"]
        self.assertEqual(0x00492FC8, self.mapping["base"])
        self.assertEqual(0x0040C6A9, anchors["reader"])
        self.assertEqual(0x0040C6CC, anchors["writer"])
        self.assertEqual(0x00492F90, anchors["scriptObject"])
        self.assertEqual(0x0041179F, anchors["condition"])
        self.assertEqual(0x00401925, anchors["assignment"])
        self.assertFalse(self.mapping["nativeIndexBoundsVerified"])
        profile = detect_engine(str(STANDARD), version_part=6)
        self.assertNotIn("variables_bool_anchor", profile.capabilities)
        self.assertNotIn("variables_bool_storage", profile.capabilities)
        layout = profile.metadata["variable_layouts"]["variables_bool"]
        self.assertEqual((1536, 1536, 2048, 192, 256), (
            layout["recordCount"], layout["writableRecordCount"],
            layout["browseRecordCount"], layout["storageBytes"],
            layout["browseStorageBytes"]))
        self.assertFalse(profile.can_write("variables_bool"))

    def test_broken_native_semantics_and_call_graph_fail_closed(self):
        anchors = self.mapping["anchors"]
        cases = {
            "reader-bit-order": (anchors["reader"] + 19, b"\x00"),
            "writer-bit-order": (anchors["writer"] + 33, b"\x00"),
            "clear-mask": (anchors["masks"], b"\xFE"),
            "division": (anchors["divide"] + 2, b"\x90"),
            "getter-callee": (anchors["reader"] + 9, b"\0\0\0\0"),
            "condition-dispatch": (anchors["dispatchTable"] + 12,
                                   struct.pack("<I", anchors["assignment"])),
            "assignment-dispatch": (anchors["dispatchTable"] + 36,
                                    struct.pack("<I", anchors["condition"])),
            "script-object-vtable": (anchors["vtable"] + 8,
                                     struct.pack("<I", anchors["reader"])),
        }
        for label, (address, raw) in cases.items():
            with self.subTest(label=label):
                image = self.mutate_image(address, raw)
                mapping, reason = locate_boolean_layout_66(image)
                self.assertIsNone(mapping)
                self.assertTrue(reason)
                profile = detect_engine(str(STANDARD), version_part=6, image=image)
                profile.force_unverified_66 = True
                self.assertFalse(profile.can_write("variables_bool"))
                self.assertFalse(profile.metadata["variable_layouts"][
                    "variables_bool"]["formatVerified"])

    def test_duplicate_reader_is_ambiguous(self):
        reader = self.mapping["anchors"]["reader"]
        duplicate = self.image.read_va(reader, 35)
        mapping, reason = locate_boolean_layout_66(
            self.mutate_image(0x00401000, duplicate))
        self.assertIsNone(mapping)
        self.assertIn("reader semantic candidates: 2", reason)

    def test_runtime_relocation_normalizes_absolute_operands(self):
        image, session, _backend = self.runtime_image(relocate=True)
        mapping, reason = locate_boolean_layout_66(image)
        self.assertIsNotNone(mapping, reason)
        self.assertEqual(self.mapping["base"], mapping["base"])
        self.assertEqual(self.mapping["anchors"], mapping["anchors"])
        profile = detect_engine(str(STANDARD), version_part=6, image=image)
        profile.probe_runtime(session)
        self.assertTrue(profile.can_read("variables_bool"))
        self.assertTrue(profile.can_write("variables_bool"))

    def test_runtime_changed_native_evidence_revokes_direct_compat_writes(self):
        image, session, backend = self.runtime_image()
        profile = detect_engine(str(STANDARD), version_part=6, image=image)
        profile.probe_runtime(session)
        self.assertTrue(profile.can_write("variables_bool"))
        actual = session.resolve_reference(self.mapping["anchors"]["masks"])
        backend.write(actual, b"\xFE")
        profile.probe_runtime(session)
        profile.force_unverified_66 = True
        self.assertFalse(profile.can_write("variables_bool"))
        self.assertIn("variables_bool", profile.runtime_probe_failures)

    def test_read_only_session_keeps_bool_readable_and_disables_writes(self):
        image, session, _backend = self.runtime_image(read_only=True)
        profile = detect_engine(str(STANDARD), version_part=6, image=image)
        profile.probe_runtime(session)
        self.assertTrue(profile.can_read("variables_bool"))
        self.assertFalse(profile.can_write("variables_bool"))

    def test_6_6_variants_use_the_native_format_and_conservative_domain(self):
        names = (
            "复刻版6.6山贼", "复刻版6.6山贼 国庆",
            "信奈的野望12关版（密码nobuna）", "信奈的野望（15+1关版，密码nobuna）",
            "厚黑6.6测（7.9水杯版）", "曹操传6.6-V7.16 解压密码：66",
            "烈焰同盟6.6x(新修版)黄线完结+红线5关",
        )
        for name in names:
            path = WORKSPACE / "基底" / name / "Ekd5.exe"
            if not path.exists():
                continue
            with self.subTest(name=name):
                mapping, reason = locate_boolean_layout_66(PEImage(str(path)))
                self.assertIsNotNone(mapping, reason)
                self.assertEqual(0x00492FC8, mapping["base"])


if __name__ == "__main__":
    unittest.main()
