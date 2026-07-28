using System;
using System.Collections.Generic;
using System.Text;

namespace SpecReport.Capture;

/// <summary>
/// Capture-time redaction. Every method here returns the redacted value AND records what it
/// hid, because SPEC 5.2 requires the report to show that a value was withheld rather than
/// silently presenting an incomplete picture.
/// </summary>
internal static class Redaction
{
    public static bool IsSensitiveName(string name, CaptureOptions options)
    {
        if (options.RedactedHeaders.Contains(name)) return true;

        // Match on a normalised form so api_key, api-key, apiKey and API.KEY are all caught by
        // the single fragment "apikey". Naming conventions vary per API and a redaction rule
        // that only catches one spelling is a rule that leaks: `api_key` slipping past
        // "api-key" is exactly how the first version of this failed.
        var normalised = Normalise(name);

        foreach (var fragment in options.SensitiveNameFragments)
        {
            if (normalised.IndexOf(Normalise(fragment), StringComparison.Ordinal) >= 0)
                return true;
        }

        return false;
    }

    /// <summary>Lower-cases and strips separators so spelling variants collapse together.</summary>
    private static string Normalise(string value)
    {
        var sb = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Redacts sensitive query-string parameters in place, preserving the rest of the URL so the
    /// report still shows which endpoint was called. A SAS token or api_key in a query string is
    /// every bit as much a secret as one in a header, and it is the case people forget.
    /// </summary>
    public static string RedactUrl(string url, CaptureOptions options, ICollection<string> redactions)
    {
        var queryStart = url.IndexOf('?');
        if (queryStart < 0 || queryStart == url.Length - 1) return url;

        var prefix = url.Substring(0, queryStart);
        var query = url.Substring(queryStart + 1);

        var fragment = string.Empty;
        var hash = query.IndexOf('#');
        if (hash >= 0)
        {
            fragment = query.Substring(hash);
            query = query.Substring(0, hash);
        }

        var rebuilt = new StringBuilder(prefix).Append('?');
        var pairs = query.Split('&');

        for (var i = 0; i < pairs.Length; i++)
        {
            if (i > 0) rebuilt.Append('&');

            var pair = pairs[i];
            var eq = pair.IndexOf('=');

            if (eq <= 0)
            {
                rebuilt.Append(pair);
                continue;
            }

            var name = pair.Substring(0, eq);

            if (IsSensitiveName(name, options))
            {
                rebuilt.Append(name).Append('=').Append(Uri.EscapeDataString(options.RedactionPlaceholder));
                redactions.Add("request.query." + name);
            }
            else
            {
                rebuilt.Append(pair);
            }
        }

        return rebuilt.Append(fragment).ToString();
    }

    public static IDictionary<string, string> RedactHeaders(
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers,
        string side,
        CaptureOptions options,
        ICollection<string> redactions)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            if (IsSensitiveName(header.Key, options))
            {
                result[header.Key] = options.RedactionPlaceholder;
                redactions.Add(side + ".headers." + header.Key);
            }
            else
            {
                result[header.Key] = string.Join(", ", header.Value);
            }
        }

        return result;
    }
}
