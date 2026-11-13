// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The editor's own undo manager, read through the internal members this capability checks.
    internal sealed class NativeHistory : INativeHistory {
        readonly object manager;
        readonly FieldInfo undo, redo;
        readonly MethodInfo register;
        internal NativeHistory(object manager) {
            this.manager = manager;
            var type = manager.GetType();
            undo = type.GetField("_undoStack", Native.Flags); redo = type.GetField("_redoStack", Native.Flags);
            register = type.GetMethod("RegisterUndo", new[] { typeof(Action), typeof(string) });
            if (undo == null || redo == null || register == null || !(undo.GetValue(manager) is IEnumerable) || !(redo.GetValue(manager) is IEnumerable))
                throw new MissingMemberException("Native undo history members unavailable");
        }
        // The native stacks enumerate newest first.
        static IReadOnlyList<object> Oldest(object stack) { var items = ((IEnumerable)stack).Cast<object>().ToList(); items.Reverse(); return items; }
        public IReadOnlyList<object> UndoGroups => Oldest(undo.GetValue(manager));
        public IReadOnlyList<object> RedoGroups => Oldest(redo.GetValue(manager));
        public void Register(Action inverse, string name) { register.Invoke(manager, new object[] { inverse, name }); }
    }
    // One attached map editor: its session, history and files.
    internal sealed class Authoring : IDisposable {
        internal readonly object Editor, Manager;
        internal readonly AuthoringSession Session;
        internal readonly string PatchPath;
        internal string Fault;
        internal Action<string> Log = _ => { };
        internal string Outstanding() { var list = Session.Outstanding(NativeDirty); return list.Count == 0 ? "none" : String.Join(", ", list); }
        readonly EventInfo changed;
        readonly Action handler;
        internal Authoring(PatchEngine engine, object editor) {
            Editor = editor; Manager = Native.Read(editor, "UndoManager");
            if (Manager == null) throw new MissingMemberException("Native undo manager unavailable");
            Native.Read(editor, "_cleanChangeCount"); Native.Read(editor, "_upgradedOnLoad"); Native.Read(Manager, "ChangeCount");
            var storage = new ActiveStorage(); PatchPath = storage.PatchPath;
            Session = new AuthoringSession(engine, new NativeHistory(Manager), storage, PackDocuments.ActiveId, PackDocuments.ModId, PackDocuments.ActivePackId());
            changed = Manager.GetType().GetEvent("DidChange", Native.Flags);
            if (changed == null || changed.EventHandlerType != typeof(Action)) throw new MissingMemberException("Native history notification unavailable");
            handler = Session.HistoryChanged;
            changed.AddEventHandler(Manager, handler);
        }
        // The editor's own change-count answer, without the patcher's accounting.
        internal bool NativeDirty => (bool)Native.Read(Editor, "_upgradedOnLoad") || Convert.ToInt32(Native.Read(Manager, "ChangeCount")) != Convert.ToInt32(Native.Read(Editor, "_cleanChangeCount"));
        // The native title and Save button read the dirty state when they are built; rebuild them after the accounting changed.
        internal void RefreshChrome() {
            try {
                var controller = Native.Read(Native.Type("Game.MapEditor.MapEditorController"), "Shared");
                var window = controller as UnityEngine.Object == null ? null : Native.Read(controller, "_window");
                if (window as UnityEngine.Object != null) Native.Call(window, "RefreshChrome");
            } catch (Exception e) { Fault = "Editor title and Save button could not be refreshed: " + e.GetBaseException().Message; }
        }
        public void Dispose() { changed.RemoveEventHandler(Manager, handler); }
    }
    // Extends the editor's Save, dirty state, Undo/Redo and exit confirmation. Installed once per process.
    internal static class Hooks {
        const string Id = "crankyant.scene-patcher";
        static Harmony harmony;
        internal static Authoring Current;
        // Why Save and history integration is unavailable, or null. Authoring stays disabled without it.
        internal static string Error { get; private set; }
        internal static void Install() {
            if (harmony != null || Error != null) return;
            var patcher = new Harmony(Id);
            try {
                var editor = Native.Type("Game.MapEditor.MapEditor"); var undo = Native.Type("Core.UndoManager"); var window = Native.Type("Game.MapEditor.MapEditorWindow");
                var save = editor?.GetMethod("Save", Native.Flags, null, Type.EmptyTypes, null);
                var dirty = editor?.GetProperty("IsDirty", Native.Flags)?.GetGetMethod(true);
                var undoMethod = undo?.GetMethod("Undo", Native.Flags, null, Type.EmptyTypes, null);
                var redoMethod = undo?.GetMethod("Redo", Native.Flags, null, Type.EmptyTypes, null);
                var exit = window?.GetMethod("ConfirmExitEditor", Native.Flags, null, Type.EmptyTypes, null);
                if (save == null || dirty == null || undoMethod == null || redoMethod == null || exit == null || window.GetMethod("RefreshChrome", Native.Flags) == null) throw new MissingMethodException("Native editor Save, history or exit functions unavailable");
                PresentMethod();
                patcher.Patch(save, prefix: Method(nameof(BeforeSave)), postfix: Method(nameof(AfterSave)), finalizer: Method(nameof(SaveFinalizer)));
                patcher.Patch(dirty, postfix: Method(nameof(AfterIsDirty)));
                patcher.Patch(undoMethod, prefix: Method(nameof(BeforeHistory)), finalizer: Method(nameof(HistoryFinalizer)));
                patcher.Patch(redoMethod, prefix: Method(nameof(BeforeHistory)), finalizer: Method(nameof(HistoryFinalizer)));
                patcher.Patch(exit, prefix: Method(nameof(BeforeConfirmExit)));
                harmony = patcher;
            } catch (Exception e) {
                try { patcher.UnpatchAll(Id); } catch { }
                Error = "Editor Save and history integration unavailable on this build: " + e.GetBaseException().Message;
            }
        }
        internal static void Remove() {
            Current = null;
            if (harmony != null) harmony.UnpatchAll(Id);
            harmony = null;
        }
        static HarmonyMethod Method(string name) => new HarmonyMethod(typeof(Hooks).GetMethod(name, Native.Flags));
        static Authoring For(object editor) { var current = Current; return current != null && ReferenceEquals(current.Editor, editor) ? current : null; }
        static Authoring ForManager(object manager) { var current = Current; return current != null && ReferenceEquals(current.Manager, manager) ? current : null; }
        // Runs before the native map write. A failing patcher write aborts the Save and reaches the native Save Failed alert.
        static void BeforeSave(object __instance) { For(__instance)?.Session.BeginSave(); }
        static void AfterSave(object __instance) {
            var current = For(__instance);
            if (current == null) return;
            current.Session.CompleteNativeSave();
            current.Log("Save completed: " + String.Join(", ", current.Session.LastSave.Completed) + "; outstanding: " + current.Outstanding());
        }
        static Exception SaveFinalizer(object __instance, Exception __exception) {
            var current = For(__instance);
            if (__exception == null || current == null) return __exception;
            try {
                var failure = new IOException(current.Session.FailSave(__exception), __exception);
                current.Log("Save failed: " + failure.Message);
                return failure;
            } catch { return __exception; }
        }
        static void AfterIsDirty(object __instance, ref bool __result) {
            var current = For(__instance);
            if (current == null) return;
            try { __result = current.Session.IsDirty(__result); }
            catch (Exception e) { __result = true; current.Fault = "Outstanding work could not be determined; the editor is treated as unsaved: " + e.GetBaseException().Message; }
        }
        static void BeforeHistory(object __instance) { ForManager(__instance)?.Session.BeginHistoryExecution(); }
        static Exception HistoryFinalizer(object __instance, Exception __exception) {
            var current = ForManager(__instance);
            if (current != null) {
                current.Session.EndHistoryExecution(__exception);
                // Native Undo and Redo rebuild the chrome before the history identity is settled here.
                current.RefreshChrome();
                try { current.Log("Native history executed" + (__exception == null ? "" : " with error " + __exception.GetBaseException().Message) + "; outstanding: " + current.Outstanding() + (current.Session.IncompleteOperation == null ? "" : "; incomplete: " + current.Session.IncompleteOperation)); }
                catch (Exception e) { current.Fault = e.GetBaseException().Message; }
            }
            return __exception;
        }
        static MethodInfo PresentMethod() {
            var alerts = Native.Type("UI.Common.ModalAlertController");
            var present = alerts?.GetMethods(BindingFlags.Public | BindingFlags.Static).SingleOrDefault(m => m.Name == "Present" && m.IsGenericMethodDefinition && m.GetParameters().Length == 4 && m.GetParameters()[1].ParameterType == typeof(string));
            var mode = Native.Type("UI.CarEditor.DefinitionEditorModeController");
            if (present == null || mode?.GetProperty("IsEditing", Native.Flags) == null || mode.GetMethod("ExitEditorMode", Native.Flags, null, Type.EmptyTypes, null) == null) throw new MissingMethodException("Native exit confirmation unavailable");
            return present.MakeGenericMethod(typeof(int));
        }
        // Replaces the native exit confirmation only while work is outstanding, with the same Cancel and Exit choices.
        static bool BeforeConfirmExit() {
            var current = Current;
            if (current == null) return true;
            try {
                var warning = current.Session.ExitWarning(current.NativeDirty);
                if (warning == null) return true;
                Action<int> choose = choice => {
                    var mode = Native.Type("UI.CarEditor.DefinitionEditorModeController");
                    if (choice == 1 && (bool)Native.Read(mode, "IsEditing")) Native.Call(mode, "ExitEditorMode");
                };
                PresentMethod().Invoke(null, new object[] { "Exit Map Editor", "Exit the map editor? " + warning + " Exit discards it; Cancel returns to the editor.", new[] { (0, "Cancel"), (1, "Exit") }, choose });
                return false;
            } catch (Exception e) { current.Fault = "Exit confirmation fell back to the native text: " + e.GetBaseException().Message; return true; }
        }
    }
}
