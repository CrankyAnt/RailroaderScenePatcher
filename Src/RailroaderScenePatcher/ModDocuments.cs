// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    internal static class ModDocuments {
        internal const string PatcherId = LoadedMod.PatcherId;
        // Raised after the patcher wrote the edited mod's patch document, with the written text.
        internal static Action<string> Written;
        internal static string ReadUtf8(string path) => ModStorage.Decode(File.ReadAllBytes(path));
        // The storage of the edited mod, the only destination the patcher writes. Mods that supply a map layer or terrain tile set are never one.
        internal static ModStorage ActiveStorage() {
            var root = ActiveRoot();
            if (root == null || !Directory.Exists(root)) throw new InvalidOperationException("The edited mod has no known writable location");
            return new ModStorage(root, null, text => Written?.Invoke(text));
        }
        internal static string[] MapIds() {
            var active = Native.Type("Game.ActiveMapDefinition");
            var id = Native.Read(active, "Identifier")?.ToString();
            if (String.IsNullOrEmpty(id)) throw new InvalidOperationException("No active map definition");
            var layers = Native.Read(active, "MapLayerIdentifiers") as IEnumerable;
            if (layers == null) throw new InvalidOperationException("Resolved map layers unavailable");
            return new[] { id }.Concat(layers.Cast<object>().Select(v => v.ToString())).Distinct(StringComparer.Ordinal).ToArray();
        }
        internal static bool IsMapEditor => (bool)Native.Read(Native.Type("Game.ActiveMapDefinition"), "IsEditTarget");
        internal static string ActiveId => Native.Read(Native.Type("Game.ActiveMapDefinition"), "Identifier")?.ToString();
        internal static string ActiveRoot() {
            var manager = Native.Read(Native.Type("Model.Database.AssetPackManager"), "Shared");
            var root = Native.Call(manager, "GetPackBasePathForDefinition", Native.Read(Native.Type("Game.ActiveMapDefinition"), "Identifier")) as string;
            return root == null ? null : Path.GetFullPath(root);
        }
        // The identifier of the mod that owns the active map definition: the key its patch document is applied under.
        internal static string EditedModId() {
            var root = ActiveRoot();
            var manager = Native.Read(Native.Type("Model.Database.AssetPackManager"), "Shared");
            if (root != null && Native.Read(manager, "OrderedPacks") is IEnumerable mods)
                foreach (var entry in mods) {
                    var store = Native.Read(entry, "Store");
                    if (String.Equals(Path.GetFullPath((string)Native.Read(store, "BasePath")), root, StringComparison.OrdinalIgnoreCase)) return Native.Read(store, "Identifier").ToString();
                }
            throw new InvalidOperationException("Edited mod owner unavailable");
        }
        // The mods carrying a patch document, in Railroader's load order (AssetPackManager.OrderedPacks). In the native map editor only the
        // edited mod is returned, marked as edit target. Whether a mod applies is decided by LoadedMod and the engine.
        internal static IReadOnlyList<LoadedMod> Discover() {
            var manager = Native.Read(Native.Type("Model.Database.AssetPackManager"), "Shared");
            var mods = Native.Read(manager, "OrderedPacks") as IEnumerable;
            if (mods == null) throw new InvalidOperationException("Load order unavailable");
            string editedModRoot = null;
            if (IsMapEditor) {
                editedModRoot = Native.Call(manager, "GetPackBasePathForDefinition", Native.Read(Native.Type("Game.ActiveMapDefinition"), "Identifier")) as string;
                if (editedModRoot == null) throw new InvalidOperationException("Edited mod owner unavailable");
                editedModRoot = Path.GetFullPath(editedModRoot);
            }
            var found = new List<LoadedMod>();
            var position = 0;
            foreach (var entry in mods) {
                position++;
                string identifier = null;
                try {
                    var store = Native.Read(entry, "Store");
                    identifier = Native.Read(store, "Identifier")?.ToString();
                    var root = Path.GetFullPath((string)Native.Read(store, "BasePath"));
                    if (editedModRoot != null && !String.Equals(root, editedModRoot, StringComparison.OrdinalIgnoreCase)) continue;
                    var path = Path.Combine(root, "Data", "scene-patches.json");
                    if (!File.Exists(path)) continue;
                    if (String.IsNullOrWhiteSpace(identifier)) throw new InvalidOperationException("Mod identifier unavailable");
                    string info = null, json = null, error = null;
                    try { info = ReadUtf8(Path.Combine(root, "Info.json")); json = ReadUtf8(path); }
                    catch (Exception e) { error = "Cannot read patch document or supplying metadata: " + e.GetType().Name; }
                    var mod = error == null ? LoadedMod.Read(identifier, info, json, editedModRoot != null) : new LoadedMod(new PatchSource(identifier, identifier, null), null, null, editedModRoot != null, error);
                    found.Add(mod);
                } catch (Exception e) {
                    // One unreadable mod entry does not hide the mods after it.
                    found.Add(new LoadedMod(new PatchSource(identifier ?? "loaded mod " + position, identifier ?? "loaded mod " + position, null), null, null, editedModRoot != null, "Cannot inspect loaded mod: " + e.GetBaseException().Message));
                }
            }
            return found;
        }
    }
}
