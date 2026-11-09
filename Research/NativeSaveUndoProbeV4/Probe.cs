// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Railroader.Modding;
using UnityEngine;

public sealed class Entry : IModEntryPoint, IWorldLifecycle
{
    private IModContext context;
    private GameObject host;
    private Harmony harmony;
    public void OnModLoad(IModContext value)
    {
        context = value;
        harmony = new Harmony("crankyant.native-save-undo-probe-v4");
        Probe.Install(harmony);
    }
    public void OnWorldLoad()
    {
        host = new GameObject("Native save undo probe v4");
        host.AddComponent<Probe>().Initialize(context.ModDirectory);
    }
    public void OnWorldUnload() { if (host != null) UnityEngine.Object.Destroy(host); host = null; }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); harmony.UnpatchAll("crankyant.native-save-undo-probe-v4"); }
}

public sealed class Probe : MonoBehaviour
{
    private sealed class Token { public long Value; }
    private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly ConditionalWeakTable<object, Token> Tokens = new ConditionalWeakTable<object, Token>();
    private static long nextToken;
    private static Probe current;
    private string directory;
    private object editor, manager;
    private GameObject a, b;
    private int av, bv, savedA, savedB;
    private long[] savedHistory;
    private bool failCustom;
    private int pendingInspection; private bool inRedo;
    private FileStream nativeLock;
    private string message = "Waiting for editor";
    private Rect rect = new Rect(20, 70, 470, 440);

