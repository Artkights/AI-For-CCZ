"""Python 3.8-compatible startup diagnostics, independent of Qt and pywin32.

Loader failures before CPython starts must be diagnosed using the onedir
package and system loader.  Once Python starts these helpers can save a report
and display a native Windows message even when importing Qt fails.
"""

from __future__ import annotations

import importlib
import importlib.util
import json
import logging
import os
import shutil
import struct
import sys
import tempfile
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Dict, Iterable, List, Optional


@dataclass
class CompatibilityReport:
    platform: str
    windows_version: Optional[str]
    python_version: str
    process_bits: int
    supported: bool = True
    warnings: List[str] = field(default_factory=list)
    errors: List[str] = field(default_factory=list)
    details: Dict[str, object] = field(default_factory=dict)

    def as_dict(self) -> Dict[str, object]:
        return dict(self.details, schema="banshou-startup-compatibility-v1",
                    platform=self.platform, windowsVersion=self.windows_version,
                    pythonVersion=self.python_version, processBits=self.process_bits,
                    supported=self.supported, warnings=list(self.warnings),
                    errors=list(self.errors))

    def message(self) -> str:
        return "；".join(list(self.errors) + list(self.warnings))


def user_data_dir(install_dir: Path, app_name: Optional[str] = None) -> Path:
    """Mutable data belongs to the current user, including frozen releases."""
    if os.name == "nt":
        root = os.environ.get("LOCALAPPDATA") or os.environ.get("APPDATA")
        if not root:
            try:
                root = str(Path.home() / "AppData" / "Local")
            except RuntimeError:
                root = tempfile.gettempdir()
        return Path(root) / (app_name or "CaoCaoZhuan/Banshou")
    return Path(install_dir) / (app_name or "user-data")


def configure_startup_logging(install_dir: Path) -> Path:
    destination = user_data_dir(install_dir) / "logs"
    try:
        destination.mkdir(parents=True, exist_ok=True)
    except OSError:
        destination = Path(tempfile.gettempdir()) / "CaoCaoZhuan-Banshou" / "logs"
        destination.mkdir(parents=True, exist_ok=True)
    log = logging.getLogger("banshou")
    log.setLevel(logging.INFO)
    path = destination / "startup.log"
    if not any(getattr(handler, "baseFilename", None) == str(path.resolve())
               for handler in log.handlers):
        handler = logging.FileHandler(str(path), encoding="utf-8")
        handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(message)s"))
        log.addHandler(handler)
    return destination


def write_report(path: Path, report: Dict[str, object]) -> None:
    path = Path(path).expanduser().resolve()
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".%d.tmp" % os.getpid())
    try:
        temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2),
                             encoding="utf-8")
        temporary.replace(path)
    finally:
        if temporary.exists():
            temporary.unlink()


def _windows_version() -> Optional[str]:
    if os.name != "nt":
        return None
    try:
        value = sys.getwindowsversion()
        return "%d.%d.%d %s" % (value.major, value.minor, value.build,
                                 getattr(value, "service_pack", ""))
    except (AttributeError, OSError):
        return None


def probe_runtime(strict: Optional[bool] = None) -> CompatibilityReport:
    """Cheap host checks; source/offline tests may use a modern interpreter."""
    strict = bool(getattr(sys, "frozen", False)) if strict is None else strict
    report = CompatibilityReport(sys.platform, _windows_version(),
                                 "%d.%d.%d" % sys.version_info[:3],
                                 struct.calcsize("P") * 8)
    report.details.update(capturedAt=datetime.now().astimezone().isoformat(),
                          frozen=bool(getattr(sys, "frozen", False)),
                          systemBits=64 if (os.environ.get("PROCESSOR_ARCHITEW6432") or
                                            os.environ.get("PROCESSOR_ARCHITECTURE", "").endswith("64")) else report.process_bits)
    if os.name != "nt":
        report.warnings.append("当前不是 Windows；仅支持离线测试")
        return report
    if report.process_bits != 32:
        message = "当前解释器为 %d 位；正常修改游戏请使用 32 位扳手，64 位宿主只允许只读诊断" % report.process_bits
        (report.errors if strict else report.warnings).append(message)
    if strict and sys.version_info[:3] != (3, 8, 10):
        report.errors.append("兼容发行包必须内置 CPython 3.8.10 x86；请重新下载正确的兼容包")
    try:
        value = sys.getwindowsversion()
        report.details["windows"] = dict(major=value.major, minor=value.minor,
                                         build=value.build,
                                         servicePack=getattr(value, "service_pack", ""),
                                         servicePackMajor=getattr(value, "service_pack_major", 0))
        if (value.major, value.minor) < (6, 1):
            report.errors.append("Windows 7 之前的系统不受支持；请使用 Windows 7 SP1 或更新系统")
        elif (value.major, value.minor) == (6, 1):
            if not (getattr(value, "service_pack_major", 0) or
                    getattr(value, "service_pack", "") or value.build >= 7601):
                message = "Windows 7 未检测到 SP1；请安装 SP1 和必要系统更新"
                (report.errors if strict else report.warnings).append(message)
            if sys.version_info[:2] != (3, 8) and not strict:
                report.warnings.append("Win7 发行包应使用 Python 3.8.10 x86，当前为 %s" % report.python_version)
    except (AttributeError, OSError):
        report.warnings.append("无法读取 Windows 版本，继续进行功能探测")
    report.supported = not report.errors
    return report


