#Requires -Version 5.1
<#
.SYNOPSIS
    Finds and fixes Deadlock mods broken by the camera attachment change.

.DESCRIPTION
    Scans the addons folder for pak<digits>_dir.vpk mods and checks each one
    for stale model attachments using vpkmerge v0.22.1.

    Default (no flags)  : Dry run -- checks only, nothing is written.
    -Apply              : Heals stale mods, backs up originals, replaces them.
    -Restore <folder>   : Copies a previous backup back into the addons folder.

.EXAMPLE
    .\FixMods.ps1
    .\FixMods.ps1 -Apply
    .\FixMods.ps1 -Restore Backups\2026-10-17_2010
#>

param(
    [switch]$Apply,
    [string]$Restore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'   # don't abort on non-fatal errors

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
$ScriptDir   = $PSScriptRoot
$VpkMerge    = Join-Path $ScriptDir "vpkmerge.exe"
$BasePak     = "C:\Program Files (x86)\Steam\steamapps\common\Deadlock\game\citadel\pak01_dir.vpk"
$AddonsDir   = "C:\Program Files (x86)\Steam\steamapps\common\Deadlock\game\citadel\addons"
$StagingDir  = Join-Path $ScriptDir "Staging"
$BackupsRoot = Join-Path $ScriptDir "Backups"
$Timestamp   = Get-Date -Format "yyyy-MM-dd_HHmm"
$LogFile     = Join-Path $ScriptDir "FixMods_$Timestamp.log"

# ---------------------------------------------------------------------------
# Logging helpers
#
# We keep one StreamWriter open for the entire run so that repeated
# Add-Content calls can't race with each other (antivirus or Windows briefly
# acquiring a share lock between open/close cycles).
# ---------------------------------------------------------------------------
$script:LogWriter = [System.IO.StreamWriter]::new(
    $LogFile,
    $false,   # don't append -- this is a fresh log
    [System.Text.Encoding]::UTF8
)
$script:LogWriter.AutoFlush = $true

# Close the log writer on any exit path (including early exit calls)
Register-EngineEvent -SourceIdentifier PowerShell.Exiting -Action {
    if ($script:LogWriter) { $script:LogWriter.Close() }
} | Out-Null

function Write-Log {
    param(
        [string]$Message,
        [string]$Color = 'White'
    )
    $line = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message
    $script:LogWriter.WriteLine($line)
    Write-Host $Message -ForegroundColor $Color
}

function Write-LogSep {
    param([string]$Title)
    $sep = "-" * 65
    Write-Log ""
    Write-Log $sep 'DarkGray'
    Write-Log "  $Title"
    Write-Log $sep 'DarkGray'
}

# Write a line without a timestamp prefix (used for the log header)
function Write-LogRaw {
    param([string]$Line)
    $script:LogWriter.WriteLine($Line)
}

# ---------------------------------------------------------------------------
# Run vpkmerge, log every line, return output + exit code
# ---------------------------------------------------------------------------
function Invoke-VpkMerge {
    param([string[]]$CmdArgs)

    # Build a readable command string for the log (quote args that have spaces)
    $quoted  = $CmdArgs | ForEach-Object {
        if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
    }
    $cmdLine = "vpkmerge.exe " + ($quoted -join ' ')

    $script:LogWriter.WriteLine("")
    $script:LogWriter.WriteLine("    CMD: $cmdLine")

    # Execute -- pass as an array so PowerShell never misquotes paths with spaces
    $output = & $VpkMerge @CmdArgs 2>&1
    $exit   = $LASTEXITCODE

    foreach ($line in $output) {
        $script:LogWriter.WriteLine("         $line")
    }
    $script:LogWriter.WriteLine("    Exit: $exit")
    $script:LogWriter.WriteLine("")

    return [PSCustomObject]@{
        Lines    = [string[]]$output
        ExitCode = $exit
    }
}

# ---------------------------------------------------------------------------
# Parse "N of M mods ship stale model attachments" from check output
# ---------------------------------------------------------------------------
function Read-CheckResult {
    param([string[]]$Lines)

    # Summary line examples:
    #   "0 of 1 mods ship stale model attachments"
    #   "1 of 1 mod ships stale model attachments"
    $summary = $Lines |
        Where-Object { $_ -match '(\d+) of \d+ mods? ship' } |
        Select-Object -First 1

    if (-not $summary) {
        return [PSCustomObject]@{
            ParseOk     = $false
            IsStale     = $false
            StaleModels = @()
        }
    }

    $null = $summary -match '(\d+) of \d+ mods? ship'
    $staleCount = [int]$Matches[1]

    # Stale model lines:  "  models/heroes/...  stale: attach_hitbox, ..."
    $staleModels = @(
        $Lines | ForEach-Object {
            if ($_ -match '^\s+(\S+)\s+stale:') { $Matches[1] }
        }
    )

    return [PSCustomObject]@{
        ParseOk     = $true
        IsStale     = ($staleCount -gt 0)
        StaleModels = $staleModels
    }
}

# ---------------------------------------------------------------------------
# Check for running game / mod-manager processes
# ---------------------------------------------------------------------------
function Get-RunningGameProcesses {
    $names = @('project8', 'DeadlockModManager', 'deadlock_mod_manager', 'CitadelModManager')
    return @($names | Where-Object { Get-Process -Name $_ -ErrorAction SilentlyContinue })
}

function Confirm-SafeToProceed {
    $running = Get-RunningGameProcesses
    if ($running.Count -eq 0) { return }

    Write-Host ""
    Write-Host "WARNING: These processes are currently running:" -ForegroundColor Yellow
    $running | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    Write-Host "Replacing VPK files while Deadlock is open can cause crashes." -ForegroundColor Yellow
    Write-Host ""
    $answer = Read-Host "Type YES to continue anyway, anything else to abort"
    if ($answer -ceq 'YES') {
        Write-Log "User confirmed to proceed despite running processes." 'Yellow'
    } else {
        Write-Log "Aborted by user (game/mod manager running)." 'Red'
        exit 1
    }
}

# ===========================================================================
# START
# ===========================================================================

$modeLabel = if ($Apply) { 'Apply' } `
             elseif ($Restore) { "Restore: $Restore" } `
             else { 'DryRun' }

Write-LogRaw "FixMods.ps1  $(Get-Date)"
Write-LogRaw "Mode    : $modeLabel"
Write-LogRaw "Base pak: $BasePak"
Write-LogRaw "Addons  : $AddonsDir"
Write-LogRaw ""

Write-Host ""
Write-Host "  Deadlock Mod Attachment Fixer" -ForegroundColor Cyan
Write-Host "  Mode: $modeLabel" -ForegroundColor Cyan
Write-Host ""

# -- Prereqs -----------------------------------------------------------------
if (-not (Test-Path -LiteralPath $VpkMerge)) {
    Write-Log "ERROR: vpkmerge.exe not found at: $VpkMerge" 'Red'
    Write-Log "Download it with:" 'Yellow'
    Write-Log "  curl -L -o vpkmerge.exe https://github.com/Slush97/vpkmerge/releases/download/v0.22.1/vpkmerge-windows-x86_64.exe" 'Yellow'
    exit 1
}
if (-not (Test-Path -LiteralPath $BasePak)) {
    Write-Log "ERROR: Base pak not found: $BasePak" 'Red'
    exit 1
}
if (-not (Test-Path -LiteralPath $AddonsDir)) {
    Write-Log "ERROR: Addons folder not found: $AddonsDir" 'Red'
    exit 1
}

# ===========================================================================
# RESTORE MODE
# ===========================================================================
if ($Restore) {
    Write-LogSep "RESTORE MODE"

    # Accept paths relative to the script folder
    if (-not [System.IO.Path]::IsPathRooted($Restore)) {
        $Restore = Join-Path $ScriptDir $Restore
    }

    if (-not (Test-Path -LiteralPath $Restore)) {
        Write-Log "ERROR: Backup folder not found: $Restore" 'Red'
        exit 1
    }

    Confirm-SafeToProceed

    $backups = @(Get-ChildItem -LiteralPath $Restore -Filter "pak*_dir.vpk" -File)
    if ($backups.Count -eq 0) {
        Write-Log "No pak*_dir.vpk files found in: $Restore" 'Yellow'
        exit 0
    }

    $ok = 0; $fail = 0
    foreach ($bf in $backups) {
        $dest = Join-Path $AddonsDir $bf.Name
        Write-Log "Restoring $($bf.Name) ..." 'Cyan'
        try {
            Copy-Item -LiteralPath $bf.FullName -Destination $dest -Force
            Write-Log "  OK -> $dest" 'Green'
            $ok++
        } catch {
            Write-Log "  ERROR: $_" 'Red'
            $fail++
        }
    }

    Write-Log ""
    $restoreColor = if ($fail -gt 0) { 'Yellow' } else { 'Green' }
    Write-Log "Restore complete: $ok restored, $fail failed." $restoreColor
    Write-Log "Log: $LogFile" 'DarkGray'
    exit 0
}

# ===========================================================================
# SCAN
# ===========================================================================
Write-LogSep "SCANNING ADDONS"

# Non-recursive -- addons is flat (replays/ is excluded by -File)
$allFiles = @(Get-ChildItem -LiteralPath $AddonsDir -File)

# Mods: exactly pak<digits>_dir.vpk
$modFiles = @($allFiles | Where-Object { $_.Name -match '^pak(\d+)_dir\.vpk$' })

# Chunk files: pak<digits>_<3digits>.vpk  e.g. pak12_000.vpk
$chunkFiles = @($allFiles | Where-Object { $_.Name -match '^pak(\d+)_\d{3}\.vpk$' })

Write-Log "Mod pak files : $($modFiles.Count)"
Write-Log "Chunk files   : $($chunkFiles.Count)"

# Set of pak-numbers that have chunk siblings
$chunkedNums = @{}
foreach ($cf in $chunkFiles) {
    if ($cf.Name -match '^pak(\d+)_\d{3}\.vpk$') {
        $chunkedNums[$Matches[1]] = $true
    }
}

# Game-process guard (before any writes)
if ($Apply) {
    Confirm-SafeToProceed
}

# Staging dir must exist before healing
if (-not (Test-Path -LiteralPath $StagingDir)) {
    New-Item -ItemType Directory -Path $StagingDir | Out-Null
}

# ===========================================================================
# PROCESS EACH MOD
# ===========================================================================
Write-LogSep "CHECKING MODS"

$results = [System.Collections.Generic.List[PSCustomObject]]::new()

foreach ($mod in $modFiles) {

    $null = $mod.Name -match '^pak(\d+)_dir\.vpk$'
    $pakNum = $Matches[1]

    Write-Log ""
    Write-Log "-- $($mod.Name)" 'Cyan'

    # Skip mods with chunk siblings
    if ($chunkedNums.ContainsKey($pakNum)) {
        $reason = "has sibling chunk files (pak${pakNum}_000.vpk etc.)"
        Write-Log "  SKIPPED: $reason" 'Yellow'
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Skipped'
            Reason      = $reason
            StaleModels = @()
        })
        continue
    }

    # -- attachments check ---------------------------------------------------
    $checkArgs = @('attachments', 'check', '--base', $BasePak, $mod.FullName)
    $check = $null
    try {
        $check = Invoke-VpkMerge -CmdArgs $checkArgs
    } catch {
        Write-Log "  ERROR (check threw): $_" 'Red'
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Error'
            Reason      = "check threw: $_"
            StaleModels = @()
        })
        continue
    }

    if ($check.ExitCode -ne 0) {
        Write-Log "  ERROR: check exited $($check.ExitCode)" 'Red'
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Error'
            Reason      = "check exit code $($check.ExitCode)"
            StaleModels = @()
        })
        continue
    }

    $parsed = Read-CheckResult -Lines $check.Lines
    if (-not $parsed.ParseOk) {
        Write-Log "  ERROR: could not parse check output:" 'Red'
        $check.Lines | ForEach-Object { Write-Log "    $_" 'DarkGray' }
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Error'
            Reason      = 'could not parse check output'
            StaleModels = @()
        })
        continue
    }

    # -- Not stale -----------------------------------------------------------
    if (-not $parsed.IsStale) {
        Write-Log "  Current / not affected." 'Green'
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Current'
            Reason      = ''
            StaleModels = @()
        })
        continue
    }

    # -- Stale ---------------------------------------------------------------
    Write-Log "  STALE: $($parsed.StaleModels.Count) model(s)" 'Yellow'
    $parsed.StaleModels | ForEach-Object { Write-Log "    - $_" 'Yellow' }

    # Dry run: report and stop here
    if (-not $Apply) {
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Stale'
            Reason      = ''
            StaleModels = $parsed.StaleModels
        })
        continue
    }

    # -- attachments heal ----------------------------------------------------
    # Output goes to Staging -- never to addons, never the same path as input
    $fixedPath = Join-Path $StagingDir "pak${pakNum}_fixed_dir.vpk"
    if (Test-Path -LiteralPath $fixedPath) { Remove-Item -LiteralPath $fixedPath -Force }

    $healArgs = @('attachments', 'heal', '--base', $BasePak, $mod.FullName, '--output', $fixedPath)
    $heal = $null
    try {
        $heal = Invoke-VpkMerge -CmdArgs $healArgs
    } catch {
        Write-Log "  ERROR (heal threw): $_" 'Red'
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Error'
            Reason      = "heal threw: $_"
            StaleModels = $parsed.StaleModels
        })
        continue
    }

    if ($heal.ExitCode -ne 0) {
        Write-Log "  ERROR: heal exited $($heal.ExitCode)" 'Red'
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Error'
            Reason      = "heal exit code $($heal.ExitCode)"
            StaleModels = $parsed.StaleModels
        })
        continue
    }

    if (-not (Test-Path -LiteralPath $fixedPath)) {
        Write-Log "  ERROR: heal ran but produced no output file" 'Red'
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Error'
            Reason      = 'heal produced no output file'
            StaleModels = $parsed.StaleModels
        })
        continue
    }

    # -- Verify the healed file is clean ------------------------------------
    Write-Log "  Verifying healed file..." 'DarkGray'
    $verifyArgs = @('attachments', 'check', '--base', $BasePak, $fixedPath)
    $verify = $null
    try {
        $verify = Invoke-VpkMerge -CmdArgs $verifyArgs
    } catch {
        Write-Log "  ERROR (verify threw): $_" 'Red'
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Error'
            Reason      = "verify threw: $_"
            StaleModels = $parsed.StaleModels
        })
        continue
    }

    $vParsed = Read-CheckResult -Lines $verify.Lines
    if ($vParsed.IsStale) {
        Write-Log "  ERROR: healed file still stale -- NOT replacing original." 'Red'
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Error'
            Reason      = 'healed file still stale after verify'
            StaleModels = $parsed.StaleModels
        })
        continue
    }

    Write-Log "  Healed file verified clean." 'Green'

    # -- Back up original ----------------------------------------------------
    $backupDir = Join-Path $BackupsRoot $Timestamp
    if (-not (Test-Path -LiteralPath $backupDir)) {
        New-Item -ItemType Directory -Path $backupDir | Out-Null
    }
    $backupDest = Join-Path $backupDir $mod.Name

    try {
        Copy-Item -LiteralPath $mod.FullName -Destination $backupDest -Force
        Write-Log "  Backed up -> $backupDest" 'DarkGray'
    } catch {
        Write-Log "  ERROR: backup failed: $_ -- skipping replacement." 'Red'
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Error'
            Reason      = "backup failed: $_"
            StaleModels = $parsed.StaleModels
        })
        continue
    }

    # -- Replace original with healed file -----------------------------------
    try {
        Copy-Item -LiteralPath $fixedPath -Destination $mod.FullName -Force
        Write-Log "  FIXED: $($mod.Name) replaced with healed file." 'Green'
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Fixed'
            Reason      = ''
            StaleModels = $parsed.StaleModels
        })
    } catch {
        Write-Log "  ERROR: replace failed: $_" 'Red'
        Write-Log "  Attempting to restore backup..." 'Yellow'
        try { Copy-Item -LiteralPath $backupDest -Destination $mod.FullName -Force } catch {}
        $results.Add([PSCustomObject]@{
            Mod         = $mod.Name
            Status      = 'Error'
            Reason      = "replace failed: $_"
            StaleModels = $parsed.StaleModels
        })
    }
}

