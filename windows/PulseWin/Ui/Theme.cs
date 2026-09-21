using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using PulseWin.Core;
using PulseWin.Storage;

namespace PulseWin.Ui;

/// <summary>
/// Every colour the interface uses.
/// </summary>
/// <remarks>
/// The brushes are <b>mutable statics rebuilt in place</b> rather than readonly
/// fields, because the theme is a setting and every surface built before a change
/// would otherwise keep the palette of the day. Callers that hold a reference to a
/// brush (rows, the card) re-read them through <c>ApplyTheme</c>.
/// </remarks>
public static class Theme
{
    /// <summary>The rail's own thickness, which the money formatter is measured against.</summary>
    public const double RailWidth = 52;

    public const double RingSize = 34;

    public const double RingSpacing = 14;

    /// <summary>Gap between two items on a horizontal rail.</summary>
    public const double RowGap = 8;

    /// <summary>Width of the figure column on a horizontal rail, so the rows line up.</summary>
    public const double WideFigureWidth = 46;

    /// <summary>How thick a horizontal rail is. Ring plus breathing room, not ring plus a text row.</summary>
    public const double HorizontalThickness = RingSize + 16 + 2;

    public static bool IsDark { get; private set; } = true;

    public static Backdrop Backdrop { get; private set; } = Backdrop.Solid;

    // --------------------------------------------------------------- the palette

    public static Color WindowBackground { get; private set; }

    public static Color Surface { get; private set; }

    public static Color Panel { get; private set; }

    public static Color Field { get; private set; }

    public static Color PrimaryText { get; private set; }

    public static Color SecondaryText { get; private set; }

    public static Color WarningText { get; private set; }

    public static Color StrokeColour { get; private set; }

    public static Color HoverColour { get; private set; }

    public static Color RingTrack { get; private set; }

    public static Color RingClock { get; private set; }

    public static Color RingMark { get; private set; }

    public static SolidColorBrush SurfaceBrush { get; private set; } = Brushes.Black;

    /// <summary>
    /// The surface colour with no transparency, for the shadow layer underneath.
    /// </summary>
    /// <remarks>
    /// The shadow sits on its own sibling rather than on the content, because a WPF
    /// <c>Effect</c> renders its element's whole subtree through an intermediate
    /// surface and costs the text its ClearType. Keeping that layer opaque is what
    /// makes the shadow read as a shadow rather than as a halo around something
    /// half-transparent.
    /// </remarks>
    public static SolidColorBrush SurfaceShadowBrush { get; private set; } = Brushes.Black;

    public static SolidColorBrush PanelBrush { get; private set; } = Brushes.Black;

    public static SolidColorBrush FieldBrush { get; private set; } = Brushes.Black;

    public static SolidColorBrush PrimaryBrush { get; private set; } = Brushes.White;

    public static SolidColorBrush SecondaryBrush { get; private set; } = Brushes.Gray;

    public static SolidColorBrush WarningBrush { get; private set; } = Brushes.Orange;

    public static SolidColorBrush StrokeBrush { get; private set; } = Brushes.Gray;

    public static SolidColorBrush HoverBrush { get; private set; } = Brushes.Transparent;

    public static SolidColorBrush WindowBrush { get; private set; } = Brushes.Black;

    public static FontFamily Font { get; } =
        new("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI, Microsoft YaHei, SimSun");

    /// <summary>Rebuilds every brush for the chosen theme and backdrop.</summary>
    public static void Apply(AppTheme theme, Backdrop backdrop)
    {
        IsDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => WindowsIsLight() is false,
        };

        Backdrop = backdrop;

