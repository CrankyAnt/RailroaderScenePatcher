// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
namespace RailroaderScenePatcher.Core {
    // The placement a gizmo drag asks for, from the drag's totals. The gizmo stands in local space, as the native map
    // editor's does: its handles follow the object's own rotation. It still reports a move as a translation in world
    // axes, along the handle's axis as it stands in the world, and a turn as a rotation about that axis in the world.
    // A translation is the same in map and world coordinates, which differ by the floating origin only.
    public static class GizmoDrag {
        public static Placement Moved(Placement start, double[] translation) =>
            new Placement(new[] { start.Position[0] + translation[0], start.Position[1] + translation[1], start.Position[2] + translation[2] }, (double[])start.Rotation.Clone());
        // A world rotation applies before the object's own rotation, which is the same as a turn about the object's own
        // axis when the gizmo's axis is that axis.
        public static Placement Turned(Placement start, double[] rotation) =>
            new Placement((double[])start.Position.Clone(), Quaternion.Normalize(Quaternion.Multiply(rotation, start.Rotation)));
    }
}
