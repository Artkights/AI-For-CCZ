"""Type-safe process memory access and transactional patching."""

from __future__ import annotations

import ctypes
import os
import struct
from contextlib import contextmanager
from ctypes import wintypes
from dataclasses import dataclass
from typing import Dict, Iterable, List, Optional, Tuple

from engine_profile import (DynamicPatchSpec, PatchSpec,
                            REFERENCE_IMAGE_BASE)
from engine_layout import CompareWriteEntry


PROCESS_CREATE_THREAD = 0x0002
PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_VM_OPERATION = 0x0008
PROCESS_VM_READ = 0x0010
PROCESS_VM_WRITE = 0x0020
SYNCHRONIZE = 0x00100000

MEM_COMMIT = 0x1000
MEM_RELEASE = 0x8000
MEM_PRIVATE = 0x20000
MEM_MAPPED = 0x40000
MEM_IMAGE = 0x1000000
PAGE_NOACCESS = 0x01
PAGE_GUARD = 0x100
PAGE_READONLY = 0x02
PAGE_READWRITE = 0x04
PAGE_WRITECOPY = 0x08
PAGE_EXECUTE = 0x10
PAGE_EXECUTE_READ = 0x20
PAGE_EXECUTE_READWRITE = 0x40
PAGE_EXECUTE_WRITECOPY = 0x80
TH32CS_SNAPTHREAD = 0x00000004
THREAD_SUSPEND_RESUME = 0x0002
THREAD_GET_CONTEXT = 0x0008
THREAD_QUERY_INFORMATION = 0x0040
CONTEXT_i386 = 0x00010000
CONTEXT_CONTROL = CONTEXT_i386 | 0x00000001
TOKEN_QUERY = 0x0008
TOKEN_INTEGRITY_LEVEL = 25
PROCESS_DYNAMIC_CODE_POLICY = 2

READABLE_PROTECTIONS = {
    PAGE_READONLY, PAGE_READWRITE, PAGE_WRITECOPY, PAGE_EXECUTE,
    PAGE_EXECUTE_READ, PAGE_EXECUTE_READWRITE, PAGE_EXECUTE_WRITECOPY,
}
WRITABLE_PROTECTIONS = {
    PAGE_READWRITE, PAGE_WRITECOPY, PAGE_EXECUTE_READWRITE,
    PAGE_EXECUTE_WRITECOPY,
}
EXECUTABLE_PROTECTIONS = {
    PAGE_EXECUTE, PAGE_EXECUTE_READ, PAGE_EXECUTE_READWRITE,
    PAGE_EXECUTE_WRITECOPY,
}


class MemoryAccessError(RuntimeError):
    pass


class PatchError(MemoryAccessError):
    pass


@dataclass(frozen=True)
class MemoryPageInfo:
    base_address: int
    allocation_base: int
    allocation_protect: int
    region_size: int
    state: int
    protect: int
    memory_type: int

    @property
    def end_address(self) -> int:
        return self.base_address + self.region_size

    @property
    def normalized_protect(self) -> int:
        return self.protect & 0xFF

    def describe(self) -> str:
        return ("base=0x%08X,size=0x%X,state=0x%X,protect=0x%X,"
                "allocationProtect=0x%X,type=0x%X" %
                (self.base_address, self.region_size, self.state,
                 self.protect, self.allocation_protect, self.memory_type))


class _MEMORY_BASIC_INFORMATION(ctypes.Structure):
    _fields_ = [
        ("BaseAddress", ctypes.c_void_p),
        ("AllocationBase", ctypes.c_void_p),
        ("AllocationProtect", wintypes.DWORD),
        ("RegionSize", ctypes.c_size_t),
        ("State", wintypes.DWORD),
        ("Protect", wintypes.DWORD),
        ("Type", wintypes.DWORD),
    ]


class _MODULEENTRY32W(ctypes.Structure):
    _fields_ = [
        ("dwSize", wintypes.DWORD),
        ("th32ModuleID", wintypes.DWORD),
        ("th32ProcessID", wintypes.DWORD),
        ("GlblcntUsage", wintypes.DWORD),
        ("ProccntUsage", wintypes.DWORD),
        ("modBaseAddr", ctypes.POINTER(ctypes.c_byte)),
        ("modBaseSize", wintypes.DWORD),
        ("hModule", wintypes.HMODULE),
        ("szModule", wintypes.WCHAR * 256),
        ("szExePath", wintypes.WCHAR * 260),
    ]


class _THREADENTRY32(ctypes.Structure):
    _fields_ = [
        ("dwSize", wintypes.DWORD),
        ("cntUsage", wintypes.DWORD),
        ("th32ThreadID", wintypes.DWORD),
        ("th32OwnerProcessID", wintypes.DWORD),
        ("tpBasePri", wintypes.LONG),
        ("tpDeltaPri", wintypes.LONG),
        ("dwFlags", wintypes.DWORD),
    ]


class _FLOATING_SAVE_AREA(ctypes.Structure):
    _fields_ = [
        ("ControlWord", wintypes.DWORD), ("StatusWord", wintypes.DWORD),
        ("TagWord", wintypes.DWORD), ("ErrorOffset", wintypes.DWORD),
        ("ErrorSelector", wintypes.DWORD), ("DataOffset", wintypes.DWORD),
        ("DataSelector", wintypes.DWORD), ("RegisterArea", ctypes.c_byte * 80),
        ("Cr0NpxState", wintypes.DWORD),
    ]


class _CONTEXT32(ctypes.Structure):
    _fields_ = [
        ("ContextFlags", wintypes.DWORD),
        ("Dr0", wintypes.DWORD), ("Dr1", wintypes.DWORD),
        ("Dr2", wintypes.DWORD), ("Dr3", wintypes.DWORD),
        ("Dr6", wintypes.DWORD), ("Dr7", wintypes.DWORD),
        ("FloatSave", _FLOATING_SAVE_AREA),
        ("SegGs", wintypes.DWORD), ("SegFs", wintypes.DWORD),
        ("SegEs", wintypes.DWORD), ("SegDs", wintypes.DWORD),
        ("Edi", wintypes.DWORD), ("Esi", wintypes.DWORD),
        ("Ebx", wintypes.DWORD), ("Edx", wintypes.DWORD),
        ("Ecx", wintypes.DWORD), ("Eax", wintypes.DWORD),
        ("Ebp", wintypes.DWORD), ("Eip", wintypes.DWORD),
        ("SegCs", wintypes.DWORD), ("EFlags", wintypes.DWORD),
        ("Esp", wintypes.DWORD), ("SegSs", wintypes.DWORD),
        ("ExtendedRegisters", ctypes.c_byte * 512),
    ]


class _SID_AND_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Sid", ctypes.c_void_p), ("Attributes", wintypes.DWORD)]


class _TOKEN_MANDATORY_LABEL(ctypes.Structure):
    _fields_ = [("Label", _SID_AND_ATTRIBUTES)]


class _PROCESS_MITIGATION_DYNAMIC_CODE_POLICY(ctypes.Structure):
    _fields_ = [("Flags", wintypes.DWORD)]


def _integrity_name(rid: int) -> str:
    if rid < 0x1000:
        return "Untrusted"
    if rid < 0x2000:
        return "Low"
    if rid < 0x3000:
        return "Medium"
    if rid < 0x4000:
        return "High"
    if rid < 0x5000:
        return "System"
    return "Protected"


