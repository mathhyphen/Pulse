using System.Text;
using System.Text.Json;
using PulseWin.Auth;
using PulseWin.Core;
using PulseWin.Providers;
using PulseWin.Storage;

namespace PulseWin;

/// <summary>
/// Checks the ported parsers against <b>captured</b> service replies.
///
/// <para>
/// This is the part of the port worth being most careful about, because none of it
/// is visible in a screenshot. A field that moved on an undocumented endpoint, a
/// fraction worked out from the wrong pair of counts, a "spent" flag smeared
/// across a whole group instead of pinned to the window that caused it — all of
/// them draw a plausible-looking ring and all of them are wrong.
/// </para>
/// <para>
/// Corpus provenance is labelled per case. The Zhipu and DeepSeek bodies are the
/// ones Pulse captured from the live services and committed to its own test
/// fixtures, so they are real traffic. The Codex bodies are <b>constructed</b>
/// from the field names its service reads, because no captured body was published
/// — they verify that this port reads that shape consistently, not that the shape
/// is what the live endpoint still sends.
/// </para>
/// </summary>
public static class FixtureCheck
{
    // ---------------------------------------------------------------- corpus

    /// <summary>Captured from open.bigmodel.cn. A freshly bought Lite plan.</summary>
    private const string ZhipuCaptured = """
    {
      "code": 200,
      "msg": "操作成功",
      "data": {
        "limits": [
          { "type": "CREDIT_LIMIT", "unit": 3, "number": 5,
            "usage": 2000, "currentValue": 0, "remaining": 2000, "percentage": 0 },
          { "type": "CREDIT_LIMIT", "unit": 6, "number": 1,
            "usage": 10000, "currentValue": 0, "remaining": 10000, "percentage": 0,
            "nextResetTime": 1789373585999 }
        ],
        "level": "lite"
      },
      "success": true
    }
    """;

    private const string DeepSeekBalanceCaptured = """
    { "is_available": true,
      "balance_infos": [ { "currency": "CNY", "total_balance": "42.30",
                           "granted_balance": "10.00", "topped_up_balance": "32.30" } ] }
    """;

    /// <summary>Captured: USD with nothing in it listed before the funded CNY purse.</summary>
    private const string DeepSeekTwoCurrenciesCaptured = """
    { "is_available": true,
      "balance_infos": [
        { "currency": "USD", "total_balance": "0.00", "granted_balance": "0.00", "topped_up_balance": "0.00" },
        { "currency": "CNY", "total_balance": "42.30", "granted_balance": "10.00", "topped_up_balance": "32.30" } ] }
    """;

    private const string DeepSeekSpentCaptured = """
    { "is_available": false,
      "balance_infos": [ { "currency": "CNY", "total_balance": "0.00",
                           "granted_balance": "0.00", "topped_up_balance": "0.00" } ] }
    """;

    /// <summary>Constructed from the field names <c>CodexService</c> reads.</summary>
    private const string CodexConstructed = """
    {
      "plan_type": "plus",
      "rate_limit": {
        "allowed": true,
        "limit_reached": false,
        "primary_window":   { "used_percent": 42.5, "limit_window_seconds": 18000,  "reset_at": 1789373586 },
        "secondary_window": { "used_percent": 12.0, "limit_window_seconds": 604800, "reset_at": 1789973586 }
      },
      "additional_rate_limits": [
        { "limit_name": "GPT-5 Codex", "metered_feature": "codex_gpt5",
          "rate_limit": { "allowed": true,
            "primary_window": { "used_percent": 88.0, "limit_window_seconds": 18000, "reset_at": 1789373586 } } }
      ],
      "credits": { "unlimited": false, "balance": "12.50" }
    }
    """;

