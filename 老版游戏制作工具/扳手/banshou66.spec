# -*- mode: python ; coding: utf-8 -*-

from pathlib import Path
import sys
from PyInstaller.utils.hooks import (collect_dynamic_libs,
                                     collect_submodules)


project_dir = Path(SPECPATH)
runtime_dll_dir = Path(sys.prefix) / "Library" / "bin"
runtime_dll_names = ("ffi.dll", "liblzma.dll", "libbz2.dll")
missing_runtime_dlls = [name for name in runtime_dll_names
                        if not (runtime_dll_dir / name).is_file()]
if missing_runtime_dlls:
    raise FileNotFoundError(
        "Missing Conda runtime DLLs: %s" % ", ".join(missing_runtime_dlls))
binaries = ([(str(runtime_dll_dir / name), ".")
             for name in runtime_dll_names] +
            collect_dynamic_libs("capstone"))
datas = [
    (str(project_dir / "logo.ico"), "."),
    (str(project_dir / "准星.cur"), "."),
    (str(project_dir / "准星.png"), "."),
    (str(project_dir / "evidence"), "evidence"),
]

a = Analysis(
    [str(project_dir / "main.py")],
    pathex=[str(project_dir)],
    binaries=binaries,
    datas=datas,
    hiddenimports=["win32timezone"] + collect_submodules("capstone"),
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
)
pyz = PYZ(a.pure, a.zipped_data)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name="6.6扳手",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=str(project_dir / "logo.ico"),
)
