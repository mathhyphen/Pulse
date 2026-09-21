namespace PulseWin.Localization;

/// <summary>The source language. Every other language is a translation of this one.</summary>
public sealed class EnglishStrings : Strings
{
    public override string WindowFiveHour => "5-hour limit";

    public override string WindowWeekly => "Weekly limit";

    public override string WindowSpend => "Spend limit";

    public override string WindowBalance => "Balance";

    public override string WindowDaily => "Daily limit";

    public override string WindowMessages => "Message allowance";

    public override string WindowMonthly => "Monthly limit";

    public override string WindowLimit => "Limit";

    public override string WindowHours(int hours) => $"{hours}-hour limit";

    public override string WindowDays(int days) => $"{days}-day limit";

    public override string EstimatePlanPrice => "estimated";

    public override string EstimateSinceTopUp => "since top-up";

    public override string EstimateYourBudget => "of your budget";

    public override string UnavailableApiKeyMissing => "No key has been entered for this service yet.";

    public override string UnavailableApiKeyRefused => "The service refused this key.";

    public override string UnavailableRateLimited =>
        "The service is rate limiting these checks. It will be tried again shortly.";

    public override string UnavailableServerError => "The service returned an error.";

    public override string UnavailableUnreachable => "Could not reach the service.";

    public override string UnavailableUnreadableReply =>
        "The service answered in a shape this version does not recognise.";

    public override string UnavailableNoLimitsReported => "The service answered, and reported no limit.";

    public override string UnavailableSignInRequired =>
        "The saved Codex login is missing or has expired. Run `codex` to sign in again.";

    public override string UnavailableZaiNoCodingPlan =>
        "This key works, but the account has no Coding Plan running. The plan is a subscription on the "
        + "account, not a property of the key.";

    public override string UnavailableCodexServerFailed => "The Codex helper could not be started.";

    public override string UnavailableGeneric => "Unavailable.";

    public override string CredentialCodex =>
        @"Borrows the login Codex saved at %USERPROFILE%\.codex\auth.json.";

    public override string CredentialOpenCodeGo =>
        @"Reads %USERPROFILE%\.local\share\opencode\auth.json, or a key you paste here.";

    public override string CredentialZhipu =>
        "Mainland storefront (open.bigmodel.cn). Paste a key, or it reads a saved GLM key file.";

    public override string CredentialZai =>
        "International storefront (api.z.ai). A key from the mainland service is refused here.";

    public override string CredentialDeepSeek =>
        "Paste a key from platform.deepseek.com. Reports a prepaid balance — there is no allowance.";

    public override string MenuRefreshNow => "Refresh now";

    public override string MenuSettings => "Settings…";

    public override string MenuHideRail => "Hide the rail";

    public override string MenuToggleRail => "Show or hide the rail";

    public override string MenuExit => "Exit PulseWin";

    public override string TrayTooltipName => "PulseWin";

    public override string DirectionLeft => "left";

    public override string DirectionUsed => "used";

    public override string CardCountingDown => "Counting down — what is left";

    public override string CardCountingUp => "Counting up — what is used";

    public override string CardResetsIn(string duration) => $"resets in {duration}";

    public override string CardWindowElapsed(int percent) => $"{percent}% of the window elapsed";

    public override string CardSuffixLeft(string figure) => $"{figure} Left";

    public override string CardSuffixUsed(string figure) => $"{figure} Used";

    public override string CardSpent => "spent";

    public override string CardNoLimitOnlyBalance => "This service reports no limit — only a balance.";

    public override string CardBalance => "Balance";

    public override string CardFiguresAreFrom(string ago) =>
        $"Figures above are from {ago}, not from the last check.";

    public override string CardNeverRead => "Never read.";

    public override string CardChecked(string ago) => $"Checked {ago}";

    public override string AgoNow => "just now";

    public override string AgoMinutes(int minutes) => $"{minutes} min ago";

    public override string AgoHours(int hours) => $"{hours} h ago";

    public override string AgoDays(int days) => $"{days} d ago";

    public override string CountdownNow => "now";

    public override string SettingsTitle => "PulseWin settings";

    public override string SettingsServices => "Services";

    public override string SettingsServicesCaption =>
        "A switched-off service is not checked at all — no credentials are read and no request is made.";

    public override string SettingsDeepSeek => "DeepSeek";

