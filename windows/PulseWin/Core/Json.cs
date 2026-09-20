using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseWin.Core;

public static class Json
{
    /// <summary>
    /// Reads and writes property names as written in the source, so the on-disk
    /// shape matches the property names a reader can see. Provider identifiers
    /// are serialized as their enum names.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Parses without throwing, so a service that answers in an unexpected shape
    /// becomes "unreadable reply" rather than an unhandled exception on the
    /// refresh loop.
    /// </summary>
    public static JsonElement? TryParse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            // Cloned, because the document is disposed when this returns.
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
