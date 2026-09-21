namespace PulseWin.Localization;

/// <summary>
/// Which language the interface is drawn in.
/// </summary>
/// <remarks>
/// Named <c>UiLanguage</c> rather than <c>Language</c> because WPF already has a
/// type by that name in scope inside any window, and the compiler resolves to it
/// silently — the error it produces names <c>XmlLanguage</c>, which points nowhere
/// near the cause.
/// </remarks>
public enum UiLanguage
{
    /// <summary>Follow Windows. Falls back to English for a culture with no translation.</summary>
    Auto,
    English,
    Chinese,
}

/// <summary>
/// Every string the interface shows.
///
/// <para>
/// This is an <b>abstract class rather than a keyed lookup</b>, and that is the
/// whole point: a dictionary of keys falls back silently when a key is missing or
/// misspelled, so the symptom of an incomplete translation is one stray English
/// word somewhere nobody looks. With abstract members the compiler refuses to build
/// a language that has not answered every question.
/// </para>
/// <para>
/// <b>Product names are not in here.</b> Claude Code, Codex, OpenCode Go, Zhipu,
/// z.ai and DeepSeek are what those products are called, in every language, and
/// upstream keeps them untranslated for the same reason. The same goes for a
/// window's <c>Scope</c>, which is a model name.
/// </para>
/// </summary>
public abstract class Strings
{
    // ------------------------------------------------------------ window names

    public abstract string WindowFiveHour { get; }

    public abstract string WindowWeekly { get; }

    public abstract string WindowSpend { get; }

    public abstract string WindowBalance { get; }

    public abstract string WindowDaily { get; }

    public abstract string WindowMessages { get; }

    public abstract string WindowMonthly { get; }

    public abstract string WindowLimit { get; }

    public abstract string WindowHours(int hours);

    public abstract string WindowDays(int days);

    public abstract string EstimatePlanPrice { get; }

    public abstract string EstimateSinceTopUp { get; }

    public abstract string EstimateYourBudget { get; }

    // ---------------------------------------------------------- unavailability

    public abstract string UnavailableApiKeyMissing { get; }

    public abstract string UnavailableApiKeyRefused { get; }

    public abstract string UnavailableRateLimited { get; }

    public abstract string UnavailableServerError { get; }

    public abstract string UnavailableUnreachable { get; }

    public abstract string UnavailableUnreadableReply { get; }

    public abstract string UnavailableNoLimitsReported { get; }

    public abstract string UnavailableSignInRequired { get; }

    public abstract string UnavailableZaiNoCodingPlan { get; }

    public abstract string UnavailableCodexServerFailed { get; }

    public abstract string UnavailableGeneric { get; }

    // ------------------------------------------------------------- credentials

    public abstract string CredentialCodex { get; }

    public abstract string CredentialOpenCodeGo { get; }

    public abstract string CredentialZhipu { get; }

    public abstract string CredentialZai { get; }

    public abstract string CredentialDeepSeek { get; }

    // -------------------------------------------------------------- rail menu

    public abstract string MenuRefreshNow { get; }

    public abstract string MenuSettings { get; }

    public abstract string MenuHideRail { get; }

    public abstract string MenuToggleRail { get; }

    public abstract string MenuExit { get; }

    public abstract string TrayTooltipName { get; }

    public abstract string DirectionLeft { get; }

    public abstract string DirectionUsed { get; }

    // ------------------------------------------------------------- detail card

    public abstract string CardCountingDown { get; }

    public abstract string CardCountingUp { get; }

    public abstract string CardResetsIn(string duration);

    public abstract string CardWindowElapsed(int percent);

    public abstract string CardSuffixLeft(string figure);

    public abstract string CardSuffixUsed(string figure);

    public abstract string CardSpent { get; }

    public abstract string CardNoLimitOnlyBalance { get; }

    public abstract string CardBalance { get; }

    public abstract string CardFiguresAreFrom(string ago);

    public abstract string CardNeverRead { get; }

    public abstract string CardChecked(string ago);

    public abstract string AgoNow { get; }

    public abstract string AgoMinutes(int minutes);

    public abstract string AgoHours(int hours);

    public abstract string AgoDays(int days);

    public abstract string CountdownNow { get; }

    // ---------------------------------------------------------------- settings

    public abstract string SettingsTitle { get; }

    public abstract string SettingsServices { get; }

    public abstract string SettingsServicesCaption { get; }

    public abstract string SettingsDeepSeek { get; }

    public abstract string SettingsDeepSeekCaption { get; }

    public abstract string SettingsRail { get; }

    public abstract string SettingsCodexAccounts { get; }

    public abstract string SettingsCodexAccountsCaption { get; }

    public abstract string SettingsBasisSinceTopUp { get; }

    public abstract string SettingsBasisBalanceOnly { get; }

