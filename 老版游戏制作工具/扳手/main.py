import argparse
import json
from pathlib import Path
import sys
import time
from datetime import datetime
import logging

from compatibility import (collect_compatibility_report, configure_startup_logging,
                           format_compatibility_errors, show_startup_error,
                           user_data_dir, write_report)

_startup_report = None
_startup_log_dir = None
if __name__ == "__main__":
    _startup_log_dir = configure_startup_logging(Path(__file__).resolve().parent)
    _startup_report = collect_compatibility_report(check_native=True)
    write_report(_startup_log_dir / "compatibility-report.json", _startup_report.as_dict())
    if not _startup_report.supported:
        show_startup_error(format_compatibility_errors(_startup_report) +
                           "\n诊断报告：" + str(_startup_log_dir))
        sys.exit(4)

try:
    from PyQt5 import QtCore
    from PyQt5.QtWidgets import QMainWindow, QApplication
    from mywindow import Ui_MainWindow
    from process_memory import MemorySession
except Exception as _import_error:
    if __name__ != "__main__":
        raise
    logging.getLogger("banshou").exception("加载主界面失败")
    if _startup_report is not None:
        _startup_report.errors.append("加载主界面失败：%s" % _import_error)
        _startup_report.supported = False
        try:
            write_report(_startup_log_dir / "compatibility-report.json",
                         _startup_report.as_dict())
        except OSError:
            logging.getLogger("banshou").exception(
                "保存兼容性报告失败")
        show_startup_error(format_compatibility_errors(_startup_report))
    else:
        show_startup_error("扳手启动失败：无法加载主界面：%s" % _import_error)
    sys.exit(4)


class MyDesiger(QMainWindow, Ui_MainWindow):
    def __init__(self, parent=None):
        super(MyDesiger, self).__init__(parent)
        self.setupUi(self)

    def mousePressEvent(self, event):
        return Ui_MainWindow.mousePressEvent(self, event)

    def mouseMoveEvent(self, event):
        return Ui_MainWindow.mouseMoveEvent(self, event)

    def mouseReleaseEvent(self, event):
        return Ui_MainWindow.mouseReleaseEvent(self, event)

    def changeEvent(self, event):
        QMainWindow.changeEvent(self, event)
        if hasattr(self, "_closing"):
            Ui_MainWindow.changeEvent(self, event)

    def closeEvent(self, event):
        if hasattr(self, "_scan_pool"):
            return Ui_MainWindow.closeEvent(self, event)
        return QMainWindow.closeEvent(self, event)

def parse_arguments(argv=None):
    parser = argparse.ArgumentParser(description="曹操传扳手")

    def positive_pid(value):
        try:
            pid = int(value, 10)
        except ValueError as exc:
            raise argparse.ArgumentTypeError("PID必须为十进制整数") from exc
        if not 0 < pid <= 0xFFFFFFFF:
            raise argparse.ArgumentTypeError("PID必须在1..4294967295范围内")
        return pid

    parser.add_argument("--attach-pid", type=positive_pid,
                        help="附加到明确指定的本地进程PID")
    parser.add_argument("--diagnostic-output", type=Path,
                        help="只读附加并把八页诊断结果保存为JSON")
    parser.add_argument("--diagnostic-exit", action="store_true",
                        help="诊断报告保存后关闭程序")
    parser.add_argument("--startup-smoke-output", type=Path,
                        help="仅验证Qt窗口和资源初始化并保存JSON；不附加游戏")
    parser.add_argument("--startup-smoke-exit", action="store_true",
                        help="启动自检报告保存后正常退出")
    options = parser.parse_args(argv)
    if options.diagnostic_output is not None and options.attach_pid is None:
        parser.error("--diagnostic-output需要显式指定--attach-pid")
    if options.diagnostic_exit and options.diagnostic_output is None:
        parser.error("--diagnostic-exit需要指定--diagnostic-output")
    if options.startup_smoke_exit and options.startup_smoke_output is None:
        parser.error("--startup-smoke-exit需要指定--startup-smoke-output")
    if options.startup_smoke_output is not None and options.attach_pid is not None:
        parser.error("启动自检不能同时附加游戏进程")
    return options


