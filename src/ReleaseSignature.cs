using System.Security.Cryptography;

namespace AssetBayLauncher;

/// <summary>
/// Checks that a BundleMenu.dll was signed with the Asset Bay release key (ECDSA P-256, SHA-256).
///
/// The SHA-256 checksum published next to a release only proves the download wasn't corrupted - anyone
/// who could upload a DLL could upload a matching checksum. The signature proves the DLL came from whoever
/// holds the private key, which never leaves the publisher's PC. Only the public half is here.
/// </summary>
public static class ReleaseSignature
{
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEHHAV7ggqIWfcX3zAFpexQsFf+y5j
        Glc6E/o0hW+RWUMuN+lQA7VWU8HMUay+ge0HwSprIHxyr8O84P5uIPT+zQ==
        -----END PUBLIC KEY-----
        """;

    /// <summary>Throws with a readable message if the signature is missing, malformed or wrong.</summary>
    public static void Verify(byte[] data, string? signatureBase64, string what)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64))
            throw new InvalidOperationException($"{what} isn't signed, so it wasn't injected.");

        byte[] signature;
        try { signature = Convert.FromBase64String(signatureBase64.Trim()); }
        catch (FormatException) { throw new InvalidOperationException($"{what} has a malformed signature, so it wasn't injected."); }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(PublicKeyPem);
        if (!ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256))
            throw new InvalidOperationException($"{what} failed its signature check (not from the Asset Bay release key), so it wasn't injected.");
    }
}
