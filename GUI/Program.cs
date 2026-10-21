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
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Deadlocker Mod Fixer")]
[assembly: AssemblyProduct("Deadlocker Mod Fixer")]
[assembly: AssemblyVersion(AppInfo.Version + ".0")]

static class AppInfo
{
    public const string Version     = "1.1.0";
    public const string Repo        = "impyrobot/deadlocker-mod-fixer";
    public const string ReleasesUrl = "https://github.com/" + Repo + "/releases/latest";
    public const string SiteUrl     = "https://deadlocker.net";

    // GitHub requires TLS 1.2; .NET 4.0 refs don't name it, so use the raw value
    public static void EnableTls12()
    {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
    }
}

// ---------------------------------------------------------------------------
// Hero names - maps model folder codenames to the names players know.
// Display names match deadlocker.net's character_list.py.
// Update both lists when new heroes are added to the game.
// ---------------------------------------------------------------------------
static class HeroNames
{
    // Codenames that differ from the display name (internal name -> display name)
    private static readonly Dictionary<string, string> Codenames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "archer",     "Grey Talon" },  { "orion",    "Grey Talon" },
        { "astro",      "Holliday" },
        { "atlas",      "Abrams" },
        { "bookworm",   "Paige" },
        { "chrono",     "Paradox" },
        { "digger",     "Mo & Krill" },
        { "engineer",   "McGinnis" },
        { "familiar",   "Rem" },
        { "fencer",     "Apollo" },
        { "forge",      "McGinnis" },
        { "frank",      "Victor" },
        { "ghost",      "Lady Geist" },
        { "gigawatt",   "Seven" },
        { "hornet",     "Vindicta" },
        { "inferno",    "Infernus" },
        { "magician",   "Sinclair" },
        { "nano",       "Calico" },
        { "necro",      "Graves" },
        { "priest",     "Venator" },
        { "punkgoat",   "Billy" },
        { "synth",      "Pocket" },
        { "tengu",      "Ivy" },
        { "unicorn",    "Celeste" },
        { "vampirebat", "Mina" },
        { "viper",      "Vyper" },
        { "werewolf",   "Silver" },
    };

    private static readonly string[] Display =
    {
        "Abrams", "Bebop", "Billy", "Calico", "Drifter", "Dynamo", "Fathom", "Grey Talon",
        "Haze", "Holliday", "Infernus", "Ivy", "Kelvin", "Lady Geist", "Lash", "Magician",
        "McGinnis", "Mina", "Mirage", "Mo & Krill", "Paige", "Paradox", "Pocket", "Raven",
        "Seven", "Shiv", "Sinclair", "The Doorman", "Trapper", "Victor", "Vindicta", "Viper",
        "Viscous", "Vyper", "Warden", "Wraith", "Wrecker", "Yamato",
        "Graves", "Rem", "Silver", "Venator", "Apollo", "Celeste", "Deadman Danny",
        "Rat King", "Solomon", "Violet", "Nurse Harrow", "Baba",
    };

    // Where hero codenames show up in mod file paths:
    //   models/heroes_wip/haze/..., materials/models/heroes_staging/yamato_v2/...  (strongest signal)
    //   panorama/images/heroes/inferno_card_psd.vtex_c
    //   particles/abilities/lash/..., sounds/weapons/inferno/..., particles/abilities/melee/viscous/...
    private static readonly Regex HeroFolder =
        new Regex(@"(?:^|/)heroes[^/]*/([^/]+)/", RegexOptions.IgnoreCase);
    private static readonly Regex HeroCard =
        new Regex(@"^panorama/images/heroes/([a-z]+)_", RegexOptions.IgnoreCase);
    private static readonly Regex HeroAssets =
        new Regex(@"^(?:particles|sounds|soundevents)/", RegexOptions.IgnoreCase);

    // Hero name for a mod, or what kind of mod it is when no hero is found
    public static string Describe(IList<string> paths)
    {
        var hero = FromFiles(paths);
        return hero != "" ? hero : ModType(paths);
    }

    // Hero most referenced by a mod's file paths, or "" if none
    public static string FromFiles(IEnumerable<string> paths)
    {
        var votes = new Dictionary<string, int>();
        foreach (var raw in paths)
        {
            var p = raw.Replace('\\', '/');

            var m = HeroFolder.Match(p);
            if (m.Success)
            {
                // A hero folder is authoritative, so name even heroes we don't know yet
                var code = m.Groups[1].Value;
                Vote(votes, Resolve(code) ?? Capitalize(Regex.Replace(code, @"_v\d+$", "")), 3);
                continue;
            }

            m = HeroCard.Match(p);
            if (m.Success) { Vote(votes, Resolve(m.Groups[1].Value), 1); continue; }

            // Effects/sounds: the first folder that is a known hero codename
            if (HeroAssets.IsMatch(p))
            {
                var segs = p.Split('/');
                for (int i = 1; i < segs.Length - 1; i++)
                {
                    var h = Resolve(segs[i]);
                    if (h != null) { Vote(votes, h, 1); break; }
                }
            }
        }
        if (votes.Count == 0) return "";

        // Effects/sound packs touching many heroes: naming just one would mislead
        var top = votes.OrderByDescending(kv => kv.Value).First();
        if (votes.Count >= 3 && top.Value * 2 < votes.Values.Sum()) return "(multi-hero pack)";
        return top.Key;
    }

    // Known hero for a codename, trying variants (yamato_v2, wraith_gen_man, werewolf_wolf), else null
    private static string Resolve(string code)
    {
        code = Regex.Replace(code, @"_v\d+$", "");
        while (code.Length > 0)
        {
            var name = Known(code);
            if (name != null) return name;
            var cut = code.LastIndexOf('_');
            if (cut <= 0) break;
            code = code.Substring(0, cut);
        }
        return null;
    }

    private static string Known(string code)
    {
        string name;
        if (Codenames.TryGetValue(code, out name)) return name;
        var key = Squash(code);
        return Display.FirstOrDefault(d => Squash(d) == key || Squash(d) == "the" + key);
    }

    private static void Vote(Dictionary<string, int> votes, string hero, int weight)
    {
        if (string.IsNullOrEmpty(hero)) return;
        int n;
        votes.TryGetValue(hero, out n);
        votes[hero] = n + weight;
    }

    private static string Capitalize(string s)
    {
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    // Label for mods with no hero files, from the folder most of their files live in
    private static string ModType(IList<string> paths)
    {
        var biggest = paths
            .Select(p => p.Replace('\\', '/').Split('/')[0].ToLowerInvariant())
            .Select(top =>
                top == "sounds" || top == "soundevents" ? "(sound mod)" :
                top == "panorama"                       ? "(UI mod)" :
                top == "particles"                      ? "(effects mod)" :
                top == "models" || top == "materials"   ? "(map/props mod)" : null)
            .Where(t => t != null)
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        // Only label it when that kind of file is a real share of the mod
        return biggest != null && biggest.Count() * 3 >= paths.Count ? biggest.Key : "(other)";
    }

    private static string Squash(string s)
    {
        return Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]", "");
    }
}