    /// <summary>Constructed: the group is spent, but only one window is the reason.</summary>
    private const string CodexSpentConstructed = """
    {
      "plan_type": "plus",
      "rate_limit": {
        "limit_reached": true,
        "primary_window":   { "used_percent": 100.0, "limit_window_seconds": 18000,  "reset_at": 1789373586 },
        "secondary_window": { "used_percent": 30.0,  "limit_window_seconds": 604800, "reset_at": 1789973586 }
      }
    }
    """;

    // ---------------------------------------------------------------- checks

    public static (int Passed, int Failed, string Report) Run()
    {
        var log = new StringBuilder();
        var passed = 0;
        var failed = 0;

        void Check(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                passed++;
                log.AppendLine($"  PASS  {name}");
            }
            else
            {
                failed++;
                log.AppendLine($"  FAIL  {name}{(detail.Length > 0 ? $"  — {detail}" : "")}");
            }
        }

        static JsonElement Root(string json) =>
            JsonDocument.Parse(json).RootElement.Clone();

        // ---------------------------------------------------------- Zhipu

        log.AppendLine("Zhipu / bigmodel — captured from open.bigmodel.cn");
        {
            var root = Root(ZhipuCaptured);
            var data = root.Obj("data")!.Value;
            var windows = ZhipuService.Windows(data.Items("limits"), Provider.Zhipu);

            Check("envelope accepted", root.Bool("success") == true && root.Int("code") == 200);
            Check("two limits became two windows", windows.Count == 2, $"got {windows.Count}");

            if (windows.Count == 2)
            {
                var first = windows[0];
                var second = windows[1];

                Check("shortest window sorts first", first.WindowSeconds < second.WindowSeconds,
                    $"{first.WindowSeconds} vs {second.WindowSeconds}");

                Check("unit 3 x 5 is a five-hour window",
                    first.Kind == WindowKind.FiveHour && first.WindowSeconds == 18_000,
                    $"kind={first.Kind} seconds={first.WindowSeconds}");

                Check("unit 6 x 1 is a weekly window",
                    second.Kind == WindowKind.Weekly && second.WindowSeconds == 604_800,
                    $"kind={second.Kind} seconds={second.WindowSeconds}");

                // usage == remaining is a spend of zero. It is a *reading*, and the
                // distinction from "no figure" is what keeps a fresh plan green
                // rather than blank.
                Check("usage == remaining reads as 0% used, not as missing",
                    Math.Abs(first.UsedFraction) < 1e-9 && Math.Abs(second.UsedFraction) < 1e-9,
                    $"first={first.UsedFraction} second={second.UsedFraction}");

                Check("only the weekly limit carries a reset stamp",
                    first.ResetsAt is null && second.ResetsAt is not null);

                Check("nextResetTime is read as epoch milliseconds, not seconds",
                    second.ResetsAt == DateTimeOffset.FromUnixTimeMilliseconds(1789373585999),
                    $"got {second.ResetsAt:O}");

                Check("window id carries the limit's position",
                    first.Id == "glmCoding.CREDIT_LIMIT.3-5.0" && second.Id == "glmCoding.CREDIT_LIMIT.6-1.1",
                    $"{first.Id} / {second.Id}");

                Check("reported lengths may be divided by, so the clock is drawn",
                    first.ReportsLength && first.ElapsedFraction(DateTimeOffset.Now) is null,
                    "five-hour limit states no reset, so the clock must stay absent");
            }

            Check("plan label falls through to `level`",
                data.Text("planName") is null && data.Text("level") == "lite");
        }
        log.AppendLine();

