// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    // Mod objects in scene patch editing: the placing click of Duplicate adds a modification that holds the source, the
    // mod object's own target and its placement; Remove modification removes the mod object with the modifications
    // of the objects below it. Every other edit of a mod object is an ordinary edit of its target.
    public sealed partial class PatchEditingSession {
        // Raised after a mod object was placed, removed or put back, by an edit or by Undo or Redo: the objects of the
        // scene changed, so an inventory reads the scene again.
        public Action ModObjectsChanged;
        // The key of this scene patch file's mod object modification whose target is the identity, or null.
        string ModObjectKey(TargetIdentity target) {
            if (scenePatchFile == null || target == null) return null;
            foreach (var item in scenePatchFile.Items) if (item[ModObjects.SourceMember] != null && Targets(item, target)) return ScenePatchFile.Key(item);
            return null;
        }
        // Whether the active scene patch file makes the object: read from the file alone, as IsChanged is.
        public bool IsModObject(TargetIdentity target) => ModObjectKey(target) != null;
        // The scene object a duplicate of the object is built from: the object itself, for a mod object of this scene
        // patch file its own source, and for an object below a mod object the map's object it was copied from, so a mod
        // object never depends on another mod object.
        public TargetIdentity DuplicateSource(TargetIdentity target) {
            var key = ModObjectKey(target);
            if (key == null) return target == null || !ModObjects.Within(target.Path) ? target : new TargetIdentity(target.Scene, ModObjects.Plain(target.Path), target.Component, target.Structure);
            try { return PatchEngine.Parse((JObject)scenePatchFile.Modifications[key], scenePatchFileKey, key).Source ?? target; }
            catch (InvalidOperationException) { return target; }
        }
        // The placing click of Duplicate: makes a mod object from the inspected object and places it. The scene patch
        // file gets its modification, under a new key that is the mod object's identity in this mod; the scene gets
        // the object. A refusal leaves both as they were. The outcome names the new object's target. group is the path
        // of the inspected object's group when it is a part: the part's copy then stands beside that group, on its own.
        public Outcome PlaceModObject(TargetIdentity inspectedObject, Placement placement, InspectedTarget inspected = null, string group = null) {
            const string name = "Place mod object";
            var outcome = new Outcome();
            try {
                if (Blocked != null) throw new InvalidOperationException(Blocked);
                if (inspectedObject == null) throw new ArgumentNullException(nameof(inspectedObject));
                if (!(scene is IModObjectScene)) throw new InvalidOperationException(ModObjects.Unavailable);
                var at = PatchEngine.CheckPlacement(placement?.Position, placement?.Rotation);
                var origin = engine.ResolveUnique(inspectedObject);
                inspected?.Validate(scene, inspectedObject, origin);
                var from = DuplicateSource(inspectedObject);
                var key = NewId(new TargetIdentity(from.Scene, ModObjects.Plain(from.Path), from.Component), ModObjects.KeyPrefix);
                var beside = ModObjects.Beside(ModObjects.Within(inspectedObject.Path) && group == null ? inspectedObject.Path : from.Path, group);
                var identity = new TargetIdentity(from.Scene, ModObjects.Path(beside, modId, key), from.Component, ModObjects.StructureOf(from.Structure));
                var value = new JObject { ["target"] = identity.ToJson(), [ModObjects.SourceMember] = SourceJson(inspectedObject, from, origin), ["placement"] = FixedJson(at), ["dependsOn"] = new JArray() };
                outcome.ResolvedTarget = Establish(key, identity, value);
                outcome.Target = identity;
                var generation = versionGeneration; var keyGeneration = KeyGeneration(key);
                stepKeys.Add(key);
                RegisterPatchAction(() => ReplayModObject(name, key, identity, null, generation, keyGeneration), name);
                outcome.Applied = true;
                try { ModObjectsChanged?.Invoke(); } catch { }
            } catch (Exception e) { Fail(outcome, e); }
            return outcome;
        }
        // The source of a new mod object, with the model and build by which a later load finds an identical instance when
        // the original is missing. A mod object of this scene patch file passes on its own source as written, build
        // included; any other object gives its own build when the scene knows it.
        JObject SourceJson(TargetIdentity inspectedObject, TargetIdentity from, object origin) {
            var key = ModObjectKey(inspectedObject);
            if (key != null && scenePatchFile.Modifications[key]?[ModObjects.SourceMember] is JObject written) return (JObject)written.DeepClone();
            var json = from.ToJson();
            var build = (scene as IModObjectSources)?.BuildOf(origin);
            if (!String.IsNullOrEmpty(build)) json[ModObjects.BuildMember] = build;
            return json;
        }
        // Puts the modification of a mod object into the scene patch file, builds the object and applies its state.
        // A failure removes the entry and whatever was built.
        object Establish(string key, TargetIdentity identity, JObject value) {
            scenePatchFile.Replace(key, value);
            object built = null;
            try {
                var parsed = PatchEngine.Parse(value, scenePatchFileKey, key);
                built = engine.Build(parsed, scenePatchFileKey);
                var resolved = engine.ResolveUnique(identity);
                if (!ReferenceEquals(resolved, built)) throw new InvalidOperationException(ModObjects.Taken);
                scene.Preflight(resolved);
                engine.Underlying(resolved);
                var desired = engine.Desired(scenePatchFile.Modifications, resolved, scenePatchFileKey);
                engine.CheckRelationships(resolved, parsed.State);
                scene.Apply(resolved, desired);
                if (!Same(scene.Read(resolved), desired)) throw new InvalidOperationException("Applied state did not verify");
                Reapplied(key);
                return resolved;
            } catch {
                scenePatchFile.Replace(key, null);
                if (built != null) try { engine.RemoveModObject(scenePatchFileKey, key, built); } catch { }
                throw;
            }
        }
        // Remove modification of a mod object: the object leaves the scene, and its modification leaves the scene
        // patch file together with the modifications of the objects below it, which exist only with it.
        Outcome RemoveModObject(TargetIdentity target, string key, InspectedTarget inspected) {
            const string name = "Remove mod object";
            var outcome = new Outcome();
            try {
                if (Blocked != null) throw new InvalidOperationException(Blocked);
                var identity = PatchEngine.Parse((JObject)scenePatchFile.Modifications[key], scenePatchFileKey, key).Identity;
                var resolved = Resolve(identity);
                if (resolved != null) inspected?.Validate(scene, target, resolved);
                var entries = Withdraw(key, identity);
                var generation = versionGeneration; var keyGeneration = KeyGeneration(key);
                stepKeys.Add(key);
                RegisterPatchAction(() => ReplayModObject(name, key, identity, entries, generation, keyGeneration), name);
                outcome.Applied = true;
                try { ModObjectsChanged?.Invoke(); } catch { }
            } catch (Exception e) { Fail(outcome, e); }
            return outcome;
        }
        // Takes the mod object out of the scene and its entries out of the scene patch file, and returns those entries,
        // the mod object's own first, so a later step can put them back.
        List<KeyValuePair<string, JObject>> Withdraw(string key, TargetIdentity identity) {
            var below = identity.Path + "/";
            var inside = scenePatchFile.Modifications.Properties()
                .Where(p => p.Name != key && p.Value is JObject item && item["target"] is JObject target && target["path"]?.Type == JTokenType.String && ((string)target["path"]).StartsWith(below, StringComparison.Ordinal))
                .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            // A modification elsewhere that names it in dependsOn would stop taking effect.
            var dependents = Dependents(key).Where(d => !inside.Contains(d, StringComparer.Ordinal)).ToList();
            if (dependents.Count != 0)
                throw new InvalidOperationException("Modification " + key + " cannot leave the scene patch file because " + String.Join(", ", dependents) + (dependents.Count == 1 ? " names" : " name") + " it in dependsOn. " + (dependents.Count == 1 ? WindowGuidance.ChangeDependentFirst : WindowGuidance.ChangeDependentsFirst) + " Nothing was changed");
            var resolved = Resolve(identity);
            if (resolved != null) engine.RemoveModObject(scenePatchFileKey, key, resolved);
            var entries = new List<KeyValuePair<string, JObject>>();
            foreach (var slot in new[] { key }.Concat(inside)) {
                entries.Add(new KeyValuePair<string, JObject>(slot, (JObject)scenePatchFile.Modifications[slot]));
                scenePatchFile.Replace(slot, null);
                refusedEntries.Remove(slot);
            }
            return entries;
        }
        // Puts a withdrawn mod object back: its own entry builds and places it, then each entry of an object below it
        // applies. An entry below it that no longer applies stays in the scene patch file and is reported.
        void Reinstate(string key, TargetIdentity identity, List<KeyValuePair<string, JObject>> entries) {
            Establish(key, identity, entries[0].Value);
            foreach (var entry in entries.Skip(1)) {
                try {
                    var target = PatchEngine.ParseTarget(entry.Value["target"] as JObject);
                    Commit(target, engine.ResolveUnique(target), null, entry.Key, null, entry.Value);
                } catch (Exception e) {
                    scenePatchFile.Replace(entry.Key, entry.Value);
                    Refuse(entry.Key, entry.Value, e.Message);
                    try { HistoryNotice?.Invoke("Modification " + entry.Key + " is back in the scene patch file but is not applied: " + e.Message + "."); } catch { }
                }
            }
        }
        // Undo and Redo of placing or removing a mod object. entries holds what the step puts back; null removes the
        // mod object. Runs inside native Undo or Redo and must not throw.
        void ReplayModObject(string name, string key, TargetIdentity identity, List<KeyValuePair<string, JObject>> entries, int generation, int keyGeneration) {
            if (generation != versionGeneration || keyGeneration != KeyGeneration(key)) {
                RegisterPatchAction(() => ReplayModObject(name, key, identity, entries, generation, keyGeneration), name);
                try { HistoryNotice?.Invoke("This discarded scene patch step no longer changes the scene patch file."); } catch { }
                return;
            }
            List<KeyValuePair<string, JObject>> inverse;
            try {
                if (entries == null) inverse = Withdraw(key, identity);
                else { Reinstate(key, identity, entries); inverse = null; }
                try { ModObjectsChanged?.Invoke(); } catch { }
            } catch (Exception e) {
                // Nothing changed: the same step stays in the history, and Save keeps what the scene patch file shows.
                inverse = entries;
                uncertain = true;
                IncompleteOperation = name + " couldn't be reversed or reapplied: " + e.Message + ". The scene patch file keeps the state shown. " + WindowGuidance.SaveStillAvailable;
            }
            RegisterPatchAction(() => ReplayModObject(name, key, identity, inverse, generation, keyGeneration), name);
        }
        // An edit of an object below a mod object of this scene patch file applies after that mod object is built:
        // dependsOn names the mod object's modification.
        JObject WithModObjectDependency(JObject value, TargetIdentity target) {
            if (value == null || target == null) return value;
            var root = ModObjects.RootOf(target.Path);
            if (root == null || root == target.Path) return value;
            var owner = scenePatchFile.Modifications.Properties().FirstOrDefault(p => p.Value is JObject item && item[ModObjects.SourceMember] != null
                && item["target"] is JObject made && made["path"]?.Type == JTokenType.String && ModObjects.SamePath((string)made["path"], root) && made["scene"]?.Type == JTokenType.String && (string)made["scene"] == target.Scene)?.Name;
            if (owner == null) return value;
            var dependencies = (value["dependsOn"] as JArray)?.Where(d => d.Type == JTokenType.String).Select(d => (string)d).ToList() ?? new List<string>();
            if (dependencies.Contains(owner, StringComparer.Ordinal)) return value;
            var result = value.Parent != null ? (JObject)value.DeepClone() : value;
            result["dependsOn"] = new JArray(dependencies.Concat(new[] { owner }).OrderBy(k => k, StringComparer.Ordinal).Cast<object>().ToArray());
            return result;
        }
    }
}
