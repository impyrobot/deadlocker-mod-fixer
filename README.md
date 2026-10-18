# Deadlocker Mod Fixer

Repairs Deadlock hero skin mods broken by the Violet update (stale camera attachments). Originals
are backed up before anything is replaced.

## Usage

1. Download `DeadlockModFixer.exe` from [Releases](https://github.com/impyrobot/deadlocker-mod-fixer/releases)
   or [deadlocker.net](https://deadlocker.net).
2. Close Deadlock and the Mod Manager.
3. Run the exe. Your addons folder is detected automatically (use **Browse…** if not).
4. Click **Scan Mods**, then **Fix All Stale Mods**.

If Windows SmartScreen shows a warning, click **More info**, then **Run anyway**.

Backups are saved to `%LocalAppData%\DeadlockerModFixer\Backups\<timestamp>\`. The PowerShell script can
restore one:

```powershell
.\FixMods.ps1 -Restore "<backup folder>"
```

## Building

Run `GUI\build.bat`. Put [vpkmerge.exe](https://github.com/Slush97/vpkmerge/releases/tag/v0.22.1) and
`image.png` in the repo root first so they get embedded.

## Credits

The scanning and repair are done by [vpkmerge](https://github.com/Slush97/vpkmerge) by
[Slush97](https://github.com/Slush97). This tool wraps vpkmerge v0.22.1 in a GUI that adds
verification, backups and installation.
