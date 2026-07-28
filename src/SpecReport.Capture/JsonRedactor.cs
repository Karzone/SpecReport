using System.Collections.Generic;
using System.Text;

namespace SpecReport.Capture;

/// <summary>
/// Redacts sensitive values inside a JSON body, at any nesting depth.
///
/// Hand-written because System.Text.Json is a NuGet package on netstandard2.0 and SPEC §3.5
/// forbids Capture any dependency beyond Reqnroll - a serializer version flowing into every
/// consumer's test project is exactly what that rule prevents.
///
/// It is a SCANNER, not a parser: it walks the text tracking string boundaries and escapes,
/// and rewrites the value that follows a sensitive key. That is deliberate -
///   * it preserves the body's original formatting, so what the report shows is what was sent;
///   * malformed JSON degrades to "left alone" rather than throwing mid-request.
///
/// It fails toward over-redaction: when a key looks sensitive, the whole value goes, object or
/// array included. Showing too little is recoverable; leaking a token into an archived CI
/// artifact is not.
/// </summary>
internal static class JsonRedactor
{
    /// <summary>
    /// Returns the body with sensitive values replaced, and reports the JSON paths it hid so
    /// the report can show WHAT was withheld rather than merely that something was (SPEC §5.2).
    /// </summary>
    public static string Redact(
        string json, string side, CaptureOptions options, ICollection<string> redactions)
    {
        if (string.IsNullOrEmpty(json)) return json;

        var output = new StringBuilder(json.Length);
        var path = new List<string>();
        var i = 0;
        var redactedAny = false;

        while (i < json.Length)
        {
            var c = json[i];

            if (c == '"')
            {
                var start = i;
                var end = SkipString(json, i);
                if (end < 0) { output.Append(json, i, json.Length - i); break; }

                var token = json.Substring(start, end - start);

                // A string followed by ':' is a key.
                var afterKey = SkipWhitespace(json, end);
                if (afterKey < json.Length && json[afterKey] == ':')
                {
                    var name = Unquote(token);
                    output.Append(token);
                    output.Append(json, end, afterKey - end + 1);

                    var valueStart = SkipWhitespace(json, afterKey + 1);

                    if (Redaction.IsSensitiveName(name, options))
                    {
                        var valueEnd = SkipValue(json, valueStart);
                        if (valueEnd < 0) { output.Append(json, valueStart, json.Length - valueStart); break; }

                        output.Append(json, afterKey + 1, valueStart - (afterKey + 1));
                        output.Append('"').Append(Escape(options.RedactionPlaceholder)).Append('"');

                        redactions.Add(side + ".body." + JoinPath(path, name));
                        redactedAny = true;
                        i = valueEnd;
                        continue;
                    }

                    // Track the key so nested redactions can report a full path.
                    path.Add(name);
                    output.Append(json, afterKey + 1, valueStart - (afterKey + 1));

                    // A scalar value closes this key immediately; objects/arrays keep it open
                    // until their closing brace, handled below.
                    if (valueStart < json.Length && json[valueStart] != '{' && json[valueStart] != '[')
                    {
                        var valueEnd = SkipValue(json, valueStart);
                        if (valueEnd < 0) { output.Append(json, valueStart, json.Length - valueStart); break; }

                        output.Append(json, valueStart, valueEnd - valueStart);
                        path.RemoveAt(path.Count - 1);
                        i = valueEnd;
                        continue;
                    }

                    i = valueStart;
                    continue;
                }

                output.Append(token);
                i = end;
                continue;
            }

            if (c == '}' || c == ']')
            {
                if (path.Count > 0) path.RemoveAt(path.Count - 1);
            }

            output.Append(c);
            i++;
        }

        _ = redactedAny;
        return output.ToString();
    }

    private static string JoinPath(List<string> path, string name) =>
        path.Count == 0 ? name : string.Join(".", path) + "." + name;

    /// <summary>Index just past the closing quote, or -1 if the string never closes.</summary>
    private static int SkipString(string json, int start)
    {
        var i = start + 1;

        while (i < json.Length)
        {
            var c = json[i];
            if (c == '\\') { i += 2; continue; }   // escaped char - including \" and \\
            if (c == '"') return i + 1;
            i++;
        }

        return -1;
    }

    private static int SkipWhitespace(string json, int i)
    {
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        return i;
    }

    /// <summary>Index just past a complete value: scalar, string, object or array.</summary>
    private static int SkipValue(string json, int start)
    {
        if (start >= json.Length) return -1;

        var c = json[start];

        if (c == '"') return SkipString(json, start);

        if (c == '{' || c == '[')
        {
            var depth = 0;
            var i = start;

            while (i < json.Length)
            {
                var ch = json[i];

                if (ch == '"')
                {
                    var end = SkipString(json, i);
                    if (end < 0) return -1;
                    i = end;
                    continue;
                }

                if (ch == '{' || ch == '[') depth++;
                else if (ch == '}' || ch == ']')
                {
                    depth--;
                    if (depth == 0) return i + 1;
                }

                i++;
            }

            return -1;
        }

        // Number, true, false, null - runs until a structural character.
        var j = start;
        while (j < json.Length && json[j] != ',' && json[j] != '}' && json[j] != ']' &&
               !char.IsWhiteSpace(json[j]))
        {
            j++;
        }

        return j > start ? j : -1;
    }

    private static string Unquote(string token)
    {
        if (token.Length < 2) return token;
        var inner = token.Substring(1, token.Length - 2);
        return inner.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
