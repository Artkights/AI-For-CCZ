"""Windows API simulations: Win7 diagnostics and live x86 attachment limits."""

import ctypes
import unittest
from types import SimpleNamespace
from unittest.mock import Mock, patch

import process_memory as memory


def function(result=1, side_effect=None):
    return Mock(return_value=result, side_effect=side_effect)


def kernel_for_architecture(target_wow64=True, current_wow64=True, alive=True):
    def wow64(handle, output):
        raw_handle = handle.value if hasattr(handle, "value") else handle
        output._obj.value = current_wow64 if raw_handle == 99 else target_wow64
        return 1

    def exit_code(_handle, output):
        output._obj.value = 259 if alive else 0
        return 1

    return SimpleNamespace(
        IsWow64Process=function(side_effect=wow64),
        GetCurrentProcess=function(99),
        GetExitCodeProcess=function(side_effect=exit_code),
        CloseHandle=function(),
    )


class OptionalMitigationApiTests(unittest.TestCase):
    def security(self, api=None, error=5):
        advapi32 = SimpleNamespace(**{
            name: function(0) for name in (
                "OpenProcessToken", "GetTokenInformation",
                "GetSidSubAuthorityCount", "GetSidSubAuthority")})
        kernel32 = SimpleNamespace(CloseHandle=function())
        if api is not None:
            kernel32.GetProcessMitigationPolicy = api
        with patch.object(memory.ctypes, "WinDLL", side_effect=[advapi32, kernel32]), \
                patch.object(memory.ctypes, "get_last_error", return_value=error):
            return memory.query_process_security(123)

    def test_missing_win8_api_reports_unknown_without_inventing_policy(self):
        report = self.security()
        self.assertFalse(report["dynamicCodePolicySupported"])
        self.assertEqual("unknown", report["dynamicCodePolicy"])
        self.assertNotIn("prohibitDynamicCode", report)
        self.assertNotIn("dynamicCodeFlags", report)

    def test_win7_api_stub_errors_remain_unknown_and_nonfatal(self):
        for error in (87, 120, 127):
            with self.subTest(error=error):
                report = self.security(function(0), error)
                self.assertEqual(error, report["dynamicCodePolicyError"])
                self.assertFalse(report["dynamicCodePolicySupported"])
                self.assertEqual("unknown", report["dynamicCodePolicy"])
                self.assertNotIn("prohibitDynamicCode", report)

    def test_access_denied_is_a_query_error_not_unsupported_api(self):
        report = self.security(function(0), 5)
        self.assertEqual(5, report["dynamicCodePolicyError"])
        self.assertNotIn("dynamicCodePolicySupported", report)
        self.assertNotIn("prohibitDynamicCode", report)

    def test_supported_api_returns_real_policy_flags(self):
        def policy(_handle, _kind, output, _size):
            output._obj.Flags = 1
            return 1
        report = self.security(function(side_effect=policy))
        self.assertTrue(report["dynamicCodePolicySupported"])
        self.assertTrue(report["prohibitDynamicCode"])
        self.assertEqual(1, report["dynamicCodeFlags"])


class LiveArchitectureTests(unittest.TestCase):
    def test_win7_x86_system_uses_iswow64process_fallback(self):
        kernel = kernel_for_architecture(False, False)
        with patch.object(memory, "_host_bits", return_value=32):
            self.assertEqual("x86", memory.validate_live_process(123, False, kernel))

    def test_win7_x64_system_accepts_x86_game_from_x86_tool(self):
        kernel = kernel_for_architecture(True, True)
        with patch.object(memory, "_host_bits", return_value=32):
            self.assertEqual("x86", memory.validate_live_process(123, False, kernel))

    def test_x86_tool_rejects_native_x64_target(self):
        kernel = kernel_for_architecture(False, True)
        with patch.object(memory, "_host_bits", return_value=32):
            with self.assertRaisesRegex(memory.MemoryAccessError, "目标架构 x64"):
                memory.validate_live_process(123, False, kernel)

    def test_x64_host_can_read_x86_target_but_cannot_write(self):
        kernel = kernel_for_architecture(True, False)
        with patch.object(memory, "_host_bits", return_value=64):
            self.assertEqual("x86", memory.validate_live_process(123, True, kernel))
            kernel.GetExitCodeProcess.reset_mock()
            with self.assertRaisesRegex(memory.MemoryAccessError, "只读诊断"):
                memory.validate_live_process(123, False, kernel)
            kernel.GetExitCodeProcess.assert_not_called()

    def test_exited_process_is_rejected_before_architecture_queries(self):
        kernel = kernel_for_architecture(alive=False)
        with patch.object(memory, "_host_bits", return_value=32):
            with self.assertRaisesRegex(memory.MemoryAccessError, "已退出"):
                memory.validate_live_process(123, False, kernel)
        kernel.IsWow64Process.assert_not_called()

    def test_iswow64process2_rejects_arm64_and_keeps_x86_emulation(self):
        for machine, native, expected in ((0, 0xAA64, "arm64"),
                                           (0x14C, 0xAA64, "x86")):
            kernel = kernel_for_architecture()
            def query(_handle, process, system):
                process._obj.value = machine
                system._obj.value = native
                return 1
            kernel.IsWow64Process2 = function(side_effect=query)
            self.assertEqual(expected, memory.query_process_architecture(123, kernel))
            kernel.IsWow64Process.assert_not_called()

    def test_fake_memory_writes_keep_working_on_x64_host(self):
        backend = memory.FakeMemoryBackend(0x400000, b"\0" * 16)
        with patch.object(memory, "_host_bits", return_value=64), \
                patch.object(memory, "validate_live_process") as validate:
            session = memory.MemorySession(1, 0x400000, 16,
                                            backend=backend, owns_handle=False)
            session.write_u8(0x400000, 77)
        validate.assert_not_called()
        self.assertEqual(77, backend.read(0x400000, 1)[0])


