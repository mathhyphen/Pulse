using System.Text.Json;
using System.Text.Json.Serialization;
using PulseWin.Core;
using PulseWin.Localization;

namespace PulseWin.Storage;

/// <summary>How the DeepSeek ring gets a denominator. Ported from <c>DeepSeekBasis</c>.</summary>
public enum DeepSeekBasis
{
    /// <summary>
    /// The highest balance PulseWin has seen since it last rose. Nobody has to
    /// type anything, and the number is one we <b>watched</b>, not one we made
    /// up — which is why it is the default.
    /// </summary>
    SinceTopUp,

    /// <summary>No denominator at all: the rail shows the money, not a fraction.</summary>
    BalanceOnly,

    /// <summary>A figure the user considers a full tank.</summary>
    Budget,
}

/// <summary>Which screen edge the rail docks to.</summary>
public enum RailEdge
{
    Right,
    Left,
    Top,
}

/// <summary>Everything the user chose, in one file.</summary>
public sealed class AppSettings
{
    public HashSet<Provider> Enabled { get; set; } = new();

    /// <summary>
    /// The primary account of each enabled provider, plus any added accounts.
    ///
    /// Added accounts are endpoint-backed and carry their own credential, so this
    /// is also where a second or third Codex login lives.
    /// </summary>
    public List<MonitoredAccount> Accounts { get; set; } = new();

    public DeepSeekBasis DeepSeekBasis { get; set; } = DeepSeekBasis.SinceTopUp;

    /// <summary>What the reader calls a full tank. Null, zero or negative leaves that mode with no denominator.</summary>
    public double? DeepSeekBudget { get; set; }

    /// <summary>Which currency the ring follows when the account holds more than one.</summary>
    public string? DeepSeekCurrency { get; set; }

    public RailEdge Edge { get; set; } = RailEdge.Right;

    /// <summary>Vertical (or horizontal, for the top edge) offset from centre, so the rail can be moved off a notch.</summary>
    public double RailOffset { get; set; }

    public bool RailVisible { get; set; } = true;

    /// <summary>How often the whole rail is refreshed, in minutes.</summary>
    public int RefreshMinutes { get; set; } = 5;

    /// <summary>
    /// Whether the figure and the ring count down — what is left — instead of up.
    ///
    /// <para>
    /// Upstream defaults this to <b>off</b>, meaning it counts up, and its hovert
    /// card carries the "Used"/"Left" word that removes the ambiguity. This port
    /// defaults it to <b>on</b>, and that is a deliberate departure: a bare
    /// percentage under a nearly empty ring reads as "almost nothing left" no
    /// matter which way it was counted, which is the single most likely thing to
    /// be misread on the rail. Counting down makes the picture a fuel gauge — a
    /// full ring means a full tank — and a full ring cannot be misread.
    /// </para>
    /// <para>
    /// The colour still comes off what is <i>gone</i> either way, so a sliver of
    /// quota left is a small red arc rather than a large one. How close a limit is
    /// does not change because the figure beside it was counted from the other end.
    /// </para>
    /// </summary>
    public bool ShowsRemaining { get; set; } = true;

    /// <summary>Which language the interface is drawn in. <c>Auto</c> follows Windows.</summary>
    public UiLanguage Language { get; set; } = UiLanguage.Auto;

    /// <summary>
    /// Whether the rail is dimmed to a thin sliver when nothing needs attention.
    /// Pulse calls it auto-collapse.
    /// </summary>
    public bool AutoCollapse { get; set; }

    private static readonly object Gate = new();
    private static AppSettings? _current;

    public static AppSettings Current
    {
        get
        {
            lock (Gate)
            {
                return _current ??= Load();
            }
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.Settings))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(AppPaths.Settings), Json.Options);
                if (loaded is not null)
                {
                    loaded.Normalise();
                    return loaded;
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // A settings file that cannot be read is replaced by defaults rather
            // than being allowed to block launch. The bad file is kept, because a
            // silent overwrite of somebody's configuration is worse than a
            // confusing one.
            TryKeepBadFile();
        }

        var fresh = new AppSettings();
        fresh.Normalise();
        return fresh;
    }

    public void Save()
    {
        lock (Gate)
        {
            try
            {
                AppPaths.WriteAtomic(AppPaths.Settings, JsonSerializer.Serialize(this, Json.Options));
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Brings the account list in line with <see cref="Enabled"/>.
    ///
    /// A provider that is switched on always has its primary account present, and
    /// a provider that is switched off has its accounts removed — including any
    /// added ones, which is the behaviour a user who just switched Codex off
    /// expects. Called on load and after every Settings change, so the rail can
    /// never be reading a list that disagrees with the checkboxes.
    /// </summary>
    public void Normalise()
    {
        Accounts ??= new List<MonitoredAccount>();

        foreach (var provider in ProviderCatalog.All)
        {
            var primary = AccountKey.Primary(provider);
            var existing = Accounts.FirstOrDefault(a => a.Key == primary);

            if (Enabled.Contains(provider))
            {
                if (existing is null)
                    Accounts.Add(new MonitoredAccount { Key = primary, Enabled = true });
            }
            else if (existing is not null)
            {
                Accounts.Remove(existing);
                Accounts.RemoveAll(a => a.Key.Provider == provider);
            }
        }

        // Added accounts survive only while their provider is on.
        Accounts.RemoveAll(a => !Enabled.Contains(a.Key.Provider));

        RefreshMinutes = Math.Clamp(RefreshMinutes, 1, 60);

        // A budget that is not finite and positive has no denominator, and an
        // infinite one makes the fraction NaN rather than a number. Settings
        // refuses one; this is the guard that does not depend on where the figure
        // came from.
        if (DeepSeekBudget is { } budget && (!double.IsFinite(budget) || budget <= 0))
            DeepSeekBudget = null;
    }

    /// <summary>The accounts the rail should be reading, in a stable order.</summary>
    /// <remarks>
    /// Derived from <see cref="Accounts"/> and <see cref="Enabled"/>, so it is kept
    /// out of the stored file — otherwise every save writes the whole account list
    /// twice, and the copy that is read back is the one nobody edits.
    /// </remarks>
    [JsonIgnore]
    public IEnumerable<MonitoredAccount> ActiveAccounts =>
        Accounts.Where(a => a.Enabled && Enabled.Contains(a.Key.Provider))
                .OrderBy(a => Array.IndexOf(ProviderCatalog.All.ToArray(), a.Key.Provider))
                .ThenBy(a => a.Key.Id, StringComparer.Ordinal);

    private static void TryKeepBadFile()
    {
        try
        {
            File.Copy(AppPaths.Settings, AppPaths.Settings + ".bad", overwrite: true);
        }
        catch (IOException)
        {
        }
    }
}
