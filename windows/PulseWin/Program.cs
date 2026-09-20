using System.Windows;

namespace PulseWin;

public static class Program
{
    /// <summary>
    /// The entry point.
    ///
    /// <para>
    /// <c>--selftest</c> runs the provider pipeline headlessly and exits, which is
    /// the mode to reach for when a ring is wrong and the question is whether the
    /// service or the drawing is at fault.
    /// </para>
    /// <para>
    /// The app itself is built in code rather than from XAML. A tray-resident
    /// borderless overlay has no document to describe — every window here is
    /// positioned by arithmetic against a monitor's work area, not by a layout
    /// system — so XAML would add a compile step and a file to keep in sync
    /// without carrying any of the decisions.
    /// </para>
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
            return SelfTest.RunAsync().GetAwaiter().GetResult();

        if (args.Contains("--fixtures", StringComparer.OrdinalIgnoreCase))
            return SelfTest.RunFixtures();

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        var controller = new AppController();
        controller.Start();

        return app.Run();
    }
}