class AttachmentErrorTests(unittest.TestCase):
    def test_access_denied_keeps_code_and_explains_permissions(self):
        error = str(memory.windows_api_error("打开游戏进程失败", 5, True))
        self.assertIn("WinError=5", error)
        self.assertIn("相同权限", error)
        self.assertIn("管理员", error)

    def test_missing_process_and_architecture_failures_are_distinguished(self):
        self.assertIn("PID 不存在", str(memory.windows_api_error("打开失败", 87, True)))
        self.assertIn("目标架构", str(memory.windows_api_error("模块失败", 299, True)))
        self.assertNotIn("架构", str(memory.windows_api_error("内存读取失败", 299)))

    def test_invalid_pid_never_opens_a_process(self):
        with patch.object(memory.ctypes, "WinDLL") as dll:
            for pid in (None, "bad", 0, -1, 0x100000000):
                with self.subTest(pid=pid), self.assertRaisesRegex(
                        memory.MemoryAccessError, "PID"):
                    memory.MemorySession.open_pid(pid, 0x400000, 16)
        dll.assert_not_called()

    def test_x64_writable_open_rejected_without_requesting_handle(self):
        with patch.object(memory, "_host_bits", return_value=64), \
                patch.object(memory.ctypes, "WinDLL") as dll:
            with self.assertRaisesRegex(memory.MemoryAccessError, "只读诊断"):
                memory.MemorySession.open_pid(123, 0x400000, 16)
        dll.assert_not_called()

    def test_open_process_access_denied_gives_actionable_error(self):
        kernel = SimpleNamespace(OpenProcess=function(0))
        with patch.object(memory, "_host_bits", return_value=32), \
                patch.object(memory.ctypes, "WinDLL", return_value=kernel), \
                patch.object(memory.ctypes, "get_last_error", return_value=5):
            with self.assertRaisesRegex(memory.MemoryAccessError, "相同权限"):
                memory.MemorySession.open_pid(123, 0x400000, 16)

    def test_failed_live_validation_closes_new_handle(self):
        kernel = SimpleNamespace(OpenProcess=function(123), CloseHandle=function())
        with patch.object(memory, "_host_bits", return_value=32), \
                patch.object(memory.ctypes, "WinDLL", return_value=kernel), \
                patch.object(memory.MemorySession, "__init__",
                             side_effect=memory.MemoryAccessError("目标架构 x64")):
            with self.assertRaisesRegex(memory.MemoryAccessError, "目标架构"):
                memory.MemorySession.open_pid(123, 0x400000, 16)
        kernel.CloseHandle.assert_called_once_with(123)

    def test_backend_read_access_denied_keeps_address_and_permission_hint(self):
        backend = object.__new__(memory.Win32Backend)
        backend.handle = 123
        backend.kernel32 = SimpleNamespace(ReadProcessMemory=function(0))
        with patch.object(memory.ctypes, "get_last_error", return_value=5):
            with self.assertRaisesRegex(memory.MemoryAccessError,
                                        "0x00401000.*WinError=5.*相同权限"):
                backend.read(0x401000, 4)


if __name__ == "__main__":
    unittest.main()
