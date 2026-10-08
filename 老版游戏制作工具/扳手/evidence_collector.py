"""Static and runtime evidence collector for CaoCaoZhuan 6.6 profiles."""

from __future__ import annotations

import argparse
import ctypes
import json
import os
import struct
import sys
import time
from ctypes import wintypes
from pathlib import Path
from typing import Any, Dict, Iterable, List, Mapping, Optional, Sequence, Tuple

from capability_evidence import (COLLECTOR_ID, EvidenceError,
                                 EvidenceRepository, finalize_bundle, make_bundle, recipe_id,
                                 required_runtime_checks, validate_bundle)
from engine_profile import PEImage, ProfileError, detect_engine
from process_memory import (_CONTEXT32, CONTEXT_CONTROL, MemoryAccessError,
                            MemorySession, get_process_module)


EXCEPTION_DEBUG_EVENT = 1
EXIT_PROCESS_DEBUG_EVENT = 5
EXCEPTION_BREAKPOINT = 0x80000003
EXCEPTION_SINGLE_STEP = 0x80000004
DBG_CONTINUE = 0x00010002
DBG_EXCEPTION_NOT_HANDLED = 0x80010001
THREAD_GET_CONTEXT = 0x0008
THREAD_SET_CONTEXT = 0x0010
THREAD_QUERY_INFORMATION = 0x0040


class CollectorError(RuntimeError):
    pass


class _EXCEPTION_RECORD32(ctypes.Structure):
    _fields_ = [
        ("ExceptionCode", wintypes.DWORD),
        ("ExceptionFlags", wintypes.DWORD),
        ("ExceptionRecord", ctypes.c_void_p),
        ("ExceptionAddress", ctypes.c_void_p),
        ("NumberParameters", wintypes.DWORD),
        ("ExceptionInformation", ctypes.c_size_t * 15),
    ]


class _EXCEPTION_DEBUG_INFO(ctypes.Structure):
    _fields_ = [("ExceptionRecord", _EXCEPTION_RECORD32),
                ("dwFirstChance", wintypes.DWORD)]


class _DEBUG_EVENT_UNION(ctypes.Union):
    _fields_ = [("Exception", _EXCEPTION_DEBUG_INFO),
                ("padding", ctypes.c_byte * 160)]


class _DEBUG_EVENT(ctypes.Structure):
    _fields_ = [("dwDebugEventCode", wintypes.DWORD),
                ("dwProcessId", wintypes.DWORD),
                ("dwThreadId", wintypes.DWORD),
                ("u", _DEBUG_EVENT_UNION)]


def _parse_int(value: str) -> int:
    return int(value, 0)


def _hex(value: int) -> str:
    return "0x%08X" % (int(value) & 0xFFFFFFFF)


def _load_recipe(path: Optional[str], feature_key: str,
                 profile) -> Dict[str, Any]:
    if path:
        raw = json.loads(Path(path).read_text(encoding="utf-8"))
        if "recipe" in raw:
            raw = raw["recipe"]
        if not isinstance(raw, Mapping):
            raise CollectorError("配方文件必须是 JSON 对象")
        return dict(raw)
    capability = profile.capability(feature_key)
    if capability.recipe:
        return dict(capability.recipe)
    return {"kind": "candidate", "feature": feature_key,
            "status": "not-locatable"}


def _capstone_engine():
    try:
        from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    except ImportError as exc:
        raise CollectorError(
            "静态证据需要 Capstone，请安装 requirements-dev.txt") from exc
    engine = Cs(CS_ARCH_X86, CS_MODE_32)
    engine.detail = False
    return engine


def _normalize_instruction(mnemonic: str, operands: str) -> str:
    import re
    normalized = re.sub(r"0x[0-9a-fA-F]+", "<imm>", operands)
    normalized = re.sub(r"(?<![A-Za-z0-9_])-?\b\d+\b", "<imm>", normalized)
    normalized = re.sub(r"\s+", " ", normalized).strip()
    return mnemonic.lower() + (" " + normalized.lower() if normalized else "")


