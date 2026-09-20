using System.Text;
using PulseWin.Core;
using PulseWin.Providers;
using PulseWin.Services;
using PulseWin.Storage;

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
        // unconfigured ones would pass on a machine where nothing is set up.
        var store = new UsageStore();
        var accounts = ProviderCatalog.All
            .Select(provider => new MonitoredAccount { Key = AccountKey.Primary(provider) })
            .ToList();

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

    private static Task<ProviderUsage> Fetch(MonitoredAccount account) =>
        account.Key.Provider switch
        {
            Provider.Codex => CodexService.FetchAsync(account),
            Provider.OpenCodeGo => OpenCodeGoService.FetchAsync(account.Key, null),
            Provider.Zhipu or Provider.Zai => ZhipuService.FetchAsync(account.Key, null),
            Provider.DeepSeek => DeepSeekService.FetchAsync(account.Key, null),
            _ => Task.FromResult(ProviderUsage.Failed(account.Key, Unavailability.NoLimitsReported)),
        };

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
