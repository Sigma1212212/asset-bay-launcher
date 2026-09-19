using System.Text.Json;

namespace AssetBayLauncher;

/// <summary>
/// Checks the Asset Bay server (the Cloudflare Worker): is it up, and how much of each free-tier
/// allowance is used (GET /asset-bay/budget). At most once a minute unless forced.
/// </summary>
public sealed class BackendMonitor
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly string baseUrl;
    private DateTime next = DateTime.MinValue;
    private bool running;

    public bool? Healthy { get; private set; }
    public string HealthText { get; private set; } = "Checking...";
    public string Host => new Uri(baseUrl).Host;

    private readonly Dictionary<string, (long used, long limit)> usage = new();
    private readonly Dictionary<string, bool> paused = new();

    public BackendMonitor(string url) => baseUrl = url.TrimEnd('/') + "/";

    public async void RefreshAsync(bool force = false)
    {
        if (running || (!force && DateTime.UtcNow < next)) return;
        running = true;
        next = DateTime.UtcNow.AddMinutes(1);
        try
        {
            var started = DateTime.UtcNow;
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(baseUrl + "budget"));
            int ms = (int)(DateTime.UtcNow - started).TotalMilliseconds;
            var root = doc.RootElement;
            var u = root.GetProperty("usage");
            var l = root.GetProperty("limits");
            long Get(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out long n) ? n : 0;
            usage["requests"] = (Get(u, "requests"), Get(l, "requestsPerDay"));
            usage["doCalls"] = (Get(u, "doCalls"), Get(l, "doCallsPerDay"));
            usage["r2Reads"] = (Get(u, "r2Reads"), Get(l, "r2ReadsPerMonth"));
            foreach (var p in root.GetProperty("paused").EnumerateObject()) paused[p.Name] = p.Value.GetBoolean();
            Healthy = true;
            HealthText = AnyPaused ? "Paused (limit)" : $"Online · {ms} ms";
        }
        catch (Exception e)
        {
            Healthy = false;
            HealthText = e is TaskCanceledException ? "Timed out" : "Offline";
        }
        finally { running = false; }
    }

    public bool AnyPaused => paused.Values.Any(v => v);

    public float Fraction(string key) =>
        usage.TryGetValue(key, out var x) && x.limit > 0 ? Math.Clamp(x.used / (float)x.limit, 0f, 1f) : 0f;

    public float WorstUsage => usage.Count == 0 ? 0f : usage.Keys.Max(Fraction);

    public string Line(string key) =>
        usage.TryGetValue(key, out var x) ? $"{Short(x.used)} / {Short(x.limit)}" : "—";

    public string UsageSummary => usage.Count == 0 ? "—" : $"{WorstUsage * 100f:0}% used";

    /// <summary>null = unknown (server not reached), true = running, false = paused at its limit.</summary>
    public bool? Running(string pausedKey) =>
        Healthy != true ? null : !(paused.TryGetValue(pausedKey, out bool p) && p);

    private static string Short(long n) => n >= 1_000_000 ? $"{n / 1_000_000f:0.#}M" : n >= 1000 ? $"{n / 1000f:0.#}k" : n.ToString();
}
