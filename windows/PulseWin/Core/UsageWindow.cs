namespace PulseWin.Core;

/// <summary>
/// What kind of window this is, kept as meaning rather than as text.
///
/// Ported from Pulse's <c>UsageWindow.Kind</c>. The names are built on demand in
/// <see cref="UsageWindow.Name"/> rather than stored, because a window is read
/// once when the provider reports it but may be displayed long after.
/// </summary>
public enum WindowKind
{
    FiveHour,
    Weekly,
    Spend,

    /// <summary>
    /// Prepaid credit, which is <b>not a limit</b>. DeepSeek sells money rather
    /// than an allowance: there is no ceiling to reach and no window to turn
    /// over, and calling it a spend limit puts the word "limit" on a row where
    /// none exists.
    /// </summary>
    Balance,

    Daily,
    Messages,

    /// <summary>OpenCode Go's billing period. Everyone else's longest window is a week.</summary>
    Monthly,

    /// <summary>A stated length that maps to none of the above. <c>OtherSeconds</c> carries it.</summary>
    Other,
}

/// <summary>Where a limit's denominator came from, when it did not come from the provider.</summary>
public enum WindowEstimate
{
    /// <summary>Command Code's monthly grant, from its published plan price. Not used by the four ported providers.</summary>
    PlanPrice,

    /// <summary>DeepSeek: the highest balance PulseWin has watched.</summary>
    SinceTopUp,

    /// <summary>DeepSeek: a figure the reader typed.</summary>
    YourBudget,
}

/// <summary>
/// One rate-limit window exactly as a provider reports it.
///
/// Everything here comes from the provider — Pulse never derives a percentage of
/// its own, because the token budgets behind these limits are not published and
/// any guess would be presented as fact. That rule is the spine of this whole
/// file and of every service that fills it in.
/// </summary>
public sealed class UsageWindow
{
    /// <summary>Stable across refreshes, so a row keeps its identity while its numbers change.</summary>
    public required string Id { get; init; }

    public required WindowKind Kind { get; init; }

    /// <summary>The length for <see cref="WindowKind.Other"/>.</summary>
    public int OtherSeconds { get; init; }

    /// <summary>
    /// The model this limit is scoped to, when the provider scopes it to one.
    /// A product name, so it is never translated.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// 0...1 under normal conditions, but a provider may report over 100% once a
    /// limit is exceeded.
    /// </summary>
    public required double UsedFraction { get; init; }

    public int WindowSeconds { get; init; }

    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>
    /// Whether <see cref="WindowSeconds"/> is a length the provider actually
    /// <b>stated</b>, or one chosen so the row sorts.
    ///
    /// They are not the same thing and only one of them can be divided by.
    /// OpenCode Go's weekly and monthly windows carry nominal lengths because
    /// only the reset stamp is ever displayed; Cursor's pools reset on a 28–31
    /// day billing cycle stored as a flat 30. Both are fine to sort by, and both
    /// would make <see cref="ElapsedFraction"/> draw an arc nobody reported.
    /// </summary>
    public bool ReportsLength { get; init; } = true;

    /// <summary>Set only where the provider states how much is left and never how large the allowance is.</summary>
    public WindowEstimate? Estimate { get; init; }

    /// <summary>
    /// Whether the provider says this limit is spent.
    ///
    /// Taken from the provider rather than inferred, because they are the ones
    /// who decide: Codex reports <c>limit_reached</c> per group. A percentage can
    /// also sail past 100 on a spend limit, at which point the ring is already
    /// full and only this can say so.
    /// </summary>
    public bool IsExhausted { get; init; }

    /// <summary>Whether the denominator was inferred at all.</summary>
    public bool IsEstimated => Estimate is not null;

    /// <summary>Length for display/geometry, whether stated or a sort key.</summary>
    public int LengthSeconds => Kind switch
    {
        WindowKind.FiveHour => 5 * 3600,
        WindowKind.Weekly => 7 * 86400,
        WindowKind.Monthly => 30 * 86400,
        WindowKind.Daily => 86400,
        WindowKind.Other => OtherSeconds,
        _ => WindowSeconds,
    };

    /// <summary>
    /// How much of this window has gone by, 0...1 — the other half of the reading
    /// the rail can show.
    ///
    /// "80% used" says nothing on its own about whether that is a problem: 80%
    /// spent a fifth of the way into the window means running out, and 80% spent
    /// with minutes left on the clock means it was budgeted about right. The two
    /// are only comparable when both are on screen.
    ///
    /// <b>Null rather than a guess.</b> It needs a reset time <i>and</i> a length,
    /// and a provider that reports one without the other cannot have this worked
    /// out for it — the same rule that drops a window whose length cannot be read
    /// rather than inventing one.
    /// </summary>
    public double? ElapsedFraction(DateTimeOffset now)
    {
        // `windowSeconds > 0` is not evidence that a length was reported — a sort
        // key is also a positive number. Dividing by one draws a fraction the
        // provider never gave, which is the one thing this app does not do.
        if (!ReportsLength || ResetsAt is null || WindowSeconds <= 0) return null;
        var remaining = (ResetsAt.Value - now).TotalSeconds;
        return Math.Clamp(1 - remaining / WindowSeconds, 0, 1);
    }