    public override string SettingsDeepSeekCaption =>
        "DeepSeek reports a balance and no allowance, so the ring needs a denominator from somewhere. "
        + "Nothing here is a guess about DeepSeek's pricing.";

    public override string SettingsRail => "The rail";

    public override string SettingsCodexAccounts => "Codex accounts";

    public override string SettingsCodexAccountsCaption =>
        "The ring above reads the login Codex saved on this machine. Add an account here to monitor a "
        + "second subscription alongside it — each one carries its own token.";

    public override string SettingsBasisSinceTopUp =>
        "Since top-up — measure against the highest balance this app has watched";

    public override string SettingsBasisBalanceOnly =>
        "Balance only — draw the money, with no percentage at all";

    public override string SettingsBasisBudget => "My budget — measure against a figure I type";

    public override string SettingsBudget => "Budget";

    public override string SettingsCurrency => "Currency";

    public override string SettingsCurrencyTooltip =>
        "Blank follows the first purse with money in it. An account can hold both CNY and USD.";

    public override string SettingsEdge => "Edge";

    public override string SettingsEdgeRight => "Right";

    public override string SettingsEdgeLeft => "Left";

    public override string SettingsEdgeTop => "Top";

    public override string SettingsEvery => "Every";

    public override string SettingsMinutesTooltip => "Minutes between checks, 1 to 60.";

    public override string SettingsOffset => "Offset";

    public override string SettingsOffsetTooltip =>
        "Slides the rail along its edge, in pixels. Negative moves it up or left.";

    public override string SettingsCountdown =>
        "Count down instead of up — show what is left, figure and ring together";

    public override string SettingsCountdownNote =>
        "The colour always follows what is gone, so a sliver of quota left stays a small red arc.";

    public override string SettingsClear => "Clear";

    public override string SettingsRemove => "Remove";

    public override string SettingsAddAccount => "Add account";

    public override string SettingsSwitchCodexOn => "Switch Codex on to add accounts.";

    public override string SettingsLanguage => "Language";

    public override string SettingsLanguageAuto => "Follow Windows";

    public override string SettingsStatusOff => "off";

    public override string SettingsStatusNotChecked => "not checked yet";

    public override string SettingsStatusNeedsKey => "needs a key";

    public override string SettingsStatusRead => "read";

    public override string SettingsStatusStale(string figure) => $"{figure} (stale)";

    public override string SettingsKeyStored => "A key is already stored. Typing here replaces it.";

    public override string SettingsKeyPaste => "Paste the key here.";

    public override string SettingsAccountLabelTooltip => "What to call this account on the rail.";

    public override string SettingsAccountTokenTooltip =>
        "The account's Codex access token. Sent as the bearer for this ring only.";

    public override string SettingsAccountIdTooltip =>
        "The ChatGPT account id this token belongs to. Sent as ChatGPT-Account-Id, and required for a "
        + "second account: without it the service may answer for whichever login it likes.";

    public override string SettingsDefaultAccountName => "Codex account";

    public override string SignInTitle => "Sign in to Codex";

    public override string SignInIntro =>
        "Open the page below and type this code. Nothing is redirected to this machine, so the sign-in can "
        + "run alongside the Codex CLI without disturbing it.";

    public override string SignInCopyCode => "Copy code";

    public override string SignInOpenPage => "Open the page";

    public override string SignInWaiting => "Waiting for you to approve it in the browser…";

    public override string SignInSucceeded(string who) => $"Signed in as {who}.";

    public override string SignInFailed(string why) => $"Sign-in failed: {why}";

    public override string SignInCancel => "Cancel";

    public override string SignInAdd => "Sign in to another account";

    public override string SignInNone => "No extra Codex accounts yet.";

    public override string SignInClose => "Close";

    public override string SignInPreparing => "Asking Codex for a code…";

    public override string SignInWhySignIn =>
        "Signing in rather than pasting a token is deliberate: a Codex access token lasts about ten days, and "
        + "the only thing that can renew a copied one is the CLI's own refresh token — which, if the service "
        + "rotates it, signs you out of your own Codex.";

    public override string SignInAccountFallback => "Codex account";

    public override string RailResetPosition => "Put the rail back on its edge";

    public override (double Threshold, double Divisor, string Suffix)[] MoneyTiers =>
    [
        (1_000_000, 1_000_000, "M"),
        (1_000, 1_000, "k"),
    ];
}