// ---------------------------------------------------------------------------
// VPK directory reader - lists the files inside a *_dir.vpk (reads only the
// header and directory tree, never the file data)
// ---------------------------------------------------------------------------
static class VpkDir
{
    public static List<string> ListFiles(string vpkPath)
    {
        var files = new List<string>();
        using (var fs = File.OpenRead(vpkPath))
        using (var br = new BinaryReader(fs))
        {
            if (br.ReadUInt32() != 0x55AA1234) throw new InvalidDataException("not a VPK file");
            var version  = br.ReadUInt32();
            var treeSize = br.ReadUInt32();
            if (version == 2) br.ReadBytes(16);   // section sizes we don't need
            else if (version != 1) throw new InvalidDataException("unsupported VPK version " + version);

            var treeEnd = fs.Position + treeSize;
            while (fs.Position < treeEnd)
            {
                var ext = ReadString(br);
                if (ext == "") break;
                while (true)
                {
                    var dir = ReadString(br);
                    if (dir == "") break;
                    while (true)
                    {
                        var name = ReadString(br);
                        if (name == "") break;

                        br.ReadUInt32();                   // CRC
                        var preload = br.ReadUInt16();
                        br.ReadBytes(2 + 4 + 4 + 2);       // archive index, offset, length, terminator
                        if (preload > 0) br.ReadBytes(preload);

                        var file = ext == " " ? name : name + "." + ext;
                        files.Add(dir == " " ? file : dir + "/" + file);
                    }
                }
            }
        }
        return files;
    }

