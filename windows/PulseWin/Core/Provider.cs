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

    /// <summary>The ring's accent, used when the service has not said it is spent.</summary>
    public static string AccentHex(this Provider provider) => provider switch
    {
        Provider.Codex => "#10A37F",
        Provider.OpenCodeGo => "#6E56CF",
        Provider.Zhipu => "#3B6EF6",
        Provider.Zai => "#3B6EF6",
        Provider.DeepSeek => "#4D6BFE",
        _ => "#8A8A8E",
    };

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
    public static string CredentialNote(this Provider provider) => provider switch
    {
        Provider.Codex => @"Borrows the login Codex saved at %USERPROFILE%\.codex\auth.json.",
        Provider.OpenCodeGo => @"Reads %USERPROFILE%\.local\share\opencode\auth.json, or a key you paste here.",
        Provider.Zhipu => @"Mainland storefront (open.bigmodel.cn). Paste a key, or it reads a saved GLM key file.",
        Provider.Zai => @"International storefront (api.z.ai). A key from the mainland service is refused here.",
        Provider.DeepSeek => @"Paste a key from platform.deepseek.com. Reports a prepaid balance — there is no allowance.",
        _ => "",
    };

    public static IReadOnlyList<Provider> All { get; } = new[]
    {
        Provider.Codex,
        Provider.OpenCodeGo,
        Provider.Zhipu,
        Provider.Zai,
        Provider.DeepSeek,
    };
}
