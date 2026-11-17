// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The result of the last action, for the message area and for the log. An edit that is refused or does not run
    // always leaves its reason here. A result describes the present: it goes when another object is inspected, when a
    // new action reports its own result, and when the native map editor's history moves by Undo or Redo, after which "Enable
    // completed" would be untrue. A result of a tab's own action also goes when the open tab or placement mode
    // changes; a result of a general action, one that is the same on every tab, stays through that change. A refusal
    // reported after the inspection was cleared stays until an object is inspected again. UI-independent, shared with
    // controlled replay.
    internal sealed class EditFeedback {
        internal enum PrimaryMessage { None, VersionChoice, BackupDiagnosis, Working, WorldChoice }
        internal static PrimaryMessage Primary(PatchEditingSession session, bool working, bool worldChoice) =>
            Primary(session != null && session.VersionState != FileVersionState.None,
                session?.LastBackup != null && session.VersionMessage != null && session.ScenePatchFileDiagnostics.Count != 0, working, worldChoice);
        internal static PrimaryMessage Primary(bool versionChoice, bool backupDiagnosis, bool working, bool worldChoice) =>
            versionChoice ? PrimaryMessage.VersionChoice : working ? PrimaryMessage.Working
            : worldChoice ? PrimaryMessage.WorldChoice : backupDiagnosis ? PrimaryMessage.BackupDiagnosis : PrimaryMessage.None;
        // One result with the action group it came from, such as Placement or Track binding.
        internal sealed class Message {
            internal string Source { get; }
            internal string Text { get; }
            // A consequence that follows a result by itself, such as terrain output after an edit, not the result of an
            // action the user selected.
            internal bool FollowUp { get; }
            // Reported by a general place: it does not belong to one tab or mode.
            internal bool General { get; }
            // Reported while nothing was inspected after the inspection was cleared: it stays until the next inspection.
            internal bool Kept { get; }
            // The correction guidance of a failed action, with the guide of its operation, or null.
            internal Recovery Recovery { get; set; }
            internal Message(string source, string text, bool followUp = false, bool general = false, bool kept = false) { Source = source; Text = text; FollowUp = followUp; General = general; Kept = kept; }
        }
        // The guidance of the newest failed action still shown, or null. It goes with its message.
        internal Recovery Recovery => messages.Select(m => m.Recovery).FirstOrDefault(r => r != null);
        // The places whose actions are the same on every tab, such as the footer's Remove modification and Recheck.
        // Every other place belongs to the tab and mode its result was reported in.
        internal readonly System.Collections.Generic.HashSet<string> GeneralPlaces = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        Message New(string place, string message, bool followUp) => new Message(place, message ?? "", followUp, GeneralPlaces.Contains(place), ReferenceEquals(inspected, released));
        readonly System.Collections.Generic.List<Message> messages = new System.Collections.Generic.List<Message>();
        // The newest message first, which is the order the message area shows them in: the result of the last action
        // and, above it, the latest follow-up of that action, such as its terrain output.
        internal System.Collections.Generic.IReadOnlyList<Message> Messages => messages;
        // The action group of the newest message, and its text.
        internal string Place => messages.Count == 0 ? null : messages[0].Source;
        internal string Result => messages.Count == 0 ? "" : messages[0].Text;
        internal Action<string> Log;
        string pendingHistoryNotice;
        internal void QueueHistoryNotice(string message) { pendingHistoryNotice = message; }
        internal void FlushHistoryNotice() { if (pendingHistoryNotice == null) return; var message = pendingHistoryNotice; pendingHistoryNotice = null; Report("Undo/Redo", message); }
        object inspected;
        // A result is about the object inspected when it was reported: inspecting another object clears it. An object
        // that is no longer present clears nothing.
        internal void Inspecting(object handle) {
            if (handle == null || ReferenceEquals(handle, inspected)) return;
            if (inspected != null) messages.Clear();
            inspected = handle;
        }
        // Nothing is inspected any more: the messages about the object go. What is reported after this, such as the
        // refusal of a request that waited for the object, stays until an object is inspected again.
        internal void Released() { messages.Clear(); inspected = released; }
        static readonly object released = new object();
        // Refresh removes earlier results without changing the inspected object or view.
        internal void Refreshed() { messages.Clear(); }
        // A new load attempt starts without the messages of the one before.
        internal void Clear() { messages.Clear(); pendingHistoryNotice = null; inspected = null; context = null; observedSession = null; observedState = FileVersionState.None; observedMessage = null; observedBackup = null; }
        string context;
        // The view a result was reported in: the open tab and, on the Placement tab, its mode. A result of a tab's
        // action in another view no longer describes what is on screen, so a change of view removes it. A general
        // result and a refusal kept for the next inspection stay. Restoring a view can preserve the current result.
        internal void View(string key, bool preserve = false) {
            if (key == context) return;
            if (context != null && !preserve) messages.RemoveAll(m => !m.General && !m.Kept);
            context = key;
        }
        // The native map editor's history moved by Undo or Redo: what the last action completed may be undone. A refusal kept
        // for the next inspection completed nothing and stays.
        internal void HistoryMoved() { messages.RemoveAll(m => !m.Kept); }
        // A world choice starts: it is a new action, so the result or refusal of the action before it goes, as it
        // goes when another action reports. A refusal kept for the next inspection stays.
        internal void ChoiceStarted() { messages.RemoveAll(m => !m.Kept); }
        // The result of an action. It takes the place of the result before it and of that result's follow-ups.
        internal void Report(string place, string message) {
            messages.Clear();
            messages.Add(New(place, message, false));
            try { Log?.Invoke(Result); } catch { }
        }
        // Reports a follow-up of the last action. It replaces the follow-up before it and never the result.
        internal void Follow(string place, string message) {
            messages.RemoveAll(m => m.FollowUp);
            messages.Insert(0, New(place, message, true));
            try { Log?.Invoke(Result); } catch { }
        }
        // Track file state even when another caller performs the check, such as Save.
        PatchEditingSession observedSession;
        FileVersionState observedState;
        string observedMessage, observedBackup;
        internal bool ObserveFileVersion(PatchEditingSession session, string place) {
            var changed = !ReferenceEquals(observedSession, session) || observedState != session.VersionState
                || observedMessage != session.VersionMessage || observedBackup != session.LastBackup;
            if (changed)
                messages.RemoveAll(m => m.Source == place);
            observedSession = session; observedState = session.VersionState;
            observedMessage = session.VersionMessage; observedBackup = session.LastBackup;
            return changed;
        }
        internal bool CheckFileVersion(PatchEditingSession session, string place) {
            session.CheckFileVersion();
            return ObserveFileVersion(session, place);
        }
        // Removes the messages of one place, as a new form of that place starts.
        internal void ClearPlace(string place) { messages.RemoveAll(m => m.Source == place && !m.Kept); }
        internal void Withdraw(string place, string message) { messages.RemoveAll(m => m.Source == place && m.Text == message); }
        // A refusal reads as one label, the action group of the message, followed by plain sentences: what was refused,
        // then why (CommandBand.Sentence), without a second label before the reason.
        internal void Refuse(string place, string action, string reason) => Report(place, action + " refused. " + CommandBand.Sentence(reason));
        // Runs one edit bound to the inspected observation. blocked is the reason the panel cannot edit now, or null.
        // Pinning compares the whole scene first, so a stale or changed inventory refuses the edit before it runs.
        // pin supplies the inspection token; without it the edit pins and compares the whole scene in this call.
        internal bool Run(string place, string action, string blocked, SceneInventory inventory, SceneObservation selected, Func<InspectedTarget, Outcome> change, Func<SceneObservation, InspectedTarget> pin = null, string completed = null, Func<Outcome> guard = null) {
            if (blocked != null) { Refuse(place, action, blocked); return false; }
            if (inventory == null || selected == null) { Refuse(place, action, "no inspected object; refresh the inventory and inspect the object first"); return false; }
            // An object an earlier failure guards is refused for that failure before the pin compares the scene: an
            // incomplete cleanup leaves the object changed, so the comparison would call the inspection stale instead.
            Outcome guarded = null;
            try { guarded = guard?.Invoke(); } catch (Exception e) { Refuse(place, action, e.GetBaseException().Message); return false; }
            if (guarded != null && !guarded.Applied) { ReportFailure(place, action, selected, guarded); return false; }
            Outcome outcome;
            try { outcome = change(pin != null ? pin(selected) : inventory.PinForEdit(selected)); }
            catch (Exception e) { Refuse(place, action, e.GetBaseException().Message); return false; }
            if (outcome == null) { Refuse(place, action, "the edit returned no outcome"); return false; }
            if (!outcome.Applied) { ReportFailure(place, action, selected, outcome); return false; }
            // completed is the result of an edit that applied without doing what its name says, such as the placement
            // of a crossing pad: it replaces "completed", so the result says what happened.
            Report(place, CommandBand.EditResult(action, outcome.Unchanged, outcome.Reverted, completed));
            return true;
        }
        // The result tells a refusal, a restored failure and an incomplete cleanup apart, and names the correction; the
        // guidance keeps the operation's guide for the footer and its failure kind for the message colour.
        void ReportFailure(string place, string action, SceneObservation selected, Outcome outcome) {
            var recovery = RecoveryGuidance.For(outcome, place, action, selected.Identity?.Structure);
            Report(place, recovery.Line(action));
            messages[0].Recovery = recovery;
        }
    }
}