        // A refusal is an HTTP 200 with a verdict inside the envelope. Every one of
        // these was measured against the live mainland host.
        log.AppendLine("Zhipu — envelope refusals (captured wording)");
        {
            void Refusal(string json, Unavailability expected, string name) =>
                Check(name, ZhipuService.Problem(Root(json)) == expected,
                    $"got {ZhipuService.Problem(Root(json))}");

            Refusal("""{"success":false,"code":500,"msg":"当前用户不存在coding plan"}""",
                Unavailability.ZaiNoCodingPlan, "a working key with no subscription is not a bad key");

            Refusal("""{"success":false,"code":1000,"msg":"身份验证失败。"}""",
                Unavailability.ApiKeyRefused, "Zhipu's own 1000 is authentication");

            Refusal("""{"success":false,"code":401,"msg":"令牌已过期或验证不正确"}""",
                Unavailability.ApiKeyRefused, "a wrong-shaped key");

            Refusal("""{"success":false,"code":1001,"msg":"Header中未收到Authorization参数"}""",
                Unavailability.ApiKeyRefused, "a missing header, matched on the English word inside it");

            // The trap: the vendor's generic 500 with a sentence that says nothing
            // about the key must not send the reader to check a credential.
            Refusal("""{"success":false,"code":500,"msg":"服务暂时不可用"}""",
                Unavailability.ServerError, "an outage is not reported as a key problem");
        }
        log.AppendLine();

        // ------------------------------------------------------- DeepSeek

