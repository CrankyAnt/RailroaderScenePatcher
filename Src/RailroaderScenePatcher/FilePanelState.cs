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
        internal const string UseFileEffect = "Applies the file immediately and discards your unsaved scene patch edits. Their steps remain in Undo and Redo but no longer change anything. Your native map edits are unaffected. No reload is needed.";
        internal const string KeepEditorEffect = "Keeps your scene patch edits and lets you save again. The next Save replaces the file with the editor version.";
        internal const string ImportEffect = "Shows the file's changes and any conflicts with your edits before applying anything. Your other edits keep their values.";
        internal static string RepairKeepEffect(PatchEditingSession session) => session.VersionFileMissing
            ? "There is no file to back up. You can save again. The next Save creates the file from the editor version."
            : session.BackupPreview == null ? "The backup path is unavailable."
            : "Writes a backup to " + session.BackupPreview + ". You can then save again. The next Save replaces the file with the editor version.";
        internal const string ImportOutdated = "Import closed because the file changed again. Review the updated version choice.";
        internal const string ImportIntro = "Import applies all changes below and your choices for all conflicts together. It adds no Undo step. Nothing is written until you click Save in the Map Editor.";
        internal static string KindLabel(ImportChangeKind kind) => kind == ImportChangeKind.New ? "New" : kind == ImportChangeKind.Removed ? "Removed" : "Changed";
        internal static string ChoiceLabel(bool useFile) => useFile ? "File version" : "Editor version";
        // The message of a completed Import, for the footer message area.
        // unsaved says whether the patch work now differs from the file on disk.
        internal static string Completed(ImportResult result, bool unsaved) {
            var parts = new[] {
                result.Changes == 0 ? "No changes applied" : result.Changes == 1 ? "1 change applied" : result.Changes + " changes applied",
                result.FileChoices == 0 ? null : result.FileChoices == 1 ? "file version used for 1 conflict" : "file version used for " + result.FileChoices + " conflicts",
                result.EditorChoices == 0 ? null : result.EditorChoices == 1 ? "editor version kept for 1 conflict" : "editor version kept for " + result.EditorChoices + " conflicts"
            }.Where(p => p != null).ToArray();
            return "Import completed. " + String.Join(", ", parts) + "."
                + (result.NotApplied.Count == 0 ? "" : " Kept in the scene patch file but not applied: " + String.Join("; ", result.NotApplied) + ".")
                + (unsaved ? " Click Save in the Map Editor to write the scene patch file." : " Your scene patch edits match the file on disk.");
        }
    }
}
