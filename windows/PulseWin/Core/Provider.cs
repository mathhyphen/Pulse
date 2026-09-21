namespace PulseWin.Core;

/// <summary>
/// The services PulseWin can draw a ring for.
///
/// Ported from Pulse's <c>Provider</c> enum, narrowed to the four the first
/// release covers. The enum member names are the stable account-id prefix, so
/// they match the Swift raw values (`codex`, `openCodeGo`, `glmCoding`,
/// `deepSeek`) rather than any display name.
/// </summary>
public enum Provider
{
    /// <summary>ChatGPT / Codex. The ring reads the Codex allowance that a Plus or Pro plan includes.</summary>
    Codex,

    /// <summary>OpenCode Go's plan limits.</summary>
    OpenCodeGo,

    /// <summary>
    /// Zhipu's mainland storefront (`open.bigmodel.cn`) — the "bigmodel" subscription.
    ///
    /// Named for the shop, not the product, which is Pulse's own decision and a
    /// deliberate one: z.ai sells its plan under the name "GLM Coding Plan" too,
    /// so a row called that is a row half the buyers pick wrongly. The company is
    /// the one thing that differs and the one thing a buyer knows.
    /// </summary>
    Zhipu,

    /// <summary>z.ai — the international storefront. Separate account, separate key.</summary>
    Zai,

    /// <summary>DeepSeek prepaid credit. No allowance, no window — money only.</summary>
    DeepSeek,
}

/// <summary>
/// Metadata about the providers, as extension methods and the ordered roster.
/// </summary>
/// <remarks>
/// Named <c>ProviderCatalog</c> rather than <c>Providers</c> on purpose: there is
/// also a <c>PulseWin.Providers</c> namespace holding the services, and a class
/// called <c>Providers</c> in scope makes <c>Providers.All</c> resolve to the
/// namespace instead — an error whose message ("no type named All in namespace
/// PulseWin.Providers") points nowhere near the cause.
/// </remarks>
public static class ProviderCatalog
{
    /// <summary>Product names, left untranslated — the same rule Pulse keeps.</summary>
    public static string DisplayName(this Provider provider) => provider switch
    {
        Provider.Codex => "Codex",
        Provider.OpenCodeGo => "OpenCode Go",
        Provider.Zhipu => "Zhipu",
        Provider.Zai => "z.ai",
        Provider.DeepSeek => "DeepSeek",
        _ => provider.ToString(),
    };

    // A per-provider accent colour used to live here, tinting the letter drawn in
    // the ring. It is gone with the letters: the rings now carry each product's own
    // mark, which is a monochrome template, and upstream's rule is that colour on
    // this surface means usage. A brand tint beside a brand-tinted arc would mean
    // nothing.

    /// <summary>
    /// The identifier Pulse uses for this provider, kept identical so that window
    /// ids and account ids line up with the original.
    ///
    /// Not the C# enum name: Pulse's raw values are camel-cased (<c>glmCoding</c>,
    /// <c>openCodeGo</c>), and a window id is what a pinned window is matched on.
    /// </summary>
    public static string StableId(this Provider provider) => provider switch
    {
        Provider.Codex => "codex",
        Provider.OpenCodeGo => "openCodeGo",
        Provider.Zhipu => "glmCoding",
        Provider.Zai => "zai",
        Provider.DeepSeek => "deepSeek",
        _ => provider.ToString(),
    };

    /// <summary>
    /// The mark drawn inside the ring.
    /// </summary>
    /// <remarks>
    /// The real brand marks are bundled as SVG — see <c>Ui/ProviderIcons</c> — and
    /// these letters are only the fallback for a mark that will not parse. They are
    /// doing real work otherwise: a ring at 3% is a hairline arc, and without
    /// something in the middle it reads as an empty circle that failed to draw.
    ///
    /// Case distinguishes the two storefronts — <c>Z</c> is the mainland plan and
    /// <c>z</c> the international one — because they are separate accounts whose
    /// keys are refused by each other, and telling them apart is the whole reason
    /// there are two rows.
    /// </remarks>
    public static string Glyph(this Provider provider) => provider switch
    {
        Provider.Codex => "C",
        Provider.OpenCodeGo => "O",
        Provider.Zhipu => "Z",
        Provider.Zai => "z",
        Provider.DeepSeek => "D",
        _ => "?",
    };

    /// <summary>Whether the user pastes a key, rather than a login another tool already stored.</summary>
    public static bool UsesApiKey(this Provider provider) => provider switch
    {
        Provider.Codex => false,
        _ => true,
    };

    /// <summary>
    /// Whether more than one account of this provider can sit on the rail.
    ///
    /// Codex only, among the four — which is the one the user actually needs it
    /// for. Pulse's rule is the same: added accounts are endpoint-backed and
    /// carry their own credential, because the CLI login belongs to whichever
    /// account the CLI is signed in to, not to this one.
    /// </summary>
    public static bool SupportsMultipleAccounts(this Provider provider) =>
        provider == Provider.Codex;

    /// <summary>Where the key comes from, stated in Settings above the field.</summary>
    public static string CredentialNote(this Provider provider)
    {
        var strings = Localization.Loc.Current;

        return provider switch
        {
            Provider.Codex => strings.CredentialCodex,
            Provider.OpenCodeGo => strings.CredentialOpenCodeGo,
            Provider.Zhipu => strings.CredentialZhipu,
            Provider.Zai => strings.CredentialZai,
            Provider.DeepSeek => strings.CredentialDeepSeek,
            _ => "",
        };
    }

    public static IReadOnlyList<Provider> All { get; } = new[]
    {
        Provider.Codex,
        Provider.OpenCodeGo,
        Provider.Zhipu,
        Provider.Zai,
        Provider.DeepSeek,
    };
}