class DiagnosticRun(QtCore.QObject):
    """Read the explicitly selected process and eight pages without game writes."""

    REQUIRED_FEATURES = (
        "person", "battle", "warehouse", "item_consumables", "effect",
        "effect_exclusive", "fatal", "variables_bool", "variables_int",
        "variables_ptr", "diy")
    PAGE_READERS = (
        ("control", "on_action_C_triggered", "_sync_66_patch_controls"),
        ("person", "on_action_M_triggered", "onData"),
        ("battle", "on_action_B_triggered", "onWar"),
        ("warehouse", "on_action_W_triggered", "onItem"),
        ("effect", "on_action_T_triggered", "onPower"),
        ("variables", "on_action_V_triggered", "onVar"),
        ("diy", "on_action_D_triggered", "DIY_Page"),
        ("fatal", "on_action_K_triggered", "onMk"))
    PAGE_SNAPSHOTS = {
        "person": ("_person_snapshot",),
        "battle": ("_battle_edit_snapshot",),
        "warehouse": ("_warehouse_full_snapshot", "_item_global_snapshot"),
        "effect": ("_effect_snapshot", "_exclusive_snapshot"),
        "fatal": ("_fatal_snapshot",),
    }

    def __init__(self, app, window, options, timeout_seconds=30.0):
        super().__init__()
        self.app = app
        self.window = window
        self.options = options
        self.timeout_seconds = timeout_seconds
        self.deadline = None
        self.done = False
        self.report = {
            "schema": "banshou-read-only-diagnostic-v1",
            "capturedAt": datetime.now().astimezone().isoformat(),
            "pid": options.attach_pid,
            "operation": "read-only-attach-eight-pages",
            "gameMemoryWrites": False,
            "remoteCalls": False,
            "pages": [],
            "success": False,
        }
        self.timer = QtCore.QTimer(self)
        self.timer.setInterval(100)
        self.timer.timeout.connect(self.poll)

    def _read_only_session(self):
        # Diagnostic attach already opens read-only.  Preserve that session's
        # generation so its in-flight worker result is still current.
        old = self.window.memory_session
        if getattr(old, "read_only", False) is True:
            self.report["readOnlyEnforced"] = True
            self.report["sessionGeneration"] = old.session_generation
            return
        readonly = MemorySession.open_pid(
            self.options.attach_pid, old.module_base, old.module_size,
            old.reference_image_base, read_only=True)
        readonly.error_handler = self.window._set_status
        self.window.memory_session = readonly
        self.window.md = readonly
        self.window.p = readonly.handle
        old.close()
        self.report["readOnlyEnforced"] = readonly.read_only
        self.report["sessionGeneration"] = readonly.session_generation

    @QtCore.pyqtSlot()
    def start(self):
        self.deadline = time.monotonic() + self.timeout_seconds
        self.window.setEnabled(False)
        try:
            self.window._diagnostic_read_only = True
            if not self.window.attach_pid(self.options.attach_pid):
                raise RuntimeError(self.window.label_window2.toolTip() or "附加失败")
            self._read_only_session()
            self.report["executable"] = self.window._attach_context[3]
            self.window.lock_timer.stop()
            self.timer.start()
            self.poll()
        except Exception as exc:
            self.finish(False, str(exc))

    @QtCore.pyqtSlot()
    def poll(self):
        if self.done:
            return
        self.window.lock_timer.stop()
        readiness = dict(self.window._page_readiness)
        self.report["readiness"] = readiness
        if (self.window.engine_profile is not None and
                self.window._catalog_signature is not None and
                all(readiness.get(key, False) for key in self.REQUIRED_FEATURES)):
            try:
                self.capture_pages()
                self.finish(all(page["success"] for page in self.report["pages"]))
            except Exception as exc:
                self.finish(False, str(exc))
        elif time.monotonic() >= self.deadline:
            pending = [key for key in self.REQUIRED_FEATURES if not readiness.get(key)]
            self.finish(False, "30秒内未取得完整运行时画像；未就绪: " + ", ".join(pending))

    def capture_pages(self):
        window = self.window
        session = window.memory_session
        if session is None or not session.read_only:
            raise RuntimeError("诊断会话未强制只读，停止验收")
        window.attach_timer.stop()
        # Page actions normally request another scan. This one-shot report
        # uses the completed profile and never restarts detection while reading.
        window._attach_context = None
        profile = window.engine_profile
        self.report["profile"] = {
            "version": int(profile.version),
            "displayVersion": profile.display_version,
            "variant": profile.variant_name,
            "summary": profile.summary(),
            "limits": dict(profile.limits),
            "addresses": dict(profile.addresses),
            "tables": {},
        }
        for key in self.REQUIRED_FEATURES:
            layout = profile.resolved_feature(key).layout
            if layout is not None and hasattr(layout, "stride"):
                self.report["profile"]["tables"][key] = {
                    "reference": layout.reference,
                    "resolver": layout.resolver.value,
                    "count": layout.record_count,
                    "stride": layout.stride,
                    "codec": layout.codec,
                }
        self.report["securityContext"] = dict(session.security_context)
        for page, action, reader in self.PAGE_READERS:
            result = {"page": page, "success": False}
            try:
                getattr(window, action)()
                for name in self.PAGE_SNAPSHOTS.get(page, ()):
                    setattr(window, name, None)
                if page == "variables":
                    for number in (1, 2, 3):
                        getattr(window, "listview_var_%d" % number).setRowCount(0)
                if reader in ("onWar", "DIY_Page"):
                    getattr(window, reader)(0 if reader == "onWar" else 1)
                else:
                    getattr(window, reader)()
                # Several historic readers report errors through the status
                # label. Require a fresh result so a swallowed read failure
                # cannot be counted as a successful diagnostic page.
                no_deployed_units = page == "battle" and window.listview_war.count() == 0
                if no_deployed_units:
                    result["note"] = "当前没有部署单位；已读取战场列表"
                else:
                    for name in self.PAGE_SNAPSHOTS.get(page, ()):
                        if getattr(window, name, None) is None:
                            raise RuntimeError("页面未产生新的读取快照: %s" % name)
                if page == "variables" and any(
                        getattr(window, "listview_var_%d" % number).rowCount() == 0
                        for number in (1, 2, 3)):
                    raise RuntimeError("变量页面未产生完整三类读取结果")
                result["success"] = True
            except Exception as exc:
                result["error"] = str(exc)
            self.report["pages"].append(result)
        self.report["recordCounts"] = {
            "person": window.listview_data.count(),
            "deployedBattleUnits": window.listview_war.count(),
            "warehouse": window.listview_item.count(),
            "consumables": window.listview_item_2.count(),
            "effects": window.listview_item_3.count(),
            "fatal": window.listview_mk.count(),
            "diy": window.listview_diy.rowCount(),
        }
        self.report["status"] = window.label_window2.toolTip()

    def finish(self, success, error=None):
        if self.done:
            return
        self.done = True
        self.timer.stop()
        self.window.attach_timer.stop()
        self.window.lock_timer.stop()
        self.report["success"] = bool(success)
        if error:
            self.report["error"] = error
        exit_code = 0 if success else 2
        try:
            destination = self.options.diagnostic_output.expanduser().resolve()
            destination.parent.mkdir(parents=True, exist_ok=True)
            temporary = destination.with_name(destination.name + ".tmp")
            temporary.write_text(json.dumps(self.report, ensure_ascii=False, indent=2),
                                 encoding="utf-8")
            temporary.replace(destination)
        except OSError as exc:
            exit_code = 3
            self.window._set_status("保存诊断报告失败: %s" % exc)
        if self.options.diagnostic_exit or exit_code:
            self.window.close()
            self.app.exit(exit_code)
        else:
            self.window.setEnabled(True)


