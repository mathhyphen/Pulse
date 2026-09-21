using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PulseWin.Core;

namespace PulseWin.Storage;

/// <summary>
/// An encrypted JSON file, one key per purpose.
/// </summary>
/// <remarks>
/// <para>
/// DPAPI at <see cref="DataProtectionScope.CurrentUser"/> ties the ciphertext to
/// this Windows account, so copying the file elsewhere yields nothing — the
/// counterpart of the CryptoKit boxes upstream writes with owner-only permissions.
/// </para>
/// <para>
/// The <c>purpose</c> string is passed to DPAPI as its optional entropy, which is
/// what upstream calls "a different derived key per purpose": a box written by one
/// store cannot be opened by another, so a bug that reads the key file where the
/// account file was meant fails to decrypt rather than appearing to succeed with
/// somebody else's data.
/// </para>
/// </remarks>
public static class EncryptedFile
{
    public static T? Load<T>(string path, string purpose) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;

            var cipher = Convert.FromBase64String(File.ReadAllText(path));
            var plain = ProtectedData.Unprotect(cipher, Entropy(purpose), DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(plain), Json.Options);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or IOException or JsonException)
        {
            // A store that will not decode is **not** an empty one. Upstream learned
            // this the hard way: treating an undecodable file as empty meant saving
            // one provider's key silently discarded every other provider's, and
            // reported success. Callers are told by the null, and none of them
            // overwrite on the strength of it without being asked to.
            return null;
        }
    }

    public static bool Save<T>(string path, string purpose, T value)
    {
        try
        {
            var plain = JsonSerializer.SerializeToUtf8Bytes(value, Json.Options);
            var cipher = ProtectedData.Protect(plain, Entropy(purpose), DataProtectionScope.CurrentUser);
            AppPaths.WriteAtomic(path, Convert.ToBase64String(cipher));
            return true;
        }
        catch (Exception e) when (e is CryptographicException or IOException)
        {
            return false;
        }
    }

    /// <summary>Whether a file exists but cannot be read, which is worth saying out loud.</summary>
    public static bool ExistsButUnreadable<T>(string path, string purpose) where T : class =>
        File.Exists(path) && Load<T>(path, purpose) is null;

    private static byte[] Entropy(string purpose) => Encoding.UTF8.GetBytes("PulseWin:" + purpose);
}
