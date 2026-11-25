// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // A bell definition of the content library, as the bell pattern resolves it.
    internal sealed class BellSource {
        internal string Identifier, Name;
        internal object Definition;
    }
    // The runtime state of one added bell, on its player's GameObject: the observer of its controller's bell key, the
    // pending audio load and the loaded audio reference it owns. Release gives up all three and is safe to repeat; it runs on
    // removal, on a failed construction and when Unity destroys the bell with its scene.
    internal sealed class OwnedBell : MonoBehaviour {
        internal Component Player;
        internal IDisposable Observer;
        internal CancellationTokenSource Cancel;
        internal IDisposable AudioReference;
        internal double[] Indexes = new double[0];
        internal float Volume = 1;
        internal string LoadProblem;
        internal bool Loaded, Released;
        internal int Signals;
        // The supplying mod's scope, the equipment ID and the controller path, which a load failure's warning names.
        internal string Scope, Id, ControllerPath;
        internal FieldInfo PlayField;
        internal Func<object, bool> ReadBool;
        // Configures the player with the loaded clip again. The player destroys the clips it split when it is disabled and
        // does not split them again when enabled, so a bell enabled again after its controller was disabled reconfigures.
        internal Action Reconfigure;
        // Raises the warning of a failure that leaves the bell silent.
        internal Action<OwnedBell, string> Fail;
        bool enabledOnce;
        // Whichever of this component and the player Unity enables first, one Loop coroutine runs: Configure stops a running
        // Loop before it starts another, and starts none while the player is not yet enabled, whose own OnEnable then starts
        // the one Loop with the clips Configure set.
        void OnEnable() {
            if (enabledOnce && Loaded && !Released && Reconfigure != null) { try { Reconfigure(); } catch (Exception e) { Fail?.Invoke(this, e.GetBaseException().Message); } }
            enabledOnce = true;
        }
        // The controller's bell key changed, or its current value at subscription.
        internal void Signal(object value) {
            Signals++;
            if (Released || Player == null) return;
            PlayField.SetValue(Player, ReadBool(value));
        }
        internal void Release() {
            if (Released) return;
            Released = true;
            try { if (Player != null) PlayField.SetValue(Player, false); } catch { }
            try { Observer?.Dispose(); } catch { }
            Observer = null;
            try { Cancel?.Cancel(); } catch { }
            try { Cancel?.Dispose(); } catch { }
            Cancel = null;
            try { AudioReference?.Dispose(); } catch { }
            AudioReference = null;
        }
        void OnDestroy() => Release();
    }
    // Relays a native observer callback of value type T to a bell.
    internal sealed class BellRelay<T> {
        readonly Action<object> target;
        public BellRelay(Action<object> target) { this.target = target; }
        public void Invoke(T value) => target(value);
    }
    // The controller-bound bell pattern of the game's crossing bell. Track.Crossings.GradeCrossingController creates a child
    // Audio.IntegerLoopingPlayer, resolves its bell definition through TrainController.Shared.ContentLibrary, loads the
    // definition's audio with PrefabStoreExtensions.LoadAssetAsync<AudioClip>, configures the player with the clip, indexes
    // and volume, keeps and later disposes the loaded reference, and sets the player's play field and its key-value object's
    // bell key from the lights state. Locomotive bells (Audio.BellController) load their definition the same way. An added
    // bell is a separate player beneath the controller that follows the same route with a definition of its own, holds its own
    // loaded reference and observes the controller's bell key, so the controller drives it without knowing it.
    internal sealed class CrossingBells {
        const string PlayerType = "Audio.IntegerLoopingPlayer", DefinitionType = "Model.Definition.Data.BellDefinition", ObjectDefinitionType = "Model.Definition.Data.ObjectDefinition",
            ProviderType = "Model.Database.IDefinitionProvider", LibraryType = "Model.Database.IContentLibrary", LocatorType = "Model.Database.AssetLocator",
            ReferenceType = "Model.Definition.Data.AssetReference", StoreExtensions = "Model.Database.PrefabStoreExtensions", TrainType = "TrainController",
            GroupType = "Audio.AudioController+Group", DistanceType = "Audio.AudioDistance", OwnerType = "KeyValue.Runtime.KeyValueObject", ValueType = "KeyValue.Runtime.Value", ClipType = "UnityEngine.AudioClip";
        readonly Type player, definition, objectDefinition, locator, reference, train, owner, value, clip;
        readonly MethodInfo allOfType, tryGet, resolve, load, observe, configure;
        readonly ConstructorInfo locatorFromReference;
        readonly FieldInfo play, mixerGroup, priority, audioDistance;
        readonly PropertyInfo boolValue;
        readonly object bellGroup, distant;
        internal string Key { get; }
        readonly Type controllerType;
        // Raises a warning with a key, a message and the originator it is about; null logs it to the Unity log.
        internal Action<string, string, string> Warn;
        internal CrossingBells(Type controller) {
            controllerType = controller;
            player = Need(PlayerType); definition = Need(DefinitionType); objectDefinition = Need(ObjectDefinitionType); locator = Need(LocatorType); reference = Need(ReferenceType);
            train = Need(TrainType); owner = Need(OwnerType); value = Need(ValueType); clip = Need(ClipType);
            var provider = Need(ProviderType); var library = Need(LibraryType);
            allOfType = provider.GetMethod("AllDefinitionInfosOfType", Native.Flags) ?? throw new MissingMethodException(ProviderType, "AllDefinitionInfosOfType");
            tryGet = provider.GetMethod("TryGetDefinitionInfo", Native.Flags) ?? throw new MissingMethodException(ProviderType, "TryGetDefinitionInfo");
            resolve = library.GetMethod("TryResolveAssetReference", Native.Flags) ?? throw new MissingMethodException(LibraryType, "TryResolveAssetReference");
            load = Need(StoreExtensions).GetMethods(Native.Flags).SingleOrDefault(m => m.Name == "LoadAssetAsync" && m.IsGenericMethodDefinition && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType == locator)
                ?? throw new MissingMethodException(StoreExtensions, "LoadAssetAsync");
            locatorFromReference = locator.GetConstructor(new[] { reference, typeof(string) }) ?? throw new MissingMethodException(LocatorType, ".ctor");
            observe = owner.GetMethods(Native.Flags).SingleOrDefault(m => m.Name == "Observe" && m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters()[2].ParameterType == typeof(bool))
                ?? throw new MissingMethodException(OwnerType, "Observe");
            if (observe.GetParameters()[1].ParameterType != typeof(Action<>).MakeGenericType(value) || !typeof(IDisposable).IsAssignableFrom(observe.ReturnType)) throw new InvalidOperationException("KeyValueObject.Observe changed");
            boolValue = value.GetProperty("BoolValue", Native.Flags) ?? throw new MissingMemberException(ValueType, "BoolValue");
            configure = player.GetMethods(Native.Flags).SingleOrDefault(m => m.Name == "Configure" && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { clip, typeof(float[]), typeof(float) }))
                ?? throw new MissingMethodException(PlayerType, "Configure");
            play = Field(player, "play"); mixerGroup = Field(player, "mixerGroup"); priority = Field(player, "priority"); audioDistance = Field(player, "audioDistance");
            if (play.FieldType != typeof(bool) || priority.FieldType != typeof(int)) throw new InvalidOperationException("IntegerLoopingPlayer fields changed");
            bellGroup = Need(GroupType).GetField("LocomotiveBell", Native.Flags)?.GetValue(null) ?? throw new MissingFieldException(GroupType, "LocomotiveBell");
            distant = Enum.Parse(Need(DistanceType), "Distant");
            if (train.GetProperty("Shared", Native.Flags) == null || train.GetProperty("ContentLibrary", Native.Flags) == null) throw new MissingMemberException(TrainType, "Shared or ContentLibrary");
            var key = controller.GetField("KeyBell", Native.Flags);
            if (key == null || !key.IsLiteral || !(key.GetRawConstantValue() is string bell) || String.IsNullOrEmpty(bell)) throw new MissingFieldException(controller.FullName, "KeyBell");
            Key = bell;
        }
        static Type Need(string name) => Native.Type(name) ?? throw new TypeLoadException(name + " unavailable");
        static FieldInfo Field(Type type, string name) => type.GetField(name, Native.Flags) ?? throw new MissingFieldException(type.FullName, name);
        object Library() {
            var shared = Native.Read(train, "Shared") ?? throw new InvalidOperationException("The train controller is not loaded, so the content library is unavailable");
            return Native.Read(shared, "ContentLibrary") ?? throw new InvalidOperationException("The content library is unavailable");
        }
        // Every bell definition the content library holds.
        internal IReadOnlyList<BellSource> All() {
            var items = (IEnumerable)allOfType.MakeGenericMethod(definition).Invoke(Library(), null);
            return items.Cast<object>().Select(Source).OrderBy(b => b.Identifier, StringComparer.Ordinal).ToList();
        }
        static BellSource Source(object item) {
            var metadata = Native.Read(item, "Metadata");
            return new BellSource { Identifier = (string)Native.Read(item, "Identifier"), Definition = Native.Read(item, "Definition"), Name = metadata == null ? null : Native.Read(metadata, "Name") as string };
        }
        // The definitions under the identifier: its bell definitions, or a definition of another kind for the pattern to
        // reject. The content library keys definitions by identifier, so at most one is found. Nothing is loaded.
        internal IReadOnlyList<BellSource> Resolve(string identifier) {
            var bells = All().Where(b => b.Identifier == identifier).ToList();
            if (bells.Count > 0) return bells;
            var args = new object[] { identifier, null };
            if ((bool)tryGet.MakeGenericMethod(objectDefinition).Invoke(Library(), args) && args[1] != null) return new[] { Source(args[1]) };
            return Array.Empty<BellSource>();
        }
        // What the bell pattern reads from the definition and the destination controller.
        internal BellFacts Facts(BellSource source, GameObject destination) {
            var facts = new BellFacts { DestinationKeyValueObjects = destination.GetComponents(owner).Length, Kind = Native.Read(source.Definition, "Kind") as string };
            if (facts.Kind != "Bell") return facts;
            var audio = Native.Read(source.Definition, "Audio");
            var asset = audio == null ? null : Native.Read(audio, "Audio");
            facts.AudioNamed = asset != null && !(bool)Native.Read(asset, "IsEmpty");
            if (!facts.AudioNamed) return facts;
            facts.Audio = asset.ToString();
            try {
                var args = new[] { locatorFromReference.Invoke(new[] { asset, source.Identifier }), null };
                facts.AudioResolves = (bool)resolve.Invoke(Library(), args);
            } catch (TargetInvocationException) { facts.AudioResolves = false; }
            facts.Indexes = ((Native.Read(audio, "Indexes") as float[]) ?? new float[0]).Select(i => (double)i).ToArray();
            facts.Volume = Convert.ToDouble(Native.Read(audio, "Volume"));
            return facts;
        }
        // Builds the player on its own child of the disabled wrapper, binds it to the controller's bell key and starts loading
        // the definition's audio into a reference the bell owns. The caller enables the wrapper. Throws, releasing what it
        // took, when the audio cannot be requested.
        internal GameObject Construct(GameObject wrapper, GameObject destination, BellSource source, string scope, string id) {
            var facts = Facts(source, destination);
            // The player is named after its source, as a light or gate copy keeps its device's name.
            var go = new GameObject(EquipmentSource.ForDefinition(source.Identifier).StoredName(source.Name)) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(wrapper.transform, false);
            var bell = go.AddComponent<OwnedBell>();
            try {
                var component = go.AddComponent(player);
                mixerGroup.SetValue(component, bellGroup); priority.SetValue(component, EquipmentAddition.BellPriority); audioDistance.SetValue(component, distant); play.SetValue(component, false);
                bell.Player = component; bell.PlayField = play; bell.ReadBool = v => (bool)boolValue.GetValue(v, null);
                bell.Scope = scope; bell.Id = id; bell.ControllerPath = destination.scene.name + "/" + BoardScene.PathOf(destination.transform); bell.Fail = Failed;
                bell.Indexes = facts.Indexes; bell.Volume = (float)facts.Volume;
                var keyOwner = destination.GetComponent(owner) ?? throw new InvalidOperationException("The crossing controller has no key-value object");
                var relayType = typeof(BellRelay<>).MakeGenericType(value);
                var relay = Activator.CreateInstance(relayType, (Action<object>)bell.Signal);
                var callback = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(value), relay, relayType.GetMethod("Invoke"));
                bell.Observer = (IDisposable)observe.Invoke(keyOwner, new object[] { Key, callback, true });
                if (SynchronizationContext.Current == null) throw new InvalidOperationException("Audio must be loaded on the game's main thread");
                var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
                var asset = Native.Read(Native.Read(source.Definition, "Audio"), "Audio");
                bell.Cancel = new CancellationTokenSource();
                Task task;
                try { task = (Task)load.MakeGenericMethod(clip).Invoke(null, new[] { Library(), locatorFromReference.Invoke(new[] { asset, source.Identifier }), bell.Cancel.Token }); }
                catch (TargetInvocationException e) { throw new InvalidOperationException("Its audio cannot be loaded: " + e.GetBaseException().Message); }
                task.ContinueWith(t => Loaded(bell, t), CancellationToken.None, TaskContinuationOptions.None, scheduler);
                return go;
            } catch {
                bell.Release();
                throw;
            }
        }
        // On the main thread after the load ends: a bell that was released meanwhile disposes the late reference at once.
        void Loaded(OwnedBell bell, Task task) {
            var result = task.Status == TaskStatus.RanToCompletion ? Native.Read(task, "Result") as IDisposable : null;
            if (bell == null || bell.Released) { _ = task.Exception; try { result?.Dispose(); } catch { } return; }
            if (task.IsCanceled) { Failed(bell, "the audio load was cancelled"); return; }
            if (task.IsFaulted) { Failed(bell, task.Exception?.GetBaseException().Message ?? "the audio load failed"); return; }
            if (result == null) { Failed(bell, "no audio reference was returned after loading"); return; }
            try {
                var audio = Native.Read(result, "Asset") as UnityEngine.Object;
                if (audio == null) throw new InvalidOperationException("the loaded audio clip is missing");
                var problem = BellPattern.ClipProblem(Convert.ToInt32(Native.Read(audio, "samples")), Convert.ToInt32(Native.Read(audio, "frequency")), bell.Indexes);
                if (problem != null) throw new InvalidOperationException(problem);
                bell.AudioReference = result;
                var indexes = bell.Indexes.Select(i => (float)i).ToArray();
                // Configure restarts the player's one Loop coroutine; see OwnedBell.OnEnable.
                bell.Reconfigure = () => configure.Invoke(bell.Player, new object[] { audio, indexes, bell.Volume });
                bell.Reconfigure();
                bell.Loaded = true;
            } catch (Exception e) {
                bell.AudioReference = null;
                try { result.Dispose(); } catch { }
                Failed(bell, e.GetBaseException().Message);
            }
        }
        // A failure after the apply leaves the bell built and silent: its state names the reason, and one warning per bell
        // names its mod, ID, controller and reason in the game's log, attributed to the supplying mod's scene patch file.
        void Failed(OwnedBell bell, string reason) {
            bell.LoadProblem = reason;
            var message = "Crossing bell " + bell.Id + " from mod " + bell.Scope + " at " + bell.ControllerPath + " stays silent because its audio failed to load. Reason: " + reason
                + ". Report this to the maintainer of mod " + bell.Scope + ".";
            try {
                if (Warn != null) Warn("bell-audio/" + bell.Scope + "/" + bell.Id + "/" + bell.ControllerPath, message, bell.Scope + ":Data/scene-patches.json");
                else PatcherLog.Warning(message);
            } catch (Exception e) { PatcherLog.Warning(message); PatcherLog.Debug(() => "Crossing bell warning delivery failed: " + e); }
        }
        // Configures a controller's own bell player again with the clip the controller loaded, its definition's indexes
        // and volume: what GradeCrossingController.LoadBellAudio does once. A player that was disabled destroyed the
        // clips it split and splits none when it is enabled, so an existing bell needs this before it is enabled
        // again: called on the inactive player, Configure sets the clips and starts no loop.
        // While the controller's audio still loads there is nothing to do: the controller configures the player itself.
        internal void ReconfigureNative(GameObject controllerObject) {
            var component = controllerObject == null ? null : controllerObject.GetComponent(controllerType);
            if (component == null) throw new InvalidOperationException("The crossing controller of this bell no longer exists");
            var native = Native.Read(component, "_bellPlayer") as Component;
            var reference = Native.Read(component, "_audioLoadReference");
            if (native == null || reference == null) return;
            var audio = Native.Read(reference, "Asset") as UnityEngine.Object;
            if (audio == null) return;
            var field = controllerType.GetField("DefaultCrossingBellIdentifier", Native.Flags);
            if (field == null || !field.IsLiteral || !(field.GetRawConstantValue() is string identifier)) throw new MissingFieldException(controllerType.FullName, "DefaultCrossingBellIdentifier");
            var source = Resolve(identifier).FirstOrDefault() ?? throw new InvalidOperationException("The crossing bell definition " + identifier + " is not in the content library");
            var facts = Facts(source, controllerObject);
            configure.Invoke(native, new object[] { audio, facts.Indexes.Select(i => (float)i).ToArray(), (float)facts.Volume });
        }
        // Why a present bell is not built, bound and loaded as the pattern requires, or null. A load still running is no
        // problem; the status says it.
        internal string Problem(OwnedBell bell, GameObject controller) {
            if (bell == null || bell.Player == null) return "its bell player is missing";
            if (bell.LoadProblem != null) return "its audio failed to load: " + bell.LoadProblem;
            if (bell.Released || bell.Observer == null) return "it does not observe its crossing controller's bell key";
            if (controller == null || controller.GetComponents(owner).Length != 1) return "its crossing controller is missing or does not have exactly one key-value object";
            if (!ReferenceEquals(mixerGroup.GetValue(bell.Player), bellGroup) || !Equals(audioDistance.GetValue(bell.Player), distant)) return "its player is not configured as a crossing bell";
            return null;
        }
        // The bell's state beside the crossing's own bell, so the panel compares them: ringing with the number of its audio
        // sources that play, or quiet. The private source list and native player are read for this display only.
        internal string Status(OwnedBell bell, GameObject controller) {
            if (bell == null || bell.Player == null) return "bell player is missing";
            if (bell.LoadProblem != null) return "audio failed to load";
            if (!bell.Loaded) return "loading audio";
            return "audio loaded. Added bell is " + Ringing(bell.Player) + ". The crossing's own bell is " + Ringing(NativeBell(controller));
        }
        Component NativeBell(GameObject controller) {
            try { return controller == null ? null : Native.Read(controller.GetComponent(controllerType), "_bellPlayer") as Component; } catch { return null; }
        }
        string Ringing(Component component) {
            if (component == null) return "unreadable";
            if (!(bool)play.GetValue(component)) return "quiet";
            try {
                var sources = (Native.Read(component, "_sources") as IEnumerable)?.Cast<object>().ToList();
                return sources == null ? "ringing" : "ringing, " + sources.Count(s => Native.Read(s, "isPlaying") is bool on && on) + " of " + sources.Count + " audio sources playing";
            } catch { return "ringing"; }
        }
    }
}
