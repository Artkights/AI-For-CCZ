"""Opt-in 60-second Qt/FakeMemory lifecycle soak (not unittest discovery).

Run with the bundled Python 3.8 x86 runtime from the project directory:
    python -B tests/patch_lifecycle_soak.py

This checks UI and memory-session lifetimes using synthetic isolated memory.
The native three-site recipe is located from a local EXE on disk.  No target
process is opened, queried, executed, or modified by this script.
"""

import argparse
import copy
import hashlib
import json
import os
import platform
import struct
import sys
import time
import traceback
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import Mock, patch

PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT))
os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from PyQt5 import QtCore
from PyQt5.QtWidgets import QApplication

import mywindow
from engine_layout import FeatureAccess
from engine_profile import (EngineVersion, PEImage, PatchSite, PatchSpec,
                            _legacy_auto_return_recipe, legacy_profile,
                            locate_auto_return_patch_66)
from main import MyDesiger
from process_memory import FakeMemoryBackend, MemorySession


class CodeImage:
    """Only the static bytes required to derive the legacy relocated stub."""
    def __init__(self):
        self.data = bytearray(0x200000)

    def put(self, address, raw):
        offset = address - 0x400000
        self.data[offset:offset + len(raw)] = raw

    def read_va(self, address, size):
        offset = address - 0x400000
        if offset < 0 or offset + size > len(self.data):
            raise ValueError("synthetic image range exceeded")
        return bytes(self.data[offset:offset + size])


