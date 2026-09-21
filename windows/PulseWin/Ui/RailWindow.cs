using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PulseWin.Core;
using PulseWin.Localization;
using PulseWin.Services;
using PulseWin.Storage;

namespace PulseWin.Ui;

/// <summary>
/// The floating rail: a thin slab docked to a screen edge, one ring per account.
///
/// <para>
/// Borderless, transparent, topmost and never activated — it is a monitor, not a
/// window, and clicking it must not take focus away from whatever the reader is
/// working in. Everything it does is reachable by pointing at it, which is why the
/// only chrome is a right-click menu rather than a title bar.
/// </para>
/// </summary>
internal sealed class RailWindow : Window
{
    private readonly StackPanel _rows = new();
    private readonly DetailCard _card = new();
    private readonly Dictionary<AccountKey, RailRow> _byKey = new();
    private RailRow? _hovered;

    // Not readonly: a backdrop change alters how many layers the slab has, so these
    // are replaced rather than recoloured. See BuildSurface.
    private Grid _surface;
    private Border? _background;
    private Border _content;

    private bool _dragging;
    private Point _dragGrab;
    private bool _horizontal;
    private RailEdge? _edge = RailEdge.Right;

    /// <summary>
    /// Whether a drag is in progress.
    /// </summary>
    /// <remarks>
    /// Read by the controller, which re-docks the rail whenever its size changes.
    /// That is right in general and wrong during a drag: the rail is being positioned
    /// by hand, and having it yanked back to its docked coordinates every time a
    /// measurement lands is the third thing that looked like flickering.
    /// </remarks>
    public bool IsDragging => _dragging;

    public event Action? SettingsRequested;

    public event Action? RefreshRequested;

    public event Action? HideRequested;

    public event Action? ExitRequested;

    public RailWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        // A monitor that steals focus is worse than no monitor.
        ShowActivated = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Title = "PulseWin";

        _rows.Margin = new Thickness(5, 8, 5, 8);

        // Built by BuildSurface, and rebuilt whenever the surface's *shape* changes —
        // see the note on ApplyTheme.
        (_surface, _background, _content) = BuildSurface();
        Content = _surface;

        MouseRightButtonUp += (_, _) => _surface.ContextMenu.IsOpen = true;

