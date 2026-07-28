using System.Text.Json;

namespace SpecReport.Core.Parsing;

/// <summary>
/// Small readers over <see cref="JsonElement"/>.
///
/// The parser works on JsonElement rather than deserializing into DTOs so that unknown
/// properties and unknown envelope types are ignored for free - forward compatibility with
/// future Reqnroll releases is a requirement (SPEC §7.1), and a DTO layer would have to be
/// revised every time the schema grows a field.
/// </summary>
internal static class Json
{
    public static JsonElement? Prop(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : null;

    public static string? Str(this JsonElement element, string name)
    {
        var prop = element.Prop(name);
        return prop is { ValueKind: JsonValueKind.String } ? prop.Value.GetString() : null;
    }

    public static int? Int(this JsonElement element, string name)
    {
        var prop = element.Prop(name);
        return prop is { ValueKind: JsonValueKind.Number } && prop.Value.TryGetInt32(out var v)
            ? v
            : null;
    }

    public static bool Bool(this JsonElement element, string name)
    {
        var prop = element.Prop(name);
        return prop is { ValueKind: JsonValueKind.True };
    }

    public static IEnumerable<JsonElement> Array(this JsonElement element, string name)
    {
        var prop = element.Prop(name);
        return prop is { ValueKind: JsonValueKind.Array }
            ? prop.Value.EnumerateArray()
            : [];
    }

    public static IReadOnlyList<string> StringArray(this JsonElement element, string name) =>
        element.Array(name)
               .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
               .Where(s => s is not null)
               .Select(s => s!)
               .ToList();

    public static IReadOnlyDictionary<string, string> StringMap(this JsonElement element, string name)
    {
        var prop = element.Prop(name);
        if (prop is not { ValueKind: JsonValueKind.Object }) return new Dictionary<string, string>();

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in prop.Value.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String)
                map[p.Name] = p.Value.GetString() ?? string.Empty;
        }

        return map;
    }

    /// <summary>Cucumber timestamps are {seconds, nanos} since the epoch, in UTC.</summary>
    public static DateTimeOffset? Timestamp(this JsonElement element, string name)
    {
        var prop = element.Prop(name);
        if (prop is not { ValueKind: JsonValueKind.Object }) return null;

        var seconds = prop.Value.Int("seconds");
        if (seconds is null) return null;

        var nanos = prop.Value.Int("nanos") ?? 0;
        return DateTimeOffset.FromUnixTimeSeconds(seconds.Value)
                             .AddTicks(nanos / 100);
    }

    /// <summary>Cucumber durations use the same {seconds, nanos} shape.</summary>
    public static TimeSpan Duration(this JsonElement element, string name)
    {
        var prop = element.Prop(name);
        if (prop is not { ValueKind: JsonValueKind.Object }) return TimeSpan.Zero;

        var seconds = prop.Value.Int("seconds") ?? 0;
        var nanos = prop.Value.Int("nanos") ?? 0;
        return TimeSpan.FromSeconds(seconds) + TimeSpan.FromTicks(nanos / 100);
    }

    /// <summary>The single property name of an envelope, e.g. "testStepFinished".</summary>
    public static string? EnvelopeType(this JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in root.EnumerateObject())
            return property.Name;

        return null;
    }
}
