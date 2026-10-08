import shutil
import struct
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

from capability_evidence import CapabilityState
from engine_layout import FeatureAccess, ResolvedTableDescriptor, SemanticAnchorRole
from engine_profile import (EFFECT_LAYOUT_16_ABSOLUTE_COUNTER_QUERY_PATTERN,
                             EFFECT_LAYOUT_16_QUERY_PATTERN,
                             EFFECT_NAME_POOL_OFFSET_65_66,
                             EFFECT_NAME_RECORD_COUNT_65_66,
                             EFFECT_ROW_SIZE_66, ITEM_CODEC_65, ITEM_CODEC_66,
                             KNOWN_66, KNOWN_66_VARIANTS, ConsumableMappingSpec,
                             EngineVersion,
                             EffectAssignmentRow66, EffectAssignmentSlot66,
                             EffectAssignmentRow8, MaskedSignature, PEImage,
                             RuntimeModuleImage,
                             ProfileError,
                             detect_composite_profile, detect_engine,
                             encode_effect_assignment_row_66, legacy_profile,
                             locate_auto_return_patch_66,
                             locate_consumable_mapping_composite,
                             locate_consumable_mapping_66,
                             locate_item_row_codec,
                             parse_effect_assignment_row_8,
                             parse_effect_assignment_row_66,
                             verify_engine_identity)
from process_memory import FakeMemoryBackend, MemoryAccessError, MemorySession


WORKSPACE = Path(__file__).resolve().parents[3]
QINGER = WORKSPACE / "基底" / "清儿吕布传" / "Ekd5.exe"
CAOCAO = WORKSPACE / "基底" / "新改曹操傳6.6修正版" / "Ekd5.exe"
BASE65 = WORKSPACE / "基底" / "加强版6.5未加密版" / "Ekd5.exe"
HOUHEI = WORKSPACE / "基底" / "厚黑6.6测（7.9水杯版）" / "Ekd5.exe"
V716 = WORKSPACE / "基底" / "曹操传6.6-V7.16 解压密码：66" / "Ekd5.exe"
ABSOLUTE_COUNTER = WORKSPACE / "基底" / "8.14-绝对反击Ekd5.exe"
YIHE = WORKSPACE / "基底" / "以和为贵1-demo" / "Ekd5.exe"
LIEYAN = (WORKSPACE / "基底" /
          "烈焰同盟6.6x(新修版)黄线完结+红线5关" / "Ekd5.exe")
BASE64 = (WORKSPACE / "基底" /
          "三国之召唤猛将6.4（60关版）基底" / "Ekd5.exe")
PL65 = (WORKSPACE / "基底" /
        "6.5pl加强版神话三国志2026新春版+攻略【密码shsgz65】" / "Ekd5.exe")
DONGWU65 = WORKSPACE / "基底" / "东吴霸王传6.5" / "Ekd5.exe"


def mapped_runtime_image(identity_path, content_path, actual_base=0x00700000):
    identity = PEImage(str(identity_path))
    content = PEImage(str(content_path))
    mapped = bytearray(identity.size_of_image)
    for section in content.sections:
        raw = content.data[
            section.raw_offset:section.raw_offset + section.raw_size]
        start = section.virtual_address
        mapped[start:start + len(raw)] = raw
    backend = FakeMemoryBackend(actual_base, bytes(mapped))
    session = MemorySession(
        1, actual_base, len(mapped), backend=backend,
        owns_handle=False, reference_image_base=identity.image_base,
        read_only=True)
    return RuntimeModuleImage(str(identity_path), session), session, backend


