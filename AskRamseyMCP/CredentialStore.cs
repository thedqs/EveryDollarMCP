using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace AskRamseyMCP;

/// <summary>
/// Persists session credentials (SESSION cookie + CSRF token) to local storage,
/// encrypted with Windows DPAPI (tied to the current user account).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CredentialStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AskRamseyMCP", "credentials.dat");

    public record StoredCredentials(
        string CsrfToken,
        string SessionCookie,
        DateTime SavedAtUtc,
        DateTime? ExpiresAtUtc);

    /// <summary>
    /// Saves credentials encrypted with DPAPI (CurrentUser scope).
    /// </summary>
    public static void Save(string csrfToken, string sessionCookie, DateTime? expiresAtUtc = null)
    {
        var creds = new StoredCredentials(csrfToken, sessionCookie, DateTime.UtcNow, expiresAtUtc);
        var json = JsonSerializer.SerializeToUtf8Bytes(creds);
        var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);

        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllBytes(StorePath, encrypted);
    }

    /// <summary>
    /// Loads and decrypts stored credentials. Returns null if no credentials exist,
    /// decryption fails, or credentials have expired.
    /// </summary>
    public static StoredCredentials? Load()
    {
        if (!File.Exists(StorePath))
            return null;

        try
        {
            var encrypted = File.ReadAllBytes(StorePath);
            var json = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var creds = JsonSerializer.Deserialize<StoredCredentials>(json);

            if (creds is null)
                return null;

            // Check expiration
            if (creds.ExpiresAtUtc.HasValue && creds.ExpiresAtUtc.Value < DateTime.UtcNow)
            {
                Clear();
                return null;
            }

            return creds;
        }
        catch (CryptographicException)
        {
            // Decryption failed (different user, corrupted file, etc.)
            Clear();
            return null;
        }
        catch (JsonException)
        {
            Clear();
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes stored credentials.
    /// </summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(StorePath))
                File.Delete(StorePath);
        }
        catch (IOException)
        {
            // Best-effort deletion
        }
    }

    /// <summary>
    /// Returns true if stored credentials exist and are not expired.
    /// </summary>
    public static bool HasValidCredentials() => Load() is not null;
}
