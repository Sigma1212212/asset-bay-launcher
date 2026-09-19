using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace AssetBayLauncher;

public sealed record ReleaseInfo(string Tag, string Name, string DownloadUrl, long Size, string? Sha256, string? ChecksumUrl,
    string? SignatureUrl, DateTimeOffset Published);

/// <summary>
/// Finds the newest release of the configured repository and keeps a verified copy of its DLL in
/// %LOCALAPPDATA%\AssetBayLauncher\cache\&lt;tag&gt;\. A download is only used after its SHA-256 matches:
/// GitHub's own asset digest when available, otherwise a "&lt;asset&gt;.sha256" file in the same release
/// (publish.ps1 uploads one).
/// </summary>
public sealed class ReleaseUpdater
{
    private static readonly HttpClient Http = CreateClient();
    private readonly LauncherConfig config;

    public ReleaseUpdater(LauncherConfig config) => this.config = config;

    public static string CacheRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AssetBayLauncher", "cache");

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AssetBayLauncher", "1.0"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public async Task<ReleaseInfo> GetLatestAsync(CancellationToken ct = default)
    {
        if (!config.RepositoryConfigured)
            throw new InvalidOperationException("Set \"repository\" in launcher.json to your GitHub repo (owner/name).");

        // /releases/latest skips prereleases; the list endpoint is used when prereleases are wanted.
        string url = config.IncludePrereleases
            ? $"https://api.github.com/repos/{config.Repository}/releases?per_page=10"
            : $"https://api.github.com/repos/{config.Repository}/releases/latest";

        using var response = await Http.GetAsync(url, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException($"No published release found in {config.Repository}.");
        if ((int)response.StatusCode == 403)
            throw new InvalidOperationException("GitHub rate limit reached - try again in a few minutes.");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        JsonElement release = doc.RootElement;
        if (release.ValueKind == JsonValueKind.Array)
        {
            release = release.EnumerateArray().FirstOrDefault(r => !r.GetProperty("draft").GetBoolean());
            if (release.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException($"No published release found in {config.Repository}.");
        }

        string tag = release.GetProperty("tag_name").GetString() ?? "unknown";
        string name = release.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : tag;
        var published = release.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(p.GetString()!) : DateTimeOffset.MinValue;

        JsonElement? dll = null, checksum = null, signature = null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            string assetName = asset.GetProperty("name").GetString() ?? "";
            if (assetName.Equals(config.AssetName, StringComparison.OrdinalIgnoreCase)) dll = asset;
            else if (assetName.Equals(config.AssetName + ".sha256", StringComparison.OrdinalIgnoreCase)) checksum = asset;
            else if (assetName.Equals(config.AssetName + ".sig", StringComparison.OrdinalIgnoreCase)) signature = asset;
        }
        if (dll is null)
            throw new InvalidOperationException($"Release {tag} has no {config.AssetName} attached.");

        string? digest = dll.Value.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        string? sha = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;

        return new ReleaseInfo(tag, name,
            dll.Value.GetProperty("browser_download_url").GetString()!,
            dll.Value.GetProperty("size").GetInt64(),
            sha,
            checksum?.GetProperty("browser_download_url").GetString(),
            signature?.GetProperty("browser_download_url").GetString(),
            published);
    }

    public string CachedPath(ReleaseInfo release) =>
        Path.Combine(CacheRoot, SafeFolder(release.Tag), config.AssetName);

    public bool IsCached(ReleaseInfo release) => File.Exists(CachedPath(release));

    private string SignaturePath(ReleaseInfo release) => CachedPath(release) + ".sig";

    /// <summary>Returns the path of a verified local copy, downloading it first if needed.</summary>
    public async Task<string> EnsureDownloadedAsync(ReleaseInfo release, IProgress<float> progress, CancellationToken ct = default)
    {
        string expected = release.Sha256 ?? await FetchChecksumFileAsync(release, ct)
            ?? throw new InvalidOperationException(
                "This release has no SHA-256 to check the download against. Re-publish it with publish.ps1.");

        string target = CachedPath(release);
        string sigPath = SignaturePath(release);

        // The signature is fetched every time (it's tiny) so a cached DLL is re-checked against the release.
        if (release.SignatureUrl is null)
            throw new InvalidOperationException($"Release {release.Tag} isn't signed, so it wasn't injected.");
        string signature = (await Http.GetStringAsync(release.SignatureUrl, ct)).Trim();

        if (File.Exists(target) && HashFile(target).Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            ReleaseSignature.Verify(await File.ReadAllBytesAsync(target, ct), signature, $"{config.AssetName} {release.Tag}");
            await File.WriteAllTextAsync(sigPath, signature, ct);
            progress.Report(1f);
            return target;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temp = target + ".download";

        using (var response = await Http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.Size;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(temp);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress.Report((float)done / total);
            }
        }

        string actual = HashFile(temp);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temp);
            throw new InvalidOperationException($"Download failed its integrity check (SHA-256 mismatch). Nothing was injected.");
        }

        try
        {
            ReleaseSignature.Verify(await File.ReadAllBytesAsync(temp, ct), signature, $"{config.AssetName} {release.Tag}");
        }
        catch
        {
            File.Delete(temp);
            throw;
        }

        File.Move(temp, target, overwrite: true);
        await File.WriteAllTextAsync(sigPath, signature, ct);
        PruneOldVersions(keep: SafeFolder(release.Tag));
        progress.Report(1f);
        return target;
    }

    private async Task<string?> FetchChecksumFileAsync(ReleaseInfo release, CancellationToken ct)
    {
        if (release.ChecksumUrl is null) return null;
        string text = await Http.GetStringAsync(release.ChecksumUrl, ct);
        string first = text.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length == 64 && first.All(Uri.IsHexDigit) ? first : null;
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void PruneOldVersions(string keep)
    {
        try
        {
            var old = new DirectoryInfo(CacheRoot).GetDirectories()
                .Where(d => d.Name != keep)
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .Skip(1); // keep one previous version to roll back to
            foreach (var dir in old) dir.Delete(recursive: true);
        }
        catch { /* cache cleanup is best-effort */ }
    }

    private static string SafeFolder(string tag) =>
        string.Concat(tag.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
