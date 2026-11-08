// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

public sealed class BridgeContactEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    private BridgeContactProbe probe;
    public void OnModLoad(IModContext context)
    {
        host = new GameObject("CrankyAnt Bridge Contact Probe");
        UnityEngine.Object.DontDestroyOnLoad(host);
        probe = host.AddComponent<BridgeContactProbe>();
        probe.Initialize(context.ModDirectory);
    }
    public void OnWorldLoad() { probe.WorldLoad(); }
    public void OnWorldUnload() { probe.WorldUnload(); }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); }
}

public sealed class BridgeContactProbe : MonoBehaviour
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string BridgePath = "World/Large Scenery/Ela/Ela Bridge";
    private const string ApproachPath = "World/Large Scenery/Ela/Ela Bridge Scenery";
    private readonly List<NodeState> states = new List<NodeState>();
    private readonly Dictionary<int, string> colliderBaseline = new Dictionary<int, string>();
    private Transform bridge, approach;
    private Vector3 origin;
    private Vector3 parentGamePosition;
    private Quaternion parentRotation;
    private bool loaded, dirty, baselineComplete;
    private string log, directory, status = "Disposable fixture only. Never save.";
    private Rect window = new Rect(450, 430, 430, 370);
    private GameObject ball;
    private int contacts;

    private sealed class NodeState
    {
        public Transform Node, Parent;
        public Vector3 Position, Scale;
        public Vector3 GamePosition;
        public Quaternion WorldRotation;
        public Quaternion Rotation;
        public bool Active;
        public NodeState(Transform node)
        {
            Node = node; Parent = node.parent; Position = node.localPosition;
            Rotation = node.localRotation; Scale = node.localScale; Active = node.gameObject.activeSelf;
            GamePosition = ConvertPoint("WorldToGame", node.position); WorldRotation = node.rotation;
        }
    }

    public void Initialize(string modDirectory)
    {
        directory = modDirectory;
        log = Path.Combine(directory, "BridgeContactProbe.log");
        Write("loaded; source=issue27-preparation-v1; runtime-unverified");
    }
    public void WorldLoad() { loaded = true; Write("OnWorldLoad; no automatic mutation"); }
    public void WorldUnload()
    {
        Write("OnWorldUnload dirty=" + dirty + "; capture cancelled");
        RemoveBall(); loaded = false; bridge = null; approach = null;
        states.Clear(); colliderBaseline.Clear(); dirty = false; baselineComplete = false;
    }
    private void OnDestroy() { RemoveBall(); }
    private static Type TypeNamed(string name)
    {
        return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null);
    }
    private static object Field(object target, string name)
    {
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            var field = t.GetField(name, Flags | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(target);
        }
        throw new MissingFieldException(target.GetType().FullName, name);
    }
    private static Vector3 ConvertPoint(string method, Vector3 point)
    {
        return (Vector3)TypeNamed("Helpers.WorldTransformer").GetMethod(method,
            BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(Vector3) }, null)
            .Invoke(null, new object[] { point });
    }
    private static Vector3 Origin() { return ConvertPoint("WorldToGame", Vector3.zero); }
    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    private static string F(float n) { return n.ToString("R", CultureInfo.InvariantCulture); }
    private static string V(Vector3 v) { return F(v.x) + "," + F(v.y) + "," + F(v.z); }
    private static string Q(Quaternion q) { return F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w); }
    private void Write(string message) { File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + message + "\n"); }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception e) { Write("ERROR " + e); status = "Failed. Restore or discard disposable session. Read log."; }
    }
    private void Guard()
    {
        if (!loaded) throw new InvalidOperationException("World unavailable.");
        var args = Environment.GetCommandLineArgs();
        int index = Array.FindIndex(args, x => string.Equals(x, "-homedir", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length || !string.Equals(
            Path.GetFullPath(args[index + 1]).TrimEnd('\\', '/'),
            @"C:\TMP\RailroaderMapResearch\RuntimeHome", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("First home argument is not isolated RuntimeHome.");
        if (!string.Equals(Path.GetFullPath(directory).TrimEnd('\\', '/'),
            @"C:\TMP\RailroaderMapResearch\RuntimeHome\Mods\BridgeContactProbe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Probe directory is not the isolated case directory.");
        var id = TypeNamed("Game.ActiveMapDefinition").GetProperty("Identifier",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
        if (Convert.ToString(id) != "crankyant/scene-operations-map") throw new InvalidOperationException("Wrong map.");
    }
    private static Transform Find(string path)
    {
        var matches = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t != null
            && t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded && PathOf(t) == path).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Target count for " + path + "=" + matches.Length);
        return matches[0];
    }
    private void CheckCapture(bool requireBaseline = true)
    {
        Guard();
        if (requireBaseline && !baselineComplete) throw new InvalidOperationException("Baseline capture incomplete.");
        if (bridge == null || approach == null || states.Count == 0) throw new InvalidOperationException("Capture first.");
        if ((Origin() - origin).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Origin changed; no further mutation. Discard session.");
        if (bridge.parent == null || Vector3.Distance(ConvertPoint("WorldToGame", bridge.parent.position), parentGamePosition) > 0.01f
            || Quaternion.Angle(bridge.parent.rotation, parentRotation) > 0.01f)
            throw new InvalidOperationException("Bridge parent moved. Discard session.");
        foreach (var state in states)
            if (state.Node == null || state.Node.parent != state.Parent) throw new InvalidOperationException("Hierarchy changed.");
    }
    private void Focus()
    {
        Guard();
        var type = TypeNamed("CameraSelector");
        var selector = type.GetProperty("shared", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null);
        var strategy = Enum.Parse(type.GetNestedType("CameraIdentifier", BindingFlags.Public | BindingFlags.NonPublic), "Strategy");
        type.GetMethod("JumpToPoint").Invoke(selector, new object[] {
            ConvertPoint("WorldToGame", Find(BridgePath).position), Quaternion.identity, strategy });
        status = "Focus requested. Wait for origin to settle, then capture.";
    }
    private void Capture()
    {
        Guard();
        if (dirty || ball != null) throw new InvalidOperationException("Restore and remove contact ball first.");
        baselineComplete = false; bridge = Find(BridgePath); approach = Find(ApproachPath);
        if (!bridge.gameObject.activeInHierarchy || bridge.parent == null) throw new InvalidOperationException("Inactive or unparented baseline bridge.");
        states.Clear(); colliderBaseline.Clear();
        states.AddRange(bridge.GetComponentsInChildren<Transform>(true).Concat(
            approach.GetComponentsInChildren<Transform>(true)).Select(t => new NodeState(t)));
        origin = Origin();
        parentGamePosition = ConvertPoint("WorldToGame", bridge.parent.position); parentRotation = bridge.parent.rotation;
        Snapshot("baseline"); baselineComplete = true; status = "Captured bridge and separate scenery. Never save.";
    }
    private string ColliderShape(Collider c)
    {
        string shape = c.GetType().FullName + " enabled=" + c.enabled + " trigger=" + c.isTrigger
            + " layer=" + c.gameObject.layer + " contactOffset=" + F(c.contactOffset);
        if (c is BoxCollider box) shape += " center=" + V(box.center) + " size=" + V(box.size);
        if (c is SphereCollider sphere) shape += " center=" + V(sphere.center) + " radius=" + F(sphere.radius);
        if (c is CapsuleCollider capsule) shape += " center=" + V(capsule.center) + " radius=" + F(capsule.radius)
            + " height=" + F(capsule.height) + " direction=" + capsule.direction;
        if (c is MeshCollider mesh) shape += " mesh.id=" + (mesh.sharedMesh == null ? 0 : mesh.sharedMesh.GetInstanceID())
            + " vertices=" + (mesh.sharedMesh == null ? 0 : mesh.sharedMesh.vertexCount)
            + " convex=" + mesh.convex + " cooking=" + mesh.cookingOptions;
        return shape;
    }
    private void Snapshot(string phase)
    {
        CheckCapture(phase != "baseline");
        Write(phase + " origin=" + V(Origin()) + " frame=" + Time.frameCount + " dirty=" + dirty);
        foreach (var state in states)
        {
            var t = state.Node;
            Write(phase + " node=" + PathOf(t) + " id=" + t.GetInstanceID()
                + " localPosition=" + V(t.localPosition) + " localRotation=" + Q(t.localRotation)
                + " localScale=" + V(t.localScale) + " gamePosition=" + V(ConvertPoint("WorldToGame", t.position))
                + " activeSelf=" + t.gameObject.activeSelf + " activeHierarchy=" + t.gameObject.activeInHierarchy
                + " positionError=" + F(Vector3.Distance(t.localPosition, state.Position))
                + " gamePositionError=" + F(Vector3.Distance(ConvertPoint("WorldToGame", t.position), state.GamePosition))
                + " worldRotationErrorDegrees=" + F(Quaternion.Angle(t.rotation, state.WorldRotation))
                + " rotationErrorDegrees=" + F(Quaternion.Angle(t.localRotation, state.Rotation))
                + " scaleError=" + F(Vector3.Distance(t.localScale, state.Scale)));
            foreach (var c in t.GetComponents<Collider>())
            {
                string shape = ColliderShape(c);
                if (phase == "baseline") colliderBaseline[c.GetInstanceID()] = shape;
                Write(phase + " collider=" + PathOf(t) + " id=" + c.GetInstanceID() + " " + shape
                    + " boundsCenter=" + V(c.bounds.center) + " boundsSize=" + V(c.bounds.size)
                    + " shapeEqualsBaseline=" + (colliderBaseline.TryGetValue(c.GetInstanceID(), out var previous) && shape == previous));
            }
            foreach (var c in t.GetComponents<Component>().Where(c => c != null))
            {
                string type = c.GetType().FullName;
                Write(phase + " component=" + PathOf(t) + " type=" + type);
                if (c is Renderer renderer) Write(phase + " renderer.enabled=" + renderer.enabled + " visible=" + renderer.isVisible + " node=" + PathOf(t));
                if (type == "Helpers.Culling.RendererCuller") Culler(phase, c);
                if (type.Contains("TrackGroup"))
                    foreach (var field in c.GetType().GetFields(Flags).Where(f => f.FieldType.IsPrimitive
                        || f.FieldType == typeof(string) || f.FieldType.IsEnum))
                        Write(phase + " group." + field.Name + "=" + Convert.ToString(field.GetValue(c), CultureInfo.InvariantCulture));
            }
        }
    }
    private void Culler(string phase, Component c)
    {
        Write(phase + " culler=" + PathOf(c.transform) + " state=" + Field(c, "_cullingState"));
        var token = Field(c, "_cullingToken");
        if (token == null) { Write(phase + " culler.token=null"); return; }
        bool removed = (bool)Field(token, "<IsRemoved>k__BackingField");
        Write(phase + " culler.tokenRemoved=" + removed);
        if (removed) return;
        int index = (int)Field(token, "<Index>k__BackingField");
        var manager = Field(token, "Manager");
        var spheres = (BoundingSphere[])Field(manager, "_spheres");
        int count = (int)Field(manager, "_sphereCount");
        if (index < 0 || index >= count || index >= spheres.Length) throw new InvalidOperationException("Invalid culler index.");
        Write(phase + " sphere.center=" + V(spheres[index].position) + " radius=" + F(spheres[index].radius));
    }
    private void RefreshCulling()
    {
        foreach (var c in bridge.GetComponentsInChildren<Component>(true).Where(c => c != null
            && c.GetType().FullName == "Helpers.Culling.RendererCuller"))
            c.GetType().GetMethod("RequestUpdateCullingPosition", Flags).Invoke(c, null);
    }
    private void Move()
    {
        CheckCapture(); if (ball != null) throw new InvalidOperationException("Remove ball first.");
        if (!bridge.gameObject.activeSelf) throw new InvalidOperationException("Restore activity first.");
        var baseline = states.First(s => s.Node == bridge);
        dirty = true; bridge.localPosition = baseline.Position;
        bridge.localRotation = baseline.Rotation;
        bridge.position += new Vector3(5, 0, 0);
        bridge.rotation = Quaternion.Euler(0, 15, 0) * bridge.rotation;
        RefreshCulling(); Snapshot("moved immediate"); status = "Moved. Wait, snapshot, test contact, restore.";
    }
    private void Toggle()
    {
        CheckCapture(); if (ball != null) throw new InvalidOperationException("Remove ball first.");
        dirty = true; bridge.gameObject.SetActive(!bridge.gameObject.activeSelf);
        Snapshot("activity immediate"); status = "Activity changed. Observe, snapshot and restore.";
    }
    private void Restore()
    {
        CheckCapture(); RemoveBall();
        var baseline = states.First(s => s.Node == bridge);
        bridge.localPosition = baseline.Position; bridge.localRotation = baseline.Rotation;
        bridge.localScale = baseline.Scale; bridge.gameObject.SetActive(baseline.Active);
        RefreshCulling(); Snapshot("restored immediate");
        bool exact = states.All(s => s.Node.localPosition.Equals(s.Position)
            && s.Node.localRotation.Equals(s.Rotation) && s.Node.localScale.Equals(s.Scale)
            && s.Node.gameObject.activeSelf == s.Active);
        bool worldWithinTolerance = states.All(s => Vector3.Distance(ConvertPoint("WorldToGame", s.Node.position), s.GamePosition) <= 0.01f
            && Quaternion.Angle(s.Node.rotation, s.WorldRotation) <= 0.01f);
        var currentColliders = states.SelectMany(s => s.Node.GetComponents<Collider>()).ToArray();
        bool shapeMatches = currentColliders.Select(c => c.GetInstanceID()).OrderBy(x => x).SequenceEqual(colliderBaseline.Keys.OrderBy(x => x)) && currentColliders.All(c =>
            colliderBaseline.TryGetValue(c.GetInstanceID(), out var shape) && shape == ColliderShape(c));
        Write("restoration exactLocalTransformsAndActivity=" + exact + " colliderShapes=" + shapeMatches
            + " worldPoseWithin0.01mAnd0.01degrees=" + worldWithinTolerance);
        if (!exact || !shapeMatches || !worldWithinTolerance) throw new InvalidOperationException("Incomplete restoration. Never save.");
        dirty = false; status = "Captured local transforms restored. Wait and snapshot again.";
    }
    private void DropBall()
    {
        CheckCapture(); if (ball != null) throw new InvalidOperationException("Remove previous ball first.");
        if (!bridge.gameObject.activeInHierarchy) throw new InvalidOperationException("Bridge inactive; no dynamic contact trial.");
        Physics.SyncTransforms();
        var colliders = bridge.GetComponentsInChildren<Collider>(true).Where(c => c.enabled
            && c.gameObject.activeInHierarchy && !c.isTrigger).ToArray();
        if (colliders.Length == 0) throw new InvalidOperationException("No active bridge collider.");
        Collider target = null; RaycastHit chosen = default;
        foreach (var collider in colliders)
        {
            var b = collider.bounds;
            if (collider.Raycast(new Ray(new Vector3(b.center.x, b.max.y + 2, b.center.z), Vector3.down), out var hit, b.size.y + 4)
                && hit.normal.y > 0.7f) { target = collider; chosen = hit; break; }
        }
        if (target == null) throw new InvalidOperationException("No upward bridge surface under bounded ray. Choose manual continuation.");
        if (Physics.GetIgnoreLayerCollision(0, target.gameObject.layer)) throw new InvalidOperationException("Default probe layer ignores bridge layer.");
        ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "CrankyAnt disposable contact ball";
        ball.transform.localScale = Vector3.one * 0.2f;
        ball.transform.position = chosen.point + chosen.normal * 0.5f;
        var body = ball.AddComponent<Rigidbody>(); body.mass = 0.05f;
        body.useGravity = true; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var observer = ball.AddComponent<BridgeBallObserver>(); observer.Owner = this; observer.Bridge = bridge;
        contacts = 0;
        Write("ball spawned target=" + PathOf(target.transform) + " rayHit=" + V(chosen.point)
            + " rayQueryIsNotContact=true fixedDeltaTime=" + F(Time.fixedDeltaTime)
            + " timeScale=" + F(Time.timeScale) + " physicsSimulationMode=" + Physics.simulationMode);
        status = "Ball dropped. Observe actual contact, then remove ball. No physics settings changed.";
    }
    public void Contact(Collision collision, Transform target)
    {
        bool onBridge = collision.collider != null && (collision.collider.transform == target
            || collision.collider.transform.IsChildOf(target));
        contacts++;
        status = onBridge ? "Bridge contact recorded. Remove ball." : "Contact with another object. Inspect log.";
        Write("OnCollisionEnter count=" + contacts + " onBridge=" + onBridge
            + " other=" + (collision.collider == null ? "null" : PathOf(collision.collider.transform))
            + " contactCount=" + collision.contactCount + " frame=" + Time.frameCount);
        for (int i = 0; i < collision.contactCount; i++)
        {
            var contact = collision.GetContact(i);
            Write("contact point=" + V(contact.point) + " normal=" + V(contact.normal)
                + " separation=" + F(contact.separation));
        }
    }
    private void RemoveBall()
    {
        if (ball == null) return;
        Write("ball remove requested contacts=" + contacts);
        ball.SetActive(false); Destroy(ball); ball = null;
    }
    private void OnGUI()
    {
        window = GUI.Window(0x42435027, window, id => {
            GUILayout.Label(status);
            if (GUILayout.Button("Focus bridge")) Run(Focus);
            if (GUILayout.Button("Capture bridge and approach")) Run(Capture);
            if (GUILayout.Button("Snapshot current")) Run(() => Snapshot("current"));
            if (GUILayout.Button("Move or repeat +5m/+15 degrees")) Run(Move);
            if (GUILayout.Button("Toggle bridge activity")) Run(Toggle);
            if (GUILayout.Button("Drop disposable contact ball")) Run(DropBall);
            if (GUILayout.Button("Remove contact ball")) Run(RemoveBall);
            if (GUILayout.Button("Restore captured bridge")) Run(Restore);
            if (dirty || ball != null) GUILayout.Label("Restore and remove ball before leaving. Never save.");
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }, "Bridge collision research");
    }
}

public sealed class BridgeBallObserver : MonoBehaviour
{
    public BridgeContactProbe Owner;
    public Transform Bridge;
    private void OnCollisionEnter(Collision collision) { if (Owner != null) Owner.Contact(collision, Bridge); }
}
