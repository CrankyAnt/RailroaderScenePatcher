// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrankyAnt.CrossingDetectionResearch
{
    public static class InputReader
    {
        static void Fields(JObject obj, params string[] names)
        {
            var actual = obj.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            if (!actual.SequenceEqual(names.OrderBy(n => n, StringComparer.Ordinal))) throw new InvalidOperationException("Expected fields: " + string.Join(",", names) + "; found: " + string.Join(",", actual));
        }
        static string Text(JObject obj, string name)
        {
            if (obj[name]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)obj[name])) throw new InvalidOperationException("Expected nonempty string: " + name);
            return (string)obj[name];
        }
        static Endpoint Location(JToken token)
        {
            var obj = token as JObject ?? throw new InvalidOperationException("Endpoint must be an object."); Fields(obj, "Segment", "End", "Distance");
            var end = Text(obj, "End"); if (end != "A" && end != "B") throw new InvalidOperationException("End must be A or B.");
            var distance = obj["Distance"]; if (distance == null || (distance.Type != JTokenType.Integer && distance.Type != JTokenType.Float)) throw new InvalidOperationException("Distance must be numeric.");
            var number = (float)distance; if (float.IsNaN(number) || float.IsInfinity(number) || number < 0) throw new InvalidOperationException("Distance must be finite and nonnegative.");
            return new Endpoint { Segment = Text(obj, "Segment"), End = end, Distance = number };
        }
        public static SpanInput[] Parse(string json)
        {
            JObject root; using (var text = new System.IO.StringReader(json)) using (var reader = new JsonTextReader(text)) { root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); if (reader.Read()) throw new InvalidOperationException("Trailing JSON content."); }
            Fields(root, "Spans"); var array = root["Spans"] as JArray ?? throw new InvalidOperationException("Spans must be an array.");
            if (array.Count != 3) throw new InvalidOperationException("Exactly three spans required.");
            var result = array.Select(token => { var obj = token as JObject ?? throw new InvalidOperationException("Span must be an object."); Fields(obj, "Id", "Lower", "Upper"); var id = Text(obj, "Id"); if (!id.StartsWith("CrankyAntDetection21_", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid ID prefix: " + id); return new SpanInput { Id = id, Lower = Location(obj["Lower"]), Upper = Location(obj["Upper"]) }; }).ToArray();
            if (result.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != 3) throw new InvalidOperationException("Duplicate span IDs.");
            return result;
        }
    }
}
