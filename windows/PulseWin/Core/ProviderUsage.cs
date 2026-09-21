namespace PulseWin.Core;

/// <summary>
/// Why a provider could not answer.
///
/// The messages are deliberately shared and provider-neutral where the cause is
/// shared. Pulse learned this the hard way: every <c>Unavailability</c> message
/// used to say "Codex", so Claude Code reported "Codex is rate limiting these
/// checks." Only the cases whose <i>remedy</i> names a tool may name one.
/// </summary>
public enum Unavailability
{
    /// <summary>No key pasted, and no file to borrow one from. Names no provider.</summary>
    ApiKeyMissing,

    /// <summary>The credential was rejected. Names no provider.</summary>
    ApiKeyRefused,

    RateLimited,
    ServerError,
    Unreachable,

    /// <summary>The reply arrived but did not have the shape we know how to read.</summary>
    UnreadableReply,

    /// <summary>The service answered, and its answer carried no limit at all.</summary>
    NoLimitsReported,

    /// <summary>
    /// The stored login is missing or has aged out, and nothing here can renew
    /// it. Names Codex, because signing in again is the remedy and `codex` is
    /// what the reader has to run.
    /// </summary>
    SignInRequired,

    /// <summary>
    /// The key works and the account has no running Coding Plan. Distinct from a
    /// refused key, and a different remedy: the vendor answers `500` — its
    /// generic number — with the sentence "当前用户不存在coding plan", and reading
    /// the code alone would send somebody looking for an outage.
    /// </summary>
    ZaiNoCodingPlan,

    /// <summary>The Codex helper process failed for a reason that is not authentication.</summary>
    CodexServerFailed,
}

public static class UnavailabilityText
{
    public static string Message(this Unavailability reason) => reason switch
    {
        Unavailability.ApiKeyMissing => "No key has been entered for this service yet.",
        Unavailability.ApiKeyRefused => "The service refused this key.",
        Unavailability.RateLimited => "The service is rate limiting these checks. It will be tried again shortly.",
        Unavailability.ServerError => "The service returned an error.",
        Unavailability.Unreachable => "Could not reach the service.",
        Unavailability.UnreadableReply => "The service answered in a shape this version does not recognise.",
        Unavailability.NoLimitsReported => "The service answered, and reported no limit.",
        Unavailability.SignInRequired => "The saved Codex login is missing or has expired. Run `codex` to sign in again.",
        Unavailability.ZaiNoCodingPlan => "This key works, but the account has no Coding Plan running. The plan is a subscription on the account, not a property of the key.",
        Unavailability.CodexServerFailed => "The Codex helper could not be started.",
        _ => "Unavailable.",
    };
}

/// <summary>Money left, as a number and the currency it is denominated in.</summary>
/// <remarks>
/// The currency is not decoration. Pulse keeps this alongside the display string
/// precisely so that a CNY balance is never compared against a USD one.
/// </remarks>
public readonly record struct CreditInfo(double Amount, string Currency);

/// <summary>
/// One provider's reading: either windows, or the reason there are none.
///
/// Ported from Pulse's <c>ProviderUsage</c>. The distinction that matters most
/// here is <see cref="Unavailable"/> being null versus set: a reading carrying
/// neither windows nor a balance is a failure to fall back from, while a reading
/// carrying a balance and no windows is a complete answer — which is exactly the
/// case DeepSeek's "balance only" mode produces, and the case Pulse shipped
/// wrong once.
/// </summary>
public sealed class ProviderUsage
{
    public required AccountKey Account { get; init; }

    public IReadOnlyList<UsageWindow> Windows { get; init; } = Array.Empty<UsageWindow>();

    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>Null when the fetch succeeded, even if it reported no windows.</summary>
    public Unavailability? Unavailable { get; init; }

    public string? Plan { get; init; }

    /// <summary>
    /// A display string, and sometimes prose — Codex's says "Unlimited". Nothing
    /// may be decided from it; <see cref="CreditRemaining"/> is the number.
    /// </summary>
    public string? CreditBalance { get; init; }

    public CreditInfo? CreditRemaining { get; init; }

    public bool IsLive => Unavailable is null;

    /// <summary>
    /// Whether this reading carries an answer at all.
    ///
    /// This is the test that replaced <c>!windows.isEmpty</c>, which was sound
    /// while every provider reported a percentage and wrong the moment one
    /// reported only money.
    /// </summary>
    public bool ReportsSomething => Windows.Count > 0 || CreditBalance is not null;

    public static ProviderUsage Failed(AccountKey account, Unavailability reason) => new()
    {
        Account = account,
        Unavailable = reason,
        ObservedAt = DateTimeOffset.Now,
    };

    /// <summary>The window closest to being spent, which is the one the ring should draw.</summary>
    public UsageWindow? Fullest =>
        Windows.Count == 0 ? null : Windows.MaxBy(w => w.IsExhausted ? 2.0 : w.UsedFraction);

    /// <summary>
    /// The percentage the ring shows, or null when there is no fraction to show.
    ///
    /// Deliberately blind to <see cref="CreditBalance"/>: a reading with money and
    /// no limit has no percentage, and inventing one is the thing this app does
    /// not do.
    /// </summary>
    public double? HeadlineFraction => Fullest?.UsedFraction;

    /// <summary>
    /// The figure for the rail, counted the way the reader asked for.
    /// </summary>
    /// <param name="remaining">
    /// True counts down — what is left. Kept as a parameter rather than read from
    /// settings here, so that the store stays free of the interface's choices and
    /// the two callers that draw a figure cannot disagree about which way it runs.
    /// </param>
    public string HeadlineText(bool remaining) =>
        Fullest is { } window ? window.PercentText(remaining) : "";

    /// <summary>Short text for the rail when there is no percentage: money, truncated rather than rounded.</summary>
    public string? RailMoney => CreditRemaining is { } credit
        ? MoneyFormat.RailText(credit.Amount, credit.Currency)
        : null;
}
