using System.Globalization;
using System.Text.Json;

namespace PulseWin.Core;

/// <summary>
/// Reads a <see cref="JsonElement"/> without throwing on a missing, null or
/// wrong-typed member.
///
/// Every service here is reading an <b>undocumented</b> endpoint that can change
/// without notice, so a field that moved must degrade to "absent" rather than
/// taking the ring down with an exception. That is the rule the whole providers
/// directory is built on.
/// </summary>
public static class JsonElementExtensions
{
    public static JsonElement? Field(this JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        return element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
    }

    public static string? Str(this JsonElement element, string name)
    {
        var value = element.Field(name);
        return value?.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
    }

    public static bool? Bool(this JsonElement element, string name)
    {
        var value = element.Field(name);
        return value?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>
    /// A number, whether the service sent it as a number or as a string.
    ///
    /// Both happen. DeepSeek sends every figure as a string <i>including the
    /// money</i>; Zhipu sends real numbers but the parsing decodes them as double
    /// on purpose, so that a service which starts reporting <c>12.5</c> where it
    /// reported <c>12</c> does not fail the whole reply over a usable figure.
    /// </summary>
    public static double? Num(this JsonElement element, string name)
    {
        var value = element.Field(name);
        if (value is null) return null;

        return value.Value.ValueKind switch
        {
            JsonValueKind.Number => value.Value.TryGetDouble(out var number) ? number : null,
            JsonValueKind.String => double.TryParse(
                value.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null,
            _ => null,
        };
    }

    public static int? Int(this JsonElement element, string name)
    {
        var number = element.Num(name);
        return number is null ? null : (int)Math.Round(number.Value);
    }

    public static JsonElement? Obj(this JsonElement element, string name)
    {
        var value = element.Field(name);
        return value?.ValueKind == JsonValueKind.Object ? value : null;
    }

    /// <summary>An array member, treating a non-array as absent rather than throwing.</summary>
    public static IEnumerable<JsonElement> Items(this JsonElement? element)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in element.Value.EnumerateArray())
            yield return item;
    }

    public static IEnumerable<JsonElement> Items(this JsonElement element, string name) =>
        element.Field(name).Items();

    /// <summary>
    /// Money: a figure that is absent or unparseable is <b>absent, not zero</b>.
    ///
    /// This is the single most consequential rule in the file. A balance read as
    /// zero is a full red ring and a notification announcing an account as spent,
    /// about an account that is perfectly fine.
    /// </summary>
    public static double? Money(this JsonElement element, string name)
    {
        var text = element.Str(name);
        if (text is null) return null;

        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>A non-empty string, trimmed; an all-whitespace one counts as absent.</summary>
    public static string? Text(this JsonElement element, string name)
    {
        var text = element.Str(name)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