def _disassemble(image: PEImage, address: int, before: int = 16,
                 size: int = 64) -> Dict[str, Any]:
    start = max(image.image_base, address - before)
    try:
        raw = image.read_va(start, size)
    except ProfileError as exc:
        return {"start": _hex(start), "target": _hex(address),
                "fileBacked": False, "reason": str(exc),
                "instructions": [], "normalizedInstructions": [],
                "normalizedFingerprint": ""}
    result = _disassemble_raw(raw, start)
    result.update({"start": _hex(start), "target": _hex(address),
                   "fileBacked": True, "bytesHex": raw.hex().upper()})
    return result


def _disassemble_raw(raw: bytes, start: int) -> Dict[str, Any]:
    engine = _capstone_engine()
    instructions = []
    normalized = []
    for instruction in engine.disasm(raw, start):
        current = {
            "address": _hex(instruction.address),
            "bytesHex": bytes(instruction.bytes).hex().upper(),
            "mnemonic": instruction.mnemonic,
            "operands": instruction.op_str,
        }
        instructions.append(current)
        normalized.append(_normalize_instruction(
            instruction.mnemonic, instruction.op_str))
    import hashlib
    fingerprint = hashlib.sha256(
        "\n".join(normalized).encode("utf-8")).hexdigest().upper()
    return {"instructions": instructions,
            "normalizedInstructions": normalized,
            "normalizedFingerprint": fingerprint}


def _overlay_u32(buffer: bytearray, offset: int, value: int,
                 width: int = 4) -> None:
    buffer[offset:offset + width] = int(value).to_bytes(
        4, "little", signed=False)[:width]


def reconstruct_legacy_auto_return_stub(original: bytes, legacy_index: int,
                                        battle_address: int,
                                        battle_stride: int) -> bytes:
    if len(original) < 0x49:
        raise CollectorError("旧自动回归代码洞样本不足 0x49 字节")
    result = bytearray(original)
    _overlay_u32(result, 0x00, 0x83EC8B55)
    _overlay_u32(result, 0x04, 0x75FF04EC)
    if legacy_index >= 3:
        _overlay_u32(result, 0x08, 0x9E8DE808)
        _overlay_u32(result, 0x0C, 0x4D8B0004)
    else:
        _overlay_u32(result, 0x08, 0xCF44E808)
        _overlay_u32(result, 0x0C, 0x4D8BFFF8)
    _overlay_u32(result, 0x10, 0xFFE18108)
    _overlay_u32(result, 0x17, 0xC96B, 2)
    _overlay_u32(result, 0x19, battle_stride)
    _overlay_u32(result, 0x1A, 0x81, 1)
    _overlay_u32(result, 0x1B, 0xC1, 1)
    _overlay_u32(result, 0x1C, battle_address, 3)
    _overlay_u32(result, 0x20, 0xB8FC4D89)
    _overlay_u32(result, 0x24, 0x06, 1)
    _overlay_u32(result, 0x28, 0x4120D0F7)
    _overlay_u32(result, 0x2C, 0x0D, 1)
    if legacy_index >= 1:
        _overlay_u32(result, 0x2D, 0xE831FF, 3)
        _overlay_u32(result, 0x30, 0xFFFB86A2)
        _overlay_u32(result, 0x34, 0x00008868)
        _overlay_u32(result, 0x38, 0x50066A00)
    else:
        _overlay_u32(result, 0x2D, 0x00008868)
        _overlay_u32(result, 0x31, 0x8B066A00)
        _overlay_u32(result, 0x35, 0x71FFFC4D)
        _overlay_u32(result, 0x39, 0x00909004, 3)
    _overlay_u32(result, 0x3C, 0xE8, 1)
    _overlay_u32(result, 0x3D, 0xFFFB8869)
    _overlay_u32(result, 0x41, 0xFE5DE58B)
    _overlay_u32(result, 0x45, 0x00C3FC4D)
    return bytes(result)


