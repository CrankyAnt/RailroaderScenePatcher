// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // What the game needs from the track binding of one kind of object.
    public enum BindingNeed {
        // The kind has no binding the game reads from the scene, such as a flare marker the game makes while it runs.
        Never,
        // No game logic reads the binding: a yard limit, speed, station or whistle sign, a milepost, a starter marker no
        // setup references, any other Generic marker. An empty binding is legal; a stored segment that does not exist
        // is a wrong reference.
        Optional,
        // Game logic reads the binding while the object is enabled: a crossing pad, whose marker gives the crossing
        // cue and the pad's geometry.
        Required,
        // Game logic reads the binding whatever the object's own activity: a starter marker that an active new-game
        // setup references.
        ReadAnyway
    }

    // The problem rules of a track binding, following the game. The game itself does not tell an empty binding from one
    // that names a missing segment, and it warns only for an enabled marker, about a missing effect. A problem here is a
    // binding the game needs and cannot use, or a stored reference that is provably wrong on an enabled object. An
    // object whose own active flag is off is never a problem, unless the game reads its binding anyway.
    public static class BindingRules {
        public static BindingNeed Need(string markerType, bool crossingPad, bool referencedByActiveSetup) =>
            referencedByActiveSetup ? BindingNeed.ReadAnyway
            : crossingPad || markerType == "Crossing" ? BindingNeed.Required
            : markerType == "Flare" ? BindingNeed.Never
            : BindingNeed.Optional;

        // Whether a kind is listed in the inventory at all. A signal marker is out of scope, the native Map Editor owns
        // the track anchor of a map object, and the builder photo marker has no role in play.
        public static bool Listed(string markerType, bool nativeMapObject, bool builderPhoto) => markerType != "Signal" && !nativeMapObject && !builderPhoto;

        // The problems an object is listed with. An object whose own active flag is off triggers no problem of any
        // kind: someone switched it off, and it stays reachable through the Disabled filter. The one exception is what
        // the game reads whatever the object's activity, such as the binding of a starter marker of the active setup.
        public static IReadOnlyList<string> Problems(bool enabled, IEnumerable<string> all, IEnumerable<string> readAnyway) =>
            (enabled ? all ?? Enumerable.Empty<string>() : readAnyway ?? Enumerable.Empty<string>()).ToArray();

        // Whether a line says what the inspection could not read or check, instead of a defect of the object. A problem
        // is a defect the modder can repair with the patcher, such as a broken binding. That a scan was incomplete, that
        // a value could not be read, that a segment could not be checked or that a script is missing from the hierarchy
        // is none: such a line is an inspection note, shown without the problem marker.
        public static bool IsNote(string line) =>
            line != null && (line.StartsWith("Direct-reference scan incomplete", StringComparison.Ordinal) || line.StartsWith("Inspection unavailable", StringComparison.Ordinal)
                || line.IndexOf("validity unknown", StringComparison.Ordinal) >= 0 || line.StartsWith("Missing script in inspected hierarchy", StringComparison.Ordinal));

        static bool Empty(string segment) => String.IsNullOrWhiteSpace(segment);
        // Whether the game reads this binding now.
        static bool Read(BindingNeed need, bool enabled) => need == BindingNeed.ReadAnyway || (enabled && need != BindingNeed.Never);

        // The problem of a binding, or null. resolves says whether the stored segment exists; null when the track graph
        // could not be read, which Unknown reports instead.
        public static string Problem(BindingNeed need, bool enabled, string role, string segment, bool? resolves) {
            if (!Read(need, enabled)) return null;
            var anyway = need == BindingNeed.ReadAnyway ? "; a new-game setup reads this marker whatever its activity" : "";
            if (Empty(segment)) return need == BindingNeed.Optional ? null : role + " has an empty segment binding" + anyway;
            return resolves == false ? role + " broken binding: missing segment " + segment + anyway : null;
        }

        // The inspection failure of a binding whose segment could not be checked, or null: only where a missing segment
        // would be a problem.
        public static string Unknown(BindingNeed need, bool enabled, string role, string segment, bool? resolves, string graphError) =>
            Read(need, enabled) && !Empty(segment) && resolves == null ? role + " binding validity unknown: " + graphError : null;

        // What the binding's state means when it is no problem, or null when there is nothing to say: an empty optional
        // binding, and a binding that becomes a problem once the object is enabled. followsGroup says the object's
        // activity is set to follow the bound track's group, which an empty binding leaves off.
        public static string Note(BindingNeed need, bool enabled, string segment, bool? resolves, bool followsGroup) {
            if (need == BindingNeed.Never) return null;
            if (Empty(segment)) {
                if (need == BindingNeed.Optional) return "Empty; this kind needs no track binding" + (followsGroup ? ", but it does not follow a track group while unbound" : "");
                return enabled || need == BindingNeed.ReadAnyway ? null : "Empty; a problem once the object is enabled";
            }
            if (resolves == false && !Read(need, enabled)) return "Names missing segment " + segment + "; a problem once the object is enabled";
            return null;
        }
    }
}
