using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PulseWin.Core;
using PulseWin.Localization;
using PulseWin.Services;
using PulseWin.Storage;

namespace PulseWin.Ui;

/// <summary>
/// The card that opens when a ring is pointed at.
///
/// <para>
/// Every figure on it is one the provider reported. Where a provider reported
/// nothing, the card says so rather than showing a zero — the distinction between
/// "0% used" and "no reading" is the one this whole app is built around, and a
/// card is where it is easiest to lose.
/// </para>
/// </summary>
internal sealed class DetailCard : Popup
{
    private readonly StackPanel _body;

    public DetailCard()
    {
        AllowsTransparency = true;
        StaysOpen = true;
        Placement = PlacementMode.Right;
        HorizontalOffset = 10;
        VerticalOffset = -12;
        PopupAnimation = PopupAnimation.Fade;

        _body = new StackPanel { Width = 268 };

        var (root, content) = Theme.Card(new CornerRadius(12), 13);
        content.Child = _body;
        Child = root;
    }

    public void Show(RailRow row, UIElement anchor)
    {
        _body.Children.Clear();

        var account = row.Account;
        var state = row.State;
        var reading = state.Reading;

        _body.Children.Add(new TextBlock
        {
            Text = account.DisplayLabel,
            FontFamily = Theme.Font,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.PrimaryBrush,
        });

        var subtitle = account.Key.Provider.DisplayName();
        if (reading?.Plan is { } plan) subtitle += $" · {plan}";
        _body.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontFamily = Theme.Font,
            FontSize = 11,
            Foreground = Theme.SecondaryBrush,
            Margin = new Thickness(0, 2, 0, 0),
        });

        if (reading is not null && reading.Windows.Count > 0)
        {
            _body.Children.Add(Divider());

            // The rail carries a bare figure, and a bare figure is ambiguous — a
            // small number under a nearly empty ring reads as "almost nothing
            // left" whichever way it was counted. The card is where the word goes.
            var showsRemaining = AppSettings.Current.ShowsRemaining;
            _body.Children.Add(new TextBlock
            {
                Text = showsRemaining ? Loc.Current.CardCountingDown : Loc.Current.CardCountingUp,
                FontFamily = Theme.Font,
                FontSize = 10.5,
                Margin = new Thickness(0, 0, 0, 2),
                Foreground = Theme.SecondaryBrush,
            });

            foreach (var window in reading.Windows)
                _body.Children.Add(WindowRow(window, showsRemaining));
        }
        else if (reading is not null)
        {
            _body.Children.Add(Divider());
            _body.Children.Add(new TextBlock
            {
                Text = Loc.Current.CardNoLimitOnlyBalance,
                FontFamily = Theme.Font,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.SecondaryBrush,
            });
        }

        if (reading?.CreditBalance is { } balance)
        {
            _body.Children.Add(Divider());
            _body.Children.Add(KeyValue(Loc.Current.CardBalance, balance));
        }

        if (state.LastFailure is { } failure)
        {
            _body.Children.Add(Divider());
            _body.Children.Add(new TextBlock
            {
                Text = failure.Message(),
                FontFamily = Theme.Font,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.WarningBrush,
            });

            // Say how old the figures above are, because they are not from this
            // attempt. Quietly showing stale numbers as if they were live is the
            // failure mode this line exists to prevent.
            if (reading is not null)
            {
                _body.Children.Add(new TextBlock
                {
                    Text = Loc.Current.CardFiguresAreFrom(Ago(reading.ObservedAt)),
                    FontFamily = Theme.Font,
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = Theme.SecondaryBrush,
                });
            }
        }

        _body.Children.Add(Divider());
        _body.Children.Add(new TextBlock
        {
            Text = reading is null
                ? Loc.Current.CardNeverRead
                : Loc.Current.CardChecked(Ago(reading.ObservedAt)),
            FontFamily = Theme.Font,
            FontSize = 10.5,
            Foreground = Theme.SecondaryBrush,
        });

        PlacementTarget = anchor;
        IsOpen = true;
    }

    public void Hide() => IsOpen = false;

    private static UIElement WindowRow(UsageWindow window, bool showsRemaining)
    {
        var now = DateTimeOffset.Now;
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text = window.Name,
            FontFamily = Theme.Font,
            FontSize = 11.5,
            Foreground = Theme.PrimaryBrush,
        });

        var reset = RingControl.CountdownText(window.ResetsAt, now);
        if (reset.Length > 0)
        {
            left.Children.Add(new TextBlock
            {
                Text = Loc.Current.CardResetsIn(reset),
                FontFamily = Theme.Font,
                FontSize = 10,
                Foreground = Theme.SecondaryBrush,
            });
        }

        // The window clock, shown only where the provider stated a length to divide
        // by. A sort key is not a length, and dividing by one draws a fraction
        // nobody reported. It is always "elapsed", never "left", because the clock
        // runs one way whichever way the figure does.
        if (window.ElapsedFraction(now) is { } elapsed)
        {
            left.Children.Add(new TextBlock
            {
                Text = Loc.Current.CardWindowElapsed(UsageWindow.Figure(elapsed)),
                FontFamily = Theme.Font,
                FontSize = 10,
                Foreground = Theme.SecondaryBrush,
            });
        }

        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(new TextBlock
        {
            // **The word, not just the number.** This is the line that makes the
            // figure unambiguous, and upstream puts it in exactly this place for
            // exactly this reason.
            Text = showsRemaining
                ? Loc.Current.CardSuffixLeft(window.PercentText(remaining: true))
                : Loc.Current.CardSuffixUsed(window.PercentText()),
            FontFamily = Theme.Font,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = window.IsExhausted ? Theme.WarningBrush : Theme.PrimaryBrush,
        });

        if (window.IsExhausted)
        {
            right.Children.Add(new TextBlock
            {
                Text = Loc.Current.CardSpent,
                FontFamily = Theme.Font,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = Theme.WarningBrush,
            });
        }

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return grid;
    }

    private static UIElement KeyValue(string key, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = key,
            FontFamily = Theme.Font,
            FontSize = 11.5,
            Foreground = Theme.PrimaryBrush,
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var figure = new TextBlock
        {
            Text = value,
            FontFamily = Theme.Font,
            FontSize = 11.5,
            Foreground = Theme.PrimaryBrush,
        };
        Grid.SetColumn(figure, 1);
        grid.Children.Add(figure);

        return grid;
    }

    private static UIElement Divider() => new Border
    {
        Height = 1,
        Margin = new Thickness(0, 9, 0, 9),
        Background = Theme.Brush(Theme.Stroke),
    };

    private static string Ago(DateTimeOffset when)
    {
        var strings = Loc.Current;
        var span = DateTimeOffset.Now - when;

        if (span < TimeSpan.Zero) return strings.AgoNow;
        if (span.TotalSeconds < 45) return strings.AgoNow;
        if (span.TotalMinutes < 60) return strings.AgoMinutes((int)span.TotalMinutes);
        if (span.TotalHours < 24) return strings.AgoHours((int)span.TotalHours);
        return strings.AgoDays((int)span.TotalDays);
    }
}