        log.AppendLine("DeepSeek — captured bodies");
        {
            var balance = Root(DeepSeekBalanceCaptured);
            var purse = DeepSeekService.SelectPurse(balance, null);

            Check("the funded purse is read", purse is { Currency: "CNY" } && Math.Abs(purse!.Value.Total - 42.30) < 1e-9,
                $"got {purse}");

            // Money is a string in this reply and stays absent when unparseable —
            // the rule whose violation draws a full red ring on a healthy account.
            Check("money is parsed from a string", purse!.Value.Granted == 10.00 && purse.Value.ToppedUp == 32.30);
            Check("a figure that is absent is absent, not zero", balance.Money("nope") is null);

            if (purse is { } p)
            {
                var mark = DeepSeekMarks.Advanced(null, p.Total, DateTimeOffset.Now);

                var sinceTopUp = DeepSeekService.BuildWindows(
                    p, DeepSeekBasis.SinceTopUp, null, mark, true);
                Check("first sight becomes the mark, so the ring reads 0%",
                    sinceTopUp.Count == 1 && Math.Abs(sinceTopUp[0].UsedFraction) < 1e-9);

                var balanceOnly = DeepSeekService.BuildWindows(
                    p, DeepSeekBasis.BalanceOnly, null, mark, true);
                Check("balance-only draws no window at all", balanceOnly.Count == 0,
                    $"got {balanceOnly.Count}");

                var budget = DeepSeekService.BuildWindows(
                    p, DeepSeekBasis.Budget, 100, mark, true);
                Check("a budget gives the fraction a denominator",
                    budget.Count == 1 && Math.Abs(budget[0].UsedFraction - 0.577) < 1e-6,
                    $"got {(budget.Count == 1 ? budget[0].UsedFraction : double.NaN)}");
                Check("the budget row says where its denominator came from",
                    budget.Count == 1 && budget[0].Estimate == WindowEstimate.YourBudget);

                // A denominator nobody gave is not a denominator.
                Check("a blank, zero or non-finite budget leaves no fraction",
                    DeepSeekService.BuildWindows(p, DeepSeekBasis.Budget, null, mark, true).Count == 0
                    && DeepSeekService.BuildWindows(p, DeepSeekBasis.Budget, 0, mark, true).Count == 0
                    && DeepSeekService.BuildWindows(p, DeepSeekBasis.Budget, double.PositiveInfinity, mark, true).Count == 0);

                Check("prepaid credit reports no length and no reset",
                    sinceTopUp[0] is { ReportsLength: false, ResetsAt: null, Kind: WindowKind.Balance });
            }

            // Two currencies cannot be added, and the funded one is not necessarily
            // the first one listed.
            var two = Root(DeepSeekTwoCurrenciesCaptured);
            Check("the first purse with money wins over an empty one listed first",
                DeepSeekService.SelectPurse(two, null) is { Currency: "CNY" });
            Check("an explicit choice is honoured even when it is the empty one",
                DeepSeekService.SelectPurse(two, "USD") is { Currency: "USD" });

            // `is_available` is the only thing that may say spent.
            var spent = Root(DeepSeekSpentCaptured);
            var spentPurse = DeepSeekService.SelectPurse(spent, null);
            var spentMark = DeepSeekMarks.Advanced(null, spentPurse!.Value.Total, DateTimeOffset.Now);
            var spentWindows = DeepSeekService.BuildWindows(
                spentPurse.Value, DeepSeekBasis.SinceTopUp, null, spentMark, spent.Bool("is_available"));

            Check("a peak of zero draws no window — never-credited is not spent",
                spentWindows.Count == 0, $"got {spentWindows.Count}");

            // With a mark that exists, the same body must come back marked spent.
            var withPeak = new DeepSeekMarks.Mark(100, DateTimeOffset.Now.AddDays(-3));
            var spentWithPeak = DeepSeekService.BuildWindows(
                spentPurse.Value, DeepSeekBasis.SinceTopUp, null, withPeak, spent.Bool("is_available"));

            Check("with a watched peak, a drained balance reads 100% and is marked spent",
                spentWithPeak.Count == 1
                && Math.Abs(spentWithPeak[0].UsedFraction - 1.0) < 1e-9
                && spentWithPeak[0].IsExhausted);

            // A budget the reader set low can reach 100% with money still in the
            // account. That is not the account being spent, and only DeepSeek's own
            // flag may say so.
            var budgetAtZero = DeepSeekService.BuildWindows(
                spentPurse.Value, DeepSeekBasis.Budget, 0.01, withPeak, spent.Bool("is_available"));
            Check("budget mode marks spent only because is_available says so",
                budgetAtZero.Count == 1 && budgetAtZero[0].IsExhausted);

            var healthyButOverBudget = DeepSeekService.BuildWindows(
                new DeepSeekService.Purse("CNY", 50, null, null), DeepSeekBasis.Budget, 10, withPeak, true);
            Check("a healthy account over its budget is not marked spent",
                healthyButOverBudget.Count == 1 && !healthyButOverBudget[0].IsExhausted);

            // **Money with no window is no fraction, and that is not a fraction of
            // zero.** Counting down made the two collide — `1 - 0` is a full ring — so
            // an account reporting no allowance at all drew a complete green circle
            // saying it had everything left. The rail reads `HasFraction` to tell the
            // two apart; this pins the side of it that comes from the service.
            var moneyOnly = DeepSeekService.BuildWindows(
                new DeepSeekService.Purse("CNY", 66.11, null, null),
                DeepSeekBasis.BalanceOnly, null, withPeak, true);

            Check("balance-only produces no fraction to draw", moneyOnly.Count == 0,
                $"got {moneyOnly.Count} window(s)");

            var asReading = new ProviderUsage
            {
                Account = AccountKey.Primary(Provider.DeepSeek),
                Windows = moneyOnly,
                CreditBalance = "¥66.11",
                CreditRemaining = new CreditInfo(66.11, "CNY"),
            };

            Check("and the rail is told there is nothing to draw",
                asReading.Fullest is null && asReading.ReportsSomething,
                "a balance is an answer, but it is not an arc");
        }
        log.AppendLine();

        // ---------------------------------------------------------- Codex

