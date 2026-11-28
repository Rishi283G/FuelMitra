using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace FuelPro.Sync.Security;

/// <summary>
/// Provides DPAPI (Data Protection API) encryption for sensitive local credentials on Windows.
/// Falls back safely with a clear warning on non-Windows/test environments.
/// </summary>
public static class WindowsDpapiProtection
{
    private static readonly ILogger Logger = Log.ForContext(typeof(WindowsDpapiProtection));
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PyroSync.StationCredentials.Entropy.v1");

    /// <summary>
    /// Encrypts a sensitive plaintext string using Windows DPAPI scoped to the local machine/user.
    /// Returns a base64 encoded string with a DPAPI marker prefix.
    /// </summary>
    public static string Protect(string plaintext, DataProtectionScope scope = DataProtectionScope.CurrentUser)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var plainBytes = Encoding.UTF8.GetBytes(plaintext);
                var encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, scope);
                return "dpapi:" + Convert.ToBase64String(encryptedBytes);
            }
            else
            {
                Logger.Warning("Non-Windows OS detected; DPAPI is not available. Using encoded fallback.");
                return "plain:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to DPAPI-protect sensitive credential. Returning original value.");
            return plaintext;
        }
    }

    /// <summary>
    /// Decrypts a ciphertext string produced by Protect.
    /// If the string is not DPAPI-protected (e.g. legacy plaintext), returns it as-is.
    /// </summary>
    public static string Unprotect(string ciphertext, DataProtectionScope scope = DataProtectionScope.CurrentUser)
    {
        if (string.IsNullOrEmpty(ciphertext)) return string.Empty;

        if (ciphertext.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var base64 = ciphertext.Substring(6);
                    var encryptedBytes = Convert.FromBase64String(base64);
                    var plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, scope);
                    return Encoding.UTF8.GetString(plainBytes);
                }
                else
                {
                    Logger.Warning("Cannot unprotect DPAPI ciphertext on non-Windows OS.");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to unprotect DPAPI credential.");
                return string.Empty;
            }
        }

        if (ciphertext.StartsWith("plain:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var base64 = ciphertext.Substring(6);
                var plainBytes = Convert.FromBase64String(base64);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return ciphertext;
            }
        }

        // Legacy or unencrypted plaintext
        return ciphertext;
    }
}
