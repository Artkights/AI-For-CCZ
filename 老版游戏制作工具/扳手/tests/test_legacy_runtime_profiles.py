import struct
import unittest
from dataclasses import replace

from engine_layout import AddressResolverKind
from engine_profile import (
    EngineVersion, ITEM_CODEC_65, ITEM_CODEC_66, ITEM_CODEC_LEGACY,
    ITEM_RUNTIME_TABLE_PATTERN, MaskedSignature, ProfileError,
    _SHUHAN64_REMOTE_ABI_SHA256, _SHUHAN64_VERIFIED_REMOTE_CALLS,
    _finish_profile, _infer_engine_family, _legacy_auto_return_recipe,
    legacy_profile, locate_fixed_effect_layout_64, locate_item_row_codec)
from process_memory import FakeMemoryBackend, MemorySession
from runtime_catalog import hydrate_runtime_item_catalog
from runtime_regions import ScanRegion


class CodeImage:
    image_base = 0x400000
    size_of_image = 0x140000
    source = "runtime-test"

    def __init__(self):
        self.data = bytearray(self.size_of_image)

    def put(self, address, raw):
        offset = address - self.image_base
        self.data[offset:offset + len(raw)] = raw

    def read_va(self, address, size):
        offset = address - self.image_base
        if offset < 0 or offset + size > len(self.data):
            raise ProfileError("out of range")
        return bytes(self.data[offset:offset + size])

    def iter_scan_regions(self, executable_only=True):
        yield ScanRegion(self.image_base, bytes(self.data), False, self.image_base)

    def find_signature(self, signature):
        return [self.image_base + offset for offset in signature.find_all(bytes(self.data))]


def make_catalog(count, prefix="runtime", legacy=False):
    rows = []
    for index in range(count):
        row = bytearray(25)
        name = (prefix + str(index)).encode("ascii")
        row[:len(name)] = name
        row[0x14] = 0xFF if legacy and index in range(87, 130) else 0
        row[0x18] = int(index == 1)
        rows.append(bytes(row))
    return b"".join(rows)


