"""Offline Windows 7 x86 release builder; requires CPython 3.8.10 x86.

--prepare-wheelhouse may run on a newer Python to download the locked wheels.
The release build installs only from that wheelhouse and verifies its hashes.
"""

import argparse
from datetime import datetime, timezone
import email
import hashlib
import json
import os
from pathlib import Path
import platform
import struct
import subprocess
import sys
import time
import unittest
import zipfile

ROOT = Path(__file__).resolve().parent
LOCK = ROOT / "requirements-win7-x86.txt"
DEFAULT_WHEELHOUSE = ROOT / "wheelhouse-win7-x86"
DEFAULT_DIST = ROOT / "dist-win7"
SOURCES = (
    "main.py", "mywindow.py", "compatibility.py", "hook.py", "engine_profile.py",
    "engine_layout.py", "semantic_resolver.py", "runtime_regions.py",
    "process_memory.py", "capability_evidence.py", "evidence_collector.py",
    "runtime_catalog.py", "variable_storage.py", "verify_release.py",
)
REQUIRED_RUNTIME = (
    "qwindows.dll", "_ctypes.pyd", "_bz2.pyd", "_lzma.pyd", "pywintypes38.dll",
    "win32api.pyd", "win32gui.pyd", "win32process.pyd", "capstone.dll",
    "python38.dll", "vcruntime140.dll", "msvcp140.dll",
)
# Optional GetProcAddress probes are allowed; static imports would prevent
# the Windows 7 loader from starting the application.
NEWER_WINDOWS_IMPORTS = {
    "GetSystemTimePreciseAsFileTime", "SetThreadDescription", "GetThreadDescription",
    "GetDpiForWindow", "GetDpiForSystem", "SetThreadDpiAwarenessContext",
    "AdjustWindowRectExForDpi", "SetProcessDpiAwarenessContext", "WaitOnAddress",
    "WakeByAddressSingle", "WakeByAddressAll", "GetOverlappedResultEx", "CreateFile2",
    "GetProcessMitigationPolicy", "SetProcessMitigationPolicy",
    "GetSystemCpuSetInformation", "GetThreadInformation", "SetThreadInformation",
    "GetCurrentThreadStackLimits", "VirtualAllocFromApp", "MapViewOfFileFromApp",
    "IsWow64Process2",
}


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write_json(path, value):
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n",
                          encoding="utf-8")


def run(args, env=None):
    print("+", subprocess.list2cmdline([str(x) for x in args]), flush=True)
    report_dir = (env or {}).get("BANSHOU_BUILD_LOG_DIR")
    if not report_dir:
        subprocess.check_call([str(x) for x in args], cwd=str(ROOT), env=env)
        return
    directory = Path(report_dir)
    directory.mkdir(parents=True, exist_ok=True)
    step = len(list(directory.glob("step-*.json"))) + 1
    stem = "step-%03d" % step
    started = datetime.now(timezone.utc).isoformat()
    tick = time.monotonic()
    process = subprocess.Popen([str(x) for x in args], cwd=str(ROOT), env=env,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               universal_newlines=True, errors="replace")
    log = directory / (stem + ".log")
    with log.open("w", encoding="utf-8") as stream:
        for line in process.stdout:
            stream.write(line)
            try:
                print(line, end="", flush=True)
            except UnicodeEncodeError:
                print(line.encode("ascii", "backslashreplace").decode(), end="", flush=True)
    code = process.wait()
    write_json(directory / (stem + ".json"), {
        "command": [str(x) for x in args], "startedAt": started,
        "durationSeconds": round(time.monotonic() - tick, 3),
        "exitCode": code, "passed": code == 0, "log": log.name})
    if code:
        raise subprocess.CalledProcessError(code, args)


def normalize(name):
    return name.lower().replace("_", "-").replace(".", "-")


def locked_packages():
    result = {}
    for raw in LOCK.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        try:
            requirement, hash_option = line.split(" --hash=sha256:", 1)
            name, version = requirement.split("==", 1)
        except ValueError:
            raise SystemExit("every release requirement must have an exact version and SHA256")
        if len(hash_option) != 64 or any(c not in "0123456789abcdef" for c in hash_option):
            raise SystemExit("invalid SHA256 in release lock: %s" % name)
        if normalize(name) in result:
            raise SystemExit("duplicate release requirement: %s" % name)
        result[normalize(name)] = {"name": name, "version": version,
                                   "sha256": hash_option}
    return result


