using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AssetBayLauncher.UI;

/// <summary>
/// The launcher window. Everything is painted in code (no designer, no image or font files) with the
/// same palettes, status lights and staggered entrance as the in-game menu.
///
/// Motion model: every animated value keeps its own state and eases toward a target using the real frame
/// delta, so timing is identical at any frame rate. The frame loop runs at ~120 fps while anything moves
/// and stops repainting entirely when the window is still.
/// </summary>
public sealed partial class LauncherForm : Form
{
    private const int W = 980, H = 640, Pad = 22;

    private readonly LauncherConfig config;
    private readonly ReleaseUpdater updater;
    private readonly GameInjector injector;
    private readonly BackendMonitor backend;
    private readonly List<Widget> widgets = new();
    private readonly System.Windows.Forms.Timer frame = new() { Interval = 8 };
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 1000 };
    private readonly Stopwatch clock = Stopwatch.StartNew();

    // theme: palette cross-fades from `paletteFrom` to `theme`
    private LauncherTheme theme, paletteFrom;
    private float themeT = 1f;

    private double entranceStart, lastFrame;
    private Widget? hovered, pressed;
    private bool busy;

    // live state shown in the rows
    private ReleaseInfo? release;
    private string releaseText = "checking...";
    private StatusKind releaseState = StatusKind.Busy;
    private bool gameRunning;
    private string injectedLabel = "";
    private StatusKind statusKind = StatusKind.Idle;
    private float progress = -1f;
    private DateTime nextReleaseCheck = DateTime.MinValue;
    private int themePollTick;

    // Refreshed once a second by Poll(): nothing below touches the disk or the process list per frame.
    private bool releaseCached, localExists;
    private string menuCopyText = "", localBuildText = "";

    // animated state
    private float windowT;                 // 0 hidden .. 1 shown
    private enum WindowMotion { Opening, Open, Closing, Minimizing }
    private WindowMotion windowMotion = WindowMotion.Opening;
    private Point restLocation;
    private readonly Crossfade title = new("Ready");
    private readonly Crossfade status = new("");
    private Color statusColor, statusColorOld;
    private float progressShown, progressAlpha, shimmer;
    private float successT = -1f;          // check mark draw-on after an injection
    private float sheen;                   // primary button hover sweep phase

    private enum StatusKind { Idle, Busy, Ok, Error }

    private sealed class Widget
    {
        public RectangleF Rect;
        public string Label = "";
        public Func<string>? Value;
        public Func<StatusKind?>? Light;
        public Func<bool>? Enabled;
        public Action? Click;
        public bool Primary, Small, Info, Nav, Card, Toggle;
        public int Order;
        public int Page = -1;              // -1 = on every page
        public Func<string>? Sub;          // card: small line under the value
        public Func<float>? Meter;         // card: 0..1 usage bar
        public Func<bool>? Selected;       // nav: current page; toggle: on
        public Func<bool>? Shown;          // extra visibility rule (null = always)
        public float Knob;                 // toggle knob position

        public float Hover, Press, EnabledT = 1f;
        public readonly Crossfade ValueText = new("");
        public Color LightNow;
        public bool LightInit;
        public StatusKind? LightKind;
        public float PingT = -1f;
        public readonly List<(PointF at, float t)> Ripples = new();

        public bool IsEnabled => Enabled?.Invoke() ?? true;
    }

    /// <summary>Old/new text pair; t runs 0..1 as the new one replaces the old.</summary>
    private sealed class Crossfade
    {
        public string Current, Old = "";
        public float T = 1f;
        public Crossfade(string initial) => Current = initial;

        public bool Set(string next)
        {
            if (next == Current) return false;
            Old = Current;
            Current = next;
            T = 0f;
            return true;
        }
    }

    public LauncherForm(LauncherConfig config)
    {
        this.config = config;
        updater = new ReleaseUpdater(config);
        injector = new GameInjector(config);
        backend = new BackendMonitor(config.BackendUrl);
        theme = Tint((config.FollowGameTheme ? LauncherTheme.FromGame() : null) ?? LauncherTheme.ByName(config.Theme));
        TopMost = config.AlwaysOnTop;
        paletteFrom = theme;
        statusColor = statusColorOld = theme.SubText;

        Text = "Asset Bay Launcher";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(W, H);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        KeyPreview = true;
        Opacity = 0; // faded in by the frame loop
        Icon = MakeIcon();

        BuildWidgets();

        frame.Tick += (_, _) => Frame();
        poll.Tick += (_, _) => Poll();
        Load += (_, _) => restLocation = Location;
        Shown += (_, _) =>
        {
            ApplyRoundedCorners();
            timeBeginPeriod(1); // 1 ms timer resolution so the 8 ms frame timer is actually 8 ms
            entranceStart = lastFrame = Now;
            frame.Start();
            poll.Start();
            Poll();
        };
        Move += (_, _) => { if (windowMotion == WindowMotion.Open && WindowState == FormWindowState.Normal) restLocation = Location; };
        Resize += (_, _) =>
        {
            // Coming back from the taskbar: fade in again.
            if (WindowState == FormWindowState.Normal && windowMotion == WindowMotion.Minimizing)
            {
                windowMotion = WindowMotion.Opening;
                windowT = 0f;
            }
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        FormClosed += (_, _) => { timeEndPeriod(1); injector.Dispose(); frame.Dispose(); poll.Dispose(); };
    }

    private double Now => clock.Elapsed.TotalSeconds;

    // ================================================================== layout

    private string MenuCopyText()
    {
        if (release != null && releaseCached) return $"downloaded {release.Tag}";
        if (BundledMenu.Available)
            return release == null || release.Tag == BundledMenu.Version
                ? $"built in {BundledMenu.Version}"
                : $"built in {BundledMenu.Version} · {release.Tag} will download";
        return "will download";
    }

    private string LocalBuildText()
    {
        string path = config.ResolvedLocalDll;
        if (!File.Exists(path)) return "not found (click Test to pick)";
        var age = DateTime.Now - File.GetLastWriteTime(path);
        return age.TotalMinutes < 1 ? "built just now"
             : age.TotalHours < 1 ? $"built {age.TotalMinutes:0} min ago"
             : age.TotalDays < 1 ? $"built {age.TotalHours:0} h ago"
             : File.GetLastWriteTime(path).ToString("d MMM");
    }

    // ================================================================== actions

    private void Poll()
    {
        injector.Refresh();
        gameRunning = injector.GameRunning;
        if (!gameRunning) injectedLabel = "";
        RefreshFileState();

        if (DateTime.UtcNow >= nextReleaseCheck) CheckReleaseAsync(force: false);
        backend.RefreshAsync();
        Invalidate(); // cards show live values

        // Follow the in-game menu's theme when it changes (checked every couple of seconds).
        if (config.FollowGameTheme && ++themePollTick % 2 == 0)
        {
            var gameTheme = LauncherTheme.FromGame();
            if (gameTheme != null && gameTheme.Name != theme.Name) SetTheme(gameTheme, fromGame: true);
        }
    }

    private void RefreshFileState()
    {
        releaseCached = release != null && updater.IsCached(release);
        localExists = File.Exists(config.ResolvedLocalDll);
        localBuildText = LocalBuildText();
        menuCopyText = MenuCopyText();
    }

    private async void CheckReleaseAsync(bool force)
    {
        nextReleaseCheck = DateTime.UtcNow.AddMinutes(5);
        if (!config.RepositoryConfigured)
        {
            releaseText = BundledMenu.Available ? $"built-in {BundledMenu.Version} only" : "set repository in launcher.json";
            releaseState = StatusKind.Error;
            return;
        }
        if (force) { releaseText = "checking..."; releaseState = StatusKind.Busy; }
        try
        {
            var latest = await updater.GetLatestAsync();
            bool newer = release != null && latest.Tag != release.Tag;
            release = latest;
            releaseText = latest.Tag + (updater.IsCached(latest) ? "" : " · new");
            RefreshFileState();
            releaseState = StatusKind.Ok;
            if (newer && injector.IsInjected)
                SetStatus($"{latest.Tag} is out - eject and inject to update.", StatusKind.Busy);
        }
        catch (Exception e)
        {
            releaseText = e is HttpRequestException ? "offline" : Short(e.Message);
            releaseState = StatusKind.Error;
            if (force) SetStatus(e.Message, StatusKind.Error);
        }
    }

    private bool needsElevation;

    private static bool IsElevated
    {
        get
        {
            try
            {
                using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    private void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? Application.ExecutablePath)
            {
                UseShellExecute = true,
                Verb = "runas", // shows the Windows "allow this app to make changes" prompt
                WorkingDirectory = AppContext.BaseDirectory,
            });
            Close();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            SetStatus("Restart cancelled. Or close Gorilla Tag and start it normally (not as administrator).", StatusKind.Error);
        }
    }

    private async void RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        try { await action(); }
        catch (Exception e)
        {
            progress = -1f;
            // Windows refused access to the game: nearly always because the game runs as administrator
            // and the launcher doesn't. Offer a one-click elevated restart instead of a cryptic error.
            if (e.Message.IndexOf("open process", StringComparison.OrdinalIgnoreCase) >= 0 && !IsElevated)
            {
                needsElevation = true;
                SetStatus("Gorilla Tag is running as administrator, so the launcher needs to be too. Click Restart as admin.", StatusKind.Error);
            }
            else
            {
                SetStatus(e.Message, StatusKind.Error);
            }
        }
        finally { busy = false; RefreshFileState(); }
    }

    /// <summary>
    /// Newest release from GitHub first. If it matches the copy built into the exe, no download is needed.
    /// If GitHub can't be reached (or the download fails its SHA-256 check), fall back to the built-in copy.
    /// </summary>
    private async Task InjectLatestAsync()
    {
        string path, label;
        try
        {
            SetStatus("Checking GitHub for the newest version...", StatusKind.Busy);
            release = await updater.GetLatestAsync();
            releaseText = release.Tag;
            releaseState = StatusKind.Ok;

            if (BundledMenu.Available && release.Tag == BundledMenu.Version && !releaseCached)
            {
                path = BundledMenu.Extract();
            }
            else
            {
                bool cached = updater.IsCached(release);
                SetStatus(cached ? $"Verifying {release.Tag}..." : $"Downloading {release.Tag} from GitHub...", StatusKind.Busy);
                progress = 0f;
                var reporter = new Progress<float>(p => progress = p);
                path = await updater.EnsureDownloadedAsync(release, reporter);
            }
            label = release.Tag;
        }
        catch (Exception e) when (BundledMenu.Available)
        {
            progress = -1f;
            releaseText = e is HttpRequestException ? "offline" : Short(e.Message);
            releaseState = StatusKind.Error;
            path = BundledMenu.Extract();
            label = BundledMenu.Version + " (built-in)";
            SetStatus($"GitHub unavailable ({Short(e.Message)}) - using the built-in {BundledMenu.Version}.", StatusKind.Busy);
            await Task.Delay(900);
        }

        progress = 1f; // let the bar glide to full before it fades out
        SetStatus("Injecting...", StatusKind.Busy);
        await Task.Run(() => injector.Inject(path));
        progress = -1f;
        injectedLabel = label;
        successT = 0f;
        SetStatus($"Injected {label}. Press Tab in game (Y on the left controller in VR).", StatusKind.Ok);
    }

    private async Task InjectLocalAsync()
    {
        string path = config.ResolvedLocalDll;
        if (!File.Exists(path))
        {
            using var dialog = new OpenFileDialog { Filter = "Menu DLL (*.dll)|*.dll", Title = "Pick the BundleMenu.dll you built" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            path = dialog.FileName;
            config.LocalDllPath = path;
            config.Save();
            RefreshFileState();
        }

        SetStatus("Injecting local build...", StatusKind.Busy);
        await Task.Run(() => injector.Inject(path));
        injectedLabel = "local";
        successT = 0f;
        SetStatus($"Injected local build ({ReleaseUpdater.HashFile(path)[..8]}). Press Tab in game.", StatusKind.Ok);
    }

    private async Task EjectAsync()
    {
        SetStatus("Ejecting...", StatusKind.Busy);
        await Task.Run(injector.Eject);
        injectedLabel = "";
        successT = -1f;
        SetStatus("Ejected. You can inject another build now.", StatusKind.Idle);
    }

    private void CycleTheme()
    {
        int i = Array.FindIndex(LauncherTheme.All, t => t.Name == theme.Name);
        config.FollowGameTheme = false; // picking by hand overrides following the game
        SetTheme(LauncherTheme.All[(i + 1) % LauncherTheme.All.Length], fromGame: false);
    }

    private void SetTheme(LauncherTheme next, bool fromGame)
    {
        next = Tint(next);
        if (next == theme) return;
        paletteFrom = themeT >= 1f ? theme : Snapshot(); // re-targeting mid-fade starts from what's on screen
        theme = next;
        themeT = 0f;
        config.Theme = next.Name;
        config.Save();
        var oldIcon = Icon;
        Icon = MakeIcon();
        oldIcon?.Dispose();
        if (fromGame) SetStatus($"Matched the in-game theme: {next.Name}", StatusKind.Idle);
    }

    private void OpenNotices()
    {
        string notice = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        if (File.Exists(notice)) Process.Start(new ProcessStartInfo(notice) { UseShellExecute = true });
    }

    private void SetStatus(string text, StatusKind kind)
    {
        Log(text, kind);
        statusColorOld = statusColor;
        statusKind = kind;
        if (!status.Set(text)) statusColorOld = StatusColorFor(kind); // same text, new colour: just recolour
    }

    private void Minimize()
    {
        windowMotion = WindowMotion.Minimizing;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Play the exit animation first; Frame() calls Close() again once it has finished.
        if (windowMotion != WindowMotion.Closing && e.CloseReason == CloseReason.UserClosing && Visible && WindowState == FormWindowState.Normal)
        {
            e.Cancel = true;
            windowMotion = WindowMotion.Closing;
            return;
        }
        base.OnFormClosing(e);
    }

    private static string Short(string s) => s.Length <= 34 ? s : s[..33] + "…";

    // ================================================================== input

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit != hovered)
        {
            hovered = hit;
            if (hit?.Primary == true) sheen = 0f;
            Cursor = hit?.Click != null && hit.IsEnabled ? Cursors.Hand : Cursors.Default;
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var hit = HitTest(e.Location);
        if (hit?.Click != null && hit.IsEnabled)
        {
            pressed = hit;
            hit.Ripples.Add((e.Location, 0f)); // ripple starts exactly where you clicked
        }
        else if (e.Button == MouseButtons.Left && (e.Y < 80 || (e.X < SideW && e.Y < 96)))
        {
            // Drag the borderless window by its header.
            ReleaseCapture();
            SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, 2 /* HTCAPTION */, 0);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var hit = HitTest(e.Location);
        if (pressed != null && hit == pressed && pressed.IsEnabled) pressed.Click?.Invoke();
        pressed = null;
    }

    private Widget? HitTest(Point p) =>
        widgets.LastOrDefault(w => w.Click != null && OnPage(w) && (page != Page.Logs || w.Page < 0) && w.Rect.Contains(p));

    // ================================================================== frame loop

    private void Frame()
    {
        double now = Now;
        float dt = (float)Math.Clamp(now - lastFrame, 0, 0.05); // never jump after a hitch
        lastFrame = now;
        if (!config.Animations) dt = 1f; // every ease below reaches its target this frame

        StepWindow(dt);
        if (IsDisposed) return;

        themeT = Math.Min(1f, themeT + dt / 0.4f);
        title.Set(injectedLabel.Length > 0 ? "Menu loaded" : "Ready");
        title.T = Math.Min(1f, title.T + dt / 0.3f);
        pageTitle.T = Math.Min(1f, pageTitle.T + dt / 0.3f);
        status.T = Math.Min(1f, status.T + dt / 0.28f);
        statusColor = Motion.Approach(statusColor, StatusColorFor(statusKind), dt, 10f);

        // Progress glides toward its real value; shimmer runs while the bar is visible.
        bool showBar = progress >= 0f || (busy && progressShown > 0f);
        if (progress >= 0f) progressShown = Motion.Approach(progressShown, progress, dt, 9f);
        progressAlpha = Motion.Approach(progressAlpha, showBar ? 1f : 0f, dt, 8f);
        if (progressAlpha < 0.01f && progress < 0f) progressShown = 0f;
        shimmer += dt;

        if (successT >= 0f && successT < 2f) successT += dt;
        if (hovered?.Primary == true) sheen += dt;

        foreach (var w in widgets)
        {
            bool enabled = w.IsEnabled;
            bool hover = w == hovered && w.Click != null && enabled;
            w.Hover = Motion.Approach(w.Hover, hover ? 1f : 0f, dt, 14f);
            w.Press = Motion.Approach(w.Press, w == pressed ? 1f : 0f, dt, 28f);
            w.EnabledT = Motion.Approach(w.EnabledT, enabled || w.Info ? 1f : 0f, dt, 9f);

            if (w.Toggle) w.Knob = Motion.Approach(w.Knob, w.Selected?.Invoke() == true ? 1f : 0f, dt, 14f);
            w.ValueText.Set(w.Value?.Invoke() ?? "");
            w.ValueText.T = Math.Min(1f, w.ValueText.T + dt / 0.28f);

            for (int i = w.Ripples.Count - 1; i >= 0; i--)
            {
                var r = w.Ripples[i];
                r.t += dt / 0.55f;
                if (r.t >= 1f) w.Ripples.RemoveAt(i); else w.Ripples[i] = r;
            }

            var kind = w.Light?.Invoke();
            if (kind != null)
            {
                var target = LightColor(kind.Value);
                if (!w.LightInit) { w.LightNow = target; w.LightInit = true; }
                else w.LightNow = Motion.Approach(w.LightNow, target, dt, 8f);
                if (kind != w.LightKind)
                {
                    if (kind == StatusKind.Ok && w.LightKind != null) w.PingT = 0f; // "it worked" ping
                    w.LightKind = kind;
                }
            }
            if (w.PingT >= 0f) { w.PingT += dt / 0.7f; if (w.PingT >= 1f) w.PingT = -1f; }
        }

        if (IsAnimating()) Invalidate();
    }

    private void StepWindow(float dt)
    {
        switch (windowMotion)
        {
            case WindowMotion.Opening:
                windowT = Math.Min(1f, windowT + dt / 0.28f);
                if (windowT >= 1f) windowMotion = WindowMotion.Open;
                break;
            case WindowMotion.Closing:
            case WindowMotion.Minimizing:
                windowT = Math.Max(0f, windowT - dt / 0.18f);
                break;
        }

        float e = windowMotion is WindowMotion.Opening or WindowMotion.Open ? Motion.OutCubic(windowT) : windowT * windowT;
        // Setting Opacity makes Windows re-composite the layered window, so only do it when it changes.
        if (Math.Abs(Opacity - e) > 0.002) Opacity = e;
        else if (e >= 1f && Opacity < 1.0) Opacity = 1.0;
        if (WindowState == FormWindowState.Normal && windowMotion != WindowMotion.Open)
            Location = new Point(restLocation.X, restLocation.Y + (int)Math.Round((1f - e) * 16f));

        if (windowT <= 0f && windowMotion == WindowMotion.Closing)
        {
            frame.Stop();
            Close();
        }
        else if (windowT <= 0f && windowMotion == WindowMotion.Minimizing && WindowState != FormWindowState.Minimized)
        {
            Location = restLocation;
            WindowState = FormWindowState.Minimized;
            Opacity = 1; // the taskbar thumbnail should look normal
        }
    }

    private bool IsAnimating()
    {
        if (windowMotion != WindowMotion.Open || themeT < 1f || title.T < 1f || status.T < 1f || pageTitle.T < 1f) return true;
        if (Now - entranceStart < 1.2) return true;
        if (progressAlpha > 0.001f || successT is >= 0f and < 2f || hovered?.Primary == true) return true;
        if (Math.Abs(statusColor.ToArgb() - StatusColorFor(statusKind).ToArgb()) > 0 && status.T < 1f) return true;
        foreach (var w in widgets)
        {
            if (w.Ripples.Count > 0 || w.PingT >= 0f || w.ValueText.T < 1f) return true;
            if (Math.Abs(w.Hover - (w == hovered && w.IsEnabled ? 1f : 0f)) > 0.003f) return true;
            if (w.Press > 0.003f || Math.Abs(w.EnabledT - (w.IsEnabled || w.Info ? 1f : 0f)) > 0.003f) return true;
            if (w.LightKind == StatusKind.Busy && OnPage(w)) return true; // breathing light
            if (w.Toggle && Math.Abs(w.Knob - (w.Selected?.Invoke() == true ? 1f : 0f)) > 0.003f) return true;
            if (w.LightInit && w.LightKind != null && w.LightNow.ToArgb() != LightColor(w.LightKind.Value).ToArgb()) return true;
        }
        return false;
    }

    /// <summary>Same "Staggered" entrance as the menu: each row slides and fades in slightly after the last.</summary>
    private (float offset, float alpha) Entrance(int order)
    {
        if (order < 0 || !config.Animations) return (0f, 1f);
        float t = (float)Math.Clamp((Now - entranceStart - 0.08 - order * 0.055) / 0.42, 0, 1);
        return ((1f - Motion.OutCubic(t)) * 46f, Math.Clamp(t * 1.6f, 0f, 1f));
    }

    // ================================================================== palette

    private Color P(Func<LauncherTheme, Color> pick) =>
        themeT >= 1f ? pick(theme) : Lerp(pick(paletteFrom), pick(theme), Motion.InOutCubic(themeT));

    /// <summary>Freeze the currently blended palette so a new theme can fade from it.</summary>
    private LauncherTheme Snapshot() => theme with
    {
        PanelTop = P(t => t.PanelTop), PanelBottom = P(t => t.PanelBottom),
        EdgeTop = P(t => t.EdgeTop), EdgeBottom = P(t => t.EdgeBottom),
        Accent = P(t => t.Accent), Accent2 = P(t => t.Accent2),
        Button = P(t => t.Button), ButtonHover = P(t => t.ButtonHover), ButtonPressed = P(t => t.ButtonPressed),
        ButtonEdge = P(t => t.ButtonEdge), Text = P(t => t.Text), SubText = P(t => t.SubText),
        Idle = P(t => t.Idle), Busy = P(t => t.Busy), Ok = P(t => t.Ok), Error = P(t => t.Error),
    };

    private LauncherTheme Shape => themeT < 0.5f ? paletteFrom : theme; // fonts / casing swap at the midpoint
    private float Radius(Func<LauncherTheme, int> pick) =>
        (pick(paletteFrom) + (pick(theme) - pick(paletteFrom)) * Motion.InOutCubic(themeT)) * CornerScale;

    private float CornerScale => config.Corners switch { "square" => 0f, "round" => 1.4f, _ => 0.6f };

    private Color StatusColorFor(StatusKind k) => k switch
    {
        StatusKind.Ok => P(t => t.Ok),
        StatusKind.Error => P(t => t.Error),
        StatusKind.Busy => P(t => t.Busy),
        _ => P(t => t.SubText),
    };

    private Color LightColor(StatusKind k) => k switch
    {
        StatusKind.Ok => P(t => t.Ok),
        StatusKind.Busy => P(t => t.Busy),
        StatusKind.Error => P(t => t.Error),
        _ => P(t => t.Idle),
    };

    // ================================================================== painting

    private string? fontFamily;
    private Font? brandFont, titleFont, labelFont, valueFont, smallFont, primaryFont;

    private void EnsureFonts()
    {
        string family = config.Font != "theme" ? config.Font : Shape.UpperCase ? "Consolas" : "Segoe UI";
        if (family == fontFamily) return;
        foreach (var f in new[] { brandFont, titleFont, labelFont, valueFont, smallFont, primaryFont, consoleFont, cardValueFont, subtitleFont }) f?.Dispose();
        consoleFont = cardValueFont = subtitleFont = null; // rebuilt in the new family by EnsureChromeFonts
        fontFamily = family;
        brandFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        titleFont = new Font(family, 20f, FontStyle.Bold);
        labelFont = new Font(family, 11.5f);
        valueFont = new Font("Segoe UI", 9.5f);
        smallFont = new Font(family, 10.5f, FontStyle.Bold);
        primaryFont = new Font(family, 14f, FontStyle.Bold);
    }

    private string Cased(string s) => config.TextCase switch
    {
        "upper" => s.ToUpperInvariant(),
        "normal" => s,
        _ => Shape.Cased(s),
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        EnsureFonts();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        PaintChrome(g);
        foreach (var w in widgets) DrawWidget(g, w);
    }

    /// <summary>Old text slides up and fades out while the new one rises in from below.</summary>
    private static void DrawCrossfade(Graphics g, Crossfade text, Font font, Rectangle rect, Color color,
        TextFormatFlags flags, float travel, Color? oldColor = null, float alpha = 1f)
    {
        float e = Motion.OutCubic(text.T);
        if (e < 1f && text.Old.Length > 0)
        {
            var r = rect; r.Offset(0, (int)Math.Round(-travel * e));
            Ink.DrawText(g, text.Old, font, r, Fade(oldColor ?? color, (1f - e) * alpha), flags);
        }
        var n = rect; n.Offset(0, (int)Math.Round(travel * (1f - e)));
        Ink.DrawText(g, text.Current, font, n, Fade(color, e * alpha), flags);
    }

    private void DrawProgress(Graphics g)
    {
        if (progressAlpha <= 0.01f) return;
        var track = new RectangleF(Pad, H - 86, W - Pad * 2, 6);
        using (var trackBrush = new SolidBrush(Fade(P(t => t.Button), progressAlpha)))
        using (var trackPath = Rounded(track, 3)) g.FillPath(trackBrush, trackPath);

        var fill = track with { Width = Math.Max(6, track.Width * Math.Clamp(progressShown, 0f, 1f)) };
        using var fillPath = Rounded(fill, 3);
        using (var fillBrush = new LinearGradientBrush(track, Fade(P(t => t.Accent), progressAlpha), Fade(P(t => t.Accent2), progressAlpha), 0f))
            g.FillPath(fillBrush, fillPath);

        // a soft highlight sweeping along the filled part
        float sweepW = 70f;
        float x = fill.X - sweepW + (shimmer * 260f % (fill.Width + sweepW * 2));
        var band = new RectangleF(x, fill.Y, sweepW, fill.Height);
        var clip = g.Clip;
        g.SetClip(fillPath);
        using (var shine = new LinearGradientBrush(band, Color.Transparent, Color.Transparent, 0f))
        {
            shine.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb((int)(110 * progressAlpha), 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                Positions = new[] { 0f, 0.5f, 1f },
            };
            g.FillRectangle(shine, band);
        }
        g.Clip = clip;
    }

    private void DrawWidget(Graphics g, Widget w)
    {
        if (!OnPage(w) || (page == Page.Logs && w.Page >= 0)) return;
        var (offset, alpha) = Entrance(w.Order);
        if (alpha <= 0f) return;
        if (w.Nav) { DrawNav(g, w, w.Rect); return; }

        float keyDepth = KeyDepth(w);
        // 3D keys travel down onto their base when pressed instead of shrinking.
        float scale = 1f + w.Hover * (w.Primary ? 0.018f : 0.012f) - (keyDepth > 0f ? 0f : w.Press * 0.035f);
        var r = w.Rect;
        r = new RectangleF(r.X + offset + r.Width * (1 - scale) / 2, r.Y + r.Height * (1 - scale) / 2, r.Width * scale, r.Height * scale);
        bool injectedLook = w.Primary && injector.IsInjected;
        float a = alpha * (0.45f + 0.55f * (injectedLook ? Math.Max(w.EnabledT, 0.8f) : w.EnabledT));

        if (w.Order == -2) // footer link: colour warms up and an underline draws in on hover
        {
            var c = Lerp(P(t => t.SubText), P(t => t.Text), w.Hover);
            Ink.DrawText(g, w.Label, valueFont, Rectangle.Round(r), Fade(c, 0.85f), TextFormatFlags.HorizontalCenter);
            if (w.Hover > 0.01f)
            {
                var size = Ink.MeasureText(w.Label, valueFont);
                float lw = size.Width * Motion.OutCubic(w.Hover);
                using var pen = new Pen(Fade(P(t => t.Accent), w.Hover), 1f);
                g.DrawLine(pen, r.X + r.Width / 2 - lw / 2, r.Y + size.Height, r.X + r.Width / 2 + lw / 2, r.Y + size.Height);
            }
            return;
        }

        float radius = w.Label is "×" or "–" ? Math.Min(Radius(t => t.ButtonRadius), 10) : Radius(t => t.ButtonRadius);
        if (keyDepth > 0f)
        {
            DrawKeyBase(g, w, r, keyDepth, a, radius);
            r.Y += w.Press * keyDepth * 0.85f;
        }
        using var path = Rounded(r, radius);

        if (w.Primary)
        {
            DrawPrimary(g, w, r, path, a);
            return;
        }
        if (w.Card) { DrawCard(g, w, r, a); return; }
        if (w.Toggle) { DrawToggle(g, w, r, a); return; }

        var fillColor = Lerp(P(t => t.Button), P(t => t.ButtonHover), w.Hover);
        fillColor = Lerp(fillColor, P(t => t.ButtonPressed), w.Press);
        if (w.Info) fillColor = Fade(P(t => t.Button), 0.55f);
        using (var fill = new SolidBrush(Fade(fillColor, a))) g.FillPath(fill, path);
        DrawKeyFace(g, w, r, path, a, radius);
        DrawRipples(g, w, path, Color.White, 0.16f * a);

        var edge = Lerp(P(t => t.ButtonEdge), P(t => t.Accent), w.Hover);
        if (edge.A > 0)
            using (var pen = new Pen(Fade(edge, a * edge.A / 255f), 1.5f)) g.DrawPath(pen, path);

        if (w.Label == "theme")
        {
            var chip = new Crossfade(theme.Name + (config.FollowGameTheme ? " ·" : "")) { T = 1f };
            Ink.DrawText(g, chip.Current, smallFont, Rectangle.Round(r), Fade(P(t => t.Text), a),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        if (w.Small)
        {
            Ink.DrawText(g, Cased(w.Label), smallFont, Rectangle.Round(r), Fade(P(t => t.Text), a),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        // row: light · label · value
        float textLeft = r.X + 18;
        if (w.LightInit && w.LightKind != null)
        {
            float d = 10;
            var centre = new PointF(r.X + 23, r.Y + r.Height / 2);
            float pulse = w.LightKind == StatusKind.Busy ? 0.55f + 0.45f * MathF.Sin((float)Now * 7f) : 1f;
            if (w.PingT >= 0f) // expanding ring when a row turns green
            {
                float pe = Motion.OutCubic(w.PingT);
                float pr = 5f + 11f * pe;
                using var ring = new Pen(Fade(w.LightNow, (1f - w.PingT) * 0.7f * a), 2f);
                g.DrawEllipse(ring, centre.X - pr, centre.Y - pr, pr * 2, pr * 2);
            }
            if (w.LightKind == StatusKind.Busy) // soft halo that breathes with the light
            {
                float hr = 7f + 3f * pulse;
                using var halo = new SolidBrush(Fade(w.LightNow, 0.18f * pulse * a));
                g.FillEllipse(halo, centre.X - hr, centre.Y - hr, hr * 2, hr * 2);
            }
            using var lb = new SolidBrush(Fade(w.LightNow, a * pulse));
            g.FillEllipse(lb, centre.X - d / 2, centre.Y - d / 2, d, d);
            textLeft = r.X + 40;
        }
        Ink.DrawText(g, Cased(w.Label), labelFont, new Point((int)textLeft, (int)(r.Y + r.Height / 2 - 11)), Fade(P(t => t.Text), a));
        DrawCrossfade(g, w.ValueText, valueFont!, Rectangle.Round(new RectangleF(r.X, r.Y, r.Width - 16, r.Height)), P(t => t.SubText),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis, 7f, alpha: a);
    }

    private void DrawPrimary(Graphics g, Widget w, RectangleF r, GraphicsPath path, float a)
    {
        var top = Lerp(P(t => t.Accent), Color.White, w.Hover * 0.1f);
        top = Lerp(top, Color.Black, w.Press * 0.12f);
        using (var fill = new SolidBrush(Fade(top, a)))
            g.FillPath(fill, path);
        DrawKeyFace(g, w, r, path, a, Radius(t => t.ButtonRadius));

        // hover sheen: a diagonal band of light sweeps across, once every 1.6 s while hovered
        if (w.Hover > 0.01f && w.EnabledT > 0.5f)
        {
            float phase = (sheen % 1.6f) / 1.1f;
            if (phase <= 1f)
            {
                float bandW = r.Width * 0.35f;
                float x = r.X - bandW + phase * (r.Width + bandW * 2);
                var band = new RectangleF(x, r.Y, bandW, r.Height);
                var clip = g.Clip;
                g.SetClip(path);
                using var shine = new LinearGradientBrush(band, Color.Transparent, Color.Transparent, 20f)
                {
                    InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb((int)(70 * w.Hover), 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                        Positions = new[] { 0f, 0.5f, 1f },
                    },
                };
                g.FillRectangle(shine, band);
                g.Clip = clip;
            }
        }
        DrawRipples(g, w, path, Color.White, 0.28f * a);

        var ink = IsLight(P(t => t.Accent)) ? Color.FromArgb(20, 22, 40) : Color.White;
        bool injected = injector.IsInjected;
        string text = Cased(injected ? "Injected" : needsElevation ? "Restart as admin" : w.Label);
        var primaryFont = smallFont;
        var textRect = Rectangle.Round(r);

        if (injected)
        {
            // check mark draws itself on, then the label settles beside it
            float ct = successT < 0f ? 1f : Math.Clamp(successT / 0.45f, 0f, 1f);
            var size = Ink.MeasureText(text, primaryFont);
            float total = size.Width + 30f;
            float left = r.X + r.Width / 2 - total / 2;
            DrawCheck(g, new PointF(left + 10, r.Y + r.Height / 2), Motion.OutCubic(ct), Fade(ink, a));
            textRect = Rectangle.Round(new RectangleF(left + 30, r.Y, size.Width + 4, r.Height));
            float labelT = successT < 0f ? 1f : Math.Clamp((successT - 0.2f) / 0.35f, 0f, 1f);
            Ink.DrawText(g, text, primaryFont, textRect, Fade(ink, a * labelT), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        else
        {
            Ink.DrawText(g, text, primaryFont, textRect, Fade(ink, a), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (w.ValueText.Current.Length > 0 || w.ValueText.T < 1f)
            DrawCrossfade(g, w.ValueText, valueFont!, Rectangle.Round(new RectangleF(r.X, r.Bottom - 22, r.Width - 16, 18)),
                Fade(ink, 0.75f), TextFormatFlags.Right, 5f, alpha: a);
    }

    private static void DrawCheck(Graphics g, PointF c, float t, Color color)
    {
        var p0 = new PointF(c.X - 8, c.Y);
        var p1 = new PointF(c.X - 2, c.Y + 6);
        var p2 = new PointF(c.X + 9, c.Y - 7);
        float l1 = Dist(p0, p1), l2 = Dist(p1, p2), drawn = (l1 + l2) * t;
        using var pen = new Pen(color, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (drawn <= 0f) return;
        if (drawn <= l1) { g.DrawLine(pen, p0, Along(p0, p1, drawn / l1)); return; }
        g.DrawLines(pen, new[] { p0, p1, Along(p1, p2, (drawn - l1) / l2) });
    }

    private static void DrawRipples(Graphics g, Widget w, GraphicsPath clipPath, Color color, float strength)
    {
        if (w.Ripples.Count == 0) return;
        var clip = g.Clip;
        g.SetClip(clipPath);
        var b = clipPath.GetBounds();
        foreach (var (at, t) in w.Ripples)
        {
            float maxR = new[] { Dist(at, new PointF(b.Left, b.Top)), Dist(at, new PointF(b.Right, b.Top)),
                                 Dist(at, new PointF(b.Left, b.Bottom)), Dist(at, new PointF(b.Right, b.Bottom)) }.Max();
            float radius = maxR * Motion.OutCubic(t);
            using var brush = new SolidBrush(Fade(color, strength * (1f - t)));
            g.FillEllipse(brush, at.X - radius, at.Y - radius, radius * 2, radius * 2);
        }
        g.Clip = clip;
    }

    private void DrawLogo(Graphics g, RectangleF r, float alpha)
    {
        // Three stacked "bundles": an original mark drawn from rounded rectangles.
        for (int i = 0; i < 3; i++)
        {
            var box = new RectangleF(r.X + i * 3, r.Y + 16 - i * 8, r.Width - 12, 12);
            using var path = Rounded(box, 3);
            using var brush = new SolidBrush(Fade(Lerp(P(t => t.Accent), P(t => t.Accent2), i / 2f), alpha));
            g.FillPath(brush, path);
        }
    }

    private Icon MakeIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(theme.PanelTop)) g.FillEllipse(bg, 0, 0, 63, 63);
            for (int i = 0; i < 3; i++)
            {
                var box = new RectangleF(14 + i * 3, 28 - i * 8, 32, 12);
                using var path = Rounded(box, 3);
                using var brush = new SolidBrush(Lerp(theme.Accent, theme.Accent2, i / 2f));
                g.FillPath(brush, path);
            }
        }
        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return icon;
    }

    // ================================================================== helpers

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static float Dist(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static PointF Along(PointF a, PointF b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static Color Fade(Color c, float a) => Color.FromArgb((int)Math.Clamp(c.A * a, 0, 255), c.R, c.G, c.B);

    private static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                              (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    private static bool IsLight(Color c) => c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 150;

    // ================================================================== Win32

    private void ApplyRoundedCorners()
    {
        // Windows 11 draws smooth rounded corners and a shadow for borderless windows when asked to.
        int preference = config.Corners == "square" ? 1 /* DWMWCP_DONOTROUND */ : 2; // DWMWCP_ROUND
        try { DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int)); }
        catch { /* Windows 10: square corners, still works */ }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);
}

/// <summary>Frame-rate-independent easing helpers.</summary>
internal static class Motion
{
    public static float OutCubic(float t) { t = Math.Clamp(t, 0f, 1f); float u = 1f - t; return 1f - u * u * u; }

    public static float InOutCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
    }

    /// <summary>Exponential approach: same feel at 60 or 240 fps.</summary>
    public static float Approach(float value, float target, float dt, float speed)
    {
        float next = value + (target - value) * (1f - MathF.Exp(-speed * dt));
        return Math.Abs(next - target) < 0.0005f ? target : next;
    }

    public static Color Approach(Color value, Color target, float dt, float speed)
    {
        float k = 1f - MathF.Exp(-speed * dt);
        int Step(int a, int b) { int n = (int)Math.Round(a + (b - a) * k); return n == a && a != b ? a + Math.Sign(b - a) : n; }
        return Color.FromArgb(Step(value.A, target.A), Step(value.R, target.R), Step(value.G, target.G), Step(value.B, target.B));
    }
}
