import hashlib
import struct
import unittest
from types import SimpleNamespace

from engine_layout import (FieldSpec, SemanticAnchorRole)
from semantic_resolver import SemanticAnchorPattern, SemanticTableResolver


class FakeImage:
    image_base = 0x00400000

    def __init__(self, data):
        self.data = bytes(data)
        self.size_of_image = len(self.data)
        self.sha256 = hashlib.sha256(self.data).hexdigest().upper()
        self.sections = [SimpleNamespace(
            executable=True, raw_offset=0, raw_size=len(self.data),
            virtual_address=0)]

    def read_va(self, address, size):
        offset = address - self.image_base
        return self.data[offset:offset + size]


class SemanticResolverTests(unittest.TestCase):
    POINTER = 0x00500C3B
    INIT = "68 ?? ?? ?? ?? 6A 10"
    READER = "6B C0 10 3B C1"
    WRITER = "A1 ?? ?? ?? ?? 88 48 02"

    def make_image(self, duplicate_reader=False, pointer_conflict=False):
        data = bytearray(b"\x90" * 0x200)
        data[0x20:0x27] = b"\x68" + struct.pack("<I", self.POINTER) + b"\x6A\x10"
        data[0x60:0x65] = b"\x6B\xC0\x10\x3B\xC1"
        writer_pointer = self.POINTER + (4 if pointer_conflict else 0)
        data[0xA0:0xA7] = b"\xA1" + struct.pack("<I", writer_pointer) + b"\x88\x48"
        data[0xA7] = 0x02
        if duplicate_reader:
            data[0xD0:0xD5] = b"\x6B\xC0\x10\x3B\xC1"
        # Sentinel-looking values are deliberately unrelated to boundaries.
        data[0x100:0x110] = struct.pack("<HHHHHHHH", 1024, 1025, 0xFFFF,
                                       255, 1024, 1025, 0xFFFF, 255)
        return FakeImage(data)

    def anchors(self, include_writer=True):
        values = [
            SemanticAnchorPattern(
                SemanticAnchorRole.INITIALIZER, self.INIT, 1),
            SemanticAnchorPattern(SemanticAnchorRole.READER, self.READER),
        ]
        if include_writer:
            values.append(SemanticAnchorPattern(
                SemanticAnchorRole.WRITER, self.WRITER, 1))
        return tuple(values)

    def test_three_anchor_descriptor_is_read_write(self):
        descriptor, hits, reason = SemanticTableResolver(
            self.make_image()).resolve(
                "effect", 255, 16, (FieldSpec("character", 0, 2),),
                self.anchors(), person_domain=(0, 1023),
                empty_predicate="character>=person_count")
        self.assertEqual("", reason)
        self.assertIsNotNone(descriptor)
        self.assertTrue(descriptor.writable_semantics)
        self.assertEqual(self.POINTER, descriptor.pointer_reference)
        self.assertEqual({"initializer", "reader", "writer"}, set(hits))
        self.assertEqual(255 * 16, descriptor.byte_size)

    def test_reader_without_writer_produces_read_only_descriptor(self):
        descriptor, _hits, reason = SemanticTableResolver(
            self.make_image()).resolve(
                "effect", 255, 16, (FieldSpec("character", 0, 2),),
                self.anchors(include_writer=False))
        self.assertEqual("", reason)
        self.assertIsNotNone(descriptor)
        self.assertFalse(descriptor.writable_semantics)
        self.assertFalse(descriptor.to_layout("effect-test").writable)

    def test_duplicate_reader_is_rejected_despite_sentinel_values(self):
        descriptor, _hits, reason = SemanticTableResolver(
            self.make_image(duplicate_reader=True)).resolve(
                "effect", 255, 16, (FieldSpec("character", 0, 2),),
                self.anchors())
        self.assertIsNone(descriptor)
        self.assertIn("reader语义锚点匹配数为 2", reason)

    def test_conflicting_pointer_operands_are_rejected(self):
        descriptor, _hits, reason = SemanticTableResolver(
            self.make_image(pointer_conflict=True)).resolve(
                "effect", 255, 16, (FieldSpec("character", 0, 2),),
                self.anchors())
        self.assertIsNone(descriptor)
        self.assertIn("不同表指针", reason)


if __name__ == "__main__":
    unittest.main()