def check_dependencies(report: CompatibilityReport) -> None:
    """Force native extensions to load before creating QApplication."""
    plugin_root = configure_qt_environment()
    modules = ("_ctypes", "_bz2", "_lzma", "PyQt5.QtCore", "PyQt5.QtWidgets",
               "win32api", "win32gui", "win32process", "psutil", "capstone")
    results = {}
    for name in modules:
        try:
            module = importlib.import_module(name)
            result = {"loaded": True, "path": str(getattr(module, "__file__", "builtin"))}
            if name == "capstone":
                decoder = module.Cs(module.CS_ARCH_X86, module.CS_MODE_32)
                if not list(decoder.disasm(b"\x90", 0x1000)):
                    raise RuntimeError("Capstone 无法反汇编 x86 指令")
                result["version"] = str(module.__version__)
            elif name == "PyQt5.QtCore":
                result["version"] = module.PYQT_VERSION_STR
                result["qtVersion"] = module.QT_VERSION_STR
                plugins = plugin_root or Path(module.QLibraryInfo.location(module.QLibraryInfo.PluginsPath))
                result["platformPlugin"] = str(plugins / "platforms" / "qwindows.dll")
                if not (plugins / "platforms" / "qwindows.dll").is_file():
                    raise OSError("缺少 Qt platforms/qwindows.dll，请完整解压便携包")
            results[name] = result
        except (ImportError, OSError, RuntimeError) as exc:
            results[name] = {"loaded": False, "error": str(exc)}
            report.errors.append("无法加载 %s：%s。请完整解压兼容包，并安装兼容 Win7 的 VC++ x86/UCRT 运行库" % (name, exc))
    report.details["dependencies"] = results
    report.supported = not report.errors


def configure_qt_environment() -> Optional[Path]:
    """Avoid Qt5's ANSI prefix-path conversion on Chinese installation paths.

    QLibraryInfo in the Win32 Qt5 wheel can replace Chinese characters with
    '?' before QApplication exists.  Use Python's Unicode package path for
    plugins, including frozen layouts, rather than that lossy prefix.
    """
    try:
        spec = importlib.util.find_spec("PyQt5")
        for root in (spec.submodule_search_locations if spec else ()) or ():
            for name in ("Qt5", "Qt"):
                plugins = Path(root) / name / "plugins"
                if (plugins / "platforms" / "qwindows.dll").is_file():
                    os.environ["QT_QPA_PLATFORM_PLUGIN_PATH"] = str(plugins)
                    return plugins
    except (ImportError, ValueError, AttributeError):
        pass
    return None


def collect_compatibility_report(check_native: bool = False,
                                 strict: Optional[bool] = None) -> CompatibilityReport:
    report = probe_runtime(strict=strict)
    if check_native and report.supported:
        check_dependencies(report)
    return report


def startup_notice(report: CompatibilityReport) -> str:
    return "[扳手兼容性] %s" % report.message() if report.message() else ""


def format_compatibility_errors(report: CompatibilityReport) -> str:
    return startup_notice(report)


def show_startup_error(message: str) -> None:
    logging.getLogger("banshou").error(message)
    if sys.stderr is not None:
        print(message, file=sys.stderr)
    # Qt may not be importable.  The optional native dialog needs only Win32.
    if os.name == "nt" and os.environ.get("QT_QPA_PLATFORM") != "offscreen":
        try:
            import ctypes
            ctypes.windll.user32.MessageBoxW(None, message, "扳手启动失败", 0x10)
        except (ImportError, OSError, AttributeError):
            pass


def migrate_user_files(source_dir: Path, destination_dir: Path,
                       names: Iterable[str] = ("DIY.csv",)) -> List[Path]:
    """Copy once with exclusive creation, preserving an existing user file."""
    source_dir, destination_dir = Path(source_dir), Path(destination_dir)
    copied = []
    logger = logging.getLogger("banshou")
    try:
        destination_dir.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        logger.warning("无法创建用户目录 %s: %s", destination_dir, exc)
        return copied
    for name in names:
        source, destination = source_dir / name, destination_dir / name
        if destination.exists() or not source.is_file():
            continue
        created = False
        try:
            with source.open("rb") as src, destination.open("xb") as dst:
                created = True
                shutil.copyfileobj(src, dst)
        except OSError as exc:
            if created:
                destination.unlink()
            logger.warning("迁移 %s 失败；继续尝试旧目录: %s", source, exc)
            continue
        copied.append(destination)
        logger.info("用户文件迁移: %s -> %s", source, destination)
    return copied


def data_file_candidates(base_dir: Path, legacy_dir: Path,
                         resource_dir: Optional[Path] = None,
                         name: str = "DIY.csv") -> List[Path]:
    candidates = [Path(base_dir) / name, Path(legacy_dir) / name]
    if resource_dir is not None:
        candidates.append(Path(resource_dir) / name)
    result, seen = [], set()
    for path in candidates:
        key = os.path.normcase(os.path.abspath(str(path)))
        if key not in seen:
            seen.add(key)
            result.append(path)
    return result
