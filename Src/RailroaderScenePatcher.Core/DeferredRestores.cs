// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    // The modifications of the scene patch files that would apply in this load, read once per load attempt for the
    // decisions of the game's restore-time callbacks, which run before the world-load apply. A scene patch file counts when
    // its mod is applicable, the file is readable, and it names the loaded map or one of its layers that is not a map
    // patch. Each modification is read through the engine's own parse; one that the patcher does not execute is left out.
    public sealed class RestoreTargets {
        readonly List<KeyValuePair<TargetIdentity, ObjectState>> entries;
        RestoreTargets(List<KeyValuePair<TargetIdentity, ObjectState>> entries) { this.entries = entries; }
        public static readonly RestoreTargets Empty = new RestoreTargets(new List<KeyValuePair<TargetIdentity, ObjectState>>());
        // The mods in load order, the loaded map followed by its layers, and the map patches among them.
        public static RestoreTargets Read(IEnumerable<LoadedMod> mods, IReadOnlyCollection<string> mapIds, IReadOnlyCollection<string> mapPatchIds) {
            var found = new List<KeyValuePair<TargetIdentity, ObjectState>>();
            var maps = mapIds ?? Array.Empty<string>();
            var patches = mapPatchIds ?? Array.Empty<string>();
            foreach (var mod in mods ?? Enumerable.Empty<LoadedMod>()) {
                if (mod == null || !mod.Applicable || mod.Json == null) continue;
                try {
                    var file = ScenePatchFile.Load(mod.Json);
                    if (!file.CanWrite) continue;
                    var map = (string)file.Root["sourceMap"]["mapId"];
                    if (!maps.Contains(map, StringComparer.Ordinal) || patches.Contains(map, StringComparer.Ordinal)) continue;
                    foreach (var entry in PatchEngine.Entries(file.Modifications)) {
                        try {
                            var parsed = PatchEngine.Parse(entry.Value, mod.Source.Identifier, entry.Key);
                            found.Add(new KeyValuePair<TargetIdentity, ObjectState>(parsed.Identity, parsed.State));
                        } catch (InvalidOperationException) { }
                    }
                } catch (Exception e) when (!(e is OutOfMemoryException)) { }
            }
            return new RestoreTargets(found);
        }
        // The states that modifications set on the object at the scene and path through one of the given component names, in
        // load order. The caller passes the component names a target of such an object can use, not the components the
        // object carries, so a modification that names the wrong component for the object also counts: the call then
        // waits, the engine refuses the modification, and the game's error comes after the patcher's load.
        public IReadOnlyList<ObjectState> For(string scene, string path, IEnumerable<string> components) {
            var carried = new HashSet<string>(components ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            return entries.Where(e => e.Key.Scene == scene && e.Key.Path == path && carried.Contains(e.Key.Component)).Select(e => e.Value).ToArray();
        }
        // The construction-segment settings for exactly that object, in load order.
        public IReadOnlyList<ConstructionSegments> ConstructionSegments(string scene, string path, IEnumerable<string> components) =>
            For(scene, path, components).Select(s => s.Segments).Where(s => s != null).ToArray();
    }
    // Whether a restore-time callback of a scene object waits for the world-load apply. It waits only when the callback
    // would act on data that an applicable scene patch file changes and the file's setting can take effect; otherwise the
    // game's own call runs, with the game's own result.
    public static class DeferredRestoreRule {
        // A construction controller reads its segment ids and dereferences each segment it finds on the track. The engine
        // applies the scene patch files in load order and refuses, before mutation, a detach and a setting with an id that
        // is not on the track, so the setting that stays applied is the last one that passes that check: one whose ids are
        // all on the track, or one that names the saved set, which the check accepts as it stands. The call waits when a
        // saved id is not on the track and that setting's ids all are. Without such a setting, or with saved ids that are
        // all on the track, the game's call runs.
        public static bool DeferConstruction(IReadOnlyList<ConstructionSegments> settings, IReadOnlyList<string> saved, Func<string, bool> onTrack) {
            if (settings == null || settings.Count == 0 || saved == null || onTrack == null) return false;
            if (saved.All(id => id != null && onTrack(id))) return false;
            var native = Core.ConstructionSegments.Of(saved.Where(id => id != null));
            bool OnTrack(ConstructionSegments s) => s.Ids.All(id => id != null && onTrack(id));
            var applied = settings.LastOrDefault(s => s != null && !s.IsDetached && s.Ids.Count > 0 && (Core.ConstructionSegments.Same(s, native) || OnTrack(s)));
            return applied != null && OnTrack(applied);
        }
    }
}
