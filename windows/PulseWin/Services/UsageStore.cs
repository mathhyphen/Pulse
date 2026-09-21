using PulseWin.Core;
using PulseWin.Providers;
using PulseWin.Storage;

namespace PulseWin.Services;

/// <summary>What is known about one account right now.</summary>
public sealed class AccountState
{
    /// <summary>
    /// The last reading that carried an answer, which may be older than the last
    /// attempt.
    ///
    /// This is the whole point of keeping two fields. When a refresh fails, the
    /// right thing to draw is the figures we last actually got — dimmed, with the
    /// failure named — rather than a ring that goes blank every time the network
    /// hiccups. Pulse's cache does the same reconciliation and its notifications
    /// have a case for "several checks in a row failed, so the panel is quietly
    /// showing older figures".
    /// </summary>
    public ProviderUsage? Reading { get; set; }

    /// <summary>The most recent failure, cleared the moment a fetch reports anything.</summary>
    public Unavailability? LastFailure { get; set; }

    public DateTimeOffset? AttemptedAt { get; set; }

    /// <summary>Consecutive failures, so a single stumble is not reported as a trend.</summary>
    public int ConsecutiveFailures { get; set; }

    public bool HasReading => Reading?.ReportsSomething == true;
}

/// <summary>
/// The refresh loop and the state behind the rail.
///
/// <para>
/// Ported from the shape of Pulse's <c>UsageStore</c>: accounts are refreshed
/// together, a provider that fails does not take the others down with it, and a
/// failure never erases a reading that was already good.
/// </para>
/// <para>
/// One deliberate simplification: Pulse reads a provider's credentials once per
/// launch rather than once per refresh, so that a credential-store problem cannot
/// blank a ring mid-session. <see cref="CredentialStore"/> does the same, and the
/// services here ask it rather than the settings UI.
/// </para>
/// </summary>
public sealed class UsageStore
{
    private readonly Dictionary<AccountKey, AccountState> _states = new();
    private readonly object _gate = new();

    public UsageStore()
    {
        // **Start from what was last seen, not from nothing.** A launch would
        // otherwise be blind until the opening refresh lands — a few seconds of four
        // network calls — and blind for a whole interval if that refresh fails, which
        // shows as four empty rows and reads as the app being broken.
        foreach (var (key, reading) in ReadingCache.Load())
        {
            if (AccountKey.TryParse(key, out var account))
                _states[account] = new AccountState { Reading = reading };
        }
    }

    /// <summary>Raised on the thread that finished a refresh; the UI marshals.</summary>
    public event Action? Changed;

    /// <summary>
    /// Whether the last pass left any account without a fresh answer.
    /// </summary>
    /// <remarks>
    /// Read by the controller to come back sooner than the configured interval. A
    /// caller-set interval answers "how often is often enough", not "how long should a
    /// stumbled fetch stay wrong" — and on a long interval those are very different
    /// questions. One failed pass used to mean an hour of an empty rail.
    /// </remarks>
    public bool AnyFailed { get; private set; }

    public AccountState? StateFor(AccountKey key)
    {
        lock (_gate)
        {
            return _states.TryGetValue(key, out var state) ? state : null;
        }
    }

    public IReadOnlyList<(MonitoredAccount Account, AccountState State)> Snapshot()
    {
        var settings = AppSettings.Current;
        lock (_gate)
        {
            return settings.ActiveAccounts
                .Select(account => (account, _states.TryGetValue(account.Key, out var state)
                    ? state
                    : new AccountState()))
                .ToList();
        }
    }

    /// <summary>
    /// Refreshes every active account once.
    ///
    /// Accounts are fetched <b>concurrently</b>: they are independent services, and
    /// a rail of five accounts should take as long as the slowest one rather than
    /// the sum. Each failure is contained — one provider being down must not stop
    /// the other four from answering.
    /// </summary>
    public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        var accounts = AppSettings.Current.ActiveAccounts.ToList();
        if (accounts.Count == 0)
        {
            Changed?.Invoke();
            return;
        }

        var work = accounts.Select(account => RefreshOneAsync(account, cancellationToken));
        await Task.WhenAll(work);

        // After the pass, not during it: one account's failure must not leave the
        // others unsaved.
        lock (_gate)
        {
            AnyFailed = accounts.Any(a => _states.TryGetValue(a.Key, out var s) && s.LastFailure is not null);

            ReadingCache.Save(_states
                .Where(pair => pair.Value.Reading is not null)
                .ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.Reading!));
        }

        Changed?.Invoke();
    }

    public async Task RefreshAsync(MonitoredAccount account, CancellationToken cancellationToken = default)
    {
        await RefreshOneAsync(account, cancellationToken);
        Changed?.Invoke();
    }

    private async Task RefreshOneAsync(MonitoredAccount account, CancellationToken cancellationToken)
    {
        ProviderUsage fresh;
        try
        {
            fresh = await FetchAsync(account, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A service that throws is a service that failed, not a crash. The
            // exception is deliberately swallowed here rather than at four call
            // sites inside the services, because every one of them would have to
            // remember to do it.
            fresh = ProviderUsage.Failed(account.Key, Unavailability.UnreadableReply);
        }

        lock (_gate)
        {
            if (!_states.TryGetValue(account.Key, out var state))
                _states[account.Key] = state = new AccountState();

            state.AttemptedAt = DateTimeOffset.Now;

            if (fresh.ReportsSomething)
            {
                state.Reading = fresh;
                state.LastFailure = null;
                state.ConsecutiveFailures = 0;
            }
            else
            {
                state.LastFailure = fresh.Unavailable ?? Unavailability.UnreadableReply;
                state.ConsecutiveFailures++;

                // Keep the older reading — whether it came from this session or from
                // the cache — but record why the panel is stale. The UI dims it and
                // names the failure rather than going blank.
            }
        }
    }

    private static Task<ProviderUsage> FetchAsync(MonitoredAccount account, CancellationToken cancellationToken)
    {
        var provider = account.Key.Provider;
        var entered = CredentialStore.Key(provider);

        return provider switch
        {
            Provider.Codex => CodexService.FetchAsync(account, cancellationToken),
            Provider.OpenCodeGo => OpenCodeGoService.FetchAsync(account.Key, entered, cancellationToken),
            Provider.Zhipu or Provider.Zai => ZhipuService.FetchAsync(account.Key, entered, cancellationToken),
            Provider.DeepSeek => DeepSeekService.FetchAsync(account.Key, entered, cancellationToken),
            _ => Task.FromResult(ProviderUsage.Failed(account.Key, Unavailability.NoLimitsReported)),
        };
    }
}
