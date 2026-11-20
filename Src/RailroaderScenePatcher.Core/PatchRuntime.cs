// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // NotApplicable: a scene patch file that does not apply in the loaded map context. It is no fault, so its warning names no one to report to.
    public enum FailureCause { Unknown, InvalidDefinition, PatcherFault, NotApplicable }
    public sealed class PatchSource {
        public PatchSource(string identifier, string name, string version, string author = null, string website = null) {
            Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier)); Name = name; Version = version; Author = author; Website = website;
        }
        public string Identifier { get; }
        public string Name { get; }
        public string Version { get; }
        public string Author { get; }
        public string Website { get; }
    }
    public sealed class PatchFailure {
        internal PatchFailure(PatchSource mod, string operation, TargetIdentity target, string error, FailureCause cause, string patcherVersion, string gameVersion, string diagnosticKey) {
            DiagnosticKey = diagnosticKey; ModIdentifier = mod.Identifier; Operation = operation; ObjectPath = target?.Path; Cause = cause;
            ReportTo = cause == FailureCause.NotApplicable ? null : cause == FailureCause.InvalidDefinition ? "mod author" : "patcher maintainer";
            Message = (mod.Name ?? mod.Identifier) + " [" + mod.Identifier + "; version " + (mod.Version ?? "not supplied") + "]. Definition Data/scene-patches.json; operation " + operation +
                "; object " + (target == null ? "not resolved" : target.Label) + ". " + error +
                ". Patcher " + patcherVersion + "; game " + gameVersion + "." + (ReportTo == null ? "" : " Report to the " + ReportTo + ".");
            if (!String.IsNullOrWhiteSpace(mod.Author)) Message += " Mod author: " + mod.Author + ".";
            if (Uri.TryCreate(mod.Website, UriKind.Absolute, out var site) && (site.Scheme == "https" || site.Scheme == "http")) Message += " Published website: " + mod.Website + ".";
        }
        public string Source => "Railroader Scene Patcher";
        public string ModIdentifier { get; }
        public string DiagnosticKey { get; }
        public string Operation { get; }
        public string ObjectPath { get; }
        public FailureCause Cause { get; }
        public string ReportTo { get; }
        public string Message { get; private set; }
    }
    public interface IPatchDiagnostics { void Warning(PatchFailure failure); }
    public sealed class CurrentWarning {
        public CurrentWarning(object token, string source, string key, string originator, string description) {
            Token = token; Source = source; Key = key; Originator = originator; Description = description;
        }
        public object Token { get; }
        public string Source { get; }
        public string Key { get; }
        public string Originator { get; }
        public string Description { get; }
    }
    public sealed class WarningAttribution {
        public WarningAttribution(CurrentWarning warning, object target, string earlyRouteUnavailable) {
            Warning = warning; Target = target; EarlyRouteUnavailable = earlyRouteUnavailable;
        }
        public CurrentWarning Warning { get; }
        public object Target { get; }
        // Concrete observation at the relevant native check, not an assumption that all world-load callbacks are late.
        public string EarlyRouteUnavailable { get; }
    }
    public interface ICurrentWarnings {
        IEnumerable<CurrentWarning> Current { get; }
        void Remove(CurrentWarning row);
    }
    // One instance belongs to one loaded world. The sink represents the structured native collector, never the file log.
    public sealed class PatchRuntime {
        readonly PatchEngine engine;
        readonly IPatchDiagnostics diagnostics;
        readonly string patcherVersion, gameVersion;
        readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, Outcome> latest = new Dictionary<string, Outcome>(StringComparer.Ordinal);
        static string Key(PatchSource source, string operation) => source.Identifier.Length + ":" + source.Identifier + operation;
        public PatchRuntime(PatchEngine engine, IPatchDiagnostics diagnostics, string patcherVersion, string gameVersion) {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine)); this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            this.patcherVersion = patcherVersion; this.gameVersion = gameVersion;
        }
        // Applies the scene patch files of loaded mods in the given native load order. Only a mod that declares the patcher
        // dependency, or the native map editor's edit target, reaches the engine, which checks whether its scene target map is in the map context.
        public IReadOnlyList<LoadedScenePatchFile> ApplyLoaded(IEnumerable<LoadedMod> loadOrder, IOverlapNotices notices) {
            if (loadOrder == null) throw new ArgumentNullException(nameof(loadOrder));
            var scenePatchFiles = new List<LoadedScenePatchFile>();
            foreach (var mod in loadOrder) {
                if (mod == null) continue;
                var scenePatchFile = new LoadedScenePatchFile(mod);
                scenePatchFiles.Add(scenePatchFile);
                if (mod.Error != null) { scenePatchFile.Skipped = mod.Error; continue; }
                if (!mod.Applicable) { scenePatchFile.Skipped = "The mod does not declare a dependency on " + LoadedMod.PatcherId; continue; }
                // The engine isolates scene patch file and operation failures itself; presentation faults stay with this scene patch file.
                ClearSource(mod.Source);
                scenePatchFile.Result = engine.Apply(mod.Json, mod.Source.Identifier);
                // In the native map editor only the edited mod loads, and its mismatch stays with scene patch editing's own diagnostic.
                try {
                    ReportResult(scenePatchFile.Result, mod.Source);
                    if (scenePatchFile.Result.NotApplicable != null && !mod.EditTarget) Report(mod.Source, "document", null, scenePatchFile.Result.NotApplicable, FailureCause.NotApplicable, true);
                }
                catch (Exception e) { scenePatchFile.PresentationError = "Failure report delivery failed: " + e.Message; }
                try { ObserveOverlap(mod, scenePatchFile.Result, notices); }
                catch (Exception e) { scenePatchFile.PresentationError = (scenePatchFile.PresentationError == null ? "" : scenePatchFile.PresentationError + "; ") + "Overlap notice delivery failed: " + e.Message; }
            }
            return scenePatchFiles;
        }
        sealed class ByReference : IEqualityComparer<object> {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
        sealed class Contribution { internal string Key; internal TargetIdentity Target; internal readonly List<LoadedMod> Mods = new List<LoadedMod>(); }
        // Supplying mods per uniquely resolved object in this loaded world, in load order, and the notices already given.
        readonly Dictionary<object, Contribution> contributions = new Dictionary<object, Contribution>(new ByReference());
        readonly HashSet<string> noticed = new HashSet<string>(StringComparer.Ordinal);
        // An outcome carries its resolved object only after unique resolution, so ambiguous or missing targets never count.
        void ObserveOverlap(LoadedMod mod, ScenePatchFileResult result, IOverlapNotices notices) {
            Exception fault = null;
            foreach (var outcome in result.Outcomes.Values) {
                if (outcome.ResolvedTarget == null || outcome.Target == null) continue;
                if (!contributions.TryGetValue(outcome.ResolvedTarget, out var contribution)) {
                    contribution = new Contribution { Target = outcome.Target, Key = "overlap/" + contributions.Count };
                    contributions.Add(outcome.ResolvedTarget, contribution);
                }
                if (contribution.Mods.Any(p => p.Source.Identifier == mod.Source.Identifier)) continue;
                contribution.Mods.Add(mod);
                if (contribution.Mods.Count < 2 || notices == null) continue;
                var signature = contribution.Key + "/" + String.Join("\n", contribution.Mods.Select(p => p.Source.Identifier));
                if (noticed.Contains(signature)) continue;
                try { notices.Notice(new OverlapNotice(contribution.Key, contribution.Target, contribution.Mods.ToArray())); noticed.Add(signature); }
                catch (Exception e) { fault = fault ?? e; }
            }
            if (fault != null) throw fault;
        }
        public ScenePatchFileResult Apply(string json, PatchSource source) {
            ClearSource(source);
            var result = engine.Apply(json, source.Identifier);
            ReportResult(result, source);
            return result;
        }
        public ScenePatchFileResult BeforeNativeCheck(string json, PatchSource source, TargetIdentity target) {
            var result = engine.BeforeNativeCheck(json, source.Identifier, target);
            if (result.Error != null) ClearSource(source);
            ReportResult(result, source);
            return result;
        }
        public ScenePatchFileResult BeforeNewGameStart(string json, PatchSource source) {
            var result = engine.BeforeNewGameStart(json, source.Identifier);
            if (result.Error != null) ClearSource(source);
            ReportResult(result, source);
            return result;
        }
        void ClearSource(PatchSource source) {
            foreach (var key in latest.Keys.Where(k => k.StartsWith(Key(source, ""), StringComparison.Ordinal)).ToArray()) latest.Remove(key);
        }
        // Records every outcome before delivery, and attempts every report even when one delivery fails.
        void ReportResult(ScenePatchFileResult result, PatchSource source) {
            foreach (var item in result.Outcomes) latest[Key(source, item.Key)] = item.Value;
            Exception fault = null;
            void Deliver(Action report) { try { report(); } catch (Exception e) { fault = fault ?? e; } }
            if (result.Error != null && !result.MapEditorOnly) Deliver(() => Report(source, "document", null, result.Error, FailureCause.Unknown, true));
            foreach (var item in result.Outcomes)
                if (!item.Value.Applied && !item.Value.MapEditorOnly) Deliver(() => Report(source, item.Key, item.Value.Target, WithRemaining(item.Value), item.Value.Cause));
            if (fault != null) throw fault;
        }
        // Removes only the unchanged row attributed at the native check, after a separate observation of repaired state.
        // No window or historical-log operations are exposed here. Manual closure is safe when window ownership is unknown.
        public bool Reconcile(PatchSource source, string operation, WarningAttribution attribution, ICurrentWarnings warnings, Func<bool> verifyRepair) {
            if (attribution?.Warning == null || attribution.Target == null || String.IsNullOrWhiteSpace(attribution.EarlyRouteUnavailable) ||
                !latest.TryGetValue(Key(source, operation), out var outcome) || !outcome.Applied || !ReferenceEquals(outcome.ResolvedTarget, attribution.Target)) return false;
            var row = Exact(warnings, attribution.Warning);
            if (row == null) return false;
            try { if (!verifyRepair()) return false; } catch { return false; }
            if (!latest.TryGetValue(Key(source, operation), out var current) || !ReferenceEquals(outcome, current)) return false;
            row = Exact(warnings, attribution.Warning);
            if (row == null) return false;
            warnings.Remove(row);
            return true;
        }
        static CurrentWarning Exact(ICurrentWarnings warnings, CurrentWarning original) {
            var matches = warnings.Current.Where(r => r.Source == original.Source && r.Key == original.Key).ToArray();
            if (matches.Length != 1) return null;
            var row = matches[0];
            return ReferenceEquals(row.Token, original.Token) && row.Originator == original.Originator && row.Description == original.Description ? row : null;
        }
        // An incomplete cleanup reports the state it left behind, so the report states what remains uncertain.
        static string WithRemaining(Outcome outcome) => outcome.Remaining.Count == 0 || outcome.Failure != FailureKind.IncompleteCleanup ? outcome.Error
            : outcome.Error.TrimEnd('.') + ". Remaining state: " + String.Join("; ", outcome.Remaining);
        void Report(PatchSource source, string operation, TargetIdentity target, string error, FailureCause cause, bool scenePatchFile = false) {
            var key = (scenePatchFile ? "document/" : "operation/") + Key(source, operation);
            if (reported.Contains(key)) return;
            diagnostics.Warning(new PatchFailure(source, operation, target, error, cause, patcherVersion, gameVersion, key));
            reported.Add(key);
        }
    }
}