# ===========================================================================
# SUMMARY TABLE
# ===========================================================================
Write-LogSep "SUMMARY"

$colW   = 36
$header = ("{0,-$colW}  {1}" -f "MOD FILE", "STATUS")
$div    = "-" * ($colW + 40)

Write-Log $header
Write-Log $div

foreach ($r in $results) {
    $statusColor = switch ($r.Status) {
        'Current' { 'Green' }
        'Stale'   { 'Yellow' }
        'Fixed'   { 'Cyan' }
        'Skipped' { 'DarkYellow' }
        'Error'   { 'Red' }
        default   { 'White' }
    }
    $detail = switch ($r.Status) {
        'Stale'   { " ($($r.StaleModels.Count) stale model(s))" }
        'Fixed'   { " ($($r.StaleModels.Count) model(s) patched)" }
        'Skipped' { " -- $($r.Reason)" }
        'Error'   { " -- $($r.Reason)" }
        default   { "" }
    }
    $modName = $r.Mod
    if ($modName.Length -gt $colW) { $modName = $modName.Substring(0, $colW - 1) + "~" }

    Write-Log ("{0,-$colW}  {1}" -f $modName, ($r.Status + $detail)) $statusColor

    # List stale/patched models indented beneath the mod
    if ($r.StaleModels.Count -gt 0 -and $r.Status -in @('Stale', 'Fixed')) {
        $r.StaleModels | ForEach-Object {
            Write-Log ("  " + (" " * $colW) + "  -> $_") 'DarkGray'
        }
    }
}

