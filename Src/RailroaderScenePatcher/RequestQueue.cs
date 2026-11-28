// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;

namespace RailroaderScenePatcher {
    // The one edit or preview that waits until the panel knows its snapshot agrees with the scene. A request belongs to
    // the open panel and the native map editor it was made in: closing the panel, leaving the native map editor or ending the load attempt
    // cancels it, as Cancel applies to every pending interaction. A cancelled request never runs and leaves a refusal
    // in its place, so nothing changes later without the modder seeing why. UI-independent, shared with controlled
    // replay.
    internal sealed class RequestQueue {
        readonly EditFeedback feedback;
        Action run;
        string action;
        bool running, refreshing;
        readonly List<(string Place, string Action)> busyRefusals = new List<(string, string)>();
        internal bool WaitingForTerrain { get; private set; }
        internal RequestQueue(EditFeedback feedback) { this.feedback = feedback ?? throw new ArgumentNullException(nameof(feedback)); }
        // The action group of the waiting request, or null. The message area shows it with "Working...".
        internal string Place { get; private set; }
        internal bool Pending => run != null;
        internal bool Busy => Pending || running || refreshing || WaitingForTerrain;
        internal void WaitForRefresh() { refreshing = true; }
        internal void Refreshed() { refreshing = false; }
        internal void WaitForTerrain() { WaitingForTerrain = true; }
        internal void ObserveTerrain(bool working) { if (!working) WaitingForTerrain = false; }
        internal bool RefuseBusyInput(string place, string action) {
            if (!Busy) return false;
            busyRefusals.Add((place, action));
            return true;
        }
        internal void ShowBusyRefusals() {
            if (Busy) return;
            foreach (var refusal in busyRefusals)
                feedback.AddNotice(refusal.Place, refusal.Action + " refused. Another edit is still running.");
            busyRefusals.Clear();
        }
        // Takes a request. One request waits at a time; a second one is refused by its caller before it gets here.
        internal void Enqueue(string place, string action, Action run) {
            if (Busy) throw new InvalidOperationException("Another edit is still running");
            this.run = run ?? throw new ArgumentNullException(nameof(run)); Place = place; this.action = action;
        }
        // Runs the waiting request once and forgets it, whatever it does.
        internal void Run() {
            var request = run;
            run = null; Place = null; action = null;
            if (request == null) return;
            running = true;
            try { request(); }
            finally { running = false; }
        }
        // The inspection ends: the results of the object that was inspected go, and a request that still waited for it
        // is refused. The refusal comes last, so it stays where the results were. Returns whether a request was waiting.
        internal bool Release(string reason) {
            feedback.Released();
            return Cancel(reason);
        }
        // Clears the inspection, by Escape or a click on the inspected row. A pending world choice ends through the
        // same path as its Cancel, so the world's clicks go back to the native map editor; then what was prepared for the
        // object is restored; last the results go and a waiting request is refused, so that its refusal stays.
        internal bool ReleaseInspection(bool choicePending, Action cancelChoice, Action restore, string reason) {
            if (choicePending) cancelChoice?.Invoke();
            restore?.Invoke();
            return Release(reason);
        }
        // Drops the waiting request with a refusal that names the reason. Returns whether a request was waiting.
        internal bool Cancel(string reason) {
            if (!Pending) return false;
            var place = Place; var name = action;
            run = null; Place = null; action = null;
            feedback.Refuse(place, name, reason);
            return true;
        }
    }
}
