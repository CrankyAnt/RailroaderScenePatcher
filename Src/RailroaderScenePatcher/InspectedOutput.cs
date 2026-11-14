// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The dependent output of the inspected object, for a panel that reads it every frame. Resolving a target searches
    // the whole scene, so the object is resolved once per observation: every inspection, refresh and completed edit
    // produces a new observation. The output itself is read on every call. UI-independent, shared with controlled replay.
    internal sealed class InspectedOutput {
        AuthoringSession owner;
        SceneObservation observed;
        object target;

        internal DependentOutput Of(AuthoringSession session, SceneObservation inspected, out string problem) {
            problem = null;
            try {
                if (!ReferenceEquals(owner, session) || !ReferenceEquals(observed, inspected)) {
                    owner = null; observed = null;
                    // Null when the target does not resolve uniquely; such a target has no output.
                    target = session.Resolve(inspected.Identity);
                    owner = session; observed = inspected;
                }
                return session.OutputOf(target);
            } catch (Exception e) { problem = e.GetBaseException().Message; return DependentOutput.None; }
        }
    }
}