class EngineProfileTests(unittest.TestCase):
    def test_packed_yihe_identity_is_diagnostic_instead_of_attach_gate(self):
        image = PEImage(str(YIHE))
        self.assertEqual(7789311, len(image.data))
        self.assertEqual(
            "402F1675DC863577AD356B16B20FCE058A94C66E13B0BDD5EFAC1CE8E752F26B",
            image.sha256)
        self.assertEqual(9, len(image.sections))
        raw_end = max(section.raw_offset + section.raw_size
                      for section in image.sections)
        self.assertGreater(len(image.data) - raw_end, 7_000_000)
        pe_offset = struct.unpack_from("<I", image.data, 0x3C)[0]
        entrypoint = struct.unpack_from("<I", image.data, pe_offset + 40)[0]
        self.assertEqual(0x00148BD3, entrypoint)
        fixed_info = image.data.find(b"\xBD\x04\xEF\xFE")
        self.assertGreaterEqual(fixed_info, 0)
        file_version_ls = struct.unpack_from(
            "<I", image.data, fixed_info + 12)[0]
        self.assertEqual(6, file_version_ls & 0xFFFF)

        identity = verify_engine_identity(str(YIHE))
        self.assertFalse(identity["core_signature_passed"])
        self.assertEqual((), identity["person_hits"])
        self.assertEqual((), identity["battle_hits"])
        self.assertEqual("disk", identity["source"])
        self.assertIn("仅作诊断", identity["reason"])

        profile = detect_engine(str(YIHE), version_part=6)
        self.assertEqual("以和为贵1-demo 6.6", profile.variant_name)
        self.assertIsNone(profile.resolved_feature("effect").layout)

    def test_identity_still_rejects_non_x86_pe(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "not-x86.exe"
            data = bytearray(CAOCAO.read_bytes())
            pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
            struct.pack_into("<H", data, pe_offset + 4, 0x8664)
            target.write_bytes(data)
            with self.assertRaises(ProfileError):
                verify_engine_identity(str(target))

    def test_packed_yihe_runtime_image_restores_full_66_profile_with_aslr(self):
        image, session, _backend = mapped_runtime_image(YIHE, CAOCAO)
        identity = verify_engine_identity(str(YIHE), image=image)
        self.assertTrue(identity["core_signature_passed"])
        self.assertEqual("runtime", identity["source"])
        self.assertEqual((0x004061E4,), identity["person_hits"])
        self.assertEqual((0x004061F9,), identity["battle_hits"])

        profile = detect_engine(str(YIHE), version_part=6, image=image)
        self.assertEqual("以和为贵1-demo 6.6", profile.variant_name)
        self.assertEqual("runtime", profile.metadata["image_source"])
        self.assertEqual("standard-4+2",
                         profile.metadata["effect_query_variant"])
        layout = profile.resolved_feature("effect").layout
        self.assertEqual(("effect-4+2-v1", 255, 16, 0x00500C3B),
                         (layout.codec, layout.record_count, layout.stride,
                          layout.reference))
        self.assertEqual("runtime-fixed-slots",
                         profile.metadata["effect_name_source"]["kind"])
        self.assertEqual("每回合恢复HP", profile.effect_name_catalog()[0])
        self.assertEqual("FE特效", profile.effect_name_catalog()[254])
        self.assertEqual(ITEM_CODEC_66,
                         profile.metadata["item_row_codec"])
        self.assertEqual(0x0049F820, profile.addresses["item_table"])
        self.assertTrue(session.read_only)

    def test_runtime_signature_scan_crosses_page_boundary(self):
        image, _session, backend = mapped_runtime_image(YIHE, CAOCAO)
        reference = 0x00401FFE
        actual = 0x00700000 + reference - 0x00400000
        backend.write(actual, b"\xA1\xB2\xC3\xD4")
        self.assertIn(reference, image.find_signature(
            MaskedSignature.parse("A1 B2 C3 D4")))

    def test_absolute_counter_814_profile_has_extended_effect_layout(self):
        profile = detect_engine(str(ABSOLUTE_COUNTER), version_part=6)
        self.assertEqual("8.14绝对反击6.6", profile.variant_name)
        self.assertEqual(
            "4A5221B4842A261647D0DE23CDE3867974A5493A9C98C21015A6116AA5F2E063",
            profile.sha256)
        self.assertEqual(1132032, profile.file_size)
        self.assertEqual("absolute-counter-4+2",
                         profile.metadata["effect_query_variant"])
        layout = profile.resolved_feature("effect").layout
        self.assertEqual("effect-4+2-v1", layout.codec)
        self.assertEqual((255, 16), (layout.record_count, layout.stride))
        self.assertEqual(0x00500C3B, layout.reference)
        anchors = {
            item.role.value: item.address
            for item in profile.metadata["table_descriptors"]["effect"].anchors
        }
        self.assertEqual({
            "initializer": 0x0040116D,
            "writer": 0x0041301E,
            "reader": 0x004198BE,
        }, anchors)
        names = profile.effect_name_catalog()
        self.assertEqual(255, len(names))
        self.assertEqual("每回合恢复HP", names[0])
        self.assertEqual("深策缚影", names[127])
        self.assertEqual("☆神圣庇佑☆", names[254])
        self.assertEqual(ITEM_CODEC_66, profile.metadata["item_row_codec"])
        self.assertEqual(0x0049F820, profile.addresses["item_table"])
        self.assertIn("Data.e5", profile.capability("item_metadata").reason)

    def test_absolute_counter_reader_is_mutually_exclusive_with_standard_reader(self):
        standard = MaskedSignature.parse(EFFECT_LAYOUT_16_QUERY_PATTERN)
        absolute = MaskedSignature.parse(
            EFFECT_LAYOUT_16_ABSOLUTE_COUNTER_QUERY_PATTERN)
        cases = (
            (ABSOLUTE_COUNTER, 0, 1),
            (V716, 1, 0),
            (QINGER, 1, 0),
            (CAOCAO, 1, 0),
            (LIEYAN, 1, 0),
            (HOUHEI, 0, 0),
        )
        for path, standard_count, absolute_count in cases:
            with self.subTest(path=path):
                image = PEImage(str(path))
                self.assertEqual(
                    standard_count, len(image.find_signature(standard)))
                self.assertEqual(
                    absolute_count, len(image.find_signature(absolute)))

    def test_absolute_counter_version_resource_keeps_private_part_six(self):
        image = PEImage(str(ABSOLUTE_COUNTER))
        self.assertEqual(b"\x06\x00", image.data[0x114244:0x114246])
        self.assertEqual(0x00542A44, image.offset_to_va(0x114244))

    def test_engine_identity_and_profile_do_not_require_ekd5_filename(self):
        with tempfile.TemporaryDirectory() as directory:
            renamed = Path(directory) / "renamed-game.exe"
            shutil.copyfile(CAOCAO, renamed)
            identity = verify_engine_identity(str(renamed))
            self.assertTrue(identity["person_index"])
            self.assertTrue(identity["battle_index"])
            profile = detect_engine(str(renamed), version_part=6)
            self.assertEqual("effect-4+2-v1",
                             profile.resolved_feature("effect").layout.codec)

    def test_composite_effect_layouts_cover_hybrid_and_extended_66(self):
        thick = detect_engine(str(HOUHEI), version_part=6)
        thick_layout = thick.resolved_feature("effect").layout
        self.assertEqual("effect-3+1-v1", thick_layout.codec)
        self.assertEqual(8, thick_layout.stride)
        self.assertTrue(thick_layout.pointer_backed)
        image = PEImage(str(HOUHEI))
        table = struct.unpack(
            "<I", image.read_va(thick_layout.reference, 4))[0]
        self.assertEqual(0x00507800, table)
        self.assertIsInstance(
            parse_effect_assignment_row_8(image.read_va(table, 8)),
            EffectAssignmentRow8)

        for path in (V716, LIEYAN):
            with self.subTest(path=path):
                profile = detect_engine(str(path), version_part=6)
                layout = profile.resolved_feature("effect").layout
                self.assertEqual("effect-4+2-v1", layout.codec)
                self.assertEqual(16, layout.stride)
                self.assertTrue(layout.semantics_proven)
                identity = verify_engine_identity(str(path))
                self.assertTrue(identity["person_index"])
                self.assertTrue(identity["battle_index"])

    def test_semantic_descriptor_fingerprints_group_equivalent_layouts(self):
        standard = []
        person = []
        for path in (QINGER, CAOCAO, V716, LIEYAN):
            profile = detect_engine(str(path), version_part=6)
            descriptors = profile.metadata["table_descriptors"]
            standard.append(descriptors["effect"].structure_fingerprint)
            person.append(descriptors["person"].structure_fingerprint)
        self.assertEqual(1, len(set(standard)))
        self.assertEqual(1, len(set(person)))
        thick = detect_engine(str(HOUHEI), version_part=6)
        self.assertNotEqual(
            standard[0],
            thick.metadata["table_descriptors"]["effect"].structure_fingerprint)

    def test_known_66_bases_have_expected_identity(self):
        expected = {
            QINGER: ("清儿吕布传", 1413120,
                     "88EC708A24718868090F70256D09086E83C92FAB0613C7B8D3742EF974D3564D"),
            CAOCAO: ("新改曹操傳6.6修正版", 1130496,
                    "4A4FD8DDBF83E5F0B769D1B97BF8F6E6431C3AB42892024A354228212D3D06A4"),
        }
        for path, (name, size, sha) in expected.items():
            with self.subTest(path=path):
                profile = detect_engine(str(path), version_part=6)
                self.assertEqual(name, profile.variant_name)
                self.assertEqual(size, profile.file_size)
                self.assertEqual(sha, profile.sha256)
                self.assertEqual(6, profile.version)
                self.assertEqual(0x30, profile.limits["battle_stride"])
                self.assertEqual(255, profile.limits["item_count"])
                self.assertEqual(255, profile.limits["item_record_count"])
                self.assertEqual(160, profile.limits["equipment_count"])
                self.assertEqual(200, profile.limits["warehouse_count"])
                self.assertEqual(0x10, profile.limits["effect_stride"])
                self.assertEqual(4, profile.limits["effect_character_slots"])
                self.assertEqual(2, profile.limits["effect_job_slots"])
                self.assertTrue(profile.limits["effect_value_per_slot"])

    def test_public_66_signatures_and_patch_sites_match_both_bases(self):
        matrices = []
        recipes = []
        for path in (QINGER, CAOCAO):
            profile = detect_engine(str(path), version_part=6)
            matrices.append({key: capability.state
                             for key, capability in profile.capabilities.items()})
            recipes.append({key: capability.recipe_id
                            for key, capability in profile.capabilities.items()
                            if capability.statically_verified})
            for key in ("battle", "person", "effect", "fatal", "revive",
                        "warehouse", "variables_bool", "variables_int",
                        "variables_ptr"):
                self.assertEqual(CapabilityState.STATIC_VERIFIED,
                                 profile.capability(key).state)
                self.assertFalse(profile.capability(key).available)
            for key in ("turn_refresh_position",):
                self.assertEqual(CapabilityState.UNAVAILABLE,
                                 profile.capability(key).state)
            for key in ("control_all_factions", "control_auto_return",
                        "turn_refresh_direction", "item_consumables"):
                self.assertEqual(CapabilityState.STATIC_VERIFIED,
                                 profile.capability(key).state)
            self.assertEqual(0x004092C0, profile.remote_calls["revive"].address)
            self.assertEqual(0x004075DF, profile.remote_calls["recalculate"].address)
            self.assertEqual(0x0041D9D1, profile.remote_calls["weather"].address)
            turn = profile.remote_calls["turn_refresh_direction"]
            self.assertEqual(0x004575B8, turn.address)
            self.assertEqual(24, turn.callee_cleanup)
            self.assertEqual("turn-66-six-args-v1", turn.wrapper_kind)
            self.assertEqual(0x00500C37, profile.addresses["fatal_pointer"])
            self.assertEqual(0x00500C3B,
                             profile.addresses["effect_assign_pointer"])
            self.assertEqual(0, profile.addresses["effect_assign"])
            self.assertEqual(0x0050E000, profile.addresses["exclusive_set"])
            self.assertEqual(
                {0x43D574, 0x43D5A5, 0x43E359, 0x44E748,
                 0x44E754, 0x455379},
                {site.address for site in profile.patches["control_friendly"].sites})
            move_patch = profile.patches["control_move_255"]
            self.assertEqual((6,), move_patch.instruction_lengths)
            self.assertEqual(6, len(move_patch.sites[0].original))
            faction_patch = profile.patches["control_all_factions"]
            self.assertEqual(
                (0x004242AA, 0x00437BC0, 0x00454405, 0x00455646),
                tuple(site.address for site in faction_patch.sites))
            self.assertEqual(
                (b"\x74\x0F", b"\x74\x34", b"\x75\x27", b"\x75\x23"),
                tuple(site.original for site in faction_patch.sites))
            self.assertEqual(
                (b"\x90\x90", b"\x90\x90", b"\x90\x90", b"\xEB\x23"),
                tuple(site.patched for site in faction_patch.sites))
            auto_return = profile.patches["control_auto_return"]
            self.assertEqual(
                (0x00412D82, 0x0043D529, 0x0043D540),
                tuple(site.address for site in auto_return.sites))
            self.assertEqual(
                (b"\x74\x13", b"\x74\x02", b"\x74\x02"),
                tuple(site.original for site in auto_return.sites))
            self.assertEqual(
                (b"\x90\x90", b"\x90\x90", b"\x90\x90"),
                tuple(site.patched for site in auto_return.sites))
            self.assertEqual((2, 2, 2), auto_return.instruction_lengths)
        self.assertEqual(matrices[0], matrices[1])
        self.assertEqual(recipes[0], recipes[1])

    def test_effect_assignment_66_codec_preserves_six_independent_values(self):
        expected = EffectAssignmentRow66(
            (EffectAssignmentSlot66(1, 0),
             EffectAssignmentSlot66(1023, 255),
             EffectAssignmentSlot66(1024, 17),
             EffectAssignmentSlot66(148, 20)),
            (EffectAssignmentSlot66(72, 30),
             EffectAssignmentSlot66(255, 20)))
        encoded = encode_effect_assignment_row_66(expected)
        self.assertEqual(EFFECT_ROW_SIZE_66, len(encoded))
        self.assertEqual(expected, parse_effect_assignment_row_66(encoded))
        with self.assertRaises(ValueError):
            parse_effect_assignment_row_66(encoded[:-1])

    def test_effect_assignment_66_layout_matches_both_known_executables(self):
        for path in (QINGER, CAOCAO):
            with self.subTest(path=path):
                image = PEImage(str(path))
                profile = detect_engine(str(path), version_part=6)
                pointer = struct.unpack(
                    "<I", image.read_va(
                        profile.addresses["effect_assign_pointer"], 4))[0]
                self.assertEqual(0x00511000, pointer)
                row = parse_effect_assignment_row_66(
                    image.read_va(pointer, EFFECT_ROW_SIZE_66))
                self.assertEqual(4, len(row.characters))
                self.assertEqual(2, len(row.jobs))
                self.assertEqual("effect-assignment-semantic-16",
                                 profile.capability("effect").recipe["kind"])
                self.assertEqual(0x10,
                                 profile.capability("effect").recipe["rowSize"])
                self.assertNotEqual(0x00507800, pointer)
                descriptor = profile.metadata["table_descriptors"]["effect"]
                self.assertIsInstance(descriptor, ResolvedTableDescriptor)
                self.assertEqual(
                    {SemanticAnchorRole.INITIALIZER,
                     SemanticAnchorRole.READER,
                     SemanticAnchorRole.WRITER}, set(descriptor.roles))
                self.assertEqual(profile.addresses["effect_assign_pointer"],
                                 descriptor.pointer_reference)
                if path == QINGER:
                    self.assertEqual(148, row.characters[3].target_id)
                else:
                    self.assertEqual((72, 30),
                                     (row.jobs[0].target_id,
                                      row.jobs[0].effect_value))
                    self.assertEqual((75, 20),
                                     (row.jobs[1].target_id,
                                      row.jobs[1].effect_value))

    def test_effect_assignment_runtime_probe_dereferences_the_table_pointer(self):
        profile = detect_engine(str(CAOCAO), version_part=6)
        base = profile.preferred_image_base
        table = 0x00511000
        data = bytearray(b"\0" * profile.image_size)
        pointer_offset = profile.addresses["effect_assign_pointer"] - base
        data[pointer_offset:pointer_offset + 4] = struct.pack("<I", table)
        row = EffectAssignmentRow66(
            tuple(EffectAssignmentSlot66(1024, 1) for _ in range(4)),
            (EffectAssignmentSlot66(255, 1),
             EffectAssignmentSlot66(255, 1)))
        row_offset = table - base
        data[row_offset:row_offset + 16] = encode_effect_assignment_row_66(row)
        backend = FakeMemoryBackend(base, bytes(data))
        session = MemorySession(1, base, len(data), backend=backend,
                                owns_handle=False,
                                reference_image_base=base)
        profile.probe_runtime(session)
        self.assertEqual(table, profile.addresses["effect_assign"])
        self.assertNotIn("effect", profile.runtime_probe_failures)
        descriptor = profile.metadata["table_descriptors"]["effect"]
        self.assertEqual(table, descriptor.runtime_base)
        self.assertTrue(descriptor.generation)
        self.assertTrue(descriptor.page_ranges)
        backend.write(profile.addresses["effect_assign_pointer"], b"\0" * 4)
        profile.probe_runtime(session)
        self.assertEqual(0, profile.addresses["effect_assign"])
        self.assertIn("effect", profile.runtime_probe_failures)

    def test_effect_assignment_runtime_probe_accepts_v716_empty_character(self):
        profile = detect_engine(str(CAOCAO), version_part=6)
        base = profile.preferred_image_base
        table = 0x00511000
        data = bytearray(b"\0" * profile.image_size)
        pointer_offset = profile.addresses["effect_assign_pointer"] - base
        data[pointer_offset:pointer_offset + 4] = struct.pack("<I", table)
        row = EffectAssignmentRow66(
            (EffectAssignmentSlot66(31, 20),
             EffectAssignmentSlot66(20, 25),
             EffectAssignmentSlot66(1025, 30),
             EffectAssignmentSlot66(1025, 0)),
            (EffectAssignmentSlot66(30, 20),
             EffectAssignmentSlot66(255, 0)))
        row_offset = table - base
        data[row_offset:row_offset + 16] = encode_effect_assignment_row_66(row)
        backend = FakeMemoryBackend(base, bytes(data))
        session = MemorySession(1, base, len(data), backend=backend,
                                owns_handle=False,
                                reference_image_base=base)
        profile.probe_runtime(session)
        self.assertEqual(table, profile.addresses["effect_assign"])
        self.assertEqual((1024, 1024, 1025, 1025),
                         profile.metadata["effect_empty_characters"])
        self.assertNotIn("effect", profile.runtime_probe_failures)

        invalid = EffectAssignmentRow66(
            tuple(EffectAssignmentSlot66(1026, 1) for _ in range(4)),
            row.jobs)
        backend.write(table, encode_effect_assignment_row_66(invalid))
        profile.probe_runtime(session)
        self.assertEqual(table, profile.addresses["effect_assign"])
        self.assertEqual((1026, 1026, 1026, 1026),
                         profile.metadata["effect_empty_characters"])
        self.assertNotIn("effect", profile.runtime_probe_failures)

    def test_effect_assignment_runtime_probe_accepts_mixed_empty_characters(self):
        profile = detect_engine(str(CAOCAO), version_part=6)
        base = profile.preferred_image_base
        table = 0x00511000
        data = bytearray(b"\0" * profile.image_size)
        pointer_offset = profile.addresses["effect_assign_pointer"] - base
        data[pointer_offset:pointer_offset + 4] = struct.pack("<I", table)
        row = EffectAssignmentRow66(
            (EffectAssignmentSlot66(1024, 20),
             EffectAssignmentSlot66(1024, 25),
             EffectAssignmentSlot66(1024, 30),
             EffectAssignmentSlot66(1025, 0)),
            (EffectAssignmentSlot66(255, 20),
             EffectAssignmentSlot66(255, 0)))
        encoded = encode_effect_assignment_row_66(row)
        row_offset = table - base
        data[row_offset:row_offset + 255 * 16] = encoded * 255
        backend = FakeMemoryBackend(base, bytes(data))
        session = MemorySession(1, base, len(data), backend=backend,
                                owns_handle=False,
                                reference_image_base=base)
        profile.probe_runtime(session)
        self.assertEqual(table, profile.addresses["effect_assign"])
        self.assertEqual((1024, 1024, 1024, 1025),
                         profile.metadata["effect_empty_characters"])
        self.assertNotIn("effect", profile.runtime_probe_failures)

    def test_v716_save_files_contain_lossless_1024_1025_effect_tables(self):
        save_root = V716.parent / "SV"
        expected_high = {
            "SV000.E5S": ((1025, 172), (1025, 188),
                          (1025, 204), (1025, 255)),
            "SV001.E5S": ((1024, 255), (1024, 255),
                          (1024, 255), (1025, 255)),
        }
        for file_name, expected in expected_high.items():
            with self.subTest(file_name=file_name):
                raw = (save_root / file_name).read_bytes()[
                    0x10000:0x10000 + 255 * EFFECT_ROW_SIZE_66]
                self.assertEqual(255 * EFFECT_ROW_SIZE_66, len(raw))
                rows = [parse_effect_assignment_row_66(
                    raw[index*EFFECT_ROW_SIZE_66:
                        (index+1)*EFFECT_ROW_SIZE_66])
                    for index in range(255)]
                for slot, (empty_value, empty_count) in enumerate(expected):
                    high = [row.characters[slot].target_id for row in rows
                            if row.characters[slot].target_id >= 1024]
                    self.assertEqual(empty_count, len(high))
                    self.assertEqual({empty_value}, set(high))
                first = rows[0]
                changed = EffectAssignmentRow66(
                    (EffectAssignmentSlot66(
                        first.characters[0].target_id,
                        (first.characters[0].effect_value + 1) & 0xFF),) +
                    first.characters[1:], first.jobs)
                reread = parse_effect_assignment_row_66(
                    encode_effect_assignment_row_66(changed))
                self.assertEqual(
                    tuple(item.target_id for item in first.characters),
                    tuple(item.target_id for item in reread.characters))
    def test_single_modified_patch_remains_unavailable_without_recipe(self):
        source = PEImage(str(CAOCAO))
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "Ekd5.exe"
            shutil.copyfile(CAOCAO, target)
            data = bytearray(target.read_bytes())
            data[source.va_to_offset(0x0044E9D3)] = 0x75
            target.write_bytes(data)
            profile = detect_engine(str(target), version_part=6)
            self.assertEqual("未知 6.6 派生版", profile.variant_name)
            self.assertEqual(CapabilityState.UNAVAILABLE,
                             profile.capability("control_no_ai").state)
            self.assertFalse(profile.force_unverified_66)
            self.assertFalse(profile.can_write("control_no_ai"))
            self.assertEqual(CapabilityState.STATIC_VERIFIED,
                             profile.capability("control_friendly").state)
            self.assertTrue(profile.capability("battle").statically_verified)
            self.assertTrue(profile.capability("person").statically_verified)

    def test_summary_reports_composite_access_surface(self):
        profile = detect_engine(str(CAOCAO), version_part=6)
        summary = profile.summary()
        self.assertIn("可读写", summary)
        self.assertIn("只读", summary)
        self.assertIn("天赋:16B/4+2/指针", summary)

    def test_direct_mode_bypasses_evidence_but_not_missing_recipes(self):
        for path in (QINGER, CAOCAO):
            with self.subTest(path=path):
                profile = detect_engine(str(path), version_part=6)
                self.assertFalse(profile.force_unverified_66)
                self.assertTrue(profile.direct_write)
                self.assertFalse(profile.can_write("turn_refresh_position"))
                self.assertTrue(profile.can_write("recalculate"))
                self.assertTrue(profile.can_read("effect"))
                self.assertTrue(profile.can_write("effect"))
                self.assertFalse(profile.capability(
                    "control_all_factions").available)
                self.assertEqual(list(range(160, 200)),
                                  profile.metadata["consumable_item_ids"])
                self.assertNotIn("forced_consumable_mapping", profile.metadata)
                mapping = profile.metadata["consumable_mapping"]
                self.assertEqual(0x00493AF0,
                                 profile.addresses["consumable_counts"])
                self.assertEqual((160, 199, 352, 1, 1),
                                 (mapping["idMin"], mapping["idMax"],
                                  mapping["storageCount"], mapping["stride"],
                                  mapping["width"]))
                layout = profile.metadata["variable_layouts"]["variables_bool"]
                self.assertIsNone(layout["countOffset"])
                self.assertEqual(0, layout["dataOffset"])
                self.assertEqual(1, layout["stride"])
                self.assertEqual(1, layout["width"])
                self.assertEqual("bitset", layout["storage"])
                self.assertEqual("msb0", layout["bitOrder"])
                self.assertEqual(512, layout["storageBytes"])
                auto_return = profile.metadata["auto_return_layout"]
                self.assertEqual("native-second-action-unrestricted",
                                 auto_return["mode"])
                self.assertEqual([0x00412D7B, 0x0043D522],
                                 auto_return["callSites"])
                self.assertEqual(0x00421175, auto_return["nativeEffect"])
                self.assertEqual(
                    [0x00412D82, 0x0043D529, 0x0043D540],
                    auto_return["patchSites"])
                image = PEImage(str(path))
                for address, expected in zip(auto_return["patchSites"],
                                             auto_return["originalBytes"]):
                    self.assertEqual(expected,
                                     image.read_va(address, len(expected)))

    def test_revive_entry_addresses_are_profile_scoped(self):
        profile65 = detect_composite_profile(str(BASE65), version_part=5)
        self.assertEqual(0x004092C7, profile65.revive_entry_address())
        for path in (QINGER, CAOCAO):
            with self.subTest(path=path):
                profile66 = detect_engine(str(path), version_part=6)
                self.assertEqual(0x004092C0,
                                 profile66.revive_entry_address())
                self.assertTrue(profile66.can_write("revive"))
                self.assertFalse(profile66.remote_calls["revive"].abi_verified)

    def test_auto_return_native_patch_matches_both_66_bases(self):
        recipes = []
        for path in (QINGER, CAOCAO):
            spec, contract, reason = locate_auto_return_patch_66(
                PEImage(str(path)))
            self.assertEqual("", reason)
            self.assertIsNotNone(spec)
            self.assertEqual("control_auto_return", spec.key)
            self.assertEqual((0x00412D82, 0x0043D529, 0x0043D540),
                             tuple(site.address for site in spec.sites))
            self.assertEqual([0x00412D7B, 0x0043D522],
                             contract["callSites"])
            self.assertEqual(0x00421175, contract["nativeEffect"])
            recipes.append(spec.recipe())
        self.assertEqual(recipes[0], recipes[1])

    def test_modified_auto_return_signature_disables_only_that_feature(self):
        source = PEImage(str(CAOCAO))
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "Ekd5.exe"
            shutil.copyfile(CAOCAO, target)
            data = bytearray(target.read_bytes())
            data[source.va_to_offset(0x00412D82)] = 0x75
            target.write_bytes(data)
            profile = detect_engine(str(target), version_part=6)
            self.assertNotIn("control_auto_return", profile.patches)
            self.assertEqual(CapabilityState.UNAVAILABLE,
                             profile.capability("control_auto_return").state)
            self.assertEqual(CapabilityState.STATIC_VERIFIED,
                             profile.capability("control_friendly").state)

    def test_consumable_count_signatures_match_both_66_bases(self):
        recipes = []
        for path in (QINGER, CAOCAO):
            base, mapping, reason = locate_consumable_mapping_66(
                PEImage(str(path)))
            self.assertEqual("", reason)
            self.assertEqual(0x00493AF0, base)
            self.assertEqual(list(range(160, 200)), mapping["itemIds"])
            self.assertEqual(0x00420299, mapping["locations"]["update"])
            self.assertEqual(0x004202C4, mapping["locations"]["read"])
            self.assertEqual(0x00421894, mapping["locations"]["write"])
            self.assertEqual(0x0042BB2D, mapping["locations"]["clear"])
            recipes.append(mapping)
        self.assertEqual(recipes[0], recipes[1])

    def test_runtime_probe_uses_compact_bool_and_ui_consumable_ranges(self):
        profile = detect_engine(str(CAOCAO), version_part=6)

        class ProbeMemory:
            def __init__(self):
                self.ranges = []

            def read_bytes(self, address, size):
                for patch in profile.patches.values():
                    for site in patch.sites:
                        if site.address == address and len(site.original) == size:
                            return site.original
                for patch in profile.dynamic_patches.values():
                    for site in patch.call_sites:
                        if site.address == address and len(site.original) == size:
                            return site.original
                for call in profile.remote_calls.values():
                    if call.address == address and len(call.signature) == size:
                        return call.signature
                return b"\0" * size

            def read_u16(self, _address):
                return 0xFFFF

            def read_u32(self, address):
                if address == profile.addresses["bool_vars"]:
                    raise AssertionError("6.6布尔位图没有数量头")
                if address == profile.addresses["person_pointer"]:
                    return 0x10000000
                if address == profile.addresses["fatal_pointer"]:
                    return 0x10010000
                if address == profile.addresses["effect_assign_pointer"]:
                    return 0x10020000
                return 0

            def validate_range(self, address, size, write=False):
                self.ranges.append((address, size, write))
                return True

            def validate_actual_range(self, _address, _size, write=False):
                return True

            def read_actual_bytes(self, _address, size):
                return b"\0" * size

            def is_dynamic_patch_enabled(self, _key):
                return False

        memory = ProbeMemory()
        profile.probe_runtime(memory)
        self.assertIn((profile.addresses["bool_vars"], 512, True),
                      memory.ranges)
        self.assertIn((profile.addresses["consumable_counts"], 352, True),
                      memory.ranges)

    def test_auto_return_patch_relocates_and_restores_three_sites(self):
        profile = detect_engine(str(CAOCAO), version_part=6)
        spec = profile.patches["control_auto_return"]
        actual_base = 0x00700000
        data = bytearray(b"\0" * profile.image_size)
        for site in spec.sites:
            offset = site.address - profile.preferred_image_base
            data[offset:offset+len(site.original)] = site.original
        backend = FakeMemoryBackend(actual_base, bytes(data))
        session = MemorySession(
            1, actual_base, len(data), backend=backend, owns_handle=False,
            reference_image_base=profile.preferred_image_base)
        session.set_patch(spec, True)
        for site in spec.sites:
            actual_site = actual_base + site.address - profile.preferred_image_base
            self.assertEqual(site.patched,
                             backend.read(actual_site, len(site.patched)))
        self.assertEqual({}, session._dynamic_leases)
        session.set_patch(spec, False)
        for site in spec.sites:
            actual_site = actual_base + site.address - profile.preferred_image_base
            self.assertEqual(site.original,
                             backend.read(actual_site, len(site.original)))

    def test_auto_return_third_site_failure_keeps_first_two_direct_writes(self):
        profile = detect_engine(str(CAOCAO), version_part=6)
        spec = profile.patches["control_auto_return"]
        base = profile.preferred_image_base
        data = bytearray(b"\0" * profile.image_size)
        for site in spec.sites:
            offset = site.address - base
            data[offset:offset+len(site.original)] = site.original
        backend = FakeMemoryBackend(base, bytes(data))
        session = MemorySession(1, base, len(data), backend=backend,
                                owns_handle=False,
                                reference_image_base=base)
        backend.fail_write_at = spec.sites[2].address
        with self.assertRaises(MemoryAccessError):
            session.set_patch(spec, True)
        for index, site in enumerate(spec.sites):
            expected = site.patched if index < 2 else site.original
            self.assertEqual(expected,
                             backend.read(site.address, len(expected)))

    def test_known_sha_table_is_complete(self):
        self.assertEqual(2, len(KNOWN_66))
        self.assertEqual(2, len(KNOWN_66_VARIANTS))

    def test_legacy_profiles_keep_normal_enabled_semantics(self):
        profile = legacy_profile(0)
        self.assertEqual("6.4", profile.display_version)
        self.assertFalse(profile.force_unverified_66)
        self.assertTrue(profile.can_use("battle"))
        self.assertTrue(profile.capability("battle").available)

    def test_64_and_65_profiles_are_independent(self):
        profile64 = legacy_profile(EngineVersion.V64)
        profile65 = legacy_profile(EngineVersion.V65)
        self.assertEqual(tuple(range(255)), profile65.item_ids("record"))
        self.assertEqual(tuple(range(150)), profile65.item_ids("equipment"))
        self.assertEqual(tuple(range(150, 255)),
                         profile65.item_ids("consumable"))
        self.assertTrue(profile65.is_item_id(254))
        self.assertFalse(profile65.is_item_id(255))
        self.assertIsNot(profile64, profile65)
        self.assertEqual("6.4", profile64.display_version)
        self.assertEqual("6.5", profile65.display_version)
        layout64 = profile64.resolved_feature("effect").layout
        layout65 = profile65.resolved_feature("effect").layout
        self.assertFalse(layout64.pointer_backed)
        self.assertEqual(0x00508998, layout64.reference)
        self.assertTrue(layout65.pointer_backed)
        self.assertEqual(0x00500C3B, layout65.reference)
        self.assertNotEqual(profile64.capability("effect").recipe_id,
                            profile65.capability("effect").recipe_id)

    def test_composite_64_and_65_effect_layouts_use_distinct_resolvers(self):
        profile64 = detect_composite_profile(str(BASE64), version_part=4)
        profile65 = detect_composite_profile(str(BASE65), version_part=5)
        layout64 = profile64.resolved_feature("effect").layout
        layout65 = profile65.resolved_feature("effect").layout
        self.assertEqual(EngineVersion.V64, profile64.engine_version)
        self.assertEqual(EngineVersion.V65, profile65.engine_version)
        self.assertEqual(0x00508998, layout64.reference)
        self.assertFalse(layout64.pointer_backed)
        self.assertEqual(0x00500C3B, layout65.reference)
        self.assertTrue(layout65.pointer_backed)
        self.assertEqual(255, layout65.record_count)
        self.assertEqual(180, profile65.limits["effect_count"])

    def test_65_runtime_probe_dereferences_255_by_8_effect_table(self):
        profile = detect_composite_profile(str(BASE65), version_part=5)
        base = profile.preferred_image_base
        data = bytearray(b"\0" * profile.image_size)
        table = 0x00507800
        pointer_offset = profile.addresses["effect_assign_pointer"] - base
        data[pointer_offset:pointer_offset + 4] = struct.pack("<I", table)
        first_row = struct.pack("<HHHBB", 1, 2, 3, 4, 5)
        row_offset = table - base
        data[row_offset:row_offset + 8] = first_row
        backend = FakeMemoryBackend(base, bytes(data))
        memory = MemorySession(
            1, base, len(data), backend=backend, owns_handle=False,
            reference_image_base=base)
        profile.probe_runtime(memory)
        self.assertEqual(table, profile.addresses["effect_assign"])
        self.assertEqual(FeatureAccess.READ_WRITE,
                         profile.resolved_feature("effect").access)
        self.assertEqual(first_row, memory.read_actual_bytes(table, 8))

    def test_consumable_mapping_matrix_covers_standard_hybrid_and_65(self):
        cases = (
            (CAOCAO, EngineVersion.V66, 0x00493AF0, 160, 40, 160),
            (HOUHEI, EngineVersion.V66, 0x00510C80, 150, 105, 150),
            (BASE65, EngineVersion.V65, 0x00510C80, 150, 105, 150),
            (BASE64, EngineVersion.V64, 0x00510C80, 150, 105, 150),
        )
        for path, version, base, first_id, ui_count, equipment_count in cases:
            with self.subTest(path=path):
                spec, reason = locate_consumable_mapping_composite(
                    PEImage(str(path)), version)
                self.assertEqual("", reason)
                self.assertEqual(base, spec.base)
                self.assertEqual(first_id, spec.id_min)
                self.assertEqual(ui_count, len(spec.ui_item_ids))
                self.assertEqual(equipment_count, spec.equipment_count)
        spec, reason = locate_consumable_mapping_composite(
            PEImage(str(PL65)), EngineVersion.V65)
        self.assertIsNone(spec)
        self.assertIn("update", reason)

    def test_item_row_codec_follows_the_resolved_engine_family(self):
        standard66_profiles = [detect_engine(str(path), version_part=6)
                               for path in (CAOCAO, QINGER, V716, LIEYAN)]
        houhei = detect_engine(str(HOUHEI), version_part=6)
        standard65 = detect_composite_profile(str(BASE65), version_part=5)
        for profile in standard66_profiles:
            self.assertEqual(ITEM_CODEC_66,
                             profile.metadata["item_row_codec"])
            self.assertTrue(profile.item_row(0).name)
        self.assertEqual(0xD1, standard66_profiles[0].item_row(0).icon)
        self.assertEqual(ITEM_CODEC_65, houhei.metadata["item_row_codec"])
        self.assertEqual("恢复用豆", houhei.item_row(150).name)
        self.assertEqual(ITEM_CODEC_65,
                         standard65.metadata["item_row_codec"])
        self.assertEqual("恢复用豆", standard65.item_row(150).name)

    def test_item_row_codec_uses_its_own_index_function_evidence(self):
        cases = (
            (BASE65, ITEM_CODEC_65, 0x004A1140),
            (HOUHEI, ITEM_CODEC_65, 0x004A1140),
            (CAOCAO, ITEM_CODEC_66, 0x0049F820),
            (QINGER, ITEM_CODEC_66, 0x0049F820),
            (V716, ITEM_CODEC_66, 0x0049F820),
            (LIEYAN, ITEM_CODEC_66, 0x0049F820),
        )
        for path, expected_codec, expected_base in cases:
            with self.subTest(path=path):
                codec, base, hits, reason = locate_item_row_codec(
                    PEImage(str(path)))
                self.assertEqual("", reason)
                self.assertEqual(expected_codec, codec)
                self.assertEqual(expected_base, base)
                self.assertIn("lookup", hits)

    def test_66_item_codec_is_not_inferred_from_consumable_boundary(self):
        legacy_consumables = ConsumableMappingSpec(
            "synthetic-legacy-boundary", 0x00510C80, 150, 105,
            tuple(range(150, 255)), 150)
        with patch("engine_profile.locate_consumable_mapping_composite",
                   return_value=(legacy_consumables, "")):
            profile = detect_engine(str(CAOCAO), version_part=6)
        self.assertEqual(150, profile.limits["equipment_count"])
        self.assertEqual(ITEM_CODEC_66,
                         profile.metadata["item_row_codec"])
        self.assertEqual(0x0049F820, profile.addresses["item_table"])

    def test_65_packed_variant_does_not_fall_back_to_fixed_item_table(self):
        profile = detect_composite_profile(str(PL65), version_part=5)
        self.assertEqual(0, profile.addresses["item_table"])
        self.assertFalse(profile.resolved_feature("item_metadata").readable)
        self.assertTrue(profile.can_write("item_bulk_treasure"))
        self.assertEqual(CapabilityState.UNAVAILABLE,
                         profile.capability("item_bulk_treasure").state)
        self.assertNotIn("item_row_codec", profile.metadata)

    def test_pe_read_does_not_cross_from_file_backed_bytes_into_bss(self):
        image = PEImage(str(BASE65))
        self.assertEqual(16, len(image.read_va(0x00485DF0, 16)))
        with self.assertRaises(ProfileError):
            image.read_va(0x00485DF0, 32)
        with self.assertRaises(ProfileError):
            image.read_va(0x00485E00, 1)

    def test_effect_and_fatal_name_sources_have_66_record_counts(self):
        for path in (QINGER, CAOCAO):
            with self.subTest(path=path):
                with path.open("rb") as stream:
                    stream.seek(0x9E800)
                    effects = stream.read(255 * 16)
                self.assertEqual(255 * 16, len(effects))
                self.assertTrue(all(effects[index * 16] != 0 for index in range(255)))
                first_effect = effects[:16].split(b"\0", 1)[0].decode("gbk")
                self.assertEqual("每回合恢复HP", first_effect)
                with path.with_name("Data.e5").open("rb") as stream:
                    stream.seek(59540)
                    fatal = stream.read(80 * 16)
                self.assertEqual(80 * 16, len(fatal))
                self.assertEqual("重击", fatal[:11].split(b"\0", 1)[0].decode("gbk"))

    def test_effect_name_catalog_is_profile_scoped_for_65_and_66(self):
        cases = (
            (BASE65, EngineVersion.V65, "步兵攻击", "如履平地",
             "FE特效", 180),
            (DONGWU65, EngineVersion.V65, "步兵攻击", "如履平地",
             "FE特效", 180),
            (PL65, EngineVersion.V65, "恶路移动", "溯回归时引",
             "穿透攻击-环击", 180),
            (CAOCAO, EngineVersion.V66, "恶路移动", "B3",
             "FE特效", 255),
        )
        catalogs = []
        for (path, engine_version, name63, name179, name254,
             ui_count) in cases:
            with self.subTest(path=path):
                if engine_version == EngineVersion.V66:
                    profile = detect_engine(str(path), version_part=6)
                else:
                    profile = detect_composite_profile(
                        str(path), version_part=5)
                names = profile.effect_name_catalog()
                catalogs.append(names)
                self.assertEqual(EFFECT_NAME_RECORD_COUNT_65_66, len(names))
                self.assertEqual("每回合恢复HP", names[0])
                self.assertEqual(name63, names[63])
                self.assertEqual(name179, names[179])
                self.assertEqual(name254, names[254])
                self.assertEqual(ui_count, profile.limits["effect_count"])
                source = profile.metadata["effect_name_source"]
                self.assertEqual(EFFECT_NAME_POOL_OFFSET_65_66,
                                 source["offset"])
                self.assertEqual(
                    PEImage(str(path)).offset_to_va(
                        EFFECT_NAME_POOL_OFFSET_65_66),
                    profile.addresses["effect_names"])
        self.assertNotEqual(catalogs[0][63], catalogs[2][63])


if __name__ == "__main__":
    unittest.main()
