using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AssetBayLauncher.UI;

/// <summary>
/// The launcher window. Everything is painted in code (no designer, no image or font files) with the
/// same palettes, status lights and staggered entrance as the in-game menu.
/// </summary>
public sealed class LauncherForm : Form
{
    private const int W = 460, H = 612, Pad = 22;

    private readonly LauncherConfig config;
    private readonly ReleaseUpdater updater;
    private readonly GameInjector injector;
    private readonly List<Widget> widgets = new();
    private readonly System.Windows.Forms.Timer frame = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 1000 };
    private readonly Stopwatch clock = Stopwatch.StartNew();

    private LauncherTheme theme;
    private double entranceStart;
    private Widget? hovered, pressed;
    private bool busy;

    // live state shown in the rows
    private ReleaseInfo? release;
    private string releaseText = "checking...";
    private StatusKind releaseState = StatusKind.Busy;
    private bool gameRunning;
    private string injectedLabel = "";
    private string statusText = "";
    private StatusKind statusKind = StatusKind.Idle;
    private float progress = -1f;
    private DateTime nextReleaseCheck = DateTime.MinValue;
    private int themePollTick;

    private enum StatusKind { Idle, Busy, Ok, Error }

    private sealed class Widget
    {
        public RectangleF Rect;
        public string Label = "";
        public Func<string>? Value;
        public Func<StatusKind?>? Light;
        public Func<bool>? Enabled;
        public Action? Click;
        public bool Primary, Small, Info;
        public int Order;
        public float Hover, Press;
        public bool IsEnabled => Enabled?.Invoke() ?? true;
    }

    public LauncherForm(LauncherConfig config)
    {
        this.config = config;
        updater = new ReleaseUpdater(config);
        injector = new GameInjector(config);
        theme = (config.FollowGameTheme ? LauncherTheme.FromGame() : null) ?? LauncherTheme.ByName(config.Theme);

        Text = "Asset Bay Launcher";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(W, H);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        KeyPreview = true;
        Icon = MakeIcon();

        BuildWidgets();

        frame.Tick += (_, _) => Animate();
        poll.Tick += (_, _) => Poll();
        Shown += (_, _) =>
        {
            ApplyRoundedCorners();
            entranceStart = Now;
            frame.Start();
            poll.Start();
            Poll();
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        FormClosed += (_, _) => { injector.Dispose(); frame.Dispose(); poll.Dispose(); };
    }

    private double Now => clock.Elapsed.TotalSeconds;

    // ================================================================== layout

    private void BuildWidgets()
    {
        float x = Pad, w = W - Pad * 2, y = 118, rowH = 50, gap = 8;
        int order = 0;

        widgets.Add(new Widget
        {
            Rect = new RectangleF(x, y, w, rowH), Label = "Gorilla Tag", Order = order++, Info = true,
            Value = () => gameRunning ? (injector.IsInjected ? "running · menu loaded" : "running") : "not running",
            Light = () => gameRunning ? StatusKind.Ok : StatusKind.Idle,
        });
        y += rowH + gap;

        widgets.Add(new Widget
        {
            Rect = new RectangleF(x, y, w, rowH), Label = "Latest release", Order = order++,
            Value = () => releaseText, Light = () => releaseState,
            Click = () => { nextReleaseCheck = DateTime.MinValue; CheckReleaseAsync(force: true); },
        });
        y += rowH + gap;

        widgets.Add(new Widget
        {
            Rect = new RectangleF(x, y, w, rowH), Label = "Menu copy", Order = order++, Info = true,
            Value = MenuCopyText,
            Light = () => (release != null && updater.IsCached(release)) || BundledMenu.Available ? StatusKind.Ok : StatusKind.Idle,
        });
        y += rowH + gap;

        widgets.Add(new Widget
        {
            Rect = new RectangleF(x, y, w, rowH), Label = "Local build", Order = order++, Info = true,
            Value = () => LocalBuildText(),
            Light = () => File.Exists(config.ResolvedLocalDll) ? StatusKind.Ok : StatusKind.Idle,
        });
        y += rowH + 20;

        widgets.Add(new Widget
        {
            Rect = new RectangleF(x, y, w, 62), Label = "Inject latest", Primary = true, Order = order++,
            Value = () => release != null ? release.Tag : "",
            Enabled = () => !busy && gameRunning && !injector.IsInjected && (config.RepositoryConfigured || BundledMenu.Available),
            Click = () => RunAsync(InjectLatestAsync),
        });
        y += 62 + 12;

        float half = (w - 10) / 2f;
        widgets.Add(new Widget
        {
            Rect = new RectangleF(x, y, half, 48), Label = "Test local build", Small = true, Order = order++,
            Enabled = () => !busy && gameRunning && !injector.IsInjected,
            Click = () => RunAsync(InjectLocalAsync),
        });
        widgets.Add(new Widget
        {
            Rect = new RectangleF(x + half + 10, y, half, 48), Label = "Eject", Small = true, Order = order,
            Enabled = () => !busy && injector.IsInjected,
            Click = () => RunAsync(EjectAsync),
        });
        order++;

        // header controls
        widgets.Add(new Widget { Rect = new RectangleF(W - Pad - 34, 24, 34, 34), Label = "×", Small = true, Order = -1, Click = Close });
        widgets.Add(new Widget { Rect = new RectangleF(W - Pad - 34 - 42, 24, 34, 34), Label = "–", Small = true, Order = -1,
                                 Click = () => WindowState = FormWindowState.Minimized });
        widgets.Add(new Widget
        {
            Rect = new RectangleF(W - Pad - 34 - 42 - 100, 28, 92, 26), Label = "theme", Small = true, Order = -1,
            Click = CycleTheme,
        });

        // footer licence link
        widgets.Add(new Widget
        {
            Rect = new RectangleF(x, H - 34, w, 20), Label = "MIT licence · third-party notices", Info = false,
            Order = -2, Click = OpenNotices,
        });
    }

    private string MenuCopyText()
    {
        if (release != null && updater.IsCached(release)) return $"downloaded {release.Tag}";
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
        gameRunning = injector.GameProcess() != null;
        if (!gameRunning) injectedLabel = "";

        if (DateTime.UtcNow >= nextReleaseCheck) CheckReleaseAsync(force: false);

        // Follow the in-game menu's theme when it changes (checked every couple of seconds).
        if (config.FollowGameTheme && ++themePollTick % 2 == 0)
        {
            var gameTheme = LauncherTheme.FromGame();
            if (gameTheme != null && gameTheme != theme) SetTheme(gameTheme, fromGame: true);
        }
        Invalidate();
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
        Invalidate();
    }

    private async void RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        try { await action(); }
        catch (Exception e) { SetStatus(e.Message, StatusKind.Error); progress = -1f; }
        finally { busy = false; Invalidate(); }
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

            if (BundledMenu.Available && release.Tag == BundledMenu.Version && !updater.IsCached(release))
            {
                path = BundledMenu.Extract();
            }
            else
            {
                bool cached = updater.IsCached(release);
                SetStatus(cached ? $"Verifying {release.Tag}..." : $"Downloading {release.Tag} from GitHub...", StatusKind.Busy);
                progress = 0f;
                var reporter = new Progress<float>(p => { progress = p; Invalidate(); });
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

        SetStatus("Injecting...", StatusKind.Busy);
        await Task.Run(() => injector.Inject(path));
        progress = -1f;
        injectedLabel = label;
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
        }

        SetStatus("Injecting local build...", StatusKind.Busy);
        await Task.Run(() => injector.Inject(path));
        injectedLabel = "local";
        SetStatus($"Injected local build ({ReleaseUpdater.HashFile(path)[..8]}). Press Tab in game.", StatusKind.Ok);
    }

    private async Task EjectAsync()
    {
        SetStatus("Ejecting...", StatusKind.Busy);
        await Task.Run(injector.Eject);
        injectedLabel = "";
        SetStatus("Ejected. You can inject another build now.", StatusKind.Idle);
    }

    private void CycleTheme()
    {
        int i = Array.IndexOf(LauncherTheme.All, theme);
        config.FollowGameTheme = false; // picking by hand overrides following the game
        SetTheme(LauncherTheme.All[(i + 1) % LauncherTheme.All.Length], fromGame: false);
    }

    private void SetTheme(LauncherTheme next, bool fromGame)
    {
        theme = next;
        config.Theme = next.Name;
        config.Save();
        Icon = MakeIcon();
        entranceStart = Now; // replay the entrance, same as the in-game menu does on theme change
        if (fromGame) SetStatus($"Matched the in-game theme: {next.Name}", StatusKind.Idle);
    }

    private void OpenNotices()
    {
        string notice = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        if (File.Exists(notice)) Process.Start(new ProcessStartInfo(notice) { UseShellExecute = true });
    }

    private void SetStatus(string text, StatusKind kind)
    {
        statusText = text;
        statusKind = kind;
        Invalidate();
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
        }
        else if (e.Button == MouseButtons.Left && e.Y < 100)
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
        widgets.LastOrDefault(w => w.Click != null && w.Rect.Contains(p));

    // ================================================================== animation + painting

    private void Animate()
    {
        float dt = 0.016f;
        foreach (var w in widgets)
        {
            float hoverTarget = w == hovered && w.Click != null && w.IsEnabled ? 1f : 0f;
            float pressTarget = w == pressed ? 1f : 0f;
            w.Hover += (hoverTarget - w.Hover) * Math.Min(1f, dt * 16f);
            w.Press += (pressTarget - w.Press) * Math.Min(1f, dt * 24f);
        }
        Invalidate();
    }

    /// <summary>Same "Staggered" entrance as the menu: each row slides and fades in slightly after the last.</summary>
    private (float offset, float alpha) Entrance(int order)
    {
        if (order < 0) return (0f, 1f);
        float t = (float)Math.Clamp((Now - entranceStart - order * 0.055) / 0.36, 0, 1);
        float e = 1f - MathF.Pow(1f - t, 3f);
        return ((1f - e) * 46f, Math.Clamp(t * 1.6f, 0f, 1f));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // background + gradient rim
        using (var bg = new LinearGradientBrush(ClientRectangle, theme.PanelTop, theme.PanelBottom, 90f))
            g.FillRectangle(bg, ClientRectangle);
        using (var rimPath = Rounded(new RectangleF(1, 1, W - 2, H - 2), 8))
        using (var rimBrush = new LinearGradientBrush(ClientRectangle, theme.EdgeTop, theme.EdgeBottom, 90f))
        using (var rim = new Pen(rimBrush, 2f))
            g.DrawPath(rim, rimPath);

        // header
        string fontName = theme.UpperCase ? "Consolas" : "Segoe UI";
        using var brandFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var titleFont = new Font(fontName, 20f, FontStyle.Bold);
        using var labelFont = new Font(fontName, 11.5f, theme.UpperCase ? FontStyle.Regular : FontStyle.Regular);
        using var valueFont = new Font("Segoe UI", 9.5f);
        using var smallFont = new Font(fontName, 10.5f, FontStyle.Bold);
        using var primaryFont = new Font(fontName, 14f, FontStyle.Bold);

        DrawLogo(g, new RectangleF(Pad, 28, 30, 30));
        TextRenderer.DrawText(g, "ASSET BAY  ·  LAUNCHER", brandFont, new Point(Pad + 40, 28), theme.SubText);
        TextRenderer.DrawText(g, theme.Cased(injectedLabel.Length > 0 ? "Menu loaded" : "Ready"), titleFont,
            new Point(Pad + 36, 42), theme.Text);

        // accent line (grows in with the entrance)
        float lineT = (float)Math.Clamp((Now - entranceStart) / 0.4, 0, 1);
        float lineW = (W - Pad * 2) * (1f - MathF.Pow(1f - lineT, 3f));
        using (var accent = new LinearGradientBrush(new RectangleF(Pad, 96, W - Pad * 2, 3), theme.Accent, theme.Accent2, 0f))
            g.FillRectangle(accent, W / 2f - lineW / 2f, 96, lineW, 3);

        foreach (var w in widgets) DrawWidget(g, w, labelFont, valueFont, smallFont, primaryFont);

        // progress + status
        float statusY = H - 74;
        if (progress >= 0f)
        {
            var track = new RectangleF(Pad, statusY - 12, W - Pad * 2, 6);
            using (var trackBrush = new SolidBrush(Fade(theme.Button, 1f)))
            using (var trackPath = Rounded(track, 3)) g.FillPath(trackBrush, trackPath);
            var fill = track with { Width = Math.Max(6, track.Width * progress) };
            using var fillBrush = new LinearGradientBrush(track, theme.Accent, theme.Accent2, 0f);
            using var fillPath = Rounded(fill, 3);
            g.FillPath(fillBrush, fillPath);
        }
        var statusColor = statusKind switch
        {
            StatusKind.Ok => theme.Ok,
            StatusKind.Error => theme.Error,
            StatusKind.Busy => theme.Busy,
            _ => theme.SubText,
        };
        TextRenderer.DrawText(g, statusText, valueFont, new Rectangle(Pad, (int)statusY, W - Pad * 2, 36), statusColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }

    private void DrawWidget(Graphics g, Widget w, Font label, Font value, Font small, Font primary)
    {
        var (offset, alpha) = Entrance(w.Order);
        if (alpha <= 0f) return;

        bool enabled = w.IsEnabled;
        float scale = 1f + w.Hover * (w.Primary ? 0.015f : 0.01f) - w.Press * 0.03f;
        var r = w.Rect;
        r = new RectangleF(r.X + offset + r.Width * (1 - scale) / 2, r.Y + r.Height * (1 - scale) / 2, r.Width * scale, r.Height * scale);
        float a = alpha * (enabled || w.Info ? 1f : 0.45f);

        if (w.Order == -2) // footer link
        {
            var c = Lerp(theme.SubText, theme.Text, w.Hover);
            TextRenderer.DrawText(g, w.Label, value, Rectangle.Round(r), Fade(c, 0.8f), TextFormatFlags.HorizontalCenter);
            return;
        }

        int radius = w.Label is "×" or "–" ? Math.Min(theme.ButtonRadius, 10) : theme.ButtonRadius;
        using var path = Rounded(r, radius);

        if (w.Primary)
        {
            var top = Lerp(theme.Accent, Color.White, w.Hover * 0.12f);
            using var fill = new LinearGradientBrush(r, Fade(top, a), Fade(theme.Accent2, a), 0f);
            g.FillPath(fill, path);
            var ink = IsLight(theme.Accent) ? Color.FromArgb(20, 22, 40) : Color.White;
            string text = theme.Cased(w.Label);
            string sub = w.Value?.Invoke() ?? "";
            TextRenderer.DrawText(g, text, primary, Rectangle.Round(r), Fade(ink, a),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (sub.Length > 0)
                TextRenderer.DrawText(g, sub, value, Rectangle.Round(new RectangleF(r.X, r.Bottom - 20, r.Width - 16, 16)),
                    Fade(ink, a * 0.75f), TextFormatFlags.Right);
            return;
        }

        var fillColor = Lerp(theme.Button, theme.ButtonHover, w.Hover);
        fillColor = Lerp(fillColor, theme.ButtonPressed, w.Press);
        if (w.Info) fillColor = Fade(theme.Button, 0.55f);
        using (var fill = new SolidBrush(Fade(fillColor, a))) g.FillPath(fill, path);

        var edge = Lerp(theme.ButtonEdge, theme.Accent, w.Hover);
        if (edge.A > 0)
            using (var pen = new Pen(Fade(edge, a * edge.A / 255f), 1.5f)) g.DrawPath(pen, path);

        if (w.Label == "theme")
        {
            TextRenderer.DrawText(g, theme.Name + (config.FollowGameTheme ? " ·" : ""), small, Rectangle.Round(r), Fade(theme.Text, a),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        if (w.Small)
        {
            TextRenderer.DrawText(g, theme.Cased(w.Label), small, Rectangle.Round(r), Fade(theme.Text, a),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        // row: light · label · value
        float textLeft = r.X + 18;
        var light = w.Light?.Invoke();
        if (light != null)
        {
            var lc = LightColor(light.Value);
            float pulse = light == StatusKind.Busy ? 0.6f + 0.4f * MathF.Sin((float)Now * 9f) : 1f;
            using var lb = new SolidBrush(Fade(lc, a * pulse));
            float d = 10;
            g.FillEllipse(lb, r.X + 18, r.Y + r.Height / 2 - d / 2, d, d);
            textLeft = r.X + 40;
        }
        TextRenderer.DrawText(g, theme.Cased(w.Label), label, new Point((int)textLeft, (int)(r.Y + r.Height / 2 - 11)), Fade(theme.Text, a));
        string val = w.Value?.Invoke() ?? "";
        TextRenderer.DrawText(g, val, value, Rectangle.Round(new RectangleF(r.X, r.Y, r.Width - 16, r.Height)), Fade(theme.SubText, a),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void DrawLogo(Graphics g, RectangleF r)
    {
        // Three stacked "bundles": an original mark drawn from rounded rectangles.
        for (int i = 0; i < 3; i++)
        {
            var box = new RectangleF(r.X + i * 3, r.Y + 16 - i * 8, r.Width - 12, 12);
            using var path = Rounded(box, 3);
            using var brush = new SolidBrush(Lerp(theme.Accent, theme.Accent2, i / 2f));
            g.FillPath(brush, path);
        }
    }

    private Color LightColor(StatusKind k) => k switch
    {
        StatusKind.Ok => theme.Ok,
        StatusKind.Busy => theme.Busy,
        StatusKind.Error => theme.Error,
        _ => theme.Idle,
    };

    private Icon MakeIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(theme.PanelTop)) g.FillEllipse(bg, 0, 0, 63, 63);
            DrawLogo(g, new RectangleF(14, 12, 44, 44));
        }
        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return icon;
    }

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
        int preference = 2; // DWMWCP_ROUND
        try { DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int)); }
        catch { /* Windows 10: square corners, still works */ }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
}
