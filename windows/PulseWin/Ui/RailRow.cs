using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PulseWin.Core;
using PulseWin.Services;
using PulseWin.Storage;

namespace PulseWin.Ui;

/// <summary>
/// One account's item on the rail: its ring, and the one figure that fits.
/// </summary>
/// <remarks>
/// The arrangement depends on which way the rail runs, and the two are genuinely
/// different layouts rather than one rotated:
/// <list type="bullet">
/// <item>
/// <b>On a side edge</b> the ring sits at the top of the item and the figure below
/// it, because there is no width to put the figure beside.
/// </item>
/// <item>
/// <b>On the top edge</b> the figure sits <i>beside</i> the ring. Below would work,
/// but it makes the strip 77 pixels thick to carry an 11-pixel line of text, and a
/// strip across the top of the screen is read as a bar — the thick version reads as
/// a panel that happens to be up there.
/// </item>
/// </list>
/// The first version of this only ever set a <c>Height</c>, so on the top edge each
/// item shrank to the width of its ring and the items touched: nothing was wrong
/// with the spacing value, there simply was not one.
/// </remarks>
internal sealed class RailRow : Grid
{
    private readonly RingControl _ring;
    private readonly TextBlock _figure;
    private readonly Border _hit;
    private bool _horizontal;

    public MonitoredAccount Account { get; }

    public AccountState State { get; private set; }

    public event Action<RailRow>? PointerEntered;

    public event Action<RailRow>? PointerLeft;

    public RailRow(MonitoredAccount account, AccountState state, bool horizontal)
    {
        Account = account;
        State = state;

        Background = Brushes.Transparent;

        _ring = new RingControl
        {
            Width = Theme.RingSize,
            Height = Theme.RingSize,
            Icon = ProviderIcons.For(account.Key.Provider),
            Glyph = account.Key.Provider.Glyph(),
        };

        _figure = new TextBlock
        {
            FontFamily = Theme.Font,
            FontSize = 10,
        };

        // An attached property, so it cannot go in an initialiser. Display mode snaps
        // stems to whole pixels, which is what makes a three-character figure legible
        // at this size; the ring's mark wants the opposite and asks for Ideal by
        // drawing through FormattedText instead.
        TextOptions.SetTextFormattingMode(_figure, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(_figure, TextRenderingMode.ClearType);

        _hit = new Border
        {
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(2, 1, 2, 1),
            Background = Brushes.Transparent,
        };

        MouseEnter += (_, _) =>
        {
            _hit.Background = Theme.HoverBrush;
            PointerEntered?.Invoke(this);
        };
        MouseLeave += (_, _) =>
        {
            _hit.Background = Brushes.Transparent;
            PointerLeft?.Invoke(this);
        };

        Arrange(horizontal);
        Update(state);
    }

    /// <summary>Lays the item out for the direction the rail is running.</summary>
    public void SetOrientation(bool horizontal) => Arrange(horizontal);

    private void Arrange(bool horizontal)
    {
        if (_horizontal == horizontal && Children.Count > 0) return;
        _horizontal = horizontal;

        Children.Clear();
        ColumnDefinitions.Clear();

        if (horizontal)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
            // A fixed column, so the figures line up down the strip instead of each
            // item being as wide as its own text and the row edges wandering.
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.WideFigureWidth) });

            Height = Theme.HorizontalThickness - 2;
            Width = double.NaN;
            Margin = new Thickness(0, 0, Theme.RowGap, 0);

            _ring.HorizontalAlignment = HorizontalAlignment.Center;
            _ring.VerticalAlignment = VerticalAlignment.Center;

            _figure.HorizontalAlignment = HorizontalAlignment.Left;
            _figure.VerticalAlignment = VerticalAlignment.Center;
            _figure.TextAlignment = TextAlignment.Left;
            _figure.Margin = new Thickness(0);

            // Side by side, so both sit in the same row.
            Grid.SetRow(_ring, 0);
            Grid.SetRow(_figure, 0);
            Grid.SetColumnSpan(_hit, 3);
            Grid.SetColumn(_hit, 0);
            Grid.SetColumn(_ring, 0);
            Grid.SetColumn(_figure, 2);
        }
        else
        {
            // **Two auto rows, not one fixed height.** The figure has to sit directly
            // under the ring it reads, and a fixed row height with the figure
            // bottom-aligned is exactly what put fourteen pixels between them. Letting
            // the rows measure keeps the number attached whatever the font does.
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Height = double.NaN;
            Width = double.NaN;
            Margin = new Thickness(0, 0, 0, Theme.AccountGap);

            _ring.HorizontalAlignment = HorizontalAlignment.Center;
            _ring.VerticalAlignment = VerticalAlignment.Center;

            _figure.HorizontalAlignment = HorizontalAlignment.Center;
            _figure.VerticalAlignment = VerticalAlignment.Center;
            _figure.TextAlignment = TextAlignment.Center;
            _figure.Margin = new Thickness(0, Theme.FigureGap, 0, 0);

            Grid.SetRow(_ring, 0);
            Grid.SetRow(_figure, 1);
            Grid.SetColumnSpan(_hit, 1);
            Grid.SetColumn(_hit, 0);
            Grid.SetColumn(_ring, 0);
            Grid.SetColumn(_figure, 0);
        }

        Children.Add(_hit);
        Children.Add(_ring);
        Children.Add(_figure);
    }

    /// <summary>Re-reads the palette. The ring draws in <c>OnRender</c>, so it only needs invalidating.</summary>
    public void ApplyTheme()
    {
        _ring.InvalidateVisual();
        Update(State);
    }

    public void Update(AccountState state)
    {
        State = state;
        var now = DateTimeOffset.Now;
        var reading = state.Reading;

        // A reading that is still the last good one, while a newer attempt has
        // failed, is drawn dimmed rather than blanked. Going blank on a network
        // hiccup loses information the app already has.
        var live = state.LastFailure is null;
        _ring.HasReading = reading is not null;

        if (reading is null)
        {
            _ring.UsedFraction = 0;
            _ring.HasFraction = false;
            _ring.ElapsedFraction = double.NaN;
            _ring.IsExhausted = false;

            // Short, because on a horizontal rail this column is 46 pixels wide.
            _figure.Text = state.LastFailure is null ? "—" : "!";
            _figure.Foreground = state.LastFailure is null ? Theme.SecondaryBrush : Theme.WarningBrush;
            ToolTip = state.LastFailure?.Message();
            return;
        }

        var fullest = reading.Fullest;
        var showsRemaining = AppSettings.Current.ShowsRemaining;

        _ring.UsedFraction = fullest?.UsedFraction ?? 0;
        _ring.IsExhausted = fullest?.IsExhausted ?? false;
        // Money with no window has no fraction to draw, and that is not the same as a
        // fraction of zero. See RingControl.HasFraction.
        _ring.HasFraction = fullest is not null;
        _ring.ElapsedFraction = fullest?.ElapsedFraction(now) ?? double.NaN;
        _ring.ShowsRemaining = showsRemaining;

        // Money where there is no percentage. This is the DeepSeek case: a reading
        // with a balance and no allowance has no fraction to show, and inventing one
        // is the single thing this app must not do.
        var figure = fullest is null
            ? reading.RailMoney ?? "—"
            : fullest.PercentText(showsRemaining);

        _figure.Text = figure;
        _figure.Foreground = !live
            ? Theme.SecondaryBrush
            : fullest?.IsExhausted == true
                ? Theme.WarningBrush
                : Theme.PrimaryBrush;

        ToolTip = null;
    }
}
