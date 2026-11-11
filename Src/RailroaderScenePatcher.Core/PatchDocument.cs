// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    // JSON that survives an edit: every numeric literal keeps its source text unless that value is replaced.
    internal sealed class PreservingJson {
        sealed class ByReference : IEqualityComparer<JValue> {
            public bool Equals(JValue a, JValue b) { return ReferenceEquals(a, b); }
            public int GetHashCode(JValue value) { return RuntimeHelpers.GetHashCode(value); }
        }
        readonly Dictionary<JValue, string> literals = new Dictionary<JValue, string>(new ByReference());
        internal JToken Root { get; private set; }
        // False when the numeric literals could not be matched to their values; such a text must not be rewritten.
        internal bool Exact { get; private set; }
        internal static PreservingJson Parse(string json) {
            var result = new PreservingJson();
            using (var reader = new JsonTextReader(new StringReader(json))) {
                reader.DateParseHandling = DateParseHandling.None;
                result.Root = JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new JsonReaderException("Additional content after the JSON value");
            }
            var numbers = result.Root.Type == JTokenType.Object || result.Root.Type == JTokenType.Array
                ? ((JContainer)result.Root).DescendantsAndSelf().OfType<JValue>().Where(v => v.Type == JTokenType.Integer || v.Type == JTokenType.Float).ToList()
                : new List<JValue>();
            var texts = Literals(json);
            result.Exact = texts.Count == numbers.Count;
            for (var i = 0; result.Exact && i < numbers.Count; i++) {
                double parsed;
                try { result.Exact = Double.TryParse(texts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed.Equals((double)numbers[i]); }
                catch (Exception e) when (e is InvalidCastException || e is OverflowException || e is FormatException) { result.Exact = false; }
                if (result.Exact) result.literals[numbers[i]] = texts[i];
            }
            if (!result.Exact) result.literals.Clear();
            return result;
        }
        static List<string> Literals(string json) {
            var found = new List<string>();
            for (var i = 0; i < json.Length;) {
                var c = json[i];
                if (c == '"' || c == '\'') {
                    for (i++; i < json.Length && json[i] != c; i++) if (json[i] == '\\') i++;
                    i++;
                } else if (c == '/' && i + 1 < json.Length && json[i + 1] == '/') {
                    while (i < json.Length && json[i] != '\n') i++;
                } else if (c == '/' && i + 1 < json.Length && json[i + 1] == '*') {
                    var end = json.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? json.Length : end + 2;
                } else if (Char.IsLetter(c) || c == '_' || c == '$') {
                    while (i < json.Length && (Char.IsLetterOrDigit(json[i]) || json[i] == '_' || json[i] == '$')) i++;
                } else if (c == '-' || (c >= '0' && c <= '9')) {
                    var start = i;
                    while (i < json.Length && "0123456789+-.eE".IndexOf(json[i]) >= 0) i++;
                    found.Add(json.Substring(start, i - start));
                } else i++;
            }
            return found;
        }
        internal string Write() {
            var text = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
            using (var writer = new JsonTextWriter(text) { Formatting = Formatting.Indented }) Write(writer, Root);
            return text.ToString() + "\n";
        }
        void Write(JsonWriter writer, JToken token) {
            if (token is JObject obj) {
                writer.WriteStartObject();
                foreach (var property in obj.Properties()) { writer.WritePropertyName(property.Name); Write(writer, property.Value); }
                writer.WriteEndObject();
            } else if (token is JArray array) {
                writer.WriteStartArray();
                foreach (var item in array) Write(writer, item);
                writer.WriteEndArray();
            } else if (token is JValue value && literals.TryGetValue(value, out var literal)) writer.WriteRawValue(literal);
            else token.WriteTo(writer);
        }
    }
    public sealed class PatchDocument {
        internal readonly JObject Root;
        readonly string original;
        readonly PreservingJson source;
        string serialized;
        bool edited;
        public string Error { get; private set; }
        public bool CanWrite { get { return Root != null && Error == null; } }
        // Set when the document is readable but an edit could not preserve all of its content.
        internal string EditError { get; private set; }
        private PatchDocument(PreservingJson source, string error, string original = null) {
            this.source = source; Root = source?.Root as JObject; Error = error; this.original = original;
            if (source != null && !source.Exact) EditError = "Numeric content of this document cannot be preserved exactly by an edit";
        }
        public static PatchDocument Load(string json) {
            try {
                var source = PreservingJson.Parse(json);
                var root = source.Root as JObject;
                if (root == null || root["formatVersion"]?.Type != JTokenType.Integer)
                    return new PatchDocument(null, "Invalid document envelope or format version");
                if (root["formatVersion"].ToString(Formatting.None) != "1")
                    return new PatchDocument(null, "Unsupported format version");
                if (!(root["sourceMap"] is JObject sourceMap) || sourceMap["mapId"]?.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)sourceMap["mapId"]) || root["modifications"]?.Type != JTokenType.Array)
                    return new PatchDocument(null, "Invalid document map context or modifications");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in (JArray)root["modifications"]) {
                    var obj = item as JObject;
                    if (obj == null || obj["id"]?.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)obj["id"]) || !ids.Add((string)obj["id"]))
                        return new PatchDocument(null, "Modification identities must be unique strings");
                }
                return new PatchDocument(source, null, json);
            } catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException || e is OverflowException) { return new PatchDocument(null, "Unreadable document: " + e.Message); }
        }
        internal static PatchDocument Create(string mapId) {
            var document = Load(new JObject { ["formatVersion"] = 1, ["sourceMap"] = new JObject { ["mapId"] = mapId }, ["modifications"] = new JArray() }.ToString(Formatting.None));
            document.edited = true;
            return document;
        }
        internal JArray Modifications { get { return (JArray)Root["modifications"]; } }
        // Replaces, removes or inserts one modification and returns the position it occupies or left.
        internal int Replace(JObject current, JObject value, int index) {
            var items = Modifications;
            if (current != null) {
                index = items.IndexOf(current);
                if (index < 0) throw new InvalidOperationException("Modification is no longer in the document");
                if (value == null) items.RemoveAt(index); else items[index] = value;
            } else if (value != null) {
                index = Math.Max(0, Math.Min(index, items.Count));
                items.Insert(index, value);
            }
            edited = true; serialized = null;
            return index;
        }
        // The canonical text: known and unknown content with their meaning, without the source whitespace.
        internal string Serialize() { return serialized ?? (serialized = source.Write()); }
        public string Write() {
            if (!CanWrite) throw new InvalidOperationException("Document cannot be safely written");
            return edited ? Serialize() : original;
        }
    }
}
