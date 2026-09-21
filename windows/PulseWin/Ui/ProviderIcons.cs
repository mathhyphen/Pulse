using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using PulseWin.Core;

namespace PulseWin.Ui;

/// <summary>
/// The provider marks drawn inside the rings.
///
/// <para>
/// These are the SVG files upstream bundles, taken from the fork's own
/// <c>Sources/Pulse/Resources</c> rather than redrawn. They come from
/// <a href="https://github.com/lobehub/lobe-icons">Lobe Icons</a> under the MIT
/// licence, and upstream's <c>THIRD_PARTY_NOTICES.md</c> already carries the notice;
/// see <c>Icons/README.md</c> here for the provenance of these five copies.
/// </para>
/// <para>
/// <b>They are monochrome templates and are drawn as such.</b> Every file is
/// <c>fill="currentColor"</c> on a 24×24 grid, which is what that attribute means:
/// the mark has no colour of its own and takes the foreground it is given. That is
/// the point rather than a limitation — upstream's rule is that <i>colour means
/// usage, not brand</i>, so the only coloured thing on a ring is the arc, and a
/// per-brand tint would put two meanings on one surface.
/// </para>
/// <para>
/// Rendered by parsing the SVG path data straight into a WPF geometry. The two
/// path mini-languages are close enough that this is a parse rather than a
/// conversion, which is why there is no SVG library here.
/// </para>
/// </summary>
public static class ProviderIcons
{
    /// <summary>The grid every Lobe mark is drawn on.</summary>
    public const double ViewBox = 24;

    private static readonly Dictionary<Provider, Geometry?> Cache = new();
    private static readonly object Gate = new();

    /// <summary>The mark for a provider, or null when it could not be read.</summary>
    public static Geometry? For(Provider provider)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(provider, out var cached)) return cached;
            var geometry = Load(provider);
            Cache[provider] = geometry;
            return geometry;
        }
    }

    /// <summary>What went wrong, for the self-test. Never throws.</summary>
    public static (bool Ok, string Detail) Diagnose(Provider provider)
    {
        var name = FileName(provider);

        try
        {
            var svg = ReadSvg(name);
            if (svg is null) return (false, $"no embedded resource ending in '{name}.svg'");

            var data = PathData(svg);
            if (data.Count == 0) return (false, "no <path d=\"…\"> found");

            var geometry = Geometry.Parse(string.Join(" ", data));
            return (true, $"{data.Count} path(s), bounds {Round(geometry.Bounds)}");
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException)
        {
            return (false, e.Message);
        }
    }

    private static Geometry? Load(Provider provider)
    {
        try
        {
            var svg = ReadSvg(FileName(provider));
            if (svg is null) return null;

            var paths = PathData(svg);
            if (paths.Count == 0) return null;

            // **One geometry, not one per path.** Several marks are drawn as two
            // overlapping paths and rely on `fill-rule="evenodd"` to cut a hole
            // where they cross — 清言's ring is exactly that. Drawing them as two
            // separate geometries would paint both fills and lose the hole, since
            // separate draws composite rather than cancel. Concatenating the data
            // keeps them in one fill, where the rule applies.
            var geometry = Geometry.Parse(string.Join(" ", paths));
            geometry.Freeze();
            return geometry;
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException)
        {
            // A mark that will not parse is a missing mark, not a crash: the ring
            // still carries the figure, which is the part that matters.
            return null;
        }
    }

    private static string FileName(Provider provider) => provider switch
    {
        Provider.Codex => "openai",
        Provider.OpenCodeGo => "opencode",
        // 清言's mark rather than the corporate Zhipu one, which is upstream's
        // choice and a deliberate one: z.ai and Zhipu are the same company's two
        // storefronts, so the mark is the only thing telling the two rows apart.
        Provider.Zhipu => "qingyan",
        Provider.Zai => "zai",
        Provider.DeepSeek => "deepseek",
        _ => provider.StableId(),
    };

    private static string? ReadSvg(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($".{name}.svg", StringComparison.OrdinalIgnoreCase));

        if (resource is null) return null;

        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream is null) return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Every <c>d</c> attribute, in document order.</summary>
    private static List<string> PathData(string svg) =>
        Regex.Matches(svg, @"d\s*=\s*""([^""]+)""")
            .Select(m => m.Groups[1].Value.Trim())
            .Where(d => d.Length > 0)
            .ToList();

    private static string Round(Rect bounds) =>
        $"{bounds.Width:0.#}×{bounds.Height:0.#} at {bounds.X:0.#},{bounds.Y:0.#}";
}