def collect_legacy_oracle(exe_path: str, output: str, legacy_index: int,
                          battle_address: int, battle_stride: int) -> Path:
    image = PEImage(exe_path)
    risk_addresses = (
        0x004242B0, 0x00437BC0, 0x00454405, 0x00455646,
        0x0044E570, 0x0044EA74, 0x00485E00,
        0x00457428, 0x004594DD, 0x00414F65,
    )
    try:
        original_stub = image.read_va(0x00485E00, 0x49)
        original_source = "file-backed"
    except ProfileError:
        original_stub = b"\0" * 0x49
        original_source = "loader-zero-filled-uninitialized-section"
    reconstructed = reconstruct_legacy_auto_return_stub(
        original_stub, legacy_index, battle_address, battle_stride)
    recipe = {
        "kind": "legacy-oracle", "legacyIndex": legacy_index,
        "battleAddress": battle_address, "battleStride": battle_stride,
        "riskAddresses": list(risk_addresses),
    }
    static_evidence = {
        "riskWindows": [_disassemble(image, item, before=16, size=64)
                        for item in risk_addresses],
        "literalReferenceCounts": {
            _hex(value): len(image.find_literal_references(value))
            for value in (0x00510C80, 0x004B09DB, 0x004B5DF0)
        },
        "autoReturnStub": {
            "address": _hex(0x00485E00),
            "originalSource": original_source,
            "originalHex": original_stub.hex().upper(),
            "reconstructedHex": reconstructed.hex().upper(),
            "reconstructedDisassembly": _disassemble_raw(
                reconstructed, 0x00485E00),
            "changedOffsets": [index for index, values in enumerate(
                zip(original_stub, reconstructed)) if values[0] != values[1]],
        },
    }
    bundle = make_bundle(
        "legacy_oracle", image.sha256, os.path.getsize(exe_path),
        recipe_id("legacy_oracle", recipe), recipe, "static", static_evidence)
    return EvidenceRepository(output).write_bundle(bundle)


def _feature_addresses(profile, feature_key: str) -> List[int]:
    result: List[int] = []
    patch = profile.patches.get(feature_key)
    if patch is not None:
        result.extend(site.address for site in patch.sites)
    dynamic = profile.dynamic_patches.get(feature_key)
    if dynamic is not None:
        result.extend(site.address for site in dynamic.call_sites)
    call = profile.remote_calls.get(feature_key)
    if call is not None and call.address:
        result.append(call.address)
    capability = profile.capability(feature_key)
    for name in ("signature", "operand"):
        value = capability.addresses.get(name)
        if value:
            result.append(value)
    if feature_key == "control_auto_return":
        layout = profile.metadata.get("auto_return_layout", {})
        if isinstance(layout, Mapping):
            for name in ("nativeEffect",):
                value = layout.get(name)
                if value:
                    result.append(int(value))
            result.extend(int(item) for item in layout.get("callSites", []))
            result.extend(int(item) for item in layout.get("patchSites", []))
    return sorted(set(result))


