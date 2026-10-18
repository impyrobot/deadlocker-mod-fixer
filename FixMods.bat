@echo off
:: FixMods.bat — Dry run: scans mods and reports stale ones. Nothing is changed.
:: Double-click this to see what needs fixing before committing to -Apply.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0FixMods.ps1"
pause
