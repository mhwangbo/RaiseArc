using System;
using UnityEngine;

namespace PrincessStudio.Unity
{
    // JsonUtility defaults missing fields and coerces numeric strings; inspect root tokens first.
    internal static class JsonRootFields
    {
        [Serializable] private sealed class Key { public string value; }
        internal static bool Contains(string json, string field) => ValueStart(json, field) >= 0;
        internal static bool HasIntegerValue(string json, string field, int value)
        {
            var start = ValueStart(json, field);
            if (start < 0) return false;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            var token = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var end = start + token.Length;
            return end < json.Length && string.CompareOrdinal(json, start, token, 0, token.Length) == 0
                && (char.IsWhiteSpace(json[end]) || json[end] == ',' || json[end] == '}');
        }
        private static int ValueStart(string json, string field)
        {
            var depth = 0;
            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '{' || c == '[') { depth++; continue; }
                if (c == '}' || c == ']') { depth--; continue; }
                if (c != '"') continue;
                var start = i++;
                for (; i < json.Length; i++)
                {
                    if (json[i] == '\\') { i++; continue; }
                    if (json[i] == '"') break;
                }
                var end = i + 1;
                while (end < json.Length && char.IsWhiteSpace(json[end])) end++;
                if (depth == 1 && end < json.Length && json[end] == ':')
                {
                    var key = JsonUtility.FromJson<Key>("{\"value\":" + json.Substring(start, i - start + 1) + "}");
                    if (key.value == field) return end + 1;
                }
            }
            return -1;
        }
    }
}