def collect_static(exe_path: str, output: str,
                   features: Sequence[str], recipe_path: Optional[str]) -> List[Path]:
    image = PEImage(exe_path)
    profile = detect_engine(exe_path, version_part=6)
    repository = EvidenceRepository(output)
    selected = list(features) if features else sorted(profile.capabilities)
    old_risk_addresses = (
        0x004242B0, 0x0044E570, 0x0044EA74, 0x00485E00,
        0x00457428, 0x004594DD, 0x00414F65,
    )
    common = {
        "legacyRiskWindows": [
            _disassemble(image, address, before=8, size=32)
            for address in old_risk_addresses
        ],
        "literalReferenceCounts": {
            _hex(value): len(image.find_literal_references(value))
            for value in (0x00510C80, 0x004B09DB, 0x004B5DF0)
        },
    }
    written = []
    for feature_key in selected:
        if feature_key not in profile.capabilities:
            raise CollectorError("画像中不存在功能: %s" % feature_key)
        recipe = _load_recipe(recipe_path, feature_key, profile)
        addresses = _feature_addresses(profile, feature_key)
        static_evidence = {
            "profileState": profile.capability(feature_key).state.value,
            "reasonCode": profile.capability(feature_key).reason_code,
            "addresses": [_hex(item) for item in addresses],
            "windows": [_disassemble(image, item) for item in addresses],
            "commonRiskFacts": common,
        }
        bundle = make_bundle(
            feature_key, profile.sha256, profile.file_size,
            recipe_id(feature_key, recipe), recipe, "static", static_evidence)
        written.append(repository.write_bundle(bundle))
    return written


def _parse_watch(value: str) -> Tuple[str, int, int]:
    try:
        name, address, size = value.split(":", 2)
        parsed = (name, _parse_int(address), _parse_int(size))
    except (ValueError, TypeError) as exc:
        raise argparse.ArgumentTypeError(
            "观察区格式应为 name:address:size") from exc
    if not parsed[0] or parsed[2] <= 0 or parsed[2] > 0x10000:
        raise argparse.ArgumentTypeError("观察区名称或长度无效")
    return parsed


def _parse_tag(value: str) -> Tuple[str, str]:
    if "=" not in value:
        raise argparse.ArgumentTypeError("标签格式应为 name=value")
    name, tag_value = value.split("=", 1)
    if not name or not tag_value:
        raise argparse.ArgumentTypeError("标签名称和值不能为空")
    return name, tag_value


def _path_value(value: Any, path: str) -> Any:
    current = value
    for part in path.split("."):
        if not isinstance(current, Mapping) or part not in current:
            raise KeyError(path)
        current = current[part]
    return current


def _evaluate_predicate(predicate: Mapping[str, Any],
                        samples: Sequence[Mapping[str, Any]]) -> bool:
    kind = predicate.get("type")
    if kind == "min-samples":
        return len(samples) >= int(predicate.get("minimum", 1))
    if kind == "tag-values":
        tag = str(predicate["tag"])
        expected = {str(item) for item in predicate.get("values", [])}
        minimum = int(predicate.get("minimumPerValue", 1))
        counts = {item: 0 for item in expected}
        for sample in samples:
            value = str(sample.get("tags", {}).get(tag, ""))
            if value in counts:
                counts[value] += 1
        return bool(expected) and all(value >= minimum for value in counts.values())
    if kind == "field-present":
        path = str(predicate["path"])
        try:
            values = [_path_value(sample, path) for sample in samples]
        except KeyError:
            return False
        return bool(values) and all(value not in (None, "", [], {}) for value in values)
    if kind == "all-equal":
        path = str(predicate["path"])
        try:
            values = [_path_value(sample, path) for sample in samples]
        except KeyError:
            return False
        return bool(values) and all(value == predicate.get("value") for value in values)
    if kind == "distinct-count":
        path = str(predicate["path"])
        try:
            values = {_path_value(sample, path) for sample in samples}
        except (KeyError, TypeError):
            return False
        return len(values) >= int(predicate.get("minimum", 1))
    if kind == "watch-relation":
        tag = str(predicate.get("tag", "phase"))
        watch = str(predicate["watch"])
        left_tag = str(predicate["left"])
        right_tag = str(predicate["right"])
        left = {sample.get("watches", {}).get(watch, {}).get("bytesHex")
                for sample in samples
                if str(sample.get("tags", {}).get(tag, "")) == left_tag}
        right = {sample.get("watches", {}).get(watch, {}).get("bytesHex")
                 for sample in samples
                 if str(sample.get("tags", {}).get(tag, "")) == right_tag}
        left.discard(None)
        left.discard("")
        right.discard(None)
        right.discard("")
        relation = predicate.get("relation")
        if not left or not right:
            return False
        if relation == "equal":
            return left == right
        if relation == "different":
            return left.isdisjoint(right)
        return False
    if kind == "all-of":
        children = predicate.get("predicates", [])
        return bool(children) and all(_evaluate_predicate(item, samples)
                                      for item in children)
    return False


