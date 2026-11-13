// A small JSON reader for Works' own readable files (mod.json manifests). Objects become
// Dictionary<string, object>, arrays List<object>, numbers double, plus string, bool and null.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnderRailWorks;

namespace UnderRailWorks.Internal
{

public static class Json
{
    public static object Parse(string text)
    {
        int i = 0;
        object v = Value(text, ref i);
        Space(text, ref i);
        if (i != text.Length) throw new FormatException("unexpected text after the JSON value at " + i);
        return v;
    }

    static void Space(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

    static object Value(string s, ref int i)
    {
        Space(s, ref i);
        if (i >= s.Length) throw new FormatException("unexpected end of JSON");
        char c = s[i];
        if (c == '{')
        {
            var o = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            i++; Space(s, ref i);
            if (s[i] == '}') { i++; return o; }
            while (true)
            {
                Space(s, ref i);
                string key = Str(s, ref i);
                Space(s, ref i);
                Expect(s, ref i, ':');
                o[key] = Value(s, ref i);
                Space(s, ref i);
                if (s[i] == ',') { i++; continue; }
                Expect(s, ref i, '}');
                return o;
            }
        }
        if (c == '[')
        {
            var a = new List<object>();
            i++; Space(s, ref i);
            if (s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Add(Value(s, ref i));
                Space(s, ref i);
                if (s[i] == ',') { i++; continue; }
                Expect(s, ref i, ']');
                return a;
            }
        }
        if (c == '"') return Str(s, ref i);
        if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
        if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
        if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
        int start = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        if (start == i) throw new FormatException("unexpected '" + c + "' at " + i);
        return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    static void Expect(string s, ref int i, char c)
    {
        if (i >= s.Length || s[i] != c) throw new FormatException("expected '" + c + "' at " + i);
        i++;
    }

    static string Str(string s, ref int i)
    {
        Expect(s, ref i, '"');
        var b = new StringBuilder();
        while (true)
        {
            if (i >= s.Length) throw new FormatException("unterminated string");
            char c = s[i++];
            if (c == '"') return b.ToString();
            if (c != '\\') { b.Append(c); continue; }
            char e = s[i++];
            switch (e)
            {
                case 'n': b.Append('\n'); break;
                case 't': b.Append('\t'); break;
                case 'r': b.Append('\r'); break;
                case 'b': b.Append('\b'); break;
                case 'f': b.Append('\f'); break;
                case 'u': b.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                default: b.Append(e); break;
            }
        }
    }

    // Writes the same shapes back: dictionaries, lists/arrays, strings, numbers, bools, null.
    public static string Write(object value, bool indent = true)
    {
        var sb = new StringBuilder();
        Write(sb, value, indent ? 0 : -1);
        return sb.ToString();
    }

    static void Write(StringBuilder sb, object v, int depth)
    {
        string nl = depth < 0 ? "" : "\n" + new string(' ', (depth + 1) * 2), end = depth < 0 ? "" : "\n" + new string(' ', depth * 2);
        int next = depth < 0 ? -1 : depth + 1;
        if (v == null) sb.Append("null");
        else if (v is string) Quote(sb, (string)v);
        else if (v is bool) sb.Append((bool)v ? "true" : "false");
        else if (v is double || v is float || v is int || v is long || v is decimal || v is short || v is byte || v is uint || v is ulong)
        {
            double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            // JSON has no NaN or infinity: written as null, so the file always reads back.
            sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture));
        }
        else if (v is System.Collections.IDictionary)
        {
            var d = (System.Collections.IDictionary)v; bool first = true;
            sb.Append('{');
            foreach (System.Collections.DictionaryEntry e in d) { if (!first) sb.Append(','); first = false; sb.Append(nl); Quote(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture)); sb.Append(depth < 0 ? ":" : ": "); Write(sb, e.Value, next); }
            if (!first) sb.Append(end);
            sb.Append('}');
        }
        else if (v is System.Collections.IEnumerable)
        {
            bool first = true;
            sb.Append('[');
            foreach (var e in (System.Collections.IEnumerable)v) { if (!first) sb.Append(','); first = false; sb.Append(nl); Write(sb, e, next); }
            if (!first) sb.Append(end);
            sb.Append(']');
        }
        else Quote(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
    }

    static void Quote(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            if (c == '"') sb.Append("\\\""); else if (c == '\\') sb.Append("\\\\");
            else if (c == '\n') sb.Append("\\n"); else if (c == '\r') sb.Append("\\r"); else if (c == '\t') sb.Append("\\t");
            else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
        }
        sb.Append('"');
    }
}
}
