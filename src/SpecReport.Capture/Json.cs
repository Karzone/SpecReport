using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpecReport.Capture;

/// <summary>
/// A deliberately tiny JSON writer.
///
/// System.Text.Json is a NuGet package on netstandard2.0, and SPEC 3.5 forbids Capture any
/// dependency beyond Reqnroll and the BCL - a serializer version flowing into every consumer's
/// test project is exactly the kind of imposition that rule exists to prevent. The payload
/// shape is small and fixed, so hand-writing it costs less than the dependency would.
/// </summary>
internal sealed class JsonBuilder
{
    private readonly StringBuilder _sb = new StringBuilder();
    private bool _needsComma;

    public JsonBuilder Object()
    {
        Separate();
        _sb.Append('{');
        _needsComma = false;
        return this;
    }

    public JsonBuilder EndObject()
    {
        _sb.Append('}');
        _needsComma = true;
        return this;
    }

    public JsonBuilder Array(string name)
    {
        Property(name);
        _sb.Append('[');
        _needsComma = false;
        return this;
    }

    public JsonBuilder EndArray()
    {
        _sb.Append(']');
        _needsComma = true;
        return this;
    }

    public JsonBuilder ObjectMember(string name)
    {
        Property(name);
        _sb.Append('{');
        _needsComma = false;
        return this;
    }

    public JsonBuilder Str(string name, string? value)
    {
        if (value is null) return this;
        Property(name);
        WriteString(value);
        _needsComma = true;
        return this;
    }

    public JsonBuilder Num(string name, long value)
    {
        Property(name);
        _sb.Append(value.ToString(CultureInfo.InvariantCulture));
        _needsComma = true;
        return this;
    }

    public JsonBuilder Num(string name, double value)
    {
        Property(name);
        _sb.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
        _needsComma = true;
        return this;
    }

    public JsonBuilder Bool(string name, bool value)
    {
        Property(name);
        _sb.Append(value ? "true" : "false");
        _needsComma = true;
        return this;
    }

    public JsonBuilder StringArray(string name, IEnumerable<string> values)
    {
        Array(name);
        foreach (var v in values)
        {
            Separate();
            WriteString(v);
            _needsComma = true;
        }
        return EndArray();
    }

    /// <summary>Writes a string-to-string map as a JSON object.</summary>
    public JsonBuilder Map(string name, IEnumerable<KeyValuePair<string, string>> entries)
    {
        ObjectMember(name);
        foreach (var e in entries)
            Str(e.Key, e.Value);
        return EndObject();
    }

    private void Property(string name)
    {
        Separate();
        WriteString(name);
        _sb.Append(':');
    }

    private void Separate()
    {
        if (_needsComma) _sb.Append(',');
        _needsComma = false;
    }

    private void WriteString(string value)
    {
        _sb.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '"': _sb.Append("\\\""); break;
                case '\\': _sb.Append("\\\\"); break;
                case '\b': _sb.Append("\\b"); break;
                case '\f': _sb.Append("\\f"); break;
                case '\n': _sb.Append("\\n"); break;
                case '\r': _sb.Append("\\r"); break;
                case '\t': _sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                        _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        _sb.Append(c);
                    break;
            }
        }

        _sb.Append('"');
    }

    public override string ToString() => _sb.ToString();
}