def wheel_manifest(wheelhouse):
    expected = locked_packages()
    packages = []
    for wheel in sorted(wheelhouse.glob("*.whl")):
        with zipfile.ZipFile(wheel) as archive:
            metadata_name = next(name for name in archive.namelist()
                                 if name.endswith(".dist-info/METADATA"))
            metadata = email.message_from_bytes(archive.read(metadata_name))
        key = normalize(metadata["Name"])
        item = expected.get(key)
        if item is None or metadata["Version"] != item["version"]:
            raise SystemExit("unlocked wheel in wheelhouse: %s" % wheel.name)
        if digest(wheel) != item["sha256"]:
            raise SystemExit("wheel hash mismatch: %s" % wheel.name)
        packages.append(dict(item, filename=wheel.name, size=wheel.stat().st_size,
                             requiresPython=metadata.get("Requires-Python"),
                             requiresDist=metadata.get_all("Requires-Dist") or []))
    keys = [normalize(item["name"]) for item in packages]
    if len(keys) != len(set(keys)) or set(keys) != set(expected):
        raise SystemExit("wheelhouse must contain exactly one wheel per locked package")
    manifest = {"schemaVersion": 1,
                "target": {"python": "3.8.10", "architecture": "x86",
                           "platform": "win32", "minimumOS": "Windows 7 SP1"},
                "lockSha256": digest(LOCK), "packages": packages}
    write_json(wheelhouse / "manifest.json", manifest)
    return manifest


def prepare_wheelhouse(wheelhouse):
    wheelhouse.mkdir(parents=True, exist_ok=True)
    run([sys.executable, "-m", "pip", "download", "--platform", "win32",
         "--python-version", "38", "--implementation", "cp", "--abi", "cp38",
         "--only-binary=:all:", "--require-hashes", "--no-deps", "-r", LOCK,
         "--dest", wheelhouse])
    manifest = wheel_manifest(wheelhouse)
    print("Verified offline wheelhouse: %d packages" % len(manifest["packages"]))


def assert_release_python():
    if sys.platform != "win32" or platform.python_implementation() != "CPython":
        raise SystemExit("release build requires CPython on Windows")
    if struct.calcsize("P") != 4 or sys.version_info[:3] != (3, 8, 10):
        raise SystemExit("release requires CPython 3.8.10 x86; got %s / %d-bit" %
                         (sys.version.split()[0], struct.calcsize("P") * 8))


def verify_installed_versions():
    from importlib import metadata
    for item in locked_packages().values():
        try:
            version = metadata.version(item["name"])
        except metadata.PackageNotFoundError:
            raise SystemExit("missing locked package: %s" % item["name"])
        if version != item["version"]:
            raise SystemExit("installed version differs from lock: %s %s (expected %s)" %
                             (item["name"], version, item["version"]))


def build_environment(build_temp):
    build_temp.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    env.update(TEMP=str(build_temp), TMP=str(build_temp),
               PIP_CACHE_DIR=str(build_temp / "pip-cache"),
               PYINSTALLER_CONFIG_DIR=str(build_temp / "pyinstaller-cache"))
    return env


def inspect_pe_bytes(data, name):
    from verify_release import inspect_pe32
    result = inspect_pe32(data)
    if not result["valid"]:
        raise SystemExit("PE32/x86 check failed for %s: %s" % (name, result))
    return result


def archive_files(exe, bundle_format):
    """Read actual CArchive entries and onedir files, not listing substrings."""
    from PyInstaller.archive.readers import CArchiveReader
    archive = CArchiveReader(str(exe))
    binaries = {exe.name: exe.read_bytes()}
    for name, entry in archive.toc.items():
        if name.lower().endswith((".dll", ".pyd")):
            binaries[name.replace("\\", "/")] = archive.extract(name)
    if bundle_format == "onedir":
        for path in exe.parent.rglob("*"):
            if path.is_file() and path.suffix.lower() in (".dll", ".pyd"):
                binaries[path.relative_to(exe.parent).as_posix()] = path.read_bytes()
    names = {Path(name).name.lower() for name in binaries}
    missing = set(REQUIRED_RUNTIME) - names
    if missing:
        raise SystemExit("bundle missing required native files: %s" % sorted(missing))
    return binaries