def evaluate_verification_contract(bundle: Mapping[str, Any]) -> Dict[str, bool]:
    recipe = bundle.get("recipe", {})
    contract = recipe.get("verificationContract", {}) if isinstance(recipe, Mapping) else {}
    declared = contract.get("checks", {}) if isinstance(contract, Mapping) else {}
    samples = bundle.get("runtimeSamples", [])
    checks = {}
    for name in required_runtime_checks(str(bundle.get("featureKey", ""))):
        predicate = declared.get(name)
        checks[name] = (isinstance(predicate, Mapping) and
                        _evaluate_predicate(predicate, samples))
    return checks


def verify_captured_bundle(path: str, output: str) -> Path:
    bundle = json.loads(Path(path).read_text(encoding="utf-8"))
    validate_bundle(bundle, runtime_required=False)
    if bundle.get("phase") != "runtime":
        raise CollectorError("只有运行时证据可以执行自动验证")
    checks = evaluate_verification_contract(bundle)
    if not checks or not all(checks.values()):
        missing = [name for name, passed in checks.items() if not passed]
        raise CollectorError("验证合同未通过: " + ", ".join(missing))
    updated = dict(bundle)
    updated["verification"] = {
        "collector": COLLECTOR_ID, "status": "verified", "automated": True,
        "checks": checks,
    }
    updated = finalize_bundle(updated)
    validate_bundle(updated, runtime_required=True)
    return EvidenceRepository(output).write_bundle(updated)


