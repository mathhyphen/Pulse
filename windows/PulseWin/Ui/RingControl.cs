using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PulseWin.Ui;

/// <summary>
/// One account's ring.
///
/// <para>
/// Drawn directly rather than composed from shapes, because everything about it is
/// arithmetic: an arc from a fraction, a secondary outer arc for how much of the
/// window has gone by, and a colour that comes off a ramp rather than from a
/// style. A template would express none of that and would hide the numbers.
/// </para>
/// <para>
/// The convention that matters: the arc is drawn <b>clockwise from twelve
/// o'clock</b>, and it shows <b>what is gone</b>. The full ring is a spent
/// account. An account with nothing spent is an empty ring with the track still
/// visible, which is why the track is never omitted — an empty ring and a missing
/// one look identical otherwise, and only one of them means "nothing spent".
/// </para>
/// </summary>
public sealed class RingControl : FrameworkElement
{
    /// <summary>0...1 gone. A provider may report past 1 once a limit is exceeded.</summary>
    public double UsedFraction { get; set; }

    /// <summary>
    /// Whether the arc counts down — what is left — instead of up.
    /// </summary>
    /// <remarks>
    /// <b>The colour is not flipped with it.</b> How close a limit is does not
    /// change because the figure beside it was counted from the other end, so a
    /// ring with a sliver left is a small red arc rather than a large one. Inverting
    /// both would paint an almost-spent account a reassuring green.
    /// <para>
    /// An exhausted window is always a full ring, whichever way it is counted:
    /// there is nothing left to show the size of, and "spent" is the one reading
    /// that has to be unmistakable at a glance.
    /// </para>
    /// </remarks>
    public bool ShowsRemaining { get; set; }

    /// <summary>The provider's own verdict. Wins over the ramp when set.</summary>
    public bool IsExhausted { get; set; }

    /// <summary>Whether there is a reading at all. False dims the whole ring.</summary>
    public bool HasReading { get; set; } = true;

    /// <summary>
    /// Whether this account reported a fraction to draw at all.
    /// </summary>
    /// <remarks>
    /// <b>Not the same question as <see cref="HasReading"/>, and not the same as
    /// <see cref="UsedFraction"/> being zero.</b> DeepSeek in its balance-only mode
    /// answers with money and no window, so it has no fraction — and a fraction of
    /// zero means "nothing used", which is a different statement. Counting down made
    /// the two collide: <c>1 - 0</c> is a full ring, so an account with no allowance
    /// at all drew a complete green circle saying it had everything left. With this
    /// false, no arc is drawn and the figure beside it is the whole reading.
    /// </remarks>
    public bool HasFraction { get; set; } = true;

    /// <summary>How far through the window, 0...1, for the outer arc. NaN draws none.</summary>
    public double ElapsedFraction { get; set; } = double.NaN;

    /// <summary>
    /// The provider's mark, in its own 24×24 coordinate space.
    /// </summary>
    /// <remarks>
    /// <b>Drawn as a monochrome template, not in brand colours.</b> Every mark is
    /// <c>fill="currentColor"</c>, which is what that attribute means: it has no
    /// colour of its own and takes the foreground it is given. That is the point
    /// rather than a limitation — colour on this surface means <i>usage</i>, so a
    /// brand tint would put two meanings on one ring and make a green mark beside a
    /// green arc say nothing.
    /// </remarks>
    public Geometry? Icon { get; set; }

    /// <summary>The letter to fall back to when <see cref="Icon"/> could not be read.</summary>
    public string Glyph { get; set; } = "";

    public double RingThickness { get; set; } = 3.5;

