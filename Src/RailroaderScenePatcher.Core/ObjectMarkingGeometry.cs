// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // What a renderer is to the object's outline. Only meshes outline it: an effect reaches past the object, a
    // shadows-only renderer is invisible and a lower level of detail repeats the first one.
    public enum ObjectMarkingPartKind { Mesh, Effect, ShadowsOnly, LowerLod }

    // One renderer of an object in world coordinates: its world-aligned box as centre and half extents, and, when known,
    // the eight corners of its own oriented box, which lies inside the world-aligned one, and the height of the
    // surface it stands on, terrain or ballast, at a place.
    public sealed class ObjectMarkingPart {
        public ObjectMarkingPart(string name, WorldPoint center, WorldPoint extents, IReadOnlyList<WorldPoint> oriented = null,
            ObjectMarkingPartKind kind = ObjectMarkingPartKind.Mesh, Func<double, double, double?> ground = null) {
            Name = name ?? ""; Center = center; Extents = extents; Oriented = oriented; Kind = kind; Ground = ground;
        }
        // The world height of the surface under a world x and z, terrain or ballast, or null where none is known.
        // Null when the surface is unknown for the whole part.
        public Func<double, double, double?> Ground { get; }
        public string Name { get; }
        public WorldPoint Center { get; }
        public WorldPoint Extents { get; }
        public IReadOnlyList<WorldPoint> Oriented { get; }
        public ObjectMarkingPartKind Kind { get; }
    }

    public sealed class ObjectMarkingFrame {
        internal ObjectMarkingFrame(ScreenRect? frame, double distance, IReadOnlyList<ScreenPoint> outline = null) { Frame = frame; Distance = distance; Outline = outline; }
        // The rectangle around the outline in top-left screen pixels, or null when the object shows only its square
        // marker. The marker stands at its centre.
        public ScreenRect? Frame { get; }
        // The frame as it is drawn and picked: the convex outline of the object's parts on screen, with the margin
        // around it. Null with a null Frame.
        public IReadOnlyList<ScreenPoint> Outline { get; }
        // Metres from the camera to the bounds of the framed piece; NaN when no piece was found.
        public double Distance { get; }
    }

    // The frame a world marking draws around an object, which picking uses too. The same frame serves the gold frame of
    // the inspected object and the frames of candidates.
    public static class ObjectMarkingGeometry {
        // Beyond this distance from the camera to an object's bounds, in metres, the object shows only its square
        // marker: the largest distance at which every frame still surrounded only its own object in the game with the
        // pointer over each, 550 m, less one 50 m measurement step. It equals the native editor's default gizmo
        // distance of 500 m.
        public const double FrameDistance = 500;
        // A part far larger than the part at the object's marker, such as a culling or proxy volume, does not outline
        // the object: larger than this factor and this margin in metres.
        public const double ExtentFactor = 4, ExtentMargin = 10;
        // Parts closer than this to the growing piece join it: a fraction of the marker part's size, at least a metre.
        public const double JoinFraction = .25, MinimumJoin = 1;
        // Pixels around a projected part.
        public const double Margin = 4;
        // Every part is cut off at the surface it stands in, terrain or ballast, so the frame shows only what stands
        // above it. The surface is read under the centre of each piece of the part: a part is divided along each of
        // its own axes into pieces of this length over the ground, in metres, and into at most this many, so a long
        // part on a slope is cut along the slope and not at one height. Along an axis longer than the two together
        // allow, 50 metres, the pieces are longer: a fifth of that axis each.
        public const double GroundPiece = 10;
        public const int GroundPieces = 5;
        // Track that lies on the terrain has its ballast surface at most this far above it, in metres: a ballast bed
        // is some decimetres deep, and a metre leaves room for a raised or canted one. Track that
        // lies higher over the terrain is carried, as on a bridge, and is no surface an object stands in.
        public const double BallastDepth = 1;
        // The surface an object stands in at a place, from the heights a ray from above meets there: the terrain,
        // or the ballast of track that lies on that terrain. Track carried over the terrain, as on a bridge deck,
        // and track without terrain under it do not count: a bridge carries its track and stands in the terrain at
        // its ends. Null where there is no terrain.
        public static double? Surface(double? terrain, IEnumerable<double> track) {
            if (!terrain.HasValue || !Finite(terrain.Value)) return null;
            var surface = terrain.Value;
            if (track != null) foreach (var height in track)
                if (Finite(height) && height > surface && height - terrain.Value <= BallastDepth) surface = height;
            return surface;
        }

        // What a renderer is to the outline, from its type, the types of the components on its object with their base
        // types, its shadow mode and its level of detail. Effects reach past the object they belong to: particles,
        // visual-effect graphs such as water spray, lines, trails, billboards and the beam geometry of a volumetric light.
        public static ObjectMarkingPartKind KindOf(string rendererType, IReadOnlyList<string> componentTypes, bool shadowsOnly, int lodLevel) {
            var type = rendererType ?? "";
            if (Array.IndexOf(EffectRenderers, type) >= 0) return ObjectMarkingPartKind.Effect;
            if (componentTypes != null)
                foreach (var component in componentTypes)
                    if (component != null && component.StartsWith("VLB.BeamGeometry", StringComparison.Ordinal)) return ObjectMarkingPartKind.Effect;
            if (shadowsOnly) return ObjectMarkingPartKind.ShadowsOnly;
            return lodLevel > 0 ? ObjectMarkingPartKind.LowerLod : ObjectMarkingPartKind.Mesh;
        }
        static readonly string[] EffectRenderers = {
            "UnityEngine.ParticleSystemRenderer", "UnityEngine.VFX.VFXRenderer", "UnityEngine.LineRenderer", "UnityEngine.TrailRenderer", "UnityEngine.BillboardRenderer"
        };

        // The anchor is the object's position and the camera the camera's position, both in world coordinates. The
        // projection maps a world point to clip coordinates (-w <= x, y, z <= w is visible). Decisions, when given,
        // receive one line per part.
        public static ObjectMarkingFrame Frame(WorldPoint anchor, WorldPoint camera, IReadOnlyList<ObjectMarkingPart> parts,
            Func<WorldPoint, ClipPoint> project, ScreenRect view, List<string> decisions) {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            if (project == null) throw new ArgumentNullException(nameof(project));
            var open = new List<ObjectMarkingPart>();
            foreach (var part in parts) {
                var reason = part.Kind == ObjectMarkingPartKind.Effect ? "effect"
                    : part.Kind == ObjectMarkingPartKind.ShadowsOnly ? "shadows only"
                    : part.Kind == ObjectMarkingPartKind.LowerLod ? "lower level of detail"
                    : !Valid(part.Center) || !Valid(part.Extents) || part.Extents.X < 0 || part.Extents.Y < 0 || part.Extents.Z < 0 ? "bounds not finite"
                    : null;
                if (reason == null) open.Add(part); else decisions?.Add(part.Name + ": excluded, " + reason);
            }
            if (open.Count == 0 || !Valid(anchor) || !Valid(camera)) {
                foreach (var part in open) decisions?.Add(part.Name + ": excluded, no anchor or camera");
                return new ObjectMarkingFrame(null, Double.NaN);
            }
            // The part the marker belongs to: under it, else the nearest; then the centre nearest it; then the smaller.
            var seed = open[0];
            foreach (var part in open) {
                double d = Distance(part, anchor), best = Distance(seed, anchor);
                if (d < best || (d == best && (Square(part.Center, anchor) < Square(seed.Center, anchor) ||
                    (Square(part.Center, anchor) == Square(seed.Center, anchor) && Size(part) < Size(seed))))) seed = part;
            }
            double limit = Math.Max(Size(seed) * ExtentFactor, Size(seed) + ExtentMargin), join = Math.Max(MinimumJoin, Size(seed) * JoinFraction);
            var piece = new List<ObjectMarkingPart> { seed };
            open.Remove(seed);
            for (int i = open.Count - 1; i >= 0; i--)
                if (Size(open[i]) > limit) { decisions?.Add(open[i].Name + ": excluded, far larger than the part at the marker"); open.RemoveAt(i); }
            double minX = seed.Center.X - seed.Extents.X, maxX = seed.Center.X + seed.Extents.X, minY = seed.Center.Y - seed.Extents.Y,
                maxY = seed.Center.Y + seed.Extents.Y, minZ = seed.Center.Z - seed.Extents.Z, maxZ = seed.Center.Z + seed.Extents.Z;
            for (bool joined = true; joined;) {
                joined = false;
                for (int i = open.Count - 1; i >= 0; i--) {
                    var p = open[i];
                    double gap = Math.Max(Gap(minX, maxX, p.Center.X, p.Extents.X), Math.Max(Gap(minY, maxY, p.Center.Y, p.Extents.Y), Gap(minZ, maxZ, p.Center.Z, p.Extents.Z)));
                    if (gap > join) continue;
                    minX = Math.Min(minX, p.Center.X - p.Extents.X); maxX = Math.Max(maxX, p.Center.X + p.Extents.X);
                    minY = Math.Min(minY, p.Center.Y - p.Extents.Y); maxY = Math.Max(maxY, p.Center.Y + p.Extents.Y);
                    minZ = Math.Min(minZ, p.Center.Z - p.Extents.Z); maxZ = Math.Max(maxZ, p.Center.Z + p.Extents.Z);
                    piece.Add(p); open.RemoveAt(i); joined = true;
                }
            }
            foreach (var part in open) decisions?.Add(part.Name + ": excluded, apart from the object's piece");
            double dx = Gap(minX, maxX, camera.X, 0), dy = Gap(minY, maxY, camera.Y, 0), dz = Gap(minZ, maxZ, camera.Z, 0);
            var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (!(distance <= FrameDistance)) {
                foreach (var part in piece) decisions?.Add(part.Name + ": included, beyond the frame distance at " + distance.ToString("0.0", CultureInfo.InvariantCulture) + " m");
                return new ObjectMarkingFrame(null, distance);
            }
            // Clip each part to the view only now, so a part that crosses the near plane or a view edge still outlines a
            // near object, and a part outside the view, however near the camera's plane, adds nothing.
            // The frame is the convex outline of the parts on screen: of each part its own oriented box, which lies
            // inside its world-aligned box, or the world-aligned box where the oriented one is unknown. A rectangle
            // around a box that stands at an angle to the view is larger than the box, at its corners; the outline is
            // not. The outline is convex: one closed line per object, easy to test a point against.
            // Burial is decided for the object, not for each part. An object that lies wholly under the surface
            // found from above, as in a tunnel or under an overhang, is not buried: that surface is not the ground
            // it stands in, so it is framed whole. Otherwise whatever lies wholly under the surface is buried and
            // adds nothing: a footing, a pier, or a piece of a wide part, while other parts stand above it.
            var cut = new List<KeyValuePair<ObjectMarkingPart, List<Piece>>>();
            var stands = false;
            foreach (var part in piece) {
                var corners = part.Oriented != null && part.Oriented.Count == 8 && part.Oriented.All(Valid) ? part.Oriented : BoxCorners(part);
                var pieces = Pieces(corners, part.Ground);
                if (pieces.Any(q => q.Above)) stands = true;
                cut.Add(new KeyValuePair<ObjectMarkingPart, List<Piece>>(part, pieces));
            }
            var points = new List<ScreenPoint>();
            foreach (var entry in cut) {
                var part = entry.Key;
                if (stands && !entry.Value.Any(q => q.Above)) { decisions?.Add(part.Name + ": included, under the surface"); continue; }
                var own = Project(stands ? entry.Value.Where(q => q.Above).ToList() : Whole(entry.Value), stands, project, view, out var finite);
                // The world-aligned box stands in for an oriented box that gives nothing: one whose projection is
                // not finite, or one that lies outside the view although its part does not.
                if (own == null && part.Oriented != null && part.Oriented.Count == 8) {
                    var aligned = Pieces(BoxCorners(part), part.Ground);
                    own = Project(stands ? aligned.Where(q => q.Above).ToList() : Whole(aligned), stands, project, view, out finite);
                }
                if (own == null) { decisions?.Add(part.Name + (finite ? ": included, outside the view" : ": included, projection not finite")); continue; }
                decisions?.Add(part.Name + ": included");
                points.AddRange(own);
            }
            if (points.Count == 0) return new ObjectMarkingFrame(null, distance);
            double left = points.Min(q => q.X) - Margin, top = points.Min(q => q.Y) - Margin, right = points.Max(q => q.X) + Margin, bottom = points.Max(q => q.Y) + Margin;
            return new ObjectMarkingFrame(Rect(left, top, right, bottom), distance, Grown(points, Margin));
        }
        // The convex outline of points, with a margin on every side: the outline of a square of that half size
        // moved over each point.
        static IReadOnlyList<ScreenPoint> Grown(IEnumerable<ScreenPoint> points, double margin) {
            var grown = new List<ScreenPoint>();
            foreach (var q in points) {
                grown.Add(new ScreenPoint(q.X - margin, q.Y - margin)); grown.Add(new ScreenPoint(q.X + margin, q.Y - margin));
                grown.Add(new ScreenPoint(q.X + margin, q.Y + margin)); grown.Add(new ScreenPoint(q.X - margin, q.Y + margin));
            }
            return WorldRibbonGeometry.Hull(grown);
        }
        // The outline as it is drawn and picked: never smaller than the marker with its margin on each side, about
        // the centre of the outline's rectangle, where the marker stands.
        public static IReadOnlyList<ScreenPoint> VisibleOutline(IReadOnlyList<ScreenPoint> outline) {
            if (outline == null || outline.Count == 0) return outline;
            double left = outline.Min(q => q.X), right = outline.Max(q => q.X), top = outline.Min(q => q.Y), bottom = outline.Max(q => q.Y);
            var least = MarkerSize + 2 * MarkerMargin;
            if (right - left >= least && bottom - top >= least && outline.Count >= 3) return outline;
            double x = (left + right) / 2, y = (top + bottom) / 2, half = least / 2;
            var all = new List<ScreenPoint>(outline) { new ScreenPoint(x - half, y - half), new ScreenPoint(x + half, y - half), new ScreenPoint(x + half, y + half), new ScreenPoint(x - half, y + half) };
            return WorldRibbonGeometry.Hull(all);
        }
        // Every inventory object within the frame distance has a frame. An object none of whose parts can be framed,
        // because it has no renderer of its own or all its parts are switched off, takes the least frame about its
        // square marker: the marker with its margin on each side. distance is from the camera to the object's
        // position, in metres.
        public static bool FramesMarker(double distance) => distance <= FrameDistance;
        public static IReadOnlyList<ScreenPoint> MarkerOutline(double x, double y) {
            var half = MarkerSize / 2 + MarkerMargin;
            return new[] { new ScreenPoint(x - half, y - half), new ScreenPoint(x + half, y - half), new ScreenPoint(x + half, y + half), new ScreenPoint(x - half, y + half) };
        }
        // Whether a point lies inside a convex outline or on it.
        public static bool Contains(IReadOnlyList<ScreenPoint> outline, double x, double y) {
            if (outline == null || outline.Count < 3) return false;
            var sign = 0;
            for (int i = 0; i < outline.Count; i++) {
                ScreenPoint a = outline[i], b = outline[(i + 1) % outline.Count];
                var cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
                if (cross == 0) continue;
                var side = cross > 0 ? 1 : -1;
                if (sign == 0) sign = side; else if (side != sign) return false;
            }
            return true;
        }
        // The line of an outline as triangles: along each edge a strip of the given weight, inside the outline.
        public static IReadOnlyList<ScreenTriangle> OutlineStroke(IReadOnlyList<ScreenPoint> outline, double weight) {
            var triangles = new List<ScreenTriangle>();
            if (outline == null || outline.Count < 3) return triangles;
            double cx = outline.Average(q => q.X), cy = outline.Average(q => q.Y);
            for (int i = 0; i < outline.Count; i++) {
                ScreenPoint a = outline[i], b = outline[(i + 1) % outline.Count];
                double ex = b.X - a.X, ey = b.Y - a.Y, length = Math.Sqrt(ex * ex + ey * ey);
                if (!(length > 0)) continue;
                double nx = -ey / length, ny = ex / length;
                if (nx * (cx - a.X) + ny * (cy - a.Y) < 0) { nx = -nx; ny = -ny; }
                ScreenPoint c = new ScreenPoint(b.X + nx * weight, b.Y + ny * weight), d = new ScreenPoint(a.X + nx * weight, a.Y + ny * weight);
                triangles.Add(new ScreenTriangle(a, b, c)); triangles.Add(new ScreenTriangle(a, c, d));
            }
            return triangles;
        }

        // One piece of a part: its eight corners, the height of the surface under its centre when one is known, and
        // whether anything of it stands above that surface. Whole marks the part's undivided box.
        sealed class Piece { internal WorldPoint[] Corners; internal double? Height; internal bool Above; internal bool Whole; }
        // The pieces of a box along its own axes, read from its corners: corner 0 and the corners that differ from it
        // in bit 0, bit 1 and bit 2 give the three axes, whatever way the box is turned. The first piece is the whole
        // box, for an object that is framed whole.
        static List<Piece> Pieces(IReadOnlyList<WorldPoint> corners, Func<double, double, double?> ground) {
            var pieces = new List<Piece> { new Piece { Corners = corners.ToArray(), Whole = true, Above = ground == null } };
            if (ground == null) return pieces;
            WorldPoint o = corners[0];
            WorldPoint Edge(int bit) => new WorldPoint(corners[bit].X - o.X, corners[bit].Y - o.Y, corners[bit].Z - o.Z);
            var axes = new[] { Edge(1), Edge(2), Edge(4) };
            var counts = axes.Select(a => Math.Max(1, Math.Min(GroundPieces, (int)Math.Ceiling(Math.Sqrt(a.X * a.X + a.Z * a.Z) / GroundPiece)))).ToArray();
            for (int i = 0; i < counts[0]; i++) for (int j = 0; j < counts[1]; j++) for (int k = 0; k < counts[2]; k++) {
                var piece = new WorldPoint[8];
                double cx = 0, cz = 0, top = Double.NegativeInfinity;
                for (int c = 0; c < 8; c++) {
                    double a = (i + (c & 1)) / (double)counts[0], b = (j + ((c >> 1) & 1)) / (double)counts[1], d = (k + ((c >> 2) & 1)) / (double)counts[2];
                    piece[c] = new WorldPoint(o.X + axes[0].X * a + axes[1].X * b + axes[2].X * d, o.Y + axes[0].Y * a + axes[1].Y * b + axes[2].Y * d, o.Z + axes[0].Z * a + axes[1].Z * b + axes[2].Z * d);
                    cx += piece[c].X / 8; cz += piece[c].Z / 8; top = Math.Max(top, piece[c].Y);
                }
                var height = ground(cx, cz);
                if (height.HasValue && !Finite(height.Value)) height = null;
                pieces.Add(new Piece { Corners = piece, Height = height, Above = !height.HasValue || top > height.Value });
            }
            return pieces;
        }
        static List<Piece> Whole(List<Piece> pieces) => pieces.Where(q => q.Whole).ToList();
        // The twelve edges of a box, as pairs of corners that differ in one bit.
        static readonly int[][] Edges = {
            new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4, 5 }, new[] { 6, 7 }, new[] { 0, 2 }, new[] { 1, 3 }, new[] { 4, 6 }, new[] { 5, 7 }, new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 }
        };
        // The screen points of pieces above their surface and inside the view. With cut set, each piece is cut off
        // at the height of its surface; the whole box of a part is never cut. Null when nothing is left, or, with
        // finite false, when the projection is not finite.
        //
        // A piece is a convex box, and so is what is left of it above a level surface: its corners above the surface
        // and the points where its edges cross it. While all of those lie inside the view volume, they are the
        // piece's outline, and nothing else is projected. Only a piece that crosses a plane of the view volume has
        // its six faces clipped at that volume, so that a box that crosses the near plane or a view edge still
        // outlines a near object, and one far to the side does not spread over more than the whole view.
        static List<ScreenPoint> Project(List<Piece> pieces, bool cut, Func<WorldPoint, ClipPoint> project, ScreenRect view, out bool finite) {
            finite = true;
            var points = new List<ScreenPoint>();
            foreach (var piece in pieces) {
                if (piece.Whole && pieces.Count > 1) continue;
                var height = cut && !piece.Whole ? piece.Height : null;
                var solid = new List<WorldPoint>();
                if (!height.HasValue) solid.AddRange(piece.Corners);
                else {
                    foreach (var corner in piece.Corners) if (corner.Y >= height.Value) solid.Add(corner);
                    foreach (var edge in Edges) {
                        WorldPoint a = piece.Corners[edge[0]], b = piece.Corners[edge[1]];
                        double da = a.Y - height.Value, db = b.Y - height.Value;
                        if ((da >= 0) == (db >= 0)) continue;
                        var t = da / (da - db);
                        solid.Add(new WorldPoint(a.X + (b.X - a.X) * t, height.Value, a.Z + (b.Z - a.Z) * t));
                    }
                }
                if (solid.Count == 0) continue;
                var clips = new ClipPoint[solid.Count];
                var inside = true;
                for (int i = 0; i < solid.Count; i++) {
                    clips[i] = project(solid[i]);
                    if (!Valid(clips[i])) { finite = false; return null; }
                    var q = clips[i];
                    if (!(q.W > 0) || Math.Abs(q.X) > q.W || Math.Abs(q.Y) > q.W || Math.Abs(q.Z) > q.W) inside = false;
                }
                if (inside) {
                    foreach (var q in clips) if (!Add(points, q, view)) { finite = false; return null; }
                    continue;
                }
                foreach (var face in Faces) {
                    var world = new List<WorldPoint> { piece.Corners[face[0]], piece.Corners[face[1]], piece.Corners[face[2]], piece.Corners[face[3]] };
                    if (height.HasValue) world = AboveGround(world, height.Value);
                    if (world.Count == 0) continue;
                    var polygon = new List<ClipPoint>();
                    foreach (var corner in world) polygon.Add(project(corner));
                    for (int plane = 0; plane < 6 && polygon.Count != 0; plane++) polygon = WorldRibbonGeometry.ClipPolygon(polygon, plane);
                    foreach (var q in polygon) if (q.W > 0 && !Add(points, q, view)) { finite = false; return null; }
                }
            }
            return points.Count == 0 ? null : points;
        }
        static bool Add(List<ScreenPoint> points, ClipPoint p, ScreenRect view) {
            double x = view.X + (p.X / p.W + 1) * view.Width / 2, y = view.Y + (1 - p.Y / p.W) * view.Height / 2;
            if (!Finite(x) || !Finite(y)) return false;
            points.Add(new ScreenPoint(x, y));
            return true;
        }
        // The part of a polygon at or above a height.
        static List<WorldPoint> AboveGround(List<WorldPoint> polygon, double ground) {
            var output = new List<WorldPoint>();
            var previous = polygon[polygon.Count - 1];
            foreach (var current in polygon) {
                double pd = previous.Y - ground, cd = current.Y - ground;
                if ((pd >= 0) != (cd >= 0)) {
                    var t = pd / (pd - cd);
                    output.Add(new WorldPoint(previous.X + (current.X - previous.X) * t, ground, previous.Z + (current.Z - previous.Z) * t));
                }
                if (cd >= 0) output.Add(current);
                previous = current;
            }
            return output;
        }
        // The six faces of a box whose corners differ in one bit along an edge, each as four corners in order.
        static readonly int[][] Faces = {
            new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 }
        };
        // The eight corners in the order Project expects: bit 0 for x, bit 1 for y, bit 2 for z, so corners that differ
        // in one bit share an edge.
        static WorldPoint[] BoxCorners(ObjectMarkingPart part) {
            var corners = new WorldPoint[8];
            for (int i = 0; i < 8; i++)
                corners[i] = new WorldPoint(part.Center.X + ((i & 1) == 0 ? -1 : 1) * part.Extents.X,
                    part.Center.Y + ((i & 2) == 0 ? -1 : 1) * part.Extents.Y, part.Center.Z + ((i & 4) == 0 ? -1 : 1) * part.Extents.Z);
            return corners;
        }
        static double Gap(double min, double max, double center, double extent) =>
            Math.Max(0, Math.Max(min - (center + extent), (center - extent) - max));
        static double Distance(ObjectMarkingPart part, WorldPoint p) {
            double dx = Math.Max(0, Math.Abs(p.X - part.Center.X) - part.Extents.X), dy = Math.Max(0, Math.Abs(p.Y - part.Center.Y) - part.Extents.Y),
                dz = Math.Max(0, Math.Abs(p.Z - part.Center.Z) - part.Extents.Z);
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        static double Square(WorldPoint a, WorldPoint b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);
        // A part's size: its largest world dimension in metres.
        static double Size(ObjectMarkingPart part) => 2 * Math.Max(part.Extents.X, Math.Max(part.Extents.Y, part.Extents.Z));
        static ScreenRect Rect(double left, double top, double right, double bottom) =>
            ScreenRect.FromEdges((float)left, (float)top, (float)right, (float)bottom);
        // The square "+" marker of a world marking and the room a frame keeps around it, in screen pixels.
        public const double MarkerSize = 24, MarkerMargin = 4;
        // The frame as it is drawn and picked: never smaller than its marker with the margin on each side, about the
        // frame's own centre, where the marker stands. A frame of a small or distant object would otherwise lie
        // under its own marker, and the object would seem to have none.
        public static ScreenRect VisibleFrame(ScreenRect frame) {
            var least = MarkerSize + 2 * MarkerMargin;
            double width = Math.Max(frame.Width, least), height = Math.Max(frame.Height, least);
            double x = frame.X + frame.Width / 2 - width / 2, y = frame.Y + frame.Height / 2 - height / 2;
            return ScreenRect.FromEdges((float)x, (float)y, (float)(x + width), (float)(y + height));
        }
        // The distance between a marker and its label, and between labels that stack.
        public const double LabelGap = 4, LabelStack = 2;
        // Where the label of a marking stands: centred directly under its marker, so the two read as one thing. A
        // label that would cover one placed before it, as the labels of markers on one spot do, goes under that
        // label: together they form one list under the spot. Without room below the marker the label stands
        // centred above it, and stacks upward. The label stays inside the screen.
        public static ScreenRect LabelUnder(ScreenRect marker, double width, double height, ScreenRect screen, IReadOnlyList<ScreenRect> placed) =>
            LabelUnder(marker, width, height, screen, placed, null);
        // blocked holds what a label may not cover: native panels and the square markers of other objects. A label
        // that would cover one of them stands at the nearest place that covers none: under its marker moved aside,
        // above it, or above it moved aside; the nearest counts from the place centred under the marker. A label
        // that fits nowhere keeps its place under the marker.
        public static ScreenRect LabelUnder(ScreenRect marker, double width, double height, ScreenRect screen, IReadOnlyList<ScreenRect> placed, IReadOnlyList<ScreenRect> blocked) {
            bool Overlaps(ScreenRect a, ScreenRect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
            ScreenRect Box(double left, double top) => ScreenRect.FromEdges((float)left, (float)top, (float)(left + width), (float)(top + height));
            double Clamp(double left) => Math.Max(screen.X, Math.Min(left, screen.Right - width));
            var others = placed ?? Array.Empty<ScreenRect>();
            // Under the marker at a horizontal place, below the labels already there; above it when there is no room.
            ScreenRect Stack(double left) {
                var y = marker.Bottom + LabelGap;
                for (var moved = true; moved;) {
                    moved = false;
                    foreach (var other in others)
                        if (Overlaps(Box(left, y), other)) { y = other.Bottom + LabelStack; moved = true; }
                }
                if (y + height <= screen.Bottom) return Box(left, y);
                return Above(left);
            }
            ScreenRect Above(double left) {
                var y = marker.Y - LabelGap - height;
                for (var moved = true; moved;) {
                    moved = false;
                    foreach (var other in others)
                        if (Overlaps(Box(left, y), other)) { y = other.Y - LabelStack - height; moved = true; }
                }
                return Box(left, Math.Max(screen.Y, Math.Min(y, screen.Bottom - height)));
            }
            var centre = Clamp(marker.X + marker.Width / 2 - width / 2);
            var first = Stack(centre);
            if (blocked == null || blocked.Count == 0) return first;
            // Every place the label may take is tested against everything that blocks, so nothing is skipped. What
            // keeps the cost from growing with the square of the markers in view is where the places come from: only
            // beside what blocks a place that was tried. It starts with what blocks the two places at the marker,
            // under it and above it, each below or above the labels placed before; a place beside one of those that
            // is blocked in turn adds what blocks it, for a few rounds. A view holds hundreds of markers and a
            // handful of them lie at the label.
            var near = new List<ScreenRect>();
            void Blocking(ScreenRect place) { foreach (var b in blocked) if (Overlaps(place, b) && !near.Contains(b)) near.Add(b); }
            Blocking(first);
            if (near.Count == 0) return first;
            Blocking(Above(centre));
            for (var round = 0; round < LabelRounds; round++) {
                var lefts = new List<double> { centre };
                foreach (var b in near) { lefts.Add(Clamp(b.Right + LabelGap)); lefts.Add(Clamp(b.X - LabelGap - width)); }
                ScreenRect? best = null; var distance = Double.PositiveInfinity;
                var known = near.Count;
                foreach (var left in lefts)
                    foreach (var candidate in new[] { Stack(left), Above(left) }) {
                        var free = true;
                        foreach (var b in blocked) if (Overlaps(candidate, b)) { free = false; if (!near.Contains(b)) near.Add(b); }
                        if (!free || others.Any(o => Overlaps(candidate, o))) continue;
                        double dx = candidate.X - first.X, dy = candidate.Y - first.Y, d = dx * dx + dy * dy;
                        if (d < distance) { distance = d; best = candidate; }
                    }
                if (best.HasValue) return best.Value;
                // Nothing new blocks: no further place can come up.
                if (near.Count == known) break;
            }
            return first;
        }
        // The rounds in which places beside newly found blockers are tried.
        public const int LabelRounds = 4;
        static bool Finite(double value) => !Double.IsNaN(value) && !Double.IsInfinity(value);
        static bool Valid(WorldPoint p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
        static bool Valid(ClipPoint p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z) && Finite(p.W);
    }
}
