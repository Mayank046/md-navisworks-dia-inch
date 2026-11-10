// MD Dia-Inch - tiny JSON reader/writer (no dependencies, works on .NET 4.6 - 4.8)
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DiaInch.Core
{
    /// <summary>Parses to Dictionary&lt;string,object&gt; (ordered by insertion), List&lt;object&gt;, string, double, bool, null.</summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            object v = Value(text ?? "", ref i);
            Ws(text, ref i);
            if (i < text.Length) throw Err(text, i, "extra text after JSON");
            return v;
        }

        private static FormatException Err(string s, int i, string what)
        {
            int line = 1, col = 1;
            for (int k = 0; k < i && k < s.Length; k++) { if (s[k] == '\n') { line++; col = 1; } else col++; }
            return new FormatException("JSON error at line " + line + ", column " + col + ": " + what);
        }

        private static void Ws(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                // allow // line comments (handy in a hand-edited rules file)
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                break;
            }
        }

        private static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw Err(s, i, "unexpected end");
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (Lit(s, ref i, "true")) return true;
            if (Lit(s, ref i, "false")) return false;
            if (Lit(s, ref i, "null")) return null;
            return Num(s, ref i);
        }

        private static bool Lit(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        private static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            i++;
            Ws(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                Ws(s, ref i);
                if (i >= s.Length || s[i] != '"') throw Err(s, i, "expected \"name\"");
                string k = Str(s, ref i);
                Ws(s, ref i);
                if (i >= s.Length || s[i] != ':') throw Err(s, i, "expected ':'");
                i++;
                d[k] = Value(s, ref i);
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; Ws(s, ref i); if (i < s.Length && s[i] == '}') { i++; return d; } continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw Err(s, i, "expected ',' or '}'");
            }
        }

        private static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            Ws(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Value(s, ref i));
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; Ws(s, ref i); if (i < s.Length && s[i] == ']') { i++; return l; } continue; }
                if (i < s.Length && s[i] == ']') { i++; return l; }
                throw Err(s, i, "expected ',' or ']'");
            }
        }

        private static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw Err(s, i, "bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw Err(s, i, "unterminated string");
        }

        private static double Num(string s, ref int i)
        {
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double d;
            if (i == st || !double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw Err(s, st, "bad value");
            return d;
        }

        // ---------- writer ----------
        public static string Write(object v)
        {
            var sb = new StringBuilder();
            W(sb, v, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static bool Simple(object v)
        {
            return v == null || v is string || v is bool || v is double || v is int || v is float || v is long || v is decimal;
        }

        private static void W(StringBuilder sb, object v, int ind)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { Q(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double || v is int || v is float || v is long || v is decimal)
            {
                sb.Append(Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as IEnumerable<KeyValuePair<string, object>>;
            if (dict != null)
            {
                sb.Append("{");
                bool first = true;
                foreach (var kv in dict)
                {
                    sb.Append(first ? "\n" : ",\n");
                    first = false;
                    sb.Append(' ', (ind + 1) * 2);
                    Q(sb, kv.Key);
                    sb.Append(": ");
                    W(sb, kv.Value, ind + 1);
                }
                if (!first) { sb.Append('\n').Append(' ', ind * 2); }
                sb.Append("}");
                return;
            }
            var list = v as System.Collections.IEnumerable;
            if (list != null)
            {
                var items = new List<object>();
                foreach (object o in list) items.Add(o);
                bool flat = items.TrueForAll(Simple);
                sb.Append("[");
                for (int k = 0; k < items.Count; k++)
                {
                    if (k > 0) sb.Append(flat ? ", " : ",");
                    if (!flat) sb.Append('\n').Append(' ', (ind + 1) * 2);
                    W(sb, items[k], ind + 1);
                }
                if (!flat && items.Count > 0) sb.Append('\n').Append(' ', ind * 2);
                sb.Append("]");
                return;
            }
            Q(sb, v.ToString());
        }

        private static void Q(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
