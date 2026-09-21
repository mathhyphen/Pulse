using System.Text.Json;
using PulseWin.Auth;
using PulseWin.Core;
using PulseWin.Storage;

namespace PulseWin.Providers;

/// <summary>
/// Reads Codex's usage — the allowance a ChatGPT Plus or Pro subscription
/// includes, which is what makes this the ring for "several GPT Plus accounts".
///
/// <para>
/// The path is the one Codex's own client uses: take the OAuth credentials Codex
/// already stored in <c>%USERPROFILE%\.codex\auth.json</c> and ask
/// <c>chatgpt.com/backend-api/wham/usage</c>, which answers with the account-wide
/// windows and any per-model limits. It is a plain HTTPS call with nothing left
/// running, so it is quick.
/// </para>
/// <para>
/// Two things to keep in mind. The endpoint is not public API — it is what the CLI
/// uses internally, and it can change without warning. And the stored access token
/// expires: Codex refreshes it while you are using Codex, but <b>nothing refreshes
/// it on our behalf</b>. Pulse falls back to a <c>codex app-server</c> process in
/// that case; this port does not, and says so instead, because spawning and
/// speaking JSON-RPC to the CLI is a large amount of machinery for a recovery path
/// that running <c>codex</c> once also clears.
/// </para>
/// </summary>
public static class CodexService
{
    private const string EndpointBase = "https://chatgpt.com/backend-api/wham";

    public static string AuthFile => Path.Combine(AppPaths.Home, ".codex", "auth.json");

    /// <summary>The credential pair the endpoint wants.</summary>
    private readonly record struct Credentials(string AccessToken, string AccountId);

    public static async Task<ProviderUsage> FetchAsync(
        MonitoredAccount account, CancellationToken cancellationToken = default)
    {
        Credentials? credentials;

        if (account.Key.IsPrimary)
        {
            credentials = LoadBorrowedCredentials();
        }
        else
        {
            // An account this app signed in to itself. There is no route to choose:
            // the app server and the stored CLI login both belong to whichever
            // account the CLI is signed in to, not this one.
            var stored = AccountCredentialStore.For(account.Key);
            if (stored is null || !stored.IsUsable)
                return ProviderUsage.Failed(account.Key, Unavailability.SignInRequired);

            if (stored.NeedsRenewal(DateTimeOffset.Now))
            {
                var (renewed, _) = await CodexDeviceLogin.RenewAsync(stored, cancellationToken);

                // A renewal that fails is a signed-out account, not a network error.
                // The remedy is the same and the reader can act on it, which is why
                // this reports SignInRequired rather than Unreachable.
                if (renewed is null)
                    return ProviderUsage.Failed(account.Key, Unavailability.SignInRequired);

                AccountCredentialStore.Set(account.Key, renewed);
                stored = renewed;
            }

            credentials = new Credentials(stored.AccessToken, stored.ServiceAccountId ?? "");
        }

        if (credentials is null)
            return ProviderUsage.Failed(account.Key, Unavailability.SignInRequired);

        var url = $"{EndpointBase}/usage";

        // The request follows the system proxy. The cost is that a tunnel dropping
        // a connection surfaces as a request failure, so a stumble is retried
        // before it becomes an error in the UI.
        HttpResult? result = null;
        for (var attempt = 0; attempt <= Http.RetryLimit; attempt++)
        {
            result = await Http.GetAsync(
                url,
                credentials.Value.AccessToken,
                20,
                // Sent only when there is one. An empty header is not the same as
                // no header: it names no account, and the service is free to answer
                // for whichever it likes — which on an added account would put the
                // *other* login's figures under this one's ring.
                [("ChatGPT-Account-Id", credentials.Value.AccountId)],
                cancellationToken);

            if (result is not null) break;

            if (attempt < Http.RetryLimit)
                await Task.Delay(Http.RetryDelayMs(attempt), cancellationToken);
        }

        if (result is null) return ProviderUsage.Failed(account.Key, Unavailability.Unreachable);

        var failure = result.Value.Status switch
        {
            System.Net.HttpStatusCode.OK => (Unavailability?)null,
            // The stored token has aged out.
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => Unavailability.SignInRequired,
            System.Net.HttpStatusCode.TooManyRequests => Unavailability.RateLimited,
            _ => Unavailability.ServerError,
        };
        if (failure is not null) return ProviderUsage.Failed(account.Key, failure.Value);

        var root = Json.TryParse(result.Value.Body);
        if (root is null) return ProviderUsage.Failed(account.Key, Unavailability.UnreadableReply);

        return Parse(root.Value, account.Key);
    }