def startup_smoke(app, ui, destination, exit_after):
    """A completed event-loop callback, not process liveness, proves startup."""
    report = {"schema": "banshou-startup-smoke-v1", "success": False,
              "gameMemoryWrites": False, "remoteCalls": False,
              "capturedAt": datetime.now().astimezone().isoformat(),
              "compatibility": app.property("banshouCompatibility")}
    exit_code = 0
    try:
        resources = {name: (ui.resource_dir / name).is_file()
                     for name in ("logo.ico", "准星.cur", "准星.png")}
        report["resources"] = resources
        report["pages"] = [getattr(ui, "widget_%d" % n).objectName()
                           for n in range(1, 9)]
        report["windowCreated"] = ui.isVisible()
        report["qtPlatform"] = app.platformName()
        report["success"] = all(resources.values()) and len(report["pages"]) == 8 and ui.isVisible()
        if not report["success"]:
            exit_code = 5
        write_report(destination, report)
    except Exception as exc:
        logging.getLogger("banshou").exception("启动自检失败")
        report["error"] = str(exc)
        report["success"] = False
        exit_code = 5
        try:
            write_report(destination, report)
        except OSError:
            pass
    if exit_after or exit_code:
        ui.close()
        app.exit(exit_code)


def _report_startup_exception(report, log_dir, phase, exc):
    """Persist a fatal GUI/bootstrap error before returning exit code 4."""
    logger = logging.getLogger("banshou")
    logger.exception("扳手启动失败：%s", phase)
    report.errors.append("%s：%s" % (phase, exc))
    report.supported = False
    try:
        write_report(Path(log_dir) / "compatibility-report.json",
                     report.as_dict())
    except OSError:
        logger.exception("保存兼容性报告失败")
    message = format_compatibility_errors(report)
    if log_dir:
        message += "\n诊断报告：" + str(Path(log_dir) / "compatibility-report.json")
    show_startup_error(message)