    public double ClockThickness { get; set; } = 1.5;

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 1) return;

        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        var outer = size / 2 - 0.5;
        var radius = outer - RingThickness / 2;

        // The track. Always drawn, so "nothing spent" is distinguishable from
        // "no data" — and dimmed further when there is no reading at all.
        var trackColour = Theme.RingTrack;
        if (!HasReading)
            trackColour = Color.FromArgb((byte)(trackColour.A / 2), trackColour.R, trackColour.G, trackColour.B);

        var track = new Pen(new SolidColorBrush(trackColour), RingThickness);
        track.Freeze();
        dc.DrawEllipse(null, track, centre, radius, radius);

        // The elapsed-window arc sits just outside the main ring. It is drawn
        // first only when there is a length to divide by — a provider that reports
        // a reset and no length gets no arc, rather than one nobody reported.
        if (!double.IsNaN(ElapsedFraction) && radius + RingThickness / 2 + ClockThickness <= outer + 1)
        {
            var clockPen = new Pen(new SolidColorBrush(Theme.RingClock), ClockThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            clockPen.Freeze();

            var clockRadius = radius + RingThickness / 2 + ClockThickness / 2;
            var sweep = Math.Clamp(ElapsedFraction, 0, 1) * 360;
            if (sweep > 0.5)
                dc.DrawGeometry(null, clockPen, Arc(centre, clockRadius, 0, sweep));
        }

        // The reading. Clamped for geometry only; the number shown elsewhere keeps
        // whatever the provider said, so a spend limit at 130% still displays 130%.
        var used = Math.Clamp(UsedFraction, 0, 1);

        // A spent window is a full ring whichever way the figure runs. A window that
        // was never reported gets no arc at all — see HasFraction.
        var arc = !HasFraction
            ? 0
            : IsExhausted || used >= 1
                ? 1
                : ShowsRemaining ? 1 - used : used;

        if (arc > 0)
        {
            var colour = IsExhausted ? ExhaustedColour : Ramp(UsedFraction);
            var pen = new Pen(new SolidColorBrush(HasReading ? colour : Dim(colour)), RingThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            pen.Freeze();

            // A full ring drawn as a 360-degree arc degenerates into a point and
            // vanishes. At (or past) the limit, a plain circle is what is wanted.
            if (arc >= 0.9999)
                dc.DrawEllipse(null, pen, centre, radius, radius);
            else
                dc.DrawGeometry(null, pen, Arc(centre, radius, 0, arc * 360));
        }

        DrawMark(dc, centre, size);
    }

    /// <summary>
    /// The provider's mark, centred.
    /// </summary>
    /// <remarks>
    /// Drawn after the arc, and in the primary foreground rather than the ring's
    /// value colour: the arc is the reading and should be the thing that changes,
    /// while the mark is the identity and should not. The two are deliberately
    /// different so a mostly-empty ring still says which account it is.
    /// <para>
    /// Scaled from the mark's own 24×24 grid rather than fitted to its ink bounds.
    /// The marks have different bounds — 16×20 for OpenCode, 24×23.8 for OpenAI —
    /// because they are drawn that way on purpose, each sized to its own optical
    /// weight. Fitting every one to the same box would flatten exactly the
    /// difference the designers put there.
    /// </para>
    /// </remarks>
    private void DrawMark(DrawingContext dc, Point centre, double size)
    {
        var box = size * 0.52;
        var brush = new SolidColorBrush(Theme.RingMark)
        {
            // Dimmed when there is no reading, the same signal the arc and the
            // figure use for "this one has nothing to say".
            Opacity = HasReading ? 1.0 : 0.38,
        };
        brush.Freeze();

        if (Icon is { } icon)
        {
            var scale = box / ProviderIcons.ViewBox;

            // Pushed translate-then-scale, which composes as Translate(Scale(shape)):
            // the mark is scaled inside its own grid first, then placed.
            dc.PushTransform(new TranslateTransform(centre.X - box / 2, centre.Y - box / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(brush, null, icon);
            dc.Pop();
            dc.Pop();
            return;
        }

        // No mark: the ring still carries the figure, which is the part that
        // matters, so this is a fallback rather than a failure.
        if (string.IsNullOrEmpty(Glyph)) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(
            new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        var text = new FormattedText(
            Glyph, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, size * 0.40, brush, dpi)
        {
            TextAlignment = TextAlignment.Center,
        };

        dc.DrawText(text, new Point(centre.X, centre.Y - text.Height / 2));
    }

    /// <summary>
    /// An arc of <paramref name="sweepDegrees"/>, clockwise from twelve o'clock.
    ///
    /// Screen coordinates have y increasing downward, so a positive angle in
    /// <see cref="Math.Sin"/> terms runs clockwise on screen — which is the
    /// direction a progress ring is read in, and the reason there is no sign flip
    /// here.
    /// </summary>
    private static Geometry Arc(Point centre, double radius, double startDegrees, double sweepDegrees)
    {
        static Point At(Point c, double r, double degrees)
        {
            var radians = (degrees - 90) * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(radians), c.Y + r * Math.Sin(radians));
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var start = At(centre, radius, startDegrees);
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(
                At(centre, radius, startDegrees + sweepDegrees),
                new Size(radius, radius),
                0,
                sweepDegrees > 180,
                SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Green through amber to red, off <b>what is gone</b>.
    ///
    /// The thresholds are the ones the notification rules use, so the colour on
    /// screen and the alert that fires agree about when something became a
    /// problem.
    /// </summary>
    private static Color Ramp(double fraction)
    {
        var stops = new (double At, Color Colour)[]
        {
            (0.00, Color.FromRgb(0x34, 0xC7, 0x59)), // green
            (0.50, Color.FromRgb(0xFF, 0x9F, 0x0A)), // amber
            (0.75, Color.FromRgb(0xFF, 0x6B, 0x00)), // orange
            (0.90, Color.FromRgb(0xFF, 0x3B, 0x30)), // red
            (1.00, Color.FromRgb(0xD7, 0x00, 0x15)), // deep red
        };

        var value = Math.Clamp(fraction, 0, 1);
        for (var i = 0; i < stops.Length - 1; i++)
        {
            var (from, low) = stops[i];
            var (to, high) = stops[i + 1];
            if (value > to) continue;

            var t = to - from < 1e-9 ? 0 : (value - from) / (to - from);
            return Lerp(low, high, t);
        }

        return stops[^1].Colour;
    }

    private static readonly Color ExhaustedColour = Color.FromRgb(0xB3, 0x26, 0x1E);

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static Color Dim(Color colour) => Color.FromRgb(
        (byte)(colour.R * 0.45), (byte)(colour.G * 0.45), (byte)(colour.B * 0.45));

    /// <summary>How long until the window turns over, in the compact form the rail uses.</summary>
    public static string CountdownText(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is not { } reset) return "";
        var left = reset - now;
        if (left <= TimeSpan.Zero) return Localization.Loc.Current.CountdownNow;

        if (left.TotalDays >= 1) return $"{(int)left.TotalDays}d {left.Hours}h";
        if (left.TotalHours >= 1) return $"{(int)left.TotalHours}h {left.Minutes:00}m";
        return $"{left.Minutes}m";
    }
}
