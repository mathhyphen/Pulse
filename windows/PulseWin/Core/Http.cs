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
}