class LegacyRuntimeProfilesTests(unittest.TestCase):
    def effect_image(self, writer_limit=0xB3, query_pointer=0x500C3B):
        image = CodeImage()
        writer = bytes.fromhex(
            "0FB645F83CB377246BC808030D3B0C50008A45F93C037714"
            "6BD00203CA668B55FA3C037405668911EB028811")
        writer = writer[:5] + bytes((writer_limit,)) + writer[6:]
        image.put(0x408839, writer)
        image.put(0x500C3B, struct.pack("<I", 0x508998))
        image.put(0x451746, bytes.fromhex("2D000100006BC808030D") +
                  struct.pack("<I", query_pointer) + b"\xE9\0\0\0\0")
        return image

    def test_b3_writer_and_same_pointer_detour_resolve_180_rows(self):
        layout, hits, reason = locate_fixed_effect_layout_64(self.effect_image())
        self.assertEqual("", reason)
        self.assertEqual(AddressResolverKind.POINTER_RVA, layout.resolver)
        self.assertEqual((180, 8, 0x500C3B),
                         (layout.record_count, layout.stride, layout.reference))
        self.assertEqual(0x508998, hits["fixed"])

    def test_b3_query_for_another_pointer_does_not_authorize_effect_table(self):
        layout, _hits, reason = locate_fixed_effect_layout_64(
            self.effect_image(query_pointer=0x500C40))
        self.assertIsNone(layout)
        self.assertIn("查询", reason)

    def test_b3_code_overrides_misleading_65_or_66_resource(self):
        for resource in (None, 5, 6):
            version, evidence = _infer_engine_family(self.effect_image(), resource)
            self.assertEqual(EngineVersion.V64, version)
            self.assertEqual("code", evidence["source"])

    def test_relocated_item_icon_accessor_proves_field_order(self):
        for icon_offset, expected in ((0x14, ITEM_CODEC_65), (0x0F, ITEM_CODEC_66)):
            image = CodeImage()
            lookup = 0x4837C2
            image.put(lookup, bytes.fromhex("558BEC6BC91981C1") +
                      struct.pack("<I", 0x510000) + b"\x5D\xC3")
            call = 0x420000
            image.put(call, b"\xE8" + struct.pack("<i", lookup - call - 5) +
                      bytes((0x0F, 0xB6, 0x41, icon_offset)))
            codec, base, _hits, reason = locate_item_row_codec(image)
            self.assertEqual((expected, 0x510000, ""), (codec, base, reason))

    def test_runtime_catalog_replaces_valid_but_stale_disk_catalog(self):
        profile = legacy_profile(EngineVersion.V65)
        profile.metadata["item_table_66"] = make_catalog(255, "disk")
        backend = FakeMemoryBackend(0x400000, bytes(0x140000))
        session = MemorySession(1, 0x400000, 0x140000, backend=backend,
                                owns_handle=False, read_only=True)
        backend.write(0x4A1140, make_catalog(255))
        self.assertTrue(hydrate_runtime_item_catalog(profile, session))
        self.assertEqual("runtime1", profile.item_row(1).name)
        self.assertFalse(hydrate_runtime_item_catalog(profile, session))
        backend.write(0x4A1140, make_catalog(255, "changed"))
        self.assertTrue(hydrate_runtime_item_catalog(profile, session))
        self.assertEqual("changed1", profile.item_row(1).name)

    def test_old_154_row_catalog_preserves_legacy_name_and_treasure_contract(self):
        profile = legacy_profile(EngineVersion.V63)
        backend = FakeMemoryBackend(0x400000, bytes(0x140000))
        backend.write(0x4A1140, make_catalog(154, legacy=True))
        session = MemorySession(1, 0x400000, 0x140000, backend=backend,
                                owns_handle=False, read_only=True)
        self.assertTrue(hydrate_runtime_item_catalog(profile, session))
        self.assertEqual(ITEM_CODEC_LEGACY, profile.metadata["item_row_codec"])
        self.assertEqual(154 * 25, len(profile.item_table_66()))
        self.assertFalse(profile.item_row(87).is_treasure)
        self.assertTrue(profile.item_row(130).is_treasure)
        with self.assertRaises(ProfileError):
            profile.item_row(154)

    def test_failed_runtime_refresh_does_not_keep_stale_names(self):
        profile = legacy_profile(EngineVersion.V65)
        profile.metadata["item_table_66"] = make_catalog(255, "stale")
        backend = FakeMemoryBackend(0x400000, bytes(0x140000))
        session = MemorySession(1, 0x400000, 0x140000, backend=backend,
                                owns_handle=False, read_only=True)
        self.assertFalse(hydrate_runtime_item_catalog(profile, session))
        self.assertNotIn("item_table_66", profile.metadata)
        self.assertFalse(profile.resolved_feature("item_metadata").readable)
        backend.write(0x4A1140, make_catalog(255))
        self.assertTrue(hydrate_runtime_item_catalog(profile, session))
        self.assertTrue(profile.resolved_feature("item_metadata").readable)

    def test_extended_runtime_name_change_beyond_byte_catalog_is_refreshed(self):
        profile = legacy_profile(EngineVersion.V65)
        profile.limits["item_record_count"] = 512
        backend = FakeMemoryBackend(0x400000, bytes(0x140000))
        backend.write(0x4A1140, make_catalog(512))
        session = MemorySession(1, 0x400000, 0x140000, backend=backend,
                                owns_handle=False, read_only=True)
        self.assertTrue(hydrate_runtime_item_catalog(profile, session))
        backend.write(0x4A1140 + 300 * 25, b"new-name\0")
        self.assertTrue(hydrate_runtime_item_catalog(profile, session))
        self.assertEqual("new-name", profile.metadata["runtime_item_names"][300])
        self.assertEqual(255 * 25, len(profile.item_table_66()))

    def test_lower_version_effect_domains_do_not_overread_neighboring_names(self):
        for version, count, base in (
                (EngineVersion.V63, 144, 0x4FF7A0),
                (EngineVersion.V62, 91, 0x4FF960),
                (EngineVersion.V61, 88, 0x4FFA80)):
            with self.subTest(version=version):
                image = CodeImage()
                names = tuple("effect%d" % index for index in range(count))
                image.put(base, b"".join(name.encode().ljust(16, b"\0")
                                         for name in names))
                profile = _finish_profile(legacy_profile(version), image, {})
                self.assertEqual(names, profile.metadata["effect_names"])
                self.assertEqual(count, profile.limits["effect_record_count"])
                self.assertEqual(count * 8,
                                 profile.metadata["table_layouts"]["effect"].byte_size)

    def test_62_expanded_runtime_catalog_updates_both_table_bounds(self):
        image = CodeImage()
        names = tuple("expanded%d" % index for index in range(126))
        image.put(0x4F0000, b"".join(name.encode().ljust(16, b"\0")
                                     for name in names))
        profile = _finish_profile(legacy_profile(EngineVersion.V62), image, {})
        self.assertEqual(names, profile.metadata["effect_names"])
        for key in ("effect", "effect_exclusive"):
            self.assertEqual(126, profile.resolved_features[key].layout.record_count)
        self.assertEqual(126, profile.limits["effect_record_count"])

    def test_legacy_position_entry_skips_proven_nop_padding(self):
        image = CodeImage()
        image.put(0x4594DD, b"\x90" * 13 + b"\x55\x8B\xEC\x83\xEC\x18\x51\x8B")
        profile = _finish_profile(legacy_profile(EngineVersion.V64), image, {})
        call = profile.remote_calls["turn_refresh_position"]
        self.assertEqual(0x4594EA, call.address)
        self.assertEqual(b"\x55\x8B\xEC\x83\xEC\x18\x51\x8B", call.signature)
        self.assertFalse(call.abi_verified)
        self.assertEqual(13, profile.metadata["remote_call_entry_aliases"]
                         ["turn_refresh_position"]["padding"])

    def remote_evidence_image(self):
        image = CodeImage()
        for address, signature, _wrapper, _check in _SHUHAN64_VERIFIED_REMOTE_CALLS.values():
            image.put(address, bytes.fromhex(signature))
        image.put(0x4594DD, b"\x90" * 13)
        return image

    def test_shuhan_abi_evidence_requires_exact_identity_signature_and_wrapper(self):
        image = self.remote_evidence_image()
        profile = legacy_profile(EngineVersion.V64)
        profile.sha256 = _SHUHAN64_REMOTE_ABI_SHA256
        profile = _finish_profile(profile, image, {})
        for key in _SHUHAN64_VERIFIED_REMOTE_CALLS:
            self.assertTrue(profile.remote_calls[key].abi_verified)
            self.assertTrue(profile.capabilities[key].evidence_ids)
        self.assertFalse(profile.remote_calls["weather"].abi_verified)
        self.assertEqual(4, len(profile.metadata["legacy_remote_abi_evidence"]))
        for mismatch in ("sha", "source", "signature", "wrapper"):
            image = self.remote_evidence_image()
            profile = legacy_profile(EngineVersion.V64)
            profile.sha256 = _SHUHAN64_REMOTE_ABI_SHA256
            if mismatch == "sha":
                profile.sha256 = "0" * 64
            elif mismatch == "source":
                image.source = "disk"
            elif mismatch == "signature":
                image.put(0x4075E6, b"\x70")
            else:
                profile.remote_calls["recalculate"] = replace(
                    profile.remote_calls["recalculate"], wrapper_kind="unknown-wrapper")
            profile = _finish_profile(profile, image, {})
            self.assertFalse(profile.remote_calls["recalculate"].abi_verified, mismatch)

    def test_legacy_auto_return_keeps_oracle_machine_code_after_relocation(self):
        from evidence_collector import reconstruct_legacy_auto_return_stub
        for version, index, original_target in (
                (EngineVersion.V64, 0, 0x412D52),
                (EngineVersion.V63, 1, 0x412D52),
                (EngineVersion.V62, 3, 0x4CFC9B)):
            image = CodeImage()
            for address in (0x44E570, 0x44EA73):
                image.put(address, b"\xE8" + struct.pack(
                    "<i", original_target - address - 5))
            for address in (original_target, 0x43E4D6, 0x43E6AA):
                image.put(address, b"\x55\x8B\xEC")
            profile = legacy_profile(version)
            spec, reason = _legacy_auto_return_recipe(profile, image)
            self.assertEqual("", reason)
            backend = FakeMemoryBackend(0x400000, bytes(0x140000))
            session = MemorySession(1, 0x400000, 0x140000, backend=backend,
                                    owns_handle=False)
            actual = session._build_dynamic_stub(spec, 0x485E00)
            oracle = reconstruct_legacy_auto_return_stub(
                bytes(73), index, profile.addresses["battle_units"],
                profile.limits["battle_stride"])
            self.assertEqual(oracle, actual)
            relocated = session._build_dynamic_stub(spec, 0x900000)
            self.assertEqual(original_target, 0x900000 + 0x0E +
                             struct.unpack_from("<i", relocated, 0x0A)[0])


if __name__ == "__main__":
    unittest.main()
