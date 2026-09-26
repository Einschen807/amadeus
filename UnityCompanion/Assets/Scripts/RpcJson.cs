using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KurisuCompanion
{
    // Small strict JSON codec for the heterogeneous JSON-RPC protocol (JsonUtility cannot read dictionaries).
    internal static class RpcJson
    {
        public static Dictionary<string, object> Obj(params object[] pairs)
        {
            var result = new Dictionary<string, object>();
            for (int i = 0; i < pairs.Length; i += 2) result[(string)pairs[i]] = pairs[i + 1];
            return result;
        }
        public static object Get(object obj, string key) => obj is Dictionary<string, object> map && map.TryGetValue(key, out var value) ? value : null;
        public static string Str(object obj, string key) => Get(obj, key) as string ?? "";
        public static bool Bool(object obj, string key) => Get(obj, key) is bool b && b;
        public static string Encode(object value)
        {
            var b = new StringBuilder(); Write(b, value); return b.ToString();
        }
        private static void Write(StringBuilder b, object value)
        {
            if (value == null) { b.Append("null"); return; }
            if (value is string text) { AgentClient.Quote(b, text); return; }
            if (value is bool flag) { b.Append(flag ? "true" : "false"); return; }
            if (value is IDictionary map)
            {
                b.Append('{'); bool first = true;
                foreach (DictionaryEntry pair in map)
                {
                    if (!first) b.Append(','); first = false;
                    Write(b, (string)pair.Key); b.Append(':'); Write(b, pair.Value);
                }
                b.Append('}'); return;
            }
            if (value is IEnumerable list)
            {
                b.Append('['); bool first = true;
                foreach (var item in list) { if (!first) b.Append(','); first = false; Write(b, item); }
                b.Append(']'); return;
            }
            if (value is IFormattable number) { b.Append(number.ToString(null, CultureInfo.InvariantCulture)); return; }
            throw new FormatException("Unsupported JSON value");
        }
        public static object Decode(string text)
        {
            var p = new Parser(text); var value = p.Value(0); p.Space();
            if (p.Position != text.Length) throw new FormatException("Trailing JSON data");
            return value;
        }
        private sealed class Parser
        {
            private readonly string text;
            public int Position;
            public Parser(string value) { text = value; }
            public void Space() { while (Position < text.Length && char.IsWhiteSpace(text[Position])) Position++; }
            private char Take() { if (Position >= text.Length) throw new FormatException("Incomplete JSON"); return text[Position++]; }
            private void Expect(char c) { Space(); if (Take() != c) throw new FormatException("Invalid JSON delimiter"); }
            public object Value(int depth)
            {
                if (depth > 100) throw new FormatException("JSON too deep");
                Space(); if (Position >= text.Length) throw new FormatException("Empty JSON");
                char c = text[Position];
                if (c == '"') return String();
                if (c == '{')
                {
                    Position++; var map = new Dictionary<string, object>(); Space();
                    if (Position < text.Length && text[Position] == '}') { Position++; return map; }
                    while (true)
                    {
                        Space(); string key = String(); Expect(':'); map[key] = Value(depth + 1); Space();
                        c = Take(); if (c == '}') return map; if (c != ',') throw new FormatException("Invalid JSON object");
                    }
                }
                if (c == '[')
                {
                    Position++; var list = new List<object>(); Space();
                    if (Position < text.Length && text[Position] == ']') { Position++; return list; }
                    while (true)
                    {
                        list.Add(Value(depth + 1)); Space(); c = Take();
                        if (c == ']') return list; if (c != ',') throw new FormatException("Invalid JSON array");
                    }
                }
                int start = Position;
                while (Position < text.Length && !char.IsWhiteSpace(text[Position]) && ",]}".IndexOf(text[Position]) < 0) Position++;
                string token = text.Substring(start, Position - start);
                if (token == "true") return true; if (token == "false") return false; if (token == "null") return null;
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer)) return integer;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && !double.IsInfinity(number) && !double.IsNaN(number)) return number;
                throw new FormatException("Invalid JSON value");
            }
            private string String()
            {
                if (Take() != '"') throw new FormatException("Expected JSON string");
                var b = new StringBuilder();
                while (true)
                {
                    char c = Take(); if (c == '"') return b.ToString();
                    if (c < 32) throw new FormatException("Invalid JSON character");
                    if (c != '\\') { b.Append(c); continue; }
                    c = Take();
                    switch (c)
                    {
                        case '"': case '\\': case '/': b.Append(c); break;
                        case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break;
                        case 'n': b.Append('\n'); break; case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break;
                        case 'u':
                            if (Position + 4 > text.Length) throw new FormatException("Incomplete Unicode escape");
                            b.Append((char)int.Parse(text.Substring(Position, 4), NumberStyles.HexNumber)); Position += 4; break;
                        default: throw new FormatException("Invalid JSON escape");
                    }
                }
            }
        }
    }
}
