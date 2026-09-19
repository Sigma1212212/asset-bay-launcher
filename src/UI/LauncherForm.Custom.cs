using System.Drawing.Drawing2D;

namespace AssetBayLauncher.UI;

/// <summary>Customisation: accent, corners, font, case, background, 3D keys, and the Settings page that edits them.</summary>
public sealed partial class LauncherForm
{
    private static readonly (string name, string hex)[] Accents =
    {
        ("Theme", ""), ("Cyan", "#3BE8FF"), ("Orange", "#FF7A1A"), ("Green", "#39E07A"), ("Pink", "#FF3FB8"),
        ("Violet", "#8A5CFF"), ("Yellow", "#FFD23F"), ("Red", "#FF4D5E"), ("White", "#F2F2F2"),
    };
    private static readonly string[] Fonts = { "theme", "Segoe UI", "Bahnschrift", "Consolas", "Cascadia Mono", "Arial", "Georgia" };
    private static readonly string[] Corners = { "square", "soft", "round" };
    private static readonly string[] Cases = { "theme", "normal", "upper" };
    private static readonly string[] Backgrounds = { "flat", "gradient", "grid" };

    /// <summary>The theme with the player's accent colour swapped in (if they picked one).</summary>
    private LauncherTheme Tint(LauncherTheme t)
    {
        if (string.IsNullOrEmpty(config.Accent)) return t;
        try
        {
            var c = ColorTranslator.FromHtml(config.Accent);
            return t with { Accent = c, Accent2 = c, EdgeTop = c };
        }
        catch { return t; }
    }

    private static string Next(string[] options, string current)
    {
        int i = Array.FindIndex(options, o => o.Equals(current, StringComparison.OrdinalIgnoreCase));
        return options[(i + 1) % options.Length];
    }

    private void SaveLook()
    {
        config.Save();
        SetTheme(LauncherTheme.ByName(theme.Name), fromGame: false); // re-tint and cross-fade
        fontFamily = null;                                            // fonts rebuild on next paint
        ApplyRoundedCorners();
        TopMost = config.AlwaysOnTop;
        Invalidate();
    }