    /// <summary>
    /// A window has unambiguously turned over since it was last seen live at
    /// <paramref name="fraction"/>, with <paramref name="previousReset"/> as its
    /// reset time then.
    ///
    /// A figure that merely <i>fell</i> is not enough: a rolling window slides
    /// down a few points at a time without anything having turned over. A reset
    /// time that has moved forward is the provider saying so; a fraction that has
    /// dropped forty points has not slid, it has turned over.
    ///
    /// <b>Never for a balance.</b> Prepaid credit has no window to turn over,
    /// <c>ResetsAt</c> is always null, and on DeepSeek the fraction is a
    /// <i>setting</i> — so the test would fire when somebody moved a picker.
    /// </summary>
    public bool HasTurnedOver(double fraction, DateTimeOffset? previousReset)
    {
        if (Kind == WindowKind.Balance) return false;
        var movedOn = ResetsAt is not null && previousReset is not null
                      && (ResetsAt.Value - previousReset.Value).TotalSeconds > 60;
        return movedOn || fraction - UsedFraction >= 0.4;
    }

    /// <summary>
    /// The same window with the spent mark set.
    ///
    /// Windows are immutable so that a reading already handed to the UI cannot be
    /// mutated underneath it; marking one spent produces a new instance, which is
    /// what <c>MarkingSpent</c> in the Codex service needs.
    /// </summary>
    public UsageWindow WithExhausted() => new()
    {
        Id = Id,
        Kind = Kind,
        OtherSeconds = OtherSeconds,
        Scope = Scope,
        UsedFraction = UsedFraction,
        WindowSeconds = WindowSeconds,
        ResetsAt = ResetsAt,
        ReportsLength = ReportsLength,
        Estimate = Estimate,
        IsExhausted = true,
    };

    /// <summary>
    /// The same reading counted from the other end, for when the reader has asked
    /// to see what is <b>left</b>.
    /// </summary>
    public double RemainingFraction => Math.Clamp(1 - UsedFraction, 0, 1);

    /// <summary>
    /// This window's figure as a percentage string.
    /// </summary>
    /// <param name="remaining">
    /// True counts down — what is left — and false counts up, which is what the
    /// providers report.
    /// </param>
    public string PercentText(bool remaining = false) =>
        $"{Figure(remaining ? RemainingFraction : UsedFraction)}%";

    /// <summary>
    /// A fraction as a whole percentage that never rounds away the fact that there
    /// is <i>some</i>, or that there is <i>not all</i>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both ends get the rule, not just one.</b> Subtracting the used figure
    /// from 100 looks tidier and is wrong at the extremes: a window 99.6% spent
    /// would read "0% left" while there is still something there, and a window
    /// 0.4% spent would read "100% left" when it is not. So the figure shown is the
    /// one being displayed, held off both ends: nothing left reads 0%, anything
    /// left reads at least 1%, nothing used reads 100%, and anything used reads at
    /// most 99%. The two views need not sum to 100 — only one is ever on screen.
    /// </para>
    /// <para>
    /// This is also why the mark and the figure agree: the arc is drawn with a
    /// round cap, so the smallest non-zero reading still puts a dot of colour on
    /// screen, and "0%" beside that dot is the same number disagreeing with itself.
    /// Cursor reports 0.03% and its own page says 1%.
    /// </para>
    /// </remarks>
    public static int Figure(double fraction)
    {
        // **Non-finite first, because `Math.Clamp` propagates NaN rather than
        // catching it** and the cast to int would then be undefined. A budget of
        // "inf" typed into Settings arrives here as (inf − balance)/inf, and
        // upstream crashed on every launch until the field was cleared, because
        // the figure had been persisted. It is guarded at the source too; this is
        // the one that cannot be bypassed.
        if (!double.IsFinite(fraction)) return 0;

        var percent = Math.Clamp(fraction, 0, 1) * 100;
        if (percent <= 0) return 0;
        if (percent >= 100) return 100;

        // Away from zero, which is what Swift's `rounded()` does — .NET's default
        // is banker's rounding and would take 0.5 down to 0.
        return (int)Math.Clamp(Math.Round(percent, MidpointRounding.AwayFromZero), 1, 99);
    }

    /// <summary>The same rule for a bare fraction that belongs to no window.</summary>
    public static string PercentTextOf(double fraction) => $"{Figure(fraction)}%";

    public string Name
    {
        get
        {
            var baseName = Kind switch
            {
                WindowKind.FiveHour => "5-hour limit",
                WindowKind.Weekly => "Weekly limit",
                WindowKind.Spend => "Spend limit",
                WindowKind.Balance => "Balance",
                WindowKind.Daily => "Daily limit",
                WindowKind.Messages => "Message allowance",
                WindowKind.Monthly => "Monthly limit",
                WindowKind.Other => OtherSeconds >= 86400
                    ? $"{Math.Round(OtherSeconds / 86400.0)}-day limit"
                    : $"{Math.Round(OtherSeconds / 3600.0)}-hour limit",
                _ => "Limit",
            };

            var scoped = Scope is null ? baseName : $"{baseName} · {Scope}";

            var estimate = Estimate switch
            {
                WindowEstimate.PlanPrice => " · estimated",
                WindowEstimate.SinceTopUp => " · since top-up",
                WindowEstimate.YourBudget => " · of your budget",
                _ => "",
            };

            return scoped + estimate;
        }
    }
}
