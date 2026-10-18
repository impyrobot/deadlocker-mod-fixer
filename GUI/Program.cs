// Deadlocker Mod Fixer - GUI tool for fixing Deadlock mods broken by the camera attachment change.
// Compiles against .NET Framework 4.0 reference assemblies; runs on .NET Framework 4.8 (Windows 10+).
// Build: see build.bat in this folder.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Deadlocker Mod Fixer")]
[assembly: AssemblyProduct("Deadlocker Mod Fixer")]
[assembly: AssemblyVersion("1.0.0.0")]

// ---------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------
enum ModStatus { Pending, Scanning, Current, Stale, Fixed, Skipped, Error }

class ModEntry
{
    public string FilePath;
    public string FileName;
    public ModStatus Status = ModStatus.Pending;
    public string[] StaleModels = new string[0];
    public string StatusDetail = "";

    public ModEntry(string path)
    {
        FilePath = path;
        FileName = Path.GetFileName(path);
    }
}

// ---------------------------------------------------------------------------
// vpkmerge runner - locates/extracts vpkmerge.exe and runs commands
// ---------------------------------------------------------------------------
static class VpkMerge
{
    private static string _exePath;

    public static string ExePath
    {
        get
        {
            if (_exePath != null) return _exePath;
            _exePath = FindOrExtract();
            return _exePath;
        }
    }

    private static string FindOrExtract()
    {
        // 1. Look next to the running .exe first (dev convenience)
        var nextTo = Path.Combine(
            Path.GetDirectoryName(Application.ExecutablePath), "vpkmerge.exe");
        if (File.Exists(nextTo)) return nextTo;

        // 2. Extract embedded resource to %LocalAppData%
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeadlockerModFixer");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "vpkmerge.exe");

        using (var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Resources.vpkmerge.exe"))
        {
            if (stream == null)
                throw new InvalidOperationException(
                    "vpkmerge.exe not found next to the application and not embedded. " +
                    "Place vpkmerge.exe in the same folder as DeadlockModFixer.exe.");
            using (var fs = File.Create(dest))
                stream.CopyTo(fs);
        }
        return dest;
    }

    // Run vpkmerge with the given arguments (each passed as a separate element,
    // so paths with spaces are always quoted correctly).
    public static RunResult Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            Arguments = BuildArgs(args),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var sb = new StringBuilder();
        int exit;
        using (var proc = Process.Start(psi))
        {
            // Read stdout and stderr together so neither buffer blocks
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();
            exit = proc.ExitCode;
        }

        var lines = sb.ToString()
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(l => l.TrimEnd())
            .ToArray();

        return new RunResult { Lines = lines, ExitCode = exit };
    }

    // Quote each argument so paths with spaces/parentheses survive the shell
    private static string BuildArgs(string[] args)
    {
        var parts = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            // Wrap in double-quotes if it contains spaces or special chars
            if (a.IndexOfAny(new[] { ' ', '(', ')' }) >= 0)
                a = "\"" + a.Replace("\"", "\\\"") + "\"";
            parts[i] = a;
        }
        return string.Join(" ", parts);
    }

    // Parse "N of M mods ship stale model attachments" from check output
    public static CheckResult ParseCheck(string[] lines)
    {
        var summary = lines.FirstOrDefault(
            l => Regex.IsMatch(l, @"\d+ of \d+ mods? ship"));
        if (summary == null)
            return new CheckResult { ParseOk = false };

        var m = Regex.Match(summary, @"(\d+) of \d+ mods? ship");
        var stale = int.Parse(m.Groups[1].Value);

        var models = lines
            .Where(l => Regex.IsMatch(l, @"^\s+\S+\s+stale:"))
            .Select(l => Regex.Match(l, @"^\s+(\S+)\s+stale:").Groups[1].Value)
            .ToArray();

        return new CheckResult { ParseOk = true, IsStale = stale > 0, StaleModels = models };
    }
}

class RunResult
{
    public string[] Lines;
    public int ExitCode;
}

class CheckResult
{
    public bool ParseOk;
    public bool IsStale;
    public string[] StaleModels = new string[0];
}

