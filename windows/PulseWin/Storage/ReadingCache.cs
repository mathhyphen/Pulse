using System.Text.Json;
using PulseWin.Core;

namespace PulseWin.Storage;

/// <summary>
/// The last reading each account gave, kept across launches.
/// </summary>
/// <remarks>
/// <para>
/// Without this, a launch is blind until the first refresh comes back — and if that
/// refresh fails, it is blind until the <i>next</i> one, which on a long interval is
/// an hour. The rail then shows four rows with nothing in them, which reads as the
/// app being broken rather than as one fetch having stumbled. The reading on disk is
/// stale and is drawn as stale, but stale figures the provider did report beat no
/// figures at all.
/// </para>
/// <para>
/// Upstream calls the equivalent reconciliation and does the same thing; this port
/// referenced it in a comment long before it implemented it.
/// </para>
/// </remarks>
public static class ReadingCache
{
    /// <summary>
    /// Its own serializer settings, not the shared ones.
    /// </summary>
    /// <remarks>
    /// <b><c>IgnoreReadOnlyProperties</c> is the whole reason this is separate.</b> A
    /// reading carries several computed members — <c>IsLive</c>, <c>Fullest</c>,
    /// <c>RailMoney</c>, a window's localised <c>Name</c> — and none of them belong on
    /// disk: they are derived, and the name would freeze in whichever language was
    /// current when the file was written. This skips every get-only property at once
    /// rather than asking each one to remember an attribute.
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        IgnoreReadOnlyProperties = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static readonly object Gate = new();

    public static Dictionary<string, ProviderUsage> Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(AppPaths.Cache)) return new Dictionary<string, ProviderUsage>();

                var loaded = JsonSerializer.Deserialize<Dictionary<string, ProviderUsage>>(
                    File.ReadAllText(AppPaths.Cache), Options);

                // A cache that will not decode is a cache, not a config file: throwing
                // it away costs one blind launch, which is what not having one costs
                // anyway.
                return loaded ?? new Dictionary<string, ProviderUsage>();
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                return new Dictionary<string, ProviderUsage>();
            }
        }
    }

    public static void Save(IReadOnlyDictionary<string, ProviderUsage> readings)
    {
        lock (Gate)
        {
            try
            {
                AppPaths.WriteAtomic(AppPaths.Cache, JsonSerializer.Serialize(readings, Options));
            }
            catch (IOException)
            {
            }
        }
    }
}