def query_process_security(process_handle: int) -> Dict[str, object]:
    """Best-effort integrity and dynamic-code diagnostics for one process."""
    if os.name != "nt":
        return {}
    result: Dict[str, object] = {}
    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    token = wintypes.HANDLE()
    advapi32.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD,
                                          ctypes.POINTER(wintypes.HANDLE)]
    advapi32.OpenProcessToken.restype = wintypes.BOOL
    advapi32.GetTokenInformation.argtypes = [
        wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD,
        ctypes.POINTER(wintypes.DWORD)]
    advapi32.GetTokenInformation.restype = wintypes.BOOL
    advapi32.GetSidSubAuthorityCount.argtypes = [ctypes.c_void_p]
    advapi32.GetSidSubAuthorityCount.restype = ctypes.POINTER(ctypes.c_ubyte)
    advapi32.GetSidSubAuthority.argtypes = [ctypes.c_void_p, wintypes.DWORD]
    advapi32.GetSidSubAuthority.restype = ctypes.POINTER(wintypes.DWORD)
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL
    if advapi32.OpenProcessToken(
            wintypes.HANDLE(process_handle), TOKEN_QUERY, ctypes.byref(token)):
        try:
            needed = wintypes.DWORD(0)
            advapi32.GetTokenInformation(
                token, TOKEN_INTEGRITY_LEVEL, None, 0, ctypes.byref(needed))
            if needed.value:
                buffer = ctypes.create_string_buffer(needed.value)
                if advapi32.GetTokenInformation(
                        token, TOKEN_INTEGRITY_LEVEL, buffer, needed,
                        ctypes.byref(needed)):
                    label = ctypes.cast(
                        buffer, ctypes.POINTER(_TOKEN_MANDATORY_LABEL)).contents
                    count_ptr = advapi32.GetSidSubAuthorityCount(label.Label.Sid)
                    if count_ptr and count_ptr[0]:
                        rid_ptr = advapi32.GetSidSubAuthority(
                            label.Label.Sid, count_ptr[0] - 1)
                        if rid_ptr:
                            rid = int(rid_ptr[0])
                            result["integrityRid"] = rid
                            result["integrity"] = _integrity_name(rid)
        finally:
            kernel32.CloseHandle(token)
    else:
        result["integrityError"] = ctypes.get_last_error()

    policy = _PROCESS_MITIGATION_DYNAMIC_CODE_POLICY()
    get_policy = getattr(kernel32, "GetProcessMitigationPolicy", None)
    if get_policy is not None:
        get_policy.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p,
                               ctypes.c_size_t]
        get_policy.restype = wintypes.BOOL
        if get_policy(wintypes.HANDLE(process_handle),
                      PROCESS_DYNAMIC_CODE_POLICY, ctypes.byref(policy),
                      ctypes.sizeof(policy)):
            result["dynamicCodeFlags"] = int(policy.Flags)
            result["prohibitDynamicCode"] = bool(policy.Flags & 0x1)
        else:
            result["dynamicCodePolicyError"] = ctypes.get_last_error()
    return result


@contextmanager
def suspended_thread_eips(pid: int):
    """Suspend all target threads and yield their x86 instruction pointers."""
    if os.name != "nt":
        raise MemoryAccessError("动态 stub 线程检查只支持 Windows")
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    wow64 = ctypes.sizeof(ctypes.c_void_p) == 8
    kernel32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    kernel32.Thread32First.argtypes = [wintypes.HANDLE, ctypes.POINTER(_THREADENTRY32)]
    kernel32.Thread32First.restype = wintypes.BOOL
    kernel32.Thread32Next.argtypes = [wintypes.HANDLE, ctypes.POINTER(_THREADENTRY32)]
    kernel32.Thread32Next.restype = wintypes.BOOL
    kernel32.OpenThread.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.OpenThread.restype = wintypes.HANDLE
    kernel32.SuspendThread.argtypes = [wintypes.HANDLE]
    kernel32.SuspendThread.restype = wintypes.DWORD
    kernel32.ResumeThread.argtypes = [wintypes.HANDLE]
    kernel32.ResumeThread.restype = wintypes.DWORD
    context_api = (kernel32.Wow64GetThreadContext if wow64
                   else kernel32.GetThreadContext)
    context_api.argtypes = [wintypes.HANDLE, ctypes.POINTER(_CONTEXT32)]
    context_api.restype = wintypes.BOOL
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL
    snapshot = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0)
    if snapshot in (0, -1, ctypes.c_void_p(-1).value):
        raise MemoryAccessError("枚举目标线程失败")
    suspended = []
    eips = []
    try:
        entry = _THREADENTRY32()
        entry.dwSize = ctypes.sizeof(entry)
        ok = kernel32.Thread32First(snapshot, ctypes.byref(entry))
        while ok:
            if int(entry.th32OwnerProcessID) == int(pid):
                access = (THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT |
                          THREAD_QUERY_INFORMATION)
                handle = kernel32.OpenThread(access, False, entry.th32ThreadID)
                if not handle:
                    raise MemoryAccessError("打开目标线程失败")
                if kernel32.SuspendThread(handle) == 0xFFFFFFFF:
                    kernel32.CloseHandle(handle)
                    raise MemoryAccessError("暂停目标线程失败")
                suspended.append(handle)
                context = _CONTEXT32()
                context.ContextFlags = CONTEXT_CONTROL
                if not context_api(handle, ctypes.byref(context)):
                    raise MemoryAccessError("读取目标线程 EIP 失败")
                eips.append(int(context.Eip))
            ok = kernel32.Thread32Next(snapshot, ctypes.byref(entry))
        yield tuple(eips)
    finally:
        for handle in reversed(suspended):
            kernel32.ResumeThread(handle)
            kernel32.CloseHandle(handle)
        kernel32.CloseHandle(snapshot)


def get_process_module(pid: int,
                       module_name: Optional[str] = None) -> Tuple[int, int, str]:
    """Return actual module base, image size and path for a 32-bit process."""
    if os.name != "nt":
        raise MemoryAccessError("模块枚举只支持 Windows")
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    kernel32.Module32FirstW.argtypes = [wintypes.HANDLE,
                                        ctypes.POINTER(_MODULEENTRY32W)]
    kernel32.Module32FirstW.restype = wintypes.BOOL
    kernel32.Module32NextW.argtypes = [wintypes.HANDLE,
                                       ctypes.POINTER(_MODULEENTRY32W)]
    kernel32.Module32NextW.restype = wintypes.BOOL
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL
    snapshot = kernel32.CreateToolhelp32Snapshot(0x00000008 | 0x00000010, int(pid))
    if snapshot in (0, -1, ctypes.c_void_p(-1).value):
        raise MemoryAccessError("枚举游戏模块失败，WinError=%d" % ctypes.get_last_error())
    try:
        entry = _MODULEENTRY32W()
        entry.dwSize = ctypes.sizeof(entry)
        ok = kernel32.Module32FirstW(snapshot, ctypes.byref(entry))
        while ok:
            if module_name is None or entry.szModule.lower() == module_name.lower():
                return (ctypes.cast(entry.modBaseAddr, ctypes.c_void_p).value or 0,
                        int(entry.modBaseSize), entry.szExePath)
            ok = kernel32.Module32NextW(snapshot, ctypes.byref(entry))
    finally:
        kernel32.CloseHandle(snapshot)
    if module_name is None:
        raise MemoryAccessError("目标进程没有可枚举的主模块")
    raise MemoryAccessError("目标进程中没有找到 %s 模块" % module_name)


