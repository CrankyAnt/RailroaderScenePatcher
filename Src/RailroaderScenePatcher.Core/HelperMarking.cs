// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The helper marker of an invisible scene object from one camera position: its square marker at the object's pivot,
    // and the frame of its colliders' bounds, or the least frame about the marker for an object without colliders.
    // It is presentation only: it holds no colour, which the object's state gives from the world colour table, and it
    // is never written into the scene or a scene patch file.
    public sealed class HelperMarking {
        internal HelperMarking(WorldPoint pivot, ScreenPoint? marker, ScreenRect? frame, IReadOnlyList<ScreenPoint> outline, double distance, bool colliderFrame) {
            Pivot = pivot; Marker = marker; Frame = frame; Outline = outline; Distance = distance; ColliderFrame = colliderFrame;
        }
        // The object's pivot in world coordinates: its transform's position as it is now.
        public WorldPoint Pivot { get; }
        // The centre of the square marker on screen, in top-left pixels: the pivot projected. Null when the pivot lies
        // behind the camera or its projection is not finite.
        public ScreenPoint? Marker { get; }
        // The rectangle around the outline, and the outline as it is drawn and picked. Null beyond the frame distance,
        // and when neither the colliders nor the marker are in view.
        public ScreenRect? Frame { get; }
        public IReadOnlyList<ScreenPoint> Outline { get; }
        // Whether the frame outlines the colliders' bounds; false for the least frame about the marker.
        public bool ColliderFrame { get; }
        // Metres from the camera to the colliders' bounds, or to the pivot without colliders.
        public double Distance { get; }
    }

    // One collider of an object, as the helper marker's frame reads it: whether it is switched on, whether it is a
    // trigger or a terrain collider, and the objects from its own up to the scene root (MarkingLink).
    public readonly struct HelperCollider {
        public HelperCollider(bool enabled, bool trigger, bool terrain, IReadOnlyList<MarkingLink> chain) {
            Enabled = enabled; Trigger = trigger; Terrain = terrain; Chain = chain;
        }
        public bool Enabled { get; }
        public bool Trigger { get; }
        public bool Terrain { get; }
        public IReadOnlyList<MarkingLink> Chain { get; }
    }

    // Invisible scene objects, such as the light-only roots of a Lights group or a collider-only click spot, show a
    // helper marker wherever their marking shows: a square marker with a "+" at the pivot, so the modder can place the
    // object exactly, and a frame for the bounds of its colliders.
    public static class HelperMarkers {
        // Whether an object shows a helper marker: no renderer would outline it. That holds for an object without
        // renderers, one with only effects, such as a light's beam, or shadows-only renderers, and one whose renderers
        // are all switched off. renderers is the frame of the renderers whose box is known (ObjectMarkingGeometry.Frame
        // found no piece in it). unseen counts the mesh renderers that are switched on but hidden with no box known
        // yet, such as a static-batched building part that has not shown since the load: it would outline its object
        // once seen, so the object is no invisible object. While the object waits for its renderer kinds to be read,
        // nothing is decided: it shows no helper marker yet.
        public static bool Shows(ObjectMarkingFrame renderers, bool waits, int unseen) =>
            !waits && unseen == 0 && (renderers == null || !renderers.Bounds.HasValue);

        // The colliders that frame an invisible object, as indices: those switched on, with every object from their own
        // up to the scene root on or off only through a culler, as a renderer counts for an outline
        // (ObjectMarkingGeometry.Outlines). A terrain collider never frames an object. A trigger frames it only when it
        // has no solid collider that counts: a trigger zone reaches past the object it belongs to.
        public static IReadOnlyList<int> Framing(IReadOnlyList<HelperCollider> colliders) {
            var counted = new List<int>();
            if (colliders == null) return counted;
            for (var i = 0; i < colliders.Count; i++)
                if (!colliders[i].Terrain && ObjectMarkingGeometry.Outlines(colliders[i].Enabled, false, colliders[i].Chain)) counted.Add(i);
            var solid = counted.Where(i => !colliders[i].Trigger).ToList();
            return solid.Count != 0 ? solid : counted;
        }

        // pivot is the object's transform position and camera the camera's position, both in world coordinates;
        // colliders holds the bounds of the object's colliders that are switched on, as parts. The projection maps a
        // world point to clip coordinates. frameDistance is the distance beyond which the object shows only its marker.
        // The colliders are framed as renderers are: the collider at the pivot and the colliders next to it, without one
        // far larger than it or apart from it (ObjectMarkingGeometry.ExtentFactor, ExtentMargin, JoinFraction), as one
        // box in the object's orientation. The frame is never cut off at the ground: it shows the colliders' bounds.
        public static HelperMarking Mark(WorldPoint pivot, WorldPoint camera, IReadOnlyList<ObjectMarkingPart> colliders, Func<WorldPoint, ClipPoint> project,
            ScreenRect view, double frameDistance = ObjectMarkingGeometry.FrameDistance, WorldBasis? orientation = null) {
            if (project == null) throw new ArgumentNullException(nameof(project));
            var marker = OnScreen(project(pivot), view);
            var bounds = (colliders ?? Array.Empty<ObjectMarkingPart>())
                .Select(c => new ObjectMarkingPart(c.Name, c.Center, c.Extents, c.Oriented, ObjectMarkingPartKind.Mesh, null)).ToArray();
            double distance = Distance(pivot, camera);
            if (bounds.Length != 0) {
                var framed = ObjectMarkingGeometry.Frame(pivot, camera, bounds, project, view, null, frameDistance, orientation);
                if (framed.Bounds.HasValue) distance = framed.Distance;
                if (framed.Frame.HasValue)
                    return new HelperMarking(pivot, marker, framed.Frame, ObjectMarkingGeometry.VisibleOutline(framed.Outline), distance, true);
            }
            if (marker.HasValue && distance <= frameDistance) {
                var outline = ObjectMarkingGeometry.MarkerOutline(marker.Value.X, marker.Value.Y);
                var half = ObjectMarkingGeometry.MarkerSize / 2 + ObjectMarkingGeometry.MarkerMargin;
                return new HelperMarking(pivot, marker, ScreenRect.FromEdges((float)(marker.Value.X - half), (float)(marker.Value.Y - half), (float)(marker.Value.X + half), (float)(marker.Value.Y + half)),
                    outline, distance, false);
            }
            return new HelperMarking(pivot, marker, null, null, distance, false);
        }

        static double Distance(WorldPoint a, WorldPoint b) {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        static ScreenPoint? OnScreen(ClipPoint p, ScreenRect view) {
            if (!(p.W > 0) || !Finite(p.X) || !Finite(p.Y) || !Finite(p.W)) return null;
            double x = view.X + (p.X / p.W + 1) * view.Width / 2, y = view.Y + (1 - p.Y / p.W) * view.Height / 2;
            return Finite(x) && Finite(y) ? new ScreenPoint(x, y) : (ScreenPoint?)null;
        }
        static bool Finite(double value) => !Double.IsNaN(value) && !Double.IsInfinity(value);
    }
}
