// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Events;
using Helpers;
using Helpers.Culling;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The game side of own culling for moved parts of baked scenery groups: the handler of a simple culler's culling
    // group, registrations with the game's culling managers, the renderer list of a renderer culler and the object list
    // of a game-object culler. The rules live in Core's CullingPlan and PartCullingController. Everything here stands
    // on top of the game's own registrations and is put back when a part's own culling ends.
    internal sealed class PartCullingHost : ICullingHost {
        static readonly FieldInfo simpleGroup = typeof(SimpleCuller).GetField("_cullingGroup", Native.Flags),
            rendererList = typeof(RendererCuller).GetField("_cullRenderers", Native.Flags), rendererManager = typeof(RendererCuller).GetField("cullingManagerName", Native.Flags),
            rendererState = typeof(RendererCuller).GetField("_cullingState", Native.Flags), switcherManager = typeof(GameObjectCuller).GetField("cullingManagerName", Native.Flags),
            switcherMode = typeof(GameObjectCuller).GetField("cullingMode", Native.Flags), switcherState = typeof(GameObjectCuller).GetField("_cullingState", Native.Flags),
            managerSpheres = typeof(CullingManager).GetField("_spheres", Native.Flags);
        readonly IDisposable[] subscriptions;
        bool unloading;
        // Raised when the map starts to unload, before anything else may touch a culling manager.
        internal Action Unloaded;
        internal PartCullingHost() {
            if (simpleGroup?.FieldType != typeof(CullingGroup)) throw new MissingFieldException("Own culling unavailable: Helpers.SimpleCuller._cullingGroup is missing");
            if (rendererList?.FieldType != typeof(Renderer[])) throw new MissingFieldException("Own culling unavailable: Helpers.Culling.RendererCuller._cullRenderers is missing");
            if (rendererManager?.FieldType != typeof(CullingManagerName) || switcherManager?.FieldType != typeof(CullingManagerName)) throw new MissingFieldException("Own culling unavailable: the cullingManagerName of a renderer or game-object culler is missing");
            if (rendererState == null || switcherState == null) throw new MissingFieldException("Own culling unavailable: the _cullingState of a renderer or game-object culler is missing");
            if (switcherMode == null) throw new MissingFieldException("Own culling unavailable: Helpers.Culling.GameObjectCuller.cullingMode is missing");
            if (managerSpheres?.FieldType != typeof(BoundingSphere[])) throw new MissingFieldException("Own culling unavailable: Helpers.Culling.CullingManager._spheres is missing");
            // The game destroys its culling managers when the map starts to unload; reading one after that would create
            // them again in the middle of the unload.
            subscriptions = new[] {
                EventBus.Subscribe<MapWillUnloadEvent>(_ => { unloading = true; Unloaded?.Invoke(); }),
                EventBus.Subscribe<MapDidUnloadEvent>(_ => unloading = false),
                EventBus.Subscribe<MapWillLoadEvent>(_ => unloading = false),
                EventBus.Subscribe<MapDidLoadEvent>(_ => unloading = false)
            };
        }
        internal void Dispose() { foreach (var subscription in subscriptions) { try { subscription?.Dispose(); } catch { } } }
        // A world load started: the managers of the new map may be read.
        internal void WorldLoaded() { unloading = false; }
        public bool Available => !unloading;
        public bool Alive(object target) => target is UnityEngine.Object native && native != null;

        static CullingGroup GroupOf(object culler) => culler is SimpleCuller simple && simple != null ? simpleGroup.GetValue(simple) as CullingGroup : null;
        public bool Started(object culler) =>
            culler is SimpleCuller ? GroupOf(culler) != null : !(culler is RendererCuller renderer) || rendererList.GetValue(renderer) != null;
        public bool Visible(object simpleCuller) { var group = GroupOf(simpleCuller); return group == null || group.IsVisible(0); }

        sealed class HookToken { internal CullingGroup.StateChanged Before, Installed; internal bool Released; }
        public object Hook(object simpleCuller, bool replace, Func<bool> changed) {
            var group = GroupOf(simpleCuller) ?? throw new InvalidOperationException("The simple culler has not started");
            var token = new HookToken { Before = group.onStateChanged };
            // The game's handler runs first when own culling only listens. It runs in own culling's place once the
            // handler is released or the controller no longer owns it, so a handler that stays only passes events on.
            token.Installed = sphere => {
                if (token.Released || !replace) token.Before?.Invoke(sphere);
                if (!token.Released && !changed() && replace) token.Before?.Invoke(sphere);
            };
            group.onStateChanged = token.Installed;
            return token;
        }
        public bool Unhook(object simpleCuller, object hook) {
            var token = (HookToken)hook;
            token.Released = true;
            var group = GroupOf(simpleCuller);
            if (group == null) return true;
            if (group.onStateChanged != token.Installed) return false;
            group.onStateChanged = token.Before;
            return true;
        }

        // One own sphere in a culling manager of the game. The game calls it as it calls its own cullers.
        sealed class Registration : CullingManager.ICullingEventHandler {
            internal CullingManager.Token Token; internal CullingManager Manager; internal Transform Owner; internal Vector3 Local;
            internal CullerKind Kind; internal bool DistanceBandMode; internal int VisibleBand; internal Action<bool> Changed; internal bool Released;
            internal bool Live => !Released && Token != null && !Token.IsRemoved && Manager != null;
            public void CullingSphereStateChanged(bool isVisible, int distanceBand) {
                if (!Live || Owner == null) return;
                Changed(CullingActivity.Shows(Kind, DistanceBandMode, isVisible, distanceBand, VisibleBand));
            }
            // The floating origin moved: the sphere stays at its part.
            public void RequestUpdateCullingPosition() {
                if (!Live || Owner == null) return;
                try { Token.UpdatePosition(Owner.TransformPoint(Local)); } catch (ArgumentException) { }
            }
        }
        static Vector3 Scene(IReadOnlyList<double> map, Transform owner) => NativeInventory.ScenePosition(new Vector3((float)map[0], (float)map[1], (float)map[2]), NativeInventory.MovesWithOrigin(owner));
        public object Register(object culler, CullerKind kind, object owner, IReadOnlyList<double> centre, double radius, Action<bool> changed) {
            if (unloading) return null;
            var part = ((GameObject)owner).transform;
            var registration = new Registration { Owner = part, Kind = kind, Changed = changed };
            try {
                // A simple culler has a culling group of its own and no distance band; its stand-in lives with the scenery.
                if (kind == CullerKind.Simple) registration.Manager = CullingManager.Scenery;
                else if (kind == CullerKind.Renderer) { var renderer = (RendererCuller)culler; registration.Manager = CullingManager.ManagerForName((CullingManagerName)rendererManager.GetValue(renderer)); registration.VisibleBand = renderer.visibleDistanceBand; }
                else {
                    var switcher = (GameObjectCuller)culler;
                    registration.Manager = CullingManager.ManagerForName((CullingManagerName)switcherManager.GetValue(switcher));
                    registration.VisibleBand = switcher.visibleDistanceBand;
                    registration.DistanceBandMode = Convert.ToString(switcherMode.GetValue(switcher)) == "DistanceBand";
                }
                // A manager that is missing or switched off takes no sphere: asking it would throw every frame.
                if (registration.Manager == null || !registration.Manager.isActiveAndEnabled) return null;
                var scene = Scene(centre, part);
                registration.Local = part.InverseTransformPoint(scene);
                registration.Token = registration.Manager.AddSphere(scene, (float)radius, registration);
            }
            // A manager that is not configured or enabled yet takes no sphere; the next update tries again.
            catch (InvalidOperationException) { return null; }
            return registration;
        }
        public bool Live(object registration) => ((Registration)registration).Live;
        public void Move(object registration, IReadOnlyList<double> centre, double radius) {
            var own = (Registration)registration;
            if (!own.Live || own.Owner == null) return;
            var scene = Scene(centre, own.Owner);
            own.Local = own.Owner.InverseTransformPoint(scene);
            // A manager that no longer holds the token refuses it: the registration is over, and the controller
            // registers the sphere again.
            try { own.Token.UpdatePosition(scene, (float)radius); } catch (ArgumentException) { own.Released = true; }
        }
        public void Release(object registration) {
            var own = (Registration)registration;
            var live = own.Live;
            own.Released = true;
            if (!live) return;
            try { own.Token.Dispose(); } catch (ArgumentException) { }
        }
        public IReadOnlyList<double> Sphere(object registration) {
            var own = (Registration)registration;
            if (!own.Live || own.Owner == null) return null;
            var spheres = managerSpheres.GetValue(own.Manager) as BoundingSphere[];
            var index = own.Token.Index;
            if (spheres == null || index < 0 || index >= spheres.Length) return null;
            var map = NativeInventory.MapPosition(spheres[index].position, NativeInventory.MovesWithOrigin(own.Owner));
            return new double[] { map.x, map.y, map.z, spheres[index].radius };
        }
        public void SetActive(object node, bool active) { var go = (GameObject)node; if (go.activeSelf != active) go.SetActive(active); }
        public IReadOnlyList<object> Renderers(object rendererCuller) => ((Renderer[])rendererList.GetValue(rendererCuller) ?? new Renderer[0]).Cast<object>().ToArray();
        public void SetRenderers(object rendererCuller, IReadOnlyList<object> renderers) => rendererList.SetValue(rendererCuller, renderers.Cast<Renderer>().ToArray());
        public bool Shows(object culler) => Convert.ToString((culler is GameObjectCuller ? switcherState : rendererState).GetValue(culler)) == "Visible";
        public void SetEnabled(object renderer, bool enabled) { var shape = (Renderer)renderer; if (shape.enabled != enabled) shape.enabled = enabled; }
        public bool Under(object renderer, object node) => ((Renderer)renderer).transform.IsChildOf(((GameObject)node).transform);
        public int Unlist(object gameObjectCuller, object node) {
            var list = ((GameObjectCuller)gameObjectCuller).gameObjects; var index = list.IndexOf((GameObject)node);
            if (index >= 0) list.RemoveAt(index);
            return index;
        }
        public void Relist(object gameObjectCuller, object node, int index) {
            var list = ((GameObjectCuller)gameObjectCuller).gameObjects; var go = (GameObject)node;
            if (!list.Contains(go)) list.Insert(Math.Max(0, Math.Min(index, list.Count)), go);
        }
    }
}