class Win32Backend:
    def __init__(self, handle: int):
        if os.name != "nt":
            raise MemoryAccessError("进程内存调试只支持 Windows")
        self.handle = int(handle)
        self.kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self._declare_api()

    def _declare_api(self) -> None:
        k32 = self.kernel32
        k32.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p,
                                          ctypes.c_void_p, ctypes.c_size_t,
                                          ctypes.POINTER(ctypes.c_size_t)]
        k32.ReadProcessMemory.restype = wintypes.BOOL
        k32.WriteProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p,
                                           ctypes.c_void_p, ctypes.c_size_t,
                                           ctypes.POINTER(ctypes.c_size_t)]
        k32.WriteProcessMemory.restype = wintypes.BOOL
        k32.FlushInstructionCache.argtypes = [wintypes.HANDLE, ctypes.c_void_p,
                                               ctypes.c_size_t]
        k32.FlushInstructionCache.restype = wintypes.BOOL
        k32.VirtualQueryEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p,
                                       ctypes.POINTER(_MEMORY_BASIC_INFORMATION),
                                       ctypes.c_size_t]
        k32.VirtualQueryEx.restype = ctypes.c_size_t
        k32.VirtualProtectEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p,
                                         ctypes.c_size_t, wintypes.DWORD,
                                         ctypes.POINTER(wintypes.DWORD)]
        k32.VirtualProtectEx.restype = wintypes.BOOL
        k32.VirtualAllocEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p,
                                       ctypes.c_size_t, wintypes.DWORD,
                                       wintypes.DWORD]
        k32.VirtualAllocEx.restype = ctypes.c_void_p
        k32.VirtualFreeEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p,
                                      ctypes.c_size_t, wintypes.DWORD]
        k32.VirtualFreeEx.restype = wintypes.BOOL
        k32.CreateRemoteThread.argtypes = [wintypes.HANDLE, ctypes.c_void_p,
                                           ctypes.c_size_t, ctypes.c_void_p,
                                           ctypes.c_void_p, wintypes.DWORD,
                                           ctypes.POINTER(wintypes.DWORD)]
        k32.CreateRemoteThread.restype = wintypes.HANDLE
        k32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        k32.WaitForSingleObject.restype = wintypes.DWORD
        k32.CloseHandle.argtypes = [wintypes.HANDLE]
        k32.CloseHandle.restype = wintypes.BOOL

    def read(self, address: int, size: int) -> bytes:
        buffer = ctypes.create_string_buffer(size)
        transferred = ctypes.c_size_t(0)
        ok = self.kernel32.ReadProcessMemory(
            self.handle, ctypes.c_void_p(address), buffer, size,
            ctypes.byref(transferred))
        if not ok or transferred.value != size:
            raise MemoryAccessError(
                "读取 0x%08X 失败，实际 %d/%d 字节，WinError=%d" %
                (address, transferred.value, size, ctypes.get_last_error()))
        return buffer.raw

    def write(self, address: int, data: bytes) -> int:
        transferred = ctypes.c_size_t(0)
        buffer = ctypes.create_string_buffer(data, len(data))
        ok = self.kernel32.WriteProcessMemory(
            self.handle, ctypes.c_void_p(address), buffer, len(data),
            ctypes.byref(transferred))
        if not ok:
            raise MemoryAccessError(
                "写入 0x%08X 失败，实际 %d/%d 字节，WinError=%d" %
                (address, transferred.value, len(data), ctypes.get_last_error()))
        return int(transferred.value)

    def flush(self, address: int, size: int) -> None:
        if not self.kernel32.FlushInstructionCache(
                self.handle, ctypes.c_void_p(address), size):
            raise MemoryAccessError("刷新指令缓存失败，WinError=%d" %
                                    ctypes.get_last_error())

    def query_page(self, address: int) -> MemoryPageInfo:
        info = _MEMORY_BASIC_INFORMATION()
        result = self.kernel32.VirtualQueryEx(
            self.handle, ctypes.c_void_p(address), ctypes.byref(info),
            ctypes.sizeof(info))
        if not result:
            raise MemoryAccessError("查询内存页 0x%08X 失败" % address)
        return MemoryPageInfo(
            int(info.BaseAddress or 0), int(info.AllocationBase or 0),
            int(info.AllocationProtect), int(info.RegionSize),
            int(info.State), int(info.Protect), int(info.Type))

    def query(self, address: int) -> Tuple[int, int, int, int]:
        info = self.query_page(address)
        return (info.base_address, info.region_size, info.state,
                info.protect)

    def protect(self, address: int, size: int, protection: int) -> int:
        old = wintypes.DWORD(0)
        if not self.kernel32.VirtualProtectEx(
                self.handle, ctypes.c_void_p(address), size, protection,
                ctypes.byref(old)):
            raise MemoryAccessError("修改内存页保护失败，WinError=%d" % ctypes.get_last_error())
        return int(old.value)

    def allocate(self, size: int, protection: int = PAGE_READWRITE) -> int:
        address = self.kernel32.VirtualAllocEx(
            self.handle, None, size, 0x1000 | 0x2000, protection)
        if not address:
            raise MemoryAccessError("分配远程内存失败，WinError=%d" % ctypes.get_last_error())
        return int(address)

    def free(self, address: int) -> None:
        if address and not self.kernel32.VirtualFreeEx(
                self.handle, ctypes.c_void_p(address), 0, MEM_RELEASE):
            raise MemoryAccessError("释放远程内存失败，WinError=%d" % ctypes.get_last_error())

    def create_thread(self, start: int, parameter: int) -> int:
        thread_id = wintypes.DWORD(0)
        handle = self.kernel32.CreateRemoteThread(
            self.handle, None, 0, ctypes.c_void_p(start),
            ctypes.c_void_p(parameter), 0, ctypes.byref(thread_id))
        if not handle:
            raise MemoryAccessError("创建远程线程失败，WinError=%d" % ctypes.get_last_error())
        return int(handle)

    def wait_thread(self, thread: int, timeout_ms: int) -> None:
        result = self.kernel32.WaitForSingleObject(thread, timeout_ms)
        if result == 0x00000102:
            raise MemoryAccessError("远程线程执行超时")
        if result != 0:
            raise MemoryAccessError("等待远程线程失败，返回值=0x%08X" % result)

    def close_thread(self, thread: int) -> None:
        if thread:
            self.kernel32.CloseHandle(thread)

    def close(self) -> None:
        if self.handle:
            self.kernel32.CloseHandle(self.handle)
            self.handle = 0


@dataclass(frozen=True)
class DynamicPatchLease:
    spec: DynamicPatchSpec
    allocation: int
    allocation_size: int
    branch_bytes: Tuple[bytes, ...]