    private static string ReadString(BinaryReader br)
    {
        var bytes = new List<byte>();
        byte b;
        while ((b = br.ReadByte()) != 0) bytes.Add(b);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}

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
    public string Hero = "";
    public bool IsSplit;

    public ModEntry(string path)
    {
        FilePath = path;
        FileName = Path.GetFileName(path);
    }
}

// ---------------------------------------------------------------------------
// vpkmerge runner - locates/downloads vpkmerge.exe (hash-verified) and runs commands
// ---------------------------------------------------------------------------
static class VpkMerge
{
    // Official vpkmerge v0.22.1 by Slush97, unmodified
    public const string Version     = "0.22.1";
    public const string Sha256      = "b4a5d0e3a3a1d4e627cb4979216807295351c7fb38e5a3aa6345b213a5c69275";
    public const string DownloadUrl =
        "https://github.com/Slush97/vpkmerge/releases/download/v0.22.1/vpkmerge-windows-x86_64.exe";

    private static string _exePath;

    // True once a verified copy has been found; null means not looked yet
    public static bool? Verified;

    public static string ExePath
    {
        get
        {
            if (_exePath != null) return _exePath;
            _exePath = FindOrDownload();
            Verified = true;
            return _exePath;
        }
    }

    // Path of the downloaded copy, used when vpkmerge.exe isn't next to the app
    public static string CachedPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeadlockerModFixer", "vpkmerge.exe");

    public static bool NeedsDownload
    {
        get
        {
            if (_exePath != null) return false;
            return !HashMatches(NextToApp) && !HashMatches(CachedPath);
        }
    }

    private static string NextToApp => Path.Combine(
        Path.GetDirectoryName(Application.ExecutablePath), "vpkmerge.exe");

