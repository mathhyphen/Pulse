using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PulseWin.Auth;
using PulseWin.Core;
using PulseWin.Localization;
using PulseWin.Providers;
using PulseWin.Services;
using PulseWin.Storage;

namespace PulseWin.Ui;

/// <summary>
/// Everything the reader can decide, in one window.
///
/// <para>
/// The order is deliberate: services first, because that is what most people came
/// for, and each service's credential field is described by where its key actually
/// comes from rather than by the word "API key". Two of the five do not want a key
/// at all — Codex borrows the CLI's login and OpenCode Go will read its own file —
/// and a field that asks for something the product never issued sends people
/// looking for one that does not exist.
/// </para>
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly UsageStore _store;
    private readonly Dictionary<Provider, TextBlock> _status = new();
    private readonly Dictionary<Provider, PasswordBox> _keyFields = new();
    private readonly StackPanel _codexAccounts = new();
    private readonly ComboBox _edge = new();
    private readonly TextBox _refresh = new();
    private readonly TextBox _offset = new();
    private readonly RadioButton _basisTopUp = new();
    private readonly RadioButton _basisBalanceOnly = new();
    private readonly RadioButton _basisBudget = new();
    private readonly TextBox _budget = new();
    private readonly TextBox _currency = new();

    public event Action? SettingsChanged;

    public SettingsWindow(UsageStore store)
    {
        _store = store;

        Width = 620;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.WindowBrush;
        FontFamily = Theme.Font;
        Foreground = Theme.PrimaryBrush;

        Load();
        Rebuild();
    }

    /// <summary>
    /// Draws the whole window from the current language and palette.
    /// </summary>
    /// <remarks>
    /// Every string and every brush here is baked in at construction, so a change to
    /// either has to redraw rather than relabel. Rebuilding wholesale is the honest
    /// version of that: the alternative is a second code path that updates existing
    /// elements, which would drift from this one and be wrong in the half nobody
    /// looks at.
    /// </remarks>
    public void Rebuild()
    {
        _status.Clear();
        _keyFields.Clear();

        Title = Loc.Current.SettingsTitle;
        Background = Theme.WindowBrush;

        var panel = new StackPanel { Margin = new Thickness(22) };

        // Appearance and language first, because they change everything under them.
        panel.Children.Add(Heading(Loc.Current.SettingsAppearance, 15));
        panel.Children.Add(AppearanceBlock());

        panel.Children.Add(Heading(Loc.Current.SettingsLanguage, 15));
        panel.Children.Add(LanguageBlock());

        panel.Children.Add(Heading(Loc.Current.SettingsServices, 17));
        panel.Children.Add(Caption(Loc.Current.SettingsServicesCaption));
        foreach (var provider in ProviderCatalog.All)
            panel.Children.Add(ProviderBlock(provider));

        panel.Children.Add(Heading(Loc.Current.SettingsDeepSeek, 15));
        panel.Children.Add(Caption(Loc.Current.SettingsDeepSeekCaption));
        panel.Children.Add(DeepSeekBlock());

        panel.Children.Add(Heading(Loc.Current.SettingsRail, 15));
        panel.Children.Add(RailBlock());

        panel.Children.Add(Heading(Loc.Current.SettingsCodexAccounts, 15));
        panel.Children.Add(Caption(Loc.Current.SettingsCodexAccountsCaption));
        panel.Children.Add(_codexAccounts);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel,
        };

        RefreshStatus();
    }

    /// <summary>
    /// The theme and surface pickers.
    /// </summary>
    /// <remarks>
    /// Both are offered rather than decided. A palette is a preference — a reader on
    /// a bright desk and a reader at night want different answers, and Windows' own
    /// setting is only right for whoever set it — and acrylic is a trade: it is the
    /// look the rail is designed around, and blur-behind is known to cost smoothness
    /// while a window is being dragged.
    /// </remarks>
    private UIElement AppearanceBlock()
    {
        var block = Block();
        var panel = new StackPanel();

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        row.Children.Add(Label(Loc.Current.SettingsTheme));

        var theme = new ComboBox
        {
            FontSize = 12,
            Width = 150,
            Margin = new Thickness(0, 0, 18, 0),
            ItemsSource = new[]
            {
                Loc.Current.SettingsThemeFollowWindows,
                Loc.Current.SettingsThemeDark,
                Loc.Current.SettingsThemeLight,
            },
            SelectedIndex = AppSettings.Current.Theme switch
            {
                AppTheme.Dark => 1,
                AppTheme.Light => 2,
                _ => 0,
            },
        };
        theme.SelectionChanged += (_, _) => ApplyAppearance(
            theme.SelectedIndex switch
            {
                1 => AppTheme.Dark,
                2 => AppTheme.Light,
                _ => AppTheme.FollowWindows,
            },
            null);
        row.Children.Add(theme);

        row.Children.Add(Label(Loc.Current.SettingsBackdrop));

        var backdrop = new ComboBox
        {
            FontSize = 12,
            Width = 150,
            ItemsSource = new[]
            {
                Loc.Current.SettingsBackdropSolid,
                Loc.Current.SettingsBackdropAcrylic,
            },
            SelectedIndex = AppSettings.Current.Backdrop == Backdrop.Acrylic ? 1 : 0,
        };
        backdrop.SelectionChanged += (_, _) => ApplyAppearance(
            null,
            backdrop.SelectedIndex == 1 ? Backdrop.Acrylic : Backdrop.Solid);
        row.Children.Add(backdrop);

        panel.Children.Add(row);
        panel.Children.Add(new TextBlock
        {
            Text = Loc.Current.SettingsBackdropNote,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = Theme.SecondaryBrush,
        });

        block.Child = panel;
        return block;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 9, 0),
        Foreground = Theme.PrimaryBrush,
    };

    /// <summary>Writes whichever of the two changed, and asks the controller to re-skin.</summary>
    private void ApplyAppearance(AppTheme? theme, Backdrop? backdrop)
    {
        var settings = AppSettings.Current;

        if (theme is { } chosen && chosen != settings.Theme)
        {
            settings.Theme = chosen;
            settings.Save();
            SettingsChanged?.Invoke();
        }

        if (backdrop is { } surface && surface != settings.Backdrop)
        {
            settings.Backdrop = surface;
            settings.Save();
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>
    /// The language picker.
    /// </summary>
    /// <remarks>
    /// <c>Follow Windows</c> is the default and reads <c>CurrentUICulture</c>, so a
    /// Chinese Windows opens in Chinese without anybody finding this row.
    /// </remarks>
    private UIElement LanguageBlock()
    {
        var block = Block();
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        var picker = new ComboBox
        {
            FontSize = 12,
            Width = 190,
            ItemsSource = new[]
            {
                Loc.Current.SettingsLanguageAuto,
                "English",
                "简体中文",
            },
            SelectedIndex = AppSettings.Current.Language switch
            {
                UiLanguage.English => 1,
                UiLanguage.Chinese => 2,
                _ => 0,
            },
        };

        picker.SelectionChanged += (_, _) =>
        {
            var chosen = picker.SelectedIndex switch
            {
                1 => UiLanguage.English,
                2 => UiLanguage.Chinese,
                _ => UiLanguage.Auto,
            };

            if (chosen == AppSettings.Current.Language) return;

            AppSettings.Current.Language = chosen;
            AppSettings.Current.Save();
            Loc.Setting = chosen;

            Rebuild();
            SettingsChanged?.Invoke();
        };

        row.Children.Add(picker);
        block.Child = row;
        return block;
    }

    // ------------------------------------------------------------- construction

    private static TextBlock Heading(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 18, 0, 4),
        Foreground = Theme.PrimaryBrush,
    };

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10),
        Foreground = Theme.SecondaryBrush,
    };

    private Border Block() => new()
    {
        Background = Theme.PanelBrush,
        BorderBrush = Theme.StrokeBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(13),
        Margin = new Thickness(0, 0, 0, 9),
    };

    private UIElement ProviderBlock(Provider provider)
    {
        var block = Block();
        var panel = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var enable = new CheckBox
        {
            IsChecked = AppSettings.Current.Enabled.Contains(provider),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0),
        };
        enable.Checked += (_, _) => SetEnabled(provider, true);
        enable.Unchecked += (_, _) => SetEnabled(provider, false);
        Grid.SetColumn(enable, 0);
        header.Children.Add(enable);

        var name = new TextBlock
        {
            Text = provider.DisplayName(),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Theme.PrimaryBrush,
        };
        Grid.SetColumn(name, 1);
        header.Children.Add(name);

        var status = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Theme.SecondaryBrush,
        };
        _status[provider] = status;
        Grid.SetColumn(status, 2);
        header.Children.Add(status);

        panel.Children.Add(header);
        panel.Children.Add(new TextBlock
        {
            Text = provider.CredentialNote(),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(26, 5, 0, 0),
            Foreground = Theme.SecondaryBrush,
        });

        if (provider.UsesApiKey())
            panel.Children.Add(KeyField(provider));

        block.Child = panel;
        return block;
    }

    private UIElement KeyField(Provider provider)
    {
        var grid = new Grid { Margin = new Thickness(26, 9, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var field = new PasswordBox
        {
            FontSize = 12,
            Padding = new Thickness(7, 5, 7, 5),
            Background = Theme.FieldBrush,
            Foreground = Theme.PrimaryBrush,
            BorderBrush = Theme.StrokeBrush,
            // The field starts empty even when a key is stored. Writing an existing
            // secret back into a text field so it can be read off the screen is the
            // one convenience not worth having.
            PasswordChar = '•',
            ToolTip = CredentialStore.Has(provider)
                ? Loc.Current.SettingsKeyStored
                : Loc.Current.SettingsKeyPaste,
        };
        _keyFields[provider] = field;

        void Commit()
        {
            var entered = field.Password;
            if (entered.Length == 0) return;

            CredentialStore.Set(provider, entered);
            field.Clear();
            RefreshStatus();
            SettingsChanged?.Invoke();
        }

        field.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) Commit();
        };
        field.LostFocus += (_, _) => Commit();

        Grid.SetColumn(field, 0);
        grid.Children.Add(field);

        var clear = new Button
        {
            Content = Loc.Current.SettingsClear,
            FontSize = 11,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(7, 0, 0, 0),
            Background = Theme.FieldBrush,
            Foreground = Theme.PrimaryBrush,
            BorderBrush = Theme.StrokeBrush,
        };
        clear.Click += (_, _) =>
        {
            CredentialStore.Set(provider, null);
            RefreshStatus();
            SettingsChanged?.Invoke();
        };
        Grid.SetColumn(clear, 1);
        grid.Children.Add(clear);

        return grid;
    }

    private UIElement DeepSeekBlock()
    {
        var block = Block();
        var panel = new StackPanel();

        string[] labels =
        [
            Loc.Current.SettingsBasisSinceTopUp,
            Loc.Current.SettingsBasisBalanceOnly,
            Loc.Current.SettingsBasisBudget,
        ];
        RadioButton[] buttons = [_basisTopUp, _basisBalanceOnly, _basisBudget];

        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].Content = labels[i];
            buttons[i].FontSize = 12;
            buttons[i].Margin = new Thickness(0, 3, 0, 3);
            buttons[i].Foreground = Theme.PrimaryBrush;
            buttons[i].GroupName = "deepseek-basis";
            panel.Children.Add(buttons[i]);
        }

        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        var budgetLabel = new TextBlock
        {
            Text = Loc.Current.SettingsBudget,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0),
            Foreground = Theme.PrimaryBrush,
        };
        Grid.SetColumn(budgetLabel, 0);
        row.Children.Add(budgetLabel);

        StyleField(_budget);
        Grid.SetColumn(_budget, 1);
        row.Children.Add(_budget);

        var currencyLabel = new TextBlock
        {
            Text = Loc.Current.SettingsCurrency,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 9, 0),
            Foreground = Theme.PrimaryBrush,
        };
        Grid.SetColumn(currencyLabel, 2);
        row.Children.Add(currencyLabel);

        StyleField(_currency);
        _currency.ToolTip = Loc.Current.SettingsCurrencyTooltip;
        Grid.SetColumn(_currency, 3);
        row.Children.Add(_currency);

        panel.Children.Add(row);

        void Commit()
        {
            var settings = AppSettings.Current;
            settings.DeepSeekBasis = _basisBalanceOnly.IsChecked == true
                ? DeepSeekBasis.BalanceOnly
                : _basisBudget.IsChecked == true
                    ? DeepSeekBasis.Budget
                    : DeepSeekBasis.SinceTopUp;

            // A budget that is not a finite positive number is not a denominator.
            // Left unparsed rather than defaulted to something, so a typo cannot
            // silently become a fraction nobody asked for.
            settings.DeepSeekBudget = double.TryParse(
                _budget.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var budget)
                && double.IsFinite(budget) && budget > 0
                    ? budget
                    : null;

            var currency = _currency.Text.Trim().ToUpperInvariant();
            settings.DeepSeekCurrency = currency.Length == 0 ? null : currency;

            settings.Save();
            SettingsChanged?.Invoke();
        }

        foreach (var button in buttons) button.Checked += (_, _) => Commit();
        _budget.LostFocus += (_, _) => Commit();
        _currency.LostFocus += (_, _) => Commit();

        block.Child = panel;
        return block;
    }

    private UIElement RailBlock()
    {
        var block = Block();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        var edgeLabel = new TextBlock
        {
            Text = Loc.Current.SettingsEdge,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0),
            Foreground = Theme.PrimaryBrush,
        };
        Grid.SetColumn(edgeLabel, 0);
        row.Children.Add(edgeLabel);

        _edge.ItemsSource = new[]
        {
            Loc.Current.SettingsEdgeRight,
            Loc.Current.SettingsEdgeLeft,
            Loc.Current.SettingsEdgeTop,
        };
        _edge.FontSize = 12;
        _edge.Margin = new Thickness(0, 0, 16, 0);
        _edge.SelectionChanged += (_, _) => CommitRail();
        Grid.SetColumn(_edge, 1);
        row.Children.Add(_edge);

        var refreshLabel = new TextBlock
        {
            Text = Loc.Current.SettingsEvery,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0),
            Foreground = Theme.PrimaryBrush,
        };
        Grid.SetColumn(refreshLabel, 2);
        row.Children.Add(refreshLabel);

        StyleField(_refresh);
        _refresh.ToolTip = Loc.Current.SettingsMinutesTooltip;
        _refresh.LostFocus += (_, _) => CommitRail();
        Grid.SetColumn(_refresh, 3);
        row.Children.Add(_refresh);

        var offsetLabel = new TextBlock
        {
            Text = Loc.Current.SettingsOffset,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 9, 0),
            Foreground = Theme.PrimaryBrush,
        };
        Grid.SetColumn(offsetLabel, 4);
        row.Children.Add(offsetLabel);

        StyleField(_offset);
        _offset.ToolTip = Loc.Current.SettingsOffsetTooltip;
        _offset.LostFocus += (_, _) => CommitRail();
        Grid.SetColumn(_offset, 5);
        row.Children.Add(_offset);

        var stack = new StackPanel();
        stack.Children.Add(row);

        // The figure's direction. Upstream calls this "Counts down instead of up,
        // figure and ring together", and it is the setting that removes the one
        // ambiguity a bare percentage cannot: a small number under a nearly empty
        // ring reads as "almost nothing left" whichever way it was counted.
        var countdown = new CheckBox
        {
            Content = Loc.Current.SettingsCountdown,
            IsChecked = AppSettings.Current.ShowsRemaining,
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = Theme.PrimaryBrush,
        };
        countdown.Checked += (_, _) => SetShowsRemaining(true);
        countdown.Unchecked += (_, _) => SetShowsRemaining(false);
        stack.Children.Add(countdown);

        stack.Children.Add(new TextBlock
        {
            Text = Loc.Current.SettingsCountdownNote,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 3, 0, 0),
            Foreground = Theme.SecondaryBrush,
        });

        block.Child = stack;
        return block;
    }

    private void SetShowsRemaining(bool value)
    {
        AppSettings.Current.ShowsRemaining = value;
        AppSettings.Current.Save();
        SettingsChanged?.Invoke();
    }

    private static void StyleField(TextBox field)
    {
        field.FontSize = 12;
        field.Padding = new Thickness(7, 5, 7, 5);
        field.Background = Theme.FieldBrush;
        field.Foreground = Theme.PrimaryBrush;
        field.BorderBrush = Theme.StrokeBrush;
    }

    // -------------------------------------------------------------- behaviour

    private void SetEnabled(Provider provider, bool enabled)
    {
        var settings = AppSettings.Current;
        if (enabled) settings.Enabled.Add(provider);
        else settings.Enabled.Remove(provider);

        settings.Normalise();
        settings.Save();
        RefreshStatus();
        SettingsChanged?.Invoke();
    }

    private void CommitRail()
    {
        var settings = AppSettings.Current;
        settings.Edge = _edge.SelectedIndex switch
        {
            1 => RailEdge.Left,
            2 => RailEdge.Top,
            _ => RailEdge.Right,
        };

        settings.RefreshMinutes = int.TryParse(_refresh.Text.Trim(), out var minutes)
            ? Math.Clamp(minutes, 1, 60)
            : settings.RefreshMinutes;

        settings.RailOffset = double.TryParse(
            _offset.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var offset)
            ? offset
            : settings.RailOffset;

        settings.Save();
        SettingsChanged?.Invoke();
    }

    private void Load()
    {
        var settings = AppSettings.Current;

        _basisTopUp.IsChecked = settings.DeepSeekBasis == DeepSeekBasis.SinceTopUp;
        _basisBalanceOnly.IsChecked = settings.DeepSeekBasis == DeepSeekBasis.BalanceOnly;
        _basisBudget.IsChecked = settings.DeepSeekBasis == DeepSeekBasis.Budget;
        _budget.Text = settings.DeepSeekBudget?.ToString(CultureInfo.InvariantCulture) ?? "";
        _currency.Text = settings.DeepSeekCurrency ?? "";

        _edge.SelectedIndex = settings.Edge switch
        {
            RailEdge.Left => 1,
            RailEdge.Top => 2,
            _ => 0,
        };
        _refresh.Text = settings.RefreshMinutes.ToString(CultureInfo.InvariantCulture);
        _offset.Text = settings.RailOffset.ToString(CultureInfo.InvariantCulture);

        RenderCodexAccounts();
    }

    /// <summary>Shows what each service last said, so Settings answers "is it working?" without a hover.</summary>
    public void RefreshStatus()
    {
        foreach (var provider in ProviderCatalog.All)
        {
            if (!_status.TryGetValue(provider, out var text)) continue;

            var settings = AppSettings.Current;
            var strings = Loc.Current;

            if (!settings.Enabled.Contains(provider))
            {
                text.Text = strings.SettingsStatusOff;
                text.Foreground = Theme.SecondaryBrush;
                continue;
            }

            var key = AccountKey.Primary(provider);
            var state = _store.StateFor(key);

            if (state?.Reading is { } reading)
            {
                var figure = reading.Fullest is not null
                    ? reading.HeadlineText(settings.ShowsRemaining)
                    : reading.RailMoney ?? strings.SettingsStatusRead;

                var direction = settings.ShowsRemaining ? strings.DirectionLeft : strings.DirectionUsed;
                var said = $"{figure} {direction}";

                text.Text = state.LastFailure is null ? said : strings.SettingsStatusStale(said);
                text.Foreground = state.LastFailure is null ? Theme.PrimaryBrush : Theme.WarningBrush;
            }
            else if (state?.LastFailure is { } failure)
            {
                text.Text = failure.Message();
                text.Foreground = Theme.WarningBrush;
            }
            else
            {
                text.Text = strings.SettingsStatusNotChecked;
                text.Foreground = Theme.SecondaryBrush;
            }

            // An unset credential is worth saying up front rather than only after a
            // failed check.
            if (provider.UsesApiKey() && !CredentialStore.Has(provider)
                && !(provider == Provider.OpenCodeGo && OpenCodeGoService.StoredKey() is not null)
                && !(provider == Provider.Zhipu && ZhipuService.StoredKey(provider) is not null))
            {
                text.Text = strings.SettingsStatusNeedsKey;
                text.Foreground = Theme.WarningBrush;
            }
        }

        RenderCodexAccounts();
    }

    private void RenderCodexAccounts()
    {
        _codexAccounts.Children.Clear();
        var settings = AppSettings.Current;

        if (!settings.Enabled.Contains(Provider.Codex))
        {
            _codexAccounts.Children.Add(Caption(Loc.Current.SettingsSwitchCodexOn));
            return;
        }

        var added = settings.Accounts.Where(a => a.Key.Provider == Provider.Codex && !a.Key.IsPrimary).ToList();

        if (added.Count == 0)
            _codexAccounts.Children.Add(Caption(Loc.Current.SignInNone));

        foreach (var account in added)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var held = AccountCredentialStore.For(account.Key);

            var label = new TextBlock
            {
                // The email the sign-in reported, so two subscriptions are not both
                // offered to the reader as "Codex".
                Text = held?.Email is { Length: > 0 } email ? email : account.DisplayLabel,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.PrimaryBrush,
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var remove = new Button
            {
                Content = Loc.Current.SettingsRemove,
                FontSize = 11,
                Padding = new Thickness(10, 4, 10, 4),
                Background = Theme.FieldBrush,
                Foreground = Theme.PrimaryBrush,
                BorderBrush = Theme.StrokeBrush,
            };
            remove.Click += (_, _) =>
            {
                // The login goes with the row. Leaving it behind would keep a
                // refresh token for an account the reader has just taken off the
                // rail, and re-adding would silently inherit it.
                AccountCredentialStore.Remove(account.Key);
                settings.Accounts.Remove(account);
                settings.Save();
                SettingsChanged?.Invoke();
                RefreshStatus();
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);

            _codexAccounts.Children.Add(row);
        }

        var signIn = new Button
        {
            Content = Loc.Current.SignInAdd,
            FontSize = 11.5,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Theme.FieldBrush,
            Foreground = Theme.PrimaryBrush,
            BorderBrush = Theme.StrokeBrush,
        };
        signIn.Click += (_, _) => SignInAnotherAccount();
        _codexAccounts.Children.Add(signIn);
    }

    /// <summary>
    /// Runs the device-code sign-in and, on success, puts the account on the rail.
    /// </summary>
    /// <remarks>
    /// There is no paste field beside this on purpose — see
    /// <see cref="CodexSignInWindow"/>. A pasted token stops working in about ten
    /// days, and the only way to renew one is the CLI's own refresh token.
    /// </remarks>
    private void SignInAnotherAccount()
    {
        var settings = AppSettings.Current;

        var dialog = new CodexSignInWindow { Owner = this };
        dialog.ShowDialog();

        if (dialog.Result is not { } credentials) return;

        // A slot generated once and never reused, so removing an account and adding
        // another cannot inherit the first one's settings or cache.
        var key = new AccountKey(Provider.Codex, $"codex-{Guid.NewGuid():N}"[..14]);

        AccountCredentialStore.Set(key, credentials);

        settings.Accounts.Add(new MonitoredAccount
        {
            Key = key,
            Label = dialog.Email ?? Loc.Current.SignInAccountFallback,
            Enabled = true,
            IsSignedIn = true,
        });

        settings.Save();
        SettingsChanged?.Invoke();
        RefreshStatus();
    }
}


