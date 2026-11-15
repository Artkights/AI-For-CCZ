import unittest

from capability_evidence import CapabilityState, FeatureCapability
from engine_profile import (ITEM_CODEC_65, ITEM_CODEC_66, legacy_profile,
                            ProfileError)
from process_memory import FakeMemoryBackend, MemorySession
from runtime_catalog import hydrate_runtime_item_catalog


def catalog(codec, count=255):
    rows = []
    for index in range(count):
        row = bytearray(25)
        name = ("物品%d" % index).encode("gbk")
        row[:len(name)] = name
        row[0x11] = 2 if index < 150 else 3
        row[0x12] = 0xFF
        row[0x18] = int(index == 1)
        rows.append(bytes(row))
    return b"".join(rows)


class RuntimeCatalogTests(unittest.TestCase):
    def make_profile(self, codec=ITEM_CODEC_66):
        profile = legacy_profile(5)
        profile.addresses["item_table"] = 0x00410000
        profile.metadata["item_row_codec"] = codec
        profile.metadata["item_layout"] = {"lookupAddress": 0x004837C2}
        profile.capabilities["item_metadata"] = FeatureCapability(
            "item_metadata", CapabilityState.UNAVAILABLE,
            "Data.e5/Star.e5 不存在")
        return profile

    def memory(self, raw):
        backend = FakeMemoryBackend(0x00400000, bytes(0x20000))
        backend.write(0x00410000, raw)
        session = MemorySession(
            1, 0x00400000, 0x20000, backend=backend,
            owns_handle=False, read_only=True)
        return session, backend

    def test_absent_files_use_only_proven_runtime_catalog_without_writes(self):
        for codec in (ITEM_CODEC_65, ITEM_CODEC_66):
            with self.subTest(codec=codec):
                profile = self.make_profile(codec)
                session, backend = self.memory(catalog(codec))
                before = session.read_bytes(0x00410000, 255 * 25)
                self.assertTrue(hydrate_runtime_item_catalog(profile, session))
                self.assertEqual("物品1", profile.item_row(1).name)
                self.assertTrue(profile.item_row(1).is_treasure)
                self.assertEqual(before, session.read_bytes(0x00410000, 255 * 25))
                self.assertFalse(hydrate_runtime_item_catalog(profile, session))
                self.assertEqual("runtime-item-catalog",
                                 profile.metadata["item_catalog_source"]["kind"])
                with self.assertRaises(ProfileError):
                    profile.item_row(256)

    def test_uninitialized_or_unproven_runtime_table_is_not_enabled(self):
        profile = self.make_profile()
        session, _backend = self.memory(bytes(255 * 25))
        self.assertFalse(hydrate_runtime_item_catalog(profile, session))
        self.assertNotIn("item_table_66", profile.metadata)
        self.assertIn("runtime_item_catalog_error", profile.metadata)
        profile.metadata["item_layout"].clear()
        self.assertFalse(hydrate_runtime_item_catalog(profile, session))

    def test_existing_file_catalog_is_preserved(self):
        profile = self.make_profile()
        profile.metadata["item_table_66"] = catalog(ITEM_CODEC_66)
        self.assertFalse(hydrate_runtime_item_catalog(profile, None))

    def test_extended_names_do_not_expand_byte_equipment_write_domain(self):
        profile = self.make_profile()
        profile.limits["item_record_count"] = 512
        profile.metadata["consumable_item_ids"] = list(range(256, 512))
        session, _backend = self.memory(catalog(ITEM_CODEC_66, 512))
        self.assertTrue(hydrate_runtime_item_catalog(profile, session))
        self.assertEqual("物品511", profile.metadata["runtime_item_names"][511])
        self.assertEqual(255 * 25, len(profile.item_table_66()))
        self.assertFalse(profile.is_item_id(256, "equipment"))
        self.assertTrue(profile.is_item_id(511, "consumable"))


if __name__ == "__main__":
    unittest.main()
