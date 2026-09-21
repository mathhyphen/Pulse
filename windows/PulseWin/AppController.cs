using System.Windows;
using System.Windows.Threading;
using PulseWin.Core;
using PulseWin.Localization;
using PulseWin.Services;
using PulseWin.Storage;
using PulseWin.Ui;

namespace PulseWin;

/// <summary>
/// Wires the store, the rail, the tray icon and the refresh clock together.
///
/// <para>
/// This is the whole of the app's lifecycle. It is deliberately the only place
/// that knows about all four, so that the rail can be tested by feeding it a store,
/// and the store can be run headlessly — which is exactly what
/// <c>--selftest</c> does.
/// </para>
/// </summary>
public sealed class AppController
{
    private readonly UsageStore _store = new();
    private readonly DispatcherTimer _clock = new();

    private RailWindow? _rail;
    private TrayIcon? _tray;
    private SettingsWindow? _settings;
    private bool _refreshing;

    public void Start(bool openSettings = false)
    {
        // Credentials are read once per launch rather than once per refresh, so a
        // locked or unreadable store cannot blank a ring in the middle of a session.
        CredentialStore.Load();

        var settings = AppSettings.Current;
        settings.Normalise();
        settings.Save();

        // Before anything is drawn: every surface below reads its strings and its
        // brushes from here, and a window built first would keep the old ones for
        // its whole life.
        Loc.Initialise(settings.Language);
        Theme.Apply(settings.Theme, settings.Backdrop);

        _store.Changed += OnStoreChanged;

        BuildRail(settings);
        BuildTray();

        // **Draw the rail before the first reading arrives.** Rows are created from
        // the store's snapshot, and the first snapshot only lands when the opening
        // refresh finishes — a few seconds of four network calls. Until then the rail
        // was an empty shell: it appeared, but as an 18-pixel sliver that grew into
        // its real shape once the readings came back. Rendering here gives it the
        // right size immediately, with every figure as a placeholder, and the
        // refresh fills them in.
        _rail?.Render(_store.Snapshot());

        _clock.Interval = TimeSpan.FromMinutes(settings.RefreshMinutes);
        _clock.Tick += (_, _) => _ = RefreshAsync();
        _clock.Start();

        // Re-dock whenever the work area changes: a resolution change, the taskbar
        // moving, a monitor being unplugged. Without this the rail can end up
        // underneath the taskbar with no way to reach it.
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea)) _rail?.Redock();
        };

        _ = RefreshAsync();

        // Nothing switched on means there is nothing to draw, and a first run that
        // shows an empty sliver explains nothing. Open Settings instead — the same
        // choice Pulse's first-run picker makes.
        if (openSettings || settings.Enabled.Count == 0)
            Dispatcher.CurrentDispatcher.BeginInvoke(OpenSettings);
    }

    private void BuildRail(AppSettings settings)
    {
        _rail = new RailWindow();
        _rail.SettingsRequested += OpenSettings;
        _rail.RefreshRequested += () => _ = RefreshAsync();
        _rail.HideRequested += () =>
        {
            settings.RailVisible = false;
            settings.Save();
            _rail?.Hide();
        };
        _rail.ExitRequested += Exit;

        if (settings.RailVisible)
        {
            _rail.Show();
            _rail.Redock();

            // Dock again once the rows have been measured, or the first position
            // uses the height of an empty rail.
            _rail.SizeChanged += (_, _) => _rail?.Redock();
        }
    }

    private void BuildTray()
    {
        _tray = new TrayIcon();
        _tray.SettingsRequested += OpenSettings;
        _tray.RefreshRequested += () => _ = RefreshAsync();
        _tray.ToggleRailRequested += ToggleRail;
        _tray.ExitRequested += Exit;
    }

    private void ToggleRail()
    {
        if (_rail is null) return;

        var settings = AppSettings.Current;

        if (_rail.IsVisible)
        {
            settings.RailVisible = false;
            _rail.Hide();
        }
        else
        {
            settings.RailVisible = true;
            _rail.Show();
            _rail.Redock();
        }

        settings.Save();
    }

    private void OpenSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(_store);
            _settings.SettingsChanged += () =>
            {
                var settings = AppSettings.Current;

                _clock.Interval = TimeSpan.FromMinutes(settings.RefreshMinutes);

                // A language change redraws Settings itself, but the rail's menu and
                // the tray menu were built once, with the strings of the day.
                Loc.Setting = settings.Language;
                _rail?.RefreshLanguage();
                _tray?.RefreshLanguage();

                // A palette change is a rebuild too, not a relabel: every brush was
                // captured when its surface was made.
                Theme.Apply(settings.Theme, settings.Backdrop);
                _rail?.ApplyTheme();

                // **Deferred.** This handler is raised from a control inside Settings,
                // and rebuilding that window destroys the very ComboBox still in the
                // middle of raising the event. Letting the event finish first is the
                // difference between a re-skin and a crash.
                Application.Current?.Dispatcher.BeginInvoke(() => _settings?.Rebuild());

                if (settings.RailVisible && _rail is { IsVisible: false }) _rail.Show();
                if (_rail is not null)
                {
                    _rail.Render(_store.Snapshot());
                    _rail.Redock();
                }

                // A service that was just switched on, or given a key, should not
                // wait for the next tick to say whether it works.
                _ = RefreshAsync();
            };
            _settings.Closed += (_, _) => _settings = null;
        }

        if (_settings.IsVisible)
        {
            _settings.Activate();
            return;
        }

        _settings.Show();
        _settings.RefreshStatus();
    }

    private void OnStoreChanged()
    {
        // The store raises this on whichever thread finished a fetch, so everything
        // that touches a visual has to come back to the UI thread.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        dispatcher.BeginInvoke(() =>
        {
            _rail?.Render(_store.Snapshot());
            _settings?.RefreshStatus();
            UpdateTooltip();
        });
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;

        try
        {
            await _store.RefreshAllAsync();

            // **The interval answers "how often is often enough", not "how long may a
            // stumble stay on screen".** Those are the same question until a pass
            // fails on a long interval, where one bad fetch meant an hour of four
            // empty rows. A failed pass now comes back inside a minute.
            var settings = AppSettings.Current;
            _clock.Interval = _store.AnyFailed
                ? TimeSpan.FromSeconds(Math.Min(60, settings.RefreshMinutes * 60))
                : TimeSpan.FromMinutes(settings.RefreshMinutes);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateTooltip()
    {
        if (_tray is null) return;

        var parts = new List<string>();
        foreach (var (account, state) in _store.Snapshot())
        {
            var reading = state.Reading;

            // **A failed account is named, not skipped.** It used to be dropped from
            // the tooltip entirely, so the one place that summarises the rail was also
            // the one place a broken account could not be seen.
            if (reading is null)
            {
                if (state.LastFailure is { } failed) parts.Add($"{account.DisplayLabel}: {failed.Message()}");
                continue;
            }

            var figure = reading.Fullest is not null
                ? reading.HeadlineText(AppSettings.Current.ShowsRemaining)
                : reading.RailMoney ?? "—";

            var direction = AppSettings.Current.ShowsRemaining
                ? Loc.Current.DirectionLeft
                : Loc.Current.DirectionUsed;
            parts.Add($"{account.DisplayLabel}: {figure} {direction}");
        }

        _tray.SetTooltip(parts.Count == 0
            ? Loc.Current.TrayTooltipName
            : $"{Loc.Current.TrayTooltipName} · " + string.Join(" · ", parts));
    }

    private void Exit()
    {
        _clock.Stop();
        _tray?.Dispose();
        Application.Current?.Shutdown();
    }
}