    private static Credentials? LoadBorrowedCredentials()
    {
        try
        {
            if (!File.Exists(AuthFile)) return null;

            var root = Json.TryParse(File.ReadAllText(AuthFile));
            var tokens = root?.Obj("tokens");
            if (tokens is null) return null;

            var token = tokens.Value.Str("access_token");
            if (string.IsNullOrEmpty(token)) return null;

            return new Credentials(token, tokens.Value.Str("account_id") ?? "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The shape <c>wham/usage</c> returns.</summary>
    internal static ProviderUsage Parse(JsonElement root, AccountKey account)
    {
        var windows = new List<UsageWindow>();

        // Account-wide limits, which the server leaves unnamed.
        var spendReached = root.Obj("spend_control")?.Bool("reached") ?? false;

        if (root.Obj("rate_limit") is { } limit)
        {
            windows.AddRange(MarkingSpent(
                HttpWindows(limit, "codex", null),
                // These flags describe the *group*, so they are pinned to the
                // window actually up against its limit rather than smeared across
                // every window in the group.
                IsGroupSpent(limit)
                || root.Field("rate_limit_reached_type") is not null
                || spendReached));
        }

        // Then per-model limits, which it does name.
        foreach (var extra in root.Items("additional_rate_limits"))
        {
            if (extra.Obj("rate_limit") is not { } extraLimit) continue;

            var label = extra.Str("limit_name");
            var key = extra.Text("metered_feature") ?? label ?? "extra";
            windows.AddRange(MarkingSpent(HttpWindows(extraLimit, key, label), IsGroupSpent(extraLimit)));
        }

        var credits = root.Obj("credits");

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            ObservedAt = DateTimeOffset.Now,
            Unavailable = windows.Count == 0 ? Unavailability.NoLimitsReported : null,
            Plan = root.Str("plan_type") is { } plan ? PlanName(plan) : null,
            CreditBalance = credits?.Bool("unlimited") == true ? null : credits?.Str("balance"),
        };
    }

    /// <summary>
    /// <c>primary_window</c> and <c>secondary_window</c> are not tied to particular
    /// durations, and which windows exist depends on the plan — ChatGPT Pro has no
    /// 5-hour limit, only the tiers below it do. On a plan without one, the
    /// account-wide group reports its <i>weekly</i> window as primary and has no
    /// secondary at all, while a per-model group uses primary for its 5-hour
    /// window. So a window's kind comes from its <b>duration</b>, never from which
    /// slot it arrived in, and the rail draws however many come back rather than
    /// expecting a fixed pair.
    /// </summary>
    private static IEnumerable<UsageWindow> HttpWindows(JsonElement limit, string idPrefix, string? scope)
    {
        foreach (var slot in new[] { "primary_window", "secondary_window" })
        {
            if (limit.Obj(slot) is not { } node) continue;

            var percent = node.Num("used_percent");
            if (percent is null) continue;

            var seconds = node.Int("limit_window_seconds");
            var resets = node.Num("reset_at") is { } epoch ? SafeFromUnixSeconds((long)epoch) : null;

            yield return new UsageWindow
            {
                Id = $"{idPrefix}.{slot}",
                Kind = seconds is null ? WindowKind.Other : KindForSeconds(seconds.Value),
                OtherSeconds = seconds ?? 0,
                Scope = scope,
                UsedFraction = percent.Value / 100,
                WindowSeconds = seconds ?? 0,
                ResetsAt = resets,
            };
        }
    }

    /// <summary>
    /// Marks the group's most-used window as spent.
    ///
    /// Codex reports "limit reached" for a whole group, but a group can hold both a
    /// 5-hour and a weekly window and only one of them is the reason. Flagging the
    /// fullest one keeps the claim as precise as the data allows.
    /// </summary>
    private static IReadOnlyList<UsageWindow> MarkingSpent(IEnumerable<UsageWindow> windows, bool spent)
    {
        var list = windows.ToList();
        if (!spent || list.Count == 0) return list;

        var fullest = list.MaxBy(w => w.UsedFraction)!;
        return list
            .Select(w => w.Id == fullest.Id ? w.WithExhausted() : w)
            .ToList();
    }

    private static bool IsGroupSpent(JsonElement limit) =>
        limit.Bool("limit_reached") == true || limit.Bool("allowed") == false;

    private static WindowKind KindForSeconds(int seconds) => seconds switch
    {
        18_000 => WindowKind.FiveHour,
        604_800 => WindowKind.Weekly,
        _ => WindowKind.Other,
    };

    /// <summary>
    /// What the plan is actually called, from the identifier Codex reports.
    ///
    /// The API answers with an internal tier name — <c>prolite</c>, <c>plus</c> —
    /// which is not the name on the plan anywhere the user has seen it. Anything
    /// unrecognised is passed through as-is rather than blanked: a name we do not
    /// know is still better than no name, and it is the only clue left if OpenAI
    /// adds a tier.
    /// </summary>
    internal static string PlanName(string raw) => raw.ToLowerInvariant() switch
    {
        "free" => "Free",
        "go" => "Go",
        "plus" => "Plus",
        "pro" => "Pro",
        // Reported for the 5× Pro tier.
        "prolite" => "Pro 5x",
        "team" => "Team",
        "business" => "Business",
        "enterprise" => "Enterprise",
        "edu" => "Edu",
        _ => raw,
    };

    private static DateTimeOffset? SafeFromUnixSeconds(long seconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
