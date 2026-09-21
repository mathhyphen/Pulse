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

    /// <summary>
    /// The gap between a ring and its own figure, measured the way it looks on screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Negative, and that is not a mistake.</b> The figure is that ring's reading,
    /// not a caption for the row, so it has to read as attached to it — and it was
    /// not: this used to be fourteen, the same value the horizontal gap uses, which
    /// pushed the number to the bottom of the row and left the space between them
    /// reading as a divider between two accounts.
    /// </para>
    /// <para>
    /// Setting it to two was not enough either, and the reason is not obvious. A
    /// <c>TextBlock</c>'s line box is taller than the digits inside it, because the
    /// font reserves ascent and descent whether or not the glyphs use them — four
    /// pixels of it, measured. So a margin of zero still leaves white space above the
    /// glyphs. This cancels that reserve, which makes the number here the gap a reader
    /// actually sees rather than the gap the layout engine was told about.
    /// <c>LineHeight</c> would be the tidier mechanism and would clip a descender the
    /// day a figure contains one.
    /// </para>
    /// </remarks>
    public const double FigureGap = -3;

    /// <summary>Air between one account and the next, so each pair reads as a pair.</summary>
    public const double AccountGap = 6;

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
    /// The surface colour with no transparency at all, for anything being read.
    /// </summary>
    /// <remarks>
    /// <b>The hover card uses this, and the rail does not.</b> A rail is scenery and
    /// can be as see-through as the reader likes; a card is where the numbers and
    /// their explanations are, and letting the desktop through it is how somebody ends
    /// up unable to read the very thing they hovered to read. The first version of the
    /// transparency setting tinted both, and a 45% card over a light desktop was
    /// nearly invisible.
    /// </remarks>
    public static SolidColorBrush OpaqueSurfaceBrush { get; private set; } = Brushes.Black;

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

    /// <summary>
    /// Rebuilds every brush for the chosen theme, backdrop and surface opacity.
    /// </summary>
    /// <param name="opacity">
    /// How opaque a translucent surface is. Ignored when solid, which is opaque by
    /// definition.
    /// </param>
    public static void Apply(AppTheme theme, Backdrop backdrop, double opacity = 0.55)
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
        // A translucent surface is a degree, not a choice between two of them, so the
        // reader sets it: see AppSettings.SurfaceOpacity. Solid ignores the number,
        // being opaque by definition.
        var alpha = backdrop == Backdrop.Acrylic
            ? (byte)Math.Round(Math.Clamp(opacity, 0.15, 1.0) * 255)
            : (byte)0xF0;

        SurfaceBrush = Brush(Argb(alpha, Surface.R, Surface.G, Surface.B));
        // Always full alpha, whatever the reader set the rail to. See the note on it.
        OpaqueSurfaceBrush = Brush(Surface);
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
    /// The rounded slab.
    /// </summary>
    /// <param name="opaque">
    /// True for a surface that is being <i>read</i> rather than sat on — the hover
    /// card. It ignores the rail's transparency, because a card is where the numbers
    /// and their explanations are, and letting the desktop through it is how a reader
    /// ends up unable to see the thing they hovered to see.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Two layers when solid, one when translucent.</b> A WPF <c>Effect</c> renders
    /// its element's whole subtree into an intermediate surface first, and text drawn
    /// through that surface loses ClearType — a 9-point figure comes out visibly soft.
    /// So the shadow lives on a background-only sibling and the content is drawn over
    /// it, which keeps the shadow and gives the text back its subpixel rendering.
    /// </para>
    /// <para>
    /// That sibling has to be a <i>filled</i> rounded rect, because an effect can only
    /// cast a shadow from something. Which means it also contributes its own opacity —
    /// and in the translucent case that is wrong twice over: the two layers stack, so
    /// a surface meant to be 55% opaque comes out at 80%, and what shows through is
    /// the layer underneath rather than what is behind the window. So the translucent
    /// case gets a single layer and no shadow. A glass panel that casts a hard shadow
    /// is not what was asked for anyway.
    /// </para>
    /// </remarks>
    public static (Grid Root, Border Content) Card(CornerRadius radius, double padding, bool opaque = false)
    {
        var root = new Grid();

        // The rail follows the reader's transparency; a card never does.
        var translucent = !opaque && Backdrop == Backdrop.Acrylic;
        var fill = opaque ? OpaqueSurfaceBrush : SurfaceBrush;

        if (translucent)
        {
            var glass = new Border
            {
                CornerRadius = radius,
                Background = fill,
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(padding),
            };

            root.Children.Add(glass);
            return (root, glass);
        }

        var background = new Border
        {
            CornerRadius = radius,
            Background = fill,
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