        if (IsDark)
        {
            WindowBackground = Rgb(0x14, 0x14, 0x16);
            Surface = Rgb(0x1C, 0x1C, 0x1E);
            Panel = Rgb(0x1F, 0x1F, 0x23);
            Field = Rgb(0x2A, 0x2A, 0x30);
            PrimaryText = Rgb(0xF2, 0xF2, 0xF7);
            SecondaryText = Rgb(0x9A, 0x9A, 0xA2);
            WarningText = Rgb(0xFF, 0xB3, 0x40);
            StrokeColour = Argb(0x28, 0xFF, 0xFF, 0xFF);
            HoverColour = Argb(0x30, 0xFF, 0xFF, 0xFF);
            // White at a fifth is the track on a dark slab; the mark takes the
            // foreground, because on this surface colour means usage and the mark
            // must not compete with the arc.
            RingTrack = Argb(0x22, 0xFF, 0xFF, 0xFF);
            RingClock = Argb(0x55, 0xFF, 0xFF, 0xFF);
            RingMark = Rgb(0xF2, 0xF2, 0xF7);
        }
        else
        {
            WindowBackground = Rgb(0xF4, 0xF4, 0xF7);
            Surface = Rgb(0xFC, 0xFC, 0xFD);
            Panel = Rgb(0xFF, 0xFF, 0xFF);
            Field = Rgb(0xEC, 0xEC, 0xEF);
            PrimaryText = Rgb(0x1C, 0x1C, 0x1E);
            SecondaryText = Rgb(0x6E, 0x6E, 0x73);
            // Not the same amber: at this weight on white it fails contrast, and a
            // warning nobody can read is not a warning.
            WarningText = Rgb(0xA8, 0x5A, 0x00);
            StrokeColour = Argb(0x22, 0x00, 0x00, 0x00);
            HoverColour = Argb(0x16, 0x00, 0x00, 0x00);
            RingTrack = Argb(0x1E, 0x00, 0x00, 0x00);
            RingClock = Argb(0x4A, 0x00, 0x00, 0x00);
            RingMark = Rgb(0x1C, 0x1C, 0x1E);
        }

        // Acrylic needs something to see through. At the solid opacity the blur is
        // behind an almost-opaque slab and the setting does nothing visible, which is
        // worse than not offering it.
        var alpha = backdrop == Backdrop.Acrylic ? (byte)0x8C : (byte)0xF0;

        SurfaceBrush = Brush(Argb(alpha, Surface.R, Surface.G, Surface.B));
        SurfaceShadowBrush = Brush(Argb(alpha, Surface.R, Surface.G, Surface.B));
        PanelBrush = Brush(Panel);
        FieldBrush = Brush(Field);
        PrimaryBrush = Brush(PrimaryText);
        SecondaryBrush = Brush(SecondaryText);
        WarningBrush = Brush(WarningText);
        StrokeBrush = Brush(StrokeColour);
        HoverBrush = Brush(HoverColour);

        // The settings window and the sign-in dialog are ordinary windows and are
        // never transparent, so they take the solid colour whatever the backdrop is.
        WindowBrush = Brush(WindowBackground);
    }

    /// <summary>Whether Windows is set to the light app theme.</summary>
    private static bool? WindowsIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            // `AppsUseLightTheme` is what Explorer's switch writes, and 0 means dark.
            // A missing value is a Windows that has never been switched, which is
            // dark for the app theme.
            return key?.GetValue("AppsUseLightTheme") is int value ? value != 0 : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public static SolidColorBrush Brush(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// The rounded dark slab, as <b>two layers</b>.
    /// </summary>
    /// <remarks>
    /// The split is not decoration. A WPF <c>Effect</c> renders its element's whole
    /// subtree into an intermediate surface first, and text drawn through that
    /// surface loses ClearType — a 9-point figure comes out visibly soft. Putting the
    /// shadow on a background-only sibling and the content on top of it keeps the
    /// shadow and gives the text back its subpixel rendering, because siblings are
    /// rendered independently.
    /// </remarks>
    public static (Grid Root, Border Content) Card(CornerRadius radius, double padding)
    {
        var root = new Grid();

        var background = new Border
        {
            CornerRadius = radius,
            Background = SurfaceShadowBrush,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 2,
                Opacity = Backdrop == Backdrop.Acrylic ? 0.25 : 0.5,
                Direction = 270,
            },
        };

        var content = new Border
        {
            CornerRadius = radius,
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(padding),
        };

        root.Children.Add(background);
        root.Children.Add(content);
        return (root, content);
    }

    /// <summary>
    /// The corner rounding for a slab docked to an edge.
    /// </summary>
    /// <remarks>
    /// <b>The docked side is squared off.</b> Rounding all four corners and sitting
    /// the slab flush against the screen leaves two small crescents of desktop
    /// showing through at the corners, which reads as a floating panel that happens
    /// to be near the edge rather than one attached to it.
    /// </remarks>
    public static CornerRadius DockedCorners(RailEdge? edge, double radius) => edge switch
    {
        // WPF order is top-left, top-right, bottom-right, bottom-left.
        RailEdge.Right => new CornerRadius(radius, 0, 0, radius),
        RailEdge.Left => new CornerRadius(0, radius, radius, 0),
        RailEdge.Top => new CornerRadius(0, 0, radius, radius),
        _ => new CornerRadius(radius),
    };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
}

