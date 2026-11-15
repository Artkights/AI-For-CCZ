"""Verify that a PE32 release embeds the exact current business bytecode.

Run with the same Python major/minor version used to build the executable.
The executable is inspected as an archive and is never launched or modified.
"""

import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import marshal
from pathlib import Path
import struct
import sys
from types import CodeType


CRITICAL_MODULES = (
    "main", "mywindow", "compatibility", "hook", "engine_profile", "engine_layout",
    "semantic_resolver", "runtime_regions", "process_memory",
    "capability_evidence", "evidence_collector", "runtime_catalog", "variable_storage",
)
NORMALIZED_FILENAME = "<release-source>"


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def normalize_code(code):
    """Normalize filenames in every nested function, class and comprehension."""
    if not isinstance(code, CodeType):
        raise TypeError("Expected a Python code object")

    def normalize_constant(value):
        if isinstance(value, CodeType):
            return normalize_code(value)
        if isinstance(value, tuple):
            return tuple(normalize_constant(item) for item in value)
        if isinstance(value, frozenset):
            return frozenset(normalize_constant(item) for item in value)
        return value

    return code.replace(
        co_filename=NORMALIZED_FILENAME,
        co_consts=tuple(normalize_constant(value) for value in code.co_consts),
    )


def normalized_code_bytes(code):
    """Serialize exact code structure independently of interning and hash seed.

    Direct marshal(CodeType) preserves object sharing and frozenset iteration
    order. Both may change during packaging without changing bytecode. A
    typed structural record retains every code attribute, orders frozensets,
    and marshal protocol 2 omits object-reference sharing records.
    """
    def constant(value):
        if isinstance(value, CodeType):
            return ("code", record(value))
        if value is None:
            return ("none",)
        if value is Ellipsis:
            return ("ellipsis",)
        if isinstance(value, bool):
            return ("bool", int(value))
        if isinstance(value, int):
            return ("int", value)
        if isinstance(value, float):
            return ("float", struct.pack("<d", value))
        if isinstance(value, complex):
            return ("complex", struct.pack("<dd", value.real, value.imag))
        if isinstance(value, str):
            return ("str", value)
        if isinstance(value, bytes):
            return ("bytes", value)
        if isinstance(value, tuple):
            return ("tuple", tuple(constant(item) for item in value))
        if isinstance(value, frozenset):
            items = sorted(marshal.dumps(constant(item), 2) for item in value)
            return ("frozenset", tuple(items))
        raise TypeError("Unsupported compiled constant type: %s" % type(value).__name__)

    def record(value):
        names = (
            "co_argcount", "co_posonlyargcount", "co_kwonlyargcount",
            "co_nlocals", "co_stacksize", "co_flags", "co_code",
            "co_names", "co_varnames", "co_filename", "co_name",
            "co_qualname", "co_firstlineno", "co_lnotab", "co_linetable",
            "co_exceptiontable", "co_freevars", "co_cellvars",
        )
        attributes = tuple((name, getattr(value, name))
                           for name in names if hasattr(value, name))
        return (attributes, tuple(constant(item) for item in value.co_consts))

    return marshal.dumps(("normalized-code-v1", record(normalize_code(code))), 2)


def inspect_pe32(data):
    """Read only PE headers; reject non-x86 and non-PE32 executables."""
    result = {"valid": False, "isPE32": False, "isX86": False}
    try:
        if len(data) < 0x40 or data[:2] != b"MZ":
            raise ValueError("Missing DOS MZ header")
        offset = struct.unpack_from("<I", data, 0x3C)[0]
        if offset < 0x40 or offset + 26 > len(data):
            raise ValueError("PE header offset is outside the executable")
        if data[offset:offset + 4] != b"PE\0\0":
            raise ValueError("Missing PE signature")
        machine = struct.unpack_from("<H", data, offset + 4)[0]
        optional_size = struct.unpack_from("<H", data, offset + 20)[0]
        if optional_size < 2 or offset + 24 + optional_size > len(data):
            raise ValueError("Optional PE header is truncated")
        magic = struct.unpack_from("<H", data, offset + 24)[0]
        result.update(
            peHeaderOffset=offset, machine="0x%04X" % machine,
            optionalHeaderMagic="0x%04X" % magic,
            isPE32=(magic == 0x10B), isX86=(machine == 0x14C),
        )
        result["valid"] = result["isPE32"] and result["isX86"]
        if not result["valid"]:
            result["error"] = "Release must be PE32/x86"
    except (ValueError, struct.error) as exc:
        result["error"] = str(exc)
    return result


