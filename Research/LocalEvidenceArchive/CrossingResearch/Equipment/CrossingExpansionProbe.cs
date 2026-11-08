using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

namespace CrankyAnt.CrossingExpansionResearch
{
    public sealed class Entry : IModEntryPoint, IWorldLifecycle
    {
        private GameObject host;
        public void OnModLoad(IModContext context) { host = new GameObject("Crossing Expansion Probe"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<Probe>().Initialize(context.ModDirectory); }
        public void OnWorldLoad() { }
        public void OnWorldUnload() { }
        public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); }
    }

    public sealed class Probe : MonoBehaviour
    {
        private const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const string Root = "World/Large Scenery/Bryson/Grade Crossing - Bryson Everett";
        private string log, status = "Read-only inspection. Use disposable research world.";
        private Rect window = new Rect(20, 200, 430, 150);
        public void Initialize(string directory) { log = Path.Combine(directory, "CrossingExpansionProbe.log"); Write("loaded"); }
        private void Write(string s) { File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + s + Environment.NewLine); }
        private static Type TypeNamed(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null); }
        private static object Read(object target, string name) { var t = target as Type ?? target.GetType(); var instance = target is Type ? null : target; var p = t.GetProperty(name, F); if (p != null) return p.GetValue(instance, null); var f = t.GetField(name, F); if (f != null) return f.GetValue(instance); throw new MissingMemberException(t.FullName, name); }
        private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
        private static bool Loaded(Component c) { return c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded; }
        private Component Guard() {
            if (Convert.ToString(Read(TypeNamed("Game.ActiveMapDefinition"), "Identifier")) != "crankyant/crossing-expansion-research") throw new InvalidOperationException("Wrong map identity.");
            var matches = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => Loaded(c) && c.GetType().FullName == "Track.Crossings.GradeCrossingController" && PathOf(c.transform) == Root).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Expected exactly one crossing controller, found " + matches.Length);
            return matches[0];
        }
        private static string Describe(object value) {
            if (value == null) return "null";
            if (value is Component c) return c.GetType().FullName + " @ " + PathOf(c.transform) + " #" + c.GetInstanceID();
            if (value is GameObject g) return "GameObject @ " + PathOf(g.transform) + " #" + g.GetInstanceID();
            if (value is UnityEngine.Object u) return u.GetType().FullName + " name=" + u.name + " #" + u.GetInstanceID();
            if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum) return Convert.ToString(value);
            if (value is IEnumerable list) { var items = list.Cast<object>().Take(40).Select(Describe).ToArray(); return "[" + string.Join(", ", items) + "]"; }
            return "<" + value.GetType().FullName + ">";
        }
        private void Inspect() {
            try {
                var controller = Guard(); Write("BEGIN inspection map=crankyant/crossing-expansion-research controller=" + Describe(controller));
                foreach (var t in controller.GetComponentsInChildren<Transform>(true)) {
                    Write("OBJECT " + PathOf(t) + " activeSelf=" + t.gameObject.activeSelf + " activeInHierarchy=" + t.gameObject.activeInHierarchy + " position=" + t.position.ToString("F4"));
                    foreach (var c in t.GetComponents<Component>()) {
                        if (c == null) { Write(" COMPONENT missing"); continue; }
                        Write(" COMPONENT " + Describe(c) + (c is Behaviour b ? " enabled=" + b.enabled : ""));
                        for (var type = c.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(Component) && type != typeof(UnityEngine.Object); type = type.BaseType) {
                            foreach (var f in type.GetFields(F | BindingFlags.DeclaredOnly).Where(x => !x.IsStatic && !x.IsNotSerialized)) {
                                if (!(f.IsPublic || f.GetCustomAttributes(false).Any(a => a.GetType().FullName == "UnityEngine.SerializeField"))) continue;
                                try { Write("  FIELD " + type.FullName + "." + f.Name + "=" + Describe(f.GetValue(c))); } catch (Exception e) { Write("  FIELD ERROR " + f.Name + " " + e.GetType().Name); }
                            }
                        }
                    }
                }
                Write("END inspection"); status = "Inspection complete; see log.";
            } catch (Exception e) { Write("INSPECTION ERROR " + e); status = "Inspection refused or failed; see log."; }
        }
        private void Focus() {
            try {
                var controller = Guard(); var transformer = TypeNamed("Helpers.WorldTransformer");
                var center = (Vector3)transformer.GetMethod("WorldToGame", F, null, new[] { typeof(Vector3) }, null).Invoke(null, new object[] { controller.transform.position });
                var camera = TypeNamed("CameraSelector"); var selector = Read(camera, "shared"); var mode = Enum.Parse(camera.GetNestedType("CameraIdentifier", F), "Strategy");
                camera.GetMethod("JumpToPoint", F).Invoke(selector, new object[] { center, Quaternion.identity, mode });
                Write("camera focus requested game=" + center.ToString("F4")); status = "Crossing camera focus requested.";
            } catch (Exception e) { Write("FOCUS ERROR " + e); status = "Focus refused or failed; see log."; }
        }
        private void OnGUI() { window = GUI.Window(0x43585052, window, id => { GUILayout.Label(status); if (GUILayout.Button("Inspect crossing")) Inspect(); if (GUILayout.Button("Focus crossing")) Focus(); GUI.DragWindow(); }, "Crossing Expansion Probe"); }
    }
}
