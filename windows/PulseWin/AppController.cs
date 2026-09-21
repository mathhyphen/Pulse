using System.Windows;
using System.Windows.Threading;
using PulseWin.Core;
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

    public void Start()
    {
        // Credentials are read once per launch rather than once per refresh, so a
        // locked or unreadable store cannot blank a ring in the middle of a session.
        CredentialStore.Load();

        var settings = AppSettings.Current;
        settings.Normalise();
        settings.Save();

        _store.Changed += OnStoreChanged;

        BuildRail(settings);
        BuildTray();

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
        if (settings.Enabled.Count == 0)
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
            _rail.Dock(settings.Edge, settings.RailOffset);

            // Dock again once the rows have been measured, or the first position
            // uses the height of an empty rail.
            _rail.SizeChanged += (_, _) => _rail?.Dock(settings.Edge, settings.RailOffset);
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
            _rail.Dock(settings.Edge, settings.RailOffset);
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

                if (settings.RailVisible && _rail is { IsVisible: false }) _rail.Show();
                if (_rail is not null)
                {
                    _rail.Render(_store.Snapshot());
                    _rail.Dock(settings.Edge, settings.RailOffset);
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
            if (reading is null) continue;

            var figure = reading.Fullest is not null
                ? reading.HeadlineText(AppSettings.Current.ShowsRemaining)
                : reading.RailMoney ?? "—";

            var direction = AppSettings.Current.ShowsRemaining ? "left" : "used";
            parts.Add($"{account.DisplayLabel}: {figure} {direction}");
        }

        _tray.SetTooltip(parts.Count == 0 ? "PulseWin" : "PulseWin · " + string.Join(" · ", parts));
    }

    private void Exit()
    {
        _clock.Stop();
        _tray?.Dispose();
        Application.Current?.Shutdown();
    }
}