class SoakCase:
    def __init__(self, version, native_recipe, dynamic=False):
        self.version = version
        self.dynamic = dynamic
        self.name = version.name + ("_legacy_dynamic" if dynamic else "_native")
        self.window = MyDesiger()
        self.backend = FakeMemoryBackend(0x400000, bytes(0x200000))
        self.session = MemorySession(1, 0x400000, 0x200000,
                                     backend=self.backend, owns_handle=False)
        # 6.6 is deliberately a UI lifecycle fixture, not a claimed complete
        # reconstruction of every native 6.6 table or remote-call ABI.
        self.profile = legacy_profile(version if version != EngineVersion.V66
                                      else EngineVersion.V65)
        self.profile.version = version
        self.profile.display_version = "6.6" if version == EngineVersion.V66 else self.profile.display_version
        self.profile.variant_name = "isolated-lifecycle-soak"
        self.profile.sha256 = "SOAK-%s" % self.name
        self.profile.access_policy = "strict"
        self.profile.direct_write = False
        self.profile.patches.clear()
        self.profile.dynamic_patches.clear()
        self.ordinary = PatchSpec("control_friendly", "模拟普通补丁", (
            PatchSite(0x401000, b"\x74", b"\xEB"),
            PatchSite(0x401010, b"\x75", b"\x90")))
        self.profile.patches[self.ordinary.key] = self.ordinary
        for site in self.ordinary.sites:
            self.backend.write(site.address, site.original)
        self.auto = native_recipe
        if dynamic:
            image = CodeImage()
            target = 0x412D52
            for address in (0x44E570, 0x44EA73):
                image.put(address, b"\xE8" + struct.pack("<i", target - address - 5))
            for address in (target, 0x43E4D6, 0x43E6AA):
                image.put(address, b"\x55\x8B\xEC")
            self.auto, reason = _legacy_auto_return_recipe(self.profile, image)
            if self.auto is None:
                raise AssertionError(reason)
            self.profile.dynamic_patches[self.auto.key] = self.auto
            for site in self.auto.call_sites:
                self.backend.write(site.address, site.original)
        else:
            self.profile.patches[self.auto.key] = self.auto
            for site in self.auto.sites:
                self.backend.write(site.address, site.original)
        for key in (self.ordinary.key, self.auto.key):
            self.profile.set_feature_access(key, FeatureAccess.READ_WRITE,
                                            runtime_probe_passed=True)
        self.window.engine_profile = self.profile
        self.window.memory_session = self.session
        self.window.md, self.window.p = self.session, self.session.handle
        self.window.ok = True
        self.window.widget_1.setEnabled(True)
        self.window._attach_context = (123, 0x400000, 0x200000,
                                       "synthetic-do-not-open.exe", None, None)
        self.window._page_readiness = {key: True for key in (
            "person", "battle", "warehouse", "effect", "effect_exclusive",
            "fatal", "variables_bool", "variables_int", "variables_ptr")}
        self.window.attach_timer.stop()
        self.window.lock_timer.stop()
        self.window._queue_profile_scan = Mock()
        self.window.isActiveWindow = Mock(return_value=True)
        # Activation still runs its real capability refresh.  Data-page
        # loaders are isolated because this is a patch lifecycle fixture.
        self.page_refreshes = {}
        for name in ("onData", "onWar", "onItem", "onPower", "onMk", "onVar", "DIY_Page"):
            control = Mock()
            self.page_refreshes[name] = control
            setattr(self.window, name, control)
        self.refresh = Mock(wraps=self.window._refresh_profile_on_demand)
        self.window._refresh_profile_on_demand = self.refresh
        self.metrics = {"name": self.name, "engine_version": int(version),
                        "display_version": self.profile.display_version,
                        "memory_backend": "FakeMemoryBackend", "dynamic": dynamic,
                        "cycles": 0, "page_switches": 0, "activation_events": 0,
                        "activation_handler_calls": 0, "background_merges": 0,
                        "full_scans": 0, "restored": False,
                        "reenabled_and_restored": False}
        self.set_enabled(True)

    def set_enabled(self, enabled):
        mywindow.version = int(self.version)
        for number in (1, 7):
            control = getattr(self.window, "checkBox_%d" % number)
            control.setChecked(enabled)
            self.window.onButtonClick(number)
            if not control.isEnabled() or control.isChecked() != enabled:
                raise AssertionError("%s control %d failed toggle %s" %
                                     (self.name, number, enabled))
        self.verify(enabled)

    def verify(self, enabled):
        if self.window.engine_profile is not self.profile:
            raise AssertionError("stable profile object was replaced")
        for spec in (self.ordinary,) + (() if self.dynamic else (self.auto,)):
            for site in spec.sites:
                raw = site.patched if enabled else site.original
                if self.backend.read(site.address, len(raw)) != raw:
                    raise AssertionError("%s site not restored/owned" % self.name)
        if self.dynamic:
            observation = self.session.probe_dynamic_patch(self.auto)
            expected = "OWNED_PATCHED" if enabled else "ORIGINAL"
            if observation["state"] != expected:
                raise AssertionError("%s: %r" % (self.name, observation))
        for number in (1, 7):
            control = getattr(self.window, "checkBox_%d" % number)
            if not control.isEnabled() or control.isChecked() != enabled:
                raise AssertionError("%s checkbox changed" % self.name)
        if self.window._queue_profile_scan.call_count:
            raise AssertionError("stable lifecycle triggered full profile scan")

    def cycle(self):
        mywindow.version = int(self.version)
        pages = ("on_action_C_triggered", "on_action_M_triggered",
                 "on_action_B_triggered", "on_action_T_triggered")
        getattr(self.window, pages[self.metrics["cycles"] % len(pages)])()
        self.metrics["page_switches"] += 1
        before = self.refresh.call_count
        QApplication.sendEvent(self.window, QtCore.QEvent(QtCore.QEvent.ActivationChange))
        self.metrics["activation_events"] += 1
        self.metrics["activation_handler_calls"] += self.refresh.call_count - before
        if self.metrics["cycles"] % 10 == 0:
            candidate = copy.deepcopy(self.profile)
            candidate.patches.clear()
            candidate.dynamic_patches.clear()
            for key in (self.ordinary.key, self.auto.key):
                candidate.set_feature_access(key, FeatureAccess.UNAVAILABLE,
                                             reason="independent empty-recipe scan")
            if self.window._merge_scanned_profile(candidate) is not self.profile:
                raise AssertionError("merge replaced stable profile")
            self.window._probe_patch_controls()
            self.metrics["background_merges"] += 1
        self.verify(True)
        self.metrics["cycles"] += 1

    def finish(self):
        self.set_enabled(False)
        self.metrics["restored"] = True
        self.set_enabled(True)
        self.set_enabled(False)
        self.metrics["reenabled_and_restored"] = True
        self.metrics["full_scans"] = self.window._queue_profile_scan.call_count
        self.metrics["active_patch_count_after_restore"] = len(self.session._active_patches)
        self.metrics["dynamic_lease_count_after_restore"] = len(self.session._dynamic_leases)
        self.metrics["freed_dynamic_allocations"] = len(self.backend.freed)
        if self.metrics["page_switches"] < 10:
            raise AssertionError("not enough page switches")
        if self.metrics["activation_handler_calls"] < 10:
            raise AssertionError("Qt ActivationChange did not dispatch to business handler")
        self.window.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--duration", type=float, default=60.2)
    parser.add_argument("--output", type=Path,
                        default=PROJECT / "docs/reports/patch-lifecycle-soak-20261109.json")
    parser.add_argument("--native-exe", type=Path, default=(
        PROJECT.parents[1] / "基底/新改曹操傳6.6修正版/Ekd5.exe"))
    args = parser.parse_args()
    if args.duration < 60:
        parser.error("the wall-clock soak duration must be at least 60 seconds")
    native, contract, reason = locate_auto_return_patch_66(PEImage(str(args.native_exe)))
    if native is None or len(native.sites) != 3:
        raise AssertionError("native three-site recipe unavailable: " + reason)
    app = QApplication.instance() or QApplication([])
    app.setQuitOnLastWindowClosed(False)
    report = {"schema": "patch-lifecycle-soak-v1",
              "started_at_utc": datetime.now(timezone.utc).isoformat(),
              "python": {"version": platform.python_version(),
                         "executable": sys.executable,
                         "pointer_bits": struct.calcsize("P") * 8},
              "host": {"platform": platform.platform(), "release": platform.release(),
                       "machine": platform.machine(), "qt_platform": app.platformName()},
              "isolation": {"target_process_access": False,
                            "backend": "FakeMemoryBackend",
                            "activation_data_page_loaders": "mocked; real patch refresh retained",
                            "native_source": "local disk PEImage only",
                            "win7_execution_claimed": False},
              "native_recipe": {"exe": str(args.native_exe.resolve()),
                                "sha256": hashlib.sha256(args.native_exe.read_bytes()).hexdigest(),
                                "patch_sites": [site.address for site in native.sites],
                                "mode": contract["mode"]},
              "requested_duration_seconds": args.duration, "errors": [], "cases": []}
    cases = []
    timer = QtCore.QTimer()
    timer.setInterval(200)
    elapsed_start = time.monotonic()
    last_progress = [elapsed_start]

    def tick():
        try:
            for case in cases:
                case.cycle()
            now = time.monotonic()
            if now - last_progress[0] >= 15:
                print("soak elapsed=%.3f cycles_per_case=%d" %
                      (now - elapsed_start, cases[0].metrics["cycles"]), flush=True)
                last_progress[0] = now
            if now - elapsed_start >= args.duration:
                timer.stop()
                app.quit()
        except Exception:
            report["errors"].append(traceback.format_exc())
            timer.stop()
            app.quit()

    with patch.object(MemorySession, "open_pid", side_effect=AssertionError("real process access forbidden")), \
            patch.object(mywindow, "get_process_module", side_effect=AssertionError("real module query forbidden")):
        try:
            for version in (EngineVersion.V61, EngineVersion.V62, EngineVersion.V63,
                            EngineVersion.V63_MP, EngineVersion.V64,
                            EngineVersion.V65, EngineVersion.V66):
                cases.append(SoakCase(version, native))
            cases.append(SoakCase(EngineVersion.V64, native, dynamic=True))
            elapsed_start = time.monotonic()
            last_progress[0] = elapsed_start
            print("starting isolated Qt soak: cases=%d duration=%.1fs" %
                  (len(cases), args.duration), flush=True)
            timer.timeout.connect(tick)
            timer.start()
            app.exec_()
            report["elapsed_seconds"] = time.monotonic() - elapsed_start
            for case in cases:
                case.finish()
            if report["elapsed_seconds"] < 60:
                raise AssertionError("less than 60 seconds of sustained execution")
        except Exception:
            report["errors"].append(traceback.format_exc())
        finally:
            report["cases"] = [case.metrics for case in cases]
            for case in cases:
                if not case.session.closed:
                    case.window.close()
    report["completed_at_utc"] = datetime.now(timezone.utc).isoformat()
    report["success"] = (not report["errors"] and len(cases) == 8 and
                         all(case.metrics["reenabled_and_restored"] for case in cases))
    report["total_cycles"] = sum(case.metrics["cycles"] for case in cases)
    report["total_background_merges"] = sum(case.metrics["background_merges"] for case in cases)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"success": report["success"], "elapsed_seconds": report.get("elapsed_seconds"),
                      "total_cycles": report["total_cycles"], "output": str(args.output),
                      "errors": report["errors"]}, ensure_ascii=False), flush=True)
    return 0 if report["success"] else 1


if __name__ == "__main__":
    sys.exit(main())
