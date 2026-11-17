// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RailroaderScenePatcher.Core {
    // What a milepost edit must leave as it is: the post's prefix, origin flag, mile value, displayed text and model, and
    // the game's origin cache entry for the prefix. The game caches the origin milepost object per prefix the first time
    // something asks a post for its origin; the cache holds the object, not its location, so a placement or binding edit
    // has nothing to recalculate there. Nothing in an edit may populate, drop or replace that entry.
    public sealed class MilepostState {
        // The cache entry for the prefix, by the scene and path of the post it holds; NotCached when the prefix has no entry.
        public const string NotCached = "not cached";
        public static string CachedAs(string origin) => "cached as " + (origin ?? "no origin milepost");
        public MilepostState(string prefix, bool origin, double mileage, string text, string renderedText, string model, string originCache, bool activeInHierarchy) {
            Prefix = prefix; Origin = origin; Mileage = mileage; Text = text; RenderedText = renderedText; Model = model; OriginCache = originCache; ActiveInHierarchy = activeInHierarchy;
        }
        public string Prefix { get; }
        public bool Origin { get; }
        public double Mileage { get; }
        // The displayed text as the post's text synchronizer holds it.
        public string Text { get; }
        // What the post's rendered texts show. A text object that has never been awake shows nothing yet and fills in when
        // the post first becomes active.
        public string RenderedText { get; }
        // A signature of the post's model: its meshes and materials, without those of its rendered texts, which a text
        // object creates when it wakes.
        public string Model { get; }
        public string OriginCache { get; }
        // Whether the post was active in the hierarchy when this state was read.
        public bool ActiveInHierarchy { get; }
        // What differs from the earlier state, or null when nothing does. The rendered texts count only when the post was
        // active before: a post that was inactive may wake during the write and fill them in.
        public string Difference(MilepostState before) {
            if (before == null) return null;
            if (Prefix != before.Prefix) return "the milepost prefix changed from " + before.Prefix + " to " + Prefix;
            if (Origin != before.Origin) return "the milepost's origin flag changed";
            if (Mileage != before.Mileage) return "the mile value changed from " + before.Mileage.ToString("R", CultureInfo.InvariantCulture) + " to " + Mileage.ToString("R", CultureInfo.InvariantCulture);
            if (Text != before.Text) return "the milepost's display text changed from " + before.Text + " to " + Text;
            if (before.ActiveInHierarchy && RenderedText != before.RenderedText) return "the text rendered on the milepost changed from " + before.RenderedText + " to " + RenderedText;
            if (Model != before.Model) return "the milepost model changed";
            if (OriginCache != before.OriginCache) return "the origin cache for prefix " + Prefix + " changed from " + before.OriginCache + " to " + OriginCache;
            return null;
        }
    }
    // The game-facing operations of the milepost capability: the shared track-marker operations plus the state an edit preserves.
    public interface IMilepostWorld : ITrackMarkerWorld {
        MilepostState Milepost(object post);
    }
    // Places, enables or disables a milepost and sets, replaces or detaches its track binding with the graph registration,
    // as for a Generic track marker. Physical placement and binding stay separate settings. The post keeps its model,
    // mile value and display, and the origin cache keeps its entry: every write is checked against the state before it,
    // and a difference fails the write, which the engine then restores.
    public sealed class MilepostScene : IScene, IRelationshipScene, IVersionedScene, ITargetConfirmation, IPlacementPreflight, IFollowUpdateScene {
        public const string Structure = "milepost-v1", RequiredVersion = "0.5.0", Component = "Helpers.MilePost";
        readonly MarkerRelationship relationship;
        readonly IMilepostWorld world;
        public MilepostScene(IMilepostWorld world, Capability capability) {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            relationship = new MarkerRelationship(world, Structure, new Preserved(world));
            Capability = capability;
        }
        public IEnumerable<string> MapIds => relationship.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) => identity.Component == Component ? relationship.Resolve(identity) : Array.Empty<object>();
        public bool StillIdentifies(object target, TargetIdentity identity) => identity.Component == Component && relationship.StillIdentifies(target, identity);
        public string MinimumPatcherVersion(TargetIdentity target) => target.Structure == Structure || (target.Structure == null && target.Component == Component) ? RequiredVersion : null;
        public void Preflight(object target) => relationship.Preflight(target);
        public void PreflightPlacement(object target) => relationship.PreflightPlacement(target);
        public void CheckRelationships(object target, ObjectState state) => relationship.CheckRelationships(target, state);
        public object Capture(object target) => relationship.Capture(target);
        public ObjectState Read(object target) => relationship.Read(target);
        public void Apply(object target, ObjectState state) => Checked(target, () => relationship.Apply(target, state));
        public void ApplyFollowUpdate(object target, ObjectState state) => Checked(target, () => relationship.Apply(target, state, true));
        public bool Restore(object target, object snapshot) => relationship.Restore(target, snapshot);
        void Checked(object target, Action write) {
            var before = world.Milepost(target);
            write();
            var difference = world.Milepost(target).Difference(before);
            if (difference != null) throw new InvalidOperationException("The milepost edit changed a value that must stay unchanged: " + difference);
        }
        // The preserved milepost state is captured with the marker state, and restoration verifies that it returned.
        sealed class Preserved : IMarkerConsumer {
            readonly IMilepostWorld world;
            internal Preserved(IMilepostWorld world) { this.world = world; }
            public object Capture(object target) => world.Milepost(target);
            public void Refresh(object target, bool resolvable, bool following, bool explicitApply) { }
            public string Problem(object target, bool resolvable, bool following, bool explicitApply) => null;
            public string Restored(object target, object captured) => world.Milepost(target).Difference((MilepostState)captured);
        }
    }
}
