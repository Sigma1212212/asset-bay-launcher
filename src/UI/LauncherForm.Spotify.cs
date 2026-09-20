namespace AssetBayLauncher.UI;

/// <summary>
/// The Spotify page: sign in once here, and the in-game board and menu can show your music. Your
/// tokens stay on this PC (%APPDATA%\AssetBay\spotify.json) and never touch the Asset Bay server.
/// </summary>
public sealed partial class LauncherForm
{
    private readonly SpotifyLink spotify = new();

    private void BuildSpotifyPage()
    {
        var p = (int)Page.Spotify;
        int order = 0;
        float w = ContentW, half = (w - 12) / 2f;

        Card(Page.Spotify, new RectangleF(ContentX, ContentY, half, 88f), order++, "Account",
            () => spotify.SignedIn ? (string.IsNullOrEmpty(spotify.Current!.account) ? "Signed in" : spotify.Current.account) : "Not signed in",
            () => spotify.Status,
            () => spotify.SignedIn ? StatusKind.Ok : StatusKind.Idle);

        Card(Page.Spotify, new RectangleF(ContentX + half + 12, ContentY, half, 88f), order++, "Where it shows",
            () => "In game", () => "Library > Spotify, or the floating board from that page");

        float y = ContentY + 100f;
        widgets.Add(new Widget
        {
            Rect = new RectangleF(ContentX, y, half, 40f), Page = p, Order = order++,
            Label = "Client ID", Value = () => Mask(config.SpotifyClientId),
            Click = () =>
            {
                if (!AskText("Spotify Client ID", config.SpotifyClientId, out var id)) return;
                config.SpotifyClientId = id.Trim();
                config.Save();
                SetStatus("Client ID saved. Now press Sign in.", StatusKind.Idle);
            },
        });
        widgets.Add(new Widget
        {
            Rect = new RectangleF(ContentX + half + 12, y, half, 40f), Page = p, Order = order++, Primary = true,
            Label = "Sign in", Value = () => "",
            Enabled = () => !spotify.Busy && config.SpotifyClientId.Trim().Length > 8,
            Click = () => SignInAsync(),
        });
        y += 48f;
        widgets.Add(new Widget
        {
            Rect = new RectangleF(ContentX, y, half, 36f), Page = p, Order = order++, Small = true,
            Label = "Open Spotify's dashboard", Click = () => Open("https://developer.spotify.com/dashboard"),
        });
        widgets.Add(new Widget
        {
            Rect = new RectangleF(ContentX + half + 12, y, half, 36f), Page = p, Order = order++, Small = true,
            Label = "Sign out", Enabled = () => spotify.SignedIn,
            Click = () => { spotify.SignOut(); SetStatus("Signed out of Spotify on this PC.", StatusKind.Idle); },
        });

        // How to get a Client ID, in plain steps.
        widgets.Add(new Widget
        {
            Rect = new RectangleF(ContentX, y + 46f, w, ConsoleTop - (y + 46f) - 12f), Page = p, Order = order,
            Label = "First time", Card = true, Info = true, Value = () => "",
            Sub = () => "1. Open Spotify's dashboard above and log in.    2. Create app - any name.    " +
                        "3. Add this exact Redirect URI:  http://127.0.0.1:8899/callback    " +
                        "4. Copy the app's Client ID into the box on the left, then press Sign in.    " +
                        "Reading what's playing works on any account; play, pause and skip need Spotify Premium.",
        });
    }

    private async void SignInAsync()
    {
        SetStatus("Opening Spotify in your browser...", StatusKind.Busy);
        await spotify.SignInAsync(config.SpotifyClientId, message => SetStatus(message, spotify.SignedIn ? StatusKind.Ok : StatusKind.Error));
        Invalidate();
    }

    private static void Open(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    private static string Mask(string id) =>
        string.IsNullOrWhiteSpace(id) ? "not set" : id.Length <= 6 ? id : id[..4] + "…" + id[^2..];
}
