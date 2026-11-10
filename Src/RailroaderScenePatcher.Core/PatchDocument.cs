// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    public sealed class PatchDocument {
        internal readonly JObject Root;
        readonly string original;
        public string Error { get; private set; }
        public bool CanWrite { get { return Root != null && Error == null; } }
        private PatchDocument(JObject root, string error, string original = null) { Root = root; Error = error; this.original = original; }
        public static PatchDocument Load(string json) {
            try {
                using (var reader = new JsonTextReader(new StringReader(json))) {
                    reader.DateParseHandling = DateParseHandling.None;
                    var root = JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }) as JObject;
                    if (reader.Read() || root == null || root["formatVersion"]?.Type != JTokenType.Integer)
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
                    return new PatchDocument(root, null, json);
                }
            } catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException || e is OverflowException) { return new PatchDocument(null, "Unreadable document: " + e.Message); }
        }
        public string Write() { if (!CanWrite) throw new InvalidOperationException("Document cannot be safely written"); return original; }
    }
}