    private static string Title(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ================================================================== settings page

    private void BuildSettingsPage()
    {
        var p = (int)Page.Settings;
        float colW = (ContentW - 16) / 2f, h = 36, gap = 5;
        float lx = ContentX, rx = ContentX + colW + 16;
        int order = 0;

        void Row(float x, int row, string label, Func<string> value, Action click) => widgets.Add(new Widget
        {
            Rect = new RectangleF(x, ContentY + row * (h + gap), colW, h), Page = p, Order = order++,
            Label = label, Value = value, Click = click,
        });
        void Switch(float x, int row, string label, Func<bool> on, Action flip) => widgets.Add(new Widget
        {
            Rect = new RectangleF(x, ContentY + row * (h + gap), colW, h), Page = p, Order = order++,
            Label = label, Toggle = true, Selected = on, Click = () => { flip(); SaveLook(); },
        });

        // Look
        Row(lx, 0, "Theme", () => theme.Name, CycleTheme);
        Row(lx, 1, "Accent", () => Accents.FirstOrDefault(a => a.hex.Equals(config.Accent, StringComparison.OrdinalIgnoreCase)).name ?? config.Accent,
            () =>
            {
                int i = Array.FindIndex(Accents, a => a.hex.Equals(config.Accent, StringComparison.OrdinalIgnoreCase));
                config.Accent = Accents[(i + 1) % Accents.Length].hex;
                SaveLook();
            });
        Row(lx, 2, "Corners", () => Title(config.Corners), () => { config.Corners = Next(Corners, config.Corners); SaveLook(); });
        Row(lx, 3, "Font", () => config.Font == "theme" ? "Theme" : config.Font, () => { config.Font = Next(Fonts, config.Font); SaveLook(); });
        Row(lx, 4, "Text case", () => Title(config.TextCase), () => { config.TextCase = Next(Cases, config.TextCase); SaveLook(); });
        Row(lx, 5, "Background", () => Title(config.Background), () => { config.Background = Next(Backgrounds, config.Background); SaveLook(); });

        // Behaviour
        Switch(rx, 0, "Match in-game theme", () => config.FollowGameTheme, () => config.FollowGameTheme = !config.FollowGameTheme);
        Switch(rx, 1, "3D buttons", () => config.Depth, () => config.Depth = !config.Depth);
        Switch(rx, 2, "Animations", () => config.Animations, () => config.Animations = !config.Animations);
        Switch(rx, 3, "Show console", () => config.ShowConsole, () => config.ShowConsole = !config.ShowConsole);
        Switch(rx, 4, "Always on top", () => config.AlwaysOnTop, () => config.AlwaysOnTop = !config.AlwaysOnTop);
        Switch(rx, 5, "Pre-release versions", () => config.IncludePrereleases,
            () => { config.IncludePrereleases = !config.IncludePrereleases; nextReleaseCheck = DateTime.MinValue; });

        float y = ContentY + 6 * (h + gap) + 6;
        widgets.Add(new Widget
        {
            Rect = new RectangleF(lx, y, colW, h), Page = p, Order = order++, Label = "Reset look",
            Value = () => "back to defaults", Click = () =>
            {
                config.Corners = "square"; config.Accent = ""; config.Font = "theme"; config.TextCase = "theme";
                config.Background = "flat"; config.Depth = true; config.Animations = true; config.ShowConsole = true;
                config.AlwaysOnTop = false;
                SaveLook();
            },
        });
        widgets.Add(new Widget
        {
            Rect = new RectangleF(rx, y, colW, h), Page = p, Order = order++, Label = "Licences",
            Value = () => "MIT · notices", Click = OpenNotices,
        });
    }

    // ================================================================== 3D keys

    /// <summary>How tall a widget stands off the window (0 = flat). Cards sit lower than buttons.</summary>
    private float KeyDepth(Widget w)
    {
        if (!config.Depth || w.Nav || w.Toggle) return 0f;
        if (w.Card) return w.Click != null ? 4f : 3f;
        if (w.Primary) return 5f;
        return w.Small ? 4f : 3f;
    }

    /// <summary>The key's base and floor shadow, drawn before the face.</summary>
    private void DrawKeyBase(Graphics g, Widget w, RectangleF r, float depth, float a, float radius)
    {
        var baseRect = r with { Y = r.Y + depth };
        using (var shadow = Rounded(baseRect with { Y = baseRect.Y + 2, X = baseRect.X + 1, Width = baseRect.Width - 2 }, radius))
        using (var sb = new SolidBrush(Color.FromArgb((int)(60 * a), 0, 0, 0)))
            g.FillPath(sb, shadow);
        var baseColor = w.Primary ? P(t => t.Accent) : P(t => t.Button);
        using var path = Rounded(baseRect, radius);
        using var b = new SolidBrush(Fade(Lerp(baseColor, Color.Black, 0.45f), a));
        g.FillPath(b, path);
    }

    /// <summary>Light on the top half, shade on the bottom, a highlight along the top edge.</summary>
    private void DrawKeyFace(Graphics g, Widget w, RectangleF r, GraphicsPath path, float a, float radius)
    {
        if (!config.Depth || KeyDepth(w) <= 0f) return;
        var clip = g.Clip;
        g.SetClip(path, CombineMode.Intersect);
        var top = r with { Height = r.Height * 0.55f };
        using (var sheen = new LinearGradientBrush(top, Color.FromArgb((int)(34 * a), 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            g.FillRectangle(sheen, top);
        var bottom = new RectangleF(r.X, r.Y + r.Height * 0.5f, r.Width, r.Height * 0.5f);
        using (var shade = new LinearGradientBrush(bottom, Color.FromArgb(0, 0, 0, 0), Color.FromArgb((int)(46 * a), 0, 0, 0), 90f))
            g.FillRectangle(shade, bottom);
        g.Clip = clip;
        float inset = Math.Max(4f, radius);
        using var hl = new Pen(Color.FromArgb((int)(70 * a), 255, 255, 255), 1f);
        g.DrawLine(hl, r.X + inset, r.Y + 1.5f, r.Right - inset, r.Y + 1.5f);
    }
}
