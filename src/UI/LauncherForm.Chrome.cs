using System.Drawing.Drawing2D;

namespace AssetBayLauncher.UI;

/// <summary>Painting for the sidebar, header, cards, toggles and the console.</summary>
public sealed partial class LauncherForm
{
    private readonly List<(DateTime at, string text, StatusKind kind)> log = new();
    private Font? consoleFont, cardValueFont, subtitleFont;

    private void Log(string text, StatusKind kind)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (log.Count > 0 && log[^1].text == text) return; // same line twice in a row: keep one
        log.Add((DateTime.Now, text, kind));
        if (log.Count > 300) log.RemoveAt(0);
        Invalidate();
    }

    private void EnsureChromeFonts()
    {
        string family = fontFamily ?? "Segoe UI";
        consoleFont ??= new Font("Consolas", 9f);
        cardValueFont ??= new Font(family, 15f, family == "Segoe UI" ? FontStyle.Regular : FontStyle.Bold);
        subtitleFont ??= new Font(family, 9.5f);
    }

    private void PaintChrome(Graphics g)
    {
        EnsureChromeFonts();

        // Whole window background: flat, a top-to-bottom gradient, or a faint grid.
        if (config.Background == "gradient")
            using (var bg = new LinearGradientBrush(ClientRectangle, P(t => t.PanelTop), P(t => t.PanelBottom), 90f)) g.FillRectangle(bg, ClientRectangle);
        else
            using (var bg = new SolidBrush(P(t => t.PanelTop))) g.FillRectangle(bg, ClientRectangle);
        if (config.Background == "grid")
            using (var gp = new Pen(Fade(P(t => t.ButtonHover), 0.35f)))
            {
                for (int gx = SideW + 24; gx < W; gx += 24) g.DrawLine(gp, gx, 0, gx, H);
                for (int gy = 0; gy < H; gy += 24) g.DrawLine(gp, SideW, gy, W, gy);
            }
        using (var side = new SolidBrush(Lerp(P(t => t.PanelBottom), Color.Black, 0.15f))) g.FillRectangle(side, 0, 0, SideW, H);
        using (var hair = new Pen(Fade(P(t => t.ButtonHover), 0.9f))) g.DrawLine(hair, SideW, 0, SideW, H);
        using (var rim = new Pen(Fade(P(t => t.EdgeTop), 0.55f), 1f)) g.DrawRectangle(rim, 0, 0, W - 1, H - 1);

        // Sidebar: logo + name, then (widgets draw the nav), then game status at the bottom.
        DrawLogo(g, new RectangleF(22, 30, 32, 32), 1f);
        Ink.DrawText(g, "Asset Bay", titleFont, new Point(62, 22), P(t => t.Text));
        Ink.DrawText(g, "launcher", valueFont, new Point(66, 58), P(t => t.SubText));

        var dot = gameRunning ? P(t => t.Ok) : P(t => t.Idle);
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, 24, H - 38, 9, 9);
        Ink.DrawText(g, gameRunning ? (injector.IsInjected ? "Menu loaded" : "Game running") : "Game not running",
            valueFont, new Point(40, H - 43), P(t => t.SubText));
        Ink.DrawText(g, "v" + (Application.ProductVersion.Split('+')[0]), valueFont, new Point(24, H - 68), Fade(P(t => t.SubText), 0.7f));

        // Header: page title and a one-line description.
        DrawCrossfade(g, pageTitle, titleFont!, new Rectangle(ContentX, 18, 400, 36), P(t => t.Text), TextFormatFlags.Left, 8f);
        Ink.DrawText(g, PageSubtitles[(int)page], subtitleFont, new Point(ContentX + 2, 54), P(t => t.SubText));

        if ((config.ShowConsole && page != Page.Designer) || page == Page.Logs) DrawConsole(g);
        DrawDesignerPreview(g);
    }

    private void DrawConsole(Graphics g)
    {
        bool big = page == Page.Logs;
        var r = new RectangleF(ContentX, big ? ContentY : ConsoleTop, ContentW, (big ? H - ContentY : H - ConsoleTop) - 18);
        using (var path = Rounded(r, Math.Min(Radius(t => t.ButtonRadius), 8)))
        {
            using var fill = new SolidBrush(Lerp(P(t => t.PanelBottom), Color.Black, 0.25f));
            g.FillPath(fill, path);
            using var edge = new Pen(Fade(P(t => t.ButtonHover), 0.9f));
            g.DrawPath(edge, path);
        }

        // Tab strip: "console" + the live status line; the progress bar runs along its bottom edge.
        Ink.DrawText(g, "console", smallFont, new Point((int)r.X + 12, (int)r.Y + 8), P(t => t.Accent));
        DrawCrossfade(g, status, valueFont!, new Rectangle((int)r.X + 90, (int)r.Y + 8, (int)r.Width - 190, 20), statusColor,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis, 6f, statusColorOld);
        using (var line = new Pen(Fade(P(t => t.ButtonHover), 0.8f))) g.DrawLine(line, r.X, r.Y + 32, r.Right, r.Y + 32);
        DrawProgressAt(g, new RectangleF(r.X, r.Y + 31, r.Width, 3));

        // Newest lines at the bottom.
        int lineH = 17, top = (int)r.Y + 40;
        int fit = Math.Max(1, ((int)r.Bottom - 8 - top) / lineH);
        int start = Math.Max(0, log.Count - fit);
        for (int i = start; i < log.Count; i++)
        {
            var (at, text, kind) = log[i];
            int y = top + (i - start) * lineH;
            Ink.DrawText(g, $"[{at:HH:mm:ss}]", consoleFont, new Point((int)r.X + 12, y), Fade(P(t => t.SubText), 0.7f));
            var c = kind == StatusKind.Error ? P(t => t.Error) : kind == StatusKind.Ok ? P(t => t.Ok) : kind == StatusKind.Busy ? P(t => t.Busy) : P(t => t.Text);
            Ink.DrawText(g, text, consoleFont, new Rectangle((int)r.X + 92, y, (int)r.Width - 104, lineH), c,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
        if (log.Count == 0)
            Ink.DrawText(g, "nothing yet", consoleFont, new Point((int)r.X + 12, top), Fade(P(t => t.SubText), 0.6f));
    }

    private void DrawProgressAt(Graphics g, RectangleF track)
    {
        if (progressAlpha <= 0.01f) return;
        var fill = track with { Width = track.Width * Math.Clamp(progressShown, 0f, 1f) };
        using var b = new SolidBrush(Fade(P(t => t.Accent), progressAlpha));
        g.FillRectangle(b, fill);
    }

    // ------------------------------------------------------------------ widget kinds

    private void DrawNav(Graphics g, Widget w, RectangleF r)
    {
        bool on = w.Selected?.Invoke() == true;
        float glow = Math.Max(w.Hover * 0.6f, on ? 1f : 0f);
        if (glow > 0.01f)
        {
            using var path = Rounded(r, Math.Min(Radius(t => t.ButtonRadius), 8));
            using var fill = new SolidBrush(Fade(on ? P(t => t.Button) : P(t => t.ButtonHover), glow));
            g.FillPath(fill, path);
        }
        if (on)
            using (var bar = new SolidBrush(P(t => t.Accent))) g.FillRectangle(bar, r.X - 14, r.Y + 8, 3, r.Height - 16);

        var ink = on ? P(t => t.Text) : Lerp(P(t => t.SubText), P(t => t.Text), w.Hover);
        DrawGlyph(g, w.Label, new RectangleF(r.X + 10, r.Y + r.Height / 2 - 9, 18, 18), on ? P(t => t.Accent) : ink);
        Ink.DrawText(g, Cased(w.Label), labelFont, new Point((int)r.X + 40, (int)(r.Y + r.Height / 2 - 11)), ink);
    }

    private void DrawCard(Graphics g, Widget w, RectangleF r, float a)
    {
        using var path = Rounded(r, Radius(t => t.ButtonRadius));
        var fillColor = Lerp(P(t => t.Button), P(t => t.ButtonHover), w.Click != null ? w.Hover : 0f);
        using (var fill = new SolidBrush(Fade(fillColor, a))) g.FillPath(fill, path);
        DrawKeyFace(g, w, r, path, a, Radius(t => t.ButtonRadius));
        using (var edge = new Pen(Fade(Lerp(P(t => t.ButtonHover), P(t => t.Accent), w.Click != null ? w.Hover * 0.8f : 0f), a))) g.DrawPath(edge, path);
        DrawRipples(g, w, path, Color.White, 0.12f * a);

        float x = r.X + 16;
        if (w.LightInit && w.LightKind != null)
        {
            float pulse = w.LightKind == StatusKind.Busy ? 0.55f + 0.45f * MathF.Sin((float)Now * 7f) : 1f;
            using var lb = new SolidBrush(Fade(w.LightNow, a * pulse));
            g.FillEllipse(lb, r.Right - 24, r.Y + 16, 9, 9);
        }
        Ink.DrawText(g, Cased(w.Label), valueFont, new Point((int)x, (int)r.Y + 12), Fade(P(t => t.SubText), a));
        if (w.Value != null && w.ValueText.Current.Length > 0)
            DrawCrossfade(g, w.ValueText, cardValueFont!, Rectangle.Round(new RectangleF(x, r.Y + 30, r.Width - 32, 30)), P(t => t.Text),
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis, 6f, alpha: a);

        string sub = w.Sub?.Invoke() ?? "";
        float subY = w.ValueText.Current.Length > 0 ? r.Y + 60 : r.Y + 34;
        if (sub.Length > 0)
            Ink.DrawText(g, sub, valueFont, Rectangle.Round(new RectangleF(x, subY, r.Width - 32, w.Meter != null ? 18 : r.Bottom - subY - 6)),
                Fade(P(t => t.SubText), a), TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                (w.Meter != null ? TextFormatFlags.SingleLine : TextFormatFlags.WordBreak));
        if (w.Meter != null)
        {
            // Usage bar along the bottom edge: green, amber past 60%, red past 90%.
            float m = Math.Clamp(w.Meter(), 0f, 1f);
            var track = new RectangleF(r.X + 1, r.Bottom - 5, r.Width - 2, 4);
            var clipBefore = g.Clip;
            g.SetClip(path, System.Drawing.Drawing2D.CombineMode.Intersect); // follow the card's corners
            using (var tb = new SolidBrush(Fade(P(t => t.PanelBottom), a))) g.FillRectangle(tb, track);
            var col = m > 0.9f ? P(t => t.Error) : m > 0.6f ? P(t => t.Busy) : P(t => t.Ok);
            using (var fb = new SolidBrush(Fade(col, a))) g.FillRectangle(fb, track with { Width = Math.Max(3, track.Width * m) });
            g.Clip = clipBefore;
        }
    }

    private void DrawToggle(Graphics g, Widget w, RectangleF r, float a)
    {
        using var path = Rounded(r, Radius(t => t.ButtonRadius));
        using (var fill = new SolidBrush(Fade(Lerp(P(t => t.Button), P(t => t.ButtonHover), w.Hover), a))) g.FillPath(fill, path);
        Ink.DrawText(g, Cased(w.Label), labelFont, new Point((int)r.X + 16, (int)(r.Y + r.Height / 2 - 11)), Fade(P(t => t.Text), a));

        var pill = new RectangleF(r.Right - 60, r.Y + r.Height / 2 - 11, 44, 22);
        using (var pp = Rounded(pill, 11))
        using (var pb = new SolidBrush(Fade(Lerp(P(t => t.PanelBottom), P(t => t.Accent), w.Knob), a))) g.FillPath(pb, pp);
        float kx = pill.X + 3 + w.Knob * 22;
        using (var kb = new SolidBrush(Fade(Color.White, a))) g.FillEllipse(kb, kx, pill.Y + 3, 16, 16);
    }

    /// <summary>Tiny line icons for the sidebar, drawn with pens.</summary>
    private static void DrawGlyph(Graphics g, string name, RectangleF r, Color c)
    {
        using var pen = new Pen(c, 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        float x = r.X, y = r.Y, s = r.Width;
        switch (name)
        {
            case "Home":
                g.DrawLines(pen, new[] { new PointF(x, y + s * 0.45f), new PointF(x + s / 2, y), new PointF(x + s, y + s * 0.45f) });
                g.DrawRectangle(pen, x + s * 0.18f, y + s * 0.42f, s * 0.64f, s * 0.58f);
                break;
            case "Menu":
                g.DrawRectangle(pen, x + 1, y + 1, s - 2, s - 2);
                g.DrawLine(pen, x + 5, y + s * 0.38f, x + s - 5, y + s * 0.38f);
                g.DrawLine(pen, x + 5, y + s * 0.64f, x + s - 5, y + s * 0.64f);
                break;
            case "Online":
                g.DrawEllipse(pen, x + 1, y + 1, s - 2, s - 2);
                g.DrawEllipse(pen, x + s * 0.3f, y + 1, s * 0.4f, s - 2);
                g.DrawLine(pen, x + 1, y + s / 2, x + s - 1, y + s / 2);
                break;
            case "Designer":
                g.DrawRectangle(pen, x + 1, y + 1, s - 2, s - 2);
                g.DrawLine(pen, x + 1, y + s * 0.35f, x + s - 1, y + s * 0.35f);
                using (var dot = new SolidBrush(c)) g.FillEllipse(dot, x + s * 0.22f, y + s * 0.55f, s * 0.2f, s * 0.2f);
                g.DrawLine(pen, x + s * 0.5f, y + s * 0.65f, x + s - 3, y + s * 0.65f);
                break;
            case "Spotify":
                g.DrawEllipse(pen, x + 1, y + 1, s - 2, s - 2);
                for (int i = 0; i < 3; i++)
                    g.DrawArc(pen, x + 3 + i * 2.5f, y + 4 + i * 2.5f, s - 6 - i * 5f, s - 9 - i * 4f, -30, 80);
                break;
            case "Logs":
                for (int i = 0; i < 4; i++) g.DrawLine(pen, x + 1, y + 2 + i * s * 0.28f, x + (i % 2 == 0 ? s - 1 : s * 0.65f), y + 2 + i * s * 0.28f);
                break;
            default: // Settings: sliders
                for (int i = 0; i < 3; i++)
                {
                    float ly = y + 3 + i * s * 0.36f, kx = x + (i == 1 ? s * 0.7f : s * 0.3f);
                    g.DrawLine(pen, x + 1, ly, x + s - 1, ly);
                    using var knob = new SolidBrush(c);
                    g.FillEllipse(knob, kx - 3, ly - 3, 6, 6);
                }
                break;
        }
    }
}

public sealed partial class LauncherForm
{
    /// <summary>Paint each page, fully settled, into &lt;folder&gt;/launcher_&lt;page&gt;.png (see Program).</summary>
    internal void RenderPages(string folder)
    {
        Directory.CreateDirectory(folder);
        Poll();
        backend.RefreshAsync(force: true);
        var until = DateTime.Now.AddSeconds(4);
        while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(50); } // let GitHub + server answer
        SetStatus("Checking GitHub for the newest version...", StatusKind.Busy);
        SetStatus("Injected v1.9.0. Press Tab in game (Y on the left controller in VR).", StatusKind.Ok);
        windowMotion = WindowMotion.Open;

        foreach (Page p in Enum.GetValues(typeof(Page)))
        {
            page = p;
            pageTitle.Set(PageTitles[(int)p]);
            entranceStart = Now - 10;
            for (int i = 0; i < 90; i++) { lastFrame = Now - 0.05; Frame(); }
            pageTitle.T = status.T = title.T = 1f;
            foreach (var w in widgets) { w.ValueText.T = 1f; w.Knob = w.Selected?.Invoke() == true ? 1f : 0f; }

            using var bmp = new Bitmap(W, H);
            using (var g = Graphics.FromImage(bmp)) OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, W, H)));
            bmp.Save(Path.Combine(folder, $"launcher_{p}.png"));

            if (p == Page.Designer) // the designer has two halves; capture the second one as well
            {
                designerTab = 1;
                using var second = new Bitmap(W, H);
                using (var g = Graphics.FromImage(second)) OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, W, H)));
                second.Save(Path.Combine(folder, "launcher_Designer_Buttons.png"));
                designerTab = 2;
                using var third = new Bitmap(W, H);
                using (var g = Graphics.FromImage(third)) OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, W, H)));
                third.Save(Path.Combine(folder, "launcher_Designer_Layout.png"));
                designerTab = 0;
            }
        }
    }
}
