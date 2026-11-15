@echo off
setlocal
cd /d "%~dp0"

rem Win7 release: use official CPython 3.8.10 x86.
python build_release.py %*
if errorlevel 1 exit /b 1

echo Build complete: dist\6.6-banshou.exe
endlocal
