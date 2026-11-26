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
        // The first number whose source text could not be matched to its value, or null when that place is unknown.
        internal JValue Inexact { get; private set; }
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
            result.Exact = true;
            // Walking the numbers in text order, the first one whose literal does not match is the place to correct. With
            // as many literals as numbers and all matching, the text is exact; a missing literal, such as one for NaN,
            // shifts the rest and shows at the first number it displaces.
            for (var i = 0; result.Exact && i < numbers.Count; i++) {
                double parsed;
                try { result.Exact = i < texts.Count && Double.TryParse(texts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed.Equals((double)numbers[i]); }
                catch (Exception e) when (e is InvalidCastException || e is OverflowException || e is FormatException) { result.Exact = false; }
                if (!result.Exact) result.Inexact = numbers[i];
            }
            if (result.Exact && texts.Count != numbers.Count) result.Exact = false;
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
        // Gives every axis of the new placement position and rotation that equals the old scene patch file's value at the single
        // precision the game stores a transform in the old value, so KeepLiterals then keeps its source literal. A placement
        // read back from the game differs from the scene patch file's literal below that precision; an edit of one axis would
        // otherwise rewrite the others' text.
        // The placements of crossing equipment entries with the same key follow the same rule.
        internal static void KeepStoredPrecision(JToken old, JToken value) {
            KeepPlacementPrecision(old?["placement"], value?["placement"]);
            if (old?["relationships"]?["crossingEquipment"] is JObject before && value?["relationships"]?["crossingEquipment"] is JObject after)
                foreach (var entry in after.Properties())
                    if (before[entry.Name] is JObject was && entry.Value is JObject now) KeepPlacementPrecision(was["placement"], now["placement"]);
        }
        static void KeepPlacementPrecision(JToken old, JToken value) {
            if (!(old is JObject) || !(value is JObject)) return;
            foreach (var member in new[] { "position", "rotation" }) {
                if (!(old?[member] is JArray items) || !(value?[member] is JArray others)) continue;
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
        internal string WriteCanonical() { return Text(writer => Write(writer, Root, Shape.ScenePatchFile)); }
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
            static readonly Shape Placement = new Shape { Known = new[] { "mode", "position", "track", "offset", "rotation" }, Member = name => name == "track" ? Location : Unknown };
            static readonly Shape Source = new Shape { Known = new[] { "scene", "path", "definition" } };
            static readonly Shape Equipment = new Shape { Known = new[] { "pattern", "source", "placement" }, Member = name => name == "source" ? Source : name == "placement" ? Placement : Unknown };
            static readonly Shape EquipmentEntries = new Shape { Member = name => Equipment };
            // The construction segments are a set: their ids are written in ordinal order.
            static readonly Shape SegmentIds = new Shape { SortedStrings = true };
            static readonly Shape ConstructionSegments = new Shape { Known = new[] { "segments", "detach" }, Member = name => name == "segments" ? SegmentIds : Unknown };
            static readonly Shape Relationships = new Shape { Known = new[] { "trackMarker", "crossingCoverage", "crossingEquipment", "constructionSegments" },
                Member = name => name == "trackMarker" ? Location : name == "crossingCoverage" ? Coverage : name == "crossingEquipment" ? EquipmentEntries : name == "constructionSegments" ? ConstructionSegments : Unknown };
            static readonly Shape Target = new Shape { Known = new[] { "scene", "path", "component", "structure", TargetIdentity.PadMarkerMember, TargetIdentity.MarkerIdMember, TargetIdentity.MapPositionMember } };
            static readonly Shape Dependencies = new Shape { SortedStrings = true };
            static readonly Shape Starter = new Shape { Known = new[] { "setup", "track", "wreck" }, Member = name => name == "track" ? Location : Unknown };
            static readonly Shape Modification = new Shape { Known = new[] { "target", "markerId", "relationships", "starterPlacement", "placement", "dependsOn", "active" },
                Member = name => name == "target" ? Target : name == "relationships" ? Relationships : name == "starterPlacement" ? Starter : name == "placement" ? Placement : name == "dependsOn" ? Dependencies : Unknown };
            static readonly Shape Modifications = new Shape { Member = name => Modification };
            static readonly Shape SceneTargetMap = new Shape { Known = new[] { "mapId" } };
            internal static readonly Shape ScenePatchFile = new Shape { Known = new[] { "formatVersion", "sourceMap", "modifications" },
                Member = name => name == "sourceMap" ? SceneTargetMap : name == "modifications" ? Modifications : Unknown };
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
    public sealed class ScenePatchFile {
        // The format version this patcher reads and writes.
        public const int CurrentFormat = 1;
        internal readonly JObject Root;
        readonly string original;
        readonly PreservingJson source;
        readonly List<ScenePatchFileDiagnostic> diagnostics = new List<ScenePatchFileDiagnostic>();
        string serialized;
        bool edited;
        public string Error { get; private set; }
        public bool CanWrite { get { return Root != null && Error == null; } }
        // Set when the scene patch file is readable but an edit could not preserve all of its content.
        internal string EditError { get; private set; }
        // What the last read found wrong in the text: why the scene patch file is protected, and each modification this
        // patcher does not execute. Empty for a scene patch file without problems.
        public IReadOnlyList<ScenePatchFileDiagnostic> Diagnostics => diagnostics;
        private ScenePatchFile(PreservingJson source, string error, string original = null) {
            this.source = source; Root = source?.Root as JObject; Error = error; this.original = original;
            if (source != null && !source.Exact) {
                EditError = "Numeric content of this scene patch file cannot be preserved exactly by an edit";
                var place = source.Inexact;
                diagnostics.Add(ScenePatchFileDiagnostic.Create(original, place?.Path ?? "", EditError, "Numbers written as plain JSON literals; correct values such as NaN or Infinity outside scene patch editing", place ?? (JToken)Root));
            }
        }
        static ScenePatchFile Protected(string json, string message, string path, string expected, JToken token = null, int line = 0, string technicalDetail = null, bool unreadable = false) {
            var rejected = new ScenePatchFile(null, technicalDetail == null ? message : message + ". " + Sentence(technicalDetail), json);
            rejected.diagnostics.Add(ScenePatchFileDiagnostic.Create(json, path, message, expected, token, line, technicalDetail, unreadable));
            return rejected;
        }
        const string VersionExpected = "\"formatVersion\": 1 as the first thing the scene patch file declares; a newer scene patch file needs the patcher version that wrote it";
        public static ScenePatchFile Load(string json) {
            // The declared format version decides how the rest of the text is read, so it is checked before anything
            // else: a missing, unrecognized or newer version protects the scene patch file whatever the rest contains.
            var version = DeclaredVersion(json);
            if (version != null && version.Problem != null) return Protected(json, version.Problem, "formatVersion", VersionExpected, null, version.Line);
            try {
                var source = PreservingJson.Parse(json);
                var root = source.Root as JObject;
                if (root == null)
                    return Protected(json, "Invalid scene patch file envelope. The scene patch file must be a JSON object", "", "A JSON object with formatVersion, sourceMap, and modifications", source.Root);
                if (!(root["sourceMap"] is JObject sceneTargetMap) || sceneTargetMap["mapId"]?.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)sceneTargetMap["mapId"]))
                    return Protected(json, "The scene patch file needs a map ID", "sourceMap.mapId", "\"sourceMap\": { \"mapId\": \"<the scene target map>\" } with the ID of the edited map or one of its map layers. Use a map definition that is not a map patch, normally the bottom map layer", root["sourceMap"]?["mapId"] ?? root["sourceMap"]);
                if (!(root["modifications"] is JObject modifications))
                    return Protected(json, "Invalid scene patch file map context or modifications. The modifications must be an object keyed by modification ID", "modifications", "\"modifications\": { \"<modification ID>\": { ... } }", root["modifications"]);
                foreach (var property in modifications.Properties())
                    if (String.IsNullOrWhiteSpace(property.Name) || !(property.Value is JObject))
                        return Protected(json, "Every modification must be an object under a key that is not empty", property.Path, "\"<modification ID>\": { \"target\": { ... }, ... } under a key that is not empty", property);
                var loaded = new ScenePatchFile(source, null, json);
                // A modification this patcher does not execute stays in the scene patch file unchanged; its diagnostic says why.
                foreach (var property in modifications.Properties()) {
                    try { PatchEngine.Parse((JObject)property.Value, "", property.Name); }
                    catch (InvalidOperationException e) {
                        loaded.diagnostics.Add(ScenePatchFileDiagnostic.Create(json, property.Path, "Modification " + property.Name + " is not executed. " + Sentence(e.Message),
                            "A target, a placement and/or active, relationships, and a dependsOn list as the format reference describes. Correct known values outside scene patch editing; keep content of a newer patcher unless you mean to remove it", property.Value));
                    }
                }
                return loaded;
            } catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException || e is OverflowException) {
                var reader = e as JsonReaderException;
                return Protected(json, "Unreadable scene patch file", reader?.Path ?? "", null, line: reader?.LineNumber ?? 0, technicalDetail: e.Message, unreadable: true);
            }
        }
        // Why the text's declared format version keeps any of its content from being read, or null. Before this answer
        // is null, no other member of the text, such as its scene target map, has a meaning.
        public static string FormatProblem(string json) => DeclaredVersion(json)?.Problem;
        sealed class Declaration { internal string Problem; internal int Line; }
        // Reads only the top-level formatVersion of the text, without the checks of the full read, so a scene patch file of
        // another format version is recognised as such even when its content would not read as the current format.
        // Null when the text cannot be read as far as a decision: the full read then reports it as unreadable.
        static Declaration DeclaredVersion(string json) {
            if (json == null) return null;
            var found = new List<Declaration>();
            var values = new List<string>();
            try {
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None }) {
                    bool Next() { while (reader.Read()) if (reader.TokenType != JsonToken.Comment) return true; return false; }
                    if (!Next() || reader.TokenType != JsonToken.StartObject) return null;
                    while (Next() && reader.TokenType == JsonToken.PropertyName) {
                        var name = (string)reader.Value;
                        var line = reader.LineNumber;
                        if (!Next()) return null;
                        if (name == "formatVersion") {
                            found.Add(new Declaration { Line = line });
                            // The reader also accepts forms JSON has no integer for, such as 01 or 0x1, and normalises
                            // them; the source literal decides, and only a JSON integer literal is a version number.
                            var literal = reader.TokenType == JsonToken.Integer ? SourceLiteral(json, reader.LineNumber, reader.LinePosition) : null;
                            // Null stands for a value that is no version number at all.
                            values.Add(literal != null && JsonInteger.IsMatch(literal) ? literal : null);
                        }
                        reader.Skip();
                    }
                }
            } catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException || e is OverflowException || e is FormatException) {
                // The text stops being readable: a version declared before that point still decides.
                var declared = values.FindIndex(v => v != "1");
                if (declared < 0) return null;
                return Describe(found[declared], values[declared]);
            }
            if (found.Count == 0) return new Declaration { Problem = "Missing format version. The scene patch file does not declare formatVersion, so none of its content is read" };
            var other = values.FindIndex(v => v != "1");
            return other < 0 ? new Declaration() : Describe(found[other], values[other]);
        }
        static readonly System.Text.RegularExpressions.Regex JsonInteger = new System.Text.RegularExpressions.Regex("^-?(0|[1-9][0-9]*)$");
        // The number literal that ends at the reader's line and position: the characters a number of any form the reader
        // accepts can hold, read backwards from there over any delimiter the reader already passed.
        static string SourceLiteral(string json, int line, int position) {
            var start = 0;
            for (int i = 0, current = 1; current < line && i < json.Length; i++) {
                if (json[i] == '\r' && i + 1 < json.Length && json[i + 1] == '\n') i++;
                if (json[i] == '\n' || json[i] == '\r') { current++; start = i + 1; }
            }
            var end = Math.Min(json.Length, start + position) - 1;
            bool Part(char c) => Char.IsLetterOrDigit(c) || c == '+' || c == '-' || c == '.';
            while (end >= start && !Part(json[end])) end--;
            var first = end;
            while (first > start && Part(json[first - 1])) first--;
            return end < first ? null : json.Substring(first, end - first + 1);
        }
        // A message of another component as one sentence inside ours: without its own final full stop, which ours adds.
        internal static string Sentence(string message) {
            var text = (message ?? "").Trim();
            while (text.EndsWith(".", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1).TrimEnd();
            return text;
        }
        static Declaration Describe(Declaration version, string value) {
            // A JSON integer literal without a sign is greater than the current format when it is longer or larger.
            var digits = value != null && value.All(c => c >= '0' && c <= '9');
            if (digits && (value.Length > 1 || value[0] - '0' > CurrentFormat))
                version.Problem = "Newer format version " + value + ". This patcher reads format version " + CurrentFormat + " and applies none of this scene patch file's content";
            else version.Problem = "Unrecognized format version. The formatVersion must be the number " + CurrentFormat + "; none of this scene patch file's content is read";
            return version;
        }
        // Whether two scene patch file texts have equal content under the Saved equality: member order, spacing, comments and the
        // literal a number is written in do not count. A text that cannot be parsed equals only the same text; a missing
        // text equals only a missing text.
        public static bool SameContent(string first, string second) {
            if (first == null || second == null) return first == null && second == null;
            if (String.Equals(first, second, StringComparison.Ordinal)) return true;
            try { return PreservingJson.SameContent(PreservingJson.Parse(first).Root, PreservingJson.Parse(second).Root); }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException || e is OverflowException) { return false; }
        }
        internal static ScenePatchFile Create(string mapId) {
            var scenePatchFile = Load(new JObject { ["formatVersion"] = 1, ["sourceMap"] = new JObject { ["mapId"] = mapId }, ["modifications"] = new JObject() }.ToString(Formatting.None));
            scenePatchFile.edited = true;
            return scenePatchFile;
        }
        // The modifications by key, in the order of the source followed by the order edits added them.
        internal JObject Modifications { get { return (JObject)Root["modifications"]; } }
        internal IEnumerable<JObject> Items { get { return Modifications.PropertyValues().OfType<JObject>(); } }
        // The key of a modification that stands in a scene patch file, or null.
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
            if (!CanWrite) throw new InvalidOperationException("Scene patch file cannot be safely written");
            return edited ? Serialize() : original;
        }
    }
}
