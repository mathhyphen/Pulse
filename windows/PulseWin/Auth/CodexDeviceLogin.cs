using System.Windows;
using PulseWin.Core;

namespace PulseWin.Auth;

/// <summary>What to put on screen while the reader goes and approves the sign-in.</summary>
public sealed record DevicePrompt(
    string UserCode, string VerificationUrl, int IntervalSeconds, DateTimeOffset ExpiresAt);

/// <summary>
/// Signing in to a second (or third) Codex account.
///
/// <para>
/// <b>This is OpenAI's device-code shape, which is not RFC 8628's.</b> They share a
/// name and almost nothing else: this one takes JSON where the specification takes
/// a form, and it reports "still waiting" with <b>403 and 404</b> where the
/// specification uses <c>authorization_pending</c> in a 400. Upstream says it
/// plainly — writing either as a special case of the other gives a parser that
/// reads neither reliably.
/// </para>
/// <para>
/// The sequence:
/// </para>
/// <list type="number">
/// <item><c>POST /api/accounts/deviceauth/usercode</c> → a short code and a
/// <c>device_auth_id</c>.</item>
/// <item>The reader types the code at <c>auth.openai.com/codex/device</c>.</item>
/// <item>Poll <c>POST /api/accounts/deviceauth/token</c> until it answers.</item>
/// <item>Exchange what came back at <c>/oauth/token</c>.</item>
/// </list>
/// <para>
/// The proof key is <b>the provider's own</b> — it generates the verifier and hands
/// both halves back with the authorization code — which is why the redirect address
/// is one of theirs and nothing is redirected to this machine. There is no local
/// port to collide with the CLI's own sign-in.
/// </para>
/// <para>
/// The redirect flow was not attempted. Upstream matched it field for field to the
/// published client and still ended on OpenAI's hosted error page before the browser
/// came back, twice.
/// </para>
/// </summary>
public static class CodexDeviceLogin
{
    // A public client id: it ships in every copy of the Codex CLI, and upstream's
    // rule for this directory is that client ids are not secrets. What is not
    // written down anywhere is a token.
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";

    private const string Origin = "https://auth.openai.com";
    private const string DeviceBase = Origin + "/api/accounts/deviceauth";
    private const string TokenUrl = Origin + "/oauth/token";

    /// <summary>The page the reader types the code into.</summary>
    public const string VerificationPage = Origin + "/codex/device";

    /// <summary>
    /// The full published set.
    /// </summary>
    /// <remarks>
    /// **Not narrowed deliberately.** Asking for less looked like good practice and
    /// is not on offer: upstream tried a subset and the sign-in ended on OpenAI's
    /// error page before the browser came back. Taken from
    /// <c>codex-rs/login/src/server.rs</c>, and read from the CLI's source rather
    /// than inferred from a binary's strings — which is how upstream got this wrong
    /// the first two times.
    /// </remarks>
    private static readonly string[] Scopes =
    [
        "openid", "profile", "email", "offline_access",
        "api.connectors.read", "api.connectors.invoke",
    ];

    /// <summary>How long a code is worth waiting on. GitHub's are fifteen minutes; this is the same patience.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(15);

    public static async Task<(DevicePrompt? Prompt, string? Error)> RequestCodeAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await Http.PostJsonAsync(
            DeviceBase + "/usercode",
            $$"""{"client_id":"{{ClientId}}"}""",
            30,
            cancellationToken: cancellationToken);

        if (result is null) return (null, "无法连接到 OpenAI。");

        if (!result.Value.IsSuccess)
            return (null, $"请求设备码失败：HTTP {(int)result.Value.Status} {Shorten(result.Value.Body)}");

        var root = Json.TryParse(result.Value.Body);
        if (root is null) return (null, "设备码接口返回了无法解析的内容。");

        // The field is spelled both ways in the wild.
        var code = root.Value.Text("user_code") ?? root.Value.Text("usercode");
        var id = root.Value.Text("device_auth_id");
        if (code is null || id is null) return (null, "设备码接口没有返回 code 或 device_auth_id。");

        // The reply states how often to ask; a provider that says nothing gets the
        // five seconds its own client falls back to. **It arrives as a string** —
        // `"interval": "5"` — which is why the number reader accepts both.
        var seconds = (int)Math.Max(root.Value.Num("interval") ?? 5, 1);

        // And it states when the code dies. Measured: `expires_at` is present and
        // absolute. A fixed fifteen-minute guess would keep polling a code the
        // service has already thrown away, and report the failure as the reader's.
        var expires = DateTimeOffset.TryParse(
            root.Value.Text("expires_at"), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var stated)
            ? stated
            : DateTimeOffset.Now + Deadline;

