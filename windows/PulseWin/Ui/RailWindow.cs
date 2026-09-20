using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PulseWin.Core;
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
    private readonly Border _surface;
    private readonly DetailCard _card = new();
    private readonly Dictionary<AccountKey, RailRow> _byKey = new();
    private RailRow? _hovered;

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

        _surface = Theme.Surface2(13, 0);
        _surface.Child = _rows;
        _surface.ContextMenu = BuildMenu();
        Content = _surface;

        MouseRightButtonUp += (_, _) => _surface.ContextMenu.IsOpen = true;
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
    }

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);

    private ContextMenu BuildMenu()    {
        var menu = new ContextMenu
        {
            FontFamily = Theme.Font,
            FontSize = 12,
        };

        menu.Items.Add(Item("Refresh now", () => RefreshRequested?.Invoke()));
        menu.Items.Add(Item("Settings…", () => SettingsRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Hide the rail", () => HideRequested?.Invoke()));
        menu.Items.Add(Item("Exit PulseWin", () => ExitRequested?.Invoke()));
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
                row = new RailRow(account, state);
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

        Resize();
    }

    private void OnRowEntered(RailRow row)
    {
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
    public void Dock(RailEdge edge, double offset)
    {
        var work = SystemParameters.WorkArea;
        var horizontal = edge == RailEdge.Top;

        SetOrientation(horizontal);
        Resize();

        // Let the panel measure before it is positioned, or the first dock uses the
        // height of an empty rail and lands in the wrong place.
        UpdateLayout();

        var width = ActualWidth > 1 ? ActualWidth : Width;
        var height = ActualHeight > 1 ? ActualHeight : Height;

        switch (edge)
        {
            case RailEdge.Right:
                Left = work.Right - width - 6;
                Top = Clamp(work.Top + (work.Height - height) / 2 + offset, work.Top, work.Bottom - height);
                break;

            case RailEdge.Left:
                Left = work.Left + 6;
                Top = Clamp(work.Top + (work.Height - height) / 2 + offset, work.Top, work.Bottom - height);
                break;

            case RailEdge.Top:
                Left = Clamp(work.Left + (work.Width - width) / 2 + offset, work.Left, work.Right - width);
                Top = work.Top + 6;
                break;
        }
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
        _rows.Orientation = horizontal
            ? System.Windows.Controls.Orientation.Horizontal
            : System.Windows.Controls.Orientation.Vertical;

        if (horizontal)
        {
            // A top rail is a strip, so its thickness becomes the ring size plus
            // padding and its length is whatever the rows need.
            Width = double.NaN;
            Height = Theme.RingSize + Theme.RingSpacing + 11 + 16 + 2;
            SizeToContent = SizeToContent.Width;
        }
        else
        {
            Width = Theme.RailWidth;
            Height = double.NaN;
            SizeToContent = SizeToContent.Height;
        }
    }

    private void Resize()
    {
        // Keep the rail hugging its rows: an empty rail should be a sliver, not a
        // screen-height bar with nothing in it.
        if (_rows.Orientation == System.Windows.Controls.Orientation.Horizontal)
        {
            Height = Theme.RingSize + Theme.RingSpacing + 11 + 16 + 2;
            SizeToContent = SizeToContent.Width;
        }
        else
        {
            Width = Theme.RailWidth;
            SizeToContent = SizeToContent.Height;
        }
    }

    private static double Clamp(double value, double low, double high) =>
        high < low ? low : Math.Clamp(value, low, high);

    /// <summary>
    /// Keeps the rail out of the taskbar's way when the work area changes — a
    /// resolution change, a monitor unplugged, the taskbar moved.
    /// </summary>
    public void Redock() => Dock(AppSettings.Current.Edge, AppSettings.Current.RailOffset);
}
