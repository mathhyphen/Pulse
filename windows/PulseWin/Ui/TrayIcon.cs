using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using PulseWin.Storage;

namespace PulseWin.Ui;

/// <summary>
/// The notification-area icon and its menu.
///
/// <para>
/// WinForms' <see cref="System.Windows.Forms.NotifyIcon"/> is used rather than a
/// WPF equivalent because WPF has none — a tray icon is a shell feature, not a
/// framework one. The icon is drawn at runtime instead of shipped as a resource,
/// which keeps the build to source files and lets the ring in the icon be the same
/// ring the rail draws.
/// </para>
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _generated;

    public event Action? SettingsRequested;

    public event Action? ToggleRailRequested;

    public event Action? RefreshRequested;

    public event Action? ExitRequested;

    public TrayIcon()
    {
        _generated = BuildIcon();

        var strings = Localization.Loc.Current;

        var menu = new System.Windows.Forms.ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(MenuItem(strings.MenuToggleRail, () => ToggleRailRequested?.Invoke()));
        menu.Items.Add(MenuItem(strings.MenuRefreshNow, () => RefreshRequested?.Invoke()));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(MenuItem(strings.MenuSettings, () => SettingsRequested?.Invoke()));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(MenuItem(strings.MenuExit, () => ExitRequested?.Invoke()));

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _generated,
            Text = strings.TrayTooltipName,
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Double-clicking the tray icon is the reflex for "show me the thing", and
        // it should not open a settings dialog to do it.
        _icon.DoubleClick += (_, _) => ToggleRailRequested?.Invoke();
    }

    /// <summary>Rebuilds the tray menu in the current language. See <c>RailWindow.RefreshLanguage</c>.</summary>
    public void RefreshLanguage()
    {
        var strings = Localization.Loc.Current;

        var menu = new System.Windows.Forms.ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(MenuItem(strings.MenuToggleRail, () => ToggleRailRequested?.Invoke()));
        menu.Items.Add(MenuItem(strings.MenuRefreshNow, () => RefreshRequested?.Invoke()));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(MenuItem(strings.MenuSettings, () => SettingsRequested?.Invoke()));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(MenuItem(strings.MenuExit, () => ExitRequested?.Invoke()));

        _icon.ContextMenuStrip = menu;
        _icon.Text = strings.TrayTooltipName;
    }

    private static System.Windows.Forms.ToolStripMenuItem MenuItem(string text, Action action)
    {
        var item = new System.Windows.Forms.ToolStripMenuItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// A ring on a transparent field, drawn at the size the notification area asks
    /// for.
    /// </summary>
    /// <remarks>
    /// <c>GetHicon</c> hands back an HICON that the caller owns, so it is destroyed
    /// once <see cref="System.Drawing.Icon"/> has copied it. Skipping that leaks a
    /// GDI handle per launch, which a tray-resident app that gets restarted often
    /// would eventually notice.
    /// </remarks>
    private static System.Drawing.Icon BuildIcon()
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var inset = 3f;
            var thickness = 4f;
            var box = new RectangleF(inset, inset, size - inset * 2, size - inset * 2);

            using var track = new Pen(Color.FromArgb(90, 255, 255, 255), thickness);
            graphics.DrawEllipse(track, box);

            // Three quarters gone, so the icon reads as a gauge rather than as a
            // logo even at 16 pixels.
            using var arc = new Pen(Color.FromArgb(0x34, 0xC7, 0x59), thickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            graphics.DrawArc(arc, box, -90, 270);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void SetTooltip(string text)
    {
        // The shell truncates anything past 63 characters, and a NotifyIcon whose
        // Text is too long throws rather than truncating.
        _icon.Text = text.Length <= 62 ? text : text[..59] + "...";
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _generated.Dispose();
    }
}