def native_import_audit(binaries, output):
    """Static loader audit; it does not claim Win7 system-test coverage."""
    import pefile
    entries, failures = [], []
    for name, data in sorted(binaries.items()):
        headers = inspect_pe_bytes(data, name)
        pe = pefile.PE(data=data, fast_load=True)
        pe.parse_data_directories(directories=[
            pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"],
            pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT"]])
        imports = []
        for entry in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []):
            dll = entry.dll.decode("ascii", "replace")
            functions = [value.name.decode("ascii", "replace")
                         for value in entry.imports if value.name]
            imports.append({"dll": dll, "functions": functions})
            denied = sorted(set(functions) & NEWER_WINDOWS_IMPORTS)
            if denied:
                failures.append({"file": name, "dll": dll, "functions": denied})
        entries.append({"path": name, "sha256": hashlib.sha256(data).hexdigest(),
                        "pe": headers, "imports": imports,
                        "delayImports": [{"dll": value.dll.decode("ascii", "replace"),
                                          "functions": [function.name.decode("ascii", "replace")
                                                        for function in value.imports if function.name]}
                                         for value in getattr(pe, "DIRECTORY_ENTRY_DELAY_IMPORT", [])]})
        pe.close()
    report = {"schemaVersion": 1, "passed": not failures,
              "check": "PE32/x86 and finite known post-Win7 static API deny list",
              "limitations": ["The deny list is not a complete Windows 7 API export database.",
                              "Delay imports are recorded separately and require execution coverage.",
                              "A passing report does not guarantee Windows 7 compatibility."],
              "failures": failures, "files": entries}
    write_json(output, report)
    if failures:
        raise SystemExit("post-Win7 static APIs found; see %s" % output)
    return report


def startup_smoke(exe, output, env):
    output = output.resolve()
    if output.exists():
        output.unlink()
    smoke_env = env.copy()
    smoke_env["QT_QPA_PLATFORM"] = "offscreen"
    flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
    try:
        result = subprocess.run([str(exe), "--startup-smoke-output", str(output),
                                 "--startup-smoke-exit"], cwd=str(exe.parent),
                                env=smoke_env, creationflags=flags, timeout=30)
    except subprocess.TimeoutExpired:
        raise SystemExit("packaged startup smoke timed out")
    if result.returncode != 0 or not output.is_file():
        raise SystemExit("startup smoke failed (exit %s)" % result.returncode)
    report = json.loads(output.read_text(encoding="utf-8"))
    if report.get("success") is not True:
        raise SystemExit("startup smoke did not report success: %s" % output)
    return report


def checksum_tree(root, output):
    files = [path for path in root.rglob("*") if path.is_file() and path != output]
    output.write_text("".join("%s *%s\n" % (digest(path), path.relative_to(root).as_posix())
                              for path in sorted(files)), encoding="utf-8")


def run_test_report(output):
    """Run the existing suite and retain machine-readable failures/skips."""
    tests_path = str(ROOT / "tests")
    if tests_path not in sys.path:
        sys.path.insert(0, tests_path)
    class ReportResult(unittest.TextTestResult):
        def __init__(self, *args, **kwargs):
            super().__init__(*args, **kwargs)
            self.successes = []

        def addSuccess(self, test):
            self.successes.append(test.id())
            super().addSuccess(test)

    tick = time.monotonic()
    suite = unittest.defaultTestLoader.discover(str(ROOT / "tests"), top_level_dir=str(ROOT))
    result = unittest.TextTestRunner(verbosity=2, resultclass=ReportResult).run(suite)
    report = {"schemaVersion": 1, "passed": result.wasSuccessful(),
              "python": sys.version, "platform": platform.platform(),
              "durationSeconds": round(time.monotonic() - tick, 3),
              "testsRun": result.testsRun, "passedTests": result.successes,
              "failures": [{"test": test.id(), "traceback": detail} for test, detail in result.failures],
              "errors": [{"test": test.id(), "traceback": detail} for test, detail in result.errors],
              "skipped": [{"test": test.id(), "reason": reason} for test, reason in result.skipped],
              "expectedFailures": [{"test": test.id(), "traceback": detail}
                                   for test, detail in result.expectedFailures],
              "unexpectedSuccesses": [test.id() for test in result.unexpectedSuccesses]}
    write_json(output, report)
    return 0 if report["passed"] else 1


def compatibility_matrix():
    checks = ["startup-ui-resources", "attach-32bit-game", "eight-pages-read",
              "write-readback-restore", "remote-call-and-patch-cleanup",
              "chinese-path-and-user", "missing-runtime-diagnostics"]
    systems = ("Windows 7 SP1 x86", "Windows 7 SP1 x64", "Windows 8.1 x86",
               "Windows 8.1 x64", "Windows 10 x86", "Windows 10 x64", "Windows 11 x64")
    return {"schemaVersion": 1, "status": "pending",
            "note": "Build-host unit tests and startup smoke do not populate this system matrix.",
            "systems": [{"system": name, "status": "pending", "evidence": None,
                         "checks": {check: "pending" for check in checks}} for name in systems]}


def write_distribution_docs(variant, bundle_format, wheelhouse=DEFAULT_WHEELHOUSE):
    readme = """曹操传 Python 扳手 — Windows 7 x86 兼容构建

解压整个 ZIP 后运行 6.6扳手.exe。目录版请保留完整目录结构。
支持目标：Windows 7 SP1 及以上，运行 32 位曹操传游戏；无需另装 Python。
Win7 前置更新：KB2533623 与 Universal C Runtime（KB2999226 或后续汇总）；
缺少 API/MSVC 运行库时，请先安装官方系统更新和适用于 Win7 的 VC++ x86 运行库。
普通启动后，将准星拖到游戏窗口完成附加。

发布包中的静态审计、源码一致性和启动检查可供复核；它们不代表全部操作系统
已经验收。请查看 compatibility-matrix.json，pending 表示尚未完成实机验证。
报告中的 native API 检查只覆盖已知有限的新版 Windows API，不能保证 Win7 兼容。
首次验证修改功能请使用游戏副本与备份存档，按说明完成写入、复读与恢复检查。

本工具含第三方组件，相关许可证见 licenses/；项目许可证见 LICENSE。
"""
    (variant / "使用说明.txt").write_text(readme, encoding="utf-8-sig")
    (variant / "LICENSE").write_bytes((ROOT / "LICENSE").read_bytes())
    write_json(variant / "compatibility-matrix.json", compatibility_matrix())
    license_dir = variant / "licenses"
    license_dir.mkdir(exist_ok=True)
    # Preserve upstream license texts shipped in the reviewed wheels.
    for wheel in Path(wheelhouse).glob("*.whl"):
        with zipfile.ZipFile(wheel) as archive:
            for name in archive.namelist():
                base = Path(name).name
                if base and (base.lower().startswith(("license", "copying")) or
                             base.lower() == "notice"):
                    target = license_dir / wheel.name.replace(".whl", "")
                    target.mkdir(exist_ok=True)
                    (target / base).write_bytes(archive.read(name))


def package_variant(bundle_format, dist, build_temp, env, skip_smoke, wheelhouse):
    variant = dist / bundle_format
    variant.mkdir(parents=True, exist_ok=True)
    reports = dist / "reports" / bundle_format
    reports.mkdir(parents=True, exist_ok=True)
    local_env = env.copy()
    local_env["BANSHOU_BUNDLE_FORMAT"] = bundle_format
    run([sys.executable, "-m", "PyInstaller", "--noconfirm", "--clean",
         "--distpath", variant, "--workpath", build_temp / bundle_format,
         ROOT / "banshou66.spec"], env=local_env)
    exe = (variant / "6.6扳手.exe" if bundle_format == "onefile" else
           variant / "6.6扳手" / "6.6扳手.exe")
    inspect_pe_bytes(exe.read_bytes(), str(exe))
    binaries = archive_files(exe, bundle_format)
    audit = native_import_audit(binaries, reports / "native-import-audit.json")
    from verify_release import verify_release
    source_check = verify_release(exe, ROOT)
    write_json(reports / "source-consistency.json", source_check)
    if not source_check["passed"]:
        raise SystemExit("source/bytecode mismatch: %s" % source_check["errors"])
    smoke = None if skip_smoke else startup_smoke(exe, reports / "startup-smoke.json", env)
    write_distribution_docs(variant, bundle_format, wheelhouse)
    checksum_tree(variant, variant / "SHA256SUMS.txt")
    zip_path = dist / ("banshou-win7-x86-%s.zip" % bundle_format)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(variant.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(variant).as_posix())
    return {"format": bundle_format, "executable": str(exe.relative_to(dist)),
            "sha256": digest(exe), "zip": zip_path.name, "zipSha256": digest(zip_path),
            "nativeFilesAudited": len(audit["files"]), "startupSmoke": smoke,
            "sourceConsistency": "reports/%s/source-consistency.json" % bundle_format}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prepare-wheelhouse", action="store_true")
    parser.add_argument("--run-tests-report", type=Path, help=argparse.SUPPRESS)
    parser.add_argument("--wheelhouse", type=Path, default=DEFAULT_WHEELHOUSE)
    parser.add_argument("--dist", type=Path, default=DEFAULT_DIST)
    parser.add_argument("--format", choices=("both", "onedir", "onefile"), default="both")
    parser.add_argument("--skip-install", action="store_true",
                        help="verify installed versions instead of offline installation")
    parser.add_argument("--offline", action="store_true", help="alias; builds are always offline")
    parser.add_argument("--skip-smoke", "-SkipSmoke", action="store_true")
    args = parser.parse_args(argv)
    if args.run_tests_report:
        return run_test_report(args.run_tests_report)
    wheelhouse = args.wheelhouse.resolve()
    if args.prepare_wheelhouse:
        prepare_wheelhouse(wheelhouse)
        return 0
    assert_release_python()
    wheels = wheel_manifest(wheelhouse)
    dist = args.dist.resolve()
    if dist == ROOT / "dist":
        raise SystemExit("use a separate output directory; preserve the existing dist release")
    dist.mkdir(parents=True, exist_ok=True)
    build_temp = Path(os.environ.get("BANSHOU_BUILD_TEMP", str(ROOT / "build-temp-win7"))).resolve()
    env = build_environment(build_temp)
    env["BANSHOU_BUILD_LOG_DIR"] = str(dist / "reports" / "build-steps")
    if not args.skip_install:
        run([sys.executable, "-m", "pip", "install", "--no-index", "--find-links",
             wheelhouse, "--require-hashes", "--only-binary=:all:", "-r", LOCK], env=env)
    verify_installed_versions()
    run([sys.executable, "-m", "py_compile"] + list(SOURCES), env=env)
    test_env = env.copy()
    test_env["QT_QPA_PLATFORM"] = "offscreen"
    run([sys.executable, ROOT / "build_release.py", "--run-tests-report",
         dist / "reports" / "tests.json"], env=test_env)
    formats = ("onedir", "onefile") if args.format == "both" else (args.format,)
    artifacts = [package_variant(value, dist, build_temp, env, args.skip_smoke, wheelhouse)
                 for value in formats]
    manifest = {"schemaVersion": 1, "builtAt": datetime.now(timezone.utc).isoformat(),
                "target": wheels["target"], "builder": {"python": sys.version,
                "windows": platform.platform()}, "lockSha256": digest(LOCK),
                "source": {name: digest(ROOT / name) for name in SOURCES},
                "artifacts": artifacts, "validation": {"startupSmokeSkipped": args.skip_smoke,
                "win7SystemTest": "not run by build script"}}
    write_json(dist / "manifest.json", manifest)
    write_json(dist / "compatibility-matrix.json", compatibility_matrix())
    checksum_tree(dist, dist / "SHA256SUMS.txt")
    print("Build complete:", dist)
    print("Win7 compatibility still requires the documented VM test matrix.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