        log.AppendLine("Codex — constructed from the field names the service reads");
        {
            var usage = CodexService.Parse(Root(CodexConstructed), AccountKey.Primary(Provider.Codex));

            Check("three windows: account-wide pair plus one named group", usage.Windows.Count == 3,
                $"got {usage.Windows.Count}");
            Check("plan identifier is turned into the name on the plan", usage.Plan == "Plus",
                $"got {usage.Plan}");
            Check("credit balance is read", usage.CreditBalance == "12.50",
                $"got {usage.CreditBalance}");

            var primary = usage.Windows.FirstOrDefault(w => w.Id == "codex.primary_window");
            var secondary = usage.Windows.FirstOrDefault(w => w.Id == "codex.secondary_window");
            var named = usage.Windows.FirstOrDefault(w => w.Id == "codex_gpt5.primary_window");

            // A window's kind comes from its duration, never from which slot it
            // arrived in: Pro has no 5-hour limit and reports its weekly one as
            // primary.
            Check("the primary slot is a five-hour window because of its duration",
                primary is { Kind: WindowKind.FiveHour } && Math.Abs(primary.UsedFraction - 0.425) < 1e-9);
            Check("the secondary slot is a weekly window",
                secondary is { Kind: WindowKind.Weekly } && Math.Abs(secondary.UsedFraction - 0.12) < 1e-9);
            Check("a per-model group is named by metered_feature and scoped by limit_name",
                named is { Scope: "GPT-5 Codex" } && Math.Abs(named.UsedFraction - 0.88) < 1e-9);

            // The precision rule: a group's "limit reached" belongs to the window
            // that caused it, not to every window in the group.
            var spent = CodexService.Parse(Root(CodexSpentConstructed), AccountKey.Primary(Provider.Codex));
            var spentPrimary = spent.Windows.First(w => w.Id == "codex.primary_window");
            var spentSecondary = spent.Windows.First(w => w.Id == "codex.secondary_window");

            Check("the fullest window in a spent group is the one marked",
                spentPrimary.IsExhausted, "the 100% window must carry the mark");
            Check("its siblings are left alone",
                !spentSecondary.IsExhausted, "the 30% weekly window must not be marked");
            Check("per-model scopes do not leak onto account-wide rows",
                primary!.Scope is null && secondary!.Scope is null);

            Check("unknown plan identifiers pass through rather than blanking",
                CodexService.PlanName("some-new-tier") == "some-new-tier");
            Check("the 5x Pro tier is named for what the buyer saw",
                CodexService.PlanName("prolite") == "Pro 5x");
        }

        log.AppendLine("Figures — the rule that holds both ends off the extremes");
        {
            // Nothing used reads 100%, anything used reads at most 99%. Nothing
            // left reads 0%, anything left reads at least 1%. The two views need
            // not sum to 100, because only one is ever on screen.
            Check("nothing used reads 100% left", UsageWindow.Figure(1.0) == 100, $"got {UsageWindow.Figure(1.0)}");
            Check("everything used reads 0% left", UsageWindow.Figure(0.0) == 0, $"got {UsageWindow.Figure(0.0)}");

            // The two that a subtraction would get wrong.
            Check("a window 0.4% spent does not read 100% left",
                UsageWindow.PercentTextOf(1 - 0.004) == "99%", $"got {UsageWindow.PercentTextOf(1 - 0.004)}");
            Check("a window 99.6% spent does not read 0% left",
                UsageWindow.PercentTextOf(1 - 0.996) == "1%", $"got {UsageWindow.PercentTextOf(1 - 0.996)}");

            Check("the smallest real reading never rounds to 0%",
                UsageWindow.Figure(0.0003) == 1, $"got {UsageWindow.Figure(0.0003)}");

            // A budget of "inf" typed into Settings arrives as (inf - balance)/inf.
            // Casting NaN to int is undefined; upstream crashed on every launch
            // until the field was cleared, because the figure had been persisted.
            Check("a non-finite fraction reads 0% rather than trapping",
                UsageWindow.Figure(double.NaN) == 0
                && UsageWindow.Figure(double.PositiveInfinity) == 0
                && UsageWindow.Figure(double.NegativeInfinity) == 0);

            Check("fractions outside 0..1 are clamped rather than shown",
                UsageWindow.Figure(1.3) == 100 && UsageWindow.Figure(-0.2) == 0);

            // 3% used is 97% left, and the pair must read the way round it was asked.
            var window = new UsageWindow
            {
                Id = "t", Kind = WindowKind.Weekly, UsedFraction = 0.03,
            };
            Check("3% used reads 3% counting up and 97% counting down",
                window.PercentText() == "3%" && window.PercentText(remaining: true) == "97%",
                $"{window.PercentText()} / {window.PercentText(remaining: true)}");
        }

