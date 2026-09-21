namespace PulseWin.Core;

/// <summary>Which palette the interface is drawn in.</summary>
/// <remarks>
/// In <c>Core</c> rather than next to <c>Ui.Theme</c>, because it is a setting: the
/// storage layer has to name it, and storage reaching into the interface layer for
/// an enum would be the dependency pointing the wrong way.
/// </remarks>
public enum AppTheme
{
    /// <summary>Follow Windows. Reads the same setting Explorer's light/dark switch writes.</summary>
    FollowWindows,
    Dark,
    Light,
}

/// <summary>What the rail's surface is made of.</summary>
public enum Backdrop
{
    /// <summary>An opaque slab. Always works, and the only option on a system without blur.</summary>
    Solid,

    /// <summary>
    /// Acrylic: the surface goes translucent and what is behind the rail shows
    /// through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Translucent, not blurred, and that is a measured limitation rather than a
    /// setting that needs turning up.</b> Both Windows blur interfaces were tried
    /// against a controlled 16-pixel black-and-white stripe pattern behind the rail:
    /// <c>SetWindowCompositionAttribute</c> with <c>ACCENT_ENABLE_ACRYLICBLURBEHIND</c>
    /// (neutered for this since Windows 10 1803) and the documented
    /// <c>DWMWA_SYSTEMBACKDROP_TYPE</c>. Neither blurs — the stripes come through
    /// individually, with single-pixel jumps of 122 and 178 where a real 30-pixel
    /// blur would have smeared them into a flat field. The documented one requires a
    /// window that is <i>not</i> layered, and a layered window is what gives this
    /// rail its antialiased rounded corners and its drop shadow.
    /// </para>
    /// <para>
    /// So it is offered for what it does — a tinted translucent surface — rather than
    /// described as something it is not. Real blur is reachable, at the price of
    /// hard-edged corners and no shadow; that is a trade to be asked about, not one
    /// to make silently.
    /// </para>
    /// </remarks>
    Acrylic,
}
