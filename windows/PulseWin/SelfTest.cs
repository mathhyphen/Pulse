using System.Text;
using System.Windows;
using System.Windows.Media;
using PulseWin.Core;
using PulseWin.Localization;
using PulseWin.Providers;
using PulseWin.Services;
using PulseWin.Storage;
using PulseWin.Ui;

namespace PulseWin;

/// <summary>
/// A headless check of the provider pipeline, reachable as <c>PulseWin.exe --selftest</c>.
///
/// <para>
/// This exists because the interesting failures in this app are not visual: a
/// field that moved on an undocumented endpoint, a key file that is read with the
/// wrong line ending, an envelope that refuses a perfectly good HTTP 200. None of
/// those show up in a screenshot, and all of them show up immediately here.
/// </para>
/// <para>
/// It writes a report to a file rather than to stdout because the app is a
/// <c>WinExe</c> and has no console attached when launched normally.
/// </para>
/// </summary>
public static class SelfTest
{
    public static string ReportPath =>
        Path.Combine(Path.GetTempPath(), "pulsewin-selftest.txt");

    public static string FixtureReportPath =>
        Path.Combine(Path.GetTempPath(), "pulsewin-fixtures.txt");

    /// <summary>
    /// Runs the parser checks against captured replies and reports the tally.
    ///
    /// Kept as its own entry point as well as being folded into the self-test,
    /// because it is the one thing worth running after <i>any</i> change to a
    /// provider: it needs no network, no key and no account, and it fails loudly
    /// when an undocumented field has been read wrongly.
    /// </summary>
    public static int RunFixtures()
    {
        var (passed, failed, report) = FixtureCheck.Run();
        var text = $"PulseWin fixture check · {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}"
                   + Environment.NewLine + new string('=', 78) + Environment.NewLine
                   + report;

        try
        {
            File.WriteAllText(FixtureReportPath, text);
        }
        catch (IOException)
        {
        }

        try
        {
            Console.WriteLine(text);
        }
        catch (IOException)
        {
        }

        return failed == 0 ? 0 : 1;
    }


