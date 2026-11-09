// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Railroader.Modding;
using UnityEngine;

public sealed class Entry : IModEntryPoint, IWorldLifecycle
{
    private IModContext context; private GameObject host; private Harmony harmony;
    public void OnModLoad(IModContext value) { context = value; harmony = new Harmony("crankyant.native-save-undo-probe"); }
    public void OnWorldLoad() { host = new GameObject("Native save undo probe"); host.AddComponent<Probe>().Initialize(context.ModDirectory, harmony); }
    public void OnWorldUnload() { if (host != null) UnityEngine.Object.Destroy(host); host = null; }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); harmony.UnpatchAll("crankyant.native-save-undo-probe"); }
}
public sealed class Probe : MonoBehaviour
{
    private static Probe current; private string directory; private object editor, manager; private GameObject a,b; private int av,bv; private bool failCustom; private FileStream nativeLock; private string message="Waiting for editor"; private Rect rect=new Rect(20,70,400,390);
    private const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static Type Find(string n) => AppDomain.CurrentDomain.GetAssemblies().Select(x=>x.GetType(n,false)).FirstOrDefault(x=>x!=null);
    private static object Read(object o,string n) { if(o==null)return null; var t=o as Type ?? o.GetType(); var x=o is Type ? null:o; return t.GetProperty(n,flags)?.GetValue(x,null) ?? t.GetField(n,flags)?.GetValue(x); }
    private void Log(string s) { message=s; File.AppendAllText(Path.Combine(directory,"Probe.log"),DateTime.UtcNow.ToString("O")+" "+s+Environment.NewLine); }
    public void Initialize(string d,Harmony h) { directory=d; current=this; var t=Find("Game.MapEditor.MapEditor"); h.Patch(t.GetMethod("Save",flags),prefix:new HarmonyMethod(typeof(Probe).GetMethod(nameof(BeforeSave),flags))); Log("Save prefix installed"); }
    private static void BeforeSave(object __instance) { if(current==null || current.editor!=__instance)return; current.Log("Save prefix failCustom="+current.failCustom+" state="+current.av+","+current.bv); if(current.failCustom) throw new IOException("Controlled custom write failure"); File.WriteAllText(Path.Combine(current.directory,"authored-test.txt"),current.av+","+current.bv); current.Log("Custom write complete"); }
    private void Update() { var c=Read(Find("Game.MapEditor.MapEditorController"),"Shared"); var e=Read(c,"MapEditor"); if(e!=null && editor!=e) { editor=e; manager=Read(e,"UndoManager"); Log("Editor attached"); } }
    private void SetState(int x,int y) { var oldX=av; var oldY=bv; manager.GetType().GetMethod("RegisterUndo").Invoke(manager,new object[]{(Action)(()=>SetState(oldX,oldY)),"Probe two-object operation"}); av=x;bv=y; if(a!=null)a.transform.localPosition=new Vector3(x,0,0); if(b!=null)b.transform.localPosition=new Vector3(y,1,0); Log("Set state "+x+","+y); }
    private void Apply() { if(manager==null)return; if(a==null){a=new GameObject("Probe state A"); b=new GameObject("Probe state B");} SetState(av+1,bv+2); }
    private void LockNative() { var path=(string)Read(editor,"_mapDataPath"); nativeLock=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None); Log("Native map locked"); }
    private void OnGUI() { rect=GUI.Window(0x43523434,rect,Window,"Native Save and Undo probe"); }
    private void Window(int id) { GUILayout.Label(message); GUILayout.Label("State: "+av+", "+bv+" Dirty: "+Read(editor,"IsDirty")+" Count: "+Read(manager,"ChangeCount")); GUILayout.Label("Undo: "+Read(manager,"UndoActionName")+" Redo: "+Read(manager,"RedoActionName")); if(GUILayout.Button("Apply two-object operation")) try { Apply(); } catch(Exception e){Log("Apply failed: "+e);} if(GUILayout.Button("Toggle custom save failure ("+failCustom+")")) failCustom=!failCustom; if(GUILayout.Button("Lock native map")) try { LockNative(); } catch(Exception e){Log("Lock failed: "+e);} if(GUILayout.Button("Release native lock")) { nativeLock?.Dispose(); nativeLock=null; Log("Native lock released"); } if(GUILayout.Button("Record snapshot")) Log("Snapshot state="+av+","+bv+" dirty="+Read(editor,"IsDirty")+" count="+Read(manager,"ChangeCount")+" undo="+Read(manager,"UndoActionName")+" redo="+Read(manager,"RedoActionName")); GUI.DragWindow(); }
    private void OnDestroy(){ nativeLock?.Dispose(); if(a!=null)Destroy(a); if(b!=null)Destroy(b); current=null; }
}
