// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

namespace RailroaderScenePatcher.Core {
    // A refusal of an editing action: the action stops before it changes anything, and its reason reaches the footer
    // and the debug log, not the player log. Any other exception of the action is a patcher failure. Where a step is a
    // check, Check turns every exception of that step into a refusal, so the step decides, not the exception's type.
    public sealed class EditRefusal : Exception {
        public EditRefusal(string message, Exception cause = null) : base(message, cause) { }
        public static T Check<T>(Func<T> check) {
            try { return check(); }
            catch (EditRefusal) { throw; }
            catch (Exception e) when (!(e is OutOfMemoryException)) { throw new EditRefusal(e.GetBaseException().Message, e); }
        }
        public static void Check(Action check) => Check<object>(() => { check(); return null; });
    }

    // The player errors of one gizmo drag: the first failure of a drag is written as a player error and stands in the
    // window; any later failure of the same drag adds none. Begin starts the next drag.
    public sealed class DragFault {
        readonly EditingFault fault;
        bool written;
        public DragFault(EditingFault fault) { this.fault = fault ?? throw new ArgumentNullException(nameof(fault)); }
        public void Begin() => written = false;
        // Whether this call wrote the failure.
        public bool Fail(EditingFailure failure, string detail, string subject) {
            if (written) return false;
            written = true;
            fault.Fail(failure, detail, subject);
            return true;
        }
    }
}
