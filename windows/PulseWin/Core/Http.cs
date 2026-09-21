using System.Net;
using System.Net.Http;

namespace PulseWin.Core;

/// <summary>What a request came back as, or that it did not come back at all.</summary>
public readonly record struct HttpResult(HttpStatusCode Status, string Body)
{
    public bool IsSuccess => (int)Status is >= 200 and < 300;
}

/// <summary>
/// The one place network calls are made.
///
/// Pulse routes every provider request through a single <c>NetworkSession</c> so
/// that the proxy setting, the timeout and the retry policy are decided once
/// rather than per service. This is the same arrangement.
/// </summary>
public static class Http
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            // The system proxy by default, which is what Pulse follows unless a
            // manual one is configured. Credentials of the *provider* are never
            // sent to a proxy it was not meant for, so the default credential
            // type is fine here.
            UseProxy = true,
            Proxy = WebRequest.DefaultWebProxy,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
        };

        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan, // per request, so each service states its own
        };
    }

    /// <summary>
    /// One GET with a bearer token.
    ///
    /// The header is set only when there is a token. An empty header is not the
    /// same as no header: it names no account, and the service is free to answer
    /// for whichever it likes — which, on an added Codex account, would put the
    /// <i>other</i> login's figures under this one's ring.
    /// </summary>
    public static async Task<HttpResult?> GetAsync(
        string url,
        string? bearer,
        int timeoutSeconds,
        IReadOnlyList<(string Name, string Value)>? extraHeaders = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(bearer))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        if (extraHeaders is not null)
        {
            foreach (var (name, value) in extraHeaders)
            {
                // An empty value is skipped rather than sent blank — see above.
                if (!string.IsNullOrEmpty(value))
                    request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            using var response = await Client.SendAsync(
                request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return new HttpResult(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The per-request deadline, not the caller giving up.
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether an exception looks like the connection tripping rather than
    /// something that will fail again the same way.
    ///
    /// A proxy or VPN dropping a connection usually clears on a retry. Pulse
    /// keeps this list explicitly rather than retrying everything, so a genuinely
    /// bad host does not cost three timeouts before the error surfaces.
    /// </summary>
    public static bool IsWorthRetrying(Exception error) => error switch
    {
        HttpRequestException => true,
        TaskCanceledException => true,
        System.Net.Sockets.SocketException => true,
        _ => false,
    };

    /// <summary>How many extra attempts a stumbling connection gets. Pulse's number.</summary>
    public const int RetryLimit = 2;

    /// <summary>The delay before attempt <paramref name="attempt"/> (0-based), in milliseconds.</summary>
    public static int RetryDelayMs(int attempt) => 600 * (attempt + 1);

    /// <summary>A POST with a JSON body, answered as a string.</summary>
    public static async Task<HttpResult?> PostJsonAsync(
        string url,
        string json,
        int timeoutSeconds,
        IReadOnlyList<(string Name, string Value)>? extraHeaders = null,
        CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await SendAsync(url, content, timeoutSeconds, extraHeaders, cancellationToken);
    }

    /// <summary>A POST with an <c>application/x-www-form-urlencoded</c> body.</summary>
    public static async Task<HttpResult?> PostFormAsync(
        string url,
        IEnumerable<KeyValuePair<string, string>> fields,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        var body = string.Join("&", fields.Select(f => $"{FormEncode(f.Key)}={FormEncode(f.Value)}"));
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        return await SendAsync(url, content, timeoutSeconds, null, cancellationToken);
    }

    /// <summary>
    /// Percent-encoding for a form body, which is <b>not</b> what a URL builder does.
    /// </summary>
    /// <remarks>
    /// Only the RFC 3986 unreserved set survives. A general-purpose encoder leaves
    /// <c>:</c> and <c>/</c> alone and passes a <c>+</c> through as a space, and on a
    /// token exchange that means the server receives a different string than the one
    /// that was signed — a failure that looks like the user's fault. Upstream was
    /// caught by exactly this.
    /// </remarks>
    public static string FormEncode(string value)
    {
        var encoded = new System.Text.StringBuilder(value.Length * 2);

        foreach (var b in System.Text.Encoding.UTF8.GetBytes(value))
        {
            var safe = b is >= (byte)'A' and <= (byte)'Z'
                       or >= (byte)'a' and <= (byte)'z'
                       or >= (byte)'0' and <= (byte)'9'
                       or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~';

            if (safe) encoded.Append((char)b);
            else encoded.Append('%').Append(b.ToString("X2"));
        }

        return encoded.ToString();
    }

    private static async Task<HttpResult?> SendAsync(
        string url,
        HttpContent content,
        int timeoutSeconds,
        IReadOnlyList<(string Name, string Value)>? extraHeaders,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        if (extraHeaders is not null)
        {
            foreach (var (name, value) in extraHeaders)
            {
                if (!string.IsNullOrEmpty(value))
                    request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            using var response = await Client.SendAsync(
                request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return new HttpResult(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
