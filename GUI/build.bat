@echo off
setlocal
cd /d "%~dp0"

set CSC="C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
set REFS=C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0

echo Building DeadlockModFixer.exe ...
echo.

%CSC% /target:winexe /out:"..\DeadlockModFixer.exe" /reference:"%REFS%\System.Windows.Forms.dll" /reference:"%REFS%\System.Drawing.dll" /reference:"%REFS%\System.Core.dll" /resource:"..\vpkmerge.exe,Resources.vpkmerge.exe" /resource:"..\image.png,Resources.logo.png" /langversion:6 Program.cs

if %ERRORLEVEL% EQU 0 (
    echo.
    echo SUCCESS: ..\DeadlockModFixer.exe
) else (
    echo.
    echo BUILD FAILED. See errors above.
)
pause