class MemorySession:
    """A process session compatible with old Read/WriteProcessMemory calls."""

    def __init__(self, handle: int, module_base: int, module_size: int,
                 backend=None, owns_handle: bool = True,
                 reference_image_base: int = REFERENCE_IMAGE_BASE,
                 pid: Optional[int] = None, direct_write: bool = True,
                 read_only: bool = False):
        self.handle = int(handle)
        self.pid = pid
        self.module_base = int(module_base)
        self.module_size = int(module_size)
        self.reference_image_base = int(reference_image_base)
        self.backend = backend if backend is not None else Win32Backend(handle)
        self.owns_handle = owns_handle
        self.direct_write = bool(direct_write)
        self.read_only = bool(read_only)
        self.closed = False
        self.last_error = ""
        self.error_handler = None
        self._active_patches: Dict[str, PatchSpec] = {}
        self._dynamic_leases: Dict[str, DynamicPatchLease] = {}
        self._orphaned_allocations = []
        self._write_guards: Dict[str, Tuple[int, bytearray]] = {}
        self.code_patch_poisoned = False
        self.last_code_patch_diagnostic: Dict[str, object] = {}
        # A timed-out remote thread may still be executing inside the target;
        # never submit another call on the same session until it is closed.
        self.remote_call_poisoned = False
        self.last_remote_call_diagnostic: Dict[str, object] = {}
        self.security_context: Dict[str, object] = {}
        if isinstance(self.backend, Win32Backend):
            try:
                self.security_context = self.process_security_diagnostics()
            except Exception as exc:
                self.security_context = {"queryError": str(exc)}

    @classmethod
    def open_pid(cls, pid: int, module_base: int, module_size: int,
                 reference_image_base: int = REFERENCE_IMAGE_BASE,
                 read_only: bool = False) -> "MemorySession":
        if os.name != "nt":
            raise MemoryAccessError("进程内存调试只支持 Windows")
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel32.OpenProcess.restype = wintypes.HANDLE
        access = (PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | SYNCHRONIZE)
        if not read_only:
            access |= PROCESS_VM_OPERATION | PROCESS_VM_WRITE
        handle = kernel32.OpenProcess(access, False, int(pid))
        if not handle:
            raise MemoryAccessError("打开游戏进程失败，WinError=%d" % ctypes.get_last_error())
        return cls(int(handle), module_base, module_size, owns_handle=True, pid=pid,
                   reference_image_base=reference_image_base,
                   read_only=read_only)

    def resolve_reference(self, address: int) -> int:
        upper = self.reference_image_base + self.module_size
        if self.reference_image_base <= address < upper:
            return self.module_base + address - self.reference_image_base
        return address

    def validate_range(self, address: int, size: int, write: bool = False) -> bool:
        if self.closed or address <= 0 or size <= 0:
            return False
        address = self.resolve_reference(address)
        return self.validate_actual_range(address, size, write)

    def validate_actual_range(self, address: int, size: int,
                              write: bool = False) -> bool:
        if self.closed or address <= 0 or size <= 0:
            return False
        end = address + size
        cursor = address
        try:
            while cursor < end:
                base, region_size, state, protection = self.backend.query(cursor)
                if state != MEM_COMMIT or protection & (PAGE_NOACCESS | PAGE_GUARD):
                    return False
                normalized = protection & 0xFF
                allowed = WRITABLE_PROTECTIONS if write else READABLE_PROTECTIONS
                if normalized not in allowed:
                    return False
                region_end = base + region_size
                if region_end <= cursor:
                    return False
                cursor = min(region_end, end)
            return True
        except MemoryAccessError:
            return False

    def query_page(self, address: int, actual_address: bool = False) -> MemoryPageInfo:
        self._check_open()
        actual = int(address) if actual_address else self.resolve_reference(int(address))
        if hasattr(self.backend, "query_page"):
            return self.backend.query_page(actual)
        base, size, state, protect = self.backend.query(actual)
        return MemoryPageInfo(base, base, protect, size, state, protect, MEM_IMAGE)

    def process_security_diagnostics(self) -> Dict[str, object]:
        if os.name != "nt" or isinstance(self.backend, FakeMemoryBackend):
            return {}
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.GetCurrentProcess.restype = wintypes.HANDLE
        current = int(kernel32.GetCurrentProcess())
        target = int(getattr(self.backend, "handle", self.handle))
        result = {
            "tool": query_process_security(current),
            "target": query_process_security(target),
        }
        self.security_context = result
        return result

    @staticmethod
    def _coerce_compare_entry(raw_entry) -> CompareWriteEntry:
        if isinstance(raw_entry, CompareWriteEntry):
            return raw_entry
        if isinstance(raw_entry, dict):
            return CompareWriteEntry(
                int(raw_entry["address"]), bytes(raw_entry["expected"]),
                bytes(raw_entry["replacement"]),
                bool(raw_entry.get("actual_address", False)))
        values = tuple(raw_entry)
        if len(values) not in (3, 4):
            raise MemoryAccessError("代码事务条目必须包含地址、原值和新值")
        return CompareWriteEntry(
            int(values[0]), bytes(values[1]), bytes(values[2]),
            bool(values[3]) if len(values) == 4 else False)

    def _code_patch_pages(self, prepared) -> List[Tuple[int, int, MemoryPageInfo]]:
        pages: Dict[int, Tuple[int, int, MemoryPageInfo]] = {}
        page_size = int(getattr(self.backend, "page_size", 0x1000))
        if page_size <= 0 or page_size & (page_size - 1):
            page_size = 0x1000
        module_end = self.module_base + self.module_size
        for actual, _expected, replacement in prepared:
            end = actual + len(replacement)
            if actual < self.module_base or end > module_end:
                raise PatchError(
                    "代码补丁超出当前主模块范围: 0x%08X+0x%X" %
                    (actual, len(replacement)))
            cursor = actual
            while cursor < end:
                info = self.query_page(cursor, actual_address=True)
                if (info.state != MEM_COMMIT or
                        info.protect & (PAGE_NOACCESS | PAGE_GUARD)):
                    raise PatchError("代码补丁页不可访问: %s" % info.describe())
                if info.memory_type not in (0, MEM_IMAGE):
                    raise PatchError("代码补丁目标不是主模块映像页: %s" %
                                     info.describe())
                page_base = cursor & ~(page_size - 1)
                segment_start = max(page_base, info.base_address)
                segment_end = min(page_base + page_size, info.end_address)
                if segment_end <= cursor:
                    raise PatchError("代码补丁页范围无效: %s" % info.describe())
                pages.setdefault(page_base, (
                    segment_start, segment_end - segment_start, info))
                cursor = min(end, segment_end)
        return [pages[key] for key in sorted(pages)]

    def write_code_transaction(self, entries: Iterable,
                               feature_key: str = "",
                               label: str = "代码补丁",
                               allow_poisoned_restore: bool = False) -> None:
        """Compare, temporarily unprotect and atomically replace code bytes."""
        self._check_open()
        self._ensure_writable("代码补丁")
        if self.direct_write:
            for raw_entry in entries:
                entry = self._coerce_compare_entry(raw_entry)
                actual = (entry.address if entry.actual_address else
                          self.resolve_reference(entry.address))
                previous = None
                try:
                    previous = self.backend.protect(
                        actual, len(entry.replacement),
                        PAGE_EXECUTE_READWRITE)
                except Exception:
                    previous = None
                try:
                    transferred = self.backend.write(
                        actual, entry.replacement)
                    if transferred != len(entry.replacement):
                        raise MemoryAccessError(
                            "代码页只直写了部分字节 0x%08X" % actual)
                    if hasattr(self.backend, "flush"):
                        self.backend.flush(actual, len(entry.replacement))
                finally:
                    if previous is not None:
                        try:
                            self.backend.protect(
                                actual, len(entry.replacement), previous)
                        except Exception:
                            pass
            return
        if self.code_patch_poisoned and not allow_poisoned_restore:
            raise PatchError("代码页保护曾恢复失败；请重新启动游戏后再操作代码补丁")

        prepared = []
        seen = []
        for raw_entry in entries:
            entry = self._coerce_compare_entry(raw_entry)
            actual = (entry.address if entry.actual_address else
                      self.resolve_reference(entry.address))
            current_range = (actual, actual + len(entry.expected))
            if any(current_range[0] < end and start < current_range[1]
                   for start, end in seen):
                raise PatchError("代码补丁事务范围不能重叠")
            current = self.backend.read(actual, len(entry.expected))
            if current != entry.expected:
                raise PatchError(
                    "%s在 0x%08X 的当前字节为 %s，预期 %s" %
                    (label, actual, current.hex(" ").upper(),
                     entry.expected.hex(" ").upper()))
            prepared.append((actual, entry.expected, entry.replacement))
            seen.append(current_range)
        if not prepared:
            return

        page_ranges = self._code_patch_pages(prepared)
        diagnostic = {
            "feature": feature_key, "label": label,
            "moduleBase": self.module_base,
            "sites": [{"address": actual, "current": expected.hex().upper(),
                       "target": replacement.hex().upper()}
                      for actual, expected, replacement in prepared],
            "pages": [info.describe() for _start, _size, info in page_ranges],
            "protectAttempted": False, "writeCompleted": False,
            "readbackCompleted": False, "flushCompleted": False,
            "protectionRestored": False,
        }
        self.last_code_patch_diagnostic = diagnostic
        protected = []
        changed = []
        operation_error: Optional[Exception] = None
        rollback_errors: List[str] = []
        restore_errors: List[str] = []
        try:
            diagnostic["protectAttempted"] = True
            for start, size, info in page_ranges:
                try:
                    previous = self.backend.protect(
                        start, size, PAGE_EXECUTE_READWRITE)
                except Exception as exc:
                    raise PatchError(
                        "%s已定位，但代码页拒绝切换为可写：%s；%s" %
                        (label, exc, info.describe())) from exc
                protected.append((start, size, previous, info))

            for actual, expected, replacement in prepared:
                changed.append((actual, expected))
                transferred = self.backend.write(actual, replacement)
                if transferred != len(replacement):
                    raise MemoryAccessError(
                        "部分写入 0x%08X，实际 %d/%d 字节" %
                        (actual, transferred, len(replacement)))
                if self.backend.read(actual, len(replacement)) != replacement:
                    raise MemoryAccessError("代码补丁写后复读不一致: 0x%08X" %
                                            actual)
            diagnostic["writeCompleted"] = True
            diagnostic["readbackCompleted"] = True
            for actual, _expected, replacement in prepared:
                self.backend.flush(actual, len(replacement))
            diagnostic["flushCompleted"] = True
        except Exception as exc:
            operation_error = exc
            for actual, expected in reversed(changed):
                try:
                    if self.backend.read(actual, len(expected)) != expected:
                        transferred = self.backend.write(actual, expected)
                        if transferred != len(expected):
                            raise MemoryAccessError("部分回滚")
                        if self.backend.read(actual, len(expected)) != expected:
                            raise MemoryAccessError("回滚复读不一致")
                    self.backend.flush(actual, len(expected))
                except Exception as rollback_exc:
                    rollback_errors.append("0x%08X: %s" %
                                           (actual, rollback_exc))
        finally:
            for start, size, previous, info in reversed(protected):
                try:
                    self.backend.protect(start, size, previous)
                except Exception as exc:
                    restore_errors.append("%s: %s" % (info.describe(), exc))
            if restore_errors and operation_error is None:
                # A patch is not considered enabled while its page remains RWX.
                # Reopen every page, restore the byte snapshots, then make one
                # final best-effort protection restore.
                diagnostic["rolledBackAfterRestoreFailure"] = True
                for start, size, _previous, info in protected:
                    try:
                        self.backend.protect(start, size,
                                             PAGE_EXECUTE_READWRITE)
                    except Exception as exc:
                        rollback_errors.append(
                            "%s reopen: %s" % (info.describe(), exc))
                for actual, expected in reversed(changed):
                    try:
                        if self.backend.read(actual, len(expected)) != expected:
                            if self.backend.write(actual, expected) != len(expected):
                                raise MemoryAccessError("部分回滚")
                            if self.backend.read(actual, len(expected)) != expected:
                                raise MemoryAccessError("回滚复读不一致")
                        self.backend.flush(actual, len(expected))
                    except Exception as exc:
                        rollback_errors.append("0x%08X: %s" % (actual, exc))
                second_restore_errors = []
                for start, size, previous, info in reversed(protected):
                    try:
                        self.backend.protect(start, size, previous)
                    except Exception as exc:
                        second_restore_errors.append(
                            "%s: %s" % (info.describe(), exc))
                restore_errors.extend(second_restore_errors)
            diagnostic["protectionRestored"] = not restore_errors
            diagnostic["rollbackErrors"] = tuple(rollback_errors)
            diagnostic["restoreErrors"] = tuple(restore_errors)
            if restore_errors:
                self.code_patch_poisoned = True

        if operation_error is not None or rollback_errors or restore_errors:
            security = self.process_security_diagnostics()
            if security:
                diagnostic["security"] = security
            diagnostic["operationError"] = (str(operation_error)
                                            if operation_error is not None else "")
            if operation_error is not None and "WinError=5" in str(operation_error):
                parts = ["%s：地址已定位，代码页写入被拒绝；%s" %
                         (label, operation_error)]
            else:
                parts = ([str(operation_error)]
                         if operation_error is not None else [])
            if rollback_errors:
                parts.append("补丁字节回滚失败：%s" % "；".join(rollback_errors))
            if restore_errors:
                parts.append("代码页保护恢复失败：%s；请重新启动游戏" %
                             "；".join(restore_errors))
            if security:
                tool_security = security.get("tool", {})
                target_security = security.get("target", {})
                parts.append(
                    "完整性级别 tool=%s,target=%s,dynamicCodeFlags=%s" %
                    (tool_security.get("integrity", "unknown"),
                     target_security.get("integrity", "unknown"),
                     target_security.get("dynamicCodeFlags", "unknown")))
            parts.append("页面：%s" % " | ".join(diagnostic["pages"]))
            raise PatchError("；".join(parts)) from operation_error

    def write_code_bytes(self, address: int, data: bytes,
                         expected: Optional[bytes] = None,
                         feature_key: str = "",
                         label: str = "代码补丁") -> None:
        actual = self.resolve_reference(int(address))
        current = (self.backend.read(actual, len(data)) if expected is None
                   else bytes(expected))
        self.write_code_transaction((
            CompareWriteEntry(int(address), current, bytes(data)),
        ), feature_key=feature_key, label=label)

    def _check_open(self) -> None:
        if self.closed:
            raise MemoryAccessError("游戏进程会话已关闭")

    def _ensure_writable(self, operation: str = "写入") -> None:
        if self.read_only:
            raise MemoryAccessError("只读进程会话禁止%s" % operation)

    def read_bytes(self, address: int, size: int) -> bytes:
        self._check_open()
        actual = self.resolve_reference(address)
        if not self.validate_actual_range(actual, size, write=False):
            raise MemoryAccessError("拒绝读取无效或不可读范围 0x%08X+0x%X" % (actual, size))
        return self.backend.read(actual, size)

    def read_actual_bytes(self, address: int, size: int) -> bytes:
        self._check_open()
        if not self.validate_actual_range(address, size, write=False):
            raise MemoryAccessError("拒绝读取无效运行时范围 0x%08X+0x%X" %
                                    (address, size))
        return self.backend.read(address, size)

    def read_many(self, address: int, size: int) -> bytes:
        return self.read_bytes(address, size)

    def read_u8(self, address: int) -> int:
        return self.read_bytes(address, 1)[0]

    def read_u16(self, address: int) -> int:
        return struct.unpack("<H", self.read_bytes(address, 2))[0]

    def read_u32(self, address: int) -> int:
        return struct.unpack("<I", self.read_bytes(address, 4))[0]

    def write_bytes(self, address: int, data: bytes, verify: bool = True,
                    allow_protect: bool = False) -> None:
        self._check_open()
        self._ensure_writable()
        if not isinstance(data, bytes) or not data:
            raise MemoryAccessError("写入数据必须是非空 bytes")
        actual = self.resolve_reference(address)
        restore_protection: Optional[int] = None
        if self.direct_write:
            if actual <= 0:
                raise MemoryAccessError("拒绝写入空地址")
            if not self.validate_actual_range(actual, len(data), write=True):
                if not self.validate_actual_range(actual, len(data), write=False):
                    raise MemoryAccessError(
                        "写入范围不可访问 0x%08X+0x%X" %
                        (actual, len(data)))
                try:
                    restore_protection = self.backend.protect(
                        actual, len(data), PAGE_EXECUTE_READWRITE)
                except Exception:
                    restore_protection = None
            try:
                transferred = self.backend.write(actual, data)
                if transferred != len(data):
                    raise MemoryAccessError(
                        "部分直写 0x%08X，实际 %d/%d 字节" %
                        (actual, transferred, len(data)))
                if hasattr(self.backend, "flush"):
                    self.backend.flush(actual, len(data))
            finally:
                if restore_protection is not None:
                    try:
                        self.backend.protect(
                            actual, len(data), restore_protection)
                    except Exception:
                        pass
            return
        if not self.validate_actual_range(actual, len(data), write=True):
            if not allow_protect or not self.validate_actual_range(actual, len(data), write=False):
                raise MemoryAccessError("拒绝写入无效或不可写范围 0x%08X+0x%X" %
                                        (actual, len(data)))
            restore_protection = self.backend.protect(
                actual, len(data), PAGE_EXECUTE_READWRITE)
        try:
            transferred = self.backend.write(actual, data)
            if transferred != len(data):
                raise MemoryAccessError("部分写入 0x%08X，实际 %d/%d 字节" %
                                        (actual, transferred, len(data)))
            if verify and self.backend.read(actual, len(data)) != data:
                raise MemoryAccessError("写后复读不一致: 0x%08X" % actual)
        finally:
            if restore_protection is not None:
                self.backend.protect(actual, len(data), restore_protection)

    def write_actual_bytes(self, address: int, data: bytes,
                           verify: bool = True) -> None:
        self._check_open()
        self._ensure_writable()
        if self.direct_write:
            if address <= 0 or not isinstance(data, bytes) or not data:
                raise MemoryAccessError("直写地址或数据无效")
            restore_protection: Optional[int] = None
            if not self.validate_actual_range(address, len(data), write=True):
                if not self.validate_actual_range(address, len(data), write=False):
                    raise MemoryAccessError(
                        "写入范围不可访问 0x%08X+0x%X" %
                        (address, len(data)))
                try:
                    restore_protection = self.backend.protect(
                        address, len(data), PAGE_EXECUTE_READWRITE)
                except Exception:
                    restore_protection = None
            try:
                transferred = self.backend.write(address, data)
                if transferred != len(data):
                    raise MemoryAccessError("运行时范围只直写了部分字节")
                if hasattr(self.backend, "flush"):
                    self.backend.flush(address, len(data))
            finally:
                if restore_protection is not None:
                    try:
                        self.backend.protect(
                            address, len(data), restore_protection)
                    except Exception:
                        pass
            return
        if not self.validate_actual_range(address, len(data), write=True):
            raise MemoryAccessError("拒绝写入无效运行时范围 0x%08X+0x%X" %
                                    (address, len(data)))
        transferred = self.backend.write(address, data)
        if transferred != len(data):
            raise MemoryAccessError("运行时范围只写入了部分字节")
        if verify and self.backend.read(address, len(data)) != data:
            raise MemoryAccessError("运行时范围写后复读不一致")

    def write_u8(self, address: int, value: int) -> None:
        self._check_value(value, 8)
        self.write_bytes(address, struct.pack("<B", value))

    def write_u16(self, address: int, value: int) -> None:
        self._check_value(value, 16)
        self.write_bytes(address, struct.pack("<H", value))

    def write_u32(self, address: int, value: int) -> None:
        self._check_value(value, 32)
        self.write_bytes(address, struct.pack("<I", value))

    def write_transaction(self, entries) -> None:
        """Write data ranges atomically, restoring earlier ranges on failure."""
        if self.direct_write:
            for address, data in entries:
                self.write_bytes(int(address), bytes(data), verify=False)
            return
        prepared = []
        seen = []
        for address, data in entries:
            if not isinstance(data, bytes) or not data:
                raise MemoryAccessError("事务写入数据必须是非空 bytes")
            actual = self.resolve_reference(int(address))
            current_range = (actual, actual + len(data))
            if any(current_range[0] < end and start < current_range[1]
                   for start, end in seen):
                raise MemoryAccessError("事务写入范围不能重叠")
            if not self.validate_actual_range(actual, len(data), write=True):
                raise MemoryAccessError("事务写入范围无效或不可写: 0x%08X" % actual)
            previous = self.backend.read(actual, len(data))
            prepared.append((int(address), data, previous))
            seen.append(current_range)
        changed = []
        try:
            for address, data, previous in prepared:
                self.write_bytes(address, data)
                changed.append((address, previous))
        except Exception:
            for address, previous in reversed(changed):
                try:
                    self.write_bytes(address, previous)
                except Exception:
                    pass
            raise

    def compare_exchange_transaction(self, entries) -> None:
        """Compare all ranges, then replace them as one rollback transaction.

        Accepted entries are ``CompareWriteEntry`` objects, mappings with
        address/expected/replacement keys, or three-item tuples.  A four-item
        tuple can set ``actual_address`` for pointer-backed runtime tables.
        No write occurs when any expected byte differs from current memory.
        """
        if self.direct_write:
            for raw_entry in entries:
                entry = self._coerce_compare_entry(raw_entry)
                if entry.actual_address:
                    self.write_actual_bytes(
                        entry.address, entry.replacement, verify=False)
                else:
                    self.write_bytes(
                        entry.address, entry.replacement, verify=False)
            return
        prepared = []
        seen = []
        for raw_entry in entries:
            if isinstance(raw_entry, CompareWriteEntry):
                entry = raw_entry
            elif isinstance(raw_entry, dict):
                entry = CompareWriteEntry(
                    int(raw_entry["address"]), bytes(raw_entry["expected"]),
                    bytes(raw_entry["replacement"]),
                    bool(raw_entry.get("actual_address", False)))
            else:
                values = tuple(raw_entry)
                if len(values) not in (3, 4):
                    raise MemoryAccessError("比较写入条目必须包含地址、原值和新值")
                entry = CompareWriteEntry(
                    int(values[0]), bytes(values[1]), bytes(values[2]),
                    bool(values[3]) if len(values) == 4 else False)
            actual = (entry.address if entry.actual_address
                      else self.resolve_reference(entry.address))
            current_range = (actual, actual + len(entry.expected))
            if any(current_range[0] < end and start < current_range[1]
                   for start, end in seen):
                raise MemoryAccessError("比较写入范围不能重叠")
            if not self.validate_actual_range(actual, len(entry.expected), write=True):
                raise MemoryAccessError(
                    "比较写入范围无效或不可写: 0x%08X" % actual)
            current = self.backend.read(actual, len(entry.expected))
            if current != entry.expected:
                raise MemoryAccessError(
                    "内存已在读取后变化，请刷新后重试: 0x%08X" % actual)
            prepared.append((actual, entry.expected, entry.replacement))
            seen.append(current_range)

        changed = []
        try:
            for actual, expected, replacement in prepared:
                # Claim the range before the backend call: a backend may write
                # a prefix and then report a short transfer.
                changed.append((actual, expected))
                transferred = self.backend.write(actual, replacement)
                if transferred != len(replacement):
                    raise MemoryAccessError(
                        "比较事务部分写入 0x%08X，实际 %d/%d 字节" %
                        (actual, transferred, len(replacement)))
                if self.backend.read(actual, len(replacement)) != replacement:
                    raise MemoryAccessError(
                        "比较事务写后复读不一致: 0x%08X" % actual)
        except Exception as exc:
            rollback_errors = []
            for actual, expected in reversed(changed):
                try:
                    if self.backend.write(actual, expected) != len(expected):
                        raise MemoryAccessError("部分回滚")
                    if self.backend.read(actual, len(expected)) != expected:
                        raise MemoryAccessError("回滚复读不一致")
                except Exception as rollback_exc:
                    rollback_errors.append("0x%08X: %s" %
                                           (actual, rollback_exc))
            if rollback_errors:
                raise MemoryAccessError(
                    "%s；事务回滚失败：%s" %
                    (exc, "；".join(rollback_errors))) from exc
            raise

    def set_write_guard(self, key: str, address: int, raw: bytes,
                        actual_address: bool = False) -> None:
        if self.direct_write:
            return
        if not key or not raw:
            raise MemoryAccessError("写入快照护栏缺少键或原始字节")
        actual = int(address) if actual_address else self.resolve_reference(int(address))
        if not self.validate_actual_range(actual, len(raw), write=False):
            raise MemoryAccessError("写入快照范围不可读: 0x%08X" % actual)
        if self.backend.read(actual, len(raw)) != raw:
            raise MemoryAccessError("登记写入快照时内存已经变化: 0x%08X" % actual)
        self._write_guards[str(key)] = (actual, bytearray(raw))

    def clear_write_guards(self, prefix: str = "") -> None:
        if not prefix:
            self._write_guards.clear()
            return
        for key in list(self._write_guards):
            if key.startswith(prefix):
                del self._write_guards[key]

    def verify_write_guards(self, prefix: str) -> None:
        if self.direct_write:
            return
        matched = [(key, value) for key, value in self._write_guards.items()
                   if key.startswith(prefix)]
        if not matched:
            raise MemoryAccessError("页面记录尚未读取，请先刷新")
        for _key, (actual, expected) in matched:
            current = self.backend.read(actual, len(expected))
            if current != bytes(expected):
                raise MemoryAccessError(
                    "内存已在读取后变化，请刷新后重试: 0x%08X" % actual)

    def _guard_for_range(self, actual: int, size: int):
        matches = []
        for key, (base, expected) in self._write_guards.items():
            if base <= actual and actual + size <= base + len(expected):
                matches.append((key, base, expected))
        if len(matches) > 1:
            raise MemoryAccessError("写入范围同时命中多个快照护栏")
        return matches[0] if matches else None

    @staticmethod
    def _check_value(value: int, width: int) -> None:
        if not isinstance(value, int) or not 0 <= value < (1 << width):
            raise ValueError("数值超出 UInt%d 范围" % width)

    def set_patch(self, spec: PatchSpec, enabled: bool) -> None:
        if self.direct_write:
            entries = tuple(
                CompareWriteEntry(
                    site.address,
                    site.original if enabled else site.patched,
                    site.patched if enabled else site.original)
                for site in spec.sites)
            self.write_code_transaction(
                entries, feature_key=spec.key, label=spec.label)
            if enabled:
                self._active_patches[spec.key] = spec
            else:
                self._active_patches.pop(spec.key, None)
            return
        if enabled:
            self._enable_patch(spec)
        else:
            self._disable_patch(spec)

    @staticmethod
    def _rel32(source_after: int, target: int) -> bytes:
        displacement = int(target) - int(source_after)
        if not -(1 << 31) <= displacement < (1 << 31):
            raise PatchError("rel32 跳转距离超出范围")
        return struct.pack("<i", displacement)

    def _build_dynamic_stub(self, spec: DynamicPatchSpec,
                            allocation: int) -> bytes:
        stub = bytearray(spec.stub_template)
        for relocation in spec.relocations:
            if relocation.offset < 0 or relocation.offset + 4 > len(stub):
                raise PatchError("%s 的 stub 重定位越界" % spec.label)
            if relocation.kind == "absolute_reference":
                value = self.resolve_reference(relocation.value) + relocation.addend
                encoded = struct.pack("<I", value & 0xFFFFFFFF)
            elif relocation.kind == "absolute_u32":
                value = relocation.value + relocation.addend
                encoded = struct.pack("<I", value & 0xFFFFFFFF)
            elif relocation.kind == "relative_reference":
                target = self.resolve_reference(relocation.value) + relocation.addend
                encoded = self._rel32(allocation + relocation.offset + 4, target)
            elif relocation.kind == "relative_stub":
                target = allocation + relocation.value + relocation.addend
                encoded = self._rel32(allocation + relocation.offset + 4, target)
            else:
                raise PatchError("%s 使用未知 stub 重定位类型 %s" %
                                 (spec.label, relocation.kind))
            stub[relocation.offset:relocation.offset + 4] = encoded
        return bytes(stub)

    def _dynamic_branch(self, site, allocation: int,
                        allocation_size: int) -> bytes:
        if not 0 <= site.stub_offset < allocation_size:
            raise PatchError("动态调用点的 stub 入口偏移越界")
        actual_site = self.resolve_reference(site.address)
        target = allocation + site.stub_offset
        branch = bytes((site.branch_opcode,)) + self._rel32(actual_site + 5, target)
        return branch + b"\x90" * (site.instruction_length - 5)

    @staticmethod
    def _dynamic_owned_write(site, branch: bytes) -> Tuple[int, bytes, bytes]:
        if site.operand_only:
            if site.instruction_length != 5 or site.branch_opcode not in (0xE8, 0xE9):
                raise PatchError("rel32 操作数补丁必须对应五字节 CALL/JMP")
            return site.address + 1, branch[1:5], site.original[1:5]
        return site.address, branch, site.original

    @contextmanager
    def _quiesced_thread_eips(self):
        if hasattr(self.backend, "quiesced_thread_eips"):
            with self.backend.quiesced_thread_eips() as eips:
                yield eips
            return
        if self.pid is None:
            raise PatchError("动态 stub 会话缺少进程 ID")
        with suspended_thread_eips(self.pid) as eips:
            yield eips

    def set_dynamic_patch(self, spec: DynamicPatchSpec, enabled: bool) -> None:
        self._ensure_writable("动态补丁")
        if self.direct_write:
            if enabled:
                self._enable_dynamic_patch_direct(spec)
            else:
                self._disable_dynamic_patch_direct(spec)
            return
        if enabled:
            self._enable_dynamic_patch(spec)
        else:
            self._disable_dynamic_patch(spec)

    def is_dynamic_patch_enabled(self, key: str) -> bool:
        """Return whether this session still owns every byte of a live stub."""
        lease = self._dynamic_leases.get(str(key))
        if self.direct_write:
            return lease is not None
        if lease is None:
            return False
        try:
            return all(self.read_bytes(site.address, len(branch)) == branch
                       for site, branch in zip(lease.spec.call_sites,
                                               lease.branch_bytes))
        except MemoryAccessError:
            return False

    def _enable_dynamic_patch_direct(self, spec: DynamicPatchSpec) -> None:
        if spec.key in self._dynamic_leases:
            return
        allocation = self.backend.allocate(
            len(spec.stub_template), PAGE_READWRITE)
        try:
            stub = self._build_dynamic_stub(spec, allocation)
            if self.backend.write(allocation, stub) != len(stub):
                raise PatchError("%s stub 只直写了部分字节" % spec.label)
            self.backend.protect(allocation, len(stub), PAGE_EXECUTE_READ)
            if hasattr(self.backend, "flush"):
                self.backend.flush(allocation, len(stub))
            branches = tuple(self._dynamic_branch(
                site, allocation, len(stub)) for site in spec.call_sites)
            for site, branch in zip(spec.call_sites, branches):
                address, replacement, _original = self._dynamic_owned_write(
                    site, branch)
                self.write_code_transaction((
                    CompareWriteEntry(address, replacement, replacement),
                ), feature_key=spec.key, label=spec.label)
            self._dynamic_leases[spec.key] = DynamicPatchLease(
                spec, allocation, len(stub), branches)
        except Exception:
            self._orphaned_allocations.append(allocation)
            raise

    def _disable_dynamic_patch_direct(self, spec: DynamicPatchSpec) -> None:
        lease = self._dynamic_leases.get(spec.key)
        branches = (lease.branch_bytes if lease is not None else
                    tuple(site.original for site in spec.call_sites))
        for site, branch in zip(spec.call_sites, branches):
            address, _owned, original = self._dynamic_owned_write(site, branch)
            self.write_code_transaction((
                CompareWriteEntry(address, original, original),
            ), feature_key=spec.key, label=spec.label)
        if lease is not None:
            self._dynamic_leases.pop(spec.key, None)
            self.backend.free(lease.allocation)

    def _enable_dynamic_patch(self, spec: DynamicPatchSpec) -> None:
        current_lease = self._dynamic_leases.get(spec.key)
        if current_lease is not None:
            for site, expected in zip(spec.call_sites, current_lease.branch_bytes):
                if self.read_bytes(site.address, len(expected)) != expected:
                    raise PatchError("%s已启用的调用点被外部修改" % spec.label)
            return
        for site in spec.call_sites:
            if self.read_bytes(site.address, len(site.original)) != site.original:
                raise PatchError("%s调用点原字节已变化" % spec.label)
        allocation = 0
        branches: Tuple[bytes, ...] = ()
        changed = []
        try:
            allocation = self.backend.allocate(len(spec.stub_template), PAGE_READWRITE)
            stub = self._build_dynamic_stub(spec, allocation)
            if self.backend.write(allocation, stub) != len(stub):
                raise PatchError("%s stub 只写入了部分字节" % spec.label)
            if self.backend.read(allocation, len(stub)) != stub:
                raise PatchError("%s stub 写后复读失败" % spec.label)
            self.backend.protect(allocation, len(stub), PAGE_EXECUTE_READ)
            if hasattr(self.backend, "flush"):
                self.backend.flush(allocation, len(stub))
            branches = tuple(self._dynamic_branch(
                site, allocation, len(stub)) for site in spec.call_sites)
            code_entries = []
            for site, branch in zip(spec.call_sites, branches):
                address, owned, original = self._dynamic_owned_write(site, branch)
                code_entries.append(CompareWriteEntry(address, original, owned))
            self.write_code_transaction(
                code_entries, feature_key=spec.key, label=spec.label)
            self._dynamic_leases[spec.key] = DynamicPatchLease(
                spec, allocation, len(stub), branches)
        except Exception:
            if allocation:
                if not self.code_patch_poisoned:
                    try:
                        self.backend.free(allocation)
                    except Exception:
                        self._orphaned_allocations.append(allocation)
                else:
                    self._orphaned_allocations.append(allocation)
            raise

    def _disable_dynamic_patch(self, spec: DynamicPatchSpec) -> None:
        lease = self._dynamic_leases.get(spec.key)
        if lease is None:
            if all(self.read_bytes(site.address, len(site.original)) == site.original
                   for site in spec.call_sites):
                return
            raise PatchError("%s并非由本工具启用，拒绝恢复" % spec.label)
        for site, expected in zip(spec.call_sites, lease.branch_bytes):
            if self.read_bytes(site.address, len(expected)) != expected:
                raise PatchError("%s调用点已被外部修改；保留 stub 到进程退出" %
                                 spec.label)
        with self._quiesced_thread_eips() as eips:
            if any(lease.allocation <= eip < lease.allocation + lease.allocation_size
                   for eip in eips):
                raise PatchError("%s仍有线程在 stub 内执行，拒绝释放" % spec.label)
            code_entries = []
            for site, branch in zip(spec.call_sites, lease.branch_bytes):
                address, owned, original = self._dynamic_owned_write(site, branch)
                code_entries.append(CompareWriteEntry(address, owned, original))
            self.write_code_transaction(
                code_entries, feature_key=spec.key, label=spec.label,
                allow_poisoned_restore=True)
            self.backend.free(lease.allocation)
            self._dynamic_leases.pop(spec.key, None)

    def _enable_patch(self, spec: PatchSpec) -> None:
        states = []
        for site in spec.sites:
            current = self.read_bytes(site.address, len(site.original))
            if current not in (site.original, site.patched):
                raise PatchError("%s在 0x%08X 的原字节已被其它修改占用" %
                                 (spec.label, site.address))
            states.append(current)
        if spec.key not in self._active_patches:
            patched_count = sum(current == site.patched
                                for site, current in zip(spec.sites, states))
            if 0 < patched_count < len(spec.sites):
                raise PatchError("%s已有部分站点被外部修改，拒绝接管" % spec.label)
        entries = [CompareWriteEntry(site.address, current, site.patched)
                   for site, current in zip(spec.sites, states)
                   if current != site.patched]
        self.write_code_transaction(
            entries, feature_key=spec.key, label=spec.label)
        if spec.key in self._active_patches or all(
                current == site.original for site, current in zip(spec.sites, states)):
            self._active_patches[spec.key] = spec

    def _disable_patch(self, spec: PatchSpec) -> None:
        if spec.key not in self._active_patches:
            if all(self.read_bytes(site.address, len(site.original)) == site.original
                   for site in spec.sites):
                return
            raise PatchError("%s并非由本工具启用，拒绝恢复" % spec.label)
        states = []
        for site in spec.sites:
            current = self.read_bytes(site.address, len(site.patched))
            if current != site.patched:
                raise PatchError("%s在 0x%08X 已被外部修改，拒绝误恢复" %
                                 (spec.label, site.address))
            states.append(current)
        self.write_code_transaction(
            [CompareWriteEntry(site.address, site.patched, site.original)
             for site in spec.sites],
            feature_key=spec.key, label=spec.label,
            allow_poisoned_restore=True)
        self._active_patches.pop(spec.key, None)

    @contextmanager
    def remote_backend(self, allow_poisoned: bool = False):
        """Acquire create-thread permission only for the duration of a call."""
        self._ensure_writable("远程调用")
        if self.remote_call_poisoned and not allow_poisoned:
            raise MemoryAccessError(
                "本会话存在未完成的远程线程，已熔断后续远程调用；请重新附加游戏")
        if isinstance(self.backend, FakeMemoryBackend):
            yield self.backend
            return
        if self.pid is None:
            raise MemoryAccessError("当前会话缺少进程 ID，不能创建远程线程")
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel32.OpenProcess.restype = wintypes.HANDLE
        access = (PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                  PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE |
                  SYNCHRONIZE)
        handle = kernel32.OpenProcess(access, False, int(self.pid))
        if not handle:
            raise MemoryAccessError("申请远程调用权限失败，WinError=%d" % ctypes.get_last_error())
        backend = Win32Backend(int(handle))
        try:
            yield backend
        finally:
            backend.close()

    def ReadProcessMemory(self, _handle, address, buffer, size, transferred) -> int:
        try:
            target = getattr(buffer, "_obj", None)
            if target is not None:
                ctypes.memset(ctypes.addressof(target), 0, ctypes.sizeof(target))
            data = self.read_bytes(int(address), int(size))
            ctypes.memmove(buffer, data, len(data))
            if transferred:
                ctypes.cast(transferred, ctypes.POINTER(ctypes.c_size_t))[0] = len(data)
            self.last_error = ""
            return 1
        except Exception as exc:
            self.last_error = "读取 0x%08X+0x%X 失败: %s" % (
                int(address), int(size), exc)
            if callable(self.error_handler):
                self.error_handler(self.last_error)
            return 0

    def WriteProcessMemory(self, _handle, address, buffer, size, transferred) -> int:
        try:
            data = ctypes.string_at(buffer, int(size))
            if self.direct_write:
                self.write_bytes(int(address), data, verify=False,
                                 allow_protect=True)
                if transferred:
                    ctypes.cast(
                        transferred,
                        ctypes.POINTER(ctypes.c_size_t))[0] = len(data)
                self.last_error = ""
                return 1
            actual = self.resolve_reference(int(address))
            in_module = self.module_base <= actual < self.module_base + self.module_size
            guard = self._guard_for_range(actual, len(data))
            if guard is None:
                page = self.query_page(actual, actual_address=True)
                if (in_module and
                        page.normalized_protect in EXECUTABLE_PROTECTIONS):
                    current = self.backend.read(actual, len(data))
                    self.write_code_transaction((
                        CompareWriteEntry(actual, current, data,
                                          actual_address=True),
                    ), feature_key="legacy", label="旧版代码补丁")
                else:
                    self.write_bytes(int(address), data, allow_protect=in_module)
            else:
                _key, base, expected = guard
                offset = actual - base
                old = bytes(expected[offset:offset + len(data)])
                self.compare_exchange_transaction((
                    CompareWriteEntry(actual, old, data, actual_address=True),
                ))
                expected[offset:offset + len(data)] = data
            if transferred:
                ctypes.cast(transferred, ctypes.POINTER(ctypes.c_size_t))[0] = len(data)
            self.last_error = ""
            return 1
        except Exception as exc:
            self.last_error = "写入 0x%08X+0x%X 失败: %s" % (
                int(address), int(size), exc)
            if callable(self.error_handler):
                self.error_handler(self.last_error)
            return 0

    def close(self, restore_patches: bool = True) -> None:
        if self.closed:
            return
        if self.read_only:
            restore_patches = False
        if restore_patches:
            for lease in list(self._dynamic_leases.values())[::-1]:
                try:
                    if self.direct_write:
                        self._disable_dynamic_patch_direct(lease.spec)
                    else:
                        self._disable_dynamic_patch(lease.spec)
                except Exception:
                    pass
            for spec in list(self._active_patches.values())[::-1]:
                try:
                    if self.direct_write:
                        self.set_patch(spec, False)
                    else:
                        self._disable_patch(spec)
                except Exception:
                    pass
        self.closed = True
        if self.owns_handle:
            self.backend.close()
        self.handle = 0

    def __enter__(self) -> "MemorySession":
        return self

    def __exit__(self, _exc_type, _exc, _traceback) -> None:
        self.close()


