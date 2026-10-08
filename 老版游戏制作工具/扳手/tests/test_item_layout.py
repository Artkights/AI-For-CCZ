import tempfile
import unittest
from pathlib import Path

from engine_profile import (ITEM_CODEC_65, ITEM_CODEC_66, ProfileError,
                            load_item_table_66, parse_item_row_65,
                            parse_item_row_66, treasure_ids_66,
                            validate_item_table_66)


WORKSPACE = Path(__file__).resolve().parents[3]


class ItemLayout66Tests(unittest.TestCase):
    @staticmethod
    def row(name, icon, effect11, effect12, catalog):
        row = bytearray(0x19)
        encoded = name.encode("gbk")
        row[:min(15, len(encoded))] = encoded[:15]
        row[0x0F] = icon
        row[0x11] = effect11
        row[0x12] = effect12
        row[0x13] = 7
        row[0x15:0x19] = bytes((8, 9, 10, catalog))
        return bytes(row)

    def test_15_byte_name_and_66_field_offsets(self):
        row = self.row("十五字节名称", 31, 41, 42, 1)
        item = parse_item_row_66(row)
        self.assertEqual("十五字节名称", item.name)
        self.assertEqual(31, item.icon)
        self.assertEqual(41, item.effect_or_type)
        self.assertEqual(42, item.equipment_effect_or_category)
        self.assertEqual(7, item.price)
        self.assertEqual(8, item.ability)
        self.assertEqual(9, item.effect_value)
        self.assertEqual(10, item.growth)
        self.assertTrue(item.is_treasure)

    def test_17_byte_name_and_65_field_offsets(self):
        row = bytearray(0x19)
        row[:17] = "八字物品名称".encode("gbk").ljust(17, b"\0")
        row[0x11:0x19] = bytes((41, 42, 7, 31, 8, 9, 10, 1))
        item = parse_item_row_65(bytes(row))
        self.assertEqual("八字物品名称", item.name)
        self.assertEqual(31, item.icon)
        self.assertEqual(41, item.effect_or_type)
        self.assertEqual(42, item.equipment_effect_or_category)
        self.assertEqual(7, item.price)
        self.assertEqual(8, item.ability)
        self.assertEqual(9, item.effect_value)
        self.assertEqual(10, item.growth)
        self.assertTrue(item.is_treasure)

    def test_treasure_filter_stops_at_the_66_equipment_boundary(self):
        rows = [self.row("普通装备", 1, 0, 1, 0) for _ in range(255)]
        rows[12] = self.row("前段宝物", 2, 0, 0, 1)
        rows[159] = self.row("末件辅助", 3, 0, 0, 1)
        rows[180] = self.row("道具伪标记", 4, 0, 0, 1)
        self.assertEqual([12, 159], treasure_ids_66(b"".join(rows)))

    def test_zero_filled_runtime_style_table_fails_semantic_probe(self):
        with self.assertRaisesRegex(ProfileError, "全零"):
            validate_item_table_66(b"\0" * (255 * 0x19))

    def test_unknown_item_row_codec_is_rejected_before_parsing(self):
        with self.assertRaisesRegex(ProfileError, "未知物品行编解码器"):
            validate_item_table_66(b"\1" * (255 * 0x19), "guessed-layout")

    def test_split_data_and_star_sources_are_joined_at_id_104(self):
        rows = [self.row("物品%d" % index, index & 0xFF, 0, 0,
                         1 if index in (12, 180) else 0)
                for index in range(255)]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            low = bytearray(33164 + 104 * 0x19)
            low[33164:] = b"".join(rows[:104])
            (root / "Data.e5").write_bytes(low)
            (root / "Star.e5").write_bytes(b"".join(rows[104:]))
            table = load_item_table_66(str(root))
        self.assertEqual("物品103", parse_item_row_66(
            table[103*0x19:104*0x19]).name)
        self.assertEqual("物品104", parse_item_row_66(
            table[104*0x19:105*0x19]).name)
        self.assertEqual([12], treasure_ids_66(table))

    def test_short_star_file_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Data.e5").write_bytes(b"\0" * (33164 + 104 * 0x19))
            (root / "Star.e5").write_bytes(b"\0" * 16)
            with self.assertRaisesRegex(ProfileError, "Star.e5 长度不足"):
                load_item_table_66(str(root))

    def test_local_66_bases_have_complete_split_item_tables(self):
        expected = {
            "新改曹操傳6.6修正版":
                ("短剑", 98, list(range(152, 159))),
            "清儿吕布传":
                ("短剑", 121, list(range(150, 160))),
            "曹操传6.6-V7.16 解压密码：66":
                ("短剑", 110, [150]),
            "烈焰同盟6.6x(新修版)黄线完结+红线5关":
                ("单手剑", 90, []),
        }
        for variant, (first_name, treasure_count, late_ids) in expected.items():
            with self.subTest(variant=variant):
                root = WORKSPACE / "基底" / variant
                table = load_item_table_66(str(root))
                items = [parse_item_row_66(
                    table[index*0x19:(index+1)*0x19]) for index in range(255)]
                treasures = treasure_ids_66(table)
                self.assertEqual(255, len(items))
                self.assertEqual(first_name, items[0].name)
                self.assertTrue(all(item.name for item in items))
                self.assertTrue(all("\r" not in item.name and "\n" not in item.name
                                    for item in items))
                self.assertEqual(treasure_count, len(treasures))
                self.assertEqual(late_ids,
                                 [item for item in treasures if item >= 150])
                self.assertTrue(all(item_id < 160 for item_id in treasures))

    def test_local_65_and_houhei_use_17_byte_item_rows(self):
        cases = (
            ("加强版6.5未加密版", "短剑", "恢复用豆"),
            ("厚黑6.6测（7.9水杯版）", "熟铜剑", "恢复用豆"),
        )
        for variant, first_name, consumable_name in cases:
            with self.subTest(variant=variant):
                root = WORKSPACE / "基底" / variant
                table = load_item_table_66(str(root), ITEM_CODEC_65)
                first = parse_item_row_65(table[:0x19])
                consumable = parse_item_row_65(
                    table[150*0x19:151*0x19])
                self.assertEqual(first_name, first.name)
                self.assertEqual(consumable_name, consumable.name)
                self.assertEqual(0, first.icon)
                self.assertEqual(3, consumable.equipment_effect_or_category)
                self.assertEqual(
                    70, len(treasure_ids_66(table, 150, ITEM_CODEC_65)))

    def test_standard_66_uses_15_byte_reordered_item_rows(self):
        root = WORKSPACE / "基底" / "新改曹操傳6.6修正版"
        table = load_item_table_66(str(root), ITEM_CODEC_66)
        first = parse_item_row_66(table[:0x19])
        self.assertEqual("短剑", first.name)
        self.assertEqual(0xD1, first.icon)
        self.assertEqual(0xFF, first.equipment_effect_or_category)


if __name__ == "__main__":
    unittest.main()
