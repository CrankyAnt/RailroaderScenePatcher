// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

// Test-only mod: a small window that arms one failure in a Scene Patcher fault build. It works only in the isolated test
// home and only when the loaded patcher was built with -p:SceneFaults=true; a normal patcher build has nothing to arm.
public sealed class FaultSwitchEntry : IModEntryPoint {
    GameObject host;
    public void OnModLoad(IModContext context) { host = new GameObject("Scene fault switch"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<FaultSwitch>(); }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); }
}
public sealed class FaultSwitch : MonoBehaviour {
    const string Home = @"C:\TMP\RailroaderMapResearch\RuntimeHome";
    const string TypeName = "RailroaderScenePatcher.Core.FaultInjection";
    static readonly string[] Points = { "after-mutation", "during-restoration", "between-addition-steps" };
    Rect window = new Rect(20, 200, 360, 0);
    // The group root for the unreadable-group and unreadable-parts points, as the group preparation failure line names it:
    // scene name/path.
    string groupRoot = "";
    // Why the last Arm unreadable-group or Arm unreadable-parts failed; the Last line shows it until an arm succeeds or
    // Disarm is clicked.
    string armFailure;
    bool visible = true;
    string problem;
    Type switchType;
    void Start() {
        try {
            var args = Environment.GetCommandLineArgs();
            var homes = Enumerable.Range(0, args.Length).Where(i => String.Equals(args[i], "-homedir", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (homes.Length != 1 || homes[0] + 1 >= args.Length || !String.Equals(Path.GetFullPath(args[homes[0] + 1]).TrimEnd('\\', '/'), Home, StringComparison.OrdinalIgnoreCase))
                problem = "Inactive: the game was not started with the single -homedir " + Home;
        } catch (Exception e) { problem = "Inactive: " + e.GetBaseException().Message; }
    }
    Type Switch() {
        if (switchType != null) return switchType;
        switchType = AppDomain.CurrentDomain.GetAssemblies().Select(a => { try { return a.GetType(TypeName, false); } catch { return null; } }).FirstOrDefault(t => t != null);
        return switchType;
    }
    static object Get(Type type, string name) => type.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    static void Call(Type type, string name, params object[] args) => type.GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
    void Update() { if (Input.GetKeyDown(KeyCode.F9)) visible = !visible; }
    void OnGUI() {
        if (!visible) return;
        // The window takes the height of its content, so a long group root cannot push Disarm out of view.
        window = GUILayout.Window(0x52FA, window, Draw, "Scene Patcher fault switch (test only, F9 hides)", GUILayout.Width(window.width));
    }
    void ArmGroup(Type type, string method, string point) {
        try {
            Call(type, method, groupRoot);
            armFailure = null;
            Debug.Log("[SceneFaultSwitch] Armed " + point + " " + groupRoot);
        } catch (Exception e) {
            armFailure = "Arm " + point + " failed: " + e.GetBaseException().Message;
            Debug.LogWarning("[SceneFaultSwitch] " + armFailure);
        }
    }
    void Draw(int id) {
        GUILayout.BeginVertical();
        if (problem != null) { GUILayout.Label(problem); GUILayout.EndVertical(); GUI.DragWindow(); return; }
        var type = Switch();
        if (type == null) { GUILayout.Label("The loaded Scene Patcher is not a fault build (-p:SceneFaults=true). Nothing can be armed."); GUILayout.EndVertical(); GUI.DragWindow(); return; }
        try {
            GUILayout.Label("Armed: " + (Get(type, "Armed") ?? "none"));
            GUILayout.Label("Last: " + (armFailure ?? Get(type, "LastTrip") ?? "nothing tripped yet"));
            foreach (var point in Points) if (GUILayout.Button("Arm " + point)) { Call(type, "Arm", point); Debug.Log("[SceneFaultSwitch] Armed " + point); }
            GUILayout.Label("Unreadable group: " + (Get(type, "UnreadableGroup") ?? "none"));
            GUILayout.Label("Unreadable parts: " + (Get(type, "UnreadableParts") ?? "none"));
            groupRoot = GUILayout.TextField(groupRoot ?? "");
            var typed = !String.IsNullOrWhiteSpace(groupRoot);
            var enabled = GUI.enabled;
            GUI.enabled = enabled && typed;
            if (GUILayout.Button(new GUIContent("Arm unreadable-group", typed ? "" : "Unavailable: type a group root"))) ArmGroup(type, "ArmUnreadableGroup", "unreadable-group");
            if (GUILayout.Button(new GUIContent("Arm unreadable-parts", typed ? "" : "Unavailable: type a group root"))) ArmGroup(type, "ArmUnreadableParts", "unreadable-parts");
            GUI.enabled = enabled;
            if (GUILayout.Button("Disarm")) { Call(type, "Disarm"); armFailure = null; Debug.Log("[SceneFaultSwitch] Disarmed"); }
            // The tooltip of the control under the pointer, such as the reason a button is unavailable.
            GUILayout.Label(GUI.tooltip);
        } catch (Exception e) { GUILayout.Label("Error: " + e.GetBaseException().Message); }
        GUILayout.EndVertical();
        GUI.DragWindow();
    }
}
