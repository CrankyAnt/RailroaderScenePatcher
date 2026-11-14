// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    // A temporary checked preview. Only Complete changes the canonical document and native history.
    public sealed class PlacementGesture {
        readonly PatchEngine engine;
        readonly AuthoringSession session;
        readonly IScene scene;
        readonly TargetIdentity identity;
        readonly object target;
        readonly string[] maps;
        readonly InspectedTarget original;
        InspectedTarget expected;
        readonly ObjectState before;
        readonly object snapshot;
        Placement desired;
        bool active = true, mutated, writingPreview;
        public bool Active => active;
        public Placement Initial => Copy(before.Placement);
        public Placement Current => Copy(desired ?? before.Placement);
        public PlacementGesture(PatchEngine engine, AuthoringSession session, TargetIdentity identity, InspectedTarget inspected) {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.identity = identity ?? throw new ArgumentNullException(nameof(identity));
            original = expected = inspected ?? throw new ArgumentNullException(nameof(inspected));
            scene = engine.Scene;
            if (scene.Capability == Capability.Incompatible) throw new InvalidOperationException("Placement capability is incompatible");
            if (session.Blocked != null) throw new InvalidOperationException(session.Blocked);
            target = engine.ResolveUnique(identity); maps = scene.MapIds.ToArray();
            if (engine.IsUnsafe(target)) throw new InvalidOperationException("Target cleanup is unverified; recheck before editing");
            engine.GuardMutation(identity, target, maps, inspected);
            var own = PatchDocument.Load(session.DocumentText).Root["modifications"].OfType<JObject>().Where(item => {
                try { return ReferenceEquals(engine.ResolveUnique(PatchEngine.ParseTarget(item["target"] as JObject)), target); }
                catch (InvalidOperationException) { return false; }
            }).ToArray();
            if (own.Length > 1) throw new InvalidOperationException("Several modifications target this object; combine them outside the editor first");
            foreach (var item in own) PatchEngine.Parse(item);
            before = scene.Read(target); snapshot = scene.Capture(target);
            engine.Underlying(target);
            engine.GuardMutation(identity, target, maps, inspected);
        }
        public Outcome Preview(Placement placement) {
            var result = new Outcome { ResolvedTarget = target };
            try {
                if (!active) throw new InvalidOperationException("The gizmo gesture has ended");
                var pose = Copy(PatchEngine.CheckPlacement(placement?.Position, placement?.Rotation));
                engine.GuardMutation(identity, target, maps, expected);
                mutated = true; writingPreview = true;
                // The preview sets the pose alone: activity stays as it is, including an explicit disable.
                scene.Apply(target, new ObjectState(pose, null).LeavingActivity());
                if (!SamePose(scene.Read(target).Placement, pose)) throw new InvalidOperationException("Gizmo preview did not verify");
                expected = Observed();
                writingPreview = false; desired = pose; result.Applied = true;
            } catch (Exception e) {
                var restored = Cancel();
                result.Error = e.Message + (restored.Error == null ? "" : "; " + restored.Error);
            }
            return result;
        }
        // The token for the target as the preview left it: the next preview, the completion and the cancellation refuse
        // when the target is no longer what this gesture made of it. A scene that can observe one object reads the
        // target alone, so a drag reads the scene in proportion to its target and not to the scene. A scene that
        // cannot is discovered and compared in full.
        InspectedTarget Observed() {
            if (scene is ITargetedInspectionScene targeted) {
                var observed = targeted.Observe(target, identity);
                if (observed == null || !ReferenceEquals(observed.Handle, target) || observed.Identity.Scene != identity.Scene || observed.Identity.Path != identity.Path || observed.Identity.Component != identity.Component)
                    throw new InvalidOperationException("The gizmo target is no longer observed");
                return new InspectedTarget(maps, observed);
            }
            var inventory = new SceneInventory((IInspectionScene)scene); inventory.Refresh();
            var observation = inventory.Entries.Single(o => ReferenceEquals(o.Handle, target) && o.Identity.Scene == identity.Scene && o.Identity.Path == identity.Path && o.Identity.Component == identity.Component);
            return inventory.Pin(observation);
        }
        public Outcome Complete() {
            if (!active) return new Outcome { Error = "The gizmo gesture has ended" };
            try {
                // Reject changed targets before restoring a preview or committing its final result.
                engine.GuardMutation(identity, target, maps, expected);
                Restore(); active = false;
                if (desired == null || Unchanged(before.Placement, desired)) { engine.AwaitOutput(target); return new Outcome { Applied = true, ResolvedTarget = target }; }
                var result = session.Edit(identity, desired, null, original);
                if (!result.Applied) engine.AwaitOutput(target);
                return result;
            } catch (Exception e) {
                var restored = Cancel();
                return new Outcome { Error = e.Message + (restored.Error == null ? "" : "; " + restored.Error), ResolvedTarget = target };
            }
        }
        public Outcome Cancel() {
            if (!active) return new Outcome { Applied = true, ResolvedTarget = target };
            active = false;
            try { Restore(); engine.AwaitOutput(target); return new Outcome { Applied = true, ResolvedTarget = target }; }
            catch (Exception e) { engine.SetUnsafe(target, true); return new Outcome { Error = "Gizmo cleanup is unverified; recheck or reload before another edit: " + e.Message, ResolvedTarget = target }; }
        }
        void Restore() {
            if (!mutated) return;
            engine.GuardMutation(identity, target, maps, writingPreview ? null : expected);
            if (!scene.Restore(target, snapshot)) throw new InvalidOperationException("Captured dependent state did not restore");
            var state = scene.Read(target);
            if (state.Active != before.Active || !SamePose(state.Placement, before.Placement)) throw new InvalidOperationException("Restored state did not verify");
            mutated = false;
        }
        static Placement Copy(Placement pose) => new Placement((double[])pose.Position.Clone(), (double[])pose.Rotation.Clone());
        // Whether a read-back pose is the pose that was written or restored. A native transform can return a rotation
        // slightly off the one it was given, so this check allows about half a degree; a tighter one would report a
        // sound restoration as unverified and guard the object.
        static bool SamePose(Placement a, Placement b) => a != null && b != null && SamePosition(a, b) && Alignment(a, b) > 0.99999;
        // Whether the gesture ended where it began, so that it records nothing. The angle is the smallest one the scene
        // adapters verify: any rotation they can tell from the starting one is a change, however small.
        const double ChangedAngleDegrees = 0.01;
        static bool Unchanged(Placement before, Placement desired) =>
            SamePosition(before, desired) && 2 * Math.Acos(Math.Min(1, Alignment(before, desired))) * 180 / Math.PI <= ChangedAngleDegrees;
        static bool SamePosition(Placement a, Placement b) => !a.Position.Zip(b.Position, (x, y) => Math.Abs(x - y)).Any(d => d > 0.01);
        // The cosine of half the angle between two rotations, or zero when either quaternion is zero.
        static double Alignment(Placement a, Placement b) {
            double dot = 0, aa = 0, bb = 0;
            for (var i = 0; i < 4; i++) { dot += a.Rotation[i] * b.Rotation[i]; aa += a.Rotation[i] * a.Rotation[i]; bb += b.Rotation[i] * b.Rotation[i]; }
            return aa > 0 && bb > 0 ? Math.Abs(dot) / Math.Sqrt(aa * bb) : 0;
        }
    }
}
