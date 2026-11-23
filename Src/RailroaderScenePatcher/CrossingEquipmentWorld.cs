// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RailroaderScenePatcher {
    // Marks the patcher's own wrapper of one equipment addition beneath a crossing controller and records what it was built
    // from, so a later application in the same world reconciles it instead of building another. The source's name and kind
    // are stored at construction, so the addition keeps its name while its copy object is gone.
    internal sealed class OwnedEquipment : MonoBehaviour {
        internal string Scope, Id, Pattern, SourceScene, SourcePath, SourceDefinition, SourceName;
        internal bool IsBell;
        internal GameObject Controller, Copy;
        internal bool Retired;
        internal EquipmentSource Source => SourceDefinition != null ? EquipmentSource.ForDefinition(SourceDefinition) : new EquipmentSource(SourceScene, SourcePath);
        (string Scope, string Id, string Name, bool IsBell) NameParts => (Scope, Id, SourceName, IsBell);
        internal string DisplayName => InventoryPresentationState.AddedEquipmentName(NameParts,
            Controller == null ? new[] { NameParts } : At(Controller).Select(item => item.NameParts));
        // Records the addition and the source it is built from on a new wrapper beneath its controller.
        internal static OwnedEquipment Mark(GameObject wrapper, GameObject controller, EquipmentAddition addition, string sourceName) {
            var owned = wrapper.AddComponent<OwnedEquipment>();
            owned.Scope = addition.Scope; owned.Id = addition.Id; owned.Pattern = addition.Pattern; owned.Controller = controller;
            owned.SourceScene = addition.Source.Scene; owned.SourcePath = addition.Source.Path; owned.SourceDefinition = addition.Source.Definition;
            owned.IsBell = addition.Pattern == EquipmentAddition.BellPattern; owned.SourceName = sourceName;
            return owned;
        }
        internal static IEnumerable<OwnedEquipment> At(GameObject controller) => Enumerable.Range(0, controller.transform.childCount)
            .Select(i => controller.transform.GetChild(i).GetComponent<OwnedEquipment>()).Where(owned => owned != null && !owned.Retired);
        // The addition this object belongs to, or null.
        internal static OwnedEquipment Of(GameObject go) {
            for (var t = go == null ? null : go.transform; t != null; t = t.parent) { var owned = t.GetComponent<OwnedEquipment>(); if (owned != null) return owned; }
            return null;
        }
    }
    // Crossing lights and gates: the light/gate pattern of the game's crossing equipment. A lamp
    // (Track.KeyValueLampMaterialController) and a gate (RollingStock.Controls.KeyValueBoolAnimator) each find the nearest
    // KeyValue.Runtime.KeyValueObject among their parents when they initialize and observe one Boolean key of it. A crossing
    // controller publishes its lights and gates keys on the key-value object of its own GameObject. A copy of a conforming
    // device placed beneath the controller is therefore addressed by that controller, and initializes through the game's
    // restore notifier after it is enabled. The controller's GameObjectCuller switches the devices in its list on and off;
    // the copy joins that list and takes the culler's current state. Model, material and animation assets are shared with the
    // source and stay loaded with the map; the copy owns only its GameObjects.
    internal sealed class CrossingEquipmentWorld : ICrossingEquipmentWorld, ICrossingEquipmentVerificationDiagnostics {
        const string LampType = "Track.KeyValueLampMaterialController", GateType = "RollingStock.Controls.KeyValueBoolAnimator", OwnerType = "KeyValue.Runtime.KeyValueObject",
            CullerType = "Helpers.Culling.GameObjectCuller", SpanType = "Track.TrackSpan", BellType = "Audio.IntegerLoopingPlayer", AnimatorType = "UnityEngine.Animator", TransformerType = "Helpers.WorldTransformer";
        internal const string WrapperPrefix = "CrankyAnt Scene Patcher equipment ";
        readonly Type controller, lamp, gate, keyOwner, culler, span, bell, animator, transformer;
        readonly FieldInfo lampKey, lampTargets, lampMaterials, gateKey, gateClip, cullerObjects, cullerState, lampObserver, gateObserver;
        readonly FieldInfo materialShared, materialProperty;
        readonly string[] keys;
        // The controller-bound bell pattern, or null with the reason when this build lacks one of its prerequisites; lights
        // and gates stay available either way.
        readonly CrossingBells bells;
        readonly string bellsUnavailable;
        internal CrossingEquipmentWorld() {
            controller = Native.Type(CrossingCoverageScene.ComponentType); lamp = Native.Type(LampType); gate = Native.Type(GateType); keyOwner = Native.Type(OwnerType);
            culler = Native.Type(CullerType); span = Native.Type(SpanType); bell = Native.Type(BellType); animator = Native.Type(AnimatorType); transformer = Native.Type(TransformerType);
            if (controller == null || lamp == null || gate == null || keyOwner == null || culler == null || span == null || animator == null || transformer == null) throw new InvalidOperationException("Crossing-equipment prerequisites unavailable");
            lampKey = Field(lamp, "key"); lampTargets = Field(lamp, "targetObjects"); lampMaterials = Field(lamp, "materialTargets");
            gateKey = Field(gate, "key"); gateClip = Field(gate, "animationClip");
            cullerObjects = Field(culler, "gameObjects"); cullerState = Field(culler, "_cullingState");
            if (lampKey.FieldType != typeof(string) || gateKey.FieldType != typeof(string) || lampTargets.FieldType != typeof(GameObject[]) || !lampMaterials.FieldType.IsArray) throw new InvalidOperationException("Crossing lamp or gate fields changed");
            if (!typeof(IList).IsAssignableFrom(cullerObjects.FieldType) || !cullerState.FieldType.IsEnum || !Enum.GetNames(cullerState.FieldType).Contains("NotVisible")) throw new InvalidOperationException("Crossing culler fields changed");
            var material = lampMaterials.FieldType.GetElementType();
            materialShared = Field(material, "sharedMaterial"); materialProperty = Field(material, "materialProperty");
            lampObserver = lamp.GetField("_observer", Native.Flags); gateObserver = gate.GetField("_observer", Native.Flags);
            // The keys the controller publishes are its own constants; a device observing another key would never respond.
            keys = new[] { "KeyLights", "KeyGates" }.Select(name => {
                var field = controller.GetField(name, Native.Flags);
                if (field == null || !field.IsLiteral || !(field.GetRawConstantValue() is string value) || String.IsNullOrEmpty(value)) throw new MissingFieldException(CrossingCoverageScene.ComponentType, name);
                return value;
            }).ToArray();
            foreach (var method in new[] { "WorldToGame", "GameToWorld" })
                if (transformer.GetMethods(Native.Flags).Count(m => m.Name == method && m.GetParameters().Length == 1) != 1) throw new MissingMethodException(TransformerType, method);
            try { bells = new CrossingBells(controller); } catch (Exception e) { bellsUnavailable = e.GetBaseException().Message; }
        }
        // Why bells are unavailable on this build, or null.
        internal string BellsUnavailable => bellsUnavailable;
        // Configures a controller's own bell again after it was enabled, or null while bells are unavailable on this build.
        internal Action<GameObject> ReconfigureBell => bells == null ? (Action<GameObject>)null : bells.ReconfigureNative;
        // Raises the warning of a bell whose audio fails to load after the apply: key, message and originator.
        internal Action<string, string, string> Warn { set { if (bells != null) bells.Warn = value; } }
        sealed class UnavailableBell { }
        static FieldInfo Field(Type type, string name) => type.GetField(name, Native.Flags) ?? throw new MissingFieldException(type.FullName, name);
        static bool Loaded(GameObject g) => g != null && g.scene.IsValid() && g.scene.isLoaded;
        public void VerificationFailed(object target, string detail) {
            var path = BoardScene.PathOf(((GameObject)target).transform).Replace("\r", "\\r").Replace("\n", "\\n");
            Debug.LogWarning("Scene Patcher crossing equipment verification failed: destination=" + path + " " + detail);
        }
        static Component[] All(GameObject root, Type type) => root.GetComponentsInChildren(type, true).Where(c => c != null).ToArray();
        static bool Inside(GameObject root, GameObject child) { for (var t = child == null ? null : child.transform; t != null; t = t.parent) if (t == root.transform) return true; return false; }

        public IReadOnlyList<object> ResolveSource(EquipmentSource source) {
            // A missing bell capability resolves to a placeholder, so the refusal names the capability, not a missing definition.
            if (source.IsDefinition) return bells == null ? new object[] { new UnavailableBell() } : bells.Resolve(source.Definition).Cast<object>().ToArray();
            var names = source.Path.Split('/');
            var found = new List<GameObject>();
            for (var i = 0; i < SceneManager.sceneCount; i++) {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || scene.name != source.Scene) continue;
                IEnumerable<Transform> level = scene.GetRootGameObjects().Select(g => g.transform).Where(t => t.name == names[0]);
                for (var n = 1; n < names.Length; n++) { var name = names[n]; level = level.SelectMany(t => Enumerable.Range(0, t.childCount).Select(t.GetChild)).Where(t => t.name == name).ToList(); }
                found.AddRange(level.Select(t => t.gameObject));
            }
            return found.Where(g => OwnedEquipment.Of(g) == null).Cast<object>().ToArray();
        }
        public string SourceProblem(object source, object target, string pattern) {
            if (source is UnavailableBell) return "crossing bells are unavailable on this game build: " + bellsUnavailable;
            if (source is BellSource bell) {
                if (pattern != EquipmentAddition.BellPattern) return "pattern " + pattern + " cannot use a definition as its source";
                var controllerObject = target as GameObject;
                if (!Loaded(controllerObject)) return "the destination crossing is not loaded";
                if (!controllerObject.activeInHierarchy) return "the destination crossing controller is inactive, so an added bell would not start";
                return BellPattern.Problem(bells.Facts(bell, controllerObject));
            }
            if (pattern == EquipmentAddition.BellPattern) return "a loaded device is not a bell definition";
            var go = source as GameObject;
            if (!Loaded(go)) return "it is not a loaded object";
            if (pattern != EquipmentAddition.LightGatePattern) return "the pattern " + pattern + " is not supported";
            var destination = target as GameObject;
            if (!Loaded(destination)) return "the destination crossing is not loaded";
            return LightGatePattern.Problem(Facts(go, destination, true), keys);
        }
        // What the light/gate pattern reads from the device and its destination. A copy under construction is read without its
        // ownership, which marks every addition.
        DeviceFacts Facts(GameObject go, GameObject destination, bool ownership) {
            var facts = new DeviceFacts {
                AddedByPatcher = ownership && OwnedEquipment.Of(go) != null,
                DestinationKeyValueObjects = destination.GetComponents(keyOwner).Length,
                DestinationCullers = destination.GetComponents(culler).Length,
                ContainsDestination = Inside(go, destination),
                OtherScene = go.scene != destination.scene,
                MissingScript = go.GetComponentsInChildren<Component>(true).Any(c => c == null),
                Controllers = All(go, controller).Length, KeyValueObjects = All(go, keyOwner).Length, Spans = All(go, span).Length,
                Cullers = All(go, culler).Length, Bells = bell == null ? 0 : All(go, bell).Length
            };
            foreach (var l in All(go, lamp)) {
                var targets = lampTargets.GetValue(l) as GameObject[] ?? new GameObject[0];
                var materials = (lampMaterials.GetValue(l) as Array)?.Cast<object>().Where(m => m != null).ToArray() ?? new object[0];
                var shared = materials.Select(m => materialShared.GetValue(m) as Material).Where(m => m != null).ToArray();
                var used = targets.Where(t => t != null).SelectMany(t => t.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m != null);
                facts.Lamps.Add(new LampFacts {
                    Name = l.name, Key = lampKey.GetValue(l) as string, Targets = targets.Length, MissingTargets = targets.Count(t => t == null),
                    OutsideTargets = targets.Count(t => t != null && !Inside(go, t)), Materials = shared.Length,
                    MaterialsWithoutProperty = materials.Count(m => String.IsNullOrEmpty(materialProperty.GetValue(m) as string)), RendererUsesMaterial = used.Any(m => shared.Contains(m))
                });
            }
            foreach (var g in All(go, gate))
                facts.Gates.Add(new GateFacts { Name = g.name, Key = gateKey.GetValue(g) as string, Animator = g.GetComponent(animator) != null, Clip = gateClip.GetValue(g) is UnityEngine.Object clip && clip != null });
            return facts;
        }
        // The devices beneath every loaded crossing controller: its direct children that hold lamps, gates or bell players, or
        // are culled with it. The controller's generated bell player is listed too, and rejected with its reason, so the
        // scene patch editing explains why a bell does not qualify. The patcher's own additions are not offered.
        public IReadOnlyList<SourceCandidate> Candidates(object target) {
            var result = new List<SourceCandidate>();
            foreach (var owner in Resources.FindObjectsOfTypeAll(controller).OfType<Component>().Where(c => c != null && Loaded(c.gameObject)).Select(c => c.gameObject).Distinct()) {
                var listed = owner.GetComponents(culler).SelectMany(c => (cullerObjects.GetValue(c) as IList)?.Cast<object>() ?? Enumerable.Empty<object>()).OfType<GameObject>().ToList();
                for (var i = 0; i < owner.transform.childCount; i++) {
                    var child = owner.transform.GetChild(i).gameObject;
                    if (child.GetComponent<OwnedEquipment>() != null) continue;
                    var device = All(child, lamp).Length + All(child, gate).Length > 0 || (bell != null && All(child, bell).Length > 0);
                    if (!device && !listed.Contains(child)) continue;
                    var model = All(child, gate).Select(g => g.name).Concat(All(child, lamp).Select(l => l.name)).FirstOrDefault() ?? child.name;
                    Placement(child.transform, out var position, out var rotation);
                    Placement(owner.transform, out var crossingPosition, out var crossingRotation);
                    result.Add(new SourceCandidate(new EquipmentSource(child.scene.name, BoardScene.PathOf(child.transform)), model, SourceProblem(child, target, EquipmentAddition.LightGatePattern),
                        position, rotation, child.name, CrossingEquipment.Place(owner.name, Ancestors(owner.transform)), crossingPosition, crossingRotation));
                }
            }
            // Every bell definition of the content library, eligible or with its reason, for the inspected controller.
            // A failure to read them leaves the devices listed and names the reason in one rejected entry.
            if (bells == null) result.Add(SourceCandidate.UnavailableBells("crossing bells are unavailable on this game build: " + bellsUnavailable));
            else if (target is GameObject destination && Loaded(destination)) {
                try {
                    var found = new List<SourceCandidate>();
                    foreach (var bell in bells.All()) found.Add(SourceCandidate.Bell(bell.Identifier, bell.Name, SourceProblem(bell, destination, EquipmentAddition.BellPattern)));
                    result.AddRange(found);
                } catch (Exception e) { result.Add(SourceCandidate.UnavailableBells("cannot read bell definitions from the content library: " + e.GetBaseException().Message)); }
            }
            return result;
        }
        // A map placement, or nulls when the floating origin cannot convert it.
        void Placement(Transform t, out double[] position, out double[] rotation) {
            position = null; rotation = null;
            try { var p = (Vector3)Native.Call(transformer, "WorldToGame", t.position); var r = t.rotation; position = new double[] { p.x, p.y, p.z }; rotation = new double[] { r.x, r.y, r.z, r.w }; } catch { }
        }
        // The names of the objects that enclose a transform, from its parent upwards.
        static IReadOnlyList<string> Ancestors(Transform t) { var names = new List<string>(); for (var p = t.parent; p != null; p = p.parent) names.Add(p.name); return names; }
        public IReadOnlyList<object> Owned(object target) => OwnedEquipment.At((GameObject)target).Cast<object>().ToArray();
        public EquipmentAddition Describe(object addition) {
            var owned = (OwnedEquipment)addition;
            return new EquipmentAddition(owned.Id, owned.Pattern, owned.Source, null, owned.Scope);
        }
        static string Safe(string text) => new string(text.Select(c => c == '/' ? ':' : c).ToArray());
        Component CullerOf(GameObject target) {
            var cullers = target.GetComponents(culler);
            if (cullers.Length > 1) throw new InvalidOperationException("The crossing controller holds several cullers; the copy's culling would be ambiguous");
            return cullers.Length == 1 ? cullers[0] : null;
        }
        // Builds the wrapper and copy disabled, checks the copy, joins the culler and enables the wrapper last, so the copy
        // initializes through the controller only when it is complete.
        public object Construct(object target, object source, EquipmentAddition addition) {
            if (addition.Pattern == EquipmentAddition.BellPattern) return ConstructBell((GameObject)target, source, addition);
            var destination = (GameObject)target; var device = (GameObject)source;
            var problem = SourceProblem(device, destination, addition.Pattern);
            if (problem != null) throw new InvalidOperationException("The source does not conform: " + problem);
            var cull = CullerOf(destination);
            var wrapper = new GameObject(WrapperPrefix + Safe(addition.Scope) + " " + addition.Id) { hideFlags = HideFlags.DontSave };
            wrapper.SetActive(false);
            IList list = null; GameObject copy = null;
            try {
                wrapper.transform.SetParent(destination.transform, false);
                var owned = OwnedEquipment.Mark(wrapper, destination, addition, addition.Source.StoredName());
                // The wrapper stands at the addition's placement and the copy at the wrapper's origin, so the wrapper, which the
                // inventory lists as the added object, is where the light or gate is.
                Place(wrapper, addition.Placement);
                copy = UnityEngine.Object.Instantiate(device, wrapper.transform, false);
                copy.name = device.name; copy.hideFlags = HideFlags.DontSave; owned.Copy = copy;
                copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.identity;
                var parent = wrapper.transform.lossyScale; var scale = device.transform.lossyScale;
                copy.transform.localScale = new Vector3(Ratio(scale.x, parent.x), Ratio(scale.y, parent.y), Ratio(scale.z, parent.z));
                copy.SetActive(true);
                var built = CopyProblem(device, copy, destination);
                if (built != null) throw new InvalidOperationException(built);
                if (cull != null) {
                    list = (IList)cullerObjects.GetValue(cull);
                    list.Add(copy);
                    if (Convert.ToString(cullerState.GetValue(cull)) == "NotVisible") copy.SetActive(false);
                }
                wrapper.SetActive(true);
                return owned;
            } catch {
                if (list != null && copy != null) list.Remove(copy);
                wrapper.SetActive(false);
                UnityEngine.Object.Destroy(wrapper);
                throw;
            }
        }
        // Builds the bell disabled: its wrapper at the placement, the player bound to the controller's bell key with its audio
        // loading, then enables the wrapper. A bell joins no culler, as the controller's own bell player does not.
        object ConstructBell(GameObject destination, object source, EquipmentAddition addition) {
            var problem = SourceProblem(source, destination, addition.Pattern);
            if (problem != null) throw new InvalidOperationException("The source does not conform: " + problem);
            var bell = (BellSource)source;
            // Unity releases a bell's audio on scene unload only for a bell that was active, so a bell is built only beneath
            // an active controller. SourceProblem refuses that before any change; this guards the construction itself.
            if (!destination.activeInHierarchy) throw new InvalidOperationException("The crossing controller is inactive, so an added bell would not start");
            var wrapper = new GameObject(WrapperPrefix + Safe(addition.Scope) + " " + addition.Id) { hideFlags = HideFlags.DontSave };
            wrapper.SetActive(false);
            GameObject player = null;
            try {
                wrapper.transform.SetParent(destination.transform, false);
                var owned = OwnedEquipment.Mark(wrapper, destination, addition, addition.Source.StoredName(bell.Name));
                Place(wrapper, addition.Placement);
                player = bells.Construct(wrapper, destination, bell, addition.Scope, addition.Id);
                owned.Copy = player;
                wrapper.SetActive(true);
                return owned;
            } catch {
                player?.GetComponent<OwnedBell>()?.Release();
                wrapper.SetActive(false);
                UnityEngine.Object.Destroy(wrapper);
                throw;
            }
        }
        static float Ratio(float a, float b) => Math.Abs(b) < 1e-6f ? a : a / b;
        // Why the copy is not a separate working device addressed by the destination controller, or null.
        string CopyProblem(GameObject source, GameObject copy, GameObject destination) {
            var problem = LightGatePattern.Problem(Facts(copy, destination, false), keys);
            if (problem != null) return "the copy " + problem;
            var originals = All(source, lamp).Concat(All(source, gate)).ToArray();
            var copies = All(copy, lamp).Concat(All(copy, gate)).ToArray();
            if (originals.Length != copies.Length || copies.Any(c => originals.Contains(c))) return "the copy shares lamp or gate components with its source";
            if (All(copy, gate).Any(g => !ReferenceEquals(gateClip.GetValue(g), gateClip.GetValue(All(source, gate)[Array.IndexOf(All(copy, gate), g)])))) return "the copy's gate animation differs from its source";
            var nearest = NearestOwner(copy.transform);
            if (nearest == null || nearest.gameObject != destination) return "the copy's nearest key-value object is not the destination controller's";
            return null;
        }
        Component NearestOwner(Transform node) {
            for (var t = node; t != null; t = t.parent) { var found = t.GetComponent(keyOwner); if (found != null) return found; }
            return null;
        }
        // The culler leaves first, so it never switches a destroyed copy; disabling stops the copy's observers.
        public void Destroy(object addition) {
            var owned = (OwnedEquipment)addition;
            if (owned == null) return;
            var copy = owned.Copy;
            // A bell stops, gives up its observer and its audio reference, and cancels a load still running.
            if (copy != null) copy.GetComponent<OwnedBell>()?.Release();
            if (owned.Controller != null && copy != null) foreach (var c in owned.Controller.GetComponents(culler)) (cullerObjects.GetValue(c) as IList)?.Remove(copy);
            owned.Retired = true;
            var wrapper = owned.gameObject;
            wrapper.SetActive(false);
            UnityEngine.Object.Destroy(wrapper);
        }
        public bool Built(object addition) { var owned = (OwnedEquipment)addition; return owned.Copy != null && owned.Copy.transform.parent == owned.transform; }
        public bool Present(object addition) { var owned = (OwnedEquipment)addition; return owned != null && !owned.Retired && owned.Controller != null && owned.transform.parent == owned.Controller.transform; }
        public Placement Placement(object addition) {
            var copy = ((OwnedEquipment)addition).gameObject;
            var p = (Vector3)Native.Call(transformer, "WorldToGame", copy.transform.position); var r = copy.transform.rotation;
            return new Placement(new double[] { p.x, p.y, p.z }, new double[] { r.x, r.y, r.z, r.w });
        }
        public void SetPlacement(object addition, Placement placement) => Place(((OwnedEquipment)addition).gameObject, placement);
        void Place(GameObject copy, Placement placement) {
            if (Native.Read(transformer, "Shared") == null) throw new InvalidOperationException("Floating origin unavailable");
            var game = new Vector3(Checked(placement.Position[0]), Checked(placement.Position[1]), Checked(placement.Position[2]));
            var local = (Vector3)Native.Call(transformer, "GameToWorld", game);
            if (((Vector3)Native.Call(transformer, "WorldToGame", local) - game).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Map/origin conversion failed its round trip");
            copy.transform.SetPositionAndRotation(local, new Quaternion(Checked(placement.Rotation[0]), Checked(placement.Rotation[1]), Checked(placement.Rotation[2]), Checked(placement.Rotation[3])).normalized);
        }
        static float Checked(double value) {
            var f = (float)value;
            if (Single.IsNaN(f) || Single.IsInfinity(f)) throw new InvalidOperationException("Placement exceeds native precision");
            return f;
        }
        public string Problem(object addition) {
            var owned = (OwnedEquipment)addition;
            var copy = owned.Copy;
            if (!Built(owned)) return "its copy is missing";
            if (!owned.gameObject.activeSelf) return "it is disabled";
            if (owned.Pattern == EquipmentAddition.BellPattern) return bells == null ? "crossing bells are unavailable on this game build: " + bellsUnavailable : bells.Problem(copy.GetComponent<OwnedBell>(), owned.Controller);
            var device = LightGatePattern.Problem(Facts(copy, owned.Controller, false), keys);
            if (device != null) return "the copy " + device;
            var nearest = NearestOwner(copy.transform);
            if (nearest == null || nearest.gameObject != owned.Controller) return "its nearest key-value object is not its crossing controller's";
            Component cull;
            try { cull = CullerOf(owned.Controller); } catch (InvalidOperationException e) { return e.Message; }
            if (cull != null) {
                var list = (IList)cullerObjects.GetValue(cull);
                if (list.Cast<object>().Count(o => ReferenceEquals(o, copy)) != 1) return "it is not listed once in its crossing's culler";
                var state = Convert.ToString(cullerState.GetValue(cull));
                if (state != "Unknown" && copy.activeSelf != (state == "Visible")) return "its activity differs from its crossing's culling state " + state;
            }
            return null;
        }
        public string Status(object addition) {
            var owned = (OwnedEquipment)addition;
            var copy = owned.Copy;
            if (copy == null) return "copy missing";
            if (owned.Pattern == EquipmentAddition.BellPattern) return bells == null ? "crossing bells are unavailable" : bells.Status(copy.GetComponent<OwnedBell>(), owned.Controller);
            if (!copy.activeInHierarchy) return "hidden with the crossing. It connects when visible";
            int Registered(Type type, FieldInfo observer) => observer == null ? -1 : All(copy, type).Count(c => observer.GetValue(c) != null);
            var lamps = All(copy, lamp).Length; var gates = All(copy, gate).Length;
            var lampsOn = Registered(lamp, lampObserver); var gatesOn = Registered(gate, gateObserver);
            if (lampsOn < 0 || gatesOn < 0) return "visible. Connection to the crossing could not be checked";
            return lampsOn == lamps && gatesOn == gates ? "visible. Connected to the crossing" : "visible. Waiting to connect to the crossing";
        }
    }
}