def main(argv=None):
    options = parse_arguments(argv)
    log_dir = _startup_log_dir or configure_startup_logging(Path(__file__).resolve().parent)
    compatibility = _startup_report or collect_compatibility_report(check_native=True)
    write_report(log_dir / "compatibility-report.json", compatibility.as_dict())
    if not compatibility.supported:
        show_startup_error(format_compatibility_errors(compatibility))
        return 4
    app = None
    ui = None
    try:
        app = QApplication([sys.argv[0]])
        app.setProperty("banshouCompatibility", compatibility.as_dict())
        ui = MyDesiger()
        ui.show()
    except Exception as exc:
        _report_startup_exception(compatibility, log_dir,
                                  "Qt界面初始化失败", exc)
        try:
            if ui is not None:
                ui.close()
            if app is not None:
                app.quit()
        except Exception:
            logging.getLogger("banshou").exception("清理启动失败的Qt对象失败")
        return 4
    if options.startup_smoke_output is not None:
        app.setQuitOnLastWindowClosed(False)
        QtCore.QTimer.singleShot(250, lambda: startup_smoke(
            app, ui, options.startup_smoke_output, options.startup_smoke_exit))
    elif options.diagnostic_output is not None:
        app.setQuitOnLastWindowClosed(False)
        ui._diagnostic_run = DiagnosticRun(app, ui, options)
        QtCore.QTimer.singleShot(0, ui._diagnostic_run.start)
    elif options.attach_pid is not None:
        QtCore.QTimer.singleShot(0, lambda: ui.attach_pid(options.attach_pid))
    return app.exec_()


if __name__ == "__main__":
    sys.exit(main())