    private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType(name, false)).FirstOrDefault(x => x != null);
    private static object Read(object obj, string name)
    {
        if (obj == null) return null;
        var type = obj as Type ?? obj.GetType();
        var receiver = obj is Type ? null : obj;
        return type.GetProperty(name, Flags)?.GetValue(receiver, null) ?? type.GetField(name, Flags)?.GetValue(receiver);
    }
    private static long TokenFor(object group) => Tokens.GetValue(group, _ => new Token { Value = ++nextToken }).Value;
    private static object[] Stack(object undoManager, string field) => ((IEnumerable)Read(undoManager, field)).Cast<object>().ToArray();
    private long[] History() => Stack(manager, "_undoStack").Select(TokenFor).ToArray();
    private void Log(string text)
    {
        message = text;
        File.AppendAllText(Path.Combine(directory, "ProbeV4.log"), DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);
    }
    private string Observed()
    {
        if (a == null || b == null) return "objects absent";
        var pa = a.transform.localPosition;
        var pb = b.transform.localPosition;
        return "actualA=" + pa.x + "," + pa.y + "," + pa.z + " actualB=" + pb.x + "," + pb.y + "," + pb.z;
    }
    public static void Install(Harmony h)
    {
        var map = Find("Game.MapEditor.MapEditor");
        var undo = Find("Core.UndoManager");
        h.Patch(map.GetMethod("Save", Flags), prefix: new HarmonyMethod(typeof(Probe).GetMethod(nameof(BeforeSave), Flags)), postfix: new HarmonyMethod(typeof(Probe).GetMethod(nameof(AfterSave), Flags)));
        h.Patch(map.GetProperty("IsDirty", Flags).GetGetMethod(), postfix: new HarmonyMethod(typeof(Probe).GetMethod(nameof(AfterIsDirty), Flags)));
        h.Patch(undo.GetMethod("Undo", Flags), prefix: new HarmonyMethod(typeof(Probe).GetMethod(nameof(BeforeUndo), Flags)), postfix: new HarmonyMethod(typeof(Probe).GetMethod(nameof(AfterUndo), Flags)));
        h.Patch(undo.GetMethod("Redo", Flags), prefix: new HarmonyMethod(typeof(Probe).GetMethod(nameof(BeforeRedo), Flags)), postfix: new HarmonyMethod(typeof(Probe).GetMethod(nameof(AfterRedo), Flags)));
    }
    public void Initialize(string path) { directory = path; current = this; var info = Harmony.GetPatchInfo(Find("Game.MapEditor.MapEditor").GetMethod("Save", Flags)); var count = info.Prefixes.Count(p => p.owner == "crankyant.native-save-undo-probe-v4"); Log("World loaded; owned Save prefixes=" + count); }
    private void Update()
    {
        var controller = Read(Find("Game.MapEditor.MapEditorController"), "Shared");
        var found = Read(controller, "MapEditor");
        if (found != null && editor != found)
        {
            editor = found; manager = Read(editor, "UndoManager");
            savedHistory = History(); savedA = av; savedB = bv;
            Log("Editor attached; initial history recorded");
        }
        if (pendingInspection > 0 && --pendingInspection == 0) InspectChrome("next frame after RefreshChrome");
    }
    private static void BeforeSave(object __instance)
    {
        if (current == null || current.editor != __instance) return;
        current.Log("Save prefix failCustom=" + current.failCustom + " state=" + current.av + "," + current.bv + " " + current.Observed());
        if (current.failCustom) throw new IOException("Controlled custom write failure");
        File.WriteAllText(Path.Combine(current.directory, "authored-test-v4.txt"), current.av + "," + current.bv);
        current.Log("Custom write complete");
    }
    private static void AfterSave(object __instance)
    {
        if (current == null || current.editor != __instance) return;
        current.savedHistory = current.History(); current.savedA = current.av; current.savedB = current.bv;
        current.Log("Native Save completed; recorded saved history and custom state");
    }
    private static void AfterIsDirty(object __instance, ref bool __result)
    {
        var p = current;
        if (p == null || p.editor != __instance || p.manager == null || p.savedHistory == null) return;
        var native = __result; __result = __result || p.av != p.savedA || p.bv != p.savedB || !p.History().SequenceEqual(p.savedHistory); if (p.inRedo) p.Log("Dirty getter during Redo native=" + native + " augmented=" + __result + " count=" + Read(p.manager, "ChangeCount"));
    }
    private static void BeforeUndo(object __instance, out long __state) { __state = CaptureToken(__instance, "_undoStack"); }
    private static void AfterUndo(object __instance, long __state) { TransferToken(__instance, "_redoStack", __state); }
    private static void BeforeRedo(object __instance, out long __state) { __state = CaptureToken(__instance, "_redoStack"); if(current != null && current.manager == __instance) current.inRedo = true; }
    private static void AfterRedo(object __instance, long __state)
    {
        if (current == null || current.manager != __instance) return;
        current.inRedo = false; current.InspectChrome("before token transfer");
        TransferToken(__instance, "_undoStack", __state);
        current.InspectChrome("after token transfer");
        var controller = Read(Find("Game.MapEditor.MapEditorController"), "Shared");
        var window = Read(controller, "_window");
        var refresh = window?.GetType().GetMethod("RefreshChrome", Flags);
        if (refresh == null) { current.Log("RefreshChrome unavailable"); return; }
        refresh.Invoke(window, null);
        current.InspectChrome("after RefreshChrome");
        current.pendingInspection = 2;
    }
    private void InspectChrome(string phase)
    {
        try
        {
            var controller = Read(Find("Game.MapEditor.MapEditorController"), "Shared");
            var window = Read(controller, "_window");
            var title = Read(Read(window, "Window"), "Title");
            var panel = Read(Read(window, "_chromePanel"), "_container") as Component;
            var labelType = Find("TMPro.TMP_Text");
            var buttonType = Find("UnityEngine.UI.Button");
            if (panel == null || labelType == null || buttonType == null)
            { Log("Chrome " + phase + " unavailable panel=" + (panel != null) + " label=" + (labelType != null) + " button=" + (buttonType != null)); return; }
            var buttons = panel.GetComponentsInChildren(labelType, true).Cast<Component>()
                .Where(x => x.gameObject.activeInHierarchy && (string)Read(x, "text") == "Save")
                .Select(x => x.GetComponentInParent(buttonType)).Where(x => x != null).Distinct().ToArray();
            if (buttons.Length != 1)
            { Log("Chrome " + phase + " dirty=" + Read(editor, "IsDirty") + " title=" + title + " saveMatches=" + buttons.Length); return; }
            var button = buttons[0];
            var isInteractable = button.GetType().GetMethod("IsInteractable", Flags);
            Log("Chrome " + phase + " dirty=" + Read(editor, "IsDirty") + " title=" + title + " saveMatches=1 interactable=" + Read(button, "interactable") + " isInteractable=" + isInteractable?.Invoke(button, null) + " active=" + button.gameObject.activeInHierarchy);
        }
        catch (Exception e) { Log("Chrome " + phase + " failed: " + e); }
    }
    private static long CaptureToken(object undoManager, string source)
    {
        if (current == null || current.manager != undoManager) return 0;
        var stack = Stack(undoManager, source);
        return stack.Length == 0 ? 0 : TokenFor(stack[0]);
    }
    private static void TransferToken(object undoManager, string destination, long token)
    {
        if (token == 0 || current == null || current.manager != undoManager) return;
        var stack = Stack(undoManager, destination);
        if (stack.Length == 0) { current.Log("Token transfer failed: destination empty"); return; }
        Tokens.Remove(stack[0]); Tokens.Add(stack[0], new Token { Value = token });
        current.Log("History token transferred " + token + " to " + destination + " " + current.Observed());
    }
    private void SetState(int x, int y)
    {
        var oldX = av; var oldY = bv;
        manager.GetType().GetMethod("RegisterUndo").Invoke(manager, new object[] { (Action)(() => SetState(oldX, oldY)), "Probe two-object operation" });
        av = x; bv = y;
        a.transform.localPosition = new Vector3(x, 0, 0); b.transform.localPosition = new Vector3(y, 1, 0);
        Log("Set state " + x + "," + y + " " + Observed());
    }
    private void Apply()
    {
        if (manager == null) return;
        if (a == null) { a = new GameObject("Probe state A"); b = new GameObject("Probe state B"); b.transform.localPosition = new Vector3(0, 1, 0); Log("Baseline " + Observed()); }
        SetState(av + 1, bv + 2);
    }
    private void LockNative()
    {
        var path = (string)Read(editor, "_mapDataPath");
        nativeLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        Log("Native map locked");
    }
    private void OnGUI() { rect = GUI.Window(0x43523435, rect, Window, "Native Save and Undo probe v4"); }
    private void Window(int id)
    {
        GUILayout.Label(message);
        GUILayout.Label("State: " + av + ", " + bv + " Dirty: " + Read(editor, "IsDirty") + " Count: " + Read(manager, "ChangeCount"));
        GUILayout.Label(Observed());
        GUILayout.Label("Undo: " + Read(manager, "UndoActionName") + " Redo: " + Read(manager, "RedoActionName"));
        if (GUILayout.Button("Apply two-object operation")) try { Apply(); } catch (Exception e) { Log("Apply failed: " + e); }
        if (GUILayout.Button("Toggle custom save failure (" + failCustom + ")")) failCustom = !failCustom;
        if (GUILayout.Button("Lock native map")) try { LockNative(); } catch (Exception e) { Log("Lock failed: " + e); }
        if (GUILayout.Button("Release native lock")) { nativeLock?.Dispose(); nativeLock = null; Log("Native lock released"); }
        if (GUILayout.Button("Record snapshot")) Log("Snapshot state=" + av + "," + bv + " " + Observed() + " dirty=" + Read(editor, "IsDirty") + " count=" + Read(manager, "ChangeCount") + " undo=" + Read(manager, "UndoActionName") + " redo=" + Read(manager, "RedoActionName"));
        GUI.DragWindow();
    }
    private void OnDestroy() { nativeLock?.Dispose(); if (a != null) Destroy(a); if (b != null) Destroy(b); current = null; }
}
