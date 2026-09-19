using Microsoft.Win32;

namespace AssetBayLauncher.UI;

/// <summary>
/// The same palettes as the in-game menu (BundleMenu/Runtime/UI/MenuTheme.cs), so the launcher and
/// the menu look like one product. Keep the two in sync when you change colours.
/// </summary>
public sealed record LauncherTheme(
    string Name,
    Color PanelTop, Color PanelBottom,
    Color EdgeTop, Color EdgeBottom,
    Color Accent, Color Accent2,
    Color Button, Color ButtonHover, Color ButtonPressed, Color ButtonEdge,
    Color Text, Color SubText,
    Color Idle, Color Busy, Color Ok, Color Error,
    int PanelRadius, int ButtonRadius, bool UpperCase)
{
    private static Color H(int rgb, int a = 255) => Color.FromArgb(a, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    public static readonly LauncherTheme Halo = new("Halo",
        H(0x1B1F3A), H(0x0D0F22), H(0x3BE8FF), H(0x8A5CFF), H(0x3BE8FF), H(0x8A5CFF),
        H(0x252A52), H(0x2F3668), H(0x3B4585), H(0x3BE8FF, 0),
        H(0xE9ECFF), H(0x8C95C6), H(0x5A6290), H(0xFFB547), H(0x45E08A), H(0xFF4D6A), 22, 12, false);

    public static readonly LauncherTheme Solstice = new("Solstice",
        H(0xFFF7EE), H(0xFFE4CC), H(0xFF9A3D), H(0xFF4F7B), H(0xFF6A3D), H(0xFF4F7B),
        H(0xFFFFFF), H(0xFFF0E2), H(0xFFDCC2), H(0xFFC9A6),
        H(0x3B2A22), H(0x9A7A68), H(0xD9C2B0), H(0xF2A33A), H(0x2DBE72), H(0xE8334F), 30, 18, false);

    public static readonly LauncherTheme Circuit = new("Circuit",
        H(0x0A120E), H(0x040806), H(0x39FF88), H(0x00B894), H(0x39FF88), H(0x00C2A8),
        H(0x0F1F17), H(0x163324), H(0x1F4A33), H(0x39FF88, 90),
        H(0xC4FFDD), H(0x4FA97A), H(0x1F4A33), H(0xE8FF5A), H(0x39FF88), H(0xFF5A5A), 4, 2, true);

    public static readonly LauncherTheme Velvet = new("Velvet",
        H(0x2B0F20), H(0x13060E), H(0xF5D27A), H(0xA8742A), H(0xF2C14E), H(0xC98A2E),
        H(0x3A1529), H(0x4B1C36), H(0x5E2444), H(0xF2C14E, 70),
        H(0xFBEFD9), H(0xB98F86), H(0x6B3A52), H(0xF2C14E), H(0x7EE0A1), H(0xFF6B6B), 14, 9, false);

    public static readonly LauncherTheme Arcade = new("Arcade",
        H(0x2A1B5E), H(0x120A2E), H(0xFFD23F), H(0xFF3F81), H(0xFFD23F), H(0xFF3F81),
        H(0x3D2A8A), H(0x5A3FC0), H(0xFF3F81), H(0xFFD23F, 120),
        H(0xFFF6D6), H(0xC9B8FF), H(0x5A4A99), H(0xFFD23F), H(0x6BF2A0), H(0xFF3F81), 20, 14, true);

    public static readonly LauncherTheme Graphite = new("Graphite",
        H(0x17181B), H(0x0F1012), H(0x3A3D42), H(0x2A2C30), H(0xF2F2F2), H(0x9A9A9A),
        H(0x222428), H(0x2C2F34), H(0x383C42), H(0xFFFFFF, 30),
        H(0xEDEDED), H(0x8A8D93), H(0x4A4D52), H(0xE6B450), H(0x7BD88F), H(0xF0706A), 12, 8, false);

    public static readonly LauncherTheme Neon = new("Neon",
        H(0x0B0616), H(0x05020C), H(0xFF2BD6), H(0x22E4FF), H(0xFF2BD6), H(0x22E4FF),
        H(0x160B2C), H(0x22103F), H(0x2E1558), H(0x22E4FF, 110),
        H(0xF4E9FF), H(0x9A7FC2), H(0x3A2A5A), H(0xFFE45A), H(0x22E4FF), H(0xFF4D6A), 16, 12, false);

    public static readonly LauncherTheme Timber = new("Timber",
        H(0x5C3A21), H(0x3E2715), H(0x2A180B), H(0x1E1008), H(0xF4C95D), H(0xE08A3C),
        H(0x7A4E2D), H(0x8D5C36), H(0x9C6A40), H(0xF4C95D, 60),
        H(0xFBEBD0), H(0xD9BF98), H(0x8C6A4A), H(0xF2A33A), H(0x8BD16A), H(0xFF6B5A), 16, 12, true);

    public static readonly LauncherTheme Parchment = new("Parchment",
        H(0xF3E7C9), H(0xE6D3A8), H(0x6B3F22), H(0x4A2A15), H(0x8C2F1B), H(0x2F5D8C),
        H(0xFFF8E6), H(0xFFFDF4), H(0xE9D9B4), H(0x6B3F22, 70),
        H(0x2B1D12), H(0x7D6247), H(0xB9A27E), H(0xC98A2E), H(0x3E7D3A), H(0xA8322D), 16, 10, false);

    public static readonly LauncherTheme[] All = { Halo, Solstice, Circuit, Velvet, Arcade, Graphite, Neon, Timber, Parchment };

    /// <summary>The in-game ThemePreset enum index -> launcher palette (Custom/Glass have no twin).</summary>
    private static readonly Dictionary<int, LauncherTheme> GameIndex = new()
    {
        [0] = Halo, [1] = Solstice, [2] = Circuit, [3] = Velvet, [5] = Arcade, [6] = Halo,
        [7] = Graphite, [8] = Neon, [9] = Timber, [10] = Parchment,
    };

    public static LauncherTheme ByName(string? name) =>
        All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? Halo;

    public string Cased(string s) => UpperCase ? s.ToUpperInvariant() : s;

    /// <summary>
    /// Reads the theme the in-game menu last used. Unity stores PlayerPrefs for Windows builds under
    /// HKCU\Software\&lt;company&gt;\&lt;product&gt;, as "&lt;key&gt;_h&lt;hash&gt;" DWORD values. The menu saves "BundleMenu.theme"
    /// as the ThemePreset enum index (Halo=0, Solstice=1, Circuit=2, Velvet=3).
    /// </summary>
    public static LauncherTheme? FromGame(string company = "Another Axiom", string product = "Gorilla Tag")
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\{company}\{product}");
            if (key == null) return null;
            string? name = key.GetValueNames().FirstOrDefault(v => v.StartsWith("BundleMenu.theme_h", StringComparison.Ordinal));
            if (name == null || key.GetValue(name) is not int index) return null;
            return GameIndex.TryGetValue(index, out var t) ? t : null;
        }
        catch
        {
            return null;
        }
    }
}
