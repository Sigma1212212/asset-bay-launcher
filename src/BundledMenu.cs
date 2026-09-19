using System.Reflection;

namespace AssetBayLauncher;

/// <summary>
/// The copy of BundleMenu.dll compiled into the launcher exe (see build-release.ps1).
/// It's the fallback when GitHub can't be reached, and it saves a download when it's already the latest.
/// </summary>
public static class BundledMenu
{
    private static readonly Lazy<byte[]?> Bytes = new(() => Read("BundledMenu.dll"));
    private static readonly Lazy<string?> Tag = new(() =>
    {
        var raw = Read("BundledMenu.version");
        return raw == null ? null : System.Text.Encoding.UTF8.GetString(raw).Trim();
    });

    private static readonly Lazy<string?> Signature = new(() =>
    {
        var raw = Read("BundledMenu.sig");
        return raw == null ? null : System.Text.Encoding.UTF8.GetString(raw).Trim();
    });

    public static bool Available => Bytes.Value is { Length: > 0 };

    /// <summary>Release tag the built-in copy came from, e.g. "v1.0.0".</summary>
    public static string Version => Tag.Value is { Length: > 0 } t ? t : "built-in";

    /// <summary>
    /// Verifies the built-in DLL's signature and writes it to the cache, rewriting the file whenever its
    /// contents differ from what's embedded (so a swapped file on disk is never injected).
    /// </summary>
    public static string Extract()
    {
        var bytes = Bytes.Value ?? throw new InvalidOperationException("This launcher was built without a built-in menu.");
        ReleaseSignature.Verify(bytes, Signature.Value, $"The built-in menu {Version}");

        string path = Path.Combine(ReleaseUpdater.CacheRoot, "built-in", Version, "BundleMenu.dll");
        bool same = File.Exists(path) &&
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))
                        .AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(bytes));
        if (!same)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        return path;
    }

    private static byte[]? Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