    public static async Task<int> RunAsync()
    {
        var report = new StringBuilder();
        report.AppendLine($"PulseWin self-test · {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine($"Settings: {AppPaths.Settings}");
        report.AppendLine($"Secrets:  {AppPaths.Secrets}");
        report.AppendLine(new string('-', 78));

        CredentialStore.Load();
        var settings = AppSettings.Current;

        // The report is drawn in whatever language the app would use, so the
        // resolved strings below are the ones a reader would actually see.
        Loc.Initialise(settings.Language);

        report.AppendLine();
        report.AppendLine("CREDENTIAL DISCOVERY (what is readable without asking the user)");
        report.AppendLine($"  Codex auth file  {CodexService.AuthFile}  exists={File.Exists(CodexService.AuthFile)}");
        report.AppendLine($"  OpenCode auth.json   {(OpenCodeGoService.StoredKey() is not null ? "found" : "not found")}");
        report.AppendLine($"  Zhipu key file       {(ZhipuService.StoredKey(Provider.Zhipu) is not null ? "found" : "not found")}");
        foreach (var provider in ProviderCatalog.All)
        {
            report.AppendLine(
                $"  {provider.DisplayName(),-16} pasted key stored = {CredentialStore.Key(provider) is not null}");
        }

        report.AppendLine();
        report.AppendLine(new string('-', 78));
        report.AppendLine("LIVE FETCH (every provider, whether or not it is switched on)");
        report.AppendLine();

        // Every provider, not just the enabled ones: a self-test that skipped the
        // unconfigured ones would pass on a machine where nothing is set up. Plus any
        // added account, which is the only place the signed-in fetch path is reachable
        // — it went untested for a while precisely because this list did not include
        // them.
        var cached = AppSettings.Current;
        var store = new UsageStore();
        var accounts = ProviderCatalog.All
            .Select(provider => new MonitoredAccount { Key = AccountKey.Primary(provider) })
            .Concat(cached.Accounts.Where(a => !a.Key.IsPrimary))
            .ToList();

        // **The way the rail actually does it: every account at once.** The loop below
        // fetches them one after another, and the two paths did not agree — four
        // accounts answered when asked in sequence and two of the same four failed
        // when asked together, which sent this diagnostic looking in the wrong place
        // for a while. Reporting both is what makes the difference visible.
        report.AppendLine("CONCURRENT PASS (exactly what the rail does when it refreshes)");
        report.AppendLine();

        var concurrent = new UsageStore();
        await concurrent.RefreshAllAsync();

        foreach (var (account, state) in concurrent.Snapshot())
        {
            var said = state.Reading is not null
                ? $"read ok ({state.Reading.Windows.Count} window(s))"
                : $"FAILED — {state.LastFailure?.Message() ?? "no reading and no stated reason"}";

            report.AppendLine($"  {account.DisplayLabel,-18} {said}");
        }

        report.AppendLine($"  any failed: {concurrent.AnyFailed}");
        report.AppendLine();
        report.AppendLine(new string('-', 78));
        report.AppendLine("SEQUENTIAL FETCH (one at a time, for comparison)");
        report.AppendLine();

        foreach (var account in accounts)
        {
            var usage = await Fetch(account);
            Describe(report, usage);
        }

        report.AppendLine();
        report.AppendLine(new string('-', 78));
        report.AppendLine("PARSER CHECKS AGAINST CAPTURED REPLIES");
        report.AppendLine();
        var (passed, failed, fixtureReport) = FixtureCheck.Run();
        report.Append(fixtureReport);

        report.AppendLine();
        DescribeLocalisation(report);
        DescribeIcons(report);

        report.AppendLine();
        report.AppendLine(new string('-', 78));
        report.AppendLine("DEEPSEEK BASELINE MARKS");
        foreach (var currency in new[] { "CNY", "USD" })
        {
            var mark = DeepSeekMarks.Peek(currency);
            report.AppendLine(mark is null
                ? $"  {currency}: no mark yet"
                : $"  {currency}: peak={mark.Value.Peak:0.00} since={mark.Value.SetAt:yyyy-MM-dd HH:mm}");
        }

        report.AppendLine();
        report.AppendLine($"Settings file on disk: {settings.Accounts.Count} account(s), {settings.Enabled.Count} enabled");

        var text = report.ToString();
        await File.WriteAllTextAsync(ReportPath, text);

        // Best effort: also print when a console happens to be attached, so
        // `dotnet run -- --selftest` in a terminal is useful too.
        try
        {
            Console.WriteLine(text);
        }
        catch (IOException)
        {
        }

        return 0;
    }

    private static void DescribeIcons(StringBuilder report)
    {
        report.AppendLine(new string('-', 78));
        report.AppendLine("PROVIDER MARKS (SVG path data parsed straight into WPF geometry)");
        report.AppendLine();

        foreach (var provider in ProviderCatalog.All)
        {
            var (ok, detail) = ProviderIcons.Diagnose(provider);
            report.AppendLine($"  {(ok ? "OK  " : "FAIL")}  {provider.DisplayName(),-16} {detail}");
        }

        report.AppendLine();
    }

    /// <summary>
    /// Fetches one provider the way the app does.
    /// </summary>
    /// <remarks>
    /// The pasted key is passed in, because that is what <c>UsageStore</c> does. The
    /// first version of this passed null and let each service find its own fallback,
    /// which meant a stored key reported "none has been entered" — a self-test that
    /// was quietly testing a path the app never takes.
    /// </remarks>
    private static Task<ProviderUsage> Fetch(MonitoredAccount account)
    {
        var provider = account.Key.Provider;
        var entered = CredentialStore.Key(provider);

        return provider switch
        {
            Provider.Codex => CodexService.FetchAsync(account),
            Provider.OpenCodeGo => OpenCodeGoService.FetchAsync(account.Key, entered),
            Provider.Zhipu or Provider.Zai => ZhipuService.FetchAsync(account.Key, entered),
            Provider.DeepSeek => DeepSeekService.FetchAsync(account.Key, entered),
            _ => Task.FromResult(ProviderUsage.Failed(account.Key, Unavailability.NoLimitsReported)),
        };
    }

    /// <summary>
    /// Reports the localisation, and proves the Chinese is real glyphs.
    /// </summary>
    /// <remarks>
    /// A missing CJK face does not fail — WPF draws a "tofu" box instead, which
    /// looks like a rendered character until you read it, and there is no exception
    /// to catch. So the check renders one character and looks at whether there is
    /// ink in the <i>middle</i> of its box: a tofu box is hollow, and 中 is not.
    /// </remarks>
    private static void DescribeLocalisation(StringBuilder report)
    {
        report.AppendLine(new string('-', 78));
        report.AppendLine("LOCALISATION");
        report.AppendLine();

        var chosen = Loc.Setting;
        report.AppendLine($"  setting = {chosen}, resolved to {(ReferenceEquals(Loc.Current, Loc.Zh) ? "Chinese" : "English")}");
        report.AppendLine($"  Windows UI culture = {System.Globalization.CultureInfo.CurrentUICulture.Name}");
        report.AppendLine();

        (string What, string En, string Zh)[] samples =
        [
            ("settings title", Loc.En.SettingsTitle, Loc.Zh.SettingsTitle),
            ("5-hour window", Loc.En.WindowFiveHour, Loc.Zh.WindowFiveHour),
            ("weekly window", Loc.En.WindowWeekly, Loc.Zh.WindowWeekly),
            ("left suffix", Loc.En.CardSuffixLeft("97%"), Loc.Zh.CardSuffixLeft("97%")),
            ("refused key", Loc.En.UnavailableApiKeyRefused, Loc.Zh.UnavailableApiKeyRefused),
            ("resets in", Loc.En.CardResetsIn("3h 20m"), Loc.Zh.CardResetsIn("3h 20m")),
            ("elapsed", Loc.En.CardWindowElapsed(7), Loc.Zh.CardWindowElapsed(7)),
        ];

        report.AppendLine($"  {"",-16}{"English",-44}Chinese");
        foreach (var (what, en, zh) in samples)
            report.AppendLine($"  {what,-16}{en,-44}{zh}");

        // Money grouping differs by language rather than by taste.
        report.AppendLine();
        var wasSet = Loc.Setting;
        Loc.Setting = UiLanguage.English;
        var moneyEn = $"{MoneyFormat.RailText(12345, "CNY")} / {MoneyFormat.RailText(250_000_000, "CNY")}";
        Loc.Setting = UiLanguage.Chinese;
        var moneyZh = $"{MoneyFormat.RailText(12345, "CNY")} / {MoneyFormat.RailText(250_000_000, "CNY")}";
        Loc.Setting = wasSet;
        report.AppendLine($"  money  12345 and 2.5e8   English: {moneyEn}");
        report.AppendLine($"                          Chinese: {moneyZh}");
        report.AppendLine();

        var (total, interior, verdict) = FontCheck();
        report.AppendLine($"  CJK glyph check: '中' rendered with {Theme.Font}");
        report.AppendLine($"    ink pixels = {total}, of which in the middle of the box = {interior}");
        report.AppendLine($"    {verdict}");
        report.AppendLine();
    }

    /// <summary>
    /// Draws one Chinese character and reports whether the result is a glyph or a
    /// missing-glyph box.
    /// </summary>
    private static (int Total, int Interior, string Verdict) FontCheck()
    {
        const int size = 64;

        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var typeface = new Typeface(
                Theme.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            var text = new FormattedText(
                "中", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 44,
                System.Windows.Media.Brushes.White, 1.0);

            dc.DrawText(text, new Point(
                (size - text.Width) / 2, (size - text.Height) / 2));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = size * 4;
        var pixels = new byte[size * stride];
        bitmap.CopyPixels(pixels, stride, 0);

        var total = 0;
        var interior = 0;
        const int margin = (int)(size * 0.32);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (pixels[y * stride + x * 4 + 3] <= 60) continue;
                total++;
                if (x > margin && x < size - margin && y > margin && y < size - margin) interior++;
            }
        }

        var verdict = total == 0
            ? "FAIL — nothing was drawn at all"
            : interior == 0
                ? "FAIL — hollow box: the font stack has no CJK face, so Windows drew a missing-glyph box"
                : $"OK — {interior} ink pixels inside the box, so a real glyph was drawn";

        return (total, interior, verdict);
    }

