"""Startup completion reports without opening or modifying a game process."""

import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

import main


class StartupSmokeTests(unittest.TestCase):
    def window(self, root, visible=True):
        window = Mock(resource_dir=Path(root))
        window.isVisible.return_value = visible
        for number in range(1, 9):
            getattr(window, "widget_%d" % number).objectName.return_value = "widget_%d" % number
        return window

    def application(self):
        app = Mock()
        app.property.return_value = {"supported": True}
        app.platformName.return_value = "offscreen"
        return app

    def resources(self, directory):
        for name in ("logo.ico", "准星.cur", "准星.png"):
            (Path(directory) / name).write_bytes(b"test resource presence")

    def test_cli_requires_output_before_smoke_exit_and_rejects_attach(self):
        for arguments in (("--startup-smoke-exit",),
                          ("--startup-smoke-output", "smoke.json", "--attach-pid", "123")):
            with self.subTest(arguments=arguments), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as caught:
                    main.parse_arguments(list(arguments))
                self.assertEqual(2, caught.exception.code)

    def test_cli_smoke_has_no_implicit_game_target(self):
        options = main.parse_arguments([
            "--startup-smoke-output", "smoke.json", "--startup-smoke-exit"])
        self.assertIsNone(options.attach_pid)
        self.assertIsNone(options.diagnostic_output)
        self.assertEqual(Path("smoke.json"), options.startup_smoke_output)
        self.assertTrue(options.startup_smoke_exit)

    def test_completed_smoke_writes_eight_pages_and_exits_successfully(self):
        with tempfile.TemporaryDirectory() as directory:
            self.resources(directory)
            app = self.application()
            window = self.window(directory)
            output = Path(directory) / "完成.json"
            main.startup_smoke(app, window, output, True)
            report = json.loads(output.read_text(encoding="utf-8"))
        self.assertTrue(report["success"])
        self.assertTrue(report["windowCreated"])
        self.assertEqual(8, len(report["pages"]))
        self.assertFalse(report["gameMemoryWrites"])
        self.assertFalse(report["remoteCalls"])
        self.assertEqual("offscreen", report["qtPlatform"])
        window.attach_pid.assert_not_called()
        window.close.assert_called_once_with()
        app.exit.assert_called_once_with(0)

    def test_missing_resource_fails_instead_of_counting_process_liveness(self):
        with tempfile.TemporaryDirectory() as directory:
            self.resources(directory)
            (Path(directory) / "准星.cur").unlink()
            app = self.application()
            window = self.window(directory)
            output = Path(directory) / "smoke.json"
            main.startup_smoke(app, window, output, False)
            report = json.loads(output.read_text(encoding="utf-8"))
        self.assertFalse(report["success"])
        self.assertFalse(report["resources"]["准星.cur"])
        app.exit.assert_called_once_with(5)
        window.close.assert_called_once_with()

    def test_hidden_window_does_not_pass_startup_smoke(self):
        with tempfile.TemporaryDirectory() as directory:
            self.resources(directory)
            app = self.application()
            window = self.window(directory, visible=False)
            output = Path(directory) / "smoke.json"
            main.startup_smoke(app, window, output, True)
            report = json.loads(output.read_text(encoding="utf-8"))
        self.assertFalse(report["success"])
        app.exit.assert_called_once_with(5)


if __name__ == "__main__":
    unittest.main()
