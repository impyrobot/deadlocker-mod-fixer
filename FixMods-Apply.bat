@echo off
:: FixMods-Apply.bat — Heals stale mods, backs up originals, replaces them in addons.
:: Run the dry-run (FixMods.bat) first to preview what will be changed.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0FixMods.ps1" -Apply
pause
