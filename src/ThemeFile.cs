using System.Text.Json;

namespace AssetBayLauncher;

/// <summary>A colour the way Unity writes them in JSON (0..1 floats).</summary>
public sealed class Col
{
    public float r, g, b, a = 1f;

    public Col() { }
    public Col(Color c) { r = c.R / 255f; g = c.G / 255f; b = c.B / 255f; a = c.A / 255f; }
    public Color ToColor() => Color.FromArgb(Clamp(a), Clamp(r), Clamp(g), Clamp(b));
    private static int Clamp(float v) => (int)Math.Clamp(MathF.Round(v * 255f), 0, 255);
    public static Col From(int rgb, float alpha = 1f) =>
        new() { r = ((rgb >> 16) & 0xFF) / 255f, g = ((rgb >> 8) & 0xFF) / 255f, b = (rgb & 0xFF) / 255f, a = alpha };
}

/// <summary>
/// One designed menu theme. The field names match the menu's MenuTheme exactly, so the file this writes
/// is the file the menu reads (%APPDATA%\AssetBay\themes\&lt;name&gt;.json), loaded on top of the Halo defaults.
/// </summary>
public sealed class ThemeFile
{
    public string DisplayName = "My theme";

    // Panel
    public Col PanelTop = Col.From(0x1B1F3A), PanelBottom = Col.From(0x0D0F22);
    public Col EdgeTop = Col.From(0x3BE8FF), EdgeBottom = Col.From(0x2BB8D0);
    public float EdgeWidth = 2f, PanelRadius = 24f, GlowStrength = 0f;

    // Accent
    public Col Accent = Col.From(0x3BE8FF), Accent2 = Col.From(0x2BB8D0);

    // Buttons
    public float ButtonRadius = 14f;
    public Col ButtonFill = Col.From(0x252A52, 0.92f), ButtonFillHover = Col.From(0x2F3668), ButtonFillPressed = Col.From(0x3B4585);
    public Col ButtonEdge = Col.From(0xFFFFFF, 0f), ButtonEdgeHover = Col.From(0x3BE8FF, 0.75f);
    public float ButtonEdgeWidth = 1.5f;

    // Text
    public Col Text = Col.From(0xE9ECFF), SubText = Col.From(0x8C95C6);
    public int LabelStyle, TitleStyle = 1;     // Unity FontStyles: 0 normal, 1 bold, 8 uppercase, 64 small caps
    public float LabelSpacing = 0.5f, TitleSpacing = 1f;

    // Behaviour / shape
    public int Layout;                         // 0 list, 1 grid
    public int Hover;                          // 0 grow, 1 slide, 2 glow
    public float RowHeight = 56f, RowSpacing = 8f, LabelSize = 21f, ValueSize = 15f, Flicker;
    public int EntranceOverride = -1;          // -1 = the player's setting
    public int Style;                          // menu type: Classic, Pillars, Console, Checklist, Switchboard, Cards, Book, Signboard
    public int Pattern;                        // 0 none, 1 wood, 2 scanlines, 3 grid, 4 paper
    public Col PatternColor = Col.From(0xFFFFFF, 0.06f);
    public float Depth = 12f, ButtonDepth = 6f, Bevel = 1f, BevelLight = 1f, BevelShadow = 1f;

    // Status lights
    public Col StatusIdle = Col.From(0x5A6290), StatusBusy = Col.From(0xFFB547);
    public Col StatusOk = Col.From(0x45E08A), StatusError = Col.From(0xFF4D6A);

    // ------------------------------------------------------------------ files

    public static readonly string[] Styles = { "Classic", "Pillars", "Console", "Checklist", "Switchboard", "Cards", "Book", "Signboard" };
    public static readonly string[] Patterns = { "None", "Wood", "Scanlines", "Grid", "Paper" };
    public static readonly string[] Hovers = { "Grow", "Slide", "Glow" };
    public static readonly string[] Layouts = { "List", "Grid" };
    public static readonly string[] Entrances = { "Menu setting", "Fade", "Pop", "Slide", "Staggered", "Cascade" };

    /// <summary>Where the menu looks for designed themes.</summary>
    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AssetBay", "themes");

    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true, WriteIndented = true };

    public static string PathFor(string name) => Path.Combine(Folder, Safe(name) + ".json");

    public static string Safe(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        name = name.Trim();
        return name.Length == 0 ? "theme" : name;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathFor(DisplayName), JsonSerializer.Serialize(this, Options));
    }

    public static ThemeFile Load(string path) =>
        JsonSerializer.Deserialize<ThemeFile>(File.ReadAllText(path), Options) ?? new ThemeFile();

    public static List<string> All()
    {
        try { return Directory.Exists(Folder) ? new List<string>(Directory.GetFiles(Folder, "*.json")) : new List<string>(); }
        catch { return new List<string>(); }
    }

    public ThemeFile Copy() => JsonSerializer.Deserialize<ThemeFile>(JsonSerializer.Serialize(this, Options), Options)!;

    /// <summary>Start from one of the launcher's own palettes, so a new theme already looks like something.</summary>
    public static ThemeFile FromPalette(UI.LauncherTheme t) => new()
    {
        DisplayName = "My " + t.Name,
        PanelTop = new Col(t.PanelTop), PanelBottom = new Col(t.PanelBottom),
        EdgeTop = new Col(t.EdgeTop), EdgeBottom = new Col(t.EdgeBottom),
        Accent = new Col(t.Accent), Accent2 = new Col(t.Accent2),
        ButtonFill = new Col(t.Button), ButtonFillHover = new Col(t.ButtonHover), ButtonFillPressed = new Col(t.ButtonPressed),
        ButtonEdge = new Col(t.ButtonEdge), ButtonEdgeHover = new Col(t.Accent),
        Text = new Col(t.Text), SubText = new Col(t.SubText),
        StatusIdle = new Col(t.Idle), StatusBusy = new Col(t.Busy), StatusOk = new Col(t.Ok), StatusError = new Col(t.Error),
        PanelRadius = t.PanelRadius, ButtonRadius = t.ButtonRadius,
        TitleStyle = t.UpperCase ? 9 : 1, LabelStyle = t.UpperCase ? 8 : 0,
    };
}