def extract_module_code(archive, pyz, module):
    """Read a script from CArchive or a business module from its embedded PYZ."""
    if module in archive.toc:
        entry = archive.toc[module]
        if entry[-1] not in ("s", "m", "M"):
            raise ValueError("Outer archive entry is not Python code: %s" % module)
        code = marshal.loads(archive.extract(module))
        storage = "CArchive:%s" % module
    elif module in pyz.toc:
        code = pyz.extract(module)
        storage = "PYZ:%s" % module
    else:
        raise ValueError("Missing critical module: %s" % module)
    if not isinstance(code, CodeType):
        raise ValueError("Archive entry did not contain Python code: %s" % module)
    return code, storage


def compare_module(source_path, archived_code):
    source = Path(source_path).read_bytes()
    compiled = compile(source, str(source_path), "exec", flags=0,
                       dont_inherit=True, optimize=0)
    expected = normalized_code_bytes(compiled)
    actual = normalized_code_bytes(archived_code)
    return {
        "sourcePath": str(Path(source_path).resolve()),
        "sourceSha256": sha256(source),
        "compiledCodeSha256": sha256(expected),
        "archivedCodeSha256": sha256(actual),
        "matched": expected == actual,
    }


def verify_release(executable, source_root=None, modules=CRITICAL_MODULES):
    executable = Path(executable).resolve()
    source_root = (Path(source_root) if source_root is not None
                   else Path(__file__).resolve().parent).resolve()
    manifest = {
        "schemaVersion": 1,
        "capturedAt": datetime.now(timezone.utc).isoformat(),
        "passed": False,
        "verification": "PE32/x86 and recursively filename-normalized bytecode equality",
        "executable": {"path": str(executable)},
        "sourceRoot": str(source_root),
        "python": {
            "verifierVersion": sys.version,
            "verifierBytecodeMagic": importlib.util.MAGIC_NUMBER.hex(),
            "compileOptimize": 0,
            "normalizedCodeFilename": NORMALIZED_FILENAME,
            "normalizedCodeFormat": "typed-structural-v1/marshal-2/sorted-frozensets",
        },
        "modules": {},
        "errors": [],
    }
    try:
        data = executable.read_bytes()
        manifest["executable"].update(size=len(data), sha256=sha256(data),
                                      pe=inspect_pe32(data))
        if not manifest["executable"]["pe"]["valid"]:
            manifest["errors"].append(manifest["executable"]["pe"]["error"])
        from PyInstaller.archive.readers import CArchiveReader
        archive = CArchiveReader(str(executable))
        pyz_names = [name for name, entry in archive.toc.items()
                     if entry[-1] == "z"]
        if len(pyz_names) != 1:
            raise ValueError("Expected exactly one embedded PYZ, found %d" % len(pyz_names))
        pyz_name = pyz_names[0]
        # Check bytecode version before allowing a reader to unmarshal the PYZ TOC.
        pyz_offset = archive._start_offset + archive.toc[pyz_name][0]
        if data[pyz_offset:pyz_offset + 4] != b"PYZ\0":
            raise ValueError("Invalid embedded PYZ header")
        magic = data[pyz_offset + 4:pyz_offset + 8]
        manifest["python"]["archiveBytecodeMagic"] = magic.hex()
        manifest["python"]["magicMatches"] = magic == importlib.util.MAGIC_NUMBER
        manifest["python"]["archiveName"] = pyz_name
        if not manifest["python"]["magicMatches"]:
            raise ValueError("Build bytecode version differs from verifier Python")
        pyz = archive.open_embedded_archive(pyz_name)
        for module in modules:
            try:
                code, storage = extract_module_code(archive, pyz, module)
                comparison = compare_module(source_root / (module + ".py"), code)
                comparison["storage"] = storage
                manifest["modules"][module] = comparison
                if not comparison["matched"]:
                    manifest["errors"].append("Embedded bytecode differs from source: %s" % module)
            except Exception as exc:
                manifest["modules"][module] = {"matched": False, "error": str(exc)}
                manifest["errors"].append("%s: %s" % (module, exc))
    except Exception as exc:
        manifest["errors"].append(str(exc))
    manifest["passed"] = (not manifest["errors"] and bool(modules) and
                          len(manifest["modules"]) == len(modules) and
                          all(item["matched"] for item in manifest["modules"].values()))
    return manifest


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("executable", type=Path)
    parser.add_argument("--source-root", type=Path,
                        default=Path(__file__).resolve().parent)
    parser.add_argument("--output", required=True, type=Path,
                        help="Destination JSON source/build manifest")
    args = parser.parse_args(argv)
    manifest = verify_release(args.executable, args.source_root)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"passed": manifest["passed"], "manifest": str(args.output.resolve()),
                      "errors": manifest["errors"]}, ensure_ascii=False))
    return 0 if manifest["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
