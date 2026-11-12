// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The result of the last edit request, for the panel section that holds its control and for the log. An edit that is
    // refused or does not run always leaves its reason here. UI-independent, shared with controlled replay.
    internal sealed class EditFeedback {
        // The panel section whose control made the request.
        internal string Place { get; private set; }
        internal string Result { get; private set; } = "";
        internal Action<string> Log;
        internal void Report(string place, string message) {
            Place = place; Result = message ?? "";
            try { Log?.Invoke(Result); } catch { }
        }
        internal void Refuse(string place, string action, string reason) => Report(place, action + " refused: " + reason);
        // Runs one edit bound to the inspected observation. blocked is the reason the panel cannot edit now, or null.
        // Pinning compares the whole scene first, so a stale or changed inventory refuses the edit before it runs.
        internal bool Run(string place, string action, string blocked, SceneInventory inventory, SceneObservation selected, Func<InspectedTarget, Outcome> change) {
            if (blocked != null) { Refuse(place, action, blocked); return false; }
            if (inventory == null || selected == null) { Refuse(place, action, "no inspected object; refresh the inventory and inspect the object first"); return false; }
            Outcome outcome;
            try { outcome = change(inventory.Pin(selected)); }
            catch (Exception e) { Refuse(place, action, e.GetBaseException().Message); return false; }
            if (outcome == null || !outcome.Applied) { Refuse(place, action, outcome?.Error ?? "the edit returned no outcome"); return false; }
            Report(place, action + " completed. Undo and Redo use the editor's history; Save uses the editor's Save button.");
            return true;
        }
    }
}
