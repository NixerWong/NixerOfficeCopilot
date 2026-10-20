using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace OfficeCopilot.OpenXml.Core
{
    /// <summary>
    /// Key/value data for {{key}} template merging, parsed from a JSON object.
    /// Nested objects/arrays are flattened to their raw JSON text. Keys are
    /// case-insensitive.
    /// </summary>
    public sealed class MergeData
    {
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> Values => _values;

        public int Count => _values.Count;

        public void Set(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("key is required.", nameof(key));
            _values[key] = value ?? string.Empty;
        }

        public bool TryGet(string key, out string value)
        {
            return _values.TryGetValue(key, out value);
        }

        /// <summary>
        /// Parse a JSON object (or a path to a .json file) into merge data.
        /// Top-level literal keys win; nested objects/arrays are flattened to
        /// dot/bracket paths ("a.b", "items[0]") only when no literal sibling
        /// key already occupies that path.
        /// </summary>
        public static MergeData FromJson(string jsonOrFilePath)
        {
            if (string.IsNullOrWhiteSpace(jsonOrFilePath))
                throw new ArgumentException("jsonOrFilePath is required.", nameof(jsonOrFilePath));

            var json = jsonOrFilePath;
            if (!json.Trim().StartsWith("{") && File.Exists(jsonOrFilePath))
                json = File.ReadAllText(jsonOrFilePath);

            var root = JsonNode.Parse(json);
            if (root == null)
                throw new ArgumentException("Invalid JSON data: parsed to null.", nameof(jsonOrFilePath));
            if (!(root is JsonObject jsonObj))
                throw new ArgumentException("JSON data must be an object (not array or primitive).", nameof(jsonOrFilePath));

            var data = new MergeData();

            // Pass 1: literal top-level keys win.
            foreach (var kvp in jsonObj)
                data.Set(kvp.Key, kvp.Value == null ? string.Empty : kvp.Value.ToString());

            // Pass 2: flatten nested objects/arrays, never overwriting pass-1 keys.
            foreach (var kvp in jsonObj)
            {
                if (kvp.Value is JsonObject nested)
                    FlattenNested(nested, kvp.Key, data);
                else if (kvp.Value is JsonArray array)
                    FlattenArray(array, kvp.Key, data);
            }

            return data;
        }

        private static void FlattenNested(JsonObject obj, string prefix, MergeData data)
        {
            foreach (var kvp in obj)
            {
                var path = prefix + "." + kvp.Key;
                if (kvp.Value is JsonObject child)
                {
                    FlattenNested(child, path, data);
                    if (!data.Values.ContainsKey(path))
                        data.Set(path, kvp.Value.ToString());
                }
                else if (kvp.Value is JsonArray nestedArray)
                {
                    FlattenArray(nestedArray, path, data);
                }
                else if (!data.Values.ContainsKey(path))
                {
                    data.Set(path, kvp.Value == null ? string.Empty : kvp.Value.ToString());
                }
            }
        }

        private static void FlattenArray(JsonArray array, string prefix, MergeData data)
        {
            for (int i = 0; i < array.Count; i++)
            {
                var path = prefix + "[" + i + "]";
                var item = array[i];
                if (item is JsonObject childObj)
                {
                    FlattenNested(childObj, path, data);
                    if (!data.Values.ContainsKey(path))
                        data.Set(path, item.ToString());
                }
                else if (item is JsonArray childArray)
                {
                    FlattenArray(childArray, path, data);
                }
                else if (!data.Values.ContainsKey(path))
                {
                    data.Set(path, item == null ? string.Empty : item.ToString());
                }
            }
        }
    }
}
