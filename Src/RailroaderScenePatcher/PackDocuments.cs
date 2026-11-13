// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
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
        internal const string ModId = LoadedPack.PatcherId;
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
        // The packs carrying a patch document, in Railroader's load order (AssetPackManager.OrderedPacks). In the editor only the
        // active edited MapMod is returned, marked as edit target. Whether a pack applies is decided by LoadedPack and the engine.
        internal static IReadOnlyList<LoadedPack> Discover() {
            var manager = Native.Read(Native.Type("Model.Database.AssetPackManager"), "Shared");
            var packs = Native.Read(manager, "OrderedPacks") as IEnumerable;
            if (packs == null) throw new InvalidOperationException("Loaded pack order unavailable");
            string editorRoot = null;
            if (IsEditor) {
                editorRoot = Native.Call(manager, "GetPackBasePathForDefinition", Native.Read(Native.Type("Game.ActiveMapDefinition"), "Identifier")) as string;
                if (editorRoot == null) throw new InvalidOperationException("Active edited MapMod owner unavailable");
                editorRoot = Path.GetFullPath(editorRoot);
            }
            var found = new List<LoadedPack>();
            var position = 0;
            foreach (var entry in packs) {
                position++;
                string identifier = null;
                try {
                    var store = Native.Read(entry, "Store");
                    identifier = Native.Read(store, "Identifier")?.ToString();
                    var root = Path.GetFullPath((string)Native.Read(store, "BasePath"));
                    if (editorRoot != null && !String.Equals(root, editorRoot, StringComparison.OrdinalIgnoreCase)) continue;
                    var path = Path.Combine(root, "Data", "scene-patches.json");
                    if (!File.Exists(path)) continue;
                    if (String.IsNullOrWhiteSpace(identifier)) throw new InvalidOperationException("Pack identifier unavailable");
                    string info = null, json = null, error = null;
                    try { info = ReadUtf8(Path.Combine(root, "Info.json")); json = ReadUtf8(path); }
                    catch (Exception e) { error = "Cannot read patch document or supplying metadata: " + e.GetType().Name; }
                    var pack = error == null ? LoadedPack.Read(identifier, info, json, editorRoot != null) : new LoadedPack(new PatchSource(identifier, identifier, null), null, null, editorRoot != null, error);
                    found.Add(pack);
                } catch (Exception e) {
                    // One unreadable pack entry does not hide the packs after it.
                    found.Add(new LoadedPack(new PatchSource(identifier ?? "loaded pack " + position, identifier ?? "loaded pack " + position, null), null, null, editorRoot != null, "Cannot inspect loaded pack: " + e.GetBaseException().Message));
                }
            }
            return found;
        }
    }
}
