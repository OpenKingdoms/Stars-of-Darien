// JsonText.cs - writes what MiniJson reads back: dictionaries, lists,
// numbers, strings, booleans and null. Whole numbers are written without a
// fraction, so glTF's integer fields stay integers.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OpenKingdomsUnity.Studio
{
    public static class JsonText
    {
        public static string Write(object value, bool pretty = false)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty ? 0 : -1);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, int indent)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: Str(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: Num(sb, d); break;
                case float f: Num(sb, f); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> o:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in o)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Line(sb, indent + 1);
                        Str(sb, kv.Key);
                        sb.Append(indent >= 0 ? ": " : ":");
                        Write(sb, kv.Value, indent >= 0 ? indent + 1 : -1);
                    }
                    if (!first) Line(sb, indent);
                    sb.Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) sb.Append(indent >= 0 ? ", " : ",");
                        firstItem = false;
                        Write(sb, item, indent);
                    }
                    sb.Append(']');
                    break;
                default: throw new ArgumentException("JsonText cannot write " + v.GetType().Name);
            }
        }

        static void Line(StringBuilder sb, int indent)
        {
            if (indent < 0) return;
            sb.Append('\n').Append(' ', indent * 2);
        }

        static void Num(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append('0'); return; }
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void Str(StringBuilder sb, string s)
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
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
