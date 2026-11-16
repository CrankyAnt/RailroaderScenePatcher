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
        // The source text of each number read from a text, by token. A token an edit creates has none.
        static readonly ConditionalWeakTable<JValue, string> SourceLiterals = new ConditionalWeakTable<JValue, string>();
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
                ? ((JContainer)result.Root).DescendantsAndSelf().OfType<JValue>().Where(IsNumber).ToList()
                : new List<JValue>();
            var texts = Literals(json);
            result.Exact = texts.Count == numbers.Count;
            for (var i = 0; result.Exact && i < numbers.Count; i++) {
                double parsed;
                try { result.Exact = Double.TryParse(texts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed.Equals((double)numbers[i]); }
                catch (Exception e) when (e is InvalidCastException || e is OverflowException || e is FormatException) { result.Exact = false; }
            }
            if (result.Exact) for (var i = 0; i < numbers.Count; i++) SourceLiterals.Add(numbers[i], texts[i]);
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
        static bool IsNumber(JToken token) { return token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float); }
        // The value a JSON number denotes, converted to the nearest double.
        static double Number(JToken token) {
            var value = (JValue)token;
            if (SourceLiterals.TryGetValue(value, out var literal) && Double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            return (double)value;
        }
        // Equal content: object members match without regard to their order, the dependsOn of a modification that is an
        // array of strings matches as a list sorted in ordinal order with duplicates counted, every other array matches in
        // order, strings, booleans and null match exactly, and numbers compare by the value they denote as a double.
        internal static bool SameContent(JToken a, JToken b) {
            var first = a as JObject; var second = b as JObject;
            if (first == null || second == null) return SameValue(a, b);
            return SameMembers(first, second, (name, x, y) => name == "modifications" && x is JObject xs && y is JObject ys
                ? SameMembers(xs, ys, (key, m, n) => SameModification(m, n))
                : SameValue(x, y));
        }
        // Equal content of two modifications; either may be absent.
        internal static bool SameModification(JToken a, JToken b) {
            return a is JObject first && b is JObject second ? SameMembers(first, second, (member, p, q) => member == "dependsOn" ? SameDependencies(p, q) : SameValue(p, q)) : SameValue(a, b);
        }
        static bool SameMembers(JObject a, JObject b, Func<string, JToken, JToken, bool> same) {
            if (a.Count != b.Count) return false;
            foreach (var property in a.Properties()) {
                var other = b.Property(property.Name);
                if (other == null || !same(property.Name, property.Value, other.Value)) return false;
            }
            return true;
        }
        static bool Strings(JToken token) { return token is JArray array && array.All(x => x.Type == JTokenType.String); }
        static bool SameDependencies(JToken a, JToken b) {
            if (!Strings(a) || !Strings(b)) return SameValue(a, b);
            return a.Select(x => (string)x).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(b.Select(x => (string)x).OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);
        }
        // Equal content of two values outside the dependencies of a modification.
        internal static bool SameValue(JToken a, JToken b) {
            if (a == null || b == null) return a == b;
            if (a is JObject first) return b is JObject second && SameMembers(first, second, (name, x, y) => SameValue(x, y));
            if (a is JArray items) {
                var others = b as JArray;
                if (others == null || items.Count != others.Count) return false;
                for (var i = 0; i < items.Count; i++) if (!SameValue(items[i], others[i])) return false;
                return true;
            }
            if (IsNumber(a) || IsNumber(b)) return IsNumber(a) && IsNumber(b) && Number(a) == Number(b);
            return a.Type == b.Type && JToken.DeepEquals(a, b);
        }
        // Gives every number of the new value that an old number at the same place equals by value that number's source
        // literal, so an edit rewrites only the numbers it changes.
        internal static void KeepLiterals(JToken old, JToken value) {
            if (old is JObject first && value is JObject second) {
                foreach (var property in second.Properties()) KeepLiterals(first[property.Name], property.Value);
            } else if (old is JArray items && value is JArray others) {
                for (var i = 0; i < items.Count && i < others.Count; i++) KeepLiterals(items[i], others[i]);
            } else if (IsNumber(old) && IsNumber(value) && !ReferenceEquals(old, value) && SourceLiterals.TryGetValue((JValue)old, out var literal) && Number(old) == Number(value)) {
                SourceLiterals.Remove((JValue)value);
                SourceLiterals.Add((JValue)value, literal);
            }
        }
        // Gives every axis of the new placement position and rotation that equals the old document's value at the single
        // precision the game stores a transform in the old value, so KeepLiterals then keeps its source literal. A pose
        // read back from the game differs from the document's literal below that precision; an edit of one axis would
        // otherwise rewrite the others' text.
        internal static void KeepStoredPrecision(JToken old, JToken value) {
            foreach (var member in new[] { "position", "rotation" }) {
                if (!(old?["placement"]?[member] is JArray items) || !(value?["placement"]?[member] is JArray others)) continue;
                for (var i = 0; i < items.Count && i < others.Count; i++) {
                    if (!IsNumber(items[i]) || !IsNumber(others[i])) continue;
                    double was = Number(items[i]), now = Number(others[i]);
                    if (was != now && (float)was == (float)now) others[i] = new JValue(was);
                }
            }
        }
        // The text in the member order of the source.
        internal string Write() { return Text(writer => Write(writer, Root, null)); }
        // The text in canonical order. The tokens stay where they are; only the text is ordered.
        internal string WriteCanonical() { return Text(writer => Write(writer, Root, Shape.Document)); }
        static string Text(Action<JsonWriter> write) {
            var text = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
            using (var writer = new JsonTextWriter(text) { Formatting = Formatting.Indented }) write(writer);
            return text.ToString() + "\n";
        }
        // The canonical order of one kind of object: its known members in their fixed order, or keys in ordinal order, and
        // the shape of each member. Members without a place follow in ordinal name order, at every depth.
        sealed class Shape {
            internal string[] Known = new string[0];
            internal Func<string, Shape> Member = name => Unknown;
            // The values of an array of strings are written in ordinal order.
            internal bool SortedStrings;
            internal static readonly Shape Unknown = new Shape();
            static readonly Shape Location = new Shape { Known = new[] { "segment", "end", "distance" } };
            static readonly Shape Span = new Shape { Known = new[] { "from", "to" }, Member = name => name == "from" || name == "to" ? Location : Unknown };
            static readonly Shape Entry = new Shape { Known = new[] { "approachA", "island", "approachB", "detach" }, Member = name => name == "approachA" || name == "island" || name == "approachB" ? Span : Unknown };
            static readonly Shape Entries = new Shape { Member = name => Entry };
            static readonly Shape Coverage = new Shape { Known = new[] { "circuits", "additions" }, Member = name => name == "circuits" || name == "additions" ? Entries : Unknown };
            static readonly Shape Relationships = new Shape { Known = new[] { "trackMarker", "crossingCoverage", "crossingEquipment" },
                Member = name => name == "trackMarker" ? Location : name == "crossingCoverage" ? Coverage : Unknown };
            static readonly Shape Placement = new Shape { Known = new[] { "mode", "position", "track", "offset", "rotation" }, Member = name => name == "track" ? Location : Unknown };
            static readonly Shape Target = new Shape { Known = new[] { "scene", "path", "component", "structure" } };
            static readonly Shape Dependencies = new Shape { SortedStrings = true };
            static readonly Shape Starter = new Shape { Known = new[] { "setup", "track" }, Member = name => name == "track" ? Location : Unknown };
            static readonly Shape Modification = new Shape { Known = new[] { "target", "relationships", "starterPlacement", "placement", "dependsOn", "active" },
                Member = name => name == "target" ? Target : name == "relationships" ? Relationships : name == "starterPlacement" ? Starter : name == "placement" ? Placement : name == "dependsOn" ? Dependencies : Unknown };
            static readonly Shape Modifications = new Shape { Member = name => Modification };
            static readonly Shape TargetMap = new Shape { Known = new[] { "mapId" } };
            internal static readonly Shape Document = new Shape { Known = new[] { "formatVersion", "sourceMap", "modifications" },
                Member = name => name == "sourceMap" ? TargetMap : name == "modifications" ? Modifications : Unknown };
        }
        static void Write(JsonWriter writer, JToken token, Shape shape) {
            if (token is JObject obj) {
                writer.WriteStartObject();
                IEnumerable<JProperty> properties = obj.Properties();
                if (shape != null) properties = shape.Known.Select(obj.Property).Where(p => p != null)
                    .Concat(obj.Properties().Where(p => !shape.Known.Contains(p.Name, StringComparer.Ordinal)).OrderBy(p => p.Name, StringComparer.Ordinal));
                foreach (var property in properties) { writer.WritePropertyName(property.Name); Write(writer, property.Value, shape?.Member(property.Name)); }
                writer.WriteEndObject();
            } else if (token is JArray array) {
                writer.WriteStartArray();
                IEnumerable<JToken> items = array;
                if (shape != null && shape.SortedStrings && Strings(array)) items = array.OrderBy(x => (string)x, StringComparer.Ordinal);
                // A dependsOn that is not an array of strings keeps its source form, also inside its items.
                foreach (var item in items) Write(writer, item, shape == null || shape.SortedStrings ? null : Shape.Unknown);
                writer.WriteEndArray();
            } else if (token is JValue value && SourceLiterals.TryGetValue(value, out var literal)) writer.WriteRawValue(literal);
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
                if (!(root["sourceMap"] is JObject targetMap) || targetMap["mapId"]?.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)targetMap["mapId"]))
                    return new PatchDocument(null, "Invalid document map context or modifications");
                if (!(root["modifications"] is JObject modifications))
                    return new PatchDocument(null, "Invalid document map context or modifications: modifications must be an object keyed by modification ID");
                foreach (var property in modifications.Properties())
                    if (String.IsNullOrWhiteSpace(property.Name) || !(property.Value is JObject))
                        return new PatchDocument(null, "Every modification must be an object under a key that is not empty");
                return new PatchDocument(source, null, json);
            } catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException || e is OverflowException) { return new PatchDocument(null, "Unreadable document: " + e.Message); }
        }
        // Whether two document texts have equal content under the Saved equality: member order, spacing, comments and the
        // literal a number is written in do not count. A text that cannot be parsed equals only the same text; a missing
        // text equals only a missing text.
        public static bool SameContent(string first, string second) {
            if (first == null || second == null) return first == null && second == null;
            if (String.Equals(first, second, StringComparison.Ordinal)) return true;
            try { return PreservingJson.SameContent(PreservingJson.Parse(first).Root, PreservingJson.Parse(second).Root); }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException || e is OverflowException) { return false; }
        }
        internal static PatchDocument Create(string mapId) {
            var document = Load(new JObject { ["formatVersion"] = 1, ["sourceMap"] = new JObject { ["mapId"] = mapId }, ["modifications"] = new JObject() }.ToString(Formatting.None));
            document.edited = true;
            return document;
        }
        // The modifications by key, in the order of the source followed by the order edits added them.
        internal JObject Modifications { get { return (JObject)Root["modifications"]; } }
        internal IEnumerable<JObject> Items { get { return Modifications.PropertyValues().OfType<JObject>(); } }
        // The key of a modification that stands in a document, or null.
        internal static string Key(JObject item) { return (item?.Parent as JProperty)?.Name; }
        // Counts the edits, so a caller can tell whether the content changed since it last looked.
        internal int Version { get; private set; }
        // Sets the modification under a key, or removes it when the value is null.
        internal void Replace(string key, JObject value) {
            if (String.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("A modification needs a key");
            // The replaced value leaves its property first, so a later Undo puts back that same token, not a copy of it.
            var property = Modifications.Property(key);
            if (property == null) { if (value != null) Modifications.Add(key, value); }
            else if (value == null) { property.Remove(); property.Value = JValue.CreateNull(); }
            else property.Value = value;
            edited = true; serialized = null; Version++;
        }
        // The canonical text: known and unknown content with their meaning in canonical order, without the source whitespace.
        internal string Serialize() { return serialized ?? (serialized = source.WriteCanonical()); }
        public string Write() {
            if (!CanWrite) throw new InvalidOperationException("Document cannot be safely written");
            return edited ? Serialize() : original;
        }
    }
}
