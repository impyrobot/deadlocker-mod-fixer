# Deadlocker Mod Fixer

Repairs Deadlock hero skin mods broken by the Violet update (stale camera attachments). Originals
are backed up before anything is replaced.

## Usage

1. Download `DeadlockModFixer.exe` from [Releases](https://github.com/impyrobot/deadlocker-mod-fixer/releases)
   or [deadlocker.net](https://deadlocker.net).
2. Close Deadlock and the Mod Manager.
3. Run `DeadlockModFixer.exe`. Your addons folder is detected automatically (use **Browse…** if not).
4. Click **Scan Mods**, then **Fix All Stale Mods**.

The repair itself is done by [vpkmerge v0.22.1](https://github.com/Slush97/vpkmerge/releases/tag/v0.22.1). On the
first scan the fixer downloads the official copy from Slush97's GitHub release into
`%LocalAppData%\DeadlockerModFixer\` and checks its SHA-256 before running it. Any other copy is refused. To run
offline, put the official `vpkmerge.exe` next to `DeadlockModFixer.exe`, or download the zip from the release
page, which includes both.

The results show which hero each mod is for, and the update banner tells you when a new version is out. If
something goes wrong, **Copy Report** copies the scan results and log for a bug report.

If Windows SmartScreen shows a warning, click **More info**, then **Run anyway**.

Backups are saved to `%LocalAppData%\DeadlockerModFixer\Backups\<timestamp>\`. To undo a fix, click
**Restore Backup…** and pick one. The PowerShell script can also restore one:

```powershell
.\FixMods.ps1 -Restore "<backup folder>"
```

## Building

Run `GUI\build.bat`. It needs `image.png` in the repo root, which is embedded as the logo. vpkmerge is not
embedded.

For the optional offline zip, put the official
[vpkmerge.exe](https://github.com/Slush97/vpkmerge/releases/tag/v0.22.1) in the repo root and run
`GUI\package.bat`. It checks vpkmerge's hash, builds the zip, and prints the SHA-256 hashes for the release notes.

## Credits

The scanning and repair are done by [vpkmerge](https://github.com/Slush97/vpkmerge) by
[Slush97](https://github.com/Slush97). This tool runs the official, unmodified vpkmerge v0.22.1 behind a GUI that adds
verification, backups and installation.