    public abstract string SettingsBasisBudget { get; }

    public abstract string SettingsBudget { get; }

    public abstract string SettingsCurrency { get; }

    public abstract string SettingsCurrencyTooltip { get; }

    public abstract string SettingsEdge { get; }

    public abstract string SettingsEdgeRight { get; }

    public abstract string SettingsEdgeLeft { get; }

    public abstract string SettingsEdgeTop { get; }

    public abstract string SettingsEvery { get; }

    public abstract string SettingsMinutesTooltip { get; }

    public abstract string SettingsOffset { get; }

    public abstract string SettingsOffsetTooltip { get; }

    public abstract string SettingsCountdown { get; }

    public abstract string SettingsCountdownNote { get; }

    public abstract string SettingsClear { get; }

    public abstract string SettingsRemove { get; }

    public abstract string SettingsAddAccount { get; }

    public abstract string SettingsSwitchCodexOn { get; }

    public abstract string SettingsLanguage { get; }

    public abstract string SettingsLanguageAuto { get; }

    public abstract string SettingsAppearance { get; }

    public abstract string SettingsTheme { get; }

    public abstract string SettingsThemeFollowWindows { get; }

    public abstract string SettingsThemeDark { get; }

    public abstract string SettingsThemeLight { get; }

    public abstract string SettingsBackdrop { get; }

    public abstract string SettingsBackdropSolid { get; }

    public abstract string SettingsBackdropAcrylic { get; }

    public abstract string SettingsBackdropNote { get; }

    public abstract string SettingsOpacity { get; }

    public abstract string SettingsOpacityNote { get; }

    public abstract string SettingsStatusOff { get; }

    public abstract string SettingsStatusNotChecked { get; }

    public abstract string SettingsStatusNeedsKey { get; }

    public abstract string SettingsStatusRead { get; }

    public abstract string SettingsStatusStale(string figure);

    public abstract string SettingsKeyStored { get; }

    public abstract string SettingsKeyPaste { get; }

    public abstract string SettingsAccountLabelTooltip { get; }

    public abstract string SettingsAccountTokenTooltip { get; }

    public abstract string SettingsAccountIdTooltip { get; }

    public abstract string SettingsDefaultAccountName { get; }

    // ------------------------------------------------------------- codex sign-in

    public abstract string SignInTitle { get; }

    public abstract string SignInIntro { get; }

    public abstract string SignInCopyCode { get; }

    public abstract string SignInOpenPage { get; }

    public abstract string SignInWaiting { get; }

    public abstract string SignInSucceeded(string who);

    public abstract string SignInFailed(string why);

    public abstract string SignInCancel { get; }

    public abstract string SignInAdd { get; }

    public abstract string SignInNone { get; }

    public abstract string SignInClose { get; }

    public abstract string SignInPreparing { get; }

    public abstract string SignInWhySignIn { get; }

    public abstract string SignInAccountFallback { get; }

    // ------------------------------------------------------------------ the rail

    public abstract string RailResetPosition { get; }

    // ------------------------------------------------------------------- money

    /// <summary>
    /// How large numbers are shortened on the rail, largest tier first.
    /// </summary>
    /// <remarks>
    /// This differs by language rather than by taste: English groups by thousands,
    /// so a hundred thousand is "100k"; Chinese groups by ten thousands, so the
    /// same figure is "10万". Upstream carries the same distinction.
    /// </remarks>
    public abstract (double Threshold, double Divisor, string Suffix)[] MoneyTiers { get; }
}

/// <summary>The language the interface is currently drawn in.</summary>
public static class Loc
{
    public static readonly Strings En = new EnglishStrings();

    public static readonly Strings Zh = new ChineseStrings();

    private static UiLanguage _setting = UiLanguage.Auto;

    public static Strings Current { get; private set; } = En;

    /// <summary>What the user chose. Setting it re-resolves <see cref="Current"/>.</summary>
    public static UiLanguage Setting
    {
        get => _setting;
        set
        {
            _setting = value;
            Current = Resolve(value);
        }
    }

    /// <summary>
    /// Picks the language, following Windows when asked to.
    /// </summary>
    /// <remarks>
    /// Only Simplified Chinese is translated, so every other culture gets English
    /// rather than an empty interface. Reading <c>CurrentUICulture</c> rather than
    /// <c>CurrentCulture</c> is deliberate: a reader who has set their number and
    /// date formats to English but runs a Chinese Windows still wants Chinese.
    /// </remarks>
    private static Strings Resolve(UiLanguage language) => language switch
    {
        UiLanguage.English => En,
        UiLanguage.Chinese => Zh,
        _ => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            == "zh"
            ? Zh
            : En,
    };

    /// <summary>Applies the language read from settings, once, at startup.</summary>
    public static void Initialise(UiLanguage language) => Setting = language;
}
