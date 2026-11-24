// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

namespace RailroaderScenePatcher.Core {
    // The failures of scene patch editing in the native map editor that would otherwise reach only the Scene Patcher
    // window, a game alert or no log. Each is a patcher failure, written once as a player error.
    public enum EditingFailure {
        // A Map Editor reload is refused while an earlier return to the working location is still pending.
        ReloadPending,
        // The reload stopped with the reason scene patch editing gives, such as a failed save.
        ReloadStopped,
        // The reload request failed.
        ReloadCancelled,
        // The return to the working location after a reload did not complete in time.
        ReturnTimedOut,
        // The Map Editor reloaded, but the working location could not be restored.
        LocationNotRestored,
        // Outstanding work could not be determined.
        OutstandingUnknown,
        // A native Undo or Redo step failed. It reaches the log only.
        HistoryError,
        // An exit was cancelled because outstanding work could not be confirmed.
        ExitCancelled,
        // The scene patch file could not be checked against the file on disk.
        FileCheck,
        // Cleanup after the gizmo could not be verified.
        GizmoCleanup,
        // A Map Editor hook is unavailable on this game build; the detail is its text.
        HookUnavailable,
        // A change of the Map Editor history could not be tracked.
        HistoryTracking,
        // The Map Editor's title and Save button could not be refreshed.
        ChromeRefresh,
        // A Map Editor history step could not be checked.
        HistoryCheck,
    }

    // The red line of the Scene Patcher window for one attached native map editor. A failure is a patcher failure: it
    // stands in the window and is written once per text as a player error. A refusal answers an editing action: it stands
    // in the window and writes no player line; the edit feedback that reports it writes its debug line.
    public sealed class EditingFault {
        readonly LogOnce written = new LogOnce();
        public string Text { get; private set; }
        public static string Describe(EditingFailure failure, string detail) {
            switch (failure) {
                case EditingFailure.ReloadPending:
                case EditingFailure.ReloadStopped:
                case EditingFailure.HookUnavailable: return detail;
                case EditingFailure.ReloadCancelled: return "Reload cancelled: " + detail;
                case EditingFailure.ReturnTimedOut: return "The return to your working location did not complete within five minutes. Check the game's load diagnostics before trying again.";
                case EditingFailure.LocationNotRestored: return "The Map Editor reloaded, but your working location could not be restored: " + detail;
                case EditingFailure.OutstandingUnknown: return "Outstanding work could not be determined; the native map editor is treated as unsaved: " + detail;
                case EditingFailure.HistoryError: return "A Map Editor Undo or Redo step failed, so scene patch editing may not match the scene: " + detail;
                case EditingFailure.ExitCancelled: return "Exit cancelled because outstanding work could not be confirmed. " + detail;
                case EditingFailure.FileCheck: return "Could not check the scene patch file: " + detail;
                case EditingFailure.GizmoCleanup: return "The patcher could not verify cleanup after using the gizmo. Reload the Map Editor. " + detail;
                case EditingFailure.HistoryTracking: return "Could not track a change to the Map Editor history: " + detail;
                case EditingFailure.ChromeRefresh: return "Native map editor title and Save button could not be refreshed: " + detail;
                case EditingFailure.HistoryCheck: return "The Map Editor history step couldn't be checked: " + detail;
                default: throw new ArgumentOutOfRangeException(nameof(failure));
            }
        }
        public void Fail(EditingFailure failure, string detail = null) {
            var text = Describe(failure, detail);
            if (failure != EditingFailure.HistoryError) Text = text;
            written.Error(text, text);
        }
        public void Refuse(string text) { Text = text; }
        public void Clear() { Text = null; }
    }
}
