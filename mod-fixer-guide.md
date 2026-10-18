# Fixing Broken Mods After the Violet Update

Valve's Violet update changed how camera attachments work in Deadlock, and it broke the majority of
hero skin mods. Modders need to manually update every affected mod — until they do, you may see
your hero model snap to a broken camera angle or appear incorrectly in game.

**The good news:** you don't have to wait. The Deadlocker Mod Fixer can repair affected mods on
your machine in seconds, without touching the originals until you say so.

---

## Checking Which of Your Mods Are Affected

Before running anything, you can get a quick picture of which mods are likely broken using the
[recently updated filter](https://deadlocker.net) on deadlocker.net. Mods that were updated
**after** the Violet update are likely already fixed by their author. Mods that haven't been
touched in a while are the ones most likely to need attention.

That said, the only way to know for certain is to run a scan — the tool will tell you exactly
which mods have the problem and which are fine.

---

## Your Two Options

**Option 1 — Wait for the author to fix it.**
If a mod you use is popular, the author will probably push an update. Keep an eye on the mod page
and check the recently updated filter on deadlocker.net. When a new version appears, re-download
and reinstall it as normal. No tool needed.

**Option 2 — Fix it yourself right now.**
Use the Deadlocker Mod Fixer. It scans your addons folder, identifies broken mods, repairs them,
and backs up your originals before touching anything. If something goes wrong, you can restore with
one click.

> **Mod Manager compatibility:** The Mod Fixer has not been tested alongside the Deadlock Mod
> Manager. If you manage your mods through the Mod Manager, proceed with caution — we recommend
> making sure the Mod Manager is closed before running a fix, and being aware that a later
> re-install through the Mod Manager may overwrite the fixed file.

---

## Download

[Download DeadlockModFixer.exe](https://deadlocker.net) from deadlocker.net. It's a single file —
no installer, nothing else to download.

> **Windows SmartScreen warning:** Because the tool isn't code-signed, Windows may show a
> "Windows protected your PC" dialog the first time you open it. Click **More info**, then
> **Run anyway**. The tool is open source and doesn't touch anything outside your Deadlock
> addons folder.

---

## Step 1 — Open the tool

Double-click **DeadlockModFixer.exe**. The tool will automatically find your Deadlock addons
folder by reading your Steam installation from the registry. You should see the path filled in
immediately.

If the path isn't detected (for example, if Deadlock is installed on a secondary drive), click
**Browse…** and navigate to:

```
<Steam library>\steamapps\common\Deadlock\game\citadel\addons
```

---

## Step 2 — Scan your mods

Click **Scan Mods**. The tool checks every mod in your addons folder and colour-codes the results:

| Colour | Meaning |
|--------|---------|
| Green  | Current — this mod is fine, no action needed |
| Yellow | Stale — this mod has broken camera attachments and needs fixing |
| Grey   | Skipped — this mod uses chunk files and can't be auto-fixed (rare) |
| Red    | Error — something unexpected happened, check the log panel |

The scan is read-only. Nothing is changed at this stage.

---

## Step 3 — Fix stale mods

If any mods show as **Stale**, the **Fix All Stale Mods** button will become available. Click it.

**Before anything is replaced, the tool will:**

1. Warn you if Deadlock or the Mod Manager is currently running (close them first).
2. Ask you to confirm.
3. Repair each stale mod and verify the fix before touching the original.
4. Back up each original file to `%LocalAppData%\DeadlockerModFixer\Backups\<timestamp>\`.
5. Only then copy the fixed file into your addons folder.

Mods that fail verification are left completely untouched — the tool never replaces a file it
isn't confident about.

---

## Restoring a backup

If anything looks wrong after fixing, click **Scan Mods** again to check the current state, or
restore your originals from the backup folder. The tool tells you the exact backup path after
every fix run.

For the PowerShell-savvy: the included `FixMods.ps1` script also supports a `-Restore` flag:

```powershell
.\FixMods.ps1 -Restore "C:\Users\<you>\AppData\Local\DeadlockerModFixer\Backups\2026-10-17_2010"
```

---

## Frequently asked questions

**Will this get me banned?**
No. Installing mods in Deadlock is client-side only and VAC does not scan for them. See the
[install guide](https://deadlocker.net/install-guide) for more detail.

**Do I need to re-run the tool when I install a new mod?**
Yes. The tool only fixes whatever is in your addons folder at the time you run it. If you install
a new mod later and it's also broken, open the tool and scan again.

**The author pushed an update — should I reinstall their mod?**
Yes, and you won't need to fix it — author-released updates are already corrected. Just reinstall
as normal.

**A mod shows as Skipped. What does that mean?**
A small number of mods are distributed as multi-part VPK packages (chunk files). The fixer can't
safely repair these automatically. You'll need to wait for the author to release an update for those.

**The tool fixed a mod but it still looks wrong in game.**
Restart Deadlock fully (not just a map change) after running the fixer. If it still looks wrong,
the mod may have other issues unrelated to the camera change — check the mod page for known
problems or report it on the deadlocker.net Discord.

---

## Credits

The actual scanning and repair work is done by **[vpkmerge](https://github.com/Slush97/vpkmerge)**
by [Slush97](https://github.com/Slush97). The Mod Fixer is a wrapper around it: it runs vpkmerge's
`attachments check` and `attachments heal` commands for you, then verifies, backs up and
installs the result. The Mod Fixer currently bundles vpkmerge
[v0.22.1](https://github.com/Slush97/vpkmerge/releases/tag/v0.22.1). All credit for the VPK
parsing and attachment-healing logic goes to its author.

---

*Deadlocker is a community project dedicated to documenting and showcasing all available skins and
community-created mods for Deadlock. — [deadlocker.net](https://deadlocker.net)*