        // Dragging. On the window rather than on a grip, because the whole rail is
        // the handle and a grip would be one more thing drawn on a surface that is
        // deliberately only a ring and a number.
        MouseLeftButtonDown += OnRailMouseDown;
        MouseMove += OnRailMouseMove;
        MouseLeftButtonUp += OnRailMouseUp;
        MouseDoubleClick += OnRailMouseDoubleClick;
    }

    /// <summary>
    /// Marks the window as a tool window once it has a handle.
    /// </summary>
    /// <remarks>
    /// <c>ShowInTaskbar = false</c> keeps the rail off the taskbar but not out of
    /// Alt-Tab, and an always-on-top strip that shows up in the window switcher is
    /// a window, not a monitor. <c>WS_EX_NOACTIVATE</c> is deliberately <i>not</i>
    /// set: it would stop the rail's right-click menu from taking focus, and every
    /// command on that menu is also on the tray icon, so the trade is not worth
    /// making blind.
    /// </remarks>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExToolWindow);

        // Here as well as in ApplyTheme, because this is the only point at startup
        // where the handle exists. Without it the backdrop is applied the first time
        // somebody touches a setting and not before — so the rail launched solid,
        // and the setting looked like it did nothing until you toggled it twice.
    }

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);

    /// <summary>
    /// Rebuilds the right-click menu in the current language.
    /// </summary>
    /// <remarks>
    /// A <see cref="ContextMenu"/>'s items are created once, so a language change
    /// leaves them in the old one until they are made again. Everything else on the
    /// rail is a number and needs no translation.
    /// </remarks>
    public void RefreshLanguage()
    {
        _surface.ContextMenu = BuildMenu();

        // The hover card is built fresh each time it opens, but one may be open now.
        _card.Hide();
    }

    private ContextMenu BuildMenu()    {
        var menu = new ContextMenu
        {
            FontFamily = Theme.Font,
            FontSize = 12,
        };

        menu.Items.Add(Item(Loc.Current.MenuRefreshNow, () => RefreshRequested?.Invoke()));
        menu.Items.Add(Item(Loc.Current.MenuSettings, () => SettingsRequested?.Invoke()));
        menu.Items.Add(new Separator());
        // There is otherwise no way back from a rail dragged half off the screen, or
        // parked somewhere the reader has since forgotten about.
        menu.Items.Add(Item(Loc.Current.RailResetPosition, ResetPosition));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.Current.MenuHideRail, () => HideRequested?.Invoke()));
        menu.Items.Add(Item(Loc.Current.MenuExit, () => ExitRequested?.Invoke()));
        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Rebuilds the rows from the store's snapshot, reusing rows that already exist.</summary>
    public void Render(IReadOnlyList<(MonitoredAccount Account, AccountState State)> snapshot)
    {
        var wanted = snapshot.Select(s => s.Account.Key).ToHashSet();

        foreach (var stale in _byKey.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _rows.Children.Remove(_byKey[stale]);
            _byKey.Remove(stale);
        }

        for (var index = 0; index < snapshot.Count; index++)
        {
            var (account, state) = snapshot[index];

            if (!_byKey.TryGetValue(account.Key, out var row))
            {
                row = new RailRow(account, state, _horizontal);
                row.PointerEntered += OnRowEntered;
                row.PointerLeft += OnRowLeft;
                _byKey[account.Key] = row;
            }
            else
            {
                row.Update(state);
            }

            // Keep the visual order equal to the store's order, because a rail
            // whose rows move between refreshes cannot be read at a glance.
            var current = _rows.Children.IndexOf(row);
            if (current != index)
            {
                if (current >= 0) _rows.Children.RemoveAt(current);
                _rows.Children.Insert(Math.Min(index, _rows.Children.Count), row);
            }
        }

        // Keep the rail hugging its items: an empty rail should be a sliver, not a
        // screen-height bar with nothing in it. SetOrientation owns the sizing — it
        // is the one place that knows which way the rail is running.
        SetOrientation(_horizontal);
    }

    private void OnRowEntered(RailRow row)
    {
        // No card while the rail is being moved: the pointer crosses rows on the way
        // and the card would open and close the whole way across the screen.
        if (_dragging) return;

        _hovered = row;
        _card.Show(row, row);
    }

    private void OnRowLeft(RailRow row)
    {
        if (ReferenceEquals(_hovered, row)) _hovered = null;
        _card.Hide();
    }

    /// <summary>
    /// Docks the rail to an edge of the primary monitor's work area.
    /// </summary>
    /// <remarks>
    /// <see cref="SystemParameters.WorkArea"/> is used rather than a raw monitor
    /// rectangle because it is already in device-independent units and already
    /// excludes the taskbar — the two things that otherwise have to be divided by a
    /// DPI scale and corrected for by hand, differently on every monitor.
    /// </remarks>
    public void Dock(RailEdge edge, double offset, Rect? bounds = null)
    {
        var work = bounds ?? SystemParameters.WorkArea;
        var horizontal = edge == RailEdge.Top;

        _edge = edge;
        SetOrientation(horizontal);
        ApplyCorners(edge);

        // Let the panel measure before it is positioned, or the first dock uses the
        // height of an empty rail and lands in the wrong place.
        UpdateLayout();

        var width = ActualWidth > 1 ? ActualWidth : Width;
        var height = ActualHeight > 1 ? ActualHeight : Height;

        switch (edge)
        {
            // Flush. A gap of a few pixels reads as a panel that happens to be near
            // the edge rather than one attached to it, and squaring the docked side
            // (see ApplyCorners) is what finishes the effect.
            case RailEdge.Right:
                Left = work.Right - width;
                Top = Clamp(work.Top + (work.Height - height) / 2 + offset, work.Top, work.Bottom - height);
                break;

            case RailEdge.Left:
                Left = work.Left;
                Top = Clamp(work.Top + (work.Height - height) / 2 + offset, work.Top, work.Bottom - height);
                break;

            case RailEdge.Top:
                Left = Clamp(work.Left + (work.Width - width) / 2 + offset, work.Left, work.Right - width);
                Top = work.Top;
                break;
        }
    }

    /// <summary>Puts the rail back where the settings say, docked or free.</summary>
    /// <remarks>
    /// Does nothing while a drag is in progress. The controller calls this whenever
    /// the rail's size changes and whenever the work area does, which is right in
    /// general and wrong here: the rail is being positioned by hand, and having it
    /// pulled back to its docked coordinates the moment a measurement lands is a
    /// visible fight. Guarded here rather than at each call site so the next caller
    /// cannot forget.
    /// </remarks>
    public void Redock()
    {
        if (_dragging) return;

        var settings = AppSettings.Current;

        if (settings.RailFree)
        {
            Free(settings.RailFreeLeft, settings.RailFreeTop);
            return;
        }

        Dock(settings.Edge, settings.RailOffset);
    }

    /// <summary>Leaves the rail where it was put, rounded on every side.</summary>
    private void Free(double left, double top)
    {
        // A rail that is not against an edge has no edge to orient itself by, so it
        // goes back to the vertical strip: it is the shape with room for the figure
        // under the ring, and a bar floating in the middle of the desktop reads as a
        // toolbar that lost its window.
        _edge = null;
        SetOrientation(false);
        ApplyCorners(null);

        // Let it measure before it is positioned, for the same reason Dock does.
        UpdateLayout();

        Left = left;
        Top = top;
    }

    /// <summary>
    /// Squares off whichever side is against the screen.
    /// </summary>
    /// <remarks>
    /// Rounding all four corners and sitting the slab flush leaves two small
    /// crescents of desktop showing through at the corners, which undoes the flush
    /// position — the slab still reads as floating. The docked side has to be
    /// square for it to read as attached, and that is the whole visual difference
    /// between docked and parked.
    /// </remarks>
    private void ApplyCorners(RailEdge? edge)
    {
        var corners = Theme.DockedCorners(edge, 13);

        // Absent in the translucent shape, which is a single layer — there is no
        // shadow there to round. See Theme.Card.
        if (_background is not null) _background.CornerRadius = corners;

        _content.CornerRadius = corners;
    }

    // ------------------------------------------------------------------ dragging

    /// <summary>
    /// Where the pointer is, in physical screen pixels.
    /// </summary>
    /// <remarks>
    /// <b>Not <c>e.GetPosition(this)</c>.</b> That is measured against the window,
    /// and the window is the thing being moved — so every step changes the frame of
    /// reference. Move the window right by one pixel and the pointer's window-relative
    /// x falls by one, which computes a position one pixel back: the two chase each
    /// other and the rail oscillates. On screen that reads as a flicker, and it is
    /// what the first version of this did.
    /// <para>
    /// <c>PointToScreen</c> adds the window's own offset back in, so it reports where
    /// the pointer actually is on the desk — which does not move when the window does.
    /// </para>
    /// </remarks>
    private Point PointerOnScreen(MouseEventArgs e) => PointToScreen(e.GetPosition(this));

    /// <summary>
    /// The window's DPI scale.
    /// </summary>
    /// <remarks>
    /// Needed because the two coordinate spaces differ: <see cref="Window.Left"/> is
    /// in device-independent units and <c>PointToScreen</c> answers in device pixels.
    /// Read per move rather than once, because dragging onto a monitor with a
    /// different scale changes it mid-drag.
    /// </remarks>
    private (double X, double Y) DpiScale()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return (dpi.DpiScaleX, dpi.DpiScaleY);
    }

    private void OnRailMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // A drag must not also open a hover card, or the card opens and closes under
        // the pointer for the whole length of the drag — the second thing that looked
        // like flickering.
        _card.Hide();

        var pointer = PointerOnScreen(e);
        var (scaleX, scaleY) = DpiScale();

        // How far into the slab the pointer took hold, in physical pixels, held for
        // the whole drag so the rail does not slide to centre itself under the cursor.
        _dragGrab = new Point(pointer.X - Left * scaleX, pointer.Y - Top * scaleY);

        _dragging = true;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnRailMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;

        var pointer = PointerOnScreen(e);
        var (scaleX, scaleY) = DpiScale();

        Left = (pointer.X - _dragGrab.X) / scaleX;
        Top = (pointer.Y - _dragGrab.Y) / scaleY;
    }

    private void OnRailMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        EndDrag();
        Settle();
    }

    /// <summary>
    /// Decides where a dropped rail belongs: against the nearest edge if it landed
    /// near one, otherwise exactly where it was let go.
    /// </summary>
    private void Settle()
    {
        var settings = AppSettings.Current;

        // The virtual screen rather than the work area, because a drag can end on
        // any monitor and both of its outer edges are real edges to snap to.
        var desktop = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        var width = ActualWidth > 1 ? ActualWidth : Width;
        var height = ActualHeight > 1 ? ActualHeight : Height;

        var toLeft = Left - desktop.Left;
        var toRight = desktop.Right - (Left + width);
        var toTop = Top - desktop.Top;

        var nearest = new[] { (Edge: RailEdge.Left, Distance: toLeft),
                              (Edge: RailEdge.Right, Distance: toRight),
                              (Edge: RailEdge.Top, Distance: toTop) }
            .OrderBy(candidate => candidate.Distance)
            .First();

        if (nearest.Distance > settings.SnapDistance)
        {
            // Left in open desktop. Remembered, so a restart puts it back rather
            // than hauling it to an edge the reader did not choose.
            settings.RailFree = true;
            settings.RailFreeLeft = Left;
            settings.RailFreeTop = Top;
            settings.Save();
            Free(Left, Top);
            return;
        }

        settings.RailFree = false;
        settings.Edge = nearest.Edge;

        // Where along the edge it landed, relative to the middle of the screen.
        settings.RailOffset = nearest.Edge == RailEdge.Top
            ? Left + width / 2 - (desktop.Left + desktop.Width / 2)
            : Top + height / 2 - (desktop.Top + desktop.Height / 2);

        settings.Save();
        Dock(nearest.Edge, settings.RailOffset, desktop);
    }

    private void OnRailMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // A double click is the reflex for "put it back".
        e.Handled = true;
        ResetPosition();
    }

    /// <summary>Puts the rail back on the edge the settings name, centred on it.</summary>
    public void ResetPosition()
    {
        // The one reposition a drag must not veto: this is the way home, and it is
        // reached from the menu rather than from a stray event.
        EndDrag();

        var settings = AppSettings.Current;
        settings.RailFree = false;
        settings.RailOffset = 0;
        settings.Save();
        Dock(settings.Edge, 0);
    }

    /// <summary>Ends a drag without settling, for the paths that reposition instead.</summary>
    private void EndDrag()
    {
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    /// <summary>
    /// A rail on a left or right edge is a vertical strip; on the top edge it is a
    /// horizontal one.
    /// </summary>
    /// <remarks>
    /// Named <c>SetOrientation</c> rather than <c>Orientation</c> because a method
    /// of that name shadows the <see cref="System.Windows.Controls.Orientation"/>
    /// enum inside the class, and the compiler then reads
    /// <c>Orientation.Vertical</c> as a call to the method.
    /// </remarks>
    private void SetOrientation(bool horizontal)
    {
        _horizontal = horizontal;

        _rows.Orientation = horizontal
            ? System.Windows.Controls.Orientation.Horizontal
            : System.Windows.Controls.Orientation.Vertical;

        if (horizontal)
        {
            // A top rail is a bar: its thickness is the ring plus breathing room, and
            // the figure sits beside the ring rather than below it (see RailRow). The
            // margins are asymmetric on purpose — each row already carries a trailing
            // gap, so the panel's left inset is the gap plus a little and its right
            // inset is only what makes the two ends match.
            _rows.Margin = new Thickness(Theme.RowGap + 4, 0, 4, 0);
            Width = double.NaN;
            Height = Theme.HorizontalThickness;
            SizeToContent = SizeToContent.Width;
        }
        else
        {
            _rows.Margin = new Thickness(5, 8, 5, 8);
            Width = Theme.RailWidth;
            Height = double.NaN;
            SizeToContent = SizeToContent.Height;
        }

        foreach (var row in _byKey.Values) row.SetOrientation(horizontal);
    }

    /// <summary>
    /// Builds the slab the rows sit on, and hands back its layers.
    /// </summary>
    /// <remarks>
    /// <b>A backdrop change alters the number of layers, so the surface has to be
    /// made again rather than recoloured.</b> <see cref="Theme.Card"/> returns two
    /// layers when solid — one to cast the shadow, one for the surface — and one when
    /// translucent, because two translucent layers stack and a surface meant to be 55%
    /// opaque comes out at 80%. Recolouring the existing pair therefore kept the pair:
    /// switching from Solid to Acrylic left two 55% layers on top of each other, which
    /// is darker and flatter than either setting promises. That is what "the rectangle
    /// underneath is still grey" was.
    /// </remarks>
    private (Grid Surface, Border? Background, Border Content) BuildSurface()
    {
        var (root, content) = Theme.Card(Theme.DockedCorners(_edge, 13), 0);

        // Re-parenting is the repaint: a element can only have one parent, so putting
        // the rows into the new content detaches them from the old one.
        content.Child = _rows;
        root.ContextMenu = BuildMenu();

        var background = root.Children.Count > 1 ? (Border)root.Children[0] : null;
        return (root, background, content);
    }

    /// <summary>
    /// Re-reads the palette for a theme or backdrop change.
    /// </summary>
    /// <remarks>
    /// The card and the menus are built fresh when they open, so they pick the new
    /// palette up on their own. The rail is not: its rows captured their brushes when
    /// they were made, and its surface may not even have the same number of layers any
    /// more, so both are remade here.
    /// </remarks>
    public void ApplyTheme()
    {
        var corners = Theme.DockedCorners(_edge, 13);

        // Remade, not recoloured — see BuildSurface.
        (_surface, _background, _content) = BuildSurface();
        Content = _surface;
        ApplyCorners(_edge);

        foreach (var row in _byKey.Values) row.ApplyTheme();

        // The window's own size can change with the backdrop, since the solid shape
        // carries a shadow and the translucent one does not.
        SetOrientation(_horizontal);
    }

    // There is deliberately no backdrop call here.
    //
    // The two Windows blur interfaces were both tried and both measured against a
    // controlled sixteen-pixel black-and-white stripe pattern behind the rail:
    // SetWindowCompositionAttribute with ACCENT_ENABLE_ACRYLICBLURBEHIND, and the
    // documented DWMWA_SYSTEMBACKDROP_TYPE. Neither blurs — the stripes come through
    // individually, with single-pixel jumps of 122 and 178 where a real thirty-pixel
    // blur would have smeared them flat. Both refuse a layered window, and a layered
    // window is what gives this rail its antialiased rounded corners.
    //
    // The accent call was not merely useless. It paints its tint over the whole
    // window *rectangle*, ignoring the shape WPF drew, so the rounded top-left corner
    // measured (64,63,63) against the desktop's (234,234,233) — square corners, for no
    // blur. Removing it is what gives the corners back. See windows/README.md.
private static double Clamp(double value, double low, double high) =>
        high < low ? low : Math.Clamp(value, low, high);
}


