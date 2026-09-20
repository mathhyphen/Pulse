using System.Text.Json;
using PulseWin.Core;
using PulseWin.Storage;

namespace PulseWin.Providers;

/// <summary>
/// DeepSeek's prepaid balance.
///
/// <para>
/// One documented route, <c>GET https://api.deepseek.com/user/balance</c>,
/// reached with a key the user pastes into Settings. Unlike the other three this
/// is not an undocumented account endpoint borrowed from a product's own UI — it
/// sits in DeepSeek's published API reference, beside chat completions.
/// </para>
/// <code>
/// { "is_available": true,
///   "balance_infos": [ { "currency": "CNY", "total_balance": "110.00",
///                        "granted_balance": "10.00",
///                        "topped_up_balance": "100.00" } ] }
/// </code>
/// <para>
/// <b>There is no allowance, no window, no reset and no spend history</b> — not
/// in this reply and not anywhere else in the API. So the denominator behind the
/// ring has to come from somewhere, and <see cref="DeepSeekBasis"/> is the
/// enumeration of the only three places it can: something we watched, nothing at
/// all, or a figure the user typed.
/// </para>
/// <para>
/// Every figure arrives as a <b>string</b>, including the money, and is parsed
/// here so nothing downstream has to know that.
/// </para>
/// </summary>
public static class DeepSeekService
{
    private const string Endpoint = "https://api.deepseek.com/user/balance";

    /// <summary>One currency's money, with the strings turned into numbers.</summary>
    internal readonly record struct Purse(string Currency, double Total, double? Granted, double? ToppedUp);

    public static async Task<ProviderUsage> FetchAsync(
        AccountKey account, string? enteredKey, CancellationToken cancellationToken = default)
    {
        var key = FirstNonEmpty(enteredKey, CredentialStore.Key(Provider.DeepSeek));
        if (key is null) return ProviderUsage.Failed(account, Unavailability.ApiKeyMissing);

        var result = await Http.GetAsync(Endpoint, key, 15, cancellationToken: cancellationToken);
        if (result is null) return ProviderUsage.Failed(account, Unavailability.Unreachable);

        var failure = result.Value.Status switch
        {
            System.Net.HttpStatusCode.OK => (Unavailability?)null,
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => Unavailability.ApiKeyRefused,
            System.Net.HttpStatusCode.TooManyRequests => Unavailability.RateLimited,
            _ => Unavailability.ServerError,
        };
        if (failure is not null) return ProviderUsage.Failed(account, failure.Value);

        var root = Json.TryParse(result.Value.Body);
        if (root is null) return ProviderUsage.Failed(account, Unavailability.UnreadableReply);

        var settings = AppSettings.Current;
        var purse = SelectPurse(root.Value, settings.DeepSeekCurrency);
        if (purse is null) return ProviderUsage.Failed(account, Unavailability.NoLimitsReported);

        // The mark is advanced on every reading, whichever basis is in force:
        // switching to "since top-up" later should find a peak already there
        // rather than start over from whatever the balance happens to be this
        // afternoon.
        var mark = DeepSeekMarks.Observe(purse.Value.Currency, purse.Value.Total, DateTimeOffset.Now);

        return new ProviderUsage
        {
            Account = account,
            Windows = BuildWindows(
                purse.Value, settings.DeepSeekBasis, settings.DeepSeekBudget, mark,
                root.Value.Bool("is_available")),
            ObservedAt = DateTimeOffset.Now,
            // The reply carries a balance and nothing else — no plan name — so
            // none is invented.
            Plan = null,
            CreditBalance = MoneyFormat.Exact(purse.Value.Total, purse.Value.Currency),
            CreditRemaining = new CreditInfo(purse.Value.Total, purse.Value.Currency),
        };
    }

    // MARK: - Reading the reply

    /// <summary>
    /// Which currency the ring follows.
    ///
    /// <b>The reply is an array</b>, and an account can hold both CNY and USD.
    /// They cannot be added together and we will not pick a "main" one by
    /// comparing figures across currencies — ¥100 against $10 is not a
    /// comparison. So: the user's choice if they made one, else the first entry
    /// with money in it, else the first entry at all.
    /// </summary>
    internal static Purse? SelectPurse(JsonElement root, string? preferring)
    {
        var purses = root.Items("balance_infos")
            .Select(ToPurse)
            .Where(p => p is not null)
            .Select(p => p!.Value)
            .ToList();

        if (purses.Count == 0) return null;

        if (!string.IsNullOrEmpty(preferring))
        {
            var chosen = purses.FirstOrDefault(p =>
                string.Equals(p.Currency, preferring, StringComparison.OrdinalIgnoreCase));
            if (chosen.Currency is not null) return chosen;
        }

        var funded = purses.FirstOrDefault(p => p.Total > 0);
        return funded.Currency is not null ? funded : purses[0];
    }

    private static Purse? ToPurse(JsonElement info)
    {
        var currency = info.Text("currency");
        var total = info.Money("total_balance");
        if (currency is null || total is null) return null;

        return new Purse(currency, total.Value, info.Money("granted_balance"), info.Money("topped_up_balance"));
    }

    // MARK: - Mapping

    /// <summary>
    /// At most one window, because there is at most one denominator.
    ///
    /// <c>BalanceOnly</c> produces none at all and the rail shows the money in
    /// place of a percentage. The other two produce a single balance row whose
    /// estimate names where its denominator came from, because that is the whole
    /// question a reader has about it.
    ///
    /// <b>No length and no reset, ever.</b> Prepaid credit does not turn over, so
    /// <c>ReportsLength</c> is false and the seconds exist only to sort the row.
    /// </summary>
    internal static IReadOnlyList<UsageWindow> BuildWindows(
        Purse purse, DeepSeekBasis basis, double? budget,
        DeepSeekMarks.Mark mark, bool? isAvailable)
    {
        var (fraction, estimate) = basis switch
        {
            DeepSeekBasis.BalanceOnly => ((double?, WindowEstimate?))(null, null),

            DeepSeekBasis.SinceTopUp => (
                DeepSeekMarks.UsedFraction(purse.Total, mark.Peak),
                WindowEstimate.SinceTopUp),

            DeepSeekBasis.Budget => (
                // **Finite, not merely positive.** An infinite denominator makes
                // the fraction NaN, which the clamps would propagate rather than
                // catch. Settings refuses one too; this is the guard that does not
                // depend on where the figure came from.
                budget is { } b && double.IsFinite(b) && b > 0
                    ? Math.Clamp((b - purse.Total) / b, 0, 1)
                    : null,
                WindowEstimate.YourBudget),

            _ => ((double?, WindowEstimate?))(null, null),
        };

        if (fraction is null) return Array.Empty<UsageWindow>();

        return new[]
        {
            new UsageWindow
            {
                Id = "balance",
                Kind = WindowKind.Balance,
                // **Not the scope.** Scope is a product name that the JSON
                // contract promises reads the same in every language, and "since
                // top-up" translated into it broke that the day it shipped.
                Scope = null,
                UsedFraction = fraction.Value,
                // Sort key only — ReportsLength below is what says so.
                WindowSeconds = 30 * 86_400,
                ResetsAt = null,
                ReportsLength = false,
                Estimate = estimate,
                // **DeepSeek's own word**, not the arithmetic: `is_available` is
                // the flag it sets when the balance can no longer pay for a call.
                // A budget the reader set low can reach 100% with money still in
                // the account, and that is not the account being spent.
                IsExhausted = isAvailable == false,
            },
        };
    }

    internal static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
}
