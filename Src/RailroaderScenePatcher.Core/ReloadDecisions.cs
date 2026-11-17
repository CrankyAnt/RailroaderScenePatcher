// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

namespace RailroaderScenePatcher.Core {
    public enum ReloadChoice { Cancel, Save, Discard }

    public sealed partial class PatchEditingSession {
        public string ReloadWarning(bool nativeDirty) {
            var warning = ExitWarning(nativeDirty);
            return CanSaveForReload ? warning : String.IsNullOrEmpty(warning) ? SaveRefusal : warning + " " + SaveRefusal;
        }
        public string ExitPrompt(bool nativeDirty) {
            var warning = ReloadWarning(nativeDirty);
            return warning == null ? null : "Exit the Map Editor? " + warning + " Exit discards unsaved work. Cancel returns to the Map Editor.";
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
                        + ". Completed writes: " + (LastSave.Completed.Count == 0 ? "none" : String.Join(", ", LastSave.Completed)) + ".");
                } else if (choice != ReloadChoice.Discard) return "Reload cancelled. The decision was not recognized.";
                reload();
                return null;
            } catch (Exception e) { return "Reload cancelled. " + e.Message; }
        }
    }
}
