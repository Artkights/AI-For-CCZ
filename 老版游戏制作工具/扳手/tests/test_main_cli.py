"""Explicit-target and read-only release diagnostics without a real process."""

import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import Mock, patch

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from PyQt5.QtWidgets import QApplication

import main
from engine_profile import legacy_profile
from process_memory import FakeMemoryBackend


class MainCliTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])

    def test_no_arguments_has_no_implicit_target_or_diagnostics(self):
        options = main.parse_arguments([])
        self.assertIsNone(options.attach_pid)
        self.assertIsNone(options.diagnostic_output)
        self.assertFalse(options.diagnostic_exit)

    def test_qt_creation_failure_writes_report_and_returns_startup_error(self):
        report = Mock(supported=True, errors=[])
        report.as_dict.return_value = {"supported": True}
        report.message.side_effect = lambda: "；".join(report.errors)
        with tempfile.TemporaryDirectory() as directory:
            log_dir = Path(directory)
            with patch.object(main, "configure_startup_logging", return_value=log_dir), \
                    patch.object(main, "collect_compatibility_report", return_value=report), \
                    patch.object(main, "write_report") as write_report, \
                    patch.object(main, "show_startup_error") as show_error, \
                    patch.object(main, "QApplication", side_effect=RuntimeError("qwindows missing")), \
                    patch.object(main.logging.getLogger("banshou"), "exception") as logged:
                self.assertEqual(4, main.main([]))
            self.assertEqual(["Qt界面初始化失败：qwindows missing"], report.errors)
            self.assertFalse(report.supported)
            self.assertGreaterEqual(write_report.call_count, 2)
            show_error.assert_called_once()
            self.assertIn("Qt界面初始化失败", show_error.call_args.args[0])
            logged.assert_called_once()

    def test_setup_ui_failure_is_reported_before_process_exit(self):
        report = Mock(supported=True, errors=[])
        report.as_dict.return_value = {"supported": True}
        report.message.side_effect = lambda: "；".join(report.errors)
        app = Mock()
        app.property.return_value = None
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(main, "configure_startup_logging", return_value=Path(directory)), \
                    patch.object(main, "collect_compatibility_report", return_value=report), \
                    patch.object(main, "write_report"), \
                    patch.object(main, "show_startup_error") as show_error, \
                    patch.object(main, "QApplication", return_value=app), \
                    patch.object(main, "MyDesiger", side_effect=ValueError("bad setupUi")), \
                    patch.object(main.logging.getLogger("banshou"), "exception"):
                self.assertEqual(4, main.main([]))
            self.assertIn("bad setupUi", report.errors[0])
            show_error.assert_called_once()
            app.quit.assert_called_once_with()

    def test_explicit_pid_is_preserved(self):
        self.assertEqual(28208, main.parse_arguments(["--attach-pid", "28208"]).attach_pid)

    def test_invalid_or_implicit_targets_are_rejected(self):
        for arguments in (
                ["--attach-pid", "0"], ["--attach-pid", "-1"],
                ["--attach-pid", "4294967296"], ["--attach-pid", "not-a-pid"],
                ["--diagnostic-output", "result.json"], ["--diagnostic-exit"],
                ["--attach-pid", "28208", "--diagnostic-exit"]):
            with self.subTest(arguments=arguments), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as caught:
                    main.parse_arguments(arguments)
                self.assertEqual(2, caught.exception.code)

    def controller(self, directory):
        options = main.parse_arguments([
            "--attach-pid", "28208", "--diagnostic-output",
            str(Path(directory) / "result.json"), "--diagnostic-exit"])
        window = Mock()
        window._page_readiness = {}
        window.engine_profile = None
        window._catalog_signature = None
        window.label_window2.toolTip.return_value = "status"
        controller = main.DiagnosticRun(Mock(), window, options)
        return controller, window, options

    def prepare_successful_pages(self, controller, window):
        window.engine_profile = legacy_profile(0)
        window._catalog_signature = ("ready",)
        window._page_readiness = dict.fromkeys(controller.REQUIRED_FEATURES, True)
        window.memory_session = Mock(read_only=True, security_context={})
        for name in ("listview_data", "listview_war", "listview_item", "listview_item_2",
                     "listview_item_3", "listview_mk"):
            getattr(window, name).count.return_value = 1
        window.listview_diy.rowCount.return_value = 0
        for number in (1, 2, 3):
            getattr(window, "listview_var_%d" % number).rowCount.return_value = 1
        for page, _action, reader in controller.PAGE_READERS:
            attributes = controller.PAGE_SNAPSHOTS.get(page, ())
            if attributes:
                def populate(*_args, names=attributes):
                    for name in names:
                        setattr(window, name, object())
                getattr(window, reader).side_effect = populate

    def test_diagnostic_reopens_only_the_explicit_pid_with_read_permissions(self):
        with tempfile.TemporaryDirectory() as directory:
            controller, window, _ = self.controller(directory)
            original = Mock(module_base=0x400000, module_size=0x200000,
                            reference_image_base=0x400000)
            window.memory_session = original
            readonly = Mock(read_only=True, handle=123, session_generation="readonly")
            with patch.object(main.MemorySession, "open_pid", return_value=readonly) as opened:
                controller._read_only_session()
            opened.assert_called_once_with(28208, 0x400000, 0x200000, 0x400000, read_only=True)
            original.close.assert_called_once_with()
            self.assertIs(readonly, window.memory_session)
            self.assertIs(readonly, window.md)
            self.assertTrue(controller.report["readOnlyEnforced"])

    def test_diagnostic_reuses_readonly_session_without_invalidating_scan_generation(self):
        with tempfile.TemporaryDirectory() as directory:
            controller, window, _ = self.controller(directory)
            readonly = main.MemorySession(
                123, 0x400000, 0x1000,
                backend=FakeMemoryBackend(0x400000, bytes(0x1000)),
                owns_handle=False, read_only=True)
            generation = readonly.session_generation
            window.memory_session = readonly
            window.md = readonly
            window.p = readonly.handle
            try:
                with patch.object(main.MemorySession, "open_pid") as opened, \
                        patch.object(readonly, "close", wraps=readonly.close) as close:
                    controller._read_only_session()
                    opened.assert_not_called()
                    close.assert_not_called()
                self.assertIs(readonly, window.memory_session)
                self.assertIs(readonly, window.md)
                self.assertEqual(123, window.p)
                self.assertEqual(generation, readonly.session_generation)
                self.assertEqual(generation, controller.report["sessionGeneration"])
                self.assertTrue(controller.report["readOnlyEnforced"])
                self.assertFalse(readonly.closed)
            finally:
                readonly.close()

    def test_diagnostic_marks_first_attachment_read_only_before_opening_handle(self):
        with tempfile.TemporaryDirectory() as directory:
            controller, window, _ = self.controller(directory)
            window._attach_context = (28208, 0x400000, 0x200000, "game.exe")
            modes = []
            def attach(_pid):
                modes.append(window._diagnostic_read_only)
                return True
            window.attach_pid.side_effect = attach
            with patch.object(controller, "_read_only_session"), \
                    patch.object(controller, "poll"):
                controller.start()
            controller.timer.stop()
            self.assertEqual([True], modes)
            window.attach_pid.assert_called_once_with(28208)

    def test_real_attachment_for_diagnostic_never_requests_write_access(self):
        window = main.MyDesiger()
        window._diagnostic_read_only = True
        target = Mock(pid=28208)
        target.exe.return_value = "game.exe"
        session = Mock(handle=123, read_only=True)
        try:
            with patch("mywindow.psutil.Process", return_value=target), \
                    patch("mywindow.get_process_module",
                          return_value=(0x400000, 0x200000, "game.exe")), \
                    patch("mywindow.os.path.samefile", return_value=True), \
                    patch("mywindow.PEImage", return_value=Mock(image_base=0x400000)), \
                    patch("mywindow.MemorySession.open_pid", return_value=session) as opened, \
                    patch.object(window, "getFileVersion", return_value=6), \
                    patch.object(window, "_queue_profile_scan"):
                self.assertTrue(window.attach_pid(28208))
            opened.assert_called_once_with(28208, 0x400000, 0x200000,
                                           0x400000, read_only=True)
        finally:
            window.close()

    def test_background_profile_scan_uses_a_read_only_handle(self):
        import mywindow
        task = mywindow._ProfileScanTask(1, 28208, 0x400000, 0x200000,
                                         "game.exe", 6, None)
        session = Mock()
        profile = Mock(metadata={})
        with patch.object(mywindow, "PEImage", return_value=Mock(image_base=0x400000)), \
                patch.object(mywindow.MemorySession, "open_pid", return_value=session) as opened, \
                patch.object(mywindow, "RuntimeModuleImage"), \
                patch.object(mywindow, "detect_composite_profile", return_value=profile), \
                patch.object(mywindow, "verify_engine_identity", return_value={}), \
                patch.object(mywindow, "hydrate_runtime_item_catalog"):
            task.run()
        opened.assert_called_once_with(28208, 0x400000, 0x200000,
                                       0x400000, read_only=True)
        session.close.assert_called_once_with()
        session.write_bytes.assert_not_called()
        session.remote_backend.assert_not_called()

    def test_diagnostic_timeout_writes_failure_and_exits_nonzero(self):
        with tempfile.TemporaryDirectory() as directory:
            controller, window, options = self.controller(directory)
            controller.deadline = time.monotonic() - 1
            controller.poll()
            report = json.loads(options.diagnostic_output.read_text(encoding="utf-8"))
            self.assertFalse(report["success"])
            self.assertFalse(report["gameMemoryWrites"])
            self.assertFalse(report["remoteCalls"])
            self.assertEqual(28208, report["pid"])
            controller.app.exit.assert_called_once_with(2)
            window.close.assert_called_once_with()

    def test_diagnostic_waits_for_all_pages_and_the_completed_catalog(self):
        with tempfile.TemporaryDirectory() as directory:
            controller, window, _ = self.controller(directory)
            controller.deadline = time.monotonic() + 30
            window.engine_profile = legacy_profile(0)
            window._page_readiness = dict.fromkeys(controller.REQUIRED_FEATURES, True)
            with patch.object(controller, "capture_pages") as capture:
                controller.poll()
            capture.assert_not_called()
            self.assertFalse(controller.done)

    def test_diagnostic_refuses_a_writable_session(self):
        with tempfile.TemporaryDirectory() as directory:
            controller, window, _ = self.controller(directory)
            window.memory_session = Mock(read_only=False)
            with self.assertRaisesRegex(RuntimeError, "未强制只读"):
                controller.capture_pages()

    def test_successful_diagnostic_reads_exactly_eight_pages_without_save_or_helpers(self):
        with tempfile.TemporaryDirectory() as directory:
            controller, window, options = self.controller(directory)
            controller.deadline = time.monotonic() + 30
            self.prepare_successful_pages(controller, window)
            controller.poll()
            report = json.loads(options.diagnostic_output.read_text(encoding="utf-8"))
            self.assertTrue(report["success"])
            self.assertEqual(8, len(report["pages"]))
            self.assertTrue(all(page["success"] for page in report["pages"]))
            self.assertIsNone(window._attach_context)
            for _page, action, reader in controller.PAGE_READERS:
                getattr(window, action).assert_called_once_with()
                getattr(window, reader).assert_called_once()
            for forbidden in ("saveData", "saveWar", "saveItem", "savePower", "saveVar",
                              "saveDIY", "saveMk", "recal", "onTurn", "onLife", "onButtonClick"):
                getattr(window, forbidden).assert_not_called()
            controller.app.exit.assert_called_once_with(0)

    def test_page_exception_causes_nonzero_report_after_all_eight_reads(self):
        with tempfile.TemporaryDirectory() as directory:
            controller, window, options = self.controller(directory)
            controller.deadline = time.monotonic() + 30
            self.prepare_successful_pages(controller, window)
            window.onPower.side_effect = RuntimeError("failed effect read")
            controller.poll()
            report = json.loads(options.diagnostic_output.read_text(encoding="utf-8"))
            self.assertEqual(8, len(report["pages"]))
            self.assertFalse(report["success"])
            self.assertIn("failed effect read", report["pages"][4]["error"])
            controller.app.exit.assert_called_once_with(2)

    def test_swallowed_reader_error_cannot_reuse_an_old_snapshot(self):
        with tempfile.TemporaryDirectory() as directory:
            controller, window, options = self.controller(directory)
            controller.deadline = time.monotonic() + 30
            self.prepare_successful_pages(controller, window)
            window._effect_snapshot = object()
            window.onPower.side_effect = None
            controller.poll()
            report = json.loads(options.diagnostic_output.read_text(encoding="utf-8"))
            self.assertFalse(report["success"])
            self.assertIn("未产生新的读取快照", report["pages"][4]["error"])
            controller.app.exit.assert_called_once_with(2)


if __name__ == "__main__":
    unittest.main()
