using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PulseWin.Core;
using PulseWin.Services;
using PulseWin.Storage;

namespace PulseWin.Ui;

/// <summary>Shared brushes and metrics, so the rail and the card cannot drift apart.</summary>
public static class Theme
{
    public static readonly Color Surface = Color.FromRgb(0x1C, 0x1C, 0x1E);
    public static readonly Color Stroke = Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF);
    public static readonly Color PrimaryText = Color.FromRgb(0xF2, 0xF2, 0xF7);
    public static readonly Color SecondaryText = Color.FromRgb(0x9A, 0x9A, 0xA2);
    public static readonly Color WarningText = Color.FromRgb(0xFF, 0xB3, 0x40);

    /// <summary>The rail's own thickness, which the money formatter is measured against.</summary>
    public const double RailWidth = 52;

    public const double RingSize = 34;

    public const double RingSpacing = 14;

    public static SolidColorBrush Brush(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    public static readonly SolidColorBrush SurfaceBrush = Brush(Color.FromArgb(0xF0, Surface.R, Surface.G, Surface.B));
    public static readonly SolidColorBrush PrimaryBrush = Brush(PrimaryText);
    public static readonly SolidColorBrush SecondaryBrush = Brush(SecondaryText);
    public static readonly SolidColorBrush WarningBrush = Brush(WarningText);
    public static readonly SolidColorBrush HoverBrush = Brush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

    public static FontFamily Font { get; } = new("Segoe UI Variable Display, Segoe UI");

    /// <summary>
    /// A rounded dark slab with a drop shadow, as <b>two layers</b>.
    /// </summary>
    /// <remarks>
    /// The split is not decoration. A WPF <c>Effect</c> renders its element's
    /// whole subtree into an intermediate surface first, and text drawn through
    /// that surface loses ClearType — a 9-point figure comes out visibly soft, and
    /// the first build of this rail shipped exactly that. Putting the shadow on a
    /// background-only sibling and the content on top of it keeps the shadow and
    /// gives the text back its subpixel rendering, because siblings are rendered
    /// independently.
    /// </remarks>
    public static (Grid Root, Border Content) Card(double radius, double padding)
    {
        var root = new Grid();

        var background = new Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = SurfaceBrush,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 2,
                Opacity = 0.5,
                Direction = 270,
            },
        };

        var content = new Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = SurfaceBrush,
            BorderBrush = Brush(Stroke),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(padding),
        };

        root.Children.Add(background);
        root.Children.Add(content);
        return (root, content);
    }
}

/// <summary>
/// One account's row on the rail: its ring, and the one figure that fits.
/// </summary>
internal sealed class RailRow : Grid
{
    private readonly RingControl _ring;
    private readonly TextBlock _figure;
    private readonly Border _hit;

    public MonitoredAccount Account { get; }

    public AccountState State { get; private set; }

    public event Action<RailRow>? PointerEntered;

    public event Action<RailRow>? PointerLeft;

    public RailRow(MonitoredAccount account, AccountState state)
    {
        Account = account;
        State = state;

        Height = Theme.RingSize + Theme.RingSpacing + 11;
        Background = Brushes.Transparent;

        _ring = new RingControl
        {
            Width = Theme.RingSize,
            Height = Theme.RingSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Accent = AccentColour(account.Key.Provider),
            Glyph = account.Key.Provider.Glyph(),
        };

        _figure = new TextBlock
        {
            FontFamily = Theme.Font,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 1),
            Foreground = Theme.PrimaryBrush,
        };

        // An attached property, so it cannot go in the initialiser above. Display
        // mode snaps stems to whole pixels, which is what makes a three-character
        // figure legible at this size; the ring's glyph wants the opposite and asks
        // for Ideal by drawing through FormattedText instead.
        TextOptions.SetTextFormattingMode(_figure, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(_figure, TextRenderingMode.ClearType);

        _hit = new Border
        {
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(2, 1, 2, 1),
            Background = Brushes.Transparent,
        };

        Children.Add(_hit);
        Children.Add(_ring);
        Children.Add(_figure);

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

        Update(state);
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
            _ring.ElapsedFraction = double.NaN;
            _ring.IsExhausted = false;
            _figure.Text = state.LastFailure is null ? "—" : "!";
            _figure.Foreground = state.LastFailure is null ? Theme.SecondaryBrush : Theme.WarningBrush;
            ToolTip = state.LastFailure?.Message();
            return;
        }

        var fullest = reading.Fullest;
        var showsRemaining = AppSettings.Current.ShowsRemaining;

        _ring.UsedFraction = fullest?.UsedFraction ?? 0;
        _ring.IsExhausted = fullest?.IsExhausted ?? false;
        _ring.ElapsedFraction = fullest?.ElapsedFraction(now) ?? double.NaN;
        _ring.ShowsRemaining = showsRemaining;

        // Money where there is no percentage. This is the DeepSeek case: a reading
        // with a balance and no allowance has no fraction to show, and inventing
        // one is the single thing this app must not do.
        var figure = fullest is null
            ? reading.RailMoney ?? "—"
            : fullest.PercentText(showsRemaining);

        _figure.Text = figure;
        _figure.Foreground = !live
            ? Theme.SecondaryBrush
            : fullest?.IsExhausted == true
                ? Theme.WarningBrush
                : Theme.PrimaryBrush;
    }

    private static Color AccentColour(Provider provider)
    {
        var hex = provider.AccentHex().TrimStart('#');
        return Color.FromRgb(
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }
}