        _pendingId = id;
        return (new DevicePrompt(code, VerificationPage, seconds, expires), null);
    }

    private static string? _pendingId;

    /// <summary>
    /// Waits for the reader to approve, then turns what comes back into tokens.
    /// </summary>
    /// <remarks>
    /// Returns null while it is still waiting, so the caller can keep the dialog
    /// responsive rather than blocking on a fifteen-minute loop.
    /// </remarks>
    public static async Task<(AccountCredentials? Credentials, string? Error, bool Waiting)> PollOnceAsync(
        DevicePrompt prompt, CancellationToken cancellationToken = default)
    {
        var id = _pendingId;
        if (id is null) return (null, "设备码会话已失效，请重新开始。", false);

        var body = $$"""{"device_auth_id":"{{id}}","user_code":"{{prompt.UserCode}}"}""";

        var result = await Http.PostJsonAsync(
            DeviceBase + "/token", body, 30, cancellationToken: cancellationToken);

        if (result is null) return (null, null, true);

        // 403 and 404 both mean "still waiting" — this provider's convention, not
        // the specification's `authorization_pending`.
        var status = (int)result.Value.Status;
        if (status is 403 or 404) return (null, null, true);

        if (status != 200)
            return (null, $"轮询失败：HTTP {status} {Shorten(result.Value.Body)}", false);

        var root = Json.TryParse(result.Value.Body);
        var code = root?.Text("authorization_code");
        var verifier = root?.Text("code_verifier");
        if (code is null || verifier is null)
            return (null, "设备码接口返回的内容里没有 authorization_code 或 code_verifier。", false);

        var exchanged = await ExchangeAsync(code, verifier, cancellationToken);
        return (exchanged.Credentials, exchanged.Error, false);
    }

    /// <summary>The full deadline, so the caller knows how long to keep polling.</summary>
    public static TimeSpan PollDeadline => Deadline;

    private static async Task<(AccountCredentials? Credentials, string? Error)> ExchangeAsync(
        string code, string verifier, CancellationToken cancellationToken)
    {
        // **Four fields, and not `state`.** Anthropic's exchange takes a state;
        // this one does not, and sending one is not harmless.
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            // One of *theirs*: the provider generated the proof key for this flow.
            ["redirect_uri"] = Origin + "/deviceauth/callback",
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        };

        var result = await Http.PostFormAsync(TokenUrl, fields, 30, cancellationToken);
        if (result is null) return (null, "换取令牌时无法连接到 OpenAI。");

        if (!result.Value.IsSuccess)
            return (null, $"换取令牌失败：HTTP {(int)result.Value.Status} {Shorten(result.Value.Body)}");

        return Read(result.Value.Body);
    }

    /// <summary>
    /// Renews an access token, which is the reason this app holds a refresh token at
    /// all rather than borrowing the CLI's.
    /// </summary>
    public static async Task<(AccountCredentials? Credentials, string? Error)> RenewAsync(
        AccountCredentials existing, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = existing.RefreshToken,
            ["client_id"] = ClientId,
            ["scope"] = string.Join(" ", Scopes),
        };

        var result = await Http.PostFormAsync(TokenUrl, fields, 30, cancellationToken);
        if (result is null) return (null, "续期时无法连接到 OpenAI。");

        if (!result.Value.IsSuccess)
        {
            var status = (int)result.Value.Status;
            // A refused refresh is a signed-out account, not a network problem: the
            // remedy is the same and the reader can act on it.
            return (null, status is 400 or 401 or 403
                ? "登录已失效，需要重新登录。"
                : $"续期失败：HTTP {status} {Shorten(result.Value.Body)}");
        }

        var (credentials, error) = Read(result.Value.Body, existing);
        return (credentials, error);
    }

    private static (AccountCredentials? Credentials, string? Error) Read(
        string body, AccountCredentials? previous = null)
    {
        var root = Json.TryParse(body);
        if (root is null) return (null, "令牌接口返回了无法解析的内容。");

        var access = root.Value.Text("access_token");
        if (access is null) return (null, "令牌接口没有返回 access_token。");

        var reply = root.Value;

        // A refresh reply may omit the refresh token, in which case the one already
        // held is still the right one. Replacing it with an empty string would sign
        // the account out at the next renewal.
        var refresh = reply.Text("refresh_token");
        if (string.IsNullOrEmpty(refresh)) refresh = previous?.RefreshToken;
        if (string.IsNullOrEmpty(refresh)) return (null, "令牌接口没有返回 refresh_token。");

        var expiresIn = reply.Num("expires_in") ?? 3600;

        return (new AccountCredentials
        {
            AccessToken = access,
            RefreshToken = refresh,
            ExpiresAt = DateTimeOffset.Now.AddSeconds(expiresIn),

            // **The account id comes from the access token**, in a namespaced claim
            // rather than at the top level, and only the access token carries it.
            // Read with `Field` rather than an indexer: `JsonElement`'s indexer takes
            // an int, because it is the array one, and an object member will not
            // resolve through it.
            ServiceAccountId =
                Jwt.Claims(access)?.Field("https://api.openai.com/auth") is { } auth
                    ? auth.Text("chatgpt_account_id")
                    : null,

            // **The name comes from the id token**, so two subscriptions are not
            // both offered to the reader as "Codex".
            Email = Jwt.Claim(reply.Text("id_token"), "email") ?? previous?.Email,
        }, null);
    }

    /// <summary>The first line of a reply, so an error message is not a wall of HTML.</summary>
    /// <remarks>
    /// Named <c>Shorten</c> rather than <c>Trim</c> because a static method by that
    /// name shadows <see cref="string.Trim()"/> for every other call in this class.
    /// </remarks>
    private static string Shorten(string body)
    {
        var text = body.Trim();
        if (text.Length == 0) return "";

        var newline = text.IndexOfAny(['\r', '\n']);
        if (newline > 0) text = text[..newline];
        return text.Length <= 160 ? text : text[..160] + "…";
    }

    /// <summary>Opens the page the code goes into. The link carries no code of its own.</summary>
    public static void OpenVerificationPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = VerificationPage,
                UseShellExecute = true,
            });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser is not a failure of the sign-in: the code and the address
            // are on screen, and the reader can type both.
        }
    }

    /// <summary>Puts the code on the clipboard, because typing eight characters is the step people fumble.</summary>
    public static void CopyCode(string code)
    {
        try
        {
            Clipboard.SetText(code);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The clipboard is a shared resource and can be held by another process.
        }
    }
}