class WindowsBreakpointCollector:
    def __init__(self, pid: int, session: MemorySession,
                 breakpoints: Sequence[int], watches: Sequence[Tuple[str, int, int]],
                 stack_size: int = 64):
        if os.name != "nt":
            raise CollectorError("运行时断点采集只支持 Windows")
        self.pid = int(pid)
        self.session = session
        self.breakpoints = tuple(sorted(set(int(item) for item in breakpoints)))
        self.watches = tuple(watches)
        self.stack_size = int(stack_size)
        self.kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self.wow64 = ctypes.sizeof(ctypes.c_void_p) == 8
        self._declare_api()
        self.original: Dict[int, bytes] = {}
        self.pending: Dict[int, Dict[str, Any]] = {}

    def _declare_api(self) -> None:
        k32 = self.kernel32
        k32.DebugActiveProcess.argtypes = [wintypes.DWORD]
        k32.DebugActiveProcess.restype = wintypes.BOOL
        k32.DebugActiveProcessStop.argtypes = [wintypes.DWORD]
        k32.DebugActiveProcessStop.restype = wintypes.BOOL
        k32.DebugSetProcessKillOnExit.argtypes = [wintypes.BOOL]
        k32.DebugSetProcessKillOnExit.restype = wintypes.BOOL
        k32.WaitForDebugEvent.argtypes = [ctypes.POINTER(_DEBUG_EVENT), wintypes.DWORD]
        k32.WaitForDebugEvent.restype = wintypes.BOOL
        k32.ContinueDebugEvent.argtypes = [wintypes.DWORD, wintypes.DWORD,
                                           wintypes.DWORD]
        k32.ContinueDebugEvent.restype = wintypes.BOOL
        k32.OpenThread.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        k32.OpenThread.restype = wintypes.HANDLE
        get_context = (k32.Wow64GetThreadContext if self.wow64
                       else k32.GetThreadContext)
        set_context = (k32.Wow64SetThreadContext if self.wow64
                       else k32.SetThreadContext)
        get_context.argtypes = [wintypes.HANDLE, ctypes.POINTER(_CONTEXT32)]
        get_context.restype = wintypes.BOOL
        set_context.argtypes = [wintypes.HANDLE, ctypes.POINTER(_CONTEXT32)]
        set_context.restype = wintypes.BOOL
        self.get_context = get_context
        self.set_context = set_context
        k32.CloseHandle.argtypes = [wintypes.HANDLE]
        k32.CloseHandle.restype = wintypes.BOOL

    def _context(self, thread_id: int) -> Tuple[int, _CONTEXT32]:
        access = THREAD_GET_CONTEXT | THREAD_SET_CONTEXT | THREAD_QUERY_INFORMATION
        handle = self.kernel32.OpenThread(access, False, int(thread_id))
        if not handle:
            raise CollectorError("打开调试线程失败")
        context = _CONTEXT32()
        context.ContextFlags = CONTEXT_CONTROL | 0x00010002
        if not self.get_context(handle, ctypes.byref(context)):
            self.kernel32.CloseHandle(handle)
            raise CollectorError("读取调试线程寄存器失败")
        return int(handle), context

    def _set_context(self, handle: int, context: _CONTEXT32) -> None:
        if not self.set_context(handle, ctypes.byref(context)):
            raise CollectorError("写回调试线程寄存器失败")

    def _read_optional(self, actual_address: int, size: int) -> str:
        try:
            return self.session.read_actual_bytes(actual_address, size).hex().upper()
        except MemoryAccessError:
            return ""

    def _sample(self, context: _CONTEXT32, address: int,
                thread_id: int) -> Dict[str, Any]:
        registers = {name: _hex(getattr(context, name)) for name in (
            "Eax", "Ebx", "Ecx", "Edx", "Esi", "Edi", "Ebp", "Esp",
            "Eip", "EFlags")}
        watches = {}
        for name, reference, size in self.watches:
            actual = self.session.resolve_reference(reference)
            watches[name] = {"address": _hex(reference), "size": size,
                             "bytesHex": self._read_optional(actual, size)}
        sample = {
            "threadId": int(thread_id), "breakpoint": _hex(address),
            "registers": registers,
            "stackHex": self._read_optional(int(context.Esp), self.stack_size),
            "watches": watches,
        }
        return sample

    def collect(self, sample_count: int, timeout_seconds: float) -> List[Dict[str, Any]]:
        if not self.breakpoints:
            raise CollectorError("运行时采集至少需要一个断点")
        if not self.kernel32.DebugActiveProcess(self.pid):
            raise CollectorError("附加 Windows 调试器失败，WinError=%d" %
                                 ctypes.get_last_error())
        attached = True
        samples: List[Dict[str, Any]] = []
        deadline = time.monotonic() + timeout_seconds
        try:
            self.kernel32.DebugSetProcessKillOnExit(False)
            for reference in self.breakpoints:
                self.original[reference] = self.session.read_bytes(reference, 1)
                self.session.write_bytes(reference, b"\xCC", allow_protect=True)
            while len(samples) < sample_count and time.monotonic() < deadline:
                event = _DEBUG_EVENT()
                if not self.kernel32.WaitForDebugEvent(ctypes.byref(event), 250):
                    error = ctypes.get_last_error()
                    if error == 121:
                        continue
                    raise CollectorError("等待调试事件失败，WinError=%d" % error)
                status = DBG_CONTINUE
                try:
                    if event.dwDebugEventCode == EXIT_PROCESS_DEBUG_EVENT:
                        break
                    if event.dwDebugEventCode != EXCEPTION_DEBUG_EVENT:
                        continue
                    code = int(event.u.Exception.ExceptionRecord.ExceptionCode)
                    thread_id = int(event.dwThreadId)
                    exception_address = int(
                        event.u.Exception.ExceptionRecord.ExceptionAddress or 0)
                    reference = next((item for item in self.breakpoints
                                      if self.session.resolve_reference(item) ==
                                      exception_address), None)
                    if code == EXCEPTION_BREAKPOINT and reference is not None:
                        handle, context = self._context(thread_id)
                        try:
                            sample = self._sample(context, reference, thread_id)
                            self.session.write_bytes(
                                reference, self.original[reference], allow_protect=True)
                            context.Eip = exception_address
                            context.EFlags |= 0x100
                            self._set_context(handle, context)
                            self.pending[thread_id] = {"reference": reference,
                                                       "sample": sample}
                        finally:
                            self.kernel32.CloseHandle(handle)
                    elif code == EXCEPTION_SINGLE_STEP and thread_id in self.pending:
                        pending = self.pending.pop(thread_id)
                        handle, context = self._context(thread_id)
                        try:
                            context.EFlags &= ~0x100
                            self._set_context(handle, context)
                            pending["sample"]["nextEip"] = _hex(context.Eip)
                        finally:
                            self.kernel32.CloseHandle(handle)
                        reference = pending["reference"]
                        self.session.write_bytes(reference, b"\xCC", allow_protect=True)
                        samples.append(pending["sample"])
                    else:
                        status = DBG_EXCEPTION_NOT_HANDLED
                finally:
                    self.kernel32.ContinueDebugEvent(
                        event.dwProcessId, event.dwThreadId, status)
        finally:
            for reference, original in self.original.items():
                try:
                    current = self.session.read_bytes(reference, 1)
                    if current == b"\xCC":
                        self.session.write_bytes(reference, original,
                                                 allow_protect=True)
                except MemoryAccessError:
                    pass
            if attached:
                self.kernel32.DebugActiveProcessStop(self.pid)
        if len(samples) < sample_count:
            raise CollectorError("采集超时，只获得 %d/%d 个样本" %
                                 (len(samples), sample_count))
        return samples


