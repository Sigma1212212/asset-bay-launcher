using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AssetBayLauncher;

/// <summary>
/// Signs you in to your own Spotify account and saves the result where the menu can read it
/// (%APPDATA%\AssetBay\spotify.json). Uses the "PKCE" sign-in, so no app secret is needed or stored,
/// and the browser sends the answer back to a one-shot local address on this PC. Nothing goes near the
/// Asset Bay server, and you can sign out again here.
/// </summary>
public sealed class SpotifyLink
{
    private const string Scopes = "user-read-playback-state user-modify-playback-state user-read-currently-playing playlist-read-private";
    private const int Port = 8899;
    private static readonly string Redirect = $"http://127.0.0.1:{Port}/callback";

    public sealed class Tokens
    {
        public string clientId { get; set; } = "";
        public string refreshToken { get; set; } = "";
        public string accessToken { get; set; } = "";
        public long expiresAt { get; set; }
        public string account { get; set; } = "";
    }

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AssetBay", "spotify.json");

    public Tokens? Current { get; private set; }
    public string Status { get; private set; } = "not signed in";
    public bool Busy { get; private set; }
    public bool SignedIn => Current != null && !string.IsNullOrEmpty(Current.refreshToken);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public SpotifyLink() => Load();

    public void Load()
    {
        try
        {
            Current = File.Exists(FilePath) ? JsonSerializer.Deserialize<Tokens>(File.ReadAllText(FilePath)) : null;
            Status = SignedIn ? $"signed in{(string.IsNullOrEmpty(Current!.account) ? "" : " as " + Current.account)}" : "not signed in";
        }
        catch (Exception e) { Status = "couldn't read the sign-in: " + e.Message; }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Json));
    }

    public void SignOut()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { /* already gone */ }
        Current = null;
        Status = "not signed in";
    }

    /// <summary>
    /// Opens Spotify's sign-in page in your browser and waits for it to come back. `clientId` comes from
    /// an app you make at developer.spotify.com (free), with this exact redirect: http://127.0.0.1:8899/callback
    /// </summary>
    public async Task<bool> SignInAsync(string clientId, Action<string> report)
    {
        if (Busy) return false;
        Busy = true;
        HttpListener? listener = null;
        try
        {
            clientId = clientId.Trim();
            if (clientId.Length < 10) { Status = "paste your Spotify app's Client ID first"; return false; }

            string verifier = RandomText(64);
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string state = RandomText(16);

            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            listener.Start();

            string url = "https://accounts.spotify.com/authorize" +
                         $"?client_id={Uri.EscapeDataString(clientId)}&response_type=code" +
                         $"&redirect_uri={Uri.EscapeDataString(Redirect)}" +
                         $"&code_challenge_method=S256&code_challenge={challenge}" +
                         $"&state={state}&scope={Uri.EscapeDataString(Scopes)}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            Status = "waiting for the browser...";
            report(Status);

            // Wait for Spotify to send the browser back here (two minutes is plenty).
            var context = await WithTimeout(listener.GetContextAsync(), TimeSpan.FromMinutes(2));
            if (context == null) { Status = "sign-in timed out"; return false; }

            string? code = context.Request.QueryString["code"];
            string? gotState = context.Request.QueryString["state"];
            string? error = context.Request.QueryString["error"];
            await Reply(context, error == null && code != null && gotState == state
                ? "<h2>Signed in.</h2><p>You can close this tab and go back to Asset Bay.</p>"
                : $"<h2>Sign-in failed.</h2><p>{error ?? "unexpected answer"}</p>");

            if (error != null) { Status = "Spotify said: " + error; return false; }
            if (code == null || gotState != state) { Status = "unexpected answer from Spotify"; return false; }

            // Swap the code for tokens.
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = Redirect,
                ["client_id"] = clientId,
                ["code_verifier"] = verifier,
            });
            var response = await Http.PostAsync("https://accounts.spotify.com/api/token", form);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) { Status = "Spotify refused the sign-in: " + Short(body); return false; }

            using var doc = JsonDocument.Parse(body);
            Current = new Tokens
            {
                clientId = clientId,
                refreshToken = doc.RootElement.GetProperty("refresh_token").GetString() ?? "",
                accessToken = doc.RootElement.GetProperty("access_token").GetString() ?? "",
                expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + doc.RootElement.GetProperty("expires_in").GetInt32(),
            };
            Current.account = await WhoAmI(Current.accessToken);
            Save();
            Status = $"signed in{(string.IsNullOrEmpty(Current.account) ? "" : " as " + Current.account)}";
            return true;
        }
        catch (Exception e)
        {
            Status = "sign-in failed: " + e.Message;
            return false;
        }
        finally
        {
            try { listener?.Stop(); } catch { /* already stopped */ }
            Busy = false;
            report(Status);
        }
    }

    private static async Task<string> WhoAmI(string token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
            request.Headers.Add("Authorization", "Bearer " + token);
            var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return "";
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("display_name", out var name) ? name.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    private static async Task Reply(HttpListenerContext context, string html)
    {
        var bytes = Encoding.UTF8.GetBytes($"<html><body style='font-family:Segoe UI;background:#14161a;color:#d6dbe0;padding:40px'>{html}</body></html>");
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static async Task<T?> WithTimeout<T>(Task<T> task, TimeSpan timeout) where T : class =>
        await Task.WhenAny(task, Task.Delay(timeout)) == task ? await task : null;

    private static string RandomText(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var sb = new StringBuilder();
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";
        foreach (var b in bytes) sb.Append(alphabet[b % alphabet.Length]);
        return sb.ToString();
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string Short(string s) => s.Length <= 120 ? s : s[..120];
}
