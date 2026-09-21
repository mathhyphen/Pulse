using PulseWin.Core;
using PulseWin.Storage;

namespace PulseWin.Auth;

/// <summary>
/// One signed-in account's tokens.
/// </summary>
/// <remarks>
/// <para>
/// Held so that the account can <b>renew itself</b>, which is the whole reason for
/// signing in rather than copying the CLI's credential. Upstream measured a Codex
/// access token at roughly 240 hours and put the problem plainly: a copied
/// credential leaves the account you are not currently using dead within a couple
/// of weeks, and the only way to renew it is the refresh token the CLI is also
/// relying on — which, if the provider rotates it, signs the user out of their own
/// CLI. A login of our own has its own refresh token and touches nothing.
/// </para>
/// </remarks>
public sealed class AccountCredentials
{
    public string AccessToken { get; set; } = "";

    public string RefreshToken { get; set; } = "";

    /// <summary>When the access token stops being accepted, read from the reply rather than guessed.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The account the token was issued for. Codex's usage endpoint wants it in a header.</summary>
    public string? ServiceAccountId { get; set; }

    /// <summary>Whatever the reply said about who this is, so two subscriptions are not both called "Codex".</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Whether this is close enough to expiry to renew first.
    /// </summary>
    /// <remarks>
    /// A minute of slack, which is upstream's number. Renewing on the stroke of
    /// expiry means a request that leaves just before it lands just after.
    /// </remarks>
    public bool NeedsRenewal(DateTimeOffset now) => ExpiresAt - now < TimeSpan.FromMinutes(1);

    public bool IsUsable => AccessToken.Length > 0 && RefreshToken.Length > 0;
}

/// <summary>
/// The signed-in accounts, encrypted at rest — upstream calls this file
/// <c>accounts.dat</c>.
/// </summary>
/// <remarks>
/// Separate from <see cref="CredentialStore"/> (<c>keys.dat</c>) and encrypted with
/// a different purpose string, so a box from one cannot be opened by the other. The
/// two hold different kinds of thing: a pasted key that nobody renews, and a login
/// that renews itself.
/// </remarks>
public static class AccountCredentialStore
{
    private const string Purpose = "accounts";

    private static readonly object Gate = new();
    private static Dictionary<string, AccountCredentials>? _accounts;

    public static string Path => System.IO.Path.Combine(AppPaths.Directory, "accounts.dat");

    public static AccountCredentials? For(AccountKey key)
    {
        lock (Gate)
        {
            var accounts = _accounts ??= Load();
            return accounts.TryGetValue(key.ToString(), out var found) ? found : null;
        }
    }

    public static void Set(AccountKey key, AccountCredentials credentials)
    {
        lock (Gate)
        {
            var accounts = _accounts ??= Load();
            accounts[key.ToString()] = credentials;
            Persist(accounts);
        }
    }

    public static void Remove(AccountKey key)
    {
        lock (Gate)
        {
            var accounts = _accounts ??= Load();
            if (accounts.Remove(key.ToString())) Persist(accounts);
        }
    }

    private static Dictionary<string, AccountCredentials> Load() =>
        EncryptedFile.Load<Dictionary<string, AccountCredentials>>(Path, Purpose)
        ?? new Dictionary<string, AccountCredentials>();

    private static void Persist(Dictionary<string, AccountCredentials> accounts) =>
        EncryptedFile.Save(Path, Purpose, accounts);
}
