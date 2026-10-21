@echo off
setlocal
cd /d "%~dp0"

rem Compiler and reference assemblies. CI sets CSC/REFS itself; locally the VS 2019 BuildTools defaults apply.
if not defined CSC  set "CSC=C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
if not defined REFS set "REFS=C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0"

echo Building DeadlockModFixer.exe ...
echo.

"%CSC%" /target:winexe /out:"..\DeadlockModFixer.exe" /reference:"%REFS%\System.Windows.Forms.dll" /reference:"%REFS%\System.Drawing.dll" /reference:"%REFS%\System.Core.dll" /resource:..\image.png,Resources.logo.png /langversion:6 Program.cs
set RESULT=%ERRORLEVEL%

if %RESULT% EQU 0 (
    echo.
    echo SUCCESS: ..\DeadlockModFixer.exe
) else (
    echo.
    echo BUILD FAILED. See errors above.
)
if not defined CI pause
exit /b %RESULT%