// ---------------------------------------------------------------------------
// Steam path detection
// ---------------------------------------------------------------------------
static class SteamFinder
{
    public static string FindAddonsFolder()
    {
        var steam = GetSteamPath();
        if (steam == null) return null;
        steam = steam.Replace('/', '\\');

        // Check default library then any extra libraries
        var roots = new List<string> { Path.Combine(steam, "steamapps") };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (var line in File.ReadAllLines(vdf))
            {
                var mv = Regex.Match(line, @"""path""\s+""(.+?)""");
                if (mv.Success)
                {
                    var lib = mv.Groups[1].Value.Replace(@"\\", @"\");
                    roots.Add(Path.Combine(lib, "steamapps"));
                }
            }
        }

        foreach (var root in roots)
        {
            var candidate = Path.Combine(root, "common", "Deadlock",
                                         "game", "citadel", "addons");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string GetSteamPath()
    {
        // Try HKCU first, then HKLM
        return
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam",  "SteamPath", null) as string ??
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string ??
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
    }
}

// ---------------------------------------------------------------------------
// Main form
// ---------------------------------------------------------------------------
class MainForm : Form
{
    // Base pak is always next to the addons folder
    private string BasePak => Path.Combine(
        Path.GetDirectoryName(_addonsBox.Text.TrimEnd('\\', '/')),
        "pak01_dir.vpk");

    private string StagingDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeadlockerModFixer", "Staging");

    private string BackupsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeadlockerModFixer", "Backups");

    // Controls
    private TextBox    _addonsBox;
    private Button     _browseBtn;
    private Button     _scanBtn;
    private Button     _fixBtn;
    private Label      _summaryLbl;
    private ProgressBar _progress;
    private Label      _progressLbl;
    private DataGridView _grid;
    private RichTextBox  _log;
    private LinkLabel    _siteLink;

    // State
    private List<ModEntry> _mods = new List<ModEntry>();
    private BackgroundWorker _worker;
    private string _backupTimestamp;

    // Grid column indices
    private const int ColMod    = 0;
    private const int ColStatus = 1;
    private const int ColModels = 2;

    public MainForm()
    {
        BuildUI();
        AutoDetectAddons();
    }

    // -----------------------------------------------------------------------
    // UI construction (no designer file needed)
    // -----------------------------------------------------------------------
    private void BuildUI()
    {
        Text            = "Deadlocker Mod Fixer";
        MinimumSize     = new Size(760, 580);
        Size            = new Size(820, 660);
        StartPosition   = FormStartPosition.CenterScreen;
        Font            = new Font("Segoe UI", 9f);
        BackColor       = Color.FromArgb(240, 240, 242);

        // Try to set the window icon from the embedded logo
        try
        {
            using (var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Resources.logo.png"))
            using (var bmp = new Bitmap(stream))
                Icon = Icon.FromHandle(bmp.GetHicon());
        }
        catch { /* icon is cosmetic, ignore */ }

        // ── Header ──────────────────────────────────────────────────────────
        var header = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 64,
            BackColor = Color.FromArgb(26, 26, 26)
        };

        var logo = new PictureBox
        {
            Width = 40, Height = 40,
            Top   = 12, Left  = 16,
            SizeMode   = PictureBoxSizeMode.Zoom,
            BackColor  = Color.Transparent
        };
        try
        {
            using (var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Resources.logo.png"))
                logo.Image = new Bitmap(stream);
        }
        catch { }

        var titleLbl = new Label
        {
            Text      = "Deadlocker Mod Fixer",
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 14f, FontStyle.Bold),
            AutoSize  = true,
            Top       = 18, Left = 66,
            BackColor = Color.Transparent
        };

        var subLbl = new Label
        {
            Text      = "Fixes mods broken by the Deadlock camera attachment change",
            ForeColor = Color.FromArgb(170, 170, 170),
            Font      = new Font("Segoe UI", 8f),
            AutoSize  = true,
            Top       = 44, Left = 67,
            BackColor = Color.Transparent
        };

        _siteLink = new LinkLabel
        {
            Text      = "deadlocker.net",
            Font      = new Font("Segoe UI", 9f),
            AutoSize  = true,
            BackColor = Color.Transparent,
            LinkColor = Color.FromArgb(100, 180, 255),
            ActiveLinkColor = Color.White
        };
        _siteLink.LinkClicked += (_, __) =>
            Process.Start(new ProcessStartInfo("https://deadlocker.net") { UseShellExecute = true });

        // Position site link on the right at runtime (needs form width)
        Shown += (_, __) =>
        {
            _siteLink.Left = header.Width - _siteLink.Width - 16;
            _siteLink.Top  = (header.Height - _siteLink.Height) / 2;
        };
        header.Resize += (_, __) =>
        {
            _siteLink.Left = header.Width - _siteLink.Width - 16;
        };

        header.Controls.AddRange(new Control[] { logo, titleLbl, subLbl, _siteLink });

        // ── Path row ────────────────────────────────────────────────────────
        var pathPanel = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 44,
            Padding   = new Padding(12, 8, 12, 0),
            BackColor = Color.FromArgb(248, 248, 250)
        };

        var pathLbl = new Label
        {
            Text     = "Addons folder:",
            AutoSize = true,
            Top = 14, Left = 0,
            ForeColor = Color.FromArgb(60, 60, 60)
        };

        _addonsBox = new TextBox
        {
            Top    = 10,
            Left   = 100,
            Height = 22,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };

        _browseBtn = new Button
        {
            Text   = "Browse…",
            Width  = 72,
            Height = 24,
            Top    = 9,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            FlatStyle = FlatStyle.System
        };
        _browseBtn.Click += BrowseClick;

        pathPanel.Controls.AddRange(new Control[] { pathLbl, _addonsBox, _browseBtn });
        pathPanel.Resize += (_, __) =>
        {
            _addonsBox.Width = pathPanel.ClientSize.Width - 100 - 80 - 12;
            _browseBtn.Left  = pathPanel.ClientSize.Width - 80 - 12;
        };

        // ── Action row ──────────────────────────────────────────────────────
        var actionPanel = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 44,
            Padding   = new Padding(12, 8, 12, 0),
            BackColor = Color.FromArgb(248, 248, 250)
        };

        _scanBtn = new Button
        {
            Text      = "Scan Mods",
            Width     = 100,
            Height    = 26,
            Top       = 8,
            Left      = 0,
            FlatStyle = FlatStyle.System,
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        _scanBtn.Click += ScanClick;

        _fixBtn = new Button
        {
            Text      = "Fix All Stale Mods",
            Width     = 140,
            Height    = 26,
            Top       = 8,
            Left      = 110,
            Enabled   = false,
            FlatStyle = FlatStyle.System,
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            BackColor = Color.FromArgb(210, 235, 255)
        };
        _fixBtn.Click += FixClick;

        _summaryLbl = new Label
        {
            Text      = "",
            AutoSize  = true,
            Top       = 14,
            Left      = 264,
            ForeColor = Color.FromArgb(80, 80, 80)
        };

        actionPanel.Controls.AddRange(new Control[] { _scanBtn, _fixBtn, _summaryLbl });

        // ── Progress row (hidden until running) ─────────────────────────────
        var progPanel = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 26,
            Padding   = new Padding(12, 4, 12, 0),
            Visible   = false
        };
        _progress    = new ProgressBar { Top = 4, Height = 16, Style = ProgressBarStyle.Continuous };
        _progressLbl = new Label { Top = 4, AutoSize = true, ForeColor = Color.FromArgb(60, 60, 60) };
        progPanel.Controls.AddRange(new Control[] { _progress, _progressLbl });
        progPanel.Resize += (_, __) =>
        {
            _progress.Left  = 0;
            _progress.Width = progPanel.ClientSize.Width - _progressLbl.Width - 12;
            _progressLbl.Left = _progress.Width + 8;
        };

        // ── Log area (bottom) ───────────────────────────────────────────────
        var logPanel = new Panel { Dock = DockStyle.Bottom, Height = 140 };
        var logLbl   = new Label
        {
            Text = "Log",
            Dock = DockStyle.Top,
            Height = 18,
            Padding = new Padding(4, 2, 0, 0),
            BackColor = Color.FromArgb(220, 220, 225),
            ForeColor = Color.FromArgb(60, 60, 60),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold)
        };
        _log = new RichTextBox
        {
            Dock      = DockStyle.Fill,
            ReadOnly  = true,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(200, 200, 200),
            Font      = new Font("Consolas", 8.5f),
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        logPanel.Controls.AddRange(new Control[] { _log, logLbl });

        // ── Grid (fills remaining space) ────────────────────────────────────
        _grid = new DataGridView
        {
            Dock              = DockStyle.Fill,
            ReadOnly          = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode     = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeRowsMode  = DataGridViewAutoSizeRowsMode.AllCells,
            BorderStyle       = BorderStyle.None,
            BackgroundColor   = Color.White,
            GridColor         = Color.FromArgb(220, 220, 225),
            CellBorderStyle   = DataGridViewCellBorderStyle.SingleHorizontal,
            DefaultCellStyle  = { WrapMode = DataGridViewTriState.True,
                                   Padding  = new Padding(4, 2, 4, 2) }
        };

        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(50, 50, 55);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.Font      = new Font("Segoe UI", 9f, FontStyle.Bold);
        _grid.EnableHeadersVisualStyles = false;

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name      = "Mod",
            HeaderText = "Mod File",
            Width     = 160,
            MinimumWidth = 120,
            DefaultCellStyle = { Font = new Font("Consolas", 8.5f) }
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name       = "Status",
            HeaderText = "Status",
            Width      = 90,
            MinimumWidth = 70
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name       = "Models",
            HeaderText = "Affected Models",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = { Font = new Font("Consolas", 8f),
                                  ForeColor = Color.FromArgb(80, 80, 80),
                                  WrapMode  = DataGridViewTriState.True }
        });

        _grid.CellFormatting += GridCellFormatting;

        // ── Assemble (order matters: Fill goes before Top/Bottom controls) ──
        Controls.Add(_grid);
        Controls.Add(logPanel);
        Controls.Add(progPanel);
        Controls.Add(actionPanel);
        Controls.Add(pathPanel);
        Controls.Add(header);

        // Keep a reference so we can toggle visibility
        Tag = progPanel;   // reused below
    }

    // -----------------------------------------------------------------------
    // Colour rows by status
    // -----------------------------------------------------------------------
    private void GridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _mods.Count) return;
        var status = _mods[e.RowIndex].Status;
        Color bg, fg;
        switch (status)
        {
            case ModStatus.Current:  bg = Color.FromArgb(240, 253, 244); fg = Color.FromArgb(20, 120, 50);  break;
            case ModStatus.Stale:    bg = Color.FromArgb(255, 251, 235); fg = Color.FromArgb(160, 90, 0);   break;
            case ModStatus.Fixed:    bg = Color.FromArgb(235, 248, 255); fg = Color.FromArgb(0, 100, 180);  break;
            case ModStatus.Skipped:  bg = Color.FromArgb(248, 248, 248); fg = Color.FromArgb(120, 120, 120); break;
            case ModStatus.Error:    bg = Color.FromArgb(255, 242, 242); fg = Color.FromArgb(180, 0, 0);    break;
            case ModStatus.Scanning: bg = Color.FromArgb(245, 245, 255); fg = Color.FromArgb(80, 80, 180);  break;
            default:                 bg = Color.White;                   fg = Color.Black;                   break;
        }
        var row = _grid.Rows[e.RowIndex];
        row.DefaultCellStyle.BackColor = bg;
        row.DefaultCellStyle.ForeColor = fg;
        // Keep model column slightly dimmer
        if (e.ColumnIndex == ColModels)
            e.CellStyle.ForeColor = Color.FromArgb(fg.R / 2 + 60, fg.G / 2 + 60, fg.B / 2 + 60);
    }

    // -----------------------------------------------------------------------
    // Auto-detect addons folder on startup
    // -----------------------------------------------------------------------
    private void AutoDetectAddons()
    {
        var found = SteamFinder.FindAddonsFolder();
        if (found != null)
        {
            _addonsBox.Text = found;
            LogLine("Addons folder auto-detected.", Color.FromArgb(100, 200, 100));
        }
        else
        {
            LogLine("Could not auto-detect addons folder. Please browse to it manually.", Color.Orange);
        }
    }

    // -----------------------------------------------------------------------
    // Browse
    // -----------------------------------------------------------------------
    private void BrowseClick(object sender, EventArgs e)
    {
        using (var dlg = new FolderBrowserDialog
        {
            Description = "Select the Deadlock addons folder",
            SelectedPath = _addonsBox.Text
        })
        {
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _addonsBox.Text = dlg.SelectedPath;
        }
    }

    // -----------------------------------------------------------------------
    // SCAN
    // -----------------------------------------------------------------------
    private void ScanClick(object sender, EventArgs e)
    {
        if (!ValidatePaths()) return;

        // Collect mods
        var addons = _addonsBox.Text;
        var allFiles = Directory.GetFiles(addons);
        var modFiles   = allFiles.Where(f => Regex.IsMatch(Path.GetFileName(f), @"^pak\d+_dir\.vpk$")).ToArray();
        var chunkFiles = allFiles.Where(f => Regex.IsMatch(Path.GetFileName(f), @"^pak\d+_\d{3}\.vpk$")).ToArray();

        var chunkedNums = new HashSet<string>(
            chunkFiles.Select(f => Regex.Match(Path.GetFileName(f), @"^pak(\d+)_\d{3}\.vpk$").Groups[1].Value));

        _mods = modFiles.Select(f => new ModEntry(f)).ToList();

        // Populate grid
        _grid.Rows.Clear();
        foreach (var m in _mods)
            _grid.Rows.Add(m.FileName, "Pending", "");

        SetButtonsEnabled(false);
        _fixBtn.Enabled = false;
        _summaryLbl.Text = "";
        ShowProgress(true, _mods.Count);
        _log.Clear();
        LogLine($"Scanning {_mods.Count} mods in {addons}...", Color.White);

        _worker = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = false
        };

        _worker.DoWork += (_, args) =>
        {
            var basePak = (string)((object[])args.Argument)[0];
            var chunkSet = (HashSet<string>)((object[])args.Argument)[1];

            for (int i = 0; i < _mods.Count; i++)
            {
                var mod = _mods[i];
                _worker.ReportProgress(i, new object[] { "scanning", mod });

                // Skip chunked mods
                var nm = Regex.Match(mod.FileName, @"^pak(\d+)_dir\.vpk$").Groups[1].Value;
                if (chunkSet.Contains(nm))
                {
                    mod.Status       = ModStatus.Skipped;
                    mod.StatusDetail = "has sibling chunk files";
                    _worker.ReportProgress(i, new object[] { "done", mod });
                    continue;
                }

                // Run check
                RunResult res;
                try { res = VpkMerge.Run("attachments", "check", "--base", basePak, mod.FilePath); }
                catch (Exception ex)
                {
                    mod.Status       = ModStatus.Error;
                    mod.StatusDetail = ex.Message;
                    _worker.ReportProgress(i, new object[] { "done", mod });
                    continue;
                }

                _worker.ReportProgress(i, new object[] { "log",
                    $"[{mod.FileName}] check exit={res.ExitCode}\n" + string.Join("\n", res.Lines) });

                if (res.ExitCode != 0)
                {
                    mod.Status       = ModStatus.Error;
                    mod.StatusDetail = $"vpkmerge exited {res.ExitCode}";
                    _worker.ReportProgress(i, new object[] { "done", mod });
                    continue;
                }

                var check = VpkMerge.ParseCheck(res.Lines);
                if (!check.ParseOk)
                {
                    mod.Status       = ModStatus.Error;
                    mod.StatusDetail = "could not parse output";
                    _worker.ReportProgress(i, new object[] { "done", mod });
                    continue;
                }

                if (!check.IsStale)
                {
                    mod.Status = ModStatus.Current;
                }
                else
                {
                    mod.Status       = ModStatus.Stale;
                    mod.StaleModels  = check.StaleModels;
                }

                _worker.ReportProgress(i, new object[] { "done", mod });
            }
        };

        _worker.ProgressChanged += (_, args) =>
        {
            var data  = (object[])args.UserState;
            var type  = (string)data[0];
            var idx   = args.ProgressPercentage;

            if (type == "log")
            {
                LogLine((string)data[1], Color.FromArgb(140, 140, 140));
                return;
            }

            var mod = (ModEntry)data[1];
            if (type == "scanning")
            {
                UpdateGridRow(idx, mod.FileName, "Scanning…", "");
                _progressLbl.Text = $"{idx + 1} / {_mods.Count}";
                _progress.Value = idx + 1;
            }
            else // "done"
            {
                string statusText = mod.Status.ToString();
                string models     = string.Join("\n", mod.StaleModels);
                if (!string.IsNullOrEmpty(mod.StatusDetail))
                    statusText += " — " + mod.StatusDetail;
                UpdateGridRow(idx, mod.FileName, statusText, models);
                _grid.InvalidateRow(idx);
            }
        };

        _worker.RunWorkerCompleted += (_, args) =>
        {
            ShowProgress(false, 0);
            SetButtonsEnabled(true);
            UpdateSummary();
            var stale = _mods.Count(m => m.Status == ModStatus.Stale);
            _fixBtn.Enabled = stale > 0;
            LogLine(stale > 0
                ? $"Scan complete. {stale} mod(s) need healing — click \"Fix All Stale Mods\"."
                : "Scan complete. All mods are current.", Color.White);
        };

        _worker.RunWorkerAsync(new object[] { BasePak, chunkedNums });
    }

    // -----------------------------------------------------------------------
    // FIX
    // -----------------------------------------------------------------------
    private void FixClick(object sender, EventArgs e)
    {
        if (!ValidatePaths()) return;

        // Warn if game is running
        var running = new[] { "project8", "DeadlockModManager", "CitadelModManager" }
            .Where(n => Process.GetProcessesByName(n).Length > 0).ToArray();
        if (running.Length > 0)
        {
            var msg = "The following processes are running:\n  " + string.Join(", ", running)
                    + "\n\nReplacing VPK files while Deadlock is open can cause crashes.\n\nContinue anyway?";
            if (MessageBox.Show(this, msg, "Deadlock is running",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
        }

        var confirm = MessageBox.Show(this,
            $"This will heal {_mods.Count(m => m.Status == ModStatus.Stale)} stale mod(s).\n\n" +
            "Originals will be backed up before being replaced.\n\nProceed?",
            "Confirm Fix", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK) return;

        Directory.CreateDirectory(StagingDir);
        _backupTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var backupDir = Path.Combine(BackupsRoot, _backupTimestamp);
        Directory.CreateDirectory(backupDir);

        var staleList = _mods.Where(m => m.Status == ModStatus.Stale).ToList();
        SetButtonsEnabled(false);
        _fixBtn.Enabled = false;
        ShowProgress(true, staleList.Count);
        LogLine($"\nHealing {staleList.Count} stale mod(s)...", Color.White);

        _worker = new BackgroundWorker { WorkerReportsProgress = true };

        _worker.DoWork += (_, args) =>
        {
            var pArgs   = (object[])args.Argument;
            var basePak = (string)pArgs[0];
            var staging = (string)pArgs[1];
            var backup  = (string)pArgs[2];
            var mods    = (List<ModEntry>)pArgs[3];

            for (int i = 0; i < mods.Count; i++)
            {
                var mod     = mods[i];
                var modIdx  = _mods.IndexOf(mod);
                _worker.ReportProgress(i, new object[] { "log", $"\n-- Healing {mod.FileName}" });
                _worker.ReportProgress(i, new object[] { "scanning", modIdx });

                // Extract pak number to name the staged file
                var pakNum    = Regex.Match(mod.FileName, @"^pak(\d+)_dir\.vpk$").Groups[1].Value;
                var fixedPath = Path.Combine(staging, $"pak{pakNum}_fixed_dir.vpk");
                if (File.Exists(fixedPath)) File.Delete(fixedPath);

                // Heal
                RunResult heal;
                try { heal = VpkMerge.Run("attachments", "heal", "--base", basePak, mod.FilePath, "--output", fixedPath); }
                catch (Exception ex)
                {
                    mod.Status = ModStatus.Error; mod.StatusDetail = ex.Message;
                    _worker.ReportProgress(i, new object[] { "done", modIdx, mod });
                    continue;
                }

                _worker.ReportProgress(i, new object[] { "log",
                    string.Join("\n", heal.Lines) + $"\nExit: {heal.ExitCode}" });

                if (heal.ExitCode != 0 || !File.Exists(fixedPath))
                {
                    mod.Status = ModStatus.Error;
                    mod.StatusDetail = heal.ExitCode != 0
                        ? $"heal exited {heal.ExitCode}" : "no output file written";
                    _worker.ReportProgress(i, new object[] { "done", modIdx, mod });
                    continue;
                }

                // Verify
                RunResult verify;
                try { verify = VpkMerge.Run("attachments", "check", "--base", basePak, fixedPath); }
                catch (Exception ex)
                {
                    mod.Status = ModStatus.Error; mod.StatusDetail = "verify: " + ex.Message;
                    _worker.ReportProgress(i, new object[] { "done", modIdx, mod });
                    continue;
                }
                var vCheck = VpkMerge.ParseCheck(verify.Lines);
                if (!vCheck.ParseOk || vCheck.IsStale)
                {
                    mod.Status = ModStatus.Error;
                    mod.StatusDetail = vCheck.IsStale ? "still stale after heal" : "verify parse failed";
                    _worker.ReportProgress(i, new object[] { "done", modIdx, mod });
                    continue;
                }

                // Backup original
                try { File.Copy(mod.FilePath, Path.Combine(backup, mod.FileName), true); }
                catch (Exception ex)
                {
                    mod.Status = ModStatus.Error; mod.StatusDetail = "backup failed: " + ex.Message;
                    _worker.ReportProgress(i, new object[] { "done", modIdx, mod });
                    continue;
                }

                // Replace
                try { File.Copy(fixedPath, mod.FilePath, true); }
                catch (Exception ex)
                {
                    mod.Status = ModStatus.Error; mod.StatusDetail = "replace failed: " + ex.Message;
                    // Try to restore backup
                    try { File.Copy(Path.Combine(backup, mod.FileName), mod.FilePath, true); } catch { }
                    _worker.ReportProgress(i, new object[] { "done", modIdx, mod });
                    continue;
                }

                mod.Status = ModStatus.Fixed;
                _worker.ReportProgress(i, new object[] { "done", modIdx, mod });
            }
        };

        _worker.ProgressChanged += (_, args) =>
        {
            var data = (object[])args.UserState;
            var type = (string)data[0];

            if (type == "log")
            {
                LogLine((string)data[1], Color.FromArgb(140, 140, 140));
            }
            else if (type == "scanning")
            {
                var modIdx = (int)data[1];
                var name   = _mods[modIdx].FileName;
                UpdateGridRow(modIdx, name, "Fixing…", "");
                _progressLbl.Text = $"{args.ProgressPercentage + 1} / {staleList.Count}";
                _progress.Value   = args.ProgressPercentage + 1;
            }
            else // "done"
            {
                var modIdx = (int)data[1];
                var mod    = (ModEntry)data[2];
                string statusText = mod.Status.ToString();
                if (!string.IsNullOrEmpty(mod.StatusDetail)) statusText += " — " + mod.StatusDetail;
                string models = string.Join("\n", mod.StaleModels);
                UpdateGridRow(modIdx, mod.FileName, statusText, models);
                _grid.InvalidateRow(modIdx);
                LogLine(mod.Status == ModStatus.Fixed
                    ? $"  FIXED: {mod.FileName}"
                    : $"  ERROR: {mod.FileName} — {mod.StatusDetail}",
                    mod.Status == ModStatus.Fixed ? Color.FromArgb(100, 200, 100) : Color.FromArgb(255, 100, 100));
            }
        };

        _worker.RunWorkerCompleted += (_, __) =>
        {
            ShowProgress(false, 0);
            SetButtonsEnabled(true);
            _fixBtn.Enabled = _mods.Any(m => m.Status == ModStatus.Stale);
            UpdateSummary();

            var fixed_  = _mods.Count(m => m.Status == ModStatus.Fixed);
            var errors  = _mods.Count(m => m.Status == ModStatus.Error);
            LogLine($"\nDone. {fixed_} fixed, {errors} errors.", Color.White);
            if (fixed_ > 0)
            {
                var bDir = Path.Combine(BackupsRoot, _backupTimestamp);
                LogLine($"Originals backed up to:\n  {bDir}", Color.FromArgb(160, 160, 255));
                MessageBox.Show(this,
                    $"{fixed_} mod(s) fixed successfully.\n\nOriginals backed up to:\n{bDir}",
                    "Fix Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (errors > 0)
            {
                MessageBox.Show(this,
                    $"Fix completed with {errors} error(s). Check the log for details.",
                    "Fix Complete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        _worker.RunWorkerAsync(new object[] { BasePak, StagingDir, backupDir, staleList });
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private bool ValidatePaths()
    {
        var addons = _addonsBox.Text.Trim();
        if (!Directory.Exists(addons))
        {
            MessageBox.Show(this, "Addons folder not found:\n" + addons,
                "Invalid Path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (!File.Exists(BasePak))
        {
            MessageBox.Show(this,
                "Base pak not found:\n" + BasePak +
                "\n\nMake sure the addons folder is inside the Deadlock installation.",
                "Base Pak Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        try { var _ = VpkMerge.ExePath; }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "vpkmerge.exe Missing",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        return true;
    }

    private void UpdateGridRow(int idx, string mod, string status, string models)
    {
        if (_grid.Rows.Count <= idx) return;
        var row = _grid.Rows[idx];
        row.Cells[ColMod].Value    = mod;
        row.Cells[ColStatus].Value = status;
        row.Cells[ColModels].Value = models;
    }

    private void UpdateSummary()
    {
        var current = _mods.Count(m => m.Status == ModStatus.Current);
        var stale   = _mods.Count(m => m.Status == ModStatus.Stale);
        var fixed_  = _mods.Count(m => m.Status == ModStatus.Fixed);
        var errors  = _mods.Count(m => m.Status == ModStatus.Error);
        var skipped = _mods.Count(m => m.Status == ModStatus.Skipped);

        var parts = new List<string>();
        if (current  > 0) parts.Add($"{current} current");
        if (stale    > 0) parts.Add($"{stale} stale");
        if (fixed_   > 0) parts.Add($"{fixed_} fixed");
        if (skipped  > 0) parts.Add($"{skipped} skipped");
        if (errors   > 0) parts.Add($"{errors} errors");
        _summaryLbl.Text = string.Join("  |  ", parts);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _scanBtn.Enabled   = enabled;
        _browseBtn.Enabled = enabled;
        _addonsBox.Enabled = enabled;
    }

    private Panel _progPanel => (Panel)Tag;
    private int   _progressMax;

    private void ShowProgress(bool show, int max)
    {
        // progPanel is stored in Tag
        var progPanel = Controls.OfType<Panel>()
            .FirstOrDefault(p => p.Controls.Contains(_progress));
        if (progPanel != null) progPanel.Visible = show;
        if (show)
        {
            _progressMax    = max;
            _progress.Maximum = max > 0 ? max : 1;
            _progress.Value   = 0;
            _progressLbl.Text = "";
        }
    }

    private void LogLine(string text, Color color)
    {
        if (_log.InvokeRequired)
        {
            _log.Invoke(new Action<string, Color>(LogLine), text, color);
            return;
        }
        _log.SelectionStart  = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor  = color;
        _log.AppendText(text + "\n");
        _log.ScrollToCaret();
    }
}

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------
static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
