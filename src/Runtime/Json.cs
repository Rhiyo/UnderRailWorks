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
        if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
        if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
        if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
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
}
}
