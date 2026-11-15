# -*- mode: python ; coding: utf-8 -*-

from pathlib import Path
import os
import sys
from PyInstaller.utils.hooks import (collect_dynamic_libs,
                                     collect_data_files,
                                     collect_submodules)
from PyInstaller.utils.hooks.qt import pyqt5_library_info


project_dir = Path(SPECPATH)
bundle_format = os.environ.get("BANSHOU_BUNDLE_FORMAT", "onedir")
if bundle_format not in ("onedir", "onefile"):
    raise ValueError("BANSHOU_BUNDLE_FORMAT must be onedir or onefile")


def _repair_pyqt5_paths():
    """Override QLibraryInfo's lossy path probe for non-ASCII build paths.

    PyInstaller 5.13 asks Qt for ``QLibraryInfo.location()`` through an
    isolated helper.  On Python 3.8/Qt 5.15 this can turn a Chinese checkout
    path into ``?`` characters before the hook calls ``Path.resolve``.  The
    wheel layout is deterministic, so derive every path from the Unicode
    ``PyQt5.__file__`` path and update the hook singleton used by Analysis.
    """
    try:
        import PyQt5
        qt_root = Path(PyQt5.__file__).resolve().parent / "Qt5"
    except (ImportError, OSError):
        return
    if not qt_root.is_dir():
        return
    locations = dict(getattr(pyqt5_library_info, "location", {}) or {})
    locations.update({
        "PrefixPath": str(qt_root),
        "BinariesPath": str(qt_root / "bin"),
        "LibrariesPath": str(qt_root / "lib"),
        "PluginsPath": str(qt_root / "plugins"),
        "TranslationsPath": str(qt_root / "translations"),
        "DataPath": str(qt_root),
        "LibraryExecutablesPath": str(qt_root / "bin"),
        "HeadersPath": str(qt_root / "include"),
        "ImportsPath": str(qt_root / "qml"),
        "Qml2ImportsPath": str(qt_root / "qml"),
        "QmlImportsPath": str(qt_root / "qml"),
        "ExamplesPath": str(qt_root / "examples"),
        "TestsPath": str(qt_root / "tests"),
    })
    pyqt5_library_info.location = locations
    pyqt5_library_info.package_location = str(Path(PyQt5.__file__).resolve().parent)
    if not getattr(pyqt5_library_info, "version", None):
        pyqt5_library_info.version = (5, 15, 2)


_repair_pyqt5_paths()


def _runtime_dlls():
    """Collect optional DLLs from CPython, Conda and wheel layouts."""
    site_packages = Path(sys.prefix) / "Lib" / "site-packages"
    roots = [
        Path(sys.prefix),
        Path(sys.prefix) / "DLLs",
        Path(sys.prefix) / "Library" / "bin",
        site_packages / "pywin32_system32",
    ]
    names = (
        "ffi.dll", "libffi-7.dll", "libffi-8.dll",
        "libbz2.dll", "libbz2-*.dll", "liblzma.dll", "liblzma-*.dll",
        "zlib1.dll", "zlib.dll",
        "vcruntime140*.dll", "msvcp140*.dll",
        "pywintypes*.dll", "pythoncom*.dll",
    )
    found = []
    seen = set()
    for root in roots:
        if not root.is_dir():
            continue
        for pattern in names:
            for path in sorted(root.glob(pattern)):
                destination = ("pywin32_system32"
                               if root.name.lower() == "pywin32_system32"
                               else ".")
                key = (path.name.lower(), destination)
                if path.is_file() and key not in seen:
                    seen.add(key)
                    found.append((str(path), destination))
    return found


# Let PyInstaller's Qt hooks collect the imported Qt modules and their
# dependencies rather than dragging unrelated network/media DLLs into a
# memory editor.  Add qwindows and the MSVC runtimes explicitly.
qt_dlls = collect_dynamic_libs("PyQt5")
qt_required = (
    "qt5core.dll", "qt5gui.dll", "qt5widgets.dll", "qwindows.dll",
    "vcruntime140.dll", "msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll",
    "concrt140.dll", "d3dcompiler_47.dll", "libegl.dll", "libglesv2.dll",
    "opengl32sw.dll",
)
binaries = (_runtime_dlls() +
            [(source, target) for source, target in qt_dlls
             if Path(source).name.lower() in qt_required] +
            collect_dynamic_libs("capstone"))
datas = [
    (str(project_dir / "logo.ico"), "."),
    (str(project_dir / "准星.cur"), "."),
    (str(project_dir / "准星.png"), "."),
    (str(project_dir / "evidence"), "evidence"),
] + collect_data_files("PyQt5", subdir="Qt5/plugins/platforms")

a = Analysis(
    [str(project_dir / "main.py")],
    pathex=[str(project_dir)],
    binaries=binaries,
    datas=datas,
    hiddenimports=["win32timezone", "evidence_collector", "_ctypes", "_bz2", "_lzma", "pywintypes",
                   "pythoncom", "win32api", "win32gui", "win32process"] +
                  collect_submodules("capstone"),
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
)
pyz = PYZ(a.pure, a.zipped_data)

exe_options = dict(
    name="6.6扳手", debug=False, bootloader_ignore_signals=False,
    strip=False, upx=False, console=False, disable_windowed_traceback=False,
    argv_emulation=False, target_arch=None, codesign_identity=None,
    entitlements_file=None, icon=str(project_dir / "logo.ico"),
)
if bundle_format == "onefile":
    exe = EXE(pyz, a.scripts, a.binaries, a.zipfiles, a.datas, [], **exe_options)
else:
    exe = EXE(pyz, a.scripts, [], exclude_binaries=True, **exe_options)
    collect = COLLECT(exe, a.binaries, a.zipfiles, a.datas,
                      strip=False, upx=False, name="6.6扳手")