        log.AppendLine("Codex extra accounts — the store and the token plumbing");
        {
            // The sign-in itself needs a human at a browser, so it cannot be driven
            // here. Everything it hands its result to can be, and those are the parts
            // that would silently do the wrong thing: the encrypted store, the purpose
            // separation, the renewal boundary, and reading an account id out of a
            // token that keeps it nested.

            var key = new AccountKey(Provider.Codex, "fixture-account");
            var credentials = new AccountCredentials
            {
                AccessToken = "fixture-access",
                RefreshToken = "fixture-refresh",
                ExpiresAt = DateTimeOffset.Now.AddHours(1),
                ServiceAccountId = "acct-1",
                Email = "somebody@example.com",
            };

            AccountCredentialStore.Set(key, credentials);
            var read = AccountCredentialStore.For(key);

            Check("a login survives the encrypted store",
                read?.RefreshToken == "fixture-refresh"
                && read.ServiceAccountId == "acct-1"
                && read.Email == "somebody@example.com",
                $"read back {read?.RefreshToken}/{read?.ServiceAccountId}");

            Check("a token an hour out does not need renewing",
                read?.NeedsRenewal(DateTimeOffset.Now) == false);

            Check("a token inside the minute of slack does",
                new AccountCredentials { ExpiresAt = DateTimeOffset.Now.AddSeconds(30) }
                    .NeedsRenewal(DateTimeOffset.Now));

            AccountCredentialStore.Remove(key);
            Check("removing an account takes its login with it",
                AccountCredentialStore.For(key) is null);

            // **The purpose string is what stops one store's box being opened by
            // another.** README claims it; this is the assertion that keeps it true.
            var scratch = Path.Combine(Path.GetTempPath(), "pulsewin-fixture-box.dat");
            try
            {
                var payload = new Dictionary<string, string> { ["k"] = "v" };
                EncryptedFile.Save(scratch, "purpose-a", payload);

                var same = EncryptedFile.Load<Dictionary<string, string>>(scratch, "purpose-a");
                var other = EncryptedFile.Load<Dictionary<string, string>>(scratch, "purpose-b");

                Check("a box opens with the purpose that wrote it", same?["k"] == "v");
                Check("and not with a different one", other is null,
                    "a box from keys.dat must not open as accounts.dat");
            }
            finally
            {
                if (File.Exists(scratch)) File.Delete(scratch);
            }

            // A token shaped the way OpenAI's is: an email at the top level and the
            // account id nested under a namespace, in the access token only.
            static string Segment(string json) => Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(json))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

            var token = Segment("""{"alg":"none"}""") + "." + Segment(
                """{"email":"a@b.c","https://api.openai.com/auth":{"chatgpt_account_id":"acct-9"}}""")
                + ".signature";

            var claims = Jwt.Claims(token);
            var auth = claims?.Field("https://api.openai.com/auth");

            Check("the account id is read from the namespaced claim, not the top level",
                auth is not null && auth.Value.Str("chatgpt_account_id") == "acct-9",
                $"got {auth?.Text("chatgpt_account_id") ?? "nothing"}");

            Check("the email is read from the top level",
                claims?.Str("email") == "a@b.c");

            Check("something that is not a JWT is refused rather than throwing",
                Jwt.Claims("not-a-jwt") is null && Jwt.Claims("") is null);
        }

        log.AppendLine();
        log.AppendLine($"  {passed} passed, {failed} failed");

        return (passed, failed, log.ToString());
    }
}
