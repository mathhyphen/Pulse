using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PulseWin.Core;

namespace PulseWin.Storage;

/// <summary>
/// The keys the user pasted, encrypted at rest.
///
/// <para>
/// Pulse keeps these in <c>keys.dat</c>, encrypted with CryptoKit and written with
/// owner-only permissions. Windows has an equivalent that is better than a
/// hand-rolled scheme: <b>DPAPI</b> at <see cref="DataProtectionScope.CurrentUser"/>,
/// which ties the ciphertext to this Windows account so that copying the file to
/// another machine — or another user on this one — yields nothing.
/// </para>
/// <para>
/// Keys are read once per launch rather than once per refresh, which is Pulse's
/// arrangement and is also the one that keeps a decryption failure from blanking
/// a ring mid-session.
/// </para>
/// </summary>
public static class CredentialStore
{
    // Deliberately not the account keys used elsewhere: this is a flat map from
    // provider to the single key the user pasted for it. Added accounts carry
    // their own credentials in the account record, not here.
    private static readonly Dictionary<string, string> Keys = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    private static bool _loaded;

    /// <summary>Reads the store off disk. Safe to call more than once; the second call is a no-op.</summary>
    public static void Load()
    {
        lock (Gate)
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                if (!File.Exists(AppPaths.Secrets)) return;
                var cipher = Convert.FromBase64String(File.ReadAllText(AppPaths.Secrets));
                var plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    Encoding.UTF8.GetString(plain), Json.Options);

                if (map is null) return;
                foreach (var (key, value) in map)
                    Keys[key] = value;
            }
            catch (Exception e) when (e is CryptographicException or FormatException or IOException or JsonException)
            {
                // A store that cannot be decrypted is a store this machine did not
                // write — a restored profile, a copied file. Reporting every
                // provider as "no key entered" is correct and recoverable;
                // crashing on launch is not.
            }
        }
    }

    public static string? Key(Provider provider)
    {
        lock (Gate)
        {
            Load();
            return Keys.TryGetValue(provider.ToString(), out var value) && !string.IsNullOrEmpty(value)
                ? value
                : null;
        }
    }

    public static void Set(Provider provider, string? value)
    {
        lock (Gate)
        {
            Load();
            if (string.IsNullOrWhiteSpace(value))
                Keys.Remove(provider.ToString());
            else
                Keys[provider.ToString()] = value.Trim();

            Save();
        }
    }

    public static bool Has(Provider provider) => Key(provider) is not null;

    private static void Save()
    {
        try
        {
            var plain = JsonSerializer.SerializeToUtf8Bytes(Keys, Json.Options);
            var cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            AppPaths.WriteAtomic(AppPaths.Secrets, Convert.ToBase64String(cipher));
        }
        catch (Exception e) when (e is CryptographicException or IOException)
        {
            // Writing the store is best-effort: a locked file must not take the
            // app down while the user is still typing a key.
        }
    }
}
