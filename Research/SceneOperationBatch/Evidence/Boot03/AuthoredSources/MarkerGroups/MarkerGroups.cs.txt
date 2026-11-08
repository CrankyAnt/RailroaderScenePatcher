// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

public sealed class MarkerGroupsEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    public void OnModLoad(IModContext context) { host = new GameObject("CrankyAnt MarkerGroups"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<MarkerGroups>().Initialize(context.ModDirectory); }
    public void OnWorldLoad() { if (host != null) host.GetComponent<MarkerGroups>().OnWorldLoaded(); }
    public void OnWorldUnload() { if (host != null) host.GetComponent<MarkerGroups>().OnUnload(); }
    public void OnModUnload() { if (host != null) { host.GetComponent<MarkerGroups>().OnUnload(); UnityEngine.Object.Destroy(host); } }
}

public sealed partial class MarkerGroups : MonoBehaviour
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private string log;
    private bool isolated;
    private Rect window = new Rect(20, 200, 400, 300);
    private string status = "Read-only preflight. Load the disposable fixture.";
    public void Initialize(string directory)
    {
        string expectedHome = Path.GetFullPath(@"C:\TMP\RailroaderMapResearch\RuntimeHome").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string expectedMod = Path.Combine(expectedHome, "Mods", "MarkerGroups");
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.FindIndex(args, s => string.Equals(s, "-homedir", StringComparison.OrdinalIgnoreCase));
        try
        {
            isolated = index >= 0 && index + 1 < args.Length && string.Equals(Path.GetFullPath(args[index + 1]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), expectedHome, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), expectedMod, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { isolated = false; }
        if (!isolated) { status = "Isolation guard failed. Probe disabled."; return; }
        log = Path.Combine(directory, "MarkerGroups.log");
    }
    private void Write(string s) { if (log != null) File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + s + Environment.NewLine); }
    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    private static string Show(object o)
    {
        if (o == null) return "null";
        if (o is Component c) return c.GetType().FullName + ":" + PathOf(c.transform);
        if (o is Transform t) return PathOf(t);
        return Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
    }
    private void Scan()
    {
        try
        {
            if (!isolated) throw new InvalidOperationException("Isolation guard failed");
            var all = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && c.GetType().FullName == "Track.TrackRelativePosition").ToArray();
            Write("scan count=" + all.Length);
            foreach (var c in all)
            {
                var follows = c.GetType().GetField("targetActiveFollowsTrackGroup", Flags);
                if (follows == null || !(follows.GetValue(c) is bool b) || !b) continue;
                var t = c.transform;
                Write("follower=" + PathOf(t) + " activeSelf=" + t.gameObject.activeSelf + " activeHierarchy=" + t.gameObject.activeInHierarchy + " parent=" + (t.parent == null ? "null" : PathOf(t.parent)) + " worldPosition=" + t.position.ToString("F4") + " worldRotation=" + t.rotation.ToString("F4") + " localPosition=" + t.localPosition.ToString("F4") + " localRotation=" + t.localRotation.ToString("F4"));
                foreach (var f in c.GetType().GetFields(Flags))
                    try { Write("field " + f.Name + "=" + Show(f.GetValue(c))); } catch (Exception e) { Write("field " + f.Name + " error=" + e.GetType().Name); }
                foreach (var marker in t.GetComponents<Component>().Where(x => x != null && x.GetType().FullName == "Track.TrackMarker"))
                    foreach (var f in marker.GetType().GetFields(Flags))
                        try { Write("marker " + f.Name + "=" + Show(f.GetValue(marker))); } catch (Exception e) { Write("marker " + f.Name + " error=" + e.GetType().Name); }
            }
            status = "Read-only scan completed. Inspect MarkerGroups.log.";
        }
        catch (Exception e) { Write("scan error=" + e); status = "Scan failed. Inspect log."; }
    }
    private void FocusMarker()
    {
        try
        {
            if (!isolated || target != null || dirty) throw new InvalidOperationException("Focus requires an isolated world without a captured trial");
            if (Convert.ToString(Read(TypeNamed("Game.ActiveMapDefinition"), "Identifier")) != "crankyant/scene-operations-map") throw new InvalidOperationException("Wrong map");
            var found = At("World/Mile Posts/T-46 Origin", "Track.TrackMarker");
            if (Convert.ToString(Read(found, "id")) != "Mdow") throw new InvalidOperationException("Marker identity differs");
            var world = TypeNamed("Helpers.WorldTransformer");
            var toGame = world.GetMethod("WorldToGame", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(Vector3) }, null) ?? throw new MissingMethodException("WorldToGame");
            var position = (Vector3)toGame.Invoke(null, new object[] { found.transform.position });
            var camera = TypeNamed("CameraSelector");
            var selector = Read(camera, "shared") ?? throw new InvalidOperationException("Camera unavailable");
            var mode = Enum.Parse(camera.GetNestedType("CameraIdentifier", BindingFlags.Public | BindingFlags.NonPublic), "Strategy");
            camera.GetMethod("JumpToPoint").Invoke(selector, new object[] { position, Quaternion.identity, mode });
            Write("focus T-46 gamePosition=" + position.ToString("F4"));
            status = "Focus requested. Wait for origin to settle before capture.";
        }
        catch (Exception e) { Write("focus ERROR " + e); status = "Focus failed. Inspect log."; }
    }
    private void OnGUI() { window = GUI.Window(0x434d4752, window, id => { GUILayout.Label(status); GUILayout.Label(autoPending ? "Automatic preflight waiting" : autoArmed ? "Next world load armed" : "Automatic application off"); if (!autoPending) { if (GUILayout.Button(autoArmed ? "Cancel next-load arm" : "Arm next world load")) ToggleAutoArm(); if (GUILayout.Button("Scan group followers")) Scan(); if (GUILayout.Button("Focus T-46 before capture")) FocusMarker(); if (GUILayout.Button("Capture trial baseline")) Capture(); if (GUILayout.Button("Rebind and apply / repeat")) ApplyTrial(); if (GUILayout.Button("Toggle old and new groups")) ToggleTrial(); if (GUILayout.Button("Test explicit disable")) DisableTrial(); if (GUILayout.Button("Restore trial")) RestoreTrial(); } if (dirty) GUILayout.Label("Dirty: restore before leaving. Never save."); GUI.DragWindow(); }, "Marker groups trial"); }
}
