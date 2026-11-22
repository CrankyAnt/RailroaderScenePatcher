// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    public enum SelectionKind { SceneObject, TrackSegment, TrackNode }

    public sealed class SelectionIdentity : IEquatable<SelectionIdentity> {
        public SelectionKind Kind { get; }
        public TargetIdentity Target { get; }
        public string Identifier { get; }

        SelectionIdentity(SelectionKind kind, TargetIdentity target, string identifier) {
            Kind = kind;
            Target = target;
            Identifier = identifier;
        }

        public static SelectionIdentity SceneObject(TargetIdentity target) {
            if (target == null || new[] { target.Scene, target.Path, target.Component }.Any(String.IsNullOrWhiteSpace))
                throw new ArgumentException("A complete scene identity is required", nameof(target));
            return new SelectionIdentity(SelectionKind.SceneObject, target, null);
        }

        public static SelectionIdentity Graph(SelectionKind kind, string identifier) {
            if ((kind != SelectionKind.TrackSegment && kind != SelectionKind.TrackNode) || String.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("A track segment or node identifier is required");
            return new SelectionIdentity(kind, null, identifier);
        }

        public bool Equals(SelectionIdentity other) {
            if (other == null || Kind != other.Kind) return false;
            if (Target == null) return Identifier == other.Identifier;
            return TargetIdentity.SameObject(Target, other.Target) && Target.Structure == other.Target.Structure;
        }

        public override bool Equals(object other) => Equals(other as SelectionIdentity);
        public override int GetHashCode() => Kind.GetHashCode() ^ (Identifier ?? Target.Path).GetHashCode();
        public override string ToString() => Target == null
            ? (Kind == SelectionKind.TrackSegment ? "Track segment " : "Track node ") + Identifier
            : Target.Label;
    }

    public sealed class SelectionCandidate {
        public object Handle { get; }
        public SelectionIdentity Identity { get; }
        public string Label { get; }
        public string Evidence { get; }

        public SelectionCandidate(object handle, SelectionIdentity identity, string label, string evidence = null) {
            Handle = handle ?? throw new ArgumentNullException(nameof(handle));
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Label = label;
            Evidence = evidence;
        }

        public bool Matches(SelectionCandidate current) =>
            current != null && ReferenceEquals(Handle, current.Handle) && Identity.Equals(current.Identity) && Evidence == current.Evidence;
    }

    public interface ISelectionScene {
        IEnumerable<string> MapIds { get; }
        IEnumerable<SelectionCandidate> Discover(SelectionKind kind);
    }
    // A selection scene that can read the current candidates of one identity without discovering every candidate of
    // its kind. Confirmation asks whether an identity still resolves to exactly the candidate that was offered; Find
    // returns every current candidate of that identity, read now, so the answer is the one a discovery would give.
    public interface ITargetedSelectionScene : ISelectionScene {
        IEnumerable<SelectionCandidate> Find(SelectionIdentity identity);
    }

    public sealed class ConfirmedSelection {
        readonly SelectionCandidate owner;
        public SelectionIdentity Identity { get; }
        public SelectionIdentity Source { get; }
        public IReadOnlyList<string> MapIds { get; }

        internal ConfirmedSelection(SelectionIdentity identity, SelectionIdentity source, SelectionCandidate owner, string[] maps) {
            Identity = identity;
            Source = source;
            this.owner = owner;
            MapIds = Array.AsReadOnly((string[])maps.Clone());
        }

        // Whether the choice belongs to the inspected inventory entry. One object with two components gives two
        // entries with one handle, so the entry's identity decides; the handle decides only without an identity.
        public bool AppliesTo(object inspectedHandle, SelectionIdentity inspectedIdentity) =>
            inspectedIdentity != null ? inspectedIdentity.Equals(owner.Identity) : inspectedHandle != null && ReferenceEquals(owner.Handle, inspectedHandle);
    }

    public sealed class PendingSelection {
        readonly ISelectionScene scene;
        readonly string[] maps;
        public SelectionKind Kind { get; }
        public string Purpose { get; }
        public SelectionCandidate Source { get; }
        public bool IsPending { get; private set; } = true;
        public IReadOnlyList<SelectionCandidate> Offers { get; private set; } = Array.Empty<SelectionCandidate>();
        public SelectionCandidate Candidate { get; private set; }
        public ConfirmedSelection Confirmed { get; private set; }
        public string Problem { get; private set; }

        public PendingSelection(ISelectionScene scene, SelectionKind kind, string purpose, SelectionCandidate source = null) {
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            Kind = kind;
            Purpose = purpose;
            Source = source;
            maps = scene.MapIds.ToArray();
            if (maps.Length == 0 || maps.Any(String.IsNullOrWhiteSpace))
                throw new InvalidOperationException("The loaded map context is unavailable");
        }

        // The choice only inspects its result, as Select in world does: no scene patch file will name it.
        public bool InspectOnly { get; set; }
        bool crossingSetup;
        // currentBindings are the current bindings of the slot under the click, which the choice does not offer: a
        // click that met only those names the first as the reason nothing is offered.
        public void Offer(IEnumerable<SelectionCandidate> hits, bool isCrossingSetup = false, IEnumerable<SelectionCandidate> currentBindings = null) {
            if (!IsPending) return;
            Offers = Array.AsReadOnly(hits.Where(c => c != null && c.Identity.Kind == Kind).ToArray());
            crossingSetup = isCrossingSetup && Kind == SelectionKind.SceneObject;
            Candidate = Offers.Count == 1 ? Offers[0] : null;
            Highlight = null;
            var bound = Offers.Count == 0 ? currentBindings?.FirstOrDefault(c => c != null && c.Identity.Kind == Kind) : null;
            Problem = bound != null ? CurrentBindingOffer(bound.Identity) : Offers.Count == 0 ? NoOffer :
                Offers.Count > 1 ? SeveralOffers : null;
        }
        // A group's objects or overlapping markers offer a list beside the pointer. The same number names each
        // offer wherever the panel lists it.
        public const string SeveralOffers = "Select a row from the list next to the pointer.";
        public string OfferCountText => Offers.Count + (crossingSetup && ListOpen ? " rows in the choice list for this crossing setup." :
            Kind == SelectionKind.SceneObject ? " objects." : " candidates.");
        public string OfferListInstruction => crossingSetup && ListOpen ? "Select an object from that list beside the pointer." : SeveralOffers;
        // A click that met no candidate, and one that met only the slot's current binding, which cannot be picked.
        public const string NoOffer = "No candidate at this point. Nothing available for this selection is under the pointer.";
        const string NoOfferStart = "No candidate at this point. ";
        public static string CurrentBindingOffer(SelectionIdentity current) => NoOfferStart + current + " is the current binding, so it can't be picked.";
        // Whether a problem text is no problem of an item: the panel shows it as ordinary text, because red marks
        // only what is broken. An offered list and a click that met no candidate are nothing broken.
        public static bool IsInstruction(string problem) => problem == SeveralOffers || (problem != null && problem.StartsWith(NoOfferStart, StringComparison.Ordinal));
        // The number a candidate carries among the offers, from 1; 0 for one that is not offered.
        public int Number(SelectionCandidate candidate) {
            for (var i = 0; i < Offers.Count; i++) if (ReferenceEquals(Offers[i], candidate)) return i + 1;
            return 0;
        }
        // The candidate the panel points at, such as the one whose row the pointer is over: the world marks it as it
        // marks a candidate under the pointer. It changes nothing about the choice.
        public SelectionCandidate Highlight { get; set; }

        // The list of the candidates of one spot is open: several are offered and none is chosen yet.
        public bool ListOpen => IsPending && Offers.Count > 1 && Candidate == null;
        // Closes that list without a choice, as Escape closes a native dropdown: the choice stays pending and
        // nothing is offered, as before the click.
        public void CloseList() {
            if (!ListOpen) return;
            Offers = Array.Empty<SelectionCandidate>();
            Candidate = null; Highlight = null; Problem = null;
        }

        public void Choose(SelectionCandidate candidate) {
            if (!IsPending || !Offers.Contains(candidate)) return;
            Candidate = candidate;
            Problem = null;
        }

        public bool Confirm() {
            if (!IsPending || Candidate == null) return false;
            if (!maps.SequenceEqual(scene.MapIds, StringComparer.Ordinal))
                return Reject("The loaded map changed. Cancel and start a new selection.");
            // An identity that several objects share cannot be targeted by a patch: a scene patch file names its target by that
            // identity, and nothing in it could tell the objects apart. Inspecting or picking again does not change
            // that, so the refusal says so instead of asking for it.
            var sharedSource = false;
            var difference = Source == null ? null : Difference(Source, out sharedSource);
            if (difference != null)
                return Reject(sharedSource ? "The inspected object shares its identity " + Source.Identity + " with another object, so a patch cannot target it. Cancel; a choice for this object cannot be confirmed."
                    : "The source object changed or no longer resolves uniquely: " + difference + ". Cancel and inspect it again.");
            difference = Difference(Candidate, out var sharedCandidate);
            // An object chosen only to be inspected (InspectOnly) needs no identity a patch could name: it is
            // confirmed by itself when it is still there, and it then says under its supported edits why it cannot
            // be edited. As a replacement target it is refused, since the scene patch file would have to name it.
            if (difference != null && sharedCandidate && InspectOnly && Source == null && Kind == SelectionKind.SceneObject && StillOffered(Candidate)) difference = null;
            if (difference != null)
                return Reject(sharedCandidate ? "The candidate shares its identity " + Candidate.Identity + " with another object, so a patch cannot target it. Pick an object whose path is unique."
                    : "The candidate changed or no longer resolves uniquely: " + difference + ". Pick again.");
            if (!maps.SequenceEqual(scene.MapIds, StringComparer.Ordinal))
                return Reject("The loaded map changed. Cancel and start a new selection.");
            Confirmed = new ConfirmedSelection(Candidate.Identity, Source?.Identity, Source ?? Candidate, maps);
            End();
            return true;
        }

        // Null when the identity still resolves to exactly the candidate that was offered; otherwise what differs, for
        // the refusal.
        string Difference(SelectionCandidate candidate, out bool shared) {
            shared = false;
            var current = (scene is ITargetedSelectionScene targeted ? targeted.Find(candidate.Identity) : scene.Discover(candidate.Identity.Kind)).ToArray();
            var matches = current.Where(c => c.Identity.Equals(candidate.Identity)).Take(2).ToArray();
            if (matches.Length == 1 && candidate.Matches(matches[0])) return null;
            if (matches.Length > 1) { shared = true; return "several candidates share its identity"; }
            if (matches.Length == 1) return ReferenceEquals(matches[0].Handle, candidate.Handle) ? "its observed geometry changed" : "another object has its identity";
            return current.Any(c => ReferenceEquals(c.Handle, candidate.Handle)) ? "its identity or its supported edits changed" : "it is no longer found";
        }

        // Whether the candidate itself, by handle and observed values, is still among the objects of its identity.
        bool StillOffered(SelectionCandidate candidate) {
            var current = scene is ITargetedSelectionScene targeted ? targeted.Find(candidate.Identity) : scene.Discover(candidate.Identity.Kind);
            return current.Any(c => ReferenceEquals(c.Handle, candidate.Handle) && candidate.Matches(c));
        }

        bool Reject(string problem) {
            Offers = Array.Empty<SelectionCandidate>();
            Candidate = null;
            Problem = problem;
            return false;
        }

        public void Cancel() => End();

        void End() {
            IsPending = false;
            Offers = Array.Empty<SelectionCandidate>();
            Candidate = null;
            Highlight = null;
            Problem = null;
        }
    }

    // Which candidates a world choice offers. A choice that only inspects offers every candidate. A choice whose result
    // a scene patch file has to name, such as a replacement object, offers no candidate whose identity another candidate
    // shares: the scene patch file could not tell them apart, so offering one would only lead to a refusal.
    public static class SelectionOffers {
        public static IReadOnlyList<SelectionCandidate> For(IEnumerable<SelectionCandidate> candidates, bool inspectOnly, SelectionIdentity excluded = null) =>
            For(candidates, inspectOnly, excluded == null ? null : new[] { excluded });
        // excluded names the current bindings a choice must not offer again, such as a construction's current segments.
        public static IReadOnlyList<SelectionCandidate> For(IEnumerable<SelectionCandidate> candidates, bool inspectOnly, IEnumerable<SelectionIdentity> excluded) {
            var all = (candidates ?? Enumerable.Empty<SelectionCandidate>()).Where(c => c != null).ToArray();
            if (inspectOnly) return all;
            var shared = new HashSet<SelectionIdentity>(all.GroupBy(c => c.Identity).Where(group => group.Count() > 1).Select(group => group.Key));
            var current = new HashSet<SelectionIdentity>((excluded ?? Enumerable.Empty<SelectionIdentity>()).Where(identity => identity != null));
            return all.Where(c => !current.Contains(c.Identity) && !shared.Contains(c.Identity)).ToArray();
        }
    }

    public sealed class SelectionSession {
        readonly ISelectionScene scene;
        public PendingSelection Pending { get; private set; }
        public ConfirmedSelection Confirmed { get; private set; }

        public SelectionSession(ISelectionScene scene) { this.scene = scene ?? throw new ArgumentNullException(nameof(scene)); }

        // inspectOnly says the choice only inspects its result: no scene patch file will name it.
        public void Begin(SelectionKind kind, string purpose, SelectionCandidate source = null, bool inspectOnly = false) {
            if (Pending != null) throw new InvalidOperationException("A selection is already pending");
            Pending = new PendingSelection(scene, kind, purpose, source) { InspectOnly = inspectOnly };
        }

        public bool Confirm() {
            if (Pending == null || !Pending.Confirm()) return false;
            Confirmed = Pending.Confirmed;
            Pending = null;
            return true;
        }

        public void Cancel() {
            Pending?.Cancel();
            Pending = null;
        }
        public void Clear() {
            Cancel();
            Consume();
        }
        // A choice survives the same inspection, including a refresh of its observations.
        public bool Inspect(object inspectedHandle, SelectionIdentity inspectedIdentity) {
            if (Confirmed == null || Confirmed.AppliesTo(inspectedHandle, inspectedIdentity)) return false;
            Consume();
            return true;
        }
        // The confirmed identity was used by an edit: it is no longer something that waits to be used.
        public void Consume() { Confirmed = null; }
    }
}
