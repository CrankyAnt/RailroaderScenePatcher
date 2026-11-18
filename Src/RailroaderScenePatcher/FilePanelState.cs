// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
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
        internal static string Heading(Kind kind) => kind == Kind.VersionChoice ? "Version choice" : kind == Kind.Repair ? "Repair" : kind == Kind.Import ? "Import" : null;
        // Why a control outside the panel cannot be used, as its "Unavailable:" tooltip gives it, or null.
        internal static string Reason(Kind kind) =>
            kind == Kind.VersionChoice ? "Choose a version in the Version choice panel first."
            : kind == Kind.Repair ? "The scene patch file needs repair. Use the Repair panel first."
            : kind == Kind.Import ? "Complete or cancel Import first."
            : null;
        internal const string UseFile = "Use file version", KeepEditor = "Keep editor version", Import = "Import", Details = "Details", Copy = "Copy to clipboard", Cancel = "Cancel";
        // What each choice does, stated before it is taken.
        internal const string UseFileEffect = "Applies the file now and discards your unsaved scene patch edits. Their steps stay in Undo and Redo and change nothing. Native map work is unaffected. No reload is needed.";
        internal const string KeepEditorEffect = "Keeps your scene patch edits. Save is no longer refused, and the next Save replaces the file with the editor version.";
        internal const string ImportEffect = "Shows the file's changes and any conflicts with your edits before anything is applied. Your other edits keep their values.";
        internal static string RepairKeepEffect(PatchEditingSession session) => session.VersionFileMissing
            ? "There is no file to back up. Save is no longer refused, and the next Save creates the file from the editor version."
            : session.BackupPreview == null ? "The backup path is unavailable."
            : "A backup will be written at " + session.BackupPreview + ". Then Save is no longer refused, and the next Save replaces the file with the editor version.";
        internal const string ImportOutdated = "Import closed. The file changed again; review the updated version choice.";
        internal const string ImportIntro = "Import applies every change below and your choice for each conflict at once. Nothing is written until you click Save in the Map Editor, and Import adds no Undo step.";
        internal static string KindLabel(ImportChangeKind kind) => kind == ImportChangeKind.New ? "New" : kind == ImportChangeKind.Removed ? "Removed" : "Changed";
        internal static string ChoiceLabel(bool useFile) => useFile ? "File version" : "Editor version";
        // The message of a completed Import, for the footer message area.
        // unsaved says whether the patch work now differs from the file on disk.
        internal static string Completed(ImportResult result, bool unsaved) {
            var parts = new[] {
                result.Changes == 0 ? "No changes applied" : result.Changes == 1 ? "1 change applied" : result.Changes + " changes applied",
                result.FileChoices == 0 ? null : result.FileChoices == 1 ? "1 conflict took the file version" : result.FileChoices + " conflicts took the file version",
                result.EditorChoices == 0 ? null : result.EditorChoices == 1 ? "1 conflict kept the editor version" : result.EditorChoices + " conflicts kept the editor version"
            }.Where(p => p != null).ToArray();
            return "Import completed. " + String.Join(", ", parts) + "."
                + (result.NotApplied.Count == 0 ? "" : " Kept in the scene patch file but not applied: " + String.Join("; ", result.NotApplied) + ".")
                + (unsaved ? " Click Save in the Map Editor to write the scene patch file." : " The scene patch work matches the file on disk.");
        }
    }
}
