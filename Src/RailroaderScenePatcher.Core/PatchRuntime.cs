// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    public enum FailureCause { Unknown, InvalidDefinition, PatcherFault }
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
        internal PatchFailure(PatchSource pack, string operation, TargetIdentity target, string error, FailureCause cause, string patcherVersion, string gameVersion, string diagnosticKey) {
            DiagnosticKey = diagnosticKey; PackIdentifier = pack.Identifier; Operation = operation; ObjectPath = target?.Path; Cause = cause;
            ReportTo = cause == FailureCause.InvalidDefinition ? "MapMod author" : "patcher maintainer";
            Message = (pack.Name ?? pack.Identifier) + " [" + pack.Identifier + "; version " + (pack.Version ?? "not supplied") + "]. Definition Data/scene-patches.json; operation " + operation +
                "; object " + (target == null ? "not resolved" : target.Scene + "/" + target.Path + " [" + target.Component + "]") + ". " + error +
                ". Patcher " + patcherVersion + "; game " + gameVersion + ". Report to the " + ReportTo + ".";
            if (!String.IsNullOrWhiteSpace(pack.Author)) Message += " Supplied author: " + pack.Author + ".";
            if (Uri.TryCreate(pack.Website, UriKind.Absolute, out var site) && (site.Scheme == "https" || site.Scheme == "http")) Message += " Published website: " + pack.Website + ".";
        }
        public string Source => "Railroader Scene Patcher";
        public string PackIdentifier { get; }
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
        public DocumentResult Apply(string json, PatchSource source) {
            ClearSource(source);
            var result = engine.Apply(json, source.Identifier);
            ReportResult(result, source);
            return result;
        }
        public DocumentResult BeforeNativeCheck(string json, PatchSource source, TargetIdentity target) {
            var result = engine.BeforeNativeCheck(json, source.Identifier, target);
            if (result.Error != null) ClearSource(source);
            ReportResult(result, source);
            return result;
        }
        void ClearSource(PatchSource source) {
            foreach (var key in latest.Keys.Where(k => k.StartsWith(Key(source, ""), StringComparison.Ordinal)).ToArray()) latest.Remove(key);
        }
        void ReportResult(DocumentResult result, PatchSource source) {
            if (result.Error != null && !result.EditorOnly) Report(source, "document", null, result.Error, FailureCause.Unknown, true);
            foreach (var item in result.Outcomes) {
                latest[Key(source, item.Key)] = item.Value;
                if (!item.Value.Applied && !item.Value.EditorOnly) Report(source, item.Key, item.Value.Target, item.Value.Error, item.Value.Cause);
            }
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
        void Report(PatchSource source, string operation, TargetIdentity target, string error, FailureCause cause, bool document = false) {
            var key = (document ? "document/" : "operation/") + Key(source, operation);
            if (reported.Contains(key)) return;
            diagnostics.Warning(new PatchFailure(source, operation, target, error, cause, patcherVersion, gameVersion, key));
            reported.Add(key);
        }
    }
}
