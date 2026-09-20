using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetBayLauncher;

/// <summary>Settings stored in launcher.json next to the exe. Edit it by hand or from the launcher.</summary>
public sealed class LauncherConfig
{
    /// <summary>"owner/name" of the GitHub repository whose latest release holds the DLL.</summary>
    [JsonPropertyName("repository")] public string Repository { get; set; } = "YOUR-GITHUB-NAME/asset-bay";
    [JsonPropertyName("assetName")] public string AssetName { get; set; } = "BundleMenu.dll";
    [JsonPropertyName("processName")] public string ProcessName { get; set; } = "Gorilla Tag";
    [JsonPropertyName("entryNamespace")] public string EntryNamespace { get; set; } = "BundleMenu";
    [JsonPropertyName("entryClass")] public string EntryClass { get; set; } = "Loader";
    [JsonPropertyName("entryMethod")] public string EntryMethod { get; set; } = "Inject";
    [JsonPropertyName("ejectMethod")] public string EjectMethod { get; set; } = "Eject";

    /// <summary>A DLL on disk for testing a build before publishing it. Relative paths resolve from the exe folder.</summary>
    [JsonPropertyName("localDllPath")] public string LocalDllPath { get; set; } = "";

    [JsonPropertyName("theme")] public string Theme { get; set; } = "Halo";
    /// <summary>Match whatever theme the in-game menu last used.</summary>
    [JsonPropertyName("followGameTheme")] public bool FollowGameTheme { get; set; } = true;
    [JsonPropertyName("includePrereleases")] public bool IncludePrereleases { get; set; }
    // ---- look (all editable on the Settings page)
    /// <summary>"square", "soft" or "round".</summary>
    [JsonPropertyName("corners")] public string Corners { get; set; } = "square";
    /// <summary>"" = the theme's own accent, otherwise "#RRGGBB".</summary>
    [JsonPropertyName("accent")] public string Accent { get; set; } = "";
    /// <summary>"theme" or a font family name.</summary>
    [JsonPropertyName("font")] public string Font { get; set; } = "theme";
    /// <summary>"theme", "upper" or "normal".</summary>
    [JsonPropertyName("textCase")] public string TextCase { get; set; } = "theme";
    /// <summary>"flat", "gradient" or "grid".</summary>
    [JsonPropertyName("background")] public string Background { get; set; } = "flat";
    /// <summary>Your own Spotify app's Client ID (from developer.spotify.com). No secret is needed.</summary>
    [JsonPropertyName("spotifyClientId")] public string SpotifyClientId { get; set; } = "";
    [JsonPropertyName("depth")] public bool Depth { get; set; } = true;
    [JsonPropertyName("animations")] public bool Animations { get; set; } = true;
    [JsonPropertyName("showConsole")] public bool ShowConsole { get; set; } = true;
    [JsonPropertyName("alwaysOnTop")] public bool AlwaysOnTop { get; set; }

    /// <summary>The Asset Bay server (Cloudflare Worker) - shown on the Online page.</summary>
    [JsonPropertyName("backendUrl")] public string BackendUrl { get; set; } = "https://assetbay.randomthingsthatarecool.dev/asset-bay/";

    [JsonIgnore] public bool RepositoryConfigured =>
        Repository.Count(c => c == '/') == 1 && !Repository.StartsWith("YOUR-", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore] public string ResolvedLocalDll =>
        string.IsNullOrWhiteSpace(LocalDllPath) ? "" :
        Path.GetFullPath(Path.IsPathRooted(LocalDllPath) ? LocalDllPath : Path.Combine(AppContext.BaseDirectory, LocalDllPath));

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "launcher.json");

    public static LauncherConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(FilePath), Json) ?? new LauncherConfig();
        }
        catch (Exception e)
        {
            MessageBox.Show($"launcher.json could not be read, using defaults.\n\n{e.Message}", "Asset Bay Launcher");
        }
        return new LauncherConfig();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch { /* read-only install folder: settings just won't persist */ }
    }
}
