"""32-bit remote-call helpers used by the original modifier UI."""

from __future__ import annotations

import logging
import struct

from process_memory import (MemoryAccessError, MemorySession, PAGE_EXECUTE_READ,
                            PAGE_READWRITE)


class HookError(RuntimeError):
    pass


LOGGER = logging.getLogger(__name__)


class my_hook:
    """Keep the historic class/method names while fixing resource ownership."""

    def __init__(self, session: MemorySession = None, profile=None):
        self.session = session
        self.profile = profile

    def _get_session(self, candidate) -> MemorySession:
        if isinstance(candidate, MemorySession):
            return candidate
        if self.session is not None:
            return self.session
        raise HookError("远程调用需要有效的 MemorySession")

    def _call_address(self, key: str, fallback: int) -> int:
        if self.profile is None:
            address = int(fallback)
            if not 0x00010000 <= address <= 0x7FFFFFFF:
                raise HookError("%s 入口地址不是合法的 PE32 用户态地址" % key)
            return address
        capability = self.profile.capability(key)
        resolved = (self.profile.resolved_feature(key)
                    if hasattr(self.profile, "resolved_feature") else None)
        can_write = (self.profile.can_write(key)
                     if hasattr(self.profile, "can_write") else
                     self.profile.can_use(key))
        if not can_write:
            raise HookError((resolved.reason if resolved is not None else "") or
                            capability.reason or
                            ("%s 当前不可写" % key))
        specs = getattr(self.profile, "remote_calls", {})
        spec = specs.get(key) if hasattr(specs, "get") else None
        if spec is None:
            # A profile may expose a version-specific fallback helper while
            # migrating old serialized profile data.  Do not invent a generic
            # address for an unknown feature.
            resolver = getattr(self.profile, "%s_entry_address" % key, None)
            if not callable(resolver):
                raise HookError("%s 缺少远程调用入口配方" % key)
            address = int(resolver())
            abi_verified = True
        else:
            address = int(spec.address)
            abi_verified = bool(getattr(spec, "abi_verified", True))
        if not 0x00010000 <= address <= 0x7FFFFFFF:
            raise HookError("%s 入口地址无效: 0x%08X" % (key, address))
        if not abi_verified:
            warning = ("direct_compat 允许执行未完成 ABI 验证的远程调用 "
                       "%s @ 0x%08X" % (key, address))
            LOGGER.warning(warning, extra={
                "feature": key,
                "address": address,
                "abi_verified": False,
            })
        return address

    @staticmethod
    def _run(session: MemorySession, shellcode: bytes, datacode: bytes,
             timeout_ms: int = 0xFFFFFFFF) -> None:
        code_address = 0
        data_address = 0
        thread = 0
        preserve_remote_allocations = False
        try:
            with session.remote_backend() as backend:
                code_address = backend.allocate(len(shellcode), PAGE_READWRITE)
                data_address = backend.allocate(len(datacode), PAGE_READWRITE)
                if backend.write(code_address, shellcode) != len(shellcode):
                    raise HookError("远程代码只写入了部分字节")
                if backend.write(data_address, datacode) != len(datacode):
                    raise HookError("远程参数只写入了部分字节")
                if backend.read(code_address, len(shellcode)) != shellcode:
                    raise HookError("远程代码写后复读失败")
                if backend.read(data_address, len(datacode)) != datacode:
                    raise HookError("远程参数写后复读失败")
                backend.protect(code_address, len(shellcode), PAGE_EXECUTE_READ)
                if hasattr(backend, "flush"):
                    backend.flush(code_address, len(shellcode))
                thread = backend.create_thread(code_address, data_address)
                backend.wait_thread(thread, timeout_ms)
                backend.close_thread(thread)
                thread = 0
                backend.free(data_address)
                data_address = 0
                backend.free(code_address)
                code_address = 0
        except (MemoryAccessError, HookError) as exc:
            if "超时" in str(exc):
                preserve_remote_allocations = True
                session.remote_call_poisoned = True
                session.last_remote_call_diagnostic = {
                    "reason": str(exc),
                    "shellcodeSize": len(shellcode),
                    "dataSize": len(datacode),
                    "thread": int(thread),
                    "codeAddress": int(code_address),
                    "dataAddress": int(data_address),
                }
            raise HookError(str(exc)) from exc
        finally:
            # A failing call still owns every resource created before the failure.
            if thread or data_address or code_address:
                try:
                    with session.remote_backend(
                            allow_poisoned=preserve_remote_allocations) as backend:
                        if thread:
                            backend.close_thread(thread)
                        if preserve_remote_allocations:
                            if data_address:
                                session._orphaned_allocations.append(data_address)
                            if code_address:
                                session._orphaned_allocations.append(code_address)
                        elif data_address:
                            backend.free(data_address)
                        if not preserve_remote_allocations and code_address:
                            backend.free(code_address)
                except Exception:
                    pass

    def life(self, h_process, direction, x, y, code, add=None):
        session = self._get_session(h_process)
        direction = int(direction)
        x = int(x)
        y = int(y)
        code = int(code)
        if not 0 <= direction <= 3:
            raise HookError("复活方向必须在 0..3 范围内")
        if not 0 <= x <= 0xFF or not 0 <= y <= 0xFF:
            raise HookError("复活坐标必须在 0..255 范围内")
        if not 0 <= code <= 0xFFFF or code == 0xFFFF:
            raise HookError("复活 Data ID 必须是有效的 UInt16 单位编号")
        fallback = 0x004092E0
        if self.profile is not None and hasattr(
                self.profile, "revive_entry_address"):
            fallback = int(self.profile.revive_entry_address())
        function = self._call_address("revive", fallback)
        spec = (getattr(self.profile, "remote_calls", {}).get("revive")
                if self.profile is not None else None)
        wrapper_kind = getattr(spec, "wrapper_kind", "")
        if wrapper_kind not in ("", "revive-v1", "legacy-life-v1"):
            raise HookError("战场复活 ABI 使用了当前扳手不支持的包装: %s" %
                            wrapper_kind)
        if add is not None and self.profile is None:
            if isinstance(add, bytes):
                function = struct.unpack("<I", add[:4])[0]
            else:
                function = int(add)
            if not 0x00010000 <= function <= 0x7FFFFFFF:
                raise HookError("复活入口地址无效: 0x%08X" % function)
        function = session.resolve_reference(function)
        shellcode = (
            b"\x55\x8B\xEC\x83\xEC\x10\x8B\x45\x08\x8B\x08\x8B\x50\x04"
            b"\x89\x4D\xF0\x8B\x48\x08\x89\x55\xF8\x8B\x50\x0C\x8B\x40\x10"
            b"\x89\x4D\xF4\x89\x55\xFC\x89\x45\x08\xFF\x75\xF0\xFF\x75\xF4"
            b"\xFF\x75\xF8\xFF\x75\xFC\xFF\x55\x08\x33\xC0\x8B\xE5\x5D"
            b"\xC2\x04\x00"
        )
        data = struct.pack("<IIIII", direction, x, y, code, function)
        if self.profile is not None and hasattr(
                self.profile, "note_compatibility_call"):
            self.profile.note_compatibility_call(
                "revive", function, (direction, x, y, code))
        self._run(session, shellcode, data)

    def recal(self, h_process, code):
        session = self._get_session(h_process)
        function = session.resolve_reference(self._call_address("recalculate", 0x004075DF))
        shellcode = (
            b"\x55\x8B\xEC\x8B\x45\x08\x8B\x08\xFF\x30\xFF\x50\x04"
            b"\x8B\xE5\x5D\xC2\x04\x00"
        )
        self._run(session, shellcode, struct.pack("<II", int(code), function))

    def changeDir(self, h_process, direction, code):
        session = self._get_session(h_process)
        fallback = (0 if self.profile is not None and self.profile.is_66
                    else 0x00457428)
        refresh_reference = self._call_address(
            "turn_refresh_direction", fallback)
        spec = (self.profile.remote_calls.get("turn_refresh_direction")
                if self.profile is not None else None)
        if spec is not None and spec.wrapper_kind not in (
                "", "legacy-turn-v1", "turn-66-six-args-v1"):
            raise HookError("转向刷新证据使用了当前扳手不支持的调用包装")
        refresh = session.resolve_reference(refresh_reference)
        if spec is not None and spec.wrapper_kind == "turn-66-six-args-v1":
            shellcode = (
                b"\x55\x8B\xEC\x8B\x45\x08\x6A\x01\x6A\x01\x6A\x00"
                b"\xFF\x30\x68\xFF\xFF\x00\x00\xFF\x70\x04\xFF\x50\x08"
                b"\x33\xC0\x8B\xE5\x5D\xC2\x04\x00"
            )
            data = struct.pack("<III", int(direction), int(code), refresh)
            self._run(session, shellcode, data)
            return
        context_reference = (spec.context_reference if spec is not None and
                             spec.context_reference else 0x004B5DF0)
        unit_lookup = session.resolve_reference(context_reference)
        shellcode = (
            b"\x55\x8B\xEC\x8B\x45\x08\x6A\x00\xFF\x30\x68\xFF\xFF\x00\x00"
            b"\xFF\x70\x04\x8B\x48\x08\xFF\x50\x0C\x8B\xE5\x5D\xC2\x04\x00"
        )
        data = struct.pack("<IIII", int(direction), int(code), unit_lookup, refresh)
        self._run(session, shellcode, data)

    def changePos(self, h_process, direction, x, y, code):
        session = self._get_session(h_process)
        refresh_reference = self._call_address("turn_refresh_position", 0x004594DD)
        spec = (self.profile.remote_calls.get("turn_refresh_position")
                if self.profile is not None else None)
        if spec is not None and spec.wrapper_kind not in ("", "legacy-position-v1"):
            raise HookError("坐标刷新证据使用了当前扳手不支持的调用包装")
        context_reference = (spec.context_reference if spec is not None and
                             spec.context_reference else 0x004B5DF0)
        unit_lookup = session.resolve_reference(context_reference)
        refresh = session.resolve_reference(refresh_reference)
        shellcode = (
            b"\x55\x8B\xEC\x8B\x45\x08\xFF\x30\xFF\x70\x04\xFF\x70\x08"
            b"\xFF\x70\x0C\x8B\x48\x10\xFF\x50\x14\x8B\xE5\x5D\xC2\x04\x00"
        )
        data = struct.pack("<IIIIII", int(direction), int(y), int(x), int(code),
                           unit_lookup, refresh)
        self._run(session, shellcode, data)

    def changeWeather(self, h_process, weather):
        session = self._get_session(h_process)
        function = session.resolve_reference(self._call_address("weather", 0x0041D9D1))
        weather_context = session.resolve_reference(0x004B3D08)
        shellcode = (
            b"\x55\x8B\xEC\x8B\x45\x08\xFF\x30\xB9" +
            struct.pack("<I", weather_context) +
            b"\xFF\x50\x04\x8B\xE5\x5D\xC2\x04\x00"
        )
        self._run(session, shellcode, struct.pack("<II", int(weather), function))

    @staticmethod
    def intToBytes(value, length):
        return int(value).to_bytes(length, "little", signed=False)
