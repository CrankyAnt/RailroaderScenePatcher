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
        // The restoration of the prior placement after a cancelled gizmo drag could not be verified; the detail is the
        // gesture's cleanup failure.
        GizmoRestore,
        // A gizmo drag preview failed with an exception outside the gesture's own checks.
        GizmoPreview,
        // The gizmo could not be shown.
        GizmoUnavailable,
        // A Map Editor hook is unavailable on this game build; the detail is its text.
        HookUnavailable,
        // A change of the Map Editor history could not be tracked.
        HistoryTracking,
        // The Map Editor's title and Save button could not be refreshed.
        ChromeRefresh,
        // A Map Editor Undo or Redo step could not be checked.
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
                case EditingFailure.ReturnTimedOut: return "The return to your working location timed out after five minutes. " + WindowGuidance.CheckPlayerLog;
                case EditingFailure.LocationNotRestored: return "The Map Editor reloaded, but your working location could not be restored: " + detail;
                case EditingFailure.OutstandingUnknown: return "The patcher couldn't check for unsaved changes. It treats the Map Editor as unsaved: " + detail;
                case EditingFailure.HistoryError: return "A Map Editor Undo or Redo step failed, so scene patch editing may not match the scene: " + detail;
                case EditingFailure.ExitCancelled: return "The Map Editor stayed open because the patcher couldn't check for unsaved changes. " + detail;
                case EditingFailure.FileCheck: return "Could not check the scene patch file: " + detail;
                case EditingFailure.GizmoRestore: return detail;
                case EditingFailure.GizmoPreview: return "The gizmo drag failed: " + detail;
                case EditingFailure.GizmoUnavailable: return "The gizmo couldn't be shown: " + detail;
                case EditingFailure.GizmoCleanup: return "The patcher couldn't confirm removal of the gizmo's temporary changes. " + WindowGuidance.ReloadMapEditor + " " + detail;
                case EditingFailure.HistoryTracking: return "The patcher couldn't track an Undo or Redo history change in the Map Editor: " + detail;
                case EditingFailure.ChromeRefresh: return "The Map Editor title and Save button couldn't be updated: " + detail;
                case EditingFailure.HistoryCheck: return "The Map Editor Undo or Redo step couldn't be checked: " + detail;
                default: throw new ArgumentOutOfRangeException(nameof(failure));
            }
        }
        // subject is the scene patch file or object the failure concerns, as the log names it; the window line stands
        // beside that file or object already.
        public void Fail(EditingFailure failure, string detail = null, string subject = null) {
            var text = Describe(failure, detail);
            if (failure != EditingFailure.HistoryError) Text = text;
            var sentence = text.TrimEnd();
            if (!sentence.EndsWith(".", StringComparison.Ordinal) && !sentence.EndsWith("!", StringComparison.Ordinal) && !sentence.EndsWith("?", StringComparison.Ordinal)) sentence += ".";
            var line = (String.IsNullOrEmpty(subject) ? "" : subject + ": ") + sentence + " Report this to the patcher maintainer.";
            written.Error(line, line);
        }
        public void Refuse(string text) { Text = text; }
        public void Clear() { Text = null; }
    }
}
