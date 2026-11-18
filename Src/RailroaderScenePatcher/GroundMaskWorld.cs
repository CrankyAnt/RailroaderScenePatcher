// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The scene ground masks at and below a scene object as the game holds them: the mask components
    // (Map.Runtime.MaskComponents.MaskComponentBase), the keys of their registrations in the terrain system, and that
    // system's tiles, which the independent-board world already reaches. The rules live in Core's GroundMasks.
    internal sealed class GroundMaskWorld : IGroundMaskWorld {
        const string RectangleType = "Map.Runtime.MaskComponents.RectangleMapMask", ShapedBase = "Map.Runtime.MaskComponents.MapMaskBase";
        static readonly Type maskBase = Native.Type("Map.Runtime.MaskComponents.MaskComponentBase"), shaped = Native.Type(ShapedBase), rectangle = Native.Type(RectangleType);
        static readonly FieldInfo keys = maskBase?.GetField("_modifierKeys", Native.Flags), flatten = shaped?.GetField("flattenHeightmap", Native.Flags), targets = shaped?.GetField("maskTargets", Native.Flags),
            sizeX = rectangle?.GetField("sizeX", Native.Flags), sizeZ = rectangle?.GetField("sizeZ", Native.Flags), degrees = rectangle?.GetField("degrees", Native.Flags);
        // The fields of one mask target (MaskTarget), the element of MapMaskBase.maskTargets.
        static readonly Type targetType = targets != null && targets.FieldType.IsGenericType ? targets.FieldType.GetGenericArguments()[0] : null;
        static readonly FieldInfo targetKind = targetType?.GetField("kind"), targetBiome = targetType?.GetField("biomeCode"), targetName = targetType?.GetField("maskName");
        static readonly Type marker = Native.Type(BakedSceneryRules.Marker);
        static readonly MethodInfo rebuild = maskBase?.GetMethod("Rebuild", Native.Flags, null, Type.EmptyTypes, null);
        readonly BoardScene terrain;
        // Whether a world reads and rebuilds the masks in this load: without one, a group that holds a mask is refused.
        internal static bool Available { get; private set; }
        internal GroundMaskWorld(BoardScene terrain) {
            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            // The members this world reads are checked here: a missing one fails the adapter with its name, not an edit.
            if (maskBase == null || shaped == null || rectangle == null) throw new InvalidOperationException("Ground mask types unavailable");
            if (keys == null || rebuild == null) throw new MissingMemberException("Ground mask registration members unavailable");
            if (flatten?.FieldType != typeof(bool) || targets == null || sizeX?.FieldType != typeof(float) || sizeZ?.FieldType != typeof(float) || degrees?.FieldType != typeof(float)) throw new MissingMemberException("Ground mask shape members unavailable");
            if (targetKind == null || targetBiome?.FieldType != typeof(int) || targetName == null || !targetName.FieldType.IsEnum) throw new MissingMemberException("Ground mask target members unavailable");
            Available = true;
        }
        internal static void Unavailable() => Available = false;
        // Whether a scene ground mask stands at or below the object, by the scene's own objects.
        internal static bool Holds(GameObject go) => go != null && maskBase != null && Components(go).Length != 0;
        // The scene objects at and below the object that hold a mask, and whether one of the masks flattens the height.
        internal static IReadOnlyList<GameObject> Holders(GameObject go, out bool flattens) {
            var found = go == null || maskBase == null ? new Component[0] : Components(go);
            flattens = found.Any(Flattens) && !FlatteningAllowed;
            return found.Select(c => c.gameObject).Distinct().ToArray();
        }
        static bool Flattens(Component mask) => flatten != null && shaped.IsInstanceOfType(mask) && (bool)flatten.GetValue(mask);
        // Test only: the fault build can lift the flattening refusal, and the inspector then offers the move.
        static bool FlatteningAllowed {
            get {
#if SCENE_PATCHER_FAULTS
                return FaultInjection.FlatteningMovesAllowed;
#else
                return false;
#endif
            }
        }
        // What the game builds while it runs does not count, as for the parts of a group. Below a marker object, only
        // the station building that the rule names has its masks handled (BakedSceneryRules.MaskedMarker): the mask of
        // any other marker object, such as a sign's, stays as the Track marker family leaves it.
        static Component[] Components(GameObject go) {
            if (marker != null && go.GetComponent(marker) != null && !BakedSceneryRules.MaskedMarker(BoardScene.PathOf(go.transform))) return new Component[0];
            return go.GetComponentsInChildren(maskBase, true).Where(c => c != null && !Transient(c.transform, go.transform)).ToArray();
        }
        static bool Transient(Transform child, Transform root) {
            for (var t = child; t != null && t != root; t = t.parent) if ((t.gameObject.hideFlags & HideFlags.DontSave) != 0) return true;
            return false;
        }
        static string IdOf(Transform root, Component mask) => (mask.transform == root ? "." : BoardScene.PathOf(mask.transform).Substring(BoardScene.PathOf(root).Length)) + "|" + mask.GetType().FullName + "#" + mask.GetInstanceID();
        static GameObject Object(object target) => target as GameObject ?? throw new InvalidOperationException("The object no longer exists");
        public IReadOnlyList<GroundMaskState> Masks(object target) {
            var go = Object(target);
            return Components(go).Select(mask => State(go.transform, mask)).ToArray();
        }
        GroundMaskState State(Transform root, Component mask) {
            var centre = NativeInventory.MapPosition(mask.transform);
            var registrations = new List<GroundMaskRegistration>(); var missing = 0;
            var held = ((IEnumerable<string>)keys.GetValue(mask)).ToArray();
            foreach (var key in held) {
                var stored = terrain.StoredModifier(key);
                if (stored == null) { missing++; continue; }
                registrations.Add(Registration(Native.Read(stored, "Mask")));
            }
            var enabled = mask is Behaviour behaviour && behaviour.isActiveAndEnabled;
            // A mask of another kind than the shaped ones makes its modifiers in its own way: it counts what it holds.
            var modifiers = shaped.IsInstanceOfType(mask) ? Modifiers(mask) : held.Length;
            return new GroundMaskState(IdOf(root, mask), mask.gameObject.name, enabled, Flattens(mask), new double[] { centre.x, centre.y, centre.z }, Axis(mask), modifiers, registrations, missing);
        }
        // The terrain modifiers a shaped mask makes when it registers. Railroader 0.6.3, Map.Runtime.dll,
        // MapMaskBase.ApplyModifiers: one HeightmapModifier when flattenHeightmap is set, and one MaskModifier for each
        // mask target that TryCreateModifier accepts: a Biome target with a biome code from 1 to 255, and any other
        // target whose mask name the MaskName enum defines. MaskComponentBase.AddModifier stores one key for each.
        static int Modifiers(Component mask) {
            var count = (bool)flatten.GetValue(mask) ? 1 : 0;
            if (!(targets.GetValue(mask) is IEnumerable list)) return count;
            foreach (var target in list) {
                if (Convert.ToString(targetKind.GetValue(target)) == "Biome") { var code = (int)targetBiome.GetValue(target); if (code > 0 && code <= 255) count++; }
                else { var name = targetName.GetValue(target); if (Enum.IsDefined(name.GetType(), name)) count++; }
            }
            return count;
        }
        // The direction a rectangle mask's shape has on the ground where its object stands. Railroader 0.6.3,
        // Map.Runtime.dll: RectangleMapMask.MakeMaskDescriptor passes Quaternion.Euler(0, degrees, 0) * transform.rotation
        // and the size (sizeX, 0, sizeZ) to RectangleMaskDescriptor, whose constructor takes MaskRotation.YawDegrees of
        // that rotation, swaps the sides and adds 90 degrees when sizeX is the longer one, and lays the axis along the
        // longer side. A rectangle of size zero and every other kind of mask has no direction.
        static double[] Axis(Component mask) {
            if (!rectangle.IsInstanceOfType(mask)) return null;
            float x = (float)sizeX.GetValue(mask), z = (float)sizeZ.GetValue(mask);
            if (Math.Max(x, z) <= 0.001f) return null;
            var rotation = Quaternion.Euler(0f, (float)degrees.GetValue(mask), 0f) * mask.transform.rotation;
            var forward = rotation * Vector3.forward;
            if (forward.x * forward.x + forward.z * forward.z < 1E-06f) forward = Vector3.Cross(rotation * Vector3.right, Vector3.up);
            var yaw = Mathf.Atan2(forward.x, forward.z) + (x > z ? Mathf.PI / 2 : 0);
            return new double[] { Math.Sin(yaw), 0, Math.Cos(yaw) };
        }
        // A stored rectangle shape (RectangleMaskDescriptor) holds the two ends of its long axis: EndpointA and EndpointB
        // are the centre plus and minus half the axis, so their midpoint is the centre the mask was made at. Any other
        // shape counts with the centre of its bounds. MapManager.AddModifier offsets a modifier added in world
        // coordinates to map coordinates (OffsetBy), which moves both ends alike, and the modifiers keep their shape as
        // given (HeightmapModifier and MaskModifier store it unchanged).
        static GroundMaskRegistration Registration(object descriptor) {
            var type = descriptor.GetType();
            FieldInfo a = type.GetField("EndpointA", Native.Flags), b = type.GetField("EndpointB", Native.Flags);
            if (a?.FieldType == typeof(Vector3) && b?.FieldType == typeof(Vector3)) {
                Vector3 first = (Vector3)a.GetValue(descriptor), second = (Vector3)b.GetValue(descriptor);
                var centre = (first + second) / 2; var along = first - second; along.y = 0;
                return new GroundMaskRegistration(new double[] { centre.x, centre.y, centre.z }, along.magnitude <= 0.001f ? null : new double[] { along.normalized.x, 0, along.normalized.z });
            }
            var bounds = (Bounds)Native.Read(descriptor, "Bounds");
            return new GroundMaskRegistration(new double[] { bounds.center.x, bounds.center.y, bounds.center.z }, null);
        }
        public void Rebuild(object target, string id) {
            var go = Object(target);
            var mask = Components(go).FirstOrDefault(c => IdOf(go.transform, c) == id) ?? throw new InvalidOperationException("The ground mask no longer exists");
            rebuild.Invoke(mask, null);
        }
        public bool TileLoaded(double[] position) => terrain.TileLoaded(position);
        public void InvalidateTile(double[] position) => terrain.InvalidateTile(position);
        public bool TerrainIdle => terrain.TerrainIdle;
        public double Now => terrain.Now;
        public event Action<double[], double[]> TileBuilt { add { terrain.TileBuilt += value; } remove { terrain.TileBuilt -= value; } }
    }
}