# Counts
$nCurrent = @($results | Where-Object Status -eq 'Current').Count
$nStale   = @($results | Where-Object Status -eq 'Stale').Count
$nFixed   = @($results | Where-Object Status -eq 'Fixed').Count
$nSkipped = @($results | Where-Object Status -eq 'Skipped').Count
$nError   = @($results | Where-Object Status -eq 'Error').Count

Write-Log ""
Write-Log "--- Totals -------------------------------------------------------" 'DarkGray'
Write-Log ("  Current (no action needed) : {0,3}" -f $nCurrent) 'Green'
if ($Apply) {
    Write-Log ("  Fixed   (healed+replaced)  : {0,3}" -f $nFixed) 'Cyan'
} else {
    Write-Log ("  Stale   (need -Apply)      : {0,3}" -f $nStale) 'Yellow'
}
Write-Log ("  Skipped (has chunk files)  : {0,3}" -f $nSkipped) 'DarkYellow'
$errColor = if ($nError -gt 0) { 'Red' } else { 'White' }
Write-Log ("  Errors                     : {0,3}" -f $nError) $errColor
Write-Log ""

if (-not $Apply -and $nStale -gt 0) {
    Write-Log "  Run FixMods-Apply.bat (or .\FixMods.ps1 -Apply) to heal stale mods." 'Yellow'
}
if ($Apply -and $nFixed -gt 0) {
    $backupDirFinal = Join-Path $BackupsRoot $Timestamp
    Write-Log "  Originals backed up in : $backupDirFinal" 'DarkGray'
    Write-Log "  To restore             : .\FixMods.ps1 -Restore `"$backupDirFinal`"" 'DarkGray'
}

Write-Log ""
Write-Log "Log written to: $LogFile" 'DarkGray'

# Close the log file handle cleanly
$script:LogWriter.Close()

Write-Host ""
