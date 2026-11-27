// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // What a renderer is to the object's outline. Only meshes outline it: an effect reaches past the object, a
    // shadows-only renderer is invisible and a lower level of detail repeats the first one.
    public enum ObjectMarkingBoxKind { Mesh, Effect, ShadowsOnly, LowerLod }

    // One renderer of an object in world coordinates: its world-aligned box as centre and half extents, and, when known,
    // the eight corners of its own oriented box, which follows its mesh, and the height of the
    // surface it stands on, terrain or ballast, at a place.
    public sealed class ObjectMarkingBox {
        public ObjectMarkingBox(string name, WorldPoint center, WorldPoint extents, IReadOnlyList<WorldPoint> oriented = null,
            ObjectMarkingBoxKind kind = ObjectMarkingBoxKind.Mesh, Func<double, double, double?> ground = null) {
            Name = name ?? ""; Center = center; Extents = extents; Oriented = oriented; Kind = kind; Ground = ground;
        }
        // The world height of the surface under a world x and z, terrain or ballast, or null where none is known.
        // Null when the surface is unknown for the whole box.
        public Func<double, double, double?> Ground { get; }
        public string Name { get; }
        public WorldPoint Center { get; }
        public WorldPoint Extents { get; }
        public IReadOnlyList<WorldPoint> Oriented { get; }
        public ObjectMarkingBoxKind Kind { get; }
    }

    // One object between a renderer and the scene root, as the outline rule reads it: whether it is switched on, and
    // whether an object that is off is off only through a culler (CullingActivity.OffThroughCuller).
    public readonly struct MarkingLink {
        public MarkingLink(bool activeSelf, bool offThroughCuller) { ActiveSelf = activeSelf; OffThroughCuller = offThroughCuller; }
        public bool ActiveSelf { get; }
        public bool OffThroughCuller { get; }
    }
    // A world-aligned box, by its least and greatest corner.
    public readonly struct WorldBox {
        public WorldBox(WorldPoint min, WorldPoint max) { Min = min; Max = max; }
        public WorldPoint Min { get; }
        public WorldPoint Max { get; }
    }

    // Three directions at right angles, such as an object's own right, up and forward: the directions of the one box
    // that frames an object of several boxes, or a group.
    public readonly struct WorldBasis {
        public WorldBasis(WorldPoint x, WorldPoint y, WorldPoint z) { X = x; Y = y; Z = z; }
        public WorldPoint X { get; }
        public WorldPoint Y { get; }
        public WorldPoint Z { get; }
        public static readonly WorldBasis World = new WorldBasis(new WorldPoint(1, 0, 0), new WorldPoint(0, 1, 0), new WorldPoint(0, 0, 1));
    }

    // How a complex shows from one camera position: the distance to its bounds, what it shows at that
    // distance, the middle of its bounds, where its square marker stands without a frame, and its one object frame.
    public sealed class GroupMarking {
        internal GroupMarking(double distance, WorldPoint center, ScreenRect? frame, IReadOnlyList<ScreenPoint> outline, WorldPoint? marker = null, bool spread = false) {
            Distance = distance; Center = center; Frame = frame; Outline = outline; Shows = Complexes.Shows(distance); Marker = marker ?? center; Spread = spread;
        }
        // Whether the group's objects stand apart (ObjectMarkingGeometry.Spread): such objects form no group, and each
        // shows on its own.
        public bool Spread { get; }
        // The world point the group's square marker stands at: the center of its one box, which stays where it is as
        // the camera zooms and turns; the middle of its bounds for a group whose box is not known.
        public WorldPoint Marker { get; }
        // Metres from the camera to the bounds of the group; NaN for a group without objects.
        public double Distance { get; }
        public GroupShows Shows { get; }
        public WorldPoint Center { get; }
        // The rectangle around the group's outline and that outline, as an object's frame has them: the outline of
        // the one box around all the group's objects. Null unless the group shows its marker and frame, and null
        // when that box is not in view: the group then takes the least frame about its square marker, as an object
        // without boxes does.
        public ScreenRect? Frame { get; }
        public IReadOnlyList<ScreenPoint> Outline { get; }
    }

    public sealed class ObjectMarkingFrame {
        internal ObjectMarkingFrame(ScreenRect? frame, double distance, IReadOnlyList<ScreenPoint> outline = null, WorldBox? bounds = null, IReadOnlyList<ObjectMarkingBox> piece = null, WorldPoint? marker = null,
            IReadOnlyList<WorldPoint> box = null, int boxes = 0) {
            Frame = frame; Distance = distance; Outline = outline; Bounds = bounds; this.piece = piece; Marker = marker; Box = box; Boxes = boxes;
        }
        // The one box the frame outlines, uncut by the ground, and how many boxes it surrounds; null and 0 when the
        // object has no frame at its distance or nothing stands to frame. Debug output reads them.
        public IReadOnlyList<WorldPoint> Box { get; }
        public int Boxes { get; }
        // The world point the square marker stands at: while the object has a frame, the center of the one box the
        // frame outlines; beyond the frame distance, the center of that box uncut by the ground. It is a point of the
        // object, so the marker stays on it as the camera zooms and turns; the center of the frame's rectangle on
        // screen would wander. Null when no piece was found or no box could be projected.
        public WorldPoint? Marker { get; }
        // The boxes of the framed piece, whether or not the object has a frame at its distance: a group's one box
        // surrounds them.
        readonly IReadOnlyList<ObjectMarkingBox> piece;
        internal IEnumerable<IReadOnlyList<WorldPoint>> PieceBoxes() => piece == null ? Enumerable.Empty<IReadOnlyList<WorldPoint>>() : piece.Select(ObjectMarkingGeometry.CornersOf);
        // The world-aligned box around the framed piece, which the distance runs to; null when no piece was found.
        public WorldBox? Bounds { get; }
        // The rectangle around the outline in top-left screen pixels, or null when the object shows only its square
        // marker. The marker stands at its centre.
        public ScreenRect? Frame { get; }
        // The frame as it is drawn and picked: the outline on screen of the one box around the object's boxes, with
        // the margin around it. Null with a null Frame.
        public IReadOnlyList<ScreenPoint> Outline { get; }
        // Metres from the camera to the bounds of the framed piece; NaN when no piece was found.
        public double Distance { get; }
    }

    // The frame a world marking draws around an object, which picking uses too. The same frame serves the gold frame of
    // the inspected object and the frames of candidates.
    public static class ObjectMarkingGeometry {
        // Beyond this distance from the camera to an object's bounds, in metres, the object shows only its square
        // marker: the largest distance at which every frame still surrounded only its own object in the game with the
        // pointer over each, 550 m, less one 50 m measurement step. It equals the native map editor's default gizmo
        // distance of 500 m.
        public const double FrameDistance = 500;
        // A box far larger than the box at the object's marker, such as a culling or proxy volume, does not outline
        // the object: larger than this factor and this margin in metres.
        public const double ExtentFactor = 4, ExtentMargin = 10;
        // Boxes closer than this to the growing piece join it: a fraction of the marker box's size, at least a metre.
        public const double JoinFraction = .25, MinimumJoin = 1;
        // Pixels around a projected box.
        public const double Margin = 4;
        // Every box is cut off at the surface it stands in, terrain or ballast, so the frame shows only what stands
        // above it. The surface is read under the centre of each piece of the box: a box is divided along each of
        // its own axes into pieces of this length over the ground, in metres, and into at most this many, so a long
        // box on a slope is cut along the slope and not at one height. Along an axis longer than the two together
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
        // What a hit of the downward ray counts as: terrain on the terrain layer, ballast on the track layer, and
        // nothing on a collider of an inventory object. The scene can put an object on the terrain layer, such as a coal
        // heap that cars and characters stand on; it is still the object, not the ground it stands in, and taking it for
        // the ground would raise the frame's lower face to the object's own top.
        public enum SurfaceHit { None, Terrain, Ballast }
        public static SurfaceHit HitKind(bool terrainLayer, bool inventoryObject) => inventoryObject ? SurfaceHit.None : terrainLayer ? SurfaceHit.Terrain : SurfaceHit.Ballast;
        public static double? Surface(double? terrain, IEnumerable<double> track) {
            if (!terrain.HasValue || !Finite(terrain.Value)) return null;
            var surface = terrain.Value;
            if (track != null) foreach (var height in track)
                if (Finite(height) && height > surface && height - terrain.Value <= BallastDepth) surface = height;
            return surface;
        }

        // Whether a renderer outlines its object, whatever the game's distance culling does to it now. A frame stands
        // for the object, not for what the camera shows of it this frame: a renderer culler switches the renderers it
        // holds off and on, a simple culler or a game-object culler switches objects off and on, and none of that may
        // grow, shrink or move the frame or its marker. A renderer counts when it is on, or off only through a culler
        // that holds it, and when every object from the renderer's own up to the scene root is on, or off only through
        // a culler (AuthoredActivity.Enabled). A renderer or object that the scene or a modder switched off does not
        // count.
        // The eight corners of the box a renderer outlines its object with. While it shows: visible, its world box.
        // While it is hidden: own, its own box from its mesh; or, for a renderer in a static batch, which keeps no mesh
        // of its own, kept, the box kept from when it last showed, in its object's own space, carried to the world by
        // toWorld with the object's current placement, so a box kept before a patcher move follows the object. Null
        // when a hidden renderer has neither: one that has not shown since the world loaded.
        public static IReadOnlyList<WorldPoint> RendererBox(bool shows, IReadOnlyList<WorldPoint> visible, IReadOnlyList<WorldPoint> own, IReadOnlyList<WorldPoint> kept, Func<WorldPoint, WorldPoint> toWorld) {
            if (shows) return visible;
            if (own != null) return own;
            if (kept == null || toWorld == null) return null;
            return kept.Select(toWorld).ToArray();
        }
        // chain holds the renderer's own object and every object above it up to the scene root.
        public static bool Outlines(bool rendererEnabled, bool heldByCuller, IEnumerable<MarkingLink> chain) =>
            (rendererEnabled || heldByCuller) && (chain == null || chain.All(link => link.ActiveSelf || link.OffThroughCuller));
        // What a renderer is to the outline, from its type, the types of the components on its object with their base
        // types, its shadow mode and its level of detail. Effects reach past the object they belong to: particles,
        // visual-effect graphs such as water spray, lines, trails, billboards and the beam geometry of a volumetric light.
        public static ObjectMarkingBoxKind KindOf(string rendererType, IReadOnlyList<string> componentTypes, bool shadowsOnly, int lodLevel) {
            var type = rendererType ?? "";
            if (Array.IndexOf(EffectRenderers, type) >= 0) return ObjectMarkingBoxKind.Effect;
            if (componentTypes != null)
                foreach (var component in componentTypes)
                    if (component != null && component.StartsWith("VLB.BeamGeometry", StringComparison.Ordinal)) return ObjectMarkingBoxKind.Effect;
            if (shadowsOnly) return ObjectMarkingBoxKind.ShadowsOnly;
            return lodLevel > 0 ? ObjectMarkingBoxKind.LowerLod : ObjectMarkingBoxKind.Mesh;
        }
        static readonly string[] EffectRenderers = {
            "UnityEngine.ParticleSystemRenderer", "UnityEngine.VFX.VFXRenderer", "UnityEngine.LineRenderer", "UnityEngine.TrailRenderer", "UnityEngine.BillboardRenderer"
        };

        // The anchor is the object's position and the camera the camera's position, both in world coordinates. The
        // projection maps a world point to clip coordinates (-w <= x, y, z <= w is visible). Decisions, when given,
        // receive one line per box.
        public static ObjectMarkingFrame Frame(WorldPoint anchor, WorldPoint camera, IReadOnlyList<ObjectMarkingBox> boxes,
            Func<WorldPoint, ClipPoint> project, ScreenRect view, List<string> decisions) =>
            Frame(anchor, camera, boxes, project, view, decisions, FrameDistance, null);
        // frameDistance is the distance to the object's bounds beyond which it has no frame. An object of a
        // complex that shows as one gives its outline to the group's frame, whose frame distance counts
        // from the group's bounds: its own frame is then asked for without a limit.
        //
        // An object frame uses one box. For several source boxes, the frame encloses them in a box that follows them
        // (BoxAlong), with the object's orientation as its fallback; without an orientation, the directions of the box at
        // the marker, or the world's.
        //
        // Most objects are framed by the piece their marker belongs to: the box at the marker and the boxes next to
        // it, without a box far larger than it or apart from it, since the scene object can hold more than the
        // object. An object that stands for everything below it, whole, is framed by all its boxes: the root of a
        // scene object with child objects, whose marker stands at the root. The box nearest it
        // can be a lamp beside a station building.
        public static ObjectMarkingFrame Frame(WorldPoint anchor, WorldPoint camera, IReadOnlyList<ObjectMarkingBox> boxes,
            Func<WorldPoint, ClipPoint> project, ScreenRect view, List<string> decisions, double frameDistance, WorldBasis? orientation = null, bool whole = false) {
            if (boxes == null) throw new ArgumentNullException(nameof(boxes));
            if (project == null) throw new ArgumentNullException(nameof(project));
            var open = new List<ObjectMarkingBox>();
            foreach (var box in boxes) {
                var reason = box.Kind == ObjectMarkingBoxKind.Effect ? "it is an effect"
                    : box.Kind == ObjectMarkingBoxKind.ShadowsOnly ? "it draws only shadows"
                    : box.Kind == ObjectMarkingBoxKind.LowerLod ? "it is a lower level of detail"
                    : !Valid(box.Center) || !Valid(box.Extents) || box.Extents.X < 0 || box.Extents.Y < 0 || box.Extents.Z < 0 ? "its box has invalid coordinates or size"
                    : null;
                if (reason == null) open.Add(box); else decisions?.Add(box.Name + ": Not used for the frame because " + reason + ".");
            }
            if (open.Count == 0 || !Valid(anchor) || !Valid(camera)) {
                foreach (var box in open) decisions?.Add(box.Name + ": Not used for the frame because the object or camera position is invalid.");
                return new ObjectMarkingFrame(null, Double.NaN);
            }
            // The box the marker belongs to: under it, else the nearest; then the centre nearest it; then the smaller.
            var seed = open[0];
            foreach (var box in open) {
                double d = Distance(box, anchor), best = Distance(seed, anchor);
                if (d < best || (d == best && (Square(box.Center, anchor) < Square(seed.Center, anchor) ||
                    (Square(box.Center, anchor) == Square(seed.Center, anchor) && Size(box) < Size(seed))))) seed = box;
            }
            double limit = whole ? Double.PositiveInfinity : Math.Max(Size(seed) * ExtentFactor, Size(seed) + ExtentMargin);
            double join = whole ? Double.PositiveInfinity : Math.Max(MinimumJoin, Size(seed) * JoinFraction);
            var piece = new List<ObjectMarkingBox> { seed };
            open.Remove(seed);
            for (int i = open.Count - 1; i >= 0; i--)
                if (Size(open[i]) > limit) { decisions?.Add(open[i].Name + ": Not used for the frame because its box is much larger than the box at the marker."); open.RemoveAt(i); }
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
            foreach (var box in open) decisions?.Add(box.Name + ": Not used for the frame because its box is too far from the boxes at the marker.");
            double dx = Gap(minX, maxX, camera.X, 0), dy = Gap(minY, maxY, camera.Y, 0), dz = Gap(minZ, maxZ, camera.Z, 0);
            var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            var bounds = new WorldBox(new WorldPoint(minX, minY, minZ), new WorldPoint(maxX, maxY, maxZ));
            // An object framed whole whose boxes stand apart, such as a container of signs along the track, forms no
            // one thing: one box around its boxes would cover the land between them and catch clicks on it. It gets no
            // frame and lends no boxes to a group; its marker stands where the object stands.
            if (whole && piece.Count > 1 && Spread(piece.Select(CornersOf))) {
                foreach (var box in piece) decisions?.Add(box.Name + ": Not used for the frame because the object's boxes form separate sets with gaps over " + ShownNumber.Text(SpreadGap) + " m between them.");
                return new ObjectMarkingFrame(null, distance, null, bounds);
            }
            if (!(distance <= frameDistance)) {
                foreach (var box in piece) decisions?.Add(box.Name + ": Used for the marker, but too far away for a frame at " + ShownNumber.Text(distance) + " m.");
                // Without a frame the marker stands on the center of the uncut box around the piece: the ground is
                // not read for an object that shows only its marker.
                var uncut = piece.Count == 1 ? CornersOf(piece[0]) : BoxAlongPieces(piece.Select(CornersOf), orientation ?? BasisOf(seed));
                return new ObjectMarkingFrame(null, distance, null, bounds, piece, uncut == null ? null : CenterOf(new List<Piece> { new Piece { Corners = uncut.ToArray() } }));
            }
            // The box is clipped to the view only now, so a box that crosses the near plane or a view edge still
            // outlines a near object, and one outside the view, however near the camera's plane, adds nothing.
            // The frame is the outline of one box on screen. A rectangle around a box that stands at an angle to the
            // view is larger than the box, at its corners; the outline is not. The outline is convex: one closed
            // line per object, easy to test a point against.
            // Burial is decided for the object, not for each box. An object that lies wholly under the surface
            // found from above, as in a tunnel or under an overhang, is not buried: that surface is not the ground
            // it stands in, so it is framed whole. Otherwise whatever lies wholly under the surface is buried and
            // adds nothing: a footing, a pier, or a piece of a wide box, while other boxes stand above it.
            var cut = new List<KeyValuePair<ObjectMarkingBox, List<Piece>>>();
            var stands = false;
            foreach (var box in piece) {
                var pieces = Pieces(CornersOf(box), box.Ground);
                if (pieces.Any(q => q.Above)) stands = true;
                cut.Add(new KeyValuePair<ObjectMarkingBox, List<Piece>>(box, pieces));
            }
            var standing = new List<KeyValuePair<ObjectMarkingBox, List<Piece>>>();
            foreach (var entry in cut) {
                if (stands && !entry.Value.Any(q => q.Above)) decisions?.Add(entry.Key.Name + ": Selected as part of the object, but below the surface and not used for the frame.");
                else standing.Add(entry);
            }
            List<ScreenPoint> points = null;
            IReadOnlyList<WorldPoint> enclosing = null;
            WorldPoint? marker = null;
            if (standing.Count == 1) {
                // For a single box, use its oriented bounds or fall back to its world-aligned
                // box where the oriented one is unknown.
                var sourceBox = standing[0].Key;
                enclosing = CornersOf(sourceBox);
                var box = stands ? Standing(standing[0].Value) : Whole(standing[0].Value);
                points = Project(box, false, project, view, out var finite);
                marker = CenterOf(box);
                // The world-aligned box stands in for an oriented box that gives nothing: one whose projection is
                // not finite, or one that lies outside the view although its box does not.
                if (points == null && sourceBox.Oriented != null && sourceBox.Oriented.Count == 8) {
                    var aligned = Pieces(BoxCorners(sourceBox), sourceBox.Ground);
                    box = stands ? Standing(aligned) : Whole(aligned);
                    points = Project(box, false, project, view, out finite);
                    marker = CenterOf(box);
                }
                decisions?.Add(sourceBox.Name + (points != null ? ": Used for the frame." : finite ? ": Selected for the frame, but outside the view." : ": Selected for the frame, but its screen position is invalid."));
            } else if (standing.Count > 1) {
                // Several boxes: one box around all of them that follows them, cut at the surface it
                // stands in, as for a single box.
                var box = BoxAlongPieces(standing.Select(entry => CornersOf(entry.Key)), orientation ?? BasisOf(seed));
                enclosing = box;
                var ground = standing.Select(entry => entry.Key.Ground).FirstOrDefault(g => g != null);
                var pieces = Pieces(box, ground);
                var above = pieces.Where(q => q.Above && !q.Whole).ToList();
                var drawn = stands && above.Count != 0 ? Standing(pieces) : Whole(pieces);
                points = Project(drawn, false, project, view, out var finite);
                marker = CenterOf(drawn);
                foreach (var entry in standing)
                    decisions?.Add(entry.Key.Name + (points != null ? ": Used for the frame." : finite ? ": Selected for the frame, but outside the view." : ": Selected for the frame, but its screen position is invalid."));
            }
            if (points == null || points.Count == 0) return new ObjectMarkingFrame(null, distance, null, bounds, piece, null, enclosing, standing.Count);
            double left = points.Min(q => q.X) - Margin, top = points.Min(q => q.Y) - Margin, right = points.Max(q => q.X) + Margin, bottom = points.Max(q => q.Y) + Margin;
            return new ObjectMarkingFrame(Rect(left, top, right, bottom), distance, Grown(points, Margin), bounds, piece, marker, enclosing, standing.Count);
        }
        // The center of the one box a frame outlines: the mean of its eight corners.
        static WorldPoint? CenterOf(List<Piece> box) {
            var corners = box?.FirstOrDefault()?.Corners;
            if (corners == null || corners.Length == 0) return null;
            var center = new WorldPoint(corners.Average(c => c.X), corners.Average(c => c.Y), corners.Average(c => c.Z));
            return Valid(center) ? center : (WorldPoint?)null;
        }
        // Use the eight oriented corners when valid. Fall back to the world-aligned corners.
        internal static IReadOnlyList<WorldPoint> CornersOf(ObjectMarkingBox box) =>
            box.Oriented != null && box.Oriented.Count == 8 && box.Oriented.All(Valid) ? box.Oriented : BoxCorners(box);
        // The directions of the oriented box, from corner 0 to the corners that differ from it in bit
        // 0, bit 1 and bit 2. Use world directions if the oriented corners are unavailable or the box is flat.
        static WorldBasis BasisOf(ObjectMarkingBox box) {
            if (box.Oriented == null || box.Oriented.Count != 8 || !box.Oriented.All(Valid)) return WorldBasis.World;
            WorldPoint o = box.Oriented[0];
            WorldPoint Edge(int bit) => new WorldPoint(box.Oriented[bit].X - o.X, box.Oriented[bit].Y - o.Y, box.Oriented[bit].Z - o.Z);
            WorldPoint x = Edge(1), y = Edge(2), z = Edge(4);
            return Length(x) > 0 && Length(y) > 0 && Length(z) > 0 ? new WorldBasis(x, y, z) : WorldBasis.World;
        }
        static double Length(WorldPoint v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        // The box around points in the directions of a basis, as eight corners in the order Pieces and Project
        // expect: bit 0 along the basis' first direction, bit 1 along its second and bit 2 along its third. A
        // direction without length or that is not finite gives the world's directions.
        public static WorldPoint[] Enclose(IEnumerable<WorldPoint> points, WorldBasis basis) {
            var axes = Unit(basis);
            var min = new[] { Double.PositiveInfinity, Double.PositiveInfinity, Double.PositiveInfinity };
            var max = new[] { Double.NegativeInfinity, Double.NegativeInfinity, Double.NegativeInfinity };
            foreach (var p in points) if (Valid(p)) Extend(axes, p, min, max);
            return min[0] > max[0] ? null : Corners(axes, min, max);
        }
        // The three directions of a basis at unit length; the world's when one has no length or is not finite.
        internal static WorldPoint[] Unit(WorldBasis basis) {
            var axes = new[] { basis.X, basis.Y, basis.Z };
            if (axes.Any(a => !Valid(a) || !(Length(a) > 0))) axes = new[] { WorldBasis.World.X, WorldBasis.World.Y, WorldBasis.World.Z };
            return axes.Select(a => { var l = Length(a); return new WorldPoint(a.X / l, a.Y / l, a.Z / l); }).ToArray();
        }
        internal static void Extend(WorldPoint[] axes, WorldPoint p, double[] min, double[] max) {
            for (int i = 0; i < 3; i++) {
                var along = p.X * axes[i].X + p.Y * axes[i].Y + p.Z * axes[i].Z;
                min[i] = Math.Min(min[i], along); max[i] = Math.Max(max[i], along);
            }
        }
        // The eight corners of the box that runs from min to max along three unit directions.
        internal static WorldPoint[] Corners(WorldPoint[] axes, double[] min, double[] max) {
            var corners = new WorldPoint[8];
            for (int c = 0; c < 8; c++) {
                double a = (c & 1) == 0 ? min[0] : max[0], b = (c & 2) == 0 ? min[1] : max[1], d = (c & 4) == 0 ? min[2] : max[2];
                corners[c] = new WorldPoint(axes[0].X * a + axes[1].X * b + axes[2].X * d, axes[0].Y * a + axes[1].Y * b + axes[2].Y * d, axes[0].Z * a + axes[1].Z * b + axes[2].Z * d);
            }
            return corners;
        }
        // The one box around points that follows what they make up (BoxAlongBuilder): for an object that stands
        // upright, the upright box along the points where it is clearly smaller than the box of the object's own axes;
        // for a tilted object, the box of its own axes, which turns with it. Null without valid points.
        public static WorldPoint[] BoxAlong(IEnumerable<WorldPoint> points, WorldBasis axes) {
            var builder = new BoxAlongBuilder(axes);
            if (points != null) builder.Add(points);
            return builder.Box;
        }
        // The one box around the boxes of an object or a group that follows them, as BoxAlong gives it, but only for
        // pieces that form a shape: a box turned away from the object's own axes stays only when it is far smaller
        // than theirs, at most ScatterShare of their size, or when the pieces cover at least CoherentShare of its
        // ground. Small pieces scattered over a building whose own mesh is not among them, such as the clocks and
        // signs of a station, give an outline that follows their scatter, not the building: the box keeps the
        // object's axes. A piece of a single point, where an object without boxes stands, covers nothing.
        public static WorldPoint[] BoxAlongPieces(IEnumerable<IReadOnlyList<WorldPoint>> pieces, WorldBasis axes) {
            var list = pieces?.Where(p => p != null).ToList() ?? new List<IReadOnlyList<WorldPoint>>();
            var builder = new BoxAlongBuilder(axes);
            foreach (var piece in list) builder.Add(piece);
            WorldPoint[] alongBox = builder.Box, axisBox = builder.AxisBox;
            if (alongBox == null || axisBox == null || Same(alongBox, axisBox)) return axisBox;
            if (Measure(alongBox) <= ScatterShare * Measure(axisBox)) return alongBox;
            var ground = Footprint(alongBox);
            var covered = list.Sum(piece => piece.Count == 8 ? Footprint(piece) : 0);
            return ground > 0 && covered >= CoherentShare * ground ? alongBox : axisBox;
        }
        // Whether boxes, or single points, stand apart: they do not all join into one piece when each joins those
        // within SpreadGap of it. The parts of a building, a crossing, a bridge or a storage display stand closer.
        public static bool Spread(IEnumerable<IReadOnlyList<WorldPoint>> pieces) {
            var boxes = new List<double[]>();
            foreach (var piece in pieces ?? Enumerable.Empty<IReadOnlyList<WorldPoint>>()) {
                var valid = piece?.Where(Valid).ToList();
                if (valid == null || valid.Count == 0) continue;
                boxes.Add(new[] { valid.Min(p => p.X), valid.Max(p => p.X), valid.Min(p => p.Y), valid.Max(p => p.Y), valid.Min(p => p.Z), valid.Max(p => p.Z) });
            }
            if (boxes.Count < 2) return false;
            var joined = new bool[boxes.Count]; joined[0] = true;
            var reach = new Queue<int>(); reach.Enqueue(0);
            var count = 1;
            while (reach.Count != 0) {
                var a = boxes[reach.Dequeue()];
                for (int k = 0; k < boxes.Count; k++) {
                    if (joined[k]) continue;
                    var b = boxes[k];
                    double gx = Math.Max(0, Math.Max(a[0] - b[1], b[0] - a[1])), gy = Math.Max(0, Math.Max(a[2] - b[3], b[2] - a[3])), gz = Math.Max(0, Math.Max(a[4] - b[5], b[4] - a[5]));
                    if (Math.Sqrt(gx * gx + gy * gy + gz * gz) > SpreadGap) continue;
                    joined[k] = true; count++; reach.Enqueue(k);
                }
            }
            return count < boxes.Count;
        }
        // Metres between boxes beyond which they stand apart.
        public const double SpreadGap = 100;
        // Pieces that cover less than CoherentShare of the ground of the box along them turn it only from
        // ScatterShare of the size of the box in the object's axes down.
        public const double ScatterShare = .8, CoherentShare = .25;
        // The ground area of a box seen from above: the area of the outline of its corners.
        static double Footprint(IReadOnlyList<WorldPoint> box) {
            var outline = WorldRibbonGeometry.Hull(box.Where(Valid).Select(p => new ScreenPoint(p.X, p.Z)).ToList());
            double sum = 0;
            for (int i = 0; i < outline.Count; i++) { var a = outline[i]; var b = outline[(i + 1) % outline.Count]; sum += a.X * b.Y - b.X * a.Y; }
            return Math.Abs(sum) / 2;
        }
        static bool Same(IReadOnlyList<WorldPoint> a, IReadOnlyList<WorldPoint> b) {
            for (int i = 0; i < 8; i++) if (a[i].X != b[i].X || a[i].Y != b[i].Y || a[i].Z != b[i].Z) return false;
            return true;
        }
        // Whether an object with these axes stands upright: its up axis lies within StandsAngle of the world's.
        public static bool Stands(WorldBasis axes) {
            var up = Unit(axes)[1];
            return up.Y >= Math.Cos(StandsAngle * Math.PI / 180);
        }
        // Degrees an object's up axis may lean and still stand upright.
        public const double StandsAngle = 1;
        // The yaw of a horizontal direction in degrees, from 0 to under 180, as a box has it: the first direction, or
        // the second where the first is upright. Debug output only.
        public static string Yaw(WorldPoint first, WorldPoint second) {
            var d = Math.Abs(first.X) + Math.Abs(first.Z) > 1e-6 * (Length(first) + 1e-12) ? first : second;
            var yaw = Math.Atan2(-d.Z, d.X) * 180 / Math.PI;
            yaw = ((yaw % 180) + 180) % 180;
            return ShownNumber.Text(yaw) + " degrees";
        }
        // The yaw of a box's first horizontal edge, as Yaw gives it. Debug output only.
        public static string Yaw(IReadOnlyList<WorldPoint> box) => box == null || box.Count != 8 ? "none" : Yaw(EdgeOf(box, 1), EdgeOf(box, 4));
        // The edge of a box from corner 0 to the corner that differs from it in a bit.
        internal static WorldPoint EdgeOf(IReadOnlyList<WorldPoint> box, int bit) => new WorldPoint(box[bit].X - box[0].X, box[bit].Y - box[0].Y, box[bit].Z - box[0].Z);
        // A box along the points replaces the box in the object's own axes from this share of its size down: a 6 by
        // 3 m pad 1.4 degrees off its object's axes gives way, one 1 degree off does not, and a nearly round model,
        // such as a tank of 16 sides, keeps its axes. The size of a box is the product of its three edges, each
        // with a slack in metres, so a flat or thin box still has one.
        public const double FitShare = .95, FitSlack = .1;
        internal static double Measure(IReadOnlyList<WorldPoint> box) =>
            (Length(EdgeOf(box, 1)) + FitSlack) * (Length(EdgeOf(box, 2)) + FitSlack) * (Length(EdgeOf(box, 4)) + FitSlack);
        // The distance from a point to a box: what remains of the way from corner 0 to the point once each of the
        // box's edges has taken its part of it.
        internal static double DistanceTo(IReadOnlyList<WorldPoint> box, WorldPoint p) {
            double rx = p.X - box[0].X, ry = p.Y - box[0].Y, rz = p.Z - box[0].Z;
            foreach (var bit in new[] { 1, 2, 4 }) {
                var edge = EdgeOf(box, bit); var length = Length(edge);
                if (!(length > 0)) continue;
                double ux = edge.X / length, uy = edge.Y / length, uz = edge.Z / length;
                var along = Math.Max(0, Math.Min(length, rx * ux + ry * uy + rz * uz));
                rx -= ux * along; ry -= uy * along; rz -= uz * along;
            }
            return Math.Sqrt(rx * rx + ry * ry + rz * rz);
        }
        // How a complex shows. Its one box surrounds all its objects: the boxes of their pieces, and where
        // an object without boxes of its own stands. The box follows them (BoxAlong), with the given orientation, that
        // of the game component that anchors the group, or the world's, as its fallback. The distance runs from the
        // camera to that box, and its center is the group's center. frames holds the frame of each object, or null for
        // an object without one. While the group shows its marker and frame, its one frame is the outline of that box.
        // Like the box of an object it is cut at the surface the group stands in, and framed whole when it lies wholly
        // under the surface found from above. listed says, per
        // object, whether it counts for the bounds and the box (Complex.Listed); without it every object does.
        public static GroupMarking GroupFrame(WorldPoint camera, IReadOnlyList<WorldPoint> anchors, IReadOnlyList<ObjectMarkingFrame> frames,
            WorldBasis? orientation, Func<double, double, double?> ground, Func<WorldPoint, ClipPoint> project, ScreenRect view, IReadOnlyList<bool> listed = null) {
            if (anchors == null) throw new ArgumentNullException(nameof(anchors));
            if (project == null) throw new ArgumentNullException(nameof(project));
            anchors = Listed(anchors, listed); frames = frames == null ? null : Listed(frames, listed);
            // Where an object stands counts for the box only without boxes of its own: the pivot of a top scene object can
            // stand apart from its building, and would widen the box over ground the group does not cover.
            var held = new List<IReadOnlyList<WorldPoint>>();
            for (int k = 0; k < anchors.Count; k++) {
                var boxes = frames != null && k < frames.Count && frames[k] != null ? frames[k].PieceBoxes().ToList() : null;
                if (boxes != null && boxes.Count != 0) held.AddRange(boxes);
                else if (Valid(anchors[k])) held.Add(new[] { anchors[k] });
            }
            var box = BoxAlongPieces(held, orientation ?? WorldBasis.World);
            if (box == null || !Valid(camera)) return new GroupMarking(Double.NaN, default, null, null);
            // The group shows its objects, its marker or its frame by the distance to that box, on whose center its
            // marker stands, so the switch and the marker agree.
            var distance = DistanceTo(box, camera);
            var center = CenterOf(new List<Piece> { new Piece { Corners = box } }) ?? default;
            // Objects that stand apart form no group: no frame, and its objects show on their own.
            if (held.Count > 1 && Spread(held)) return new GroupMarking(distance, center, null, null, null, true);
            var shows = Complexes.Shows(distance);
            if (shows == GroupShows.Objects) return new GroupMarking(distance, center, null, null);
            // Beyond the frame distance the group shows only its marker, on the center of its box uncut by the ground.
            if (shows != GroupShows.MarkerAndFrame) return new GroupMarking(distance, center, null, null, center);
            var pieces = Pieces(box, ground);
            var above = pieces.Where(q => q.Above && !q.Whole).ToList();
            var drawn = above.Count != 0 ? Standing(pieces) : Whole(pieces);
            var marker = CenterOf(drawn);
            var points = Project(drawn, false, project, view, out _);
            if (points == null || points.Count == 0) return new GroupMarking(distance, center, null, null, marker);
            return new GroupMarking(distance, center, Rect(points.Min(q => q.X) - Margin, points.Min(q => q.Y) - Margin, points.Max(q => q.X) + Margin, points.Max(q => q.Y) + Margin), Grown(points, Margin), marker);
        }
        // The objects of a group that count for its bounds and its box: those its listed members name
        // (Complex.Listed), by position; every object without them. The invisible detection spans of a
        // grade-crossing setup do not count.
        static IReadOnlyList<T> Listed<T>(IReadOnlyList<T> objects, IReadOnlyList<bool> listed) =>
            listed == null ? objects : objects.Where((item, k) => k < listed.Count && listed[k]).ToArray();
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
        // Whether an inventory object of a family stands for everything below it and is framed whole: the root of a
        // scene object with child objects, which the inventory lists as one entry for the whole group, a crossing controller,
        // which stands for the whole crossing: its scene object is the root of the grade-crossing setup it controls, and
        // the crossing object of a crossing without signals, which its outliner node stands for: its frame encloses the
        // crossbucks and pads of that crossing.
        public const string ControllerFamily = "Grade crossing controller";
        public static bool FramesWhole(string family) => SceneObjectRules.InFamilies(family) || family == ControllerFamily || family == CrossingObjectScene.Family;
        // Whether a renderer below an object framed whole counts for its frame. families holds the families of the
        // inventory objects on the renderer's scene object and above it, up to the framed object, nearest first. A
        // crossing controller's frame leaves out its whistle signs, which are no part of its setup
        // (Complexes.Of), and its detection spans, which are invisible; the masts with their gates, the bell
        // and the pads count. Everything below another object framed whole counts.
        // The crossing object of a crossing without signals leaves out its whistle signs as well: they stand in it, up to
        // hundreds of metres along the track, and are no part of its complex either.
        public static bool WholeCounts(string family, IEnumerable<string> families) =>
            (family != ControllerFamily && family != CrossingObjectScene.Family) || families == null || !families.Any(f => f == SceneFamilies.TrackMarker || f == "Track span");
        // Where a marker with a frame stands on screen, from its world point projected to x and y: on that point
        // whenever it lies in front of the camera, also outside the frame's rectangle or the view, so it stays on its
        // world point as the camera zooms and turns; the center of the visible part of a frame larger than the view
        // would wander. A point behind the camera falls back to the center of the frame's rectangle.
        public static ScreenPoint MarkerScreen(bool inFront, double x, double y, ScreenRect rect) =>
            inFront ? new ScreenPoint(x, y) : new ScreenPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        // Within the frame distance, an object with no boxes to frame gets a frame around its square marker,
        // including the margin on each side. Distance is measured from the camera to the object in metres.
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

        // One piece of a box: its eight corners, the height of the surface under its centre when one is known, and
        // whether anything of it stands above that surface. Whole marks the original, undivided box.
        // Range holds where a piece lies along the whole box's three axes, as fractions: from and to for each axis.
        sealed class Piece { internal WorldPoint[] Corners; internal double? Height; internal bool Above; internal bool Whole; internal double[] Range; }
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
                pieces.Add(new Piece { Corners = piece, Height = height, Above = !height.HasValue || top > height.Value,
                    Range = new[] { i / (double)counts[0], (i + 1) / (double)counts[0], j / (double)counts[1], (j + 1) / (double)counts[1], k / (double)counts[2], (k + 1) / (double)counts[2] } });
            }
            return pieces;
        }
        static List<Piece> Whole(List<Piece> pieces) => pieces.Where(q => q.Whole).ToList();
        // What stands above the surface, as one box. The whole box shrinks along its own axes to the pieces that stand
        // above the surface, so a box that runs into a bank loses what lies inside it. Its lower face, across its most
        // upright axis, then rises to the lowest surface under those pieces. On a slope the box keeps some ground on its
        // high side, but the frame stays the outline of one box and leaves nothing that stands above out. A box without
        // an upright axis, turned on its edge, is not raised.
        static List<Piece> Standing(List<Piece> pieces) {
            var whole = pieces.First(q => q.Whole);
            var above = pieces.Where(q => !q.Whole && q.Above && q.Range != null).ToList();
            if (above.Count == 0) return new List<Piece> { whole };
            var corners = whole.Corners;
            WorldPoint o = corners[0];
            var axes = new[] { 1, 2, 4 }.Select(bit => new WorldPoint(corners[bit].X - o.X, corners[bit].Y - o.Y, corners[bit].Z - o.Z)).ToArray();
            // The range of the standing pieces along each axis, as fractions of the whole box.
            var lower = new double[3]; var upper = new double[3];
            for (int a = 0; a < 3; a++) { lower[a] = above.Min(q => q.Range[2 * a]); upper[a] = above.Max(q => q.Range[2 * a + 1]); }
            var heights = above.Where(q => q.Height.HasValue).Select(q => q.Height.Value).ToList();
            int upright = -1; double best = 0;
            for (int a = 0; a < 3; a++) {
                var length = Math.Sqrt(axes[a].X * axes[a].X + axes[a].Y * axes[a].Y + axes[a].Z * axes[a].Z);
                var share = length > 0 ? Math.Abs(axes[a].Y) / length : 0;
                if (share > best) { best = share; upright = a; }
            }
            if (heights.Count != 0 && upright >= 0 && best >= UprightAxis) {
                // The fraction along the upright axis at which the box's centre line meets the lowest surface.
                var ground = heights.Min();
                var rest = o.Y;
                for (int a = 0; a < 3; a++) if (a != upright) rest += axes[a].Y * (lower[a] + upper[a]) / 2;
                var at = (ground - rest) / axes[upright].Y;
                var span = upper[upright] - lower[upright];
                if (axes[upright].Y > 0) lower[upright] = Math.Max(lower[upright], Math.Min(at, upper[upright] - span * (1 - MaxRaise)));
                else upper[upright] = Math.Min(upper[upright], Math.Max(at, lower[upright] + span * (1 - MaxRaise)));
            }
            var box = new WorldPoint[8];
            for (int c = 0; c < 8; c++) {
                double x = o.X, y = o.Y, z = o.Z;
                for (int a = 0; a < 3; a++) {
                    var f = ((c >> a) & 1) == 0 ? lower[a] : upper[a];
                    x += axes[a].X * f; y += axes[a].Y * f; z += axes[a].Z * f;
                }
                box[c] = new WorldPoint(x, y, z);
            }
            return new List<Piece> { new Piece { Corners = box, Whole = true, Above = true } };
        }
        // An axis counts as upright from about 60 degrees above the horizontal; the lower face rises at most this far
        // along it, so a box keeps a sliver above a surface that reaches its top.
        const double UprightAxis = 0.866, MaxRaise = 0.98;
        // The twelve edges of a box, as pairs of corners that differ in one bit.
        static readonly int[][] Edges = {
            new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4, 5 }, new[] { 6, 7 }, new[] { 0, 2 }, new[] { 1, 3 }, new[] { 4, 6 }, new[] { 5, 7 }, new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 }
        };
        // Project the pieces into the view. When cut is set, clip each piece at its surface height.
        // Leave the original, undivided box intact. Return null when no points remain.
        // A non-finite projection also returns null and sets finite to false.
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
        static WorldPoint[] BoxCorners(ObjectMarkingBox box) {
            var corners = new WorldPoint[8];
            for (int i = 0; i < 8; i++)
                corners[i] = new WorldPoint(box.Center.X + ((i & 1) == 0 ? -1 : 1) * box.Extents.X,
                    box.Center.Y + ((i & 2) == 0 ? -1 : 1) * box.Extents.Y, box.Center.Z + ((i & 4) == 0 ? -1 : 1) * box.Extents.Z);
            return corners;
        }
        static double Gap(double min, double max, double center, double extent) =>
            Math.Max(0, Math.Max(min - (center + extent), (center - extent) - max));
        static double Distance(ObjectMarkingBox box, WorldPoint p) {
            double dx = Math.Max(0, Math.Abs(p.X - box.Center.X) - box.Extents.X), dy = Math.Max(0, Math.Abs(p.Y - box.Center.Y) - box.Extents.Y),
                dz = Math.Max(0, Math.Abs(p.Z - box.Center.Z) - box.Extents.Z);
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        static double Square(WorldPoint a, WorldPoint b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);
        // A box's size: its largest world dimension in metres.
        static double Size(ObjectMarkingBox box) => 2 * Math.Max(box.Extents.X, Math.Max(box.Extents.Y, box.Extents.Z));
        static ScreenRect Rect(double left, double top, double right, double bottom) =>
            ScreenRect.FromEdges((float)left, (float)top, (float)right, (float)bottom);
        // The square "+" marker of a world marking and the room a frame keeps around it, in screen pixels.
        public const double MarkerSize = 24, MarkerMargin = 4;
        // The symbol inside a square marker, as bars about the marker's centre: a plus for an object or a part, and
        // a hollow square for a group, so that a group never reads as one of its parts.
        public static IReadOnlyList<ScreenRect> MarkerSymbol(bool group) => group
            ? new[] { new ScreenRect(-5, -5, 10, 2), new ScreenRect(-5, 3, 10, 2), new ScreenRect(-5, -5, 2, 10), new ScreenRect(3, -5, 2, 10) }
            : new[] { new ScreenRect(-5, -1, 10, 2), new ScreenRect(-1, -5, 2, 10) };
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
