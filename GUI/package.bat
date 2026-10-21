@echo off
setlocal
cd /d "%~dp0\.."

rem Packages DeadlockModFixer.exe + the official, unmodified vpkmerge.exe into a release zip
rem and prints the SHA-256 hashes to publish on the release page and fixer guide.
rem Run build.bat first.

set VERSION=1.1.0
set ZIP=DeadlockModFixer-v%VERSION%.zip

if not exist DeadlockModFixer.exe (
    echo DeadlockModFixer.exe not found. Run GUI\build.bat first.
    pause
    exit /b 1
)
if not exist vpkmerge.exe (
    echo vpkmerge.exe not found. Download v0.22.1 from https://github.com/Slush97/vpkmerge/releases/tag/v0.22.1
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference = 'Stop';" ^
  "$expected = 'b4a5d0e3a3a1d4e627cb4979216807295351c7fb38e5a3aa6345b213a5c69275';" ^
  "if ((Get-FileHash vpkmerge.exe -Algorithm SHA256).Hash.ToLower() -ne $expected) { throw 'vpkmerge.exe is not the official v0.22.1 build' }" ^
  "if (Test-Path '%ZIP%') { Remove-Item '%ZIP%' }" ^
  "Compress-Archive -Path DeadlockModFixer.exe, vpkmerge.exe -DestinationPath '%ZIP%';" ^
  "Get-FileHash '%ZIP%', DeadlockModFixer.exe, vpkmerge.exe -Algorithm SHA256 | ForEach-Object { '{0}  {1}' -f $_.Hash.ToLower(), (Split-Path $_.Path -Leaf) }"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo SUCCESS: %ZIP%
) else (
    echo.
    echo PACKAGING FAILED. See errors above.
)
pause
