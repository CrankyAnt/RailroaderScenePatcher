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
    // The native operations of the existing crossing equipment capability: the light object of a mast, with its gate on
    // a child, and the bell player a crossing controller creates. The crossing controller's object is the equipment's
    // group: its transforms, cullers and own culling are read and written through the group world, so a moved mast
    // culls as a moved child object does. The rules live in Core's ExistingEquipmentPattern and
    // ExistingEquipmentScene.
    //
    // Railroader 0.6.3: Track.KeyValueLampMaterialController and RollingStock.Controls.KeyValueBoolAnimator take the
    // nearest KeyValue.Runtime.KeyValueObject among their parents when they are enabled and observe one key of it, so
    // a lamp or gate keeps its controller wherever its object stands below that controller, and takes the key's
    // current value again when it is enabled. Track.Crossings.GradeCrossingController creates its bell player as a
    // child named Crossing Bell and sets its play field itself. Audio.IntegerLoopingPlayer destroys the clips it
    // split when it is disabled and splits none when enabled, so a bell that is enabled again is configured again
    // with the controller's loaded clip.
    //
    // The controller's object is also the root of the whole move of its grade-crossing setup (WholeMove): everything
    // below it moves with its transform, and the crossing pads, the whistle signs and the detection spans are put
    // back where they stood (Stays). A detection span reads its two track locations and never its transform
    // (Track.TrackSpan), and a crossing pad is generated along its marker's track location, so neither follows a move.
    internal sealed class CrossingDeviceWorld : IWholeMoveWorld {
        const string ControllerType = CrossingCoverageScene.ComponentType, LampType = ExistingEquipmentScene.MastComponent, GateType = ExistingEquipmentScene.GateComponent,
            BellType = ExistingEquipmentScene.BellComponent, OwnerType = "KeyValue.Runtime.KeyValueObject", SpanType = "Track.TrackSpan", RelativeType = "Track.TrackRelativePosition";
        static readonly Type controller = Native.Type(ControllerType), lamp = Native.Type(LampType), gate = Native.Type(GateType), bell = Native.Type(BellType), keyOwner = Native.Type(OwnerType),
            span = Native.Type(SpanType), relative = Native.Type(RelativeType), switcher = Native.Type(GroupWorld.GameObjectCullerType), marker = Native.Type(TrackMarkerWorld.MarkerType);
        static readonly FieldInfo lampKey = lamp?.GetField("key", Native.Flags), lampTargets = lamp?.GetField("targetObjects", Native.Flags), gateKey = gate?.GetField("key", Native.Flags),
            bellPlayer = controller?.GetField("_bellPlayer", Native.Flags), circuitSets = controller?.GetField("circuitSets", Native.Flags), followsGroup = relative?.GetField("targetActiveFollowsTrackGroup", Native.Flags);
        static readonly string[] keys = Keys();
        static string[] Keys() {
            try {
                return new[] { "KeyLights", "KeyGates" }.Select(name => {
                    var field = controller?.GetField(name, Native.Flags);
                    return field != null && field.IsLiteral && field.GetRawConstantValue() is string value && value.Length != 0 ? value : throw new MissingFieldException(ControllerType, name);
                }).ToArray();
            } catch { return null; }
        }
        // Why the capability cannot run on this game build, or null. Checked once: a missing member fails the adapter
        // with its name, not a scene edit.
        internal static readonly string Unavailable = Check();
        static string Check() {
            if (controller == null || lamp == null || gate == null || bell == null || keyOwner == null || span == null || switcher == null) return "crossing equipment types are missing";
            if (lampKey?.FieldType != typeof(string) || gateKey?.FieldType != typeof(string) || lampTargets?.FieldType != typeof(GameObject[])) return "the key or target fields of a crossing lamp or gate changed";
            if (bellPlayer == null || circuitSets == null) return "Track.Crossings.GradeCrossingController._bellPlayer or circuitSets is missing";
            if (keys == null) return "the lights and gates keys of Track.Crossings.GradeCrossingController are missing";
            return null;
        }
        readonly GroupWorld groups;
        readonly bool bells;
        // Configures a controller's own bell again after it was enabled, or null when bells cannot be configured on this build.
        readonly Action<GameObject> reconfigure;
        readonly string reconfigureUnavailable;
        internal CrossingDeviceWorld(GroupWorld groups, bool bells, Action<GameObject> reconfigure = null, string reconfigureUnavailable = null) {
            if (Unavailable != null) throw new InvalidOperationException("Existing crossing equipment is unavailable: " + Unavailable);
            this.groups = groups ?? throw new ArgumentNullException(nameof(groups)); this.bells = bells; this.reconfigure = reconfigure; this.reconfigureUnavailable = reconfigureUnavailable;
            if (bells) bellEnable = reconfigure == null ? "This game build cannot reconfigure the bell when it is enabled again: " + (reconfigureUnavailable ?? "crossing bells are unavailable") : null;
        }
        // Why a disabled bell could not be enabled again on this build, or null; shared with the inspector.
        static string bellEnable = "Existing crossing bells are unavailable";
        public IEnumerable<string> MapIds => ModScenePatchFiles.MapIds();
        static bool Loaded(GameObject g) => g != null && g.scene.IsValid() && g.scene.isLoaded;
        static Component[] All(GameObject root, Type type) => root.GetComponentsInChildren(type, true).Where(c => c != null).ToArray();
        static bool Inside(GameObject root, GameObject child) => child != null && child.transform.IsChildOf(root.transform);
        // The object of the crossing controller above the object, or null. A controller's own object stands below none.
        internal static GameObject ControllerOf(GameObject go) {
            if (controller == null || go == null) return null;
            for (var t = go.transform.parent; t != null; t = t.parent) if (t.GetComponent(controller) != null) return t.gameObject;
            return null;
        }
        // Whether a lamp stands on the object or on an object above it, below the crossing controller: the object is
        // then the light object of a mast or a part of one, such as its gate.
        internal static bool OnLightObject(Transform at) {
            if (lamp == null || controller == null) return false;
            for (var t = at; t != null && t.GetComponent(controller) == null; t = t.parent) if (t.GetComponent(lamp) != null) return true;
            return false;
        }
        static GameObject Device(object target, string noun) => target as GameObject ?? throw new InvalidOperationException("The " + noun + " no longer exists");

        // What the mast pattern reads from the object: a light object, or a gate that stands on none.
        static MastFacts Facts(GameObject go) {
            var owner = ControllerOf(go);
            Component nearest = null;
            for (var t = go.transform; t != null && nearest == null; t = t.parent) nearest = t.GetComponent(keyOwner);
            var facts = new MastFacts {
                AddedByPatcher = OwnedEquipment.Of(go) != null,
                MissingScript = go.GetComponentsInChildren<Component>(true).Any(c => c == null),
                BelowController = owner != null && nearest != null && nearest.gameObject == owner,
                Controllers = All(go, controller).Length, KeyValueObjects = All(go, keyOwner).Length, Spans = All(go, span).Length, Cullers = All(go, switcher).Length,
                Bells = All(go, bell).Length, Rigidbodies = go.GetComponentsInChildren<Rigidbody>(true).Length,
                SharedPath = BoardScene.LoadedAt(go.scene.name, BoardScene.PathOf(go.transform)).Count(held => held != null && (held.GetComponent(lamp) != null || held.GetComponent(gate) != null))
            };
            var own = go.GetComponents(lamp).Where(c => c != null).ToArray();
            foreach (var l in own) {
                var targets = lampTargets.GetValue(l) as GameObject[] ?? new GameObject[0];
                facts.Lamps.Add(new LampFacts { Name = l.name, Key = lampKey.GetValue(l) as string, Targets = targets.Length, MissingTargets = targets.Count(t => t == null), OutsideTargets = targets.Count(t => t != null && !Inside(go, t)) });
            }
            facts.LampsBelow = own.Length == 0 ? 0 : All(go, lamp).Length - own.Length;
            // The gates below the object directly above the light object that do not stand at or below the light object.
            var above = go.transform.parent;
            if (own.Length != 0 && above != null && above.gameObject != owner) facts.GatesBeside = All(above.gameObject, gate).Count(g => !Inside(go, g.gameObject));
            foreach (var g in All(go, gate)) facts.Gates.Add(new GateFacts { Name = g.name, Key = gateKey.GetValue(g) as string });
            if (relative != null && followsGroup != null) {
                var followers = All(go, relative).ToList();
                for (var t = go.transform.parent; t != null && (owner == null || t.gameObject != owner); t = t.parent) followers.AddRange(t.GetComponents(relative).Where(c => c != null));
                facts.FollowsTrackGroup = followers.Any(f => followsGroup.GetValue(f) is bool follows && follows);
            }
            return facts;
        }
        // Why the light object, or the gate that stands on none, cannot be edited, or null. Shared with the inventory.
        internal static string MastProblem(GameObject go) {
            if (Unavailable != null) return "existing crossing equipment cannot be edited on this game build: " + Unavailable;
            if (!Loaded(go)) return "its scene is not loaded";
            return ExistingEquipmentPattern.MastProblem(Facts(go), keys);
        }
        // Why the bell player cannot be edited, or null. Shared with the inventory.
        internal static string BellProblem(GameObject go) {
            if (Unavailable != null) return "existing crossing equipment cannot be edited on this game build: " + Unavailable;
            if (!Loaded(go)) return "its scene is not loaded";
            var owner = ControllerOf(go);
            var component = owner == null ? null : owner.GetComponent(controller) as Behaviour;
            var player = go.GetComponent(bell);
            return ExistingEquipmentPattern.BellProblem(new ExistingBellFacts {
                ControllersOwn = component != null && player != null && ReferenceEquals(bellPlayer.GetValue(component), player),
                ControllerActive = component != null && component.isActiveAndEnabled,
                MissingScript = go.GetComponentsInChildren<Component>(true).Any(c => c == null),
                SharedPath = BoardScene.LoadedAt(go.scene.name, BoardScene.PathOf(go.transform)).Count(held => held != null && held.GetComponent(bell) != null)
            });
        }
        // Why Enabled cannot be set for the equipment, or null: a game-object culler on an object above it lists the
        // equipment itself and would override a stored setting; or a bell could not be configured again once enabled.
        internal static string ActivityProblem(GameObject go) {
            if (go == null || switcher == null) return null;
            for (var t = go.transform.parent; t != null; t = t.parent) {
                if (GroupWorld.ListedOn(go, t.gameObject)) return CommandBand.EquipmentSwitched;
                if (controller != null && t.GetComponent(controller) != null) break;
            }
            return bell != null && go.GetComponent(bell) != null ? bellEnable : null;
        }
        // What an inspection shows of a mast: each lamp and gate with the key it observes, and the controller that drives them.
        internal static IEnumerable<string> Describe(GameObject go) {
            var lines = new List<string>();
            if (Unavailable != null || go == null) return lines;
            foreach (var l in All(go, lamp)) lines.Add("Mast light " + l.name + " observes key '" + (lampKey.GetValue(l) as string) + "'");
            foreach (var g in All(go, gate)) lines.Add("Mast gate " + g.name + " observes key '" + (gateKey.GetValue(g) as string) + "'");
            var owner = ControllerOf(go);
            if (owner != null) lines.Add("Driven by crossing controller " + BoardScene.PathOf(owner.transform));
            return lines;
        }
        // The culling membership of a mast or bell, for the inspection's runtime state: which object at or above it
        // the crossing's culler lists, which one own culling took out of that list, and each own culling sphere that
        // shows the equipment or an object above it. It changes with the camera and with edits, and pins nothing.
        internal static IEnumerable<string> CullingState(GameObject go) {
            var lines = new List<string>();
            var owner = ControllerOf(go);
            if (owner == null || switcher == null) return lines;
            var handed = GroupWorld.HandedOver().ToList();
            for (var t = go.transform; t != null && t.gameObject != owner; t = t.parent) {
                if (handed.Any(item => item.Item1 == t.gameObject)) lines.Add("Culling membership: " + BoardScene.PathOf(t) + " was removed from the crossing culler's list and uses separate culling");
                else if (GroupWorld.ListedOn(t.gameObject, owner)) lines.Add("Culling membership: " + BoardScene.PathOf(t) + " is in the crossing culler's list");
            }
            foreach (var region in GroupWorld.OwnSpheres(owner)) {
                var holder = region.Owner as GameObject;
                if (holder == null || !go.transform.IsChildOf(holder.transform)) continue;
                lines.Add("Own culling sphere at " + BoardScene.PathOf(holder.transform) + ": " + (region.Centre == null ? "awaiting registration" : "center " + String.Join(", ", region.Centre.Select(CommandBand.Number)) + "; radius " + CommandBand.Number(region.Radius)) + "; shows object: " + region.Shows);
            }
            if (lines.Count == 0) lines.Add("Culling membership: no game culler enables or disables this object or an object above it");
            return lines;
        }
        // The crossing controller that holds each loaded detection span in a circuit, read once per frame for every
        // span an inspection asks about in it. A coverage edit changes who holds what within a frame: the
        // re-observation after an edit, and every discovery, read the controllers again (InvalidateHolders).
        static Dictionary<Component, GameObject> holders;
        static int holdersFrame = -1;
        internal static void InvalidateHolders() { holders = null; }
        internal static GameObject HolderOf(Component heldSpan) {
            if (Unavailable != null || heldSpan == null) return null;
            if (holders == null || holdersFrame != Time.frameCount) {
                var found = new Dictionary<Component, GameObject>();
                foreach (var owner in Resources.FindObjectsOfTypeAll(controller).OfType<Component>().Where(c => c != null && Loaded(c.gameObject)).OrderBy(c => c.GetInstanceID())) {
                    if (!(circuitSets.GetValue(owner) is IEnumerable sets)) continue;
                    foreach (var set in sets)
                        foreach (var role in CircuitCoverage.Roles) {
                            if (set == null) continue;
                            var member = set.GetType().GetField(role, Native.Flags)?.GetValue(set) as Component;
                            if (member != null && !found.ContainsKey(member)) found[member] = owner.gameObject;
                        }
                }
                holders = found; holdersFrame = Time.frameCount;
            }
            return holders.TryGetValue(heldSpan, out var holder) ? holder : null;
        }

        // The crossing controller is no target of this capability: only its equipment resolves. The crossing capability
        // resolves the controller and moves its setup through this world.
        public IReadOnlyList<object> Find(string scene, string path) => Array.Empty<object>();
        // Why the setup cannot move now, or null: a ground mask below the controller that cannot be read and rebuilt.
        public string Refusal(object group) => GroupWorld.MaskProblem(group as GameObject);
        public void SetActive(object group, bool active) => throw new InvalidOperationException("A crossing controller cannot be enabled or disabled");
        public void SetPlacement(object group, Placement placement) => groups.SetPlacement(Device(group, "crossing controller"), placement);
        // The objects below the controller that keep their map placement when the setup moves as a whole: each track
        // marker, a crossing pad or a whistle sign, whose place its track binding sets, and each detection span.
        // Equipment the patcher added is no part of the observation; the crossing capability keeps its placement.
        public IReadOnlyList<object> Stays(object group) {
            var root = Device(group, "crossing controller");
            var kept = new List<object>();
            foreach (var type in new[] { marker, span }) {
                if (type == null) continue;
                foreach (var held in root.GetComponentsInChildren(type, true))
                    if (held != null && held.gameObject != root && !kept.Contains(held.gameObject)) kept.Add(held.gameObject);
            }
            return kept;
        }
        // Each kept object keeps the scene position and rotation it has, read before the root moves and written back
        // afterwards, so a pad that stays is not placed again from a map position computed through its moved root.
        public void SetPlacementKeeping(object group, Placement placement, IReadOnlyList<object> kept) {
            var root = Device(group, "crossing controller");
            var standing = (kept ?? Array.Empty<object>()).OfType<GameObject>().Where(go => go != null).Select(go => (go.transform, go.transform.position, go.transform.rotation)).ToArray();
            groups.SetPlacement(root, placement);
            foreach (var (transform, position, rotation) in standing) transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();
        }
        // The transforms at and below an animator move while the game runs, as a gate's arm does: they keep only
        // their identity and activity in the check that nothing but the equipment changed.
        static readonly Type animator = Native.Type("UnityEngine.Animator");
        static bool Moves(Transform part, Transform root) {
            for (var t = part; t != null && t != root; t = t.parent) if (animator != null && t.GetComponent(animator) != null) return true;
            return false;
        }
        public GroupObservation Observe(object group) {
            var root = group as GameObject ?? throw new InvalidOperationException("The crossing controller no longer exists");
            return groups.Observe(root, root, part => Moves(part, root.transform));
        }
        public IReadOnlyList<object> FindParts(string scene, string path) =>
            BoardScene.LoadedAt(scene, path).Where(g => Loaded(g) && OwnedEquipment.Of(g) == null && ControllerOf(g) != null && g.GetComponent(bells ? bell : lamp) != null).Cast<object>().ToArray();
        public string PartRefusal(object part) {
            var go = part as GameObject;
            var problem = go == null ? "it no longer exists" : bells ? BellProblem(go) : MastProblem(go);
            return problem == null ? null : "This " + (bells ? "bell" : "mast") + " cannot be edited: " + problem;
        }
        public string ActivityRefusal(object part) => ActivityProblem(part as GameObject);
        // No switch of the game names a crossing device in Railroader 0.6.3. A mast or bell that a progression or a map
        // feature switches is refused a disable that could not hold, with the reason, as a scene object with child objects is.
        public string DisableRefusal(object target) => GroupWorld.DisableProblem(target as GameObject);
        // Existing equipment refuses track-following placement itself (ExistingEquipmentScene).
        public string FollowingRefusal(object target) => null;
        // Crossing masts and bells hold no lake surface.
        public string PlacementRefusal(object target) => null;
        static double[] Coordinates(Vector3 v) => new double[] { v.x, v.y, v.z };
        static double[] Coordinates(Quaternion q) => new double[] { q.x, q.y, q.z, q.w };
        public PartObservation ObservePart(object target) {
            var part = Device(target, bells ? "bell" : "mast");
            var root = ControllerOf(part) ?? throw new InvalidOperationException("The crossing controller of this " + (bells ? "bell" : "mast") + " no longer exists");
            var parent = part.transform.parent;
            var parentPlacement = new Placement(Coordinates(NativeInventory.MapPosition(parent)), Coordinates(parent.rotation));
            return new PartObservation(new ObjectState(new Placement(Coordinates(NativeInventory.MapPosition(part.transform)), Coordinates(part.transform.rotation)), GroupWorld.Authored(part, root)), root, Observe(root), parent.gameObject, parentPlacement);
        }
        public void SetPartActive(object target, bool active) {
            var part = Device(target, bells ? "bell" : "mast");
            // The player destroyed the clips it split when it was disabled and still names them. Its OnEnable starts
            // the loop at once, which would play a destroyed clip while the crossing rings. So the controller's
            // loaded clip configures the player while the bell is still off, and only then is the bell switched on:
            // Configure starts no loop on an inactive player, and OnEnable starts it with the new clips.
            if (bells && active && !part.activeSelf) {
                if (reconfigure == null) throw new InvalidOperationException(bellEnable);
                reconfigure(ControllerOf(part));
            }
            groups.SetPartActive(part, active);
        }
        public void SetPartPlacement(object target, Placement placement) => groups.SetPartPlacement(Device(target, bells ? "bell" : "mast"), placement);
        public void RefreshCulling(object group, CullingPlan plan) => groups.RefreshCulling(group, plan);
        public IReadOnlyList<IReadOnlyList<double>> Presence(object part, IReadOnlyList<object> without) => groups.Presence(part, without);
        public IReadOnlyList<double> Anchor(object part) => groups.Anchor(part);
        public bool Below(object part, object above) => groups.Below(part, above);
        public bool Alive(object handle) => groups.Alive(handle);
    }
}
