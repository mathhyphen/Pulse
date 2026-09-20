using System.Text.Json;
using PulseWin.Core;
using PulseWin.Storage;

namespace PulseWin.Providers;

/// <summary>
/// The GLM Coding Plan's limits, from Zhipu's quota endpoint.
///
/// <para>
/// <b>Two providers, one service.</b> Z.ai and BigModel are the same company's
/// international and mainland storefronts, answering the same JSON at the same
/// path on different hosts — but they are <b>separate accounts with separate
/// keys</b>, and a key for one is refused by the other. So each gets its own ring,
/// because someone with only the mainland plan (which is the "bigmodel"
/// subscription) should not have to know that an international one exists in
/// order to configure their own.
/// </para>
/// <para>
/// <c>GET {host}/api/monitor/usage/quota/limit</c>, with the key as a bearer
/// token. Undocumented, and it can change without notice.
/// </para>
/// <para>
/// <b>The reply wraps its payload in a status of its own</b> — <c>success</c> and
/// <c>code</c>, both of which have to say 200 even when HTTP did. A refused key
/// comes back as HTTP 200 with <c>success: false</c>, so reading only the status
/// line would report an empty plan rather than a bad key.
/// </para>
/// </summary>
public static class ZhipuService
{
    private static string Host(Provider provider) =>
        provider == Provider.Zhipu ? "https://open.bigmodel.cn" : "https://api.z.ai";

    private static string Endpoint(Provider provider) => $"{Host(provider)}/api/monitor/usage/quota/limit";

