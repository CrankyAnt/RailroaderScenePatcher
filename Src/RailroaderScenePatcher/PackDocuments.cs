// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    internal sealed class PackDocument {
        internal string Identifier, Name, Version, Author, Website, Json, Error;
    }
    // The two files of the active edited MapMod that the patcher writes. Inherited packs are never a destination.
    internal sealed class ActiveStorage : IStorage {
        readonly string root;
        internal ActiveStorage() {
            root = PackDocuments.ActiveRoot();
            if (root == null || !Directory.Exists(root)) throw new InvalidOperationException("The active edited MapMod has no known writable location");
        }
        internal string PatchPath => Path.Combine(root, "Data", "scene-patches.json");
        string InfoPath => Path.Combine(root, "Info.json");
        public string ReadPatchDocument() => File.Exists(PatchPath) ? PackDocuments.ReadUtf8(PatchPath) : null;
        public string ReadPackInfo() => File.Exists(InfoPath) ? PackDocuments.ReadUtf8(InfoPath) : null;
        public void WritePatchDocument(string text) { Directory.CreateDirectory(Path.GetDirectoryName(PatchPath)); File.WriteAllBytes(PatchPath, PackDocuments.Utf8.GetBytes(text)); }
        public void WritePackInfo(string text) { File.WriteAllBytes(InfoPath, PackDocuments.Utf8.GetBytes(text)); }
    }
    internal static class PackDocuments {
        internal const string ModId = "crankyant/scene-patcher";
        internal static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        internal static string ReadUtf8(string path) {
            var bytes = File.ReadAllBytes(path);
            var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            return Utf8.GetString(bytes, offset, bytes.Length - offset);
        }
        internal static string[] MapIds() {
            var active = Native.Type("Game.ActiveMapDefinition");
            var id = Native.Read(active, "Identifier")?.ToString();
            if (String.IsNullOrEmpty(id)) throw new InvalidOperationException("No active Map definition");
            var layers = Native.Read(active, "MapLayerIdentifiers") as IEnumerable;
            if (layers == null) throw new InvalidOperationException("Resolved map layers unavailable");
            return new[] { id }.Concat(layers.Cast<object>().Select(v => v.ToString())).Distinct(StringComparer.Ordinal).ToArray();
        }
        internal static bool IsEditor => (bool)Native.Read(Native.Type("Game.ActiveMapDefinition"), "IsEditTarget");
        internal static string ActiveId => Native.Read(Native.Type("Game.ActiveMapDefinition"), "Identifier")?.ToString();
        internal static string ActiveRoot() {
            var manager = Native.Read(Native.Type("Model.Database.AssetPackManager"), "Shared");
            var root = Native.Call(manager, "GetPackBasePathForDefinition", Native.Read(Native.Type("Game.ActiveMapDefinition"), "Identifier")) as string;
            return root == null ? null : Path.GetFullPath(root);
        }
        // The identifier of the pack that owns the active Map definition: the key its patch document is applied under.
        internal static string ActivePackId() {
            var root = ActiveRoot();
            var manager = Native.Read(Native.Type("Model.Database.AssetPackManager"), "Shared");
            if (root != null && Native.Read(manager, "OrderedPacks") is IEnumerable packs)
                foreach (var entry in packs) {
                    var store = Native.Read(entry, "Store");
                    if (String.Equals(Path.GetFullPath((string)Native.Read(store, "BasePath")), root, StringComparison.OrdinalIgnoreCase)) return Native.Read(store, "Identifier").ToString();
                }
            throw new InvalidOperationException("Active edited MapMod owner unavailable");
        }
        internal static IEnumerable<PackDocument> Discover() {
            var manager = Native.Read(Native.Type("Model.Database.AssetPackManager"), "Shared");
            var packs = Native.Read(manager, "OrderedPacks") as IEnumerable;
            if (packs == null) throw new InvalidOperationException("Loaded pack order unavailable");
            string editorRoot = null;
            if (IsEditor) {
                editorRoot = Native.Call(manager, "GetPackBasePathForDefinition", Native.Read(Native.Type("Game.ActiveMapDefinition"), "Identifier")) as string;
                if (editorRoot == null) throw new InvalidOperationException("Active edited MapMod owner unavailable");
                editorRoot = Path.GetFullPath(editorRoot);
            }
            foreach (var entry in packs) {
                var store = Native.Read(entry, "Store");
                var root = Path.GetFullPath((string)Native.Read(store, "BasePath"));
                if (editorRoot != null && !String.Equals(root, editorRoot, StringComparison.OrdinalIgnoreCase)) continue;
                var path = Path.Combine(root, "Data", "scene-patches.json");
                if (!File.Exists(path)) continue;
                var document = new PackDocument { Identifier = Native.Read(store, "Identifier").ToString() };
                try {
                    var info = JObject.Parse(ReadUtf8(Path.Combine(root, "Info.json")));
                    // The editor applies the active document before Save has declared the dependency.
                    if (editorRoot == null && (!(info["dependencies"]?["required"] is JArray dependencies) || !dependencies.Values<string>().Any(d => d != null && d.Split(' ')[0] == ModId))) continue;
                    document.Name = (string)info["name"] ?? document.Identifier;
                    document.Version = (string)info["version"];
                    document.Author = (string)info["author"];
                    document.Website = (string)info["website"];
                    document.Json = ReadUtf8(path);
                } catch (Exception e) { document.Error = "Cannot read patch document or supplying metadata: " + e.GetType().Name; }
                yield return document;
            }
        }
    }
}
