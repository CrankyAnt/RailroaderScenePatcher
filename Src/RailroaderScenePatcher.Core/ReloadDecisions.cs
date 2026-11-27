// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    public enum ReloadChoice { Cancel, Save, Discard }

    public sealed partial class PatchEditingSession {
        public string ReloadPrompt(bool nativeDirty) {
            var warning = ReloadWarning(nativeDirty, out var changes) ?? "All required work is saved.";
            string choices;
            if (!CanSaveForReload) choices = warning + " Resolve the scene patch file version choice before saving.";
            else if (changes == null) choices = warning;
            else choices = "Save and Reload saves changes to the " + changes + " before reloading. " + warning;
            return "Reload the Map Editor? Reloading ends the Map Editor's Undo and Redo history. "
                + choices
                + " Cancel keeps the Map Editor open. Your working location is restored when available.";
        }
        public string ReloadWarning(bool nativeDirty) => ReloadWarning(nativeDirty, out _);
        // Whether a reload now discards unsaved work. Only then does the prompt offer Save and Reload and Discard and
        // Reload; with everything saved it offers Cancel and Reload, as the native Exit prompt offers Cancel and Exit.
        public bool ReloadDiscardsWork(bool nativeDirty) => Outstanding(nativeDirty).Count != 0;
        string ReloadWarning(bool nativeDirty, out string changes) {
            var outstanding = Outstanding(nativeDirty);
            changes = outstanding.Count == 0 ? null : OutstandingWriteList(outstanding);
            if (changes == null) return SaveRefusal;
            var warning = "Discard and Reload discards unsaved changes to the " + changes + ".";
            if (IncompleteOperation != null) warning += " " + IncompleteOperation;
            if (SaveRefusal != null) warning += " " + SaveRefusal;
            return warning;
        }
        public string ExitPrompt(bool nativeDirty) {
            var outstanding = Outstanding(nativeDirty);
            var unsaved = outstanding.Count != 0;
            if (!unsaved && CanSaveForReload) return null;
            var incomplete = unsaved && IncompleteOperation != null ? " " + ScenePatchFile.Sentence(IncompleteOperation) + "." : "";
            var pending = VersionState == FileVersionState.Changed ? " A file version choice is still pending."
                : VersionState == FileVersionState.Repair ? " The scene patch file still needs repair." : "";
            var consequence = unsaved ? " Exit discards unsaved changes to the " + OutstandingWriteList(outstanding) + "."
                : " Exit discards unsaved work.";
            return "Exit the Map Editor?" + incomplete + pending + consequence;
        }
        // Live scene edits and outside file choices need no reload. An unrecovered native
        // history cannot resume recording coherent edits until the native map editor is reloaded.
        public string ReloadRequirement => unrecovered;

        // Called after the explicit confirmation. The caller keeps this session attached
        // until the native reload replaces it; Save uses the ordinary native Save route.
        public string Reload(ReloadChoice choice, Func<bool> nativeDirty, Action save, Action reload) {
            if (choice == ReloadChoice.Cancel) return null;
            try {
                if (choice == ReloadChoice.Save) {
                    CheckFileVersion();
                    if (!CanSaveForReload) return "Reload cancelled. " + SaveRefusal;
                    save();
                    var warning = ExitWarning(nativeDirty());
                    if (warning != null) return "Reload cancelled. " + warning + (LastSave?.Error == null ? "" : " Last Save failed: " + LastSave.Error
                        + ". Completed writes: " + (LastSave.Completed.Count == 0 ? "none" : String.Join(", ", LastSave.Completed.Select(DisplayItem))) + ".");
                } else if (choice != ReloadChoice.Discard) return "Reload cancelled. The decision was not recognized.";
                reload();
                return null;
            } catch (Exception e) { return "Reload cancelled. " + PatcherLog.Reason(e); }
        }
    }
}