    private static void Describe(StringBuilder report, ProviderUsage usage)
    {
        var name = usage.Account.Provider.DisplayName();
        report.AppendLine($"[{name}]");

        if (!usage.IsLive)
            report.AppendLine($"  unavailable: {usage.Unavailable!.Value.Message()}");
        else
            report.AppendLine($"  live · plan={usage.Plan ?? "(none reported)"} · credits={usage.CreditBalance ?? "(none)"}");

        foreach (var window in usage.Windows)
        {
            var reset = window.ResetsAt is { } at
                ? at.ToLocalTime().ToString("MM-dd HH:mm")
                : "—";
            var elapsed = window.ElapsedFraction(DateTimeOffset.Now);
            var clock = elapsed is null ? "—" : $"{elapsed.Value * 100:0}%";
            var spent = window.IsExhausted ? "  SPENT" : "";
            var estimate = window.Estimate is { } e ? $"  ({e})" : "";
            var scope = window.Scope is { } s ? $" · {s}" : "";

            report.AppendLine(
                $"  {window.Name,-34}{scope,-8} used={window.UsedFraction * 100,6:0.0}%  " +
                $"clock={clock,-5} resets={reset,-12}{spent}{estimate}");
        }

        if (usage.Windows.Count == 0 && usage.IsLive)
            report.AppendLine("  (no windows — this reading is money only)");

        if (usage.RailMoney is { } money)
            report.AppendLine($"  rail money: {money}   exact: {usage.CreditBalance}");

        report.AppendLine();
    }
}