def collect_runtime(pid: int, feature_key: str, output: str,
                    breakpoints: Sequence[int], watches: Sequence[Tuple[str, int, int]],
                    sample_count: int, timeout_seconds: float,
                    stack_size: int, recipe_path: Optional[str],
                    tags: Mapping[str, str], append_path: Optional[str]) -> Path:
    module_base, module_size, exe_path = get_process_module(pid)
    profile = detect_engine(exe_path, version_part=6)
    recipe = _load_recipe(recipe_path, feature_key, profile)
    session = MemorySession.open_pid(
        pid, module_base, module_size, profile.preferred_image_base)
    try:
        collector = WindowsBreakpointCollector(
            pid, session, breakpoints, watches, stack_size)
        samples = collector.collect(sample_count, timeout_seconds)
    finally:
        session.close()
    for sample in samples:
        sample["tags"] = {"base": profile.variant_name, **dict(tags)}
    if append_path:
        previous = json.loads(Path(append_path).read_text(encoding="utf-8"))
        validate_bundle(previous, runtime_required=False)
        if (previous.get("phase") != "runtime" or
                previous.get("featureKey") != feature_key or
                str(previous.get("target", {}).get("sha256", "")).upper() !=
                profile.sha256 or previous.get("recipeId") != recipe_id(feature_key, recipe)):
            raise CollectorError("追加证据的目标、功能或配方不一致")
        samples = list(previous.get("runtimeSamples", [])) + samples
    bundle = make_bundle(
        feature_key, profile.sha256, profile.file_size,
        recipe_id(feature_key, recipe), recipe, "runtime",
        {"breakpoints": [_hex(item) for item in breakpoints],
         "watches": [list(item) for item in watches]}, samples,
        {"collector": COLLECTOR_ID, "status": "captured", "automated": True,
         "checks": {name: False for name in required_runtime_checks(feature_key)}})
    return EvidenceRepository(output).write_bundle(bundle)


