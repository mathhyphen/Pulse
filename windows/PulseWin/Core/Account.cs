using System.Globalization;
using System.Text.Json.Serialization;
using PulseWin.Localization;

namespace PulseWin.Core;

/// <summary>
/// Identifies one monitored account.
///
/// <para>
/// Ported from Pulse's <c>AccountKey</c>. The rule that matters for upgrades and
/// for the cache: <b>a provider's first account id is the provider's own raw
/// value</b>, and added accounts get their own id. Making a change here look like
/// a fresh install has already cost the original project a release.
/// </para>
/// </summary>
public sealed record AccountKey(Provider Provider, string Id)
{
    /// <summary>The account every provider has, whose credential is the borrowed or pasted one.</summary>
    public static AccountKey Primary(Provider provider) => new(provider, provider.ToString());

    /// <summary>
    /// Whether this is the provider's borrowed-or-pasted account rather than one
    /// the user added.
    /// </summary>
    /// <remarks>
    /// Computed, and therefore kept out of the stored shape: a record serializes
    /// every public property, so without this the settings file grows a field that
    /// is derived from two others and can disagree with them if it is ever edited
    /// by hand.
    /// </remarks>
    [JsonIgnore]
    public bool IsPrimary => Id == Provider.ToString();

    public override string ToString() => $"{Provider}:{Id}";
}

/// <summary>One account on the rail: which service, what to call it, and whether it is switched on.</summary>
public sealed class MonitoredAccount
{
    public required AccountKey Key { get; init; }

    /// <summary>What the user calls it. Empty falls back to the provider's name.</summary>
    public string Label { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// For an added account, the OAuth token PulseWin holds for it.
    ///
    /// Added accounts are <b>endpoint-backed and carry their own credential</b> —
    /// the CLI login belongs to whichever account the CLI is signed in to, which
    /// is not this one. On the primary account this is null and the borrowed file
    /// is read instead.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>The account id the service should answer for. Sent as <c>ChatGPT-Account-Id</c>.</summary>
    public string? ServiceAccountId { get; set; }

    [JsonIgnore]
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(Label) ? Label : Key.Provider.DisplayName();
}

/// <summary>
/// Short money text for the rail, where the label is budgeted for "100%".
/// </summary>
/// <remarks>
/// Ported from Pulse's <c>CreditAmount.railText</c>, including the rule that it
/// is <b>truncated, never rounded</b>: a balance shown as more than it is is the
/// wrong way to be wrong. Cents survive below a hundred, where they are the part
/// somebody might be watching.
/// </remarks>
public static class MoneyFormat
{
    private static readonly Dictionary<string, string> NarrowSymbols = new()
    {
        ["CNY"] = "¥",
        ["USD"] = "$",
        ["EUR"] = "€",
        ["JPY"] = "¥",
        ["GBP"] = "£",
        ["HKD"] = "HK$",
    };

    public static string Symbol(string currency) =>
        NarrowSymbols.TryGetValue(currency.ToUpperInvariant(), out var symbol) ? symbol : currency + " ";

    public static string RailText(double amount, string currency)
    {
        var symbol = Symbol(currency);
        var value = Math.Abs(amount);
        var sign = amount < 0 ? "-" : "";

        // The tiers come from the language, not from taste: English groups by
        // thousands, so a hundred thousand reads "100k"; Chinese groups by ten
        // thousands, so the same figure reads "10万".
        foreach (var (threshold, divisor, suffix) in Loc.Current.MoneyTiers)
        {
            if (value >= threshold)
                return $"{sign}{symbol}{Truncate(value / divisor)}{suffix}";
        }

        if (value >= 100)
            return $"{sign}{symbol}{Math.Truncate(value).ToString(CultureInfo.InvariantCulture)}";
        if (value == Math.Truncate(value))
            return $"{sign}{symbol}{value.ToString("0", CultureInfo.InvariantCulture)}";

        // Below a hundred, the cents are the part somebody might be watching.
        return $"{sign}{symbol}{value.ToString("0.##", CultureInfo.InvariantCulture)}";
    }

    /// <summary>Truncated to one decimal place, never rounded up.</summary>
    private static string Truncate(double value)
    {
        var truncated = Math.Truncate(value * 10) / 10;
        return truncated == Math.Truncate(truncated)
            ? truncated.ToString("0", CultureInfo.InvariantCulture)
            : truncated.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>The exact figure, for the detail card and Settings.</summary>
    public static string Exact(double amount, string currency)
    {
        var symbol = Symbol(currency);
        return $"{symbol}{amount.ToString("#,##0.00", CultureInfo.InvariantCulture)}";
    }
}
