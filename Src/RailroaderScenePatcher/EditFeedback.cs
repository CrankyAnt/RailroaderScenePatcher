// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The results of the last edit requests, for the message area and for the log. An edit that is refused or does not
    // run always leaves its reason here. UI-independent, shared with controlled replay.
    internal sealed class EditFeedback {
        // One result with the action group it came from, such as Placement or Track binding.
        internal sealed class Message {
            internal string Source { get; }
            internal string Text { get; }
            // A consequence that follows a result by itself, such as terrain output after an edit, not the result of an
            // action the user selected.
            internal bool FollowUp { get; }
            internal Message(string source, string text, bool followUp = false) { Source = source; Text = text; FollowUp = followUp; }
        }
        internal const int Kept = 3;
        readonly System.Collections.Generic.List<Message> messages = new System.Collections.Generic.List<Message>();
        // The newest message first, which is the order the message area shows them in; at most Kept, of which at most
        // one follow-up: the latest. A row of follow-ups therefore never pushes the result they follow out of view.
        internal System.Collections.Generic.IReadOnlyList<Message> Messages => messages;
        // The action group of the newest message, and its text.
        internal string Place => messages.Count == 0 ? null : messages[0].Source;
        internal string Result => messages.Count == 0 ? "" : messages[0].Text;
        internal Action<string> Log;
        object inspected;
        // A result is about the object inspected when it was reported: inspecting another object clears it. An object
        // that is no longer present clears nothing.
        internal void Inspecting(object handle) {
            if (handle == null || ReferenceEquals(handle, inspected)) return;
            if (inspected != null) messages.Clear();
            inspected = handle;
        }
        // A new load attempt starts without the messages of the one before.
        internal void Clear() { messages.Clear(); inspected = null; }
        internal void Report(string place, string message) {
            // The same result again is one message: it moves to the top instead of being listed twice.
            messages.RemoveAll(m => !m.FollowUp && m.Source == place && m.Text == (message ?? ""));
            messages.Insert(0, new Message(place, message ?? ""));
            if (messages.Count > Kept) messages.RemoveRange(Kept, messages.Count - Kept);
            try { Log?.Invoke(Result); } catch { }
        }
        // Reports a follow-up. It replaces the follow-up before it and never a result.
        internal void Follow(string place, string message) {
            messages.RemoveAll(m => m.FollowUp);
            messages.Insert(0, new Message(place, message ?? "", true));
            if (messages.Count > Kept) messages.RemoveRange(Kept, messages.Count - Kept);
            try { Log?.Invoke(Result); } catch { }
        }
        // Removes a message that announced a state which has ended, such as a form that closed. Results stay.
        internal void Withdraw(string place, string message) { messages.RemoveAll(m => m.Source == place && m.Text == message); }
        internal void Refuse(string place, string action, string reason) => Report(place, action + " refused: " + reason);
        // Runs one edit bound to the inspected observation. blocked is the reason the panel cannot edit now, or null.
        // Pinning compares the whole scene first, so a stale or changed inventory refuses the edit before it runs.
        // pin supplies the inspection token; without it the edit pins and compares the whole scene in this call.
        internal bool Run(string place, string action, string blocked, SceneInventory inventory, SceneObservation selected, Func<InspectedTarget, Outcome> change, Func<SceneObservation, InspectedTarget> pin = null) {
            if (blocked != null) { Refuse(place, action, blocked); return false; }
            if (inventory == null || selected == null) { Refuse(place, action, "no inspected object; refresh the inventory and inspect the object first"); return false; }
            Outcome outcome;
            try { outcome = change(pin != null ? pin(selected) : inventory.PinForEdit(selected)); }
            catch (Exception e) { Refuse(place, action, e.GetBaseException().Message); return false; }
            if (outcome == null || !outcome.Applied) { Refuse(place, action, outcome?.Error ?? "the edit returned no outcome"); return false; }
            Report(place, action + " completed.");
            return true;
        }
    }
}
