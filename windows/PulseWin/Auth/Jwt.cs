using System.Text;
using System.Text.Json;
using PulseWin.Core;

namespace PulseWin.Auth;

/// <summary>
/// Reads the claims out of a JWT.
/// </summary>
/// <remarks>
/// Codex hands back an <c>id_token</c> carrying the account's email and an access
/// token carrying the account id in a namespaced claim — <b>only the access token
/// has it</b>, and it is nested rather than at the top level. Both are read here
/// rather than inferred, because getting either wrong means two subscriptions are
/// both offered to the reader as "Codex", which is the one thing extra accounts
/// exist to prevent.
/// <para>
/// Nothing here verifies a signature. These tokens came back over TLS from the
/// issuer we just spoke to, and are being read for a label and a header value, not
/// for authorisation.
/// </para>
/// </remarks>
public static class Jwt
{
    /// <summary>The second segment, decoded, or null when the token is not a JWT.</summary>
    public static JsonElement? Claims(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;

        var parts = token.Split('.');
        if (parts.Length != 3) return null;

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload += new string('=', (4 - payload.Length % 4) % 4);

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (Exception e) when (e is FormatException or JsonException or DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>A top-level string claim.</summary>
    public static string? Claim(string? token, string name) =>
        Claims(token)?.Str(name);
}