    /// <summary>
    /// A key already sitting on this machine, for the mainland plan only.
    ///
    /// The relay and console tools that set GLM up write the key to a one-line
    /// file, so anyone already using it configures nothing. <b>Never consulted for
    /// the international route</b>: they are separate accounts, and quietly sending
    /// a BigModel key to <c>api.z.ai</c> would report a refused key for a plan the
    /// user does not have.
    /// </summary>
    /// <remarks>
    /// Only the first readable line is taken, and it is taken carefully. Pulse
    /// shipped a bug here worth keeping the fix for: a CRLF file was not split at
    /// all, the whole file became the "key", and the HTTP layer then
    /// <i>silently discarded</i> a header value containing a newline — so the
    /// request went out unauthenticated, came back 401, and was reported as a
    /// refused key, about a key that was correct. Splitting on both CR and LF is
    /// the whole fix.
    /// </remarks>
    public static string? StoredKey(Provider provider)
    {
        if (provider != Provider.Zhipu) return null;

        var home = AppPaths.Home;
        var candidates = new[]
        {
            Path.Combine(home, ".coding-relay", "glm-api-key"),
            Path.Combine(home, ".config", "bigmodel", "api_key"),
            Path.Combine(home, ".config", "zhipu", "api_key"),
        };

        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path)) continue;

                var key = File.ReadAllText(path)
                    .Split('\n', '\r')
                    .Select(line => line.Trim())
                    .FirstOrDefault(line => line.Length > 0);

                if (!string.IsNullOrEmpty(key)) return key!;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    public static async Task<ProviderUsage> FetchAsync(
        AccountKey account, string? enteredKey, CancellationToken cancellationToken = default)
    {
        var provider = account.Provider;

        // What the user pasted wins, so a stale file cannot quietly override a
        // deliberate choice — the same order OpenCode Go's two sources take.
        var key = DeepSeekService.FirstNonEmpty(enteredKey, StoredKey(provider));
        if (key is null) return ProviderUsage.Failed(account, Unavailability.ApiKeyMissing);

        var result = await Http.GetAsync(Endpoint(provider), key, 15, cancellationToken: cancellationToken);
        if (result is null) return ProviderUsage.Failed(account, Unavailability.Unreachable);

        var failure = result.Value.Status switch
        {
            System.Net.HttpStatusCode.OK => (Unavailability?)null,
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => Unavailability.ApiKeyRefused,
            System.Net.HttpStatusCode.TooManyRequests => Unavailability.RateLimited,
            _ => Unavailability.ServerError,
        };
        if (failure is not null) return ProviderUsage.Failed(account, failure.Value);

        var root = Json.TryParse(result.Value.Body);
        if (root is null) return ProviderUsage.Failed(account, Unavailability.UnreadableReply);

        // The envelope's own verdict. A key the service refuses arrives here as a
        // perfectly good HTTP 200, so this is the only place it can be seen.
        //
        // **But not every refusal is about the key.** A 500 or a rate limit
        // arrives the same way, and reporting those as a bad key sends the user to
        // check a credential that is fine. The envelope's own code says which, so
        // it is read rather than assumed.
        if (root.Value.Bool("success") != true || root.Value.Int("code") != 200)
            return ProviderUsage.Failed(account, Problem(root.Value));

        var data = root.Value.Obj("data");
        var windows = Windows(data?.Items("limits") ?? Enumerable.Empty<JsonElement>(), provider);

        if (windows.Count == 0) return ProviderUsage.Failed(account, Unavailability.NoLimitsReported);

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            ObservedAt = DateTimeOffset.Now,
            Plan = data is null ? null : PlanLabel(data.Value),
            CreditBalance = null,
        };
    }

    /// <summary>
    /// What the envelope's refusal actually was.
    ///
    /// <b>The keywords used to be English only, and the mainland host answers in
    /// Chinese</b>, so nothing matched and everything fell through to the code —
    /// which only knew HTTP's numbers. Measured against the live endpoint: a key of
    /// the wrong shape gets <c>401 令牌已过期或验证不正确</c>, a well-formed key
    /// the host does not recognise gets <c>1000 身份验证失败。</c>, and a missing
    /// header gets <c>1001</c>. Only the first was mapped, so the common case — a
    /// key from the <i>other region</i>, which is the right shape and the wrong
    /// account — was reported as "the service returned an error" and sent people
    /// looking for an outage.
    /// </summary>
    internal static Unavailability Problem(JsonElement root)
    {
        var said = (root.Str("msg") ?? "").ToLowerInvariant();

        // **Checked before anything else, because the code is useless here.** A
        // working key on an account with no running subscription answers `500` —
        // the vendor's generic number — with this sentence, and 500 alone would say
        // the service broke. The phrase is embedded in English on both hosts, so
        // one test covers both wordings.
        if (said.Contains("coding plan")) return Unavailability.ZaiNoCodingPlan;

        string[] authWords =
        [
            "token", "auth", "key", "unauthor", "forbidden", "credential",
            // The same sentences from the mainland host. Matched as text because
            // the code list cannot be complete: this is one vendor's private
            // numbering and it is not published in full.
            "身份验证", "鉴权", "认证", "令牌", "未授权", "无权限", "密钥",
        ];
        if (authWords.Any(said.Contains)) return Unavailability.ApiKeyRefused;

        return root.Int("code") switch
        {
            // HTTP's numbers, which this envelope also uses.
            401 or 403 => Unavailability.ApiKeyRefused,
            429 => Unavailability.RateLimited,
            // Zhipu's own 1000-series is authentication. 1000 and 1001 are
            // measured; the rest of the band is documented as the same family, and
            // sending somebody to check their key is the better mistake — the
            // alternative reported a working service as broken.
            >= 1000 and <= 1099 => Unavailability.ApiKeyRefused,
            _ => Unavailability.ServerError,
        };
    }

    /// <summary>The plan's name, under whichever of five keys this account's tier happens to use.</summary>
    private static string? PlanLabel(JsonElement data)
    {
        string?[] candidates =
        [
            data.Text("planName"), data.Text("plan"), data.Text("plan_type"),
            data.Text("packageName"), data.Text("level"),
        ];
        return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c));
    }

    /// <summary>
    /// Internal so the mapping can be held against captured JSON: the field names
    /// are undocumented and the arithmetic below is the whole feature.
    /// </summary>
    internal static IReadOnlyList<UsageWindow> Windows(IEnumerable<JsonElement> limits, Provider provider)
    {
        var windows = new List<UsageWindow>();
        var index = 0;

        foreach (var limit in limits)
        {
            var window = Window(limit, index, provider);
            if (window is not null) windows.Add(window);
            index++;
        }

        // Shortest first, so a five-hour limit is read before a weekly one.
        return windows.OrderBy(w => w.WindowSeconds).ToList();
    }

    private static UsageWindow? Window(JsonElement limit, int index, Provider provider)
    {
        // Only these three carry a quota. Anything else the service starts
        // reporting is left out rather than shown under a heading guessed at.
        var type = limit.Text("type");
        if (type is null || type is not ("TOKENS_LIMIT" or "CREDIT_LIMIT" or "TIME_LIMIT"))
            return null;

        var unit = limit.Int("unit");
        var number = limit.Int("number");
        if (unit is null || number is null) return null;

        var minutes = Minutes(unit.Value, number.Value, type);
        var used = UsedPercent(limit);
        if (minutes is null || used is null) return null;

        return new UsageWindow
        {
            // The position is in the id because two limits can share a type and a
            // duration. Ids are what a pinned window is matched on and the
            // identity the rows use, so a collision leaves rows undefined and a
            // pin unresolvable.
            Id = $"{provider.StableId()}.{type}.{unit}-{number}.{index}",
            Kind = KindForMinutes(minutes.Value),
            OtherSeconds = minutes.Value * 60,
            // The MCP lane is a different allowance from the coding quota, and
            // saying so is the only way two rows of the same length tell apart.
            Scope = type == "TIME_LIMIT" ? "MCP" : null,
            UsedFraction = used.Value / 100,
            WindowSeconds = minutes.Value * 60,
            // **Epoch milliseconds**, and only the weekly limit carries one.
            ResetsAt = limit.Num("nextResetTime") is { } stamp
                ? SafeFromUnixMilliseconds((long)stamp)
                : null,
        };
    }

    /// <summary>
    /// How long the window runs.
    ///
    /// The reply states a <c>unit</c> code and a <c>number</c> of them. An
    /// unrecognised unit means the length cannot be read, and a window with no
    /// length can be neither named nor sorted — so it is <b>dropped rather than
    /// given an invented one</b>.
    /// </summary>
    private static int? Minutes(int unit, int number, string type)
    {
        // A monthly MCP allowance is reported as "1 minute", which is a marker
        // rather than a duration — taken literally it would sort above a five-hour
        // limit and claim to reset every minute.
        if (type == "TIME_LIMIT" && unit == 5 && number == 1) return 30 * 24 * 60;

        var perUnit = new Dictionary<int, int> { [1] = 1440, [3] = 60, [5] = 1, [6] = 10080 };
        if (number <= 0 || !perUnit.TryGetValue(unit, out var multiplier)) return null;

        return number * multiplier;
    }

    private static WindowKind KindForMinutes(int minutes) => minutes switch
    {
        300 => WindowKind.FiveHour,
        10080 => WindowKind.Weekly,
        43200 => WindowKind.Monthly,
        _ => WindowKind.Other,
    };

    /// <summary>
    /// How much of the limit is gone, 0...100.
    ///
    /// <c>percentage</c> is what the service intends to be read, but it is a whole
    /// number — so a plan whose counts are also given is worked out from those
    /// instead, which is finer. <c>remaining</c> is what is <i>left</i>, so the
    /// spend is the difference; <c>currentValue</c> is the spend directly and wins
    /// when both are present, since it is the one the service is counting up.
    /// </summary>
    private static double? UsedPercent(JsonElement limit)
    {
        var usage = limit.Num("usage");
        if (usage is > 0)
        {
            var remaining = limit.Num("remaining");
            var current = limit.Num("currentValue");

            double? used = null;
            if (remaining is not null)
                used = Math.Max(usage.Value - remaining.Value, current ?? (usage.Value - remaining.Value));
            else if (current is not null)
                used = current;

            if (used is not null)
                return Math.Clamp(Math.Min(used.Value, usage.Value) / usage.Value * 100, 0, 100);
        }

        // **Null rather than zero.** A limit that arrives with no figure at all is
        // not a limit at 0% — it is a limit whose reading is missing, and drawing a
        // full green ring for an account that may be out of quota is the one thing
        // this app is not allowed to do.
        var percentage = limit.Num("percentage");
        return percentage is null ? null : Math.Clamp(percentage.Value, 0, 100);
    }

    private static DateTimeOffset? SafeFromUnixMilliseconds(long milliseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
