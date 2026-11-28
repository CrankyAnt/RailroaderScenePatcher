// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;

namespace RailroaderScenePatcher.Core {
    // Whether a world marking pass can be used again: its markers and frames hold while the camera's numbers (its
    // position, rotation, projection, view and the world origin) and the pass's inputs (the candidates, the choice and
    // the inspected object, the inventory snapshot) stay the same and it is less than its kept age old, so parts the
    // game moves while it runs and the outlines of renderers its culling switches still follow. A pass that left reads
    // for a later frame has a shorter age: the passes after it read further. A camera number counts as the same within
    // a millionth of its size, so a camera that only jitters in its last digits stands still.
    public sealed class MarkingReuse {
        readonly int maxAge, incompleteAge;
        double[] view = Array.Empty<double>();
        object[] inputs = Array.Empty<object>();
        int frame, age;
        bool kept;
        public const double Tolerance = 1e-6;
        public MarkingReuse(int maxAge, int incompleteAge = 0) {
            this.maxAge = maxAge > 0 ? maxAge : throw new ArgumentOutOfRangeException(nameof(maxAge));
            this.incompleteAge = incompleteAge >= 0 && incompleteAge <= maxAge ? incompleteAge : throw new ArgumentOutOfRangeException(nameof(incompleteAge));
        }
        // Inputs compare by reference.
        public bool Reuses(IReadOnlyList<double> currentView, IReadOnlyList<object> currentInputs, int currentFrame) {
            if (!kept || currentFrame - frame >= age || currentFrame < frame) return false;
            if (currentView == null || currentView.Count != view.Length || currentInputs == null || currentInputs.Count != inputs.Length) return false;
            for (var i = 0; i < view.Length; i++) if (!(Math.Abs(currentView[i] - view[i]) <= Tolerance * (1 + Math.Abs(view[i])))) return false;
            for (var i = 0; i < inputs.Length; i++) if (!ReferenceEquals(currentInputs[i], inputs[i])) return false;
            return true;
        }
        // Keeps the numbers and inputs of a pass just made, for its age.
        public void Keep(IReadOnlyList<double> currentView, IReadOnlyList<object> currentInputs, int currentFrame, bool complete) {
            age = complete ? maxAge : incompleteAge;
            kept = age > 0;
            if (!kept) return;
            if (view.Length != currentView.Count) view = new double[currentView.Count];
            for (var i = 0; i < view.Length; i++) view[i] = currentView[i];
            if (inputs.Length != currentInputs.Count) inputs = new object[currentInputs.Count];
            for (var i = 0; i < inputs.Length; i++) inputs[i] = currentInputs[i];
            frame = currentFrame;
        }
        // The scene may have changed: the next pass is made anew.
        public void Forget() { kept = false; }
    }

    // When the frame diagnostics of world marking run: on the first pass of a camera that stands still, that is, a pass
    // from the same camera numbers as the pass before, once per rest and set of candidates. A moving camera runs none,
    // so the diagnostics stay off the per-frame path.
    public sealed class ObservationPace {
        double[] last = Array.Empty<double>();
        double[] observed;
        object observedSet;
        public bool Due(IReadOnlyList<double> view, object set) {
            var still = Same(last, view);
            if (!still) {
                if (last.Length != view.Count) last = new double[view.Count];
                for (var i = 0; i < last.Length; i++) last[i] = view[i];
                return false;
            }
            if (observed != null && ReferenceEquals(set, observedSet) && Same(observed, view)) return false;
            observed = new double[view.Count];
            for (var i = 0; i < observed.Length; i++) observed[i] = view[i];
            observedSet = set;
            return true;
        }
        static bool Same(double[] kept, IReadOnlyList<double> view) {
            if (view == null || kept.Length != view.Count || kept.Length == 0) return false;
            for (var i = 0; i < kept.Length; i++) if (!view[i].Equals(kept[i])) return false;
            return true;
        }
    }

    // Which object a world marking measured, the same however often it is observed again: its handle, the parts whose
    // renderers make up its frame, the parts framed uncut, and its family. Handles and parts compare by reference,
    // parts in order.
    public readonly struct MarkingKey : IEquatable<MarkingKey> {
        readonly object handle; readonly IReadOnlyList<object> parts, uncut; readonly string family;
        public MarkingKey(object handle, IReadOnlyList<object> parts, IReadOnlyList<object> uncut, string family) {
            this.handle = handle; this.parts = parts; this.uncut = uncut; this.family = family;
        }
        public bool Equals(MarkingKey other) =>
            ReferenceEquals(handle, other.handle) && string.Equals(family, other.family, StringComparison.Ordinal) && Same(parts, other.parts) && Same(uncut, other.uncut);
        public override bool Equals(object obj) => obj is MarkingKey other && Equals(other);
        public override int GetHashCode() {
            unchecked {
                var hash = handle == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(handle);
                hash = hash * 31 + (family == null ? 0 : StringComparer.Ordinal.GetHashCode(family));
                hash = hash * 31 + (parts?.Count ?? -1);
                return hash * 31 + (uncut?.Count ?? -1);
            }
        }
        // The handle the key names, for letting go of objects the game destroyed.
        public object Handle => handle;
        static bool Same(IReadOnlyList<object> a, IReadOnlyList<object> b) {
            if (a == null || b == null) return a == null && b == null;
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++) if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }
    }

    // What world marking keeps per measured object, by its MarkingKey: an object observed again, in a new candidate of
    // another pass, a click or a repaint, finds its entry. Past the limit everything is let go and measured anew, so the
    // store never grows without bound.
    public sealed class KeptGeometry<T> {
        readonly Dictionary<MarkingKey, T> kept = new Dictionary<MarkingKey, T>();
        readonly int limit;
        public KeptGeometry(int limit) { this.limit = limit > 0 ? limit : throw new ArgumentOutOfRangeException(nameof(limit)); }
        public int Count => kept.Count;
        public bool TryGet(MarkingKey key, out T value) => kept.TryGetValue(key, out value);
        public void Keep(MarkingKey key, T value) {
            if (!kept.ContainsKey(key) && kept.Count >= limit) kept.Clear();
            kept[key] = value;
        }
        public void Remove(MarkingKey key) { kept.Remove(key); }
        public void Clear() { kept.Clear(); }
        // Lets go of the entries whose handle is gone.
        public void RemoveWhere(Func<object, bool> gone) {
            var dropped = new List<MarkingKey>();
            foreach (var key in kept.Keys) if (gone(key.Handle)) dropped.Add(key);
            foreach (var key in dropped) kept.Remove(key);
        }
    }
}