@dataclass
class FakeRegion:
    base: int
    data: bytearray
    protection: int = PAGE_READWRITE
    allocation_protect: int = PAGE_READWRITE
    memory_type: int = MEM_IMAGE


class FakeMemoryBackend:
    """Deterministic backend used by the standard-library unit tests."""

    def __init__(self, base: int, data: bytes,
                 protection: int = PAGE_READWRITE):
        self.region = FakeRegion(base, bytearray(data), protection, protection)
        self.fail_write_at: Optional[int] = None
        self.partial_write_at: Optional[int] = None
        self.fail_flush_at: Optional[int] = None
        self.fail_protect_at: Optional[int] = None
        self.fail_protect_value: Optional[int] = None
        self.closed = False
        self.allocations: Dict[int, int] = {}
        self.freed = []
        self.closed_threads = []
        self.waited_threads = []
        self._next_thread = 1
        self.flushed = []
        self.protect_calls = []
        self.active_eips = []
        self.page_size = 0x1000

    def _slice(self, address: int, size: int) -> slice:
        start = address - self.region.base
        if start < 0 or start + size > len(self.region.data):
            raise MemoryAccessError("假内存越界")
        return slice(start, start + size)

    def read(self, address: int, size: int) -> bytes:
        return bytes(self.region.data[self._slice(address, size)])

    def write(self, address: int, data: bytes) -> int:
        if self.fail_write_at == address:
            raise MemoryAccessError("模拟写入失败")
        count = len(data) - 1 if self.partial_write_at == address and len(data) > 1 else len(data)
        target = self._slice(address, count)
        self.region.data[target] = data[:count]
        return count

    def flush(self, address: int, size: int) -> None:
        if self.fail_flush_at == address:
            raise MemoryAccessError("模拟刷新指令缓存失败")
        self.flushed.append((address, size))

    def query_page(self, address: int) -> MemoryPageInfo:
        if not self.region.base <= address < self.region.base + len(self.region.data):
            raise MemoryAccessError("假内存页不存在")
        return MemoryPageInfo(
            self.region.base, self.region.base,
            self.region.allocation_protect, len(self.region.data), MEM_COMMIT,
            self.region.protection, self.region.memory_type)

    def query(self, address: int) -> Tuple[int, int, int, int]:
        info = self.query_page(address)
        return info.base_address, info.region_size, info.state, info.protect

    def protect(self, address: int, size: int, protection: int) -> int:
        self.protect_calls.append((address, size, protection))
        if ((self.fail_protect_at is None or self.fail_protect_at == address) and
                self.fail_protect_value == protection):
            raise MemoryAccessError("模拟修改页保护失败")
        old = self.region.protection
        self.region.protection = protection
        return old

    def allocate(self, size: int, protection: int = PAGE_READWRITE) -> int:
        address = self.region.base + len(self.region.data)
        self.region.data.extend(b"\0" * size)
        self.region.protection = protection
        self.allocations[address] = size
        return address

    @contextmanager
    def quiesced_thread_eips(self):
        yield tuple(self.active_eips)

    def free(self, address: int) -> None:
        if address not in self.allocations:
            raise MemoryAccessError("释放未知的假远程内存")
        self.freed.append(address)
        del self.allocations[address]

    def create_thread(self, _start: int, _parameter: int) -> int:
        thread = self._next_thread
        self._next_thread += 1
        return thread

    def wait_thread(self, thread: int, _timeout_ms: int) -> None:
        self.waited_threads.append(thread)

    def close_thread(self, thread: int) -> None:
        self.closed_threads.append(thread)

    def close(self) -> None:
        self.closed = True