def inspect_bundle(path: str) -> int:
    bundle = json.loads(Path(path).read_text(encoding="utf-8"))
    validate_bundle(bundle, runtime_required=False)
    checks = bundle.get("verification", {}).get("checks", {})
    missing = [name for name in required_runtime_checks(
        str(bundle["featureKey"])) if checks.get(name) is not True]
    print("feature=%s phase=%s digest=%s" %
          (bundle["featureKey"], bundle.get("phase"), bundle["bundleDigest"]))
    if missing:
        print("not verified; missing automated checks: " + ", ".join(missing))
        return 2
    try:
        validate_bundle(bundle, runtime_required=True)
    except EvidenceError as exc:
        print("not verified: %s" % exc)
        return 2
    print("runtime evidence verified")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="曹操传 6.6 扳手证据采集器")
    commands = parser.add_subparsers(dest="command", required=True)
    static = commands.add_parser("static", help="采集离线签名与规范化反汇编")
    static.add_argument("--exe", required=True)
    static.add_argument("--output", required=True)
    static.add_argument("--feature", action="append", default=[])
    static.add_argument("--recipe")
    oracle = commands.add_parser("oracle", help="还原旧版危险站点和自动回归 stub")
    oracle.add_argument("--exe", required=True)
    oracle.add_argument("--output", required=True)
    oracle.add_argument("--legacy-index", type=int, default=0,
                        choices=range(0, 5))
    oracle.add_argument("--battle-address", type=_parse_int, default=0x004A7B20)
    oracle.add_argument("--battle-stride", type=_parse_int, default=0x30)
    runtime = commands.add_parser("runtime", help="附加进程并采集断点原始样本")
    runtime.add_argument("--pid", required=True, type=int)
    runtime.add_argument("--feature", required=True)
    runtime.add_argument("--output", required=True)
    runtime.add_argument("--breakpoint", action="append", required=True,
                         type=_parse_int)
    runtime.add_argument("--watch", action="append", default=[], type=_parse_watch)
    runtime.add_argument("--samples", type=int, default=6)
    runtime.add_argument("--timeout", type=float, default=120.0)
    runtime.add_argument("--stack-bytes", type=int, default=64)
    runtime.add_argument("--recipe")
    runtime.add_argument("--tag", action="append", default=[], type=_parse_tag)
    runtime.add_argument("--append")
    inspect = commands.add_parser("inspect", help="检查证据完整性和缺失断言")
    inspect.add_argument("path")
    verify = commands.add_parser("verify", help="按配方合同自动验证运行时样本")
    verify.add_argument("path")
    verify.add_argument("--output", required=True)
    manifest = commands.add_parser("manifest", help="重建证据 SHA256 清单")
    manifest.add_argument("--output", required=True)
    return parser


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        if args.command == "static":
            paths = collect_static(args.exe, args.output, args.feature, args.recipe)
            for path in paths:
                print(path)
            return 0
        if args.command == "runtime":
            path = collect_runtime(
                args.pid, args.feature, args.output, args.breakpoint, args.watch,
                args.samples, args.timeout, args.stack_bytes, args.recipe,
                dict(args.tag), args.append)
            print(path)
            return 0
        if args.command == "oracle":
            path = collect_legacy_oracle(
                args.exe, args.output, args.legacy_index,
                args.battle_address, args.battle_stride)
            print(path)
            return 0
        if args.command == "inspect":
            return inspect_bundle(args.path)
        if args.command == "verify":
            print(verify_captured_bundle(args.path, args.output))
            return 0
        if args.command == "manifest":
            EvidenceRepository(args.output).rebuild_manifest()
            return 0
    except (CollectorError, EvidenceError, MemoryAccessError, ProfileError, OSError,
            ValueError, json.JSONDecodeError) as exc:
        print("error: %s" % exc, file=sys.stderr)
        return 1
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
