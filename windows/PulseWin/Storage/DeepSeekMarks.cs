using System.Text.Json;
using PulseWin.Core;

namespace PulseWin.Storage;

/// <summary>
/// The highest balance seen since it last rose — the denominator behind
/// <see cref="DeepSeekBasis.SinceTopUp"/>.
///
/// <para>
/// The whole of this type's honesty rests on one distinction, and it is worth
/// restating because it is the most interesting thing in the DeepSeek port: the
/// mark is <b>measured, not inferred</b>. PulseWin reads the balance on every
/// refresh and remembers the peak. A balance that goes <i>up</i> can only be a
/// top-up, so that resets the mark and the ring starts again from full. Nothing
/// here is a guess about DeepSeek's pricing, a table of plans, or a number typed
/// by anyone.
/// </para>
/// <para>
/// What it costs is the first run: on a machine that has never watched this
/// account there is no mark, so the first reading becomes one and the ring reads
/// 0% until money is actually spent. That is a true statement about what has been
/// seen, and the card says which date it has been watching since.
/// </para>
/// <para>
/// <b>One mark per currency</b>, because DeepSeek reports an array of them and a
/// CNY peak is not a denominator for a USD balance.
/// </para>
/// </summary>
public static class DeepSeekMarks
{
    public readonly record struct Mark(double Peak, DateTimeOffset SetAt);

    private static readonly object Gate = new();
    private static Dictionary<string, Mark>? _marks;

    /// <summary>
    /// What the mark becomes after seeing <paramref name="balance"/>.
    ///
    /// Pure, so the rule can be reasoned about without touching a disk: a first
    /// sight and a top-up both set the mark, and everything else leaves it alone.
    /// </summary>
    public static Mark Advanced(Mark? mark, double balance, DateTimeOffset now)
    {
        if (mark is null || balance > mark.Value.Peak)
            return new Mark(Math.Max(balance, 0), now);

        return mark.Value;
    }

    /// <summary>
    /// How much of that peak is gone, or null where there is no denominator.
    ///
    /// A peak of zero is an account that has never had any credit to spend, which
    /// is not the same as one that has spent all of it.
    /// </summary>
    public static double? UsedFraction(double balance, double peak)
    {
        if (peak <= 0) return null;
        return Math.Clamp((peak - balance) / peak, 0, 1);
    }

    /// <summary>
    /// Reads the balance's mark, advances it, and returns the advanced one.
    ///
    /// The mark is advanced on <b>every</b> reading, whichever basis is in force:
    /// switching to "since top-up" later should find a peak already there rather
    /// than start over from whatever the balance happens to be that afternoon.
    /// </summary>
    public static Mark Observe(string currency, double balance, DateTimeOffset now)
    {
        lock (Gate)
        {
            var marks = _marks ??= Load();
            var advanced = Advanced(marks.TryGetValue(currency, out var existing) ? existing : null, balance, now);

            if (!marks.TryGetValue(currency, out var current) || current != advanced)
            {
                marks[currency] = advanced;
                Store(marks);
            }

            return advanced;
        }
    }

    /// <summary>The mark as it stands, without advancing it.</summary>
    public static Mark? Peek(string currency)
    {
        lock (Gate)
        {
            var marks = _marks ??= Load();
            return marks.TryGetValue(currency, out var mark) ? mark : null;
        }
    }

    private static Dictionary<string, Mark> Load()
    {
        try
        {
            if (!File.Exists(AppPaths.DeepSeekBaseline)) return new Dictionary<string, Mark>();
            return JsonSerializer.Deserialize<Dictionary<string, Mark>>(
                       File.ReadAllText(AppPaths.DeepSeekBaseline), Json.Options)
                   ?? new Dictionary<string, Mark>();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return new Dictionary<string, Mark>();
        }
    }

    private static void Store(Dictionary<string, Mark> marks)
    {
        try
        {
            AppPaths.WriteAtomic(AppPaths.DeepSeekBaseline, JsonSerializer.Serialize(marks, Json.Options));
        }
        catch (IOException)
        {
        }
    }
}