    private static string FindOrDownload()
    {
        // 1. Next to the app (shipped in the zip)
        if (HashMatches(NextToApp)) return NextToApp;

        // 2. Previously downloaded copy
        var cached = CachedPath;
        if (HashMatches(cached)) return cached;

        // 3. Download from the official GitHub release, verify, then move into place
        var nextToExists = File.Exists(NextToApp);
        var tmp = cached + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cached));
            AppInfo.EnableTls12();
            var req = (HttpWebRequest)WebRequest.Create(DownloadUrl);
            req.UserAgent = "DeadlockerModFixer/" + AppInfo.Version;
            req.Timeout   = 60000;
            using (var resp = req.GetResponse())
            using (var src  = resp.GetResponseStream())
            using (var fs   = File.Create(tmp))
                src.CopyTo(fs);

            if (!HashMatches(tmp))
                throw new InvalidOperationException("downloaded file failed the SHA-256 check");

            if (File.Exists(cached)) File.Delete(cached);
            File.Move(tmp, cached);
            return cached;
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw new InvalidOperationException(
                (nextToExists
                    ? "The vpkmerge.exe next to the app doesn't match the official v" + Version + " release, so it won't be run.\n\n"
                    : "") +
                "Couldn't download vpkmerge (" + ex.Message + ").\n\n" +
                "Extract the full zip, or put the official vpkmerge.exe v" + Version +
                " next to DeadlockModFixer.exe:\nhttps://github.com/Slush97/vpkmerge/releases/tag/v" + Version);
        }
    }

    public static bool HashMatches(string path)
    {
        if (!File.Exists(path)) return false;
        try { return FileSha256(path) == Sha256; }
        catch { return false; }
    }

    public static string FileSha256(string path)
    {
        using (var sha = SHA256.Create())
        using (var fs  = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
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
            // Read stdout and stderr together so neither buffer blocks. The handlers run on
            // separate threads and StringBuilder isn't thread-safe, so lock or lines get lost.
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
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

        var all = lines
            .Select(l => Regex.Match(l, @"^\s+(\S+\.vmdl_c)\s"))
            .Where(mm => mm.Success)
            .Select(mm => mm.Groups[1].Value)
            .ToArray();

        return new CheckResult { ParseOk = true, IsStale = stale > 0, StaleModels = models, AllModels = all };
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
    public string[] AllModels   = new string[0];
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
    private Button     _restoreBtn;
    private Button     _reportBtn;
    private Label      _summaryLbl;
    private ProgressBar _progress;
    private Label      _progressLbl;
    private DataGridView _grid;
    private RichTextBox  _log;
    private LinkLabel    _siteLink;
    private LinkLabel    _updateLink;

    // State
    private List<ModEntry> _mods = new List<ModEntry>();
    private BackgroundWorker _worker;
    private string _backupTimestamp;

    // Grid column indices
    private const int ColMod    = 0;
    private const int ColHero   = 1;
    private const int ColStatus = 2;
    private const int ColModels = 3;

    public MainForm()
    {
        BuildUI();
        AutoDetectAddons();
        Shown += (_, __) => CheckForUpdate();
    }

    // -----------------------------------------------------------------------
    // UI construction (no designer file needed)
    // -----------------------------------------------------------------------
    private void BuildUI()
    {
        Text            = "Deadlocker Mod Fixer v" + AppInfo.Version;
        MinimumSize     = new Size(820, 580);
        Size            = new Size(900, 660);
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
        _siteLink.LinkClicked += (_, __) => OpenUrl(AppInfo.SiteUrl);

        // Shown only when a newer release exists (see CheckForUpdate)
        _updateLink = new LinkLabel
        {
            Text      = "",
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            AutoSize  = true,
            Visible   = false,
            BackColor = Color.Transparent,
            LinkColor = Color.FromArgb(255, 200, 60),
            ActiveLinkColor = Color.White
        };
        _updateLink.LinkClicked += (_, __) => OpenUrl(AppInfo.ReleasesUrl);

        // Position header links on the right at runtime (needs form width)
        Action placeLinks = () =>
        {
            _siteLink.Left   = header.Width - _siteLink.Width - 16;
            _siteLink.Top    = (header.Height - _siteLink.Height) / 2;
            _updateLink.Left = _siteLink.Left - _updateLink.Width - 20;
            _updateLink.Top  = _siteLink.Top;
        };
        Shown         += (_, __) => placeLinks();
        header.Resize += (_, __) => placeLinks();
        _updateLink.SizeChanged += (_, __) => placeLinks();

        header.Controls.AddRange(new Control[] { logo, titleLbl, subLbl, _siteLink, _updateLink });

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

        _restoreBtn = new Button
        {
            Text      = "Restore Backup…",
            Width     = 120,
            Height    = 26,
            Top       = 8,
            Left      = 260,
            FlatStyle = FlatStyle.System
        };
        _restoreBtn.Click += RestoreClick;

        _reportBtn = new Button
        {
            Text      = "Copy Report",
            Width     = 100,
            Height    = 26,
            Top       = 8,
            Left      = 390,
            FlatStyle = FlatStyle.System
        };
        _reportBtn.Click += CopyReportClick;

        _summaryLbl = new Label
        {
            Text      = "",
            AutoSize  = true,
            Top       = 14,
            Left      = 504,
            ForeColor = Color.FromArgb(80, 80, 80)
        };

        actionPanel.Controls.AddRange(new Control[] { _scanBtn, _fixBtn, _restoreBtn, _reportBtn, _summaryLbl });

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
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = true
        };
        _log.LinkClicked += (_, e) => OpenUrl(e.LinkText);
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
            Name       = "Hero",
            HeaderText = "Hero",
            Width      = 135,   // fits "(multi-hero pack)" and "Deadman Danny" on one line
            MinimumWidth = 70,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.False }
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
            _grid.Rows.Add(m.FileName, "", "Pending", "");

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

                // Name the hero from the files inside the mod (also works for skipped mods)
                try { mod.Hero = HeroNames.Describe(VpkDir.ListFiles(mod.FilePath)); }
                catch { /* unreadable directory; fall back to the check output below */ }

                // Skip mods split across several files (pakNN_000.vpk etc.)
                var nm = Regex.Match(mod.FileName, @"^pak(\d+)_dir\.vpk$").Groups[1].Value;
                if (chunkSet.Contains(nm))
                {
                    mod.Status       = ModStatus.Skipped;
                    mod.IsSplit      = true;
                    mod.StatusDetail = "split across multiple files, not supported yet";
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

                // Fallback if the VPK's file list couldn't be read above
                if (mod.Hero == "") mod.Hero = HeroNames.FromFiles(check.AllModels);

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
                UpdateGridRow(idx, mod, "Scanning…");
                _progressLbl.Text = $"{idx + 1} / {_mods.Count}";
                _progress.Value = idx + 1;
            }
            else // "done"
            {
                UpdateGridRow(idx, mod);
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
        if (!ConfirmGameNotRunning()) return;

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
                UpdateGridRow(modIdx, _mods[modIdx], "Fixing…");
                _progressLbl.Text = $"{args.ProgressPercentage + 1} / {staleList.Count}";
                _progress.Value   = args.ProgressPercentage + 1;
            }
            else // "done"
            {
                var modIdx = (int)data[1];
                var mod    = (ModEntry)data[2];
                UpdateGridRow(modIdx, mod);
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
                LogLine($"Check {AppInfo.SiteUrl} for official updates to your mods.", Color.White);
                MessageBox.Show(this,
                    $"Fixed {fixed_} mod(s). Check deadlocker.net for official updates to your mods.\n\n" +
                    $"Originals backed up to:\n{bDir}\n\nUse \"Restore Backup…\" to undo.",
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
        // First run without vpkmerge next to the app downloads it (~9 MB) on the UI thread
        var downloading = VpkMerge.NeedsDownload;
        if (downloading)
        {
            LogLine("Downloading vpkmerge v" + VpkMerge.Version + " from github.com/Slush97/vpkmerge…", Color.White);
            _log.Refresh();
            UseWaitCursor = true;
        }
        try
        {
            var _ = VpkMerge.ExePath;
            if (downloading)
                LogLine("vpkmerge downloaded and SHA-256 verified.", Color.FromArgb(100, 200, 100));
        }
        catch (Exception ex)
        {
            VpkMerge.Verified = false;
            LogLine(ex.Message, Color.FromArgb(255, 100, 100));
            MessageBox.Show(this, ex.Message, "vpkmerge.exe Missing",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally { UseWaitCursor = false; }
        return true;
    }

    private bool ConfirmGameNotRunning()
    {
        var running = new[] { "project8", "DeadlockModManager", "CitadelModManager" }
            .Where(n => Process.GetProcessesByName(n).Length > 0).ToArray();
        if (running.Length == 0) return true;

        var msg = "The following processes are running:\n  " + string.Join(", ", running)
                + "\n\nReplacing VPK files while Deadlock is open can cause crashes.\n\nContinue anyway?";
        return MessageBox.Show(this, msg, "Deadlock is running",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser configured */ }
    }

    // -----------------------------------------------------------------------
    // RESTORE
    // -----------------------------------------------------------------------
    private void RestoreClick(object sender, EventArgs e)
    {
        var backups = Directory.Exists(BackupsRoot)
            ? new DirectoryInfo(BackupsRoot).GetDirectories()
                .Select(d => new { Dir = d, Paks = d.GetFiles("pak*_dir.vpk") })
                .Where(b => b.Paks.Length > 0)
                .OrderByDescending(b => b.Dir.Name)
                .ToList()
            : null;

        if (backups == null || backups.Count == 0)
        {
            MessageBox.Show(this, "No backups found yet.\n\nBackups are made automatically when you fix mods.",
                "Restore Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string chosen = null;
        using (var dlg = new Form
        {
            Text            = "Restore Backup",
            Size            = new Size(520, 340),
            MinimumSize     = new Size(400, 260),
            StartPosition   = FormStartPosition.CenterParent,
            Font            = Font,
            ShowInTaskbar   = false,
            MinimizeBox     = false,
            MaximizeBox     = false
        })
        {
            var info = new Label
            {
                Text   = "Pick a backup. Its original mod files will be copied back into your addons folder.",
                Dock   = DockStyle.Top,
                Height = 36,
                Padding = new Padding(8, 8, 8, 0)
            };
            var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            foreach (var b in backups)
            {
                DateTime when;
                var label = DateTime.TryParseExact(b.Dir.Name, "yyyy-MM-dd_HHmm", null,
                        System.Globalization.DateTimeStyles.None, out when)
                    ? when.ToString("yyyy-MM-dd HH:mm") : b.Dir.Name;
                var paks = string.Join(", ", b.Paks.Select(p => p.Name.Replace("_dir.vpk", "")));
                list.Items.Add($"{label}  —  {b.Paks.Length} mod(s) ({paks})");
            }
            list.SelectedIndex = 0;

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            var openLink = new LinkLabel { Text = "Open backups folder", AutoSize = true, Left = 8, Top = 12 };
            openLink.LinkClicked += (_, __) => OpenUrl(BackupsRoot);
            var ok     = new Button { Text = "Restore", Width = 90, Height = 26, Top = 7, FlatStyle = FlatStyle.System,
                                     DialogResult = DialogResult.OK, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var cancel = new Button { Text = "Cancel", Width = 80, Height = 26, Top = 7, FlatStyle = FlatStyle.System,
                                     DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            bottom.Controls.AddRange(new Control[] { openLink, ok, cancel });
            bottom.Layout += (_, __) =>
            {
                cancel.Left = bottom.ClientSize.Width - cancel.Width - 8;
                ok.Left     = cancel.Left - ok.Width - 6;
            };
            list.DoubleClick += (_, __) => { dlg.DialogResult = DialogResult.OK; };

            dlg.Controls.Add(list);
            dlg.Controls.Add(bottom);
            dlg.Controls.Add(info);
            dlg.AcceptButton = ok;
            dlg.CancelButton = cancel;

            if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
            chosen = backups[list.SelectedIndex].Dir.FullName;
        }

        var addons = _addonsBox.Text.Trim();
        if (!Directory.Exists(addons))
        {
            MessageBox.Show(this, "Addons folder not found:\n" + addons,
                "Invalid Path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var files = Directory.GetFiles(chosen, "pak*_dir.vpk");
        if (MessageBox.Show(this,
                $"Restore {files.Length} mod file(s) from\n{chosen}\n\ninto\n{addons}?\n\n" +
                "The current versions of these files will be overwritten.",
                "Confirm Restore", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        if (!ConfirmGameNotRunning()) return;

        LogLine($"\nRestoring backup {Path.GetFileName(chosen)}...", Color.White);
        int restored = 0, failed = 0;
        foreach (var f in files)
        {
            try
            {
                File.Copy(f, Path.Combine(addons, Path.GetFileName(f)), true);
                LogLine("  RESTORED: " + Path.GetFileName(f), Color.FromArgb(100, 200, 100));
                restored++;
            }
            catch (Exception ex)
            {
                LogLine($"  ERROR: {Path.GetFileName(f)} — {ex.Message}", Color.FromArgb(255, 100, 100));
                failed++;
            }
        }
        LogLine($"Restore complete: {restored} restored, {failed} failed.", Color.White);

        MessageBox.Show(this, $"Restored {restored} mod file(s)" + (failed > 0 ? $", {failed} failed. Check the log." : "."),
            "Restore Complete", MessageBoxButtons.OK,
            failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

        if (restored > 0 && File.Exists(BasePak))
            ScanClick(this, EventArgs.Empty);
    }

    // -----------------------------------------------------------------------
    // COPY REPORT - details for bug reports on Reddit/GitHub
    // -----------------------------------------------------------------------
    private void CopyReportClick(object sender, EventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Deadlocker Mod Fixer report");
        sb.AppendLine($"- Fixer version: {AppInfo.Version}");
        sb.AppendLine($"- vpkmerge: v{VpkMerge.Version}, " +
            (VpkMerge.Verified == true ? "SHA-256 verified"
             : VpkMerge.Verified == false ? "NOT found / failed verification" : "not checked yet"));
        sb.AppendLine($"- Windows: {WindowsVersion()} ({(Environment.Is64BitOperatingSystem ? "64" : "32")}-bit)");
        sb.AppendLine($"- Addons folder: {_addonsBox.Text}");
        sb.AppendLine($"- Base pak found: {(File.Exists(BasePak) ? "yes" : "no")}");
        sb.AppendLine();

        if (_mods.Count > 0)
        {
            sb.AppendLine("| Mod | Hero | Status | Affected models |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var m in _mods)
                sb.AppendLine($"| {m.FileName} | {m.Hero} | {StatusText(m)} | {string.Join("<br>", m.StaleModels)} |");
            sb.AppendLine();
        }

        sb.AppendLine("<details><summary>Log</summary>");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(_log.Text.TrimEnd());
        sb.AppendLine("```");
        sb.AppendLine("</details>");

        try
        {
            Clipboard.SetText(sb.ToString());
            LogLine("Report copied to clipboard. Paste it into your Reddit or GitHub post.", Color.FromArgb(160, 160, 255));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't copy to the clipboard: " + ex.Message,
                "Copy Report", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // -----------------------------------------------------------------------
    // UPDATE CHECK - asks GitHub for the latest release, silent on any failure
    // -----------------------------------------------------------------------
    private void CheckForUpdate()
    {
        var t = new Thread(() =>
        {
            try
            {
                AppInfo.EnableTls12();
                var req = (HttpWebRequest)WebRequest.Create(
                    "https://api.github.com/repos/" + AppInfo.Repo + "/releases/latest");
                req.UserAgent = "DeadlockerModFixer/" + AppInfo.Version;
                req.Accept    = "application/vnd.github+json";
                req.Timeout   = 5000;
                string json;
                using (var resp = req.GetResponse())
                using (var rd   = new StreamReader(resp.GetResponseStream()))
                    json = rd.ReadToEnd();

                var tag = Regex.Match(json, @"""tag_name""\s*:\s*""v?([0-9][0-9.]*)""").Groups[1].Value;
                Version latest;
                if (!Version.TryParse(tag, out latest)) return;
                if (latest <= new Version(AppInfo.Version)) return;

                BeginInvoke(new Action(() =>
                {
                    _updateLink.Text    = $"New version v{tag} available";
                    _updateLink.Visible = true;
                    LogLine($"New version v{tag} available: {AppInfo.ReleasesUrl}", Color.FromArgb(255, 200, 60));
                }));
            }
            catch { /* offline, rate-limited, etc. - not worth bothering the user */ }
        });
        t.IsBackground = true;
        t.Start();
    }

    // Environment.OSVersion reports 6.2 on Windows 10/11 without an app manifest; ask the registry
    private static string WindowsVersion()
    {
        const string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var build   = Registry.GetValue(key, "CurrentBuild", null) as string;
        var display = Registry.GetValue(key, "DisplayVersion", null) as string;
        if (build == null) return Environment.OSVersion.VersionString;
        int b;
        var name = int.TryParse(build, out b) && b >= 22000 ? "Windows 11" : "Windows 10";
        return $"{name} {display} (build {build})".Replace("  ", " ");
    }

    private static string StatusText(ModEntry mod)
    {
        var s = mod.Status.ToString();
        if (!string.IsNullOrEmpty(mod.StatusDetail)) s += " — " + mod.StatusDetail;
        return s;
    }

    private void UpdateGridRow(int idx, ModEntry mod, string statusOverride = null)
    {
        if (_grid.Rows.Count <= idx) return;
        var row = _grid.Rows[idx];
        row.Cells[ColMod].Value    = mod.FileName;
        row.Cells[ColHero].Value   = mod.Hero;
        row.Cells[ColStatus].Value = statusOverride ?? StatusText(mod);
        row.Cells[ColModels].Value = statusOverride != null ? "" : string.Join("\n", mod.StaleModels);
    }

    private void UpdateSummary()
    {
        var current = _mods.Count(m => m.Status == ModStatus.Current);
        var stale   = _mods.Count(m => m.Status == ModStatus.Stale);
        var fixed_  = _mods.Count(m => m.Status == ModStatus.Fixed);
        var errors  = _mods.Count(m => m.Status == ModStatus.Error);
        var split   = _mods.Count(m => m.Status == ModStatus.Skipped && m.IsSplit);
        var skipped = _mods.Count(m => m.Status == ModStatus.Skipped && !m.IsSplit);

        var parts = new List<string>();
        if (current  > 0) parts.Add($"{current} current");
        if (stale    > 0) parts.Add($"{stale} stale");
        if (fixed_   > 0) parts.Add($"{fixed_} fixed");
        if (split    > 0) parts.Add($"{split} split (not supported yet)");
        if (skipped  > 0) parts.Add($"{skipped} skipped");
        if (errors   > 0) parts.Add($"{errors} errors");
        _summaryLbl.Text = string.Join("  |  ", parts);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _scanBtn.Enabled    = enabled;
        _restoreBtn.Enabled = enabled;
        _reportBtn.Enabled  = enabled;
        _browseBtn.Enabled  = enabled;
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
