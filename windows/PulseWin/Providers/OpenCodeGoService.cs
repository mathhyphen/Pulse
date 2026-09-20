using System.Globalization;
using System.Text.Json;
using PulseWin.Core;
using PulseWin.Storage;

namespace PulseWin.Providers;

/// <summary>
/// The OpenCode Go plan's limits.
///
/// <para>
/// Two places the key can come from, in this order:
/// </para>
/// <list type="number">
/// <item>
/// <b>A key pasted into Settings.</b> It wins, because someone who typed a key
/// meant that one to be used — otherwise a stale key left behind by OpenCode
/// would quietly override a deliberate choice.
/// </item>
/// <item>
/// <b>What OpenCode saved for itself</b> in its own <c>auth.json</c>, which means
/// anyone already signed in there has nothing to configure.
/// </item>
/// </list>
/// <para>
/// The endpoint is <c>GET /zen/go/v1/usage</c>, which is <b>not documented</b> —
/// OpenCode's own docs describe only the model endpoints — so it can change
/// without notice.
/// </para>
/// </summary>
public static class OpenCodeGoService
{
    private const string Endpoint = "https://opencode.ai/zen/go/v1/usage";

    /// <summary>
    /// Where OpenCode keeps the key it wrote when it signed in.
    ///
    /// Pulse has one path, because macOS has one convention. Windows does not:
    /// OpenCode follows an XDG-style layout on some installs and an
    /// <c>%APPDATA%</c> layout on others, so all the plausible homes are listed
    /// and the first one that exists wins. Reading a path that is not there costs
    /// nothing; missing the one that is costs the whole provider.
    /// </summary>
    private static IEnumerable<string> AuthFileCandidates()
    {
        var home = AppPaths.Home;
        yield return Path.Combine(home, ".local", "share", "opencode", "auth.json");
        yield return Path.Combine(home, ".config", "opencode", "auth.json");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode", "auth.json");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode", "auth.json");
        yield return Path.Combine(home, ".opencode", "auth.json");
    }

    /// <summary>The key <c>opencode</c> wrote when it signed in.</summary>
    public static string? StoredKey()
    {
        foreach (var path in AuthFileCandidates())
        {
            try
            {
                if (!File.Exists(path)) continue;

                var root = Json.TryParse(File.ReadAllText(path));
                if (root is null) continue;

                var entry = root.Value.Field("opencode-go");
                if (entry is null) continue;

                // The documented-by-observation shape is { "opencode-go": { "key": … } }.
                var key = entry.Value.ValueKind switch
                {
                    JsonValueKind.String => entry.Value.GetString(),
                    JsonValueKind.Object => DeepSeekService.FirstNonEmpty(
                        entry.Value.Str("key"), entry.Value.Str("apiKey"), entry.Value.Str("access_token")),
                    _ => null,
                };

                if (!string.IsNullOrEmpty(key)) return key;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // An unreadable candidate is skipped rather than fatal, so one
                // locked file cannot hide a working key in the next location.
            }
        }

        return null;
    }

    public static async Task<ProviderUsage> FetchAsync(
        AccountKey account, string? enteredKey, CancellationToken cancellationToken = default)
    {
        var key = DeepSeekService.FirstNonEmpty(enteredKey, StoredKey());
        if (key is null) return ProviderUsage.Failed(account, Unavailability.ApiKeyMissing);

        var result = await Http.GetAsync(Endpoint, key, 15, cancellationToken: cancellationToken);
        if (result is null) return ProviderUsage.Failed(account, Unavailability.Unreachable);

        var failure = result.Value.Status switch
        {
            System.Net.HttpStatusCode.OK => (Unavailability?)null,
            // Not SignInRequired: that message names Codex, and a message that
            // names the wrong provider is the exact trap this port inherited a
            // fix for.
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => Unavailability.ApiKeyRefused,
            System.Net.HttpStatusCode.TooManyRequests => Unavailability.RateLimited,
            _ => Unavailability.ServerError,
        };
        if (failure is not null) return ProviderUsage.Failed(account, failure.Value);

        var root = Json.TryParse(result.Value.Body);
        if (root is null) return ProviderUsage.Failed(account, Unavailability.UnreadableReply);

        var windows = Windows(root.Value);
        if (windows.Count == 0) return ProviderUsage.Failed(account, Unavailability.NoLimitsReported);

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            ObservedAt = DateTimeOffset.Now,
            // The reply carries limits and nothing else — no plan name, no
            // balance — so neither is invented here.
            Plan = null,
            CreditBalance = null,
        };
    }

    /// <summary>
    /// Shortest window first, which is the order the other providers' limits
    /// arrive in and the order they matter in — the one about to bite leads.
    /// </summary>
    private static IReadOnlyList<UsageWindow> Windows(JsonElement root)
    {
        var usage = root.Obj("usage");
        if (usage is null) return Array.Empty<UsageWindow>();

        var windows = new List<UsageWindow>();
        Add(windows, usage.Value, "rolling", WindowKind.FiveHour, 5 * 3_600);
        Add(windows, usage.Value, "weekly", WindowKind.Weekly, 7 * 86_400);
        Add(windows, usage.Value, "monthly", WindowKind.Monthly, 30 * 86_400);
        return windows;
    }

    /// <summary>
    /// The reply calls the short window "rolling" and never says how long it runs,
    /// but it is the five-hour one — measured, the reset it reports lands five
    /// hours out. So it is named as such rather than by the key it arrives under;
    /// the key stays the id, which is what a pinned window is matched on.
    ///
    /// <paramref name="seconds"/> orders the rows. Only the reset stamp is ever
    /// displayed, so these are the nominal lengths the names imply and
    /// <c>ReportsLength</c> stays false.
    /// </summary>
    private static void Add(List<UsageWindow> into, JsonElement usage, string id, WindowKind kind, int seconds)
    {
        var reported = usage.Obj(id);
        if (reported is null) return;

        var percent = reported.Value.Num("percent");
        if (percent is null) return;

        into.Add(new UsageWindow
        {
            Id = id,
            Kind = kind,
            Scope = null,
            UsedFraction = Math.Clamp(percent.Value / 100, 0, 1),
            WindowSeconds = seconds,
            ResetsAt = ParseStamp(reported.Value.Str("resetsAt")),
            // Sort key, not a stated length. Only the reset stamp is displayed,
            // and the window clock must not divide by a figure nobody reported.
            ReportsLength = false,
            // The provider's own verdict, not one inferred from the percentage.
            // Anything other than "ok" is treated as spent — erring towards
            // "you're blocked" is the safer way to be wrong.
            IsExhausted = !string.Equals(reported.Value.Str("status") ?? "ok", "ok", StringComparison.OrdinalIgnoreCase),
        });
    }

    /// <summary>The stamps carry milliseconds; the fallback accepts ones that do not.</summary>
    private static DateTimeOffset? ParseStamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed;

        return null;
    }
}
