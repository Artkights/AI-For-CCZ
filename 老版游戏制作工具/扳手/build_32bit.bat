@echo off
setlocal
cd /d "%~dp0"

python -c "import struct,sys; sys.exit(0 if struct.calcsize('P') == 4 else 1)"
if errorlevel 1 (
    echo Error: use a 32-bit Python 3.8-3.11 interpreter.
    exit /b 1
)

python -m pip install -r requirements.txt
if errorlevel 1 exit /b 1

python -m PyInstaller --noconfirm --clean --onefile --windowed ^
    --name "6.6扳手" ^
    --icon "logo.ico" ^
    --add-data "logo.ico;." ^
    --add-data "准星.cur;." ^
    --add-data "准星.png;." ^
    --add-data "evidence;evidence" ^
    main.py
if errorlevel 1 exit /b 1

echo Build complete: dist\6.6扳手.exe
endlocal
