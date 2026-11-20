// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The file panel: while the scene patch file waits for a version choice, needs repair or is being imported, this panel
    // takes the place of the inspector tabs and tab body, and every other Scene Patcher control except Refresh is
    // unavailable with the panel's reason. Its heading names the state. UI-independent, shared with the core tests.
    internal static class FilePanelState {
        internal enum Kind { None, VersionChoice, Repair, Import }
        internal static Kind Of(PatchEditingSession session) {
            if (session == null) return Kind.None;
            if (session.Import != null) return Kind.Import;
            switch (session.VersionState) {
                case FileVersionState.Changed: return Kind.VersionChoice;
                case FileVersionState.Repair: return Kind.Repair;
                default: return Kind.None;
            }
        }
        // The tooltip title of an outliner row and of its disclosure while the panel is open: the first line of the row's own
        // tooltip, its object's name, or else the row's label, such as a folder's count and name; the reason follows as the
        // tooltip's text.
        internal static string RowTitle(string tooltip, string label) {
            var first = (tooltip ?? "").Split('\n')[0].Trim();
            return first.Length != 0 ? first : (label ?? "").Trim();
        }
        // Whether a click on a file panel button drawn for this panel and file version comes late: a choice already
        // closed or replaced the panel, or the panel now stands for another file version. Use file version runs in one
        // frame, which can take seconds, and a second click during it reaches the button as it was drawn. A late click
        // does nothing; the result of the first one stays in the footer.
        internal static bool Late(Kind drawnFor, string drawnVersion, PatchEditingSession session) =>
            session == null || Of(session) != drawnFor || !String.Equals(session.ObservedVersion, drawnVersion, StringComparison.Ordinal);
        internal static string Heading(Kind kind) => kind == Kind.VersionChoice ? "Version choice" : kind == Kind.Repair ? "Repair" : kind == Kind.Import ? "Import" : null;
        // Why a control outside the panel cannot be used, as its "Unavailable:" tooltip gives it, or null.
        internal static string Reason(Kind kind) =>
            kind == Kind.VersionChoice ? "Choose a version in the Version choice panel first."
            : kind == Kind.Repair ? "The scene patch file needs repair. Use the Repair panel first."
            : kind == Kind.Import ? "Complete or cancel Import first."
            : null;
        internal const string UseFile = "Use file version", KeepEditor = "Keep editor version", Import = "Import", Details = "Details", Copy = "Copy to clipboard", Cancel = "Cancel";
        // What each choice does, stated before it is taken.
        internal const string UseFileEffect = "Applies the file immediately and discards your unsaved scene patch edits. Their steps remain in Undo and Redo but no longer change anything. Your other map edits stay as they are. No reload is needed.";
        internal const string KeepEditorEffect = "The next Save replaces the file with the editor version.";
        internal const string ImportEffect = "Shows the file's changes and any conflicts with your edits before applying anything. Your other edits keep their values.";
        internal static string RepairKeepEffect(PatchEditingSession session) => session.VersionFileMissing
            ? "There is no file to back up. The next Save creates the file from the editor version."
            : session.BackupPreview == null ? "The backup path is unavailable."
            : "Writes a backup to " + session.BackupPreview + ". " + KeepEditorEffect;
        internal const string ImportOutdated = "Import closed because the file changed again. Review the updated version choice.";
        internal const string ImportIntro = "Import applies all changes below and your choices for all conflicts together. It adds no Undo step. Nothing is written until you click Save in the Map Editor.";
        internal static string KindLabel(ImportChangeKind kind) => kind == ImportChangeKind.New ? "New" : kind == ImportChangeKind.Removed ? "Removed" : "Changed";
        internal static string ChoiceLabel(bool useFile) => useFile ? "File version" : "Editor version";
        // The message of Use file version, for the footer message area: in the form of a completed Import, it counts the
        // applied modifications and those that did not apply by failure kind, and names each with its reason. A refused
        // modification stays in the scene patch file but is not applied; the other kinds keep their own label, so an
        // incomplete cleanup never reads as kept but not applied.
        internal static string FileVersionApplied(ScenePatchFileResult result) =>
            result == null ? FileVersionApplied(null, 0, Array.Empty<KeyValuePair<string, (FailureKind, string)>>())
            : FileVersionApplied(result.Error, result.Outcomes?.Count(o => o.Value.Applied) ?? 0,
                (result.Outcomes ?? new Dictionary<string, Outcome>()).Where(o => !o.Value.Applied).Select(o => new KeyValuePair<string, (FailureKind, string)>(o.Key, (o.Value.Failure, o.Value.Error))).ToArray());
        internal static string FileVersionApplied(string fileError, int applied, IReadOnlyList<KeyValuePair<string, (FailureKind Kind, string Reason)>> failed) {
            string Plain(string text) => (text ?? "").Trim().TrimEnd('.').TrimEnd();
            if (fileError != null) return "File version applied. Its modifications are not applied: " + Plain(fileError) + ".";
            if (failed == null || failed.Count == 0) return "File version applied.";
            string Count(int n) => n == 0 ? "No modifications" : n == 1 ? "1 modification" : n + " modifications";
            string Lower(string label) => Char.ToLowerInvariant(label[0]) + label.Substring(1);
            var groups = failed.GroupBy(f => f.Value.Kind).OrderBy(g => (int)g.Key).ToArray();
            var counts = new[] { Count(applied) + " applied" }.Concat(groups.Select(g => Count(g.Count()) + " " + Lower(FailureLabels.Of(g.Key))));
            var lists = groups.Select(g => (g.Key == FailureKind.Refused ? "Kept in the scene patch file but not applied" : FailureLabels.Of(g.Key)) + ": "
                + String.Join("; ", g.Select(f => f.Key + " (" + Plain(f.Value.Reason) + ")")) + ".");
            return "File version applied. " + String.Join(", ", counts) + ". " + String.Join(" ", lists);
        }
        // The message of a completed Import, for the footer message area.
        // unsaved says whether the patch work now differs from the file on disk.
        // A conflict resolved with the file version changed the entry, so it counts as an applied change; the conflicts kept
        // at the editor version changed nothing and are counted apart.
        internal static string Completed(ImportResult result, bool unsaved) {
            var applied = result.Changes + result.FileChoices;
            var parts = new[] {
                applied == 0 ? "No changes applied" : applied == 1 ? "1 change applied" : applied + " changes applied",
                result.EditorChoices == 0 ? null : result.EditorChoices == 1 ? "1 conflict kept at the editor version" : result.EditorChoices + " conflicts kept at the editor version"
            }.Where(p => p != null).ToArray();
            return "Import completed. " + String.Join(", ", parts) + "."
                + (result.NotApplied.Count == 0 ? "" : " Kept in the scene patch file but not applied: " + String.Join("; ", result.NotApplied) + ".")
                + (unsaved ? " Click Save in the Map Editor to write the scene patch file." : " Your scene patch edits match the file on disk.");
        }
    }
}
